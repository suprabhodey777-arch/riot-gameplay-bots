/**
 * Drill generators: one per skill. A drill is a DrillSpec (the shared shape the
 * bot, coach and 3D client read) and `drillSimOptions` turns it into the sim
 * options that start it. Deterministic from (skill, difficulty, map, seed).
 *
 * Difficulty 0-1 changes how many bots there are, how fast and accurate they
 * are (reaction 450 ms down to 200 ms), and how much time the player has.
 *
 * The archetype traits here are only plausible numbers so every spec carries a
 * valid BotTraits; the bot lane owns the canonical archetype tables.
 */
import type { MapDef, SiteId, Team, Vec2 } from '../shared/contract.ts';
import type { BotTraits, DrillBot, DrillSpec, Skill, WeaknessFinding } from '../shared/coaching.ts';
import { loadMap } from '../shared/maps/index.ts';
import { AGENT_RADIUS, closestOnSegment, pointInPolygon, siteIds, sitePoly } from '../sim/geometry.ts';
import { createRng, type Rng } from '../sim/rng.ts';
import type { CreateSimOptions } from '../sim/sim.ts';
import { bbox, centroid, dist } from './geo.ts';

export const PLAYER_ID = 'player';
/** Seeds from here up are the held-out exam; the planner only uses seeds below it. */
export const EXAM_SEED_BASE = 9000;

/**
 * Positions snap to a 0.1 m grid. Exam positions are shifted 0.05 m off it, so an
 * exam spawn can never equal a practice spawn, whatever the seed.
 */
let snapOffset = 0;
const snap = (v: number): number => Math.round(v * 10) / 10 + snapOffset;

const clamp = (v: number, lo = 0, hi = 1): number => Math.min(hi, Math.max(lo, v));
const lerp = (a: number, b: number, t: number): number => a + (b - a) * t;

export type ArchetypeId = 'entry-duelist' | 'patient-lurker' | 'off-angle-anchor' | 'utility-controller' | 'baseline';

/** Placeholder personality, before difficulty is applied. */
const STYLE: Record<ArchetypeId, Pick<BotTraits, 'aggression' | 'offAngle' | 'patience' | 'utility'>> = {
  'entry-duelist': { aggression: 0.9, offAngle: 0.2, patience: 0.2, utility: 0.3 },
  'patient-lurker': { aggression: 0.3, offAngle: 0.5, patience: 0.9, utility: 0.4 },
  'off-angle-anchor': { aggression: 0.2, offAngle: 0.9, patience: 0.6, utility: 0.3 },
  'utility-controller': { aggression: 0.4, offAngle: 0.3, patience: 0.5, utility: 0.9 },
  baseline: { aggression: 0.5, offAngle: 0.5, patience: 0.5, utility: 0.5 },
};

/** Traits for an archetype at a difficulty: reaction 450 -> 200 ms, accuracy up. */
export function traitsFor(archetype: ArchetypeId, difficulty: number): BotTraits {
  const d = clamp(difficulty);
  const style = STYLE[archetype];
  return {
    reactionMs: Math.round(lerp(450, 200, d)),
    headAccuracy: round2(lerp(0.08, 0.4, d)),
    bodyAccuracy: round2(lerp(0.45, 0.75, d)),
    aggression: round2(lerp(style.aggression * 0.8, Math.min(1, style.aggression + 0.1), d)),
    offAngle: style.offAngle,
    patience: style.patience,
    utility: round2(lerp(style.utility * 0.7, style.utility, d)),
  };
}
const round2 = (v: number): number => Math.round(v * 100) / 100;

/** True when a body at p would not overlap a wall or leave the map. */
function isFree(map: MapDef, p: Vec2): boolean {
  const m = AGENT_RADIUS + 0.5;
  if (p.x < m || p.y < m || p.x > map.bounds.width - m || p.y > map.bounds.height - m) return false;
  for (const w of map.walls) {
    if (pointInPolygon(p, w.points)) return false;
    for (let i = 0; i < w.points.length; i++) {
      if (closestOnSegment(p, w.points[i], w.points[(i + 1) % w.points.length]).dist < m) return false;
    }
  }
  return true;
}

/** `p` if free, else the first free point found in growing rings around it. */
function freeNear(map: MapDef, p: Vec2, rng: Rng): Vec2 {
  if (isFree(map, p)) return round(p);
  for (let r = 1; r < 30; r += 0.5) {
    for (let i = 0; i < 12; i++) {
      const a = rng.next() * Math.PI * 2;
      const q = { x: p.x + Math.cos(a) * r, y: p.y + Math.sin(a) * r };
      if (isFree(map, q)) return round(q);
    }
  }
  return round(p);
}
const round = (p: Vec2): Vec2 => ({ x: snap(p.x), y: snap(p.y) });

function inPoly(map: MapDef, poly: Vec2[], rng: Rng): Vec2 {
  const b = bbox(poly);
  for (let i = 0; i < 300; i++) {
    const p = { x: lerp(b.x0, b.x1, rng.next()), y: lerp(b.y0, b.y1, rng.next()) };
    if (pointInPolygon(p, poly) && isFree(map, p)) return round(p);
  }
  return round(centroid(poly));
}

function jitter(p: Vec2, r: number, rng: Rng): Vec2 {
  return { x: p.x + (rng.next() * 2 - 1) * r, y: p.y + (rng.next() * 2 - 1) * r };
}

export const bot = (
  n: number,
  team: Team,
  spawn: Vec2,
  archetype: ArchetypeId,
  difficulty: number,
): DrillBot => ({ id: `bot-${n}`, team, spawn, archetypeId: archetype, traits: traitsFor(archetype, difficulty) });

/** A practice-grid (0.1 m) walkable point at or near `p`; rehearsals use this, never the exam's offset grid. */
export function walkablePoint(map: MapDef, p: Vec2, seed: number): Vec2 {
  snapOffset = 0;
  return freeNear(map, p, createRng(seed >>> 0));
}

const secs = (s: number): number => Math.round(s * 10) / 10;

export function buildDrill(skill: Skill, difficultyIn: number, map: MapDef, seed: number): DrillSpec {
  const d = clamp(difficultyIn);
  snapOffset = seed >= EXAM_SEED_BASE ? 0.05 : 0;
  const rng = createRng((seed * 2654435761 + skill.length * 97 + skill.charCodeAt(0)) >>> 0);
  const siteList = siteIds(map);
  const siteName: SiteId = siteList[Math.floor(rng.next() * siteList.length)];
  const site = sitePoly(map, siteName);
  const siteC = centroid(site);
  const attackSpawns = map.spawns.attack;
  const defenseSpawns = map.spawns.defense;
  const id = `${skill}-${seed}-${Math.round(d * 100)}`;
  const base = { id, skill, mapId: map.id, seed, difficulty: d };

  if (skill === 'entry') {
    const n = d >= 0.5 ? 2 : 1;
    const spawn = freeNear(map, jitter(attackSpawns[rng.int(attackSpawns.length)], 1.5, rng), rng);
    const bots = [bot(1, 'defense', inPoly(map, site, rng), 'off-angle-anchor', d)];
    if (n > 1) bots.push(bot(2, 'defense', inPoly(map, site, rng), 'baseline', d));
    return {
      ...base,
      title: `Opening duel into ${siteName} site`,
      goal: `Push into ${siteName} site and win the first fight against ${n} defender${n > 1 ? 's' : ''}.`,
      player: { id: PLAYER_ID, team: 'attack', spawn, hp: 100, carryingSpike: true },
      bots,
      roundTimeLeftS: secs(100 - 20 * d),
      spikePlanted: null,
    };
  }

  if (skill === 'clutch') {
    const n = 1 + Math.round(2 * d);
    const left = secs(32 - 10 * d);
    const pos = inPoly(map, site, rng);
    const spawn = freeNear(map, jitter(pos, 3, rng), rng);
    const styles: ArchetypeId[] = ['patient-lurker', 'entry-duelist', 'baseline'];
    const bots = Array.from({ length: n }, (_, i) =>
      bot(i + 1, 'defense', freeNear(map, jitter(defenseSpawns[rng.int(defenseSpawns.length)], 2, rng), rng), styles[i], d),
    );
    return {
      ...base,
      title: `1v${n} post-plant on ${siteName}`,
      goal: `Your team is down. The spike is planted on ${siteName} with ${left} s left: stop ${n === 1 ? 'the defender' : `the ${n} defenders`} or run the clock out.`,
      player: { id: PLAYER_ID, team: 'attack', spawn, hp: Math.round(100 - 30 * d), carryingSpike: false },
      bots,
      roundTimeLeftS: left,
      spikePlanted: { site: siteName, pos, secondsLeft: left },
    };
  }

  if (skill === 'time_pressure') {
    if (rng.next() < 0.5) {
      const left = secs(30 - 12 * d);
      const spawn = freeNear(map, jitter(attackSpawns[rng.int(attackSpawns.length)], 1.5, rng), rng);
      const bots = [bot(1, 'defense', inPoly(map, site, rng), 'off-angle-anchor', d)];
      if (d > 0.5) bots.push(bot(2, 'defense', inPoly(map, site, rng), 'baseline', d));
      return {
        ...base,
        title: `Late plant on ${siteName}`,
        goal: `Only ${left} s are left. Get to ${siteName} site and plant the spike before time runs out.`,
        player: { id: PLAYER_ID, team: 'attack', spawn, hp: 100, carryingSpike: true },
        bots,
        roundTimeLeftS: left,
        spikePlanted: null,
      };
    }
    const left = secs(18 - 7 * d);
    const pos = inPoly(map, site, rng);
    const spawn = freeNear(map, jitter(defenseSpawns[rng.int(defenseSpawns.length)], 1.5, rng), rng);
    const bots = [bot(1, 'attack', freeNear(map, jitter(pos, 6, rng), rng), 'patient-lurker', d)];
    if (d > 0.5) bots.push(bot(2, 'attack', freeNear(map, jitter(pos, 6, rng), rng), 'entry-duelist', d));
    return {
      ...base,
      title: `Late defuse on ${siteName}`,
      goal: `The spike is down on ${siteName} with ${left} s on it. Get there and finish the defuse before it blows.`,
      player: { id: PLAYER_ID, team: 'defense', spawn, hp: 100, carryingSpike: false },
      bots,
      roundTimeLeftS: left,
      spikePlanted: { site: siteName, pos, secondsLeft: left },
    };
  }

  // smoke_usage: a choke in front of the site is watched by a bot; the player starts well back.
  const awayC = centroid(attackSpawns);
  const len = dist(awayC, siteC) || 1;
  const dir = { x: (awayC.x - siteC.x) / len, y: (awayC.y - siteC.y) / len };
  const at = (k: number): Vec2 => ({ x: siteC.x + dir.x * k, y: siteC.y + dir.y * k });
  const spawn = freeNear(map, jitter(at(Math.min(34, len - 2)), 1.5, rng), rng);
  const bots = [bot(1, 'defense', freeNear(map, jitter(at(4), 1, rng), rng), 'off-angle-anchor', d)];
  if (d >= 0.5) bots.push(bot(2, 'defense', freeNear(map, jitter(siteC, 3, rng), rng), 'utility-controller', d));
  return {
    ...base,
    title: `Smoke the choke into ${siteName}`,
    goal: `A defender watches the way into ${siteName} site. Throw a smoke on it before you push, then plant.`,
    player: { id: PLAYER_ID, team: 'attack', spawn, hp: 100, carryingSpike: true },
    bots,
    roundTimeLeftS: secs(60 - 15 * d),
    spikePlanted: null,
  };
}

/** The drill for a finding at a difficulty. */
export function drillFor(finding: WeaknessFinding, difficulty: number, map: MapDef, seed: number): DrillSpec {
  return buildDrill(finding.skill, difficulty, map, seed);
}

/** The sim options that start a drill (uses the sim's `scenario`). */
export function drillSimOptions(spec: DrillSpec): CreateSimOptions {
  const map = loadMap(spec.mapId);
  const agents: CreateSimOptions['agents'] = [
    { id: spec.player.id, team: spec.player.team, isBot: false },
    ...spec.bots.map((b) => ({ id: b.id, team: b.team, isBot: true })),
  ];
  const scenarioAgents: NonNullable<NonNullable<CreateSimOptions['scenario']>['agents']> = {
    [spec.player.id]: { spawn: spec.player.spawn, hp: spec.player.hp, carryingSpike: spec.player.carryingSpike },
  };
  for (const b of spec.bots) scenarioAgents[b.id] = { spawn: b.spawn };
  return {
    map,
    seed: spec.seed,
    agents,
    scenario: { agents: scenarioAgents, roundTimeLeftS: spec.roundTimeLeftS, spikePlanted: spec.spikePlanted },
  };
}

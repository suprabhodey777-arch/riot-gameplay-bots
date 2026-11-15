import { isPersona, PERSONA_WEAPONS } from '../shared/personas.ts';
import { AGENTS, agentForArchetype, type AgentId } from '../shared/contract.ts';
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
import { pathBetween, minWallDist } from '../bot/nav.ts';
import { RUN_SPEED } from '../sim/sim.ts';
import { AGENT_RADIUS, closestOnSegment, lineOfSight, pointInPolygon, siteIds, sitePoly } from '../sim/geometry.ts';
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

/**
 * Extra seconds a clock needs on a map bigger than the one the numbers were tuned on:
 * the walking time beyond `freeM` metres. Zero on the small test map.
 */
function longWalkS(map: MapDef, from: Vec2, to: Vec2, freeM: number): number {
  const path = pathBetween(map, from, to);
  if (!path) return 0;
  let len = 0;
  let at = from;
  for (const p of path) {
    len += dist(at, p);
    at = p;
  }
  return Math.max(0, Math.round(((len - freeM) / RUN_SPEED) * 10) / 10);
}

const secs = (s: number): number => Math.round(s * 10) / 10;

const yawTo = (from: Vec2, to: Vec2): number => Math.atan2(to.y - from.y, to.x - from.x);

/** The player starts at least this far (m) from every bot. */
export const START_CLEAR_M = 8;
/** The drill starts this far (m of path) short of the first point that can see the enemy. */
export const START_CONTACT_MIN_M = 7;
export const START_CONTACT_MAX_M = 12;
/** The band is narrowed inside [MIN, MAX] so snapping cannot push the final start outside it. */
const CONTACT_BAND_M = { min: START_CONTACT_MIN_M + 0.5, max: START_CONTACT_MAX_M - 0.5 };
/** Ground this far (m) ahead, along the start's facing, must be clear of walls. */
export const START_AHEAD_M = 2;
const START_AHEAD_CLEAR_M = 0.4;
/** Spacing of the sight and candidate samples along a route (m). */
const SIGHT_STEP_M = 0.5;
const START_STEP_M = 0.5;
/** The lead drills aim for: the middle of the band, about 1.4 s of running. */
export const START_TARGET_M = (START_CONTACT_MIN_M + START_CONTACT_MAX_M) / 2;

const inContactBand = (m: number): boolean =>
  m >= CONTACT_BAND_M.min - 1e-9 && m <= CONTACT_BAND_M.max + 1e-9;

/** The point `s` metres along a route. */
function pointAt(route: Route, s: number): Vec2 {
  for (let i = 1; i < route.pts.length; i++) {
    if (route.len[i] + 1e-9 >= s) {
      const seg = route.len[i] - route.len[i - 1] || 1;
      const k = (s - route.len[i - 1]) / seg;
      return { x: route.pts[i - 1].x + (route.pts[i].x - route.pts[i - 1].x) * k, y: route.pts[i - 1].y + (route.pts[i].y - route.pts[i - 1].y) * k };
    }
  }
  return { ...route.pts[route.pts.length - 1] };
}

/** A walkable route, and the path length walked to reach each point. */
export interface Route {
  pts: Vec2[];
  len: number[];
}

/** The path from `from` to `to` as points, with the running length to each (`from` first, `to` last). */
export function routeTo(map: MapDef, from: Vec2, to: Vec2): Route {
  const pts: Vec2[] = [{ ...from }];
  for (const p of pathBetween(map, from, to) ?? []) {
    if (dist(pts[pts.length - 1], p) > 1e-6) pts.push({ ...p });
  }
  if (dist(pts[pts.length - 1], to) > 1e-6) pts.push({ ...to });
  const len = [0];
  for (let i = 1; i < pts.length; i++) len.push(len[i - 1] + dist(pts[i - 1], pts[i]));
  return { pts, len };
}

/** The first point on the route that can see `target`, and the path length walked to reach it. */
export function firstSight(map: MapDef, route: Route, target: Vec2): { pos: Vec2; len: number } | null {
  if (lineOfSight(route.pts[0], target, map.walls)) return { pos: { ...route.pts[0] }, len: 0 };
  for (let i = 1; i < route.pts.length; i++) {
    const a = route.pts[i - 1];
    const b = route.pts[i];
    const seg = dist(a, b) || 1;
    for (let k = SIGHT_STEP_M; k < seg; k += SIGHT_STEP_M) {
      const q = { x: a.x + ((b.x - a.x) * k) / seg, y: a.y + ((b.y - a.y) * k) / seg };
      if (lineOfSight(q, target, map.walls)) return { pos: q, len: route.len[i - 1] + k };
    }
    if (lineOfSight(b, target, map.walls)) return { pos: { ...b }, len: route.len[i] };
  }
  return null;
}

/** Facing along a route from `at` metres in: toward the first waypoint far enough ahead to give a heading. */
function facingAlongRoute(route: Route, at: number, from: Vec2, to: Vec2): number {
  for (let i = 0; i < route.pts.length; i++) {
    if (route.len[i] > at + 0.75 && dist(from, route.pts[i]) > 0.75) return yawTo(from, route.pts[i]);
  }
  return yawTo(from, to);
}

/** True when the ground `START_AHEAD_M` ahead along `yaw` is clear of walls. */
export function facingClear(map: MapDef, from: Vec2, yaw: number): boolean {
  const ahead = { x: from.x + Math.cos(yaw) * START_AHEAD_M, y: from.y + Math.sin(yaw) * START_AHEAD_M };
  return minWallDist(ahead, map.walls) > START_AHEAD_CLEAR_M;
}

/** Which way to face to set off from `from` along the path to `to` (never into a wall if avoidable). */
export function facingToward(map: MapDef, from: Vec2, to: Vec2): number {
  const route = routeTo(map, from, to);
  const along = facingAlongRoute(route, 0, from, to);
  if (facingClear(map, from, along)) return along;
  const direct = yawTo(from, to);
  return facingClear(map, from, direct) ? direct : along;
}

/** Where a drill starts: the spot, its facing, and how far the first sight of an enemy still is. */
export interface ContactStart {
  spawn: Vec2;
  yaw: number;
  /** Path length (m) from the spawn to the point where an enemy first comes into view. */
  remainingM: number;
  contact: Vec2;
}

/**
 * Move a start that is already out of sight so the first sight of an enemy is about
 * START_TARGET_M metres of path away: on toward the fight along the route when the contact is
 * further off, back along clear ground when it is closer. The start keeps the caller's hiding
 * rule (`ok`) and faces on along the way in. Null when there is no such ground.
 */
export function backOffStart(
  map: MapDef,
  start: Vec2,
  targets: Vec2[],
  ok: (p: Vec2) => boolean,
  snap: (p: Vec2) => Vec2,
): ContactStart | null {
  const target = targets.reduce((best, t) => (dist(t, start) < dist(best, start) ? t : best));
  const route = routeTo(map, start, target);
  const sight = firstSight(map, route, target);
  if (!sight) return null;
  const attempt = (pos: Vec2, yawFor: (spawn: Vec2) => number): ContactStart | null => {
    const spawn = snap(pos);
    if (dist(spawn, pos) > 0.75 || !ok(spawn)) return null;
    const seen = firstSight(map, routeTo(map, spawn, target), target);
    if (!seen || !inContactBand(seen.len)) return null;
    const yaw = yawFor(spawn);
    if (!facingClear(map, spawn, yaw)) return null;
    return { spawn, yaw, remainingM: secs(seen.len), contact: seen.pos };
  };
  const lead = sight.len - START_TARGET_M;
  if (lead >= 0) {
    // On toward the fight: the contact and the ground between are already known.
    for (const off of [0, 0.5, -0.5, 1, -1, 1.5, -1.5, 2, -2]) {
      const at = Math.min(Math.max(lead + off, 0), route.len[route.len.length - 1]);
      const pos = pointAt(route, at);
      const got = attempt(pos, (spawn) => facingAlongRoute(route, at, spawn, sight.pos));
      if (got) return got;
    }
    return null;
  }
  // Closer to the contact than a couple of seconds: step back, away from the fight, on clear ground.
  const back = -lead;
  const away = Math.atan2(start.y - sight.pos.y, start.x - sight.pos.x);
  for (const turn of [0, 0.26, -0.26, 0.52, -0.52, 0.79, -0.79, 1.05, -1.05]) {
    let prev = start;
    for (let s = 0.5; s <= back + 2 + 1e-9; s += 0.5) {
      const a = away + turn;
      const q = { x: start.x + Math.cos(a) * s, y: start.y + Math.sin(a) * s };
      if (!isFree(map, q) || !lineOfSight(prev, q, map.walls)) break;
      prev = q;
      if (s < back - 2 - 1e-9) continue;
      const got = attempt(q, (spawn) => facingToward(map, spawn, sight.pos));
      if (got) return got;
    }
  }
  return null;
}

/**
 * A start a few steps from the fight: a point on `route` whose remaining path to the first
 * sight of an enemy is START_CONTACT_MIN_M..START_CONTACT_MAX_M metres, out of every
 * `targets`' sight (`ok`), facing on along the route. `maxLen` keeps the point on one leg of
 * the route (a rehearsal only starts on the player's own side of the moment).
 * Returns null when the route offers no such point.
 */
export function contactStart(
  map: MapDef,
  route: Route,
  targets: Vec2[],
  ok: (p: Vec2) => boolean,
  snap: (p: Vec2) => Vec2,
  maxLen = Infinity,
): ContactStart | null {
  const sight = firstSight(map, route, targets[0]);
  if (!sight) return null;
  const cands: { pos: Vec2; at: number; remaining: number }[] = [];
  const add = (pos: Vec2, at: number): void => {
    const remaining = sight.len - at;
    if (at > maxLen + 1e-9) return;
    if (remaining < CONTACT_BAND_M.min - 1e-9 || remaining > CONTACT_BAND_M.max + 1e-9) return;
    cands.push({ pos, at, remaining });
  };
  for (let i = 1; i < route.pts.length; i++) {
    const a = route.pts[i - 1];
    const b = route.pts[i];
    const seg = dist(a, b) || 1;
    for (let k = START_STEP_M; k < seg; k += START_STEP_M) {
      add({ x: a.x + ((b.x - a.x) * k) / seg, y: a.y + ((b.y - a.y) * k) / seg }, route.len[i - 1] + k);
    }
    add(b, route.len[i]);
  }
  // Furthest back first: the most ground to read the fight from, still inside the band.
  cands.sort((x, y) => y.remaining - x.remaining);
  for (const c of cands) {
    if (!ok(c.pos)) continue;
    const spawn = snap(c.pos);
    if (!ok(spawn)) continue;
    const target = targets.reduce((best, t) => (dist(t, spawn) < dist(best, spawn) ? t : best));
    const seen = firstSight(map, routeTo(map, spawn, target), target);
    const remaining = seen ? seen.len : Infinity;
    if (remaining < CONTACT_BAND_M.min - 1e-9 || remaining > CONTACT_BAND_M.max + 1e-9) continue;
    const yaw = facingAlongRoute(route, c.at, spawn, sight.pos);
    if (!facingClear(map, spawn, yaw)) continue;
    return { spawn, yaw, remainingM: secs(remaining), contact: seen?.pos ?? sight.pos };
  }
  return null;
}

function buildDrillBase(skill: Skill, difficultyIn: number, map: MapDef, seed: number): DrillSpec {
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
    const back = freeNear(map, jitter(attackSpawns[rng.int(attackSpawns.length)], 1.5, rng), rng);
    const bots = [bot(1, 'defense', inPoly(map, site, rng), 'off-angle-anchor', d)];
    if (n > 1) bots.push(bot(2, 'defense', inPoly(map, site, rng), 'baseline', d));
    // A couple of seconds before the fight: the last stretch where no defender can see the player.
    const nearest = bots.reduce((a, b) => (dist(b.spawn, back) < dist(a.spawn, back) ? b : a));
    const hidden = (p: Vec2): boolean =>
      bots.every((b) => dist(p, b.spawn) >= START_CLEAR_M && !lineOfSight(p, b.spawn, map.walls));
    const snapRng = createRng((seed * 31 + 17) >>> 0);
    const start = contactStart(map, routeTo(map, back, nearest.spawn), bots.map((b) => b.spawn), hidden, (p) => freeNear(map, p, snapRng));
    const spawn = start?.spawn ?? back;
    return {
      ...base,
      title: `Opening duel into ${siteName} site`,
      goal: `Push into ${siteName} site and win the first fight against ${n} defender${n > 1 ? 's' : ''}.`,
      player: { id: PLAYER_ID, team: 'attack', spawn, yaw: start?.yaw ?? facingToward(map, spawn, siteC), hp: 100, carryingSpike: true },
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
      player: { id: PLAYER_ID, team: 'attack', spawn, yaw: facingToward(map, spawn, pos), hp: Math.round(100 - 30 * d), carryingSpike: false },
      bots,
      roundTimeLeftS: left,
      spikePlanted: { site: siteName, pos, secondsLeft: left },
    };
  }

  if (skill === 'time_pressure') {
    if (rng.next() < 0.5) {
      const spawn = freeNear(map, jitter(attackSpawns[rng.int(attackSpawns.length)], 1.5, rng), rng);
      const left = secs(30 - 12 * d + longWalkS(map, spawn, siteC, 55));
      const bots = [bot(1, 'defense', inPoly(map, site, rng), 'off-angle-anchor', d)];
      if (d > 0.5) bots.push(bot(2, 'defense', inPoly(map, site, rng), 'baseline', d));
      return {
        ...base,
        title: `Late plant on ${siteName}`,
        goal: `Only ${left} s are left. Get to ${siteName} site and plant the spike before time runs out.`,
        player: { id: PLAYER_ID, team: 'attack', spawn, yaw: facingToward(map, spawn, siteC), hp: 100, carryingSpike: true },
        bots,
        roundTimeLeftS: left,
        spikePlanted: null,
      };
    }
    const pos = inPoly(map, site, rng);
    const spawn = freeNear(map, jitter(defenseSpawns[rng.int(defenseSpawns.length)], 1.5, rng), rng);
    const left = secs(18 - 7 * d + longWalkS(map, spawn, pos, 15));
    const bots = [bot(1, 'attack', freeNear(map, jitter(pos, 6, rng), rng), 'patient-lurker', d)];
    if (d > 0.5) bots.push(bot(2, 'attack', freeNear(map, jitter(pos, 6, rng), rng), 'entry-duelist', d));
    return {
      ...base,
      title: `Late defuse on ${siteName}`,
      goal: `The spike is down on ${siteName} with ${left} s on it. Get there and finish the defuse before it blows.`,
      player: { id: PLAYER_ID, team: 'defense', spawn, yaw: facingToward(map, spawn, pos), hp: 100, carryingSpike: false },
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
    player: { id: PLAYER_ID, team: 'attack', spawn, yaw: facingToward(map, spawn, siteC), hp: 100, carryingSpike: true },
    bots,
    roundTimeLeftS: secs(60 - 15 * d),
    spikePlanted: null,
  };
}

/** Apply a player selection; smoke drills always supply a usable smoke. */
export function withPlayerAgent(spec: DrillSpec, selected: AgentId = spec.player.agent ?? 'rook'): DrillSpec {
  const agent = spec.skill === 'smoke_usage' && !AGENTS[selected].kit.smoke ? 'haze' : selected;
  return { ...spec, player: { ...spec.player, agent } };
}
export function buildDrill(...args: Parameters<typeof buildDrillBase>): DrillSpec {
  return withPlayerAgent(buildDrillBase(...args));
}

/** The drill for a finding at a difficulty. */
export function drillFor(finding: WeaknessFinding, difficulty: number, map: MapDef, seed: number): DrillSpec {
  return buildDrill(finding.skill, difficulty, map, seed);
}

/** The sim options that start a drill (uses the sim's `scenario`). */
export function drillSimOptions(spec: DrillSpec): CreateSimOptions {
  const map = loadMap(spec.mapId);
  const agents: CreateSimOptions['agents'] = [
    { id: spec.player.id, team: spec.player.team, isBot: false, loadout: spec.loadout, agent: withPlayerAgent(spec).player.agent },
    ...spec.bots.map((b) => ({ id: b.id, team: b.team, isBot: true, loadout: isPersona(b.archetypeId) ? { primary: spec.eco && PERSONA_WEAPONS[b.archetypeId].includes('hammer') ? 'hammer' as const : PERSONA_WEAPONS[b.archetypeId][0], secondary: 'hammer' as const, armor: null, credits: 0 } : spec.eco ? undefined : spec.loadout, agent: agentForArchetype(b.archetypeId) })),
  ];
  const scenarioAgents: NonNullable<NonNullable<CreateSimOptions['scenario']>['agents']> = {
    [spec.player.id]: { spawn: spec.player.spawn, hp: spec.player.hp, carryingSpike: spec.player.carryingSpike, yaw: spec.player.yaw },
  };
  for (const b of spec.bots) scenarioAgents[b.id] = { spawn: b.spawn };
  return {
    map,
    seed: spec.seed,
    agents,
    scenario: { agents: scenarioAgents, roundTimeLeftS: spec.roundTimeLeftS, spikePlanted: spec.spikePlanted },
  };
}

/** Same skill and scenario, practising a cheap sidearm against rifles. */
export function ecoDrill(spec: DrillSpec): DrillSpec {
  return { ...spec, id: `eco-${spec.id}`, eco: true, title: `Eco round: ${spec.title}`, goal: `Win with a sidearm against rifles. ${spec.goal}`, loadout: { primary: null, secondary: spec.seed % 2 ? 'pin' : 'hammer', armor: null, credits: 800 } };
}

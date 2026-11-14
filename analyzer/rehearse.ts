/**
 * Rehearse my mistakes: rebuild the moment a player failed as a drill, so they can
 * play it again. Same side, same positions (snapped to walkable ground), the same
 * number of enemies standing where they were and facing the player, the same clock
 * and spike state. The enemy that beat the player gets the archetype that matches
 * how they won (an off-angle if they were tucked away, a rush if they came at the
 * player), the rest play the baseline.
 *
 * Positions come from `WeaknessEvidence.situation`; see `situation.ts` for which of
 * them are exact and which are approximations. Without a situation (an old log) the
 * evidence is rehearsed as an ordinary drill for the skill.
 */
import type { MapDef, Team, Vec2 } from '../shared/contract.ts';
import type { DrillBot, DrillSpec, Situation, WeaknessEvidence, WeaknessFinding } from '../shared/coaching.ts';
import { minWallDist, spotsFor } from '../bot/nav.ts';

import { bot, type ArchetypeId, buildDrill, EXAM_SEED_BASE, PLAYER_ID, walkablePoint } from './drills.ts';
import { calloutAt, centroid, dist } from './geo.ts';
import { siteIds, sitePoly, lineOfSight } from '../sim/geometry.ts';
import { DEFUSE_TIME_S, DETONATION_S, PLANT_TIME_S, RUN_SPEED } from '../sim/sim.ts';
import { navFor, pathBetween } from '../bot/nav.ts';

/** Difficulty of the rebuilt bots: the player lost to them once, so they are a fair middle. */
export const REHEARSAL_DIFFICULTY = 0.5;

const yawTo = (from: Vec2, to: Vec2): number => Math.atan2(to.y - from.y, to.x - from.x);
const clampN = (v: number, lo: number, hi: number): number => Math.min(hi, Math.max(lo, v));


/** The player starts at least this far from every enemy, and this long (s) before the contact if the ground allows. */
export const MIN_START_DIST_M = 8;
export const MIN_LEAD_S = 3;
/** Extra margin (s) on top of walk + plant/defuse for a time-pressure rehearsal. */
export const TIME_MARGIN_S = 3;

const unseen = (map: MapDef, p: Vec2, enemies: Vec2[], minDist: number): boolean =>
  enemies.every((e) => dist(p, e) >= minDist && !lineOfSight(p, e, map.walls));

/** Length of the walkable route between two points (straight line when none is found). */
function routeLength(map: MapDef, from: Vec2, to: Vec2): number {
  const path = pathBetween(map, from, to);
  if (!path) return dist(from, to);
  let len = 0;
  let at = from;
  for (const p of path) {
    len += dist(at, p);
    at = p;
  }
  return len;
}

/**
 * Where the player starts the approach: walk back from where the moment happened toward the
 * side's spawn along the nav graph and take the first point (at least MIN_LEAD_S of running
 * back if one is out of sight, else the first out of sight at all) that no enemy can see and
 * that is at least `minDist` from all of them. When the whole route to the spawn is watched,
 * the nearest nav node that is out of sight is used instead.
 */
export function approachStart(map: MapDef, moment: Vec2, enemies: Vec2[], side: Team, minDist = MIN_START_DIST_M): { pos: Vec2; walkS: number } {
  const lead = MIN_LEAD_S * RUN_SPEED;
  let first: { pos: Vec2; d: number } | null = null;
  let leadPick: { pos: Vec2; d: number } | null = null;
  for (const spawn of map.spawns[side]) {
    const path = pathBetween(map, moment, spawn) ?? [spawn];
    let d = 0;
    let at = moment;
    let f0: { pos: Vec2; d: number } | null = null;
    let l0: { pos: Vec2; d: number } | null = null;
    for (const wp of path) {
      const seg = dist(at, wp);
      for (let k = 0.5; k <= seg + 1e-9 && !l0; k += 0.5) {
        // Test the point as it will really stand: snapped onto ground with room around it.
        const p = walkablePoint(map, { x: at.x + ((wp.x - at.x) * k) / seg, y: at.y + ((wp.y - at.y) * k) / seg }, 5);
        if (!unseen(map, p, enemies, minDist)) continue;
        f0 ??= { pos: p, d: d + k };
        if (d + k >= lead) l0 = { pos: p, d: d + k };
      }
      d += seg;
      at = wp;
      if (l0) break;
    }
    if (f0 && (!first || f0.d < first.d)) first = f0;
    if (l0 && (!leadPick || l0.d < leadPick.d)) leadPick = l0;
  }
  let pick = leadPick ?? first;
  if (!pick) {
    // Every route is watched: the nearest nav node that is out of sight once snapped.
    const g = navFor(map);
    const byDist = g.nodes.map((n) => ({ n, d: dist(n, moment) })).sort((a, b) => a.d - b.d);
    for (const { n } of byDist) {
      const p = walkablePoint(map, n, 5);
      if (unseen(map, p, enemies, minDist)) {
        pick = { pos: p, d: 0 };
        break;
      }
    }
  }
  const pos = pick ? pick.pos : walkablePoint(map, moment, 5);
  return { pos, walkS: Math.round((routeLength(map, pos, moment) / RUN_SPEED) * 10) / 10 };
}

/** What kind of opponent the enemy at `pos` was, judged from where it stood. */
export function archetypeFor(map: MapDef, s: Situation, pos: Vec2, skill: WeaknessFinding['skill']): ArchetypeId {
  if (skill === 'smoke_usage') return 'utility-controller';
  if (s.side === 'attack') {
    // The enemies hold. Tucked against a wall, or on a spot the common ones skip, is an off-angle.
    const tucked = minWallDist(pos, map.walls) < 1.3;
    const spots = s.site ? spotsFor(map, s.site, 'attack') : [];
    const near = spots.filter((p) => dist(p.pos, pos) <= 3).sort((a, b) => dist(a.pos, pos) - dist(b.pos, pos))[0];
    return tucked || (near && near.unusual > near.common) ? 'off-angle-anchor' : 'baseline';
  }
  // The enemies attack: guarding a planted spike is patient, otherwise they rush.
  return s.spikePlanted ? 'patient-lurker' : 'entry-duelist';
}

const LATER = 'Same angle, a few seconds earlier: take it differently.';

/** "Round 11: you lost the opening duel at A Pans 0.6 s after contact. Same angle, a few seconds earlier: take it differently." */
function goalText(evidence: WeaknessEvidence, finding: WeaknessFinding, s: Situation, map: MapDef, enemies: number, left: number): string {
  const round = `Round ${evidence.round + 1}`;
  const where = calloutAt(map, s.playerPos);
  const n = enemies === 1 ? '1 enemy' : `${enemies} enemies`;
  switch (finding.skill) {
    case 'entry': {
      const lead = /([\d.]+) s after first contact/.exec(evidence.note);
      return `${round}: you lost the opening duel at ${where}${lead ? ` ${lead[1]} s after contact` : ''}. ${LATER}`;
    }
    case 'clutch':
      return `${round}: you lost a 1v${enemies} clutch near ${where} with ${s.spikePlanted ? `${left} s on the spike` : `${left} s left`}. ${n} wait where they waited. ${LATER}`;
    case 'time_pressure':
      return s.side === 'attack'
        ? `${round}: the clock beat you at ${where}. ${evidence.note.charAt(0).toUpperCase()}${evidence.note.slice(1)}. Plant with ${left} s left this time. ${LATER}`
        : `${round}: the retake from ${where} ran out of time. Defuse before the ${left} s are up. ${LATER}`;
    default:
      return `${round}: your smoke went wrong near ${where} (${evidence.note}). Throw it on the choke before the push, from out of sight. ${LATER}`;
  }
}

export function rehearsalDrill(evidence: WeaknessEvidence, finding: WeaknessFinding, map: MapDef, seedIn: number): DrillSpec {
  // Rehearsals are practice: their seed stays below the exam's.
  const seed = ((Math.floor(seedIn) % EXAM_SEED_BASE) + EXAM_SEED_BASE) % EXAM_SEED_BASE || 1;
  const s = evidence.situation;
  if (!s) {
    const d = buildDrill(finding.skill, REHEARSAL_DIFFICULTY, map, seed);
    return { ...d, id: `rehearse-${d.id}`, goal: `Round ${evidence.round + 1}: ${evidence.note}. No positions were logged, so this is a fresh drill for the same skill.` };
  }

  const place = (p: Vec2, k: number): Vec2 => walkablePoint(map, p, seed * 31 + k);
  const moment = place(s.playerPos, 0);
  const wasPlanted = s.spikePlanted !== null;

  // The enemies stay where they beat the player; nobody starts on top of the moment.
  const enemyPos = s.enemies.map((e, i) => {
    const p = place(e.pos, i + 1);
    return dist(p, moment) < 2 ? place({ x: p.x + 3, y: p.y + 3 }, i + 40) : p;
  });
  if (enemyPos.length === 0) {
    const d = buildDrill(finding.skill, REHEARSAL_DIFFICULTY, map, seed);
    return { ...d, id: `rehearse-${d.id}` };
  }

  // The player starts back along the approach, out of every enemy's sight (a smoke needs more room to land).
  const start = approachStart(map, moment, enemyPos, s.side, finding.skill === 'smoke_usage' ? 12 : MIN_START_DIST_M);
  const spawn = start.pos;

  const enemyTeam = s.side === 'attack' ? 'defense' : 'attack';
  const bots: DrillBot[] = enemyPos.map((p, i) => {
    const arche: ArchetypeId = i === 0 ? archetypeFor(map, s, p, finding.skill) : 'baseline';
    return { ...bot(i + 1, enemyTeam, p, arche, REHEARSAL_DIFFICULTY), hold: { pos: p, yaw: yawTo(p, moment) } };
  });

  const planted = s.spikePlanted ? { site: s.spikePlanted.site, pos: place(s.spikePlanted.pos, 77) } : null;
  const ids = siteIds(map);
  const siteGoal = planted ? planted.pos : centroid(sitePoly(map, s.site && ids.includes(s.site) ? s.site : ids[0]));

  // The clock: the walk back is time the player spends again, so the moment of contact keeps its clock.
  let left: number;
  if (finding.skill === 'time_pressure') {
    // Keep the tight clock, but the plant or defuse must be reachable from the new start.
    const job = s.side === 'attack' ? PLANT_TIME_S : DEFUSE_TIME_S;
    const need = routeLength(map, spawn, siteGoal) / RUN_SPEED + job + TIME_MARGIN_S;
    const kept = planted ? clampN(s.spikePlanted?.secondsLeft ?? 0, 6, DETONATION_S) : clampN(s.roundTimeLeftS, 8, 100);
    left = Math.round(Math.max(kept, need) * 10) / 10;
  } else if (planted) {
    left = Math.round(clampN((s.spikePlanted?.secondsLeft ?? 0) + start.walkS, 6, DETONATION_S) * 10) / 10;
  } else {
    left = Math.round(clampN(s.roundTimeLeftS + start.walkS, 8, 100 + start.walkS) * 10) / 10;
  }

  const hasSpike = !wasPlanted && s.playerHasSpike;
  return {
    id: `rehearse-${finding.skill}-${evidence.matchId}-r${evidence.round}-${evidence.tick}`,
    skill: finding.skill,
    title: `Rehearsal: round ${evidence.round + 1}`,
    goal: goalText(evidence, finding, s, map, bots.length, left),
    mapId: map.id,
    seed,
    difficulty: REHEARSAL_DIFFICULTY,
    player: { id: PLAYER_ID, team: s.side, spawn, hp: 100, carryingSpike: hasSpike },
    bots,
    roundTimeLeftS: left,
    spikePlanted: planted ? { ...planted, secondsLeft: left } : null,
  };
}

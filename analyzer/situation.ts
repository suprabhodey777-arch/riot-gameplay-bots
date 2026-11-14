/**
 * Rebuilds "where was everyone" at a moment of a logged round, for rehearsal drills.
 *
 * Only damage and kill events carry positions, so each agent's position is the latest
 * one known at or before `posTick`. Anyone without one is placed near the centre of the
 * callout the action was in (`approxPos`), a few metres apart: an approximation, and the
 * drill builder snaps everything onto walkable ground anyway.
 */
import type { MapDef, MatchLog, SimEvent, SiteId, Vec2 } from '../shared/contract.ts';
import type { Situation } from '../shared/coaching.ts';
import { siteIds, sitePoly } from '../sim/geometry.ts';
import { DETONATION_S, ROUND_TIME_S, TICK_RATE } from '../sim/sim.ts';
import { centroid, dist } from './geo.ts';

type Round = MatchLog['rounds'][number];

export interface SituationOptions {
  /** Count kills at exactly `tick` as already happened (a clutch starts after the last ally dies). */
  inclusive?: boolean;
  /** Take positions from events up to this tick instead (a smoke is thrown before anyone is seen). */
  posTick?: number;
  /** Where to look for the action when the player's own position is unknown. */
  hint?: Vec2;
  /** Replace the clock: "a late plant" or "a tight retake" is rehearsed with this many seconds left. */
  secondsLeft?: number;
  /** Keep every living enemy (a clutch is 1vK) rather than only those with a known position. */
  allEnemies?: boolean;
}

const secs = (ticks: number): number => ticks / TICK_RATE;

/** The centre of the callout around `hint`, nudged by `i` so several agents do not stack. */
export function approxPos(map: MapDef, hint: Vec2 | null, i: number): Vec2 {
  const at = hint ?? { x: map.bounds.width / 2, y: map.bounds.height / 2 };
  let best: Vec2 = at;
  let bestD = Number.POSITIVE_INFINITY;
  for (const c of map.callouts) {
    const m = centroid(c.polygon);
    const d = dist(m, at);
    if (d < bestD) {
      bestD = d;
      best = m;
    }
  }
  const a = i * 2.4;
  return { x: best.x + Math.cos(a) * 2.5 * Math.ceil(i / 2), y: best.y + Math.sin(a) * 2.5 * Math.ceil(i / 2) };
}

export function situationAt(map: MapDef, r: Round, playerId: string, tick: number, opts: SituationOptions = {}): Situation {
  const events: SimEvent[] = [...r.events].sort((a, b) => a.tick - b.tick);
  const posTick = opts.posTick ?? tick;
  const last = new Map<string, Vec2>();
  const dead = new Set<string>();
  let plant: (SimEvent & { type: 'plant' }) | null = null;
  const killer = { id: null as string | null };
  for (const e of events) {
    if (e.tick <= posTick) {
      if (e.type === 'damage') {
        last.set(e.attacker, e.attackerPos);
        last.set(e.victim, e.victimPos);
      } else if (e.type === 'kill') {
        last.set(e.killer, e.killerPos);
        last.set(e.victim, e.victimPos);
      }
    }
    if (e.type === 'kill' && (opts.inclusive ? e.tick <= tick : e.tick < tick)) dead.add(e.victim);
    if (e.type === 'kill' && e.victim === playerId && e.tick === tick) killer.id = e.killer;
    if (e.type === 'damage' && e.victim === playerId && e.tick === tick && !killer.id) killer.id = e.attacker;
    if (e.type === 'plant' && e.tick < tick && !plant) plant = e;
  }
  const defused = events.some((e) => e.type === 'defuse' && e.tick < tick);

  const playerKnown = last.get(playerId) ?? null;
  const hint = playerKnown ?? opts.hint ?? [...last.values()][0] ?? null;
  const playerPos = playerKnown ?? approxPos(map, hint, 0);

  const enemyIds = Object.keys(r.teams)
    .filter((id) => r.teams[id] !== r.playerTeam && !dead.has(id))
    .sort((a, b) => (a === killer.id ? -1 : b === killer.id ? 1 : a < b ? -1 : 1));
  const known = enemyIds.filter((id) => last.has(id));
  const chosen = opts.allEnemies ? enemyIds : known.length > 0 ? known : enemyIds.slice(0, 1);
  let approx = 1;
  const enemies = chosen.map((id) => ({ pos: { ...(last.get(id) ?? approxPos(map, hint, approx++)) } }));
  const allies = Object.keys(r.teams)
    .filter((id) => r.teams[id] === r.playerTeam && id !== playerId && !dead.has(id) && last.has(id))
    .sort()
    .map((id) => ({ pos: { ...(last.get(id) as Vec2) } }));

  const planted = plant && !defused ? (plant as SimEvent & { type: 'plant' }) : null;
  const ids = siteIds(map);
  const nearest = (p: Vec2): SiteId => ids.reduce((a, b) => (dist(p, centroid(sitePoly(map, a))) <= dist(p, centroid(sitePoly(map, b))) ? a : b));
  const site = planted ? planted.site : nearest(enemies[0]?.pos ?? playerPos);
  const sitePos = centroid(sitePoly(map, site));

  let spikePlanted: Situation['spikePlanted'] = null;
  let roundTimeLeftS = Math.max(0, ROUND_TIME_S - secs(tick));
  if (planted) {
    const left = opts.secondsLeft ?? Math.max(0, DETONATION_S - secs(tick - planted.tick));
    spikePlanted = { site: planted.site, pos: { ...sitePos }, secondsLeft: Math.round(left * 10) / 10 };
    roundTimeLeftS = spikePlanted.secondsLeft;
  } else if (opts.secondsLeft !== undefined) roundTimeLeftS = opts.secondsLeft;

  return {
    side: r.playerTeam,
    site,
    playerPos: { ...playerPos },
    enemies,
    allies,
    roundTimeLeftS: Math.round(roundTimeLeftS * 10) / 10,
    spikePlanted,
    playerHasSpike: r.playerTeam === 'attack' && !planted,
  };
}

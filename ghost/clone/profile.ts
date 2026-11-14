/**
 * Measure the numbers that make one human's play recognisable.
 *
 * Every metric is a median or share over real samples. When a metric has no
 * samples it falls back to `PROFILE_DEFAULTS` and names itself in `defaulted`,
 * so a ghost never silently pretends to know something it never saw.
 */
import type { LogEvent, PlayerProfile } from '../contracts.ts';
import { CHEST_HEIGHT } from '../bot/view.ts';
import { DEFAULTS } from '../bot/policy-types.ts';
import type { Frame } from './replay.ts';
import { eventKey } from './replay.ts';
import { labelFrames } from './label.ts';

/** Metric defaults when a player has no sample for it; the bot's own defaults. */
export const PROFILE_DEFAULTS = {
  reactionMs: DEFAULTS.reactionMs,
  aimErrorDeg: DEFAULTS.aimErrorDeg,
  flickDegPerS: DEFAULTS.flickDegPerS,
  headshotRate: DEFAULTS.headshotRate,
  moveWhileFiring: 0.4,
} as const;

export interface MeasuredProfile extends PlayerProfile {
  /** Metrics that had no samples and therefore hold their default. */
  defaulted: string[];
}

export interface ProfileCounts {
  rounds: number;
  labelled: number;
  reaction: number;
  aim: number;
  flick: number;
  hits: number;
  tracers: number;
}

export interface MeasuredResult {
  profile: MeasuredProfile;
  counts: ProfileCounts;
}

function median(values: number[]): number | null {
  if (values.length === 0) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const mid = sorted.length >> 1;
  return sorted.length % 2 === 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
}

function firstAtOrAfter<T extends { t: number }>(list: T[], time: number): number {
  let lo = 0;
  let hi = list.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (list[mid].t < time - 1e-9) lo = mid + 1;
    else hi = mid;
  }
  return lo;
}

function lastAtOrBefore<T extends { t: number }>(list: T[], time: number): number {
  const i = firstAtOrAfter(list, time);
  if (i < list.length && Math.abs(list[i].t - time) <= 1e-9) return i;
  return i - 1;
}

function norm(v: [number, number, number]): [number, number, number] {
  const len = Math.hypot(v[0], v[1], v[2]) || 1;
  return [v[0] / len, v[1] / len, v[2] / len];
}

function angleDeg(a: [number, number, number], b: [number, number, number]): number {
  const dot = a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
  return (Math.acos(Math.max(-1, Math.min(1, dot))) * 180) / Math.PI;
}

/** Best-matching enemy chest for a tracer, by smallest angular error. */
function aimAt(
  frame: Frame,
  tracer: Extract<LogEvent, { type: 'tracer' }>,
): { target: string; errorDeg: number } | null {
  let best: { target: string; errorDeg: number } | null = null;
  for (const enemy of frame.view.players) {
    if (!enemy.alive || enemy.name === frame.player || enemy.team === frame.situation.side) continue;
    const toward: [number, number, number] = [
      enemy.pos.x - tracer.from[0],
      enemy.pos.y + CHEST_HEIGHT - tracer.from[1],
      enemy.pos.z - tracer.from[2],
    ];
    const errorDeg = angleDeg(norm(tracer.dir), norm(toward));
    if (!best || errorDeg < best.errorDeg) best = { target: enemy.name, errorDeg };
  }
  return best && best.errorDeg <= 45 ? best : null;
}

function groupFrames(frames: Frame[], player: string): Map<string, Frame[]> {
  const out = new Map<string, Frame[]>();
  for (const f of frames) {
    if (f.player !== player) continue;
    const key = eventKey(f);
    const list = out.get(key) ?? [];
    list.push(f);
    out.set(key, list);
  }
  for (const list of out.values()) list.sort((a, b) => a.t - b.t);
  return out;
}

/** How far each `move` is from a tracer, for the "moving while firing" share. */
function nearestMove(moves: LogEvent[], t: number): Extract<LogEvent, { type: 'move' }> | null {
  if (moves.length === 0) return null;
  const i = firstAtOrAfter(moves, t);
  const before = i > 0 ? (moves[i - 1] as Extract<LogEvent, { type: 'move' }>) : null;
  const after = i < moves.length ? (moves[i] as Extract<LogEvent, { type: 'move' }>) : null;
  if (!before) return after;
  if (!after) return before;
  return Math.abs(after.t - t) < Math.abs(t - before.t) ? after : before;
}

/** Full measurement plus the sample size behind each metric. */
export function measureProfileWithCounts(
  player: string,
  frames: Frame[],
  events: LogEvent[],
): MeasuredResult {
  const rounds = groupFrames(frames, player);
  const ownTracers = events
    .filter((e): e is Extract<LogEvent, { type: 'tracer' }> => e.type === 'tracer' && e.player === player)
    .sort((a, b) => a.t - b.t);
  const ownHits = events.filter((e): e is Extract<LogEvent, { type: 'hit' }> => e.type === 'hit' && e.player === player);
  const ownMoves = events.filter((e): e is Extract<LogEvent, { type: 'move' }> => e.type === 'move' && e.player === player);
  const ownAbilities = events.filter((e) => e.type === 'ability' && e.player === player);

  // Reaction: first frame an enemy appears -> next tracer by this player within 2 s.
  const reactions: number[] = [];
  for (const [key, list] of rounds) {
    const tracers = ownTracers.filter((e) => eventKey(e) === key);
    const seen = new Set<string>();
    for (const frame of list) {
      const now = new Set(frame.visible);
      for (const enemy of now) {
        if (seen.has(enemy)) continue;
        const i = firstAtOrAfter(tracers, frame.t);
        const tracer = tracers[i];
        if (tracer && tracer.t <= frame.t + 2 + 1e-9) reactions.push((tracer.t - frame.t) * 1000);
      }
      seen.clear();
      for (const enemy of now) seen.add(enemy);
    }
  }

  // Aim error: tracer direction vs. the chest of the enemy it points at.
  const aims: number[] = [];
  const shots: { key: string; t: number; target: string }[] = [];
  for (const tracer of ownTracers) {
    const key = eventKey(tracer);
    const list = rounds.get(key) ?? [];
    const i = lastAtOrBefore(list, tracer.t - 1e-6);
    if (i < 0) continue;
    const frame = list[i];
    const aim = aimAt(frame, tracer);
    if (!aim) continue;
    aims.push(aim.errorDeg);
    shots.push({ key, t: tracer.t, target: aim.target });
  }

  // Flick: peak yaw rate in the half second before the first shot at a target.
  const flicks: number[] = [];
  const firstShots: { key: string; t: number; target: string }[] = [];
  const shotKeys = new Set<string>();
  for (const shot of shots) {
    const k = `${shot.key}|${shot.target}`;
    if (shotKeys.has(k)) continue;
    shotKeys.add(k);
    firstShots.push(shot);
  }
  for (const shot of firstShots) {
    const list = rounds.get(shot.key) ?? [];
    const window = list.filter((f) => f.t >= shot.t - 0.5 - 1e-9 && f.t <= shot.t + 1e-9);
    let peak = 0;
    for (let i = 1; i < window.length; i++) {
      const a = window[i - 1];
      const b = window[i];
      const ya = a.view.players.find((p) => p.name === player)?.yaw;
      const yb = b.view.players.find((p) => p.name === player)?.yaw;
      if (ya === undefined || yb === undefined || b.t - a.t <= 1e-9) continue;
      const dy = Math.atan2(Math.sin(yb - ya), Math.cos(yb - ya));
      peak = Math.max(peak, Math.abs(dy / (b.t - a.t)));
    }
    if (window.length >= 2) flicks.push((peak * 180) / Math.PI);
  }

  // Moving while firing: nearest move to each tracer.
  let movingShots = 0;
  let movingSamples = 0;
  for (const tracer of ownTracers) {
    const moves = ownMoves.filter((e) => eventKey(e) === eventKey(tracer));
    const move = nearestMove(moves, tracer.t);
    if (!move) continue;
    movingSamples++;
    if (move.moving) movingShots++;
  }

  const headshots = ownHits.filter((h) => h.headshot).length;

  const labelled = labelFrames(frames, events).filter((l) => l.frame.player === player);
  const actionCounts: Record<string, number> = {};
  for (const l of labelled) actionCounts[l.action.kind] = (actionCounts[l.action.kind] ?? 0) + 1;
  const actionMix: PlayerProfile['actionMix'] = {};
  for (const [kind, count] of Object.entries(actionCounts)) actionMix[kind as keyof typeof actionMix] = count / (labelled.length || 1);

  const zoneCounts: Record<string, number> = {};
  for (const [key, list] of rounds) {
    for (const frame of list) {
      zoneCounts[frame.situation.selfZone] = (zoneCounts[frame.situation.selfZone] ?? 0) + 1;
    }
  }
  const zoneMix: Record<string, number> = {};
  const zoneTotal = Object.values(zoneCounts).reduce((a, b) => a + b, 0) || 1;
  for (const [zone, count] of Object.entries(zoneCounts)) zoneMix[zone] = count / zoneTotal;

  const abilityCounts = new Map<string, number>();
  for (const e of ownAbilities) {
    if (e.type !== 'ability') continue;
    abilityCounts.set(e.ability, (abilityCounts.get(e.ability) ?? 0) + 1);
  }
  const favouriteAbilities = [...abilityCounts.entries()]
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .map(([name]) => name);

  const defaulted: string[] = [];
  const pick = (name: keyof typeof PROFILE_DEFAULTS, value: number | null): number => {
    if (value === null) {
      defaulted.push(name);
      return PROFILE_DEFAULTS[name];
    }
    return value;
  };

  const profile: MeasuredProfile = {
    player,
    roundsRecorded: rounds.size,
    reactionMs: pick('reactionMs', median(reactions)),
    aimErrorDeg: pick('aimErrorDeg', median(aims)),
    flickDegPerS: pick('flickDegPerS', median(flicks)),
    headshotRate: pick('headshotRate', ownHits.length > 0 ? headshots / ownHits.length : null),
    moveWhileFiring: pick('moveWhileFiring', movingSamples > 0 ? movingShots / movingSamples : null),
    actionMix,
    zoneMix,
    favouriteAbilities,
    defaulted,
  };

  return {
    profile,
    counts: {
      rounds: rounds.size,
      labelled: labelled.length,
      reaction: reactions.length,
      aim: aims.length,
      flick: flicks.length,
      hits: ownHits.length,
      tracers: ownTracers.length,
    },
  };
}

export function measureProfile(player: string, frames: Frame[], events: LogEvent[]): MeasuredProfile {
  return measureProfileWithCounts(player, frames, events).profile;
}

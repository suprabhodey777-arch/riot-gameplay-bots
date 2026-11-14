/**
 * Turn each replay frame into the single contract `Action` the human took.
 *
 * A rule looks only at the next second of that player's own events (plus their
 * own future frames for movement), so a label can be learned from a log and
 * still match what a controller could decide at that instant.
 */
import type { Action, ActionKind, LogEvent, SiteId, Vec2 } from '../contracts.ts';
import { CHEST_HEIGHT } from '../bot/view.ts';
import { loadMapExport, loadZones, zoneAt } from '../bot/nav.ts';
import type { Frame } from './replay.ts';
import { eventKey } from './replay.ts';

export interface LabelledFrame {
  frame: Frame;
  action: Action;
}

const WINDOW_S = 1;
const PEEK_WINDOW_S = 1.5;
const MOVE_EPS = 0.3;
/** A tracer is "at" an enemy only when it points this close to their chest. */
const TRACER_MAX_DEG = 25;

function frameKey(frame: { matchId: string; round: number; player: string }): string {
  return `${eventKey(frame)}|${frame.player}`;
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

function norm(v: [number, number, number]): [number, number, number] {
  const len = Math.hypot(v[0], v[1], v[2]) || 1;
  return [v[0] / len, v[1] / len, v[2] / len];
}

function angleDeg(a: [number, number, number], b: [number, number, number]): number {
  const dot = a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
  return (Math.acos(Math.max(-1, Math.min(1, dot))) * 180) / Math.PI;
}

/** The visible enemy a tracer points at most closely, or null. */
function tracerTarget(frame: Frame, tracer: Extract<LogEvent, { type: 'tracer' }>): string | null {
  let best: { name: string; angle: number } | null = null;
  for (const name of frame.visible) {
    const enemy = frame.view.players.find((p) => p.name === name && p.alive);
    if (!enemy) continue;
    const chest: [number, number, number] = [enemy.pos.x, enemy.pos.y + CHEST_HEIGHT, enemy.pos.z];
    const toward: [number, number, number] = [
      chest[0] - tracer.from[0],
      chest[1] - tracer.from[1],
      chest[2] - tracer.from[2],
    ];
    const angle = angleDeg(norm(tracer.dir), norm(toward));
    if (angle > TRACER_MAX_DEG) continue;
    if (!best || angle < best.angle) best = { name, angle };
  }
  return best ? best.name : null;
}

function siteAt(pos: Vec2): SiteId | null {
  const sites = loadMapExport().sites;
  for (const id of Object.keys(sites) as SiteId[]) {
    const r = sites[id];
    if (pos.x >= r.x1 && pos.x <= r.x2 && pos.z >= r.z1 && pos.z <= r.z2) return id;
  }
  return null;
}

function plantedSite(events: LogEvent[], t: number, fallbackPos: Vec2): SiteId | null {
  let site: SiteId | null = null;
  for (const e of events) {
    if (e.t > t + 1e-9) break;
    if (e.type === 'plant' && e.stage === 'done' && e.site) site = e.site;
  }
  return site ?? siteAt(fallbackPos);
}

function zoneDistance(a: string | null, b: string | null): number | null {
  if (!a || !b) return null;
  const zones = loadZones().zones;
  const za = zones.find((z) => z.id === a);
  const zb = zones.find((z) => z.id === b);
  if (!za || !zb) return null;
  return Math.hypot(za.waypoint.x - zb.waypoint.x, za.waypoint.z - zb.waypoint.z);
}

function siteSide(zone: string | null): 'A' | 'B' | null {
  if (!zone) return null;
  if (zone.startsWith('a-')) return 'A';
  if (zone.startsWith('b-')) return 'B';
  return null;
}

/** Lateral sign of a displacement in the player's own facing frame. */
function lateralDir(yaw: number, dx: number, dz: number): 'left' | 'right' {
  const right = { x: Math.cos(yaw), z: -Math.sin(yaw) };
  return dx * right.x + dz * right.z >= 0 ? 'right' : 'left';
}

/**
 * Label every frame. Rules are tried in this order:
 *  1. tracer at a visible enemy -> fight.
 *  2. ability used -> use_ability.
 *  3. plant start -> plant.
 *  4. defuse start -> defuse.
 *  5. buy -> buy.
 *  6. moved < 0.3 m and no enemy visible -> hold.
 *  7. exposed to enemy sight and back inside 1.5 s -> peek_jiggle.
 *  8. exposed to enemy sight and still exposed -> peek_wide.
 *  9. moving toward the last-seen enemy's zone -> push.
 * 10. moving away after damage or below 40 hp -> retreat.
 * 11. moving from the A side to the B side or back -> rotate.
 * 12. anything else moving -> push toward the t+1 s zone.
 */
export function labelFrames(frames: Frame[], events: LogEvent[]): LabelledFrame[] {
  const byPlayer = new Map<string, LogEvent[]>();
  for (const e of events) {
    if (!e.player) continue;
    const key = `${eventKey(e)}|${e.player}`;
    const list = byPlayer.get(key) ?? [];
    list.push(e);
    byPlayer.set(key, list);
  }
  for (const list of byPlayer.values()) list.sort((a, b) => a.t - b.t);

  const series = new Map<string, Frame[]>();
  for (const f of frames) {
    const key = frameKey(f);
    const list = series.get(key) ?? [];
    list.push(f);
    series.set(key, list);
  }
  for (const list of series.values()) list.sort((a, b) => a.t - b.t);

  return frames.map((frame) => ({ frame, action: labelOne(frame, events, byPlayer, series) }));
}

function labelOne(
  frame: Frame,
  events: LogEvent[],
  byPlayer: Map<string, LogEvent[]>,
  series: Map<string, Frame[]>,
): Action {
  const key = frameKey(frame);
  const own = byPlayer.get(key) ?? [];
  const future = own.filter((e) => e.t >= frame.t - 1e-9 && e.t <= frame.t + WINDOW_S + 1e-9);
  const ownFrames = series.get(key) ?? [frame];
  const now = frame.situation;
  const selfYaw = frame.view.players.find((p) => p.name === frame.player)?.yaw ?? 0;

  const destAt = (dtn: number): Frame => {
    const i = firstAtOrAfter(ownFrames, frame.t + dtn);
    return ownFrames[Math.min(i, ownFrames.length - 1)] ?? frame;
  };

  // 1. fight: the player fired at an enemy they could see.
  for (const e of future) {
    if (e.type !== 'tracer') continue;
    const target = tracerTarget(frame, e);
    if (target) return { kind: 'fight', target };
  }

  // 2. use_ability: smoke is E, every other logged ability is Q.
  for (const e of future) {
    if (e.type !== 'ability') continue;
    return {
      kind: 'use_ability',
      slot: e.ability === 'smoke' ? 'E' : 'Q',
      at: e.at ? { x: e.at[0], z: e.at[1] } : null,
    };
  }

  // 3. plant.
  for (const e of future) {
    if (e.type === 'plant' && e.stage === 'start' && e.site) return { kind: 'plant', site: e.site };
  }

  // 4. defuse: the site comes from the completed plant (or the defuse position).
  for (const e of future) {
    if (e.type !== 'defuse' || e.stage !== 'start') continue;
    const site = plantedSite(events, frame.t, e.pos);
    if (site) return { kind: 'defuse', site };
  }

  // 5. buy.
  for (const e of future) {
    if (e.type === 'buy') return { kind: 'buy', item: e.item };
  }

  const dest = destAt(WINDOW_S);
  const shortWindow = ownFrames.filter((f) => f.t >= frame.t - 1e-9 && f.t <= frame.t + WINDOW_S + 1e-9);
  let moved = 0;
  for (const f of shortWindow) {
    moved = Math.max(moved, Math.hypot(f.situation.selfPos.x - now.selfPos.x, f.situation.selfPos.z - now.selfPos.z));
  }
  const destZone = zoneAt(dest.situation.selfPos);
  const seenZone = now.enemyLastSeenZone;

  // 6. hold: barely moved and nothing to shoot at.
  if (moved < MOVE_EPS && !now.enemyVisible) {
    return { kind: 'hold', pos: { ...now.selfPos }, watchYaw: selfYaw };
  }

  const peekEnd = destAt(PEEK_WINDOW_S);
  const peekWindow = ownFrames.filter(
    (f) => f.t >= frame.t - 1e-9 && f.t <= frame.t + PEEK_WINDOW_S + 1e-9,
  );
  const sawEnemy = peekWindow.some((f) => f.situation.enemyVisible);
  let peekPeak = frame;
  let peakMoved = 0;
  for (const f of peekWindow) {
    const d = Math.hypot(f.situation.selfPos.x - now.selfPos.x, f.situation.selfPos.z - now.selfPos.z);
    if (d > peakMoved) {
      peakMoved = d;
      peekPeak = f;
    }
  }
  const netMoved = Math.hypot(
    peekEnd.situation.selfPos.x - now.selfPos.x,
    peekEnd.situation.selfPos.z - now.selfPos.z,
  );
  const peakDx = peekPeak.situation.selfPos.x - now.selfPos.x;
  const peakDz = peekPeak.situation.selfPos.z - now.selfPos.z;

  if (!now.enemyVisible && sawEnemy && !peekEnd.situation.enemyVisible && peakMoved >= MOVE_EPS && netMoved < 1) {
    // 7. peek_jiggle: in and out of sight, back behind cover within 1.5 s.
    return {
      kind: 'peek_jiggle',
      corner: { ...now.selfPos },
      dir: lateralDir(selfYaw, peakDx, peakDz),
    };
  }
  if (!now.enemyVisible && sawEnemy && peekEnd.situation.enemyVisible && netMoved >= MOVE_EPS) {
    // 8. peek_wide: into sight and staying there.
    return {
      kind: 'peek_wide',
      corner: { ...now.selfPos },
      dir: lateralDir(selfYaw, peekEnd.situation.selfPos.x - now.selfPos.x, peekEnd.situation.selfPos.z - now.selfPos.z),
    };
  }

  const moving = moved >= MOVE_EPS;
  const zone = destZone ?? (now.selfZone !== 'unknown' ? now.selfZone : null);

  if (moving && zone && seenZone) {
    const dNow = zoneDistance(now.selfZone, seenZone);
    const dDest = zoneDistance(zone, seenZone);
    // 9. push: the destination zone is closer to where the enemy was last seen.
    if (dNow !== null && dDest !== null && dDest < dNow) return { kind: 'push', zone };
  }

  if (moving && zone && (now.tookDamageRecent || now.selfHp < 40)) {
    const dNow = zoneDistance(now.selfZone, seenZone);
    const dDest = zoneDistance(zone, seenZone);
    // 10. retreat: moving away from the threat after damage or while low.
    if (dNow === null || dDest === null || dDest >= dNow) return { kind: 'retreat', zone };
  }

  const fromSide = siteSide(now.selfZone);
  const toSide = siteSide(zone);
  if (moving && zone && fromSide && toSide && fromSide !== toSide) {
    // 11. rotate: crossing from one site side to the other.
    return { kind: 'rotate', zone };
  }

  // 12. push: default movement toward the next zone.
  return { kind: 'push', zone: zone ?? now.selfZone };
}

/** Human-readable share per action kind, handy for tests and the report. */
export function actionMix(labelled: LabelledFrame[]): Partial<Record<ActionKind, number>> {
  const counts: Partial<Record<ActionKind, number>> = {};
  for (const { action } of labelled) counts[action.kind] = (counts[action.kind] ?? 0) + 1;
  const total = labelled.length || 1;
  for (const kind of Object.keys(counts) as ActionKind[]) counts[kind] = (counts[kind] ?? 0) / total;
  return counts;
}

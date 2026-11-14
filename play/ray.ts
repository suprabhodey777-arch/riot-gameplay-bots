/**
 * The client's "raycast": what a shot along the player's aim hits. Pure, so it
 * can be tested without a browser. The sim still re-checks line of sight before
 * it applies damage; this decides head, body or miss and where a tracer ends.
 */
import type { FireIntent, SmokeState, Vec2 } from '../shared/contract.ts';
import { AGENT_RADIUS, lineOfSight } from '../sim/geometry.ts';

/** A shot passing this close to an enemy's centre is a head shot (metres). */
export const HEAD_RADIUS_M = 0.12;
/** Within the agent's radius it is a body shot. */
export const BODY_RADIUS_M = AGENT_RADIUS;
/** Tracers and rays stop here (metres). */
export const MAX_RANGE_M = 80;

type Poly = { points: Vec2[] };

export interface Target {
  id: string;
  pos: Vec2;
}

/** Distance along the ray (unit `dir`) to the first wall edge or smoke, or `maxRange`. */
export function rayBlockDistance(
  origin: Vec2,
  dir: Vec2,
  walls: Poly[],
  smokes: Pick<SmokeState, 'pos' | 'radius'>[] = [],
  maxRange = MAX_RANGE_M,
): number {
  let best = maxRange;
  for (const w of walls) {
    const pts = w.points;
    for (let i = 0; i < pts.length; i++) {
      const c = pts[i];
      const d = pts[(i + 1) % pts.length];
      const sx = d.x - c.x;
      const sy = d.y - c.y;
      const denom = dir.x * sy - dir.y * sx;
      if (Math.abs(denom) < 1e-12) continue;
      const qx = c.x - origin.x;
      const qy = c.y - origin.y;
      const t = (qx * sy - qy * sx) / denom;
      const u = (qx * dir.y - qy * dir.x) / denom;
      if (t >= 0 && u >= 0 && u <= 1 && t < best) best = t;
    }
  }
  for (const s of smokes) {
    const fx = origin.x - s.pos.x;
    const fy = origin.y - s.pos.y;
    const c = fx * fx + fy * fy - s.radius * s.radius;
    if (c < 0) return 0; // shooter is inside the cloud
    const b = fx * dir.x + fy * dir.y;
    const disc = b * b - c;
    if (disc < 0) continue;
    const t = -b - Math.sqrt(disc);
    if (t >= 0 && t < best) best = t;
  }
  return best;
}

export interface ShotResult {
  fire: FireIntent;
  /** Where the tracer ends, as a distance along the aim. */
  endDistance: number;
  /** Set when an enemy was hit. */
  hitId: string | null;
}

/**
 * Classifies one shot from `origin` along `yaw`. The first enemy whose circle the
 * ray crosses before any wall or smoke is hit: 'head' when the ray passes within
 * HEAD_RADIUS_M of its centre, else 'body' within BODY_RADIUS_M. Otherwise 'miss',
 * aimed (for the record) at the nearest enemy, or an empty id when there is none.
 */
export function classifyShot(
  origin: Vec2,
  yaw: number,
  enemies: Target[],
  walls: Poly[],
  smokes: Pick<SmokeState, 'pos' | 'radius'>[] = [],
): ShotResult {
  const dir = { x: Math.cos(yaw), y: Math.sin(yaw) };
  const blockAt = rayBlockDistance(origin, dir, walls, smokes);
  let hit: { id: string; t: number; d: number } | null = null;
  let nearest: { id: string; dist: number } | null = null;
  for (const e of enemies) {
    const rx = e.pos.x - origin.x;
    const ry = e.pos.y - origin.y;
    const dist = Math.hypot(rx, ry);
    if (!nearest || dist < nearest.dist) nearest = { id: e.id, dist };
    const t = rx * dir.x + ry * dir.y;
    if (t <= 0) continue;
    const d = Math.abs(rx * dir.y - ry * dir.x);
    if (d > BODY_RADIUS_M) continue;
    if (t > blockAt) continue;
    // The sim checks the centre-to-centre line, so a graze it would reject is not a hit here either.
    if (!lineOfSight(origin, e.pos, walls, smokes)) continue;
    if (!hit || t < hit.t) hit = { id: e.id, t, d };
  }
  if (hit) {
    return {
      fire: { targetId: hit.id, result: hit.d <= HEAD_RADIUS_M ? 'head' : 'body' },
      endDistance: hit.t,
      hitId: hit.id,
    };
  }
  return { fire: { targetId: nearest?.id ?? '', result: 'miss' }, endDistance: blockAt, hitId: null };
}

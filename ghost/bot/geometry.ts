/**
 * Small segment tests shared by `nav.ts` and `view.ts`.
 * Everything here is pure: no map or game state, so both modules can use it.
 */

import type { Vec2 } from '../contracts.ts';

/** True when the 2D segment a-b touches the closed rect [min, max]. */
export function segmentHitsRect(a: Vec2, b: Vec2, min: Vec2, max: Vec2): boolean {
  const dx = b.x - a.x;
  const dz = b.z - a.z;
  let t0 = 0;
  let t1 = 1;
  const slabs: [number, number, number, number][] = [
    [a.x, dx, min.x, max.x],
    [a.z, dz, min.z, max.z],
  ];
  for (const [p, d, lo, hi] of slabs) {
    if (Math.abs(d) < 1e-12) {
      if (p < lo || p > hi) return false;
      continue;
    }
    let enter = (lo - p) / d;
    let exit = (hi - p) / d;
    if (enter > exit) [enter, exit] = [exit, enter];
    t0 = Math.max(t0, enter);
    t1 = Math.min(t1, exit);
    if (t0 > t1) return false;
  }
  return true;
}

/**
 * True when the 3D segment a-b crosses the open box (min, max).
 * A box the ray only grazes at an endpoint does not count.
 */
export function segmentHitsBox3(
  a: { x: number; y: number; z: number },
  b: { x: number; y: number; z: number },
  min: { x: number; y: number; z: number },
  max: { x: number; y: number; z: number },
): boolean {
  const eps = 1e-6;
  let t0 = 0;
  let t1 = 1;
  const slabs: [number, number, number, number][] = [
    [a.x, b.x - a.x, min.x, max.x],
    [a.y, b.y - a.y, min.y, max.y],
    [a.z, b.z - a.z, min.z, max.z],
  ];
  for (const [p, d, lo, hi] of slabs) {
    if (Math.abs(d) < 1e-12) {
      if (p <= lo || p >= hi) return false;
      continue;
    }
    let enter = (lo - p) / d;
    let exit = (hi - p) / d;
    if (enter > exit) [enter, exit] = [exit, enter];
    t0 = Math.max(t0, enter);
    t1 = Math.min(t1, exit);
    if (t0 >= t1) return false;
  }
  return t1 > eps && t0 < 1 - eps;
}

/** Distance from point p to the 2D segment a-b. */
export function distanceToSegment(p: Vec2, a: Vec2, b: Vec2): number {
  const dx = b.x - a.x;
  const dz = b.z - a.z;
  const len2 = dx * dx + dz * dz;
  if (len2 < 1e-12) return Math.hypot(p.x - a.x, p.z - a.z);
  let t = ((p.x - a.x) * dx + (p.z - a.z) * dz) / len2;
  t = Math.max(0, Math.min(1, t));
  return Math.hypot(p.x - (a.x + dx * t), p.z - (a.z + dz * t));
}

/** True when the 2D segment a-b comes closer than `r` to the vertical cylinder at (cx, cz). */
export function segmentHitsCylinder(a: Vec2, b: Vec2, cx: number, cz: number, r: number): boolean {
  return distanceToSegment({ x: cx, z: cz }, a, b) < r;
}

import type { MapDef, Vec2 } from '../shared/contract.ts';
import { pointInPolygon } from '../sim/geometry.ts';

export const dist = (a: Vec2, b: Vec2): number => Math.hypot(a.x - b.x, a.y - b.y);

export function centroid(poly: Vec2[]): Vec2 {
  let x = 0;
  let y = 0;
  for (const p of poly) {
    x += p.x;
    y += p.y;
  }
  return { x: x / poly.length, y: y / poly.length };
}

/** Axis-aligned bounds of a polygon. */
export function bbox(poly: Vec2[]): { x0: number; y0: number; x1: number; y1: number } {
  const xs = poly.map((p) => p.x);
  const ys = poly.map((p) => p.y);
  return { x0: Math.min(...xs), y0: Math.min(...ys), x1: Math.max(...xs), y1: Math.max(...ys) };
}

/** The callout a point is in, else the nearest callout's name prefixed with "near ". */
export function calloutAt(map: MapDef, p: Vec2): string {
  for (const c of map.callouts) if (pointInPolygon(p, c.polygon)) return c.name;
  let best = 'open ground';
  let bestD = Number.POSITIVE_INFINITY;
  for (const c of map.callouts) {
    const d = dist(p, centroid(c.polygon));
    if (d < bestD) {
      bestD = d;
      best = `near ${c.name}`;
    }
  }
  return best;
}

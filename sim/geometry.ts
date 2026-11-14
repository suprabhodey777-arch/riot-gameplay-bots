import type { MapDef, SiteId, SmokeState, Vec2 } from '../shared/contract.ts';

/** Agent collision radius in metres. */
export const AGENT_RADIUS = 0.4;

type Poly = { points: Vec2[] };

/** The sites a map has, in order: A, B, and C when the map has a third. */
export function siteIds(map: Pick<MapDef, 'sites'>): SiteId[] {
  return map.sites.C ? ['A', 'B', 'C'] : ['A', 'B'];
}

/** The polygon of a site; throws when the map has no such site. */
export function sitePoly(map: Pick<MapDef, 'sites'>, id: SiteId): Vec2[] {
  const p = map.sites[id];
  if (!p) throw new Error(`map has no site ${id}`);
  return p;
}

/** Which site's polygon contains `p`, or null. */
export function siteAt(map: Pick<MapDef, 'sites'>, p: Vec2): SiteId | null {
  for (const id of siteIds(map)) if (pointInPolygon(p, map.sites[id] as Vec2[])) return id;
  return null;
}

/** True when `p` is inside the polygon (even-odd rule). */
export function pointInPolygon(p: Vec2, poly: Vec2[]): boolean {
  let inside = false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    const a = poly[i];
    const b = poly[j];
    if (a.y > p.y !== b.y > p.y && p.x < ((b.x - a.x) * (p.y - a.y)) / (b.y - a.y) + a.x) {
      inside = !inside;
    }
  }
  return inside;
}

type Box = { x0: number; y0: number; x1: number; y1: number };
const boxes = new WeakMap<Vec2[], Box>();

/** Axis-aligned bounds of a polygon, cached per points array. Used only to skip walls that cannot matter. */
export function polyBox(pts: Vec2[]): Box {
  let b = boxes.get(pts);
  if (!b) {
    b = { x0: Infinity, y0: Infinity, x1: -Infinity, y1: -Infinity };
    for (const p of pts) {
      b.x0 = Math.min(b.x0, p.x);
      b.y0 = Math.min(b.y0, p.y);
      b.x1 = Math.max(b.x1, p.x);
      b.y1 = Math.max(b.y1, p.y);
    }
    boxes.set(pts, b);
  }
  return b;
}

/** True when the segment a-b, grown by `pad`, cannot reach the polygon's bounds. */
export function segmentMissesBox(a: Vec2, b: Vec2, pts: Vec2[], pad = 0): boolean {
  const q = polyBox(pts);
  return (
    Math.max(a.x, b.x) < q.x0 - pad ||
    Math.min(a.x, b.x) > q.x1 + pad ||
    Math.max(a.y, b.y) < q.y0 - pad ||
    Math.min(a.y, b.y) > q.y1 + pad
  );
}

const cross = (ax: number, ay: number, bx: number, by: number) => ax * by - ay * bx;

/** True when segment a-b crosses segment c-d (touching counts). */
export function segmentsIntersect(a: Vec2, b: Vec2, c: Vec2, d: Vec2): boolean {
  const rx = b.x - a.x;
  const ry = b.y - a.y;
  const sx = d.x - c.x;
  const sy = d.y - c.y;
  const denom = cross(rx, ry, sx, sy);
  const qx = c.x - a.x;
  const qy = c.y - a.y;
  if (Math.abs(denom) < 1e-12) {
    if (Math.abs(cross(qx, qy, rx, ry)) > 1e-12) return false; // parallel, apart
    const rr = rx * rx + ry * ry;
    if (rr === 0) return false;
    const t0 = (qx * rx + qy * ry) / rr;
    const t1 = t0 + (sx * rx + sy * ry) / rr;
    return Math.max(Math.min(t0, t1), 0) <= Math.min(Math.max(t0, t1), 1);
  }
  const t = cross(qx, qy, sx, sy) / denom;
  const u = cross(qx, qy, rx, ry) / denom;
  return t >= 0 && t <= 1 && u >= 0 && u <= 1;
}

/** Distance from `p` to segment a-b, plus the closest point. */
export function closestOnSegment(p: Vec2, a: Vec2, b: Vec2): { point: Vec2; dist: number } {
  const dx = b.x - a.x;
  const dy = b.y - a.y;
  const len2 = dx * dx + dy * dy;
  const t = len2 === 0 ? 0 : Math.max(0, Math.min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / len2));
  const point = { x: a.x + t * dx, y: a.y + t * dy };
  return { point, dist: Math.hypot(p.x - point.x, p.y - point.y) };
}

/** True when nothing blocks the straight line from `from` to `to`: no wall edge, no smoke circle. */
export function lineOfSight(from: Vec2, to: Vec2, walls: Poly[], smokes: Pick<SmokeState, 'pos' | 'radius'>[] = []): boolean {
  for (const w of walls) {
    const pts = w.points;
    if (segmentMissesBox(from, to, pts)) continue;
    for (let i = 0; i < pts.length; i++) {
      if (segmentsIntersect(from, to, pts[i], pts[(i + 1) % pts.length])) return false;
    }
  }
  for (const s of smokes) {
    if (closestOnSegment(s.pos, from, to).dist < s.radius) return false;
  }
  return true;
}

/**
 * Pushes a circle out of every wall polygon (and keeps it inside `bounds`), so an
 * agent moving into a wall slides along it. Returns the corrected centre.
 */
export function resolveCircle(
  pos: Vec2,
  radius: number,
  walls: Poly[],
  bounds?: { width: number; height: number },
): Vec2 {
  let x = pos.x;
  let y = pos.y;
  for (let pass = 0; pass < 4; pass++) {
    let moved = false;
    for (const w of walls) {
      const pts = w.points;
      const q = polyBox(pts);
      if (x < q.x0 - radius || x > q.x1 + radius || y < q.y0 - radius || y > q.y1 + radius) continue;
      const inside = pointInPolygon({ x, y }, pts);
      let best: { point: Vec2; dist: number } | null = null;
      for (let i = 0; i < pts.length; i++) {
        const c = closestOnSegment({ x, y }, pts[i], pts[(i + 1) % pts.length]);
        if (!best || c.dist < best.dist) best = c;
      }
      if (!best) continue;
      if (inside) {
        // Centre is inside the wall: go to the nearest edge, then out by the radius.
        const dx = best.point.x - x;
        const dy = best.point.y - y;
        const d = Math.hypot(dx, dy) || 1;
        x = best.point.x + (dx / d) * radius;
        y = best.point.y + (dy / d) * radius;
        moved = true;
      } else if (best.dist < radius) {
        const dx = x - best.point.x;
        const dy = y - best.point.y;
        const d = best.dist || 1;
        x = best.point.x + (dx / d) * radius;
        y = best.point.y + (dy / d) * radius;
        moved = true;
      }
    }
    if (bounds) {
      x = Math.max(radius, Math.min(bounds.width - radius, x));
      y = Math.max(radius, Math.min(bounds.height - radius, y));
    }
    if (!moved) break;
  }
  return { x, y };
}

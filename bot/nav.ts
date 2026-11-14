import type { MapDef, SiteId, Vec2 } from '../shared/contract.ts';
import { closestOnSegment, pointInPolygon, segmentMissesBox, segmentsIntersect, sitePoly } from '../sim/geometry.ts';

type Walls = MapDef['walls'];

const dist = (a: Vec2, b: Vec2) => Math.hypot(a.x - b.x, a.y - b.y);

export const hashStr = (s: string): number => {
  let h = 2166136261;
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 16777619);
  return h >>> 0;
};

/** Distance from p to the nearest wall edge; 0 inside a wall. */
export function minWallDist(p: Vec2, walls: Walls): number {
  let best = Infinity;
  for (const w of walls) {
    if (pointInPolygon(p, w.points)) return 0;
    const pts = w.points;
    for (let i = 0; i < pts.length; i++) best = Math.min(best, closestOnSegment(p, pts[i], pts[(i + 1) % pts.length]).dist);
  }
  return best;
}

/** True when the segment a-b crosses no wall and stays at least `clr` from every wall edge. */
export function clearSegment(a: Vec2, b: Vec2, walls: Walls, clr: number): boolean {
  for (const w of walls) {
    const pts = w.points;
    if (segmentMissesBox(a, b, pts, clr)) continue;
    for (let i = 0; i < pts.length; i++) {
      const c = pts[i];
      const d = pts[(i + 1) % pts.length];
      if (segmentsIntersect(a, b, c, d)) return false;
      if (
        closestOnSegment(a, c, d).dist < clr ||
        closestOnSegment(b, c, d).dist < clr ||
        closestOnSegment(c, a, b).dist < clr ||
        closestOnSegment(d, a, b).dist < clr
      )
        return false;
    }
  }
  return true;
}

/** How far a ray from p at `angle` travels before a wall edge, capped at `max`. */
export function rayDist(p: Vec2, angle: number, walls: Walls, max: number): number {
  const dx = Math.cos(angle);
  const dy = Math.sin(angle);
  let best = max;
  for (const w of walls) {
    const pts = w.points;
    for (let i = 0; i < pts.length; i++) {
      const c = pts[i];
      const d = pts[(i + 1) % pts.length];
      const ex = d.x - c.x;
      const ey = d.y - c.y;
      const denom = dx * ey - dy * ex;
      if (Math.abs(denom) < 1e-12) continue;
      const qx = c.x - p.x;
      const qy = c.y - p.y;
      const t = (qx * ey - qy * ex) / denom;
      const u = (qx * dy - qy * dx) / denom;
      if (t >= 0 && u >= 0 && u <= 1 && t < best) best = t;
    }
  }
  return best;
}

export const centroid = (poly: Vec2[]): Vec2 => ({
  x: poly.reduce((s, p) => s + p.x, 0) / poly.length,
  y: poly.reduce((s, p) => s + p.y, 0) / poly.length,
});

const polyDist = (p: Vec2, poly: Vec2[]): number => {
  if (pointInPolygon(p, poly)) return 0;
  let best = Infinity;
  for (let i = 0; i < poly.length; i++) best = Math.min(best, closestOnSegment(p, poly[i], poly[(i + 1) % poly.length]).dist);
  return best;
};

/** Name of the callout region containing p, else the nearest region's centre, else coordinates. */
export function calloutAt(map: MapDef, p: Vec2): string {
  for (const c of map.callouts) if (pointInPolygon(p, c.polygon)) return c.name;
  let best: { name: string; d: number } | null = null;
  for (const c of map.callouts) {
    const d = dist(p, centroid(c.polygon));
    if (!best || d < best.d) best = { name: c.name, d };
  }
  return best ? `near ${best.name}` : `(${p.x.toFixed(0)},${p.y.toFixed(0)})`;
}

// ---- waypoint graph ----

export const NAV_STEP = 1.5;
const NODE_CLEARANCE = 0.45;
const EDGE_CLEARANCE = 0.4;

export interface NavGraph {
  nodes: Vec2[];
  adj: number[][];
}

const graphs = new WeakMap<MapDef, NavGraph>();

export function navFor(map: MapDef): NavGraph {
  const cached = graphs.get(map);
  if (cached) return cached;
  const nodes: Vec2[] = [];
  const index = new Map<string, number>();
  const nx = Math.floor((map.bounds.width - 1e-9) / NAV_STEP);
  const ny = Math.floor((map.bounds.height - 1e-9) / NAV_STEP);
  for (let ix = 0; ix <= nx; ix++) {
    for (let iy = 0; iy <= ny; iy++) {
      const p = { x: ix * NAV_STEP + NAV_STEP / 2, y: iy * NAV_STEP + NAV_STEP / 2 };
      if (p.x >= map.bounds.width || p.y >= map.bounds.height) continue;
      if (minWallDist(p, map.walls) < NODE_CLEARANCE) continue;
      index.set(`${ix},${iy}`, nodes.length);
      nodes.push(p);
    }
  }
  const adj: number[][] = nodes.map(() => []);
  for (const [key, i] of index) {
    const [ix, iy] = key.split(',').map(Number);
    for (const [ox, oy] of [[1, 0], [0, 1], [1, 1], [1, -1]]) {
      const j = index.get(`${ix + ox},${iy + oy}`);
      if (j === undefined) continue;
      if (clearSegment(nodes[i], nodes[j], map.walls, EDGE_CLEARANCE)) {
        adj[i].push(j);
        adj[j].push(i);
      }
    }
  }
  const g = { nodes, adj };
  graphs.set(map, g);
  return g;
}

function nearestNode(g: NavGraph, p: Vec2, map: MapDef): number {
  let best = -1;
  let bestD = Infinity;
  let bestSeen = -1;
  let bestSeenD = Infinity;
  for (let i = 0; i < g.nodes.length; i++) {
    const d = dist(g.nodes[i], p);
    if (d < bestD) {
      bestD = d;
      best = i;
    }
    if (d < bestSeenD && d < 8 && clearSegment(p, g.nodes[i], map.walls, 0.2)) {
      bestSeenD = d;
      bestSeen = i;
    }
  }
  return bestSeen >= 0 ? bestSeen : best;
}

/** Waypoints from `from` to `to` (ending at `to`), or null when no route exists. */
export function pathBetween(map: MapDef, from: Vec2, to: Vec2): Vec2[] | null {
  if (dist(from, to) < 0.3 || clearSegment(from, to, map.walls, 0.3)) return [{ ...to }];
  const g = navFor(map);
  if (g.nodes.length === 0) return null;
  const s = nearestNode(g, from, map);
  const t = nearestNode(g, to, map);
  const gScore = new Float64Array(g.nodes.length).fill(Infinity);
  const prev = new Int32Array(g.nodes.length).fill(-1);
  const closed = new Uint8Array(g.nodes.length);
  const heap: [number, number][] = [];
  const push = (f: number, n: number) => {
    heap.push([f, n]);
    let i = heap.length - 1;
    while (i > 0) {
      const p = (i - 1) >> 1;
      if (heap[p][0] <= heap[i][0]) break;
      [heap[p], heap[i]] = [heap[i], heap[p]];
      i = p;
    }
  };
  const pop = (): number => {
    const top = heap[0];
    const last = heap.pop() as [number, number];
    if (heap.length > 0) {
      heap[0] = last;
      let i = 0;
      for (;;) {
        const l = 2 * i + 1;
        const r = l + 1;
        let m = i;
        if (l < heap.length && heap[l][0] < heap[m][0]) m = l;
        if (r < heap.length && heap[r][0] < heap[m][0]) m = r;
        if (m === i) break;
        [heap[m], heap[i]] = [heap[i], heap[m]];
        i = m;
      }
    }
    return top[1];
  };
  gScore[s] = 0;
  push(dist(g.nodes[s], g.nodes[t]), s);
  while (heap.length > 0) {
    const n = pop();
    if (closed[n]) continue;
    closed[n] = 1;
    if (n === t) break;
    for (const m of g.adj[n]) {
      const cand = gScore[n] + dist(g.nodes[n], g.nodes[m]);
      if (cand < gScore[m]) {
        gScore[m] = cand;
        prev[m] = n;
        push(cand + dist(g.nodes[m], g.nodes[t]), m);
      }
    }
  }
  if (!closed[t]) return null;
  const out: Vec2[] = [];
  for (let n = t; n !== -1; n = prev[n]) out.push({ ...g.nodes[n] });
  out.reverse();
  out.push({ ...to });
  return out;
}

// ---- hold spots ----

export interface Spot {
  pos: Vec2;
  /** Yaw to hold, toward the longest sight line facing the threat. */
  facing: number;
  /** Open ground, near the middle of the site: what most players pick. */
  common: number;
  /** Tucked against a wall or cover, away from the middle. */
  unusual: number;
  sight: number;
}

const spotCache = new WeakMap<MapDef, Map<string, Spot[]>>();

/** Candidate hold spots on or near a site, facing the opposing spawns. Deterministic from the map. */
export function spotsFor(map: MapDef, site: SiteId, toward: 'attack' | 'defense'): Spot[] {
  let perMap = spotCache.get(map);
  if (!perMap) spotCache.set(map, (perMap = new Map()));
  const key = `${site}-${toward}`;
  const hit = perMap.get(key);
  if (hit) return hit;
  const poly = sitePoly(map, site);
  const c = centroid(poly);
  const threat = centroid(map.spawns[toward]);
  const xs = poly.map((p) => p.x);
  const ys = poly.map((p) => p.y);
  const x0 = Math.min(...xs) - 2.5;
  const x1 = Math.max(...xs) + 2.5;
  const y0 = Math.min(...ys) - 2.5;
  const y1 = Math.max(...ys) + 2.5;
  const out: Spot[] = [];
  for (let x = Math.ceil(x0); x <= x1; x++) {
    for (let y = Math.ceil(y0); y <= y1; y++) {
      const p = { x, y };
      if (p.x < 1 || p.y < 1 || p.x > map.bounds.width - 1 || p.y > map.bounds.height - 1) continue;
      if (polyDist(p, poly) > 2.5) continue;
      const wd = minWallDist(p, map.walls);
      if (wd < 0.6) continue;
      const bearing = Math.atan2(threat.y - p.y, threat.x - p.x);
      let facing = bearing;
      let sight = -1;
      for (let k = -6; k <= 6; k++) {
        const a = bearing + (k * 10 * Math.PI) / 180;
        const r = rayDist(p, a, map.walls, 40);
        if (r > sight + 1e-9) {
          sight = r;
          facing = a;
        }
      }
      if (sight < 5) continue;
      const toCentre = Math.min(1, dist(p, c) / 8);
      const common = (1 - toCentre) * 0.5 + Math.min(1, sight / 30) * 0.5;
      const unusual = Math.min(1, 1 - Math.min(1, wd / 3)) * 0.5 + toCentre * 0.3 + (sight >= 8 ? 0.2 : 0);
      out.push({ pos: p, facing, common, unusual, sight });
    }
  }
  if (out.length === 0) out.push({ pos: c, facing: Math.atan2(threat.y - c.y, threat.x - c.x), common: 1, unusual: 0, sight: 10 });
  perMap.set(key, out);
  return out;
}

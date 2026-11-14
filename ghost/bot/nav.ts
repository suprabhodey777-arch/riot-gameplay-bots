/**
 * Navigation for a Ghost bot: collision data from `ghost/map/colliders.json`,
 * callouts from `ghost/map/zones.json`, and waypoint pathing over the zones.
 *
 * The collision model mirrors `collideMove` in `game/public/js/map.js:145`:
 * a player is a vertical cylinder of radius 0.45 and height 1.7 standing at y=0.
 */
import { readFileSync } from 'node:fs';

import type { Box, MapExport, Vec2, Zone, ZoneMap } from '../contracts.ts';
import { segmentHitsRect } from './geometry.ts';

export const PLAYER_RADIUS = 0.45;
export const PLAYER_HEIGHT = 1.7;
/** `collideMove` only tests boxes whose top clears `pos.y + 0.2`. */
const FOOT_CLEARANCE = 0.2;
/** The server drops moves of 5 units or more, so never hand it a step that big. */
export const MAX_STEP = 4.99;
/** The client clamps positions to |x|, |z| <= 33 (`game/public/js/map.js:173`). */
const WORLD_LIMIT = 33;

/** Cache: the map files are build artifacts, not live state. */
let mapCache: MapExport | null = null;
let zoneCache: ZoneMap | null = null;

/** Collision boxes of the open game map, exported by `ghost/map/export.ts`. */
export function loadMapExport(): MapExport {
  mapCache ??= JSON.parse(readFileSync(new URL('../map/colliders.json', import.meta.url), 'utf8')) as MapExport;
  return mapCache;
}

/** Named callout zones and the walkable links between their waypoints. */
export function loadZones(): ZoneMap {
  zoneCache ??= JSON.parse(readFileSync(new URL('../map/zones.json', import.meta.url), 'utf8')) as ZoneMap;
  return zoneCache;
}

/** True when the box is tall enough at the given foot height to stop a standing player. */
export function blocksPlayer(box: Box, footY = 0): boolean {
  return !(footY + PLAYER_HEIGHT < box.min.y || footY + FOOT_CLEARANCE > box.max.y);
}

/** True when the box, grown by the player radius, contains p. */
function containsInflated(box: Box, p: Vec2): boolean {
  return (
    p.x > box.min.x - PLAYER_RADIUS &&
    p.x < box.max.x + PLAYER_RADIUS &&
    p.z > box.min.z - PLAYER_RADIUS &&
    p.z < box.max.z + PLAYER_RADIUS
  );
}

/** True when a standing player at p would overlap a collision box. */
export function blocked(p: Vec2): boolean {
  return loadMapExport().colliders.some((box) => blocksPlayer(box) && containsInflated(box, p));
}

/** True when a standing player can walk the straight line from a to b without touching a box. */
export function clearPath(a: Vec2, b: Vec2): boolean {
  const min = { x: 0, z: 0 };
  const max = { x: 0, z: 0 };
  for (const box of loadMapExport().colliders) {
    if (!blocksPlayer(box)) continue;
    min.x = box.min.x - PLAYER_RADIUS;
    min.z = box.min.z - PLAYER_RADIUS;
    max.x = box.max.x + PLAYER_RADIUS;
    max.z = box.max.z + PLAYER_RADIUS;
    if (segmentHitsRect(a, b, min, max)) return false;
  }
  return true;
}

const dist = (a: Vec2, b: Vec2): number => Math.hypot(a.x - b.x, a.z - b.z);

/** Nearest zone waypoint that is walkable from p (used for both path ends). */
function nearestVisibleWaypoint(p: Vec2, zones: Zone[]): Zone | null {
  const sorted = [...zones].sort((a, b) => dist(p, a.waypoint) - dist(p, b.waypoint));
  for (const z of sorted) {
    if (blocked(z.waypoint)) continue;
    if (clearPath(p, z.waypoint)) return z;
  }
  return null;
}

/**
 * A* over the zone waypoints. The first leg runs to the nearest waypoint with a
 * clear line from `from`, the last leg runs from a waypoint straight to `to`.
 * Returns [] when no route exists.
 */
export function findPath(from: Vec2, to: Vec2): Vec2[] {
  const map = loadZones();
  const byId = new Map(map.zones.map((z) => [z.id, z]));
  if (dist(from, to) < 1e-6) return [{ ...to }];
  if (clearPath(from, to)) return [{ ...to }];

  const start = nearestVisibleWaypoint(from, map.zones);
  const goal = nearestVisibleWaypoint(to, map.zones);
  if (!start || !goal) return [];

  const adj = new Map<string, { id: string; cost: number }[]>();
  for (const z of map.zones) adj.set(z.id, []);
  for (const [a, b] of map.links) {
    const za = byId.get(a);
    const zb = byId.get(b);
    if (!za || !zb) continue;
    const cost = dist(za.waypoint, zb.waypoint);
    adj.get(a)?.push({ id: b, cost });
    adj.get(b)?.push({ id: a, cost });
  }

  const goalWp = goal.waypoint;
  const gScore = new Map<string, number>([[start.id, 0]]);
  const prev = new Map<string, string>();
  const closed = new Set<string>();
  const open: { id: string; f: number }[] = [{ id: start.id, f: dist(start.waypoint, goalWp) }];

  while (open.length > 0) {
    let best = 0;
    for (let i = 1; i < open.length; i++) if (open[i].f < open[best].f) best = i;
    const current = open.splice(best, 1)[0];
    if (closed.has(current.id)) continue;
    closed.add(current.id);
    if (current.id === goal.id) break;
    const g = gScore.get(current.id) ?? Infinity;
    for (const next of adj.get(current.id) ?? []) {
      if (closed.has(next.id)) continue;
      const candidate = g + next.cost;
      if (candidate >= (gScore.get(next.id) ?? Infinity)) continue;
      gScore.set(next.id, candidate);
      prev.set(next.id, current.id);
      const wp = byId.get(next.id)?.waypoint;
      if (!wp) continue;
      open.push({ id: next.id, f: candidate + dist(wp, goalWp) });
    }
  }

  if (!closed.has(goal.id)) return [];

  const legs: Vec2[] = [];
  for (let id: string | undefined = goal.id; id; id = prev.get(id)) {
    const zone = byId.get(id);
    if (zone) legs.push({ ...zone.waypoint });
    if (id === start.id) break;
  }
  legs.reverse();
  if (dist(legs[legs.length - 1], to) > 1e-9) legs.push({ ...to });
  return legs;
}

/**
 * Push p out of every box it overlaps, exactly like `collideMove` does:
 * away from the nearest point on the box, or along the thinnest axis when p
 * is already inside one. A small epsilon keeps the result strictly outside.
 */
function resolve(p: Vec2): Vec2 {
  let x = p.x;
  let z = p.z;
  const boxes = loadMapExport().colliders.filter((box) => blocksPlayer(box));
  for (let pass = 0; pass < 3; pass++) {
    let moved = false;
    for (const box of boxes) {
      const nx = Math.max(box.min.x, Math.min(x, box.max.x));
      const nz = Math.max(box.min.z, Math.min(z, box.max.z));
      const dx = x - nx;
      const dz = z - nz;
      const d2 = dx * dx + dz * dz;
      if (d2 >= PLAYER_RADIUS * PLAYER_RADIUS) continue;
      moved = true;
      const target = PLAYER_RADIUS + 1e-6;
      if (d2 > 1e-6) {
        const d = Math.sqrt(d2);
        const push = (target - d) / d;
        x += dx * push;
        z += dz * push;
      } else {
        const px1 = x - box.min.x;
        const px2 = box.max.x - x;
        const pz1 = z - box.min.z;
        const pz2 = box.max.z - z;
        const m = Math.min(px1, px2, pz1, pz2);
        if (m === px1) x = box.min.x - target;
        else if (m === px2) x = box.max.x + target;
        else if (m === pz1) z = box.min.z - target;
        else z = box.max.z + target;
      }
    }
    if (!moved) break;
  }
  return {
    x: Math.max(-WORLD_LIMIT, Math.min(WORLD_LIMIT, x)),
    z: Math.max(-WORLD_LIMIT, Math.min(WORLD_LIMIT, z)),
  };
}

/**
 * One movement tick: advance at most `maxStep` toward `target`, sliding along
 * boxes instead of stopping dead, and never finish inside a box.
 */
export function stepToward(pos: Vec2, target: Vec2, maxStep: number): Vec2 {
  const step = Math.min(Math.max(maxStep, 0), MAX_STEP);
  const dx = target.x - pos.x;
  const dz = target.z - pos.z;
  const d = Math.hypot(dx, dz);
  if (d < 1e-9 || step === 0) return { ...pos };
  const k = Math.min(step, d) / d;

  const full = resolve({ x: pos.x + dx * k, z: pos.z + dz * k });
  if (!blocked(full)) return full;
  const onlyX = resolve({ x: pos.x + dx * k, z: pos.z });
  if (!blocked(onlyX)) return onlyX;
  const onlyZ = resolve({ x: pos.x, z: pos.z + dz * k });
  if (!blocked(onlyZ)) return onlyZ;
  return { ...pos };
}

/** The callout id whose rect contains p, or null outside every zone. */
export function zoneAt(p: Vec2): string | null {
  for (const zone of loadZones().zones) {
    const r = zone.rect;
    if (p.x >= r.x1 && p.x <= r.x2 && p.z >= r.z1 && p.z <= r.z2) return zone.id;
  }
  return null;
}

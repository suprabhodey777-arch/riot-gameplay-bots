// Maps are authored as JSON and loaded here. `test-box` is a small placeholder
// for tests; the real map is owned by the 3D team and added as another JSON file.
import type { MapDef, Vec2 } from '../contract.ts';
import testBox from './test-box.json';

const isVec2 = (p: unknown): p is Vec2 =>
  typeof p === 'object' && p !== null && Number.isFinite((p as Vec2).x) && Number.isFinite((p as Vec2).y);

const isPoly = (p: unknown): boolean => Array.isArray(p) && p.length >= 3 && p.every(isVec2);

/** Throws a descriptive error when `m` is not a usable MapDef; returns it typed otherwise. */
export function validateMap(m: unknown): MapDef {
  const map = m as MapDef;
  const fail = (why: string): never => {
    throw new Error(`Invalid map ${map?.id ?? '?'}: ${why}`);
  };
  if (!map || typeof map.id !== 'string' || typeof map.name !== 'string') fail('missing id or name');
  if (!(map.bounds?.width > 0 && map.bounds?.height > 0)) fail('bad bounds');
  if (!Array.isArray(map.walls)) fail('walls must be an array');
  for (const w of map.walls) if (!isPoly(w.points) || !(w.height > 0)) fail(`bad wall ${w.id}`);
  if (!isPoly(map.sites?.A) || !isPoly(map.sites?.B)) fail('sites A and B need polygons');
  for (const t of ['attack', 'defense'] as const) {
    const s = map.spawns?.[t];
    if (!Array.isArray(s) || s.length === 0 || !s.every(isVec2)) fail(`no ${t} spawns`);
  }
  for (const c of map.callouts ?? []) if (!isPoly(c.polygon)) fail(`bad callout ${c.name}`);
  return map;
}

const registry: Record<string, MapDef> = {
  'test-box': validateMap(testBox),
};

/** Registers a validated map (e.g. the real one) under its id. */
export function registerMap(m: unknown): MapDef {
  const map = validateMap(m);
  registry[map.id] = map;
  return map;
}

/** Looks a map up by id; throws when unknown. */
export function loadMap(id: string): MapDef {
  const m = registry[id];
  if (!m) throw new Error(`Unknown map: ${id}`);
  return m;
}

export const testBoxMap: MapDef = registry['test-box'];

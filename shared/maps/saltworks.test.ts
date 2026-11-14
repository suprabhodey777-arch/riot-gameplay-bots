import { describe, expect, test } from 'vitest';
import type { SiteId, Vec2 } from '../contract.ts';
import { centroid } from '../../analyzer/geo.ts';
import { pathBetween } from '../../bot/nav.ts';
import { lineOfSight, pointInPolygon, siteIds } from '../../sim/geometry.ts';
import { DEFAULT_MAP_ID, loadMap, validateMap } from './index.ts';

const map = loadMap('saltworks');
const sites = siteIds(map);
const poly = (id: SiteId): Vec2[] => map.sites[id] as Vec2[];

/** A grid of points inside a site polygon that are not inside a wall. */
function sitePoints(id: SiteId, step = 1): Vec2[] {
  const xs = poly(id).map((p) => p.x);
  const ys = poly(id).map((p) => p.y);
  const out: Vec2[] = [];
  for (let x = Math.min(...xs) + 0.5; x < Math.max(...xs); x += step) {
    for (let y = Math.min(...ys) + 0.5; y < Math.max(...ys); y += step) {
      const p = { x, y };
      if (pointInPolygon(p, poly(id)) && !map.walls.some((w) => pointInPolygon(p, w.points))) out.push(p);
    }
  }
  return out;
}

describe('saltworks map', () => {
  test('is the default map, valid, three sites, about 110 x 80 m', () => {
    expect(DEFAULT_MAP_ID).toBe('saltworks');
    expect(() => validateMap(map)).not.toThrow();
    expect(sites).toEqual(['A', 'B', 'C']);
    expect(map.bounds).toEqual({ width: 110, height: 80 });
    expect(map.spawns.attack).toHaveLength(5);
    expect(map.spawns.defense).toHaveLength(5);
  });

  test('has 12-20 original callouts and sensible wall heights', () => {
    expect(map.callouts.length).toBeGreaterThanOrEqual(12);
    expect(map.callouts.length).toBeLessThanOrEqual(20);
    expect(new Set(map.callouts.map((c) => c.name)).size).toBe(map.callouts.length);
    for (const w of map.walls) {
      const ok = (w.height >= 1 && w.height <= 1.5) || (w.height >= 2.5 && w.height <= 4);
      expect(ok, `${w.id} height ${w.height}`).toBe(true);
    }
  });

  test('spawns and site centres are open ground', () => {
    const open = (p: Vec2) => !map.walls.some((w) => pointInPolygon(p, w.points));
    for (const p of [...map.spawns.attack, ...map.spawns.defense, ...sites.map((s) => centroid(poly(s)))]) expect(open(p)).toBe(true);
  });

  test('a bot can path from every spawn to every site, and the path stays out of walls', () => {
    for (const team of ['attack', 'defense'] as const) {
      for (const spawn of map.spawns[team]) {
        for (const id of sites) {
          const goal = centroid(poly(id));
          const path = pathBetween(map, spawn, goal);
          expect(path, `${team} ${JSON.stringify(spawn)} to ${id}`).not.toBeNull();
          let at = spawn;
          for (const p of path as Vec2[]) {
            expect(lineOfSight(at, p, map.walls), `${team} to ${id}: leg crosses a wall`).toBe(true);
            at = p;
          }
        }
      }
    }
  });

  test('no site can see another site, from any open point of one to any of the other', () => {
    for (const a of sites) {
      for (const b of sites) {
        if (a >= b) continue;
        for (const p of sitePoints(a)) {
          for (const q of sitePoints(b)) {
            if (lineOfSight(p, q, map.walls)) throw new Error(`site ${a} ${JSON.stringify(p)} sees site ${b} ${JSON.stringify(q)}`);
          }
        }
      }
    }
  });

  test('attacker spawn sees no site centre and no point of any site', () => {
    for (const s of map.spawns.attack) {
      for (const id of sites) {
        expect(lineOfSight(s, centroid(poly(id)), map.walls)).toBe(false);
        for (const q of sitePoints(id, 2)) expect(lineOfSight(s, q, map.walls), `${JSON.stringify(s)} sees ${id}`).toBe(false);
      }
    }
  });

  test('the attackers are not in view of the defenders at the start', () => {
    for (const a of map.spawns.attack) for (const d of map.spawns.defense) expect(lineOfSight(a, d, map.walls)).toBe(false);
  });
});

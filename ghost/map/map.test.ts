import { describe, expect, it } from 'vitest';

import type { Box } from '../contracts.ts';
import { blocked, clearPath, loadMapExport, loadZones } from '../bot/nav.ts';

const map = loadMapExport();
const zones = loadZones();

describe('colliders.json', () => {
  it('has more than ten boxes exported from map.js', () => {
    expect(map.colliders.length).toBeGreaterThan(10);
  });

  it('has every box as a real axis-aligned volume', () => {
    for (const box of map.colliders as Box[]) {
      expect(box.min.x).toBeLessThan(box.max.x);
      expect(box.min.y).toBeLessThan(box.max.y);
      expect(box.min.z).toBeLessThan(box.max.z);
    }
  });

  it('keeps the two sites and both spawns from the game', () => {
    expect(map.sites.A).toEqual({ x1: -24, x2: -10, z1: 2, z2: 18 });
    expect(map.sites.B).toEqual({ x1: 10, x2: 24, z1: 2, z2: 18 });
    expect(map.spawns.attack.x).toBe(0);
    expect(map.spawns.attack.z).toBe(-26);
    expect(map.spawns.defense.x).toBe(0);
    expect(map.spawns.defense.z).toBe(26);
  });
});

describe('zones.json', () => {
  it('has 15 to 25 zones with unique kebab-case ids', () => {
    expect(zones.zones.length).toBeGreaterThanOrEqual(15);
    expect(zones.zones.length).toBeLessThanOrEqual(25);
    const ids = zones.zones.map((z) => z.id);
    expect(new Set(ids).size).toBe(ids.length);
    for (const id of ids) expect(id).toMatch(/^[a-z0-9]+(-[a-z0-9]+)*$/);
    for (const zone of zones.zones) expect(zone.name.length).toBeGreaterThan(0);
  });

  it('puts every waypoint inside its own rect and on walkable ground', () => {
    for (const zone of zones.zones) {
      const { x1, x2, z1, z2 } = zone.rect;
      expect(zone.waypoint.x, `${zone.id} x`).toBeGreaterThanOrEqual(x1);
      expect(zone.waypoint.x, `${zone.id} x`).toBeLessThanOrEqual(x2);
      expect(zone.waypoint.z, `${zone.id} z`).toBeGreaterThanOrEqual(z1);
      expect(zone.waypoint.z, `${zone.id} z`).toBeLessThanOrEqual(z2);
      expect(blocked(zone.waypoint), `${zone.id} is blocked`).toBe(false);
    }
  });

  it('only links existing zones whose waypoints are walkable straight lines apart', () => {
    const byId = new Map(zones.zones.map((z) => [z.id, z]));
    for (const [a, b] of zones.links) {
      const za = byId.get(a);
      const zb = byId.get(b);
      expect(za, `missing zone ${a}`).toBeDefined();
      expect(zb, `missing zone ${b}`).toBeDefined();
      if (!za || !zb) continue;
      expect(clearPath(za.waypoint, zb.waypoint), `${a} -> ${b} is blocked`).toBe(true);
    }
  });

  it('has a connected link graph covering every zone', () => {
    const adj = new Map<string, string[]>(zones.zones.map((z) => [z.id, []]));
    for (const [a, b] of zones.links) {
      adj.get(a)?.push(b);
      adj.get(b)?.push(a);
    }
    const seen = new Set<string>([zones.zones[0].id]);
    const queue = [zones.zones[0].id];
    while (queue.length > 0) {
      const id = queue.shift() as string;
      for (const next of adj.get(id) ?? []) {
        if (seen.has(next)) continue;
        seen.add(next);
        queue.push(next);
      }
    }
    expect(seen.size).toBe(zones.zones.length);
  });
});

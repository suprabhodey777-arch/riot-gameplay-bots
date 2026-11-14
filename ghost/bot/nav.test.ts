import { describe, expect, it } from 'vitest';

import type { SiteId, Vec2 } from '../contracts.ts';
import { blocked, clearPath, findPath, loadMapExport, loadZones, stepToward } from './nav.ts';

const map = loadMapExport();
const zones = loadZones();

const siteCentre = (site: SiteId): Vec2 => {
  const s = map.sites[site];
  return { x: (s.x1 + s.x2) / 2, z: (s.z1 + s.z2) / 2 };
};

/** Walk a path with 0.5 m steps, reporting how it went. */
function walk(start: Vec2, path: Vec2[]) {
  let pos = { ...start };
  let steps = 0;
  let landedBlocked = false;
  let maxJump = 0;
  for (const waypoint of path) {
    let remaining = Math.hypot(waypoint.x - pos.x, waypoint.z - pos.z);
    while (remaining > 0.05 && steps < 2000) {
      const next = stepToward(pos, waypoint, 0.5);
      const moved = Math.hypot(next.x - pos.x, next.z - pos.z);
      maxJump = Math.max(maxJump, moved);
      if (moved < 1e-9) break;
      pos = next;
      steps++;
      if (blocked(pos)) landedBlocked = true;
      remaining = Math.hypot(waypoint.x - pos.x, waypoint.z - pos.z);
    }
    if (steps >= 2000) break;
  }
  return { pos, steps, landedBlocked, maxJump };
}

describe('findPath', () => {
  for (const site of ['A', 'B'] as SiteId[]) {
    it(`routes from attack spawn to the centre of ${site} site along clear legs`, () => {
      const goal = siteCentre(site);
      expect(blocked(goal)).toBe(false);
      const path = findPath(map.spawns.attack, goal);
      expect(path.length).toBeGreaterThan(0);
      expect(path[path.length - 1]).toEqual(goal);

      let previous: Vec2 = map.spawns.attack;
      for (const point of path) {
        expect(clearPath(previous, point), `leg ${JSON.stringify(previous)} -> ${JSON.stringify(point)}`).toBe(true);
        previous = point;
      }

      const result = walk(map.spawns.attack, path);
      expect(result.landedBlocked).toBe(false);
      expect(result.maxJump).toBeLessThanOrEqual(0.5 + 1e-9);
      expect(result.steps).toBeLessThan(2000);
      expect(Math.hypot(result.pos.x - goal.x, result.pos.z - goal.z)).toBeLessThan(1);
    });
  }

  it('returns a direct leg when the goal is already in sight', () => {
    const from = map.spawns.attack;
    const to = { x: 0, z: -24 };
    expect(findPath(from, to)).toEqual([to]);
  });

  it('returns an empty path for a goal inside a wall', () => {
    const path = findPath(map.spawns.attack, { x: -17, z: 2 });
    expect(path).toEqual([]);
  });
});

describe('zones', () => {
  it('exposes at least both sites and both spawns as callouts', () => {
    const ids = new Set(zones.zones.map((z) => z.id));
    for (const id of ['a-site', 'b-site', 'attacker-spawn', 'defense-spawn']) expect(ids.has(id)).toBe(true);
  });
});

describe('stepToward', () => {
  it('moves at most maxStep and stays out of boxes', () => {
    const pos = stepToward({ x: 0, z: -28 }, { x: 0, z: 10 }, 0.5);
    expect(Math.hypot(pos.x, pos.z + 28)).toBeLessThanOrEqual(0.5 + 1e-9);
    expect(blocked(pos)).toBe(false);
  });

  it('slides along a wall instead of stopping dead', () => {
    // Push north-east into the A site's south wall (z 1.5..2.5, x -24..-10).
    const start = { x: -22, z: 1 };
    const end = stepToward(start, { x: -16, z: 6 }, 0.5);
    expect(blocked(end)).toBe(false);
    expect(end.x).toBeGreaterThan(start.x);
    expect(end.z).toBeLessThan(1.5);
  });
});

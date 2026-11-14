import { describe, expect, it } from 'vitest';

import type { Vec3 } from '../contracts.ts';
import { loadMapExport } from '../bot/nav.ts';
import {
  EYE_HEIGHT,
  createPerception,
  inViewCone,
  lineOfSight,
  yawToDir,
} from './view.ts';

const map = loadMapExport();

const eyeAt = (x: number, z: number): Vec3 => ({ x, y: EYE_HEIGHT, z });

describe('yawToDir', () => {
  it('matches the client: yaw 0 faces -z, yaw PI faces +z', () => {
    expect(yawToDir(0).x).toBeCloseTo(0, 6);
    expect(yawToDir(0).z).toBeCloseTo(-1, 6);
    expect(yawToDir(Math.PI).x).toBeCloseTo(0, 6);
    expect(yawToDir(Math.PI).z).toBeCloseTo(1, 6);
    // A quarter turn to the left of -z is -x.
    expect(yawToDir(Math.PI / 2).x).toBeCloseTo(-1, 6);
    expect(yawToDir(Math.PI / 2).z).toBeCloseTo(0, 6);
  });
});

describe('lineOfSight', () => {
  it('is blocked through the A site south wall', () => {
    // Wall at z 1.5..2.5, x -24..-10, 3.5 m tall (game/public/js/map.js:100).
    const wall = map.colliders.find((b) => b.min.x === -24 && b.max.x === -10 && b.min.z === 1.5 && b.max.z === 2.5);
    expect(wall).toBeDefined();
    expect(lineOfSight(eyeAt(-17, 5), eyeAt(-17, -1), map.colliders, [])).toBe(false);
  });

  it('is clear across open ground in the attack spawn', () => {
    expect(lineOfSight(eyeAt(0, -26), eyeAt(0, -20), map.colliders, [])).toBe(true);
  });

  it('is blocked by a smoke sitting on the line', () => {
    const smokes = [{ x: 0, z: -23, r: 2 }];
    expect(lineOfSight(eyeAt(0, -26), eyeAt(0, -20), map.colliders, smokes)).toBe(false);
  });

  it('is not blocked by a smoke off to the side', () => {
    const smokes = [{ x: 6, z: -23, r: 2 }];
    expect(lineOfSight(eyeAt(0, -26), eyeAt(0, -20), map.colliders, smokes)).toBe(true);
  });
});

describe('inViewCone', () => {
  const self = { x: 0, z: -26 };

  it('sees a target in front', () => {
    expect(inViewCone(self, 0, { x: 0, z: -32 })).toBe(true);
  });

  it('does not see a target behind', () => {
    expect(inViewCone(self, 0, { x: 0, z: -20 })).toBe(false);
  });

  it('does not see a target 90 degrees to the side', () => {
    expect(inViewCone(self, 0, { x: 6, z: -26 })).toBe(false);
  });

  it('honours a narrower fov argument', () => {
    const offAxis = { x: Math.sin((30 * Math.PI) / 180) * 6, z: -26 - Math.cos((30 * Math.PI) / 180) * 6 };
    expect(inViewCone(self, 0, offAxis, 103)).toBe(true);
    expect(inViewCone(self, 0, offAxis, 40)).toBe(false);
  });
});

describe('createPerception', () => {
  const self = { pos: { x: 0, y: 0, z: -26 }, yaw: Math.PI };
  const enemy = (x: number, z: number) => [{ name: 'p1', pos: { x, y: 0, z }, alive: true }];

  it('reports an enemy only after reactionMs of continuous sight', () => {
    const perception = createPerception({ reactionMs: 200 });
    expect(perception.update(1000, self, enemy(0, -20), map.colliders, []).visible).toEqual([]);
    expect(perception.update(1100, self, enemy(0, -20), map.colliders, []).visible).toEqual([]);
    const seen = perception.update(1200, self, enemy(0, -20), map.colliders, []);
    expect(seen.visible.map((v) => v.name)).toEqual(['p1']);
    expect(seen.lastSeen.p1.pos).toEqual({ x: 0, y: 0, z: -20 });
    expect(seen.lastSeen.p1.t).toBe(1200);
  });

  it('resets the reaction timer when sight breaks', () => {
    const perception = createPerception({ reactionMs: 200 });
    perception.update(1000, self, enemy(0, -20), map.colliders, []);
    perception.update(1150, self, enemy(0, -20), map.colliders, []);
    // Enemy steps behind the bot: out of the cone, so sight breaks.
    expect(perception.update(1200, self, enemy(0, -32), map.colliders, []).visible).toEqual([]);
    // Back in sight: the clock starts over instead of reporting instantly.
    expect(perception.update(1300, self, enemy(0, -20), map.colliders, []).visible).toEqual([]);
    expect(perception.update(1400, self, enemy(0, -20), map.colliders, []).visible).toEqual([]);
    const seen = perception.update(1500, self, enemy(0, -20), map.colliders, []);
    expect(seen.visible.map((v) => v.name)).toEqual(['p1']);
    expect(seen.lastSeen.p1.t).toBe(1500);
  });

  it('does not see an enemy hidden behind a wall', () => {
    const perception = createPerception({ reactionMs: 0 });
    // Bot north of the A site south wall, enemy south of it, same x.
    const behindWall = { pos: { x: -17, y: 0, z: 5 }, yaw: 0 };
    const result = perception.update(0, behindWall, enemy(-17, -1), map.colliders, []);
    expect(result.visible).toEqual([]);
  });

  it('keeps the last seen position after sight breaks', () => {
    const perception = createPerception({ reactionMs: 0 });
    perception.update(0, self, enemy(0, -20), map.colliders, []);
    const later = perception.update(100, self, enemy(0, -32), map.colliders, []);
    expect(later.visible).toEqual([]);
    expect(later.lastSeen.p1.pos).toEqual({ x: 0, y: 0, z: -20 });
    expect(later.lastSeen.p1.t).toBe(0);
  });
});

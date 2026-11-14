import { describe, expect, it } from 'vitest';

import type { Vec3 } from '../contracts.ts';
import { anglesTo, resolveShot } from './aim.ts';
import { loadMapExport } from './nav.ts';

const colliders = loadMapExport().colliders;
const fixedRng = (): number => 0.5;

function aimAt(origin: Vec3, point: Vec3): { yaw: number; pitch: number } {
  return anglesTo(origin, point);
}

describe('resolveShot', () => {
  it('hits a target in open ground', () => {
    const origin: Vec3 = { x: -3, y: 1.62, z: -20 };
    const feet: Vec3 = { x: -3, y: 0, z: -14 };
    const aim = aimAt(origin, { x: feet.x, y: feet.y + 1.2, z: feet.z });
    const shot = resolveShot({
      origin,
      yaw: aim.yaw,
      pitch: aim.pitch,
      weapon: 'vandal',
      moving: false,
      aimErrorDeg: 0,
      rng: fixedRng,
      boxes: colliders,
      targets: [{ name: 'dummy', pos: feet }],
    });
    expect(shot.dir).toHaveLength(3);
    expect(shot.hit).not.toBeNull();
    expect(shot.hit?.victim).toBe('dummy');
    expect(shot.hit?.headshot).toBe(false);
    expect(shot.hit?.damage).toBe(40);
    expect(shot.hit?.weapon).toBe('vandal');
  });

  it('misses a target behind a wall from colliders.json', () => {
    // The mid wall at x -6..6, z -8.6..-7.4 (4 m tall) sits between these points.
    const origin: Vec3 = { x: 0, y: 1.62, z: -10 };
    const feet: Vec3 = { x: 0, y: 0, z: -5 };
    const aim = aimAt(origin, { x: feet.x, y: feet.y + 1.2, z: feet.z });
    const shot = resolveShot({
      origin,
      yaw: aim.yaw,
      pitch: aim.pitch,
      weapon: 'vandal',
      moving: false,
      aimErrorDeg: 0,
      rng: fixedRng,
      boxes: colliders,
      targets: [{ name: 'dummy', pos: feet }],
    });
    expect(shot.hit).toBeNull();
  });

  it('headshots when aimed at the head with zero error', () => {
    const origin: Vec3 = { x: -3, y: 1.62, z: -20 };
    const feet: Vec3 = { x: -3, y: 0, z: -10 };
    const aim = aimAt(origin, { x: feet.x, y: feet.y + 1.62, z: feet.z });
    const shot = resolveShot({
      origin,
      yaw: aim.yaw,
      pitch: aim.pitch,
      weapon: 'vandal',
      moving: false,
      aimErrorDeg: 0,
      rng: fixedRng,
      boxes: colliders,
      targets: [{ name: 'dummy', pos: feet }],
    });
    expect(shot.hit).not.toBeNull();
    expect(shot.hit?.headshot).toBe(true);
    expect(shot.hit?.damage).toBe(100);
  });
});

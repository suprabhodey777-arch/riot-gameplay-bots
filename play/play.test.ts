import { describe, expect, it } from 'vitest';
import type { Vec2 } from '../shared/contract.ts';
import { BODY_RADIUS_M, HEAD_RADIUS_M, classifyShot, rayBlockDistance } from './ray.ts';
import { buildInput, moveFromKeys, yawTo } from './input.ts';

const box = (x0: number, y0: number, x1: number, y1: number) => ({ points: [{ x: x0, y: y0 }, { x: x1, y: y0 }, { x: x1, y: y1 }, { x: x0, y: y1 }] as Vec2[] });
const o = { x: 0, y: 0 };

describe('classifyShot', () => {
  it('head when the ray passes within 0.12 m of the centre', () => {
    const r = classifyShot(o, 0, [{ id: 'e', pos: { x: 10, y: HEAD_RADIUS_M - 0.02 } }], []);
    expect(r.fire).toEqual({ targetId: 'e', result: 'head' });
    expect(r.hitId).toBe('e');
  });
  it('body between 0.12 m and the agent radius', () => {
    const r = classifyShot(o, 0, [{ id: 'e', pos: { x: 10, y: 0.3 } }], []);
    expect(r.fire.result).toBe('body');
    expect(classifyShot(o, 0, [{ id: 'e', pos: { x: 10, y: BODY_RADIUS_M - 0.01 } }], []).fire.result).toBe('body');
  });
  it('miss beyond the radius, aimed at the nearest enemy', () => {
    const r = classifyShot(o, 0, [{ id: 'far', pos: { x: 20, y: 5 } }, { id: 'near', pos: { x: 10, y: 1 } }], []);
    expect(r.fire).toEqual({ targetId: 'near', result: 'miss' });
    expect(r.hitId).toBeNull();
  });
  it('miss with no enemies, and never hits behind the shooter', () => {
    expect(classifyShot(o, 0, [], []).fire).toEqual({ targetId: '', result: 'miss' });
    expect(classifyShot(o, 0, [{ id: 'e', pos: { x: -10, y: 0 } }], []).fire.result).toBe('miss');
  });
  it('a wall in between blocks, and the tracer stops at the wall', () => {
    const walls = [box(5, -2, 6, 2)];
    const r = classifyShot(o, 0, [{ id: 'e', pos: { x: 10, y: 0 } }], walls);
    expect(r.fire.result).toBe('miss');
    expect(r.endDistance).toBeCloseTo(5, 6);
    expect(classifyShot(o, 0, [{ id: 'e', pos: { x: 10, y: 0 } }], [box(5, 3, 6, 5)]).fire.result).toBe('head');
  });
  it('a smoke blocks too', () => {
    const smokes = [{ pos: { x: 5, y: 0 }, radius: 2 }];
    expect(classifyShot(o, 0, [{ id: 'e', pos: { x: 10, y: 0 } }], [], smokes).fire.result).toBe('miss');
    expect(rayBlockDistance(o, { x: 1, y: 0 }, [], smokes)).toBeCloseTo(3, 6);
  });
  it('hits the nearer of two enemies in line', () => {
    const r = classifyShot(o, 0, [{ id: 'back', pos: { x: 12, y: 0 } }, { id: 'front', pos: { x: 6, y: 0.2 } }], []);
    expect(r.fire.targetId).toBe('front');
  });
  it('works at other angles', () => {
    const r = classifyShot(o, Math.PI / 2, [{ id: 'e', pos: { x: 0.05, y: 8 } }], []);
    expect(r.fire).toEqual({ targetId: 'e', result: 'head' });
  });
});

describe('input mapping', () => {
  const keys = (...k: string[]) => new Set(k);
  it('W is up (+y), S down, D east, A west', () => {
    expect(moveFromKeys(keys('KeyW'))).toEqual({ x: 0, y: 1 });
    expect(moveFromKeys(keys('KeyS'))).toEqual({ x: 0, y: -1 });
    expect(moveFromKeys(keys('KeyD'))).toEqual({ x: 1, y: 0 });
    expect(moveFromKeys(keys('KeyA'))).toEqual({ x: -1, y: 0 });
  });
  it('diagonals are normalised and opposites cancel', () => {
    const m = moveFromKeys(keys('KeyW', 'KeyD'));
    expect(Math.hypot(m.x, m.y)).toBeCloseTo(1, 9);
    expect(moveFromKeys(keys('KeyW', 'KeyS'))).toEqual({ x: 0, y: 0 });
  });
  it('shift walks, ctrl crouches, F plants for attack and defuses for defense', () => {
    const base = { agentId: 'player', yaw: 1, fire: null, smokeAt: null };
    const a = buildInput({ ...base, team: 'attack', keys: keys('ShiftLeft', 'ControlLeft', 'KeyF') });
    expect(a).toMatchObject({ walk: true, crouch: true, plantHeld: true, defuseHeld: false, yaw: 1, agentId: 'player' });
    const d = buildInput({ ...base, team: 'defense', keys: keys('ControlLeft', 'KeyF') });
    expect(d).toMatchObject({ walk: false, crouch: true, plantHeld: false, defuseHeld: true });
    expect(buildInput({ ...base, team: 'attack', keys: keys() })).toMatchObject({ walk: false, crouch: false, plantHeld: false });
  });
  it('yawTo points at the cursor', () => {
    expect(yawTo(o, { x: 0, y: 3 })).toBeCloseTo(Math.PI / 2, 9);
    expect(yawTo({ x: 1, y: 1 }, { x: 0, y: 1 })).toBeCloseTo(Math.PI, 9);
  });
});

it('C is smoke rather than crouch; F holds the objective while E is available for dash', () => {
  const base = { agentId: 'player', team: 'attack' as const, yaw: 0, fire: null, smokeAt: null };
  const ability = buildInput({ ...base, keys: new Set(['KeyC', 'KeyE']), dash: true, flashAt: { x: 1, y: 2 }, reconAt: { x: 3, y: 4 } });
  expect(ability).toMatchObject({ crouch: false, plantHeld: false, dash: true, flashAt: { x: 1, y: 2 }, reconAt: { x: 3, y: 4 } });
  expect(buildInput({ ...base, keys: new Set(['KeyF']) }).plantHeld).toBe(true);
});

import { WeaponAim } from './weapon-aim.ts';
it('recoil follows the shared pattern, camera kick decays, and releasing resets the spray', () => {
  const aim = new WeaponAim();
  const first = aim.shot(0, 'lancer', 0, false, false, false, false, false);
  const second = aim.shot(0, 'lancer', 0.15, false, false, false, false, false);
  expect(first).not.toBe(second);
  expect(aim.cameraKick(0.15).y).toBeGreaterThan(0);
  expect(aim.cameraKick(0.55).y).toBeCloseTo(0);
  expect(aim.shot(0, 'lancer', 1, false, false, false, false, false)).toBe(first);
});

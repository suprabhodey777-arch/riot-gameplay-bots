import { expect, test } from 'vitest';
import { AIM_DIFFICULTY, aimQuality, createBot } from './bot.ts';
import { DEFAULT_TRAITS } from './traits.ts';
import { arena, duel } from '../sim/test-util.ts';
import { createSim } from '../sim/sim.ts';
import { WEAPONS, type WeaponId } from '../shared/weapons.ts';

function firing(id: WeaponId, range = 5, aggression = 0.3, roll = 0.4) {
  const map = arena({ defenseAt: { x: 2 + range, y: 10 } });
  const state = createSim({ map, seed: 1, agents: duel.map(a => ({ ...a, loadout: { primary: id } })) }).state();
  const bot = createBot({ id: 'a', map, teamPlay: false, traits: { ...DEFAULT_TRAITS, headAccuracy: 0.4, bodyAccuracy: 0.5, reactionMs: 0, aggression }, rng: { next: () => roll, int: () => 0, state: () => 0 } });
  return { bot, state };
}
test.each(Object.keys(WEAPONS) as WeaponId[])('%s bot fires at its own interval', id => {
  const { bot, state } = firing(id); const ticks: number[] = [];
  for (let tick = 0; tick < 60; tick++) { state.tick = tick; if (bot.decide(state).input.fire) ticks.push(tick); }
  const gap = Math.ceil(WEAPONS[id].fireIntervalS * 20 - 1e-9);
  expect(ticks.length).toBeGreaterThan(1);
  expect(ticks.slice(1).every((t, i) => t - ticks[i] === gap)).toBe(true);
});
test('weapon and range scale head/body accuracy without extra random draws', () => {
  // A low fixed roll: the first shot after acquiring a target carries the flick penalty (aimQuality),
  // so the same roll lands a head shot only where the weapon and range help.
  const close = firing('wasp', 5, 0.3, 0.1), far = firing('wasp', 16, 0.3, 0.1);
  expect(close.bot.decide(close.state).input.fire?.result).toBe('head');
  expect(far.bot.decide(far.state).input.fire?.result).not.toBe('head');
  const still = firing('longshot', 16, 0.3, 0.1), moving = firing('longshot', 16, 0.9, 0.1);
  expect(still.bot.decide(still.state).input.fire?.result).toBe('head');
  expect(moving.bot.decide(moving.state).input.fire?.result).not.toBe('head');
});

test('aim quality: a fast turn, a strafing target, range, moving and a long spray all cost accuracy', () => {
  const base = { turnRad: 0, sinceReadyMs: 2000, reactionMs: 250, targetSpeed: 0, distM: 8, selfMoving: false, shotsInRow: 1 };
  expect(aimQuality(base)).toBeCloseTo(1);
  expect(aimQuality({ ...base, sinceReadyMs: 0 })).toBeCloseTo(0.35);
  // A bigger turn takes longer to settle: 150 ms after a half turn is worse than after a small one.
  expect(aimQuality({ ...base, sinceReadyMs: 150, turnRad: Math.PI })).toBeLessThan(aimQuality({ ...base, sinceReadyMs: 150, turnRad: 0.2 }));
  expect(aimQuality({ ...base, targetSpeed: 6.75 })).toBeCloseTo(0.5);
  expect(aimQuality({ ...base, distM: 42 })).toBeCloseTo(0.55);
  expect(aimQuality({ ...base, selfMoving: true })).toBeCloseTo(0.65);
  expect(aimQuality({ ...base, shotsInRow: 7 })).toBeCloseTo(0.5);
  expect(AIM_DIFFICULTY.easy).toBeLessThan(AIM_DIFFICULTY.normal);
  expect(AIM_DIFFICULTY.normal).toBeLessThan(AIM_DIFFICULTY.hard);
  expect(AIM_DIFFICULTY.hard).toBeLessThan(AIM_DIFFICULTY.pro);
});

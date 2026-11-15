import { expect, test } from 'vitest';
import { createBot } from './bot.ts';
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
  const close = firing('wasp', 5), far = firing('wasp', 16);
  expect(close.bot.decide(close.state).input.fire?.result).toBe('head');
  expect(far.bot.decide(far.state).input.fire?.result).toBe('miss');
  const still = firing('longshot', 16), moving = firing('longshot', 16, 0.9);
  expect(still.bot.decide(still.state).input.fire?.result).toBe('head');
  expect(moving.bot.decide(moving.state).input.fire?.result).toBe('miss');
});

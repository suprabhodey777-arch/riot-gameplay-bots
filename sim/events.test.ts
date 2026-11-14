import { expect, test } from 'vitest';
import { createSim } from './sim.ts';
import { arena, duel, input } from './test-util.ts';

test('a kill event carries both positions, and so does the damage event', () => {
  const sim = createSim({ map: arena(), seed: 1, agents: duel });
  const { events } = sim.step([input('a', { fire: { targetId: 'd', result: 'head' } })]);
  const kill = events.find((e) => e.type === 'kill');
  const damage = events.find((e) => e.type === 'damage');
  expect(kill).toMatchObject({ killerPos: { x: 2, y: 10 }, victimPos: { x: 18, y: 10 } });
  expect(damage).toMatchObject({ attackerPos: { x: 2, y: 10 }, victimPos: { x: 18, y: 10 } });
});

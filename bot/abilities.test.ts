import { expect, test } from 'vitest';
import { createSim } from '../sim/sim.ts';
import { arena, duel, input, wallBetween } from '../sim/test-util.ts';
import { createRng } from '../sim/rng.ts';
import { createBot } from './bot.ts';
import { createMatch } from './match.ts';
import { archetypeById } from './traits.ts';
import { botView, createMemory } from './view.ts';

test('a blinded bot has no visual contacts and cannot engage or fire', () => {
  const map = arena();
  const state = createSim({ map, seed: 1, agents: duel }).state();
  state.agents[0].blindUntilTick = 20;
  const view = botView(state, 'a', map, createMemory());
  expect(view.visible).toEqual([]);
  const bot = createBot({ id: 'a', map, rng: createRng(1), traits: archetypeById('baseline').traits });
  const d = bot.decide(state);
  expect(d.node).toBe('blinded');
  expect(d.input.fire).toBeNull();
});
test('a bot turns toward loud footsteps through a wall, but not walking footsteps', () => {
  const map = arena({ walls: wallBetween, defenseAt: { x: 16, y: 10 } });
  const state = createSim({ map, seed: 1, agents: duel }).state();
  state.agents[0].yaw = Math.PI / 2;
  state.tick = 100;
  state.agents[0].carryingSpike = false;
  for (const loud of [true, false]) {
    const bot = createBot({ id: 'a', map, rng: createRng(1), traits: archetypeById('baseline').traits });
    const d = bot.decide(state, [{ type: 'footstep', agent: 'd', pos: { x: 16, y: 10 }, loud, tick: 100 }]);
    expect(d.node === 'investigate').toBe(loud);
    if (loud) expect(d.input.yaw).toBe(0);
  }
});
test('heard positions are snapshots and stop updating after the footstep', () => {
  const map = arena({ walls: wallBetween, defenseAt: { x: 16, y: 10 } });
  const state = createSim({ map, seed: 1, agents: duel }).state();
  const memory = createMemory();
  botView(state, 'a', map, memory, [{ type: 'footstep', agent: 'd', pos: { x: 16, y: 9 }, loud: true, tick: 0 }]);
  state.tick = 1;
  state.agents[1].pos = { x: 19, y: 19 };
  expect(botView(state, 'a', map, memory).remembered).toEqual([{ id: 'd', pos: { x: 16, y: 9 }, tick: 0 }]);
});
test('running footsteps beyond 15m are not heard', () => {
  const map = arena({ walls: wallBetween });
  const state = createSim({ map, seed: 1, agents: duel }).state();
  expect(botView(state, 'a', map, createMemory(), [{ type: 'footstep', agent: 'd', pos: { x: 18, y: 10 }, loud: true, tick: 0 }]).remembered).toEqual([]);
});
test('recon remains known through walls while revealed, blindness still prevents spotting', () => {
  const map = arena({ walls: wallBetween });
  const state = createSim({ map, seed: 1, agents: duel }).state();
  state.agents[1].revealedUntilTick = 40;
  expect(botView(state, 'a', map, createMemory()).visible.map(e => e.id)).toEqual(['d']);
  state.agents[0].blindUntilTick = 20;
  expect(botView(state, 'a', map, createMemory()).visible).toEqual([]);
});
test('Rook flashes before peeking a held angle and turns away from the fuse', () => {
  const map = arena({ walls: wallBetween, defenseAt: { x: 16, y: 10 } });
  const m = createMatch({ map, seed: 1, agents: [{ ...duel[0], isBot: true, archetypeId: 'entry-duelist' }, { ...duel[1], isBot: false }],
    scenario: { agents: { a: { carryingSpike: false } } } });
  const state = m.state();
  expect(state.agents[0].agent).toBe('rook');
  expect(botView(state, 'a', map, createMemory()).visible).toEqual([]);
  // The defender just stopped on the other side of the wall. Rook knows the
  // angle from its last footstep, but has not exposed itself to the hold.
  const bot = createBot({ id: 'a', map, rng: createRng(1), archetypeId: 'entry-duelist', traits: archetypeById('entry-duelist').traits });
  const intent = bot.decide(state, [{ type: 'footstep', agent: 'd', pos: { x: 16, y: 10 }, loud: true, tick: 0 }]).input;
  expect(intent.flashAt).toEqual({ x: 16, y: 10 });
  expect(intent.move).toEqual({ x: 0, y: 0 });
  expect(intent.fire).toBeNull();
  expect(Math.cos(intent.yaw)).toBeCloseTo(-1);
  const first = m.sim.step([intent, input('d', { yaw: Math.PI })]);
  expect(first.events.some(e => e.type === 'flash')).toBe(true);
  expect(first.state.agents[0].pos).toEqual(state.agents[0].pos);
  for (let i = 0; i < 12; i++) m.sim.step([input('d', { yaw: Math.PI })]);
  expect(m.state().agents[0].blindUntilTick).toBe(0);
});
test('Pike recons a heard flank and patient lurkers walk near expected enemies', () => {
  const map = arena({ walls: wallBetween, defenseAt: { x: 16, y: 10 } });
  const state = createSim({ map, seed: 1, agents: [{ ...duel[0], agent: 'pike' }, duel[1]] }).state();
  state.tick = 1000;
  const bot = createBot({ id: 'a', map, rng: createRng(1), traits: archetypeById('patient-lurker').traits });
  const d = bot.decide(state, [{ type: 'footstep', agent: 'd', pos: { x: 16, y: 10 }, loud: true, tick: 1000 }]);
  expect(d.input.reconAt).toEqual({ x: 16, y: 10 });
  expect(d.input.walk).toBe(true);
  const anchor = createBot({ id: 'a', map, rng: createRng(1), traits: archetypeById('off-angle-anchor').traits });
  expect(anchor.decide(state, [{ type: 'footstep', agent: 'd', pos: { x: 16, y: 10 }, loud: true, tick: 1000 }]).input.walk).toBe(false);
});
test('post-plant attackers cover the spike rather than chase a stale footstep', () => {
  const map = arena({ walls: wallBetween, defenseAt: { x: 16, y: 10 } });
  const state = createSim({ map, seed: 1, agents: duel, scenario: { spikePlanted: { site: 'A', pos: { x: 3, y: 10 }, secondsLeft: 30 } } }).state();
  const bot = createBot({ id: 'a', map, rng: createRng(1), traits: archetypeById('baseline').traits });
  const d = bot.decide(state, [{ type: 'footstep', agent: 'd', pos: { x: 16, y: 10 }, loud: true, tick: 0 }]);
  expect(d.node).not.toBe('investigate');
});

test('a teammate foothold commits the team before individual lurk timers expire', () => {
  const map = arena({ walls: wallBetween });
  // No opponents are visible or heard; only shared teammate positions provide the cue.
  const state = createSim({ map, seed: 1, agents: [duel[0], { id: 'entry', team: 'attack', isBot: true }],
    scenario: { agents: { a: { spawn: { x: 7, y: 18 }, carryingSpike: false }, entry: { spawn: { x: 3, y: 10 }, carryingSpike: true } } } }).state();
  const bot = createBot({ id: 'a', map, rng: createRng(1), traits: archetypeById('patient-lurker').traits });
  // The team's seeded site choice is public and deterministic. Put an ally on each site.
  state.agents.push({ ...state.agents[1], id: 'entry-b', pos: { x: 17, y: 2 } });
  expect(bot.decide(state).node).not.toBe('lurk');
});

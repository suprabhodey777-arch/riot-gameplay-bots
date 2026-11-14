import { expect, test } from 'vitest';
import { AGENTS } from '../shared/contract.ts';
import { createSim, DASH_DISTANCE_M, FLASH_BLIND_S, FLASH_FUSE_S, RECON_REVEAL_S, TICK_RATE } from './sim.ts';
import { arena, duel, input, rect, wallBetween } from './test-util.ts';

const flash = (yaw = Math.PI, walls = false, pop = { x: 12, y: 10 }) => {
  const sim = createSim({ map: arena({ walls: walls ? wallBetween : [] }), seed: 1,
    agents: duel.map(a => ({ ...a, agent: 'wren' as const })), scenario: { agents: { d: { yaw } } } });
  sim.step([input('a', { flashAt: pop })]);
  return sim;
};

test('selected agents start with their exact per-round kit; smoke aliases stay equal', () => {
  for (const agent of Object.keys(AGENTS) as (keyof typeof AGENTS)[]) {
    const sim = createSim({ map: arena(), seed: 1, agents: [{ ...duel[0], agent }] });
    expect(sim.state().agents[0].abilities).toEqual(AGENTS[agent].kit);
    const s = sim.step([input('a', { smokeAt: { x: 5, y: 10 } })]).state.agents[0];
    expect(s.smokesLeft).toBe(s.abilities.smoke);
  }
});
test('flash waits its full fuse, then blinds facing agents for 1.75 seconds', () => {
  const sim = flash();
  for (let i = 1; i < FLASH_FUSE_S * TICK_RATE; i++) expect(sim.step([]).state.agents[1].blindUntilTick).toBe(0);
  const r = sim.step([]);
  expect(r.state.agents[1].blindUntilTick).toBe(r.state.tick + FLASH_BLIND_S * TICK_RATE);
  expect(r.events).toContainEqual({ type: 'blinded', tick: r.state.tick, agent: 'd', by: 'a', untilTick: r.state.tick + 35 });
});
test('a close flash behind an agent blinds for only half a second', () => {
  const sim = flash(0);
  for (let i = 0; i < 12; i++) sim.step([]);
  expect(sim.state().agents[1].blindUntilTick).toBe(sim.state().tick + 10);
});
test('a flash behind farther than 8m does not blind', () => {
  const sim = flash(0, false, { x: 8, y: 10 });
  for (let i = 0; i < 12; i++) sim.step([]);
  expect(sim.state().agents[1].blindUntilTick).toBe(0);
});
test('flash requires LOS, including walls', () => {
  const sim = flash(Math.PI, true, { x: 8, y: 10 });
  for (let i = 0; i < 12; i++) sim.step([]);
  expect(sim.state().agents[1].blindUntilTick).toBe(0);
});
test('flash uses the 103 degree FOV boundary', () => {
  for (const [offset, full] of [[51, true], [53, false]] as const) {
    const sim = flash(Math.PI + offset * Math.PI / 180);
    for (let i = 0; i < 12; i++) sim.step([]);
    expect(sim.state().agents[1].blindUntilTick - sim.state().tick).toBe(full ? 35 : 10);
  }
});
test('a flash blinds the thrower and teammates too', () => {
  const sim = createSim({ map: arena(), seed: 1, agents: [
    ...duel.map(a => ({ ...a, agent: 'wren' as const })),
    { id: 'ally', team: 'attack', isBot: true, agent: 'wren' },
  ] });
  sim.step([input('a', { flashAt: { x: 12, y: 10 } })]);
  for (let i = 0; i < 12; i++) sim.step([]);
  expect(sim.state().agents[0].blindUntilTick).toBe(sim.state().tick + 35);
  expect(sim.state().agents[2].blindUntilTick).toBe(sim.state().tick + 35);
});
test('blinded shots miss until the exact expiry tick', () => {
  const sim = flash();
  for (let i = 0; i < 12; i++) sim.step([]);
  const shot = input('d', { fire: { targetId: 'a', result: 'head' } });
  expect(sim.step([shot]).events.some(e => e.type === 'damage')).toBe(false);
  while (sim.state().tick < 47) sim.step([]);
  expect(sim.step([shot]).events.some(e => e.type === 'damage')).toBe(true);
});
test('dash covers 6m over four ticks in move direction, consuming one charge', () => {
  const sim = createSim({ map: arena(), seed: 1, agents: [{ ...duel[0], agent: 'rook' }] });
  for (let i = 0; i < 4; i++) {
    const r = sim.step([input('a', { move: { x: 1, y: 0 }, dash: i === 0 })]);
    expect(r.state.agents[0].pos.x).toBeCloseTo(2 + (i + 1) * DASH_DISTANCE_M / 4);
    expect(r.events.some(e => e.type === 'dash')).toBe(i === 3);
  }
  expect(sim.state().agents[0].abilities.dash).toBe(0);
});
test('stationary dash uses facing and stops at a thin wall without tunnelling', () => {
  const map = arena({ walls: [{ id: 'thin', points: rect(4, 0, 4.05, 20), height: 3 }] });
  const sim = createSim({ map, seed: 1, agents: [{ ...duel[0], agent: 'rook' }] });
  for (let i = 0; i < 4; i++) sim.step([input('a', { dash: i === 0 })]);
  expect(sim.state().agents[0].pos.x).toBeGreaterThan(3.4);
  expect(sim.state().agents[0].pos.x).toBeLessThanOrEqual(3.6);
});
test('recon fuses for .5s and reveals only enemies in radius with LOS for 2s', () => {
  const sim = createSim({ map: arena(), seed: 1, agents: duel.map(a => ({ ...a, agent: 'wren' as const })) });
  sim.step([input('a', { reconAt: { x: 12, y: 10 } })]);
  for (let i = 0; i < 9; i++) expect(sim.step([]).state.agents[1].revealedUntilTick).toBe(0);
  const r = sim.step([]);
  expect(r.state.agents[1].revealedUntilTick).toBe(r.state.tick + RECON_REVEAL_S * TICK_RATE);
  expect(r.state.agents[0].revealedUntilTick).toBe(0);
});
test('recon cannot reveal through a wall or beyond its radius', () => {
  for (const map of [arena({ walls: wallBetween }), arena()]) {
    const sim = createSim({ map, seed: 1, agents: duel.map(a => ({ ...a, agent: 'wren' as const })) });
    sim.step([input('a', { reconAt: { x: 5, y: 10 } })]);
    for (let i = 0; i < 10; i++) sim.step([]);
    expect(sim.state().agents[1].revealedUntilTick).toBe(0);
  }
});
test('out-of-range throws do not consume charges or produce events', () => {
  for (const ability of ['flash', 'recon', 'smoke'] as const) {
    const agent = ability === 'smoke' ? 'haze' : 'wren';
    const sim = createSim({ map: arena(), seed: 1, agents: [{ ...duel[0], agent }] });
    const r = sim.step([input('a', { [`${ability}At`]: { x: 100, y: 100 } })]);
    expect(r.state.agents[0].abilities).toEqual(AGENTS[agent].kit);
    expect(r.events.some(e => e.type === ability)).toBe(false);
  }
});
test.each([{ walk: false, crouch: false, loud: true }, { walk: true, crouch: false, loud: false }, { walk: false, crouch: true, loud: false }])('movement emits a footstep with loud=$loud', ({ walk, crouch, loud }) => {
  const sim = createSim({ map: arena(), seed: 1, agents: duel });
  const r = sim.step([input('a', { move: { x: 1, y: 0 }, walk, crouch })]);
  expect(r.events.find(e => e.type === 'footstep')).toMatchObject({ agent: 'a', pos: r.state.agents[0].pos, loud });
});
test('stationary agents emit no footsteps; sim never consumes its RNG', () => {
  const sim = createSim({ map: arena(), seed: 1, agents: [{ ...duel[0], agent: 'rook' }] });
  const reference = createSim({ map: arena(), seed: 1, agents: duel });
  expect(sim.step([]).events.some(e => e.type === 'footstep')).toBe(false);
  sim.step([input('a', { flashAt: { x: 5, y: 10 }, dash: true })]);
  for (let i = 0; i < 20; i++) sim.step([]);
  expect(sim.rng.next()).toBe(reference.rng.next());
});

test('a facing flash beyond 30m does not blind', () => {
  const map = arena(); map.bounds.width = 60; map.spawns.defense = [{ x: 45, y: 10 }];
  const sim = createSim({ map, seed: 1, agents: duel.map(a => ({ ...a, agent: 'wren' as const })) });
  sim.step([input('a', { flashAt: { x: 12, y: 10 } }), input('d', { yaw: Math.PI })]);
  for (let i = 0; i < 12; i++) sim.step([]);
  expect(sim.state().agents[1].blindUntilTick).toBe(0);
});
test('blind agents cannot spot an enemy that first enters LOS during the blind', () => {
  const map = arena({ walls: [{ id: 'partial', points: rect(9, 0, 11, 11), height: 3 }] });
  const sim = createSim({ map, seed: 1, agents: duel.map(a => ({ ...a, agent: 'wren' as const })) });
  sim.step([input('a', { flashAt: { x: 2, y: 10 } })]);
  for (let i = 0; i < 12; i++) sim.step([]);
  expect(sim.state().agents[0].blindUntilTick).toBe(48);
  for (let i = 0; i < 20; i++) {
    const r = sim.step([input('d', { move: { x: 0, y: 1 } })]);
    expect(r.events.some(e => e.type === 'spotted' && e.observer === 'a')).toBe(false);
  }
  while (sim.state().tick < 47) sim.step([]);
  expect(sim.step([]).events).toContainEqual({ type: 'spotted', tick: 48, observer: 'a', target: 'd' });
});
test('recon counts an enemy as spotted for the team even behind the observer wall', () => {
  const sim = createSim({ map: arena({ walls: wallBetween }), seed: 1, agents: duel.map(a => ({ ...a, agent: 'wren' as const })) });
  sim.step([input('a', { reconAt: { x: 12, y: 10 } })]);
  for (let i = 0; i < 9; i++) sim.step([]);
  const r = sim.step([]);
  expect(r.events).toContainEqual({ type: 'spotted', tick: 11, observer: 'a', target: 'd' });
});
test('dash continues its four-tick movement after the activation input is released', () => {
  const sim = createSim({ map: arena(), seed: 1, agents: [{ ...duel[0], agent: 'rook' }] });
  sim.step([input('a', { dash: true })]);
  for (let i = 0; i < 3; i++) sim.step([]);
  expect(sim.state().agents[0].pos.x).toBeCloseTo(8);
});

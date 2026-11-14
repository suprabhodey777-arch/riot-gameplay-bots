import { describe, expect, test } from 'vitest';
import type { PlayerInput, SimEvent } from '../shared/contract.ts';
import { testBoxMap } from '../shared/maps/index.ts';
import { createSim } from './sim.ts';
import { arena, duel, input, wallBetween } from './test-util.ts';

const head = { targetId: 'd', result: 'head' as const };
const body = { targetId: 'd', result: 'body' as const };

describe('movement', () => {
  test('running into a wall slides along it and never passes through', () => {
    const map = arena({ walls: [{ id: 'w', points: [{ x: 8, y: 0 }, { x: 12, y: 0 }, { x: 12, y: 20 }, { x: 8, y: 20 }], height: 4 }] });
    const sim = createSim({ map, seed: 1, agents: [duel[0]] });
    for (let i = 0; i < 60; i++) sim.step([input('a', { move: { x: 1, y: 1 } })]);
    const p = sim.state().agents[0].pos;
    expect(p.x).toBeLessThanOrEqual(8 - 0.4 + 1e-6);
    expect(p.x).toBeGreaterThan(6);
    expect(p.y).toBeGreaterThan(10.5); // kept sliding north
  });

  test('speeds: run 6.75, walk 3, crouch 2 m/s', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: [duel[0]] });
    for (let i = 0; i < 20; i++) sim.step([input('a', { move: { x: 1, y: 0 } })]);
    expect(sim.state().agents[0].pos.x).toBeCloseTo(2 + 6.75);
    const walk = createSim({ map: arena(), seed: 1, agents: [duel[0]] });
    for (let i = 0; i < 20; i++) walk.step([input('a', { move: { x: 1, y: 0 }, walk: true })]);
    expect(walk.state().agents[0].pos.x).toBeCloseTo(5);
    const crouch = createSim({ map: arena(), seed: 1, agents: [duel[0]] });
    for (let i = 0; i < 20; i++) crouch.step([input('a', { move: { x: 1, y: 0 }, crouch: true })]);
    expect(crouch.state().agents[0].pos.x).toBeCloseTo(4);
  });

  test('inputs for unknown or dead agents are ignored', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: duel });
    sim.step([input('a', { fire: head })]);
    const r = sim.step([input('d', { move: { x: -1, y: 0 } }), input('ghost', { move: { x: 1, y: 0 } })]);
    expect(r.state.agents.find((a) => a.id === 'd')?.pos.x).toBe(18);
    expect(r.state.agents).toHaveLength(2);
  });
});

describe('fighting', () => {
  test('a head shot kills', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: duel });
    const r = sim.step([input('a', { fire: head })]);
    expect(r.events.map((e) => e.type)).toEqual(expect.arrayContaining(['spotted', 'damage', 'kill', 'round_end']));
    expect(r.state.agents[1].alive).toBe(false);
    expect(r.state.winner).toBe('attack');
  });

  test('body shots take three to kill', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: [...duel, { id: 'd2', team: 'defense', isBot: true }] });
    for (let i = 0; i < 2; i++) {
      const r = sim.step([input('a', { fire: body })]);
      expect(r.state.agents.find((a) => a.id === 'd')?.alive).toBe(true);
    }
    const r = sim.step([input('a', { fire: body })]);
    expect(r.state.agents.find((a) => a.id === 'd')?.alive).toBe(false);
    expect(r.state.agents.find((a) => a.id === 'd')?.hp).toBe(0);
  });

  test('a miss does nothing', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: duel });
    const r = sim.step([input('a', { fire: { targetId: 'd', result: 'miss' } })]);
    expect(r.events.some((e) => e.type === 'damage')).toBe(false);
  });

  test('a wall blocks shots', () => {
    const sim = createSim({ map: arena({ walls: wallBetween }), seed: 1, agents: duel });
    const r = sim.step([input('a', { fire: head })]);
    expect(r.events.filter((e) => e.type !== 'spotted')).toEqual([]);
    expect(r.state.agents[1].alive).toBe(true);
  });

  test('smoke blocks shots and sight, then expires after 15 s', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: [...duel, { id: 'd2', team: 'defense', isBot: true }] });
    const r = sim.step([input('a', { smokeAt: { x: 10, y: 10 }, fire: head })]);
    expect(r.events.some((e) => e.type === 'smoke')).toBe(true);
    expect(r.events.some((e) => e.type === 'damage')).toBe(false);
    expect(r.events.some((e) => e.type === 'spotted')).toBe(false);
    expect(r.state.smokes[0].radius).toBe(4.5);
    expect(r.state.agents[0].smokesLeft).toBe(1);
    for (let i = 0; i < 299; i++) sim.step([]);
    expect(sim.state().smokes).toHaveLength(1);
    sim.step([]);
    expect(sim.state().smokes).toHaveLength(0);
  });

  test('spotted fires once per pair', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: duel });
    const first = sim.step([]);
    const second = sim.step([]);
    expect(first.events.filter((e) => e.type === 'spotted')).toHaveLength(2);
    expect(second.events).toHaveLength(0);
  });
});

describe('spike and round', () => {
  const run = (sim: ReturnType<typeof createSim>, ticks: number, f: (t: number) => PlayerInput[]) => {
    const events: SimEvent[] = [];
    for (let t = 0; t < ticks; t++) events.push(...sim.step(f(t)).events);
    return events;
  };

  test('plant after 4 s, then detonation ends the round for attack', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: duel });
    const ev = run(sim, 79, () => [input('a', { plantHeld: true })]);
    expect(ev.some((e) => e.type === 'plant')).toBe(false);
    expect(run(sim, 1, () => [input('a', { plantHeld: true })]).map((e) => e.type)).toEqual(['plant']);
    expect(sim.state().phase).toBe('post_plant');
    const rest = run(sim, 900, () => []);
    const end = rest.find((e) => e.type === 'round_end');
    expect(end).toMatchObject({ winner: 'attack', reason: 'detonated' });
    expect(sim.state().spike.status).toBe('detonated');
  });

  test('plant is not possible outside a site', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: duel });
    const ev = run(sim, 100, () => [input('a', { move: { x: 1, y: 0 } , plantHeld: true })]);
    expect(ev.some((e) => e.type === 'plant')).toBe(false);
  });

  test('defuse after 7 s next to the spike wins for defense', () => {
    const sim = createSim({ map: arena({ defenseAt: { x: 3, y: 10 } }), seed: 1, agents: duel });
    run(sim, 80, () => [input('a', { plantHeld: true })]);
    expect(sim.state().spike.status).toBe('planted');
    const ev = run(sim, 140, () => [input('d', { defuseHeld: true })]);
    expect(ev.map((e) => e.type)).toEqual(['defuse', 'round_end']);
    expect(sim.state().winner).toBe('defense');
    expect(ev[1]).toMatchObject({ reason: 'defused' });
  });

  test('time-out goes to defense when the spike is not planted', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: duel });
    const ev = run(sim, 2000, () => []);
    expect(ev.find((e) => e.type === 'round_end')).toMatchObject({ winner: 'defense', reason: 'time' });
    expect(sim.state().phase).toBe('ended');
    expect(sim.state().tick).toBe(2000);
  });

  test('a dead carrier drops the spike and a nearby attacker picks it up', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: [...duel, { id: 'a2', team: 'attack', isBot: false }] });
    sim.step([input('d', { fire: { targetId: 'a', result: 'head' } })]);
    expect(sim.state().spike).toEqual({ status: 'carried', carrierId: 'a2' });
  });
});

test('determinism: same seed and inputs give identical states and events', () => {
  const agents = [
    ...['a1', 'a2', 'a3', 'a4', 'a5'].map((id) => ({ id, team: 'attack' as const, isBot: id !== 'a1' })),
    ...['d1', 'd2', 'd3', 'd4', 'd5'].map((id) => ({ id, team: 'defense' as const, isBot: true })),
  ];
  const script = (t: number): PlayerInput[] =>
    agents.map((a, i) =>
      input(a.id, {
        move: { x: Math.cos(t / 7 + i), y: Math.sin(t / 5 + i) },
        yaw: t / 10,
        walk: t % 40 > 30,
        fire: t % 17 === i ? { targetId: agents[(i + 5) % 10].id, result: i % 2 ? 'body' : 'head' } : null,
        smokeAt: t === 10 + i ? { x: 20 + i, y: 20 } : null,
        plantHeld: t > 50,
      }),
    );
  const play = () => {
    const sim = createSim({ map: testBoxMap, seed: 42, agents });
    const states: unknown[] = [];
    const events: unknown[] = [];
    for (let t = 0; t < 200; t++) {
      const r = sim.step(script(t));
      states.push(r.state);
      events.push(r.events);
    }
    return { states, events };
  };
  const a = play();
  const b = play();
  expect(JSON.stringify(a)).toBe(JSON.stringify(b));
  expect(a.states).toEqual(b.states);
  expect(a.events.flat().length).toBeGreaterThan(0);
});

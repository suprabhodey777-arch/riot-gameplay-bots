import { describe, expect, test } from 'vitest';
import type { PlayerInput } from '../shared/contract.ts';
import { testBoxMap } from '../shared/maps/index.ts';
import { createSim, type CreateSimOptions, type Scenario } from './sim.ts';
import { input } from './test-util.ts';

const agents: CreateSimOptions['agents'] = [
  { id: 'a1', team: 'attack', isBot: false },
  { id: 'a2', team: 'attack', isBot: true },
  { id: 'd1', team: 'defense', isBot: true },
  { id: 'd2', team: 'defense', isBot: true },
];

/** A fixed script that moves, smokes, fires, plants and runs the clock out. */
function scripted(scenario?: Scenario): string {
  const sim = createSim({ map: testBoxMap, seed: 7, agents, scenario });
  let h1 = 0x811c9dc5;
  let h2 = 0;
  for (let t = 0; t < 2200; t++) {
    const ins: PlayerInput[] = [
      input('a1', { move: { x: 1, y: t % 40 < 20 ? 0.3 : -0.3 }, plantHeld: t > 600, smokeAt: t === 5 ? { x: 20, y: 20 } : null }),
      input('a2', { move: { x: 0.5, y: 0.5 } }),
      input('d1', { move: { x: -1, y: 0 }, fire: t % 50 === 0 ? { targetId: 'a1', result: 'body' } : null }),
      input('d2', { move: { x: -0.2, y: 0 }, defuseHeld: true }),
    ];
    const r = sim.step(ins);
    const str = JSON.stringify(r);
    for (let i = 0; i < str.length; i++) {
      h1 = Math.imul(h1 ^ str.charCodeAt(i), 0x01000193) >>> 0;
      h2 = (Math.imul(h2, 31) + str.charCodeAt(i)) >>> 0;
    }
  }
  return `${h1.toString(16)}-${h2.toString(16)}`;
}

describe('scenario', () => {
  test('omitting a scenario and empty scenarios have the same deterministic v4 replay', () => {
    // v4 digest includes agent kits, status timers and footstep events.
    expect(scripted()).toBe('579372ba-ae2bc791');
    expect(scripted({})).toBe(scripted());
    expect(scripted({ agents: {} })).toBe(scripted());
  });

  test('per-agent spawn, hp, yaw', () => {
    const sim = createSim({
      map: testBoxMap,
      seed: 1,
      agents,
      scenario: { agents: { a1: { spawn: { x: 30, y: 8 }, hp: 35, yaw: 1.5 } } },
    });
    const a1 = sim.state().agents.find((a) => a.id === 'a1')!;
    expect(a1.pos).toEqual({ x: 30, y: 8 });
    expect(a1.hp).toBe(35);
    expect(a1.yaw).toBe(1.5);
    // The next attacker still gets the first map spawn.
    expect(sim.state().agents.find((a) => a.id === 'a2')!.pos).toEqual(testBoxMap.spawns.attack[0]);
    // hp 35 means one body hit (40) kills.
    const sim2 = createSim({ map: testBoxMap, seed: 1, agents: agents.slice(0, 3), scenario: { agents: { d1: { hp: 35 }, a1: { spawn: { x: 50, y: 20 } }, d1x: {} } } });
    const r = sim2.step([input('a1', { fire: { targetId: 'd1', result: 'body' } })]);
    expect(r.events.some((e) => e.type === 'kill')).toBe(true);
  });

  test('carryingSpike moves the spike to the named agent, or to nobody', () => {
    const named = createSim({ map: testBoxMap, seed: 1, agents, scenario: { agents: { a2: { carryingSpike: true } } } });
    expect(named.state().spike).toEqual({ status: 'carried', carrierId: 'a2' });
    expect(named.state().agents.find((a) => a.id === 'a1')!.carryingSpike).toBe(false);
    const skipped = createSim({ map: testBoxMap, seed: 1, agents, scenario: { agents: { a1: { carryingSpike: false } } } });
    expect(skipped.state().spike).toEqual({ status: 'carried', carrierId: 'a2' });
  });

  test('roundTimeLeftS sets the clock and the round ends on time', () => {
    const sim = createSim({ map: testBoxMap, seed: 1, agents, scenario: { roundTimeLeftS: 5 } });
    expect(sim.state().roundTimeLeftS).toBe(5);
    let last = sim.step([]);
    expect(last.state.roundTimeLeftS).toBeCloseTo(4.95);
    for (let i = 0; i < 99; i++) last = sim.step([]);
    expect(last.state.phase).toBe('ended');
    expect(last.state.winner).toBe('defense');
    expect(last.events.at(-1)).toMatchObject({ type: 'round_end', reason: 'time', tick: 100 });
  });

  test('spikePlanted starts post_plant with the right detonation time', () => {
    const pos = { x: 50, y: 32 };
    const sim = createSim({
      map: testBoxMap,
      seed: 1,
      agents,
      scenario: { spikePlanted: { site: 'A', pos, secondsLeft: 10 } },
    });
    const st = sim.state();
    expect(st.phase).toBe('post_plant');
    expect(st.roundTimeLeftS).toBe(10);
    expect(st.spike).toMatchObject({ status: 'planted', site: 'A', pos });
    expect(st.agents.some((a) => a.carryingSpike)).toBe(false);
    let r = sim.step([]);
    expect(r.state.roundTimeLeftS).toBeCloseTo(9.95);
    for (let i = 0; i < 198; i++) r = sim.step([]);
    expect(r.state.phase).toBe('post_plant');
    r = sim.step([]);
    expect(r.state.phase).toBe('ended');
    expect(r.events.at(-1)).toMatchObject({ type: 'round_end', reason: 'detonated', tick: 200 });
  });

  test('a planted spike can be defused from the start', () => {
    const pos = { x: 50, y: 32 };
    const sim = createSim({
      map: testBoxMap,
      seed: 1,
      agents: agents.filter((a) => a.id !== 'a2'),
      scenario: { spikePlanted: { site: 'A', pos, secondsLeft: 20 }, agents: { d1: { spawn: { x: 49, y: 32 } } } },
    });
    let last = sim.step([]);
    for (let i = 0; i < 140; i++) last = sim.step([input('d1', { defuseHeld: true })]);
    expect(last.state.winner).toBe('defense');
    expect(last.events.some((e) => e.type === 'defuse')).toBe(true);
  });

  test('attackers all dead does not end a planted round', () => {
    const sim = createSim({
      map: testBoxMap,
      seed: 1,
      agents: [agents[0], agents[2]],
      scenario: { spikePlanted: { site: 'B', pos: { x: 50, y: 8 }, secondsLeft: 30 }, agents: { a1: { hp: 1, spawn: { x: 20, y: 20 } }, d1: { spawn: { x: 21, y: 20 } } } },
    });
    const r = sim.step([input('d1', { fire: { targetId: 'a1', result: 'body' } })]);
    expect(r.state.agents[0].alive).toBe(false);
    expect(r.state.phase).toBe('post_plant');
  });
});

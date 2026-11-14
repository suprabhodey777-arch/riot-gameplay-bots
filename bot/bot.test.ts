import { describe, expect, test } from 'vitest';
import type { SimEvent, SimState } from '../shared/contract.ts';
import { testBoxMap } from '../shared/maps/index.ts';
import { createSim } from '../sim/sim.ts';
import { arena, duel, input, wallBetween } from '../sim/test-util.ts';
import { createMatch, runHeadless } from './match.ts';
import { ARCHETYPES, archetypeById, DEFAULT_TRAITS } from './traits.ts';
import { botView, createMemory, FOV_RAD } from './view.ts';

declare const console: { log(...a: unknown[]): void };

const viewOf = (state: SimState, map = arena(), memory = createMemory()) => botView(state, 'a', map, memory);

describe('fog of war', () => {
  test('an enemy in the open in front of the bot is visible', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: duel });
    expect(viewOf(sim.state()).visible.map((v) => v.id)).toEqual(['d']);
  });

  test('an enemy behind a wall is not in view and its position is not remembered', () => {
    const map = arena({ walls: wallBetween });
    const sim = createSim({ map, seed: 1, agents: duel });
    const memory = createMemory();
    const v = viewOf(sim.state(), map, memory);
    expect(v.visible).toEqual([]);
    expect(v.remembered).toEqual([]);
    expect(memory.lastSeen).toEqual({});
  });

  test('a smoke between them hides the enemy', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: duel });
    const { state } = sim.step([input('a', { smokeAt: { x: 10, y: 10 } })]);
    expect(state.smokes).toHaveLength(1);
    expect(viewOf(state).visible).toEqual([]);
  });

  test('an enemy behind the bot, outside its field of view and farther than 3 m, is not in view', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: duel });
    const { state } = sim.step([input('a', { yaw: Math.PI })]); // faces away from the enemy
    expect(viewOf(state).visible).toEqual([]);
    // The edge of the 103 degree cone: just inside is seen, just outside is not.
    const inside = sim.step([input('a', { yaw: FOV_RAD / 2 - 0.05 })]).state;
    expect(viewOf(inside, arena()).visible).toHaveLength(1);
    const outside = sim.step([input('a', { yaw: FOV_RAD / 2 + 0.05 })]).state;
    expect(viewOf(outside).visible).toEqual([]);
  });

  test('within 3 m an enemy is heard even from behind', () => {
    const map = arena({ defenseAt: { x: 4, y: 10 } });
    const sim = createSim({ map, seed: 1, agents: duel });
    const { state } = sim.step([input('a', { yaw: Math.PI })]);
    expect(viewOf(state, map).visible.map((v) => v.id)).toEqual(['d']);
  });

  test('last seen positions are remembered with their tick once the enemy leaves view', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: duel });
    const memory = createMemory();
    const first = viewOf(sim.state(), arena(), memory);
    expect(first.visible).toHaveLength(1);
    const { state } = sim.step([input('a', { yaw: Math.PI })]);
    const v = viewOf(state, arena(), memory);
    expect(v.visible).toEqual([]);
    expect(v.remembered).toEqual([{ id: 'd', pos: { x: 18, y: 10 }, tick: 0 }]);
  });

  test('the bot never reads the position of an enemy it cannot see: its inputs ignore a hidden enemy', () => {
    // Same bot, same map, enemy hidden behind a wall in two different places: identical decisions.
    const map = arena({ walls: wallBetween });
    const run = (defenseAt: { x: number; y: number }) => {
      const m = createMatch({ map: { ...map, spawns: { ...map.spawns, defense: [defenseAt] } }, seed: 3, agents: [{ id: 'a', team: 'attack', isBot: true }, { id: 'd', team: 'defense', isBot: false }] });
      const out: unknown[] = [];
      for (let i = 0; i < 40; i++) {
        const r = m.step([]);
        out.push(r.trace, m.state().agents[0].pos);
      }
      return JSON.stringify(out);
    };
    expect(run({ x: 18, y: 5 })).toBe(run({ x: 18, y: 15 }));
  });
});

const bothBots = (a: string, d: string, rt?: { a?: number; d?: number }) => [
  { id: 'a', team: 'attack' as const, isBot: true, traits: { ...archetypeById(a).traits, ...(rt?.a ? { reactionMs: rt.a } : {}) } },
  { id: 'd', team: 'defense' as const, isBot: true, traits: { ...archetypeById(d).traits, ...(rt?.d ? { reactionMs: rt.d } : {}) } },
];

describe('reaction time', () => {
  test('no shot lands earlier than reactionMs after first sight', () => {
    const reactionMs = 600; // 12 ticks
    let damage = 0;
    for (let seed = 1; seed <= 10; seed++) {
      const m = createMatch({ map: testBoxMap, seed, agents: bothBots('entry-duelist', 'baseline', { a: reactionMs, d: reactionMs }) });
      const r = runHeadless(m, 2000);
      const spotted = new Map<string, number>();
      for (const e of r.events) if (e.type === 'spotted') spotted.set(`${e.observer}>${e.target}`, e.tick);
      for (const e of r.events) {
        if (e.type !== 'damage') continue;
        damage++;
        // The sim spots in 360 degrees, so this is a conservative lower bound on first sight.
        expect(e.tick - (spotted.get(`${e.attacker}>${e.victim}`) as number)).toBeGreaterThanOrEqual(reactionMs / 50);
      }
      // The trace agrees: a "firing" entry is never the first thing after seeing.
      for (const t of r.trace) if (t.node === 'engage' && t.reason.includes('firing')) expect(t.reason).toContain(`reaction ${reactionMs} ms elapsed`);
    }
    expect(damage).toBeGreaterThan(0);
  });
});

describe('determinism', () => {
  test('same seed gives identical states, events and traces over 600 ticks', () => {
    const agents = [
      { id: 'a1', team: 'attack' as const, isBot: true, traits: archetypeById('entry-duelist').traits },
      { id: 'a2', team: 'attack' as const, isBot: true, traits: archetypeById('utility-controller').traits },
      { id: 'd1', team: 'defense' as const, isBot: true, traits: archetypeById('off-angle-anchor').traits },
      { id: 'd2', team: 'defense' as const, isBot: true, traits: archetypeById('patient-lurker').traits },
    ];
    const run = (seed: number) => {
      const r = runHeadless(createMatch({ map: testBoxMap, seed, agents }), 600, true);
      return JSON.stringify([r.states, r.events, r.trace]);
    };
    expect(run(7)).toBe(run(7));
    expect(run(7)).not.toBe(run(8));
  });
});

describe('rounds', () => {
  test('a 1v1 bot round on test-box ends with a round_end within the round time', () => {
    for (let seed = 1; seed <= 6; seed++) {
      const r = runHeadless(createMatch({ map: testBoxMap, seed, agents: bothBots('baseline', 'baseline') }), 2000);
      const end = r.events.find((e): e is Extract<SimEvent, { type: 'round_end' }> => e.type === 'round_end');
      expect(end, `seed ${seed}`).toBeDefined();
      expect(end?.tick).toBeLessThanOrEqual(2000);
    }
  });

  test('every behaviour node appears across archetype rounds, with readable reasons', () => {
    const nodes = new Set<string>();
    for (const atk of ARCHETYPES) {
      for (const dfn of ARCHETYPES) {
        for (let seed = 1; seed <= 3; seed++) {
          const r = runHeadless(createMatch({ map: testBoxMap, seed, agents: bothBots(atk.id, dfn.id) }), 2400);
          for (const t of r.trace) {
            nodes.add(t.node);
            expect(t.reason.length).toBeGreaterThan(8);
            expect(t.traits.reactionMs).toBeGreaterThan(0);
          }
          // Trace stays readable: far fewer entries than ticks.
          expect(r.trace.length).toBeLessThan(r.ticks / 4 + 10);
        }
      }
    }
    for (const n of ['engage', 'hold_angle', 'push', 'lurk', 'plant', 'defuse', 'smoke', 'investigate']) expect(nodes.has(n), n).toBe(true);
  });
});

describe('circuit breaker', () => {
  const agents = bothBots('baseline', 'baseline');

  test('a frozen bot trips the breaker, goes to baseline and recovers', () => {
    const m = createMatch({ map: testBoxMap, seed: 2, agents: [{ ...agents[0], traits: archetypeById('entry-duelist').traits }, agents[1]] });
    m.injectFault('a', 'freeze');
    const start = m.state().agents[0].pos;
    let trip: { tick: number; reason: string; traits: Record<string, number> } | undefined;
    for (let i = 0; i < 400 && !trip; i++) {
      const r = m.step([]);
      const t = r.trace.find((x) => x.node === 'circuit_breaker');
      if (t) trip = t;
    }
    expect(trip).toBeDefined();
    expect(trip?.reason).toMatch(/entropy|stuck/);
    expect(trip?.traits.reactionMs).toBe(DEFAULT_TRAITS.reactionMs);
    expect(trip?.traits.aggression).toBe(DEFAULT_TRAITS.aggression);
    expect(Math.hypot(m.state().agents[0].pos.x - start.x, m.state().agents[0].pos.y - start.y)).toBeLessThan(0.5);
    for (let i = 0; i < 100; i++) m.step([]);
    const p = m.state().agents[0].pos;
    expect(Math.hypot(p.x - start.x, p.y - start.y)).toBeGreaterThan(5);
    expect(m.tripped().a).toBeDefined();
  });

  test('spam (NaN yaw, move length over 1) trips at once and never reaches the sim', () => {
    const m = createMatch({ map: testBoxMap, seed: 2, agents });
    m.injectFault('a', 'spam');
    const r = m.step([]);
    expect(r.trace.find((t) => t.node === 'circuit_breaker')?.reason).toMatch(/invalid input/);
    for (const a of r.state.agents) {
      expect(Number.isFinite(a.pos.x + a.pos.y + a.yaw)).toBe(true);
    }
  });

  test('healthy bots never trip the breaker', () => {
    for (const atk of ARCHETYPES) {
      for (let seed = 1; seed <= 3; seed++) {
        const r = runHeadless(createMatch({ map: testBoxMap, seed, agents: bothBots(atk.id, 'off-angle-anchor') }), 2400);
        expect(r.trace.filter((t) => t.node === 'circuit_breaker')).toEqual([]);
      }
    }
  });
});

describe('archetypes', () => {
  // Keep the defender stationary so its new loud footsteps do not turn this
  // approach-speed comparison into a noise-investigation test.
  const firstContact = (atk: string, seed: number) => {
    const r = runHeadless(createMatch({ map: testBoxMap, seed, agents: bothBots(atk, 'baseline').map((a, i) => i === 1 ? { ...a, isBot: false } : a) }), 2400);
    return r.trace.find((t) => t.node === 'engage')?.tick ?? 2400;
  };
  const mean = (atk: string) => {
    let s = 0;
    for (let seed = 1; seed <= 20; seed++) s += firstContact(atk, seed);
    return s / 20;
  };

  test('entry-duelist makes first contact earlier than patient-lurker (mean over 20 seeds)', () => {
    const duelist = mean('entry-duelist');
    const lurker = mean('patient-lurker');
    const baseline = mean('baseline');
    console.log(`first contact, mean ticks over 20 seeds: entry-duelist ${duelist.toFixed(0)}, baseline ${baseline.toFixed(0)}, patient-lurker ${lurker.toFixed(0)}`);
    expect(duelist).toBeLessThan(baseline);
    expect(baseline).toBeLessThan(lurker);
    expect(duelist).toBeLessThan(lurker / 2);
  });

  test('the off-angle anchor holds a different spot from the baseline defender', () => {
    const spot = (id: string) => {
      const r = runHeadless(createMatch({ map: testBoxMap, seed: 4, agents: bothBots('baseline', id) }), 60);
      return r.trace.find((t) => t.botId === 'd' && t.node === 'hold_angle')?.reason ?? '';
    };
    expect(spot('off-angle-anchor')).toContain('off-angle');
    expect(spot('baseline')).toContain('common');
  });
});

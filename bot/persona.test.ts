import { describe, expect, it } from 'vitest';
import { PERSONAS, PERSONA_TRAITS } from '../shared/personas.ts';
import type { DrillSpec } from '../shared/coaching.ts';
import { registerMap } from '../shared/maps/index.ts';
import { createRng } from '../sim/rng.ts';
import { createSim } from '../sim/sim.ts';
import { arena, wallBetween } from '../sim/test-util.ts';
import { createBot } from './bot.ts';
import { createMemory, botView } from './view.ts';
import { createPersonaMoves } from './persona.ts';
import { startDrill } from '../session/drill-run.ts';
import { fairnessHook } from '../evidence/run.ts';
import { createSimulatedPlayer, profileWithWeakness, traitsForSkill } from '../session/simulated-player.ts';

const map = arena();
const sim = () => createSim({ map, seed: 1, scenario: { agents: { mate: { spawn: { x: 6, y: 10 } } } }, agents: [
  { id: 'p', team: 'attack', isBot: true, agent: 'rook' },
  { id: 'mate', team: 'attack', isBot: false },
  { id: 'e', team: 'defense', isBot: false },
  { id: 'e2', team: 'defense', isBot: false },
] });
const base = { node: 'push', target: 'site', reason: 'entering', move: { x: 1, y: 0 }, fire: null };

describe('persona signatures in purpose-built scenarios', () => {
  it('Zephyr jiggles a held angle then swings at full run and re-peeks a kill', () => {
    const state = sim().state(); const memory = createMemory();
    const move = createPersonaMoves('zephyr', map, 'p');
    const first = move(botView(state, 'p', map, memory), [], base);
    expect(first.node).toBe('jigglePeek'); expect(first.fire).toBeNull(); expect(first.walk).toBe(false);
    state.tick = 4;
    const swing = move(botView(state, 'p', map, memory), [], base);
    expect(swing.node).toBe('wideSwing'); expect(swing.walk).toBe(false); expect(Math.hypot(swing.move!.x, swing.move!.y)).toBeCloseTo(1);
    const rePeek = move(botView(state, 'p', map, memory), [{ type: 'kill', tick: 4, killer: 'p', victim: 'e2', part: 'head', killerPos: state.agents[0].pos, victimPos: state.agents[3].pos }], base);
    expect(rePeek.node).toBe('wideSwing');
  });
  it('Atlas waits for support, smokes the foothold and trades a fallen teammate within 1.5 s', () => {
    const state = sim().state(); state.agents[0].yaw = Math.PI; state.agents[0].smokesLeft = 3;
    const move = createPersonaMoves('atlas', map, 'p'); const memory = createMemory();
    expect(move(botView(state, 'p', map, memory), [], base).node).toBe('teamWait');
    state.agents[1].pos = { x: 4, y: 10 };
    expect(move(botView(state, 'p', map, memory), [], base).smokeAt).toBeTruthy();
    state.tick = 20; state.agents[1].alive = false;
    const traded = move(botView(state, 'p', map, memory), [{ type: 'kill', tick: 20, killer: 'e', victim: 'mate', part: 'body', killerPos: { x: 18, y: 10 }, victimPos: { x: 6, y: 10 } }], base);
    expect(traded.node).toBe('trade'); expect(traded.move!.x).toBeGreaterThan(0);
    state.tick = 51;
    expect(move(botView(state, 'p', map, memory), [], base).node).not.toBe('trade');
  });
  it('Ember falls back to a fresh perch after every shot', () => {
    const state = sim().state(); const memory = createMemory(); const move = createPersonaMoves('ember', map, 'p');
    const shot = { ...base, fire: { targetId: 'e', result: 'miss' as const } };
    expect(move(botView(state, 'p', map, memory), [], shot).fire).toEqual(shot.fire);
    state.tick++;
    const reposition = move(botView(state, 'p', map, memory), [], shot);
    expect(reposition.node).toBe('reposition'); expect(reposition.fire).toBeNull(); expect(reposition.move).not.toEqual({ x: 0, y: 0 });
  });
  it('Wisp fakes noise, waits silently, then walks the flank on a team push', () => {
    const state = sim().state(); state.agents[0].yaw = Math.PI;
    const memory = createMemory(); const move = createPersonaMoves('wisp', map, 'p');
    expect(move(botView(state, 'p', map, memory), [], base).walk).toBe(false);
    state.tick = 6;
    const wait = move(botView(state, 'p', map, memory), [], base);
    expect(wait.node).toBe('lurkWait'); expect(wait.move).toEqual({ x: 0, y: 0 }); expect(wait.walk).toBe(true);
    state.agents[1].pos = { x: 4, y: 10 };
    const flank = move(botView(state, 'p', map, memory), [], base);
    expect(flank.walk).toBe(true); expect(flank.move).not.toEqual({ x: 0, y: 0 });
  });
  it('Nova recons before the push, pop flashes for a teammate and plays second', () => {
    const state = sim().state(); state.agents[0].yaw = Math.PI; state.agents[0].agent = 'wren'; state.agents[0].abilities = { smoke: 1, recon: 1, flash: 1, dash: 0 };
    const memory = createMemory(); const move = createPersonaMoves('nova', map, 'p');
    const recon = move(botView(state, 'p', map, memory), [], base);
    expect(recon.node).toBe('reconPush'); expect(recon.reconAt).toBeTruthy();
    state.agents[0].abilities.recon = 0; state.tick = 10;
    const flash = move(botView(state, 'p', map, memory), [], base);
    expect(flash.node).toBe('popFlash'); expect(flash.flashAt).toBeTruthy();
    state.agents[0].abilities.flash = 0; state.tick++;
    expect(move(botView(state, 'p', map, memory), [], base).node).toBe('secondEntry');
  });
});

it.each(PERSONAS)('$name cannot learn hidden enemy positions', p => {
  const run = (y: number) => {
    const hiddenMap = arena({ walls: wallBetween });
    const state = createSim({ map: hiddenMap, seed: 1, agents: [{ id: 'p', team: 'attack', isBot: true, agent: p.agent }, { id: 'e', team: 'defense', isBot: false }] }).state();
    state.agents[1].pos.y = y;
    const bot = createBot({ id: 'p', map: hiddenMap, traits: PERSONA_TRAITS[p.id], archetypeId: p.id, rng: createRng(3) });
    return Array.from({ length: 30 }, (_, i) => { state.tick = i; return bot.decide(state); });
  };
  expect(run(5)).toEqual(run(15));
});

it.each(PERSONAS)('$name obeys the evidence fairness checker across 20 seeded duels', p => {
  const duelMap = registerMap({ ...arena(), id: 'persona-fairness-arena' });
  const fairness = { fires: 0, tooEarly: 0 };
  const learner = createSimulatedPlayer({ seed: 1, profile: profileWithWeakness('clutch', .7) });
  const check = fairnessHook(fairness, () => learner);
  for (let seed = 1; seed <= 20; seed++) {
    const spec: DrillSpec = { id: 'persona-duel', seed, mapId: duelMap.id, skill: 'clutch', difficulty: .3, title: 'Duel', goal: 'Win', player: { id: 'player', team: 'attack', spawn: { x: 2, y: 10 }, hp: 1000, carryingSpike: false }, bots: [{ id: 'enemy', team: 'defense', spawn: { x: 18, y: 10 }, archetypeId: p.id, traits: PERSONA_TRAITS[p.id] }], roundTimeLeftS: 20, spikePlanted: null };
    const run = startDrill(spec);
    const opponent = createBot({ id: 'player', map: duelMap, traits: traitsForSkill('clutch', .7), rng: createRng(seed), teamPlay: false });
    for (let tick = 0; tick < 400 && !run.done(); tick++) {
      const before = run.state(); const input = opponent.decide(before).input; run.step(input); check(run, before, input);
    }
  }
  expect(fairness.fires).toBeGreaterThan(0); expect(fairness.tooEarly).toBe(0);
});

it('Ember actually fires from different positions over a sustained fight', () => {
  const sim = createSim({ map, seed: 1, agents: [{ id: 'p', team: 'attack', isBot: true, agent: 'pike', loadout: { primary: 'longshot', secondary: 'hammer', armor: null, credits: 0 } }, { id: 'e', team: 'defense', isBot: false }], scenario: { agents: { p: { carryingSpike: false }, e: { hp: 10000 } }, roundTimeLeftS: 60 } });
  const bot = createBot({ id: 'p', map, archetypeId: 'ember', traits: PERSONA_TRAITS.ember, rng: createRng(1) });
  const origins: string[] = [];
  let repositions = 0;
  for (let tick = 0; tick < 1000 && sim.state().phase !== 'ended'; tick++) {
    const before = sim.state(); const decision = bot.decide(before);
    if (decision.input.fire) origins.push(`${before.agents[0].pos.x.toFixed(1)},${before.agents[0].pos.y.toFixed(1)}`);
    if (decision.node === 'reposition') repositions++;
    sim.step([decision.input]);
  }
  expect(origins.length).toBeGreaterThan(3); expect(new Set(origins).size).toBe(origins.length); expect(repositions).toBeGreaterThan(10);
});

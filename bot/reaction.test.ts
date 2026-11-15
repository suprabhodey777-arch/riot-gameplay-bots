import { expect, test } from 'vitest';
import { testBoxMap } from '../shared/maps/index.ts';
import { createMatch } from './match.ts';
import { archetypeById } from './traits.ts';
import { REACTION_JITTER_MS, TURN_RATE_DEG_S } from './bot.ts';
import type { SimEvent, SimState, TraceEntry } from '../shared/contract.ts';

const MAX_TICKS = 1200;
const SEEDS = Array.from({ length: 50 }, (_, i) => i + 1);

/** One seeded 1v1 encounter on the box map, with every intermediate state. */
function encounter(seed: number) {
  const agents = [
    { id: 'a', team: 'attack' as const, isBot: true, archetypeId: 'entry-duelist' },
    { id: 'd', team: 'defense' as const, isBot: true, archetypeId: 'baseline' },
  ];
  const match = createMatch({ map: testBoxMap, seed, agents });
  const initial = match.state();
  const events: SimEvent[] = [];
  const trace: TraceEntry[] = [];
  const byTick = new Map<number, SimState>([[initial.tick, initial]]);
  for (let i = 0; i < MAX_TICKS; i++) {
    if (match.state().phase === 'ended') break;
    const r = match.step([]);
    events.push(...r.events);
    trace.push(...r.trace);
    byTick.set(r.state.tick, r.state);
  }
  return { events, trace, byTick };
}

/** Tick of the first `spotted` event per observer, and of the first firing decision per bot. */
function firstContacts(events: SimEvent[], trace: TraceEntry[]) {
  const spotted = new Map<string, number>();
  for (const e of events) if (e.type === 'spotted' && !spotted.has(e.observer)) spotted.set(e.observer, e.tick);
  const firing = new Map<string, number>();
  for (const t of trace) if (t.node === 'engage' && t.reason.includes('firing') && !firing.has(t.botId)) firing.set(t.botId, t.tick);
  return { spotted, firing };
}

const MS_PER_TICK = 50;

test('no shot is issued earlier than reactionMs minus the jitter window after first sight', () => {
  let encounters = 0;
  let shots = 0;
  for (const seed of SEEDS) {
    const { events, trace } = encounter(seed);
    const { spotted, firing } = firstContacts(events, trace);
    for (const [botId, tick] of firing) {
      const seen = spotted.get(botId);
      if (seen === undefined) continue;
      encounters++;
      shots++;
      const reactionMs = archetypeById(botId === 'a' ? 'entry-duelist' : 'baseline').traits.reactionMs;
      // The sim spots in 360 degrees, so `seen` is never later than the bot's own first look.
      expect((tick - seen) * MS_PER_TICK, `seed ${seed}, bot ${botId}`).toBeGreaterThanOrEqual(reactionMs - REACTION_JITTER_MS);
      expect(tick, `seed ${seed}, bot ${botId}`).toBeGreaterThan(seen);
    }
  }
  // The scenarios must actually produce encounters, or the check proves nothing.
  expect(encounters).toBeGreaterThan(0);
  expect(shots).toBeGreaterThan(0);
});

test('yaw never turns faster than 540 deg/s while engaging a target', () => {
  const maxPerTick = (TURN_RATE_DEG_S * Math.PI / 180) / 20;
  let samples = 0;
  for (const seed of SEEDS) {
    const { trace, byTick } = encounter(seed);
    for (const t of trace) {
      if (t.node !== 'engage') continue;
      const before = byTick.get(t.tick);
      const after = byTick.get(t.tick + 1);
      if (!before || !after) continue;
      const a = before.agents.find((x) => x.id === t.botId);
      const b = after.agents.find((x) => x.id === t.botId);
      if (!a || !b) continue;
      const delta = Math.abs(Math.atan2(Math.sin(b.yaw - a.yaw), Math.cos(b.yaw - a.yaw)));
      samples++;
      expect(delta, `seed ${seed}, bot ${t.botId}, tick ${t.tick}`).toBeLessThanOrEqual(maxPerTick + 1e-9);
    }
  }
  expect(samples).toBeGreaterThan(0);
});

test('the first shot is never on the spotted tick', () => {
  let checked = 0;
  for (const seed of SEEDS) {
    const { events, trace } = encounter(seed);
    const { spotted, firing } = firstContacts(events, trace);
    for (const [botId, tick] of firing) {
      const seen = spotted.get(botId);
      if (seen === undefined) continue;
      checked++;
      expect(tick, `seed ${seed}, bot ${botId}`).toBeGreaterThan(seen);
    }
  }
  expect(checked).toBeGreaterThan(0);
});

import { expect, it } from 'vitest';
import { StreamerOverlay, CHAT_LIMIT, CHAT_WINDOW_TICKS } from './streamer.ts';
import type { StreamerMessage } from './protocol.ts';
import type { SimEvent, SimState } from '../shared/contract.ts';
import { createSim } from '../sim/sim.ts';
import { arena } from '../sim/test-util.ts';

const state = () => createSim({ map: arena(), seed: 1, agents: [
  { id: 'p', team: 'attack', isBot: false }, { id: 'mate', team: 'attack', isBot: false },
  ...Array.from({ length: 5 }, (_, i) => ({ id: `e${i}`, team: 'defense' as const, isBot: true })),
] }).state();
const kill = (victim: string, killer = 'p'): SimEvent => ({ type: 'kill', tick: 1, killer, victim, part: 'body', killerPos: { x: 2, y: 10 }, victimPos: { x: 18, y: 10 } });
const chat = (messages: StreamerMessage[]) => messages.filter(m => m.type === 'chat').map(m => m.text);
function drain(overlay: StreamerOverlay, before: SimState, after: SimState, events: SimEvent[]) {
  const output = overlay.observe(before, after, events, 'p');
  for (let tick = 1; tick < 500; tick++) { const next = { ...after, tick }; output.push(...overlay.observe(next, next, [], 'p')); }
  return output;
}
it('reacts to actual first blood, 2K, 3K and ACE with matching chat and alerts', () => {
  const overlay = new StreamerOverlay(7); overlay.toggle(true);
  const before = state(), after = state(); after.tick = 1;
  for (const e of after.agents.filter(a => a.team === 'defense')) e.alive = false;
  const output = drain(overlay, before, after, Array.from({ length: 5 }, (_, i) => kill(`e${i}`)));
  for (const pattern of [/first blood/i, /2K/, /3K/, /ACE/]) expect(chat(output).some(t => pattern.test(t))).toBe(true);
  expect(output).toContainEqual({ type: 'alert', kind: 'ace', text: 'ACE', big: true });
  expect(output).toContainEqual({ type: 'alert', kind: 'first_blood', text: 'FIRST BLOOD', big: true });
});
it.each([true, false])('reacts to a real 1v3 clutch result (won=%s)', won => {
  const before = state(), after = state();
  before.agents[1].alive = false; after.agents[1].alive = false;
  for (const s of [before, after]) { s.agents[5].alive = false; s.agents[6].alive = false; }
  after.tick = 1;
  const overlay = new StreamerOverlay(8); overlay.toggle(true);
  const output = drain(overlay, before, after, [{ type: 'round_end', tick: 1, winner: won ? 'attack' : 'defense', reason: 'elimination' }]);
  expect(chat(output).some(t => t.includes(`clutch ${won ? 'won' : 'lost'}`))).toBe(true);
  expect(output).toContainEqual({ type: 'alert', kind: 'clutch', text: `CLUTCH 1v3 ${won ? 'WON' : 'LOST'}`, big: true });
});
it('is seed deterministic and rate limited even under sustained event bursts', () => {
  const run = (seed: number) => {
    const overlay = new StreamerOverlay(seed); overlay.toggle(true); const s = state();
    return Array.from({ length: 300 }, (_, tick) => {
      s.tick = tick;
      return overlay.observe(s, s, [kill(`e${tick % 5}`), { type: 'plant', tick, agent: 'p', site: 'A' }], 'p', { p: 'zephyr' }, { p: 'wideSwing' });
    });
  };
  const output = run(17); expect(run(17)).toEqual(output); expect(run(18)).not.toEqual(output);
  for (let tick = 0; tick < output.length; tick++) expect(output.slice(Math.max(0, tick - CHAT_WINDOW_TICKS + 1), tick + 1).flat().filter(m => m.type === 'chat').length).toBeLessThanOrEqual(CHAT_LIMIT);
  expect(output.flat().filter(m => m.type === 'chat').length).toBeGreaterThan(40);
});
it('emits original callouts with streamer off, and disables chat and alerts immediately', () => {
  const overlay = new StreamerOverlay(1), s = state();
  expect(overlay.observe(s, s, [kill('e0')], 'p', { p: 'zephyr' })).toEqual([{ type: 'persona_line', persona: 'zephyr', text: expect.any(String) }]);
  overlay.toggle(true); s.tick = 1; overlay.observe(s, s, [kill('e1')], 'p'); overlay.toggle(false);
  for (let i = 0; i < 50; i++) { s.tick++; expect(overlay.observe(s, s, [kill('e2')], 'p')).toEqual([]); }
});
it('reacts to headshots, plant, defuse, self flash, missed sprays, streaks and eco wins', () => {
  const before = state(), after = state(); after.tick = 1;
  before.agents.filter(a => a.team === 'attack').forEach(a => a.primary = null);
  const overlay = new StreamerOverlay(3); overlay.toggle(true);
  const events: SimEvent[] = [{ ...kill('e0'), part: 'head' } as SimEvent, { type: 'plant', tick: 1, agent: 'p', site: 'A' }, { type: 'defuse', tick: 1, agent: 'p' }, { type: 'blinded', tick: 1, agent: 'p', by: 'p', untilTick: 30 }, { type: 'round_end', tick: 1, winner: 'attack', reason: 'elimination' }];
  const output = overlay.observe(before, after, events, 'p');
  for (let tick = 1; tick < 8; tick++) { const beforeShot = { ...after, agents: after.agents.map(a => ({ ...a, ammo: { ...a.ammo } })) }; after.tick++; after.agents[0].ammo.lancer--; output.push(...overlay.observe(beforeShot, after, tick === 7 ? [{ type: 'round_end', tick: 8, winner: 'attack', reason: 'time' }] : [], 'p', {}, {}, { agentId: 'p', move: { x: 0, y: 0 }, yaw: 0, walk: false, crouch: false, fire: { targetId: 'e0', result: 'miss' }, smokeAt: null, plantHeld: false, defuseHeld: false })); }
  for (let tick = 8; tick < 500; tick++) { after.tick++; output.push(...overlay.observe(after, after, [], 'p')); }
  for (const pattern of [/headshot/i, /plant/, /defuse/, /self flash|flash lit/, /spray/, /2 round|2 rounds/, /eco win/i]) expect(chat(output).some(t => pattern.test(t))).toBe(true);
});

it('serializes each overlay message to the exact wire.4 field types and keys', () => {
  const overlay = new StreamerOverlay(9); overlay.toggle(true); const before = state(), after = state(); after.tick = 1;
  const output = overlay.observe(before, after, [kill('e0')], 'p', { p: 'zephyr' });
  expect(output.map(m => m.type).sort()).toEqual(['alert', 'chat', 'persona_line']);
  for (const raw of output) {
    const m = JSON.parse(JSON.stringify(raw));
    if (m.type === 'chat') { expect(Object.keys(m).sort()).toEqual(['badge', 'color', 'text', 'type', 'user']); expect(m).toEqual({ type: 'chat', user: expect.any(String), color: expect.any(String), text: expect.any(String), badge: m.badge }); expect([null, 'sub', 'mod']).toContain(m.badge); }
    if (m.type === 'alert') expect(m).toEqual({ type: 'alert', kind: 'first_blood', text: expect.any(String), big: expect.any(Boolean) });
    if (m.type === 'persona_line') expect(m).toEqual({ type: 'persona_line', persona: 'zephyr', text: expect.any(String) });
  }
});

it('does not call rejected fire inputs a whiffed spray', () => {
  const overlay = new StreamerOverlay(4); overlay.toggle(true); const s = state(); const output: StreamerMessage[] = [];
  for (let tick = 0; tick < 200; tick++) { s.tick = tick; output.push(...overlay.observe(s, s, [], 'p', {}, {}, { agentId: 'p', move: { x: 0, y: 0 }, yaw: 0, walk: false, crouch: false, fire: { targetId: 'e0', result: 'miss' }, smokeAt: null, plantHeld: false, defuseHeld: false })); }
  expect(chat(output).some(t => /spray/.test(t))).toBe(false);
});

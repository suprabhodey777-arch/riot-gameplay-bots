import type { Socket } from 'node:net';
import { setTimeout } from 'node:timers';
import { expect, it } from 'vitest';
import { serveBridgeConnection } from './server.ts';
import type { ClientMessage } from './protocol.ts';
import { PERSONAS } from '../shared/personas.ts';
import { idleInput } from '../session/drill-run.ts';

function connection() {
  let receive = (_: string) => {};
  const messages: Record<string, any>[] = [];
  const socket = { destroyed: false, writable: true, setEncoding() {}, on(event: string, listener: (s: string) => void) { if (event === 'data') receive = listener; }, write(s: string) { messages.push(JSON.parse(s)); return true; } };
  serveBridgeConnection(socket as unknown as Socket, { apiKey: null });
  return { async send(msg: unknown) { receive(JSON.stringify(msg) + '\n'); await new Promise<void>(resolve => setTimeout(() => resolve(), 0)); return messages.splice(0); } };
}
it('serializes the exact personas profile JSON shape from the NDJSON handler', async () => {
  const c = connection();
  expect((await c.send({ type: 'hello', protocol: 'bots-wire.4' }))[0].protocol).toBe('bots-wire.4');
  const [m] = await c.send({ type: 'personas' });
  expect(Object.keys(m).sort()).toEqual(['personas', 'type']); expect(m.type).toBe('personas'); expect(m.personas).toHaveLength(5);
  expect(m.personas.map((p: any) => p.id)).toEqual(['zephyr', 'atlas', 'ember', 'wisp', 'nova']);
  for (const p of m.personas) {
    expect(Object.keys(p).sort()).toEqual(['agent', 'beatTip', 'colors', 'id', 'lines', 'name', 'signature', 'style', 'tagline']);
    for (const key of ['agent', 'beatTip', 'id', 'name', 'signature', 'style', 'tagline']) expect(typeof p[key]).toBe('string');
    expect(p.lines).toHaveLength(3); expect(p.lines.every((l: unknown) => typeof l === 'string')).toBe(true);
    expect(p.colors).toHaveLength(2); expect(p.colors.every((l: string) => /^#[0-9a-f]{6}$/.test(l))).toBe(true);
  }
});
it('accepts the exact new client shapes, offers a selectable persona drill, and retains scalar ammo', async () => {
  const c = connection();
  const commands: ClientMessage[] = [{ type: 'personas' }, { type: 'streamer', on: true }, { type: 'duel', persona: 'zephyr', map: 'ironvale', site: 'A' }, { type: 'match', map: 'ironvale', agent: 'rook', enemyPersonas: ['zephyr', 'atlas', 'ember', 'wisp', 'nova'] }];
  expect(JSON.parse(JSON.stringify(commands))).toEqual([{ type: 'personas' }, { type: 'streamer', on: true }, { type: 'duel', persona: 'zephyr', map: 'ironvale', site: 'A' }, { type: 'match', map: 'ironvale', agent: 'rook', enemyPersonas: ['zephyr', 'atlas', 'ember', 'wisp', 'nova'] }]);
  await c.send(commands[1]);
  const start = await c.send(commands[2]);
  expect(start.map(m => m.type)).toEqual(['map', 'drill', 'step']); expect(start[2].state.agents).toHaveLength(2);
  // The client sets the round up from the drill message: the player, their side and the persona bot.
  expect(start[1]).toMatchObject({ playerTeam: 'attack', bots: [{ archetype: 'zephyr' }] });
  expect(start[1].state.agents.every((a: any) => typeof a.ammo === 'number' && typeof a.reserve === 'number')).toBe(true);
  expect((await c.send({ type: 'match', map: 'ironvale', agent: 'rook' }))[0].type).toBe('error');
  await c.send({ type: 'abandon' });
  const match = await c.send(commands[3]); expect(match.map(m => m.type)).toEqual(['map', 'buy_phase']);
  const round = await c.send({ type: 'ready' }); expect(round[0].state.agents.filter((a: any) => a.team === 'defense').map((a: any) => a.agent)).toEqual(PERSONAS.map(p => p.agent));
  await c.send({ type: 'abandon' });
  const [report] = await c.send({ type: 'load_history', sample: { skill: 'entry', seed: 11 } });
  expect(report.personaOffer).toEqual({ persona: 'ember', skill: 'entry', title: expect.any(String), beatTip: expect.any(String) });
  const drill = await c.send({ type: 'versus', archetype: report.personaOffer.persona, skill: report.personaOffer.skill });
  const spec = drill.find(m => m.type === 'drill')!;
  expect(spec.spec.bots.every((b: any) => b.archetypeId === 'ember')).toBe(true);
  expect(spec.intro).toContain(report.personaOffer.beatTip);
}, 60000);
it('rejects malformed new messages without replacing the live mode', async () => {
  const c = connection();
  for (const msg of [{ type: 'streamer', on: 1 }, { type: 'duel', persona: 'unknown', map: 'ironvale', site: 'A' }, { type: 'duel', persona: 'ember', map: 'ironvale', site: 'C' }, { type: 'match', agent: 'rook', map: 'ironvale', enemyPersonas: ['ember'] }, { type: 'match', agent: 'rook', map: 'ironvale', enemyPersonas: 'ember' }]) expect((await c.send(msg))[0].type).toBe('error');
  expect((await c.send({ type: 'duel', persona: 'ember', map: 'ironvale', site: 'B' })).map(m => m.type)).toEqual(['map', 'drill', 'step']);
  expect((await c.send({ type: 'input', input: idleInput('player') }))[0].state.tick).toBe(1);
});
it.each(['bots-wire.1', 'bots-wire.2', 'bots-wire.3'])('preserves %s and rejects v4-only commands', async protocol => {
  const c = connection();
  expect((await c.send({ type: 'hello', protocol }))[0].protocol).toBe(protocol);
  for (const msg of [{ type: 'personas' }, { type: 'streamer', on: true }, { type: 'duel', persona: 'wisp', map: 'ironvale', site: 'A' }]) expect((await c.send(msg))[0].type).toBe('error');
  if (protocol === 'bots-wire.3') { expect((await c.send({ type: 'match', map: 'ironvale', agent: 'rook' })).map(m => m.type)).toEqual(['map', 'buy_phase']); await c.send({ type: 'abandon' }); }
});

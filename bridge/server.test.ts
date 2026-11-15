import { connect, type Socket, type Server } from 'node:net';
import { once } from 'node:events';
import { createInterface } from 'node:readline';
import { afterEach, describe, expect, it } from 'vitest';
import { createBridgeServer, RULES } from './server.ts';
import { PROTOCOL, type ClientMessage, type ServerMessage } from './protocol.ts';
import { idleInput, startDrill } from '../session/drill-run.ts';
import { CoachingSession } from '../session/session.ts';
import { sampleHistory } from '../analyzer/synthetic.ts';
import { archetypeById } from '../bot/traits.ts';
import { createRecorder } from '../session/recorder.ts';
import { analyze } from '../analyzer/analyze.ts';
import { RUN_SPEED } from '../sim/sim.ts';

const servers: Server[] = [];
const sockets: Socket[] = [];
afterEach(async () => {
  for (const socket of sockets.splice(0)) socket.destroy();
  await Promise.all(servers.splice(0).filter((server) => server.listening).map((server) => new Promise<void>((resolve, reject) => server.close((err) => err ? reject(err) : resolve()))));
});
async function client(server?: Server) {
  if (!server) {
    server = createBridgeServer({ apiKey: null });
    servers.push(server);
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
  }
  const address = server.address();
  if (!address || typeof address === 'string') throw new Error('Missing TCP address');
  const socket = connect(address.port, '127.0.0.1');
  sockets.push(socket);
  const lines = createInterface({ input: socket, crlfDelay: Infinity })[Symbol.asyncIterator]();
  await once(socket, 'connect');
  return {
    socket, server,
    send: (msg: ClientMessage) => socket.write(JSON.stringify(msg) + '\n'),
    async next<T extends ServerMessage['type']>(type: T): Promise<Extract<ServerMessage, { type: T }>> {
      const line = await lines.next();
      if (line.done) throw new Error('Unexpected socket close');
      const msg = JSON.parse(line.value) as ServerMessage;
      expect(msg.type).toBe(type);
      return msg as Extract<ServerMessage, { type: T }>;
    },
  };
}
const load = { type: 'load_history', sample: { skill: 'entry', seed: 11 } } as const;

describe('Unity bridge TCP', () => {
  it('plays the sample drill, records it, then sends result, debrief and a fresh report in order', async () => {
    const c = await client();
    c.send({ type: 'hello', protocol: PROTOCOL });
    const hello = await c.next('hello');
    expect(hello.protocol).toBe(PROTOCOL);
    expect(hello.rules).toEqual(RULES);
    expect(hello.rules.moveSpeedMps).toBe(RUN_SPEED);
    c.send(load);
    const initialReport = await c.next('report');
    expect(initialReport.played).toBe(0);
    c.send({ type: 'next_drill' });
    const drill = await c.next('drill');
    const reference = startDrill(drill.spec);
    expect(drill.state).toEqual(reference.state());
    let done = false;
    for (let tick = 1; !done && tick <= reference.maxTicks; tick++) {
      c.send({ type: 'input', input: idleInput('ignored') });
      const step = await c.next('step');
      const expected = reference.step(idleInput(drill.playerId));
      expect(step.state).toEqual(expected.state);
      expect(step.events).toEqual(expected.events);
      expect(step.thoughts).toEqual(Object.fromEntries(expected.trace.map((t) => [t.botId, t.node])));
      expect(step.state.tick).toBe(tick);
      done = step.done;
    }
    expect(done).toBe(true);
    const result = await c.next('result');
    expect(result.result).toEqual(reference.finish());
    expect(result.success).toBe(result.result.success);
    const debrief = await c.next('debrief');
    expect(debrief.source).toBe('template');
    expect(debrief.text.length).toBeGreaterThan(0);
    const report = await c.next('report');
    expect(report.played).toBe(1);
    expect(report.staircases[drill.spec.skill].difficulty).toBe(result.staircaseAfter);
    const rec = createRecorder({ matchId: 'bridge-0', mapId: drill.spec.mapId, playerId: drill.playerId });
    rec.beginRound(drill.state);
    rec.push(result.result.events);
    rec.endRound(reference.state().winner ?? (result.success ? drill.playerTeam : drill.playerTeam === 'attack' ? 'defense' : 'attack'));
    expect(report.report).toEqual(analyze([...sampleHistory('entry', 11), rec.toLog()], drill.playerId));
    c.send({ type: 'input', input: idleInput(drill.playerId) });
    await c.next('error');
  }, 60000);

  it('rejects input without a live drill', async () => {
    const c = await client();
    c.send({ type: 'input', input: idleInput('player') });
    expect((await c.next('error')).message).toMatch(/No live drill/);
  }, 60000);

  it('recovers from malformed JSON, fragmented lines and multiple lines in one write', async () => {
    const c = await client();
    c.socket.write('{bad json}\n{"type":"hel');
    await c.next('error');
    c.socket.write('lo","protocol":"bots-wire.1"}\n{"type":"hello","protocol":"bots-wire.1"}\n');
    await c.next('hello');
    await c.next('hello');
    expect(c.socket.destroyed).toBe(false);
  }, 60000);

  it('starts versus with canonical traits on every bot and leaves staircases alone', async () => {
    const c = await client();
    c.send({ type: 'versus', archetype: 'entry-duelist', skill: 'clutch' });
    const drill = await c.next('drill');
    expect(drill.spec.bots.length).toBeGreaterThan(1);
    for (const bot of drill.spec.bots) {
      expect(bot.archetypeId).toBe('entry-duelist');
      expect(bot.traits).toEqual(archetypeById('entry-duelist').traits);
    }
    expect(drill.bots.every((b) => b.archetype === 'entry-duelist')).toBe(true);
    c.send({ type: 'abandon' });
    const result = await c.next('result');
    expect(result.success).toBe(false);
    expect(result.staircaseAfter).toBe(result.staircaseBefore);
    await c.next('debrief');
    expect((await c.next('report')).played).toBe(1);
  }, 60000);

  it('starts rehearsal index 0 and preserves the offered list across re-analysis', async () => {
    const c = await client();
    c.send(load);
    const report = await c.next('report');
    const session = CoachingSession.fromHistory(sampleHistory('entry', 11), 'me', { seed: 11, apiKey: null });
    const expected = session.rehearsals()[0];
    expect(report.rehearsals[0].title).toBe(expected.title);
    c.send({ type: 'rehearse', index: 0 });
    const drill = await c.next('drill');
    expect(drill.spec).toEqual({ ...expected, player: { ...expected.player, id: 'me' } });
    c.send({ type: 'abandon' });
    await c.next('result');
    await c.next('debrief');
    await c.next('report');
    c.send({ type: 'next_drill' });
    expect((await c.next('drill')).spec.id).not.toBe(expected.id);
  }, 60000);

  it('rehearses mistakes on the map they happened on, even after picking another map', async () => {
    const c = await client();
    c.send({ type: 'hello', protocol: PROTOCOL });
    await c.next('hello');
    c.send({ type: 'pick_map', map: 'ironvale' });
    await c.next('map');
    c.send(load);
    const report = await c.next('report');
    expect(report.rehearsals.length).toBeGreaterThan(0);
    c.send({ type: 'rehearse', index: 0 });
    const map = await c.next('map');
    expect(map.map.id).toBe('saltworks');
    const drill = await c.next('drill');
    expect(drill.spec.mapId).toBe('saltworks');
    c.send({ type: 'abandon' });
    await c.next('result');
    await c.next('debrief');
    await c.next('report');
  }, 60000);

  it('retry plays the same drill again, and refuses with nothing to retry', async () => {
    const c = await client();
    c.send({ type: 'retry' });
    expect((await c.next('error')).message).toMatch(/retry/);
    c.send(load);
    await c.next('report');
    c.send({ type: 'next_drill' });
    const first = await c.next('drill');
    c.send({ type: 'abandon' });
    await c.next('result');
    await c.next('debrief');
    await c.next('report');
    c.send({ type: 'retry' });
    const again = await c.next('drill');
    expect(again.spec.id).toBe(first.spec.id);
    expect(again.spec.seed).toBe(first.spec.seed);
    expect(again.state).toEqual(first.state);
  }, 60000);

  it('abandons an existing drill before starting another and serializes pipelined commands', async () => {
    const c = await client();
    c.send(load);
    await c.next('report');
    c.send({ type: 'next_drill' });
    const first = await c.next('drill');
    c.send({ type: 'versus', archetype: 'baseline', skill: 'entry' });
    c.send({ type: 'input', input: idleInput('wrong-id') });
    const result = await c.next('result');
    expect(result.result.drillId).toBe(first.spec.id);
    expect(result.success).toBe(false);
    expect(result.staircaseAfter).toBeLessThan(result.staircaseBefore);
    await c.next('debrief');
    await c.next('report');
    const second = await c.next('drill');
    const step = await c.next('step');
    expect(step.state.tick).toBe(1);
    expect(step.state.agents.some((a) => a.id === second.playerId)).toBe(true);
  }, 60000);

  it('loads real logs and keeps connected players independent', async () => {
    const c = await client();
    const other = await client(c.server);
    const logs = sampleHistory('clutch', 5, 1);
    c.send({ type: 'load_history', logs });
    expect((await c.next('report')).report).toEqual(analyze(logs, 'me'));
    c.send({ type: 'next_drill' });
    await c.next('drill');
    other.send({ type: 'input', input: idleInput('player') });
    await other.next('error');
    other.send({ type: 'hello', protocol: PROTOCOL });
    await other.next('hello');
  }, 60000);

  it('rejects invalid commands and input without advancing the sim', async () => {
    const c = await client();
    c.send({ type: 'hello', protocol: 'wrong' } as unknown as ClientMessage);
    await c.next('error');
    c.send({ type: 'rehearse', index: -1 });
    await c.next('error');
    c.send({ type: 'versus', archetype: 'unknown', skill: 'entry' } as unknown as ClientMessage);
    await c.next('error');
    c.send({ type: 'next_drill' });
    await c.next('drill');
    c.socket.write('{"type":"input","input":{}}\n');
    await c.next('error');
    const { agentId: _, ...input } = idleInput('player');
    c.send({ type: 'input', input });
    expect((await c.next('step')).state.tick).toBe(1);
  }, 60000);
});

import type { Socket } from 'node:net';
import { describe, expect, it } from 'vitest';
import { serveBridgeConnection } from './server.ts';
import { PROTOCOL, type ClientMessage, type ServerMessage } from './protocol.ts';
import { idleInput, startDrill } from '../session/drill-run.ts';
import { sampleHistory } from '../analyzer/synthetic.ts';
import { analyze } from '../analyzer/analyze.ts';
import { createRecorder } from '../session/recorder.ts';
import { CoachingSession } from '../session/session.ts';

/** Exercises the real NDJSON handler even in sandboxes that prohibit TCP bind. */
function stream() {
  let receive: (chunk: string) => void = () => {};
  const messages: ServerMessage[] = [];
  let waiting: ((msg: ServerMessage) => void) | undefined;
  const socket = {
    destroyed: false, writable: true,
    setEncoding() {},
    on(event: string, listener: (chunk: string) => void) { if (event === 'data') receive = listener; },
    write(line: string) {
      const msg = JSON.parse(line) as ServerMessage;
      if (waiting) { const resolve = waiting; waiting = undefined; resolve(msg); }
      else messages.push(msg);
      return true;
    },
  };
  serveBridgeConnection(socket as unknown as Socket, { apiKey: null });
  return {
    raw: (s: string) => receive(s),
    send: (msg: ClientMessage) => receive(JSON.stringify(msg) + '\n'),
    async next<T extends ServerMessage['type']>(type: T): Promise<Extract<ServerMessage, { type: T }>> {
      const msg = messages.shift() ?? await new Promise<ServerMessage>((resolve) => { waiting = resolve; });
      expect(msg.type).toBe(type);
      return msg as Extract<ServerMessage, { type: T }>;
    },
  };
}
const load = { type: 'load_history', sample: { skill: 'entry', seed: 11 } } as const;

describe('Unity bridge connection handler (in memory)', () => {
  it('runs, judges, records and re-analyzes the same drill as the sim', async () => {
    const c = stream();
    c.send({ type: 'hello', protocol: PROTOCOL });
    await c.next('hello');
    c.send(load);
    await c.next('report');
    c.send({ type: 'next_drill' });
    const drill = await c.next('drill');
    const reference = startDrill(drill.spec);
    let done = false;
    for (let tick = 1; tick <= reference.maxTicks && !done; tick++) {
      c.send({ type: 'input', input: idleInput('ignored') });
      const step = await c.next('step');
      const expected = reference.step(idleInput(drill.playerId));
      expect(step.state).toEqual(expected.state);
      expect(step.events).toEqual(expected.events);
      expect(step.thoughts).toEqual(Object.fromEntries(expected.trace.map((t) => [t.botId, t.node])));
      done = step.done;
    }
    expect(done).toBe(true);
    const result = await c.next('result');
    expect(result.result).toEqual(reference.finish());
    expect((await c.next('debrief')).source).toBe('template');
    const report = await c.next('report');
    const recorder = createRecorder({ matchId: 'bridge-0', mapId: drill.spec.mapId, playerId: drill.playerId });
    recorder.beginRound(drill.state);
    recorder.push(result.result.events);
    recorder.endRound(reference.state().winner ?? (result.success ? drill.playerTeam : drill.playerTeam === 'attack' ? 'defense' : 'attack'));
    expect(report.report).toEqual(analyze([...sampleHistory('entry', 11), recorder.toLog()], drill.playerId));
    expect(report.played).toBe(1);
    expect(report.staircases[drill.spec.skill].difficulty).toBe(result.staircaseAfter);
  }, 60000);

  it('recovers from invalid JSON and input without a drill, including fragmented commands', async () => {
    const c = stream();
    c.send({ type: 'input', input: idleInput('player') });
    await c.next('error');
    c.raw('{bad}\n{"type":"hel');
    await c.next('error');
    c.raw('lo","protocol":"bots-wire.1"}\n');
    await c.next('hello');
  }, 60000);

  it('starts the selected rehearsal and abandons it before versus without re-offering it', async () => {
    const c = stream();
    c.send(load);
    await c.next('report');
    c.send({ type: 'rehearse', index: 0 });
    const drill = await c.next('drill');
    const session = CoachingSession.fromHistory(sampleHistory('entry', 11), 'me', { seed: 11, apiKey: null });
    expect(drill.spec.id).toBe(session.rehearsals()[0].id);
    c.send({ type: 'versus', archetype: 'patient-lurker', skill: 'clutch' });
    expect((await c.next('result')).success).toBe(false);
    await c.next('debrief');
    const report = await c.next('report');
    const versus = await c.next('drill');
    expect(versus.spec.bots.every((b) => b.archetypeId === 'patient-lurker')).toBe(true);
    c.send({ type: 'abandon' });
    const result = await c.next('result');
    expect(result.staircaseAfter).toBe(result.staircaseBefore);
    await c.next('debrief');
    const after = await c.next('report');
    expect(after.staircases).toEqual(report.staircases);
    expect(after.played).toBe(2);
    c.send({ type: 'next_drill' });
    expect((await c.next('drill')).spec.id).not.toBe(drill.spec.id);
  }, 60000);
});

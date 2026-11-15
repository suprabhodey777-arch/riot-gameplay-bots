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

describe('wire v2 additions', () => {
  it('lists maps, preserves picks across history loading, and generates on the picked map', async () => {
    const c = stream();
    c.send({ type: 'hello', protocol: 'bots-wire.2' });
    const hello = await c.next('hello');
    expect(hello.rules).toMatchObject({ flashFuseS: .6, flashBlindS: 1.75, dashDistanceM: 6, runHearingM: 15, walkSpeedMps: 3, crouchSpeedMps: 2 });
    expect(Object.keys(hello.rules.agents ?? {})).toEqual(['rook', 'haze', 'wren', 'pike']);
    c.send({ type: 'maps' });
    expect((await c.next('maps')).maps.map(m => m.id)).toEqual(['saltworks', 'ironvale']);
    c.send({ type: 'pick_map', map: 'ironvale' });
    expect((await c.next('map')).map.id).toBe('ironvale');
    c.send({ type: 'pick_agent', agent: 'pike' });
    expect(await c.next('agent')).toMatchObject({ agent: 'pike', kit: { smoke: 1, recon: 2 } });
    c.send(load);
    const report = await c.next('report');
    expect(report.rehearsals).toEqual([]);
    c.send({ type: 'next_drill' });
    const drill = await c.next('drill');
    expect(drill.spec.mapId).toBe('ironvale');
    expect(drill.playerAgent).toBe('pike');
    expect(drill.spec.player.agent).toBe('pike');
    expect(drill.bots.every(b => b.agent)).toBe(true);
    expect(drill.state.agents.find(a => a.id === drill.playerId)?.abilities).toEqual({ smoke: 1, flash: 0, dash: 0, recon: 2 });
    c.send({ type: 'input', input: { ...idleInput('ignored'), reconAt: drill.state.agents.find(a => a.id === drill.playerId)!.pos } });
    expect((await c.next('step')).events.some(e => e.type === 'recon')).toBe(true);
  }, 60000);

  it('wire v1 strips all v4 fields and new events, including nested result events', async () => {
    const c = stream();
    c.send({ type: 'hello', protocol: 'bots-wire.1' });
    const hello = await c.next('hello');
    expect(hello.protocol).toBe('bots-wire.1');
    expect(hello.rules.flashFuseS).toBeUndefined();
    expect(hello.rules.agents).toBeUndefined();
    c.send({ type: 'versus', archetype: 'entry-duelist', skill: 'entry' });
    const drill = await c.next('drill');
    expect(drill.playerAgent).toBeUndefined();
    expect(drill.spec.player.agent).toBeUndefined();
    expect(drill.bots[0].agent).toBeUndefined();
    expect(drill.state.agents[0].agent).toBeUndefined();
    expect(drill.state.agents[0].abilities).toBeUndefined();
    c.send({ type: 'input', input: { ...idleInput('ignored'), move: { x: 1, y: 0 }, dash: true } });
    const step = await c.next('step');
    expect(step.state.agents[0].blindUntilTick).toBeUndefined();
    expect(step.events.some(e => e.type === 'footstep' || e.type === 'flash' || e.type === 'dash')).toBe(false);
    c.send({ type: 'abandon' });
    expect((await c.next('result')).result.events.some(e => e.type === 'footstep')).toBe(false);
  }, 60000);

  it('rejects invalid maps, agents and optional input fields without stepping', async () => {
    const c = stream();
    c.raw('{"type":"pick_map","map":"missing"}\n{"type":"pick_agent","agent":"missing"}\n');
    await c.next('error'); await c.next('error');
    c.send({ type: 'versus', archetype: 'baseline', skill: 'entry' });
    await c.next('drill');
    for (const extra of [{ flashAt: { x: 'bad', y: 0 } }, { reconAt: [] }, { dash: 1 }]) {
      c.raw(JSON.stringify({ type: 'input', input: { ...idleInput('player'), ...extra } }) + '\n');
      await c.next('error');
    }
    c.send({ type: 'input', input: idleInput('player') });
    expect((await c.next('step')).state.tick).toBe(1);
  }, 60000);
});

it('wire v3 exposes weapons and runs a full match through buy, ready, results and re-analysis', async () => {
  const c = stream(); c.send({ type: 'hello', protocol: PROTOCOL });
  const hello = await c.next('hello'); expect(hello.rules.weapons.lancer.fireIntervalS).toBe(0.15);
  expect(hello.rules.weapons.brick.pellets).toBe(8);
  c.send({ type: 'match', map: 'ironvale', agent: 'rook' });
  await c.next('map'); let buy = await c.next('buy_phase'); expect(buy.credits).toBe(800); expect(buy.team).toHaveLength(4);
  c.send({ type: 'buy', item: 'longshot' }); expect((await c.next('error')).message).toContain('afford');
  c.send({ type: 'buy', item: 'hammer' }); expect((await c.next('loadout')).credits).toBe(300);
  c.send({ type: 'sell', item: 'hammer' }); expect((await c.next('loadout')).credits).toBe(800);
  let complete = false;
  while (!complete) {
    c.send({ type: 'ready' }); await c.next('step');
    let done = false;
    for (let tick = 0; tick < 3001 && !done; tick++) {
      c.send({ type: 'input', input: idleInput('spoofed-enemy') });
      done = (await c.next('step')).done;
    }
    expect(done).toBe(true);
    const result = await c.next('round_result');
    expect(result.creditsEarned).toBeGreaterThanOrEqual(1900);
    complete = Math.max(result.score.player, result.score.enemy) === 7;
    if (!complete) { buy = await c.next('buy_phase'); if (buy.round === 7) { expect(buy.side).toBe('defense'); expect(buy.roundType).toBe('pistol'); } }
  }
  expect((await c.next('match_end')).score).toEqual(expect.objectContaining({ enemy: expect.any(Number) }));
  const report = await c.next('report'); expect(report.played).toBe(1); expect(report.report.matchesRead).toBe(1);
}, 60000);

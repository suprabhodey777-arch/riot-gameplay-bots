import process from 'node:process';
import { createServer, type Server, type Socket } from 'node:net';
import { pathToFileURL } from 'node:url';
import type { MatchLog, SimState } from '../shared/contract.ts';
import type { DrillSpec, Skill } from '../shared/coaching.ts';
import { loadMap } from '../shared/maps/index.ts';
import { CoachingSession, SKILLS, type SessionOptions } from '../session/session.ts';
import { startDrill, type DrillRun } from '../session/drill-run.ts';
import { createRecorder } from '../session/recorder.ts';
import { sampleHistory } from '../analyzer/synthetic.ts';
import { analyze } from '../analyzer/analyze.ts';
import { buildDrill } from '../analyzer/drills.ts';
import { archetypeById } from '../bot/traits.ts';
import { debrief } from '../coach/debrief.ts';
import { DEFUSE_TIME_S, PLANT_TIME_S, RUN_SPEED, TICK_RATE } from '../sim/sim.ts';
import { PROTOCOL, type BridgeInput, type ClientMessage, type Rules, type ServerMessage } from './protocol.ts';

export const RULES: Rules = {
  tickRate: TICK_RATE, fireIntervalS: 0.15, smokeMaxRangeM: 25,
  plantTimeS: PLANT_TIME_S, defuseTimeS: DEFUSE_TIME_S, moveSpeedMps: RUN_SPEED,
};

const object = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const vec = (v: unknown): boolean => object(v) && Number.isFinite(v.x) && Number.isFinite(v.y);
function validateInput(v: unknown): asserts v is BridgeInput {
  if (!object(v) || !vec(v.move) || !Number.isFinite(v.yaw)
    || !['walk', 'crouch', 'plantHeld', 'defuseHeld'].every((k) => typeof v[k] === 'boolean')
    || !(v.smokeAt === null || vec(v.smokeAt))
    || !(v.fire === null || (object(v.fire) && typeof v.fire.targetId === 'string' && ['head', 'body', 'miss'].includes(String(v.fire.result))))) {
    throw new Error('Invalid player input');
  }
}
function skill(v: unknown): asserts v is Skill {
  if (!SKILLS.includes(v as Skill)) throw new Error('Unknown skill');
}

/** Create without listening, so callers can bind port 0 and close it in tests. */
export function createBridgeServer(options: SessionOptions = {}): Server {
  return createServer((socket) => serveBridgeConnection(socket, options));
}

/** Handle one ordered NDJSON stream; exported for transport-independent verification. */
export function serveBridgeConnection(socket: Socket, options: SessionOptions = {}): void {
  let logs: MatchLog[] = [];
  let played: MatchLog[] = [];
  let session = CoachingSession.fromHistory([], 'player', options);
  let active: { run: DrillRun; initial: SimState; versus: boolean } | null = null;
  let sequence = 0;
  const send = (msg: ServerMessage): void => { if (!socket.destroyed && socket.writable) socket.write(JSON.stringify(msg) + '\n'); };
  const report = (): void => {
    const staircases = Object.fromEntries(SKILLS.map((s) => [s, { difficulty: session.staircase(s).difficulty }])) as Record<Skill, { difficulty: number }>;
    send({ type: 'report', report: session.report, rehearsals: session.rehearsals().map((d, index) => ({ index, title: d.title, brief: d.goal, skill: d.skill })), staircases, played: played.length });
  };
  const finish = async (abandon = false): Promise<void> => {
    if (!active) throw new Error('No live drill');
    const { run, initial, versus } = active;
    active = null;
    const spec = run.spec;
    const result = { ...run.finish(), ...(abandon ? { success: false } : {}) };
    const rec = createRecorder({ matchId: `bridge-${sequence++}`, mapId: spec.mapId, playerId: session.playerId, role: session.report.role });
    rec.beginRound(initial);
    rec.push(result.events);
    rec.endRound(run.state().winner ?? (result.success ? spec.player.team : spec.player.team === 'attack' ? 'defense' : 'attack'));
    played.push(rec.toLog());
    const before = session.staircase(spec.skill).difficulty;
    // complete updates the staircase synchronously before awaiting the coach.
    const pending = versus
      ? debrief({ report: session.report, result, drill: spec, playerId: session.playerId }, options)
      : session.complete(result);
    send({ type: 'result', result, success: result.success, headline: abandon ? 'Drill abandoned' : result.success ? 'Drill complete' : 'Not this time', staircaseBefore: before, staircaseAfter: session.staircase(spec.skill).difficulty });
    const written = await pending;
    send({ type: 'debrief', text: written.text, source: written.source });
    const fresh = analyze([...logs, ...played], session.playerId);
    session = CoachingSession.fromJSON({ ...session.toJSON(), report: fresh.findings.length ? fresh : { ...session.report, matchesRead: fresh.matchesRead } }, options);
    report();
  };
  const begin = (drill: DrillSpec, versus = false): void => {
    const spec = { ...drill, player: { ...drill.player, id: session.playerId } };
    if (!versus) {
      const state = session.toJSON();
      session = CoachingSession.fromJSON({ ...state, current: spec }, options);
    }
    const run = startDrill(spec);
    active = { run, initial: run.state(), versus };
    send({ type: 'drill', spec, intro: spec.goal, playerId: spec.player.id, playerTeam: spec.player.team,
      bots: spec.bots.map((b) => ({ id: b.id, team: b.team, archetype: b.archetypeId })), state: run.state() });
  };
  const handle = async (raw: unknown): Promise<void> => {
    if (!object(raw) || typeof raw.type !== 'string') throw new Error('Expected an object with a type');
    const msg = raw as unknown as ClientMessage;
    switch (msg.type) {
      case 'hello':
        if (msg.protocol !== PROTOCOL) throw new Error(`Expected protocol ${PROTOCOL}`);
        send({ type: 'hello', protocol: PROTOCOL, map: loadMap(session.toJSON().mapId), rules: RULES });
        break;
      case 'load_history': {
        let next: MatchLog[];
        let seed = 1;
        if (msg.sample) {
          skill(msg.sample.skill);
          if (!Number.isSafeInteger(msg.sample.seed)) throw new Error('Invalid sample seed');
          seed = msg.sample.seed;
          next = sampleHistory(msg.sample.skill, seed);
        } else {
          if (!Array.isArray(msg.logs)) throw new Error('Expected sample or logs');
          next = msg.logs;
          if (!next.every((l) => object(l) && typeof l.playerId === 'string' && typeof l.mapId === 'string' && Array.isArray(l.rounds))) throw new Error('Invalid match logs');
        }
        const replacement = CoachingSession.fromHistory(next, next[0]?.playerId ?? 'player', { ...options, seed });
        loadMap(replacement.toJSON().mapId);
        if (active) await finish(true);
        logs = next;
        played = [];
        session = replacement;
        report();
        break;
      }
      case 'next_drill':
        if (active) await finish(true);
        begin(session.nextDrill().drill);
        break;
      case 'rehearse': {
        // Resolve the index against the report the client is currently showing.
        const spec = session.rehearsals()[msg.index];
        if (!Number.isInteger(msg.index) || !spec) throw new Error('Invalid rehearsal index');
        if (active) await finish(true);
        const state = session.toJSON();
        session = CoachingSession.fromJSON({ ...state, history: [...state.history, { skill: spec.skill, seed: spec.seed }], rehearsed: [...new Set([...(state.rehearsed ?? []), spec.id])] }, options);
        begin(spec);
        break;
      }
      case 'versus': {
        skill(msg.skill);
        const archetype = archetypeById(msg.archetype);
        if (active) await finish(true);
        const spec = buildDrill(msg.skill, 0.3, loadMap(session.toJSON().mapId), 1 + sequence++ % 8999);
        begin({ ...spec, id: `versus-${spec.id}`, bots: spec.bots.map((b) => ({ ...b, archetypeId: archetype.id, traits: { ...archetype.traits } })) }, true);
        break;
      }
      case 'input': {
        if (!active) throw new Error('No live drill');
        validateInput(msg.input);
        const step = active.run.step({ ...msg.input, agentId: active.run.spec.player.id });
        const thoughts: Record<string, string> = {};
        for (const t of step.trace) thoughts[t.botId] = t.node;
        send({ type: 'step', state: step.state, events: step.events, thoughts, done: step.done });
        if (step.done) await finish();
        break;
      }
      case 'abandon': await finish(true); break;
      default: throw new Error(`Unknown message type: ${raw.type}`);
    }
  };
  // Serialize each connection's messages, including asynchronous debriefs.
  let queue = Promise.resolve();
  let buffer = '';
  socket.setEncoding('utf8');
  socket.on('error', () => { /* The disconnected player's state is connection-local. */ });
  socket.on('data', (chunk: string) => {
    buffer += chunk;
    let end: number;
    while ((end = buffer.indexOf('\n')) !== -1) {
      const line = buffer.slice(0, end);
      buffer = buffer.slice(end + 1);
      queue = queue.then(async () => {
        if (socket.destroyed) return;
        await handle(JSON.parse(line) as unknown);
      }).catch((err: unknown) => send({ type: 'error', message: err instanceof Error ? err.message : String(err) }));
    }
  });
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const port = Number(process.env.BRIDGE_PORT ?? 7777);
  const server = createBridgeServer();
  server.on('error', (err) => { console.error(err.message); process.exitCode = 1; });
  server.listen(port, '127.0.0.1', () => console.log(`Unity bridge listening on 127.0.0.1:${port}`));
}

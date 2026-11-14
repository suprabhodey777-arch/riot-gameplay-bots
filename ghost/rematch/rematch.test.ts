// End-to-end check of Ghost Rematch on the real game server: list a death from
// a fixture log, start a drill against a ghost bot that the test connects,
// win it, tune the skill, retry, leave, and reject an unknown moment.
// Spawns `node game/server.js` on PORT 3193 with temp log and data dirs.
import { afterAll, beforeAll, describe, expect, test } from 'vitest';
import { spawn, type ChildProcess } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { io, type Socket } from 'socket.io-client';
import type { LogEvent, Team } from '../contracts';

const root = fileURLToPath(new URL('../..', import.meta.url));
const PORT = 3193;
const logDir = mkdtempSync(path.join(tmpdir(), 'ghost-rematch-logs-'));
const dataDir = mkdtempSync(path.join(tmpdir(), 'ghost-rematch-data-'));
const fixtureFile = path.join(logDir, 'old.jsonl');

interface SnapPlayer {
  id: string;
  name: string;
  team: Team;
  pos: { x: number; y: number; z: number };
  alive: boolean;
  hp: number;
  bot?: boolean;
}

interface Snapshot {
  round: {
    number: number;
    phase: string;
    timer: number;
    scoreAtk: number;
    scoreDef: number;
    practice: boolean;
  };
  players: SnapPlayer[];
}

interface JoinedPayload {
  me: SnapPlayer;
  snapshot: Snapshot;
}

interface Moment {
  id: string;
  matchId: string;
  round: number;
  t: number;
  killer: string;
  killerGhostReady: boolean;
  zone: string | null;
  weapon: string;
  beaten: boolean;
  attempts: number;
}

interface DrillState {
  momentId: string;
  ghostOf: string;
  ghostBot: string;
  attempt: number;
  beaten: boolean;
  variation: boolean;
  skillScale: number;
  autoSkill: boolean;
  goal: string;
}

interface Ack {
  ok: boolean;
  error?: string;
  moments?: Moment[];
  drill?: DrillState;
}

interface Debrief {
  momentId: string;
  attempt: number;
  text: string;
  source: 'llm' | 'template';
}

// The fixture: one round where bob kills ann at t=30. The drill rebuilds the
// state three seconds earlier, at t=27.
const fixture: LogEvent[] = [
  {
    v: 1,
    matchId: 'old',
    round: 1,
    t: 0,
    player: '',
    type: 'round-start',
    teams: { ann: 'defense', bob: 'attack' },
    practice: false,
  },
];
for (let i = 0; i <= 20; i++) {
  const t = 20 + i * 0.5;
  fixture.push({
    v: 1,
    matchId: 'old',
    round: 1,
    t,
    player: 'ann',
    type: 'move',
    pos: { x: -10 + (t - 20) * 0.5, y: 0, z: 0 },
    yaw: 0,
    pitch: 0,
    moving: true,
    crouch: false,
    team: 'defense',
    hp: 100,
  });
  fixture.push({
    v: 1,
    matchId: 'old',
    round: 1,
    t,
    player: 'bob',
    type: 'move',
    pos: { x: 10 - (t - 20) * 0.5, y: 0, z: 0 },
    yaw: Math.PI,
    pitch: 0,
    moving: true,
    crouch: false,
    team: 'attack',
    hp: 100,
  });
}
fixture.push({
  v: 1,
  matchId: 'old',
  round: 1,
  t: 30,
  player: 'bob',
  type: 'kill',
  victim: 'ann',
  killer: 'bob',
  weapon: 'vandal',
  headshot: false,
  killerPos: { x: 5, y: 0, z: 0 },
  victimPos: { x: -5, y: 0, z: 0 },
});
writeFileSync(fixtureFile, `${fixture.map((event) => JSON.stringify(event)).join('\n')}\n`);

const sockets: Socket[] = [];
let child: ChildProcess | null = null;
let ann: Socket;
let bot: Socket;
let botId = '';
let momentId = '';

function connect(): Socket {
  const sock = io(`http://localhost:${PORT}`, {
    transports: ['websocket'],
    forceNew: true,
    reconnection: false,
  });
  sockets.push(sock);
  return sock;
}

function once<T>(sock: Socket, event: string, ms = 5000): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => {
      sock.off(event, handler);
      reject(new Error(`timed out waiting for '${event}'`));
    }, ms);
    const handler = (data: T) => {
      clearTimeout(timer);
      sock.off(event, handler);
      resolve(data);
    };
    sock.on(event, handler);
  });
}

function nextMatching<T>(sock: Socket, event: string, match: (data: T) => boolean, ms = 5000): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => {
      sock.off(event, handler);
      reject(new Error(`no matching '${event}' within ${ms}ms`));
    }, ms);
    const handler = (data: T) => {
      if (!match(data)) return;
      clearTimeout(timer);
      sock.off(event, handler);
      resolve(data);
    };
    sock.on(event, handler);
  });
}

function request<T>(sock: Socket, event: string, payload?: unknown): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`no ack for '${event}' within 5000ms`)), 5000);
    const cb = (result: T) => {
      clearTimeout(timer);
      resolve(result);
    };
    if (payload === undefined) sock.emit(event, cb);
    else sock.emit(event, payload, cb);
  });
}

async function join(sock: Socket, payload: Record<string, unknown>): Promise<JoinedPayload> {
  const joined = once<JoinedPayload>(sock, 'joined');
  sock.emit('join', payload);
  return joined;
}

async function waitForTick(
  sock: Socket,
  match: (snap: Snapshot) => boolean,
  ms = 5000,
): Promise<Snapshot> {
  const deadline = Date.now() + ms;
  let last: Snapshot | null = null;
  while (Date.now() < deadline) {
    last = await once<Snapshot>(sock, 'tick', Math.max(1, deadline - Date.now()));
    if (match(last)) return last;
  }
  throw new Error(`no matching tick within ${ms}ms (last phase ${last?.round.phase})`);
}

beforeAll(async () => {
  const env: NodeJS.ProcessEnv = {
    ...process.env,
    PORT: String(PORT),
    GHOST_LOG_DIR: logDir,
    GHOST_DATA_DIR: dataDir,
    MATCH_ID: 'live',
    GHOST_ALLOW_DEFAULT: '1',
    GHOST_BOT_CMD: '',
  };
  delete env.ANTHROPIC_API_KEY;

  child = spawn(process.execPath, [path.join(root, 'game', 'server.js')], {
    cwd: root,
    env,
    stdio: ['ignore', 'pipe', 'pipe'],
  });

  let stderr = '';
  child.stderr?.on('data', (chunk: Buffer) => {
    stderr += chunk.toString();
  });

  await new Promise<void>((resolve, reject) => {
    const timer = setTimeout(() => {
      reject(new Error(`server did not start in time. stderr: ${stderr}`));
    }, 15000);
    child!.stdout?.on('data', (chunk: Buffer) => {
      if (!chunk.toString().includes('Ghost Rematch on')) return;
      clearTimeout(timer);
      resolve();
    });
    child!.once('exit', (code) => {
      clearTimeout(timer);
      reject(new Error(`server exited with code ${code}. stderr: ${stderr}`));
    });
  });
}, 20000);

afterAll(async () => {
  for (const sock of sockets) sock.close();
  if (child && child.exitCode === null) {
    await new Promise<void>((resolve) => {
      const timer = setTimeout(resolve, 3000);
      child!.once('exit', () => {
        clearTimeout(timer);
        resolve();
      });
      child!.kill();
    });
  }
  rmSync(logDir, { recursive: true, force: true });
  rmSync(dataDir, { recursive: true, force: true });
});

describe('ghost rematch', () => {
  test('(a) rematch-list finds the fixture death', async () => {
    ann = connect();
    const joined = await join(ann, { name: 'ann', agent: 'jett', team: 'defense' });
    expect(joined.me.name).toBe('ann');
    expect(joined.me.team).toBe('defense');

    const list = await request<Ack>(ann, 'rematch-list');
    expect(list.ok).toBe(true);
    expect(list.moments).toHaveLength(1);
    const moment = list.moments![0];
    momentId = moment.id;
    expect(moment.killer).toBe('bob');
    expect(moment.killerGhostReady).toBe(true);
    expect(moment.beaten).toBe(false);
    expect(moment.attempts).toBe(0);
  });

  test('(b) rematch-start rebuilds the moment with the ghost bot', async () => {
    bot = connect();
    const joined = await join(bot, { name: 'bob·ghost', agent: 'jett', team: 'attack', bot: true });
    botId = joined.me.id;
    expect(joined.me.name).toBe('bob·ghost');

    const config = once<{ ghostOf: string; skillScale: number; drillPlayer: string }>(bot, 'bot-config', 8000);
    const ack = await request<Ack>(ann, 'rematch-start', { momentId, variation: false });
    expect(ack.ok).toBe(true);
    const drill = ack.drill!;
    expect(drill.ghostOf).toBe('bob');
    expect(drill.attempt).toBe(1);
    expect(drill.goal).toBe('Kill the ghost or survive 20 s');

    const snap = await waitForTick(ann, (s) => s.round.practice && s.round.phase === 'live');
    const me = snap.players.find((p) => p.name === 'ann')!;
    expect(me.pos.x).toBeCloseTo(-6.5, 2);
    expect(me.pos.z).toBeCloseTo(0, 2);

    const settings = await config;
    expect(settings.ghostOf).toBe('bob');
  });

  test('(c) the win ends the drill and brings a template debrief', async () => {
    const practiceEnd = once<{ winner: Team; reason: string }>(ann, 'practice-end', 8000);
    const drilled = nextMatching<DrillState>(
      ann,
      'drill-state',
      (d) => d.beaten === true && Math.abs(d.skillScale - 1.1) < 1e-9,
      8000,
    );
    const debrief = once<Debrief>(ann, 'debrief', 12000);

    ann.emit('hit', { victimId: botId, damage: 150, headshot: false, weapon: 'vandal' });

    const end = await practiceEnd;
    expect(end.winner).toBe('defense');

    const state = await drilled;
    expect(state.attempt).toBe(1);
    expect(state.beaten).toBe(true);

    const text = await debrief;
    expect(text.momentId).toBe(momentId);
    expect(text.attempt).toBe(1);
    expect(text.source).toBe('template');
    expect(text.text.trim().length).toBeGreaterThan(0);
  }, 20000);

  test('(d) skill-scale sets the ghost and turns auto-tuning off', async () => {
    const state = nextMatching<DrillState>(
      ann,
      'drill-state',
      (d) => d.skillScale === 0.7 && d.autoSkill === false,
      5000,
    );
    const ack = await request<Ack>(ann, 'skill-scale', { value: 0.7 });
    expect(ack.ok).toBe(true);
    const drilled = await state;
    expect(drilled.skillScale).toBe(0.7);
    expect(drilled.autoSkill).toBe(false);
  });

  test('(e) a second start is attempt 2 and the moment stays beaten', async () => {
    const ack = await request<Ack>(ann, 'rematch-start', { momentId, variation: false });
    expect(ack.ok).toBe(true);
    expect(ack.drill!.attempt).toBe(2);

    const list = await request<Ack>(ann, 'rematch-list');
    expect(list.ok).toBe(true);
    const moment = list.moments!.find((m) => m.id === momentId)!;
    expect(moment.attempts).toBeGreaterThanOrEqual(1);
    expect(moment.beaten).toBe(true);
  });

  test('(f) rematch-leave returns to a normal round', async () => {
    const roundStart = once<Snapshot>(ann, 'round-start', 5000);
    const ack = await request<Ack>(ann, 'rematch-leave');
    expect(ack.ok).toBe(true);
    const snap = await roundStart;
    expect(snap.round.practice).toBe(false);
  });

  test('(g) an unknown moment is refused', async () => {
    const ack = await request<Ack>(ann, 'rematch-start', { momentId: 'missing:1:1', variation: false });
    expect(ack.ok).toBe(false);
    expect(typeof ack.error).toBe('string');
  });
});

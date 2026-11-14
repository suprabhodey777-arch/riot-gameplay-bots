// End-to-end check of the match recorder and `load-scenario` on the real game server.
// Spawns `node game/server.js` on PORT 3191 with its own temp log dir, drives it with
// socket.io-client, then inspects the JSONL file. The ADMIN_KEY case is skipped on
// purpose (the brief forbids a second server in this test).
import { afterAll, beforeAll, describe, expect, test } from 'vitest';
import { spawn, type ChildProcess } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { io, type Socket } from 'socket.io-client';
import type { LogEvent, ScenarioFile, Team } from '../contracts';

const root = fileURLToPath(new URL('../..', import.meta.url));
const PORT = 3191;
const logDir = mkdtempSync(path.join(tmpdir(), 'ghost-server-test-'));
const logFile = path.join(logDir, 't1.jsonl');

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

interface KillPayload {
  killer: { id: string; name: string; team: Team };
  victim: { id: string; name: string; team: Team };
}

interface Ack {
  ok: boolean;
  error?: string;
}

const sockets: Socket[] = [];
let child: ChildProcess | null = null;
let ann: Socket;
let bob: Socket;
let annId = '';
let bobId = '';
let practiceEnd: Promise<{ winner: Team; reason: string }> | null = null;

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

const scenario: ScenarioFile = {
  id: 'test-scenario',
  source: null,
  phase: 'live',
  timer: 90,
  spike: {
    planted: false,
    site: null,
    pos: null,
    explodeTimer: 0,
    holder: 'ann',
    dropped: null,
  },
  players: [
    {
      name: 'ann',
      team: 'attack',
      pos: { x: 0, y: 0, z: -5 },
      yaw: 0,
      pitch: 0,
      hp: 100,
      armor: 0,
      weapon: 'vandal',
      hasSpike: true,
      ultPoints: 0,
    },
    {
      name: 'bob',
      team: 'defense',
      pos: { x: 0, y: 0, z: 5 },
      yaw: 0,
      pitch: 0,
      hp: 100,
      armor: 0,
      weapon: 'vandal',
      hasSpike: false,
      ultPoints: 0,
    },
  ],
  practice: true,
};

beforeAll(async () => {
  child = spawn(process.execPath, [path.join(root, 'game', 'server.js')], {
    cwd: root,
    env: {
      ...process.env,
      PORT: String(PORT),
      GHOST_LOG_DIR: logDir,
      MATCH_ID: 't1',
      ADMIN_KEY: '',
    },
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
});

describe('game server recorder', () => {
  test('(a) forced teams reach the joined payloads', async () => {
    ann = connect();
    const a = await join(ann, { name: 'ann', agent: 'jett', team: 'attack' });
    annId = a.me.id;
    expect(a.me.name).toBe('ann');
    expect(a.me.team).toBe('attack');

    bob = connect();
    const b = await join(bob, { name: 'bob', agent: 'sage', team: 'defense' });
    bobId = b.me.id;
    expect(b.me.name).toBe('bob');
    expect(b.me.team).toBe('defense');

    expect(b.snapshot.players.find((p) => p.name === 'ann')?.team).toBe('attack');
  });

  test('(b) a duplicate name gets a numeric suffix', async () => {
    const third = connect();
    const joined = await join(third, { name: 'ann', agent: 'jett' });
    expect(joined.me.name).toBe('ann-2');
    third.close();
  });

  test('(c) load-scenario restores positions and practice', async () => {
    const ack = await new Promise<Ack>((resolve) => {
      ann.emit('load-scenario', { scenario }, resolve);
    });
    expect(ack).toMatchObject({ ok: true });

    const snap = await waitForTick(ann, (s) => {
      const a = s.players.find((p) => p.name === 'ann');
      const b = s.players.find((p) => p.name === 'bob');
      return !!a && !!b && a.pos.z === -5 && b.pos.z === 5 && s.round.practice === true;
    });
    const a = snap.players.find((p) => p.name === 'ann')!;
    const b = snap.players.find((p) => p.name === 'bob')!;
    expect(a.pos.x).toBe(0);
    expect(a.pos.z).toBe(-5);
    expect(b.pos.x).toBe(0);
    expect(b.pos.z).toBe(5);
    expect(snap.round.phase).toBe('live');
    expect(snap.round.practice).toBe(true);
  });

  test('(d) a lethal hit produces a kill', async () => {
    // Register the practice-end listener before firing so the tick that ends the
    // round cannot emit it before case (f) starts listening.
    practiceEnd = once<{ winner: Team; reason: string }>(ann, 'practice-end');
    const kill = once<KillPayload>(ann, 'kill');
    ann.emit('hit', { victimId: bobId, damage: 150, headshot: false, weapon: 'vandal' });
    const msg = await kill;
    expect(msg.killer.id).toBe(annId);
    expect(msg.killer.name).toBe('ann');
    expect(msg.victim.name).toBe('bob');
  });

  test('(e) the log is valid JSONL with the required events', () => {
    const lines = readFileSync(logFile, 'utf8').split('\n').filter((line) => line.length > 0);
    expect(lines.length).toBeGreaterThan(0);

    const events = lines.map((line) => JSON.parse(line) as LogEvent);
    for (const event of events) {
      expect(event.v).toBe(1);
      expect(event.matchId).toBe('t1');
      expect(typeof event.round).toBe('number');
      expect(typeof event.t).toBe('number');
      expect(typeof event.player).toBe('string');
      expect(typeof event.type).toBe('string');
    }

    const types = events.map((e) => e.type);
    expect(types).toContain('round-start');
    expect(types).toContain('scenario');
    expect(types).toContain('damage');
    expect(types).toContain('kill');

    const kill = events.find((e): e is Extract<LogEvent, { type: 'kill' }> => e.type === 'kill');
    expect(kill?.victim).toBe('bob');
    expect(kill?.killer).toBe('ann');
  });

  test('(f) practice end reports the winner and leaves the score at 0-0', async () => {
    expect(practiceEnd).not.toBeNull();
    const end = await practiceEnd!;
    expect(end.winner).toBe('attack');

    const snap = await waitForTick(ann, (s) => s.round.phase === 'end');
    expect(snap.round.scoreAtk).toBe(0);
    expect(snap.round.scoreDef).toBe(0);
    expect(snap.round.practice).toBe(true);
  });

  // (g) The ADMIN_KEY rejection case is skipped by the brief: it would need a second server.
});

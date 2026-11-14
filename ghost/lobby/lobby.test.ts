// Ghost lobby on the real game server: list one hand-written ghost profile,
// add a ghost bot and a default bot to the two teams, read the server's bot
// list, refuse an unknown ghost, and remove both bots again. Spawns
// `node game/server.js` on PORT 3196 with temp log and profile dirs.
import { afterAll, beforeAll, describe, expect, test } from 'vitest';
import { execFileSync, spawn, type ChildProcess } from 'node:child_process';
import { mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { io, type Socket } from 'socket.io-client';
import type { PlayerProfile, Team } from '../contracts';

const root = fileURLToPath(new URL('../..', import.meta.url));
const PORT = 3196;
const logDir = mkdtempSync(path.join(tmpdir(), 'ghost-lobby-logs-'));
const profilesDir = mkdtempSync(path.join(tmpdir(), 'ghost-lobby-profiles-'));

interface SnapPlayer {
  id: string;
  name: string;
  team: Team;
  alive: boolean;
  bot?: boolean;
}

interface Snapshot {
  round: { number: number; phase: string; practice: boolean };
  players: SnapPlayer[];
}

interface Heldout {
  agreement: number;
  baseline: number | null;
  n: number;
}

interface Ghost {
  player: string;
  roundsRecorded: number;
  style: string;
  heldout: Heldout | null;
}

interface Ack {
  ok: boolean;
  error?: string;
  name?: string;
  ghosts?: Ghost[];
  bots?: { name: string; team: Team; ghostOf: string | null }[];
}

// One hand-written ghost: 3 rounds, pushes half the time, reacts in 240 ms and
// no report.json, so its held-out score is unknown.
const annProfile: PlayerProfile = {
  player: 'ann',
  roundsRecorded: 3,
  reactionMs: 240,
  aimErrorDeg: 1.4,
  flickDegPerS: 420,
  headshotRate: 0.22,
  moveWhileFiring: 0.31,
  actionMix: { push: 0.5, hold: 0.3, fight: 0.2 },
  zoneMix: { mid: 0.5, 'a-site': 0.3, 'b-site': 0.2 },
  favouriteAbilities: ['smoke', 'flash'],
};
writeFileSync(path.join(profilesDir, 'ann.json'), `${JSON.stringify(annProfile, null, 2)}\n`);

// Bot PIDs this server started, straight from the process tree.
function childPids(parent: number): number[] {
  if (!Number.isInteger(parent) || parent <= 0) return [];
  if (process.platform === 'linux') {
    const out: number[] = [];
    for (const entry of readdirSync('/proc')) {
      if (!/^\d+$/.test(entry)) continue;
      let stat = '';
      try {
        stat = readFileSync(`/proc/${entry}/stat`, 'utf8');
      } catch (err) {
        if ((err as NodeJS.ErrnoException).code !== 'ENOENT') throw err;
        continue; // the process exited between readdir and read
      }
      const ppid = Number(stat.slice(stat.lastIndexOf(')') + 2).split(' ')[1]);
      if (ppid === parent) out.push(Number(entry));
    }
    return out;
  }
  if (process.platform === 'win32') {
    const text = execFileSync(
      'powershell',
      [
        '-NoProfile',
        '-NonInteractive',
        '-Command',
        `Get-CimInstance Win32_Process -Filter "ParentProcessId=${parent}" | ForEach-Object { $_.ProcessId }`,
      ],
      { encoding: 'utf8' },
    );
    return text
      .split(/\s+/)
      .map((value) => Number(value))
      .filter((value) => Number.isInteger(value) && value > 0);
  }
  return [];
}

function alive(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch (err) {
    return (err as NodeJS.ErrnoException).code === 'EPERM';
  }
}

async function waitUntilGone(pids: number[], ms: number): Promise<number[]> {
  const deadline = Date.now() + ms;
  let left = pids.filter(alive);
  while (left.length && Date.now() < deadline) {
    await new Promise((resolve) => setTimeout(resolve, 100));
    left = pids.filter(alive);
  }
  return left;
}

const sockets: Socket[] = [];
let child: ChildProcess | null = null;
let serverPid = 0;
let human: Socket;
let ghostName = '';
let defaultName = '';
const spawnedPids: number[] = [];

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

function request<T>(sock: Socket, event: string, payload?: unknown, ms = 15000): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`no ack for '${event}' within ${ms}ms`)), ms);
    const cb = (result: T) => {
      clearTimeout(timer);
      resolve(result);
    };
    if (payload === undefined) sock.emit(event, cb);
    else sock.emit(event, payload, cb);
  });
}

async function waitForTick(sock: Socket, match: (snap: Snapshot) => boolean, ms = 10000): Promise<Snapshot> {
  const deadline = Date.now() + ms;
  let last: Snapshot | null = null;
  while (Date.now() < deadline) {
    last = await once<Snapshot>(sock, 'tick', Math.max(1, deadline - Date.now()));
    if (match(last)) return last;
  }
  throw new Error(`no matching tick within ${ms}ms (players: ${last?.players.map((p) => p.name).join(', ') ?? 'none'})`);
}

beforeAll(async () => {
  const env: NodeJS.ProcessEnv = {
    ...process.env,
    PORT: String(PORT),
    GHOST_LOG_DIR: logDir,
    GHOST_PROFILES_DIR: profilesDir,
    MATCH_ID: 'lobby',
  };
  delete env.GHOST_BOT_CMD;
  delete env.GHOST_ALLOW_DEFAULT;
  delete env.ANTHROPIC_API_KEY;

  child = spawn(process.execPath, [path.join(root, 'game', 'server.js')], {
    cwd: root,
    env,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  serverPid = child.pid ?? 0;

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
    const stopped = new Promise<void>((resolve) => {
      const timer = setTimeout(resolve, 5000);
      child!.once('exit', () => {
        clearTimeout(timer);
        resolve();
      });
    });
    try {
      process.kill(serverPid);
    } catch (err) {
      console.error(`lobby.test: could not signal server pid ${serverPid}: ${(err as Error).message}`);
    }
    await stopped;
  }
  expect(serverPid > 0 && !alive(serverPid), 'the server process is still running').toBe(true);

  // Every bot this server started must be gone once it stops: killAll runs on
  // the server's way out, and a dropped socket stops a bot by itself.
  const leftover = await waitUntilGone(spawnedPids, 10000);
  for (const pid of leftover) {
    try {
      process.kill(pid);
    } catch (err) {
      if ((err as NodeJS.ErrnoException).code !== 'ESRCH') {
        console.error(`lobby.test: could not stop leftover bot pid ${pid}: ${(err as Error).message}`);
      }
    }
  }
  expect(leftover, 'bot processes still running after the server stopped').toEqual([]);

  rmSync(logDir, { recursive: true, force: true });
  rmSync(profilesDir, { recursive: true, force: true });
});

describe('ghost lobby', () => {
  test('(1) ghost-list reads the profile and its style line', async () => {
    human = connect();
    const ack = await request<Ack>(human, 'ghost-list');
    expect(ack.ok, ack.error).toBe(true);
    expect(ack.ghosts).toHaveLength(1);
    const ghost = ack.ghosts![0];
    expect(ghost.player).toBe('ann');
    expect(ghost.roundsRecorded).toBe(3);
    expect(ghost.style).toContain('50%');
    expect(ghost.style).toContain('240 ms');
    expect(ghost.heldout).toBeNull();
  });

  test('(2) add-bot spawns a ghost onto the requested team', async () => {
    const joined = once<{ me: SnapPlayer }>(human, 'joined');
    human.emit('join', { name: 'tester', agent: 'jett', team: 'defense' });
    const me = (await joined).me;
    expect(me.name).toBe('tester');

    const ack = await request<Ack>(human, 'add-bot', { ghostOf: 'ann', team: 'defense' });
    expect(ack.ok, ack.error).toBe(true);
    ghostName = ack.name!;
    expect(ghostName.startsWith('ann')).toBe(true);

    const snap = await waitForTick(human, (s) => s.players.some((p) => p.name === ghostName));
    const bot = snap.players.find((p) => p.name === ghostName)!;
    expect(bot.bot).toBe(true);
    expect(bot.team).toBe('defense');

    spawnedPids.push(...childPids(serverPid));
    expect(spawnedPids.length).toBeGreaterThan(0);
  }, 20000);

  test('(3) add-bot spawns a default bot onto the other team', async () => {
    const ack = await request<Ack>(human, 'add-bot', { ghostOf: null, team: 'attack' });
    expect(ack.ok, ack.error).toBe(true);
    defaultName = ack.name!;
    expect(defaultName.startsWith('bot-')).toBe(true);

    const snap = await waitForTick(human, (s) => s.players.some((p) => p.name === defaultName));
    const bot = snap.players.find((p) => p.name === defaultName)!;
    expect(bot.bot).toBe(true);
    expect(bot.team).toBe('attack');

    for (const pid of childPids(serverPid)) if (!spawnedPids.includes(pid)) spawnedPids.push(pid);
    expect(spawnedPids.length).toBeGreaterThanOrEqual(2);
  }, 20000);

  test('(4) lobby-bots lists both', async () => {
    const ack = await request<Ack>(human, 'lobby-bots');
    expect(ack.ok, ack.error).toBe(true);
    const bots = ack.bots ?? [];
    expect(bots).toHaveLength(2);
    expect(bots.find((b) => b.name === ghostName)).toEqual({ name: ghostName, team: 'defense', ghostOf: 'ann' });
    expect(bots.find((b) => b.name === defaultName)).toEqual({ name: defaultName, team: 'attack', ghostOf: null });
  });

  test('(5) an unknown ghost is refused', async () => {
    const ack = await request<Ack>(human, 'add-bot', { ghostOf: 'nobody', team: 'attack' });
    expect(ack.ok).toBe(false);
    expect(typeof ack.error).toBe('string');

    const bots = await request<Ack>(human, 'lobby-bots');
    expect(bots.bots).toHaveLength(2);
  });

  test('(6) remove-bot stops both and they leave the players list', async () => {
    const first = await request<Ack>(human, 'remove-bot', { name: ghostName });
    expect(first.ok, first.error).toBe(true);
    const second = await request<Ack>(human, 'remove-bot', { name: defaultName });
    expect(second.ok, second.error).toBe(true);

    const gone = await waitForTick(
      human,
      (s) => !s.players.some((p) => p.name === ghostName) && !s.players.some((p) => p.name === defaultName),
      5000,
    );
    expect(gone.players.some((p) => p.name === 'tester')).toBe(true);

    const list = await request<Ack>(human, 'lobby-bots');
    expect(list.bots).toHaveLength(0);
    expect(await waitUntilGone(spawnedPids, 5000)).toEqual([]);

    const unknown = await request<Ack>(human, 'remove-bot', { name: ghostName });
    expect(unknown.ok).toBe(false);
  }, 20000);

  test('(7) a bot left behind is stopped when the server goes down', async () => {
    const ack = await request<Ack>(human, 'add-bot', { ghostOf: null, team: 'defense' });
    expect(ack.ok, ack.error).toBe(true);
    const left = childPids(serverPid).filter((pid) => !spawnedPids.includes(pid));
    expect(left.length).toBeGreaterThan(0);
    spawnedPids.push(...left);
  }, 20000);
});

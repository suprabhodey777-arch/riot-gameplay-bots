/**
 * Integration check for the team layer: one human socket (`hum`) and three real
 * `run.ts` bots (`mate` attacking with the human, `d1`/`d2` defending) on port
 * 3197. The scenario plants the spike on A and puts the human 6 m from its
 * teammate, so the default bot's hold is bent into "hold the other angle" and a
 * `(team: ...)` reason must reach the relayed `trace` stream.
 */
import { spawn, type ChildProcess } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { io, type Socket } from 'socket.io-client';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';

import type { ScenarioFile, TraceEvent, Vec2 } from '../contracts.ts';
import { blocked, clearPath } from './nav.ts';

const PORT = 3197;
const SERVER = join(process.cwd(), 'game', 'server.js');
const RUNTIME = join(process.cwd(), 'ghost', 'bot', 'run.ts');

/** Open ground inside A site, with the spike between the two teammates. */
const MATE_POS: Vec2 = { x: -12, z: 6 };
const HUM_POS: Vec2 = { x: -12, z: 12 };
const SPIKE_POS: Vec2 = { x: -12, z: 9 };
const D1_POS: Vec2 = { x: 14, z: 6 };
const D2_POS: Vec2 = { x: 21, z: 12 };

let server: ChildProcess | null = null;
const bots: ChildProcess[] = [];
let hum: Socket | null = null;
let logDir = '';
const serverErrors: string[] = [];
const botErrors: string[] = [];

function waitFor<T>(check: () => T | null | undefined, timeoutMs: number, label: string): Promise<T> {
  const started = Date.now();
  return new Promise<T>((resolve, reject) => {
    const poll = (): void => {
      const value = check();
      if (value != null) {
        resolve(value);
        return;
      }
      if (Date.now() - started > timeoutMs) {
        reject(new Error(`timed out after ${timeoutMs} ms waiting for ${label}`));
        return;
      }
      setTimeout(poll, 50);
    };
    poll();
  });
}

/**
 * The server's startup banner is not part of any contract, so wait for the port
 * by retrying a websocket join instead of matching stdout.
 */
function joinHum(timeoutMs: number): Promise<Socket> {
  const deadline = Date.now() + timeoutMs;
  let lastError = 'no attempt made';
  const attempt = async (): Promise<Socket | null> => {
    if (Date.now() > deadline) return null;
    const socket = io(`http://localhost:${PORT}`, { transports: ['websocket'], reconnection: false, forceNew: true });
    try {
      const joined = await new Promise<{ me: { name: string; team: string } }>((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error('no joined message within 3000 ms')), 3000);
        socket.on('connect', () => socket.emit('join', { name: 'hum', team: 'attack', agent: 'omen' }));
        socket.once('joined', (msg: { me: { name: string; team: string } }) => {
          clearTimeout(timer);
          resolve(msg);
        });
        socket.once('connect_error', (err: Error) => {
          clearTimeout(timer);
          reject(err);
        });
      });
      expect(joined.me.name).toBe('hum');
      expect(joined.me.team).toBe('attack');
      socket.removeAllListeners('connect_error');
      return socket;
    } catch (err) {
      lastError = err instanceof Error ? err.message : String(err);
      socket.disconnect();
      await new Promise<void>((resolve) => setTimeout(resolve, 250));
      return null;
    }
  };
  const poll = async (): Promise<Socket> => {
    for (;;) {
      const socket = await attempt();
      if (socket) return socket;
      if (Date.now() > deadline) throw new Error(`could not join the server on port ${PORT}: ${lastError}`);
    }
  };
  return poll();
}
async function stopChild(child: ChildProcess | null): Promise<void> {
  if (!child || child.pid == null || child.exitCode !== null) return;
  const pid = child.pid;
  try {
    process.kill(pid, 'SIGTERM');
  } catch (err) {
    console.error(`team-run.test: could not signal pid ${pid}: ${err instanceof Error ? err.message : String(err)}`);
    return;
  }
  await new Promise<void>((resolve) => {
    const timer = setTimeout(() => {
      try {
        process.kill(pid);
      } catch (err) {
        console.error(`team-run.test: could not force pid ${pid}: ${err instanceof Error ? err.message : String(err)}`);
      }
      resolve();
    }, 3000);
    child.once('exit', () => {
      clearTimeout(timer);
      resolve();
    });
  });
}

const scenario: ScenarioFile = {
  id: 'ghost-bot-team-run-test',
  source: null,
  phase: 'live',
  timer: 25,
  spike: { planted: true, site: 'A', pos: SPIKE_POS, explodeTimer: 20, holder: null, dropped: null },
  players: [
    { name: 'hum', team: 'attack', pos: { x: HUM_POS.x, y: 0, z: HUM_POS.z }, yaw: Math.PI - 0.5, pitch: 0, hp: 100, armor: 0, weapon: 'vandal', hasSpike: false, ultPoints: 0 },
    { name: 'mate', team: 'attack', pos: { x: MATE_POS.x, y: 0, z: MATE_POS.z }, yaw: Math.PI, pitch: 0, hp: 100, armor: 0, weapon: 'vandal', hasSpike: false, ultPoints: 0 },
    { name: 'd1', team: 'defense', pos: { x: D1_POS.x, y: 0, z: D1_POS.z }, yaw: 0, pitch: 0, hp: 100, armor: 0, weapon: 'classic', hasSpike: false, ultPoints: 0 },
    { name: 'd2', team: 'defense', pos: { x: D2_POS.x, y: 0, z: D2_POS.z }, yaw: 0, pitch: 0, hp: 100, armor: 0, weapon: 'classic', hasSpike: false, ultPoints: 0 },
  ],
  practice: true,
};

beforeAll(async () => {
  logDir = mkdtempSync(join(tmpdir(), 'ghost-bot-team-'));
  for (const [label, pos] of [
    ['mate', MATE_POS],
    ['hum', HUM_POS],
    ['spike', SPIKE_POS],
    ['d1', D1_POS],
    ['d2', D2_POS],
  ] as [string, Vec2][]) {
    expect(blocked(pos), `${label} at ${JSON.stringify(pos)} is inside a collider`).toBe(false);
  }
  expect(clearPath(HUM_POS, MATE_POS)).toBe(true);
  expect(Math.hypot(HUM_POS.x - MATE_POS.x, HUM_POS.z - MATE_POS.z)).toBeCloseTo(6, 5);

  server = spawn(process.execPath, [SERVER], {
    cwd: process.cwd(),
    env: { ...process.env, PORT: String(PORT), GHOST_LOG_DIR: logDir },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  server.stderr?.on('data', (chunk: Buffer) => serverErrors.push(chunk.toString()));
  hum = await joinHum(20000);

  const seen: string[] = [];
  hum.on('players', (list: { name: string }[]) => {
    for (const p of list) if (p && typeof p.name === 'string' && !seen.includes(p.name)) seen.push(p.name);
  });

  for (const [name, team] of [
    ['mate', 'attack'],
    ['d1', 'defense'],
    ['d2', 'defense'],
  ] as [string, string][]) {
    const bot = spawn(process.execPath, [RUNTIME, '--server', `http://localhost:${PORT}`, '--name', name, '--team', team], {
      cwd: process.cwd(),
      env: process.env,
      stdio: ['ignore', 'ignore', 'pipe'],
    });
    bot.stderr?.on('data', (chunk: Buffer) => botErrors.push(`${name}: ${chunk.toString()}`));
    bots.push(bot);
  }

  await waitFor(() => (['mate', 'd1', 'd2'].every((name) => seen.includes(name)) ? true : null), 20000, 'the three bots to join');
}, 60000);

afterAll(async () => {
  hum?.disconnect();
  hum = null;
  for (const bot of bots) await stopChild(bot);
  bots.length = 0;
  await stopChild(server);
  server = null;
  if (logDir) rmSync(logDir, { recursive: true, force: true });
});

describe('bot team play over socket.io', () => {
  it(
    'completes the round with a teammate trace carrying a team rule',
    async () => {
      if (!hum) throw new Error('no hum socket');

      const traces: TraceEvent[] = [];
      let ended: { winner?: unknown; reason?: string } | null = null;
      hum.on('trace', (msg: TraceEvent) => traces.push(msg));
      hum.on('practice-end', (msg: { winner?: unknown; reason?: string }) => {
        ended = msg;
      });
      hum.on('round-end', (msg: { winner?: unknown; reason?: string }) => {
        ended = msg;
      });

      const ack = await new Promise<{ ok: boolean; error?: string }>((resolve) => {
        hum?.emit('load-scenario', { scenario }, (reply: { ok: boolean; error?: string }) => resolve(reply));
      });
      expect(ack.ok, ack.error).toBe(true);

      const teamTrace = await waitFor(
        () => traces.find((trace) => trace.bot === 'mate' && trace.reason.includes('(team:')),
        30000,
        "a teammate trace with '(team:' in its reason",
      );
      expect(teamTrace.action.kind === 'hold' || teamTrace.action.kind === 'push').toBe(true);
      expect(teamTrace.reason).toContain("hum");
      expect(teamTrace.reason).toContain('(team: hold-other-angle)');
      expect(teamTrace.situation.side).toBe('attack');

      await waitFor(() => ended, 30000, 'practice-end or round-end');
      expect(botErrors.join('')).not.toMatch(/bot runtime failed/);
      expect(serverErrors.join('')).not.toMatch(/Error: listen EADDRINUSE/);
    },
    60000,
  );
});

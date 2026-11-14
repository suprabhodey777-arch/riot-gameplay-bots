/**
 * End-to-end check against a real game server: a defense dummy and `run.ts` on
 * port 3194, then a scenario that puts them face to face in open ground. The
 * dummy must see a tracer from the bot and receive its trace events.
 *
 * The original coordinates (0,-10) and (0,8) were blocked by the mid wall at
 * x -6..6, z -8.6..-7.4, so both players stand on x = -3, ten metres apart,
 * where `lineOfSight` is clear.
 */
import { spawn, type ChildProcess } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { io, type Socket } from 'socket.io-client';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';

import type { ScenarioFile, TraceEvent } from '../contracts.ts';
import { loadMapExport } from './nav.ts';
import { lineOfSight } from './view.ts';

const PORT = 3194;
const SERVER = join(process.cwd(), 'game', 'server.js');
const RUNTIME = join(process.cwd(), 'ghost', 'bot', 'run.ts');

let server: ChildProcess | null = null;
let bot: ChildProcess | null = null;
let dummy: Socket | null = null;
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

function waitForOutput(child: ChildProcess, needle: string, timeoutMs: number): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    let buffer = '';
    const timer = setTimeout(() => {
      cleanup();
      reject(new Error(`server did not print "${needle}" within ${timeoutMs} ms`));
    }, timeoutMs);
    const onData = (chunk: Buffer): void => {
      buffer += chunk.toString();
      if (buffer.includes(needle)) {
        cleanup();
        resolve();
      }
    };
    const onExit = (code: number | null): void => {
      cleanup();
      reject(new Error(`server exited with code ${code} before printing "${needle}"`));
    };
    function cleanup(): void {
      clearTimeout(timer);
      child.stdout?.off('data', onData);
      child.off('exit', onExit);
    }
    child.stdout?.on('data', onData);
    child.once('exit', onExit);
  });
}

async function stopChild(child: ChildProcess | null): Promise<void> {
  if (!child || child.pid == null || child.exitCode !== null) return;
  const pid = child.pid;
  try {
    process.kill(pid, 'SIGTERM');
  } catch (err) {
    console.error(`run.test: could not signal pid ${pid}: ${err instanceof Error ? err.message : String(err)}`);
    return;
  }
  await new Promise<void>((resolve) => {
    const timer = setTimeout(() => {
      try {
        process.kill(pid);
      } catch (err) {
        console.error(`run.test: could not force pid ${pid}: ${err instanceof Error ? err.message : String(err)}`);
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
  id: 'ghost-bot-run-test',
  source: null,
  phase: 'live',
  timer: 60,
  spike: { planted: false, site: null, pos: null, explodeTimer: 0, holder: null, dropped: null },
  players: [
    {
      name: 'bot1',
      team: 'attack',
      pos: { x: -3, y: 0, z: -20 },
      yaw: Math.PI,
      pitch: 0,
      hp: 100,
      armor: 0,
      weapon: 'vandal',
      hasSpike: false,
      ultPoints: 0,
    },
    {
      name: 'dummy',
      team: 'defense',
      pos: { x: -3, y: 0, z: -10 },
      yaw: 0,
      pitch: 0,
      hp: 100,
      armor: 0,
      weapon: 'classic',
      hasSpike: false,
      ultPoints: 0,
    },
  ],
  practice: true,
};

beforeAll(async () => {
  logDir = mkdtempSync(join(tmpdir(), 'ghost-bot-run-'));
  expect(lineOfSight({ x: -3, y: 1.62, z: -20 }, { x: -3, y: 1.2, z: -10 }, loadMapExport().colliders, [])).toBe(true);

  server = spawn(process.execPath, [SERVER], {
    cwd: process.cwd(),
    env: { ...process.env, PORT: String(PORT), GHOST_LOG_DIR: logDir },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  server.stderr?.on('data', (chunk: Buffer) => serverErrors.push(chunk.toString()));
  await waitForOutput(server, 'Ghost Rematch on', 15000);

  dummy = io(`http://localhost:${PORT}`, { transports: ['websocket'], reconnection: false, forceNew: true });
  dummy.on('connect', () => {
    dummy?.emit('join', { name: 'dummy', team: 'defense', agent: 'omen' });
  });
  const joined = await new Promise<{ me: { name: string; team: string } }>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('dummy did not join')), 10000);
    dummy?.once('joined', (msg: { me: { name: string; team: string } }) => {
      clearTimeout(timer);
      resolve(msg);
    });
  });
  expect(joined.me.name).toBe('dummy');

  const seen: string[] = [];
  dummy.on('players', (list: { name: string }[]) => {
    for (const p of list) if (p && typeof p.name === 'string' && !seen.includes(p.name)) seen.push(p.name);
  });

  bot = spawn(process.execPath, [RUNTIME, '--server', `http://localhost:${PORT}`, '--name', 'bot1', '--team', 'attack'], {
    cwd: process.cwd(),
    env: process.env,
    stdio: ['ignore', 'ignore', 'pipe'],
  });
  bot.stderr?.on('data', (chunk: Buffer) => botErrors.push(chunk.toString()));

  await waitFor(() => (seen.includes('bot1') ? true : null), 15000, 'bot1 to join');
}, 40000);

afterAll(async () => {
  dummy?.disconnect();
  dummy = null;
  await stopChild(bot);
  bot = null;
  await stopChild(server);
  server = null;
  if (logDir) rmSync(logDir, { recursive: true, force: true });
});

describe('bot runtime over socket.io', () => {
  it(
    'fires a tracer and sends trace events a nearby player can receive',
    async () => {
      if (!dummy) throw new Error('no dummy socket');

      const tracers: { from?: unknown; dir?: unknown; id?: string }[] = [];
      const traces: TraceEvent[] = [];
      dummy.on('tracer', (msg: { from?: unknown; dir?: unknown; id?: string }) => tracers.push(msg));
      dummy.on('trace', (msg: TraceEvent) => traces.push(msg));

      const ack = await new Promise<{ ok: boolean; error?: string }>((resolve) => {
        dummy?.emit('load-scenario', { scenario }, (reply: { ok: boolean; error?: string }) => resolve(reply));
      });
      expect(ack.ok, ack.error).toBe(true);

      await waitFor(() => (tracers.length > 0 ? true : null), 15000, 'a tracer from bot1');
      await waitFor(() => (traces.length >= 3 ? true : null), 15000, 'three trace events');

      const shot = tracers[0];
      expect(Array.isArray(shot.from)).toBe(true);
      expect(Array.isArray(shot.dir)).toBe(true);
      expect(shot.from).toHaveLength(3);
      expect(shot.dir).toHaveLength(3);

      const validKinds = new Set([
        'hold',
        'peek_wide',
        'peek_jiggle',
        'fight',
        'push',
        'retreat',
        'rotate',
        'use_ability',
        'plant',
        'defuse',
        'buy',
      ]);
      for (const trace of traces.slice(0, 3)) {
        expect(trace.bot).toBe('bot1');
        expect(validKinds.has(trace.action.kind)).toBe(true);
        expect(trace.situation.side).toBe('attack');
        expect(typeof trace.round).toBe('number');
        expect(typeof trace.t).toBe('number');
        expect(typeof trace.reason).toBe('string');
        expect(trace.reason.length).toBeGreaterThan(0);
      }
      expect(botErrors.join('')).not.toMatch(/bot runtime failed/);
      expect(serverErrors.join('')).not.toMatch(/Error: listen EADDRINUSE/);
    },
    40000,
  );
});

import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import type { Server } from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterAll, describe, expect, test } from 'vitest';
import { binomialTestVsChance, diffCI, summarize } from './stats.ts';

const requireJs = createRequire(import.meta.url);
const express = requireJs('../../game/node_modules/express') as () => any;
const { mountGhostRoutes } = requireJs('../../game/ghost-routes.js') as {
  mountGhostRoutes: (app: any, opts?: { dataDir?: string }) => unknown;
};

const dataDir = mkdtempSync(join(tmpdir(), 'ghost-evidence-'));
const app = express();
mountGhostRoutes(app, { dataDir });
const server: Server = app.listen(0, '127.0.0.1');

await new Promise<void>((resolve, reject) => {
  server.once('listening', resolve);
  server.once('error', reject);
});

const address = server.address();
if (!address || typeof address === 'string') throw new Error('test server has no port');
const base = `http://127.0.0.1:${address.port}`;

afterAll(async () => {
  await new Promise<void>((resolve) => server.close(() => resolve()));
  rmSync(dataDir, { recursive: true, force: true });
});

async function postJson(path: string, body: unknown): Promise<{ status: number; body: any }> {
  const res = await fetch(`${base}${path}`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
  });
  return { status: res.status, body: await res.json() };
}

async function getJson(path: string): Promise<{ status: number; body: any }> {
  const res = await fetch(`${base}${path}`);
  return { status: res.status, body: await res.json() };
}

function lines(file: string): string[] {
  return readFileSync(join(dataDir, file), 'utf8')
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean);
}

describe('GET /api/results on an empty data directory', () => {
  test('returns zeros and nulls, not an error', async () => {
    const { status, body } = await getJson('/api/results');
    expect(status).toBe(200);
    expect(body.guess).toEqual({ n: 0, correct: 0, share: null, chance: 0.5, pValue: null, byClip: {} });
    expect(body.transfer.nGhost).toBe(0);
    expect(body.transfer.nDefault).toBe(0);
    expect(body.transfer.ghost).toEqual({ label: 'ghost', n: 0, mean: null, ci: null, baseline: null });
    expect(body.transfer.default).toEqual({ label: 'default', n: 0, mean: null, ci: null, baseline: null });
    expect(body.transfer.diff).toEqual({ mean: null, ci: null });
  });
});

describe('POST /api/guess', () => {
  test('rejects invalid bodies', async () => {
    for (const body of [
      {},
      { tester: 'a', clipId: 'c1', guess: 'A' },
      { tester: '', clipId: 'c1', guess: 'A', truth: 'A' },
      { tester: 'a', clipId: '   ', guess: 'A', truth: 'A' },
      { tester: 'a', clipId: 'c1', guess: 3, truth: 'A' },
      { tester: 'a', clipId: 'c1', guess: 'A', truth: null },
    ]) {
      const { status, body: out } = await postJson('/api/guess', body);
      expect(status).toBe(400);
      expect(out.ok).toBe(false);
      expect(typeof out.error).toBe('string');
    }
  });

  test('rejects a body that is not JSON', async () => {
    const res = await fetch(`${base}/api/guess`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: '{not json',
    });
    expect(res.status).toBe(400);
    expect(await res.json()).toEqual({ ok: false, error: 'request body is not valid JSON' });
  });

  test('records answers in guesses.jsonl with a server timestamp', async () => {
    const answers = [
      { tester: 'ana', clipId: 'c1', guess: 'A', truth: 'A' },
      { tester: 'ana', clipId: 'c1', guess: 'A', truth: 'A' },
      { tester: 'bo', clipId: 'c1', guess: 'B', truth: 'A' },
      { tester: 'bo', clipId: 'c2', guess: 'B', truth: 'B' },
    ];
    for (const answer of answers) {
      const { status, body } = await postJson('/api/guess', answer);
      expect(status).toBe(200);
      expect(body).toEqual({ ok: true });
    }
    const rows = lines('guesses.jsonl').map((line) => JSON.parse(line));
    expect(rows).toHaveLength(4);
    expect(rows.map((r) => r.tester)).toEqual(['ana', 'ana', 'bo', 'bo']);
    for (const row of rows) {
      expect(row.clipId).toBeTruthy();
      expect(row.guess).toBeTruthy();
      expect(row.truth).toBeTruthy();
      expect(new Date(row.at).toISOString()).toBe(row.at);
    }
  });
});

describe('POST /api/transfer/assign', () => {
  test('rejects an invalid body', async () => {
    const { status, body } = await postJson('/api/transfer/assign', { tester: '' });
    expect(status).toBe(400);
    expect(body.ok).toBe(false);
  });

  test('assigns 10 testers in balanced blocks of two (5 ghost, 5 default)', async () => {
    const groups: Record<string, string> = {};
    for (let i = 0; i < 10; i++) {
      const { status, body } = await postJson('/api/transfer/assign', { tester: `t${i}` });
      expect(status).toBe(200);
      expect(['ghost', 'default']).toContain(body.group);
      groups[`t${i}`] = body.group;
    }
    const values = Object.values(groups);
    expect(values.filter((g) => g === 'ghost')).toHaveLength(5);
    expect(values.filter((g) => g === 'default')).toHaveLength(5);

    const stored = JSON.parse(readFileSync(join(dataDir, 'transfer-groups.json'), 'utf8'));
    expect(Object.keys(stored.assignments)).toHaveLength(10);
    for (const [tester, group] of Object.entries(groups)) {
      expect(stored.assignments[tester].group).toBe(group);
      expect(new Date(stored.assignments[tester].at).toISOString()).toBe(stored.assignments[tester].at);
    }
  });

  test('re-assigning returns the same group', async () => {
    const before = JSON.parse(readFileSync(join(dataDir, 'transfer-groups.json'), 'utf8'));
    const first = await postJson('/api/transfer/assign', { tester: 't0' });
    const second = await postJson('/api/transfer/assign', { tester: 't0' });
    expect(first.body.group).toBe(second.body.group);
    const after = JSON.parse(readFileSync(join(dataDir, 'transfer-groups.json'), 'utf8'));
    expect(after).toEqual(before);
  });
});

describe('POST /api/transfer/result', () => {
  test('rejects unassigned testers and invalid counts', async () => {
    const unassigned = await postJson('/api/transfer/result', {
      tester: 'nobody',
      opponent: 'X',
      roundsWon: 1,
      roundsPlayed: 2,
    });
    expect(unassigned.status).toBe(400);

    for (const body of [
      { tester: 't0', opponent: 'X', roundsWon: 3, roundsPlayed: 2 },
      { tester: 't0', opponent: 'X', roundsWon: -1, roundsPlayed: 2 },
      { tester: 't0', opponent: 'X', roundsWon: 1.5, roundsPlayed: 4 },
      { tester: 't0', opponent: 'X', roundsWon: 1, roundsPlayed: 4.5 },
      { tester: 't0', opponent: '', roundsWon: 1, roundsPlayed: 4 },
    ]) {
      const { status, body: out } = await postJson('/api/transfer/result', body);
      expect(status).toBe(400);
      expect(out.ok).toBe(false);
    }
  });

  test('records one result per tester and feeds the transfer summary', async () => {
    const stored = JSON.parse(readFileSync(join(dataDir, 'transfer-groups.json'), 'utf8')).assignments as Record<
      string,
      { group: 'ghost' | 'default' }
    >;
    const ghostShares = [1, 0.5, 0.75, 0.25, 0];
    const defaultShares = [0.5, 0, 1, 0.25, 0.75];
    let ghostIndex = 0;
    let defaultIndex = 0;
    for (let i = 0; i < 10; i++) {
      const tester = `t${i}`;
      const group = stored[tester].group;
      const share = group === 'ghost' ? ghostShares[ghostIndex++] : defaultShares[defaultIndex++];
      const roundsWon = share * 4;
      const { status, body } = await postJson('/api/transfer/result', {
        tester,
        opponent: 'X',
        roundsWon,
        roundsPlayed: 4,
      });
      expect(status).toBe(200);
      expect(body).toEqual({ ok: true });
    }
    expect(ghostIndex).toBe(5);
    expect(defaultIndex).toBe(5);

    const rows = lines('transfer-results.jsonl').map((line) => JSON.parse(line));
    expect(rows).toHaveLength(10);
    for (const row of rows) {
      expect(row.opponent).toBe('X');
      expect(row.roundsPlayed).toBe(4);
      expect(new Date(row.at).toISOString()).toBe(row.at);
    }

    const { status, body } = await getJson('/api/results');
    expect(status).toBe(200);
    expect(body.transfer.nGhost).toBe(5);
    expect(body.transfer.nDefault).toBe(5);
    expect(body.transfer.ghost).toEqual(summarize('ghost', ghostShares));
    expect(body.transfer.default).toEqual(summarize('default', defaultShares));
    const expectedDiff = diffCI(ghostShares, defaultShares);
    expect(body.transfer.diff.mean).toBeCloseTo((0.5 - 0.5) as number, 12);
    expect(body.transfer.diff.ci).toEqual({ lo: expectedDiff.lo, hi: expectedDiff.hi });
  });
});

describe('GET /api/results on the known dataset', () => {
  test('guess headline matches the recorded answers', async () => {
    const { status, body } = await getJson('/api/results');
    expect(status).toBe(200);
    const guess = body.guess;
    expect(guess.n).toBe(4);
    expect(guess.correct).toBe(3);
    expect(guess.share).toBeCloseTo(0.75, 12);
    expect(guess.chance).toBe(0.5);
    expect(guess.pValue).toBeCloseTo(binomialTestVsChance(3, 4, 0.5), 12);
    expect(guess.byClip).toEqual({
      c1: { n: 3, correct: 2, share: 2 / 3 },
      c2: { n: 1, correct: 1, share: 1 },
    });
  });
});

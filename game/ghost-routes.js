/**
 * Evidence routes for the human tests. Mounted by `game/server.js`:
 *
 *   const { mountGhostRoutes } = require('./ghost-routes.js');
 *   mountGhostRoutes(app);
 *
 * All routes return JSON. Data lives in `evidence/data` by default (override with
 * the `dataDir` option, used by tests):
 *
 *   guesses.jsonl          one line per guess: {tester, clipId, guess, truth, at}
 *   transfer-groups.json   {assignments: {tester: {group, at}}}
 *   transfer-results.jsonl one line per result: {tester, opponent, roundsWon, roundsPlayed, at}
 *
 * Assignment uses balanced blocks of two: while the group counts are tied a coin
 * decides who goes first, then the next tester is forced into the other group.
 * Groups therefore never differ by more than one.
 */

'use strict';

const express = require('express');
const fs = require('fs');
const path = require('path');
const stats = require('./ghost-stats.js');

const GROUPS = ['ghost', 'default'];

function nonEmptyString(value) {
  return typeof value === 'string' && value.trim() !== '';
}

function trim(value) {
  return value.trim();
}

function readJson(file, fallback) {
  let text;
  try {
    text = fs.readFileSync(file, 'utf8');
  } catch (err) {
    if (err && err.code === 'ENOENT') return fallback;
    throw err;
  }
  return JSON.parse(text);
}

function readJsonl(file) {
  let text;
  try {
    text = fs.readFileSync(file, 'utf8');
  } catch (err) {
    if (err && err.code === 'ENOENT') return [];
    throw err;
  }
  const rows = [];
  text.split('\n').forEach((line, index) => {
    const trimmed = line.trim();
    if (!trimmed) return;
    try {
      rows.push(JSON.parse(trimmed));
    } catch (err) {
      throw new Error(`${path.basename(file)} line ${index + 1} is not JSON: ${err.message}`);
    }
  });
  return rows;
}

function appendJsonl(file, record) {
  fs.appendFileSync(file, `${JSON.stringify(record)}\n`);
}

function mountGhostRoutes(app, opts = {}) {
  if (!app || typeof app.use !== 'function') throw new Error('mountGhostRoutes(app, ...): app must be an Express app');
  const dataDir = path.resolve(opts.dataDir || path.join(__dirname, '..', 'evidence', 'data'));
  fs.mkdirSync(dataDir, { recursive: true });
  const files = {
    guesses: path.join(dataDir, 'guesses.jsonl'),
    groups: path.join(dataDir, 'transfer-groups.json'),
    results: path.join(dataDir, 'transfer-results.jsonl'),
  };

  const loadGroups = () => {
    const raw = readJson(files.groups, { assignments: {} });
    if (!raw || typeof raw !== 'object' || !raw.assignments || typeof raw.assignments !== 'object') {
      throw new Error('transfer-groups.json has no assignments object');
    }
    return raw;
  };

  const saveGroups = (state) => {
    fs.writeFileSync(files.groups, `${JSON.stringify(state, null, 2)}\n`);
  };

  const router = express.Router();
  router.use(express.json());

  // ------------------------------------------------------------------ guesses
  router.post('/api/guess', (req, res) => {
    const body = req.body && typeof req.body === 'object' ? req.body : {};
    for (const field of ['tester', 'clipId', 'guess', 'truth']) {
      if (!nonEmptyString(body[field])) {
        res.status(400).json({ ok: false, error: `${field} must be a non-empty string` });
        return;
      }
    }
    const record = {
      tester: trim(body.tester),
      clipId: trim(body.clipId),
      guess: trim(body.guess),
      truth: trim(body.truth),
      at: new Date().toISOString(),
    };
    appendJsonl(files.guesses, record);
    res.json({ ok: true });
  });

  // --------------------------------------------------------- transfer groups
  router.post('/api/transfer/assign', (req, res) => {
    const body = req.body && typeof req.body === 'object' ? req.body : {};
    if (!nonEmptyString(body.tester)) {
      res.status(400).json({ ok: false, error: 'tester must be a non-empty string' });
      return;
    }
    const tester = trim(body.tester);
    const state = loadGroups();
    const existing = state.assignments[tester];
    if (existing && GROUPS.includes(existing.group)) {
      res.json({ group: existing.group });
      return;
    }

    const counts = { ghost: 0, default: 0 };
    for (const assignment of Object.values(state.assignments)) {
      if (assignment && GROUPS.includes(assignment.group)) counts[assignment.group]++;
    }
    let group;
    if (counts.ghost < counts.default) group = 'ghost';
    else if (counts.default < counts.ghost) group = 'default';
    else group = Math.random() < 0.5 ? 'ghost' : 'default';

    state.assignments[tester] = { group, at: new Date().toISOString() };
    saveGroups(state);
    res.json({ group });
  });

  // --------------------------------------------------------- transfer result
  router.post('/api/transfer/result', (req, res) => {
    const body = req.body && typeof req.body === 'object' ? req.body : {};
    if (!nonEmptyString(body.tester) || !nonEmptyString(body.opponent)) {
      res.status(400).json({ ok: false, error: 'tester and opponent must be non-empty strings' });
      return;
    }
    if (!Number.isInteger(body.roundsWon) || !Number.isInteger(body.roundsPlayed)) {
      res.status(400).json({ ok: false, error: 'roundsWon and roundsPlayed must be integers' });
      return;
    }
    if (body.roundsWon < 0 || body.roundsPlayed < 0 || body.roundsWon > body.roundsPlayed) {
      res.status(400).json({ ok: false, error: 'need 0 <= roundsWon <= roundsPlayed' });
      return;
    }
    const tester = trim(body.tester);
    const state = loadGroups();
    const assignment = state.assignments[tester];
    if (!assignment || !GROUPS.includes(assignment.group)) {
      res.status(400).json({ ok: false, error: 'tester is not assigned to a transfer group' });
      return;
    }
    appendJsonl(files.results, {
      tester,
      opponent: trim(body.opponent),
      roundsWon: body.roundsWon,
      roundsPlayed: body.roundsPlayed,
      at: new Date().toISOString(),
    });
    res.json({ ok: true });
  });

  // --------------------------------------------------------------- summary
  router.get('/api/results', (req, res) => {
    const guesses = readJsonl(files.guesses);
    const validGuesses = guesses.filter(
      (g) => nonEmptyString(g.tester) && nonEmptyString(g.clipId) && nonEmptyString(g.guess) && nonEmptyString(g.truth),
    );
    const n = validGuesses.length;
    const correct = validGuesses.filter((g) => g.guess === g.truth).length;
    const truths = new Set(validGuesses.map((g) => g.truth));
    const chance = 1 / Math.max(2, truths.size);
    const share = n > 0 ? correct / n : null;
    const pValue = n > 0 ? stats.binomialTestVsChance(correct, n, chance) : null;

    const byClip = {};
    for (const g of validGuesses) {
      if (!byClip[g.clipId]) byClip[g.clipId] = { n: 0, correct: 0, share: null };
      byClip[g.clipId].n++;
      if (g.guess === g.truth) byClip[g.clipId].correct++;
    }
    for (const entry of Object.values(byClip)) entry.share = entry.n > 0 ? entry.correct / entry.n : null;

    const state = loadGroups();
    const latestByTester = new Map();
    for (const row of readJsonl(files.results)) {
      if (!nonEmptyString(row.tester) || !Number.isInteger(row.roundsWon) || !Number.isInteger(row.roundsPlayed)) continue;
      if (row.roundsPlayed <= 0 || row.roundsWon < 0 || row.roundsWon > row.roundsPlayed) continue;
      latestByTester.set(row.tester, row);
    }
    const ghostShares = [];
    const defaultShares = [];
    for (const [tester, row] of latestByTester) {
      const assignment = state.assignments[tester];
      if (!assignment) continue;
      if (assignment.group === 'ghost') ghostShares.push(row.roundsWon / row.roundsPlayed);
      else if (assignment.group === 'default') defaultShares.push(row.roundsWon / row.roundsPlayed);
    }
    const ghost = stats.summarize('ghost', ghostShares);
    const def = stats.summarize('default', defaultShares);
    const difference = stats.diffCI(ghostShares, defaultShares);

    res.json({
      guess: { n, correct, share, chance, pValue, byClip },
      transfer: {
        ghost,
        default: def,
        diff: {
          mean: difference.mean,
          ci: difference.lo === null ? null : { lo: difference.lo, hi: difference.hi },
        },
        nGhost: ghostShares.length,
        nDefault: defaultShares.length,
      },
    });
  });

  // Keep the API JSON-only, including malformed request bodies.
  router.use((err, req, res, next) => {
    if (res.headersSent) return next(err);
    const status = err.status || err.statusCode || 500;
    const error = err.type === 'entity.parse.failed' ? 'request body is not valid JSON' : status >= 500 ? 'internal error' : String(err.message || err);
    res.status(status).json({ ok: false, error });
  });

  app.use(router);
  return router;
}

module.exports = { mountGhostRoutes };

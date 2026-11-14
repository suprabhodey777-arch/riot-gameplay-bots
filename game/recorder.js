// Match recorder: appends one JSON line per logged action to <logDir>/<matchId>.jsonl.
// Shapes match `LogEvent` in ghost/contracts.ts. Writes are synchronous so a reader
// (the ghost test, the analyzer) can see a line as soon as the action is accepted.
const fs = require('fs');
const path = require('path');

// 2026-11-14T20-31-05: ISO UTC up to the second, colons replaced for a safe file name.
function defaultMatchId(now = new Date()) {
  return now.toISOString().slice(0, 19).replace(/:/g, '-');
}

function defaultLogDir() {
  return path.join(__dirname, '..', 'logs');
}

/**
 * createRecorder({ logDir, matchId, state })
 * `state()` returns { round, t }: the round number and seconds since the round started.
 */
function createRecorder({ logDir = defaultLogDir(), matchId = defaultMatchId(), state }) {
  if (typeof state !== 'function') throw new Error('createRecorder needs a state() function');
  fs.mkdirSync(logDir, { recursive: true });
  const file = path.join(logDir, `${matchId}.jsonl`);

  function log(player, type, fields = {}) {
    const { round, t } = state();
    const line = { v: 1, matchId, round, t, player: player || '', type, ...fields };
    fs.appendFileSync(file, `${JSON.stringify(line)}\n`);
  }

  return { log, path: file, matchId };
}

module.exports = { createRecorder, defaultMatchId, defaultLogDir };

// Ghost lobby: list the ghosts built under `GHOST_PROFILES_DIR`, spawn one (or
// a default bot) into either team on request, remove a bot this server started,
// and report which bots it is running.
//
// Filling both teams with ghosts makes a 2v2 up to a 5v5 practice match; the
// game itself is 5v5, so a full team refuses more bots. Shapes for the browser
// live in the `ghost-list`, `add-bot`, `remove-bot` and `lobby-bots` acks.
const fs = require('fs');
const path = require('path');
const bots = require('./bots');

const MAX_NAME = 12;
const GHOST_SUFFIX = '·ghost';
const TEAM_SIZE = 5;
const JOIN_TIMEOUT_MS = 8000;
const JOIN_POLL_MS = 100;

// How each action kind reads in the one-line style summary.
const ACTION_WORDS = {
  hold: { one: 'Holds', many: 'holds' },
  push: { one: 'Pushes', many: 'pushes' },
  fight: { one: 'Fights', many: 'fights' },
  peek_wide: { one: 'Wide peeks', many: 'wide peeks' },
  peek_jiggle: { one: 'Jiggle peeks', many: 'jiggle peeks' },
  retreat: { one: 'Falls back', many: 'falls back' },
  rotate: { one: 'Rotates', many: 'rotates' },
  use_ability: { one: 'Uses abilities', many: 'uses abilities' },
  plant: { one: 'Plants', many: 'plants' },
  defuse: { one: 'Defuses', many: 'defuses' },
  buy: { one: 'Buys', many: 'buys' },
};

/** The profile directory: `GHOST_PROFILES_DIR`, default `<repo>/profiles`. */
const profilesDir = () => bots.profilesDir();

function readJson(file) {
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch (err) {
    if (err.code !== 'ENOENT') console.error(`lobby: could not read ${file}: ${err.message}`);
    return null;
  }
}

/** One generated line from the profile: the two biggest action shares and the reaction time. */
function styleOf(profile) {
  const mix = profile && profile.actionMix && typeof profile.actionMix === 'object' ? profile.actionMix : {};
  const shares = Object.entries(mix)
    .filter(([kind, share]) => ACTION_WORDS[kind] && Number.isFinite(share) && share > 0)
    .sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? -1 : 1))
    .slice(0, 2)
    .map(([kind, share], index) => `${index === 0 ? ACTION_WORDS[kind].one : ACTION_WORDS[kind].many} ${Math.round(share * 100)}%`);
  const reactionMs = Number(profile && profile.reactionMs);
  const react = Number.isFinite(reactionMs) && reactionMs > 0 ? `reacts in ${Math.round(reactionMs)} ms` : null;
  const parts = [];
  if (shares.length) parts.push(shares.join(', '));
  if (react) parts.push(react);
  return parts.length ? parts.join(' · ') : 'No recorded actions yet';
}

/** `profiles/report.json` -> player -> { agreement, baseline, n }, tolerating a missing file. */
function readHeldout(dir) {
  const report = readJson(path.join(dir, 'report.json'));
  const players = report && report.players && typeof report.players === 'object' ? report.players : {};
  const out = new Map();
  for (const [player, item] of Object.entries(players)) {
    if (!item || typeof item !== 'object') continue;
    const agreement = item.ghost && Number.isFinite(item.ghost.agreement) ? item.ghost.agreement : null;
    if (agreement == null) continue;
    const n = item.ghost && Number.isFinite(item.ghost.n) ? item.ghost.n : 0;
    const baseline = item.baseline && Number.isFinite(item.baseline.agreement) ? item.baseline.agreement : null;
    out.set(player, { agreement, baseline, n });
  }
  return out;
}

/** Every `profiles/<player>.json` ghost, newest-recording heavy first. */
function listGhosts() {
  const dir = profilesDir();
  let files;
  try {
    files = fs.readdirSync(dir).filter((file) => file.endsWith('.json'));
  } catch (err) {
    if (err.code !== 'ENOENT') console.error(`lobby: could not list ${dir}: ${err.message}`);
    return [];
  }
  const heldout = readHeldout(dir);
  const ghosts = [];
  for (const file of files) {
    if (file === 'report.json' || file.endsWith('.samples.json')) continue;
    const profile = readJson(path.join(dir, file));
    if (!profile || typeof profile !== 'object' || Array.isArray(profile)) continue;
    const player = typeof profile.player === 'string' && profile.player ? profile.player : file.replace(/\.json$/, '');
    ghosts.push({
      player,
      roundsRecorded: Number.isFinite(profile.roundsRecorded) ? profile.roundsRecorded : 0,
      style: styleOf(profile),
      heldout: heldout.get(player) || null,
    });
  }
  ghosts.sort((a, b) => b.roundsRecorded - a.roundsRecorded || (a.player < b.player ? -1 : 1));
  return ghosts;
}

function hasProfile(player) {
  return fs.existsSync(path.join(profilesDir(), `${player}.json`));
}

/** The name `game/server.js` will give this base name, copying its `uniqueName` rule. */
function uniqueName(players, base) {
  const taken = new Set([...players.values()].map((p) => p.name));
  if (!taken.has(base)) return base;
  for (let n = 2; ; n++) {
    const candidate = `${base}-${n}`;
    if (!taken.has(candidate)) return candidate;
  }
}

function ghostNameFor(player) {
  const keep = Math.max(0, MAX_NAME - GHOST_SUFFIX.length);
  return `${String(player).slice(0, keep)}${GHOST_SUFFIX}`;
}

function defaultBotName(players) {
  const taken = new Set([...players.values()].map((p) => p.name));
  for (let n = 1; ; n++) {
    const candidate = `bot-${n}`;
    if (!taken.has(candidate)) return candidate;
  }
}

/** The new bot player once it is in `players`, under `wanted` or `wanted-2` ... after a clash. */
function waitForBot(players, before, wanted, team, timeoutMs) {
  return new Promise((resolve) => {
    const deadline = Date.now() + timeoutMs;
    const match = () => {
      for (const p of players.values()) {
        if (!p || !p.bot || p.team !== team || before.has(p.name)) continue;
        if (p.name === wanted || p.name.startsWith(`${wanted}-`)) return p;
      }
      return null;
    };
    const poll = () => {
      const found = match();
      if (found) {
        resolve(found);
        return;
      }
      if (Date.now() >= deadline) {
        resolve(null);
        return;
      }
      setTimeout(poll, JOIN_POLL_MS);
    };
    poll();
  });
}

function attachLobby({ io, players, port, rematch }) {
  const running = new Map(); // joined name -> { name, spawnName, team, ghostOf }

  function teamSize(team) {
    let count = 0;
    for (const p of players.values()) if (p.team === team) count++;
    return count;
  }

  function lobbyBots() {
    return [...running.values()].map((bot) => ({ name: bot.name, team: bot.team, ghostOf: bot.ghostOf }));
  }

  async function addBot(payload) {
    const ghostOf = payload && typeof payload.ghostOf === 'string' && payload.ghostOf ? payload.ghostOf : null;
    const team = payload ? payload.team : null;
    if (team !== 'attack' && team !== 'defense') return { ok: false, error: 'team must be attack or defense' };
    if (rematch && rematch.isDrillActive && rematch.isDrillActive()) {
      return { ok: false, error: 'a rematch drill is running; leave practice first' };
    }
    if (teamSize(team) >= TEAM_SIZE) return { ok: false, error: `the ${team} team already has ${TEAM_SIZE} players` };
    if (ghostOf && !hasProfile(ghostOf)) return { ok: false, error: `no recorded profile for ${ghostOf}` };

    const before = new Set([...players.values()].map((p) => p.name));
    const base = ghostOf ? ghostNameFor(ghostOf) : defaultBotName(players);
    const wanted = uniqueName(players, base);
    const started = bots.spawnBot({ name: wanted, team, ghostOf, port });
    if (started.error) return { ok: false, error: started.error };

    const joined = await waitForBot(players, before, wanted, team, JOIN_TIMEOUT_MS);
    if (!joined) {
      bots.killBot(wanted);
      return { ok: false, error: 'the bot did not join' };
    }
    running.set(joined.name, { name: joined.name, spawnName: wanted, team, ghostOf });
    return { ok: true, name: joined.name };
  }

  io.on('connection', (socket) => {
    const ackOf = (first, second) => (typeof first === 'function' ? first : typeof second === 'function' ? second : () => {});

    socket.on('ghost-list', (first) => {
      const reply = ackOf(first);
      try {
        reply({ ok: true, ghosts: listGhosts() });
      } catch (err) {
        console.error(`lobby: could not list ghosts: ${err && err.message ? err.message : err}`);
        reply({ ok: false, error: 'could not read the ghost profiles' });
      }
    });

    socket.on('add-bot', (payload, ack) => {
      const reply = ackOf(payload, ack);
      addBot(payload).then(reply, (err) => {
        console.error(`lobby: could not add a bot: ${err && err.message ? err.message : err}`);
        reply({ ok: false, error: 'could not add the bot' });
      });
    });

    socket.on('remove-bot', (payload, ack) => {
      const reply = ackOf(payload, ack);
      const name = payload && typeof payload.name === 'string' ? payload.name : '';
      const bot = running.get(name);
      if (!bot) {
        reply({ ok: false, error: `no bot called ${name || 'that'} was spawned by this server` });
        return;
      }
      running.delete(name);
      if (!bots.killBot(bot.spawnName)) {
        reply({ ok: false, error: `${name} is no longer running` });
        return;
      }
      reply({ ok: true });
    });

    socket.on('lobby-bots', (first) => {
      const reply = ackOf(first);
      reply({ ok: true, bots: lobbyBots() });
    });
  });

  const onSignal = () => {
    bots.killAll();
    process.exit(0);
  };
  process.once('exit', bots.killAll);
  process.once('SIGINT', onSignal);
  process.once('SIGTERM', onSignal);

  return { listGhosts, lobbyBots };
}

module.exports = { attachLobby, listGhosts, styleOf };

// Spawning and tracking the bot processes the Ghost Rematch server owns: the
// drills in `game/rematch.js` and the lobby bots in `game/lobby.js`.
//
// Every bot is `node <repo>/ghost/bot/run.ts --server <url> --name <name>
// --team attack|defense [--ghost-of <player>]`, which joins like a browser
// player. Children are tracked by PID so a server can stop exactly the bots it
// started. `GHOST_BOT_CMD=''` turns spawning off (the tests use it when they
// connect their own bots).
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const REPO_ROOT = path.join(__dirname, '..');
const BOT_RUNTIME = path.join(REPO_ROOT, 'ghost', 'bot', 'run.ts');
const JOIN_POLL_MS = 100;

const children = new Map(); // pid -> { child, name }

/** `GHOST_PROFILES_DIR`, default `<repo>/profiles`: where `<player>.json` lives. */
function profilesDir() {
  return process.env.GHOST_PROFILES_DIR || path.join(REPO_ROOT, 'profiles');
}

/**
 * Start one bot. `{ name, team, ghostOf, port }` -> `{ pid, child }` or
 * `{ error }` when spawning is off, the runtime is missing or the process
 * could not be started. Never throws.
 */
function spawnBot({ name, team, ghostOf, port }) {
  if (process.env.GHOST_BOT_CMD === '') return { error: 'spawning bots is turned off on this server' };
  if (!fs.existsSync(BOT_RUNTIME)) return { error: 'bot runtime not built yet' };

  const args = [BOT_RUNTIME, '--server', `http://localhost:${port}`, '--name', name, '--team', team];
  if (ghostOf) args.push('--ghost-of', ghostOf);

  let child;
  try {
    child = spawn(process.execPath, args, {
      cwd: REPO_ROOT,
      env: process.env,
      stdio: ['ignore', 'ignore', 'pipe'],
    });
  } catch (err) {
    return { error: `could not start the bot: ${err && err.message ? err.message : err}` };
  }

  if (child.pid == null) return { error: 'could not start the bot' };
  const pid = child.pid;
  children.set(pid, { child, name });

  child.on('error', (err) => {
    children.delete(pid);
    console.error(`bots: ${name} failed to start: ${err.message}`);
  });
  if (child.stderr) {
    child.stderr.on('data', (chunk) => {
      const text = String(chunk).trim();
      if (text) console.error(`bots: ${name}: ${text}`);
    });
  }
  child.on('exit', (code, signal) => {
    children.delete(pid);
    if (signal == null && code !== 0) console.error(`bots: ${name} exited with code ${code}`);
  });
  return { pid, child };
}

/**
 * Resolve with the joined player once `players` (a Map of the server's player
 * objects) holds this exact name, or null after `timeoutMs`.
 */
function waitForJoin(players, name, timeoutMs = 8000) {
  return new Promise((resolve) => {
    const deadline = Date.now() + timeoutMs;
    const poll = () => {
      for (const player of players.values()) {
        if (player && player.name === name) {
          resolve(player);
          return;
        }
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

/** Stop the bot this server started under this name, if any. */
function killBot(name) {
  for (const [pid, entry] of [...children.entries()]) {
    if (entry.name !== name) continue;
    children.delete(pid);
    try {
      entry.child.kill();
    } catch (err) {
      console.error(`bots: could not stop ${name} (pid ${pid}): ${err.message}`);
    }
    return true;
  }
  return false;
}

/** Stop every bot this server started. */
function killAll() {
  for (const [pid, entry] of [...children.entries()]) {
    children.delete(pid);
    try {
      entry.child.kill();
    } catch (err) {
      console.error(`bots: could not stop ${entry.name} (pid ${pid}): ${err.message}`);
    }
  }
}

/** The PIDs still tracked as running, for tests and shutdown logs. */
function pids() {
  return [...children.keys()];
}

module.exports = { spawnBot, waitForJoin, killBot, killAll, pids, profilesDir };

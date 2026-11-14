// Ghost Rematch server logic: list the deaths in the match logs, rebuild one as
// a drill against the killer's ghost bot, retry it, tune the ghost's skill and
// send a coach debrief after every attempt.
//
// Shapes follow `ghost/contracts.ts` ("Rematch messages", "Log event",
// "Scenario file", "Trace event"). `game/server.js` owns the game state and
// passes it in, so this module never imports the server.
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');
const bots = require('./bots');

const REPO_ROOT = path.join(__dirname, '..');
const GOAL = 'Kill the ghost or survive 20 s';
const DRILL_TIMER = 20;
const SPIKE_EXPLODE_TIME = 40;
const BOT_JOIN_TIMEOUT_MS = 5000;
const GHOST_SUFFIX = '·ghost';
const MAX_NAME = 12;
const SKILL_MIN = 0.5;
const SKILL_MAX = 1.5;
// Items the base game's buy message can put in a player's hands.
const WEAPON_ITEMS = new Set(['classic', 'sheriff', 'spectre', 'phantom', 'vandal', 'operator', 'knife']);

const clampSkill = (value) => Math.round(Math.min(SKILL_MAX, Math.max(SKILL_MIN, value)) * 100) / 100;

function progressFile() {
  const dir = process.env.GHOST_DATA_DIR || path.join(REPO_ROOT, 'evidence', 'data');
  return path.join(dir, 'rematch-progress.json');
}

function loadProgress() {
  const file = progressFile();
  try {
    const raw = fs.readFileSync(file, 'utf8');
    const data = JSON.parse(raw);
    return data && typeof data === 'object' ? data : {};
  } catch (err) {
    if (err.code !== 'ENOENT') console.error(`rematch: could not read ${file}: ${err.message}`);
    return {};
  }
}

function saveProgress(data) {
  const file = progressFile();
  try {
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, `${JSON.stringify(data, null, 2)}\n`);
  } catch (err) {
    console.error(`rematch: could not write ${file}: ${err.message}`);
  }
}

function readLogFile(file) {
  let text;
  try {
    text = fs.readFileSync(file, 'utf8');
  } catch (err) {
    console.error(`rematch: could not read ${file}: ${err.message}`);
    return [];
  }
  const events = [];
  let malformed = 0;
  for (const line of text.split('\n')) {
    if (!line.trim()) continue;
    try {
      events.push(JSON.parse(line));
    } catch {
      malformed += 1;
    }
  }
  if (malformed > 0) console.error(`rematch: skipped ${malformed} malformed line(s) in ${file}`);
  return events;
}

function ghostBotName(killer) {
  const keep = Math.max(0, MAX_NAME - GHOST_SUFFIX.length);
  return `${String(killer).slice(0, keep)}${GHOST_SUFFIX}`;
}

function ghostReady(killer) {
  if (process.env.GHOST_ALLOW_DEFAULT === '1') return true;
  return fs.existsSync(path.join(bots.profilesDir(), `${killer}.json`));
}

function attachRematch({ io, players, round, applyScenario, endRound, recorder, nextRound, logDir, port }) {
  const progress = loadProgress();
  const skills = new Map(); // player name -> { scale, autoSkill }
  const sessionAttempts = new Map(); // "player|momentId" -> DrillAttemptInput[]
  let active = null; // the one drill that may run at a time

  function skillFor(playerName) {
    let skill = skills.get(playerName);
    if (!skill) {
      skill = { scale: 1, autoSkill: true };
      skills.set(playerName, skill);
    }
    return skill;
  }

  function progressFor(playerName, momentId) {
    if (!progress[playerName] || typeof progress[playerName] !== 'object') progress[playerName] = {};
    const item = progress[playerName][momentId];
    if (!item || typeof item !== 'object') return { attempts: 0, beaten: false };
    return { attempts: Number(item.attempts) || 0, beaten: item.beaten === true };
  }

  function setProgress(playerName, momentId, attempts, beaten) {
    if (!progress[playerName] || typeof progress[playerName] !== 'object') progress[playerName] = {};
    progress[playerName][momentId] = { attempts, beaten };
    saveProgress(progress);
  }

  // Every death of this player, newest first, except kills inside an earlier
  // rematch (a round that already contains a `scenario` line before the kill).
  function scanMoments(playerName) {
    let files;
    try {
      files = fs.readdirSync(logDir).filter((file) => file.endsWith('.jsonl'));
    } catch (err) {
      console.error(`rematch: could not list ${logDir}: ${err.message}`);
      return [];
    }

    const found = [];
    for (const name of files) {
      const file = path.join(logDir, name);
      let mtime = 0;
      try {
        mtime = fs.statSync(file).mtimeMs;
      } catch (err) {
        console.error(`rematch: could not stat ${file}: ${err.message}`);
      }
      const events = readLogFile(file);
      const firstScenario = new Map();
      events.forEach((event, index) => {
        if (!event || event.type !== 'scenario' || firstScenario.has(event.round)) return;
        firstScenario.set(event.round, index);
      });
      events.forEach((event, index) => {
        if (!event || event.type !== 'kill' || event.victim !== playerName) return;
        const scenarioIndex = firstScenario.get(event.round);
        if (scenarioIndex != null && index > scenarioIndex) return;
        const matchId = typeof event.matchId === 'string' && event.matchId
          ? event.matchId
          : name.replace(/\.jsonl$/, '');
        const id = `${matchId}:${event.round}:${event.t}`;
        const item = progressFor(playerName, id);
        found.push({
          moment: {
            id,
            matchId,
            round: event.round,
            t: event.t,
            killer: event.killer,
            killerGhostReady: ghostReady(event.killer),
            zone: null,
            weapon: typeof event.weapon === 'string' ? event.weapon : 'vandal',
            beaten: item.beaten,
            attempts: item.attempts,
          },
          mtime,
          index,
        });
      });
    }

    found.sort((a, b) => b.mtime - a.mtime || b.index - a.index);
    return found.map((entry) => entry.moment);
  }

  function lastMoveAt(events, playerName, t0) {
    let last = null;
    for (const event of events) {
      if (event.type === 'move' && event.player === playerName && event.t <= t0) last = event;
    }
    return last;
  }

  // What a player carried and wore at t0: last move, last gun buy, armor.
  function playerAt(events, playerName, t0) {
    const move = lastMoveAt(events, playerName, t0);
    if (!move || !move.pos) return null;
    let weapon = 'vandal';
    let armor = 0;
    for (const event of events) {
      if (event.type !== 'buy' || event.player !== playerName || event.t > t0) continue;
      if (WEAPON_ITEMS.has(event.item)) weapon = event.item;
      if (event.item === 'armor') armor = 50;
    }
    return {
      pos: { x: move.pos.x, y: Number.isFinite(move.pos.y) ? move.pos.y : 0, z: move.pos.z },
      yaw: Number.isFinite(move.yaw) ? move.yaw : 0,
      pitch: Number.isFinite(move.pitch) ? move.pitch : 0,
      hp: Number.isFinite(move.hp) ? move.hp : 100,
      team: move.team === 'attack' ? 'attack' : 'defense',
      weapon,
      armor,
    };
  }

  function spikeAt(events, t0) {
    let plantDone = null;
    let holderName = null;
    let dropped = null;
    for (const event of events) {
      if (event.t > t0) continue;
      if (event.type === 'plant' && event.stage === 'done') plantDone = event;
      if (event.type !== 'spike') continue;
      if (event.stage === 'pickup') {
        holderName = typeof event.player === 'string' && event.player ? event.player : null;
        dropped = null;
      } else if (event.stage === 'drop') {
        holderName = null;
        dropped = event.pos && Number.isFinite(event.pos.x) && Number.isFinite(event.pos.z)
          ? { x: event.pos.x, z: event.pos.z }
          : null;
      }
    }
    if (plantDone) holderName = null;
    return {
      planted: !!plantDone,
      site: plantDone && (plantDone.site === 'A' || plantDone.site === 'B') ? plantDone.site : null,
      pos: plantDone && plantDone.pos && Number.isFinite(plantDone.pos.x)
        ? { x: plantDone.pos.x, z: plantDone.pos.z }
        : null,
      explodeTimer: plantDone ? Math.max(0, SPIKE_EXPLODE_TIME - Math.max(0, t0 - plantDone.t)) : 0,
      holder: holderName,
      dropped: !plantDone && !holderName ? dropped : null,
    };
  }

  // A random 1.5 to 3 m step that stays inside the play area. The server has no
  // collider data for the base map yet, so the step can land inside geometry
  // (known limit; the bot's own navigation moves it out).
  function variedPos(pos) {
    for (let tries = 0; tries < 12; tries++) {
      const angle = Math.random() * Math.PI * 2;
      const distance = 1.5 + Math.random() * 1.5;
      const x = pos.x + Math.cos(angle) * distance;
      const z = pos.z + Math.sin(angle) * distance;
      if (Math.abs(x) <= 35 && Math.abs(z) <= 35) return { x, y: pos.y || 0, z };
    }
    return {
      x: Math.max(-35, Math.min(35, pos.x)),
      y: pos.y || 0,
      z: Math.max(-35, Math.min(35, pos.z)),
    };
  }

  function scenarioFromMoment(moment, victimName, botName, attempt, variation) {
    const events = readLogFile(path.join(logDir, `${moment.matchId}.jsonl`))
      .filter((event) => event && event.round === moment.round);
    const t0 = Math.max(0, moment.t - 3);
    const victim = playerAt(events, victimName, t0);
    const killer = playerAt(events, moment.killer, t0);
    if (!victim || !killer) return { error: 'not enough recorded play before this death' };

    const spike = spikeAt(events, t0);
    const holder = spike.holder === victimName
      ? victimName
      : spike.holder === moment.killer ? botName : null;
    spike.holder = holder;

    const ghostPos = variation ? variedPos(killer.pos) : killer.pos;
    const scenario = {
      id: `drill:${moment.id}:${attempt}`,
      source: { matchId: moment.matchId, round: moment.round, t: moment.t },
      phase: 'live',
      timer: DRILL_TIMER,
      spike,
      players: [
        {
          name: victimName,
          team: victim.team,
          pos: victim.pos,
          yaw: victim.yaw,
          pitch: victim.pitch,
          hp: victim.hp,
          armor: victim.armor,
          weapon: victim.weapon,
          hasSpike: holder === victimName,
          ultPoints: 0,
        },
        {
          name: botName,
          team: killer.team,
          pos: ghostPos,
          yaw: killer.yaw,
          pitch: killer.pitch,
          hp: killer.hp,
          armor: killer.armor,
          weapon: killer.weapon,
          hasSpike: holder === botName,
          ultPoints: 0,
        },
      ],
      practice: true,
    };
    return { scenario, playerTeam: victim.team };
  }

  function findPlayerByName(name) {
    for (const player of players.values()) {
      if (player.name === name) return player;
    }
    return null;
  }

  function socketOf(player) {
    return player ? io.sockets.sockets.get(player.id) || null : null;
  }

  function sendDrillState(drill) {
    const socket = io.sockets.sockets.get(drill.playerId);
    if (socket) socket.emit('drill-state', drill.state);
  }

  function sendBotConfig(drill) {
    const bot = findPlayerByName(drill.ghostBot);
    const socket = socketOf(bot);
    if (!socket) return;
    socket.emit('bot-config', {
      ghostOf: drill.ghostOf,
      skillScale: drill.state.skillScale,
      drillPlayer: drill.playerName,
    });
  }

  function waitForPlayer(name, ms) {
    return bots.waitForJoin(players, name, ms);
  }

  function killGhostBot(name) {
    if (name) bots.killBot(name);
  }

  function collectAttempt(drill, won) {
    const events = readLogFile(recorder.path);
    let start = -1;
    for (let i = events.length - 1; i >= 0; i--) {
      const event = events[i];
      if (event && event.type === 'scenario' && event.scenario && event.scenario.id === drill.scenarioId) {
        start = i;
        break;
      }
    }
    const slice = [];
    if (start >= 0) {
      for (let i = start; i < events.length; i++) {
        const event = events[i];
        slice.push(event);
        if (event.type === 'round-end') break;
      }
    }
    const scenarioLine = slice.find((event) => event.type === 'scenario');
    const endLine = slice.find((event) => event.type === 'round-end');
    const durationS = scenarioLine && endLine ? Math.max(0, endLine.t - scenarioLine.t) : 0;
    const traces = slice
      .filter((event) => event.type === 'trace' && event.trace)
      .map((event) => event.trace);
    return { won, durationS, events: slice, traces };
  }

  async function buildDebrief(drill, attempts) {
    try {
      const coach = await import(pathToFileURL(path.join(REPO_ROOT, 'ghost', 'coach', 'index.ts')).href);
      const result = await coach.drillDebrief({
        player: drill.playerName,
        ghostOf: drill.ghostOf,
        scenarioId: drill.scenarioId,
        attempts,
      });
      if (!result || typeof result.text !== 'string' || !result.text) {
        throw new Error('the coach returned no text');
      }
      const socket = io.sockets.sockets.get(drill.playerId);
      if (!socket) return;
      socket.emit('debrief', {
        momentId: drill.momentId,
        attempt: drill.attempt,
        text: result.text,
        source: result.source === 'llm' ? 'llm' : 'template',
      });
    } catch (err) {
      console.error(`rematch: debrief failed: ${err && err.message ? err.message : err}`);
    }
  }

  function onPracticeEnd(winnerTeam, reason) {
    const drill = active;
    if (!drill || !drill.running) return;
    drill.running = false;
    const won = winnerTeam === drill.playerTeam;
    drill.won = won;

    const item = progressFor(drill.playerName, drill.momentId);
    const attempts = Math.max(item.attempts, drill.attempt);
    setProgress(drill.playerName, drill.momentId, attempts, item.beaten || won);

    const skill = skillFor(drill.playerName);
    if (skill.autoSkill) {
      // A 1-up-1-down staircase: it converges near a 50% win rate.
      skill.scale = clampSkill(skill.scale + (won ? 0.1 : -0.1));
    }
    drill.state.beaten = item.beaten || won;
    drill.state.skillScale = skill.scale;
    drill.state.autoSkill = skill.autoSkill;
    drill.state.goal = GOAL;
    sendDrillState(drill);
    sendBotConfig(drill);

    const attempt = collectAttempt(drill, won);
    const key = `${drill.playerName}|${drill.momentId}`;
    const list = sessionAttempts.get(key) || [];
    list.push(attempt);
    sessionAttempts.set(key, list);
    void buildDebrief(drill, list);
  }

  function drillPlayerTeam() {
    return active && active.running ? active.playerTeam : null;
  }

  function endDrill() {
    if (!active) return false;
    const ghostBot = active.ghostBot;
    active = null;
    killGhostBot(ghostBot);
    round.practice = false;
    nextRound();
    return true;
  }

  async function startDrill({ socket, me, moment, variation }) {
    const botName = ghostBotName(moment.killer);
    const built = scenarioFromMoment(moment, me.name, botName, progressFor(me.name, moment.id).attempts + 1, variation);
    if (built.error) return { ok: false, error: built.error };

    let bot = findPlayerByName(botName);
    if (!bot) {
      const started = bots.spawnBot({
        name: botName,
        team: built.scenario.players[1].team,
        ghostOf: moment.killer,
        port,
      });
      if (started.error) return { ok: false, error: started.error };
      bot = await waitForPlayer(botName, BOT_JOIN_TIMEOUT_MS);
      if (!bot) return { ok: false, error: 'ghost bot did not join' };
    }

    const applied = applyScenario(built.scenario);
    if (!applied || !applied.ok) {
      return { ok: false, error: applied && applied.error ? applied.error : 'could not load the scenario' };
    }

    const item = progressFor(me.name, moment.id);
    const skill = skillFor(me.name);
    const attempt = item.attempts + 1;
    const state = {
      momentId: moment.id,
      ghostOf: moment.killer,
      ghostBot: botName,
      attempt,
      beaten: item.beaten,
      variation: !!variation,
      skillScale: skill.scale,
      autoSkill: skill.autoSkill,
      goal: GOAL,
    };
    active = {
      playerName: me.name,
      playerId: socket.id,
      playerTeam: built.playerTeam,
      momentId: moment.id,
      ghostOf: moment.killer,
      ghostBot: botName,
      attempt,
      scenarioId: built.scenario.id,
      variation: !!variation,
      running: true,
      state,
    };

    sendBotConfig(active);
    socket.emit('drill-state', state);
    return { ok: true, drill: state };
  }

  io.on('connection', (socket) => {
    const ackOf = (first, second) => (typeof first === 'function' ? first : typeof second === 'function' ? second : () => {});

    socket.on('rematch-list', (first) => {
      const reply = ackOf(first);
      const me = players.get(socket.id);
      if (!me) {
        reply({ ok: false, error: 'join first' });
        return;
      }
      reply({ ok: true, moments: scanMoments(me.name) });
    });

    socket.on('rematch-start', (payload, ack) => {
      const reply = ackOf(payload, ack);
      const me = players.get(socket.id);
      if (!me) {
        reply({ ok: false, error: 'join first' });
        return;
      }
      if (active && active.playerName !== me.name) {
        reply({ ok: false, error: 'another drill is running' });
        return;
      }
      const momentId = payload && typeof payload.momentId === 'string' ? payload.momentId : '';
      const variation = !!(payload && payload.variation);
      const moment = scanMoments(me.name).find((m) => m.id === momentId);
      if (!moment) {
        reply({ ok: false, error: 'unknown moment' });
        return;
      }
      if (!moment.killerGhostReady) {
        reply({ ok: false, error: `the ghost of ${moment.killer} is not ready` });
        return;
      }
      let replied = false;
      const finish = (result) => {
        if (replied) return;
        replied = true;
        reply(result);
      };
      startDrill({ socket, me, moment, variation }).then(finish, (err) => {
        console.error(`rematch: could not start drill: ${err && err.message ? err.message : err}`);
        finish({ ok: false, error: 'could not start the drill' });
      });
    });

    socket.on('skill-scale', (payload, ack) => {
      const reply = ackOf(payload, ack);
      const me = players.get(socket.id);
      if (!me) {
        reply({ ok: false, error: 'join first' });
        return;
      }
      const value = payload ? payload.value : undefined;
      const skill = skillFor(me.name);
      if (value === null) {
        skill.autoSkill = true;
      } else if (typeof value === 'number' && Number.isFinite(value)) {
        skill.scale = clampSkill(value);
        skill.autoSkill = false;
      } else {
        reply({ ok: false, error: 'value must be a number or null' });
        return;
      }
      if (active && active.playerName === me.name) {
        active.state.skillScale = skill.scale;
        active.state.autoSkill = skill.autoSkill;
        sendDrillState(active);
        sendBotConfig(active);
      }
      reply({ ok: true });
    });

    socket.on('rematch-leave', (first) => {
      const reply = ackOf(first);
      const me = players.get(socket.id);
      if (me && active && active.playerName === me.name) endDrill();
      reply({ ok: true });
    });

    socket.on('disconnect', () => {
      if (!active || active.playerId !== socket.id) return;
      // The drill player left: stop the ghost and go back to a normal round.
      endDrill();
    });
  });

  // A drill owns the practice round; the lobby refuses to add bots while it runs.
  const isDrillActive = () => !!(active && active.running);

  process.once('exit', bots.killAll);
  const onSignal = () => {
    bots.killAll();
    process.exit(0);
  };
  process.once('SIGINT', onSignal);
  process.once('SIGTERM', onSignal);

  return { onPracticeEnd, drillPlayerTeam, isDrillActive };
}

module.exports = { attachRematch };

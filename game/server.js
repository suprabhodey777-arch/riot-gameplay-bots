// Ghost Rematch game server (based on jangerine/valorant-like-fps, MIT)
// 팀, 라운드, 스파이크, 머니, 킬 처리 - Authoritative (판정 보조)
const express = require('express');
const http = require('http');
const { Server } = require('socket.io');
const path = require('path');
const { createRecorder, defaultLogDir, defaultMatchId } = require('./recorder');

const app = express();
const server = http.createServer(app);
const io = new Server(server, { cors: { origin: '*' } });

app.use(express.static(path.join(__dirname, 'public')));
// Ghost Rematch: tester answers and transfer-test groups (game/ghost-routes.js).
require('./ghost-routes').mountGhostRoutes(app);

// ---------- 상수 ----------
const ROUND_BUY_TIME = 20;
const ROUND_LIVE_TIME = 100;
const ROUND_END_TIME = 6;
const PLANT_TIME = 4.0;
const DEFUSE_TIME = 7.0;
const SPIKE_EXPLODE_TIME = 40;
const WIN_SCORE = 7;
const HALF_ROUND = 6; // 6라운드 후 공수교대
const START_MONEY = 800;

const SITE_A = { x1: -24, x2: -10, z1: 2, z2: 18 };
const SITE_B = { x1: 10, x2: 24, z1: 2, z2: 18 };

function inSite(pos, site) {
  return pos.x >= site.x1 && pos.x <= site.x2 && pos.z >= site.z1 && pos.z <= site.z2;
}
function onSite(pos) {
  if (inSite(pos, SITE_A)) return 'A';
  if (inSite(pos, SITE_B)) return 'B';
  return null;
}

// ---------- 상태 ----------
const players = new Map(); // socketId -> player
const lastMoveLogAt = new Map(); // socketId -> 마지막 move 로그 시각 (ms)
let rematch = null; // Ghost Rematch hooks, attached below once the server is wired
let round = {
  number: 1,
  phase: 'buy', // buy | live | end
  timer: ROUND_BUY_TIME,
  scoreAtk: 0,
  scoreDef: 0,
  spike: {
    holder: null,      // playerId
    dropped: null,     // {x,z}
    planted: false,
    site: null,
    pos: null,
    plantProgress: 0,
    plantingBy: null,
    defusingBy: null,
    defuseProgress: 0,
    explodeTimer: 0,
  },
  endReason: '',
  winner: null,
  practice: false,       // 연습 모드: 점수/머니 변화 없음
  roundStartedAt: Date.now(), // 로그 t 기준 시각
};

// 매치 로그: <GHOST_LOG_DIR 또는 ../logs>/<MATCH_ID 또는 타임스탬프>.jsonl
const recorder = createRecorder({
  logDir: process.env.GHOST_LOG_DIR || defaultLogDir(),
  matchId: process.env.MATCH_ID || defaultMatchId(),
  state: () => ({ round: round.number, t: roundTime() }),
});
// Raw match logs for the 3D replay page (public/replay.html).
app.use('/logs', express.static(path.dirname(recorder.path), { extensions: ['jsonl'] }));

function roundTime() {
  // Milliseconds since the round started, expressed in seconds (3 decimals).
  return Math.round(Date.now() - round.roundStartedAt) / 1000;
}

function log(playerName, type, fields) {
  recorder.log(playerName || '', type, fields);
}

function teamsByName() {
  const teams = {};
  for (const p of players.values()) teams[p.name] = p.team;
  return teams;
}

function teamCounts() {
  let atk = 0, def = 0;
  for (const p of players.values()) {
    if (p.team === 'attack') atk++;
    else if (p.team === 'defense') def++;
  }
  return { atk, def };
}

function pickSpawn(team) {
  // 공격: 남쪽, 수비: 북쪽 + 약간 랜덤
  const r = () => (Math.random() - 0.5) * 6;
  if (team === 'attack') return { x: 0 + r(), y: 0, z: -26 + r() * 0.5 };
  return { x: 0 + r(), y: 0, z: 26 + r() * 0.5 };
}

function resetRoundPositions() {
  for (const p of players.values()) {
    const s = pickSpawn(p.team);
    p.pos = { ...s };
    p.hp = 100;
    p.alive = true;
    p.yaw = p.team === 'attack' ? Math.PI : 0; // 공격은 북쪽(+z?) 바라보게
    // 실제로: 공격 스폰 z=-26에서 +z 방향(북)을 봐야 함 => yaw=0 이 +z 가정시
    // 클라이언트와 맞춤: yaw=0 이 -z 를 보는 three.js 관례라면 반전 필요.
    // 여기선 단순화: attack yaw=Math.PI, defense yaw=0 으로 고정, 클라에서 해석.
    p.pitch = 0;
  }
}

function giveSpike() {
  const atks = [...players.values()].filter(p => p.team === 'attack');
  if (atks.length === 0) { round.spike.holder = null; return; }
  // 살아있는 랜덤 공격에게
  const holder = atks[Math.floor(Math.random() * atks.length)];
  round.spike.holder = holder.id;
  round.spike.dropped = null;
  round.spike.planted = false;
  round.spike.site = null;
  round.spike.pos = null;
  round.spike.plantProgress = 0;
  round.spike.plantingBy = null;
  round.spike.defusingBy = null;
  round.spike.defuseProgress = 0;
  holder.hasSpike = true;
  for (const p of players.values()) if (p.id !== holder.id) p.hasSpike = false;
}

function uniqueName(base) {
  const taken = new Set([...players.values()].map(p => p.name));
  if (!taken.has(base)) return base;
  // 접속 중인 이름과 겹치면 -2, -3 ... (12자 자른 뒤 접미사)
  for (let n = 2; ; n++) {
    const candidate = `${base}-${n}`;
    if (!taken.has(candidate)) return candidate;
  }
}

function startRound() {
  round.phase = 'buy';
  round.timer = ROUND_BUY_TIME;
  round.endReason = '';
  round.winner = null;
  round.roundStartedAt = Date.now();
  round.spike = {
    holder: null, dropped: null, planted: false, site: null, pos: null,
    plantProgress: 0, plantingBy: null, defusingBy: null, defuseProgress: 0, explodeTimer: 0,
  };
  resetRoundPositions();
  giveSpike();
  log('', 'round-start', { teams: teamsByName(), practice: !!round.practice });
  io.emit('round-start', snapshot());
}

function endRound(winnerTeam, reason) {
  if (round.phase === 'end') return;
  round.phase = 'end';
  round.timer = ROUND_END_TIME;
  round.winner = winnerTeam;
  round.endReason = reason;
  const practice = !!round.practice;

  if (!practice) {
    if (winnerTeam === 'attack') round.scoreAtk++;
    else if (winnerTeam === 'defense') round.scoreDef++;
  }

  // 머니 지급 (연습 모드에서는 없음)
  for (const p of players.values()) {
    if (!practice) {
      if (p.team === winnerTeam) p.money += 3000;
      else p.money += 1900;
      // 연패 보너스 간소화: 패배팀 추가 +500
      p.money = Math.min(p.money, 9000);
    }
    p.hasSpike = false;
  }

  log('', 'round-end', {
    winner: winnerTeam,
    reason,
    scoreAtk: round.scoreAtk,
    scoreDef: round.scoreDef,
  });

  // 매치 종료 체크 (연습 모드에서는 매치가 끝나지 않음)
  let matchOver = false, matchWinner = null;
  if (!practice && (round.scoreAtk >= WIN_SCORE || round.scoreDef >= WIN_SCORE)) {
    matchOver = true;
    matchWinner = round.scoreAtk > round.scoreDef ? 'attack' : 'defense';
  }

  io.emit('round-end', { ...snapshot(), reason, winnerTeam, matchOver, matchWinner });
  if (practice) {
    // 점수/머니 없이 결과만 알리고, 타이머가 끝나도 다음 라운드로 넘어가지 않는다.
    io.emit('practice-end', { winner: winnerTeam, reason });
    // Ghost Rematch: update the drill record and start the coach debrief.
    if (rematch) rematch.onPracticeEnd(winnerTeam, reason);
    return;
  }
  if (matchOver) {
    setTimeout(() => {
      round.number = 1; round.scoreAtk = 0; round.scoreDef = 0;
      // 팀 섞기? 그대로 유지
      for (const p of players.values()) p.money = START_MONEY;
      startRound();
    }, 10000);
  }
}

function nextRound() {
  // 하프타임 공수교대 (점수는 사이드 기준 유지, 플레이어 팀만 스왑)
  if (round.number === HALF_ROUND) {
    for (const p of players.values()) {
      p.team = p.team === 'attack' ? 'defense' : 'attack';
    }
    io.emit('half-time', { message: 'Half time! Teams swap sides.' });
  }
  round.number++;
  startRound();
}

function snapshot() {
  return {
    round: {
      number: round.number,
      phase: round.phase,
      timer: Math.ceil(round.timer),
      scoreAtk: round.scoreAtk,
      scoreDef: round.scoreDef,
      spike: { ...round.spike },
      winner: round.winner,
      endReason: round.endReason,
      practice: !!round.practice,
    },
    players: [...players.values()].map(p => ({ ...p })),
    sites: { A: SITE_A, B: SITE_B },
  };
}

// ---------- 틱 루프 ----------
setInterval(() => {
  if (players.size === 0) return;
  round.timer -= 0.25;

  // Ghost Rematch: a drill ends when its player survives the whole window.
  if (rematch && round.phase === 'live' && round.timer <= 0) {
    const drillTeam = rematch.drillPlayerTeam();
    if (drillTeam) endRound(drillTeam, 'You survived');
  }

  // 설치 중/해체 중 진행 (plantingBy가 계속 plant-hold를 보내야 함)
  const sp = round.spike;

  if (round.phase === 'buy' && round.timer <= 0) {
    round.phase = 'live';
    round.timer = ROUND_LIVE_TIME;
    io.emit('phase', snapshot());
  } else if (round.phase === 'live') {
    // 스파이크 폭발
    if (sp.planted) {
      sp.explodeTimer -= 0.25;
      if (sp.explodeTimer <= 0) {
        log('', 'spike', { stage: 'exploded', pos: sp.pos ? { x: sp.pos.x, z: sp.pos.z } : null });
        io.emit('spike-exploded', { pos: sp.pos });
        endRound('attack', 'The Spike detonated');
      } else if (sp.explodeTimer <= 10 && Math.abs(sp.explodeTimer % 1) < 0.26) {
        io.emit('spike-beep', { timeLeft: sp.explodeTimer });
      }
    } else if (round.timer <= 0) {
      endRound('defense', 'Time ran out: defenders win');
    }
    // 전멸 체크
    const atkAlive = [...players.values()].some(p => p.team === 'attack' && p.alive);
    const defAlive = [...players.values()].some(p => p.team === 'defense' && p.alive);
    const hasAtk = [...players.values()].some(p => p.team === 'attack');
    const hasDef = [...players.values()].some(p => p.team === 'defense');
    if (hasAtk && hasDef) {
      if (!atkAlive && !sp.planted) endRound('defense', 'All attackers eliminated');
      else if (!defAlive && !sp.planted) endRound('attack', 'All defenders eliminated');
      else if (!defAlive && sp.planted) {
        // 설치 후 수비 전멸이어도 해체자가 없으면 공격 승리까지 대기 → 폭발까지 기다리지 않고 바로 끝내지 않음
        // 수비 전원 사망 시 공격 승리로 간주 (설치됨)
        endRound('attack', 'All defenders eliminated (Spike planted)');
      } else if (!atkAlive && sp.planted) {
        // 공격 전멸 + 설치됨 → 수비는 해체해야 함. 바로 끝내지 않고 해체/폭발까지 대기.
      }
    }
  } else if (round.phase === 'end' && round.timer <= 0) {
    // 연습 모드: 새 시나리오나 set-practice {on:false} 전까지 'end'에 머문다.
    if (!round.practice) nextRound();
  }

  io.emit('tick', snapshot());
}, 250);

// ---------- 소켓 ----------
// Apply a ScenarioFile (ghost/contracts.ts) to the live round. Used by the
// `load-scenario` socket message and by Ghost Rematch drills. Returns {ok} or
// {ok:false, error} and changes nothing when the holder is unknown.
function applyScenario(scenario) {
  if (!scenario || typeof scenario !== 'object') {
    return { ok: false, error: 'invalid scenario' };
  }

  const byName = new Map();
  for (const p of players.values()) byName.set(p.name, p);

  // 적용 전에 홀더 이름을 검증한다 (실패하면 아무것도 바꾸지 않음)
  const spikeIn = scenario.spike || {};
  let holder = null;
  if (spikeIn.holder != null) {
    holder = byName.get(spikeIn.holder) || null;
    if (!holder) {
      return { ok: false, error: `unknown player ${spikeIn.holder}` };
    }
  }

  if (scenario.phase === 'buy' || scenario.phase === 'live' || scenario.phase === 'end') {
    round.phase = scenario.phase;
  }
  if (Number.isFinite(scenario.timer)) round.timer = scenario.timer;

  const sp = round.spike;
  sp.holder = holder ? holder.id : null;
  sp.dropped = spikeIn.dropped && Number.isFinite(spikeIn.dropped.x) && Number.isFinite(spikeIn.dropped.z)
    ? { x: spikeIn.dropped.x, z: spikeIn.dropped.z }
    : null;
  sp.planted = !!spikeIn.planted;
  sp.site = sp.planted ? (spikeIn.site || null) : null;
  sp.pos = spikeIn.pos && Number.isFinite(spikeIn.pos.x) && Number.isFinite(spikeIn.pos.z)
    ? { x: spikeIn.pos.x, z: spikeIn.pos.z }
    : null;
  sp.plantProgress = 0;
  sp.plantingBy = null;
  sp.defusingBy = null;
  sp.defuseProgress = 0;
  sp.explodeTimer = sp.planted && Number.isFinite(spikeIn.explodeTimer) ? spikeIn.explodeTimer : 0;

  // 시나리오에 있는 플레이어만 이름으로 매칭해 되돌린다.
  const inScenario = new Set();
  for (const s of (scenario.players || [])) {
    if (!s || typeof s.name !== 'string') continue;
    const p = byName.get(s.name);
    if (!p) continue;
    inScenario.add(s.name);
    if (s.team === 'attack' || s.team === 'defense') p.team = s.team;
    if (s.pos && Number.isFinite(s.pos.x) && Number.isFinite(s.pos.z)) {
      p.pos = { x: s.pos.x, y: Number.isFinite(s.pos.y) ? s.pos.y : 0, z: s.pos.z };
    }
    p.yaw = Number.isFinite(s.yaw) ? s.yaw : p.yaw;
    p.pitch = Number.isFinite(s.pitch) ? s.pitch : p.pitch;
    p.hp = Number.isFinite(s.hp) ? s.hp : p.hp;
    p.armor = Number.isFinite(s.armor) ? s.armor : p.armor;
    if (typeof s.weapon === 'string') p.weapon = s.weapon;
    p.hasSpike = !!s.hasSpike;
    if (Number.isFinite(s.ultPoints)) p.ultPoints = s.ultPoints;
    p.alive = true;
  }
  for (const p of players.values()) {
    if (!inScenario.has(p.name)) p.alive = false;
  }

  round.practice = !!scenario.practice;
  round.roundStartedAt = Date.now();
  log('', 'scenario', { scenario });
  io.emit('scenario-loaded', { id: scenario.id });
  io.emit('round-start', snapshot());
  return { ok: true };
}

io.on('connection', (socket) => {
  console.log('join', socket.id);

  socket.on('join', (payload = {}) => {
    const { name, agent, team: forcedTeam, spawn: forcedSpawn, bot } = payload;
    const { atk, def } = teamCounts();
    const team = forcedTeam === 'attack' || forcedTeam === 'defense'
      ? forcedTeam
      : (atk <= def ? 'attack' : 'defense');
    const spawn = forcedSpawn && Number.isFinite(forcedSpawn.x) && Number.isFinite(forcedSpawn.z)
      ? { x: forcedSpawn.x, y: 0, z: forcedSpawn.z }
      : pickSpawn(team);
    const player = {
      id: socket.id,
      name: uniqueName((name || 'Agent').slice(0, 12)),
      agent: agent || 'jett',
      team,
      bot: !!bot,
      pos: { ...spawn },
      yaw: team === 'attack' ? Math.PI : 0,
      pitch: 0,
      hp: 100,
      armor: 0,
      alive: true,
      kills: 0,
      deaths: 0,
      money: START_MONEY,
      weapon: 'vandal',
      hasSpike: false,
      ultPoints: 0,
      killsThisRound: 0,
    };
    players.set(socket.id, player);
    // 첫 접속자에게 스파이크가 없을 때 공격이면 재분배
    if (!round.spike.holder && team === 'attack') giveSpike();
    socket.emit('joined', { me: player, snapshot: snapshot() });
    socket.broadcast.emit('player-joined', player);
    io.emit('players', [...players.values()]);
  });

  socket.on('move', (d) => {
    const p = players.get(socket.id);
    if (!p || !p.alive) return;
    // 기본 검증: 맵 범위, 순간이동 방지
    if (typeof d.x !== 'number' || typeof d.z !== 'number') return;
    if (Math.abs(d.x) > 40 || Math.abs(d.z) > 40) return;
    const dx = d.x - p.pos.x, dz = d.z - p.pos.z;
    if (dx * dx + dz * dz > 25) return; // 너무 큰 이동 무시 (텔포 스킬은 별도 이벤트)
    p.pos = { x: d.x, y: d.y || 0, z: d.z };
    p.yaw = d.yaw; p.pitch = d.pitch;
    p.moving = !!d.moving;
    p.crouch = !!d.crouch;
    // 스파이크 드랍줍기
    const sp = round.spike;
    if (!sp.planted && !sp.holder && sp.dropped && p.team === 'attack' && p.alive) {
      const ddx = p.pos.x - sp.dropped.x, ddz = p.pos.z - sp.dropped.z;
      if (ddx * ddx + ddz * ddz < 2.5) {
        sp.holder = p.id; sp.dropped = null; p.hasSpike = true;
        io.emit('spike-pickup', { by: p.name, holder: p.id });
        log(p.name, 'spike', { stage: 'pickup', pos: { x: p.pos.x, z: p.pos.z } });
      }
    }
    // 로그: 플레이어당 최대 50ms에 한 번 (그 외 이동은 적용만 하고 기록하지 않음)
    const now = Date.now();
    const lastLog = lastMoveLogAt.get(socket.id) || 0;
    if (now - lastLog >= 50) {
      lastMoveLogAt.set(socket.id, now);
      log(p.name, 'move', {
        pos: { x: p.pos.x, y: p.pos.y || 0, z: p.pos.z },
        yaw: p.yaw, pitch: p.pitch,
        moving: !!p.moving, crouch: !!p.crouch,
        team: p.team, hp: p.hp,
      });
    }
  });

  // 위치 강제 설정 (대시/텔포/업드래프트 승인)
  socket.on('teleport', (pos) => {
    const p = players.get(socket.id);
    if (!p || !p.alive) return;
    if (Math.abs(pos.x) > 35 || Math.abs(pos.z) > 35) return;
    p.pos = { x: pos.x, y: pos.y || 0, z: pos.z };
    log(p.name, 'teleport', { pos: { x: p.pos.x, y: p.pos.y, z: p.pos.z } });
  });

  socket.on('tracer', (d) => {
    socket.broadcast.emit('tracer', { id: socket.id, ...d });
    if (!d || !Array.isArray(d.from) || !Array.isArray(d.dir)) return;
    const p = players.get(socket.id);
    log(p ? p.name : '', 'tracer', { from: d.from, dir: d.dir });
  });

  socket.on('hit', ({ victimId, damage, headshot, weapon }) => {
    const atk = players.get(socket.id);
    const vic = players.get(victimId);
    if (!atk || !vic || !atk.alive || !vic.alive) return;
    if (atk.team === vic.team) return;
    if (round.phase !== 'live') return;
    // 거리 검증 (월샷 간소화 허용 60m)
    const dx = atk.pos.x - vic.pos.x, dz = atk.pos.z - vic.pos.z;
    if (dx * dx + dz * dz > 3600) return;
    let dmg = Math.max(1, Math.min(200, damage | 0));
    const shotWeapon = weapon || atk.weapon;
    log(atk.name, 'hit', { victim: vic.name, damage: dmg, headshot: !!headshot, weapon: shotWeapon });
    if (vic.armor > 0) { dmg = Math.round(dmg * 0.66); vic.armor = Math.max(0, vic.armor - 25); }
    vic.hp -= dmg;
    log(atk.name, 'damage', {
      victim: vic.name, attacker: atk.name, damage: dmg, headshot: !!headshot,
      hpLeft: Math.max(0, vic.hp),
    });
    socket.emit('hit-confirm', { victimId, headshot, damage: dmg, hpLeft: Math.max(0, vic.hp) });
    io.emit('damage', { victimId, attackerId: socket.id, damage: dmg, headshot });

    if (vic.hp <= 0) {
      vic.hp = 0; vic.alive = false; vic.deaths++;
      atk.kills++; atk.killsThisRound++;
      atk.money = Math.min(9000, atk.money + 300);
      atk.ultPoints = Math.min(8, (atk.ultPoints || 0) + 1);
      // 스파이크 드랍
      const sp = round.spike;
      if (sp.holder === vic.id) {
        sp.holder = null; vic.hasSpike = false;
        sp.dropped = { x: vic.pos.x, z: vic.pos.z };
        log(vic.name, 'spike', { stage: 'drop', pos: { x: vic.pos.x, z: vic.pos.z } });
      }
      log(atk.name, 'kill', {
        victim: vic.name, killer: atk.name, weapon: shotWeapon, headshot: !!headshot,
        killerPos: { x: atk.pos.x, y: atk.pos.y || 0, z: atk.pos.z },
        victimPos: { x: vic.pos.x, y: vic.pos.y || 0, z: vic.pos.z },
      });
      io.emit('kill', {
        killer: { id: atk.id, name: atk.name, team: atk.team, weapon, headshot },
        victim: { id: vic.id, name: vic.name, team: vic.team },
        killerKills: atk.kills,
      });
    }
  });

  // 스킬 브로드캐스트 (연막, 섬광, 정찰 등 위치 공유)
  socket.on('ability', (d) => {
    const p = players.get(socket.id);
    if (!p || !p.alive) return;
    if (round.phase !== 'live' && round.phase !== 'buy') return;
    // 궁 포인트 차감은 클라이언트에서 체크, 서버는 브로드캐스트
    if (d.spendUlt) { if ((p.ultPoints || 0) < 6) return; p.ultPoints = 0; }
    const abilityFields = { ability: String(d.type) };
    if (Array.isArray(d.at)) abilityFields.at = d.at;
    const aimedTarget = d.targetId ? players.get(d.targetId) : null;
    if (aimedTarget) abilityFields.target = aimedTarget.name;
    else if (typeof d.target === 'string') abilityFields.target = d.target;
    log(p.name, 'ability', abilityFields);
    socket.broadcast.emit('ability', { id: socket.id, team: p.team, agent: p.agent, ...d });
    // 자가 효과: 힐
    if (d.type === 'heal' && d.targetId) {
      const t = players.get(d.targetId);
      if (t && t.alive && t.team === p.team) {
        t.hp = Math.min(100, t.hp + 60);
        io.emit('healed', { targetId: t.id, hp: t.hp });
      }
    }
    // 쇼크다트/헌터스퓨리 데미지
    if ((d.type === 'shock' || d.type === 'fury') && d.targetId && d.damage) {
      const t = players.get(d.targetId);
      if (t && t.alive && t.team !== p.team) {
        t.hp -= d.damage;
        log(p.name, 'damage', {
          victim: t.name, attacker: p.name, damage: d.damage, headshot: false,
          hpLeft: Math.max(0, t.hp),
        });
        io.emit('damage', { victimId: t.id, attackerId: p.id, damage: d.damage, headshot: false });
        if (t.hp <= 0) {
          t.hp = 0; t.alive = false; t.deaths++; p.kills++;
          p.money = Math.min(9000, p.money + 300);
          log(p.name, 'kill', {
            victim: t.name, killer: p.name, weapon: String(d.type), headshot: false,
            killerPos: { x: p.pos.x, y: p.pos.y || 0, z: p.pos.z },
            victimPos: { x: t.pos.x, y: t.pos.y || 0, z: t.pos.z },
          });
          io.emit('kill', {
            killer: { id: p.id, name: p.name, team: p.team, weapon: d.type, headshot: false },
            victim: { id: t.id, name: t.name, team: t.team },
            killerKills: p.kills,
          });
        }
      }
    }
  });

  // 설치 / 해체: 누르고 있는 동안 클라이언트가 4Hz로 hold 전송
  socket.on('plant-hold', () => {
    const p = players.get(socket.id);
    const sp = round.spike;
    if (!p || !p.alive || round.phase !== 'live' || sp.planted) return;
    if (sp.holder !== p.id) return;
    if (p.team !== 'attack') return;
    const site = onSite(p.pos);
    if (!site) { sp.plantProgress = 0; sp.plantingBy = null; return; }
    // 처음 설치를 시작한 순간에만 start 기록
    if (!sp.plantingBy) {
      log(p.name, 'plant', { stage: 'start', site, pos: { x: p.pos.x, z: p.pos.z } });
    }
    sp.plantingBy = p.id;
    sp.plantProgress += 0.25; // tick 250ms마다 호출 가정
    io.emit('plant-progress', { progress: sp.plantProgress / PLANT_TIME, by: p.name });
    if (sp.plantProgress >= PLANT_TIME) {
      sp.planted = true; sp.site = site;
      sp.pos = { x: p.pos.x, z: p.pos.z };
      sp.explodeTimer = SPIKE_EXPLODE_TIME;
      sp.holder = null; p.hasSpike = false;
      sp.plantProgress = 0; sp.plantingBy = null;
      round.timer = Math.max(round.timer, SPIKE_EXPLODE_TIME); // 라운드 시간 연장
      log(p.name, 'plant', { stage: 'done', site, pos: { x: sp.pos.x, z: sp.pos.z } });
      io.emit('spike-planted', { site, pos: sp.pos, by: p.name });
    }
  });

  socket.on('plant-stop', () => {
    const sp = round.spike;
    if (sp.plantingBy === socket.id && !sp.planted) {
      const p = players.get(socket.id);
      sp.plantProgress = 0; sp.plantingBy = null;
      io.emit('plant-progress', { progress: 0, by: null });
      if (p) log(p.name, 'plant', { stage: 'stop', site: onSite(p.pos), pos: { x: p.pos.x, z: p.pos.z } });
    }
  });

  socket.on('defuse-hold', () => {
    const p = players.get(socket.id);
    const sp = round.spike;
    if (!p || !p.alive || !sp.planted || round.phase !== 'live') return;
    if (p.team !== 'defense') return;
    const dx = p.pos.x - sp.pos.x, dz = p.pos.z - sp.pos.z;
    if (dx * dx + dz * dz > 9) { sp.defuseProgress = 0; sp.defusingBy = null; return; }
    if (!sp.defusingBy) {
      log(p.name, 'defuse', { stage: 'start', pos: { x: p.pos.x, z: p.pos.z } });
    }
    sp.defusingBy = p.id;
    sp.defuseProgress += 0.25;
    io.emit('defuse-progress', { progress: sp.defuseProgress / DEFUSE_TIME, by: p.name });
    if (sp.defuseProgress >= DEFUSE_TIME) {
      sp.defusingBy = null;
      log(p.name, 'defuse', { stage: 'done', pos: { x: p.pos.x, z: p.pos.z } });
      endRound('defense', `${p.name} defused the Spike`);
    }
  });

  socket.on('defuse-stop', () => {
    const sp = round.spike;
    if (sp.defusingBy === socket.id) {
      // 발로란트처럼 해체 진도 절반 유지? 여기선 절반 유지
      sp.defuseProgress = sp.defuseProgress >= DEFUSE_TIME / 2 ? DEFUSE_TIME / 2 : 0;
      sp.defusingBy = null;
      io.emit('defuse-progress', { progress: sp.defuseProgress / DEFUSE_TIME, by: null });
      const p = players.get(socket.id);
      if (p) log(p.name, 'defuse', { stage: 'stop', pos: { x: p.pos.x, z: p.pos.z } });
    }
  });

  socket.on('buy', ({ item }) => {
    const p = players.get(socket.id);
    if (!p || round.phase !== 'buy') return;
    const PRICES = {
      spectre: 1600, phantom: 2900, vandal: 2900, operator: 4700,
      sheriff: 800, armor: 1000, ability: 300,
    };
    const cost = PRICES[item];
    if (cost == null || p.money < cost) return;
    // 총기류면 교체
    if (['spectre', 'phantom', 'vandal', 'operator', 'sheriff'].includes(item)) p.weapon = item;
    if (item === 'armor') p.armor = 50;
    p.money -= cost;
    log(p.name, 'buy', { item, money: p.money });
    socket.emit('bought', { weapon: p.weapon, money: p.money, armor: p.armor });
  });

  socket.on('chat', (msg) => {
    const p = players.get(socket.id);
    if (!p) return;
    io.emit('chat', { name: p.name, team: p.team, text: String(msg).slice(0, 80) });
  });

  // 시나리오 로드: 특정 순간으로 게임 상태를 되돌린다 (관리자 키가 있으면 검사)
  socket.on('load-scenario', (payload, ack) => {
    const reply = typeof ack === 'function' ? ack : () => {};
    const { key, scenario } = payload || {};
    if (process.env.ADMIN_KEY && key !== process.env.ADMIN_KEY) {
      reply({ ok: false, error: 'forbidden' });
      return;
    }
    reply(applyScenario(scenario));
  });

  // 연습 모드 종료: set-practice {on:false} -> 다음 라운드 시작
  socket.on('set-practice', (payload, ack) => {
    const reply = typeof ack === 'function' ? ack : () => {};
    const on = !!(payload && payload.on);
    round.practice = on;
    if (!on) nextRound();
    reply({ ok: true, practice: round.practice });
  });

  // 봇 결정 트레이스 중계 + 기록
  socket.on('trace', (trace) => {
    if (!trace || typeof trace !== 'object') return;
    const p = players.get(socket.id);
    io.emit('trace', trace);
    log(p ? p.name : '', 'trace', { trace });
  });

  socket.on('disconnect', () => {
    const p = players.get(socket.id);
    if (p) {
      // 스파이크 홀더가 나가면 드랍
      const sp = round.spike;
      if (sp.holder === socket.id) {
        sp.holder = null; sp.dropped = { x: p.pos.x, z: p.pos.z };
        log(p.name, 'spike', { stage: 'drop', pos: { x: p.pos.x, z: p.pos.z } });
      }
      players.delete(socket.id);
      lastMoveLogAt.delete(socket.id);
      io.emit('player-left', socket.id);
      io.emit('players', [...players.values()]);
    }
  });
});

const PORT = process.env.PORT || 3000;
// Ghost Rematch: moments, drills, skill and the coach (game/rematch.js).
rematch = require('./rematch').attachRematch({
  io,
  players,
  round,
  applyScenario,
  endRound,
  recorder,
  nextRound,
  logDir: path.dirname(recorder.path),
  port: PORT,
});
// Ghost lobby: list the recorded ghosts and spawn/remove their bots in a team
// (game/lobby.js). Attached after the rematch hooks so it can see an active drill.
require('./lobby').attachLobby({
  io,
  players,
  port: PORT,
  rematch,
});
// 라운드 1을 시작 상태로 만들고 로그에도 round-start 한 줄을 남긴다.
startRound();
server.listen(PORT, () => {
  console.log(`Ghost Rematch on http://localhost:${PORT}`);
  console.log(`match log: ${recorder.path}`);
});

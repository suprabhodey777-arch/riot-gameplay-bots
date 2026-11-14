/**
 * Synthetic match logs with two planted styles, for the cloning tests.
 *
 * `rusher` (attack) runs from attacker spawn into A site and fires the moment
 * the scripted `dummy` is visible. `anchor` (defense) walks to an A-site spot,
 * faces the entrance and holds, firing when the dummy appears. Six natural
 * rounds each, plus a practice segment that the replay must skip.
 *
 * All positions are checked with `blocked`/`clearPath`, and every tracer is
 * aimed at the dummy's chest with a planted angular error so the measured
 * profile has a known right answer.
 */
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import type { LogEvent, ScenarioFile, Team, Vec2, Vec3 } from '../contracts.ts';
import { CHEST_HEIGHT, EYE_HEIGHT, inViewCone, lineOfSight } from '../bot/view.ts';
import { blocked, clearPath, loadMapExport, loadZones } from '../bot/nav.ts';

export const MATCH_ID = 'fixture-match';
export const ROUNDS = 12;
export const RUSHER_ROUNDS = 6;
export const ANCHOR_ROUNDS = 6;
export const PLANTED_AIM_ERROR_DEG = 1.2;
/** Moment the scripted dummy becomes visible in a round of each style. */
const RUSHER_APPEAR_T = 24;
const ANCHOR_APPEAR_T = 34;
/** Move log cadence, at the server's 20/s ceiling. */
const TICK = 0.05;
const BASE_SPEED = 7;
const BASE_REACTION_S = 0.5;
/** Let the shooter keep moving after the kill so it never parks in the site. */
const END_AFTER_KILL_S = 3.5;
const DUMMY_FINAL: Vec2 = { x: -22, z: 10 };

/**
 * One distinct hiding zone per recorded round. Rounds must not be carbon copies
 * of each other, and a dummy parked in a different place each round is both
 * realistic and testable. Every one of these waypoints is out of the shooter's
 * sight before the dummy appears (checked below).
 */
const HIDE_ZONE_IDS = [
  'a-link',
  'a-lane',
  'b-lane',
  'b-long',
  'b-main',
  'defense-spawn',
  'a-long',
  'a-main',
  'b-link',
  'mid-doors',
  'mid-cross',
  'mid-north',
];

function hidePoint(round: number): Vec2 {
  const id = HIDE_ZONE_IDS[(round - 1) % HIDE_ZONE_IDS.length];
  const zone = loadZones().zones.find((z) => z.id === id);
  if (!zone) throw new Error(`fixture hide zone missing from zones.json: ${id}`);
  return { ...zone.waypoint };
}

const RUSHER_PATH: Vec2[] = [
  { x: 0, z: -26 },
  { x: -28, z: -25 },
  { x: -26.5, z: -14 },
  { x: -25, z: -8 },
  { x: -25.3, z: 6 },
  { x: -22, z: 10 },
  { x: -17, z: 14 },
  { x: -21, z: 17.5 },
  { x: -20, z: 20 },
];

const ANCHOR_PATH: Vec2[] = [
  { x: 0, z: 26 },
  { x: -20, z: 20 },
  { x: -21, z: 17.5 },
  { x: -19.5, z: 17 },
  { x: -17, z: 14 },
];

interface Route {
  points: Vec2[];
  lengths: number[];
  total: number;
}

function route(points: Vec2[]): Route {
  const lengths: number[] = [];
  let total = 0;
  for (let i = 1; i < points.length; i++) {
    const d = Math.hypot(points[i].x - points[i - 1].x, points[i].z - points[i - 1].z);
    lengths.push(d);
    total += d;
  }
  return { points, lengths, total };
}

function positionOn(r: Route, seconds: number, speed: number): { pos: Vec2; moving: boolean } {
  const walked = Math.max(0, seconds) * speed;
  if (walked >= r.total) return { pos: { ...r.points[r.points.length - 1] }, moving: false };
  let left = walked;
  for (let i = 0; i < r.lengths.length; i++) {
    if (left <= r.lengths[i]) {
      const k = r.lengths[i] < 1e-9 ? 0 : left / r.lengths[i];
      return {
        pos: {
          x: r.points[i].x + (r.points[i + 1].x - r.points[i].x) * k,
          z: r.points[i].z + (r.points[i + 1].z - r.points[i].z) * k,
        },
        moving: true,
      };
    }
    left -= r.lengths[i];
  }
  return { pos: { ...r.points[r.points.length - 1] }, moving: false };
}

function aimAt(from: Vec2, target: Vec2): { yaw: number; pitch: number } {
  const dx = target.x - from.x;
  const dz = target.z - from.z;
  const horizontal = Math.hypot(dx, dz);
  const eyeY = 0 + EYE_HEIGHT;
  const chestY = 0 + CHEST_HEIGHT;
  return {
    yaw: Math.atan2(-dx, -dz),
    pitch: Math.atan2(chestY - eyeY, horizontal),
  };
}

function normalize(v: [number, number, number]): [number, number, number] {
  const len = Math.hypot(v[0], v[1], v[2]) || 1;
  return [v[0] / len, v[1] / len, v[2] / len];
}

/** Rotate a unit vector by `deg` around a perpendicular axis. */
function perturbDirection(dir: [number, number, number], deg: number): [number, number, number] {
  const trueDir = normalize(dir);
  const reference: [number, number, number] = Math.abs(trueDir[1]) < 0.9 ? [0, 1, 0] : [1, 0, 0];
  const u = normalize([
    reference[1] * trueDir[2] - reference[2] * trueDir[1],
    reference[2] * trueDir[0] - reference[0] * trueDir[2],
    reference[0] * trueDir[1] - reference[1] * trueDir[0],
  ]);
  const a = (deg * Math.PI) / 180;
  return normalize([
    trueDir[0] * Math.cos(a) + u[0] * Math.sin(a),
    trueDir[1] * Math.cos(a) + u[1] * Math.sin(a),
    trueDir[2] * Math.cos(a) + u[2] * Math.sin(a),
  ]);
}

function sight(from: Vec2, yaw: number, to: Vec2): boolean {
  const eye: Vec3 = { x: from.x, y: EYE_HEIGHT, z: from.z };
  const chest: Vec3 = { x: to.x, y: CHEST_HEIGHT, z: to.z };
  return inViewCone(eye, yaw, chest) && lineOfSight(eye, chest, loadMapExport().colliders, []);
}

function assertWalkable(): void {
  const hides = Array.from({ length: HIDE_ZONE_IDS.length }, (_, i) => hidePoint(i + 1));
  const all = [...RUSHER_PATH, ...ANCHOR_PATH, ...hides, DUMMY_FINAL];
  for (const p of all) {
    if (blocked(p)) throw new Error(`fixture position is blocked: ${JSON.stringify(p)}`);
  }
  for (const path of [RUSHER_PATH, ANCHOR_PATH]) {
    for (let i = 1; i < path.length; i++) {
      if (!clearPath(path[i - 1], path[i])) {
        throw new Error(`fixture path leg is not clear: ${JSON.stringify(path[i - 1])} -> ${JSON.stringify(path[i])}`);
      }
    }
  }
}

interface RoundSpec {
  round: number;
  shooter: 'rusher' | 'anchor';
  shooterTeam: Team;
  /** Human round-to-round variation: pace, reaction, and the dummy's wait. */
  startT: number;
  speed: number;
  reactionS: number;
  appearT: number;
  hide: Vec2;
  path: Route;
}

function buildRound(spec: RoundSpec): LogEvent[] {
  const { round, shooter, shooterTeam, startT: shooterStart, speed, reactionS, appearT, hide, path } = spec;
  const dummy = 'dummy';
  const enemyTeam: Team = shooterTeam === 'attack' ? 'defense' : 'attack';
  const events: LogEvent[] = [];
  const base = { v: 1 as const, matchId: MATCH_ID, round };

  const shooterAt = (t: number) => positionOn(path, Math.max(0, t - shooterStart), speed);
  const dummyAt = (t: number): Vec2 => (t < appearT ? hide : DUMMY_FINAL);

  // The dummy must stay out of sight until it appears, or the round is not the
  // planted style anymore.
  for (let t = 20; t < appearT - 1e-9; t += TICK) {
    const from = shooterAt(t).pos;
    if (from.x === hide.x && from.z === hide.z) continue;
    if (sight(from, aimAt(from, DUMMY_FINAL).yaw, hide)) {
      throw new Error(`fixture round ${round}: dummy's hide spot is visible to ${shooter} before it appears`);
    }
  }

  // Find the first moment the dummy is visible; the shot follows the reaction.
  let shotT: number | null = null;
  for (let t = appearT; t < appearT + 12 && shotT === null; t += TICK) {
    const from = shooterAt(t).pos;
    const aim = aimAt(from, dummyAt(t));
    if (sight(from, aim.yaw, dummyAt(t))) shotT = Math.round((t + reactionS) * 20) / 20;
  }
  if (shotT === null) throw new Error(`fixture round ${round}: ${shooter} never sees the dummy`);
  const endT = Math.round((shotT + END_AFTER_KILL_S) * 1000) / 1000;
  const headshot = shooter === 'rusher' ? round % 3 === 0 : round % 2 === 0;

  events.push({
    ...base,
    t: 0,
    player: '',
    type: 'round-start',
    teams: { [shooter]: shooterTeam, dummy: enemyTeam },
    practice: false,
  });
  events.push({ ...base, t: 5, player: shooter, type: 'buy', item: 'vandal', money: 3900 });
  events.push({ ...base, t: 20.05, player: shooter, type: 'ability', ability: 'smoke', at: [0, 0] });

  for (let t = 0.1; t <= endT + 1e-9; t = Math.round((t + TICK) * 1000) / 1000) {
    const shooterPos = shooterAt(t);
    const shooterAim = aimAt(shooterPos.pos, DUMMY_FINAL);
    events.push({
      ...base,
      t,
      player: shooter,
      type: 'move',
      pos: { x: shooterPos.pos.x, y: 0, z: shooterPos.pos.z },
      yaw: shooterAim.yaw,
      pitch: shooterAim.pitch,
      moving: t >= shooterStart && shooterPos.moving,
      crouch: false,
      team: shooterTeam,
      hp: 100,
    });

    if (Math.abs(t - appearT) < 1e-9) {
      events.push({
        ...base,
        t,
        player: dummy,
        type: 'teleport',
        pos: { x: DUMMY_FINAL.x, y: 0, z: DUMMY_FINAL.z },
      });
    }
    const dummyPos = dummyAt(t);
    events.push({
      ...base,
      t,
      player: dummy,
      type: 'move',
      pos: { x: dummyPos.x, y: 0, z: dummyPos.z },
      yaw: 0,
      pitch: 0,
      moving: false,
      crouch: false,
      team: enemyTeam,
      hp: 100,
    });

    if (Math.abs(t - shotT) < 1e-9) {
      const from = shooterAt(t).pos;
      const eye: [number, number, number] = [from.x, EYE_HEIGHT, from.z];
      const toward: [number, number, number] = [
        dummyPos.x - eye[0],
        CHEST_HEIGHT - eye[1],
        dummyPos.z - eye[2],
      ];
      events.push({
        ...base,
        t,
        player: shooter,
        type: 'tracer',
        from: eye,
        dir: perturbDirection(toward, PLANTED_AIM_ERROR_DEG),
      });
      events.push({ ...base, t, player: shooter, type: 'hit', victim: dummy, damage: 100, headshot, weapon: 'vandal' });
      events.push({
        ...base,
        t,
        player: shooter,
        type: 'damage',
        victim: dummy,
        attacker: shooter,
        damage: 100,
        headshot,
        hpLeft: 0,
      });
      events.push({
        ...base,
        t,
        player: shooter,
        type: 'kill',
        victim: dummy,
        killer: shooter,
        weapon: 'vandal',
        headshot,
        killerPos: { x: from.x, y: 0, z: from.z },
        victimPos: { x: dummyPos.x, y: 0, z: dummyPos.z },
      });
    }
  }

  events.push({
    ...base,
    t: endT,
    player: '',
    type: 'round-end',
    winner: shooterTeam,
    reason: 'elimination',
    scoreAtk: 0,
    scoreDef: 0,
  });

  events.sort((a, b) => a.t - b.t);
  return events;
}

function practiceSegment(): LogEvent[] {
  const scenario: ScenarioFile = {
    id: 'fixture-practice',
    source: null,
    phase: 'buy',
    timer: 20,
    spike: { planted: false, site: null, pos: null, explodeTimer: 0, holder: null, dropped: null },
    players: [],
    practice: true,
  };
  const base = { v: 1 as const, matchId: MATCH_ID, round: 90 };
  return [
    { ...base, t: 0, player: '', type: 'scenario', scenario },
    // Everything between the scenario line and the next round-start is a drill.
    { ...base, t: 0.5, player: 'rusher', type: 'tracer', from: [0, 1.62, -26], dir: [0, 0, 1] },
    { ...base, t: 1, player: '', type: 'move', pos: { x: -22, y: 0, z: 0 }, yaw: 0, pitch: 0, moving: true, crouch: false, team: 'attack', hp: 100 },
    {
      ...base,
      t: 2,
      player: '',
      type: 'round-start',
      teams: { rusher: 'attack', dummy: 'defense' },
      practice: true,
    },
    {
      ...base,
      t: 21,
      player: 'rusher',
      type: 'move',
      pos: { x: -22, y: 0, z: 10 },
      yaw: 0,
      pitch: 0,
      moving: true,
      crouch: false,
      team: 'attack',
      hp: 100,
    },
    { ...base, t: 22, player: '', type: 'round-end', winner: null, reason: 'practice', scoreAtk: 0, scoreDef: 0 },
  ];
}

export interface Fixture {
  dir: string;
  logsDir: string;
  outDir: string;
  matchId: string;
  rounds: number;
  skippedLines: number;
  plantedAimErrorDeg: number;
  cleanup(): void;
}

/** Write the fixture logs into `<dir>/logs` and return the paths. */
export function writeFixtureLogs(logsDir: string): { matchId: string; rounds: number; skippedLines: number } {
  assertWalkable();
  const events: LogEvent[] = [];
  for (let round = 1; round <= ROUNDS; round++) {
    if (round === 4) events.push(...practiceSegment());
    const shooter = round <= RUSHER_ROUNDS ? 'rusher' : 'anchor';
    // Rounds are not carbon copies: each has its own pace, reaction and wait.
    const index = shooter === 'rusher' ? round - 1 : round - RUSHER_ROUNDS - 1;
    const rusher = shooter === 'rusher';
    events.push(
      ...buildRound({
        round,
        shooter,
        shooterTeam: rusher ? 'attack' : 'defense',
        startT: rusher ? 16 + 0.2 * index : 20 + index,
        speed: rusher ? 5.8 + 0.3 * index : 6.3 + 0.1 * index,
        reactionS: BASE_REACTION_S + 0.02 * index,
        appearT: rusher ? 22 + 0.5 * index : ANCHOR_APPEAR_T + index,
        hide: hidePoint(round),
        path: route(rusher ? RUSHER_PATH : ANCHOR_PATH),
      }),
    );
  }
  mkdirSync(logsDir, { recursive: true });
  const text = events.map((e) => JSON.stringify(e)).join('\n');
  // One unparseable line exercises the reader's skip counter.
  writeFileSync(join(logsDir, 'fixture.jsonl'), `${text}\n{"unparseable":\n`);
  return { matchId: MATCH_ID, rounds: ROUNDS, skippedLines: 1 };
}

/** Temp-directory fixture used by the tests. */
export function makeFixture(): Fixture {
  const dir = mkdtempSync(join(tmpdir(), 'ghost-clone-'));
  const logsDir = join(dir, 'logs');
  const outDir = join(dir, 'profiles');
  const info = writeFixtureLogs(logsDir);
  return {
    dir,
    logsDir,
    outDir,
    matchId: info.matchId,
    rounds: info.rounds,
    skippedLines: info.skippedLines,
    plantedAimErrorDeg: PLANTED_AIM_ERROR_DEG,
    cleanup() {
      rmSync(dir, { recursive: true, force: true });
    },
  };
}


/**
 * Replay recorded match logs into exactly the data the cloning pipeline trains on.
 *
 * A log is one `LogEvent` per line (`ghost/contracts.ts`). Training has to see
 * what live play sees, so every frame is built through `createPerception` and
 * `buildSituation`, with the same map, smokes and callout zones the bot uses.
 */
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

import type { Box, LogEvent, Situation, Team, Vec3 } from '../contracts.ts';
import { createPerception } from '../bot/view.ts';
import type { EnemyState, Perception, Smoke } from '../bot/view.ts';
import { loadMapExport } from '../bot/nav.ts';
import { RECENT_S, buildSituation, wrapAngle } from './situation.ts';
import type { WorldPlayer, WorldView } from './situation.ts';

/** A smoke lives this long after its `ability` event (seconds). */
export const SMOKE_DURATION_S = 18;
/** Smoke radius, the same cylinder `lineOfSight` tests. */
export const SMOKE_RADIUS = 2.6;
/** The buy phase is the first 20 s of the server clock. */
export const BUY_TIME_S = 20;
/** Live phase length: `ROUND_LIVE_TIME` in `game/server.js`. */
export const LIVE_TIME_S = 100;
/** Knife-round / buy defaults; logs carry no armor or ammo. */
const DEFAULT_ARMOR = 0;
const DEFAULT_WEAPON = 'classic';

export interface LogFile {
  file: string;
  events: LogEvent[];
  /** Non-empty lines that were not a JSON object with a string `type`. */
  skipped: number;
}

/** Read every `<dir>/*.jsonl`, one event array per file. */
export function readLogFiles(dir: string): LogFile[] {
  const files = readdirSync(dir)
    .filter((name) => name.toLowerCase().endsWith('.jsonl'))
    .sort();
  const out: LogFile[] = [];
  for (const name of files) {
    const text = readFileSync(join(dir, name), 'utf8');
    const events: LogEvent[] = [];
    let skipped = 0;
    for (const raw of text.split(/\r?\n/)) {
      const line = raw.trim();
      if (!line) continue;
      try {
        const parsed = JSON.parse(line) as unknown;
        if (!parsed || typeof parsed !== 'object' || typeof (parsed as { type?: unknown }).type !== 'string') {
          skipped++;
          continue;
        }
        events.push(parsed as LogEvent);
      } catch {
        skipped++;
      }
    }
    out.push({ file: name, events, skipped });
  }
  return out;
}

/** Convenience wrapper: `readLogFiles` without the per-file stats. Unparseable lines are skipped; `readLogFiles` counts them. */
export function readLogs(dir: string): LogEvent[][] {
  return readLogFiles(dir).map((file) => file.events);
}

export interface ReplaySegment {
  matchId: string;
  round: number;
  events: LogEvent[];
  teams: Record<string, Team>;
}

/** One player's position/yaw sample from a `move` (or a `teleport`). */
interface TrackPoint {
  t: number;
  pos: Vec3;
  yaw: number;
  pitch: number;
  moving: boolean;
  crouch: boolean;
  teleport: boolean;
}

interface SmokeField {
  x: number;
  z: number;
  start: number;
  end: number;
}

function eventKey(e: { matchId: string; round: number }): string {
  return `${e.matchId}|${e.round}`;
}

function compareRounds(a: { matchId: string; round: number }, b: { matchId: string; round: number }): number {
  if (a.matchId !== b.matchId) return a.matchId < b.matchId ? -1 : 1;
  return a.round - b.round;
}

/**
 * Natural-play rounds only.
 *
 * A `scenario` line starts a practice segment: everything until the next
 * `round-start` is dropped. Rounds whose `round-start` says `practice: true`
 * are dropped as well, because drills are not natural play.
 */
export function segments(events: LogEvent[]): ReplaySegment[] {
  const out: ReplaySegment[] = [];
  let current: ReplaySegment | null = null;
  let inScenario = false;

  for (const e of events) {
    if (e.type === 'scenario') {
      inScenario = true;
      current = null;
      continue;
    }
    if (e.type === 'round-start') {
      inScenario = false;
      current = null;
      if (e.practice) continue;
      current = { matchId: e.matchId, round: e.round, events: [e], teams: { ...e.teams } };
      out.push(current);
      continue;
    }
    if (inScenario || !current) continue;
    if (eventKey(e) !== eventKey(current)) continue;
    current.events.push(e);
    if (e.type === 'round-end') current = null;
  }

  return out;
}

function isWeaponName(item: string): boolean {
  return ['classic', 'sheriff', 'spectre', 'phantom', 'vandal', 'operator', 'knife'].includes(item);
}

function buildSmokes(events: LogEvent[]): SmokeField[] {
  const out: SmokeField[] = [];
  for (const e of events) {
    if (e.type !== 'ability' || e.ability !== 'smoke' || !e.at) continue;
    out.push({ x: e.at[0], z: e.at[1], start: e.t, end: e.t + SMOKE_DURATION_S });
  }
  return out;
}

function activeSmokes(fields: SmokeField[], t: number): Smoke[] {
  return fields.filter((f) => t >= f.start && t < f.end).map((f) => ({ x: f.x, z: f.z, r: SMOKE_RADIUS }));
}

function buildTracks(events: LogEvent[]): Map<string, TrackPoint[]> {
  const tracks = new Map<string, TrackPoint[]>();
  for (const e of events) {
    if (!e.player || (e.type !== 'move' && e.type !== 'teleport')) continue;
    const list = tracks.get(e.player) ?? [];
    const prev = list[list.length - 1];
    if (e.type === 'move') {
      list.push({
        t: e.t,
        pos: { ...e.pos },
        yaw: e.yaw,
        pitch: e.pitch,
        moving: e.moving,
        crouch: e.crouch,
        teleport: false,
      });
    } else {
      list.push({
        t: e.t,
        pos: { ...e.pos },
        yaw: prev?.yaw ?? 0,
        pitch: prev?.pitch ?? 0,
        moving: prev?.moving ?? false,
        crouch: prev?.crouch ?? false,
        teleport: true,
      });
    }
    tracks.set(e.player, list);
  }
  for (const list of tracks.values()) list.sort((a, b) => a.t - b.t);
  return tracks;
}

/** Index of the last element with `t <= time`, or -1. */
function lastAtOrBefore<T extends { t: number }>(list: T[], time: number): number {
  let lo = 0;
  let hi = list.length - 1;
  let found = -1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (list[mid].t <= time + 1e-9) {
      found = mid;
      lo = mid + 1;
    } else {
      hi = mid - 1;
    }
  }
  return found;
}

/**
 * Position at `t`, linearly interpolated between the surrounding `move`s.
 * A `teleport` is never interpolated through: the old position holds until the
 * teleport instant, then the new one starts a fresh segment.
 */
function sampleTrack(points: TrackPoint[], t: number): TrackPoint | null {
  if (points.length === 0) return null;
  const i = lastAtOrBefore(points, t);
  if (i < 0) return null;
  const cur = points[i];
  const next = points[i + 1];
  if (!next || next.teleport) return cur;
  const span = next.t - cur.t;
  if (span <= 1e-9) return next;
  const k = Math.max(0, Math.min(1, (t - cur.t) / span));
  const yaw = wrapAngle(cur.yaw + wrapAngle(next.yaw - cur.yaw) * k);
  return {
    t,
    pos: {
      x: cur.pos.x + (next.pos.x - cur.pos.x) * k,
      y: cur.pos.y + (next.pos.y - cur.pos.y) * k,
      z: cur.pos.z + (next.pos.z - cur.pos.z) * k,
    },
    yaw,
    pitch: cur.pitch + (next.pitch - cur.pitch) * k,
    moving: cur.moving,
    crouch: cur.crouch,
    teleport: false,
  };
}

interface HpSample {
  t: number;
  hp: number;
}

function buildHpTracks(events: LogEvent[]): Map<string, HpSample[]> {
  const tracks = new Map<string, HpSample[]>();
  const add = (name: string, t: number, hp: number) => {
    if (!name) return;
    const list = tracks.get(name) ?? [];
    list.push({ t, hp: Math.max(0, Math.min(100, hp)) });
    tracks.set(name, list);
  };
  for (const e of events) {
    if (e.type === 'move') add(e.player, e.t, e.hp);
    else if (e.type === 'damage') add(e.victim, e.t, e.hpLeft);
    else if (e.type === 'kill') add(e.victim, e.t, 0);
  }
  for (const list of tracks.values()) list.sort((a, b) => a.t - b.t);
  return tracks;
}

function hpAt(track: HpSample[] | undefined, t: number): number {
  if (!track || track.length === 0) return 100;
  const i = lastAtOrBefore(track, t);
  if (i < 0) return track[0].hp;
  return track[i].hp;
}

function buildDeaths(events: LogEvent[]): Map<string, number> {
  const deaths = new Map<string, number>();
  for (const e of events) {
    if (e.type === 'kill' && !deaths.has(e.victim)) deaths.set(e.victim, e.t);
  }
  return deaths;
}

function buildWeapons(events: LogEvent[]): Map<string, string> {
  const weapon = new Map<string, string>();
  for (const e of events) {
    if (e.type === 'buy' && e.player && isWeaponName(e.item)) weapon.set(e.player, e.item);
  }
  return weapon;
}

interface SpikeState {
  holder: string | null;
  planted: boolean;
  dropped: { x: number; z: number } | null;
}

function spikeAt(events: LogEvent[], t: number): SpikeState {
  const state: SpikeState = { holder: null, planted: false, dropped: null };
  for (const e of events) {
    if (e.t > t + 1e-9) break;
    if (e.type === 'spike') {
      if (e.stage === 'pickup') {
        state.holder = e.player;
        state.dropped = null;
      } else if (e.stage === 'drop') {
        if (state.holder === e.player) state.holder = null;
        state.dropped = e.pos;
      }
    } else if (e.type === 'plant' && e.stage === 'done') {
      state.planted = true;
      state.holder = null;
    }
  }
  return state;
}

function abilitiesAt(events: LogEvent[], t: number): Record<string, { Q: boolean; E: boolean; X: boolean }> {
  const used: Record<string, { Q: boolean; E: boolean; X: boolean }> = {};
  for (const e of events) {
    if (e.t > t + 1e-9) break;
    if (e.type !== 'ability' || !e.player) continue;
    const ready = (used[e.player] ??= { Q: true, E: true, X: false });
    const slot = e.ability === 'smoke' ? 'E' : 'Q';
    ready[slot] = false;
  }
  return used;
}

export interface Frame {
  matchId: string;
  round: number;
  /** Seconds since the round started (buy phase included), same as `LogEvent.t`. */
  t: number;
  player: string;
  view: WorldView;
  situation: Situation;
  /** Enemies this player actually saw on this frame (reaction 0). */
  visible: string[];
}

function playerNames(segment: ReplaySegment): string[] {
  return Object.keys(segment.teams).sort();
}

/**
 * Sample every live frame of a natural-play round.
 *
 * Positions come from interpolated `move`s (with `teleport` overrides), hp from
 * damage (plus move resync), a player stays alive until their `kill`, the spike
 * from `pickup`/`drop`/`plant`, and `timeLeft = 100 - (t - 20)` while live.
 * Buy-phase frames (t < 20) are skipped.
 */
export function frames(segment: ReplaySegment, hz = 4): Frame[] {
  const { events, teams } = segment;
  const names = playerNames(segment);
  if (names.length === 0 || events.length === 0) return [];

  const tracks = buildTracks(events);
  const hpTracks = buildHpTracks(events);
  const deaths = buildDeaths(events);
  const weapons = buildWeapons(events);
  const smokeFields = buildSmokes(events);
  const boxes: Box[] = loadMapExport().colliders;
  const perceptions = new Map<string, Perception>();
  for (const name of names) perceptions.set(name, createPerception({ reactionMs: 0 }));

  let lastT = BUY_TIME_S;
  for (const e of events) if (e.t > lastT) lastT = e.t;
  const out: Frame[] = [];
  const step = 1 / hz;
  const steps = Math.floor((lastT - BUY_TIME_S) * hz + 1e-9);

  for (let i = 0; i <= steps; i++) {
    const t = BUY_TIME_S + i * step;
    const nowMs = t * 1000;
    const spike = spikeAt(events, t);
    const abilities = abilitiesAt(events, t);
    const recent = events.filter((e) => e.t >= t - RECENT_S - 1e-9 && e.t <= t + 1e-9);

    const world: WorldPlayer[] = [];
    const sampled = new Map<string, TrackPoint>();
    for (const name of names) {
      const sample = sampleTrack(tracks.get(name) ?? [], t);
      if (!sample) continue;
      sampled.set(name, sample);
      const death = deaths.get(name);
      const alive = death === undefined || t < death;
      world.push({
        name,
        team: teams[name],
        pos: sample.pos,
        yaw: sample.yaw,
        pitch: sample.pitch,
        hp: alive ? hpAt(hpTracks.get(name), t) : 0,
        armor: DEFAULT_ARMOR,
        alive,
        weapon: weapons.get(name) ?? DEFAULT_WEAPON,
      });
    }

    const view: WorldView = {
      t,
      timeLeft: Math.max(0, LIVE_TIME_S - (t - BUY_TIME_S)),
      spike,
      players: world,
      recent,
      abilities,
    };
    const smokes = activeSmokes(smokeFields, t);

    for (const name of names) {
      const self = sampled.get(name);
      const selfWorld = world.find((p) => p.name === name);
      if (!self || !selfWorld?.alive) continue;
      const enemies: EnemyState[] = world
        .filter((p) => p.alive && p.name !== name)
        .map((p) => ({ name: p.name, pos: p.pos, alive: true }));
      const perception = perceptions.get(name);
      if (!perception) continue;
      const update = perception.update(nowMs, { pos: self.pos, yaw: self.yaw }, enemies, boxes, smokes);
      const situation = buildSituation(view, name, update, nowMs);
      out.push({
        matchId: segment.matchId,
        round: segment.round,
        t,
        player: name,
        view,
        situation,
        visible: update.visible.map((v) => v.name),
      });
    }
  }

  return out;
}

export { compareRounds, eventKey };

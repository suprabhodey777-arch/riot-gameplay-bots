/**
 * The bot's world model: everything a normal client learns from the server's
 * snapshot stream, hoop-shots and broadcasts. Positions of enemies live here only
 * so `buildSituation` can count the living; aiming uses `PerceptionUpdate`.
 */
import type { Action, LogEvent, Phase, SiteId, Team, Vec2, Vec3 } from '../contracts.ts';
import { CONTRACTS_VERSION } from '../contracts.ts';
import type { WorldPlayer, WorldView } from '../clone/situation.ts';
import type { EnemyState, Smoke } from './view.ts';

/** Smoke shape and lifetime from `game/public/js/game.js:132-138`. */
export const SMOKE_RADIUS = 2.6;
export const SMOKE_LIFETIME_MS = 18000;
/** Keep a little more than `RECENT_S` (3 s) of events for the situation builder. */
const RECENT_KEEP_S = 10;
/** How much of a teammate's movement the team layer looks at when it infers a push. */
const TRACK_KEEP_MS = 2000;
/** How long a teammate death stays interesting for a trade. */
const DEATH_KEEP_MS = 10000;
/** The longest a relayed teammate trace still counts as their current intent. */
export const TRACE_KEEP_MS = 3000;

export interface SnapshotPlayer {
  id: string;
  name: string;
  team: Team;
  pos: Vec3;
  yaw: number;
  pitch: number;
  hp: number;
  armor: number;
  alive: boolean;
  weapon: string;
  hasSpike?: boolean;
  money?: number;
}

export interface SnapshotSpike {
  holder?: string | null;
  dropped?: Vec2 | null;
  planted?: boolean;
  site?: SiteId | null;
  pos?: Vec2 | null;
  explodeTimer?: number;
}

export interface Snapshot {
  round: {
    number: number;
    phase: Phase;
    timer: number;
    practice?: boolean;
    spike: SnapshotSpike;
    winner?: Team | null;
    endReason?: string;
  };
  players: SnapshotPlayer[];
}

export interface WorldPlayerState extends WorldPlayer {
  id: string;
  hasSpike: boolean;
  money: number;
}

export interface WorldSpikeState {
  /** Player name, because everything the bot reasons about is by name. */
  holder: string | null;
  dropped: Vec2 | null;
  planted: boolean;
  site: SiteId | null;
  pos: Vec2 | null;
  explodeTimer: number;
}

export interface WorldSmoke {
  x: number;
  z: number;
  r: number;
  until: number;
}

/** A teammate bot's last relayed decision (`trace` socket event). */
export interface WorldTrace {
  action: Action;
  atMs: number;
}

/** A death the bot heard about, kept so a teammate's death can be traded. */
export interface WorldDeath {
  /** The player who died. */
  name: string;
  killer: string;
  /** Where they died, from the kill event. */
  pos: Vec2;
  atMs: number;
}

export interface AbilityMessage {
  id?: string;
  team?: Team;
  type?: string;
  at?: unknown;
}

export interface DamageMessage {
  victimId?: string;
  attackerId?: string;
  damage?: number;
  headshot?: boolean;
}

export interface KillMessage {
  killer?: { id?: string; name?: string; weapon?: string; headshot?: boolean };
  victim?: { id?: string; name?: string };
}

export type SnapshotKind = 'joined' | 'tick' | 'round-start' | 'phase' | 'round-end';

const isPoint = (p: unknown): p is Vec2 =>
  !!p && typeof p === 'object' && Number.isFinite((p as Vec2).x) && Number.isFinite((p as Vec2).z);

const ACTION_KINDS = new Set<string>([
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

const isActionKind = (kind: string): boolean => ACTION_KINDS.has(kind);

export function createWorld() {
  let round = 1;
  let phase: Phase = 'buy';
  let timeLeft = 0;
  let practice = false;
  let roundStartMs = 0;
  let clockSet = false;
  let selfName: string | null = null;
  const players = new Map<string, WorldPlayerState>();
  const smokes: WorldSmoke[] = [];
  const recent: LogEvent[] = [];
  const traces = new Map<string, WorldTrace>();
  /** Position samples per player name, newest last, for inferring a human's push. */
  const tracks = new Map<string, { x: number; z: number; atMs: number }[]>();
  const deaths: WorldDeath[] = [];
  let spike: WorldSpikeState = {
    holder: null,
    dropped: null,
    planted: false,
    site: null,
    pos: null,
    explodeTimer: 0,
  };

  /** Seconds since the last round-start / scenario the bot saw. */
  function t(now: number): number {
    if (!clockSet) return 0;
    return Math.max(0, (now - roundStartMs) / 1000);
  }

  function resetClock(now: number): void {
    roundStartMs = now;
    clockSet = true;
    recent.length = 0;
    smokes.length = 0;
    traces.clear();
    tracks.clear();
    deaths.length = 0;
  }

  function pushRecent(event: LogEvent, now: number): void {
    recent.push(event);
    const cutoff = t(now) - RECENT_KEEP_S;
    while (recent.length > 0 && recent[0].t < cutoff) recent.shift();
  }

  function byId(id: string): WorldPlayerState | null {
    return players.get(id) ?? null;
  }

  function byName(name: string): WorldPlayerState | null {
    for (const p of players.values()) if (p.name === name) return p;
    return null;
  }

  function self(): WorldPlayerState | null {
    return selfName ? byName(selfName) : null;
  }

  function applySnapshot(snapshot: Snapshot, now: number, kind: SnapshotKind): void {
    if (!snapshot || !snapshot.round || !Array.isArray(snapshot.players)) return;
    if (snapshot.round.number !== round) {
      round = snapshot.round.number;
      resetClock(now);
    }
    if (kind === 'round-start') resetClock(now);
    phase = snapshot.round.phase;
    timeLeft = Number.isFinite(snapshot.round.timer) ? snapshot.round.timer : 0;
    practice = !!snapshot.round.practice;

    players.clear();
    for (const p of snapshot.players) {
      if (!p || typeof p.id !== 'string' || typeof p.name !== 'string') continue;
      players.set(p.id, {
        id: p.id,
        name: p.name,
        team: p.team,
        pos: {
          x: Number.isFinite(p.pos?.x) ? p.pos.x : 0,
          y: Number.isFinite(p.pos?.y) ? p.pos.y : 0,
          z: Number.isFinite(p.pos?.z) ? p.pos.z : 0,
        },
        yaw: Number.isFinite(p.yaw) ? p.yaw : 0,
        pitch: Number.isFinite(p.pitch) ? p.pitch : 0,
        hp: Number.isFinite(p.hp) ? p.hp : 100,
        armor: Number.isFinite(p.armor) ? p.armor : 0,
        alive: p.alive !== false,
        weapon: typeof p.weapon === 'string' ? p.weapon : 'vandal',
        hasSpike: !!p.hasSpike,
        money: Number.isFinite(p.money) ? (p.money as number) : 0,
      });
    }

    const sp = snapshot.round.spike ?? {};
    spike = {
      holder: sp.holder ? (byId(sp.holder)?.name ?? null) : null,
      dropped: isPoint(sp.dropped) ? { x: sp.dropped.x, z: sp.dropped.z } : null,
      planted: !!sp.planted,
      site: sp.site === 'A' || sp.site === 'B' ? sp.site : null,
      pos: isPoint(sp.pos) ? { x: sp.pos.x, z: sp.pos.z } : null,
      explodeTimer: Number.isFinite(sp.explodeTimer) ? (sp.explodeTimer as number) : 0,
    };

    // Movement history, so a human teammate's direction can be inferred without
    // any trace event: the team layer compares their position one second apart.
    const trackCutoff = now - TRACK_KEEP_MS;
    for (const p of players.values()) {
      let samples = tracks.get(p.name);
      if (!samples) {
        samples = [];
        tracks.set(p.name, samples);
      }
      samples.push({ x: p.pos.x, z: p.pos.z, atMs: now });
      while (samples.length > 1 && samples[0].atMs < trackCutoff) samples.shift();
    }
  }

  function addSmoke(at: Vec2, now: number): void {
    smokes.push({ x: at.x, z: at.z, r: SMOKE_RADIUS, until: now + SMOKE_LIFETIME_MS });
  }

  function applyAbility(msg: AbilityMessage, now: number): void {
    if (!msg || msg.type !== 'smoke') return;
    const at = msg.at;
    if (!Array.isArray(at) || at.length < 2) return;
    const x = Number(at[0]);
    const z = Number(at[1]);
    if (!Number.isFinite(x) || !Number.isFinite(z)) return;
    addSmoke({ x, z }, now);
  }

  function applyDamage(msg: DamageMessage, now: number): void {
    if (!msg || typeof msg.victimId !== 'string' || typeof msg.attackerId !== 'string') return;
    const victim = byId(msg.victimId);
    const attacker = byId(msg.attackerId);
    if (!victim || !attacker) return;
    const damage = Number(msg.damage);
    pushRecent(
      {
        v: CONTRACTS_VERSION,
        matchId: '',
        round,
        t: t(now),
        player: attacker.name,
        type: 'damage',
        victim: victim.name,
        attacker: attacker.name,
        damage: Number.isFinite(damage) ? damage : 0,
        headshot: !!msg.headshot,
        hpLeft: Math.max(0, victim.hp),
      },
      now,
    );
  }

  function applyKill(msg: KillMessage, now: number): void {
    if (!msg || !msg.killer || !msg.victim) return;
    const killerName = typeof msg.killer.name === 'string' ? msg.killer.name : '';
    const victimName = typeof msg.victim.name === 'string' ? msg.victim.name : '';
    if (!killerName || !victimName) return;
    const killer = byName(killerName);
    const victim = byName(victimName);
    const killerPos = killer ? { ...killer.pos } : { x: 0, y: 0, z: 0 };
    const victimPos = victim ? { ...victim.pos } : { x: 0, y: 0, z: 0 };
    pushRecent(
      {
        v: CONTRACTS_VERSION,
        matchId: '',
        round,
        t: t(now),
        player: killerName,
        type: 'kill',
        victim: victimName,
        killer: killerName,
        weapon: typeof msg.killer.weapon === 'string' ? msg.killer.weapon : '',
        headshot: !!msg.killer.headshot,
        killerPos,
        victimPos,
      },
      now,
    );
    // Only where the victim died is trustworthy: whether the killer was visible to
    // anyone is not in this event, so its position is never stored here.
    if (!victim) return;
    deaths.push({ name: victimName, killer: killerName, pos: { x: victimPos.x, z: victimPos.z }, atMs: now });
    const cutoff = now - DEATH_KEEP_MS;
    while (deaths.length > 0 && deaths[0].atMs < cutoff) deaths.shift();
  }

  /**
   * A teammate bot's relayed `trace` socket event. Only the action is kept: the
   * trace carries the sender's own `Situation`, which is its private information.
   */
  function applyTrace(msg: unknown, now: number): void {
    if (!msg || typeof msg !== 'object') return;
    const event = msg as { bot?: unknown; action?: unknown };
    if (typeof event.bot !== 'string' || !event.bot) return;
    const action = event.action;
    if (!action || typeof action !== 'object') return;
    const kind = (action as { kind?: unknown }).kind;
    if (typeof kind !== 'string' || !isActionKind(kind)) return;
    traces.set(event.bot, { action: action as Action, atMs: now });
  }

  /** The last action a bot announced, or null when it is older than `maxAgeMs`. */
  function traceFor(name: string, now: number, maxAgeMs = TRACE_KEEP_MS): WorldTrace | null {
    const seen = traces.get(name);
    if (!seen || now - seen.atMs > maxAgeMs) return null;
    return seen;
  }

  /** How far a player moved over the last `windowMs`, or null with too few samples. */
  function recentMotion(name: string, now: number, windowMs = 1000): Vec2 | null {
    const samples = tracks.get(name);
    if (!samples || samples.length < 2) return null;
    let oldest: { x: number; z: number; atMs: number } | null = null;
    for (const sample of samples) {
      if (now - sample.atMs <= windowMs) {
        oldest = sample;
        break;
      }
    }
    const newest = samples[samples.length - 1];
    if (!oldest || oldest === newest) return null;
    return { x: newest.x - oldest.x, z: newest.z - oldest.z };
  }

  /** Deaths from the last `windowMs`, oldest first. */
  function recentDeaths(now: number, windowMs: number): WorldDeath[] {
    return deaths.filter((d) => now - d.atMs <= windowMs).map((d) => ({ ...d, pos: { ...d.pos } }));
  }

  /** Alive enemies of the player named `name`, for the perception cone. */
  function enemies(name: string): EnemyState[] {
    const me = byName(name);
    if (!me) return [];
    const out: EnemyState[] = [];
    for (const p of players.values()) {
      if (p.team === me.team || !p.alive) continue;
      out.push({ name: p.name, pos: { ...p.pos }, alive: true });
    }
    return out;
  }

  function activeSmokes(now: number): Smoke[] {
    return smokes.filter((s) => s.until > now).map((s) => ({ x: s.x, z: s.z, r: s.r }));
  }

  function view(now: number, abilities?: WorldView['abilities']): WorldView {
    const list: WorldPlayer[] = [];
    for (const p of players.values()) {
      list.push({
        name: p.name,
        team: p.team,
        pos: { ...p.pos },
        yaw: p.yaw,
        pitch: p.pitch,
        hp: p.hp,
        armor: p.armor,
        alive: p.alive,
        weapon: p.weapon,
      });
    }
    return {
      t: t(now),
      timeLeft,
      spike: {
        holder: spike.holder,
        planted: spike.planted,
        dropped: spike.dropped ? { ...spike.dropped } : null,
      },
      players: list,
      recent: [...recent],
      abilities,
    };
  }

  return {
    applySnapshot,
    resetClock,
    applyAbility,
    applyDamage,
    applyKill,
    applyTrace,
    addSmoke,
    enemies,
    activeSmokes,
    view,
    self,
    byName,
    byId,
    traceFor,
    recentMotion,
    recentDeaths,
    t,
    setSelfName(name: string) {
      selfName = name;
    },
    get round() {
      return round;
    },
    get phase() {
      return phase;
    },
    get timeLeft() {
      return timeLeft;
    },
    get practice() {
      return practice;
    },
    get spike() {
      return spike;
    },
    get players() {
      return [...players.values()];
    },
  };
}

export type World = ReturnType<typeof createWorld>;

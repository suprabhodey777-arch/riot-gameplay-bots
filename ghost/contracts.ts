/**
 * Ghost Rematch contracts (handoff stream 0). Every lane builds against these.
 * Changing a shape here needs every lane to agree.
 *
 * Units and axes are the base game's (`game/server.js`, `game/public/js/map.js`):
 * the ground is the x/z plane, y is up, 1 unit = 1 metre, the play area is
 * |x|, |z| <= 40. `yaw` and `pitch` are recorded exactly as the client sends them.
 */

export const CONTRACTS_VERSION = 1;

export type Team = 'attack' | 'defense';
export type Phase = 'buy' | 'live' | 'end';
export type SiteId = 'A' | 'B';
export type Weapon = 'classic' | 'sheriff' | 'spectre' | 'phantom' | 'vandal' | 'operator' | 'knife';
export type AbilitySlot = 'Q' | 'E' | 'X';

export interface Vec3 { x: number; y: number; z: number }
export interface Vec2 { x: number; z: number }

// ---------------------------------------------------------------------------
// 1. Log event: one JSON object per line in `logs/<matchId>.jsonl`, written by the server.

export type LogEventType =
  | 'move' | 'tracer' | 'hit' | 'ability' | 'teleport' | 'buy' | 'plant' | 'defuse'
  | 'kill' | 'damage' | 'round-start' | 'round-end' | 'spike' | 'scenario' | 'trace';

interface LogBase {
  v: typeof CONTRACTS_VERSION;
  matchId: string;
  /** Round number as the server counts it. */
  round: number;
  /** Seconds since the current round started (buy phase included). */
  t: number;
  /** Stable player name chosen at join, never the socket id. Empty for round-level events. */
  player: string;
}

export type LogEvent = LogBase & (
  /** At most 20 per second per player; the server drops extra moves. */
  | { type: 'move'; pos: Vec3; yaw: number; pitch: number; moving: boolean; crouch: boolean; team: Team; hp: number }
  | { type: 'tracer'; from: [number, number, number]; dir: [number, number, number] }
  /** `hit` is what the shooter claimed; `damage` is what the server applied. */
  | { type: 'hit'; victim: string; damage: number; headshot: boolean; weapon: string }
  | { type: 'damage'; victim: string; attacker: string; damage: number; headshot: boolean; hpLeft: number }
  | { type: 'kill'; victim: string; killer: string; weapon: string; headshot: boolean; killerPos: Vec3; victimPos: Vec3 }
  | { type: 'ability'; ability: string; at?: [number, number]; target?: string }
  | { type: 'teleport'; pos: Vec3 }
  | { type: 'buy'; item: string; money: number }
  | { type: 'plant'; stage: 'start' | 'stop' | 'done'; site: SiteId | null; pos: Vec2 }
  | { type: 'defuse'; stage: 'start' | 'stop' | 'done'; pos: Vec2 }
  | { type: 'spike'; stage: 'pickup' | 'drop' | 'exploded'; pos: Vec2 | null }
  | { type: 'round-start'; teams: Record<string, Team>; practice: boolean }
  | { type: 'round-end'; winner: Team | null; reason: string; scoreAtk: number; scoreDef: number }
  | { type: 'scenario'; scenario: ScenarioFile }
  | { type: 'trace'; trace: TraceEvent }
);

// ---------------------------------------------------------------------------
// Zone map: 15 to 25 named zones with waypoints, in `ghost/map/zones.json`.

export interface Zone {
  id: string;            // 'a-site', 'mid', 'b-main' ...
  name: string;          // callout text shown to players
  rect: { x1: number; x2: number; z1: number; z2: number };
  waypoint: Vec2;        // a walkable point inside the zone
}

export interface ZoneMap {
  zones: Zone[];
  /** Undirected walkable links between zone waypoints. */
  links: [string, string][];
}

/** Axis-aligned box, exported from `map.js` colliders to `ghost/map/colliders.json`. */
export interface Box { min: Vec3; max: Vec3 }

export interface MapExport {
  colliders: Box[];
  sites: Record<SiteId, { x1: number; x2: number; z1: number; z2: number }>;
  spawns: Record<Team, Vec2>;
}

// ---------------------------------------------------------------------------
// 2. Situation vector: what a player knows on one tick. ONE function builds it from a
// log (training) and from live state (play): `ghost/clone/situation.ts`.

export interface Situation {
  selfPos: Vec2;
  selfZone: string;
  selfHp: number;
  selfArmor: number;
  weapon: string;
  ammo: number | null;        // null when unknown (logs carry no ammo)
  abilitiesReady: Record<AbilitySlot, boolean>;
  /** True only with line of sight, inside the view cone, after the reaction delay. */
  enemyVisible: boolean;
  enemyRel: { dist: number; bearing: number } | null;   // bearing in radians, relative to own yaw
  enemyLastSeenZone: string | null;
  sinceSeen: number | null;   // seconds
  tookDamageRecent: boolean;  // within the last 3 s
  gotKillRecent: boolean;     // within the last 3 s
  side: Team;
  spikeState: 'carried' | 'dropped' | 'planted' | 'none';
  roundTime: number;          // seconds left in the current phase
  teammatesAlive: number;
  enemiesAlive: number;
}

// ---------------------------------------------------------------------------
// 3. Action set: what the cloning model predicts and the controller executes.

export type Action =
  | { kind: 'hold'; pos: Vec2; watchYaw: number }
  | { kind: 'peek_wide'; corner: Vec2; dir: 'left' | 'right' }
  | { kind: 'peek_jiggle'; corner: Vec2; dir: 'left' | 'right' }
  | { kind: 'fight'; target: string }
  | { kind: 'push'; zone: string }
  | { kind: 'retreat'; zone: string }
  | { kind: 'rotate'; zone: string }
  | { kind: 'use_ability'; slot: AbilitySlot; at: Vec2 | null }
  | { kind: 'plant'; site: SiteId }
  | { kind: 'defuse'; site: SiteId }
  | { kind: 'buy'; item: string };

export type ActionKind = Action['kind'];

// ---------------------------------------------------------------------------
// 4. Player profile: `profiles/<player>.json`, the measured numbers the controller uses.

export interface PlayerProfile {
  player: string;
  roundsRecorded: number;
  /** Median ms from first visibility to first shot. */
  reactionMs: number;
  /** Median degrees between crosshair and enemy at the shot. */
  aimErrorDeg: number;
  /** Median degrees per second while turning onto a target. */
  flickDegPerS: number;
  headshotRate: number;
  /** Share of time spent moving while firing, 0..1. */
  moveWhileFiring: number;
  /** Share of each action kind in this player's labelled play. */
  actionMix: Partial<Record<ActionKind, number>>;
  /** Share of time spent in each zone. */
  zoneMix: Record<string, number>;
  favouriteAbilities: string[];
}

// ---------------------------------------------------------------------------
// 5. Trace event: emitted by a bot on every decision, shown live in the overlay,
// relayed by the server as the socket message `trace`, and logged for the coach.

export interface TraceEvent {
  bot: string;               // the bot's stable name
  ghostOf: string | null;    // the human it was cloned from; null for the default bot
  round: number;
  t: number;
  action: Action;
  reason: string;            // one short line: why this action
  /** The recording the choice came from, or null in fallback. */
  matched: { player: string; matchId: string; round: number; t: number } | null;
  matchDistance: number | null;
  /** True when the nearest recording is beyond the threshold and the default bot is in control. */
  fallback: boolean;
  fallbackReason: 'no-close-recording' | 'stuck' | 'degenerate' | null;
  skillScale: number;        // 0.5 .. 1.5, 1 = the human's own level
  situation: Situation;
}

// ---------------------------------------------------------------------------
// Rematch messages between the browser client (`game/public/js/ghost-ui.js`) and the server.
//
// client -> server (socket.io, every one with an ack callback):
//   'rematch-list'  ()                                   ack RematchListAck
//   'rematch-start' { momentId: string, variation: boolean }  ack RematchStartAck
//   'skill-scale'   { value: number | null }             ack { ok: boolean }   (null = auto-tune)
//   'rematch-leave' ()                                   ack { ok: boolean }   (practice off, next normal round)
// server -> client:
//   'drill-state'   DrillState      after every rematch-start and skill change
//   'practice-end'  { winner: Team | null, reason: string }
//   'debrief'       Debrief         a few seconds after practice-end; template if the LLM is slow
//   'trace'         TraceEvent      relayed from bots
// HTTP: GET /logs/<matchId>.jsonl serves raw logs to the 3D replay page.

/** One death of this player that can be rematched. */
export interface Moment {
  id: string;
  matchId: string;
  round: number;
  t: number;
  killer: string;
  /** False until enough of the killer's play is recorded to build a ghost. */
  killerGhostReady: boolean;
  zone: string | null;
  weapon: string;
  beaten: boolean;
  attempts: number;
}

export type RematchListAck = { ok: true; moments: Moment[] } | { ok: false; error: string };
export type RematchStartAck = { ok: true; drill: DrillState } | { ok: false; error: string };

export interface DrillState {
  momentId: string;
  ghostOf: string;
  ghostBot: string;
  attempt: number;
  beaten: boolean;
  variation: boolean;
  skillScale: number;
  autoSkill: boolean;
  /** One line shown on the HUD, e.g. "Kill the ghost or survive 20 s". */
  goal: string;
}

export interface Debrief {
  momentId: string;
  attempt: number;
  text: string;
  source: 'llm' | 'template';
}

// ---------------------------------------------------------------------------
// Scenario file: everything the server needs to load a moment (`load-scenario`).

export interface ScenarioPlayer {
  name: string;
  team: Team;
  pos: Vec3;
  yaw: number;
  pitch: number;
  hp: number;
  armor: number;
  weapon: string;
  hasSpike: boolean;
  ultPoints: number;
}

export interface ScenarioFile {
  id: string;
  /** Where the moment came from, when it was rebuilt from a log. */
  source: { matchId: string; round: number; t: number } | null;
  phase: Phase;
  timer: number;             // seconds left in the phase
  spike: {
    planted: boolean;
    site: SiteId | null;
    pos: Vec2 | null;
    explodeTimer: number;    // seconds, only when planted
    holder: string | null;   // player name
    dropped: Vec2 | null;
  };
  players: ScenarioPlayer[];
  /** True: scoring and economy are off while this scenario runs. */
  practice: boolean;
}

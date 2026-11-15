/**
 * Cross-lane contract between the simulation (brain) and the 3D client.
 * Dependency-free, with the version and agent loadouts shared by every lane.
 * All coordinates are metres on a 2D floor plan: x east, y north.
 * Yaw is in radians, 0 = +x, counter-clockwise.
 */

import type { ArmorId, Loadout, Slot, WeaponId, RoundType } from './weapons.ts';
export { WEAPONS, ARMOR, DEFAULT_LOADOUT } from './weapons.ts';
export type { WeaponId, Weapon, ArmorId, Loadout, Slot, RoundType } from './weapons.ts';
export const CONTRACT_VERSION = 'v5';

/** A point or vector on the floor plan, in metres. */
export interface Vec2 {
  x: number;
  y: number;
}

/** A bomb site. Maps have A and B; some also have C (v3). */
export type SiteId = 'A' | 'B' | 'C';

/** The two sides of a round. */
export type Team = 'attack' | 'defense';

/** A map. The sim and the 3D renderer both build from this one description. */
export interface MapDef {
  id: string;
  name: string;
  /** Playable extent in metres; the floor plan spans (0,0) to (width,height). */
  bounds: { width: number; height: number };
  /** Closed polygons that block movement and sight; `height` is for rendering. */
  walls: { id: string; points: Vec2[]; height: number }[];
  /** Plant zones as polygons. `C` is present only on three-site maps (v3). */
  sites: { A: Vec2[]; B: Vec2[]; C?: Vec2[] };
  spawns: { attack: Vec2[]; defense: Vec2[] };
  /** Named regions, used for callouts and for the analyzer. */
  callouts: { name: string; polygon: Vec2[] }[];
}

export type AgentId = 'rook' | 'haze' | 'wren' | 'pike';
export interface AbilityCharges { smoke: number; flash: number; dash: number; recon: number }
export const AGENTS: Record<AgentId, { role: 'duelist' | 'controller' | 'initiator' | 'sentinel'; kit: AbilityCharges }> = {
  rook: { role: 'duelist', kit: { smoke: 0, flash: 1, dash: 1, recon: 0 } },
  haze: { role: 'controller', kit: { smoke: 3, flash: 0, dash: 0, recon: 0 } },
  wren: { role: 'initiator', kit: { smoke: 0, flash: 2, dash: 0, recon: 1 } },
  pike: { role: 'sentinel', kit: { smoke: 1, flash: 0, dash: 0, recon: 2 } },
};
export const agentForArchetype = (id: string): AgentId => ({
  'entry-duelist': 'rook', 'utility-controller': 'haze', 'patient-lurker': 'pike', 'off-angle-anchor': 'pike', baseline: 'wren',
} as Record<string, AgentId>)[id] ?? 'wren';

/** One player or bot as the sim currently sees it. */
export interface AgentState {
  id: string;
  team: Team;
  isBot: boolean;
  pos: Vec2;
  yaw: number;
  /** 0-100. */
  hp: number;
  alive: boolean;
  agent: AgentId;
  abilities: AbilityCharges;
  blindUntilTick: number;
  revealedUntilTick: number;
  smokesLeft: number;
  carryingSpike: boolean;
  credits: number;
  armor: number;
  primary: WeaponId | null;
  secondary: WeaponId;
  slot: Slot;
  ammo: Record<WeaponId, number>;
  reserve: Record<WeaponId, number>;
  reloadingUntilTick: number;
  scoped: boolean;
}

/** A smoke cloud that blocks line of sight until `endsAtTick`. */
export interface SmokeState {
  id: string;
  ownerId: string;
  pos: Vec2;
  radius: number;
  endsAtTick: number;
}

/** Where the spike is and what has happened to it. */
export type SpikeState =
  | { status: 'carried'; carrierId: string }
  | { status: 'dropped'; pos: Vec2 }
  | {
      status: 'planted';
      pos: Vec2;
      site: SiteId;
      plantedAtTick: number;
      /** Seconds of defuse so far (0 when nobody is defusing). */
      defuseProgressS: number;
    }
  | { status: 'defused' }
  | { status: 'detonated' };

/** The whole world at one tick. Read-only for clients. */
export interface SimState {
  tick: number;
  /** Ticks per second (20). */
  tickRate: number;
  roundTimeLeftS: number;
  phase: 'buy' | 'live' | 'post_plant' | 'ended';
  agents: AgentState[];
  smokes: SmokeState[];
  spike: SpikeState;
  winner: Team | null;
}

/** A shot the client already raycast: who it was aimed at and what it hit. */
export interface FireIntent {
  targetId: string;
  result: 'head' | 'body' | 'miss';
}

/** What one agent wants to do this tick. */
export interface PlayerInput {
  agentId: string;
  /** Desired direction in the world frame, length at most 1. */
  move: Vec2;
  yaw: number;
  walk: boolean;
  crouch: boolean;
  fire: FireIntent | null;
  /** Throw a smoke at this point, or null. */
  smokeAt: Vec2 | null;
  flashAt?: Vec2 | null;
  dash?: boolean;
  reload?: boolean;
  slot?: Slot;
  scoped?: boolean;
  reconAt?: Vec2 | null;
  plantHeld: boolean;
  defuseHeld: boolean;
}

/** Something that happened during a step. */
export type SimEvent =
  | { type: 'reload'; tick: number; agent: string; weapon: WeaponId }
  | { type: 'switch'; tick: number; agent: string; slot: Slot }
  | { type: 'buy'; tick: number; agent: string; item: WeaponId | ArmorId; price: number }
  | { type: 'spotted'; tick: number; observer: string; target: string }
  | {
      type: 'damage';
      tick: number;
      attacker: string;
      victim: string;
      amount: number;
      part: 'head' | 'body';
      /** Positions at the moment of the hit. */
      attackerPos: Vec2;
      victimPos: Vec2;
    }
  | {
      type: 'kill';
      tick: number;
      killer: string;
      victim: string;
      part: 'head' | 'body';
      /** Positions at the moment of the kill. */
      killerPos: Vec2;
      victimPos: Vec2;
    }
  | { type: 'flash'; thrower: string; pos: Vec2; tick: number }
  | { type: 'blinded'; agent: string; untilTick: number; by: string; tick: number }
  | { type: 'dash'; agent: string; from: Vec2; to: Vec2; tick: number }
  | { type: 'recon'; thrower: string; pos: Vec2; tick: number }
  | { type: 'revealed'; target: string; by: string; untilTick: number; tick: number }
  | { type: 'footstep'; agent: string; pos: Vec2; loud: boolean; tick: number }
  | { type: 'smoke'; tick: number; agent: string; pos: Vec2 }
  | { type: 'plant'; tick: number; agent: string; site: SiteId }
  | { type: 'defuse'; tick: number; agent: string }
  | {
      type: 'round_end';
      tick: number;
      winner: Team;
      reason: 'elimination' | 'detonated' | 'defused' | 'time';
    };

/** Why a bot did something: the behavior-tree node, the reason, and its traits. */
export interface TraceEntry {
  tick: number;
  botId: string;
  node: string;
  reason: string;
  traits: Record<string, number>;
}

/** What `step()` returns. */
export interface StepResult {
  state: SimState;
  events: SimEvent[];
  trace: TraceEntry[];
}

/**
 * A finished match in the shape the weakness analyzer reads. Each round is a
 * fresh sim starting at tick 0, so event ticks are relative to the round start.
 */
export interface MatchLog {
  matchId: string;
  mapId: string;
  playerId: string;
  rounds: {
    index: number;
    roundType?: RoundType;
    loadout?: Loadout;
    playerTeam: Team;
    playerRole: 'duelist' | 'initiator' | 'controller' | 'sentinel';
    /** Every agent id that appears in this round's events, mapped to its team. */
    teams: Record<string, Team>;
    events: SimEvent[];
    winner: Team;
  }[];
}

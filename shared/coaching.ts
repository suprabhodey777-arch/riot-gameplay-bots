/**
 * Coaching contract: weaknesses, drills and bot traits. Shared by the
 * analyzer, the bot, the coach and the 3D client (which shows the drill goal
 * and the debrief). Type-only.
 */
import type { SimEvent, SiteId, Team, TraceEntry, Vec2 } from './contract.ts';

/** The situations we measure and drill. */
export type Skill = 'entry' | 'clutch' | 'time_pressure' | 'smoke_usage';

/** One moment in a match that supports a finding, so the player can check it. */
export interface WeaknessEvidence {
  matchId: string;
  round: number;
  tick: number;
  /** Plain words, e.g. "first death on entry, 3 s after first contact". */
  note: string;
  /** Where everyone was at that moment, so a drill can rebuild it (see `analyzer/rehearse.ts`). */
  situation?: Situation;
}

/**
 * A moment of a round, rebuilt from the log (v3, optional). Logs only carry positions on
 * damage and kill events, so a position is the agent's latest known one at or before the
 * moment; an agent with no known position is placed near the centre of the callout the
 * action happened in (an approximation). Enemies are listed killer-first where there is one.
 */
export interface Situation {
  /** The player's side. */
  side: Team;
  /** The site the moment is about: where the spike was planted, else the site nearest the action. */
  site?: SiteId;
  playerPos: Vec2;
  /** Enemies still alive at the moment. */
  enemies: { pos: Vec2 }[];
  /** Living teammates (not the player) with a known position. */
  allies: { pos: Vec2 }[];
  roundTimeLeftS: number;
  /** Set when the spike was already down; `pos` is the site's centre (the log has no plant position). */
  spikePlanted: { site: SiteId; pos: Vec2; secondsLeft: number } | null;
  playerHasSpike: boolean;
}

/** How a player does at one skill against a benchmark for their role. */
export interface WeaknessFinding {
  skill: Skill;
  /** The player's success rate at this skill, 0-1. */
  rate: number;
  /** The benchmark rate for their role, 0-1. */
  benchmark: number;
  /** 0 (fine) to 1 (worst), how far below the benchmark, weighted by confidence. */
  severity: number;
  /** How many situations the rate is based on. */
  sampleSize: number;
  summary: string;
  evidence: WeaknessEvidence[];
}

/** Everything the analyzer found for one player, worst first. */
export interface WeaknessReport {
  playerId: string;
  role: 'duelist' | 'initiator' | 'controller' | 'sentinel';
  matchesRead: number;
  findings: WeaknessFinding[];
}

/** How a bot plays. Every number is 0-1 unless the name says otherwise. */
export interface BotTraits {
  /** Delay from first seeing an enemy to being able to fire. */
  reactionMs: number;
  /** Chance a shot is a head shot once the bot is allowed to fire. */
  headAccuracy: number;
  /** Chance a shot that is not a head shot hits the body. */
  bodyAccuracy: number;
  /** Pushes for contact rather than holding. */
  aggression: number;
  /** Prefers off-angles to the common spot. */
  offAngle: number;
  /** Waits and lurks instead of moving with the clock. */
  patience: number;
  /** Uses smokes, and uses them well. */
  utility: number;
}

/** A named playstyle: the archetype mode, and the bots a drill uses. */
export interface Archetype {
  id: string;
  name: string;
  description: string;
  traits: BotTraits;
}

/** One bot in a drill. */
export interface DrillBot {
  id: string;
  team: Team;
  spawn: Vec2;
  archetypeId: string;
  traits: BotTraits;
  /**
   * Optional (rehearsal drills): the bot stands at `pos` facing `yaw` until it sees an enemy,
   * instead of walking to its role's usual spot.
   */
  hold?: { pos: Vec2; yaw: number };
}

/** A scenario built to practise one skill. */
export interface DrillSpec {
  id: string;
  skill: Skill;
  title: string;
  /** What the player is asked to do, shown on screen. */
  goal: string;
  mapId: string;
  seed: number;
  /** 0 (easiest) to 1 (hardest); set by the staircase. */
  difficulty: number;
  player: { id: string; team: Team; spawn: Vec2; hp: number; carryingSpike: boolean };
  bots: DrillBot[];
  roundTimeLeftS: number;
  /** Set for retake and post-plant drills. */
  spikePlanted: { site: SiteId; pos: Vec2; secondsLeft: number } | null;
}

/** What happened when a drill was played. */
export interface DrillResult {
  drillId: string;
  skill: Skill;
  success: boolean;
  ticks: number;
  events: SimEvent[];
  trace: TraceEntry[];
}

/** The coach's write-up of a drill or a session. */
export interface Debrief {
  text: string;
  /** 'llm' when the model wrote it and it passed the number check, else 'template'. */
  source: 'llm' | 'template';
}

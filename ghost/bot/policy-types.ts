/**
 * The interface the cloned-ghost policy (`ghost/clone/policy.ts`) exposes to the
 * bot runtime. The policy lane writes that module; this file only describes it,
 * so the bot compiles and runs while the policy is still missing.
 */
import type { Action, PlayerProfile, Situation } from '../contracts.ts';

/** The recording a ghost decision came from (`TraceEvent.matched`). */
export interface GhostMatch {
  player: string;
  matchId: string;
  round: number;
  t: number;
}

export interface GhostDecision {
  action: Action;
  matched: GhostMatch;
  /** Distance between this situation and the recorded one; smaller is closer. */
  distance: number;
}

export interface GhostPolicy {
  profile: PlayerProfile;
  /** Decisions at or below this distance are the ghost's to make. */
  threshold: number;
  decide(situation: Situation): GhostDecision | null;
}

/** The four numbers the skill scale bends; see `ghost/bot/run.ts`. */
export interface SkillNumbers {
  reactionMs: number;
  aimErrorDeg: number;
  flickDegPerS: number;
  headshotRate: number;
}

/** Used whenever no recorded profile is available. */
export const DEFAULTS: SkillNumbers = {
  reactionMs: 250,
  aimErrorDeg: 2.5,
  flickDegPerS: 360,
  headshotRate: 0.25,
};

/**
 * The profile's numbers at the drill's skill scale: a better player reacts
 * sooner, aims tighter and turns faster. `scale` is clamped to the server's
 * 0.5 .. 1.5 range, so a missing or broken config cannot divide by zero.
 */
export function skillNumbers(profile: PlayerProfile | null, scale: number): SkillNumbers {
  const safe = Number.isFinite(scale) ? Math.min(1.5, Math.max(0.5, scale)) : 1;
  const base: SkillNumbers = profile
    ? {
        reactionMs: profile.reactionMs,
        aimErrorDeg: profile.aimErrorDeg,
        flickDegPerS: profile.flickDegPerS,
        headshotRate: profile.headshotRate,
      }
    : DEFAULTS;
  return {
    reactionMs: base.reactionMs / safe,
    aimErrorDeg: base.aimErrorDeg / safe,
    flickDegPerS: base.flickDegPerS * safe,
    headshotRate: base.headshotRate,
  };
}

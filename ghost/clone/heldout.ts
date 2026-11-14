/**
 * Held-out fidelity: does the ghost actually copy this human?
 *
 * Each player's natural-play rounds are split deterministically 80/20 by round.
 * The ghost and its profile are built from the 80%; the remaining rounds are
 * scored for action-kind agreement against the human and against a baseline
 * that always answers the population's most common action kind.
 */
import type { ActionKind } from '../contracts.ts';
import type { LogEvent } from '../contracts.ts';
import type { Frame } from './replay.ts';
import type { LabelledFrame } from './label.ts';
import { measureProfileWithCounts } from './profile.ts';
import type { MeasuredProfile, ProfileCounts } from './profile.ts';
import { createPolicy, featureVector, leaveOneOutThreshold } from './policy.ts';
import type { PolicySample } from './policy.ts';

export interface PlayerRound {
  matchId: string;
  round: number;
  frames: Frame[];
  labelled: LabelledFrame[];
  events: LogEvent[];
}

export interface RoundSplit {
  train: PlayerRound[];
  heldout: PlayerRound[];
}

/**
 * Deterministic split: sort by matchId then round, and hold out the 5th, 10th,
 * ... round. With fewer than five rounds the last one is held out.
 */
export function splitPlayerRounds(rounds: PlayerRound[]): RoundSplit {
  const sorted = [...rounds].sort((a, b) => {
    if (a.matchId !== b.matchId) return a.matchId < b.matchId ? -1 : 1;
    return a.round - b.round;
  });
  const heldout: PlayerRound[] = [];
  const train: PlayerRound[] = [];
  if (sorted.length === 0) return { train, heldout };
  if (sorted.length < 5) {
    train.push(...sorted.slice(0, -1));
    heldout.push(sorted[sorted.length - 1]);
    return { train, heldout };
  }
  for (let i = 0; i < sorted.length; i++) {
    if (i % 5 === 4) heldout.push(sorted[i]);
    else train.push(sorted[i]);
  }
  return { train, heldout };
}

export function countActionKinds(labelled: LabelledFrame[]): Map<ActionKind, number> {
  const counts = new Map<ActionKind, number>();
  for (const { action } of labelled) counts.set(action.kind, (counts.get(action.kind) ?? 0) + 1);
  return counts;
}

/** The most common action kind, ties broken alphabetically for determinism. */
export function mostCommonKind(counts: Map<ActionKind, number>): ActionKind {
  let best: ActionKind | null = null;
  let bestCount = -1;
  for (const [kind, count] of counts) {
    if (count > bestCount || (count === bestCount && best !== null && kind < best)) {
      best = kind;
      bestCount = count;
    }
  }
  return best ?? 'hold';
}

export interface HeldoutReport {
  player: string;
  rounds: { total: number; train: number; heldout: number };
  samples: number;
  threshold: number;
  frames: { train: number; heldout: number };
  ghost: { agreement: number | null; n: number };
  fallback: { rate: number | null; n: number };
  baseline: { agreement: number | null; n: number; kind: ActionKind };
  traits: {
    reactionMs: { ghost: number; heldout: number; absError: number | null; n: number };
    aimErrorDeg: { ghost: number; heldout: number; absError: number | null; n: number };
    headshotRate: { ghost: number; heldout: number; absError: number | null; n: number };
  };
  profile: { train: MeasuredProfile; heldout: MeasuredProfile; trainCounts: ProfileCounts; heldoutCounts: ProfileCounts };
}

function samplesFor(player: string, rounds: PlayerRound[]): PolicySample[] {
  const out: PolicySample[] = [];
  for (const round of rounds) {
    for (const l of round.labelled) {
      if (l.frame.player !== player) continue;
      out.push({
        features: featureVector(l.frame.situation),
        action: l.action,
        source: { player, matchId: round.matchId, round: round.round, t: l.frame.t },
      });
    }
  }
  return out;
}

/** Evaluate one player against their own held-out rounds. */
export function evaluatePlayer(
  player: string,
  rounds: PlayerRound[],
  baselineKind: ActionKind,
): HeldoutReport {
  const split = splitPlayerRounds(rounds);
  const trainEvents = split.train.flatMap((r) => r.events);
  const heldoutEvents = split.heldout.flatMap((r) => r.events);
  const trainFrames = split.train.flatMap((r) => r.frames).filter((f) => f.player === player);
  const heldoutFrames = split.heldout.flatMap((r) => r.frames).filter((f) => f.player === player);

  const trainMeasured = measureProfileWithCounts(player, trainFrames, trainEvents);
  const heldoutMeasured = measureProfileWithCounts(player, heldoutFrames, heldoutEvents);
  const samples = samplesFor(player, split.train);
  const policy = createPolicy(trainMeasured.profile, samples, leaveOneOutThreshold(samples));

  let ghostHits = 0;
  let ghostN = 0;
  let fallbackN = 0;
  let baselineHits = 0;
  let baselineN = 0;
  for (const round of split.heldout) {
    for (const l of round.labelled) {
      if (l.frame.player !== player) continue;
      baselineN++;
      if (l.action.kind === baselineKind) baselineHits++;
      const decision = policy.decide(l.frame.situation);
      if (!decision || decision.distance > policy.threshold) {
        fallbackN++;
        continue;
      }
      ghostN++;
      if (decision.action.kind === l.action.kind) ghostHits++;
    }
  }

  const trait = (ghost: number, heldout: number, n: number) => ({
    ghost,
    heldout,
    absError: n > 0 ? Math.abs(ghost - heldout) : null,
    n,
  });

  return {
    player,
    rounds: { total: rounds.length, train: split.train.length, heldout: split.heldout.length },
    samples: samples.length,
    threshold: policy.threshold,
    frames: { train: trainFrames.length, heldout: heldoutFrames.length },
    ghost: { agreement: ghostN > 0 ? ghostHits / ghostN : null, n: ghostN },
    fallback: { rate: baselineN > 0 ? fallbackN / baselineN : null, n: baselineN },
    baseline: { agreement: baselineN > 0 ? baselineHits / baselineN : null, n: baselineN, kind: baselineKind },
    traits: {
      reactionMs: trait(
        trainMeasured.profile.reactionMs,
        heldoutMeasured.profile.reactionMs,
        heldoutMeasured.counts.reaction,
      ),
      aimErrorDeg: trait(
        trainMeasured.profile.aimErrorDeg,
        heldoutMeasured.profile.aimErrorDeg,
        heldoutMeasured.counts.aim,
      ),
      headshotRate: trait(
        trainMeasured.profile.headshotRate,
        heldoutMeasured.profile.headshotRate,
        heldoutMeasured.counts.hits,
      ),
    },
    profile: {
      train: trainMeasured.profile,
      heldout: heldoutMeasured.profile,
      trainCounts: trainMeasured.counts,
      heldoutCounts: heldoutMeasured.counts,
    },
  };
}

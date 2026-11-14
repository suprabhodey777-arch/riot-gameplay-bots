import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { afterAll, beforeAll, describe, expect, it } from 'vitest';

import type { ActionKind, Situation } from '../contracts.ts';
import { makeFixture } from './fixtures.ts';
import type { Fixture } from './fixtures.ts';
import { readLogFiles, readLogs, segments, frames } from './replay.ts';
import type { Frame, ReplaySegment } from './replay.ts';
import { labelFrames } from './label.ts';
import type { LabelledFrame } from './label.ts';
import { measureProfile } from './profile.ts';
import { FEATURE_WEIGHTS, leaveOneOutThreshold, loadPolicy, weightedDistance } from './policy.ts';
import type { GhostPolicy, PolicySample } from './policy.ts';

function kindCounts(labelled: LabelledFrame[], player: string): Record<string, number> {
  const counts: Record<string, number> = {};
  for (const l of labelled) {
    if (l.frame.player !== player) continue;
    counts[l.action.kind] = (counts[l.action.kind] ?? 0) + 1;
  }
  return counts;
}

describe('ghost cloning pipeline', () => {
  let fixture: Fixture;
  let allSegments: ReplaySegment[] = [];
  let allFrames: Frame[] = [];
  let allLabelled: LabelledFrame[] = [];
  let report: {
    population: { kind: ActionKind; n: number };
    players: Record<
      string,
      {
        ghost: { agreement: number | null; n: number };
        fallback: { rate: number | null; n: number };
        baseline: { agreement: number | null; n: number; kind: ActionKind };
        traits: Record<string, { n: number; absError: number | null }>;
      }
    >;
  };

  beforeAll(() => {
    fixture = makeFixture();
    const files = readLogFiles(fixture.logsDir);
    expect(files).toHaveLength(1);
    expect(files[0].skipped).toBe(fixture.skippedLines);
    for (const file of files) {
      const fileSegments = segments(file.events);
      allSegments.push(...fileSegments);
      for (const segment of fileSegments) {
        const segmentFrames = frames(segment);
        allFrames.push(...segmentFrames);
        allLabelled.push(...labelFrames(segmentFrames, segment.events));
      }
    }
    expect(readLogs(fixture.logsDir)).toHaveLength(1);

    const buildPath = fileURLToPath(new URL('./build.ts', import.meta.url));
    execFileSync(process.execPath, [buildPath, '--logs', fixture.logsDir, '--out', fixture.outDir], {
      encoding: 'utf8',
    });
    report = JSON.parse(readFileSync(join(fixture.outDir, 'report.json'), 'utf8'));
  }, 120_000);

  afterAll(() => {
    fixture?.cleanup();
  });

  it('skips practice segments and keeps only natural-play rounds', () => {
    expect(allSegments).toHaveLength(fixture.rounds);
    expect(allSegments.map((s) => s.round)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
    expect(allSegments.some((s) => s.round === 90)).toBe(false);
    for (const segment of allSegments) {
      expect(segment.events.some((e) => e.type === 'scenario')).toBe(false);
      expect(segment.events.some((e) => e.type === 'round-start' && e.practice)).toBe(false);
    }
    expect(allFrames.length).toBeGreaterThan(0);
    expect(allFrames.every((f) => f.t >= 20)).toBe(true);
    expect(allFrames.every((f) => f.situation.roundTime <= 100 && f.situation.roundTime >= 0)).toBe(true);
  });

  it('labels the rusher as push/fight and the anchor as hold/fight', () => {
    const rusher = kindCounts(allLabelled, 'rusher');
    const anchor = kindCounts(allLabelled, 'anchor');
    const rusherTotal = Object.values(rusher).reduce((a, b) => a + b, 0);
    const anchorTotal = Object.values(anchor).reduce((a, b) => a + b, 0);

    expect((rusher.push ?? 0) / rusherTotal).toBeGreaterThan(0.5);
    expect(((rusher.push ?? 0) + (rusher.fight ?? 0)) / rusherTotal).toBeGreaterThan(0.75);
    expect((rusher.push ?? 0) > (rusher.hold ?? 0)).toBe(true);

    expect(((anchor.hold ?? 0) + (anchor.fight ?? 0)) / anchorTotal).toBeGreaterThan(0.6);
    expect((anchor.hold ?? 0) / anchorTotal).toBeGreaterThan(0.5);
    expect((anchor.hold ?? 0) > (anchor.push ?? 0)).toBe(true);
  });

  it('measures the planted aim error within half a degree', () => {
    const events = allSegments.flatMap((s) => s.events);
    for (const player of ['rusher', 'anchor']) {
      const profile = measureProfile(player, allFrames, events);
      expect(profile.defaulted).not.toContain('aimErrorDeg');
      expect(Math.abs(profile.aimErrorDeg - fixture.plantedAimErrorDeg)).toBeLessThanOrEqual(0.5);
    }
  });

  it('gives the two ghosts different actions for the same situation', () => {
    const situation: Situation = {
      selfPos: { x: -17, z: 14 },
      selfZone: 'a-site',
      selfHp: 100,
      selfArmor: 0,
      weapon: 'vandal',
      ammo: null,
      abilitiesReady: { Q: true, E: true, X: false },
      enemyVisible: false,
      enemyRel: null,
      enemyLastSeenZone: null,
      sinceSeen: null,
      tookDamageRecent: false,
      gotKillRecent: false,
      side: 'attack',
      spikeState: 'carried',
      roundTime: 60,
      teammatesAlive: 2,
      enemiesAlive: 2,
    };
    const rusher: GhostPolicy | null = loadPolicy('rusher', fixture.outDir);
    const anchor: GhostPolicy | null = loadPolicy('anchor', fixture.outDir);
    expect(rusher).not.toBeNull();
    expect(anchor).not.toBeNull();
    const a = rusher!.decide(situation);
    const b = anchor!.decide(situation);
    expect(a).not.toBeNull();
    expect(b).not.toBeNull();
    expect(a!.action.kind).not.toBe(b!.action.kind);
    expect(a!.matched.player).toBe('rusher');
    expect(b!.matched.player).toBe('anchor');
  });

  it('gives every published policy a non-zero fallback threshold', () => {
    const players = Object.keys(report.players);
    expect(players.length).toBeGreaterThan(0);
    for (const player of players) {
      const policy = loadPolicy(player, fixture.outDir);
      expect(policy).not.toBeNull();
      expect(policy!.threshold).toBeGreaterThan(0);
    }
  });

  it('falls back on less than half of each player’s held-out frames', () => {
    for (const [player, held] of Object.entries(report.players)) {
      expect(held.fallback.rate, player).not.toBeNull();
      expect(held.fallback.rate!, player).toBeLessThan(0.5);
      expect(held.fallback.n, player).toBeGreaterThan(0);
    }
  });

  it('beats the population baseline on each player’s held-out rounds', () => {
    for (const player of ['rusher', 'anchor']) {
      const held = report.players[player];
      expect(held).toBeDefined();
      expect(held.ghost.n).toBeGreaterThan(0);
      expect(held.baseline.n).toBeGreaterThan(0);
      expect(held.ghost.agreement).not.toBeNull();
      expect(held.baseline.agreement).not.toBeNull();
      expect(held.ghost.agreement!).toBeGreaterThan(held.baseline.agreement!);
      expect(held.fallback.n).toBeGreaterThan(0);
      expect(held.fallback.rate).not.toBeNull();
      expect(held.traits.reactionMs.n).toBeGreaterThan(0);
      expect(held.traits.aimErrorDeg.n).toBeGreaterThan(0);
    }
    expect(report.population.n).toBeGreaterThan(0);
  });

  it('returns null from loadPolicy when a player was never recorded', () => {
    expect(loadPolicy('nobody-with-this-name', fixture.outDir)).toBeNull();
  });

  it('uses GHOST_PROFILES_DIR when no explicit directory is passed', () => {
    const previous = process.env.GHOST_PROFILES_DIR;
    process.env.GHOST_PROFILES_DIR = fixture.outDir;
    try {
      const policy = loadPolicy('rusher');
      expect(policy).not.toBeNull();
      expect(policy!.profile.player).toBe('rusher');
    } finally {
      if (previous === undefined) delete process.env.GHOST_PROFILES_DIR;
      else process.env.GHOST_PROFILES_DIR = previous;
    }
  });

  it('writes the profile, samples and report files', () => {
    for (const name of ['rusher.json', 'rusher.samples.json', 'anchor.json', 'anchor.samples.json', 'report.json']) {
      expect(existsSync(join(fixture.outDir, name))).toBe(true);
    }
    const samples = JSON.parse(readFileSync(join(fixture.outDir, 'rusher.samples.json'), 'utf8')) as unknown[];
    expect(Array.isArray(samples)).toBe(true);
    expect(samples.length).toBeGreaterThan(0);
    const profile = JSON.parse(readFileSync(join(fixture.outDir, 'rusher.json'), 'utf8')) as {
      player: string;
      threshold: number;
      reactionMs: number;
    };
    expect(profile.player).toBe('rusher');
    expect(profile.reactionMs).toBeGreaterThan(0);
    expect(typeof profile.threshold).toBe('number');
  });
});

function makeSample(round: number, t: number, firstFeature: number): PolicySample {
  const features = Array.from({ length: FEATURE_WEIGHTS.length }, () => 0);
  features[0] = firstFeature;
  return {
    features,
    action: { kind: 'hold', pos: { x: 0, z: 0 }, watchYaw: 0 },
    source: { player: 'p', matchId: 'm', round, t },
  };
}

/** The previous all-samples behaviour, kept only to prove the regression. */
function legacyThreshold(samples: PolicySample[]): number {
  const nearest = samples.map((sample, i) => {
    let best = Number.POSITIVE_INFINITY;
    samples.forEach((other, j) => {
      if (i === j) return;
      best = Math.min(best, weightedDistance(sample.features, other.features));
    });
    return best;
  });
  nearest.sort((a, b) => a - b);
  return nearest[Math.round(0.9 * (nearest.length - 1))];
}

describe('leaveOneOutThreshold', () => {
  it('ignores same-round near-duplicate frames when another round exists', () => {
    const roundA = [0, 0.25, 0.5].map((t) => makeSample(1, t, 0));
    const roundB = [0, 0.25, 0.5].map((t) => makeSample(2, t, 1));
    const samples = [...roundA, ...roundB];
    const crossRound = weightedDistance(roundA[0].features, roundB[0].features);

    expect(crossRound).toBeGreaterThan(0);
    expect(legacyThreshold(samples)).toBe(0);
    expect(leaveOneOutThreshold(samples)).toBeCloseTo(crossRound, 12);
  });

  it('uses samples at least 2 s apart when only one round was recorded', () => {
    const samples = [
      makeSample(1, 0, 0),
      makeSample(1, 0.25, 0),
      makeSample(1, 0.5, 0),
      makeSample(1, 3, 1),
      makeSample(1, 3.25, 1),
    ];
    const spaced = weightedDistance(samples[0].features, samples[3].features);

    expect(legacyThreshold(samples)).toBe(0);
    expect(leaveOneOutThreshold(samples)).toBeCloseTo(spaced, 12);
  });
});

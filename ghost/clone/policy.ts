/**
 * Nearest-neighbour ghost policy and its on-disk loader.
 *
 * Live play imports `loadPolicy` only; training writes the two JSON files the
 * loader needs. Situations the training set covers well return the human's
 * recorded action; `threshold` is the distance at which the live bot should
 * hand control back to its default bot.
 */
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import type { Action, PlayerProfile, Situation } from '../contracts.ts';
import { loadZones } from '../bot/nav.ts';

export interface SampleSource {
  player: string;
  matchId: string;
  round: number;
  t: number;
}

export interface PolicySample {
  features: number[];
  action: Action;
  source: SampleSource;
}

export interface GhostPolicy {
  profile: PlayerProfile;
  threshold: number;
  decide(s: Situation): { action: Action; matched: SampleSource; distance: number } | null;
}

/** Zone ids in map-file order; the one-hot block follows this list. */
export const ZONE_IDS: string[] = loadZones().zones.map((z) => z.id);
const SPIKE_STATES = ['none', 'carried', 'dropped', 'planted'] as const;

/**
 * Feature weights, used as multipliers on squared differences:
 *
 * | feature                  | weight | why |
 * |--------------------------|--------|-----|
 * | enemyVisible             | 3.0    | seeing someone changes everything |
 * | enemyDist                | 3.0    | range drives the engagement |
 * | enemy bearing sin/cos    | 2.0 ea | direction without a wrap discontinuity |
 * | selfZone (one-hot)       | 2.5 ea | where you are is most of the tactic |
 * | hp                       | 3.0    | low hp flips holds into retreats |
 * | armor                    | 1.0    | a small tie-breaker |
 * | side                     | 1.5    | attack and defense play differently |
 * | spikeState (one-hot)     | 2.0 ea | objective context |
 * | roundTime                | 1.0    | early and late rounds differ |
 * | sinceSeen (0..10 s)      | 1.0    | stale information is weaker |
 * | tookDamageRecent         | 2.0    | recent contact changes intent |
 * | teammatesAlive           | 1.5    | numbers matter |
 * | enemiesAlive             | 1.5    | numbers matter |
 */
export const FEATURE_WEIGHTS: number[] = [
  3.0, // enemyVisible
  3.0, // enemyDist
  2.0, // enemy bearing sin
  2.0, // enemy bearing cos
  ...ZONE_IDS.map(() => 2.5),
  3.0, // hp
  1.0, // armor
  1.5, // side
  ...SPIKE_STATES.map(() => 2.0),
  1.0, // roundTime
  1.0, // sinceSeen
  2.0, // tookDamageRecent
  1.5, // teammatesAlive
  1.5, // enemiesAlive
];

export const FEATURE_NAMES: string[] = [
  'enemyVisible',
  'enemyDist',
  'enemyBearingSin',
  'enemyBearingCos',
  ...ZONE_IDS.map((id) => `zone:${id}`),
  'hp',
  'armor',
  'side',
  ...SPIKE_STATES.map((state) => `spike:${state}`),
  'roundTime',
  'sinceSeen',
  'tookDamageRecent',
  'teammatesAlive',
  'enemiesAlive',
];

const clamp01 = (v: number): number => Math.max(0, Math.min(1, v));

/** Situation -> weighted-feature vector, in `FEATURE_NAMES` order. */
export function featureVector(s: Situation): number[] {
  const out: number[] = [];
  out.push(s.enemyVisible ? 1 : 0);
  out.push(s.enemyRel ? clamp01(s.enemyRel.dist / 40) : 0);
  out.push(s.enemyRel ? Math.sin(s.enemyRel.bearing) : 0);
  out.push(s.enemyRel ? Math.cos(s.enemyRel.bearing) : 0);
  for (const zone of ZONE_IDS) out.push(s.selfZone === zone ? 1 : 0);
  out.push(clamp01(s.selfHp / 100));
  out.push(clamp01(s.selfArmor / 50));
  out.push(s.side === 'defense' ? 1 : 0);
  for (const state of SPIKE_STATES) out.push(s.spikeState === state ? 1 : 0);
  out.push(clamp01(s.roundTime / 100));
  out.push(s.sinceSeen === null ? 1 : clamp01(s.sinceSeen / 10));
  out.push(s.tookDamageRecent ? 1 : 0);
  out.push(clamp01(s.teammatesAlive / 5));
  out.push(clamp01(s.enemiesAlive / 5));
  return out;
}

export function weightedDistance(a: number[], b: number[]): number {
  let sum = 0;
  const n = Math.min(a.length, b.length, FEATURE_WEIGHTS.length);
  for (let i = 0; i < n; i++) {
    const d = a[i] - b[i];
    sum += FEATURE_WEIGHTS[i] * d * d;
  }
  return Math.sqrt(sum);
}

function roundKey(source: SampleSource): string {
  return `${source.matchId}|${source.round}`;
}

/**
 * 90th percentile of leave-one-ROUND-out nearest distances: each sample is
 * compared only against samples from a different (matchId, round), so the
 * near-duplicate frames next to it in its own round cannot collapse the
 * threshold. About a tenth of the training situations sit beyond it.
 * With only one round recorded we fall back to within-round nearest distances
 * between samples at least 2 s apart, so they are not consecutive near-copies.
 */
export function leaveOneOutThreshold(samples: PolicySample[], percentile = 0.9): number {
  const n = samples.length;
  if (n < 2) return 0;
  const nearest: number[] = [];
  for (let i = 0; i < n; i++) {
    const ownRound = roundKey(samples[i].source);
    let best = Number.POSITIVE_INFINITY;
    for (let j = 0; j < n; j++) {
      if (i === j) continue;
      if (roundKey(samples[j].source) === ownRound) continue;
      const d = weightedDistance(samples[i].features, samples[j].features);
      if (d < best) best = d;
    }
    if (!Number.isFinite(best)) {
      // Single-round fallback: same round, at least 2 s apart.
      for (let j = 0; j < n; j++) {
        if (i === j) continue;
        if (roundKey(samples[j].source) !== ownRound) continue;
        if (Math.abs(samples[i].source.t - samples[j].source.t) < 2) continue;
        const d = weightedDistance(samples[i].features, samples[j].features);
        if (d < best) best = d;
      }
    }
    if (Number.isFinite(best)) nearest.push(best);
  }
  if (nearest.length === 0) return 0;
  nearest.sort((a, b) => a - b);
  const index = Math.min(nearest.length - 1, Math.round(percentile * (nearest.length - 1)));
  return nearest[index];
}

/** Build an in-memory policy. `threshold` defaults to the LOO estimate. */
export function createPolicy(
  profile: PlayerProfile,
  samples: PolicySample[],
  threshold = leaveOneOutThreshold(samples),
): GhostPolicy {
  return {
    profile,
    threshold,
    decide(s: Situation) {
      if (samples.length === 0) return null;
      const features = featureVector(s);
      let best: PolicySample | null = null;
      let bestDistance = Number.POSITIVE_INFINITY;
      for (const sample of samples) {
        const distance = weightedDistance(features, sample.features);
        if (distance < bestDistance) {
          best = sample;
          bestDistance = distance;
        }
      }
      if (!best) return null;
      return {
        action: best.action,
        matched: { ...best.source },
        distance: bestDistance,
      };
    },
  };
}

/** `<repo>/profiles`, the default place `build.ts` writes. */
export const DEFAULT_PROFILE_DIR = fileURLToPath(new URL('../../profiles/', import.meta.url));

/**
 * The directory `loadPolicy` uses when no explicit one is given: the server's
 * `GHOST_PROFILES_DIR` when set, otherwise the repo's `profiles/`. Read at call
 * time so a bot spawned with a custom directory finds its ghost.
 */
function profilesDir(dir?: string): string {
  if (dir) return dir;
  const fromEnv = process.env.GHOST_PROFILES_DIR;
  if (fromEnv && fromEnv.trim()) return fromEnv;
  return DEFAULT_PROFILE_DIR;
}

interface StoredProfile extends PlayerProfile {
  threshold?: number;
}

/**
 * Read `profiles/<player>.json` and `profiles/<player>.samples.json`.
 * Returns null when either file is missing; malformed JSON throws.
 */
export function loadPolicy(player: string, dir?: string): GhostPolicy | null {
  const baseDir = profilesDir(dir);
  const profilePath = join(baseDir, `${player}.json`);
  const samplesPath = join(baseDir, `${player}.samples.json`);
  if (!existsSync(profilePath) || !existsSync(samplesPath)) return null;

  const profile = JSON.parse(readFileSync(profilePath, 'utf8')) as StoredProfile;
  const parsed = JSON.parse(readFileSync(samplesPath, 'utf8')) as unknown;
  if (!Array.isArray(parsed)) throw new Error(`loadPolicy: ${samplesPath} is not a JSON array`);
  const samples = parsed as PolicySample[];

  const threshold = typeof profile.threshold === 'number' ? profile.threshold : leaveOneOutThreshold(samples);
  return createPolicy(profile, samples, threshold);
}

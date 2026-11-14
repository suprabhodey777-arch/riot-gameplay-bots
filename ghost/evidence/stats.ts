/**
 * Statistics for the human evidence tests (guess-the-ghost, transfer test).
 *
 * The bootstrap is the same percentile bootstrap of the mean that
 * `evidence/run.ts` uses, with the same mulberry32 seeded PRNG as `sim/rng.ts`
 * and the same index rule (`floor(alpha * iters)` to
 * `ceil((1 - alpha) * iters) - 1`). Nothing here is a new method.
 *
 * `game/ghost-stats.js` mirrors these formulas for the CommonJS server routes;
 * `stats.test.ts` checks the two implementations agree on the same inputs.
 *
 * Empty samples never produce NaN: a 0-length sample gives `null`s, a
 * 1-element sample gives a zero-width CI equal to the value.
 */

export interface CI {
  /** Sample mean, or null for an empty sample. */
  mean: number | null;
  lo: number | null;
  hi: number | null;
}

export interface BubbleOptions {
  /** Number of bootstrap resamples. Default 2000. */
  iters?: number;
  /** PRNG seed. Default 7 (the seed `evidence/run.ts` uses). */
  seed?: number;
  /** Confidence level in (0, 1). Default 0.95. */
  level?: number;
}

export interface Summary {
  label: string;
  n: number;
  /** Sample mean, or null when n = 0. */
  mean: number | null;
  /** Percentile bootstrap CI of the mean, or null when n = 0. */
  ci: { lo: number; hi: number } | null;
  /** Reference value the mean is compared against, when one exists. */
  baseline: number | null;
}

/** mulberry32, exactly as `sim/rng.ts` (one uint32 of state). */
function createRng(seed: number): { next: () => number; int: (n: number) => number } {
  let a = seed >>> 0;
  const next = (): number => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  return { next, int: (n) => Math.floor(next() * n) };
}

/** Mean of a sample; 0 for an empty sample (the convention in `evidence/run.ts`). */
export function mean(values: readonly number[]): number {
  return values.length ? values.reduce((s, x) => s + x, 0) / values.length : 0;
}

interface Resolved {
  iters: number;
  seed: number;
  level: number;
}

function resolve(opts: BubbleOptions): Resolved {
  return {
    iters: Math.max(1, Math.floor(opts.iters ?? 2000)),
    seed: opts.seed ?? 7,
    level: Math.min(1, Math.max(0, opts.level ?? 0.95)),
  };
}

function percentileIndexes(iters: number, level: number): { lo: number; hi: number } {
  const alpha = (1 - level) / 2;
  return {
    lo: Math.floor(alpha * iters),
    hi: Math.min(iters - 1, Math.ceil((1 - alpha) * iters) - 1),
  };
}

/** Percentile bootstrap CI of the mean (deterministic for a given seed). */
export function bootstrapCI(values: readonly number[], opts: BubbleOptions = {}): CI {
  if (values.length === 0) return { mean: null, lo: null, hi: null };
  const { iters, seed, level } = resolve(opts);
  const rng = createRng(seed);
  const means = new Array<number>(iters);
  for (let b = 0; b < iters; b++) {
    let s = 0;
    for (let i = 0; i < values.length; i++) s += values[rng.int(values.length)];
    means[b] = s / values.length;
  }
  means.sort((x, y) => x - y);
  const { lo, hi } = percentileIndexes(iters, level);
  return { mean: mean(values), lo: means[lo], hi: means[hi] };
}

/**
 * Percentile bootstrap CI for the difference of two group means (unpaired).
 * Each resample draws independently from both groups with one PRNG stream.
 */
export function diffCI(a: readonly number[], b: readonly number[], opts: BubbleOptions = {}): CI {
  if (a.length === 0 || b.length === 0) return { mean: null, lo: null, hi: null };
  const { iters, seed, level } = resolve(opts);
  const rng = createRng(seed);
  const diffs = new Array<number>(iters);
  for (let k = 0; k < iters; k++) {
    let sa = 0;
    for (let i = 0; i < a.length; i++) sa += a[rng.int(a.length)];
    let sb = 0;
    for (let i = 0; i < b.length; i++) sb += b[rng.int(b.length)];
    diffs[k] = sa / a.length - sb / b.length;
  }
  diffs.sort((x, y) => x - y);
  const { lo, hi } = percentileIndexes(iters, level);
  return { mean: mean(a) - mean(b), lo: diffs[lo], hi: diffs[hi] };
}

const LANCZOS = [
  676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
  12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
];

/** log Gamma(x) for x > 0 (Lanczos, g = 7). */
function logGamma(x: number): number {
  if (x < 0.5) {
    // Reflection formula; the binomial argument is always >= 1, kept for completeness.
    return Math.log(Math.PI / Math.sin(Math.PI * x)) - logGamma(1 - x);
  }
  let z = x - 1;
  let acc = 0.99999999999980993;
  for (let i = 0; i < LANCZOS.length; i++) acc += LANCZOS[i] / (z + i + 1);
  const t = z + LANCZOS.length - 0.5;
  return 0.5 * Math.log(2 * Math.PI) + (z + 0.5) * Math.log(t) - t + Math.log(acc);
}

/**
 * One-sided exact binomial p-value P[X >= successes] for X ~ Binomial(n, p).
 * Returns 1 when n <= 0 (there is nothing to reject).
 */
export function binomialTestVsChance(successes: number, n: number, p: number): number {
  if (!Number.isFinite(n) || n <= 0) return 1;
  const N = Math.floor(n);
  if (!Number.isFinite(successes)) return successes > 0 ? 0 : 1;
  const k = Math.ceil(successes);
  if (k > N) return 0;
  if (p <= 0) return k <= 0 ? 1 : 0;
  if (p >= 1) return 1;
  const lnP = Math.log(p);
  const lnQ = Math.log1p(-p);
  let tail = 0;
  for (let i = Math.max(0, k); i <= N; i++) {
    const lnChoose = logGamma(N + 1) - logGamma(i + 1) - logGamma(N - i + 1);
    tail += Math.exp(lnChoose + i * lnP + (N - i) * lnQ);
  }
  return Math.min(1, Math.max(0, tail));
}

/** Mean with its CI, sample size, and an optional baseline the mean is judged against. */
export function summarize(label: string, values: readonly number[], baseline?: number | null): Summary {
  const n = values.length;
  if (n === 0) return { label, n: 0, mean: null, ci: null, baseline: baseline ?? null };
  if (n === 1) {
    const value = values[0];
    return { label, n: 1, mean: value, ci: { lo: value, hi: value }, baseline: baseline ?? null };
  }
  const ci = bootstrapCI(values);
  // n >= 2, so the CI fields are numbers; the nulls are unreachable here.
  const lo = ci.lo as number;
  const hi = ci.hi as number;
  return { label, n, mean: ci.mean as number, ci: { lo, hi }, baseline: baseline ?? null };
}

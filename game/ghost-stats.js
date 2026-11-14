/**
 * CommonJS mirror of the formulas in `ghost/evidence/stats.ts`.
 *
 * The server routes in `game/ghost-routes.js` are plain CommonJS, so they cannot
 * import the TypeScript module directly. This file holds the same formulas and
 * `ghost/evidence/stats.test.ts` checks both implementations agree on the same
 * inputs. Keep the two files in step.
 *
 * Same method as `evidence/run.ts`: percentile bootstrap of the mean with the
 * mulberry32 PRNG from `sim/rng.ts`.
 */

'use strict';

/** mulberry32, exactly as `sim/rng.ts`. */
function createRng(seed) {
  let a = seed >>> 0;
  const next = () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  return { next, int: (n) => Math.floor(next() * n) };
}

/** Mean of a sample; 0 for an empty sample (the convention in `evidence/run.ts`). */
function mean(values) {
  return values.length ? values.reduce((s, x) => s + x, 0) / values.length : 0;
}

function resolve(opts = {}) {
  return {
    iters: Math.max(1, Math.floor(opts.iters === undefined ? 2000 : opts.iters)),
    seed: opts.seed === undefined ? 7 : opts.seed,
    level: Math.min(1, Math.max(0, opts.level === undefined ? 0.95 : opts.level)),
  };
}

function percentileIndexes(iters, level) {
  const alpha = (1 - level) / 2;
  return {
    lo: Math.floor(alpha * iters),
    hi: Math.min(iters - 1, Math.ceil((1 - alpha) * iters) - 1),
  };
}

/** Percentile bootstrap CI of the mean (deterministic for a given seed). */
function bootstrapCI(values, opts = {}) {
  if (values.length === 0) return { mean: null, lo: null, hi: null };
  const { iters, seed, level } = resolve(opts);
  const rng = createRng(seed);
  const means = new Array(iters);
  for (let b = 0; b < iters; b++) {
    let s = 0;
    for (let i = 0; i < values.length; i++) s += values[rng.int(values.length)];
    means[b] = s / values.length;
  }
  means.sort((x, y) => x - y);
  const { lo, hi } = percentileIndexes(iters, level);
  return { mean: mean(values), lo: means[lo], hi: means[hi] };
}

/** Percentile bootstrap CI for the difference of two group means (unpaired). */
function diffCI(a, b, opts = {}) {
  if (a.length === 0 || b.length === 0) return { mean: null, lo: null, hi: null };
  const { iters, seed, level } = resolve(opts);
  const rng = createRng(seed);
  const diffs = new Array(iters);
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
function logGamma(x) {
  if (x < 0.5) return Math.log(Math.PI / Math.sin(Math.PI * x)) - logGamma(1 - x);
  const z = x - 1;
  let acc = 0.99999999999980993;
  for (let i = 0; i < LANCZOS.length; i++) acc += LANCZOS[i] / (z + i + 1);
  const t = z + LANCZOS.length - 0.5;
  return 0.5 * Math.log(2 * Math.PI) + (z + 0.5) * Math.log(t) - t + Math.log(acc);
}

/** One-sided exact binomial p-value P[X >= successes] for X ~ Binomial(n, p). */
function binomialTestVsChance(successes, n, p) {
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

/** Mean with its CI, sample size, and an optional baseline. */
function summarize(label, values, baseline) {
  const n = values.length;
  const base = baseline === undefined ? null : baseline;
  if (n === 0) return { label, n: 0, mean: null, ci: null, baseline: base };
  if (n === 1) {
    const value = values[0];
    return { label, n: 1, mean: value, ci: { lo: value, hi: value }, baseline: base };
  }
  const ci = bootstrapCI(values);
  return { label, n, mean: ci.mean, ci: { lo: ci.lo, hi: ci.hi }, baseline: base };
}

module.exports = {
  mean,
  bootstrapCI,
  diffCI,
  binomialTestVsChance,
  summarize,
};

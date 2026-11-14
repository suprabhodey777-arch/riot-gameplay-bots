import { createRequire } from 'node:module';
import { describe, expect, test } from 'vitest';
import {
  binomialTestVsChance,
  bootstrapCI,
  diffCI,
  mean,
  summarize,
} from './stats.ts';

interface JsStats {
  mean(values: number[]): number;
  bootstrapCI(values: number[], opts?: { iters?: number; seed?: number; level?: number }): {
    mean: number | null;
    lo: number | null;
    hi: number | null;
  };
  diffCI(
    a: number[],
    b: number[],
    opts?: { iters?: number; seed?: number; level?: number },
  ): { mean: number | null; lo: number | null; hi: number | null };
  binomialTestVsChance(successes: number, n: number, p: number): number;
  summarize(label: string, values: number[], baseline?: number | null): {
    label: string;
    n: number;
    mean: number | null;
    ci: { lo: number; hi: number } | null;
    baseline: number | null;
  };
}

const requireJs = createRequire(import.meta.url);
const js = requireJs('../../game/ghost-stats.js') as JsStats;

describe('mean', () => {
  test('known values', () => {
    expect(mean([1, 2, 3, 4])).toBe(2.5);
    expect(mean([2])).toBe(2);
    expect(mean([-1, 1])).toBe(0);
  });

  test('empty sample is 0, not NaN', () => {
    expect(mean([])).toBe(0);
    expect(Number.isNaN(mean([]))).toBe(false);
  });
});

describe('bootstrapCI', () => {
  test('a constant sample has a zero-width CI equal to the value', () => {
    expect(bootstrapCI([5, 5, 5], { iters: 200, seed: 3 })).toEqual({ mean: 5, lo: 5, hi: 5 });
  });

  test('n = 1 gives a CI equal to the value', () => {
    expect(bootstrapCI([2.5], { iters: 50 })).toEqual({ mean: 2.5, lo: 2.5, hi: 2.5 });
  });

  test('n = 0 returns nulls, never NaN', () => {
    const ci = bootstrapCI([]);
    expect(ci).toEqual({ mean: null, lo: null, hi: null });
    for (const v of Object.values(ci)) expect(v).not.toBeNaN();
  });

  test('is deterministic for a given seed and ordered', () => {
    const values = [1, 2, 3, 4, 5, 6, 7, 8];
    const a = bootstrapCI(values, { iters: 500, seed: 11 });
    const b = bootstrapCI(values, { iters: 500, seed: 11 });
    expect(a).toEqual(b);
    expect(a.lo).not.toBeNull();
    expect(a.hi).not.toBeNull();
    expect(a.lo! <= a.mean!).toBe(true);
    expect(a.mean! <= a.hi!).toBe(true);
  });

  test('a wider level does not narrow the 90% CI', () => {
    const values = [0.1, 0.4, 0.35, 0.8, 0.5, 0.2, 0.9, 0.6];
    const narrow = bootstrapCI(values, { iters: 1000, seed: 5, level: 0.8 });
    const wide = bootstrapCI(values, { iters: 1000, seed: 5, level: 0.95 });
    expect(wide.lo!).toBeLessThanOrEqual(narrow.lo!);
    expect(wide.hi!).toBeGreaterThanOrEqual(narrow.hi!);
  });
});

describe('diffCI', () => {
  test('constant groups give an exact difference', () => {
    expect(diffCI([2, 2, 2], [1, 1, 1], { iters: 100, seed: 2 })).toEqual({ mean: 1, lo: 1, hi: 1 });
  });

  test('single-value groups have a zero-width CI', () => {
    expect(diffCI([5], [2], { iters: 100 })).toEqual({ mean: 3, lo: 3, hi: 3 });
  });

  test('an empty group gives nulls', () => {
    expect(diffCI([], [1, 2])).toEqual({ mean: null, lo: null, hi: null });
    expect(diffCI([1, 2], [])).toEqual({ mean: null, lo: null, hi: null });
  });

  test('brackets the observed difference', () => {
    const a = [0.6, 0.7, 0.8, 0.9, 0.5];
    const b = [0.2, 0.4, 0.3, 0.1, 0.5];
    const d = diffCI(a, b, { iters: 1000, seed: 9 });
    expect(d.lo!).toBeLessThanOrEqual(d.mean!);
    expect(d.hi!).toBeGreaterThanOrEqual(d.mean!);
  });
});

describe('binomialTestVsChance', () => {
  test('10 of 10 at p = 0.5 is 1/1024', () => {
    expect(binomialTestVsChance(10, 10, 0.5)).toBeCloseTo(1 / 1024, 15);
  });

  test('known values', () => {
    expect(binomialTestVsChance(0, 10, 0.5)).toBe(1);
    expect(binomialTestVsChance(5, 10, 0.5)).toBeCloseTo(0.623046875, 12);
    expect(binomialTestVsChance(3, 5, 0.25)).toBeCloseTo(0.103515625, 12);
    expect(binomialTestVsChance(0, 0, 0.5)).toBe(1);
  });

  test('edge p and impossible counts', () => {
    expect(binomialTestVsChance(1, 5, 0)).toBe(0);
    expect(binomialTestVsChance(0, 5, 0)).toBe(1);
    expect(binomialTestVsChance(5, 5, 1)).toBe(1);
    expect(binomialTestVsChance(6, 5, 0.5)).toBe(0);
  });
});

describe('summarize', () => {
  test('keeps the baseline and sample size beside the mean', () => {
    const s = summarize('ghost', [1, 0.5, 0.75], 0.5);
    expect(s.label).toBe('ghost');
    expect(s.n).toBe(3);
    expect(s.mean).toBeCloseTo(0.75, 12);
    expect(s.baseline).toBe(0.5);
    expect(s.ci!.lo).toBeLessThanOrEqual(s.ci!.hi);
  });

  test('n = 0 is n 0 with nulls', () => {
    const s = summarize('default', []);
    expect(s).toEqual({ label: 'default', n: 0, mean: null, ci: null, baseline: null });
  });

  test('n = 1 has a zero-width CI equal to the value', () => {
    const s = summarize('x', [0.4], 0.25);
    expect(s).toEqual({ label: 'x', n: 1, mean: 0.4, ci: { lo: 0.4, hi: 0.4 }, baseline: 0.25 });
  });
});

describe('game/ghost-stats.js agrees with ghost/evidence/stats.ts', () => {
  const samples: number[][] = [
    [],
    [0.5],
    [1, 1, 1],
    [0.1, 0.2, 0.3, 0.4],
    [0, 1, 0, 1, 1, 0, 1],
    [12, 7.5, 9.25, 3, 18, 11],
  ];
  const optionSets = [
    undefined,
    { iters: 1, seed: 0 },
    { iters: 50, seed: 3 },
    { iters: 200, seed: 17, level: 0.9 },
    { iters: 300, seed: 1, level: 0.99 },
  ];

  test('mean', () => {
    for (const values of samples) expect(js.mean(values)).toBe(mean(values));
  });

  test('bootstrapCI', () => {
    for (const values of samples) {
      for (const opts of optionSets) expect(js.bootstrapCI(values, opts)).toEqual(bootstrapCI(values, opts));
    }
  });

  test('diffCI', () => {
    const pairs: [number[], number[]][] = [
      [[], []],
      [[0.5], []],
      [[1, 0, 1], [0, 0, 1]],
      [[0.1, 0.2, 0.3], [0.3, 0.2, 0.1]],
      [[1, 0.5, 0.75, 0.25], [0.5, 0, 1, 0.25]],
    ];
    for (const [a, b] of pairs)
      for (const opts of optionSets) expect(js.diffCI(a, b, opts)).toEqual(diffCI(a, b, opts));
  });

  test('binomialTestVsChance', () => {
    for (const n of [0, 1, 5, 10, 25]) {
      for (const successes of [0, 1, Math.floor(n / 2), n, n + 1]) {
        for (const p of [0, 0.25, 0.5, 0.75, 1]) {
          expect(js.binomialTestVsChance(successes, n, p)).toBeCloseTo(
            binomialTestVsChance(successes, n, p),
            12,
          );
        }
      }
    }
  });

  test('summarize', () => {
    for (const values of samples) {
      for (const baseline of [undefined, null, 0.5]) {
        expect(js.summarize('x', values, baseline)).toEqual(summarize('x', values, baseline));
      }
    }
  });
});

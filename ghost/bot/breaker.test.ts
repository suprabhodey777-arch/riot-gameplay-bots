import { describe, expect, it } from 'vitest';

import { createBreaker } from './breaker.ts';

describe('circuit breaker', () => {
  it('falls back when the nearest recording is beyond the threshold', () => {
    const breaker = createBreaker();
    expect(breaker.choose(0, { distance: 0.2, threshold: 0.5 })).toEqual({ control: 'ghost', fallbackReason: null });
    expect(breaker.choose(50, { distance: 0.9, threshold: 0.5 })).toEqual({
      control: 'default',
      fallbackReason: 'no-close-recording',
    });
    expect(breaker.choose(100, null)).toEqual({ control: 'default', fallbackReason: 'no-close-recording' });
  });

  it('returns control to the ghost after 3 consecutive in-range decisions', () => {
    const breaker = createBreaker();
    breaker.choose(0, { distance: 0.9, threshold: 0.5 });
    expect(breaker.choose(50, { distance: 0.1, threshold: 0.5 }).control).toBe('default');
    expect(breaker.choose(100, { distance: 0.1, threshold: 0.5 }).control).toBe('default');
    expect(breaker.choose(150, { distance: 0.1, threshold: 0.5 })).toEqual({ control: 'ghost', fallbackReason: null });
  });

  it('flags a bot that tries to move but does not', () => {
    const breaker = createBreaker();
    let tripped: string | null = null;
    for (let now = 0; now <= 5000; now += 50) {
      const reason = breaker.sample(now, true, { x: 0, z: 0 });
      if (reason) tripped = reason;
    }
    expect(tripped).toBe('stuck');
    // The penalty keeps the default bot in control with the stuck reason.
    expect(breaker.choose(5050, { distance: 0.1, threshold: 0.5 })).toEqual({
      control: 'default',
      fallbackReason: 'stuck',
    });
    // After the 5 s penalty, the ghost needs its 3 in-range decisions again.
    expect(breaker.choose(10000, { distance: 0.1, threshold: 0.5 }).control).toBe('default');
    expect(breaker.choose(10050, { distance: 0.1, threshold: 0.5 }).control).toBe('default');
    expect(breaker.choose(10100, { distance: 0.1, threshold: 0.5 }).control).toBe('ghost');
  });

  it('does not flag a bot that is actually moving', () => {
    const breaker = createBreaker();
    let tripped: string | null = null;
    for (let i = 0; i <= 100; i += 1) {
      const reason = breaker.sample(i * 50, true, { x: i * 0.1, z: 0 });
      if (reason) tripped = reason;
    }
    expect(tripped).toBeNull();
  });

  it('flags action kinds that flip-flop more than 6 times in 3 s', () => {
    const breaker = createBreaker();
    let tripped: string | null = null;
    for (let i = 0; i < 10; i += 1) {
      const reason = breaker.action(i * 100, i % 2 === 0 ? 'hold' : 'fight');
      if (reason) tripped = reason;
    }
    expect(tripped).toBe('degenerate');
    expect(breaker.choose(1000, { distance: 0.1, threshold: 0.5 })).toEqual({
      control: 'default',
      fallbackReason: 'degenerate',
    });
  });
});

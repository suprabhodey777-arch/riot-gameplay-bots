/**
 * The circuit breaker between the cloned ghost and the default bot. The ghost is
 * in control while its nearest recording is close enough; otherwise the default
 * bot plays with `fallbackReason: 'no-close-recording'`. A stuck or degenerate
 * bot is demoted to the default for five seconds, and the ghost only gets
 * control back after three consecutive in-range decisions.
 *
 * The stuck/degenerate checks are the ideas from `bot/health.ts` (sliding
 * windows over the bot's own outputs), retuned to this runtime's 20 Hz ticks.
 */
import type { ActionKind, Vec2 } from '../contracts.ts';

export type FallbackReason = 'no-close-recording' | 'stuck' | 'degenerate';

export interface BreakerConfig {
  /** In-range decisions needed to hand control back to the ghost. */
  returnAfter: number;
  stuckWindowMs: number;
  /** Moved less than this over the whole window while trying to move. */
  stuckDistance: number;
  /** Share of samples in the window that must have been trying to move. */
  stuckMoveShare: number;
  degenerateWindowMs: number;
  /** More kind changes than this many inside the window is degenerate. */
  degenerateFlips: number;
  /** How long a stuck/degenerate penalty keeps the default bot in control. */
  penaltyMs: number;
}

export const BREAKER_DEFAULTS: BreakerConfig = {
  returnAfter: 3,
  stuckWindowMs: 5000,
  stuckDistance: 0.25,
  stuckMoveShare: 0.9,
  degenerateWindowMs: 3000,
  degenerateFlips: 6,
  penaltyMs: 5000,
};

export interface PolicyGateResult {
  distance: number;
  threshold: number;
}

export interface BreakerVerdict {
  control: 'ghost' | 'default';
  fallbackReason: FallbackReason | null;
}

export interface Breaker {
  /** Call once per decision, with the policy's nearest match or null when it had none. */
  choose(nowMs: number, result: PolicyGateResult | null): BreakerVerdict;
  /** Call once per executed tick. Returns the reason when it just tripped. */
  sample(nowMs: number, tryingToMove: boolean, pos: Vec2): FallbackReason | null;
  /** Call once per executed action kind. Returns the reason when it just tripped. */
  action(nowMs: number, kind: ActionKind): FallbackReason | null;
  reset(): void;
}

interface MoveSample {
  t: number;
  moving: boolean;
  x: number;
  z: number;
}

interface KindSample {
  t: number;
  kind: ActionKind;
}

export function createBreaker(config: Partial<BreakerConfig> = {}): Breaker {
  const cfg: BreakerConfig = { ...BREAKER_DEFAULTS, ...config };
  let control: 'ghost' | 'default' = 'ghost';
  let inRange = 0;
  let penaltyUntil: number | null = null;
  let penaltyReason: FallbackReason | null = null;
  let moves: MoveSample[] = [];
  let kinds: KindSample[] = [];

  function forced(nowMs: number): boolean {
    if (penaltyUntil == null) return false;
    if (nowMs < penaltyUntil) return true;
    penaltyUntil = null;
    penaltyReason = null;
    return false;
  }

  function force(nowMs: number, reason: FallbackReason): void {
    control = 'default';
    inRange = 0;
    penaltyUntil = nowMs + cfg.penaltyMs;
    penaltyReason = reason;
    moves = [];
    kinds = [];
  }

  return {
    choose(nowMs, result) {
      if (forced(nowMs)) {
        return { control: 'default', fallbackReason: penaltyReason };
      }
      if (!result || !Number.isFinite(result.distance) || result.distance > result.threshold) {
        control = 'default';
        inRange = 0;
        return { control: 'default', fallbackReason: 'no-close-recording' };
      }
      inRange += 1;
      if (control === 'ghost') return { control: 'ghost', fallbackReason: null };
      if (inRange >= cfg.returnAfter) {
        control = 'ghost';
        inRange = 0;
        return { control: 'ghost', fallbackReason: null };
      }
      return { control: 'default', fallbackReason: 'no-close-recording' };
    },

    sample(nowMs, tryingToMove, pos) {
      moves.push({ t: nowMs, moving: tryingToMove, x: pos.x, z: pos.z });
      while (moves.length > 0 && moves[0].t < nowMs - cfg.stuckWindowMs) moves.shift();
      if (forced(nowMs) || moves.length < 2) return null;
      const span = nowMs - moves[0].t;
      // One tick of slack so a window sampled exactly every 50 ms still fills.
      if (span < cfg.stuckWindowMs - 50) return null;
      const moving = moves.filter((m) => m.moving).length / moves.length;
      if (moving < cfg.stuckMoveShare) return null;
      const last = moves[moves.length - 1];
      const moved = Math.hypot(last.x - moves[0].x, last.z - moves[0].z);
      if (moved >= cfg.stuckDistance) return null;
      force(nowMs, 'stuck');
      return 'stuck';
    },

    action(nowMs, kind) {
      kinds.push({ t: nowMs, kind });
      while (kinds.length > 0 && kinds[0].t < nowMs - cfg.degenerateWindowMs) kinds.shift();
      if (forced(nowMs) || kinds.length < 2) return null;
      let flips = 0;
      for (let i = 1; i < kinds.length; i++) if (kinds[i].kind !== kinds[i - 1].kind) flips += 1;
      if (flips <= cfg.degenerateFlips) return null;
      force(nowMs, 'degenerate');
      return 'degenerate';
    },

    reset() {
      control = 'ghost';
      inRange = 0;
      penaltyUntil = null;
      penaltyReason = null;
      moves = [];
      kinds = [];
    },
  };
}

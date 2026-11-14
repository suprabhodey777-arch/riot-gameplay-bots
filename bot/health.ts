import type { PlayerInput, Vec2 } from '../shared/contract.ts';

export interface HealthConfig {
  /** Ticks of idling with a repeated node that trips the entropy check (default 60 = 3 s). */
  entropyWindow: number;
  entropyRatio: number;
  /** Ticks without moving that trip the stuck check (default 100 = 5 s). */
  stuckWindow: number;
  stuckDistance: number;
}

export const DEFAULT_HEALTH: HealthConfig = { entropyWindow: 60, entropyRatio: 0.9, stuckWindow: 100, stuckDistance: 0.25 };

export interface Sample {
  tick: number;
  node: string;
  /** Standing still is intended (holding an angle, planting, defusing, reacting). */
  holding: boolean;
  input: PlayerInput;
  pos: Vec2;
}

export interface HealthVerdict {
  tripped: boolean;
  reason?: string;
}

/** Watches one bot's own outputs over a sliding window. */
export function createHealth(cfg: HealthConfig = DEFAULT_HEALTH) {
  let samples: Sample[] = [];
  const keep = Math.max(cfg.entropyWindow, cfg.stuckWindow);

  function observe(s: Sample): HealthVerdict {
    const { move, yaw } = s.input;
    if (!Number.isFinite(move.x) || !Number.isFinite(move.y) || !Number.isFinite(yaw)) {
      return { tripped: true, reason: 'invalid input: NaN in move or yaw' };
    }
    const len = Math.hypot(move.x, move.y);
    if (len > 1 + 1e-6) return { tripped: true, reason: `invalid input: move length ${len.toFixed(2)} is over 1` };
    samples.push(s);
    if (samples.length > keep) samples.shift();

    if (samples.length >= cfg.entropyWindow) {
      const w = samples.slice(-cfg.entropyWindow);
      const counts = new Map<string, number>();
      for (const x of w) {
        if (!x.holding && Math.hypot(x.input.move.x, x.input.move.y) < 1e-3) counts.set(x.node, (counts.get(x.node) ?? 0) + 1);
      }
      for (const [node, n] of counts) {
        if (n / w.length >= cfg.entropyRatio) {
          return { tripped: true, reason: `low action entropy: ${Math.round((n / w.length) * 100)}% of the last ${(w.length / 20).toFixed(0)} s on '${node}' with zero movement` };
        }
      }
    }
    if (samples.length >= cfg.stuckWindow) {
      const w = samples.slice(-cfg.stuckWindow);
      const moving = w.filter((x) => Math.hypot(x.input.move.x, x.input.move.y) >= 1e-3 && !x.holding).length;
      const moved = Math.hypot(w[w.length - 1].pos.x - w[0].pos.x, w[w.length - 1].pos.y - w[0].pos.y);
      if (moving / w.length >= 0.9 && moved < cfg.stuckDistance) {
        return { tripped: true, reason: `stuck: tried to move for ${(w.length / 20).toFixed(0)} s but travelled ${moved.toFixed(2)} m` };
      }
    }
    return { tripped: false };
  }

  return { observe, reset: () => (samples = []) };
}

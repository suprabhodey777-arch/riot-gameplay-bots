/**
 * Trace plumbing for the bot: a `TraceEvent` (contract 5) on every action change
 * and at least every 500 ms, plus the small gate that decides when to send one.
 */
import type { Action, Situation, TraceEvent } from '../contracts.ts';

export interface TraceInput {
  bot: string;
  ghostOf: string | null;
  round: number;
  t: number;
  action: Action;
  reason: string;
  matched: TraceEvent['matched'];
  matchDistance: number | null;
  fallback: boolean;
  fallbackReason: TraceEvent['fallbackReason'];
  skillScale: number;
  situation: Situation;
}

export function makeTrace(input: TraceInput): TraceEvent {
  return {
    bot: input.bot,
    ghostOf: input.ghostOf,
    round: input.round,
    t: input.t,
    action: input.action,
    reason: input.reason,
    matched: input.matched,
    matchDistance: input.matchDistance,
    fallback: input.fallback,
    fallbackReason: input.fallbackReason,
    skillScale: input.skillScale,
    situation: input.situation,
  };
}

export interface TraceGate {
  /** True when the action changed since the last trace or the gap reached `maxGapMs`. */
  due(nowMs: number, key: string): boolean;
  /** Record that a trace for `key` was sent at `nowMs`. */
  mark(nowMs: number, key: string): void;
  reset(): void;
}

/** One trace per action change, and at least one every `maxGapMs` (default 500 ms). */
export function createTraceGate(maxGapMs = 500): TraceGate {
  let lastKey: string | null = null;
  let sentAt = -Infinity;
  return {
    due(nowMs, key) {
      return key !== lastKey || nowMs - sentAt >= maxGapMs;
    },
    mark(nowMs, key) {
      lastKey = key;
      sentAt = nowMs;
    },
    reset() {
      lastKey = null;
      sentAt = -Infinity;
    },
  };
}

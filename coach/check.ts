/**
 * The guard against invented figures: every number in the model's text must
 * already be a number in the facts. Also rejects empty or overlong text.
 */
import { extractNumbers, type Fact } from './facts.ts';

export const MAX_WORDS = 120;

export type CheckReason = 'empty' | 'too_long' | 'unsupported_number';

export interface CheckResult {
  ok: boolean;
  reason?: CheckReason;
  /** The numbers that were not in the facts. */
  offending: number[];
  words: number;
}

export function wordCount(text: string): number {
  return text.split(/\s+/).filter(Boolean).length;
}

export function checkText(text: string, facts: Fact[]): CheckResult {
  const trimmed = text.trim();
  const words = wordCount(trimmed);
  if (words === 0) return { ok: false, reason: 'empty', offending: [], words };
  if (words > MAX_WORDS) return { ok: false, reason: 'too_long', offending: [], words };
  const allowed = new Set<number>();
  const entities: string[] = [];
  for (const f of facts) {
    for (const n of f.numbers) allowed.add(n);
    entities.push(...f.entities);
  }
  const offending = [...new Set(extractNumbers(trimmed, entities).filter((n) => !allowed.has(n)))];
  if (offending.length > 0) return { ok: false, reason: 'unsupported_number', offending, words };
  return { ok: true, offending: [], words };
}

/** True when the text is non-empty, at most 120 words, and cites only numbers from the facts. */
export function supported(text: string, facts: Fact[]): boolean {
  return checkText(text, facts).ok;
}

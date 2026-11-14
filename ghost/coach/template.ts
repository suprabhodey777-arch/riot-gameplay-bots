/**
 * The deterministic ghost debrief: built from the facts alone, so it can never
 * state a number the facts do not hold. It is what the player sees whenever
 * the model is missing, slow or wrong.
 */
import type { DrillFacts } from './facts.ts';

export const MAX_DEBRIEF_WORDS = 90;

const cap = (s: string): string => (s ? s[0].toUpperCase() + s.slice(1) : s);

/** One thing to try next, chosen from the facts. No numbers, on purpose. */
function nextTip(facts: DrillFacts): string {
  const loss = facts.perAttempt.find((a) => !a.won);
  if (!loss) return 'Try to keep the first shot and the same exit on the next run.';
  if (loss.fallbackTraces > 0) {
    return 'Try to punish the fallback: when the ghost has no close recording it walks the same route, so hold that angle first.';
  }
  if (loss.firstShotBy === 'ghost') {
    return 'Try to take the first shot yourself: peek from cover instead of letting the ghost fire first.';
  }
  return 'Try a different approach: enter from another zone and take the first fight from cover.';
}

/**
 * Two or three sentences: the outcome, the first lost attempt, then either the
 * first winning attempt or the next thing to try.
 */
export function templateDrillDebrief(facts: DrillFacts): string {
  if (facts.attempts === 0) return 'There is no drill data to read yet.';
  const text = (id: string): string | undefined =>
    facts.sentences.find((f) => f.id === id)?.text;
  const outcome = text('outcome') ?? 'This moment was replayed.';
  const parts: string[] = [cap(outcome)];

  const lossIndex = facts.perAttempt.findIndex((a) => !a.won);
  if (lossIndex >= 0) {
    const loss = text(`attempt-${lossIndex + 1}`);
    if (loss) parts.push(cap(loss));
  }
  if (facts.firstWinIndex !== null) {
    const win = text(`attempt-${facts.firstWinIndex + 1}`);
    if (win) parts.push(cap(win));
  } else {
    parts.push(cap(nextTip(facts)));
  }
  return parts.join(' ');
}

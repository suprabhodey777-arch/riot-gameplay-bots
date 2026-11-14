/**
 * The deterministic debrief: built from the facts alone, so it can never state
 * a number the facts do not hold. It is what the player sees whenever the
 * model is missing, slow or wrong.
 */
import type { Skill } from '../shared/coaching.ts';
import type { Fact } from './facts.ts';

export const MAX_TEMPLATE_WORDS = 90;

/** The one thing to change, per skill. No numbers on purpose. */
export const TIPS: Record<Skill, string> = {
  entry:
    'Do not swing into the site alone: wait for a teammate or a smoke, and take a short peek to check the angle first.',
  clutch:
    'Slow down when you are outnumbered: take one fight at a time from cover instead of pushing the group.',
  time_pressure:
    'Decide on a site early and start your plant or defuse before the clock gets short.',
  smoke_usage: 'Throw your smoke before you move through the choke, not after you are spotted.',
};

const GOALS: Record<Skill, string> = {
  entry: 'open the site and win the first duel without dying first.',
  clutch: 'win the round from behind by taking the fights one at a time.',
  time_pressure: 'finish the plant or defuse with time to spare.',
  smoke_usage: 'place the smoke early enough that it blocks the angle before contact.',
};

const words = (s: string): number => s.split(/\s+/).filter(Boolean).length;
const cap = (s: string): string => (s ? s[0].toUpperCase() + s.slice(1) : s);

function skillOf(facts: Fact[], fallback?: Skill): Skill | undefined {
  return facts.find((f) => f.skill)?.skill ?? fallback;
}

export function templateDebrief(facts: Fact[], skillHint?: Skill): string {
  const skill = skillOf(facts, skillHint);
  const outcome = facts.find((f) => f.id === 'outcome');
  const weakness = facts.find((f) => f.kind === 'weakness');
  const detail =
    facts.find((f) => f.id === 'died-first') ??
    facts.find((f) => f.id === 'plant') ??
    facts.find((f) => f.id === 'defuse') ??
    facts.find((f) => f.id === 'kill-1') ??
    facts.find((f) => f.id === 'first-contact');
  const exam = facts.find((f) => f.id === 'exam');

  const happened: string[] = [];
  if (outcome) happened.push(cap(outcome.text));
  else if (weakness) happened.push(cap(weakness.text));
  if (detail) happened.push(cap(detail.text));
  if (happened.length === 0) happened.push('There is no drill data to read yet.');

  const change = skill ? `Change one thing: ${TIPS[skill]}` : 'Change one thing: play the next drill slowly and watch where you lose the fight.';
  const next = skill ? `Next drill: ${GOALS[skill]}` : 'Next drill: replay the same situation and try a different approach.';
  const examLine = exam ? cap(exam.text) : '';

  // Drop the optional parts, least useful first, until it fits.
  const attempts = [
    [...happened, examLine, change, next],
    [...happened, change, next],
    [happened[0], change, next],
  ].map((parts) => parts.filter(Boolean).join(' '));
  for (const text of attempts) if (words(text) <= MAX_TEMPLATE_WORDS) return text;
  return attempts[attempts.length - 1].split(/\s+/).slice(0, MAX_TEMPLATE_WORDS).join(' ');
}

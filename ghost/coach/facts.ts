/**
 * Facts for a Ghost Rematch drill: the only material the debrief may use.
 * Each fact is one plain sentence plus the numbers in it, so the template can
 * read them directly and the checker can reject any number the facts lack.
 *
 * The shapes come from `ghost/contracts.ts`: a drill is a list of attempts,
 * each attempt a slice of the match log plus the ghost's traces.
 */
import type { ActionKind, LogEvent, TraceEvent, Vec2 } from '../contracts.ts';
import { extractNumbers, type Fact, type FactKind } from '../../coach/facts.ts';

export interface DrillAttemptInput {
  won: boolean;
  /** Wall clock length of the attempt, in seconds. */
  durationS: number;
  events: LogEvent[];
  traces: TraceEvent[];
}

export interface DrillInput {
  player: string;
  ghostOf: string;
  scenarioId: string;
  attempts: DrillAttemptInput[];
}

/** One ghost decision, kept as the kind plus the time it happened. */
export interface GhostActionFact {
  kind: ActionKind;
  t: number;
}

export interface AttemptFacts {
  /** 0-based index in `DrillInput.attempts`. */
  index: number;
  won: boolean;
  durationS: number;
  /** Who fired first, from the tracer events; null when nobody fired. */
  firstShotBy: 'player' | 'ghost' | null;
  /** Consecutive duplicate kinds collapsed; times in seconds. */
  ghostActions: GhostActionFact[];
  /** Where the player died, or where they were when they won. */
  endPos: Vec2 | null;
  damageDealt: number;
  damageTaken: number;
  headshots: number;
  /** True when any trace of the attempt was in fallback. */
  fallback: boolean;
  fallbackTraces: number;
  traces: number;
  /** Seconds from the ghost's first trace with the enemy visible to its first shot. */
  reactionS: number | null;
}

export interface DrillFacts {
  scenarioId: string;
  attempts: number;
  wins: number;
  /** 0-based index of the first winning attempt, or null when never won. */
  firstWinIndex: number | null;
  perAttempt: AttemptFacts[];
  /** The plain sentences the checker matches, in reading order. */
  sentences: Fact[];
}

/** Readable labels for an action kind, used inside the sentences. */
export const ACTION_LABEL: Record<ActionKind, string> = {
  hold: 'holding',
  peek_wide: 'a wide peek',
  peek_jiggle: 'a jiggle peek',
  fight: 'fighting',
  push: 'pushing',
  retreat: 'retreating',
  rotate: 'rotating',
  use_ability: 'using utility',
  plant: 'planting',
  defuse: 'defusing',
  buy: 'buying',
};

const secs = (t: number): number => Math.round(t * 10) / 10;
const dmg = (n: number): number => Math.round(n * 10) / 10;
const metres = (n: number): number => Math.round(n);
const plural = (n: number, one: string, many: string): string => (n === 1 ? one : many);

/** The bot that played the ghost, taken from its own traces. */
function ghostNameOf(input: DrillInput, traces: TraceEvent[]): string | null {
  const named =
    traces.find((t) => t.ghostOf === input.ghostOf && t.bot !== input.player) ??
    traces.find((t) => t.bot !== input.player);
  if (named) return named.bot;
  return input.ghostOf || null;
}

function shotTimes(events: LogEvent[], who: string | null): number[] {
  if (!who) return [];
  const out: number[] = [];
  for (const e of events) {
    if (e.type === 'tracer' && e.player === who) out.push(e.t);
  }
  return out.sort((a, b) => a - b);
}

function attemptFacts(input: DrillInput, attempt: DrillAttemptInput, index: number): AttemptFacts {
  const events = Array.isArray(attempt?.events) ? attempt.events : [];
  const traces = (Array.isArray(attempt?.traces) ? attempt.traces : [])
    .slice()
    .sort((a, b) => a.t - b.t);
  const ghost = ghostNameOf(input, traces);

  const playerShots = shotTimes(events, input.player);
  const ghostShots = shotTimes(events, ghost);
  let firstShotBy: AttemptFacts['firstShotBy'] = null;
  if (playerShots.length > 0 && (ghostShots.length === 0 || playerShots[0] <= ghostShots[0])) {
    firstShotBy = 'player';
  } else if (ghostShots.length > 0) {
    firstShotBy = 'ghost';
  }

  let damageDealt = 0;
  let damageTaken = 0;
  let headshots = 0;
  for (const e of events) {
    if (e.type !== 'damage') continue;
    if (e.attacker === input.player) {
      damageDealt += e.damage;
      if (e.headshot) headshots += 1;
    }
    if (e.victim === input.player) damageTaken += e.damage;
  }

  let endPos: Vec2 | null = null;
  const moves = events.filter(
    (e): e is Extract<LogEvent, { type: 'move' }> => e.type === 'move' && e.player === input.player,
  );
  const lastMove = moves[moves.length - 1];
  if (lastMove) endPos = { x: lastMove.pos.x, z: lastMove.pos.z };
  const death = events.find(
    (e): e is Extract<LogEvent, { type: 'kill' }> => e.type === 'kill' && e.victim === input.player,
  );
  if (!attempt.won && death) endPos = { x: death.victimPos.x, z: death.victimPos.z };

  const ghostActions: GhostActionFact[] = [];
  for (const t of traces) {
    const kind = t.action?.kind;
    if (!kind) continue;
    const last = ghostActions[ghostActions.length - 1];
    if (last && last.kind === kind) continue;
    ghostActions.push({ kind, t: secs(t.t) });
  }

  const fallbackTraces = traces.filter((t) => t.fallback === true).length;

  const seen = traces.find((t) => t.situation?.enemyVisible === true);
  let reactionS: number | null = null;
  if (seen) {
    const after = ghostShots.find((t) => t >= seen.t);
    if (typeof after === 'number') reactionS = secs(after - seen.t);
  }

  return {
    index,
    won: attempt.won === true,
    durationS: secs(attempt.durationS),
    firstShotBy,
    ghostActions,
    endPos: endPos ? { x: metres(endPos.x), z: metres(endPos.z) } : null,
    damageDealt: dmg(damageDealt),
    damageTaken: dmg(damageTaken),
    headshots,
    fallback: fallbackTraces > 0,
    fallbackTraces,
    traces: traces.length,
    reactionS,
  };
}

function attemptSentence(a: AttemptFacts): string {
  const n = a.index + 1;
  const shot =
    a.firstShotBy === 'player'
      ? 'you shot first'
      : a.firstShotBy === 'ghost'
        ? 'the ghost shot first'
        : 'nobody shot first';
  const end = a.won ? 'you won' : 'you died';
  const where = a.endPos ? ` at x ${a.endPos.x}, z ${a.endPos.z}` : '';
  return `On attempt ${n} ${shot} and the attempt lasted ${a.durationS} s; ${end}${where}, dealing ${a.damageDealt} damage and taking ${a.damageTaken}, with ${a.headshots} head ${plural(a.headshots, 'shot', 'shots')}.`;
}

function actionsSentence(a: AttemptFacts): string {
  const n = a.index + 1;
  if (a.ghostActions.length === 0) {
    return `The ghost made no recorded actions on attempt ${n}.`;
  }
  const list = a.ghostActions.map((g) => `${ACTION_LABEL[g.kind]} at ${g.t} s`).join(', ');
  return `The ghost's actions on attempt ${n} were ${list}.`;
}

function fallbackSentence(a: AttemptFacts): string {
  const n = a.index + 1;
  if (a.fallbackTraces === 0) {
    return `The ghost never fell back to the default bot on attempt ${n}.`;
  }
  return `The ghost was in fallback for ${a.fallbackTraces} of ${a.traces} ${plural(a.traces, 'trace', 'traces')} on attempt ${n}.`;
}

function buildSentences(
  input: DrillInput,
  perAttempt: AttemptFacts[],
  wins: number,
): Fact[] {
  // Ids can hold digits; listing them keeps those digits out of the number check.
  const entities = [input.player, input.ghostOf].filter(Boolean);
  const mk = (id: string, kind: FactKind, text: string): Fact => ({
    id,
    text,
    kind,
    entities,
    numbers: extractNumbers(text, entities),
  });

  const sentences: Fact[] = [];
  const times = perAttempt.length;
  const outcome =
    times === 1
      ? `You (${input.player}) replayed this moment once against the ghost of ${input.ghostOf} and ${wins > 0 ? 'won it' : 'did not win'}.`
      : `You (${input.player}) replayed this moment ${times} times against the ghost of ${input.ghostOf} and ${wins > 0 ? `won ${wins}` : 'did not win'}.`;
  sentences.push(mk('outcome', 'outcome', outcome));

  for (const a of perAttempt) {
    const n = a.index + 1;
    sentences.push(mk(`attempt-${n}`, 'event', attemptSentence(a)));
    sentences.push(mk(`actions-${n}`, 'bot', actionsSentence(a)));
    if (a.reactionS !== null) {
      sentences.push(
        mk(`reaction-${n}`, 'timing', `The ghost fired ${a.reactionS} s after it first saw you on attempt ${n}.`),
      );
    }
    sentences.push(mk(`fallback-${n}`, 'bot', fallbackSentence(a)));
  }
  return sentences;
}

/** Everything the debrief may use, in the order it should be considered. */
export function drillFacts(input: DrillInput): DrillFacts {
  const attempts = Array.isArray(input?.attempts) ? input.attempts : [];
  const perAttempt = attempts.map((a, i) => attemptFacts(input, a, i));
  const wins = perAttempt.filter((a) => a.won).length;
  const firstWinIndex = perAttempt.findIndex((a) => a.won);
  return {
    scenarioId: input?.scenarioId ?? '',
    attempts: perAttempt.length,
    wins,
    firstWinIndex: firstWinIndex < 0 ? null : firstWinIndex,
    perAttempt,
    sentences: buildSentences(input, perAttempt, wins),
  };
}

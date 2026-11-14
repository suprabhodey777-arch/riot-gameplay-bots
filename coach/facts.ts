/**
 * Facts: the only material the debrief may use. Each fact is one plain
 * sentence plus the numbers in it. The template reads them directly, the model
 * is shown them, and the checker rejects any number that is not among them.
 */
import type { SimEvent } from '../shared/contract.ts';
import type { DrillResult, DrillSpec, Skill, WeaknessReport } from '../shared/coaching.ts';

export const TICK_RATE = 20;

export type FactKind = 'outcome' | 'event' | 'timing' | 'bot' | 'weakness' | 'score';

export interface Fact {
  id: string;
  text: string;
  /** Every number the sentence states (ids such as "bot-2" are not numbers). */
  numbers: number[];
  /** Player and bot ids named in the sentence; their digits are not numbers. */
  entities: string[];
  kind: FactKind;
  skill?: Skill;
}

export interface CoachInput {
  report?: WeaknessReport;
  result?: DrillResult;
  /** The drill that was played; gives the round clock and spike time. */
  drill?: DrillSpec;
  /** The human's id; defaults to the report's player, then the drill's. */
  playerId?: string;
  /** Held-out exam success rates (0-1) before and after practice. */
  examBefore?: number;
  examAfter?: number;
}

export const SKILL_LABEL: Record<Skill, string> = {
  entry: 'entry',
  clutch: 'clutch',
  time_pressure: 'time pressure',
  smoke_usage: 'smoke usage',
};

/** Remove entity ids so their digits are not read as numbers. */
export function stripEntities(text: string, entities: string[]): string {
  let out = text;
  const sorted = [...entities].sort((a, b) => b.length - a.length);
  for (const e of sorted) {
    if (!e) continue;
    out = out.replace(new RegExp(e.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'gi'), ' ');
  }
  return out;
}

/** Every number written in the text: integers, decimals, with or without % or units. */
export function extractNumbers(text: string, entities: string[] = []): number[] {
  const clean = stripEntities(text, entities);
  const found = clean.match(/\d[\d,]*(?:\.\d+)?/g) ?? [];
  return found.map((s) => Number(s.replace(/,/g, ''))).filter((n) => Number.isFinite(n));
}

function mk(
  id: string,
  text: string,
  kind: FactKind,
  entities: string[] = [],
  skill?: Skill,
): Fact {
  return { id, text, kind, entities, skill, numbers: extractNumbers(text, entities) };
}

const secs = (tick: number): number => Math.round((tick / TICK_RATE) * 10) / 10;
const pct = (rate: number): number => Math.round(rate * 100);
const plural = (n: number, one: string, many: string): string => (n === 1 ? one : many);

function factsFromResult(input: CoachInput, playerId: string | undefined): Fact[] {
  const { result, drill } = input;
  if (!result) return [];
  const facts: Fact[] = [];
  const who = (id: string): string => (id === playerId ? `you (${id})` : id);
  const ents = (...ids: string[]): string[] => ids;

  const label = SKILL_LABEL[result.skill];
  facts.push(
    mk(
      'outcome',
      `The ${label} drill was a ${result.success ? 'success' : 'failure'} after ${secs(result.ticks)} s.`,
      'outcome',
      [],
      result.skill,
    ),
  );

  const kills = result.events.filter((e): e is Extract<SimEvent, { type: 'kill' }> => e.type === 'kill');
  kills.slice(0, 4).forEach((k, i) => {
    const part = k.part === 'head' ? 'a head shot' : 'a body shot';
    facts.push(
      mk(
        `kill-${i + 1}`,
        `${who(k.killer)} killed ${who(k.victim)} with ${part} at ${secs(k.tick)} s.`,
        'event',
        ents(k.killer, k.victim),
      ),
    );
  });
  if (kills.length > 0 && playerId) {
    const first = kills[0];
    if (first.victim === playerId) {
      facts.push(
        mk(
          'died-first',
          `You (${playerId}) died first, at ${secs(first.tick)} s, to ${first.killer}.`,
          'event',
          ents(playerId, first.killer),
        ),
      );
    }
  }

  if (playerId) {
    const dmg = result.events.filter(
      (e): e is Extract<SimEvent, { type: 'damage' }> => e.type === 'damage' && e.attacker === playerId,
    );
    const head = dmg.filter((d) => d.part === 'head').length;
    const body = dmg.length - head;
    facts.push(
      mk(
        'shots-landed',
        `You (${playerId}) landed ${head} head ${plural(head, 'shot', 'shots')} and ${body} body ${plural(body, 'shot', 'shots')}.`,
        'event',
        ents(playerId),
      ),
    );
  }

  const spotted = result.events.find(
    (e): e is Extract<SimEvent, { type: 'spotted' }> => e.type === 'spotted',
  );
  if (spotted) {
    facts.push(
      mk(
        'first-contact',
        `${who(spotted.observer)} first spotted ${who(spotted.target)} at ${secs(spotted.tick)} s.`,
        'timing',
        ents(spotted.observer, spotted.target),
      ),
    );
  }

  for (const e of result.events) {
    if (e.type === 'smoke') {
      facts.push(
        mk(`smoke-${e.tick}`, `${who(e.agent)} threw a smoke at ${secs(e.tick)} s.`, 'event', ents(e.agent)),
      );
    }
  }

  const plant = result.events.find((e): e is Extract<SimEvent, { type: 'plant' }> => e.type === 'plant');
  if (plant) {
    let text = `${who(plant.agent)} planted on site ${plant.site} at ${secs(plant.tick)} s`;
    if (drill) {
      const left = Math.round((drill.roundTimeLeftS - plant.tick / TICK_RATE) * 10) / 10;
      text += ` with ${left} s left`;
    }
    facts.push(mk('plant', `${text}.`, 'timing', ents(plant.agent)));
  }

  const defuse = result.events.find((e): e is Extract<SimEvent, { type: 'defuse' }> => e.type === 'defuse');
  if (defuse) {
    let text = `${who(defuse.agent)} defused the spike at ${secs(defuse.tick)} s`;
    if (drill?.spikePlanted) {
      const left = Math.round((drill.spikePlanted.secondsLeft - defuse.tick / TICK_RATE) * 10) / 10;
      text += ` with ${left} s left on it`;
    }
    facts.push(mk('defuse', `${text}.`, 'timing', ents(defuse.agent)));
  }
  if (drill?.spikePlanted) {
    facts.push(
      mk(
        'spike-start',
        `The spike was planted on site ${drill.spikePlanted.site} with ${drill.spikePlanted.secondsLeft} s left when the drill began.`,
        'timing',
      ),
    );
  }

  const end = result.events.find(
    (e): e is Extract<SimEvent, { type: 'round_end' }> => e.type === 'round_end',
  );
  if (end) {
    const why: Record<typeof end.reason, string> = {
      elimination: 'by elimination',
      detonated: 'when the spike detonated',
      defused: 'when the spike was defused',
      time: 'when time ran out',
    };
    facts.push(
      mk('round-end', `The ${end.winner} side won ${why[end.reason]} at ${secs(end.tick)} s.`, 'outcome'),
    );
  }

  // Bot behaviour from the trace: traits at first sight, first and last decision.
  const byBot = new Map<string, typeof result.trace>();
  for (const t of result.trace) {
    const list = byBot.get(t.botId) ?? [];
    list.push(t);
    byBot.set(t.botId, list);
  }
  let n = 0;
  for (const [botId, entries] of byBot) {
    if (n++ >= 3) break;
    const first = entries[0];
    const last = entries[entries.length - 1];
    if (typeof first.traits.reactionMs === 'number') {
      facts.push(
        mk(
          `reaction-${botId}`,
          `The bot ${botId} had a reaction time of ${Math.round(first.traits.reactionMs)} ms.`,
          'bot',
          ents(botId),
        ),
      );
    }
    facts.push(
      mk(
        `last-${botId}`,
        `The bot ${botId} last chose ${last.node} at ${secs(last.tick)} s because: ${last.reason}`,
        'bot',
        ents(botId),
      ),
    );
    if (entries.length > 1) {
      facts.push(
        mk(
          `first-${botId}`,
          `The bot ${botId} first chose ${first.node} at ${secs(first.tick)} s because: ${first.reason}`,
          'bot',
          ents(botId),
        ),
      );
    }
  }
  return facts;
}

function factsFromReport(report: WeaknessReport): Fact[] {
  const facts: Fact[] = [];
  const ents = [report.playerId];
  report.findings.slice(0, 2).forEach((f, i) => {
    const label = SKILL_LABEL[f.skill];
    facts.push(
      mk(
        `finding-${f.skill}`,
        `${report.playerId} succeeded at ${label} ${pct(f.rate)}% of the time, against a benchmark of ${pct(f.benchmark)}%, over ${f.sampleSize} ${plural(f.sampleSize, 'situation', 'situations')}.`,
        'weakness',
        ents,
        f.skill,
      ),
    );
    if (f.summary) facts.push(mk(`summary-${f.skill}`, f.summary, 'weakness', ents, f.skill));
    if (i === 0 && f.evidence[0]) {
      const ev = f.evidence[0];
      facts.push(
        mk(`evidence-${f.skill}`, `Example from round ${ev.round}: ${ev.note}`, 'weakness', ents, f.skill),
      );
    }
  });
  return facts;
}

/** Everything the debrief may use, in the order it should be considered. */
export function factsFrom(input: CoachInput): Fact[] {
  const playerId = input.playerId ?? input.report?.playerId ?? input.drill?.player.id;
  const facts: Fact[] = [];
  facts.push(...factsFromResult(input, playerId));
  if (input.report) facts.push(...factsFromReport(input.report));
  if (typeof input.examBefore === 'number' && typeof input.examAfter === 'number') {
    const b = pct(input.examBefore);
    const a = pct(input.examAfter);
    const d = Math.abs(a - b);
    const dir = a > b ? `up ${d} points` : a < b ? `down ${d} points` : 'unchanged';
    facts.push(
      mk('exam', `The held-out exam score went from ${b}% to ${a}%, ${dir}.`, 'score', [], input.result?.skill),
    );
  }
  return facts;
}

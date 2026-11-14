import { describe, expect, it } from 'vitest';
import type { DrillResult, DrillSpec, WeaknessReport } from '../shared/coaching.ts';
import { checkText, supported } from './check.ts';
import { debrief, type FallbackReason } from './debrief.ts';
import { env } from './env.ts';
import { factsFrom, type CoachInput } from './facts.ts';
import { AnthropicCoach, SYSTEM_PROMPT, userPrompt, type CoachModel } from './llm.ts';
import { templateDebrief } from './template.ts';

const v = (x: number, y: number) => ({ x, y });
const traits = { reactionMs: 280, headAccuracy: 0.3, bodyAccuracy: 0.7, aggression: 0.5, offAngle: 0, patience: 0.5, utility: 0 };

const drill = {
  id: 'drill-1',
  skill: 'time_pressure',
  roundTimeLeftS: 30,
  player: { id: 'player-1', team: 'attack', spawn: v(0, 0), hp: 100, carryingSpike: true },
  spikePlanted: null,
} as unknown as DrillSpec;

const result: DrillResult = {
  drillId: 'drill-1',
  skill: 'time_pressure',
  success: false,
  ticks: 600,
  events: [
    { type: 'spotted', tick: 100, observer: 'bot-2', target: 'player-1' },
    { type: 'plant', tick: 360, agent: 'player-1', site: 'B' },
    { type: 'damage', tick: 400, attacker: 'player-1', victim: 'bot-2', amount: 40, part: 'head', attackerPos: v(0, 0), victimPos: v(1, 1) },
    { type: 'kill', tick: 500, killer: 'bot-2', victim: 'player-1', part: 'body', killerPos: v(1, 1), victimPos: v(0, 0) },
    { type: 'round_end', tick: 600, winner: 'defense', reason: 'elimination' },
  ],
  trace: [
    { tick: 100, botId: 'bot-2', node: 'hold_angle', reason: 'enemy seen, waiting for reaction', traits },
    { tick: 480, botId: 'bot-2', node: 'push', reason: 'enemy is planting', traits },
  ],
};

const report: WeaknessReport = {
  playerId: 'player-1',
  role: 'duelist',
  matchesRead: 3,
  findings: [
    {
      skill: 'entry',
      rate: 0.2,
      benchmark: 0.55,
      severity: 0.8,
      sampleSize: 5,
      summary: 'Died first on entry in 4 of 5 rounds.',
      evidence: [{ matchId: 'm1', round: 3, tick: 220, note: 'first death 3 s after first contact' }],
    },
  ],
};

const input: CoachInput = { result, drill, report, examBefore: 0.4, examAfter: 0.65 };

const sleep = (ms: number) => new Promise<void>((r) => env().setTimeout(r, ms));
const fake = (fn: () => Promise<string>): CoachModel => ({ write: fn });

describe('facts', () => {
  const facts = factsFrom(input);
  const byId = (id: string) => facts.find((f) => f.id === id);

  it('extracts drill facts with numbers and ids', () => {
    expect(byId('outcome')?.text).toBe('The time pressure drill was a failure after 30 s.');
    expect(byId('plant')?.text).toBe('you (player-1) planted on site B at 18 s with 12 s left.');
    expect(byId('plant')?.numbers).toEqual([18, 12]);
    expect(byId('died-first')?.text).toContain('bot-2');
    expect(byId('shots-landed')?.numbers).toEqual([1, 0]);
    expect(byId('reaction-bot-2')?.text).toContain('280 ms');
    expect(byId('reaction-bot-2')?.numbers).toEqual([280]);
    expect(byId('last-bot-2')?.text).toContain('enemy is planting');
  });

  it('does not read digits in ids as numbers', () => {
    expect(byId('kill-1')?.numbers).toEqual([25]);
  });

  it('extracts report and exam facts', () => {
    expect(byId('finding-entry')?.text).toContain('20%');
    expect(byId('finding-entry')?.numbers).toEqual([20, 55, 5]);
    expect(byId('summary-entry')?.numbers).toEqual([4, 5]);
    expect(byId('exam')?.numbers).toEqual([40, 65, 25]);
  });
});

describe('check', () => {
  const facts = factsFrom(input);
  it('accepts supported numbers, units and ids', () => {
    expect(supported('You planted at 18 s with 12 s left; bot-2 had 280 ms. Died first in 4 of 5 rounds, 20%.', facts)).toBe(true);
  });
  it('rejects an invented number', () => {
    const r = checkText('You planted with 9 s left.', facts);
    expect(r.ok).toBe(false);
    expect(r.offending).toEqual([9]);
  });
  it('rejects empty and overlong text', () => {
    expect(supported('   ', facts)).toBe(false);
    expect(supported(Array(121).fill('word').join(' '), facts)).toBe(false);
  });
});

describe('template', () => {
  it('uses only numbers from the facts and stays short', () => {
    for (const inp of [input, { result }, { report }, {}] as CoachInput[]) {
      const facts = factsFrom(inp);
      const text = templateDebrief(facts);
      expect(supported(text, facts)).toBe(true);
      expect(text.split(/\s+/).length).toBeLessThanOrEqual(90);
      expect(text).not.toMatch(/[\u{1F300}-\u{1FAFF}]/u);
    }
  });
});

describe('debrief', () => {
  const good = 'You planted with 12 s left and bot-2 killed you. Plant earlier. Next drill: finish with time to spare.';

  it('uses a good model answer as llm', async () => {
    const out = await debrief(input, { model: fake(async () => good) });
    expect(out.source).toBe('llm');
    expect(out.text).toBe(good);
    expect(out.fallbackReason).toBeUndefined();
  });

  it('falls back on an invented number', async () => {
    const seen: FallbackReason[] = [];
    const out = await debrief(input, {
      model: fake(async () => 'You planted with 7 s left.'),
      onFallback: (r) => seen.push(r),
    });
    expect(out.source).toBe('template');
    expect(out.fallbackReason).toBe('unsupported_number');
    expect(out.detail).toContain('7');
    expect(seen).toEqual(['unsupported_number']);
  });

  it('falls back on a slow model', async () => {
    const out = await debrief(input, { model: fake(async () => { await sleep(200); return good; }), timeoutMs: 20 });
    expect(out.source).toBe('template');
    expect(out.fallbackReason).toBe('timeout');
  });

  it('falls back when the model throws', async () => {
    const out = await debrief(input, { model: fake(async () => { throw new Error('boom'); }) });
    expect(out.source).toBe('template');
    expect(out.fallbackReason).toBe('model_error');
    expect(out.detail).toBe('boom');
  });

  it('falls back on empty text', async () => {
    const out = await debrief(input, { model: fake(async () => '') });
    expect(out.fallbackReason).toBe('empty');
  });

  it('falls back with no API key', async () => {
    const out = await debrief(input, { apiKey: null });
    expect(out.source).toBe('template');
    expect(out.fallbackReason).toBe('no_api_key');
  });

  it('never throws, even on bad input', async () => {
    const out = await debrief({ result: { events: null } } as unknown as CoachInput, { model: fake(async () => good) });
    expect(out.source).toBe('template');
    expect(out.fallbackReason).toBe('internal_error');
  });
});

describe('AnthropicCoach request', () => {
  it('sends the documented headers and only the facts', async () => {
    let captured: { url: string; headers: Record<string, string>; body: any } | undefined;
    const coach = new AnthropicCoach({
      apiKey: 'k',
      fetch: async (url, init) => {
        captured = { url, headers: init.headers, body: JSON.parse(init.body) };
        return { ok: true, status: 200, json: async () => ({ content: [{ type: 'text', text: 'hi' }] }) };
      },
    });
    const facts = factsFrom(input);
    expect(await coach.write(facts, { maxWords: 90 })).toBe('hi');
    expect(captured?.url).toBe('https://api.anthropic.com/v1/messages');
    expect(captured?.headers['anthropic-version']).toBe('2023-06-01');
    expect(captured?.headers['x-api-key']).toBe('k');
    expect(captured?.body.model).toBe('claude-haiku-5-5');
    expect(captured?.body.system).toBe(SYSTEM_PROMPT);
    expect(captured?.body.messages[0].content).toBe(userPrompt(facts, { maxWords: 90 }));
  });
});

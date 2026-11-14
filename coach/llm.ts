/**
 * The model that writes the debrief. It only talks: it is given the facts and
 * returns words. Nothing here touches the sim or any frame-by-frame decision.
 */
import { apiKeyFromEnv, env, type AbortSignalLike, type FetchLike } from './env.ts';
import type { Fact } from './facts.ts';
import type { Skill } from '../shared/coaching.ts';

export interface CoachContext {
  skill?: Skill;
  maxWords: number;
  /** Aborted when the caller's timeout fires. */
  signal?: AbortSignalLike;
}

export interface CoachModel {
  write(facts: Fact[], context: CoachContext): Promise<string>;
}

export const DEFAULT_MODEL = 'claude-haiku-5-5';
export const ENDPOINT = 'https://api.anthropic.com/v1/messages';

export const SYSTEM_PROMPT = [
  'You are the coach in a VALORANT-style practice game. Write a short debrief for the player.',
  'Use only the facts you are given. Do not state any number that is not in the facts.',
  'Do not invent events, causes, players or timings. Refer to players and bots only by the ids in the facts.',
  'Write at most 90 words, in plain second person ("you"), with no emoji and no lists.',
  'Cover: what happened, the one concrete tip to change, and the goal for the next drill.',
].join(' ');

export function userPrompt(facts: Fact[], context: CoachContext): string {
  const lines = facts.map((f, i) => `F${i + 1}: ${f.text}`);
  const skill = context.skill ? `\nSkill being drilled: ${context.skill.replace('_', ' ')}.` : '';
  return `Facts:\n${lines.join('\n')}${skill}\n\nWrite the debrief now, at most ${context.maxWords} words.`;
}

export interface AnthropicCoachOptions {
  /** Defaults to ANTHROPIC_API_KEY from the environment. */
  apiKey?: string;
  model?: string;
  maxTokens?: number;
  endpoint?: string;
  /** Thinking depth. Haiku 5.5 thinks by default; low keeps the tokens for the answer. */
  effort?: 'low' | 'medium' | 'high';
  /** Injected for tests; defaults to the global fetch. */
  fetch?: FetchLike;
}

export class AnthropicCoach implements CoachModel {
  private readonly apiKey: string | null;
  private readonly model: string;
  private readonly maxTokens: number;
  private readonly endpoint: string;
  private readonly effort: string;
  private readonly fetchFn: FetchLike | undefined;

  constructor(options: AnthropicCoachOptions = {}) {
    this.apiKey = options.apiKey ?? apiKeyFromEnv();
    this.model = options.model ?? DEFAULT_MODEL;
    this.maxTokens = options.maxTokens ?? 400;
    this.endpoint = options.endpoint ?? ENDPOINT;
    this.effort = options.effort ?? 'low';
    this.fetchFn = options.fetch;
  }

  get hasKey(): boolean {
    return this.apiKey !== null && this.apiKey !== '';
  }

  /** The exact request body; exported through a method so tests can read it. */
  buildBody(facts: Fact[], context: CoachContext): Record<string, unknown> {
    return {
      model: this.model,
      max_tokens: this.maxTokens,
      system: SYSTEM_PROMPT,
      output_config: { effort: this.effort },
      messages: [{ role: 'user', content: userPrompt(facts, context) }],
    };
  }

  async write(facts: Fact[], context: CoachContext): Promise<string> {
    if (!this.apiKey) throw new Error('no_api_key');
    const doFetch = this.fetchFn ?? env().fetch;
    if (!doFetch) throw new Error('no fetch available');
    const res = await doFetch(this.endpoint, {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        'x-api-key': this.apiKey,
        'anthropic-version': '2023-06-01',
      },
      body: JSON.stringify(this.buildBody(facts, context)),
      signal: context.signal,
    });
    if (!res.ok) throw new Error(`anthropic http ${res.status}`);
    const json = (await res.json()) as {
      stop_reason?: string;
      content?: { type: string; text?: string }[];
    };
    if (json.stop_reason === 'refusal') throw new Error('model refused');
    const text = (json.content ?? [])
      .filter((b) => b.type === 'text' && typeof b.text === 'string')
      .map((b) => b.text)
      .join('')
      .trim();
    return text;
  }
}

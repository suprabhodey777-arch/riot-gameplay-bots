/**
 * The model transport for the ghost coach. It only talks: it is given the
 * facts as a prompt and returns words. Nothing here touches play.
 *
 * Adapted from `coach/llm.ts`: the request body, response parsing and error
 * text are copied because that class is bound to the old `Fact[]`/`Skill`
 * context and a fixed system prompt. The model id, the endpoint and the
 * runtime globals are imported so both coaches use the same API.
 */
import { env, type AbortSignalLike, type FetchLike } from '../../coach/env.ts';
import { DEFAULT_MODEL, ENDPOINT } from '../../coach/llm.ts';

/** A model call: the full user prompt in, the answer text out. */
export type ModelFn = (prompt: string, signal?: AbortSignalLike) => Promise<string>;

export const GHOST_SYSTEM_PROMPT = [
  'You are the coach in a VALORANT-style practice game. The player just replayed a moment they lost, against a bot cloned from the opponent who beat them.',
  'Use only the facts you are given. Do not state any number that is not in the facts.',
  'Do not invent events, causes, players or timings. Call the player "you" and the clone "the ghost".',
  'Write 2 or 3 sentences, in plain second person, with no emoji and no lists.',
  'Say why the player lost, then what changed when they won. If they never won, say what to try next instead.',
].join(' ');

export interface GhostModelOptions {
  /** Required; the caller reads it from the environment, never logs it. */
  apiKey: string;
  model?: string;
  maxTokens?: number;
  endpoint?: string;
  /** Thinking depth. Haiku 5.5 thinks by default; low keeps the tokens for the answer. */
  effort?: 'low' | 'medium' | 'high';
  /** Injected for tests; defaults to the global fetch. */
  fetch?: FetchLike;
}

/** Build a model call that posts to the same API the old coach uses. */
export function anthropicModel(options: GhostModelOptions): ModelFn {
  const apiKey = options.apiKey;
  const model = options.model ?? DEFAULT_MODEL;
  const maxTokens = options.maxTokens ?? 400;
  const endpoint = options.endpoint ?? ENDPOINT;
  const effort = options.effort ?? 'low';
  const injected = options.fetch;

  return async (prompt, signal) => {
    if (!apiKey) throw new Error('no_api_key');
    const doFetch = injected ?? env().fetch;
    if (!doFetch) throw new Error('no fetch available');
    const body = {
      model,
      max_tokens: maxTokens,
      system: GHOST_SYSTEM_PROMPT,
      output_config: { effort },
      messages: [{ role: 'user', content: prompt }],
    };
    const res = await doFetch(endpoint, {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        'x-api-key': apiKey,
        'anthropic-version': '2023-06-01',
      },
      body: JSON.stringify(body),
      signal,
    });
    if (!res.ok) throw new Error(`anthropic http ${res.status}`);
    const json = (await res.json()) as {
      stop_reason?: string;
      content?: { type: string; text?: string }[];
    };
    if (json.stop_reason === 'refusal') throw new Error('model refused');
    return (json.content ?? [])
      .filter((b) => b.type === 'text' && typeof b.text === 'string')
      .map((b) => b.text)
      .join('')
      .trim();
  };
}

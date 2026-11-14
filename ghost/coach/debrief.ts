/**
 * drillDebrief(): the model writes if it can, in time, and only with numbers
 * the facts hold. Anything else gets the template. It never throws and it
 * never blocks play: the caller can drop the promise.
 */
import { checkText } from '../../coach/check.ts';
import { apiKeyFromEnv, env } from '../../coach/env.ts';
import { drillFacts, type DrillFacts, type DrillInput } from './facts.ts';
import { anthropicModel, type ModelFn } from './model.ts';
import { MAX_DEBRIEF_WORDS, templateDrillDebrief } from './template.ts';

export const DEFAULT_TIMEOUT_MS = 8000;

export type DrillFallbackReason = 'unchecked-number' | 'timeout' | 'no-key' | 'error';

export interface DrillDebriefResult {
  text: string;
  source: 'llm' | 'template';
  /** Why the template was used; absent when the model's text is used. */
  reason?: DrillFallbackReason;
}

export interface DrillDebriefOptions {
  /** A model to use. Without one, the Anthropic model is used when a key exists. */
  model?: ModelFn;
  /** Milliseconds to wait for the model. Default 8000. */
  timeoutMs?: number;
  /** Key for the default model. Omit to read the environment; null means none. */
  apiKey?: string | null;
}

class Timeout extends Error {}

/** The user prompt: the facts, then the shape of the answer. */
export function drillPrompt(facts: DrillFacts): string {
  const lines = facts.sentences.map((f, i) => `F${i + 1}: ${f.text}`);
  return [
    'Facts:',
    lines.join('\n'),
    '',
    `Write the debrief now, at most ${MAX_DEBRIEF_WORDS} words: why the player lost this moment, and what changed when they won. If they never won, say what to try next.`,
  ].join('\n');
}

/** Never throws: a broken template still returns one safe sentence. */
function safeTemplate(facts: DrillFacts): string {
  try {
    return templateDrillDebrief(facts);
  } catch {
    // The only fallback left when the facts themselves are unreadable.
    return 'There is no drill data to read yet.';
  }
}

export async function drillDebrief(
  input: DrillInput,
  options: DrillDebriefOptions = {},
): Promise<DrillDebriefResult> {
  let facts: DrillFacts;
  try {
    facts = drillFacts(input);
  } catch {
    // The input was not a drill; report it and stay templated.
    return { text: 'There is no drill data to read yet.', source: 'template', reason: 'error' };
  }

  const template = (reason: DrillFallbackReason): DrillDebriefResult => ({
    text: safeTemplate(facts),
    source: 'template',
    reason,
  });

  let model = options.model;
  if (!model) {
    const key = options.apiKey === undefined ? apiKeyFromEnv() : options.apiKey;
    if (!key) return template('no-key');
    model = anthropicModel({ apiKey: key });
  }

  const timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  const e = env();
  const controller = new e.AbortController();
  let timer: unknown;
  let raw: string;
  try {
    const timeout = new Promise<never>((_, reject) => {
      timer = e.setTimeout(() => {
        controller.abort();
        reject(new Timeout(`no answer within ${timeoutMs} ms`));
      }, timeoutMs);
    });
    raw = await Promise.race([model(drillPrompt(facts), controller.signal), timeout]);
  } catch (err) {
    return err instanceof Timeout ? template('timeout') : template('error');
  } finally {
    e.clearTimeout(timer);
  }

  const check = checkText(typeof raw === 'string' ? raw : '', facts.sentences);
  if (!check.ok) {
    return template(check.reason === 'unsupported_number' ? 'unchecked-number' : 'error');
  }
  return { text: raw.trim(), source: 'llm' };
}

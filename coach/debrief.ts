/**
 * debrief(): the model writes if it can, in time, and only with numbers the
 * facts hold. Anything else gets the template. It never throws.
 */
import type { Debrief } from '../shared/coaching.ts';
import { checkText } from './check.ts';
import { apiKeyFromEnv, env } from './env.ts';
import { factsFrom, type CoachInput, type Fact } from './facts.ts';
import { AnthropicCoach, type CoachModel } from './llm.ts';
import { MAX_TEMPLATE_WORDS, templateDebrief } from './template.ts';

export type FallbackReason =
  | 'no_api_key'
  | 'timeout'
  | 'model_error'
  | 'empty'
  | 'too_long'
  | 'unsupported_number'
  | 'internal_error';

export interface DebriefOptions {
  /** A model to use. Without one, the Anthropic model is used when a key exists. */
  model?: CoachModel;
  /** Key for the default model. Omit to read the environment; null means none. */
  apiKey?: string | null;
  /** Milliseconds to wait for the model. Default 4000. */
  timeoutMs?: number;
  /** Called with the reason whenever the template is used instead of the model. */
  onFallback?: (reason: FallbackReason, detail: string) => void;
}

export interface DebriefOutcome extends Debrief {
  /** Why the template was used; absent when the model's text was used. */
  fallbackReason?: FallbackReason;
  /** Error message, offending numbers, or word count behind the reason. */
  detail?: string;
  facts: Fact[];
}

class Timeout extends Error {}

export async function debrief(
  input: CoachInput,
  options: DebriefOptions = {},
): Promise<DebriefOutcome> {
  let facts: Fact[] = [];
  const timeoutMs = options.timeoutMs ?? 4000;
  try {
    facts = factsFrom(input);
    const template = (reason: FallbackReason, detail: string): DebriefOutcome => {
      try {
        options.onFallback?.(reason, detail);
      } catch {
        // a broken logger must not break the debrief
      }
      return {
        text: templateDebrief(facts, input.result?.skill),
        source: 'template',
        fallbackReason: reason,
        detail,
        facts,
      };
    };

    let model = options.model;
    if (!model) {
      const key = options.apiKey === undefined ? apiKeyFromEnv() : options.apiKey;
      if (!key) return template('no_api_key', 'ANTHROPIC_API_KEY is not set');
      model = new AnthropicCoach({ apiKey: key });
    }

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
      raw = await Promise.race([
        model.write(facts, { skill: input.result?.skill, maxWords: MAX_TEMPLATE_WORDS, signal: controller.signal }),
        timeout,
      ]);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return err instanceof Timeout ? template('timeout', message) : template('model_error', message);
    } finally {
      e.clearTimeout(timer);
    }

    const check = checkText(typeof raw === 'string' ? raw : '', facts);
    if (!check.ok) {
      const detail =
        check.reason === 'unsupported_number'
          ? `numbers not in the facts: ${check.offending.join(', ')}`
          : `${check.words} words`;
      return template(check.reason as FallbackReason, detail);
    }
    return { text: raw.trim(), source: 'llm', facts };
  } catch (err) {
    const detail = err instanceof Error ? err.message : String(err);
    let text = 'No debrief is available for this drill.';
    try {
      text = templateDebrief(facts);
    } catch {
      // keep the plain sentence
    }
    return { text, source: 'template', fallbackReason: 'internal_error', detail, facts };
  }
}

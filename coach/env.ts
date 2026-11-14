/**
 * Runtime globals the coach needs (fetch, timers, AbortController, process.env).
 * tsconfig has neither the DOM nor the node lib, so they are reached through
 * `globalThis` with a minimal local type instead of global declarations.
 */

export interface FetchResponseLike {
  ok: boolean;
  status: number;
  json(): Promise<unknown>;
}

export type FetchLike = (
  url: string,
  init: {
    method: string;
    headers: Record<string, string>;
    body: string;
    signal?: AbortSignalLike;
  },
) => Promise<FetchResponseLike>;

export interface AbortSignalLike {
  readonly aborted: boolean;
}

export interface AbortControllerLike {
  readonly signal: AbortSignalLike;
  abort(): void;
}

interface Env {
  fetch?: FetchLike;
  setTimeout: (fn: () => void, ms: number) => unknown;
  clearTimeout: (id: unknown) => void;
  AbortController: new () => AbortControllerLike;
  process?: { env?: Record<string, string | undefined> };
}

export function env(): Env {
  return globalThis as unknown as Env;
}

/** The API key from the environment, or null when unset or empty. */
export function apiKeyFromEnv(): string | null {
  const key = env().process?.env?.ANTHROPIC_API_KEY;
  return key ? key : null;
}

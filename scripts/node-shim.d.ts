/**
 * Minimal Node typings for the scripts and the evidence harness. The project
 * deliberately has no @types/node dependency; this declares only what they use.
 */
declare module 'node:fs' {
  export function writeFileSync(path: string, data: string): void;
}
declare module 'node:url' {
  export function fileURLToPath(url: string): string;
}
declare module 'node:path' {
  export function dirname(p: string): string;
  export function join(...parts: string[]): string;
}
declare const process: { argv: string[]; exit(code?: number): never; stdout: { write(s: string): void } };
declare const console: { log(...a: unknown[]): void; error(...a: unknown[]): void };
interface ImportMeta {
  url: string;
}

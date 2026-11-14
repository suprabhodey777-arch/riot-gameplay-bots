/** Minimal Node APIs used by the bridge; this repo intentionally has no @types/node dependency. */
declare module 'node:net' {
  export interface Socket {
    destroyed: boolean;
    writable: boolean;
    write(data: string): boolean;
    end(): this;
    destroy(error?: Error): this;
    setEncoding(encoding: string): this;
    setTimeout(ms: number, callback: () => void): this;
    on(event: 'data', listener: (chunk: string) => void): this;
    on(event: 'error', listener: (error: Error) => void): this;
    on(event: 'connect', listener: () => void): this;
  }
  export interface Server {
    listening: boolean;
    listen(port: number, host: string, callback?: () => void): this;
    close(callback: (error?: Error) => void): this;
    address(): { port: number; address: string; family: string } | string | null;
    on(event: 'error', listener: (error: Error) => void): this;
  }
  export function createServer(listener: (socket: Socket) => void): Server;
  export function connect(port: number, host: string): Socket;
}
declare module 'node:events' {
  export function once(emitter: unknown, event: string): Promise<unknown[]>;
}
declare module 'node:readline' {
  export function createInterface(options: { input: unknown; crlfDelay?: number }): AsyncIterable<string> & { close(): void };
}
declare module 'node:timers/promises' {
  export function setTimeout(ms: number): Promise<void>;
}
declare module 'node:url' {
  export function pathToFileURL(path: string): { href: string };
}
declare module 'node:process' {
  const process: { argv: string[]; env: Record<string, string | undefined>; exitCode?: number };
  export default process;
}

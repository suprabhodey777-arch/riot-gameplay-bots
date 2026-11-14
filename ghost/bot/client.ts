/**
 * The socket.io connection a bot uses. It sends exactly the `join` payload a
 * browser sends (`game/public/js/game.js`), plus `bot: true` so the server and
 * the overlay can tell it apart, and no client-side prediction shortcuts.
 */
import { io, type Socket } from 'socket.io-client';
import type { Team } from '../contracts.ts';

export interface JoinRequest {
  server: string;
  name: string;
  agent: string;
  team?: Team;
}

export interface JoinedPlayer {
  id: string;
  name: string;
  team: Team;
  agent?: string;
}

export interface JoinedMessage {
  me: JoinedPlayer;
  /** A server snapshot; `ghost/bot/world.ts` parses it. */
  snapshot: unknown;
}

export interface BotClient {
  socket: Socket;
  joined: Promise<JoinedMessage>;
  close(): void;
}

const JOIN_TIMEOUT_MS = 10000;

export function createBotClient(req: JoinRequest): BotClient {
  const socket = io(req.server, {
    transports: ['websocket'],
    reconnection: false,
    forceNew: true,
  });

  let resolveJoined: (msg: JoinedMessage) => void = () => {};
  let rejectJoined: (err: Error) => void = () => {};
  const joined = new Promise<JoinedMessage>((resolve, reject) => {
    resolveJoined = resolve;
    rejectJoined = reject;
  });
  const timeout = setTimeout(() => rejectJoined(new Error(`no joined message within ${JOIN_TIMEOUT_MS} ms`)), JOIN_TIMEOUT_MS);

  socket.on('connect', () => {
    const payload: Record<string, unknown> = { name: req.name, agent: req.agent, bot: true };
    if (req.team) payload.team = req.team;
    socket.emit('join', payload);
  });
  socket.on('joined', (msg: JoinedMessage) => {
    clearTimeout(timeout);
    resolveJoined(msg);
  });
  socket.on('connect_error', (err: Error) => {
    clearTimeout(timeout);
    rejectJoined(new Error(`could not connect to ${req.server}: ${err.message}`));
  });

  return {
    socket,
    joined,
    close: () => socket.disconnect(),
  };
}

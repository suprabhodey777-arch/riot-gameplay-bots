import { connect, type Socket, type Server } from 'node:net';
import { once } from 'node:events';
import { createInterface } from 'node:readline';
import { afterEach, expect, it } from 'vitest';
import { createBridgeServer } from './server.ts';
import { PROTOCOL, type ClientMessage, type ServerMessage } from './protocol.ts';
import { idleInput } from '../session/drill-run.ts';

const servers: Server[] = [];
const sockets: Socket[] = [];
afterEach(async () => {
  for (const socket of sockets.splice(0)) socket.destroy();
  await Promise.all(servers.splice(0).filter((server) => server.listening).map((server) => new Promise<void>((resolve, reject) => server.close((err) => err ? reject(err) : resolve()))));
});

async function client() {
  const server = createBridgeServer({ apiKey: null });
  servers.push(server);
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  const address = server.address();
  if (!address || typeof address === 'string') throw new Error('Missing TCP address');
  const socket = connect(address.port, '127.0.0.1');
  sockets.push(socket);
  const lines = createInterface({ input: socket, crlfDelay: Infinity })[Symbol.asyncIterator]();
  await once(socket, 'connect');
  return {
    send: (msg: ClientMessage) => socket.write(JSON.stringify(msg) + '\n'),
    async next<T extends ServerMessage['type']>(type: T): Promise<Extract<ServerMessage, { type: T }>> {
      const line = await lines.next();
      if (line.done) throw new Error('Unexpected socket close');
      const msg = JSON.parse(line.value) as ServerMessage;
      expect(msg.type).toBe(type);
      return msg as Extract<ServerMessage, { type: T }>;
    },
    async nextAny(): Promise<ServerMessage> {
      const line = await lines.next();
      if (line.done) throw new Error('Unexpected socket close');
      return JSON.parse(line.value) as ServerMessage;
    },
  };
}

it('plays a full 5v5 match from match to match_end over the bridge', async () => {
  const c = await client();
  c.send({ type: 'hello', protocol: PROTOCOL });
  expect((await c.next('hello')).protocol).toBe(PROTOCOL);
  c.send({ type: 'match', map: 'saltworks', agent: 'rook' });
  expect((await c.next('map')).map.id).toBe('saltworks');

  let buy = await c.next('buy_phase');
  expect(buy.round).toBe(1);
  expect(buy.side).toBe('attack');
  expect(buy.team).toHaveLength(4);

  // Buying and selling are answered with the player's current loadout.
  c.send({ type: 'buy', item: 'light' });
  expect((await c.next('loadout')).armor).toBe('light');
  c.send({ type: 'sell', item: 'light' });
  expect((await c.next('loadout')).armor).toBeNull();

  let rounds = 0;
  let end: Extract<ServerMessage, { type: 'match_end' }> | null = null;
  let sawSwap = false;
  let guard = 0;
  while (!end && guard++ < 13) {
    expect(buy.round).toBe(rounds + 1);
    if (buy.side === 'defense') sawSwap = true;
    c.send({ type: 'ready' });

    // A match round starts like a drill: the first step of the round carries the round-start
    // fields (spec, teams, bots), because the client ignores steps until it has a world.
    const start = await c.next('step');
    expect(start.matchStart).toBe(true);
    expect(start.playerId).toBe('player');
    expect(start.playerTeam).toBe(buy.side);
    expect(start.state.agents).toHaveLength(10);
    expect(start.bots).toHaveLength(9);
    expect(start.bots!.every((b) => b.id !== start.playerId)).toBe(true);
    expect(start.spec?.mapId).toBe('saltworks');
    expect(start.spec?.bots).toHaveLength(9);
    expect(start.spec?.loadout?.credits).toBeGreaterThanOrEqual(0);
    expect(start.spec?.player.id).toBe(start.playerId);
    expect(start.done).toBe(false);

    let step = { done: false, state: start.state } as { done: boolean; state: typeof start.state };
    let ticks = 0;
    while (!step.done && ticks++ <= 2000) {
      c.send({ type: 'input', input: idleInput(start.playerId!) });
      step = await c.next('step');
    }
    expect(step.done).toBe(true);
    expect(step.state.phase).toBe('ended');

    const result = await c.next('round_result');
    expect(result.score.player + result.score.enemy).toBe(rounds + 1);
    expect(['elimination', 'detonated', 'defused', 'time']).toContain(result.how);
    rounds++;

    const next = await c.nextAny();
    if (next.type === 'match_end') end = next;
    else if (next.type === 'buy_phase') buy = next;
    else throw new Error(`Unexpected ${next.type} after round_result`);
  }

  expect(end).not.toBeNull();
  expect(Math.max(end!.score.player, end!.score.enemy)).toBe(7);
  expect(end!.score.player + end!.score.enemy).toBe(rounds);
  expect(end!.won).toBe(end!.score.player === 7);
  expect(end!.stats.deaths).toBeGreaterThanOrEqual(0);
  expect(sawSwap).toBe(true);
}, 120000);

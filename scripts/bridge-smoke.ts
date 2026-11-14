import process from 'node:process';
/** Run against `npm run bridge`: node scripts/bridge-smoke.ts */
import { connect } from 'node:net';
import { createInterface } from 'node:readline';
import { setTimeout } from 'node:timers/promises';
import { PROTOCOL, type ClientMessage, type ServerMessage } from '../bridge/protocol.ts';
import { idleInput } from '../session/drill-run.ts';
import type { SimState } from '../shared/contract.ts';

const socket = connect(Number(process.env.BRIDGE_PORT ?? 7777), '127.0.0.1');
const lines = createInterface({ input: socket, crlfDelay: Infinity });
const send = (msg: ClientMessage): void => { socket.write(JSON.stringify(msg) + '\n'); };
let playerId = 'player';
let firstBotId = '';
let tickRate = 20;
let fireIntervalS = 0.15;
let lastShot = -Infinity;
let gotResult = false;
socket.setTimeout(15000, () => socket.destroy(new Error('Bridge response timed out')));
socket.on('error', (err) => { console.error(err.message); process.exitCode = 1; });
socket.on('connect', () => send({ type: 'hello', protocol: PROTOCOL }));
async function input(state: SimState): Promise<void> {
  await setTimeout(1000 / tickRate);
  const me = state.agents.find((a) => a.id === playerId)!;
  const bot = state.agents.find((a) => a.id === firstBotId);
  const next = idleInput(playerId, me.yaw);
  if (bot?.alive) {
    const dx = bot.pos.x - me.pos.x;
    const dy = bot.pos.y - me.pos.y;
    const distance = Math.hypot(dx, dy);
    next.move = distance > 1 ? { x: dx / distance, y: dy / distance } : { x: 0, y: 0 };
    next.yaw = Math.atan2(dy, dx);
    if (distance <= 20 && (state.tick - lastShot) / tickRate + 1e-9 >= fireIntervalS) {
      next.fire = { targetId: bot.id, result: 'head' };
      lastShot = state.tick;
    }
  }
  send({ type: 'input', input: next });
}
try {
  for await (const line of lines) {
    const msg = JSON.parse(line) as ServerMessage;
    console.log(msg.type);
    switch (msg.type) {
      case 'hello':
        tickRate = msg.rules.tickRate;
        fireIntervalS = msg.rules.fireIntervalS;
        send({ type: 'load_history', sample: { skill: 'entry', seed: 11 } });
        break;
      case 'report':
        if (gotResult) socket.end();
        else send({ type: 'next_drill' });
        break;
      case 'drill':
        playerId = msg.playerId;
        firstBotId = msg.bots[0]?.id ?? '';
        await input(msg.state);
        break;
      case 'step': if (!msg.done) await input(msg.state); break;
      case 'result':
        gotResult = true;
        console.log(`Verdict: ${msg.success ? 'success' : 'fail'} — ${msg.headline}`);
        break;
      case 'error': throw new Error(msg.message);
    }
  }
} catch (err) {
  console.error(err instanceof Error ? err.message : String(err));
  process.exitCode = 1;
} finally {
  socket.destroy();
  lines.close();
}

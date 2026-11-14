/** Turns sim events into sentences a player can read. */
import type { SimEvent } from '../shared/contract.ts';
import { TICK_RATE } from '../sim/sim.ts';

const who = (id: string, playerId: string): string => (id === playerId ? 'you' : id);
const cap = (s: string): string => s.charAt(0).toUpperCase() + s.slice(1);
const at = (tick: number): string => `${(tick / TICK_RATE).toFixed(1)} s`;

export function narrate(events: SimEvent[], playerId: string): string[] {
  const out: string[] = [];
  const killedAt = new Set(events.filter((e) => e.type === 'kill').map((e) => `${e.tick}|${(e as { victim: string }).victim}`));
  for (const e of events) {
    switch (e.type) {
      case 'kill': {
        const part = e.part === 'head' ? 'a headshot' : 'a body shot';
        out.push(`${at(e.tick)}: ${cap(who(e.killer, playerId))} killed ${who(e.victim, playerId)} with ${part}.`);
        break;
      }
      case 'damage':
        if (killedAt.has(`${e.tick}|${e.victim}`)) break;
        out.push(`${at(e.tick)}: ${cap(who(e.attacker, playerId))} hit ${who(e.victim, playerId)} (${e.part}, ${e.amount} damage).`);
        break;
      case 'spotted':
        if (e.observer === playerId || e.target === playerId) {
          out.push(`${at(e.tick)}: ${cap(who(e.observer, playerId))} had a clear line to ${who(e.target, playerId)} for the first time.`);
        }
        break;
      case 'smoke':
        out.push(`${at(e.tick)}: ${cap(who(e.agent, playerId))} threw a smoke.`);
        break;
      case 'plant':
        out.push(`${at(e.tick)}: ${cap(who(e.agent, playerId))} planted the spike on site ${e.site}.`);
        break;
      case 'defuse':
        out.push(`${at(e.tick)}: ${cap(who(e.agent, playerId))} defused the spike.`);
        break;
      case 'round_end': {
        const why = { elimination: 'by elimination', detonated: 'when the spike detonated', defused: 'with a defuse', time: 'when time ran out' }[e.reason];
        out.push(`${at(e.tick)}: Round over, ${e.winner === 'attack' ? 'attackers' : 'defenders'} won ${why}.`);
        break;
      }
    }
  }
  return out;
}

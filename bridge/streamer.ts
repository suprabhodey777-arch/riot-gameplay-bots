import { equipped } from '../shared/weapons.ts';
import type { PlayerInput, SimEvent, SimState } from '../shared/contract.ts';
import { isPersona, personaById, type PersonaId } from '../shared/personas.ts';
import type { StreamerMessage } from './protocol.ts';
import { createRng } from '../sim/rng.ts';

// At most six chat messages in any rolling second, one every four simulation ticks.
// Event bursts contain 2..6 messages; a bounded queue prevents old reactions piling up.
export const CHAT_WINDOW_TICKS = 20;
export const CHAT_LIMIT = 6;
export const CHAT_SPACING_TICKS = 4;
export const CHAT_QUEUE_LIMIT = 120;
const LINES = {
  first_blood: ['first blood, the opening is yours', 'first blood woke up the whole lobby'],
  multi_kill: ['{n}K! the follow-up was clean', '{n}K, keep the crosshair ready'],
  ace: ['ACE! every angle answered', 'ACE, five names on the board'],
  clutch_won: ['clutch won! patience paid off', 'clutch won, what a last stand'],
  clutch_lost: ['clutch lost, reset and take the lesson', 'clutch lost, next round has a new plan'],
  headshot: ['headshot, right on the mark', 'headshot with a tidy crosshair'],
  whiff: ['that spray found the wallpaper', 'reset the spray, breathe, try again'],
  self_flash: ['self flash, sunglasses next round', 'that flash lit up its own doorstep'],
  plant: ['plant down, time to hold the lanes', 'plant secured, the clock is your teammate'],
  defuse: ['defuse complete, wires handled', 'defuse secured under pressure'],
  streak: ['{n} round win streak, keep the rhythm', '{n} rounds running, the plan is working'],
  eco_win: ['eco win! thrift meets timing', 'eco win, budget aim came through'],
  idle: ['checking corners with the chat', 'one calm breath before the next angle'],
};
type Reaction = keyof typeof LINES;
export class StreamerOverlay {
  on = false;
  private rng: ReturnType<typeof createRng>;
  private clock = 0;
  private sent: number[] = [];
  private lastSent = -1000;
  private pending: Extract<StreamerMessage, { type: 'chat' }>[] = [];
  private kills = new Map<string, number>();
  private first = true;
  private clutch = new Map<string, number>();
  private streak = 0;
  private spray = 0;
  private lastShot = -1000;
  private calls = new Map<string, number>();
  constructor(seed: number) { this.rng = createRng(seed); }
  toggle(on: boolean): void { this.on = on; this.pending = []; }
  private burst(key: Reaction, n = 0): void {
    if (!this.on) return;
    const count = key === 'ace' || key.startsWith('clutch') ? 6 : 2 + this.rng.int(3);
    for (let i = 0; i < count; i++) {
      const left = ['frost', 'moss', 'orbit', 'copper', 'cloud', 'velvet'][this.rng.int(6)];
      const right = ['byte', 'kite', 'fern', 'spark', 'pebble', 'moth'][this.rng.int(6)];
      this.pending.push({ type: 'chat', user: `${left}${right}_${10 + this.rng.int(90)}`, color: ['#8be9fd', '#bd93f9', '#50fa7b'][this.rng.int(3)], text: LINES[key][this.rng.int(LINES[key].length)].replace('{n}', String(n)), badge: ([null, 'sub', 'mod'] as const)[this.rng.int(3)] });
    }
    if (this.pending.length > CHAT_QUEUE_LIMIT) this.pending.splice(0, this.pending.length - CHAT_QUEUE_LIMIT);
  }
  /** Called once per sim tick; its monotonic clock spans rounds with tick-zero resets. */
  observe(before: SimState, after: SimState, events: SimEvent[], playerId: string, personas: Record<string, PersonaId> = {}, thoughts: Record<string, string> = {}, input?: PlayerInput | null): StreamerMessage[] {
    this.clock++;
    if (before.tick === 0) { this.first = true; this.kills.clear(); this.clutch.clear(); this.spray = 0; }
    const out: StreamerMessage[] = [];
    const alert = (kind: Extract<StreamerMessage, { type: 'alert' }>['kind'], text: string, big = true) => { if (this.on) out.push({ type: 'alert', kind, text, big }); };
    const me = before.agents.find(a => a.id === playerId);
    // Detect a 1vN from actual alive counts, including the tick on which the last teammate dies.
    for (const state of [before, after]) for (const a of state.agents.filter(a => a.alive)) {
      const allies = state.agents.filter(b => b.alive && b.team === a.team).length;
      const enemies = state.agents.filter(b => b.alive && b.team !== a.team).length;
      if (allies === 1 && enemies >= 2 && !this.clutch.has(a.id)) { this.clutch.set(a.id, enemies); alert('clutch', `CLUTCH 1v${enemies}`); }
    }
    const call = (agent: string) => {
      const persona = personas[agent];
      if (!isPersona(persona) || this.clock - (this.calls.get(agent) ?? -1000) < 100) return;
      this.calls.set(agent, this.clock);
      out.push({ type: 'persona_line', persona, text: personaById(persona).lines[this.rng.int(3)] });
    };
    for (const e of events) {
      if (e.type === 'kill') {
        const n = (this.kills.get(e.killer) ?? 0) + 1; this.kills.set(e.killer, n);
        if (this.first) { this.first = false; this.burst('first_blood'); alert('first_blood', 'FIRST BLOOD'); }
        if (n === 2 || n === 3) { this.burst('multi_kill', n); alert('multi_kill', `${n}K`, n === 3); }
        if (n === 5) { this.burst('ace'); alert('ace', 'ACE'); }
        if (e.part === 'head') this.burst('headshot');
        call(e.killer);
      }
      if (e.type === 'plant' || e.type === 'defuse') this.burst(e.type);
      if (e.type === 'blinded' && e.by === e.agent) this.burst('self_flash');
      if (e.type === 'round_end') {
        for (const [id, n] of this.clutch) {
          const a = after.agents.find(a => a.id === id);
          const won = a?.team === e.winner;
          this.burst(won ? 'clutch_won' : 'clutch_lost'); alert('clutch', `CLUTCH 1v${n}${won ? ' WON' : ' LOST'}`);
        }
        this.streak = me?.team === e.winner ? this.streak + 1 : 0;
        if (this.streak >= 2) this.burst('streak', this.streak);
        const winners = before.agents.filter(a => a.team === e.winner);
        const losers = before.agents.filter(a => a.team !== e.winner);
        if (winners.length && winners.every(a => a.primary === null || ['wasp', 'brick'].includes(a.primary)) && losers.some(a => a.primary && ['lancer', 'viper', 'longshot'].includes(a.primary))) { this.burst('eco_win'); alert('eco_win', 'ECO WIN'); }
      }
    }
    const playerAfter = after.agents.find(a => a.id === playerId);
    const fired = me && playerAfter && playerAfter.ammo[equipped(me)] < me.ammo[equipped(me)];
    if (input?.fire && fired) {
      if (this.clock - this.lastShot > CHAT_WINDOW_TICKS) this.spray = 0;
      this.lastShot = this.clock;
      const hit = events.some(e => e.type === 'damage' && e.attacker === playerId);
      if (hit) this.spray = 0;
      else if (++this.spray === 6) { this.burst('whiff'); this.spray = 0; }
    }
    for (const [agent, node] of Object.entries(thoughts)) if (['wideSwing', 'trade', 'reposition', 'lurkWait', 'popFlash'].includes(node)) call(agent);
    if (this.on && !this.pending.length && this.clock % 160 === 0) this.burst('idle');
    this.sent = this.sent.filter(t => this.clock - t < CHAT_WINDOW_TICKS);
    if (this.on && this.pending.length && this.sent.length < CHAT_LIMIT && this.clock - this.lastSent >= CHAT_SPACING_TICKS) {
      out.push(this.pending.shift()!); this.sent.push(this.clock); this.lastSent = this.clock;
    }
    return out;
  }
}

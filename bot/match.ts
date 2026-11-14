import type { BotTraits } from '../shared/coaching.ts';
import type { MapDef, PlayerInput, SimEvent, SimState, StepResult, Team, TraceEntry } from '../shared/contract.ts';
import { createRng } from '../sim/rng.ts';
import { createSim, type Sim } from '../sim/sim.ts';
import { type Bot, createBot } from './bot.ts';
import { createHealth } from './health.ts';
import { hashStr } from './nav.ts';
import { DEFAULT_TRAITS } from './traits.ts';

export type Fault = 'freeze' | 'spam';

export interface MatchAgent {
  id: string;
  team: Team;
  isBot: boolean;
  traits?: BotTraits;
}

export interface Match {
  sim: Sim;
  state(): SimState;
  /** Advances one tick: human inputs plus every bot's decision. Bot traces are in `StepResult.trace`. */
  step(humanInputs: PlayerInput[]): StepResult;
  /** Simulates a bot bug for the demo: 'freeze' outputs no movement, 'spam' outputs invalid inputs. */
  injectFault(botId: string, fault: Fault): void;
  setTraits(botId: string, t: BotTraits): void;
  /** Bots whose circuit breaker has tripped, with the reason. */
  tripped(): Record<string, string>;
}

const sanitize = (i: PlayerInput, fallbackYaw: number): PlayerInput => {
  let { x, y } = i.move;
  if (!Number.isFinite(x) || !Number.isFinite(y)) {
    x = 0;
    y = 0;
  }
  const len = Math.hypot(x, y);
  if (len > 1) {
    x /= len;
    y /= len;
  }
  return { ...i, move: { x, y }, yaw: Number.isFinite(i.yaw) ? i.yaw : fallbackYaw };
};

export function createMatch(opts: { map: MapDef; seed: number; agents: MatchAgent[] }): Match {
  const sim = createSim({
    map: opts.map,
    seed: opts.seed,
    agents: opts.agents.map((a) => ({ id: a.id, team: a.team, isBot: a.isBot })),
  });
  const bots = new Map<string, Bot>();
  const health = new Map<string, ReturnType<typeof createHealth>>();
  for (const a of opts.agents) {
    if (!a.isBot) continue;
    const rng = createRng((Math.imul(opts.seed >>> 0, 2654435761) ^ hashStr(a.id)) >>> 0);
    bots.set(a.id, createBot({ id: a.id, map: opts.map, traits: a.traits ?? DEFAULT_TRAITS, rng, siteSeed: opts.seed }));
    health.set(a.id, createHealth());
  }
  const faults = new Map<string, Fault>();
  const trippedBy: Record<string, string> = {};

  function step(humanInputs: PlayerInput[]): StepResult {
    const before = sim.state();
    const trace: TraceEntry[] = [];
    const inputs: PlayerInput[] = [...humanInputs];
    for (const [id, bot] of bots) {
      const agent = before.agents.find((x) => x.id === id);
      if (!agent || before.phase === 'ended') continue;
      const d = bot.decide(before);
      trace.push(...d.trace);
      let input = d.input;
      const fault = faults.get(id);
      if (fault === 'freeze') input = { ...input, move: { x: 0, y: 0 }, fire: null };
      else if (fault === 'spam') input = { ...input, move: { x: 3, y: 3 }, yaw: Number.NaN };
      if (agent.alive) {
        const verdict = health.get(id)?.observe({ tick: before.tick, node: d.node, holding: d.holding && !fault, input, pos: agent.pos });
        if (verdict?.tripped) {
          const reason = verdict.reason ?? 'unhealthy output';
          const baseline = { ...DEFAULT_TRAITS };
          bot.setTraits(baseline);
          faults.delete(id);
          health.get(id)?.reset();
          trippedBy[id] = reason;
          trace.push({
            tick: before.tick,
            botId: id,
            node: 'circuit_breaker',
            reason: `${reason}; handing control to the baseline bot`,
            traits: { ...baseline },
          });
        }
      }
      inputs.push(sanitize(input, agent.yaw));
    }
    const res = sim.step(inputs);
    return { state: res.state, events: res.events, trace };
  }

  return {
    sim,
    state: () => sim.state(),
    step,
    injectFault: (botId, fault) => {
      if (!bots.has(botId)) throw new Error(`No bot ${botId}`);
      faults.set(botId, fault);
    },
    setTraits: (botId, t) => bots.get(botId)?.setTraits(t),
    tripped: () => ({ ...trippedBy }),
  };
}

export interface HeadlessResult {
  ticks: number;
  state: SimState;
  events: SimEvent[];
  trace: TraceEntry[];
  /** One state per tick, only when `record` is set. */
  states: SimState[];
}

/** Runs a bot-vs-bot match until the round ends or `maxTicks` pass. */
export function runHeadless(match: Match, maxTicks: number, record = false): HeadlessResult {
  const events: SimEvent[] = [];
  const trace: TraceEntry[] = [];
  const states: SimState[] = [];
  let state = match.state();
  let ticks = 0;
  while (ticks < maxTicks && state.phase !== 'ended') {
    const r = match.step([]);
    state = r.state;
    events.push(...r.events);
    trace.push(...r.trace);
    if (record) states.push(r.state);
    ticks++;
  }
  return { ticks, state, events, trace, states };
}

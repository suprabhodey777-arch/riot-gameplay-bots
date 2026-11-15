import type { Loadout } from '../shared/weapons.ts';
import { agentForArchetype, type AgentId } from '../shared/contract.ts';
import type { BotTraits } from '../shared/coaching.ts';
import type { MapDef, PlayerInput, SimEvent, SimState, StepResult, SiteId, Team, TraceEntry, Vec2 } from '../shared/contract.ts';
import { createRng } from '../sim/rng.ts';
import { createSim, type Scenario, type Sim } from '../sim/sim.ts';
import { type Bot, createBot } from './bot.ts';
import { createHealth } from './health.ts';
import { hashStr } from './nav.ts';
import { ARCHETYPES, archetypeById, DEFAULT_TRAITS } from './traits.ts';

export type Fault = 'freeze' | 'spam';

export interface MatchAgent {
  id: string;
  team: Team;
  isBot: boolean;
  agent?: AgentId;
  loadout?: Loadout;
  /** Explicit traits; win over `archetypeId`. */
  traits?: BotTraits;
  /** A named archetype from `traits.ts`, used when `traits` is not given. */
  archetypeId?: string;
  /** Stand here facing `yaw` until an enemy is seen (see `BotOptions.hold`). */
  hold?: { pos: Vec2; yaw: number };
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
  /** The (sanitised) inputs the bots fed the sim on the last `step`, in agent order. */
  lastBotInputs(): PlayerInput[];
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

export function createMatch(opts: { map: MapDef; seed: number; site?: SiteId; agents: MatchAgent[]; scenario?: Scenario; /** Bot team behaviour (default true); see BotOptions.teamPlay. */ teamPlay?: boolean; /** Bot aim from the difficulty setting; see AIM_DIFFICULTY. */ aimScale?: number }): Match {
  const agents = opts.agents.map(a => {
    const archetypeId = a.archetypeId ?? ARCHETYPES.find(b => JSON.stringify(b.traits) === JSON.stringify(a.traits ?? DEFAULT_TRAITS))?.id ?? 'baseline';
    return { ...a, archetypeId, agent: a.agent ?? (a.isBot ? agentForArchetype(archetypeId) : 'rook') };
  });
  const sim = createSim({
    map: opts.map,
    seed: opts.seed,
    agents: agents.map((a) => ({ id: a.id, team: a.team, isBot: a.isBot, loadout: a.loadout, agent: a.agent ?? (a.archetypeId ? agentForArchetype(a.archetypeId) : undefined) })),
    scenario: opts.scenario,
  });
  const bots = new Map<string, Bot>();
  const health = new Map<string, ReturnType<typeof createHealth>>();
  for (const a of agents) {
    if (!a.isBot) continue;
    const rng = createRng((Math.imul(opts.seed >>> 0, 2654435761) ^ hashStr(a.id)) >>> 0);
    bots.set(a.id, createBot({ id: a.id, map: opts.map, archetypeId: a.archetypeId, traits: a.traits ?? (a.archetypeId ? archetypeById(a.archetypeId).traits : DEFAULT_TRAITS), rng, siteSeed: opts.seed, site: opts.site, hold: a.hold, teamPlay: opts.teamPlay, aimScale: opts.aimScale }));
    health.set(a.id, createHealth());
  }
  const faults = new Map<string, Fault>();
  const trippedBy: Record<string, string> = {};
  let lastEvents: SimEvent[] = [];
  let botInputs: PlayerInput[] = [];

  function step(humanInputs: PlayerInput[]): StepResult {
    const before = sim.state();
    const trace: TraceEntry[] = [];
    const inputs: PlayerInput[] = [...humanInputs];
    const fromBots: PlayerInput[] = [];
    for (const [id, bot] of bots) {
      const agent = before.agents.find((x) => x.id === id);
      if (!agent || before.phase === 'ended') continue;
      const d = bot.decide(before, lastEvents);
      trace.push(...d.trace);
      let input = d.input;
      const fault = faults.get(id);
      if (fault === 'freeze') input = { ...input, move: { x: 0, y: 0 }, fire: null, dash: false, smokeAt: null, flashAt: null, reconAt: null };
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
      const clean = sanitize(input, agent.yaw);
      inputs.push(clean);
      fromBots.push(clean);
    }
    botInputs = fromBots;
    const res = sim.step(inputs);
    lastEvents = res.events;
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
    lastBotInputs: () => botInputs,
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

/**
 * Plays one drill: a match built from a DrillSpec, stepped with the player's
 * input, judged by the success rules below.
 *
 * SUCCESS, defined once, here (`DrillJudge`). A drill also ends on round_end or
 * after a tick budget (round clock + 10 s); hitting the budget is a failure
 * unless a skill's rule already succeeded.
 *
 *   entry          The player gets the first kill of the round and is still
 *                  alive 3 s (60 ticks) after it, OR the player's team wins the
 *                  round. (The drill ends as soon as the 3 s have passed.)
 *   clutch         The player's team wins the round (elimination of the
 *                  defenders, or the spike detonating, or a defuse by the
 *                  player's own side if they are defending).
 *   time_pressure  The plant (player on attack) or the defuse (player on
 *                  defense) completes, OR the player's team wins the round
 *                  first (an elimination ends the round before a plant is
 *                  needed; the clock no longer matters). Ends at once.
 *   smoke_usage    BOTH: (1) a smoke thrown by the player lands within
 *                  SMOKE_CHOKE_RANGE_M of the watched choke (the first bot's
 *                  starting place, which the drill puts in front of the site)
 *                  before first contact (the first `damage` or `kill` event; a
 *                  bare `spotted` does not count here, because on a small map the
 *                  watcher sees the player at tick 1 and nothing could succeed), and (2) the player reaches a plant site alive
 *                  (inside any site while alive, and not dead by the end),
 *                  or wins the round outright (nobody left to smoke against).
 *                  If first contact comes before a good smoke, the drill is
 *                  ended as a failure (it can no longer succeed); so is the
 *                  player dying.
 */
import type { MapDef, PlayerInput, SimEvent, SimState, StepResult, Team, TraceEntry, Vec2 } from '../shared/contract.ts';
import type { DrillResult, DrillSpec } from '../shared/coaching.ts';
import { createMatch, type Match } from '../bot/match.ts';
import { siteAt } from '../sim/geometry.ts';
import { TICK_RATE } from '../sim/sim.ts';
import { drillSimOptions } from '../analyzer/drills.ts';

/** How long the entry player must stay alive after the opening kill. */
export const ENTRY_SURVIVE_TICKS = 3 * TICK_RATE;
/** A smoke counts as "on the choke" when its centre is this close to the watched spot (smoke radius 4.5 m + 1.5 m). */
export const SMOKE_CHOKE_RANGE_M = 6;
/** Seconds of play allowed beyond the drill's own round clock. */
export const BUDGET_SLACK_S = 10;

export type Verdict = 'success' | 'fail' | null;

const dist = (a: Vec2, b: Vec2): number => Math.hypot(a.x - b.x, a.y - b.y);

export class DrillJudge {
  private firstKill: (SimEvent & { type: 'kill' }) | null = null;
  private firstContact: number | null = null;
  private smokeOk = false;
  private reachedSite = false;
  private planted = false;
  private defused = false;
  private playerDeathTick: number | null = null;
  private winner: Team | null = null;
  private ended = false;
  private lastAlive = true;
  private lastTick = 0;
  verdict: Verdict = null;

  readonly spec: DrillSpec;
  private sites: MapDef['sites'];

  constructor(spec: DrillSpec, sites: MapDef['sites']) {
    this.spec = spec;
    this.sites = sites;
  }

  /** Feed one step; returns the verdict as soon as the rules decide it. */
  observe(state: SimState, events: SimEvent[]): Verdict {
    if (this.verdict) return this.verdict;
    const pid = this.spec.player.id;
    const me = state.agents.find((a) => a.id === pid);
    this.lastTick = state.tick;
    for (const e of events) {
      if ((e.type === 'damage' || e.type === 'kill') && this.firstContact === null) this.firstContact = e.tick;
      if (e.type === 'kill') {
        if (!this.firstKill) this.firstKill = e;
        if (e.victim === pid) this.playerDeathTick = e.tick;
      } else if (e.type === 'smoke' && e.agent === pid) {
        const choke = this.spec.bots[0]?.spawn;
        const early = this.firstContact === null || e.tick < this.firstContact;
        if (choke && early && dist(e.pos, choke) <= SMOKE_CHOKE_RANGE_M) this.smokeOk = true;
      } else if (e.type === 'plant') this.planted = true;
      else if (e.type === 'defuse') this.defused = true;
      else if (e.type === 'round_end') {
        this.ended = true;
        this.winner = e.winner;
      }
    }
    this.lastAlive = !!me?.alive;
    if (me?.alive && siteAt({ sites: this.sites }, me.pos)) this.reachedSite = true;
    return this.decide();
  }

  private decide(): Verdict {
    const skill = this.spec.skill;
    const team = this.spec.player.team;
    let v: Verdict = null;
    if (skill === 'entry') {
      const k = this.firstKill;
      const mine = k !== null && k.killer === this.spec.player.id;
      const survived = mine && (this.playerDeathTick === null || this.playerDeathTick > k.tick + ENTRY_SURVIVE_TICKS);
      if (survived && k && (this.lastTick >= k.tick + ENTRY_SURVIVE_TICKS || this.ended)) v = 'success';
      else if (this.ended) v = this.winner === team ? 'success' : 'fail';
    } else if (skill === 'clutch') {
      if (this.ended) v = this.winner === team ? 'success' : 'fail';
    } else if (skill === 'time_pressure') {
      if (team === 'attack' ? this.planted : this.defused) v = 'success';
      else if (this.ended) v = this.winner === team ? 'success' : 'fail';
    } else if (this.smokeOk && this.lastAlive && (this.reachedSite || (this.ended && this.winner === team))) v = 'success';
    else if (this.ended || this.playerDeathTick !== null || (this.firstContact !== null && !this.smokeOk)) v = 'fail';
    this.verdict = v;
    return v;
  }

  /** The verdict when the drill is stopped early (tick budget): whatever the rules say now, else failure. */
  final(): boolean {
    return (this.verdict ?? this.decide()) === 'success';
  }
}

export interface DrillStep extends StepResult {
  done: boolean;
}

export interface DrillRun {
  spec: DrillSpec;
  match: Match;
  /** The tick budget this drill may use. */
  maxTicks: number;
  state(): SimState;
  /** Advances one tick. `playerInput` null leaves the player idle. After `done`, further calls do nothing. */
  step(playerInput: PlayerInput | null): DrillStep;
  done(): boolean;
  /** The result so far (or at the end). */
  finish(): DrillResult;
}

export function idleInput(agentId: string, yaw = 0): PlayerInput {
  return { agentId, move: { x: 0, y: 0 }, yaw, walk: false, crouch: false, fire: null, smokeAt: null, plantHeld: false, defuseHeld: false };
}

export function startDrill(spec: DrillSpec, opts: { maxTicks?: number } = {}): DrillRun {
  const sim = drillSimOptions(spec);
  const botTraits = new Map(spec.bots.map((b) => [b.id, b]));
  const match = createMatch({
    map: sim.map,
    seed: sim.seed,
    scenario: sim.scenario,
    agents: sim.agents.map((a) => ({ id: a.id, team: a.team, isBot: a.isBot, traits: botTraits.get(a.id)?.traits })),
  });
  const maxTicks = opts.maxTicks ?? Math.round((spec.roundTimeLeftS + BUDGET_SLACK_S) * TICK_RATE);
  const judge = new DrillJudge(spec, sim.map.sites);
  const events: SimEvent[] = [];
  const trace: TraceEntry[] = [];
  let ticks = 0;
  let finished = false;
  judge.observe(match.state(), []);

  const step = (playerInput: PlayerInput | null): DrillStep => {
    if (finished) return { state: match.state(), events: [], trace: [], done: true };
    const input = playerInput ?? idleInput(spec.player.id);
    const r = match.step([{ ...input, agentId: spec.player.id }]);
    ticks++;
    events.push(...r.events);
    trace.push(...r.trace);
    const verdict = judge.observe(r.state, r.events);
    finished = verdict !== null || r.state.phase === 'ended' || ticks >= maxTicks;
    return { ...r, done: finished };
  };

  return {
    spec,
    match,
    maxTicks,
    state: () => match.state(),
    step,
    done: () => finished,
    finish: () => ({ drillId: spec.id, skill: spec.skill, success: judge.final(), ticks, events: [...events], trace: [...trace] }),
  };
}

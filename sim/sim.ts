import type {
  AgentState,
  MapDef,
  PlayerInput,
  SimEvent,
  SimState,
  SmokeState,
  StepResult,
  Team,
} from '../shared/contract.ts';
import { AGENT_RADIUS, closestOnSegment, lineOfSight, pointInPolygon, resolveCircle } from './geometry.ts';
import { createRng, type Rng } from './rng.ts';

export const TICK_RATE = 20;
export const RUN_SPEED = 6.75;
export const WALK_SPEED = 3;
export const CROUCH_SPEED = 2;
export const HEAD_DAMAGE = 150;
export const BODY_DAMAGE = 40;
export const SMOKES_PER_AGENT = 2;
export const SMOKE_RADIUS = 4.5;
export const SMOKE_DURATION_S = 15;
export const PLANT_TIME_S = 4;
export const DEFUSE_TIME_S = 7;
export const DETONATION_S = 45;
export const ROUND_TIME_S = 100;
/** How close a defender must be to the planted spike to defuse it. */
export const DEFUSE_RANGE = 2;
/** How close an attacker must be to a dropped spike to pick it up. */
export const PICKUP_RANGE = 1;

export interface CreateSimOptions {
  map: MapDef;
  seed: number;
  agents: { id: string; team: Team; isBot: boolean }[];
}

export interface Sim {
  /** A copy of the current state; mutating it does not affect the sim. */
  state(): SimState;
  /** Advances one tick (1/20 s) and returns the new state, the events and the bot trace. */
  step(inputs: PlayerInput[]): StepResult;
  /** The sim's own PRNG, for the bot lane; the sim itself draws nothing from it yet. */
  rng: Rng;
}

const clone = <T>(v: T): T => JSON.parse(JSON.stringify(v)) as T;

export function createSim(opts: CreateSimOptions): Sim {
  const { map } = opts;
  const rng = createRng(opts.seed);
  const walls = map.walls;

  const used: Record<Team, number> = { attack: 0, defense: 0 };
  const agents: AgentState[] = opts.agents.map((a) => {
    const spawns = map.spawns[a.team];
    const pos = spawns[used[a.team]++ % spawns.length];
    return {
      id: a.id,
      team: a.team,
      isBot: a.isBot,
      pos: { x: pos.x, y: pos.y },
      yaw: a.team === 'attack' ? 0 : Math.PI,
      hp: 100,
      alive: true,
      smokesLeft: SMOKES_PER_AGENT,
      carryingSpike: false,
    };
  });
  const hadAgents = {
    attack: agents.some((a) => a.team === 'attack'),
    defense: agents.some((a) => a.team === 'defense'),
  };
  const carrier = agents.find((a) => a.team === 'attack');
  if (carrier) carrier.carryingSpike = true;

  const s: SimState = {
    tick: 0,
    tickRate: TICK_RATE,
    roundTimeLeftS: ROUND_TIME_S,
    phase: 'live',
    agents,
    smokes: [],
    spike: carrier ? { status: 'carried', carrierId: carrier.id } : { status: 'detonated' },
    winner: null,
  };
  if (!carrier) s.spike = { status: 'dropped', pos: { x: map.bounds.width / 2, y: map.bounds.height / 2 } };

  const spotted = new Set<string>();
  let smokeCounter = 0;
  let plantTicks = 0;
  let defuseTicks = 0;

  const siteOf = (p: { x: number; y: number }): 'A' | 'B' | null =>
    pointInPolygon(p, map.sites.A) ? 'A' : pointInPolygon(p, map.sites.B) ? 'B' : null;

  function endRound(events: SimEvent[], winner: Team, reason: 'elimination' | 'detonated' | 'defused' | 'time') {
    s.phase = 'ended';
    s.winner = winner;
    events.push({ type: 'round_end', tick: s.tick, winner, reason });
  }

  function step(inputs: PlayerInput[]): StepResult {
    const events: SimEvent[] = [];
    if (s.phase === 'ended') return { state: clone(s), events, trace: [] };

    s.tick++;
    s.smokes = s.smokes.filter((k) => k.endsAtTick > s.tick);

    const byId = new Map<string, PlayerInput>();
    for (const inp of inputs) byId.set(inp.agentId, inp);
    const aliveAtStart = new Set(agents.filter((a) => a.alive).map((a) => a.id));
    const live = (id: string) => aliveAtStart.has(id);

    // Movement and aim, in agent order.
    for (const a of agents) {
      const inp = byId.get(a.id);
      if (!a.alive || !inp) continue;
      let mx = inp.move.x;
      let my = inp.move.y;
      const len = Math.hypot(mx, my);
      if (len > 1) {
        mx /= len;
        my /= len;
      }
      const speed = inp.crouch ? CROUCH_SPEED : inp.walk ? WALK_SPEED : RUN_SPEED;
      a.pos = resolveCircle(
        { x: a.pos.x + (mx * speed) / TICK_RATE, y: a.pos.y + (my * speed) / TICK_RATE },
        AGENT_RADIUS,
        walls,
        map.bounds,
      );
      a.yaw = inp.yaw;
    }

    // Smokes.
    for (const a of agents) {
      const inp = byId.get(a.id);
      if (!a.alive || !inp?.smokeAt || a.smokesLeft <= 0) continue;
      a.smokesLeft--;
      const smoke: SmokeState = {
        id: `smoke-${++smokeCounter}`,
        ownerId: a.id,
        pos: { x: inp.smokeAt.x, y: inp.smokeAt.y },
        radius: SMOKE_RADIUS,
        endsAtTick: s.tick + SMOKE_DURATION_S * TICK_RATE,
      };
      s.smokes.push(smoke);
      events.push({ type: 'smoke', tick: s.tick, agent: a.id, pos: { ...smoke.pos } });
    }

    // Spotting: first time an agent has line of sight to an enemy (per ordered pair, 360 degrees).
    for (const o of agents) {
      if (!o.alive) continue;
      for (const t of agents) {
        if (!t.alive || t.team === o.team) continue;
        const key = `${o.id}>${t.id}`;
        if (spotted.has(key)) continue;
        if (lineOfSight(o.pos, t.pos, walls, s.smokes)) {
          spotted.add(key);
          events.push({ type: 'spotted', tick: s.tick, observer: o.id, target: t.id });
        }
      }
    }

    // Fire: simultaneous, resolved against who was alive when the tick began.
    for (const a of agents) {
      const inp = byId.get(a.id);
      if (!inp?.fire || !live(a.id)) continue;
      const target = agents.find((x) => x.id === inp.fire?.targetId);
      if (!target || target.team === a.team || target.hp <= 0 || !live(target.id)) continue;
      if (inp.fire.result === 'miss') continue;
      if (!lineOfSight(a.pos, target.pos, walls, s.smokes)) continue;
      const part = inp.fire.result;
      const amount = part === 'head' ? HEAD_DAMAGE : BODY_DAMAGE;
      target.hp = Math.max(0, target.hp - amount);
      events.push({ type: 'damage', tick: s.tick, attacker: a.id, victim: target.id, amount, part });
      if (target.hp <= 0) {
        target.alive = false;
        events.push({ type: 'kill', tick: s.tick, killer: a.id, victim: target.id, part });
        if (target.carryingSpike) {
          target.carryingSpike = false;
          s.spike = { status: 'dropped', pos: { ...target.pos } };
        }
      }
    }

    // Spike pickup.
    if (s.spike.status === 'dropped') {
      const dropPos = s.spike.pos;
      const picker = agents.find(
        (a) => a.alive && a.team === 'attack' && Math.hypot(a.pos.x - dropPos.x, a.pos.y - dropPos.y) <= PICKUP_RANGE,
      );
      if (picker) {
        picker.carryingSpike = true;
        s.spike = { status: 'carried', carrierId: picker.id };
      }
    }

    // Planting.
    if (s.spike.status === 'carried') {
      const c = agents.find((a) => a.id === (s.spike as { carrierId: string }).carrierId);
      const site = c && c.alive ? siteOf(c.pos) : null;
      if (c && site && byId.get(c.id)?.plantHeld) {
        plantTicks++;
        if (plantTicks >= PLANT_TIME_S * TICK_RATE) {
          c.carryingSpike = false;
          s.spike = { status: 'planted', pos: { ...c.pos }, site, plantedAtTick: s.tick, defuseProgressS: 0 };
          s.phase = 'post_plant';
          plantTicks = 0;
          events.push({ type: 'plant', tick: s.tick, agent: c.id, site });
        }
      } else {
        plantTicks = 0;
      }
    }

    // Defusing.
    if (s.spike.status === 'planted') {
      const sp = s.spike;
      const defuser = agents.find(
        (a) =>
          a.alive &&
          a.team === 'defense' &&
          byId.get(a.id)?.defuseHeld &&
          Math.hypot(a.pos.x - sp.pos.x, a.pos.y - sp.pos.y) <= DEFUSE_RANGE,
      );
      if (defuser) {
        defuseTicks++;
        sp.defuseProgressS = defuseTicks / TICK_RATE;
        if (defuseTicks >= DEFUSE_TIME_S * TICK_RATE) {
          s.spike = { status: 'defused' };
          events.push({ type: 'defuse', tick: s.tick, agent: defuser.id });
        }
      } else {
        defuseTicks = 0;
        sp.defuseProgressS = 0;
      }
    }

    // Round end checks.
    const aliveCount = (t: Team) => agents.filter((a) => a.team === t && a.alive).length;
    const planted = s.spike.status === 'planted';
    if (s.spike.status === 'defused') {
      s.roundTimeLeftS = 0;
      endRound(events, 'defense', 'defused');
    } else if (hadAgents.defense && aliveCount('defense') === 0) {
      endRound(events, 'attack', 'elimination');
    } else if (hadAgents.attack && aliveCount('attack') === 0 && !planted) {
      endRound(events, 'defense', 'elimination');
    } else if (planted) {
      const sp = s.spike as { plantedAtTick: number };
      const left = DETONATION_S - (s.tick - sp.plantedAtTick) / TICK_RATE;
      s.roundTimeLeftS = Math.max(0, left);
      if (left <= 0) {
        s.spike = { status: 'detonated' };
        endRound(events, 'attack', 'detonated');
      }
    } else {
      s.roundTimeLeftS = Math.max(0, ROUND_TIME_S - s.tick / TICK_RATE);
      if (s.roundTimeLeftS <= 0) endRound(events, 'defense', 'time');
    }

    return { state: clone(s), events, trace: [] };
  }

  return { state: () => clone(s), step, rng };
}

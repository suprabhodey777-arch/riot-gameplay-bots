import { ARMOR, DEFAULT_LOADOUT, WEAPONS, equipped, weaponDamage, type Loadout, type WeaponId } from '../shared/weapons.ts';
import { AGENTS, type AgentId } from '../shared/contract.ts';
import type {
  AgentState,
  MapDef,
  PlayerInput,
  SimEvent,
  SimState,
  SmokeState,
  StepResult,
  SiteId,
  Team,
  Vec2,
} from '../shared/contract.ts';
import { AGENT_RADIUS, lineOfSight, resolveCircle, siteAt } from './geometry.ts';
import { createRng, type Rng } from './rng.ts';

export const SMOKE_MAX_RANGE_M = 25;
export const FLASH_MAX_RANGE_M = 20;
export const FLASH_FUSE_S = 0.6;
export const FLASH_BLIND_S = 1.75;
export const FLASH_RANGE_M = 30;
export const FLASH_BEHIND_RANGE_M = 8;
export const FLASH_BEHIND_BLIND_S = 0.5;
export const FOV_RAD = 103 * Math.PI / 180;
export const DASH_DISTANCE_M = 6;
export const DASH_DURATION_S = 0.2;
export const RECON_MAX_RANGE_M = 30;
export const RECON_FUSE_S = 0.5;
export const RECON_RADIUS_M = 10;
export const RECON_REVEAL_S = 2;
export const RUN_HEARING_M = 15;
export const HEARING_RANGE = 3;
export const TICK_RATE = 20;
export const RUN_SPEED = 6.75;
export const WALK_SPEED = 3;
export const CROUCH_SPEED = 2;
export const HEAD_DAMAGE = 150;
export const BODY_DAMAGE = 40;
/** Default controller loadout for callers that do not pick an agent. */
export const SMOKES_PER_AGENT = AGENTS.haze.kit.smoke;
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

/** Optional starting conditions, for drills. Everything left out keeps the normal round start. */
export interface Scenario {
  /** Per agent id: where it starts, its health, whether it holds the spike, which way it faces. */
  agents?: Record<string, { spawn?: Vec2; hp?: number; carryingSpike?: boolean; yaw?: number }>;
  /** Seconds on the round clock at tick 0 (default 100). */
  roundTimeLeftS?: number;
  /** Start already planted, in `post_plant`, with this long until detonation. */
  spikePlanted?: { site: SiteId; pos: Vec2; secondsLeft: number } | null;
}

export interface CreateSimOptions {
  map: MapDef;
  seed: number;
  agents: { id: string; team: Team; isBot: boolean; agent?: AgentId; loadout?: Loadout }[];
  scenario?: Scenario;
}

export interface Sim {
  /** A copy of the current state; mutating it does not affect the sim. */
  state(): SimState;
  /** Advances one tick (1/20 s) and returns the new state, the events and the bot trace. */
  step(inputs: PlayerInput[]): StepResult;
  /** The sim's own PRNG, for the bot lane; the sim itself draws nothing from it yet. */
  rng: Rng;
}

// Copy the mutable world explicitly: loadout maps make JSON round-tripping costly at 20 Hz.
const clone = (v: SimState): SimState => ({
  ...v,
  agents: v.agents.map(a => ({ ...a, pos: { ...a.pos }, abilities: { ...a.abilities }, ammo: { ...a.ammo }, reserve: { ...a.reserve } })),
  smokes: v.smokes.map(s => ({ ...s, pos: { ...s.pos } })),
  spike: v.spike.status === 'planted' || v.spike.status === 'dropped' ? { ...v.spike, pos: { ...v.spike.pos } } : { ...v.spike },
});

export function createSim(opts: CreateSimOptions): Sim {
  const { map } = opts;
  const rng = createRng(opts.seed);
  const walls = map.walls;

  const sc = opts.scenario;
  const roundLimitS = sc?.roundTimeLeftS ?? ROUND_TIME_S;
  const used: Record<Team, number> = { attack: 0, defense: 0 };
  const agents: AgentState[] = opts.agents.map((a) => {
    const spawns = map.spawns[a.team];
    const over = sc?.agents?.[a.id];
    const pos = over?.spawn ?? spawns[used[a.team]++ % spawns.length];
    return {
      id: a.id,
      team: a.team,
      isBot: a.isBot,
      pos: { x: pos.x, y: pos.y },
      yaw: over?.yaw ?? (a.team === 'attack' ? 0 : Math.PI),
      hp: over?.hp ?? 100,
      alive: true,
      agent: a.agent ?? 'haze',
      abilities: { ...AGENTS[a.agent ?? 'haze'].kit },
      blindUntilTick: 0,
      revealedUntilTick: 0,
      smokesLeft: AGENTS[a.agent ?? 'haze'].kit.smoke,
      carryingSpike: false,
      credits: a.loadout?.credits ?? 0,
      armor: a.loadout?.armorPoints ?? (a.loadout?.armor ? ARMOR[a.loadout.armor].points : 0),
      primary: (a.loadout ?? DEFAULT_LOADOUT).primary ?? null,
      secondary: a.loadout?.secondary ?? 'pin',
      slot: (a.loadout ?? DEFAULT_LOADOUT).primary ? 'primary' : 'secondary',
      ammo: Object.fromEntries(Object.entries(WEAPONS).map(([id, w]) => [id, w.mag])) as Record<WeaponId, number>,
      reserve: Object.fromEntries(Object.entries(WEAPONS).map(([id, w]) => [id, w.mag * 3])) as Record<WeaponId, number>,
      reloadingUntilTick: 0,
      scoped: false,
    };
  });
  const hadAgents = {
    attack: agents.some((a) => a.team === 'attack'),
    defense: agents.some((a) => a.team === 'defense'),
  };
  const planted0 = sc?.spikePlanted ?? null;
  const carrier = planted0
    ? undefined
    : (agents.find((a) => sc?.agents?.[a.id]?.carryingSpike === true) ??
      agents.find((a) => a.team === 'attack' && sc?.agents?.[a.id]?.carryingSpike !== false));
  if (carrier) carrier.carryingSpike = true;

  const s: SimState = {
    tick: 0,
    tickRate: TICK_RATE,
    roundTimeLeftS: planted0 ? planted0.secondsLeft : roundLimitS,
    phase: planted0 ? 'post_plant' : 'live',
    agents,
    smokes: [],
    spike: carrier ? { status: 'carried', carrierId: carrier.id } : { status: 'detonated' },
    winner: null,
  };
  if (planted0) {
    s.spike = {
      status: 'planted',
      pos: { ...planted0.pos },
      site: planted0.site,
      plantedAtTick: -Math.round((DETONATION_S - planted0.secondsLeft) * TICK_RATE),
      defuseProgressS: 0,
    };
  } else if (!carrier) s.spike = { status: 'dropped', pos: { x: map.bounds.width / 2, y: map.bounds.height / 2 } };

  const pending: { kind: 'flash' | 'recon'; by: string; pos: Vec2; at: number }[] = [];
  const dashes = new Map<string, { dir: Vec2; left: number; from: Vec2 }>();
  // Team-specific reveal ownership stays private; the public timer is for rendering.
  const reveals = new Map<string, { team: Team; until: number }[]>();
  const distance = (a: Vec2, b: Vec2) => Math.hypot(a.x - b.x, a.y - b.y);
  const spotted = new Set<string>();
  const lastFire = new Map<string, number>();
  const reloadWeapon = new Map<string, WeaponId>();
  let smokeCounter = 0;
  let plantTicks = 0;
  let defuseTicks = 0;

  const siteOf = (p: { x: number; y: number }): SiteId | null => siteAt(map, p);

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

    // Weapon actions precede shooting; switching cancels a reload without granting ammo.
    for (const a of agents) {
      if (!a.alive) continue;
      const inp = byId.get(a.id);
      if (a.reloadingUntilTick && s.tick >= a.reloadingUntilTick) {
        const id = reloadWeapon.get(a.id)!;
        const count = Math.min(WEAPONS[id].mag - a.ammo[id], a.reserve[id]);
        a.ammo[id] += count; a.reserve[id] -= count;
        a.reloadingUntilTick = 0; reloadWeapon.delete(a.id);
      }
      if (inp?.slot && inp.slot !== a.slot && a[inp.slot]) {
        a.slot = inp.slot; a.reloadingUntilTick = 0; reloadWeapon.delete(a.id);
        events.push({ type: 'switch', tick: s.tick, agent: a.id, slot: a.slot });
      }
      if (inp?.scoped !== undefined) a.scoped = inp.scoped;
      const id = equipped(a), w = WEAPONS[id];
      if (inp?.reload && !a.reloadingUntilTick && a.ammo[id] < w.mag && a.reserve[id] > 0) {
        a.reloadingUntilTick = s.tick + Math.ceil(w.reloadS * TICK_RATE);
        reloadWeapon.set(a.id, id);
        events.push({ type: 'reload', tick: s.tick, agent: a.id, weapon: id });
      }
    }

    // Movement and aim, in agent order.
    for (const a of agents) {
      const inp = byId.get(a.id) ?? (dashes.has(a.id) ? { agentId: a.id, move: { x: 0, y: 0 }, yaw: a.yaw, walk: false, crouch: false, dash: false } : undefined);
      if (!a.alive || !inp) continue;
      let mx = inp.move.x;
      let my = inp.move.y;
      const len = Math.hypot(mx, my);
      if (len > 1) {
        mx /= len;
        my /= len;
      }
      if (inp.dash && a.abilities.dash > 0 && !dashes.has(a.id)) {
        a.abilities.dash--;
        const d = Math.hypot(mx, my);
        dashes.set(a.id, { dir: d ? { x: mx / d, y: my / d } : { x: Math.cos(inp.yaw), y: Math.sin(inp.yaw) }, left: DASH_DURATION_S * TICK_RATE, from: { ...a.pos } });
      }
      const dash = dashes.get(a.id);
      const from = { ...a.pos };
      const speed = inp.crouch ? CROUCH_SPEED : inp.walk ? WALK_SPEED : RUN_SPEED;
      a.pos = resolveCircle(
        { x: a.pos.x + (mx * speed) / TICK_RATE, y: a.pos.y + (my * speed) / TICK_RATE },
        AGENT_RADIUS,
        walls,
        map.bounds,
      );
      if (dash) {
        a.pos = from;
        // Small swept steps prevent tunnelling through thin walls.
        for (let j = 0; j < 15; j++) {
          const next = { x: a.pos.x + dash.dir.x * DASH_DISTANCE_M / (DASH_DURATION_S * TICK_RATE * 15), y: a.pos.y + dash.dir.y * DASH_DISTANCE_M / (DASH_DURATION_S * TICK_RATE * 15) };
          const resolved = resolveCircle(next, AGENT_RADIUS, walls, map.bounds);
          if (distance(next, resolved) > 1e-6) { dash.left = 0; break; }
          a.pos = resolved;
        }
        if (--dash.left <= 0) {
          events.push({ type: 'dash', tick: s.tick, agent: a.id, from: dash.from, to: { ...a.pos } });
          dashes.delete(a.id);
        }
      }
      if (distance(from, a.pos) > 1e-6) events.push({ type: 'footstep', tick: s.tick, agent: a.id, pos: { ...a.pos }, loud: !inp.walk && !inp.crouch });
      a.yaw = inp.yaw;
    }

    // Smokes.
    for (const a of agents) {
      const inp = byId.get(a.id);
      if (!a.alive || !inp?.smokeAt || a.smokesLeft <= 0) continue;
      if (distance(a.pos, inp.smokeAt) > SMOKE_MAX_RANGE_M) continue;
      a.smokesLeft--;
      a.abilities.smoke = a.smokesLeft;
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

    // Throws consume a charge only when the target is in range. No random draws.
    for (const a of agents) {
      const inp = byId.get(a.id);
      if (!a.alive || !inp) continue;
      for (const kind of ['flash', 'recon'] as const) {
        const pos = kind === 'flash' ? inp.flashAt : inp.reconAt;
        if (!pos || a.abilities[kind] <= 0 || distance(a.pos, pos) > (kind === 'flash' ? FLASH_MAX_RANGE_M : RECON_MAX_RANGE_M)) continue;
        a.abilities[kind]--;
        pending.push({ kind, by: a.id, pos: { ...pos }, at: s.tick + Math.round((kind === 'flash' ? FLASH_FUSE_S : RECON_FUSE_S) * TICK_RATE) });
        events.push({ type: kind, thrower: a.id, pos: { ...pos }, tick: s.tick });
      }
    }
    for (let i = pending.length - 1; i >= 0; i--) {
      const p = pending[i];
      if (p.at > s.tick) continue;
      pending.splice(i, 1);
      const owner = agents.find(a => a.id === p.by)!;
      for (const a of agents) {
        if (!a.alive || !lineOfSight(a.pos, p.pos, walls, s.smokes)) continue;
        const d = distance(a.pos, p.pos);
        if (p.kind === 'flash') {
          const angle = Math.atan2(p.pos.y - a.pos.y, p.pos.x - a.pos.x) - a.yaw;
          const facing = Math.abs(Math.atan2(Math.sin(angle), Math.cos(angle))) <= FOV_RAD / 2;
          const seconds = d <= FLASH_RANGE_M && facing ? FLASH_BLIND_S : d <= FLASH_BEHIND_RANGE_M ? FLASH_BEHIND_BLIND_S : 0;
          if (!seconds) continue;
          a.blindUntilTick = Math.max(a.blindUntilTick, s.tick + Math.round(seconds * TICK_RATE));
          events.push({ type: 'blinded', agent: a.id, untilTick: a.blindUntilTick, by: p.by, tick: s.tick });
        } else if (a.team !== owner.team && d <= RECON_RADIUS_M) {
          a.revealedUntilTick = Math.max(a.revealedUntilTick, s.tick + RECON_REVEAL_S * TICK_RATE);
          reveals.set(a.id, [...(reveals.get(a.id) ?? []), { team: owner.team, until: a.revealedUntilTick }]);
          events.push({ type: 'revealed', target: a.id, by: p.by, untilTick: a.revealedUntilTick, tick: s.tick });
        }
      }
    }

    // Spotting: first time an agent has line of sight to an enemy (per ordered pair, 360 degrees).
    for (const o of agents) {
      if (!o.alive || o.blindUntilTick > s.tick) continue;
      for (const t of agents) {
        if (!t.alive || t.team === o.team) continue;
        const key = `${o.id}>${t.id}`;
        if (spotted.has(key)) continue;
        if (lineOfSight(o.pos, t.pos, walls, s.smokes) || reveals.get(t.id)?.some(r => r.team === o.team && r.until > s.tick)) {
          spotted.add(key);
          events.push({ type: 'spotted', tick: s.tick, observer: o.id, target: t.id });
        }
      }
    }

    // Fire: simultaneous, resolved against who was alive when the tick began.
    for (const a of agents) {
      const inp = byId.get(a.id);
      if (!inp?.fire || !live(a.id)) continue;
      const id = equipped(a), w = WEAPONS[id];
      if (a.reloadingUntilTick || a.ammo[id] <= 0 || s.tick - (lastFire.get(a.id) ?? -10000) < Math.ceil(w.fireIntervalS * TICK_RATE - 1e-9)) continue;
      a.ammo[id]--; lastFire.set(a.id, s.tick);
      const target = agents.find((x) => x.id === inp.fire?.targetId);
      if (!target || target.team === a.team || target.hp <= 0 || !live(target.id)) continue;
      if (a.blindUntilTick > s.tick || inp.fire.result === 'miss') continue;
      if (!lineOfSight(a.pos, target.pos, walls, s.smokes)) continue;
      const part = inp.fire.result;
      let amount = 0;
      for (let pellet = 0; pellet < w.pellets; pellet++) {
        const damage = weaponDamage(id, part, distance(a.pos, target.pos));
        const absorbed = Math.min(target.armor, damage * 0.66);
        target.armor -= absorbed; amount += damage - absorbed;
      }
      target.hp = Math.max(0, target.hp - amount);
      events.push({
        type: 'damage',
        tick: s.tick,
        attacker: a.id,
        victim: target.id,
        amount,
        part,
        attackerPos: { ...a.pos },
        victimPos: { ...target.pos },
      });
      if (target.hp <= 0) {
        target.alive = false;
        events.push({
          type: 'kill',
          tick: s.tick,
          killer: a.id,
          victim: target.id,
          part,
          killerPos: { ...a.pos },
          victimPos: { ...target.pos },
        });
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
      s.roundTimeLeftS = Math.max(0, roundLimitS - s.tick / TICK_RATE);
      if (s.roundTimeLeftS <= 0) endRound(events, 'defense', 'time');
    }

    return { state: clone(s), events, trace: [] };
  }

  return { state: () => clone(s), step, rng };
}

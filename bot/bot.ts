import type { BotTraits } from '../shared/coaching.ts';
import type { FireIntent, MapDef, PlayerInput, SimState, SimEvent, SiteId, TraceEntry, Vec2 } from '../shared/contract.ts';
import { lineOfSight, pointInPolygon, siteAt, siteIds, sitePoly } from '../sim/geometry.ts';
import type { Rng } from '../sim/rng.ts';
import { RUN_HEARING_M, SMOKE_RADIUS, TICK_RATE } from '../sim/sim.ts';
import { calloutAt, centroid, clearSegment, hashStr, pathBetween, type Spot, spotsFor, navFor, minWallDist } from './nav.ts';
import { type BotView, botView, createMemory } from './view.ts';

/** Ticks between shots while engaging (0.2 s). */
export const FIRE_INTERVAL_TICKS = 4;
const MS_PER_TICK = 1000 / TICK_RATE;

export interface BotOptions {
  id: string;
  map: MapDef;
  traits: BotTraits;
  archetypeId?: string;
  /**
   * Team behaviour (default true): commit on a teammate's foothold, spread after the plant,
   * use the agent's flash, dash and recon, gate investigations on patience, and hear running
   * footsteps through walls. Off gives the round-1 decision code and 3 m hearing. The evidence
   * harness's simulated learner turns it off: it is built from this bot, and with team habits it
   * flashed, turned away and froze mid-entry, which is the bot's habit, not the skill profile it
   * models (120 players: targeted-minus-baseline weak-skill gain fell to +1.3 pp; with the learner
   * focused it is +4.7 against team drill bots and +6.0 against focused ones).
   */
  teamPlay?: boolean;
  rng: Rng;
  /** Mixed into the team's site choice so different matches pick different sites. */
  siteSeed?: number;
  /** Stand here, facing `yaw`, until an enemy is seen (rehearsal drills). Overrides the role's own hold spot. */
  hold?: { pos: Vec2; yaw: number };
}

export interface BotDecision {
  input: PlayerInput;
  trace: TraceEntry[];
  /** The behavior-tree node chosen this tick (every tick, unlike `trace`). */
  node: string;
  /** True when standing still is the intent (holding, planting, defusing, reacting). */
  holding: boolean;
}

export interface Bot {
  id: string;
  decide(state: SimState, events?: SimEvent[]): BotDecision;
  setTraits(t: BotTraits): void;
  traits(): BotTraits;
}

interface Action {
  node: string;
  target: string;
  reason: string;
  move?: Vec2;
  yaw?: number;
  walk?: boolean;
  crouch?: boolean;
  fire?: FireIntent | null;
  plant?: boolean;
  defuse?: boolean;
  holding?: boolean;
  smokeAt?: Vec2 | null;
  forceTrace?: boolean;
}

const dist = (a: Vec2, b: Vec2) => Math.hypot(a.x - b.x, a.y - b.y);
const bearing = (a: Vec2, b: Vec2) => Math.atan2(b.y - a.y, b.x - a.x);
const fmt = (p: Vec2) => `(${p.x.toFixed(0)},${p.y.toFixed(0)})`;

function alongPath(from: Vec2, pts: Vec2[], d: number): Vec2 {
  let cur = from;
  let left = d;
  for (const p of pts) {
    const seg = dist(cur, p);
    if (seg >= left && seg > 0) return { x: cur.x + ((p.x - cur.x) * left) / seg, y: cur.y + ((p.y - cur.y) * left) / seg };
    left -= seg;
    cur = p;
  }
  return cur;
}

export function createBot(opts: BotOptions): Bot {
  const team = opts.teamPlay !== false;
  const { id, map, rng } = opts;
  let traits: BotTraits = { ...opts.traits };
  const memory = createMemory();
  const sites = siteIds(map);
  const defenseSite: SiteId = sites[Math.floor(rng.next() * sites.length)];
  const holdRoll = rng.int(3);

  let lastKey = '';
  let lastFireTick = -1000;
  let target: string | null = null;
  const investigated = new Set<string>();
  let smokesThrown = 0;
  let utilityUntil = -1;
  let flashPoint: Vec2 | null = null;
  let lastSmokeTick = -1000;

  let path: Vec2[] | null = null;
  let pathIdx = 0;
  let pathKey = '';
  let pathTick = -1000;
  let postPlantGuard: { key: string; pos: Vec2; facing: number } | null = null;
  let lurkSpot: Vec2 | null = null;
  let spotCache: { key: string; spot: Spot } | null = null;

  const smokeBudget = () => (traits.utility >= 0.75 ? (team ? 3 : 2) : traits.utility >= 0.35 ? 1 : 0);

  /** Steps toward `goal` along the waypoint graph; also reports distance left and the path ahead. */
  function moveTo(me: Vec2, goal: Vec2, tick: number): { dir: Vec2; left: number; ahead: Vec2[] } {
    const key = `${Math.round(goal.x * 2)},${Math.round(goal.y * 2)}`;
    if (!path || key !== pathKey || tick - pathTick > 40) {
      path = pathBetween(map, me, goal) ?? [goal];
      pathIdx = 0;
      pathKey = key;
      pathTick = tick;
    }
    while (pathIdx < path.length - 1 && dist(me, path[pathIdx]) < 0.7) pathIdx++;
    for (let j = Math.min(path.length - 1, pathIdx + 6); j > pathIdx; j--) {
      if (clearSegment(me, path[j], map.walls, 0.3)) {
        pathIdx = j;
        break;
      }
    }
    const left = dist(me, goal);
    const wp = path[pathIdx];
    const d = dist(me, wp);
    const dir = left < 0.35 || d < 1e-6 ? { x: 0, y: 0 } : { x: (wp.x - me.x) / d, y: (wp.y - me.y) / d };
    return { dir, left, ahead: path.slice(pathIdx) };
  }

  function pickSpot(site: SiteId, toward: 'attack' | 'defense', role: string): Spot {
    const key = `${site}-${toward}-${role}-${traits.offAngle.toFixed(2)}`;
    if (spotCache?.key === key) return spotCache.spot;
    const all = spotsFor(map, site, toward);
    const w = traits.offAngle;
    const ranked = all
      .map((s, i) => ({ s, i, score: (1 - w) * s.common + w * s.unusual }))
      .sort((a, b) => b.score - a.score || a.i - b.i);
    const top = ranked.slice(0, Math.min(3, ranked.length));
    const spot = top[(hashStr(id) + holdRoll) % top.length].s;
    spotCache = { key, spot };
    return spot;
  }

  const scan = (base: number, tick: number) => base + 0.5 * Math.sin((tick * 2 * Math.PI) / (4 * TICK_RATE));

  function decideAction(view: BotView, state: SimState): Action {
    const me = view.self;
    const tick = view.tick;
    const elapsedS = tick / TICK_RATE;
    const seen = view.visible.filter((v) => v.los);
    // A patient lurker does not turn a distant sound into a rush across the map.
    const quietApproach = traits.patience > 0.6 && [...view.visible, ...view.remembered].some(e => dist(me.pos, e.pos) <= 25);

    // 1. engage
    if (seen.length > 0) {
      const cur = target ? seen.find((v) => v.id === target) : undefined;
      const foe = cur ?? seen.reduce((a, b) => (dist(me.pos, b.pos) < dist(me.pos, a.pos) ? b : a));
      target = foe.id;
      const ageMs = (tick - foe.since) * MS_PER_TICK;
      const yaw = bearing(me.pos, foe.pos);
      const where = calloutAt(map, foe.pos);
      const ageS = (ageMs / 1000).toFixed(2);
      if (ageMs < traits.reactionMs) {
        return {
          node: 'engage',
          target: `${foe.id}:react`,
          reason: `saw ${foe.id} at ${where} ${ageS} s ago, reaction ${traits.reactionMs} ms not elapsed, turning to it`,
          yaw,
          holding: true,
        };
      }
      let fire: FireIntent | null = null;
      if (tick - lastFireTick >= FIRE_INTERVAL_TICKS) {
        lastFireTick = tick;
        const r = rng.next();
        const result: FireIntent['result'] = r < traits.headAccuracy ? 'head' : rng.next() < traits.bodyAccuracy ? 'body' : 'miss';
        fire = { targetId: foe.id, result };
      }
      const d = dist(me.pos, foe.pos);
      const advance = traits.aggression > 0.7 && d > 6 && (!team || view.spike?.status !== 'planted');
      return {
        node: 'engage',
        target: `${foe.id}:fire`,
        reason: `saw ${foe.id} at ${where} ${ageS} s ago, reaction ${traits.reactionMs} ms elapsed, firing${fire ? ` (${fire.result})` : ''}`,
        move: advance ? { x: Math.cos(yaw), y: Math.sin(yaw) } : { x: 0, y: 0 },
        walk: advance,
        yaw,
        fire,
        holding: !advance,
        forceTrace: fire !== null,
      };
    }
    target = null;

    if (opts.hold) {
      const h = opts.hold;
      const m = moveTo(me.pos, h.pos, tick);
      if (m.left < 0.6) {
        return {
          node: 'hold_angle',
          target: fmt(h.pos),
          reason: `holding ${calloutAt(map, h.pos)}, the spot the situation was rebuilt around, no enemy in view`,
          yaw: scan(h.yaw, tick),
          crouch: traits.offAngle > 0.6,
          holding: true,
        };
      }
      return { node: 'hold_angle', target: fmt(h.pos), reason: `moving to ${calloutAt(map, h.pos)} (${m.left.toFixed(0)} m)`, move: m.dir, walk: true, yaw: Math.atan2(m.dir.y, m.dir.x) };
    }

    const sp = view.spike;
    // 2. defuse
    if (me.team === 'defense' && sp?.status === 'planted') {
      const near = dist(me.pos, sp.pos) <= 1.5;
      if (near) {
        return { node: 'defuse', target: 'spike', reason: `spike planted at ${sp.site}, no enemy in view, defusing`, defuse: true, yaw: bearing(me.pos, sp.pos), holding: true };
      }
      const m = moveTo(me.pos, sp.pos, tick);
      return { node: 'defuse', target: 'spike', reason: `spike planted at ${sp.site}, no enemy in view, going to defuse (${m.left.toFixed(0)} m)`, move: m.dir, yaw: bearing(me.pos, sp.pos) };
    }

    // 3. plant
    if (me.carryingSpike) {
      const inSite = siteAt(map, me.pos);
      if (inSite) {
        return { node: 'plant', target: inSite, reason: `on site ${inSite} with no enemy in view, holding plant`, plant: true, holding: true };
      }
    }

    // 5. investigate (smoke is overlaid afterwards)
    const windowTicks = (1 + 9 * traits.aggression) * TICK_RATE;
    // Heard-but-unseen enemies are leads to chase only for attackers and for defenders after the plant.
    // Before the plant a defender keeps holding its angle and turns to face the sound (role nodes below):
    // chasing every footstep would leave the very angle an entry drill is built to train.
    const heard = view.visible.filter((e) => !e.los);
    const chaseSounds = team && (me.team === 'attack' || sp?.status === 'planted');
    const lead = [...view.remembered, ...(chaseSounds ? heard.map((e) => ({ id: e.id, pos: e.pos, tick })) : [])]
      .filter((r) => tick - r.tick <= windowTicks && !investigated.has(`${r.id}@${r.tick}`))
      .sort((a, b) => b.tick - a.tick)[0];
    if (lead && (!team || (!(me.team === 'attack' && sp?.status === 'planted') && (traits.patience <= 0.6 || elapsedS >= Math.max(0, traits.patience - 0.6 * traits.aggression) * 55)))) {
      const m = moveTo(me.pos, lead.pos, tick);
      if (m.left < 1.5) {
        investigated.add(`${lead.id}@${lead.tick}`);
      } else {
        const where = calloutAt(map, lead.pos);
        return {
          node: 'investigate',
          target: lead.id,
          reason: `lost ${lead.id} at ${where} ${((tick - lead.tick) / TICK_RATE).toFixed(1)} s ago, checking last seen position`,
          move: m.dir,
          walk: team ? quietApproach : traits.patience > 0.6,
          yaw: bearing(me.pos, lead.pos),
        };
      }
    }

    // 6. role nodes
    const walkSpeed = traits.aggression < 0.4;
    if (me.team === 'defense') {
      const spot = pickSpot(defenseSite, 'attack', 'def');
      const m = moveTo(me.pos, spot.pos, tick);
      if (m.left < 0.6) {
        return {
          node: 'hold_angle',
          target: fmt(spot.pos),
          reason: team && heard.length
            ? `holding ${calloutAt(map, spot.pos)} on site ${defenseSite}, heard ${heard[0].id} near ${calloutAt(map, heard[0].pos)}, facing the sound`
            : `holding ${calloutAt(map, spot.pos)} on site ${defenseSite}, ${traits.offAngle > 0.6 ? 'off-angle' : 'common'} spot, no enemy in view`,
          yaw: team && heard.length ? bearing(me.pos, heard[0].pos) : scan(spot.facing, tick),
          crouch: traits.offAngle > 0.6,
          holding: true,
        };
      }
      return {
        node: 'hold_angle',
        target: fmt(spot.pos),
        reason: `moving to ${traits.offAngle > 0.6 ? 'off-angle' : 'common'} hold spot ${calloutAt(map, spot.pos)} on site ${defenseSite} (${m.left.toFixed(0)} m)`,
        move: m.dir,
        walk: walkSpeed,
        yaw: m.dir.x || m.dir.y ? Math.atan2(m.dir.y, m.dir.x) : me.yaw,
      };
    }

    // attack
    const attackers = state.agents.filter((a) => a.team === 'attack').map((a) => a.id).sort();
    const planSite: SiteId = sites[hashStr(`${opts.siteSeed ?? 0}:${attackers[0]}`) % sites.length];
    const site = sp?.status === 'planted' ? sp.site : planSite;

    if (sp?.status === 'dropped') {
      const mates = [me, ...view.allies].filter((a) => a.alive);
      const nearest = mates.reduce((a, b) => (dist(a.pos, sp.pos) <= dist(b.pos, sp.pos) ? a : b));
      if (nearest.id === me.id) {
        const m = moveTo(me.pos, sp.pos, tick);
        return { node: 'push', target: 'spike', reason: `spike dropped, recovering it (${m.left.toFixed(0)} m)`, move: m.dir, yaw: bearing(me.pos, sp.pos) };
      }
    }

    const guard = pickSpot(site, 'defense', 'atk');
    const plantSpots = spotsFor(map, site, 'defense')
      .filter((s) => pointInPolygon(s.pos, sitePoly(map, site)))
      .sort((a, b) => b.common - a.common || a.pos.x - b.pos.x || a.pos.y - b.pos.y);
    const plantSpot = plantSpots.length > 0 ? plantSpots[hashStr(id) % Math.min(3, plantSpots.length)].pos : centroid(sitePoly(map, site));
    let guardSpot: { pos: Vec2; facing: number } = guard;
    if (sp?.status === 'planted' && !team) {
      // Post-plant (focused bots): a few metres from the spike with a clear view of it.
      const near = spotsFor(map, site, 'defense')
        .filter((s) => lineOfSight(s.pos, sp.pos, map.walls))
        .sort((a, b) => Math.abs(dist(a.pos, sp.pos) - 4) - Math.abs(dist(b.pos, sp.pos) - 4) || a.pos.x - b.pos.x || a.pos.y - b.pos.y);
      if (near.length > 0) guardSpot = { pos: near[hashStr(id) % Math.min(2, near.length)].pos, facing: bearing(near[0].pos, sp.pos) };
    } else if (sp?.status === 'planted') {
      // Post-plant: stand a few metres from the spike with a clear view of it, so a defuser is caught in the open.
      const key = `${sp.pos.x},${sp.pos.y}`;
      if (postPlantGuard?.key !== key) {
        const home = centroid(map.spawns.attack);
        // Include cover outside the plant polygon: limiting this to site spots
        // made attackers walk into the open as soon as the plant completed.
        const candidates = navFor(map).nodes.filter(p => dist(p, sp.pos) >= 6 && dist(p, sp.pos) <= 22 && lineOfSight(p, sp.pos, map.walls));
        const score = (p: Vec2) => dist(p, me.pos) * 2 + Math.abs(dist(p, sp.pos) - 10) * 0.3 + Math.max(0, dist(p, home) - dist(sp.pos, home)) * 0.3 + Math.min(4, minWallDist(p, map.walls));
        candidates.sort((a, b) => score(a) - score(b) || a.x - b.x || a.y - b.y);
        const spread: Vec2[] = [];
        for (const p of candidates) {
          if (spread.every(q => dist(p, q) >= 3)) spread.push(p);
          if (spread.length >= 5) break;
        }
        const chosen = spread[hashStr(id) % spread.length] ?? guard.pos;
        postPlantGuard = { key, pos: chosen, facing: bearing(chosen, centroid(map.spawns.defense)) };
      }
      guardSpot = postPlantGuard;
    }
    const goal: Vec2 = me.carryingSpike && sp?.status !== 'planted' ? plantSpot : guardSpot.pos;

    const commitAtS = sp?.status === 'planted' ? 0 : Math.max(0, traits.patience - 0.6 * traits.aggression) * 55;
    // A teammate establishing a foothold is a team cue: do not leave the
    // entry or planter unsupported while an individual lurk timer runs out.
    const foothold = view.allies.some(a => a.alive && siteAt(map, a.pos) === site);
    const committed = (team && foothold) || elapsedS >= commitAtS || view.roundTimeLeftS < 40;
    if (!committed) {
      if (!lurkSpot) {
        const full = pathBetween(map, me.pos, goal) ?? [goal];
        lurkSpot = alongPath(me.pos, full, Math.max(0, pathLength(me.pos, full) - 20));
      }
      const m = moveTo(me.pos, lurkSpot, tick);
      if (m.left < 0.6) {
        return {
          node: 'lurk',
          target: fmt(lurkSpot),
          reason: `waiting at ${calloutAt(map, lurkSpot)} until ${commitAtS.toFixed(0)} s (patience ${traits.patience.toFixed(2)}, now ${elapsedS.toFixed(0)} s)`,
          yaw: scan(bearing(lurkSpot, goal), tick),
          holding: true,
        };
      }
      return { node: 'lurk', target: fmt(lurkSpot), reason: `slipping to lurk spot ${calloutAt(map, lurkSpot)} (${m.left.toFixed(0)} m)`, move: m.dir, walk: true, yaw: Math.atan2(m.dir.y, m.dir.x) };
    }
    const m = moveTo(me.pos, goal, tick);
    if (m.left < 0.6) {
      return {
        node: me.carryingSpike ? 'push' : 'hold_angle',
        target: fmt(goal),
        reason: me.carryingSpike ? `at plant spot ${calloutAt(map, goal)}` : `covering site ${site} from ${calloutAt(map, goal)}`,
        yaw: scan(!team && sp?.status === 'planted' ? bearing(guardSpot.pos, sp.pos) : guardSpot.facing, tick),
        holding: true,
      };
    }
    return {
      node: 'push',
      target: fmt(goal),
      reason: `${walkSpeed || (traits.patience > 0.6 && m.left < 15) ? 'walking' : 'running'} to site ${site} ${calloutAt(map, goal)} (${m.left.toFixed(0)} m), aggression ${traits.aggression.toFixed(2)}, ${elapsedS.toFixed(0)} s into the round`,
      move: m.dir,
      walk: walkSpeed || (traits.patience > 0.6 && m.left < (team ? 25 : 15)),
      yaw: Math.atan2(m.dir.y, m.dir.x),
    };
  }

  function pathLength(from: Vec2, pts: Vec2[]): number {
    let t = 0;
    let cur = from;
    for (const p of pts) {
      t += dist(cur, p);
      cur = p;
    }
    return t;
  }

  /** 4. smoke: overlaid on a moving action when the utility trait budget allows. */
  function smokeFor(view: BotView, act: Action): Vec2 | null {
    const me = view.self;
    if (smokesThrown >= smokeBudget() || me.smokesLeft <= 0 || view.tick - lastSmokeTick < 3 * TICK_RATE) return null;
    if (act.node === 'engage' || act.node === 'plant' || act.node === 'defuse') return null;
    const threat = view.remembered.filter((r) => view.tick - r.tick <= 8 * TICK_RATE).sort((a, b) => b.tick - a.tick)[0];
    if (threat) {
      const d = dist(me.pos, threat.pos);
      if (d >= 7 && d <= 22) {
        const t = Math.max(5.5, Math.min(10, d * 0.5));
        return { x: me.pos.x + ((threat.pos.x - me.pos.x) * t) / d, y: me.pos.y + ((threat.pos.y - me.pos.y) * t) / d };
      }
    }
    if (me.team === 'attack' && act.node === 'push' && path && path.length > 0 && act.move && (act.move.x || act.move.y)) {
      const ahead = path.slice(pathIdx);
      const left = pathLength(me.pos, ahead);
      if (left >= 16 && left <= 28) return alongPath(me.pos, ahead, left - 10);
    }
    return null;
  }

  function decide(state: SimState, events: SimEvent[] = []): BotDecision {
    // Focused drill bots hear by the drill rules (3 m), not running footsteps through walls.
    const heardEvents = team ? events : [];
    const view = botView(state, id, map, memory, heardEvents);
    const me = view.self;
    const idle: PlayerInput = { agentId: id, move: { x: 0, y: 0 }, yaw: me.yaw, walk: false, crouch: false, fire: null, smokeAt: null, plantHeld: false, defuseHeld: false };
    if (!me.alive) return { input: idle, trace: [], node: 'dead', holding: true };
    if (me.blindUntilTick > view.tick) return { input: idle, trace: [], node: 'blinded', holding: true };
    const act = decideAction(view, state);
    const threat = [...view.visible, ...view.remembered].find(e => dist(me.pos, e.pos) <= 25);
    const lurker = opts.archetypeId === 'patient-lurker' || (!opts.archetypeId && traits.patience > 0.8);
    if (team) act.walk = lurker && !!threat;
    let flashAt: Vec2 | null = null;
    let reconAt: Vec2 | null = null;
    let dash = false;
    const moving = !!act.move && Math.hypot(act.move.x, act.move.y) > 0;
    // Use known angles or the approach to site, never hidden enemy positions.
    const ahead = moving && path ? alongPath(me.pos, path.slice(pathIdx), 12) : null;
    const angle = threat?.pos ?? (act.node === 'push' ? ahead : null);
    if (team && view.tick >= utilityUntil && angle) {
      if ((me.agent === 'rook' || me.agent === 'wren') && me.abilities.flash > 0 && dist(me.pos, angle) <= 20) {
        flashAt = { ...angle };
        flashPoint = flashAt;
        utilityUntil = view.tick + 13;
      } else if (me.agent === 'rook' && moving && me.abilities.dash > 0 && utilityUntil >= 0) dash = true;
      if (me.abilities.recon > 0 && (me.agent === 'wren' || (me.agent === 'pike' && threat)) && dist(me.pos, angle) <= 30) reconAt = { ...angle };
    }
    if (flashAt || view.tick < utilityUntil) { if (flashPoint) act.yaw = bearing(me.pos, flashPoint) + Math.PI; act.move = { x: 0, y: 0 }; act.fire = null; act.holding = true; }

    // A sound changes our aim immediately, including while holding the spike.
    // It never turns a post-plant hold into a chase.
    const heard = heardEvents.slice().reverse().find(e => e.type === 'footstep' && e.loud && state.agents.some(a => a.id === e.agent && a.team !== me.team) && dist(me.pos, e.pos) <= RUN_HEARING_M);
    if (heard?.type === 'footstep' && !flashAt && view.tick >= utilityUntil) act.yaw = bearing(me.pos, heard.pos);
    let smokeAt = smokeFor(view, act);
    if (smokeAt && me.team === 'attack' && view.spike?.status === 'planted' && !lineOfSight(me.pos, view.spike.pos, [], [{ pos: smokeAt, radius: SMOKE_RADIUS }])) smokeAt = null;
    let thrownNow = false;
    if (smokeAt) {
      smokesThrown++;
      lastSmokeTick = view.tick;
      thrownNow = true;
    }
    const input: PlayerInput = {
      agentId: id,
      move: act.move ?? { x: 0, y: 0 },
      yaw: act.yaw ?? me.yaw,
      walk: act.walk ?? false,
      crouch: act.crouch ?? false,
      fire: act.fire ?? null,
      smokeAt,
      flashAt, reconAt, dash,
      plantHeld: act.plant ?? false,
      defuseHeld: act.defuse ?? false,
    };
    const trace: TraceEntry[] = [];
    if (thrownNow && smokeAt) {
      trace.push({ tick: view.tick, botId: id, node: 'smoke', reason: `utility ${traits.utility.toFixed(2)}: smoking ${calloutAt(map, smokeAt)} ${fmt(smokeAt)} between me and the threat or site`, traits: { ...traits } });
      lastKey = `smoke:${fmt(smokeAt)}`;
    }
    const key = `${act.node}:${act.target}`;
    if (key !== lastKey || act.forceTrace) {
      trace.push({ tick: view.tick, botId: id, node: act.node, reason: act.reason, traits: { ...traits } });
      lastKey = key;
    }
    return { input, trace, node: act.node, holding: act.holding ?? false };
  }

  return {
    id,
    decide,
    setTraits(t) {
      traits = { ...t };
      spotCache = null;
      lastKey = '';
    },
    traits: () => ({ ...traits }),
  };
}

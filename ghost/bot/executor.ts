/**
 * Executes one contract `Action` per tick. The executor owns the bot's
 * client-side position, facing and path, turns actions into the same socket
 * messages a browser sends (`move`, `tracer`, `hit`, `ability`, `plant-hold`,
 * `defuse-hold`, `buy`), and reports whether it is trying to move so the breaker
 * can spot a stuck bot.
 */
import type { Action, Box, SiteId, Vec2, Vec3, Weapon } from '../contracts.ts';
import type { ShotHit } from './aim.ts';
import { createCombat } from './combat.ts';
import { findPath, loadMapExport, loadZones, stepToward } from './nav.ts';
import type { SkillNumbers } from './policy-types.ts';
import { FOLLOW_MAX_M, FOLLOW_MIN_M } from './team.ts';
import { yawToDir } from './view.ts';
import type { World, WorldPlayerState } from './world.ts';

const map = loadMapExport();
const zones = loadZones();

/** `game/public/js/game.js:946`: run speed in metres per second. */
export const RUN_SPEED = 5.3;
/** Walk speed while holding or peeking, same source. */
export const WALK_SPEED = 2.7;

export interface MovePayload {
  x: number;
  y: number;
  z: number;
  yaw: number;
  pitch: number;
  moving: boolean;
  crouch: boolean;
}

export interface ExecutorContext {
  now: number;
  dt: number;
  world: World;
  skill: SkillNumbers;
  rng: () => number;
  /** `PerceptionUpdate.visible`, the only enemies the fight routine may aim at. */
  visible: { name: string; pos: Vec3 }[];
  boxes: Box[];
  /** A teammate to stay 2-4 m behind while pushing, from `team.ts`. */
  followName?: string | null;
}

export interface ExecResult {
  move: MovePayload;
  tracer: { from: [number, number, number]; dir: [number, number, number] } | null;
  hit: ShotHit | null;
  ability: { type: 'smoke'; at: [number, number] } | null;
  plantHold: boolean;
  defuseHold: boolean;
  buy: string | null;
  /** True when this tick's action asked the bot to move. */
  tryingToMove: boolean;
}

export interface Executor {
  sync(self: WorldPlayerState): void;
  tick(action: Action | null, ctx: ExecutorContext): ExecResult;
  resetRound(): void;
  position(): Vec2;
  view(): { yaw: number; pitch: number };
  abilityUsed(): boolean;
}

interface PeekState {
  key: string;
  origin: Vec2;
  side: Vec2;
  since: number;
  reach: number;
  outMs: number;
  holdMs: number;
  backMs: number;
}

const dist = (a: Vec2, b: Vec2): number => Math.hypot(a.x - b.x, a.z - b.z);
const wrap = (a: number): number => Math.atan2(Math.sin(a), Math.cos(a));
const clamp = (v: number, lo: number, hi: number): number => Math.max(lo, Math.min(hi, v));

function zoneWaypoint(id: string): Vec2 | null {
  const zone = zones.zones.find((z) => z.id === id);
  return zone ? { ...zone.waypoint } : null;
}

function siteZoneId(site: SiteId): string {
  return site === 'A' ? 'a-site' : 'b-site';
}

function inSite(site: SiteId, p: Vec2): boolean {
  const r = map.sites[site];
  return !!r && p.x >= r.x1 && p.x <= r.x2 && p.z >= r.z1 && p.z <= r.z2;
}

export function createExecutor(): Executor {
  let pos: Vec2 = { x: 0, z: 0 };
  let yaw = 0;
  let pitch = 0;
  let ready = false;
  const combat = createCombat({ yaw: 0, pitch: 0 });

  let path: Vec2[] = [];
  let pathKey = '';
  let pathAt = 0;
  let repathAt = 0;
  let peek: PeekState | null = null;
  let plantHoldAt = 0;
  let defuseHoldAt = 0;
  let abilityUsed = false;
  let lastBuy = '';

  /** Cap a turn to `degPerS * dt`, taking the short way round. */
  function turnToward(targetYaw: number, degPerS: number, dt: number): void {
    const max = (degPerS * Math.PI * dt) / 180;
    yaw = wrap(yaw + clamp(wrap(targetYaw - yaw), -max, max));
  }

  /** Walk toward `goal` along the zone graph. Returns true when it moved. */
  function advance(goal: Vec2, speed: number, dt: number, now: number): boolean {
    const key = `${Math.round(goal.x * 2)}:${Math.round(goal.z * 2)}`;
    if (key !== pathKey || path.length === 0 || now >= repathAt) {
      path = findPath(pos, goal);
      if (path.length === 0) path = [{ ...goal }];
      pathKey = key;
      pathAt = 0;
      repathAt = now + 2000;
    }
    while (pathAt < path.length - 1 && dist(pos, path[pathAt]) < 0.5) pathAt += 1;
    const wp = path[pathAt];
    const before = pos;
    pos = stepToward(pos, wp, speed * dt);
    const dx = pos.x - before.x;
    const dz = pos.z - before.z;
    const moved = Math.hypot(dx, dz);
    if (moved > 1e-4) turnToward(Math.atan2(-dx, -dz), 900, dt);
    return moved > 1e-4;
  }

  /** One out-and-back step from a corner. A jiggle completes in 0.5 s. */
  function runPeek(action: Extract<Action, { kind: 'peek_wide' | 'peek_jiggle' }>, dt: number, now: number): boolean {
    const key = JSON.stringify(action);
    if (!peek || peek.key !== key) {
      const toCorner = { x: action.corner.x - pos.x, z: action.corner.z - pos.z };
      const len = Math.hypot(toCorner.x, toCorner.z);
      const base = len > 0.25 ? { x: toCorner.x / len, z: toCorner.z / len } : yawToDir(yaw);
      const left = { x: base.z, z: -base.x };
      const side = action.dir === 'left' ? left : { x: -left.x, z: -left.z };
      const jiggle = action.kind === 'peek_jiggle';
      peek = {
        key,
        origin: { ...pos },
        side,
        since: now,
        reach: jiggle ? 0.68 : 2.7,
        outMs: jiggle ? 250 : 1000,
        holdMs: jiggle ? 0 : 350,
        backMs: jiggle ? 250 : 1000,
      };
    }
    const p = peek;
    const out: Vec2 = { x: p.origin.x + p.side.x * p.reach, z: p.origin.z + p.side.z * p.reach };
    const elapsed = now - p.since;
    let target: Vec2;
    let facing: Vec2;
    if (elapsed <= p.outMs) {
      const k = p.outMs <= 0 ? 1 : elapsed / p.outMs;
      target = { x: p.origin.x + (out.x - p.origin.x) * k, z: p.origin.z + (out.z - p.origin.z) * k };
      facing = p.side;
    } else if (elapsed <= p.outMs + p.holdMs) {
      target = out;
      facing = p.side;
    } else if (elapsed <= p.outMs + p.holdMs + p.backMs) {
      const k = p.backMs <= 0 ? 1 : (elapsed - p.outMs - p.holdMs) / p.backMs;
      target = { x: out.x + (p.origin.x - out.x) * k, z: out.z + (p.origin.z - out.z) * k };
      facing = { x: -p.side.x, z: -p.side.z };
    } else {
      target = p.origin;
      facing = p.side;
    }
    turnToward(Math.atan2(-facing.x, -facing.z), 900, dt);
    const before = pos;
    pos = stepToward(pos, target, WALK_SPEED * dt);
    return Math.hypot(pos.x - before.x, pos.z - before.z) > 1e-4;
  }

  function due(now: number, last: number, everyMs: number): boolean {
    return now - last >= everyMs;
  }

  function tick(action: Action | null, ctx: ExecutorContext): ExecResult {
    const dt = clamp(ctx.dt, 0, 0.25);
    let tryingToMove = false;
    let tracer: ExecResult['tracer'] = null;
    let hit: ShotHit | null = null;
    let ability: ExecResult['ability'] = null;
    let plantHold = false;
    let defuseHold = false;
    let buy: string | null = null;

    switch (action?.kind) {
      case 'hold': {
        if (dist(pos, action.pos) > 0.45) {
          tryingToMove = advance(action.pos, WALK_SPEED, dt, ctx.now);
        } else {
          turnToward(action.watchYaw, 360, dt);
        }
        break;
      }
      case 'peek_wide':
      case 'peek_jiggle': {
        tryingToMove = runPeek(action, dt, ctx.now);
        break;
      }
      case 'fight': {
        const out = combat.update({
          now: ctx.now,
          selfPos: { x: pos.x, y: 0, z: pos.z },
          visible: ctx.visible,
          weapon: (ctx.world.self()?.weapon ?? 'vandal') as Weapon,
          moving: false,
          boxes: ctx.boxes,
          engage: true,
          targetName: action.target,
          skill: ctx.skill,
          rng: ctx.rng,
        });
        yaw = out.yaw;
        pitch = out.pitch;
        tracer = out.tracer;
        hit = out.hit;
        break;
      }
      case 'push':
      case 'retreat':
      case 'rotate': {
        const wp = zoneWaypoint(action.zone);
        const leader = ctx.followName ? ctx.world.byName(ctx.followName) : null;
        if (leader && leader.alive && action.kind === 'push') {
          // Keep the 2-4 m band: stop too close, walk inside the band, run when
          // the teammate pulls away. Never overtake them on the way to the zone.
          const gap = dist(pos, { x: leader.pos.x, z: leader.pos.z });
          if (gap < FOLLOW_MIN_M) {
            turnToward(Math.atan2(-(leader.pos.x - pos.x), -(leader.pos.z - pos.z)), 360, dt);
            break;
          }
          if (wp && dist(pos, wp) > 0.6) {
            tryingToMove = advance(wp, gap <= FOLLOW_MAX_M ? WALK_SPEED : RUN_SPEED, dt, ctx.now);
          } else if (wp) {
            turnToward(Math.atan2(-(wp.x - pos.x), -(wp.z - pos.z)), 360, dt);
          }
          break;
        }
        if (wp && dist(pos, wp) > 0.6) tryingToMove = advance(wp, RUN_SPEED, dt, ctx.now);
        else if (wp) turnToward(Math.atan2(-(wp.x - pos.x), -(wp.z - pos.z)), 360, dt);
        break;
      }
      case 'use_ability': {
        if (action.slot === 'E' && !abilityUsed) {
          const at = action.at ?? (() => {
            const dir = yawToDir(yaw);
            return { x: clamp(pos.x + dir.x * 8, -33, 33), z: clamp(pos.z + dir.z * 8, -33, 33) };
          })();
          ability = { type: 'smoke', at: [at.x, at.z] };
          abilityUsed = true;
        }
        break;
      }
      case 'plant': {
        const site = zoneWaypoint(siteZoneId(action.site));
        if (!inSite(action.site, pos) && site) {
          tryingToMove = advance(site, WALK_SPEED, dt, ctx.now);
        } else if (inSite(action.site, pos) && due(ctx.now, plantHoldAt, 250)) {
          plantHold = true;
          plantHoldAt = ctx.now;
        }
        break;
      }
      case 'defuse': {
        const sp = ctx.world.spike;
        if (!sp.planted || !sp.pos) break;
        if (dist(pos, sp.pos) > 2.8) {
          tryingToMove = advance(sp.pos, RUN_SPEED, dt, ctx.now);
        } else if (due(ctx.now, defuseHoldAt, 250)) {
          defuseHold = true;
          defuseHoldAt = ctx.now;
        }
        break;
      }
      case 'buy': {
        if (action.item !== lastBuy) {
          buy = action.item;
          lastBuy = action.item;
        }
        break;
      }
      case undefined:
        break;
    }

    if (action?.kind !== 'fight') combat.setView(yaw, pitch);
    if (action?.kind !== 'peek_wide' && action?.kind !== 'peek_jiggle') peek = null;

    const move: MovePayload = {
      x: pos.x,
      y: 0,
      z: pos.z,
      yaw,
      pitch,
      moving: tryingToMove,
      crouch: false,
    };
    return { move, tracer, hit, ability, plantHold, defuseHold, buy, tryingToMove };
  }

  return {
    sync(self) {
      const jump = Math.hypot(self.pos.x - pos.x, self.pos.z - pos.z);
      if (!ready || jump > 1.5) {
        pos = { x: self.pos.x, z: self.pos.z };
        yaw = Number.isFinite(self.yaw) ? self.yaw : 0;
        pitch = Number.isFinite(self.pitch) ? self.pitch : 0;
        combat.setView(yaw, pitch);
        path = [];
        pathKey = '';
        peek = null;
        ready = true;
      }
    },
    tick,
    resetRound() {
      abilityUsed = false;
      plantHoldAt = 0;
      defuseHoldAt = 0;
      lastBuy = '';
      peek = null;
      path = [];
      pathKey = '';
      combat.reset();
    },
    position: () => ({ ...pos }),
    view: () => ({ yaw, pitch }),
    abilityUsed: () => abilityUsed,
  };
}

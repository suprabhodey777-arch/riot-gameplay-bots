/**
 * Team play on top of the ghost/default decision. The bot still plays its own
 * clone (or the default bot); this module only bends that choice toward the
 * team, and only when the bot is not already busy: a fight, a plant or a defuse
 * is never touched, and no rule fires while an enemy is in view.
 *
 * The four rules, in priority order, are the usual team behaviours of a tactical
 * shooter: rotate when the spike goes down somewhere else, trade a teammate who
 * just died next to you, follow a teammate's push, and hold a different angle
 * from a teammate holding next to you.
 *
 * `buildTeamContext` is what makes this honest: it reads only the bot's own
 * world (snapshots, relayed teammate traces, its own perception) and never
 * invents an enemy position. `teamAdjust` itself is pure.
 */
import type { Action, ActionKind, SiteId, Situation, Vec2 } from '../contracts.ts';
import { loadZones, zoneAt } from './nav.ts';
import type { PerceptionUpdate } from './view.ts';
import type { World } from './world.ts';

/** How long after a teammate's death a trade still makes sense. */
export const TRADE_WINDOW_MS = 4000;
/** Only trade a death this close; farther away the bot cannot arrive in time. */
export const TRADE_RANGE_M = 25;
/** A push is only followed while the teammate is this close. */
export const FOLLOW_RANGE_M = 15;
/** The band the executor keeps behind a followed teammate. */
export const FOLLOW_MIN_M = 2;
export const FOLLOW_MAX_M = 4;
/** A teammate holding this close means this bot should take a different angle. */
export const HOLD_MATE_RANGE_M = 8;
/** The minimum angle between two teammates' watch directions. */
export const HOLD_ANGLE_SEP_RAD = Math.PI / 3;
/** Displacement over the last second that counts as walking, not standing. */
const HUMAN_MOVE_M = 0.5;
/** Window used to infer a human teammate's direction. */
const HUMAN_WINDOW_MS = 1000;

export const TEAM_RULES = {
  rotateOnPlant: 'rotate-on-plant',
  trade: 'trade',
  followPush: 'follow-push',
  holdOtherAngle: 'hold-other-angle',
} as const;

export type TeamRule = (typeof TEAM_RULES)[keyof typeof TEAM_RULES];

export interface TeamMate {
  name: string;
  pos: Vec2;
  alive: boolean;
  /** The teammate bot's last traced action kind; null for humans and unknown. */
  actionKind: ActionKind | null;
  /** The zone that action points at, when it points at one. */
  targetZone: string | null;
  /** Facing from the snapshot, the teammate's watch direction while holding. */
  watchYaw: number;
  /** Moving toward `targetZone` (traced push/rotate, or inferred human movement). */
  pushing: boolean;
  /** Standing on an angle (traced hold/peek, or a human who is not moving). */
  holding: boolean;
}

export interface TeamDeath {
  /** The teammate who died. */
  name: string;
  killer: string;
  /** Where they died, from the kill event. */
  pos: Vec2;
  /** Where this bot itself last saw the killer, or null when it never did. */
  killerPos: Vec2 | null;
  atMs: number;
}

export interface TeamContext {
  now: number;
  teammates: TeamMate[];
  deaths: TeamDeath[];
  spike: {
    planted: boolean;
    site: SiteId | null;
    /** The callout zone of the planted site, e.g. `a-site`. */
    siteZone: string | null;
  };
  selfHasSpike: boolean;
}

export interface TeamDecision {
  action: Action;
  reason: string;
  teamRule: string | null;
}

const PROTECTED = new Set<ActionKind>(['fight', 'plant', 'defuse']);

const dist = (a: Vec2, b: Vec2): number => Math.hypot(a.x - b.x, a.z - b.z);
const wrap = (a: number): number => Math.atan2(Math.sin(a), Math.cos(a));

export function siteZoneOf(site: SiteId): string {
  return site === 'A' ? 'a-site' : 'b-site';
}

/** True when `zoneId` is the site callout or one of that site's approach callouts. */
function inSiteZones(siteZone: string, zoneId: string): boolean {
  return zoneId === siteZone || zoneId.startsWith(`${siteZone.slice(0, 1)}-`);
}

/** The callout a traced action is working on, when it names one. */
function traceZone(action: Action): string | null {
  switch (action.kind) {
    case 'hold':
      return zoneAt(action.pos);
    case 'peek_wide':
    case 'peek_jiggle':
      return zoneAt(action.corner);
    case 'push':
    case 'retreat':
    case 'rotate':
      return action.zone;
    case 'plant':
    case 'defuse':
      return siteZoneOf(action.site);
    default:
      return null;
  }
}

/** The zone a walker is heading for: the waypoint most aligned with their motion. */
function zoneAlong(from: Vec2, motion: Vec2): string | null {
  const len = Math.hypot(motion.x, motion.z);
  if (len < 1e-6) return null;
  const ux = motion.x / len;
  const uz = motion.z / len;
  let best: { id: string; cos: number } | null = null;
  for (const zone of loadZones().zones) {
    const dx = zone.waypoint.x - from.x;
    const dz = zone.waypoint.z - from.z;
    const d = Math.hypot(dx, dz);
    if (d < 1) continue;
    const cos = (dx * ux + dz * uz) / d;
    if (!best || cos > best.cos) best = { id: zone.id, cos };
  }
  return best && best.cos >= 0.7 ? best.id : null;
}

/**
 * Everything the team rules may use, from the bot's own world. Teammate bots
 * speak through their relayed `trace` events; a human teammate is read from how
 * they moved over the last second. A teammate's death keeps only where it
 * happened; the killer's position comes from this bot's own eyes, never from the
 * kill event.
 */
export function buildTeamContext(
  world: World,
  selfName: string,
  now: number,
  seen: PerceptionUpdate['lastSeen'] = {},
): TeamContext {
  const spike = world.spike;
  const self = world.byName(selfName);
  if (!self) {
    const site = spike.planted ? spike.site : null;
    return {
      now,
      teammates: [],
      deaths: [],
      spike: { planted: spike.planted, site, siteZone: site ? siteZoneOf(site) : null },
      selfHasSpike: false,
    };
  }

  const teammates: TeamMate[] = [];
  const mateNames = new Set<string>();
  for (const p of world.players) {
    if (p.name === selfName || p.team !== self.team) continue;
    mateNames.add(p.name);

    let actionKind: ActionKind | null = null;
    let targetZone: string | null = null;
    let pushing = false;
    let holding = false;
    const trace = p.alive ? world.traceFor(p.name, now) : null;
    if (trace) {
      actionKind = trace.action.kind;
      targetZone = traceZone(trace.action);
      pushing = actionKind === 'push' || actionKind === 'rotate';
      holding = actionKind === 'hold' || actionKind === 'peek_wide' || actionKind === 'peek_jiggle';
    } else if (p.alive) {
      const motion = world.recentMotion(p.name, now, HUMAN_WINDOW_MS);
      if (motion && Math.hypot(motion.x, motion.z) >= HUMAN_MOVE_M) {
        pushing = true;
        targetZone = zoneAlong({ x: p.pos.x, z: p.pos.z }, motion);
      } else {
        holding = true;
      }
    }

    teammates.push({
      name: p.name,
      pos: { x: p.pos.x, z: p.pos.z },
      alive: p.alive,
      actionKind,
      targetZone,
      watchYaw: Number.isFinite(p.yaw) ? p.yaw : 0,
      pushing,
      holding,
    });
  }

  const deaths: TeamDeath[] = [];
  for (const death of world.recentDeaths(now, TRADE_WINDOW_MS)) {
    if (!mateNames.has(death.name)) continue;
    const killerSeen = seen[death.killer];
    deaths.push({
      name: death.name,
      killer: death.killer,
      pos: { ...death.pos },
      killerPos: killerSeen ? { x: killerSeen.pos.x, z: killerSeen.pos.z } : null,
      atMs: death.atMs,
    });
  }

  const site = spike.planted ? spike.site : null;
  return {
    now,
    teammates,
    deaths,
    spike: { planted: spike.planted, site, siteZone: site ? siteZoneOf(site) : null },
    selfHasSpike: spike.holder === selfName,
  };
}

const nearest = (mates: TeamMate[], from: Vec2): TeamMate | null => {
  let best: { mate: TeamMate; gap: number } | null = null;
  for (const mate of mates) {
    if (!mate.alive) continue;
    const gap = dist(from, mate.pos);
    if (!best || gap < best.gap) best = { mate, gap };
  }
  return best ? best.mate : null;
};

/** The nearest teammate pushing into `zone` (any zone when null) within range. */
export function followMateFor(selfPos: Vec2, ctx: TeamContext, zone: string | null = null): TeamMate | null {
  let best: { mate: TeamMate; gap: number } | null = null;
  for (const mate of ctx.teammates) {
    if (!mate.alive || !mate.pushing || !mate.targetZone) continue;
    if (zone != null && mate.targetZone !== zone) continue;
    const gap = dist(selfPos, mate.pos);
    if (gap > FOLLOW_RANGE_M) continue;
    if (!best || gap < best.gap) best = { mate, gap };
  }
  return best ? best.mate : null;
}

/** The nearest teammate standing on an angle within `HOLD_MATE_RANGE_M`. */
function holdingMateFor(selfPos: Vec2, ctx: TeamContext): TeamMate | null {
  const close = ctx.teammates.filter((mate) => mate.alive && mate.holding && dist(selfPos, mate.pos) <= HOLD_MATE_RANGE_M);
  return nearest(close, selfPos);
}

/**
 * Bend the base decision toward the team. Returns the base decision untouched
 * (`teamRule: null`) when no rule applies, or when the bot is fighting, planting,
 * defusing, or can see an enemy.
 */
export function teamAdjust(base: { action: Action; reason: string }, s: Situation, ctx: TeamContext): TeamDecision {
  if (PROTECTED.has(base.action.kind) || s.enemyVisible) {
    return { action: base.action, reason: base.reason, teamRule: null };
  }

  // 1. Rotate on plant: the spike is down somewhere else and this defender is not there.
  if (s.side === 'defense' && ctx.spike.planted && ctx.spike.siteZone) {
    const zone = ctx.spike.siteZone;
    if (!inSiteZones(zone, s.selfZone)) {
      const plantedBy = ctx.teammates.find((mate) => mate.actionKind === 'plant' && mate.targetZone === zone);
      const alreadyThere = ctx.teammates.find((mate) => mate.alive && zoneAt(mate.pos) === zone);
      const mate = plantedBy ?? alreadyThere ?? nearest(ctx.teammates, s.selfPos);
      const site = ctx.spike.site ?? '?';
      return {
        action: { kind: 'rotate', zone },
        reason: mate
          ? `spike planted at ${site}, rotating to ${zone} to join ${mate.name}`
          : `spike planted at ${site}, rotating to ${zone}`,
        teamRule: TEAM_RULES.rotateOnPlant,
      };
    }
  }

  // 2. Trade: a teammate just died close by, so push where it happened.
  let trade: TeamDeath | null = null;
  let tradeGap = 0;
  for (const death of ctx.deaths) {
    if (ctx.now - death.atMs > TRADE_WINDOW_MS) continue;
    const gap = dist(s.selfPos, death.pos);
    if (gap > TRADE_RANGE_M) continue;
    if (!zoneAt(death.pos)) continue;
    if (trade && death.atMs <= trade.atMs) continue;
    trade = death;
    tradeGap = gap;
  }
  if (trade) {
    const zone = zoneAt(trade.pos);
    if (zone) {
      const age = (ctx.now - trade.atMs) / 1000;
      const sawKiller = trade.killerPos ? `, I last saw ${trade.killer} there` : '';
      return {
        action: { kind: 'push', zone },
        reason: `${trade.name} died ${age.toFixed(1)} s ago ${tradeGap.toFixed(0)} m away${sawKiller}, pushing ${zone} to trade`,
        teamRule: TEAM_RULES.trade,
      };
    }
  }

  // 3. Follow a push: stay in the 2-4 m band behind a teammate going somewhere.
  const follow = followMateFor(s.selfPos, ctx);
  if (follow && follow.targetZone) {
    const gap = dist(s.selfPos, follow.pos);
    return {
      action: { kind: 'push', zone: follow.targetZone },
      reason: `${follow.name} is pushing ${follow.targetZone} ${gap.toFixed(0)} m ahead, following 2-4 m behind`,
      teamRule: TEAM_RULES.followPush,
    };
  }

  // 4. Hold the other angle: two teammates on adjacent angles must not overlap.
  if (base.action.kind === 'hold') {
    const mate = holdingMateFor(s.selfPos, ctx);
    if (mate) {
      const separation = wrap(base.action.watchYaw - mate.watchYaw);
      if (Math.abs(separation) < HOLD_ANGLE_SEP_RAD) {
        const sign = separation >= 0 ? 1 : -1;
        const watchYaw = wrap(mate.watchYaw + sign * HOLD_ANGLE_SEP_RAD);
        return {
          action: { kind: 'hold', pos: { ...base.action.pos }, watchYaw },
          reason: `${mate.name} is holding ${dist(s.selfPos, mate.pos).toFixed(0)} m away, watching 60 deg off their angle`,
          teamRule: TEAM_RULES.holdOtherAngle,
        };
      }
    }
  }

  return { action: base.action, reason: base.reason, teamRule: null };
}

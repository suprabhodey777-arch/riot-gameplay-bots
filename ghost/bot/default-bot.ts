/**
 * The default bot: the comparison baseline, and the fallback whenever the clone
 * has no close recording. It is a pure `decide(situation, memory)` function so
 * every rule can be tested with a hand-built `Situation`; the runtime fills the
 * memory from the world and the perception update before calling it.
 */
import type { Action, SiteId, Situation, Vec2, Zone } from '../contracts.ts';
import { blocked, clearPath, loadZones, zoneAt } from './nav.ts';

const zones = loadZones();

export interface DefaultBotMemory {
  /** Picks the site to attack and the side to jiggle; stable for the bot's life. */
  seed: number;
  /** The nearest enemy the runtime's perception currently reports, if any. */
  visibleTarget: string | null;
  /** Where that enemy was last seen, so a smoke can land on it. */
  lastSeenPos: Vec2 | null;
  spikeSite: SiteId | null;
  spikePos: Vec2 | null;
  /**
   * `Situation.spikeState === 'carried'` says the spike is in play, not who has
   * it. The runtime sets this from `world.spike.holder`; only the holder plants.
   */
  selfHasSpike: boolean;
  /** One smoke per round. */
  smokeUsed: boolean;
  /** `Situation.roundTime` when the current hold began (it counts down). */
  holdStartedRoundTime: number | null;
  /** One jiggle per hold, so the bot does not loop between hold and peek. */
  peekDone: boolean;
}

/**
 * `selfHasSpike` defaults to true so a pure `decide()` call with no world still
 * behaves like the bot that was handed the spike; the runtime always overwrites
 * it from the world before deciding.
 */
export function createDefaultMemory(seed: number, selfHasSpike = true): DefaultBotMemory {
  return {
    seed,
    visibleTarget: null,
    lastSeenPos: null,
    spikeSite: null,
    spikePos: null,
    selfHasSpike,
    smokeUsed: false,
    holdStartedRoundTime: null,
    peekDone: false,
  };
}

/** A stable small integer from a bot name, so two bots do not mirror each other. */
export function seedFromName(name: string): number {
  let h = 0;
  for (const ch of name) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  return h;
}

const SITES: SiteId[] = ['A', 'B'];
const siteChoice = (seed: number): SiteId => SITES[Math.abs(seed) % SITES.length];
const siteZoneId = (site: SiteId): string => (site === 'A' ? 'a-site' : 'b-site');

function siteZone(site: SiteId): Zone {
  const id = siteZoneId(site);
  const zone = zones.zones.find((z) => z.id === id);
  if (!zone) throw new Error(`default bot: no zone ${id} in ghost/map/zones.json`);
  return zone;
}

const dist = (a: Vec2, b: Vec2): number => Math.hypot(a.x - b.x, a.z - b.z);

/** Yaw that looks from `from` toward `to` (yaw 0 faces -z). */
function yawToward(from: Vec2, to: Vec2): number {
  return Math.atan2(-(to.x - from.x), -(to.z - from.z));
}

function holdFor(site: SiteId): { pos: Vec2; watchYaw: number } {
  const zone = siteZone(site);
  const pos = { ...zone.waypoint };
  // Watch the site entrance on the south edge, where attackers come from.
  const entrance: Vec2 = { x: zone.waypoint.x, z: zone.rect.z1 };
  const watchYaw = dist(pos, entrance) < 1e-6 ? 0 : yawToward(pos, entrance);
  return { pos, watchYaw };
}

function retreatZone(situation: Situation): string {
  const seen = situation.enemyLastSeenZone;
  if (seen) {
    if (seen === 'a-site' || seen.startsWith('a-')) return 'b-site';
    if (seen === 'b-site' || seen.startsWith('b-')) return 'a-site';
  }
  return situation.side === 'attack' ? 'attacker-spawn' : 'defense-spawn';
}

/** The nearest walkable corner of the zone, for a jiggle peek. */
function nearestCorner(zoneId: string, from: Vec2): Vec2 {
  const zone = zones.zones.find((z) => z.id === zoneId) ?? siteZone('A');
  const r = zone.rect;
  const corners: Vec2[] = [
    { x: r.x1, z: r.z1 },
    { x: r.x2, z: r.z1 },
    { x: r.x1, z: r.z2 },
    { x: r.x2, z: r.z2 },
  ];
  const sorted = [...corners].sort((a, b) => dist(from, a) - dist(from, b));
  for (const corner of sorted) {
    if (!blocked(corner) && clearPath(from, corner)) return corner;
  }
  return sorted[0];
}

function areaClear(situation: Situation): boolean {
  return !situation.enemyVisible && (situation.sinceSeen == null || situation.sinceSeen >= 2);
}

/**
 * Hold `pos` at `watchYaw`; after 8 s of quiet the bot jiggles the nearest
 * corner once. `Situation.roundTime` counts down, so elapsed time is
 * `start - now`.
 */
function holdOrPeek(
  pos: Vec2,
  watchYaw: number,
  situation: Situation,
  mem: DefaultBotMemory,
  reason: string,
): { action: Action; reason: string } {
  if (mem.holdStartedRoundTime == null || mem.holdStartedRoundTime < situation.roundTime) {
    mem.holdStartedRoundTime = situation.roundTime;
    mem.peekDone = false;
  }
  const held = mem.holdStartedRoundTime - situation.roundTime;
  const quiet = !situation.enemyVisible && (situation.sinceSeen == null || situation.sinceSeen >= 8);
  if (quiet && held >= 8 && !mem.peekDone) {
    mem.peekDone = true;
    const corner = nearestCorner(situation.selfZone, situation.selfPos);
    return {
      action: { kind: 'peek_jiggle', corner, dir: mem.seed % 2 === 0 ? 'left' : 'right' },
      reason: `held ${situation.selfZone} for ${held.toFixed(0)} s with no contact, jiggling the nearest corner`,
    };
  }
  return { action: { kind: 'hold', pos, watchYaw }, reason };
}

/**
 * One decision with a one-line reason. Order matters: a fight always wins, then
 * a low-hp retreat, then the objective (defuse, plant), utility, and finally the
 * role's hold / push behavior.
 */
export function decide(situation: Situation, mem: DefaultBotMemory): { action: Action; reason: string } {
  if (situation.enemyVisible) {
    mem.holdStartedRoundTime = null;
    mem.peekDone = false;
    const target = mem.visibleTarget ?? 'enemy';
    const bearing = situation.enemyRel ? `${situation.enemyRel.bearing.toFixed(2)} rad` : 'no bearing';
    return {
      action: { kind: 'fight', target },
      reason: `enemy ${target} in view (${bearing}), fighting`,
    };
  }

  if (situation.selfHp < 30) {
    mem.holdStartedRoundTime = null;
    const zone = retreatZone(situation);
    return {
      action: { kind: 'retreat', zone },
      reason: `hp ${situation.selfHp} is below 30, falling back to ${zone} away from the last sighting`,
    };
  }

  if (situation.side === 'defense' && situation.spikeState === 'planted' && areaClear(situation)) {
    mem.holdStartedRoundTime = null;
    const site = mem.spikeSite ?? (mem.spikePos ? (zoneAt(mem.spikePos)?.startsWith('a') ? 'A' : 'B') : 'A');
    return {
      action: { kind: 'defuse', site },
      reason: `spike planted at ${site} and no enemy in sight, defusing`,
    };
  }

  if (situation.side === 'attack' && situation.spikeState === 'carried' && mem.selfHasSpike) {
    mem.holdStartedRoundTime = null;
    if (situation.selfZone === 'a-site' || situation.selfZone === 'b-site') {
      const site: SiteId = situation.selfZone === 'a-site' ? 'A' : 'B';
      return { action: { kind: 'plant', site }, reason: `on site ${site} with the spike, planting` };
    }
    const site = siteChoice(mem.seed);
    return {
      action: { kind: 'push', zone: siteZoneId(site) },
      reason: `carrying the spike, pushing site ${site} to plant`,
    };
  }

  if (
    situation.sinceSeen != null &&
    situation.sinceSeen < 5 &&
    !mem.smokeUsed &&
    situation.abilitiesReady.E
  ) {
    mem.smokeUsed = true;
    const at = mem.lastSeenPos;
    return {
      action: { kind: 'use_ability', slot: 'E', at: at ? { ...at } : null },
      reason: `enemy seen ${situation.sinceSeen.toFixed(1)} s ago and not in view, smoking the last-seen spot`,
    };
  }

  if (situation.side === 'defense') {
    const site = siteChoice(mem.seed);
    const hold = holdFor(site);
    return holdOrPeek(
      hold.pos,
      hold.watchYaw,
      situation,
      mem,
      `holding a fixed angle on site ${site} at ${situation.selfZone}, no enemy in view`,
    );
  }

  if (situation.side === 'attack') {
    if (situation.spikeState === 'planted') {
      const site = mem.spikeSite ?? (mem.spikePos ? (zoneAt(mem.spikePos)?.startsWith('a') ? 'A' : 'B') : siteChoice(mem.seed));
      if (mem.spikePos && dist(situation.selfPos, mem.spikePos) > 12) {
        mem.holdStartedRoundTime = null;
        return {
          action: { kind: 'push', zone: siteZoneId(site) },
          reason: `spike is down at site ${site}, regrouping on it (${dist(situation.selfPos, mem.spikePos).toFixed(0)} m)`,
        };
      }
      const watchYaw = mem.spikePos ? yawToward(situation.selfPos, mem.spikePos) : 0;
      return holdOrPeek(
        { ...situation.selfPos },
        watchYaw,
        situation,
        mem,
        `spike is planted, watching it from ${situation.selfZone}`,
      );
    }
    if (situation.spikeState === 'dropped' && mem.spikePos) {
      const zone = zoneAt(mem.spikePos) ?? siteZoneId(siteChoice(mem.seed));
      mem.holdStartedRoundTime = null;
      return {
        action: { kind: 'push', zone },
        reason: `spike dropped in ${zone}, pushing to recover it`,
      };
    }
    const site = siteChoice(mem.seed);
    mem.holdStartedRoundTime = null;
    return {
      action: { kind: 'push', zone: siteZoneId(site) },
      reason: `attacking site ${site} with the team`,
    };
  }

  return holdOrPeek(
    { ...situation.selfPos },
    0,
    situation,
    mem,
    `holding ${situation.selfZone}, no role objective left`,
  );
}

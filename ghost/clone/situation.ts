/**
 * The one function that turns "what is going on" into a `Situation` (contract 2).
 * Live play and training both call `buildSituation`: the bot runtime fills a
 * `WorldView` from server snapshots, the cloning pipeline fills one by replaying a
 * log. Keeping a single builder means the ghost is trained on exactly what it sees.
 */
import type { AbilitySlot, LogEvent, Situation, Team, Vec2, Vec3 } from '../contracts.ts';
import type { PerceptionUpdate } from '../bot/view.ts';
import { zoneAt } from '../bot/nav.ts';

/** How far back "recent" damage and kills reach, in seconds. */
export const RECENT_S = 3;

export interface WorldPlayer {
  name: string;
  team: Team;
  pos: Vec3;
  yaw: number;
  pitch: number;
  hp: number;
  armor: number;
  alive: boolean;
  weapon: string;
}

export interface WorldView {
  /** Seconds since the round started, the same clock as `LogEvent.t`. */
  t: number;
  /** Seconds left in the current phase. */
  timeLeft: number;
  spike: { holder: string | null; planted: boolean; dropped: Vec2 | null };
  players: WorldPlayer[];
  /** Events from the last RECENT_S seconds or more; older ones are ignored. */
  recent: LogEvent[];
  /** Abilities still available to each player; missing = all ready. */
  abilities?: Record<string, Partial<Record<AbilitySlot, boolean>>>;
}

/**
 * `perception` is the bot's own fog of war (`createPerception` in bot/view.ts),
 * updated by the caller with the same clock in milliseconds as `nowMs`.
 */
export function buildSituation(view: WorldView, selfName: string, perception: PerceptionUpdate, nowMs: number): Situation {
  const self = view.players.find((p) => p.name === selfName);
  if (!self) throw new Error(`buildSituation: no player named ${selfName}`);
  const selfPos: Vec2 = { x: self.pos.x, z: self.pos.z };

  // Nearest enemy the bot actually sees (after its reaction delay).
  let enemyRel: Situation['enemyRel'] = null;
  for (const v of perception.visible) {
    const dx = v.pos.x - self.pos.x, dz = v.pos.z - self.pos.z;
    const dist = Math.hypot(dx, dz);
    if (enemyRel && dist >= enemyRel.dist) continue;
    // Bearing relative to facing: yaw 0 faces -z (bot/view.ts yawToDir), positive = to the left.
    const toward = Math.atan2(-dx, -dz);
    enemyRel = { dist, bearing: wrapAngle(toward - self.yaw) };
  }

  // Most recent sighting of any enemy.
  let last: { zone: string | null; t: number } | null = null;
  for (const s of Object.values(perception.lastSeen)) if (!last || s.t > last.t) last = { zone: s.zone, t: s.t };

  const since = view.t - RECENT_S;
  const recent = view.recent.filter((e) => e.t >= since && e.t <= view.t);
  const tookDamageRecent = recent.some((e) => e.type === 'damage' && e.victim === selfName);
  const gotKillRecent = recent.some((e) => e.type === 'kill' && e.killer === selfName);

  const ready = view.abilities?.[selfName] ?? {};
  const alive = (team: Team) => view.players.filter((p) => p.alive && p.team === team && p.name !== selfName).length;
  const enemyTeam: Team = self.team === 'attack' ? 'defense' : 'attack';

  return {
    selfPos,
    selfZone: zoneAt(selfPos) ?? 'unknown',
    selfHp: self.hp,
    selfArmor: self.armor,
    weapon: self.weapon,
    ammo: null,
    abilitiesReady: { Q: ready.Q ?? true, E: ready.E ?? true, X: ready.X ?? false },
    enemyVisible: perception.visible.length > 0,
    enemyRel,
    enemyLastSeenZone: last ? last.zone : null,
    sinceSeen: last ? Math.max(0, (nowMs - last.t) / 1000) : null,
    tookDamageRecent,
    gotKillRecent,
    side: self.team,
    spikeState: view.spike.planted ? 'planted' : view.spike.holder ? 'carried' : view.spike.dropped ? 'dropped' : 'none',
    roundTime: view.timeLeft,
    teammatesAlive: alive(self.team),
    enemiesAlive: view.players.filter((p) => p.alive && p.team === enemyTeam).length,
  };
}

export function wrapAngle(a: number): number {
  return Math.atan2(Math.sin(a), Math.cos(a));
}

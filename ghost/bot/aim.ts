/**
 * Aim and shot maths, ported from `game/public/js/game.js` (eye position, shoot
 * direction, `rayWalls`, `rayPlayer`, `fireOneShot` spread/damage rules) so the
 * bot resolves a shot exactly the way the browser client does. The server only
 * applies the `hit` the shooter claims, so this file is the bot's half of the
 * contract.
 */
import type { Box, Vec3, Weapon } from '../contracts.ts';
import { CHEST_HEIGHT } from './view.ts';
import { WEAPONS, type WeaponStats } from './weapons.ts';

/** Head sphere centre above the feet and its radius (`game.js:248-253`). */
export const HEAD_HEIGHT = 1.62;
export const HEAD_RADIUS = 0.32;
/** Body box: x +-0.38, y 0..1.45, z +-0.3 (`game.js:255-264`). */
export const BODY_HALF_X = 0.38;
export const BODY_HALF_Z = 0.3;
export const BODY_TOP = 1.45;

export const DEG = Math.PI / 180;

/** Camera height for a standing player; crouching is never used by the bot. */
export const SHOOT_EYE_HEIGHT = 1.62;

const wrap = (a: number): number => Math.atan2(Math.sin(a), Math.cos(a));

/** The unit vector a client looks along: `Euler(pitch, yaw, 0, 'YXZ')` on (0,0,-1). */
export function shootDir(yaw: number, pitch: number): Vec3 {
  const cp = Math.cos(pitch);
  return { x: -cp * Math.sin(yaw), y: Math.sin(pitch), z: -cp * Math.cos(yaw) };
}

/** Yaw and pitch that point from `origin` at `target` (inverse of `shootDir`). */
export function anglesTo(origin: Vec3, target: Vec3): { yaw: number; pitch: number } {
  const dx = target.x - origin.x;
  const dy = target.y - origin.y;
  const dz = target.z - origin.z;
  return { yaw: Math.atan2(-dx, -dz), pitch: Math.atan2(dy, Math.hypot(dx, dz)) };
}

/** Smallest angle between two directions, in degrees. */
export function angularDiffDeg(a: Vec3, b: Vec3): number {
  const la = Math.hypot(a.x, a.y, a.z);
  const lb = Math.hypot(b.x, b.y, b.z);
  if (la < 1e-9 || lb < 1e-9) return 180;
  const cos = (a.x * b.x + a.y * b.y + a.z * b.z) / (la * lb);
  return Math.acos(Math.max(-1, Math.min(1, cos))) / DEG;
}

/** The point on a player the bot aims at: head or chest. */
export function aimPoint(feet: Vec3, head: boolean): Vec3 {
  return { x: feet.x, y: feet.y + (head ? HEAD_HEIGHT : CHEST_HEIGHT), z: feet.z };
}

/** Distance to the nearest wall along `dir`, or `maxDist` (`game.js:226-243`). */
export function rayWalls(origin: Vec3, dir: Vec3, maxDist: number, boxes: Box[]): number {
  let best = maxDist;
  const invX = 1 / (dir.x || 1e-8);
  const invY = 1 / (dir.y || 1e-8);
  const invZ = 1 / (dir.z || 1e-8);
  for (const c of boxes) {
    const t1 = (c.min.x - origin.x) * invX;
    const t2 = (c.max.x - origin.x) * invX;
    const t3 = (c.min.y - origin.y) * invY;
    const t4 = (c.max.y - origin.y) * invY;
    const t5 = (c.min.z - origin.z) * invZ;
    const t6 = (c.max.z - origin.z) * invZ;
    const tmin = Math.max(Math.min(t1, t2), Math.min(t3, t4), Math.min(t5, t6));
    const tmax = Math.min(Math.max(t1, t2), Math.max(t3, t4), Math.max(t5, t6));
    if (tmax >= Math.max(tmin, 0) && tmin < best && tmin > 0) best = tmin;
  }
  // Floor at y = 0.02, as the client does.
  if (dir.y < -1e-4) {
    const t = (0.02 - origin.y) / dir.y;
    if (t > 0 && t < best) best = t;
  }
  return best;
}

export interface RayHit {
  dist: number;
  head: boolean;
}

/** Head sphere first, then the body box (`game.js:245-266`). */
export function rayPlayer(origin: Vec3, dir: Vec3, feet: Vec3, maxDist: number): RayHit | null {
  const hc: Vec3 = { x: feet.x, y: feet.y + HEAD_HEIGHT, z: feet.z };
  const ocx = hc.x - origin.x;
  const ocy = hc.y - origin.y;
  const ocz = hc.z - origin.z;
  const tProj = ocx * dir.x + ocy * dir.y + ocz * dir.z;
  if (tProj > 0 && tProj < maxDist) {
    const cx = origin.x + dir.x * tProj;
    const cy = origin.y + dir.y * tProj;
    const cz = origin.z + dir.z * tProj;
    if (Math.hypot(cx - hc.x, cy - hc.y, cz - hc.z) < HEAD_RADIUS) return { dist: tProj, head: true };
  }

  const minX = feet.x - BODY_HALF_X;
  const maxX = feet.x + BODY_HALF_X;
  const minY = feet.y;
  const maxY = feet.y + BODY_TOP;
  const minZ = feet.z - BODY_HALF_Z;
  const maxZ = feet.z + BODY_HALF_Z;
  const invX = 1 / (dir.x || 1e-8);
  const invY = 1 / (dir.y || 1e-8);
  const invZ = 1 / (dir.z || 1e-8);
  const t1 = (minX - origin.x) * invX;
  const t2 = (maxX - origin.x) * invX;
  const t3 = (minY - origin.y) * invY;
  const t4 = (maxY - origin.y) * invY;
  const t5 = (minZ - origin.z) * invZ;
  const t6 = (maxZ - origin.z) * invZ;
  const tmin = Math.max(Math.min(t1, t2), Math.min(t3, t4), Math.min(t5, t6));
  const tmax = Math.min(Math.max(t1, t2), Math.max(t3, t4), Math.max(t5, t6));
  if (tmax >= Math.max(tmin, 0) && tmin > 0 && tmin < maxDist) return { dist: tmin, head: false };
  return null;
}

/**
 * Spread from `fireOneShot` (`game.js:296-298`) with no ADS and no bloom growth:
 * moving is 1.6x, standing still 0.25x, plus a flat 0.004 for guns.
 */
export function spreadFor(stats: WeaponStats, moving: boolean): number {
  const movePenalty = moving ? 1.6 : 0.25;
  return stats.spread * movePenalty + (stats.melee ? 0 : 0.004);
}

/** A standard normal sample from a uniform rng (Box-Muller). */
export function gaussian(rng: () => number): number {
  const u = Math.max(rng(), 1e-12);
  const v = rng();
  return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v);
}

export interface ShotTarget {
  name: string;
  pos: Vec3;
}

export interface ShotHit {
  victim: string;
  damage: number;
  headshot: boolean;
  weapon: string;
}

export interface ShotResult {
  from: [number, number, number];
  dir: [number, number, number];
  hit: ShotHit | null;
}

export interface ShotOptions {
  origin: Vec3;
  yaw: number;
  pitch: number;
  weapon: Weapon;
  /** True when the shooter is walking; selects the moving spread penalty. */
  moving: boolean;
  /** Standard deviation of the extra aim error, in degrees. */
  aimErrorDeg: number;
  rng: () => number;
  boxes: Box[];
  /** The only bodies the shot may hit: enemies this bot can actually see. */
  targets: ShotTarget[];
}

/**
 * One trigger pull: add the skill's angular error, then the weapon spread, then
 * walk the ray through the map's colliders and the visible players. Damage and
 * falloff follow `fireOneShot` (`game.js:328-334`).
 */
export function resolveShot(opts: ShotOptions): ShotResult {
  const stats = WEAPONS[opts.weapon];
  if (!stats) throw new Error(`resolveShot: unknown weapon ${opts.weapon}`);

  const yaw = wrap(opts.yaw + gaussian(opts.rng) * opts.aimErrorDeg * DEG);
  const pitch = Math.max(-1.55, Math.min(1.55, opts.pitch + gaussian(opts.rng) * opts.aimErrorDeg * DEG));
  const dir = shootDir(yaw, pitch);
  const spread = spreadFor(stats, opts.moving);
  dir.x += (opts.rng() - 0.5) * spread * 2;
  dir.y += (opts.rng() - 0.5) * spread * 2;
  dir.z += (opts.rng() - 0.5) * spread * 2;
  const len = Math.hypot(dir.x, dir.y, dir.z) || 1;
  dir.x /= len;
  dir.y /= len;
  dir.z /= len;

  const maxDist = stats.range || 60;
  const wallDist = rayWalls(opts.origin, dir, maxDist, opts.boxes);
  let best: { target: ShotTarget; dist: number; head: boolean } | null = null;
  for (const target of opts.targets) {
    const h = rayPlayer(opts.origin, dir, target.pos, Math.min(wallDist, maxDist));
    if (h && (!best || h.dist < best.dist)) best = { target, dist: h.dist, head: h.head };
  }

  const result: ShotResult = {
    from: [opts.origin.x, opts.origin.y, opts.origin.z],
    dir: [dir.x, dir.y, dir.z],
    hit: null,
  };
  if (!best) return result;

  let dmg = stats.damage;
  if (!stats.sniper && !stats.melee && best.dist > 20) {
    dmg = Math.round(dmg * Math.max(0.6, 1 - (best.dist - 20) / 80));
  }
  result.hit = {
    victim: best.target.name,
    damage: best.head ? Math.round(dmg * stats.headMult) : dmg,
    headshot: best.head,
    weapon: opts.weapon,
  };
  return result;
}

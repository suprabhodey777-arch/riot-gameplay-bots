/**
 * What a bot may see. Sight is a 3D ray between eye and chest plus a view cone,
 * so the bot can only learn about enemies a player in its place could see.
 */
import type { Box, Vec3 } from '../contracts.ts';
import { distanceToSegment, segmentHitsBox3 } from './geometry.ts';
import { zoneAt } from './nav.ts';

/** Camera height while standing (`game/public/js/game.js:956`: `pos.y + 1.62`). */
export const EYE_HEIGHT = 1.62;

/** Enemy chest height above the feet, the point a sight check aims at. */
export const CHEST_HEIGHT = 1.2;

/** Horizontal field of view in degrees, centred on yaw. */
export const FOV_DEG = 103;

/** A smoke: a vertical cylinder that blocks sight through it. */
export interface Smoke {
  x: number;
  z: number;
  r: number;
}

export interface EnemyState {
  name: string;
  pos: Vec3;
  alive: boolean;
}

export interface Dir2 {
  x: number;
  z: number;
}

/**
 * Facing direction for a yaw value. The client moves forward as
 * `(-sin(yaw), -cos(yaw))` on x/z (`game/public/js/game.js:934-936`), i.e.
 * yaw 0 looks toward -z and yaw = PI looks toward +z.
 */
export function yawToDir(yaw: number): Dir2 {
  return { x: -Math.sin(yaw), z: -Math.cos(yaw) };
}

/** True when the straight 3D segment from eye to chest is neither walled nor smoked. */
export function lineOfSight(from: Vec3, to: Vec3, boxes: Box[], smokes: Smoke[]): boolean {
  for (const box of boxes) {
    if (segmentHitsBox3(from, to, box.min, box.max)) return false;
  }
  for (const smoke of smokes) {
    if (distanceToSegment({ x: smoke.x, z: smoke.z }, { x: from.x, z: from.z }, { x: to.x, z: to.z }) < smoke.r) {
      return false;
    }
  }
  return true;
}

/** True when `target` lies inside the horizontal view cone around yaw. */
export function inViewCone(selfPos: { x: number; z: number }, selfYaw: number, target: { x: number; z: number }, fovDeg = FOV_DEG): boolean {
  const tx = target.x - selfPos.x;
  const tz = target.z - selfPos.z;
  const len = Math.hypot(tx, tz);
  if (len < 1e-9) return true;
  const dir = yawToDir(selfYaw);
  const cos = (dir.x * tx + dir.z * tz) / len;
  const angle = Math.acos(Math.max(-1, Math.min(1, cos)));
  return angle <= (fovDeg * Math.PI) / 360;
}

export interface PerceptionOptions {
  /** How long an enemy must stay in sight before the bot reacts to it, in ms. */
  reactionMs: number;
}

export interface PerceptionUpdate {
  visible: { name: string; pos: Vec3 }[];
  /** The last position where each enemy was actually seen, with its callout. */
  lastSeen: Record<string, { pos: Vec3; zone: string | null; t: number }>;
}

export interface Perception {
  update(
    now: number,
    self: { pos: Vec3; yaw: number },
    enemies: EnemyState[],
    boxes: Box[],
    smokes: Smoke[],
  ): PerceptionUpdate;
}

/** Fog of war driven by the clock: an enemy is reported only after `reactionMs` of unbroken sight. */
export function createPerception(opts: PerceptionOptions): Perception {
  const since = new Map<string, number>();
  const lastSeen: PerceptionUpdate['lastSeen'] = {};

  return {
    update(now, self, enemies, boxes, smokes): PerceptionUpdate {
      const eye: Vec3 = { x: self.pos.x, y: self.pos.y + EYE_HEIGHT, z: self.pos.z };
      const visible: { name: string; pos: Vec3 }[] = [];

      for (const enemy of enemies) {
        if (!enemy.alive) {
          since.delete(enemy.name);
          delete lastSeen[enemy.name];
          continue;
        }
        const chest: Vec3 = { x: enemy.pos.x, y: enemy.pos.y + CHEST_HEIGHT, z: enemy.pos.z };
        const inSight =
          inViewCone(eye, self.yaw, chest) &&
          lineOfSight(eye, chest, boxes, smokes);

        if (!inSight) {
          since.delete(enemy.name);
          continue;
        }
        const startedAt = since.get(enemy.name) ?? now;
        since.set(enemy.name, startedAt);
        if (now - startedAt < opts.reactionMs) continue;

        visible.push({ name: enemy.name, pos: { ...enemy.pos } });
        lastSeen[enemy.name] = {
          pos: { ...enemy.pos },
          zone: zoneAt({ x: enemy.pos.x, z: enemy.pos.z }),
          t: now,
        };
      }

      return {
        visible,
        lastSeen: Object.fromEntries(
          Object.entries(lastSeen).map(([name, seen]) => [name, { pos: { ...seen.pos }, zone: seen.zone, t: seen.t }]),
        ),
      };
    },
  };
}

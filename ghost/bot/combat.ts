/**
 * The bot's trigger discipline: turn toward the visible enemy at the skill's
 * flick speed, and only pull the trigger once the crosshair is within twice the
 * skill's aim error. Enemy positions come from `PerceptionUpdate` only — this
 * module never reads the world snapshot for targets.
 */
import type { Box, Vec3, Weapon } from '../contracts.ts';
import { wrapAngle } from '../clone/situation.ts';
import { DEG, aimPoint, anglesTo, angularDiffDeg, resolveShot, shootDir, type ShotHit } from './aim.ts';
import type { SkillNumbers } from './policy-types.ts';
import { EYE_HEIGHT } from './view.ts';
import { WEAPONS } from './weapons.ts';

export interface CombatTarget {
  name: string;
  pos: Vec3;
}

export interface CombatInput {
  now: number;
  selfPos: Vec3;
  /** `PerceptionUpdate.visible`: the only enemies the bot may aim at. */
  visible: CombatTarget[];
  weapon: Weapon;
  moving: boolean;
  boxes: Box[];
  /** The chosen action is a fight; false only settles the view. */
  engage: boolean;
  /** The action's named target, when it has one. */
  targetName: string | null;
  skill: SkillNumbers;
  rng: () => number;
}

export interface CombatOutput {
  yaw: number;
  pitch: number;
  tracer: { from: [number, number, number]; dir: [number, number, number] } | null;
  hit: ShotHit | null;
  target: string | null;
  aimedAt: 'head' | 'chest' | null;
  /** True when the crosshair is within 2 x the skill's aim error. */
  aimed: boolean;
  reason: string;
}

export interface Combat {
  update(input: CombatInput): CombatOutput;
  setView(yaw: number, pitch: number): void;
  view(): { yaw: number; pitch: number };
  /** A fresh magazine at the start of a round. */
  reset(): void;
}

/**
 * Stateful across ticks: it owns the aim direction, the magazine and the reload
 * timer for the weapon currently in hand, so rpm, magazine and reload time all
 * behave like `tryFire` (`game.js:268-290`).
 */
export function createCombat(initial: { yaw: number; pitch: number }): Combat {
  let yaw = initial.yaw;
  let pitch = initial.pitch;
  let lastNow: number | null = null;
  let targetName: string | null = null;
  let aimHead = false;
  let ammoWeapon: Weapon | null = null;
  let ammo = 0;
  let reloadUntil = 0;
  let nextShot = 0;

  function syncWeapon(now: number, weapon: Weapon): void {
    if (ammoWeapon !== weapon) {
      ammoWeapon = weapon;
      const stats = WEAPONS[weapon];
      ammo = stats ? stats.mag : 0;
      reloadUntil = 0;
      nextShot = 0;
      targetName = null;
    }
    const stats = WEAPONS[weapon];
    if (stats && reloadUntil > 0 && now >= reloadUntil) {
      ammo = stats.mag;
      reloadUntil = 0;
    }
  }

  function update(input: CombatInput): CombatOutput {
    const dt = lastNow == null ? 0 : Math.min(0.25, Math.max(0, (input.now - lastNow) / 1000));
    lastNow = input.now;
    syncWeapon(input.now, input.weapon);
    const stats = WEAPONS[input.weapon];

    const out: CombatOutput = {
      yaw,
      pitch,
      tracer: null,
      hit: null,
      target: null,
      aimedAt: null,
      aimed: false,
      reason: input.engage ? 'no enemy in view' : 'not engaging',
    };
    if (!input.engage || input.visible.length === 0 || !stats) {
      targetName = null;
      return out;
    }

    const named = input.targetName ? input.visible.find((v) => v.name === input.targetName) : undefined;
    const target =
      named ??
      input.visible.reduce((a, b) =>
        Math.hypot(b.pos.x - input.selfPos.x, b.pos.z - input.selfPos.z) <
        Math.hypot(a.pos.x - input.selfPos.x, a.pos.z - input.selfPos.z)
          ? b
          : a,
      );

    if (target.name !== targetName) {
      targetName = target.name;
      aimHead = input.rng() < input.skill.headshotRate;
    }

    const eye: Vec3 = { x: input.selfPos.x, y: input.selfPos.y + EYE_HEIGHT, z: input.selfPos.z };
    const point = aimPoint(target.pos, aimHead);
    const want = anglesTo(eye, point);
    const maxTurn = input.skill.flickDegPerS * DEG * dt;
    const dy = wrapAngle(want.yaw - yaw);
    yaw = wrapAngle(yaw + Math.max(-maxTurn, Math.min(maxTurn, dy)));
    const dp = want.pitch - pitch;
    pitch = Math.max(-1.55, Math.min(1.55, pitch + Math.max(-maxTurn, Math.min(maxTurn, dp))));

    const facing = shootDir(yaw, pitch);
    const toPoint: Vec3 = { x: point.x - eye.x, y: point.y - eye.y, z: point.z - eye.z };
    const offDeg = angularDiffDeg(facing, toPoint);
    const aimed = offDeg <= 2 * input.skill.aimErrorDeg;

    let reason = `${target.name} ${offDeg.toFixed(1)}° off, ${aimed ? 'on target' : 'turning'}`;
    const reloading = reloadUntil > 0 && input.now < reloadUntil;
    const ready = input.now >= nextShot && !reloading && ammo > 0;

    if (aimed && ready) {
      const shot = resolveShot({
        origin: eye,
        yaw,
        pitch,
        weapon: input.weapon,
        moving: input.moving,
        aimErrorDeg: input.skill.aimErrorDeg,
        rng: input.rng,
        boxes: input.boxes,
        targets: input.visible,
      });
      out.tracer = { from: shot.from, dir: shot.dir };
      out.hit = shot.hit;
      ammo -= 1;
      nextShot = input.now + 60000 / stats.rpm;
      if (ammo <= 0) reloadUntil = input.now + stats.reload * 1000;
      reason = shot.hit
        ? `shot ${shot.hit.victim}${shot.hit.headshot ? ' in the head' : ' in the body'}`
        : `missed ${target.name}`;
    } else if (aimed && !ready) {
      reason = reloading
        ? `${target.name} in sights, reloading`
        : ammo <= 0
          ? `${target.name} in sights, out of ammo`
          : `${target.name} in sights, waiting ${Math.max(0, Math.round(nextShot - input.now))} ms`;
    }

    out.yaw = yaw;
    out.pitch = pitch;
    out.target = target.name;
    out.aimedAt = aimHead ? 'head' : 'chest';
    out.aimed = aimed;
    out.reason = reason;
    return out;
  }

  return {
    update,
    setView(y, p) {
      yaw = y;
      pitch = p;
    },
    view: () => ({ yaw, pitch }),
    reset() {
      const stats = ammoWeapon ? WEAPONS[ammoWeapon] : null;
      ammo = stats ? stats.mag : 0;
      reloadUntil = 0;
      nextShot = 0;
      targetName = null;
    },
  };
}

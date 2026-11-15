import { spreadDeg, WEAPONS, type WeaponId } from '../shared/weapons.ts';

/** A top-down projection of recoil: yaw is lateral, pitch contributes a small screen kick. */
export class WeaponAim {
  private bullet = 0;
  private lastS = -100;
  private id: WeaponId = 'lancer';
  private kick = { x: 0, y: 0 };
  cameraKick(timeS: number): { x: number; y: number } {
    const left = Math.max(0, 1 - (timeS - this.lastS) / WEAPONS[this.id].recoveryS);
    return { x: this.kick.x * left, y: this.kick.y * left };
  }
  shot(yaw: number, id: WeaponId, timeS: number, moving: boolean, walk: boolean, crouch: boolean, airborne: boolean, scoped: boolean): number {
    const w = WEAPONS[id];
    if (id !== this.id || timeS - this.lastS >= w.recoveryS) this.bullet = 0;
    this.id = id;
    const offset = w.spray[Math.min(this.bullet, w.spray.length - 1)];
    const jitter = Math.sin((this.bullet + 1) * 127.1 + timeS * 311.7) * spreadDeg(id, moving, walk, crouch, airborne, scoped);
    this.kick = { x: offset.yaw * 0.25, y: offset.pitch * 0.25 };
    this.lastS = timeS; this.bullet++;
    return yaw + (offset.yaw + offset.pitch * 0.15 + jitter) * Math.PI / 180;
  }
}

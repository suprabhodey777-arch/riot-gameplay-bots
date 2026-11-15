import type { AgentState } from './contract.ts';

export type WeaponId = 'pin' | 'hammer' | 'wasp' | 'brick' | 'lancer' | 'viper' | 'longshot';
export type ArmorId = 'light' | 'heavy';
export type Slot = 'primary' | 'secondary';
export type RoundType = 'pistol' | 'eco' | 'force' | 'full buy';
export interface Loadout { primary?: WeaponId | null; secondary?: WeaponId; armor?: ArmorId | null; credits?: number; armorPoints?: number }
export interface Weapon {
  name: string; class: string; price: number; head: number; body: number;
  fireIntervalS: number; mag: number; reloadS: number; pellets: number;
  spray: { pitch: number; yaw: number }[]; recoveryS: number; baseSpreadDeg: number;
  falloff: { beyondM: number; multiplier: number; head?: number } | null;
}
// Original offsets: asymmetric rise and drift, with distinct rhythm per weapon.
const pattern = (n: number, rise: number, drift: number, phase: number) => Array.from({ length: n }, (_, i) => ({
  pitch: Math.round((rise * Math.min(i + 1, 9) + Math.sin(i * 0.73 + phase) * rise * 0.3) * 1000) / 1000,
  yaw: Math.round(Math.sin(i * 0.61 + phase) * drift * Math.min(1, i / 5) * 1000) / 1000,
}));
const weapon = (name: string, cls: string, price: number, head: number, body: number, fireIntervalS: number, mag: number, reloadS: number, baseSpreadDeg: number, rise: number, drift: number, phase: number, recoveryS: number, falloff: Weapon['falloff'] = null, pellets = 1): Weapon =>
  ({ name, class: cls, price, head, body, fireIntervalS, mag, reloadS, baseSpreadDeg, spray: pattern(mag, rise, drift, phase), recoveryS, falloff, pellets });
export const WEAPONS: Record<WeaponId, Weapon> = {
  pin: weapon('Pin', 'sidearm', 0, 75, 26, 0.16, 12, 1.5, 0.8, 0.12, 0.05, 0.2, 0.16),
  hammer: weapon('Hammer', 'heavy pistol', 500, 150, 50, 0.40, 6, 2, 1.1, 0.3, 0.08, 0.5, 0.24, { beyondM: 15, multiplier: 1, head: 110 }),
  wasp: weapon('Wasp', 'SMG', 950, 70, 27, 0.075, 25, 2.2, 0.35, 0.06, 0.17, 1.3, 0.25, { beyondM: 15, multiplier: 0.8 }),
  brick: weapon('Brick', 'shotgun', 1800, 18, 12, 0.90, 5, 2.5, 1.5, 0.55, 0.2, 2.1, 0.45, { beyondM: 8, multiplier: 0.5 }, 8),
  lancer: weapon('Lancer', 'rifle', 2900, 150, 40, 0.15, 25, 2.5, 0.9, 0.17, 0.65, 0, 0.4),
  viper: weapon('Viper', 'rifle', 2900, 140, 38, 0.09, 30, 2.5, 0.7, 0.11, 0.38, 0.8, 0.32, { beyondM: 20, multiplier: 0.85 }),
  longshot: weapon('Longshot', 'sniper', 4700, 255, 150, 1.25, 5, 3.5, 5, 0.7, 0.12, 2.5, 0.65),
};
// Pistols kick once and reset between taps.
for (const id of ['pin', 'hammer'] as const) WEAPONS[id].spray = WEAPONS[id].spray.map(() => ({ ...WEAPONS[id].spray[0] }));
export const ARMOR = { light: { price: 400, points: 25 }, heavy: { price: 1000, points: 50 } };
export const DEFAULT_LOADOUT: Loadout = { primary: 'lancer', secondary: 'pin', armor: null, credits: 0 };
export const weaponSlot = (id: WeaponId): Slot => WEAPONS[id].class.includes('pistol') || id === 'pin' ? 'secondary' : 'primary';
export const equipped = (a: AgentState): WeaponId => a[a.slot] ?? a.secondary;
export const roundType = (round: number, average: number): RoundType => round === 1 || round === 7 ? 'pistol' : average < 2000 ? 'eco' : average < 3900 ? 'force' : 'full buy';
export function weaponDamage(id: WeaponId, part: 'head' | 'body', distance: number): number {
  const w = WEAPONS[id];
  return (w.falloff && distance > w.falloff.beyondM ? part === 'head' && w.falloff.head !== undefined ? w.falloff.head : w[part] * w.falloff.multiplier : w[part]);
}
export function spreadDeg(id: WeaponId, moving: boolean, walk: boolean, crouch: boolean, airborne: boolean, scoped: boolean): number {
  const base = id === 'longshot' && scoped && !moving && !airborne ? 0.1 : WEAPONS[id].baseSpreadDeg;
  return base * (airborne ? 6 : !moving ? (id === 'longshot' && !scoped ? 1 : 0) : crouch ? 0.5 : walk ? 1 : 4);
}

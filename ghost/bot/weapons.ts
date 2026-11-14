/**
 * The weapon numbers the bot needs, copied verbatim from
 * `game/public/js/config.js` (`WEAPONS`). `ghost/bot/weapons.test.ts` imports the
 * game file and asserts every field still matches, so this copy cannot drift.
 */
import type { Weapon } from '../contracts.ts';

export interface WeaponStats {
  name: string;
  price: number;
  damage: number;
  headMult: number;
  mag: number;
  reserve: number;
  auto: boolean;
  rpm: number;
  spread: number;
  reload: number;
  range: number;
  sniper?: boolean;
  melee?: boolean;
}

export const WEAPONS: Record<Weapon, WeaponStats> = {
  classic: { name: 'STUB', price: 0, damage: 26, headMult: 2.5, mag: 12, reserve: 36, auto: false, rpm: 400, spread: 0.025, reload: 1.6, range: 50 },
  sheriff: { name: 'WARDEN', price: 800, damage: 55, headMult: 2.6, mag: 6, reserve: 24, auto: false, rpm: 250, spread: 0.012, reload: 2.0, range: 60 },
  spectre: { name: 'HORNET', price: 1600, damage: 22, headMult: 2.4, mag: 30, reserve: 90, auto: true, rpm: 780, spread: 0.035, reload: 2.1, range: 35 },
  phantom: { name: 'DRIFT', price: 2900, damage: 35, headMult: 2.5, mag: 25, reserve: 75, auto: true, rpm: 660, spread: 0.022, reload: 2.3, range: 45 },
  vandal: { name: 'RIVET', price: 2900, damage: 40, headMult: 2.5, mag: 25, reserve: 75, auto: true, rpm: 600, spread: 0.020, reload: 2.4, range: 60 },
  operator: { name: 'LONGSHOT', price: 4700, damage: 120, headMult: 2.0, mag: 5, reserve: 15, auto: false, rpm: 60, spread: 0.002, reload: 3.2, range: 100, sniper: true },
  knife: { name: 'KNIFE', price: 0, damage: 50, headMult: 2.0, mag: Infinity, reserve: Infinity, auto: false, rpm: 90, spread: 0, reload: 0, range: 2.6, melee: true },
};

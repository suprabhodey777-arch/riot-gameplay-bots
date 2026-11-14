import { describe, expect, it } from 'vitest';

import type { Weapon } from '../contracts.ts';
import { WEAPONS } from './weapons.ts';

interface GameWeapon {
  [field: string]: unknown;
}

describe('weapon copy', () => {
  it('matches game/public/js/config.js field by field', async () => {
    const url = new URL('../../game/public/js/config.js', import.meta.url).href;
    const mod = (await import(url)) as { WEAPONS: Record<string, GameWeapon> };
    expect(mod.WEAPONS, 'config.js exports WEAPONS').toBeTruthy();
    expect(Object.keys(WEAPONS).sort()).toEqual(Object.keys(mod.WEAPONS).sort());

    for (const id of Object.keys(mod.WEAPONS)) {
      const ours = WEAPONS[id as Weapon] as unknown as GameWeapon;
      const theirs = mod.WEAPONS[id];
      expect(ours, `missing weapon ${id}`).toBeDefined();
      expect(Object.keys(ours).sort(), `fields of ${id}`).toEqual(Object.keys(theirs).sort());
      for (const field of Object.keys(theirs)) {
        expect(ours[field], `${id}.${field}`).toBe(theirs[field]);
      }
    }
  });
});

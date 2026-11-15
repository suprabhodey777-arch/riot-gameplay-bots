import { describe, expect, test } from 'vitest';
import { createSim } from './sim.ts';
import { arena, duel, input } from './test-util.ts';
import { ARMOR, WEAPONS, spreadDeg, weaponDamage, type WeaponId, type Loadout } from '../shared/weapons.ts';

function fixture(id: WeaponId = 'lancer', armor: Loadout['armor'] = null, distance = 5) {
  return createSim({ map: arena({ defenseAt: { x: 2 + distance, y: 10 } }), seed: 1,
    agents: duel.map((a, i) => ({ ...a, loadout: i ? { primary: 'lancer' as const, armor } : { primary: id, secondary: 'pin' as const } })),
    scenario: { agents: { d: { hp: 10000 } } } });
}
const shot = (result: 'head' | 'body' | 'miss' = 'body') => input('a', { fire: { targetId: 'd', result } });
const damage = (sim: ReturnType<typeof fixture>, part: 'head' | 'body' = 'body') => sim.step([shot(part)]).events.filter(e => e.type === 'damage').reduce((n, e) => n + e.amount, 0);

describe('weapon enforcement', () => {
  test('default loadout exactly preserves Lancer damage, with Pin and no armour', () => {
    const sim = createSim({ map: arena(), seed: 1, agents: duel });
    const a = sim.state().agents[0];
    expect([a.primary, a.secondary, a.armor, a.ammo.lancer]).toEqual(['lancer', 'pin', 0, 25]);
    expect([WEAPONS.lancer.head, WEAPONS.lancer.body, WEAPONS.lancer.fireIntervalS]).toEqual([150, 40, 0.15]);
    expect(damage(fixture())).toBe(40); expect(damage(fixture(), 'head')).toBe(150);
  });
  test.each(Object.keys(WEAPONS) as WeaponId[])('%s enforces interval, consumes misses and rejects an empty magazine', id => {
    const sim = fixture(id), gap = Math.ceil(WEAPONS[id].fireIntervalS * 20 - 1e-9);
    sim.step([shot('miss')]);
    expect(sim.state().agents[0].ammo[id]).toBe(WEAPONS[id].mag - 1);
    for (let i = 1; i < gap; i++) sim.step([shot('miss')]);
    expect(sim.state().agents[0].ammo[id]).toBe(WEAPONS[id].mag - 1);
    sim.step([shot('miss')]);
    expect(sim.state().agents[0].ammo[id]).toBe(WEAPONS[id].mag - 2);
    for (let i = 0; i < gap * WEAPONS[id].mag; i++) sim.step([shot('miss')]);
    expect(sim.state().agents[0].ammo[id]).toBe(0);
    expect(damage(sim)).toBe(0);
  });
  test.each(Object.keys(WEAPONS) as WeaponId[])('%s reload has exact duration, finite reserve and no shots during reload', id => {
    const sim = fixture(id), w = WEAPONS[id];
    sim.step([shot('miss')]);
    const r = sim.step([input('a', { reload: true, fire: shot().fire })]);
    expect(r.events.some(e => e.type === 'reload')).toBe(true);
    expect(r.events.some(e => e.type === 'damage')).toBe(false);
    expect(sim.state().agents[0].reloadingUntilTick).toBe(2 + Math.ceil(w.reloadS * 20));
    for (let i = 1; i < Math.ceil(w.reloadS * 20); i++) sim.step([shot()]);
    expect(sim.state().agents[0].ammo[id]).toBe(w.mag - 1);
    sim.step([]);
    expect(sim.state().agents[0].ammo[id]).toBe(w.mag);
    expect(sim.state().agents[0].reserve[id]).toBe(w.mag * 3 - 1);
  });
  test.each(['light', 'heavy'] as const)('%s armour absorbs 66 percent, then only remaining points', armor => {
    const sim = fixture('pin', armor);
    const initial = ARMOR[armor].points;
    expect(damage(sim)).toBeCloseTo(26 * 0.34);
    expect(sim.state().agents[1].armor).toBeCloseTo(initial - 26 * 0.66);
    for (let i = 0; i < 4; i++) sim.step([]);
    const second = damage(sim);
    expect(second).toBeCloseTo(26 - Math.min(initial - 26 * 0.66, 26 * 0.66));
  });
  test('pellets each apply falloff and armour, without random draws', () => {
    expect(damage(fixture('brick', null, 5))).toBe(96);
    expect(damage(fixture('brick', null, 9))).toBe(48);
    expect(damage(fixture('brick', 'heavy', 5))).toBeCloseTo(46);
    const sim = fixture('brick'), control = fixture('brick'); sim.step([shot()]);
    expect(sim.rng.next()).toBe(control.rng.next());
  });
  test.each([
    ['hammer', 'head', 15, 150], ['hammer', 'head', 15.01, 110], ['hammer', 'body', 16, 50],
    ['wasp', 'body', 15, 27], ['wasp', 'body', 16, 21.6],
    ['viper', 'head', 20, 140], ['viper', 'head', 21, 119], ['longshot', 'body', 100, 150],
  ] as const)('%s %s at %sm is %s', (id, part, range, expected) => expect(weaponDamage(id, part, range)).toBeCloseTo(expected));
  test('switch emits an event, cancels reload and preserves separate magazines', () => {
    const sim = fixture(); sim.step([shot('miss')]); sim.step([input('a', { reload: true })]);
    const r = sim.step([input('a', { slot: 'secondary' })]);
    expect(r.events.some(e => e.type === 'switch')).toBe(true);
    expect(r.state.agents[0].reloadingUntilTick).toBe(0);
    expect(r.state.agents[0].ammo.lancer).toBe(24);
    expect(r.state.agents[0].ammo.pin).toBe(12);
  });
  test('all patterns and spreads are original, complete and include movement multipliers', () => {
    for (const w of Object.values(WEAPONS)) { expect(w.spray).toHaveLength(w.mag); expect(w.recoveryS).toBeGreaterThan(0); expect(w.baseSpreadDeg).toBeGreaterThan(0); }
    expect(new Set(Object.values(WEAPONS).map(w => JSON.stringify(w.spray))).size).toBe(7);
    expect(spreadDeg('lancer', false, false, false, false, false)).toBe(0);
    expect(spreadDeg('lancer', true, true, false, false, false)).toBe(0.9);
    expect(spreadDeg('lancer', true, false, true, false, false)).toBe(0.45);
    expect(spreadDeg('lancer', true, false, false, false, false)).toBe(3.6);
    expect(spreadDeg('lancer', true, false, false, true, false)).toBeCloseTo(5.4);
    expect(spreadDeg('longshot', false, false, false, false, true)).toBe(0);
    expect(spreadDeg('longshot', true, false, false, false, true)).toBe(20);
  });
});

import { describe, expect, it } from 'vitest';

import type { Situation } from '../contracts.ts';
import { createDefaultMemory, decide } from './default-bot.ts';

const base: Situation = {
  selfPos: { x: 0, z: 0 },
  selfZone: 'mid',
  selfHp: 100,
  selfArmor: 0,
  weapon: 'vandal',
  ammo: null,
  abilitiesReady: { Q: true, E: true, X: false },
  enemyVisible: false,
  enemyRel: null,
  enemyLastSeenZone: null,
  sinceSeen: null,
  tookDamageRecent: false,
  gotKillRecent: false,
  side: 'attack',
  spikeState: 'none',
  roundTime: 100,
  teammatesAlive: 2,
  enemiesAlive: 2,
};

const sit = (overrides: Partial<Situation>): Situation => ({ ...base, ...overrides });

describe('default bot', () => {
  it('fights an enemy in view', () => {
    const mem = createDefaultMemory(3);
    mem.visibleTarget = 'enemy1';
    const out = decide(sit({ enemyVisible: true, enemyRel: { dist: 12, bearing: 0.1 } }), mem);
    expect(out.action.kind).toBe('fight');
    expect(out.reason.length).toBeGreaterThan(0);
  });

  it('retreats away from the last-seen enemy at low hp', () => {
    const mem = createDefaultMemory(3);
    const out = decide(sit({ selfHp: 20, enemyLastSeenZone: 'a-site' }), mem);
    expect(out.action.kind).toBe('retreat');
    if (out.action.kind === 'retreat') expect(out.action.zone).not.toBe('a-site');
    expect(out.reason).toContain('hp 20');
  });

  it('plants when it is on a site with the spike', () => {
    const mem = createDefaultMemory(4);
    const out = decide(sit({ selfZone: 'b-site', spikeState: 'carried' }), mem);
    expect(out.action).toEqual({ kind: 'plant', site: 'B' });
  });

  it('pushes to a site when it carries the spike away from one', () => {
    const mem = createDefaultMemory(4);
    const out = decide(sit({ selfZone: 'mid', spikeState: 'carried' }), mem);
    expect(out.action.kind).toBe('push');
    if (out.action.kind === 'push') expect(['a-site', 'b-site']).toContain(out.action.zone);
  });

  it('holds a fixed angle on defense', () => {
    const mem = createDefaultMemory(2);
    const out = decide(sit({ side: 'defense', selfZone: 'defense-spawn' }), mem);
    expect(out.action.kind).toBe('hold');
    if (out.action.kind === 'hold') expect(out.action.pos).toEqual(expect.objectContaining({ x: expect.any(Number), z: expect.any(Number) }));
    expect(out.reason).toContain('holding');
  });

  it('defuses a planted spike when the area is clear', () => {
    const mem = createDefaultMemory(2);
    mem.spikeSite = 'B';
    const out = decide(sit({ side: 'defense', spikeState: 'planted', selfZone: 'b-site' }), mem);
    expect(out.action).toEqual({ kind: 'defuse', site: 'B' });
  });

  it('jiggles the nearest corner after 8 s holding without contact', () => {
    const mem = createDefaultMemory(2);
    const first = decide(sit({ side: 'defense', selfZone: 'a-site', roundTime: 100 }), mem);
    expect(first.action.kind).toBe('hold');
    const later = decide(sit({ side: 'defense', selfZone: 'a-site', roundTime: 92 }), mem);
    expect(later.action.kind).toBe('peek_jiggle');
    if (later.action.kind === 'peek_jiggle') {
      expect(['left', 'right']).toContain(later.action.dir);
      expect(Number.isFinite(later.action.corner.x)).toBe(true);
    }
  });

  it('smokes once when the enemy was last seen under 5 s ago', () => {
    const mem = createDefaultMemory(1);
    mem.lastSeenPos = { x: 4, z: -3 };
    const out = decide(sit({ side: 'defense', selfZone: 'b-site', sinceSeen: 1.5 }), mem);
    expect(out.action).toEqual({ kind: 'use_ability', slot: 'E', at: { x: 4, z: -3 } });
    expect(mem.smokeUsed).toBe(true);
    const again = decide(sit({ side: 'defense', selfZone: 'b-site', sinceSeen: 1.6 }), mem);
    expect(again.action.kind).not.toBe('use_ability');
  });
});

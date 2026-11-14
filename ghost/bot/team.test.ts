/**
 * Unit tests for the team layer: every rule on a hand-built Situation and a
 * hand-built TeamContext, the guards that keep it out of a fight, the priority
 * order between rules, and the spike-holder check in the default bot.
 */
import { describe, expect, it } from 'vitest';

import type { Action, Situation } from '../contracts.ts';
import { createDefaultMemory, decide } from './default-bot.ts';
import {
  HOLD_ANGLE_SEP_RAD,
  TEAM_RULES,
  teamAdjust,
  type TeamContext,
  type TeamDeath,
  type TeamMate,
} from './team.ts';

const baseSituation: Situation = {
  selfPos: { x: -20, z: -5 },
  selfZone: 'a-main',
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

const sit = (overrides: Partial<Situation> = {}): Situation => ({ ...baseSituation, ...overrides });

const mate = (overrides: Partial<TeamMate> = {}): TeamMate => ({
  name: 'mate',
  pos: { x: -17, z: -5 },
  alive: true,
  actionKind: null,
  targetZone: null,
  watchYaw: 0,
  pushing: false,
  holding: false,
  ...overrides,
});

const death = (overrides: Partial<TeamDeath> = {}): TeamDeath => ({
  name: 'fallen',
  killer: 'enemy',
  // a-main's waypoint, i.e. a known callout
  pos: { x: -25, z: -8 },
  killerPos: null,
  atMs: 8500,
  ...overrides,
});

const context = (overrides: Partial<TeamContext> = {}): TeamContext => ({
  now: 10_000,
  teammates: [],
  deaths: [],
  spike: { planted: false, site: null, siteZone: null },
  selfHasSpike: false,
  ...overrides,
});

const hold: Action = { kind: 'hold', pos: { x: -20, z: -5 }, watchYaw: 0 };
const base = (action: Action = hold, reason = 'holding a-main'): { action: Action; reason: string } => ({ action, reason });

const separation = (a: number, b: number): number => Math.abs(Math.atan2(Math.sin(a - b), Math.cos(a - b)));

describe('teamAdjust rules', () => {
  it('rotates to the planted site when a defender is somewhere else', () => {
    const ctx = context({
      spike: { planted: true, site: 'A', siteZone: 'a-site' },
      teammates: [mate({ name: 'planter', pos: { x: -17, z: 14 }, actionKind: 'plant', targetZone: 'a-site' })],
    });
    const out = teamAdjust(base(), sit({ side: 'defense', selfZone: 'b-site', selfPos: { x: 25, z: 14 } }), ctx);
    expect(out.action).toEqual({ kind: 'rotate', zone: 'a-site' });
    expect(out.teamRule).toBe(TEAM_RULES.rotateOnPlant);
    expect(out.reason).toContain('planter');
  });

  it('does not rotate while the defender is already in the planted site', () => {
    const ctx = context({
      spike: { planted: true, site: 'A', siteZone: 'a-site' },
      teammates: [mate({ name: 'planter' })],
    });
    const out = teamAdjust(base(), sit({ side: 'defense', selfZone: 'a-main', selfPos: { x: -20, z: -5 } }), ctx);
    expect(out.action).toEqual(hold);
    expect(out.teamRule).toBeNull();
  });

  it('trades a teammate who just died within 25 m', () => {
    const ctx = context({ deaths: [death({ name: 'fallen' })] });
    const out = teamAdjust(base(), sit({ side: 'defense' }), ctx);
    expect(out.action).toEqual({ kind: 'push', zone: 'a-main' });
    expect(out.teamRule).toBe(TEAM_RULES.trade);
    expect(out.reason).toContain('fallen');
  });

  it('ignores a death that is too old or too far away', () => {
    const far = context({ deaths: [death({ pos: { x: 25, z: 14 } })] });
    expect(teamAdjust(base(), sit(), far).teamRule).toBeNull();
    const stale = context({ deaths: [death({ atMs: 5000 })] });
    expect(teamAdjust(base(), sit(), stale).teamRule).toBeNull();
  });

  it('follows a teammate pushing within 15 m into the same zone', () => {
    const ctx = context({
      teammates: [mate({ name: 'lead', pos: { x: -16, z: -5 }, actionKind: 'push', targetZone: 'b-site', pushing: true })],
    });
    const out = teamAdjust(base(), sit(), ctx);
    expect(out.action).toEqual({ kind: 'push', zone: 'b-site' });
    expect(out.teamRule).toBe(TEAM_RULES.followPush);
    expect(out.reason).toContain('lead');
  });

  it('does not follow a teammate who is too far away', () => {
    const ctx = context({
      teammates: [mate({ name: 'lead', pos: { x: 5, z: 5 }, actionKind: 'push', targetZone: 'b-site', pushing: true })],
    });
    expect(teamAdjust(base(), sit(), ctx).teamRule).toBeNull();
  });

  it('takes an angle at least 60 degrees off a teammate holding next to it', () => {
    const mateYaw = 0.3;
    const ctx = context({
      teammates: [mate({ name: 'buddy', pos: { x: -17, z: -5 }, watchYaw: mateYaw, actionKind: 'hold', holding: true })],
    });
    const out = teamAdjust(base({ kind: 'hold', pos: { x: -20, z: -5 }, watchYaw: 0.4 }), sit({ side: 'defense' }), ctx);
    expect(out.teamRule).toBe(TEAM_RULES.holdOtherAngle);
    expect(out.reason).toContain('buddy');
    if (out.action.kind !== 'hold') throw new Error(`expected hold, got ${out.action.kind}`);
    expect(separation(out.action.watchYaw, mateYaw)).toBeGreaterThanOrEqual(HOLD_ANGLE_SEP_RAD - 1e-9);
    expect(out.action.pos).toEqual({ x: -20, z: -5 });
  });

  it('leaves an already separated angle alone', () => {
    const ctx = context({
      teammates: [mate({ name: 'buddy', watchYaw: 0, actionKind: 'hold', holding: true })],
    });
    const out = teamAdjust(base({ kind: 'hold', pos: { x: -20, z: -5 }, watchYaw: Math.PI / 2 }), sit(), ctx);
    expect(out.teamRule).toBeNull();
  });
});

describe('teamAdjust guards', () => {
  const richContext = (): TeamContext =>
    context({
      spike: { planted: true, site: 'A', siteZone: 'a-site' },
      teammates: [
        mate({ name: 'buddy', pos: { x: -17, z: -5 }, watchYaw: 0.1, actionKind: 'hold', holding: true }),
        mate({ name: 'lead', pos: { x: -16, z: -6 }, actionKind: 'push', targetZone: 'b-site', pushing: true }),
      ],
      deaths: [death()],
    });
  const richSituation = (): Situation =>
    sit({ side: 'defense', selfZone: 'b-site', selfPos: { x: 25, z: 14 }, spikeState: 'planted' });

  for (const action of [
    { kind: 'fight', target: 'enemy1' },
    { kind: 'plant', site: 'A' },
    { kind: 'defuse', site: 'A' },
  ] as Action[]) {
    it(`never overrides ${action.kind}`, () => {
      const out = teamAdjust(base(action, `doing ${action.kind}`), richSituation(), richContext());
      expect(out.action).toBe(action);
      expect(out.reason).toBe(`doing ${action.kind}`);
      expect(out.teamRule).toBeNull();
    });
  }

  it('never overrides anything while an enemy is visible', () => {
    const out = teamAdjust(base(), sit({ ...richSituation(), enemyVisible: true }), richContext());
    expect(out.action).toEqual(hold);
    expect(out.teamRule).toBeNull();
  });
});

describe('teamAdjust priority', () => {
  const full = (): TeamContext =>
    context({
      spike: { planted: true, site: 'A', siteZone: 'a-site' },
      teammates: [
        mate({ name: 'planter', pos: { x: -17, z: 14 }, actionKind: 'plant', targetZone: 'a-site' }),
        mate({ name: 'lead', pos: { x: -16, z: -5 }, actionKind: 'push', targetZone: 'b-main', pushing: true }),
        mate({ name: 'buddy', pos: { x: -19, z: -4 }, watchYaw: 0.2, actionKind: 'hold', holding: true }),
      ],
      deaths: [death({ pos: { x: 17, z: 14 } })],
    });
  const defend = (): Situation => sit({ side: 'defense', selfZone: 'b-site', selfPos: { x: 25, z: 14 } });

  it('rotate on plant beats trade, follow and hold', () => {
    expect(teamAdjust(base(), defend(), full()).teamRule).toBe(TEAM_RULES.rotateOnPlant);
  });

  it('trade beats follow and hold', () => {
    const ctx = { ...full(), spike: { planted: false, site: null, siteZone: null } };
    expect(teamAdjust(base(), defend(), ctx).teamRule).toBe(TEAM_RULES.trade);
  });

  it('follow beats hold', () => {
    const ctx = { ...full(), spike: { planted: false, site: null, siteZone: null }, deaths: [] };
    const out = teamAdjust(base(), sit(), ctx);
    expect(out.teamRule).toBe(TEAM_RULES.followPush);
  });

  it('hold only fires from a base hold', () => {
    const ctx = { ...full(), spike: { planted: false, site: null, siteZone: null }, deaths: [] };
    const pushing: Action = { kind: 'push', zone: 'a-site' };
    const out = teamAdjust(base(pushing, 'pushing a-site'), sit(), ctx);
    // The follow rule still applies to a push, but the hold rule must not.
    expect(out.teamRule).not.toBe(TEAM_RULES.holdOtherAngle);
  });
});

describe('default bot spike holder', () => {
  it('does not plant when the spike is carried by someone else', () => {
    const mem = createDefaultMemory(4, false);
    const out = decide(sit({ side: 'attack', selfZone: 'b-site', spikeState: 'carried' }), mem);
    expect(out.action.kind).not.toBe('plant');
    expect(out.action.kind).toBe('push');
  });

  it('plants when it is the holder and standing on a site', () => {
    const mem = createDefaultMemory(4, true);
    const out = decide(sit({ side: 'attack', selfZone: 'b-site', spikeState: 'carried' }), mem);
    expect(out.action).toEqual({ kind: 'plant', site: 'B' });
  });
});

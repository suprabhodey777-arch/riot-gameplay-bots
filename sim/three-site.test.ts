import { describe, expect, test } from 'vitest';
import type { SiteId, Vec2 } from '../shared/contract.ts';
import { createMatch, runHeadless } from '../bot/match.ts';
import { centroid } from '../analyzer/geo.ts';
import { buildDrill } from '../analyzer/drills.ts';
import { generateMatches, neutralProfile } from '../analyzer/synthetic.ts';
import { loadMap } from '../shared/maps/index.ts';
import { siteAt } from './geometry.ts';
import { createSim } from './sim.ts';

const map = loadMap('saltworks');
const test2 = loadMap('test-box');

describe('three-site support', () => {
  test('siteAt finds C and the sim lets an attacker plant there', () => {
    const c = centroid(map.sites.C as Vec2[]);
    expect(siteAt(map, c)).toBe('C');
    expect(siteAt(test2, c)).toBeNull();
    const sim = createSim({ map, seed: 1, agents: [{ id: 'p', team: 'attack', isBot: false }, { id: 'd', team: 'defense', isBot: false }], scenario: { agents: { p: { spawn: c, carryingSpike: true } } } });
    const input = (id: string, plant: boolean) => ({ agentId: id, move: { x: 0, y: 0 }, yaw: 0, walk: false, crouch: false, fire: null, smokeAt: null, plantHeld: plant, defuseHeld: false });
    let planted: SiteId | null = null;
    for (let i = 0; i < 100 && !planted; i++) {
      const r = sim.step([input('p', true), input('d', false)]);
      for (const e of r.events) if (e.type === 'plant') planted = e.site;
    }
    expect(planted).toBe('C');
  });

  test('bot rounds on the three-site map plant on all three sites across seeds', () => {
    const planted = new Set<SiteId>();
    for (let seed = 1; seed <= 10; seed++) {
      const agents = [
        ...[1, 2, 3, 4, 5].map((i) => ({ id: `a${i}`, team: 'attack' as const, isBot: true })),
        ...[1, 2, 3, 4, 5].map((i) => ({ id: `d${i}`, team: 'defense' as const, isBot: true })),
      ];
      const r = runHeadless(createMatch({ map, seed, agents }), 2400);
      expect(r.state.phase).toBe('ended');
      for (const e of r.events) if (e.type === 'plant') planted.add(e.site);
    }
    expect([...planted].sort()).toEqual(['A', 'B', 'C']);
  });

  test('drills and synthetic logs use all three sites, and a two-site map never gets C', () => {
    const drillSites = new Set<string>();
    for (let seed = 1; seed <= 30; seed++) drillSites.add(buildDrill('clutch', 0.5, map, seed).spikePlanted?.site as string);
    expect([...drillSites].sort()).toEqual(['A', 'B', 'C']);
    const sites = (mapId: string) => {
      const out = new Set<string>();
      for (const log of generateMatches(neutralProfile('duelist'), 3, { mapId }))
        for (const r of log.rounds) for (const e of r.events) if (e.type === 'plant') out.add(e.site);
      return out;
    };
    expect(sites('saltworks').has('C')).toBe(true);
    expect(sites('test-box').has('C')).toBe(false);
  });
});

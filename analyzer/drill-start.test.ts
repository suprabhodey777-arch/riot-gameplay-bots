/**
 * A drill has to put the player right before the moment: around the corner, out of sight, facing
 * the way they will go, a couple of seconds from contact. These tests pin that down for the
 * sample players the play page offers, on the real map.
 */
import { describe, expect, test } from 'vitest';
import type { DrillSpec, Skill } from '../shared/coaching.ts';
import type { Vec2 } from '../shared/contract.ts';
import { loadMap } from '../shared/maps/index.ts';
import { minWallDist, pathBetween } from '../bot/nav.ts';
import { startDrill } from '../session/drill-run.ts';
import { CoachingSession } from '../session/session.ts';
import { lineOfSight } from '../sim/geometry.ts';
import { buildDrill } from './drills.ts';
import { PLAYER_ID, sampleHistory } from './synthetic.ts';

const map = loadMap('saltworks');
const SKILLS: Skill[] = ['entry', 'clutch', 'time_pressure', 'smoke_usage'];
const NO_KEY = { apiKey: null } as const;
const SEED = 11;

const dist = (a: Vec2, b: Vec2): number => Math.hypot(a.x - b.x, a.y - b.y);
const nearest = (from: Vec2, to: Vec2[]): Vec2 =>
  to.reduce((best, p) => (dist(p, from) < dist(best, from) ? p : best));

/**
 * Path length from `from` to the first point on the walk to `enemy` where it comes into sight
 * (the whole path when it is never in sight).
 */
function contactPathLen(from: Vec2, enemy: Vec2): number {
  let len = 0;
  let at = from;
  for (const p of pathBetween(map, from, enemy) ?? [enemy]) {
    const seg = dist(at, p);
    for (let k = 0.5; k < seg - 1e-9; k += 0.5) {
      const q = { x: at.x + ((p.x - at.x) * k) / seg, y: at.y + ((p.y - at.y) * k) / seg };
      if (lineOfSight(q, enemy, map.walls)) return len + k;
    }
    if (lineOfSight(p, enemy, map.walls)) return len + seg;
    len += seg;
    at = p;
  }
  return len;
}

const sampleRehearsals = (skill: Skill): DrillSpec[] =>
  CoachingSession.fromHistory(sampleHistory(skill, SEED), PLAYER_ID, { seed: SEED, ...NO_KEY }).rehearsals();

const generated = (skill: Skill, seed: number, difficulty = 0.5): DrillSpec => buildDrill(skill, difficulty, map, seed);

describe('drill starts a few steps from the fight', () => {
  const rehearsals = SKILLS.flatMap((s) => sampleRehearsals(s));
  const drills = SKILLS.flatMap((s) => [1, 2, 3, 4, 5, 6].map((seed) => generated(s, seed)));

  test('the samples offer entry rehearsals and entry drills to check', () => {
    expect(rehearsals.filter((d) => d.skill === 'entry').length).toBeGreaterThan(0);
    expect(drills.filter((d) => d.skill === 'entry').length).toBeGreaterThan(0);
  });

  test('entry rehearsals and entry drills start 7-12 m of path short of the contact, unseen', () => {
    for (const d of [...rehearsals, ...drills].filter((x) => x.skill === 'entry')) {
      const contact = contactPathLen(d.player.spawn, nearest(d.player.spawn, d.bots.map((b) => b.spawn)));
      expect(contact, `${d.id} starts ${contact.toFixed(1)} m from contact`).toBeGreaterThanOrEqual(7);
      expect(contact, `${d.id} starts ${contact.toFixed(1)} m from contact`).toBeLessThanOrEqual(12);
      for (const b of d.bots) expect(lineOfSight(d.player.spawn, b.spawn, map.walls), `${d.id} sees ${b.id}`).toBe(false);
    }
  });

  test('every drill starts the player facing a way that is not into a wall', () => {
    for (const d of [...rehearsals, ...drills]) {
      const yaw = d.player.yaw;
      expect(Number.isFinite(yaw), `${d.id} has no facing`).toBe(true);
      const ahead = { x: d.player.spawn.x + Math.cos(yaw as number) * 2, y: d.player.spawn.y + Math.sin(yaw as number) * 2 };
      expect(minWallDist(ahead, map.walls), `${d.id} faces a wall`).toBeGreaterThan(0.4);
    }
  });

  test('round 2 at North Causeway starts within a dozen metres of the enemy it lost to', () => {
    const round2 = sampleRehearsals('entry').find((d) => d.title === 'Rehearsal: round 2');
    expect(round2?.goal).toContain('North Causeway');
    const bot1 = round2?.bots[0].spawn as Vec2;
    const contact = contactPathLen(round2?.player.spawn as Vec2, bot1);
    expect(contact, `starts ${contact.toFixed(1)} m from contact`).toBeLessThanOrEqual(12);
    expect(contact).toBeGreaterThanOrEqual(7);
  });

  test('the sim starts the player facing the drill facing, and keeps the old default without one', () => {
    const d = generated('entry', 3);
    expect(d.player.yaw).toBeDefined();
    const me = startDrill(d).state().agents.find((a) => a.id === d.player.id);
    expect(me?.yaw).toBeCloseTo(d.player.yaw as number, 6);
    const old = { ...d, player: { ...d.player } };
    delete old.player.yaw;
    expect(startDrill(old).state().agents.find((a) => a.id === d.player.id)?.yaw).toBe(0);
  });
});

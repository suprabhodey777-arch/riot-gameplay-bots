/**
 * Proof that the four showcase drills play out the way they were cut for the demo:
 * the player starts on open ground, out of every enemy's sight and facing the action;
 * the action is 1-2 s of running away (7-12 m); the retake actually retakes; the smoke
 * is what makes Kiln Gap safe; and the enemy takes the fight within 10 s.
 */
import { describe, expect, test } from 'vitest';
import type { DrillSpec, Skill } from '../shared/coaching.ts';
import type { SimEvent, Vec2 } from '../shared/contract.ts';
import { loadMap } from '../shared/maps/index.ts';
import { minWallDist, pathBetween } from '../bot/nav.ts';
import { AGENT_RADIUS, lineOfSight, siteAt } from '../sim/geometry.ts';
import { PLANT_TIME_S, RUN_SPEED, SMOKE_RADIUS, TICK_RATE } from '../sim/sim.ts';
import { idleInput, startDrill } from '../session/drill-run.ts';
import { calloutAt } from './geo.ts';
import {
  ENTRY_SPOT,
  KILN_GAP_SPOT,
  nearestPlantablePoint,
  SHOWCASE_DIFFICULTY,
  SHOWCASE_SEEDS,
  SHOWCASE_SKILLS,
  showcaseDrill,
  showcaseDrills,
} from './showcase.ts';

const map = loadMap('saltworks');
const dist = (a: Vec2, b: Vec2): number => Math.hypot(a.x - b.x, a.y - b.y);

/** True when a body's centre fits here: inside the map and clear of every wall by its radius. */
const fits = (p: Vec2): boolean =>
  p.x >= AGENT_RADIUS && p.y >= AGENT_RADIUS && p.x <= map.bounds.width - AGENT_RADIUS && p.y <= map.bounds.height - AGENT_RADIUS &&
  minWallDist(p, map.walls) >= AGENT_RADIUS;

/** The walkable route between two points (a straight hop when nothing blocks). */
const route = (from: Vec2, to: Vec2): Vec2[] => pathBetween(map, from, to) ?? [to];

function routeLength(from: Vec2, to: Vec2): number {
  let len = 0;
  let at = from;
  for (const p of route(from, to)) {
    len += dist(at, p);
    at = p;
  }
  return len;
}

/**
 * The first point on the walk from `from` to `to` where `observer` has a clear line to the
 * player, with how far it is along the walk. Sampled every 10 cm so a thin sightline counts.
 */
function firstExposure(from: Vec2, to: Vec2, observer: Vec2): { at: Vec2; walked: number } | null {
  let at = from;
  let walked = 0;
  for (const wp of route(from, to)) {
    const seg = dist(at, wp);
    const n = Math.max(1, Math.ceil(seg / 0.1));
    for (let i = 1; i <= n; i++) {
      const p = { x: at.x + ((wp.x - at.x) * i) / n, y: at.y + ((wp.y - at.y) * i) / n };
      if (lineOfSight(observer, p, map.walls)) return { at: p, walked: walked + (seg * i) / n };
    }
    walked += seg;
    at = wp;
  }
  return null;
}

/**
 * Plays a drill with a scripted player that runs the route to `target` and then stands still
 * (holding plant there when `plant` is set), and returns the verdict and every event.
 */
function playRoute(spec: DrillSpec, target: Vec2, opts: { plant?: boolean; maxTicks?: number } = {}): { success: boolean; events: SimEvent[] } {
  const run = startDrill(spec);
  const walk = route(spec.player.spawn, target);
  const events: SimEvent[] = [];
  let i = 0;
  for (let t = 0; t < (opts.maxTicks ?? 10 * TICK_RATE) && !run.done(); t++) {
    const me = run.state().agents.find((a) => a.id === spec.player.id);
    if (!me) break;
    while (i < walk.length - 1 && dist(me.pos, walk[i]) < 0.6) i++;
    const wp = walk[i];
    const left = dist(me.pos, wp);
    const move = left < 0.6 ? { x: 0, y: 0 } : { x: (wp.x - me.pos.x) / left, y: (wp.y - me.pos.y) / left };
    events.push(
      ...run.step({
        ...idleInput(spec.player.id),
        move,
        yaw: left < 0.6 ? me.yaw : Math.atan2(move.y, move.x),
        plantHeld: opts.plant === true && dist(me.pos, target) < 1.2,
      }).events,
    );
  }
  return { success: run.finish().success, events };
}

const drills = showcaseDrills();
const bySkill = (skill: Skill): DrillSpec => drills.find((d) => d.skill === skill) as DrillSpec;

describe('the showcase is four curated drills', () => {
  test('one per skill, in demo order, deterministic, at the middle difficulty', () => {
    expect(drills.map((d) => d.skill)).toEqual(SHOWCASE_SKILLS);
    expect(drills.map((d) => d.mapId)).toEqual(SHOWCASE_SKILLS.map(() => 'saltworks'));
    for (const d of drills) {
      expect(d.difficulty).toBe(SHOWCASE_DIFFICULTY);
      expect(d.seed).toBe(SHOWCASE_SEEDS[d.skill]);
      expect(showcaseDrill(d.skill)).toEqual(d);
      expect(d.bots.length).toBeGreaterThan(0);
      expect(d.player.team).toBe('attack');
      // Every fighter is on a rifle.
      expect(d.loadout?.primary).toBe('lancer');
    }
    // Fixing the seed means the same drill every time, and no two drills share one.
    expect(new Set(drills.map((d) => d.seed)).size).toBe(drills.length);
    // The clocks the demo was cut to: a full entry round, a 30 s post-plant, the computed
    // late plant (tested below), and a full smoke round.
    expect(bySkill('entry').roundTimeLeftS).toBe(60);
    expect(bySkill('clutch').roundTimeLeftS).toBe(30);
    expect(bySkill('clutch').spikePlanted?.secondsLeft).toBe(30);
    expect(bySkill('smoke_usage').roundTimeLeftS).toBe(60);
  });

  test('the four drills start in four different parts of the map', () => {
    const callouts = drills.map((d) => calloutAt(map, d.player.spawn));
    expect(new Set(callouts).size, callouts.join(' / ')).toBe(4);
  });

  test('every drill carries an on-screen objective on walkable ground or on a site', () => {
    const labels = drills.map((d) => d.objective?.label);
    expect(labels).toEqual(['HELD ANGLE', 'DEFEND THE SPIKE', 'PLANT ON C', 'SMOKE THE GAP']);
    for (const d of drills) {
      const at = d.objective?.pos as Vec2;
      expect(at, d.id).toBeDefined();
      expect(fits(at) || siteAt(map, at) !== null, `${d.id} @ ${at.x},${at.y}`).toBe(true);
    }
    // And each marker sits on the thing it names.
    expect(bySkill('entry').objective?.pos).toEqual(bySkill('entry').bots[0].hold?.pos);
    expect(bySkill('clutch').objective?.pos).toEqual(bySkill('clutch').spikePlanted?.pos);
    expect(siteAt(map, bySkill('time_pressure').objective?.pos as Vec2)).toBe('C');
    expect(dist(bySkill('smoke_usage').objective?.pos as Vec2, KILN_GAP_SPOT.centre)).toBeLessThan(0.1);
  });
});

describe.each(drills.map((d) => [d.id, d] as const))('%s', (_id, drill) => {
  test('the player starts on open ground, out of every enemy sight, facing the action', () => {
    const spawn = drill.player.spawn;
    expect(fits(spawn)).toBe(true);

    // The spec's positions are the ones the sim starts from.
    const state = startDrill(drill).state();
    expect(state.agents.find((a) => a.id === drill.player.id)?.pos).toEqual(spawn);
    for (const b of drill.bots) expect(state.agents.find((a) => a.id === b.id)?.pos).toEqual(b.spawn);

    // Nobody sees the player at tick 0, from its spawn or from the spot it holds.
    for (const b of drill.bots) {
      expect(lineOfSight(spawn, b.spawn, map.walls), `${b.id} spawn`).toBe(false);
      if (b.hold) expect(lineOfSight(spawn, b.hold.pos, map.walls), `${b.id} hold`).toBe(false);
    }

    // The player faces the fight, and the first 2 m of that are open.
    const yaw = drill.player.yaw;
    expect(Number.isFinite(yaw as number)).toBe(true);
    const ahead = { x: spawn.x + Math.cos(yaw as number) * 2, y: spawn.y + Math.sin(yaw as number) * 2 };
    expect(fits(ahead), `ahead ${ahead.x.toFixed(1)},${ahead.y.toFixed(1)}`).toBe(true);
  });
});

describe('the action is 1-2 s of running away', () => {
  test('entry: North Causeway opens up 7-12 m up the alley', () => {
    const d = bySkill('entry');
    const hold = d.bots[0].hold as NonNullable<DrillSpec['bots'][number]['hold']>;
    // The anchor watches the alley exit, not some other angle.
    expect(lineOfSight(hold.pos, ENTRY_SPOT.mouth, map.walls)).toBe(true);
    const toMouth = Math.atan2(ENTRY_SPOT.mouth.y - hold.pos.y, ENTRY_SPOT.mouth.x - hold.pos.x);
    expect(Math.abs(Math.atan2(Math.sin(hold.yaw - toMouth), Math.cos(hold.yaw - toMouth)))).toBeLessThan(0.05);

    const seen = firstExposure(d.player.spawn, hold.pos, hold.pos);
    expect(seen, 'the anchor never sees the player').not.toBeNull();
    expect(seen?.walked ?? 0).toBeGreaterThanOrEqual(7);
    expect(seen?.walked ?? 0).toBeLessThanOrEqual(12);
    // The angle opens inside the alley, before the player reaches the causeway itself.
    expect(calloutAt(map, seen?.at ?? d.player.spawn)).toBe('Mid Road');
  });

  test('time_pressure: C site edge is 7-12 m away, and the clock is walk + plant + margin', () => {
    const d = bySkill('time_pressure');
    const plant = d.objective?.pos as Vec2;
    expect(siteAt(map, d.player.spawn)).toBeNull();
    const walk = routeLength(d.player.spawn, plant);
    expect(walk).toBeGreaterThanOrEqual(7);
    expect(walk).toBeLessThanOrEqual(12);
    // The clock fits exactly: the walk, the plant, and the 3 s margin, no more (rounded to 0.1 s).
    const need = walk / RUN_SPEED + PLANT_TIME_S + 3;
    expect(Math.abs(d.roundTimeLeftS - need), `clock ${d.roundTimeLeftS} vs need ${need.toFixed(2)}`).toBeLessThanOrEqual(0.05 + 1e-9);
  });

  test('smoke_usage: Kiln Gap is watched 7-12 m from the start', () => {
    const d = bySkill('smoke_usage');
    const hold = d.bots[0].hold?.pos as Vec2;
    const seen = firstExposure(d.player.spawn, KILN_GAP_SPOT.goal, hold);
    expect(seen, 'the anchor never sees the player').not.toBeNull();
    expect(seen?.walked ?? 0).toBeGreaterThanOrEqual(7);
    expect(seen?.walked ?? 0).toBeLessThanOrEqual(12);
    // And the angle opens on the gap itself, not in the open.
    expect(calloutAt(map, seen?.at ?? d.player.spawn)).toBe('Kiln Gap');
    // The anchor is a long angle across the gap, as designed.
    const gap = d.objective?.pos as Vec2;
    expect(dist(hold, gap)).toBeGreaterThanOrEqual(20);
    expect(dist(hold, gap)).toBeLessThanOrEqual(35);
    // The goal point is on the far side: 6 m past the corridor's south mouth (y = 22), in
    // South Causeway, and out of the anchor's sight, so a smoked crossing can be finished.
    expect(calloutAt(map, KILN_GAP_SPOT.goal)).toBe('South Causeway');
    expect(22 - KILN_GAP_SPOT.goal.y).toBeGreaterThanOrEqual(6);
    expect(lineOfSight(hold, KILN_GAP_SPOT.goal, map.walls)).toBe(false);
  });
});

describe('the 1v2 retake actually retakes', () => {
  test('both defenders close to within 6 m of the planted spike inside 12 s', () => {
    const d = bySkill('clutch');
    const spike = d.spikePlanted?.pos as Vec2;
    expect(d.spikePlanted?.site).toBe('B');
    expect(d.bots).toHaveLength(2);
    expect(d.bots.map((b) => b.archetypeId).sort()).toEqual(['entry-duelist', 'patient-lurker']);
    // Neither is told to hold: they play to the spike.
    expect(d.bots.every((b) => b.hold === undefined)).toBe(true);
    // They come at it from opposite doors.
    expect(calloutAt(map, d.bots[0].spawn)).toBe('Rear Court');
    expect(calloutAt(map, d.bots[1].spawn)).toBe('Weighbridge');

    const run = startDrill(d);
    const closest = new Map<string, number>();
    for (let t = 0; t < 12 * TICK_RATE && !run.done(); t++) {
      const state = run.step(idleInput(d.player.id)).state;
      for (const a of state.agents) if (a.team === 'defense') closest.set(a.id, Math.min(closest.get(a.id) ?? Infinity, dist(a.pos, spike)));
    }
    for (const b of d.bots) expect(closest.get(b.id) ?? Infinity, b.id).toBeLessThanOrEqual(6);
  });
});

describe('the smoke is what makes Kiln Gap crossable', () => {
  test('the anchor covers the crossing line bare, and loses it under a smoke at the gap centre', () => {
    const d = bySkill('smoke_usage');
    const gap = d.objective?.pos as Vec2;
    const hold = d.bots[0].hold?.pos as Vec2;
    // The crossing line: the gap's north-south axis, from 3 m north of the centre to 3 m
    // south of it - the 6 m stretch a smoke at the centre has to close.
    const line: Vec2[] = [];
    for (let dy = -3; dy <= 3 + 1e-9; dy += 0.5) line.push({ x: gap.x, y: gap.y + dy });
    expect(line).toHaveLength(13);

    const bare = line.filter((p) => lineOfSight(hold, p, map.walls));
    expect(bare.length, 'the anchor must watch the whole crossing bare').toBe(line.length);

    const smoked = line.filter((p) => lineOfSight(hold, p, map.walls, [{ pos: gap, radius: SMOKE_RADIUS }]));
    expect(smoked.length, 'the smoke must close the crossing').toBe(0);
  });
});

const ENEMY_CASES: { skill: Skill; target: () => Vec2 }[] = [
  { skill: 'entry', target: () => bySkill('entry').bots[0].hold?.pos as Vec2 },
  { skill: 'time_pressure', target: () => bySkill('time_pressure').objective?.pos as Vec2 },
  { skill: 'smoke_usage', target: () => KILN_GAP_SPOT.goal },
];

describe('the enemy takes the fight', () => {
  test.each(ENEMY_CASES)('a player who runs the route is spotted or shot within 10 s ($skill)', ({ skill, target }) => {
    const drill = bySkill(skill);
    const contact = playRoute(drill, target()).events.find(
      (e) => (e.type === 'spotted' && e.target === drill.player.id) || (e.type === 'damage' && e.victim === drill.player.id),
    );
    expect(contact, 'no spotted/damage event involving the player in 10 s').toBeDefined();
    expect(contact?.tick ?? Infinity).toBeLessThanOrEqual(10 * TICK_RATE);
    expect(['spotted', 'damage']).toContain(contact?.type);
  });
});

describe('the late plant can be finished', () => {
  test('a player who runs the door and holds the plant gets it down before the clock', () => {
    const d = bySkill('time_pressure');
    const result = playRoute(d, d.objective?.pos as Vec2, { plant: true, maxTicks: Math.round((d.roundTimeLeftS + 1) * TICK_RATE) });
    expect(result.events.some((e) => e.type === 'plant'), 'the plant never completed').toBe(true);
    expect(result.success).toBe(true);
  });
});

// Guard the exported helper the late-plant clock is built from: the nearest plantable point
// really is on the near side of the plant zone, and it is plantable.
describe('nearestPlantablePoint', () => {
  test('returns a plantable point on the near side of the site', () => {
    const d = bySkill('time_pressure');
    const p = nearestPlantablePoint(map, 'C', d.player.spawn, 7);
    expect(siteAt(map, p)).toBe('C');
    expect(fits(p)).toBe(true);
    expect(routeLength(d.player.spawn, p)).toBeLessThanOrEqual(routeLength(d.player.spawn, { x: 92, y: 18 }) + 1e-9);
  });
});

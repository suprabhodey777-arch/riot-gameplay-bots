import { describe, expect, test } from 'vitest';
import type { Skill, WeaknessEvidence, WeaknessFinding } from '../shared/coaching.ts';
import { loadMap } from '../shared/maps/index.ts';
import { idleInput, startDrill } from '../session/drill-run.ts';
import { sessionWithWeakness } from '../session/headless.ts';
import { createSimulatedPlayer, profileWithWeakness } from '../session/simulated-player.ts';
import { lineOfSight } from '../sim/geometry.ts';
import { DEFUSE_TIME_S, PLANT_TIME_S, RUN_SPEED } from '../sim/sim.ts';
import { pathBetween } from '../bot/nav.ts';
import { analyze } from './analyze.ts';
import { EXAM_SEED_BASE } from './drills.ts';
import { HIDDEN_RADIUS_M, rehearsalDrill } from './rehearse.ts';
import { CoachingSession } from '../session/session.ts';
import type { DrillSpec } from '../shared/coaching.ts';
import type { Vec2 } from '../shared/contract.ts';
import { generateMatches, PLAYER_ID, weakProfile } from './synthetic.ts';

const map = loadMap('saltworks');
const SKILLS: Skill[] = ['entry', 'clutch', 'time_pressure', 'smoke_usage'];
const NO_KEY = { apiKey: null } as const;

const finding = (skill: Skill): WeaknessFinding => ({ skill, rate: 0.1, benchmark: 0.5, severity: 0.8, sampleSize: 20, summary: '', evidence: [] });
const evidence = (over: Partial<NonNullable<WeaknessEvidence['situation']>> = {}): WeaknessEvidence => ({
  matchId: 'm1',
  round: 10,
  tick: 400,
  note: 'lost the opening duel at Mid Road, 0.6 s after first contact',
  situation: {
    side: 'attack',
    site: 'B',
    playerPos: { x: 30, y: 40 },
    enemies: [{ pos: { x: 64, y: 40 } }, { pos: { x: 60, y: 50 } }],
    allies: [],
    roundTimeLeftS: 80,
    spikePlanted: null,
    playerHasSpike: true,
    ...over,
  },
});

describe('analyzer situations', () => {
  test('every failed moment carries a situation with positions on the map and a clock', () => {
    for (const skill of SKILLS) {
      const r = analyze(generateMatches(weakProfile('duelist', skill, 0.05), 5), PLAYER_ID);
      const f = r.findings.find((x) => x.skill === skill);
      expect(f?.evidence.length, skill).toBeGreaterThan(0);
      for (const ev of f?.evidence ?? []) {
        const s = ev.situation;
        expect(s, `${skill} ${ev.note}`).toBeDefined();
        for (const p of [s?.playerPos, ...(s?.enemies ?? []).map((e) => e.pos)]) {
          expect(p?.x).toBeGreaterThan(0);
          expect(p?.x).toBeLessThan(map.bounds.width + 5);
          expect(p?.y).toBeGreaterThan(-5);
        }
        expect(s?.enemies.length).toBeGreaterThan(0);
        expect(s?.roundTimeLeftS).toBeGreaterThanOrEqual(0);
      }
    }
  });

  test('a clutch situation lists every living enemy', () => {
    const r = analyze(generateMatches(weakProfile('duelist', 'clutch', 0.05), 5), PLAYER_ID);
    for (const ev of r.findings.find((f) => f.skill === 'clutch')?.evidence ?? []) {
      const k = Number(/1v(\d)/.exec(ev.note)?.[1]);
      expect(ev.situation?.enemies.length).toBe(k);
    }
  });
});

describe('rehearsalDrill', () => {
  test('starts the approach: enemies where the evidence said, the player back out of their sight', () => {
    const d = rehearsalDrill(evidence(), finding('entry'), map, 7);
    expect(d.player.spawn).not.toEqual({ x: 30, y: 40 });
    expect(d.bots.map((b) => b.spawn)).toEqual([{ x: 64, y: 40 }, { x: 60, y: 50 }]);
    expect(d.player.team).toBe('attack');
    expect(d.bots.every((b) => b.team === 'defense' && b.hold)).toBe(true);
    // The walk back is added to the clock.
    expect(d.roundTimeLeftS).toBeGreaterThan(80);
    expect(d.player.carryingSpike).toBe(true);
    const run = startDrill(d);
    const at = (id: string) => run.state().agents.find((a) => a.id === id)?.pos;
    expect(at('player')).toEqual(d.player.spawn);
    expect(at('bot-1')).toEqual({ x: 64, y: 40 });
    expect(at('bot-2')).toEqual({ x: 60, y: 50 });
    expect(run.state().roundTimeLeftS).toBe(d.roundTimeLeftS);
  });

  test('the goal text says what happened and names the callout', () => {
    const d = rehearsalDrill(evidence(), finding('entry'), map, 7);
    expect(d.goal).toBe('Round 11: you lost the opening duel at Mid Road 0.6 s after contact. Same angle, a few seconds earlier: take it differently.');
    const c = rehearsalDrill(evidence({ side: 'attack', enemies: [{ pos: { x: 62, y: 40 } }] }), finding('clutch'), map, 7);
    expect(c.goal).toContain('Mid Road');
    expect(c.goal).toContain('1v1 clutch');
  });

  test('a tucked enemy is rebuilt as an off-angle anchor; a rusher on a defense round as an entry duelist', () => {
    const tucked = rehearsalDrill(evidence({ enemies: [{ pos: { x: 96.8, y: 60 } }], site: 'A' }), finding('entry'), map, 7);
    expect(tucked.bots[0].archetypeId).toBe('off-angle-anchor');
    const rush = rehearsalDrill(
      evidence({ side: 'defense', playerPos: { x: 70, y: 40 }, enemies: [{ pos: { x: 40, y: 30 } }], playerHasSpike: false }),
      finding('entry'),
      map,
      7,
    );
    expect(rush.player.team).toBe('defense');
    expect(rush.bots[0].team).toBe('attack');
    expect(rush.bots[0].archetypeId).toBe('entry-duelist');
  });

  test('spike state is rebuilt: a planted spike gives a retake with the seconds that were left', () => {
    const d = rehearsalDrill(
      evidence({ side: 'defense', playerPos: { x: 70, y: 40 }, enemies: [{ pos: { x: 90, y: 40 } }], spikePlanted: { site: 'B', pos: { x: 90.5, y: 40 }, secondsLeft: 22 }, roundTimeLeftS: 22, playerHasSpike: false }),
      finding('time_pressure'),
      map,
      7,
    );
    expect(d.spikePlanted?.site).toBe('B');
    expect(d.spikePlanted?.secondsLeft).toBeGreaterThanOrEqual(22);
    expect(d.player.carryingSpike).toBe(false);
    expect(startDrill(d).state().spike.status).toBe('planted');
  });

  test('a bot stands its ground at the rebuilt position while it sees nobody', () => {
    const d = rehearsalDrill(evidence({ enemies: [{ pos: { x: 96.8, y: 60 } }], playerPos: { x: 30, y: 10 }, site: 'A' }), finding('entry'), map, 7);
    const run = startDrill(d);
    for (let i = 0; i < 60; i++) run.step(idleInput('player'));
    const b = run.state().agents.find((a) => a.id === 'bot-1')?.pos;
    expect(Math.hypot((b?.x ?? 0) - d.bots[0].spawn.x, (b?.y ?? 0) - d.bots[0].spawn.y)).toBeLessThan(0.5);
  });

  test('evidence with no situation falls back to a plain drill for the skill', () => {
    const d = rehearsalDrill({ matchId: 'm', round: 2, tick: 5, note: 'lost it' }, finding('clutch'), map, 7);
    expect(d.skill).toBe('clutch');
    expect(d.bots.length).toBeGreaterThan(0);
  });

  test('rehearsals of every skill are practice drills that run and are deterministic', () => {
    for (const skill of SKILLS) {
      const s = sessionWithWeakness(skill, 5, NO_KEY);
      const a = s.rehearsals();
      const b = sessionWithWeakness(skill, 5, NO_KEY).rehearsals();
      expect(a, skill).toEqual(b);
      expect(a.length, skill).toBeGreaterThan(0);
      for (const d of a) {
        expect(d.seed).toBeLessThan(EXAM_SEED_BASE);
        expect(d.bots.length).toBeGreaterThan(0);
        const run = startDrill(d);
        for (let i = 0; i < 80 && !run.done(); i++) run.step(null);
        expect(run.finish().drillId).toBe(d.id);
      }
    }
  });

  test('the player starts out of every enemy sight and at least 8 m away, for every skill', () => {
    for (const skill of SKILLS) {
      for (const seed of [1, 2, 3, 4, 5, 6]) {
        for (const d of sessionWithWeakness(skill, seed, NO_KEY).rehearsals()) {
          const st = startDrill(d).state();
          const me = st.agents.find((a) => a.id === 'player');
          expect(me).toBeDefined();
          for (const a of st.agents.filter((x) => x.id !== 'player')) {
            expect(lineOfSight(me?.pos ?? a.pos, a.pos, map.walls), `${skill} seed ${seed} ${d.id}`).toBe(false);
            expect(Math.hypot((me?.pos.x ?? 0) - a.pos.x, (me?.pos.y ?? 0) - a.pos.y), `${skill} seed ${seed}`).toBeGreaterThanOrEqual(8);
          }
        }
      }
    }
  });

  test('a competent simulated player wins rehearsals of every skill, so they can be won', () => {
    for (const skill of SKILLS) {
      let n = 0;
      let won = 0;
      for (const seed of [1, 2, 3]) {
        for (const d of sessionWithWeakness(skill, seed, NO_KEY).rehearsals()) {
          n++;
          if (createSimulatedPlayer({ seed: 1, profile: profileWithWeakness(skill, 0.9, 0.9) }).play(d).success) won++;
        }
      }
      expect(n, skill).toBeGreaterThan(0);
      expect(won / n, `${skill}: ${won}/${n}`).toBeGreaterThanOrEqual(0.2);
    }
  });

  test('a time-pressure rehearsal leaves time to reach the site and plant or defuse', () => {
    for (const seed of [1, 2, 3, 4, 5, 6]) {
      for (const d of sessionWithWeakness('time_pressure', seed, NO_KEY).rehearsals()) {
        const goal = d.spikePlanted ? d.spikePlanted.pos : null;
        const target = goal ?? d.bots[0].spawn;
        const path = pathBetween(map, d.player.spawn, target) ?? [target];
        let len = 0;
        let at = d.player.spawn;
        for (const p of path) {
          len += Math.hypot(p.x - at.x, p.y - at.y);
          at = p;
        }
        const job = d.player.team === 'attack' ? PLANT_TIME_S : DEFUSE_TIME_S;
        // Site centre is not known to the test for a plant; the enemy stands in the site, so it is a close stand-in.
        expect(d.roundTimeLeftS, `${d.id}`).toBeGreaterThanOrEqual(len / RUN_SPEED + job - 3);
      }
    }
  });
});

// The play page's sample players (play/main.ts): the rehearsals a person actually gets.
const SAMPLES: [Skill, number][] = [
  ['entry', 11],
  ['clutch', 12],
  ['time_pressure', 13],
  ['smoke_usage', 14],
];
const sampleRehearsals = (skill: Skill, seed: number): DrillSpec[] =>
  CoachingSession.fromHistory(generateMatches(weakProfile('duelist', skill, 0.1), seed, { matches: 6 }), PLAYER_ID, { seed, ...NO_KEY }).rehearsals();

/** Ticks until the first bot `engage` while the player runs with `steer`, or null within `ticks`. */
function firstReaction(spec: DrillSpec, steer: (p: Vec2) => Vec2, ticks: number): number | null {
  const run = startDrill(spec);
  for (let t = 1; t <= ticks && !run.done(); t++) {
    const me = run.state().agents.find((a) => a.id === spec.player.id);
    const d = steer(me?.pos ?? spec.player.spawn);
    const len = Math.hypot(d.x, d.y) || 1;
    const r = run.step({ ...idleInput(spec.player.id), move: { x: d.x / len, y: d.y / len }, yaw: Math.atan2(d.y, d.x) });
    if (r.trace.some((e) => e.node === 'engage')) return t;
  }
  return null;
}

describe('rehearsal starts stay hidden on the way out', () => {
  // Found in the browser: a start out of sight at tick 0 was one step from a gap in the dock wall,
  // and the holding bot reacted 0.3 s after the player moved.
  const graceTicks = Math.floor((HIDDEN_RADIUS_M / RUN_SPEED) * 20);

  test.each(SAMPLES)('no bot reacts while the player covers the first 3 m in any direction (%s sample)', (skill, seed) => {
    for (const spec of sampleRehearsals(skill, seed)) {
      const target = spec.bots[0]?.hold?.pos ?? spec.bots[0].spawn;
      const route = [...(pathBetween(map, spec.player.spawn, target) ?? [target])];
      const ways: Record<string, (p: Vec2) => Vec2> = {
        route: (p) => {
          while (route.length > 1 && Math.hypot(route[0].x - p.x, route[0].y - p.y) < 0.6) route.shift();
          return { x: route[0].x - p.x, y: route[0].y - p.y };
        },
        north: () => ({ x: 0, y: 1 }),
        south: () => ({ x: 0, y: -1 }),
        east: () => ({ x: 1, y: 0 }),
        west: () => ({ x: -1, y: 0 }),
      };
      for (const [way, steer] of Object.entries(ways)) {
        expect(firstReaction(spec, steer, graceTicks), `${spec.id} moving ${way}`).toBeNull();
      }
    }
  });

  test.each(SAMPLES)('the player starts on their own side of the moment, not behind the enemy (%s sample)', (skill, seed) => {
    const logs = generateMatches(weakProfile('duelist', skill, 0.1), seed, { matches: 6 });
    const moments = (analyze(logs, PLAYER_ID).findings[0]?.evidence ?? []).filter((e) => e.situation).slice(0, 5);
    const specs = sampleRehearsals(skill, seed);
    expect(specs.length).toBe(moments.length);
    specs.forEach((spec, i) => {
      const moment = moments[i].situation?.playerPos as Vec2;
      const own = map.spawns[spec.player.team];
      const toOwn = (p: Vec2) => Math.min(...own.map((s) => Math.hypot(p.x - s.x, p.y - s.y)));
      // No further from their own spawn than where it happened (2 m rule + snapping): they come at it from the front.
      expect(toOwn(spec.player.spawn), spec.id).toBeLessThanOrEqual(toOwn(moment) + 4);
      expect(spec.id, spec.id).toMatch(/-hide-(strict|fov)$/);
    });
  });
});

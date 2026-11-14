import { describe, expect, test } from 'vitest';
import type { Skill, WeaknessFinding } from '../shared/coaching.ts';
import { testBoxMap } from '../shared/maps/index.ts';
import { createSim } from '../sim/sim.ts';
import { drillFor, drillSimOptions, buildDrill, traitsFor } from './drills.ts';
import { EXAM_SEED_BASE, examDrills, planNext, PRACTICE_SEED_MAX } from './plan.ts';
import { MAX_JUMP, newStaircase, recentRate, TARGET_SUCCESS, update } from './staircase.ts';
import { analyze } from './analyze.ts';
import { generateMatches, PLAYER_ID, weakProfile } from './synthetic.ts';

const SKILLS: Skill[] = ['entry', 'clutch', 'time_pressure', 'smoke_usage'];
const finding = (skill: Skill): WeaknessFinding => ({ skill, rate: 0.1, benchmark: 0.5, severity: 0.8, sampleSize: 20, summary: '', evidence: [] });
const KNOWN = ['entry-duelist', 'patient-lurker', 'off-angle-anchor', 'utility-controller', 'baseline'];

describe('drills', () => {
  test('are deterministic', () => {
    for (const s of SKILLS) expect(drillFor(finding(s), 0.5, testBoxMap, 12)).toEqual(drillFor(finding(s), 0.5, testBoxMap, 12));
  });

  for (const skill of SKILLS) {
    for (const d of [0, 0.5, 1]) {
      for (const seed of [1, 2, 3, 4]) {
        test(`${skill} d=${d} seed ${seed} starts a sim that matches the spec`, () => {
          const spec = drillFor(finding(skill), d, testBoxMap, seed);
          expect(spec.skill).toBe(skill);
          expect(spec.bots.length).toBeGreaterThanOrEqual(1);
          for (const b of spec.bots) {
            expect(KNOWN).toContain(b.archetypeId);
            expect(b.traits.reactionMs).toBeGreaterThanOrEqual(200);
            expect(b.traits.reactionMs).toBeLessThanOrEqual(450);
            expect(b.team).not.toBe(spec.player.team);
          }
          const sim = createSim(drillSimOptions(spec));
          const r = sim.step([]);
          expect(r.state.phase).not.toBe('ended');
          const player = r.state.agents.find((a) => a.id === spec.player.id);
          expect(player?.pos).toEqual(spec.player.spawn);
          expect(player?.hp).toBe(spec.player.hp);
          expect(player?.carryingSpike).toBe(spec.player.carryingSpike);
          for (const b of spec.bots) expect(r.state.agents.find((a) => a.id === b.id)?.pos).toEqual(b.spawn);
          if (spec.spikePlanted) {
            expect(r.state.phase).toBe('post_plant');
            expect(r.state.spike).toMatchObject({ status: 'planted', site: spec.spikePlanted.site });
            expect(r.state.roundTimeLeftS).toBeCloseTo(spec.spikePlanted.secondsLeft - 0.05, 5);
          } else {
            expect(r.state.phase).toBe('live');
            expect(r.state.roundTimeLeftS).toBeCloseTo(spec.roundTimeLeftS - 0.05, 5);
          }
        });
      }
    }
  }

  test('time pressure covers both the plant and the defuse variant', () => {
    const titles = new Set(Array.from({ length: 30 }, (_, i) => buildDrill('time_pressure', 0.5, testBoxMap, i + 1).title.split(' on ')[0]));
    expect(titles).toEqual(new Set(['Late plant', 'Late defuse']));
  });

  test('difficulty raises bot count, speed and accuracy and cuts time', () => {
    for (const skill of SKILLS) {
      // Compare on the same seed so only difficulty differs.
      const easy = buildDrill(skill, 0, testBoxMap, 10);
      const hard = buildDrill(skill, 1, testBoxMap, 10);
      expect(hard.bots.length).toBeGreaterThanOrEqual(easy.bots.length);
      expect(hard.bots[0].traits.reactionMs).toBeLessThan(easy.bots[0].traits.reactionMs);
      expect(hard.bots[0].traits.headAccuracy).toBeGreaterThan(easy.bots[0].traits.headAccuracy);
      expect(hard.roundTimeLeftS).toBeLessThanOrEqual(easy.roundTimeLeftS);
    }
    expect(buildDrill('clutch', 0, testBoxMap, 3).bots).toHaveLength(1);
    expect(buildDrill('clutch', 1, testBoxMap, 3).bots).toHaveLength(3);
    expect(traitsFor('baseline', 0).reactionMs).toBe(450);
    expect(traitsFor('baseline', 1).reactionMs).toBe(200);
  });

  test('a drill can actually be won: killing the bot in an entry drill ends the round for the player', () => {
    const spec = buildDrill('entry', 0, testBoxMap, 5);
    const sim = createSim(drillSimOptions(spec));
    const r = sim.step([
      {
        agentId: spec.player.id, move: { x: 0, y: 0 }, yaw: 0, walk: false, crouch: false,
        fire: null, smokeAt: null, plantHeld: false, defuseHeld: false,
      },
    ]);
    expect(r.state.agents).toHaveLength(1 + spec.bots.length);
  });
});

describe('staircase', () => {
  test('success makes it harder, failure easier, in the right proportion', () => {
    const s = newStaircase('entry', 0.5);
    const up = update(s, true);
    const down = update(s, false);
    expect(up.difficulty).toBeGreaterThan(0.5);
    expect(down.difficulty).toBeLessThan(0.5);
    // Weighted: a failure moves it TARGET/(1-TARGET) = 3x as far as a success.
    expect((0.5 - down.difficulty) / (up.difficulty - 0.5)).toBeCloseTo(TARGET_SUCCESS / (1 - TARGET_SUCCESS), 5);
  });

  test('stays in 0-1 and never jumps more than 0.15', () => {
    let s = newStaircase('clutch', 0.5);
    let seed = 1;
    const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
    for (let i = 0; i < 500; i++) {
      const next = update(s, rnd() < 0.5);
      expect(Math.abs(next.difficulty - s.difficulty)).toBeLessThanOrEqual(MAX_JUMP + 1e-9);
      expect(next.difficulty).toBeGreaterThanOrEqual(0);
      expect(next.difficulty).toBeLessThanOrEqual(1);
      s = next;
    }
    let low = newStaircase('clutch', 0);
    for (let i = 0; i < 10; i++) low = update(low, false);
    expect(low.difficulty).toBe(0);
    let high = newStaircase('clutch', 1);
    for (let i = 0; i < 10; i++) high = update(high, true);
    expect(high.difficulty).toBe(1);
  });

  test('settles where the player succeeds about 75% of the time', () => {
    // A learner who succeeds with probability 1 - difficulty: the fixed point is 0.25.
    let s = newStaircase('entry', 0.9);
    let seed = 7;
    const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
    const tail: number[] = [];
    for (let i = 0; i < 600; i++) {
      s = update(s, rnd() < 1 - s.difficulty);
      if (i >= 300) tail.push(s.difficulty);
    }
    const mean = tail.reduce((a, b) => a + b, 0) / tail.length;
    expect(mean).toBeGreaterThan(0.18);
    expect(mean).toBeLessThan(0.32);
  });

  test('state is plain JSON and pure', () => {
    const s = newStaircase('smoke_usage');
    const t = update(s, true);
    expect(s.history).toEqual([]);
    expect(JSON.parse(JSON.stringify(t))).toEqual(t);
    expect(recentRate(t)).toBe(1);
    expect(recentRate(s)).toBeNull();
    let h = s;
    for (let i = 0; i < 40; i++) h = update(h, true);
    expect(h.history).toHaveLength(20);
  });
});

describe('planner and exam', () => {
  const report = analyze(generateMatches(weakProfile('duelist', 'entry', 0.05), 1), PLAYER_ID);

  test('mostly drills the worst skill, sometimes the second, and never repeats a seed', () => {
    expect(report.findings[0].skill).toBe('entry');
    const hist: { skill: Skill; seed: number }[] = [];
    const counts = new Map<Skill, number>();
    for (let i = 0; i < 200; i++) {
      const next = planNext(report, {}, hist, testBoxMap, 3);
      counts.set(next.skill, (counts.get(next.skill) ?? 0) + 1);
      if (hist.length) expect(next.drill.seed).not.toBe(hist[hist.length - 1].seed);
      expect(next.drill.seed).toBeGreaterThanOrEqual(1);
      expect(next.drill.seed).toBeLessThanOrEqual(PRACTICE_SEED_MAX);
      hist.push({ skill: next.skill, seed: next.drill.seed });
    }
    expect(counts.get(report.findings[0].skill) ?? 0).toBeGreaterThan(110);
    expect(counts.get(report.findings[1].skill) ?? 0).toBeGreaterThan(20);
    expect(counts.size).toBe(2);
  });

  test('is deterministic and uses the staircase difficulty', () => {
    const a = planNext(report, {}, [], testBoxMap, 9);
    expect(planNext(report, {}, [], testBoxMap, 9)).toEqual(a);
    const states = { [a.skill]: newStaircase(a.skill, 0.9) };
    expect(planNext(report, states, [], testBoxMap, 9).drill.difficulty).toBe(0.9);
  });

  test('exam drills are fixed, cover every skill, and are held out from practice', () => {
    const exam = examDrills(testBoxMap, 0);
    expect(exam).toEqual(examDrills(testBoxMap, 0));
    expect(new Set(exam.map((d) => d.skill))).toEqual(new Set(SKILLS));
    expect(exam.length).toBe(8);
    for (const d of exam) expect(d.seed).toBeGreaterThanOrEqual(EXAM_SEED_BASE);

    // Every seed the planner can produce, every skill, at several difficulties.
    const practiceSpawns = new Set<string>();
    for (let seed = 1; seed <= PRACTICE_SEED_MAX; seed++) {
      for (const skill of SKILLS) {
        for (const diff of [0, 1]) {
          const d = buildDrill(skill, diff, testBoxMap, seed);
          practiceSpawns.add(JSON.stringify([skill, d.player.spawn]));
          for (const b of d.bots) practiceSpawns.add(JSON.stringify([skill, b.spawn]));
        }
      }
    }
    for (let form = 0; form < 100; form += 11) {
      for (const d of examDrills(testBoxMap, form)) {
        expect(d.seed).toBeGreaterThan(PRACTICE_SEED_MAX);
        expect(practiceSpawns.has(JSON.stringify([d.skill, d.player.spawn]))).toBe(false);
        for (const b of d.bots) expect(practiceSpawns.has(JSON.stringify([d.skill, b.spawn]))).toBe(false);
      }
    }
  });

  test('exam drills start sims that run', () => {
    for (const d of examDrills(testBoxMap, 3)) {
      const sim = createSim(drillSimOptions(d));
      expect(sim.step([]).state.agents.length).toBe(1 + d.bots.length);
    }
  });
});

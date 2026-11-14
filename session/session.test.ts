import { describe, expect, test } from 'vitest';
import type { DrillResult } from '../shared/coaching.ts';
import { EXAM_SEED_BASE } from '../analyzer/plan.ts';
import { generateMatches, weakProfile } from '../analyzer/synthetic.ts';
import { examOnce, practiceOnce, sessionWithWeakness } from './headless.ts';
import { CoachingSession } from './session.ts';
import { createSimulatedPlayer, LEARN_RATE, profileWithWeakness } from './simulated-player.ts';

const NO_KEY = { apiKey: null } as const;

describe('CoachingSession', () => {
  test('fromHistory finds the planted weakness, and an empty history still gives a plannable report', () => {
    const s = sessionWithWeakness('clutch', 4, NO_KEY);
    expect(s.report.findings[0].skill).toBe('clutch');
    const empty = CoachingSession.fromHistory([], 'me', NO_KEY);
    expect(empty.report.findings.map((f) => f.skill).length).toBe(4);
    expect(empty.nextDrill().drill.seed).toBeLessThan(EXAM_SEED_BASE);
  });

  test('complete() moves the staircase, writes a template debrief with no key, and ignores nothing silently', async () => {
    const s = CoachingSession.fromHistory(generateMatches(weakProfile('duelist', 'entry'), 2), 'me', { ...NO_KEY, seed: 2 });
    const { drill, skill } = s.nextDrill();
    const player = createSimulatedPlayer({ seed: 2, profile: profileWithWeakness(skill) });
    const before = s.staircase(skill).difficulty;
    const result = player.play(drill);
    const debrief = await s.complete(result);
    expect(debrief.source).toBe('template');
    expect(debrief.text.length).toBeGreaterThan(10);
    expect(s.staircase(skill).history).toEqual([result.success]);
    expect(s.staircase(skill).difficulty).not.toBe(before);
    expect(s.practiceLog).toHaveLength(1);
  });

  test('toJSON/fromJSON round-trips and the restored session plans the same drills', async () => {
    const a = sessionWithWeakness('time_pressure', 9, NO_KEY);
    const player = createSimulatedPlayer({ seed: 9, profile: profileWithWeakness('time_pressure') });
    for (let i = 0; i < 4; i++) await practiceOnce(a, player);
    const saved = JSON.stringify(a.toJSON());
    const b = CoachingSession.fromJSON(JSON.parse(saved), NO_KEY);
    expect(JSON.stringify(b.toJSON())).toBe(saved);
    expect(b.nextDrill()).toEqual(a.nextDrill());
  });

  test('exam and practice never share a seed or a player spawn', () => {
    const s = sessionWithWeakness('entry', 5, NO_KEY);
    const practice = Array.from({ length: 80 }, () => s.nextDrill().drill);
    const exam = [0, 1, 2, 17, 99].flatMap((f) => s.exam(f));
    const pSeeds = new Set(practice.map((d) => d.seed));
    const pSpawns = new Set(practice.map((d) => `${d.player.spawn.x},${d.player.spawn.y}`));
    for (const e of exam) {
      expect(e.seed).toBeGreaterThanOrEqual(EXAM_SEED_BASE);
      expect(pSeeds.has(e.seed)).toBe(false);
      expect(pSpawns.has(`${e.player.spawn.x},${e.player.spawn.y}`)).toBe(false);
    }
    for (const p of practice) expect(p.seed).toBeLessThan(EXAM_SEED_BASE);
  });

  test('examScore gives per-skill and overall pass rates and remembers them for the debrief', () => {
    const s = sessionWithWeakness('entry', 5, NO_KEY);
    const mk = (skill: DrillResult['skill'], success: boolean): DrillResult => ({ drillId: 'x', skill, success, ticks: 1, events: [], trace: [] });
    const score = s.examScore([mk('entry', true), mk('entry', false), mk('clutch', true)], 'before');
    expect(score.perSkill.entry).toEqual({ passed: 1, total: 2, rate: 0.5 });
    expect(score.perSkill.clutch.rate).toBe(1);
    expect(score.perSkill.smoke_usage.rate).toBeNull();
    expect(score.overall.rate).toBeCloseTo(2 / 3);
    expect(s.toJSON().examBefore).toBeCloseTo(2 / 3);
  });
});

describe('simulated player', () => {
  test('the learning rule raises a skill most when the drill is at its level, and never past 1', () => {
    const p = createSimulatedPlayer({ seed: 1, profile: { entry: 0.5, clutch: 0.5, time_pressure: 0.5, smoke_usage: 0.99 } });
    const s = sessionWithWeakness('entry', 1, NO_KEY);
    const near = { ...s.nextDrill().drill, skill: 'entry' as const, difficulty: 0.5 };
    const far = { ...near, skill: 'clutch' as const, difficulty: 1 };
    p.learn(near);
    p.learn(far);
    expect(p.profile.entry).toBeCloseTo(0.5 + LEARN_RATE);
    expect(p.profile.clutch).toBe(0.5);
    p.learn({ ...near, skill: 'smoke_usage', difficulty: 0.99 });
    p.learn({ ...near, skill: 'smoke_usage', difficulty: 0.99 });
    expect(p.profile.smoke_usage).toBeLessThanOrEqual(1);
  });

  test('a full headless session is deterministic given its seed', async () => {
    const run = async () => {
      const s = sessionWithWeakness('smoke_usage', 12, NO_KEY);
      const p = createSimulatedPlayer({ seed: 12, profile: profileWithWeakness('smoke_usage') });
      const pre = await examOnce(s, p, [0], 'before');
      const log: string[] = [];
      for (let i = 0; i < 6; i++) {
        const { drill, result } = await practiceOnce(s, p);
        log.push(`${drill.id}:${result.success}:${result.ticks}`);
      }
      const post = await examOnce(s, p, [0], 'after');
      return JSON.stringify({ pre, post, log, profile: p.profile, state: s.toJSON() });
    };
    expect(await run()).toBe(await run());
  });
});

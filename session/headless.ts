/** Helpers that run a CoachingSession against a SimulatedPlayer with no client. */
import type { DrillResult, DrillSpec } from '../shared/coaching.ts';
import { generateMatches, weakProfile } from '../analyzer/synthetic.ts';
import type { Skill } from '../shared/coaching.ts';
import { CoachingSession, type ExamScore, type SessionOptions } from './session.ts';
import type { SimulatedPlayer } from './simulated-player.ts';

/** A session whose history is synthetic matches with `weak` planted (the analyzer must find it). */
export function sessionWithWeakness(weak: Skill, seed: number, opts: SessionOptions = {}): CoachingSession {
  const logs = generateMatches(weakProfile('duelist', weak, 0.1), seed, { matches: 6 });
  return CoachingSession.fromHistory(logs, 'me', { ...opts, seed });
}

/** Plays every exam drill of one form; does not learn. */
export function playExam(player: SimulatedPlayer, drills: DrillSpec[]): DrillResult[] {
  return drills.map((d) => player.play(d));
}

export async function examOnce(session: CoachingSession, player: SimulatedPlayer, forms: number[], phase?: 'before' | 'after'): Promise<ExamScore> {
  const results = forms.flatMap((f) => playExam(player, session.exam(f)));
  return session.examScore(results, phase);
}

/** One practice drill from the planner: play, learn, complete (staircase + debrief). */
export async function practiceOnce(session: CoachingSession, player: SimulatedPlayer): Promise<{ drill: DrillSpec; result: DrillResult }> {
  const { drill } = session.nextDrill();
  const result = player.play(drill);
  player.learn(drill);
  await session.complete(result);
  return { drill, result };
}

/**
 * Difficulty staircase, one per skill. A weighted up/down rule (Kaernbach 1991)
 * aimed at a target success rate: a success makes the next drill harder by
 * step * (1 - target), a failure easier by step * target. At the target rate the
 * two cancel, so difficulty settles where the player succeeds about that often.
 *
 * Inspired by Wilson et al. (2019), "The Eighty Five Percent Rule for optimal
 * learning", which finds ~85% accuracy is the best training difficulty for
 * simple learners (binary classification trained by gradient descent). Humans
 * learning a shooter are not that, so we aim at 75%, inside the 70-80% band we
 * want, and treat it as a starting guess to tune on pilot data.
 *
 * Pure and serialisable: state is plain JSON, `update` returns a new state.
 */
import type { Skill } from '../shared/coaching.ts';

export const TARGET_SUCCESS = 0.75;
export const STEP = 0.2;
export const MAX_JUMP = 0.15;
const HISTORY = 20;

export interface StaircaseState {
  skill: Skill;
  /** 0 (easiest) to 1 (hardest). */
  difficulty: number;
  target: number;
  /** The last results, newest last, capped. */
  history: boolean[];
}

export function newStaircase(skill: Skill, difficulty = 0.3, target = TARGET_SUCCESS): StaircaseState {
  return { skill, difficulty: clamp01(difficulty), target, history: [] };
}

const clamp01 = (v: number): number => Math.min(1, Math.max(0, v));

/** The state after one drill result. */
export function update(state: StaircaseState, success: boolean): StaircaseState {
  const delta = Math.min(MAX_JUMP, success ? STEP * (1 - state.target) : STEP * state.target);
  return {
    ...state,
    difficulty: clamp01(Math.round((state.difficulty + (success ? delta : -delta)) * 1e6) / 1e6),
    history: [...state.history, success].slice(-HISTORY),
  };
}

/** Share of recent results that were successes, or null with none. */
export function recentRate(state: StaircaseState): number | null {
  return state.history.length ? state.history.filter(Boolean).length / state.history.length : null;
}

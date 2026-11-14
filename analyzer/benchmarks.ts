/**
 * PLACEHOLDER BENCHMARKS. These numbers are made up so the pipeline has
 * something to compare against. Replace them with rates measured from real
 * match data for each role (and ideally each rank) before any claim is made
 * about a real player.
 */
import type { MatchLog } from '../shared/contract.ts';
import type { Skill } from '../shared/coaching.ts';

export type Role = MatchLog['rounds'][number]['playerRole'];

/** Expected success rate per role and skill, 0-1. Placeholder values. */
export const BENCHMARKS: Record<Role, Record<Skill, number>> = {
  duelist: { entry: 0.52, clutch: 0.22, time_pressure: 0.5, smoke_usage: 0.45 },
  initiator: { entry: 0.45, clutch: 0.2, time_pressure: 0.5, smoke_usage: 0.6 },
  controller: { entry: 0.4, clutch: 0.2, time_pressure: 0.5, smoke_usage: 0.75 },
  sentinel: { entry: 0.4, clutch: 0.28, time_pressure: 0.5, smoke_usage: 0.5 },
};

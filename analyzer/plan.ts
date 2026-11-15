import { personaById, PERSONA_TRAITS, type PersonaId } from '../shared/personas.ts';
/**
 * Session planner and the transfer exam.
 *
 * `planNext` picks the next drill: mostly the worst skill, sometimes the second
 * (interleaving), at that skill's staircase difficulty, with a practice seed
 * that differs from the last one.
 *
 * Practice seeds are 1..8999. The exam uses seeds from 9000 up, which the
 * planner never produces, so exam drills are held out: their seeds, and with
 * them their spawns, never appear in practice.
 */
import type { MapDef } from '../shared/contract.ts';
import type { DrillSpec, Skill, WeaknessReport } from '../shared/coaching.ts';
import { createRng } from '../sim/rng.ts';
import { buildDrill, ecoDrill, EXAM_SEED_BASE } from './drills.ts';
import { newStaircase, type StaircaseState } from './staircase.ts';

export const PRACTICE_SEED_MAX = 8999;
export { EXAM_SEED_BASE };
/** Chance of drilling the second-worst skill instead of the worst. */
export const SECOND_SKILL_SHARE = 0.3;

export interface PlayedDrill {
  skill: Skill;
  seed: number;
}

export interface NextDrill {
  skill: Skill;
  drill: DrillSpec;
}

export function planNext(
  report: WeaknessReport,
  states: Partial<Record<Skill, StaircaseState>>,
  history: PlayedDrill[],
  map: MapDef,
  baseSeed = 0,
): NextDrill {
  if (report.findings.length === 0) throw new Error('planNext: the report has no findings');
  const rng = createRng((baseSeed * 7919 + history.length * 104729 + 1) >>> 0);
  const useSecond = report.findings.length > 1 && rng.next() < SECOND_SKILL_SHARE;
  const finding = report.findings[useSecond ? 1 : 0];
  const lastSeed = history.length ? history[history.length - 1].seed : -1;
  let seed = 1 + rng.int(PRACTICE_SEED_MAX);
  while (seed === lastSeed) seed = 1 + rng.int(PRACTICE_SEED_MAX);
  const state = states[finding.skill] ?? newStaircase(finding.skill);
  const drill = buildDrill(finding.skill, state.difficulty, map, seed);
  return { skill: finding.skill, drill: report.ecoLosses && history.length % 3 === 0 ? ecoDrill(drill) : drill };
}

const SKILLS: Skill[] = ['entry', 'clutch', 'time_pressure', 'smoke_usage'];

/**
 * A fixed held-out set: every skill at a medium and a hard difficulty (eight
 * drills). `seed` (0-99) picks one of 100 fixed forms, so a pre-test and a
 * post-test can use different forms of equal shape.
 */
export function examDrills(map: MapDef, seed = 0): DrillSpec[] {
  const form = ((Math.floor(seed) % 100) + 100) % 100;
  const out: DrillSpec[] = [];
  for (const [row, difficulty] of [0.4, 0.75].entries()) {
    for (const [i, skill] of SKILLS.entries()) {
      out.push(buildDrill(skill, difficulty, map, EXAM_SEED_BASE + form * 10 + row * 4 + i));
    }
  }
  return out;
}

/** An optional offer: ordinary planning and the held-out evidence stay unchanged. */
export function personaOffer(report: WeaknessReport) {
  const skill = report.findings[0]?.skill ?? 'entry';
  const persona: PersonaId = skill === 'clutch' ? 'wisp' : skill === 'time_pressure' ? 'zephyr' : 'ember';
  const profile = personaById(persona);
  return { persona, skill, title: `Learn from ${profile.name}`, beatTip: profile.beatTip };
}
export function personaDrill(report: WeaknessReport, map: MapDef, seed: number, difficulty = .3): DrillSpec {
  const offer = personaOffer(report);
  const drill = buildDrill(offer.skill, difficulty, map, seed);
  return { ...drill, id: `persona-${drill.id}`, title: offer.title, goal: `${drill.goal} ${offer.beatTip}`, bots: drill.bots.map(b => ({ ...b, archetypeId: offer.persona, traits: { ...PERSONA_TRAITS[offer.persona] } })) };
}

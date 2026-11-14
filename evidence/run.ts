/**
 * Evidence harness. Run: `node evidence/run.ts [--n 30] [--budget 20] [--forms 5]`.
 *
 * For each of N seeds, a simulated player with ONE planted weak skill takes the
 * held-out exam (pre-test), practises for a fixed budget of drills under each of
 * three conditions, and takes the same exam again (post-test):
 *
 *   baseline   generic bot practice: a random skill each drill, the `baseline`
 *              archetype at fixed medium difficulty (0.5), no adaptation. This
 *              mirrors today's co-op against AI.
 *   random     random skills, but difficulty follows the per-skill staircase
 *              (ablation: adaptation without targeting).
 *   targeted   our planner: the worst skill first (sometimes the second) from the
 *              analyzer's report on the player's (synthetic) match history, at the
 *              staircase difficulty.
 *
 * Every condition starts from the same player, the same pre-test score and the
 * same exam drills. Practice seeds are below 9000, exam seeds 9000+.
 *
 * WHAT THIS IS NOT. The learners are simulated and the learning rule
 * (session/simulated-player.ts: LEARN_RATE, LEARN_WIDTH) is ours and made up.
 * Targeting wins here partly because the rule says drills near your level teach
 * most. This shows that the pipeline (analyzer -> planner -> staircase -> drills
 * -> exam) finds and drills the planted weakness, and that the plumbing is
 * sound. It is not evidence of transfer to human players.
 *
 * Also reported: a fairness check that no bot (or the simulated player) ever
 * fires earlier than its reactionMs after it could first see the target.
 */
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { DrillSpec, Skill } from '../shared/coaching.ts';
import type { PlayerInput, SimState } from '../shared/contract.ts';
import { DEFAULT_MAP_ID, loadMap } from '../shared/maps/index.ts';
import { buildDrill, traitsFor } from '../analyzer/drills.ts';
import { PRACTICE_SEED_MAX } from '../analyzer/plan.ts';
import { newStaircase, type StaircaseState, update } from '../analyzer/staircase.ts';
import { DEFAULT_TRAITS } from '../bot/traits.ts';
import { FOV_RAD, HEARING_RANGE, REACQUIRE_TICKS } from '../bot/view.ts';
import { lineOfSight } from '../sim/geometry.ts';
import { createRng } from '../sim/rng.ts';
import { TICK_RATE } from '../sim/sim.ts';
import type { DrillRun } from '../session/drill-run.ts';
import { sessionWithWeakness } from '../session/headless.ts';
import { SKILLS } from '../session/session.ts';
import { createSimulatedPlayer, profileWithWeakness, type SimulatedPlayer, traitsForSkill } from '../session/simulated-player.ts';

export type Condition = 'baseline' | 'random' | 'targeted';
export const CONDITIONS: Condition[] = ['baseline', 'random', 'targeted'];

export interface Config {
  n: number;
  budget: number;
  forms: number;
  /** Competence of the planted weak skill and of the others. */
  weakLevel: number;
  otherLevel: number;
  bootstrap: number;
}
export const DEFAULTS: Config = { n: 30, budget: 20, forms: 5, weakLevel: 0.15, otherLevel: 0.7, bootstrap: 4000 };

export interface Fairness {
  fires: number;
  tooEarly: number;
}

/** Tracks, independently of the bots' own memory, when each shooter first saw each target. */
function fairnessHook(f: Fairness, player: () => SimulatedPlayer | null) {
  const trackers = new WeakMap<DrillRun, Map<string, { since: number; last: number }>>();
  return (run: DrillRun, before: SimState, playerInput: PlayerInput): void => {
    const spec = run.spec;
    const reaction = new Map(spec.bots.map((b) => [b.id, b.traits.reactionMs]));
    // A tripped circuit breaker hands the bot to the baseline traits (250 ms), which is by design.
    for (const id of Object.keys(run.match.tripped())) reaction.set(id, DEFAULT_TRAITS.reactionMs);
    let t = trackers.get(run);
    if (!t) {
      t = new Map();
      trackers.set(run, t);
    }
    const map = loadMap(spec.mapId);
    // Update who sees whom at this decision tick (same rule as bot/view.ts).
    for (const o of before.agents) {
      if (!o.alive) continue;
      for (const e of before.agents) {
        if (!e.alive || e.team === o.team) continue;
        const dx = e.pos.x - o.pos.x;
        const dy = e.pos.y - o.pos.y;
        const heard = Math.hypot(dx, dy) <= HEARING_RANGE;
        const los = lineOfSight(o.pos, e.pos, map.walls, before.smokes);
        let d = (Math.atan2(dy, dx) - o.yaw) % (2 * Math.PI);
        if (d > Math.PI) d -= 2 * Math.PI;
        if (d < -Math.PI) d += 2 * Math.PI;
        if (!((los && Math.abs(d) <= FOV_RAD / 2) || heard)) continue;
        const key = `${o.id}>${e.id}`;
        const prev = t.get(key);
        if (!prev || before.tick - prev.last > REACQUIRE_TICKS) t.set(key, { since: before.tick, last: before.tick });
        else prev.last = before.tick;
      }
    }
    const inputs = [...run.match.lastBotInputs(), playerInput];
    for (const inp of inputs) {
      if (!inp.fire) continue;
      const shooter = before.agents.find((a) => a.id === inp.agentId);
      if (!shooter?.alive) continue;
      const rms = inp.agentId === spec.player.id ? traitsForSkill(spec.skill, player()?.profile[spec.skill] ?? 0).reactionMs : reaction.get(inp.agentId);
      if (rms === undefined) continue;
      f.fires++;
      const seen = t.get(`${inp.agentId}>${inp.fire.targetId}`);
      if (!seen || ((before.tick - seen.since) * 1000) / TICK_RATE < rms) f.tooEarly++;
    }
  };
}

function mean(xs: number[]): number {
  return xs.length ? xs.reduce((s, x) => s + x, 0) / xs.length : 0;
}

/** Percentile bootstrap CI of the mean (deterministic). */
export function bootstrapCI(xs: number[], resamples: number, seed = 7): { mean: number; lo: number; hi: number } {
  const rng = createRng(seed);
  const means: number[] = [];
  for (let b = 0; b < resamples; b++) {
    let s = 0;
    for (let i = 0; i < xs.length; i++) s += xs[rng.int(xs.length)];
    means.push(s / xs.length);
  }
  means.sort((a, b) => a - b);
  return { mean: mean(xs), lo: means[Math.floor(0.025 * resamples)], hi: means[Math.min(resamples - 1, Math.ceil(0.975 * resamples) - 1)] };
}

interface ExamNumbers {
  weak: number;
  overall: number;
}

export interface Row {
  seed: number;
  weak: Skill;
  analyzerTop: Skill;
  pre: ExamNumbers;
  conditions: Record<Condition, { post: ExamNumbers; gainWeak: number; gainOverall: number; weakShareDrills: number; weakShareTicks: number; weakCompetence: number }>;
}

export interface Summary {
  gainWeak: { mean: number; lo: number; hi: number };
  gainOverall: { mean: number; lo: number; hi: number };
  weakShareDrills: number;
  weakShareTicks: number;
  weakCompetence: number;
}

export interface Results {
  config: Config;
  contractNote: string;
  rows: Row[];
  summary: Record<Condition, Summary>;
  targetedMinusBaseline: { gainWeak: { mean: number; lo: number; hi: number }; gainOverall: { mean: number; lo: number; hi: number } };
  targetedMinusRandom: { gainWeak: { mean: number; lo: number; hi: number }; gainOverall: { mean: number; lo: number; hi: number } };
  analyzerFoundWeakShare: number;
  preMeanWeak: number;
  fairness: Fairness & { rule: string };
  drillsPlayed: number;
}

export async function runEvidence(cfg: Config = DEFAULTS): Promise<Results> {
  const map = loadMap(DEFAULT_MAP_ID); // Saltworks, the three-site map
  const fairness: Fairness = { fires: 0, tooEarly: 0 };
  let drillsPlayed = 0;
  const rows: Row[] = [];

  for (let seed = 1; seed <= cfg.n; seed++) {
    const weak = SKILLS[seed % SKILLS.length];
    const profile = profileWithWeakness(weak, cfg.weakLevel, cfg.otherLevel);
    const forms = Array.from({ length: cfg.forms }, (_, i) => i);
    const base = sessionWithWeakness(weak, seed, { apiKey: null });
    const examDrills = forms.flatMap((f) => base.exam(f));

    const check = fairnessHook(fairness, () => current);
    let current: SimulatedPlayer | null = null;
    const makePlayer = (): SimulatedPlayer => {
      let self: SimulatedPlayer | null = null;
      self = createSimulatedPlayer({ seed, profile, hooks: { onTick: check } });
      current = self;
      return self;
    };
    const playMany = (p: SimulatedPlayer, drills: DrillSpec[]): ExamNumbers => {
      const results = drills.map((d) => p.play(d));
      drillsPlayed += results.length;
      const w = results.filter((r) => r.skill === weak);
      return { weak: w.filter((r) => r.success).length / w.length, overall: results.filter((r) => r.success).length / results.length };
    };

    const pre = playMany(makePlayer(), examDrills);
    const row: Row = { seed, weak, analyzerTop: base.report.findings[0].skill, pre, conditions: {} as Row['conditions'] };

    for (const cond of CONDITIONS) {
      const player = makePlayer();
      const session = sessionWithWeakness(weak, seed, { apiKey: null, rehearsalsFirst: false });
      const rng = createRng(seed * 7919 + CONDITIONS.indexOf(cond) * 104729 + 3);
      const stairs: Partial<Record<Skill, StaircaseState>> = {};
      let weakDrills = 0;
      let weakTicks = 0;
      let totalTicks = 0;
      for (let i = 0; i < cfg.budget; i++) {
        let spec: DrillSpec;
        if (cond === 'targeted') spec = session.nextDrill().drill;
        else {
          const skill = SKILLS[rng.int(SKILLS.length)];
          const pseed = 1 + rng.int(PRACTICE_SEED_MAX);
          if (cond === 'baseline') {
            const d = buildDrill(skill, 0.5, map, pseed);
            spec = { ...d, bots: d.bots.map((b) => ({ ...b, archetypeId: 'baseline', traits: traitsFor('baseline', 0.5) })) };
          } else spec = buildDrill(skill, (stairs[skill] ?? newStaircase(skill)).difficulty, map, pseed);
        }
        const result = player.play(spec);
        drillsPlayed++;
        player.learn(spec);
        if (cond === 'targeted') await session.complete(result);
        else if (cond === 'random') stairs[spec.skill] = update(stairs[spec.skill] ?? newStaircase(spec.skill), result.success);
        totalTicks += result.ticks;
        if (spec.skill === weak) {
          weakDrills++;
          weakTicks += result.ticks;
        }
      }
      const post = playMany(player, examDrills);
      row.conditions[cond] = {
        post,
        gainWeak: post.weak - pre.weak,
        gainOverall: post.overall - pre.overall,
        weakShareDrills: weakDrills / cfg.budget,
        weakShareTicks: totalTicks ? weakTicks / totalTicks : 0,
        weakCompetence: player.profile[weak],
      };
    }
    rows.push(row);
  }

  const pick = (c: Condition, f: (r: Row['conditions'][Condition]) => number): number[] => rows.map((r) => f(r.conditions[c]));
  const summary = {} as Record<Condition, Summary>;
  for (const c of CONDITIONS) {
    summary[c] = {
      gainWeak: bootstrapCI(pick(c, (x) => x.gainWeak), cfg.bootstrap, 11),
      gainOverall: bootstrapCI(pick(c, (x) => x.gainOverall), cfg.bootstrap, 13),
      weakShareDrills: mean(pick(c, (x) => x.weakShareDrills)),
      weakShareTicks: mean(pick(c, (x) => x.weakShareTicks)),
      weakCompetence: mean(pick(c, (x) => x.weakCompetence)),
    };
  }
  const diff = (a: Condition, b: Condition) => ({
    gainWeak: bootstrapCI(rows.map((r) => r.conditions[a].gainWeak - r.conditions[b].gainWeak), cfg.bootstrap, 17),
    gainOverall: bootstrapCI(rows.map((r) => r.conditions[a].gainOverall - r.conditions[b].gainOverall), cfg.bootstrap, 19),
  });
  return {
    config: cfg,
    contractNote: 'Simulated learners with a made-up learning rule (session/simulated-player.ts). Shows the mechanism, not human transfer.',
    rows,
    summary,
    targetedMinusBaseline: diff('targeted', 'baseline'),
    targetedMinusRandom: diff('targeted', 'random'),
    analyzerFoundWeakShare: rows.filter((r) => r.analyzerTop === r.weak).length / rows.length,
    preMeanWeak: mean(rows.map((r) => r.pre.weak)),
    fairness: { ...fairness, rule: 'fire inputs issued earlier than reactionMs after the shooter first saw the target (must be 0)' },
    drillsPlayed,
  };
}

const pc = (x: number): string => `${(x * 100).toFixed(1)}`;
const ci = (c: { mean: number; lo: number; hi: number }): string => `${pc(c.mean)} [${pc(c.lo)}, ${pc(c.hi)}]`;

export function markdown(r: Results): string {
  const lines: string[] = [];
  lines.push('# Evidence: simulated learners', '');
  lines.push(
    `N = ${r.config.n} simulated players, each with one planted weak skill (competence ${r.config.weakLevel}, the others ${r.config.otherLevel}). ` +
      `Practice budget ${r.config.budget} drills per condition; exam = ${r.config.forms} fixed held-out forms (${r.config.forms * 2} drills per skill), same drills before and after. ` +
      'Gains are percentage points of exam pass rate, mean with 95% bootstrap CI over players.',
    '',
  );
  lines.push('**The learners are simulated and the learning rule is ours (a made-up rule: a drill raises its skill by 0.04 x how close its difficulty is to the player\'s level). This shows the mechanism works end to end, not that humans transfer.**', '');
  lines.push('| Condition | Exam gain, weak skill (pp) | Exam gain, overall (pp) | Practice drills on weak skill | Practice time on weak skill |');
  lines.push('| --- | --- | --- | --- | --- |');
  for (const c of CONDITIONS) {
    const s = r.summary[c];
    lines.push(`| ${c} | ${ci(s.gainWeak)} | ${ci(s.gainOverall)} | ${pc(s.weakShareDrills)}% | ${pc(s.weakShareTicks)}% |`);
  }
  lines.push('');
  lines.push(`Targeted minus baseline: weak skill ${ci(r.targetedMinusBaseline.gainWeak)} pp, overall ${ci(r.targetedMinusBaseline.gainOverall)} pp.`);
  lines.push(`Targeted minus random-with-staircase: weak skill ${ci(r.targetedMinusRandom.gainWeak)} pp, overall ${ci(r.targetedMinusRandom.gainOverall)} pp.`, '');
  lines.push('By planted weak skill (mean weak-skill exam gain, pp; baseline / random / targeted):', '');
  lines.push('| Planted weakness | Players | baseline | random | targeted | Weak competence after (targeted) |');
  lines.push('| --- | --- | --- | --- | --- | --- |');
  for (const sk of SKILLS) {
    const rs = r.rows.filter((x) => x.weak === sk);
    if (!rs.length) continue;
    const g = (c: Condition) => pc(mean(rs.map((x) => x.conditions[c].gainWeak)));
    lines.push(`| ${sk} | ${rs.length} | ${g('baseline')} | ${g('random')} | ${g('targeted')} | ${mean(rs.map((x) => x.conditions.targeted.weakCompetence)).toFixed(2)} (from ${r.config.weakLevel}) |`);
  }
  lines.push('');
  lines.push('Clutch caveat: the exam\'s clutch drills are 1v2 and 1v3 post-plant fights, and the simulated player almost never wins one at those difficulties even with the weak skill trained (about 0% before, 3% after on Saltworks, the three-site map), so the exam barely moves on clutch; the staircase trains it at 1v1 where it can. Saltworks is a long map: the late-plant and late-defuse clocks are stretched by the extra walking time (`longWalkS` in `analyzer/drills.ts`), without which the time-pressure exam is almost all failures. Rehearsal drills are not part of this comparison (`rehearsalsFirst: false`).', '');
  lines.push(`The analyzer put the planted weak skill first for ${pc(r.analyzerFoundWeakShare)}% of players (from 6 synthetic matches each). Mean pre-test pass rate on the weak skill: ${pc(r.preMeanWeak)}%.`, '');
  lines.push(`Fairness: ${r.fairness.fires} fire inputs checked over ${r.drillsPlayed} drills; ${r.fairness.tooEarly} earlier than the shooter's reactionMs after first sight (must be 0).`, '');
  return `${lines.join('\n')}\n`;
}

async function main(): Promise<void> {
  const args = process.argv.slice(2);
  const get = (k: string, d: number): number => {
    const i = args.indexOf(`--${k}`);
    return i >= 0 ? Number(args[i + 1]) : d;
  };
  const cfg: Config = { ...DEFAULTS, n: get('n', DEFAULTS.n), budget: get('budget', DEFAULTS.budget), forms: get('forms', DEFAULTS.forms) };
  const t0 = Date.now();
  const results = await runEvidence(cfg);
  const dir = dirname(fileURLToPath(import.meta.url));
  writeFileSync(join(dir, 'results.json'), `${JSON.stringify(results, null, 2)}\n`);
  writeFileSync(join(dir, 'RESULTS.md'), markdown(results));
  console.log(markdown(results));
  console.log(`done in ${((Date.now() - t0) / 1000).toFixed(1)} s`);
  if (results.fairness.tooEarly > 0) process.exit(1);
}

if (process.argv[1]?.endsWith('evidence/run.ts')) await main();

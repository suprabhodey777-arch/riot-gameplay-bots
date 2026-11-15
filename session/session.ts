/**
 * CoachingSession: the one object the 3D client drives.
 *
 *   const s = CoachingSession.fromHistory(logs, 'me');   // weakness report
 *   const { drill } = s.nextDrill();                      // planner + staircases
 *   const run = startDrill(drill);                        // session/drill-run.ts
 *   ... client steps `run` with the player's input ...
 *   const debrief = await s.complete(run.finish());       // staircase + coach
 *   const exam = s.exam();                                // held-out drills
 *   const score = s.examScore(examResults);
 *   localStorage.setItem('s', JSON.stringify(s.toJSON())); // plain state
 *
 * Practice drills use seeds below 9000 (the planner), the exam seeds from 9000,
 * so they never share a drill (see analyzer/plan.ts). With no weakness findings
 * (no history), the report lists every skill with severity 0 in a fixed order, so
 * the planner rotates by its own interleaving rather than throwing.
 */
import type { Debrief, DrillResult, DrillSpec, Skill, WeaknessReport } from '../shared/coaching.ts';
import type { AgentId, MatchLog } from '../shared/contract.ts';
import { DEFAULT_MAP_ID, loadMap } from '../shared/maps/index.ts';
import { withPlayerAgent } from '../analyzer/drills.ts';
import { analyze } from '../analyzer/analyze.ts';
import { BENCHMARKS } from '../analyzer/benchmarks.ts';
import { rehearsalDrill } from '../analyzer/rehearse.ts';
import { EXAM_SEED_BASE, examDrills, type NextDrill, type PlayedDrill, planNext } from '../analyzer/plan.ts';
import { newStaircase, type StaircaseState, update } from '../analyzer/staircase.ts';
import { debrief as writeDebrief } from '../coach/debrief.ts';
import type { CoachModel } from '../coach/llm.ts';

export const SKILLS: Skill[] = ['entry', 'clutch', 'time_pressure', 'smoke_usage'];

export interface SessionOptions {
  /** A coach model; omit to use the template unless an API key exists. */
  model?: CoachModel;
  /** Key for the default model. Omit to read ANTHROPIC_API_KEY; null forces the template. */
  apiKey?: string | null;
  timeoutMs?: number;
  /** Offer a rehearsal of each of the worst finding's failed moments before generated drills (default true). */
  rehearsalsFirst?: boolean;
}

export interface PracticeRecord {
  drillId: string;
  skill: Skill;
  difficulty: number;
  success: boolean;
}

/** Everything that survives a save: plain JSON. */
export interface SessionState {
  version: 1;
  seed: number;
  mapId: string;
  playerAgent?: AgentId;
  historyMapId?: string;
  historyMaps?: Record<string, string>;
  playerId: string;
  report: WeaknessReport;
  staircases: Partial<Record<Skill, StaircaseState>>;
  history: PlayedDrill[];
  current: DrillSpec | null;
  /** Ids of the rehearsals already offered, so each moment comes up once. Absent in older saves. */
  rehearsed?: string[];
  practice: PracticeRecord[];
  examBefore: number | null;
  examAfter: number | null;
}

export interface SkillScore {
  passed: number;
  total: number;
  /** Pass rate 0-1, or null with no drills of that skill. */
  rate: number | null;
}

export interface ExamScore {
  perSkill: Record<Skill, SkillScore>;
  overall: SkillScore;
}

const score = (passed: number, total: number): SkillScore => ({ passed, total, rate: total ? passed / total : null });

/** A report with every skill and no evidence, for a player with no history. */
function emptyReport(playerId: string): WeaknessReport {
  return {
    playerId,
    role: 'duelist',
    matchesRead: 0,
    findings: SKILLS.map((skill) => ({
      skill,
      rate: BENCHMARKS.duelist[skill],
      benchmark: BENCHMARKS.duelist[skill],
      severity: 0,
      sampleSize: 0,
      summary: `No ${skill.replace('_', ' ')} situations in the history yet.`,
      evidence: [],
    })),
  };
}

export class CoachingSession {
  private s: SessionState;
  private opts: SessionOptions;

  private constructor(state: SessionState, opts: SessionOptions) {
    this.s = state;
    this.opts = opts;
  }

  static fromHistory(logs: MatchLog[], playerId: string, opts: SessionOptions & { seed?: number; mapId?: string } = {}): CoachingSession {
    let report = analyze(logs, playerId);
    if (report.findings.length === 0) report = { ...emptyReport(playerId), matchesRead: report.matchesRead, ...(report.ecoLosses ? { ecoLosses: report.ecoLosses } : {}) };
    const mapId = opts.mapId ?? logs.find((l) => l.playerId === playerId)?.mapId ?? DEFAULT_MAP_ID;
    return new CoachingSession(
      { version: 1, seed: opts.seed ?? 1, mapId, historyMaps: Object.fromEntries(logs.map(l => [l.matchId, l.mapId])), historyMapId: logs.find(l => l.playerId === playerId)?.mapId ?? mapId, playerId, report, staircases: {}, history: [], current: null, practice: [], examBefore: null, examAfter: null },
      opts,
    );
  }

  static fromJSON(state: SessionState, opts: SessionOptions = {}): CoachingSession {
    if (state.version !== 1) throw new Error(`Unknown session version ${String(state.version)}`);
    return new CoachingSession(JSON.parse(JSON.stringify(state)) as SessionState, opts);
  }

  /** A deep copy of the plain state; `JSON.stringify` it to save. */
  toJSON(): SessionState {
    return JSON.parse(JSON.stringify(this.s)) as SessionState;
  }

  get report(): WeaknessReport {
    return this.s.report;
  }
  get playerId(): string {
    return this.s.playerId;
  }
  get current(): DrillSpec | null {
    return this.s.current;
  }
  staircase(skill: Skill): StaircaseState {
    return this.s.staircases[skill] ?? newStaircase(skill);
  }
  get practiceLog(): PracticeRecord[] {
    return [...this.s.practice];
  }

  /**
   * One rehearsal drill per failed moment of the worst finding (at most MAX_EVIDENCE = 5): the
   * moment rebuilt with the same side, positions, enemies, clock and spike. Practice seeds only.
   */
  rehearsals(): DrillSpec[] {
    const finding = this.s.report.findings[0];
    // Only a credible weakness has mistakes to rehearse; a severity of 0 means the evidence is not there.
    if (!finding || finding.sampleSize === 0 || finding.severity <= 0) return [];
    // Each moment is rebuilt on the map it happened on, whatever map new drills use: picking a
    // different map used to drop every mistake from the history and leave nothing to rehearse.
    return finding.evidence
      .filter((e) => e.situation)
      .slice(0, 5)
      .map((e, i) => {
        const map = loadMap(this.s.historyMaps?.[e.matchId] ?? this.s.historyMapId ?? this.s.mapId);
        return withPlayerAgent(rehearsalDrill(e, finding, map, 1 + ((this.s.seed * 7919 + (i + 1) * 104729) % (EXAM_SEED_BASE - 1))), this.s.playerAgent);
      });
  }

  /**
   * The next practice drill. A rehearsal of the worst finding's next unplayed moment comes first
   * (unless `rehearsalsFirst` is false); after those, the worst skill (sometimes the second) at
   * that skill's staircase difficulty.
   */
  nextDrill(): NextDrill {
    const map = loadMap(this.s.mapId);
    if (this.opts.rehearsalsFirst !== false) {
      const done = new Set(this.s.rehearsed ?? []);
      const next = this.rehearsals().find((d) => !done.has(d.id));
      if (next) {
        this.s.rehearsed = [...done, next.id];
        this.s.history.push({ skill: next.skill, seed: next.seed });
        this.s.current = next;
        return { skill: next.skill, drill: next };
      }
    }
    const next = planNext(this.s.report, this.s.staircases, this.s.history, map, this.s.seed);
    next.drill = withPlayerAgent(next.drill, this.s.playerAgent);
    this.s.history.push({ skill: next.skill, seed: next.drill.seed });
    this.s.current = next.drill;
    return next;
  }

  /**
   * Records a finished practice drill: moves that skill's staircase and writes
   * the debrief. Exam drills (seed >= 9000) are not practice and do not move it.
   */
  async complete(result: DrillResult): Promise<Debrief> {
    const drill = this.s.current && this.s.current.id === result.drillId ? this.s.current : undefined;
    const isExam = drill ? drill.seed >= EXAM_SEED_BASE : false;
    if (!isExam) {
      this.s.staircases[result.skill] = update(this.staircase(result.skill), result.success);
      this.s.practice.push({ drillId: result.drillId, skill: result.skill, difficulty: drill?.difficulty ?? this.staircase(result.skill).difficulty, success: result.success });
    }
    const out = await writeDebrief(
      {
        report: this.s.report,
        result,
        drill,
        playerId: this.s.playerId,
        examBefore: this.s.examBefore ?? undefined,
        examAfter: this.s.examAfter ?? undefined,
      },
      { model: this.opts.model, apiKey: this.opts.apiKey, timeoutMs: this.opts.timeoutMs },
    );
    return { text: out.text, source: out.source };
  }

  /** The held-out exam drills (eight per form; forms 0-99). */
  exam(form = 0): DrillSpec[] {
    return examDrills(loadMap(this.s.mapId), form).map(d => withPlayerAgent(d, this.s.playerAgent));
  }

  /** Pass rate per skill and overall. With `phase`, also remembers the overall rate for later debriefs. */
  examScore(results: DrillResult[], phase?: 'before' | 'after'): ExamScore {
    const perSkill = {} as Record<Skill, SkillScore>;
    for (const skill of SKILLS) {
      const mine = results.filter((r) => r.skill === skill);
      perSkill[skill] = score(mine.filter((r) => r.success).length, mine.length);
    }
    const overall = score(results.filter((r) => r.success).length, results.length);
    if (phase === 'before') this.s.examBefore = overall.rate;
    else if (phase === 'after') this.s.examAfter = overall.rate;
    return { perSkill, overall };
  }
}

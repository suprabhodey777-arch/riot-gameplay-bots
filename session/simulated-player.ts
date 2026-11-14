/**
 * A scripted stand-in for a human, so the whole loop runs headless.
 *
 * It is the bot's own decision code (`createBot`, so the same fog of war and
 * reaction delay apply) driven by a SKILL PROFILE: a competence from 0 to 1 per
 * skill. Competence in the drill's skill sets the traits and overlays below; the
 * other skills' competences are not used in that drill.
 *
 *   entry          reaction 520 -> 200 ms, head accuracy .06 -> .30, body .40 -> .75,
 *                  aggression .25 -> .90, patience .80 -> .10 (c = 0 -> 1): a weak
 *                  player is slow, inaccurate and hangs back before the swing.
 *   clutch         reaction 400 -> 140 ms, head .10 -> .60, body .50 -> .95 (a composed player aims well;
 *                  without a stronger spread the 1v2 and 1v3 exam drills are unwinnable); and once per drill the player PANICS with probability 1 - c: it
 *                  abandons its position and runs at the defenders' side of the
 *                  map, reacting 200 ms slower and shooting at 30% of its normal
 *                  accuracy. Otherwise it plays the bot's patient post-plant guard.
 *   time_pressure  the player is slow to press: once the bot would hold plant or
 *                  defuse, the key is withheld for (1 - c) * 12 s.
 *   smoke_usage    with probability c the player throws a smoke on the watched
 *                  choke (within 3 m x (1 - c)) in the first half second; otherwise half
 *                  the time it never smokes, half the time it smokes a spot far
 *                  from the choke.
 *
 * LEARNING RULE (ours and made up; see LEARN_RATE). After each practice drill
 * the drill's skill rises by
 *     LEARN_RATE * max(0, 1 - |difficulty - competence| / LEARN_WIDTH)
 * capped at 1. Drills far from the player's level teach little, drills at their
 * level teach the most. This is the evidence harness's learner, not a claim
 * about how people learn.
 *
 * Deterministic: every random draw comes from an RNG seeded from the player's
 * seed and the drill id.
 */
import type { BotTraits, DrillResult, DrillSpec, Skill } from '../shared/coaching.ts';
import type { PlayerInput, SimState, Vec2 } from '../shared/contract.ts';
import { createBot } from '../bot/bot.ts';
import { loadMap } from '../shared/maps/index.ts';
import { hashStr, pathBetween } from '../bot/nav.ts';
import { createRng, type Rng } from '../sim/rng.ts';
import { TICK_RATE } from '../sim/sim.ts';
import { type DrillRun, startDrill } from './drill-run.ts';

export type SkillProfile = Record<Skill, number>;

/** Competence gained by a drill exactly at the player's level. */
export const LEARN_RATE = 0.04;
/** How far (in difficulty) from the player's level a drill still teaches anything. */
export const LEARN_WIDTH = 0.5;

/** A panicking clutch player reacts this much slower and shoots at this share of their normal accuracy. */
export const PANIC_EXTRA_REACTION_MS = 200;
export const PANIC_AIM = 0.3;

const SKILLS: Skill[] = ['entry', 'clutch', 'time_pressure', 'smoke_usage'];
const lerp = (a: number, b: number, t: number): number => a + (b - a) * t;
const clamp01 = (v: number): number => Math.min(1, Math.max(0, v));
const r2 = (v: number): number => Math.round(v * 100) / 100;

/** A profile with every skill at `level`, except `weak` at `weakLevel`. */
export function profileWithWeakness(weak: Skill, weakLevel = 0.15, level = 0.7): SkillProfile {
  const p = { entry: level, clutch: level, time_pressure: level, smoke_usage: level };
  p[weak] = weakLevel;
  return p;
}

const BASE: BotTraits = { reactionMs: 300, headAccuracy: 0.22, bodyAccuracy: 0.6, aggression: 0.5, offAngle: 0.3, patience: 0.5, utility: 0 };

/** The bot traits the player uses for a drill of `skill` at competence `c`. */
export function traitsForSkill(skill: Skill, c: number): BotTraits {
  const k = clamp01(c);
  if (skill === 'entry') {
    return {
      ...BASE,
      reactionMs: Math.round(lerp(520, 200, k)),
      headAccuracy: r2(lerp(0.06, 0.3, k)),
      bodyAccuracy: r2(lerp(0.4, 0.75, k)),
      aggression: r2(lerp(0.25, 0.9, k)),
      patience: r2(lerp(0.8, 0.1, k)),
    };
  }
  if (skill === 'clutch') {
    return { ...BASE, reactionMs: Math.round(lerp(400, 140, k)), headAccuracy: r2(lerp(0.1, 0.6, k)), bodyAccuracy: r2(lerp(0.5, 0.95, k)), aggression: 0.3, patience: 0.7 };
  }
  return { ...BASE };
}

export interface SimulatedPlayer {
  /** Live competences; `learn` changes them. */
  profile: SkillProfile;
  /** Plays a drill to its end and returns the result (does not learn). */
  play(spec: DrillSpec): DrillResult;
  /** Applies the learning rule for a practice drill. */
  learn(spec: DrillSpec): void;
  /** The traits (incl. reaction time) the player used for `spec`. */
  traitsFor(spec: DrillSpec): BotTraits;
}

export interface PlayHooks {
  /** Called after every tick with the run (for traces, fairness checks). */
  onTick?: (run: DrillRun, before: SimState, playerInput: PlayerInput) => void;
}

export function createSimulatedPlayer(opts: { seed: number; profile: SkillProfile; hooks?: PlayHooks }): SimulatedPlayer {
  const profile: SkillProfile = { ...opts.profile };

  function play(spec: DrillSpec): DrillResult {
    const c = profile[spec.skill];
    const map = loadMap(spec.mapId);
    const rng: Rng = createRng((Math.imul(opts.seed >>> 0, 2654435761) ^ hashStr(spec.id) ^ hashStr('sim-player')) >>> 0);
    const run = startDrill(spec);
    const pid = spec.player.id;
    const botRng = createRng(rng.next() * 4294967296);

    // Per-drill choices, drawn once, in a fixed order.
    const panics = spec.skill === 'clutch' && rng.next() < 1 - c;
    const base = traitsForSkill(spec.skill, c);
    const traits: BotTraits = panics
      ? { ...base, reactionMs: base.reactionMs + PANIC_EXTRA_REACTION_MS, headAccuracy: r2(base.headAccuracy * PANIC_AIM), bodyAccuracy: r2(base.bodyAccuracy * PANIC_AIM), aggression: 1, patience: 0 }
      : base;
    const bot = createBot({ id: pid, map, traits, rng: botRng, siteSeed: spec.seed });
    const ditherTicks = spec.skill === 'time_pressure' ? Math.round((1 - c) * 12 * TICK_RATE) : 0;
    const smokeRoll = rng.next();
    const smokeAtTick = 1 + rng.int(10);
    const smokeJitter = { x: (rng.next() * 2 - 1) * 3 * (1 - c), y: (rng.next() * 2 - 1) * 3 * (1 - c) };
    const choke = spec.bots[0]?.spawn ?? spec.player.spawn;
    let smokeTarget: Vec2 | null = null;
    if (spec.skill === 'smoke_usage') {
      if (smokeRoll < c) smokeTarget = { x: choke.x + smokeJitter.x, y: choke.y + smokeJitter.y };
      else if (smokeRoll < c + (1 - c) / 2) smokeTarget = farFrom(choke, spec.player.spawn);
    }
    const defenseGoal = centroidOf(map.spawns.defense);
    let wantedSince: number | null = null;
    let steerPath: { key: string; pts: Vec2[]; i: number } | null = null;

    while (!run.done()) {
      const before = run.state();
      const me = before.agents.find((a) => a.id === pid);
      const d = bot.decide(before);
      let input: PlayerInput = { ...d.input, agentId: pid };
      if (me?.alive) {
        if (panics && d.node !== 'engage' && d.node !== 'defuse') {
          const key = `${Math.round(defenseGoal.x)},${Math.round(defenseGoal.y)}`;
          if (!steerPath || steerPath.key !== key || before.tick % 40 === 0) steerPath = { key, pts: pathBetween(map, me.pos, defenseGoal) ?? [defenseGoal], i: 0 };
          while (steerPath.i < steerPath.pts.length - 1 && Math.hypot(me.pos.x - steerPath.pts[steerPath.i].x, me.pos.y - steerPath.pts[steerPath.i].y) < 0.8) steerPath.i++;
          const wp = steerPath.pts[steerPath.i];
          const len = Math.hypot(wp.x - me.pos.x, wp.y - me.pos.y) || 1;
          input = { ...input, move: { x: (wp.x - me.pos.x) / len, y: (wp.y - me.pos.y) / len }, yaw: Math.atan2(wp.y - me.pos.y, wp.x - me.pos.x), walk: false, crouch: false };
        }
        if (ditherTicks > 0 && (input.plantHeld || input.defuseHeld)) {
          wantedSince ??= before.tick;
          if (before.tick - wantedSince < ditherTicks) input = { ...input, plantHeld: false, defuseHeld: false };
        } else if (!input.plantHeld && !input.defuseHeld) wantedSince = null;
        if (smokeTarget && before.tick + 1 >= smokeAtTick && me.smokesLeft > 0) {
          input = { ...input, smokeAt: smokeTarget };
          smokeTarget = null;
        }
      }
      run.step(input);
      opts.hooks?.onTick?.(run, before, input);
    }
    return run.finish();
  }

  return {
    profile,
    play,
    learn(spec) {
      const c = profile[spec.skill];
      const closeness = Math.max(0, 1 - Math.abs(spec.difficulty - c) / LEARN_WIDTH);
      profile[spec.skill] = clamp01(c + LEARN_RATE * closeness);
    },
    traitsFor: (spec) => traitsForSkill(spec.skill, profile[spec.skill]),
  };
}

const centroidOf = (pts: Vec2[]): Vec2 => ({ x: pts.reduce((s, p) => s + p.x, 0) / pts.length, y: pts.reduce((s, p) => s + p.y, 0) / pts.length });

/** A point about 14 m from `choke` on the player's side: a smoke that does nothing useful. */
function farFrom(choke: Vec2, from: Vec2): Vec2 {
  const d = Math.hypot(from.x - choke.x, from.y - choke.y) || 1;
  const k = Math.min(14, d * 0.6);
  return { x: choke.x + ((from.x - choke.x) / d) * k, y: choke.y + ((from.y - choke.y) / d) * k };
}

export { SKILLS };

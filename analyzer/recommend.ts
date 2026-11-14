/**
 * Agent advice from a player's own matches: which of our agents to main, which
 * to practise the weakest skill with, and the VALORANT agents that play the same
 * way, so the advice carries over to the real game.
 *
 * Two inputs, both measured, never guessed:
 *   - the weakness report (rate against benchmark for each skill), and
 *   - a playstyle read straight from the logged events: how often the player is
 *     in the round's first duel, how far away their kills are, how many smokes
 *     they throw, and how often they plant or defuse.
 *
 * The main pick plays to the strongest skill and style; the practice pick is the
 * kit that makes the weakest skill easier to learn (a flash before an entry,
 * a reveal before a clutch). A finding with too little evidence is never called
 * a weakness here either: with no credible weakness the practice pick is the
 * main pick.
 */
import type { MatchLog, SimEvent, Vec2 } from '../shared/contract.ts';
import type { Skill, WeaknessReport } from '../shared/coaching.ts';

export type AgentId = 'rook' | 'haze' | 'wren' | 'pike';

export interface AgentInfo {
  id: AgentId;
  name: string;
  role: 'duelist' | 'controller' | 'initiator' | 'sentinel';
  kit: string;
  /** VALORANT agents with a similar role and kit, most similar first. */
  valorantLike: string[];
}

export const AGENTS: Record<AgentId, AgentInfo> = {
  rook: { id: 'rook', name: 'Rook', role: 'duelist', kit: 'Dash, Flash', valorantLike: ['Jett', 'Reyna', 'Phoenix', 'Neon'] },
  haze: { id: 'haze', name: 'Haze', role: 'controller', kit: '3 Smokes', valorantLike: ['Brimstone', 'Omen', 'Clove', 'Astra'] },
  wren: { id: 'wren', name: 'Wren', role: 'initiator', kit: '2 Flashes, Recon', valorantLike: ['Skye', 'KAY/O', 'Sova', 'Fade'] },
  pike: { id: 'pike', name: 'Pike', role: 'sentinel', kit: '2 Recons, Smoke', valorantLike: ['Cypher', 'Killjoy', 'Sage', 'Deadlock'] },
};

export interface Playstyle {
  rounds: number;
  /** Share of rounds where the player got or gave the round's first kill. */
  firstDuelShare: number;
  /** Share of those first duels the player won. */
  firstDuelWinRate: number;
  /** Mean distance of the player's kills, metres (null with no kills). */
  meanKillDistanceM: number | null;
  smokesPerRound: number;
  /** Share of rounds the player planted or defused. */
  objectiveShare: number;
  kills: number;
  deaths: number;
}

export interface AgentPick {
  agent: AgentInfo;
  /** One sentence in plain words, using only numbers from the playstyle and report. */
  why: string;
}

export interface AgentAdvice {
  playstyle: Playstyle;
  main: AgentPick;
  practice: AgentPick;
  /** Every agent with a 0-1 fit score, best first, for a picker UI. */
  ranked: { agent: AgentInfo; fit: number }[];
}

const dist = (a: Vec2, b: Vec2): number => Math.hypot(a.x - b.x, a.y - b.y);
const pct = (x: number): string => `${Math.round(x * 100)}%`;

/** Reads the playstyle of `playerId` from their logged rounds. */
export function readPlaystyle(logs: MatchLog[], playerId: string): Playstyle {
  let rounds = 0;
  let firstDuels = 0;
  let firstWins = 0;
  let kills = 0;
  let deaths = 0;
  let killDist = 0;
  let smokes = 0;
  let objectives = 0;
  for (const log of logs) {
    if (log.playerId !== playerId) continue;
    for (const round of log.rounds) {
      rounds++;
      const evs: SimEvent[] = round.events;
      const first = evs.find((e) => e.type === 'kill');
      if (first && first.type === 'kill' && (first.killer === playerId || first.victim === playerId)) {
        firstDuels++;
        if (first.killer === playerId) firstWins++;
      }
      let objective = false;
      for (const e of evs) {
        if (e.type === 'kill' && e.killer === playerId) {
          kills++;
          killDist += dist(e.killerPos, e.victimPos);
        } else if (e.type === 'kill' && e.victim === playerId) deaths++;
        else if (e.type === 'smoke' && e.agent === playerId) smokes++;
        else if ((e.type === 'plant' || e.type === 'defuse') && e.agent === playerId) objective = true;
      }
      if (objective) objectives++;
    }
  }
  const per = (n: number): number => (rounds ? n / rounds : 0);
  return {
    rounds,
    firstDuelShare: per(firstDuels),
    firstDuelWinRate: firstDuels ? firstWins / firstDuels : 0,
    meanKillDistanceM: kills ? killDist / kills : null,
    smokesPerRound: per(smokes),
    objectiveShare: per(objectives),
    kills,
    deaths,
  };
}

/** How well each skill is going, as rate over benchmark (1 = at benchmark). */
const ratios = (report: WeaknessReport): Partial<Record<Skill, number>> =>
  Object.fromEntries(report.findings.filter((f) => f.sampleSize > 0 && f.benchmark > 0).map((f) => [f.skill, f.rate / f.benchmark]));

/** The kit that helps most while learning each skill. */
const PRACTICE_FOR: Record<Skill, { agent: AgentId; how: string }> = {
  entry: { agent: 'wren', how: 'flash or recon the angle before you take the first fight, so you enter with information instead of guessing' },
  clutch: { agent: 'pike', how: 'recon tells you where the last enemies are, so a clutch becomes a plan instead of a coin flip' },
  time_pressure: { agent: 'rook', how: 'a dash buys the seconds you lose getting to the spike when the clock is short' },
  smoke_usage: { agent: 'haze', how: 'three smokes give you the reps to learn which choke to cut before you cross' },
};

const SKILL_WORDS: Record<Skill, string> = { entry: 'entry', clutch: 'clutch', time_pressure: 'time-pressure', smoke_usage: 'smoke' };

/** Agent advice for `playerId` from their logs and weakness report. */
export function recommendAgents(logs: MatchLog[], report: WeaknessReport): AgentAdvice {
  const style = readPlaystyle(logs, report.playerId);
  const r = ratios(report);
  const at = (s: Skill): number => r[s] ?? 1;

  // Fit 0-1 per agent: half skill strength, half playstyle.
  const clamp = (x: number): number => Math.max(0, Math.min(1, x));
  const longRange = style.meanKillDistanceM === null ? 0.5 : clamp((style.meanKillDistanceM - 8) / 20);
  const fit: Record<AgentId, number> = {
    rook: 0.5 * clamp(at('entry') / 1.5) + 0.5 * clamp(style.firstDuelShare * 2 * (0.5 + style.firstDuelWinRate)),
    haze: 0.5 * clamp(at('smoke_usage') / 1.5) + 0.5 * clamp(style.smokesPerRound / 1.5),
    wren: 0.5 * clamp((at('entry') + at('time_pressure')) / 3) + 0.5 * clamp(longRange * 0.6 + style.firstDuelShare),
    pike: 0.5 * clamp(at('clutch') / 1.5) + 0.5 * clamp(style.objectiveShare + (1 - style.firstDuelShare) * 0.5),
  };
  const ranked = (Object.keys(fit) as AgentId[]).map((id) => ({ agent: AGENTS[id], fit: Math.round(fit[id] * 100) / 100 })).sort((a, b) => b.fit - a.fit);
  const main = ranked[0].agent;

  const mainWhy = (() => {
    const like = main.valorantLike.slice(0, 2).join(' or ');
    switch (main.id) {
      case 'rook':
        return `You are in the first duel in ${pct(style.firstDuelShare)} of rounds and win ${pct(style.firstDuelWinRate)} of them: take the space, like ${like}.`;
      case 'haze':
        return `You throw ${style.smokesPerRound.toFixed(1)} smokes a round: you already play for the team's sight lines, like ${like}.`;
      case 'wren':
        return style.meanKillDistanceM === null
          ? `Your play is balanced between taking fights and setting them up: open fights for others, like ${like}.`
          : `Your kills average ${style.meanKillDistanceM.toFixed(0)} m away: find enemies first and let the team trade, like ${like}.`;
      case 'pike':
        return `You plant or defuse in ${pct(style.objectiveShare)} of rounds and are in the first duel in only ${pct(style.firstDuelShare)}: hold and anchor, like ${like}.`;
    }
  })();

  // Practice pick: the worst credible weakness. Severity 0 means not enough evidence.
  const weak = report.findings.find((f) => f.severity > 0);
  const practice = weak
    ? (() => {
        const p = PRACTICE_FOR[weak.skill];
        const agent = AGENTS[p.agent];
        return {
          agent,
          why: `Your ${SKILL_WORDS[weak.skill]} rate is ${pct(weak.rate)} against a ${pct(weak.benchmark)} benchmark: practise on ${agent.name} (in VALORANT, ${agent.valorantLike.slice(0, 2).join(' or ')}), because ${p.how}.`,
        };
      })()
    : { agent: main, why: `No skill is clearly below its benchmark yet, so keep practising on ${main.name}.` };

  return { playstyle: style, main: { agent: main, why: mainWhy }, practice, ranked };
}

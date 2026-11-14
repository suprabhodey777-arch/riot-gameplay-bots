/**
 * Weakness analyzer: reads match logs and measures four skills from events
 * alone. Every definition below is about what the log shows, not about intent.
 *
 * Teams: each round carries a `teams` map (agent id to team), so no team is
 * inferred. An agent missing from the map is ignored. A team has 5 players.
 *
 * "First contact" = the first spotted, damage or kill event of the round.
 * Times are seconds = ticks / 20. The round clock is 100 s from tick 0.
 *
 * ENTRY. Only rounds where the player's role is duelist and they are on
 *   attack. A round counts when the first damage or kill event of the round
 *   (the first duel) involves the player. It is won when the player is the
 *   attacker of that event, lost when they are the victim.
 *
 * CLUTCH. A round counts from the moment the player's last living teammate dies
 *   while the player is alive and at least one enemy is alive (any role, any
 *   side). Won when the player's team wins the round.
 *
 * TIME_PRESSURE. Two kinds of situation, any role:
 *   - Attack rounds where the plant (by anyone) happened with < 20 s on the
 *     clock, or where nobody planted and the round ended on time. Success when
 *     attack wins the round. A round lost on time counts as a failure.
 *   - Defense rounds where the player was alive at the plant and either a
 *     defuse finished with < 10 s left on the spike, or the spike detonated.
 *     Success when it was defused. A comfortable defuse (10 s or more to spare)
 *     is not pressure and is left out.
 *
 * SMOKE_USAGE. Every smoke the player threw. Useful when it was thrown before
 *   first contact AND lands within 15 m of where the first damage or kill of the
 *   round happened (the victim's position) or of the plant site's centre.
 *   Late or far smokes are wasted.
 */
import type { MapDef, MatchLog, SimEvent, Vec2 } from '../shared/contract.ts';
import type { Skill, WeaknessEvidence, WeaknessFinding, WeaknessReport } from '../shared/coaching.ts';
import { loadMap } from '../shared/maps/index.ts';
import { DETONATION_S, ROUND_TIME_S, TICK_RATE } from '../sim/sim.ts';
import { BENCHMARKS, type Role } from './benchmarks.ts';
import { calloutAt, centroid, dist } from './geo.ts';

export const TEAM_SIZE = 5;
export const LATE_PLANT_LEFT_S = 20;
export const TIGHT_DEFUSE_MARGIN_S = 10;
export const SMOKE_RANGE_M = 15;
export const MAX_EVIDENCE = 5;

type Round = MatchLog['rounds'][number];

/** Wilson score interval for k successes in n trials. */
export function wilson(k: number, n: number, z = 1.28): { lower: number; upper: number } {
  if (n === 0) return { lower: 0, upper: 1 };
  const p = k / n;
  const d = 1 + (z * z) / n;
  const c = p + (z * z) / (2 * n);
  const h = z * Math.sqrt((p * (1 - p)) / n + (z * z) / (4 * n * n));
  return { lower: (c - h) / d, upper: (c + h) / d };
}

/**
 * Severity, 0-1: the shortfall below the benchmark that we can be fairly sure
 * of. It is the gap between the benchmark and the upper end of the Wilson 80%
 * interval for the player's rate, relative to the benchmark. A big sample with a
 * clear gap scores high; a small sample cannot, because its interval is wide.
 */
export function severityOf(successes: number, n: number, benchmark: number): number {
  if (n <= 0) return 0;
  const gap = benchmark - wilson(successes, n).upper;
  return Math.max(0, Math.min(1, gap / benchmark));
}

const secs = (ticks: number): number => ticks / TICK_RATE;
const fmt = (s: number): string => (Math.round(s * 10) / 10).toString();

interface Sample {
  success: boolean;
  evidence: WeaknessEvidence;
}

function firstContactTick(events: SimEvent[]): number | null {
  let best: number | null = null;
  for (const e of events) {
    if ((e.type === 'spotted' || e.type === 'damage' || e.type === 'kill') && (best === null || e.tick < best)) best = e.tick;
  }
  return best;
}

function firstFight(events: SimEvent[]): (SimEvent & { type: 'damage' | 'kill' }) | null {
  let best: (SimEvent & { type: 'damage' | 'kill' }) | null = null;
  for (const e of events) {
    if ((e.type === 'damage' || e.type === 'kill') && (best === null || e.tick < best.tick)) best = e;
  }
  return best;
}

function entrySample(log: MatchLog, r: Round, playerId: string, map: MapDef): Sample | null {
  if (r.playerRole !== 'duelist' || r.playerTeam !== 'attack') return null;
  const fight = firstFight(r.events);
  if (!fight) return null;
  const attacker = fight.type === 'damage' ? fight.attacker : fight.killer;
  if (attacker !== playerId && fight.victim !== playerId) return null;
  const won = attacker === playerId;
  const contact = firstContactTick(r.events) ?? fight.tick;
  const where = calloutAt(map, fight.victimPos);
  const lead = fmt(secs(fight.tick - contact));
  return {
    success: won,
    evidence: {
      matchId: log.matchId,
      round: r.index,
      tick: fight.tick,
      note: won
        ? `won the opening duel at ${where}, ${lead} s after first contact`
        : `${fight.type === 'kill' ? 'died first' : 'lost the opening duel'} at ${where}, ${lead} s after first contact`,
    },
  };
}

function clutchSample(log: MatchLog, r: Round, playerId: string, map: MapDef): Sample | null {
    const deadAllies = new Set<string>();
  const deadEnemies = new Set<string>();
  const lastPos = { v: null as Vec2 | null };
  const events = [...r.events].sort((a, b) => a.tick - b.tick);
  for (const e of events) {
    if (e.type === 'damage') {
      if (e.attacker === playerId) lastPos.v = e.attackerPos;
      else if (e.victim === playerId) lastPos.v = e.victimPos;
    } else if (e.type === 'kill') {
      if (e.killer === playerId) lastPos.v = e.killerPos;
      else if (e.victim === playerId) lastPos.v = e.victimPos;
    }
    if (e.type !== 'kill') continue;
    if (e.victim === playerId) return null; // died before the clutch began
    const victimTeam = r.teams[e.victim];
    if (victimTeam === r.playerTeam) deadAllies.add(e.victim);
    else if (victimTeam) deadEnemies.add(e.victim);
    if (deadAllies.size >= TEAM_SIZE - 1 && deadEnemies.size < TEAM_SIZE) {
      const left = TEAM_SIZE - deadEnemies.size;
      const won = r.winner === r.playerTeam;
      const where = lastPos.v ? ` near ${calloutAt(map, lastPos.v)}` : '';
      return {
        success: won,
        evidence: {
          matchId: log.matchId,
          round: r.index,
          tick: e.tick,
          note: `${won ? 'won' : 'lost'} a 1v${left} clutch${where}, ${fmt(secs(e.tick))} s into the round`,
        },
      };
    }
  }
  return null;
}

function timePressureSample(log: MatchLog, r: Round, playerId: string): Sample | null {
  const plant = r.events.find((e): e is SimEvent & { type: 'plant' } => e.type === 'plant');
  const defuse = r.events.find((e): e is SimEvent & { type: 'defuse' } => e.type === 'defuse');
  const end = r.events.find((e): e is SimEvent & { type: 'round_end' } => e.type === 'round_end');
  if (r.playerTeam === 'attack') {
    if (plant) {
      const left = ROUND_TIME_S - secs(plant.tick);
      if (left >= LATE_PLANT_LEFT_S) return null;
      const won = r.winner === 'attack';
      return {
        success: won,
        evidence: {
          matchId: log.matchId,
          round: r.index,
          tick: plant.tick,
          note: `planted with ${fmt(left)} s left and ${won ? 'still won the round' : 'lost the round'}`,
        },
      };
    }
    if (end && end.reason === 'time') {
      return {
        success: false,
        evidence: { matchId: log.matchId, round: r.index, tick: end.tick, note: 'never planted; the round was lost on time' },
      };
    }
    return null;
  }
  if (!plant) return null;
  const dead = r.events.some((e) => e.type === 'kill' && e.victim === playerId && e.tick <= plant.tick);
  if (dead) return null;
  if (defuse) {
    const margin = DETONATION_S - secs(defuse.tick - plant.tick);
    if (margin >= TIGHT_DEFUSE_MARGIN_S) return null;
    return {
      success: true,
      evidence: { matchId: log.matchId, round: r.index, tick: defuse.tick, note: `defused with ${fmt(margin)} s to spare` },
    };
  }
  if (end && end.reason === 'detonated') {
    return {
      success: false,
      evidence: { matchId: log.matchId, round: r.index, tick: end.tick, note: 'the spike detonated before the retake finished a defuse' },
    };
  }
  return null;
}

function smokeSamples(log: MatchLog, r: Round, playerId: string, map: MapDef): Sample[] {
  const out: Sample[] = [];
  const contact = firstContactTick(r.events);
  const fight = firstFight(r.events);
  const plant = r.events.find((e): e is SimEvent & { type: 'plant' } => e.type === 'plant');
  const refs: Vec2[] = [];
  if (fight) refs.push(fight.victimPos);
  if (plant) refs.push(centroid(map.sites[plant.site]));
  for (const e of r.events) {
    if (e.type !== 'smoke' || e.agent !== playerId) continue;
    const early = contact === null || e.tick < contact;
    const nearest = refs.length ? Math.min(...refs.map((p) => dist(p, e.pos))) : Number.POSITIVE_INFINITY;
    const near = nearest <= SMOKE_RANGE_M;
    const where = calloutAt(map, e.pos);
    let note: string;
    if (early && near) note = `smoke at ${where} thrown ahead of contact and close to the fight`;
    else if (!early && contact !== null) note = `smoke at ${where} thrown ${fmt(secs(e.tick - contact))} s after first contact`;
    else if (!Number.isFinite(nearest)) note = `smoke at ${where} thrown in a round with no fight or plant`;
    else note = `smoke at ${where} landed ${Math.round(nearest)} m from where the fight happened`;
    out.push({ success: early && near, evidence: { matchId: log.matchId, round: r.index, tick: e.tick, note } });
  }
  return out;
}

/** Up to MAX_EVIDENCE failing samples, spread evenly over the list; passing ones only if nothing failed. */
function pickEvidence(samples: Sample[]): WeaknessEvidence[] {
  const fails = samples.filter((s) => !s.success);
  const pool = fails.length ? fails : [];
  if (pool.length <= MAX_EVIDENCE) return pool.map((s) => s.evidence);
  const out: WeaknessEvidence[] = [];
  for (let i = 0; i < MAX_EVIDENCE; i++) out.push(pool[Math.floor((i * pool.length) / MAX_EVIDENCE)].evidence);
  return out;
}

const SKILL_TEXT: Record<Skill, string> = {
  entry: 'opening duels won',
  clutch: 'clutches won',
  time_pressure: 'late-clock rounds converted',
  smoke_usage: 'smokes thrown early and close to the action',
};

export function analyze(logs: MatchLog[], playerId: string): WeaknessReport {
  const mine = logs.filter((l) => l.playerId === playerId);
  const samples: Record<Skill, Sample[]> = { entry: [], clutch: [], time_pressure: [], smoke_usage: [] };
  const roleCount = new Map<Role, number>();

  for (const log of mine) {
    const map = loadMap(log.mapId);
    for (const r of log.rounds) {
      roleCount.set(r.playerRole, (roleCount.get(r.playerRole) ?? 0) + 1);
      const e = entrySample(log, r, playerId, map);
      if (e) samples.entry.push(e);
      const c = clutchSample(log, r, playerId, map);
      if (c) samples.clutch.push(c);
      const t = timePressureSample(log, r, playerId);
      if (t) samples.time_pressure.push(t);
      samples.smoke_usage.push(...smokeSamples(log, r, playerId, map));
    }
  }

  let role: Role = 'duelist';
  let top = -1;
  for (const [k, v] of roleCount) {
    if (v > top) {
      top = v;
      role = k;
    }
  }

  const findings: WeaknessFinding[] = [];
  for (const skill of Object.keys(samples) as Skill[]) {
    const s = samples[skill];
    if (s.length === 0) continue;
    const wins = s.filter((x) => x.success).length;
    const rate = wins / s.length;
    const benchmark = BENCHMARKS[role][skill];
    findings.push({
      skill,
      rate,
      benchmark,
      severity: severityOf(wins, s.length, benchmark),
      sampleSize: s.length,
      summary: `${wins} of ${s.length} ${SKILL_TEXT[skill]} (${Math.round(rate * 100)}%), against a ${Math.round(benchmark * 100)}% benchmark for ${role}s.`,
      evidence: pickEvidence(s),
    });
  }
  findings.sort((a, b) => b.severity - a.severity);
  return { playerId, role, matchesRead: mine.length, findings };
}


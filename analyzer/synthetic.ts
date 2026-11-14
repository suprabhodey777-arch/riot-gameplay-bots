/**
 * Synthetic players. A profile is a role plus a true success rate per skill;
 * `generateMatches` writes match logs whose events carry those rates, so the
 * analyzer can be tested against a known answer. Deterministic from the seed.
 *
 * Each round is one of a few situation kinds, picked by weight (the role only
 * changes how often entry duels come up). Ids: the player is `me`, teammates
 * `a1`-`a4`, enemies `e1`-`e5`. Ticks start at 0 and run at 20 Hz; the round
 * clock is 100 s and the spike takes 45 s to detonate.
 *
 * Plausible but invented: this is not real match data and says nothing about
 * how real players behave.
 */
import type { MapDef, MatchLog, SimEvent, SiteId, Team, Vec2 } from '../shared/contract.ts';
import type { Skill } from '../shared/coaching.ts';
import { DEFAULT_MAP_ID, loadMap } from '../shared/maps/index.ts';
import { pointInPolygon, siteIds, sitePoly } from '../sim/geometry.ts';
import { createRng, type Rng } from '../sim/rng.ts';
import { DETONATION_S, TICK_RATE } from '../sim/sim.ts';
import { BENCHMARKS, type Role } from './benchmarks.ts';
import { bbox, centroid, dist } from './geo.ts';

export const PLAYER_ID = 'me';

export interface PlayerProfile {
  role: Role;
  /** True probability of succeeding at each skill's situation. */
  rates: Record<Skill, number>;
}

/** A player exactly at the benchmark for their role: nothing planted. */
export function neutralProfile(role: Role): PlayerProfile {
  return { role, rates: { ...BENCHMARKS[role] } };
}

/** A benchmark player with one skill pushed down to `rate`. */
export function weakProfile(role: Role, skill: Skill, rate = 0.1): PlayerProfile {
  const p = neutralProfile(role);
  p.rates[skill] = rate;
  return p;
}

export interface GenerateOptions {
  matches?: number;
  roundsPerMatch?: number;
  mapId?: string;
}

const ALLIES = ['a1', 'a2', 'a3', 'a4'];
const ENEMIES = ['e1', 'e2', 'e3', 'e4', 'e5'];
/** Callouts where fights are placed: every callout of the map that holds no spawn point. */
function fightCallouts(map: MapDef): string[] {
  const spawns = [...map.spawns.attack, ...map.spawns.defense];
  return map.callouts.filter((c) => !spawns.some((p) => pointInPolygon(p, c.polygon))).map((c) => c.name);
}
const T = (s: number): number => Math.round(s * TICK_RATE);

type Kind = 'entry' | 'clutch' | 'late' | 'standard' | 'retake_tight' | 'retake_easy';

class RoundBuilder {
  events: SimEvent[] = [];
  firstContact = 0;
  firstFightPos: Vec2 = { x: 0, y: 0 };
  plantSite: SiteId | null = null;
  readonly fightCallouts: string[];
  readonly rng: Rng;
  readonly map: MapDef;
  constructor(rng: Rng, map: MapDef) {
    this.rng = rng;
    this.map = map;
    this.fightCallouts = fightCallouts(map);
  }

  u(a: number, b: number): number {
    return a + this.rng.next() * (b - a);
  }
  chance(p: number): boolean {
    return this.rng.next() < p;
  }
  pick<X>(xs: X[]): X {
    return xs[this.rng.int(xs.length)];
  }
  /** A random point inside a named callout. */
  inCallout(name: string): Vec2 {
    const c = this.map.callouts.find((x) => x.name === name);
    if (!c) throw new Error(`no callout ${name}`);
    const b = bbox(c.polygon);
    for (let i = 0; i < 200; i++) {
      const p = { x: this.u(b.x0, b.x1), y: this.u(b.y0, b.y1) };
      if (pointInPolygon(p, c.polygon)) return p;
    }
    return centroid(c.polygon);
  }

  /** A fight. The first one sets the round's first contact and fight position. */
  duel(winner: string, loser: string, tick: number, callout: string, first = false): number {
    const posW = this.inCallout(callout);
    const posL = this.inCallout(callout);
    const hit = tick + Math.round(this.u(6, 16));
    if (first) {
      this.firstContact = tick;
      this.firstFightPos = posL;
      this.events.push({ type: 'spotted', tick, observer: winner, target: loser });
      this.events.push({ type: 'spotted', tick, observer: loser, target: winner });
    }
    let t = hit;
    if (this.chance(0.3)) {
      this.events.push({ type: 'damage', tick: t, attacker: winner, victim: loser, amount: 40, part: 'body', attackerPos: posW, victimPos: posL });
      t += 6;
    }
    this.events.push({ type: 'damage', tick: t, attacker: winner, victim: loser, amount: 150, part: 'head', attackerPos: posW, victimPos: posL });
    this.events.push({ type: 'kill', tick: t, killer: winner, victim: loser, part: 'head', killerPos: posW, victimPos: posL });
    return t;
  }

  plant(agent: string, tick: number): void {
    this.plantSite = this.pick(siteIds(this.map));
    this.events.push({ type: 'plant', tick, agent, site: this.plantSite });
  }

  end(tick: number, winner: Team, reason: 'elimination' | 'detonated' | 'defused' | 'time'): { winner: Team } {
    const last = Math.max(tick, ...this.events.map((e) => e.tick));
    this.events.push({ type: 'round_end', tick: last, winner, reason });
    return { winner };
  }
}

/** Attack-side ending after the first duel. `playerDead` means the player already died. */
function finishAttack(b: RoundBuilder, t0: number, win: boolean, playerDead: boolean): Team {
  if (!win && playerDead) {
    let t = t0;
    for (const a of ALLIES) {
      t += Math.round(b.u(40, 160));
      b.duel(b.pick(ENEMIES), a, t, b.pick(b.fightCallouts));
    }
    return b.end(t + 5, 'defense', 'elimination').winner;
  }
  const tp = Math.max(t0 + 120, T(100 - b.u(30, 70)));
  b.plant(b.pick(['a1', 'a2', 'a3']), tp);
  if (win) return b.end(tp + T(DETONATION_S), 'attack', 'detonated').winner;
  const td = tp + Math.round(b.u(T(15), T(40)));
  b.events.push({ type: 'defuse', tick: td, agent: b.pick(ENEMIES) });
  return b.end(td, 'defense', 'defused').winner;
}

function genRound(rng: Rng, profile: PlayerProfile, map: MapDef, index: number, half: number): MatchLog['rounds'][number] {
  const b = new RoundBuilder(rng, map);
  const team: Team = index < half ? 'attack' : 'defense';
  const { rates } = profile;
  const tc = Math.round(b.u(T(8), T(30)));
  const callout = b.pick(b.fightCallouts);

  const weights: [Kind, number][] =
    team === 'attack'
      ? [
          ['entry', profile.role === 'duelist' ? 0.4 : 0.15],
          ['clutch', 0.2],
          ['late', 0.25],
          ['standard', profile.role === 'duelist' ? 0.2 : 0.45],
        ]
      : [
          ['clutch', 0.2],
          ['retake_tight', 0.35],
          ['retake_easy', 0.1],
          ['standard', 0.4],
        ];
  let roll = rng.next() * weights.reduce((s, w) => s + w[1], 0);
  let kind: Kind = weights[0][0];
  for (const [k, w] of weights) {
    if (roll < w) {
      kind = k;
      break;
    }
    roll -= w;
  }

  let winner: Team;
  if (kind === 'entry') {
    const win = b.chance(rates.entry);
    const e = b.pick(ENEMIES);
    const t = win ? b.duel(PLAYER_ID, e, tc, callout, true) : b.duel(e, PLAYER_ID, tc, callout, true);
    winner = finishAttack(b, t, b.chance(win ? 0.7 : 0.35), !win);
  } else if (kind === 'clutch') {
    const k = 1 + rng.int(3); // enemies left when the clutch starts
    let t = b.duel('e1', 'a1', tc, callout, true);
    // Enemy deaths (all but k enemies, e1 survives) and ally deaths, interleaved in time.
    const dying = ENEMIES.slice(1, 1 + (5 - k)); // 5-k enemies die; e1 stays alive
    const survivors = ENEMIES.filter((x) => !dying.includes(x));
    const allyDeaths = ALLIES.slice(1);
    const order: { victim: string; ally: boolean }[] = [
      ...dying.map((v) => ({ victim: v, ally: false })),
      ...allyDeaths.map((v) => ({ victim: v, ally: true })),
    ];
    // Shuffle, then force an ally death last so the clutch begins after it.
    for (let i = order.length - 1; i > 0; i--) {
      const j = rng.int(i + 1);
      [order[i], order[j]] = [order[j], order[i]];
    }
    const lastAlly = order.map((o) => o.ally).lastIndexOf(true);
    [order[lastAlly], order[order.length - 1]] = [order[order.length - 1], order[lastAlly]];
    const deadAllies = new Set(['a1']);
    for (const o of order) {
      t += Math.round(b.u(30, 140));
      if (o.ally) {
        b.duel(b.pick(survivors), o.victim, t, b.pick(b.fightCallouts));
        deadAllies.add(o.victim);
      } else {
        const killers = ALLIES.filter((a) => !deadAllies.has(a)).concat(PLAYER_ID);
        b.duel(b.pick(killers), o.victim, t, b.pick(b.fightCallouts));
      }
    }
    // Player must not be a killer after dying: they are alive here. Now the 1vK.
    const win = b.chance(rates.clutch);
    const alive = survivors;
    if (win) {
      for (const e of alive) {
        t += Math.round(b.u(60, 200));
        b.duel(PLAYER_ID, e, t, callout);
      }
      winner = b.end(t + 2, team, 'elimination').winner;
    } else {
      t += Math.round(b.u(60, 200));
      b.duel(alive[0], PLAYER_ID, t, callout);
      winner = b.end(t + 2, team === 'attack' ? 'defense' : 'attack', 'elimination').winner;
    }
  } else if (kind === 'late') {
    b.duel('a1', 'e1', tc, callout, true);
    const ok = b.chance(rates.time_pressure);
    const tp = Math.round(b.u(T(82), T(97.5)));
    if (ok) {
      b.plant(b.pick(['a1', 'a2', 'me']), tp);
      winner = b.end(tp + T(DETONATION_S), 'attack', 'detonated').winner;
    } else if (b.chance(0.5)) {
      b.plant(b.pick(['a1', 'a2']), tp);
      const td = tp + Math.round(b.u(T(20), T(40)));
      b.events.push({ type: 'defuse', tick: td, agent: b.pick(ENEMIES) });
      winner = b.end(td, 'defense', 'defused').winner;
    } else {
      winner = b.end(T(100), 'defense', 'time').winner;
    }
  } else if (kind === 'retake_tight' || kind === 'retake_easy') {
    let t = b.duel('a1', 'e1', tc, callout, true);
    t = b.duel(PLAYER_ID, 'e2', t + Math.round(b.u(30, 100)), callout);
    const tp = t + Math.round(b.u(40, 120));
    b.plant('e3', tp);
    const tight = kind === 'retake_tight';
    const ok = tight ? b.chance(rates.time_pressure) : true;
    if (ok) {
      const margin = tight ? b.u(1, 9.5) : b.u(15, 30);
      const td = tp + T(DETONATION_S - margin);
      b.events.push({ type: 'defuse', tick: td, agent: b.pick([PLAYER_ID, 'a1', 'a2']) });
      winner = b.end(td, 'defense', 'defused').winner;
    } else {
      winner = b.end(tp + T(DETONATION_S), 'attack', 'detonated').winner;
    }
  } else if (team === 'attack') {
    const aWins = b.chance(0.5);
    const t = aWins ? b.duel('a1', 'e1', tc, callout, true) : b.duel('e1', 'a1', tc, callout, true);
    winner = finishAttack(b, t, b.chance(aWins ? 0.7 : 0.35), false);
  } else {
    const win = b.chance(0.5);
    let t = win ? b.duel('a1', 'e1', tc, callout, true) : b.duel('e1', 'a1', tc, callout, true);
    if (win) {
      for (const e of ENEMIES.slice(1)) {
        t += Math.round(b.u(40, 160));
        b.duel(b.pick(['a2', 'a3', PLAYER_ID]), e, t, b.pick(b.fightCallouts));
      }
      winner = b.end(t + 2, 'defense', 'elimination').winner;
    } else {
      // The player dies first so the round can never read as a clutch.
      t = b.duel('e2', PLAYER_ID, t + Math.round(b.u(40, 160)), callout);
      for (const a of ['a2', 'a3', 'a4']) {
        t += Math.round(b.u(40, 160));
        b.duel(b.pick(ENEMIES), a, t, b.pick(b.fightCallouts));
      }
      winner = b.end(t + 2, 'attack', 'elimination').winner;
    }
  }

  // Every enemy gets a look at the player once the round is live, which is what ties
  // teammates together for the analyzer's team inference.
  for (const e of ENEMIES) {
    b.events.push({ type: 'spotted', tick: b.firstContact + 1 + rng.int(10), observer: e, target: PLAYER_ID });
  }

  // Smokes: any round, independent of its kind.
  const roundEnd = b.events.find((e) => e.type === 'round_end')?.tick ?? T(100);
  const nSmokes = rng.next() < 0.25 ? 0 : rng.next() < 0.5 ? 1 : 2;
  const siteC = b.plantSite ? centroid(sitePoly(map, b.plantSite)) : null;
  const tcAct = b.firstContact;
  for (let i = 0; i < nSmokes; i++) {
    const good = b.chance(rates.smoke_usage);
    let tick: number;
    let pos: Vec2;
    const near = (): Vec2 => ({ x: b.firstFightPos.x + b.u(-5, 5), y: b.firstFightPos.y + b.u(-5, 5) });
    if (good) {
      tick = Math.max(10, tcAct - Math.round(b.u(40, 120)));
      pos = near();
    } else if (b.chance(0.5)) {
      tick = Math.min(roundEnd - 1, tcAct + Math.round(b.u(30, 100)));
      pos = near();
    } else {
      tick = Math.max(10, tcAct - Math.round(b.u(40, 120)));
      pos = b.firstFightPos;
      for (let n = 0; n < 500; n++) {
        const p = { x: b.u(3, map.bounds.width - 3), y: b.u(3, map.bounds.height - 3) };
        if (dist(p, b.firstFightPos) >= 22 && (!siteC || dist(p, siteC) >= 22)) {
          pos = p;
          break;
        }
      }
    }
    b.events.push({ type: 'smoke', tick, agent: PLAYER_ID, pos });
  }

  // round_end last, everything else in time order.
  const end = b.events.find((e) => e.type === 'round_end') as SimEvent;
  const rest = b.events.filter((e) => e !== end).sort((a, c) => a.tick - c.tick);
  if (end.tick < (rest.at(-1)?.tick ?? 0)) end.tick = rest.at(-1)?.tick ?? end.tick;
  const enemyTeam: Team = team === 'attack' ? 'defense' : 'attack';
  const teams: Record<string, Team> = { [PLAYER_ID]: team };
  for (const id of ALLIES) teams[id] = team;
  for (const id of ENEMIES) teams[id] = enemyTeam;
  return { index, playerTeam: team, playerRole: profile.role, teams, events: [...rest, end], winner };
}

/** Deterministic match logs for one profile. Match i uses an RNG derived from `seed` and i. */
export function generateMatches(profile: PlayerProfile, seed: number, opts: GenerateOptions = {}): MatchLog[] {
  const { matches = 8, roundsPerMatch = 24, mapId = DEFAULT_MAP_ID } = opts;
  const map = loadMap(mapId);
  const logs: MatchLog[] = [];
  for (let m = 0; m < matches; m++) {
    const rng = createRng((seed * 1000003 + m * 7919 + 12345) >>> 0);
    const rounds = [];
    for (let i = 0; i < roundsPerMatch; i++) rounds.push(genRound(rng, profile, map, i, roundsPerMatch / 2));
    logs.push({ matchId: `syn-${seed}-${m}`, mapId, playerId: PLAYER_ID, rounds });
  }
  return logs;
}

/**
 * A sample player's weak skill succeeds at this share of the role's benchmark. A flat 10% was a
 * strong weakness for skills benchmarked near 50% but a mild one for clutch (22% for duelists),
 * too mild for six matches to show: the clutch sample's history read as no weakness at all.
 */
export const SAMPLE_WEAK_SHARE = 0.2;

/** History for a sample duelist who is weak at `skill` (the play page's sample players). */
export function sampleHistory(skill: Skill, seed: number, matches = 6): MatchLog[] {
  return generateMatches(weakProfile('duelist', skill, BENCHMARKS.duelist[skill] * SAMPLE_WEAK_SHARE), seed, { matches });
}

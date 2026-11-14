import { describe, expect, test } from 'vitest';
import type { MatchLog } from '../shared/contract.ts';
import type { Skill } from '../shared/coaching.ts';
import { analyze, severityOf, wilson } from './analyze.ts';
import { generateMatches, neutralProfile, PLAYER_ID, weakProfile } from './synthetic.ts';

const SKILLS: Skill[] = ['entry', 'clutch', 'time_pressure', 'smoke_usage'];
const ROLES = ['duelist', 'initiator', 'controller', 'sentinel'] as const;
const SEEDS = Array.from({ length: 10 }, (_, i) => i + 1);

describe('synthetic players', () => {
  test('are deterministic from the seed and differ across seeds', () => {
    const p = neutralProfile('duelist');
    expect(generateMatches(p, 5)).toEqual(generateMatches(p, 5));
    expect(generateMatches(p, 5)).not.toEqual(generateMatches(p, 6));
  });

  test('logs are well formed: ticks from 0 at 20 Hz, sorted, one round_end last', () => {
    for (const log of generateMatches(neutralProfile('controller'), 3)) {
      expect(log.rounds).toHaveLength(24);
      for (const r of log.rounds) {
        const ticks = r.events.map((e) => e.tick);
        expect(ticks).toEqual([...ticks].sort((a, b) => a - b));
        expect(Math.min(...ticks)).toBeGreaterThanOrEqual(0);
        expect(r.events.filter((e) => e.type === 'round_end')).toHaveLength(1);
        expect(r.events.at(-1)?.type).toBe('round_end');
        const end = r.events.at(-1);
        expect(end && end.type === 'round_end' && end.winner).toBe(r.winner);
      }
    }
  });
});

describe('analyze', () => {
  for (const role of ROLES) {
    for (const skill of SKILLS) {
      if (skill === 'entry' && role !== 'duelist') continue;
      test(`a ${role} with weak ${skill} has it ranked worst (at least 9 of 10 seeds)`, () => {
        const hits = SEEDS.filter(
          (s) => analyze(generateMatches(weakProfile(role, skill, 0.05), s), PLAYER_ID).findings[0]?.skill === skill,
        ).length;
        expect(hits).toBeGreaterThanOrEqual(9);
      });
    }
  }

  test('a player with nothing planted gets low severities', () => {
    for (const role of ROLES) {
      const sev = SEEDS.map((s) => analyze(generateMatches(neutralProfile(role), s), PLAYER_ID).findings[0].severity);
      expect(Math.max(...sev)).toBeLessThan(0.5);
      expect(sev.filter((v) => v < 0.3).length).toBeGreaterThanOrEqual(8);
    }
  });

  test('a planted weakness scores clearly higher than the same player with none', () => {
    const weak = analyze(generateMatches(weakProfile('duelist', 'entry', 0.05), 2), PLAYER_ID);
    const fine = analyze(generateMatches(neutralProfile('duelist'), 2), PLAYER_ID);
    expect(weak.findings[0].severity).toBeGreaterThan(0.5);
    expect(weak.findings[0].severity).toBeGreaterThan(fine.findings[0].severity + 0.3);
  });

  test('findings are sorted worst first and rates stay in range', () => {
    const r = analyze(generateMatches(weakProfile('sentinel', 'clutch', 0.05), 4), PLAYER_ID);
    const sev = r.findings.map((f) => f.severity);
    expect(sev).toEqual([...sev].sort((a, b) => b - a));
    for (const f of r.findings) {
      expect(f.rate).toBeGreaterThanOrEqual(0);
      expect(f.rate).toBeLessThanOrEqual(1);
      expect(f.severity).toBeGreaterThanOrEqual(0);
      expect(f.severity).toBeLessThanOrEqual(1);
      expect(f.sampleSize).toBeGreaterThan(0);
    }
    expect(r.role).toBe('sentinel');
    expect(r.matchesRead).toBe(8);
  });

  test('entry is only measured for duelists', () => {
    const r = analyze(generateMatches(neutralProfile('controller'), 1), PLAYER_ID);
    expect(r.findings.some((f) => f.skill === 'entry')).toBe(false);
  });

  test('evidence points at real moments in the logs', () => {
    for (const skill of SKILLS) {
      const logs = generateMatches(weakProfile('duelist', skill, 0.05), 7);
      const report = analyze(logs, PLAYER_ID);
      const f = report.findings.find((x) => x.skill === skill);
      expect(f?.evidence.length).toBeGreaterThan(0);
      expect(f?.evidence.length).toBeLessThanOrEqual(5);
      for (const ev of f?.evidence ?? []) {
        const log = logs.find((l) => l.matchId === ev.matchId);
        const round = log?.rounds.find((r) => r.index === ev.round);
        expect(round, `${ev.matchId} round ${ev.round}`).toBeTruthy();
        expect(round?.events.some((e) => e.tick === ev.tick)).toBe(true);
        expect(ev.note.length).toBeGreaterThan(10);
      }
    }
  });

  test('entry evidence names the callout and the time after first contact', () => {
    const r = analyze(generateMatches(weakProfile('duelist', 'entry', 0.05), 7), PLAYER_ID);
    const note = r.findings[0].evidence[0].note;
    expect(note).toMatch(/died first|lost the opening duel/);
    expect(note).toMatch(/(Mid|A Site|B Site|South Lane|North Lane)/);
    expect(note).toMatch(/\d+(\.\d)? s after first contact/);
  });

  test('ignores other players, and an empty history gives no findings', () => {
    const logs = generateMatches(neutralProfile('duelist'), 1);
    expect(analyze(logs, 'someone-else').findings).toEqual([]);
    expect(analyze([], PLAYER_ID).matchesRead).toBe(0);
  });
});

describe('skill definitions on hand-written rounds', () => {
  const log = (events: MatchLog['rounds'][number]['events'], o: Partial<MatchLog['rounds'][number]> = {}): MatchLog[] => [
    {
      matchId: 'm',
      mapId: 'test-box',
      playerId: PLAYER_ID,
      rounds: [{ index: 0, playerTeam: 'attack', playerRole: 'duelist', events, winner: 'defense', ...o }],
    },
  ];
  const p = { x: 30, y: 20 };
  const dmg = (tick: number, attacker: string, victim: string) =>
    ({ type: 'damage', tick, attacker, victim, amount: 150, part: 'head', attackerPos: p, victimPos: p }) as const;

  test('entry: first duel won counts as a success, someone else first counts for nothing', () => {
    const won = analyze(log([dmg(100, PLAYER_ID, 'e1'), { type: 'round_end', tick: 200, winner: 'attack', reason: 'elimination' }], { winner: 'attack' }), PLAYER_ID);
    expect(won.findings.find((f) => f.skill === 'entry')?.rate).toBe(1);
    const other = analyze(log([dmg(100, 'a1', 'e1'), dmg(120, 'e2', PLAYER_ID), { type: 'round_end', tick: 200, winner: 'defense', reason: 'elimination' }]), PLAYER_ID);
    expect(other.findings.find((f) => f.skill === 'entry')).toBeUndefined();
  });

  test('time pressure: a round lost on time without a plant is a failure', () => {
    const r = analyze(log([{ type: 'round_end', tick: 2000, winner: 'defense', reason: 'time' }]), PLAYER_ID);
    const f = r.findings.find((x) => x.skill === 'time_pressure');
    expect(f).toMatchObject({ rate: 0, sampleSize: 1 });
  });

  test('time pressure: an early plant is not pressure', () => {
    const r = analyze(
      log([{ type: 'plant', tick: 800, agent: 'a1', site: 'A' }, { type: 'round_end', tick: 1700, winner: 'attack', reason: 'detonated' }], { winner: 'attack' }),
      PLAYER_ID,
    );
    expect(r.findings.find((x) => x.skill === 'time_pressure')).toBeUndefined();
  });

  test('smoke: before contact and near is useful, after contact is wasted', () => {
    const r = analyze(
      log([
        { type: 'smoke', tick: 100, agent: PLAYER_ID, pos: { x: 31, y: 20 } },
        { type: 'spotted', tick: 300, observer: 'e1', target: 'a1' },
        dmg(310, 'e1', 'a1'),
        { type: 'smoke', tick: 400, agent: PLAYER_ID, pos: { x: 31, y: 20 } },
        { type: 'round_end', tick: 600, winner: 'defense', reason: 'time' },
      ]),
      PLAYER_ID,
    );
    const f = r.findings.find((x) => x.skill === 'smoke_usage');
    expect(f).toMatchObject({ rate: 0.5, sampleSize: 2 });
    expect(f?.evidence[0].note).toMatch(/after first contact/);
  });
});

describe('severity', () => {
  test('is zero at or above the benchmark and grows with sample size for the same rate', () => {
    expect(severityOf(10, 10, 0.5)).toBe(0);
    expect(severityOf(5, 10, 0.5)).toBe(0);
    expect(severityOf(2, 40, 0.5)).toBeGreaterThan(severityOf(0, 4, 0.5));
    expect(severityOf(10, 100, 0.5)).toBeGreaterThan(severityOf(1, 10, 0.5));
  });
  test('wilson interval brackets the rate', () => {
    const w = wilson(3, 10);
    expect(w.lower).toBeLessThan(0.3);
    expect(w.upper).toBeGreaterThan(0.3);
  });
});

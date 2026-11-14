import { describe, expect, it } from 'vitest';
import { analyze } from './analyze.ts';
import { recommendAgents, readPlaystyle } from './recommend.ts';
import { generateMatches, neutralProfile, PLAYER_ID, sampleHistory } from './synthetic.ts';

describe('agent advice', () => {
  it('reads a playstyle from the logged events', () => {
    const logs = sampleHistory('entry', 11);
    const s = readPlaystyle(logs, PLAYER_ID);
    expect(s.rounds).toBeGreaterThan(0);
    expect(s.firstDuelShare).toBeGreaterThanOrEqual(0);
    expect(s.firstDuelShare).toBeLessThanOrEqual(1);
    expect(s.kills + s.deaths).toBeGreaterThan(0);
    if (s.meanKillDistanceM !== null) expect(s.meanKillDistanceM).toBeGreaterThan(0);
  });

  it('practises each credible weakness on the kit that helps it', () => {
    const want = { entry: 'wren', clutch: 'pike', time_pressure: 'rook', smoke_usage: 'haze' } as const;
    for (const skill of Object.keys(want) as (keyof typeof want)[]) {
      const logs = sampleHistory(skill, 11);
      const report = analyze(logs, PLAYER_ID);
      const advice = recommendAgents(logs, report);
      if (report.findings[0].severity > 0 && report.findings[0].skill === skill) {
        expect(advice.practice.agent.id).toBe(want[skill]);
        expect(advice.practice.why).toMatch(/benchmark/);
      }
      expect(advice.ranked).toHaveLength(4);
      expect(advice.ranked[0].agent.id).toBe(advice.main.agent.id);
      expect(advice.main.agent.valorantLike.length).toBeGreaterThan(0);
    }
  });

  it('does not invent a weakness for a neutral player', () => {
    const logs = generateMatches(neutralProfile('duelist'), 5, { matches: 6 });
    const report = analyze(logs, PLAYER_ID);
    const advice = recommendAgents(logs, report);
    if (report.findings.every((f) => f.severity <= 0)) expect(advice.practice.agent.id).toBe(advice.main.agent.id);
  });

  it('every number in the advice comes from the playstyle or the report', () => {
    const logs = sampleHistory('clutch', 11);
    const report = analyze(logs, PLAYER_ID);
    const a = recommendAgents(logs, report);
    const allowed = new Set<string>();
    const add = (x: number) => { allowed.add(String(Math.round(x * 100))); allowed.add(x.toFixed(0)); allowed.add(x.toFixed(1)); };
    const s = a.playstyle;
    [s.firstDuelShare, s.firstDuelWinRate, s.objectiveShare].forEach(add);
    add(s.smokesPerRound); if (s.meanKillDistanceM !== null) add(s.meanKillDistanceM);
    for (const f of report.findings) { add(f.rate); add(f.benchmark); }
    for (const n of `${a.main.why} ${a.practice.why}`.match(/\d+(\.\d+)?/g) ?? []) expect(allowed.has(n)).toBe(true);
  });
});

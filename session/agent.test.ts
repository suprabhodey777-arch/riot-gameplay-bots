import { expect, test } from 'vitest';
import { withPlayerAgent, buildDrill } from '../analyzer/drills.ts';
import { testBoxMap } from '../shared/maps/index.ts';
import { startDrill } from './drill-run.ts';
import { CoachingSession } from './session.ts';

test('smoke drills give Haze to agents without smoke, preserving smoke-capable picks', () => {
  const spec = buildDrill('smoke_usage', .3, testBoxMap, 1);
  expect(spec.player.agent).toBe('haze');
  for (const agent of ['rook', 'wren', 'pike', 'haze'] as const) {
    const selected = withPlayerAgent(spec, agent);
    const expected = agent === 'rook' || agent === 'wren' ? 'haze' : agent;
    expect(selected.player.agent).toBe(expected);
    const player = startDrill(selected).state().agents[0];
    expect(player.agent).toBe(expected);
    expect(player.smokesLeft).toBeGreaterThan(0);
  }
});
test('session selections survive serialization and apply to generated and exam drills', () => {
  const initial = CoachingSession.fromHistory([], 'player', { mapId: 'test-box', rehearsalsFirst: false }).toJSON();
  const session = CoachingSession.fromJSON({ ...initial, playerAgent: 'pike' });
  expect(session.nextDrill().drill.player.agent).toBe('pike');
  expect(session.exam().every(d => d.player.agent === 'pike')).toBe(true);
  expect(CoachingSession.fromJSON(session.toJSON()).toJSON().playerAgent).toBe('pike');
});

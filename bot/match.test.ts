import { describe, expect, test } from 'vitest';
import { testBoxMap } from '../shared/maps/index.ts';
import { createMatch } from './match.ts';
import { archetypeById } from './traits.ts';

describe('createMatch options', () => {
  const agents = [
    { id: 'a', team: 'attack' as const, isBot: true, archetypeId: 'entry-duelist' },
    { id: 'd', team: 'defense' as const, isBot: true },
  ];

  test('passes the scenario to the sim', () => {
    const m = createMatch({ map: testBoxMap, seed: 1, agents, scenario: { agents: { a: { spawn: { x: 10, y: 10 } } }, roundTimeLeftS: 30 } });
    const s = m.state();
    expect(s.agents.find((x) => x.id === 'a')?.pos).toEqual({ x: 10, y: 10 });
    expect(s.roundTimeLeftS).toBe(30);
  });

  test('an archetype id sets the bot traits, and explicit traits win', () => {
    const m = createMatch({ map: testBoxMap, seed: 1, agents });
    const trace = m.step([]).trace.filter((t) => t.botId === 'a');
    expect(trace[0].traits).toEqual(archetypeById('entry-duelist').traits);
    const base = archetypeById('baseline').traits;
    const m2 = createMatch({ map: testBoxMap, seed: 1, agents: [{ ...agents[0], traits: base }, agents[1]] });
    expect(m2.step([]).trace.find((t) => t.botId === 'a')?.traits).toEqual(base);
  });

  test('lastBotInputs lists the bots inputs from the last step', () => {
    const m = createMatch({ map: testBoxMap, seed: 1, agents });
    expect(m.lastBotInputs()).toEqual([]);
    m.step([]);
    expect(m.lastBotInputs().map((i) => i.agentId).sort()).toEqual(['a', 'd']);
  });
});

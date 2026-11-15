import { expect, it } from 'vitest';
import { MatchGame } from './match-game.ts';
import { PERSONAS, PERSONA_WEAPONS, PERSONA_TRAITS } from '../shared/personas.ts';
import { personaDrill, personaOffer, planNext } from '../analyzer/plan.ts';
import { loadMap } from '../shared/maps/index.ts';
import { analyze } from '../analyzer/analyze.ts';
import { sampleHistory } from '../analyzer/synthetic.ts';
import { startDrill } from './drill-run.ts';
import { siteAt } from '../sim/geometry.ts';

it.each(['A', 'B'] as const)('plays a complete first-to-five 1v1 duel at chosen site %s and serializes duel_end', site => {
  const game = new MatchGame({ map: 'ironvale', agent: 'rook', seed: 4, duel: { persona: 'zephyr', site } });
  expect(game.wallets).toHaveLength(2);
  let end;
  for (let round = 0; round < 9 && !end; round++) {
    game.ready(); expect(game.state().agents).toHaveLength(2);
    expect(game.state().agents.some(a => siteAt(loadMap('ironvale'), a.pos) === site)).toBe(true);
    for (let tick = 0; tick < 901 && game.phase === 'live'; tick++) end = game.step(null).find(m => m.type === 'duel_end') ?? end;
  }
  expect(game.phase).toBe('ended'); expect(Math.max(game.score.player, game.score.enemy)).toBe(5);
  expect(JSON.parse(JSON.stringify(end))).toEqual({ type: 'duel_end', score: { player: expect.any(Number), enemy: expect.any(Number) }, beatTip: PERSONAS[0].beatTip, stats: { kills: expect.any(Number), deaths: expect.any(Number), headshotPct: expect.any(Number) } });
});
it('uses all five persona kits, traits and weapon preferences on the enemy team', () => {
  const game = new MatchGame({ map: 'ironvale', agent: 'rook', enemyPersonas: PERSONAS.map(p => p.id) });
  game.ready();
  const enemies = game.state().agents.filter(a => a.team === 'defense');
  for (const [i, p] of PERSONAS.entries()) { expect(enemies[i].agent).toBe(p.agent); expect(game.wallets[i + 5].archetypeId).toBe(p.id); }
});
it.each(['entry', 'clutch', 'smoke_usage', 'time_pressure'] as const)('offers a persona for weakest %s without changing ordinary planning', skill => {
  const report = analyze(sampleHistory(skill, 11), 'me');
  const offer = personaOffer(report), map = loadMap('ironvale');
  expect(offer.persona).toBe(skill === 'clutch' ? 'wisp' : skill === 'time_pressure' ? 'zephyr' : 'ember');
  const before = planNext(report, {}, [], map, 2);
  const drill = personaDrill(report, map, 2); const run = startDrill(drill);
  expect(drill.bots.every(b => b.archetypeId === offer.persona && b.traits.reactionMs === PERSONA_TRAITS[offer.persona].reactionMs)).toBe(true);
  expect(run.state().agents.filter(a => a.isBot).every(a => a.primary === PERSONA_WEAPONS[offer.persona][0])).toBe(true);
  expect(planNext(report, {}, [], map, 2)).toEqual(before);
});

it.each(PERSONAS)('$name duel buys the preferred weapon and uses its exact kit', p => {
  const game = new MatchGame({ map: 'ironvale', agent: 'rook', duel: { persona: p.id, site: 'A' } });
  game.ready(); const enemy = game.state().agents.find(a => a.isBot)!;
  expect(enemy.primary).toBe(PERSONA_WEAPONS[p.id][0]); expect(enemy.agent).toBe(p.agent);
});

import { expect, test } from 'vitest';
import { MatchGame, cappedCredits, lossAward, roundAward } from './match-game.ts';
import { createBot } from '../bot/bot.ts';
import { DEFAULT_TRAITS } from '../bot/traits.ts';
import { createRng } from '../sim/rng.ts';
import { loadMap } from '../shared/maps/index.ts';
import { analyze } from '../analyzer/analyze.ts';
import { CoachingSession } from './session.ts';
import { ecoDrill, buildDrill, drillSimOptions } from '../analyzer/drills.ts';
import { createSim } from '../sim/sim.ts';
import { roundType } from '../shared/weapons.ts';

const game = () => new MatchGame({ map: 'ironvale', agent: 'rook', seed: 11 });
test('buy rejects unaffordable items, sell refunds purchases, and live buying is rejected', () => {
  const g = game();
  expect(g.loadoutMessage()).toMatchObject({ credits: 800, primary: null, secondary: 'pin' });
  expect(() => g.buy('viper')).toThrow('afford');
  g.buy('hammer'); expect(g.loadoutMessage()).toMatchObject({ credits: 300, secondary: 'hammer' });
  expect(() => g.buy('light')).toThrow('afford');
  g.sell('hammer'); expect(g.loadoutMessage()).toMatchObject({ credits: 800, secondary: 'pin' });
  expect(() => g.sell('pin')).toThrow();
  g.buy('light'); g.ready(); expect(g.state().agents[0].armor).toBe(25);
  expect(() => g.buy('pin')).toThrow('buy phase'); expect(() => g.sell('light')).toThrow('buy phase');
});
test('economy table and cap', () => {
  expect([0, 1999, 2000, 3899, 3900].map(c => roundType(2, c))).toEqual(['eco', 'eco', 'force', 'force', 'full buy']);
  expect(roundType(1, 9000)).toBe('pistol'); expect(roundType(7, 9000)).toBe('pistol');
  expect([1, 2, 3, 4].map(lossAward)).toEqual([1900, 2400, 2900, 2900]);
  expect(cappedCredits(8900, 3000)).toBe(9000);
  expect(roundAward(true, 0, 1, false)).toBe(3200);
  expect(roundAward(true, 0, 2, true)).toBe(3700);
  expect(roundAward(false, 1, 0, true)).toBe(2200);
  expect(roundAward(false, 2, 0, false)).toBe(2400);
  expect(roundAward(false, 3, 1, false)).toBe(3100);
});
test('a whole headless match reaches match_end, records every round and awards exact credits', () => {
  const g = game(); const map = loadMap(g.mapId); let ended = false; let rounds = 0; let sawSwap = false;
  while (!ended && rounds < 13) {
    const buy = g.buyPhase();
    expect(buy.timeS).toBe(20);
    expect(buy.round).toBe(rounds + 1);
    if (buy.round === 7) { expect(buy.side).toBe('defense'); expect(buy.roundType).toBe('pistol'); expect(buy.credits).toBe(800); sawSwap = true; }
    const before = g.wallets.map(w => ({ ...w }));
    g.advanceBuy(20); expect(g.phase).toBe('live');
    const bot = createBot({ id: g.playerId, map, rng: createRng(rounds + 10), traits: DEFAULT_TRAITS, teamPlay: true });
    let ticks = 0;
    while (g.phase === 'live' && ticks++ < 3001) {
      const messages = g.step(bot.decide(g.state()).input);
      const result = messages.find(m => m.type === 'round_result');
      if (result?.type === 'round_result') {
        rounds++;
        const log = g.log().rounds.at(-1)!;
        const kills = log.events.filter(e => e.type === 'kill');
        const win = log.winner === log.playerTeam;
        const previous = g.log().rounds.slice(0, -1).reverse();
        let streak = 1; for (const r of previous) { if (r.winner === r.playerTeam || r.index === 5) break; streak++; }
        const expected = (win ? 3000 : lossAward(streak)) + kills.filter(e => e.killer === g.playerId).length * 200 + (log.playerTeam === 'attack' && log.events.some(e => e.type === 'plant') ? 300 : 0);
        expect(result.creditsEarned).toBe(expected);
        if (g.round !== 7 || messages.some(m => m.type === 'match_end')) expect(g.wallets[0].credits).toBe(cappedCredits(before[0].credits, expected));
        const step = messages.find(m => m.type === 'step');
        if (step?.type === 'step') {
          const dead = step.state.agents[0];
          if (!dead.alive) expect(g.wallets[0].primary).toBeNull();
        }
      }
      ended = messages.some(m => m.type === 'match_end');
    }
    expect(ticks).toBeLessThan(3001);
  }
  expect(ended).toBe(true); expect(sawSwap).toBe(true);
  expect(Math.max(g.score.player, g.score.enemy)).toBe(7);
  expect(g.log().rounds).toHaveLength(rounds);
  expect(analyze([g.log()], g.playerId).matchesRead).toBe(1);
}, 60000);
test('dead players lose primary, survivors keep weapons, force and full-buy bots buy their roles', () => {
  const g = game(); g.wallets.forEach(w => { w.credits = 6000; });
  g.buy('lancer'); g.ready();
  const player = createBot({ id: g.playerId, map: loadMap(g.mapId), teamPlay: true, rng: createRng(10), traits: { ...DEFAULT_TRAITS, headAccuracy: 0, bodyAccuracy: 0, aggression: 1 } });
  let final: ReturnType<MatchGame['state']> | undefined;
  while (g.phase === 'live') {
    const messages = g.step(player.decide(g.state()).input);
    const step = messages.find(m => m.type === 'step'); if (step?.type === 'step') final = step.state;
  }
  expect(final!.agents[0].alive).toBe(false);
  expect(g.wallets[0].primary).toBeNull();
  for (const w of g.wallets.slice(1)) { expect(w.primary).not.toBeNull(); expect(w.armor).toBe('heavy'); }
}, 30000);
test('eco losses offer sidearm practice for every skill, without changing no-history planning', () => {
  const map = loadMap('ironvale');
  for (const skill of ['entry', 'clutch', 'time_pressure', 'smoke_usage'] as const) {
    const drill = ecoDrill(buildDrill(skill, 0.3, map, 4));
    const state = createSim(drillSimOptions(drill)).state();
    expect(state.agents[0].primary).toBeNull(); expect(state.agents[0].secondary).toBe('hammer');
    expect(state.agents.slice(1).every(a => a.primary === 'lancer')).toBe(true);
  }
  const g = game(); g.ready(); while (g.phase === 'live') g.step(null);
  const log = g.log(); log.rounds[0].roundType = 'eco'; log.rounds[0].winner = 'defense';
  const session = CoachingSession.fromHistory([log], 'player', { rehearsalsFirst: false });
  expect(session.report.ecoLosses).toBe(1);
  expect(session.nextDrill().drill.eco).toBe(true);
});

test('surviving equipment can be upgraded, with a current-phase sale restoring the old loadout', () => {
  const g = game(), me = g.wallets[0];
  me.credits = 5000; me.primary = 'wasp'; me.armor = 'light'; me.armorPoints = 13;
  g.buy('viper'); g.buy('heavy');
  expect(g.loadoutMessage()).toMatchObject({ primary: 'viper', armor: 'heavy', credits: 1100 });
  g.sell('viper'); g.sell('heavy');
  expect(g.loadoutMessage()).toMatchObject({ primary: 'wasp', armor: 'light', credits: 5000 });
  expect(me.armorPoints).toBe(13);
});

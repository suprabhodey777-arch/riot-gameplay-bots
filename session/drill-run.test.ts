import { describe, expect, test } from 'vitest';
import type { SimEvent, SimState, Vec2 } from '../shared/contract.ts';
import type { DrillSpec, Skill } from '../shared/coaching.ts';
import { loadMap } from '../shared/maps/index.ts';
import { buildDrill } from '../analyzer/drills.ts';
import { createSim } from '../sim/sim.ts';
import { drillSimOptions } from '../analyzer/drills.ts';
import { centroid } from '../analyzer/geo.ts';
import { DrillJudge, ENTRY_SURVIVE_TICKS, idleInput, SMOKE_CHOKE_RANGE_M, startDrill } from './drill-run.ts';

const map = loadMap('test-box');
const specOf = (skill: Skill, seed = 5, d = 0.3): DrillSpec => buildDrill(skill, d, map, seed);

/** A judge plus a way to feed it a state with the player at `pos`, alive or not, at `tick`. */
function harness(spec: DrillSpec) {
  const judge = new DrillJudge(spec, map.sites);
  const base = createSim(drillSimOptions(spec)).state();
  const feed = (tick: number, events: SimEvent[], o: { pos?: Vec2; alive?: boolean } = {}) => {
    const state: SimState = JSON.parse(JSON.stringify(base));
    state.tick = tick;
    const me = state.agents.find((a) => a.id === spec.player.id);
    if (me) {
      if (o.pos) me.pos = o.pos;
      if (o.alive === false) me.alive = false;
    }
    return judge.observe(state, events);
  };
  return { judge, feed };
}
const kill = (tick: number, killer: string, victim: string): SimEvent => ({ type: 'kill', tick, killer, victim, part: 'body', killerPos: { x: 1, y: 1 }, victimPos: { x: 2, y: 2 } });
const end = (tick: number, winner: 'attack' | 'defense', reason: 'elimination' | 'time' = 'elimination'): SimEvent => ({ type: 'round_end', tick, winner, reason });

describe('success rules', () => {
  test('entry: first kill plus 3 s alive succeeds', () => {
    const spec = specOf('entry');
    const { feed } = harness(spec);
    expect(feed(100, [kill(100, spec.player.id, 'bot-1')])).toBeNull();
    expect(feed(100 + ENTRY_SURVIVE_TICKS - 1, [])).toBeNull();
    expect(feed(100 + ENTRY_SURVIVE_TICKS, [])).toBe('success');
  });

  test('entry: dying within 3 s of the first kill is not enough unless the team then wins', () => {
    const spec = specOf('entry');
    const a = harness(spec);
    a.feed(100, [kill(100, spec.player.id, 'bot-1')]);
    a.feed(120, [kill(120, 'bot-2', spec.player.id)], { alive: false });
    expect(a.feed(130, [end(130, 'defense')], { alive: false })).toBe('fail');
  });

  test('entry: a bot getting the first kill fails the round, a team win still succeeds', () => {
    const spec = specOf('entry');
    const lost = harness(spec);
    lost.feed(50, [kill(50, 'bot-1', spec.player.id)], { alive: false });
    expect(lost.feed(51, [end(51, 'defense')], { alive: false })).toBe('fail');
    const won = harness(spec);
    won.feed(50, [kill(50, 'bot-1', 'bot-2')]);
    expect(won.feed(60, [end(60, 'attack')])).toBe('success');
  });

  test('clutch: succeeds exactly when the player team wins', () => {
    const spec = specOf('clutch');
    const w = harness(spec);
    expect(w.feed(10, [end(10, spec.player.team)])).toBe('success');
    const l = harness(spec);
    expect(spec.player.team).toBe('attack');
    expect(l.feed(10, [end(10, 'defense')])).toBe('fail');
  });

  test('time_pressure: a plant (attack) or a defuse (defense) completes the drill; a round lost does not', () => {
    for (let seed = 1; seed < 20; seed++) {
      const spec = specOf('time_pressure', seed);
      const h = harness(spec);
      if (spec.player.team === 'attack') {
        expect(h.feed(200, [{ type: 'plant', tick: 200, agent: spec.player.id, site: 'A' }])).toBe('success');
        expect(harness(spec).feed(300, [end(300, 'defense', 'time')])).toBe('fail');
      } else {
        expect(h.feed(200, [{ type: 'defuse', tick: 200, agent: spec.player.id }])).toBe('success');
        expect(harness(spec).feed(300, [end(300, 'attack')])).toBe('fail');
      }
    }
  });

  test('smoke_usage: an early smoke on the choke plus reaching the site alive succeeds', () => {
    const spec = specOf('smoke_usage');
    const choke = spec.bots[0].spawn;
    const site = centroid(map.sites.A);
    const { feed } = harness(spec);
    expect(feed(5, [{ type: 'smoke', tick: 5, agent: spec.player.id, pos: { x: choke.x + 1, y: choke.y } }])).toBeNull();
    expect(feed(300, [], { pos: site })).toBe('success');
  });

  test('smoke_usage: a smoke off the choke, or after first contact, or no site, fails', () => {
    const spec = specOf('smoke_usage');
    const choke = spec.bots[0].spawn;
    const far = { x: choke.x - SMOKE_CHOKE_RANGE_M - 3, y: choke.y };
    const a = harness(spec);
    a.feed(5, [{ type: 'smoke', tick: 5, agent: spec.player.id, pos: far }]);
    expect(a.feed(300, [], { pos: centroid(map.sites.A) })).toBeNull();
    expect(a.feed(400, [end(400, 'defense')])).toBe('fail');
    const b = harness(spec);
    b.feed(40, [{ type: 'damage', tick: 40, attacker: 'bot-1', victim: spec.player.id, amount: 40, part: 'body', attackerPos: choke, victimPos: choke }]);
    expect(b.feed(50, [{ type: 'smoke', tick: 50, agent: spec.player.id, pos: choke }])).toBe('fail');
    const c = harness(spec);
    c.feed(5, [{ type: 'smoke', tick: 5, agent: spec.player.id, pos: choke }]);
    expect(c.feed(60, [kill(60, 'bot-1', spec.player.id)], { alive: false })).toBe('fail');
  });
});

describe('startDrill', () => {
  test('an idle player ends the drill on round_end or the tick budget, and the result carries the events and trace', () => {
    const spec = specOf('time_pressure', 3, 0.2);
    const run = startDrill(spec);
    let guard = 0;
    while (!run.done() && guard++ < 5000) run.step(null);
    expect(run.done()).toBe(true);
    const r = run.finish();
    expect(r.drillId).toBe(spec.id);
    expect(r.skill).toBe('time_pressure');
    expect(r.ticks).toBeLessThanOrEqual(run.maxTicks);
    expect(r.trace.length).toBeGreaterThan(0);
    expect(run.step(idleInput(spec.player.id)).done).toBe(true);
    expect(run.finish().ticks).toBe(r.ticks);
  });

  test('the scenario reaches the sim: bots start where the spec says', () => {
    const spec = specOf('clutch', 7, 0.5);
    const run = startDrill(spec);
    const s = run.state();
    for (const b of spec.bots) expect(s.agents.find((a) => a.id === b.id)?.pos).toEqual(b.spawn);
    expect(s.spike.status).toBe('planted');
  });
});

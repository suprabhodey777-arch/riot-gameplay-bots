import type { MapDef, SimEvent, SiteId, Vec2 } from '../shared/contract.ts';
import type { PersonaId } from '../shared/personas.ts';
import { siteAt, siteIds, sitePoly } from '../sim/geometry.ts';
import { TICK_RATE } from '../sim/sim.ts';
import { centroid, navFor, pathBetween, clearSegment } from './nav.ts';
import type { BotView } from './view.ts';
import type { Action } from './bot.ts';

const distance = (a: Vec2, b: Vec2) => Math.hypot(a.x - b.x, a.y - b.y);
const bearing = (a: Vec2, b: Vec2) => Math.atan2(b.y - a.y, b.x - a.x);
/** Persona state uses only the fog-filtered view, team cues and public map geometry. */
export function createPersonaMoves(id: PersonaId, map: MapDef, botId: string, site?: SiteId) {
  let contact = -1;
  let relocation: Vec2 | null = null;
  let perch: Vec2 | null = null;
  const used = new Set<string>();
  let trade: { pos: Vec2; until: number } | null = null;
  let pushStarted = false;
  let reconTick = -1000;
  let flashTick = -1000;
  let lastKill = -1000;
  const home = centroid(map.spawns.attack);
  const sites = siteIds(map);
  const goal = centroid(sitePoly(map, site ?? sites[0]));
  function toward(view: BotView, pos: Vec2): Vec2 {
    const points = pathBetween(map, view.self.pos, pos) ?? [pos];
    const next = points.find(p => distance(view.self.pos, p) > .5) ?? pos;
    const d = distance(view.self.pos, next);
    return d < .1 ? { x: 0, y: 0 } : { x: (next.x - view.self.pos.x) / d, y: (next.y - view.self.pos.y) / d };
  }
  return (view: BotView, events: SimEvent[], base: Action): Action => {
    const me = view.self;
    const visible = view.visible.filter(e => e.los);
    const foe = visible[0];
    const mates = view.allies.filter(a => a.alive);
    const foothold = mates.some(a => !!siteAt(map, a.pos) || distance(a.pos, home) > distance(me.pos, home) + 4);
    if (foothold || events.some(e => (e.type === 'flash' || e.type === 'dash' || e.type === 'plant') && view.allies.some(a => a.id === (e.type === 'plant' ? e.agent : e.type === 'flash' ? e.thrower : e.agent)))) pushStarted = true;
    for (const e of events) {
      if (e.type === 'kill' && e.killer === botId) lastKill = view.tick;
      if (e.type === 'kill' && view.allies.some(a => a.id === e.victim)) trade = { pos: e.victimPos, until: e.tick + 1.5 * TICK_RATE };
    }
    if (base.node === 'plant' || base.node === 'defuse') return base;
    const action = (node: string, reason: string, extra: Partial<Action> = {}): Action => ({ ...base, node, reason, target: node, forceTrace: true, ...extra });
    if (id === 'zephyr') {
      if (foe) {
        if (contact < 0) contact = view.tick;
        const yaw = bearing(me.pos, foe.pos);
        const age = view.tick - contact;
        if (age < 4 && view.tick - lastKill > 20) return action('jigglePeek', 'Baiting the held angle before a full-run swing', { move: { x: -Math.sin(yaw) * (age < 2 ? 1 : -1), y: Math.cos(yaw) * (age < 2 ? 1 : -1) }, walk: false, fire: null, holding: false });
        return action('wideSwing', 'Full-run swing past the held angle; re-peek after a kill', { move: { x: Math.cos(yaw) * .6 - Math.sin(yaw) * .8, y: Math.sin(yaw) * .6 + Math.cos(yaw) * .8 }, walk: false, holding: false });
      }
      contact = -1;
      return action(base.node, 'Taking first entry into the site', { dash: base.node === 'push' && me.abilities.dash > 0 });
    }
    if (id === 'atlas') {
      if (trade && view.tick <= trade.until) return action('trade', 'Following the fallen teammate within 1.5 seconds', { move: toward(view, trade.pos), walk: false, holding: false });
      const support = mates.find(a => distance(me.pos, a.pos) < 12);
      if (!support && foe) return action('holdTradeAngle', 'Holding contact without peeking alone', { move: { x: 0, y: 0 }, holding: true });
      if (!support || (!foothold && !foe)) return action('teamWait', 'Waiting for a teammate foothold; never peeking alone', { move: support ? toward(view, support.pos) : { x: 0, y: 0 }, fire: null, holding: !support });
      return action(base.node, 'Supporting the foothold behind a choke smoke', { smokeAt: me.smokesLeft > 0 && !foe ? { x: (me.pos.x + goal.x) / 2, y: (me.pos.y + goal.y) / 2 } : null });
    }
    if (id === 'ember') {
      if (relocation) {
        if (distance(me.pos, relocation) < .6) { perch = relocation; relocation = null; }
        else return action('reposition', 'Falling back to a fresh perch after the shot', { move: toward(view, relocation), fire: null, walk: false, holding: false });
      }
      if (base.fire) {
        used.add(`${Math.round(me.pos.x)},${Math.round(me.pos.y)}`);
        const candidates = navFor(map).nodes.filter(p => distance(me.pos, p) >= 3 && distance(me.pos, p) <= 9 && !used.has(`${Math.round(p.x)},${Math.round(p.y)}`) && clearSegment(me.pos, p, map.walls, .3));
        candidates.sort((a, b) => (foe ? distance(b, foe.pos) - distance(a, foe.pos) : 0) || a.x - b.x || a.y - b.y);
        relocation = candidates[0] ?? null;
        if (relocation) used.add(`${Math.round(relocation.x)},${Math.round(relocation.y)}`);
      }
      return { ...base, move: base.fire || foe || perch ? { x: 0, y: 0 } : base.move, scoped: true, holding: !!perch || base.holding };
    }
    if (id === 'wisp' && !foe) {
      const flank = navFor(map).nodes.filter(p => distance(p, goal) < 20).sort((a, b) => Math.abs(b.y - home.y) - Math.abs(a.y - home.y) || a.x - b.x)[0] ?? goal;
      if (view.tick < 6) return action('fakeFootsteps', 'Making noise in the first lane before switching to the silent flank', { move: toward(view, goal), walk: false, fire: null, holding: false });
      if (!pushStarted && view.enemiesAlive > 1) return action('lurkWait', 'Waiting silently for the team push to begin', { move: { x: 0, y: 0 }, fire: null, walk: true, holding: true });
      return action('lurkWait', 'Push underway: walking the other lane into the rear', { move: toward(view, flank), walk: true, holding: false });
    }
    if (id === 'nova' && !foe && me.team === 'attack') {
      const yaw = base.yaw ?? bearing(me.pos, goal);
      const ahead = { x: me.pos.x + Math.cos(yaw) * 12, y: me.pos.y + Math.sin(yaw) * 12 };
      if (base.node === 'push' && view.tick - reconTick > 80 && me.abilities.recon > 0) {
        reconTick = view.tick;
        return action('reconPush', 'Recon before committing to the push', { reconAt: ahead, move: { x: 0, y: 0 }, fire: null, holding: true });
      }
      if (mates.length && view.tick - reconTick >= 10 && view.tick - flashTick > 80 && me.abilities.flash > 0) {
        flashTick = view.tick;
        return action('popFlash', 'Pop flash over the approach for the teammate taking first entry', { flashAt: ahead, yaw: me.yaw + Math.PI, move: { x: 0, y: 0 }, fire: null, holding: true });
      }
      if (mates.length && !foothold) return action('secondEntry', 'Holding second until a teammate enters', { move: { x: 0, y: 0 }, fire: null, holding: true });
    }
    return base;
  };
}

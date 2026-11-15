import { isPersona, personaById, PERSONA_WEAPONS, type PersonaId } from '../shared/personas.ts';
import { pointInPolygon, sitePoly } from '../sim/geometry.ts';
import { navFor, spotsFor } from '../bot/nav.ts';
import type { SiteId } from '../shared/contract.ts';
import { AGENTS, type AgentId, type MatchLog, type PlayerInput, type SimEvent, type SimState, type Team } from '../shared/contract.ts';
import { ARMOR, WEAPONS, roundType, weaponSlot, type ArmorId, type Loadout, type RoundType, type WeaponId } from '../shared/weapons.ts';
import { loadMap } from '../shared/maps/index.ts';
import { createMatch, type Match } from '../bot/match.ts';
import { createRecorder, type Recorder } from './recorder.ts';
import type { ServerMessage } from '../bridge/protocol.ts';
import type { DrillSpec } from '../shared/coaching.ts';

type Wallet = { id: string; ally: boolean; agent: AgentId; archetypeId: string; credits: number; primary: WeaponId | null; secondary: WeaponId; armor: ArmorId | null; armorPoints: number; purchases: Map<string, number>; previousWeapons: Map<WeaponId, WeaponId | null>; armorBefore?: { armor: ArmorId | null; points: number } };
export const lossAward = (streak: number): number => [1900, 2400, 2900][Math.min(2, Math.max(0, streak - 1))];
export const roundAward = (won: boolean, streak: number, kills: number, plantedAttack: boolean): number => (won ? 3000 : lossAward(streak)) + kills * 200 + (plantedAttack ? 300 : 0);
export const cappedCredits = (credits: number, award: number): number => Math.min(9000, credits + award);

/** A complete first-to-seven match. Wall clock is owned by the transport; headless callers advance the buy clock. */
export class MatchGame {
  readonly playerId: string;
  readonly mapId: string;
  readonly wallets: Wallet[];
  readonly recorder: Recorder;
  aimScale: number | undefined;
  round = 1;
  score = { player: 0, enemy: 0 };
  phase: 'buy' | 'live' | 'ended' = 'buy';
  timeS = 20;
  match: Match | null = null;
  private seed: number;
  readonly duel?: { persona: PersonaId; site: SiteId };
  private losses = { player: 0, enemy: 0 };
  private types: { player: RoundType; enemy: RoundType } = { player: 'pistol', enemy: 'pistol' };
  private purchases: SimEvent[] = [];
  private kills = 0;
  private deaths = 0;
  private heads = 0;

  constructor(opts: { map: string; agent: AgentId; playerId?: string; seed?: number; matchId?: string; enemyPersonas?: PersonaId[]; allyPersonas?: PersonaId[]; duel?: { persona: PersonaId; site: SiteId }; /** Bot aim from the difficulty setting; see AIM_DIFFICULTY. */ aimScale?: number }) {
    const map = loadMap(opts.map);
    if (opts.duel) { personaById(opts.duel.persona); sitePoly(map, opts.duel.site); }
    for (const [ids, length] of [[opts.enemyPersonas, 5], [opts.allyPersonas, 4]] as const) if (ids && (ids.length !== length || !ids.every(isPersona))) throw new Error('Invalid persona team');
    this.duel = opts.duel;
    this.aimScale = opts.aimScale;
    this.mapId = opts.map; this.playerId = opts.playerId ?? 'player'; this.seed = opts.seed ?? 1;
    this.wallets = Array.from({ length: 10 }, (_, i) => ({ id: i === 0 ? this.playerId : i < 5 ? `ally-${i}` : `enemy-${i - 4}`, ally: i < 5,
      agent: i === 0 ? opts.agent : (['rook', 'haze', 'wren', 'pike'] as const)[i % 4],
      archetypeId: ['entry-duelist', 'utility-controller', 'baseline', 'patient-lurker', 'off-angle-anchor'][i % 5],
      credits: 800, primary: null, secondary: 'pin', armor: null, armorPoints: 0, purchases: new Map(), previousWeapons: new Map() }));
    if (opts.duel) this.wallets.splice(1, 8);
    for (const [i, w] of this.wallets.entries()) {
      const persona = opts.duel && !w.ally ? opts.duel.persona : w.ally ? opts.allyPersonas?.[i - 1] : opts.enemyPersonas?.[i - 5];
      if (persona) { w.archetypeId = persona; w.agent = personaById(persona).agent; }
    }
    this.recorder = createRecorder({ matchId: opts.matchId ?? `match-${this.seed}`, mapId: this.mapId, playerId: this.playerId, role: AGENTS[opts.agent].role });
    this.prepareBuy();
  }
  side(): Team { return this.duel || this.round <= 6 ? 'attack' : 'defense'; }
  /**
   * The `drill` message that opens a match or duel round. The 3D client sets up the player, the
   * bots and the world from it and ignores every step until one arrives; without it match and
   * duel rounds never started in the client. The spec is a thin stand-in: a round has no drill rules.
   */
  roundDrill(): Extract<ServerMessage, { type: 'drill' }> {
    const state = this.state();
    const side = this.side();
    const me = this.wallets[0];
    const spawn = state.agents.find((a) => a.id === this.playerId)?.pos ?? { x: 0, y: 0 };
    const title = this.duel ? `Duel vs ${personaById(this.duel.persona).name}, round ${this.round}` : `Round ${this.round}`;
    const goal = this.duel ? 'Win the round: first to 5 takes the duel.' : side === 'attack' ? 'Plant the spike or eliminate the defenders.' : 'Stop the plant, defuse, or eliminate the attackers.';
    const spec = {
      id: `${this.duel ? 'duel' : 'match'}-${this.seed}-r${this.round}`,
      mapId: this.mapId, skill: 'entry', difficulty: 0.5, seed: this.seed, title, goal,
      roundTimeLeftS: state.roundTimeLeftS,
      player: { id: this.playerId, team: side, agent: me.agent, spawn, hp: 100, carryingSpike: false },
      bots: [], loadout: this.loadout(me),
    } as unknown as DrillSpec;
    const bots = this.wallets.slice(1).map((w) => ({ id: w.id, team: this.team(w), archetype: w.archetypeId, agent: w.agent }));
    return { type: 'drill', spec, intro: goal, playerId: this.playerId, playerTeam: side, playerAgent: me.agent, bots, state };
  }
  private team(w: Wallet): Team { return w.ally ? this.side() : this.side() === 'attack' ? 'defense' : 'attack'; }
  private loadout(w: Wallet): Loadout { return { primary: w.primary, secondary: w.secondary, armor: w.armor, credits: w.credits }; }
  loadoutMessage(): Extract<ServerMessage, { type: 'loadout' }> {
    const w = this.wallets[0]; return { type: 'loadout', credits: w.credits, primary: w.primary, secondary: w.secondary, armor: w.armor };
  }
  buyPhase(): Extract<ServerMessage, { type: 'buy_phase' }> {
    return { type: 'buy_phase', round: this.round, roundType: this.types.player, credits: this.wallets[0].credits, timeS: this.timeS, score: { ...this.score }, side: this.side(), team: this.wallets.filter(w => w.ally && w.id !== this.playerId).map(w => ({ id: w.id, credits: w.credits, loadout: this.loadout(w) })) };
  }
  private prepareBuy(): void {
    this.phase = 'buy'; this.timeS = 20; this.match = null; this.purchases = [];
    if (this.duel) {
      // Duels reset full equipment each round; the 5v5 economy remains independent.
      for (const w of this.wallets) { w.credits = 9000; w.primary = null; w.secondary = 'hammer'; w.armor = null; w.armorPoints = 0; }
      const player = this.wallets[0]; player.primary = 'lancer'; player.armor = 'heavy'; player.armorPoints = ARMOR.heavy.points;
    }
    if (!this.duel && this.round === 7) {
      this.losses = { player: 0, enemy: 0 };
      for (const w of this.wallets) { w.credits = 800; w.primary = null; w.secondary = 'pin'; w.armor = null; w.armorPoints = 0; }
    }
    for (const w of this.wallets) { w.purchases.clear(); w.previousWeapons.clear(); w.armorBefore = undefined; }
    for (const ally of [true, false]) {
      const group = this.wallets.filter(w => w.ally === ally);
      const type = this.duel ? 'full buy' : roundType(this.round, group.reduce((n, w) => n + w.credits, 0) / group.length);
      this.types[ally ? 'player' : 'enemy'] = type;
      for (const w of group.filter(w => w.id !== this.playerId)) this.botBuy(w, type);
    }
  }
  private purchase(w: Wallet, item: string): void {
    if (this.phase !== 'buy') throw new Error('Buying is only allowed during the buy phase');
    if (Object.hasOwn(ARMOR, item)) {
      const id = item as ArmorId, armor = ARMOR[id];
      if (w.credits < armor.price) throw new Error('Cannot afford item');
      if (w.armorPoints >= armor.points || w.purchases.has('light') || w.purchases.has('heavy')) throw new Error('Armour already owned');
      w.armorBefore = { armor: w.armor, points: w.armorPoints };
      w.credits -= armor.price; w.armor = id; w.armorPoints = armor.points; w.purchases.set(id, armor.price);
      this.purchases.push({ type: 'buy', tick: 0, agent: w.id, item: id, price: armor.price });
    } else {
      if (!Object.hasOwn(WEAPONS, item)) throw new Error('Unknown item');
      const id = item as WeaponId, weapon = WEAPONS[id], slot = weaponSlot(id);
      if (w.credits < weapon.price) throw new Error('Cannot afford item');
      if (w[slot] === id || w.purchases.has(w[slot] ?? '')) throw new Error('Sell this buy phase’s weapon before replacing it');
      w.previousWeapons.set(id, w[slot]);
      w.credits -= weapon.price; w[slot] = id; w.purchases.set(id, weapon.price);
      this.purchases.push({ type: 'buy', tick: 0, agent: w.id, item: id, price: weapon.price });
    }
  }
  buy(item: string): void { this.purchase(this.wallets[0], item); }
  sell(item: string): void {
    if (this.phase !== 'buy') throw new Error('Selling is only allowed during the buy phase');
    const w = this.wallets[0], paid = w.purchases.get(item);
    if (paid === undefined) throw new Error('Only purchases from this buy phase can be sold');
    if (Object.hasOwn(ARMOR, item)) { w.armor = w.armorBefore?.armor ?? null; w.armorPoints = w.armorBefore?.points ?? 0; w.armorBefore = undefined; }
    else if (weaponSlot(item as WeaponId) === 'primary') w.primary = w.previousWeapons.get(item as WeaponId) ?? null;
    else w.secondary = w.previousWeapons.get(item as WeaponId) ?? 'pin';
    w.previousWeapons.delete(item as WeaponId);
    w.credits = cappedCredits(w.credits, paid); w.purchases.delete(item);
    this.purchases = this.purchases.filter(e => !(e.type === 'buy' && e.agent === w.id && e.item === item));
  }
  private botBuy(w: Wallet, type: RoundType): void {
    const tryBuy = (item: string) => { try { this.purchase(w, item); } catch { /* Save when retained equipment or credits preclude this purchase. */ } };
    if (isPersona(w.archetypeId)) {
      const preferred = PERSONA_WEAPONS[w.archetypeId];
      const weapon = preferred.find(id => WEAPONS[id].price <= w.credits);
      if (weapon && (type !== 'pistol' && type !== 'eco' || WEAPONS[weapon].price <= 800 || this.duel)) tryBuy(weapon);
      else if (preferred.includes('hammer')) tryBuy('hammer');
      if (type === 'full buy' || this.duel) tryBuy('heavy');
      return;
    }
    if (type === 'pistol') { tryBuy('hammer'); return; }
    if (type === 'eco') { if (w.credits >= 1500) tryBuy('hammer'); return; }
    if (type === 'force') { if (!w.primary) tryBuy(w.credits >= 2200 ? 'brick' : 'wasp'); tryBuy('light'); return; }
    if (!w.primary || !['lancer', 'viper', 'longshot'].includes(w.primary)) tryBuy(w.archetypeId === 'off-angle-anchor' && w.credits >= 5700 ? 'longshot' : 'lancer');
    tryBuy('heavy');
  }
  advanceBuy(seconds: number): boolean {
    if (this.phase !== 'buy') return false;
    this.timeS = Math.max(0, this.timeS - Math.max(0, seconds));
    if (!this.timeS) { this.ready(); return true; } return false;
  }
  ready(): void {
    if (this.phase !== 'buy') throw new Error('No buy phase');
    const map = loadMap(this.mapId);
    const duelSpots = this.duel ? spotsFor(map, this.duel.site, 'attack') : [];
    const inside = this.duel ? navFor(map).nodes.filter(p => pointInPolygon(p, sitePoly(map, this.duel!.site))) : [];
    const enemySpawn = duelSpots[0]?.pos;
    const distanceFromEnemy = (p: { x: number; y: number }) => Math.hypot(p.x - enemySpawn!.x, p.y - enemySpawn!.y);
    const playerSpawn = inside.sort((a, b) => distanceFromEnemy(b) - distanceFromEnemy(a))[0];
    this.match = createMatch({ map, site: this.duel?.site, scenario: this.duel ? { roundTimeLeftS: 45, agents: Object.fromEntries(this.wallets.map(w => {
      const spawn = w.ally ? playerSpawn ?? enemySpawn! : enemySpawn!;
      const opponent = w.ally ? enemySpawn! : playerSpawn ?? enemySpawn!;
      return [w.id, { spawn, yaw: Math.atan2(opponent.y - spawn.y, opponent.x - spawn.x), carryingSpike: false }];
    })) } : undefined, seed: this.seed + this.round, teamPlay: true, aimScale: this.aimScale,
      agents: this.wallets.map(w => ({ id: w.id, team: this.team(w), isBot: w.id !== this.playerId, agent: w.agent, archetypeId: w.archetypeId, loadout: { ...this.loadout(w), armorPoints: w.armorPoints } })) });
    this.phase = 'live';
    this.recorder.beginRound(this.match.state(), undefined, { roundType: this.types.player, loadout: this.loadout(this.wallets[0]) });
    this.recorder.push(this.purchases);
  }
  state(): SimState { if (!this.match) throw new Error('Round has not started'); return this.match.state(); }
  step(input: PlayerInput | null): ServerMessage[] {
    if (this.phase !== 'live' || !this.match) throw new Error('No live round');
    const r = this.match.step(input ? [{ ...input, agentId: this.playerId }] : []);
    this.recorder.push(r.events);
    const messages: ServerMessage[] = [{ type: 'step', ...r, thoughts: Object.fromEntries(r.trace.map(t => [t.botId, t.node])), done: r.state.phase === 'ended' }];
    if (r.state.phase !== 'ended') return messages;
    const end = r.events.find(e => e.type === 'round_end')! as Extract<SimEvent, { type: 'round_end' }>;
    const won = end.winner === this.side();
    const key = won ? 'player' : 'enemy', loser = won ? 'enemy' : 'player';
    this.score[key]++; this.losses[key] = 0; this.losses[loser]++;
    const all = this.recorder.toLog().rounds.at(-1)!.events;
    const kills = all.filter((e): e is Extract<SimEvent, { type: 'kill' }> => e.type === 'kill');
    this.kills += kills.filter(e => e.killer === this.playerId).length;
    this.heads += kills.filter(e => e.killer === this.playerId && e.part === 'head').length;
    this.deaths += kills.filter(e => e.victim === this.playerId).length;
    const planted = all.some(e => e.type === 'plant');
    let creditsEarned = 0;
    for (const w of this.wallets) {
      const a = r.state.agents.find(a => a.id === w.id)!;
      const winner = a.team === end.winner;
      const earned = roundAward(winner, this.losses[w.ally ? 'player' : 'enemy'], kills.filter(e => e.killer === w.id).length, planted && a.team === 'attack');
      w.credits = cappedCredits(w.credits, earned);
      if (w.id === this.playerId) creditsEarned = earned;
      w.primary = a.alive ? a.primary : null; w.secondary = a.alive ? a.secondary : 'pin';
      w.armorPoints = a.alive ? a.armor : 0; if (!w.armorPoints) w.armor = null;
    }
    const counts = new Map(this.wallets.map(w => [w.id, kills.filter(e => e.killer === w.id).length]));
    const mvp = [...this.wallets].sort((a, b) => Number(this.team(b) === end.winner) - Number(this.team(a) === end.winner) || counts.get(b.id)! - counts.get(a.id)!)[0].id;
    messages.push({ type: 'round_result', winner: end.winner, how: end.reason, creditsEarned, score: { ...this.score }, mvp });
    const target = this.duel ? 5 : 7;
    if (this.score.player === target || this.score.enemy === target) {
      this.phase = 'ended';
      const stats = { kills: this.kills, deaths: this.deaths, headshotPct: this.kills ? this.heads / this.kills : 0 };
      messages.push(this.duel ? { type: 'duel_end', score: { ...this.score }, beatTip: personaById(this.duel.persona).beatTip, stats } : { type: 'match_end', score: { ...this.score }, won: this.score.player === target, stats });
    } else { this.round++; this.prepareBuy(); messages.push(this.buyPhase()); }
    return messages;
  }
  log(): MatchLog { return this.recorder.toLog(); }
}

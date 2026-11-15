import { StreamerOverlay } from './streamer.ts';
import { PERSONAS, isPersona, personaById, type PersonaId } from '../shared/personas.ts';
import { personaOffer } from '../analyzer/plan.ts';
import { siteIds } from '../sim/geometry.ts';
import { setTimeout, clearTimeout } from 'node:timers';
import { WEAPONS } from '../shared/weapons.ts';
import { MatchGame } from '../session/match-game.ts';
import { AGENTS, agentForArchetype, type AgentId } from '../shared/contract.ts';
import process from 'node:process';
import { createServer, type Server, type Socket } from 'node:net';
import { pathToFileURL } from 'node:url';
import type { MatchLog, SimState } from '../shared/contract.ts';
import type { DrillSpec, Skill } from '../shared/coaching.ts';
import { DEFAULT_MAP_ID, PLAYABLE_MAPS, loadMap } from '../shared/maps/index.ts';
import { CoachingSession, SKILLS, type SessionOptions } from '../session/session.ts';
import { startDrill, type DrillRun } from '../session/drill-run.ts';
import { createRecorder } from '../session/recorder.ts';
import { sampleHistory } from '../analyzer/synthetic.ts';
import { analyze } from '../analyzer/analyze.ts';
import { recommendAgents } from '../analyzer/recommend.ts';
import { navFor } from '../bot/nav.ts';
import { buildDrill, withPlayerAgent } from '../analyzer/drills.ts';
import { archetypeById } from '../bot/traits.ts';
import { debrief } from '../coach/debrief.ts';
import { DEFUSE_TIME_S, PLANT_TIME_S, RUN_SPEED, TICK_RATE, FLASH_MAX_RANGE_M, FLASH_FUSE_S, FLASH_BLIND_S, DASH_DISTANCE_M, RECON_MAX_RANGE_M, RECON_RADIUS_M, RECON_REVEAL_S, RUN_HEARING_M, WALK_SPEED, CROUCH_SPEED } from '../sim/sim.ts';
import { PROTOCOL, type BridgeInput, type ClientMessage, type Rules, type ServerMessage, type Protocol } from './protocol.ts';

export const RULES: Rules = {
  weapons: WEAPONS,
  flashMaxRangeM: FLASH_MAX_RANGE_M, flashFuseS: FLASH_FUSE_S, flashBlindS: FLASH_BLIND_S,
  dashDistanceM: DASH_DISTANCE_M, reconMaxRangeM: RECON_MAX_RANGE_M, reconRadiusM: RECON_RADIUS_M, reconRevealS: RECON_REVEAL_S,
  runHearingM: RUN_HEARING_M, walkSpeedMps: WALK_SPEED, crouchSpeedMps: CROUCH_SPEED, agents: AGENTS,
  tickRate: TICK_RATE, fireIntervalS: 0.15, smokeMaxRangeM: 25,
  plantTimeS: PLANT_TIME_S, defuseTimeS: DEFUSE_TIME_S, moveSpeedMps: RUN_SPEED,
};

const NEW_EVENTS = new Set(['flash', 'blinded', 'dash', 'recon', 'revealed', 'footstep', 'reload', 'buy', 'switch']);
const WEAPON_FIELDS = new Set(['weapons', 'credits', 'armor', 'primary', 'secondary', 'slot', 'ammo', 'reserve', 'reloadingUntilTick', 'scoped']);
const NEW_FIELDS = new Set(['agent', 'playerAgent', 'abilities', 'blindUntilTick', 'revealedUntilTick',
  'flashMaxRangeM', 'flashFuseS', 'flashBlindS', 'dashDistanceM', 'reconMaxRangeM', 'reconRadiusM', 'reconRevealS',
  'runHearingM', 'walkSpeedMps', 'crouchSpeedMps', 'agents']);
function weaponLegacy(value: unknown): unknown {
  if (Array.isArray(value)) return value.filter(v => !(v && ['reload', 'buy', 'switch'].includes(v.type))).map(weaponLegacy);
  if (!value || typeof value !== 'object') return value;
  return Object.fromEntries(Object.entries(value).filter(([k]) => !WEAPON_FIELDS.has(k)).map(([k, v]) => [k, weaponLegacy(v)]));
}
function legacyMessage(value: unknown): unknown {
  if (Array.isArray(value)) return value.filter(v => !(v && NEW_EVENTS.has(v.type))).map(legacyMessage);
  if (!value || typeof value !== 'object') return value;
  return Object.fromEntries(Object.entries(value).filter(([k, v]) => !NEW_FIELDS.has(k) || (k === 'agent' && 'type' in value) || (k === 'agents' && Array.isArray(v))).map(([k, v]) => [k, legacyMessage(v)]));
}

const object = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const vec = (v: unknown): boolean => object(v) && Number.isFinite(v.x) && Number.isFinite(v.y);
function validateInput(v: unknown): asserts v is BridgeInput {
  if (!object(v) || !vec(v.move) || !Number.isFinite(v.yaw)
    || !['walk', 'crouch', 'plantHeld', 'defuseHeld'].every((k) => typeof v[k] === 'boolean')
    || !['flashAt', 'reconAt'].every(k => v[k] === undefined || v[k] === null || vec(v[k]))
    || !['dash', 'reload', 'scoped'].every(k => v[k] === undefined || typeof v[k] === 'boolean')
    || !(v.slot === undefined || v.slot === 'primary' || v.slot === 'secondary')
    || !(v.smokeAt === null || vec(v.smokeAt))
    || !(v.fire === null || (object(v.fire) && typeof v.fire.targetId === 'string' && ['head', 'body', 'miss'].includes(String(v.fire.result))))) {
    throw new Error('Invalid player input');
  }
}
function skill(v: unknown): asserts v is Skill {
  if (!SKILLS.includes(v as Skill)) throw new Error('Unknown skill');
}

/**
 * The wire sends each agent's ammo and reserve as the count for the weapon in hand. The sim keeps a
 * count per weapon, but the 3D client built before 2026-11-14 reads one number and failed to parse
 * every step otherwise (practice drills froze). Newer clients accept either shape.
 */
export function ammoInHand<T>(msg: T): T {
  const m = msg as unknown as { state?: { agents?: Record<string, unknown>[] } };
  if (!m.state?.agents) return msg;
  const agents = m.state.agents.map((a) => {
    const ammo = a.ammo as Record<string, number> | number | undefined;
    const reserve = a.reserve as Record<string, number> | number | undefined;
    if (typeof ammo !== 'object' && typeof reserve !== 'object') return a;
    const weapon = (a.slot === 'secondary' ? a.secondary : (a.primary ?? a.secondary)) as string;
    const pick = (v: Record<string, number> | number | undefined): number | undefined => (typeof v === 'object' && v !== null ? (v[weapon] ?? 0) : v);
    return { ...a, ammo: pick(ammo), reserve: pick(reserve) };
  });
  return { ...(msg as object), state: { ...m.state, agents } } as T;
}

/** Create without listening, so callers can bind port 0 and close it in tests. */
export function createBridgeServer(options: SessionOptions = {}): Server {
  return createServer((socket) => serveBridgeConnection(socket, options));
}

/** Handle one ordered NDJSON stream; exported for transport-independent verification. */
export function serveBridgeConnection(socket: Socket, options: SessionOptions = {}): void {
  let protocol: Protocol = PROTOCOL;
  let overlay = new StreamerOverlay(1);
  const personaTeam = (): Record<string, PersonaId> => Object.fromEntries(fullMatch ? fullMatch.wallets.filter(w => isPersona(w.archetypeId)).map(w => [w.id, w.archetypeId as PersonaId]) : active?.run.spec.bots.filter(b => isPersona(b.archetypeId)).map(b => [b.id, b.archetypeId as PersonaId]) ?? []);
  let pickedMap: string | undefined;
  let pickedAgent: AgentId | undefined;
  let logs: MatchLog[] = [];
  let played: MatchLog[] = [];
  let session = CoachingSession.fromHistory([], 'player', options);
  let active: { run: DrillRun; initial: SimState; versus: boolean } | null = null;
  /** The last drill started, so `retry` can play the same situation again (same seed, same bots). */
  let lastDrill: { drill: DrillSpec; versus: boolean } | null = null;
  /** The map the client last received, so a drill on another map (a rehearsal) brings its own. */
  // A client that skipped hello still has the default map in hand (the one hello would send).
  let clientMap: string = session.toJSON().mapId;
  let sequence = 0;
  let fullMatch: MatchGame | null = null;
  let buyTimer: ReturnType<typeof setTimeout> | undefined;
  const clearBuyTimer = () => { if (buyTimer) clearTimeout(buyTimer); buyTimer = undefined; };
  socket.on('close', clearBuyTimer);
  const startMatchRound = () => {
    if (!fullMatch || fullMatch.phase !== 'buy') return;
    clearBuyTimer(); fullMatch.ready();
    const state = fullMatch.state();
    send({ type: 'step', state, events: [], thoughts: {}, done: false });
  };
  const scheduleBuy = () => { clearBuyTimer(); buyTimer = setTimeout(startMatchRound, 20000); buyTimer.unref(); };
  const send = (msg: ServerMessage): void => { if (protocol !== PROTOCOL && ['chat', 'alert', 'persona_line', 'personas', 'duel_end'].includes(msg.type)) return; if (!socket.destroyed && socket.writable) socket.write(JSON.stringify(protocol === 'bots-wire.1' ? legacyMessage(weaponLegacy(msg)) : protocol === 'bots-wire.2' ? weaponLegacy(msg) : ammoInHand(msg)) + '\n'); };
  const report = (): void => {
    const staircases = Object.fromEntries(SKILLS.map((s) => [s, { difficulty: session.staircase(s).difficulty }])) as Record<Skill, { difficulty: number }>;
    send({ type: 'report', report: session.report, rehearsals: session.rehearsals().map((d, index) => ({ index, title: d.title, brief: d.goal, skill: d.skill })), staircases, played: played.length, agentAdvice: recommendAgents([...logs, ...played], session.report), ...(protocol === PROTOCOL ? { personaOffer: personaOffer(session.report) } : {}) });
  };
  const finish = async (abandon = false): Promise<void> => {
    if (!active) throw new Error('No live drill');
    const { run, initial, versus } = active;
    active = null;
    const spec = run.spec;
    const result = { ...run.finish(), ...(abandon ? { success: false } : {}) };
    const rec = createRecorder({ matchId: `bridge-${sequence++}`, mapId: spec.mapId, playerId: session.playerId, role: session.report.role });
    rec.beginRound(initial);
    rec.push(result.events);
    rec.endRound(run.state().winner ?? (result.success ? spec.player.team : spec.player.team === 'attack' ? 'defense' : 'attack'));
    played.push(rec.toLog());
    const before = session.staircase(spec.skill).difficulty;
    // complete updates the staircase synchronously before awaiting the coach.
    const pending = versus
      ? debrief({ report: session.report, result, drill: spec, playerId: session.playerId }, options)
      : session.complete(result);
    send({ type: 'result', result, success: result.success, headline: abandon ? 'Drill abandoned' : result.success ? 'Drill complete' : 'Not this time', staircaseBefore: before, staircaseAfter: session.staircase(spec.skill).difficulty });
    const written = await pending;
    send({ type: 'debrief', text: written.text, source: written.source });
    const fresh = analyze([...logs, ...played], session.playerId);
    session = CoachingSession.fromJSON({ ...session.toJSON(), historyMaps: Object.fromEntries([...logs, ...played].map(l => [l.matchId, l.mapId])), report: fresh.findings.length ? fresh : { ...session.report, matchesRead: fresh.matchesRead } }, options);
    report();
  };
  const begin = (drill: DrillSpec, versus = false): void => {
    lastDrill = { drill, versus };
    const selected = withPlayerAgent(drill, pickedAgent);
    const spec = { ...selected, player: { ...selected.player, id: session.playerId } };
    if (!versus) {
      const state = session.toJSON();
      session = CoachingSession.fromJSON({ ...state, current: spec }, options);
    }
    if (spec.mapId !== clientMap) {
      clientMap = spec.mapId;
      send({ type: 'map', map: loadMap(spec.mapId) });
    }
    const run = startDrill(spec);
    active = { run, initial: run.state(), versus };
    send({ type: 'drill', spec, intro: spec.goal, playerId: spec.player.id, playerTeam: spec.player.team,
      playerAgent: spec.player.agent, bots: spec.bots.map((b) => ({ id: b.id, team: b.team, archetype: b.archetypeId, agent: agentForArchetype(b.archetypeId) })), state: run.state() });
  };
  const handle = async (raw: unknown): Promise<void> => {
    if (!object(raw) || typeof raw.type !== 'string') throw new Error('Expected an object with a type');
    const msg = raw as unknown as ClientMessage;
    if (fullMatch && ['match', 'duel', 'load_history', 'next_drill', 'retry', 'rehearse', 'versus', 'pick_map', 'pick_agent'].includes(msg.type)) throw new Error('Abandon the match before changing mode');
    if (protocol === 'bots-wire.1' && ['maps', 'pick_map', 'pick_agent'].includes(msg.type)) throw new Error('This command requires bots-wire.2');
    if (protocol !== PROTOCOL && (['personas', 'duel', 'streamer'].includes(msg.type) || (msg.type === 'match' && (msg.enemyPersonas !== undefined || msg.allyPersonas !== undefined)))) throw new Error('This command requires bots-wire.4');
    switch (msg.type) {
      case 'hello':
        if (fullMatch && msg.protocol !== protocol) throw new Error('Match mode requires bots-wire.3');
        if (msg.protocol !== PROTOCOL && msg.protocol !== 'bots-wire.1' && msg.protocol !== 'bots-wire.2' && msg.protocol !== 'bots-wire.3') throw new Error(`Expected protocol ${PROTOCOL}`);
        protocol = msg.protocol;
        if (protocol !== PROTOCOL) overlay.toggle(false);
        clientMap = session.toJSON().mapId;
        send({ type: 'hello', protocol, map: loadMap(clientMap), rules: RULES });
        break;
      case 'maps':
        send({ type: 'maps', maps: PLAYABLE_MAPS.map(id => ({ id, name: loadMap(id).name })) });
        break;
      case 'pick_map': {
        const map = loadMap(msg.map);
        pickedMap = map.id;
        session = CoachingSession.fromJSON({ ...session.toJSON(), mapId: map.id }, options);
        clientMap = map.id;
        send({ type: 'map', map });
        break;
      }
      case 'pick_agent':
        if (!Object.hasOwn(AGENTS, msg.agent)) throw new Error('Unknown agent');
        pickedAgent = msg.agent;
        session = CoachingSession.fromJSON({ ...session.toJSON(), playerAgent: pickedAgent }, options);
        send({ type: 'agent', agent: pickedAgent, kit: { ...AGENTS[pickedAgent].kit } });
        break;
      case 'load_history': {
        let next: MatchLog[];
        let seed = 1;
        if (msg.sample) {
          skill(msg.sample.skill);
          if (!Number.isSafeInteger(msg.sample.seed)) throw new Error('Invalid sample seed');
          seed = msg.sample.seed;
          next = sampleHistory(msg.sample.skill, seed);
        } else {
          if (!Array.isArray(msg.logs)) throw new Error('Expected sample or logs');
          next = msg.logs;
          if (!next.every((l) => object(l) && typeof l.playerId === 'string' && typeof l.mapId === 'string' && Array.isArray(l.rounds))) throw new Error('Invalid match logs');
        }
        const replacement = CoachingSession.fromHistory(next, next[0]?.playerId ?? 'player', { ...options, seed, mapId: pickedMap ?? next[0]?.mapId ?? DEFAULT_MAP_ID });
        loadMap(replacement.toJSON().mapId);
        if (active) await finish(true);
        const streaming = overlay.on;
        overlay = new StreamerOverlay(seed);
        overlay.toggle(streaming);
        logs = next;
        played = [];
        session = CoachingSession.fromJSON({ ...replacement.toJSON(), playerAgent: pickedAgent }, options);
        report();
        break;
      }
      case 'retry': {
        if (!lastDrill) throw new Error('No drill to retry');
        const again = lastDrill;
        if (active) await finish(true);
        begin(again.drill, again.versus);
        break;
      }
      case 'next_drill':
        if (active) await finish(true);
        begin(session.nextDrill().drill);
        break;
      case 'rehearse': {
        // Resolve the index against the report the client is currently showing.
        const spec = session.rehearsals()[msg.index];
        if (!Number.isInteger(msg.index) || !spec) throw new Error('Invalid rehearsal index');
        if (active) await finish(true);
        const state = session.toJSON();
        session = CoachingSession.fromJSON({ ...state, history: [...state.history, { skill: spec.skill, seed: spec.seed }], rehearsed: [...new Set([...(state.rehearsed ?? []), spec.id])] }, options);
        begin(spec);
        break;
      }
      case 'versus': {
        skill(msg.skill);
        if (isPersona(msg.archetype) && protocol !== PROTOCOL) throw new Error('Personas require bots-wire.4');
        const archetype = archetypeById(msg.archetype);
        if (active) await finish(true);
        const spec = buildDrill(msg.skill, 0.3, loadMap(session.toJSON().mapId), 1 + sequence++ % 8999);
        begin({ ...spec, goal: isPersona(archetype.id) ? `${spec.goal} ${personaById(archetype.id).beatTip}` : spec.goal, id: `versus-${spec.id}`, bots: spec.bots.map((b) => ({ ...b, archetypeId: archetype.id, traits: { ...archetype.traits } })) }, true);
        break;
      }
      case 'personas':
        send({ type: 'personas', personas: PERSONAS }); break;
      case 'streamer':
        if (typeof msg.on !== 'boolean') throw new Error('Invalid streamer toggle');
        overlay.toggle(msg.on); break;
      case 'duel': {
        const map = loadMap(msg.map);
        personaById(msg.persona);
        if (!siteIds(map).includes(msg.site)) throw new Error('Unknown site');
        if (active) await finish(true);
        clearBuyTimer();
        fullMatch = new MatchGame({ map: msg.map, agent: pickedAgent ?? 'rook', playerId: session.playerId, seed: ++sequence, duel: { persona: msg.persona, site: msg.site } });
        send({ type: 'map', map }); startMatchRound(); break;
      }
      case 'match': {
        if (protocol !== PROTOCOL && protocol !== 'bots-wire.3') throw new Error('Match mode requires bots-wire.3');
        loadMap(msg.map);
        if (!Object.hasOwn(AGENTS, msg.agent)) throw new Error('Unknown agent');
        for (const [ids, length] of [[msg.enemyPersonas, 5], [msg.allyPersonas, 4]] as const) if (ids !== undefined && (!Array.isArray(ids) || ids.length !== length || !ids.every(isPersona))) throw new Error('Invalid persona team');
        if (active) await finish(true);
        clearBuyTimer();
        pickedMap = msg.map; pickedAgent = msg.agent;
        session = CoachingSession.fromJSON({ ...session.toJSON(), mapId: msg.map, playerAgent: msg.agent }, options);
        fullMatch = new MatchGame({ map: msg.map, agent: msg.agent, playerId: session.playerId, seed: ++sequence, matchId: `bridge-match-${sequence}`, enemyPersonas: msg.enemyPersonas, allyPersonas: msg.allyPersonas });
        send({ type: 'map', map: loadMap(msg.map) }); send(fullMatch.buyPhase()); scheduleBuy(); break;
      }
      case 'buy': case 'sell':
        if (!fullMatch) throw new Error('No match');
        fullMatch[msg.type](msg.item); send(fullMatch.loadoutMessage()); break;
      case 'ready':
        if (!fullMatch || fullMatch.phase !== 'buy') throw new Error('No buy phase');
        startMatchRound(); break;
      case 'input': {
        if (fullMatch) {
          validateInput(msg.input);
          const before = fullMatch.state();
          const personas = personaTeam();
          const messages = fullMatch.step({ ...msg.input, agentId: session.playerId });
          for (const message of messages) {
            if (message.type === 'buy_phase' && fullMatch.duel) continue;
            send(message);
            if (message.type === 'step' && protocol === PROTOCOL) for (const event of overlay.observe(before, message.state, message.events, session.playerId, personas, message.thoughts, { ...msg.input, agentId: session.playerId })) send(event);
          }
          if (fullMatch.duel && fullMatch.phase === 'buy') startMatchRound();
          if (fullMatch.phase === 'buy') scheduleBuy();
          if (fullMatch.phase === 'ended') {
            played.push(fullMatch.log()); fullMatch = null;
            const fresh = CoachingSession.fromHistory([...logs, ...played], session.playerId, { ...options, mapId: pickedMap ?? session.toJSON().mapId }).toJSON();
            session = CoachingSession.fromJSON({ ...session.toJSON(), report: fresh.report, historyMaps: fresh.historyMaps }, options);
            report();
          }
          break;
        }
        if (!active) throw new Error('No live drill');
        validateInput(msg.input);
        const before = active.run.state();
        const step = active.run.step({ ...msg.input, agentId: active.run.spec.player.id });
        const thoughts: Record<string, string> = {};
        for (const t of step.trace) thoughts[t.botId] = t.node;
        send({ type: 'step', state: step.state, events: step.events, thoughts, done: step.done });
        if (protocol === PROTOCOL) for (const event of overlay.observe(before, step.state, step.events, session.playerId, personaTeam(), thoughts, { ...msg.input, agentId: session.playerId })) send(event);
        if (step.done) await finish();
        break;
      }
      case 'abandon':
        if (fullMatch) { clearBuyTimer(); const log = fullMatch.log(); if (log.rounds.length) played.push(log); fullMatch = null;
          const fresh = CoachingSession.fromHistory([...logs, ...played], session.playerId, options).toJSON();
          session = CoachingSession.fromJSON({ ...session.toJSON(), report: fresh.report, historyMaps: fresh.historyMaps }, options); report(); }
        else await finish(true);
        break;
      default: throw new Error(`Unknown message type: ${raw.type}`);
    }
  };
  // Serialize each connection's messages, including asynchronous debriefs.
  let queue = Promise.resolve();
  let buffer = '';
  socket.setEncoding('utf8');
  socket.on('error', () => { /* The disconnected player's state is connection-local. */ });
  socket.on('data', (chunk: string) => {
    buffer += chunk;
    let end: number;
    while ((end = buffer.indexOf('\n')) !== -1) {
      const line = buffer.slice(0, end);
      buffer = buffer.slice(end + 1);
      queue = queue.then(async () => {
        if (socket.destroyed) return;
        await handle(JSON.parse(line) as unknown);
      }).catch((err: unknown) => send({ type: 'error', message: err instanceof Error ? err.message : String(err) }));
    }
  });
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const port = Number(process.env.BRIDGE_PORT ?? 7777);
  const server = createBridgeServer();
  server.on('error', (err) => { console.error(err.message); process.exitCode = 1; });
  // Build each map's waypoint graph now, so the first drill does not stall on it (~170 ms).
  for (const id of PLAYABLE_MAPS) navFor(loadMap(id));
  server.listen(port, '127.0.0.1', () => console.log(`Unity bridge listening on 127.0.0.1:${port}`));
}

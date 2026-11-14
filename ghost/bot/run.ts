/**
 * Ghost Rematch bot runtime.
 *
 * `node ghost/bot/run.ts --server http://localhost:3000 --name bot1 --team attack`
 *
 * The bot joins over socket.io exactly like a browser client, keeps a world model
 * from the server's snapshots, perceives enemies through its own eyes
 * (`createPerception`), builds the one `Situation` (`buildSituation`), and lets
 * either the cloned ghost policy or `default-bot.ts` pick an `Action`, which
 * `executor.ts` turns into the normal client messages.
 */
import { writeSync } from 'node:fs';

import type { Action, Situation, Team, TraceEvent, Weapon } from '../contracts.ts';
import { buildSituation } from '../clone/situation.ts';
import { calloutChanged } from '../coach/callouts.ts';
import type { BreakerVerdict, FallbackReason } from './breaker.ts';
import { createBreaker } from './breaker.ts';
import { createBotClient, type BotClient, type JoinedMessage } from './client.ts';
import { createDefaultMemory, decide as decideDefault, seedFromName, type DefaultBotMemory } from './default-bot.ts';
import { createExecutor } from './executor.ts';
import { createPerception, type Perception } from './view.ts';
import { loadMapExport, loadZones } from './nav.ts';
import { DEFAULTS, skillNumbers, type GhostDecision, type GhostPolicy, type SkillNumbers } from './policy-types.ts';
import { buildTeamContext, followMateFor, TEAM_RULES, teamAdjust } from './team.ts';
import { createTraceGate, makeTrace } from './trace.ts';
import { WEAPONS } from './weapons.ts';
import {
  createWorld,
  type AbilityMessage,
  type DamageMessage,
  type KillMessage,
  type Snapshot,
  type WorldPlayerState,
} from './world.ts';

const USAGE =
  'usage: node ghost/bot/run.ts --server <url> --name <name> [--team attack|defense] [--ghost-of <player>] [--agent omen]';
const TICK_MS = 50;
const TRACE_GAP_MS = 500;
const CHAT_GAP_MS = 2000;

interface Cli {
  server: string;
  name: string;
  team?: Team;
  ghostOf: string | null;
  agent: string;
}

function parseArgs(argv: string[]): Cli {
  const cli: Cli = { server: '', name: '', ghostOf: null, agent: 'omen' };
  for (let i = 0; i < argv.length; i += 1) {
    const flag = argv[i];
    const value = argv[i + 1];
    const next = (): string => {
      if (value == null || value.startsWith('--')) throw new Error(`${flag} needs a value`);
      i += 1;
      return value;
    };
    switch (flag) {
      case '--server':
        cli.server = next();
        break;
      case '--name':
        cli.name = next();
        break;
      case '--team': {
        const team = next();
        if (team !== 'attack' && team !== 'defense') throw new Error(`--team must be attack or defense, got ${team}`);
        cli.team = team;
        break;
      }
      case '--ghost-of':
        cli.ghostOf = next();
        break;
      case '--agent':
        cli.agent = next();
        break;
      default:
        throw new Error(`unknown argument ${flag}`);
    }
  }
  if (!cli.server) throw new Error('--server is required');
  if (!cli.name) throw new Error('--name is required');
  return cli;
}

function parseOrExit(): Cli {
  try {
    return parseArgs(process.argv.slice(2));
  } catch (err) {
    console.error(err instanceof Error ? err.message : String(err));
    console.error(USAGE);
    process.exit(2);
  }
}

/** The best gun the bot can afford, only when it beats what it already holds. */
const BUY_ORDER: Weapon[] = ['operator', 'vandal', 'phantom', 'spectre', 'sheriff'];
function chooseBuy(self: WorldPlayerState | null): Extract<Action, { kind: 'buy' }> | null {
  if (!self) return null;
  const current = WEAPONS[self.weapon as Weapon];
  const currentPrice = current ? current.price : 0;
  for (const item of BUY_ORDER) {
    const stats = WEAPONS[item];
    if (!stats || stats.price <= currentPrice) continue;
    if (self.money >= stats.price) return { kind: 'buy', item };
  }
  return null;
}

interface PolicyModule {
  loadPolicy?: (player: string) => GhostPolicy | null;
}

async function main(): Promise<void> {
  const cli = parseOrExit();
  const client: BotClient = createBotClient({
    server: cli.server,
    name: cli.name,
    agent: cli.agent,
    team: cli.team,
  });
  const socket = client.socket;
  let botName = cli.name;
  let stopping = false;
  let timer: ReturnType<typeof setInterval> | null = null;

  const stop = (line: string, code = 0): void => {
    if (stopping) return;
    stopping = true;
    if (timer) clearInterval(timer);
    timer = null;
    socket.disconnect();
    writeSync(1, `${line}\n`);
    process.exit(code);
  };

  process.once('SIGINT', () => stop(`${botName}: stopped (SIGINT)`));
  process.once('SIGTERM', () => stop(`${botName}: stopped (SIGTERM)`));
  socket.on('disconnect', (reason: string) => stop(`${botName}: disconnected (${reason})`));

  let joined: JoinedMessage;
  try {
    joined = await client.joined;
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    console.error(`${cli.name}: could not join ${cli.server}: ${message}`);
    client.close();
    process.exit(1);
  }
  botName = joined.me.name;

  const world = createWorld();
  world.setSelfName(botName);
  world.applySnapshot(joined.snapshot as Snapshot, Date.now(), 'joined');
  /** Set when a round/scenario restarts sight timers, so nobody is pre-reacted. */
  let perceptionReset = true;

  socket.on('tick', (snapshot: Snapshot) => world.applySnapshot(snapshot, Date.now(), 'tick'));
  socket.on('round-start', (snapshot: Snapshot) => {
    world.applySnapshot(snapshot, Date.now(), 'round-start');
    perceptionReset = true;
  });
  socket.on('phase', (snapshot: Snapshot) => world.applySnapshot(snapshot, Date.now(), 'phase'));
  socket.on('round-end', (snapshot: Snapshot) => world.applySnapshot(snapshot, Date.now(), 'round-end'));
  socket.on('scenario-loaded', () => {
    world.resetClock(Date.now());
    perceptionReset = true;
  });
  socket.on('ability', (msg: AbilityMessage) => world.applyAbility(msg, Date.now()));
  socket.on('damage', (msg: DamageMessage) => world.applyDamage(msg, Date.now()));
  socket.on('kill', (msg: KillMessage) => world.applyKill(msg, Date.now()));
  // Teammate bots announce every decision; the team layer reads their action only.
  socket.on('trace', (msg: unknown) => world.applyTrace(msg, Date.now()));

  let ghostOf: string | null = cli.ghostOf;
  let skillScale = 1;
  let policy: GhostPolicy | null = null;
  let policyNote = '';
  let policyLoadedFor: string | null | undefined;
  let policyToken = 0;
  let loggedPolicyError = false;

  async function ensurePolicy(player: string | null): Promise<void> {
    if (player === policyLoadedFor) return;
    policyLoadedFor = player;
    const token = ++policyToken;
    policy = null;
    policyNote = '';
    loggedPolicyError = false;
    if (!player) return;
    const spec = new URL('../clone/policy.ts', import.meta.url).href;
    try {
      const mod = (await import(spec)) as PolicyModule;
      if (token !== policyToken) return;
      if (typeof mod.loadPolicy !== 'function') {
        policyNote = `ghost ${player}: clone policy module has no loadPolicy`;
        return;
      }
      const loaded = mod.loadPolicy(player);
      if (token !== policyToken) return;
      if (!loaded) {
        policyNote = `ghost ${player}: no recorded policy`;
        return;
      }
      policy = loaded;
    } catch (err) {
      if (token !== policyToken) return;
      const message = err instanceof Error ? err.message : String(err);
      policyNote = `ghost ${player}: clone policy unavailable (${message})`;
    }
  }

  await ensurePolicy(ghostOf);

  socket.on('bot-config', (cfg: { ghostOf?: unknown; skillScale?: unknown; drillPlayer?: unknown }) => {
    if (!cfg || typeof cfg !== 'object') return;
    const requested = typeof cfg.ghostOf === 'string' && cfg.ghostOf ? cfg.ghostOf : null;
    if (typeof cfg.skillScale === 'number' && Number.isFinite(cfg.skillScale)) {
      skillScale = Math.min(1.5, Math.max(0.5, cfg.skillScale));
    }
    if (requested !== ghostOf) {
      ghostOf = requested;
      void ensurePolicy(ghostOf);
    }
  });

  const boxes = loadMapExport().colliders;
  const zones = loadZones();
  const zoneName = (id: string): string => zones.zones.find((z) => z.id === id)?.name ?? id;
  const perceptionOptions = { reactionMs: DEFAULTS.reactionMs };
  let perception: Perception | null = null;
  const breaker = createBreaker();
  const executor = createExecutor();
  const traceGate = createTraceGate(TRACE_GAP_MS);
  const seed = seedFromName(botName);
  let mem: DefaultBotMemory = createDefaultMemory(seed);
  let prevAction: Action | null = null;
  let lastChatAt = -Infinity;
  let roundSeen = -1;
  let lastLoopAt: number | null = null;

  function noteForFallback(decision: GhostDecision | null): string {
    if (policyNote) return policyNote;
    if (!policy) return `ghost ${ghostOf ?? '?'}: no policy loaded`;
    if (decision) {
      const threshold = policy.threshold.toFixed(2);
      return `nearest recording is ${decision.distance.toFixed(2)} away, over the ${threshold} threshold`;
    }
    return `ghost ${ghostOf ?? '?'}: no close recording for this situation`;
  }

  function tick(now: number): void {
    if (stopping) return;
    const self = world.self();
    if (!self) return;

    if (world.round !== roundSeen) {
      roundSeen = world.round;
      mem = createDefaultMemory(seed);
      breaker.reset();
      traceGate.reset();
      prevAction = null;
      executor.resetRound();
      perceptionReset = true;
    }
    executor.sync(self);

    const dt = lastLoopAt == null ? TICK_MS / 1000 : Math.min(0.25, Math.max(0, (now - lastLoopAt) / 1000));
    lastLoopAt = now;

    if (!self.alive || world.phase === 'end') {
      prevAction = null;
      return;
    }

    const skill: SkillNumbers = skillNumbers(policy?.profile ?? null, skillScale);
    perceptionOptions.reactionMs = skill.reactionMs;
    if (!perception || perceptionReset) {
      perception = createPerception(perceptionOptions);
      perceptionReset = false;
    }
    const execPos = executor.position();
    const aimView = executor.view();
    const perceptionUpdate = perception.update(
      now,
      { pos: { x: execPos.x, y: 0, z: execPos.z }, yaw: aimView.yaw },
      world.enemies(botName),
      boxes,
      world.activeSmokes(now),
    );
    const abilities = { [botName]: { Q: false, E: !executor.abilityUsed(), X: false } };
    const situation: Situation = buildSituation(world.view(now, abilities), botName, perceptionUpdate, now);

    let action: Action;
    let reason: string;
    let matched: TraceEvent['matched'] = null;
    let matchDistance: number | null = null;
    let fallback = false;
    let fallbackReason: FallbackReason | null = null;
    /** A teammate to stay 2-4 m behind, set when the team layer chose a follow. */
    let followName: string | null = null;

    if (world.phase === 'buy') {
      const buy = chooseBuy(self);
      action = buy ?? { kind: 'hold', pos: { x: self.pos.x, z: self.pos.z }, watchYaw: executor.view().yaw };
      reason = buy ? `buy phase: buying ${buy.item}` : 'buy phase: holding still at the spawn';
    } else {
      let ghostDecision: GhostDecision | null = null;
      if (policy && ghostOf) {
        try {
          ghostDecision = policy.decide(situation);
        } catch (err) {
          if (!loggedPolicyError) {
            loggedPolicyError = true;
            const message = err instanceof Error ? err.message : String(err);
            console.error(`${botName}: ghost ${ghostOf} decide() failed: ${message}`);
          }
          ghostDecision = null;
        }
      }

      let verdict: BreakerVerdict = { control: 'default', fallbackReason: null };
      if (ghostOf) {
        verdict = breaker.choose(
          now,
          policy && ghostDecision ? { distance: ghostDecision.distance, threshold: policy.threshold } : null,
        );
      }

      if (ghostOf && verdict.control === 'ghost' && policy && ghostDecision) {
        action = ghostDecision.action;
        matched = ghostDecision.matched;
        matchDistance = ghostDecision.distance;
        reason = `ghost ${ghostOf}: ${action.kind}, match distance ${ghostDecision.distance.toFixed(2)}`;
      } else {
        let nearest: { name: string; dist: number } | null = null;
        for (const v of perceptionUpdate.visible) {
          const d = Math.hypot(v.pos.x - execPos.x, v.pos.z - execPos.z);
          if (!nearest || d < nearest.dist) nearest = { name: v.name, dist: d };
        }
        mem.visibleTarget = nearest ? nearest.name : null;
        let latest: { pos: { x: number; z: number }; t: number } | null = null;
        for (const seen of Object.values(perceptionUpdate.lastSeen)) {
          if (!latest || seen.t > latest.t) latest = { pos: { x: seen.pos.x, z: seen.pos.z }, t: seen.t };
        }
        mem.lastSeenPos = latest ? { ...latest.pos } : null;
        mem.spikeSite = world.spike.site;
        mem.spikePos = world.spike.planted
          ? world.spike.pos
            ? { ...world.spike.pos }
            : null
          : world.spike.dropped
            ? { ...world.spike.dropped }
            : null;
        // `spikeState === 'carried'` only says the spike is in play; the world
        // knows who holds it, and only that bot may plant.
        mem.selfHasSpike = world.spike.holder === botName;

        const decided = decideDefault(situation, mem);
        action = decided.action;
        reason = decided.reason;
        matched = null;
        matchDistance = null;
        fallback = ghostOf != null;
        fallbackReason = ghostOf ? (verdict.fallbackReason ?? 'no-close-recording') : null;
        if (ghostOf) reason = `${reason} — ${noteForFallback(ghostDecision)}`;
      }

      // Team play on top of whichever policy chose: rotate, trade, follow, split angles.
      const teamCtx = buildTeamContext(world, botName, now, perceptionUpdate.lastSeen);
      const adjusted = teamAdjust({ action, reason }, situation, teamCtx);
      action = adjusted.action;
      reason = adjusted.teamRule ? `${adjusted.reason} (team: ${adjusted.teamRule})` : adjusted.reason;
      if (adjusted.teamRule === TEAM_RULES.followPush && action.kind === 'push') {
        followName = followMateFor(situation.selfPos, teamCtx, action.zone)?.name ?? null;
      }

      breaker.action(now, action.kind);
    }

    const result = executor.tick(action, {
      now,
      dt,
      world,
      skill,
      rng: Math.random,
      visible: perceptionUpdate.visible,
      boxes,
      followName,
    });

    socket.emit('move', result.move);
    if (result.tracer) socket.emit('tracer', result.tracer);
    if (result.hit) {
      const victim = world.byName(result.hit.victim);
      if (victim) {
        socket.emit('hit', {
          victimId: victim.id,
          damage: result.hit.damage,
          headshot: result.hit.headshot,
          weapon: result.hit.weapon,
        });
      }
    }
    if (result.ability) {
      socket.emit('ability', result.ability);
      world.addSmoke({ x: result.ability.at[0], z: result.ability.at[1] }, now);
    }
    if (result.plantHold) socket.emit('plant-hold');
    if (result.defuseHold) socket.emit('defuse-hold');
    if (result.buy) socket.emit('buy', { item: result.buy });

    breaker.sample(now, result.tryingToMove, executor.position());

    const key = JSON.stringify(action);
    if (traceGate.due(now, key)) {
      traceGate.mark(now, key);
      socket.emit(
        'trace',
        makeTrace({
          bot: botName,
          ghostOf,
          round: world.round,
          t: world.t(now),
          action,
          reason,
          matched,
          matchDistance,
          fallback,
          fallbackReason,
          skillScale,
          situation,
        }),
      );
    }

    const line = calloutChanged(prevAction, action, zoneName);
    if (line && now - lastChatAt >= CHAT_GAP_MS) {
      socket.emit('chat', line);
      lastChatAt = now;
    }
    prevAction = action;
  }

  let tickErrors = 0;
  timer = setInterval(() => {
    try {
      tick(Date.now());
    } catch (err) {
      tickErrors += 1;
      if (tickErrors <= 3) {
        const message = err instanceof Error ? err.stack ?? err.message : String(err);
        console.error(`${botName}: tick failed (${tickErrors}): ${message}`);
      }
    }
  }, TICK_MS);
  tick(Date.now());

  writeSync(1, `${botName}: joined ${cli.server} as ${joined.me.team}${ghostOf ? `, ghost of ${ghostOf}` : ''}\n`);
}

main().catch((err: unknown) => {
  const message = err instanceof Error ? err.stack ?? err.message : String(err);
  console.error(`bot runtime failed: ${message}`);
  process.exit(1);
});

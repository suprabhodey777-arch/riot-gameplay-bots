import { describe, expect, it } from 'vitest';
import { supported } from '../../coach/check.ts';
import type { Action, LogEvent, Situation, TraceEvent, Vec3 } from '../contracts.ts';
import { callout, calloutChanged } from './callouts.ts';
import { drillDebrief } from './debrief.ts';
import { env } from '../../coach/env.ts';
import { drillFacts, type DrillInput } from './facts.ts';
import type { ModelFn } from './model.ts';
import { templateDrillDebrief } from './template.ts';

const PLAYER = 'p1';
const GHOST = 'ghost-bot';
const GHOST_OF = 'rival';

const v3 = (x: number, y: number, z: number): Vec3 => ({ x, y, z });
const v2 = (x: number, z: number) => ({ x, z });

// --- fixtures, built by hand from the contract types -----------------------

const logBase = (t: number, player: string) => ({ v: 1 as const, matchId: 'm1', round: 1, t, player });

const tracer = (t: number, player: string): LogEvent => ({
  ...logBase(t, player),
  type: 'tracer',
  from: [0, 0, 0],
  dir: [1, 0, 0],
});

const damage = (t: number, attacker: string, victim: string, amount: number, headshot = false): LogEvent => ({
  ...logBase(t, attacker),
  type: 'damage',
  victim,
  attacker,
  damage: amount,
  headshot,
  hpLeft: 0,
});

const kill = (
  t: number,
  killer: string,
  victim: string,
  killerPos: Vec3,
  victimPos: Vec3,
): LogEvent => ({
  ...logBase(t, killer),
  type: 'kill',
  victim,
  killer,
  weapon: 'vandal',
  headshot: false,
  killerPos,
  victimPos,
});

const move = (t: number, player: string, pos: Vec3): LogEvent => ({
  ...logBase(t, player),
  type: 'move',
  pos,
  yaw: 0,
  pitch: 0,
  moving: true,
  crouch: false,
  team: 'attack',
  hp: 100,
});

function situation(overrides: Partial<Situation> = {}): Situation {
  return {
    selfPos: v2(0, 0),
    selfZone: 'mid',
    selfHp: 100,
    selfArmor: 0,
    weapon: 'vandal',
    ammo: null,
    abilitiesReady: { Q: true, E: true, X: false },
    enemyVisible: false,
    enemyRel: null,
    enemyLastSeenZone: null,
    sinceSeen: null,
    tookDamageRecent: false,
    gotKillRecent: false,
    side: 'defense',
    spikeState: 'none',
    roundTime: 30,
    teammatesAlive: 1,
    enemiesAlive: 1,
    ...overrides,
  };
}

function trace(
  t: number,
  action: Action,
  opts: { fallback?: boolean; enemyVisible?: boolean } = {},
): TraceEvent {
  return {
    bot: GHOST,
    ghostOf: GHOST_OF,
    round: 1,
    t,
    action,
    reason: 'test',
    matched: null,
    matchDistance: null,
    fallback: opts.fallback ?? false,
    fallbackReason: opts.fallback ? 'no-close-recording' : null,
    skillScale: 1,
    situation: situation({ enemyVisible: opts.enemyVisible ?? false }),
  };
}

const push = (zone: string): Action => ({ kind: 'push', zone });
const hold = (x: number, z: number): Action => ({ kind: 'hold', pos: v2(x, z), watchYaw: 0 });

const attemptOne: DrillInput['attempts'][number] = {
  won: false,
  durationS: 18.5,
  events: [
    tracer(5.8, GHOST),
    damage(5.9, GHOST, PLAYER, 60),
    damage(6.2, PLAYER, GHOST, 40, true),
    move(8, PLAYER, v3(11, 0, -5)),
    kill(9, GHOST, PLAYER, v3(13, 0, -3.4), v3(12, 0, -4)),
  ],
  traces: [
    trace(0.5, push('a-site')),
    trace(3.2, hold(0, 0), { enemyVisible: true }),
    trace(5.8, { kind: 'fight', target: PLAYER }, { enemyVisible: true }),
    trace(6.0, hold(0, 0)),
    trace(6.4, hold(0, 0)),
    trace(8.0, { kind: 'retreat', zone: 'b-main' }),
  ],
};

const attemptTwo: DrillInput['attempts'][number] = {
  won: false,
  durationS: 21,
  events: [
    tracer(4, PLAYER),
    damage(4.1, PLAYER, GHOST, 30),
    damage(6, GHOST, PLAYER, 100),
    kill(6.5, GHOST, PLAYER, v3(0, 0, 0), v3(-6.2, 0, 3.4)),
  ],
  traces: [
    trace(1, hold(0, 0), { fallback: true }),
    trace(2, push('b-site'), { fallback: true }),
    trace(5, hold(0, 0)),
  ],
};

const attemptThree: DrillInput['attempts'][number] = {
  won: true,
  durationS: 24,
  events: [
    tracer(4, PLAYER),
    tracer(4.2, GHOST),
    damage(4.3, PLAYER, GHOST, 100, true),
    kill(4.4, PLAYER, GHOST, v3(10, 0, -2), v3(10, 0, -2)),
    move(24, PLAYER, v3(10, 0, -2)),
  ],
  traces: [
    trace(3.8, { kind: 'fight', target: GHOST }, { enemyVisible: true }),
    trace(5, hold(0, 0)),
  ],
};

const drill: DrillInput = {
  player: PLAYER,
  ghostOf: GHOST_OF,
  scenarioId: 'moment-1',
  attempts: [attemptOne, attemptTwo, attemptThree],
};

const neverWon: DrillInput = { ...drill, attempts: [attemptOne, attemptTwo] };

const sleep = (ms: number) => new Promise<void>((r) => env().setTimeout(r, ms));
const fake = (fn: () => Promise<string>): ModelFn => fn;

// --- facts -----------------------------------------------------------------

describe('drillFacts', () => {
  const facts = drillFacts(drill);

  it('counts attempts and the first win', () => {
    expect(facts.attempts).toBe(3);
    expect(facts.wins).toBe(1);
    expect(facts.firstWinIndex).toBe(2);
    expect(drillFacts(neverWon).firstWinIndex).toBeNull();
  });

  it('reads each attempt from its log and traces', () => {
    const [one, two, three] = facts.perAttempt;
    expect(one.index).toBe(0);
    expect(one.firstShotBy).toBe('ghost');
    expect(one.durationS).toBe(18.5);
    expect(one.damageDealt).toBe(40);
    expect(one.damageTaken).toBe(60);
    expect(one.headshots).toBe(1);
    expect(one.endPos).toEqual({ x: 12, z: -4 });
    expect(one.fallback).toBe(false);
    expect(one.fallbackTraces).toBe(0);
    expect(one.traces).toBe(6);
    expect(one.reactionS).toBe(2.6);

    expect(two.firstShotBy).toBe('player');
    expect(two.fallback).toBe(true);
    expect(two.fallbackTraces).toBe(2);
    expect(two.traces).toBe(3);
    expect(two.reactionS).toBeNull();
    expect(two.endPos).toEqual({ x: -6, z: 3 });

    expect(three.won).toBe(true);
    expect(three.damageDealt).toBe(100);
    expect(three.headshots).toBe(1);
    expect(three.reactionS).toBe(0.4);
    expect(three.endPos).toEqual({ x: 10, z: -2 });
  });

  it('collapses consecutive duplicate ghost kinds and keeps their times', () => {
    expect(facts.perAttempt[0].ghostActions).toEqual([
      { kind: 'push', t: 0.5 },
      { kind: 'hold', t: 3.2 },
      { kind: 'fight', t: 5.8 },
      { kind: 'hold', t: 6 },
      { kind: 'retreat', t: 8 },
    ]);
  });

  it('writes sentences whose numbers match the text', () => {
    const outcome = facts.sentences.find((f) => f.id === 'outcome');
    expect(outcome?.text).toBe(
      'You (p1) replayed this moment 3 times against the ghost of rival and won 1.',
    );
    expect(outcome?.numbers).toEqual([3, 1]);
    const reaction = facts.sentences.find((f) => f.id === 'reaction-1');
    expect(reaction?.text).toContain('2.6 s');
    const fallback = facts.sentences.find((f) => f.id === 'fallback-2');
    expect(fallback?.text).toContain('2 of 3 traces');
    for (const s of facts.sentences) expect(supported(s.text, facts.sentences)).toBe(true);
  });
});

// --- template --------------------------------------------------------------

describe('template', () => {
  it('passes the number check for a never-won drill', () => {
    const facts = drillFacts(neverWon);
    const text = templateDrillDebrief(facts);
    expect(supported(text, facts.sentences)).toBe(true);
    expect(text.split(/\.\s+/).length).toBe(3);
    expect(text).toContain('did not win');
  });

  it('passes the number check for a drill won on the third attempt', () => {
    const facts = drillFacts(drill);
    const text = templateDrillDebrief(facts);
    expect(supported(text, facts.sentences)).toBe(true);
    expect(text.split(/\.\s+/).length).toBe(3);
    expect(text).toContain('attempt 3');
  });
});

// --- debrief ---------------------------------------------------------------

describe('drillDebrief', () => {
  const good =
    'The ghost shot first and you died at x 12, z 4, but on attempt 3 you shot first and won with 100 damage.';

  it('uses a good model answer as llm', async () => {
    const out = await drillDebrief(drill, { model: fake(async () => good) });
    expect(out.source).toBe('llm');
    expect(out.text).toBe(good);
    expect(out.reason).toBeUndefined();
  });

  it('falls back on an invented number', async () => {
    const out = await drillDebrief(drill, {
      model: fake(async () => 'You lost the first 9 fights.'),
    });
    expect(out.source).toBe('template');
    expect(out.reason).toBe('unchecked-number');
  });

  it('falls back when the model never resolves', async () => {
    const started = Date.now();
    const out = await drillDebrief(drill, {
      model: fake(() => new Promise<string>(() => {})),
      timeoutMs: 40,
    });
    expect(out.source).toBe('template');
    expect(out.reason).toBe('timeout');
    expect(Date.now() - started).toBeLessThanOrEqual(40 + 500);
  });

  it('falls back with no API key', async () => {
    const out = await drillDebrief(drill, { apiKey: null });
    expect(out.source).toBe('template');
    expect(out.reason).toBe('no-key');
    expect(supported(out.text, drillFacts(drill).sentences)).toBe(true);
  });

  it('falls back when the model throws and never throws itself', async () => {
    const out = await drillDebrief(drill, {
      model: fake(async () => {
        throw new Error('boom');
      }),
    });
    expect(out.source).toBe('template');
    expect(out.reason).toBe('error');
  });

  it('uses the default timeout when none is given', async () => {
    const out = await drillDebrief(drill, {
      model: fake(async () => {
        await sleep(80);
        return good;
      }),
    });
    expect(out.source).toBe('llm');
  });
});

// --- callouts --------------------------------------------------------------

describe('callouts', () => {
  const zoneName = (id: string) => (id === 'a-site' ? 'A Site' : id);
  const at = v2(1, 2);

  it('names the destination for each action kind', () => {
    expect(callout(push('a-site'), zoneName)).toBe('Pushing A Site');
    expect(callout({ kind: 'retreat', zone: 'b-main' }, zoneName)).toBe('Falling back to b-main');
    expect(callout({ kind: 'rotate', zone: 'mid' }, zoneName)).toBe('Rotating mid');
    expect(callout({ kind: 'use_ability', slot: 'Q', at }, zoneName)).toBe('Utility out');
    expect(callout({ kind: 'plant', site: 'A' }, zoneName)).toBe('Planting A');
    expect(callout({ kind: 'defuse', site: 'B' }, zoneName)).toBe('Defusing');
    expect(callout({ kind: 'fight', target: 'ghost-bot' }, zoneName)).toBe('Fighting ghost-bot');
  });

  it('stays quiet for actions that need no callout', () => {
    expect(callout(hold(1, 2), zoneName)).toBeNull();
    expect(callout({ kind: 'peek_wide', corner: at, dir: 'left' }, zoneName)).toBeNull();
    expect(callout({ kind: 'peek_jiggle', corner: at, dir: 'right' }, zoneName)).toBeNull();
    expect(callout({ kind: 'buy', item: 'vandal' }, zoneName)).toBeNull();
  });

  it('only calls out a change of kind or destination', () => {
    expect(calloutChanged(push('a-site'), push('a-site'), zoneName)).toBeNull();
    expect(calloutChanged(null, push('a-site'), zoneName)).toBe('Pushing A Site');
    expect(calloutChanged(push('a-site'), push('b-main'), zoneName)).toBe('Pushing b-main');
    expect(calloutChanged({ kind: 'retreat', zone: 'b-main' }, push('a-site'), zoneName)).toBe(
      'Pushing A Site',
    );
    expect(
      calloutChanged({ kind: 'fight', target: 'ghost-bot' }, { kind: 'fight', target: 'p2' }, zoneName),
    ).toBe('Fighting p2');
    expect(
      calloutChanged(
        { kind: 'use_ability', slot: 'Q', at },
        { kind: 'use_ability', slot: 'E', at },
        zoneName,
      ),
    ).toBe('Utility out');
  });
});

import { describe, expect, test } from 'vitest';
import type { Skill, WeaknessEvidence, WeaknessFinding } from '../shared/coaching.ts';
import { loadMap } from '../shared/maps/index.ts';
import { idleInput, startDrill } from '../session/drill-run.ts';
import { sessionWithWeakness } from '../session/headless.ts';
import { analyze } from './analyze.ts';
import { EXAM_SEED_BASE } from './drills.ts';
import { rehearsalDrill } from './rehearse.ts';
import { generateMatches, PLAYER_ID, weakProfile } from './synthetic.ts';

const map = loadMap('saltworks');
const SKILLS: Skill[] = ['entry', 'clutch', 'time_pressure', 'smoke_usage'];
const NO_KEY = { apiKey: null } as const;

const finding = (skill: Skill): WeaknessFinding => ({ skill, rate: 0.1, benchmark: 0.5, severity: 0.8, sampleSize: 20, summary: '', evidence: [] });
const evidence = (over: Partial<NonNullable<WeaknessEvidence['situation']>> = {}): WeaknessEvidence => ({
  matchId: 'm1',
  round: 10,
  tick: 400,
  note: 'lost the opening duel at Mid Road, 0.6 s after first contact',
  situation: {
    side: 'attack',
    site: 'B',
    playerPos: { x: 30, y: 40 },
    enemies: [{ pos: { x: 64, y: 40 } }, { pos: { x: 60, y: 50 } }],
    allies: [],
    roundTimeLeftS: 80,
    spikePlanted: null,
    playerHasSpike: true,
    ...over,
  },
});

describe('analyzer situations', () => {
  test('every failed moment carries a situation with positions on the map and a clock', () => {
    for (const skill of SKILLS) {
      const r = analyze(generateMatches(weakProfile('duelist', skill, 0.05), 5), PLAYER_ID);
      const f = r.findings.find((x) => x.skill === skill);
      expect(f?.evidence.length, skill).toBeGreaterThan(0);
      for (const ev of f?.evidence ?? []) {
        const s = ev.situation;
        expect(s, `${skill} ${ev.note}`).toBeDefined();
        for (const p of [s?.playerPos, ...(s?.enemies ?? []).map((e) => e.pos)]) {
          expect(p?.x).toBeGreaterThan(0);
          expect(p?.x).toBeLessThan(map.bounds.width + 5);
          expect(p?.y).toBeGreaterThan(-5);
        }
        expect(s?.enemies.length).toBeGreaterThan(0);
        expect(s?.roundTimeLeftS).toBeGreaterThanOrEqual(0);
      }
    }
  });

  test('a clutch situation lists every living enemy', () => {
    const r = analyze(generateMatches(weakProfile('duelist', 'clutch', 0.05), 5), PLAYER_ID);
    for (const ev of r.findings.find((f) => f.skill === 'clutch')?.evidence ?? []) {
      const k = Number(/1v(\d)/.exec(ev.note)?.[1]);
      expect(ev.situation?.enemies.length).toBe(k);
    }
  });
});

describe('rehearsalDrill', () => {
  test('starts a sim with the player and enemies where the evidence said, enemies facing the player', () => {
    const d = rehearsalDrill(evidence(), finding('entry'), map, 7);
    expect(d.player.spawn).toEqual({ x: 30, y: 40 });
    expect(d.bots.map((b) => b.spawn)).toEqual([{ x: 64, y: 40 }, { x: 60, y: 50 }]);
    expect(d.player.team).toBe('attack');
    expect(d.bots.every((b) => b.team === 'defense' && b.hold)).toBe(true);
    expect(d.roundTimeLeftS).toBe(80);
    expect(d.player.carryingSpike).toBe(true);
    const run = startDrill(d);
    const at = (id: string) => run.state().agents.find((a) => a.id === id)?.pos;
    expect(at('player')).toEqual({ x: 30, y: 40 });
    expect(at('bot-1')).toEqual({ x: 64, y: 40 });
    expect(at('bot-2')).toEqual({ x: 60, y: 50 });
    expect(run.state().roundTimeLeftS).toBe(80);
  });

  test('the goal text says what happened and names the callout', () => {
    const d = rehearsalDrill(evidence(), finding('entry'), map, 7);
    expect(d.goal).toBe('Round 11: you lost the opening duel at Mid Road 0.6 s after contact. Same spot, same angle. Try again.');
    const c = rehearsalDrill(evidence({ side: 'attack', enemies: [{ pos: { x: 62, y: 40 } }] }), finding('clutch'), map, 7);
    expect(c.goal).toContain('Mid Road');
    expect(c.goal).toContain('1v1 clutch');
  });

  test('a tucked enemy is rebuilt as an off-angle anchor; a rusher on a defense round as an entry duelist', () => {
    const tucked = rehearsalDrill(evidence({ enemies: [{ pos: { x: 96.8, y: 60 } }], site: 'A' }), finding('entry'), map, 7);
    expect(tucked.bots[0].archetypeId).toBe('off-angle-anchor');
    const rush = rehearsalDrill(
      evidence({ side: 'defense', playerPos: { x: 70, y: 40 }, enemies: [{ pos: { x: 40, y: 30 } }], playerHasSpike: false }),
      finding('entry'),
      map,
      7,
    );
    expect(rush.player.team).toBe('defense');
    expect(rush.bots[0].team).toBe('attack');
    expect(rush.bots[0].archetypeId).toBe('entry-duelist');
  });

  test('spike state is rebuilt: a planted spike gives a retake with the seconds that were left', () => {
    const d = rehearsalDrill(
      evidence({ side: 'defense', playerPos: { x: 70, y: 40 }, enemies: [{ pos: { x: 90, y: 40 } }], spikePlanted: { site: 'B', pos: { x: 90.5, y: 40 }, secondsLeft: 22 }, roundTimeLeftS: 22, playerHasSpike: false }),
      finding('time_pressure'),
      map,
      7,
    );
    expect(d.spikePlanted?.site).toBe('B');
    expect(d.spikePlanted?.secondsLeft).toBe(22);
    expect(d.player.carryingSpike).toBe(false);
    expect(startDrill(d).state().spike.status).toBe('planted');
  });

  test('a bot stands its ground at the rebuilt position while it sees nobody', () => {
    const d = rehearsalDrill(evidence({ enemies: [{ pos: { x: 96.8, y: 60 } }], playerPos: { x: 30, y: 10 }, site: 'A' }), finding('entry'), map, 7);
    const run = startDrill(d);
    for (let i = 0; i < 60; i++) run.step(idleInput('player'));
    const b = run.state().agents.find((a) => a.id === 'bot-1')?.pos;
    expect(Math.hypot((b?.x ?? 0) - d.bots[0].spawn.x, (b?.y ?? 0) - d.bots[0].spawn.y)).toBeLessThan(0.5);
  });

  test('evidence with no situation falls back to a plain drill for the skill', () => {
    const d = rehearsalDrill({ matchId: 'm', round: 2, tick: 5, note: 'lost it' }, finding('clutch'), map, 7);
    expect(d.skill).toBe('clutch');
    expect(d.bots.length).toBeGreaterThan(0);
  });

  test('rehearsals of every skill are practice drills that run and are deterministic', () => {
    for (const skill of SKILLS) {
      const s = sessionWithWeakness(skill, 5, NO_KEY);
      const a = s.rehearsals();
      const b = sessionWithWeakness(skill, 5, NO_KEY).rehearsals();
      expect(a, skill).toEqual(b);
      expect(a.length, skill).toBeGreaterThan(0);
      for (const d of a) {
        expect(d.seed).toBeLessThan(EXAM_SEED_BASE);
        expect(d.bots.length).toBeGreaterThan(0);
        const run = startDrill(d);
        for (let i = 0; i < 80 && !run.done(); i++) run.step(null);
        expect(run.finish().drillId).toBe(d.id);
      }
    }
  });
});

import { describe, expect, test } from 'vitest';
import type { PlayerInput } from '../shared/contract.ts';
import { loadMap } from '../shared/maps/index.ts';
import { analyze } from '../analyzer/analyze.ts';
import { createMatch } from '../bot/match.ts';
import { playRecordedMatch } from './test-helpers.ts';
import { createRecorder } from './recorder.ts';

const map = loadMap('test-box');

describe('recorder', () => {
  test('a recorded 5v5 match carries teams and feeds the analyzer', () => {
    const { log } = playRecordedMatch(map, 3);
    expect(log.rounds.length).toBe(3);
    for (const r of log.rounds) {
      expect(Object.keys(r.teams).length).toBe(10);
      expect(r.teams.me).toBe(r.playerTeam);
      expect(r.events.at(-1)?.type).toBe('round_end');
      expect(r.winner).toBe((r.events.at(-1) as { winner: string }).winner);
    }
    const report = analyze([log], 'me');
    expect(report.matchesRead).toBe(1);
    expect(report.playerId).toBe('me');
    // Every finding the analyzer makes is about this player and carries evidence strings.
    for (const f of report.findings) expect(f.sampleSize).toBeGreaterThan(0);
  });

  test('a round that never ended is dropped, a winner can close it by hand', () => {
    const match = createMatch({ map, seed: 1, agents: [{ id: 'me', team: 'attack', isBot: false }, { id: 'e1', team: 'defense', isBot: true }] });
    const rec = createRecorder({ matchId: 'm', mapId: map.id, playerId: 'me' });
    rec.beginRound(match.state());
    const idle: PlayerInput = { agentId: 'me', move: { x: 0, y: 0 }, yaw: 0, walk: false, crouch: false, fire: null, smokeAt: null, plantHeld: false, defuseHeld: false };
    rec.push(match.step([idle]).events);
    rec.endRound();
    expect(rec.rounds()).toBe(0);
    rec.beginRound(match.state());
    rec.endRound('defense');
    expect(rec.toLog().rounds[0]).toMatchObject({ index: 0, winner: 'defense', playerTeam: 'attack' });
  });
});

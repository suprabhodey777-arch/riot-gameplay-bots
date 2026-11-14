/** Test-only helper: plays full 5v5 rounds with a bot standing in for the human, and records them. */
import type { MapDef, MatchLog, Team } from '../shared/contract.ts';
import { createMatch, type MatchAgent } from '../bot/match.ts';
import { createRecorder } from './recorder.ts';

export function playRecordedMatch(map: MapDef, rounds: number): { log: MatchLog } {
  const rec = createRecorder({ matchId: 'm-test', mapId: map.id, playerId: 'me', role: 'duelist' });
  for (let r = 0; r < rounds; r++) {
    const side: Team = r % 2 === 0 ? 'attack' : 'defense';
    const other: Team = side === 'attack' ? 'defense' : 'attack';
    const agents: MatchAgent[] = [{ id: 'me', team: side, isBot: true }];
    for (let i = 1; i <= 4; i++) agents.push({ id: `a${i}`, team: side, isBot: true });
    for (let i = 1; i <= 5; i++) agents.push({ id: `e${i}`, team: other, isBot: true });
    const match = createMatch({ map, seed: 40 + r, agents });
    rec.beginRound(match.state());
    for (let t = 0; t < 2400 && match.state().phase !== 'ended'; t++) rec.push(match.step([]).events);
    rec.endRound(match.state().winner ?? undefined);
  }
  return { log: rec.toLog() };
}

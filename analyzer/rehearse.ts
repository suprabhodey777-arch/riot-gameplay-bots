/**
 * Rehearse my mistakes: rebuild the moment a player failed as a drill, so they can
 * play it again. Same side, same positions (snapped to walkable ground), the same
 * number of enemies standing where they were and facing the player, the same clock
 * and spike state. The enemy that beat the player gets the archetype that matches
 * how they won (an off-angle if they were tucked away, a rush if they came at the
 * player), the rest play the baseline.
 *
 * Positions come from `WeaknessEvidence.situation`; see `situation.ts` for which of
 * them are exact and which are approximations. Without a situation (an old log) the
 * evidence is rehearsed as an ordinary drill for the skill.
 */
import type { MapDef, Vec2 } from '../shared/contract.ts';
import type { DrillBot, DrillSpec, Situation, WeaknessEvidence, WeaknessFinding } from '../shared/coaching.ts';
import { minWallDist, spotsFor } from '../bot/nav.ts';
import { bot, type ArchetypeId, buildDrill, EXAM_SEED_BASE, PLAYER_ID, walkablePoint } from './drills.ts';
import { calloutAt, dist } from './geo.ts';

/** Difficulty of the rebuilt bots: the player lost to them once, so they are a fair middle. */
export const REHEARSAL_DIFFICULTY = 0.5;

const yawTo = (from: Vec2, to: Vec2): number => Math.atan2(to.y - from.y, to.x - from.x);
const clampN = (v: number, lo: number, hi: number): number => Math.min(hi, Math.max(lo, v));

/** What kind of opponent the enemy at `pos` was, judged from where it stood. */
export function archetypeFor(map: MapDef, s: Situation, pos: Vec2, skill: WeaknessFinding['skill']): ArchetypeId {
  if (skill === 'smoke_usage') return 'utility-controller';
  if (s.side === 'attack') {
    // The enemies hold. Tucked against a wall, or on a spot the common ones skip, is an off-angle.
    const tucked = minWallDist(pos, map.walls) < 1.3;
    const spots = s.site ? spotsFor(map, s.site, 'attack') : [];
    const near = spots.filter((p) => dist(p.pos, pos) <= 3).sort((a, b) => dist(a.pos, pos) - dist(b.pos, pos))[0];
    return tucked || (near && near.unusual > near.common) ? 'off-angle-anchor' : 'baseline';
  }
  // The enemies attack: guarding a planted spike is patient, otherwise they rush.
  return s.spikePlanted ? 'patient-lurker' : 'entry-duelist';
}

/** "Round 11: you lost the opening duel at A Pans 0.6 s after contact. Same spot, same angle. Try again." */
function goalText(evidence: WeaknessEvidence, finding: WeaknessFinding, s: Situation, map: MapDef, enemies: number): string {
  const round = `Round ${evidence.round + 1}`;
  const where = calloutAt(map, s.playerPos);
  const n = enemies === 1 ? '1 enemy' : `${enemies} enemies`;
  const sameSpot = 'Same spot, same angle. Try again.';
  switch (finding.skill) {
    case 'entry': {
      const lead = /([\d.]+) s after first contact/.exec(evidence.note);
      return `${round}: you lost the opening duel at ${where}${lead ? ` ${lead[1]} s after contact` : ''}. ${sameSpot}`;
    }
    case 'clutch':
      return `${round}: you lost a 1v${enemies} clutch near ${where} with ${s.spikePlanted ? `${s.spikePlanted.secondsLeft} s on the spike` : `${s.roundTimeLeftS} s left`}. ${n} stand where they stood. ${sameSpot}`;
    case 'time_pressure':
      return s.side === 'attack'
        ? `${round}: the clock beat you at ${where}. ${evidence.note.charAt(0).toUpperCase()}${evidence.note.slice(1)}. Plant with ${s.roundTimeLeftS} s left this time. ${sameSpot}`
        : `${round}: the retake from ${where} ran out of time. Defuse before the ${s.roundTimeLeftS} s are up. ${sameSpot}`;
    default:
      return `${round}: your smoke went wrong near ${where} (${evidence.note}). Throw it on the choke before the push. ${sameSpot}`;
  }
}

export function rehearsalDrill(evidence: WeaknessEvidence, finding: WeaknessFinding, map: MapDef, seedIn: number): DrillSpec {
  // Rehearsals are practice: their seed stays below the exam's.
  const seed = ((Math.floor(seedIn) % EXAM_SEED_BASE) + EXAM_SEED_BASE) % EXAM_SEED_BASE || 1;
  const s = evidence.situation;
  if (!s) {
    const d = buildDrill(finding.skill, REHEARSAL_DIFFICULTY, map, seed);
    return { ...d, id: `rehearse-${d.id}`, goal: `Round ${evidence.round + 1}: ${evidence.note}. No positions were logged, so this is a fresh drill for the same skill.` };
  }

  const place = (p: Vec2, k: number): Vec2 => walkablePoint(map, p, seed * 31 + k);
  const playerSpawn = place(s.playerPos, 0);
  const wasPlanted = s.spikePlanted !== null;

  let enemyPos = s.enemies.map((e, i) => place(e.pos, i + 1));
  // A smoke is thrown before the push: the player starts well back from the watched choke.
  let spawn = playerSpawn;
  if (finding.skill === 'smoke_usage' && enemyPos[0]) {
    const away = map.spawns.attack[0];
    const choke = enemyPos[0];
    const d = dist(playerSpawn, choke);
    if (d < 24) {
      const len = dist(away, choke) || 1;
      const k = Math.min(30, len - 2);
      spawn = place({ x: choke.x + ((away.x - choke.x) / len) * k, y: choke.y + ((away.y - choke.y) / len) * k }, 99);
    }
  }
  // Nobody starts on top of the player.
  enemyPos = enemyPos.map((p, i) => (dist(p, spawn) < 2 ? place({ x: p.x + 3, y: p.y + 3 }, i + 40) : p));

  const enemyTeam = s.side === 'attack' ? 'defense' : 'attack';
  const bots: DrillBot[] = enemyPos.map((p, i) => {
    const arche: ArchetypeId = i === 0 ? archetypeFor(map, s, p, finding.skill) : 'baseline';
    return { ...bot(i + 1, enemyTeam, p, arche, REHEARSAL_DIFFICULTY), hold: { pos: p, yaw: yawTo(p, spawn) } };
  });
  if (bots.length === 0) {
    const d = buildDrill(finding.skill, REHEARSAL_DIFFICULTY, map, seed);
    return { ...d, id: `rehearse-${d.id}` };
  }

  const left = clampN(s.roundTimeLeftS, 8, 100);
  const planted = s.spikePlanted
    ? { site: s.spikePlanted.site, pos: place(s.spikePlanted.pos, 77), secondsLeft: clampN(s.spikePlanted.secondsLeft, 6, 45) }
    : null;
  const hasSpike = !wasPlanted && s.playerHasSpike;
  return {
    id: `rehearse-${finding.skill}-${evidence.matchId}-r${evidence.round}-${evidence.tick}`,
    skill: finding.skill,
    title: `Rehearsal: round ${evidence.round + 1}`,
    goal: goalText(evidence, finding, s, map, bots.length),
    mapId: map.id,
    seed,
    difficulty: REHEARSAL_DIFFICULTY,
    player: { id: PLAYER_ID, team: s.side, spawn, hp: 100, carryingSpike: hasSpike },
    bots,
    roundTimeLeftS: planted ? planted.secondsLeft : left,
    spikePlanted: planted,
  };
}

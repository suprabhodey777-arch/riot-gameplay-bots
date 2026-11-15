/**
 * The showcase: four hand-authored drills, one per skill, cut for the 90 s demo.
 *
 * The generated drills are many, similar and start the player far from the action.
 * These four are curated instead: each one lives in a different part of Saltworks, is a
 * different situation (a held angle, a 1v2 retake, the clock, utility), and starts the
 * player 1-2 s of running from the action, facing it:
 *
 *   entry          North Causeway: 1v1 against an anchor holding the alley exit from the
 *                  causeway. The player starts at the foot of the alley (Mid Road), out of
 *                  the anchor's sight; the angle opens 8.5 m up the alley.
 *   clutch         B site: the spike is planted and two defenders retake it, one through
 *                  the back door (Rear Court) and one through B Door. The player holds the
 *                  spike from behind B's pillar, 4-5 s before either defender arrives.
 *   time_pressure  C Door: the player carries the spike with no time to spare. The clock is
 *                  the walk to the nearest plantable point + the plant + 3 s, computed here.
 *   smoke_usage    Kiln Gap: an anchor holds a 23 m angle across the gap from South
 *                  Causeway east, so crossing in the open is suicide until a smoke lands in
 *                  the gap. The player starts on the Crane Yard side, 8-9 m from the edge of
 *                  the anchor's view.
 *
 * Positions are hand-picked from `scripts/build-saltworks.ts` and snapped to walkable
 * ground with `walkablePoint`, the same way rehearsals place theirs. Seeds are fixed and
 * unique per drill, drawn from the exam band's neighbourhood but above it, so a showcase
 * drill can never equal an exam or practice drill.
 */
import type { MapDef, SiteId, Vec2 } from '../shared/contract.ts';
import type { Loadout } from '../shared/weapons.ts';
import type { DrillBot, DrillSpec, Skill } from '../shared/coaching.ts';
import { loadMap } from '../shared/maps/index.ts';
import { closestOnSegment, pointInPolygon, sitePoly } from '../sim/geometry.ts';
import { PLANT_TIME_S, RUN_SPEED } from '../sim/sim.ts';
import { pathBetween } from '../bot/nav.ts';
import { bot, EXAM_SEED_BASE, PLAYER_ID, walkablePoint, type ArchetypeId } from './drills.ts';
import { centroid } from './geo.ts';

export const SHOWCASE_MAP_ID = 'saltworks';
/** Every showcase drill is a fair middle: the player lost this kind of fight once. */
export const SHOWCASE_DIFFICULTY = 0.5;

/** The order the demo plays them in: one per skill. */
export const SHOWCASE_SKILLS: Skill[] = ['entry', 'clutch', 'time_pressure', 'smoke_usage'];

/**
 * Showcase seeds sit above the exam's 9000-9995 band, so no showcase drill can ever share a
 * seed with a holdout exam form or a practice drill (practice stays below 9000).
 */
export const SHOWCASE_SEED_BASE = EXAM_SEED_BASE + 1001;

export const SHOWCASE_SEEDS: Record<Skill, number> = {
  entry: SHOWCASE_SEED_BASE,
  clutch: SHOWCASE_SEED_BASE + 1,
  time_pressure: SHOWCASE_SEED_BASE + 2,
  smoke_usage: SHOWCASE_SEED_BASE + 3,
};

/** Seconds on the late-plant clock beyond the walk and the plant itself. */
export const SHOWCASE_MARGIN_S = 7;
/** How far inside the plant zone a plantable point is taken (a body needs room). */
const PLANT_INSET_M = 1;
/** Everyone in a showcase drill is on a rifle: these are even fights, not eco rounds. */
export const SHOWCASE_LOADOUT: Loadout = { primary: 'lancer', secondary: 'pin', armor: 'heavy', credits: 0 };

// ---- drill 1: the alley from Mid Road up into North Causeway ----

/**
 * Drill 1. The alley is the 4.5 m corridor between the Dock's east wall and Saltstore
 * (x 21.5-26, y 42-58); its mouth at y = 58.5 is North Causeway's south-west corner. The
 * anchor stands west of the causeway spine (x 30) where it can see the mouth, and the
 * saltstore corner hides everything below y ~ 51 in the alley - 8.5 m of running.
 */
export const ENTRY_SPOT = {
  /** Foot of the alley, at the Dock's east exit: the last cover before the causeway. */
  player: { x: 24, y: 42 },
  /** North Causeway, west of the spine, holding the alley exit. */
  hold: { x: 29.5, y: 70 },
  /** The alley mouth the anchor watches. */
  mouth: { x: 24.5, y: 58.5 },
} as const;

// ---- drill 2: the 1v2 post-plant on B ----

/**
 * Drill 2. The spike is down at B's centre; the player holds it from the pocket behind
 * `b-pillar-2` (92-94, 34.5-36.5), facing B Door. The retake comes from both doors at once:
 * `Rear Court` through the back door (x 98-99.5, y 36-44) and the front door (x 78-79.5,
 * y 36-44). The room's south strip (y < 36) is the one place neither door's sightline reaches.
 */
export const B_RETAKE_SPOT = {
  player: { x: 95.5, y: 36.8 },
  spike: { x: 90.5, y: 40 },
  /** Rear Court, behind B: comes through the back door. */
  rear: { x: 108, y: 29 },
  /** Outside B Door on the front side (Weighbridge): comes through the front door. */
  front: { x: 64, y: 31 },
  /** B Door's centre, what the player faces. */
  door: { x: 78.75, y: 40 },
} as const;

// ---- drill 3: the late plant on C ----

/**
 * Drill 3. The player starts in C Door on the South Causeway side (x 76.5, y 19), behind
 * the front wall's north shoulder, so the anchor holding C cannot see them through the
 * door. The door (x 78-79.5, y 10-18) is 7.5 m of wall away from the plant zone. The
 * anchor watches that door from C Pump Deck (90, 14): crossing in the open costs you, but
 * the near plant spot is behind `c-pillar-1`, so a quick plant can still finish.
 */
export const C_LATE_PLANT_SPOT = {
  player: { x: 76.5, y: 19 },
  /** C Pump Deck, 11 m inside the site, watching the door past c-pillar-1. */
  hold: { x: 90, y: 14 },
  /** C Door's centre, what the player faces and the anchor holds. */
  door: { x: 78.75, y: 14 },
} as const;

// ---- drill 4: the smoke over Kiln Gap ----

/**
 * Drill 4. Kiln Gap is the north-south corridor between `kiln-w` (26-40, 22-28) and
 * `kiln-e` (50-64, 22-28). An anchor in South Causeway east (58, 6) holds the crossing from
 * 23 m: it sees the whole crossing through the corridor's south mouth and nothing past the
 * gap (kiln-e shadows the way on to C). A smoke at the gap centre (radius 4.5 m) closes it.
 */
export const KILN_GAP_SPOT = {
  /** Crane Yard's south edge, just east of the gap's north mouth. */
  player: { x: 52, y: 32 },
  /** South Causeway east (Tide Steps side), 23 m from the gap centre. */
  hold: { x: 58, y: 6 },
  /** The middle of Kiln Gap: where the smoke goes and where the crossing is watched. */
  centre: { x: 45, y: 25 },
  /** South Causeway, 9 m past the gap centre and out of the anchor's sight. */
  goal: { x: 45, y: 16 },
} as const;

const yawTo = (from: Vec2, to: Vec2): number => Math.atan2(to.y - from.y, to.x - from.x);
const r1 = (v: number): number => Math.round(v * 10) / 10;

/** Length of the walkable route between two points (straight line when none is found). */
function routeLength(map: MapDef, from: Vec2, to: Vec2): number {
  const path = pathBetween(map, from, to);
  if (!path) return Math.hypot(to.x - from.x, to.y - from.y);
  let len = 0;
  let at = from;
  for (const p of path) {
    len += Math.hypot(p.x - at.x, p.y - at.y);
    at = p;
  }
  return len;
}

/** A bot that stands at `pos` facing `lookAt` until it sees an enemy (see DrillBot.hold). */
function holder(map: MapDef, n: number, pos: Vec2, archetype: ArchetypeId, lookAt: Vec2): DrillBot {
  return { ...bot(n, 'defense', pos, archetype, SHOWCASE_DIFFICULTY), hold: { pos, yaw: yawTo(pos, lookAt) } };
}

/**
 * The plantable point in `site` nearest to `from` by walking route: the point of the plant
 * zone closest to the player, nudged inside, then the best of a small grid around it. This
 * is where a late plant has to happen, so the clock is measured to it.
 */
export function nearestPlantablePoint(map: MapDef, site: SiteId, from: Vec2, seed: number): Vec2 {
  const poly = sitePoly(map, site);
  const centre = centroid(poly);
  let edge = poly[0];
  let edgeDist = Infinity;
  for (let i = 0; i < poly.length; i++) {
    const q = closestOnSegment(from, poly[i], poly[(i + 1) % poly.length]);
    if (q.dist < edgeDist) {
      edgeDist = q.dist;
      edge = q.point;
    }
  }
  const inLen = Math.hypot(centre.x - edge.x, centre.y - edge.y) || 1;
  const inside = { x: edge.x + ((centre.x - edge.x) / inLen) * PLANT_INSET_M, y: edge.y + ((centre.y - edge.y) / inLen) * PLANT_INSET_M };

  let best = walkablePoint(map, inside, seed);
  let bestLen = routeLength(map, from, best);
  for (let dx = -2; dx <= 2; dx++) {
    for (let dy = -2; dy <= 2; dy++) {
      const p = walkablePoint(map, { x: inside.x + dx * 0.5, y: inside.y + dy * 0.5 }, seed);
      if (!pointInPolygon(p, poly)) continue;
      const len = routeLength(map, from, p);
      if (len < bestLen - 1e-9) {
        best = p;
        bestLen = len;
      }
    }
  }
  return best;
}

/** Drill 1: the opening duel at North Causeway. */
function entryDrill(map: MapDef, seed: number): DrillSpec {
  const spawn = walkablePoint(map, ENTRY_SPOT.player, seed);
  const hold = walkablePoint(map, ENTRY_SPOT.hold, seed + 1);
  const mouth = walkablePoint(map, ENTRY_SPOT.mouth, seed + 2);
  return {
    id: 'showcase-entry-north-causeway',
    skill: 'entry',
    title: 'Rehearsal: opening duel at North Causeway',
    goal: 'Push up the alley and win the duel against the anchor holding its exit.',
    mapId: SHOWCASE_MAP_ID,
    seed,
    difficulty: SHOWCASE_DIFFICULTY,
    loadout: SHOWCASE_LOADOUT,
    player: { agent: 'rook', id: PLAYER_ID, team: 'attack', spawn, yaw: Math.PI / 2, hp: 100, carryingSpike: false },
    bots: [holder(map, 1, hold, 'off-angle-anchor', mouth)],
    roundTimeLeftS: 60,
    spikePlanted: null,
    objective: { label: 'HELD ANGLE', pos: hold },
  };
}

/** Drill 2: the 1v2 post-plant on B. */
function clutchDrill(map: MapDef, seed: number): DrillSpec {
  const spawn = walkablePoint(map, B_RETAKE_SPOT.player, seed);
  const spike = walkablePoint(map, B_RETAKE_SPOT.spike, seed + 1);
  const rear = walkablePoint(map, B_RETAKE_SPOT.rear, seed + 2);
  const front = walkablePoint(map, B_RETAKE_SPOT.front, seed + 3);
  const door = B_RETAKE_SPOT.door;
  return {
    id: 'showcase-clutch-b-retake',
    skill: 'clutch',
    title: 'Rehearsal: 1v2 post-plant on B',
    goal: 'The spike is down on B: hold it against the two defenders retaking.',
    mapId: SHOWCASE_MAP_ID,
    seed,
    difficulty: SHOWCASE_DIFFICULTY,
    loadout: SHOWCASE_LOADOUT,
    player: { agent: 'rook', id: PLAYER_ID, team: 'attack', spawn, yaw: yawTo(spawn, door), hp: 100, carryingSpike: false },
    // No hold: both defenders play their roles, which means pushing the planted spike.
    bots: [bot(1, 'defense', rear, 'patient-lurker', SHOWCASE_DIFFICULTY), bot(2, 'defense', front, 'entry-duelist', SHOWCASE_DIFFICULTY)],
    roundTimeLeftS: 30,
    spikePlanted: { site: 'B', pos: spike, secondsLeft: 30 },
    objective: { label: 'DEFEND THE SPIKE', pos: spike },
  };
}

/** Drill 3: the late plant on C. */
function timePressureDrill(map: MapDef, seed: number): DrillSpec {
  const spawn = walkablePoint(map, C_LATE_PLANT_SPOT.player, seed);
  const hold = walkablePoint(map, C_LATE_PLANT_SPOT.hold, seed + 1);
  const door = C_LATE_PLANT_SPOT.door;
  const plant = nearestPlantablePoint(map, 'C', spawn, seed + 2);
  // Enough clock to walk there, plant, and still have the margin: no more, no less.
  const left = r1(routeLength(map, spawn, plant) / RUN_SPEED + PLANT_TIME_S + SHOWCASE_MARGIN_S);
  return {
    id: 'showcase-time-pressure-c-late-plant',
    skill: 'time_pressure',
    title: 'Rehearsal: late plant on C',
    goal: 'Run through C Door and plant the spike before the clock runs out.',
    mapId: SHOWCASE_MAP_ID,
    seed,
    difficulty: SHOWCASE_DIFFICULTY,
    loadout: SHOWCASE_LOADOUT,
    player: { agent: 'rook', id: PLAYER_ID, team: 'attack', spawn, yaw: yawTo(spawn, door), hp: 100, carryingSpike: true },
    bots: [holder(map, 1, hold, 'off-angle-anchor', door)],
    roundTimeLeftS: left,
    spikePlanted: null,
    objective: { label: 'PLANT ON C', pos: plant },
  };
}

/** Drill 4: the smoke over Kiln Gap. */
function smokeDrill(map: MapDef, seed: number): DrillSpec {
  const spawn = walkablePoint(map, KILN_GAP_SPOT.player, seed);
  const hold = walkablePoint(map, KILN_GAP_SPOT.hold, seed + 1);
  const centre = walkablePoint(map, KILN_GAP_SPOT.centre, seed + 2);
  return {
    id: 'showcase-smoke-kiln-gap',
    skill: 'smoke_usage',
    title: 'Rehearsal: smoke Kiln Gap before you cross',
    goal: 'Smoke Kiln Gap, cross into South Causeway and take C site.',
    mapId: SHOWCASE_MAP_ID,
    seed,
    difficulty: SHOWCASE_DIFFICULTY,
    loadout: SHOWCASE_LOADOUT,
    // haze carries three smokes; the stock judge also forces one on smoke drills.
    player: { agent: 'haze', id: PLAYER_ID, team: 'attack', spawn, yaw: yawTo(spawn, centre), hp: 100, carryingSpike: true },
    bots: [holder(map, 1, hold, 'off-angle-anchor', centre)],
    roundTimeLeftS: 60,
    spikePlanted: null,
    objective: { label: 'SMOKE THE GAP', pos: centre },
  };
}

/** One showcase drill. Deterministic: the same spec every call. */
export function showcaseDrill(skill: Skill): DrillSpec {
  const map = loadMap(SHOWCASE_MAP_ID);
  const seed = SHOWCASE_SEEDS[skill];
  switch (skill) {
    case 'entry':
      return entryDrill(map, seed);
    case 'clutch':
      return clutchDrill(map, seed);
    case 'time_pressure':
      return timePressureDrill(map, seed);
    case 'smoke_usage':
      return smokeDrill(map, seed);
    default: {
      const never: never = skill;
      throw new Error(`no showcase drill for ${String(never)}`);
    }
  }
}

/** All four, in demo order. */
export function showcaseDrills(): DrillSpec[] {
  return SHOWCASE_SKILLS.map(showcaseDrill);
}

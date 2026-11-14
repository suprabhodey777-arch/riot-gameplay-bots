/**
 * Writes shared/maps/ironvale.json: an original two-site map with mid, 100 x 80 m,
 * laid out so every situation the coach trains has a place to happen.
 * Run from the repo root: node scripts/build-ironvale.ts
 *
 * Layout (x east, y north). Attackers start in the west strip, defenders in the east.
 *
 *   situation                     where
 *   long-range duel               A Long: a straight 48 m lane from spawn to A Main
 *   close corners                 Alleys: three staggered walls, every corner under 7 m
 *   smoke a choke                 A Main and B Main: 12 m and 6 m doorways into the sites
 *   mid control and rotations     Courtyard, with A Link and B Link to each site
 *   lurk and flank                Market: a corridor from mid to the defender spawn
 *   retake                        A Back and B Back: defender doors on the far side of each site
 *   off-angles and post-plant     pillars and boxes on each site around the default plant box
 *
 * Walls between the sites (the two warehouses) stop any sight line from one site
 * to the other, so a rotation is a real walk.
 */
import { writeFileSync } from 'node:fs';

type Rect = [number, number, number, number];
const walls: { id: string; points: { x: number; y: number }[]; height: number }[] = [];
const poly = ([x0, y0, x1, y1]: Rect) => [
  { x: x0, y: y0 },
  { x: x1, y: y0 },
  { x: x1, y: y1 },
  { x: x0, y: y1 },
];
const wall = (id: string, r: Rect, height: number): void => {
  walls.push({ id, points: poly(r), height });
};

// Outer boundary.
wall('outer-s', [0, 0, 100, 1], 4);
wall('outer-n', [0, 79, 100, 80], 4);
wall('outer-w', [0, 1, 1, 79], 4);
wall('outer-e', [99, 1, 100, 79], 4);

// Attacker spawn: cover either side of the spawn line.
wall('spawn-n', [1, 58, 12, 59.5], 4);
wall('spawn-s', [1, 20.5, 12, 22], 4);
wall('spawn-box', [14, 39, 16, 41], 1.2);

// Blocks that split the attacker side into three lanes.
wall('block-nw', [20, 50, 34, 64], 4);
wall('block-sw', [20, 18, 34, 30], 4);

// A Long: one cover box, otherwise open for the long duel.
wall('a-long-wall', [34, 62.5, 56, 64], 4);
wall('long-box', [42, 70, 44, 72], 1.2);

// Courtyard (mid): a tower in the middle forces a left or right choice.
wall('block-n', [34, 50, 56, 62.5], 4);
wall('block-s', [34, 16, 56, 30], 4);
wall('mid-tower', [44, 37, 50, 43], 4);
wall('mid-box-n', [38, 46, 40, 48], 1.2);
wall('mid-box-s', [56, 32, 58, 34], 1.2);

// Alleys (the B approach): staggered walls make a zigzag of close corners.
wall('alley-1', [28, 1, 30, 10], 4);
wall('alley-2', [36, 8, 38, 16], 4);
wall('alley-3', [44, 1, 46, 10], 4);
wall('alley-box', [50, 12, 52, 14], 1.2);

// A side: A Link's east wall, the A Main doorway and the site's back wall.
wall('a-west', [62, 50, 68, 62], 4);
wall('a-gate', [66, 74, 68, 79], 4);
wall('a-main-pillar', [66, 66, 67.5, 68], 3);
wall('a-east-1', [88, 50, 89.5, 56], 4);
wall('a-east-2', [88, 64, 89.5, 79], 4);

// B side: B Link's east wall, the narrow B Main doorway and the site's back wall.
wall('b-west', [62, 18, 68, 30], 4);
wall('b-gate-s', [66, 1, 68, 5], 4);
wall('b-gate-n', [66, 11, 68, 18], 4);
wall('b-east-1', [88, 1, 89.5, 16], 4);
wall('b-east-2', [88, 24, 89.5, 30], 4);

// The warehouses between the sites, with the Market corridor between them.
wall('warehouse-n', [64, 43, 88, 50], 4);
wall('warehouse-s', [64, 30, 88, 37], 4);
wall('market-box', [76, 39, 78, 41], 1.2);

// Cover on the sites: a default plant box, a pillar for an off-angle and a box.
wall('a-default', [77, 63, 80, 66], 1.2);
wall('a-pillar', [83, 69, 85, 71], 3);
wall('a-box', [73, 57, 75, 59], 1.2);
wall('b-default', [78, 13, 81, 16], 1.2);
wall('b-pillar', [74, 19, 76, 21], 3);
wall('b-box', [83, 7, 85, 9], 1.2);

// Defender spawn: one box so a spawn push is not a straight line.
wall('def-box', [92, 39, 94, 41], 1.2);

// Callouts: most specific first, since the first match names a point.
const callouts: { name: string; polygon: { x: number; y: number }[] }[] = (
  [
    ['A Main', [66, 62, 72, 74]],
    ['A Link', [56, 50, 62, 64]],
    ['A Back', [88, 56, 90, 64]],
    ['B Main', [62, 5, 68, 11]],
    ['B Link', [56, 16, 62, 30]],
    ['B Back', [88, 16, 90, 24]],
    ['Market', [64, 37, 89.5, 43]],
    ['A Long', [19.5, 64, 66, 79]],
    ['Alleys', [19.5, 1, 62, 18]],
    ['Mid Lane', [19.5, 30, 34, 50]],
    ['Courtyard', [34, 30, 64, 50]],
    ['A Yard', [68, 50, 88, 79]],
    ['B Yard', [68, 1, 88, 30]],
    ['Attacker Spawn', [1, 1, 19.5, 79]],
    ['Defender Spawn', [89.5, 1, 99, 79]],
  ] as [string, Rect][]
).map(([name, r]) => ({ name, polygon: poly(r) }));

const map = {
  id: 'ironvale',
  name: 'Ironvale',
  bounds: { width: 100, height: 80 },
  walls,
  sites: { A: poly([72, 55, 86, 75]), B: poly([72, 5, 86, 25]) },
  spawns: {
    attack: [34, 37, 40, 43, 46].map((y) => ({ x: 6, y })),
    defense: [34, 37, 40, 43, 46].map((y) => ({ x: 95, y })),
  },
  callouts,
};
writeFileSync('shared/maps/ironvale.json', `${JSON.stringify(map, null, 1)}\n`);
console.log(`wrote ironvale.json: ${walls.length} walls, ${callouts.length} callouts`);

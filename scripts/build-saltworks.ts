/**
 * Writes shared/maps/saltworks.json: an original three-site map, 110 x 80 m.
 * Everything is an axis-aligned rectangle so line of sight is easy to reason
 * about. Run from the repo root: node scripts/build-saltworks.ts
 *
 * Layout (x east, y north). The attackers start in the Dock on the west edge.
 * Three lanes (North Causeway, Crane Yard mid, South Causeway) cross a field of
 * buildings to a Forecourt, then a defender wall with three doors into three
 * site rooms (A north, B middle, C south). Walls between the rooms stop any
 * sight line from one site to another; a Back Hall behind them links the sites
 * for rotations and holds the defender spawn.
 */
import { writeFileSync } from 'node:fs';

type Rect = [number, number, number, number];
const walls: { id: string; points: { x: number; y: number }[]; height: number }[] = [];
const wall = (id: string, [x0, y0, x1, y1]: Rect, height: number): void => {
  walls.push({
    id,
    points: [
      { x: x0, y: y0 },
      { x: x1, y: y0 },
      { x: x1, y: y1 },
      { x: x0, y: y1 },
    ],
    height,
  });
};
const poly = ([x0, y0, x1, y1]: Rect) => [
  { x: x0, y: y0 },
  { x: x1, y: y0 },
  { x: x1, y: y1 },
  { x: x0, y: y1 },
];

// Outer boundary.
wall('outer-s', [0, 0, 110, 1], 4);
wall('outer-n', [0, 79, 110, 80], 4);
wall('outer-w', [0, 1, 1, 79], 4);
wall('outer-e', [109, 1, 110, 79], 4);

// The Dock (attacker spawn): closed on three sides, three ways out.
wall('dock-east-s', [20, 22, 21.5, 38], 4);
wall('dock-east-n', [20, 42, 21.5, 58], 4);
wall('dock-north', [1, 58, 14, 59.5], 4);
wall('dock-south', [1, 20.5, 14, 22], 4);
wall('dock-box-n', [10, 52, 12, 54], 1.2);
wall('dock-box-s', [10, 26, 12, 28], 1.2);

// Building rows either side of mid.
wall('saltstore-w', [26, 52, 38, 58], 4);
wall('saltstore-e', [48, 52, 64, 58], 4);
wall('kiln-w', [26, 22, 40, 28], 4);
wall('kiln-e', [50, 22, 64, 28], 4);

// Mid: the crane yard block, boxes and a pillar.
wall('crane-yard', [42, 35, 54, 45], 4);
wall('weighbridge-baffle', [68, 37.5, 70, 42.5], 4);
wall('mid-box-1', [30, 36, 32, 38], 1.2);
wall('mid-box-2', [60, 30, 62, 32], 1.2);
wall('mid-box-3', [60, 46, 62, 48], 1.2);
wall('mid-pillar', [34, 44, 36, 46], 3);

// North Causeway and Cinder Ramp.
wall('north-spine', [30, 66, 46, 67.5], 3);
wall('north-box-1', [52, 62, 54, 64], 1.2);
wall('north-box-2', [56, 72, 58, 74], 1.2);
wall('north-pillar', [36, 72, 38, 74], 3);
wall('ramp-block', [70, 72, 74, 76], 4);
wall('ramp-box', [66, 66, 68, 68], 1.2);

// South Causeway and Tide Steps.
wall('south-spine', [34, 12.5, 50, 14], 3);
wall('south-box-1', [54, 14, 56, 16], 1.2);
wall('south-box-2', [28, 6, 30, 8], 1.2);
wall('south-pillar', [44, 5, 46, 7], 3);
wall('steps-block', [70, 4, 74, 8], 4);
wall('steps-box', [66, 12, 68, 14], 1.2);

// Defender wall (front doors: A y62-70, B y36-44, C y10-18).
wall('front-1', [78, 1, 79.5, 10], 4);
wall('front-2', [78, 18, 79.5, 36], 4);
wall('front-3', [78, 44, 79.5, 62], 4);
wall('front-4', [78, 70, 79.5, 79], 4);
// Walls between the site rooms.
wall('between-b-c', [79.5, 26, 99, 27.5], 4);
wall('between-a-b', [79.5, 52, 99, 53.5], 4);
// Back wall (back doors: A y68-76, B y36-44, C y4-12).
wall('back-1', [98, 1, 99.5, 4], 4);
wall('back-2', [98, 12, 99.5, 36], 4);
wall('back-3', [98, 44, 99.5, 68], 4);
wall('back-4', [98, 76, 99.5, 79], 4);

// Cover in the rooms.
wall('a-pillar-1', [86.5, 68.5, 88.5, 70.5], 3);
wall('a-pillar-2', [84.5, 64, 86.5, 66], 3);
wall('a-box-1', [91, 62, 93.5, 64], 1.2);
wall('a-box-2', [92, 70, 94, 72], 1.2);
wall('b-pillar-1', [87, 37, 89, 39], 3);
wall('b-pillar-2', [92, 34.5, 94, 36.5], 3);
wall('b-box-1', [93, 41.5, 95.5, 43.5], 1.2);
wall('b-box-2', [90, 45, 92, 47], 1.2);
wall('c-pillar-1', [86, 16, 88, 18], 3);
wall('c-pillar-2', [84.5, 10, 86.5, 12], 3);
wall('c-box-1', [92, 9, 94.5, 11], 1.2);
wall('c-box-2', [91, 16, 93, 18], 1.2);

const callouts: { name: string; polygon: { x: number; y: number }[] }[] = (
  [
    ['A Door', [76, 60, 80, 72]],
    ['B Door', [76, 34, 80, 46]],
    ['C Door', [76, 8, 80, 20]],
    ['Salt Gap', [38, 52, 48, 59.5]],
    ['Kiln Gap', [40, 20.5, 50, 28]],
    ['Attacker Dock', [1, 22, 20, 58]],
    ['Crane Yard', [40, 30, 62, 50]],
    ['Mid Road', [21.5, 28, 40, 52]],
    ['Weighbridge', [62, 28, 78, 52]],
    ['Cinder Ramp', [62, 52, 78, 79]],
    ['Tide Steps', [62, 1, 78, 28]],
    ['North Causeway', [14, 59.5, 62, 79]],
    ['South Causeway', [14, 1, 62, 20.5]],
    ['A Pans', [79.5, 54, 98, 79]],
    ['B Silo Yard', [79.5, 28, 98, 52]],
    ['C Pump Deck', [79.5, 1, 98, 26]],
    ['North Hall', [98, 52, 109, 79]],
    ['Rear Court', [98, 28, 109, 52]],
    ['South Hall', [98, 1, 109, 28]],
  ] as [string, Rect][]
).map(([name, r]) => ({ name, polygon: poly(r) }));

const map = {
  id: 'saltworks',
  name: 'Saltworks',
  bounds: { width: 110, height: 80 },
  walls,
  sites: { A: poly([84, 60, 96, 74]), B: poly([85, 34, 96, 46]), C: poly([84, 8, 96, 20]) },
  spawns: {
    attack: [32, 36, 40, 44, 48].map((y) => ({ x: 4, y })),
    defense: [34, 37, 40, 43, 46].map((y) => ({ x: 104, y })),
  },
  callouts,
};
writeFileSync('shared/maps/saltworks.json', `${JSON.stringify(map, null, 1)}\n`);
console.log(`wrote saltworks.json: ${walls.length} walls, ${callouts.length} callouts`);

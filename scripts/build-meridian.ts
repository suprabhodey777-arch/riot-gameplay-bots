/**
 * Writes shared/maps/meridian.json: an original three-site map, 120 x 84 m.
 * Everything is an axis-aligned rectangle so line of sight is easy to reason
 * about. Run from the repo root: node scripts/build-meridian.ts
 *
 * Layout (x east, y north). The attackers start in the Dock on the west edge and
 * leave through one narrow door into a baffled plaza. From there the map splits
 * three ways: the North Lane runs to A Deck (north-east), the Dish Court feeds B
 * Platform through two doors (west and south), and the South Lane runs to C
 * Pumphouse (south-east). Relay Hall joins the Dish Court to the North Lane and
 * the narrow Culvert joins it to the South Lane. The sites are staggered, so no
 * lane looks into another. Behind them a defender spine on the east edge links
 * three rear doors through a Back Link and A Walk, and holds the defender spawn.
 * Every site is its own walled room: no site can see another site.
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
wall('outer-s', [0, 0, 120, 1], 4);
wall('outer-n', [0, 83, 120, 84], 4);
wall('outer-w', [0, 1, 1, 83], 4);
wall('outer-e', [119, 1, 120, 83], 4);

// The Dock (attacker spawn): closed, with one 4 m door east (y38-42).
wall('dock-n', [1, 52.5, 14, 54], 4);
wall('dock-s', [1, 30, 14, 31.5], 4);
wall('dock-e-n', [14, 42, 15.5, 52.5], 4);
wall('dock-e-s', [14, 31.5, 15.5, 38], 4);
wall('dock-box-n', [10.5, 46, 12.5, 48], 1.2);
wall('dock-box-s', [10.5, 32, 12.5, 34], 1.2);

// Baffle just inside the door: the only way on is around its north or south end.
wall('entry-baffle', [20, 34, 21.5, 46], 3);

// Dish Court: north edge with Relay Hall (gap x38-46), south edge with Culvert (gap x58-61).
wall('dish-north-w', [15.5, 56.5, 38, 58], 4);
wall('dish-north-e', [46, 56.5, 74, 58], 4);
wall('dish-south-w', [15.5, 20, 60, 21.5], 4);
wall('dish-south-e', [63, 20, 86, 21.5], 4);
// Cover across the south approach to B Platform.
wall('b-approach-cover', [66, 26, 74, 27.5], 3);
// Block between the Dish Court and B's west door, so that door is reached the long way.
wall('dish-east-block', [52, 40, 58, 50.5], 4);

// Dish Court cover: the Coolant Yard block plus scattered boxes and a pillar.
wall('coolant-block', [32, 32, 44, 46], 4);
wall('coolant-box-w', [26, 38, 28, 40], 1.2);
wall('coolant-box-e', [46, 40, 48, 42], 1.2);
wall('switch-box', [50, 44, 52, 46], 1.2);
wall('switch-pillar', [50, 28, 52, 30], 3);

// North Lane cover.
wall('north-spine', [24, 64, 40, 65.5], 3);
wall('north-box-1', [50, 62, 52, 64], 1.2);
wall('north-box-2', [60, 72, 62, 74], 1.2);
wall('north-pillar', [54, 68, 56, 70], 3);
wall('gantry-block', [64, 76, 72, 82], 4);

// South Lane cover.
wall('south-spine', [30, 10, 46, 11.5], 3);
wall('south-box-1', [50, 12, 52, 14], 1.2);
wall('south-box-2', [70, 4, 72, 6], 1.2);
wall('south-pillar', [60, 16, 62, 18], 3);
wall('south-block', [76, 2, 84, 8], 4);

// A Deck room: west door from the North Lane (y64-72), east door to A Walk (y66-74).
wall('a-front-s', [74, 58, 75.5, 64], 4);
wall('a-front-n', [74, 72, 75.5, 77.5], 4);
wall('a-south', [74, 56.5, 94, 58], 4);
wall('a-north', [74, 77.5, 94, 79], 4);
wall('a-east-s', [92.5, 58, 94, 66], 4);
wall('a-east-n', [92.5, 74, 94, 77.5], 4);
// A Deck cover.
wall('a-pillar-1', [79.5, 64, 81.5, 66], 3);
wall('a-pillar-2', [88, 71, 90, 73], 3);
wall('a-box-1', [84, 63, 86.5, 65], 1.2);
wall('a-box-2', [89, 64, 91, 66], 1.2);
wall('a-box-3', [79, 71, 81, 73], 1.2);
wall('a-box-4', [85, 74, 87, 76], 1.2);

// B Platform room: two entrances from the Dish Court (west y38-46, south x70-78).
wall('b-front-n', [64, 46, 65.5, 52], 4);
wall('b-front-s', [64, 32, 65.5, 38], 4);
wall('b-north', [64, 50.5, 84, 52], 4);
wall('b-south-w', [64, 32, 70, 33.5], 4);
wall('b-south-e', [78, 32, 84, 33.5], 4);
wall('b-east-n', [82.5, 47, 84, 52], 4);
wall('b-east-s', [82.5, 32, 84, 41], 4);
// B Platform cover.
wall('b-pillar-1', [69, 40, 71, 42], 3);
wall('b-pillar-2', [78, 44, 80, 46], 3);
wall('b-box-1', [73, 37, 75, 39], 1.2);
wall('b-box-2', [76, 38, 78, 40], 1.2);
wall('b-box-3', [70, 45, 72, 47], 1.2);

// C Pumphouse room: west door from the South Lane (y10-18), east door to the spine (y12-20).
wall('c-front-s', [86, 4, 87.5, 10], 4);
wall('c-front-n', [86, 18, 87.5, 24], 4);
wall('c-north', [86, 22.5, 106, 24], 4);
wall('c-south', [86, 4, 106, 5.5], 4);
wall('c-east-s', [104.5, 4, 106, 12], 4);
wall('c-east-n', [104.5, 20, 106, 24], 4);
// C Pumphouse cover.
wall('c-pillar-1', [91, 12, 93, 14], 3);
wall('c-pillar-2', [98, 16, 100, 18], 3);
wall('c-box-1', [95, 9, 97, 11], 1.2);
wall('c-box-2', [99, 11, 101, 13], 1.2);
wall('c-box-3', [91, 17, 93, 19], 1.2);

// Defender half: Switchyard building, Back Link behind B, Control Room, A Walk,
// and the 8 m spine (x106-114) along the east edge that ties them together.
wall('switchyard', [86, 24, 106, 38.5], 4);
wall('back-link-s', [84, 38.5, 106, 40], 4);
wall('back-link-n', [84, 48, 106, 49.5], 4);
wall('control-room', [94, 49.5, 106, 66], 4);
wall('a-walk-s', [94, 64.5, 106, 66], 4);
wall('a-walk-n', [94, 74, 106, 75.5], 4);
wall('spine-w-n', [104.5, 75.5, 106, 80], 4);
wall('spine-e', [114, 4, 115.5, 80], 4);
wall('spine-n', [106, 78.5, 114, 80], 4);
wall('spine-s', [106, 4, 114, 5.5], 4);
wall('spine-box-1', [110, 52, 112, 54], 1.2);
wall('spine-box-2', [109, 28, 111, 30], 1.2);

const callouts: { name: string; polygon: { x: number; y: number }[] }[] = (
  [
    ['Relay Hall', [37, 54, 47, 60]],
    ['Culvert', [59, 19, 64, 23]],
    ['Dock', [1, 30, 15.5, 54]],
    ['Rear Corridor', [106, 4, 114, 80]],
    ['Coolant Yard', [26, 32, 48, 50]],
    ['Gantry', [24, 60, 48, 68]],
    ['Antenna Walk', [48, 74, 74, 82]],
    ['A Walk', [94, 64, 106, 76]],
    ['Back Link', [84, 38, 106, 50]],
    ['A Deck', [74, 56.5, 94, 79]],
    ['B Platform', [64, 32, 84, 52]],
    ['C Pumphouse', [86, 4, 106, 24]],
    ['Switchyard', [84, 24, 106, 39]],
    ['Control Room', [94, 48, 106, 66]],
    ['Dish Court', [15.5, 21.5, 64, 56.5]],
    ['North Lane', [15.5, 58, 74, 82]],
    ['South Lane', [15.5, 1, 86, 20]],
    ['Array', [62, 1, 86, 20]],
  ] as [string, Rect][]
).map(([name, r]) => ({ name, polygon: poly(r) }));

const map = {
  id: 'meridian',
  name: 'Meridian',
  bounds: { width: 120, height: 84 },
  walls,
  sites: {
    A: poly([78, 62, 92, 76]),
    B: poly([68, 36, 82, 48]),
    C: poly([90, 8, 102, 20]),
  },
  spawns: {
    attack: [34, 37, 40, 43, 46].map((y) => ({ x: 5, y })),
    defense: [34, 37, 40, 43, 46].map((y) => ({ x: 111, y })),
  },
  callouts,
};
writeFileSync('shared/maps/meridian.json', `${JSON.stringify(map, null, 1)}\n`);
console.log(`wrote meridian.json: ${walls.length} walls, ${callouts.length} callouts`);

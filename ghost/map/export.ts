/**
 * Export the game's collision boxes to `ghost/map/colliders.json`.
 *
 * Run from the repo root: `node ghost/map/export.ts`.
 * `game/public/js/map.js` is browser code but has no DOM dependency: it only
 * needs `three`, so a fake scene object is enough to run `buildMap`.
 */
import { writeFileSync } from 'node:fs';

import type { Box, MapExport, SiteId, Vec2 } from '../contracts.ts';

interface Vec3Like { x: number; y: number; z: number }

interface GameMapModule {
  SITES: Record<SiteId, { x1: number; x2: number; z1: number; z2: number }>;
  colliders: { min: Vec3Like; max: Vec3Like }[];
  buildMap(scene: FakeScene): void;
}

/** The only parts of `THREE.Scene` that `buildMap` touches. */
interface FakeScene {
  background: unknown;
  fog: unknown;
  add(object: unknown): void;
}

// A computed specifier keeps TypeScript from trying to type a browser-only module.
const moduleUrl = new URL('../../game/public/js/map.js', import.meta.url).href;
const gameMap = (await import(moduleUrl)) as GameMapModule;

const scene: FakeScene = {
  background: null,
  fog: null,
  add() {
    // Geometry is irrelevant here; only `addCollider` side effects matter.
  },
};

gameMap.buildMap(scene);

const colliders: Box[] = gameMap.colliders.map((c) => ({
  min: { x: c.min.x, y: c.min.y, z: c.min.z },
  max: { x: c.max.x, y: c.max.y, z: c.max.z },
}));

// `pickSpawn` in game/server.js:70-75 places both teams on the centre line,
// attack at z=-26 and defense at z=+26, with up to ±3 m of jitter.
const spawns: Record<'attack' | 'defense', Vec2> = {
  attack: { x: 0, z: -26 },
  defense: { x: 0, z: 26 },
};

const mapExport: MapExport = { colliders, sites: gameMap.SITES, spawns };

const outPath = new URL('./colliders.json', import.meta.url);
writeFileSync(outPath, `${JSON.stringify(mapExport, null, 2)}\n`);

console.log(`wrote ${colliders.length} colliders to ${outPath.pathname}`);

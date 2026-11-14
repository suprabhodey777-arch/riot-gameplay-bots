import type { MapDef, PlayerInput, Vec2 } from '../shared/contract.ts';

export const rect = (x0: number, y0: number, x1: number, y1: number): Vec2[] => [
  { x: x0, y: y0 },
  { x: x1, y: y0 },
  { x: x1, y: y1 },
  { x: x0, y: y1 },
];

/** A 20 x 20 m arena for tests: attacker spawns at (2,10) inside site A, defender at (18,10). */
export function arena(opts: { walls?: MapDef['walls']; defenseAt?: Vec2 } = {}): MapDef {
  return {
    id: 'arena',
    name: 'Arena',
    bounds: { width: 20, height: 20 },
    walls: opts.walls ?? [],
    sites: { A: rect(0, 8, 5, 12), B: rect(15, 0, 20, 4) },
    spawns: { attack: [{ x: 2, y: 10 }], defense: [opts.defenseAt ?? { x: 18, y: 10 }] },
    callouts: [],
  };
}

export const wallBetween: MapDef['walls'] = [{ id: 'mid', points: rect(9, 0, 11, 20), height: 4 }];

export function input(agentId: string, o: Partial<PlayerInput> = {}): PlayerInput {
  return {
    agentId,
    move: { x: 0, y: 0 },
    yaw: 0,
    walk: false,
    crouch: false,
    fire: null,
    smokeAt: null,
    plantHeld: false,
    defuseHeld: false,
    ...o,
  };
}

export const duel = [
  { id: 'a', team: 'attack' as const, isBot: false },
  { id: 'd', team: 'defense' as const, isBot: true },
];

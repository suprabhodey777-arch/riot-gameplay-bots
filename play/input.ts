/** Keyboard and mouse state to a sim PlayerInput. Pure. Keys are `KeyboardEvent.code` values. */
import type { FireIntent, PlayerInput, Team, Vec2 } from '../shared/contract.ts';

/** Keys the page claims, so the browser does not scroll or search on them. */
export const GAME_KEYS = new Set([
  'KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyQ', 'KeyE', 'KeyC', 'KeyR', 'KeyX', 'KeyF',
  'ShiftLeft', 'ShiftRight', 'ControlLeft', 'ControlRight', 'Space', 'Escape',
]);

/** W is up on screen, which is +y in the world. Length at most 1. */
export function moveFromKeys(keys: ReadonlySet<string>): Vec2 {
  let x = 0;
  let y = 0;
  if (keys.has('KeyW')) y += 1;
  if (keys.has('KeyS')) y -= 1;
  if (keys.has('KeyD')) x += 1;
  if (keys.has('KeyA')) x -= 1;
  const len = Math.hypot(x, y);
  return len > 1 ? { x: x / len, y: y / len } : { x, y };
}

export const isWalk = (keys: ReadonlySet<string>): boolean => keys.has('ShiftLeft') || keys.has('ShiftRight');
export const isCrouch = (keys: ReadonlySet<string>): boolean =>
  keys.has('ControlLeft') || keys.has('ControlRight');

/** Aim angle from the player to the cursor, radians, 0 = +x, counter-clockwise. */
export const yawTo = (from: Vec2, to: Vec2): number => Math.atan2(to.y - from.y, to.x - from.x);

export interface InputFrame {
  agentId: string;
  team: Team;
  keys: ReadonlySet<string>;
  yaw: number;
  fire: FireIntent | null;
  smokeAt: Vec2 | null;
  flashAt?: Vec2 | null;
  reconAt?: Vec2 | null;
  dash?: boolean;
}

export function buildInput(f: InputFrame): PlayerInput {
  const held = f.keys.has('KeyF');
  return {
    agentId: f.agentId,
    move: moveFromKeys(f.keys),
    yaw: f.yaw,
    walk: isWalk(f.keys),
    crouch: isCrouch(f.keys),
    fire: f.fire,
    smokeAt: f.smokeAt,
    flashAt: f.flashAt, reconAt: f.reconAt, dash: f.dash,
    plantHeld: held && f.team === 'attack',
    defuseHeld: held && f.team === 'defense',
  };
}

/** Shots per second the player may fire, and the same as a gap in 20 Hz ticks. */
export const MAX_SHOTS_PER_S = 5;
export const SHOT_GAP_TICKS = 20 / MAX_SHOTS_PER_S;

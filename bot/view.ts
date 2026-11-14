import type { AgentState, MapDef, SimState, SmokeState, SpikeState, Vec2 } from '../shared/contract.ts';
import { lineOfSight } from '../sim/geometry.ts';

/** Horizontal field of view in radians (103 degrees). */
export const FOV_RAD = (103 * Math.PI) / 180;
/** Enemies this close are heard even behind a wall or behind the bot. */
export const HEARING_RANGE = 3;
/** An enemy lost for longer than this many ticks counts as newly seen when it comes back. */
export const REACQUIRE_TICKS = 6;

/** What the bot has seen so far: the only enemy information it keeps between ticks. */
export interface BotMemory {
  lastSeen: Record<string, { pos: Vec2; tick: number }>;
  /** Tick the current unbroken sighting of an enemy began. */
  visibleSince: Record<string, number>;
  lastVisible: Record<string, number>;
}

export const createMemory = (): BotMemory => ({ lastSeen: {}, visibleSince: {}, lastVisible: {} });

export interface SeenEnemy {
  id: string;
  pos: Vec2;
  /** True when there is a clear line to it (false: only heard through a wall). */
  los: boolean;
  /** Tick this sighting began. */
  since: number;
}

export interface Remembered {
  id: string;
  pos: Vec2;
  tick: number;
}

/** Everything a bot may use to decide. Enemy positions appear only in `visible` and `remembered`. */
export interface BotView {
  tick: number;
  roundTimeLeftS: number;
  phase: SimState['phase'];
  self: AgentState;
  /** Teammates (a team shares positions and health). */
  allies: AgentState[];
  visible: SeenEnemy[];
  remembered: Remembered[];
  /** How many enemies are alive (the kill feed is public). */
  enemiesAlive: number;
  smokes: SmokeState[];
  /** The spike as this team knows it: a planted spike is heard by all; carried or dropped is attack-only. */
  spike: SpikeState | null;
}

const angleDiff = (a: number, b: number): number => {
  let d = (a - b) % (2 * Math.PI);
  if (d > Math.PI) d -= 2 * Math.PI;
  if (d < -Math.PI) d += 2 * Math.PI;
  return Math.abs(d);
};

/** Fog of war: builds what `botId` is allowed to know, updating `memory` with what it sees now. */
export function botView(state: SimState, botId: string, map: MapDef, memory: BotMemory): BotView {
  const self = state.agents.find((a) => a.id === botId);
  if (!self) throw new Error(`Unknown agent ${botId}`);
  const visible: SeenEnemy[] = [];
  let enemiesAlive = 0;
  for (const e of state.agents) {
    if (e.team === self.team) continue;
    if (!e.alive) {
      delete memory.lastSeen[e.id];
      delete memory.visibleSince[e.id];
      delete memory.lastVisible[e.id];
      continue;
    }
    enemiesAlive++;
    if (!self.alive) continue;
    const dx = e.pos.x - self.pos.x;
    const dy = e.pos.y - self.pos.y;
    const heard = Math.hypot(dx, dy) <= HEARING_RANGE;
    const los = lineOfSight(self.pos, e.pos, map.walls, state.smokes);
    const inFov = angleDiff(Math.atan2(dy, dx), self.yaw) <= FOV_RAD / 2;
    if (!((los && inFov) || heard)) continue;
    const prev = memory.lastVisible[e.id];
    if (prev === undefined || state.tick - prev > REACQUIRE_TICKS) memory.visibleSince[e.id] = state.tick;
    memory.lastVisible[e.id] = state.tick;
    memory.lastSeen[e.id] = { pos: { x: e.pos.x, y: e.pos.y }, tick: state.tick };
    visible.push({ id: e.id, pos: { x: e.pos.x, y: e.pos.y }, los, since: memory.visibleSince[e.id] });
  }
  const seenNow = new Set(visible.map((v) => v.id));
  const remembered: Remembered[] = Object.entries(memory.lastSeen)
    .filter(([id]) => !seenNow.has(id))
    .map(([id, m]) => ({ id, pos: { ...m.pos }, tick: m.tick }));

  let spike: SpikeState | null = null;
  const sp = state.spike;
  if (sp.status === 'planted' || sp.status === 'defused' || sp.status === 'detonated') spike = sp;
  else if (self.team === 'attack') spike = sp;

  return {
    tick: state.tick,
    roundTimeLeftS: state.roundTimeLeftS,
    phase: state.phase,
    self,
    allies: state.agents.filter((a) => a.team === self.team && a.id !== self.id),
    visible,
    remembered,
    enemiesAlive,
    smokes: state.smokes,
    spike,
  };
}

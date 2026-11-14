/** Canvas drawing for the drill view. World is metres, x east, y north; the screen flips y. */
import type { AgentState, MapDef, SimState, SpikeState, Vec2 } from '../shared/contract.ts';
import { rayBlockDistance } from './ray.ts';

export const FOV_RAD = (103 * Math.PI) / 180;
export const HEARING_M = 3;
const CONE_RANGE_M = 30;
/** Walls this low or lower are cover, not walls, and are drawn lighter. */
export const LOW_WALL_M = 1.5;

export interface View {
  width: number;
  height: number;
  scale: number;
  ox: number;
  oy: number;
  mapH: number;
}

export function makeView(map: MapDef, width: number, height: number): View {
  const pad = 12;
  const scale = Math.max(1, Math.min((width - 2 * pad) / map.bounds.width, (height - 2 * pad) / map.bounds.height));
  return {
    width,
    height,
    scale,
    ox: (width - map.bounds.width * scale) / 2,
    oy: (height - map.bounds.height * scale) / 2,
    mapH: map.bounds.height,
  };
}

export const toScreen = (v: View, p: Vec2): Vec2 => ({ x: v.ox + p.x * v.scale, y: v.oy + (v.mapH - p.y) * v.scale });
export const toWorld = (v: View, sx: number, sy: number): Vec2 => ({ x: (sx - v.ox) / v.scale, y: v.mapH - (sy - v.oy) / v.scale });

export interface DrawAgent extends AgentState {
  /** Interpolated position and yaw. */
  pos: Vec2;
}

export interface Tracer {
  from: Vec2;
  to: Vec2;
  /** performance.now() at creation. */
  born: number;
  kind: 'player' | 'bot';
  hit: boolean;
}

export interface MarkerSeen {
  pos: Vec2;
  dead: boolean;
  /** Seconds of round clock when last seen, for the label. */
  atS: number;
}

export interface Scene {
  map: MapDef;
  state: SimState;
  agents: DrawAgent[];
  playerId: string;
  playerTeam: string;
  aimYaw: number;
  seen: Set<string>;
  lastSeen: Map<string, MarkerSeen>;
  tracers: Tracer[];
  now: number;
  flash: number;
  /** 0-1 ring progress for plant or defuse, and which. */
  hold: { progress: number; kind: 'plant' | 'defuse' } | null;
  cursor: Vec2 | null;
  /** Mark the whole map as visible (the player is dead). */
  revealAll: boolean;
}

const cen = (pts: Vec2[]): Vec2 => ({ x: pts.reduce((s, p) => s + p.x, 0) / pts.length, y: pts.reduce((s, p) => s + p.y, 0) / pts.length });
const SITE_COLORS = ['230,150,30', '40,150,200', '120,90,200'];

export function draw(ctx: CanvasRenderingContext2D, v: View, s: Scene): void {
  const { map } = s;
  ctx.clearRect(0, 0, v.width, v.height);
  const P = (p: Vec2): Vec2 => toScreen(v, p);
  const poly = (pts: Vec2[]): void => {
    ctx.beginPath();
    pts.forEach((p, i) => {
      const q = P(p);
      if (i) ctx.lineTo(q.x, q.y);
      else ctx.moveTo(q.x, q.y);
    });
    ctx.closePath();
  };
  const text = (t: string, p: Vec2, color: string, font: string, dy = 0): void => {
    const q = P(p);
    ctx.fillStyle = color;
    ctx.font = font;
    ctx.textAlign = 'center';
    ctx.fillText(t, q.x, q.y + dy);
  };

  // floor
  const tl = P({ x: 0, y: map.bounds.height });
  ctx.fillStyle = '#ffffff';
  ctx.fillRect(tl.x, tl.y, map.bounds.width * v.scale, map.bounds.height * v.scale);

  Object.entries(map.sites).forEach(([name, pts], i) => {
    const col = SITE_COLORS[i % SITE_COLORS.length];
    poly(pts);
    ctx.fillStyle = `rgba(${col},.2)`;
    ctx.fill();
    ctx.strokeStyle = `rgba(${col},.85)`;
    ctx.lineWidth = 1.5;
    ctx.stroke();
  });

  // The player's view: a cone cut off by walls and smokes, plus a short hearing circle.
  const me = s.agents.find((a) => a.id === s.playerId);
  if (me?.alive) {
    const o = P(me.pos);
    ctx.beginPath();
    ctx.moveTo(o.x, o.y);
    const steps = 90;
    for (let i = 0; i <= steps; i++) {
      const a = s.aimYaw - FOV_RAD / 2 + (FOV_RAD * i) / steps;
      const dir = { x: Math.cos(a), y: Math.sin(a) };
      const d = rayBlockDistance(me.pos, dir, map.walls, s.state.smokes, CONE_RANGE_M);
      const q = P({ x: me.pos.x + dir.x * d, y: me.pos.y + dir.y * d });
      ctx.lineTo(q.x, q.y);
    }
    ctx.closePath();
    ctx.fillStyle = 'rgba(40,100,230,.14)';
    ctx.fill();
    ctx.beginPath();
    ctx.arc(o.x, o.y, HEARING_M * v.scale, 0, Math.PI * 2);
    ctx.strokeStyle = 'rgba(40,100,230,.25)';
    ctx.setLineDash([3, 4]);
    ctx.lineWidth = 1;
    ctx.stroke();
    ctx.setLineDash([]);
  }

  // Full-height walls are near black; low boxes (cover you can shoot over) are a clearly lighter grey with an outline.
  for (const w of map.walls) {
    poly(w.points);
    if (w.height <= LOW_WALL_M) {
      ctx.fillStyle = '#a9b0bc';
      ctx.fill();
      ctx.strokeStyle = '#4b5260';
      ctx.lineWidth = 1.5;
      ctx.stroke();
      ctx.lineWidth = 1;
    } else {
      ctx.fillStyle = '#2b2f38';
      ctx.fill();
    }
  }

  // Place names sit above the walls, faint but readable, with a pale halo so a box behind them does not eat them.
  ctx.lineJoin = 'round';
  for (const c of map.callouts) {
    const q = P(cen(c.polygon));
    ctx.font = '12px system-ui';
    ctx.textAlign = 'center';
    ctx.lineWidth = 3;
    ctx.strokeStyle = 'rgba(255,255,255,.75)';
    ctx.strokeText(c.name, q.x, q.y);
    ctx.fillStyle = 'rgba(55,65,85,.6)';
    ctx.fillText(c.name, q.x, q.y);
  }
  ctx.lineWidth = 1;

  for (const sm of s.state.smokes) {
    const q = P(sm.pos);
    ctx.beginPath();
    ctx.arc(q.x, q.y, sm.radius * v.scale, 0, Math.PI * 2);
    ctx.fillStyle = 'rgba(130,130,145,.72)';
    ctx.fill();
    ctx.strokeStyle = 'rgba(90,90,105,.8)';
    ctx.stroke();
  }

  drawSpike(ctx, v, s.state.spike, s.agents);

  // last-seen markers for enemies out of sight
  for (const [id, m] of s.lastSeen) {
    if (s.seen.has(id) || s.revealAll) continue;
    const q = P(m.pos);
    ctx.globalAlpha = m.dead ? 0.7 : 0.4;
    ctx.strokeStyle = '#d62d2d';
    ctx.fillStyle = '#d62d2d';
    ctx.lineWidth = 2;
    if (m.dead) {
      ctx.beginPath();
      ctx.moveTo(q.x - 5, q.y - 5);
      ctx.lineTo(q.x + 5, q.y + 5);
      ctx.moveTo(q.x + 5, q.y - 5);
      ctx.lineTo(q.x - 5, q.y + 5);
      ctx.stroke();
    } else {
      ctx.setLineDash([3, 3]);
      ctx.beginPath();
      ctx.arc(q.x, q.y, 8, 0, Math.PI * 2);
      ctx.stroke();
      ctx.setLineDash([]);
      ctx.font = 'bold 12px system-ui';
      ctx.textAlign = 'center';
      ctx.fillText('?', q.x, q.y + 4);
    }
    ctx.globalAlpha = 1;
    ctx.font = '11px system-ui';
    ctx.textAlign = 'center';
    ctx.fillStyle = '#8a3a3a';
    ctx.fillText(`${id} last seen`, q.x, q.y + 21);
    ctx.lineWidth = 1;
  }

  // tracers
  for (const t of s.tracers) {
    const age = s.now - t.born;
    const life = t.kind === 'player' ? 220 : 160;
    if (age > life) continue;
    const a = P(t.from);
    const b = P(t.to);
    ctx.globalAlpha = 1 - age / life;
    ctx.strokeStyle = t.kind === 'player' ? (t.hit ? '#ff7a00' : '#e6a800') : '#d62d2d';
    ctx.lineWidth = t.hit ? 2.5 : 1.5;
    ctx.beginPath();
    ctx.moveTo(a.x, a.y);
    ctx.lineTo(b.x, b.y);
    ctx.stroke();
    if (t.hit) {
      ctx.beginPath();
      ctx.arc(b.x, b.y, 5, 0, Math.PI * 2);
      ctx.stroke();
    }
    ctx.globalAlpha = 1;
    ctx.lineWidth = 1;
  }

  // agents
  for (const a of s.agents) {
    const isMe = a.id === s.playerId;
    const enemy = a.team !== s.playerTeam;
    if (enemy && !s.seen.has(a.id) && !s.revealAll) continue;
    const q = P(a.pos);
    const col = isMe ? '#1f5fe0' : enemy ? '#d62d2d' : '#2a9d5c';
    if (a.alive) {
      ctx.beginPath();
      ctx.arc(q.x, q.y, 0.4 * v.scale + 2, 0, Math.PI * 2);
      ctx.fillStyle = col;
      ctx.fill();
      ctx.strokeStyle = '#fff';
      ctx.lineWidth = 1.5;
      ctx.stroke();
      // facing
      const yaw = isMe ? s.aimYaw : a.yaw;
      ctx.beginPath();
      ctx.moveTo(q.x, q.y);
      ctx.lineTo(q.x + Math.cos(yaw) * (0.4 * v.scale + 9), q.y - Math.sin(yaw) * (0.4 * v.scale + 9));
      ctx.strokeStyle = col;
      ctx.lineWidth = 3;
      ctx.stroke();
      ctx.lineWidth = 1;
      ctx.fillStyle = '#222';
      ctx.font = '11px system-ui';
      ctx.textAlign = 'center';
      ctx.fillText(isMe ? `you ${Math.round(a.hp)}` : `${a.id} ${Math.round(a.hp)}`, q.x, q.y + 0.4 * v.scale + 16);
    } else {
      ctx.strokeStyle = col;
      ctx.lineWidth = 3;
      ctx.beginPath();
      ctx.moveTo(q.x - 6, q.y - 6);
      ctx.lineTo(q.x + 6, q.y + 6);
      ctx.moveTo(q.x + 6, q.y - 6);
      ctx.lineTo(q.x - 6, q.y + 6);
      ctx.stroke();
      ctx.lineWidth = 1;
    }
  }

  // plant / defuse ring, and smoke target preview
  if (me?.alive && s.hold) {
    const q = P(me.pos);
    ctx.beginPath();
    ctx.arc(q.x, q.y, 20, 0, Math.PI * 2);
    ctx.strokeStyle = 'rgba(0,0,0,.18)';
    ctx.lineWidth = 5;
    ctx.stroke();
    ctx.beginPath();
    ctx.arc(q.x, q.y, 20, -Math.PI / 2, -Math.PI / 2 + Math.PI * 2 * s.hold.progress);
    ctx.strokeStyle = s.hold.kind === 'plant' ? '#e8a400' : '#1f9d55';
    ctx.stroke();
    ctx.lineWidth = 1;
    ctx.fillStyle = '#222';
    ctx.font = 'bold 12px system-ui';
    ctx.textAlign = 'center';
    ctx.fillText(s.hold.kind === 'plant' ? 'Planting' : 'Defusing', q.x, q.y - 28);
  }
  if (s.cursor && me?.alive) {
    const c = P(s.cursor);
    ctx.strokeStyle = 'rgba(20,30,50,.8)';
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.arc(c.x, c.y, 8, 0, Math.PI * 2);
    ctx.moveTo(c.x - 13, c.y);
    ctx.lineTo(c.x - 4, c.y);
    ctx.moveTo(c.x + 4, c.y);
    ctx.lineTo(c.x + 13, c.y);
    ctx.moveTo(c.x, c.y - 13);
    ctx.lineTo(c.x, c.y - 4);
    ctx.moveTo(c.x, c.y + 4);
    ctx.lineTo(c.x, c.y + 13);
    ctx.stroke();
    ctx.lineWidth = 1;
  }

  // Site labels go on last, over boxes, smokes and people.
  Object.entries(map.sites).forEach(([name, pts]) => {
    const q = P(cen(pts));
    ctx.font = 'bold 16px system-ui';
    ctx.textAlign = 'center';
    ctx.lineWidth = 4;
    ctx.strokeStyle = '#fff';
    ctx.strokeText(`Site ${name}`, q.x, q.y - 18);
    ctx.fillStyle = '#2b3340';
    ctx.fillText(`Site ${name}`, q.x, q.y - 18);
    ctx.lineWidth = 1;
  });

  if (s.flash > 0) {
    ctx.fillStyle = `rgba(214,45,45,${Math.min(0.35, s.flash * 0.35)})`;
    ctx.fillRect(0, 0, v.width, v.height);
  }
}

function drawSpike(ctx: CanvasRenderingContext2D, v: View, sp: SpikeState, agents: DrawAgent[]): void {
  let pos: Vec2 | null = null;
  let color = '#e8a400';
  if (sp.status === 'planted') {
    pos = sp.pos;
    color = '#d11';
  } else if (sp.status === 'dropped') pos = sp.pos;
  else if (sp.status === 'carried') {
    const c = agents.find((a) => a.id === sp.carrierId);
    if (c) {
      const q = toScreen(v, c.pos);
      ctx.fillStyle = color;
      ctx.fillRect(q.x + 5, q.y - 12, 8, 8);
    }
    return;
  }
  if (!pos) return;
  const q = toScreen(v, pos);
  ctx.fillStyle = color;
  ctx.fillRect(q.x - 6, q.y - 6, 12, 12);
  ctx.strokeStyle = '#fff';
  ctx.strokeRect(q.x - 6, q.y - 6, 12, 12);
  if (sp.status === 'planted') {
    ctx.beginPath();
    ctx.arc(q.x, q.y, 14, 0, Math.PI * 2);
    ctx.strokeStyle = 'rgba(209,17,17,.5)';
    ctx.stroke();
  }
}

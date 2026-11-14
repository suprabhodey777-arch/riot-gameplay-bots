/**
 * One playable drill: fixed 20 Hz sim steps driven from requestAnimationFrame,
 * the mouse and keyboard turned into PlayerInput, drawn with interpolation.
 * The sim and the bot decide every outcome; this file only aims, raycasts the
 * shot and draws.
 */
import type { DrillResult, DrillSpec } from '../shared/coaching.ts';
import type { PlayerInput, SimEvent, SimState, TraceEntry, Vec2 } from '../shared/contract.ts';
import { loadMap } from '../shared/maps/index.ts';
import { lineOfSight, pointInPolygon } from '../sim/geometry.ts';
import { DEFUSE_TIME_S, PLANT_TIME_S, TICK_RATE } from '../sim/sim.ts';
import { startDrill, type DrillRun } from '../session/drill-run.ts';
import { draw, makeView, toWorld, type DrawAgent, type MarkerSeen, type Tracer, type View } from './draw.ts';
import { GAME_KEYS, SHOT_GAP_TICKS, buildInput, yawTo } from './input.ts';
import { classifyShot } from './ray.ts';

const TICK_MS = 1000 / TICK_RATE;

export interface HudInfo {
  hp: number;
  alive: boolean;
  clockS: number;
  phase: SimState['phase'];
  spike: string;
  smokesLeft: number;
  planting: number | null;
  paused: boolean;
  started: boolean;
  /** Newest first. */
  mind: TraceEntry[];
}

export interface FinishInfo {
  result: DrillResult;
  initial: SimState;
  final: SimState;
}

export interface GameCallbacks {
  onHud(h: HudInfo): void;
  onFinish(f: FinishInfo): void;
  onRestart(): void;
  onPauseChange(paused: boolean, started: boolean): void;
}

export class Game {
  readonly spec: DrillSpec;
  private canvas: HTMLCanvasElement;
  private ctx: CanvasRenderingContext2D;
  private cb: GameCallbacks;
  private run: DrillRun;
  private map;
  private initial: SimState;
  private prev: SimState;
  private curr: SimState;
  private keys = new Set<string>();
  private mouse: Vec2 | null = null;
  private mouseDown = false;
  private fireQueued = false;
  private smokeQueued = false;
  private lastShotTick = -100;
  private plantTicks = 0;
  private aimYaw = 0;
  private tracers: Tracer[] = [];
  private lastSeen = new Map<string, MarkerSeen>();
  private seen = new Set<string>();
  private mind: TraceEntry[] = [];
  private mindKey = new Map<string, string>();
  private flash = 0;
  private acc = 0;
  private lastT = 0;
  private lastHud = 0;
  private raf = 0;
  private view: View;
  private paused = true;
  private started = false;
  private over = false;
  private observer: ResizeObserver;
  private cleanup: (() => void)[] = [];

  constructor(canvas: HTMLCanvasElement, spec: DrillSpec, cb: GameCallbacks) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d') as CanvasRenderingContext2D;
    this.spec = spec;
    this.cb = cb;
    this.map = loadMap(spec.mapId);
    this.run = startDrill(spec);
    this.initial = this.run.state();
    this.prev = this.initial;
    this.curr = this.initial;
    this.aimYaw = this.initial.agents.find((a) => a.id === spec.player.id)?.yaw ?? 0;
    this.view = makeView(this.map, 100, 100);
    this.observer = new ResizeObserver(() => this.resize());
    this.observer.observe(canvas.parentElement ?? canvas);
    this.bind();
    this.resize();
    this.raf = requestAnimationFrame(this.frame);
    this.cb.onPauseChange(true, false);
  }

  /** Starts the clock (from the "ready" overlay). */
  begin(): void {
    this.started = true;
    this.setPaused(false);
  }

  setPaused(p: boolean): void {
    if (this.over) return;
    this.paused = p;
    if (p) {
      this.keys.clear();
      this.mouseDown = false;
    }
    this.cb.onPauseChange(p, this.started);
  }

  destroy(): void {
    cancelAnimationFrame(this.raf);
    this.observer.disconnect();
    for (const f of this.cleanup) f();
    this.cleanup = [];
  }

  private on<K extends keyof WindowEventMap>(t: K, f: (e: WindowEventMap[K]) => void, target: Window | HTMLElement = window): void {
    (target as Window).addEventListener(t, f as EventListener);
    this.cleanup.push(() => (target as Window).removeEventListener(t, f as EventListener));
  }

  private bind(): void {
    this.on('keydown', (e) => {
      if (e.target instanceof HTMLElement && /^(INPUT|SELECT|TEXTAREA)$/.test(e.target.tagName)) return;
      if (e.code === 'Escape') {
        if (!e.repeat) this.setPaused(!this.paused);
        e.preventDefault();
        return;
      }
      if (e.code === 'KeyR' && !e.ctrlKey && !e.metaKey) {
        e.preventDefault();
        this.cb.onRestart();
        return;
      }
      if (this.paused) {
        if (!this.started && (e.code === 'Enter' || e.code === 'Space')) {
          e.preventDefault();
          this.begin();
        }
        return;
      }
      if (GAME_KEYS.has(e.code) && !e.metaKey) e.preventDefault();
      if (e.repeat) return;
      this.keys.add(e.code);
      if (e.code === 'KeyQ') this.smokeQueued = true;
    });
    this.on('keyup', (e) => {
      this.keys.delete(e.code);
    });
    this.on('blur', () => {
      if (this.started && !this.over) this.setPaused(true);
      this.keys.clear();
    });
    this.on('mousemove', (e) => this.setMouse(e));
    this.on('mousedown', (e) => {
      this.setMouse(e);
      if (e.button !== 0 || this.paused) return;
      if (e.target !== this.canvas) return;
      this.mouseDown = true;
      this.fireQueued = true;
    });
    this.on('mouseup', () => {
      this.mouseDown = false;
    });
    this.on('contextmenu', (e) => e.preventDefault(), this.canvas);
  }

  private setMouse(e: MouseEvent): void {
    const r = this.canvas.getBoundingClientRect();
    this.mouse = toWorld(this.view, e.clientX - r.left, e.clientY - r.top);
  }

  private resize(): void {
    const box = (this.canvas.parentElement ?? this.canvas).getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    const w = Math.max(200, Math.floor(box.width));
    const h = Math.max(200, Math.floor(box.height));
    const bw = Math.round(w * dpr);
    const bh = Math.round(h * dpr);
    // Assigning canvas.width or height empties the bitmap, even to the same value, so only do it on a real change.
    if (this.canvas.width !== bw || this.canvas.height !== bh) {
      this.canvas.style.width = `${w}px`;
      this.canvas.style.height = `${h}px`;
      this.canvas.width = bw;
      this.canvas.height = bh;
    }
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    this.view = makeView(this.map, w, h);
    // A resize empties the bitmap. ResizeObserver callbacks run after a frame's animation callbacks, so waiting
    // for the next frame paints one blank frame first (and nothing at all if frames are throttled).
    this.render();
  }

  private me(s: SimState) {
    return s.agents.find((a) => a.id === this.spec.player.id);
  }

  private frame = (t: number): void => {
    const dt = this.lastT ? Math.min(100, t - this.lastT) : 0;
    this.lastT = t;
    if (!this.paused && !this.over) {
      this.acc += dt;
      while (this.acc >= TICK_MS && !this.over) {
        this.acc -= TICK_MS;
        this.tick();
      }
    }
    this.flash = Math.max(0, this.flash - dt / 400);
    this.render();
    if (t - this.lastHud > 90) {
      this.lastHud = t;
      this.hud();
    }
    this.raf = requestAnimationFrame(this.frame);
  };

  private tick(): void {
    const spec = this.spec;
    const state = this.curr;
    const me = this.me(state);
    const walls = this.map.walls;
    if (me && this.mouse) this.aimYaw = yawTo(me.pos, this.mouse);
    else if (me) this.aimYaw = me.yaw;

    let fire: PlayerInput['fire'] = null;
    const now = performance.now();
    if (me?.alive && (this.fireQueued || this.mouseDown) && state.tick - this.lastShotTick >= SHOT_GAP_TICKS) {
      const enemies = state.agents.filter((a) => a.alive && a.team !== spec.player.team).map((a) => ({ id: a.id, pos: a.pos }));
      const shot = classifyShot(me.pos, this.aimYaw, enemies, walls, state.smokes);
      fire = shot.fire;
      this.lastShotTick = state.tick;
      this.fireQueued = false;
      this.tracers.push({
        from: { ...me.pos },
        to: { x: me.pos.x + Math.cos(this.aimYaw) * shot.endDistance, y: me.pos.y + Math.sin(this.aimYaw) * shot.endDistance },
        born: now,
        kind: 'player',
        hit: shot.hitId !== null,
      });
    }
    let smokeAt: Vec2 | null = null;
    if (this.smokeQueued) {
      this.smokeQueued = false;
      if (me?.alive && me.smokesLeft > 0 && this.mouse) {
        smokeAt = {
          x: Math.max(0, Math.min(this.map.bounds.width, this.mouse.x)),
          y: Math.max(0, Math.min(this.map.bounds.height, this.mouse.y)),
        };
      }
    }
    const input = buildInput({ agentId: spec.player.id, team: spec.player.team, keys: this.keys, yaw: this.aimYaw, fire, smokeAt });
    const r = this.run.step(input);
    this.prev = state;
    this.curr = r.state;
    this.afterStep(r.events, r.trace, state, now);
    if (r.done) this.finish();
  }

  private afterStep(events: SimEvent[], trace: TraceEntry[], before: SimState, now: number): void {
    const pid = this.spec.player.id;
    // plant progress, mirrored from the sim's rule so the ring matches
    const me = this.me(this.curr);
    const onSite = !!me && Object.values(this.map.sites).some((p) => pointInPolygon(me.pos, p));
    if (me?.alive && me.carryingSpike && onSite && this.keys.has('KeyE') && this.spec.player.team === 'attack') this.plantTicks++;
    else this.plantTicks = 0;
    for (const e of events) {
      if (e.type === 'damage' && e.victim === pid) this.flash = 1;
    }
    // bots' shots, from the inputs they fed the sim (misses are drawn off-target)
    const prevById = new Map(before.agents.map((a) => [a.id, a]));
    for (const inp of this.run.match.lastBotInputs()) {
      if (!inp.fire) continue;
      const shooter = prevById.get(inp.agentId);
      const target = prevById.get(inp.fire.targetId);
      if (!shooter || !target) continue;
      const hit = inp.fire.result !== 'miss';
      const ang = Math.atan2(target.pos.y - shooter.pos.y, target.pos.x - shooter.pos.x) + (hit ? 0 : 0.07);
      const d = Math.hypot(target.pos.x - shooter.pos.x, target.pos.y - shooter.pos.y);
      this.tracers.push({
        from: { ...shooter.pos },
        to: { x: shooter.pos.x + Math.cos(ang) * d, y: shooter.pos.y + Math.sin(ang) * d },
        born: now,
        kind: 'bot',
        hit,
      });
    }
    this.tracers = this.tracers.filter((t) => now - t.born < 300);
    for (const t of trace) {
      const k = `${t.node}|${t.reason}`;
      if (this.mindKey.get(t.botId) === k) continue;
      this.mindKey.set(t.botId, k);
      this.mind.unshift(t);
    }
    if (this.mind.length > 60) this.mind.length = 60;
  }

  private finish(): void {
    this.over = true;
    this.keys.clear();
    this.mouseDown = false;
    this.cb.onFinish({ result: this.run.finish(), initial: this.initial, final: this.curr });
  }

  private render(): void {
    const a = this.paused || this.over ? (this.over ? 1 : 0) : Math.min(1, this.acc / TICK_MS);
    const me = this.me(this.curr);
    const revealAll = !!me && (!me.alive || this.over);
    const prevById = new Map(this.prev.agents.map((p) => [p.id, p]));
    const agents: DrawAgent[] = this.curr.agents.map((c) => {
      const p = prevById.get(c.id) ?? c;
      return { ...c, pos: { x: p.pos.x + (c.pos.x - p.pos.x) * a, y: p.pos.y + (c.pos.y - p.pos.y) * a } };
    });
    const mePos = agents.find((x) => x.id === this.spec.player.id)?.pos;
    if (mePos && this.mouse && me?.alive && !this.paused) this.aimYaw = yawTo(mePos, this.mouse);
    this.seen.clear();
    if (me?.alive && mePos) {
      for (const e of agents) {
        if (e.team === this.spec.player.team) continue;
        const d = Math.hypot(e.pos.x - mePos.x, e.pos.y - mePos.y);
        const ang = Math.atan2(e.pos.y - mePos.y, e.pos.x - mePos.x);
        let diff = Math.abs(ang - this.aimYaw) % (Math.PI * 2);
        if (diff > Math.PI) diff = Math.PI * 2 - diff;
        const inView = d <= 3 || diff <= (103 * Math.PI) / 360;
        if (inView && lineOfSight(mePos, e.pos, this.map.walls, this.curr.smokes)) {
          this.seen.add(e.id);
          this.lastSeen.set(e.id, { pos: { ...e.pos }, dead: !e.alive, atS: this.curr.roundTimeLeftS });
        }
      }
    }
    let hold: { progress: number; kind: 'plant' | 'defuse' } | null = null;
    const sp = this.curr.spike;
    if (sp.status === 'planted') {
      if (sp.defuseProgressS > 0) hold = { progress: Math.min(1, sp.defuseProgressS / DEFUSE_TIME_S), kind: 'defuse' };
    } else if (this.plantTicks > 0) hold = { progress: Math.min(1, this.plantTicks / (PLANT_TIME_S * TICK_RATE)), kind: 'plant' };
    draw(this.ctx, this.view, {
      map: this.map,
      state: this.curr,
      agents,
      playerId: this.spec.player.id,
      playerTeam: this.spec.player.team,
      aimYaw: this.aimYaw,
      seen: this.seen,
      lastSeen: this.lastSeen,
      tracers: this.tracers,
      now: performance.now(),
      flash: this.flash,
      hold,
      cursor: this.mouse,
      revealAll,
    });
  }

  private hud(): void {
    const s = this.curr;
    const me = this.me(s);
    const sp = s.spike;
    let spike = '';
    if (sp.status === 'carried') spike = sp.carrierId === this.spec.player.id ? 'You carry the spike' : `${sp.carrierId} carries the spike`;
    else if (sp.status === 'dropped') spike = 'Spike dropped';
    else if (sp.status === 'planted') spike = `Planted on ${sp.site}, ${Math.max(0, s.roundTimeLeftS).toFixed(0)} s to detonation${sp.defuseProgressS > 0 ? `, defuse ${Math.round((sp.defuseProgressS / DEFUSE_TIME_S) * 100)}%` : ''}`;
    else if (sp.status === 'defused') spike = 'Spike defused';
    else spike = 'Spike detonated or not in play';
    this.cb.onHud({
      hp: me ? Math.round(me.hp) : 0,
      alive: !!me?.alive,
      clockS: s.roundTimeLeftS,
      phase: s.phase,
      spike,
      smokesLeft: me?.smokesLeft ?? 0,
      planting: this.plantTicks > 0 ? this.plantTicks / (PLANT_TIME_S * TICK_RATE) : null,
      paused: this.paused,
      started: this.started,
      mind: this.mind.slice(0, 14),
    });
  }
}

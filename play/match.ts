import { lineOfSight } from '../sim/geometry.ts';
import { MatchGame } from '../session/match-game.ts';
import type { MatchLog, SimState, Vec2 } from '../shared/contract.ts';
import { WEAPONS, equipped } from '../shared/weapons.ts';
import { loadMap } from '../shared/maps/index.ts';
import { draw, makeView, toWorld, type View } from './draw.ts';
import { buildInput, yawTo } from './input.ts';
import { classifyShot } from './ray.ts';
import { WeaponAim } from './weapon-aim.ts';

/** Local 2D client for the same match controller used by the bridge. */
export function playMatch(root: HTMLElement, onDone: (log: MatchLog) => void): void {
  const game = new MatchGame({ map: 'ironvale', agent: 'rook', matchId: `play-match-${Date.now()}`, seed: Date.now() % 9000 });
  const map = loadMap(game.mapId);
  root.innerHTML = '<section class="screen"><div class="wrap"><h1>Match</h1><p id="match-status"></p><div id="match-buy"></div><canvas id="match-cv" width="900" height="620"></canvas><p>WASD move · Shift walk · Ctrl crouch · mouse fire · right click scope · R reload · 1/2 switch · F plant/defuse · B buy menu · Enter ready</p><button id="match-exit">Leave match</button></div></section>';
  const canvas = root.querySelector('#match-cv') as HTMLCanvasElement, ctx = canvas.getContext('2d')!;
  const status = root.querySelector('#match-status') as HTMLElement, menu = root.querySelector('#match-buy') as HTMLElement;
  const keys = new Set<string>();
  const aim = new WeaponAim();
  let state: SimState | null = null, mouse: Vec2 | null = null, down = false, scoped = false, lastShot = -100, view: View = makeView(map, 900, 620), stopped = false;
  let last = performance.now(), accumulator = 0, showBuy = true;
  const refresh = () => {
    const buy = game.phase === 'buy';
    const me = state?.agents.find(a => a.id === game.playerId);
    status.textContent = `Round ${game.round} · ${game.score.player} : ${game.score.enemy} · ${game.side()} · ${buy ? `${game.buyPhase().roundType}, ${Math.ceil(game.timeS)}s, ${game.wallets[0].credits} credits` : `${me?.hp.toFixed(0)} HP · armour ${me?.armor.toFixed(0)} · ${me ? equipped(me) : ''} ${me ? me.ammo[equipped(me)] : ''} · ${state?.roundTimeLeftS.toFixed(0)}s`}`;
    menu.hidden = !buy || !showBuy;
  };
  const paintMenu = () => {
    menu.innerHTML = `${Object.entries(WEAPONS).map(([id, w]) => `<button data-buy="${id}">${w.name} ${w.price}</button>`).join('')}<button data-buy="light">Light 400</button><button data-buy="heavy">Heavy 1000</button><button data-ready>Ready</button><span id="buy-error"></span><p>${[game.wallets[0].primary, game.wallets[0].secondary, game.wallets[0].armor].filter(Boolean).map(id => `<button data-sell="${id}">Sell ${id}</button>`).join('')}</p>`;
    menu.querySelectorAll<HTMLButtonElement>('[data-buy],[data-sell]').forEach(button => button.onclick = () => {
      try { if (button.dataset.buy) game.buy(button.dataset.buy); else game.sell(button.dataset.sell!); paintMenu(); refresh(); }
      catch (error) { menu.querySelector('#buy-error')!.textContent = String(error); }
    });
    menu.querySelector<HTMLButtonElement>('[data-ready]')!.onclick = ready;
  };
  function ready() { if (game.phase !== 'buy') return; game.ready(); state = game.state(); lastShot = -100; refresh(); }
  const keydown = (e: KeyboardEvent) => {
    if (['KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyF', 'KeyR', 'Space', 'Enter'].includes(e.code)) e.preventDefault();
    keys.add(e.code);
    if (!e.repeat && e.code === 'KeyB') { showBuy = !showBuy; refresh(); }
    if (!e.repeat && e.code === 'Enter') ready();
  };
  const keyup = (e: KeyboardEvent) => keys.delete(e.code);
  const move = (e: MouseEvent) => { const r = canvas.getBoundingClientRect(); mouse = toWorld(view, (e.clientX - r.left) * 900 / r.width, (e.clientY - r.top) * 620 / r.height); };
  const mousedown = (e: MouseEvent) => { if (e.button === 0) down = true; if (e.button === 2) scoped = true; };
  const mouseup = () => { down = false; scoped = false; };
  const blur = () => { keys.clear(); down = false; };
  window.addEventListener('keydown', keydown); window.addEventListener('keyup', keyup); window.addEventListener('mouseup', mouseup); window.addEventListener('blur', blur);
  canvas.addEventListener('mousemove', move); canvas.addEventListener('mousedown', mousedown); canvas.oncontextmenu = e => e.preventDefault();
  function cleanup() { stopped = true; window.removeEventListener('keydown', keydown); window.removeEventListener('keyup', keyup); window.removeEventListener('mouseup', mouseup); window.removeEventListener('blur', blur); }
  root.querySelector<HTMLButtonElement>('#match-exit')!.onclick = () => { cleanup(); onDone(game.log()); };
  function frame(now: number) {
    if (stopped) return;
    accumulator += Math.min(100, now - last); last = now;
    while (accumulator >= 50 && !stopped) {
      accumulator -= 50;
      if (game.phase === 'buy') { if (game.advanceBuy(0.05)) { state = game.state(); lastShot = -100; } }
      else if (game.phase === 'live' && state) {
        const me = state.agents.find(a => a.id === game.playerId)!;
        const yaw = mouse ? yawTo(me.pos, mouse) : me.yaw;
        const input = buildInput({ agentId: me.id, team: me.team, keys, yaw, fire: null, smokeAt: keys.has('KeyC') ? mouse : null, flashAt: keys.has('KeyQ') ? mouse : null, dash: keys.has('KeyE'), reconAt: keys.has('KeyX') ? mouse : null });
        input.scoped = scoped;
        const id = equipped(me);
        if (down && me.alive && !me.reloadingUntilTick && me.ammo[id] && state.tick - lastShot >= Math.ceil(WEAPONS[id].fireIntervalS * 20 - 1e-9)) {
          const shotYaw = aim.shot(yaw, id, state.tick / 20, !!(input.move.x || input.move.y), input.walk, input.crouch, !!input.dash, scoped);
          input.fire = classifyShot(me.pos, shotYaw, state.agents.filter(a => a.team !== me.team && a.alive), map.walls, state.smokes).fire;
          lastShot = state.tick;
        }
        const messages = game.step(input);
        const step = messages.find(m => m.type === 'step'); if (step?.type === 'step') state = step.state;
        if (messages.some(m => m.type === 'buy_phase')) { showBuy = true; paintMenu(); }
        if (messages.some(m => m.type === 'match_end')) { cleanup(); onDone(game.log()); return; }
      }
    }
    refresh();
    if (state) {
      const me = state.agents.find(a => a.id === game.playerId)!;
      view = makeView(map, 900, 620);
      const kick = aim.cameraKick(state.tick / 20);
      draw(ctx, { ...view, ox: view.ox + kick.x * 3, oy: view.oy + kick.y * 3 }, { map, state, agents: state.agents, playerId: me.id, playerTeam: me.team, aimYaw: mouse ? yawTo(me.pos, mouse) : me.yaw, seen: new Set(state.agents.filter(a => a.team !== me.team && (a.revealedUntilTick > state!.tick || me.blindUntilTick <= state!.tick && lineOfSight(me.pos, a.pos, map.walls, state!.smokes))).map(a => a.id)), lastSeen: new Map(), tracers: [], now, flash: 0, hold: null, cursor: mouse, revealAll: !me.alive });
    }
    requestAnimationFrame(frame);
  }
  paintMenu(); refresh(); requestAnimationFrame(frame);
}

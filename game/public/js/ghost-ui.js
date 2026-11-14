// Ghost Rematch UI layer on top of the base game: the bot's mind panel, the
// "moments you lost" menu, the drill HUD (attempts, skill, retry, coach debrief)
// and spectator cameras. Message shapes: ghost/contracts.ts ("Rematch messages").

const ACK_MS = 3000;

const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
const zoneName = (id) => (id ? String(id).split('-').map((w) => (w.length <= 1 ? w.toUpperCase() : w[0].toUpperCase() + w.slice(1))).join(' ') : '?');
const clock = (t) => `${Math.floor(t / 60)}:${String(Math.floor(t % 60)).padStart(2, '0')}`;

/** Plain words for one contract Action. */
export function describeAction(a) {
  if (!a) return '—';
  switch (a.kind) {
    case 'hold': return 'Holding an angle';
    case 'peek_wide': return `Wide peek ${a.dir}`;
    case 'peek_jiggle': return `Jiggle peek ${a.dir}`;
    case 'fight': return `Fighting ${a.target}`;
    case 'push': return `Pushing ${zoneName(a.zone)}`;
    case 'retreat': return `Falling back to ${zoneName(a.zone)}`;
    case 'rotate': return `Rotating to ${zoneName(a.zone)}`;
    case 'use_ability': return `Using ${a.slot}`;
    case 'plant': return `Planting ${a.site}`;
    case 'defuse': return 'Defusing';
    case 'buy': return `Buying ${a.item}`;
    default: return a.kind;
  }
}

function el(tag, cls, html) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (html != null) e.innerHTML = html;
  return e;
}

/**
 * ctx: { THREE, scene, camera, gunGroup, players: () => Map, me: () => player|null,
 *        alive: () => boolean, socketId: () => string|undefined, releasePointer: () => void }
 */
export function createGhostUI(ctx) {
  let socket = null;
  let drill = null;            // DrillState while in a rematch
  let practiceOver = null;     // {winner, reason} after a drill attempt ends
  let menuOpen = false;
  let openMenuOnJoin = false;
  let lobbyOpen = false;
  let openLobbyOnJoin = false;
  let ghosts = [];             // latest ghost-list
  let lobbyBots = [];          // latest lobby-bots
  let lobbyBusy = false;       // an add-bot/remove-bot request is in flight
  let mindForced = null;       // null = automatic, true/false = toggled with T
  const traces = new Map();    // bot name -> { last: TraceEvent, history: [{t, label}] }

  // ---------- DOM ----------
  const mind = el('div', 'gr-panel gr-mind');
  const drillBar = el('div', 'gr-drillbar');
  const endCard = el('div', 'gr-card gr-end');
  const menu = el('div', 'gr-card gr-menu');
  const lobby = el('div', 'gr-card gr-lobby');
  const camTag = el('div', 'gr-camtag');
  const keysTag = el('div', 'gr-keys', '<b>T</b> bot mind · <b>M</b> rematch · <b>L</b> ghosts · <b>V</b> camera (when dead)');
  for (const n of [mind, drillBar, endCard, menu, lobby, camTag, keysTag]) document.body.appendChild(n);

  // Lobby: a second way in, straight to the rematch menu.
  const startBtn = document.getElementById('start-btn');
  if (startBtn) {
    const b = el('button', 'gr-lobby-btn', '↺ &nbsp;Rematch a moment I lost');
    b.type = 'button';
    b.onclick = () => { openMenuOnJoin = true; startBtn.click(); };
    startBtn.insertAdjacentElement('afterend', b);
    const g = el('button', 'gr-lobby-btn', '👻 &nbsp;Play with ghosts');
    g.type = 'button';
    g.onclick = () => { openLobbyOnJoin = true; startBtn.click(); };
    b.insertAdjacentElement('afterend', g);
  }

  // ---------- mind panel ----------
  function mindVisible() {
    if (mindForced !== null) return mindForced;
    return !!drill || !ctx.alive() || spectate.mode !== 'off';
  }

  function renderMind() {
    const show = mindVisible() && traces.size > 0;
    mind.style.display = show ? 'block' : 'none';
    if (!show) return;
    const rows = [];
    for (const [bot, { last, history }] of traces) {
      const badge = last.fallback
        ? `<span class="gr-badge warn">FALLBACK · default bot</span>`
        : last.ghostOf ? `<span class="gr-badge ghost">GHOST OF ${esc(last.ghostOf).toUpperCase()}</span>` : `<span class="gr-badge">DEFAULT BOT</span>`;
      const s = last.situation || {};
      const sees = s.enemyVisible && s.enemyRel
        ? `Sees an enemy ${s.enemyRel.dist.toFixed(0)} m away`
        : s.enemyLastSeenZone ? `Last saw an enemy at ${esc(zoneName(s.enemyLastSeenZone))}${s.sinceSeen != null ? `, ${s.sinceSeen.toFixed(0)} s ago` : ''}` : 'No enemy seen yet';
      const matched = last.matched
        ? `Copying ${esc(last.matched.player)} · round ${last.matched.round} at ${clock(last.matched.t)} <span class="gr-dim">(distance ${last.matchDistance != null ? last.matchDistance.toFixed(2) : '?'})</span>`
        : `<span class="gr-dim">No close recording${last.fallbackReason ? ` (${esc(last.fallbackReason)})` : ''}</span>`;
      const hist = history.slice(-5).reverse().map((h) => `<li><span class="gr-dim">${clock(h.t)}</span> ${esc(h.label)}</li>`).join('');
      rows.push(`<div class="gr-bot">
        <div class="gr-bot-head"><b>${esc(bot)}</b>${badge}<span class="gr-skill">skill ×${(last.skillScale ?? 1).toFixed(2)}</span></div>
        <div class="gr-doing">${esc(describeAction(last.action))}</div>
        <div class="gr-why">${esc(last.reason || '')}</div>
        <div class="gr-line">${matched}</div>
        <div class="gr-line">${sees}</div>
        ${hist ? `<ul class="gr-hist">${hist}</ul>` : ''}
      </div>`);
    }
    mind.innerHTML = `<div class="gr-title">BOT'S MIND <span class="gr-dim">what it saw, chose and why</span></div>${rows.join('')}`;
  }

  function onTrace(tr) {
    if (!tr || !tr.bot) return;
    const entry = traces.get(tr.bot) || { last: tr, history: [] };
    const label = describeAction(tr.action) + (tr.fallback ? ' (fallback)' : '');
    const prev = entry.history[entry.history.length - 1];
    if (!prev || prev.label !== label) entry.history.push({ t: tr.t, label });
    if (entry.history.length > 30) entry.history.shift();
    entry.last = tr;
    traces.set(tr.bot, entry);
  }

  // ---------- drill HUD ----------
  function renderDrillBar() {
    if (!drill) { drillBar.style.display = 'none'; return; }
    drillBar.style.display = 'block';
    drillBar.innerHTML = `<span class="gr-tag">REMATCH</span> vs the ghost of <b>${esc(drill.ghostOf)}</b>
      · attempt <b>${drill.attempt}</b>${drill.variation ? ' · <span class="gr-tag alt">VARIATION</span>' : ''}
      ${drill.beaten ? ' · <span class="gr-ok">beaten</span>' : ''}
      <div class="gr-goal">${esc(drill.goal)}</div>`;
  }

  function renderEndCard(debrief) {
    if (!drill || !practiceOver) { endCard.style.display = 'none'; return; }
    const me = ctx.me();
    const won = practiceOver.winner && me && practiceOver.winner === me.team;
    const coach = debrief
      ? `<div class="gr-coach"><div class="gr-coach-h">COACH <span class="gr-dim">${debrief.source === 'llm' ? 'written by the LLM, checked against the trace' : 'template (the LLM was slow, off or failed the check)'}</span></div><p>${esc(debrief.text)}</p></div>`
      : `<div class="gr-coach"><div class="gr-coach-h">COACH</div><p class="gr-dim">Reading the trace…</p></div>`;
    const scale = drill.skillScale.toFixed(2);
    endCard.innerHTML = `
      <h2 class="${won ? 'gr-ok' : 'gr-bad'}">${won ? 'YOU WON THE MOMENT' : 'THE GHOST WON'}</h2>
      <div class="gr-dim">${esc(practiceOver.reason)} · attempt ${drill.attempt} vs ${esc(drill.ghostOf)}</div>
      ${coach}
      <div class="gr-skillrow">
        <label>Ghost skill <input type="range" min="0.5" max="1.5" step="0.05" value="${scale}" id="gr-skill" ${drill.autoSkill ? 'disabled' : ''}></label>
        <span id="gr-skill-val">×${scale}</span>
        <label class="gr-check"><input type="checkbox" id="gr-auto" ${drill.autoSkill ? 'checked' : ''}> auto (aim for 50% wins)</label>
      </div>
      <div class="gr-actions">
        <button id="gr-retry" class="gr-primary">Retry  [F]</button>
        <button id="gr-vary" ${drill.beaten ? '' : 'disabled title="Beat the moment once to unlock variations"'}>Try a variation</button>
        <button id="gr-moments">All moments  [M]</button>
        <button id="gr-leave">Leave practice</button>
      </div>
      <div class="gr-error" id="gr-end-error"></div>`;
    endCard.style.display = 'block';
    endCard.querySelector('#gr-retry').onclick = () => startMoment(drill.momentId, false);
    endCard.querySelector('#gr-vary').onclick = () => startMoment(drill.momentId, true);
    endCard.querySelector('#gr-moments').onclick = () => openMenu();
    endCard.querySelector('#gr-leave').onclick = leavePractice;
    const slider = endCard.querySelector('#gr-skill');
    const auto = endCard.querySelector('#gr-auto');
    slider.oninput = () => { endCard.querySelector('#gr-skill-val').textContent = '×' + Number(slider.value).toFixed(2); };
    slider.onchange = () => setSkill(Number(slider.value));
    auto.onchange = () => setSkill(auto.checked ? null : Number(slider.value));
  }

  function showError(where, msg) {
    if (where === 'lobby') { lobbyStatus(msg, false); return; }
    const box = (where === 'menu' ? menu : endCard).querySelector('.gr-error');
    if (box) box.textContent = msg;
  }

  function lobbyStatus(msg, ok) {
    const box = lobby.querySelector('.gr-status');
    if (!box) return;
    box.textContent = msg;
    box.classList.toggle('gr-ok', !!ok);
  }

  function call(event, payload, onOk, where, onDone) {
    if (!socket) { showError(where, 'Join the server first.'); if (onDone) onDone(); return; }
    const args = payload === undefined ? [] : [payload];
    socket.timeout(ACK_MS).emit(event, ...args, (err, res) => {
      if (onDone) onDone();
      if (err) { showError(where, 'The server did not answer. Is the rematch backend running?'); return; }
      if (!res || res.ok === false) { showError(where, res?.error || 'The server refused the request.'); return; }
      onOk(res);
    });
  }

  function startMoment(momentId, variation) {
    call('rematch-start', { momentId, variation }, (res) => {
      drill = res.drill; practiceOver = null; lastDebrief = null;
      closeMenu(); renderDrillBar(); renderEndCard(null);
    }, menuOpen ? 'menu' : 'end');
  }

  function setSkill(value) {
    call('skill-scale', { value }, () => {}, 'end');
  }

  function leavePractice() {
    call('rematch-leave', undefined, () => {
      drill = null; practiceOver = null; renderDrillBar(); renderEndCard(null);
    }, 'end');
  }

  // ---------- moments menu ----------
  function openMenu() {
    menuOpen = true;
    ctx.releasePointer();
    menu.style.display = 'block';
    menu.innerHTML = `<h2>MOMENTS YOU LOST</h2><p class="gr-dim">Each death is saved. Pick one to replay it against the ghost of the player who beat you.</p><div class="gr-list gr-dim">Loading…</div><div class="gr-error"></div><div class="gr-actions"><button id="gr-close">Close  [M]</button></div>`;
    menu.querySelector('#gr-close').onclick = closeMenu;
    call('rematch-list', undefined, (res) => renderMoments(res.moments), 'menu');
  }

  function renderMoments(moments) {
    const list = menu.querySelector('.gr-list');
    if (!list) return;
    list.classList.remove('gr-dim');
    if (!moments.length) {
      list.innerHTML = `<div class="gr-empty">No lost moments yet. Play a round first: every death is saved here.</div>`;
      return;
    }
    list.innerHTML = moments.map((m) => `
      <div class="gr-moment ${m.beaten ? 'beaten' : ''}">
        <div><b>Round ${m.round}</b> <span class="gr-dim">at ${clock(m.t)}</span> · killed by <b>${esc(m.killer)}</b>${m.zone ? ` at ${esc(zoneName(m.zone))}` : ''} <span class="gr-dim">(${esc(m.weapon)})</span></div>
        <div class="gr-moment-r">
          <span class="${m.beaten ? 'gr-ok' : 'gr-dim'}">${m.beaten ? '✓ beaten' : 'not beaten'}${m.attempts ? ` · ${m.attempts} tries` : ''}</span>
          <button data-id="${esc(m.id)}" ${m.killerGhostReady ? '' : `disabled title="Not enough of ${esc(m.killer)}'s play is recorded yet"`}>${m.killerGhostReady ? 'Rematch' : 'Ghost not ready'}</button>
        </div>
      </div>`).join('');
    for (const b of list.querySelectorAll('button[data-id]')) b.onclick = () => startMoment(b.dataset.id, false);
  }

  function closeMenu() { menuOpen = false; menu.style.display = 'none'; }

  // ---------- ghost lobby ----------
  const teamWord = (t) => (t === 'attack' ? 'attackers' : 'defenders');
  const pct = (x) => `${Math.round(x * 100)}%`;

  function heldoutLine(h) {
    if (!h) return `<span class="gr-dim">Held-out: not scored yet</span>`;
    const base = h.baseline != null ? ` vs ${pct(h.baseline)} baseline` : ' (no baseline yet)';
    return `Held-out: <b>${pct(h.agreement)}</b>${base} <span class="gr-dim">· n=${h.n}</span>`;
  }

  function openLobby() {
    lobbyOpen = true;
    ctx.releasePointer();
    lobby.style.display = 'block';
    lobby.innerHTML = `<h2>GHOST LOBBY</h2>
      <p class="gr-dim">Fill either team with ghosts cloned from recorded players, up to five a side. Or add a default bot.</p>
      <div class="gr-list" id="gr-ghost-list"><span class="gr-dim">Loading…</span></div>
      <div class="gr-lobby-row">
        <span>Default bot</span>
        <button id="gr-bot-attack">Add to attackers</button>
        <button id="gr-bot-defense">Add to defenders</button>
      </div>
      <div class="gr-lobby-h">BOTS ON THIS SERVER</div>
      <div class="gr-list" id="gr-lobby-bots"><span class="gr-dim">Loading…</span></div>
      <div class="gr-status"></div>
      <div class="gr-actions"><button id="gr-lobby-close">Close  [L]</button></div>`;
    lobby.querySelector('#gr-lobby-close').onclick = closeLobby;
    lobby.querySelector('#gr-bot-attack').onclick = () => addBot(null, 'attack');
    lobby.querySelector('#gr-bot-defense').onclick = () => addBot(null, 'defense');
    ghosts = [];
    lobbyBots = [];
    refreshLobby();
  }

  function closeLobby() {
    lobbyOpen = false;
    lobby.style.display = 'none';
  }

  function refreshLobby() {
    call('ghost-list', undefined, (res) => { ghosts = res.ghosts || []; renderGhosts(); }, 'lobby');
    call('lobby-bots', undefined, (res) => { lobbyBots = res.bots || []; renderBots(); }, 'lobby');
  }

  function renderGhosts() {
    const list = lobby.querySelector('#gr-ghost-list');
    if (!list) return;
    if (!ghosts.length) {
      list.innerHTML = `<div class="gr-empty">No ghosts yet. Record a few rounds, then run: <code>node ghost/clone/build.ts</code></div>`;
      return;
    }
    const me = ctx.me();
    const mine = me ? me.team : null;
    const other = mine === 'attack' ? 'defense' : mine === 'defense' ? 'attack' : null;
    const dead = me ? '' : ' disabled title="Join a team first"';
    list.innerHTML = ghosts.map((g) => `
      <div class="gr-moment gr-ghost">
        <div>
          <div><b>${esc(g.player)}</b> <span class="gr-dim">· ${g.roundsRecorded} round${g.roundsRecorded === 1 ? '' : 's'} recorded</span></div>
          <div class="gr-style">${esc(g.style)}</div>
          <div class="gr-line">${heldoutLine(g.heldout)}</div>
        </div>
        <div class="gr-moment-r">
          <button data-add="mine" data-player="${esc(g.player)}"${dead}>Add to my team</button>
          <button data-add="other" data-player="${esc(g.player)}"${dead}>Add to enemy team</button>
        </div>
      </div>`).join('');
    for (const b of list.querySelectorAll('button[data-add]')) {
      b.onclick = () => addBot(b.dataset.player, b.dataset.add === 'mine' ? mine : other);
    }
  }

  function renderBots() {
    const list = lobby.querySelector('#gr-lobby-bots');
    if (!list) return;
    if (!lobbyBots.length) {
      list.innerHTML = `<div class="gr-empty">No bots on this server yet.</div>`;
      return;
    }
    list.innerHTML = lobbyBots.map((b) => `
      <div class="gr-moment">
        <div><b>${esc(b.name)}</b> <span class="gr-dim">· ${teamWord(b.team)}${b.ghostOf ? ` · ghost of ${esc(b.ghostOf)}` : ' · default bot'}</span></div>
        <div class="gr-moment-r"><button data-remove="${esc(b.name)}">Remove</button></div>
      </div>`).join('');
    for (const btn of list.querySelectorAll('button[data-remove]')) btn.onclick = () => removeBot(btn.dataset.remove);
  }

  function addBot(ghostOf, team) {
    if (lobbyBusy) return;
    if (!ctx.me()) { lobbyStatus('Join a team first, then add a bot.', false); return; }
    if (team !== 'attack' && team !== 'defense') { lobbyStatus('Pick attackers or defenders.', false); return; }
    lobbyBusy = true;
    lobbyStatus(`Adding ${ghostOf ? `the ghost of ${ghostOf}` : 'a default bot'} to the ${teamWord(team)}…`, true);
    call('add-bot', { ghostOf: ghostOf || null, team }, (res) => {
      lobbyStatus(`${res.name} joined the ${teamWord(team)}.`, true);
      refreshLobby();
    }, 'lobby', () => { lobbyBusy = false; });
  }

  function removeBot(name) {
    if (lobbyBusy) return;
    lobbyBusy = true;
    call('remove-bot', { name }, () => {
      lobbyStatus(`${name} removed.`, true);
      refreshLobby();
    }, 'lobby', () => { lobbyBusy = false; });
  }

  // ---------- spectator cameras (only while dead, so they never give a live player extra information) ----------
  const spectate = { mode: 'off', target: null, fly: { x: 0, y: 18, z: -30, yaw: Math.PI, pitch: -0.5 } };
  const MODES = ['off', 'follow', 'eyes', 'fly'];
  const flyKeys = {};

  function others() {
    const id = ctx.socketId();
    return [...ctx.players().values()].filter((p) => p.id !== id && p.alive);
  }
  function cycleTarget(step) {
    const list = others();
    if (!list.length) { spectate.target = null; return; }
    const i = list.findIndex((p) => p.id === spectate.target);
    spectate.target = list[(i + step + list.length) % list.length].id;
  }
  function cycleMode() {
    spectate.mode = MODES[(MODES.indexOf(spectate.mode) + 1) % MODES.length];
    if ((spectate.mode === 'follow' || spectate.mode === 'eyes') && !others().some((p) => p.id === spectate.target)) cycleTarget(1);
    renderCamTag();
  }
  function renderCamTag() {
    if (spectate.mode === 'off') { camTag.style.display = 'none'; return; }
    const p = ctx.players().get(spectate.target);
    const who = p ? `${esc(p.name)}${p.bot ? ' (bot)' : ''}` : 'nobody';
    const label = spectate.mode === 'follow' ? `FOLLOWING ${who}` : spectate.mode === 'eyes' ? `${who}'S EYES` : 'FREE CAMERA · WASD, mouse drag, Space/C up/down';
    camTag.innerHTML = `${label} <span class="gr-dim">· V mode · [ ] switch player</span>`;
    camTag.style.display = 'block';
  }

  let drag = null;
  addEventListener('mousedown', (e) => { if (spectate.mode === 'fly' && !ctx.alive()) drag = { x: e.clientX, y: e.clientY }; });
  addEventListener('mouseup', () => { drag = null; });
  addEventListener('mousemove', (e) => {
    if (!drag) return;
    spectate.fly.yaw -= (e.clientX - drag.x) * 0.004;
    spectate.fly.pitch = Math.max(-1.4, Math.min(1.4, spectate.fly.pitch - (e.clientY - drag.y) * 0.004));
    drag = { x: e.clientX, y: e.clientY };
  });

  /** Called every frame after the game set its own camera; overrides it while spectating. */
  function updateCamera(dt) {
    const cam = ctx.camera;
    if (ctx.alive() || spectate.mode === 'off') {
      if (spectate.mode !== 'off' && ctx.alive()) { spectate.mode = 'off'; renderCamTag(); }
      ctx.gunGroup.visible = true;
      return;
    }
    ctx.gunGroup.visible = false;
    cam.rotation.order = 'YXZ';
    if (spectate.mode === 'fly') {
      const f = spectate.fly, sp = 14 * dt;
      const fwd = (flyKeys.KeyW ? 1 : 0) - (flyKeys.KeyS ? 1 : 0), side = (flyKeys.KeyD ? 1 : 0) - (flyKeys.KeyA ? 1 : 0);
      f.x += (-Math.sin(f.yaw) * fwd + Math.cos(f.yaw) * side) * sp;
      f.z += (-Math.cos(f.yaw) * fwd - Math.sin(f.yaw) * side) * sp;
      f.y += ((flyKeys.Space ? 1 : 0) - (flyKeys.KeyC ? 1 : 0)) * sp;
      cam.position.set(f.x, f.y, f.z);
      cam.rotation.set(f.pitch, f.yaw, 0);
      return;
    }
    const p = ctx.players().get(spectate.target);
    if (!p || !p.alive) { cycleTarget(1); renderCamTag(); return; }
    const px = p.interp?.x ?? p.pos.x, pz = p.interp?.z ?? p.pos.z, py = p.pos.y || 0;
    if (spectate.mode === 'eyes') {
      cam.position.set(px, py + (p.crouch ? 1.15 : 1.62), pz);
      cam.rotation.set(p.pitch || 0, p.yaw || 0, 0);
      if (p.mesh) p.mesh.visible = false;
    } else {
      // Behind and above, looking where the player looks (yaw 0 faces -z, as in the game).
      const back = 4.2;
      cam.position.set(px + Math.sin(p.yaw || 0) * back, py + 2.6, pz + Math.cos(p.yaw || 0) * back);
      cam.lookAt(px, py + 1.4, pz);
    }
  }

  // ---------- keys ----------
  const typing = () => document.activeElement && document.activeElement.tagName === 'INPUT';
  addEventListener('keydown', (e) => {
    flyKeys[e.code] = true;
    if (typing() || !socket) return;
    if (e.code === 'KeyT') { mindForced = !mindVisible(); }
    if (e.code === 'KeyM') { menuOpen ? closeMenu() : openMenu(); }
    if (e.code === 'KeyL') { lobbyOpen ? closeLobby() : openLobby(); }
    if (e.code === 'KeyF' && drill && practiceOver) startMoment(drill.momentId, false);
    if (e.code === 'KeyV' && !ctx.alive()) cycleMode();
    if (e.code === 'BracketRight' && !ctx.alive()) { cycleTarget(1); renderCamTag(); }
    if (e.code === 'BracketLeft' && !ctx.alive()) { cycleTarget(-1); renderCamTag(); }
  });
  addEventListener('keyup', (e) => { flyKeys[e.code] = false; });

  // ---------- socket ----------
  let lastDebrief = null;
  function bind(s) {
    socket = s;
    s.on('joined', () => {
      if (openMenuOnJoin) { openMenuOnJoin = false; setTimeout(openMenu, 300); }
      if (openLobbyOnJoin) { openLobbyOnJoin = false; setTimeout(openLobby, 300); }
    });
    s.on('player-joined', () => { if (lobbyOpen) refreshLobby(); });
    s.on('player-left', () => { if (lobbyOpen) refreshLobby(); });
    s.on('trace', onTrace);
    s.on('drill-state', (d) => { drill = d; renderDrillBar(); if (practiceOver) renderEndCard(lastDebrief); });
    s.on('practice-end', (pe) => {
      if (!drill) return;
      practiceOver = pe; lastDebrief = null;
      ctx.releasePointer();
      renderEndCard(null);
    });
    s.on('debrief', (d) => {
      if (!drill || d.momentId !== drill.momentId) return;
      lastDebrief = d;
      renderEndCard(d);
    });
    s.on('round-start', (snap) => {
      if (!snap?.round?.practice && drill) { drill = null; practiceOver = null; renderDrillBar(); renderEndCard(null); }
      if (practiceOver) { practiceOver = null; renderEndCard(null); }
    });
  }

  setInterval(renderMind, 200);

  return { bind, updateCamera, isBusy: () => menuOpen || lobbyOpen || (!!drill && !!practiceOver) };
}

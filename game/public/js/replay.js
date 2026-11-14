// 3D replay of a recorded match log (ghost/contracts.ts LogEvent lines) in the game's own map.
// URL: replay.html?match=<matchId>[&seg=<n>][&follow=<name>][&cam=top|follow|eyes][&embed=1][&autoplay=1]
import * as THREE from 'three';
import { buildMap } from './map.js';
import { describeAction } from './ghost-ui.js';
import { buildCharacter } from './characters.js';

const q = new URLSearchParams(location.search);
const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
const clock = (t) => `${Math.floor(t / 60)}:${String(Math.floor(t % 60)).padStart(2, '0')}`;
if (q.get('embed') === '1') document.body.classList.add('embed');

// ---------- scene ----------
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.shadowMap.enabled = true;
$('view').appendChild(renderer.domElement);
const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(70, 1, 0.05, 400);
camera.rotation.order = 'YXZ';
buildMap(scene);

function resize() {
  const r = $('view').getBoundingClientRect();
  renderer.setSize(r.width, r.height);
  camera.aspect = r.width / Math.max(1, r.height);
  camera.updateProjectionMatrix();
}
addEventListener('resize', resize);

// The log has no agent per player, so every player is the neutral character in team colour.
function agentMesh(name, team) {
  const grp = buildCharacter(null, team, name);
  scene.add(grp);
  return grp;
}

// ---------- data ----------
/** A segment runs from one round-start or scenario load to the next. */
let segments = [];
let seg = null;
let meshes = new Map();   // name -> mesh
let fx = [];              // transient three objects for this segment
let t = 0, playing = false, speed = 1;
let camMode = q.get('cam') || 'top';
let follow = q.get('follow') || null;
const orbit = { yaw: 0.6, pitch: -0.9, dist: 62 };

function parseLog(text) {
  const events = [];
  for (const line of text.split('\n')) {
    if (!line.trim()) continue;
    try { events.push(JSON.parse(line)); } catch { /* a half-written last line is skipped and counted */ events.bad = (events.bad || 0) + 1; }
  }
  return events;
}

function buildSegments(events) {
  const out = [];
  let cur = null;
  for (const e of events) {
    if (e.type === 'round-start' || e.type === 'scenario') {
      cur = { round: e.round, label: e.type === 'scenario' ? `Round ${e.round} · rematch ${e.scenario?.id ?? ''}` : `Round ${e.round}`, events: [], teams: {} };
      if (e.type === 'round-start') cur.teams = { ...e.teams };
      if (e.type === 'scenario') for (const p of e.scenario?.players || []) cur.teams[p.name] = p.team;
      out.push(cur);
      continue;
    }
    if (!cur) { cur = { round: e.round, label: `Round ${e.round}`, events: [], teams: {} }; out.push(cur); }
    cur.events.push(e);
    if (e.type === 'move' && e.team && !cur.teams[e.player]) cur.teams[e.player] = e.team;
  }
  for (const s of out) {
    s.duration = s.events.reduce((m, e) => Math.max(m, e.t), 0);
    s.tracks = new Map();
    for (const e of s.events) if (e.type === 'move') {
      if (!s.tracks.has(e.player)) s.tracks.set(e.player, []);
      s.tracks.get(e.player).push(e);
    }
    s.deaths = new Map();
    for (const e of s.events) if (e.type === 'kill' && !s.deaths.has(e.victim)) s.deaths.set(e.victim, e.t);
  }
  return out.filter((s) => s.events.length > 0);
}

function sample(track, time) {
  if (!track.length || time < track[0].t) return null;
  let lo = 0, hi = track.length - 1;
  while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (track[mid].t <= time) lo = mid; else hi = mid - 1; }
  const a = track[lo], b = track[Math.min(lo + 1, track.length - 1)];
  const k = b.t > a.t ? Math.min(1, (time - a.t) / (b.t - a.t)) : 0;
  return {
    x: a.pos.x + (b.pos.x - a.pos.x) * k, y: a.pos.y + (b.pos.y - a.pos.y) * k, z: a.pos.z + (b.pos.z - a.pos.z) * k,
    yaw: a.yaw, pitch: a.pitch, crouch: a.crouch,
  };
}

function clearSegment() {
  for (const m of meshes.values()) scene.remove(m);
  for (const o of fx) scene.remove(o.obj);
  meshes = new Map(); fx = [];
}

function loadSegment(i) {
  clearSegment();
  seg = segments[i];
  $('seg').value = String(i);
  for (const name of seg.tracks.keys()) meshes.set(name, agentMesh(name, seg.teams[name] || 'attack'));
  // Kill markers, smokes and tracers become timed effects.
  for (const e of seg.events) {
    if (e.type === 'kill' && e.victimPos) {
      const m = new THREE.Mesh(new THREE.TorusGeometry(0.45, 0.07, 6, 18), new THREE.MeshBasicMaterial({ color: 0xff2222 }));
      m.rotation.x = Math.PI / 2; m.position.set(e.victimPos.x, 0.06, e.victimPos.z);
      fx.push({ obj: m, from: e.t, to: Infinity });
    } else if (e.type === 'ability' && e.ability === 'smoke' && e.at) {
      const m = new THREE.Mesh(new THREE.SphereGeometry(2.6, 18, 18), new THREE.MeshStandardMaterial({ color: 0x9aa0a6, transparent: true, opacity: 0.85 }));
      m.position.set(e.at[0], 1.6, e.at[1]);
      fx.push({ obj: m, from: e.t, to: e.t + 18 });
    } else if (e.type === 'tracer') {
      const o = new THREE.Vector3(...e.from), d = new THREE.Vector3(...e.dir);
      const line = new THREE.Line(new THREE.BufferGeometry().setFromPoints([o, o.clone().add(d.multiplyScalar(40))]),
        new THREE.LineBasicMaterial({ color: 0xffe08a }));
      fx.push({ obj: line, from: e.t, to: e.t + 0.15 });
    }
  }
  for (const o of fx) { o.obj.visible = false; scene.add(o.obj); }
  const names = [...seg.tracks.keys()];
  $('follow').innerHTML = names.map((n) => `<option ${n === follow ? 'selected' : ''}>${esc(n)}</option>`).join('');
  if (!names.includes(follow)) follow = names[0] || null;
  $('scrub').max = String(seg.duration);
  t = 0;
  draw();
}

// ---------- per-frame ----------
function draw() {
  if (!seg) return;
  for (const [name, mesh] of meshes) {
    const s = sample(seg.tracks.get(name), t);
    const dead = seg.deaths.has(name) && seg.deaths.get(name) <= t;
    mesh.visible = !!s && !dead && !(camMode === 'eyes' && name === follow);
    if (s) { mesh.position.set(s.x, s.y, s.z); mesh.rotation.y = -s.yaw + Math.PI; }
  }
  for (const o of fx) o.obj.visible = t >= o.from && t <= o.to;

  const target = follow && sample(seg.tracks.get(follow), t);
  if (camMode === 'eyes' && target) {
    camera.position.set(target.x, target.y + (target.crouch ? 1.15 : 1.62), target.z);
    camera.rotation.set(target.pitch || 0, target.yaw || 0, 0);
  } else if (camMode === 'follow' && target) {
    camera.position.set(target.x + Math.sin(target.yaw) * 4.2, target.y + 2.6, target.z + Math.cos(target.yaw) * 4.2);
    camera.lookAt(target.x, target.y + 1.4, target.z);
  } else {
    camera.position.set(Math.sin(orbit.yaw) * Math.cos(orbit.pitch) * orbit.dist, -Math.sin(orbit.pitch) * orbit.dist, Math.cos(orbit.yaw) * Math.cos(orbit.pitch) * orbit.dist);
    camera.lookAt(0, 0, 0);
  }

  $('scrub').value = String(t);
  $('time').textContent = `${clock(t)} / ${clock(seg.duration)}`;
  renderSidebar();
  renderer.render(scene, camera);
}

function renderSidebar() {
  const feed = seg.events.filter((e) => e.t <= t && (e.type === 'kill' || e.type === 'plant' || e.type === 'defuse' || e.type === 'round-end'));
  $('feed').innerHTML = feed.slice(-8).reverse().map((e) => {
    if (e.type === 'kill') return `<li><span class="dim">${clock(e.t)}</span> <b>${esc(e.killer)}</b> killed <b>${esc(e.victim)}</b>${e.headshot ? ' <span class="hs">HEAD</span>' : ''}</li>`;
    if (e.type === 'round-end') return `<li><span class="dim">${clock(e.t)}</span> ${esc(e.winner || 'nobody')} won · ${esc(e.reason)}</li>`;
    return `<li><span class="dim">${clock(e.t)}</span> ${esc(e.player)} ${e.type} ${esc(e.stage)}</li>`;
  }).join('') || '<li class="dim">Nothing yet</li>';
  const latest = new Map();
  for (const e of seg.events) if (e.type === 'trace' && e.t <= t) latest.set(e.trace.bot, e.trace);
  $('mind').innerHTML = latest.size ? [...latest.values()].map((tr) => `
    <div class="bot"><b>${esc(tr.bot)}</b> <span class="badge ${tr.fallback ? 'warn' : ''}">${tr.fallback ? 'FALLBACK' : tr.ghostOf ? 'ghost of ' + esc(tr.ghostOf) : 'default bot'}</span>
    <div class="doing">${esc(describeAction(tr.action))}</div><div class="dim">${esc(tr.reason)}</div></div>`).join('')
    : '<div class="dim">No bot traces in this round.</div>';
}

let last = performance.now();
function loop(now) {
  const dt = Math.min(0.1, (now - last) / 1000); last = now;
  if (playing && seg) {
    t = Math.min(seg.duration, t + dt * speed);
    if (t >= seg.duration) setPlaying(false);
  }
  if (seg) draw();
  requestAnimationFrame(loop);
}

function setPlaying(p) { playing = p; $('play').textContent = p ? 'Pause' : 'Play'; }

// ---------- controls ----------
$('play').onclick = () => { if (seg && t >= seg.duration) t = 0; setPlaying(!playing); };
$('speed').onchange = (e) => { speed = Number(e.target.value); };
$('scrub').oninput = (e) => { t = Number(e.target.value); draw(); };
$('seg').onchange = (e) => { setPlaying(false); loadSegment(Number(e.target.value)); };
$('cam').onchange = (e) => { camMode = e.target.value; draw(); };
$('follow').onchange = (e) => { follow = e.target.value; draw(); };
$('cam').value = camMode;
let drag = null;
renderer.domElement.addEventListener('mousedown', (e) => { drag = { x: e.clientX, y: e.clientY }; });
addEventListener('mouseup', () => { drag = null; });
addEventListener('mousemove', (e) => {
  if (!drag || camMode !== 'top') return;
  orbit.yaw -= (e.clientX - drag.x) * 0.005;
  orbit.pitch = Math.max(-1.5, Math.min(-0.15, orbit.pitch - (e.clientY - drag.y) * 0.005));
  drag = { x: e.clientX, y: e.clientY };
});
renderer.domElement.addEventListener('wheel', (e) => { orbit.dist = Math.max(15, Math.min(120, orbit.dist + e.deltaY * 0.05)); e.preventDefault(); }, { passive: false });
addEventListener('keydown', (e) => { if (e.code === 'Space') { e.preventDefault(); $('play').click(); } });
// The side-by-side page drives both replays at once.
addEventListener('message', (e) => {
  if (e.origin !== location.origin || !e.data || e.data.gr !== 'replay') return;
  if (e.data.cmd === 'play') { t = 0; setPlaying(true); }
  if (e.data.cmd === 'pause') setPlaying(false);
});

// ---------- start ----------
async function start() {
  resize();
  const match = q.get('match');
  if (!match) { showMessage('Add <code>?match=&lt;matchId&gt;</code> to the address. Match ids are the file names in <code>logs/</code>.'); return; }
  let text;
  try {
    const res = await fetch(`/logs/${encodeURIComponent(match)}.jsonl`);
    if (!res.ok) { showMessage(`No log for match <b>${esc(match)}</b> (HTTP ${res.status}).`); return; }
    text = await res.text();
  } catch (err) { showMessage(`Could not load the log: ${esc(err.message)}`); return; }
  const events = parseLog(text);
  segments = buildSegments(events);
  if (!segments.length) { showMessage(`Match <b>${esc(match)}</b> has no recorded rounds yet.`); return; }
  $('title').textContent = `Replay · ${match}`;
  $('seg').innerHTML = segments.map((s, i) => `<option value="${i}">${esc(s.label)}</option>`).join('');
  if (events.bad) $('warn').textContent = `${events.bad} unreadable line(s) skipped.`;
  loadSegment(Math.min(segments.length - 1, Math.max(0, Number(q.get('seg') || 0))));
  if (q.get('autoplay') === '1') setPlaying(true);
  requestAnimationFrame(loop);
}
function showMessage(html) { $('msg').innerHTML = html; $('msg').style.display = 'block'; }
start();

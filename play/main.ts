/** Screens: start (pick a history, see the weakness report), drill (play), result (debrief). */
import type { DrillResult, DrillSpec, Skill, WeaknessReport } from '../shared/coaching.ts';
import type { MatchLog } from '../shared/contract.ts';
import { analyze } from '../analyzer/analyze.ts';
import { sampleHistory } from '../analyzer/synthetic.ts';
import { createRecorder } from '../session/recorder.ts';
import { CoachingSession } from '../session/session.ts';
import { Game, type FinishInfo, type HudInfo } from './game.ts';
import { narrate } from './narrate.ts';

const $app = document.getElementById('app') as HTMLElement;
const esc = (s: unknown): string => String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c] as string);
const PLAYER = 'player';

interface Source {
  id: string;
  label: string;
  skill?: Skill;
  seed: number;
}
const SOURCES: Source[] = [
  { id: 'entry', label: 'Sample player: dies in opening duels (weak entry)', skill: 'entry', seed: 11 },
  { id: 'clutch', label: 'Sample player: loses 1vX situations (weak clutch)', skill: 'clutch', seed: 12 },
  { id: 'time_pressure', label: 'Sample player: slow plants and defuses (time pressure)', skill: 'time_pressure', seed: 13 },
  { id: 'smoke_usage', label: 'Sample player: smokes too late or off target (smokes)', skill: 'smoke_usage', seed: 14 },
  { id: 'own', label: 'My own rounds (starts empty, grows as you play)', seed: 7 },
];

// ---- my own rounds, kept across page loads ------------------------------------------
const STORE = 'drill-practice.ownLogs.v1';
let ownLogs: MatchLog[] = [];
try {
  ownLogs = JSON.parse(localStorage.getItem(STORE) ?? '[]') as MatchLog[];
} catch {
  ownLogs = [];
}
const saveOwn = (): void => {
  try {
    localStorage.setItem(STORE, JSON.stringify(ownLogs.slice(-60)));
  } catch {
    // storage full or blocked: the history lives for this page only
  }
};
const ownRounds = (): number => ownLogs.reduce((n, l) => n + l.rounds.length, 0);

// ---- app state ------------------------------------------------------------------------
let source: Source = SOURCES[0];
let session: CoachingSession = makeSession(source);
let game: Game | null = null;
let currentSpec: DrillSpec | null = null;
let queue: DrillSpec[] = [];
let lastResult: { spec: DrillSpec; info: FinishInfo } | null = null;

function makeSession(src: Source): CoachingSession {
  if (src.skill) {
    const logs = sampleHistory(src.skill, src.seed);
    return CoachingSession.fromHistory(logs, 'me', { seed: src.seed, apiKey: null });
  }
  return CoachingSession.fromHistory(ownLogs, PLAYER, { seed: src.seed, apiKey: null });
}

type Rehearsable = { rehearsals?: () => unknown };
const hasRehearsals = (): boolean => typeof (session as unknown as Rehearsable).rehearsals === 'function';

/** The other agent's `rehearsals()` may return specs or wrappers around them; take whatever has a drill. */
function readRehearsals(): DrillSpec[] {
  const raw = (session as unknown as Rehearsable).rehearsals?.();
  if (!Array.isArray(raw)) return [];
  return raw
    .map((r) => (r && typeof r === 'object' && 'drill' in r ? (r as { drill: DrillSpec }).drill : r && typeof r === 'object' && 'spec' in r ? (r as { spec: DrillSpec }).spec : r) as DrillSpec)
    .filter((d) => d && typeof d === 'object' && 'bots' in d);
}

// ---- start screen ------------------------------------------------------------------------
const sev = (s: number): string => (s >= 0.6 ? '#c0262d' : s >= 0.3 ? '#d98a00' : '#4a8f4a');

function reportHtml(r: WeaknessReport): string {
  if (r.matchesRead === 0) {
    return `<p class="muted">No rounds read yet. Play a few drills, then choose "Re-analyze my play" and the practice follows what you did.</p>`;
  }
  const rows = r.findings
    .slice(0, 4)
    .map((f) => {
      const e = f.evidence[0];
      return `<div class="finding" style="border-color:${sev(f.severity)}">
        <b>${esc(f.skill.replace('_', ' '))}</b>, severity ${f.severity.toFixed(2)}
        <span class="muted">(your rate ${(f.rate * 100).toFixed(0)}% against ${(f.benchmark * 100).toFixed(0)}% for a ${esc(r.role)}, from ${f.sampleSize} situations)</span>
        <div class="bar"><i style="width:${Math.round(f.severity * 100)}%;background:${sev(f.severity)}"></i></div>
        <small>${esc(f.summary)}</small>
        ${e ? `<small>Evidence: round ${e.round}, tick ${e.tick}: ${esc(e.note)}</small>` : ''}
      </div>`;
    })
    .join('');
  return `<p class="muted">Read ${r.matchesRead} matches.</p>${rows}`;
}

function showStart(): void {
  game?.destroy();
  game = null;
  $app.innerHTML = `<section id="start" class="screen"><div class="wrap">
    <h1>Drill practice</h1>
    <p class="muted">The bot reads your history, finds your weakest situation and recreates it. You play it here, top down.</p>
    <div class="card">
      <h2>Whose history?</h2>
      <div class="row">
        <select id="src" aria-label="Player history">${SOURCES.map((s) => `<option value="${s.id}" ${s.id === source.id ? 'selected' : ''}>${esc(s.label)}</option>`).join('')}</select>
        <span class="muted" id="ownInfo"></span>
      </div>
      <h2>Weakness report</h2>
      <div id="report"></div>
      <div class="row">
        <button class="primary" id="go">Start practice</button>
        <button id="rehearse" hidden>Rehearse my mistakes</button>
        <button id="reanalyze" hidden>Re-analyze my play</button>
        <button id="clear" hidden>Clear my rounds</button>
      </div>
    </div>
  </div></section>`;
  const $ = <T extends HTMLElement>(id: string): T => document.getElementById(id) as T;
  const refresh = (): void => {
    $('report').innerHTML = reportHtml(session.report);
    const own = source.id === 'own';
    $('ownInfo').textContent = own ? `${ownRounds()} rounds recorded from your drills` : '';
    $('reanalyze').hidden = !own;
    $('clear').hidden = !own || ownRounds() === 0;
    // Only offer rehearsals when there is a credible weakness with mistakes to rebuild.
    $('rehearse').hidden = !hasRehearsals() || readRehearsals().length === 0;
  };
  $<HTMLSelectElement>('src').onchange = (e) => {
    source = SOURCES.find((s) => s.id === (e.target as HTMLSelectElement).value) ?? SOURCES[0];
    session = makeSession(source);
    queue = [];
    refresh();
  };
  $('go').onclick = () => {
    queue = [];
    playSpec(session.nextDrill().drill);
  };
  $('rehearse').onclick = () => {
    try {
      queue = readRehearsals();
    } catch (err) {
      queue = [];
      console.warn('rehearsals() failed', err);
    }
    const first = queue.shift();
    if (first) playSpec(first);
    else alert('No rehearsal drills are available for this history yet.');
  };
  $('reanalyze').onclick = () => {
    reanalyze();
    refresh();
  };
  $('clear').onclick = () => {
    ownLogs = [];
    saveOwn();
    session = makeSession(source);
    refresh();
  };
  refresh();
}

/** Reads the recorded rounds again and keeps the drill progress (staircases) of the running session. */
function reanalyze(): void {
  if (ownRounds() === 0) return;
  const report = analyze(ownLogs, PLAYER);
  if (report.findings.length === 0) return;
  const old = session.toJSON();
  session = CoachingSession.fromJSON({ ...old, report }, { apiKey: null });
}

// ---- drill screen ------------------------------------------------------------------------------
const KEYS: [string, string][] = [
  ['W A S D', 'Move (W is up on screen)'],
  ['Mouse', 'Aim'],
  ['Left click', 'Fire (5 shots a second at most)'],
  ['Shift', 'Walk (quiet, accurate)'],
  ['Ctrl or C', 'Crouch'],
  ['Q', 'Throw a smoke at the cursor'],
  ['Hold E', 'Plant (attack, on a site) or defuse (defense, at the spike)'],
  ['Esc', 'Pause'],
  ['R', 'Restart this drill'],
];

function playSpec(spec: DrillSpec): void {
  game?.destroy();
  currentSpec = spec;
  $app.innerHTML = `<section id="drill" class="screen">
    <div id="stage"><div id="cvwrap"><canvas id="cv"></canvas></div><div id="overlay"></div></div>
    <aside id="side">
      <h2>Drill</h2>
      <div><b id="title"></b> <span class="muted" id="skill"></span></div>
      <div id="goal" style="margin-top:4px"></div>
      <h2>You</h2>
      <div class="hp"><i id="hp" style="width:100%"></i></div>
      <div class="stats">
        <span class="muted">Health</span><b id="hpn"></b>
        <span class="muted">Round clock</span><b id="clock"></b>
        <span class="muted">Spike</span><b id="spike"></b>
        <span class="muted">Smokes left</span><b id="smokes"></b>
      </div>
      <h2>The bot's mind</h2>
      <div id="mind"></div>
      <h2>Keys</h2>
      <div class="keys">${KEYS.map(([k, d]) => `<kbd>${k}</kbd><span>${d}</span>`).join('')}</div>
      <div class="row"><button id="pause">Pause</button><button id="restart">Restart</button></div>
    </aside></section>`;
  const el = (id: string): HTMLElement => document.getElementById(id) as HTMLElement;
  el('title').textContent = spec.title;
  el('skill').textContent = `${spec.skill.replace('_', ' ')}, difficulty ${spec.difficulty.toFixed(2)}`;
  el('goal').textContent = spec.goal;
  const overlay = el('overlay');
  const g = new Game(el('cv') as HTMLCanvasElement, spec, {
    onHud: (h) => paintHud(h),
    onFinish: (info) => finished(spec, info),
    onRestart: () => playSpec(spec),
    onPauseChange: (paused, started) => {
      overlay.hidden = !paused;
      if (!paused) return;
      overlay.innerHTML = started
        ? `<div class="box"><h1>Paused</h1><p>Esc resumes, R restarts the drill.</p><button class="primary" id="resume">Resume</button></div>`
        : `<div class="box"><h1>${esc(spec.title)}</h1><p>${esc(spec.goal)}</p><p class="muted">The clock starts when you do.</p><button class="primary" id="begin">Start (Enter)</button></div>`;
      (document.getElementById('resume') as HTMLElement | null)?.addEventListener('click', () => g.setPaused(false));
      (document.getElementById('begin') as HTMLElement | null)?.addEventListener('click', () => g.begin());
      el('pause').textContent = 'Resume';
    },
  });
  game = g;
  el('pause').onclick = () => {
    if (!overlay.hidden && !overlay.querySelector('#resume')) g.begin();
    else g.setPaused(overlay.hidden);
  };
  el('restart').onclick = () => playSpec(spec);
}

function paintHud(h: HudInfo): void {
  const el = (id: string): HTMLElement | null => document.getElementById(id);
  const hp = el('hp');
  if (!hp) return;
  hp.style.width = `${Math.max(0, h.hp)}%`;
  (hp.style as CSSStyleDeclaration).background = h.hp > 50 ? '#2a9d5c' : h.hp > 25 ? '#d98a00' : '#c0262d';
  el('hpn')!.textContent = h.alive ? `${h.hp} / 100` : 'down';
  el('clock')!.textContent = `${Math.max(0, h.clockS).toFixed(1)} s (${h.phase.replace('_', ' ')})`;
  el('spike')!.textContent = h.spike;
  el('smokes')!.textContent = String(h.smokesLeft);
  if (!h.paused || h.started) el('pause')!.textContent = h.paused ? 'Resume' : 'Pause';
  el('mind')!.innerHTML = h.mind.length
    ? h.mind.map((t) => `<div>${(t.tick / 20).toFixed(1)}s ${esc(t.botId)} [${esc(t.node)}] ${esc(t.reason)}</div>`).join('')
    : '<div class="muted">Nothing yet. The bots decide once the clock runs.</div>';
}

// ---- result screen ------------------------------------------------------------------------------------
function record(spec: DrillSpec, info: FinishInfo): void {
  const rec = createRecorder({ matchId: `play-${Date.now()}-${ownLogs.length}`, mapId: spec.mapId, playerId: PLAYER, role: 'duelist' });
  rec.beginRound(info.initial);
  rec.push(info.result.events);
  // A drill can end before the round does (the verdict is reached first); close it with the side that the drill's verdict favours.
  const team = spec.player.team;
  rec.endRound(info.final.winner ?? (info.result.success ? team : team === 'attack' ? 'defense' : 'attack'));
  const log = rec.toLog();
  if (log.rounds.length) {
    ownLogs.push(log);
    saveOwn();
  }
}

async function finished(spec: DrillSpec, info: FinishInfo): Promise<void> {
  lastResult = { spec, info };
  record(spec, info);
  const before = session.staircase(spec.skill).difficulty;
  showResult(spec, info, null, before, before);
  let debrief: { text: string; source: string };
  try {
    debrief = await session.complete(info.result as DrillResult);
  } catch (err) {
    debrief = { text: `The debrief could not be written (${String(err)}).`, source: 'none' };
  }
  const after = session.staircase(spec.skill).difficulty;
  if (lastResult?.info === info) showResult(spec, info, debrief, before, after);
}

function showResult(spec: DrillSpec, info: FinishInfo, debrief: { text: string; source: string } | null, before: number, after: number): void {
  game?.destroy();
  game = null;
  const r = info.result;
  const lines = narrate(r.events, PLAYER).slice(0, 24);
  const change = debrief === null ? '' : after > before ? 'harder' : after < before ? 'easier' : 'the same';
  $app.innerHTML = `<section id="result" class="screen"><div class="wrap">
    <div class="card">
      <div class="verdict ${r.success ? 'ok' : 'bad'}">${r.success ? 'Drill complete' : 'Not this time'}</div>
      <div><b>${esc(spec.title)}</b> <span class="muted">(${esc(spec.skill.replace('_', ' '))}, ${(r.ticks / 20).toFixed(1)} s)</span></div>
      <div class="muted">${esc(spec.goal)}</div>
      <h2>What happened</h2>
      ${lines.length ? `<ul id="events">${lines.map((l) => `<li>${esc(l)}</li>`).join('')}</ul>` : '<p class="muted">Nothing happened in the round: no shots, kills or plants.</p>'}
      <h2>Coach</h2>
      ${debrief ? `<p>${esc(debrief.text).replace(/\n/g, '<br>')}</p><p class="muted">Written by: ${esc(debrief.source)}</p>` : '<p class="muted">Writing the debrief...</p>'}
      ${debrief ? `<h2>Next difficulty</h2><p>${esc(spec.skill.replace('_', ' '))} drills go from ${before.toFixed(2)} to ${after.toFixed(2)} (${change}).</p>` : ''}
      <div class="row">
        <button id="retry">Retry same situation</button>
        <button class="primary" id="next" ${debrief ? '' : 'disabled'}>Next drill</button>
        <button id="re" ${debrief ? '' : 'disabled'}>Re-analyze my play</button>
        <button id="home">Back to start</button>
      </div>
      <p class="muted">${ownRounds()} rounds of your play recorded.</p>
    </div></div></section>`;
  const $ = (id: string): HTMLElement => document.getElementById(id) as HTMLElement;
  $('retry').onclick = () => playSpec(spec);
  $('next').onclick = () => {
    const q = queue.shift();
    playSpec(q ?? session.nextDrill().drill);
  };
  $('re').onclick = () => {
    if (source.id !== 'own') {
      // The sample report came from invented rounds; switch to the player's own.
      source = SOURCES.find((s) => s.id === 'own') ?? source;
      session = makeSession(source);
    }
    reanalyze();
    showStart();
  };
  $('home').onclick = showStart;
}

// Reachable from the console for debugging.
Object.defineProperty(window, 'drillPractice', { get: () => ({ session, currentSpec, lastResult, ownLogs }) });

showStart();

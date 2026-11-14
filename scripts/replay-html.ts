/**
 * Builds replay/index.html: one self-contained 2D replay of a simulated player
 * playing the planner's first three drills (report, drill, trace, outcome, debrief).
 * Run: `node scripts/replay-html.ts [seed]`. The page template is scripts/replay-template.html.
 */
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { Skill } from '../shared/coaching.ts';
import type { SimEvent, SimState, TraceEntry } from '../shared/contract.ts';
import { loadMap } from '../shared/maps/index.ts';
import { sessionWithWeakness } from '../session/headless.ts';
import { createSimulatedPlayer, profileWithWeakness } from '../session/simulated-player.ts';

const seed = Number(process.argv[2] ?? 6);
const WEAK: Skill = 'time_pressure';
const DRILLS = 3;
const r = (v: number): number => Math.round(v * 100) / 100;

const slim = (s: SimState) => ({
  tick: s.tick,
  t: r(s.roundTimeLeftS),
  phase: s.phase,
  winner: s.winner,
  agents: s.agents.map((a) => ({ id: a.id, team: a.team, bot: a.isBot, x: r(a.pos.x), y: r(a.pos.y), yaw: r(a.yaw), hp: Math.round(a.hp), alive: a.alive, spike: a.carryingSpike })),
  smokes: s.smokes.map((m) => ({ x: r(m.pos.x), y: r(m.pos.y), radius: m.radius, ends: m.endsAtTick })),
  spike: s.spike,
});

const session = sessionWithWeakness(WEAK, seed);
const player = createSimulatedPlayer({
  seed,
  profile: profileWithWeakness(WEAK),
  hooks: { onTick: (run) => frames?.push(slim(run.state())) },
});
let frames: ReturnType<typeof slim>[] | null = null;

const drills = [];
for (let i = 0; i < DRILLS; i++) {
  const { drill } = session.nextDrill();
  frames = [];
  // The state before the first step is not given to the hook; record it from a fresh run's view by
  // taking the first frame's tick as the start (ticks are 1-based after the first step).
  const result = player.play(drill);
  const traits = player.traitsFor(drill);
  player.learn(drill);
  const debrief = await session.complete(result);
  drills.push({
    spec: drill,
    playerTraits: traits,
    success: result.success,
    ticks: result.ticks,
    frames,
    events: result.events as SimEvent[],
    trace: result.trace as TraceEntry[],
    debrief,
  });
}
frames = null;

const data = { seed, tickRate: 20, map: loadMap(drills[0].spec.mapId), report: session.report, drills };
const json = JSON.stringify(data).replace(/</g, '\\u003c');
const here = dirname(fileURLToPath(import.meta.url));
const html = readFileSync(join(here, 'replay-template.html'), 'utf8').replace('/*__DATA__*/null', () => json);
const outDir = join(here, '..', 'replay');
mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, 'index.html'), html);
console.log(`wrote replay/index.html (${html.length} bytes)`);
for (const d of drills) console.log(`  ${d.spec.title}: ${d.frames.length} ticks, ${d.success ? 'success' : 'failed'}, ${d.debrief.source}`);

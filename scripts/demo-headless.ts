/**
 * The "bot -> scenario -> behavior trace" demo with no 3D client.
 * Run: `node scripts/demo-headless.ts [seed]`.
 *
 * One simulated player with a planted weakness: the analyzer reads their match
 * history, the planner picks the first drill, the bot plays it, and the coach
 * writes the debrief (the template unless ANTHROPIC_API_KEY is set).
 */
import type { Skill } from '../shared/coaching.ts';
import { sessionWithWeakness } from '../session/headless.ts';
import { CoachingSession } from '../session/session.ts';
import { createSimulatedPlayer, profileWithWeakness } from '../session/simulated-player.ts';

const seed = Number(process.argv[2] ?? 6);
const WEAK: Skill = 'time_pressure';

const session: CoachingSession = sessionWithWeakness(WEAK, seed);
const report = session.report;
console.log(`== Weakness report for ${report.playerId} (${report.role}, ${report.matchesRead} matches read)`);
for (const f of report.findings) {
  console.log(`- ${f.skill}: severity ${f.severity.toFixed(2)}. ${f.summary}`);
  for (const e of f.evidence.slice(0, 2)) console.log(`    evidence: match ${e.matchId} round ${e.round}, tick ${e.tick}: ${e.note}`);
}

const { skill, drill } = session.nextDrill();
console.log(`\n== First drill: ${drill.title} (${skill}, difficulty ${drill.difficulty})`);
console.log(`Goal: ${drill.goal}`);
console.log(`Bots: ${drill.bots.map((b) => `${b.id} (${b.archetypeId}, reaction ${b.traits.reactionMs} ms)`).join(', ')}`);

const player = createSimulatedPlayer({ seed, profile: profileWithWeakness(WEAK) });
const result = player.play(drill);

console.log('\n== Bot behavior trace (behavior-tree node: reason)');
const bots = result.trace.filter((t) => t.botId !== drill.player.id);
const lines = bots.filter((t, i) => i === 0 || t.node !== bots[i - 1].node || t.reason !== bots[i - 1].reason);
const step = Math.max(1, Math.ceil(lines.length / 15));
for (const t of lines.filter((_, i) => i % step === 0).slice(0, 15)) {
  console.log(`  t=${(t.tick / 20).toFixed(1)}s ${t.botId} [${t.node}] ${t.reason}`);
}
console.log(`  (${bots.length} trace entries in ${(result.ticks / 20).toFixed(1)} s of play)`);

console.log(`\n== Outcome: ${result.success ? 'SUCCESS' : 'FAILED'} after ${(result.ticks / 20).toFixed(1)} s`);
const key = result.events.filter((e) => e.type === 'kill' || e.type === 'plant' || e.type === 'defuse' || e.type === 'round_end');
for (const e of key.slice(0, 6)) console.log(`  ${JSON.stringify(e)}`);

player.learn(drill);
const debrief = await session.complete(result);
console.log(`\n== Debrief (source: ${debrief.source})`);
console.log(debrief.text);
console.log(`\nNext difficulty for ${skill}: ${session.staircase(skill).difficulty.toFixed(2)}`);

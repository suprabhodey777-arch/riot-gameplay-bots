/**
 * Clone one recorded human into a ghost.
 *
 *   node ghost/clone/build.ts [--logs <dir>] [--out <dir>] [--player <name>]
 *
 * Reads `logs/*.jsonl`, replays natural-play rounds, labels every frame,
 * measures a profile, stores the nearest-neighbour training samples and writes
 * `profiles/report.json` with held-out fidelity numbers.
 */
import { mkdirSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import type { ActionKind } from '../contracts.ts';
import type { LogEvent } from '../contracts.ts';
import { readLogFiles, segments, frames } from './replay.ts';
import type { Frame, ReplaySegment } from './replay.ts';
import { labelFrames } from './label.ts';
import type { LabelledFrame } from './label.ts';
import { measureProfileWithCounts } from './profile.ts';
import { featureVector, leaveOneOutThreshold, DEFAULT_PROFILE_DIR } from './policy.ts';
import type { PolicySample } from './policy.ts';
import {
  evaluatePlayer,
  mostCommonKind,
  splitPlayerRounds,
} from './heldout.ts';
import type { HeldoutReport, PlayerRound } from './heldout.ts';

export const DEFAULT_LOG_DIR = fileURLToPath(new URL('../../logs/', import.meta.url));

export interface BuildOptions {
  logs?: string;
  out?: string;
  player?: string;
}

export interface BuildReport {
  version: 1;
  logs: {
    dir: string;
    files: { file: string; events: number; skipped: number }[];
    skippedLines: number;
    segments: number;
  };
  players: Record<string, HeldoutReport>;
  population: { kind: ActionKind; n: number; counts: Partial<Record<ActionKind, number>> };
}

interface LabelledRound extends PlayerRound {
  segment: ReplaySegment;
}

function parseArgs(argv: string[]): BuildOptions {
  const opts: BuildOptions = {};
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    const eq = arg.indexOf('=');
    const name = eq >= 0 ? arg.slice(0, eq) : arg;
    const inline = eq >= 0 ? arg.slice(eq + 1) : null;
    const value = () => {
      if (inline !== null) return inline;
      const next = argv[++i];
      if (next === undefined) throw new Error(`missing value for ${name}`);
      return next;
    };
    if (name === '--logs') opts.logs = value();
    else if (name === '--out') opts.out = value();
    else if (name === '--player') opts.player = value();
    else throw new Error(`unknown argument: ${arg}`);
  }
  return opts;
}

/** Run the whole pipeline. Writes files and returns the report object. */
export function runBuild(options: BuildOptions = {}): BuildReport {
  const logsDir = resolve(options.logs ?? DEFAULT_LOG_DIR);
  const outDir = resolve(options.out ?? DEFAULT_PROFILE_DIR);

  const files = readLogFiles(logsDir);
  const allSegments: ReplaySegment[] = [];
  for (const file of files) allSegments.push(...segments(file.events));

  const playerRounds = new Map<string, ReplaySegment[]>();
  for (const segment of allSegments) {
    for (const name of Object.keys(segment.teams)) {
      const list = playerRounds.get(name) ?? [];
      list.push(segment);
      playerRounds.set(name, list);
    }
  }

  const wanted = options.player;
  const allNames = [...playerRounds.keys()].sort();
  const populationPlayers = allNames.filter((name) => (playerRounds.get(name)?.length ?? 0) >= 2);
  const players = wanted ? allNames.filter((name) => name === wanted) : populationPlayers;

  const frameCache = new Map<ReplaySegment, Frame[]>();
  const labelCache = new Map<ReplaySegment, LabelledFrame[]>();
  const eventsFor = (segment: ReplaySegment): LogEvent[] => segment.events;
  const roundsFor = (segment: ReplaySegment): LabelledRound => {
    let f = frameCache.get(segment);
    if (!f) {
      f = frames(segment);
      frameCache.set(segment, f);
    }
    let labelled = labelCache.get(segment);
    if (!labelled) {
      labelled = labelFrames(f, eventsFor(segment));
      labelCache.set(segment, labelled);
    }
    return {
      segment,
      matchId: segment.matchId,
      round: segment.round,
      frames: f,
      labelled,
      events: eventsFor(segment),
    };
  };

  mkdirSync(outDir, { recursive: true });

  // Every player's own held-out split, used to build the population prior.
  const populationCounts = new Map<ActionKind, number>();
  const playerRoundData = new Map<string, LabelledRound[]>();
  for (const player of [...new Set([...populationPlayers, ...players])]) {
    const rounds = (playerRounds.get(player) ?? []).map(roundsFor);
    playerRoundData.set(player, rounds);
    if (!populationPlayers.includes(player)) continue;
    const split = splitPlayerRounds(rounds);
    for (const round of split.train) {
      for (const l of round.labelled) {
        if (l.frame.player !== player) continue;
        populationCounts.set(l.action.kind, (populationCounts.get(l.action.kind) ?? 0) + 1);
      }
    }
  }
  const baselineKind = mostCommonKind(populationCounts);

  const report: BuildReport = {
    version: 1,
    logs: {
      dir: logsDir,
      files: files.map((f) => ({ file: f.file, events: f.events.length, skipped: f.skipped })),
      skippedLines: files.reduce((sum, f) => sum + f.skipped, 0),
      segments: allSegments.length,
    },
    players: {},
    population: {
      kind: baselineKind,
      n: [...populationCounts.values()].reduce((a, b) => a + b, 0),
      counts: Object.fromEntries(populationCounts) as Partial<Record<ActionKind, number>>,
    },
  };

  for (const player of players) {
    const rounds = playerRoundData.get(player) ?? [];
    // The published ghost learns from every natural round; the report's
    // held-out score deliberately re-trains on only the 80% split.
    const allFrames = rounds.flatMap((r) => r.frames).filter((f) => f.player === player);
    const allEvents = rounds.flatMap((r) => r.events);
    const measured = measureProfileWithCounts(player, allFrames, allEvents).profile;
    measured.roundsRecorded = rounds.length;

    const samples: PolicySample[] = [];
    for (const round of rounds) {
      for (const l of round.labelled) {
        if (l.frame.player !== player) continue;
        samples.push({
          features: featureVector(l.frame.situation),
          action: l.action,
          source: { player, matchId: round.matchId, round: round.round, t: l.frame.t },
        });
      }
    }
    const threshold = leaveOneOutThreshold(samples);

    writeFileSync(
      join(outDir, `${player}.json`),
      `${JSON.stringify({ ...measured, threshold }, null, 2)}\n`,
    );
    writeFileSync(join(outDir, `${player}.samples.json`), `${JSON.stringify(samples)}\n`);

    report.players[player] = evaluatePlayer(player, rounds, baselineKind);
  }

  writeFileSync(join(outDir, 'report.json'), `${JSON.stringify(report, null, 2)}\n`);
  return report;
}

export function main(argv = process.argv.slice(2)): void {
  try {
    const options = parseArgs(argv);
    const report = runBuild(options);
    const count = Object.keys(report.players).length;
    console.log(`ghosts built: ${count} player(s), ${report.logs.segments} natural rounds, ${report.logs.skippedLines} skipped line(s)`);
    console.log(`report: ${join(resolve(options.out ?? DEFAULT_PROFILE_DIR), 'report.json')}`);
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}

function isMain(): boolean {
  const entry = process.argv[1];
  if (!entry) return false;
  return pathToFileURL(resolve(entry)).href === import.meta.url;
}

if (isMain()) main();

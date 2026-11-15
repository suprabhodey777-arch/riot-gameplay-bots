# ValoDrill

A coach that reads your matches, finds the situation you lose most, rebuilds those exact moments as drills against bots that play like people, and adapts as you improve. Built for DevHacks 2026, Riot track “Gameplay bots: agents that play and adapt.” The demo starts with sample match histories and records your practice and matches into the same coaching loop. The maps, agents and weapons are our own designs inspired by tactical shooters.

## What you can do

- **Practice:** train entry fights, clutches, time pressure and smoke usage. Each skill has its own difficulty staircase; results update the weakness report and next drill.
- **Rehearse my mistakes:** replay failed moments with the recorded side, positions, enemies, clock and spike state, on their original map. Retry the same drill and seed.
- **Play a match:** you and four bot teammates face five bots, first to 7 rounds, swapping sides after round 6. Buy phases last 20 seconds, with pistol, eco, force and full-buy rounds, weapon carryover and loss bonuses.
- **Duel streamer-style personas:** first to 5 against Zephyr (aggressive duelist), Atlas (team leader), Ember (sniper anchor), Wisp (lurker) or Nova (utility support). Face a persona team in match mode too.
- **Use the streamer overlay:** simulated chat, event alerts, goals and score displays react to the game.
- **Get agent advice:** main and practice picks use your opening duels, kill distances, smoke use, objectives and skill results. Choose Rook, Haze, Wren or Pike for dash, flash, smoke and recon kits. Advice also lists comparable tactical-shooter roles and kits.

## How it works

```text
Unity client <-> bridge <-> sim / bots / analyzer / coach
render + input    TCP       rules, decisions, weakness report, drills, debrief
```

Unity renders, takes input and raycasts player shots. The bridge owns game logic: a deterministic simulation advances one tick per input at 20 Hz over local TCP (`127.0.0.1:7777`, newline-delimited JSON, `bots-wire.4`). The browser client and evidence harness use the same simulation.

The analyzer ranks failures against role benchmarks. The planner rehearses mistakes, then generates drills for weak skills. A separate staircase targets 75% success per skill: success raises difficulty by 0.05; failure lowers it by 0.15, bounded to 0–1. This target needs testing with people.

## The bots

A behaviour tree chooses fights, holds, investigations, pushes, plants, defuses and utility use, with decision traces explaining why. Fog of war limits enemy information to sight, hearing, recon reveals and remembered positions. Walls and smoke block sight; running footsteps give positional clues through walls.

The fairness rule forbids firing before the bot's `reactionMs` has elapsed after first detection. Aim depends on settling after a turn, target movement, distance, the bot's movement, weapon and sustained fire. Turning onto a moving target is harder than holding a stationary one; hit outcomes are seeded probabilities.

Difficulty aim scales in [bot/bot.ts](bot/bot.ts): Easy **0.35**, Normal **0.85**, Hard **1.2**, Pro **1.45**. Easy also doubles reaction delays. These are tuning parameters, not measured player ranks. Personas add wide swings, jiggle peeks, trades and repositioning. All personas have original names, characters and text callouts; no real person's name, likeness or voice is used.

## The evidence

[Saved results](evidence/RESULTS.md) compare **120 simulated learners**, each with one planted weakness, across **20 practice drills per condition**. Pre/post exams use the same **5 held-out forms**, totalling **10 drills per skill**. Gains are percentage points of pass rate, with **95% bootstrap confidence intervals**.

| Practice condition | Weak-skill gain | Overall gain | Drills on weak skill |
| --- | --- | --- | --- |
| Generic baseline, fixed difficulty | 4.3 [2.7, 5.9] | 1.5 [1.0, 1.9] | 24.7% |
| Random skills with staircase | 5.5 [3.8, 7.3] | 1.4 [1.0, 1.9] | 25.5% |
| Targeted skills with staircase | 9.7 [7.3, 12.2] | 2.4 [1.9, 3.1] | 67.9% |

Targeted practice gained **5.4 [3.9, 6.9] points** over baseline and **4.2 [3.0, 5.3]** over random practice on the weak skill. The analyzer ranked the planted weakness first for **94.2%** of learners. The fairness check found **0 early fire inputs out of 137,008**, across **26,400 drills**.

The learners use an invented learning rule; this does not establish human improvement or transfer. Role benchmarks are placeholders. Rehearsals were excluded. Clutch and smoke-usage exam gains were **0.0 points in every condition**; the clutch exam is especially hard for the simulated learner. The saved run demonstrates the mechanism under those assumptions, not validation of every current feature or bot setting.

## How to run

From the repository root, use Node with native TypeScript execution and JSON import attributes, plus npm:

```sh
npm install
npm run bridge
```

Keep the bridge running. Open `unity/` in Unity Hub with **Unity 6000.6.9f1** (the version in `ProjectSettings/ProjectVersion.txt`), then press **Play**. The client bootstraps from an empty scene. If disconnected, start the bridge and use **Reconnect**. `BRIDGE_PORT` overrides the port on both sides.

For a Mac app, run the command from [unity/README.md](unity/README.md) at the repository root:

```sh
/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath unity -executeMethod CommandLineBuild.MacApp -logFile -
```

Launch `unity/Build/GameplayCoach.app` with the bridge running. For a first demo, choose **Practice**, a sample player, map and agent, then inspect the report and select **Rehearse my mistakes** or **Next practice drill**. Press **Begin** or **Enter** at the drill intro.

| Controls | Action |
| --- | --- |
| Mouse / WASD | Look / move |
| Shift / Ctrl | Walk quietly / crouch |
| Left / right mouse | Fire / scope with Longshot |
| R / 1 / 2 / mouse wheel | Reload / primary / secondary / cycle weapons |
| Hold C / Q / X, release | Aim and throw smoke / flash / recon |
| E / hold F | Dash / plant or defuse |
| B | Buy menu during buy phase |
| M / hold Tab | Tactical map / peek map |
| Esc / F3 | Pause and settings / performance readout |

Optional browser client: `npm run play`, then open `http://localhost:5181`.

## Repo layout

| Path | Purpose |
| --- | --- |
| `unity/`, `play/` | First-person Unity and browser clients |
| `bridge/` | Local transport, sessions and streamer events |
| `sim/`, `shared/` | Simulation, contracts, original maps, agents, weapons and personas |
| `bot/` | Behaviour tree, perception, navigation, aim and persona moves |
| `analyzer/` | Weakness analysis, rehearsals, planning, staircases and agent advice |
| `session/`, `replay/` | Drill judging, match economy, recording and replay |
| `coach/`, `evidence/` | Debriefs and simulated-learner evaluation |
| `docs/` | [Bridge protocol](docs/unity-bridge.md), [abilities](docs/abilities.md), [weapons and economy](docs/weapons-economy.md), [streamer mode](docs/streamer-mode.md), [art direction](docs/art-direction.md) |

## Testing

```sh
npm run typecheck
npm run test:quick
npm test
npm run play:build
```

`test:quick` skips longer integration, rehearsal, persona and evidence suites. Regenerate evidence with `npm run evidence:quick` for 30 learners, or `node evidence/run.ts --n 120 --budget 20 --forms 5` for the full configuration. Both overwrite `evidence/RESULTS.md` and `evidence/results.json`.

## Limits and what is next

The demo uses synthetic histories and locally recorded play. The bridge accepts the repository's `MatchLog` format; there is no external match-service importer. Rehearsals reconstruct logged situations within our simulation. Sessions belong to a connection; reconnecting starts fresh. Local play needs no service key, and debriefs have a template fallback.

Next: measure benchmarks from real match data, test retention and transfer with people, improve clutch and smoke exam sensitivity, and rerun comparisons with rehearsals, current aim settings and team behaviour included.

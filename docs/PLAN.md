# Ghost Rematch: gap review and work split

Saturday 14 Nov, 3:15 PM. Live version, with comments:
https://claude.ai/artifact/FQazgDbQNEM7taWUpy1jw6

This replaces the plan in `docs/TEAM.md`. The spec is the handoff
("Ghost Rematch — DevHacks '26 Handoff").

## Verdict

The sim work now on `main` (Suprabho, 40 commits, about 7,000 lines of
TypeScript) is good engineering for a different product: all 220 of its tests
pass (one live-API test skipped). It follows the older `docs/TEAM.md` plan: our
own simulation, a 2D top-down client, and a weakness analyzer fed by synthetic
match logs. The handoff, written at 2:40 PM, changed three things it does not
do: build on the open-source game `jangerine/valorant-like-fps`, play in
first-person 3D, and clone bots from recorded human play.

The gap is not effort or quality. Two plans were live at once. About a third of
the sim work ports straight over (rehearse-my-mistake, the coach, the circuit
breaker, the staircase, the fairness check, the evidence statistics). The sim,
the 2D client, the archetype bots and the simulated-learner evidence should not
be the demo.

The base game has not been run yet. The handoff's first gate is **3:30 PM**.

## Feature by feature

Of the handoff's 11 work streams, none is fully met, five are partly covered,
six have nothing. Port = move the logic into the base game's Node side;
build = new work.

| Handoff stream | What `main` has | Call |
| --- | --- | --- |
| 0. Contracts | `shared/contract.ts` v3 for its own sim; `analyzer/situation.ts` is close to the situation vector | Build the handoff's five formats; borrow names from `situation.ts` |
| 1. Game base | Nothing. Own sim on its own map, Saltworks | Build |
| 2. Recorder | `session/recorder.ts` records its own sim, not a real game | Build in `server.js`; reuse the visibility idea from `bot/view.ts` |
| 3. Bot runtime | `bot/nav.ts` (waypoints, paths), `bot/view.ts` (fog of war, view cone, reaction delay), all 2D | Port nav and view to the base map's boxes; build the socket client, aim and spread |
| 4. Default bot | `bot/bot.ts` behavior tree with archetype traits | Port as the default bot; add ability use |
| 5. Cloning | No learning from play; bots are hand-set archetypes. `bot/health.ts` fires on stuck play, not distance to recordings | Build; keep `health.ts` as a second breaker trigger |
| 6. Rematch the moment | `analyzer/rehearse.ts` rebuilds a lost moment with hidden starts and the matching enemy archetype | Port, the closest match. Swap archetype for the killer's ghost |
| 7. Skill matching | `analyzer/staircase.ts` keeps drills near a target success rate | Port as the auto-tuner; build the slider |
| 8. Trace overlay and coach | `coach/` (LLM debrief, number check, template fallback, timeout) with tests | Port the coach almost as is; build the 3D overlay and callouts |
| 9. Teammate ghost and library | Nothing | Build |
| 10. 3D views | `play/` is 2D top-down; `scripts/replay-html.ts` writes a 2D replay page | Build 3D; reuse `replay-html.ts` as the 2D debug viewer |
| 11. Evidence | `evidence/run.ts`: 30 simulated learners, bootstrap CIs, fairness check (0 of 21,705 shots early). Its own caveat: "not that humans transfer" | Reuse the stats and fairness check; build the human tests |

The weakness analyzer over synthetic match history is not in the handoff. There
is no real match history to feed it, so it stays out of the demo.

## Decision

Build on `jangerine/valorant-like-fps` as the handoff says, and port the sim
work's useful parts into a `ghost/` folder beside the game. Do not revert the
sim work: tag it `sim-v3`, and put the base game and ghost code in new top-level
folders (`game/`, `ghost/`).

- **Why not keep the sim:** the judge said not to remake VALORANT, and the team
  rejected top-down. A 3D client on our own sim is still a remake, with no
  abilities, buying or weapons.
- **Why not start the brain from zero:** rehearse, coach, breaker, staircase and
  the evidence stats are tested and map onto streams 6, 8, 5, 7 and 11.
- **What we give up:** a deterministic, seeded sim that replays exactly from
  inputs. The base game trusts clients and runs in real time. Name it as a known
  limit.
- **Fallback:** if the base game does not run a 1v1 round by 3:30 PM, switch to
  a backup repo from the handoff, not to the 2D client.

## Who does what

Teammates 3 and 4 are placeholders until names are set. Branches follow
`lane/feature`.

### Lane A, Pratham: game base, recorder, rematch (streams 0, 1, 2, 6)

- [ ] By 3:30 PM: `npm install`, `npm start`, a 1v1 round in two tabs at localhost:3000
- [ ] Write the five contracts in `ghost/contracts.ts`; team approves before agents start
- [ ] Force team and spawn; allow any mix of humans and bots, including 1v1
- [ ] `load-scenario` admin message and a practice flag that turns off scoring
- [ ] Export colliders and site boxes as plain JSON for Node; translate UI text to English
- [ ] Recorder: every message and event to `logs/<matchId>.jsonl`, stable names, `move` at 20/s
- [ ] Rematch: port `analyzer/rehearse.ts` to read our logs; deaths menu with beaten mark; instant retry; variations after a win
- [ ] Owns the video cut and the Devpost

Done when: two tabs play a full round, its log matches a human's watch-through,
and a death is being replayed within seconds.

### Lane B, Suprabho: bot runtime, default bot, cloning (streams 3, 4, 5, 7)

- [ ] Node bot on `socket.io-client`: join, `move` at 20/s, `tracer`, `hit`, `ability`, `buy`, plant and defuse
- [ ] Port `bot/nav.ts` and `bot/view.ts` to the base map's boxes: waypoints, A*, ray-vs-box sight from head height, smokes, reaction delay; steps under 5 units
- [ ] Aim with capped turn speed and angular error; weapon spread, more while moving
- [ ] Port `bot/bot.ts` as the default bot; add at least one ability
- [ ] Cloning: featurize at 4/s, label actions by rule, compute profiles, nearest-neighbor policy per player
- [ ] Breaker on neighbor distance, plus `bot/health.ts` for stuck play; `fallback` set in the trace
- [ ] Held-out score on 20% of rounds: ghost vs default bot
- [ ] Skill matching: 0.5 to 1.5 scale on reaction, aim error, flick speed; port `staircase.ts` as the auto-tuner

Done when: two ghosts play the same scenario visibly differently, and held-out
agreement beats the default bot.

### Lane C, Teammate 3: views, overlay, coach (streams 8, 10)

- [ ] Trace relay: bot to server to spectators and the drill player
- [ ] Overlay: action, matched recording, match distance, fallback badge, skill scale
- [ ] Port `coach/` to read the new trace; keep the number check, timeout and template fallback
- [ ] Templated callouts from actions, no LLM
- [ ] Spectator camera (free-fly and follow), 3D replay from a log, ghost's-eye view
- [ ] Side-by-side mode for the video; reuse `scripts/replay-html.ts` as the 2D path viewer

Done when: the overlay updates live, the coach cites real trace events, and one
recorded round plays back in 3D.

### Lane D, Teammate 4: evidence, teammate ghost (streams 9, 11)

- [ ] Guess-the-ghost page that records answers
- [ ] Transfer test script: random groups set before play, timed drills, then logged rounds against the real player
- [ ] Results panel: every number with baseline and sample size; reuse the bootstrap CI and fairness check from `evidence/run.ts`
- [ ] Recruit at least 8 testers for 10 PM to 1 AM
- [ ] Teammate ghost: follow a push, trade, hold the other angle, rotate on plant; callouts in chat
- [ ] Lobby listing ghosts with rounds recorded and a one-line style
- [ ] README with setup, architecture and credit to the base repo

Done when: a 2v2 with one ghost teammate completes, and the guess-the-ghost and
transfer tests have been run on real testers.

### Everyone

- [ ] 7 to 10 PM: record as many rounds as possible; each person is a future ghost
- [ ] Screen-record every play session from the first working build

## Timeline

Gates are the handoff's fixed anchors; the work between them is our sizing.

| When | Gate or work |
| --- | --- |
| Sat 3:30 PM | **Gate:** game runs a 1v1 |
| Sat 3 to 7 PM | A: game base, contracts, recorder · B: bot runtime, default bot · C: overlay and coach port (to 10 PM) · D: test pages and results |
| Sat 7 PM | **Gate:** recorder works and the default bot fights |
| Sat 7 to 10 PM | All: record rounds · A: rematch (to 1 AM) · B: cloning and breaker · D: teammate ghost and lobby |
| Sat 10 PM | **Gate:** two ghosts are visibly different (make or break) |
| Sat 10 PM to Sun 1 AM | All: tests on other hackers · B: skill matching · C: 3D views |
| Sun 1 to 7 AM | Quiet hours: polish, capture all footage |
| Sun 7 to 9 AM | A: edit the video |
| Sun 9 AM | **Gate:** video exported |
| Sun 10:30 AM | **Gate:** Devpost submitted (11 AM is final) |

If two ghosts are not clearly different at 10 PM, lanes C and D stop new
features and help with style traits and more recorded rounds. If behind at
4 AM, drop the coach first, then the teammate ghost, then the new-site demo.

## Standards every merge must meet

1. Built against the approved contracts; a contract change needs every lane to agree.
2. One human has played the feature before merge, not just the agent's report or green tests.
3. Runs in the base game, first-person 3D. No new 2D or sim-only features.
4. The bot sees only what line of sight allows and never fires before its reaction delay; the fairness check stays at 0.
5. The LLM never touches aim or movement; every coach call has a timeout and a fallback.
6. Every number shown has its baseline and sample size; flat or negative results are reported as they are.
7. Raw logs and test answers are committed so the judge can check them.
8. Our code lives in its own folders (`ghost/`, `tests/`), so the weekend's work is clear against the base repo.

## Open questions

- [ ] Who are Teammates 3 and 4, and do they take lanes C and D?
- [ ] Is Suprabho on board with porting rather than continuing the sim?
- [ ] Does the base game run a 1v1 round? Answer by 3:30 PM, or switch repos.
- [ ] Keep Riot's agent names or rename them? Ask the judge.
- [ ] Which LLM and key does the coach use? The coach already wires the Anthropic API.
- [ ] How many recorded rounds make a usable ghost? Find out by 10 PM.

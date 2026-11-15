# Showcase scenarios (draft)

**Goal:** show judges how our tech helps a player get better, from finding a weakness to proving it improved. Every scenario shows a feature that serves that goal. Settings, menus and options are left out.

The story in one line: **the game watches how you play, finds what you lose, makes you replay that exact moment against bots that behave like people, coaches you after each attempt, and measures whether you improved.**

## Scenario 1: "What am I bad at?" (weakness report)
- **Setup:** Practice → choose the sample player *weak entry* (seed 11).
- **Show:** the weakness report. Entry duels come out as the worst finding, with a severity bar, the rate against the benchmark, and the evidence lines (round, callout, how long after contact they died).
- **Point it makes:** the analyzer reads real match logs and names the problem in plain words, with proof.
- **Shots:** the report screen, then slowly scrolling the evidence.

## Scenario 2: "Play the moment you lost" (Rehearse my mistakes)
- **Setup:** from the report, Rehearse → round 2 (lost the opening duel at North Causeway 0.6 s after contact).
- **Show:**
  - The drill intro card, then the same spot, same clock, same enemy angle.
  - Attempt 1: the player dies again.
  - The coach's debrief after the attempt.
  - Attempt 2: the player pre-aims the angle and wins.
- **Point it makes:** practice is built from *your* mistakes, not generic aim training.
- **Shots:**
  - first person, with the minimap and the bot's thought line visible
  - the result card showing the staircase moving
  - the debrief text

## Scenario 3: "Bots that play like people" (fair, readable bots)
- **Setup:** any drill. Turn on spectating or the bot's-mind display if the build has it.
- **Show:**
  - **Fair timing:** a bot reacts after a human delay (about 240–320 ms), turns at a human speed, and misses its first shot while still settling its aim.
  - **Visible reasoning:** its thought line explains what it's doing ("holding A main", "heard footsteps").
- **Point it makes:**
  - the bots are fair, with no wallhacks and no instant snaps
  - their reasoning is visible, so the player learns why they lost
- **Shots:**
  - a slow-motion clip of a duel with the reaction delay marked on screen
  - a flash scene where the bot is dazed

## Scenario 4: "Practise against a style" (Favourite streamer / archetypes)
- **Setup:** Favourite streamer → pick a persona (the flick duellist, the anchor, the lurker).
- **Show:** the same site played against two personas. One rushes, one holds an off-angle.
- **Point it makes:** players can rehearse against the kind of opponent they struggle with.

## Scenario 5: "Take it into a real match" (Play match)
- **Setup:** Play match on Meridian (our new three-site map).
- **Show:**
  - the buy phase
  - a round with smokes and flashes (the new flash whiteout)
  - the full tactical map (M)
  - the round result
- **Point it makes:** the skills practised carry into a full 5v5 game with economy and utility.
- **Shots:** a fly-over of Meridian, the buy menu, a flash going off, the M map.

## Scenario 6: "Did it work?" (evidence)
- **Setup:** no gameplay. Show `evidence/RESULTS.md` as a clean chart.
- **Show:** among 120 simulated players, targeted practice raised the weak skill by **+4.8 points** against baseline practice and **+3.6** against random practice with the staircase. The fairness check found 0 shots fired faster than the bot's set reaction time.
- **Point it makes:** we measure improvement instead of just claiming it. Say plainly that these are simulated learners.

## Video plan (3 minutes)
| Time | Content |
|---|---|
| 0:00–0:15 | Hook: "You keep dying to the same angle. Here's a game that notices." |
| 0:15–0:40 | Scenario 1: the weakness report |
| 0:40–1:25 | Scenario 2: rehearse the exact moment, the coach, the win |
| 1:25–1:50 | Scenario 3: fair bots with visible reasoning |
| 1:50–2:10 | Scenario 4: personas |
| 2:10–2:40 | Scenario 5: full match on Meridian |
| 2:40–3:00 | Scenario 6: evidence chart, then the closing line |

## Open items before recording
- [ ] Minimap and M map fixed (in progress)
- [ ] Flash overlay in the HUD (in progress)
- [ ] Recorded sounds wired up (in progress)
- [ ] Maps that feel like places (in progress)
- [ ] Decide on the player models after the side-by-side (in progress)
- [ ] A way to show reaction time on screen for Scenario 3 (a debug overlay or the bot's thought line)
- [ ] A recording setup: fixed 1920×1080 Game view, plus Unity Recorder or OBS (to decide)

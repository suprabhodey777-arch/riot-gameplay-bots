# Idea: Whetstone, a sparring partner graded on your improvement

Status: proposal. Sources for every claim are in [RESEARCH.md](RESEARCH.md).

## One sentence

A practice bot whose goal is for the player to get better, not for itself to win
or lose: it plays under human limits, finds your weakest fundamental, sets up
repetitions at the edge of your ability, shows its reasoning, and then measures
whether you improved against opponents it never used in practice.

## Who it is for, and what they do differently afterward

A new or improving player who today has two bad options: bot games that Riot's
own data says do not improve PvP results, or PvP itself, with smurfs and no
feedback. After ten minutes with Whetstone they know which fundamental is
weakest and have a measured before-and-after on it.

Second user: a designer exploring bot behavior, who gets a deterministic replay
and a trace of why the bot did each thing.

## Why this idea

| Complaint (see RESEARCH.md §5) | What Whetstone does |
|---|---|
| A. Bot games do not transfer to PvP | Scores success on a held-out exam (unseen opponent styles and champions), not on win rate against the bot |
| B. Fixed tiers fit almost no one | A per-skill staircase holds each skill near a target success rate |
| C. Predictable yet inhuman | The game runner enforces reaction delay and fog of war; the bot cannot see newer or hidden information |
| D. No feedback | Live "bot's mind" panel and a debrief built from the behavior trace |
| E. Per-champion tuning does not scale | Champions are data; threat ranges, dodge windows and guaranteed combos are derived from the definition |
| F. Bots fail badly in production | Health monitor trips a circuit breaker to a simple scripted bot, visibly |
| G. Hidden bots | Always labelled as a bot, reasoning on screen |

Not addressed: levelling-bot accounts (anti-cheat), leaver replacement in
VALORANT, full 5v5 play.

## How it works

- **Environment.** A small deterministic 1v1 lane duel: skillshots, cooldowns,
  basic-attack ranges, brush. Same code runs headless and in the browser, so any
  match can be replayed exactly.
- **Four fundamentals**, each defined from game events:
  - dodging: an enemy shot was released on target; was it avoided?
  - landing skillshots: the player fired; did it hit?
  - punishing cooldowns: the enemy's main ability is down; did the player land a hit before it returned?
  - spacing: the enemy landed a basic attack; did the player trade back or step out?
- **Fast layer.** A tactical controller chooses actions every tick. No language
  model in the loop.
- **Slow layer.** Every 15 seconds or so a planner picks which fundamental to
  drill and how hard, from the staircases.
- **Language model.** Only writes the debrief from trace facts. Numbers in its
  text are checked against the trace; if it is slow or wrong, a template is used.
- **Safety.** Action entropy, stuck detection and out-of-range inputs feed a
  circuit breaker that hands control to a simple scripted bot and tells the player.

## The demo (bot → scenario → behavior trace)

1. Play the baseline tiered bot: walking sideways beats it.
2. Play Whetstone: it finds the weakness and drills it; the panel shows why.
3. New situation: load a champion it has never seen; it derives the combo and uses it.
4. Inject a fault: the breaker trips to safe mode.
5. Debrief and before/after exam score.

## Evidence plan

1. **Difficulty band.** Scripted players of known skill against each bot.
   Whetstone should hold the target success band; fixed tiers should not.
2. **Situation coverage.** Share of the situations exam opponents create that
   each bot ever presents in practice.
3. **Transfer.** Identical learning agents practise against the baseline or
   Whetstone for the same time, then take the held-out exam. Includes an
   ablation with random instead of adaptive drills.
4. **Human pilot.** Pre-test, randomised practice block, post-test, run on other
   hackers.
5. **Fairness.** Counterfactual replays showing the bot never reacts before its
   reaction delay or to hidden information.
6. **Failure.** Fault injection showing the breaker trips, and how often it
   trips falsely.

## Limits to say out loud

- Human transfer is unproven. Simulated learners show the mechanism only, and
  the pilot will be small.
- The exam opponents are scripted stand-ins for PvP, designed by the same team
  as the bot. A judge can fairly push on that.
- A 1v1 duel covers laning fundamentals, not macro play.
- Some players use bot games to relax; this is an opt-in practice mode, not a
  replacement for Co-op vs. AI.

## The mentor's five questions

1. **Who is it for, and what do they do differently?** See above.
2. **Engines ship agent integrations; what does this add?** A role and a
   measurement: a bot whose objective is transfer, with the harness to test it.
3. **Can it pass by changing the test?** The exam uses opponents and champions
   held out of practice, and the bot never sees exam results.
4. **Where is the LLM, and what if it is slow or wrong?** Outside the frame
   loop, writing the debrief only; validated, with a template fallback.
5. **How does it fail, and what does the player see?** Breaker to a scripted
   bot with an on-screen notice; a staircase floor so it cannot farm the player.

## Alternatives considered

- **Bot that fills in for a VALORANT leaver.** Real need, but competitive
  integrity concerns and no way to show evidence in a toy environment.
- **Designer tool for debugging bot behavior.** Useful, but closer to track 2
  and a weaker player story.

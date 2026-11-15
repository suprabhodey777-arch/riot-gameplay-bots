# Evidence: simulated learners

N = 120 simulated players, each with one planted weak skill (competence 0.15, the others 0.7). Practice budget 20 drills per condition; exam = 5 fixed held-out forms (10 drills per skill), same drills before and after. Gains are percentage points of exam pass rate, mean with 95% bootstrap CI over players.

**The learners are simulated and the learning rule is ours (a made-up rule: a drill raises its skill by 0.04 x how close its difficulty is to the player's level). This shows the mechanism works end to end, not that humans transfer.**

| Condition | Exam gain, weak skill (pp) | Exam gain, overall (pp) | Practice drills on weak skill | Practice time on weak skill |
| --- | --- | --- | --- | --- |
| baseline | 4.3 [2.7, 5.9] | 1.5 [1.0, 1.9] | 24.7% | 29.7% |
| random | 5.5 [3.8, 7.3] | 1.4 [1.0, 1.9] | 25.5% | 30.7% |
| targeted | 9.7 [7.3, 12.2] | 2.4 [1.9, 3.1] | 67.9% | 68.7% |

Targeted minus baseline: weak skill 5.4 [3.9, 6.9] pp, overall 1.0 [0.6, 1.4] pp.
Targeted minus random-with-staircase: weak skill 4.2 [3.0, 5.3] pp, overall 1.0 [0.7, 1.3] pp.

By planted weak skill (mean weak-skill exam gain, pp; baseline / random / targeted):

| Planted weakness | Players | baseline | random | targeted | Weak competence after (targeted) |
| --- | --- | --- | --- | --- | --- |
| entry | 30 | -1.0 | 1.7 | 9.0 | 0.42 (from 0.15) |
| clutch | 30 | 0.0 | 0.0 | 0.0 | 0.37 (from 0.15) |
| time_pressure | 30 | 18.0 | 20.3 | 29.7 | 0.46 (from 0.15) |
| smoke_usage | 30 | 0.0 | 0.0 | 0.0 | 0.40 (from 0.15) |

Clutch caveat: the exam's clutch drills are 1v2 and 1v3 post-plant fights, and the simulated player almost never wins one at those difficulties even with the weak skill trained (about 0% before, 3% after on Saltworks, the three-site map), so the exam barely moves on clutch; the staircase trains it at 1v1 where it can. Saltworks is a long map: the late-plant and late-defuse clocks are stretched by the extra walking time (`longWalkS` in `analyzer/drills.ts`), without which the time-pressure exam is almost all failures. Rehearsal drills are not part of this comparison (`rehearsalsFirst: false`).

The analyzer put the planted weak skill first for 94.2% of players (from 6 synthetic matches each). Mean pre-test pass rate on the weak skill: 8.4%.

Fairness: 137008 fire inputs checked over 26400 drills; 0 earlier than the shooter's reactionMs after first sight (must be 0).


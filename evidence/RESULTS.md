# Evidence: simulated learners

N = 30 simulated players, each with one planted weak skill (competence 0.15, the others 0.7). Practice budget 20 drills per condition; exam = 5 fixed held-out forms (10 drills per skill), same drills before and after. Gains are percentage points of exam pass rate, mean with 95% bootstrap CI over players.

**The learners are simulated and the learning rule is ours (a made-up rule: a drill raises its skill by 0.04 x how close its difficulty is to the player's level). This shows the mechanism works end to end, not that humans transfer.**

| Condition | Exam gain, weak skill (pp) | Exam gain, overall (pp) | Practice drills on weak skill | Practice time on weak skill |
| --- | --- | --- | --- | --- |
| baseline | 4.7 [1.7, 7.7] | 2.0 [0.8, 3.3] | 23.5% | 29.3% |
| random | 7.0 [3.7, 10.3] | 2.0 [1.2, 2.8] | 25.0% | 32.0% |
| targeted | 15.0 [10.7, 20.0] | 4.0 [2.8, 5.2] | 68.5% | 64.7% |

Targeted minus baseline: weak skill 10.3 [6.7, 14.0] pp, overall 2.0 [0.5, 3.3] pp.
Targeted minus random-with-staircase: weak skill 8.0 [4.0, 12.0] pp, overall 2.0 [1.0, 3.1] pp.

By planted weak skill (mean weak-skill exam gain, pp; baseline / random / targeted):

| Planted weakness | Players | baseline | random | targeted | Weak competence after (targeted) |
| --- | --- | --- | --- | --- | --- |
| entry | 7 | -1.4 | 5.7 | 12.9 | 0.44 (from 0.15) |
| clutch | 8 | 1.3 | 1.3 | 2.5 | 0.38 (from 0.15) |
| time_pressure | 8 | 16.3 | 17.5 | 28.7 | 0.49 (from 0.15) |
| smoke_usage | 7 | 1.4 | 2.9 | 15.7 | 0.40 (from 0.15) |

Clutch caveat: the exam's clutch drills are 1v2 and 1v3 post-plant fights, and the simulated player almost never wins one at those difficulties even with the weak skill trained (about 0% before, 3% after on Saltworks, the three-site map), so the exam barely moves on clutch; the staircase trains it at 1v1 where it can. Saltworks is a long map: the late-plant and late-defuse clocks are stretched by the extra walking time (`longWalkS` in `analyzer/drills.ts`), without which the time-pressure exam is almost all failures. Rehearsal drills are not part of this comparison (`rehearsalsFirst: false`).

The analyzer put the planted weak skill first for 96.7% of players (from 6 synthetic matches each). Mean pre-test pass rate on the weak skill: 9.0%.

Fairness: 21705 fire inputs checked over 6600 drills; 0 earlier than the shooter's reactionMs after first sight (must be 0).


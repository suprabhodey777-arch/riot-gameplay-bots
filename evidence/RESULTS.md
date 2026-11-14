# Evidence: simulated learners

N = 30 simulated players, each with one planted weak skill (competence 0.15, the others 0.7). Practice budget 20 drills per condition; exam = 5 fixed held-out forms (10 drills per skill), same drills before and after. Gains are percentage points of exam pass rate, mean with 95% bootstrap CI over players.

**The learners are simulated and the learning rule is ours (a made-up rule: a drill raises its skill by 0.04 x how close its difficulty is to the player's level). This shows the mechanism works end to end, not that humans transfer.**

| Condition | Exam gain, weak skill (pp) | Exam gain, overall (pp) | Practice drills on weak skill | Practice time on weak skill |
| --- | --- | --- | --- | --- |
| baseline | 1.7 [0.3, 3.0] | 2.2 [1.4, 2.9] | 23.5% | 28.3% |
| random | 4.0 [2.0, 6.0] | 1.5 [1.0, 2.1] | 25.0% | 30.9% |
| targeted | 8.3 [5.0, 11.7] | 2.4 [1.6, 3.2] | 67.8% | 66.6% |

Targeted minus baseline: weak skill 6.7 [3.7, 10.0] pp, overall 0.3 [-0.7, 1.3] pp.
Targeted minus random-with-staircase: weak skill 4.3 [1.7, 7.3] pp, overall 0.9 [0.1, 1.7] pp.

By planted weak skill (mean weak-skill exam gain, pp; baseline / random / targeted):

| Planted weakness | Players | baseline | random | targeted | Weak competence after (targeted) |
| --- | --- | --- | --- | --- | --- |
| entry | 7 | 1.4 | 7.1 | 7.1 | 0.45 (from 0.15) |
| clutch | 8 | 0.0 | 0.0 | 0.0 | 0.38 (from 0.15) |
| time_pressure | 8 | 3.7 | 6.2 | 17.5 | 0.44 (from 0.15) |
| smoke_usage | 7 | 1.4 | 2.9 | 8.6 | 0.40 (from 0.15) |

Clutch caveat: on the placeholder test-box map a 1v2 or 1v3 in open sight is almost unwinnable at the exam difficulties whatever the simulated player's competence, so the exam cannot show a clutch gain; the staircase trains it at 1v1 where it can.

The analyzer put the planted weak skill first for 93.3% of players (from 6 synthetic matches each). Mean pre-test pass rate on the weak skill: 13.7%.

Fairness: 25106 fire inputs checked over 6600 drills; 0 earlier than the shooter's reactionMs after first sight (must be 0).


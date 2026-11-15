# Showcase video: 90 seconds

**One message:** ValoDrill finds the moment you keep losing, makes you replay it against bots that play like people, coaches you, and measures whether you improved.

Rules for the cut:
- Every shot shows a feature that helps a player get better. No settings or menus for their own sake.
- No shot longer than 6 s.
- On-screen captions carry the story, so there's no voice-over to record. Music plus game sound.
- 1920×1080, 60 fps, the HUD visible in gameplay shots.

## Shot list

| # | Time | Length | Shot | Caption |
|---|---|---|---|---|
| 1 | 0:00 | 4 s | First person: the player peeks North Causeway and dies instantly to an enemy holding the angle | "You keep losing the same fight." |
| 2 | 0:04 | 4 s | Black frame, logo, title | "ValoDrill: practice built from your own mistakes" |
| 3 | 0:08 | 8 s | Weakness report: "Entry duels" as the top finding, severity bar filling, evidence lines | "It reads your matches and names the weakness, with proof." |
| 4 | 0:16 | 5 s | Rehearsal card: "Round 2: you lost the opening duel at North Causeway 0.6 s after contact" | "Then puts you back in that exact moment." |
| 5 | 0:21 | 6 s | Attempt 1: same spot, same clock, the player swings wide and dies again | "Same angle. Same clock." |
| 6 | 0:27 | 7 s | Result card: the coach's debrief text appears, the difficulty staircase moves | "A coach explains what went wrong, after the round, never during it." |
| 7 | 0:34 | 7 s | Attempt 2: the player pre-aims the angle, wins the duel, hit marker and headshot sound | "Try again until it sticks." |
| 8 | 0:41 | 8 s | Slow motion, side view of a bot: it spots the player, pauses, turns, misses its first shot, then hits | "Bots react like people: about 250 ms, human turn speed, no wallhacks." |
| 9 | 0:49 | 5 s | Bot thought line over its head ("holding A main", "heard footsteps") | "And they show their thinking, so you learn why you lost." |
| 10 | 0:54 | 6 s | Persona select (flick duellist, anchor, lurker), then a quick clip against one | "Practise against the style that beats you." |
| 11 | 1:00 | 12 s | Montage on Meridian: fly-over, buy phase, a flash going off (white-out and afterimage), a smoke, a round won | "Then take it into a full 5v5 match." |
| 12 | 1:12 | 10 s | Evidence chart: weak-skill gain, targeted vs baseline vs random practice (+4.8 points vs baseline) | "Targeted practice improved the weak skill 2–3× more (120 simulated players)." |
| 13 | 1:22 | 8 s | Logo, tagline, DevHacks '26 | "ValoDrill. Lose once. Learn it. Win it." |

Total: 90 s.

## How each shot gets recorded
- **Shots 1, 4–7:** Practice → sample player "weak entry" → Rehearse → round 2. Record attempts 1 and 2 as separate takes.
- **Shot 3:** the report screen right after loading the sample. Record a slow scroll.
- **Shots 8–9:** a drill with the camera set to watch a bot. If there's no spectator camera, record from the player and slow it down in the edit.
- **Shot 10:** Favourite streamer → persona select → duel.
- **Shot 11:** Play match on Meridian. Fly-over from the menu backdrop camera or a scripted camera path.
- **Shot 12:** a chart made from `evidence/RESULTS.md`, rendered as a 1080p image and animated in the edit.
- **Shots 2 and 13:** title cards made in the editing tool.

## Before recording
- [ ] Recording setup: Unity Recorder (package) or OBS capturing the Game view at 1080p60
- [ ] Bridge running; bot difficulty on the setting that makes the rehearsal winnable on take 2
- [ ] Hide the Unity editor chrome: Game view maximised, or a standalone build
- [ ] Keep the M map out of frame, or fix its cone first (the cone points the wrong way after turning)

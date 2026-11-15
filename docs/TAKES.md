# Recording script: what to play in each take

**How to record:** in Unity's Play mode, press **F10** to start recording and **F10** again to stop. Each take saves to `riot-unity/recordings/take_HHMMSS.mp4` (1080p, 60 fps, with sound). Do several tries of a take if you need to; I'll pick the best one.

**Before you start:**
- The bridge must be running (I'll keep it up), and the Game view should be maximised: double-click the Game tab, or Shift+Space.
- **Don't** open the M map during takes. Its view cone still points the wrong way after you turn.
- **Don't** open Settings on camera.
- Move the mouse smoothly, and pause 1 second at the start and end of each take (it gives me room to cut).

---

## Take 1: The hook (0:00–0:06), about 10 s
- **Path:** Menu → **Rehearse** → pick the *North Causeway* opening-duel moment (round 2) → **Begin drill**.
- **Play:** walk straight out and peek the angle *without* pre-aiming. Die fast.
- **Goal:** a clean, frustrating death. Do 2–3 tries; I'll use one or two of them back to back.

## Take 2: The weakness report (0:12–0:20), about 15 s
- **Path:** Menu → **Practice** → **Sample** (the one with the weak entry) → **Load**. The report opens.
- **Play:** don't click anything for 3 s, then scroll slowly through the top finding (Entry) and its evidence lines. Hover over **Rehearse** at the end without clicking.
- **Goal:** the report with "Entry" as the worst skill and the proof under it.

## Take 3: The rehearsal, attempt 1, losing (0:20–0:30), about 20 s
- **Path:** from the report, click the rehearsal for round 2 → the briefing card shows ("you lost the opening duel at North Causeway…"). Hold on it for 3 s → **Begin drill**.
- **Play:** same mistake as Take 1: swing wide and die. Stay on the **result screen** for 6 s so the coach's debrief and the difficulty change are readable.
- **Goal:** briefing, then the loss, then the coach explaining why.

## Take 4: The rehearsal, attempt 2, winning (0:30–0:44), about 20 s
- **Path:** on the result screen → **Retry this drill**.
- **Play:** pre-aim the corner, walk (Shift) rather than run, peek and **hit the headshot**. Win the duel. Stay on the result screen for 4 s.
- **Goal:** a clean win with a hit marker. Do as many tries as it takes; set bots to **Easy** in the drill setup if needed. That's fine to show, and the report will reflect it.

## Take 5: The report after (0:44–0:48), about 8 s
- **Path:** result screen → **Back to report**.
- **Play:** just hold. The Entry finding should show the updated numbers.
- **Goal:** proof the loop closes: you practised, and the report changed.

## Take 6: Fair bots (0:48–0:58), about 25 s
- **Path:** Menu → **Practice** → **Next practice drill** (any entry drill) → **Begin drill**.
- **Play:**
  - Walk slowly toward a bot so its **thought line** above its head is readable (for example, "holding", "heard footsteps").
  - Then let it see you: stand still in the open for a moment. It should take a beat before shooting.
  - Then run past it once (running is heard through walls).
- **Goal:** the bot's visible reasoning and its human delay before the first shot. I'll slow it down and add a timer in the edit.

## Take 7: Persona duel (rapid-fire), about 15 s
- **Path:** Menu → **Favourite streamer** → pick the *flick duelist* → **Start duel**.
- **Play:** one duel, win or lose. It just has to look intense.

## Take 8: Match round on Meridian (rapid-fire), 60–90 s
- **Path:** Menu → **Play match** → map **Meridian**, any agent → **Start match**.
- **Play:**
  1. **Buy phase:** open the buy menu, buy a rifle and armour → **Ready up**.
  2. **Round:**
     - throw a **smoke** (hold C, release on a choke)
     - throw a **flash** (hold Q) around a corner **toward enemies**, so I get bots dazed or you whited out
     - use **dash** (E)
     - throw a **recon dart** (hold X) onto a site
  3. Fight. If you're attacking, plant the spike (hold F).
- **Goal:** lots of variety in one round. Several short clips are fine.

## Take 9: Extra flash shot (optional), about 10 s
- **Play:** in any drill or match, throw a flash at a wall right in front of you and look at it, so the full white-out and afterimage show.

---

**Done when:** takes 1–8 are in `recordings/`. Tell me when they are. Meanwhile I'll build:
- the title cards
- the captions
- the animated evidence chart
- the closing card
- a terminal capture of `node evidence/run.ts` rerunning the results

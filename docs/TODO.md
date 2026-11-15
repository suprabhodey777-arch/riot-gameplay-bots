# Unity game: to-do list

Work happens in this checkout: `C:\Users\Pratham\Documents\riot-unity`, branch `feat/unity-3d`, Unity 6000.6.9f1.
`ghost/build` (the old browser version) is only a reference; we are not building it.
Nothing here is committed yet. Ask before committing.

## How to resume
1. Start the bridge: `npm run bridge` (port 7777).
2. Open the project: `unity open C:/Users/Pratham/Documents/riot-unity/unity`.
3. Compile check for C# changes: `unity recompile --project-path C:/Users/Pratham/Documents/riot-unity/unity` (no rows = clean).
4. TypeScript checks: `npx vitest run` and `npx tsc --noEmit`.

## Already done
- [x] Unity CLI installed, signed in, editor 6000.6.9f1.
- [x] Input popup fixed: `activeInputHandler: 2` (Both) in `unity/ProjectSettings/ProjectSettings.asset`. Worth committing so teammates stop seeing it.
- [x] Unity control package `com.unity.pipeline` added to `unity/Packages/manifest.json`, so the editor can be driven from the command line.
- [x] Open-licence fonts in `unity/Assets/Resources/Fonts/`: Barlow Condensed (Medium/SemiBold/Bold/ExtraBold) and Barlow (Regular/Medium/SemiBold/Bold), with OFL licence files.
- [x] Skill `game-art-polish` installed at `~/.claude/skills/game-art-polish`, 3D parts only. Removed `game-feel`, which is aimed at 2D.
- [x] Investigations finished: the map pipeline and the UI/minimap/flash code (results summarised below).

## Fixes
- [ ] **Match mode doesn't work.** The investigation was stopped before it finished, so rerun it. Trace the flow: client `match` message → `bridge/server.ts` (match/buy/sell/ready/abandon) → `session/match-game.ts` → Unity `GameController.cs` and `ClientUI.Match.cs`.
- [ ] **Bots react inhumanly fast.** Find where `reactionMs` is turned into ticks (`bot/bot.ts`, `bot/traits.ts`, `bot/view.ts`). Check whether aim snaps instantly and whether the first shot can land on the same tick the bot spots you. Target human numbers: reaction 220–350 ms, a limit on how fast bots can turn, and an aim-settle delay before accurate shots.
- [ ] **Minimap** (`UI/Minimap.cs`). The rotation and axis maths check out; these are the real defects:
  - Holding Tab, then pausing, drops the full map (`GameController.cs:498`).
  - The minimap keeps updating while paused (`GameController.cs:499`).
  - The view-cone angle doesn't match the camera's field of view (`Minimap.cs:158`, `:353`).
  - The corner map shows a fixed 32 m window instead of scaling to the map (`:232`).
  - The cone length isn't drawn to scale (`:354-355`).
  - Nothing is drawn at all before the map has loaded (`:218`).
  - `GL.Viewport` isn't put back to its previous value, so the overlay can bleed (`:239`, `:269`).
  - The opacity setting only half works (`:38`, `:242`, `:262-266`).
  - Site labels depend on the state left by the last draw (`:206-210`).
  - The same transform is written twice and could drift apart (`:199-204` vs `:251-255`).
  - Default to north-up with a rotating player arrow, and keep the rotating map as an option.
- [ ] **Flash should look right.** The sim already scales blindness by facing: 1.75 s if you're looking at it, 0.5 s if it's behind you within range (`sim/sim.ts:21-24`, `:294`). The client overlay is a flat white square (`ClientUI.cs:858-864`, curve in `GameController.cs:692-704`). Build:
  - a fast fade-in of about 0.08 s
  - full white for about 40% of the blind time
  - a smooth ease-out
  - an afterimage: freeze the frame captured at the pop and fade it out more slowly than the white
  - the HUD dimmed while blind
  - a weaker, shorter version when the flash was behind you
  
  Put it in a new `UI/FlashOverlay.cs`. Check whether the `flash` event fires at the throw or at the pop (`GameController.cs:1173` assumes the throw).
- [ ] **Sound effects sound ridiculous.** Every sound is synthesized in code (`World/Look.cs`, `Synth.*`). Replace them with CC0 recordings: guns per weapon class, reload, dry-fire, footsteps on different surfaces, hits on bodies and walls, the headshot sound, UI hover/click/confirm/cancel, round start/end, the flash pop and the ringing after it. Add a mixer with MusicVol/SFXVol.
- [ ] Other bugs the investigation found:
  - HUD elements overlap on narrow windows (`ClientUI.cs:890-891`).
  - Text piles up in the centre of the screen (`ClientUI.Match.cs:695`, `ClientUI.cs:964-966`).
  - A null-reference crash is possible in `DrawHud` when the camera doesn't exist yet (`ClientUI.cs:835`).
  - The shared GL material is changed by the minimap (`Draw2D.Mat`).
  - "Reset everything" doesn't reset ShowFps.

## UI rebuild (must not look AI-made)
- [ ] UI is IMGUI only (`UI/ClientUI*.cs`, `Theme.cs`). Plan: keep IMGUI and restyle it, rather than rewriting everything in UI Toolkit.
- [ ] Rules:
  - Real fonts: Barlow Condensed for numbers and headings, Barlow for body text. No default engine font.
  - Square or near-square corners and thin 1 px lines. No rounded web-style cards, no gradients, no purple-blue.
  - Terse uppercase labels, with no explanatory sentences on the HUD or the pause screen.
  - Clear hierarchy, team colours from `World/Look.cs`, one accent.
  - Motion: about 120 ms on hover, about 200 ms between screens, a lagging chunk when health drops, kill feed rows slide in.
  - Sound on every UI action.
- [ ] Base it on real shipped shooter UIs (Game UI Database screenshots), measured for layout, sizes and spacing. Don't copy any one game's art.
- [ ] Use the `game-art-polish` skill's `references/ui-toolkit.md` menu-polish checklist (it fails if 3 or more items are missing) and Unity's `craft-game-floors` visual scorecard as the done gate. Judge it from real Unity screenshots, not HTML mockups.
- [ ] Add an in-game scoreboard on Tab, and move the full map to another key. There isn't one now; Tab only shows the full map.
- [ ] First-person feel goes on the gun model, never the camera: kick when firing, sway, walk bob, a dip when landing or crouching, a hit flash at the screen edge toward the shooter. No camera shake that moves your aim.

## New map
- [ ] **Meridian**: an ORIGINAL three-site map. It must not copy Haven's layout or look. The worker was stopped before writing any files.
  - 120×84 m, attackers in the west, defenders in the east.
  - Staggered sites:
    - A in the north-east (about x78–92, y62–76)
    - B in the centre, nearer mid (about x68–82, y36–48)
    - C in the south-east (about x90–102, y8–20)
  - Lanes: North to A, the central "Dish Court" with two entrances to B, South to C.
  - Connectors on the attacker half: "Relay Hall" and "Culvert", a 3 m corridor.
  - A rear corridor for defenders.
  - Walk times: 12–16 s from the attacker spawn to each site, 3–9 s from the defender spawn.
- [ ] Files: `scripts/build-meridian.ts` (follow `build-saltworks.ts`), `shared/maps/meridian.json`, the registry in `shared/maps/index.ts` (imports at 5-7, registry at 34-38, PLAYABLE_MAPS at 62), and `shared/maps/meridian.test.ts`. The test should mirror `saltworks.test.ts`, plus bots planting on all 3 sites and the walk-time checks.
- [ ] Rules: boundary wall ids start with `outer`, wall heights are 1.2 / 3 / 4, and callouts don't contain spawns.
- [ ] Unity: add a menu blurb at `ClientUI.cs:467-469`, and a colour for site C (`Look.cs:49` currently falls back to green).

## 3D models and animation
- [ ] Guns: stylised (not low-poly, not realistic) with the general look of a modern hero shooter. Don't model them from Riot's gun designs. Use free CC0/CC-BY models first, and build our own only if nothing fits. Classes needed: pistol, SMG, rifle, sniper, shotgun. Candidates: Quaternius 50 guns, the 3dmodelscc0 pack, OpenGameArt CC0 weapons.
- [ ] Characters: free rigged humanoids on the Quaternius universal rig (CC0). Improve the whole model set.
- [ ] Animations: Quaternius Universal Animation Library 1 and 2 (CC0), for movement, crouch and death. Check whether they include rifle poses; if not, layer an aim pose over the upper body.
- [ ] Re-shade everything with one stylised look: soft gradient lighting, crisp edges, painted colours. Show a side-by-side before switching.
- [ ] Downloads: a partial run is in `asset-staging/`, which isn't imported. Rerun it. Some itch.io pages need a manual click in a browser.
- [ ] Every asset goes into `CREDITS.md` with its source page and licence.

## Feature: bots copied from real players (the handoff's main idea)
- [ ] 1. Record a `sample` event 4 times a second for the human player (position, yaw, health, weapon, inputs, what they can see) in `session/recorder.ts` and `match-game.ts`. Save logs to disk at `logs/<player>/<match>.json`.
- [ ] 2. `bot/profile.ts`: measure the 7 BotTraits from real logs (reaction time from `spotted` to first shot, head and body accuracy from damage events, aggression, patience, utility use) and feed them to `createBot`.
- [ ] 3. `bot/ghost.ts`: copies decisions by finding the most similar recorded situations, using the same `decide(state)` interface. Falls back to the normal bot with the player's measured style when nothing recorded is similar, or when it gets stuck.
- [ ] 4. Wire it into the game: a `versus_ghost {player}` message, "Rehearse my mistakes" able to use a ghost, a "Play vs ghost" button, and a ghost tag on the bot's thought line.
- [ ] Note: the game is single-player against bots, so ghosts come from human recordings (yours or a teammate's).
- [ ] Also from the old version, lower priority: a full panel showing the bot's reasoning, and replaying a finished round.

## Look of the 3D world (from the game-art-polish skill)
- [ ] Post-processing and lighting: bloom on bright sources, one colour grade, a light vignette, a warm key light plus a cool fill. The project is on the built-in render pipeline, so check what's available before planning URP Volumes.
- [ ] Check palette discipline across walls, crates and props (`World/Look.cs`, `WorldLook.cs`) so new models and old geometry look like one game.

## Docs and housekeeping
- [ ] Docs for the Unity game: `unity/README.md` says Unity 6000.0.30f1, but the project is 6000.6.9f1. Update the README: how to run, controls, maps, and how the bots and coach work.
- [ ] Old repo `C:\Users\Pratham\Documents\riot-gameplay-bots` (branch `ghost/build`) has uncommitted work from earlier (bug fixes, characters, `docs/DEVPOST.md`, `docs/UNITY.md`, a half-finished movement change with 3 failing tests). Decide whether to commit it to `ghost/build` as an archive or leave it. `docs/UNITY.md` was written for the old version and doesn't match the Unity game's bridge design.
- [ ] Decide what to commit to `feat/unity-3d`, and when. Ask the teammate before pushing to their branch.

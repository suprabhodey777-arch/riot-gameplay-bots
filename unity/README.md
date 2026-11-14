# Saltworks 3D client (Unity)

First-person client for the coaching bridge. Unity only draws, takes input and raycasts shots; the sim, the bots,
the weakness analyzer, the staircase and the coach all run in the bridge (TypeScript). Protocol: `bots-wire.2`,
see `docs/unity-bridge.md`.

- Unity **6000.0.30f1** (any Unity 6000.0.x should work), built-in render pipeline.
- Packages (from `Packages/manifest.json`): Newtonsoft Json, Input System, uGUI and the standard modules.
- No scenes, prefabs or art assets: everything is built from code at runtime.

## Run it

1. Start the bridge from the repository root (it listens on `127.0.0.1:7777`, env `BRIDGE_PORT` overrides on both sides):

   ```sh
   npm run bridge
   ```

2. Open the `unity/` folder in Unity Hub (Add > Add project from disk) with Unity 6000.0.x.
   On first open Unity may ask whether to enable the new Input System backends. Either answer works: the code
   supports both the Input System package and the legacy Input Manager.
3. Press **Play** in whatever scene is open (the default empty scene is fine). A bootstrap
   (`[RuntimeInitializeOnLoadMethod]` in `Assets/Scripts/Core/Bootstrap.cs`) builds the camera, lights, map and UI.
4. If the bridge is not running you will see "Start the bridge: npm run bridge" and a Reconnect button.

For a player build, run the menu item **Saltworks > Save Main Scene and Add to Build Settings** once. It saves
`Assets/Scenes/Main.unity`, puts it first in Build Settings and writes small materials into
`Assets/Resources/SaltworksShaderRefs/` so the built-in shaders found at runtime with `Shader.Find` are included in
the build. Pressing Play in the editor does not need this step.

## Flow

1. Connect: the client sends `hello` and builds the map from the `hello` reply.
2. Choose a sample player (weak entry, clutch, time pressure or smoke usage; seed 11), which sends `load_history`.
3. Weakness report: findings with severity bars, rate against benchmark, summaries and evidence. From here:
   "Rehearse my mistakes" (one button per rehearsal, hidden when there are none), "Next practice drill",
   or "Versus an archetype" (pick a skill, then one of five archetypes).
4. Drill intro card: press Begin (or Enter). Inputs start only now, one every 50 ms.
5. Result: verdict, headline, staircase before -> after, what happened, the coach's debrief, Next drill / Back to report.
   The report screen refreshes whenever the bridge sends a new report.

## Controls

| Key | Action |
| --- | --- |
| Mouse | Look (yaw is sent to the sim; pitch stays in Unity) |
| W A S D | Move, relative to where you look |
| Shift | Walk (quiet: running is heard 15 m away through walls) |
| Ctrl | Crouch (eye 1.1 m instead of 1.6 m, quiet) |
| Left click | Fire (at most once per `rules.fireIntervalS`) |
| C | Smoke: hold to aim at the floor, release to throw |
| Q | Flash: hold to aim, release to throw |
| E | Dash |
| X | Recon: hold to aim, release to throw |
| Hold F | Plant (attack, on a site, carrying the spike) or defuse (defense, near the planted spike) |
| M / hold Tab | Toggle the minimap / show the full map |
| F3 | Frame rate, frame time and prediction error |
| Esc | Pause (no inputs are sent, so the sim waits), settings, resume or abandon the drill |
| Enter | Begin the drill from the intro card |

## Code layout

| File | What it does |
| --- | --- |
| `Assets/Scripts/Net/Protocol.cs` | Wire DTOs and message builders (Newtonsoft) |
| `Assets/Scripts/Net/BridgeClient.cs` | TCP + NDJSON on a background thread, main-thread queue |
| `Assets/Scripts/Core/Bootstrap.cs` | Creates the game after the scene loads |
| `Assets/Scripts/Core/GameController.cs` | Screens, message handling, the 50 ms input loop, kill feed |
| `Assets/Scripts/Core/GameInput.cs` | One facade over the Input System and the legacy Input Manager |
| `Assets/Scripts/World/MapBuilder.cs` | Extrudes walls (ear clipping), floor, sites, callout labels |
| `Assets/Scripts/World/WorldView.cs` | Bots, smokes, spike, tracers; interpolation between steps |
| `Assets/Scripts/World/PlayerRig.cs` | First-person camera, viewmodel, shot raycast, smoke aim |
| `Assets/Scripts/World/Look.cs` | Palette, runtime materials, procedural textures and sounds |
| `Assets/Scripts/UI/ClientUI.cs` | All screens and the HUD in IMGUI |
| `Assets/Resources/SaltworksWorldText.shader` | Depth-tested text for floor labels |
| `Assets/Editor/BuildMenu.cs` | The Saltworks menu (scene + build settings) |

The look is original: pastel concrete, attack red-orange, defense teal. It borrows no map, agent, weapon, logo
or UI art from any game.

## Building the Mac app

```bash
/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath unity -executeMethod CommandLineBuild.MacApp -logFile -
```

The app lands in `unity/Build/GameplayCoach.app`. Start the bridge first (`npm run bridge`).

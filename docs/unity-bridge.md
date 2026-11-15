# Unity bridge: wire protocol `bots-wire.1`

The Unity 3D client is only the eyes, ears and hands. Everything that decides
anything (the sim, the bots, the weakness analyzer, rehearsals, the staircase,
the coach) stays in TypeScript and runs in the bridge, a Node process on the
same machine. This keeps one source of truth: a drill played in Unity is the
same drill, judged by the same rules, as one played in the 2D client or the
evidence harness.

```
Unity (C#)  ── TCP 127.0.0.1:7777, one JSON object per line ──  bridge/ (Node, TS)
  render, input, raycast shots, HUD, menus                       CoachingSession, startDrill, recorder
```

## Transport

- TCP, `127.0.0.1`, port `7777` (env `BRIDGE_PORT` overrides). No WebSocket,
  no extra packages on either side: Node's `net`, .NET's `TcpClient`.
- Newline-delimited JSON (NDJSON), UTF-8. Every message is one object with a
  `type` string. No message contains a raw newline.
- One connection = one player = one `CoachingSession`. The bridge serves
  connections one after another or concurrently; they share nothing.
- The bridge never steps the sim on its own clock. **One `input` message = one
  sim tick (50 ms at 20 Hz).** The client sends one `input` every 50 ms of real
  time while a drill is live; the bridge answers each with one `step`. This keeps
  the sim deterministic and lets the client pause by simply not sending.

## Coordinates (read this before writing any C#)

The sim is 2D, metres. Unity is 3D, metres.

| sim | Unity |
| --- | --- |
| `{ x, y }` | `new Vector3(x, 0, y)` (y up is height) |
| `yaw` radians, 0 = +x, counter-clockwise, direction `(cos yaw, sin yaw)` | `transform.eulerAngles.y = 90 - yaw * Mathf.Rad2Deg` |
| Unity forward `f` (flattened) | `yaw = Mathf.Atan2(f.z, f.x)` |
| wall polygon `points` + `height` | extruded prism from y=0 to `height` |
| site / callout polygon | flat decal or outline at y≈0.01 |

Pitch exists only in Unity (aiming up and down); the sim never sees it.
Eye height: 1.6 m standing, 1.1 m crouched. Bot capsule: radius 0.4 m, height
1.8 m; head is the top 0.3 m (a sphere collider of radius 0.15 at y=1.65).

## Client → bridge

```jsonc
{ "type": "hello", "protocol": "bots-wire.1" }

// Pick whose history to coach. "sample" builds one of the built-in sample
// players (analyzer/synthetic.ts sampleHistory). "logs" sends real MatchLogs.
{ "type": "load_history", "sample": { "skill": "entry", "seed": 11 } }
{ "type": "load_history", "logs": [ /* MatchLog[] */ ] }

// The next practice drill: a rehearsal of a failed moment first, then
// generated drills at the staircase difficulty (CoachingSession.nextDrill).
{ "type": "next_drill" }

// One specific rehearsal by index into report.rehearsals.
{ "type": "rehearse", "index": 0 }

// A free round against one bot archetype (versus mode), not coaching practice.
{ "type": "versus", "archetype": "entry-duelist", "skill": "entry" }

// One tick of the player's input. Same shape as contract.ts PlayerInput,
// agentId may be omitted (the bridge fills in the drill's player id).
{ "type": "input", "input": {
    "move": { "x": 0, "y": 1 }, "yaw": 1.57, "walk": false, "crouch": false,
    "fire": { "targetId": "bot-0", "result": "head" },   // or null
    "smokeAt": null, "plantHeld": false, "defuseHeld": false } }

// Give up on the live drill (counts as a fail, debrief still written).
{ "type": "abandon" }
```

`fire` is decided by the client: it raycasts from the camera through the
crosshair against walls and bot colliders and reports what it hit
(`head`, `body`) or `miss` with the nearest bot it was aimed at. Send at most
one shot every `rules.fireIntervalS` seconds.

## Bridge → client

```jsonc
{ "type": "hello", "protocol": "bots-wire.1", "map": /* MapDef */,
  "rules": { "tickRate": 20, "fireIntervalS": 0.15, "smokeMaxRangeM": 25,
             "plantTimeS": 4, "defuseTimeS": 7, "moveSpeedMps": 5.4 } }

// After load_history, and again after every finished drill (the report
// re-analyses the history plus every drill played so far, so it moves as the
// player improves).
{ "type": "report",
  "report": /* WeaknessReport */,
  "rehearsals": [ { "index": 0, "title": "...", "brief": "...", "skill": "clutch" } ],
  "staircases": { "entry": { "difficulty": 0.42 }, "clutch": { ... } },
  "played": 3 }

// A drill is starting. `bots` carries archetype names so the HUD can label
// them. `intro` is one sentence the client shows before play begins.
{ "type": "drill", "spec": /* DrillSpec */, "intro": "...",
  "playerId": "player", "playerTeam": "attack",
  "bots": [ { "id": "bot-0", "team": "defense", "archetype": "off-angle-anchor" } ],
  "state": /* SimState at tick 0 */ }

// Answer to each input.
{ "type": "step", "state": /* SimState */, "events": /* SimEvent[] */,
  "thoughts": { "bot-0": "hold_angle" },   // last behaviour-tree node per bot
  "done": false }

// Once, right after the step whose done is true.
{ "type": "result", "result": /* DrillResult */, "success": true,
  "headline": "Clutch won", "staircaseBefore": 0.40, "staircaseAfter": 0.45 }

// The coach's debrief, possibly a few seconds after `result` (LLM or template).
{ "type": "debrief", "text": "...", "source": "llm" | "template" }

{ "type": "error", "message": "..." }   // never closes the socket by itself
```

## Flow

```
hello ─► hello(map, rules)
load_history ─► report
next_drill | rehearse | versus ─► drill
input × N ─► step × N … step(done) ─► result ─► debrief ─► report
```

Sending `input` with no live drill answers `error`. Starting a drill while one
is live abandons the old one first.

## Round 2 additions (`bots-wire.2`, with contract v4)

Read `docs/abilities.md` first. `hello.protocol` becomes `bots-wire.2`; the bridge
still accepts a `bots-wire.1` hello and then simply never sends the new fields.

Client → bridge:

```jsonc
{ "type": "maps" }                                   // -> { "type": "maps", "maps": [{ "id": "ironvale", "name": "Ironvale" }, ...] }
{ "type": "pick_map", "map": "ironvale" }            // -> hello-shaped { "type": "map", "map": MapDef }; next drills use it
{ "type": "pick_agent", "agent": "rook" }            // rook | haze | wren | pike; -> { "type": "agent", "agent": "rook", "kit": { "flash": 1, "dash": 1, ... } }
// input gains optional fields (absent = not used this tick):
{ "type": "input", "input": { ..., "flashAt": { "x": 0, "y": 0 }, "dash": true, "reconAt": { "x": 0, "y": 0 } } }
```

Bridge → client:

- `hello.rules` gains `flashMaxRangeM`, `flashFuseS`, `flashBlindS`,
  `dashDistanceM`, `reconMaxRangeM`, `reconRadiusM`, `reconRevealS`,
  `runHearingM`, `walkSpeedMps`, `crouchSpeedMps`, and `agents`: the four
  agents with role and kit.
- `drill` gains `playerAgent` and, per bot, `agent`.
- `step.state.agents[]` carry `agent`, `abilities`, `blindUntilTick`,
  `revealedUntilTick` (contract v4); `step.events` carry the new events.
- History from a sample player is generated on the default map, but drills use
  the picked map: rehearsal positions are rebuilt only when the history was
  played on that map; otherwise the planner's generated drills are used.

## Ownership

| Path | Who | Notes |
| --- | --- | --- |
| `docs/unity-bridge.md` | Lead (Suprabho) | This file. Workers do not edit it; they report any gap. |
| `bridge/` + `npm run bridge` | Codex | Node/TS, imports the existing lanes, adds no dependencies. |
| `unity/` | Kiro | Unity 6 project, C#, built-in render pipeline. |
| everything else | Lead (Suprabho) | Workers do not edit it. |

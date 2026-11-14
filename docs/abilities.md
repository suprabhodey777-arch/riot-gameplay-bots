# Agents, abilities and footsteps (contract v4)

Our own agents and abilities. The names, kits and numbers are original; nothing
here is a VALORANT agent or ability. The point is that every situation the coach
trains (entry, clutch, time pressure, utility) can be practised with the tools a
tactical-shooter player actually reaches for: a smoke, a flash, a dash, a reveal,
and moving quietly.

## Agents

| Agent | Role | Kit (charges per round) | Plays like |
| --- | --- | --- | --- |
| **Rook** | duelist | Dash ×1, Flash ×1 | takes the first fight |
| **Haze** | controller | Smoke ×3 | cuts sight lines for the team |
| **Wren** | initiator | Flash ×2, Recon ×1 | finds and blinds defenders for an entry |
| **Pike** | sentinel | Recon ×2, Smoke ×1 | holds a site and watches a flank |

The player picks an agent before a drill. A drill may require one (a smoke-usage
drill gives Haze unless the player picked an agent with a smoke). Bots get the
agent that matches their archetype: entry-duelist → Rook,
utility-controller → Haze, patient-lurker → Pike, off-angle-anchor → Pike,
baseline → Wren.

## Abilities

All numbers are constants in `sim/sim.ts` and are sent to clients in the bridge
`rules`.

| Ability | Input | Effect |
| --- | --- | --- |
| **Smoke** (exists) | `smokeAt: Vec2` within 25 m | 4.5 m sphere that blocks sight for 15 s |
| **Flash** | `flashAt: Vec2` within 20 m | Pops 0.6 s after the throw (`FLASH_FUSE_S`). Every agent (both teams, the thrower too) with line of sight to the pop point within 30 m **and** facing it (within the 103° FOV) is blinded for 1.75 s (`FLASH_BLIND_S`), or 0.5 s if the pop is behind them but within 8 m. A blinded agent cannot spot anyone, and their shots are misses. |
| **Dash** | `dash: true` | Moves 6 m (`DASH_DISTANCE_M`) in the current move direction (or facing, if not moving) over 0.2 s, stopping at walls. Fires a `dash` event. |
| **Recon** | `reconAt: Vec2` within 30 m | After 0.5 s, reveals every enemy within 10 m of the point who has line of sight to it, for 2 s: the thrower's team counts them as spotted (bots know where they are) and the client draws them through walls. |

New `SimEvent`s: `flash` (thrower, pos, tick), `blinded` (agent, untilTick,
by), `dash` (agent, from, to), `recon` (thrower, pos), `revealed` (target, by,
untilTick), `footstep` (agent, pos, loud).

## Footsteps: running versus walking

Today a bot hears every enemy within 3 m whatever they do. Now:

| Movement | Speed | Heard by enemies within |
| --- | --- | --- |
| Run (default) | 6.75 m/s | **15 m, through walls** (`RUN_HEARING_M`) |
| Walk (hold Shift) | 3 m/s | 3 m (`HEARING_RANGE`, unchanged) |
| Crouch (hold Ctrl) | 2 m/s | 3 m |

A heard enemy gives the bot their position at that moment, not continuous
tracking: the bot turns toward it and its behaviour tree may investigate. Bots
choose to walk by trait: patient-lurker walks when within 25 m of an enemy it
expects; others run. This is what makes Shift-walk a real decision.

## What each lane changes

| Lane | Change |
| --- | --- |
| `shared/contract.ts` | `CONTRACT_VERSION` v4. `AgentState` gains `agent: AgentId`, `abilities: { smoke, flash, dash, recon }` (charges left), `blindUntilTick`, `revealedUntilTick`. `smokesLeft` stays, equal to `abilities.smoke`, so older readers still work. `PlayerInput` gains optional `flashAt`, `dash`, `reconAt`. The new events above. |
| `sim/` | The rules above, deterministic, with tests for each. |
| `bot/` | Bots see blindness (`view.ts`), hear footsteps, and use their kit: Rook dashes and flashes before peeking a held angle, Haze smokes chokes, Wren flashes and recons for an entry, Pike recons a flank when it hears steps. |
| `analyzer/`, `session/` | Drill specs carry the player's agent; `smoke_usage` drills give a smoke. The analyzer is unchanged. |
| `bridge/` | `rules` gains the new constants; `input` passes the new fields; a `pick_agent` message sets the player's agent for the next drill. |
| `unity/` | Agent select, keys (C smoke, Q flash, E dash, X recon, Shift walk, Ctrl crouch), white-out for blindness, revealed outlines through walls, footstep sounds by distance, ability charges in the HUD. |
| `play/` (2D) | Same keys; enough to keep the 2D client working. |

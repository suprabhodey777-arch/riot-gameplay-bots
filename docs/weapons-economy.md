# Weapons, recoil and economy (contract v5, `bots-wire.3`)

Our own weapons and economy. They fill the roles a tactical-shooter player
knows (a free sidearm, a heavy pistol, an SMG, a shotgun, two rifles, a
sniper) so practice carries over, but the names, numbers, models and spray
patterns are original. Nothing here copies a VALORANT weapon.

## Weapons

| Id | Name | Class | Price | Head | Body | Fire interval | Mag | Reload | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `pin` | Pin | sidearm | 0 (free) | 75 | 26 | 0.16 s | 12 | 1.5 s | every player always has one |
| `hammer` | Hammer | heavy pistol | 500 | 150 | 50 | 0.40 s | 6 | 2.0 s | one head tap inside 15 m, 110 head beyond |
| `wasp` | Wasp | SMG | 950 | 70 | 27 | 0.075 s | 25 | 2.2 s | damage x0.8 beyond 15 m; best moving accuracy |
| `brick` | Brick | shotgun | 1800 | 8 pellets x 18 | 8 pellets x 12 | 0.90 s | 5 | 2.5 s | each pellet x0.5 beyond 8 m |
| `lancer` | Lancer | rifle | 2900 | **150** | **40** | **0.15 s** | 25 | 2.5 s | **the drill default: the exact numbers the sim uses today**, so drills and evidence do not move |
| `viper` | Viper | rifle | 2900 | 140 | 38 | 0.09 s | 30 | 2.5 s | faster, softer recoil, damage x0.85 beyond 20 m |
| `longshot` | Longshot | sniper | 4700 | 255 | 150 | 1.25 s | 5 | 3.5 s | right-click scope; very inaccurate unless still and scoped |

Armour: `light` 400 credits, 25 points; `heavy` 1000, 50 points. Armour absorbs
66% of each hit until it runs out. Drills give no armour unless the drill says so.

Movement inaccuracy (degrees of random spread added to the first shot): standing
0, walking x1, crouching x0.5, running x4, airborne/dashing x6, times the
weapon's base spread (Wasp lowest, Longshot unscoped highest).

## Recoil (client side)

Each weapon has a **spray pattern**: a list of pitch/yaw offsets in degrees,
one per bullet of a held spray, designed per weapon (rifles rise then drift
left and right; the Wasp a tight wobble; pistols a single kick with fast reset).
The client applies the offset to the aim before its raycast, kicks the camera
by a fraction of it, and recovers over `recoveryS` when the trigger is
released. The bridge sends the patterns in `rules.weapons[id].spray` so the
2D client, the 3D client and any bot read the same numbers. A shot's
`FireIntent` is still decided by the client's raycast; the sim checks the
fire interval, the magazine and reloading, and rejects a shot that breaks them.

Bots use the weapon too: their head and body accuracy is scaled per weapon and
range (a Longshot bot is deadly still and far, poor when moving; a Wasp bot
strong close), and their fire interval is the weapon's.

## Economy (full matches)

| Event | Credits |
| --- | --- |
| Start of match (pistol round) | 800 |
| Kill | +200 |
| Round win | +3000 |
| Round loss, 1st / 2nd / 3rd+ in a row | +1900 / +2400 / +2900 |
| Spike planted (whole attacking team, win or lose) | +300 |
| Cap | 9000 |

A surviving player keeps their weapons into the next round. A dead one loses
them (except the Pin).

Round types the HUD names, from the team's average credits at the buy phase:
**pistol** (round 1 and the first round after the halftime swap), **eco**
(under 2000: save), **force** (2000-3900: buy what you can), **full buy**
(3900+: rifle and heavy armour).

## Match mode

A new client message starts a **match**: the player plus four bot teammates
against five bots, first to 7 rounds, sides swap after round 6. Before each
round the bridge sends a **buy phase** (`buy_phase`: credits, round number,
round type, score, `timeS: 20`); the client shows the buy menu (B), sends `buy`
messages, then `ready` (or the timer ends); the round then plays like a drill,
with the same input/step loop, and ends with a `round_result` (winner, how,
credits earned, MVP). Bots buy by the round type: eco saves (Pin or Hammer),
force buys a Wasp or Brick plus light armour, full buy a rifle (or a Longshot
for an off-angle-anchor) plus heavy armour. A finished match is recorded like
drills and re-analysed, so match play feeds the weakness report.

Drills: a DrillSpec may carry a `loadout` (primary, secondary, armour,
credits). Without one the player and bots have a Lancer and a Pin and no
armour, which reproduces today's numbers exactly. New drill flavour: an **eco
round** drill (Pin or Hammer only, against rifles) for each skill, offered by
the planner when the player's history shows eco rounds lost.

## Wire additions (`bots-wire.3`)

Client → bridge:

```jsonc
{ "type": "match", "map": "ironvale", "agent": "rook" }
{ "type": "buy", "item": "viper" }        // weapon id, "light", "heavy"; answered by `loadout`
{ "type": "sell", "item": "viper" }       // during the buy phase only
{ "type": "ready" }
// input gains optional: "reload": true, "slot": "primary" | "secondary", "scoped": true
```

Bridge → client:

```jsonc
{ "type": "buy_phase", "round": 3, "roundType": "force", "credits": 2400, "timeS": 20,
  "score": { "player": 1, "enemy": 1 }, "side": "attack", "team": [ { "id": "ally-1", "credits": 2100, "loadout": {...} } ] }
{ "type": "loadout", "credits": 1450, "primary": "wasp", "secondary": "pin", "armor": "light" }
{ "type": "round_result", "winner": "attack", "how": "elimination" | "detonated" | "defused" | "time",
  "creditsEarned": 3200, "score": { "player": 2, "enemy": 1 }, "mvp": "player" }
{ "type": "match_end", "score": { "player": 7, "enemy": 4 }, "won": true, "stats": { "kills": 18, "deaths": 9, "headshotPct": 0.31 } }
```

`hello.rules.weapons` is the table above plus `spray`, `recoveryS`, `baseSpreadDeg`,
`falloff`, `pellets`. `AgentState` gains `credits`, `armor`, `primary`,
`secondary`, `slot`, `ammo`, `reserve`, `reloadingUntilTick`, `scoped`.
Events gain `reload`, `buy`, `switch`.

## Ownership

| Lane | Change |
| --- | --- |
| `shared/contract.ts`, `sim/`, `bot/`, `analyzer/`, `session/`, `bridge/`, `play/` | Codex |
| `unity/Assets/` | Kiro |
| docs | Lead (Suprabho) |

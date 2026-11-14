# Ghost Rematch — game layer

Spike Protocol: the 5v5 tactical FPS that Ghost Rematch plays in. Attackers plant the
Spike on A or B and hold it; defenders stop them or defuse. First team to 7 rounds wins,
and the teams swap sides after round 6.

Ghost Rematch's game layer, built on [jangerine/valorant-like-fps](https://github.com/jangerine/valorant-like-fps) (MIT).

## Run it

From the repo root (the `game` script runs `node game/server.js`):

```bash
npm install
npm run game
```

Or from this folder (the `start` and `dev` scripts both run `node server.js`):

```bash
cd game
npm install
npm start
```

Open http://localhost:3000 (set `PORT` to serve somewhere else). Enter a name, pick an
agent and press **Join the match**; a friend on the same address (same Wi-Fi, or a tunnel
such as ngrok) joins the same match.

## Controls

| Input | Action |
| --- | --- |
| WASD | Move |
| Mouse | Aim |
| Left click | Fire |
| Right click | Aim down sights |
| R | Reload |
| Shift | Walk (quiet) |
| Space | Jump |
| C | Crouch |
| Q / E | Abilities |
| X | Ultimate (charged by 6 kills) |
| 4 (hold) | Plant or defuse the Spike (inside a site) |
| 1 / 3 | Primary weapon / knife |
| B | Shop (buy phase) |
| Tab | Scoreboard |
| Enter | Chat |

### Ghost Rematch keys

| Input | Action |
| --- | --- |
| T | Toggle the bot mind panel |
| M | Rematch menu (moments you lost) |
| F | Retry the current drill after a failed attempt |
| V | Camera mode while dead (off, follow, eyes, free) |
| [ / ] | Switch spectated player while dead |

## Agents

| Agent | Role | Kit |
| --- | --- | --- |
| SKIFF | Duelist | Q Spring Heel (high jump) · E Bolt Step (dash) · X Fan of Five (five knives) |
| BRINE | Controller | Q Salt Glare (blind) · E Fog Bank (smoke) · X Short Cut (teleport) |
| SONDE | Initiator | Q Ping Stake (reveal on minimap) · E Arc Stake (area damage) · X Bore Line (beam through walls) |
| PATCH | Sentinel | Q Mire Field (slow) · E Quick Stitch (heal) · X Second Wind (revive) |

## Weapons (shop)

| Weapon | Price | Notes |
| --- | --- | --- |
| STUB | $0 | Basic pistol |
| WARDEN | $800 | Heavy revolver · one-tap headshot |
| HORNET | $1600 | Fast SMG · strong up close |
| DRIFT | $2900 | Steady rifle · fires through smoke |
| RIVET | $2900 | Accurate rifle · one-tap headshot |
| LONGSHOT | $4700 | Bolt sniper · one-shot body |

Also on sale: HEAVY SHIELD ($1000) and SKILL CHARGE ($300).

A RIVET headshot kills in one hit (100 damage), and a LONGSHOT body shot kills in one hit.

## Rules

- Buy phase 20 s, live round 100 s, round end 6 s.
- Spike plant 4 s, defuse 7 s, detonation 40 s after the plant.
- Kill +$300, round win +$3000, round loss +$1900.
- A round ends on the timer, on elimination, or on detonation / defuse.

## Credit

This game layer is based on [jangerine/valorant-like-fps](https://github.com/jangerine/valorant-like-fps)
and follows the same MIT licence. The full text is in `LICENSE`.

# Streamer mode and pro personas (`bots-wire.4`)

Two features under one menu entry.

1. **Pro personas.** Play against, or alongside, bots that play like the
   famous styles of pro and streamer play: the hyper-aggressive flick duelist,
   the calm in-game leader, the operator anchor, the lurker who lives in your
   flank, the utility general. Each persona is an original character with an
   original name, look, taglines and a signature move set. **No real person's
   name, likeness or voice is used**: a persona is a playstyle, not an
   impersonation of a streamer.
2. **Streamer overlay.** A broadcast-style layer over your game, as if you were
   streaming the practice: a simulated chat that reacts to what actually
   happens, kill and clutch alerts, a goal bar, and a scoreboard bug. It makes
   practice feel like a show and is the part no other aim trainer has.

## Personas

| Id | Name | Style | Signature moves (bot behaviour) | Kit | Weapon preference |
| --- | --- | --- | --- | --- | --- |
| `zephyr` | **Zephyr** | hyper-aggressive flick duelist | reaction 150 ms, head accuracy high; **wide swings** past a held angle at full run; **jiggle peeks** to bait a shot then swings; dashes into a site first; re-peeks after a kill | Rook | Viper, Hammer on eco |
| `atlas` | **Atlas** | calm in-game leader | waits for a teammate's foothold, then **trades** within 1.5 s of a teammate's death; smokes the site's main choke before the push; never peeks alone | Haze | Lancer |
| `ember` | **Ember** | operator anchor | holds one long angle with a Longshot, **re-positions after every shot** (never the same spot twice), falls back after first contact | Pike | Longshot, Hammer on eco |
| `wisp` | **Wisp** | lurker | walks (silent) through the flank, **waits for the push to start**, then hits the enemy from behind; fakes footsteps in one lane and lurks the other | Pike | Wasp, Viper |
| `nova` | **Nova** | utility general | flashes for teammates (pop flash over a wall), **recons before every push**, plays second into the site | Wren | Lancer |

Bot behaviour lives in `bot/` as new archetypes plus persona-only moves
(`wideSwing`, `jigglePeek`, `trade`, `reposition`, `lurkWait`, `popFlash`). Every
move must still respect the fairness rule: no shot before `reactionMs` after
first sight, and no knowledge of enemies the bot cannot see or hear.

Each persona has a profile: tagline, three original voice lines (text, shown
as callouts), a colour pair, a signature stat ("first bloods 34%"), and a
"how to beat them" tip the coach writes from the persona's moves (e.g. Zephyr:
"he wide-swings: hold a tight angle off the corner, not the corner itself").

## Modes

- **Duel a persona:** 1v1 on a chosen site, first to 5 rounds; the coach
  reports what beat you.
- **Persona match:** the existing 5v5 match mode, with the enemy team made of
  personas (and optionally your teammates too).
- **Learn from a persona:** the drill planner offers a drill against the
  persona whose style punishes your weakest skill most (weak entry: Ember
  holding the angle you enter; weak clutch: Wisp lurking; weak smoke usage:
  Ember's long angle).

## Streamer overlay (client side, data from the bridge)

The bridge sends `chat` messages generated from real events, deterministic
from the seed, from original template lines with usernames generated from word
lists (no real usernames). Reactions to: first blood, multi-kills (2K, 3K, ACE),
clutch won or lost, headshots, a whiffed spray, a flash that blinded yourself,
plant, defuse, round win streaks, eco wins. Rate-limited so chat reads
naturally (2-6 messages per event, bursts on big moments, idle chatter between).
Alerts: "FIRST BLOOD", "CLUTCH 1v3", "ACE". A goal bar fills with kills or
rounds ("Win 3 rounds against Zephyr").

## Wire additions (`bots-wire.4`)

```jsonc
// client -> bridge
{ "type": "personas" }                                     // -> { "type": "personas", "personas": [Persona...] }
{ "type": "duel", "persona": "zephyr", "map": "ironvale", "site": "A" }
{ "type": "match", "map": "ironvale", "agent": "rook", "enemyPersonas": ["zephyr","atlas","ember","wisp","nova"] }
{ "type": "streamer", "on": true }                         // chat and alerts start flowing
// bridge -> client
{ "type": "personas", "personas": [ { "id": "zephyr", "name": "Zephyr", "style": "...", "tagline": "...",
    "lines": ["...","...","..."], "colors": ["#ff4655","#ffd166"], "agent": "rook", "signature": "...", "beatTip": "..." } ] }
{ "type": "chat", "user": "frostbyte_77", "color": "#8be9fd", "text": "that swing was disgusting", "badge": "sub" | "mod" | null }
{ "type": "alert", "kind": "first_blood" | "multi_kill" | "clutch" | "ace" | "eco_win", "text": "CLUTCH 1v3", "big": true }
{ "type": "persona_line", "persona": "zephyr", "text": "..." }   // a callout from a persona bot
{ "type": "duel_end", "score": { "player": 3, "enemy": 5 }, "beatTip": "...", "stats": {...} }
```

## Ownership

| Lane | Owner |
| --- | --- |
| `bot/`, `analyzer/`, `session/`, `bridge/`, `shared/` | Codex |
| `unity/Assets/` | Kiro |
| this doc | Claude |

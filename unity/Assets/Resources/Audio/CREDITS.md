# Audio credits

Recorded sound effects shipped in `Assets/Resources/Audio/`, loaded through
`World/SoundBank.cs`. Nothing here is generated at runtime; the only synthesised
clip left is the flashblind ear-ring (`Synth.BlindRing`), and every recorded clip
falls back to its old synthesized stand-in if the file is missing.

Source manifest: `asset-staging/CREDITS.json` (licences verified there).
`asset-staging/audio/oga-gun-reload-effects` was **not** used: its licence is unverified.

Legend: **CC0 1.0** = public domain dedication; **CC-BY 3.0** = attribution required
(the credit lines at the bottom must stay in the shipped build).

## Guns

| File | Source file | Pack | Author | Page | Licence |
|------|-------------|------|--------|------|---------|
| `Guns/pistol_shot_01.ogg` | `gunshot-sounds/sounds/cz.wav` (trimmed 0–1.8 s, fade) | Gunshot Sounds | Tabasco (audio: Vincent Sevedge) | https://opengameart.org/content/gunshot-sounds | CC-BY 3.0 |
| `Guns/rifle_shot_01.ogg` | `gunshot-sounds/sounds/sks.wav` (trimmed 0.15–2.55 s, fade) | Gunshot Sounds | Tabasco (audio: Vincent Sevedge) | https://opengameart.org/content/gunshot-sounds | CC-BY 3.0 |
| `Guns/sniper_shot_01.ogg` | `gunshot-sounds/sounds/mosin.wav` (trimmed 0.15–4.05 s, fade) | Gunshot Sounds | Tabasco (audio: Vincent Sevedge) | https://opengameart.org/content/gunshot-sounds | CC-BY 3.0 |
| `Guns/shotgun_shot_01.wav` | `gunshot-sounds/sounds/shotty.wav` | Gunshot Sounds | Tabasco (audio: Vincent Sevedge) | https://opengameart.org/content/gunshot-sounds | CC-BY 3.0 |
| `Guns/smg_shot_01.ogg` | `oga-25-bang-sfx/shot_02.ogg` | 25 CC0 bang / firework SFX | rubberduck | https://opengameart.org/content/25-cc0-bang-firework-sfx | CC0 1.0 |

The three trimmed files are multi-shot field recordings; only their first shot plus
its natural tail is kept so automatic fire does not stack 7–15 s of reverb.

## Reload and weapon handling

| File | Source file | Pack | Author | Page | Licence |
|------|-------------|------|--------|------|---------|
| `Reload/mag_out_01.ogg` | `oga-100-cc0-sfx/metal_02.ogg` | 100 CC0 SFX | rubberduck | https://opengameart.org/content/100-cc0-sfx | CC0 1.0 |
| `Reload/slide_01.ogg` | `oga-100-cc0-sfx/metal_03.ogg` | 100 CC0 SFX | rubberduck | https://opengameart.org/content/100-cc0-sfx | CC0 1.0 |
| `Reload/shell_in_01.ogg` | `oga-100-cc0-sfx/metal_11.ogg` | 100 CC0 SFX | rubberduck | https://opengameart.org/content/100-cc0-sfx | CC0 1.0 |
| `Reload/mag_in_01.wav` | `oga-weapon-clicks/clicks/weapload.wav` | Weapon clicks | Michel Baradari (apollo-music.de) | (page unresolved; `clicks/info.txt`) | CC-BY 3.0 |
| `Reload/dry_fire_01.wav` | `oga-weapon-clicks/clicks/outofammo.wav` | Weapon clicks | Michel Baradari (apollo-music.de) | (page unresolved; `clicks/info.txt`) | CC-BY 3.0 |
| `Foley/weapon_draw_01.ogg` | `oga-100-cc0-sfx/tools_04.ogg` | 100 CC0 SFX | rubberduck | https://opengameart.org/content/100-cc0-sfx | CC0 1.0 |
| `Foley/shell_tink_01.ogg` | `oga-metal-dings/ding.4.ogg` | 4 Metal Dings/Rings | StarNinjas | https://opengameart.org/content/4-metal-dingsrings | CC0 1.0 |
| `Foley/spike_beep_01.ogg` | `interface-sounds/Audio/bong_001.ogg` | Interface Sounds | Kenney | https://kenney.nl/assets/interface-sounds | CC0 1.0 |

## Footsteps

| File | Source file | Pack | Author | Page | Licence |
|------|-------------|------|--------|------|---------|
| `Footsteps/footstep_concrete_01.ogg` | `impact-sounds/Audio/footstep_concrete_000.ogg` | Impact Sounds | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Footsteps/footstep_concrete_02.ogg` | `impact-sounds/Audio/footstep_concrete_001.ogg` | Impact Sounds | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Footsteps/footstep_concrete_03.ogg` | `impact-sounds/Audio/footstep_concrete_002.ogg` | Impact Sounds | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Footsteps/footstep_concrete_04.ogg` | `impact-sounds/Audio/footstep_concrete_003.ogg` | Impact Sounds | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |

## Impacts

| File | Source file | Pack | Author | Page | Licence |
|------|-------------|------|--------|------|---------|
| `Impacts/wall_hit_01.ogg` | `impact-sounds/Audio/impactPlate_light_000.ogg` | Impact Sounds | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/wall_hit_02.ogg` | `impact-sounds/Audio/impactPlate_light_001.ogg` | Impact Sounds | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/wall_hit_03.ogg` | `impact-sounds/Audio/impactPlate_light_002.ogg` | Impact Sounds | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/body_hit_01.ogg` | `impact-sounds/Audio/impactPunch_medium_000.ogg` | Impact Sounds | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/body_hit_02.ogg` | `impact-sounds/Audio/impactPunch_medium_001.ogg` | Impact Sounds | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/body_hit_03.ogg` | `impact-sounds/Audio/impactPunch_medium_002.ogg` | Impact Sounds | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Impacts/headshot_01.ogg` | `oga-metal-dings/ding.1.ogg` | 4 Metal Dings/Rings | StarNinjas | https://opengameart.org/content/4-metal-dingsrings | CC0 1.0 |

## Abilities

| File | Source file | Pack | Author | Page | Licence |
|------|-------------|------|--------|------|---------|
| `Abilities/throw_whoosh_01.ogg` | `oga-tactical-weapons/sfx/SOROTOSS.ogg` (trimmed 0–0.9 s, fade) | Tactical Weapons And Tactics Sound Pack | XCVG / XCVG Systems | https://opengameart.org/content/tactical-weapons-and-tactics-sound-pack | CC-BY 3.0 / OGA-BY 3.0 |
| `Abilities/dash_whoosh_01.ogg` | `oga-100-cc0-sfx/noise_01.ogg` | 100 CC0 SFX | rubberduck | https://opengameart.org/content/100-cc0-sfx | CC0 1.0 |
| `Abilities/flash_pop_01.ogg` | `oga-25-bang-sfx/bang_05.ogg` | 25 CC0 bang / firework SFX | rubberduck | https://opengameart.org/content/25-cc0-bang-firework-sfx | CC0 1.0 |
| `Abilities/smoke_deploy_01.ogg` | `oga-25-bang-sfx/bang_09.ogg` | 25 CC0 bang / firework SFX | rubberduck | https://opengameart.org/content/25-cc0-bang-firework-sfx | CC0 1.0 |
| `Abilities/dart_thunk_01.ogg` | `impact-sounds/Audio/impactSoft_heavy_000.ogg` | Impact Sounds | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 |
| `Abilities/recon_ping_01.ogg` | `oga-metal-dings/ding.3.ogg` | 4 Metal Dings/Rings | StarNinjas | https://opengameart.org/content/4-metal-dingsrings | CC0 1.0 |

## UI

Exposed as `SoundBank` keys for the menu work (`ui.click`, `ui.hover`, `ui.confirm`, `ui.cancel`).

| File | Source file | Pack | Author | Page | Licence |
|------|-------------|------|--------|------|---------|
| `UI/click_01.ogg` | `ui-audio/Audio/click1.ogg` | UI SFX Set (UI Audio) | Kenney | https://kenney.nl/assets/ui-audio | CC0 1.0 |
| `UI/hover_01.ogg` | `ui-audio/Audio/rollover1.ogg` | UI SFX Set (UI Audio) | Kenney | https://kenney.nl/assets/ui-audio | CC0 1.0 |
| `UI/confirm_01.ogg` | `interface-sounds/Audio/confirmation_001.ogg` | Interface Sounds | Kenney | https://kenney.nl/assets/interface-sounds | CC0 1.0 |
| `UI/cancel_01.ogg` | `interface-sounds/Audio/back_001.ogg` | Interface Sounds | Kenney | https://kenney.nl/assets/interface-sounds | CC0 1.0 |

## Attribution lines to keep in-game (CC-BY only)

- "Gunshot Sounds" by Tabasco / Vincent Sevedge — CC-BY 3.0 — https://opengameart.org/content/gunshot-sounds
- "Weapon clicks" by Michel Baradari (apollo-music.de) — CC-BY 3.0
- "Tactical Weapons And Tactics Sound Pack" by XCVG / XCVG Systems — CC-BY 3.0 — https://opengameart.org/content/tactical-weapons-and-tactics-sound-pack

Kenney, StarNinjas and rubberduck are CC0; a courtesy credit is still nice:
"Some audio by Kenney (kenney.nl), StarNinjas and rubberduck (OpenGameArt)".

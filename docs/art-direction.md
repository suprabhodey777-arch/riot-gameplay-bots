# Art direction: Saltworks at golden hour

One look for the whole game, so the menus, the HUD and the world read as one
product. Everything is our own design: a modern tactical shooter, not a copy of
any existing game's UI, characters, maps or weapons.

## The idea

A coastal salt works an hour before sunset. Warm low sun, long shadows, cool
shade, weathered industrial surfaces with faded paint. The interface is quiet
and precise: it gets out of the way in a fight and feels premium in the menus.

## Palette

| Role | Colour |
| --- | --- |
| UI base | `#0D1014` charcoal, panels `#151A21` at 88-94% opacity |
| Text | `#ECE8E1` bone; secondary `#9AA3AE`; never pure white blocks |
| Accent | the player's accent setting (default coral `#FF5A4D`), used sparingly: one accent element per view |
| Ally / enemy | from the colour settings (default teal `#2FD3B5` / red `#FF4655`) |
| World | warm sunlit `#F2C894` light on surfaces, shade toward `#5B6B80`, paint accents: faded teal, oxide red, signal yellow |

## Type

Runtime OS fonts (no font files): display `DIN Condensed Bold`, then
`Avenir Next Condensed Demi Bold`, `Arial Narrow Bold`, `Impact`; body
`Avenir Next`, then `Helvetica Neue`, `Arial`. Display in caps with slight
tracking for titles and numbers; body in sentence case. Three sizes per screen
at most. Numbers (timer, ammo, health) in the display face.

## Menus

- A calm full-bleed backdrop: the map at golden hour with a slow camera move, a
  dark gradient from the left for text, no haze, no scanlines, no glitch.
- Left-aligned navigation as plain large type with a thin accent marker on the
  selected item, not boxed cards. Secondary actions as quiet outlined buttons.
- Cards only for things that are objects (agents, weapons, personas, findings).
  Flat panels, 1 px hairline borders at 10% white, 2 px accent top edge when
  selected. No cut corners on everything: at most one chamfer on primary buttons.
- Generous margins (96 px at 1080p), a strict 8 px grid, consistent button
  heights (48 px primary, 40 px secondary).

## HUD

- No heavy plates. Text and icons sit directly on the world with a soft shadow;
  a subtle dark gradient at the bottom edge gives contrast.
- Bottom left: health as one large display number with a thin bar, armour
  small beside it. Bottom centre: four small ability icons with key letter and
  charge pips, no labels. Bottom right: ammo as a large number, reserve small,
  weapon name small above. Top centre: timer between the two scores, compact.
  Top left: minimap, with one line of objective text under it; the drill card
  shows for 5 s at round start and then collapses to that line.
- Kill feed top right, small. Banners (round start, results) are thin and fast.

## World

- Sun at about 25-30 degrees elevation, warm; cool ambient; soft shadows;
  light warm haze in the distance. Nothing clips to white: filmic tonemapping
  and a gentle grade on every graphics preset, bloom only on high presets.
- Surfaces: weathered concrete with a faded paint band at about 1.1 m in a
  zone colour (wayfinding), dark painted steel, straight timber planks (no wavy
  grain), stone floors with seams and stains. Site areas have their own floor
  and a large painted letter.
- Guns: smooth-shaded gunmetal and polymer with highlights, believable
  proportions, gloved hands. Characters keep their designs; improve shading.
- No screen distortion of any kind: no heat haze, no chromatic fringe, no
  camera bob, no screen shake (removed on purpose; do not bring back).

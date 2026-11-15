// The settings screen, opened from the menus (top-right button) and from the
// pause menu. Tabs: Gameplay, Mouse & view, Video, Controls, Crosshair (with a
// live GL preview), Map (the corner minimap, with a live preview), Colours,
// Personas (custom display names) and About. Every control writes straight
// into Settings and applies live; closing (Done or Esc) saves to PlayerPrefs.
// Each tab fits the 900 px virtual height without scrolling, so every label is
// crisp and every control is reachable with the mouse or the keyboard.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        static readonly string[] TabNames = { "Gameplay", "Mouse & view", "Video", "Controls", "Crosshair", "Map", "Colours", "Personas", "About" };
        /// <summary>The About tab (the menu's About button opens settings on it).</summary>
        const int AboutTab = 8;
        /// <summary>The Personas tab (persona select's "Rename" opens settings on it).</summary>
        const int PersonasTab = 7;
        static readonly string[] OnOff = { "Off", "On" };
        static readonly string[] MapModes = { "Rotate with me", "Fixed north" };
        int settingsTab;
        Rect settingsArea;

        // The row cursor of the tab being drawn.
        float rowX, rowY, rowW, rowLabelW = 240f, rowTop;

        void DrawSettings()
        {
            Fill(new Rect(0f, 0f, W, H), new Color(Theme.Base.r, Theme.Base.g, Theme.Base.b, 0.7f * Intro01));
            Rect r = Centered(Mathf.Min(1200f, W - 60f), H - 60f);
            settingsArea = r;
            Frame(r, "Settings");
            Glass(r);
            float x = r.x + Pad;
            Header(x, r.y + 26f, r.width - 400f, null, "Settings", "Changes apply at once and are saved when you close this screen.");
            if (GButton(new Rect(r.xMax - Pad - 180f, r.y + 32f, 180f, Theme.ButtonH), "Done", 1, true, 0, false, null, "Esc")) Game.Do(Game.CloseSettings);

            GUI.changed = false;
            // Left: the tabs.
            float navW = 200f, top = r.y + 132f;
            Fill(new Rect(x + navW + 17f, top, 1f, r.yMax - Pad - top), Theme.Line);
            for (int i = 0; i < TabNames.Length; i++)
            {
                int idx = i;
                if (NavItem(new Rect(x, top + i * 46f, navW, 42f), TabNames[i], i == settingsTab, StableKey("tab:" + TabNames[i]))) settingsTab = idx;
            }
            if (GButton(new Rect(x, r.yMax - Pad - 40f, navW, 40f), "Reset everything", 2)) ResetAll();

            // Right: the tab's rows.
            float cx = x + navW + 36f, cw = r.xMax - Pad - cx;
            rowX = cx;
            rowY = top;
            rowTop = top;
            rowW = cw;
            rowLabelW = 240f;
            switch (settingsTab)
            {
                case 1: MouseTab(); break;
                case 2: VideoTab(); break;
                case 3: ControlsTab(); break;
                case 4: CrosshairTab(); break;
                case 5: MinimapTab(); break;
                case 6: ColoursTab(); break;
                case PersonasTab: PersonasPanel(); break;
                case AboutTab: AboutPanel(); break;
                default: GameplayTab(); break;
            }
            if (GUI.changed) Game.Do(Settings.Changed);
        }

        void ResetAll()
        {
            Settings.ValorantSens = 1.14f;
            Settings.FovH = 103f;
            Settings.Volume = 0.8f;
            Settings.VSync = false;
            Settings.FrameCap = 2;
            Settings.Crosshair = Settings.Preset(0);
            Settings.MinimapOn = true;
            Settings.MinimapRotate = true;
            Settings.MinimapZoom = 1f;
            Settings.MinimapSize = 230f;
            Settings.MinimapOpacity = 0.9f;
            Settings.EnemyColor = 0;
            Settings.AllyColor = 0;
            Settings.AccentColor = 0;
            Settings.World = WorldPaletteId.Day;
            Settings.GfxPreset = 2;
            Settings.DamageNumbers = true;
            Settings.ViewmodelFov = 60f;
            Settings.ViewmodelX = 0f;
            Settings.ViewmodelY = 0f;
            Settings.ViewmodelZ = 0f;
            Settings.ViewmodelLeft = false;
            Settings.InvertY = false;
            Settings.ToggleCrouch = false;
            Settings.ToggleWalk = false;
            Settings.ScopedSens = 1f;
            GUI.changed = true;
        }

        // ---- rows ------------------------------------------------------------------------------
        void Section(string text)
        {
            if (rowY > rowTop + 1f) rowY += 10f;
            rowY = SectionLabel(rowX, rowY, rowW, text);
        }

        void Note(string text)
        {
            rowY = Para(rowX, rowY - 2f, rowW, text, small, Theme.Muted) + 8f;
        }

        void RowLabel(string label, float h)
        {
            Lbl(new Rect(rowX, rowY, rowLabelW - 12f, h), label, bodyMid, Theme.Ink);
        }

        float SliderRow(string label, float v, float min, float max, string valueText, float step, bool enabled = true)
        {
            const float h = 40f, valW = 84f;
            RowLabel(label, h);
            float cx = rowX + rowLabelW;
            float before = v;
            v = SliderCtl(new Rect(cx, rowY + 8f, rowW - rowLabelW - valW - 12f, 24f), v, min, max, StableKey("slider:" + label), step, enabled);
            if (v != before) GUI.changed = true;
            Lbl(new Rect(rowX + rowW - valW, rowY, valW, h), valueText, valueStyle, enabled ? Theme.Ink : Theme.Faint);
            rowY += h + 4f;
            return v;
        }

        int IntRow(string label, int v, int min, int max, string unit = " px")
        {
            return Mathf.RoundToInt(SliderRow(label, v, min, max, v + unit, 1f));
        }

        bool ToggleRow(string label, bool v, string note = null)
        {
            const float h = 40f;
            RowLabel(label, h);
            float cx = rowX + rowLabelW;
            bool nv = SwitchCtl(new Rect(cx, rowY + 7f, 54f, 26f), v, StableKey("toggle:" + label));
            if (nv != v) GUI.changed = true;
            Lbl(new Rect(cx + 66f, rowY, 40f, h), nv ? "On" : "Off", bodyMid, nv ? Theme.Ink : Theme.Muted);
            if (!string.IsNullOrEmpty(note)) Lbl(new Rect(cx + 110f, rowY, rowW - rowLabelW - 110f, h), note, smallMid, Theme.Muted);
            rowY += h + 4f;
            return nv;
        }

        int ChoiceRow(string label, int v, string[] options, float width = 0f, bool enabled = true)
        {
            const float h = 40f;
            RowLabel(label, h);
            float avail = rowW - rowLabelW;
            float w = width > 0f ? Mathf.Min(width, avail) : avail;
            int nv = SegmentedCtl(new Rect(rowX + rowLabelW, rowY + 2f, w, 36f), v, options, StableKey("choice:" + label), enabled);
            if (nv != v) GUI.changed = true;
            rowY += h + 4f;
            return nv;
        }

        /// <summary>A row of colour swatches; returns the picked index (or `selected`).</summary>
        int SwatchRow(string label, int selected, NamedColor[] colors)
        {
            const float h = 44f, s = 34f;
            RowLabel(label, h);
            float x = rowX + rowLabelW;
            for (int i = 0; i < colors.Length; i++)
            {
                var r = new Rect(x, rowY + 5f, s, s);
                int key = StableKey("swatch:" + label + i);
                float hv = CardHover(r, key, true);
                if (i == selected) PlateLine(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), Settings.Accent);
                else if (hv > 0.01f) PlateLine(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), new Color(1f, 1f, 1f, 0.4f * hv));
                Plate(r, colors[i].Color);
                if (CardHit(r, key, true) && selected != i)
                {
                    selected = i;
                    GUI.changed = true;
                }
                x += s + 10f;
            }
            Lbl(new Rect(x + 6f, rowY, rowX + rowW - x - 6f, h), selected >= 0 && selected < colors.Length ? colors[selected].Name : "Custom", smallMid, Theme.Muted);
            rowY += h + 4f;
            return selected;
        }

        // ---- tabs ------------------------------------------------------------------------------
        void GameplayTab()
        {
            Section("Streamer mode");
            bool streamer = ToggleRow("Streamer mode", Settings.StreamerMode, "Chat, alerts, a goal bar, the LIVE pill and a camera frame.");
            if (streamer != Settings.StreamerMode) Game.Do(() => Game.SetStreamer(streamer));
            if (!Game.V4) Note("Chat comes from a bridge that speaks bots-wire.4; on this bridge the overlay shows your own kill alerts.");
            Section("Bots");
            // Four buttons, not a slider: rounding a dragged slider to a level every frame held it in place.
            int diff = ChoiceRow("Bot difficulty", Mathf.Clamp(Settings.BotDifficulty, 0, 3), Settings.DifficultyNames, 520f);
            if (diff != Settings.BotDifficulty)
            {
                Settings.BotDifficulty = Mathf.Clamp(diff, 0, 3);
                Game.Do(Game.SendDifficulty);
            }
            Note("How well bots aim and how fast they react. Easy bots react twice as slowly and land far fewer shots; every level misses more after a fast turn, against a strafing target, at range and in a long spray. Applies to the next drill, match or duel.");
            Section("Movement");
            Settings.ToggleCrouch = ToggleRow("Toggle crouch", Settings.ToggleCrouch, "On: Ctrl switches crouch on and off. Off: hold.");
            Settings.ToggleWalk = ToggleRow("Toggle walk", Settings.ToggleWalk, "On: Shift switches walk on and off. Off: hold.");
            Section("Feedback");
            Settings.DamageNumbers = ToggleRow("Damage numbers", Settings.DamageNumbers, "Floating numbers over the bots you hit.");
            Section("Audio");
            Settings.Volume = SliderRow("Master volume", Settings.Volume, 0f, 1f, Mathf.RoundToInt(Settings.Volume * 100f) + "%", 0.05f);
        }

        void MouseTab()
        {
            Section("Mouse");
            float sens = SliderRow("Sensitivity", Settings.ValorantSens, 0.05f, 3f, Settings.ValorantSens.ToString("0.000"), 0.005f);
            Settings.ValorantSens = Mathf.Clamp(Mathf.Round(sens * 1000f) / 1000f, 0.05f, 5f);
            Note("0.07\u00b0 per mouse count \u00d7 sensitivity, the common tactical-shooter scale, so your number carries over.   800 DPI: "
                 + Settings.CmPer360(800f).ToString("0.0") + " cm/360   \u00b7   1600 DPI: " + Settings.CmPer360(1600f).ToString("0.0") + " cm/360");
            Settings.ScopedSens = Mathf.Round(SliderRow("Scoped sensitivity", Settings.ScopedSens, 0.2f, 2f, Settings.ScopedSens.ToString("0.00") + "\u00d7", 0.05f) * 100f) / 100f;
            Settings.InvertY = ToggleRow("Invert vertical look", Settings.InvertY);
            Section("View");
            Settings.FovH = Mathf.Round(SliderRow("Field of view", Settings.FovH, 80f, 110f, Settings.FovH.ToString("0") + "\u00b0", 1f));
            Note("Horizontal. The sim's own vision cone stays 103\u00b0.");
            Section("Weapon");
            Settings.ViewmodelFov = Mathf.Round(SliderRow("Weapon field of view", Settings.ViewmodelFov, 45f, 80f, Settings.ViewmodelFov.ToString("0") + "\u00b0", 1f));
            Settings.ViewmodelLeft = ChoiceRow("Weapon hand", Settings.ViewmodelLeft ? 1 : 0, Settings.HandNames, 260f) == 1;
            Settings.ViewmodelX = Mathf.Round(SliderRow("Position: side", Settings.ViewmodelX, -1f, 1f, Mathf.RoundToInt(Settings.ViewmodelX * 4f) + " cm", 0.05f) * 20f) / 20f;
            Settings.ViewmodelY = Mathf.Round(SliderRow("Position: height", Settings.ViewmodelY, -1f, 1f, Mathf.RoundToInt(Settings.ViewmodelY * 4f) + " cm", 0.05f) * 20f) / 20f;
            Settings.ViewmodelZ = Mathf.Round(SliderRow("Position: reach", Settings.ViewmodelZ, -1f, 1f, Mathf.RoundToInt(Settings.ViewmodelZ * 4f) + " cm", 0.05f) * 20f) / 20f;
        }

        static readonly string[] GfxNotes =
        {
            "Low: no shadows, no bloom, fewer props, 75% render scale, no anti-aliasing.",
            "Medium: hard shadows to 45 m, no bloom, most props, 90% render scale, 2x MSAA.",
            "High: soft shadows to 70 m (2 cascades), bloom, all props, full resolution, 4x MSAA, HDR.",
            "Ultra: soft shadows to 95 m (4 cascades, very high resolution), stronger bloom, extra props, 4x MSAA, HDR.",
        };

        void VideoTab()
        {
            Section("Graphics");
            Settings.GfxPreset = ChoiceRow("Preset", Settings.GfxPreset, Settings.GfxNames, 520f);
            Note(GfxNotes[Mathf.Clamp(Settings.GfxPreset, 0, GfxNotes.Length - 1)]);
            Note("Changing the preset rebuilds the map's decoration (pipes, vents, light strips, markings); walls and play are unchanged.");
            Section("Display");
            Settings.VSync = ToggleRow("Vertical sync", Settings.VSync, "Off gives the lowest input lag.");
            Settings.FrameCap = ChoiceRow("Frame rate cap", Settings.FrameCap, Settings.FrameCapNames, 0f, !Settings.VSync);
            Settings.ShowFps = ToggleRow("Frame rate counter", Settings.ShowFps, "F3 in game.");
        }

        void ControlsTab()
        {
            Section("Keys");
            Note("The key map is fixed in this build. Toggle crouch and walk are under Gameplay.");
            int n = GameInput.Bindings.Length;
            int perCol = Mathf.CeilToInt(n / 2f);
            float colW = (rowW - 24f) / 2f;
            float y0 = rowY;
            for (int i = 0; i < n; i++)
            {
                string[] b = GameInput.Bindings[i];
                float x = rowX + (i / perCol) * (colW + 24f);
                float y = y0 + (i % perCol) * 38f;
                Fill(new Rect(x, y + 36f, colW, 1f), Theme.Line);
                Lbl(new Rect(x, y, colW - 170f, 36f), b[0], bodyMid, Theme.Ink);
                float kw = Mathf.Min(170f, TextW(b[1], keyCap) + 16f);
                KeyCapAt(new Rect(x + colW - kw, y + 6f, kw, 24f), b[1], new Color(1f, 1f, 1f, 0.1f), Theme.Ink);
            }
            rowY = y0 + perCol * 38f;
        }

        void CrosshairTab()
        {
            CrosshairSettings c = Settings.Crosshair;
            // Live preview: light, dark and a 4x zoom.
            var pv = new Rect(rowX, rowY, rowW, 132f);
            if (Event.current.type == EventType.Repaint)
            {
                float third = (pv.width - 16f) / 3f;
                var a = new Rect(pv.x, pv.y, third, pv.height);
                var b = new Rect(pv.x + third + 8f, pv.y, third, pv.height);
                var z = new Rect(pv.x + 2f * (third + 8f), pv.y, third, pv.height);
                Plate(a, new Color(0.78f, 0.84f, 0.90f));
                Fill(new Rect(a.x, a.y + a.height * 0.55f, a.width, a.height * 0.45f - 2f), new Color(0.86f, 0.84f, 0.80f));
                Plate(b, new Color(0.10f, 0.11f, 0.14f));
                Plate(z, new Color(0.18f, 0.20f, 0.24f));
                Lbl(new Rect(a.x + 12f, a.y + 8f, a.width, 18f), "LIGHT", tinyCaps, new Color(0.1f, 0.1f, 0.12f, 0.8f));
                Lbl(new Rect(b.x + 12f, b.y + 8f, b.width, 18f), "DARK", tinyCaps, Theme.Muted);
                Lbl(new Rect(z.x + 12f, z.y + 8f, z.width, 18f), "ZOOM \u00d74", tinyCaps, Theme.Muted);
                int k = Crosshair.PixelScale(Screen.height);
                float demoSpread = c.Dynamic ? (Mathf.Sin(Time.unscaledTime * 2.5f) * 0.5f + 0.5f) * 6f : 0f;
                Draw2D.Begin(Screen.width, Screen.height);
                Crosshair.Draw(a.center.x * scale, a.center.y * scale, c, k, demoSpread, 1f);
                Crosshair.Draw(b.center.x * scale, b.center.y * scale, c, k, demoSpread, 1f);
                Crosshair.Draw(z.center.x * scale, z.center.y * scale, c, k * 4, demoSpread, 1f);
                float hitAge = Mathf.Repeat(Time.unscaledTime, 1.6f);
                Crosshair.HitMarker(b.center.x * scale, b.center.y * scale, k, hitAge, false, false);
                Draw2D.End();
            }
            rowY += pv.height + 12f;
            // Presets.
            float px = rowX;
            Lbl(new Rect(px, rowY, 90f, 38f), "Presets", bodyMid, Theme.Ink);
            px += 90f;
            float pw = (rowW - 90f - 8f * (Settings.PresetNames.Length - 1)) / Settings.PresetNames.Length;
            for (int i = 0; i < Settings.PresetNames.Length; i++)
            {
                if (GButton(new Rect(px + i * (pw + 8f), rowY, pw, 38f), Settings.PresetNames[i], 0, true, StableKey("xh-preset" + i)))
                {
                    int hit = c.HitColorPreset;
                    Settings.Crosshair = Settings.Preset(i);
                    Settings.Crosshair.HitColorPreset = hit;
                    c = Settings.Crosshair;
                    GUI.changed = true;
                }
            }
            rowY += 50f;

            // Two columns: shape and outline, colour and hit marker.
            float colW = (rowW - 28f) / 2f;
            float y0 = rowY, baseX = rowX, baseW = rowW;
            rowW = colW;
            rowLabelW = 150f;
            Section("Shape");
            c.Style = (CrosshairStyle)ChoiceRow("Style", (int)c.Style, ShortStyleNames);
            bool arms = c.Style == CrosshairStyle.Cross || c.Style == CrosshairStyle.CrossDot || c.Style == CrosshairStyle.TShape;
            if (arms) c.Length = IntRow("Line length", c.Length, 0, 20);
            c.Thickness = IntRow(c.Style == CrosshairStyle.Circle ? "Ring width" : "Thickness", c.Thickness, 1, 10);
            if (c.Style != CrosshairStyle.Dot) c.Gap = IntRow(c.Style == CrosshairStyle.Circle ? "Ring radius" : "Centre gap", c.Gap, 0, 24);
            c.DotSize = IntRow("Centre dot", c.DotSize, 0, 10);
            c.Dynamic = ToggleRow("Dynamic", c.Dynamic, "Opens when moving");
            Section("Outline");
            c.Outline = ToggleRow("Outline", c.Outline);
            if (c.Outline)
            {
                c.OutlineThickness = IntRow("Thickness ", c.OutlineThickness, 1, 4);
                c.OutlineOpacity = SliderRow("Opacity ", c.OutlineOpacity, 0f, 1f, Mathf.RoundToInt(c.OutlineOpacity * 100f) + "%", 0.05f);
            }

            rowX = baseX + colW + 28f;
            rowY = y0;
            rowTop = y0;
            Section("Colour");
            int picked = SwatchRow("Colour", c.ColorPreset, Settings.CrosshairColors);
            if (picked != c.ColorPreset && picked >= 0)
            {
                c.ColorPreset = picked;
                c.Color = Settings.CrosshairColors[picked].Color;
            }
            Color before = c.Color;
            float r = SliderRow("Red", c.Color.r * 255f, 0f, 255f, Mathf.RoundToInt(c.Color.r * 255f).ToString(), 5f);
            float g = SliderRow("Green", c.Color.g * 255f, 0f, 255f, Mathf.RoundToInt(c.Color.g * 255f).ToString(), 5f);
            float bl = SliderRow("Blue", c.Color.b * 255f, 0f, 255f, Mathf.RoundToInt(c.Color.b * 255f).ToString(), 5f);
            var custom = new Color(Mathf.Round(r) / 255f, Mathf.Round(g) / 255f, Mathf.Round(bl) / 255f, 1f);
            if (Mathf.Abs(custom.r - before.r) + Mathf.Abs(custom.g - before.g) + Mathf.Abs(custom.b - before.b) > 0.002f)
            {
                c.Color = custom;
                c.ColorPreset = -1;
            }
            c.Opacity = SliderRow("Opacity", c.Opacity, 0.1f, 1f, Mathf.RoundToInt(c.Opacity * 100f) + "%", 0.05f);
            Section("Hit marker");
            c.HitColorPreset = SwatchRow("Colour ", c.HitColorPreset, Settings.HitColors);
            rowX = baseX;
            rowW = baseW;
            rowLabelW = 240f;
        }

        static readonly string[] ShortStyleNames = { "Cross", "Dot", "+ Dot", "Circle", "T" };

        void MinimapTab()
        {
            float size = Settings.MinimapSize;
            float baseW = rowW;
            rowW = baseW - Mathf.Max(size, 230f) - 32f;
            rowLabelW = 190f;
            Section("Tactical map");
            Note("M opens the full tactical map during a round (M or Esc closes it); hold Tab to peek at it.");
            Section("Corner minimap");
            Settings.MinimapOn = ToggleRow("Show minimap", Settings.MinimapOn);
            Settings.MinimapRotate = ChoiceRow("Orientation", Settings.MinimapRotate ? 0 : 1, MapModes) == 0;
            Settings.MinimapZoom = Mathf.Round(SliderRow("Zoom", Settings.MinimapZoom, 0.5f, 3f, Settings.MinimapZoom.ToString("0.0") + "\u00d7", 0.1f) * 10f) / 10f;
            Settings.MinimapSize = Mathf.Round(SliderRow("Size", Settings.MinimapSize, 150f, 380f, Settings.MinimapSize.ToString("0") + " px", 10f));
            Settings.MinimapOpacity = SliderRow("Opacity", Settings.MinimapOpacity, 0.3f, 1f, Mathf.RoundToInt(Settings.MinimapOpacity * 100f) + "%", 0.05f);
            Note("Enemies show only when you can see them (in your view, with line of sight, not through smokes), while a recon dart reveals them, or as a ripple when you hear them run within " + Game.Rules.runHearingM.ToString("0") + " m.");
            var pv = new Rect(rowX + rowW + 32f, rowTop + 34f, size, size);
            Lbl(new Rect(pv.x, rowTop, pv.width, 24f), Spaced("Preview"), tinyCaps, Theme.Muted);
            if (Event.current.type == EventType.Repaint && Game.Map != null)
            {
                Vector2 eye = Game.EyePos;
                if (eye == Vector2.zero && Game.Map.bounds != null) eye = new Vector2(Game.Map.bounds.width / 2f, Game.Map.bounds.height / 2f);
                Game.Minimap.Draw(Game, PhysRect(pv), false, eye, Game.Rig.Heading, Settings.MinimapOpacity, scale);
            }
            rowW = baseW;
            rowLabelW = 240f;
        }

        void ColoursTab()
        {
            Section("Players");
            Settings.EnemyColor = Mathf.Max(0, SwatchRow("Enemy highlight", Settings.EnemyColor, Settings.EnemyColors));
            Settings.AllyColor = Mathf.Max(0, SwatchRow("Ally colour", Settings.AllyColor, Settings.AllyColors));
            Note("The colour-blind safe enemy colour (vermillion) stays distinct from sky-blue allies for deuteranopia, protanopia and tritanopia.");
            Section("Interface");
            Settings.AccentColor = Mathf.Max(0, SwatchRow("Accent", Settings.AccentColor, Settings.AccentColors));
            Section("World");
            Settings.World = (WorldPaletteId)ChoiceRow("Palette", (int)Settings.World, Settings.PaletteNames);
            Note("Day: soft pastel daylight.  Dusk: low warm sun, long shadows.  Night: cool moonlight.  High contrast: bright walls, dark floor, thin fog.");
            Section("Preview");
            float x = rowX;
            x += PreviewChip(x, rowY, "ENEMY", Settings.Enemy) + 12f;
            x += PreviewChip(x, rowY, "ALLY", Settings.Ally) + 12f;
            x += PreviewChip(x, rowY, "ACCENT", Settings.Accent) + 24f;
            GButton(new Rect(x, rowY, 220f, 40f), "Primary button", 1, true, StableKey("preview-primary"));
            GButton(new Rect(x + 232f, rowY, 200f, 40f), "Button", 0, true, StableKey("preview-secondary"));
            rowY += 52f;
        }

        float PreviewChip(float x, float y, string label, Color c)
        {
            var r = new Rect(x, y, 128f, 40f);
            Plate(r, new Color(c.r * 0.25f, c.g * 0.25f, c.b * 0.25f, 0.95f));
            Plate(new Rect(r.x + 12f, r.y + 12f, 16f, 16f), c);
            Lbl(new Rect(r.x + 36f, r.y, r.width - 40f, r.height), label, bodyBoldMid, c);
            return r.width;
        }

        // ---- personas: custom display names ------------------------------------------------------------
        readonly Dictionary<string, string> nameEdits = new Dictionary<string, string>();

        void PersonasPanel()
        {
            Section("Persona names");
            Note("Give each pro persona the name you want to see. It is used everywhere the client shows the persona: the select screens, name tags, the kill feed, chat and callouts. Leave a field empty for the original name.");
            List<Persona> all = Personas.All;
            for (int i = 0; i < all.Count; i++)
            {
                Persona p = all[i];
                if (rowY + 52f > settingsArea.yMax - Pad) break;
                var r = new Rect(rowX, rowY, rowW, 48f);
                Fill(new Rect(r.x, r.y + 8f, 4f, r.height - 16f), p.Primary);
                Lbl(new Rect(r.x + 16f, r.y + 2f, 360f, 24f), p.DefaultName, bodyBoldMid, p.Primary);
                Lbl(new Rect(r.x + 16f, r.y + 26f, 360f, 18f), Upper(p.Style ?? ""), tinyCaps, Theme.Faint);
                float fx = r.x + 390f, fw = Mathf.Max(160f, r.width - 390f - 130f);
                PersonaNameField(new Rect(fx, r.y + 4f, fw, 40f), p);
                bool custom = !string.IsNullOrEmpty(Personas.CustomName(p.Id));
                if (GButton(new Rect(fx + fw + 12f, r.y + 4f, 112f, 40f), "Reset", 2, custom, StableKey("pname-reset:" + p.Id)))
                {
                    string id = p.Id;
                    nameEdits[id] = "";
                    Personas.SetCustomName(id, "");
                    GUI.FocusControl(null);
                }
                rowY += 56f;
            }
        }

        /// <summary>The custom-name field of persona `p` (empty shows the default name as a hint).</summary>
        void PersonaNameField(Rect r, Persona p)
        {
            string field = "pname:" + p.Id;
            if (!nameEdits.TryGetValue(p.Id, out string buf)) buf = Personas.CustomName(p.Id) ?? "";
            string typed = Personas.FilterTyped(TextFieldCtl(r, buf, Personas.MaxNameLength, field));
            if (typed != buf)
            {
                nameEdits[p.Id] = typed;
                Personas.SetCustomName(p.Id, typed, false);
            }
            if (typed.Length == 0 && GUI.GetNameOfFocusedControl() != field)
                Lbl(new Rect(r.x + 14f, r.y, r.width - 28f, r.height), p.DefaultName + "   (original name)", smallMid, Theme.Faint);
        }

        /// <summary>About: what this is, who made it, and the persona rule.</summary>
        void AboutPanel()
        {
            Condensed(new Rect(rowX, rowY, rowW, 70f), Spaced("Saltworks"), displayL, Theme.Ink, 0.78f);
            rowY += 74f;
            Neon(new Rect(rowX, rowY, 120f, 3f), Settings.Accent, 1f);
            rowY += 14f;
            DrawCredit(new Vector2(rowX, rowY));
            rowY += 34f;
            rowY = Para(rowX, rowY, rowW, "A tactical coaching client: the bridge reads a player's matches, finds the situation they lose most, and builds drills against bots that play like people. Then the same skills go into full matches, duels against pro personas, and a practice stream with its own chat.", body, Theme.Ink) + 8f;
            Section("Built from code");
            Note("Every figure, gun, map prop, texture, sound and screen is generated at runtime: no downloaded models, textures or audio.");
            Section("Pro personas");
            Note("The pro personas are original characters that play a well-known style. No real player's name, likeness or voice is used; you can rename them under Personas.");
            Section("Credits");
            rowY = Para(rowX, rowY, rowW, "Built for DevHacks 2026.", bodyBold, Theme.Ink) + 6f;
            Note("Bridge protocol " + (Game.V4 ? Wire.Protocol4 : Game.V3 ? Wire.Protocol : Game.V2 ? Wire.Protocol2 : Wire.LegacyProtocol) + "   \u00b7   Unity " + Application.unityVersion);
        }
    }
}

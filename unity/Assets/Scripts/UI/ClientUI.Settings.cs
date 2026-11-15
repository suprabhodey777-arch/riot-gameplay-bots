// The settings screen, opened from the menus (top-right button) and from the
// pause menu: General (mouse, field of view, volume, video), Crosshair (with a
// live GL preview), Minimap (with a live preview) and Colours. Every control
// writes straight into Settings and applies live; closing saves to PlayerPrefs.
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        static readonly string[] TabNames = { "General", "Video", "Controls", "Crosshair", "Minimap", "Colours" };
        static readonly string[] OnOff = { "Off", "On" };
        static readonly string[] MapModes = { "Rotate with me", "Fixed north" };
        int settingsTab;
        Rect settingsArea;

        void DrawSettings()
        {
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.6f * Intro01));
            settingsArea = Centered(1020f, H - 60f);
            GUILayout.BeginArea(settingsArea, panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Settings", title);
            GUILayout.FlexibleSpace();
            if (Btn("Done", primary, GUILayout.Width(140f), GUILayout.Height(40f))) Game.Do(Game.CloseSettings);
            GUILayout.EndHorizontal();
            GUILayout.Label("Changes apply at once and are saved when you close this screen (Esc).", muted);
            GUILayout.Space(Theme.S3);

            GUI.changed = false;
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(180f));
            for (int i = 0; i < TabNames.Length; i++)
            {
                int idx = i;
                if (Btn(TabNames[i], i == settingsTab ? tabOn : tab, GUILayout.Height(42f))) settingsTab = idx;
            }
            GUILayout.FlexibleSpace();
            if (Btn("Reset everything", ghost, GUILayout.Height(34f))) ResetAll();
            GUILayout.EndVertical();
            GUILayout.Space(Theme.S5);
            GUILayout.BeginVertical();
            switch (settingsTab)
            {
                case 1: VideoTab(); break;
                case 2: ControlsTab(); break;
                case 3: CrosshairTab(); break;
                case 4: MinimapTab(); break;
                case 5: ColoursTab(); break;
                default: GeneralTab(); break;
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            bool changed = GUI.changed;
            GUILayout.EndArea();
            if (changed) Game.Do(Settings.Changed);
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
            Settings.MinimapRotate = false;
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
        float SliderRow(string label, float v, float min, float max, string valueText)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(30f));
            GUILayout.Label(label, body, GUILayout.Width(190f));
            GUILayout.BeginVertical();
            GUILayout.Space(6f);
            v = GUILayout.HorizontalSlider(v, min, max, slider, thumb, GUILayout.ExpandWidth(true));
            GUILayout.EndVertical();
            GUILayout.Space(Theme.S3);
            GUILayout.Label(valueText, bodyBold, GUILayout.Width(80f));
            GUILayout.EndHorizontal();
            return v;
        }

        int IntRow(string label, int v, int min, int max, string unit = " px")
        {
            return Mathf.RoundToInt(SliderRow(label, v, min, max, v + unit));
        }

        bool ToggleRow(string label, bool v, string note = null)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(34f));
            GUILayout.Label(label, body, GUILayout.Width(190f));
            int i = GUILayout.Toolbar(v ? 1 : 0, OnOff, toolbar, GUILayout.Width(160f));
            if (note != null)
            {
                GUILayout.Space(Theme.S3);
                GUILayout.Label(note, small);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return i == 1;
        }

        int ChoiceRow(string label, int v, string[] options, float width = 0f)
        {
            GUILayout.BeginHorizontal(GUILayout.MinHeight(34f));
            GUILayout.Label(label, body, GUILayout.Width(190f));
            v = width > 0f ? GUILayout.Toolbar(v, options, toolbar, GUILayout.Width(width)) : GUILayout.Toolbar(v, options, toolbar);
            GUILayout.EndHorizontal();
            return v;
        }

        /// <summary>A row of colour swatches; returns the picked index (or `selected`).</summary>
        int SwatchRow(string label, int selected, NamedColor[] colors)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(40f));
            GUILayout.Label(label, body, GUILayout.Width(190f));
            for (int i = 0; i < colors.Length; i++)
            {
                Rect r = GUILayoutUtility.GetRect(34f, 34f, GUILayout.Width(34f), GUILayout.Height(34f));
                if (GUI.Button(r, GUIContent.none, ghost)) selected = i;
                if (i == selected) Round(r, Settings.Accent);
                Round(new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f), colors[i].Color);
                GUILayout.Space(Theme.S1);
            }
            GUILayout.Space(Theme.S2);
            GUILayout.Label(selected >= 0 && selected < colors.Length ? colors[selected].Name : "Custom", small);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return selected;
        }

        // ---- tabs ------------------------------------------------------------------------------
        void GeneralTab()
        {
            settingsScroll = GUILayout.BeginScrollView(settingsScroll, false, false);
            SectionHeader("Mouse");
            GUILayout.BeginHorizontal();
            float sens = SliderRow("Sensitivity", Settings.ValorantSens, 0.05f, 3f, Settings.ValorantSens.ToString("0.000"));
            if (Btn("\u2212", toolbar, GUILayout.Width(34f))) sens -= 0.005f;
            if (Btn("+", toolbar, GUILayout.Width(34f))) sens += 0.005f;
            Settings.ValorantSens = Mathf.Clamp(Mathf.Round(sens * 1000f) / 1000f, 0.05f, 5f);
            GUILayout.EndHorizontal();
            GUILayout.Label("Same scale as VALORANT (0.07\u00b0 per mouse count \u00d7 sensitivity), so your number carries over.   " +
                            "800 DPI: " + Settings.CmPer360(800f).ToString("0.0") + " cm/360   1600 DPI: " + Settings.CmPer360(1600f).ToString("0.0") + " cm/360", small);
            Settings.ScopedSens = Mathf.Round(SliderRow("Scoped sensitivity", Settings.ScopedSens, 0.2f, 2f, Settings.ScopedSens.ToString("0.00") + "\u00d7") * 100f) / 100f;
            GUILayout.Label("Relative to the zoom: 1.00 turns the same distance across the scope as across the unscoped view.", small);
            Settings.InvertY = ToggleRow("Invert vertical look", Settings.InvertY);

            SectionHeader("View");
            Settings.FovH = Mathf.Round(SliderRow("Field of view", Settings.FovH, 80f, 110f, Settings.FovH.ToString("0") + "\u00b0"));
            GUILayout.Label("Horizontal. The sim's own vision cone stays 103\u00b0.", small);
            Settings.ViewmodelFov = Mathf.Round(SliderRow("Weapon field of view", Settings.ViewmodelFov, 45f, 80f, Settings.ViewmodelFov.ToString("0") + "\u00b0"));
            Settings.ViewmodelLeft = ChoiceRow("Weapon hand", Settings.ViewmodelLeft ? 1 : 0, Settings.HandNames, 220f) == 1;
            Settings.ViewmodelX = Mathf.Round(SliderRow("Weapon position: side", Settings.ViewmodelX, -1f, 1f, Mathf.RoundToInt(Settings.ViewmodelX * 4f) + " cm") * 20f) / 20f;
            Settings.ViewmodelY = Mathf.Round(SliderRow("Weapon position: height", Settings.ViewmodelY, -1f, 1f, Mathf.RoundToInt(Settings.ViewmodelY * 4f) + " cm") * 20f) / 20f;
            Settings.ViewmodelZ = Mathf.Round(SliderRow("Weapon position: reach", Settings.ViewmodelZ, -1f, 1f, Mathf.RoundToInt(Settings.ViewmodelZ * 4f) + " cm") * 20f) / 20f;

            SectionHeader("Gameplay");
            Settings.ToggleCrouch = ToggleRow("Toggle crouch", Settings.ToggleCrouch, "On: Ctrl switches crouch on and off. Off: hold.");
            Settings.ToggleWalk = ToggleRow("Toggle walk", Settings.ToggleWalk, "On: Shift switches walk on and off. Off: hold.");
            Settings.DamageNumbers = ToggleRow("Damage numbers", Settings.DamageNumbers, "Floating numbers over the bots you hit.");

            SectionHeader("Audio");
            Settings.Volume = SliderRow("Master volume", Settings.Volume, 0f, 1f, Mathf.RoundToInt(Settings.Volume * 100f) + "%");
            GUILayout.EndScrollView();
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
            settingsScroll = GUILayout.BeginScrollView(settingsScroll, false, false);
            SectionHeader("Graphics");
            Settings.GfxPreset = ChoiceRow("Preset", Settings.GfxPreset, Settings.GfxNames);
            GUILayout.Label(GfxNotes[Mathf.Clamp(Settings.GfxPreset, 0, GfxNotes.Length - 1)], small);
            GUILayout.Label("Changing the preset rebuilds the map's decoration (pipes, vents, light strips, markings); walls and play are unchanged.", small);
            SectionHeader("Display");
            Settings.VSync = ToggleRow("Vertical sync", Settings.VSync, "Off gives the lowest input lag.");
            GUI.enabled = !Settings.VSync;
            Settings.FrameCap = ChoiceRow("Frame rate cap", Settings.FrameCap, Settings.FrameCapNames);
            GUI.enabled = true;
            Settings.ShowFps = ToggleRow("Frame rate counter", Settings.ShowFps, "F3 in game.");
            GUILayout.EndScrollView();
        }

        void ControlsTab()
        {
            settingsScroll = GUILayout.BeginScrollView(settingsScroll, false, false);
            SectionHeader("Keys");
            GUILayout.Label("The key map is fixed in this build. Toggle crouch and walk are in General.", small);
            GUILayout.Space(Theme.S2);
            foreach (string[] b in GameInput.Bindings)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(28f));
                GUILayout.Label(b[0], body, GUILayout.Width(260f));
                Vector2 sz = badge.CalcSize(new GUIContent(b[1]));
                Rect r = GUILayoutUtility.GetRect(sz.x + 16f, 24f, GUILayout.Width(sz.x + 16f), GUILayout.Height(24f));
                Round(r, new Color(1f, 1f, 1f, 0.1f));
                GUI.Label(r, b[1], badge);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        void CrosshairTab()
        {
            CrosshairSettings c = Settings.Crosshair;
            // Live preview (outside the scroll view, so GL drawing is never clipped wrongly).
            Rect pv = GUILayoutUtility.GetRect(10f, 150f, GUILayout.ExpandWidth(true), GUILayout.Height(150f));
            if (Event.current.type == EventType.Repaint)
            {
                float third = (pv.width - 16f) / 3f;
                var a = new Rect(pv.x, pv.y, third, pv.height);
                var b = new Rect(pv.x + third + 8f, pv.y, third, pv.height);
                var z = new Rect(pv.x + 2f * (third + 8f), pv.y, third, pv.height);
                Round(a, new Color(0.78f, 0.84f, 0.90f));
                Round(new Rect(a.x, a.y + a.height * 0.55f, a.width, a.height * 0.45f), new Color(0.86f, 0.84f, 0.80f));
                Round(b, new Color(0.10f, 0.11f, 0.14f));
                Round(z, new Color(0.18f, 0.20f, 0.24f));
                GUI.Label(new Rect(a.x + 10f, a.y + 6f, a.width, 18f), "LIGHT", caption);
                GUI.Label(new Rect(b.x + 10f, b.y + 6f, b.width, 18f), "DARK", caption);
                GUI.Label(new Rect(z.x + 10f, z.y + 6f, z.width, 18f), "ZOOM \u00d74", caption);
                int k = Crosshair.PixelScale(Screen.height);
                float demoSpread = c.Dynamic ? (Mathf.Sin(Time.unscaledTime * 2.5f) * 0.5f + 0.5f) * 6f : 0f;
                Draw2D.Begin(Screen.width, Screen.height);
                Crosshair.Draw((settingsArea.x + a.center.x) * scale, (settingsArea.y + a.center.y) * scale, c, k, demoSpread, 1f);
                Crosshair.Draw((settingsArea.x + b.center.x) * scale, (settingsArea.y + b.center.y) * scale, c, k, demoSpread, 1f);
                Crosshair.Draw((settingsArea.x + z.center.x) * scale, (settingsArea.y + z.center.y) * scale, c, k * 4, demoSpread, 1f);
                float hitAge = Mathf.Repeat(Time.unscaledTime, 1.6f);
                Crosshair.HitMarker((settingsArea.x + b.center.x) * scale, (settingsArea.y + b.center.y) * scale, k, hitAge, false, false);
                Draw2D.End();
            }
            GUILayout.Space(Theme.S2);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Presets", bodyBold, GUILayout.Width(80f));
            for (int i = 0; i < Settings.PresetNames.Length; i++)
            {
                if (Btn(Settings.PresetNames[i], button, GUILayout.Height(34f)))
                {
                    int hit = c.HitColorPreset;
                    Settings.Crosshair = Settings.Preset(i);
                    Settings.Crosshair.HitColorPreset = hit;
                    c = Settings.Crosshair;
                }
            }
            GUILayout.EndHorizontal();

            settingsScroll = GUILayout.BeginScrollView(settingsScroll, false, false);
            SectionHeader("Shape");
            c.Style = (CrosshairStyle)ChoiceRow("Style", (int)c.Style, Settings.StyleNames);
            bool arms = c.Style == CrosshairStyle.Cross || c.Style == CrosshairStyle.CrossDot || c.Style == CrosshairStyle.TShape;
            if (arms) c.Length = IntRow("Line length", c.Length, 0, 20);
            c.Thickness = IntRow(c.Style == CrosshairStyle.Circle ? "Ring thickness" : "Line thickness", c.Thickness, 1, 10);
            if (c.Style != CrosshairStyle.Dot) c.Gap = IntRow(c.Style == CrosshairStyle.Circle ? "Ring radius" : "Centre gap", c.Gap, 0, 24);
            c.DotSize = IntRow("Centre dot", c.DotSize, 0, 10);
            c.Dynamic = ToggleRow("Dynamic spread", c.Dynamic, "Opens while running and firing.");

            SectionHeader("Colour");
            int picked = SwatchRow("Colour", c.ColorPreset, Settings.CrosshairColors);
            if (picked != c.ColorPreset && picked >= 0)
            {
                c.ColorPreset = picked;
                c.Color = Settings.CrosshairColors[picked].Color;
            }
            Color before = c.Color;
            float r = SliderRow("Red", c.Color.r * 255f, 0f, 255f, Mathf.RoundToInt(c.Color.r * 255f).ToString());
            float g = SliderRow("Green", c.Color.g * 255f, 0f, 255f, Mathf.RoundToInt(c.Color.g * 255f).ToString());
            float bl = SliderRow("Blue", c.Color.b * 255f, 0f, 255f, Mathf.RoundToInt(c.Color.b * 255f).ToString());
            var custom = new Color(Mathf.Round(r) / 255f, Mathf.Round(g) / 255f, Mathf.Round(bl) / 255f, 1f);
            if (Mathf.Abs(custom.r - before.r) + Mathf.Abs(custom.g - before.g) + Mathf.Abs(custom.b - before.b) > 0.002f)
            {
                c.Color = custom;
                c.ColorPreset = -1;
            }
            c.Opacity = SliderRow("Opacity", c.Opacity, 0.1f, 1f, Mathf.RoundToInt(c.Opacity * 100f) + "%");

            SectionHeader("Outline");
            c.Outline = ToggleRow("Outline", c.Outline);
            GUI.enabled = c.Outline;
            c.OutlineThickness = IntRow("Outline thickness", c.OutlineThickness, 1, 4);
            c.OutlineOpacity = SliderRow("Outline opacity", c.OutlineOpacity, 0f, 1f, Mathf.RoundToInt(c.OutlineOpacity * 100f) + "%");
            GUI.enabled = true;

            SectionHeader("Hit marker");
            c.HitColorPreset = SwatchRow("Hit marker colour", c.HitColorPreset, Settings.HitColors);
            GUILayout.EndScrollView();
        }

        void MinimapTab()
        {
            float size = Settings.MinimapSize;
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            settingsScroll = GUILayout.BeginScrollView(settingsScroll, false, false);
            SectionHeader("Minimap");
            Settings.MinimapOn = ToggleRow("Show minimap", Settings.MinimapOn, "M in game. Hold Tab for the full map.");
            Settings.MinimapRotate = ChoiceRow("Orientation", Settings.MinimapRotate ? 0 : 1, MapModes, 300f) == 0;
            Settings.MinimapZoom = Mathf.Round(SliderRow("Zoom", Settings.MinimapZoom, 0.5f, 3f, Settings.MinimapZoom.ToString("0.0") + "\u00d7") * 10f) / 10f;
            Settings.MinimapSize = Mathf.Round(SliderRow("Size", Settings.MinimapSize, 150f, 380f, Settings.MinimapSize.ToString("0") + " px"));
            Settings.MinimapOpacity = SliderRow("Opacity", Settings.MinimapOpacity, 0.3f, 1f, Mathf.RoundToInt(Settings.MinimapOpacity * 100f) + "%");
            GUILayout.Space(Theme.S2);
            GUILayout.Label("Enemies show only when you can see them (in your view, with line of sight, not through smokes), while a recon dart reveals them, or as a ripple when you hear them run within " + Game.Rules.runHearingM.ToString("0") + " m.", small);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.Space(Theme.S4);
            Rect pv = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            GUILayout.EndHorizontal();
            if (Event.current.type == EventType.Repaint && Game.Map != null)
            {
                var vr = new Rect(settingsArea.x + pv.x, settingsArea.y + pv.y, pv.width, pv.height);
                Vector2 eye = Game.EyePos;
                if (eye == Vector2.zero && Game.Map.bounds != null) eye = new Vector2(Game.Map.bounds.width / 2f, Game.Map.bounds.height / 2f);
                Game.Minimap.Draw(Game, new Rect(vr.x * scale, vr.y * scale, vr.width * scale, vr.height * scale), false, eye, Game.Rig.Heading, Settings.MinimapOpacity);
            }
        }

        void ColoursTab()
        {
            settingsScroll = GUILayout.BeginScrollView(settingsScroll, false, false);
            SectionHeader("Players");
            Settings.EnemyColor = Mathf.Max(0, SwatchRow("Enemy highlight", Settings.EnemyColor, Settings.EnemyColors));
            Settings.AllyColor = Mathf.Max(0, SwatchRow("Ally colour", Settings.AllyColor, Settings.AllyColors));
            GUILayout.Label("The colour-blind safe enemy colour (vermillion) stays distinct from sky-blue allies for deuteranopia, protanopia and tritanopia.", small);

            SectionHeader("Interface");
            Settings.AccentColor = Mathf.Max(0, SwatchRow("Accent", Settings.AccentColor, Settings.AccentColors));

            SectionHeader("World");
            Settings.World = (WorldPaletteId)ChoiceRow("Palette", (int)Settings.World, Settings.PaletteNames);
            GUILayout.Label("Day: soft pastel daylight.  Dusk: low warm sun, long shadows.  Night: cool moonlight.  High contrast: bright walls, dark floor, thin fog.", small);

            SectionHeader("Preview");
            GUILayout.BeginHorizontal(card);
            PreviewChip("ENEMY", Settings.Enemy);
            PreviewChip("ALLY", Settings.Ally);
            PreviewChip("ACCENT", Settings.Accent);
            GUILayout.FlexibleSpace();
            Btn("Primary button", primary, GUILayout.Width(200f), GUILayout.Height(38f));
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }

        void PreviewChip(string label, Color c)
        {
            Rect r = GUILayoutUtility.GetRect(120f, 38f, GUILayout.Width(120f), GUILayout.Height(38f));
            Round(r, new Color(c.r * 0.25f, c.g * 0.25f, c.b * 0.25f, 0.9f));
            Round(new Rect(r.x + 10f, r.y + 11f, 16f, 16f), c);
            Color old = GUI.contentColor;
            GUI.contentColor = c;
            GUI.Label(new Rect(r.x + 34f, r.y + 10f, r.width - 36f, 20f), label, bodyBold);
            GUI.contentColor = old;
            GUILayout.Space(Theme.S2);
        }
    }
}

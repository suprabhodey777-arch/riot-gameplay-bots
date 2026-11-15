// Player settings: input, video, audio, crosshair, minimap and colours. Stored
// in PlayerPrefs (key prefix "sw."), applied live. Every change bumps Version so
// views that cache derived data (materials, minimap textures) can rebuild.
using UnityEngine;

namespace Saltworks
{
    public enum CrosshairStyle
    {
        Cross,
        Dot,
        CrossDot,
        Circle,
        TShape,
    }

    public enum WorldPaletteId
    {
        Day,
        Dusk,
        Night,
        HighContrast,
    }

    public class CrosshairSettings
    {
        public CrosshairStyle Style = CrosshairStyle.CrossDot;
        /// <summary>Index into Settings.CrosshairColors, or -1 for a custom colour.</summary>
        public int ColorPreset = 2;
        public Color Color = new Color(0f, 1f, 1f, 1f);
        public float Opacity = 1f;
        /// <summary>Arm length in screen pixels at 1080p (scaled with the window height).</summary>
        public int Length = 6;
        public int Thickness = 2;
        public int Gap = 3;
        public int DotSize = 2;
        public bool Outline = true;
        public int OutlineThickness = 1;
        public float OutlineOpacity = 0.6f;
        public bool Dynamic;
        public int HitColorPreset = 0;

        public CrosshairSettings Clone()
        {
            return (CrosshairSettings)MemberwiseClone();
        }
    }

    public struct NamedColor
    {
        public string Name;
        public Color Color;

        public NamedColor(string name, Color c)
        {
            Name = name;
            Color = c;
        }
    }

    public static class Settings
    {
        // ---- choices ---------------------------------------------------------------------------
        public static readonly NamedColor[] CrosshairColors =
        {
            new NamedColor("White", new Color(1f, 1f, 1f)),
            new NamedColor("Green", new Color(0.2f, 1f, 0.25f)),
            new NamedColor("Cyan", new Color(0f, 1f, 1f)),
            new NamedColor("Yellow", new Color(1f, 0.95f, 0.15f)),
            new NamedColor("Red", new Color(1f, 0.2f, 0.2f)),
            new NamedColor("Magenta", new Color(1f, 0.25f, 0.95f)),
        };

        public static readonly NamedColor[] HitColors =
        {
            new NamedColor("White", new Color(1f, 1f, 1f)),
            new NamedColor("Red", new Color(1f, 0.25f, 0.25f)),
            new NamedColor("Yellow", new Color(1f, 0.88f, 0.2f)),
            new NamedColor("Cyan", new Color(0.2f, 1f, 1f)),
        };

        /// <summary>Enemy highlight. The last one is the Okabe-Ito vermillion, distinct for deuteranopia, protanopia and tritanopia against sky-blue allies.</summary>
        public static readonly NamedColor[] EnemyColors =
        {
            new NamedColor("Red", new Color(1f, 0.27f, 0.27f)),
            new NamedColor("Yellow", new Color(1f, 0.86f, 0.1f)),
            new NamedColor("Purple", new Color(0.78f, 0.35f, 1f)),
            new NamedColor("Colour-blind safe", new Color(0.90f, 0.38f, 0f)),
        };

        public static readonly NamedColor[] AllyColors =
        {
            new NamedColor("Teal", new Color(0.16f, 0.78f, 0.72f)),
            new NamedColor("Sky blue (colour-blind safe)", new Color(0.34f, 0.71f, 0.91f)),
            new NamedColor("Green", new Color(0.35f, 0.85f, 0.35f)),
        };

        public static readonly NamedColor[] AccentColors =
        {
            new NamedColor("Coral", new Color(1.00f, 0.42f, 0.33f)),
            new NamedColor("Teal", new Color(0.18f, 0.78f, 0.72f)),
            new NamedColor("Violet", new Color(0.62f, 0.48f, 1.00f)),
            new NamedColor("Gold", new Color(1.00f, 0.76f, 0.25f)),
            new NamedColor("Blue", new Color(0.30f, 0.60f, 1.00f)),
        };

        public static readonly string[] PaletteNames = { "Day", "Dusk", "Night", "High contrast" };
        public static readonly string[] StyleNames = { "Cross", "Dot", "Cross + dot", "Circle", "T-shape" };
        public static readonly int[] FrameCaps = { 60, 120, 144, 165, 240, 0 };
        public static readonly string[] FrameCapNames = { "60", "120", "144", "165", "240", "Unlimited" };

        /// <summary>Four shipped crosshairs.</summary>
        public static readonly string[] PresetNames = { "Sharp", "Pro dot", "Classic", "Ring" };

        public static CrosshairSettings Preset(int i)
        {
            switch (i)
            {
                case 1:
                    return new CrosshairSettings { Style = CrosshairStyle.Dot, ColorPreset = 1, Color = CrosshairColors[1].Color, DotSize = 4, Outline = true, OutlineThickness = 1, OutlineOpacity = 0.8f, Dynamic = false };
                case 2:
                    return new CrosshairSettings { Style = CrosshairStyle.Cross, ColorPreset = 0, Color = CrosshairColors[0].Color, Length = 8, Thickness = 2, Gap = 5, DotSize = 0, Outline = true, OutlineThickness = 1, OutlineOpacity = 0.7f, Dynamic = true };
                case 3:
                    return new CrosshairSettings { Style = CrosshairStyle.Circle, ColorPreset = 3, Color = CrosshairColors[3].Color, Length = 0, Thickness = 2, Gap = 9, DotSize = 2, Outline = true, OutlineThickness = 1, OutlineOpacity = 0.6f, Dynamic = true };
                default:
                    return new CrosshairSettings { Style = CrosshairStyle.CrossDot, ColorPreset = 2, Color = CrosshairColors[2].Color, Length = 6, Thickness = 2, Gap = 3, DotSize = 2, Outline = true, OutlineThickness = 1, OutlineOpacity = 0.5f, Dynamic = false };
            }
        }

        // ---- values ----------------------------------------------------------------------------
        /// <summary>VALORANT-style sensitivity: 0.07 degrees per mouse count times this.</summary>
        public static float ValorantSens = 1.14f;
        public static float FovH = 103f;
        public static float Volume = 0.8f;
        public static int Quality = -1;
        public static bool VSync;
        public static int FrameCap = 2;
        public static bool ShowFps;

        public static CrosshairSettings Crosshair = Preset(0);

        public static bool MinimapOn = true;
        public static bool MinimapRotate = true;
        public static float MinimapZoom = 1f;
        public static float MinimapSize = 230f;
        public static float MinimapOpacity = 0.9f;

        public static int EnemyColor;
        public static int AllyColor;
        public static int AccentColor;
        public static WorldPaletteId World = WorldPaletteId.Day;

        // ---- round 4 ------------------------------------------------------------------------------
        public static readonly string[] GfxNames = { "Low", "Medium", "High", "Ultra" };
        public static readonly string[] HandNames = { "Right", "Left" };
        /// <summary>0 Low, 1 Medium, 2 High, 3 Ultra (shadows, bloom, prop density, resolution scale).</summary>
        public static int GfxPreset = 2;
        public static bool DamageNumbers = true;
        /// <summary>How well bots aim: 0 easy, 1 normal, 2 hard, 3 pro (the bridge's AIM_DIFFICULTY).</summary>
        public static int BotDifficulty = 1;
        public static readonly string[] DifficultyIds = { "easy", "normal", "hard", "pro" };
        public static readonly string[] DifficultyNames = { "Easy", "Normal", "Hard", "Pro" };
        /// <summary>Vertical field of view of the weapon camera, degrees.</summary>
        public static float ViewmodelFov = 60f;
        /// <summary>Viewmodel offsets, -1..1 (about 4 cm each way).</summary>
        public static float ViewmodelX, ViewmodelY, ViewmodelZ;
        public static bool ViewmodelLeft;
        public static bool InvertY;
        public static bool ToggleCrouch;
        public static bool ToggleWalk;
        /// <summary>Scoped sensitivity, relative to the zoom (1 = the same turn per screen width as unscoped).</summary>
        public static float ScopedSens = 1f;

        // ---- round 5 ------------------------------------------------------------------------------
        /// <summary>The broadcast overlay (chat, alerts, goal bar, LIVE pill, camera frame) over the game.</summary>
        public static bool StreamerMode;

        /// <summary>Bumped on every change, so caches can tell they are stale.</summary>
        public static int Version { get; private set; }

        public static Color Enemy => EnemyColors[Mathf.Clamp(EnemyColor, 0, EnemyColors.Length - 1)].Color;
        public static Color Ally => AllyColors[Mathf.Clamp(AllyColor, 0, AllyColors.Length - 1)].Color;
        public static Color Accent => AccentColors[Mathf.Clamp(AccentColor, 0, AccentColors.Length - 1)].Color;
        public static Color HitColor => HitColors[Mathf.Clamp(Crosshair.HitColorPreset, 0, HitColors.Length - 1)].Color;

        /// <summary>Multiplier from GameInput's degrees-per-pixel to the VALORANT number's turn rate.</summary>
        public static float RigSensitivity => ValorantSens * 0.07f / GameInput.DegreesPerPixel;

        /// <summary>Centimetres of mouse travel for a full turn at the given DPI.</summary>
        public static float CmPer360(float dpi)
        {
            float deg = 0.07f * Mathf.Max(0.001f, ValorantSens);
            return 360f / deg / Mathf.Max(1f, dpi) * 2.54f;
        }

        // ---- persistence -----------------------------------------------------------------------
        const string P = "sw.";
        static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            ValorantSens = PlayerPrefs.GetFloat(P + "sens", ValorantSens);
            FovH = PlayerPrefs.GetFloat(P + "fov", FovH);
            Volume = PlayerPrefs.GetFloat(P + "vol", Volume);
            Quality = PlayerPrefs.GetInt(P + "quality", QualitySettings.GetQualityLevel());
            VSync = PlayerPrefs.GetInt(P + "vsync", 0) == 1;
            FrameCap = PlayerPrefs.GetInt(P + "cap", FrameCap);
            ShowFps = PlayerPrefs.GetInt(P + "fps", 0) == 1;

            CrosshairSettings c = Preset(0);
            c.Style = (CrosshairStyle)PlayerPrefs.GetInt(P + "xh.style", (int)c.Style);
            c.ColorPreset = PlayerPrefs.GetInt(P + "xh.cp", c.ColorPreset);
            c.Color = new Color(PlayerPrefs.GetFloat(P + "xh.r", c.Color.r), PlayerPrefs.GetFloat(P + "xh.g", c.Color.g), PlayerPrefs.GetFloat(P + "xh.b", c.Color.b), 1f);
            c.Opacity = PlayerPrefs.GetFloat(P + "xh.a", c.Opacity);
            c.Length = PlayerPrefs.GetInt(P + "xh.len", c.Length);
            c.Thickness = PlayerPrefs.GetInt(P + "xh.th", c.Thickness);
            c.Gap = PlayerPrefs.GetInt(P + "xh.gap", c.Gap);
            c.DotSize = PlayerPrefs.GetInt(P + "xh.dot", c.DotSize);
            c.Outline = PlayerPrefs.GetInt(P + "xh.ol", c.Outline ? 1 : 0) == 1;
            c.OutlineThickness = PlayerPrefs.GetInt(P + "xh.olt", c.OutlineThickness);
            c.OutlineOpacity = PlayerPrefs.GetFloat(P + "xh.ola", c.OutlineOpacity);
            c.Dynamic = PlayerPrefs.GetInt(P + "xh.dyn", c.Dynamic ? 1 : 0) == 1;
            c.HitColorPreset = PlayerPrefs.GetInt(P + "xh.hit", c.HitColorPreset);
            Crosshair = c;

            MinimapOn = PlayerPrefs.GetInt(P + "mm.on", 1) == 1;
            MinimapRotate = PlayerPrefs.GetInt(P + "mm.rot", 1) == 1;
            MinimapZoom = PlayerPrefs.GetFloat(P + "mm.zoom", MinimapZoom);
            MinimapSize = PlayerPrefs.GetFloat(P + "mm.size", MinimapSize);
            MinimapOpacity = PlayerPrefs.GetFloat(P + "mm.alpha", MinimapOpacity);

            EnemyColor = PlayerPrefs.GetInt(P + "col.enemy", 0);
            AllyColor = PlayerPrefs.GetInt(P + "col.ally", 0);
            AccentColor = PlayerPrefs.GetInt(P + "col.accent", 0);
            World = (WorldPaletteId)Mathf.Clamp(PlayerPrefs.GetInt(P + "col.world", 0), 0, PaletteNames.Length - 1);
            GfxPreset = PlayerPrefs.GetInt(P + "gfx", GfxPreset);
            DamageNumbers = PlayerPrefs.GetInt(P + "dmgnum", 1) == 1;
            BotDifficulty = Mathf.Clamp(PlayerPrefs.GetInt(P + "botdiff", 1), 0, 3);
            ViewmodelFov = PlayerPrefs.GetFloat(P + "vm.fov", ViewmodelFov);
            ViewmodelX = PlayerPrefs.GetFloat(P + "vm.x", 0f);
            ViewmodelY = PlayerPrefs.GetFloat(P + "vm.y", 0f);
            ViewmodelZ = PlayerPrefs.GetFloat(P + "vm.z", 0f);
            ViewmodelLeft = PlayerPrefs.GetInt(P + "vm.left", 0) == 1;
            InvertY = PlayerPrefs.GetInt(P + "inverty", 0) == 1;
            ToggleCrouch = PlayerPrefs.GetInt(P + "tog.crouch", 0) == 1;
            ToggleWalk = PlayerPrefs.GetInt(P + "tog.walk", 0) == 1;
            ScopedSens = PlayerPrefs.GetFloat(P + "sens.scoped", ScopedSens);
            StreamerMode = PlayerPrefs.GetInt(P + "streamer", 0) == 1;
            Clamp();
            Version++;
        }

        static void Clamp()
        {
            ValorantSens = Mathf.Clamp(ValorantSens, 0.05f, 5f);
            FovH = Mathf.Clamp(FovH, 80f, 110f);
            Volume = Mathf.Clamp01(Volume);
            Quality = Mathf.Clamp(Quality, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
            FrameCap = Mathf.Clamp(FrameCap, 0, FrameCaps.Length - 1);
            CrosshairSettings c = Crosshair;
            c.Style = (CrosshairStyle)Mathf.Clamp((int)c.Style, 0, StyleNames.Length - 1);
            c.ColorPreset = Mathf.Clamp(c.ColorPreset, -1, CrosshairColors.Length - 1);
            c.Opacity = Mathf.Clamp(c.Opacity, 0.1f, 1f);
            c.Length = Mathf.Clamp(c.Length, 0, 30);
            c.Thickness = Mathf.Clamp(c.Thickness, 1, 10);
            c.Gap = Mathf.Clamp(c.Gap, 0, 30);
            c.DotSize = Mathf.Clamp(c.DotSize, 0, 10);
            c.OutlineThickness = Mathf.Clamp(c.OutlineThickness, 1, 4);
            c.OutlineOpacity = Mathf.Clamp01(c.OutlineOpacity);
            c.HitColorPreset = Mathf.Clamp(c.HitColorPreset, 0, HitColors.Length - 1);
            MinimapZoom = Mathf.Clamp(MinimapZoom, 0.5f, 3f);
            MinimapSize = Mathf.Clamp(MinimapSize, 150f, 380f);
            MinimapOpacity = Mathf.Clamp(MinimapOpacity, 0.3f, 1f);
            EnemyColor = Mathf.Clamp(EnemyColor, 0, EnemyColors.Length - 1);
            AllyColor = Mathf.Clamp(AllyColor, 0, AllyColors.Length - 1);
            AccentColor = Mathf.Clamp(AccentColor, 0, AccentColors.Length - 1);
            GfxPreset = Mathf.Clamp(GfxPreset, 0, GfxNames.Length - 1);
            ViewmodelFov = Mathf.Clamp(ViewmodelFov, 45f, 80f);
            ViewmodelX = Mathf.Clamp(ViewmodelX, -1f, 1f);
            ViewmodelY = Mathf.Clamp(ViewmodelY, -1f, 1f);
            ViewmodelZ = Mathf.Clamp(ViewmodelZ, -1f, 1f);
            ScopedSens = Mathf.Clamp(ScopedSens, 0.2f, 2f);
        }

        /// <summary>Writes everything to PlayerPrefs (call when the settings screen closes, or after a hotkey toggle).</summary>
        public static void Save()
        {
            Clamp();
            PlayerPrefs.SetFloat(P + "sens", ValorantSens);
            PlayerPrefs.SetFloat(P + "fov", FovH);
            PlayerPrefs.SetFloat(P + "vol", Volume);
            PlayerPrefs.SetInt(P + "quality", Quality);
            PlayerPrefs.SetInt(P + "vsync", VSync ? 1 : 0);
            PlayerPrefs.SetInt(P + "cap", FrameCap);
            PlayerPrefs.SetInt(P + "fps", ShowFps ? 1 : 0);
            CrosshairSettings c = Crosshair;
            PlayerPrefs.SetInt(P + "xh.style", (int)c.Style);
            PlayerPrefs.SetInt(P + "xh.cp", c.ColorPreset);
            PlayerPrefs.SetFloat(P + "xh.r", c.Color.r);
            PlayerPrefs.SetFloat(P + "xh.g", c.Color.g);
            PlayerPrefs.SetFloat(P + "xh.b", c.Color.b);
            PlayerPrefs.SetFloat(P + "xh.a", c.Opacity);
            PlayerPrefs.SetInt(P + "xh.len", c.Length);
            PlayerPrefs.SetInt(P + "xh.th", c.Thickness);
            PlayerPrefs.SetInt(P + "xh.gap", c.Gap);
            PlayerPrefs.SetInt(P + "xh.dot", c.DotSize);
            PlayerPrefs.SetInt(P + "xh.ol", c.Outline ? 1 : 0);
            PlayerPrefs.SetInt(P + "xh.olt", c.OutlineThickness);
            PlayerPrefs.SetFloat(P + "xh.ola", c.OutlineOpacity);
            PlayerPrefs.SetInt(P + "xh.dyn", c.Dynamic ? 1 : 0);
            PlayerPrefs.SetInt(P + "xh.hit", c.HitColorPreset);
            PlayerPrefs.SetInt(P + "mm.on", MinimapOn ? 1 : 0);
            PlayerPrefs.SetInt(P + "mm.rot", MinimapRotate ? 1 : 0);
            PlayerPrefs.SetFloat(P + "mm.zoom", MinimapZoom);
            PlayerPrefs.SetFloat(P + "mm.size", MinimapSize);
            PlayerPrefs.SetFloat(P + "mm.alpha", MinimapOpacity);
            PlayerPrefs.SetInt(P + "col.enemy", EnemyColor);
            PlayerPrefs.SetInt(P + "col.ally", AllyColor);
            PlayerPrefs.SetInt(P + "col.accent", AccentColor);
            PlayerPrefs.SetInt(P + "col.world", (int)World);
            PlayerPrefs.SetInt(P + "gfx", GfxPreset);
            PlayerPrefs.SetInt(P + "dmgnum", DamageNumbers ? 1 : 0);
            PlayerPrefs.SetInt(P + "botdiff", BotDifficulty);
            PlayerPrefs.SetFloat(P + "vm.fov", ViewmodelFov);
            PlayerPrefs.SetFloat(P + "vm.x", ViewmodelX);
            PlayerPrefs.SetFloat(P + "vm.y", ViewmodelY);
            PlayerPrefs.SetFloat(P + "vm.z", ViewmodelZ);
            PlayerPrefs.SetInt(P + "vm.left", ViewmodelLeft ? 1 : 0);
            PlayerPrefs.SetInt(P + "inverty", InvertY ? 1 : 0);
            PlayerPrefs.SetInt(P + "tog.crouch", ToggleCrouch ? 1 : 0);
            PlayerPrefs.SetInt(P + "tog.walk", ToggleWalk ? 1 : 0);
            PlayerPrefs.SetFloat(P + "sens.scoped", ScopedSens);
            PlayerPrefs.SetInt(P + "streamer", StreamerMode ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Marks the settings changed and applies the engine-level ones (frame rate, vSync, volume).</summary>
        public static void Changed()
        {
            Clamp();
            Version++;
            ApplyEngine();
        }

        static int appliedQuality = -2;

        public static void ApplyEngine()
        {
            if (Quality != appliedQuality && Quality >= 0 && Quality < QualitySettings.names.Length)
            {
                appliedQuality = Quality;
                if (QualitySettings.GetQualityLevel() != Quality) QualitySettings.SetQualityLevel(Quality, true);
                WorldLook.ApplyQualityExtras();
            }
            // A quality level carries its own vSync count, so set ours after it.
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            int cap = FrameCaps[Mathf.Clamp(FrameCap, 0, FrameCaps.Length - 1)];
            Application.targetFrameRate = VSync ? -1 : (cap <= 0 ? -1 : cap);
            AudioListener.volume = Volume;
            Gfx.ApplyQuality();
        }
    }
}

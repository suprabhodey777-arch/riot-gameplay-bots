// Procedural UI textures for IMGUI: angular (chamfered) and rounded plates used
// as 9-sliced GUIStyle backgrounds, the glass palette, plus the type scale and
// small cached strings so the HUD does not allocate every frame.
using UnityEngine;

namespace Saltworks
{
    public static class Theme
    {
        // Type scale (virtual pixels at 900 px window height; the UI scales with the window height, so a
        // 16 px body is 13 px at 720p and 38 px on a 2146 px Retina panel). Nothing below Caption.
        public const int Display = 44;
        public const int Title = 32;
        public const int H2 = 22;
        public const int H3 = 18;
        public const int Body = 16;
        public const int Small = 14;
        public const int Caption = 12;

        // Spacing scale (an 8 px grid).
        public const float S1 = 4f;
        public const float S2 = 8f;
        public const float S3 = 12f;
        public const float S4 = 16f;
        public const float S5 = 24f;
        public const float S6 = 32f;
        public const float S7 = 48f;

        /// <summary>Standard control heights.</summary>
        public const float ButtonH = 48f;
        public const float ButtonSmallH = 40f;
        public const float RowH = 44f;

        /// <summary>Corner radius of every panel, card and button (design system: 8 px).</summary>
        public const int Radius = 8;

        // Palette (docs/art-direction.md): bone text on charcoal, never pure white blocks.
        public static readonly Color Ink = new Color(0.925f, 0.910f, 0.882f);      // #ECE8E1
        public static readonly Color Muted = new Color(0.604f, 0.639f, 0.682f);    // #9AA3AE
        public static readonly Color Faint = new Color(0.40f, 0.43f, 0.48f);
        /// <summary>The figure stage's clear colour (World/FigureStage.cs): frames around a figure use it so no seam shows.</summary>
        public static readonly Color StageC = new Color(0.02f, 0.025f, 0.04f, 1f);
        /// <summary>The UI base, #0D1014.</summary>
        public static readonly Color Base = new Color(0.051f, 0.063f, 0.078f);
        // Panels #151A21 at about 92%.
        public static readonly Color PanelC = new Color(0.082f, 0.102f, 0.129f, 0.92f);
        public static readonly Color CardC = new Color(0.115f, 0.138f, 0.170f, 0.94f);
        public static readonly Color ButtonC = new Color(0.145f, 0.170f, 0.205f, 0.96f);
        public static readonly Color ButtonHoverC = new Color(0.20f, 0.23f, 0.275f, 1f);
        public static readonly Color ButtonDownC = new Color(0.09f, 0.105f, 0.13f, 1f);
        /// <summary>Text on an accent-filled control.</summary>
        public static readonly Color OnAccent = new Color(0.04f, 0.04f, 0.05f);
        /// <summary>Hairlines and quiet outlines.</summary>
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.10f);
        /// <summary>The dark inset behind controls (tracks, fields, segmented bars).</summary>
        public static readonly Color WellC = new Color(0.04f, 0.05f, 0.063f, 0.9f);
        /// <summary>The broadcast's second neon (cyan), used against the accent in lines and glitches.</summary>
        public static readonly Color Neon2 = new Color(0.25f, 0.95f, 1f);
        /// <summary>Chamfer of the cut corners (top-left and bottom-right) on panels and buttons.</summary>
        public const int Cut = 0;
        /// <summary>The one chamfer in the system: the bottom-right corner of a primary button.</summary>
        public const int PrimaryCut = 9;

        /// <summary>
        /// An angular plate: a rectangle with the top-left and bottom-right corners cut at 45 degrees (`cut` px), an
        /// optional inner stroke, anti-aliased. Use with GUIStyle.border = Border(cut) so IMGUI 9-slices it.
        /// </summary>
        public static Texture2D Chamfer(Color fill, int cut, Color stroke, float strokeW = 1f, bool topLeft = true)
        {
            int n = cut * 2 + 8;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    // Signed distance to the outline (negative inside): the box, minus the two cut corners.
                    float box = Mathf.Max(Mathf.Max(-fx, fx - n), Mathf.Max(-fy, fy - n));
                    // Texture y is up: the top-left cut is x + (n - y) = cut, the bottom-right one (n - x) + y = cut.
                    float dTl = topLeft ? (cut - (fx + (n - fy))) * 0.70710678f : -1000f;
                    float dBr = (cut - ((n - fx) + fy)) * 0.70710678f;
                    float d = Mathf.Max(box, Mathf.Max(dTl, dBr));
                    float a = Mathf.Clamp01(0.5f - d);
                    Color c = fill;
                    if (stroke.a > 0f && strokeW > 0f)
                    {
                        float s = Mathf.Clamp01(d + strokeW + 0.5f);
                        c = Color.Lerp(fill, new Color(stroke.r, stroke.g, stroke.b, Mathf.Max(fill.a, stroke.a)), s * stroke.a);
                    }
                    c.a *= a;
                    px[y * n + x] = c;
                }
            }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }

        public static Texture2D Chamfer(Color fill, int cut)
        {
            return Chamfer(fill, cut, new Color(0f, 0f, 0f, 0f), 0f);
        }

        /// <summary>
        /// A rounded rectangle texture (radius in pixels), with an optional inner stroke. Use with
        /// GUIStyle.border = Border(radius) so IMGUI 9-slices it at any size.
        /// </summary>
        public static Texture2D Rounded(Color fill, int radius, Color stroke, float strokeW = 1f)
        {
            int n = radius * 2 + 8;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            float half = n / 2f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float a = Mathf.Clamp01(0.5f - outside);
                    Color c = fill;
                    if (stroke.a > 0f && strokeW > 0f)
                    {
                        float s = Mathf.Clamp01(outside + strokeW + 0.5f);
                        c = Color.Lerp(fill, new Color(stroke.r, stroke.g, stroke.b, Mathf.Max(fill.a, stroke.a)), s * stroke.a);
                    }
                    c.a *= a;
                    px[y * n + x] = c;
                }
            }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }

        public static Texture2D Rounded(Color fill, int radius)
        {
            return Rounded(fill, radius, new Color(0f, 0f, 0f, 0f), 0f);
        }

        public static RectOffset Border(int radius)
        {
            int b = radius + 2;
            return new RectOffset(b, b, b, b);
        }

        public static Color Lighten(Color c, float k)
        {
            return Color.Lerp(c, Color.white, k);
        }

        public static Color Darken(Color c, float k)
        {
            Color d = Color.Lerp(c, Color.black, k);
            d.a = c.a;
            return d;
        }

        /// <summary>Ease-out cubic.</summary>
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }

        // ---- fonts ---------------------------------------------------------------------------------
        // Runtime OS fonts (no font files in the project): a condensed display face for titles and numbers, a
        // humanist sans for text. Unity's text engine loads an OS face by FAMILY name in its Regular style, so
        // a family with no Regular cut ("DIN Condensed" is Bold only) cannot be asked for by name: that one is
        // loaded from its font file instead. A candidate that still fails to load (the engine logs it, and
        // draws nothing) is dropped at once and the next one is tried; the last resort is the built-in font.
        struct Face
        {
            public string File;    // a font file name to find among the OS font paths, or null
            public string Family;  // an OS family name
            public bool Bold;      // ask for the bold style
        }

        static readonly Face[] DisplayFaces =
        {
            new Face { File = "DIN Condensed Bold.ttf", Family = "DIN Condensed", Bold = false },
            new Face { Family = "Avenir Next Condensed", Bold = true },
            new Face { Family = "Arial Narrow", Bold = true },
            new Face { Family = "Impact", Bold = false },
        };
        static readonly string[] BodyFamilies = { "Avenir Next", "Helvetica Neue", "Arial" };
        static Font displayFont, bodyFont, bodyBoldFont;
        static bool fontsTried, displayBold;
        static int displayIndex, bodyIndex;
        static string displayName, bodyName;
        static System.Collections.Generic.HashSet<string> osNames;
        static string[] osPaths;

        /// <summary>Bumped when a face turns out not to load, so the UI rebuilds its styles without it.</summary>
        public static int FontVersion { get; private set; }
        /// <summary>The display face is asked for in bold.</summary>
        public static bool DisplayUsesBold { get { LoadFonts(); return displayBold; } }
        /// <summary>True when a condensed display face was found.</summary>
        public static bool DisplayIsReal { get { LoadFonts(); return displayFont != null; } }

        static void LoadFonts()
        {
            if (fontsTried) return;
            fontsTried = true;
            try
            {
                osNames = new System.Collections.Generic.HashSet<string>(Font.GetOSInstalledFontNames(), System.StringComparer.OrdinalIgnoreCase);
                osPaths = Font.GetPathsToOSFonts();
                Application.logMessageReceived += OnLog;
                PickBody();
                PickDisplay();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("UI fonts: falling back to the built-in font (" + e.Message + ")");
            }
        }

        static void PickBody()
        {
            bodyFont = bodyBoldFont = null;
            bodyName = null;
            for (; bodyIndex < BodyFamilies.Length; bodyIndex++)
            {
                string f = BodyFamilies[bodyIndex];
                if (!osNames.Contains(f)) continue;
                bodyName = f;
                bodyFont = Font.CreateDynamicFontFromOSFont(f, 16);
                // A second font object for bold text keeps the two styles' glyph caches apart.
                bodyBoldFont = Font.CreateDynamicFontFromOSFont(f, 16);
                bodyFont.hideFlags = bodyBoldFont.hideFlags = HideFlags.DontSave;
                break;
            }
            Debug.Log("UI fonts: body=" + (bodyName ?? "built-in"));
        }

        static void PickDisplay()
        {
            displayFont = null;
            displayName = null;
            for (; displayIndex < DisplayFaces.Length; displayIndex++)
            {
                Face f = DisplayFaces[displayIndex];
                Font made = null;
                if (f.File != null)
                {
                    foreach (string p in osPaths)
                    {
                        if (!p.EndsWith("/" + f.File, System.StringComparison.OrdinalIgnoreCase) && !p.EndsWith("\\" + f.File, System.StringComparison.OrdinalIgnoreCase)) continue;
                        made = new Font(p);
                        break;
                    }
                }
                else if (osNames.Contains(f.Family) || (f.Bold && osNames.Contains(f.Family + " Bold"))) made = Font.CreateDynamicFontFromOSFont(f.Family, 32);
                if (made == null) continue;
                made.hideFlags = HideFlags.DontSave;
                displayFont = made;
                displayName = f.Family;
                displayBold = f.Bold;
                break;
            }
            Debug.Log("UI fonts: display=" + (displayName ?? "built-in") + (displayFont != null && DisplayFaces[displayIndex].File != null ? " (from file)" : "") + (displayBold ? " (bold)" : ""));
        }

        static bool Names(string condition, Font f, string label)
        {
            if (f == null) return false;
            if (label != null && condition.Contains("[" + label + "]")) return true;
            if (!string.IsNullOrEmpty(f.name) && condition.Contains("[" + f.name + "]")) return true;
            string[] names = f.fontNames;
            if (names != null) foreach (string n in names) if (!string.IsNullOrEmpty(n) && condition.Contains("[" + n + "]")) return true;
            return false;
        }

        /// <summary>A face the text engine cannot load draws nothing: drop it, try the next, and let the styles rebuild.</summary>
        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (condition == null || !condition.StartsWith("Unable to load font face for [")) return;
            if (Names(condition, displayFont, displayName))
            {
                displayIndex++;
                PickDisplay();
                FontVersion++;
            }
            else if (Names(condition, bodyFont, bodyName))
            {
                bodyIndex++;
                PickBody();
                FontVersion++;
            }
        }

        /// <summary>The display face (condensed bold): titles, buttons and numbers. Null means the built-in font.</summary>
        public static Font DisplayFont { get { LoadFonts(); return displayFont; } }
        /// <summary>The body face. Null means the built-in font.</summary>
        public static Font BodyFont { get { LoadFonts(); return bodyFont; } }
        /// <summary>The body face again, for bold text (ask for FontStyle.Bold).</summary>
        public static Font BodyBoldFont { get { LoadFonts(); return bodyBoldFont; } }

        // ---- cached strings ---------------------------------------------------------------------
        static readonly string[] ints = new string[1001];

        /// <summary>n as a string without allocating for 0..1000.</summary>
        public static string Int(int n)
        {
            if (n < 0 || n > 1000) return n.ToString();
            return ints[n] ??= n.ToString();
        }

        static readonly string[] pcts = new string[101];

        public static string Percent(int n)
        {
            n = Mathf.Clamp(n, 0, 100);
            return pcts[n] ??= n + "%";
        }
    }
}

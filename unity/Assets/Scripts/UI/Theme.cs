// Procedural UI textures for IMGUI: angular (chamfered) and rounded plates used
// as 9-sliced GUIStyle backgrounds, the glass palette, plus the type scale and
// small cached strings so the HUD does not allocate every frame.
using UnityEngine;

namespace Saltworks
{
    public static class Theme
    {
        // Type scale (virtual pixels at 900 px window height).
        public const int Display = 40;
        public const int Title = 30;
        public const int H2 = 20;
        public const int H3 = 16;
        public const int Body = 15;
        public const int Small = 13;
        public const int Caption = 11;

        // Spacing scale.
        public const float S1 = 4f;
        public const float S2 = 8f;
        public const float S3 = 12f;
        public const float S4 = 16f;
        public const float S5 = 24f;

        /// <summary>Corner radius of every panel, card and button (design system: 8 px).</summary>
        public const int Radius = 8;

        public static readonly Color Ink = new Color(0.96f, 0.96f, 0.97f);
        public static readonly Color Muted = new Color(0.64f, 0.68f, 0.76f);
        public static readonly Color Faint = new Color(0.42f, 0.46f, 0.55f);
        // Dark glass: blue-black, translucent enough that the map behind reads through.
        public static readonly Color PanelC = new Color(0.035f, 0.042f, 0.062f, 0.86f);
        public static readonly Color CardC = new Color(0.075f, 0.085f, 0.12f, 0.92f);
        public static readonly Color ButtonC = new Color(0.12f, 0.135f, 0.18f, 0.96f);
        public static readonly Color ButtonHoverC = new Color(0.18f, 0.2f, 0.27f, 1f);
        public static readonly Color ButtonDownC = new Color(0.08f, 0.09f, 0.12f, 1f);
        /// <summary>The broadcast's second neon (cyan), used against the accent in lines and glitches.</summary>
        public static readonly Color Neon2 = new Color(0.25f, 0.95f, 1f);
        /// <summary>Chamfer of the cut corners (top-left and bottom-right) on panels and buttons.</summary>
        public const int Cut = 10;

        /// <summary>
        /// An angular plate: a rectangle with the top-left and bottom-right corners cut at 45 degrees (`cut` px), an
        /// optional inner stroke, anti-aliased. Use with GUIStyle.border = Border(cut) so IMGUI 9-slices it.
        /// </summary>
        public static Texture2D Chamfer(Color fill, int cut, Color stroke, float strokeW = 1f)
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
                    float dTl = (cut - (fx + (n - fy))) * 0.70710678f;
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

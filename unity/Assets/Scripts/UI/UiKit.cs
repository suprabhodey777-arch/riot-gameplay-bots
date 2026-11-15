// The broadcast look's procedural assets: interface sounds (hover tick, confirm,
// alert sting, intro hit, transition whoosh) and textures (scanlines, vignette,
// soft glow, role glyphs, headshot and wallbang marks). Built once, shared,
// never assets on disk.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public static class UiSound
    {
        const int Rate = 44100;
        static AudioClip tick, confirm, sting, hit, whoosh, chat, back;

        static AudioClip Make(string name, float seconds, System.Func<float, float> f)
        {
            int n = Mathf.Max(1, (int)(seconds * Rate));
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
            AudioClip clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A short bright blip for hovering a control.</summary>
        public static AudioClip Tick => tick != null ? tick : tick = Make("ui_tick", 0.035f, t =>
            Mathf.Sin(2f * Mathf.PI * 3400f * t) * 0.22f * Mathf.Exp(-t * 140f) * Mathf.Clamp01(t * 4000f));

        /// <summary>Two rising notes for a click.</summary>
        public static AudioClip Confirm => confirm != null ? confirm : confirm = Make("ui_confirm", 0.12f, t =>
        {
            float f = t < 0.045f ? 1480f : 2220f;
            return Mathf.Sin(2f * Mathf.PI * f * t) * 0.25f * Mathf.Exp(-(t < 0.045f ? t : t - 0.045f) * 45f) * Mathf.Clamp01(t * 3000f);
        });

        /// <summary>A falling note (back, cancel).</summary>
        public static AudioClip Back => back != null ? back : back = Make("ui_back", 0.09f, t =>
            Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(1500f, 900f, t / 0.09f) * t) * 0.2f * Mathf.Exp(-t * 35f) * Mathf.Clamp01(t * 3000f));

        /// <summary>The alert sting: a synth stab (a fifth) with a noise swell, for chat alerts and round banners.</summary>
        public static AudioClip Sting
        {
            get
            {
                if (sting != null) return sting;
                var rng = new System.Random(77);
                float lp = 0f;
                sting = Make("ui_sting", 0.9f, t =>
                {
                    float env = Mathf.Clamp01(t * 200f) * Mathf.Exp(-t * 4.2f);
                    float saw(float f) => 2f * Mathf.Repeat(f * t, 1f) - 1f;
                    float stab = (saw(220f) + saw(330f) * 0.8f + saw(440.5f) * 0.5f) * 0.12f;
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp += (noise - lp) * 0.08f;
                    float swell = lp * 0.5f * Mathf.Exp(-t * 7f);
                    float sub = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.35f * Mathf.Exp(-t * 5f);
                    return (stab + swell) * env + sub;
                });
                return sting;
            }
        }

        /// <summary>The intro's impact: a low boom and a bright ring.</summary>
        public static AudioClip Hit
        {
            get
            {
                if (hit != null) return hit;
                var rng = new System.Random(91);
                hit = Make("ui_hit", 1.4f, t =>
                {
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    float boom = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(90f, 38f, Mathf.Clamp01(t * 2f)) * t) * Mathf.Exp(-t * 3.2f) * 0.7f;
                    float crack = noise * Mathf.Exp(-t * 30f) * 0.5f;
                    float ring = (Mathf.Sin(2f * Mathf.PI * 1760f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 2637f * t) * 0.4f) * 0.08f * Mathf.Exp(-t * 2.5f);
                    return boom + crack + ring;
                });
                return hit;
            }
        }

        /// <summary>The screen-change swoosh: band-passed noise sweeping up.</summary>
        public static AudioClip Whoosh
        {
            get
            {
                if (whoosh != null) return whoosh;
                var rng = new System.Random(12);
                float lp = 0f, bp = 0f;
                whoosh = Make("ui_whoosh", 0.32f, t =>
                {
                    float x = t / 0.32f;
                    float env = Mathf.Sin(Mathf.PI * x);
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp += (noise - lp) * Mathf.Lerp(0.04f, 0.4f, x);
                    bp += (lp - bp) * 0.35f;
                    return (lp - bp) * 1.6f * env * env;
                });
                return whoosh;
            }
        }

        /// <summary>A soft pop for a chat message.</summary>
        public static AudioClip Chat => chat != null ? chat : chat = Make("ui_chat", 0.05f, t =>
            Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.12f * Mathf.Exp(-t * 90f) * Mathf.Clamp01(t * 3000f));
    }

    public static class UiTex
    {
        static Texture2D scan, vignette, glow, hBar, noise;
        static readonly Dictionary<string, Texture2D> glyphs = new Dictionary<string, Texture2D>();

        static Texture2D New(string name, int w, int h, TextureWrapMode wrap)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, hideFlags = HideFlags.DontSave, wrapMode = wrap, filterMode = FilterMode.Bilinear };
        }

        /// <summary>Horizontal scanlines, 1 dark line every 3 px (tile with DrawTextureWithTexCoords).</summary>
        public static Texture2D Scanlines
        {
            get
            {
                if (scan != null) return scan;
                scan = New("UiScan", 1, 3, TextureWrapMode.Repeat);
                scan.filterMode = FilterMode.Point;
                scan.SetPixels(new[] { new Color(0f, 0f, 0f, 1f), new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.25f) });
                scan.Apply(false);
                return scan;
            }
        }

        /// <summary>Clear centre, opaque white edges (tint for the low-health and broadcast vignettes).</summary>
        public static Texture2D Vignette
        {
            get
            {
                if (vignette != null) return vignette;
                const int n = 128;
                vignette = New("UiVignette", n, n, TextureWrapMode.Clamp);
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float a = Mathf.Clamp01((r - 0.55f) / 0.6f);
                    a = a * a * (3f - 2f * a);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                vignette.SetPixels32(px);
                vignette.Apply(false);
                return vignette;
            }
        }

        /// <summary>A soft round glow (white, alpha falloff), for neon halos.</summary>
        public static Texture2D Glow
        {
            get
            {
                if (glow != null) return glow;
                const int n = 64;
                glow = New("UiGlow", n, n, TextureWrapMode.Clamp);
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f).magnitude / (n / 2f);
                    float a = Mathf.Clamp01(1f - r);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
                glow.SetPixels32(px);
                glow.Apply(false);
                return glow;
            }
        }

        /// <summary>A vertical soft bar: bright centre line fading up and down (the halo of a neon line).</summary>
        public static Texture2D NeonHalo
        {
            get
            {
                if (hBar != null) return hBar;
                const int h = 32;
                hBar = New("UiNeonHalo", 2, h, TextureWrapMode.Clamp);
                var px = new Color32[2 * h];
                for (int y = 0; y < h; y++)
                {
                    float v = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;
                    float a = Mathf.Pow(Mathf.Clamp01(1f - v), 2.2f);
                    px[y * 2] = px[y * 2 + 1] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                hBar.SetPixels32(px);
                hBar.Apply(false);
                return hBar;
            }
        }

        /// <summary>Blocky noise for glitch slices (point-filtered).</summary>
        public static Texture2D Noise
        {
            get
            {
                if (noise != null) return noise;
                const int n = 64;
                noise = New("UiNoise", n, n, TextureWrapMode.Repeat);
                noise.filterMode = FilterMode.Point;
                var rng = new System.Random(5);
                var px = new Color32[n * n];
                for (int i = 0; i < px.Length; i++)
                {
                    byte v = (byte)rng.Next(120, 255);
                    px[i] = new Color32(v, v, v, (byte)rng.Next(0, 255));
                }
                noise.SetPixels32(px);
                noise.Apply(false);
                return noise;
            }
        }

        // ---- glyphs (64 x 64, white on clear) ------------------------------------------------------
        static float Inside(float edge, float d)
        {
            return Mathf.Clamp01((edge - d) / 1.5f + 0.5f);
        }

        static float Seg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return (a + ab * t - p).magnitude;
        }

        /// <summary>"headshot" (a head in a reticle), "wallbang" (a bullet through a wall), "sentinel" (a shield),
        /// "duelist" (a blade), "controller" (a cloud), "initiator" (an eye), "persona" (a star), "live" (a dot).</summary>
        public static Texture2D Glyph(string id)
        {
            if (string.IsNullOrEmpty(id)) id = "persona";
            if (glyphs.TryGetValue(id, out Texture2D t) && t != null) return t;
            const int n = 64;
            t = New("UiGlyph " + id, n, n, TextureWrapMode.Clamp);
            var px = new Color32[n * n];
            var c = new Vector2(32f, 32f);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float v = 0f;
                switch (id)
                {
                    case "headshot":
                    {
                        float d = (p - c).magnitude;
                        v = Inside(2.2f, Mathf.Abs(d - 25f));
                        v = Mathf.Max(v, Inside(9.5f, (p - new Vector2(32f, 36f)).magnitude));
                        v = Mathf.Max(v, Inside(3f, Seg(p, new Vector2(18f, 16f), new Vector2(46f, 16f))) * (p.y < 26f ? 1f : 0f));
                        if (Mathf.Abs(p.x - 32f) < 2f && (p.y < 9f || p.y > 55f)) v = 1f;
                        if (Mathf.Abs(p.y - 32f) < 2f && (p.x < 9f || p.x > 55f)) v = 1f;
                        break;
                    }
                    case "wallbang":
                    {
                        bool wall = p.x > 26f && p.x < 38f && p.y > 6f && p.y < 58f && Mathf.Repeat(p.y, 13f) > 1.5f;
                        v = wall ? 0.55f : 0f;
                        float arrow = Seg(p, new Vector2(4f, 32f), new Vector2(56f, 32f));
                        v = Mathf.Max(v, Inside(3f, arrow));
                        v = Mathf.Max(v, Inside(3f, Mathf.Min(Seg(p, new Vector2(60f, 32f), new Vector2(48f, 42f)), Seg(p, new Vector2(60f, 32f), new Vector2(48f, 22f)))));
                        break;
                    }
                    case "sentinel":
                    {
                        // A shield: flat top, sides curving to a point.
                        float hw = p.y > 40f ? 22f : 22f * Mathf.Sqrt(Mathf.Clamp01((p.y - 6f) / 34f));
                        bool inside = p.y > 6f && p.y < 58f && Mathf.Abs(p.x - 32f) < hw;
                        bool inner = p.y > 12f && p.y < 52f && Mathf.Abs(p.x - 32f) < hw - 6f;
                        v = inside ? (inner ? 0.35f : 1f) : 0f;
                        if (Mathf.Abs(p.x - 32f) < 2.5f && p.y > 14f && p.y < 50f) v = 1f;
                        break;
                    }
                    case "duelist":
                    {
                        float blade = Seg(p, new Vector2(14f, 12f), new Vector2(50f, 52f));
                        v = Inside(4.5f, blade);
                        v = Mathf.Max(v, Inside(3f, Seg(p, new Vector2(10f, 30f), new Vector2(30f, 8f))));
                        break;
                    }
                    case "controller":
                        v = Mathf.Max(Inside(13f, (p - new Vector2(22f, 28f)).magnitude), Inside(16f, (p - new Vector2(36f, 34f)).magnitude));
                        v = Mathf.Max(v, Inside(11f, (p - new Vector2(46f, 26f)).magnitude));
                        if (p.y < 16f) v = 0f;
                        break;
                    case "initiator":
                    {
                        // An eye: a lens shape with a pupil.
                        Vector2 d = p - c;
                        float lens = Mathf.Abs(d.y) - 16f * (1f - (d.x * d.x) / (26f * 26f));
                        v = Inside(0f, lens) * (Mathf.Abs(d.x) < 26f ? 1f : 0f);
                        float pupil = d.magnitude;
                        if (pupil < 10f) v = pupil < 5f ? 1f : 0.2f;
                        break;
                    }
                    case "live":
                        v = Inside(14f, (p - c).magnitude);
                        break;
                    default:
                    {
                        // A five-point star.
                        Vector2 d = p - c;
                        float ang = Mathf.Atan2(d.y, d.x) + Mathf.PI / 2f;
                        float k = Mathf.Abs(Mathf.Cos(ang * 2.5f));
                        float r = 12f + 16f * Mathf.Pow(k, 3f);
                        v = Inside(r, d.magnitude);
                        break;
                    }
                }
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(v) * 255f));
            }
            t.SetPixels32(px);
            t.Apply(false);
            glyphs[id] = t;
            return t;
        }

        /// <summary>The role glyph for an agent role ("duelist", "controller", "initiator", "sentinel").</summary>
        public static Texture2D Role(string role)
        {
            switch ((role ?? "").ToLowerInvariant())
            {
                case "duelist": return Glyph("duelist");
                case "controller": return Glyph("controller");
                case "initiator": return Glyph("initiator");
                case "sentinel": return Glyph("sentinel");
                default: return Glyph("persona");
            }
        }
    }
}

// Colours, runtime materials (built-in render pipeline), procedural textures,
// synthesised sounds and small geometry helpers. Nothing here is an asset.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    public static class Palette
    {
        public static readonly Color Attack = new Color(1.00f, 0.42f, 0.29f);
        public static readonly Color Defense = new Color(0.16f, 0.70f, 0.66f);
        public static readonly Color Sky = new Color(0.80f, 0.86f, 0.91f);
        public static readonly Color Fog = new Color(0.84f, 0.87f, 0.90f);
        public static readonly Color Floor = new Color(0.86f, 0.84f, 0.80f);
        public static readonly Color Ground = new Color(0.72f, 0.70f, 0.66f);
        public static readonly Color Wall = new Color(0.93f, 0.90f, 0.85f);
        public static readonly Color WallTop = new Color(0.97f, 0.95f, 0.92f);
        public static readonly Color Outer = new Color(0.78f, 0.80f, 0.84f);
        public static readonly Color Pillar = new Color(0.84f, 0.85f, 0.92f);
        public static readonly Color Crate = new Color(0.93f, 0.79f, 0.62f);
        public static readonly Color CrateTop = new Color(0.97f, 0.87f, 0.72f);
        public static readonly Color Smoke = new Color(0.86f, 0.84f, 0.93f);
        public static readonly Color Spike = new Color(1.00f, 0.25f, 0.35f);
        public static readonly Color Gun = new Color(0.22f, 0.24f, 0.29f);
        public static readonly Color Ok = new Color(0.30f, 0.78f, 0.47f);
        public static readonly Color Bad = new Color(0.90f, 0.30f, 0.30f);
        public static readonly Color Warn = new Color(0.95f, 0.65f, 0.15f);

        public static Color Team(string team)
        {
            return team == "defense" ? Defense : Attack;
        }

        /// <summary>The player's colour settings: enemies in the enemy highlight, teammates in the ally colour.</summary>
        public static Color Relation(string team, string playerTeam)
        {
            return team == playerTeam ? Settings.Ally : Settings.Enemy;
        }

        public static Color Accent => Settings.Accent;

        public static Color Site(string letter)
        {
            switch (letter)
            {
                case "A": return new Color(1.00f, 0.70f, 0.55f);
                case "B": return new Color(0.70f, 0.78f, 1.00f);
                default: return new Color(0.66f, 0.92f, 0.76f);
            }
        }

        public static Color Severity(float s)
        {
            return s >= 0.6f ? Bad : s >= 0.3f ? Warn : Ok;
        }
    }

    public static class Mats
    {
        static readonly Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>();
        static readonly Dictionary<Color, Material> LitCache = new Dictionary<Color, Material>();

        static Shader Find(params string[] names)
        {
            foreach (string n in names)
            {
                if (Shaders.TryGetValue(n, out Shader cached) && cached != null) return cached;
                Shader s = Shader.Find(n);
                if (s != null)
                {
                    Shaders[n] = s;
                    return s;
                }
            }
            Debug.LogWarning("Saltworks: none of these shaders was found: " + string.Join(", ", names));
            return null;
        }

        static Material Make(Shader s, Color c)
        {
            if (s == null) s = Find("Unlit/Color", "Sprites/Default", "Hidden/InternalErrorShader");
            var m = new Material(s);
            if (m.HasProperty("_Color")) m.color = c;
            return m;
        }

        /// <summary>Opaque lit material (Standard), cached per colour.</summary>
        public static Material Lit(Color c)
        {
            if (LitCache.TryGetValue(c, out Material m) && m != null) return m;
            m = Make(Find("Standard", "Unlit/Color"), c);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.12f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            LitCache[c] = m;
            return m;
        }

        public static Material LitTextured(Color c, Texture tex, float glossiness)
        {
            Material m = Make(Find("Standard", "Unlit/Texture", "Unlit/Color"), c);
            if (m.HasProperty("_MainTex")) m.mainTexture = tex;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", glossiness);
            return m;
        }

        /// <summary>Lit with a glow (Standard emission), for the spike and the gun's accents.</summary>
        public static Material Emissive(Color c, float strength)
        {
            Material m = Make(Find("Standard", "Unlit/Color"), c);
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * strength);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return m;
        }

        public static Material Unlit(Color c)
        {
            return Make(Find("Unlit/Color", "Standard"), c);
        }

        /// <summary>
        /// Coloured unlit silhouette drawn on top of everything (ZTest Always, Assets/Resources/SaltworksReveal.shader),
        /// for enemies revealed by recon. Falls back to an ordinary transparent material if the shader is missing.
        /// </summary>
        public static Material Reveal(Color c)
        {
            Shader s = Find("Saltworks/RevealThroughWalls");
            Material m = s != null ? new Material(s) { color = c } : Transparent(c, 4000);
            m.renderQueue = 4000;
            return m;
        }

        /// <summary>Alpha-blended material; colour alpha is the opacity.</summary>
        public static Material Transparent(Color c, int queue)
        {
            Shader s = Find("Legacy Shaders/Transparent/Diffuse", "Sprites/Default");
            Material m;
            if (s != null)
            {
                m = Make(s, c);
            }
            else
            {
                // Standard in Fade mode as a last resort.
                m = Make(Find("Standard"), c);
                m.SetFloat("_Mode", 2f);
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_ALPHABLEND_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            m.renderQueue = queue;
            return m;
        }

        /// <summary>Vertex-coloured unlit material for LineRenderers.</summary>
        public static Material Line()
        {
            Material m = Make(Find("Sprites/Default", "Unlit/Color"), Color.white);
            m.renderQueue = 3300;
            return m;
        }

        /// <summary>Depth-tested material for TextMesh labels (the font's own material draws through walls).</summary>
        public static Material WorldText(Font font)
        {
            Shader s = Find("Saltworks/WorldText");
            Material m = s != null ? new Material(s) : new Material(font.material);
            m.mainTexture = font.material.mainTexture;
            m.renderQueue = 3100;
            return m;
        }

        public static Font DefaultFont()
        {
            // Unity 2022.2+ ships LegacyRuntime.ttf as the built-in font (Arial.ttf was removed).
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        static Texture2D gradient;

        /// <summary>A vertical fade: opaque at v = 0, clear at v = 1 (smooth falloff). Shared.</summary>
        public static Texture2D GradientTexture()
        {
            if (gradient != null) return gradient;
            const int n = 64;
            gradient = new Texture2D(4, n, TextureFormat.RGBA32, false) { name = "SaltworksGradient", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color[4 * n];
            for (int y = 0; y < n; y++)
            {
                float v = y / (float)(n - 1);
                float a = (1f - v) * (1f - v);
                for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color(1f, 1f, 1f, a);
            }
            gradient.SetPixels(px);
            gradient.Apply(false);
            return gradient;
        }

        /// <summary>Soft concrete tiles: 1 texture repeat = 4 m.</summary>
        public static Texture2D FloorTexture()
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "SaltworksFloor", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color[n * n];
            var rng = new System.Random(7);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float v = 1f - (float)rng.NextDouble() * 0.035f;
                    bool seam = x % 64 < 2 || y % 64 < 2;
                    if (seam) v *= 0.86f;
                    px[y * n + x] = new Color(v, v, v, 1f);
                }
            }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        /// <summary>Wall panels with a horizontal seam: 1 repeat = 2 m.</summary>
        public static Texture2D WallTexture()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "SaltworksWall", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color[n * n];
            var rng = new System.Random(3);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float v = 1f - (float)rng.NextDouble() * 0.03f;
                    if (y < 1 || x < 1) v *= 0.9f;
                    px[y * n + x] = new Color(v, v, v, 1f);
                }
            }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }
    }

    /// <summary>Short procedural sounds, so the project needs no audio assets.</summary>
    public static class Synth
    {
        const int Rate = 44100;

        static AudioClip Make(string name, float seconds, System.Func<float, float> f)
        {
            int n = Mathf.Max(1, (int)(seconds * Rate));
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
            AudioClip clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static AudioClip Shot()
        {
            var rng = new System.Random(1);
            return Make("shot", 0.16f, t =>
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float body = Mathf.Sin(2f * Mathf.PI * 110f * t) * Mathf.Exp(-t * 30f);
                return (noise * Mathf.Exp(-t * 38f) * 0.55f) + body * 0.5f;
            });
        }

        public static AudioClip Beep()
        {
            return Make("beep", 0.09f, t => Mathf.Sin(2f * Mathf.PI * 1760f * t) * 0.35f * Mathf.Clamp01((0.09f - t) * 40f));
        }

        public static AudioClip Tick()
        {
            return Make("hit", 0.05f, t => Mathf.Sin(2f * Mathf.PI * 2400f * t) * 0.3f * Mathf.Exp(-t * 80f));
        }

        /// <summary>A boot on concrete: a low thump and a short low-passed scuff. Seeds give variants.</summary>
        public static AudioClip Footstep(int seed)
        {
            var rng = new System.Random(100 + seed);
            float pitch = 70f + seed * 9f;
            float lp = 0f;
            float k = 0.18f + seed * 0.03f;
            return Make("footstep" + seed, 0.13f, t =>
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * k; // one-pole low-pass: a dull scuff, not a hiss
                float thump = Mathf.Sin(2f * Mathf.PI * pitch * t) * Mathf.Exp(-t * 45f);
                float heel = t < 0.004f ? t / 0.004f : 1f;
                return (thump * 0.7f + lp * 1.6f * Mathf.Exp(-t * 32f)) * heel;
            });
        }

        /// <summary>The flash going off: a bright crack with a ringing tail.</summary>
        public static AudioClip FlashPop()
        {
            var rng = new System.Random(5);
            return Make("flashpop", 0.5f, t =>
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float crack = noise * Mathf.Exp(-t * 26f) * 0.8f;
                float ring = Mathf.Sin(2f * Mathf.PI * 3100f * t) * 0.12f * Mathf.Exp(-t * 6f);
                return crack + ring;
            });
        }

        /// <summary>A soft throw whoosh with a rising pitch (also used for the dash, longer).</summary>
        public static AudioClip Whoosh(float seconds, int seed)
        {
            var rng = new System.Random(20 + seed);
            float bp = 0f, lp = 0f;
            return Make("whoosh" + seed, seconds, t =>
            {
                float x = t / seconds;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(x)) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(x));
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float k = Mathf.Lerp(0.05f, 0.35f, x);
                lp += (noise - lp) * k;
                bp += (lp - bp) * 0.5f;
                return (lp - bp) * 2.4f * env;
            });
        }

        /// <summary>The recon dart hitting the floor, then its sonar ping.</summary>
        public static AudioClip DartThunk()
        {
            return Make("dart", 0.08f, t => Mathf.Sin(2f * Mathf.PI * 520f * t * (1f - t * 4f)) * 0.5f * Mathf.Exp(-t * 60f));
        }

        public static AudioClip ReconPing()
        {
            return Make("ping", 0.7f, t =>
            {
                float f = Mathf.Lerp(1320f, 990f, Mathf.Clamp01(t / 0.7f));
                return Mathf.Sin(2f * Mathf.PI * f * t) * 0.35f * Mathf.Exp(-t * 5f) * Mathf.Clamp01(t * 300f);
            });
        }

        /// <summary>The high whine heard while blinded.</summary>
        public static AudioClip BlindRing()
        {
            return Make("blindring", 1.8f, t =>
            {
                float env = Mathf.Clamp01(t * 40f) * Mathf.Exp(-t * 1.6f);
                return (Mathf.Sin(2f * Mathf.PI * 4200f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 4230f * t) * 0.4f) * 0.16f * env;
            });
        }
    }

    /// <summary>Procedural 64x64 ability icons (white on transparent; tint with GUI.color).</summary>
    public static class Icons
    {
        const int N = 64;
        static readonly Dictionary<Ability, Texture2D> Cache = new Dictionary<Ability, Texture2D>();

        public static Texture2D For(Ability a)
        {
            if (Cache.TryGetValue(a, out Texture2D t) && t != null) return t;
            t = Draw(a);
            Cache[a] = t;
            return t;
        }

        static float Smooth(float edge, float d)
        {
            // Anti-aliased inside test: 1 inside, 0 outside, 1.5 px soft edge.
            return Mathf.Clamp01((edge - d) / 1.5f + 0.5f);
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return (a + ab * t - p).magnitude;
        }

        static Texture2D Draw(Ability a)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "Icon " + a, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[N * N];
            var c = new Vector2(N / 2f, N / 2f);
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float v = 0f;
                    switch (a)
                    {
                        case Ability.Smoke:
                            // Three overlapping puffs on a flat base.
                            v = Mathf.Max(Smooth(13f, (p - new Vector2(22f, 26f)).magnitude), Smooth(16f, (p - new Vector2(36f, 32f)).magnitude));
                            v = Mathf.Max(v, Smooth(11f, (p - new Vector2(46f, 24f)).magnitude));
                            if (p.y < 14f) v = 0f;
                            break;
                        case Ability.Flash:
                        {
                            // An eight-point starburst around a bright core.
                            Vector2 d = p - c;
                            float ang = Mathf.Atan2(d.y, d.x);
                            float spikes = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 4f)), 6f);
                            float r = 9f + 19f * spikes;
                            v = Mathf.Max(Smooth(r, d.magnitude), Smooth(10f, d.magnitude));
                            break;
                        }
                        case Ability.Dash:
                        {
                            // Two chevrons pointing right.
                            float d1 = Mathf.Min(SegDist(p, new Vector2(14f, 14f), new Vector2(32f, 32f)), SegDist(p, new Vector2(14f, 50f), new Vector2(32f, 32f)));
                            float d2 = Mathf.Min(SegDist(p, new Vector2(32f, 14f), new Vector2(50f, 32f)), SegDist(p, new Vector2(32f, 50f), new Vector2(50f, 32f)));
                            v = Mathf.Max(Smooth(4f, d1) * 0.6f, Smooth(4f, d2));
                            break;
                        }
                        default:
                        {
                            // Recon: concentric rings and a centre dot, like a sonar sweep.
                            float d = (p - c).magnitude;
                            v = Mathf.Max(Smooth(5f, d), Smooth(2f, Mathf.Abs(d - 14f)));
                            v = Mathf.Max(v, Smooth(2f, Mathf.Abs(d - 25f)) * 0.6f);
                            break;
                        }
                    }
                    px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(v));
                }
            }
            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }
    }

    /// <summary>2D floor-plan helpers. Sim (x, y) is Unity (x, 0, y).</summary>
    public static class Geo
    {
        public static Vector3 ToWorld(Vec2 p, float height = 0f)
        {
            return p == null ? new Vector3(0f, height, 0f) : new Vector3(p.x, height, p.y);
        }

        public static Vector2 ToV2(Vec2 p)
        {
            return p == null ? Vector2.zero : new Vector2(p.x, p.y);
        }

        /// <summary>Unity heading in degrees for a sim yaw (radians, 0 = +x, counter-clockwise).</summary>
        public static float YawToHeading(float yaw)
        {
            return 90f - yaw * Mathf.Rad2Deg;
        }

        /// <summary>Sim yaw for a Unity direction (flattened).</summary>
        public static float DirToYaw(Vector3 f)
        {
            return Mathf.Atan2(f.z, f.x);
        }

        public static bool PointInPolygon(Vector2 p, List<Vec2> poly)
        {
            if (poly == null || poly.Count < 3) return false;
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vec2 a = poly[i];
                Vec2 b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        public static Vector2 Centroid(List<Vec2> poly)
        {
            Vector2 c = Vector2.zero;
            if (poly == null || poly.Count == 0) return c;
            foreach (Vec2 p in poly) c += new Vector2(p.x, p.y);
            return c / poly.Count;
        }

        /// <summary>Does the segment a-b pass through a sphere (centre c, radius r)?</summary>
        public static bool SegmentHitsSphere(Vector3 a, Vector3 b, Vector3 c, float r)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(c - a, ab) / len2);
            return (a + ab * t - c).sqrMagnitude <= r * r;
        }
    }
}

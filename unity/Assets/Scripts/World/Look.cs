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
        public static readonly Color WallTop = new Color(0.64f, 0.62f, 0.59f); // was near-white: clipped to white in sun
        public static readonly Color Outer = new Color(0.78f, 0.80f, 0.84f);
        public static readonly Color Pillar = new Color(0.84f, 0.85f, 0.92f);
        public static readonly Color Crate = new Color(0.66f, 0.54f, 0.41f);
        public static readonly Color CrateTop = new Color(0.6f, 0.5f, 0.38f); // was near-white: clipped to white in sun
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

        static Shader worldShader;
        static bool worldShaderLooked;

        /// <summary>Saltworks/WorldLit if it is in the build, else null (callers fall back to Standard).</summary>
        public static Shader WorldShader
        {
            get
            {
                if (!worldShaderLooked)
                {
                    worldShaderLooked = true;
                    worldShader = Shader.Find("Saltworks/WorldLit");
                    if (worldShader == null) Debug.LogWarning("Saltworks: Saltworks/WorldLit not found, using Standard.");
                }
                return worldShader;
            }
        }

        /// <summary>
        /// A world surface (Saltworks/WorldLit, Standard as a fallback): albedo and normal textures, smoothness for paint
        /// or polymer (`gloss`) and for metal (`metalGloss`, `metallic`), and how much the vertex colours count.
        /// </summary>
        public static Material World(Color c, Texture albedo, Texture normal, float bump, float gloss, float metallic, float metalGloss, float vertexWeight)
        {
            Shader s = WorldShader;
            Material m;
            if (s != null)
            {
                m = new Material(s) { color = c };
                if (albedo != null) m.SetTexture("_MainTex", albedo);
                if (normal != null) m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", bump);
                m.SetFloat("_PolyGloss", gloss);
                m.SetFloat("_Metallic", metallic);
                m.SetFloat("_Glossiness", metalGloss);
                m.SetFloat("_VertexAO", vertexWeight);
                m.SetColor("_EmissionColor", Color.black);
            }
            else
            {
                m = Make(Find("Standard", "Unlit/Color"), c);
                if (albedo != null && m.HasProperty("_MainTex")) m.mainTexture = albedo;
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", Mathf.Lerp(gloss, metalGloss, metallic > 0.01f && vertexWeight < 0.5f ? 1f : 0f));
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", vertexWeight < 0.5f ? metallic : 0f);
            }
            return m;
        }

        /// <summary>A glowing surface for light strips and accents: WorldLit with emission (Standard emission as a fallback).</summary>
        public static Material Glow(Color c, float intensity)
        {
            Shader s = WorldShader;
            if (s == null) return Emissive(c, intensity);
            var m = new Material(s) { color = Color.Lerp(c, Color.white, 0.3f) };
            m.SetFloat("_VertexAO", 0f);
            m.SetFloat("_PolyGloss", 0.6f);
            m.SetFloat("_Glossiness", 0.6f);
            m.SetFloat("_Metallic", 0f);
            m.SetColor("_EmissionColor", c * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        /// <summary>Additive glow (Saltworks/Additive; Sprites/Default as a fallback).</summary>
        public static Material Additive(Texture tex, Color c, float intensity, int queue = 3100)
        {
            Shader s = Find("Saltworks/Additive", "Sprites/Default");
            Material m = Make(s, c);
            if (tex != null && m.HasProperty("_MainTex")) m.mainTexture = tex;
            if (m.HasProperty("_Intensity")) m.SetFloat("_Intensity", intensity);
            m.renderQueue = queue;
            return m;
        }

        /// <summary>Alpha-blended decal (Saltworks/Decal; Sprites/Default as a fallback), depth-offset onto the surface.</summary>
        public static Material Decal(Texture tex, Color c, int queue = 2460)
        {
            Shader s = Find("Saltworks/Decal", "Sprites/Default");
            Material m = Make(s, c);
            if (tex != null && m.HasProperty("_MainTex")) m.mainTexture = tex;
            m.renderQueue = queue;
            return m;
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

    /// <summary>What a footstep sounds like underfoot (MapBuilder.StepSurfaceAt).</summary>
    public enum StepSurface
    {
        Concrete,
        Metal,
        Wood,
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

        /// <summary>
        /// A gunshot per weapon class: a noise crack (decay `crack`), a body tone at `pitch` Hz (decay `thump`) and a
        /// low-passed tail of `seconds`.
        /// </summary>
        public static AudioClip Gun(string name, int seed, float pitch, float crack, float thump, float seconds, float tail)
        {
            var rng = new System.Random(300 + seed);
            float lp = 0f;
            return Make(name, seconds, t =>
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.12f;
                float body = Mathf.Sin(2f * Mathf.PI * pitch * t * (1f - t * 1.5f)) * Mathf.Exp(-t * thump);
                float attack = t < 0.002f ? t / 0.002f : 1f;
                return (noise * Mathf.Exp(-t * crack) * 0.6f + body * 0.55f + lp * tail * Mathf.Exp(-t * 7f)) * attack;
            });
        }

        public static AudioClip GunFor(WeaponClass c)
        {
            switch (c)
            {
                case WeaponClass.Sidearm: return Gun("shot_pin", 1, 180f, 48f, 40f, 0.14f, 0.6f);
                case WeaponClass.HeavyPistol: return Gun("shot_hammer", 2, 90f, 30f, 18f, 0.32f, 1.4f);
                case WeaponClass.Smg: return Gun("shot_wasp", 3, 210f, 60f, 50f, 0.1f, 0.5f);
                case WeaponClass.Shotgun: return Gun("shot_brick", 4, 70f, 22f, 14f, 0.42f, 2.0f);
                case WeaponClass.Sniper: return Gun("shot_longshot", 5, 60f, 18f, 10f, 0.6f, 2.4f);
                default: return Gun("shot_rifle", 6, 120f, 38f, 30f, 0.2f, 1.0f);
            }
        }

        /// <summary>A metallic click (magazine out/in, slot switch). `pitch` shifts it.</summary>
        public static AudioClip Click(int seed, float pitch)
        {
            var rng = new System.Random(400 + seed);
            return Make("click" + seed, 0.06f, t =>
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                return (noise * 0.5f + Mathf.Sin(2f * Mathf.PI * pitch * t) * 0.5f) * Mathf.Exp(-t * 90f) * 0.6f;
            });
        }

        /// <summary>A brass shell landing.</summary>
        public static AudioClip Tink()
        {
            return Make("tink", 0.12f, t => (Mathf.Sin(2f * Mathf.PI * 5200f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 7300f * t) * 0.3f) * Mathf.Exp(-t * 40f) * 0.25f);
        }

        public static AudioClip Tick()
        {
            return Make("hit", 0.05f, t => Mathf.Sin(2f * Mathf.PI * 2400f * t) * 0.3f * Mathf.Exp(-t * 80f));
        }

        /// <summary>A boot on concrete (the older single-surface step; Step and Scuff are the current ones).</summary>
        public static AudioClip Footstep(int seed)
        {
            return Step(StepSurface.Concrete, seed);
        }

        static AudioClip FromData(string name, float[] data, float peak)
        {
            float max = 1e-5f;
            for (int i = 0; i < data.Length; i++) max = Mathf.Max(max, Mathf.Abs(data[i]));
            float g = peak / max;
            // Fade the last 8 ms so no clip ends on a click.
            int fade = Mathf.Min(data.Length, Rate / 125);
            for (int i = 0; i < data.Length; i++)
            {
                float f = i >= data.Length - fade ? (data.Length - i) / (float)fade : 1f;
                data[i] = Mathf.Clamp(data[i] * g * f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// One soft footfall on `surface`: a heel strike and a lighter toe roll 40-70 ms later, each a twice low-passed
        /// noise burst over a low body thump; metal adds a short damped ring, wood a hollow knock. Every seed varies the
        /// timing, tone and filter, so a set of five never sounds like one sample on repeat.
        /// </summary>
        public static AudioClip Step(StepSurface surface, int seed)
        {
            var rng = new System.Random(900 + seed * 37 + (int)surface * 1009);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float seconds = surface == StepSurface.Metal ? 0.26f : 0.2f;
            int n = (int)(seconds * Rate);
            var data = new float[n];
            float toeAt = R(0.04f, 0.07f), toeGain = R(0.35f, 0.6f);
            float cut = surface == StepSurface.Wood ? R(0.06f, 0.09f) : surface == StepSurface.Metal ? R(0.1f, 0.14f) : R(0.08f, 0.12f);
            float thumpHz = surface == StepSurface.Metal ? R(85f, 110f) : surface == StepSurface.Wood ? R(70f, 90f) : R(55f, 72f);
            float knockHz = R(150f, 200f);
            float r1 = R(330f, 420f), r2 = R(560f, 690f), r3 = R(980f, 1150f);
            float lp1 = 0f, lp2 = 0f, grit = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp1 += (noise - lp1) * cut;
                lp2 += (lp1 - lp2) * cut;
                grit += (noise - grit) * 0.3f;
                float heel = Env(t, 0f, 0.003f, 55f);
                float toe = Env(t, toeAt, 0.006f, 70f) * toeGain;
                float hit = heel + toe;
                float v = lp2 * 3.2f * hit + Mathf.Sin(2f * Mathf.PI * thumpHz * t) * Env(t, 0f, 0.002f, 38f) * 0.55f;
                switch (surface)
                {
                    case StepSurface.Metal:
                        float ring = Env(t, 0f, 0.002f, 24f) + Env(t, toeAt, 0.002f, 30f) * toeGain * 0.6f;
                        v += (Mathf.Sin(2f * Mathf.PI * r1 * t) * 0.1f + Mathf.Sin(2f * Mathf.PI * r2 * t) * 0.06f + Mathf.Sin(2f * Mathf.PI * r3 * t) * 0.03f) * ring;
                        break;
                    case StepSurface.Wood:
                        v += (Mathf.Sin(2f * Mathf.PI * knockHz * t) * 0.32f + Mathf.Sin(2f * Mathf.PI * knockHz * 2.1f * t) * 0.08f) * (Env(t, 0f, 0.002f, 34f) + Env(t, toeAt, 0.003f, 40f) * toeGain * 0.7f);
                        break;
                    default:
                        v += grit * 0.06f * hit;
                        break;
                }
                data[i] = v;
            }
            return FromData("step_" + surface + "_" + seed, data, 0.55f);
        }

        /// <summary>A very quiet shoe scuff (the player's own careful steps): slow attack, low-passed, no thump.</summary>
        public static AudioClip Scuff(int seed)
        {
            var rng = new System.Random(1700 + seed * 13);
            int n = (int)(0.22f * Rate);
            var data = new float[n];
            float lp1 = 0f, lp2 = 0f;
            float k = 0.05f + seed * 0.01f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp1 += (noise - lp1) * k;
                lp2 += (lp1 - lp2) * k;
                float env = Mathf.Clamp01(t / 0.03f) * Mathf.Exp(-t * 16f);
                data[i] = lp2 * env;
            }
            return FromData("scuff" + seed, data, 0.35f);
        }

        /// <summary>A transient at `at`: linear attack over `attack` seconds, then exponential decay at `decay` per second.</summary>
        static float Env(float t, float at, float attack, float decay)
        {
            float x = t - at;
            if (x < 0f) return 0f;
            return x < attack ? x / attack : Mathf.Exp(-(x - attack) * decay);
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

        /// <summary>The soft high whine heard while blinded: one 3.2 kHz sine fading out over two seconds.</summary>
        public static AudioClip BlindRing()
        {
            return Make("blindring", 2f, t =>
            {
                float env = Mathf.Clamp01(t * 12f) * Mathf.Exp(-t * 1.9f) * Mathf.Clamp01((2f - t) * 6f);
                return Mathf.Sin(2f * Mathf.PI * 3200f * t) * 0.06f * env;
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

// Round 5 atmosphere, built per map and cheap enough for 120 fps: a drifting
// cloud dome over the procedural sky, additive light shafts at the site
// entrances with dust motes in them, glossy puddles that catch the reflection
// probe, painted hanging site signs, heat haze over the spawn barriers (High and
// Ultra only, one GrabPass), plus the pooled light flashes for muzzles and
// impacts (FxLights) and the chromatic damage edge (DamageFx). Static parts are
// merged into one mesh per material; nothing allocates per frame.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    /// <summary>Procedural textures for the world effects.</summary>
    public static class FxTex
    {
        static Texture2D shaft, clouds, chevrons, puff;

        static Texture2D New(string name, int w, int h, TextureWrapMode wrap, bool mips)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, mips) { name = name, hideFlags = HideFlags.DontSave, wrapMode = wrap, filterMode = FilterMode.Bilinear };
        }

        /// <summary>A light shaft: soft across (u), bright near the floor (v = 0) and fading up.</summary>
        public static Texture2D Shaft()
        {
            if (shaft != null) return shaft;
            const int w = 32, h = 64;
            shaft = New("FxShaft", w, h, TextureWrapMode.Clamp, false);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                float across = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 2f);
                float up = Mathf.Clamp01(v / 0.08f) * Mathf.Pow(1f - v, 1.4f);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(across * up * 255f));
            }
            shaft.SetPixels32(px);
            shaft.Apply(false);
            return shaft;
        }

        static float Lattice(int x, int y, int period, int seed)
        {
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            unchecked
            {
                int n = x * 374761393 + y * 668265263 + seed * 2147483;
                n = (n ^ (n >> 13)) * 1274126177;
                return ((n ^ (n >> 16)) & 0xffff) / 65535f;
            }
        }

        /// <summary>Tileable value noise at `period` cells across the texture.</summary>
        static float Noise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Lattice(x0, y0, period, seed), b = Lattice(x0 + 1, y0, period, seed);
            float c = Lattice(x0, y0 + 1, period, seed), d = Lattice(x0 + 1, y0 + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Tileable cloud noise (R = density), five octaves.</summary>
        public static Texture2D Clouds()
        {
            if (clouds != null) return clouds;
            const int n = 128;
            clouds = New("FxClouds", n, n, TextureWrapMode.Repeat, true);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float f = 0f, amp = 0.5f, total = 0f;
                int period = 4;
                for (int o = 0; o < 5; o++)
                {
                    f += Noise(u, v, period, 31 + o) * amp;
                    total += amp;
                    amp *= 0.5f;
                    period *= 2;
                }
                byte b = (byte)(Mathf.Clamp01(f / total) * 255f);
                px[y * n + x] = new Color32(b, b, b, 255);
            }
            clouds.SetPixels32(px);
            clouds.Apply(true);
            return clouds;
        }

        /// <summary>A soft, lumpy smoke puff: a round falloff broken up by noise (white, alpha).</summary>
        public static Texture2D Puff()
        {
            if (puff != null) return puff;
            const int n = 64;
            puff = New("FxPuff", n, n, TextureWrapMode.Clamp, true);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float r = new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f;
                float noise = Noise(u, v, 4, 7) * 0.6f + Noise(u, v, 8, 9) * 0.4f;
                float a = Mathf.Clamp01(1f - r * (0.75f + 0.5f * noise));
                a = a * a * (3f - 2f * a);
                byte shade = (byte)(220 + 35 * noise);
                px[y * n + x] = new Color32(shade, shade, shade, (byte)(a * 255f));
            }
            puff.SetPixels32(px);
            puff.Apply(true);
            return puff;
        }

        /// <summary>A strip of chevrons (repeats along u) for the neon signs.</summary>
        public static Texture2D Chevrons()
        {
            if (chevrons != null) return chevrons;
            const int w = 64, h = 32;
            chevrons = New("FxChevrons", w, h, TextureWrapMode.Repeat, false);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                // One chevron per repeat: a "<" whose arms meet at u = 0.3.
                float arm = Mathf.Abs(v - 0.5f) * 0.6f + 0.3f;
                float d = Mathf.Abs(u - arm);
                float a = Mathf.Clamp01(1f - d / 0.09f) * (v > 0.12f && v < 0.88f ? 1f : 0f);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            chevrons.SetPixels32(px);
            chevrons.Apply(false);
            return chevrons;
        }
    }

    public class WorldFx : MonoBehaviour
    {
        GameObject root;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Material> mats = new List<Material>();
        Material shaftMat, cloudMat, chevronMat, ringMat;
        Transform dome;
        GameObject haze;
        ParticleSystem dust;
        readonly List<Vector3> shaftFeet = new List<Vector3>();
        readonly List<Sign> signs = new List<Sign>();
        float dustAcc;
        Camera cam;

        class Sign
        {
            public Transform T;
            public Material Board;
            public Color Color;
            public float Seed;
        }

        static Shader Find(string name)
        {
            Shader s = Shader.Find(name);
            if (s == null || !s.isSupported)
            {
                Debug.LogWarning("Saltworks: " + name + " is missing or unsupported; that effect is off.");
                return null;
            }
            return s;
        }

        Material Own(Material m)
        {
            if (m != null) mats.Add(m);
            return m;
        }

        Mesh Own(Mesh m)
        {
            meshes.Add(m);
            return m;
        }

        public void Clear()
        {
            if (root != null) Destroy(root);
            root = null;
            foreach (Mesh m in meshes) if (m != null) Destroy(m);
            meshes.Clear();
            foreach (Material m in mats) if (m != null) Destroy(m);
            mats.Clear();
            shaftFeet.Clear();
            signs.Clear();
            dome = null;
            haze = null;
            dust = null;
            shaftMat = cloudMat = chevronMat = ringMat = null;
        }

        void OnDestroy()
        {
            Clear();
        }

        GameObject Child(string name, Mesh mesh, Material mat, bool shadows = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        /// <summary>Builds every effect for `map` (call after the map itself is built).</summary>
        public void Build(MapDef map, Font font, Material textMat)
        {
            Clear();
            if (map == null) return;
            root = new GameObject("World FX");
            try
            {
                BuildSky();
                BuildShafts(map);
                BuildPuddles(map);
                BuildSigns(map, font, textMat);
                // Heat haze removed: its screen distortion made the view ripple and hurt aiming.
                BuildDust();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("Saltworks: could not build the world effects: " + ex);
            }
        }

        // ---- sky -------------------------------------------------------------------------------------
        void BuildSky()
        {
            Shader s = Find("Saltworks/SkyClouds");
            if (s == null) return;
            cloudMat = Own(new Material(s) { hideFlags = HideFlags.DontSave });
            cloudMat.SetTexture("_MainTex", FxTex.Clouds());
            var k = new MeshKit(1);
            const int rings = 10, segs = 32;
            for (int r = 0; r < rings; r++)
            {
                float e0 = Mathf.Lerp(-0.05f, 1.45f, r / (float)rings), e1 = Mathf.Lerp(-0.05f, 1.45f, (r + 1) / (float)rings);
                for (int i = 0; i < segs; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                    Vector3 P(float a, float e) => new Vector3(Mathf.Cos(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(a) * Mathf.Cos(e));
                    k.Quad(0, P(a0, e0), P(a1, e0), P(a1, e1), P(a0, e1), -P((a0 + a1) / 2f, (e0 + e1) / 2f), Color.white);
                }
            }
            Mesh m = Own(k.ToMesh("Cloud dome"));
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            dome = Child("Clouds", m, cloudMat).transform;
            dome.localScale = Vector3.one * 420f;
        }

        // ---- light shafts and dust ----------------------------------------------------------------------
        /// <summary>Points on each site's edge that open onto free floor: where players walk in.</summary>
        static List<Vector3> Entrances(MapDef map)
        {
            var list = new List<Vector3>();
            foreach (KeyValuePair<string, List<Vec2>> site in map.SiteList())
            {
                List<Vec2> poly = site.Value;
                Vector2 cen = Geo.Centroid(poly);
                var found = new List<Vector2>();
                for (int i = 0; i < poly.Count; i++)
                {
                    Vec2 a = poly[i], b = poly[(i + 1) % poly.Count];
                    var pa = new Vector2(a.x, a.y);
                    var pb = new Vector2(b.x, b.y);
                    float len = (pb - pa).magnitude;
                    int steps = Mathf.Clamp(Mathf.FloorToInt(len / 5f), 1, 6);
                    for (int s = 0; s < steps; s++)
                    {
                        Vector2 p = Vector2.Lerp(pa, pb, (s + 0.5f) / steps);
                        Vector2 outward = (p - cen).normalized;
                        Vector2 q = p + outward * 2f;
                        if (!MapBuilder.IsClear(p, map, 1.2f) || !MapBuilder.IsClear(q, map, 1.2f)) continue;
                        bool near = false;
                        foreach (Vector2 f in found) if ((f - p).sqrMagnitude < 64f) near = true;
                        if (near) continue;
                        found.Add(p);
                        if (found.Count >= 3) break;
                    }
                    if (found.Count >= 3) break;
                }
                foreach (Vector2 f in found) list.Add(new Vector3(f.x, 0f, f.y));
            }
            return list;
        }

        void BuildShafts(MapDef map)
        {
            Shader s = Find("Saltworks/FxAdditive");
            if (s == null) return;
            shaftMat = Own(new Material(s) { hideFlags = HideFlags.DontSave });
            shaftMat.SetTexture("_MainTex", FxTex.Shaft());
            shaftMat.SetColor("_Color", new Color(1f, 0.93f, 0.78f, 1f));
            shaftMat.SetFloat("_Intensity", 0.55f);
            List<Vector3> feet = Entrances(map);
            if (feet.Count == 0) return;
            // Slanted along the sun, a little steeper so they stay inside the lanes.
            Vector3 sunDir = RenderSettings.sun != null ? RenderSettings.sun.transform.forward : new Vector3(0.3f, -1f, 0.2f);
            Vector3 slant = new Vector3(-sunDir.x, 0f, -sunDir.z);
            slant = slant.sqrMagnitude > 1e-4f ? slant.normalized * 0.35f : Vector3.zero;
            var k = new MeshKit(1);
            const float height = 9f;
            for (int i = 0; i < feet.Count; i++)
            {
                Vector3 foot = feet[i];
                shaftFeet.Add(foot);
                Vector3 top = foot + (Vector3.up + slant) * height;
                float alpha = 0.55f + 0.35f * Mathf.Repeat(i * 0.618f, 1f);
                var col = new Color(1f, 1f, 1f, alpha);
                for (int q = 0; q < 3; q++)
                {
                    float ang = q * 60f + i * 17f;
                    Vector3 side = Quaternion.Euler(0f, ang, 0f) * Vector3.right;
                    Vector3 b0 = foot - side * 1.3f, b1 = foot + side * 1.3f;
                    Vector3 t0 = top - side * 0.7f, t1 = top + side * 0.7f;
                    k.QuadUv(0, b0, b1, t1, t0, Vector3.Cross(side, Vector3.up), col, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
                }
            }
            Child("Light shafts", Own(k.ToMesh("Light shafts")), shaftMat);
        }

        void BuildDust()
        {
            if (shaftFeet.Count == 0) return;
            Shader s = Find("Saltworks/FxAdditive");
            if (s == null) return;
            var mat = Own(new Material(s) { hideFlags = HideFlags.DontSave });
            mat.SetTexture("_MainTex", Textures.SoftDot());
            mat.SetFloat("_Intensity", 1.2f);
            var go = new GameObject("Dust motes");
            go.transform.SetParent(root.transform, false);
            dust = go.AddComponent<ParticleSystem>();
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dust.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = 260;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = 6f;
            var em = dust.emission;
            em.enabled = false;
            var sh = dust.shape;
            sh.enabled = false;
            var col = dust.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var noise = dust.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.4f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            dust.Play();
        }

        // ---- puddles -----------------------------------------------------------------------------------
        void BuildPuddles(MapDef map)
        {
            float w = map.bounds != null && map.bounds.width > 0 ? map.bounds.width : 100f;
            float h = map.bounds != null && map.bounds.height > 0 ? map.bounds.height : 100f;
            var rng = new System.Random(MapBuilder.StableHash(map.id ?? map.name ?? "map"));
            var k = new MeshKit(1);
            var placed = new List<Vector2>();
            int want = 16;
            for (int tries = 0; tries < 400 && placed.Count < want; tries++)
            {
                var p = new Vector2((float)rng.NextDouble() * w, (float)rng.NextDouble() * h);
                float r = 0.9f + (float)rng.NextDouble() * 1.6f;
                if (!MapBuilder.IsClear(p, map, r + 0.6f)) continue;
                bool onSite = false;
                foreach (KeyValuePair<string, List<Vec2>> s in map.SiteList()) if (Geo.PointInPolygon(p, s.Value)) onSite = true;
                if (onSite) continue;
                bool near = false;
                foreach (Vector2 q in placed) if ((q - p).sqrMagnitude < 100f) near = true;
                if (near) continue;
                placed.Add(p);
                // An irregular blob: radius wobbles with two sine terms, stretched along a random axis.
                const int seg = 18;
                float ph1 = (float)rng.NextDouble() * 6.28f, ph2 = (float)rng.NextDouble() * 6.28f;
                float stretch = 1f + (float)rng.NextDouble() * 0.8f;
                float rot = (float)rng.NextDouble() * 6.28f;
                var centre = new Vector3(p.x, 0.007f, p.y);
                Vector3 Pt(int i)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    float rr = r * (0.78f + 0.14f * Mathf.Sin(a * 3f + ph1) + 0.08f * Mathf.Sin(a * 5f + ph2));
                    float x = Mathf.Cos(a) * rr * stretch, z = Mathf.Sin(a) * rr;
                    float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
                    return centre + new Vector3(x * cs - z * sn, 0f, x * sn + z * cs);
                }
                for (int i = 0; i < seg; i++) k.Tri(0, centre, Pt(i), Pt(i + 1), Vector3.up, Color.white);
            }
            if (k.Empty) return;
            Material wet = Own(Mats.World(new Color(0.13f, 0.14f, 0.17f), null, null, 0f, 0.97f, 0f, 0.97f, 0f));
            GameObject go = Child("Puddles", Own(k.ToMesh("Puddles")), wet);
            var mr = go.GetComponent<MeshRenderer>();
            mr.receiveShadows = true;
            mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        }

        // ---- signs -------------------------------------------------------------------------------------
        void BuildSigns(MapDef map, Font font, Material textMat)
        {
            // Painted signs: no additive chevron strip (round 6: restrained lighting, no neon).
            chevronMat = null;
            int n = 0;
            foreach (KeyValuePair<string, List<Vec2>> site in map.SiteList())
            {
                Color c = Palette.Site(site.Key);
                Vector2 at = MapBuilder.FindFreeSpot(site.Value, map, 2.5f);
                var sign = new GameObject("Sign " + site.Key).transform;
                sign.SetParent(root.transform, false);
                sign.position = new Vector3(at.x, 4.6f, at.y);

                var k = new MeshKit(1);
                k.Box(0, Matrix4x4.identity, new Vector3(2.8f, 0.95f, 0.08f), 0.02f, Color.white);
                Material board = Own(Mats.World(Color.Lerp(c, new Color(0.93f, 0.9f, 0.84f), 0.35f), Textures.Grain(), null, 0f, 0.3f, 0f, 0.3f, 0f));
                GameObject b = Child("Board", Own(k.ToMesh("Sign board")), board);
                b.transform.SetParent(sign, false);
                var fk = new MeshKit(1);
                Color frame = new Color(0.08f, 0.09f, 0.11f, 1f);
                fk.Box(0, MeshKit.At(new Vector3(0f, 0.5f, 0f), Vector3.zero), new Vector3(2.96f, 0.08f, 0.14f), 0.02f, frame);
                fk.Box(0, MeshKit.At(new Vector3(0f, -0.5f, 0f), Vector3.zero), new Vector3(2.96f, 0.08f, 0.14f), 0.02f, frame);
                fk.Box(0, MeshKit.At(new Vector3(0f, 1.6f, 0f), Vector3.zero), new Vector3(0.05f, 2.1f, 0.05f), 0f, frame);
                GameObject f = Child("Frame", Own(fk.ToMesh("Sign frame")), BotRig.BodyMaterial);
                f.transform.SetParent(sign, false);

                if (font != null && textMat != null)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        GameObject label = MapBuilder.Label("SITE " + site.Key, Vector3.zero, 0.5f, new Color(0.03f, 0.03f, 0.05f, 1f), font, textMat, sign, false);
                        label.transform.localPosition = new Vector3(0f, 0.04f, side == 0 ? -0.05f : 0.05f);
                        label.transform.localRotation = Quaternion.Euler(0f, side == 0 ? 0f : 180f, 0f);
                    }
                }
                if (chevronMat != null)
                {
                    var ck = new MeshKit(1);
                    var white = new Color(c.r, c.g, c.b, 1f);
                    for (int side = 0; side < 2; side++)
                    {
                        float z = side == 0 ? -0.07f : 0.07f;
                        ck.QuadUv(0, new Vector3(-1.4f, -0.62f, z), new Vector3(1.4f, -0.62f, z), new Vector3(1.4f, -0.82f, z), new Vector3(-1.4f, -0.82f, z), Vector3.back, white,
                                  new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 0f));
                    }
                    GameObject ch = Child("Chevrons", Own(ck.ToMesh("Sign chevrons")), chevronMat);
                    ch.transform.SetParent(sign, false);
                }
                signs.Add(new Sign { T = sign, Board = board, Color = c, Seed = n * 1.7f });
                n++;
            }
        }

        // ---- spawn barriers ------------------------------------------------------------------------------
        void BuildHaze(MapDef map)
        {
            if (map.spawns == null) return;
            Shader hs = Find("Saltworks/HeatHaze");
            Shader add = Find("Saltworks/FxAdditive");
            if (add != null)
            {
                ringMat = Own(new Material(add) { hideFlags = HideFlags.DontSave });
                ringMat.SetTexture("_MainTex", Mats.GradientTexture());
                ringMat.SetFloat("_Intensity", 1.3f);
            }
            Material hazeMat = hs != null ? Own(new Material(hs) { hideFlags = HideFlags.DontSave }) : null;
            var hk = new MeshKit(1);
            var rk = new MeshKit(1);
            void Barrier(List<Vec2> pts, Color team)
            {
                if (pts == null || pts.Count == 0) return;
                Vector2 c = Vector2.zero;
                foreach (Vec2 p in pts) c += new Vector2(p.x, p.y);
                c /= pts.Count;
                float r = 0f;
                foreach (Vec2 p in pts) r = Mathf.Max(r, (new Vector2(p.x, p.y) - c).magnitude);
                r = Mathf.Clamp(r + 2.5f, 3f, 11f);
                const int seg = 32;
                var ringCol = new Color(team.r, team.g, team.b, 0.9f);
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                    var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    var c3 = new Vector3(c.x, 0f, c.y);
                    Vector3 p0 = c3 + d0 * r, p1 = c3 + d1 * r;
                    hk.QuadUv(0, p0, p1, p1 + Vector3.up * 2.8f, p0 + Vector3.up * 2.8f, d0, Color.white,
                              new Vector2(i / (float)seg, 0f), new Vector2((i + 1) / (float)seg, 0f), new Vector2((i + 1) / (float)seg, 1f), new Vector2(i / (float)seg, 1f));
                    // A glowing floor line at the barrier, its gradient fading inward.
                    Vector3 q0 = c3 + d0 * (r - 0.5f), q1 = c3 + d1 * (r - 0.5f);
                    rk.QuadUv(0, p0 + Vector3.up * 0.02f, p1 + Vector3.up * 0.02f, q1 + Vector3.up * 0.02f, q0 + Vector3.up * 0.02f, Vector3.up, ringCol,
                              new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                }
            }
            Barrier(map.spawns.attack, Palette.Attack);
            Barrier(map.spawns.defense, Palette.Defense);
            if (hazeMat != null && !hk.Empty) haze = Child("Heat haze", Own(hk.ToMesh("Heat haze")), hazeMat);
            if (ringMat != null && !rk.Empty) Child("Barrier lines", Own(rk.ToMesh("Barrier lines")), ringMat);
        }

        // ---- per frame -----------------------------------------------------------------------------------
        void LateUpdate()
        {
            if (root == null) return;
            if (cam == null) cam = Camera.main;
            float t = Time.time;
            int preset = Gfx.Preset;
            if (dome != null && cam != null)
            {
                dome.position = cam.transform.position;
                if (cloudMat != null)
                {
                    WorldPaletteId p = Settings.World;
                    Color sky = RenderSettings.ambientSkyColor;
                    Color lit = Color.Lerp(sky, Color.white, p == WorldPaletteId.Night ? 0.1f : 0.65f);
                    lit.a = p == WorldPaletteId.Night ? 0.55f : 0.9f;
                    cloudMat.SetColor("_Color", lit);
                    cloudMat.SetColor("_Shade", RenderSettings.ambientEquatorColor * (p == WorldPaletteId.Night ? 0.5f : 0.85f));
                    cloudMat.SetFloat("_Cover", p == WorldPaletteId.HighContrast ? 0.25f : p == WorldPaletteId.Dusk ? 0.55f : 0.45f);
                }
            }
            if (shaftMat != null)
            {
                float night = Settings.World == WorldPaletteId.Night ? 0.45f : Settings.World == WorldPaletteId.HighContrast ? 0.3f : 1f;
                shaftMat.SetFloat("_Intensity", (0.5f + 0.06f * Mathf.Sin(t * 0.7f) + 0.03f * Mathf.Sin(t * 2.3f)) * night);
            }
            if (chevronMat != null) chevronMat.mainTextureOffset = new Vector2(-Mathf.Repeat(t * 0.6f, 1f), 0f);
            foreach (Sign s in signs)
            {
                if (s.T == null) continue;
                // A painted hanging board (no glow), swaying a little.
                s.T.rotation = Quaternion.Euler(Mathf.Sin(t * 0.9f + s.Seed) * 0.8f, s.Seed * 40f + Mathf.Sin(t * 0.45f + s.Seed) * 3f, Mathf.Sin(t * 0.7f + s.Seed * 2f) * 0.8f);
            }
            if (haze != null)
            {
                bool on = preset >= 2;
                if (haze.activeSelf != on) haze.SetActive(on);
            }
            if (dust != null && preset >= 1 && shaftFeet.Count > 0)
            {
                dustAcc += Time.deltaTime * 6f * shaftFeet.Count * (preset >= 3 ? 1.5f : 1f);
                int guard = 0;
                while (dustAcc >= 1f && guard++ < 20)
                {
                    dustAcc -= 1f;
                    Vector3 foot = shaftFeet[Random.Range(0, shaftFeet.Count)];
                    float y = Random.Range(0.3f, 5f);
                    Vector2 off = Random.insideUnitCircle * Mathf.Lerp(1.1f, 0.7f, y / 9f);
                    var ep = new ParticleSystem.EmitParams
                    {
                        position = foot + new Vector3(off.x, y, off.y),
                        velocity = new Vector3(Random.Range(-0.05f, 0.05f), Random.Range(-0.02f, 0.06f), Random.Range(-0.05f, 0.05f)),
                        startSize = Random.Range(0.015f, 0.04f),
                        startLifetime = Random.Range(4f, 7f),
                        startColor = new Color(1f, 0.94f, 0.8f, Random.Range(0.4f, 0.9f)),
                        applyShapeToPosition = false,
                    };
                    dust.Emit(ep, 1);
                }
            }
        }
    }

    /// <summary>Pooled point lights for muzzle flashes and impacts: brief, unshadowed, oldest reused first.</summary>
    public class FxLights : MonoBehaviour
    {
        const int Count = 8;
        static FxLights instance;
        readonly Light[] lights = new Light[Count];
        readonly float[] born = new float[Count];
        readonly float[] life = new float[Count];
        readonly float[] peak = new float[Count];
        int next;

        void Awake()
        {
            instance = this;
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject("Flash light " + i);
                go.transform.SetParent(transform, false);
                Light l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.Auto;
                l.enabled = false;
                lights[i] = l;
            }
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>A flash of light at `p` for `duration` seconds (off on the Low preset).</summary>
        public static void Flash(Vector3 p, Color c, float intensity, float range, float duration)
        {
            if (instance == null || Gfx.Preset == 0) return;
            instance.Fire(p, c, intensity, range, duration);
        }

        void Fire(Vector3 p, Color c, float intensity, float range, float duration)
        {
            int i = next;
            next = (next + 1) % Count;
            Light l = lights[i];
            l.transform.position = p;
            l.color = c;
            l.range = range;
            l.intensity = intensity;
            l.enabled = true;
            born[i] = Time.time;
            life[i] = Mathf.Max(0.01f, duration);
            peak[i] = intensity;
        }

        void Update()
        {
            float t = Time.time;
            for (int i = 0; i < Count; i++)
            {
                Light l = lights[i];
                if (!l.enabled) continue;
                float k = (t - born[i]) / life[i];
                if (k >= 1f)
                {
                    l.enabled = false;
                    continue;
                }
                float f = 1f - k;
                l.intensity = peak[i] * f * f;
            }
        }
    }

    /// <summary>The chromatic damage edge on the main camera; enabled only while there is damage to show.</summary>
    [RequireComponent(typeof(Camera))]
    public class DamageFx : MonoBehaviour
    {
        Material mat;
        float amount;

        void Awake()
        {
            Shader s = Shader.Find("Hidden/Saltworks/DamageChroma");
            if (s != null && s.isSupported) mat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            else Debug.LogWarning("Saltworks: the damage shader is missing or unsupported; the chromatic edge is off.");
            enabled = false;
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }

        /// <summary>0..1; at 0 (or on the Low preset) the component switches off, so there is no extra blit.</summary>
        public void Set(float a)
        {
            amount = Mathf.Clamp01(a);
            // Off for good: the colour fringe smeared the screen on every hit, which hurt aiming.
            bool on = false;
            if (enabled != on) enabled = on;
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (mat == null)
            {
                Graphics.Blit(src, dst);
                return;
            }
            mat.SetFloat("_Amount", amount);
            Graphics.Blit(src, dst, mat);
        }
    }
}

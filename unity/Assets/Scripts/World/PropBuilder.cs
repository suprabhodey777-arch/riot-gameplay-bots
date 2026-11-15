// Decoration generated from the MapDef alone: pipes, vents and air-conditioning
// units on tall walls (above head height or flush, so sight lines do not
// change), wall lamps with a warm lens and a faint halo, painted metal sign
// plates, metal corner caps and straps on crates, small prop groups tucked into
// inside corners (barrels, crate stacks, pallets with sacks), hazard markings
// across chokes, a plant-zone stencil on every site, one landmark per large
// exterior area standing on the wall head beside it (a water tower, a satellite
// dish, a tower crane or a billboard), and a fogged skyline of banded buildings
// with rooftop tanks beyond the outer walls. Nothing here has a collider: the
// sim's walls are the only geometry that matters for play.
// Everything is merged into one mesh per material, so the whole set costs about
// eight draw calls. Density (0.35 to 1.3) comes from the graphics preset.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    public static class PropBuilder
    {
        static Material metalMat, skyMat, hazardMat, plantMat, haloMat, woodMat, lampMat;
        static readonly Color LampColor = new Color(1f, 0.84f, 0.62f);

        struct Edge
        {
            public Vector2 A, B, N;
            public float Len, Height;
            public bool Crate;
        }

        static void EnsureMaterials()
        {
            if (metalMat == null) metalMat = Mats.Weathered(Mats.World(Color.white, Textures.Grain(), null, 0f, 0.35f, 0.8f, 0.6f, 1f), 0.5f);
            if (skyMat == null) skyMat = Mats.World(Color.white, Texture2D.whiteTexture, null, 0f, 0.1f, 0f, 0.1f, 1f);
            if (woodMat == null)
            {
                Textures.Pair cr = Textures.Crate();
                woodMat = Mats.Weathered(Mats.World(Color.white, cr.Albedo, cr.Normal, 0.8f, 0.18f, 0f, 0.18f, 1f), 0.7f);
            }
            if (lampMat == null) lampMat = Mats.Glow(LampColor, 1.4f);
            if (hazardMat == null) hazardMat = Mats.Decal(Textures.Hazard(), Color.white, 2455);
            if (plantMat == null) plantMat = Mats.Decal(Textures.PlantZone(), Color.white, 2456);
            if (haloMat == null) haloMat = Mats.Additive(Textures.GlowStrip(), Color.white, 1f, 3050);
        }

        public static GameObject Build(MapDef map, float density)
        {
            EnsureMaterials();
            var root = new GameObject("Props");
            if (map == null || map.walls == null) return root;
            density = Mathf.Clamp(density, 0f, 2f);
            var rng = new System.Random(MapBuilder.StableHash(map.id ?? map.name ?? "map"));
            float w = map.bounds != null && map.bounds.width > 0 ? map.bounds.width : 100f;
            float h = map.bounds != null && map.bounds.height > 0 ? map.bounds.height : 100f;

            var metal = new MeshKit(1) { UvScale = 2f };
            var wood = new MeshKit(1) { UvScale = 1.2f };
            var lamps = new MeshKit(1);
            var halo = new MeshKit(1);
            var hazard = new MeshKit(1);
            var plant = new MeshKit(1);
            var sky = new MeshKit(1) { UvScale = 0.1f };

            // Places where floor props must not go: spawns, choke mouths and the site letters.
            var keepOut = new List<Vector2>();
            if (map.spawns != null)
            {
                if (map.spawns.attack != null) foreach (Vec2 s in map.spawns.attack) if (s != null) keepOut.Add(new Vector2(s.x, s.y));
                if (map.spawns.defense != null) foreach (Vec2 s in map.spawns.defense) if (s != null) keepOut.Add(new Vector2(s.x, s.y));
            }
            foreach (Choke c in FindChokes(map)) keepOut.Add((c.A + c.B) * 0.5f);
            foreach (KeyValuePair<string, List<Vec2>> s in map.SiteList()) keepOut.Add(MapBuilder.FindFreeSpot(s.Value, map, 2.5f));

            List<Edge> edges = Edges(map, w, h);
            int lampsPlaced = 0;
            foreach (Edge e in edges)
            {
                if (e.Crate) { CrateDressing(metal, e); continue; }
                double roll = rng.NextDouble();
                bool piped = e.Height >= 2.9f && e.Len >= 4f && roll < 0.4 * density;
                if (piped) Pipe(metal, e, rng);
                if (e.Height >= 2.6f && e.Len >= 2.5f && rng.NextDouble() < 0.25 * density) Vent(metal, e, rng);
                if (!piped && e.Height >= 3.6f && e.Len >= 3f && rng.NextDouble() < 0.2 * density) AirCon(metal, e, rng);
                if (e.Height >= 2.8f && e.Len >= 3f && rng.NextDouble() < 0.32 * density)
                {
                    WallLamp(metal, lamps, halo, e, rng);
                    lampsPlaced++;
                }
                if (e.Height >= 3f && e.Len >= 2.5f && rng.NextDouble() < 0.14 * density) SignPlate(metal, e, rng);
            }
            // Crate corners: a dark metal cap on every corner of every low box.
            foreach (WallDef wall in map.walls)
            {
                if (wall.points == null || wall.points.Count < 3 || wall.height > 1.5f) continue;
                List<Vector2> p = MapBuilder.ToCounterClockwise(wall.points);
                for (int i = 0; i < p.Count; i++)
                {
                    Vector2 prev = p[(i - 1 + p.Count) % p.Count], cur = p[i], next = p[(i + 1) % p.Count];
                    Vector2 dir = (next - prev);
                    float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                    metal.Box(0, MeshKit.At(new Vector3(cur.x, wall.height * 0.5f, cur.y), new Vector3(0f, yaw, 0f)), new Vector3(0.1f, wall.height + 0.02f, 0.1f), 0.012f, new Color(0.2f, 0.21f, 0.23f, 1f));
                }
            }
            CornerProps(metal, wood, map, edges, keepOut, density, rng);
            Landmarks(metal, map);

            Chokes(hazard, map, density);
            PlantZones(plant, map);
            Skyline(sky, w, h, density, rng);

            Add(root, "Props metal", metal, metalMat, true);
            Add(root, "Props wood", wood, woodMat, true);
            Add(root, "Light halos", halo, haloMat, false);
            Add(root, "Hazard markings", hazard, hazardMat, false);
            Add(root, "Plant zones", plant, plantMat, false);
            Add(root, "Skyline", sky, skyMat, false);
            if (lampsPlaced > 0)
            {
                WorldLook.RegisterGlow(lampMat, LampColor, 1.4f);
                Add(root, "Lamp lenses", lamps, lampMat, false);
            }
            return root;
        }

        static void Add(GameObject root, string name, MeshKit k, Material m, bool shadows)
        {
            if (k.Empty) return;
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = k.ToMesh(name);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = shadows;
            mr.lightProbeUsage = LightProbeUsage.Off;
        }

        /// <summary>Every wall face that looks onto open floor inside the map.</summary>
        static List<Edge> Edges(MapDef map, float w, float h)
        {
            var list = new List<Edge>();
            foreach (WallDef wall in map.walls)
            {
                if (wall.points == null || wall.points.Count < 3) continue;
                List<Vector2> p = MapBuilder.ToCounterClockwise(wall.points);
                for (int i = 0; i < p.Count; i++)
                {
                    Vector2 a = p[i], b = p[(i + 1) % p.Count];
                    Vector2 e = b - a;
                    float len = e.magnitude;
                    if (len < 0.8f) continue;
                    var n = new Vector2(e.y, -e.x) / len;
                    Vector2 front = (a + b) * 0.5f + n * 0.6f;
                    if (front.x < 0.2f || front.y < 0.2f || front.x > w - 0.2f || front.y > h - 0.2f) continue;
                    if (!MapBuilder.IsClear(front, map, 0.2f)) continue;
                    list.Add(new Edge { A = a, B = b, N = n, Len = len, Height = Mathf.Max(0.05f, wall.height), Crate = wall.height <= 1.5f });
                }
            }
            return list;
        }

        static Vector3 W(Vector2 p, float y)
        {
            return new Vector3(p.x, y, p.y);
        }

        static float YawOf(Vector2 dir)
        {
            return Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
        }

        static readonly Color[] PipeColors =
        {
            new Color(0.58f, 0.28f, 0.2f, 0f),
            new Color(0.3f, 0.42f, 0.5f, 0f),
            new Color(0.78f, 0.66f, 0.28f, 0f),
            new Color(0.34f, 0.36f, 0.38f, 1f),
        };

        /// <summary>One or two horizontal pipes along the face above head height, on brackets, with a drop at one end.</summary>
        static void Pipe(MeshKit k, Edge e, System.Random rng)
        {
            Vector2 dir = (e.B - e.A) / e.Len;
            float y = Mathf.Clamp(e.Height - 0.4f, 2.5f, 3.4f);
            float r = 0.05f + (float)rng.NextDouble() * 0.035f;
            float off = 0.12f + r;
            Color c = PipeColors[rng.Next(PipeColors.Length)];
            Color bracket = new Color(0.22f, 0.23f, 0.25f, 1f);
            float a0 = 0.4f, a1 = e.Len - 0.4f;
            Vector2 p0 = e.A + dir * a0 + e.N * off, p1 = e.A + dir * a1 + e.N * off;
            k.Cylinder(0, MeshKit.Along(W((p0 + p1) * 0.5f, y), W(p1 - p0, 0f)), r, a1 - a0, 10, c);
            bool second = rng.NextDouble() < 0.35;
            if (second)
            {
                float r2 = r * 0.6f;
                Vector2 q0 = e.A + dir * a0 + e.N * (0.12f + r2), q1 = e.A + dir * a1 + e.N * (0.12f + r2);
                k.Cylinder(0, MeshKit.Along(W((q0 + q1) * 0.5f, y - 0.22f), W(q1 - q0, 0f)), r2, a1 - a0, 8, PipeColors[rng.Next(PipeColors.Length)]);
            }
            for (float t = a0 + 0.3f; t < a1; t += 1.6f)
            {
                Vector2 bp = e.A + dir * t + e.N * (off * 0.5f);
                k.Box(0, MeshKit.At(W(bp, y - (second ? 0.11f : 0f)), new Vector3(0f, YawOf(e.N), 0f)), new Vector3(0.05f, second ? 0.36f : 0.14f, off + 0.02f), 0.008f, bracket);
            }
            // Couplings.
            for (float t = a0 + 1f; t < a1 - 0.3f; t += 2.4f)
            {
                Vector2 cp = e.A + dir * t + e.N * off;
                k.Cylinder(0, MeshKit.Along(W(cp, y), W(dir, 0f)), r * 1.25f, 0.08f, 10, bracket);
            }
            // A drop down the wall at one end to about knee height, tight to the face.
            bool atEnd = rng.NextDouble() < 0.5;
            Vector2 dp = atEnd ? p1 : p0;
            float bottom = 0.5f;
            k.Cylinder(0, MeshKit.Along(W(dp, (y + bottom) * 0.5f), Vector3.up), r, y - bottom, 10, c);
            k.Box(0, MeshKit.At(W(dp, y), new Vector3(0f, YawOf(dir), 0f)), new Vector3(r * 2.4f, r * 2.4f, r * 2.4f), r * 0.5f, bracket);
            k.Box(0, MeshKit.At(W(dp - e.N * off * 0.5f, bottom + 0.3f), new Vector3(0f, YawOf(e.N), 0f)), new Vector3(0.05f, 0.12f, off + 0.02f), 0.008f, bracket);
        }

        /// <summary>A louvred vent panel flush on the face (4 cm proud).</summary>
        static void Vent(MeshKit k, Edge e, System.Random rng)
        {
            Vector2 dir = (e.B - e.A) / e.Len;
            float wv = 0.7f + (float)rng.NextDouble() * 0.4f, hv = 0.45f + (float)rng.NextDouble() * 0.2f;
            float along = Mathf.Lerp(wv * 0.5f + 0.3f, e.Len - wv * 0.5f - 0.3f, (float)rng.NextDouble());
            bool low = rng.NextDouble() < 0.4;
            float y = low ? 0.55f : Mathf.Min(e.Height - 0.5f, 2.3f);
            Vector2 c = e.A + dir * along + e.N * 0.02f;
            float yaw = YawOf(e.N);
            Color frame = new Color(0.4f, 0.42f, 0.45f, 1f);
            Color slat = new Color(0.16f, 0.17f, 0.18f, 1f);
            Matrix4x4 m = MeshKit.At(W(c, y), new Vector3(0f, yaw, 0f));
            k.Box(0, m, new Vector3(wv, hv, 0.04f), 0.01f, frame);
            int n = Mathf.Max(3, Mathf.RoundToInt(hv / 0.08f));
            for (int i = 0; i < n; i++)
            {
                float sy = -hv * 0.5f + 0.06f + i * (hv - 0.12f) / Mathf.Max(1, n - 1);
                k.Box(0, m * Matrix4x4.TRS(new Vector3(0f, sy, 0.012f), Quaternion.Euler(-35f, 0f, 0f), Vector3.one), new Vector3(wv - 0.08f, 0.012f, 0.05f), 0f, slat);
            }
        }

        /// <summary>Metal straps around a crate's face: a mid band and a top rim.</summary>
        static void CrateDressing(MeshKit k, Edge e)
        {
            Vector2 dir = (e.B - e.A) / e.Len;
            Vector2 mid = (e.A + e.B) * 0.5f + e.N * 0.008f;
            float yaw = YawOf(e.N);
            Color strap = new Color(0.24f, 0.25f, 0.27f, 1f);
            k.Box(0, MeshKit.At(W(mid, e.Height * 0.5f), new Vector3(0f, yaw, 0f)), new Vector3(e.Len, 0.06f, 0.02f), 0.004f, strap);
            k.Box(0, MeshKit.At(W(mid, e.Height - 0.025f), new Vector3(0f, yaw, 0f)), new Vector3(e.Len, 0.05f, 0.03f), 0.006f, strap);
            k.Box(0, MeshKit.At(W(mid, 0.03f), new Vector3(0f, yaw, 0f)), new Vector3(e.Len, 0.06f, 0.025f), 0.006f, strap);
            // Two stencil-like light plates near the middle.
            Color plateC = new Color(0.82f, 0.8f, 0.72f, 0f);
            if (e.Len > 1.2f)
                k.Box(0, MeshKit.At(W(mid + dir * 0f + e.N * 0.004f, e.Height * 0.72f), new Vector3(0f, yaw, 0f)), new Vector3(Mathf.Min(0.5f, e.Len * 0.3f), 0.16f, 0.006f), 0f, plateC);
        }

        /// <summary>
        /// A wall lamp above head height: a dark housing on a bracket, a downward shade with a warm lens, and a faint
        /// warm halo on the wall and on the floor below. Restrained: practical light, not neon.
        /// </summary>
        static void WallLamp(MeshKit housing, MeshKit lens, MeshKit halo, Edge e, System.Random rng)
        {
            Vector2 dir = (e.B - e.A) / e.Len;
            float along = Mathf.Lerp(0.8f, e.Len - 0.8f, 0.3f + (float)rng.NextDouble() * 0.4f);
            float y = Mathf.Min(2.6f, e.Height - 0.35f);
            Vector2 at = e.A + dir * along;
            float yaw = YawOf(e.N);
            Color dark = new Color(0.13f, 0.135f, 0.14f, 1f);
            Matrix4x4 m = MeshKit.At(W(at, y), new Vector3(0f, yaw, 0f));
            housing.Box(0, m * Matrix4x4.TRS(new Vector3(0f, 0.08f, 0.03f), Quaternion.identity, Vector3.one), new Vector3(0.14f, 0.2f, 0.05f), 0.01f, dark);
            housing.Box(0, m * Matrix4x4.TRS(new Vector3(0f, 0.1f, 0.15f), Quaternion.identity, Vector3.one), new Vector3(0.03f, 0.03f, 0.22f), 0.006f, dark);
            housing.Box(0, m * Matrix4x4.TRS(new Vector3(0f, 0.04f, 0.27f), Quaternion.Euler(18f, 0f, 0f), Vector3.one), new Vector3(0.26f, 0.06f, 0.2f), 0.012f, new Color(0.24f, 0.26f, 0.24f, 0.6f));
            Vector3 lc = m.MultiplyPoint3x4(new Vector3(0f, 0.005f, 0.27f));
            Vector3 lx = m.MultiplyVector(Vector3.right) * 0.1f, lz = m.MultiplyVector(Vector3.forward) * 0.075f;
            lens.Quad(0, lc - lx - lz, lc + lx - lz, lc + lx + lz, lc - lx + lz, Vector3.down, Color.white);
            var col = new Color(LampColor.r, LampColor.g, LampColor.b, 0.22f);
            Vector3 n3 = W(e.N, 0f);
            Vector2 a = at - dir * 0.8f + e.N * 0.03f, b = at + dir * 0.8f + e.N * 0.03f;
            halo.QuadUv(0, W(a, y - 0.9f), W(b, y - 0.9f), W(b, y + 0.15f), W(a, y + 0.15f), n3, col,
                        new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0.55f), new Vector2(0f, 0.55f));
            var fc = new Color(LampColor.r, LampColor.g, LampColor.b, 0.1f);
            Vector2 fa = at - dir * 0.9f, fb = at + dir * 0.9f;
            halo.QuadUv(0, W(fa, 0.016f), W(fb, 0.016f), W(fb + e.N * 1.2f, 0.016f), W(fa + e.N * 1.2f, 0.016f), Vector3.up, fc,
                        new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 1f), new Vector2(0f, 1f));
        }

        /// <summary>An air-conditioning unit on brackets high on a tall wall: casing, front grille, a fan disc and a drain pipe.</summary>
        static void AirCon(MeshKit k, Edge e, System.Random rng)
        {
            Vector2 dir = (e.B - e.A) / e.Len;
            float along = Mathf.Lerp(0.8f, e.Len - 0.8f, (float)rng.NextDouble());
            float y = Mathf.Min(e.Height - 0.55f, 2.85f + (float)rng.NextDouble() * 0.4f);
            Vector2 at = e.A + dir * along;
            Matrix4x4 m = MeshKit.At(W(at, y), new Vector3(0f, YawOf(e.N), 0f));
            float v = 0.72f + (float)rng.NextDouble() * 0.1f;
            Color casing = new Color(v, v * 0.99f, v * 0.95f, 0f);
            Color dark = new Color(0.12f, 0.12f, 0.13f, 1f);
            k.Box(0, m * Matrix4x4.TRS(new Vector3(0f, 0f, 0.28f), Quaternion.identity, Vector3.one), new Vector3(0.86f, 0.58f, 0.48f), 0.025f, casing);
            k.Box(0, m * Matrix4x4.TRS(new Vector3(-0.18f, 0f, 0.525f), Quaternion.identity, Vector3.one), new Vector3(0.44f, 0.44f, 0.02f), 0.006f, dark);
            k.Cylinder(0, m * Matrix4x4.TRS(new Vector3(-0.18f, 0f, 0.535f), Quaternion.identity, Vector3.one), 0.19f, 0.01f, 18, new Color(0.3f, 0.31f, 0.32f, 1f));
            for (int i = 0; i < 5; i++)
                k.Box(0, m * Matrix4x4.TRS(new Vector3(0.24f, -0.18f + i * 0.09f, 0.525f), Quaternion.identity, Vector3.one), new Vector3(0.3f, 0.025f, 0.02f), 0.003f, dark);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(0, m * Matrix4x4.TRS(new Vector3(s * 0.34f, -0.33f, 0.26f), Quaternion.identity, Vector3.one), new Vector3(0.04f, 0.04f, 0.52f), 0.006f, dark);
                k.Box(0, m * Matrix4x4.TRS(new Vector3(s * 0.34f, -0.15f, 0.03f), Quaternion.Euler(-45f, 0f, 0f), Vector3.one), new Vector3(0.035f, 0.035f, 0.5f), 0.006f, dark);
            }
            Vector3 drainTop = m.MultiplyPoint3x4(new Vector3(0.38f, -0.29f, 0.1f));
            Vector3 drainBot = new Vector3(drainTop.x, 0.4f, drainTop.z);
            if (drainTop.y - drainBot.y > 0.1f)
                k.Cylinder(0, MeshKit.Along((drainTop + drainBot) * 0.5f, Vector3.up), 0.018f, drainTop.y - drainBot.y, 8, new Color(0.5f, 0.5f, 0.48f, 0f));
        }

        static readonly Color[] SignColors =
        {
            new Color(0.82f, 0.62f, 0.16f, 0f),
            new Color(0.2f, 0.32f, 0.46f, 0f),
            new Color(0.62f, 0.62f, 0.6f, 0f),
            new Color(0.5f, 0.18f, 0.14f, 0f),
        };

        /// <summary>A painted metal sign plate flush on the wall (warning, info or plain), with two bolts and stencil bands.</summary>
        static void SignPlate(MeshKit k, Edge e, System.Random rng)
        {
            Vector2 dir = (e.B - e.A) / e.Len;
            float along = Mathf.Lerp(0.6f, e.Len - 0.6f, (float)rng.NextDouble());
            float y = 1.9f + (float)rng.NextDouble() * 0.5f;
            Matrix4x4 m = MeshKit.At(W(e.A + dir * along, y), new Vector3(0f, YawOf(e.N), 0f));
            Color c = SignColors[rng.Next(SignColors.Length)];
            Color stripe = new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 0f);
            float sw = 0.55f + (float)rng.NextDouble() * 0.35f, sh = 0.32f + (float)rng.NextDouble() * 0.15f;
            k.Box(0, m * Matrix4x4.TRS(new Vector3(0f, 0f, 0.012f), Quaternion.identity, Vector3.one), new Vector3(sw, sh, 0.012f), 0.004f, c);
            k.Box(0, m * Matrix4x4.TRS(new Vector3(0f, -sh * 0.18f, 0.02f), Quaternion.identity, Vector3.one), new Vector3(sw * 0.8f, sh * 0.16f, 0.004f), 0f, stripe);
            k.Box(0, m * Matrix4x4.TRS(new Vector3(0f, sh * 0.2f, 0.02f), Quaternion.identity, Vector3.one), new Vector3(sw * 0.5f, sh * 0.1f, 0.004f), 0f, stripe);
            for (int s = -1; s <= 1; s += 2)
                k.Cylinder(0, m * Matrix4x4.TRS(new Vector3(s * (sw * 0.5f - 0.04f), sh * 0.5f - 0.04f, 0.02f), Quaternion.identity, Vector3.one), 0.012f, 0.01f, 8, new Color(0.3f, 0.3f, 0.3f, 1f));
        }

        // ---- floor props in inside corners -------------------------------------------------------------
        static readonly Color[] BarrelColors =
        {
            new Color(0.2f, 0.3f, 0.42f, 0f),
            new Color(0.48f, 0.16f, 0.12f, 0f),
            new Color(0.3f, 0.34f, 0.22f, 0f),
            new Color(0.56f, 0.5f, 0.4f, 0f),
        };

        /// <summary>
        /// Small prop groups tucked into inside corners where two wall faces meet: barrels, a crate with a smaller
        /// one on top, or a pallet with sacks. Kept within ~0.9 m of both faces and away from spawns, choke mouths and
        /// the site letters, so they dress the space without changing where people stand or look.
        /// </summary>
        static void CornerProps(MeshKit metal, MeshKit wood, MapDef map, List<Edge> edges, List<Vector2> keepOut, float density, System.Random rng)
        {
            var used = new List<Vector2>();
            foreach (Edge e in edges)
            {
                if (e.Crate || e.Height < 2.4f) continue;
                Vector2 dir = (e.B - e.A) / e.Len;
                for (int end = 0; end < 2; end++)
                {
                    Vector2 corner = end == 0 ? e.A : e.B;
                    Vector2 inward = end == 0 ? dir : -dir;
                    // Another wall continues past this end on the open side: an inside corner.
                    Vector2 probe = corner + e.N * 0.5f - inward * 0.35f;
                    if (MapBuilder.IsClear(probe, map, 0f)) continue;
                    Vector2 spot = corner + e.N * 0.5f + inward * 0.5f;
                    if (!MapBuilder.IsClear(spot, map, 0.3f)) continue;
                    if (rng.NextDouble() > 0.4 * density) continue;
                    bool near = false;
                    foreach (Vector2 q in keepOut) if ((q - spot).sqrMagnitude < 16f) { near = true; break; }
                    foreach (Vector2 q in used) if ((q - spot).sqrMagnitude < 36f) { near = true; break; }
                    if (near) continue;
                    used.Add(spot);
                    float yaw = YawOf(e.N);
                    int kind = rng.Next(3);
                    if (kind == 0)
                    {
                        int count = 1 + rng.Next(2);
                        for (int i = 0; i < count; i++)
                        {
                            Vector2 p = corner + e.N * (0.36f + i * 0.08f) + inward * (0.36f + i * 0.6f);
                            Barrel(metal, p, BarrelColors[rng.Next(BarrelColors.Length)], (float)rng.NextDouble() * 360f);
                        }
                    }
                    else if (kind == 1)
                    {
                        Vector2 p = corner + e.N * 0.45f + inward * 0.45f;
                        float tone = 0.72f + (float)rng.NextDouble() * 0.15f;
                        var c = new Color(tone, tone * 0.86f, tone * 0.66f, 0f);
                        var c2 = new Color(c.r * 0.92f, c.g * 0.92f, c.b * 0.92f, 0f);
                        wood.Box(0, MeshKit.At(W(p, 0.38f), new Vector3(0f, yaw, 0f)), new Vector3(0.76f, 0.76f, 0.76f), 0.02f, c);
                        WoodCrateTrim(metal, p, 0.76f, yaw);
                        if (rng.NextDouble() < 0.6)
                            wood.Box(0, MeshKit.At(W(p + inward * 0.04f, 0.97f), new Vector3(0f, yaw + 20f + (float)rng.NextDouble() * 20f, 0f)), new Vector3(0.44f, 0.42f, 0.44f), 0.015f, c2);
                    }
                    else
                    {
                        Vector2 p = corner + e.N * 0.5f + inward * 0.62f;
                        Matrix4x4 pm = MeshKit.At(W(p, 0f), new Vector3(0f, yaw, 0f));
                        var slat = new Color(0.66f, 0.55f, 0.4f, 0f);
                        var runner = new Color(0.53f, 0.44f, 0.32f, 0f);
                        for (int i = -1; i <= 1; i++)
                            wood.Box(0, pm * Matrix4x4.TRS(new Vector3(i * 0.36f, 0.05f, 0f), Quaternion.identity, Vector3.one), new Vector3(0.1f, 0.09f, 0.8f), 0.008f, runner);
                        for (int i = 0; i < 5; i++)
                            wood.Box(0, pm * Matrix4x4.TRS(new Vector3(0f, 0.115f, -0.34f + i * 0.17f), Quaternion.identity, Vector3.one), new Vector3(1.1f, 0.03f, 0.12f), 0.004f, slat);
                        var sack = new Color(0.62f, 0.56f, 0.44f, 0f);
                        int sacks = 1 + rng.Next(3);
                        for (int i = 0; i < sacks; i++)
                        {
                            Vector3 sp = pm.MultiplyPoint3x4(new Vector3(-0.27f + i * 0.27f, 0.22f, (i % 2) * 0.12f - 0.06f));
                            Loft.Blob(wood, Matrix4x4.identity, sp, new Vector3(0.14f, 0.09f, 0.28f), sack, new Vector3(0f, yaw + i * 12f, 0f), 12, 6, 3f);
                        }
                    }
                }
            }
        }

        static void Barrel(MeshKit k, Vector2 p, Color c, float yaw)
        {
            const float r = 0.29f, hgt = 0.88f;
            Color rim = new Color(c.r * 0.7f, c.g * 0.7f, c.b * 0.7f, 0.7f);
            k.Cylinder(0, MeshKit.Along(W(p, hgt * 0.5f), Vector3.up), r, hgt, 18, c);
            k.Cylinder(0, MeshKit.Along(W(p, hgt * 0.33f), Vector3.up), r + 0.012f, 0.035f, 18, rim, false, false);
            k.Cylinder(0, MeshKit.Along(W(p, hgt * 0.67f), Vector3.up), r + 0.012f, 0.035f, 18, rim, false, false);
            k.Cylinder(0, MeshKit.Along(W(p, hgt), Vector3.up), r + 0.008f, 0.03f, 18, rim);
            Vector2 cap = p + new Vector2(Mathf.Cos(yaw * Mathf.Deg2Rad), Mathf.Sin(yaw * Mathf.Deg2Rad)) * 0.15f;
            k.Cylinder(0, MeshKit.Along(W(cap, hgt + 0.02f), Vector3.up), 0.035f, 0.02f, 10, rim);
        }

        static void WoodCrateTrim(MeshKit k, Vector2 p, float size, float yaw)
        {
            Color strap = new Color(0.22f, 0.23f, 0.24f, 1f);
            Matrix4x4 m = MeshKit.At(W(p, size * 0.5f), new Vector3(0f, yaw, 0f));
            float hs = size * 0.5f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                k.Box(0, m * Matrix4x4.TRS(new Vector3(sx * hs, 0f, sz * hs), Quaternion.identity, Vector3.one), new Vector3(0.06f, size + 0.01f, 0.06f), 0.008f, strap);
        }

        // ---- landmarks -------------------------------------------------------------------------------
        /// <summary>
        /// One landmark per large exterior area, standing on the wall head beside it so it is seen against the sky
        /// and never in the way: a water tower, a satellite dish, a tower crane or a painted billboard, chosen by a
        /// stable hash of the area's name. All of it above the wall top, and no collider.
        /// </summary>
        static void Landmarks(MeshKit k, MapDef map)
        {
            foreach (MapZones.LandmarkSite site in MapZones.Landmarks(map))
            {
                Matrix4x4 m = MeshKit.At(new Vector3(site.At.x, site.Base, site.At.y), new Vector3(0f, site.Yaw, 0f));
                switch (site.Kind)
                {
                    case 0: WaterTower(k, m); break;
                    case 1: Dish(k, m); break;
                    case 2: Crane(k, m); break;
                    default: Billboard(k, m); break;
                }
            }
        }

        /// <summary>A water tower on four braced legs: tank with hoops, conical roof and a ladder.</summary>
        static void WaterTower(MeshKit k, Matrix4x4 m)
        {
            Color steel = new Color(0.34f, 0.36f, 0.38f, 1f);
            Color paint = new Color(0.76f, 0.74f, 0.68f, 0f);
            const float legTop = 3.3f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 a = m.MultiplyPoint3x4(new Vector3(sx * 0.72f, 0.02f, sz * 0.72f));
                Vector3 b = m.MultiplyPoint3x4(new Vector3(sx * 1.08f, legTop, sz * 1.08f));
                k.Cylinder(0, MeshKit.Along((a + b) * 0.5f, b - a), 0.085f, (b - a).magnitude, 8, steel);
            }
            for (int lvl = 0; lvl < 2; lvl++)
            {
                float y = 1.05f + lvl * 1.1f;
                float half = Mathf.Lerp(0.78f, 1.03f, y / legTop);
                for (int s = -1; s <= 1; s += 2)
                {
                    k.Box(0, m * Matrix4x4.TRS(new Vector3(0f, y, s * half), Quaternion.identity, Vector3.one), new Vector3(half * 2f, 0.06f, 0.06f), 0.005f, steel);
                    k.Box(0, m * Matrix4x4.TRS(new Vector3(s * half, y, 0f), Quaternion.identity, Vector3.one), new Vector3(0.06f, 0.06f, half * 2f), 0.005f, steel);
                }
            }
            k.Cylinder(0, MeshKit.Along(m.MultiplyPoint3x4(new Vector3(0f, legTop + 1.3f, 0f)), Vector3.up), 1.42f, 2.6f, 16, paint);
            k.Cylinder(0, MeshKit.Along(m.MultiplyPoint3x4(new Vector3(0f, legTop + 0.24f, 0f)), Vector3.up), 1.46f, 0.12f, 16, steel);
            k.Cylinder(0, MeshKit.Along(m.MultiplyPoint3x4(new Vector3(0f, legTop + 2.36f, 0f)), Vector3.up), 1.46f, 0.12f, 16, steel);
            List<Sec> roof = Loft.Begin();
            roof.Add(new Sec(0f, 1.44f, 1.44f));
            roof.Add(new Sec(0.9f, 0.22f, 0.22f));
            Loft.Tube(k, m * Matrix4x4.TRS(new Vector3(0f, legTop + 2.6f, 0f), Quaternion.identity, Vector3.one), roof, 16, new Color(0.5f, 0.47f, 0.42f, 0f));
            for (int i = 0; i < 8; i++)
                k.Box(0, m * Matrix4x4.TRS(new Vector3(0.7f, 0.35f + i * 0.4f, 0.82f), Quaternion.identity, Vector3.one), new Vector3(0.32f, 0.03f, 0.03f), 0f, steel);
        }

        /// <summary>A satellite dish on a mast: a shallow bowl of concentric rings, tilted out over the yard, with a feed horn.</summary>
        static void Dish(MeshKit k, Matrix4x4 m)
        {
            Color white = new Color(0.83f, 0.82f, 0.79f, 0f);
            Color steel = new Color(0.3f, 0.32f, 0.34f, 1f);
            k.Cylinder(0, MeshKit.Along(m.MultiplyPoint3x4(new Vector3(0f, 1.45f, 0f)), Vector3.up), 0.14f, 2.9f, 10, steel);
            k.Box(0, m * Matrix4x4.TRS(new Vector3(0f, 0.06f, 0f), Quaternion.identity, Vector3.one), new Vector3(0.9f, 0.12f, 0.9f), 0.02f, steel);
            Matrix4x4 d = m * Matrix4x4.TRS(new Vector3(0f, 3.5f, 0.1f), Quaternion.Euler(52f, 0f, 0f), Vector3.one);
            float[] r = { 0.3f, 0.75f, 1.15f, 1.5f, 1.75f };
            float[] dy = { 0f, 0.05f, 0.14f, 0.29f, 0.5f };
            const int sides = 20;
            Vector3 face = d.MultiplyVector(Vector3.up).normalized;
            for (int ring = 0; ring + 1 < r.Length; ring++)
            {
                for (int s = 0; s < sides; s++)
                {
                    float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                    Vector3 p0 = d.MultiplyPoint3x4(new Vector3(Mathf.Cos(a0) * r[ring], dy[ring], Mathf.Sin(a0) * r[ring]));
                    Vector3 p1 = d.MultiplyPoint3x4(new Vector3(Mathf.Cos(a1) * r[ring], dy[ring], Mathf.Sin(a1) * r[ring]));
                    Vector3 p2 = d.MultiplyPoint3x4(new Vector3(Mathf.Cos(a1) * r[ring + 1], dy[ring + 1], Mathf.Sin(a1) * r[ring + 1]));
                    Vector3 p3 = d.MultiplyPoint3x4(new Vector3(Mathf.Cos(a0) * r[ring + 1], dy[ring + 1], Mathf.Sin(a0) * r[ring + 1]));
                    k.Quad(0, p0, p1, p2, p3, face, white);
                }
            }
            List<Sec> back = Loft.Begin();
            back.Add(new Sec(-0.5f, 0.25f, 0.25f));
            back.Add(new Sec(0f, 1.75f, 1.75f));
            Loft.Tube(k, d, back, sides, new Color(0.62f, 0.61f, 0.58f, 0f), true, false);
            Vector3 focus = d.MultiplyPoint3x4(new Vector3(0f, 1.15f, 0f));
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                Vector3 rim = d.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * 1.55f, 0.35f, Mathf.Sin(a) * 1.55f));
                k.Cylinder(0, MeshKit.Along((rim + focus) * 0.5f, focus - rim), 0.025f, (focus - rim).magnitude, 6, steel);
            }
            k.Cylinder(0, MeshKit.Along(focus, face), 0.09f, 0.3f, 8, steel);
        }

        /// <summary>A tower crane: a braced mast, a jib reaching over the yard with cross ties, a counterweight and a hook.</summary>
        static void Crane(MeshKit k, Matrix4x4 m)
        {
            Color steel = new Color(0.72f, 0.66f, 0.22f, 1f);
            Color dark = new Color(0.24f, 0.25f, 0.26f, 1f);
            const float mastTop = 7.2f;
            k.Box(0, m * Matrix4x4.TRS(new Vector3(0f, 0.18f, 0f), Quaternion.identity, Vector3.one), new Vector3(1.5f, 0.36f, 1.5f), 0.03f, new Color(0.6f, 0.6f, 0.58f, 0f));
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 a = m.MultiplyPoint3x4(new Vector3(sx * 0.45f, 0.36f, sz * 0.45f));
                Vector3 b = m.MultiplyPoint3x4(new Vector3(sx * 0.38f, mastTop, sz * 0.38f));
                k.Cylinder(0, MeshKit.Along((a + b) * 0.5f, b - a), 0.075f, (b - a).magnitude, 6, steel);
            }
            for (int lvl = 0; lvl < 6; lvl++)
            {
                float y = 1.1f + lvl * 1f;
                for (int s = -1; s <= 1; s += 2)
                {
                    k.Box(0, m * Matrix4x4.TRS(new Vector3(0f, y, s * 0.42f), Quaternion.identity, Vector3.one), new Vector3(0.84f, 0.05f, 0.05f), 0f, dark);
                    k.Box(0, m * Matrix4x4.TRS(new Vector3(s * 0.42f, y, 0f), Quaternion.identity, Vector3.one), new Vector3(0.05f, 0.05f, 0.84f), 0f, dark);
                }
            }
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(0, m * Matrix4x4.TRS(new Vector3(s * 0.4f, mastTop + 0.25f, 3.9f), Quaternion.identity, Vector3.one), new Vector3(0.12f, 0.12f, 9.4f), 0f, steel);
                k.Box(0, m * Matrix4x4.TRS(new Vector3(s * 0.4f, mastTop + 0.85f, 3f), Quaternion.identity, Vector3.one), new Vector3(0.09f, 0.09f, 8.6f), 0f, steel);
                k.Box(0, m * Matrix4x4.TRS(new Vector3(s * 0.4f, mastTop + 0.25f, -1.1f), Quaternion.identity, Vector3.one), new Vector3(0.12f, 0.12f, 1.6f), 0f, steel);
            }
            for (int i = 0; i < 8; i++)
            {
                float z = 0.4f + i * 1f;
                for (int s = -1; s <= 1; s += 2)
                    k.Box(0, m * Matrix4x4.TRS(new Vector3(s * 0.4f, mastTop + 0.55f, z), Quaternion.identity, Vector3.one), new Vector3(0.05f, 0.6f, 0.05f), 0f, dark);
                k.Box(0, m * Matrix4x4.TRS(new Vector3(0f, mastTop + 0.55f, z), Quaternion.identity, Vector3.one), new Vector3(0.84f, 0.05f, 0.05f), 0f, dark);
            }
            k.Box(0, m * Matrix4x4.TRS(new Vector3(0f, mastTop + 0.1f, -1.4f), Quaternion.identity, Vector3.one), new Vector3(1.5f, 0.9f, 1f), 0.04f, new Color(0.5f, 0.5f, 0.48f, 0f));
            k.Box(0, m * Matrix4x4.TRS(new Vector3(0.5f, mastTop - 0.9f, 0.6f), Quaternion.identity, Vector3.one), new Vector3(0.9f, 1.1f, 1.2f), 0.04f, new Color(0.82f, 0.74f, 0.36f, 0f));
            Vector3 hookTop = m.MultiplyPoint3x4(new Vector3(0f, mastTop + 0.1f, 7.8f));
            Vector3 hookBot = m.MultiplyPoint3x4(new Vector3(0f, mastTop - 2.6f, 7.8f));
            k.Cylinder(0, MeshKit.Along((hookTop + hookBot) * 0.5f, hookBot - hookTop), 0.03f, (hookTop - hookBot).magnitude, 5, dark);
            k.Box(0, MeshKit.At(hookBot, Vector3.zero), new Vector3(0.3f, 0.5f, 0.3f), 0.04f, new Color(0.42f, 0.42f, 0.4f, 0f));
        }

        /// <summary>A painted billboard on two posts on the wall head, facing the yard: frame, bands and letter bars.</summary>
        static void Billboard(MeshKit k, Matrix4x4 m)
        {
            Color post = new Color(0.3f, 0.31f, 0.32f, 1f);
            Color paint = new Color(0.82f, 0.34f, 0.24f, 0f);
            Color ink = new Color(0.94f, 0.92f, 0.86f, 0f);
            Color frame = new Color(0.22f, 0.24f, 0.28f, 0f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(0, m * Matrix4x4.TRS(new Vector3(s * 2.5f, 1.25f, 0f), Quaternion.identity, Vector3.one), new Vector3(0.22f, 2.5f, 0.22f), 0.02f, post);
                k.Box(0, m * Matrix4x4.TRS(new Vector3(s * 2.5f, 0.1f, 0f), Quaternion.identity, Vector3.one), new Vector3(0.6f, 0.2f, 0.6f), 0.02f, new Color(0.55f, 0.55f, 0.53f, 0f));
            }
            Matrix4x4 p = m * Matrix4x4.TRS(new Vector3(0f, 3.7f, 0.16f), Quaternion.identity, Vector3.one);
            k.Box(0, p, new Vector3(6.4f, 2.8f, 0.18f), 0.02f, paint);
            k.Box(0, p * Matrix4x4.TRS(new Vector3(0f, 1.42f, 0f), Quaternion.identity, Vector3.one), new Vector3(6.6f, 0.16f, 0.24f), 0.01f, frame);
            k.Box(0, p * Matrix4x4.TRS(new Vector3(0f, -1.42f, 0f), Quaternion.identity, Vector3.one), new Vector3(6.6f, 0.16f, 0.24f), 0.01f, frame);
            for (int s = -1; s <= 1; s += 2)
                k.Box(0, p * Matrix4x4.TRS(new Vector3(s * 3.2f, 0f, 0f), Quaternion.identity, Vector3.one), new Vector3(0.16f, 2.8f, 0.24f), 0.01f, frame);
            k.Box(0, p * Matrix4x4.TRS(new Vector3(-0.3f, 0.55f, 0.11f), Quaternion.identity, Vector3.one), new Vector3(4.2f, 0.62f, 0.04f), 0f, ink);
            k.Box(0, p * Matrix4x4.TRS(new Vector3(-1.1f, -0.25f, 0.11f), Quaternion.identity, Vector3.one), new Vector3(2.8f, 0.4f, 0.04f), 0f, ink);
            k.Box(0, p * Matrix4x4.TRS(new Vector3(1.9f, -0.6f, 0.11f), Quaternion.identity, Vector3.one), new Vector3(1.1f, 0.24f, 0.04f), 0f, new Color(0.98f, 0.78f, 0.2f, 0f));
            for (int q = -1; q <= 1; q += 2)
            for (int v = -1; v <= 1; v += 2)
                k.Cylinder(0, p * Matrix4x4.TRS(new Vector3(q * 3.05f, v * 1.28f, 0.1f), Quaternion.identity, Vector3.one), 0.045f, 0.04f, 8, new Color(0.5f, 0.5f, 0.48f, 1f));
        }

        // ---- chokes ----------------------------------------------------------------------------------
        static void ClosestOnSegments(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2, out Vector2 c1, out Vector2 c2)
        {
            Vector2 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector2.Dot(d1, d1), e = Vector2.Dot(d2, d2), f = Vector2.Dot(d2, r);
            float s, t;
            if (a <= 1e-8f && e <= 1e-8f) { s = t = 0f; }
            else if (a <= 1e-8f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector2.Dot(d1, r);
                if (e <= 1e-8f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector2.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > 1e-8f ? Mathf.Clamp01((b * f - c * e) / denom) : 0.5f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }

        /// <summary>A narrow opening between two walls (a door or a choke): the closest points on each side, the width and the lower wall height.</summary>
        public struct Choke
        {
            public Vector2 A, B;
            public float Width;
            public float MinHeight;
        }

        static List<Choke> chokeCache;
        static MapDef chokeMap;

        /// <summary>
        /// The openings between pairs of walls 2.2 to 6.5 m apart with clear floor between them, narrowest first, at
        /// least 7 m apart from each other (cached per map).
        /// </summary>
        public static List<Choke> FindChokes(MapDef map)
        {
            if (map == null || map.walls == null) return new List<Choke>();
            if (chokeCache != null && ReferenceEquals(chokeMap, map)) return chokeCache;
            var polys = new List<List<Vector2>>();
            var heights = new List<float>();
            var mins = new List<Vector2>();
            var maxs = new List<Vector2>();
            foreach (WallDef wall in map.walls)
            {
                if (wall == null || wall.points == null || wall.points.Count < 3 || wall.height < 1f) continue;
                List<Vector2> p = MapBuilder.ToCounterClockwise(wall.points);
                if (p.Count < 3) continue;
                Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
                foreach (Vector2 v in p) { mn = Vector2.Min(mn, v); mx = Vector2.Max(mx, v); }
                polys.Add(p);
                heights.Add(wall.height);
                mins.Add(mn);
                maxs.Add(mx);
            }
            var gaps = new List<Choke>();
            const float minGap = 2.2f, maxGap = 6.5f;
            for (int i = 0; i < polys.Count; i++)
            {
                for (int j = i + 1; j < polys.Count; j++)
                {
                    if (mins[i].x > maxs[j].x + maxGap || mins[j].x > maxs[i].x + maxGap || mins[i].y > maxs[j].y + maxGap || mins[j].y > maxs[i].y + maxGap) continue;
                    float best = float.MaxValue;
                    Vector2 ba = Vector2.zero, bb = Vector2.zero;
                    List<Vector2> pi = polys[i], pj = polys[j];
                    for (int a = 0; a < pi.Count; a++)
                    {
                        for (int b = 0; b < pj.Count; b++)
                        {
                            ClosestOnSegments(pi[a], pi[(a + 1) % pi.Count], pj[b], pj[(b + 1) % pj.Count], out Vector2 c1, out Vector2 c2);
                            float d = (c1 - c2).magnitude;
                            if (d < best) { best = d; ba = c1; bb = c2; }
                        }
                    }
                    if (best < minGap || best > maxGap) continue;
                    Vector2 mid = (ba + bb) * 0.5f;
                    if (!MapBuilder.IsClear(mid, map, 0.6f)) continue;
                    bool blocked = false;
                    for (float t = 0.2f; t < 0.85f && !blocked; t += 0.2f) blocked = !MapBuilder.IsClear(Vector2.Lerp(ba, bb, t), map, 0f);
                    if (blocked) continue;
                    gaps.Add(new Choke { A = ba, B = bb, Width = best, MinHeight = Mathf.Min(heights[i], heights[j]) });
                }
            }
            gaps.Sort((x, y) => x.Width.CompareTo(y.Width));
            var placed = new List<Choke>();
            foreach (Choke g in gaps)
            {
                Vector2 mid = (g.A + g.B) * 0.5f;
                bool close = false;
                foreach (Choke p in placed) if (((p.A + p.B) * 0.5f - mid).magnitude < 7f) { close = true; break; }
                if (!close) placed.Add(g);
            }
            chokeCache = placed;
            chokeMap = map;
            return placed;
        }

        /// <summary>Hazard stripes across the narrowest openings between walls (doors and chokes, 2.2 to 6.5 m wide).</summary>
        static void Chokes(MeshKit k, MapDef map, float density)
        {
            int limit = Mathf.RoundToInt(14f * density);
            int count = 0;
            foreach (Choke g in FindChokes(map))
            {
                if (count++ >= limit) break;
                Vector2 dir = (g.B - g.A).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x) * 0.2f;
                Vector2 a = g.A + dir * 0.12f, b = g.B - dir * 0.12f;
                float len = (b - a).magnitude;
                float u = len / 0.8f;
                var white = new Color(1f, 1f, 1f, 0.7f);
                k.QuadUv(0, W(a - side, 0.012f), W(b - side, 0.012f), W(b + side, 0.012f), W(a + side, 0.012f), Vector3.up, white,
                         new Vector2(0f, 0f), new Vector2(u, 0f), new Vector2(u, 1f), new Vector2(0f, 1f));
            }
        }

        /// <summary>The spike plant-zone stencil around each site's letter.</summary>
        static void PlantZones(MeshKit k, MapDef map)
        {
            foreach (KeyValuePair<string, List<Vec2>> site in map.SiteList())
            {
                Vector2 c = MapBuilder.FindFreeSpot(site.Value, map, 2.5f);
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                foreach (Vec2 p in site.Value)
                {
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                }
                float r = Mathf.Clamp(Mathf.Min(maxX - minX, maxY - minY) * 0.38f, 2.5f, 4.8f);
                Color sc = Palette.Site(site.Key);
                var col = new Color(sc.r, sc.g, sc.b, 0.85f);
                k.QuadUv(0, new Vector3(c.x - r, 0.011f, c.y - r), new Vector3(c.x + r, 0.011f, c.y - r), new Vector3(c.x + r, 0.011f, c.y + r), new Vector3(c.x - r, 0.011f, c.y + r), Vector3.up, col,
                         new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            }
        }

        /// <summary>
        /// Buildings in a ring well outside the map, softened by the fog into a skyline: each a block with floor
        /// bands (slab edges and window rows), a setback upper storey on some, and rooftop water tanks, plant rooms or
        /// masts, so the horizon reads as a city and not as boxes.
        /// </summary>
        static void Skyline(MeshKit k, float w, float h, float density, System.Random rng)
        {
            var c = new Vector2(w * 0.5f, h * 0.5f);
            float r0 = Mathf.Max(w, h) * 0.5f + 70f;
            int n = Mathf.Clamp(Mathf.RoundToInt(40f * density), 14, 56);
            for (int i = 0; i < n; i++)
            {
                float a = (i + (float)rng.NextDouble() * 0.7f) / n * Mathf.PI * 2f;
                float r = r0 + (float)rng.NextDouble() * 90f;
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                float bw = 10f + (float)rng.NextDouble() * 20f, bd = 10f + (float)rng.NextDouble() * 20f;
                float bh = 12f + (float)rng.NextDouble() * (float)rng.NextDouble() * 50f;
                float yaw = (float)rng.NextDouble() * 90f;
                float v = 0.34f + (float)rng.NextDouble() * 0.12f; // dark against the bright horizon, so the skyline reads as a silhouette
                var col = new Color(v * 0.98f, v * 0.92f, v * 0.88f, 0f);
                var band = new Color(v * 0.62f, v * 0.66f, v * 0.72f, 0f);
                var slab = new Color(v * 1.1f, v * 1.04f, v * 0.98f, 0f);
                Matrix4x4 m = MeshKit.At(new Vector3(p.x, 0f, p.y), new Vector3(0f, yaw, 0f));
                k.Box(0, m * Matrix4x4.TRS(new Vector3(0f, bh * 0.5f - 0.1f, 0f), Quaternion.identity, Vector3.one), new Vector3(bw, bh, bd), 0f, col);
                // Window rows (recessed-looking dark bands) and slab edges every storey.
                float storey = 3.4f + (float)rng.NextDouble() * 0.6f;
                for (float y = storey; y < bh - 1.5f; y += storey)
                {
                    k.Box(0, m * Matrix4x4.TRS(new Vector3(0f, y - storey * 0.45f, 0f), Quaternion.identity, Vector3.one), new Vector3(bw + 0.08f, storey * 0.42f, bd + 0.08f), 0f, band);
                    k.Box(0, m * Matrix4x4.TRS(new Vector3(0f, y, 0f), Quaternion.identity, Vector3.one), new Vector3(bw + 0.3f, 0.25f, bd + 0.3f), 0f, slab);
                }
                // Parapet.
                k.Box(0, m * Matrix4x4.TRS(new Vector3(0f, bh + 0.4f, 0f), Quaternion.identity, Vector3.one), new Vector3(bw + 0.2f, 0.8f, bd + 0.2f), 0f, slab);
                double roof = rng.NextDouble();
                if (roof < 0.35)
                {
                    float th = 3f + (float)rng.NextDouble() * 6f;
                    k.Box(0, m * Matrix4x4.TRS(new Vector3(bw * 0.1f, bh + th * 0.5f, 0f), Quaternion.identity, Vector3.one), new Vector3(bw * 0.55f, th, bd * 0.55f), 0f, col);
                    k.Box(0, m * Matrix4x4.TRS(new Vector3(bw * 0.1f, bh + th * 0.5f, 0f), Quaternion.identity, Vector3.one), new Vector3(bw * 0.55f + 0.06f, th * 0.35f, bd * 0.55f + 0.06f), 0f, band);
                }
                else if (roof < 0.7)
                {
                    // A water tank on legs.
                    Vector3 tp = m.MultiplyPoint3x4(new Vector3(-bw * 0.2f, bh + 3.2f, bd * 0.15f));
                    k.Cylinder(0, MeshKit.Along(tp, Vector3.up), 1.6f, 2.6f, 10, band);
                    k.Box(0, MeshKit.At(tp + Vector3.down * 2.1f, new Vector3(0f, yaw, 0f)), new Vector3(2.4f, 1.8f, 2.4f), 0f, new Color(v * 0.5f, v * 0.5f, v * 0.52f, 0f));
                }
                else if (roof < 0.85)
                {
                    Vector3 mp = m.MultiplyPoint3x4(new Vector3(bw * 0.25f, bh, -bd * 0.2f));
                    float mh = 6f + (float)rng.NextDouble() * 8f;
                    k.Cylinder(0, MeshKit.Along(mp + Vector3.up * mh * 0.5f, Vector3.up), 0.25f, mh, 6, band, true, false);
                }
            }
        }
    }
}

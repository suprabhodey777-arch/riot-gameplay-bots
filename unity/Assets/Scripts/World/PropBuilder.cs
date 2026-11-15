// Decoration generated from the MapDef alone: pipes and vents on tall walls
// (above head height or flush, so sight lines do not change), metal corner caps
// and straps on crates, emissive light strips with soft additive halos (site
// coloured near a site), hazard markings across chokes, a plant-zone stencil on
// every site and a fogged skyline beyond the outer walls. Nothing here has a
// collider: the sim's walls are the only geometry that matters for play.
// Everything is merged into one mesh per material, so the whole set costs about
// eight draw calls. Density (0.35 to 1.3) comes from the graphics preset.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    public static class PropBuilder
    {
        static Material metalMat, skyMat, hazardMat, plantMat, haloMat;
        static readonly Dictionary<Color, Material> stripMats = new Dictionary<Color, Material>();

        struct Edge
        {
            public Vector2 A, B, N;
            public float Len, Height;
            public bool Crate;
        }

        static void EnsureMaterials()
        {
            if (metalMat == null) metalMat = Mats.World(Color.white, Textures.Grain(), null, 0f, 0.35f, 0.8f, 0.6f, 1f);
            if (skyMat == null) skyMat = Mats.World(Color.white, Texture2D.whiteTexture, null, 0f, 0.1f, 0f, 0.1f, 1f);
            if (hazardMat == null) hazardMat = Mats.Decal(Textures.Hazard(), Color.white, 2455);
            if (plantMat == null) plantMat = Mats.Decal(Textures.PlantZone(), Color.white, 2456);
            if (haloMat == null) haloMat = Mats.Additive(Textures.GlowStrip(), Color.white, 1f, 3050);
        }

        static Material StripMat(Color c)
        {
            if (stripMats.TryGetValue(c, out Material m) && m != null) return m;
            m = Mats.Glow(c, 2.2f);
            stripMats[c] = m;
            return m;
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
            var halo = new MeshKit(1);
            var hazard = new MeshKit(1);
            var plant = new MeshKit(1);
            var sky = new MeshKit(1) { UvScale = 0.1f };
            var strips = new Dictionary<Color, MeshKit>();

            var sites = new List<KeyValuePair<string, Vector2>>();
            foreach (KeyValuePair<string, List<Vec2>> s in map.SiteList()) sites.Add(new KeyValuePair<string, Vector2>(s.Key, Geo.Centroid(s.Value)));

            List<Edge> edges = Edges(map, w, h);
            foreach (Edge e in edges)
            {
                if (e.Crate) { CrateDressing(metal, e); continue; }
                double roll = rng.NextDouble();
                if (e.Height >= 2.9f && e.Len >= 4f && roll < 0.45 * density) Pipe(metal, e, rng);
                if (e.Height >= 2.6f && e.Len >= 2.5f && rng.NextDouble() < 0.3 * density) Vent(metal, e, rng);
                if (e.Height >= 2.6f && e.Len >= 3f)
                {
                    Vector2 mid = (e.A + e.B) * 0.5f;
                    Color c = new Color(0.72f, 0.88f, 1f);
                    bool near = false;
                    foreach (KeyValuePair<string, Vector2> s in sites)
                    {
                        if ((s.Value - mid).magnitude < 15f)
                        {
                            c = Palette.Site(s.Key);
                            near = true;
                            break;
                        }
                    }
                    double p = near ? 0.85 * density : 0.22 * density;
                    if (rng.NextDouble() < p)
                    {
                        if (!strips.TryGetValue(c, out MeshKit sk)) strips[c] = sk = new MeshKit(1);
                        LightStrip(sk, metal, halo, e, c);
                    }
                }
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

            Chokes(hazard, map, density);
            PlantZones(plant, map);
            Skyline(sky, w, h, density, rng);

            Add(root, "Props metal", metal, metalMat, true);
            Add(root, "Light halos", halo, haloMat, false);
            Add(root, "Hazard markings", hazard, hazardMat, false);
            Add(root, "Plant zones", plant, plantMat, false);
            Add(root, "Skyline", sky, skyMat, false);
            foreach (KeyValuePair<Color, MeshKit> kv in strips)
            {
                Material m = StripMat(kv.Key);
                WorldLook.RegisterGlow(m, kv.Key, 2.2f);
                Add(root, "Light strips", kv.Value, m, false);
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

        /// <summary>An emissive strip in a dark housing, with a soft additive halo quad on the wall around it.</summary>
        static void LightStrip(MeshKit strip, MeshKit housing, MeshKit halo, Edge e, Color c)
        {
            Vector2 dir = (e.B - e.A) / e.Len;
            float y = Mathf.Min(2.45f, e.Height - 0.3f);
            float len = e.Len - 0.8f;
            Vector2 mid = (e.A + e.B) * 0.5f;
            float yaw = YawOf(e.N);
            housing.Box(0, MeshKit.At(W(mid + e.N * 0.015f, y), new Vector3(0f, yaw, 0f)), new Vector3(len + 0.06f, 0.07f, 0.03f), 0.008f, new Color(0.14f, 0.15f, 0.16f, 1f));
            strip.Box(0, MeshKit.At(W(mid + e.N * 0.032f, y), new Vector3(0f, yaw, 0f)), new Vector3(len, 0.03f, 0.012f), 0.004f, Color.white);
            Vector2 a = mid - dir * (len * 0.5f + 0.15f) + e.N * 0.05f, b = mid + dir * (len * 0.5f + 0.15f) + e.N * 0.05f;
            float hh = 0.22f;
            var col = new Color(c.r, c.g, c.b, 0.55f);
            Vector3 n3 = W(e.N, 0f);
            halo.QuadUv(0, W(a, y - hh), W(b, y - hh), W(b, y + hh), W(a, y + hh), n3, col,
                        new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            // A faint pool of light on the floor below.
            Vector2 fa = mid - dir * len * 0.5f, fb = mid + dir * len * 0.5f;
            var fc = new Color(c.r, c.g, c.b, 0.22f);
            halo.QuadUv(0, W(fa, 0.015f), W(fb, 0.015f), W(fb + e.N * 1.1f, 0.015f), W(fa + e.N * 1.1f, 0.015f), Vector3.up, fc,
                        new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 1f), new Vector2(0f, 1f));
        }

        // ---- chokes ----------------------------------------------------------------------------------
        struct Gap
        {
            public Vector2 A, B;
            public float D;
        }

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

        /// <summary>Hazard stripes across the narrowest openings between walls (doors and chokes, 2.2 to 6.5 m wide).</summary>
        static void Chokes(MeshKit k, MapDef map, float density)
        {
            var polys = new List<List<Vector2>>();
            var mins = new List<Vector2>();
            var maxs = new List<Vector2>();
            foreach (WallDef wall in map.walls)
            {
                if (wall.points == null || wall.points.Count < 3 || wall.height < 1f) continue;
                List<Vector2> p = MapBuilder.ToCounterClockwise(wall.points);
                Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
                foreach (Vector2 v in p) { mn = Vector2.Min(mn, v); mx = Vector2.Max(mx, v); }
                polys.Add(p);
                mins.Add(mn);
                maxs.Add(mx);
            }
            var gaps = new List<Gap>();
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
                    gaps.Add(new Gap { A = ba, B = bb, D = best });
                }
            }
            gaps.Sort((x, y) => x.D.CompareTo(y.D));
            int limit = Mathf.RoundToInt(14f * density);
            var placed = new List<Vector2>();
            foreach (Gap g in gaps)
            {
                if (placed.Count >= limit) break;
                Vector2 mid = (g.A + g.B) * 0.5f;
                bool close = false;
                foreach (Vector2 p in placed) if ((p - mid).magnitude < 7f) { close = true; break; }
                if (close) continue;
                placed.Add(mid);
                Vector2 dir = (g.B - g.A).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x) * 0.2f;
                Vector2 a = g.A + dir * 0.12f, b = g.B - dir * 0.12f;
                float len = (b - a).magnitude;
                float u = len / 0.8f;
                var white = new Color(1f, 1f, 1f, 0.85f);
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

        /// <summary>Blocky buildings in a ring well outside the map, softened by the fog into a skyline.</summary>
        static void Skyline(MeshKit k, float w, float h, float density, System.Random rng)
        {
            var c = new Vector2(w * 0.5f, h * 0.5f);
            float r0 = Mathf.Max(w, h) * 0.5f + 70f;
            int n = Mathf.Clamp(Mathf.RoundToInt(40f * density), 12, 56);
            for (int i = 0; i < n; i++)
            {
                float a = (i + (float)rng.NextDouble() * 0.7f) / n * Mathf.PI * 2f;
                float r = r0 + (float)rng.NextDouble() * 90f;
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                float bw = 10f + (float)rng.NextDouble() * 20f, bd = 10f + (float)rng.NextDouble() * 20f;
                float bh = 12f + (float)rng.NextDouble() * (float)rng.NextDouble() * 50f;
                float yaw = (float)rng.NextDouble() * 90f;
                float v = 0.5f + (float)rng.NextDouble() * 0.15f;
                var col = new Color(v * 0.92f, v * 0.95f, v, 0f);
                k.Box(0, MeshKit.At(new Vector3(p.x, bh * 0.5f - 0.1f, p.y), new Vector3(0f, yaw, 0f)), new Vector3(bw, bh, bd), 0f, col);
                if (rng.NextDouble() < 0.5)
                {
                    float th = 2f + (float)rng.NextDouble() * 6f;
                    var col2 = new Color(v * 0.85f, v * 0.88f, v * 0.94f, 0f);
                    k.Box(0, MeshKit.At(new Vector3(p.x, bh + th * 0.5f, p.y), new Vector3(0f, yaw, 0f)), new Vector3(bw * 0.4f, th, bd * 0.4f), 0f, col2);
                }
            }
        }
    }
}

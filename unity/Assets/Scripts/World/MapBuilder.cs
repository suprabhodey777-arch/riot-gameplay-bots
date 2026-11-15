// Builds the map from the MapDef the bridge sends: every wall polygon extruded
// to its height (ear-clipped caps, mesh colliders for the shot raycast) in a
// material that depends on its zone (weathered concrete, brick, painted plaster
// or painted steel, chosen per callout area), architectural trim on every face
// that looks onto the floor (plinths, cornices, string courses, pilasters,
// framed windows above head height, ribs on steel), door frames with lintels
// and stepped arches at the chokes, a stone flagstone floor with a collider,
// each site paved in its own material (steel deck plate, timber or polished
// tiles) with a painted border and big painted letters on the floor and on a
// wall, and callout labels. Trim and frames carry no colliders and stay above
// head height or within a few centimetres of a wall, so sight lines match the
// sim. All trim is one merged mesh, windows another.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public static class MapBuilder
    {
        /// <summary>Raycasts for shots and smokes use the Default layer; nothing else in the scene has colliders.</summary>
        public const int WorldLayer = 0;

        struct SurfaceArea
        {
            public List<Vec2> Poly;
            public StepSurface Surface;
        }

        static readonly List<SurfaceArea> surfaces = new List<SurfaceArea>();

        /// <summary>The footstep surface at a world position: the site paving there, else the stone floor.</summary>
        public static StepSurface StepSurfaceAt(Vector3 p)
        {
            var q = new Vector2(p.x, p.z);
            for (int i = 0; i < surfaces.Count; i++)
                if (Geo.PointInPolygon(q, surfaces[i].Poly)) return surfaces[i].Surface;
            return StepSurface.Concrete;
        }

        enum Zone { Concrete, Brick, Plaster, Steel }

        struct ZoneLook
        {
            public Color Side;
            public Textures.Pair Tex;
            public float Gloss, Bump;
        }

        static ZoneLook LookOf(Zone z)
        {
            switch (z)
            {
                case Zone.Brick: return new ZoneLook { Side = new Color(0.66f, 0.43f, 0.34f), Tex = Textures.Brick(), Gloss = 0.15f, Bump = 1f };
                case Zone.Plaster: return new ZoneLook { Side = new Color(0.86f, 0.8f, 0.69f), Tex = Textures.Concrete(), Gloss = 0.18f, Bump = 0.45f };
                case Zone.Steel: return new ZoneLook { Side = new Color(0.42f, 0.5f, 0.52f), Tex = Textures.PaintedMetal(), Gloss = 0.42f, Bump = 1f };
                default: return new ZoneLook { Side = new Color(0.76f, 0.74f, 0.7f), Tex = Textures.Concrete(), Gloss = 0.25f, Bump = 1f };
            }
        }

        /// <summary>The zone a wall belongs to: the callout area around its centre (else a 24 m grid cell), hashed with the map id.</summary>
        static Zone ZoneOf(WallDef wall, MapDef map)
        {
            Vector2 c = Geo.Centroid(wall.points);
            string key = null;
            float best = 30f;
            if (map.callouts != null)
            {
                foreach (CalloutDef co in map.callouts)
                {
                    if (co == null || co.polygon == null || co.polygon.Count < 3 || string.IsNullOrEmpty(co.name)) continue;
                    if (Geo.PointInPolygon(c, co.polygon)) { key = co.name; break; }
                    float d = (Geo.Centroid(co.polygon) - c).magnitude;
                    if (d < best) { best = d; key = co.name; }
                }
            }
            if (key == null) key = Mathf.FloorToInt(c.x / 24f) + "," + Mathf.FloorToInt(c.y / 24f);
            return (Zone)(StableHash((map.id ?? map.name ?? "") + "/" + key) % 4);
        }

        public static GameObject Build(MapDef map, Font font, Material textMat)
        {
            var root = new GameObject("Map " + (map.name ?? map.id));
            float w = map.bounds != null && map.bounds.width > 0 ? map.bounds.width : 100f;
            float h = map.bounds != null && map.bounds.height > 0 ? map.bounds.height : 100f;

            WorldLook.Prune();
            surfaces.Clear();
            BuildFloor(root.transform, w, h);

            Textures.Pair concrete = Textures.Concrete();
            Textures.Pair crate = Textures.Crate();
            Textures.Pair trimTex = Textures.Trim();
            var matCache = new Dictionary<string, Material>();
            Material Get(string key, Color c, Surface kind, Textures.Pair tex, float gloss, float bump)
            {
                if (!matCache.TryGetValue(key, out Material m))
                {
                    m = Mats.World(c, tex.Albedo, tex.Normal, bump, gloss, 0f, gloss, 1f);
                    WorldLook.Register(m, c, kind);
                    matCache[key] = m;
                }
                return m;
            }

            var walls = new GameObject("Walls").transform;
            walls.SetParent(root.transform, false);
            var contact = new MeshBuilder(1);
            var trim = new MeshKit(1) { UvScale = 0.5f };
            var glass = new MeshKit(1);
            if (map.walls == null) map.walls = new List<WallDef>();
            foreach (WallDef wall in map.walls)
            {
                if (wall == null || wall.points == null || wall.points.Count < 3) continue;
                bool outer = wall.id != null && wall.id.StartsWith("outer");
                Color side, top;
                Textures.Pair sideTex;
                float sideGloss, bump = 1f;
                Zone zone = outer ? Zone.Concrete : ZoneOf(wall, map);
                if (outer)
                {
                    side = Palette.Outer;
                    top = Palette.Outer;
                    sideTex = concrete;
                    sideGloss = 0.25f;
                }
                else if (wall.height <= 1.5f)
                {
                    side = Palette.Crate;
                    top = Palette.CrateTop;
                    sideTex = crate;
                    sideGloss = 0.2f;
                }
                else
                {
                    ZoneLook zl = LookOf(zone);
                    side = zl.Side;
                    top = Palette.WallTop;
                    sideTex = zl.Tex;
                    sideGloss = zl.Gloss;
                    bump = zl.Bump;
                }
                Color baseTrim = new Color(side.r * 0.6f, side.g * 0.6f, side.b * 0.62f);
                float height = Mathf.Max(0.05f, wall.height);
                Mesh mesh = Prism(wall.points, height, Mathf.Min(TrimHeight, height * 0.3f));
                mesh.name = "wall " + wall.id;
                var go = new GameObject("Wall " + wall.id) { layer = WorldLayer };
                go.transform.SetParent(walls, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                string tk = sideTex.Albedo != null ? sideTex.Albedo.name : "";
                mr.sharedMaterials = new[]
                {
                    Get("s" + side + tk, side, Surface.Wall, sideTex, sideGloss, bump),
                    Get("t" + top, top, Surface.Wall, concrete, 0.25f, 0.8f),
                    Get("r" + baseTrim, baseTrim, Surface.Trim, trimTex, 0.45f, 1f),
                };
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                ContactShadow(contact, wall.points, ContactWidth, 0.012f);
                if (height > 1.5f) WallTrim(trim, glass, wall, map, w, h, outer ? Zone.Concrete : zone, side);
            }
            Doorways(trim, map);
            // One draw call per wall material: static batching of every wall (the colliders stay per wall).
            StaticBatchingUtility.Combine(walls.gameObject);

            // The trim (stone and paint, by vertex colour) and the window glass, each one mesh.
            Material trimMat = Get("trim", Color.white, Surface.Trim, concrete, 0.22f, 0.6f);
            AddMesh(root.transform, "Architectural trim", trim, trimMat, true);
            Material glassMat = Mats.World(new Color(0.16f, 0.19f, 0.22f), Texture2D.whiteTexture, null, 0f, 0.88f, 0.2f, 0.9f, 0f);
            AddMesh(root.transform, "Window glass", glass, glassMat, false);

            // Soft darkening on the floor around every wall base: an ambient-occlusion feel in one mesh.
            var contactGo = new GameObject("Wall contact shadows");
            contactGo.transform.SetParent(root.transform, false);
            Mesh contactMesh = contact.ToMesh();
            contactMesh.RecalculateNormals();
            contactGo.AddComponent<MeshFilter>().sharedMesh = contactMesh;
            var cmr = contactGo.AddComponent<MeshRenderer>();
            Material cm = Mats.Transparent(new Color(0.05f, 0.05f, 0.08f, 0.42f), 2990);
            if (cm.HasProperty("_MainTex")) cm.mainTexture = Mats.GradientTexture();
            cmr.sharedMaterial = cm;
            cmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var deco = new GameObject("Sites and callouts").transform;
            deco.SetParent(root.transform, false);
            foreach (KeyValuePair<string, List<Vec2>> site in map.SiteList()) SiteArt(deco, site.Key, site.Value, map, font, textMat, w, h);
            if (map.callouts != null)
            {
                foreach (CalloutDef co in map.callouts)
                {
                    if (co == null || co.polygon == null || co.polygon.Count < 3 || string.IsNullOrEmpty(co.name)) continue;
                    Vector2 cen = FreeSpot(co.polygon, map, 1.2f);
                    Label(co.name.ToUpperInvariant(), new Vector3(cen.x, 0.03f, cen.y), 0.7f, new Color(0.2f, 0.21f, 0.24f, 0.5f), font, textMat, deco, true);
                }
            }
            if (map.spawns != null)
            {
                AddSpawnPads(deco, map.spawns.attack, Palette.Attack);
                AddSpawnPads(deco, map.spawns.defense, Palette.Defense);
            }
            return root;
        }

        static void AddMesh(Transform parent, string name, MeshKit k, Material m, bool shadows)
        {
            if (k.Empty) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = k.ToMesh(name);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }

        // ---- architecture ---------------------------------------------------------------------------
        static readonly Color StoneTrim = new Color(0.72f, 0.69f, 0.63f, 0f);
        static readonly Color DarkStone = new Color(0.42f, 0.4f, 0.38f, 0f);

        /// <summary>A box along a wall face: `along` from the edge start, `y` its centre height, `proud` metres out from the face.</summary>
        static void FaceBox(MeshKit k, Vector2 a, Vector2 dir, Vector2 n, float along, float length, float y, float height, float proud, Color c, float bevel = 0f)
        {
            Vector2 p = a + dir * along + n * (proud * 0.5f - 0.01f);
            float yaw = Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg;
            k.Box(0, MeshKit.At(new Vector3(p.x, y, p.y), new Vector3(0f, yaw, 0f)), new Vector3(length, height, proud + 0.02f), bevel, c);
        }

        /// <summary>Plinth, cornice, string course, pilasters or steel ribs, and framed windows on every face that looks onto the floor.</summary>
        static void WallTrim(MeshKit k, MeshKit glass, WallDef wall, MapDef map, float mapW, float mapH, Zone zone, Color side)
        {
            List<Vector2> p = ToCounterClockwise(wall.points);
            float height = wall.height;
            int seed = StableHash(wall.id ?? "");
            Color plinth = zone == Zone.Steel ? new Color(0.24f, 0.25f, 0.26f, 0.6f) : DarkStone;
            Color stone = zone == Zone.Steel ? new Color(side.r * 0.8f, side.g * 0.8f, side.b * 0.8f, 0.4f) : zone == Zone.Brick ? StoneTrim : new Color(side.r * 1.06f, side.g * 1.05f, side.b * 1.03f, 0f);
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % p.Count];
                Vector2 e = b - a;
                float len = e.magnitude;
                if (len < 0.6f) continue;
                Vector2 dir = e / len;
                var n = new Vector2(e.y, -e.x) / len;
                Vector2 front = (a + b) * 0.5f + n * 0.6f;
                if (front.x < 0.2f || front.y < 0.2f || front.x > mapW - 0.2f || front.y > mapH - 0.2f) continue;
                if (!IsClear(front, map, 0.2f)) continue;
                float mid = len * 0.5f;
                // Plinth with a chamfered cap.
                float ph = height >= 2.9f ? 0.34f : 0.22f;
                FaceBox(k, a, dir, n, mid, len + 0.07f, ph * 0.5f, ph, 0.035f, plinth, 0.008f);
                FaceBox(k, a, dir, n, mid, len + 0.09f, ph + 0.02f, 0.04f, 0.045f, plinth, 0.012f);
                if (height < 2.4f)
                {
                    // Half walls: a coping stone on top instead of a cornice.
                    FaceBox(k, a, dir, n, mid, len + 0.06f, height - 0.04f, 0.08f, 0.04f, stone, 0.01f);
                    continue;
                }
                bool cornice = height >= 2.9f;
                if (cornice)
                {
                    if (zone == Zone.Steel)
                    {
                        FaceBox(k, a, dir, n, mid, len + 0.06f, height - 0.06f, 0.12f, 0.04f, stone, 0.006f);
                    }
                    else
                    {
                        FaceBox(k, a, dir, n, mid, len + 0.14f, height - 0.08f, 0.16f, 0.07f, stone, 0.012f);
                        FaceBox(k, a, dir, n, mid, len + 0.08f, height - 0.2f, 0.07f, 0.04f, stone, 0.008f);
                    }
                }
                if (height >= 4.6f && zone != Zone.Steel) FaceBox(k, a, dir, n, mid, len + 0.06f, 3.1f, 0.08f, 0.035f, stone, 0.006f);
                float top = cornice ? height - 0.24f : height;
                // Vertical rhythm: steel gets ribs every metre; masonry gets pilasters in bays of about 4.5 m.
                var bays = new List<float>();
                if (zone == Zone.Steel)
                {
                    for (float t = 0.5f; t < len - 0.3f; t += 1f) FaceBox(k, a, dir, n, t, 0.06f, (ph + top) * 0.5f, top - ph, 0.03f, stone);
                    bays.Add(0f);
                    bays.Add(len);
                }
                else
                {
                    int count = len >= 6f ? Mathf.Max(1, Mathf.RoundToInt(len / 4.5f)) : 0;
                    bays.Add(0f);
                    for (int j = 1; j < count; j++)
                    {
                        float t = len * j / count;
                        bays.Add(t);
                        FaceBox(k, a, dir, n, t, 0.36f, (ph + top) * 0.5f, top - ph, 0.05f, stone, 0.006f);
                    }
                    bays.Add(len);
                }
                // Windows above head height, centred in each bay, on about two faces in three.
                bool windows = height >= 4.2f && len >= 3.5f && (seed + i) % 3 != 0;
                if (!windows) continue;
                const float sill = 2.55f;
                float wh = Mathf.Min(1.3f, top - 0.35f - sill);
                if (wh < 0.7f) continue;
                for (int j = 0; j + 1 < bays.Count; j++)
                {
                    float b0 = bays[j], b1 = bays[j + 1];
                    float bay = b1 - b0;
                    int per = zone == Zone.Steel ? Mathf.FloorToInt((bay - 0.6f) / 2.4f) : bay >= 3.4f ? 2 : bay >= 1.6f ? 1 : 0;
                    for (int q = 0; q < per; q++)
                    {
                        float c = b0 + bay * (q + 0.5f) / per;
                        Window(k, glass, a, dir, n, c, sill, 0.9f, wh, stone, zone);
                    }
                }
            }
        }

        static void Window(MeshKit k, MeshKit glass, Vector2 a, Vector2 dir, Vector2 n, float c, float sill, float ww, float wh, Color stone, Zone zone)
        {
            Color frame = zone == Zone.Steel ? new Color(0.16f, 0.17f, 0.18f, 0.7f) : new Color(0.2f, 0.19f, 0.18f, 0.2f);
            float yc = sill + wh * 0.5f;
            // The pane, a hair off the wall, then the frame, a mullion and a transom, the sill and the lintel.
            Vector2 gp = a + dir * c + n * 0.012f;
            Vector3 g = new Vector3(gp.x, yc, gp.y);
            Vector3 right = new Vector3(dir.x, 0f, dir.y) * (ww * 0.5f), up = Vector3.up * (wh * 0.5f);
            glass.Quad(0, g - right - up, g + right - up, g + right + up, g - right + up, new Vector3(n.x, 0f, n.y), Color.white);
            FaceBox(k, a, dir, n, c, ww + 0.1f, sill + wh + 0.025f, 0.05f, 0.05f, frame);
            FaceBox(k, a, dir, n, c, ww + 0.1f, sill - 0.025f, 0.05f, 0.05f, frame);
            FaceBox(k, a, dir, n, c - ww * 0.5f - 0.025f, 0.05f, yc, wh, 0.05f, frame);
            FaceBox(k, a, dir, n, c + ww * 0.5f + 0.025f, 0.05f, yc, wh, 0.05f, frame);
            FaceBox(k, a, dir, n, c, 0.035f, yc, wh, 0.035f, frame);
            FaceBox(k, a, dir, n, c, ww, sill + wh * 0.62f, 0.03f, 0.035f, frame);
            FaceBox(k, a, dir, n, c, ww + 0.24f, sill - 0.08f, 0.07f, 0.09f, stone, 0.01f);
            if (zone != Zone.Steel) FaceBox(k, a, dir, n, c, ww + 0.2f, sill + wh + 0.12f, 0.14f, 0.045f, stone, 0.008f);
        }

        /// <summary>
        /// Door frames at every choke between two walls: stone jambs at both sides, and where the walls are tall
        /// enough a lintel with a keystone and a stepped arch, all above 2.6 m.
        /// </summary>
        static void Doorways(MeshKit k, MapDef map)
        {
            foreach (PropBuilder.Choke c in PropBuilder.FindChokes(map))
            {
                if (c.MinHeight < 2.6f) continue;
                Vector2 dir = (c.B - c.A).normalized;
                var perp = new Vector2(-dir.y, dir.x);
                float yaw = Mathf.Atan2(perp.x, perp.y) * Mathf.Rad2Deg;
                bool lintel = c.MinHeight >= 3.3f;
                float jambTop = lintel ? 2.9f : Mathf.Min(c.MinHeight, 3f);
                Color jamb = StoneTrim;
                for (int s = 0; s < 2; s++)
                {
                    Vector2 at = s == 0 ? c.A - dir * 0.13f : c.B + dir * 0.13f;
                    k.Box(0, MeshKit.At(new Vector3(at.x, jambTop * 0.5f, at.y), new Vector3(0f, yaw, 0f)), new Vector3(0.32f, jambTop, 0.44f), 0.012f, jamb);
                    k.Box(0, MeshKit.At(new Vector3(at.x, 0.12f, at.y), new Vector3(0f, yaw, 0f)), new Vector3(0.38f, 0.24f, 0.5f), 0.01f, DarkStone);
                    if (!lintel)
                        k.Box(0, MeshKit.At(new Vector3(at.x, jambTop + 0.05f, at.y), new Vector3(0f, yaw, 0f)), new Vector3(0.4f, 0.1f, 0.52f), 0.012f, jamb);
                }
                if (!lintel) continue;
                Vector2 mid = (c.A + c.B) * 0.5f;
                float topY = Mathf.Min(c.MinHeight, 3.5f);
                float lh = topY - 2.9f;
                k.Box(0, MeshKit.At(new Vector3(mid.x, 2.9f + lh * 0.5f, mid.y), new Vector3(0f, yaw, 0f)), new Vector3(c.Width + 0.64f, lh, 0.44f), 0.012f, jamb);
                k.Box(0, MeshKit.At(new Vector3(mid.x, 2.9f + lh * 0.5f - 0.02f, mid.y), new Vector3(0f, yaw, 0f)), new Vector3(0.32f, lh + 0.08f, 0.5f), 0.01f, new Color(0.78f, 0.75f, 0.68f, 0f));
                // The stepped arch: three courses closing in from each jamb under the lintel.
                for (int s = 0; s < 2; s++)
                {
                    float sign = s == 0 ? 1f : -1f;
                    Vector2 from = s == 0 ? c.A : c.B;
                    for (int step = 0; step < 3; step++)
                    {
                        float reach = new[] { 0.55f, 0.34f, 0.16f }[step];
                        if (reach * 2f > c.Width - 1.2f) continue;
                        Vector2 at = from + dir * sign * (reach * 0.5f);
                        float y = 2.9f - 0.06f - step * 0.12f;
                        k.Box(0, MeshKit.At(new Vector3(at.x, y, at.y), new Vector3(0f, yaw, 0f)), new Vector3(reach, 0.12f, 0.42f), 0.006f, jamb);
                    }
                }
            }
        }

        // ---- sites ---------------------------------------------------------------------------------
        /// <summary>
        /// A site: paving in its own material (steel deck or timber, alternating per map; C polished tiles) that
        /// also sets the footstep sound, a painted border, the big painted letter on the floor, and the letter again
        /// painted large on the tallest wall face looking onto the site.
        /// </summary>
        static void SiteArt(Transform parent, string letter, List<Vec2> poly, MapDef map, Font font, Material textMat, float mapW, float mapH)
        {
            Color sc = Palette.Site(letter);
            Color paint = Color.Lerp(sc, new Color(0.93f, 0.9f, 0.84f), 0.4f);
            int baseline = StableHash(map.id ?? map.name ?? "") % 2;
            int pick = letter == "C" ? 2 : letter == "B" ? 1 - baseline : baseline;
            StepSurface surf = pick == 0 ? StepSurface.Metal : pick == 1 ? StepSurface.Wood : StepSurface.Concrete;
            Textures.Pair tex = surf == StepSurface.Metal ? Textures.DeckPlate() : surf == StepSurface.Wood ? Textures.Timber() : Textures.FloorTiles();
            Color fc = surf == StepSurface.Metal ? new Color(0.62f, 0.64f, 0.64f) : surf == StepSurface.Wood ? new Color(0.78f, 0.62f, 0.46f) : new Color(0.84f, 0.84f, 0.82f);
            Material fm = Mats.World(fc, tex.Albedo, tex.Normal, 1f, surf == StepSurface.Wood ? 0.2f : 0.45f, surf == StepSurface.Metal ? 0.65f : 0f, 0.55f, 0f);
            WorldLook.Register(fm, fc, Surface.Floor);
            var floor = new GameObject("Site " + letter + " paving");
            floor.transform.SetParent(parent, false);
            floor.AddComponent<MeshFilter>().sharedMesh = FlatPolygon(poly, 0.008f, surf == StepSurface.Concrete ? 4f : 2f);
            var fmr = floor.AddComponent<MeshRenderer>();
            fmr.sharedMaterial = fm;
            fmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            surfaces.Add(new SurfaceArea { Poly = poly, Surface = surf });

            var edge = new GameObject("Site " + letter + " border");
            edge.transform.SetParent(parent, false);
            edge.AddComponent<MeshFilter>().sharedMesh = EdgeStrip(poly, 0.16f, 0.014f);
            var emr = edge.AddComponent<MeshRenderer>();
            emr.sharedMaterial = Mats.Transparent(new Color(paint.r, paint.g, paint.b, 0.8f), 3002);
            emr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Vector2 cen = FreeSpot(poly, map, 2.5f);
            Label(letter, new Vector3(cen.x, 0.03f, cen.y), 6f, new Color(paint.r, paint.g, paint.b, 0.72f), font, textMat, parent, true);

            // The wall letter: the longest tall face whose front is on (or within 3 m of) the site.
            float bestLen = 0f;
            Vector2 wa = Vector2.zero, wn = Vector2.zero;
            float wTop = 0f;
            foreach (WallDef wall in map.walls)
            {
                if (wall == null || wall.points == null || wall.points.Count < 3 || wall.height < 3.2f) continue;
                List<Vector2> p = ToCounterClockwise(wall.points);
                for (int i = 0; i < p.Count; i++)
                {
                    Vector2 a = p[i], b = p[(i + 1) % p.Count];
                    Vector2 e = b - a;
                    float len = e.magnitude;
                    if (len < 2.6f || len <= bestLen) continue;
                    var n = new Vector2(e.y, -e.x) / len;
                    Vector2 front = (a + b) * 0.5f + n * 1.5f;
                    if (front.x < 0.2f || front.y < 0.2f || front.x > mapW - 0.2f || front.y > mapH - 0.2f) continue;
                    if (!IsClear(front, map, 0.5f)) continue;
                    if (!Geo.PointInPolygon(front, poly) && !Geo.PointInPolygon(front + n * 1.5f, poly)) continue;
                    bestLen = len;
                    wa = (a + b) * 0.5f;
                    wn = n;
                    wTop = wall.height;
                }
            }
            if (bestLen > 0f && font != null && textMat != null)
            {
                float size = Mathf.Min(2f, wTop - 1.9f);
                if (size >= 1f)
                {
                    Vector3 at = new Vector3(wa.x + wn.x * 0.04f, 1.65f + size * 0.5f, wa.y + wn.y * 0.04f);
                    GameObject l = Label(letter, at, size, new Color(paint.r, paint.g, paint.b, 0.85f), font, textMat, parent, false);
                    l.transform.rotation = Quaternion.LookRotation(new Vector3(-wn.x, 0f, -wn.y), Vector3.up);
                }
            }
        }

        /// <summary>Height of the darker trim strip at the base of every wall.</summary>
        public const float TrimHeight = 0.28f;
        public const float ContactWidth = 0.7f;

        /// <summary>FNV-1a of a string, non-negative and the same on every run (string.GetHashCode is not guaranteed).</summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (s != null)
                    foreach (char ch in s) h = (h ^ ch) * 16777619u;
                return (int)(h & 0x7fffffff);
            }
        }

        /// <summary>
        /// A floor strip `width` wide outside each edge of a wall footprint (with round fans at convex corners), UV v
        /// from 0 at the wall to 1 at the outer edge, for a fading gradient texture.
        /// </summary>
        static void ContactShadow(MeshBuilder b, List<Vec2> pts, float width, float y)
        {
            List<Vector2> p = ToCounterClockwise(pts);
            int n = p.Count;
            if (n < 3) return;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = p[i];
                Vector2 c = p[(i + 1) % n];
                Vector2 e = c - a;
                if (e.sqrMagnitude < 1e-8f) continue;
                // Outward normal of a counter-clockwise edge is (e.y, -e.x).
                Vector2 o = new Vector2(e.y, -e.x).normalized * width;
                int v0 = b.Add(new Vector3(a.x, y, a.y), new Vector2(0.5f, 0f));
                int v1 = b.Add(new Vector3(c.x, y, c.y), new Vector2(0.5f, 0f));
                int v2 = b.Add(new Vector3(c.x + o.x, y, c.y + o.y), new Vector2(0.5f, 1f));
                int v3 = b.Add(new Vector3(a.x + o.x, y, a.y + o.y), new Vector2(0.5f, 1f));
                b.Tri(0, v0, v1, v2, Vector3.up);
                b.Tri(0, v0, v2, v3, Vector3.up);
            }
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = p[(i - 1 + n) % n], cur = p[i], next = p[(i + 1) % n];
                Vector2 e0 = cur - prev, e1 = next - cur;
                if (e0.sqrMagnitude < 1e-8f || e1.sqrMagnitude < 1e-8f) continue;
                if (Cross(e0, e1) <= 0f) continue; // concave corner: the strips already overlap there
                float a0 = Mathf.Atan2(-e0.x, e0.y);
                float a1 = Mathf.Atan2(-e1.x, e1.y);
                float da = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                const int seg = 4;
                int ctr = b.Add(new Vector3(cur.x, y, cur.y), new Vector2(0.5f, 0f));
                for (int s = 0; s < seg; s++)
                {
                    float t0 = a0 + da * s / seg, t1 = a0 + da * (s + 1) / seg;
                    int q0 = b.Add(new Vector3(cur.x + Mathf.Cos(t0) * width, y, cur.y + Mathf.Sin(t0) * width), new Vector2(0.5f, 1f));
                    int q1 = b.Add(new Vector3(cur.x + Mathf.Cos(t1) * width, y, cur.y + Mathf.Sin(t1) * width), new Vector2(0.5f, 1f));
                    b.Tri(0, ctr, q0, q1, Vector3.up);
                }
            }
        }

        /// <summary>
        /// A point for a floor label: the polygon's centroid when it is on open floor, otherwise the open point inside the
        /// polygon nearest to it, at least `clearance` metres from any wall (any map's walls, from the MapDef alone).
        /// </summary>
        static Vector2 FreeSpot(List<Vec2> poly, MapDef map, float clearance)
        {
            return FindFreeSpot(poly, map, clearance);
        }

        /// <summary>Public form of FreeSpot (PropBuilder places the plant-zone stencil with it).</summary>
        public static Vector2 FindFreeSpot(List<Vec2> poly, MapDef map, float clearance)
        {
            Vector2 cen = Geo.Centroid(poly);
            if (Clear(cen, map, clearance)) return cen;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (Vec2 p in poly)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            float step = Mathf.Max(0.5f, Mathf.Max(maxX - minX, maxY - minY) / 60f);
            for (float c = clearance; c >= 0f; c -= clearance * 0.5f)
            {
                Vector2 best = cen;
                float bestD = float.MaxValue;
                for (float y = minY + step * 0.5f; y < maxY; y += step)
                {
                    for (float x = minX + step * 0.5f; x < maxX; x += step)
                    {
                        var q = new Vector2(x, y);
                        float d = (q - cen).sqrMagnitude;
                        if (d >= bestD || !Geo.PointInPolygon(q, poly) || !Clear(q, map, c)) continue;
                        best = q;
                        bestD = d;
                    }
                }
                if (bestD < float.MaxValue) return best;
            }
            return cen;
        }

        static bool Clear(Vector2 p, MapDef map, float clearance)
        {
            return IsClear(p, map, clearance);
        }

        /// <summary>Is `p` on open floor at least `clearance` metres from every wall?</summary>
        public static bool IsClear(Vector2 p, MapDef map, float clearance)
        {
            if (map.walls == null) return true;
            foreach (WallDef w in map.walls)
            {
                if (w == null || w.points == null || w.points.Count < 3) continue;
                if (Geo.PointInPolygon(p, w.points)) return false;
                if (clearance <= 0f) continue;
                for (int i = 0, j = w.points.Count - 1; i < w.points.Count; j = i++)
                {
                    var a = new Vector2(w.points[j].x, w.points[j].y);
                    var b = new Vector2(w.points[i].x, w.points[i].y);
                    Vector2 ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    if ((a + ab * t - p).sqrMagnitude < clearance * clearance) return false;
                }
            }
            return true;
        }

        static void BuildFloor(Transform root, float w, float h)
        {
            var floor = new GameObject("Floor") { layer = WorldLayer };
            floor.transform.SetParent(root, false);
            var pts = new List<Vec2>
            {
                new Vec2 { x = 0, y = 0 }, new Vec2 { x = w, y = 0 }, new Vec2 { x = w, y = h }, new Vec2 { x = 0, y = h },
            };
            Mesh m = FlatPolygon(pts, 0f, 4f);
            floor.AddComponent<MeshFilter>().sharedMesh = m;
            Textures.Pair tiles = Textures.Flagstones();
            Material fm = Mats.World(Palette.Floor, tiles.Albedo, tiles.Normal, 1f, 0.45f, 0f, 0.45f, 0f);
            WorldLook.Register(fm, Palette.Floor, Surface.Floor);
            floor.AddComponent<MeshRenderer>().sharedMaterial = fm;
            var box = floor.AddComponent<BoxCollider>();
            box.center = new Vector3(w / 2f, -0.05f, h / 2f);
            box.size = new Vector3(w, 0.1f, h);

            // Ground beyond the walls, so the horizon is not empty; no collider.
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root, false);
            var gpts = new List<Vec2>
            {
                new Vec2 { x = -300, y = -300 }, new Vec2 { x = w + 300, y = -300 }, new Vec2 { x = w + 300, y = h + 300 }, new Vec2 { x = -300, y = h + 300 },
            };
            ground.AddComponent<MeshFilter>().sharedMesh = FlatPolygon(gpts, -0.04f, 6f);
            // Its own material (not the shared Lit cache), so the palette tint does not leak onto bots.
            Textures.Pair gt = Textures.Ground();
            Material gm = Mats.World(Palette.Ground, gt.Albedo, gt.Normal, 0.8f, 0.12f, 0f, 0.12f, 0f);
            WorldLook.Register(gm, Palette.Ground, Surface.Ground);
            ground.AddComponent<MeshRenderer>().sharedMaterial = gm;
        }

        static void AddSpawnPads(Transform parent, List<Vec2> spawns, Color c)
        {
            if (spawns == null) return;
            Material mat = Mats.Transparent(new Color(c.r, c.g, c.b, 0.22f), 3000);
            foreach (Vec2 s in spawns)
            {
                var pts = new List<Vec2>();
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI * 2f / 12f;
                    pts.Add(new Vec2 { x = s.x + Mathf.Cos(a) * 0.6f, y = s.y + Mathf.Sin(a) * 0.6f });
                }
                var go = new GameObject("Spawn pad");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = FlatPolygon(pts, 0.014f);
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        /// <summary>A flat TextMesh lying on the floor, its top pointing east (+x), the way attackers walk.</summary>
        public static GameObject Label(string text, Vector3 pos, float heightM, Color c, Font font, Material mat, Transform parent, bool flat)
        {
            var go = new GameObject("Label " + text);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            if (flat) go.transform.rotation = Quaternion.Euler(90f, 90f, 0f);
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = 96;
            tm.characterSize = heightM * 10f / tm.fontSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = c;
            tm.text = text;
            mr.sharedMaterial = mat;
            return go;
        }

        // ---- meshes -------------------------------------------------------------------

        class MeshBuilder
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<int>[] Tris;

            public MeshBuilder(int submeshes)
            {
                Tris = new List<int>[submeshes];
                for (int i = 0; i < submeshes; i++) Tris[i] = new List<int>();
            }

            public int Add(Vector3 v, Vector2 uv)
            {
                return Add(v, uv, new Color(1f, 1f, 1f, 0f));
            }

            /// <summary>A vertex with a colour (RGB: baked occlusion/tint; A: WorldLit's metal mask, 0 here).</summary>
            public int Add(Vector3 v, Vector2 uv, Color c)
            {
                Verts.Add(v);
                Uvs.Add(uv);
                Colors.Add(c);
                return Verts.Count - 1;
            }

            /// <summary>Adds a triangle whose front face points along `normal` (Unity is clockwise-front).</summary>
            public void Tri(int sub, int a, int b, int c, Vector3 normal)
            {
                Vector3 n = Vector3.Cross(Verts[b] - Verts[a], Verts[c] - Verts[a]);
                if (Vector3.Dot(n, normal) >= 0f) { Tris[sub].Add(a); Tris[sub].Add(b); Tris[sub].Add(c); }
                else { Tris[sub].Add(a); Tris[sub].Add(c); Tris[sub].Add(b); }
            }

            public Mesh ToMesh()
            {
                var m = new Mesh();
                if (Verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(Verts);
                m.SetUVs(0, Uvs);
                m.SetColors(Colors);
                m.subMeshCount = Tris.Length;
                for (int i = 0; i < Tris.Length; i++) m.SetTriangles(Tris[i], i);
                m.RecalculateNormals();
                // Tangents for the procedural normal maps (Saltworks/WorldLit).
                m.RecalculateTangents();
                m.RecalculateBounds();
                return m;
            }
        }

        public static List<Vector2> ToCounterClockwise(List<Vec2> pts)
        {
            var p = new List<Vector2>(pts.Count);
            foreach (Vec2 v in pts)
            {
                var q = new Vector2(v.x, v.y);
                if (p.Count == 0 || (p[p.Count - 1] - q).sqrMagnitude > 1e-8f) p.Add(q);
            }
            if (p.Count > 1 && (p[0] - p[p.Count - 1]).sqrMagnitude <= 1e-8f) p.RemoveAt(p.Count - 1);
            if (SignedArea(p) < 0f) p.Reverse();
            return p;
        }

        static float SignedArea(List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) a += p[j].x * p[i].y - p[i].x * p[j].y;
            return a * 0.5f;
        }

        static float Cross(Vector2 u, Vector2 v)
        {
            return u.x * v.y - u.y * v.x;
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            return Cross(b - a, p - a) >= 0f && Cross(c - b, p - b) >= 0f && Cross(a - c, p - c) >= 0f;
        }

        /// <summary>Ear clipping for a simple counter-clockwise polygon. Returns index triples.</summary>
        public static List<int> Triangulate(List<Vector2> p)
        {
            var tris = new List<int>();
            int n = p.Count;
            if (n < 3) return tris;
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                int c = idx.Count;
                for (int i = 0; i < c; i++)
                {
                    int ia = idx[(i - 1 + c) % c], ib = idx[i], ic = idx[(i + 1) % c];
                    Vector2 a = p[ia], b = p[ib], cc = p[ic];
                    if (Cross(b - a, cc - b) <= 1e-7f) continue; // reflex or collinear corner
                    bool blocked = false;
                    for (int k = 0; k < c && !blocked; k++)
                    {
                        int j = idx[k];
                        if (j == ia || j == ib || j == ic) continue;
                        Vector2 q = p[j];
                        if ((q - a).sqrMagnitude < 1e-8f || (q - b).sqrMagnitude < 1e-8f || (q - cc).sqrMagnitude < 1e-8f) continue;
                        blocked = InTriangle(q, a, b, cc);
                    }
                    if (blocked) continue;
                    tris.Add(ia); tris.Add(ib); tris.Add(ic);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    // Degenerate input (self-touching or all collinear): fan the rest so nothing is lost.
                    for (int k = 1; k < idx.Count - 1; k++) { tris.Add(idx[0]); tris.Add(idx[k]); tris.Add(idx[k + 1]); }
                    idx.Clear();
                }
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            return tris;
        }

        /// <summary>
        /// Wall prism: submesh 0 = sides, 1 = top cap, 2 = the darker base trim (the bottom `trimH` metres of the sides).
        /// Flat-shaded, UVs in 2 m units. Vertex colours bake a soft occlusion gradient: darker at the foot of the wall,
        /// full brightness from about 1.3 m up.
        /// </summary>
        public static Mesh Prism(List<Vec2> pts, float height, float trimH = 0f)
        {
            List<Vector2> p = ToCounterClockwise(pts);
            var b = new MeshBuilder(3);
            const float uvScale = 0.5f;
            const float aoHeight = 1.3f;
            trimH = Mathf.Clamp(trimH, 0f, height * 0.5f);
            Color Ao(float y)
            {
                float k = Mathf.Clamp01(y / aoHeight);
                k = 1f - (1f - k) * (1f - k);
                float v = Mathf.Lerp(0.72f, 1f, k);
                return new Color(v, v, v, 0f);
            }

            List<int> cap = Triangulate(p);
            int baseTop = b.Verts.Count;
            foreach (Vector2 v in p) b.Add(new Vector3(v.x, height, v.y), new Vector2(v.x, v.y) * uvScale, new Color(1f, 1f, 1f, 0f));
            for (int i = 0; i < cap.Count; i += 3) b.Tri(1, baseTop + cap[i], baseTop + cap[i + 1], baseTop + cap[i + 2], Vector3.up);

            float run = 0f;
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 a2 = p[i];
                Vector2 c2 = p[(i + 1) % p.Count];
                Vector2 e = c2 - a2;
                float len = e.magnitude;
                if (len < 1e-5f) continue;
                // Outward normal of a counter-clockwise edge (sim coords) is (e.y, -e.x).
                var normal = new Vector3(e.y, 0f, -e.x).normalized;
                float y0 = trimH;
                if (trimH > 0f)
                {
                    int t0 = b.Add(new Vector3(a2.x, 0f, a2.y), new Vector2(run, 0f) * uvScale, Ao(0f));
                    int t1 = b.Add(new Vector3(c2.x, 0f, c2.y), new Vector2(run + len, 0f) * uvScale, Ao(0f));
                    int t2 = b.Add(new Vector3(c2.x, trimH, c2.y), new Vector2(run + len, trimH) * uvScale, Ao(trimH));
                    int t3 = b.Add(new Vector3(a2.x, trimH, a2.y), new Vector2(run, trimH) * uvScale, Ao(trimH));
                    b.Tri(2, t0, t1, t2, normal);
                    b.Tri(2, t0, t2, t3, normal);
                }
                // Two rows: the occlusion gradient up to aoHeight, then plain wall to the top.
                float mid = Mathf.Clamp(aoHeight, y0, height);
                float[] ys = mid > y0 + 1e-3f && mid < height - 1e-3f ? new[] { y0, mid, height } : new[] { y0, height };
                for (int r = 0; r + 1 < ys.Length; r++)
                {
                    float ya = ys[r], yb = ys[r + 1];
                    int v0 = b.Add(new Vector3(a2.x, ya, a2.y), new Vector2(run, ya) * uvScale, Ao(ya));
                    int v1 = b.Add(new Vector3(c2.x, ya, c2.y), new Vector2(run + len, ya) * uvScale, Ao(ya));
                    int v2 = b.Add(new Vector3(c2.x, yb, c2.y), new Vector2(run + len, yb) * uvScale, Ao(yb));
                    int v3 = b.Add(new Vector3(a2.x, yb, a2.y), new Vector2(run, yb) * uvScale, Ao(yb));
                    b.Tri(0, v0, v1, v2, normal);
                    b.Tri(0, v0, v2, v3, normal);
                }
                run += len;
            }
            return b.ToMesh();
        }

        /// <summary>A flat polygon facing up at height y. UVs are metres / uvMetres.</summary>
        public static Mesh FlatPolygon(List<Vec2> pts, float y, float uvMetres = 1f)
        {
            List<Vector2> p = ToCounterClockwise(pts);
            var b = new MeshBuilder(1);
            foreach (Vector2 v in p) b.Add(new Vector3(v.x, y, v.y), v / uvMetres);
            List<int> tris = Triangulate(p);
            for (int i = 0; i < tris.Count; i += 3) b.Tri(0, tris[i], tris[i + 1], tris[i + 2], Vector3.up);
            return b.ToMesh();
        }

        /// <summary>An outline: one inset quad along each edge of the polygon.</summary>
        public static Mesh EdgeStrip(List<Vec2> pts, float width, float y)
        {
            List<Vector2> p = ToCounterClockwise(pts);
            var b = new MeshBuilder(1);
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 a = p[i];
                Vector2 c = p[(i + 1) % p.Count];
                Vector2 e = c - a;
                if (e.sqrMagnitude < 1e-8f) continue;
                // Inward normal of a counter-clockwise edge is (-e.y, e.x).
                Vector2 inward = new Vector2(-e.y, e.x).normalized * width;
                int v0 = b.Add(new Vector3(a.x, y, a.y), Vector2.zero);
                int v1 = b.Add(new Vector3(c.x, y, c.y), Vector2.right);
                int v2 = b.Add(new Vector3(c.x + inward.x, y, c.y + inward.y), Vector2.one);
                int v3 = b.Add(new Vector3(a.x + inward.x, y, a.y + inward.y), Vector2.up);
                b.Tri(0, v0, v1, v2, Vector3.up);
                b.Tri(0, v0, v2, v3, Vector3.up);
            }
            return b.ToMesh();
        }
    }
}

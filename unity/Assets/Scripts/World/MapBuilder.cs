// Builds the map from the MapDef the bridge sends: every wall polygon extruded
// to its height (ear-clipped caps, mesh colliders for the shot raycast), a
// floor with a collider, tinted sites with a big letter, and callout labels.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public static class MapBuilder
    {
        /// <summary>Raycasts for shots and smokes use the Default layer; nothing else in the scene has colliders.</summary>
        public const int WorldLayer = 0;

        public static GameObject Build(MapDef map, Font font, Material textMat)
        {
            var root = new GameObject("Map " + (map.name ?? map.id));
            float w = map.bounds != null && map.bounds.width > 0 ? map.bounds.width : 100f;
            float h = map.bounds != null && map.bounds.height > 0 ? map.bounds.height : 100f;

            BuildFloor(root.transform, w, h);

            Texture2D wallTex = Mats.WallTexture();
            var matCache = new Dictionary<string, Material>();
            Material Get(string key, Color c)
            {
                if (!matCache.TryGetValue(key, out Material m))
                {
                    m = Mats.LitTextured(c, wallTex, 0.08f);
                    matCache[key] = m;
                }
                return m;
            }

            var walls = new GameObject("Walls").transform;
            walls.SetParent(root.transform, false);
            foreach (WallDef wall in map.walls)
            {
                if (wall.points == null || wall.points.Count < 3) continue;
                bool outer = wall.id != null && wall.id.StartsWith("outer");
                Color side, top;
                if (outer) { side = Palette.Outer; top = Palette.Outer; }
                else if (wall.height <= 1.5f) { side = Palette.Crate; top = Palette.CrateTop; }
                else if (wall.height <= 3.2f) { side = Palette.Pillar; top = Palette.WallTop; }
                else { side = Palette.Wall; top = Palette.WallTop; }
                Mesh mesh = Prism(wall.points, Mathf.Max(0.05f, wall.height));
                mesh.name = "wall " + wall.id;
                var go = new GameObject("Wall " + wall.id) { layer = WorldLayer };
                go.transform.SetParent(walls, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { Get("s" + side, side), Get("t" + top, top) };
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }

            var deco = new GameObject("Sites and callouts").transform;
            deco.SetParent(root.transform, false);
            foreach (KeyValuePair<string, List<Vec2>> site in map.SiteList())
            {
                Color c = Palette.Site(site.Key);
                var tint = new GameObject("Site " + site.Key);
                tint.transform.SetParent(deco, false);
                tint.AddComponent<MeshFilter>().sharedMesh = FlatPolygon(site.Value, 0.012f);
                tint.AddComponent<MeshRenderer>().sharedMaterial = Mats.Transparent(new Color(c.r, c.g, c.b, 0.30f), 3000);

                var edge = new GameObject("Site " + site.Key + " edge");
                edge.transform.SetParent(deco, false);
                edge.AddComponent<MeshFilter>().sharedMesh = EdgeStrip(site.Value, 0.25f, 0.016f);
                edge.AddComponent<MeshRenderer>().sharedMaterial = Mats.Transparent(new Color(c.r * 0.9f, c.g * 0.9f, c.b * 0.9f, 0.85f), 3001);

                Vector2 cen = Geo.Centroid(site.Value);
                Label(site.Key, new Vector3(cen.x, 0.03f, cen.y), 6f, new Color(c.r * 0.85f, c.g * 0.85f, c.b * 0.85f, 0.9f), font, textMat, deco, true);
            }
            if (map.callouts != null)
            {
                foreach (CalloutDef co in map.callouts)
                {
                    if (co.polygon == null || co.polygon.Count < 3 || string.IsNullOrEmpty(co.name)) continue;
                    Vector2 cen = Geo.Centroid(co.polygon);
                    Label(co.name.ToUpperInvariant(), new Vector3(cen.x, 0.03f, cen.y), 0.7f, new Color(0.25f, 0.27f, 0.32f, 0.55f), font, textMat, deco, true);
                }
            }
            if (map.spawns != null)
            {
                AddSpawnPads(deco, map.spawns.attack, Palette.Attack);
                AddSpawnPads(deco, map.spawns.defense, Palette.Defense);
            }
            return root;
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
            floor.AddComponent<MeshRenderer>().sharedMaterial = Mats.LitTextured(Palette.Floor, Mats.FloorTexture(), 0.18f);
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
            ground.AddComponent<MeshFilter>().sharedMesh = FlatPolygon(gpts, -0.04f);
            ground.AddComponent<MeshRenderer>().sharedMaterial = Mats.Lit(Palette.Ground);
        }

        static void AddSpawnPads(Transform parent, List<Vec2> spawns, Color c)
        {
            if (spawns == null) return;
            Material mat = Mats.Transparent(new Color(c.r, c.g, c.b, 0.35f), 3000);
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
            public readonly List<int>[] Tris;

            public MeshBuilder(int submeshes)
            {
                Tris = new List<int>[submeshes];
                for (int i = 0; i < submeshes; i++) Tris[i] = new List<int>();
            }

            public int Add(Vector3 v, Vector2 uv)
            {
                Verts.Add(v);
                Uvs.Add(uv);
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
                m.subMeshCount = Tris.Length;
                for (int i = 0; i < Tris.Length; i++) m.SetTriangles(Tris[i], i);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }

        static List<Vector2> CounterClockwise(List<Vec2> pts)
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

        /// <summary>Wall prism: submesh 0 = sides, submesh 1 = top cap. Flat-shaded, UVs in 2 m units.</summary>
        public static Mesh Prism(List<Vec2> pts, float height)
        {
            List<Vector2> p = CounterClockwise(pts);
            var b = new MeshBuilder(2);
            const float uvScale = 0.5f;

            List<int> cap = Triangulate(p);
            int baseTop = b.Verts.Count;
            foreach (Vector2 v in p) b.Add(new Vector3(v.x, height, v.y), new Vector2(v.x, v.y) * uvScale);
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
                int v0 = b.Add(new Vector3(a2.x, 0f, a2.y), new Vector2(run, 0f) * uvScale);
                int v1 = b.Add(new Vector3(c2.x, 0f, c2.y), new Vector2(run + len, 0f) * uvScale);
                int v2 = b.Add(new Vector3(c2.x, height, c2.y), new Vector2(run + len, height) * uvScale);
                int v3 = b.Add(new Vector3(a2.x, height, a2.y), new Vector2(run, height) * uvScale);
                b.Tri(0, v0, v1, v2, normal);
                b.Tri(0, v0, v2, v3, normal);
                run += len;
            }
            return b.ToMesh();
        }

        /// <summary>A flat polygon facing up at height y. UVs are metres / uvMetres.</summary>
        public static Mesh FlatPolygon(List<Vec2> pts, float y, float uvMetres = 1f)
        {
            List<Vector2> p = CounterClockwise(pts);
            var b = new MeshBuilder(1);
            foreach (Vector2 v in p) b.Add(new Vector3(v.x, y, v.y), v / uvMetres);
            List<int> tris = Triangulate(p);
            for (int i = 0; i < tris.Count; i += 3) b.Tri(0, tris[i], tris[i + 1], tris[i + 2], Vector3.up);
            return b.ToMesh();
        }

        /// <summary>An outline: one inset quad along each edge of the polygon.</summary>
        public static Mesh EdgeStrip(List<Vec2> pts, float width, float y)
        {
            List<Vector2> p = CounterClockwise(pts);
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

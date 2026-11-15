// Reads the map's callouts as places: an area is an interior (a building with a roof) when more than
// 60% of its boundary runs along walls 3 m or taller, otherwise it is outside. Also finds the door
// gaps between facing walls of height 3 or more, and one landmark plot per large exterior area, on the
// wall top beside it. Everything here is analysis of the MapDef alone, shared by the map and prop
// builders, so the ceiling, the paving, the door heads and the landmarks all agree. Visual only:
// nothing in here is a collider and nothing changes where the sim thinks a wall is.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public static class MapZones
    {
        /// <summary>A callout read as a place: its footprint, size, and whether it is roofed.</summary>
        public class Area
        {
            public string Name;
            public List<Vec2> Poly;
            public List<Vector2> Edge;
            public Vector2 Centre;
            public float Size;
            public float Cover;
            public float Roof;
            public bool Interior;
            public int Index;
        }

        /// <summary>A doorway: the two wall faces either side of it, how wide it is and the lower of their heights.</summary>
        public struct DoorGap
        {
            public Vector2 A, B;
            public float Width;
            public float Height;
        }

        /// <summary>Where a landmark stands: on the wall top, its local +z pointing out into the area, and which kind.</summary>
        public struct LandmarkSite
        {
            public string Name;
            public Vector2 At;
            public float Base;
            public float Yaw;
            public int Kind;
            public float Size;
        }

        static readonly List<Area> noAreas = new List<Area>();
        static readonly List<DoorGap> noDoors = new List<DoorGap>();
        static readonly List<LandmarkSite> noSites = new List<LandmarkSite>();

        static List<Area> areas;
        static MapDef areasMap;
        static List<DoorGap> doors;
        static MapDef doorsMap;
        static List<LandmarkSite> sites;
        static MapDef sitesMap;

        /// <summary>
        /// Bigger than this a callout is a yard or a lane rather than a room (real buildings do not span it), so it
        /// keeps the sky even when walls close it in on most sides.
        /// </summary>
        public const float MaxInteriorSize = 1100f;

        /// <summary>The map's callouts classified as interiors or exteriors (cached per map).</summary>
        public static List<Area> Areas(MapDef map)
        {
            if (map == null) return noAreas;
            if (areas != null && ReferenceEquals(areasMap, map)) return areas;
            var list = new List<Area>();
            if (map.callouts != null)
            {
                for (int i = 0; i < map.callouts.Count; i++)
                {
                    CalloutDef co = map.callouts[i];
                    if (co == null || co.polygon == null || co.polygon.Count < 3 || string.IsNullOrEmpty(co.name)) continue;
                    List<Vector2> edge = MapBuilder.ToCounterClockwise(co.polygon);
                    if (edge.Count < 3) continue;
                    var a = new Area
                    {
                        Name = co.name,
                        Poly = co.polygon,
                        Edge = edge,
                        Centre = Geo.Centroid(co.polygon),
                        Size = PolygonArea(edge),
                        Index = i,
                    };
                    a.Cover = BoundaryCover(map, edge, out float roof);
                    a.Interior = a.Cover > 0.6f && a.Size >= 6f && a.Size <= MaxInteriorSize;
                    a.Roof = a.Interior ? Mathf.Clamp(roof, 3.2f, 4f) : 0f;
                    list.Add(a);
                }
            }
            areas = list;
            areasMap = map;
            return list;
        }

        /// <summary>The area whose footprint contains `p`, or null.</summary>
        public static Area At(MapDef map, Vector2 p)
        {
            foreach (Area a in Areas(map))
                if (Geo.PointInPolygon(p, a.Poly)) return a;
            return null;
        }

        /// <summary>Is any face of this wall footprint looking into an interior? Reports the ceiling height there.</summary>
        public static bool EnclosesInterior(MapDef map, List<Vector2> edge, out float roof)
        {
            roof = 0f;
            for (int i = 0; i < edge.Count; i++)
            {
                Vector2 a = edge[i], b = edge[(i + 1) % edge.Count];
                Vector2 e = b - a;
                float len = e.magnitude;
                if (len < 0.5f) continue;
                var n = new Vector2(e.y, -e.x) / len;
                Area face = At(map, (a + b) * 0.5f + n * 0.6f);
                if (face != null && face.Interior) roof = Mathf.Max(roof, face.Roof);
            }
            return roof > 0f;
        }

        /// <summary>
        /// Every doorway on the map: the narrow openings between facing walls found by the prop builder, plus the
        /// straight gaps of 2.5 to 8 m those missed. Cached per map.
        /// </summary>
        public static List<DoorGap> Doors(MapDef map)
        {
            if (map == null) return noDoors;
            if (doors != null && ReferenceEquals(doorsMap, map)) return doors;
            var list = new List<DoorGap>();
            foreach (PropBuilder.Choke c in PropBuilder.FindChokes(map))
                if (c.MinHeight >= 2.6f) list.Add(new DoorGap { A = c.A, B = c.B, Width = c.Width, Height = c.MinHeight });
            foreach (DoorGap g in Straight(map, 2.5f, 8f))
            {
                Vector2 mid = (g.A + g.B) * 0.5f;
                bool known = false;
                foreach (DoorGap d in list)
                {
                    Vector2 other = (d.A + d.B) * 0.5f;
                    if ((other - mid).sqrMagnitude < 2.25f) { known = true; break; }
                }
                if (!known) list.Add(g);
            }
            list.Sort((x, y) => x.Width.CompareTo(y.Width));
            doors = list;
            doorsMap = map;
            return list;
        }

        /// <summary>
        /// One landmark per exterior area over 150 m²: the longest tall wall face that looks into it, from which the
        /// landmark is mounted on the wall top. Areas already dressed keep 12 m between them. Cached per map.
        /// </summary>
        public static List<LandmarkSite> Landmarks(MapDef map)
        {
            if (map == null || map.walls == null) return noSites;
            if (sites != null && ReferenceEquals(sitesMap, map)) return sites;
            var list = new List<LandmarkSite>();
            int seed = MapBuilder.StableHash((map.id ?? map.name ?? "") + "/landmark");
            foreach (Area a in Areas(map))
            {
                if (a.Interior || a.Size < 150f) continue;
                float bestLen = 0f, bestBase = 0f;
                Vector2 bestAt = Vector2.zero, bestN = Vector2.zero;
                foreach (WallDef wall in map.walls)
                {
                    if (wall == null || wall.points == null || wall.points.Count < 3 || wall.height < 3.5f) continue;
                    List<Vector2> p = MapBuilder.ToCounterClockwise(wall.points);
                    for (int i = 0; i < p.Count; i++)
                    {
                        Vector2 e = p[(i + 1) % p.Count] - p[i];
                        float len = e.magnitude;
                        if (len < 4f || len <= bestLen) continue;
                        Vector2 mid = (p[i] + p[(i + 1) % p.Count]) * 0.5f;
                        var n = new Vector2(e.y, -e.x) / len;
                        Vector2 front = mid + n * 0.8f;
                        if (!Geo.PointInPolygon(front, a.Poly)) continue;
                        if (!MapBuilder.IsClear(front, map, 0.4f)) continue;
                        Vector2 at = mid - n * 0.55f;
                        bool clash = false;
                        foreach (LandmarkSite s in list)
                            if ((s.At - at).sqrMagnitude < 144f) { clash = true; break; }
                        if (clash) continue;
                        bestLen = len;
                        bestAt = at;
                        bestN = n;
                        bestBase = wall.height;
                    }
                }
                if (bestLen <= 0f) continue;
                list.Add(new LandmarkSite
                {
                    Name = a.Name,
                    At = bestAt,
                    Base = bestBase,
                    Yaw = Mathf.Atan2(bestN.x, bestN.y) * Mathf.Rad2Deg,
                    Kind = (seed + list.Count) % 4,
                    Size = a.Size,
                });
            }
            sites = list;
            sitesMap = map;
            return list;
        }

        /// <summary>The fraction of the polygon's boundary that runs along walls 3 m or taller, and the tallest of them.</summary>
        public static float BoundaryCover(MapDef map, List<Vector2> edge, out float roof)
        {
            roof = 0f;
            if (map == null || map.walls == null) return 0f;
            float total = 0f, covered = 0f;
            for (int i = 0; i < edge.Count; i++)
            {
                Vector2 a = edge[i], b = edge[(i + 1) % edge.Count];
                float len = (b - a).magnitude;
                if (len < 1e-4f) continue;
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.4f));
                float w = len / steps;
                for (int s = 0; s < steps; s++)
                {
                    total += w;
                    if (!TallAt(map, Vector2.Lerp(a, b, (s + 0.5f) / steps), 0.35f, out float h)) continue;
                    covered += w;
                    roof = Mathf.Max(roof, h);
                }
            }
            return total > 1e-4f ? covered / total : 0f;
        }

        /// <summary>Footprint in square metres (shoelace, non-negative).</summary>
        public static float PolygonArea(List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) a += p[j].x * p[i].y - p[i].x * p[j].y;
            return Mathf.Abs(a) * 0.5f;
        }

        /// <summary>
        /// The polygon moved `d` metres outwards (negative: inwards), mitred at the corners and clamped so a sharp
        /// corner cannot throw a spike across the room. For counter-clockwise input, as ToCounterClockwise returns.
        /// </summary>
        public static List<Vector2> Offset(List<Vector2> p, float d)
        {
            int n = p.Count;
            var outp = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = p[(i - 1 + n) % n], cur = p[i], next = p[(i + 1) % n];
                Vector2 e0 = cur - prev, e1 = next - cur;
                if (e0.sqrMagnitude < 1e-8f || e1.sqrMagnitude < 1e-8f) { outp.Add(cur); continue; }
                e0.Normalize();
                e1.Normalize();
                var n0 = new Vector2(e0.y, -e0.x);
                var n1 = new Vector2(e1.y, -e1.x);
                Vector2 bisector = n0 + n1;
                if (bisector.sqrMagnitude < 1e-6f) bisector = n1;
                bisector.Normalize();
                float miter = d / Mathf.Max(0.35f, Vector2.Dot(bisector, n1));
                miter = Mathf.Clamp(miter, -Mathf.Abs(d) * 2.5f, Mathf.Abs(d) * 2.5f);
                outp.Add(cur + bisector * miter);
            }
            return outp;
        }

        // ---- helpers -----------------------------------------------------------------------------------
        struct Face
        {
            public Vector2 A, B, N;
            public float Len, Height;
        }

        /// <summary>Is `p` on or within `tolerance` of a wall 3 m or taller? Reports the tallest such wall.</summary>
        static bool TallAt(MapDef map, Vector2 p, float tolerance, out float height)
        {
            height = 0f;
            foreach (WallDef w in map.walls)
            {
                if (w == null || w.points == null || w.points.Count < 3 || w.height < 3f) continue;
                if (Geo.PointInPolygon(p, w.points))
                {
                    height = Mathf.Max(height, w.height);
                    continue;
                }
                if (tolerance <= 0f) continue;
                for (int i = 0, j = w.points.Count - 1; i < w.points.Count; j = i++)
                {
                    var a = new Vector2(w.points[j].x, w.points[j].y);
                    var b = new Vector2(w.points[i].x, w.points[i].y);
                    Vector2 ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    if ((a + ab * t - p).sqrMagnitude > tolerance * tolerance) continue;
                    height = Mathf.Max(height, w.height);
                    break;
                }
            }
            return height > 0f;
        }

        /// <summary>Gaps of `minGap` to `maxGap` between two parallel wall faces that look at each other, with clear floor between.</summary>
        static List<DoorGap> Straight(MapDef map, float minGap, float maxGap)
        {
            var gaps = new List<DoorGap>();
            if (map.walls == null) return gaps;
            var faces = new List<Face>();
            foreach (WallDef w in map.walls)
            {
                if (w == null || w.points == null || w.points.Count < 3 || w.height < 3f) continue;
                List<Vector2> p = MapBuilder.ToCounterClockwise(w.points);
                for (int i = 0; i < p.Count; i++)
                {
                    Vector2 a = p[i], b = p[(i + 1) % p.Count];
                    Vector2 e = b - a;
                    float len = e.magnitude;
                    if (len < 2f) continue;
                    var n = new Vector2(e.y, -e.x) / len;
                    if (!MapBuilder.IsClear((a + b) * 0.5f + n * 0.6f, map, 0.2f)) continue;
                    faces.Add(new Face { A = a, B = b, N = n, Len = len, Height = w.height });
                }
            }
            for (int i = 0; i < faces.Count; i++)
            {
                for (int j = i + 1; j < faces.Count; j++)
                {
                    Face fi = faces[i], fj = faces[j];
                    if (Vector2.Dot(fi.N, fj.N) > -0.9f) continue;
                    ClosestOnSegments(fi.A, fi.B, fj.A, fj.B, out Vector2 c1, out Vector2 c2);
                    float width = (c2 - c1).magnitude;
                    if (width < minGap || width > maxGap) continue;
                    // Both ends must be on the flat of the faces, not around a corner.
                    float s = Vector2.Dot(c1 - fi.A, fi.B - fi.A) / (fi.Len * fi.Len);
                    float t = Vector2.Dot(c2 - fj.A, fj.B - fj.A) / (fj.Len * fj.Len);
                    if (s < 0.08f || s > 0.92f || t < 0.08f || t > 0.92f) continue;
                    Vector2 dir = (c2 - c1) / width;
                    if (Mathf.Abs(Vector2.Dot(dir, fi.N)) < 0.86f) continue;
                    if (!MapBuilder.IsClear((c1 + c2) * 0.5f, map, 0.4f)) continue;
                    bool blocked = false;
                    for (float u = 0.25f; u < 0.9f && !blocked; u += 0.25f)
                        blocked = !MapBuilder.IsClear(Vector2.Lerp(c1, c2, u), map, 0f);
                    if (blocked) continue;
                    Vector2 mid = (c1 + c2) * 0.5f;
                    bool dup = false;
                    foreach (DoorGap g in gaps)
                        if (((g.A + g.B) * 0.5f - mid).sqrMagnitude < 4f) { dup = true; break; }
                    if (dup) continue;
                    gaps.Add(new DoorGap { A = c1, B = c2, Width = width, Height = Mathf.Min(fi.Height, fj.Height) });
                }
            }
            return gaps;
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
    }
}

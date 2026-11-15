// Smooth lofted solids for the character meshes: a stack of superelliptic
// cross-sections along local +y, skinned into one smooth-shaded surface with
// finite-difference normals, optional flat or domed caps, open arcs (coat
// panels, hoods, pauldrons) emitted double-sided, and helpers for limbs between
// two points and ellipsoids. Everything writes into a MeshKit, so a whole
// figure part is still one merged mesh. Vertex colour RGB is the albedo and A
// the specular mask of Saltworks/HeroToon (0 cloth, ~0.4 leather, 1 metal).
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    /// <summary>One cross-section: height, half-width (x), half-depth (z), centre offset and squareness (2 ellipse, 4+ rounded box).</summary>
    public struct Sec
    {
        public float Y, Rx, Rz, Cx, Cz, Sq;

        public Sec(float y, float rx, float rz, float cx = 0f, float cz = 0f, float sq = 2f)
        {
            Y = y;
            Rx = rx;
            Rz = rz;
            Cx = cx;
            Cz = cz;
            Sq = sq;
        }
    }

    public static class Loft
    {
        static readonly List<Vector3> P = new List<Vector3>();
        static readonly List<Vector3> Nrm = new List<Vector3>();
        static readonly List<Sec> Tmp = new List<Sec>();

        static float SPow(float v, float e)
        {
            return Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), e);
        }

        /// <summary>The point on section `s` at angle `deg` (0 = front, +z; 90 = right, +x).</summary>
        static Vector3 OnSec(Sec s, float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            float e = 2f / Mathf.Max(0.5f, s.Sq);
            return new Vector3(s.Cx + s.Rx * SPow(Mathf.Sin(a), e), s.Y, s.Cz + s.Rz * SPow(Mathf.Cos(a), e));
        }

        /// <summary>
        /// A closed (a1 - a0 = 360) or open loft through `secs` (ascending y) with `sides` segments, transformed by `m`.
        /// Closed lofts get flat caps where asked; open ones are emitted on both faces (`thick` apart) so they never vanish
        /// from behind.
        /// </summary>
        public static void Tube(MeshKit k, Matrix4x4 m, IList<Sec> secs, int sides, Color col, bool capBottom = true, bool capTop = true, float a0 = 0f, float a1 = 360f, float thick = 0.012f)
        {
            if (k == null || secs == null || secs.Count < 2) return;
            sides = Mathf.Clamp(sides, 3, 48);
            bool closed = a1 - a0 >= 359.9f;
            Build(k, m, secs, sides, col, closed, a0, a1, false, 0f);
            if (closed)
            {
                if (capBottom) Cap(k, m, secs[0], sides, col, false);
                if (capTop) Cap(k, m, secs[secs.Count - 1], sides, col, true);
            }
            else
            {
                Build(k, m, secs, sides, col, false, a0, a1, true, thick);
            }
        }

        static void Build(MeshKit k, Matrix4x4 m, IList<Sec> secs, int sides, Color col, bool closed, float a0, float a1, bool inner, float thick)
        {
            int rows = secs.Count;
            int cols = closed ? sides : sides + 1;
            P.Clear();
            Nrm.Clear();
            for (int i = 0; i < rows; i++)
            {
                Sec s = secs[i];
                if (inner)
                {
                    s.Rx = Mathf.Max(0.001f, s.Rx - thick);
                    s.Rz = Mathf.Max(0.001f, s.Rz - thick);
                }
                for (int j = 0; j < cols; j++) P.Add(OnSec(s, a0 + (a1 - a0) * j / sides));
            }
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    int jl = closed ? (j - 1 + cols) % cols : Mathf.Max(0, j - 1);
                    int jr = closed ? (j + 1) % cols : Mathf.Min(cols - 1, j + 1);
                    int id = Mathf.Max(0, i - 1), iu = Mathf.Min(rows - 1, i + 1);
                    Vector3 du = P[i * cols + jr] - P[i * cols + jl];
                    Vector3 dv = P[iu * cols + j] - P[id * cols + j];
                    Vector3 n = Vector3.Cross(dv, du);
                    Vector3 c = new Vector3(secs[i].Cx, secs[i].Y, secs[i].Cz);
                    Vector3 outDir = P[i * cols + j] - c;
                    if (n.sqrMagnitude < 1e-12f)
                    {
                        // A pole (radius 0): point along the stack.
                        n = i == 0 ? Vector3.down : i == rows - 1 ? Vector3.up : outDir;
                        if (n.sqrMagnitude < 1e-12f) n = Vector3.up;
                    }
                    else if (outDir.sqrMagnitude > 1e-12f && Vector3.Dot(n, outDir) < 0f) n = -n;
                    else if (outDir.sqrMagnitude <= 1e-12f && i == 0 && n.y > 0f) n = -n;
                    else if (outDir.sqrMagnitude <= 1e-12f && i == rows - 1 && n.y < 0f) n = -n;
                    n.Normalize();
                    if (inner) n = -n;
                    Nrm.Add(n);
                }
            }
            int baseIdx = k.VertexCount;
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    Vector3 p = P[i * cols + j];
                    Vector3 wn = m.MultiplyVector(Nrm[i * cols + j]).normalized;
                    k.VertUv(m.MultiplyPoint3x4(p), wn, col, new Vector2(j / (float)sides, p.y * 3f));
                }
            }
            for (int i = 0; i + 1 < rows; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int jn = closed ? (j + 1) % cols : j + 1;
                    int v00 = baseIdx + i * cols + j, v01 = baseIdx + i * cols + jn;
                    int v10 = baseIdx + (i + 1) * cols + j, v11 = baseIdx + (i + 1) * cols + jn;
                    Vector3 fn = k.N[v00] + k.N[v01] + k.N[v10] + k.N[v11];
                    if (fn.sqrMagnitude < 1e-10f) continue;
                    if (TriOk(k, v00, v10, v11)) k.Index(0, v00, v10, v11, fn);
                    if (TriOk(k, v00, v11, v01)) k.Index(0, v00, v11, v01, fn);
                }
            }
        }

        static bool TriOk(MeshKit k, int a, int b, int c)
        {
            return Vector3.Cross(k.V[b] - k.V[a], k.V[c] - k.V[a]).sqrMagnitude > 1e-14f;
        }

        static void Cap(MeshKit k, Matrix4x4 m, Sec s, int sides, Color col, bool top)
        {
            if (s.Rx < 1e-4f && s.Rz < 1e-4f) return;
            Vector3 n = m.MultiplyVector(top ? Vector3.up : Vector3.down).normalized;
            int c = k.VertUv(m.MultiplyPoint3x4(new Vector3(s.Cx, s.Y, s.Cz)), n, col, new Vector2(0.5f, 0.5f));
            int first = k.VertexCount;
            for (int j = 0; j < sides; j++)
            {
                Vector3 p = OnSec(s, 360f * j / sides);
                k.VertUv(m.MultiplyPoint3x4(p), n, col, new Vector2(p.x, p.z) * 3f);
            }
            for (int j = 0; j < sides; j++) k.Index(0, c, first + j, first + (j + 1) % sides, n);
        }

        // ---- section builders ----------------------------------------------------------------------------
        /// <summary>Appends a rounded end above the last section: the radius closes along a quarter circle over `h`.</summary>
        public static void DomeUp(List<Sec> s, float h, int steps = 3)
        {
            if (s.Count == 0) return;
            Sec b = s[s.Count - 1];
            for (int i = 1; i <= steps; i++)
            {
                float a = i / (float)steps * Mathf.PI * 0.5f;
                float r = i == steps ? 0f : Mathf.Cos(a);
                s.Add(new Sec(b.Y + h * Mathf.Sin(a), b.Rx * r, b.Rz * r, b.Cx, b.Cz, b.Sq));
            }
        }

        /// <summary>Prepends a rounded end below the first section.</summary>
        public static void DomeDown(List<Sec> s, float h, int steps = 3)
        {
            if (s.Count == 0) return;
            Sec b = s[0];
            for (int i = 1; i <= steps; i++)
            {
                float a = i / (float)steps * Mathf.PI * 0.5f;
                float r = i == steps ? 0f : Mathf.Cos(a);
                s.Insert(0, new Sec(b.Y - h * Mathf.Sin(a), b.Rx * r, b.Rz * r, b.Cx, b.Cz, b.Sq));
            }
        }

        /// <summary>A scratch list (cleared) for building sections without allocating.</summary>
        public static List<Sec> Begin()
        {
            Tmp.Clear();
            return Tmp;
        }

        // ---- shapes ---------------------------------------------------------------------------------------
        /// <summary>An ellipsoid at `c` with radii `r` (rotation `euler`), smooth.</summary>
        public static void Blob(MeshKit k, Matrix4x4 parent, Vector3 c, Vector3 r, Color col, Vector3 euler = default, int sides = 14, int rings = 8, float sq = 2f)
        {
            List<Sec> s = Begin();
            rings = Mathf.Max(3, rings);
            for (int i = 0; i <= rings; i++)
            {
                float a = -Mathf.PI * 0.5f + Mathf.PI * i / rings;
                float rr = Mathf.Cos(a);
                if (i == 0 || i == rings) rr = 0f;
                s.Add(new Sec(Mathf.Sin(a) * r.y, r.x * rr, r.z * rr, 0f, 0f, sq));
            }
            Tube(k, parent * Matrix4x4.TRS(c, Quaternion.Euler(euler), Vector3.one), s, sides, col, false, false);
        }

        /// <summary>A matrix whose local +y runs from `a` towards `b` (lofts between two points).</summary>
        public static Matrix4x4 Along(Matrix4x4 parent, Vector3 a, Vector3 b, float roll = 0f)
        {
            Vector3 d = b - a;
            Quaternion q = d.sqrMagnitude > 1e-10f ? Quaternion.FromToRotation(Vector3.up, d.normalized) : Quaternion.identity;
            return parent * Matrix4x4.TRS(a, q * Quaternion.Euler(0f, roll, 0f), Vector3.one);
        }

        /// <summary>A tapered rounded rod from `a` (radius ra) to `b` (radius rb), domed at both ends.</summary>
        public static void Rod(MeshKit k, Matrix4x4 parent, Vector3 a, Vector3 b, float ra, float rb, Color col, int sides = 10, bool domes = true)
        {
            float len = (b - a).magnitude;
            if (len < 1e-5f) return;
            List<Sec> s = Begin();
            s.Add(new Sec(0f, ra, ra));
            s.Add(new Sec(len, rb, rb));
            if (domes)
            {
                DomeDown(s, Mathf.Min(ra, len * 0.5f), 2);
                DomeUp(s, Mathf.Min(rb, len * 0.5f), 2);
            }
            Tube(k, Along(parent, a, b), s, sides, col, !domes, !domes);
        }

        /// <summary>A short ring band around a limb (straps, cuffs, belts): from y0 to y1 at radii rx, rz.</summary>
        public static void Band(MeshKit k, Matrix4x4 m, float y0, float y1, float rx, float rz, Color col, float cx = 0f, float cz = 0f, float sq = 2f, int sides = 16)
        {
            List<Sec> s = Begin();
            float e = Mathf.Min(0.004f, (y1 - y0) * 0.3f);
            s.Add(new Sec(y0, rx - e, rz - e, cx, cz, sq));
            s.Add(new Sec(y0 + e, rx, rz, cx, cz, sq));
            s.Add(new Sec(y1 - e, rx, rz, cx, cz, sq));
            s.Add(new Sec(y1, rx - e, rz - e, cx, cz, sq));
            Tube(k, m, s, sides, col, false, false);
        }

        /// <summary>A flat double-sided ribbon from `a` to `b` (width w0 to w1), its face towards `normal`.</summary>
        public static void Ribbon(MeshKit k, Matrix4x4 m, Vector3 a, Vector3 b, float w0, float w1, Vector3 normal, Color col)
        {
            Vector3 d = b - a;
            Vector3 side = Vector3.Cross(normal, d).normalized;
            Vector3 n = m.MultiplyVector(normal).normalized;
            Vector3 p0 = m.MultiplyPoint3x4(a - side * w0 * 0.5f), p1 = m.MultiplyPoint3x4(a + side * w0 * 0.5f);
            Vector3 p2 = m.MultiplyPoint3x4(b + side * w1 * 0.5f), p3 = m.MultiplyPoint3x4(b - side * w1 * 0.5f);
            k.QuadUv(0, p0, p1, p2, p3, n, col, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            k.QuadUv(0, p0, p3, p2, p1, -n, col, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
        }
    }
}

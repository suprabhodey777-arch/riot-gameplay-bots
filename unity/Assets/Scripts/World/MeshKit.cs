// Builds merged meshes from simple solids (chamfered boxes, cylinders, quads)
// with flat normals, box-projected UVs and per-vertex colours. Vertex colour
// RGB is the albedo tint and A the material mask of Saltworks/WorldLit
// (1 = metal, 0 = paint/polymer). Many parts become one draw call per submesh.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    public class MeshKit
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<int>[] T;
        /// <summary>UV units per metre for the box projection.</summary>
        public float UvScale = 1f;

        public MeshKit(int submeshes = 1)
        {
            T = new List<int>[Mathf.Max(1, submeshes)];
            for (int i = 0; i < T.Length; i++) T[i] = new List<int>();
        }

        public int VertexCount => V.Count;

        public bool Empty
        {
            get
            {
                foreach (List<int> t in T)
                    if (t.Count > 0) return false;
                return true;
            }
        }

        int Vert(Vector3 p, Vector3 n, Color c)
        {
            V.Add(p);
            N.Add(n);
            C.Add(c);
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            Vector2 uv;
            if (ax >= ay && ax >= az) uv = new Vector2(p.z, p.y);
            else if (ay >= az) uv = new Vector2(p.x, p.z);
            else uv = new Vector2(p.x, p.y);
            UV.Add(uv * UvScale);
            return V.Count - 1;
        }

        /// <summary>Adds a vertex with an explicit UV.</summary>
        public int VertUv(Vector3 p, Vector3 n, Color c, Vector2 uv)
        {
            V.Add(p);
            N.Add(n);
            C.Add(c);
            UV.Add(uv);
            return V.Count - 1;
        }

        /// <summary>A triangle whose front face points along `n` (Unity's front faces are clockwise).</summary>
        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 n, Color col)
        {
            int ia = Vert(a, n, col), ib = Vert(b, n, col), ic = Vert(c, n, col);
            Index(sub, ia, ib, ic, n);
        }

        public void Index(int sub, int ia, int ib, int ic, Vector3 n)
        {
            Vector3 f = Vector3.Cross(V[ib] - V[ia], V[ic] - V[ia]);
            List<int> t = T[Mathf.Clamp(sub, 0, T.Length - 1)];
            if (Vector3.Dot(f, n) >= 0f) { t.Add(ia); t.Add(ib); t.Add(ic); }
            else { t.Add(ia); t.Add(ic); t.Add(ib); }
        }

        /// <summary>A planar quad (corners in order around it), facing `n`.</summary>
        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color col)
        {
            int ia = Vert(a, n, col), ib = Vert(b, n, col), ic = Vert(c, n, col), id = Vert(d, n, col);
            Index(sub, ia, ib, ic, n);
            Index(sub, ia, ic, id, n);
        }

        /// <summary>A quad with explicit UVs (decal strips, markings).</summary>
        public void QuadUv(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color col, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int ia = VertUv(a, n, col, ua), ib = VertUv(b, n, col, ub), ic = VertUv(c, n, col, uc), id = VertUv(d, n, col, ud);
            Index(sub, ia, ib, ic, n);
            Index(sub, ia, ic, id, n);
        }

        static Vector3 Set(Vector3 v, int axis, float value)
        {
            v[axis] = value;
            return v;
        }

        static Vector3 Axis(int a, float s)
        {
            Vector3 v = Vector3.zero;
            v[a] = s;
            return v;
        }

        /// <summary>A box of `size` at matrix `m` (translation and rotation only) with edges chamfered by `bevel` metres.</summary>
        public void Box(int sub, Matrix4x4 m, Vector3 size, float bevel, Color col)
        {
            Vector3 h = size * 0.5f;
            float b = Mathf.Clamp(bevel, 0f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.9f);
            Vector3 i = h - new Vector3(b, b, b);
            // Faces.
            for (int a = 0; a < 3; a++)
            {
                int u = (a + 1) % 3, w = (a + 2) % 3;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 n = Axis(a, s);
                    Vector3 c0 = Set(Set(Set(Vector3.zero, a, s * h[a]), u, i[u]), w, i[w]);
                    Vector3 c1 = Set(Set(Set(Vector3.zero, a, s * h[a]), u, i[u]), w, -i[w]);
                    Vector3 c2 = Set(Set(Set(Vector3.zero, a, s * h[a]), u, -i[u]), w, -i[w]);
                    Vector3 c3 = Set(Set(Set(Vector3.zero, a, s * h[a]), u, -i[u]), w, i[w]);
                    QuadM(sub, m, c0, c1, c2, c3, n, col);
                }
            }
            if (b <= 1e-5f) return;
            // Edges.
            for (int a1 = 0; a1 < 3; a1++)
            {
                for (int a2 = a1 + 1; a2 < 3; a2++)
                {
                    int t = 3 - a1 - a2;
                    for (int s1 = -1; s1 <= 1; s1 += 2)
                    {
                        for (int s2 = -1; s2 <= 1; s2 += 2)
                        {
                            Vector3 n = (Axis(a1, s1) + Axis(a2, s2)).normalized;
                            Vector3 p1 = Set(Set(Set(Vector3.zero, a1, s1 * h[a1]), a2, s2 * i[a2]), t, -i[t]);
                            Vector3 p2 = Set(Set(Set(Vector3.zero, a1, s1 * h[a1]), a2, s2 * i[a2]), t, i[t]);
                            Vector3 p3 = Set(Set(Set(Vector3.zero, a1, s1 * i[a1]), a2, s2 * h[a2]), t, i[t]);
                            Vector3 p4 = Set(Set(Set(Vector3.zero, a1, s1 * i[a1]), a2, s2 * h[a2]), t, -i[t]);
                            QuadM(sub, m, p1, p2, p3, p4, n, col);
                        }
                    }
                }
            }
            // Corners.
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var n = new Vector3(sx, sy, sz).normalized;
                var pa = new Vector3(sx * h.x, sy * i.y, sz * i.z);
                var pb = new Vector3(sx * i.x, sy * h.y, sz * i.z);
                var pc = new Vector3(sx * i.x, sy * i.y, sz * h.z);
                Tri(sub, m.MultiplyPoint3x4(pa), m.MultiplyPoint3x4(pb), m.MultiplyPoint3x4(pc), m.MultiplyVector(n).normalized, col);
            }
        }

        void QuadM(int sub, Matrix4x4 m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color col)
        {
            Quad(sub, m.MultiplyPoint3x4(a), m.MultiplyPoint3x4(b), m.MultiplyPoint3x4(c), m.MultiplyPoint3x4(d), m.MultiplyVector(n).normalized, col);
        }

        /// <summary>A cylinder along local +z (centred), `sides` facets; smooth sides unless faceted, flat caps.</summary>
        public void Cylinder(int sub, Matrix4x4 m, float radius, float length, int sides, Color col, bool faceted = false, bool caps = true)
        {
            sides = Mathf.Max(3, sides);
            float hz = length * 0.5f;
            float step = Mathf.PI * 2f / sides;
            float off = faceted ? step * 0.5f : 0f;
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * step + off, a1 = (k + 1) * step + off;
                var d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f);
                var d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                Vector3 p0 = d0 * radius + Vector3.forward * -hz, p1 = d1 * radius + Vector3.forward * -hz;
                Vector3 p2 = d1 * radius + Vector3.forward * hz, p3 = d0 * radius + Vector3.forward * hz;
                if (faceted)
                {
                    QuadM(sub, m, p0, p1, p2, p3, ((d0 + d1) * 0.5f).normalized, col);
                }
                else
                {
                    Vector3 n0 = m.MultiplyVector(d0).normalized, n1 = m.MultiplyVector(d1).normalized;
                    int i0 = Vert(m.MultiplyPoint3x4(p0), n0, col), i1 = Vert(m.MultiplyPoint3x4(p1), n1, col);
                    int i2 = Vert(m.MultiplyPoint3x4(p2), n1, col), i3 = Vert(m.MultiplyPoint3x4(p3), n0, col);
                    Vector3 fn = m.MultiplyVector((d0 + d1) * 0.5f).normalized;
                    Index(sub, i0, i1, i2, fn);
                    Index(sub, i0, i2, i3, fn);
                }
                if (caps)
                {
                    Tri(sub, m.MultiplyPoint3x4(Vector3.forward * hz), m.MultiplyPoint3x4(p3), m.MultiplyPoint3x4(p2), m.MultiplyVector(Vector3.forward).normalized, col);
                    Tri(sub, m.MultiplyPoint3x4(Vector3.forward * -hz), m.MultiplyPoint3x4(p0), m.MultiplyPoint3x4(p1), m.MultiplyVector(Vector3.back).normalized, col);
                }
            }
        }

        /// <summary>Matrix for a part at `pos` with Euler rotation `euler` (degrees), inside `parent`.</summary>
        public static Matrix4x4 At(Matrix4x4 parent, Vector3 pos, Vector3 euler)
        {
            return parent * Matrix4x4.TRS(pos, Quaternion.Euler(euler), Vector3.one);
        }

        public static Matrix4x4 At(Vector3 pos, Vector3 euler)
        {
            return Matrix4x4.TRS(pos, Quaternion.Euler(euler), Vector3.one);
        }

        /// <summary>Matrix with local +z along `dir` (for cylinders between two points).</summary>
        public static Matrix4x4 Along(Vector3 centre, Vector3 dir)
        {
            Quaternion q = dir.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(dir.normalized, Mathf.Abs(dir.normalized.y) > 0.95f ? Vector3.forward : Vector3.up) : Quaternion.identity;
            return Matrix4x4.TRS(centre, q, Vector3.one);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (V.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(V);
            mesh.SetNormals(N);
            mesh.SetUVs(0, UV);
            mesh.SetColors(C);
            mesh.subMeshCount = T.Length;
            for (int i = 0; i < T.Length; i++) mesh.SetTriangles(T[i], i, false);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Clear()
        {
            V.Clear();
            N.Clear();
            UV.Clear();
            C.Clear();
            foreach (List<int> t in T) t.Clear();
        }
    }
}

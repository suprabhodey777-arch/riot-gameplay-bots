// Immediate-mode GL drawing in pixel space (top-left origin, like IMGUI), for
// the crosshair and the minimap. Draws coloured triangles with an optional
// texture through Saltworks/Overlay. Call Begin, then shapes, then End; every
// shape is batched into the same GL.TRIANGLES block.
using UnityEngine;

namespace Saltworks
{
    public static class Draw2D
    {
        static Material mat;
        static bool open;

        public static Material Mat
        {
            get
            {
                if (mat != null) return mat;
                Shader s = Shader.Find("Saltworks/Overlay");
                if (s == null) s = Shader.Find("Hidden/Internal-Colored");
                if (s == null) s = Shader.Find("Sprites/Default");
                mat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
                if (mat.HasProperty("_ZTest")) mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
                if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                return mat;
            }
        }

        /// <summary>Starts drawing into the current target, `w` x `h` pixels, y down. `tex` null = flat colour.</summary>
        public static void Begin(float w, float h, Texture tex = null)
        {
            if (open) End();
            Material m = Mat;
            if (m.HasProperty("_MainTex")) m.mainTexture = tex != null ? tex : Texture2D.whiteTexture;
            m.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, w, h, 0f);
            GL.Begin(GL.TRIANGLES);
            open = true;
        }

        /// <summary>Switches the texture mid-drawing (flushes the current batch).</summary>
        public static void SetTexture(Texture tex)
        {
            if (!open) return;
            GL.End();
            Material m = Mat;
            if (m.HasProperty("_MainTex")) m.mainTexture = tex != null ? tex : Texture2D.whiteTexture;
            m.SetPass(0);
            GL.Begin(GL.TRIANGLES);
        }

        public static void End()
        {
            if (!open) return;
            GL.End();
            GL.PopMatrix();
            open = false;
        }

        static void V(float x, float y, float u = 0.5f, float v = 0.5f)
        {
            GL.TexCoord2(u, v);
            GL.Vertex3(x, y, 0f);
        }

        public static void Rect(float x, float y, float w, float h, Color c)
        {
            if (w <= 0f || h <= 0f) return;
            GL.Color(c);
            V(x, y); V(x + w, y); V(x + w, y + h);
            V(x, y); V(x + w, y + h); V(x, y + h);
        }

        public static void Tri(Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            GL.Color(col);
            V(a.x, a.y); V(b.x, b.y); V(c.x, c.y);
        }

        /// <summary>A textured quad: corners and their texture coordinates in order (a, b, c, d).</summary>
        public static void TexQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Color col)
        {
            GL.Color(col);
            V(a.x, a.y, ua.x, ua.y); V(b.x, b.y, ub.x, ub.y); V(c.x, c.y, uc.x, uc.y);
            V(a.x, a.y, ua.x, ua.y); V(c.x, c.y, uc.x, uc.y); V(d.x, d.y, ud.x, ud.y);
        }

        public static void Disc(Vector2 c, float r, Color col, int segments = 24)
        {
            if (r <= 0f) return;
            GL.Color(col);
            float step = Mathf.PI * 2f / segments;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * step, a1 = (i + 1) * step;
                V(c.x, c.y);
                V(c.x + Mathf.Cos(a0) * r, c.y + Mathf.Sin(a0) * r);
                V(c.x + Mathf.Cos(a1) * r, c.y + Mathf.Sin(a1) * r);
            }
        }

        public static void Ring(Vector2 c, float rIn, float rOut, Color col, int segments = 48)
        {
            if (rOut <= rIn) return;
            GL.Color(col);
            float step = Mathf.PI * 2f / segments;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * step, a1 = (i + 1) * step;
                float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0), c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
                V(c.x + c0 * rIn, c.y + s0 * rIn); V(c.x + c0 * rOut, c.y + s0 * rOut); V(c.x + c1 * rOut, c.y + s1 * rOut);
                V(c.x + c0 * rIn, c.y + s0 * rIn); V(c.x + c1 * rOut, c.y + s1 * rOut); V(c.x + c1 * rIn, c.y + s1 * rIn);
            }
        }

        /// <summary>A thick line segment.</summary>
        public static void Line(Vector2 a, Vector2 b, float width, Color col)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector2 n = new Vector2(-d.y, d.x) / len * (width * 0.5f);
            GL.Color(col);
            V(a.x + n.x, a.y + n.y); V(b.x + n.x, b.y + n.y); V(b.x - n.x, b.y - n.y);
            V(a.x + n.x, a.y + n.y); V(b.x - n.x, b.y - n.y); V(a.x - n.x, a.y - n.y);
        }
    }
}

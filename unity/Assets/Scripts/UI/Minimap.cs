// Corner minimap (toggle M) and full map (hold Tab). The static map (floor,
// sites, walls) is one vertex-coloured mesh built once per MapDef and drawn with
// a single DrawMeshNow, so it stays sharp at every zoom. Dynamic icons (player
// and view cone, teammates, smokes, spike, enemies) are GL triangles drawn each
// frame inside a GL viewport that clips them to the panel.
//
// Enemies appear only when the player can see them (inside the view cone with
// line of sight from the eye, walls and smokes block, not while blind), while a
// recon reveal lasts (revealedUntilTick), or as a fading ping when they make a
// loud footstep within rules.runHearingM.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public sealed class Minimap
    {
        class Track
        {
            public Vector2 Pos;
            public float SeenAt = -10f;
            public bool Visible;
            public bool Revealed;
            public float PingAt = -10f;
            public Vector2 PingPos;
        }

        public struct SiteLabel
        {
            public string Letter;
            public Vector2 Sim;
        }

        static readonly Color32 FloorC = new Color32(46, 50, 60, 255);
        static readonly Color32 WallC = new Color32(165, 172, 188, 255);
        static readonly Color32 LowC = new Color32(112, 118, 132, 255);
        static readonly Color32 OuterC = new Color32(90, 96, 110, 255);
        static readonly Color BackC = new Color(0.05f, 0.06f, 0.08f, 0.88f);

        Mesh mesh;
        MapDef builtFor;
        float mapW = 100f, mapH = 100f;
        readonly List<SiteLabel> sites = new List<SiteLabel>();
        readonly Dictionary<string, Track> tracks = new Dictionary<string, Track>();

        public IReadOnlyList<SiteLabel> Sites => sites;

        /// <summary>The panel transform of the last Draw, for labels drawn over it.</summary>
        Vector2 focus, centre;
        float pxPerM = 1f, angle;

        public void Clear()
        {
            tracks.Clear();
        }

        // ---- static map -----------------------------------------------------------------------
        public void Build(MapDef map)
        {
            if (map == null || ReferenceEquals(map, builtFor)) return;
            builtFor = map;
            tracks.Clear();
            sites.Clear();
            if (mesh != null) Object.Destroy(mesh);
            mapW = map.bounds != null && map.bounds.width > 0 ? map.bounds.width : 100f;
            mapH = map.bounds != null && map.bounds.height > 0 ? map.bounds.height : 100f;

            var verts = new List<Vector3>();
            var cols = new List<Color32>();
            var tris = new List<int>();
            void Quad(float x0, float y0, float x1, float y1, Color32 c)
            {
                int b = verts.Count;
                verts.Add(new Vector3(x0, y0, 0f)); verts.Add(new Vector3(x1, y0, 0f));
                verts.Add(new Vector3(x1, y1, 0f)); verts.Add(new Vector3(x0, y1, 0f));
                for (int i = 0; i < 4; i++) cols.Add(c);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            void Poly(List<Vec2> pts, Color32 c)
            {
                if (pts == null || pts.Count < 3) return;
                List<Vector2> p = MapBuilder.ToCounterClockwise(pts);
                List<int> t = MapBuilder.Triangulate(p);
                int b = verts.Count;
                foreach (Vector2 v in p)
                {
                    verts.Add(new Vector3(v.x, v.y, 0f));
                    cols.Add(c);
                }
                foreach (int i in t) tris.Add(b + i);
            }
            void Outline(List<Vec2> pts, float w, Color32 c)
            {
                if (pts == null || pts.Count < 3) return;
                List<Vector2> p = MapBuilder.ToCounterClockwise(pts);
                for (int i = 0; i < p.Count; i++)
                {
                    Vector2 a = p[i], d = p[(i + 1) % p.Count];
                    Vector2 e = d - a;
                    if (e.sqrMagnitude < 1e-8f) continue;
                    Vector2 n = new Vector2(-e.y, e.x).normalized * w;
                    int b = verts.Count;
                    verts.Add(new Vector3(a.x, a.y, 0f)); verts.Add(new Vector3(d.x, d.y, 0f));
                    verts.Add(new Vector3(d.x + n.x, d.y + n.y, 0f)); verts.Add(new Vector3(a.x + n.x, a.y + n.y, 0f));
                    for (int k = 0; k < 4; k++) cols.Add(c);
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                }
            }

            Quad(0f, 0f, mapW, mapH, FloorC);
            foreach (KeyValuePair<string, List<Vec2>> s in map.SiteList())
            {
                Color sc = Palette.Site(s.Key);
                Poly(s.Value, new Color32((byte)(sc.r * 110), (byte)(sc.g * 110), (byte)(sc.b * 110), 255));
                Outline(s.Value, 0.5f, new Color32((byte)(sc.r * 255), (byte)(sc.g * 255), (byte)(sc.b * 255), 230));
                sites.Add(new SiteLabel { Letter = s.Key, Sim = Geo.Centroid(s.Value) });
            }
            if (map.walls != null)
            {
                foreach (WallDef w in map.walls)
                {
                    bool outer = w.id != null && w.id.StartsWith("outer");
                    Poly(w.points, outer ? OuterC : w.height <= 1.5f ? LowC : WallC);
                }
            }
            mesh = new Mesh { name = "Minimap", hideFlags = HideFlags.DontSave };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            var uv = new List<Vector2>(verts.Count);
            for (int i = 0; i < verts.Count; i++) uv.Add(new Vector2(0.5f, 0.5f));
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        // ---- per frame: who is visible --------------------------------------------------------
        Track TrackOf(string id)
        {
            if (!tracks.TryGetValue(id, out Track t))
            {
                t = new Track();
                tracks[id] = t;
            }
            return t;
        }

        /// <summary>Updates enemy visibility from the eye. Call once per frame while a drill is shown.</summary>
        public void Tick(GameController g, Vector2 eye, float headingDeg)
        {
            WorldView world = g.World;
            if (world?.Curr == null || g.Walls == null) return;
            float now = Time.unscaledTime;
            AgentState me = g.Me;
            bool canSee = me != null && me.alive && !world.IsBlind(g.PlayerId);
            float halfFov = Settings.FovH * 0.5f + 4f;
            List<SmokeState> smokes = world.Curr.smokes;
            var fwd = new Vector2(Mathf.Sin(headingDeg * Mathf.Deg2Rad), Mathf.Cos(headingDeg * Mathf.Deg2Rad));
            foreach (BotView b in world.Bots.Values)
            {
                if (b.Team == g.PlayerTeam) continue;
                AgentState a = world.Agent(b.Id);
                if (a == null) continue;
                Track t = TrackOf(b.Id);
                var p = new Vector2(b.Root.transform.position.x, b.Root.transform.position.z);
                t.Revealed = b.Alive && world.IsRevealed(b.Id);
                bool vis = false;
                if (canSee && b.Alive)
                {
                    Vector2 d = p - eye;
                    float dist = d.magnitude;
                    if (dist < 1.2f) vis = true;
                    else if (dist < 120f && Vector2.Angle(fwd, d) <= halfFov) vis = g.Walls.LineOfSight(eye, p, smokes);
                }
                t.Visible = vis;
                if (vis || t.Revealed)
                {
                    t.Pos = p;
                    t.SeenAt = now;
                }
                if (!b.Alive) t.SeenAt = Mathf.Min(t.SeenAt, now - 10f);
            }
        }

        /// <summary>A loud enemy footstep the player can hear: a fading ping at `pos` (sim coordinates).</summary>
        public void Ping(string id, Vector2 pos)
        {
            if (string.IsNullOrEmpty(id)) return;
            Track t = TrackOf(id);
            float now = Time.unscaledTime;
            if (now - t.PingAt < 0.45f && (t.PingPos - pos).sqrMagnitude < 4f) return;
            t.PingAt = now;
            t.PingPos = pos;
        }

        // ---- drawing ---------------------------------------------------------------------------
        Vector2 ToPanel(Vector2 sim)
        {
            Vector2 d = sim - focus;
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            return centre + new Vector2(d.x * c + d.y * s, d.x * s - d.y * c) * pxPerM;
        }

        /// <summary>Where a sim point is drawn, relative to the panel's top-left, in screen pixels (last Draw).</summary>
        public Vector2 PanelPoint(Vector2 sim)
        {
            return ToPanel(sim);
        }

        /// <summary>
        /// Draws the map into `screenRect` (screen pixels, top-left origin). `full` = whole map, north up;
        /// otherwise the corner view around the player. Call during a Repaint event.
        /// </summary>
        public void Draw(GameController g, Rect screenRect, bool full, Vector2 eye, float headingDeg, float opacity)
        {
            if (mesh == null || Event.current == null || Event.current.type != EventType.Repaint) return;
            float w = Mathf.Round(screenRect.width), h = Mathf.Round(screenRect.height);
            centre = new Vector2(w / 2f, h / 2f);
            if (full)
            {
                focus = new Vector2(mapW / 2f, mapH / 2f);
                angle = 0f;
                pxPerM = Mathf.Min(w / mapW, h / mapH) * 0.94f;
            }
            else
            {
                focus = eye;
                float yaw = (90f - headingDeg) * Mathf.Deg2Rad;
                angle = Settings.MinimapRotate ? yaw - Mathf.PI / 2f : 0f;
                pxPerM = (w * 0.5f) / (32f / Mathf.Max(0.1f, Settings.MinimapZoom));
            }

            Material m = Draw2D.Mat;
            Color oldTint = m.HasProperty("_Color") ? m.color : Color.white;
            if (m.HasProperty("_Color")) m.color = new Color(1f, 1f, 1f, Mathf.Clamp01(opacity));
            float px = Mathf.Round(screenRect.x), py = Screen.height - Mathf.Round(screenRect.y) - h;
            GL.Viewport(new Rect(px, py, w, h));

            Draw2D.Begin(w, h);
            Draw2D.Rect(0f, 0f, w, h, BackC);
            Draw2D.End();

            // Static map: one draw call with the panel transform as the model matrix.
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, w, h, 0f);
            if (m.HasProperty("_MainTex")) m.mainTexture = Texture2D.whiteTexture;
            m.SetPass(0);
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            var mat = Matrix4x4.identity;
            mat.m00 = pxPerM * c; mat.m01 = pxPerM * s;
            mat.m10 = pxPerM * s; mat.m11 = -pxPerM * c;
            mat.m03 = centre.x - (mat.m00 * focus.x + mat.m01 * focus.y);
            mat.m13 = centre.y - (mat.m10 * focus.x + mat.m11 * focus.y);
            Graphics.DrawMeshNow(mesh, mat);
            GL.PopMatrix();

            Draw2D.Begin(w, h);
            DrawDynamic(g, eye, headingDeg, full);
            // A thin frame.
            Color frame = new Color(1f, 1f, 1f, 0.16f);
            Draw2D.Rect(0f, 0f, w, 1f, frame);
            Draw2D.Rect(0f, h - 1f, w, 1f, frame);
            Draw2D.Rect(0f, 0f, 1f, h, frame);
            Draw2D.Rect(w - 1f, 0f, 1f, h, frame);
            Draw2D.End();

            GL.Viewport(new Rect(0f, 0f, Screen.width, Screen.height));
            if (m.HasProperty("_Color")) m.color = oldTint;
        }

        void DrawDynamic(GameController g, Vector2 eye, float headingDeg, bool full)
        {
            WorldView world = g.World;
            float now = Time.unscaledTime;
            float k = full ? 1.25f : 1f;
            SimState st = world?.Curr;

            // Smokes.
            if (st?.smokes != null)
            {
                Color sc = new Color(Palette.Smoke.r, Palette.Smoke.g, Palette.Smoke.b, 0.62f);
                foreach (SmokeState sm in st.smokes)
                {
                    if (sm?.pos == null) continue;
                    Vector2 p = ToPanel(new Vector2(sm.pos.x, sm.pos.y));
                    Draw2D.Disc(p, sm.radius * pxPerM, sc, 28);
                    Draw2D.Ring(p, sm.radius * pxPerM - 1.5f, sm.radius * pxPerM, new Color(1f, 1f, 1f, 0.5f), 28);
                }
            }

            // Spike.
            SpikeState sp = st?.spike;
            if (sp != null && sp.pos != null && (sp.status == "planted" || sp.status == "dropped"))
            {
                Vector2 p = ToPanel(new Vector2(sp.pos.x, sp.pos.y));
                float pulse = sp.status == "planted" ? 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(now * 5f)) : 1f;
                Diamond(p, 6f * k + 1.5f, new Color(0f, 0f, 0f, 0.7f));
                Diamond(p, 6f * k, new Color(Palette.Spike.r, Palette.Spike.g, Palette.Spike.b, pulse));
            }

            if (world == null) return;
            // Teammates.
            Color ally = Settings.Ally;
            foreach (BotView b in world.Bots.Values)
            {
                if (b.Team != g.PlayerTeam || !b.Alive) continue;
                Vector3 wp = b.Root.transform.position;
                Vector2 p = ToPanel(new Vector2(wp.x, wp.z));
                Dot(p, 4.5f * k, ally);
                if (st?.spike != null && st.spike.status == "carried" && st.spike.carrierId == b.Id) Diamond(p + new Vector2(0f, -9f * k), 3.5f * k, Palette.Spike);
            }

            // Enemies: seen, revealed, or heard.
            Color enemy = Settings.Enemy;
            foreach (KeyValuePair<string, Track> kv in tracks)
            {
                Track t = kv.Value;
                float pingAge = now - t.PingAt;
                if (pingAge < 1.6f)
                {
                    float a = 1f - pingAge / 1.6f;
                    Vector2 pp = ToPanel(t.PingPos);
                    float r = (4f + 10f * (1f - a)) * k;
                    Draw2D.Ring(pp, r - 1.6f, r, new Color(enemy.r, enemy.g, enemy.b, 0.9f * a), 24);
                    Draw2D.Disc(pp, 2f * k, new Color(enemy.r, enemy.g, enemy.b, a), 10);
                }
                float age = now - t.SeenAt;
                bool live = t.Visible || t.Revealed;
                if (!live && age > 1.2f) continue;
                float alpha = live ? 1f : 1f - age / 1.2f;
                Vector2 p = ToPanel(t.Pos);
                if (t.Revealed)
                {
                    float rr = (7f + 2f * Mathf.Sin(now * 10f)) * k;
                    Draw2D.Ring(p, rr - 1.5f, rr, new Color(enemy.r, enemy.g, enemy.b, 0.8f * alpha), 24);
                }
                Dot(p, 4.5f * k, new Color(enemy.r, enemy.g, enemy.b, alpha));
            }

            // The player: view cone and an arrow.
            float yaw = (90f - headingDeg) * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw));
            Vector2 me = ToPanel(eye);
            Vector2 tip = ToPanel(eye + dir);
            Vector2 up = (tip - me).normalized;
            Color accent = Settings.Accent;
            AgentState mine = g.Me;
            bool alive = mine == null || mine.alive;
            if (alive)
            {
                float cone = Settings.FovH * 0.5f * Mathf.Deg2Rad;
                float reach = (full ? 14f : 20f) * pxPerM * (full ? 1f : 0.6f);
                reach = Mathf.Clamp(reach, 30f, 160f);
                const int segs = 10;
                float baseAng = Mathf.Atan2(up.y, up.x);
                Color cc = new Color(accent.r, accent.g, accent.b, 0.22f);
                for (int i = 0; i < segs; i++)
                {
                    float a0 = baseAng - cone + 2f * cone * i / segs;
                    float a1 = baseAng - cone + 2f * cone * (i + 1) / segs;
                    Draw2D.Tri(me, me + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * reach, me + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * reach, cc);
                }
            }
            Vector2 side = new Vector2(-up.y, up.x);
            float sz = 7f * k;
            Color body = alive ? Color.white : new Color(0.6f, 0.6f, 0.65f);
            Draw2D.Tri(me + up * (sz + 2f), me - up * (sz * 0.6f + 1.5f) + side * (sz * 0.75f + 1.5f), me - up * (sz * 0.6f + 1.5f) - side * (sz * 0.75f + 1.5f), new Color(0f, 0f, 0f, 0.75f));
            Draw2D.Tri(me + up * sz, me - up * sz * 0.6f + side * sz * 0.75f, me - up * sz * 0.6f - side * sz * 0.75f, body);
            if (mine != null && mine.carryingSpike) Diamond(me - up * (sz + 6f), 3.5f * k, Palette.Spike);
        }

        static void Dot(Vector2 p, float r, Color c)
        {
            Draw2D.Disc(p, r + 1.5f, new Color(0f, 0f, 0f, 0.75f * c.a), 14);
            Draw2D.Disc(p, r, c, 14);
        }

        static void Diamond(Vector2 p, float r, Color c)
        {
            Draw2D.Tri(p + new Vector2(0f, -r), p + new Vector2(r, 0f), p + new Vector2(0f, r), c);
            Draw2D.Tri(p + new Vector2(0f, -r), p + new Vector2(0f, r), p + new Vector2(-r, 0f), c);
        }
    }
}

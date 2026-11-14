// Client-side prediction of the player's own movement. Walls2D is a port of
// sim/geometry.ts (resolveCircle, lineOfSight) so the client moves the eye the
// way the sim will. PlayerPredictor applies each sent input immediately, keeps
// a ring of predicted positions by tick, and on every bridge step replays the
// unacknowledged inputs from the bridge's position (the truth). The difference
// that replay makes is shown as a visual offset that blends out over ~100 ms.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    /// <summary>The map's wall polygons on the floor plan, for movement and line of sight.</summary>
    public sealed class Walls2D
    {
        struct Poly
        {
            public Vector2[] P;
            public float X0, Y0, X1, Y1;
        }

        readonly List<Poly> polys = new List<Poly>();
        readonly float width, height;
        readonly bool hasBounds;

        public Walls2D(MapDef map)
        {
            if (map?.bounds != null && map.bounds.width > 0 && map.bounds.height > 0)
            {
                width = map.bounds.width;
                height = map.bounds.height;
                hasBounds = true;
            }
            if (map?.walls == null) return;
            foreach (WallDef w in map.walls)
            {
                if (w.points == null || w.points.Count < 3) continue;
                var p = new Vector2[w.points.Count];
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                for (int i = 0; i < p.Length; i++)
                {
                    p[i] = new Vector2(w.points[i].x, w.points[i].y);
                    x0 = Mathf.Min(x0, p[i].x); x1 = Mathf.Max(x1, p[i].x);
                    y0 = Mathf.Min(y0, p[i].y); y1 = Mathf.Max(y1, p[i].y);
                }
                polys.Add(new Poly { P = p, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 });
            }
        }

        static bool Inside(float x, float y, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        static Vector2 Closest(Vector2 p, Vector2 a, Vector2 b, out float dist)
        {
            float dx = b.x - a.x, dy = b.y - a.y;
            float len2 = dx * dx + dy * dy;
            float t = len2 == 0f ? 0f : Mathf.Clamp01(((p.x - a.x) * dx + (p.y - a.y) * dy) / len2);
            var q = new Vector2(a.x + t * dx, a.y + t * dy);
            dist = Mathf.Sqrt((p.x - q.x) * (p.x - q.x) + (p.y - q.y) * (p.y - q.y));
            return q;
        }

        /// <summary>sim/geometry.ts resolveCircle: pushes a circle out of every wall and keeps it inside the bounds.</summary>
        public Vector2 Resolve(Vector2 pos, float radius)
        {
            float x = pos.x, y = pos.y;
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                for (int w = 0; w < polys.Count; w++)
                {
                    Poly q = polys[w];
                    if (x < q.X0 - radius || x > q.X1 + radius || y < q.Y0 - radius || y > q.Y1 + radius) continue;
                    Vector2[] pts = q.P;
                    bool inside = Inside(x, y, pts);
                    var p = new Vector2(x, y);
                    float bestD = float.MaxValue;
                    Vector2 best = p;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        Vector2 c = Closest(p, pts[i], pts[(i + 1) % pts.Length], out float d);
                        if (d < bestD)
                        {
                            bestD = d;
                            best = c;
                        }
                    }
                    if (bestD == float.MaxValue) continue;
                    if (inside)
                    {
                        float dx = best.x - x, dy = best.y - y;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d == 0f) d = 1f;
                        x = best.x + dx / d * radius;
                        y = best.y + dy / d * radius;
                        moved = true;
                    }
                    else if (bestD < radius)
                    {
                        float dx = x - best.x, dy = y - best.y;
                        float d = bestD == 0f ? 1f : bestD;
                        x = best.x + dx / d * radius;
                        y = best.y + dy / d * radius;
                        moved = true;
                    }
                }
                if (hasBounds)
                {
                    x = Mathf.Max(radius, Mathf.Min(width - radius, x));
                    y = Mathf.Max(radius, Mathf.Min(height - radius, y));
                }
                if (!moved) break;
            }
            return new Vector2(x, y);
        }

        static float Cross(float ax, float ay, float bx, float by)
        {
            return ax * by - ay * bx;
        }

        static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float rx = b.x - a.x, ry = b.y - a.y, sx = d.x - c.x, sy = d.y - c.y;
            float denom = Cross(rx, ry, sx, sy);
            float qx = c.x - a.x, qy = c.y - a.y;
            if (Mathf.Abs(denom) < 1e-12f)
            {
                if (Mathf.Abs(Cross(qx, qy, rx, ry)) > 1e-12f) return false;
                float rr = rx * rx + ry * ry;
                if (rr == 0f) return false;
                float t0 = (qx * rx + qy * ry) / rr;
                float t1 = t0 + (sx * rx + sy * ry) / rr;
                return Mathf.Max(Mathf.Min(t0, t1), 0f) <= Mathf.Min(Mathf.Max(t0, t1), 1f);
            }
            float t = Cross(qx, qy, sx, sy) / denom;
            float u = Cross(qx, qy, rx, ry) / denom;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        /// <summary>sim/geometry.ts lineOfSight: no wall edge crosses a-b and no smoke circle touches it.</summary>
        public bool LineOfSight(Vector2 a, Vector2 b, List<SmokeState> smokes)
        {
            float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x), minY = Mathf.Min(a.y, b.y), maxY = Mathf.Max(a.y, b.y);
            for (int w = 0; w < polys.Count; w++)
            {
                Poly q = polys[w];
                if (maxX < q.X0 || minX > q.X1 || maxY < q.Y0 || minY > q.Y1) continue;
                Vector2[] pts = q.P;
                for (int i = 0; i < pts.Length; i++)
                    if (SegmentsIntersect(a, b, pts[i], pts[(i + 1) % pts.Length])) return false;
            }
            if (smokes != null)
            {
                for (int i = 0; i < smokes.Count; i++)
                {
                    SmokeState s = smokes[i];
                    if (s?.pos == null) continue;
                    Closest(new Vector2(s.pos.x, s.pos.y), a, b, out float d);
                    if (d < s.radius) return false;
                }
            }
            return true;
        }
    }

    public sealed class PlayerPredictor
    {
        public const float Radius = 0.4f;
        public const float DashDurationS = 0.2f;
        /// <summary>Errors larger than this snap instead of blending.</summary>
        public const float SnapDistance = 1.5f;
        /// <summary>Time constant of the error blend: about 95% gone after 100 ms.</summary>
        public const float BlendTau = 0.033f;

        struct Frame
        {
            public int Tick;
            public Vector2 Pos;
            public Vector2 Move;
            public float Speed;
            public bool Dash;
            public float Yaw;
            /// <summary>Dash state after this tick.</summary>
            public int DashLeft;
            public Vector2 DashDir;
        }

        const int Cap = 256;
        readonly Frame[] ring = new Frame[Cap];

        Walls2D walls;
        float tickRate = 20f;
        float dashDistance = 6f;
        int dashTicks = 4;

        Vector2 pos;
        int tick;
        int dashLeft;
        Vector2 dashDir;
        int ackTick = int.MinValue;
        Vector2 visualOffset;

        public bool Ready { get; private set; }
        /// <summary>The newest predicted position (after the last sent input).</summary>
        public Vector2 TickPos => pos;
        public bool Dashing => dashLeft > 0;
        /// <summary>Last correction size in metres (for the F3 overlay).</summary>
        public float LastError { get; private set; }

        public void Configure(Walls2D w, Rules rules)
        {
            walls = w;
            tickRate = rules != null && rules.tickRate > 0f ? rules.tickRate : 20f;
            dashDistance = rules != null && rules.dashDistanceM > 0f ? rules.dashDistanceM : 6f;
            dashTicks = Mathf.Max(1, Mathf.RoundToInt(DashDurationS * tickRate));
        }

        public void Reset(Vector2 start, int startTick)
        {
            pos = start;
            tick = startTick;
            ackTick = startTick;
            dashLeft = 0;
            dashDir = Vector2.zero;
            visualOffset = Vector2.zero;
            LastError = 0f;
            for (int i = 0; i < Cap; i++) ring[i].Tick = int.MinValue;
            ring[Index(startTick)] = new Frame { Tick = startTick, Pos = start };
            Ready = walls != null;
        }

        public void Stop()
        {
            Ready = false;
        }

        static int Index(int t)
        {
            int i = t % Cap;
            return i < 0 ? i + Cap : i;
        }

        Vector2 Resolve(Vector2 p)
        {
            return walls != null ? walls.Resolve(p, Radius) : p;
        }

        /// <summary>One sim tick of movement, exactly as sim/sim.ts step() does it.</summary>
        Vector2 Step(Vector2 p, Vector2 move, float speed, bool dashReq, float yaw, ref int left, ref Vector2 dir)
        {
            float len = move.magnitude;
            if (len > 1f) move /= len;
            if (dashReq && left <= 0)
            {
                float d = move.magnitude;
                dir = d > 0f ? move / d : new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw));
                left = dashTicks;
            }
            Vector2 from = p;
            p = Resolve(p + move * speed / tickRate);
            if (left > 0)
            {
                p = from;
                float stepLen = dashDistance / (dashTicks * 15f);
                for (int j = 0; j < 15; j++)
                {
                    Vector2 next = p + dir * stepLen;
                    Vector2 res = Resolve(next);
                    if ((next - res).sqrMagnitude > 1e-12f)
                    {
                        left = 0;
                        break;
                    }
                    p = res;
                }
                left = Mathf.Max(0, left - 1);
            }
            return p;
        }

        /// <summary>Applies the input just sent for the next tick and records the predicted result.</summary>
        public void Apply(Vector2 move, float speed, bool dash, float yaw)
        {
            if (!Ready) return;
            pos = Step(pos, move, speed, dash, yaw, ref dashLeft, ref dashDir);
            tick++;
            ring[Index(tick)] = new Frame { Tick = tick, Pos = pos, Move = move, Speed = speed, Dash = dash, Yaw = yaw, DashLeft = dashLeft, DashDir = dashDir };
        }

        /// <summary>
        /// The bridge's state for `serverTick`. Replays the inputs after it from the bridge's position; the change
        /// to the newest prediction becomes a visual offset (blended out), or a snap when large or dead.
        /// </summary>
        public void Reconcile(int serverTick, Vector2 serverPos, bool alive)
        {
            if (!Ready) return;
            if (serverTick < ackTick) return;
            ackTick = serverTick;
            Vector2 before = pos;
            int i = Index(serverTick);
            bool known = ring[i].Tick == serverTick && serverTick <= tick && tick - serverTick < Cap;
            if (!known)
            {
                // A step we have no prediction for (e.g. the first one): adopt it.
                pos = serverPos;
                if (serverTick > tick) tick = serverTick;
                dashLeft = 0;
                ring[Index(serverTick)] = new Frame { Tick = serverTick, Pos = serverPos };
                Finish(before, (serverPos - before).magnitude, alive);
                return;
            }
            float err = (serverPos - ring[i].Pos).magnitude;
            if (err < 1e-4f)
            {
                LastError = err;
                return;
            }
            Vector2 p = serverPos;
            int left = ring[i].DashLeft;
            Vector2 dir = ring[i].DashDir;
            ring[i].Pos = serverPos;
            for (int t = serverTick + 1; t <= tick; t++)
            {
                int k = Index(t);
                Frame f = ring[k];
                p = Step(p, f.Move, f.Speed, f.Dash, f.Yaw, ref left, ref dir);
                f.Pos = p;
                f.DashLeft = left;
                f.DashDir = dir;
                ring[k] = f;
            }
            pos = p;
            dashLeft = left;
            dashDir = dir;
            Finish(before, err, alive);
        }

        void Finish(Vector2 before, float err, bool alive)
        {
            LastError = err;
            if (!alive || err > SnapDistance) visualOffset = Vector2.zero;
            else visualOffset += before - pos;
            if (visualOffset.sqrMagnitude > SnapDistance * SnapDistance) visualOffset = Vector2.zero;
        }

        /// <summary>
        /// Where to draw the eye this frame: the newest prediction plus the movement accumulated since the last send
        /// (`pending`, metres) or the running dash's share of the current tick, plus the fading correction.
        /// </summary>
        public Vector2 Render(Vector2 pending, float tickFraction, float dt)
        {
            visualOffset *= Mathf.Exp(-Mathf.Max(0f, dt) / BlendTau);
            if (visualOffset.sqrMagnitude < 1e-8f) visualOffset = Vector2.zero;
            Vector2 p;
            if (dashLeft > 0)
            {
                float k = Mathf.Clamp01(tickFraction);
                p = Resolve(pos + dashDir * (dashDistance / dashTicks) * k);
            }
            else p = pending.sqrMagnitude > 0f ? Resolve(pos + pending) : pos;
            return p + visualOffset;
        }
    }
}

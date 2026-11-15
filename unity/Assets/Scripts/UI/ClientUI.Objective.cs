// Drill objective marker: showcase drills carry an objective (label + position); a diamond with the
// label and distance shows where it is until the player reaches it.
// Off screen or behind the camera, the marker sits on the screen edge in that direction.
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        const float ObjectiveHideM = 3f;

        void DrawObjective()
        {
            if (Event.current.type != EventType.Repaint) return;
            DrillMsg d = Game.Drill;
            SimState s = Game.State;
            AgentState me = Game.Me;
            Camera cam = Game.Rig != null ? Game.Rig.Cam : null;
            if (Game.InMatch || d == null || d.spec == null || s == null || s.agents == null || me == null || !me.alive || cam == null) return;
            DrillObjective obj = d.spec.objective;
            if (obj == null || obj.pos == null || string.IsNullOrEmpty(obj.label)) return;
            float dx = obj.pos.x - me.pos.x, dy = obj.pos.y - me.pos.y;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist < ObjectiveHideM) return;

            Vector3 sp = cam.WorldToScreenPoint(new Vector3(obj.pos.x, 1.4f, obj.pos.y));
            bool behind = sp.z < 0f;
            float vx = sp.x / scale, vy = (Screen.height - sp.y) / scale;
            if (behind) { vx = W - vx; vy = H * 0.5f; }
            const float m = 70f;
            bool edge = behind || vx < m || vx > W - m || vy < m || vy > H - m;
            vx = Mathf.Clamp(vx, m, W - m);
            vy = Mathf.Clamp(vy, m, H - m);

            Color ac = Palette.Bad;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
            Matrix4x4 old = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, new Vector2(vx, vy) * scale);
            Fill(new Rect(vx - 9f, vy - 9f, 18f, 18f), new Color(0f, 0f, 0f, 0.55f));
            Fill(new Rect(vx - 6f, vy - 6f, 12f, 12f), new Color(ac.r, ac.g, ac.b, pulse));
            GUI.matrix = old;

            string label = obj.label + "  " + Mathf.RoundToInt(dist) + " m";
            float lw = TextW(label, hudLine) + 16f;
            float lx = Mathf.Clamp(vx - lw / 2f, 8f, W - lw - 8f);
            Shadowed(new Rect(lx, vy + 14f, lw, 20f), label, hudLine, Color.white);
        }
    }
}

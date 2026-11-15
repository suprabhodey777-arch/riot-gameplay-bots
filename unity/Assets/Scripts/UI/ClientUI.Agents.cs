// Agent select: one tall card per agent with the agent's figure turning on a
// lit turntable (FigureStage), the role glyph, the coach's badges, the kit and
// the lock-in button. The hovered (or keyboard-focused) card lifts and its
// figure takes the hero light. GUI.* calls only.
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        void DrawAgentSelect()
        {
            float now = Time.unscaledTime;
            string mainPick = Game.AdviceMain, practice = Game.AdvicePractice;
            bool advice = mainPick != null || practice != null;
            string current = Game.PickedAgentId ?? practice ?? Game.RememberedAgent;
            float x0 = 56f, top = 44f;
            float cy = Header(x0, top, W - 2f * x0 - 200f, "Practice  //  agent select", "Select your agent", advice
                ? "The coach read your matches: one pick suits how you play, one trains what you avoid."
                : "Your kit for every drill. A drill that trains a tool you lack lends you an agent who has it.");

            int n = Agents.All.Count;
            float gap = 16f;
            float cw = (W - 2f * x0 - gap * (n - 1)) / Mathf.Max(1, n);
            float ch = H - cy - 104f;
            FigureSpec[] specs = Figures("agents" + n, n, i =>
            {
                AgentInfo a = Agents.All[i];
                return new FigureSpec { Look = a.Id, Accent = a.Color, Team = Settings.Ally, Weapon = DefaultWeapon(a.Id) };
            });
            Game.Stage?.Use("agents" + n, specs);
            int hero = -1;
            bool idle = Idle;
            for (int i = 0; i < n; i++)
            {
                AgentInfo a = Agents.All[i];
                string id = a.Id;
                bool sel = id == current;
                if (sel && hero < 0) hero = i;
                float ik = Theme.Ease((now - screenAt - i * 0.06f) / 0.35f);
                var hit = new Rect(x0 + i * (cw + gap), cy, cw, ch);
                int btnKey = StableKey("lock:" + id);
                bool hot = idle && (keyNav ? focusId == btnKey : Hover(hit));
                float h = Anim(6000 + i, hot);
                if (hot) hero = i;
                var r = new Rect(hit.x, hit.y + (1f - ik) * 30f - h * 8f, hit.width, hit.height);
                Color oldC = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, oldC.a * ik);
                Shadow(r, 20f + 12f * h, 0.5f + 0.2f * h);
                Glass(r);
                PlateLine(r, new Color(a.Color.r, a.Color.g, a.Color.b, sel ? 1f : 0.15f + 0.6f * h));
                Fill(new Rect(r.x + Theme.Cut, r.y, r.width - Theme.Cut - 4f, sel ? 4f : 3f), a.Color);

                var fr = new Rect(r.x + 10f, r.y + 12f, r.width - 20f, r.height * 0.5f);
                Fill(fr, new Color(a.Color.r * 0.12f, a.Color.g * 0.12f, a.Color.b * 0.12f, 0.95f));
                DrawFigure(fr, i);
                Tex(new Rect(fr.xMax - 36f, fr.y + 10f, 26f, 26f), UiTex.Role(a.Role), new Color(a.Color.r, a.Color.g, a.Color.b, 0.9f));
                float by = fr.y + 10f;
                if (id == mainPick)
                {
                    Chip(fr.x + 10f, by, "YOUR MAIN", Theme.Darken(a.Color, 0.45f), Color.white);
                    by += 28f;
                }
                if (id == practice) Chip(fr.x + 10f, by, "PRACTISE YOUR WEAKNESS", Settings.Accent, Theme.OnAccent);
                if (sel)
                {
                    var lk = new Rect(fr.x, fr.yMax - 28f, fr.width, 28f);
                    Fill(lk, new Color(a.Color.r, a.Color.g, a.Color.b, 0.9f));
                    Lbl(lk, "L O C K E D   I N", centerSmall, Theme.OnAccent);
                }

                float y = fr.yMax + 12f;
                Condensed(new Rect(r.x + 16f, y, r.width - 32f, 46f), Upper(a.Name), personaName, a.Color, 0.8f, true);
                y += 46f;
                Lbl(new Rect(r.x + 16f, y, r.width - 32f, 18f), Spaced(a.Role ?? ""), tinyCaps, Theme.Muted);
                y += 26f;
                float ix = r.x + 16f;
                foreach (Ability ab in Agents.AllAbilities)
                {
                    int c = a.Kit != null ? a.Kit.Get(ab) : 0;
                    if (c <= 0) continue;
                    if (ix + 110f > r.xMax - 8f)
                    {
                        ix = r.x + 16f;
                        y += 44f;
                    }
                    var slot = new Rect(ix, y, 36f, 36f);
                    Plate(slot, new Color(0.02f, 0.025f, 0.04f, 0.85f));
                    PlateLine(slot, new Color(1f, 1f, 1f, 0.12f));
                    Icon(new Rect(slot.x + 6f, slot.y + 6f, 24f, 24f), ab, Agents.AbilityColor(ab));
                    Lbl(new Rect(slot.xMax + 6f, slot.y, 72f, 18f), Upper(Agents.AbilityName(ab)), tinyCaps, Theme.Ink);
                    Lbl(new Rect(slot.xMax + 6f, slot.y + 18f, 72f, 18f), "\u00d7" + c + "   " + Agents.KeyLabel(ab), tinyCaps, Theme.Muted);
                    ix += 116f;
                }
                y += 48f;
                float blurbH = r.yMax - 66f - y;
                if (blurbH > 20f) Lbl(new Rect(r.x + 16f, y, r.width - 32f, blurbH), a.Blurb ?? "", tipStyle, Theme.Muted);
                GUI.color = oldC;
                if (GButton(new Rect(r.x + 14f, r.yMax - 56f, r.width - 28f, 44f), sel ? "Play " + a.Name : "Lock in " + a.Name, sel ? 1 : 0, idle, btnKey))
                    Game.Do(() => Game.PickAgent(id));
            }
            if (Game.Stage != null) Game.Stage.Hero = hero;

            float fy = H - 40f - Theme.ButtonH;
            if (Game.V2 && GButton(new Rect(x0, fy, 200f, Theme.ButtonH), "Back to maps", 2, idle, 0, false, UiTex.Glyph("back"))) Game.Do(Game.OpenMapSelect);
            string keep = Game.PickedAgentId != null ? "Keep " + Agents.NameOf(Game.PickedAgentId) : "Let the drills decide";
            if (GButton(new Rect(x0 + 212f, fy, 260f, Theme.ButtonH), keep, 0, idle)) Game.Do(Game.SkipAgent);
            if (Game.PickedAgentId == null && practice != null)
            {
                string pid = practice;
                if (GButton(new Rect(W - x0 - 400f, fy, 400f, Theme.ButtonH), "Play " + Agents.NameOf(pid) + "  (recommended)", 1, idle, 0, false, UiTex.Glyph("play"))) Game.Do(() => Game.PickAgent(pid));
            }
            WaitingLine(new Rect(x0 + 490f, fy + 14f, 300f, 20f));
        }
    }
}

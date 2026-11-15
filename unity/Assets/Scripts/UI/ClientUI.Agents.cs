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
            float x0 = 80f, top = 56f;
            float cy = Header(x0, top, W - 2f * x0 - 200f, "Practice", "Select your agent", advice
                ? "The coach read your matches: one pick suits how you play, one trains what you avoid."
                : "Your kit for every drill. A drill that trains a tool you lack lends you an agent who has it.");

            int n = Agents.All.Count;
            float gap = 16f;
            float cw = (W - 2f * x0 - gap * (n - 1)) / Mathf.Max(1, n);
            float ch = H - cy - 112f;
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
                var r = new Rect(hit.x, hit.y + (1f - ik) * 16f - h * 4f, hit.width, hit.height);
                Color oldC = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, oldC.a * ik);
                // A flat card: hairline, the agent's colour as a 2 px top edge only when it is the pick.
                Plate(r, Color.Lerp(Theme.PanelC, Theme.CardC, h));
                PlateLine(r, new Color(1f, 1f, 1f, 0.10f + 0.14f * h));
                if (sel) Fill(new Rect(r.x, r.y, r.width, 2f), a.Color);

                var fr = new Rect(r.x + 1f, r.y + (sel ? 2f : 1f), r.width - 2f, r.height * 0.5f);
                Fill(fr, Theme.StageC);
                DrawFigure(fr, i);
                Tex(new Rect(fr.xMax - 40f, fr.y + 14f, 24f, 24f), UiTex.Role(a.Role), new Color(a.Color.r, a.Color.g, a.Color.b, 0.9f));
                float by = fr.y + 14f;
                if (id == mainPick)
                {
                    Chip(fr.x + 14f, by, "YOUR MAIN", Theme.Darken(a.Color, 0.45f), Theme.Ink);
                    by += 28f;
                }
                if (id == practice)
                {
                    Chip(fr.x + 14f, by, "PRACTISE YOUR WEAKNESS", new Color(1f, 1f, 1f, 0.12f), Theme.Ink);
                    by += 28f;
                }
                if (sel)
                {
                    Chip(fr.x + 14f, by, "LOCKED IN", a.Color, Theme.OnAccent);
                }

                float px = r.x + 20f, pw = r.width - 40f;
                float y = fr.yMax + 16f;
                Condensed(new Rect(px, y, pw, 44f), Upper(a.Name), personaName, Theme.Ink);
                y += 44f;
                Lbl(new Rect(px, y, pw, 18f), Upper(a.Role ?? ""), tinyCaps, a.Color);
                y += 30f;
                // The kit: icon and count, no boxes.
                float ix = px;
                foreach (Ability ab in Agents.AllAbilities)
                {
                    int c = a.Kit != null ? a.Kit.Get(ab) : 0;
                    if (c <= 0) continue;
                    string label = Upper(Agents.AbilityName(ab)) + " \u00d7" + c;
                    float lw = TextW(label, tinyCaps);
                    if (ix + 30f + lw > r.xMax - 16f && ix > px)
                    {
                        ix = px;
                        y += 30f;
                    }
                    Icon(new Rect(ix, y, 22f, 22f), ab, Agents.AbilityColor(ab));
                    Lbl(new Rect(ix + 28f, y + 2f, lw + 4f, 18f), label, tinyCaps, Theme.Muted);
                    ix += 28f + lw + 20f;
                }
                y += 38f;
                Fill(new Rect(px, y - 6f, pw, 1f), Theme.Line);
                float blurbH = r.yMax - 72f - y;
                if (blurbH > 20f)
                {
                    BeginClip(new Rect(px, y + 4f, pw, blurbH));
                    Lbl(new Rect(0f, 0f, pw, blurbH), a.Blurb ?? "", tipStyle, Theme.Muted);
                    EndClip();
                }
                GUI.color = oldC;
                if (GButton(new Rect(r.x + 20f, r.yMax - 64f, r.width - 40f, 44f), sel ? "Play " + a.Name : "Lock in " + a.Name, sel ? 1 : 0, idle, btnKey))
                    Game.Do(() => Game.PickAgent(id));
            }
            if (Game.Stage != null) Game.Stage.Hero = hero;

            float fy = H - 48f - Theme.ButtonH;
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

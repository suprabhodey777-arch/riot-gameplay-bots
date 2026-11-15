// Agent select in the broadcast look: one full-height card per agent with the
// agent's figure turning on a lit turntable (FigureStage), the role glyph, the
// coach's badges, the kit and the lock-in button. GUI.* calls only.
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        void DrawAgentSelect()
        {
            EnsureStreamerStyles();
            float now = Time.unscaledTime;
            string mainPick = Game.AdviceMain, practice = Game.AdvicePractice;
            bool advice = mainPick != null || practice != null;
            string current = Game.PickedAgentId ?? practice ?? Game.RememberedAgent;
            float x0 = 50f, top = 36f;
            Kicker(new Rect(x0, top, 500f, 16f), "Agent select", Settings.Accent);
            Condensed(new Rect(x0, top + 16f, 900f, 60f), Spaced("Select your agent"), displayL, Theme.Ink, 0.78f);
            Neon(new Rect(x0, top + 80f, 120f, 3f), Settings.Accent, Intro01);
            Lbl(new Rect(x0 + 140f, top + 72f, W - x0 - 200f, 20f), advice
                ? "The coach read your matches: one pick suits how you play, one trains what you avoid."
                : "Your kit for every drill. A drill that trains a tool you lack lends you an agent who has it.", small, Theme.Muted);

            int n = Agents.All.Count;
            float gap = 14f;
            float cw = (W - 2f * x0 - gap * (n - 1)) / Mathf.Max(1, n);
            float cy = top + 104f, ch = H - cy - 92f;
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
                bool hot = idle && Hover(hit);
                float h = HoverAnim(6000 + i, hot);
                if (hot) hero = i;
                var r = new Rect(hit.x, hit.y + (1f - ik) * 30f - h * 8f, hit.width, hit.height);
                Color oldC = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, oldC.a * ik);
                Glass(r);
                PlateLine(r, new Color(a.Color.r, a.Color.g, a.Color.b, sel ? 1f : 0.15f + 0.6f * h));
                Fill(new Rect(r.x + Theme.Cut, r.y, r.width - Theme.Cut - 4f, sel ? 4f : 3f), a.Color);

                var fr = new Rect(r.x + 8f, r.y + 10f, r.width - 16f, r.height * 0.52f);
                Fill(fr, new Color(a.Color.r * 0.13f, a.Color.g * 0.13f, a.Color.b * 0.13f, 0.95f));
                DrawFigure(fr, i);
                Scanlines(fr, 0.14f);
                Tex(new Rect(fr.xMax - 34f, fr.y + 10f, 24f, 24f), UiTex.Role(a.Role), new Color(a.Color.r, a.Color.g, a.Color.b, 0.9f));
                float by = fr.y + 10f;
                if (id == mainPick)
                {
                    var b = new Rect(fr.x + 8f, by, 96f, 18f);
                    Plate(b, Theme.Darken(a.Color, 0.35f));
                    Lbl(b, "YOUR MAIN", bugSmall, Color.white);
                    by += 22f;
                }
                if (id == practice)
                {
                    var b = new Rect(fr.x + 8f, by, 150f, 18f);
                    Plate(b, Settings.Accent);
                    Lbl(b, "PRACTISE YOUR WEAKNESS", bugSmall, new Color(0.04f, 0.04f, 0.05f));
                }
                if (sel)
                {
                    var lk = new Rect(fr.x, fr.yMax - 24f, fr.width, 24f);
                    Fill(lk, new Color(a.Color.r, a.Color.g, a.Color.b, 0.85f));
                    Lbl(lk, "L O C K E D   I N", bugSmall, new Color(0.04f, 0.04f, 0.05f));
                }

                float y = fr.yMax + 10f;
                Condensed(new Rect(r.x + 14f, y, r.width - 28f, 46f), Upper(a.Name), personaName, a.Color, 0.8f, true);
                y += 44f;
                Lbl(new Rect(r.x + 14f, y, r.width - 28f, 14f), Spaced(a.Role ?? ""), tinyCaps, Theme.Muted);
                y += 22f;
                float ix = r.x + 14f;
                foreach (Ability ab in Agents.AllAbilities)
                {
                    int c = a.Kit != null ? a.Kit.Get(ab) : 0;
                    if (c <= 0) continue;
                    var slot = new Rect(ix, y, 34f, 34f);
                    Plate(slot, new Color(0.02f, 0.025f, 0.04f, 0.8f));
                    PlateLine(slot, new Color(1f, 1f, 1f, 0.12f));
                    Icon(new Rect(slot.x + 6f, slot.y + 6f, 22f, 22f), ab, Agents.AbilityColor(ab));
                    Lbl(new Rect(slot.xMax + 4f, slot.y, 70f, 16f), Upper(Agents.AbilityName(ab)), tinyCaps, Theme.Ink);
                    Lbl(new Rect(slot.xMax + 4f, slot.y + 16f, 70f, 16f), "\u00d7" + c + "  " + Agents.KeyLabel(ab), tinyCaps, Theme.Muted);
                    ix += 112f;
                    if (ix + 100f > r.xMax) { ix = r.x + 14f; y += 40f; }
                }
                y += 44f;
                Lbl(new Rect(r.x + 14f, y, r.width - 28f, r.yMax - 60f - y), a.Blurb ?? "", tipStyle, Theme.Muted);
                GUI.color = oldC;
                if (GButton(new Rect(r.x + 12f, r.yMax - 52f, r.width - 24f, 40f), sel ? "Play " + a.Name + "  \u2713" : "Lock in " + a.Name, sel ? 1 : 0, idle))
                    Game.Do(() => Game.PickAgent(id));
            }
            if (Game.Stage != null) Game.Stage.Hero = hero;

            float fy = H - 70f;
            if (Game.V2 && GButton(new Rect(x0, fy, 180f, 44f), "Back to maps", 2, idle)) Game.Do(Game.OpenMapSelect);
            string keep = Game.PickedAgentId != null ? "Keep " + Agents.NameOf(Game.PickedAgentId) : "Let the drills decide";
            if (GButton(new Rect(x0 + 190f, fy, 240f, 44f), keep, 0, idle)) Game.Do(Game.SkipAgent);
            if (Game.PickedAgentId == null && practice != null)
            {
                string pid = practice;
                if (GButton(new Rect(W - x0 - 380f, fy, 380f, 44f), "Play " + Agents.NameOf(pid) + "  (recommended practice)", 1, idle)) Game.Do(() => Game.PickAgent(pid));
            }
            if (Game.WaitingFor != null) Lbl(new Rect(x0 + 450f, fy + 12f, 300f, 20f), "Waiting for the bridge...", small, Theme.Muted);
        }
    }
}

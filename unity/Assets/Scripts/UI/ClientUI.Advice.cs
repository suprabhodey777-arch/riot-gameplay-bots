// "Recommended for you" on the report screen, from report.agentAdvice: the main
// pick and the practice pick as cards, a playstyle readout and the fit bars for
// every agent. Three blocks of the report's paged list (ClientUI.Practice.cs),
// each measured first and then drawn. Hidden when the bridge sends no advice.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        AgentAdvice adviceFor;
        readonly List<string> adviceStats = new List<string>();
        readonly List<string> adviceStatLabels = new List<string>();
        string mainPlays = "", practicePlays = "", mainKit = "", practiceKit = "";

        static string PlaysLike(AdviceAgent a)
        {
            if (a?.valorantLike == null || a.valorantLike.Count == 0) return "";
            return "Plays like in VALORANT: " + string.Join(", ", a.valorantLike);
        }

        void CacheAdvice(AgentAdvice adv)
        {
            if (ReferenceEquals(adv, adviceFor)) return;
            adviceFor = adv;
            adviceStats.Clear();
            adviceStatLabels.Clear();
            mainPlays = PlaysLike(adv.main?.agent);
            practicePlays = PlaysLike(adv.practice?.agent);
            mainKit = adv.main?.agent?.KitText() ?? "";
            practiceKit = adv.practice?.agent?.KitText() ?? "";
            Playstyle p = adv.playstyle;
            if (p == null) return;
            void Stat(string label, string value)
            {
                adviceStatLabels.Add(label);
                adviceStats.Add(value);
            }
            Stat("Rounds", p.rounds.ToString());
            Stat("Takes first duel", Mathf.RoundToInt(p.firstDuelShare * 100f) + "%");
            Stat("Wins first duel", Mathf.RoundToInt(p.firstDuelWinRate * 100f) + "%");
            Stat("Kill distance", p.meanKillDistanceM.ToString("0.0") + " m");
            Stat("Smokes / round", p.smokesPerRound.ToString("0.0"));
            Stat("On objective", Mathf.RoundToInt(p.objectiveShare * 100f) + "%");
            Stat("K/D", p.kills + " / " + p.deaths);
        }

        // ---- the two picks ---------------------------------------------------------------------------
        bool AdviceSideBySide(float w) => w >= 560f;

        float AdviceCardHeight(AdvicePick pick, string plays, float cw)
        {
            if (pick?.agent == null) return 0f;
            float iw = cw - 40f;
            return 20f + 24f + 10f + 32f + 22f + 8f + TextH(pick.why, body, iw) + (plays.Length > 0 ? 4f + TextH(plays, small, iw) : 0f) + 16f + Theme.ButtonSmallH + 20f;
        }

        float AdvicePicksHeight(AgentAdvice adv, float w)
        {
            bool side = AdviceSideBySide(w);
            float cw = side ? (w - 16f) / 2f : w;
            float a = AdviceCardHeight(adv.main, mainPlays, cw), b = AdviceCardHeight(adv.practice, practicePlays, cw);
            return 34f + (side ? Mathf.Max(a, b) : a + (a > 0f && b > 0f ? 16f : 0f) + b);
        }

        void DrawAdvicePicks(AgentAdvice adv, Rect r)
        {
            float y = SectionLabel(r.x, r.y + 4f, r.width, "Recommended for you");
            bool side = AdviceSideBySide(r.width);
            float cw = side ? (r.width - 16f) / 2f : r.width;
            float a = AdviceCardHeight(adv.main, mainPlays, cw), b = AdviceCardHeight(adv.practice, practicePlays, cw);
            float h = side ? Mathf.Max(a, b) : 0f;
            float x = r.x;
            if (adv.main?.agent != null)
            {
                AdviceCard(new Rect(x, y, cw, side ? h : a), adv.main, "YOUR MAIN", "Suits how you already play.", mainKit, mainPlays, false);
                if (side) x += cw + 16f;
                else y += a + 16f;
            }
            if (adv.practice?.agent != null)
                AdviceCard(new Rect(x, y, cw, side ? h : b), adv.practice, "PRACTISE YOUR WEAKNESS", "Trains the part of the game you avoid.", practiceKit, practicePlays, true);
        }

        void AdviceCard(Rect r, AdvicePick pick, string badgeText, string tagline, string kit, string plays, bool practiceCard)
        {
            AdviceAgent a = pick.agent;
            string id = a.id;
            Color ac = Agents.ColorOf(id);
            Card(r, ac);
            float x = r.x + 20f, w = r.width - 40f, y = r.y + 20f;
            float bw = practiceCard
                ? Chip(x, y, badgeText, Settings.Accent, Theme.OnAccent, 24f)
                : Chip(x, y, badgeText, Theme.Darken(ac, 0.45f), Color.white, 24f);
            Lbl(new Rect(x + bw + 10f, y, w - bw - 10f, 24f), tagline, smallMid, Theme.Muted);
            y += 34f;
            string name = a.name ?? Agents.NameOf(id);
            Condensed(new Rect(x, y, w, 32f), Upper(name), cardTitleL, ac, 0.9f, true);
            y += 32f;
            Lbl(new Rect(x, y, w, 20f), Spaced((a.role ?? Agents.Get(id)?.Role ?? "") ) + "   " + Upper(kit), tinyCaps, Theme.Muted);
            y += 30f;
            y = Para(x, y, w, pick.why, body, Theme.Ink);
            if (plays.Length > 0) Para(x, y + 4f, w, plays, small, Theme.Faint);
            bool enabled = Idle && Game.V2 && Agents.Get(id) != null;
            bool picked = Game.PickedAgentId == id;
            string label = picked ? "Playing " + name : "Play " + name;
            if (GButton(new Rect(x, r.yMax - 20f - Theme.ButtonSmallH, w, Theme.ButtonSmallH), label, practiceCard && !picked ? 1 : 0, enabled, StableKey("advice:" + id), picked))
                Game.Do(() => Game.PickAgent(id));
        }

        // ---- playstyle readout ------------------------------------------------------------------------
        const float StatW = 132f, StatH = 54f;

        int StatsPerRow(float w) => Mathf.Max(1, Mathf.FloorToInt((w - 40f) / StatW));

        float AdviceStyleHeight(float w)
        {
            int rows = Mathf.CeilToInt(adviceStats.Count / (float)StatsPerRow(w));
            return 20f + 22f + rows * StatH + 14f;
        }

        void DrawAdviceStyle(Rect r)
        {
            Card(r, new Color(0f, 0f, 0f, 0f));
            Lbl(new Rect(r.x + 20f, r.y + 16f, r.width - 40f, 18f), Spaced("Your playstyle"), tinyCaps, Theme.Muted);
            int per = StatsPerRow(r.width);
            for (int i = 0; i < adviceStats.Count; i++)
            {
                float sx = r.x + 20f + (i % per) * StatW, sy = r.y + 42f + (i / per) * StatH;
                Lbl(new Rect(sx, sy, StatW - 8f, 28f), adviceStats[i], cardTitleL, Theme.Ink);
                Lbl(new Rect(sx, sy + 28f, StatW - 8f, 18f), Upper(adviceStatLabels[i]), tinyCaps, Theme.Faint);
            }
        }

        // ---- fit for every agent ----------------------------------------------------------------------
        float AdviceFitsHeight(AgentAdvice adv)
        {
            int n = 0;
            foreach (AdviceFit f in adv.ranked) if (f?.agent != null) n++;
            return 20f + 26f + n * 30f + 10f;
        }

        void DrawAdviceFits(AgentAdvice adv, Rect r)
        {
            Card(r, new Color(0f, 0f, 0f, 0f));
            Lbl(new Rect(r.x + 20f, r.y + 16f, r.width - 40f, 18f), Spaced("Fit for every agent"), tinyCaps, Theme.Muted);
            float y = r.y + 44f;
            float nameW = 110f, roleW = 110f, pctW = 52f, badgeW = 96f;
            float barX = r.x + 20f + nameW + roleW;
            float barW = Mathf.Max(40f, r.xMax - 20f - badgeW - pctW - 12f - barX);
            foreach (AdviceFit f in adv.ranked)
            {
                if (f?.agent == null) continue;
                string id = f.agent.id;
                Color ac = Agents.ColorOf(id);
                Lbl(new Rect(r.x + 20f, y, nameW, 26f), f.agent.name ?? Agents.NameOf(id), bodyBoldMid, ac);
                Lbl(new Rect(r.x + 20f + nameW, y, roleW, 26f), Upper(f.agent.role ?? Agents.Get(id)?.Role ?? ""), tinyCaps, Theme.Faint);
                Meter(new Rect(barX, y + 10f, barW, 6f), f.fit, ac);
                Lbl(new Rect(barX + barW + 10f, y, pctW, 26f), Theme.Percent(Mathf.RoundToInt(Mathf.Clamp01(f.fit) * 100f)), bodyBoldMid, Theme.Ink);
                float bx = r.xMax - 20f - badgeW;
                if (id == adv.main?.agent?.id) Chip(bx, y + 2f, "MAIN", Theme.Darken(ac, 0.45f), Color.white);
                else if (id == adv.practice?.agent?.id) Chip(bx, y + 2f, "PRACTICE", Settings.Accent, Theme.OnAccent);
                y += 30f;
            }
        }
    }
}

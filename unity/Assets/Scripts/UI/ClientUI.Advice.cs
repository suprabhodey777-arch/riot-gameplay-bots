// "Recommended for you" on the report screen, from report.agentAdvice: the main
// pick and the practice pick as cards, a playstyle readout and the fit bars for
// every agent. Hidden when the bridge sends no advice (an older bridge).
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
            Stat("ROUNDS", p.rounds.ToString());
            Stat("TAKES FIRST DUEL", Mathf.RoundToInt(p.firstDuelShare * 100f) + "%");
            Stat("WINS FIRST DUEL", Mathf.RoundToInt(p.firstDuelWinRate * 100f) + "%");
            Stat("KILL DISTANCE", p.meanKillDistanceM.ToString("0.0") + " m");
            Stat("SMOKES / ROUND", p.smokesPerRound.ToString("0.0"));
            Stat("ON OBJECTIVE", Mathf.RoundToInt(p.objectiveShare * 100f) + "%");
            Stat("K / D", p.kills + " / " + p.deaths);
        }

        void DrawAdvice(AgentAdvice adv)
        {
            if (adv.main?.agent == null && adv.practice?.agent == null) return;
            CacheAdvice(adv);
            SectionHeader("Recommended for you");
            bool wide = W > 900f;
            if (wide) GUILayout.BeginHorizontal();
            if (adv.main?.agent != null) AdviceCard(adv.main, "YOUR MAIN", "Suits how you already play.", mainKit, mainPlays, false);
            if (wide) GUILayout.Space(Theme.S3);
            if (adv.practice?.agent != null) AdviceCard(adv.practice, "PRACTISE YOUR WEAKNESS", "Trains the part of the game you avoid.", practiceKit, practicePlays, true);
            if (wide) GUILayout.EndHorizontal();

            if (adviceStats.Count > 0)
            {
                GUILayout.BeginVertical(card);
                GUILayout.Label("YOUR PLAYSTYLE", caption);
                GUILayout.Space(Theme.S1);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < adviceStats.Count; i++)
                {
                    GUILayout.BeginVertical(GUILayout.MinWidth(96f));
                    GUILayout.Label(adviceStats[i], h3);
                    GUILayout.Label(adviceStatLabels[i], caption);
                    GUILayout.EndVertical();
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            if (adv.ranked != null && adv.ranked.Count > 0)
            {
                GUILayout.BeginVertical(card);
                GUILayout.Label("FIT FOR EVERY AGENT", caption);
                GUILayout.Space(Theme.S1);
                foreach (AdviceFit f in adv.ranked)
                {
                    if (f?.agent == null) continue;
                    string id = f.agent.id;
                    Color ac = Agents.ColorOf(id);
                    GUILayout.BeginHorizontal(GUILayout.Height(24f));
                    Color old = GUI.contentColor;
                    GUI.contentColor = ac;
                    GUILayout.Label(f.agent.name ?? Agents.NameOf(id), bodyBold, GUILayout.Width(90f));
                    GUI.contentColor = old;
                    GUILayout.Label((f.agent.role ?? Agents.Get(id)?.Role ?? "").ToUpperInvariant(), caption, GUILayout.Width(90f));
                    GUILayout.BeginVertical();
                    GUILayout.Space(7f);
                    Bar(f.fit, ac, 10f);
                    GUILayout.EndVertical();
                    GUILayout.Space(Theme.S2);
                    GUILayout.Label(Theme.Percent(Mathf.RoundToInt(Mathf.Clamp01(f.fit) * 100f)), bodyBold, GUILayout.Width(48f));
                    if (id == adv.main?.agent?.id) Badge("MAIN", Theme.Darken(ac, 0.35f), Color.white);
                    else if (id == adv.practice?.agent?.id) Badge("PRACTICE", Settings.Accent, new Color(0.06f, 0.06f, 0.07f));
                    else GUILayout.Space(70f);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }
            GUILayout.Space(Theme.S2);
        }

        void AdviceCard(AdvicePick pick, string badgeText, string tagline, string kit, string plays, bool practiceCard)
        {
            AdviceAgent a = pick.agent;
            string id = a.id;
            Color ac = Agents.ColorOf(id);
            GUILayout.BeginVertical(card, GUILayout.ExpandWidth(true), GUILayout.MinHeight(190f));
            Rect band = GUILayoutUtility.GetRect(10f, 6f, GUILayout.ExpandWidth(true));
            Round(band, ac);
            GUILayout.Space(Theme.S2);
            GUILayout.BeginHorizontal();
            if (practiceCard) Badge(badgeText, Settings.Accent, new Color(0.06f, 0.06f, 0.07f));
            else Badge(badgeText, Theme.Darken(ac, 0.35f), Color.white);
            GUILayout.Space(Theme.S2);
            GUILayout.Label(tagline, small);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(Theme.S1);
            GUILayout.BeginHorizontal();
            Color old = GUI.contentColor;
            GUI.contentColor = ac;
            GUILayout.Label(a.name ?? Agents.NameOf(id), h2, GUILayout.ExpandWidth(false));
            GUI.contentColor = old;
            GUILayout.Space(Theme.S2);
            GUILayout.BeginVertical();
            GUILayout.Space(6f);
            GUILayout.Label(((a.role ?? Agents.Get(id)?.Role ?? "") + "   " + kit).ToUpperInvariant(), caption);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(pick.why)) GUILayout.Label(pick.why, body);
            if (plays.Length > 0) GUILayout.Label(plays, small);
            GUILayout.FlexibleSpace();
            GUI.enabled = Idle && Game.V2 && Agents.Get(id) != null;
            bool picked = Game.PickedAgentId == id;
            if (Btn(picked ? "Playing " + (a.name ?? Agents.NameOf(id)) + "  \u2713" : "Play " + (a.name ?? Agents.NameOf(id)), picked ? chosenButton : practiceCard ? primary : button, GUILayout.Height(38f)))
                Game.Do(() => Game.PickAgent(id));
            GUI.enabled = true;
            GUILayout.EndVertical();
        }
    }
}

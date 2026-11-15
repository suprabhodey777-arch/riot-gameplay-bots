// The practice flow's screens: connect, sample player, map select, the coach
// report, the drill intro card, the pause menu and the drill result. All are
// laid out on the 900 px virtual grid (W varies with the window's aspect, H is
// always 900), with GUI.* calls only, so their text is crisp and nothing
// depends on IMGUI's layout pass. Long lists (the report's findings and
// rehearsals) page by whole cards instead of clipping mid-card: the wheel or
// the "more" buttons move one card at a time.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        const float Pad = 32f;

        // ---- (1) connect --------------------------------------------------------------------------
        void DrawConnect()
        {
            Rect r = Centered(680f, 420f);
            Frame(r, "Bridge link");
            Glass(r);
            float x = r.x + Pad, w = r.width - 2f * Pad;
            float y = Header(x, r.y + 28f, w, null, "ValoDrill", "Tactical coaching client. The bridge runs the sim, the bots and the coach; this window is the eyes and hands.");
            BridgeClient c = Game.Client;
            string where = c.Host + ":" + c.Port;
            Color dot;
            string status;
            switch (Game.Link)
            {
                case LinkState.Connecting:
                case LinkState.Idle:
                    dot = Palette.Warn;
                    status = "Connecting to the bridge at " + where + "...";
                    break;
                case LinkState.Connected:
                    dot = Palette.Ok;
                    status = "Connected to " + where + ". Waiting for the bridge's hello...";
                    break;
                default:
                    dot = Palette.Bad;
                    status = "Cannot reach the bridge at " + where + ".";
                    break;
            }
            var sr = new Rect(x, y, w, 52f);
            Plate(sr, Theme.WellC);
            float pulse = Game.Link == LinkState.Connecting ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f) : 1f;
            Plate(new Rect(sr.x + 16f, sr.center.y - 5f, 10f, 10f), new Color(dot.r, dot.g, dot.b, pulse));
            Lbl(new Rect(sr.x + 38f, sr.y, sr.width - 50f, sr.height), status, bodyBoldMid, Theme.Ink);
            y = sr.yMax + Theme.S4;
            if (Game.Link == LinkState.Failed || Game.Link == LinkState.Closed)
            {
                Lbl(new Rect(x, y, w, 20f), "Start the bridge from the repository root, then press Reconnect:", small, Theme.Muted);
                y += 24f;
                Lbl(new Rect(x, y, w, 28f), "npm run bridge", monoBig, Theme.Ink);
                y += 32f;
                Lbl(new Rect(x, y, w, 20f), "(" + (string.IsNullOrEmpty(Game.LinkError) ? "no answer" : Game.LinkError) + ")", small, Theme.Faint);
            }
            bool can = Game.Link != LinkState.Connecting;
            if (GButton(new Rect(r.xMax - Pad - 240f, r.yMax - Pad - Theme.ButtonH, 240f, Theme.ButtonH), "Reconnect", 1, can, 0, false, null, "Enter")) Game.Do(Game.ConnectNow);
        }

        // ---- (2) choose a sample player ---------------------------------------------------------------
        void DrawChoose()
        {
            Rect r = Centered(820f, 600f);
            Frame(r, "Practice");
            Glass(r);
            float x = r.x + Pad, w = r.width - 2f * Pad;
            float y = Header(x, r.y + 28f, w, null, "Whose history?", "Pick a sample player. The bridge reads their matches, finds the situation they lose most and builds drills from it.");
            float gap = Theme.S4;
            float cw = (w - gap) / 2f, ch = 118f;
            bool idle = Idle;
            for (int i = 0; i < GameController.Samples.Length; i++)
            {
                Choice s = GameController.Samples[i];
                var cr = new Rect(x + (i % 2) * (cw + gap), y + (i / 2) * (ch + gap), cw, ch);
                int id = StableKey("sample:" + s.Id);
                float h = CardHover(cr, id, idle);
                Card(cr, new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, 0.35f + 0.65f * h), h);
                Lbl(new Rect(cr.x + 20f, cr.y + 18f, cr.width - 40f, 22f), Spaced("Sample  0" + (i + 1)), tinyCaps, Settings.Accent);
                Condensed(new Rect(cr.x + 20f, cr.y + 38f, cr.width - 40f, 32f), Upper(s.Label), cardTitleL, Theme.Ink, 0.9f);
                Lbl(new Rect(cr.x + 20f, cr.y + 72f, cr.width - 40f, 40f), s.Blurb, menuDesc, Theme.Muted);
                string sid = s.Id;
                if (CardHit(cr, id, idle)) Game.Do(() => Game.ChooseSample(sid));
            }
            WaitingLine(new Rect(x, r.yMax - Pad - Theme.ButtonH - 30f, w, 20f));
            float by = r.yMax - Pad - Theme.ButtonH;
            if (GButton(new Rect(x, by, 180f, Theme.ButtonH), "Menu", 2, true, 0, false, UiTex.Glyph("back"))) Game.Do(Game.OpenMenu);
            if (Game.Report != null && GButton(new Rect(r.xMax - Pad - 300f, by, 300f, Theme.ButtonH), "Back to the current report", 0)) Game.Do(Game.BackToReport);
        }

        // ---- (2b) map choice ----------------------------------------------------------------------------
        static string MapBlurb(string id)
        {
            switch (id)
            {
                case "saltworks": return "Three sites, 110 x 80 m. Three lanes cross a field of buildings to a defender wall with three doors.";
                case "ironvale": return "Two sites and a mid courtyard, 100 x 80 m. A long lane, tight alleys and a market flank.";
                default: return "";
            }
        }

        static string MapSites(string id)
        {
            switch (id)
            {
                case "saltworks": return "3 SITES";
                case "ironvale": return "2 SITES";
                default: return "";
            }
        }

        readonly List<MapInfo> mapCards = new List<MapInfo>();

        /// <summary>The bridge's maps (or the loaded one when the list is missing), for the map cards.</summary>
        List<MapInfo> ShownMaps()
        {
            mapCards.Clear();
            if (Game.Maps != null) foreach (MapInfo m in Game.Maps) if (m != null && !string.IsNullOrEmpty(m.id)) mapCards.Add(m);
            if (mapCards.Count == 0 && Game.Map != null) mapCards.Add(new MapInfo { id = Game.Map.id, name = Game.MapName });
            return mapCards;
        }

        void DrawMapSelect()
        {
            float x0 = 80f, top = 56f;
            float y = Header(x0, top, W - 2f * x0 - 200f, "Practice", "Choose a map",
                "Drills are built on this map. Rehearsals of your own mistakes need the map the history was played on; on another map the coach generates fresh drills instead.");
            List<MapInfo> maps = ShownMaps();
            bool idle = Idle;
            float gap = 20f;
            float footer = 96f;
            if (Game.Maps == null && maps.Count == 0)
            {
                Lbl(new Rect(x0, y + 20f, 600f, 24f), "Asking the bridge for its maps...", bodyBold, Theme.Muted);
            }
            else
            {
                int n = Mathf.Max(1, maps.Count);
                float cw = Mathf.Min(520f, (W - 2f * x0 - gap * (n - 1)) / n);
                float ch = Mathf.Min(560f, H - y - footer - 10f);
                float total = n * cw + (n - 1) * gap;
                float cx0 = Mathf.Max(x0, (W - total) / 2f);
                string remembered = Game.RememberedMap;
                for (int i = 0; i < maps.Count; i++)
                {
                    MapInfo m = maps[i];
                    string id = m.id;
                    bool loaded = Game.Map != null && Game.Map.id == id;
                    float ik = Theme.Ease((Time.unscaledTime - screenAt - i * 0.06f) / 0.3f);
                    var cr = new Rect(cx0 + i * (cw + gap), y + (1f - ik) * 24f, cw, ch);
                    int key = StableKey("map:" + id);
                    float h = Anim(StableKey("mapcard:" + id), idle && Hover(cr));
                    Color ac = Settings.Accent;
                    Card(cr, loaded ? ac : new Color(1f, 1f, 1f, 0.12f + 0.3f * h), h);

                    // The layout preview: the loaded map is drawn from its data; another map shows its name until picked.
                    var pv = new Rect(cr.x + 1f, cr.y + 2f, cr.width - 2f, ch * 0.56f);
                    Fill(pv, new Color(Theme.Base.r * 0.6f, Theme.Base.g * 0.6f, Theme.Base.b * 0.6f, 0.96f));
                    if (loaded && Game.Map != null)
                    {
                        if (Event.current.type == EventType.Repaint)
                            Game.Minimap.Draw(Game, PhysRect(pv), true, Vector2.zero, 0f, 1f, scale, false);
                    }
                    else
                    {
                        Condensed(new Rect(pv.x, pv.y, pv.width, pv.height), Spaced(m.name ?? id), displayMC, new Color(1f, 1f, 1f, 0.12f), 0.84f);
                        Lbl(new Rect(pv.x, pv.yMax - 30f, pv.width, 20f), "PREVIEW AFTER LOADING", centerSmall, Theme.Faint);
                    }
                    float ty = pv.yMax + 18f;
                    Condensed(new Rect(cr.x + 20f, ty, cr.width - 40f, 38f), Upper(m.name ?? id), cardTitleL, Theme.Ink, 0.9f);
                    ty += 40f;
                    float chipX = cr.x + 20f;
                    string sites = MapSites(id);
                    if (sites.Length > 0) chipX += Chip(chipX, ty, sites, new Color(1f, 1f, 1f, 0.1f), Theme.Ink) + 8f;
                    if (loaded) chipX += Chip(chipX, ty, "LOADED", ac, Theme.OnAccent) + 8f;
                    else if (id == remembered) chipX += Chip(chipX, ty, "LAST PLAYED", new Color(1f, 1f, 1f, 0.1f), Theme.Muted) + 8f;
                    ty += 34f;
                    Para(cr.x + 20f, ty, cr.width - 40f, MapBlurb(id), menuDesc, Theme.Muted);
                    if (GButton(new Rect(cr.x + 20f, cr.yMax - 20f - Theme.ButtonH, cr.width - 40f, Theme.ButtonH), loaded ? "Practise on " + (m.name ?? id) : "Load " + (m.name ?? id), loaded ? 1 : 0, idle, key))
                        Game.Do(() => Game.PickMap(id));
                }
                if (maps.Count == 0) Lbl(new Rect(x0, y + 20f, 600f, 24f), "The bridge listed no maps; the current one stays.", bodyBold, Theme.Muted);
            }
            float by = H - 48f - Theme.ButtonH;
            WaitingLine(new Rect(x0 + 340f, by + 14f, 400f, 20f));
            string keep = string.IsNullOrEmpty(Game.MapName) ? "Keep the current map" : "Keep " + Game.MapName;
            if (GButton(new Rect(x0, by, 300f, Theme.ButtonH), keep, 2, idle)) Game.Do(Game.KeepCurrentMap);
        }

        // ---- (3) weakness report ------------------------------------------------------------------------
        enum ReportBlock { AdvicePicks, AdviceStyle, AdviceFits, FindingsHeader, Finding, NoFindings, RehearsalsHeader, Rehearsal }

        struct Block
        {
            public ReportBlock Kind;
            public int Index;
            public float Height;
        }

        readonly List<Block> reportBlocks = new List<Block>();
        int reportFirst;
        ReportMsg reportFor;

        void DrawReport()
        {
            ReportMsg rm = Game.Report;
            if (rm == null)
            {
                DrawChoose();
                return;
            }
            WeaknessReport rep = rm.report ?? new WeaknessReport();
            Rect r = Centered(Mathf.Min(1280f, W - 60f), H - 64f);
            Frame(r, "Coach report");
            Glass(r);
            float x = r.x + Pad, w = r.width - 2f * Pad;
            string sub = string.Format("Player {0}, played as {1}.  {2} matches read, {3} drills played this session.", rep.playerId ?? "?", rep.role ?? "?", rep.matchesRead, rm.played);
            float y = Header(x, r.y + 26f, w - 450f, null, "Weakness report", sub);
            bool idle = Idle;
            float hx = r.xMax - Pad;
            if (GButton(new Rect(hx - 52f, r.y + 30f, 52f, Theme.ButtonSmallH), "", 2, true, StableKey("report-settings"), false, UiTex.Glyph("gear"))) Game.Do(Game.OpenSettings);
            hx -= 52f + 12f;
            if (GButton(new Rect(hx - 140f, r.y + 30f, 140f, Theme.ButtonSmallH), "Menu", 2, idle, 0, false, UiTex.Glyph("back"))) Game.Do(Game.OpenMenu);
            hx -= 140f + 12f;
            if (GButton(new Rect(hx - 210f, r.y + 30f, 210f, Theme.ButtonSmallH), "Another player", 0, idle)) Game.Do(Game.BackToChoose);
            if (rm.staircases != null && rm.staircases.Count > 0)
            {
                float sx = x;
                Lbl(new Rect(sx, y - 6f, 140f, 22f), Spaced("Difficulty"), tinyCaps, Theme.Muted);
                sx += 110f;
                foreach (KeyValuePair<string, StaircaseInfo> kv in rm.staircases)
                {
                    string t = Upper(GameController.SkillName(kv.Key)) + "  " + (kv.Value != null ? kv.Value.difficulty.ToString("0.00") : "-");
                    sx += Chip(sx, y - 6f, t, new Color(1f, 1f, 1f, 0.08f), Theme.Ink) + 8f;
                    if (sx > x + w - 200f) break;
                }
                y += 26f;
            }

            float colGap = 28f;
            float rightW = Mathf.Clamp(w * 0.38f, 380f, 470f);
            float leftW = w - rightW - colGap;
            float bottom = r.yMax - Pad;
            DrawReportList(rm, rep, new Rect(x, y, leftW, bottom - y));
            DrawReportActions(rm, new Rect(x + leftW + colGap, y, rightW, bottom - y), idle);
        }

        /// <summary>The left column: advice, findings and rehearsals as cards, paged one card at a time.</summary>
        void DrawReportList(ReportMsg rm, WeaknessReport rep, Rect col)
        {
            if (!ReferenceEquals(reportFor, rm))
            {
                reportFor = rm;
                reportFirst = 0;
            }
            float w = col.width;
            reportBlocks.Clear();
            bool rehearsals = rm.rehearsals != null && rm.rehearsals.Count > 0;
            bool rehearseFirst = Game.RehearseFocus && rehearsals;
            if (rehearseFirst) AddRehearsalBlocks(rm, w);
            AgentAdvice adv = rm.agentAdvice;
            if (adv != null && (adv.main?.agent != null || adv.practice?.agent != null))
            {
                CacheAdvice(adv);
                reportBlocks.Add(new Block { Kind = ReportBlock.AdvicePicks, Height = AdvicePicksHeight(adv, w) });
                if (adviceStats.Count > 0) reportBlocks.Add(new Block { Kind = ReportBlock.AdviceStyle, Height = AdviceStyleHeight(w) });
                if (adv.ranked != null && adv.ranked.Count > 0) reportBlocks.Add(new Block { Kind = ReportBlock.AdviceFits, Height = AdviceFitsHeight(adv) });
            }
            reportBlocks.Add(new Block { Kind = ReportBlock.FindingsHeader, Height = 34f });
            if (rep.findings == null || rep.findings.Count == 0) reportBlocks.Add(new Block { Kind = ReportBlock.NoFindings, Height = 30f });
            else
                for (int i = 0; i < rep.findings.Count; i++)
                    if (rep.findings[i] != null) reportBlocks.Add(new Block { Kind = ReportBlock.Finding, Index = i, Height = FindingHeight(rep.findings[i], w) });
            if (!rehearseFirst && rehearsals) AddRehearsalBlocks(rm, w);

            // Wheel over the column pages by one card.
            Event e = Event.current;
            if (e.type == EventType.ScrollWheel && col.Contains(e.mousePosition))
            {
                reportFirst += e.delta.y > 0f ? 1 : -1;
                e.Use();
            }
            reportFirst = Mathf.Clamp(reportFirst, 0, Mathf.Max(0, reportBlocks.Count - 1));
            const float gap = 12f, more = 34f;
            bool above = reportFirst > 0;
            float y = col.y;
            if (above)
            {
                if (GButton(new Rect(col.x, y, w, 28f), "\u25b2  " + reportFirst + " more above", 2, true, StableKey("report-up"))) reportFirst--;
                y += more;
            }
            int last = reportFirst - 1;
            for (int i = reportFirst; i < reportBlocks.Count; i++)
            {
                Block b = reportBlocks[i];
                bool lastOne = i == reportBlocks.Count - 1;
                float room = col.yMax - y - (lastOne ? 0f : more);
                if (b.Height > room && i > reportFirst) break;
                DrawReportBlock(rm, rep, b, new Rect(col.x, y, w, Mathf.Min(b.Height, col.yMax - y)));
                y += b.Height + gap;
                last = i;
            }
            int below = reportBlocks.Count - 1 - last;
            if (below > 0 && GButton(new Rect(col.x, col.yMax - 28f, w, 28f), "\u25bc  " + below + " more below", 2, true, StableKey("report-down"))) reportFirst++;
        }

        void AddRehearsalBlocks(ReportMsg rm, float w)
        {
            reportBlocks.Add(new Block { Kind = ReportBlock.RehearsalsHeader, Height = 52f });
            for (int i = 0; i < rm.rehearsals.Count; i++)
                if (rm.rehearsals[i] != null) reportBlocks.Add(new Block { Kind = ReportBlock.Rehearsal, Index = i, Height = RehearsalHeight(rm.rehearsals[i], w) });
        }

        float FindingHeight(WeaknessFinding f, float w)
        {
            float h = 18f + 26f + 14f + 24f + 16f + 14f;
            h += TextH(f.summary, body, w - 40f);
            if (f.evidence != null && f.evidence.Count > 0) h += 6f + TextH(EvidenceText(f.evidence[0]), small, w - 40f);
            return h + 18f;
        }

        static string EvidenceText(WeaknessEvidence e)
        {
            return e == null ? "" : string.Format("Evidence: {0}, round {1}, {2:0.0} s: {3}", e.matchId, e.round, e.tick / 20f, e.note);
        }

        float RehearsalHeight(RehearsalInfo ri, float w)
        {
            return 46f + TextH(ri.brief, small, w - 160f) + 14f;
        }

        void DrawReportBlock(ReportMsg rm, WeaknessReport rep, Block b, Rect r)
        {
            switch (b.Kind)
            {
                case ReportBlock.AdvicePicks: DrawAdvicePicks(rm.agentAdvice, r); break;
                case ReportBlock.AdviceStyle: DrawAdviceStyle(r); break;
                case ReportBlock.AdviceFits: DrawAdviceFits(rm.agentAdvice, r); break;
                case ReportBlock.FindingsHeader: SectionLabel(r.x, r.y + 4f, r.width, "Findings"); break;
                case ReportBlock.NoFindings: Lbl(new Rect(r.x, r.y, r.width, 24f), "No findings yet: the history has too few situations to judge.", body, Theme.Muted); break;
                case ReportBlock.Finding: DrawFinding(rep.findings[b.Index], rep.role, r); break;
                case ReportBlock.RehearsalsHeader:
                    SectionLabel(r.x, r.y + 4f, r.width, "Rehearse my mistakes");
                    Lbl(new Rect(r.x, r.y + 32f, r.width, 20f), "Each one rebuilds a moment from the history: same positions, same clock.", small, Theme.Muted);
                    break;
                case ReportBlock.Rehearsal: DrawRehearsal(rm.rehearsals[b.Index], r); break;
            }
        }

        void DrawFinding(WeaknessFinding f, string role, Rect r)
        {
            Color sev = Palette.Severity(f.severity);
            Card(r, sev);
            float x = r.x + 20f, w = r.width - 40f, y = r.y + 18f;
            Condensed(new Rect(x, y, w - 160f, 26f), Upper(GameController.SkillName(f.skill)), cardTitleL, Theme.Ink, 0.9f);
            string sevText = "SEVERITY " + f.severity.ToString("0.00");
            float cw = TextW(sevText, chipStyle) + 18f;
            Chip(r.xMax - 20f - cw, y + 2f, sevText, new Color(sev.r * 0.3f, sev.g * 0.3f, sev.b * 0.3f, 1f), sev);
            y += 30f;
            Meter(new Rect(x, y, w, 6f), f.severity, sev);
            y += 18f;
            Lbl(new Rect(x, y, w, 22f), string.Format("Your rate {0:0}%   vs   benchmark {1:0}% for a {2}   ({3} situations)", f.rate * 100f, f.benchmark * 100f, role ?? "player", f.sampleSize), bodyBold, Theme.Ink);
            y += 26f;
            Meter(new Rect(x, y, w, 6f), f.rate, f.rate >= f.benchmark ? Palette.Ok : Palette.Warn, f.benchmark);
            y += 20f;
            y = Para(x, y, w, f.summary, body, Theme.Muted);
            if (f.evidence != null && f.evidence.Count > 0) Para(x, y + 6f, w, EvidenceText(f.evidence[0]), small, Theme.Faint);
        }

        void DrawRehearsal(RehearsalInfo ri, Rect r)
        {
            int key = StableKey("rehearse:" + ri.index);
            bool idle = Idle;
            float h = CardHover(r, key, idle);
            Card(r, new Color(0f, 0f, 0f, 0f), h);
            Fill(new Rect(r.x + 2f, r.y + 12f, 3f, r.height - 24f), Settings.Accent);
            Lbl(new Rect(r.x + 20f, r.y + 14f, r.width - 180f, 24f), ri.title ?? "Rehearsal " + ri.index, cardTitle, Theme.Ink);
            string sk = Upper(GameController.SkillName(ri.skill));
            if (sk.Length > 0) Chip(r.x + 20f + Mathf.Min(r.width - 300f, TextW(ri.title ?? "Rehearsal " + ri.index, cardTitle) + 12f), r.y + 14f, sk, new Color(1f, 1f, 1f, 0.08f), Theme.Muted);
            Para(r.x + 20f, r.y + 42f, r.width - 160f, ri.brief, small, Theme.Muted);
            Lbl(new Rect(r.xMax - 140f, r.y, 120f, r.height), "REHEARSE  \u203a", rightSmall, Color.Lerp(Theme.Muted, Settings.Accent, 0.4f + 0.6f * h));
            int idx = ri.index;
            if (CardHit(r, key, idle)) Game.Do(() => Game.RequestRehearsal(idx));
        }

        /// <summary>The right column: the next drill, the setup (map, agent) and a free round against an archetype.</summary>
        void DrawReportActions(ReportMsg rm, Rect col, bool idle)
        {
            float x = col.x, w = col.width, y = col.y;
            // Next drill: the main call to action.
            var cta = new Rect(x, y, w, 166f);
            Card(cta, Settings.Accent);
            Lbl(new Rect(cta.x + 20f, cta.y + 18f, w - 40f, 18f), Spaced("Next practice drill"), tinyCaps, Settings.Accent);
            Para(cta.x + 20f, cta.y + 40f, w - 40f, "A rehearsal of a failed moment first, then generated drills at your current difficulty.", small, Theme.Muted);
            if (GButton(new Rect(cta.x + 20f, cta.yMax - 20f - Theme.ButtonH, w - 40f, Theme.ButtonH), "Start next drill", 1, idle, 0, false, UiTex.Glyph("play"), "Enter")) Game.Do(Game.RequestNextDrill);
            y = cta.yMax + Theme.S4;

            // Setup: map and agent (bots-wire.2).
            if (Game.V2)
            {
                var su = new Rect(x, y, w, 128f);
                Card(su, new Color(0f, 0f, 0f, 0f));
                float half = (w - 40f - 16f) / 2f;
                float sx = su.x + 20f;
                Lbl(new Rect(sx, su.y + 16f, half, 18f), Spaced("Map"), tinyCaps, Theme.Muted);
                Lbl(new Rect(sx, su.y + 34f, half, 26f), Upper(string.IsNullOrEmpty(Game.MapName) ? "?" : Game.MapName), cardTitle, Theme.Ink);
                if (GButton(new Rect(sx, su.yMax - 16f - Theme.ButtonSmallH, half, Theme.ButtonSmallH), "Change map", 0, idle)) Game.Do(Game.OpenMapSelect);
                float ax = sx + half + 16f;
                AgentInfo a = Agents.Get(Game.PickedAgentId);
                Lbl(new Rect(ax, su.y + 16f, half, 18f), Spaced("Agent"), tinyCaps, Theme.Muted);
                Lbl(new Rect(ax, su.y + 34f, half, 26f), a != null ? Upper(a.Name) : "SET BY EACH DRILL", cardTitle, a != null ? a.Color : Theme.Muted);
                if (GButton(new Rect(ax, su.yMax - 16f - Theme.ButtonSmallH, half, Theme.ButtonSmallH), "Change agent", 0, idle)) Game.Do(Game.OpenAgentSelect);
                y = su.yMax + Theme.S4;
            }

            // Versus an archetype.
            y = SectionLabel(x, y, w, "Versus an archetype");
            Game.VersusSkill = SegmentedCtl(new Rect(x, y, w, 38f), Game.VersusSkill, SkillLabels, StableKey("versus-skill"), idle);
            y += 50f;
            float bw = (w - 10f) / 2f, bh = 66f;
            for (int i = 0; i < GameController.Archetypes.Length; i++)
            {
                Choice a = GameController.Archetypes[i];
                var br = new Rect(x + (i % 2) * (bw + 10f), y + (i / 2) * (bh + 10f), bw, bh);
                if (br.yMax > col.yMax - 22f) break;
                int key = StableKey("versus:" + a.Id);
                float h = CardHover(br, key, idle);
                Plate(br, Color.Lerp(Theme.ButtonC, Theme.ButtonHoverC, h));
                PlateLine(br, new Color(1f, 1f, 1f, 0.10f + 0.2f * h));
                Lbl(new Rect(br.x + 14f, br.y + 8f, br.width - 28f, 20f), a.Label, bodyBold, idle ? Theme.Ink : Theme.Faint);
                Lbl(new Rect(br.x + 14f, br.y + 28f, br.width - 28f, 36f), a.Blurb, tipStyle, Theme.Muted);
                string id = a.Id;
                if (CardHit(br, key, idle)) Game.Do(() => Game.RequestVersus(id));
            }
            WaitingLine(new Rect(x, col.yMax - 20f, w, 20f));
        }

        // ---- (4) drill intro ----------------------------------------------------------------------------
        void DrawIntro()
        {
            DrillMsg d = Game.Drill;
            if (d == null) return;
            DrillSpec spec = d.spec ?? new DrillSpec();
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.35f));
            Rect r = Centered(Mathf.Min(1120f, W - 60f), Mathf.Min(700f, H - 60f));
            Frame(r, "Drill briefing");
            Glass(r);
            float x = r.x + Pad;
            float rightW = Mathf.Min(420f, r.width * 0.38f);
            float leftW = r.width - 2f * Pad - rightW - 32f;
            float y = Header(x, r.y + 26f, leftW, null, spec.title ?? "Drill");
            float cx = x;
            Color team = Palette.Team(Game.PlayerTeam);
            cx += Chip(cx, y - 8f, Upper(GameController.SkillName(spec.skill)), new Color(1f, 1f, 1f, 0.1f), Theme.Ink, 24f) + 8f;
            cx += Chip(cx, y - 8f, "DIFFICULTY " + spec.difficulty.ToString("0.00"), new Color(1f, 1f, 1f, 0.1f), Theme.Ink, 24f) + 8f;
            cx += Chip(cx, y - 8f, Upper(Game.PlayerTeam), new Color(team.r * 0.3f, team.g * 0.3f, team.b * 0.3f, 1f), team, 24f) + 8f;
            if (!string.IsNullOrEmpty(Game.MapName)) Chip(cx, y - 8f, Upper(Game.MapName), new Color(1f, 1f, 1f, 0.1f), Theme.Ink, 24f);
            y += 30f;
            float maxY = r.yMax - Pad - Theme.ButtonH - 20f;
            if (!string.IsNullOrEmpty(d.intro)) y = Para(x, y, leftW, d.intro, lead, Theme.Ink) + Theme.S4;
            if (!string.IsNullOrEmpty(spec.goal))
            {
                float gh = TextH(spec.goal, body, leftW - 40f) + 44f;
                var gr = new Rect(x, y, leftW, gh);
                Plate(gr, new Color(1f, 1f, 1f, 0.04f));
                PlateLine(gr, Theme.Line);
                Lbl(new Rect(gr.x + 20f, gr.y + 10f, gr.width - 40f, 18f), "GOAL", tinyCaps, Theme.Muted);
                Para(gr.x + 20f, gr.y + 30f, gr.width - 40f, spec.goal, body, Theme.Ink);
                y = gr.yMax + Theme.S4;
            }
            if (!string.IsNullOrEmpty(Game.PlayerAgent))
            {
                AgentInfo ai = Agents.Get(Game.PlayerAgent);
                string lent = Game.PickedAgentId != null && Game.PlayerAgent != Game.PickedAgentId ? "   (this drill lends you this agent)" : "";
                string line = "Agent: " + Agents.NameOf(Game.PlayerAgent) + (ai != null ? ", " + ai.Role + ".  " + Agents.KitText(Game.PlayerKit()) : "") + lent;
                y = Para(x, y, leftW, line, bodyBold, Agents.ColorOf(Game.PlayerAgent)) + Theme.S3;
            }
            if (d.bots != null && d.bots.Count > 0 && y + 70f < maxY)
            {
                y = SectionLabel(x, y, leftW, "Bots");
                foreach (DrillBotInfo b in d.bots)
                {
                    if (b == null || y + 24f > maxY) break;
                    Color rc = Palette.Relation(b.team, Game.PlayerTeam);
                    Plate(new Rect(x, y + 6f, 10f, 10f), rc);
                    string who = Game.PersonaName(b.id) ?? b.id;
                    string agent = !string.IsNullOrEmpty(b.agent) ? "   " + Agents.NameOf(b.agent) : "";
                    Lbl(new Rect(x + 20f, y, leftW - 20f, 22f), who + "   " + (b.team ?? "") + "   " + GameController.ArchetypeName(b.archetype) + agent, body, Theme.Ink);
                    y += 26f;
                }
            }

            // Right: the controls.
            var cr = new Rect(r.xMax - Pad - rightW, r.y + 30f, rightW, maxY - r.y - 30f);
            Plate(cr, new Color(1f, 1f, 1f, 0.03f));
            float cy = SectionLabel(cr.x + 16f, cr.y + 14f, cr.width - 32f, "Controls");
            DrawControlsList(cr.x + 16f, cy, cr.width - 32f, cr.yMax - 10f);

            float by = r.yMax - Pad - Theme.ButtonH;
            Lbl(new Rect(x, by, r.width - 2f * Pad - 340f, Theme.ButtonH), "The clock starts when you begin. The mouse is captured; Esc pauses.", smallMid, Theme.Muted);
            if (GButton(new Rect(r.xMax - Pad - 320f, by, 320f, Theme.ButtonH), "Begin drill", 1, true, 0, false, UiTex.Glyph("play"), "Enter")) Game.Do(Game.Begin);
        }

        /// <summary>The key list: a key cap column and the action beside it, as many rows as fit above `maxY`.</summary>
        void DrawControlsList(float x, float y, float w, float maxY)
        {
            const float rowH = 26f, keyW = 112f;
            foreach (string[] c in Controls)
            {
                if (y + rowH > maxY) break;
                float kw = Mathf.Min(keyW, TextW(c[0], keyCap) + 14f);
                KeyCapAt(new Rect(x, y + 2f, kw, 22f), c[0], new Color(1f, 1f, 1f, 0.09f), Theme.Ink);
                FitLine(new Rect(x + keyW + 10f, y, w - keyW - 10f, 26f), c[1], smallMid, Theme.Muted, false);
                y += rowH;
            }
        }

        // ---- pause ------------------------------------------------------------------------------------
        void DrawPause()
        {
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.6f));
            Rect r = Centered(Mathf.Min(980f, W - 60f), 600f);
            Frame(r, "Paused");
            Glass(r);
            float x = r.x + Pad;
            float leftW = 380f;
            float y = Header(x, r.y + 26f, leftW, null, "Paused", "No inputs are sent while paused, so the sim waits for you.");
            y += Theme.S2;
            if (GButton(new Rect(x, y, leftW, 54f), "Resume", 1, true, 0, false, UiTex.Glyph("play"), "Esc")) Game.Do(Game.Resume);
            y += 54f + Theme.S3;
            if (GButton(new Rect(x, y, leftW, Theme.ButtonH), "Settings", 0, true, 0, false, UiTex.Glyph("gear"))) Game.Do(Game.OpenSettings);
            y += Theme.ButtonH + Theme.S3;
            if (Game.InMatch)
            {
                if (GButton(new Rect(x, y, leftW, Theme.ButtonH), "Leave match", 0)) Game.Do(Game.LeaveMatch);
            }
            else if (GButton(new Rect(x, y, leftW, Theme.ButtonH), "Abandon drill", 0)) Game.Do(Game.AbandonDrill);
            y += Theme.ButtonH + 6f;
            if (!Game.InMatch) Lbl(new Rect(x, y, leftW, 20f), "An abandoned drill counts as a fail.", small, Theme.Faint);

            var cr = new Rect(x + leftW + 32f, r.y + 30f, r.xMax - Pad - (x + leftW + 32f), r.height - 60f);
            Plate(cr, new Color(1f, 1f, 1f, 0.03f));
            float cy = SectionLabel(cr.x + 16f, cr.y + 14f, cr.width - 32f, "Controls");
            DrawControlsList(cr.x + 16f, cy, cr.width - 32f, cr.yMax - 10f);
        }

        // ---- (5) result ------------------------------------------------------------------------------------
        float debriefScroll;
        DebriefMsg debriefFor;

        void DrawResult()
        {
            ResultMsg res = Game.Result;
            if (res == null) return;
            DrillSpec spec = Game.Drill?.spec ?? new DrillSpec();
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.3f));
            Rect r = Centered(Mathf.Min(1160f, W - 60f), H - 70f);
            Color vc = res.success ? Palette.Ok : Palette.Bad;
            Frame(r, res.success ? "Drill complete" : "Drill failed");
            Glass(r);
            Fill(new Rect(r.x + Theme.Cut, r.y, r.width - Theme.Cut * 2f, 3f), vc);
            float x = r.x + Pad, w = r.width - 2f * Pad;
            float y = r.y + 26f;
            Condensed(new Rect(x, y, w, 56f), Spaced(res.success ? "Drill complete" : "Not this time"), displayM, vc, 0.84f, true);
            y += 58f;
            if (!string.IsNullOrEmpty(res.headline)) y = Para(x, y, w, res.headline, h2, Theme.Ink) + 6f;
            int ticks = res.result != null ? res.result.ticks : 0;
            Lbl(new Rect(x, y, w, 22f), (spec.title ?? "Drill") + "   \u00b7   " + GameController.SkillName(spec.skill) + "   \u00b7   " + (ticks / Mathf.Max(1f, Game.Rules.tickRate)).ToString("0.0") + " s", bodyBold, Theme.Muted);
            y += 30f;

            float footerY = r.yMax - Pad - Theme.ButtonH;
            float colGap = 28f;
            float leftW = (w - colGap) * 0.46f, rightW = w - colGap - leftW;
            float ly = y;
            // Left: the difficulty change and what happened.
            ly = SectionLabel(x, ly, leftW, "Difficulty");
            if (res.staircaseBefore.HasValue && res.staircaseAfter.HasValue)
            {
                float b = res.staircaseBefore.Value, a = res.staircaseAfter.Value;
                string change = a > b + 1e-4f ? "harder" : a < b - 1e-4f ? "easier" : "the same";
                ly = Para(x, ly, leftW, string.Format("{0} drills go from {1:0.00} to {2:0.00} ({3}).", GameController.SkillName(spec.skill), b, a, change), body, Theme.Ink) + 10f;
                Meter(new Rect(x, ly, leftW, 8f), a, a >= b ? Palette.Attack : Palette.Defense, b);
                ly += 26f;
            }
            else
            {
                Lbl(new Rect(x, ly, leftW, 22f), "No staircase change for this round.", body, Theme.Muted);
                ly += 30f;
            }
            List<string> lines = WhatHappened(res);
            if (lines.Count > 0)
            {
                ly = SectionLabel(x, ly + 6f, leftW, "What happened");
                foreach (string l in lines)
                {
                    float lh = TextH(l, small, leftW);
                    if (ly + lh > footerY - 16f) break;
                    Lbl(new Rect(x, ly, leftW, lh), l, small, Theme.Muted);
                    ly += lh + 2f;
                }
            }

            // Right: the coach's debrief.
            float rx = x + leftW + colGap;
            var dr = new Rect(rx, y, rightW, footerY - 20f - y);
            Card(dr, new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, 0.8f));
            Lbl(new Rect(dr.x + 20f, dr.y + 18f, dr.width - 40f, 18f), Spaced("Coach"), tinyCaps, Settings.Accent);
            DebriefMsg db = Game.Debrief;
            var tb = new Rect(dr.x + 20f, dr.y + 44f, dr.width - 40f, dr.height - 80f);
            if (db == null)
            {
                int dots = 1 + (int)(Time.unscaledTime * 3f) % 3;
                Lbl(new Rect(tb.x, tb.y, tb.width, 24f), "The coach is writing" + new string('.', dots), body, Theme.Muted);
            }
            else
            {
                if (!ReferenceEquals(debriefFor, db))
                {
                    debriefFor = db;
                    debriefScroll = 0f;
                }
                string text = db.text ?? "";
                float th = TextH(text, debrief, tb.width);
                if (th <= tb.height) Lbl(new Rect(tb.x, tb.y, tb.width, th), text, debrief, Theme.Ink);
                else
                {
                    // Longer than the card: scroll it with the wheel (the only clipped text on this screen).
                    Event e = Event.current;
                    if (e.type == EventType.ScrollWheel && tb.Contains(e.mousePosition))
                    {
                        debriefScroll += e.delta.y * 24f;
                        e.Use();
                    }
                    debriefScroll = Mathf.Clamp(debriefScroll, 0f, th - tb.height);
                    BeginClip(tb);
                    Lbl(new Rect(0f, -debriefScroll, tb.width, th), text, debrief, Theme.Ink);
                    EndClip();
                    float bar = tb.height * tb.height / th;
                    Fill(new Rect(tb.xMax + 8f, tb.y + (tb.height - bar) * (debriefScroll / Mathf.Max(1f, th - tb.height)), 3f, bar), new Color(1f, 1f, 1f, 0.25f));
                }
                Lbl(new Rect(dr.x + 20f, dr.yMax - 30f, dr.width - 40f, 20f), "Written by: " + (db.source ?? "?"), small, Theme.Faint);
            }

            // Footer. Like the 2D client, the next drill waits for the debrief (with a timeout in case it never comes).
            bool next = Idle && (db != null || Time.unscaledTime - Game.ResultAt > 15f);
            WaitingLine(new Rect(x, footerY - 22f, 400f, 20f));
            if (GButton(new Rect(x, footerY, 220f, Theme.ButtonH), "Back to report", 2, true, 0, false, UiTex.Glyph("back"))) Game.Do(Game.BackToReport);
            if (GButton(new Rect(r.xMax - Pad - 300f - 12f - 220f, footerY, 220f, Theme.ButtonH), "Retry this drill", 0, next)) Game.Do(Game.RequestRetry);
            if (GButton(new Rect(r.xMax - Pad - 300f, footerY, 300f, Theme.ButtonH), "Next drill", 1, next, 0, false, UiTex.Glyph("play"), "Enter")) Game.Do(Game.RequestNextDrill);
        }

        ResultMsg linesFor;
        readonly List<string> lineCache = new List<string>();

        List<string> WhatHappened(ResultMsg res)
        {
            if (ReferenceEquals(linesFor, res)) return lineCache;
            linesFor = res;
            List<string> lines = lineCache;
            lines.Clear();
            if (res.result?.events == null) return lines;
            float rate = Mathf.Max(1f, Game.Rules.tickRate);
            foreach (SimEvent e in res.result.events)
            {
                if (e == null) continue;
                string at = (e.tick / rate).ToString("0.0") + " s   ";
                switch (e.type)
                {
                    case "kill": lines.Add(at + Game.Name(e.killer) + " killed " + Game.Name(e.victim) + (e.part == "head" ? " (headshot)" : "")); break;
                    case "smoke": lines.Add(at + Game.Name(e.agent) + " threw a smoke"); break;
                    case "flash": lines.Add(at + Game.Name(e.thrower) + " threw a flash"); break;
                    case "blinded": lines.Add(at + Game.Name(e.agent) + " was blinded by " + Game.Name(e.by) + " for " + ((e.untilTick - e.tick) / rate).ToString("0.0") + " s"); break;
                    case "recon": lines.Add(at + Game.Name(e.thrower) + " threw a recon dart"); break;
                    case "revealed": lines.Add(at + Game.Name(e.target) + " was revealed by " + Game.Name(e.by)); break;
                    case "plant": lines.Add(at + Game.Name(e.agent) + " planted on " + e.site); break;
                    case "defuse": lines.Add(at + Game.Name(e.agent) + " defused"); break;
                    case "round_end": lines.Add(at + "Round over: " + e.winner + " wins (" + e.reason + ")"); break;
                }
                if (lines.Count >= 16) break;
            }
            return lines;
        }
    }
}

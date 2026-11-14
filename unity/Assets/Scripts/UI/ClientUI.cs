// All screens and the HUD, drawn with IMGUI and custom GUIStyles (no Canvas,
// no TextMeshPro). Layout works in a virtual 900-pixel-high space scaled to the
// window. Buttons queue their action through GameController.Do so a screen
// change never happens between IMGUI's layout and repaint passes.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public class ClientUI : MonoBehaviour
    {
        public GameController Game;

        const float VirtualHeight = 900f;

        float scale = 1f;
        float W, H;
        bool ready;
        Vector2 reportScroll, resultScroll, introScroll;
        readonly RaycastHit[] hits = new RaycastHit[16];

        GUIStyle title, h2, h3, body, bodyBold, muted, small, mono;
        GUIStyle button, primary, bigButton, toolbar;
        GUIStyle panel, card;
        GUIStyle hudClock, hudSmall, hudText, hudBig, hudCenter, feed, banner, bannerSub, botName, botLine;
        GUIStyle monoBig, lead, debrief, verdictTitle, severityLabel, toastStyle, hudTitle, hudSkill, hudGoal, capLeft, capRight, spikeStyle;
        Texture2D texPanel, texCard, texButton, texButtonHover, texButtonActive, texPrimary, texPrimaryHover;

        static readonly string[] SkillLabels = { "Entry", "Clutch", "Time pressure", "Smoke usage" };

        static readonly string[][] Controls =
        {
            new[] { "Mouse", "Look (yaw goes to the sim, pitch stays here)" },
            new[] { "W A S D", "Move" },
            new[] { "Shift", "Walk" },
            new[] { "Ctrl / C", "Crouch" },
            new[] { "Left click", "Fire" },
            new[] { "Q / E", "Throw a smoke where the crosshair meets the floor" },
            new[] { "Hold F", "Plant (attack, on a site, with the spike) or defuse (defense, at the spike)" },
            new[] { "Esc", "Pause" },
        };

        // ---- styles --------------------------------------------------------------------------
        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        static GUIStyle Text(GUIStyle from, int size, FontStyle fs, Color c, TextAnchor a = TextAnchor.UpperLeft, bool wrap = true)
        {
            var s = new GUIStyle(from) { fontSize = size, fontStyle = fs, alignment = a, wordWrap = wrap, richText = false };
            s.normal.textColor = c;
            s.hover.textColor = c;
            s.active.textColor = c;
            return s;
        }

        static GUIStyle Button(GUIStyle from, int size, Texture2D n, Texture2D h, Texture2D a, Color text, TextAnchor align)
        {
            var s = new GUIStyle(from)
            {
                fontSize = size,
                alignment = align,
                wordWrap = true,
                richText = false,
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(14, 14, 9, 9),
                margin = new RectOffset(4, 4, 5, 5),
            };
            s.normal.background = n;
            s.hover.background = h;
            s.active.background = a;
            s.focused.background = n;
            s.onNormal.background = a;
            s.onHover.background = a;
            s.onActive.background = a;
            s.normal.textColor = text;
            s.hover.textColor = text;
            s.active.textColor = text;
            s.focused.textColor = text;
            s.onNormal.textColor = text;
            s.onHover.textColor = text;
            s.onActive.textColor = text;
            return s;
        }

        void EnsureStyles()
        {
            if (ready) return;
            ready = true;
            texPanel = Tex(new Color(0.09f, 0.10f, 0.13f, 0.93f));
            texCard = Tex(new Color(0.15f, 0.16f, 0.20f, 0.96f));
            texButton = Tex(new Color(0.22f, 0.24f, 0.30f, 1f));
            texButtonHover = Tex(new Color(0.30f, 0.33f, 0.41f, 1f));
            texButtonActive = Tex(new Color(0.16f, 0.55f, 0.53f, 1f));
            texPrimary = Tex(Palette.Attack);
            texPrimaryHover = Tex(Color.Lerp(Palette.Attack, Color.white, 0.2f));

            GUIStyle lbl = GUI.skin.label;
            var white = new Color(0.96f, 0.96f, 0.97f);
            var grey = new Color(0.68f, 0.70f, 0.75f);
            title = Text(lbl, 30, FontStyle.Bold, white);
            h2 = Text(lbl, 19, FontStyle.Bold, white);
            h3 = Text(lbl, 16, FontStyle.Bold, white);
            body = Text(lbl, 15, FontStyle.Normal, white);
            bodyBold = Text(lbl, 15, FontStyle.Bold, white);
            muted = Text(lbl, 14, FontStyle.Normal, grey);
            small = Text(lbl, 13, FontStyle.Normal, grey);
            mono = Text(lbl, 14, FontStyle.Bold, new Color(0.55f, 0.92f, 0.88f), TextAnchor.UpperLeft, false);

            button = Button(GUI.skin.button, 15, texButton, texButtonHover, texButtonActive, white, TextAnchor.MiddleCenter);
            primary = Button(GUI.skin.button, 16, texPrimary, texPrimaryHover, texButtonActive, new Color(0.1f, 0.08f, 0.08f), TextAnchor.MiddleCenter);
            primary.fontStyle = FontStyle.Bold;
            bigButton = Button(GUI.skin.button, 15, texButton, texButtonHover, texButtonActive, white, TextAnchor.MiddleLeft);
            toolbar = Button(GUI.skin.button, 14, texButton, texButtonHover, texButtonActive, white, TextAnchor.MiddleCenter);
            toolbar.margin = new RectOffset(2, 2, 4, 4);

            panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(26, 26, 22, 22), border = new RectOffset(0, 0, 0, 0) };
            panel.normal.background = texPanel;
            card = new GUIStyle(GUI.skin.box) { padding = new RectOffset(16, 16, 12, 12), margin = new RectOffset(0, 0, 6, 6), border = new RectOffset(0, 0, 0, 0) };
            card.normal.background = texCard;

            hudClock = Text(lbl, 34, FontStyle.Bold, white, TextAnchor.MiddleCenter, false);
            hudSmall = Text(lbl, 13, FontStyle.Bold, grey, TextAnchor.MiddleCenter, false);
            hudText = Text(lbl, 15, FontStyle.Normal, white);
            hudBig = Text(lbl, 40, FontStyle.Bold, white, TextAnchor.LowerLeft, false);
            hudCenter = Text(lbl, 17, FontStyle.Bold, white, TextAnchor.MiddleCenter, false);
            feed = Text(lbl, 14, FontStyle.Bold, white, TextAnchor.MiddleRight, false);
            banner = Text(lbl, 46, FontStyle.Bold, white, TextAnchor.MiddleCenter, false);
            bannerSub = Text(lbl, 20, FontStyle.Normal, white, TextAnchor.MiddleCenter, true);
            botName = Text(lbl, 14, FontStyle.Bold, white, TextAnchor.LowerCenter, false);
            botLine = Text(lbl, 12, FontStyle.Normal, white, TextAnchor.LowerCenter, false);

            monoBig = Text(mono, 20, FontStyle.Bold, mono.normal.textColor, TextAnchor.UpperLeft, false);
            lead = Text(lbl, 18, FontStyle.Normal, white);
            debrief = Text(lbl, 16, FontStyle.Normal, white);
            verdictTitle = Text(lbl, 34, FontStyle.Bold, Color.white);
            severityLabel = Text(lbl, 15, FontStyle.Bold, Color.white, TextAnchor.UpperRight, false);
            toastStyle = Text(lbl, 14, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            hudTitle = Text(lbl, 17, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            hudSkill = Text(lbl, 13, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            hudGoal = Text(lbl, 13, FontStyle.Normal, Color.white);
            capLeft = Text(lbl, 12, FontStyle.Bold, grey, TextAnchor.UpperLeft, false);
            capRight = Text(lbl, 12, FontStyle.Bold, grey, TextAnchor.UpperRight, false);
            spikeStyle = Text(lbl, 15, FontStyle.Bold, Color.white, TextAnchor.MiddleRight, false);
        }

        // ---- drawing helpers ---------------------------------------------------------------------
        static void Fill(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Shadowed(Rect r, string text, GUIStyle s, Color c)
        {
            Color old = GUI.contentColor;
            GUI.contentColor = new Color(0f, 0f, 0f, 0.75f * c.a);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, s);
            GUI.contentColor = c;
            GUI.Label(r, text, s);
            GUI.contentColor = old;
        }

        void Bar(float v, Color c, float height)
        {
            Rect r = GUILayoutUtility.GetRect(100f, height, GUILayout.ExpandWidth(true));
            Fill(r, new Color(1f, 1f, 1f, 0.08f));
            Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(v), r.height), c);
        }

        /// <summary>The player's rate as a bar with a white tick at the benchmark.</summary>
        void RateBar(float rate, float benchmark)
        {
            Rect r = GUILayoutUtility.GetRect(100f, 8f, GUILayout.ExpandWidth(true));
            Fill(r, new Color(1f, 1f, 1f, 0.08f));
            Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(rate), r.height), rate >= benchmark ? Palette.Ok : Palette.Warn);
            float bx = r.x + r.width * Mathf.Clamp01(benchmark);
            Fill(new Rect(bx - 1.5f, r.y - 3f, 3f, r.height + 6f), Color.white);
        }

        bool Btn(string text, GUIStyle s, params GUILayoutOption[] o)
        {
            return GUILayout.Button(text, s, o);
        }

        Rect Centered(float w, float h)
        {
            w = Mathf.Min(w, W - 30f);
            h = Mathf.Min(h, H - 30f);
            return new Rect((W - w) / 2f, (H - h) / 2f, w, h);
        }

        bool Idle => Game.WaitingFor == null && Game.Connected;

        // ---- entry ------------------------------------------------------------------------------
        void OnGUI()
        {
            if (Game == null) return;
            EnsureStyles();
            scale = Mathf.Clamp(Screen.height / VirtualHeight, 0.6f, 3f);
            W = Screen.width / scale;
            H = Screen.height / scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            switch (Game.Screen)
            {
                case AppScreen.Connect: DrawConnect(); break;
                case AppScreen.Choose: DrawChoose(); break;
                case AppScreen.Report: DrawReport(); break;
                case AppScreen.Intro: DrawIntro(); break;
                case AppScreen.Playing: DrawHud(); break;
                case AppScreen.Result: DrawResult(); break;
            }
            DrawToast();
        }

        void DrawToast()
        {
            if (string.IsNullOrEmpty(Game.Toast) || Time.unscaledTime > Game.ToastUntil) return;
            var r = new Rect(W / 2f - 320f, H - 70f, 640f, 40f);
            Fill(r, new Color(0.55f, 0.16f, 0.16f, 0.92f));
            GUI.Label(new Rect(r.x + 12f, r.y, r.width - 24f, r.height), Game.Toast, toastStyle);
        }

        void Waiting()
        {
            if (Game.WaitingFor == null) return;
            int dots = 1 + (int)(Time.unscaledTime * 3f) % 3;
            GUILayout.Label("Waiting for the bridge" + new string('.', dots), muted);
        }

        // ---- (1) connect --------------------------------------------------------------------------
        void DrawConnect()
        {
            GUILayout.BeginArea(Centered(600f, 340f), panel);
            GUILayout.Label("SALTWORKS", title);
            GUILayout.Label("Tactical coaching client. The bridge runs the sim, the bots and the coach; this window is the eyes and hands.", muted);
            GUILayout.Space(16f);
            BridgeClient c = Game.Client;
            string where = c.Host + ":" + c.Port;
            switch (Game.Link)
            {
                case LinkState.Connecting:
                case LinkState.Idle:
                    GUILayout.Label("Connecting to the bridge at " + where + "...", body);
                    break;
                case LinkState.Connected:
                    GUILayout.Label("Connected to " + where + ". Waiting for the bridge's hello...", body);
                    break;
                default:
                    GUILayout.Label("Cannot reach the bridge at " + where + ".", bodyBold);
                    GUILayout.Space(6f);
                    GUILayout.Label("Start the bridge: npm run bridge", monoBig);
                    GUILayout.Label("Run it from the repository root, then press Reconnect.", muted);
                    GUILayout.Label("(" + (string.IsNullOrEmpty(Game.LinkError) ? "no answer" : Game.LinkError) + ")", small);
                    break;
            }
            GUILayout.FlexibleSpace();
            GUI.enabled = Game.Link != LinkState.Connecting;
            if (Btn("Reconnect", primary, GUILayout.Height(42f))) Game.Do(Game.ConnectNow);
            GUI.enabled = true;
            GUILayout.EndArea();
        }

        // ---- (2) choose a sample player ---------------------------------------------------------------
        void DrawChoose()
        {
            GUILayout.BeginArea(Centered(660f, 520f), panel);
            GUILayout.Label("Whose history?", title);
            GUILayout.Label("Pick a sample player. The bridge reads their matches, finds the weakest situation and builds drills from it.", muted);
            GUILayout.Space(12f);
            GUI.enabled = Idle;
            foreach (Choice s in GameController.Samples)
            {
                string id = s.Id;
                if (Btn(s.Label + "\n" + s.Blurb, bigButton, GUILayout.Height(62f))) Game.Do(() => Game.ChooseSample(id));
            }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            Waiting();
            if (Game.Report != null && Btn("Back to the current report", button)) Game.Do(Game.BackToReport);
            GUILayout.EndArea();
        }

        // ---- (3) weakness report ------------------------------------------------------------------------
        void DrawReport()
        {
            ReportMsg rm = Game.Report;
            if (rm == null)
            {
                DrawChoose();
                return;
            }
            WeaknessReport r = rm.report ?? new WeaknessReport();
            GUILayout.BeginArea(Centered(980f, H - 50f), panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Weakness report", title);
            GUILayout.FlexibleSpace();
            GUI.enabled = Idle;
            if (Btn("Choose another player", button, GUILayout.Width(220f))) Game.Do(Game.BackToChoose);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(string.Format("Player {0}, played as {1}. {2} matches read, {3} drills played this session.",
                r.playerId ?? "?", r.role ?? "?", r.matchesRead, rm.played), muted);
            if (rm.staircases != null && rm.staircases.Count > 0)
            {
                var parts = new List<string>();
                foreach (KeyValuePair<string, StaircaseInfo> kv in rm.staircases)
                    parts.Add(GameController.SkillName(kv.Key) + " " + (kv.Value != null ? kv.Value.difficulty.ToString("0.00") : "-"));
                GUILayout.Label("Drill difficulty:  " + string.Join("   ", parts), small);
            }

            reportScroll = GUILayout.BeginScrollView(reportScroll, false, false);
            if (r.findings == null || r.findings.Count == 0)
                GUILayout.Label("No findings yet: the history has too few situations to judge.", body);
            else
                foreach (WeaknessFinding f in r.findings) DrawFinding(f, r.role);

            GUILayout.Space(10f);
            GUI.enabled = Idle;
            if (rm.rehearsals != null && rm.rehearsals.Count > 0)
            {
                GUILayout.Label("Rehearse my mistakes", h2);
                GUILayout.Label("Each one rebuilds a moment from the history: same positions, same clock.", muted);
                foreach (RehearsalInfo ri in rm.rehearsals)
                {
                    int idx = ri.index;
                    string label = (ri.title ?? "Rehearsal " + idx) + "  (" + GameController.SkillName(ri.skill) + ")" + (string.IsNullOrEmpty(ri.brief) ? "" : "\n" + ri.brief);
                    if (Btn(label, bigButton, GUILayout.MinHeight(54f))) Game.Do(() => Game.RequestRehearsal(idx));
                }
                GUILayout.Space(10f);
            }

            GUILayout.Label("Next practice drill", h2);
            GUILayout.Label("A rehearsal of a failed moment first, then generated drills at your current difficulty.", muted);
            if (Btn("Next practice drill", primary, GUILayout.Height(46f))) Game.Do(Game.RequestNextDrill);

            GUILayout.Space(10f);
            GUILayout.Label("Versus an archetype", h2);
            GUILayout.Label("A free round against one bot playstyle. Not coaching practice.", muted);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Skill", bodyBold, GUILayout.Width(50f));
            Game.VersusSkill = GUILayout.Toolbar(Game.VersusSkill, SkillLabels, toolbar);
            GUILayout.EndHorizontal();
            bool wide = W > 820f;
            if (wide) GUILayout.BeginHorizontal();
            foreach (Choice a in GameController.Archetypes)
            {
                string id = a.Id;
                if (Btn(a.Label + "\n" + a.Blurb, wide ? button : bigButton, GUILayout.MinHeight(70f))) Game.Do(() => Game.RequestVersus(id));
            }
            if (wide) GUILayout.EndHorizontal();
            GUI.enabled = true;
            GUILayout.EndScrollView();
            Waiting();
            GUILayout.EndArea();
        }

        void DrawFinding(WeaknessFinding f, string role)
        {
            GUILayout.BeginVertical(card);
            GUILayout.BeginHorizontal();
            GUILayout.Label(GameController.SkillName(f.skill).ToUpperInvariant(), h3);
            GUILayout.FlexibleSpace();
            Color old = GUI.contentColor;
            GUI.contentColor = Palette.Severity(f.severity);
            GUILayout.Label("severity " + f.severity.ToString("0.00"), severityLabel);
            GUI.contentColor = old;
            GUILayout.EndHorizontal();
            Bar(f.severity, Palette.Severity(f.severity), 10f);
            GUILayout.Space(6f);
            GUILayout.Label(string.Format("Your rate {0:0}%  vs  benchmark {1:0}% for a {2}   ({3} situations)",
                f.rate * 100f, f.benchmark * 100f, role ?? "player", f.sampleSize), body);
            RateBar(f.rate, f.benchmark);
            GUILayout.Space(4f);
            if (!string.IsNullOrEmpty(f.summary)) GUILayout.Label(f.summary, body);
            if (f.evidence != null && f.evidence.Count > 0)
            {
                WeaknessEvidence e = f.evidence[0];
                GUILayout.Label(string.Format("Evidence: {0}, round {1}, {2:0.0} s: {3}", e.matchId, e.round, e.tick / 20f, e.note), small);
            }
            GUILayout.EndVertical();
        }

        // ---- (4) drill intro ----------------------------------------------------------------------------
        void DrawIntro()
        {
            DrillMsg d = Game.Drill;
            if (d == null) return;
            DrillSpec spec = d.spec ?? new DrillSpec();
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.35f));
            GUILayout.BeginArea(Centered(660f, 620f), panel);
            introScroll = GUILayout.BeginScrollView(introScroll, false, false);
            GUILayout.Label(spec.title ?? "Drill", title);
            GUILayout.Label(GameController.SkillName(spec.skill) + ", difficulty " + spec.difficulty.ToString("0.00"), muted);
            GUILayout.Space(10f);
            if (!string.IsNullOrEmpty(d.intro)) GUILayout.Label(d.intro, lead);
            if (!string.IsNullOrEmpty(spec.goal))
            {
                GUILayout.Space(6f);
                GUILayout.Label("Goal: " + spec.goal, body);
            }
            GUILayout.Space(10f);
            Color old = GUI.contentColor;
            GUI.contentColor = Palette.Team(Game.PlayerTeam);
            GUILayout.Label("You play " + Game.PlayerTeam + ".", bodyBold);
            GUI.contentColor = old;
            if (d.bots != null && d.bots.Count > 0)
            {
                GUILayout.Label("Bots", h3);
                foreach (DrillBotInfo b in d.bots)
                {
                    GUI.contentColor = Palette.Team(b.team);
                    GUILayout.Label("  " + b.id + "  " + b.team + "  " + GameController.ArchetypeName(b.archetype), body);
                }
                GUI.contentColor = old;
            }
            GUILayout.Space(10f);
            GUILayout.Label("Controls", h3);
            foreach (string[] c in Controls)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(c[0], mono, GUILayout.Width(110f));
                GUILayout.Label(c[1], small);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.Space(8f);
            if (Btn("Begin  (Enter)", primary, GUILayout.Height(48f))) Game.Do(Game.Begin);
            GUILayout.Label("The clock starts when you begin. The mouse is captured; Esc pauses.", small);
            GUILayout.EndArea();
        }

        // ---- in-drill HUD ------------------------------------------------------------------------------
        void DrawHud()
        {
            SimState s = Game.State;
            AgentState me = Game.Me;
            float now = Time.unscaledTime;
            Rules rules = Game.Rules;
            DrillSpec spec = Game.Drill?.spec;
            if (s == null) return;

            // Screen-space effects first, so text stays readable on top.
            Camera cam = Game.Rig.Cam;
            float smoke = Game.World.SmokeDensityAt(cam.transform.position);
            if (smoke > 0f) Fill(new Rect(0f, 0f, W, H), new Color(Palette.Smoke.r * 0.9f, Palette.Smoke.g * 0.9f, Palette.Smoke.b * 0.9f, 0.92f * smoke));
            float dmg = 1f - Mathf.Clamp01((now - Game.DamageAt) / 0.45f);
            if (dmg > 0f) Fill(new Rect(0f, 0f, W, H), new Color(0.9f, 0.1f, 0.1f, 0.3f * dmg));

            DrawBotLabels(cam);

            bool alive = me != null && me.alive;
            if (alive && !Game.Paused) DrawCrosshair(now);

            // Top centre: clock.
            bool post = s.phase == "post_plant";
            float t = Mathf.Max(0f, s.roundTimeLeftS);
            var clockR = new Rect(W / 2f - 90f, 12f, 180f, 62f);
            Fill(clockR, new Color(0.08f, 0.09f, 0.12f, 0.75f));
            Fill(new Rect(clockR.x, clockR.yMax - 3f, clockR.width, 3f), post ? Palette.Spike : Palette.Team(Game.PlayerTeam));
            string clock = string.Format("{0}:{1:00}", (int)(t / 60f), (int)t % 60) + (t < 10f ? "." + ((int)(t * 10f) % 10) : "");
            Shadowed(new Rect(clockR.x, clockR.y + 2f, clockR.width, 42f), clock, hudClock, post ? new Color(1f, 0.55f, 0.55f) : Color.white);
            GUI.Label(new Rect(clockR.x, clockR.y + 40f, clockR.width, 18f), post ? "SPIKE PLANTED" : (s.phase ?? "").Replace('_', ' ').ToUpperInvariant(), hudSmall);

            // Top left: drill.
            if (spec != null)
            {
                var r = new Rect(16f, 14f, 420f, 120f);
                Shadowed(new Rect(r.x, r.y, r.width, 24f), spec.title ?? "", hudTitle, Color.white);
                Shadowed(new Rect(r.x, r.y + 24f, r.width, 20f), GameController.SkillName(spec.skill) + "  |  difficulty " + spec.difficulty.ToString("0.00"), hudSkill, Palette.Team(Game.PlayerTeam));
                if (!string.IsNullOrEmpty(spec.goal)) Shadowed(new Rect(r.x, r.y + 44f, r.width, 70f), spec.goal, hudGoal, new Color(0.9f, 0.9f, 0.92f));
            }

            // Top right: kill feed.
            float fy = 14f;
            for (int i = Game.Feed.Count - 1; i >= 0; i--)
            {
                FeedItem item = Game.Feed[i];
                float age = now - item.At;
                if (age > 7f) continue;
                float a = Mathf.Clamp01((7f - age) / 1f);
                var r = new Rect(W - 516f, fy, 500f, 24f);
                Vector2 size = feed.CalcSize(new GUIContent(item.Text));
                Fill(new Rect(r.xMax - size.x - 12f, r.y, size.x + 12f, r.height), new Color(0.06f, 0.07f, 0.09f, 0.6f * a));
                Fill(new Rect(r.xMax - 3f, r.y, 3f, r.height), new Color(item.Color.r, item.Color.g, item.Color.b, a));
                Shadowed(new Rect(r.x, r.y, r.width - 8f, r.height), item.Text, feed, new Color(1f, 1f, 1f, a));
                fy += 28f;
            }

            // Bottom left: health and smokes.
            float hp = me != null ? Mathf.Max(0f, me.hp) : 0f;
            Color hpc = hp > 50f ? Palette.Ok : hp > 25f ? Palette.Warn : Palette.Bad;
            Shadowed(new Rect(20f, H - 92f, 160f, 50f), alive ? Mathf.RoundToInt(hp).ToString() : "DOWN", hudBig, alive ? Color.white : Palette.Bad);
            GUI.Label(new Rect(22f, H - 42f, 200f, 18f), "HEALTH", capLeft);
            Fill(new Rect(20f, H - 24f, 220f, 8f), new Color(1f, 1f, 1f, 0.15f));
            Fill(new Rect(20f, H - 24f, 220f * Mathf.Clamp01(hp / 100f), 8f), hpc);
            int smokes = me != null ? me.smokesLeft : 0;
            GUI.Label(new Rect(262f, H - 42f, 200f, 18f), "SMOKES  Q / E", capLeft);
            for (int i = 0; i < Mathf.Max(smokes, 2); i++)
                Fill(new Rect(262f + i * 22f, H - 26f, 16f, 12f), i < smokes ? Palette.Smoke : new Color(1f, 1f, 1f, 0.15f));

            // Bottom right: spike.
            string spikeText = SpikeText(s);
            var sr = new Rect(W - 420f, H - 50f, 400f, 30f);
            Shadowed(sr, spikeText, spikeStyle, s.spike != null && s.spike.status == "planted" ? new Color(1f, 0.6f, 0.6f) : Color.white);
            GUI.Label(new Rect(sr.x, sr.y - 18f, sr.width, 18f), "SPIKE", capRight);

            // Centre: plant / defuse progress and hints.
            float cx = W / 2f, cy = H / 2f;
            float progress = -1f;
            string what = null;
            if (s.spike != null && s.spike.status == "planted" && s.spike.defuseProgressS > 0f)
            {
                progress = s.spike.defuseProgressS / Mathf.Max(0.1f, rules.defuseTimeS);
                what = "DEFUSING";
            }
            else if (Game.PlantTicks > 0)
            {
                progress = Game.PlantTicks / Mathf.Max(1f, rules.plantTimeS * rules.tickRate);
                what = "PLANTING";
            }
            if (progress >= 0f)
            {
                var pr = new Rect(cx - 140f, cy + 70f, 280f, 12f);
                Shadowed(new Rect(pr.x, pr.y - 26f, pr.width, 22f), what + "  " + Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f) + "%", hudCenter, Color.white);
                Fill(pr, new Color(0f, 0f, 0f, 0.5f));
                Fill(new Rect(pr.x, pr.y, pr.width * Mathf.Clamp01(progress), pr.height), what == "DEFUSING" ? Palette.Defense : Palette.Attack);
            }
            else if (alive && !Game.DrillDone)
            {
                string hint = null;
                if (Game.PlayerTeam == "attack" && me.carryingSpike && Game.OnSite(me.pos)) hint = "Hold F to plant";
                else if (Game.PlayerTeam == "defense" && Game.NearPlantedSpike(me.pos)) hint = "Hold F to defuse";
                if (hint != null) Shadowed(new Rect(cx - 150f, cy + 56f, 300f, 24f), hint, hudCenter, new Color(1f, 0.95f, 0.8f));
            }
            if (!alive) Shadowed(new Rect(cx - 250f, cy + 40f, 500f, 30f), "You are down. The round plays out.", hudCenter, new Color(1f, 0.7f, 0.7f));
            if (Game.DrillDone && Game.Result == null) Shadowed(new Rect(cx - 250f, cy + 100f, 500f, 30f), "Round over. Waiting for the verdict...", hudCenter, Color.white);

            // Verdict banner.
            if (Game.Result != null && now < Game.VerdictUntil)
            {
                bool ok = Game.Result.success;
                var br = new Rect(0f, H * 0.28f, W, 120f);
                Fill(br, new Color(0.05f, 0.06f, 0.08f, 0.78f));
                Fill(new Rect(0f, br.y, W, 4f), ok ? Palette.Ok : Palette.Bad);
                Fill(new Rect(0f, br.yMax - 4f, W, 4f), ok ? Palette.Ok : Palette.Bad);
                Shadowed(new Rect(0f, br.y + 10f, W, 60f), ok ? "DRILL COMPLETE" : "NOT THIS TIME", banner, ok ? Palette.Ok : Palette.Bad);
                Shadowed(new Rect(40f, br.y + 70f, W - 80f, 40f), Game.Result.headline ?? "", bannerSub, Color.white);
            }

            if (Game.Paused && !Game.DrillDone) DrawPause();
        }

        string SpikeText(SimState s)
        {
            SpikeState sp = s.spike;
            if (sp == null) return "";
            switch (sp.status)
            {
                case "carried":
                    return sp.carrierId == Game.PlayerId ? "You carry the spike" : Game.Name(sp.carrierId) + " carries the spike";
                case "dropped":
                    return "Spike dropped";
                case "planted":
                    string text = "Planted on " + sp.site + ", " + Mathf.Max(0f, s.roundTimeLeftS).ToString("0") + " s to detonation";
                    if (sp.defuseProgressS > 0f) text += ", defuse " + Mathf.RoundToInt(sp.defuseProgressS / Mathf.Max(0.1f, Game.Rules.defuseTimeS) * 100f) + "%";
                    return text;
                case "defused":
                    return "Spike defused";
                default:
                    return s.phase == "ended" ? "Spike detonated" : "No spike in play";
            }
        }

        void DrawCrosshair(float now)
        {
            float cx = W / 2f, cy = H / 2f;
            Color outline = new Color(0f, 0f, 0f, 0.7f);
            Color c = new Color(0.55f, 1f, 0.9f, 0.95f);
            const float len = 7f, gap = 4f, th = 2f;
            void Arm(float x, float y, float w, float h)
            {
                Fill(new Rect(x - 1f, y - 1f, w + 2f, h + 2f), outline);
                Fill(new Rect(x, y, w, h), c);
            }
            Arm(cx - gap - len, cy - th / 2f, len, th);
            Arm(cx + gap, cy - th / 2f, len, th);
            Arm(cx - th / 2f, cy - gap - len, th, len);
            Arm(cx - th / 2f, cy + gap, th, len);
            Fill(new Rect(cx - 1f, cy - 1f, 2f, 2f), c);

            float age = now - Game.HitMarkerAt;
            if (age < 0.28f)
            {
                float a = 1f - age / 0.28f;
                Color hc = Game.HitMarkerKill ? new Color(1f, 0.3f, 0.3f, a) : Game.HitMarkerHead ? new Color(1f, 0.85f, 0.2f, a) : new Color(1f, 1f, 1f, a);
                float l = Game.HitMarkerKill ? 12f : 9f;
                float g = 6f + (1f - a) * 3f;
                Matrix4x4 m = GUI.matrix;
                GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy) * scale);
                Fill(new Rect(cx - g - l, cy - 1.5f, l, 3f), hc);
                Fill(new Rect(cx + g, cy - 1.5f, l, 3f), hc);
                Fill(new Rect(cx - 1.5f, cy - g - l, 3f, l), hc);
                Fill(new Rect(cx - 1.5f, cy + g, 3f, l), hc);
                GUI.matrix = m;
            }
        }

        void DrawBotLabels(Camera cam)
        {
            if (Event.current.type != EventType.Repaint) return;
            Physics.SyncTransforms();
            Vector3 eye = cam.transform.position;
            foreach (BotView b in Game.World.Bots.Values)
            {
                if (!b.Alive) continue;
                Vector3 anchor = b.Root.transform.position + Vector3.up * (BotView.Height + 0.35f);
                Vector3 sp = cam.WorldToScreenPoint(anchor);
                if (sp.z <= 0.1f || sp.z > 90f) continue;
                if (Occluded(eye, b)) continue;
                float x = sp.x / scale;
                float y = (Screen.height - sp.y) / scale;
                Color tc = Color.Lerp(Palette.Team(b.Team), Color.white, 0.25f);
                Shadowed(new Rect(x - 150f, y - 52f, 300f, 18f), b.Id, botName, tc);
                Shadowed(new Rect(x - 150f, y - 35f, 300f, 16f), GameController.ArchetypeName(b.Archetype), botLine, new Color(0.92f, 0.92f, 0.95f));
                if (!string.IsNullOrEmpty(b.Thought))
                    Shadowed(new Rect(x - 150f, y - 19f, 300f, 16f), b.Thought.Replace('_', ' '), botLine, new Color(0.65f, 0.95f, 0.88f));
            }
        }

        /// <summary>True when a wall (anything that is not a bot) or a smoke is between the eye and the bot's head.</summary>
        bool Occluded(Vector3 eye, BotView b)
        {
            Vector3 head = b.HeadPos;
            Vector3 d = head - eye;
            float dist = d.magnitude;
            if (dist < 0.01f) return false;
            int n = Physics.RaycastNonAlloc(eye, d / dist, hits, dist - 0.05f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (hits[i].collider != null && hits[i].collider.GetComponent<BotPart>() == null) return true;
            return Game.World.SmokeBlocks(eye, head);
        }

        void DrawPause()
        {
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.55f));
            GUILayout.BeginArea(Centered(520f, 520f), panel);
            GUILayout.Label("Paused", title);
            GUILayout.Label("No inputs are sent while paused, so the sim waits for you.", muted);
            GUILayout.Space(10f);
            if (Btn("Resume", primary, GUILayout.Height(46f))) Game.Do(Game.Resume);
            if (Btn("Abandon drill (counts as a fail)", button, GUILayout.Height(40f))) Game.Do(Game.AbandonDrill);
            GUILayout.Space(12f);
            GUILayout.Label("Controls", h3);
            foreach (string[] c in Controls)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(c[0], mono, GUILayout.Width(110f));
                GUILayout.Label(c[1], small);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        // ---- (5) result ------------------------------------------------------------------------------------
        void DrawResult()
        {
            ResultMsg res = Game.Result;
            if (res == null) return;
            DrillSpec spec = Game.Drill?.spec ?? new DrillSpec();
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.3f));
            GUILayout.BeginArea(Centered(760f, H - 60f), panel);
            resultScroll = GUILayout.BeginScrollView(resultScroll, false, false);
            Color old = GUI.contentColor;
            GUI.contentColor = res.success ? Palette.Ok : Palette.Bad;
            GUILayout.Label(res.success ? "DRILL COMPLETE" : "NOT THIS TIME", verdictTitle);
            GUI.contentColor = old;
            if (!string.IsNullOrEmpty(res.headline)) GUILayout.Label(res.headline, h2);
            int ticks = res.result != null ? res.result.ticks : 0;
            GUILayout.Label((spec.title ?? "Drill") + "  (" + GameController.SkillName(spec.skill) + ", " + (ticks / Mathf.Max(1f, Game.Rules.tickRate)).ToString("0.0") + " s)", bodyBold);
            if (!string.IsNullOrEmpty(spec.goal)) GUILayout.Label(spec.goal, muted);

            GUILayout.Space(10f);
            GUILayout.Label("Difficulty", h3);
            if (res.staircaseBefore.HasValue && res.staircaseAfter.HasValue)
            {
                float b = res.staircaseBefore.Value, a = res.staircaseAfter.Value;
                string change = a > b + 1e-4f ? "harder" : a < b - 1e-4f ? "easier" : "the same";
                GUILayout.Label(string.Format("{0} drills go from {1:0.00} -> {2:0.00} ({3}).", GameController.SkillName(spec.skill), b, a, change), body);
                Rect r = GUILayoutUtility.GetRect(100f, 10f, GUILayout.ExpandWidth(true));
                Fill(r, new Color(1f, 1f, 1f, 0.08f));
                Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(a), r.height), a >= b ? Palette.Attack : Palette.Defense);
                Fill(new Rect(r.x + r.width * Mathf.Clamp01(b) - 1.5f, r.y - 3f, 3f, r.height + 6f), Color.white);
            }
            else
            {
                GUILayout.Label("No staircase change for this round.", muted);
            }

            List<string> lines = WhatHappened(res);
            if (lines.Count > 0)
            {
                GUILayout.Space(10f);
                GUILayout.Label("What happened", h3);
                foreach (string l in lines) GUILayout.Label(l, small);
            }

            GUILayout.Space(10f);
            GUILayout.Label("Coach", h3);
            DebriefMsg db = Game.Debrief;
            if (db == null)
            {
                int dots = 1 + (int)(Time.unscaledTime * 3f) % 3;
                GUILayout.Label("Coach is writing" + new string('.', dots), muted);
            }
            else
            {
                GUILayout.Label(db.text ?? "", debrief);
                GUILayout.Label("Written by: " + (db.source ?? "?"), small);
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            // Like the 2D client, wait for the debrief before the next drill (with a timeout in case it never comes).
            GUI.enabled = Idle && (db != null || Time.unscaledTime - Game.ResultAt > 15f);
            if (Btn("Next drill", primary, GUILayout.Height(46f))) Game.Do(Game.RequestNextDrill);
            GUI.enabled = true;
            if (Btn("Back to report", button, GUILayout.Height(46f))) Game.Do(Game.BackToReport);
            GUILayout.EndHorizontal();
            Waiting();
            GUILayout.EndArea();
        }

        List<string> WhatHappened(ResultMsg res)
        {
            var lines = new List<string>();
            if (res.result?.events == null) return lines;
            float rate = Mathf.Max(1f, Game.Rules.tickRate);
            foreach (SimEvent e in res.result.events)
            {
                string at = (e.tick / rate).ToString("0.0") + " s  ";
                switch (e.type)
                {
                    case "kill": lines.Add(at + Game.Name(e.killer) + " killed " + Game.Name(e.victim) + (e.part == "head" ? " (headshot)" : "")); break;
                    case "smoke": lines.Add(at + Game.Name(e.agent) + " threw a smoke"); break;
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

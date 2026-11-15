// All screens and the HUD, drawn with IMGUI and custom GUIStyles (no Canvas,
// no TextMeshPro). Layout works in a virtual 900-pixel-high space scaled to the
// window. Buttons queue their action through GameController.Do so a screen
// change never happens between IMGUI's layout and repaint passes.
//
// Look: rounded 9-sliced panels and buttons (Theme), one type scale, the
// accent colour from the settings, a short fade-and-rise when a screen opens.
// The in-drill HUD uses only GUI.* calls (no GUILayout), so the layout pass is
// switched off while playing, and its strings are cached so it allocates
// nothing per frame. The crosshair and minimap are drawn with GL (Draw2D).
//
// Split across ClientUI.cs (screens, HUD), ClientUI.Settings.cs and ClientUI.Advice.cs.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI : MonoBehaviour
    {
        public GameController Game;

        const float VirtualHeight = 900f;

        float scale = 1f;
        float W, H;
        bool ready;
        int stylesAccent = -1;
        Vector2 reportScroll, resultScroll, introScroll, settingsScroll;
        AppScreen shownScreen = (AppScreen)(-1);
        bool shownSettings;
        float screenAt;
        float spread;

        GUIStyle title, display, h2, h3, body, bodyBold, muted, small, caption, mono;
        GUIStyle button, primary, bigButton, toolbar, chosenButton, ghost, tab, tabOn;
        GUIStyle panel, card, roundWhite, roundSmall, badge;
        GUIStyle slider, thumb;
        GUIStyle hudClock, hudSmall, hudBig, hudCenter, feed, banner, bannerSub, botName, botLine, mapLetter, fpsStyle;
        GUIStyle monoBig, lead, debrief, verdictTitle, severityLabel, toastStyle, hudTitle, hudSkill, hudGoal, capLeft, capRight, spikeStyle;
        GUIStyle slotKey, slotCount, slotName, modeStyle;
        readonly List<Texture2D> owned = new List<Texture2D>();

        static readonly string[] SkillLabels = { "Entry", "Clutch", "Time pressure", "Smoke usage" };
        static readonly string[] AbilityUpper = { "SMOKE", "FLASH", "DASH", "RECON" };

        static readonly string[][] Controls =
        {
            new[] { "Mouse", "Look (yaw goes to the sim, pitch stays here)" },
            new[] { "W A S D", "Move (running is heard through walls within 15 m)" },
            new[] { "Shift", "Walk (quiet: heard only within 3 m)" },
            new[] { "Ctrl", "Crouch (quiet, slowest)" },
            new[] { "Left click", "Fire (pistols, the Brick and the Longshot: one shot per click)" },
            new[] { "Right click", "Scope (Longshot): zoom, slower turn, accurate only when still" },
            new[] { "1 / 2 / wheel", "Primary / secondary weapon / switch" },
            new[] { "R", "Reload" },
            new[] { "B", "Buy menu during a match's buy phase" },
            new[] { "Hold C", "Smoke: aim at the floor, release to throw" },
            new[] { "Hold Q", "Flash: aim, release to throw; it pops 0.6 s later. Look away!" },
            new[] { "E", "Dash in the direction you are moving" },
            new[] { "Hold X", "Recon dart: aim, release; reveals enemies near it through walls" },
            new[] { "Hold F", "Plant (attack, on a site, with the spike) or defuse (defense, at the spike)" },
            new[] { "M / hold Tab", "Minimap on or off / full map" },
            new[] { "F3", "Frame rate and prediction readout" },
            new[] { "Esc", "Pause (settings are in the pause menu)" },
        };

        // ---- styles --------------------------------------------------------------------------
        Texture2D Own(Texture2D t)
        {
            owned.Add(t);
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

        static GUIStyle Button(GUIStyle from, int size, Texture2D n, Texture2D h, Texture2D a, Texture2D on, Color text, TextAnchor align, int radius)
        {
            var s = new GUIStyle(from)
            {
                fontSize = size,
                alignment = align,
                wordWrap = true,
                richText = false,
                border = Theme.Border(radius),
                padding = new RectOffset(16, 16, 10, 10),
                margin = new RectOffset(4, 4, 5, 5),
            };
            s.normal.background = n;
            s.hover.background = h;
            s.active.background = a;
            s.focused.background = n;
            s.onNormal.background = on;
            s.onHover.background = on;
            s.onActive.background = on;
            s.onFocused.background = on;
            s.normal.textColor = text;
            s.hover.textColor = Color.white;
            s.active.textColor = text;
            s.focused.textColor = text;
            s.onNormal.textColor = Color.white;
            s.onHover.textColor = Color.white;
            s.onActive.textColor = Color.white;
            return s;
        }

        void EnsureStyles()
        {
            if (ready && stylesAccent == Settings.AccentColor) return;
            foreach (Texture2D t in owned) if (t != null) Destroy(t);
            owned.Clear();
            ready = true;
            stylesAccent = Settings.AccentColor;
            Color accent = Settings.Accent;

            Texture2D texPanel = Own(Theme.Rounded(Theme.PanelC, Theme.Radius, new Color(1f, 1f, 1f, 0.07f)));
            Texture2D texCard = Own(Theme.Rounded(Theme.CardC, Theme.Radius, new Color(1f, 1f, 1f, 0.05f)));
            Texture2D texButton = Own(Theme.Rounded(Theme.ButtonC, 8));
            Texture2D texButtonHover = Own(Theme.Rounded(Theme.ButtonHoverC, 8, new Color(accent.r, accent.g, accent.b, 0.55f)));
            Texture2D texButtonDown = Own(Theme.Rounded(Theme.ButtonDownC, 8));
            Texture2D texChosen = Own(Theme.Rounded(Theme.Darken(accent, 0.55f), 8, accent, 1.5f));
            Texture2D texPrimary = Own(Theme.Rounded(accent, 8));
            Texture2D texPrimaryHover = Own(Theme.Rounded(Theme.Lighten(accent, 0.18f), 8));
            Texture2D texPrimaryDown = Own(Theme.Rounded(Theme.Darken(accent, 0.2f), 8));
            Texture2D texGhostHover = Own(Theme.Rounded(new Color(1f, 1f, 1f, 0.07f), 8));
            Texture2D texWhite = Own(Theme.Rounded(Color.white, Theme.Radius));
            Texture2D texTrack = Own(Theme.Rounded(new Color(1f, 1f, 1f, 0.14f), 3));
            Texture2D texThumb = Own(Theme.Rounded(Color.white, 8, new Color(accent.r, accent.g, accent.b, 1f), 2.5f));
            Texture2D texThumbHover = Own(Theme.Rounded(Theme.Lighten(accent, 0.6f), 8, accent, 2.5f));

            GUIStyle lbl = GUI.skin.label;
            Color white = Theme.Ink, grey = Theme.Muted;
            display = Text(lbl, Theme.Display, FontStyle.Bold, white);
            title = Text(lbl, Theme.Title, FontStyle.Bold, white);
            h2 = Text(lbl, Theme.H2, FontStyle.Bold, white);
            h3 = Text(lbl, Theme.H3, FontStyle.Bold, white);
            body = Text(lbl, Theme.Body, FontStyle.Normal, white);
            bodyBold = Text(lbl, Theme.Body, FontStyle.Bold, white);
            muted = Text(lbl, 14, FontStyle.Normal, grey);
            small = Text(lbl, Theme.Small, FontStyle.Normal, grey);
            caption = Text(lbl, Theme.Caption, FontStyle.Bold, grey, TextAnchor.UpperLeft, false);
            mono = Text(lbl, 14, FontStyle.Bold, Theme.Lighten(accent, 0.35f), TextAnchor.UpperLeft, false);

            button = Button(GUI.skin.button, Theme.Body, texButton, texButtonHover, texButtonDown, texChosen, white, TextAnchor.MiddleCenter, 8);
            primary = Button(GUI.skin.button, 16, texPrimary, texPrimaryHover, texPrimaryDown, texPrimary, new Color(0.08f, 0.07f, 0.08f), TextAnchor.MiddleCenter, 8);
            primary.fontStyle = FontStyle.Bold;
            primary.hover.textColor = new Color(0.05f, 0.05f, 0.06f);
            primary.active.textColor = new Color(0.05f, 0.05f, 0.06f);
            bigButton = Button(GUI.skin.button, Theme.Body, texButton, texButtonHover, texButtonDown, texChosen, white, TextAnchor.MiddleLeft, 8);
            toolbar = Button(GUI.skin.button, 14, texButton, texButtonHover, texButtonDown, texChosen, white, TextAnchor.MiddleCenter, 8);
            toolbar.margin = new RectOffset(2, 2, 3, 3);
            toolbar.padding = new RectOffset(10, 10, 7, 7);
            chosenButton = Button(GUI.skin.button, Theme.Body, texChosen, texChosen, texChosen, texChosen, Color.white, TextAnchor.MiddleLeft, 8);
            chosenButton.fontStyle = FontStyle.Bold;
            ghost = Button(GUI.skin.button, 14, null, texGhostHover, texButtonDown, texChosen, grey, TextAnchor.MiddleCenter, 8);
            tab = Button(GUI.skin.button, Theme.Body, null, texGhostHover, texButtonDown, texChosen, grey, TextAnchor.MiddleLeft, 8);
            tab.padding = new RectOffset(14, 14, 11, 11);
            tabOn = Button(GUI.skin.button, Theme.Body, texChosen, texChosen, texChosen, texChosen, Color.white, TextAnchor.MiddleLeft, 8);
            tabOn.padding = tab.padding;
            tabOn.fontStyle = FontStyle.Bold;

            panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(28, 28, 24, 24), border = Theme.Border(Theme.Radius) };
            panel.normal.background = texPanel;
            card = new GUIStyle(GUI.skin.box) { padding = new RectOffset(16, 16, 14, 14), margin = new RectOffset(0, 0, 6, 6), border = Theme.Border(Theme.Radius) };
            card.normal.background = texCard;
            roundWhite = new GUIStyle { border = Theme.Border(Theme.Radius) };
            roundWhite.normal.background = texWhite;
            roundSmall = new GUIStyle { border = Theme.Border(2) };
            roundSmall.normal.background = Own(Theme.Rounded(Color.white, 2));
            badge = Text(lbl, Theme.Caption, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            badge.padding = new RectOffset(8, 8, 3, 3);

            slider = new GUIStyle(GUI.skin.horizontalSlider) { border = new RectOffset(5, 5, 3, 3), fixedHeight = 6f, margin = new RectOffset(4, 4, 10, 10), padding = new RectOffset(0, 0, 0, 0), overflow = new RectOffset(0, 0, 0, 0) };
            slider.normal.background = texTrack;
            slider.hover.background = texTrack;
            slider.active.background = texTrack;
            slider.focused.background = texTrack;
            // The thumb's rect matches the 6 px track; overflow draws it as an 18 px knob centred on it.
            thumb = new GUIStyle(GUI.skin.horizontalSliderThumb) { border = new RectOffset(0, 0, 0, 0), fixedWidth = 6f, fixedHeight = 6f, overflow = new RectOffset(6, 6, 6, 6), margin = new RectOffset(0, 0, 0, 0), padding = new RectOffset(0, 0, 0, 0) };
            thumb.normal.background = texThumb;
            thumb.hover.background = texThumbHover;
            thumb.active.background = texThumbHover;
            thumb.focused.background = texThumb;

            hudClock = Text(lbl, 34, FontStyle.Bold, white, TextAnchor.MiddleCenter, false);
            hudSmall = Text(lbl, 12, FontStyle.Bold, grey, TextAnchor.MiddleCenter, false);
            hudBig = Text(lbl, 40, FontStyle.Bold, white, TextAnchor.LowerLeft, false);
            hudCenter = Text(lbl, 17, FontStyle.Bold, white, TextAnchor.MiddleCenter, false);
            feed = Text(lbl, 14, FontStyle.Bold, white, TextAnchor.MiddleRight, false);
            banner = Text(lbl, 46, FontStyle.Bold, white, TextAnchor.MiddleCenter, false);
            bannerSub = Text(lbl, 20, FontStyle.Normal, white, TextAnchor.MiddleCenter, true);
            botName = Text(lbl, 14, FontStyle.Bold, white, TextAnchor.LowerCenter, false);
            botLine = Text(lbl, 12, FontStyle.Normal, white, TextAnchor.LowerCenter, false);
            mapLetter = Text(lbl, 15, FontStyle.Bold, white, TextAnchor.MiddleCenter, false);
            fpsStyle = Text(lbl, 12, FontStyle.Bold, white, TextAnchor.UpperRight, false);

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
            slotKey = Text(lbl, 13, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            slotCount = Text(lbl, 14, FontStyle.Bold, Color.white, TextAnchor.LowerRight, false);
            slotName = Text(lbl, 11, FontStyle.Bold, grey, TextAnchor.UpperCenter, false);
            modeStyle = Text(lbl, 13, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            EnsureMatchStyles();
        }

        void OnDestroy()
        {
            foreach (Texture2D t in owned) if (t != null) Destroy(t);
            owned.Clear();
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

        /// <summary>A rounded rectangle in colour `c` (alpha multiplies with the current GUI colour).</summary>
        void Round(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            float m = Mathf.Min(r.width, r.height);
            // Thin shapes use a smaller corner so the 9-slice borders never overlap.
            if (m >= 20f) roundWhite.Draw(r, false, false, false, false);
            else if (m >= 8f) roundSmall.Draw(r, false, false, false, false);
            else GUI.DrawTexture(r, Texture2D.whiteTexture);
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
            Round(r, new Color(1f, 1f, 1f, 0.08f));
            if (v > 0.01f) Round(new Rect(r.x, r.y, Mathf.Max(height, r.width * Mathf.Clamp01(v)), r.height), c);
        }

        /// <summary>The player's rate as a bar with a white tick at the benchmark.</summary>
        void RateBar(float rate, float benchmark)
        {
            Rect r = GUILayoutUtility.GetRect(100f, 8f, GUILayout.ExpandWidth(true));
            Round(r, new Color(1f, 1f, 1f, 0.08f));
            if (rate > 0.01f) Round(new Rect(r.x, r.y, Mathf.Max(8f, r.width * Mathf.Clamp01(rate)), r.height), rate >= benchmark ? Palette.Ok : Palette.Warn);
            float bx = r.x + r.width * Mathf.Clamp01(benchmark);
            Fill(new Rect(bx - 1.5f, r.y - 3f, 3f, r.height + 6f), Color.white);
        }

        /// <summary>A small rounded label chip.</summary>
        void Badge(string text, Color bg, Color fg)
        {
            Vector2 size = badge.CalcSize(new GUIContent(text));
            Rect r = GUILayoutUtility.GetRect(size.x, 20f, GUILayout.Width(size.x), GUILayout.Height(20f));
            Round(r, bg);
            Color old = GUI.contentColor;
            GUI.contentColor = fg;
            GUI.Label(r, text, badge);
            GUI.contentColor = old;
        }

        bool Btn(string text, GUIStyle s, params GUILayoutOption[] o)
        {
            return GUILayout.Button(text, s, o);
        }

        /// <summary>The opening animation of the current screen: 0 just opened, 1 settled.</summary>
        float Intro01 => Theme.Ease((Time.unscaledTime - screenAt) / 0.28f);

        Rect Centered(float w, float h)
        {
            w = Mathf.Min(w, W - 30f);
            h = Mathf.Min(h, H - 30f);
            float rise = (1f - Intro01) * 14f;
            return new Rect(Mathf.Round((W - w) / 2f), Mathf.Round((H - h) / 2f + rise), w, h);
        }

        void SectionHeader(string text)
        {
            GUILayout.Space(Theme.S2);
            GUILayout.Label(text.ToUpperInvariant(), caption);
            Rect r = GUILayoutUtility.GetRect(10f, 2f, GUILayout.ExpandWidth(true));
            Fill(new Rect(r.x, r.y, 28f, 2f), Settings.Accent);
            GUILayout.Space(Theme.S1);
        }

        bool Idle => Game.WaitingFor == null && Game.Connected;

        // ---- entry ------------------------------------------------------------------------------
        void LateUpdate()
        {
            if (Game == null) return;
            // The HUD uses only GUI.* calls: skip IMGUI's layout pass while playing (it is pure overhead there).
            useGUILayout = !(Game.Screen == AppScreen.Playing && !Game.Paused && !Game.SettingsOpen);
            if (Game.Screen != shownScreen || Game.SettingsOpen != shownSettings)
            {
                shownScreen = Game.Screen;
                shownSettings = Game.SettingsOpen;
                screenAt = Time.unscaledTime;
            }
        }

        void OnGUI()
        {
            if (Game == null) return;
            EnsureStyles();
            scale = Mathf.Clamp(Screen.height / VirtualHeight, 0.6f, 3f);
            W = Screen.width / scale;
            H = Screen.height / scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            bool layout = useGUILayout;

            bool menu = Game.Screen != AppScreen.Playing;
            GUI.color = new Color(1f, 1f, 1f, menu && !Game.SettingsOpen ? Mathf.Lerp(0.2f, 1f, Intro01) : 1f);
            switch (Game.Screen)
            {
                case AppScreen.Connect: DrawConnect(); break;
                case AppScreen.Menu: DrawMenu(); break;
                case AppScreen.MatchSetup: DrawMatchSetup(); break;
                case AppScreen.Buy:
                    DrawBuy(false);
                    DrawRoundBanners(Time.unscaledTime);
                    break;
                case AppScreen.MatchEnd: DrawMatchEnd(); break;
                case AppScreen.Choose: DrawChoose(); break;
                case AppScreen.MapSelect: DrawMapSelect(); break;
                case AppScreen.AgentSelect: DrawAgentSelect(); break;
                case AppScreen.Report: DrawReport(); break;
                case AppScreen.Intro: DrawIntro(); break;
                case AppScreen.Playing: DrawHud(layout); break;
                case AppScreen.Result: DrawResult(); break;
            }
            GUI.color = Color.white;
            if (menu && !Game.SettingsOpen && Game.Screen != AppScreen.Menu && Game.Screen != AppScreen.Buy && Game.Screen != AppScreen.MatchEnd) DrawSettingsButton();
            if (Game.SettingsOpen && layout) DrawSettings();
            DrawFps();
            DrawToast();
        }

        void DrawSettingsButton()
        {
            var r = new Rect(W - 146f, 16f, 130f, 38f);
            if (GUI.Button(r, "Settings", ghost)) Game.Do(Game.OpenSettings);
        }

        string fpsText = "";
        int fpsKey = -1;

        void DrawFps()
        {
            if (!Settings.ShowFps) return;
            int key = Mathf.RoundToInt(Game.Fps) * 10000 + Mathf.RoundToInt(Game.WorstMs * 10f) * 10 + Mathf.Min(9, Mathf.RoundToInt(Game.Predictor.LastError * 10f));
            if (key != fpsKey)
            {
                fpsKey = key;
                fpsText = Mathf.RoundToInt(Game.Fps) + " fps   " + (1000f / Mathf.Max(1f, Game.Fps)).ToString("0.0") + " ms   worst " + Game.WorstMs.ToString("0.0") + " ms\nprediction error " + Game.Predictor.LastError.ToString("0.00") + " m";
            }
            var r = new Rect(W - 330f, Game.Screen == AppScreen.Playing ? H - 140f : 60f, 314f, 40f);
            Shadowed(r, fpsText, fpsStyle, Game.Fps >= 110f ? Palette.Ok : Game.Fps >= 60f ? Palette.Warn : Palette.Bad);
        }

        void DrawToast()
        {
            if (string.IsNullOrEmpty(Game.Toast) || Time.unscaledTime > Game.ToastUntil) return;
            var r = new Rect(W / 2f - 320f, H - 70f, 640f, 40f);
            Round(r, new Color(0.55f, 0.16f, 0.16f, 0.94f));
            GUI.Label(new Rect(r.x + 12f, r.y, r.width - 24f, r.height), Game.Toast, toastStyle);
        }

        static readonly string[] WaitText = { "Waiting for the bridge.", "Waiting for the bridge..", "Waiting for the bridge..." };

        void Waiting()
        {
            if (Game.WaitingFor == null) return;
            int dots = (int)(Time.unscaledTime * 3f) % 3;
            GUILayout.Label(WaitText[dots], muted);
        }

        // ---- (1) connect --------------------------------------------------------------------------
        void DrawConnect()
        {
            GUILayout.BeginArea(Centered(620f, 360f), panel);
            GUILayout.Label("SALTWORKS", display);
            GUILayout.Label("Tactical coaching client. The bridge runs the sim, the bots and the coach; this window is the eyes and hands.", muted);
            GUILayout.Space(Theme.S4);
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
            if (Btn("Reconnect", primary, GUILayout.Height(44f))) Game.Do(Game.ConnectNow);
            GUI.enabled = true;
            GUILayout.EndArea();
        }

        // ---- (2) choose a sample player ---------------------------------------------------------------
        void DrawChoose()
        {
            GUILayout.BeginArea(Centered(680f, 540f), panel);
            GUILayout.Label("Whose history?", title);
            GUILayout.Label("Pick a sample player. The bridge reads their matches, finds the weakest situation and builds drills from it.", muted);
            GUILayout.Space(Theme.S3);
            GUI.enabled = Idle;
            foreach (Choice s in GameController.Samples)
            {
                string id = s.Id;
                if (Btn(s.Label + "\n" + s.Blurb, bigButton, GUILayout.Height(64f))) Game.Do(() => Game.ChooseSample(id));
            }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            Waiting();
            if (Game.Report != null && Btn("Back to the current report", button)) Game.Do(Game.BackToReport);
            if (Btn("Back to the menu", ghost)) Game.Do(Game.OpenMenu);
            GUILayout.EndArea();
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

        void DrawMapSelect()
        {
            GUILayout.BeginArea(Centered(720f, 560f), panel);
            GUILayout.Label("Choose a map", title);
            GUILayout.Label("Drills are built on this map. Rehearsals of your own mistakes need the map the history was played on; on another map the coach generates fresh drills instead.", muted);
            GUILayout.Space(Theme.S3);
            List<MapInfo> maps = Game.Maps;
            GUI.enabled = Idle;
            if (maps == null)
            {
                GUILayout.Label("Asking the bridge for its maps...", body);
            }
            else if (maps.Count == 0)
            {
                GUILayout.Label("The bridge listed no maps; the current one stays.", body);
            }
            else
            {
                string remembered = Game.RememberedMap;
                foreach (MapInfo m in maps)
                {
                    if (m == null || string.IsNullOrEmpty(m.id)) continue;
                    string id = m.id;
                    bool loaded = Game.Map != null && Game.Map.id == id;
                    string tag = loaded ? "   (loaded)" : id == remembered ? "   (last time)" : "";
                    string blurb = MapBlurb(id);
                    string label = (m.name ?? id) + tag + (blurb.Length > 0 ? "\n" + blurb : "");
                    if (Btn(label, loaded ? chosenButton : bigButton, GUILayout.MinHeight(64f))) Game.Do(() => Game.PickMap(id));
                }
            }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            Waiting();
            GUI.enabled = Idle;
            string keep = string.IsNullOrEmpty(Game.MapName) ? "Keep the current map" : "Keep " + Game.MapName;
            if (Btn(keep, button, GUILayout.Height(42f))) Game.Do(Game.KeepCurrentMap);
            GUI.enabled = true;
            GUILayout.EndArea();
        }

        // ---- (2c) agent select ---------------------------------------------------------------------------
        void DrawAgentSelect()
        {
            bool wide = W > 960f;
            string mainPick = Game.AdviceMain, practice = Game.AdvicePractice;
            bool advice = mainPick != null || practice != null;
            GUILayout.BeginArea(Centered(wide ? 1040f : 660f, wide ? (advice ? 560f : 500f) : H - 50f), panel);
            GUILayout.Label("Choose your agent", title);
            GUILayout.Label(advice
                ? "Your kit for every drill. The coach read your matches: one pick suits how you play, one trains what you avoid. The practice pick is selected."
                : "Your kit for every drill. A drill that trains a tool you lack lends you an agent who has it.", muted);
            GUILayout.Space(Theme.S3);
            string current = Game.PickedAgentId ?? practice ?? Game.RememberedAgent;
            GUI.enabled = Idle;
            if (wide) GUILayout.BeginHorizontal();
            foreach (AgentInfo a in Agents.All)
            {
                string id = a.Id;
                bool sel = id == current;
                GUILayout.BeginVertical(card, wide ? GUILayout.Width(236f) : GUILayout.ExpandWidth(true));
                Rect band = GUILayoutUtility.GetRect(10f, 6f, GUILayout.ExpandWidth(true));
                Round(band, a.Color);
                GUILayout.Space(Theme.S1);
                GUILayout.BeginHorizontal(GUILayout.Height(22f));
                if (id == mainPick) Badge("YOUR MAIN", Theme.Darken(a.Color, 0.35f), Color.white);
                if (id == practice) Badge("PRACTISE YOUR WEAKNESS", Settings.Accent, new Color(0.06f, 0.06f, 0.07f));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                Color old = GUI.contentColor;
                GUI.contentColor = a.Color;
                GUILayout.Label(a.Name + (sel ? "  \u2713" : ""), h2);
                GUI.contentColor = old;
                GUILayout.Label((a.Role ?? "").ToUpperInvariant(), capLeft);
                GUILayout.Space(Theme.S1);
                GUILayout.BeginHorizontal();
                foreach (Ability ab in Agents.AllAbilities)
                {
                    int n = a.Kit != null ? a.Kit.Get(ab) : 0;
                    if (n <= 0) continue;
                    Rect ir = GUILayoutUtility.GetRect(24f, 24f, GUILayout.Width(24f), GUILayout.Height(24f));
                    Icon(ir, ab, Agents.AbilityColor(ab));
                    GUILayout.Label(Agents.AbilityName(ab) + " \u00d7" + n, bodyBold, GUILayout.ExpandWidth(false));
                    GUILayout.Space(Theme.S1);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(Theme.S1);
                GUILayout.Label(a.Blurb ?? "", small, GUILayout.MinHeight(36f));
                GUILayout.Space(Theme.S1);
                if (Btn("Play " + a.Name, sel ? primary : button, GUILayout.Height(40f))) Game.Do(() => Game.PickAgent(id));
                GUILayout.EndVertical();
            }
            if (wide) GUILayout.EndHorizontal();
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            Waiting();
            GUILayout.BeginHorizontal();
            GUI.enabled = Idle;
            if (Game.V2 && Btn("Back to maps", button, GUILayout.Height(38f))) Game.Do(Game.OpenMapSelect);
            if (Btn(Game.PickedAgentId != null ? "Keep " + Agents.NameOf(Game.PickedAgentId) : "Let the drills decide", button, GUILayout.Height(38f))) Game.Do(Game.SkipAgent);
            GUILayout.FlexibleSpace();
            if (Game.PickedAgentId == null && practice != null)
            {
                string pid = practice;
                if (Btn("Play " + Agents.NameOf(pid) + "  (recommended practice)", primary, GUILayout.Height(38f), GUILayout.MinWidth(300f))) Game.Do(() => Game.PickAgent(pid));
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        static void Icon(Rect r, Ability a, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            GUI.DrawTexture(r, Icons.For(a), ScaleMode.ScaleToFit, true);
            GUI.color = old;
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
            GUILayout.BeginArea(Centered(1000f, H - 50f), panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Weakness report", title);
            GUILayout.FlexibleSpace();
            GUI.enabled = Idle;
            if (Btn("Menu", button, GUILayout.Width(100f))) Game.Do(Game.OpenMenu);
            if (Btn("Choose another player", button, GUILayout.Width(220f))) Game.Do(Game.BackToChoose);
            GUI.enabled = true;
            GUILayout.Space(140f);
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
            bool rehearseFirst = Game.RehearseFocus && rm.rehearsals != null && rm.rehearsals.Count > 0;
            if (rehearseFirst)
            {
                GUI.enabled = Idle;
                DrawRehearsals(rm);
                GUI.enabled = true;
            }
            if (rm.agentAdvice != null) DrawAdvice(rm.agentAdvice);
            SectionHeader("Findings");
            if (r.findings == null || r.findings.Count == 0)
                GUILayout.Label("No findings yet: the history has too few situations to judge.", body);
            else
                foreach (WeaknessFinding f in r.findings) DrawFinding(f, r.role);

            GUILayout.Space(Theme.S3);
            DrawSetupRow();
            GUI.enabled = Idle;
            if (!rehearseFirst) DrawRehearsals(rm);

            SectionHeader("Next practice drill");
            GUILayout.Label("A rehearsal of a failed moment first, then generated drills at your current difficulty.", muted);
            if (Btn("Next practice drill", primary, GUILayout.Height(48f))) Game.Do(Game.RequestNextDrill);

            GUILayout.Space(Theme.S2);
            SectionHeader("Versus an archetype");
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
                if (Btn(a.Label + "\n" + a.Blurb, wide ? button : bigButton, GUILayout.MinHeight(72f))) Game.Do(() => Game.RequestVersus(id));
            }
            if (wide) GUILayout.EndHorizontal();
            GUI.enabled = true;
            GUILayout.EndScrollView();
            Waiting();
            GUILayout.EndArea();
        }

        /// <summary>The report's rehearsals: one button per rebuilt moment.</summary>
        void DrawRehearsals(ReportMsg rm)
        {
            if (rm.rehearsals == null || rm.rehearsals.Count == 0) return;
            SectionHeader("Rehearse my mistakes");
            GUILayout.Label("Each one rebuilds a moment from the history: same positions, same clock.", muted);
            foreach (RehearsalInfo ri in rm.rehearsals)
            {
                int idx = ri.index;
                string label = (ri.title ?? "Rehearsal " + idx) + "  (" + GameController.SkillName(ri.skill) + ")" + (string.IsNullOrEmpty(ri.brief) ? "" : "\n" + ri.brief);
                if (Btn(label, bigButton, GUILayout.MinHeight(56f))) Game.Do(() => Game.RequestRehearsal(idx));
            }
            GUILayout.Space(Theme.S2);
        }

        /// <summary>Map and agent for the next drills, with buttons to change either (bots-wire.2 only).</summary>
        void DrawSetupRow()
        {
            if (!Game.V2) return;
            GUILayout.BeginVertical(card);
            GUILayout.BeginHorizontal();
            GUILayout.Label("MAP", capLeft, GUILayout.Width(40f));
            GUILayout.Label(string.IsNullOrEmpty(Game.MapName) ? "?" : Game.MapName, bodyBold, GUILayout.Width(150f));
            GUI.enabled = Idle;
            if (Btn("Change map", button, GUILayout.Width(140f))) Game.Do(Game.OpenMapSelect);
            GUI.enabled = true;
            GUILayout.Space(30f);
            GUILayout.Label("AGENT", capLeft, GUILayout.Width(50f));
            AgentInfo a = Agents.Get(Game.PickedAgentId);
            Color old = GUI.contentColor;
            if (a != null) GUI.contentColor = a.Color;
            GUILayout.Label(a != null ? a.Name + "  " + Agents.KitText(Game.PickedKit ?? a.Kit) : "set by each drill", bodyBold, GUILayout.MinWidth(200f));
            GUI.contentColor = old;
            GUI.enabled = Idle;
            if (Btn("Change agent", button, GUILayout.Width(140f))) Game.Do(Game.OpenAgentSelect);
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(6f);
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
            GUILayout.Space(Theme.S1);
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
            GUILayout.BeginArea(Centered(680f, 640f), panel);
            introScroll = GUILayout.BeginScrollView(introScroll, false, false);
            GUILayout.Label(spec.title ?? "Drill", title);
            GUILayout.Label(GameController.SkillName(spec.skill) + ", difficulty " + spec.difficulty.ToString("0.00"), muted);
            GUILayout.Space(Theme.S3);
            if (!string.IsNullOrEmpty(d.intro)) GUILayout.Label(d.intro, lead);
            if (!string.IsNullOrEmpty(spec.goal))
            {
                GUILayout.Space(6f);
                GUILayout.Label("Goal: " + spec.goal, body);
            }
            GUILayout.Space(Theme.S3);
            Color old = GUI.contentColor;
            GUI.contentColor = Palette.Team(Game.PlayerTeam);
            GUILayout.Label("You play " + Game.PlayerTeam + (string.IsNullOrEmpty(Game.MapName) ? "." : " on " + Game.MapName + "."), bodyBold);
            GUI.contentColor = old;
            if (!string.IsNullOrEmpty(Game.PlayerAgent))
            {
                AgentInfo ai = Agents.Get(Game.PlayerAgent);
                GUI.contentColor = Agents.ColorOf(Game.PlayerAgent);
                string lent = Game.PickedAgentId != null && Game.PlayerAgent != Game.PickedAgentId ? "  (this drill lends you " + Agents.NameOf(Game.PlayerAgent) + ")" : "";
                GUILayout.Label("Agent: " + Agents.NameOf(Game.PlayerAgent) + (ai != null ? ", " + ai.Role + ". " + Agents.KitText(Game.PlayerKit()) : "") + lent, bodyBold);
                GUI.contentColor = old;
            }
            if (d.bots != null && d.bots.Count > 0)
            {
                SectionHeader("Bots");
                foreach (DrillBotInfo b in d.bots)
                {
                    GUI.contentColor = Palette.Relation(b.team, Game.PlayerTeam);
                    string agent = !string.IsNullOrEmpty(b.agent) ? "  (" + Agents.NameOf(b.agent) + ")" : "";
                    GUILayout.Label("  " + b.id + "  " + b.team + "  " + GameController.ArchetypeName(b.archetype) + agent, body);
                }
                GUI.contentColor = old;
            }
            SectionHeader("Controls");
            DrawControls();
            GUILayout.EndScrollView();
            GUILayout.Space(Theme.S2);
            if (Btn("Begin  (Enter)", primary, GUILayout.Height(50f))) Game.Do(Game.Begin);
            GUILayout.Label("The clock starts when you begin. The mouse is captured; Esc pauses.", small);
            GUILayout.EndArea();
        }

        void DrawControls()
        {
            foreach (string[] c in Controls)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(c[0], mono, GUILayout.Width(120f));
                GUILayout.Label(c[1], small);
                GUILayout.EndHorizontal();
            }
        }

        // ---- in-drill HUD ------------------------------------------------------------------------------
        // Cached HUD strings: rebuilt only when what they show changes.
        string clockStr = "", phaseStr = "", hpStr = "", spikeStr = "", progressStr = "", hintStr = "", modeStr = "";
        int clockKey = int.MinValue, hpKey = int.MinValue, progressKey = int.MinValue, hintKey = int.MinValue, modeKey = int.MinValue;
        string phaseSrc;
        string spikeStatus, spikeCarrier;
        int spikeSecs = int.MinValue, spikeDefuse = int.MinValue;
        DrillMsg hudDrill;
        string hudTitleStr = "", hudSkillStr = "", hudSetupStr = "", hudGoalStr = "";
        Color hudSetupColor = Color.white;

        void CacheDrillStrings()
        {
            if (ReferenceEquals(hudDrill, Game.Drill)) return;
            hudDrill = Game.Drill;
            DrillSpec spec = hudDrill?.spec;
            hudTitleStr = spec?.title ?? "";
            hudSkillStr = spec == null ? "" : GameController.SkillName(spec.skill) + "  |  difficulty " + spec.difficulty.ToString("0.00");
            string setup = Game.MapName;
            if (!string.IsNullOrEmpty(Game.PlayerAgent))
                setup += (setup.Length > 0 ? "  |  " : "") + Agents.NameOf(Game.PlayerAgent) + " (" + (Agents.Get(Game.PlayerAgent)?.Role ?? "agent") + ")";
            hudSetupStr = setup;
            hudSetupColor = string.IsNullOrEmpty(Game.PlayerAgent) ? Color.white : Agents.ColorOf(Game.PlayerAgent);
            hudGoalStr = spec?.goal ?? "";
        }

        void DrawHud(bool layout)
        {
            SimState s = Game.State;
            AgentState me = Game.Me;
            float now = Time.unscaledTime;
            Rules rules = Game.Rules;
            if (s == null) return;
            CacheDrillStrings();
            bool repaint = Event.current.type == EventType.Repaint;

            // Screen-space effects first, so text stays readable on top.
            Camera cam = Game.Rig.Cam;
            float smoke = Game.World.SmokeDensityAt(cam.transform.position);
            if (smoke > 0f) Fill(new Rect(0f, 0f, W, H), new Color(Palette.Smoke.r * 0.9f, Palette.Smoke.g * 0.9f, Palette.Smoke.b * 0.9f, 0.92f * smoke));
            float dmg = 1f - Mathf.Clamp01((now - Game.DamageAt) / 0.45f);
            if (dmg > 0f) Fill(new Rect(0f, 0f, W, H), new Color(0.9f, 0.1f, 0.1f, 0.3f * dmg));
            DrawDashStreaks();

            if (repaint) DrawBotLabels(cam);
            if (repaint) DrawDamageNumbers(cam, now);

            bool alive = me != null && me.alive;
            DrawScope();
            bool scoped = Game.Gun.Weapon.Scopes && Game.Gun.Scope01 >= 0.9f;
            if (alive && !Game.Paused && repaint && !scoped && !Game.BuyOpen) DrawCrosshair(now);
            else if (alive && !Game.Paused && repaint && scoped)
            {
                // Hit markers still show through the scope.
                int hk2 = Crosshair.PixelScale(Screen.height);
                Draw2D.Begin(Screen.width, Screen.height);
                Crosshair.HitMarker(Screen.width / 2f, Screen.height / 2f, hk2, now - Game.HitMarkerAt, Game.HitMarkerHead, Game.HitMarkerKill);
                Draw2D.End();
            }

            // Blinded: a white-out over the world and the crosshair that eases out over the blind's remaining time.
            float blind = Game.BlindAmount();
            if (blind > 0f)
            {
                Fill(new Rect(0f, 0f, W, H), new Color(1f, 1f, 0.97f, 0.97f * blind));
                if (blind > 0.5f) Shadowed(new Rect(0f, H * 0.62f, W, 30f), "BLINDED", hudCenter, new Color(0.35f, 0.35f, 0.4f, blind));
            }

            // Top left: minimap, then the drill info beside it.
            float infoX = 16f;
            if (Settings.MinimapOn && !Game.FullMap)
            {
                float ms = Settings.MinimapSize;
                var mr = new Rect(16f, 14f, ms, ms);
                DrawMap(mr, false, Settings.MinimapOpacity);
                infoX = mr.xMax + 18f;
            }

            // Top centre: score bar and round clock.
            DrawScoreBar(s, now);
            if (alive && Game.World.IsRevealed(Game.PlayerId))
            {
                float pulse = 0.7f + 0.3f * Mathf.Sin(now * 10f);
                var rr = new Rect(W / 2f - 110f, 112f, 220f, 24f);
                Color rc = Agents.AbilityColor(Ability.Recon);
                Round(rr, new Color(rc.r * 0.35f, rc.g * 0.2f, rc.b * 0.25f, 0.85f));
                Shadowed(rr, "YOU ARE REVEALED", hudCenter, new Color(rc.r, rc.g, rc.b, pulse));
            }

            // Drill, map and agent.
            if (hudDrill?.spec != null)
            {
                float iw = Mathf.Min(420f, W / 2f - 150f - infoX);
                var r = new Rect(infoX, 14f, Mathf.Max(160f, iw), 140f);
                Shadowed(new Rect(r.x, r.y, r.width, 24f), Game.InMatch ? "Match" : hudTitleStr, hudTitle, Color.white);
                if (!Game.InMatch) Shadowed(new Rect(r.x, r.y + 24f, r.width, 20f), hudSkillStr, hudSkill, Palette.Team(Game.PlayerTeam));
                float gy = r.y + (Game.InMatch ? 24f : 44f);
                if (hudSetupStr.Length > 0)
                {
                    Shadowed(new Rect(r.x, gy, r.width, 20f), hudSetupStr, hudSkill, hudSetupColor);
                    gy += 20f;
                }
                if (hudGoalStr.Length > 0 && !Game.InMatch) Shadowed(new Rect(r.x, gy, r.width, 70f), hudGoalStr, hudGoal, new Color(0.9f, 0.9f, 0.92f));
            }

            // Top right: kill feed.
            DrawKillFeed(now);

            // Bottom left: health and movement mode.
            float hp = me != null ? Mathf.Max(0f, me.hp) : 0f;
            int hk = alive ? Mathf.RoundToInt(hp) : -1;
            if (hk != hpKey)
            {
                hpKey = hk;
                hpStr = alive ? Theme.Int(hk) : "DOWN";
            }
            Color hpc = hp > 50f ? Palette.Ok : hp > 25f ? Palette.Warn : Palette.Bad;
            Shadowed(new Rect(20f, H - 92f, 160f, 50f), hpStr, hudBig, alive ? Color.white : Palette.Bad);
            GUI.Label(new Rect(22f, H - 42f, 200f, 18f), "HEALTH", capLeft);
            Round(new Rect(20f, H - 24f, 220f, 8f), new Color(1f, 1f, 1f, 0.15f));
            if (hp > 0f) Round(new Rect(20f, H - 24f, Mathf.Max(8f, 220f * Mathf.Clamp01(hp / 100f)), 8f), hpc);
            DrawMoveMode(new Rect(20f, H - 122f, 240f, 24f), alive);
            DrawCreditsArmor(me);

            // Bottom centre: abilities.
            DrawAbilityBar(me, now);

            // Bottom right: weapon and ammo, the spike above it.
            DrawAmmo(me, now);
            UpdateSpikeText(s);
            var sr = new Rect(W - 420f, H - 154f, 400f, 30f);
            Shadowed(sr, spikeStr, spikeStyle, s.spike != null && s.spike.status == "planted" ? new Color(1f, 0.6f, 0.6f) : Color.white);
            GUI.Label(new Rect(sr.x, sr.y - 18f, sr.width, 18f), "SPIKE", capRight);

            // Centre: plant / defuse progress and hints.
            float cx = W / 2f, cy = H / 2f;
            float progress = -1f;
            bool defusing = false;
            if (s.spike != null && s.spike.status == "planted" && s.spike.defuseProgressS > 0f)
            {
                progress = s.spike.defuseProgressS / Mathf.Max(0.1f, rules.defuseTimeS);
                defusing = true;
            }
            else if (Game.PlantTicks > 0)
            {
                progress = Game.PlantTicks / Mathf.Max(1f, rules.plantTimeS * rules.tickRate);
            }
            if (progress >= 0f)
            {
                int pct = Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f);
                int pk = pct + (defusing ? 1000 : 0);
                if (pk != progressKey)
                {
                    progressKey = pk;
                    progressStr = (defusing ? "DEFUSING  " : "PLANTING  ") + Theme.Percent(pct);
                }
                var pr = new Rect(cx - 140f, cy + 70f, 280f, 10f);
                Shadowed(new Rect(pr.x, pr.y - 26f, pr.width, 22f), progressStr, hudCenter, Color.white);
                Round(pr, new Color(0f, 0f, 0f, 0.5f));
                if (progress > 0.01f) Round(new Rect(pr.x, pr.y, Mathf.Max(10f, pr.width * Mathf.Clamp01(progress)), pr.height), defusing ? Palette.Defense : Palette.Attack);
            }
            else if (alive && !Game.DrillDone)
            {
                string hint = null;
                if (Game.PlayerTeam == "attack" && me.carryingSpike && Game.OnSite(me.pos)) hint = "Hold F to plant";
                else if (Game.PlayerTeam == "defense" && Game.NearPlantedSpike(me.pos)) hint = "Hold F to defuse";
                if (hint != null) Shadowed(new Rect(cx - 150f, cy + 56f, 300f, 24f), hint, hudCenter, new Color(1f, 0.95f, 0.8f));
            }
            if (!alive) Shadowed(new Rect(cx - 250f, cy + 40f, 500f, 30f), "You are down. The round plays out.", hudCenter, new Color(1f, 0.7f, 0.7f));
            if (Game.DrillDone && Game.Result == null && !Game.InMatch) Shadowed(new Rect(cx - 250f, cy + 100f, 500f, 30f), "Round over. Waiting for the verdict...", hudCenter, Color.white);
            if (Game.DrillDone && Game.InMatch && Game.RoundResult == null) Shadowed(new Rect(cx - 250f, cy + 100f, 500f, 30f), "Round over.", hudCenter, Color.white);

            // Full map while Tab is held.
            if (Game.FullMap)
            {
                Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.45f));
                float size = Mathf.Min(W - 120f, H - 120f);
                var fr = new Rect(Mathf.Round((W - size) / 2f), Mathf.Round((H - size) / 2f), size, size);
                DrawMap(fr, true, 0.97f);
                Shadowed(new Rect(fr.x, fr.yMax + 6f, fr.width, 20f), "YOU  white arrow     ALLY  dot     ENEMY  dot when seen, ring when revealed, ripple when heard", capLeft, Theme.Muted);
            }

            // Verdict banner.
            if (Game.Result != null && now < Game.VerdictUntil)
            {
                bool ok = Game.Result.success;
                float k = Theme.Ease((now - (Game.VerdictUntil - 2.5f)) / 0.3f);
                var br = new Rect(0f, H * 0.28f + (1f - k) * 20f, W, 120f);
                Fill(br, new Color(0.05f, 0.06f, 0.08f, 0.8f * k));
                Fill(new Rect(W / 2f - W * 0.5f * k, br.y, W * k, 4f), ok ? Palette.Ok : Palette.Bad);
                Fill(new Rect(W / 2f - W * 0.5f * k, br.yMax - 4f, W * k, 4f), ok ? Palette.Ok : Palette.Bad);
                Color vc = ok ? Palette.Ok : Palette.Bad;
                vc.a = k;
                Shadowed(new Rect(0f, br.y + 10f, W, 60f), ok ? "DRILL COMPLETE" : "NOT THIS TIME", banner, vc);
                Shadowed(new Rect(40f, br.y + 70f, W - 80f, 40f), Game.Result.headline ?? "", bannerSub, new Color(1f, 1f, 1f, k));
            }
            if (Game.InMatch) DrawRoundBanners(now);
            if (Game.BuyOpen && Game.InMatch) DrawBuy(true);

            if (Game.Paused && !Game.DrillDone && layout && !Game.SettingsOpen) DrawPause();
        }

        /// <summary>The minimap or full map in a virtual-space rect, with site letters on top.</summary>
        void DrawMap(Rect vr, bool full, float opacity)
        {
            var sr = new Rect(vr.x * scale, vr.y * scale, vr.width * scale, vr.height * scale);
            Game.Minimap.Draw(Game, sr, full, Game.EyePos, Game.Rig.Heading, opacity);
            if (Event.current.type != EventType.Repaint) return;
            IReadOnlyList<Minimap.SiteLabel> sites = Game.Minimap.Sites;
            for (int i = 0; i < sites.Count; i++)
            {
                Vector2 p = Game.Minimap.PanelPoint(sites[i].Sim) / scale;
                if (p.x < 8f || p.y < 8f || p.x > vr.width - 8f || p.y > vr.height - 8f) continue;
                Color c = Palette.Site(sites[i].Letter);
                c.a = opacity;
                Shadowed(new Rect(vr.x + p.x - 15f, vr.y + p.y - 11f, 30f, 22f), sites[i].Letter, mapLetter, c);
            }
        }

        /// <summary>Wind streaks rushing outward from the centre while the dash's FOV kick lasts.</summary>
        void DrawDashStreaks()
        {
            float k = Game.Rig.DashKickAmount;
            if (k <= 0f || Event.current.type != EventType.Repaint) return;
            float cx = W / 2f, cy = H / 2f;
            float reach = Mathf.Max(W, H) * 0.6f;
            float travel = 1f - k; // 0 at the start of the dash, 1 when it has eased out
            Matrix4x4 m = GUI.matrix;
            for (int i = 0; i < 18; i++)
            {
                // Fixed pseudo-random angles and offsets, so the streaks do not flicker frame to frame.
                float ang = (i * 137.5f) % 360f;
                float seed = Mathf.Repeat(i * 0.618f, 1f);
                float r0 = reach * (0.35f + 0.3f * seed + 0.5f * travel);
                float len = reach * (0.12f + 0.1f * seed) * k;
                GUI.matrix = m;
                GUIUtility.RotateAroundPivot(ang, new Vector2(cx, cy) * scale);
                Fill(new Rect(cx + r0, cy - 1f, len, 2f + 1.5f * seed), new Color(0.92f, 0.98f, 1f, 0.45f * k));
            }
            GUI.matrix = m;
        }

        void DrawMoveMode(Rect r, bool alive)
        {
            if (!alive) return;
            string mode = Game.MoveMode;
            bool moving = Game.Moving;
            int key = (mode == "crouch" ? 0 : mode == "walk" ? 2 : 4) + (moving ? 1 : 0);
            Color c;
            if (mode == "crouch" || mode == "walk") c = Palette.Ok;
            else c = moving ? Palette.Warn : new Color(0.8f, 0.82f, 0.86f);
            if (key != modeKey)
            {
                modeKey = key;
                if (mode == "crouch") modeStr = moving ? "CROUCHING  quiet" : "CROUCHED";
                else if (mode == "walk") modeStr = moving ? "WALKING  quiet" : "WALK";
                else modeStr = moving ? "RUNNING  heard within " + Game.Rules.runHearingM.ToString("0") + " m" : "STILL";
                modeWidth = modeStyle.CalcSize(new GUIContent(modeStr)).x;
            }
            Round(new Rect(r.x, r.y, modeWidth + 30f, r.height), new Color(0.05f, 0.06f, 0.08f, 0.62f));
            Fill(new Rect(r.x + 4f, r.y + 5f, 3f, r.height - 10f), c);
            // A footprint-like dot that pulses with the stride while moving.
            float pulse = moving ? 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * (mode == "run" ? 9f : 5f))) : 0.4f;
            Round(new Rect(r.x + 12f, r.y + r.height / 2f - 3f, 6f, 6f), new Color(c.r, c.g, c.b, pulse));
            Shadowed(new Rect(r.x + 24f, r.y, modeWidth + 4f, r.height), modeStr, modeStyle, c);
        }

        float modeWidth;

        /// <summary>Bottom centre: one slot per ability with its key, a procedural icon and the charges left.</summary>
        void DrawAbilityBar(AgentState me, float now)
        {
            const float size = 58f, gap = 8f;
            Ability[] all = Agents.AllAbilities;
            float total = all.Length * size + (all.Length - 1) * gap;
            float x0 = W / 2f - total / 2f;
            float y = H - size - 34f;
            AbilityCharges kit = Game.PlayerKit();
            for (int i = 0; i < all.Length; i++)
            {
                Ability ab = all[i];
                var r = new Rect(x0 + i * (size + gap), y, size, size);
                int charges = me != null ? me.Charges(ab) : 0;
                int max = Mathf.Max(charges, kit != null ? kit.Get(ab) : 0);
                bool usable = charges > 0 && me != null && me.alive;
                bool aiming = Game.Aiming.HasValue && Game.Aiming.Value == ab;
                Color ac = Agents.AbilityColor(ab);

                float denied = 1f - Mathf.Clamp01((now - Game.DeniedAt[(int)ab]) / 0.45f);
                float used = 1f - Mathf.Clamp01((now - Game.UsedAt[(int)ab]) / 0.35f);
                Color border = aiming ? ac : denied > 0f ? new Color(Palette.Bad.r, Palette.Bad.g, Palette.Bad.b, denied) : new Color(1f, 1f, 1f, usable ? 0.22f : 0.08f);
                float bw = aiming ? 3f : 2f;
                Round(r, border);
                Round(new Rect(r.x + bw, r.y + bw, r.width - 2f * bw, r.height - 2f * bw), new Color(0.06f, 0.07f, 0.09f, usable ? 0.9f : 0.7f));
                if (used > 0f) Round(r, new Color(ac.r, ac.g, ac.b, 0.35f * used));

                Color iconC = usable ? ac : new Color(0.55f, 0.56f, 0.6f, max > 0 ? 0.45f : 0.2f);
                Icon(new Rect(r.x + 11f, r.y + 9f, size - 22f, size - 22f), ab, iconC);
                Shadowed(new Rect(r.x + 6f, r.y + 4f, 20f, 18f), Agents.KeyLabel(ab), slotKey, usable ? Color.white : new Color(1f, 1f, 1f, 0.4f));
                if (max > 0) Shadowed(new Rect(r.x, r.y, r.width - 6f, r.height - 3f), Theme.Int(charges), slotCount, usable ? Color.white : new Color(1f, 1f, 1f, 0.45f));

                // Pips: one per charge in the kit, lit while unused.
                float pw = 8f, pg = 4f;
                float pipsW = max * pw + Mathf.Max(0, max - 1) * pg;
                for (int p = 0; p < max; p++)
                    Round(new Rect(r.center.x - pipsW / 2f + p * (pw + pg), r.yMax + 4f, pw, 4f), p < charges ? ac : new Color(1f, 1f, 1f, 0.15f));
                GUI.Label(new Rect(r.x - 10f, r.yMax + 10f, r.width + 20f, 16f), AbilityUpper[(int)ab], slotName);
            }
            if (Game.Aiming.HasValue)
            {
                Ability ab = Game.Aiming.Value;
                int key = (int)ab * 2 + (Game.AimClamped ? 1 : 0);
                if (key != hintKey)
                {
                    hintKey = key;
                    hintStr = "Release " + Agents.KeyLabel(ab) + " to throw the " + Agents.AbilityName(ab).ToLowerInvariant() + (Game.AimClamped ? "  (max range)" : "");
                }
                Shadowed(new Rect(W / 2f - 250f, y - 28f, 500f, 22f), hintStr, hudCenter, Agents.AbilityColor(ab));
            }
        }

        void UpdateSpikeText(SimState s)
        {
            SpikeState sp = s.spike;
            string status = sp?.status;
            string carrier = sp?.carrierId;
            int secs = status == "planted" ? Mathf.RoundToInt(Mathf.Max(0f, s.roundTimeLeftS)) : 0;
            int def = status == "planted" && sp.defuseProgressS > 0f ? Mathf.RoundToInt(sp.defuseProgressS / Mathf.Max(0.1f, Game.Rules.defuseTimeS) * 100f) : -1;
            if (status == spikeStatus && carrier == spikeCarrier && secs == spikeSecs && def == spikeDefuse && (status != null || spikeStr.Length > 0)) return;
            spikeStatus = status;
            spikeCarrier = carrier;
            spikeSecs = secs;
            spikeDefuse = def;
            spikeStr = SpikeText(s);
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
            CrosshairSettings cs = Settings.Crosshair;
            float target = 0f;
            if (cs.Dynamic)
            {
                // The weapon's current spread (movement x base spread), as pixels at this field of view, plus firing.
                Camera cam = Game.Rig.Cam;
                float halfV = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
                float px = Mathf.Tan(Game.Gun.SpreadDeg * Mathf.Deg2Rad) / Mathf.Max(1e-4f, Mathf.Tan(halfV)) * (Screen.height * 0.5f);
                target += px / Crosshair.PixelScale(Screen.height);
                target += Mathf.Clamp01(Game.Rig.Recoil / 1.5f) * 5f;
                target = Mathf.Min(target, 80f);
            }
            spread = Mathf.MoveTowards(spread, target, Time.unscaledDeltaTime * 120f);
            int k = Crosshair.PixelScale(Screen.height);
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            Draw2D.Begin(Screen.width, Screen.height);
            Crosshair.Draw(cx, cy, cs, k, spread, 1f);
            Crosshair.HitMarker(cx, cy, k, now - Game.HitMarkerAt, Game.HitMarkerHead, Game.HitMarkerKill);
            Draw2D.End();
        }

        void DrawBotLabels(Camera cam)
        {
            Vector2 eye = Game.EyePos;
            List<SmokeState> smokes = Game.State?.smokes;
            foreach (BotView b in Game.World.Bots.Values)
            {
                if (!b.Alive) continue;
                Vector3 root = b.Root.transform.position;
                Vector3 anchor = root + Vector3.up * (BotView.Height + 0.35f);
                Vector3 sp = cam.WorldToScreenPoint(anchor);
                if (sp.z <= 0.1f) continue;
                // Recon-revealed enemies keep their label through walls and at any range.
                bool revealed = b.Revealed;
                if (!revealed && (sp.z > 90f || (Game.Walls != null && !Game.Walls.LineOfSight(eye, new Vector2(root.x, root.z), smokes)))) continue;
                float x = sp.x / scale;
                float y = (Screen.height - sp.y) / scale;
                Color tc = Color.Lerp(Palette.Relation(b.Team, Game.PlayerTeam), Color.white, 0.25f);
                string name = b.Id;
                if (revealed)
                {
                    int m = Mathf.RoundToInt(sp.z);
                    if (m != b.RevealLabelM || b.RevealLabel == null)
                    {
                        b.RevealLabelM = m;
                        b.RevealLabel = b.Id + "  REVEALED " + m + " m";
                    }
                    name = b.RevealLabel;
                }
                Shadowed(new Rect(x - 150f, y - 52f, 300f, 18f), name, botName, revealed ? Settings.Enemy : tc);
                Shadowed(new Rect(x - 150f, y - 35f, 300f, 16f), b.LabelLine ?? "", botLine, string.IsNullOrEmpty(b.Agent) ? new Color(0.92f, 0.92f, 0.95f) : Color.Lerp(Agents.ColorOf(b.Agent), Color.white, 0.35f));
                string thought = b.Blind ? "dazed" : b.ThoughtText();
                if (!string.IsNullOrEmpty(thought))
                    Shadowed(new Rect(x - 150f, y - 19f, 300f, 16f), thought, botLine, b.Blind ? new Color(1f, 0.95f, 0.65f) : new Color(0.65f, 0.95f, 0.88f));
            }
        }

        void DrawPause()
        {
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.55f));
            GUILayout.BeginArea(Centered(560f, 620f), panel);
            GUILayout.Label("Paused", title);
            GUILayout.Label("No inputs are sent while paused, so the sim waits for you.", muted);
            GUILayout.Space(Theme.S3);
            if (Btn("Resume", primary, GUILayout.Height(48f))) Game.Do(Game.Resume);
            GUILayout.BeginHorizontal();
            if (Btn("Settings", button, GUILayout.Height(42f))) Game.Do(Game.OpenSettings);
            if (Game.InMatch)
            {
                if (Btn("Leave match", button, GUILayout.Height(42f))) Game.Do(Game.LeaveMatch);
            }
            else if (Btn("Abandon drill (counts as a fail)", button, GUILayout.Height(42f))) Game.Do(Game.AbandonDrill);
            GUILayout.EndHorizontal();
            SectionHeader("Controls");
            DrawControls();
            GUILayout.EndArea();
        }

        // ---- (5) result ------------------------------------------------------------------------------------
        void DrawResult()
        {
            ResultMsg res = Game.Result;
            if (res == null) return;
            DrillSpec spec = Game.Drill?.spec ?? new DrillSpec();
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.3f));
            GUILayout.BeginArea(Centered(780f, H - 60f), panel);
            resultScroll = GUILayout.BeginScrollView(resultScroll, false, false);
            Color old = GUI.contentColor;
            GUI.contentColor = res.success ? Palette.Ok : Palette.Bad;
            GUILayout.Label(res.success ? "DRILL COMPLETE" : "NOT THIS TIME", verdictTitle);
            GUI.contentColor = old;
            if (!string.IsNullOrEmpty(res.headline)) GUILayout.Label(res.headline, h2);
            int ticks = res.result != null ? res.result.ticks : 0;
            GUILayout.Label((spec.title ?? "Drill") + "  (" + GameController.SkillName(spec.skill) + ", " + (ticks / Mathf.Max(1f, Game.Rules.tickRate)).ToString("0.0") + " s)", bodyBold);
            if (!string.IsNullOrEmpty(spec.goal)) GUILayout.Label(spec.goal, muted);

            SectionHeader("Difficulty");
            if (res.staircaseBefore.HasValue && res.staircaseAfter.HasValue)
            {
                float b = res.staircaseBefore.Value, a = res.staircaseAfter.Value;
                string change = a > b + 1e-4f ? "harder" : a < b - 1e-4f ? "easier" : "the same";
                GUILayout.Label(string.Format("{0} drills go from {1:0.00} -> {2:0.00} ({3}).", GameController.SkillName(spec.skill), b, a, change), body);
                Rect r = GUILayoutUtility.GetRect(100f, 10f, GUILayout.ExpandWidth(true));
                Round(r, new Color(1f, 1f, 1f, 0.08f));
                if (a > 0.01f) Round(new Rect(r.x, r.y, Mathf.Max(10f, r.width * Mathf.Clamp01(a)), r.height), a >= b ? Palette.Attack : Palette.Defense);
                Fill(new Rect(r.x + r.width * Mathf.Clamp01(b) - 1.5f, r.y - 3f, 3f, r.height + 6f), Color.white);
            }
            else
            {
                GUILayout.Label("No staircase change for this round.", muted);
            }

            List<string> lines = WhatHappened(res);
            if (lines.Count > 0)
            {
                SectionHeader("What happened");
                foreach (string l in lines) GUILayout.Label(l, small);
            }

            SectionHeader("Coach");
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

            GUILayout.Space(Theme.S2);
            GUILayout.BeginHorizontal();
            // Like the 2D client, wait for the debrief before the next drill (with a timeout in case it never comes).
            GUI.enabled = Idle && (db != null || Time.unscaledTime - Game.ResultAt > 15f);
            if (Btn("Next drill", primary, GUILayout.Height(48f))) Game.Do(Game.RequestNextDrill);
            if (Btn("Retry this drill", button, GUILayout.Height(48f))) Game.Do(Game.RequestRetry);
            GUI.enabled = true;
            if (Btn("Back to report", button, GUILayout.Height(48f))) Game.Do(Game.BackToReport);
            GUILayout.EndHorizontal();
            Waiting();
            GUILayout.EndArea();
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
                string at = (e.tick / rate).ToString("0.0") + " s  ";
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

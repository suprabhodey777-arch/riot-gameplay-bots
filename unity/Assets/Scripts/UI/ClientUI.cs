// All screens and the HUD, drawn with IMGUI's immediate GUI.* calls and custom
// GUIStyles (no Canvas, no TextMeshPro, no GUILayout). Layout works in a
// virtual space 900 px high scaled to the window (so everything scales with
// the window height); text is drawn at the real pixel size (ClientUI.Kit.cs).
// Buttons queue their action through GameController.Do so a screen change
// never happens in the middle of IMGUI's event passes.
//
// Split across ClientUI.cs (styles, frame, in-drill HUD), .Kit (crisp text,
// button component, keyboard focus, controls), .Style (design language,
// sting), .Practice (connect, sample player, map select, report, intro, pause,
// result), .Advice (report's agent advice), .MapView (the tactical map on M),
// .Match (menu, match setup, buy, banners), .Hud (in-round HUD parts),
// .Agents (agent select), .Streamer (overlay, personas, duels) and .Settings.
// The in-drill HUD's strings are cached so a frame allocates nothing; the
// crosshair and minimap are drawn with GL (Draw2D).
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
        AppScreen shownScreen = (AppScreen)(-1);
        bool shownSettings;
        float screenAt;
        float spread;

        GUIStyle title, display, h2, h3, body, bodyBold, muted, small, caption, mono;
        GUIStyle roundWhite, roundSmall;
        GUIStyle slider, thumb;
        GUIStyle hudClock, hudSmall, hudBig, hudCenter, feed, banner, bannerSub, botName, botLine, mapLetter, fpsStyle;
        GUIStyle monoBig, lead, debrief, verdictTitle, severityLabel, toastStyle, hudTitle, hudSkill, hudGoal, capLeft, capRight, spikeStyle;
        GUIStyle slotKey, slotCount, slotName, modeStyle;
        readonly List<Texture2D> owned = new List<Texture2D>();

        static readonly string[] SkillLabels = { "Entry", "Clutch", "Time pressure", "Smoke usage" };

        static readonly string[][] Controls =
        {
            new[] { "Mouse", "Look around" },
            new[] { "W A S D", "Move (running is heard through walls within 15 m)" },
            new[] { "Shift", "Walk (quiet: heard only within 3 m)" },
            new[] { "Ctrl", "Crouch (quiet, slowest)" },
            new[] { "Left click", "Fire (pistols, the Brick and the Longshot: one shot per click)" },
            new[] { "Right click", "Scope (Longshot): zoom, slower turn, accurate only when still" },
            new[] { "1 / 2 / wheel", "Primary / secondary weapon / switch" },
            new[] { "R", "Reload" },
            new[] { "B", "Buy menu during a match's buy phase" },
            new[] { "Hold C", "Smoke: aim at the floor, release to throw" },
            new[] { "Hold Q", "Flash: aim, release; it pops 0.6 s later. Look away" },
            new[] { "E", "Dash in the direction you are moving" },
            new[] { "Hold X", "Recon dart: reveals enemies near it through walls" },
            new[] { "Hold F", "Plant (attack, on a site) or defuse (defense, at the spike)" },
            new[] { "M / hold Tab", "Tactical map / peek at it" },
            new[] { "F3", "Frame rate and prediction readout" },
            new[] { "Esc", "Pause (settings are in the pause menu)" },
        };

        static readonly string[] WaitText = { "Waiting for the bridge.", "Waiting for the bridge..", "Waiting for the bridge..." };

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

        void EnsureStyles()
        {
            if (ready && stylesAccent == Settings.AccentColor) return;
            foreach (Texture2D t in owned) if (t != null) Destroy(t);
            owned.Clear();
            crispStyles.Clear();
            ready = true;
            stylesAccent = Settings.AccentColor;
            Color accent = Settings.Accent;

            Texture2D texWhite = Own(Theme.Chamfer(Color.white, Theme.Cut));
            Texture2D texTrack = Own(Theme.Rounded(new Color(1f, 1f, 1f, 0.14f), 3));
            Texture2D texThumb = Own(Theme.Chamfer(Color.white, 4, new Color(accent.r, accent.g, accent.b, 1f), 2.5f));
            Texture2D texThumbHover = Own(Theme.Chamfer(Theme.Lighten(accent, 0.6f), 4, accent, 2.5f));

            GUIStyle lbl = GUI.skin.label;
            Color white = Theme.Ink, grey = Theme.Muted;
            display = Text(lbl, Theme.Display, FontStyle.Bold, white);
            title = Text(lbl, Theme.Title, FontStyle.Bold, white);
            h2 = Text(lbl, Theme.H2, FontStyle.Bold, white);
            h3 = Text(lbl, Theme.H3, FontStyle.Bold, white);
            body = Text(lbl, Theme.Body, FontStyle.Normal, white);
            bodyBold = Text(lbl, Theme.Body, FontStyle.Bold, white);
            muted = Text(lbl, 15, FontStyle.Normal, grey);
            small = Text(lbl, Theme.Small, FontStyle.Normal, grey);
            caption = Text(lbl, Theme.Caption, FontStyle.Bold, grey, TextAnchor.UpperLeft, false);
            mono = Text(lbl, 15, FontStyle.Bold, Theme.Lighten(accent, 0.35f), TextAnchor.UpperLeft, false);

            roundWhite = new GUIStyle { border = Theme.Border(Theme.Cut) };
            roundWhite.normal.background = texWhite;
            roundSmall = new GUIStyle { border = Theme.Border(4) };
            roundSmall.normal.background = Own(Theme.Chamfer(Color.white, 4));

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
            hudCenter = Text(lbl, 18, FontStyle.Bold, white, TextAnchor.MiddleCenter, false);
            feed = Text(lbl, 15, FontStyle.Bold, white, TextAnchor.MiddleRight, false);
            banner = Text(lbl, 46, FontStyle.Bold, white, TextAnchor.MiddleCenter, false);
            bannerSub = Text(lbl, 20, FontStyle.Normal, white, TextAnchor.MiddleCenter, true);
            botName = Text(lbl, 15, FontStyle.Bold, white, TextAnchor.LowerCenter, false);
            botLine = Text(lbl, 13, FontStyle.Normal, white, TextAnchor.LowerCenter, false);
            mapLetter = Text(lbl, 17, FontStyle.Bold, white, TextAnchor.MiddleCenter, false);
            fpsStyle = Text(lbl, 13, FontStyle.Bold, white, TextAnchor.UpperLeft, false);

            monoBig = Text(mono, 20, FontStyle.Bold, mono.normal.textColor, TextAnchor.UpperLeft, false);
            lead = Text(lbl, 19, FontStyle.Normal, white);
            debrief = Text(lbl, 17, FontStyle.Normal, white);
            verdictTitle = Text(lbl, 34, FontStyle.Bold, Color.white);
            severityLabel = Text(lbl, 15, FontStyle.Bold, Color.white, TextAnchor.MiddleRight, false);
            toastStyle = Text(lbl, 15, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            hudTitle = Text(lbl, 19, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            hudSkill = Text(lbl, 14, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            hudGoal = Text(lbl, 14, FontStyle.Normal, Color.white);
            capLeft = Text(lbl, 12, FontStyle.Bold, grey, TextAnchor.UpperLeft, false);
            capRight = Text(lbl, 12, FontStyle.Bold, grey, TextAnchor.UpperRight, false);
            spikeStyle = Text(lbl, 16, FontStyle.Bold, Color.white, TextAnchor.MiddleRight, false);
            slotKey = Text(lbl, 13, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            slotCount = Text(lbl, 15, FontStyle.Bold, Color.white, TextAnchor.LowerRight, false);
            slotName = Text(lbl, 12, FontStyle.Bold, grey, TextAnchor.UpperCenter, false);
            modeStyle = Text(lbl, 14, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
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
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>An angular plate in colour `c` (alpha multiplies with the current GUI colour).</summary>
        void Round(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            float m = Mathf.Min(r.width, r.height);
            // Thin shapes use a smaller cut so the 9-slice borders never overlap.
            if (m >= 26f) roundWhite.Draw(r, false, false, false, false);
            else if (m >= 12f) roundSmall.Draw(r, false, false, false, false);
            else GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void Shadowed(Rect r, string text, GUIStyle s, Color c)
        {
            if (string.IsNullOrEmpty(text)) return;
            Color old = GUI.contentColor;
            GUI.contentColor = new Color(0f, 0f, 0f, 0.7f * c.a);
            TextAt(new Rect(r.x + 1.25f, r.y + 1.25f, r.width, r.height), text, s);
            GUI.contentColor = c;
            TextAt(r, text, s);
            GUI.contentColor = old;
        }

        /// <summary>The opening animation of the current screen: 0 just opened, 1 settled.</summary>
        float Intro01 => Theme.Ease((Time.unscaledTime - screenAt) / 0.28f);

        Rect Centered(float w, float h)
        {
            w = Mathf.Min(w, W - 40f);
            h = Mathf.Min(h, H - 40f);
            float rise = (1f - Intro01) * 12f;
            return new Rect(Mathf.Round((W - w) / 2f), Mathf.Round((H - h) / 2f + rise), w, h);
        }

        bool Idle => Game.WaitingFor == null && Game.Connected;

        static void Icon(Rect r, Ability a, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            GUI.DrawTexture(r, Icons.For(a), ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        // ---- entry ------------------------------------------------------------------------------
        void LateUpdate()
        {
            if (Game == null) return;
            // Every screen uses GUI.* calls only: IMGUI's layout pass is pure overhead.
            useGUILayout = false;
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
            clipDepth = 0;
            float now = Time.unscaledTime;
            StingInput(now);
            BeginFocusFrame();

            bool menu = Game.Screen != AppScreen.Playing;
            bool settings = Game.SettingsOpen;
            if (menu && Game.Screen != AppScreen.Buy) Backdrop(Game.Screen == AppScreen.Intro ? 0.5f : 1f);
            // With the settings open over a menu, the menu under them is not drawn (its buttons would take the clicks).
            if (!(menu && settings))
            {
                GUI.color = new Color(1f, 1f, 1f, menu ? Mathf.Lerp(0.2f, 1f, Intro01) : 1f);
                switch (Game.Screen)
                {
                    case AppScreen.Connect: DrawConnect(); break;
                    case AppScreen.Menu: DrawMenu(); break;
                    case AppScreen.MatchSetup: DrawMatchSetup(); break;
                    case AppScreen.Buy:
                        // The round banners go under the buy panel: its header already carries the round and score.
                        DrawRoundBanners(now);
                        bool buyPaused = Game.Paused && !Game.SettingsOpen;
                        bool buyEnabled = GUI.enabled;
                        if (buyPaused) GUI.enabled = false;
                        DrawBuy(false);
                        if (Settings.StreamerMode) DrawStreamerOverlay(now, false);
                        GUI.enabled = buyEnabled;
                        if (buyPaused) DrawPause();
                        break;
                    case AppScreen.MatchEnd: DrawMatchEnd(); break;
                    case AppScreen.Choose: DrawChoose(); break;
                    case AppScreen.MapSelect: DrawMapSelect(); break;
                    case AppScreen.AgentSelect: DrawAgentSelect(); break;
                    case AppScreen.Report: DrawReport(); break;
                    case AppScreen.Intro: DrawIntro(); break;
                    case AppScreen.Playing: DrawHud(); break;
                    case AppScreen.Result: DrawResult(); break;
                    case AppScreen.PersonaSelect: DrawPersonaSelect(); break;
                    case AppScreen.DuelSetup: DrawDuelSetup(); break;
                    case AppScreen.DuelEnd: DrawDuelEnd(); break;
                }
                GUI.color = Color.white;
            }
            // The menus' Settings button (top right); the tall report and result panels carry their own.
            bool noSettingsButton = Game.Screen == AppScreen.Menu || Game.Screen == AppScreen.Buy || Game.Screen == AppScreen.MatchEnd || Game.Screen == AppScreen.DuelEnd
                                    || Game.Screen == AppScreen.Report || Game.Screen == AppScreen.Result;
            if (menu && !settings && !noSettingsButton) DrawSettingsButton();
            if (settings)
            {
                focusLayer = 1;
                DrawSettings();
                focusLayer = 0;
            }
            if (menu || settings) DrawTransition(now);
            DrawFps();
            DrawToast();
            DrawSting(now);
            ScreenSounds();
            FinishHoverSounds();
            EndFocusFrame();
            // The flash also washes out the HUD, so it has to land after everything else this frame.
            if (Game.Screen == AppScreen.Playing && FlashOverlay.Active) FlashOverlay.Draw(new Rect(0f, 0f, W, H), Game.BlindSecondsLeft, Game.BlindTotalSeconds, true);
        }

        void DrawSettingsButton()
        {
            if (GButton(new Rect(W - 172f, 20f, 148f, 40f), "Settings", 2, true, 0, false, UiTex.Glyph("gear"))) Game.Do(Game.OpenSettings);
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
            // In a round: bottom left above the movement chip (clear of the spike line and the weapon panel).
            var r = Game.Screen == AppScreen.Playing ? new Rect(22f, H - 236f, 330f, 40f) : new Rect(24f, H - 52f, 330f, 40f);
            Shadowed(r, fpsText, fpsStyle, Game.Fps >= 110f ? Palette.Ok : Game.Fps >= 60f ? Palette.Warn : Palette.Bad);
        }

        void DrawToast()
        {
            if (string.IsNullOrEmpty(Game.Toast) || Time.unscaledTime > Game.ToastUntil) return;
            // Under the score bug in a round (clear of the drill card when one is shown), low on the menus.
            float y = Game.Screen == AppScreen.Playing ? (hudCardBottom > 0f ? hudCardBottom + 12f : 124f) : H - 84f;
            float w = Mathf.Min(680f, Mathf.Max(320f, TextW(Game.Toast, toastStyle) + 60f));
            var r = new Rect(W / 2f - w / 2f, y, w, 44f);
            Shadow(r, 12f, 0.5f);
            Color toastPlate = Theme.Darken(Palette.Bad, 0.6f);
            Plate(r, new Color(toastPlate.r, toastPlate.g, toastPlate.b, 0.96f));
            Fill(new Rect(r.x + 2f, r.y + 10f, 3f, r.height - 20f), Palette.Bad);
            Lbl(new Rect(r.x + 16f, r.y, r.width - 32f, r.height), Game.Toast, toastStyle, Color.white);
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
        float hudGoalH, hudInfoW = -1f;
        /// <summary>The drill card's bottom this repaint (-1 when no card), so the toast can clear it.</summary>
        float hudCardBottom = -1f;
        Color hudSetupColor = Color.white;

        void CacheDrillStrings(float infoW)
        {
            if (ReferenceEquals(hudDrill, Game.Drill) && Mathf.Abs(infoW - hudInfoW) < 0.5f) return;
            hudDrill = Game.Drill;
            hudInfoW = infoW;
            DrillSpec spec = hudDrill?.spec;
            hudTitleStr = spec?.title ?? "";
            hudSkillStr = spec == null ? "" : Upper(GameController.SkillName(spec.skill)) + "   \u00b7   DIFFICULTY " + spec.difficulty.ToString("0.00");
            string setup = Game.MapName;
            if (!string.IsNullOrEmpty(Game.PlayerAgent))
            {
                string role = Agents.Get(Game.PlayerAgent)?.Role ?? "agent";
                setup += (setup.Length > 0 ? "   \u00b7   " : "") + Agents.NameOf(Game.PlayerAgent) + " (" + Upper(role) + ")";
            }
            hudSetupStr = setup;
            hudSetupColor = string.IsNullOrEmpty(Game.PlayerAgent) ? Color.white : Agents.ColorOf(Game.PlayerAgent);
            hudGoalStr = spec?.goal ?? "";
            hudGoalH = Mathf.Min(80f, TextH(hudGoalStr, hudGoal, infoW - 24f));
        }

        void DrawHud()
        {
            SimState s = Game.State;
            AgentState me = Game.Me;
            float now = Time.unscaledTime;
            Rules rules = Game.Rules;
            if (s == null) return;
            bool repaint = Event.current.type == EventType.Repaint;

            // Screen-space effects first, so text stays readable on top.
            Camera cam = Game.Rig.Cam;
            float smoke = Game.World.SmokeDensityAt(cam.transform.position);
            if (smoke > 0f) Fill(new Rect(0f, 0f, W, H), new Color(Palette.Smoke.r * 0.9f, Palette.Smoke.g * 0.9f, Palette.Smoke.b * 0.9f, 0.92f * smoke));
            float dmg = 1f - Mathf.Clamp01((now - Game.DamageAt) / 0.45f);
            if (dmg > 0f && repaint)
            {
                Color oldD = GUI.color;
                GUI.color = new Color(Palette.Bad.r, Palette.Bad.g, Palette.Bad.b, 0.55f * dmg);
                GUI.DrawTexture(new Rect(-30f, -30f, W + 60f, H + 60f), UiTex.Vignette, ScaleMode.StretchToFill, true);
                GUI.color = oldD;
            }
            DrawLowHealth(me, now);
            DrawDashStreaks();

            if (repaint && !Game.FullMap) DrawBotLabels(cam);
            if (repaint) DrawDamageNumbers(cam, now);

            bool alive = me != null && me.alive;
            DrawScope();
            bool scoped = Game.Gun.Weapon.Scopes && Game.Gun.Scope01 >= 0.9f;
            if (alive && !Game.Paused && repaint && !scoped && !Game.BuyOpen && !Game.MapOpen) DrawCrosshair(now);
            else if (alive && !Game.Paused && repaint && scoped)
            {
                // Hit markers still show through the scope.
                int hk2 = Crosshair.PixelScale(Screen.height);
                Draw2D.Begin(Screen.width, Screen.height);
                Crosshair.HitMarker(Screen.width / 2f, Screen.height / 2f, hk2, now - Game.HitMarkerAt, Game.HitMarkerHead, Game.HitMarkerKill);
                Draw2D.End();
            }
            if (alive && !Game.Paused) DrawDamageIndicators(now);

            // Blinded: FlashOverlay's white-out and afterimage over the world and the crosshair.
            FlashOverlay.Draw(new Rect(0f, 0f, W, H), Game.BlindSecondsLeft, Game.BlindTotalSeconds, false);

            // The tactical map (M) or the peek (Tab) covers the play view; the round's HUD stays on top of it.
            if (Game.FullMap) DrawTacticalMap(Game.MapPeek && !Game.MapOpen);

            // Top left: minimap, then the drill info beside it.
            float infoX = 20f;
            if (Settings.MinimapOn && !Game.FullMap)
            {
                float ms = Settings.MinimapSize;
                var mr = new Rect(20f, 18f, ms, ms);
                DrawMap(mr, Settings.MinimapOpacity);
                infoX = mr.xMax + 20f;
            }

            // Top centre: the score bug and round clock.
            DrawScoreBug(s, now);
            if (alive && Game.World.IsRevealed(Game.PlayerId))
            {
                float pulse = 0.7f + 0.3f * Mathf.Sin(now * 10f);
                var rr = new Rect(W / 2f - 120f, 168f, 240f, 28f);
                Color rc = Agents.AbilityColor(Ability.Recon);
                Plate(rr, new Color(rc.r * 0.35f, rc.g * 0.2f, rc.b * 0.25f, 0.88f));
                Shadowed(rr, "YOU ARE REVEALED", hudCenter, new Color(rc.r, rc.g, rc.b, pulse));
            }

            // Drill, map and agent: a quiet card beside the minimap, clear of the score bug, or stacked under the
            // minimap when the window is too narrow for a card beside it.
            float iw = Mathf.Min(420f, W / 2f - 170f - infoX);
            float iy = 18f;
            if (iw < 180f)
            {
                // Too narrow for a card beside the minimap: stack it under the minimap (or, with the minimap off,
                // under the score bug) instead of dropping it.
                infoX = 20f;
                iw = Mathf.Max(200f, Mathf.Min(340f, W - 40f));
                iy = Settings.MinimapOn && !Game.FullMap ? 18f + Settings.MinimapSize + 12f : 118f;
            }
            if (!Game.FullMap) CacheDrillStrings(iw);
            hudCardBottom = -1f;
            if (hudDrill?.spec != null && !Game.FullMap)
            {
                float ih = 12f + 24f + (Game.InMatch ? 0f : 20f) + (hudSetupStr.Length > 0 ? 20f : 0f) + (hudGoalStr.Length > 0 && !Game.InMatch ? hudGoalH + 6f : 0f) + 10f;
                var r = new Rect(infoX, iy, iw, ih);
                hudCardBottom = r.yMax;
                Plate(r, new Color(0.02f, 0.025f, 0.04f, 0.55f));
                Fill(new Rect(r.x + 2f, r.y + 10f, 3f, 22f), Palette.Team(Game.PlayerTeam));
                float x = r.x + 14f, w = r.width - 24f, y = r.y + 10f;
                Shadowed(new Rect(x, y, w, 24f), Upper(Game.InMatch ? "Match" : hudTitleStr), hudTitle, Color.white);
                y += 26f;
                if (!Game.InMatch)
                {
                    Shadowed(new Rect(x, y, w, 20f), hudSkillStr, hudSkill, Palette.Team(Game.PlayerTeam));
                    y += 20f;
                }
                if (hudSetupStr.Length > 0)
                {
                    Shadowed(new Rect(x, y, w, 20f), hudSetupStr, hudSkill, hudSetupColor);
                    y += 20f;
                }
                if (hudGoalStr.Length > 0 && !Game.InMatch) Shadowed(new Rect(x, y + 4f, w, hudGoalH), hudGoalStr, hudGoal, new Color(0.9f, 0.9f, 0.92f));
            }

            // Top right: kill feed (the tactical map's legend takes that corner while it is open).
            if (!Game.FullMap) DrawKillFeed(now);

            // Bottom left: health, armour, credits and movement mode.
            DrawVitals(me, alive, now);

            // Bottom centre: abilities.
            DrawAbilityBar(me, now);

            // Bottom right: weapon and ammo, the spike above it.
            DrawAmmo(me, now);
            UpdateSpikeText(s);
            if (spikeStr.Length > 0)
            {
                var sr = new Rect(W - 440f, H - 160f, 420f, 30f);
                // On a dark plate: dim text straight on a sunlit floor could not be read.
                Plate(new Rect(sr.x + 100f, sr.y - 22f, sr.width - 100f, sr.height + 26f), new Color(0.02f, 0.025f, 0.04f, 0.78f));
                Shadowed(new Rect(sr.x, sr.y, sr.width - 12f, sr.height), spikeStr, spikeStyle, s.spike != null && s.spike.status == "planted" ? Theme.Lighten(Palette.Bad, 0.35f) : Color.white);
                Lbl(new Rect(sr.x, sr.y - 18f, sr.width - 12f, 18f), "S P I K E", capRight, new Color(0.75f, 0.77f, 0.82f));
            }

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
                var pr = new Rect(cx - 150f, cy + 76f, 300f, 10f);
                Shadowed(new Rect(pr.x, pr.y - 28f, pr.width, 24f), progressStr, hudCenter, Color.white);
                Plate(pr, new Color(0f, 0f, 0f, 0.5f));
                if (progress > 0.01f) Fill(new Rect(pr.x, pr.y, Mathf.Max(10f, pr.width * Mathf.Clamp01(progress)), pr.height), defusing ? Palette.Defense : Palette.Attack);
            }
            else if (alive && !Game.DrillDone)
            {
                string hint = null;
                if (Game.PlayerTeam == "attack" && me.carryingSpike && Game.OnSite(me.pos)) hint = "Hold F to plant";
                else if (Game.PlayerTeam == "defense" && Game.NearPlantedSpike(me.pos)) hint = "Hold F to defuse";
                if (hint != null) Shadowed(new Rect(cx - 160f, cy + 60f, 320f, 26f), hint, hudCenter, new Color(1f, 0.95f, 0.8f));
            }
            if (!alive) Shadowed(new Rect(cx - 260f, cy + 44f, 520f, 30f), "You are down. The round plays out.", hudCenter, Theme.Lighten(Palette.Bad, 0.45f));
            if (Game.DrillDone && Game.Result == null && !Game.InMatch) Shadowed(new Rect(cx - 260f, cy + 104f, 520f, 30f), "Round over. Waiting for the verdict...", hudCenter, Color.white);
            if (Game.DrillDone && Game.InMatch && Game.RoundResult == null) Shadowed(new Rect(cx - 260f, cy + 104f, 520f, 30f), "Round over.", hudCenter, Color.white);

            // Verdict banner.
            if (Game.Result != null && now < Game.VerdictUntil)
            {
                bool ok = Game.Result.success;
                float k = Theme.Ease((now - (Game.VerdictUntil - 2.5f)) / 0.3f);
                var br = new Rect(0f, H * 0.28f + (1f - k) * 20f, W, 124f);
                Fill(br, new Color(0.03f, 0.04f, 0.06f, 0.86f * k));
                Fill(new Rect(W / 2f - W * 0.5f * k, br.y, W * k, 3f), ok ? Palette.Ok : Palette.Bad);
                Fill(new Rect(W / 2f - W * 0.5f * k, br.yMax - 3f, W * k, 3f), ok ? Palette.Ok : Palette.Bad);
                Color vc = ok ? Palette.Ok : Palette.Bad;
                vc.a = k;
                Condensed(new Rect(0f, br.y + 10f, W, 60f), Spaced(ok ? "Drill complete" : "Not this time"), banner, vc, 0.86f, true);
                Shadowed(new Rect(40f, br.y + 74f, W - 80f, 40f), Game.Result.headline ?? "", bannerSub, new Color(1f, 1f, 1f, k));
            }
            if (Settings.StreamerMode && !Game.FullMap) DrawStreamerOverlay(now, true);
            if (Game.InMatch) DrawRoundBanners(now);
            if (Game.BuyOpen && Game.InMatch) DrawBuy(true);

            if (Game.Paused && !Game.DrillDone && !Game.SettingsOpen) DrawPause();
        }

        /// <summary>The corner minimap in a virtual-space rect, with site letters on top.</summary>
        void DrawMap(Rect vr, float opacity)
        {
            var sr = new Rect(vr.x * scale, vr.y * scale, vr.width * scale, vr.height * scale);
            Game.Minimap.Draw(Game, sr, false, Game.EyePos, Game.Rig.Heading, opacity, scale);
            if (Event.current.type != EventType.Repaint) return;
            IReadOnlyList<Minimap.SiteLabel> sites = Game.Minimap.Sites;
            for (int i = 0; i < sites.Count; i++)
            {
                Vector2 p = Game.Minimap.PanelPoint(sites[i].Sim) / scale;
                if (p.x < 10f || p.y < 10f || p.x > vr.width - 10f || p.y > vr.height - 10f) continue;
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
                if (mode == "crouch") modeStr = "CROUCHED";
                else if (mode == "walk") modeStr = "WALKING";
                else modeStr = moving ? "RUNNING" : "STILL";
                modeWidth = modeStyle.CalcSize(new GUIContent(modeStr)).x;
            }
            Plate(new Rect(r.x, r.y, modeWidth + 32f, r.height), new Color(0.02f, 0.025f, 0.04f, 0.68f));
            Fill(new Rect(r.x + 3f, r.y + 6f, 3f, r.height - 12f), c);
            // A footprint-like dot that pulses with the stride while moving.
            float pulse = moving ? 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * (mode == "run" ? 9f : 5f))) : 0.4f;
            Plate(new Rect(r.x + 12f, r.y + r.height / 2f - 3f, 6f, 6f), new Color(c.r, c.g, c.b, pulse));
            Shadowed(new Rect(r.x + 24f, r.y, modeWidth + 6f, r.height), modeStr, modeStyle, c);
        }

        float modeWidth;

        // The ability bar is in ClientUI.Hud.cs.

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
                string name = Game.DisplayName(b.Id);
                if (revealed)
                {
                    int m = Mathf.RoundToInt(sp.z);
                    if (m != b.RevealLabelM || b.RevealLabel == null)
                    {
                        b.RevealLabelM = m;
                        b.RevealLabel = name + "  REVEALED " + m + " m";
                    }
                    name = b.RevealLabel;
                }
                Shadowed(new Rect(x - 160f, y - 56f, 320f, 20f), name, botName, revealed ? Settings.Enemy : tc);
                Shadowed(new Rect(x - 160f, y - 37f, 320f, 18f), b.LabelLine ?? "", botLine, string.IsNullOrEmpty(b.Agent) ? new Color(0.92f, 0.92f, 0.95f) : Color.Lerp(Agents.ColorOf(b.Agent), Color.white, 0.35f));
                string thought = b.Blind ? "dazed" : b.ThoughtText();
                if (!string.IsNullOrEmpty(thought))
                    Shadowed(new Rect(x - 160f, y - 19f, 320f, 18f), thought, botLine, b.Blind ? new Color(1f, 0.95f, 0.65f) : new Color(0.65f, 0.95f, 0.88f));
            }
        }
    }
}

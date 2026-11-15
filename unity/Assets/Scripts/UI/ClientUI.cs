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
        int stylesAccent = -1, stylesFont = -1;
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

        /// <summary>Bold text at this size or larger is set in the display face.</summary>
        const int DisplayFrom = 17;
        /// <summary>The condensed face reads smaller than a grotesque at the same size; grow it a little.</summary>
        const float DisplayGrow = 1.12f;
        /// <summary>Vertical correction for the display face, as a fraction of its size (its caps sit high or low in the line box).</summary>
        const float DisplayNudge = 0f;

        /// <summary>A display-face style at an exact size (titles, numbers, buttons).</summary>
        static GUIStyle DisplayText(GUIStyle from, int size, TextAnchor a)
        {
            GUIStyle s = Text(from, Mathf.Max(DisplayFrom, size), FontStyle.Bold, Color.white, a, false);
            if (size < DisplayFrom) s.fontSize = Theme.DisplayIsReal ? Mathf.RoundToInt(size * DisplayGrow) : size;
            return s;
        }

        static GUIStyle Text(GUIStyle from, int size, FontStyle fs, Color c, TextAnchor a = TextAnchor.UpperLeft, bool wrap = true)
        {
            var s = new GUIStyle(from) { fontSize = size, fontStyle = fs, alignment = a, wordWrap = wrap, richText = false };
            // Type (docs/art-direction.md): large bold text is the condensed display face, small bold text the
            // body face's bold cut, everything else the body face. With no OS face found, the built-in font stays.
            bool bold = fs == FontStyle.Bold;
            if (bold && size >= DisplayFrom && Theme.DisplayFont != null)
            {
                s.font = Theme.DisplayFont;
                s.fontStyle = Theme.DisplayUsesBold ? FontStyle.Bold : FontStyle.Normal;
                s.fontSize = Mathf.RoundToInt(size * DisplayGrow);
                s.contentOffset = new Vector2(0f, s.fontSize * DisplayNudge);
            }
            else if (bold && Theme.BodyBoldFont != null) s.font = Theme.BodyBoldFont;
            else if (Theme.BodyFont != null) s.font = Theme.BodyFont;
            s.clipping = TextClipping.Overflow;
            s.normal.textColor = c;
            s.hover.textColor = c;
            s.active.textColor = c;
            return s;
        }

        void EnsureStyles()
        {
            if (ready && stylesAccent == Settings.AccentColor && stylesFont == Theme.FontVersion) return;
            stylesFont = Theme.FontVersion;
            chatStyle = null;
            foreach (Texture2D t in owned) if (t != null) Destroy(t);
            owned.Clear();
            // Destroy is deferred to the end of the frame, so these would still look alive below: drop them now.
            fadeLeft = null;
            fadeDown = null;
            crispStyles.Clear();
            ready = true;
            stylesAccent = Settings.AccentColor;
            Color accent = Settings.Accent;

            Texture2D texWhite = Own(Theme.Chamfer(Color.white, Theme.PrimaryCut, new Color(0f, 0f, 0f, 0f), 0f, false));
            Texture2D texTrack = Own(Theme.Rounded(new Color(1f, 1f, 1f, 0.14f), 3));
            Texture2D texThumb = Own(Theme.Rounded(Theme.Ink, 9));
            Texture2D texThumbHover = Own(Theme.Rounded(Color.white, 9));

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

            roundWhite = new GUIStyle { border = Theme.Border(Theme.PrimaryCut) };
            roundWhite.normal.background = texWhite;
            roundSmall = new GUIStyle { border = Theme.Border(4) };
            roundSmall.normal.background = Own(Theme.Rounded(Color.white, 3));

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
            banner = Text(lbl, 40, FontStyle.Bold, white, TextAnchor.MiddleCenter, false);
            bannerSub = Text(lbl, 17, FontStyle.Normal, white, TextAnchor.MiddleCenter, false);
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
            // Flat, square-cornered: panels, cards and controls are plain rectangles.
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>The primary button's shape: a rectangle with one chamfer (bottom right).</summary>
        void PrimaryPlate(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            if (Mathf.Min(r.width, r.height) >= 26f) roundWhite.Draw(r, false, false, false, false);
            else GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>A small round dot (status marks, pips).</summary>
        void Dot(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            roundSmall.Draw(r, false, false, false, false);
            GUI.color = old;
        }

        void Shadowed(Rect r, string text, GUIStyle s, Color c)
        {
            if (string.IsNullOrEmpty(text)) return;
            Color old = GUI.contentColor;
            // A soft shadow: a wider faint copy under a tighter dark one, so text reads on a bright floor.
            GUI.contentColor = new Color(0f, 0f, 0f, 0.32f * c.a);
            TextAt(new Rect(r.x, r.y + 2.5f, r.width, r.height), text, s);
            GUI.contentColor = new Color(0f, 0f, 0f, 0.4f * c.a);
            TextAt(new Rect(r.x - 1f, r.y + 1f, r.width, r.height), text, s);
            GUI.contentColor = new Color(0f, 0f, 0f, 0.7f * c.a);
            TextAt(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, s);
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
            if (GUI.skin.font != Theme.BodyFont) GUI.skin.font = Theme.BodyFont;
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
            if (menu && Game.Screen != AppScreen.Buy) Backdrop(Game.Screen == AppScreen.Intro ? 0.45f : Game.Screen == AppScreen.Menu && !settings ? 0.6f : 1f);
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
                        DrawBuy(false);
                        DrawRoundBanners(now);
                        if (Settings.StreamerMode) DrawStreamerOverlay(now, false);
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
            var r = Game.Screen == AppScreen.Playing ? new Rect(36f, H - 206f, 330f, 40f) : new Rect(24f, H - 52f, 330f, 40f);
            Shadowed(r, fpsText, fpsStyle, Game.Fps >= 110f ? Palette.Ok : Game.Fps >= 60f ? Palette.Warn : Palette.Bad);
        }

        void DrawToast()
        {
            if (string.IsNullOrEmpty(Game.Toast) || Time.unscaledTime > Game.ToastUntil) return;
            // Under the score bug in a round (the bottom centre holds the ability bar), low on the menus.
            float y = Game.Screen == AppScreen.Playing ? 124f : H - 84f;
            float w = Mathf.Min(680f, Mathf.Max(320f, TextW(Game.Toast, toastStyle) + 60f));
            var r = new Rect(W / 2f - w / 2f, y, w, 44f);
            Plate(r, new Color(0.16f, 0.07f, 0.075f, 0.95f));
            Fill(new Rect(r.x, r.y, 2f, r.height), Palette.Bad);
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
        string hudLineStr = "";
        Color hudSetupColor = Color.white;

        void CacheDrillStrings(float infoW)
        {
            if (ReferenceEquals(hudDrill, Game.Drill) && Mathf.Abs(infoW - hudInfoW) < 0.5f) return;
            hudDrill = Game.Drill;
            hudInfoW = infoW;
            DrillSpec spec = hudDrill?.spec;
            hudTitleStr = spec?.title ?? "";
            string setup0 = Game.MapName ?? "";
            if (!string.IsNullOrEmpty(Game.PlayerAgent)) setup0 += (setup0.Length > 0 ? " / " : "") + Agents.NameOf(Game.PlayerAgent);
            hudSkillStr = spec == null ? "" : Upper(GameController.SkillName(spec.skill)) + "   \u00b7   DIFFICULTY " + spec.difficulty.ToString("0.00") + (setup0.Length > 0 ? "   \u00b7   " + Upper(setup0) : "");
            string setup = Game.MapName;
            if (!string.IsNullOrEmpty(Game.PlayerAgent))
                setup += (setup.Length > 0 ? "   \u00b7   " : "") + Agents.NameOf(Game.PlayerAgent) + " (" + (Agents.Get(Game.PlayerAgent)?.Role ?? "agent") + ")";
            hudSetupStr = setup;
            hudSetupColor = string.IsNullOrEmpty(Game.PlayerAgent) ? Color.white : Agents.ColorOf(Game.PlayerAgent);
            hudGoalStr = spec?.goal ?? "";
            hudGoalH = TextH(hudGoalStr, hudGoal, infoW - 28f);
            // The collapsed line: what this round is, in a few words.
            hudLineStr = Game.InMatch ? "MATCH" + (setup.Length > 0 ? "   \u00b7   " + Upper(setup) : "")
                : Upper(hudTitleStr) + (spec != null ? "   \u00b7   " + Upper(GameController.SkillName(spec.skill)) : "");
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
                GUI.color = new Color(0.95f, 0.08f, 0.08f, 0.55f * dmg);
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

            // Blinded: a white-out over the world and the crosshair that eases out over the blind's remaining time.
            float blind = Game.BlindAmount();
            if (blind > 0f)
            {
                Fill(new Rect(0f, 0f, W, H), new Color(1f, 1f, 0.97f, 0.97f * blind));
                if (blind > 0.5f) Shadowed(new Rect(0f, H * 0.62f, W, 30f), "BLINDED", hudCenter, new Color(0.35f, 0.35f, 0.4f, blind));
            }

            // The tactical map (M) or the peek (Tab) covers the play view; the round's HUD stays on top of it.
            if (Game.FullMap) DrawTacticalMap(Game.MapPeek && !Game.MapOpen);

            // The HUD's only backing: a dark gradient at the bottom edge.
            if (!Game.FullMap) HudBottomFade();

            // Top left: minimap, with one line of objective text under it.
            float infoY = 24f;
            if (Settings.MinimapOn && !Game.FullMap)
            {
                float ms = Settings.MinimapSize;
                var mr = new Rect(24f, 24f, ms, ms);
                DrawMap(mr, Settings.MinimapOpacity);
                PlateLine(new Rect(mr.x - 1f, mr.y - 1f, mr.width + 2f, mr.height + 2f), new Color(1f, 1f, 1f, 0.12f));
                infoY = mr.yMax + 10f;
            }

            // Top centre: the score bug and round clock.
            DrawScoreBug(s, now);
            if (alive && Game.World.IsRevealed(Game.PlayerId))
            {
                float pulse = 0.7f + 0.3f * Mathf.Sin(now * 10f);
                Color rc = Agents.AbilityColor(Ability.Recon);
                Shadowed(new Rect(W / 2f - 160f, 132f, 320f, 26f), "YOU ARE REVEALED", hudCenter, new Color(rc.r, rc.g, rc.b, pulse));
            }

            // The drill card shows for 5 s at round start, then collapses to one objective line.
            float iw = Mathf.Clamp(W / 2f - 280f, 220f, 380f);
            if (!Game.FullMap) CacheDrillStrings(iw);
            if (hudDrill?.spec != null && !Game.FullMap)
            {
                float since = now - screenAt;
                float cardK = Game.InMatch ? 0f : 1f - Mathf.Clamp01((since - 5f) / 0.35f);
                if (cardK > 0.01f)
                {
                    float ih = 14f + 26f + 20f + (hudGoalStr.Length > 0 ? hudGoalH + 8f : 0f) + 12f;
                    var r = new Rect(24f, infoY, iw, ih);
                    Color oldC = GUI.color;
                    GUI.color = new Color(oldC.r, oldC.g, oldC.b, oldC.a * cardK);
                    Fill(r, new Color(Theme.PanelC.r, Theme.PanelC.g, Theme.PanelC.b, 0.86f));
                    PlateLine(r, Theme.Line);
                    Fill(new Rect(r.x, r.y, r.width, 2f), Palette.Team(Game.PlayerTeam));
                    float x = r.x + 14f, w = r.width - 28f, y = r.y + 14f;
                    Condensed(new Rect(x, y, w, 24f), Upper(hudTitleStr), hudTitle, Theme.Ink);
                    y += 26f;
                    Condensed(new Rect(x, y, w, 18f), hudSkillStr, hudSkill, Theme.Muted);
                    y += 20f;
                    if (hudGoalStr.Length > 0) Lbl(new Rect(x, y + 4f, w, hudGoalH), hudGoalStr, hudGoal, Theme.Ink);
                    GUI.color = oldC;
                }
                else
                {
                    Fill(new Rect(24f, infoY + 3f, 2f, 14f), Palette.Team(Game.PlayerTeam));
                    Condensed(new Rect(34f, infoY, iw - 10f, 20f), hudLineStr, hudSkill, Theme.Ink, 1f, true);
                }
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
                var sr = new Rect(W - 36f - 520f, H - 166f, 520f, 20f);
                Shadowed(sr, Upper(spikeStr), hudLineR, s.spike != null && s.spike.status == "planted" ? Palette.Spike : Theme.Ink);
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
                pr = new Rect(pr.x, pr.y, pr.width, 4f);
                Fill(pr, new Color(0f, 0f, 0f, 0.5f));
                if (progress > 0.01f) Fill(new Rect(pr.x, pr.y, Mathf.Max(10f, pr.width * Mathf.Clamp01(progress)), pr.height), defusing ? Palette.Defense : Palette.Attack);
            }
            else if (alive && !Game.DrillDone)
            {
                string hint = null;
                if (Game.PlayerTeam == "attack" && me.carryingSpike && Game.OnSite(me.pos)) hint = "Hold F to plant";
                else if (Game.PlayerTeam == "defense" && Game.NearPlantedSpike(me.pos)) hint = "Hold F to defuse";
                if (hint != null) Shadowed(new Rect(cx - 160f, cy + 60f, 320f, 26f), hint, hudCenter, new Color(1f, 0.95f, 0.8f));
            }
            if (!alive) Shadowed(new Rect(cx - 260f, cy + 44f, 520f, 30f), "You are down. The round plays out.", hudCenter, new Color(1f, 0.7f, 0.7f));
            if (Game.DrillDone && Game.Result == null && !Game.InMatch) Shadowed(new Rect(cx - 260f, cy + 104f, 520f, 30f), "Round over. Waiting for the verdict...", hudCenter, Color.white);
            if (Game.DrillDone && Game.InMatch && Game.RoundResult == null) Shadowed(new Rect(cx - 260f, cy + 104f, 520f, 30f), "Round over.", hudCenter, Color.white);

            // Verdict banner.
            if (Game.Result != null && now < Game.VerdictUntil)
            {
                bool ok = Game.Result.success;
                float k = Theme.Ease((now - (Game.VerdictUntil - 2.5f)) / 0.3f);
                var br = new Rect(0f, H * 0.27f, W, 104f);
                Color vc = ok ? Palette.Ok : Palette.Bad;
                Fill(br, new Color(Theme.Base.r, Theme.Base.g, Theme.Base.b, 0.8f * k));
                Fill(new Rect(W / 2f - 90f * k, br.yMax - 2f, 180f * k, 2f), new Color(vc.r, vc.g, vc.b, k));
                vc.a = k;
                Shadowed(new Rect(0f, br.y + 8f, W, 50f), ok ? "DRILL COMPLETE" : "NOT THIS TIME", banner, vc);
                FitLine(new Rect(40f, br.y + 62f, W - 80f, 28f), Game.Result.headline ?? "", bannerSub, new Color(Theme.Ink.r, Theme.Ink.g, Theme.Ink.b, k));
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
            else c = moving ? Palette.Warn : Theme.Muted;
            if (key != modeKey)
            {
                modeKey = key;
                if (mode == "crouch") modeStr = moving ? "CROUCHING  quiet" : "CROUCHED";
                else if (mode == "walk") modeStr = moving ? "WALKING  quiet" : "WALK";
                else modeStr = moving ? "RUNNING  heard within " + Game.Rules.runHearingM.ToString("0") + " m" : "STILL";
                modeWidth = modeStyle.CalcSize(new GUIContent(modeStr)).x;
            }
            // A footprint-like dot that pulses with the stride while moving, then the words; no plate.
            float pulse = moving ? 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * (mode == "run" ? 9f : 5f))) : 0.5f;
            Fill(new Rect(r.x + 1f, r.center.y - 2f, 6f, 6f), new Color(0f, 0f, 0f, 0.45f));
            Fill(new Rect(r.x, r.center.y - 3f, 6f, 6f), new Color(c.r, c.g, c.b, pulse));
            Shadowed(new Rect(r.x + 14f, r.y, modeWidth + 6f, r.height), modeStr, modeStyle, c);
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
                string name = b.Id;
                if (!string.IsNullOrEmpty(b.Persona)) name = Game.PersonaName(b.Id) ?? b.Id;
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

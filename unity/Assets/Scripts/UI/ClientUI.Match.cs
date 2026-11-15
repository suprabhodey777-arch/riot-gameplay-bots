// The main menu (the wordmark over the orbiting map, the mode list, the
// streamer switch, the session card), the match setup (map, agent, the persona
// enemy team), the buy menu (full screen between rounds, an overlay during the
// round's buy phase), the round result and side-swap banners and the match end
// screen. GUI.* calls only; every card and button takes the mouse and the
// keyboard focus (arrows / Tab, Enter). The in-round HUD parts are in
// ClientUI.Hud.cs.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        GUIStyle hero, kicker, menuTitle, menuDesc, cardTitle, cardSmall, priceStyle, statLabel, bigNumRight, timerStyle,
            roundChip, ammoBig, ammoSmall, killName, dmgStyle, dmgHead, scoreNum, centerSmall, centerBody, rightSmall, menuNum, menuDescLine;
        Texture2D fadeLeft, fadeDown;

        void EnsureMatchStyles()
        {
            GUIStyle lbl = GUI.skin.label;
            hero = Text(lbl, 64, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            // Lbl tints with GUI.contentColor, so these styles are white and take their colour from the call.
            kicker = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            menuTitle = Text(lbl, 28, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            menuDesc = Text(lbl, 14, FontStyle.Normal, Color.white, TextAnchor.UpperLeft, true);
            // A menu card's one-line subtitle: never wraps (a wrapped second line would fall out of the card).
            menuDescLine = Text(lbl, 14, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft, false);
            menuNum = Text(lbl, 14, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            cardTitle = Text(lbl, 17, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            cardSmall = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            priceStyle = Text(lbl, 16, FontStyle.Bold, Color.white, TextAnchor.UpperRight, false);
            statLabel = Text(lbl, 11, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            bigNumRight = Text(lbl, 40, FontStyle.Bold, Color.white, TextAnchor.MiddleRight, false);
            timerStyle = Text(lbl, 30, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            roundChip = Text(lbl, 20, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            ammoBig = Text(lbl, 50, FontStyle.Bold, Color.white, TextAnchor.LowerRight, false);
            ammoSmall = Text(lbl, 18, FontStyle.Bold, Color.white, TextAnchor.LowerLeft, false);
            killName = Text(lbl, 15, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            dmgStyle = Text(lbl, 18, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            dmgHead = Text(lbl, 24, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            scoreNum = Text(lbl, 30, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            centerSmall = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            centerBody = Text(lbl, 16, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter, true);
            rightSmall = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleRight, false);
            EnsureBroadcastStyles();
            EnsureHudStyles();
            EnsureStreamerStyles();
            if (fadeLeft == null)
            {
                fadeLeft = Own(Gradient(256, 1, true));
                fadeDown = Own(Gradient(1, 256, false));
                // Warm the generated textures now, so the first buy menu or scope does not hitch.
                foreach (string id in Weapons.Order) GunModels.Icon(id);
                Textures.ScopeMask();
            }
        }

        /// <summary>
        /// A black fade, eased: horizontal is opaque at the left and clear at the right; vertical is clear at the
        /// top and opaque at the bottom (texture row 0 is the bottom of the rect GUI.DrawTexture fills).
        /// </summary>
        static Texture2D Gradient(int w, int h, bool horizontal)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float k = horizontal ? x / (float)(w - 1) : y / (float)(h - 1);
                float a = (1f - k);
                a = a * a * (3f - 2f * a);
                px[y * w + x] = new Color32(4, 6, 12, (byte)Mathf.RoundToInt(a * 245f));
            }
            t.SetPixels32(px);
            t.Apply(false);
            return t;
        }

        void Lbl(Rect r, string s, GUIStyle st, Color c)
        {
            if (string.IsNullOrEmpty(s)) return;
            Color old = GUI.contentColor;
            GUI.contentColor = c;
            TextAt(r, s, st);
            GUI.contentColor = old;
        }

        static void Tex(Rect r, Texture t, Color c)
        {
            if (t == null || Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            GUI.DrawTexture(r, t, ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        /// <summary>A selected card: card fill with a bright outline, an inner line and a top bar.</summary>
        void Outline(Rect r, Color c, float w)
        {
            Shadow(r, 16f, 0.5f);
            Plate(r, Theme.CardC);
            PlateLine(r, c);
            if (w > 1.5f) PlateLine(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), new Color(c.r, c.g, c.b, 0.5f));
            Fill(new Rect(r.x + Theme.Cut, r.y, r.width - Theme.Cut - 4f, 3f), c);
        }

        bool Hover(Rect r)
        {
            return r.Contains(Event.current.mousePosition);
        }

        static Color RoundTypeColor(string t)
        {
            switch ((t ?? "").ToLowerInvariant().Replace("_", "").Replace(" ", "").Replace("-", ""))
            {
                case "pistol": return Palette.Warn;
                case "eco": return new Color(0.6f, 0.64f, 0.7f);
                case "force": return new Color(1f, 0.5f, 0.25f);
                case "full": case "fullbuy": return Palette.Ok;
                default: return Settings.Accent;
            }
        }

        // ---- main menu -----------------------------------------------------------------------------
        static readonly string[] MenuTitles = { "Play match", "Practice", "Rehearse", "Favourite streamer", "Settings" };
        static readonly string[] MenuNums = { "01", "02", "03", "04", "05" };
        static readonly string[] MenuIcons = { "play", "target", "rewind", "persona", "gear" };
        // One line each: the card is sized to the widest of them (DrawMenu).
        static readonly string[] MenuDescs =
        {
            "You and four bot teammates against five bots. Buy rounds, first to 7.",
            "The coach reads a player's matches and drills their weakest moment.",
            "Replay the exact moments a sample player lost, on the same clock.",
            "Play your favourite streamer's style: the flick duelist, the anchor, the lurker.",
            "Mouse, crosshair, graphics, map, streamer mode, colours and names.",
        };

        string MenuDesc(int i)
        {
            return i == 0 && !Game.V3 ? "Needs a bridge that speaks bots-wire.3 (weapons and economy)."
                : i == 3 && !Game.V4 ? "Needs a bridge that speaks bots-wire.4 to play the personas."
                : MenuDescs[i];
        }

        void DrawMenu()
        {
            float now = Time.unscaledTime;
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(0f, 0f, Mathf.Min(W, 1000f), H), fadeLeft, ScaleMode.StretchToFill, true);
                GUI.DrawTexture(new Rect(0f, H - 260f, W, 260f), fadeDown, ScaleMode.StretchToFill, true);
            }
            float k = Intro01;
            float x = 80f, y = Mathf.Max(56f, H * 0.09f) + (1f - k) * 14f;
            Color ac = Settings.Accent;

            // The wordmark, an accent rule and the tagline.
            Kicker(new Rect(x, y, 700f, 18f), "Season 01  //  Tactical training", ac);
            GUIStyle word = W > 1150f ? displayXL : displayL;
            float wh = W > 1150f ? 112f : 70f;
            Condensed(new Rect(x - 6f, y + 18f, Mathf.Min(980f, W - x - 20f), wh), Spaced("Saltworks"), word, Theme.Ink, 0.78f);
            float ry = y + 18f + wh + 4f;
            Neon(new Rect(x, ry, 160f, 3f), ac, Mathf.Clamp01((now - screenAt) / 0.6f));
            Lbl(new Rect(x, ry + 16f, 600f, 44f), "Find what you lose, practise it against bots that play like people, then take it into a full match.", subStyle, Theme.Muted);

            // The modes: one card each, staggered in.
            float by = ry + 80f;
            bool idle = Idle;
            // Every subtitle stays on one line: the cards are as wide as the widest one needs (desc from x+72 to xMax-48).
            float descMax = 0f;
            for (int i = 0; i < MenuTitles.Length; i++) descMax = Mathf.Max(descMax, TextW(MenuDesc(i), menuDescLine));
            float slatW = Mathf.Clamp(descMax + 72f + 48f + 8f, 560f, Mathf.Min(720f, W - x - 460f));
            const float slatH = 76f, slatGap = 10f;
            for (int i = 0; i < MenuTitles.Length; i++)
            {
                float ik = Theme.Ease((now - screenAt - 0.06f - i * 0.04f) / 0.3f);
                if (ik <= 0f) continue;
                bool enabled = i == 4 || (idle && (i != 0 || Game.V3));
                var r = new Rect(x - (1f - ik) * 24f, by + i * (slatH + slatGap), slatW, slatH);
                int key = 1000 + i;
                // The hover covers the resting slot and the lifted card, so the click rect never misses what is drawn.
                float h = CardHover(new Rect(r.x - 8f, r.y, r.width + 8f, r.height), key, enabled);
                Color oldC = GUI.color;
                GUI.color = new Color(oldC.r, oldC.g, oldC.b, oldC.a * ik);
                var b = new Rect(r.x + h * 8f, r.y, r.width, r.height);
                Shadow(b, 14f, 0.4f);
                Plate(b, Color.Lerp(new Color(0.035f, 0.045f, 0.07f, 0.84f), new Color(ac.r * 0.2f, ac.g * 0.2f, ac.b * 0.2f, 0.96f), h));
                PlateLine(b, Color.Lerp(new Color(1f, 1f, 1f, 0.07f), new Color(ac.r, ac.g, ac.b, 0.75f), h));
                Fill(new Rect(b.x + 2f, b.y + 12f, 3f, b.height - 24f), enabled ? ac : Theme.Faint);
                Color tc = enabled ? Theme.Ink : Theme.Faint;
                Tex(new Rect(b.x + 22f, b.y + 22f, 32f, 32f), UiTex.Glyph(MenuIcons[i]), enabled ? Color.Lerp(Theme.Muted, ac, 0.3f + 0.7f * h) : Theme.Faint);
                Lbl(new Rect(b.x + 72f, b.y + 8f, 30f, 30f), MenuNums[i], menuNum, enabled ? ac : Theme.Faint);
                Condensed(new Rect(b.x + 102f, b.y + 6f, b.width - 160f, 34f), Upper(MenuTitles[i]), menuTitle, tc, 0.86f);
                string desc = MenuDesc(i);
                var dr = new Rect(b.x + 72f, b.y + 42f, b.width - 120f, 24f);
                Color dc = enabled ? Theme.Muted : Theme.Faint;
                float dw = TextW(desc, menuDescLine);
                // A subtitle still too wide for the narrowest window is condensed rather than cut.
                if (dw > dr.width) Condensed(dr, desc, menuDescLine, dc, Mathf.Max(0.75f, dr.width / dw));
                else Lbl(dr, desc, menuDescLine, dc);
                Lbl(new Rect(b.xMax - 40f, b.y, 28f, b.height), "\u203a", menuTitle, new Color(ac.r, ac.g, ac.b, 0.25f + 0.75f * h));
                GUI.color = oldC;
                if (CardHit(b, key, enabled))
                {
                    switch (i)
                    {
                        case 0: Game.Do(Game.OpenMatchSetup); break;
                        case 1: Game.Do(Game.OpenPractice); break;
                        case 2: Game.Do(Game.OpenRehearse); break;
                        case 3: Game.Do(Game.OpenPersonas); break;
                        default: Game.Do(Game.OpenSettings); break;
                    }
                }
            }

            // Top right: streamer mode switch, the LIVE pill, About.
            DrawStreamerSwitch(new Rect(W - 432f, 24f, 248f, 40f));
            if (GButton(new Rect(W - 172f, 24f, 148f, 40f), "About", 2)) Game.Do(() => { settingsTab = AboutTab; Game.OpenSettings(); });

            // Bottom right: the session card.
            var sr = new Rect(W - 424f, H - 192f, 392f, 152f);
            Card(sr, ac);
            float sy = sr.y + 18f;
            Lbl(new Rect(sr.x + 20f, sy, 340f, 18f), Spaced("Session"), tinyCaps, Theme.Muted);
            Plate(new Rect(sr.x + 20f, sy + 30f, 9f, 9f), Game.Connected ? Palette.Ok : Palette.Bad);
            Lbl(new Rect(sr.x + 36f, sy + 24f, 340f, 22f), Game.Connected ? "Online" : "Offline", smallMid, Game.Connected ? Palette.Ok : Palette.Bad);
            Lbl(new Rect(sr.x + 20f, sy + 58f, 160f, 18f), Spaced("Map"), tinyCaps, Theme.Muted);
            Lbl(new Rect(sr.x + 20f, sy + 78f, 170f, 26f), Upper(string.IsNullOrEmpty(Game.MapName) ? "?" : Game.MapName), cardTitle, Theme.Ink);
            Lbl(new Rect(sr.x + 200f, sy + 58f, 160f, 18f), Spaced("Agent"), tinyCaps, Theme.Muted);
            string ag = Game.PickedAgentId ?? Game.RememberedAgent;
            Lbl(new Rect(sr.x + 200f, sy + 78f, 170f, 26f), Upper(string.IsNullOrEmpty(ag) ? "not picked" : Agents.NameOf(ag)), cardTitle, string.IsNullOrEmpty(ag) ? Theme.Muted : Agents.ColorOf(ag));

            // Bottom left: the credit and the keys.
            DrawCredit(new Vector2(x, H - 48f));
            Lbl(new Rect(x + 270f, H - 48f, 560f, 20f), "Arrow keys and Enter work in every menu   \u00b7   Esc closes menus   \u00b7   F3 frame rate", smallMid, Theme.Faint);
        }

        /// <summary>The event credit with a small spinning star mark.</summary>
        void DrawCredit(Vector2 at)
        {
            Color ac = Settings.Accent;
            Matrix4x4 m = GUI.matrix;
            var star = new Rect(at.x, at.y, 20f, 20f);
            GUIUtility.RotateAroundPivot(Time.unscaledTime * 40f, star.center * scale);
            Tex(star, UiTex.Glyph("persona"), ac);
            GUI.matrix = m;
            Lbl(new Rect(at.x + 28f, at.y, 240f, 20f), Spaced("DevHacks 2026"), kickerStyle, Theme.Ink);
        }

        /// <summary>The streamer mode switch (menu): label, a sliding knob, and the LIVE pill when on.</summary>
        void DrawStreamerSwitch(Rect r)
        {
            bool on = Settings.StreamerMode;
            const int key = 4242;
            float h = CardHover(r, key, true);
            Plate(r, new Color(0.04f, 0.05f, 0.08f, 0.82f + 0.14f * h));
            PlateLine(r, new Color(1f, 1f, 1f, 0.1f + 0.25f * h));
            Lbl(new Rect(r.x + 16f, r.y, r.width - 84f, r.height), Spaced("Streamer mode"), kickerStyle, on ? Theme.Ink : Theme.Muted);
            float kk = Anim(key * 3 + 1, on, 9f);
            var track = new Rect(r.xMax - 62f, r.center.y - 11f, 46f, 22f);
            Plate(track, Color.Lerp(new Color(1f, 1f, 1f, 0.15f), Palette.Spike, kk));
            Plate(new Rect(Mathf.Lerp(track.x + 3f, track.xMax - 19f, kk), track.y + 3f, 16f, 16f), Color.white);
            if (on) DrawLivePill(new Vector2(r.x - 86f, r.y + 7f), false);
            if (CardHit(r, key, true)) Game.Do(() => Game.SetStreamer(!Settings.StreamerMode));
        }

        // ---- match setup ----------------------------------------------------------------------------
        void DrawMatchSetup()
        {
            Rect area = Centered(Mathf.Min(1180f, W - 40f), Mathf.Min(760f, H - 50f));
            Frame(area, "Match setup");
            Glass(area);
            float x = area.x + Pad, y = area.y + 26f, w = area.width - 2f * Pad;
            y = Header(x, y, w, null, "Play a match", "You and four bot teammates against five bots, first to 7 rounds, sides swap after round 6. Each round starts with a 20 s buy phase (B). A finished match is analysed like your drills.");
            bool idle = Idle;

            y = SectionLabel(x, y, w, "Map");
            List<MapInfo> shown = ShownMaps();
            float mw = Mathf.Min(360f, (w - 12f * (shown.Count - 1)) / Mathf.Max(1, shown.Count));
            for (int i = 0; i < shown.Count; i++)
            {
                MapInfo m = shown[i];
                var r = new Rect(x + i * (mw + 12f), y, mw, 84f);
                bool sel = m.id == Game.MatchMapId;
                int key = StableKey("match-map:" + m.id);
                float h = CardHover(r, key, true);
                if (sel) Outline(r, Settings.Accent, 2f);
                else Card(r, new Color(0f, 0f, 0f, 0f), h);
                Condensed(new Rect(r.x + 16f, r.y + 10f, r.width - 32f, 26f), Upper(m.name ?? m.id), cardTitleL, Theme.Ink, 0.9f);
                if (sel) Chip(r.xMax - 16f - TextW("SELECTED", chipStyle) - 18f, r.y + 12f, "SELECTED", Settings.Accent, Theme.OnAccent);
                Lbl(new Rect(r.x + 16f, r.y + 40f, r.width - 32f, 40f), MapBlurb(m.id), menuDesc, Theme.Muted);
                string id = m.id;
                if (CardHit(r, key, true)) Game.Do(() => Game.MatchMapId = id);
            }
            y += 84f + Theme.S5;

            y = SectionLabel(x, y, w, "Agent");
            int n = Agents.All.Count;
            float aw = (w - 12f * (n - 1)) / Mathf.Max(1, n);
            // The cards grow with the longest blurb instead of clipping it at 46 px.
            float blurbH = 0f;
            for (int i = 0; i < n; i++) blurbH = Mathf.Max(blurbH, TextH(Agents.All[i].Blurb ?? "", menuDesc, aw - 32f));
            float agentH = Mathf.Max(150f, 100f + blurbH + 8f);
            for (int i = 0; i < n; i++)
            {
                AgentInfo a = Agents.All[i];
                var r = new Rect(x + i * (aw + 12f), y, aw, agentH);
                bool sel = a.Id == Game.MatchAgentId;
                int key = StableKey("match-agent:" + a.Id);
                float h = CardHover(r, key, true);
                if (sel) Outline(r, a.Color, 2f);
                else Card(r, new Color(a.Color.r, a.Color.g, a.Color.b, 0.4f), h);
                Tex(new Rect(r.xMax - 42f, r.y + 16f, 26f, 26f), UiTex.Role(a.Role), new Color(a.Color.r, a.Color.g, a.Color.b, 0.85f));
                Condensed(new Rect(r.x + 16f, r.y + 12f, r.width - 64f, 34f), Upper(a.Name), displayM, a.Color, 0.84f);
                Lbl(new Rect(r.x + 16f, r.y + 46f, r.width - 32f, 18f), Spaced(a.Role ?? ""), tinyCaps, Theme.Muted);
                float ix = r.x + 16f;
                foreach (Ability ab in Agents.AllAbilities)
                {
                    int c = a.Kit != null ? a.Kit.Get(ab) : 0;
                    if (c <= 0) continue;
                    if (ix + 50f > r.xMax - 10f) break;
                    Icon(new Rect(ix, r.y + 70f, 22f, 22f), ab, Agents.AbilityColor(ab));
                    Lbl(new Rect(ix + 24f, r.y + 72f, 30f, 20f), "\u00d7" + c, cardSmall, Theme.Ink);
                    ix += 56f;
                }
                Lbl(new Rect(r.x + 16f, r.y + 100f, r.width - 32f, blurbH), a.Blurb ?? "", menuDesc, Theme.Muted);
                string id = a.Id;
                if (CardHit(r, key, true)) Game.Do(() => Game.MatchAgentId = id);
            }
            y += agentH + Theme.S5;

            // Enemy team: plain bots, or personas (bots-wire.4).
            y = SectionLabel(x, y, w, "Enemy team");
            bool v4 = Game.V4;
            if (GButton(new Rect(x, y, 150f, 44f), "Bots", 0, true, StableKey("enemy-bots"), !Game.PersonaEnemies)) Game.Do(() => Game.PersonaEnemies = false);
            if (GButton(new Rect(x + 162f, y, 200f, 44f), "Pro personas", 0, v4, StableKey("enemy-personas"), Game.PersonaEnemies)) Game.Do(() => Game.SetPersonaEnemies(true));
            if (!v4) Lbl(new Rect(x + 380f, y, w - 380f, 44f), "Persona teams need a bridge that speaks bots-wire.4.", smallMid, Theme.Muted);
            else if (Game.PersonaEnemies)
            {
                float px = x + 380f;
                float pw = Mathf.Min(150f, (area.xMax - Pad - px - 4f * 8f) / 5f);
                List<Persona> all = Personas.All;
                for (int i = 0; i < all.Count && i < 5; i++)
                {
                    Persona p = all[i];
                    var r = new Rect(px + i * (pw + 8f), y, pw, 44f);
                    bool inTeam = Game.EnemyPersonas.Contains(p.Id);
                    int key = StableKey("match-persona:" + p.Id);
                    float h = CardHover(r, key, true);
                    Plate(r, inTeam ? new Color(p.Primary.r * 0.3f, p.Primary.g * 0.3f, p.Primary.b * 0.3f, 0.95f) : Color.Lerp(Theme.CardC, Theme.ButtonHoverC, h));
                    PlateLine(r, inTeam ? p.Primary : new Color(1f, 1f, 1f, 0.08f + 0.3f * h));
                    Fill(new Rect(r.x + 2f, r.y + 10f, 3f, r.height - 20f), p.Primary);
                    FitCondensed(new Rect(r.x + 14f, r.y, r.width - 22f, r.height), Upper(p.Name), bodyBoldMid, inTeam ? Theme.Ink : Theme.Muted, 1f);
                    string pid = p.Id;
                    if (CardHit(r, key, true)) Game.Do(() => Game.ToggleEnemyPersona(pid));
                }
            }

            float byy = area.yMax - Pad - Theme.ButtonH;
            if (!Game.V3) Lbl(new Rect(x, byy - 30f, w, 22f), "This bridge does not speak bots-wire.3, so it has no match mode. Update the bridge, then reconnect.", bodyBold, Palette.Warn);
            if (Game.WaitingFor == "match") Lbl(new Rect(x, byy - 30f, w, 22f), "Starting the match...", bodyBold, Theme.Muted);
            if (GButton(new Rect(x, byy, 180f, Theme.ButtonH), "Back", 2, true, 0, false, UiTex.Glyph("back"))) Game.Do(Game.OpenMenu);
            bool canStart = idle && Game.V3 && !string.IsNullOrEmpty(Game.MatchAgentId) && (!Game.PersonaEnemies || Game.EnemyPersonas.Count > 0);
            if (GButton(new Rect(area.xMax - Pad - 320f, byy, 320f, Theme.ButtonH), Game.PersonaEnemies ? "Start persona match" : "Start match", 1, canStart, 0, false, UiTex.Glyph("play"), "Enter")) Game.Do(Game.StartMatch);
        }

        // ---- buy menu ---------------------------------------------------------------------------------
        static readonly string[][] BuyColumns =
        {
            new[] { "pin", "hammer" },
            new[] { "wasp", "brick" },
            new[] { "lancer", "viper" },
            new[] { "longshot" },
        };
        static readonly string[] BuyColumnNames = { "Sidearms", "SMG / Shotgun", "Rifles", "Sniper" };

        string buyTimerStr = "", creditStr = "", scoreStr = "", roundStr = "";
        int buyTimerKey = -1, creditKey = int.MinValue, scoreKey = int.MinValue, roundKey = int.MinValue;

        static string Credits(int c)
        {
            return c < 0 ? "?" : c.ToString("N0");
        }

        /// <summary>The buy menu. `overlay`: drawn over the round; otherwise the whole screen between rounds.</summary>
        void DrawBuy(bool overlay)
        {
            BuyPhaseMsg bp = Game.BuyPhase;
            Fill(new Rect(0f, 0f, W, H), new Color(0.01f, 0.015f, 0.03f, overlay ? 0.66f : 0.55f));
            float pw = Mathf.Min(1260f, W - 40f), ph = Mathf.Min(720f, H - 40f);
            var area = new Rect(Mathf.Round((W - pw) / 2f), Mathf.Round((H - ph) / 2f), pw, ph);
            Frame(area, overlay ? null : "Buy phase");
            Glass(area);
            float x = area.x + 24f, y = area.y + 22f;

            // Header: round type chip, round, score, timer, credits.
            string rt = Weapons.RoundTypeLabel(Game.RoundType);
            Color rc = RoundTypeColor(Game.RoundType);
            if (rt.Length > 0)
            {
                float cw = TextW(rt, roundChip) + 32f;
                var chip = new Rect(x, y, cw, 42f);
                Plate(chip, rc);
                Lbl(chip, rt, roundChip, Theme.OnAccent);
                x = chip.xMax + 16f;
            }
            int rk = Game.Round * 10 + (Game.MatchSide == "defense" ? 1 : 0);
            if (rk != roundKey)
            {
                roundKey = rk;
                roundStr = "ROUND " + Game.Round + (string.IsNullOrEmpty(Game.MatchSide) ? "" : "   " + Game.MatchSide.ToUpperInvariant());
            }
            Lbl(new Rect(x, y, 300f, 22f), roundStr, cardTitle, Theme.Ink);
            float adviceW = Mathf.Max(200f, area.center.x - 130f - x);
            string advice = Weapons.RoundTypeAdvice(Game.RoundType);
            float adviceH = Mathf.Min(46f, TextH(advice, small, adviceW));
            Lbl(new Rect(x, y + 22f, adviceW, adviceH), advice, small, Theme.Muted);

            ScoreInfo sc = Game.Score;
            int sk = sc == null ? -1 : sc.player * 100 + sc.enemy;
            if (sk != scoreKey)
            {
                scoreKey = sk;
                scoreStr = sc == null ? "0  :  0" : sc.player + "  :  " + sc.enemy;
            }
            Lbl(new Rect(area.center.x - 100f, y - 4f, 200f, 36f), scoreStr, scoreNum, Theme.Ink);
            Lbl(new Rect(area.center.x - 140f, y + 30f, 280f, 16f), Game.InDuel ? "YOU  \u00b7  FIRST TO 5  \u00b7  " + Upper(Game.DuelPersonaName) : "YOU  \u00b7  FIRST TO 7  \u00b7  THEM", centerSmall, Theme.Muted);

            float left = Game.BuyTimeLeft;
            int tk = Mathf.CeilToInt(left);
            if (tk != buyTimerKey)
            {
                buyTimerKey = tk;
                buyTimerStr = tk + " s";
            }
            var timerR = new Rect(area.xMax - 24f - 390f, y, 116f, 44f);
            Plate(timerR, new Color(1f, 1f, 1f, 0.06f));
            Lbl(timerR, Game.Readied ? "READY" : buyTimerStr, timerStyle, left < 5f && !Game.Readied ? Palette.Warn : Theme.Ink);
            float total = bp != null && bp.timeS > 0f ? bp.timeS : 20f;
            Fill(new Rect(timerR.x + 8f, timerR.yMax - 3f, (timerR.width - 16f) * Mathf.Clamp01(left / total), 2f), Settings.Accent);
            int ck = Game.Credits;
            if (ck != creditKey)
            {
                creditKey = ck;
                creditStr = Credits(ck);
            }
            Lbl(new Rect(area.xMax - 24f - 250f, y - 4f, 250f, 16f), Spaced("Credits"), rightSmall, Theme.Muted);
            Lbl(new Rect(area.xMax - 24f - 250f, y + 8f, 250f, 40f), creditStr, bigNumRight, Settings.Accent);

            float top = area.y + 90f;
            Fill(new Rect(area.x + 24f, top - 12f, area.width - 48f, 1f), Theme.Line);

            float teamW = Mathf.Min(300f, area.width * 0.26f);
            float gridW = area.width - 48f - teamW - 20f;
            float colW = (gridW - 3f * 12f) / 4f;
            float cardH = Mathf.Min(176f, (area.height - 90f - 160f - 40f) / 2f);
            for (int c = 0; c < BuyColumns.Length; c++)
            {
                float cx = area.x + 24f + c * (colW + 12f);
                Kicker(new Rect(cx, top, colW, 18f), BuyColumnNames[c], Theme.Muted);
                for (int i = 0; i < BuyColumns[c].Length; i++)
                {
                    WeaponInfo wi = Weapons.Find(BuyColumns[c][i]);
                    if (wi == null) continue;
                    WeaponCard(new Rect(cx, top + 24f + i * (cardH + 10f), colW, cardH), wi);
                }
            }

            float ay = top + 24f + 2f * (cardH + 10f) + 4f;
            Kicker(new Rect(area.x + 24f, ay, 200f, 18f), "Armour", Theme.Muted);
            float armW = (gridW - 12f) / 2f;
            ArmorCard(new Rect(area.x + 24f, ay + 22f, armW, 74f), "light");
            ArmorCard(new Rect(area.x + 24f + armW + 12f, ay + 22f, armW, 74f), "heavy");

            var tr = new Rect(area.xMax - 24f - teamW, top, teamW, area.yMax - 24f - top - 64f);
            Card(tr, new Color(0f, 0f, 0f, 0f));
            Lbl(new Rect(tr.x + 16f, tr.y + 12f, tr.width - 32f, 18f), Spaced("Team credits"), tinyCaps, Theme.Muted);
            float ty = tr.y + 40f;
            if (bp?.team != null && bp.team.Count > 0)
            {
                foreach (TeamMember m in bp.team)
                {
                    if (m == null) continue;
                    if (ty > tr.yMax - 44f) break;
                    bool me = m.id == Game.PlayerId;
                    Lbl(new Rect(tr.x + 16f, ty, tr.width - 120f, 22f), me ? "You" : Upper(Game.DisplayName(m.id)), bodyBoldMid, me ? Settings.Accent : Settings.Ally);
                    Lbl(new Rect(tr.xMax - 106f, ty, 90f, 22f), Credits(m.credits), priceStyle, Theme.Ink);
                    string prim = m.loadout?.primary;
                    if (!string.IsNullOrEmpty(prim)) Tex(new Rect(tr.x + 16f, ty + 22f, 60f, 20f), GunModels.Icon(prim), new Color(1f, 1f, 1f, 0.75f));
                    string arm = m.loadout?.ArmorKind;
                    Lbl(new Rect(tr.x + 82f, ty + 24f, tr.width - 98f, 18f), (string.IsNullOrEmpty(prim) ? "no primary" : Weapons.NameOf(prim)) + (string.IsNullOrEmpty(arm) ? "" : "  \u00b7  " + arm + " armour"), cardSmall, Theme.Muted);
                    ty += 50f;
                }
            }
            else Lbl(new Rect(tr.x + 16f, ty, tr.width - 32f, 60f), "The bridge sends the team's credits with the buy phase.", menuDesc, Theme.Muted);
            int avg = Game.TeamAverageCredits();
            if (avg >= 0) Lbl(new Rect(tr.x + 16f, tr.yMax - 28f, tr.width - 32f, 18f), "Team average " + Credits(avg), cardSmall, Theme.Muted);

            float fy = area.yMax - 24f - Theme.ButtonH;
            Lbl(new Rect(area.x + 24f, fy, area.width - teamW - 200f, Theme.ButtonH),
                (overlay ? "B closes. " : "") + "Buy, then Ready up. The round starts at 0:00.", smallMid, Theme.Muted);
            if (GButton(new Rect(area.xMax - 24f - teamW, fy, teamW, Theme.ButtonH), Game.Readied ? "Ready" : "Ready up", 1, Game.BuyPhaseActive, 0, false, null, "Enter")) Game.Do(Game.ReadyUp);
            if (overlay && GButton(new Rect(area.xMax - 24f - teamW - 142f, fy, 130f, Theme.ButtonH), "Close", 2, true, 0, false, null, "B")) Game.Do(Game.CloseBuyMenu);
            if ((!overlay && Game.Readied) || (!overlay && !Game.BuyPhaseActive))
                Lbl(new Rect(area.x + 24f, fy - 24f, 500f, 20f), "Waiting for the round to start...", smallMid, Color.white);
        }

        void StatBar(Rect r, string label, float v, Color c)
        {
            Lbl(new Rect(r.x, r.y, 44f, r.height), label, statLabel, Theme.Muted);
            var bar = new Rect(r.x + 44f, r.y + r.height / 2f - 2f, r.width - 44f, 4f);
            // Segmented: ten cells, lit up to the value.
            float cw = (bar.width - 9f * 2f) / 10f;
            for (int i = 0; i < 10; i++)
            {
                float lit = Mathf.Clamp01(v * 10f - i);
                var cell = new Rect(bar.x + i * (cw + 2f), bar.y, cw, bar.height);
                Fill(cell, new Color(1f, 1f, 1f, 0.08f));
                if (lit > 0f) Fill(new Rect(cell.x, cell.y, cell.width * lit, cell.height), c);
            }
        }

        void WeaponCard(Rect r, WeaponInfo w)
        {
            bool owned = Game.Owns(w.Id);
            bool afford = Game.CanAfford(w.Id);
            bool active = Game.BuyPhaseActive;
            bool clickable = active && (afford || (owned && w.Price > 0));
            int key = StableKey("buy:" + w.Id);
            float h = CardHover(r, key, clickable);
            float a = owned || afford ? 1f : 0.45f;
            if (owned) Outline(r, Settings.Accent, 2f);
            else
            {
                Card(r, new Color(0f, 0f, 0f, 0f), h);
                PlateLine(r, new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, 0.08f + 0.6f * h));
            }
            Color oldC = GUI.color;
            GUI.color = new Color(oldC.r, oldC.g, oldC.b, oldC.a * a);
            Condensed(new Rect(r.x + 12f, r.y + 8f, r.width - 100f, 24f), Upper(w.Name), cardTitle, Theme.Ink, 0.9f);
            Lbl(new Rect(r.x + 12f, r.y + 30f, r.width - 24f, 16f), Spaced(w.ClassName), tinyCaps, Theme.Muted);
            string price = w.Price <= 0 ? "FREE" : Credits(w.Price);
            Lbl(new Rect(r.xMax - 90f, r.y + 10f, 78f, 22f), price, priceStyle, afford || owned ? Theme.Ink : Palette.Bad);
            float iconH = Mathf.Clamp(r.height - 116f, 26f, 56f);
            Tex(new Rect(r.x + 12f + h * 4f, r.y + 48f, r.width - 24f, iconH), GunModels.Icon(w.Id), owned ? Settings.Accent : Color.white);
            float sy = r.y + 52f + iconH;
            float sw = r.width - 24f;
            Color bc = owned ? Settings.Accent : Theme.Lighten(Settings.Accent, 0.2f);
            StatBar(new Rect(r.x + 12f, sy, sw, 13f), "DMG", w.BodyPerShot / 150f, bc);
            StatBar(new Rect(r.x + 12f, sy + 15f, sw, 13f), "RATE", (1f / Mathf.Max(0.05f, w.FireIntervalS)) / 13.4f, bc);
            StatBar(new Rect(r.x + 12f, sy + 30f, sw, 13f), "MOB", w.Mobility, bc);
            if (owned) Lbl(new Rect(r.x + 12f, r.yMax - 20f, r.width - 24f, 16f), w.Price > 0 && active ? "OWNED  \u00b7  CLICK TO SELL" : "OWNED", cardSmall, Settings.Accent);
            else if (!afford) Lbl(new Rect(r.x + 12f, r.yMax - 20f, r.width - 24f, 16f), "NOT ENOUGH CREDITS", cardSmall, Palette.Bad);
            GUI.color = oldC;
            string id = w.Id;
            if (CardHit(r, key, clickable))
            {
                if (owned && w.Price > 0) { PlayUi(SoundBank.Get("ui.cancel"), 0.5f); Game.Do(() => Game.SellItem(id)); }
                else if (!owned && afford) Game.Do(() => Game.BuyItem(id));
            }
        }

        void ArmorCard(Rect r, string kind)
        {
            bool owned = Game.Owns(kind);
            bool afford = Game.CanAfford(kind);
            bool active = Game.BuyPhaseActive;
            bool clickable = active && (afford || owned);
            int key = StableKey("buy:" + kind);
            float h = CardHover(r, key, clickable);
            if (owned) Outline(r, Settings.Accent, 2f);
            else
            {
                Card(r, new Color(0f, 0f, 0f, 0f), h);
                PlateLine(r, new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, 0.08f + 0.6f * h));
            }
            float a = owned || afford ? 1f : 0.45f;
            Color oldC = GUI.color;
            GUI.color = new Color(oldC.r, oldC.g, oldC.b, oldC.a * a);
            Tex(new Rect(r.x + 14f, r.y + 14f, 44f, 46f), UiTex.Glyph("sentinel"), owned ? Settings.Accent : new Color(0.75f, 0.85f, 1f));
            Condensed(new Rect(r.x + 68f, r.y + 10f, r.width - 170f, 26f), Upper(Weapons.ArmorName(kind)), cardTitle, Theme.Ink, 0.9f);
            Lbl(new Rect(r.x + 68f, r.y + 36f, r.width - 170f, 34f), Weapons.ArmorPointsOf(kind) + " points, absorbs 66% of each hit" + (owned ? (active ? "   OWNED \u00b7 CLICK TO SELL" : "   OWNED") : ""), menuDesc, owned ? Settings.Accent : Theme.Muted);
            Lbl(new Rect(r.xMax - 96f, r.y + 12f, 82f, 22f), Credits(Weapons.ArmorPrice(kind)), priceStyle, afford || owned ? Theme.Ink : Palette.Bad);
            GUI.color = oldC;
            string id = kind;
            if (CardHit(r, key, clickable))
            {
                if (owned) { PlayUi(SoundBank.Get("ui.cancel"), 0.5f); Game.Do(() => Game.SellItem(id)); }
                else if (afford) Game.Do(() => Game.BuyItem(id));
            }
        }

        // ---- banners ----------------------------------------------------------------------------------
        string resultSub = "";
        RoundResultMsg resultFor;

        void DrawRoundBanners(float now)
        {
            RoundResultMsg rr = Game.RoundResult;
            if (rr != null && now - Game.RoundResultAt < 4.5f)
            {
                bool won = Game.RoundWon(rr);
                float age = now - Game.RoundResultAt;
                float k = Theme.Ease(age / 0.3f) * Mathf.Clamp01((4.5f - age) / 0.4f);
                if (!ReferenceEquals(resultFor, rr))
                {
                    resultFor = rr;
                    string how = string.IsNullOrEmpty(rr.how) ? "" : rr.how.ToUpperInvariant();
                    string earned = rr.creditsEarned > 0 ? "+" + rr.creditsEarned.ToString("N0") + " credits" : "";
                    string mvp = string.IsNullOrEmpty(rr.mvp) ? "" : "MVP  " + Game.DisplayName(rr.mvp);
                    string score = rr.score != null ? rr.score.player + " : " + rr.score.enemy : "";
                    var parts = new List<string>();
                    foreach (string s in new[] { how, score, earned, mvp }) if (s.Length > 0) parts.Add(s);
                    resultSub = string.Join("     ", parts);
                }
                Color c = won ? Palette.Ok : Palette.Bad;
                var br = new Rect(0f, H * 0.26f + (1f - k) * 16f, W, 120f);
                Fill(br, new Color(0.02f, 0.03f, 0.05f, 0.86f * k));
                Fill(new Rect(W / 2f - W * 0.32f * k, br.y - 4f, W * 0.64f * k, 3f), c);
                Fill(new Rect(W / 2f - W * 0.32f * k, br.yMax + 1f, W * 0.64f * k, 3f), c);
                c.a = k;
                Condensed(new Rect(0f, br.y + 10f, W, 64f), won ? "ROUND WON" : "ROUND LOST", displayLC, c, 0.82f, true);
                Shadowed(new Rect(40f, br.y + 78f, W - 80f, 30f), resultSub, bannerSub, new Color(1f, 1f, 1f, k));
            }
            float sw = now - Game.SideSwapAt;
            if (sw >= 0f && sw < 4f)
            {
                float k = Mathf.Clamp01(sw / 0.3f) * Mathf.Clamp01((4f - sw) / 0.4f);
                var r = new Rect(W / 2f - 270f, H * 0.18f, 540f, 62f);
                Plate(r, new Color(0.03f, 0.04f, 0.07f, 0.9f * k));
                PlateLine(r, new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, k));
                Condensed(new Rect(r.x, r.y + 4f, r.width, 32f), Spaced("Sides swap"), hudCenter, new Color(1f, 1f, 1f, k), 0.9f, true);
                Shadowed(new Rect(r.x, r.y + 34f, r.width, 20f), Upper("You now play " + (Game.MatchSide ?? "the other side")), centerSmall, new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, k));
            }
            DrawRoundSweep(now);
        }

        // ---- match end --------------------------------------------------------------------------------
        void DrawMatchEnd()
        {
            MatchEndMsg me = Game.MatchEnd;
            Fill(new Rect(0f, 0f, W, H), new Color(0.01f, 0.015f, 0.03f, 0.6f));
            Rect area = Centered(860f, 600f);
            bool won = me != null && me.won;
            Color c = won ? Palette.Ok : Palette.Bad;
            Frame(area, won ? "Match won" : "Match lost");
            Glass(area);
            float k = Intro01;
            Fill(new Rect(area.x + Theme.Cut, area.y, (area.width - Theme.Cut * 2f) * k, 3f), c);
            Condensed(new Rect(area.x, area.y + 24f, area.width, 90f), won ? Spaced("Victory") : Spaced("Defeat"), displayLC, c, 0.82f, true);
            ScoreInfo sc = me?.score ?? Game.Score;
            Lbl(new Rect(area.x, area.y + 112f, area.width, 40f), sc != null ? sc.player + "  :  " + sc.enemy : "", scoreNum, Theme.Ink);
            Lbl(new Rect(area.x, area.y + 150f, area.width, 18f), "ROUNDS   YOU  :  THEM", centerSmall, Theme.Muted);

            int nh = Game.RoundHistory.Count;
            float pw = 18f, pg = 6f;
            float px0 = area.center.x - (nh * pw + Mathf.Max(0, nh - 1) * pg) / 2f;
            for (int i = 0; i < nh; i++)
            {
                float pk = Mathf.Clamp01((Time.unscaledTime - screenAt - 0.3f - i * 0.05f) / 0.2f);
                Color rc = Game.RoundHistory[i] ? Palette.Ok : Palette.Bad;
                Fill(new Rect(px0 + i * (pw + pg), area.y + 178f + (1f - pk) * 6f, pw, 8f), new Color(rc.r, rc.g, rc.b, pk));
            }

            MatchStats st = me?.stats;
            int kills = st != null ? st.kills : Game.MatchKills;
            int deaths = st != null ? st.deaths : Game.MatchDeaths;
            float hs = st != null ? st.headshotPct : (Game.MatchKills > 0 ? Game.MatchHeadKills / (float)Game.MatchKills : 0f);
            if (hs > 1f) hs /= 100f;
            string[] labels = { "Kills", "Deaths", "K / D", "Headshot kills" };
            string[] values = { kills.ToString(), deaths.ToString(), (deaths > 0 ? kills / (float)deaths : kills).ToString("0.00"), Mathf.RoundToInt(hs * 100f) + "%" };
            float cw = (area.width - 2f * Pad - 36f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(area.x + Pad + i * (cw + 12f), area.y + 210f, cw, 104f);
                Card(r, i == 0 ? Settings.Accent : new Color(1f, 1f, 1f, 0.15f));
                Condensed(new Rect(r.x, r.y + 14f, r.width, 50f), values[i], displayMC, Theme.Ink, 0.9f);
                Lbl(new Rect(r.x, r.y + 70f, r.width, 18f), Spaced(labels[i]), centerSmall, Theme.Muted);
            }
            Lbl(new Rect(area.x + 40f, area.y + 334f, area.width - 80f, 60f),
                "The match is recorded like your drills and re-analysed, so what happened here feeds your weakness report.", centerBody, Theme.Muted);
            float by = area.yMax - Pad - Theme.ButtonH;
            if (GButton(new Rect(area.x + Pad, by, 220f, Theme.ButtonH), "Main menu", 2, true, 0, false, UiTex.Glyph("back"))) Game.Do(Game.BackFromMatchEnd);
            if (GButton(new Rect(area.xMax - Pad - 280f, by, 280f, Theme.ButtonH), "Play again", 1, Idle && Game.V3, 0, false, UiTex.Glyph("play"), "Enter")) Game.Do(Game.OpenMatchSetup);
        }
    }
}

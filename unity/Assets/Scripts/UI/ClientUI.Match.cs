// Round 4 and 5 screens in the broadcast look: the main menu (animated hero over
// the orbiting map, streamer toggle, event credit), the match setup (with
// the persona enemy team), the buy menu (full screen between rounds, an overlay
// during the round's buy phase), the round result and side-swap banners and the
// match end screen. The buy menu and the banners use only GUI.* calls (no
// GUILayout), because IMGUI's layout pass is off while playing. The in-round
// HUD parts are in ClientUI.Hud.cs.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        GUIStyle hero, kicker, menuTitle, menuDesc, cardTitle, cardSmall, priceStyle, statLabel, bigNumRight, timerStyle,
            roundChip, ammoBig, ammoSmall, killName, dmgStyle, dmgHead, scoreNum, centerSmall, centerBody, rightSmall, menuNum;
        Texture2D fadeLeft, fadeDown;

        void EnsureMatchStyles()
        {
            GUIStyle lbl = GUI.skin.label;
            hero = Text(lbl, 64, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            // Lbl tints with GUI.contentColor, so these styles are white and take their colour from the call.
            kicker = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            menuTitle = Text(lbl, 26, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            menuDesc = Text(lbl, 13, FontStyle.Normal, Color.white, TextAnchor.UpperLeft, true);
            menuNum = Text(lbl, 13, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            cardTitle = Text(lbl, 16, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            cardSmall = Text(lbl, 11, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            priceStyle = Text(lbl, 15, FontStyle.Bold, Color.white, TextAnchor.UpperRight, false);
            statLabel = Text(lbl, 10, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            bigNumRight = Text(lbl, 40, FontStyle.Bold, Color.white, TextAnchor.MiddleRight, false);
            timerStyle = Text(lbl, 30, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            roundChip = Text(lbl, 20, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            ammoBig = Text(lbl, 50, FontStyle.Bold, Color.white, TextAnchor.LowerRight, false);
            ammoSmall = Text(lbl, 18, FontStyle.Bold, Color.white, TextAnchor.LowerLeft, false);
            killName = Text(lbl, 14, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            dmgStyle = Text(lbl, 18, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            dmgHead = Text(lbl, 24, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            scoreNum = Text(lbl, 30, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            centerSmall = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            centerBody = Text(lbl, 15, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter, true);
            rightSmall = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleRight, false);
            EnsureBroadcastStyles();
            EnsureHudStyles();
            if (fadeLeft == null)
            {
                fadeLeft = Own(Gradient(256, 1, true));
                fadeDown = Own(Gradient(1, 256, false));
                // Warm the generated textures now, so the first buy menu or scope does not hitch.
                foreach (string id in Weapons.Order) GunModels.Icon(id);
                Textures.ScopeMask();
            }
        }

        /// <summary>A black fade: opaque at the start (left or top) to clear, eased.</summary>
        static Texture2D Gradient(int w, int h, bool horizontal)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float k = horizontal ? x / (float)(w - 1) : 1f - y / (float)(h - 1);
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
            Color old = GUI.contentColor;
            GUI.contentColor = c;
            GUI.Label(r, s, st);
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

        /// <summary>A selected card: angular card fill with a bright outline and an inner glow line.</summary>
        void Outline(Rect r, Color c, float w)
        {
            Plate(r, Theme.CardC);
            PlateLine(r, c);
            if (w > 1.5f) PlateLine(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), new Color(c.r, c.g, c.b, 0.5f));
            Fill(new Rect(r.x + Theme.Cut, r.y, r.width - Theme.Cut - 4f, 2f), c);
        }

        bool Hover(Rect r)
        {
            return r.Contains(Event.current.mousePosition);
        }

        static Color RoundTypeColor(string t)
        {
            switch ((t ?? "").ToLowerInvariant().Replace("_", "").Replace(" ", "").Replace("-", ""))
            {
                case "pistol": return new Color(0.98f, 0.72f, 0.22f);
                case "eco": return new Color(0.6f, 0.64f, 0.7f);
                case "force": return new Color(1f, 0.5f, 0.25f);
                case "full": case "fullbuy": return new Color(0.3f, 0.85f, 0.5f);
                default: return Settings.Accent;
            }
        }

        // ---- main menu -----------------------------------------------------------------------------
        static readonly string[] MenuTitles = { "Play match", "Practice", "Rehearse", "Pro personas", "Settings" };
        static readonly string[] MenuNums = { "01", "02", "03", "04", "05" };
        static readonly string[] MenuDescs =
        {
            "You and four bot teammates against five bots. Buy rounds, first to 7.",
            "The coach reads a player's matches and builds drills from the weakest moment.",
            "Replay the exact moments a sample player lost: same positions, same clock.",
            "Duel or face original pro playstyles: the flick duelist, the anchor, the lurker.",
            "Mouse, crosshair, graphics, streamer mode, colours and the key list.",
        };

        void DrawMenu()
        {
            float now = Time.unscaledTime;
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(0f, 0f, Mathf.Min(W, 980f), H), fadeLeft, ScaleMode.StretchToFill, true);
                GUI.DrawTexture(new Rect(0f, H - 260f, W, 260f), fadeDown, ScaleMode.StretchToFill, true);
            }
            float k = Intro01;
            float x = 76f, y = Mathf.Max(54f, H * 0.11f) + (1f - k) * 18f;
            Color ac = Settings.Accent;

            // Hero: kicker, wordmark (with a rare glitch), neon rule, tagline.
            Kicker(new Rect(x, y, 700f, 16f), "Season 01  //  Tactical training protocol", ac);
            GUIStyle word = W > 1150f ? displayXL : displayL;
            float wh = W > 1150f ? 112f : 70f;
            var wr = new Rect(x - 6f, y + 18f, Mathf.Min(980f, W - x - 20f), wh);
            string wordmark = Spaced("Saltworks");
            int gseed = (int)(now * 24f);
            bool glitch = Mathf.Repeat(now, 4.7f) < 0.14f || k < 0.6f;
            if (glitch)
            {
                Condensed(new Rect(wr.x + (Hash01(gseed) - 0.5f) * 14f, wr.y, wr.width, wr.height), wordmark, word, new Color(ac.r, ac.g, ac.b, 0.75f), 0.78f);
                Condensed(new Rect(wr.x - (Hash01(gseed + 9) - 0.5f) * 14f, wr.y + 2f, wr.width, wr.height), wordmark, word, new Color(Theme.Neon2.r, Theme.Neon2.g, Theme.Neon2.b, 0.6f), 0.78f);
            }
            Condensed(wr, wordmark, word, Theme.Ink, 0.78f);
            float ry = wr.yMax + 4f;
            Neon(new Rect(x, ry, 160f, 3f), ac, Mathf.Clamp01((now - screenAt) / 0.6f));
            Neon(new Rect(x + 170f, ry, 40f, 3f), Theme.Neon2, Mathf.Clamp01((now - screenAt - 0.2f) / 0.5f));
            Lbl(new Rect(x, ry + 14f, 560f, 40f), "Find what you lose, practise it against bots that play like people, then take it into a full match.", menuDesc, Theme.Muted);

            // Menu items: numbered angular slats that stagger in, slide on hover and tick.
            float by = ry + 70f;
            bool idle = Idle;
            for (int i = 0; i < MenuTitles.Length; i++)
            {
                float ik = Theme.Ease((now - screenAt - 0.08f - i * 0.05f) / 0.3f);
                if (ik <= 0f) continue;
                bool enabled = i == 4 || (idle && (i != 0 || Game.V3));
                var r = new Rect(x - (1f - ik) * 40f, by + i * 66f, 500f, 58f);
                float h = HoverAnim(1000 + i, enabled && Hover(r));
                Color oldC = GUI.color;
                GUI.color = new Color(oldC.r, oldC.g, oldC.b, oldC.a * ik);
                var body2 = new Rect(r.x + h * 12f, r.y, r.width, r.height);
                Plate(body2, Color.Lerp(new Color(0.04f, 0.05f, 0.08f, 0.72f), new Color(ac.r * 0.22f, ac.g * 0.22f, ac.b * 0.22f, 0.9f), h));
                PlateLine(body2, new Color(1f, 1f, 1f, 0.06f + 0.1f * h));
                Fill(new Rect(body2.x, body2.y + 8f, 3f + 3f * h, body2.height - 16f), enabled ? ac : Theme.Faint);
                if (h > 0.01f) Fill(new Rect(body2.x + 14f, body2.yMax - 2f, (body2.width - 28f) * Theme.Ease(h), 2f), ac);
                Color tc = enabled ? Theme.Ink : Theme.Faint;
                Lbl(new Rect(body2.x + 18f, body2.y + 6f, 30f, 28f), MenuNums[i], menuNum, enabled ? ac : Theme.Faint);
                Condensed(new Rect(body2.x + 48f, body2.y + 4f, body2.width - 90f, 30f), Upper(MenuTitles[i]), menuTitle, tc, 0.86f);
                string desc = i == 0 && !Game.V3 ? "Needs a bridge that speaks bots-wire.3 (weapons and economy)." : i == 3 && !Game.V4 ? "Needs a bridge that speaks bots-wire.4. The screens open; the bridge plays the personas." : MenuDescs[i];
                Lbl(new Rect(body2.x + 48f, body2.y + 33f, body2.width - 80f, 20f), desc, menuDesc, enabled ? Theme.Muted : Theme.Faint);
                if (h > 0.01f) Lbl(new Rect(body2.xMax - 34f, body2.y + 12f, 24f, 30f), "\u203a", menuTitle, new Color(ac.r, ac.g, ac.b, h));
                GUI.color = oldC;
                if (!enabled) continue;
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    PlayUi(UiSound.Confirm, 0.8f);
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
            DrawStreamerSwitch(new Rect(W - 400f, 22f, 230f, 36f));
            if (GButton(new Rect(W - 156f, 22f, 130f, 36f), "About", 2)) Game.Do(() => { settingsTab = AboutTab; Game.OpenSettings(); });

            // Bottom right: the session card.
            var sr = new Rect(W - 392f, H - 168f, 360f, 128f);
            Glass(sr);
            Brackets(new Rect(sr.x - 5f, sr.y - 5f, sr.width + 10f, sr.height + 10f), new Color(ac.r, ac.g, ac.b, 0.4f), 12f, 2f);
            float sy = sr.y + 14f;
            Kicker(new Rect(sr.x + 16f, sy, 320f, 14f), "Bridge", Theme.Muted);
            string proto = Game.V4 ? "bots-wire.4: personas, streamer overlay" : Game.V3 ? "bots-wire.3: matches, weapons, economy" : Game.V2 ? "bots-wire.2: drills, maps, agents" : "bots-wire.1: drills";
            Round(new Rect(sr.x + 18f, sy + 24f, 8f, 8f), Game.Connected ? Palette.Ok : Palette.Bad);
            Lbl(new Rect(sr.x + 32f, sy + 18f, 310f, 20f), (Game.Connected ? "Online  " : "Offline  ") + proto, small, Game.Connected ? Palette.Ok : Palette.Bad);
            Kicker(new Rect(sr.x + 16f, sy + 48f, 150f, 14f), "Map", Theme.Muted);
            Lbl(new Rect(sr.x + 18f, sy + 66f, 150f, 24f), Upper(string.IsNullOrEmpty(Game.MapName) ? "?" : Game.MapName), cardTitle, Theme.Ink);
            Kicker(new Rect(sr.x + 186f, sy + 48f, 150f, 14f), "Agent", Theme.Muted);
            string ag = Game.PickedAgentId ?? Game.RememberedAgent;
            Lbl(new Rect(sr.x + 188f, sy + 66f, 150f, 24f), Upper(string.IsNullOrEmpty(ag) ? "not picked" : Agents.NameOf(ag)), cardTitle, string.IsNullOrEmpty(ag) ? Theme.Muted : Agents.ColorOf(ag));

            // Bottom left: the credit.
            DrawCredit(new Vector2(x, H - 44f));
            Lbl(new Rect(x + 260f, H - 40f, 400f, 18f), "Esc closes menus   \u00b7   F3 frame rate", small, Theme.Faint);
        }

        /// <summary>The event credit with a small spinning star mark.</summary>
        void DrawCredit(Vector2 at)
        {
            Color ac = Settings.Accent;
            Matrix4x4 m = GUI.matrix;
            var star = new Rect(at.x, at.y, 18f, 18f);
            GUIUtility.RotateAroundPivot(Time.unscaledTime * 40f, star.center * scale);
            Tex(star, UiTex.Glyph("persona"), ac);
            GUI.matrix = m;
            Lbl(new Rect(at.x + 26f, at.y, 240f, 18f), Spaced("DevHacks 2026"), kickerStyle, Theme.Ink);
        }

        /// <summary>The streamer mode switch (menu and settings): label, a sliding knob, and the LIVE pill when on.</summary>
        void DrawStreamerSwitch(Rect r)
        {
            bool on = Settings.StreamerMode;
            float h = HoverAnim(4242, Hover(r));
            Plate(r, new Color(0.04f, 0.05f, 0.08f, 0.8f + 0.15f * h));
            PlateLine(r, new Color(1f, 1f, 1f, 0.08f + 0.2f * h));
            Lbl(new Rect(r.x + 14f, r.y, r.width - 80f, r.height), Spaced("Streamer mode"), kickerStyle, on ? Theme.Ink : Theme.Muted);
            var track = new Rect(r.xMax - 58f, r.center.y - 8f, 42f, 16f);
            Plate(track, on ? Palette.Spike : new Color(1f, 1f, 1f, 0.15f));
            float kx = on ? track.xMax - 15f : track.x + 1f;
            Fill(new Rect(kx, track.y + 2f, 14f, 12f), Color.white);
            if (on) DrawLivePill(new Vector2(r.x - 86f, r.y + 5f), false);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                PlayUi(UiSound.Confirm, 0.8f);
                Game.Do(() => Game.SetStreamer(!Settings.StreamerMode));
            }
        }

        // ---- match setup ----------------------------------------------------------------------------
        void DrawMatchSetup()
        {
            Rect area = Centered(Mathf.Min(1100f, W - 40f), Mathf.Min(720f, H - 50f));
            Frame(area, "Match setup");
            Glass(area);
            float x = area.x + 28f, y = area.y + 22f, w = area.width - 56f;
            Condensed(new Rect(x, y, w, 44f), "PLAY A MATCH", titleDisplay, Theme.Ink);
            Lbl(new Rect(x, y + 44f, w, 36f), "You and four bot teammates against five bots, first to 7 rounds, sides swap after round 6. Each round starts with a 20 s buy phase (B). A finished match is analysed like your drills.", muted, Color.white);
            y += 92f;

            Kicker(new Rect(x, y, 200f, 16f), "Map", Settings.Accent);
            y += 22f;
            List<MapInfo> maps = Game.Maps;
            var shown = new List<MapInfo>();
            if (maps != null) foreach (MapInfo m in maps) if (m != null && !string.IsNullOrEmpty(m.id)) shown.Add(m);
            if (shown.Count == 0 && Game.Map != null) shown.Add(new MapInfo { id = Game.Map.id, name = Game.MapName });
            float mw = Mathf.Min(320f, (w - 12f * (shown.Count - 1)) / Mathf.Max(1, shown.Count));
            for (int i = 0; i < shown.Count; i++)
            {
                MapInfo m = shown[i];
                var r = new Rect(x + i * (mw + 12f), y, mw, 72f);
                bool sel = m.id == Game.MatchMapId;
                float h = HoverAnim(KeyOf(m.id, r), Hover(r));
                if (sel) Outline(r, Settings.Accent, 2f);
                else Plate(r, Color.Lerp(Theme.CardC, Theme.ButtonHoverC, h));
                Condensed(new Rect(r.x + 14f, r.y + 8f, r.width - 28f, 24f), Upper(m.name ?? m.id) + (sel ? "  \u2713" : ""), cardTitle, Theme.Ink, 0.9f);
                Lbl(new Rect(r.x + 14f, r.y + 32f, r.width - 28f, 36f), MapBlurb(m.id), menuDesc, Theme.Muted);
                string id = m.id;
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { PlayUi(UiSound.Confirm, 0.7f); Game.Do(() => Game.MatchMapId = id); }
            }
            y += 88f;

            Kicker(new Rect(x, y, 200f, 16f), "Agent", Settings.Accent);
            y += 22f;
            int n = Agents.All.Count;
            float aw = (w - 12f * (n - 1)) / Mathf.Max(1, n);
            for (int i = 0; i < n; i++)
            {
                AgentInfo a = Agents.All[i];
                var r = new Rect(x + i * (aw + 12f), y, aw, 140f);
                bool sel = a.Id == Game.MatchAgentId;
                float h = HoverAnim(KeyOf(a.Id, r), Hover(r));
                if (sel) Outline(r, a.Color, 2f);
                else Plate(r, Color.Lerp(Theme.CardC, Theme.ButtonHoverC, h));
                Tex(new Rect(r.xMax - 40f, r.y + 14f, 24f, 24f), UiTex.Role(a.Role), new Color(a.Color.r, a.Color.g, a.Color.b, 0.8f));
                Condensed(new Rect(r.x + 14f, r.y + 12f, r.width - 60f, 30f), Upper(a.Name) + (sel ? "  \u2713" : ""), displayM, a.Color, 0.84f);
                Lbl(new Rect(r.x + 14f, r.y + 44f, r.width - 28f, 16f), Spaced(a.Role ?? ""), tinyCaps, Theme.Muted);
                float ix = r.x + 14f;
                foreach (Ability ab in Agents.AllAbilities)
                {
                    int c = a.Kit != null ? a.Kit.Get(ab) : 0;
                    if (c <= 0) continue;
                    Icon(new Rect(ix, r.y + 64f, 20f, 20f), ab, Agents.AbilityColor(ab));
                    Lbl(new Rect(ix + 22f, r.y + 64f, 40f, 20f), "\u00d7" + c, cardSmall, Theme.Ink);
                    ix += 56f;
                }
                Lbl(new Rect(r.x + 14f, r.y + 90f, r.width - 28f, 46f), a.Blurb ?? "", menuDesc, Theme.Muted);
                string id = a.Id;
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { PlayUi(UiSound.Confirm, 0.7f); Game.Do(() => Game.MatchAgentId = id); }
            }
            y += 156f;

            // Enemy team: plain bots, or personas (bots-wire.4).
            Kicker(new Rect(x, y, 300f, 16f), "Enemy team", Settings.Accent);
            y += 22f;
            bool v4 = Game.V4;
            if (GButton(new Rect(x, y, 150f, 40f), "Bots", Game.PersonaEnemies ? 0 : 1)) Game.Do(() => Game.PersonaEnemies = false);
            if (GButton(new Rect(x + 160f, y, 190f, 40f), "Pro personas", Game.PersonaEnemies ? 1 : 0, v4)) Game.Do(() => Game.SetPersonaEnemies(true));
            if (!v4) Lbl(new Rect(x + 366f, y + 10f, w - 370f, 20f), "Persona teams need a bridge that speaks bots-wire.4.", small, Theme.Muted);
            else if (Game.PersonaEnemies)
            {
                float px = x + 366f;
                float pw = Mathf.Min(140f, (area.xMax - 28f - px - 4f * 8f) / 5f);
                List<Persona> all = Personas.All;
                for (int i = 0; i < all.Count && i < 5; i++)
                {
                    Persona p = all[i];
                    var r = new Rect(px + i * (pw + 8f), y, pw, 40f);
                    bool inTeam = Game.EnemyPersonas.Contains(p.Id);
                    float h = HoverAnim(KeyOf(p.Id, r), Hover(r));
                    Plate(r, inTeam ? new Color(p.Primary.r * 0.3f, p.Primary.g * 0.3f, p.Primary.b * 0.3f, 0.95f) : Color.Lerp(Theme.CardC, Theme.ButtonHoverC, h));
                    if (inTeam) PlateLine(r, p.Primary);
                    Fill(new Rect(r.x + 8f, r.y + 8f, 3f, r.height - 16f), p.Primary);
                    Lbl(new Rect(r.x + 18f, r.y, r.width - 20f, r.height), Upper(p.Name), cardTitle, inTeam ? Theme.Ink : Theme.Muted);
                    string pid = p.Id;
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { PlayUi(UiSound.Tick, 0.8f); Game.Do(() => Game.ToggleEnemyPersona(pid)); }
                }
            }

            float byy = area.yMax - 68f;
            if (!Game.V3) Lbl(new Rect(x, byy - 26f, w, 20f), "This bridge does not speak bots-wire.3, so it has no match mode. Update the bridge, then reconnect.", bodyBold, Palette.Warn);
            if (Game.WaitingFor == "match") Lbl(new Rect(x, byy - 26f, w, 20f), "Starting the match...", bodyBold, Theme.Muted);
            if (GButton(new Rect(x, byy, 160f, 46f), "Back", 2)) Game.Do(Game.OpenMenu);
            bool canStart = Idle && Game.V3 && !string.IsNullOrEmpty(Game.MatchAgentId) && (!Game.PersonaEnemies || Game.EnemyPersonas.Count > 0);
            if (GButton(new Rect(area.xMax - 28f - 280f, byy, 280f, 46f), Game.PersonaEnemies ? "Start persona match" : "Start match", 1, canStart)) Game.Do(Game.StartMatch);
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
            Fill(new Rect(0f, 0f, W, H), new Color(0.01f, 0.015f, 0.03f, overlay ? 0.62f : 0.5f));
            float pw = Mathf.Min(1240f, W - 40f), ph = Mathf.Min(700f, H - 40f);
            var area = new Rect(Mathf.Round((W - pw) / 2f), Mathf.Round((H - ph) / 2f), pw, ph);
            if (!overlay) Frame(area, "Buy phase");
            Glass(area);
            float x = area.x + 24f, y = area.y + 20f;

            // Header: round type chip, round, score, timer, credits.
            string rt = Weapons.RoundTypeLabel(Game.RoundType);
            Color rc = RoundTypeColor(Game.RoundType);
            if (rt.Length > 0)
            {
                Vector2 sz = roundChip.CalcSize(new GUIContent(rt));
                var chip = new Rect(x, y, sz.x + 32f, 40f);
                Plate(chip, rc);
                Lbl(chip, rt, roundChip, new Color(0.04f, 0.04f, 0.05f));
                x = chip.xMax + 16f;
            }
            int rk = Game.Round * 10 + (Game.MatchSide == "defense" ? 1 : 0);
            if (rk != roundKey)
            {
                roundKey = rk;
                roundStr = "ROUND " + Game.Round + (string.IsNullOrEmpty(Game.MatchSide) ? "" : "   " + Game.MatchSide.ToUpperInvariant());
            }
            Lbl(new Rect(x, y, 260f, 20f), roundStr, cardTitle, Theme.Ink);
            Lbl(new Rect(x, y + 20f, 420f, 20f), Weapons.RoundTypeAdvice(Game.RoundType), small, Color.white);

            ScoreInfo sc = Game.Score;
            int sk = sc == null ? -1 : sc.player * 100 + sc.enemy;
            if (sk != scoreKey)
            {
                scoreKey = sk;
                scoreStr = sc == null ? "0  :  0" : sc.player + "  :  " + sc.enemy;
            }
            Lbl(new Rect(area.center.x - 90f, y - 4f, 180f, 34f), scoreStr, scoreNum, Theme.Ink);
            Lbl(new Rect(area.center.x - 120f, y + 28f, 240f, 16f), Game.InDuel ? "YOU  \u00b7  FIRST TO 5  \u00b7  " + Upper(Game.DuelPersonaName) : "YOU  \u00b7  FIRST TO 7  \u00b7  THEM", centerSmall, Theme.Muted);

            float left = Game.BuyTimeLeft;
            int tk = Mathf.CeilToInt(left);
            if (tk != buyTimerKey)
            {
                buyTimerKey = tk;
                buyTimerStr = tk + " s";
            }
            var timerR = new Rect(area.xMax - 24f - 380f, y, 110f, 40f);
            Plate(timerR, new Color(1f, 1f, 1f, 0.06f));
            Lbl(timerR, Game.Readied ? "READY" : buyTimerStr, timerStyle, left < 5f && !Game.Readied ? Palette.Warn : Theme.Ink);
            float total = bp != null && bp.timeS > 0f ? bp.timeS : 20f;
            Neon(new Rect(timerR.x + 8f, timerR.yMax - 3f, timerR.width - 16f, 2f), Settings.Accent, Mathf.Clamp01(left / total));
            int ck = Game.Credits;
            if (ck != creditKey)
            {
                creditKey = ck;
                creditStr = Credits(ck);
            }
            Lbl(new Rect(area.xMax - 24f - 250f, y - 4f, 250f, 14f), Spaced("Credits"), rightSmall, Theme.Muted);
            Lbl(new Rect(area.xMax - 24f - 250f, y + 6f, 250f, 40f), creditStr, bigNumRight, Settings.Accent);

            float top = area.y + 84f;
            Fill(new Rect(area.x + 24f, top - 10f, area.width - 48f, 1f), new Color(1f, 1f, 1f, 0.08f));

            float teamW = Mathf.Min(300f, area.width * 0.26f);
            float gridW = area.width - 48f - teamW - 20f;
            float colW = (gridW - 3f * 12f) / 4f;
            float cardH = Mathf.Min(170f, (area.height - 84f - 150f - 40f) / 2f);
            for (int c = 0; c < BuyColumns.Length; c++)
            {
                float cx = area.x + 24f + c * (colW + 12f);
                Kicker(new Rect(cx, top, colW, 16f), BuyColumnNames[c], Theme.Muted);
                for (int i = 0; i < BuyColumns[c].Length; i++)
                {
                    WeaponInfo wi = Weapons.Find(BuyColumns[c][i]);
                    if (wi == null) continue;
                    WeaponCard(new Rect(cx, top + 22f + i * (cardH + 10f), colW, cardH), wi);
                }
            }

            float ay = top + 22f + 2f * (cardH + 10f) + 4f;
            Kicker(new Rect(area.x + 24f, ay, 200f, 16f), "Armour", Theme.Muted);
            float armW = (gridW - 12f) / 2f;
            ArmorCard(new Rect(area.x + 24f, ay + 20f, armW, 70f), "light");
            ArmorCard(new Rect(area.x + 24f + armW + 12f, ay + 20f, armW, 70f), "heavy");

            var tr = new Rect(area.xMax - 24f - teamW, top, teamW, area.yMax - 24f - top - 60f);
            Plate(tr, Theme.CardC);
            Kicker(new Rect(tr.x + 14f, tr.y + 10f, tr.width, 16f), "Team credits", Theme.Muted);
            float ty = tr.y + 34f;
            if (bp?.team != null && bp.team.Count > 0)
            {
                foreach (TeamMember m in bp.team)
                {
                    if (m == null) continue;
                    if (ty > tr.yMax - 40f) break;
                    bool me = m.id == Game.PlayerId;
                    Lbl(new Rect(tr.x + 14f, ty, tr.width - 120f, 20f), me ? "You" : m.id, bodyBold, me ? Settings.Accent : Settings.Ally);
                    Lbl(new Rect(tr.xMax - 104f, ty, 90f, 20f), Credits(m.credits), priceStyle, Theme.Ink);
                    string prim = m.loadout?.primary;
                    if (!string.IsNullOrEmpty(prim)) Tex(new Rect(tr.x + 14f, ty + 20f, 60f, 20f), GunModels.Icon(prim), new Color(1f, 1f, 1f, 0.75f));
                    string arm = m.loadout?.ArmorKind;
                    Lbl(new Rect(tr.x + 80f, ty + 22f, tr.width - 94f, 16f), (string.IsNullOrEmpty(prim) ? "no primary" : Weapons.NameOf(prim)) + (string.IsNullOrEmpty(arm) ? "" : "  \u00b7  " + arm + " armour"), cardSmall, Theme.Muted);
                    ty += 48f;
                }
            }
            else Lbl(new Rect(tr.x + 14f, ty, tr.width - 28f, 60f), "The bridge sends the team's credits with the buy phase.", menuDesc, Theme.Muted);
            int avg = Game.TeamAverageCredits();
            if (avg >= 0) Lbl(new Rect(tr.x + 14f, tr.yMax - 26f, tr.width - 28f, 18f), "Team average " + Credits(avg), cardSmall, Theme.Muted);

            float fy = area.yMax - 24f - 48f;
            Lbl(new Rect(area.x + 24f, fy + 4f, area.width - 380f, 40f),
                (overlay ? "B closes the menu.  " : "") + "Click a weapon to buy it, click an owned one to sell it back. The round starts when the timer ends or everyone is ready.", menuDesc, Theme.Muted);
            if (GButton(new Rect(area.xMax - 24f - teamW, fy, teamW, 48f), Game.Readied ? "Ready" : "Ready up", 1, Game.BuyPhaseActive)) Game.Do(Game.ReadyUp);
            if (overlay && GButton(new Rect(area.xMax - 24f - teamW - 132f, fy, 120f, 48f), "Close (B)", 2)) Game.Do(Game.CloseBuyMenu);
            if ((!overlay && Game.Readied) || (!overlay && !Game.BuyPhaseActive))
                Lbl(new Rect(area.x + 24f, fy - 22f, 500f, 18f), "Waiting for the round to start...", small, Color.white);
        }

        void StatBar(Rect r, string label, float v, Color c)
        {
            Lbl(new Rect(r.x, r.y, 40f, r.height), label, statLabel, Theme.Muted);
            var bar = new Rect(r.x + 40f, r.y + r.height / 2f - 2f, r.width - 40f, 4f);
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
            float h = HoverAnim(KeyOf(w.Id, r), Hover(r) && active && (afford || owned));
            float a = owned || afford ? 1f : 0.45f;
            if (owned) Outline(r, Settings.Accent, 2f);
            else
            {
                Plate(r, Color.Lerp(Theme.CardC, Theme.ButtonHoverC, h));
                PlateLine(r, new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, 0.1f + 0.5f * h));
            }
            Color oldC = GUI.color;
            GUI.color = new Color(oldC.r, oldC.g, oldC.b, oldC.a * a);
            Condensed(new Rect(r.x + 12f, r.y + 6f, r.width - 100f, 24f), Upper(w.Name), cardTitle, Theme.Ink, 0.9f);
            Lbl(new Rect(r.x + 12f, r.y + 28f, r.width - 24f, 14f), Spaced(w.ClassName), tinyCaps, Theme.Muted);
            string price = w.Price <= 0 ? "FREE" : Credits(w.Price);
            Lbl(new Rect(r.xMax - 90f, r.y + 8f, 78f, 22f), price, priceStyle, afford || owned ? Theme.Ink : Palette.Bad);
            float iconH = Mathf.Clamp(r.height - 112f, 26f, 54f);
            Tex(new Rect(r.x + 12f + h * 6f, r.y + 46f, r.width - 24f, iconH), GunModels.Icon(w.Id), owned ? Settings.Accent : Color.white);
            float sy = r.y + 50f + iconH;
            float sw = r.width - 24f;
            Color bc = owned ? Settings.Accent : Theme.Lighten(Settings.Accent, 0.2f);
            StatBar(new Rect(r.x + 12f, sy, sw, 12f), "DMG", w.BodyPerShot / 150f, bc);
            StatBar(new Rect(r.x + 12f, sy + 14f, sw, 12f), "RATE", (1f / Mathf.Max(0.05f, w.FireIntervalS)) / 13.4f, bc);
            StatBar(new Rect(r.x + 12f, sy + 28f, sw, 12f), "MOB", w.Mobility, bc);
            if (owned) Lbl(new Rect(r.x + 12f, r.yMax - 18f, r.width - 24f, 14f), w.Price > 0 && active ? "OWNED  \u00b7  CLICK TO SELL" : "OWNED", cardSmall, Settings.Accent);
            else if (!afford) Lbl(new Rect(r.x + 12f, r.yMax - 18f, r.width - 24f, 14f), "NOT ENOUGH CREDITS", cardSmall, Palette.Bad);
            GUI.color = oldC;
            if (!active) return;
            string id = w.Id;
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                if (owned && w.Price > 0) { PlayUi(UiSound.Back, 0.8f); Game.Do(() => Game.SellItem(id)); }
                else if (!owned && afford) { PlayUi(UiSound.Confirm, 0.8f); Game.Do(() => Game.BuyItem(id)); }
            }
        }

        void ArmorCard(Rect r, string kind)
        {
            bool owned = Game.Owns(kind);
            bool afford = Game.CanAfford(kind);
            bool active = Game.BuyPhaseActive;
            float h = HoverAnim(KeyOf(kind, r), Hover(r) && active && (afford || owned));
            if (owned) Outline(r, Settings.Accent, 2f);
            else
            {
                Plate(r, Color.Lerp(Theme.CardC, Theme.ButtonHoverC, h));
                PlateLine(r, new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, 0.1f + 0.5f * h));
            }
            float a = owned || afford ? 1f : 0.45f;
            Color oldC = GUI.color;
            GUI.color = new Color(oldC.r, oldC.g, oldC.b, oldC.a * a);
            Tex(new Rect(r.x + 12f, r.y + 12f, 44f, 46f), UiTex.Glyph("sentinel"), owned ? Settings.Accent : new Color(0.75f, 0.85f, 1f));
            Condensed(new Rect(r.x + 64f, r.y + 10f, r.width - 160f, 24f), Upper(Weapons.ArmorName(kind)), cardTitle, Theme.Ink, 0.9f);
            Lbl(new Rect(r.x + 64f, r.y + 34f, r.width - 160f, 30f), Weapons.ArmorPointsOf(kind) + " points, absorbs 66% of each hit" + (owned ? (active ? "   OWNED \u00b7 CLICK TO SELL" : "   OWNED") : ""), menuDesc, owned ? Settings.Accent : Theme.Muted);
            Lbl(new Rect(r.xMax - 92f, r.y + 12f, 80f, 22f), Credits(Weapons.ArmorPrice(kind)), priceStyle, afford || owned ? Theme.Ink : Palette.Bad);
            GUI.color = oldC;
            if (!active) return;
            string id = kind;
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                if (owned) { PlayUi(UiSound.Back, 0.8f); Game.Do(() => Game.SellItem(id)); }
                else if (afford) { PlayUi(UiSound.Confirm, 0.8f); Game.Do(() => Game.BuyItem(id)); }
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
                    string mvp = string.IsNullOrEmpty(rr.mvp) ? "" : "MVP  " + (rr.mvp == Game.PlayerId ? "You" : rr.mvp);
                    string score = rr.score != null ? rr.score.player + " : " + rr.score.enemy : "";
                    var parts = new List<string>();
                    foreach (string s in new[] { how, score, earned, mvp }) if (s.Length > 0) parts.Add(s);
                    resultSub = string.Join("     ", parts);
                }
                Color c = won ? Palette.Ok : Palette.Bad;
                var br = new Rect(0f, H * 0.26f + (1f - k) * 16f, W, 116f);
                Fill(br, new Color(0.02f, 0.03f, 0.05f, 0.82f * k));
                Slash(new Rect(W / 2f - W * 0.32f * k, br.y - 6f, W * 0.64f * k, 4f), c);
                Slash(new Rect(W / 2f - W * 0.32f * k, br.yMax + 2f, W * 0.64f * k, 4f), c);
                Scanlines(br, 0.25f * k);
                c.a = k;
                Condensed(new Rect(0f, br.y + 10f, W, 64f), won ? "ROUND WON" : "ROUND LOST", displayLC, c, 0.82f, true);
                Shadowed(new Rect(40f, br.y + 76f, W - 80f, 30f), resultSub, bannerSub, new Color(1f, 1f, 1f, k));
            }
            float sw = now - Game.SideSwapAt;
            if (sw >= 0f && sw < 4f)
            {
                float k = Mathf.Clamp01(sw / 0.3f) * Mathf.Clamp01((4f - sw) / 0.4f);
                var r = new Rect(W / 2f - 260f, H * 0.18f, 520f, 58f);
                Plate(r, new Color(0.03f, 0.04f, 0.07f, 0.88f * k));
                PlateLine(r, new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, k));
                Condensed(new Rect(r.x, r.y + 4f, r.width, 30f), Spaced("Sides swap"), hudCenter, new Color(1f, 1f, 1f, k), 0.9f, true);
                Shadowed(new Rect(r.x, r.y + 32f, r.width, 20f), "You now play " + (Game.MatchSide ?? "the other side"), centerSmall, new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, k));
            }
            DrawRoundSweep(now);
        }

        // ---- match end --------------------------------------------------------------------------------
        void DrawMatchEnd()
        {
            MatchEndMsg me = Game.MatchEnd;
            Fill(new Rect(0f, 0f, W, H), new Color(0.01f, 0.015f, 0.03f, 0.6f));
            Rect area = Centered(820f, 580f);
            bool won = me != null && me.won;
            Color c = won ? Palette.Ok : Palette.Bad;
            Frame(area, won ? "Match won" : "Match lost");
            Glass(area);
            float k = Intro01;
            Neon(new Rect(area.x + Theme.Cut, area.y, area.width - Theme.Cut * 2f, 3f), c, k);
            Condensed(new Rect(area.x, area.y + 24f, area.width, 90f), won ? Spaced("Victory") : Spaced("Defeat"), displayLC, c, 0.82f, true);
            ScoreInfo sc = me?.score ?? Game.Score;
            Lbl(new Rect(area.x, area.y + 112f, area.width, 40f), sc != null ? sc.player + "  :  " + sc.enemy : "", scoreNum, Theme.Ink);
            Lbl(new Rect(area.x, area.y + 148f, area.width, 18f), "ROUNDS  YOU : THEM", centerSmall, Theme.Muted);

            int nh = Game.RoundHistory.Count;
            float pw = 18f, pg = 6f;
            float px0 = area.center.x - (nh * pw + Mathf.Max(0, nh - 1) * pg) / 2f;
            for (int i = 0; i < nh; i++)
            {
                float pk = Mathf.Clamp01((Time.unscaledTime - screenAt - 0.3f - i * 0.05f) / 0.2f);
                Plate(new Rect(px0 + i * (pw + pg), area.y + 176f + (1f - pk) * 6f, pw, 8f), new Color(Game.RoundHistory[i] ? Palette.Ok.r : Palette.Bad.r, Game.RoundHistory[i] ? Palette.Ok.g : Palette.Bad.g, Game.RoundHistory[i] ? Palette.Ok.b : Palette.Bad.b, pk));
            }

            MatchStats st = me?.stats;
            int kills = st != null ? st.kills : Game.MatchKills;
            int deaths = st != null ? st.deaths : Game.MatchDeaths;
            float hs = st != null ? st.headshotPct : (Game.MatchKills > 0 ? Game.MatchHeadKills / (float)Game.MatchKills : 0f);
            if (hs > 1f) hs /= 100f;
            string[] labels = { "Kills", "Deaths", "K / D", "Headshot kills" };
            string[] values = { kills.ToString(), deaths.ToString(), (deaths > 0 ? kills / (float)deaths : kills).ToString("0.00"), Mathf.RoundToInt(hs * 100f) + "%" };
            float cw = (area.width - 56f - 36f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(area.x + 28f + i * (cw + 12f), area.y + 208f, cw, 100f);
                Plate(r, Theme.CardC);
                Fill(new Rect(r.x + Theme.Cut, r.y, r.width - Theme.Cut - 4f, 2f), i == 0 ? Settings.Accent : new Color(1f, 1f, 1f, 0.15f));
                Condensed(new Rect(r.x, r.y + 12f, r.width, 50f), values[i], displayMC, Theme.Ink, 0.9f);
                Lbl(new Rect(r.x, r.y + 66f, r.width, 18f), Spaced(labels[i]), centerSmall, Theme.Muted);
            }
            Lbl(new Rect(area.x + 40f, area.y + 326f, area.width - 80f, 60f),
                "The match is recorded like your drills and re-analysed, so what happened here feeds your weakness report.", centerBody, Theme.Muted);
            if (GButton(new Rect(area.x + 28f, area.yMax - 74f, 220f, 46f), "Main menu", 2)) Game.Do(Game.BackFromMatchEnd);
            if (GButton(new Rect(area.xMax - 28f - 260f, area.yMax - 74f, 260f, 46f), "Play again", 1, Idle && Game.V3)) Game.Do(Game.OpenMatchSetup);
        }
    }
}

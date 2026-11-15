// Round 4 screens and HUD parts: the main menu, the match setup, the buy menu
// (full screen between rounds, an overlay during the round's buy phase), the
// round result and side-swap banners, the match end screen, and the in-round
// score bar, ammo counter, credits, armour, kill feed with weapon icons, damage
// numbers and the Longshot's scope. The buy menu and every HUD part use only
// GUI.* calls (no GUILayout), because IMGUI's layout pass is off while playing.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        GUIStyle hero, kicker, menuTitle, menuDesc, cardTitle, cardSmall, priceStyle, statLabel, bigNumRight, timerStyle,
            roundChip, ammoBig, ammoSmall, killName, dmgStyle, dmgHead, scoreNum, centerSmall, centerBody, rightSmall;
        Texture2D fadeLeft, fadeDown;

        void EnsureMatchStyles()
        {
            GUIStyle lbl = GUI.skin.label;
            hero = Text(lbl, 64, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            // Lbl tints with GUI.contentColor, so these styles are white and take their colour from the call.
            kicker = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            menuTitle = Text(lbl, 21, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            menuDesc = Text(lbl, 13, FontStyle.Normal, Color.white, TextAnchor.UpperLeft, true);
            cardTitle = Text(lbl, 16, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            cardSmall = Text(lbl, 11, FontStyle.Bold, Color.white, TextAnchor.UpperLeft, false);
            priceStyle = Text(lbl, 15, FontStyle.Bold, Color.white, TextAnchor.UpperRight, false);
            statLabel = Text(lbl, 10, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            bigNumRight = Text(lbl, 40, FontStyle.Bold, Color.white, TextAnchor.MiddleRight, false);
            timerStyle = Text(lbl, 30, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            roundChip = Text(lbl, 20, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            ammoBig = Text(lbl, 44, FontStyle.Bold, Color.white, TextAnchor.LowerRight, false);
            ammoSmall = Text(lbl, 18, FontStyle.Bold, Color.white, TextAnchor.LowerLeft, false);
            killName = Text(lbl, 14, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            dmgStyle = Text(lbl, 18, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            dmgHead = Text(lbl, 24, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            scoreNum = Text(lbl, 26, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            centerSmall = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            centerBody = Text(lbl, 15, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter, true);
            rightSmall = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleRight, false);
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
                px[y * w + x] = new Color32(6, 7, 10, (byte)Mathf.RoundToInt(a * 240f));
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

        /// <summary>A rounded outline: `w` px of colour `c` around the inside of `r`.</summary>
        void Outline(Rect r, Color c, float w)
        {
            Round(r, c);
            Round(new Rect(r.x + w, r.y + w, r.width - 2f * w, r.height - 2f * w), new Color(Theme.CardC.r, Theme.CardC.g, Theme.CardC.b, 1f));
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
        static readonly string[] MenuTitles = { "Play a match", "Practice", "Rehearse", "Settings" };
        static readonly string[] MenuDescs =
        {
            "You and four bot teammates against five bots. Buy rounds, first to 7, sides swap after round 6.",
            "The coach reads a player's matches, finds the weakest situation and builds drills from it.",
            "Replay the exact moments a sample player lost: same positions, same clock.",
            "Mouse, crosshair, graphics preset, viewmodel, minimap, colours and the key list.",
        };

        void DrawMenu()
        {
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(0f, 0f, Mathf.Min(W, 900f), H), fadeLeft, ScaleMode.StretchToFill, true);
                GUI.DrawTexture(new Rect(0f, H - 220f, W, 220f), fadeDown, ScaleMode.StretchToFill, true);
            }
            float rise = (1f - Intro01) * 18f;
            float x = 72f, y = Mathf.Max(60f, H * 0.16f) + rise;
            Lbl(new Rect(x, y, 600f, 18f), "TACTICAL COACHING CLIENT", kicker, Settings.Accent);
            Lbl(new Rect(x - 3f, y + 16f, 700f, 80f), "SALTWORKS", hero, Theme.Ink);
            Fill(new Rect(x, y + 94f, 64f, 4f), Settings.Accent);
            Lbl(new Rect(x, y + 108f, 520f, 40f), "Find what you lose, practise it against bots that play like people, then take it into a full match.", menuDesc, Theme.Muted);

            float by = y + 168f;
            bool idle = Idle;
            for (int i = 0; i < MenuTitles.Length; i++)
            {
                var r = new Rect(x, by + i * 86f, 440f, 74f);
                bool enabled = i == 3 || (idle && (i != 0 || Game.V3));
                bool hover = enabled && Hover(r);
                Round(r, hover ? new Color(1f, 1f, 1f, 0.09f) : new Color(0.08f, 0.09f, 0.12f, 0.72f));
                Fill(new Rect(r.x, r.y + 12f, hover ? 4f : 2f, r.height - 24f), enabled ? Settings.Accent : Theme.Faint);
                Color tc = enabled ? Theme.Ink : Theme.Faint;
                Lbl(new Rect(r.x + 22f, r.y + 10f, r.width - 40f, 26f), MenuTitles[i], menuTitle, tc);
                string desc = i == 0 && !Game.V3 ? "Needs a bridge that speaks bots-wire.3 (weapons and economy). This one does not." : MenuDescs[i];
                Lbl(new Rect(r.x + 22f, r.y + 37f, r.width - 40f, 34f), desc, menuDesc, enabled ? Theme.Muted : Theme.Faint);
                if (hover) Lbl(new Rect(r.xMax - 40f, r.y + 22f, 24f, 30f), "\u203a", menuTitle, Settings.Accent);
                GUI.enabled = enabled;
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    switch (i)
                    {
                        case 0: Game.Do(Game.OpenMatchSetup); break;
                        case 1: Game.Do(Game.OpenPractice); break;
                        case 2: Game.Do(Game.OpenRehearse); break;
                        default: Game.Do(Game.OpenSettings); break;
                    }
                }
                GUI.enabled = true;
            }

            // Status card, bottom right.
            var sr = new Rect(W - 372f, H - 154f, 340f, 118f);
            Round(sr, new Color(0.07f, 0.08f, 0.11f, 0.86f));
            float sy = sr.y + 14f;
            Lbl(new Rect(sr.x + 18f, sy, 300f, 16f), "BRIDGE", caption, Color.white);
            string proto = Game.V3 ? "bots-wire.3: matches, weapons, economy" : Game.V2 ? "bots-wire.2: drills, maps, agents" : "bots-wire.1: drills";
            Lbl(new Rect(sr.x + 18f, sy + 16f, 310f, 20f), (Game.Connected ? "Connected  " : "Offline  ") + proto, small, Game.Connected ? Palette.Ok : Palette.Bad);
            Lbl(new Rect(sr.x + 18f, sy + 44f, 140f, 16f), "MAP", caption, Color.white);
            Lbl(new Rect(sr.x + 18f, sy + 60f, 150f, 22f), string.IsNullOrEmpty(Game.MapName) ? "?" : Game.MapName, bodyBold, Theme.Ink);
            Lbl(new Rect(sr.x + 180f, sy + 44f, 140f, 16f), "AGENT", caption, Color.white);
            string ag = Game.PickedAgentId ?? Game.RememberedAgent;
            Lbl(new Rect(sr.x + 180f, sy + 60f, 150f, 22f), string.IsNullOrEmpty(ag) ? "not picked" : Agents.NameOf(ag), bodyBold, string.IsNullOrEmpty(ag) ? Theme.Muted : Agents.ColorOf(ag));
            Lbl(new Rect(x, H - 34f, 600f, 18f), "Esc closes menus.   F3 frame rate.", small, Theme.Faint);
        }

        // ---- match setup ----------------------------------------------------------------------------
        void DrawMatchSetup()
        {
            Rect area = Centered(Mathf.Min(1060f, W - 40f), Mathf.Min(620f, H - 40f));
            Round(area, Theme.PanelC);
            float x = area.x + 28f, y = area.y + 24f, w = area.width - 56f;
            Lbl(new Rect(x, y, w, 40f), "Play a match", title, Theme.Ink);
            Lbl(new Rect(x, y + 40f, w, 36f), "You and four bot teammates against five bots, first to 7 rounds, sides swap after round 6. Each round starts with a 20 s buy phase (B). A finished match is analysed like your drills.", muted, Color.white);
            y += 92f;

            Lbl(new Rect(x, y, 200f, 16f), "MAP", caption, Color.white);
            Fill(new Rect(x, y + 16f, 28f, 2f), Settings.Accent);
            y += 26f;
            List<MapInfo> maps = Game.Maps;
            var shown = new List<MapInfo>();
            if (maps != null) foreach (MapInfo m in maps) if (m != null && !string.IsNullOrEmpty(m.id)) shown.Add(m);
            if (shown.Count == 0 && Game.Map != null) shown.Add(new MapInfo { id = Game.Map.id, name = Game.MapName });
            float mw = Mathf.Min(320f, (w - 12f * (shown.Count - 1)) / Mathf.Max(1, shown.Count));
            for (int i = 0; i < shown.Count; i++)
            {
                MapInfo m = shown[i];
                var r = new Rect(x + i * (mw + 12f), y, mw, 74f);
                bool sel = m.id == Game.MatchMapId;
                if (sel) Outline(r, Settings.Accent, 2f);
                else Round(r, Hover(r) ? Theme.ButtonHoverC : Theme.CardC);
                Lbl(new Rect(r.x + 14f, r.y + 10f, r.width - 28f, 22f), (m.name ?? m.id) + (sel ? "  \u2713" : ""), cardTitle, Theme.Ink);
                Lbl(new Rect(r.x + 14f, r.y + 34f, r.width - 28f, 36f), MapBlurb(m.id), menuDesc, Theme.Muted);
                string id = m.id;
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) Game.Do(() => Game.MatchMapId = id);
            }
            y += 92f;

            Lbl(new Rect(x, y, 200f, 16f), "AGENT", caption, Color.white);
            Fill(new Rect(x, y + 16f, 28f, 2f), Settings.Accent);
            y += 26f;
            int n = Agents.All.Count;
            float aw = (w - 12f * (n - 1)) / Mathf.Max(1, n);
            for (int i = 0; i < n; i++)
            {
                AgentInfo a = Agents.All[i];
                var r = new Rect(x + i * (aw + 12f), y, aw, 150f);
                bool sel = a.Id == Game.MatchAgentId;
                if (sel) Outline(r, a.Color, 2f);
                else Round(r, Hover(r) ? Theme.ButtonHoverC : Theme.CardC);
                Round(new Rect(r.x + 12f, r.y + 12f, r.width - 24f, 5f), a.Color);
                Lbl(new Rect(r.x + 14f, r.y + 24f, r.width - 28f, 26f), a.Name + (sel ? "  \u2713" : ""), h2, a.Color);
                Lbl(new Rect(r.x + 14f, r.y + 50f, r.width - 28f, 16f), (a.Role ?? "").ToUpperInvariant(), cardSmall, Theme.Muted);
                float ix = r.x + 14f;
                foreach (Ability ab in Agents.AllAbilities)
                {
                    int c = a.Kit != null ? a.Kit.Get(ab) : 0;
                    if (c <= 0) continue;
                    Icon(new Rect(ix, r.y + 70f, 20f, 20f), ab, Agents.AbilityColor(ab));
                    Lbl(new Rect(ix + 22f, r.y + 70f, 40f, 20f), "\u00d7" + c, cardSmall, Theme.Ink);
                    ix += 56f;
                }
                Lbl(new Rect(r.x + 14f, r.y + 96f, r.width - 28f, 50f), a.Blurb ?? "", menuDesc, Theme.Muted);
                string id = a.Id;
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) Game.Do(() => Game.MatchAgentId = id);
            }

            float byy = area.yMax - 70f;
            if (!Game.V3) Lbl(new Rect(x, byy - 26f, w, 20f), "This bridge does not speak bots-wire.3, so it has no match mode. Update the bridge, then reconnect.", bodyBold, Palette.Warn);
            if (Game.WaitingFor == "match") Lbl(new Rect(x, byy - 26f, w, 20f), "Starting the match...", bodyBold, Theme.Muted);
            if (GUI.Button(new Rect(x, byy, 160f, 46f), "Back", button)) Game.Do(Game.OpenMenu);
            GUI.enabled = Idle && Game.V3 && !string.IsNullOrEmpty(Game.MatchAgentId);
            if (GUI.Button(new Rect(area.xMax - 28f - 260f, byy, 260f, 46f), "Start match", primary)) Game.Do(Game.StartMatch);
            GUI.enabled = true;
        }

        // ---- buy menu ---------------------------------------------------------------------------------
        static readonly string[][] BuyColumns =
        {
            new[] { "pin", "hammer" },
            new[] { "wasp", "brick" },
            new[] { "lancer", "viper" },
            new[] { "longshot" },
        };
        static readonly string[] BuyColumnNames = { "SIDEARMS", "SMG  /  SHOTGUN", "RIFLES", "SNIPER" };

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
            Fill(new Rect(0f, 0f, W, H), new Color(0.02f, 0.03f, 0.05f, overlay ? 0.62f : 0.5f));
            float pw = Mathf.Min(1240f, W - 40f), ph = Mathf.Min(700f, H - 40f);
            var area = new Rect(Mathf.Round((W - pw) / 2f), Mathf.Round((H - ph) / 2f), pw, ph);
            Round(area, Theme.PanelC);
            float x = area.x + 24f, y = area.y + 20f;

            // Header: round type chip, round, score, timer, credits.
            string rt = Weapons.RoundTypeLabel(Game.RoundType);
            Color rc = RoundTypeColor(Game.RoundType);
            if (rt.Length > 0)
            {
                Vector2 sz = roundChip.CalcSize(new GUIContent(rt));
                var chip = new Rect(x, y, sz.x + 28f, 40f);
                Round(chip, rc);
                Lbl(chip, rt, roundChip, new Color(0.06f, 0.06f, 0.07f));
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

            // Score in the middle.
            ScoreInfo sc = Game.Score;
            int sk = sc == null ? -1 : sc.player * 100 + sc.enemy;
            if (sk != scoreKey)
            {
                scoreKey = sk;
                scoreStr = sc == null ? "0  :  0" : sc.player + "  :  " + sc.enemy;
            }
            Lbl(new Rect(area.center.x - 90f, y - 2f, 180f, 30f), scoreStr, scoreNum, Theme.Ink);
            Lbl(new Rect(area.center.x - 90f, y + 26f, 180f, 16f), "YOU  \u00b7  FIRST TO 7  \u00b7  THEM", centerSmall, Theme.Muted);

            // Timer and credits on the right.
            float left = Game.BuyTimeLeft;
            int tk = Mathf.CeilToInt(left);
            if (tk != buyTimerKey)
            {
                buyTimerKey = tk;
                buyTimerStr = Game.Readied ? "READY" : tk + " s";
            }
            var timerR = new Rect(area.xMax - 24f - 380f, y, 110f, 40f);
            Round(timerR, new Color(1f, 1f, 1f, 0.06f));
            Lbl(timerR, Game.Readied ? "READY" : buyTimerStr, timerStyle, left < 5f && !Game.Readied ? Palette.Warn : Theme.Ink);
            float total = bp != null && bp.timeS > 0f ? bp.timeS : 20f;
            Fill(new Rect(timerR.x + 8f, timerR.yMax - 3f, (timerR.width - 16f) * Mathf.Clamp01(left / total), 3f), Settings.Accent);
            int ck = Game.Credits;
            if (ck != creditKey)
            {
                creditKey = ck;
                creditStr = Credits(ck);
            }
            Lbl(new Rect(area.xMax - 24f - 250f, y - 4f, 250f, 14f), "CREDITS", rightSmall, Theme.Muted);
            Lbl(new Rect(area.xMax - 24f - 250f, y + 6f, 250f, 40f), creditStr, bigNumRight, Settings.Accent);

            float top = area.y + 84f;
            Fill(new Rect(area.x + 24f, top - 10f, area.width - 48f, 1f), new Color(1f, 1f, 1f, 0.08f));

            // Weapons (left) and team (right).
            float teamW = Mathf.Min(300f, area.width * 0.26f);
            float gridW = area.width - 48f - teamW - 20f;
            float colW = (gridW - 3f * 12f) / 4f;
            float cardH = Mathf.Min(170f, (area.height - 84f - 150f - 40f) / 2f);
            for (int c = 0; c < BuyColumns.Length; c++)
            {
                float cx = area.x + 24f + c * (colW + 12f);
                Lbl(new Rect(cx, top, colW, 16f), BuyColumnNames[c], caption, Color.white);
                for (int i = 0; i < BuyColumns[c].Length; i++)
                {
                    WeaponInfo wi = Weapons.Find(BuyColumns[c][i]);
                    if (wi == null) continue;
                    WeaponCard(new Rect(cx, top + 20f + i * (cardH + 10f), colW, cardH), wi);
                }
            }

            // Armour row.
            float ay = top + 20f + 2f * (cardH + 10f) + 4f;
            Lbl(new Rect(area.x + 24f, ay, 200f, 16f), "ARMOUR", caption, Color.white);
            float armW = (gridW - 12f) / 2f;
            ArmorCard(new Rect(area.x + 24f, ay + 20f, armW, 70f), "light");
            ArmorCard(new Rect(area.x + 24f + armW + 12f, ay + 20f, armW, 70f), "heavy");

            // Team panel.
            var tr = new Rect(area.xMax - 24f - teamW, top, teamW, area.yMax - 24f - top - 60f);
            Round(tr, Theme.CardC);
            Lbl(new Rect(tr.x + 14f, tr.y + 10f, tr.width, 16f), "TEAM CREDITS", caption, Color.white);
            float ty = tr.y + 32f;
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

            // Footer.
            float fy = area.yMax - 24f - 48f;
            Lbl(new Rect(area.x + 24f, fy + 4f, area.width - 380f, 40f),
                (overlay ? "B closes the menu.  " : "") + "Click a weapon to buy it, click an owned one to sell it back. The round starts when the timer ends or everyone is ready.", menuDesc, Theme.Muted);
            GUI.enabled = Game.BuyPhaseActive;
            if (GUI.Button(new Rect(area.xMax - 24f - teamW, fy, teamW, 48f), Game.Readied ? "Ready" : "Ready up", primary)) Game.Do(Game.ReadyUp);
            GUI.enabled = true;
            if (overlay && GUI.Button(new Rect(area.xMax - 24f - teamW - 132f, fy, 120f, 48f), "Close (B)", button)) Game.Do(Game.CloseBuyMenu);
            if (!overlay && Game.Readied || (!overlay && !Game.BuyPhaseActive))
                Lbl(new Rect(area.x + 24f, fy - 22f, 500f, 18f), "Waiting for the round to start...", small, Color.white);
        }

        void StatBar(Rect r, string label, float v, Color c)
        {
            Lbl(new Rect(r.x, r.y, 40f, r.height), label, statLabel, Theme.Muted);
            var bar = new Rect(r.x + 40f, r.y + r.height / 2f - 2f, r.width - 40f, 4f);
            Fill(bar, new Color(1f, 1f, 1f, 0.1f));
            Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(v), bar.height), c);
        }

        void WeaponCard(Rect r, WeaponInfo w)
        {
            bool owned = Game.Owns(w.Id);
            bool afford = Game.CanAfford(w.Id);
            bool active = Game.BuyPhaseActive;
            bool hover = Hover(r) && active;
            float a = owned || afford ? 1f : 0.45f;
            if (owned) Outline(r, Settings.Accent, 2f);
            else Round(r, hover && afford ? Theme.ButtonHoverC : Theme.CardC);
            Color oldC = GUI.color;
            GUI.color = new Color(oldC.r, oldC.g, oldC.b, oldC.a * a);
            Lbl(new Rect(r.x + 12f, r.y + 8f, r.width - 90f, 22f), w.Name, cardTitle, Theme.Ink);
            Lbl(new Rect(r.x + 12f, r.y + 28f, r.width - 24f, 14f), w.ClassName.ToUpperInvariant(), cardSmall, Theme.Muted);
            string price = w.Price <= 0 ? "FREE" : Credits(w.Price);
            Lbl(new Rect(r.xMax - 90f, r.y + 8f, 78f, 22f), price, priceStyle, afford || owned ? Theme.Ink : Palette.Bad);
            float iconH = Mathf.Clamp(r.height - 112f, 26f, 54f);
            Tex(new Rect(r.x + 12f, r.y + 46f, r.width - 24f, iconH), GunModels.Icon(w.Id), owned ? Settings.Accent : Color.white);
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
                if (owned && w.Price > 0) Game.Do(() => Game.SellItem(id));
                else if (!owned && afford) Game.Do(() => Game.BuyItem(id));
            }
        }

        void ArmorCard(Rect r, string kind)
        {
            bool owned = Game.Owns(kind);
            bool afford = Game.CanAfford(kind);
            bool active = Game.BuyPhaseActive;
            bool hover = Hover(r) && active;
            if (owned) Outline(r, Settings.Accent, 2f);
            else Round(r, hover && afford ? Theme.ButtonHoverC : Theme.CardC);
            float a = owned || afford ? 1f : 0.45f;
            Color oldC = GUI.color;
            GUI.color = new Color(oldC.r, oldC.g, oldC.b, oldC.a * a);
            // A shield glyph: a rounded plate with one or two bars.
            var sh = new Rect(r.x + 14f, r.y + 14f, 34f, 42f);
            Round(sh, owned ? Settings.Accent : new Color(0.7f, 0.74f, 0.8f));
            Round(new Rect(sh.x + 5f, sh.y + 5f, sh.width - 10f, sh.height - 10f), Theme.CardC);
            Fill(new Rect(sh.x + 10f, sh.y + 14f, sh.width - 20f, 4f), owned ? Settings.Accent : Color.white);
            if (kind == "heavy") Fill(new Rect(sh.x + 10f, sh.y + 22f, sh.width - 20f, 4f), owned ? Settings.Accent : Color.white);
            Lbl(new Rect(r.x + 62f, r.y + 12f, r.width - 160f, 22f), Weapons.ArmorName(kind), cardTitle, Theme.Ink);
            Lbl(new Rect(r.x + 62f, r.y + 34f, r.width - 160f, 30f), Weapons.ArmorPointsOf(kind) + " points, absorbs 66% of each hit" + (owned ? (active ? "   OWNED \u00b7 CLICK TO SELL" : "   OWNED") : ""), menuDesc, owned ? Settings.Accent : Theme.Muted);
            Lbl(new Rect(r.xMax - 92f, r.y + 12f, 80f, 22f), Credits(Weapons.ArmorPrice(kind)), priceStyle, afford || owned ? Theme.Ink : Palette.Bad);
            GUI.color = oldC;
            if (!active) return;
            string id = kind;
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                if (owned) Game.Do(() => Game.SellItem(id));
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
                float k = Theme.Ease((now - Game.RoundResultAt) / 0.3f) * Mathf.Clamp01((4.5f - (now - Game.RoundResultAt)) / 0.4f);
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
                var br = new Rect(0f, H * 0.26f + (1f - k) * 16f, W, 112f);
                Fill(br, new Color(0.04f, 0.05f, 0.07f, 0.82f * k));
                Fill(new Rect(W / 2f - W * 0.3f * k, br.y, W * 0.6f * k, 3f), c);
                Fill(new Rect(W / 2f - W * 0.3f * k, br.yMax - 3f, W * 0.6f * k, 3f), c);
                c.a = k;
                Shadowed(new Rect(0f, br.y + 12f, W, 56f), won ? "ROUND WON" : "ROUND LOST", banner, c);
                Shadowed(new Rect(40f, br.y + 70f, W - 80f, 30f), resultSub, bannerSub, new Color(1f, 1f, 1f, k));
            }
            float sw = now - Game.SideSwapAt;
            if (sw >= 0f && sw < 4f)
            {
                float k = Mathf.Clamp01(sw / 0.3f) * Mathf.Clamp01((4f - sw) / 0.4f);
                var r = new Rect(W / 2f - 260f, H * 0.18f, 520f, 54f);
                Round(r, new Color(0.06f, 0.07f, 0.1f, 0.85f * k));
                Shadowed(new Rect(r.x, r.y + 4f, r.width, 28f), "SIDES SWAP", hudCenter, new Color(1f, 1f, 1f, k));
                Shadowed(new Rect(r.x, r.y + 28f, r.width, 20f), "You now play " + (Game.MatchSide ?? "the other side"), centerSmall, new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, k));
            }
        }

        // ---- match end --------------------------------------------------------------------------------
        void DrawMatchEnd()
        {
            MatchEndMsg me = Game.MatchEnd;
            Fill(new Rect(0f, 0f, W, H), new Color(0.02f, 0.03f, 0.05f, 0.7f));
            Rect area = Centered(760f, 560f);
            Round(area, Theme.PanelC);
            bool won = me != null && me.won;
            Color c = won ? Palette.Ok : Palette.Bad;
            Fill(new Rect(area.x, area.y, area.width, 4f), c);
            Lbl(new Rect(area.x, area.y + 30f, area.width, 60f), won ? "VICTORY" : "DEFEAT", banner, c);
            ScoreInfo sc = me?.score ?? Game.Score;
            Lbl(new Rect(area.x, area.y + 92f, area.width, 40f), sc != null ? sc.player + "  :  " + sc.enemy : "", scoreNum, Theme.Ink);
            Lbl(new Rect(area.x, area.y + 128f, area.width, 18f), "ROUNDS  YOU : THEM", centerSmall, Theme.Muted);

            // Round history pips.
            int nh = Game.RoundHistory.Count;
            float pw = 18f, pg = 6f;
            float px0 = area.center.x - (nh * pw + Mathf.Max(0, nh - 1) * pg) / 2f;
            for (int i = 0; i < nh; i++)
                Round(new Rect(px0 + i * (pw + pg), area.y + 156f, pw, 8f), Game.RoundHistory[i] ? Palette.Ok : Palette.Bad);

            MatchStats st = me?.stats;
            int kills = st != null ? st.kills : Game.MatchKills;
            int deaths = st != null ? st.deaths : Game.MatchDeaths;
            float hs = st != null ? st.headshotPct : (Game.MatchKills > 0 ? Game.MatchHeadKills / (float)Game.MatchKills : 0f);
            if (hs > 1f) hs /= 100f;
            string[] labels = { "KILLS", "DEATHS", "K / D", "HEADSHOT KILLS" };
            string[] values = { kills.ToString(), deaths.ToString(), (deaths > 0 ? kills / (float)deaths : kills).ToString("0.00"), Mathf.RoundToInt(hs * 100f) + "%" };
            float cw = (area.width - 56f - 36f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(area.x + 28f + i * (cw + 12f), area.y + 190f, cw, 96f);
                Round(r, Theme.CardC);
                Lbl(new Rect(r.x, r.y + 12f, r.width, 44f), values[i], scoreNum, Theme.Ink);
                Lbl(new Rect(r.x, r.y + 62f, r.width, 18f), labels[i], centerSmall, Theme.Muted);
            }
            Lbl(new Rect(area.x + 40f, area.y + 306f, area.width - 80f, 60f),
                "The match is recorded like your drills and re-analysed, so what happened here feeds your weakness report.", centerBody, Theme.Muted);
            if (GUI.Button(new Rect(area.x + 28f, area.yMax - 74f, 220f, 46f), "Main menu", button)) Game.Do(Game.BackFromMatchEnd);
            GUI.enabled = Idle && Game.V3;
            if (GUI.Button(new Rect(area.xMax - 28f - 260f, area.yMax - 74f, 260f, 46f), "Play again", primary)) Game.Do(Game.OpenMatchSetup);
            GUI.enabled = true;
        }

        // ---- in-round HUD parts --------------------------------------------------------------------------
        string scoreBarL = "", scoreBarR = "", roundLine = "";
        int scoreBarKey = int.MinValue, roundLineKey = int.MinValue;

        /// <summary>Top centre: your score, the round clock, their score, alive pips, and the round line.</summary>
        void DrawScoreBar(SimState s, float now)
        {
            bool post = s.phase == "post_plant";
            float t = Mathf.Max(0f, s.roundTimeLeftS);
            var clockR = new Rect(W / 2f - 70f, 12f, 140f, 54f);
            Round(clockR, new Color(0.06f, 0.07f, 0.10f, 0.8f));
            Fill(new Rect(clockR.x + 10f, clockR.yMax - 3f, clockR.width - 20f, 3f), post ? Palette.Spike : Palette.Team(Game.PlayerTeam));
            int ck = t < 10f ? (int)(t * 10f) : (int)t * 10;
            if (ck != clockKey)
            {
                clockKey = ck;
                clockStr = string.Format("{0}:{1:00}", (int)(t / 60f), (int)t % 60) + (t < 10f ? "." + ((int)(t * 10f) % 10) : "");
            }
            if (!ReferenceEquals(phaseSrc, s.phase))
            {
                phaseSrc = s.phase;
                phaseStr = (s.phase ?? "").Replace('_', ' ').ToUpperInvariant();
            }
            Shadowed(new Rect(clockR.x, clockR.y + 1f, clockR.width, 38f), clockStr, hudClock, post ? new Color(1f, 0.55f, 0.55f) : Color.white);
            GUI.Label(new Rect(clockR.x, clockR.y + 35f, clockR.width, 16f), post ? "SPIKE PLANTED" : phaseStr, hudSmall);

            // Score boxes either side (a match) or team labels (a drill).
            ScoreInfo sc = Game.Score;
            int key = sc == null ? -1 : sc.player * 100 + sc.enemy;
            if (key != scoreBarKey)
            {
                scoreBarKey = key;
                scoreBarL = sc == null ? "" : Theme.Int(sc.player);
                scoreBarR = sc == null ? "" : Theme.Int(sc.enemy);
            }
            Color ally = Settings.Ally, enemy = Settings.Enemy;
            var lr = new Rect(clockR.x - 70f, 12f, 64f, 54f);
            var rr = new Rect(clockR.xMax + 6f, 12f, 64f, 54f);
            Round(lr, new Color(ally.r * 0.25f, ally.g * 0.25f, ally.b * 0.25f, 0.85f));
            Round(rr, new Color(enemy.r * 0.25f, enemy.g * 0.25f, enemy.b * 0.25f, 0.85f));
            Fill(new Rect(lr.x + 8f, lr.yMax - 3f, lr.width - 16f, 3f), ally);
            Fill(new Rect(rr.x + 8f, rr.yMax - 3f, rr.width - 16f, 3f), enemy);
            if (Game.InMatch && sc != null)
            {
                Shadowed(new Rect(lr.x, lr.y + 4f, lr.width, 34f), scoreBarL, scoreNum, ally);
                Shadowed(new Rect(rr.x, rr.y + 4f, rr.width, 34f), scoreBarR, scoreNum, enemy);
            }
            else
            {
                Shadowed(new Rect(lr.x, lr.y + 8f, lr.width, 24f), "US", hudCenter, ally);
                Shadowed(new Rect(rr.x, rr.y + 8f, rr.width, 24f), "THEM", hudCenter, enemy);
            }
            // Alive pips under each box.
            int allyAlive = 0, allyAll = 0, enemyAlive = 0, enemyAll = 0;
            if (s.agents != null)
                foreach (AgentState a in s.agents)
                {
                    bool mine = a.team == Game.PlayerTeam;
                    if (mine) { allyAll++; if (a.alive) allyAlive++; }
                    else { enemyAll++; if (a.alive) enemyAlive++; }
                }
            Pips(new Rect(lr.x, lr.yMax - 16f, lr.width, 8f), allyAlive, allyAll, ally);
            Pips(new Rect(rr.x, rr.yMax - 16f, rr.width, 8f), enemyAlive, enemyAll, enemy);

            if (Game.InMatch)
            {
                int rk = Game.Round * 10 + (Game.RoundType ?? "").Length;
                if (rk != roundLineKey)
                {
                    roundLineKey = rk;
                    string tl = Weapons.RoundTypeLabel(Game.RoundType);
                    roundLine = "ROUND " + Game.Round + (tl.Length > 0 ? "  \u00b7  " + tl : "");
                }
                Shadowed(new Rect(W / 2f - 150f, clockR.yMax + 4f, 300f, 16f), roundLine, centerSmall, Theme.Ink);
                if (Game.BuyPhaseActive)
                {
                    int left = Mathf.CeilToInt(Game.BuyTimeLeft);
                    Shadowed(new Rect(W / 2f - 150f, clockR.yMax + 20f, 300f, 18f), Game.BuyOpen ? "BUY PHASE" : "BUY PHASE  \u00b7  B TO BUY  \u00b7  " + Theme.Int(left) + " s", centerSmall, Settings.Accent);
                }
            }
        }

        void Pips(Rect r, int alive, int all, Color c)
        {
            if (all <= 0) return;
            float pw = Mathf.Min(8f, (r.width - 8f) / all - 2f), gap = 2f;
            float tot = all * pw + (all - 1) * gap;
            float x0 = r.center.x - tot / 2f;
            for (int i = 0; i < all; i++)
                Fill(new Rect(x0 + i * (pw + gap), r.y + 2f, pw, 3f), i < alive ? c : new Color(1f, 1f, 1f, 0.15f));
        }

        string ammoStr = "", reserveStr = "", weaponLine = "", creditsHud = "", armorStr = "";
        int ammoKey = int.MinValue, reserveKey = int.MinValue, creditsHudKey = int.MinValue, armorKey = int.MinValue;
        string weaponLineFor;

        /// <summary>Bottom right: the weapon, its ammo, reload progress and the two slots.</summary>
        void DrawAmmo(AgentState me, float now)
        {
            if (me == null || !me.alive) return;
            Gunplay g = Game.Gun;
            WeaponInfo w = g.Weapon;
            var r = new Rect(W - 336f, H - 118f, 316f, 98f);
            Round(r, new Color(0.05f, 0.06f, 0.08f, 0.62f));
            Fill(new Rect(r.x, r.y + 10f, 3f, r.height - 20f), Settings.Accent);
            int ak = g.Ammo;
            if (ak != ammoKey)
            {
                ammoKey = ak;
                ammoStr = Theme.Int(ak);
            }
            int rk = g.Reserve * 1000 + w.Mag;
            if (rk != reserveKey)
            {
                reserveKey = rk;
                reserveStr = g.Reserve >= 0 ? "/ " + Theme.Int(g.Reserve) : "/ " + Theme.Int(w.Mag);
            }
            if (!ReferenceEquals(weaponLineFor, w.Id))
            {
                weaponLineFor = w.Id;
                weaponLine = w.Name.ToUpperInvariant() + "  \u00b7  " + w.ClassName.ToUpperInvariant();
            }
            bool low = g.Ammo <= Mathf.Max(1, w.Mag / 4);
            Shadowed(new Rect(r.x + 14f, r.y + 6f, r.width - 28f, 16f), weaponLine, capLeft, Color.white);
            Shadowed(new Rect(r.x + 10f, r.y + 14f, 96f, 52f), ammoStr, ammoBig, low ? Palette.Warn : Color.white);
            Shadowed(new Rect(r.x + 110f, r.y + 14f, 70f, 46f), reserveStr, ammoSmall, Theme.Muted);
            Tex(new Rect(r.xMax - 136f, r.y + 22f, 124f, 40f), GunModels.Icon(w.Id), new Color(1f, 1f, 1f, 0.9f));
            if (g.Reloading(now))
            {
                float p = g.ReloadProgress(now);
                var pr = new Rect(r.x + 14f, r.y + 68f, r.width - 28f, 4f);
                Fill(pr, new Color(1f, 1f, 1f, 0.12f));
                Fill(new Rect(pr.x, pr.y, pr.width * p, pr.height), Settings.Accent);
                Shadowed(new Rect(r.x + 14f, r.y + 72f, r.width - 28f, 18f), "RELOADING", capLeft, Settings.Accent);
            }
            else
            {
                // Slots: 1 primary, 2 secondary.
                string prim = g.Primary, sec = g.Secondary;
                SlotChip(new Rect(r.x + 14f, r.y + 70f, 140f, 20f), "1", prim != null ? Weapons.NameOf(prim) : "none", g.OnPrimary && prim != null, prim != null);
                SlotChip(new Rect(r.x + 160f, r.y + 70f, 140f, 20f), "2", Weapons.NameOf(sec), !g.OnPrimary || prim == null, true);
            }
            if (g.Ammo <= 0 && !g.Reloading(now)) Shadowed(new Rect(W / 2f - 150f, H / 2f + 40f, 300f, 22f), "R  RELOAD", hudCenter, Palette.Warn);
        }

        void SlotChip(Rect r, string key, string name, bool on, bool has)
        {
            Round(r, on ? new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, 0.85f) : new Color(1f, 1f, 1f, has ? 0.08f : 0.03f));
            Color tc = on ? new Color(0.05f, 0.05f, 0.06f) : has ? Theme.Ink : Theme.Faint;
            Lbl(new Rect(r.x + 8f, r.y + 1f, 16f, r.height), key, slotKey, tc);
            Lbl(new Rect(r.x + 24f, r.y + 3f, r.width - 28f, r.height), name.ToUpperInvariant(), cardSmall, tc);
        }

        /// <summary>Bottom left, above health: credits (a match or a drill with a loadout) and armour.</summary>
        void DrawCreditsArmor(AgentState me)
        {
            int c = Game.Credits;
            if (c >= 0)
            {
                if (c != creditsHudKey)
                {
                    creditsHudKey = c;
                    creditsHud = c.ToString("N0");
                }
                var cr = new Rect(20f, H - 158f, 170f, 28f);
                Round(cr, new Color(0.05f, 0.06f, 0.08f, 0.62f));
                // A small credit coin: a ring with a bar.
                Round(new Rect(cr.x + 8f, cr.y + 7f, 14f, 14f), Settings.Accent);
                Round(new Rect(cr.x + 11f, cr.y + 10f, 8f, 8f), new Color(0.05f, 0.06f, 0.08f, 1f));
                Fill(new Rect(cr.x + 14f, cr.y + 9f, 2f, 10f), Settings.Accent);
                Shadowed(new Rect(cr.x + 30f, cr.y + 4f, 130f, 20f), creditsHud, modeStyle, Color.white);
            }
            if (me == null || !me.alive) return;
            int pts = me.ArmorPoints;
            string kind = me.ArmorKind ?? (Game.InMatch ? Game.OwnedArmor : null);
            if (pts < 0 && !string.IsNullOrEmpty(kind)) pts = Weapons.ArmorPointsOf(kind);
            if (pts <= 0) return;
            if (pts != armorKey)
            {
                armorKey = pts;
                armorStr = Theme.Int(pts);
            }
            var ar = new Rect(184f, H - 86f, 64f, 40f);
            Round(new Rect(ar.x, ar.y + 6f, 18f, 22f), new Color(0.7f, 0.85f, 1f));
            Round(new Rect(ar.x + 3f, ar.y + 9f, 12f, 16f), new Color(0.05f, 0.06f, 0.08f, 1f));
            Shadowed(new Rect(ar.x + 24f, ar.y, 50f, 34f), armorStr, hudCenter, new Color(0.75f, 0.88f, 1f));
            GUI.Label(new Rect(ar.x, ar.y + 30f, 80f, 14f), "ARMOUR", capLeft);
        }

        /// <summary>Top right: kills as "killer [weapon] (headshot) victim", other events as text.</summary>
        void DrawKillFeed(float now)
        {
            float fy = 14f;
            for (int i = Game.Feed.Count - 1; i >= 0; i--)
            {
                FeedItem item = Game.Feed[i];
                float age = now - item.At;
                if (age > 7f) continue;
                float a = Mathf.Clamp01((7f - age) / 1f);
                float right = W - 16f;
                if (item.IsKill)
                {
                    if (item.KillerW < 0f) item.KillerW = killName.CalcSize(new GUIContent(item.Killer)).x;
                    if (item.VictimW < 0f) item.VictimW = killName.CalcSize(new GUIContent(item.Victim)).x;
                    const float iconW = 58f, headW = 18f;
                    float total = 12f + item.KillerW + 10f + iconW + (item.Head ? headW + 4f : 0f) + 10f + item.VictimW + 12f;
                    var r = new Rect(right - total, fy, total, 28f);
                    bool mine = item.Killer == "You" || item.Victim == "You";
                    Round(r, mine ? new Color(Settings.Accent.r * 0.3f, Settings.Accent.g * 0.3f, Settings.Accent.b * 0.3f, 0.75f * a) : new Color(0.05f, 0.06f, 0.08f, 0.62f * a));
                    if (mine) Fill(new Rect(r.x, r.y + 4f, 2f, r.height - 8f), new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, a));
                    float x = r.x + 12f;
                    Shadowed(new Rect(x, r.y, item.KillerW + 4f, r.height), item.Killer, killName, new Color(item.KillerColor.r, item.KillerColor.g, item.KillerColor.b, a));
                    x += item.KillerW + 10f;
                    Tex(new Rect(x, r.y + 5f, iconW, 18f), GunModels.Icon(item.Weapon), new Color(1f, 1f, 1f, 0.92f * a));
                    x += iconW + 4f;
                    if (item.Head)
                    {
                        // A headshot mark: a small ringed dot.
                        var hr = new Rect(x, r.y + 7f, 14f, 14f);
                        Round(hr, new Color(1f, 0.85f, 0.3f, a));
                        Round(new Rect(hr.x + 3f, hr.y + 3f, 8f, 8f), new Color(0.05f, 0.06f, 0.08f, a));
                        Round(new Rect(hr.x + 5f, hr.y + 5f, 4f, 4f), new Color(1f, 0.85f, 0.3f, a));
                        x += headW + 4f;
                    }
                    x += 6f;
                    Shadowed(new Rect(x, r.y, item.VictimW + 4f, r.height), item.Victim, killName, new Color(item.VictimColor.r, item.VictimColor.g, item.VictimColor.b, a));
                }
                else
                {
                    var r = new Rect(W - 516f, fy, 500f, 26f);
                    if (item.Width < 0f) item.Width = feed.CalcSize(new GUIContent(item.Text)).x;
                    Round(new Rect(r.xMax - item.Width - 16f, r.y, item.Width + 16f, r.height), new Color(0.05f, 0.06f, 0.08f, 0.62f * a));
                    Fill(new Rect(r.xMax - 3f, r.y + 4f, 3f, r.height - 8f), new Color(item.Color.r, item.Color.g, item.Color.b, a));
                    Shadowed(new Rect(r.x, r.y, r.width - 9f, r.height), item.Text, feed, new Color(1f, 1f, 1f, a));
                }
                fy += 32f;
            }
        }

        void DrawDamageNumbers(Camera cam, float now)
        {
            if (!Settings.DamageNumbers) return;
            List<DamageNumber> list = Game.DamageNumbers;
            for (int i = 0; i < list.Count; i++)
            {
                DamageNumber d = list[i];
                float age = now - d.At;
                if (age < 0f || age > 1f) continue;
                Vector3 sp = cam.WorldToScreenPoint(d.World);
                if (sp.z <= 0.1f) continue;
                float x = sp.x / scale, y = (Screen.height - sp.y) / scale - age * 36f;
                float a = Mathf.Clamp01((1f - age) / 0.35f);
                float pop = 1f + Mathf.Clamp01(1f - age / 0.12f) * 0.3f;
                GUIStyle st = d.Head ? dmgHead : dmgStyle;
                Color c = d.Head ? new Color(1f, 0.85f, 0.3f, a) : new Color(1f, 1f, 1f, a);
                Shadowed(new Rect(x - 60f * pop, y - 16f * pop, 120f * pop, 32f * pop), d.Text, st, c);
            }
        }

        /// <summary>The Longshot's scope: black outside a lens, fine cross hairs, a centre dot.</summary>
        void DrawScope()
        {
            Gunplay g = Game.Gun;
            if (g == null || !g.Weapon.Scopes || g.Scope01 < 0.9f || Event.current.type != EventType.Repaint) return;
            float size = H;
            float x0 = (W - size) / 2f;
            Fill(new Rect(0f, 0f, x0 + 1f, H), Color.black);
            Fill(new Rect(x0 + size - 1f, 0f, W - (x0 + size) + 1f, H), Color.black);
            GUI.DrawTexture(new Rect(x0, 0f, size, size), Textures.ScopeMask(), ScaleMode.StretchToFill, true);
            Color line = new Color(0f, 0f, 0f, 0.85f);
            float cx = W / 2f, cy = H / 2f;
            Fill(new Rect(x0 + size * 0.08f, cy - 0.75f, size * 0.84f, 1.5f), line);
            Fill(new Rect(cx - 0.75f, size * 0.08f, 1.5f, size * 0.84f), line);
            Fill(new Rect(x0 + size * 0.08f, cy - 2f, size * 0.25f, 4f), line);
            Fill(new Rect(x0 + size * 0.67f, cy - 2f, size * 0.25f, 4f), line);
            Fill(new Rect(cx - 2f, cy + size * 0.17f, 4f, size * 0.25f), line);
            Round(new Rect(cx - 2.5f, cy - 2.5f, 5f, 5f), new Color(1f, 0.25f, 0.2f, 0.95f));
        }
    }
}

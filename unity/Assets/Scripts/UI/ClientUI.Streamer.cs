// Streamer mode and the pro personas (docs/streamer-mode.md). The broadcast
// overlay over a round: the LIVE pill with a viewer count that rises with good
// plays, the goal bar, a camera-frame box with the player's agent, the chat
// panel (bridge `chat`: badge, coloured name, slide-in, auto-scroll, fade), big
// centre alerts with a sting (bridge `alert`, or made from kills on an older
// bridge) and persona callouts as speech captions (`persona_line`). Then the
// persona select grid, the duel setup and the duel end screen. Overlay parts
// use GUI.* only (the HUD runs without the layout pass).
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        GUIStyle chatStyle, alertStyle, alertSmall, calloutName, calloutText, liveStyle, personaName, tipStyle, quoteStyle;
        readonly Dictionary<string, FigureSpec[]> figureSets = new Dictionary<string, FigureSpec[]>();
        int chatSeen;
        float chatOffset;
        AlertItem alertShown;
        string viewersStr = "";
        int viewersKey = -1;

        void EnsureStreamerStyles()
        {
            if (chatStyle != null) return;
            GUIStyle lbl = GUI.skin.label;
            chatStyle = Text(lbl, 13, FontStyle.Normal, Color.white, TextAnchor.UpperLeft, true);
            chatStyle.richText = true;
            alertStyle = Text(lbl, 64, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            alertSmall = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            calloutName = Text(lbl, 14, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            calloutText = Text(lbl, 16, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft, true);
            liveStyle = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            personaName = Text(lbl, 40, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            tipStyle = Text(lbl, 13, FontStyle.Normal, Color.white, TextAnchor.UpperLeft, true);
            quoteStyle = Text(lbl, 14, FontStyle.Italic, Color.white, TextAnchor.UpperLeft, true);
        }

        /// <summary>Cached figure lists for the stage (built once per key).</summary>
        FigureSpec[] Figures(string key, int count, System.Func<int, FigureSpec> make)
        {
            if (figureSets.TryGetValue(key, out FigureSpec[] specs) && specs.Length == count) return specs;
            specs = new FigureSpec[count];
            for (int i = 0; i < count; i++) specs[i] = make(i);
            figureSets[key] = specs;
            return specs;
        }

        static string DefaultWeapon(string agent)
        {
            switch (agent)
            {
                case "rook": return "wasp";
                case "haze": return "lancer";
                case "wren": return "viper";
                case "pike": return "longshot";
                default: return Weapons.Default;
            }
        }

        /// <summary>Draws slot `i` of the stage into `r` (kept at the slot's aspect, centred); `crop` keeps the top part.</summary>
        void DrawFigure(Rect r, int i, float crop = 1f)
        {
            FigureStage st = Game.Stage;
            if (st == null || st.Texture == null || i >= st.Count || Event.current.type != EventType.Repaint) return;
            Rect uv = st.Uv(i);
            uv = new Rect(uv.x, uv.y + uv.height * (1f - crop), uv.width, uv.height * crop);
            float aspect = FigureStage.Aspect / crop;
            float w = r.width, h = r.width / aspect;
            if (h > r.height) { h = r.height; w = h * aspect; }
            var dr = new Rect(r.center.x - w / 2f, r.yMax - h, w, h);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, old.a);
            GUI.DrawTextureWithTexCoords(dr, st.Texture, uv, false);
            GUI.color = old;
        }

        // ---- the overlay ---------------------------------------------------------------------------
        /// <summary>The broadcast layer. `round` true over a live round (everything), false between rounds (chat and alerts).</summary>
        void DrawStreamerOverlay(float now, bool round)
        {
            EnsureStreamerStyles();
            float chatTop = 334f;
            float chatH = Mathf.Max(120f, H - chatTop - 136f - (round ? 100f : 0f));
            if (round)
            {
                // The broadcast column on the right, under the kill feed: LIVE and viewers, chat, the goal.
                DrawLivePill(new Vector2(W - 350f, chatTop - 34f), true);
                DrawGoal(new Rect(W - 350f, chatTop + chatH + 8f, 330f, 44f));
                DrawCamFrame();
                DrawCallouts(now);
            }
            DrawChat(new Rect(W - 350f, chatTop, 330f, chatH), now);
            DrawAlert(now);
        }

        /// <summary>The red LIVE pill, and with `viewers` the viewer count that flashes when it jumps.</summary>
        void DrawLivePill(Vector2 at, bool viewers)
        {
            EnsureStreamerStyles();
            float now = Time.unscaledTime;
            var pill = new Rect(at.x, at.y, 72f, 26f);
            Plate(pill, new Color(0.9f, 0.1f, 0.18f, 0.95f));
            float pulse = 0.55f + 0.45f * Mathf.Sin(now * 5f);
            Round(new Rect(pill.x + 10f, pill.y + 9f, 8f, 8f), new Color(1f, 1f, 1f, pulse));
            Lbl(new Rect(pill.x + 24f, pill.y, 48f, pill.height), "L I V E", liveStyle, Color.white);
            if (!viewers) return;
            int v = Game.Viewers;
            if (v != viewersKey)
            {
                viewersKey = v;
                viewersStr = v.ToString("N0") + "  W A T C H I N G";
            }
            float bump = 1f - Mathf.Clamp01((now - Game.ViewerBumpAt) / 0.8f);
            var vr = new Rect(pill.xMax + 6f, pill.y, 170f, 26f);
            Plate(vr, new Color(0.02f, 0.025f, 0.04f, 0.8f));
            if (bump > 0f) PlateLine(vr, new Color(Palette.Ok.r, Palette.Ok.g, Palette.Ok.b, bump));
            Lbl(new Rect(vr.x + 10f, vr.y, vr.width - 12f, vr.height), viewersStr, liveStyle, Color.Lerp(Theme.Ink, Palette.Ok, bump));
        }

        string goalText = "";
        int goalKey = int.MinValue;

        /// <summary>The stream goal: the duel, the match, or kills this session.</summary>
        void DrawGoal(Rect r)
        {
            int target, have;
            string what;
            if (Game.InDuel)
            {
                target = 5;
                have = Game.DuelScore?.player ?? 0;
                what = "Win 5 rounds against " + Game.DuelPersonaName;
            }
            else if (Game.InMatch)
            {
                target = 7;
                have = Game.Score?.player ?? 0;
                what = "Win 7 rounds";
            }
            else
            {
                target = 10;
                have = Game.SessionKills % 10 == 0 && Game.SessionKills > 0 ? 10 : Game.SessionKills % 10;
                what = "10 kills this session";
            }
            int key = target * 1000 + have + (Game.InDuel ? 100000 : 0);
            if (key != goalKey)
            {
                goalKey = key;
                goalText = what + "   " + Mathf.Min(have, target) + " / " + target;
            }
            Plate(r, new Color(0.02f, 0.025f, 0.04f, 0.8f));
            Lbl(new Rect(r.x + 10f, r.y + 4f, r.width - 20f, 14f), "S T R E A M   G O A L", tinyCaps, Settings.Accent);
            Lbl(new Rect(r.x + 10f, r.y + 16f, r.width - 20f, 16f), goalText, tinyCaps, Theme.Ink);
            var bar = new Rect(r.x + 10f, r.yMax - 9f, r.width - 20f, 4f);
            Fill(bar, new Color(1f, 1f, 1f, 0.1f));
            Neon(bar, Settings.Accent, Mathf.Clamp01(have / (float)Mathf.Max(1, target)));
        }

        /// <summary>The streamer's camera box: the player's agent figure (head and shoulders) with a broadcast frame.</summary>
        void DrawCamFrame()
        {
            string agent = Game.PlayerAgent ?? Game.PickedAgentId ?? "rook";
            float y = Settings.MinimapOn && !Game.FullMap ? 14f + Settings.MinimapSize + 30f : 150f;
            var r = new Rect(16f, y, 210f, 150f);
            if (r.yMax > H - 200f) return;
            if (!ReferenceEquals(camAgent, agent))
            {
                camAgent = agent;
                camKey = "cam:" + agent;
                camTag = "YOU  \u00b7  " + Agents.NameOf(agent).ToUpperInvariant();
                camSpecs = new[] { new FigureSpec { Look = agent, Accent = Agents.ColorOf(agent), Team = Settings.Ally, Weapon = DefaultWeapon(agent) } };
            }
            Game.Stage?.Use(camKey, camSpecs, true);
            if (Game.Stage != null) Game.Stage.Hero = -1;
            Plate(r, new Color(0.02f, 0.025f, 0.04f, 0.9f));
            DrawFigure(new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f), 0, 0.42f);
            Scanlines(r, 0.25f);
            Color ac = Agents.ColorOf(agent);
            Brackets(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), new Color(ac.r, ac.g, ac.b, 0.9f), 14f, 2f);
            float now = Time.unscaledTime;
            Round(new Rect(r.x + 10f, r.y + 10f, 8f, 8f), new Color(1f, 0.15f, 0.2f, Mathf.Repeat(now, 1.2f) < 0.8f ? 1f : 0.2f));
            Lbl(new Rect(r.x + 24f, r.y + 6f, 100f, 16f), "C A M   0 1", tinyCaps, Color.white);
            var tag = new Rect(r.x + 8f, r.yMax - 26f, r.width - 16f, 20f);
            Plate(tag, new Color(0.02f, 0.025f, 0.04f, 0.85f));
            Fill(new Rect(tag.x, tag.y, 3f, tag.height), ac);
            Lbl(new Rect(tag.x + 10f, tag.y, tag.width - 12f, tag.height), camTag, tinyCaps, Theme.Ink);
        }

        string camAgent, camKey, camTag;
        FigureSpec[] camSpecs;

        /// <summary>Chat, newest at the bottom: lines slide in, older ones scroll up and fade after 30 s.</summary>
        void DrawChat(Rect r, float now)
        {
            List<ChatLine> chat = Game.Chat;
            Plate(r, new Color(0.02f, 0.025f, 0.04f, 0.55f));
            PlateLine(r, new Color(1f, 1f, 1f, 0.06f));
            var head = new Rect(r.x, r.y, r.width, 26f);
            Fill(new Rect(head.x + 10f, head.yMax - 1f, head.width - 20f, 1f), new Color(1f, 1f, 1f, 0.08f));
            Round(new Rect(head.x + 12f, head.y + 9f, 8f, 8f), Palette.Spike);
            Lbl(new Rect(head.x + 26f, head.y, 200f, head.height), "S T R E A M   C H A T", tinyCaps, Theme.Ink);
            float innerW = r.width - 24f;
            // New lines push the stack up; the offset eases back to zero (the auto-scroll).
            if (chat.Count < chatSeen) chatSeen = 0;
            for (int i = chatSeen; i < chat.Count; i++) chatOffset += ChatHeight(chat[i], innerW) + 6f;
            chatSeen = chat.Count;
            chatOffset = Mathf.Lerp(chatOffset, 0f, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f));
            if (chat.Count == 0)
            {
                Lbl(new Rect(r.x + 12f, r.y + 34f, innerW, 40f), Game.V4 ? "Chat warms up as the round goes." : "Chat runs on a bridge that speaks bots-wire.4. Alerts still fire from your kills.", tipStyle, Theme.Muted);
                return;
            }
            var body = new Rect(r.x + 12f, r.y + 30f, innerW, r.height - 38f);
            GUI.BeginGroup(body);
            float y = body.height + chatOffset;
            for (int i = chat.Count - 1; i >= 0 && y > -40f; i--)
            {
                ChatLine l = chat[i];
                float age = now - l.At;
                if (age > 36f) break;
                float h = ChatHeight(l, innerW);
                y -= h + 6f;
                float k = Theme.Ease(age / 0.25f);
                float a = k * Mathf.Clamp01((36f - age) / 6f);
                if (a <= 0.01f || y > body.height) continue;
                float x = (1f - k) * 40f;
                var lr = new Rect(x, y, innerW, h);
                if (!string.IsNullOrEmpty(l.Badge))
                {
                    bool mod = l.Badge == "mod";
                    var chip = new Rect(x, y + 1f, mod ? 30f : 28f, 15f);
                    Plate(chip, mod ? new Color(0.2f, 0.8f, 0.4f, a) : new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, a));
                    Lbl(chip, mod ? "MOD" : "SUB", bugSmall, new Color(0.03f, 0.03f, 0.04f, a));
                }
                Lbl(lr, l.Composed, chatStyle, new Color(1f, 1f, 1f, a));
            }
            GUI.EndGroup();
        }

        /// <summary>Builds the line's rich text (the name in its colour, the badge's room) and measures it once.</summary>
        float ChatHeight(ChatLine l, float width)
        {
            if (l.Composed == null)
            {
                string hex = ColorUtility.ToHtmlStringRGB(l.Color);
                string pad = string.IsNullOrEmpty(l.Badge) ? "" : "         ";
                l.Composed = pad + "<b><color=#" + hex + ">" + Safe(l.User) + "</color></b>  " + Safe(l.Text);
            }
            if (l.Height < 0f) l.Height = Mathf.Max(16f, chatStyle.CalcHeight(new GUIContent(l.Composed), width));
            return l.Height;
        }

        /// <summary>Bridge text cannot inject rich-text tags.</summary>
        static string Safe(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.Replace('<', '\u2039').Replace('>', '\u203a');
        }

        static Color AlertColor(string kind)
        {
            switch (kind)
            {
                case "first_blood": return new Color(1f, 0.25f, 0.3f);
                case "multi_kill": return Settings.Accent;
                case "clutch": return new Color(1f, 0.8f, 0.25f);
                case "ace": return new Color(1f, 0.35f, 0.95f);
                case "eco_win": return new Color(0.35f, 1f, 0.55f);
                default: return Theme.Neon2;
            }
        }

        /// <summary>The centre alert: a slash band snaps in, the text pops and glitches, then everything slides away.</summary>
        void DrawAlert(float now)
        {
            AlertItem al = Game.Alert;
            if (al == null) return;
            if (!ReferenceEquals(al, alertShown))
            {
                alertShown = al;
                PlayUi(UiSound.Sting, al.Big ? 1f : 0.75f);
            }
            float age = now - al.At;
            float inK = Theme.Ease(age / 0.18f);
            float outK = Mathf.Clamp01((age - (al.Duration - 0.35f)) / 0.35f);
            float a = inK * (1f - outK);
            if (a <= 0f) return;
            Color c = AlertColor(al.Kind);
            float y = H * 0.19f;
            float bandW = (al.Big ? 760f : 560f) * inK;
            Slash(new Rect(W / 2f - bandW / 2f - outK * 80f, y, bandW, al.Big ? 96f : 76f), new Color(0.02f, 0.025f, 0.045f, 0.88f * a));
            Slash(new Rect(W / 2f - bandW / 2f, y - 5f, bandW * 0.7f, 4f), new Color(c.r, c.g, c.b, a));
            Slash(new Rect(W / 2f - bandW / 2f + bandW * 0.35f, y + (al.Big ? 96f : 76f) + 1f, bandW * 0.65f, 3f), new Color(Theme.Neon2.r, Theme.Neon2.g, Theme.Neon2.b, a));
            float pop = 1f + (1f - Theme.Ease(age / 0.25f)) * 0.25f;
            Matrix4x4 m = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), new Vector2(W / 2f, y + 40f) * scale);
            var tr = new Rect(0f, y + (al.Big ? 4f : 0f), W, al.Big ? 80f : 66f);
            if (age < 0.3f)
            {
                int seed = (int)(now * 40f);
                Condensed(new Rect(tr.x + (Hash01(seed) - 0.5f) * 16f, tr.y, tr.width, tr.height), Spaced(al.Text), alertStyle, new Color(Theme.Neon2.r, Theme.Neon2.g, Theme.Neon2.b, 0.6f * a), 0.8f);
            }
            Condensed(tr, Spaced(al.Text), alertStyle, new Color(c.r, c.g, c.b, a), 0.8f, true);
            GUI.matrix = m;
            Lbl(new Rect(0f, y - 22f, W, 14f), "S T R E A M   A L E R T", alertSmall, new Color(1f, 1f, 1f, 0.7f * a));
        }

        /// <summary>Persona callouts as speech captions above the ability bar, typed on.</summary>
        void DrawCallouts(float now)
        {
            List<Callout> list = Game.Callouts;
            float y = H - 232f;
            for (int i = list.Count - 1, shown = 0; i >= 0 && shown < 2; i--, shown++)
            {
                Callout c = list[i];
                float age = now - c.At;
                float a = Theme.Ease(age / 0.2f) * Mathf.Clamp01((5f - age) / 0.6f);
                if (a <= 0f) continue;
                int chars = Mathf.Clamp(Mathf.RoundToInt(c.Text.Length * Mathf.Clamp01(age / 0.7f)), 0, c.Text.Length);
                string shownText = chars >= c.Text.Length ? c.Text : c.Text.Substring(0, chars);
                var r = new Rect(W / 2f - 300f, y - shown * 52f, 600f, 44f);
                Plate(r, new Color(0.02f, 0.025f, 0.04f, 0.82f * a));
                Fill(new Rect(r.x, r.y, 4f, r.height), new Color(c.Color.r, c.Color.g, c.Color.b, a));
                var tag = new Rect(r.x + 14f, r.y + 6f, 120f, 14f);
                Lbl(tag, Spaced(c.Name), tinyCaps, new Color(c.Color.r, c.Color.g, c.Color.b, a));
                Lbl(new Rect(r.x + 14f, r.y + 18f, r.width - 28f, 24f), "\u201c" + shownText + "\u201d", calloutText, new Color(1f, 1f, 1f, a));
            }
        }

        // ---- persona select ------------------------------------------------------------------------
        void DrawPersonaSelect()
        {
            EnsureStreamerStyles();
            float now = Time.unscaledTime;
            float x0 = 60f, top = 40f;
            Kicker(new Rect(x0, top, 600f, 16f), "Pro personas  //  original playstyles, not real people", Settings.Accent);
            Condensed(new Rect(x0, top + 16f, 900f, 60f), Spaced("Pick your opponent"), displayL, Theme.Ink, 0.78f);
            Neon(new Rect(x0, top + 80f, 120f, 3f), Settings.Accent, Intro01);
            if (!Game.V4) Lbl(new Rect(x0 + 140f, top + 72f, W - x0 - 200f, 20f), "This bridge does not speak bots-wire.4: the cards show the built-in profiles and duels stay off.", small, Palette.Warn);

            List<Persona> all = Personas.All;
            int n = Mathf.Min(5, all.Count);
            float gap = 14f;
            float cw = (W - 2f * x0 - gap * (n - 1)) / Mathf.Max(1, n);
            float cy = top + 100f, ch = H - cy - 90f;
            FigureSpec[] specs = Figures("personas", n, i => new FigureSpec { Look = all[i].Id, Accent = all[i].Primary, Team = Settings.Enemy, Weapon = all[i].Weapon });
            Game.Stage?.Use("personas", specs);
            int hovered = -1;
            for (int i = 0; i < n; i++)
            {
                Persona p = all[i];
                float ik = Theme.Ease((now - screenAt - i * 0.06f) / 0.35f);
                var r = new Rect(x0 + i * (cw + gap), cy + (1f - ik) * 30f, cw, ch);
                bool hot = Hover(r);
                if (hot) hovered = i;
                float h = HoverAnim(5000 + i, hot);
                Color oldC = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, oldC.a * ik);
                Glass(r);
                PlateLine(r, new Color(p.Primary.r, p.Primary.g, p.Primary.b, 0.2f + 0.7f * h));
                Fill(new Rect(r.x + Theme.Cut, r.y, r.width - Theme.Cut - 4f, 3f), p.Primary);
                // The figure, on a backdrop in its two colours.
                var fr = new Rect(r.x + 8f, r.y + 10f, r.width - 16f, r.height * 0.44f);
                Fill(fr, new Color(p.Primary.r * 0.12f, p.Primary.g * 0.12f, p.Primary.b * 0.12f, 0.9f));
                DrawFigure(fr, i);
                Scanlines(fr, 0.15f);
                Tex(new Rect(fr.xMax - 28f, fr.y + 8f, 20f, 20f), UiTex.Role(Agents.Get(p.Agent)?.Role), new Color(1f, 1f, 1f, 0.7f));
                float y = fr.yMax + 10f;
                Condensed(new Rect(r.x + 14f, y, r.width - 28f, 44f), Upper(p.Name), personaName, p.Primary, 0.8f, true);
                y += 42f;
                Lbl(new Rect(r.x + 14f, y, r.width - 28f, 14f), Spaced(p.Style ?? ""), tinyCaps, Theme.Muted);
                y += 18f;
                Lbl(new Rect(r.x + 14f, y, r.width - 28f, 36f), "\u201c" + (p.Tagline ?? "") + "\u201d", quoteStyle, Theme.Ink);
                y += 38f;
                Plate(new Rect(r.x + 14f, y, 26f, 12f), p.Primary);
                Plate(new Rect(r.x + 44f, y, 26f, 12f), p.Secondary);
                Lbl(new Rect(r.x + 78f, y - 2f, r.width - 90f, 16f), Upper(Agents.NameOf(p.Agent)) + "  KIT", tinyCaps, Theme.Muted);
                y += 22f;
                Lbl(new Rect(r.x + 14f, y, r.width - 28f, 12f), "S I G N A T U R E", tinyCaps, p.Primary);
                Lbl(new Rect(r.x + 14f, y + 12f, r.width - 28f, 34f), p.Signature ?? "", bodyBold, Theme.Ink);
                y += 48f;
                float tipH = r.yMax - 64f - y;
                if (tipH > 40f)
                {
                    var tip = new Rect(r.x + 10f, y, r.width - 20f, tipH);
                    Plate(tip, new Color(1f, 1f, 1f, 0.04f));
                    Lbl(new Rect(tip.x + 10f, tip.y + 6f, tip.width - 20f, 12f), "H O W   T O   B E A T   T H E M", tinyCaps, Settings.Accent);
                    Lbl(new Rect(tip.x + 10f, tip.y + 20f, tip.width - 20f, tip.height - 24f), p.BeatTip ?? "", tipStyle, Theme.Muted);
                }
                GUI.color = oldC;
                string pid = p.Id;
                if (GButton(new Rect(r.x + 12f, r.yMax - 54f, r.width - 24f, 42f), "Duel " + p.Name, 1, Game.V4 && Idle)) Game.Do(() => Game.OpenDuel(pid));
            }
            if (Game.Stage != null) Game.Stage.Hero = hovered;
            float by = H - 70f;
            if (GButton(new Rect(x0, by, 160f, 44f), "Back", 2)) Game.Do(Game.OpenMenu);
            if (GButton(new Rect(W - x0 - 300f, by, 300f, 44f), "Persona match  (5v5)", 0, Game.V4 && Game.V3 && Idle)) Game.Do(Game.OpenPersonaMatch);
            Lbl(new Rect(x0 + 180f, by + 12f, W - 2f * x0 - 500f, 20f), "Each persona is an original character with its own look, lines and moves. No real player's name, likeness or voice is used.", small, Theme.Faint);
        }

        // ---- duel setup ----------------------------------------------------------------------------
        void DrawDuelSetup()
        {
            EnsureStreamerStyles();
            Persona p = Personas.Get(Game.DuelPersona);
            if (p == null)
            {
                Game.Do(Game.OpenPersonas);
                return;
            }
            FigureSpec[] specs = Figures("duel:" + p.Id, 1, i => new FigureSpec { Look = p.Id, Accent = p.Primary, Team = Settings.Enemy, Weapon = p.Weapon });
            Game.Stage?.Use("duel:" + p.Id, specs);
            if (Game.Stage != null) Game.Stage.Hero = 0;
            float k = Intro01;
            // Left: the persona, large.
            var fr = new Rect(40f, 40f, Mathf.Min(W * 0.42f, (H - 80f) * FigureStage.Aspect * 1.25f), H - 80f);
            Fill(fr, new Color(p.Primary.r * 0.1f, p.Primary.g * 0.1f, p.Primary.b * 0.1f, 0.85f));
            DrawFigure(fr, 0);
            Scanlines(fr, 0.15f);
            Brackets(new Rect(fr.x - 6f, fr.y - 6f, fr.width + 12f, fr.height + 12f), new Color(p.Primary.r, p.Primary.g, p.Primary.b, 0.8f * k), 22f, 2f);
            // Right: the setup.
            var area = new Rect(fr.xMax + 40f, 70f + (1f - k) * 14f, Mathf.Min(620f, W - fr.xMax - 80f), H - 140f);
            Frame(area, "Duel  //  1v1  //  first to 5");
            Glass(area);
            float x = area.x + 28f, y = area.y + 24f, w = area.width - 56f;
            Condensed(new Rect(x, y, w, 64f), Spaced(p.Name), displayL, p.Primary, 0.78f, true);
            Lbl(new Rect(x, y + 64f, w, 14f), Spaced(p.Style ?? ""), tinyCaps, Theme.Muted);
            Lbl(new Rect(x, y + 84f, w, 40f), "\u201c" + (p.Tagline ?? "") + "\u201d", quoteStyle, Theme.Ink);
            y += 136f;
            Kicker(new Rect(x, y, 200f, 16f), "Map", Settings.Accent);
            y += 22f;
            List<MapInfo> maps = Game.Maps;
            var shown = new List<MapInfo>();
            if (maps != null) foreach (MapInfo m in maps) if (m != null && !string.IsNullOrEmpty(m.id)) shown.Add(m);
            if (shown.Count == 0 && Game.Map != null) shown.Add(new MapInfo { id = Game.Map.id, name = Game.MapName });
            float mw = (w - 10f * (shown.Count - 1)) / Mathf.Max(1, shown.Count);
            for (int i = 0; i < shown.Count; i++)
            {
                string id = shown[i].id;
                if (GButton(new Rect(x + i * (mw + 10f), y, mw, 40f), shown[i].name ?? id, id == Game.DuelMapId ? 1 : 0)) Game.Do(() => Game.DuelMapId = id);
            }
            y += 56f;
            Kicker(new Rect(x, y, 200f, 16f), "Site", Settings.Accent);
            y += 22f;
            List<string> sites = Game.DuelSites();
            for (int i = 0; i < sites.Count; i++)
            {
                string s = sites[i];
                var sr = new Rect(x + i * 96f, y, 86f, 56f);
                bool sel = s == Game.DuelSite;
                float h = HoverAnim(KeyOf("site" + s, sr), Hover(sr));
                Color sc = Palette.Site(s);
                Plate(sr, sel ? new Color(sc.r * 0.35f, sc.g * 0.35f, sc.b * 0.35f, 0.95f) : Color.Lerp(Theme.CardC, Theme.ButtonHoverC, h));
                PlateLine(sr, new Color(sc.r, sc.g, sc.b, sel ? 1f : 0.25f + 0.5f * h));
                Condensed(new Rect(sr.x, sr.y, sr.width, sr.height), s, displayMC, sel ? sc : Theme.Ink, 0.9f);
                if (GUI.Button(sr, GUIContent.none, GUIStyle.none)) { PlayUi(UiSound.Confirm, 0.7f); Game.Do(() => Game.DuelSite = s); }
            }
            y += 76f;
            var tip = new Rect(x, y, w, 96f);
            Plate(tip, new Color(1f, 1f, 1f, 0.04f));
            Lbl(new Rect(tip.x + 12f, tip.y + 8f, tip.width - 24f, 12f), "C O A C H   T I P", tinyCaps, Settings.Accent);
            Lbl(new Rect(tip.x + 12f, tip.y + 24f, tip.width - 24f, tip.height - 28f), p.BeatTip ?? "", tipStyle, Theme.Ink);
            float by = area.yMax - 70f;
            if (Game.WaitingFor == "duel") Lbl(new Rect(x, by - 26f, w, 20f), "Starting the duel...", bodyBold, Theme.Muted);
            if (!Game.V4) Lbl(new Rect(x, by - 26f, w, 20f), "Duels need a bridge that speaks bots-wire.4.", bodyBold, Palette.Warn);
            if (GButton(new Rect(x, by, 150f, 46f), "Back", 2)) Game.Do(Game.OpenPersonas);
            if (GButton(new Rect(area.xMax - 28f - 260f, by, 260f, 46f), "Start duel", 1, Idle && Game.V4)) Game.Do(Game.StartDuel);
        }

        // ---- duel end ------------------------------------------------------------------------------
        DuelEndMsg statsFor;
        readonly List<string> duelStatKeys = new List<string>();
        readonly List<string> duelStatValues = new List<string>();

        void CacheDuelStats(DuelEndMsg m)
        {
            if (ReferenceEquals(m, statsFor)) return;
            statsFor = m;
            duelStatKeys.Clear();
            duelStatValues.Clear();
            if (m?.stats is JObject o)
            {
                foreach (JProperty prop in o.Properties())
                {
                    if (duelStatKeys.Count >= 6) break;
                    string v = Lenient.Text(prop.Value);
                    if (string.IsNullOrEmpty(v)) continue;
                    if (prop.Value.Type == JTokenType.Float) v = ((double)prop.Value).ToString("0.##");
                    duelStatKeys.Add(Spaced(System.Text.RegularExpressions.Regex.Replace(prop.Name, "([a-z])([A-Z])", "$1 $2")));
                    duelStatValues.Add(v);
                }
            }
        }

        void DrawDuelEnd()
        {
            EnsureStreamerStyles();
            DuelEndMsg de = Game.DuelEnd;
            Persona p = Personas.Get(Game.DuelPersona);
            string name = p != null ? p.Name : Game.DuelPersonaName;
            ScoreInfo sc = de != null && de.score is JObject ? new ScoreInfo { player = de.PlayerScore, enemy = de.EnemyScore } : Game.DuelScore;
            bool won = sc != null && sc.player > sc.enemy;
            Color c = won ? Palette.Ok : Palette.Bad;
            CacheDuelStats(de);
            Fill(new Rect(0f, 0f, W, H), new Color(0.01f, 0.015f, 0.03f, 0.55f));
            Rect area = Centered(860f, 620f);
            Frame(area, "Duel over  //  vs " + name);
            Glass(area);
            float k = Intro01;
            Neon(new Rect(area.x + Theme.Cut, area.y, area.width - Theme.Cut * 2f, 3f), c, k);
            Condensed(new Rect(area.x, area.y + 20f, area.width, 90f), Spaced(won ? "Victory" : "Defeat"), displayLC, c, 0.82f, true);
            Lbl(new Rect(area.x, area.y + 106f, area.width, 40f), sc != null ? sc.player + "  :  " + sc.enemy : "", scoreNum, Theme.Ink);
            Lbl(new Rect(area.x, area.y + 142f, area.width, 16f), "YOU  :  " + Upper(name), centerSmall, p != null ? p.Primary : Theme.Muted);

            var tip = new Rect(area.x + 28f, area.y + 176f, area.width - 56f, 130f);
            Plate(tip, new Color(1f, 1f, 1f, 0.05f));
            Fill(new Rect(tip.x, tip.y, 4f, tip.height), p != null ? p.Primary : Settings.Accent);
            Lbl(new Rect(tip.x + 16f, tip.y + 10f, tip.width - 32f, 14f), Spaced("How to beat " + name), tinyCaps, Settings.Accent);
            string beat = !string.IsNullOrEmpty(de?.beatTip) ? de.beatTip : p?.BeatTip ?? "";
            Lbl(new Rect(tip.x + 16f, tip.y + 30f, tip.width - 32f, tip.height - 36f), beat, debrief, Theme.Ink);

            float sy = tip.yMax + 18f;
            int ns = duelStatKeys.Count;
            if (ns > 0)
            {
                float cw = (area.width - 56f - 10f * (ns - 1)) / ns;
                for (int i = 0; i < ns; i++)
                {
                    var r = new Rect(area.x + 28f + i * (cw + 10f), sy, cw, 84f);
                    Plate(r, Theme.CardC);
                    Condensed(new Rect(r.x, r.y + 8f, r.width, 44f), duelStatValues[i], displayMC, Theme.Ink, 0.88f);
                    Lbl(new Rect(r.x + 4f, r.y + 54f, r.width - 8f, 22f), duelStatKeys[i], bugSmall, Theme.Muted);
                }
            }
            else Lbl(new Rect(area.x + 40f, sy + 20f, area.width - 80f, 40f), "The coach adds this duel to your report.", centerBody, Theme.Muted);

            float by = area.yMax - 72f;
            if (GButton(new Rect(area.x + 28f, by, 180f, 46f), "Personas", 2)) Game.Do(Game.BackFromDuelEnd);
            if (GButton(new Rect(area.x + 220f, by, 160f, 46f), "Menu", 2)) Game.Do(() => { Game.BackFromDuelEnd(); Game.OpenMenu(); });
            if (GButton(new Rect(area.xMax - 28f - 260f, by, 260f, 46f), "Rematch", 1, Idle && Game.V4)) Game.Do(Game.StartDuel);
        }
    }
}

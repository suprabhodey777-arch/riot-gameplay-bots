// Streamer mode and the pro personas (docs/streamer-mode.md). The overlay over
// a round: the LIVE pill with a viewer count that rises with good plays, the
// goal bar, a camera-frame box with the player's agent, the chat panel (bridge
// `chat`: badge, coloured name, slide-in, auto-scroll, fade), centre alerts
// (bridge `alert`, or made from kills on an older bridge) and persona callouts
// as speech captions (`persona_line`). Then the persona select cards (with the
// player's custom names, editable in place), the duel setup and the duel end
// screen. GUI.* calls only. Persona names always come from Persona.Name, which
// is the player's custom name when one is set.
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
        /// <summary>The persona whose name is being edited on the select screen, or null.</summary>
        string renaming;

        void EnsureStreamerStyles()
        {
            if (chatStyle != null) return;
            GUIStyle lbl = GUI.skin.label;
            chatStyle = Text(lbl, 14, FontStyle.Normal, Color.white, TextAnchor.UpperLeft, true);
            chatStyle.richText = true;
            alertStyle = Text(lbl, 64, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            alertSmall = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            calloutName = Text(lbl, 15, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            calloutText = Text(lbl, 17, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft, true);
            liveStyle = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            personaName = Text(lbl, 40, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            tipStyle = Text(lbl, 14, FontStyle.Normal, Color.white, TextAnchor.UpperLeft, true);
            quoteStyle = Text(lbl, 15, FontStyle.Italic, Color.white, TextAnchor.UpperLeft, true);
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
            float chatTop = 334f;
            float chatH = Mathf.Max(120f, H - chatTop - 136f - (round ? 100f : 0f));
            if (round)
            {
                // The broadcast column on the right, under the kill feed: LIVE and viewers, chat, the goal.
                DrawLivePill(new Vector2(W - 354f, chatTop - 36f), true);
                DrawGoal(new Rect(W - 354f, chatTop + chatH + 8f, 334f, 46f));
                DrawCamFrame();
                DrawCallouts(now);
            }
            DrawChat(new Rect(W - 354f, chatTop, 334f, chatH), now);
            DrawAlert(now);
        }

        /// <summary>The red LIVE pill, and with `viewers` the viewer count that flashes when it jumps.</summary>
        void DrawLivePill(Vector2 at, bool viewers)
        {
            float now = Time.unscaledTime;
            var pill = new Rect(at.x, at.y, 76f, 28f);
            Plate(pill, new Color(0.9f, 0.1f, 0.18f, 0.95f));
            float pulse = 0.55f + 0.45f * Mathf.Sin(now * 5f);
            Plate(new Rect(pill.x + 10f, pill.y + 10f, 8f, 8f), new Color(1f, 1f, 1f, pulse));
            Lbl(new Rect(pill.x + 24f, pill.y, 50f, pill.height), "LIVE", liveStyle, Color.white);
            if (!viewers) return;
            int v = Game.Viewers;
            if (v != viewersKey)
            {
                viewersKey = v;
                viewersStr = v.ToString("N0") + "  W A T C H I N G";
            }
            float bump = 1f - Mathf.Clamp01((now - Game.ViewerBumpAt) / 0.8f);
            var vr = new Rect(pill.xMax + 6f, pill.y, 186f, 28f);
            Plate(vr, new Color(0.02f, 0.025f, 0.04f, 0.82f));
            if (bump > 0f) PlateLine(vr, new Color(Palette.Ok.r, Palette.Ok.g, Palette.Ok.b, bump));
            Lbl(new Rect(vr.x + 10f, vr.y, vr.width - 12f, vr.height), viewersStr, liveStyle, Color.Lerp(Theme.Ink, Palette.Ok, bump));
        }

        string goalText = "";
        int goalKey = int.MinValue;
        string goalPersonaName;

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
            if (key != goalKey || (Game.InDuel && !string.Equals(goalPersonaName, Game.DuelPersonaName)))
            {
                goalKey = key;
                goalPersonaName = Game.DuelPersonaName;
                goalText = what + "   " + Mathf.Min(have, target) + " / " + target;
            }
            Plate(r, new Color(0.02f, 0.025f, 0.04f, 0.82f));
            Lbl(new Rect(r.x + 12f, r.y + 4f, r.width - 24f, 16f), "STREAM GOAL", tinyCaps, Settings.Accent);
            Lbl(new Rect(r.x + 12f, r.y + 18f, r.width - 24f, 18f), goalText, tinyCaps, Theme.Ink);
            var bar = new Rect(r.x + 12f, r.yMax - 9f, r.width - 24f, 4f);
            Fill(bar, new Color(1f, 1f, 1f, 0.1f));
            Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(have / (float)Mathf.Max(1, target)), bar.height), Settings.Accent);
        }

        /// <summary>The streamer's camera box: the player's agent figure (head and shoulders) with a camera frame.</summary>
        void DrawCamFrame()
        {
            string agent = Game.PlayerAgent ?? Game.PickedAgentId ?? "rook";
            float y = Settings.MinimapOn && !Game.FullMap ? 18f + Settings.MinimapSize + 30f : 150f;
            var r = new Rect(20f, y, 214f, 152f);
            if (r.yMax > H - 250f) return;
            if (!string.Equals(camAgent, agent))
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
            Scanlines(r, 0.12f);
            Color ac = Agents.ColorOf(agent);
            Brackets(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), new Color(ac.r, ac.g, ac.b, 0.9f), 14f, 2f);
            float now = Time.unscaledTime;
            Plate(new Rect(r.x + 10f, r.y + 10f, 8f, 8f), new Color(1f, 0.15f, 0.2f, Mathf.Repeat(now, 1.2f) < 0.8f ? 1f : 0.2f));
            Lbl(new Rect(r.x + 24f, r.y + 5f, 110f, 18f), "CAM 01", tinyCaps, Color.white);
            var tag = new Rect(r.x + 8f, r.yMax - 28f, r.width - 16f, 22f);
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
            Plate(r, new Color(0.02f, 0.025f, 0.04f, 0.6f));
            PlateLine(r, new Color(1f, 1f, 1f, 0.06f));
            var head = new Rect(r.x, r.y, r.width, 28f);
            Fill(new Rect(head.x + 10f, head.yMax - 1f, head.width - 20f, 1f), new Color(1f, 1f, 1f, 0.08f));
            Plate(new Rect(head.x + 12f, head.y + 10f, 8f, 8f), Palette.Spike);
            Lbl(new Rect(head.x + 26f, head.y, 220f, head.height), "STREAM CHAT", tinyCaps, Theme.Ink);
            float innerW = r.width - 24f;
            // New lines push the stack up; the offset eases back to zero (the auto-scroll).
            if (chat.Count < chatSeen) chatSeen = 0;
            for (int i = chatSeen; i < chat.Count; i++) chatOffset += ChatHeight(chat[i], innerW) + 6f;
            chatSeen = chat.Count;
            chatOffset = Mathf.Lerp(chatOffset, 0f, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f));
            if (chat.Count == 0)
            {
                Lbl(new Rect(r.x + 12f, r.y + 36f, innerW, 44f), Game.V4 ? "Chat warms up as the round goes." : "Chat runs on a bridge that speaks bots-wire.4. Alerts still fire from your kills.", tipStyle, Theme.Muted);
                return;
            }
            var body = new Rect(r.x + 12f, r.y + 32f, innerW, r.height - 40f);
            BeginClip(body);
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
                    var chip = new Rect(x, y + 1f, mod ? 32f : 30f, 16f);
                    Plate(chip, mod ? new Color(0.2f, 0.8f, 0.4f, a) : new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, a));
                    Lbl(chip, mod ? "MOD" : "SUB", bugSmall, new Color(0.03f, 0.03f, 0.04f, a));
                }
                Lbl(lr, l.Composed, chatStyle, new Color(1f, 1f, 1f, a));
            }
            EndClip();
        }

        /// <summary>Builds the line's rich text (the name in its colour, the badge's room) and measures it once.</summary>
        float ChatHeight(ChatLine l, float width)
        {
            if (l.Composed == null)
            {
                string hex = ColorUtility.ToHtmlStringRGB(l.Color);
                string pad = string.IsNullOrEmpty(l.Badge) ? "" : "          ";
                l.Composed = pad + "<b><color=#" + hex + ">" + Safe(l.User) + "</color></b>  " + Safe(l.Text);
            }
            if (l.Height < 0f) l.Height = Mathf.Max(18f, chatStyle.CalcHeight(new GUIContent(l.Composed), width));
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

        /// <summary>The centre alert: a slanted band snaps in, the text pops, then everything slides away.</summary>
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
            float bandH = al.Big ? 96f : 76f;
            float bandW = (al.Big ? 760f : 560f) * inK;
            Slash(new Rect(W / 2f - bandW / 2f - outK * 80f, y, bandW, bandH), new Color(0.02f, 0.025f, 0.045f, 0.88f * a));
            Slash(new Rect(W / 2f - bandW / 2f, y - 5f, bandW * 0.7f, 4f), new Color(c.r, c.g, c.b, a));
            float pop = 1f + (1f - Theme.Ease(age / 0.25f)) * 0.2f;
            Matrix4x4 m = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), new Vector2(W / 2f, y + 40f) * scale);
            var tr = new Rect(0f, y + (al.Big ? 4f : 0f), W, al.Big ? 80f : 66f);
            Condensed(tr, Spaced(al.Text), alertStyle, new Color(c.r, c.g, c.b, a), 0.8f, true);
            GUI.matrix = m;
            Lbl(new Rect(0f, y - 24f, W, 16f), "STREAM ALERT", alertSmall, new Color(1f, 1f, 1f, 0.7f * a));
        }

        /// <summary>Persona callouts as speech captions above the ability bar, typed on.</summary>
        void DrawCallouts(float now)
        {
            List<Callout> list = Game.Callouts;
            float y = H - 236f;
            for (int i = list.Count - 1, shown = 0; i >= 0 && shown < 2; i--, shown++)
            {
                Callout c = list[i];
                float age = now - c.At;
                float a = Theme.Ease(age / 0.2f) * Mathf.Clamp01((5f - age) / 0.6f);
                if (a <= 0f || string.IsNullOrEmpty(c.Text)) continue;
                int chars = Mathf.Clamp(Mathf.RoundToInt(c.Text.Length * Mathf.Clamp01(age / 0.7f)), 0, c.Text.Length);
                string shownText = chars >= c.Text.Length ? c.Text : c.Text.Substring(0, chars);
                var r = new Rect(W / 2f - 320f, y - shown * 56f, 640f, 48f);
                Plate(r, new Color(0.02f, 0.025f, 0.04f, 0.84f * a));
                Fill(new Rect(r.x + 2f, r.y + 8f, 3f, r.height - 16f), new Color(c.Color.r, c.Color.g, c.Color.b, a));
                Lbl(new Rect(r.x + 16f, r.y + 5f, r.width - 32f, 16f), Spaced(c.Name), tinyCaps, new Color(c.Color.r, c.Color.g, c.Color.b, a));
                Lbl(new Rect(r.x + 16f, r.y + 20f, r.width - 32f, 26f), "\u201c" + shownText + "\u201d", calloutText, new Color(1f, 1f, 1f, a));
            }
        }

        // ---- persona select ------------------------------------------------------------------------
        void DrawPersonaSelect()
        {
            float now = Time.unscaledTime;
            float x0 = 80f, top = 56f;
            float cy = Header(x0, top, W - 2f * x0 - 200f, "Favourite streamer", "Pick your streamer",
                Game.V4 ? "Each persona plays one well-known style. Rename any of them: the name is used everywhere in the client."
                        : "This bridge does not speak bots-wire.4: the cards show the built-in profiles and duels stay off.");

            List<Persona> all = Personas.All;
            int n = Mathf.Min(5, all.Count);
            float gap = 16f;
            float cw = (W - 2f * x0 - gap * (n - 1)) / Mathf.Max(1, n);
            float ch = H - cy - 112f;
            FigureSpec[] specs = Figures("personas", n, i => new FigureSpec { Look = all[i].Id, Accent = all[i].Primary, Team = Settings.Enemy, Weapon = all[i].Weapon });
            Game.Stage?.Use("personas", specs);
            int hovered = -1;
            for (int i = 0; i < n; i++)
            {
                Persona p = all[i];
                float ik = Theme.Ease((now - screenAt - i * 0.06f) / 0.35f);
                var r = new Rect(x0 + i * (cw + gap), cy + (1f - ik) * 30f, cw, ch);
                int duelKey = StableKey("duel:" + p.Id);
                bool hot = keyNav ? focusId == duelKey || focusId == StableKey("rename:" + p.Id) : Hover(r);
                if (hot) hovered = i;
                float h = Anim(5000 + i, hot);
                Color oldC = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, oldC.a * ik);
                Plate(r, Color.Lerp(Theme.PanelC, Theme.CardC, h));
                PlateLine(r, new Color(1f, 1f, 1f, 0.10f + 0.14f * h));
                Fill(new Rect(r.x, r.y, r.width, 2f), new Color(p.Primary.r, p.Primary.g, p.Primary.b, 0.35f + 0.65f * h));
                // The figure, on the stage's own dark.
                var fr = new Rect(r.x + 1f, r.y + 2f, r.width - 2f, r.height * 0.42f);
                Fill(fr, Theme.StageC);
                DrawFigure(fr, i);
                Tex(new Rect(fr.xMax - 30f, fr.y + 8f, 22f, 22f), UiTex.Role(Agents.Get(p.Agent)?.Role), new Color(1f, 1f, 1f, 0.7f));
                float y = fr.yMax + 12f;
                float nameW = r.width - 32f - 40f;
                if (renaming == p.Id)
                {
                    PersonaNameField(new Rect(r.x + 16f, y + 2f, r.width - 32f - 48f, 40f), p);
                    bool done = fieldCommitted;
                    if (GButton(new Rect(r.xMax - 16f - 40f, y + 2f, 40f, 40f), "", 1, true, StableKey("rename:" + p.Id), false, UiTex.Glyph("check"))) done = true;
                    if (done) EndRename();
                }
                else
                {
                    FitCondensed(new Rect(r.x + 16f, y, nameW, 44f), Upper(p.Name), personaName, p.Primary, 0.8f, true);
                    if (GButton(new Rect(r.xMax - 16f - 36f, y + 4f, 36f, 36f), "", 2, true, StableKey("rename:" + p.Id), false, UiTex.Glyph("edit"))) BeginRename(p.Id);
                }
                y += 46f;
                string original = string.IsNullOrEmpty(Personas.CustomName(p.Id)) ? "" : "   \u00b7   ORIGINALLY " + Upper(p.DefaultName);
                Lbl(new Rect(r.x + 16f, y, r.width - 32f, 18f), Upper(p.Style ?? "") + original, tinyCaps, Theme.Muted);
                y += 24f;
                Lbl(new Rect(r.x + 16f, y, r.width - 32f, 40f), "\u201c" + (p.Tagline ?? "") + "\u201d", quoteStyle, Theme.Ink);
                y += 44f;
                Plate(new Rect(r.x + 16f, y + 2f, 26f, 12f), p.Primary);
                Plate(new Rect(r.x + 46f, y + 2f, 26f, 12f), p.Secondary);
                Lbl(new Rect(r.x + 80f, y - 1f, r.width - 96f, 18f), Upper(Agents.NameOf(p.Agent)) + "  KIT", tinyCaps, Theme.Muted);
                y += 26f;
                Lbl(new Rect(r.x + 16f, y, r.width - 32f, 16f), "SIGNATURE", tinyCaps, Theme.Muted);
                Lbl(new Rect(r.x + 16f, y + 16f, r.width - 32f, 40f), p.Signature ?? "", bodyBold, Theme.Ink);
                y += 58f;
                float tipH = r.yMax - 68f - y;
                if (tipH > 44f)
                {
                    var tip = new Rect(r.x + 12f, y, r.width - 24f, tipH);
                    Fill(new Rect(tip.x + 4f, tip.y, tip.width - 8f, 1f), Theme.Line);
                    Lbl(new Rect(tip.x + 4f, tip.y + 10f, tip.width - 8f, 16f), "HOW TO BEAT THEM", tinyCaps, Theme.Muted);
                    BeginClip(new Rect(tip.x + 4f, tip.y + 28f, tip.width - 8f, tip.height - 30f));
                    Lbl(new Rect(0f, 0f, tip.width - 8f, tip.height - 30f), p.BeatTip ?? "", tipStyle, Theme.Ink);
                    EndClip();
                }
                GUI.color = oldC;
                string did = p.Id;
                if (GButton(new Rect(r.x + 14f, r.yMax - 58f, r.width - 28f, 44f), "Duel " + p.Name, 0, Game.V4 && Idle, duelKey)) Game.Do(() => Game.OpenDuel(did));
            }
            if (Game.Stage != null) Game.Stage.Hero = hovered;
            float by = H - 48f - Theme.ButtonH;
            if (GButton(new Rect(x0, by, 180f, Theme.ButtonH), "Back", 2, true, 0, false, UiTex.Glyph("back"))) Game.Do(Game.OpenMenu);
            if (GButton(new Rect(W - x0 - 320f, by, 320f, Theme.ButtonH), "Persona match  (5v5)", 0, Game.V4 && Game.V3 && Idle)) Game.Do(Game.OpenPersonaMatch);
            Lbl(new Rect(x0 + 200f, by, W - 2f * x0 - 540f, Theme.ButtonH), "Original characters with their own look, lines and moves. No real player's name, likeness or voice is used.", smallMid, Theme.Faint);
        }

        /// <summary>Starts editing persona `id`'s name in place (call from OnGUI); the field takes the focus when drawn.</summary>
        void BeginRename(string id)
        {
            renaming = id;
            nameEdits.Remove(id);
            pendingFocus = "pname:" + id;
        }

        /// <summary>Ends the in-place edit (call from OnGUI) and writes the names to disk.</summary>
        void EndRename()
        {
            renaming = null;
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
            PlayerPrefs.Save();
        }

        // ---- duel setup ----------------------------------------------------------------------------
        void DrawDuelSetup()
        {
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
            var fr = new Rect(48f, 48f, Mathf.Min(W * 0.4f, (H - 96f) * FigureStage.Aspect * 1.25f), H - 96f);
            Fill(fr, Theme.StageC);
            DrawFigure(fr, 0);
            PlateLine(fr, Theme.Line);
            Fill(new Rect(fr.x, fr.y, fr.width, 2f), p.Primary);
            Lbl(new Rect(fr.x + 16f, fr.yMax - 34f, fr.width - 32f, 20f), Spaced("Duel  \u00b7  1v1  \u00b7  first to 5"), tinyCaps, Theme.Ink);
            // Right: the setup.
            var area = new Rect(fr.xMax + 40f, 70f + (1f - k) * 12f, Mathf.Min(660f, W - fr.xMax - 88f), H - 140f);
            Frame(area, "Duel setup");
            Glass(area);
            float x = area.x + Pad, y = area.y + 26f, w = area.width - 2f * Pad;
            FitCondensed(new Rect(x, y, w, 64f), Spaced(p.Name), displayL, p.Primary, 0.78f, true);
            Lbl(new Rect(x, y + 66f, w, 18f), Spaced(p.Style ?? ""), tinyCaps, Theme.Muted);
            Lbl(new Rect(x, y + 88f, w, 40f), "\u201c" + (p.Tagline ?? "") + "\u201d", quoteStyle, Theme.Ink);
            y += 140f;
            y = SectionLabel(x, y, w, "Map");
            List<MapInfo> shown = ShownMaps();
            float mw = (w - 10f * (shown.Count - 1)) / Mathf.Max(1, shown.Count);
            for (int i = 0; i < shown.Count; i++)
            {
                string id = shown[i].id;
                if (GButton(new Rect(x + i * (mw + 10f), y, mw, 44f), shown[i].name ?? id, 0, true, StableKey("duel-map:" + id), id == Game.DuelMapId)) Game.Do(() => Game.DuelMapId = id);
            }
            y += 44f + Theme.S5;
            y = SectionLabel(x, y, w, "Site");
            List<string> sites = Game.DuelSites();
            for (int i = 0; i < sites.Count; i++)
            {
                string s = sites[i];
                var sr = new Rect(x + i * 100f, y, 88f, 60f);
                bool sel = s == Game.DuelSite;
                int key = StableKey("duel-site:" + s);
                float h = CardHover(sr, key, true);
                Color sc = Palette.Site(s);
                Plate(sr, sel ? new Color(sc.r * 0.35f, sc.g * 0.35f, sc.b * 0.35f, 0.95f) : Color.Lerp(Theme.CardC, Theme.ButtonHoverC, h));
                PlateLine(sr, new Color(sc.r, sc.g, sc.b, sel ? 1f : 0.25f + 0.5f * h));
                Condensed(new Rect(sr.x, sr.y, sr.width, sr.height), s, displayMC, sel ? sc : Theme.Ink, 0.9f);
                if (CardHit(sr, key, true)) Game.Do(() => Game.DuelSite = s);
            }
            y += 60f + Theme.S5;
            float tipH = Mathf.Min(120f, area.yMax - Pad - Theme.ButtonH - 40f - y);
            if (tipH > 50f)
            {
                var tip = new Rect(x, y, w, tipH);
                Plate(tip, new Color(1f, 1f, 1f, 0.04f));
                Fill(new Rect(tip.x + 2f, tip.y + 10f, 3f, tip.height - 20f), Settings.Accent);
                Lbl(new Rect(tip.x + 18f, tip.y + 10f, tip.width - 36f, 16f), "COACH TIP", tinyCaps, Settings.Accent);
                Lbl(new Rect(tip.x + 18f, tip.y + 30f, tip.width - 36f, tip.height - 34f), p.BeatTip ?? "", tipStyle, Theme.Ink);
            }
            float by = area.yMax - Pad - Theme.ButtonH;
            if (Game.WaitingFor == "duel") Lbl(new Rect(x, by - 30f, w, 22f), "Starting the duel...", bodyBold, Theme.Muted);
            if (!Game.V4) Lbl(new Rect(x, by - 30f, w, 22f), "Duels need a bridge that speaks bots-wire.4.", bodyBold, Palette.Warn);
            if (GButton(new Rect(x, by, 160f, Theme.ButtonH), "Back", 2, true, 0, false, UiTex.Glyph("back"))) Game.Do(Game.OpenPersonas);
            if (GButton(new Rect(area.xMax - Pad - 280f, by, 280f, Theme.ButtonH), "Start duel", 1, Idle && Game.V4, 0, false, UiTex.Glyph("play"), "Enter")) Game.Do(Game.StartDuel);
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
            DuelEndMsg de = Game.DuelEnd;
            Persona p = Personas.Get(Game.DuelPersona);
            string name = p != null ? p.Name : Game.DuelPersonaName;
            ScoreInfo sc = de != null && de.score is JObject ? new ScoreInfo { player = de.PlayerScore, enemy = de.EnemyScore } : Game.DuelScore;
            bool won = sc != null && sc.player > sc.enemy;
            Color c = won ? Palette.Ok : Palette.Bad;
            CacheDuelStats(de);
            Fill(new Rect(0f, 0f, W, H), new Color(0.01f, 0.015f, 0.03f, 0.55f));
            Rect area = Centered(900f, 640f);
            Frame(area, "Duel over");
            Glass(area);
            float k = Intro01;
            Fill(new Rect(area.x + Theme.Cut, area.y, (area.width - Theme.Cut * 2f) * k, 3f), c);
            Condensed(new Rect(area.x, area.y + 20f, area.width, 90f), Spaced(won ? "Victory" : "Defeat"), displayLC, c, 0.82f, true);
            Lbl(new Rect(area.x, area.y + 106f, area.width, 40f), sc != null ? sc.player + "  :  " + sc.enemy : "", scoreNum, Theme.Ink);
            Lbl(new Rect(area.x, area.y + 144f, area.width, 18f), "YOU  :  " + Upper(name), centerSmall, p != null ? p.Primary : Theme.Muted);

            var tip = new Rect(area.x + Pad, area.y + 180f, area.width - 2f * Pad, 134f);
            Card(tip, new Color(0f, 0f, 0f, 0f));
            Fill(new Rect(tip.x + 2f, tip.y + 12f, 3f, tip.height - 24f), p != null ? p.Primary : Settings.Accent);
            Lbl(new Rect(tip.x + 20f, tip.y + 12f, tip.width - 40f, 16f), Spaced("How to beat them"), tinyCaps, Settings.Accent);
            string beat = !string.IsNullOrEmpty(de?.beatTip) ? Personas.Rename(de.beatTip) : p?.BeatTip ?? "";
            Lbl(new Rect(tip.x + 20f, tip.y + 34f, tip.width - 40f, tip.height - 40f), beat, debrief, Theme.Ink);

            float sy = tip.yMax + 20f;
            int ns = duelStatKeys.Count;
            if (ns > 0)
            {
                float cw = (area.width - 2f * Pad - 10f * (ns - 1)) / ns;
                for (int i = 0; i < ns; i++)
                {
                    var r = new Rect(area.x + Pad + i * (cw + 10f), sy, cw, 88f);
                    Card(r, new Color(1f, 1f, 1f, 0.12f));
                    FitCondensed(new Rect(r.x + 6f, r.y + 8f, r.width - 12f, 44f), duelStatValues[i], displayMC, Theme.Ink, 0.88f);
                    Lbl(new Rect(r.x + 4f, r.y + 56f, r.width - 8f, 22f), duelStatKeys[i], bugSmall, Theme.Muted);
                }
            }
            else Lbl(new Rect(area.x + 40f, sy + 20f, area.width - 80f, 40f), "The coach adds this duel to your report.", centerBody, Theme.Muted);

            float by = area.yMax - Pad - Theme.ButtonH;
            if (GButton(new Rect(area.x + Pad, by, 180f, Theme.ButtonH), "Personas", 2, true, 0, false, UiTex.Glyph("back"))) Game.Do(Game.BackFromDuelEnd);
            if (GButton(new Rect(area.x + Pad + 192f, by, 160f, Theme.ButtonH), "Menu", 2)) Game.Do(() => { Game.BackFromDuelEnd(); Game.OpenMenu(); });
            if (GButton(new Rect(area.xMax - Pad - 260f, by, 260f, Theme.ButtonH), "Rematch", 1, Idle && Game.V4, 0, false, UiTex.Glyph("play"), "Enter")) Game.Do(Game.StartDuel);
        }
    }
}

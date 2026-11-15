// The broadcast design language shared by every screen: angular glass plates
// with cut corners, condensed display type (the built-in font squeezed with
// GUI.matrix), letter-spaced kickers, neon lines that grow in, corner brackets,
// animated buttons with hover ticks, a scanline backdrop, the glitch transition
// between screens and the launch sting (the game's name, skippable).
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        GUIStyle plateStroke, displayXL, displayL, displayM, displayLC, displayMC, titleDisplay, btnText, btnTextDark, kickerStyle, stingTitle, tinyCaps;
        AudioSource uiAudio;
        readonly Dictionary<int, float> hoverAnim = new Dictionary<int, float>();
        int hoverKey, lastHoverKey;
        readonly Dictionary<string, string> upperCache = new Dictionary<string, string>();
        readonly Dictionary<string, string> spacedCache = new Dictionary<string, string>();
        float stingStart = -1f, stingEnd = -1f;
        bool stingDone, stingHit;
        AppScreen soundScreen = (AppScreen)(-1);

        void EnsureBroadcastStyles()
        {
            GUIStyle lbl = GUI.skin.label;
            plateStroke = new GUIStyle { border = Theme.Border(Theme.Cut) };
            plateStroke.normal.background = Own(Theme.Chamfer(new Color(1f, 1f, 1f, 0f), Theme.Cut, Color.white, 1.4f));
            displayXL = Text(lbl, 96, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            displayL = Text(lbl, 54, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            displayM = Text(lbl, 30, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            displayLC = Text(lbl, 54, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            displayMC = Text(lbl, 30, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            titleDisplay = Text(lbl, 36, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            btnText = Text(lbl, 15, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            btnTextDark = Text(lbl, 15, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            kickerStyle = Text(lbl, 11, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            stingTitle = Text(lbl, 110, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            tinyCaps = Text(lbl, 10, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
        }

        // ---- strings -------------------------------------------------------------------------------
        /// <summary>Upper-case, cached (no allocation after the first call per string).</summary>
        string Upper(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (!upperCache.TryGetValue(s, out string u))
            {
                u = s.ToUpperInvariant();
                if (upperCache.Count > 512) upperCache.Clear();
                upperCache[s] = u;
            }
            return u;
        }

        /// <summary>Upper-case with tracking: a space between letters, three between words ("L I V E"). Cached.</summary>
        string Spaced(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (spacedCache.TryGetValue(s, out string r)) return r;
            var sb = new System.Text.StringBuilder(s.Length * 2);
            string u = s.ToUpperInvariant();
            for (int i = 0; i < u.Length; i++)
            {
                char ch = u[i];
                if (ch == ' ') sb.Append("  ");
                else
                {
                    sb.Append(ch);
                    if (i < u.Length - 1) sb.Append(' ');
                }
            }
            r = sb.ToString();
            if (spacedCache.Count > 512) spacedCache.Clear();
            spacedCache[s] = r;
            return r;
        }

        static float Hash01(int n)
        {
            unchecked
            {
                n = (n << 13) ^ n;
                return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483647f;
            }
        }

        // ---- sound ---------------------------------------------------------------------------------
        void PlayUi(AudioClip clip, float volume = 1f)
        {
            if (clip == null) return;
            if (uiAudio == null)
            {
                uiAudio = gameObject.AddComponent<AudioSource>();
                uiAudio.playOnAwake = false;
                uiAudio.spatialBlend = 0f;
                uiAudio.ignoreListenerPause = true;
            }
            uiAudio.PlayOneShot(clip, volume * 0.8f);
        }

        /// <summary>Hover ticks: one per newly hovered control (called at the end of each repaint).</summary>
        void FinishHoverSounds()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (hoverKey != 0 && hoverKey != lastHoverKey) PlayUi(UiSound.Tick, 0.55f);
            lastHoverKey = hoverKey;
            hoverKey = 0;
        }

        /// <summary>0..1 hover animation for control `key`, eased toward `hot`; registers the hover for its tick.</summary>
        float HoverAnim(int key, bool hot)
        {
            hoverAnim.TryGetValue(key, out float v);
            if (Event.current.type == EventType.Repaint)
            {
                v = Mathf.MoveTowards(v, hot ? 1f : 0f, Time.unscaledDeltaTime * 7f);
                hoverAnim[key] = v;
                if (hot) hoverKey = key;
            }
            return v;
        }

        static int KeyOf(string label, Rect r)
        {
            unchecked
            {
                int h = string.IsNullOrEmpty(label) ? 17 : label.GetHashCode();
                return (h * 397) ^ ((int)r.x * 7919) ^ (int)r.width | 1;
            }
        }

        // ---- shapes --------------------------------------------------------------------------------
        /// <summary>An angular plate (cut top-left and bottom-right corners).</summary>
        void Plate(Rect r, Color c)
        {
            Round(r, c);
        }

        /// <summary>The 1.4 px outline of an angular plate.</summary>
        void PlateLine(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint || plateStroke == null) return;
            if (Mathf.Min(r.width, r.height) < 26f)
            {
                Fill(new Rect(r.x, r.y, r.width, 1f), c);
                Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
                Fill(new Rect(r.x, r.y, 1f, r.height), c);
                Fill(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
                return;
            }
            Color old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            plateStroke.Draw(r, false, false, false, false);
            GUI.color = old;
        }

        /// <summary>A dark glass panel: translucent angular plate, a faint outline and a top highlight.</summary>
        void Glass(Rect r, float a = 1f)
        {
            Plate(r, new Color(Theme.PanelC.r, Theme.PanelC.g, Theme.PanelC.b, Theme.PanelC.a * a));
            PlateLine(r, new Color(1f, 1f, 1f, 0.09f * a));
            Fill(new Rect(r.x + Theme.Cut + 4f, r.y + 1f, Mathf.Max(0f, r.width * 0.4f), 1f), new Color(1f, 1f, 1f, 0.14f * a));
        }

        /// <summary>A neon line that grows from the left as `k` goes 0 to 1, with a soft halo and a hot leading tip.</summary>
        void Neon(Rect r, Color c, float k)
        {
            if (k <= 0.001f || Event.current.type != EventType.Repaint) return;
            float e = Theme.Ease(k);
            var g = new Rect(r.x, r.y, r.width * e, r.height);
            Color old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, 0.5f * c.a * old.a);
            GUI.DrawTexture(new Rect(g.x - 4f, g.center.y - 8f, g.width + 8f, 16f), UiTex.NeonHalo, ScaleMode.StretchToFill, true);
            GUI.color = old;
            Fill(g, c);
            if (e < 0.995f) Fill(new Rect(g.xMax - 12f, g.y - 1f, 12f, g.height + 2f), new Color(1f, 1f, 1f, 0.9f * c.a));
        }

        /// <summary>Corner brackets around `r`.</summary>
        void Brackets(Rect r, Color c, float len = 14f, float th = 2f)
        {
            if (Event.current.type != EventType.Repaint) return;
            Fill(new Rect(r.x, r.y, len, th), c);
            Fill(new Rect(r.x, r.y, th, len), c);
            Fill(new Rect(r.xMax - len, r.y, len, th), c);
            Fill(new Rect(r.xMax - th, r.y, th, len), c);
            Fill(new Rect(r.x, r.yMax - th, len, th), c);
            Fill(new Rect(r.x, r.yMax - len, th, len), c);
            Fill(new Rect(r.xMax - len, r.yMax - th, len, th), c);
            Fill(new Rect(r.xMax - th, r.yMax - len, th, len), c);
        }

        /// <summary>A slanted parallelogram strip (a broadcast slash), drawn as rotated fills.</summary>
        void Slash(Rect r, Color c, float skewDeg = -18f)
        {
            if (Event.current.type != EventType.Repaint) return;
            Matrix4x4 m = GUI.matrix;
            // A shear is not available on the GUI matrix through GUIUtility, so build it directly.
            Vector2 pivot = new Vector2(r.center.x * scale, r.center.y * scale);
            float sh = Mathf.Tan(skewDeg * Mathf.Deg2Rad);
            Matrix4x4 shear = Matrix4x4.identity;
            shear.m01 = sh;
            GUI.matrix = Matrix4x4.Translate(new Vector3(pivot.x, pivot.y, 0f)) * shear * Matrix4x4.Translate(new Vector3(-pivot.x, -pivot.y, 0f)) * m;
            Fill(r, c);
            GUI.matrix = m;
        }

        /// <summary>Letter-spaced small caps with a short accent bar in front.</summary>
        void Kicker(Rect r, string text, Color c)
        {
            Fill(new Rect(r.x, r.center.y - 5f, 3f, 10f), c);
            Lbl(new Rect(r.x + 10f, r.y, r.width - 10f, r.height), Spaced(text), kickerStyle, c);
        }

        /// <summary>
        /// Condensed display type: the label squeezed horizontally to `k` of its width around its anchor (the built-in
        /// font has no condensed cut). `r` is the box it must fit after squeezing.
        /// </summary>
        void Condensed(Rect r, string text, GUIStyle s, Color c, float k = 0.84f, bool shadow = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            TextAnchor a = s.alignment;
            bool centre = a == TextAnchor.UpperCenter || a == TextAnchor.MiddleCenter || a == TextAnchor.LowerCenter;
            bool right = a == TextAnchor.UpperRight || a == TextAnchor.MiddleRight || a == TextAnchor.LowerRight;
            float px = centre ? r.center.x : right ? r.xMax : r.x;
            float w = r.width / k;
            var rr = new Rect(centre ? px - w / 2f : right ? px - w : px, r.y, w, r.height);
            Matrix4x4 m = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(k, 1f), new Vector2(px * scale, r.y * scale));
            if (shadow) Shadowed(rr, text, s, c);
            else Lbl(rr, text, s, c);
            GUI.matrix = m;
        }

        /// <summary>Scanlines over `r` at opacity `a`.</summary>
        void Scanlines(Rect r, float a)
        {
            if (a <= 0f || Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a * old.a);
            GUI.DrawTextureWithTexCoords(r, UiTex.Scanlines, new Rect(0f, 0f, 1f, r.height * scale / 3f), true);
            GUI.color = old;
        }

        /// <summary>
        /// An angular button: plate, accent outline that brightens on hover, an underline sweep, a tick on hover and a
        /// confirm sound on click. `kind` 0 normal, 1 primary (accent fill), 2 ghost.
        /// </summary>
        bool GButton(Rect r, string label, int kind = 0, bool enabled = true, int key = 0)
        {
            bool on = enabled && GUI.enabled;
            if (key == 0) key = KeyOf(label, r);
            float h = HoverAnim(key, on && Hover(r));
            Color ac = Settings.Accent;
            float a = on ? 1f : 0.38f;
            Color bg;
            if (kind == 1) bg = Color.Lerp(ac, Theme.Lighten(ac, 0.22f), h);
            else if (kind == 2) bg = new Color(1f, 1f, 1f, 0.03f + 0.07f * h);
            else bg = Color.Lerp(Theme.ButtonC, Theme.ButtonHoverC, h);
            bg.a *= a;
            Plate(r, bg);
            if (kind != 1) PlateLine(r, new Color(ac.r, ac.g, ac.b, (0.14f + 0.6f * h) * a));
            if (h > 0.01f) Fill(new Rect(r.x + 4f, r.yMax - 3f, (r.width - 8f) * Theme.Ease(h), 2f), kind == 1 ? new Color(1f, 1f, 1f, 0.75f) : ac);
            Color tc = kind == 1 ? new Color(0.04f, 0.04f, 0.05f, a) : new Color(1f, 1f, 1f, a * (0.82f + 0.18f * h));
            Lbl(new Rect(r.x + h * 3f, r.y, r.width, r.height), Upper(label), kind == 1 ? btnTextDark : btnText, tc);
            if (!on) return false;
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                PlayUi(kind == 2 ? UiSound.Back : UiSound.Confirm, 0.8f);
                return true;
            }
            return false;
        }

        /// <summary>A GUILayout form of GButton.</summary>
        bool LButton(string label, int kind, float height, params GUILayoutOption[] o)
        {
            var opts = new List<GUILayoutOption>(o) { GUILayout.Height(height) };
            Rect r = GUILayoutUtility.GetRect(new GUIContent(Upper(label)), btnText, opts.ToArray());
            return GButton(r, label, kind);
        }

        // ---- screen furniture ---------------------------------------------------------------------
        /// <summary>The broadcast backdrop behind menu screens: deep vignette, scanlines and a slow scan band.</summary>
        void Backdrop(float strength)
        {
            if (Event.current.type != EventType.Repaint) return;
            var full = new Rect(0f, 0f, W, H);
            Color old = GUI.color;
            GUI.color = new Color(0.01f, 0.014f, 0.03f, 0.9f * strength * old.a);
            GUI.DrawTexture(full, UiTex.Vignette, ScaleMode.StretchToFill, true);
            float y = Mathf.Repeat(Time.unscaledTime * 70f, H + 300f) - 150f;
            GUI.color = new Color(1f, 1f, 1f, 0.035f * strength * old.a);
            GUI.DrawTexture(new Rect(0f, y, W, 140f), UiTex.NeonHalo, ScaleMode.StretchToFill, true);
            GUI.color = old;
            Scanlines(full, 0.16f * strength);
        }

        /// <summary>Decoration around a GUILayout panel (draw before BeginArea): shadow, neon top line, brackets.</summary>
        void Frame(Rect r, string kick = null)
        {
            if (Event.current.type != EventType.Repaint) return;
            float k = Intro01;
            Color ac = Settings.Accent;
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f * old.a);
            GUI.DrawTexture(new Rect(r.x - 40f, r.y - 30f, r.width + 80f, r.height + 70f), UiTex.Glow, ScaleMode.StretchToFill, true);
            GUI.color = old;
            Neon(new Rect(r.x + Theme.Cut + 6f, r.y - 4f, r.width * 0.45f, 2f), ac, k);
            Neon(new Rect(r.xMax - 90f, r.y - 4f, 70f, 2f), Theme.Neon2, Mathf.Clamp01(k * 1.4f - 0.4f));
            Brackets(new Rect(r.x - 7f, r.y - 7f, r.width + 14f, r.height + 14f), new Color(ac.r, ac.g, ac.b, 0.55f * k), 18f, 2f);
            if (!string.IsNullOrEmpty(kick)) Kicker(new Rect(r.x + 4f, r.y - 26f, r.width, 16f), kick, new Color(ac.r, ac.g, ac.b, k));
        }

        /// <summary>A GUILayout title: optional kicker, condensed upper-case display type, a neon underline.</summary>
        void TitleBlock(string text, string kick = null)
        {
            if (!string.IsNullOrEmpty(kick))
            {
                Rect kr = GUILayoutUtility.GetRect(10f, 16f, GUILayout.ExpandWidth(true));
                Kicker(kr, kick, Settings.Accent);
            }
            Rect r = GUILayoutUtility.GetRect(10f, 44f, GUILayout.ExpandWidth(true));
            Condensed(r, Upper(text), titleDisplay, Theme.Ink, 0.84f);
            Rect l = GUILayoutUtility.GetRect(10f, 8f, GUILayout.ExpandWidth(true));
            Neon(new Rect(l.x, l.y + 2f, 64f, 3f), Settings.Accent, Intro01);
            Fill(new Rect(l.x + 70f, l.y + 3f, 10f, 1f), new Color(1f, 1f, 1f, 0.35f * Intro01));
        }

        /// <summary>The glitch transition laid over a screen for 0.4 s after it opens.</summary>
        void DrawTransition(float now)
        {
            float t = now - screenAt;
            const float Dur = 0.4f;
            if (t < 0f || t > Dur || Event.current.type != EventType.Repaint) return;
            float k = 1f - t / Dur;
            int seed = (int)(now * 45f);
            Color ac = Settings.Accent;
            for (int i = 0; i < 10; i++)
            {
                float y = Hash01(seed * 31 + i * 7) * H;
                float h = 2f + Hash01(seed * 17 + i * 3) * 18f * k;
                float dx = (Hash01(seed * 13 + i) - 0.5f) * 80f * k;
                float pick = Hash01(seed * 5 + i * 11);
                Color c = pick < 0.4f ? ac : pick < 0.75f ? Theme.Neon2 : Color.white;
                c.a = (0.08f + 0.22f * Hash01(seed + i * 19)) * k;
                Fill(new Rect(dx, y, W, h), c);
            }
            // A dark band tearing across, and the scanlines settling.
            float band = Hash01(seed * 3) * H;
            Fill(new Rect(0f, band, W, 30f * k), new Color(0f, 0f, 0f, 0.35f * k));
            Scanlines(new Rect(0f, 0f, W, H), 0.45f * k);
            if (t < 0.05f) Fill(new Rect(0f, 0f, W, H), new Color(1f, 1f, 1f, 0.07f));
        }

        /// <summary>True while the launch sting is on screen.</summary>
        bool StingActive => !stingDone;

        /// <summary>Skips the sting on any key or click (call at the top of OnGUI, before the screens see the event).</summary>
        void StingInput(float now)
        {
            if (stingDone || stingStart < 0f) return;
            EventType et = Event.current.type;
            if (et == EventType.KeyDown || et == EventType.MouseDown)
            {
                if (stingEnd < 0f) stingEnd = now;
                Event.current.Use();
            }
        }

        /// <summary>The launch sting: neon lines rush in, the wordmark glitches together, then the credit types on.</summary>
        void DrawSting(float now)
        {
            if (stingDone) return;
            if (stingStart < 0f)
            {
                stingStart = now;
                PlayUi(UiSound.Whoosh, 0.9f);
            }
            float t = now - stingStart;
            const float Dur = 3.6f;
            float fade = stingEnd >= 0f ? Mathf.Clamp01((now - stingEnd) / 0.3f) : Mathf.Clamp01((t - (Dur - 0.55f)) / 0.55f);
            if (fade >= 1f)
            {
                stingDone = true;
                screenAt = now;
                return;
            }
            float a = 1f - fade;
            Fill(new Rect(0f, 0f, W, H), new Color(0.01f, 0.012f, 0.02f, a));
            if (!stingHit && t >= 0.55f)
            {
                stingHit = true;
                if (stingEnd < 0f) PlayUi(UiSound.Hit, 0.9f);
            }
            Color ac = Settings.Accent;
            float cx = W / 2f, cy = H / 2f;
            // Lines rush in from both edges and meet behind the wordmark.
            float lk = Mathf.Clamp01((t - 0.1f) / 0.5f);
            float e = Theme.Ease(lk);
            Fill(new Rect(0f, cy - 70f, cx * e, 2f), new Color(ac.r, ac.g, ac.b, a));
            Fill(new Rect(W - cx * e, cy + 68f, cx * e, 2f), new Color(Theme.Neon2.r, Theme.Neon2.g, Theme.Neon2.b, a));
            if (lk > 0f && lk < 1f)
            {
                Fill(new Rect(cx * e - 30f, cy - 71f, 30f, 4f), new Color(1f, 1f, 1f, a));
                Fill(new Rect(W - cx * e, cy + 67f, 30f, 4f), new Color(1f, 1f, 1f, a));
            }
            // The wordmark: glitched copies in the two neons, settling into white.
            float wk = Mathf.Clamp01((t - 0.5f) / 0.4f);
            if (wk > 0f)
            {
                var wr = new Rect(0f, cy - 60f, W, 120f);
                string word = Spaced("Saltworks");
                int seed = (int)(now * 30f);
                float jitter = (1f - wk) * 26f + (t > 0.9f && Hash01(seed) > 0.93f ? 10f : 0f);
                if (jitter > 0.5f)
                {
                    Condensed(new Rect(wr.x + (Hash01(seed + 1) - 0.5f) * jitter, wr.y, wr.width, wr.height), word, stingTitle, new Color(ac.r, ac.g, ac.b, 0.7f * a), 0.8f);
                    Condensed(new Rect(wr.x - (Hash01(seed + 2) - 0.5f) * jitter, wr.y, wr.width, wr.height), word, stingTitle, new Color(Theme.Neon2.r, Theme.Neon2.g, Theme.Neon2.b, 0.7f * a), 0.8f);
                }
                Condensed(wr, word, stingTitle, new Color(1f, 1f, 1f, a * wk), 0.8f);
                if (wk < 1f)
                    for (int i = 0; i < 5; i++)
                        Fill(new Rect(0f, wr.y + Hash01(seed * 7 + i) * wr.height, W, 3f + 6f * Hash01(seed + i)), new Color(0.01f, 0.012f, 0.02f, a * 0.8f));
            }
            // The credit types on under it.
            float ck = Mathf.Clamp01((t - 1.25f) / 0.6f);
            if (ck > 0f)
            {
                string credit = Spaced("Gameplay Coach");
                int n = Mathf.Clamp(Mathf.RoundToInt(credit.Length * ck), 0, credit.Length);
                bool caret = ck < 1f || Mathf.Repeat(now, 0.8f) < 0.4f;
                Lbl(new Rect(0f, cy + 86f, W, 24f), credit.Substring(0, n) + (caret ? " _" : ""), centerSmall, new Color(1f, 1f, 1f, 0.9f * a));
                Lbl(new Rect(0f, cy + 110f, W, 20f), Spaced("Tactical training, built from code"), centerSmall, new Color(Theme.Muted.r, Theme.Muted.g, Theme.Muted.b, a * Mathf.Clamp01((t - 1.9f) / 0.4f)));
            }
            Lbl(new Rect(0f, H - 40f, W, 18f), "PRESS ANY KEY TO SKIP", centerSmall, new Color(1f, 1f, 1f, 0.35f * a));
            Scanlines(new Rect(0f, 0f, W, H), 0.3f * a);
        }

        /// <summary>A whoosh when the screen changes (not for the HUD, and not during the sting).</summary>
        void ScreenSounds()
        {
            if (Event.current.type != EventType.Repaint || Game.Screen == soundScreen) return;
            AppScreen prev = soundScreen;
            soundScreen = Game.Screen;
            if (!stingDone || (int)prev < 0) return;
            if (Game.Screen != AppScreen.Playing) PlayUi(UiSound.Whoosh, 0.45f);
        }
    }
}

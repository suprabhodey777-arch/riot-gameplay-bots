// The design language shared by every screen: angular glass plates with cut
// corners (top-left and bottom-right), condensed display type (the built-in
// font squeezed with GUI.matrix), letter-spaced kickers, thin accent rules,
// soft depth, a quiet vignette backdrop, a short fade-and-rise transition
// between screens and the launch sting (the game's name, skippable). Clarity
// first: no glitch copies, no scanlines over menus.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        GUIStyle navItem, plateStroke, displayXL, displayL, displayM, displayLC, displayMC, titleDisplay, btnText, btnTextL, btnTextDark, kickerStyle, stingTitle, tinyCaps;
        GUIStyle segText, sectionStyle, subStyle, chipStyle, fieldStyle, sliderBare, titleSmall, bodyBoldMid, cardTitleL, smallMid, siteLetter, calloutStyle, bodyMid, valueStyle;
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
            plateStroke = new GUIStyle { border = Theme.Border(Theme.PrimaryCut) };
            plateStroke.normal.background = Own(Theme.Chamfer(new Color(1f, 1f, 1f, 0f), Theme.PrimaryCut, Color.white, 1.2f, false));
            displayXL = Text(lbl, 96, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            displayL = Text(lbl, 54, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            displayM = Text(lbl, 32, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            displayLC = Text(lbl, 54, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            displayMC = Text(lbl, 32, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            titleDisplay = Text(lbl, 40, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            navItem = DisplayText(lbl, 20, TextAnchor.MiddleLeft);
            btnText = DisplayText(lbl, 18, TextAnchor.MiddleCenter);
            btnTextL = DisplayText(lbl, 18, TextAnchor.MiddleLeft);
            btnTextDark = DisplayText(lbl, 18, TextAnchor.MiddleCenter);
            kickerStyle = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            stingTitle = Text(lbl, 110, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            tinyCaps = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            segText = DisplayText(lbl, 15, TextAnchor.MiddleCenter);
            sectionStyle = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            subStyle = Text(lbl, Theme.Body, FontStyle.Normal, Color.white, TextAnchor.UpperLeft, true);
            chipStyle = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            titleSmall = Text(lbl, 28, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            bodyBoldMid = Text(lbl, Theme.Body, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            cardTitleL = Text(lbl, 24, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            smallMid = Text(lbl, Theme.Small, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft, false);
            siteLetter = Text(lbl, 20, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            calloutStyle = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            bodyMid = Text(lbl, Theme.Body, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft, false);
            valueStyle = Text(lbl, Theme.Body, FontStyle.Bold, Color.white, TextAnchor.MiddleRight, false);

            fieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 17,
                font = Theme.BodyBoldFont,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                richText = false,
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(2, 2, 2, 2),
                margin = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, 0, 0),
            };
            foreach (GUIStyleState st in new[] { fieldStyle.normal, fieldStyle.hover, fieldStyle.active, fieldStyle.focused, fieldStyle.onNormal, fieldStyle.onHover, fieldStyle.onActive, fieldStyle.onFocused })
            {
                st.background = null;
                st.textColor = Theme.Ink;
            }

            // The slider without its track (SliderCtl draws the track and fill); padding centres the 6 px thumb in 24 px.
            sliderBare = new GUIStyle(slider) { fixedHeight = 0f, padding = new RectOffset(0, 0, 9, 9), margin = new RectOffset(0, 0, 0, 0), border = new RectOffset(0, 0, 0, 0) };
            sliderBare.normal.background = null;
            sliderBare.hover.background = null;
            sliderBare.active.background = null;
            sliderBare.focused.background = null;
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

        /// <summary>Upper-case with tracking: a space between letters, three between words ("LIVE"). Cached.</summary>
        string Spaced(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // The display and label faces carry their own spacing: tracking by inserted spaces read as a prototype.
            if (s != null) return Upper(s);
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
            if (hoverKey != 0 && hoverKey != lastHoverKey) PlayUi(UiSound.Tick, 0.45f);
            lastHoverKey = hoverKey;
            hoverKey = 0;
        }

        /// <summary>A 0..1 value for `key` eased toward `on` (no sound).</summary>
        float Anim(int key, bool on, float speed = 7f)
        {
            hoverAnim.TryGetValue(key, out float v);
            if (Event.current.type == EventType.Repaint)
            {
                v = Mathf.MoveTowards(v, on ? 1f : 0f, Time.unscaledDeltaTime * speed);
                hoverAnim[key] = v;
            }
            return v;
        }

        /// <summary>0..1 hover animation for control `key`, eased toward `hot`; registers the hover for its tick.</summary>
        float HoverAnim(int key, bool hot)
        {
            float v = Anim(key, hot);
            if (hot && Event.current.type == EventType.Repaint) hoverKey = key;
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
            if (Event.current.type != EventType.Repaint || plateStroke == null || c.a <= 0.001f) return;
            Fill(new Rect(r.x, r.y, r.width, 1f), c);
            Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
            Fill(new Rect(r.x, r.y + 1f, 1f, r.height - 2f), c);
            Fill(new Rect(r.xMax - 1f, r.y + 1f, 1f, r.height - 2f), c);
        }

        /// <summary>The outline of the primary button's shape (one chamfer, bottom right).</summary>
        void PrimaryLine(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint || plateStroke == null || c.a <= 0.001f) return;
            if (Mathf.Min(r.width, r.height) < 26f)
            {
                PlateLine(r, c);
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
            PlateLine(r, new Color(1f, 1f, 1f, 0.10f * a));
        }

        /// <summary>An accent rule that grows from the left as `k` goes 0 to 1, with a soft halo.</summary>
        void Neon(Rect r, Color c, float k)
        {
            if (k <= 0.001f || Event.current.type != EventType.Repaint) return;
            float e = Theme.Ease(k);
            var g = new Rect(r.x, r.y, r.width * e, r.height);
            Fill(g, c);
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

        /// <summary>A slanted parallelogram strip, drawn as a sheared fill.</summary>
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
            Lbl(r, Upper(text), kickerStyle, c);
        }

        /// <summary>
        /// Condensed display type: the label squeezed horizontally to `k` of its width around its anchor (the built-in
        /// font has no condensed cut). `r` is the box it must fit after squeezing.
        /// </summary>
        void Condensed(Rect r, string text, GUIStyle s, Color c, float k = 0.84f, bool shadow = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            // The display face is condensed already: squeeze only as far as a long string needs to fit.
            k = Mathf.Clamp(r.width / Mathf.Max(1f, TextW(text, s)), 0.6f, 1f);
            if (k > 0.985f)
            {
                if (shadow) Shadowed(r, text, s, c);
                else Lbl(r, text, s, c);
                return;
            }
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

        /// <summary>Faint scanlines over `r` (only the streamer camera frame uses them, as a "camera" cue).</summary>
        void Scanlines(Rect r, float a)
        {
            if (a <= 0f || Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a * old.a);
            GUI.DrawTextureWithTexCoords(r, UiTex.Scanlines, new Rect(0f, 0f, 1f, r.height * scale / 3f), true);
            GUI.color = old;
        }

        // ---- screen furniture ---------------------------------------------------------------------
        /// <summary>The backdrop behind menu screens: a deep vignette over the orbiting map.</summary>
        void Backdrop(float strength)
        {
            if (Event.current.type != EventType.Repaint) return;
            var full = new Rect(0f, 0f, W, H);
            Color old = GUI.color;
            Color b = Theme.Base;
            // A calm grade over the orbiting map, then a dark gradient from the left for text and one from the bottom.
            GUI.color = new Color(b.r, b.g, b.b, 0.66f * strength * old.a);
            GUI.DrawTexture(full, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, old.a);
            if (fadeLeft != null) GUI.DrawTexture(new Rect(0f, 0f, W * 0.78f, H), fadeLeft, ScaleMode.StretchToFill, true);
            GUI.color = new Color(1f, 1f, 1f, 0.85f * old.a);
            if (fadeDown != null) GUI.DrawTexture(new Rect(0f, H * 0.55f, W, H * 0.45f), fadeDown, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        /// <summary>Depth and a label for a panel (draw before its content): soft shadow, a short accent rule, the kicker above.</summary>
        void Frame(Rect r, string kick = null)
        {
            if (Event.current.type != EventType.Repaint) return;
            float k = Intro01;
            Color ac = Settings.Accent;
            Shadow(r, 48f, 0.5f);
            if (!string.IsNullOrEmpty(kick)) Kicker(new Rect(r.x, r.y - 26f, r.width, 18f), kick, new Color(Theme.Muted.r, Theme.Muted.g, Theme.Muted.b, k));
        }

        /// <summary>The screen transition: the new screen fades up from dark over 0.25 s (its content also rises, see Centered).</summary>
        void DrawTransition(float now)
        {
            float t = now - screenAt;
            const float Dur = 0.25f;
            if (t < 0f || t > Dur || Event.current.type != EventType.Repaint) return;
            float k = 1f - Theme.Ease(t / Dur);
            Fill(new Rect(0f, 0f, W, H), new Color(Theme.Base.r, Theme.Base.g, Theme.Base.b, 0.55f * k));
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

        /// <summary>The launch sting: an accent rule draws across, the wordmark rises in, the line under it fades in.</summary>
        void DrawSting(float now)
        {
            if (stingDone) return;
            if (GameController.TourActive)
            {
                stingDone = true;
                return;
            }
            if (stingStart < 0f)
            {
                stingStart = now;
                PlayUi(UiSound.Whoosh, 0.7f);
            }
            float t = now - stingStart;
            const float Dur = 2.6f;
            float fade = stingEnd >= 0f ? Mathf.Clamp01((now - stingEnd) / 0.3f) : Mathf.Clamp01((t - (Dur - 0.5f)) / 0.5f);
            if (fade >= 1f)
            {
                stingDone = true;
                screenAt = now;
                return;
            }
            float a = 1f - fade;
            Fill(new Rect(0f, 0f, W, H), new Color(Theme.Base.r, Theme.Base.g, Theme.Base.b, a));
            if (!stingHit && t >= 0.45f)
            {
                stingHit = true;
                if (stingEnd < 0f) PlayUi(UiSound.Hit, 0.7f);
            }
            Color ac = Settings.Accent;
            float cx = W / 2f, cy = H / 2f;
            float lk = Theme.Ease(Mathf.Clamp01((t - 0.1f) / 0.6f));
            Fill(new Rect(cx - 260f * lk, cy + 62f, 520f * lk, 2f), new Color(ac.r, ac.g, ac.b, a));
            float wk = Theme.Ease(Mathf.Clamp01((t - 0.35f) / 0.5f));
            if (wk > 0f) Condensed(new Rect(0f, cy - 64f + (1f - wk) * 18f, W, 120f), Spaced("Saltworks"), stingTitle, new Color(Theme.Ink.r, Theme.Ink.g, Theme.Ink.b, a * wk), 0.8f);
            float ck = Mathf.Clamp01((t - 0.9f) / 0.5f);
            if (ck > 0f) Lbl(new Rect(0f, cy + 80f, W, 24f), Spaced("Gameplay Coach"), centerSmall, new Color(Theme.Muted.r, Theme.Muted.g, Theme.Muted.b, a * ck));
            Lbl(new Rect(0f, H - 44f, W, 20f), "PRESS ANY KEY TO SKIP", centerSmall, new Color(1f, 1f, 1f, 0.3f * a));
        }

        /// <summary>A soft whoosh when the screen changes (not for the HUD, and not during the sting).</summary>
        void ScreenSounds()
        {
            if (Event.current.type != EventType.Repaint || Game.Screen == soundScreen) return;
            AppScreen prev = soundScreen;
            soundScreen = Game.Screen;
            if (!stingDone || (int)prev < 0) return;
            if (Game.Screen != AppScreen.Playing) PlayUi(UiSound.Whoosh, 0.3f);
        }
    }
}

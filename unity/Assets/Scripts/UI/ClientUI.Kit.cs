// The interface kit every screen is built from (GUI.* only, no GUILayout):
//
// - Crisp text. The UI is laid out in a virtual space 900 px high and scaled to
//   the window with GUI.matrix; text drawn through that matrix would be
//   rasterised at the virtual size and magnified (soft on a Retina panel).
//   TextAt undoes the scale for the draw and uses a copy of the style with the
//   font size multiplied, so glyphs are rasterised at the real pixel size.
//   Inside a clip (group, scroll view) the plain path is used, because the
//   clip's offset would be scaled with the rect (clipDepth tracks this).
// - GButton: one button component for every screen, with default, hover,
//   pressed, disabled, selected and keyboard-focus states and an optional icon.
// - Keyboard focus: buttons and controls register their rects each repaint;
//   the arrow keys (spatially) and Tab (in order) move the focus, Enter
//   activates it, Left / Right adjust a focused slider or segmented control.
// - Controls: switch, segmented choice, slider, text field, chips, key caps,
//   panels with depth (soft shadow, glass, hairline), section labels.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        // ---- crisp text ------------------------------------------------------------------------------
        int clipDepth;
        float crispScale = -1f;
        readonly Dictionary<GUIStyle, GUIStyle> crispStyles = new Dictionary<GUIStyle, GUIStyle>();
        static readonly GUIContent tmpContent = new GUIContent();

        bool CrispOn => clipDepth == 0 && Mathf.Abs(scale - 1f) > 0.01f;

        /// <summary>A copy of `st` for drawing at the real pixel size (font size and padding times the UI scale).</summary>
        GUIStyle Crisp(GUIStyle st)
        {
            if (crispScale != scale)
            {
                crispStyles.Clear();
                crispScale = scale;
            }
            if (crispStyles.TryGetValue(st, out GUIStyle c)) return c;
            c = new GUIStyle(st);
            int fs = st.fontSize > 0 ? st.fontSize : 13;
            c.fontSize = Mathf.Max(1, Mathf.RoundToInt(fs * scale));
            c.padding = ScaledOffset(st.padding);
            c.contentOffset = st.contentOffset * scale;
            c.fixedWidth = st.fixedWidth * scale;
            c.fixedHeight = st.fixedHeight * scale;
            if (crispStyles.Count > 400) crispStyles.Clear();
            crispStyles[st] = c;
            return c;
        }

        RectOffset ScaledOffset(RectOffset o)
        {
            return new RectOffset(Mathf.RoundToInt(o.left * scale), Mathf.RoundToInt(o.right * scale), Mathf.RoundToInt(o.top * scale), Mathf.RoundToInt(o.bottom * scale));
        }

        /// <summary>A virtual-space rect in the coordinates of the unscaled matrix (whole pixels).</summary>
        Rect PhysRect(Rect r)
        {
            float x0 = Mathf.Round(r.x * scale), y0 = Mathf.Round(r.y * scale);
            return new Rect(x0, y0, Mathf.Round(r.xMax * scale) - x0, Mathf.Round(r.yMax * scale) - y0);
        }

        /// <summary>Draws a label (tinted by GUI.contentColor) with glyphs at the real pixel size.</summary>
        void TextAt(Rect r, string s, GUIStyle st)
        {
            if (string.IsNullOrEmpty(s) || Event.current.type != EventType.Repaint) return;
            if (!CrispOn)
            {
                GUI.Label(r, s, st);
                return;
            }
            Matrix4x4 m = GUI.matrix;
            GUI.matrix = m * Matrix4x4.Scale(new Vector3(1f / scale, 1f / scale, 1f));
            GUI.Label(PhysRect(r), s, Crisp(st));
            GUI.matrix = m;
        }

        float TextH(string s, GUIStyle st, float w)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            tmpContent.text = s;
            return st.CalcHeight(tmpContent, w);
        }

        float TextW(string s, GUIStyle st)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            tmpContent.text = s;
            return st.CalcSize(tmpContent).x;
        }

        /// <summary>A clipping group; text inside uses the plain path (see the file header).</summary>
        void BeginClip(Rect r)
        {
            clipDepth++;
            GUI.BeginGroup(r);
        }

        void EndClip()
        {
            GUI.EndGroup();
            clipDepth = Mathf.Max(0, clipDepth - 1);
        }

        // ---- keyboard focus ---------------------------------------------------------------------------
        struct Focusable
        {
            public int Id;
            public Rect R;
            public bool Primary;
            public bool Horizontal;
        }

        List<Focusable> focusNow = new List<Focusable>(), focusLast = new List<Focusable>();
        int focusId;
        bool keyNav;
        /// <summary>Which layer is being drawn (0 the screen, 1 the settings over it); only the top layer takes focus.</summary>
        int focusLayer, topLayer;
        int pressedKey;
        int adjustId, adjustStep;
        AppScreen focusScreen = (AppScreen)(-1);

        /// <summary>Top of OnGUI: arrow keys and Tab move the focus, the mouse hides it.</summary>
        void BeginFocusFrame()
        {
            Event e = Event.current;
            topLayer = Game.SettingsOpen ? 1 : 0;
            focusLayer = 0;
            adjustId = 0;
            adjustStep = 0;
            if (focusScreen != Game.Screen)
            {
                focusScreen = Game.Screen;
                focusId = 0;
            }
            if (e.type == EventType.MouseDown || e.type == EventType.MouseMove || (e.type == EventType.MouseDrag && keyNav)) keyNav = false;
            if (e.type == EventType.MouseUp) pressedKey = 0;
            if (e.type == EventType.KeyDown && GUIUtility.keyboardControl == 0 && focusLast.Count > 0)
            {
                int dir = -1;
                switch (e.keyCode)
                {
                    case KeyCode.UpArrow: dir = 0; break;
                    case KeyCode.DownArrow: dir = 1; break;
                    case KeyCode.LeftArrow: dir = 2; break;
                    case KeyCode.RightArrow: dir = 3; break;
                    case KeyCode.Tab: dir = e.shift ? 5 : 4; break;
                }
                if (dir >= 0)
                {
                    int cur = FocusIndex(focusId);
                    if (keyNav && cur >= 0 && focusLast[cur].Horizontal && (dir == 2 || dir == 3))
                    {
                        adjustId = focusId;
                        adjustStep = dir == 2 ? -1 : 1;
                    }
                    // The first key press only shows where the focus is; the next ones move it.
                    else if (keyNav || cur < 0) MoveFocus(dir, cur);
                    keyNav = true;
                    e.Use();
                }
            }
            if (e.type == EventType.Repaint) focusNow.Clear();
        }

        /// <summary>End of OnGUI: this repaint's focusables become the navigation targets of the next events.</summary>
        void EndFocusFrame()
        {
            if (Event.current.type != EventType.Repaint) return;
            List<Focusable> t = focusLast;
            focusLast = focusNow;
            focusNow = t;
            if (keyNav && FocusIndex(focusId) < 0) focusId = DefaultFocus();
        }

        int FocusIndex(int id)
        {
            if (id == 0) return -1;
            for (int i = 0; i < focusLast.Count; i++) if (focusLast[i].Id == id) return i;
            return -1;
        }

        int DefaultFocus()
        {
            if (focusLast.Count == 0) return 0;
            foreach (Focusable f in focusLast) if (f.Primary) return f.Id;
            return focusLast[0].Id;
        }

        void MoveFocus(int dir, int cur)
        {
            int n = focusLast.Count;
            if (cur < 0)
            {
                focusId = DefaultFocus();
                return;
            }
            if (dir >= 4)
            {
                focusId = focusLast[(cur + (dir == 4 ? 1 : -1) + n) % n].Id;
                return;
            }
            Vector2 c0 = focusLast[cur].R.center;
            Vector2 want = dir == 0 ? Vector2.down : dir == 1 ? Vector2.up : dir == 2 ? Vector2.left : Vector2.right;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (i == cur) continue;
                Vector2 d = focusLast[i].R.center - c0;
                float along = Vector2.Dot(d, want);
                if (along <= 2f) continue;
                float across = Mathf.Abs(want.x != 0f ? d.y : d.x);
                float score = along + across * 2.5f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            if (best >= 0) focusId = focusLast[best].Id;
        }

        /// <summary>Registers a focusable control drawn this repaint; true when it has the keyboard focus.</summary>
        bool Focus(int id, Rect r, bool primary = false, bool horizontal = false)
        {
            if (focusLayer != topLayer) return false;
            if (Event.current.type == EventType.Repaint) focusNow.Add(new Focusable { Id = id, R = r, Primary = primary, Horizontal = horizontal });
            return keyNav && focusId == id;
        }

        /// <summary>Enter on the focused control.</summary>
        bool FocusActivated(bool focused)
        {
            Event e = Event.current;
            if (!focused || e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0) return false;
            if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return false;
            e.Use();
            return true;
        }

        /// <summary>Left / Right on the focused horizontal control this event: -1, +1, or 0.</summary>
        int FocusAdjust(int id)
        {
            if (adjustId != id || adjustStep == 0) return 0;
            int s = adjustStep;
            adjustStep = 0;
            return s;
        }

        void FocusRing(Rect r)
        {
            PlateLine(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f), new Color(1f, 1f, 1f, 0.85f));
        }

        static int StableKey(string s)
        {
            unchecked
            {
                int h = 23;
                if (s != null) for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
                return h | 1;
            }
        }

        // ---- the button ----------------------------------------------------------------------------
        /// <summary>
        /// The button. `kind` 0 secondary, 1 primary (accent fill), 2 ghost (outline only). States: default, hover,
        /// pressed, disabled, selected (accent tint and bar), focused (ring). `key` distinguishes buttons with the same
        /// label; `icon` is drawn before the label in the text colour; `hint` is a key cap at the right ("Enter").
        /// </summary>
        bool GButton(Rect r, string label, int kind = 0, bool enabled = true, int key = 0, bool selected = false, Texture icon = null, string hint = null)
        {
            bool on = enabled && GUI.enabled;
            int id = key != 0 ? key : StableKey(label);
            bool focused = on && Focus(id, r, kind == 1);
            Event e = Event.current;
            bool mouseIn = Hover(r);
            bool hot = on && (focused || (!keyNav && mouseIn));
            float h = HoverAnim(id, hot);
            if (on && e.type == EventType.MouseDown && e.button == 0 && mouseIn) pressedKey = id;
            bool down = on && pressedKey == id && mouseIn;
            Rect b = down ? new Rect(r.x, r.y + 1f, r.width, r.height) : r;
            Color ac = Settings.Accent;
            Color bg, line, tc;
            switch (kind)
            {
                case 1:
                    bg = down ? Theme.Darken(ac, 0.18f) : Color.Lerp(ac, Theme.Lighten(ac, 0.18f), h);
                    line = new Color(1f, 1f, 1f, 0.3f * h);
                    tc = Theme.OnAccent;
                    break;
                case 2:
                    bg = new Color(1f, 1f, 1f, down ? 0.1f : 0.015f + 0.06f * h);
                    line = new Color(1f, 1f, 1f, 0.16f + 0.3f * h);
                    tc = Color.Lerp(Theme.Muted, Theme.Ink, h);
                    break;
                default:
                    bg = down ? Theme.ButtonDownC : Color.Lerp(Theme.ButtonC, Theme.ButtonHoverC, h);
                    line = Color.Lerp(new Color(1f, 1f, 1f, 0.1f), new Color(ac.r, ac.g, ac.b, 0.85f), h);
                    tc = Theme.Ink;
                    break;
            }
            if (selected && kind != 1)
            {
                bg = Color.Lerp(new Color(ac.r * 0.2f, ac.g * 0.2f, ac.b * 0.2f, 0.95f), new Color(ac.r * 0.3f, ac.g * 0.3f, ac.b * 0.3f, 1f), h);
                line = ac;
                tc = Color.white;
            }
            if (!on)
            {
                bg.a *= 0.4f;
                line.a *= 0.35f;
                tc = kind == 1 ? new Color(0.05f, 0.05f, 0.06f, 0.55f) : Theme.Faint;
            }
            if (kind != 2 && on) Shadow(b, 10f, 0.35f);
            Plate(b, bg);
            PlateLine(b, line);
            if (selected && kind != 1) Fill(new Rect(b.x + 2f, b.y + 8f, 3f, b.height - 16f), ac);

            string text = Upper(label);
            float hintW = string.IsNullOrEmpty(hint) ? 0f : TextW(hint, keyCap) + 14f;
            float iconW = icon != null ? Mathf.Min(22f, b.height - 18f) : 0f;
            float gap = icon != null ? 10f : 0f;
            float tw = TextW(text, btnText);
            float avail = b.width - 24f - (hintW > 0f ? hintW + 8f : 0f);
            float total = Mathf.Min(avail, tw + iconW + gap);
            float x = b.x + 12f + Mathf.Max(0f, (avail - total) / 2f);
            if (icon != null) Tex(new Rect(x, b.center.y - iconW / 2f, iconW, iconW), icon, tc);
            Lbl(new Rect(x + iconW + gap, b.y, Mathf.Max(10f, total - iconW - gap + 4f), b.height), text, btnTextL, tc);
            if (hintW > 0f) KeyCapAt(new Rect(b.xMax - 12f - hintW, b.center.y - 11f, hintW, 22f), hint, kind == 1 ? new Color(0f, 0f, 0f, 0.22f) : new Color(1f, 1f, 1f, 0.1f), tc);
            if (focused) FocusRing(r);
            if (!on) return false;
            // The screen's default action: a primary button showing the Enter hint takes Enter while no other control has the focus.
            bool isDefault = kind == 1 && hint == "Enter" && !keyNav && focusLayer == topLayer && GUIUtility.keyboardControl == 0
                             && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            if (isDefault) e.Use();
            if (isDefault || FocusActivated(focused) || GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                if (kind == 1) PlayUi(SoundBank.Get("ui.confirm"), 0.6f);
                else if (kind == 2) PlayUi(SoundBank.Get("ui.cancel"), 0.5f);
                else PlayUi(SoundBank.Get("ui.click"), 0.5f);
                return true;
            }
            return false;
        }

        /// <summary>The hover amount of a clickable card (mouse over it, or the keyboard focus on it); ticks on hover.</summary>
        float CardHover(Rect r, int id, bool enabled)
        {
            return HoverAnim(id, enabled && (keyNav ? focusId == id : Hover(r)));
        }

        /// <summary>A clickable card area (draw the card first): focus, ring, Enter and click. Returns the click.</summary>
        bool CardHit(Rect r, int id, bool enabled)
        {
            bool focused = enabled && Focus(id, r);
            if (focused) FocusRing(r);
            if (!enabled) return false;
            if (FocusActivated(focused) || GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                PlayUi(SoundBank.Get("ui.confirm"), 0.6f);
                return true;
            }
            return false;
        }

        // ---- controls ---------------------------------------------------------------------------------
        /// <summary>An on / off switch in `r` (about 52 x 26).</summary>
        bool SwitchCtl(Rect r, bool v, int id, bool enabled = true)
        {
            bool focused = enabled && Focus(id, r);
            float k = Anim(id * 7 + 3, v, 9f);
            float h = HoverAnim(id, enabled && (focused || (!keyNav && Hover(r))));
            Color ac = Settings.Accent;
            float a = enabled ? 1f : 0.4f;
            Color track = Color.Lerp(new Color(1f, 1f, 1f, 0.12f + 0.06f * h), ac, k);
            track.a *= a;
            Plate(r, track);
            float kw = r.height - 8f;
            var knob = new Rect(Mathf.Lerp(r.x + 4f, r.xMax - 4f - kw, k), r.y + 4f, kw, kw);
            Plate(knob, new Color(1f, 1f, 1f, a));
            if (focused) FocusRing(r);
            if (!enabled) return v;
            int adj = FocusAdjust(id);
            if (adj != 0) return adj > 0;
            if (FocusActivated(focused) || GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                PlayUi(SoundBank.Get("ui.click"), 0.5f);
                return !v;
            }
            return v;
        }

        /// <summary>A segmented choice: one cell per option, the chosen one lit. Left / Right step it when focused.</summary>
        int SegmentedCtl(Rect r, int v, string[] options, int id, bool enabled = true)
        {
            int n = Mathf.Max(1, options.Length);
            bool focused = enabled && Focus(id, r, false, true);
            Plate(r, new Color(Theme.WellC.r, Theme.WellC.g, Theme.WellC.b, Theme.WellC.a * (enabled ? 1f : 0.5f)));
            PlateLine(r, focused ? new Color(1f, 1f, 1f, 0.5f) : Theme.Line);
            float w = r.width / n;
            Color ac = Settings.Accent;
            for (int i = 0; i < options.Length; i++)
            {
                var cell = new Rect(r.x + i * w, r.y, w, r.height);
                bool sel = i == v;
                float h = HoverAnim(id * 13 + i, enabled && !keyNav && Hover(cell));
                var inner = new Rect(cell.x + 3f, cell.y + 3f, cell.width - 6f, cell.height - 6f);
                if (sel)
                {
                    Plate(inner, new Color(ac.r * 0.3f, ac.g * 0.3f, ac.b * 0.3f, enabled ? 1f : 0.5f));
                    PlateLine(inner, new Color(ac.r, ac.g, ac.b, enabled ? 0.9f : 0.4f));
                }
                else if (h > 0.01f) Plate(inner, new Color(1f, 1f, 1f, 0.06f * h));
                Color tc = !enabled ? Theme.Faint : sel ? Color.white : Color.Lerp(Theme.Muted, Theme.Ink, h);
                Lbl(inner, options[i], segText, tc);
                if (i > 0 && !sel && i - 1 != v) Fill(new Rect(cell.x, cell.y + 10f, 1f, cell.height - 20f), Theme.Line);
                if (enabled && GUI.Button(cell, GUIContent.none, GUIStyle.none) && v != i)
                {
                    PlayUi(SoundBank.Get("ui.click"), 0.5f);
                    v = i;
                }
            }
            if (focused) FocusRing(r);
            if (!enabled) return v;
            int adj = FocusAdjust(id);
            if (adj != 0) v = Mathf.Clamp(v + adj, 0, options.Length - 1);
            return v;
        }

        /// <summary>A slider with an accent fill to the value. Left / Right step it by `step` when focused.</summary>
        float SliderCtl(Rect r, float v, float min, float max, int id, float step, bool enabled = true)
        {
            bool focused = enabled && Focus(id, r, false, true);
            float t = Mathf.InverseLerp(min, max, v);
            var track = new Rect(r.x, r.center.y - 3f, r.width, 6f);
            Fill(track, new Color(1f, 1f, 1f, enabled ? 0.12f : 0.06f));
            Fill(new Rect(track.x, track.y, track.width * t, track.height), enabled ? Settings.Accent : Theme.Faint);
            if (focused) FocusRing(r);
            bool old = GUI.enabled;
            GUI.enabled = enabled && old;
            v = GUI.HorizontalSlider(new Rect(r.x, r.center.y - 12f, r.width, 24f), v, min, max, sliderBare, thumb);
            GUI.enabled = old;
            int adj = enabled ? FocusAdjust(id) : 0;
            if (adj != 0) v = Mathf.Clamp(v + adj * step, min, max);
            return v;
        }

        /// <summary>Set by TextFieldCtl when Enter (or Esc) ended the edit this event.</summary>
        bool fieldCommitted;
        /// <summary>A text field to give the keyboard focus when it is next drawn.</summary>
        string pendingFocus;

        /// <summary>A single-line text field (crisp), with a well and an accent outline while typing.</summary>
        string TextFieldCtl(Rect r, string value, int maxLength, string controlName)
        {
            fieldCommitted = false;
            bool editing = GUI.GetNameOfFocusedControl() == controlName;
            float h = Anim(StableKey(controlName), editing || Hover(r));
            Plate(r, Theme.WellC);
            PlateLine(r, editing ? Settings.Accent : new Color(1f, 1f, 1f, 0.14f + 0.2f * h));
            var inner = new Rect(r.x + 12f, r.y + 2f, r.width - 24f, r.height - 4f);
            GUI.SetNextControlName(controlName);
            string v;
            if (CrispOn)
            {
                Matrix4x4 m = GUI.matrix;
                GUI.matrix = m * Matrix4x4.Scale(new Vector3(1f / scale, 1f / scale, 1f));
                v = GUI.TextField(PhysRect(inner), value ?? "", maxLength, Crisp(fieldStyle));
                GUI.matrix = m;
            }
            else v = GUI.TextField(inner, value ?? "", maxLength, fieldStyle);
            if (pendingFocus == controlName)
            {
                GUI.FocusControl(controlName);
                pendingFocus = null;
            }
            Event e = Event.current;
            if (editing && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape))
            {
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
                e.Use();
                fieldCommitted = true;
            }
            return v;
        }

        /// <summary>Condensed display type squeezed further (down to half width) so a long name still fits `r`.</summary>
        void FitCondensed(Rect r, string text, GUIStyle st, Color c, float kMax = 0.84f, bool shadow = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            float tw = TextW(text, st);
            float k = Mathf.Clamp(r.width / Mathf.Max(1f, tw), 0.5f, kMax);
            Condensed(r, text, st, c, k, shadow);
        }

        // ---- furniture --------------------------------------------------------------------------------
        /// <summary>A soft drop shadow under `r` (depth for cards, panels and buttons).</summary>
        void Shadow(Rect r, float spread, float alpha)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, alpha * old.a);
            GUI.DrawTexture(new Rect(r.x - spread, r.y - spread * 0.4f, r.width + spread * 2f, r.height + spread * 1.8f), UiTex.Glow, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        /// <summary>A raised card: shadow, card fill, hairline and a top highlight; `accent` (alpha > 0) draws a top bar.</summary>
        void Card(Rect r, Color accent, float lift = 0f)
        {
            Shadow(r, 16f + 10f * lift, 0.45f + 0.2f * lift);
            Plate(r, Color.Lerp(Theme.CardC, Theme.ButtonHoverC, lift * 0.6f));
            PlateLine(r, new Color(1f, 1f, 1f, 0.07f + 0.1f * lift));
            if (accent.a > 0f) Fill(new Rect(r.x + Theme.Cut, r.y, r.width - Theme.Cut - 4f, 3f), accent);
        }

        /// <summary>A section label: small caps with an accent tick and a hairline; returns the y under it.</summary>
        float SectionLabel(float x, float y, float w, string text)
        {
            Fill(new Rect(x, y + 3f, 3f, 12f), Settings.Accent);
            Lbl(new Rect(x + 10f, y, w - 10f, 18f), Spaced(text), sectionStyle, Theme.Muted);
            Fill(new Rect(x, y + 24f, w, 1f), Theme.Line);
            return y + 34f;
        }

        /// <summary>A screen header: kicker, condensed title, optional subtitle; returns the y under it.</summary>
        float Header(float x, float y, float w, string kick, string title, string sub = null)
        {
            if (!string.IsNullOrEmpty(kick))
            {
                Kicker(new Rect(x, y, w, 18f), kick, Settings.Accent);
                y += 24f;
            }
            FitTitle(new Rect(x, y, w, 48f), Upper(title), Theme.Ink);
            y += 50f;
            if (!string.IsNullOrEmpty(sub))
            {
                float h = TextH(sub, subStyle, w);
                Lbl(new Rect(x, y, w, h), sub, subStyle, Theme.Muted);
                y += h;
            }
            return y + Theme.S4;
        }

        /// <summary>A display title that always fits `r`: condensed a little more, then a size down, when it is long.</summary>
        void FitTitle(Rect r, string text, Color c)
        {
            float tw = TextW(text, titleDisplay);
            float k = Mathf.Min(0.86f, r.width / Mathf.Max(1f, tw));
            if (k >= 0.7f)
            {
                Condensed(r, text, titleDisplay, c, k);
                return;
            }
            float tw2 = TextW(text, titleSmall);
            Condensed(r, text, titleSmall, c, Mathf.Clamp(r.width / Mathf.Max(1f, tw2), 0.6f, 0.86f));
        }

        /// <summary>A wrapped paragraph; returns the y under it.</summary>
        float Para(float x, float y, float w, string s, GUIStyle st, Color c)
        {
            if (string.IsNullOrEmpty(s)) return y;
            float h = TextH(s, st, w);
            Lbl(new Rect(x, y, w, h), s, st, c);
            return y + h;
        }

        /// <summary>A small filled chip with text; returns its width.</summary>
        float Chip(float x, float y, string text, Color bg, Color fg, float h = 22f)
        {
            float w = TextW(text, chipStyle) + 18f;
            var r = new Rect(x, y, w, h);
            Plate(r, bg);
            Lbl(r, text, chipStyle, fg);
            return w;
        }

        void KeyCapAt(Rect r, string key, Color bg, Color fg)
        {
            Plate(r, bg);
            PlateLine(r, new Color(fg.r, fg.g, fg.b, 0.35f * fg.a));
            Lbl(r, key, keyCap, fg);
        }

        /// <summary>A horizontal bar: track, a fill to `v` (0..1), and an optional white tick at `mark`.</summary>
        void Meter(Rect r, float v, Color c, float mark = -1f)
        {
            Fill(r, new Color(1f, 1f, 1f, 0.09f));
            if (v > 0.001f) Fill(new Rect(r.x, r.y, Mathf.Max(2f, r.width * Mathf.Clamp01(v)), r.height), c);
            if (mark >= 0f) Fill(new Rect(r.x + r.width * Mathf.Clamp01(mark) - 1f, r.y - 3f, 2f, r.height + 6f), Color.white);
        }

        /// <summary>The bridge-waiting line, animated.</summary>
        void WaitingLine(Rect r)
        {
            if (Game.WaitingFor == null) return;
            int dots = (int)(Time.unscaledTime * 3f) % 3;
            Lbl(r, WaitText[dots], small, Theme.Muted);
        }
    }
}

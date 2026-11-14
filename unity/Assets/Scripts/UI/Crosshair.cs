// The crosshair, drawn with GL in whole screen pixels so it stays crisp at any
// resolution: sizes are pixels at 1080p, multiplied by a whole-number factor on
// taller windows. Call inside Draw2D.Begin/End.
using UnityEngine;

namespace Saltworks
{
    public static class Crosshair
    {
        /// <summary>Whole-pixel scale for the window height (1 up to 1619 px, 2 up to 2699 px, ...).</summary>
        public static int PixelScale(float screenHeight)
        {
            return Mathf.Max(1, Mathf.RoundToInt(screenHeight / 1080f));
        }

        /// <summary>
        /// Draws `s` centred on pixel (cx, cy) (top-left origin). `spread` adds pixels to the gap (dynamic spread);
        /// `alpha` multiplies all opacities.
        /// </summary>
        public static void Draw(float cx, float cy, CrosshairSettings s, int k, float spread, float alpha)
        {
            cx = Mathf.Floor(cx);
            cy = Mathf.Floor(cy);
            Color main = s.Color;
            main.a = Mathf.Clamp01(s.Opacity * alpha);
            Color ol = new Color(0f, 0f, 0f, Mathf.Clamp01(s.OutlineOpacity * alpha));
            float t = Mathf.Max(1, s.Thickness) * k;
            float len = s.Length * k;
            float gap = s.Gap * k + Mathf.Round(spread * k);
            float dot = s.DotSize * k;
            float o = s.OutlineThickness * k;
            float half = Mathf.Floor(t / 2f);
            float dhalf = Mathf.Floor(dot / 2f);

            bool arms = s.Style == CrosshairStyle.Cross || s.Style == CrosshairStyle.CrossDot || s.Style == CrosshairStyle.TShape;
            bool top = s.Style != CrosshairStyle.TShape;
            bool showDot = (s.Style == CrosshairStyle.Dot || s.Style == CrosshairStyle.CrossDot || s.Style == CrosshairStyle.Circle) && dot > 0f;
            if (s.Style == CrosshairStyle.Dot && dot <= 0f)
            {
                dot = 2f * k;
                dhalf = k;
                showDot = true;
            }
            bool circle = s.Style == CrosshairStyle.Circle;

            for (int pass = 0; pass < 2; pass++)
            {
                bool outline = pass == 0;
                if (outline && (!s.Outline || ol.a <= 0f)) continue;
                float e = outline ? o : 0f;
                Color c = outline ? ol : main;
                if (arms && len > 0f)
                {
                    Draw2D.Rect(cx + gap - e, cy - half - e, len + 2f * e, t + 2f * e, c);
                    Draw2D.Rect(cx - gap - len - e, cy - half - e, len + 2f * e, t + 2f * e, c);
                    Draw2D.Rect(cx - half - e, cy + gap - e, t + 2f * e, len + 2f * e, c);
                    if (top) Draw2D.Rect(cx - half - e, cy - gap - len - e, t + 2f * e, len + 2f * e, c);
                }
                if (circle)
                {
                    float r0 = Mathf.Max(1f, gap) - e;
                    Draw2D.Ring(new Vector2(cx, cy), Mathf.Max(0f, r0), Mathf.Max(1f, gap) + t + e, c, 48);
                }
                if (showDot) Draw2D.Rect(cx - dhalf - e, cy - dhalf - e, dot + 2f * e, dot + 2f * e, c);
            }
        }

        /// <summary>The hit marker: four diagonal ticks, larger for a headshot and largest for a kill.</summary>
        public static void HitMarker(float cx, float cy, int k, float age, bool head, bool kill)
        {
            const float life = 0.28f;
            if (age >= life || age < 0f) return;
            float a = 1f - age / life;
            Color c = Settings.HitColor;
            c.a = a;
            Color ol = new Color(0f, 0f, 0f, 0.6f * a);
            float l = (kill ? 12f : head ? 10f : 8f) * k;
            float g = (6f + (1f - a) * 3f) * k;
            float w = (kill ? 3f : 2f) * k;
            var ctr = new Vector2(Mathf.Floor(cx), Mathf.Floor(cy));
            for (int pass = 0; pass < 2; pass++)
            {
                float extra = pass == 0 ? 2f * k : 0f;
                Color col = pass == 0 ? ol : c;
                for (int i = 0; i < 4; i++)
                {
                    float ang = (45f + 90f * i) * Mathf.Deg2Rad;
                    var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    Draw2D.Line(ctr + d * (g - extra * 0.5f), ctr + d * (g + l + extra * 0.5f), w + extra, col);
                }
            }
        }
    }
}

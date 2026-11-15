// The tactical map: M opens it full screen during a round (M again or Esc
// closes it), holding Tab peeks at it. The whole map is drawn from the map
// data (floor, walls, sites with their letters, callout regions and names, both
// spawns) by Minimap in full mode, with the player's position and facing,
// teammates, enemies under the usual visibility rules (seen, revealed by a
// recon dart, or heard running), smokes and the spike. A legend, a scale bar
// and a north mark sit around it. The round's vitals, abilities and weapon stay
// drawn on top by the HUD.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        static readonly string[] LegendKinds = { "you", "ally", "enemy", "revealed", "heard", "smoke", "spike", "spawn-attack", "spawn-defense" };
        static readonly string[] LegendNames =
        {
            "You, and where you look", "Teammate", "Enemy in sight", "Enemy revealed by recon", "Enemy heard running",
            "Smoke", "Spike (dropped or planted)", "Attacker spawn", "Defender spawn",
        };

        readonly List<Rect> placedLabels = new List<Rect>();
        string legendNote = "";
        float legendNoteFor = -1f;
        string mapTitleFor, mapTitle = "";

        void DrawTacticalMap(bool peek)
        {
            Minimap mm = Game.Minimap;
            if (!mm.Ready) return;
            Fill(new Rect(0f, 0f, W, H), new Color(0.015f, 0.02f, 0.035f, peek ? 0.78f : 0.93f));

            // Header: what this is, and how to close it.
            float x0 = 48f, top = 34f;
            Kicker(new Rect(x0, top, 400f, 18f), peek ? "Map peek" : "Tactical map", Settings.Accent);
            if (!ReferenceEquals(mapTitleFor, Game.MapName))
            {
                mapTitleFor = Game.MapName;
                mapTitle = Spaced(string.IsNullOrEmpty(Game.MapName) ? "Map" : Game.MapName);
            }
            Condensed(new Rect(x0, top + 20f, 520f, 44f), mapTitle, titleDisplay, Theme.Ink, 0.86f);

            // The legend column on the right, the map in the space left of it (clear of the bottom HUD).
            float legendW = 280f;
            float legendX = W - 48f - legendW;
            var area = new Rect(x0, 110f, legendX - 36f - x0, H - 130f - 110f);
            float aspect = mm.MapWidth / Mathf.Max(1f, mm.MapHeight);
            float mw = area.width, mh = mw / aspect;
            if (mh > area.height)
            {
                mh = area.height;
                mw = mh * aspect;
            }
            var mr = new Rect(Mathf.Round(area.x + (area.width - mw) / 2f), Mathf.Round(area.y + (area.height - mh) / 2f), Mathf.Round(mw), Mathf.Round(mh));
            Shadow(mr, 30f, 0.6f);
            Rect panel = PhysRect(mr);
            mm.Draw(Game, panel, true, Game.EyePos, Game.Rig.Heading, 1f, scale);
            if (Event.current.type == EventType.Repaint) DrawMapLabels(mm, mr);

            // North and the scale bar. The bar takes its scale from the same panel transform the map draws with.
            Lbl(new Rect(mr.xMax - 40f, mr.y + 8f, 30f, 20f), "N", mapLetter, Theme.Muted);
            Fill(new Rect(mr.xMax - 26f, mr.y + 28f, 2f, 12f), Theme.Muted);
            float tenM = Minimap.PanelScale(panel.width, panel.height, true, mm.MapWidth, mm.MapHeight) * 10f / scale;
            if (tenM > 8f)
            {
                float sx = mr.x, sy = mr.yMax + 12f;
                Fill(new Rect(sx, sy + 6f, tenM, 2f), Theme.Ink);
                Fill(new Rect(sx, sy + 1f, 2f, 12f), Theme.Ink);
                Fill(new Rect(sx + tenM - 2f, sy + 1f, 2f, 12f), Theme.Ink);
                Lbl(new Rect(sx + tenM + 10f, sy - 2f, 80f, 18f), "10 m", smallMid, Theme.Muted);
            }

            // Close hints.
            float hx = W - 48f;
            if (peek)
            {
                float w1 = TextW("RELEASE TO CLOSE", tinyCaps);
                Lbl(new Rect(hx - w1, top + 2f, w1, 24f), "RELEASE TO CLOSE", tinyCaps, Theme.Muted);
                hx -= w1 + 10f;
                float kw = TextW("TAB", keyCap) + 14f;
                KeyCapAt(new Rect(hx - kw, top + 1f, kw, 24f), "TAB", new Color(1f, 1f, 1f, 0.1f), Theme.Ink);
            }
            else
            {
                float w1 = TextW("CLOSE", tinyCaps);
                Lbl(new Rect(hx - w1, top + 2f, w1, 24f), "CLOSE", tinyCaps, Theme.Muted);
                hx -= w1 + 10f;
                float kw = TextW("ESC", keyCap) + 14f;
                KeyCapAt(new Rect(hx - kw, top + 1f, kw, 24f), "ESC", new Color(1f, 1f, 1f, 0.1f), Theme.Ink);
                hx -= kw + 8f;
                float kw2 = 24f;
                KeyCapAt(new Rect(hx - kw2, top + 1f, kw2, 24f), "M", new Color(1f, 1f, 1f, 0.1f), Theme.Ink);
            }

            DrawMapLegend(new Rect(legendX, 110f, legendW, H - 130f - 110f - 40f));
        }

        /// <summary>Callout names, spawn names and site letters over the drawn map, without overlaps.</summary>
        void DrawMapLabels(Minimap mm, Rect mr)
        {
            placedLabels.Clear();
            // Site letters first (they matter most), as lettered badges.
            IReadOnlyList<Minimap.SiteLabel> sites = mm.Sites;
            for (int i = 0; i < sites.Count; i++)
            {
                Vector2 p = mm.PanelPoint(sites[i].Sim) / scale + mr.position;
                Color sc = Palette.Site(sites[i].Letter);
                var b = new Rect(p.x - 17f, p.y - 17f, 34f, 34f);
                Plate(b, new Color(0.02f, 0.025f, 0.04f, 0.9f));
                PlateLine(b, sc);
                Lbl(b, sites[i].Letter, siteLetter, sc);
                placedLabels.Add(new Rect(b.x - 4f, b.y - 4f, b.width + 8f, b.height + 8f));
            }
            // Spawns.
            IReadOnlyList<Minimap.SpawnZone> spawns = mm.Spawns;
            for (int i = 0; i < spawns.Count; i++)
            {
                Vector2 p = mm.PanelPoint(spawns[i].Sim) / scale + mr.position;
                string t = spawns[i].Team == "attack" ? "ATTACKER SPAWN" : "DEFENDER SPAWN";
                float tw = TextW(t, tinyCaps) + 16f;
                var r = new Rect(p.x - tw / 2f, p.y + 12f, tw, 22f);
                if (!Inside(r, mr) || Overlaps(r)) r.y = p.y - 34f;
                if (!Inside(r, mr) || Overlaps(r)) continue;
                Color tc = Palette.Team(spawns[i].Team);
                Plate(r, new Color(tc.r * 0.2f, tc.g * 0.2f, tc.b * 0.2f, 0.9f));
                Lbl(new Rect(r.x + 8f, r.y, r.width - 8f, r.height), t, tinyCaps, tc);
                placedLabels.Add(r);
            }
            // Callout names: small and quiet, skipped where they would collide.
            IReadOnlyList<Minimap.CalloutLabel> callouts = mm.Callouts;
            float pxM = mm.PxPerM / scale;
            for (int i = 0; i < callouts.Count; i++)
            {
                string name = Upper(callouts[i].Name);
                float tw = TextW(name, calloutStyle) + 6f;
                // A name much wider than its room only fits when the map is drawn large.
                if (tw > Mathf.Max(60f, callouts[i].Size * pxM * 1.6f)) continue;
                Vector2 p = mm.PanelPoint(callouts[i].Sim) / scale + mr.position;
                var r = new Rect(p.x - tw / 2f, p.y - 9f, tw, 18f);
                if (!Inside(r, mr) || Overlaps(r)) continue;
                Shadowed(r, name, calloutStyle, new Color(0.86f, 0.88f, 0.93f, 0.85f));
                placedLabels.Add(r);
            }
        }

        static bool Inside(Rect r, Rect outer)
        {
            return r.x >= outer.x + 2f && r.y >= outer.y + 2f && r.xMax <= outer.xMax - 2f && r.yMax <= outer.yMax - 2f;
        }

        bool Overlaps(Rect r)
        {
            foreach (Rect p in placedLabels) if (p.Overlaps(r)) return true;
            return false;
        }

        void DrawMapLegend(Rect r)
        {
            const float rowH = 32f;
            float h = 20f + 24f + LegendKinds.Length * rowH + 8f + 30f + 70f;
            var box = new Rect(r.x, r.y, r.width, Mathf.Min(r.height, h));
            Shadow(box, 20f, 0.5f);
            Glass(box);
            float x = box.x + 18f, y = box.y + 16f, w = box.width - 36f;
            y = SectionLabel(x, y, w, "Legend");
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint) Draw2D.Begin(Screen.width, Screen.height);
            for (int i = 0; i < LegendKinds.Length; i++)
            {
                if (y + rowH > box.yMax - 10f) break;
                if (repaint) Minimap.LegendIcon(LegendKinds[i], new Vector2((x + 14f) * scale, (y + rowH / 2f) * scale), scale);
                y += rowH;
            }
            if (repaint) Draw2D.End();
            y = box.y + 16f + 34f;
            for (int i = 0; i < LegendKinds.Length; i++)
            {
                if (y + rowH > box.yMax - 10f) break;
                Lbl(new Rect(x + 36f, y, w - 36f, rowH), LegendNames[i], smallMid, Theme.Ink);
                y += rowH;
            }
            // Sites as colour chips.
            if (y + 30f < box.yMax - 10f)
            {
                float sx = x;
                IReadOnlyList<Minimap.SiteLabel> sites = Game.Minimap.Sites;
                for (int i = 0; i < sites.Count; i++)
                {
                    Color sc = Palette.Site(sites[i].Letter);
                    var b = new Rect(sx, y + 4f, 26f, 24f);
                    Plate(b, new Color(0.02f, 0.025f, 0.04f, 0.9f));
                    PlateLine(b, sc);
                    Lbl(b, sites[i].Letter, chipStyle, sc);
                    sx += 32f;
                }
                Lbl(new Rect(sx + 4f, y + 4f, w - (sx - x), 24f), sites.Count > 0 ? "Plant sites" : "", smallMid, Theme.Ink);
                y += 40f;
            }
            if (legendNoteFor != Game.Rules.runHearingM)
            {
                legendNoteFor = Game.Rules.runHearingM;
                legendNote = "Enemies show only while you can see them, while a recon dart reveals them, or as a ripple when you hear them run within " + Game.Rules.runHearingM.ToString("0") + " m.";
            }
            if (y + 40f < box.yMax) Para(x, y, w, legendNote, small, Theme.Muted);
        }
    }
}

// The in-round HUD in the broadcast look: the top-centre score bug (round
// clock, scores, alive pips), bottom-left vitals (segmented health and armour
// with a damage ghost, credits), the bottom-centre ability bar (key caps,
// charge pips, cooldown sweeps), the bottom-right weapon panel (ammo ticks,
// reload), the kill feed (weapon icons, headshot and wallbang marks), damage
// direction arcs around the crosshair, the low-health vignette, the round-start
// sweep banner, damage numbers and the Longshot's scope. GUI.* calls and GL
// only (no GUILayout); strings are cached so a frame allocates nothing.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public partial class ClientUI
    {
        GUIStyle hpBig, bugTimer, bugScore, bugSmall, keyCap, weaponName, sweepTitle, sweepSub;
        float hpGhost = 100f;
        float sweepPlayedFor = -10f;

        void EnsureHudStyles()
        {
            GUIStyle lbl = GUI.skin.label;
            hpBig = Text(lbl, 48, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            bugTimer = Text(lbl, 32, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            bugScore = Text(lbl, 34, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            bugSmall = Text(lbl, 11, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            keyCap = Text(lbl, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            weaponName = Text(lbl, 22, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            sweepTitle = Text(lbl, 46, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            sweepSub = Text(lbl, 14, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
        }

        // ---- score bug -----------------------------------------------------------------------------
        string scoreBarL = "", scoreBarR = "", roundLine = "", buyLine = "";
        int scoreBarKey = int.MinValue, roundLineKey = int.MinValue, buyLineKey = int.MinValue;

        /// <summary>Top centre: the round clock between the two scores, alive pips under each, and the round line.</summary>
        void DrawScoreBug(SimState s, float now)
        {
            bool post = s.phase == "post_plant";
            float t = Mathf.Max(0f, s.roundTimeLeftS);
            Color ally = Settings.Ally, enemy = Settings.Enemy;
            var clockR = new Rect(W / 2f - 64f, 10f, 128f, 54f);
            Plate(clockR, new Color(0.02f, 0.025f, 0.04f, 0.88f));
            PlateLine(clockR, new Color(1f, 1f, 1f, 0.1f));
            Color line = post ? Palette.Spike : Palette.Team(Game.PlayerTeam);
            if (post) line.a = 0.6f + 0.4f * Mathf.Sin(now * 8f);
            Fill(new Rect(clockR.x + 12f, clockR.yMax - 3f, clockR.width - 24f, 3f), line);
            int ck = t < 10f ? (int)(t * 10f) : (int)t * 10;
            if (ck != clockKey)
            {
                clockKey = ck;
                clockStr = string.Format("{0}:{1:00}", (int)(t / 60f), (int)t % 60) + (t < 10f ? "." + ((int)(t * 10f) % 10) : "");
            }
            if (!ReferenceEquals(phaseSrc, s.phase))
            {
                phaseSrc = s.phase;
                phaseStr = Spaced((s.phase ?? "").Replace('_', ' '));
            }
            Color tc = post || t < 10f ? new Color(1f, 0.45f + 0.2f * Mathf.Sin(now * 10f), 0.45f) : Color.white;
            Condensed(new Rect(clockR.x, clockR.y + 2f, clockR.width, 36f), clockStr, bugTimer, tc, 0.88f, true);
            Lbl(new Rect(clockR.x, clockR.y + 36f, clockR.width, 12f), post ? "S P I K E   P L A N T E D" : phaseStr, bugSmall, Theme.Muted);

            ScoreInfo sc = Game.InDuel ? Game.DuelScore : Game.Score;
            int key = sc == null ? -1 : sc.player * 100 + sc.enemy;
            if (key != scoreBarKey)
            {
                scoreBarKey = key;
                scoreBarL = sc == null ? "" : Theme.Int(sc.player);
                scoreBarR = sc == null ? "" : Theme.Int(sc.enemy);
            }
            var lr = new Rect(clockR.x - 92f, 10f, 88f, 54f);
            var rr = new Rect(clockR.xMax + 4f, 10f, 88f, 54f);
            Plate(lr, new Color(ally.r * 0.22f, ally.g * 0.22f, ally.b * 0.22f, 0.88f));
            Plate(rr, new Color(enemy.r * 0.22f, enemy.g * 0.22f, enemy.b * 0.22f, 0.88f));
            Fill(new Rect(lr.x, lr.y + 8f, 4f, lr.height - 16f), ally);
            Fill(new Rect(rr.xMax - 4f, rr.y + 8f, 4f, rr.height - 16f), enemy);
            if ((Game.InMatch || Game.InDuel) && sc != null)
            {
                Condensed(new Rect(lr.x, lr.y + 2f, lr.width, 38f), scoreBarL, bugScore, ally, 0.9f, true);
                Condensed(new Rect(rr.x, rr.y + 2f, rr.width, 38f), scoreBarR, bugScore, enemy, 0.9f, true);
            }
            else
            {
                Lbl(new Rect(lr.x, lr.y + 8f, lr.width, 24f), "U S", hudCenter, ally);
                Lbl(new Rect(rr.x, rr.y + 8f, rr.width, 24f), "T H E M", hudCenter, enemy);
            }
            int allyAlive = 0, allyAll = 0, enemyAlive = 0, enemyAll = 0;
            if (s.agents != null)
                foreach (AgentState a in s.agents)
                {
                    if (a.team == Game.PlayerTeam) { allyAll++; if (a.alive) allyAlive++; }
                    else { enemyAll++; if (a.alive) enemyAlive++; }
                }
            Pips(new Rect(lr.x + 6f, lr.yMax - 13f, lr.width - 12f, 6f), allyAlive, allyAll, ally);
            Pips(new Rect(rr.x + 6f, rr.yMax - 13f, rr.width - 12f, 6f), enemyAlive, enemyAll, enemy);

            if (Game.InMatch || Game.InDuel)
            {
                int rk = Game.Round * 10 + (Game.RoundType ?? "").Length + (Game.InDuel ? 5000 : 0);
                if (rk != roundLineKey)
                {
                    roundLineKey = rk;
                    string tl = Game.InDuel ? "DUEL  \u00b7  FIRST TO 5" : Weapons.RoundTypeLabel(Game.RoundType);
                    roundLine = "ROUND " + Game.Round + (tl.Length > 0 ? "  \u00b7  " + tl : "");
                }
                var rl = new Rect(W / 2f - 150f, clockR.yMax + 4f, 300f, 18f);
                Plate(new Rect(W / 2f - 110f, rl.y, 220f, 18f), new Color(0.02f, 0.025f, 0.04f, 0.7f));
                Lbl(rl, roundLine, centerSmall, Theme.Ink);
                if (Game.BuyPhaseActive)
                {
                    int left = Mathf.CeilToInt(Game.BuyTimeLeft);
                    int bk = left * 2 + (Game.BuyOpen ? 1 : 0);
                    if (bk != buyLineKey)
                    {
                        buyLineKey = bk;
                        buyLine = Game.BuyOpen ? "BUY PHASE" : "BUY PHASE  \u00b7  B TO BUY  \u00b7  " + left + " s";
                    }
                    Shadowed(new Rect(W / 2f - 150f, rl.yMax + 2f, 300f, 18f), buyLine, centerSmall, Settings.Accent);
                }
            }
        }

        void Pips(Rect r, int alive, int all, Color c)
        {
            if (all <= 0) return;
            float pw = Mathf.Min(12f, (r.width - (all - 1) * 3f) / all), gap = 3f;
            float tot = all * pw + (all - 1) * gap;
            float x0 = r.center.x - tot / 2f;
            for (int i = 0; i < all; i++)
                Slash(new Rect(x0 + i * (pw + gap), r.y, pw, r.height), i < alive ? c : new Color(1f, 1f, 1f, 0.14f));
        }

        // ---- vitals --------------------------------------------------------------------------------
        string creditsHud = "", armorStr = "";
        int creditsHudKey = int.MinValue, armorKey = int.MinValue;

        /// <summary>Bottom left: health (segmented, with a ghost of recent damage), armour and credits.</summary>
        void DrawVitals(AgentState me, bool alive, float now)
        {
            float hp = me != null ? Mathf.Max(0f, me.hp) : 0f;
            hpGhost = hp > hpGhost ? hp : Mathf.MoveTowards(hpGhost, hp, Time.unscaledDeltaTime * 40f);
            int hk = alive ? Mathf.RoundToInt(hp) : -1;
            if (hk != hpKey)
            {
                hpKey = hk;
                hpStr = alive ? Theme.Int(hk) : "DOWN";
            }
            var panel = new Rect(20f, H - 112f, 330f, 92f);
            Plate(panel, new Color(0.02f, 0.025f, 0.04f, 0.72f));
            PlateLine(panel, new Color(1f, 1f, 1f, 0.07f));
            Color hpc = hp > 50f ? new Color(0.92f, 0.96f, 1f) : hp > 25f ? Palette.Warn : Palette.Bad;
            if (alive && hp <= 25f) hpc.a = 0.7f + 0.3f * Mathf.Sin(now * 9f);
            Condensed(new Rect(panel.x + 14f, panel.y + 4f, 100f, 56f), hpStr, hpBig, alive ? hpc : Palette.Bad, 0.86f, true);
            Lbl(new Rect(panel.x + 16f, panel.y + 60f, 100f, 14f), "H E A L T H", tinyCaps, Theme.Muted);

            // Health: ten skewed cells of ten points, the recent damage as a red ghost.
            var bar = new Rect(panel.x + 118f, panel.y + 22f, 196f, 14f);
            Segments(bar, 10, hp / 100f, hpGhost / 100f, hpc);
            // Armour: five cells of ten points.
            int pts = me != null ? me.ArmorPoints : -1;
            string kind = me?.ArmorKind ?? (Game.InMatch ? Game.OwnedArmor : null);
            if (pts < 0 && !string.IsNullOrEmpty(kind)) pts = Weapons.ArmorPointsOf(kind);
            var abar = new Rect(panel.x + 118f, panel.y + 46f, 98f, 10f);
            Color armc = new Color(0.55f, 0.82f, 1f);
            Segments(abar, 5, Mathf.Max(0, pts) / 50f, Mathf.Max(0, pts) / 50f, armc);
            if (pts != armorKey)
            {
                armorKey = pts;
                armorStr = pts > 0 ? Theme.Int(pts) : "0";
            }
            Tex(new Rect(abar.xMax + 10f, abar.y - 4f, 16f, 18f), UiTex.Glyph("sentinel"), pts > 0 ? armc : new Color(1f, 1f, 1f, 0.25f));
            Lbl(new Rect(abar.xMax + 30f, abar.y - 5f, 60f, 20f), armorStr, modeStyle, pts > 0 ? armc : Theme.Faint);
            Lbl(new Rect(panel.x + 118f, panel.y + 60f, 120f, 14f), "A R M O U R", tinyCaps, Theme.Muted);

            int c = Game.Credits;
            if (c >= 0)
            {
                if (c != creditsHudKey)
                {
                    creditsHudKey = c;
                    creditsHud = c.ToString("N0");
                }
                var cr = new Rect(20f, H - 146f, 150f, 28f);
                Plate(cr, new Color(0.02f, 0.025f, 0.04f, 0.72f));
                Plate(new Rect(cr.x + 8f, cr.y + 8f, 12f, 12f), Settings.Accent);
                Shadowed(new Rect(cr.x + 28f, cr.y + 4f, 120f, 20f), creditsHud, modeStyle, Color.white);
            }
            DrawMoveMode(new Rect(20f, H - (c >= 0 ? 180f : 146f), 240f, 26f), alive);
        }

        /// <summary>`n` skewed cells across `r`, lit to `v` (0..1), with a red ghost up to `ghost`.</summary>
        void Segments(Rect r, int n, float v, float ghost, Color c)
        {
            float gap = 4f;
            float cw = (r.width - (n - 1) * gap) / n;
            for (int i = 0; i < n; i++)
            {
                var cell = new Rect(r.x + i * (cw + gap), r.y, cw, r.height);
                float lit = Mathf.Clamp01(v * n - i);
                float gh = Mathf.Clamp01(ghost * n - i);
                Slash(cell, new Color(1f, 1f, 1f, 0.09f));
                if (gh > lit) Slash(new Rect(cell.x, cell.y, cell.width * gh, cell.height), new Color(1f, 0.25f, 0.25f, 0.75f));
                if (lit > 0f) Slash(new Rect(cell.x, cell.y, cell.width * lit, cell.height), c);
            }
        }

        // ---- abilities -----------------------------------------------------------------------------
        static readonly string[] AbilitySpaced = { "S M O K E", "F L A S H", "D A S H", "R E C O N" };

        /// <summary>Bottom centre: one slot per ability with a key cap, its icon, charge pips and a cooldown sweep after use.</summary>
        void DrawAbilityBar(AgentState me, float now)
        {
            const float size = 60f, gap = 12f;
            Ability[] all = Agents.AllAbilities;
            float total = all.Length * size + (all.Length - 1) * gap;
            float x0 = W / 2f - total / 2f;
            float y = H - size - 36f;
            AbilityCharges kit = Game.PlayerKit();
            bool repaint = Event.current.type == EventType.Repaint;
            for (int pass = 0; pass < 3; pass++)
            {
                if (pass == 1)
                {
                    // The sweeps, in one GL batch over the plates and under the text.
                    if (!repaint) continue;
                    Draw2D.Begin(Screen.width, Screen.height);
                    for (int i = 0; i < all.Length; i++)
                    {
                        Ability ab = all[i];
                        var r = new Rect(x0 + i * (size + gap), y, size, size);
                        float used = Mathf.Clamp01((now - Game.UsedAt[(int)ab]) / 0.9f);
                        if (used < 1f) Pie(r.center * scale, size * 0.44f * scale, 1f - used, new Color(0f, 0f, 0f, 0.55f));
                        Color ac = Agents.AbilityColor(ab);
                        if (used < 1f) Pie(r.center * scale, size * 0.48f * scale, 1f - used, new Color(ac.r, ac.g, ac.b, 0.15f));
                    }
                    Draw2D.End();
                    continue;
                }
                for (int i = 0; i < all.Length; i++)
                {
                    Ability ab = all[i];
                    var r = new Rect(x0 + i * (size + gap), y, size, size);
                    int charges = me != null ? me.Charges(ab) : 0;
                    int max = Mathf.Max(charges, kit != null ? kit.Get(ab) : 0);
                    bool usable = charges > 0 && me != null && me.alive;
                    bool aiming = Game.Aiming.HasValue && Game.Aiming.Value == ab;
                    Color ac = Agents.AbilityColor(ab);
                    float denied = 1f - Mathf.Clamp01((now - Game.DeniedAt[(int)ab]) / 0.45f);
                    if (pass == 0)
                    {
                        if (aiming)
                        {
                            Color oldG = GUI.color;
                            GUI.color = new Color(ac.r, ac.g, ac.b, 0.5f * oldG.a);
                            if (repaint) GUI.DrawTexture(new Rect(r.x - 18f, r.y - 18f, r.width + 36f, r.height + 36f), UiTex.Glow, ScaleMode.StretchToFill, true);
                            GUI.color = oldG;
                        }
                        Plate(r, new Color(0.025f, 0.03f, 0.045f, usable ? 0.88f : 0.62f));
                        Color border = aiming ? ac : denied > 0f ? new Color(Palette.Bad.r, Palette.Bad.g, Palette.Bad.b, denied) : new Color(1f, 1f, 1f, usable ? 0.22f : 0.08f);
                        PlateLine(r, border);
                        if (aiming) PlateLine(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), ac);
                        Color iconC = usable ? ac : new Color(0.55f, 0.56f, 0.6f, max > 0 ? 0.4f : 0.15f);
                        Icon(new Rect(r.x + 12f, r.y + 10f, size - 24f, size - 24f), ab, iconC);
                        if (max > 0 && charges <= 0) Fill(new Rect(r.x + 8f, r.center.y - 1f, r.width - 16f, 2f), new Color(1f, 1f, 1f, 0.15f));
                    }
                    else
                    {
                        // Key cap over the top-left edge, charge pips and the name under the slot.
                        var cap = new Rect(r.x - 6f, r.y - 8f, 22f, 20f);
                        Plate(cap, usable ? Color.white : new Color(1f, 1f, 1f, 0.35f));
                        Lbl(cap, Agents.KeyLabel(ab), keyCap, new Color(0.04f, 0.04f, 0.06f));
                        if (max > 0) Shadowed(new Rect(r.x, r.y, r.width - 6f, r.height - 3f), Theme.Int(charges), slotCount, usable ? Color.white : new Color(1f, 1f, 1f, 0.45f));
                        float pw = 10f, pg = 4f;
                        float pipsW = max * pw + Mathf.Max(0, max - 1) * pg;
                        for (int p = 0; p < max; p++)
                            Slash(new Rect(r.center.x - pipsW / 2f + p * (pw + pg), r.yMax + 5f, pw, 4f), p < charges ? ac : new Color(1f, 1f, 1f, 0.15f));
                        Lbl(new Rect(r.x - 12f, r.yMax + 12f, r.width + 24f, 14f), AbilitySpaced[(int)ab], bugSmall, usable ? Theme.Muted : Theme.Faint);
                    }
                }
            }
            if (Game.Aiming.HasValue)
            {
                Ability ab = Game.Aiming.Value;
                int key = (int)ab * 2 + (Game.AimClamped ? 1 : 0);
                if (key != hintKey)
                {
                    hintKey = key;
                    hintStr = "Release " + Agents.KeyLabel(ab) + " to throw the " + Agents.AbilityName(ab).ToLowerInvariant() + (Game.AimClamped ? "  (max range)" : "");
                }
                Shadowed(new Rect(W / 2f - 250f, y - 34f, 500f, 22f), hintStr, hudCenter, Agents.AbilityColor(ab));
            }
        }

        /// <summary>A clockwise pie from 12 o'clock covering `frac` of the circle (GL, screen pixels, inside Draw2D.Begin).</summary>
        static void Pie(Vector2 c, float r, float frac, Color col)
        {
            if (frac <= 0f) return;
            int seg = Mathf.Max(3, Mathf.CeilToInt(32 * frac));
            float total = frac * Mathf.PI * 2f;
            for (int i = 0; i < seg; i++)
            {
                float a0 = total * i / seg, a1 = total * (i + 1) / seg;
                var p0 = new Vector2(c.x + Mathf.Sin(a0) * r, c.y - Mathf.Cos(a0) * r);
                var p1 = new Vector2(c.x + Mathf.Sin(a1) * r, c.y - Mathf.Cos(a1) * r);
                Draw2D.Tri(c, p0, p1, col);
            }
        }

        /// <summary>A ring segment centred on `angDeg` (0 = up, clockwise), `spanDeg` wide (GL, inside Draw2D.Begin).</summary>
        static void Arc(Vector2 c, float rIn, float rOut, float angDeg, float spanDeg, Color col)
        {
            const int seg = 10;
            float a0 = (angDeg - spanDeg / 2f) * Mathf.Deg2Rad;
            float step = spanDeg * Mathf.Deg2Rad / seg;
            for (int i = 0; i < seg; i++)
            {
                float u0 = a0 + step * i, u1 = u0 + step;
                // Taper the ends so the arc reads as a pointer.
                float t0 = 1f - Mathf.Abs(i - seg / 2f) / (seg / 2f + 1f);
                float t1 = 1f - Mathf.Abs(i + 1 - seg / 2f) / (seg / 2f + 1f);
                float o0 = Mathf.Lerp(rIn, rOut, 0.35f + 0.65f * t0), o1 = Mathf.Lerp(rIn, rOut, 0.35f + 0.65f * t1);
                var i0 = new Vector2(c.x + Mathf.Sin(u0) * rIn, c.y - Mathf.Cos(u0) * rIn);
                var i1 = new Vector2(c.x + Mathf.Sin(u1) * rIn, c.y - Mathf.Cos(u1) * rIn);
                var e0 = new Vector2(c.x + Mathf.Sin(u0) * o0, c.y - Mathf.Cos(u0) * o0);
                var e1 = new Vector2(c.x + Mathf.Sin(u1) * o1, c.y - Mathf.Cos(u1) * o1);
                Draw2D.Tri(i0, e0, e1, col);
                Draw2D.Tri(i0, e1, i1, col);
            }
        }

        // ---- weapon panel --------------------------------------------------------------------------
        string ammoStr = "", reserveStr = "", weaponNameStr = "", weaponClassStr = "";
        int ammoKey = int.MinValue, reserveKey = int.MinValue;
        string weaponLineFor;

        /// <summary>Bottom right: the weapon, its ammo with a tick per round, reload progress and the two slots.</summary>
        void DrawAmmo(AgentState me, float now)
        {
            if (me == null || !me.alive) return;
            Gunplay g = Game.Gun;
            WeaponInfo w = g.Weapon;
            var r = new Rect(W - 350f, H - 112f, 330f, 92f);
            Plate(r, new Color(0.02f, 0.025f, 0.04f, 0.72f));
            PlateLine(r, new Color(1f, 1f, 1f, 0.07f));
            Fill(new Rect(r.xMax - 4f, r.y + 10f, 3f, r.height - 20f), Settings.Accent);
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
                weaponNameStr = w.Name.ToUpperInvariant();
                weaponClassStr = Spaced(w.ClassName);
            }
            bool low = g.Ammo <= Mathf.Max(1, w.Mag / 4);
            Condensed(new Rect(r.x + 14f, r.y + 6f, 150f, 26f), weaponNameStr, weaponName, Color.white, 0.86f, true);
            Lbl(new Rect(r.x + 14f, r.y + 30f, 160f, 12f), weaponClassStr, tinyCaps, Theme.Muted);
            Tex(new Rect(r.x + 12f, r.y + 42f, 120f, 30f), GunModels.Icon(w.Id), new Color(1f, 1f, 1f, 0.9f));
            Color ammoC = low ? new Color(Palette.Warn.r, Palette.Warn.g, Palette.Warn.b, 0.75f + 0.25f * Mathf.Sin(now * 8f)) : Color.white;
            Condensed(new Rect(r.xMax - 210f, r.y + 2f, 130f, 62f), ammoStr, ammoBig, ammoC, 0.86f, true);
            Shadowed(new Rect(r.xMax - 78f, r.y + 14f, 64f, 44f), reserveStr, ammoSmall, Theme.Muted);

            // A tick per round in the magazine, lit while loaded.
            int mag = Mathf.Clamp(w.Mag, 1, 30);
            float tw = Mathf.Min(5f, (150f - (mag - 1) * 2f) / mag);
            float tx0 = r.xMax - 18f - mag * (tw + 2f);
            int loaded = Mathf.Clamp(Mathf.RoundToInt(g.Ammo * mag / (float)Mathf.Max(1, w.Mag)), 0, mag);
            for (int i = 0; i < mag; i++)
                Fill(new Rect(tx0 + i * (tw + 2f), r.y + 64f, tw, 6f), i < loaded ? (low ? Palette.Warn : Settings.Accent) : new Color(1f, 1f, 1f, 0.12f));

            if (g.Reloading(now))
            {
                float p = g.ReloadProgress(now);
                Neon(new Rect(r.x + 14f, r.y + 80f, 130f, 3f), Settings.Accent, Mathf.Max(0.01f, p));
                Lbl(new Rect(r.x + 160f, r.y + 75f, 150f, 12f), "R E L O A D I N G", tinyCaps, Settings.Accent);
            }
            else
            {
                string prim = g.Primary, sec = g.Secondary;
                SlotChip(new Rect(r.x + 14f, r.y + 74f, 120f, 14f), "1", prim != null ? Weapons.NameOf(prim) : "none", g.OnPrimary && prim != null, prim != null);
                SlotChip(new Rect(r.x + 140f, r.y + 74f, 120f, 14f), "2", Weapons.NameOf(sec), !g.OnPrimary || prim == null, true);
            }
            if (g.Ammo <= 0 && !g.Reloading(now)) Shadowed(new Rect(W / 2f - 150f, H / 2f + 40f, 300f, 22f), "R  RELOAD", hudCenter, Palette.Warn);
        }

        void SlotChip(Rect r, string key, string name, bool on, bool has)
        {
            Plate(r, on ? new Color(Settings.Accent.r, Settings.Accent.g, Settings.Accent.b, 0.85f) : new Color(1f, 1f, 1f, has ? 0.07f : 0.03f));
            Color tc = on ? new Color(0.04f, 0.04f, 0.05f) : has ? Theme.Ink : Theme.Faint;
            Lbl(new Rect(r.x + 6f, r.y, 14f, r.height), key, tinyCaps, tc);
            Lbl(new Rect(r.x + 20f, r.y, r.width - 22f, r.height), Upper(name), tinyCaps, tc);
        }

        // ---- kill feed -----------------------------------------------------------------------------
        /// <summary>Top right: kills as "killer [weapon] (headshot) (wallbang) victim", other events as text. Entries slide in.</summary>
        void DrawKillFeed(float now)
        {
            float fy = 14f;
            for (int i = Game.Feed.Count - 1; i >= 0; i--)
            {
                FeedItem item = Game.Feed[i];
                float age = now - item.At;
                if (age > 7f) continue;
                float a = Mathf.Clamp01((7f - age) / 1f);
                float slide = (1f - Theme.Ease(age / 0.22f)) * 60f;
                float right = W - 16f + slide;
                if (item.IsKill)
                {
                    if (item.KillerW < 0f) item.KillerW = killName.CalcSize(new GUIContent(item.Killer)).x;
                    if (item.VictimW < 0f) item.VictimW = killName.CalcSize(new GUIContent(item.Victim)).x;
                    const float iconW = 58f, markW = 18f;
                    float marks = (item.Head ? markW + 4f : 0f) + (item.Wallbang ? markW + 4f : 0f);
                    float total = 14f + item.KillerW + 10f + iconW + 4f + marks + 8f + item.VictimW + 14f;
                    var r = new Rect(right - total, fy, total, 28f);
                    bool mine = item.Killer == "You" || item.Victim == "You";
                    Color ac = Settings.Accent;
                    Plate(r, mine ? new Color(ac.r * 0.3f, ac.g * 0.3f, ac.b * 0.3f, 0.8f * a) : new Color(0.02f, 0.025f, 0.04f, 0.7f * a));
                    if (mine) PlateLine(r, new Color(ac.r, ac.g, ac.b, 0.8f * a));
                    Fill(new Rect(r.x + 4f, r.y + 5f, 3f, r.height - 10f), new Color(item.KillerColor.r, item.KillerColor.g, item.KillerColor.b, a));
                    float x = r.x + 14f;
                    Shadowed(new Rect(x, r.y, item.KillerW + 4f, r.height), item.Killer, killName, new Color(item.KillerColor.r, item.KillerColor.g, item.KillerColor.b, a));
                    x += item.KillerW + 10f;
                    Tex(new Rect(x, r.y + 5f, iconW, 18f), GunModels.Icon(item.Weapon), new Color(1f, 1f, 1f, 0.92f * a));
                    x += iconW + 4f;
                    if (item.Head)
                    {
                        Tex(new Rect(x, r.y + 5f, markW, markW), UiTex.Glyph("headshot"), new Color(1f, 0.85f, 0.3f, a));
                        x += markW + 4f;
                    }
                    if (item.Wallbang)
                    {
                        Tex(new Rect(x, r.y + 5f, markW, markW), UiTex.Glyph("wallbang"), new Color(0.6f, 0.9f, 1f, a));
                        x += markW + 4f;
                    }
                    x += 4f;
                    Shadowed(new Rect(x, r.y, item.VictimW + 4f, r.height), item.Victim, killName, new Color(item.VictimColor.r, item.VictimColor.g, item.VictimColor.b, a));
                }
                else
                {
                    if (item.Width < 0f) item.Width = feed.CalcSize(new GUIContent(item.Text)).x;
                    var r = new Rect(right - item.Width - 20f, fy, item.Width + 20f, 26f);
                    Plate(r, new Color(0.02f, 0.025f, 0.04f, 0.62f * a));
                    Fill(new Rect(r.xMax - 4f, r.y + 5f, 3f, r.height - 10f), new Color(item.Color.r, item.Color.g, item.Color.b, a));
                    Shadowed(new Rect(r.x, r.y, r.width - 10f, r.height), item.Text, feed, new Color(1f, 1f, 1f, a));
                }
                fy += 32f;
            }
        }

        // ---- damage direction, low health, round sweep -----------------------------------------------
        /// <summary>Red arcs around the crosshair pointing at whoever hit you, fading over 1.4 s.</summary>
        void DrawDamageIndicators(float now)
        {
            if (Event.current.type != EventType.Repaint) return;
            bool any = false;
            foreach (DamageIndicator d in Game.Indicators) if (d.At > 0f && now - d.At < 1.4f) any = true;
            if (!any) return;
            Vector2 c = new Vector2(Screen.width / 2f, Screen.height / 2f);
            float r = 118f * scale;
            float heading = Game.Rig.Heading;
            Draw2D.Begin(Screen.width, Screen.height);
            foreach (DamageIndicator d in Game.Indicators)
            {
                float age = now - d.At;
                if (d.At <= 0f || age >= 1.4f) continue;
                Vector2 to = d.From - Game.EyePos;
                if (to.sqrMagnitude < 1e-4f) continue;
                float ang = Mathf.DeltaAngle(heading, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg);
                float k = 1f - age / 1.4f;
                float pop = 1f + Mathf.Clamp01(1f - age / 0.15f) * 0.15f;
                Arc(c, r * pop, (r + 16f * scale) * pop, ang, 46f, new Color(1f, 0.16f, 0.12f, 0.85f * k));
                Arc(c, r * pop - 3f * scale, r * pop, ang, 52f, new Color(1f, 0.85f, 0.8f, 0.6f * k));
            }
            Draw2D.End();
        }

        /// <summary>A red pulsing vignette below 35 health.</summary>
        void DrawLowHealth(AgentState me, float now)
        {
            if (me == null || !me.alive || me.hp > 35f || Event.current.type != EventType.Repaint) return;
            float k = 1f - Mathf.Clamp01(me.hp / 35f);
            float pulse = 0.75f + 0.25f * Mathf.Sin(now * (4f + 4f * k));
            Color old = GUI.color;
            GUI.color = new Color(0.75f, 0.02f, 0.04f, (0.35f + 0.45f * k) * pulse * old.a);
            GUI.DrawTexture(new Rect(-40f, -40f, W + 80f, H + 80f), UiTex.Vignette, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        /// <summary>The round-start sweep: a slanted band slides across with "ROUND 3 — FORCE BUY", then slides out.</summary>
        void DrawRoundSweep(float now)
        {
            float t = now - Game.RoundBannerAt;
            const float Dur = 2.6f;
            if (t < 0f || t > Dur || string.IsNullOrEmpty(Game.RoundBannerText)) return;
            if (sweepPlayedFor != Game.RoundBannerAt)
            {
                sweepPlayedFor = Game.RoundBannerAt;
                PlayUi(UiSound.Sting, 0.7f);
            }
            float inK = Theme.Ease(t / 0.35f);
            float outK = Theme.Ease((t - (Dur - 0.4f)) / 0.4f);
            float x = -W * (1f - inK) + W * outK;
            float y = H * 0.34f;
            Color ac = Settings.Accent;
            Slash(new Rect(x - 40f, y, W + 80f, 86f), new Color(0.02f, 0.025f, 0.045f, 0.9f));
            Slash(new Rect(x + W * 0.12f, y - 4f, W * 0.76f, 4f), ac);
            Slash(new Rect(x + W * 0.2f, y + 86f, W * 0.6f, 3f), Theme.Neon2);
            float textX = x + (1f - inK) * -80f + outK * 80f;
            Condensed(new Rect(textX, y + 6f, W, 56f), Game.RoundBannerText, sweepTitle, Color.white, 0.82f, true);
            Lbl(new Rect(textX, y + 60f, W, 18f), Game.RoundBannerSub ?? "", sweepSub, ac);
        }

        // ---- damage numbers and scope ------------------------------------------------------------------
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

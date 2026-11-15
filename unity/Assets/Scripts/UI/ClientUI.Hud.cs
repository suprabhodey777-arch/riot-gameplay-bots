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
        GUIStyle hpBig, bugTimer, bugScore, bugSmall, keyCap, weaponName, sweepTitle, sweepSub, hudSide, hudNumSmall, hudLine, hudLineR;
        float hpGhost = 100f;
        float sweepPlayedFor = -10f;

        void EnsureHudStyles()
        {
            GUIStyle lbl = GUI.skin.label;
            hpBig = Text(lbl, 60, FontStyle.Bold, Color.white, TextAnchor.LowerLeft, false);
            bugTimer = Text(lbl, 34, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            bugScore = Text(lbl, 28, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            bugSmall = Text(lbl, 11, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            keyCap = Text(lbl, 13, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            weaponName = Text(lbl, 13, FontStyle.Bold, Color.white, TextAnchor.MiddleRight, false);
            sweepTitle = Text(lbl, 40, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            sweepSub = Text(lbl, 13, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            hudSide = Text(lbl, 20, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter, false);
            hudNumSmall = Text(lbl, 22, FontStyle.Bold, Color.white, TextAnchor.LowerLeft, false);
            hudLine = Text(lbl, 13, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, false);
            hudLineR = Text(lbl, 13, FontStyle.Bold, Color.white, TextAnchor.MiddleRight, false);
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
            float cx0 = W / 2f;
            // No plate: a soft dark pool behind the clock keeps it readable against a bright sky.
            SoftDark(new Rect(cx0 - 230f, -40f, 460f, 150f), 0.5f);
            var clockR = new Rect(cx0 - 60f, 12f, 120f, 42f);
            int ck = t < 10f ? (int)(t * 10f) : (int)t * 10;
            if (ck != clockKey)
            {
                clockKey = ck;
                clockStr = string.Format("{0}:{1:00}", (int)(t / 60f), (int)t % 60) + (t < 10f ? "." + ((int)(t * 10f) % 10) : "");
            }
            if (!ReferenceEquals(phaseSrc, s.phase))
            {
                phaseSrc = s.phase;
                phaseStr = Upper((s.phase ?? "").Replace('_', ' '));
            }
            Color tc = post || t < 10f ? Color.Lerp(Palette.Bad, Theme.Ink, 0.25f + 0.2f * Mathf.Sin(now * 10f)) : Theme.Ink;
            Shadowed(clockR, clockStr, bugTimer, tc);
            Shadowed(new Rect(cx0 - 100f, clockR.yMax - 2f, 200f, 14f), post ? "SPIKE PLANTED" : phaseStr, bugSmall, post ? Palette.Spike : Theme.Muted);

            ScoreInfo sc = Game.InDuel ? Game.DuelScore : Game.Score;
            int key = sc == null ? -1 : sc.player * 100 + sc.enemy;
            if (key != scoreBarKey)
            {
                scoreBarKey = key;
                scoreBarL = sc == null ? "" : Theme.Int(sc.player);
                scoreBarR = sc == null ? "" : Theme.Int(sc.enemy);
            }
            var lr = new Rect(clockR.x - 76f, 12f, 64f, 42f);
            var rr = new Rect(clockR.xMax + 12f, 12f, 64f, 42f);
            if ((Game.InMatch || Game.InDuel) && sc != null)
            {
                Shadowed(new Rect(lr.x, lr.y + 2f, lr.width, 34f), scoreBarL, bugScore, ally);
                Shadowed(new Rect(rr.x, rr.y + 2f, rr.width, 34f), scoreBarR, bugScore, enemy);
            }
            else
            {
                Shadowed(new Rect(lr.x, lr.y + 4f, lr.width, 30f), "US", hudSide, ally);
                Shadowed(new Rect(rr.x, rr.y + 4f, rr.width, 30f), "THEM", hudSide, enemy);
            }
            int allyAlive = 0, allyAll = 0, enemyAlive = 0, enemyAll = 0;
            if (s.agents != null)
                foreach (AgentState a in s.agents)
                {
                    if (a.team == Game.PlayerTeam) { allyAll++; if (a.alive) allyAlive++; }
                    else { enemyAll++; if (a.alive) enemyAlive++; }
                }
            Pips(new Rect(lr.x, lr.yMax - 4f, lr.width, 4f), allyAlive, allyAll, ally);
            Pips(new Rect(rr.x, rr.yMax - 4f, rr.width, 4f), enemyAlive, enemyAll, enemy);

            if (Game.InMatch || Game.InDuel)
            {
                int rk = Game.Round * 10 + (Game.RoundType ?? "").Length + (Game.InDuel ? 5000 : 0);
                if (rk != roundLineKey)
                {
                    roundLineKey = rk;
                    string tl = Game.InDuel ? "DUEL  \u00b7  FIRST TO 5" : Weapons.RoundTypeLabel(Game.RoundType);
                    roundLine = "ROUND " + Game.Round + (tl.Length > 0 ? "  \u00b7  " + tl : "");
                }
                var rl = new Rect(cx0 - 150f, clockR.yMax + 14f, 300f, 16f);
                Shadowed(rl, roundLine, bugSmall, Theme.Ink);
                if (Game.BuyPhaseActive)
                {
                    int left = Mathf.CeilToInt(Game.BuyTimeLeft);
                    int bk = left * 2 + (Game.BuyOpen ? 1 : 0);
                    if (bk != buyLineKey)
                    {
                        buyLineKey = bk;
                        buyLine = Game.BuyOpen ? "BUY PHASE" : "BUY PHASE  \u00b7  B TO BUY  \u00b7  " + left + " s";
                    }
                    Shadowed(new Rect(cx0 - 150f, rl.yMax + 2f, 300f, 16f), buyLine, centerSmall, Settings.Accent);
                }
            }
        }

        /// <summary>A soft dark pool (no edges) for contrast under HUD text.</summary>
        void SoftDark(Rect r, float a)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, a * old.a);
            GUI.DrawTexture(r, UiTex.Glow, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        /// <summary>The HUD's only backing: a dark gradient rising from the bottom edge.</summary>
        void HudBottomFade()
        {
            if (Event.current.type != EventType.Repaint || fadeDown == null) return;
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.95f * old.a);
            GUI.DrawTexture(new Rect(0f, H - 280f, W, 280f), fadeDown, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        void Pips(Rect r, int alive, int all, Color c)
        {
            if (all <= 0) return;
            float pw = Mathf.Min(8f, (r.width - (all - 1) * 3f) / all), gap = 3f;
            float tot = all * pw + (all - 1) * gap;
            float x0 = r.center.x - tot / 2f;
            for (int i = 0; i < all; i++)
            {
                var cell = new Rect(x0 + i * (pw + gap), r.y, pw, r.height);
                Fill(new Rect(cell.x + 1f, cell.y + 1f, cell.width, cell.height), new Color(0f, 0f, 0f, 0.45f));
                Fill(cell, i < alive ? c : new Color(1f, 1f, 1f, 0.22f));
            }
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
            const float mx = 36f;
            Color hpc = hp > 50f ? Theme.Ink : hp > 25f ? Palette.Warn : Palette.Bad;
            if (alive && hp <= 25f) hpc.a = 0.7f + 0.3f * Mathf.Sin(now * 9f);
            // Health: one large number over a thin bar; recent damage lingers on the bar as a red ghost.
            var num = new Rect(mx, H - 116f, 220f, 76f);
            Shadowed(num, hpStr, hpBig, alive ? hpc : Palette.Bad);
            var bar = new Rect(mx, H - 36f, 220f, 4f);
            Fill(new Rect(bar.x, bar.y + 1f, bar.width + 1f, bar.height), new Color(0f, 0f, 0f, 0.4f));
            Fill(bar, new Color(1f, 1f, 1f, 0.16f));
            if (hpGhost > hp) Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(hpGhost / 100f), bar.height), new Color(1f, 0.27f, 0.27f, 0.85f));
            if (hp > 0f) Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(hp / 100f), bar.height), hpc);

            // Armour: a small shield and its points beside the number.
            int pts = me != null ? me.ArmorPoints : -1;
            string kind = me?.ArmorKind ?? (Game.InMatch ? Game.OwnedArmor : null);
            if (pts < 0 && !string.IsNullOrEmpty(kind)) pts = Weapons.ArmorPointsOf(kind);
            if (pts != armorKey)
            {
                armorKey = pts;
                armorStr = pts > 0 ? Theme.Int(pts) : "0";
            }
            float ax = mx + TextW(hpStr, hpBig) + 16f;
            Color armc = pts > 0 ? Theme.Ink : new Color(Theme.Muted.r, Theme.Muted.g, Theme.Muted.b, 0.75f);
            Tex(new Rect(ax + 1f, H - 69f, 16f, 18f), UiTex.Glyph("sentinel"), new Color(0f, 0f, 0f, 0.5f));
            Tex(new Rect(ax, H - 70f, 16f, 18f), UiTex.Glyph("sentinel"), armc);
            Shadowed(new Rect(ax + 22f, H - 80f, 70f, 32f), armorStr, hudNumSmall, armc);

            // One quiet line above: credits (in a match) and how loud the player is.
            float ly = H - 142f;
            float lx = mx;
            int c = Game.Credits;
            // Credits only mean something in a match; in practice the chip read as an unexplained "0".
            if (c >= 0 && Game.InMatch)
            {
                if (c != creditsHudKey)
                {
                    creditsHudKey = c;
                    creditsHud = c.ToString("N0");
                }
                Fill(new Rect(lx, ly + 6f, 8f, 8f), Settings.Accent);
                Shadowed(new Rect(lx + 14f, ly, 90f, 20f), creditsHud, hudLine, Theme.Ink);
                lx += 14f + TextW(creditsHud, hudLine) + 20f;
            }
            DrawMoveMode(new Rect(lx, ly, 300f, 20f), alive);
        }

        // ---- abilities -----------------------------------------------------------------------------
        /// <summary>Bottom centre: one slot per ability with a key cap, its icon, charge pips and a cooldown sweep after use.</summary>
        void DrawAbilityBar(AgentState me, float now)
        {
            const float size = 34f, gap = 34f;
            Ability[] all = Agents.AllAbilities;
            float total = all.Length * size + (all.Length - 1) * gap;
            float x0 = W / 2f - total / 2f;
            float y = H - 92f;
            AbilityCharges kit = Game.PlayerKit();
            bool repaint = Event.current.type == EventType.Repaint;
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
                float used = 1f - Mathf.Clamp01((now - Game.UsedAt[(int)ab]) / 0.9f);
                if (aiming && repaint)
                {
                    Color oldG = GUI.color;
                    GUI.color = new Color(ac.r, ac.g, ac.b, 0.45f * oldG.a);
                    GUI.DrawTexture(new Rect(r.x - 16f, r.y - 16f, r.width + 32f, r.height + 32f), UiTex.Glow, ScaleMode.StretchToFill, true);
                    GUI.color = oldG;
                }
                // The icon sits straight on the world: a dark copy under it for contrast, then the icon.
                Color iconC = usable ? Color.Lerp(Theme.Ink, ac, 0.55f) : new Color(Theme.Muted.r, Theme.Muted.g, Theme.Muted.b, max > 0 ? 0.55f : 0.28f);
                if (used > 0f) iconC = Color.Lerp(iconC, Color.white, used);
                if (denied > 0f) iconC = Color.Lerp(iconC, Palette.Bad, denied);
                float grow = aiming ? 3f : used * 3f;
                var ir = new Rect(r.x - grow, r.y - grow, r.width + grow * 2f, r.height + grow * 2f);
                Icon(new Rect(ir.x + 1.5f, ir.y + 1.5f, ir.width, ir.height), ab, new Color(0f, 0f, 0f, 0.5f * iconC.a));
                Icon(ir, ab, iconC);
                // Charge pips, then the key letter. No labels.
                float pw = 7f, pg = 3f;
                float pipsW = max * pw + Mathf.Max(0, max - 1) * pg;
                for (int p = 0; p < max; p++)
                {
                    var pr = new Rect(r.center.x - pipsW / 2f + p * (pw + pg), r.yMax + 7f, pw, 3f);
                    Fill(new Rect(pr.x + 1f, pr.y + 1f, pr.width, pr.height), new Color(0f, 0f, 0f, 0.45f));
                    Fill(pr, p < charges ? ac : new Color(1f, 1f, 1f, 0.22f));
                }
                Shadowed(new Rect(r.x - 10f, r.yMax + 14f, r.width + 20f, 16f), Agents.KeyLabel(ab), keyCap, usable ? Theme.Ink : Theme.Muted);
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
                Shadowed(new Rect(W / 2f - 250f, y - 36f, 500f, 22f), hintStr, hudCenter, Agents.AbilityColor(ab));
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
            const float mx = 36f;
            float right = W - mx;
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
                reserveStr = g.Reserve >= 0 ? Theme.Int(g.Reserve) : Theme.Int(w.Mag);
            }
            bool low = g.Ammo <= Mathf.Max(1, w.Mag / 4);
            bool reloading = g.Reloading(now);
            Color ammoC = low ? new Color(Palette.Warn.r, Palette.Warn.g, Palette.Warn.b, 0.75f + 0.25f * Mathf.Sin(now * 8f)) : Theme.Ink;
            // Ammo: one large number, the reserve small beside it, over a thin magazine bar.
            float resW = Mathf.Max(34f, TextW(reserveStr, hudNumSmall));
            Shadowed(new Rect(right - resW, H - 80f, resW + 4f, 32f), reserveStr, hudNumSmall, Theme.Muted);
            Shadowed(new Rect(right - resW - 14f - 220f, H - 116f, 220f, 76f), ammoStr, ammoBig, ammoC);
            var bar = new Rect(right - 220f, H - 36f, 220f, 4f);
            Fill(new Rect(bar.x, bar.y + 1f, bar.width + 1f, bar.height), new Color(0f, 0f, 0f, 0.4f));
            Fill(bar, new Color(1f, 1f, 1f, 0.16f));
            if (reloading)
            {
                float p = Mathf.Clamp01(g.ReloadProgress(now));
                Fill(new Rect(bar.xMax - bar.width * p, bar.y, bar.width * p, bar.height), Settings.Accent);
            }
            else
            {
                float f = Mathf.Clamp01(g.Ammo / (float)Mathf.Max(1, w.Mag));
                if (f > 0f) Fill(new Rect(bar.xMax - bar.width * f, bar.y, bar.width * f, bar.height), low ? Palette.Warn : Theme.Ink);
            }

            // One quiet line above: the two slots, the one in hand bright (or the reload).
            var line = new Rect(right - 360f, H - 142f, 360f, 20f);
            if (reloading) Shadowed(line, "RELOADING", weaponName, Settings.Accent);
            else
            {
                string prim = g.Primary, sec = g.Secondary;
                bool onPrim = g.OnPrimary && prim != null;
                int sk2 = (prim ?? "").GetHashCode() * 31 + (sec ?? "").GetHashCode();
                if (sk2 != slotKeyHash || slotA == null)
                {
                    slotKeyHash = sk2;
                    slotA = prim != null ? "1  " + Upper(Weapons.NameOf(prim)) : "";
                    slotB = "2  " + Upper(Weapons.NameOf(sec));
                    slotBW = TextW(slotB, weaponName);
                }
                Shadowed(line, slotB, weaponName, onPrim ? new Color(Theme.Muted.r, Theme.Muted.g, Theme.Muted.b, 0.85f) : Theme.Ink);
                if (slotA.Length > 0) Shadowed(new Rect(line.x, line.y, line.width - slotBW - 18f, line.height), slotA, weaponName, onPrim ? Theme.Ink : new Color(Theme.Muted.r, Theme.Muted.g, Theme.Muted.b, 0.85f));
            }
            if (g.Ammo <= 0 && !reloading) Shadowed(new Rect(W / 2f - 150f, H / 2f + 40f, 300f, 22f), "R  RELOAD", hudCenter, Palette.Warn);
        }

        string slotA, slotB = "";
        float slotBW;
        int slotKeyHash;

        // ---- kill feed -----------------------------------------------------------------------------
        /// <summary>Top right: kills as "killer [weapon] (headshot) (wallbang) victim", other events as text. Entries slide in.</summary>
        void DrawKillFeed(float now)
        {
            float fy = 22f;
            for (int i = Game.Feed.Count - 1; i >= 0; i--)
            {
                FeedItem item = Game.Feed[i];
                float age = now - item.At;
                if (age > 7f) continue;
                float a = Mathf.Clamp01((7f - age) / 1f);
                float slide = (1f - Theme.Ease(age / 0.22f)) * 60f;
                float right = W - 24f + slide;
                if (item.IsKill)
                {
                    if (item.KillerW < 0f) item.KillerW = killName.CalcSize(new GUIContent(item.Killer)).x;
                    if (item.VictimW < 0f) item.VictimW = killName.CalcSize(new GUIContent(item.Victim)).x;
                    const float iconW = 46f, markW = 15f;
                    float marks = (item.Head ? markW + 4f : 0f) + (item.Wallbang ? markW + 4f : 0f);
                    float total = 10f + item.KillerW + 10f + iconW + 4f + marks + 8f + item.VictimW + 12f;
                    var r = new Rect(right - total, fy, total, 24f);
                    bool mine = item.Killer == "You" || item.Victim == "You";
                    Color ac = Settings.Accent;
                    Fill(r, new Color(Theme.Base.r, Theme.Base.g, Theme.Base.b, (mine ? 0.5f : 0.3f) * a));
                    if (mine) Fill(new Rect(r.xMax - 2f, r.y, 2f, r.height), new Color(ac.r, ac.g, ac.b, a));
                    float x = r.x + 10f;
                    Shadowed(new Rect(x, r.y, item.KillerW + 4f, r.height), item.Killer, killName, new Color(item.KillerColor.r, item.KillerColor.g, item.KillerColor.b, a));
                    x += item.KillerW + 10f;
                    Tex(new Rect(x, r.y + 4f, iconW, 16f), GunModels.Icon(item.Weapon), new Color(1f, 1f, 1f, 0.92f * a));
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
                    var r = new Rect(right - item.Width - 22f, fy, item.Width + 22f, 24f);
                    Fill(r, new Color(Theme.Base.r, Theme.Base.g, Theme.Base.b, 0.3f * a));
                    Fill(new Rect(r.xMax - 2f, r.y, 2f, r.height), new Color(item.Color.r, item.Color.g, item.Color.b, a));
                    Shadowed(new Rect(r.x, r.y, r.width - 10f, r.height), item.Text, feed, new Color(Theme.Ink.r, Theme.Ink.g, Theme.Ink.b, a));
                }
                fy += 27f;
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
            const float Dur = 2.2f;
            if (t < 0f || t > Dur || string.IsNullOrEmpty(Game.RoundBannerText)) return;
            if (sweepPlayedFor != Game.RoundBannerAt)
            {
                sweepPlayedFor = Game.RoundBannerAt;
                PlayUi(UiSound.Sting, 0.7f);
            }
            // A thin band that fades up and out; the rule under the title draws from the centre.
            float a = Theme.Ease(t / 0.2f) * Mathf.Clamp01((Dur - t) / 0.3f);
            float y = H * 0.27f;
            Color ac = Settings.Accent;
            Fill(new Rect(0f, y, W, 72f), new Color(Theme.Base.r, Theme.Base.g, Theme.Base.b, 0.78f * a));
            float lk = Theme.Ease(t / 0.35f);
            Fill(new Rect(W / 2f - 90f * lk, y + 70f, 180f * lk, 2f), new Color(ac.r, ac.g, ac.b, a));
            Shadowed(new Rect(0f, y + 6f, W, 44f), Game.RoundBannerText, sweepTitle, new Color(Theme.Ink.r, Theme.Ink.g, Theme.Ink.b, a));
            Lbl(new Rect(0f, y + 47f, W, 18f), Game.RoundBannerSub ?? "", sweepSub, new Color(Theme.Muted.r, Theme.Muted.g, Theme.Muted.b, a));
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
            Dot(new Rect(cx - 2.5f, cy - 2.5f, 5f, 5f), new Color(1f, 0.25f, 0.2f, 0.95f));
        }
    }
}

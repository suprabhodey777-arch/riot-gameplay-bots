// The player's weapons on the client: slots, magazines, reload, draw, scope,
// the spray pattern and movement spread (docs/weapons-economy.md, "Recoil").
// Predicted locally so the gun answers at once; the sim's AgentState fields
// (primary, secondary, slot, ammo, reserve, reloadingUntilTick) win whenever
// they are present and nothing local is in flight. Without them (an older
// bridge) the player has a Lancer and a Pin, as the drills do.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public class Gunplay
    {
        public const float HolsterS = 0.14f;
        /// <summary>Horizontal field of view while scoped, from the Longshot's 2.5x zoom of the player's FOV.</summary>
        public const float ScopeZoom = 2.5f;

        public string Primary = Weapons.Default;
        public string Secondary = Weapons.Sidearm;
        public bool OnPrimary = true;

        /// <summary>The weapon id in hand.</summary>
        public string Current => OnPrimary && !string.IsNullOrEmpty(Primary) ? Primary : (string.IsNullOrEmpty(Secondary) ? Weapons.Sidearm : Secondary);
        public WeaponInfo Weapon => Weapons.Get(Current);

        readonly Dictionary<string, int> mags = new Dictionary<string, int>();
        /// <summary>Spare rounds when the sim says (-1 = unknown, shown as infinite).</summary>
        public int Reserve = -1;

        public float ReloadStart = -10f;
        public float ReloadEnd = -10f;
        bool reloadActive;

        /// <summary>When the last switch began, and the weapon put away (for the holster/draw animation).</summary>
        public float SwitchAt = -10f;
        public string SwitchFrom;
        public float DrawEnd = -10f;

        public bool ScopeWanted;
        public float Scope01;

        float spray;
        float sprayAtRelease;
        bool recovering = true;
        public float LastShotAt = -10f;
        public int ShotIndex;
        /// <summary>Smoothed movement spread (degrees): the shots use it and the crosshair draws it.</summary>
        public float SpreadDeg;
        float lastLocalAmmoChange = -10f;
        float lastLocalSwitch = -10f;

        // For the next input tick.
        public bool PendingReload;
        public string PendingSlot;

        /// <summary>Per-bullet aim offsets of the last planned shot (x pitch up, y yaw right, degrees); Count entries.</summary>
        public readonly Vector2[] Offsets = new Vector2[16];
        public int OffsetCount;

        readonly System.Random rng = new System.Random(4242);

        /// <summary>
        /// An older bridge has no magazines, reloads or second weapon: the Lancer never runs dry and the slots stay put,
        /// so drills play exactly as before.
        /// </summary>
        public bool Legacy;

        public int Ammo
        {
            get
            {
                string id = Current;
                if (mags.TryGetValue(id, out int n)) return n;
                int full = Weapons.Get(id).Mag;
                mags[id] = full;
                return full;
            }
            private set => mags[Current] = value;
        }

        public bool Reloading(float now) => reloadActive && now < ReloadEnd;
        public float ReloadProgress(float now) => !reloadActive ? 0f : Mathf.Clamp01((now - ReloadStart) / Mathf.Max(0.05f, ReloadEnd - ReloadStart));
        public bool Drawing(float now) => now < DrawEnd;
        public bool Scoped => Scope01 > 0.5f;
        public bool ScopedFully => Scope01 > 0.98f;

        /// <summary>The camera kick (x pitch up, y yaw right, degrees): a fraction of the spray offset, recovering with it.</summary>
        public Vector2 Kick
        {
            get
            {
                WeaponInfo w = Weapon;
                // x1.6: the kick was too faint to feel; the bullet still follows the spray pattern itself.
                return w.SprayAt(spray) * w.KickFraction * 1.6f;
            }
        }

        public float SprayPosition => spray;

        /// <summary>A fresh round: default or given loadout, full magazines, primary in hand when there is one.</summary>
        public void Reset(string primary, string secondary)
        {
            Primary = string.IsNullOrEmpty(primary) ? null : primary;
            Secondary = string.IsNullOrEmpty(secondary) ? Weapons.Sidearm : secondary;
            OnPrimary = Primary != null;
            mags.Clear();
            Reserve = -1;
            reloadActive = false;
            ReloadEnd = -10f;
            DrawEnd = Time.unscaledTime + 0.25f;
            SwitchAt = Time.unscaledTime - HolsterS;
            SwitchFrom = null;
            ScopeWanted = false;
            Scope01 = 0f;
            spray = 0f;
            recovering = true;
            LastShotAt = -10f;
            ShotIndex = 0;
            SpreadDeg = 0f;
            PendingReload = false;
            PendingSlot = null;
        }

        /// <summary>A `loadout` answer or a drill loadout: new weapons come with a full magazine.</summary>
        public void SetLoadout(string primary, string secondary)
        {
            string p = string.IsNullOrEmpty(primary) ? null : primary;
            string s = string.IsNullOrEmpty(secondary) ? Weapons.Sidearm : secondary;
            if (p != Primary && p != null) mags.Remove(p);
            if (s != Secondary) mags.Remove(s);
            bool hadPrimary = Primary != null;
            Primary = p;
            Secondary = s;
            if (Primary == null) OnPrimary = false;
            else if (!hadPrimary) OnPrimary = true;
        }

        /// <summary>Takes the sim's fields when present (an older bridge leaves them all out).</summary>
        public void Sync(AgentState me, SimState s, float tickRate, float now)
        {
            if (me == null) return;
            // `secondary` is always set by a bots-wire.3 sim (every player has a Pin), so it marks the fields as present.
            if (!string.IsNullOrEmpty(me.secondary))
            {
                if (me.primary != Primary || me.secondary != Secondary) SetLoadout(me.primary, me.secondary);
            }
            if (!string.IsNullOrEmpty(me.slot) && now - lastLocalSwitch > 0.5f)
            {
                bool p = me.slot != "secondary" && Primary != null;
                if (p != OnPrimary) BeginSwitch(p, now, false);
            }
            int? reserveNow = me.ReserveInHand;
            int? ammoNow = me.AmmoInHand;
            if (reserveNow.HasValue) Reserve = reserveNow.Value;
            if (ammoNow.HasValue && now - lastLocalAmmoChange > 0.4f && now - lastLocalSwitch > 0.5f && !reloadActive)
                Ammo = Mathf.Max(0, ammoNow.Value);
            if (s != null && me.reloadingUntilTick > s.tick && !reloadActive)
            {
                float left = (me.reloadingUntilTick - s.tick) / Mathf.Max(1f, tickRate);
                reloadActive = true;
                ReloadEnd = now + left;
                ReloadStart = ReloadEnd - Weapon.ReloadS;
            }
        }

        public void RequestSlot(bool primary, float now)
        {
            if (Legacy) return;
            if (primary && Primary == null) return;
            if (primary == OnPrimary) return;
            BeginSwitch(primary, now, true);
        }

        public void Cycle(float now)
        {
            if (Legacy || Primary == null) return;
            BeginSwitch(!OnPrimary, now, true);
        }

        void BeginSwitch(bool primary, float now, bool local)
        {
            SwitchFrom = Current;
            OnPrimary = primary;
            SwitchAt = now;
            DrawEnd = now + HolsterS + Weapon.DrawS;
            reloadActive = false;
            ReloadEnd = -10f;
            ScopeWanted = false;
            spray = 0f;
            recovering = true;
            if (local)
            {
                lastLocalSwitch = now;
                PendingSlot = primary ? "primary" : "secondary";
            }
        }

        public void RequestReload(float now)
        {
            if (Legacy || reloadActive || Drawing(now)) return;
            WeaponInfo w = Weapon;
            if (Ammo >= w.Mag || Reserve == 0) return;
            reloadActive = true;
            ReloadStart = now;
            ReloadEnd = now + w.ReloadS;
            ScopeWanted = false;
            PendingReload = true;
        }

        /// <summary>Per frame: reload completion, spray recovery, movement spread and the scope.</summary>
        public void Tick(float now, float dt, bool triggerHeld, bool moving, string moveMode, bool dashing, bool scopeHeld)
        {
            WeaponInfo w = Weapon;
            if (reloadActive && now >= ReloadEnd)
            {
                reloadActive = false;
                int need = w.Mag - Ammo;
                int add = Reserve < 0 ? need : Mathf.Min(need, Reserve);
                Ammo += add;
                if (Reserve > 0) Reserve -= add;
                lastLocalAmmoChange = now;
            }

            // Recovery starts when the trigger is released (or a semi-automatic's shot has had its interval).
            bool spraying = triggerHeld && w.Auto && now - LastShotAt < w.FireIntervalS * 1.8f;
            if (!spraying)
            {
                if (!recovering)
                {
                    recovering = true;
                    sprayAtRelease = Mathf.Max(1f, spray);
                }
                spray = Mathf.Max(0f, spray - dt * sprayAtRelease / Mathf.Max(0.02f, w.RecoveryS));
            }

            ScopeWanted = scopeHeld && w.Scopes && !reloadActive && !Drawing(now);
            Scope01 = Mathf.MoveTowards(Scope01, ScopeWanted ? 1f : 0f, dt / 0.14f);

            float mult = 0f;
            if (dashing || PlayerRig.Airborne) mult = 6f; // jumping is as inaccurate as dashing
            else if (moving) mult = moveMode == "crouch" ? 0.5f : moveMode == "walk" ? 1f : 4f;
            // The Longshot is very inaccurate unless still and scoped.
            if (w.Scopes && !ScopedFully) mult += 1f;
            float target = w.BaseSpreadDeg * mult;
            SpreadDeg = Mathf.Lerp(SpreadDeg, target, 1f - Mathf.Exp(-dt * (target > SpreadDeg ? 18f : 10f)));

            if (Ammo <= 0 && !reloadActive && !Drawing(now) && now - LastShotAt > 0.2f) RequestReload(now);
        }

        public bool CanFire(float now)
        {
            return !reloadActive && !Drawing(now) && Ammo > 0;
        }

        /// <summary>
        /// Plans one trigger pull: the spray offset of this bullet plus random movement spread (and the pellet cone),
        /// into Offsets. Spends a round and advances the spray.
        /// </summary>
        public void PlanShot(float now)
        {
            WeaponInfo w = Weapon;
            Vector2 baseOff = w.SprayAt(spray);
            int n = Mathf.Clamp(w.Pellets, 1, Offsets.Length);
            for (int i = 0; i < n; i++)
            {
                Vector2 off = baseOff + Disc(SpreadDeg);
                if (n > 1) off += Disc(w.PelletConeDeg);
                Offsets[i] = off;
            }
            OffsetCount = n;
            if (!Legacy) Ammo = Mathf.Max(0, Ammo - 1);
            lastLocalAmmoChange = now;
            LastShotAt = now;
            ShotIndex++;
            recovering = false;
            spray = Mathf.Min(spray + 1f, Mathf.Max(1, w.Spray.Count) + 4f);
        }

        Vector2 Disc(float radius)
        {
            if (radius <= 0f) return Vector2.zero;
            double a = rng.NextDouble() * System.Math.PI * 2.0;
            float r = radius * Mathf.Sqrt((float)rng.NextDouble());
            return new Vector2((float)System.Math.Sin(a) * r, (float)System.Math.Cos(a) * r);
        }
    }
}

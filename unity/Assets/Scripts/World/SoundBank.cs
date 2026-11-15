// The recorded sounds: every gameplay cue is a real recording shipped in
// Assets/Resources/Audio (licences in CREDITS.md there), addressed by a stable
// key. A key with no imported file warns once and falls back to the clip the
// project synthesised before, so a fresh clone or a failed import is quiet
// rather than silent, and Get never returns null.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public static class SoundBank
    {
        class Cue
        {
            /// <summary>Resources paths without extension; several paths are random variants.</summary>
            public readonly string[] Paths;
            /// <summary>The synthesised clip this cue replaced, used when no file loads.</summary>
            public readonly Func<AudioClip> Fallback;

            public Cue(Func<AudioClip> fallback, params string[] paths)
            {
                Fallback = fallback;
                Paths = paths;
            }
        }

        static readonly Dictionary<string, Cue> Cues = new Dictionary<string, Cue>
        {
            // Guns, one recording per weapon class (CC-BY 3.0 unless the credits say CC0).
            { "gun.pistol", new Cue(() => Synth.GunFor(WeaponClass.Sidearm), "Audio/Guns/pistol_shot_01") },
            { "gun.heavypistol", new Cue(() => Synth.GunFor(WeaponClass.HeavyPistol), "Audio/Guns/pistol_shot_01") },
            { "gun.smg", new Cue(() => Synth.GunFor(WeaponClass.Smg), "Audio/Guns/smg_shot_01") },
            { "gun.rifle", new Cue(() => Synth.GunFor(WeaponClass.Rifle), "Audio/Guns/rifle_shot_01") },
            { "gun.shotgun", new Cue(() => Synth.GunFor(WeaponClass.Shotgun), "Audio/Guns/shotgun_shot_01") },
            { "gun.sniper", new Cue(() => Synth.GunFor(WeaponClass.Sniper), "Audio/Guns/sniper_shot_01") },

            // Weapon handling: the reload's stages, the draw, a dry trigger.
            { "reload.magout", new Cue(() => Synth.Click(1, 900f), "Audio/Reload/mag_out_01") },
            { "reload.magin", new Cue(() => Synth.Click(2, 1400f), "Audio/Reload/mag_in_01") },
            { "reload.slide", new Cue(() => Synth.Click(3, 600f), "Audio/Reload/slide_01") },
            { "reload.shell", new Cue(() => Synth.Click(4, 1200f), "Audio/Reload/shell_in_01") },
            { "reload.dryfire", new Cue(() => Synth.Click(5, 700f), "Audio/Reload/dry_fire_01") },
            { "weapon.draw", new Cue(() => Synth.Whoosh(0.18f, 7), "Audio/Foley/weapon_draw_01") },

            // Small foley: a brass case on the floor, the planted spike's pulse.
            { "foley.shell", new Cue(() => Synth.Tink(), "Audio/Foley/shell_tink_01") },
            { "foley.spike", new Cue(() => Synth.Beep(), "Audio/Foley/spike_beep_01") },

            // Four concrete steps; Pick chooses one per step.
            { "footstep.concrete", new Cue(() => Synth.Footstep(0),
                "Audio/Footsteps/footstep_concrete_01", "Audio/Footsteps/footstep_concrete_02",
                "Audio/Footsteps/footstep_concrete_03", "Audio/Footsteps/footstep_concrete_04") },

            // Bullet impacts: plate/armour for a wall, a punch for a body, a ding to confirm a headshot.
            { "impact.wall", new Cue(() => Synth.Tick(),
                "Audio/Impacts/wall_hit_01", "Audio/Impacts/wall_hit_02", "Audio/Impacts/wall_hit_03") },
            { "impact.body", new Cue(() => Synth.Footstep(1),
                "Audio/Impacts/body_hit_01", "Audio/Impacts/body_hit_02", "Audio/Impacts/body_hit_03") },
            { "impact.headshot", new Cue(() => Synth.Tink(), "Audio/Impacts/headshot_01") },

            // Abilities.
            { "ability.throw", new Cue(() => Synth.Whoosh(0.22f, 1), "Audio/Abilities/throw_whoosh_01") },
            { "ability.dash", new Cue(() => Synth.Whoosh(0.32f, 2), "Audio/Abilities/dash_whoosh_01") },
            { "ability.flash", new Cue(() => Synth.FlashPop(), "Audio/Abilities/flash_pop_01") },
            { "ability.smoke", new Cue(() => Synth.Whoosh(0.5f, 4), "Audio/Abilities/smoke_deploy_01") },
            { "ability.dart", new Cue(() => Synth.DartThunk(), "Audio/Abilities/dart_thunk_01") },
            { "ability.ping", new Cue(() => Synth.ReconPing(), "Audio/Abilities/recon_ping_01") },

            // UI, for the menus and the buy phase.
            { "ui.click", new Cue(() => Synth.Click(7, 1100f), "Audio/UI/click_01") },
            { "ui.hover", new Cue(() => Synth.Click(8, 1700f), "Audio/UI/hover_01") },
            { "ui.confirm", new Cue(() => Synth.Beep(), "Audio/UI/confirm_01") },
            { "ui.cancel", new Cue(() => Synth.Beep(), "Audio/UI/cancel_01") },
        };

        static readonly Dictionary<string, AudioClip[]> Loaded = new Dictionary<string, AudioClip[]>();
        static readonly HashSet<string> Warned = new HashSet<string>();

        /// <summary>Every key the game knows; used by the audio check.</summary>
        public static IEnumerable<string> Keys => Cues.Keys;

        /// <summary>The gunshot key for a weapon class.</summary>
        public static string GunKey(WeaponClass c)
        {
            switch (c)
            {
                case WeaponClass.Sidearm: return "gun.pistol";
                case WeaponClass.HeavyPistol: return "gun.heavypistol";
                case WeaponClass.Smg: return "gun.smg";
                case WeaponClass.Shotgun: return "gun.shotgun";
                case WeaponClass.Sniper: return "gun.sniper";
                default: return "gun.rifle";
            }
        }

        /// <summary>The clip for a key (the first variant). Never null.</summary>
        public static AudioClip Get(string key)
        {
            AudioClip[] clips = Variants(key);
            return clips.Length > 0 ? clips[0] : Synth.Tick();
        }

        /// <summary>A random variant for a key: footsteps, wall hits, body hits. Never null.</summary>
        public static AudioClip Pick(string key)
        {
            AudioClip[] clips = Variants(key);
            if (clips.Length == 0) return Synth.Tick();
            return clips[UnityEngine.Random.Range(0, clips.Length)];
        }

        static AudioClip[] Variants(string key)
        {
            if (Loaded.TryGetValue(key, out AudioClip[] found)) return found;
            if (!Cues.TryGetValue(key, out Cue cue))
            {
                WarnOnce("key:" + key, "SoundBank: no such sound key '" + key + "'");
                return new AudioClip[0];
            }
            var list = new List<AudioClip>(cue.Paths.Length);
            foreach (string path in cue.Paths)
            {
                AudioClip clip = Resources.Load<AudioClip>(path);
                if (clip != null) list.Add(clip);
                else WarnOnce(path, "SoundBank: nothing at Resources/" + path + " for '" + key + "'; playing the synthesized fallback");
            }
            if (list.Count == 0)
            {
                // Not cached, so a file that was still importing gets picked up by a later call.
                AudioClip synth = cue.Fallback != null ? cue.Fallback() : null;
                return synth != null ? new[] { synth } : new AudioClip[0];
            }
            found = list.ToArray();
            Loaded[key] = found;
            return found;
        }

        static void WarnOnce(string token, string message)
        {
            if (!Warned.Add(token)) return;
            Debug.LogWarning(message);
        }
    }
}

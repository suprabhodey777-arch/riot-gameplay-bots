// The first-person weapon: a second camera (layer 31 only, depth cleared, its
// own field of view from the settings) draws the gun in hand over the world.
// Animations are procedural: holster and draw on a switch, a reload with the
// magazine dropping out and a new one rising in (the Hammer swings its drum,
// the Brick feeds shells), recoil kick with a spring, slide/bolt/pump cycling,
// sway with the mouse, bob with movement, and the scope raise for the Longshot.
// Muzzle flashes are additive star quads sized per weapon; shells go into the
// world through ImpactFx.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    /// <summary>Turns shadows off while the weapon camera renders (its own shadow pass would only cost time).</summary>
    public class NoShadowCamera : MonoBehaviour
    {
        float saved;

        void OnPreCull()
        {
            saved = QualitySettings.shadowDistance;
            QualitySettings.shadowDistance = 0f;
        }

        void OnPostRender()
        {
            QualitySettings.shadowDistance = saved;
        }
    }

    public class Viewmodel : MonoBehaviour
    {
        public const int Layer = 31;

        public Gunplay Gun;
        public ImpactFx Impacts;
        public Camera WeaponCam { get; private set; }

        Transform anchor;
        readonly Dictionary<string, GunModel> models = new Dictionary<string, GunModel>();
        readonly Dictionary<string, GameObject> armsFor = new Dictionary<string, GameObject>();
        GunModel shown;
        bool visible = true;

        Transform flashRoot;
        Material flashMat;
        Light flashLight;
        float flashUntil;

        AudioSource audioSrc;
        readonly Dictionary<WeaponClass, AudioClip> shotClips = new Dictionary<WeaponClass, AudioClip>();
        AudioClip magOut, magIn, slideClip, shellClip, drawClip, dryFireClip;
        bool triggerHeld;

        float kickZ, kickZVel, kickPitch, kickPitchVel;
        /// <summary>Uniform scale of every first-person gun and its hands.</summary>
        const float ViewmodelScale = 0.62f;
        Vector2 sway;
        float bobPhase;
        float cycleAt = -10f;
        bool cycleEjected = true;
        float lastReloadP;
        float lastSwitchSeen = -20f;
        Material armsMat;

        public void Init(Camera main)
        {
            var go = new GameObject("Weapon camera");
            go.transform.SetParent(main.transform, false);
            WeaponCam = go.AddComponent<Camera>();
            WeaponCam.clearFlags = CameraClearFlags.Depth;
            WeaponCam.cullingMask = 1 << Layer;
            WeaponCam.depth = main.depth + 1f;
            WeaponCam.nearClipPlane = 0.01f;
            WeaponCam.farClipPlane = 4f;
            WeaponCam.allowHDR = false;
            WeaponCam.allowMSAA = true;
            WeaponCam.useOcclusionCulling = false;
            WeaponCam.fieldOfView = Settings.ViewmodelFov;
            go.AddComponent<NoShadowCamera>();
            main.cullingMask &= ~(1 << Layer);

            anchor = new GameObject("Viewmodel anchor") { layer = Layer }.transform;
            anchor.SetParent(main.transform, false);

            flashMat = Mats.Additive(Textures.FlashStar(), Color.white, 2.4f, 3200);
            flashRoot = new GameObject("Muzzle flash") { layer = Layer }.transform;
            flashRoot.SetParent(anchor, false);
            FlashQuad(Vector3.zero, Quaternion.identity, 1f, 1f);
            FlashQuad(new Vector3(0f, 0f, 0.5f), Quaternion.Euler(0f, 90f, 0f), 1f, 2f);
            FlashQuad(new Vector3(0f, 0f, 0.5f), Quaternion.Euler(0f, 90f, 90f), 1f, 2f);
            var lgo = new GameObject("Flash light");
            lgo.transform.SetParent(flashRoot, false);
            flashLight = lgo.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.range = 7f;
            flashLight.intensity = 3f;
            flashLight.shadows = LightShadows.None;
            flashLight.renderMode = LightRenderMode.ForcePixel;
            flashRoot.gameObject.SetActive(false);

            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false;
            audioSrc.spatialBlend = 0f;
            foreach (WeaponClass c in System.Enum.GetValues(typeof(WeaponClass))) shotClips[c] = SoundBank.Get(SoundBank.GunKey(c));
            magOut = SoundBank.Get("reload.magout");
            magIn = SoundBank.Get("reload.magin");
            slideClip = SoundBank.Get("reload.slide");
            shellClip = SoundBank.Get("reload.shell");
            drawClip = SoundBank.Get("weapon.draw");
            dryFireClip = SoundBank.Get("reload.dryfire");
            armsMat = Mats.Gun(0.5f, 0.3f, 0.45f);
        }

        void FlashQuad(Vector3 pos, Quaternion rot, float w, float h)
        {
            var go = new GameObject("Flash quad") { layer = Layer };
            go.transform.SetParent(flashRoot, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = new Vector3(w, h, 1f);
            var k = new MeshKit(1);
            k.QuadUv(0, new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), Vector3.back, Color.white,
                     new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            go.AddComponent<MeshFilter>().sharedMesh = k.ToMesh("Flash quad");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = flashMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        public void SetVisible(bool v)
        {
            visible = v;
            if (anchor != null && anchor.gameObject.activeSelf != v) anchor.gameObject.SetActive(v);
            if (WeaponCam != null && WeaponCam.enabled != v) WeaponCam.enabled = v;
        }

        GunModel ModelFor(string id)
        {
            if (models.TryGetValue(id, out GunModel m) && m != null) return m;
            m = GunModels.Build(id, anchor, Layer, true, false);
            m.Root.SetActive(false);
            AttachArms(m);
            models[id] = m;
            return m;
        }

        /// <summary>
        /// Gloved hands and sleeves on the grip and the support point (one merged mesh per weapon): tapered lofted
        /// forearms with a cuff and a team-coloured band, gloves with a palm, wrapped fingers, a thumb and a knuckle plate.
        /// </summary>
        void AttachArms(GunModel m)
        {
            GunDef d = m.Def;
            var k = new MeshKit(1) { UvScale = 8f };
            var glove = new Color(0.1f, 0.095f, 0.09f, 0.3f);
            var knuckle = new Color(0.16f, 0.16f, 0.17f, 0.8f);
            var sleeve = new Color(0.19f, 0.21f, 0.24f, 0f);
            var cuff = new Color(0.13f, 0.14f, 0.16f, 0f);
            var band = new Color(0.18f, 0.6f, 0.58f, 0f);
            Matrix4x4 id = Matrix4x4.identity;
            // Right hand on the grip, its forearm running back and down to the right.
            Vector3 g = d.Grip;
            Vector3 elbowR = g + new Vector3(0.07f, -0.12f, -0.3f);
            Vector3 wristR = g + (elbowR - g).normalized * 0.05f;
            Loft.Rod(k, id, wristR, elbowR, 0.031f, 0.044f, sleeve, 14);
            Loft.Band(k, Loft.Along(id, wristR, elbowR), 0.0f, 0.035f, 0.035f, 0.035f, cuff, 0f, 0f, 2f, 14);
            Loft.Band(k, Loft.Along(id, wristR, elbowR), 0.11f, 0.13f, 0.04f, 0.04f, band, 0f, 0f, 2f, 14);
            Loft.Blob(k, id, g + new Vector3(0.006f, 0.004f, -0.004f), new Vector3(0.026f, 0.04f, 0.034f), glove, new Vector3(16f, 0f, 0f), 12, 8);
            Loft.Blob(k, id, g + new Vector3(-0.012f, -0.012f, 0.026f), new Vector3(0.022f, 0.036f, 0.02f), glove, new Vector3(16f, 0f, 0f), 10, 6, 2.6f);
            Loft.Rod(k, id, g + new Vector3(-0.016f, 0.026f, -0.01f), g + new Vector3(-0.02f, 0.03f, 0.03f), 0.01f, 0.009f, glove, 8);
            k.Box(0, MeshKit.At(g + new Vector3(0.022f, -0.004f, 0.012f), new Vector3(16f, 0f, 0f)), new Vector3(0.008f, 0.045f, 0.03f), 0.003f, knuckle);
            // Left hand on the support point, its forearm back and down to the left.
            Vector3 s = d.Support;
            Vector3 elbowL = s + new Vector3(-0.12f, -0.12f, -0.26f);
            Vector3 wristL = s + (elbowL - s).normalized * 0.055f;
            Loft.Rod(k, id, wristL, elbowL, 0.031f, 0.044f, sleeve, 14);
            Loft.Band(k, Loft.Along(id, wristL, elbowL), 0.0f, 0.035f, 0.035f, 0.035f, cuff, 0f, 0f, 2f, 14);
            Loft.Blob(k, id, s + new Vector3(-0.012f, -0.012f, -0.004f), new Vector3(0.03f, 0.026f, 0.04f), glove, Vector3.zero, 12, 8);
            Loft.Blob(k, id, s + new Vector3(0.004f, -0.004f, 0.024f), new Vector3(0.026f, 0.02f, 0.024f), glove, new Vector3(0f, 0f, -20f), 10, 6, 2.6f);
            Loft.Rod(k, id, s + new Vector3(-0.02f, 0.006f, 0.0f), s + new Vector3(0.002f, 0.016f, 0.03f), 0.009f, 0.008f, glove, 8);
            k.Box(0, MeshKit.At(s + new Vector3(-0.03f, -0.006f, 0.012f), Vector3.zero), new Vector3(0.008f, 0.03f, 0.045f), 0.003f, knuckle);
            var go = new GameObject("Arms") { layer = Layer };
            go.transform.SetParent(m.Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = k.ToMesh("Arms " + d.Id);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = armsMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            armsFor[d.Id] = go;
        }

        /// <summary>The world point the shots appear to leave from (for tracers).</summary>
        public Vector3 MuzzleWorld(Transform eye)
        {
            float side = Settings.ViewmodelLeft ? -1f : 1f;
            return eye.position + eye.forward * 0.55f + eye.right * (0.13f * side) - eye.up * 0.1f;
        }

        static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        static float Bump(float t, float a, float b)
        {
            if (t <= a || t >= b) return 0f;
            return Mathf.Sin((t - a) / (b - a) * Mathf.PI);
        }

        /// <summary>Per frame from PlayerRig.Place: picks the model, animates it. `speed01` is ground speed / run speed.</summary>
        public void Frame(float dt, float speed01, bool crouch, Vector2 lookDelta)
        {
            if (Gun == null || anchor == null) return;
            float now = Time.unscaledTime;
            if (WeaponCam != null) WeaponCam.fieldOfView = Settings.ViewmodelFov;

            // Holster the old weapon, then draw the new one.
            bool holstering = now < Gun.SwitchAt + Gunplay.HolsterS && !string.IsNullOrEmpty(Gun.SwitchFrom);
            string id = holstering ? Gun.SwitchFrom : Gun.Current;
            GunModel m = ModelFor(id);
            if (m != shown)
            {
                if (shown != null) shown.Root.SetActive(false);
                shown = m;
                shown.Root.SetActive(true);
                flashRoot.SetParent(shown.MuzzlePoint, false);
                flashRoot.localPosition = Vector3.zero;
                flashRoot.localRotation = Quaternion.identity;
            }
            if (Gun.SwitchAt != lastSwitchSeen)
            {
                lastSwitchSeen = Gun.SwitchAt;
                audioSrc.PlayOneShot(drawClip, 0.35f);
            }
            float away;
            if (holstering) away = Ease((now - Gun.SwitchAt) / Gunplay.HolsterS);
            else
            {
                float start = Gun.SwitchAt + Gunplay.HolsterS;
                float len = Mathf.Max(0.05f, Gun.DrawEnd - start);
                away = 1f - Ease((now - start) / len);
            }

            GunDef d = m.Def;
            WeaponInfo w = Weapons.Get(m.Id);

            // Recoil spring.
            const float k = 220f, damp = 22f;
            kickZVel += (-k * kickZ - damp * kickZVel) * dt;
            kickZ += kickZVel * dt;
            kickPitchVel += (-k * kickPitch - damp * kickPitchVel) * dt;
            kickPitch += kickPitchVel * dt;

            // Sway against the mouse, bob with the stride.
            Vector2 swayTarget = new Vector2(Mathf.Clamp(-lookDelta.x * 0.0022f, -0.03f, 0.03f), Mathf.Clamp(-lookDelta.y * 0.0022f, -0.03f, 0.03f));
            sway = Vector2.Lerp(sway, swayTarget, 1f - Mathf.Exp(-dt * 12f));
            bobPhase += dt * Mathf.Lerp(0f, 10.5f, speed01);
            float bobX = Mathf.Cos(bobPhase * 0.5f) * 0.007f * speed01;
            float bobY = -Mathf.Abs(Mathf.Sin(bobPhase * 0.5f)) * 0.008f * speed01;
            float breathe = Mathf.Sin(now * 1.6f) * 0.0018f;

            // Reload pose and parts.
            float rp = Gun.Reloading(now) ? Gun.ReloadProgress(now) : 0f;
            float tilt = rp > 0f ? Mathf.Clamp01(rp / 0.15f) * Mathf.Clamp01((1f - rp) / 0.15f) : 0f;
            tilt = tilt * tilt * (3f - 2f * tilt);
            AnimateReload(m, rp, now);
            if (rp > 0f && lastReloadP <= 0f) audioSrc.PlayOneShot(slideClip, 0.4f);
            lastReloadP = rp;

            // A pull on an empty gun with nothing in reserve: the hammer falls on a dry chamber.
            bool trigger = GameInput.FireHeld;
            if (trigger && !triggerHeld && Gun.Ammo <= 0 && Gun.Reserve == 0 && rp <= 0f && !Gun.Drawing(now))
                audioSrc.PlayOneShot(dryFireClip, 0.5f);
            triggerHeld = trigger;

            // Slide / bolt / pump cycle.
            if (m.Slide != null)
            {
                float since = now - cycleAt;
                float cyc = since >= 0f && since < d.CycleS ? Mathf.Sin(since / d.CycleS * Mathf.PI) : 0f;
                float reloadPull = m.Id == "hammer" ? 0f : Bump(rp, 0.78f, 0.92f);
                m.Slide.localPosition = m.SlideHome + new Vector3(0f, 0f, -d.CycleTravel * Mathf.Max(cyc, reloadPull));
                if (!cycleEjected && since >= d.CycleS * 0.45f)
                {
                    cycleEjected = true;
                    Eject(m, 1, w.Class == WeaponClass.Shotgun ? 1.8f : 1f);
                    if (d.CycleDelay > 0f) audioSrc.PlayOneShot(slideClip, 0.5f);
                }
            }

            // Scope raise (Longshot): towards the centre, then the overlay takes over.
            float scope = Gun.Scope01;
            float side = Settings.ViewmodelLeft ? -1f : 1f;
            Vector3 user = new Vector3(Settings.ViewmodelX * 0.04f, Settings.ViewmodelY * 0.04f, Settings.ViewmodelZ * 0.04f);
            Vector3 hold = d.Hold + user;
            // Sit lower and further right than the model's own hold, so the gun stays in the lower-right corner.
            hold += new Vector3(0.035f, -0.03f, 0f);
            hold.x *= side;
            Vector3 pos = hold + new Vector3(bobX + sway.x, bobY + breathe + sway.y + (crouch ? -0.01f : 0f), -kickZ);
            pos += new Vector3(-0.02f * side, 0.012f, 0f) * tilt;
            pos += new Vector3(0f, -0.24f, -0.05f) * away;
            pos = Vector3.Lerp(pos, new Vector3(0f, -0.07f, 0.16f), scope * 0.85f);
            anchor.localPosition = pos;
            anchor.localRotation = Quaternion.Euler(-kickPitch - 8f * tilt + 38f * away + sway.y * 120f, -1.5f * side + sway.x * -120f, (22f * tilt - 18f * away) * side);
            // At full size the guns filled a quarter of the screen; 62% is a normal first-person size.
            anchor.localScale = new Vector3(side, 1f, 1f) * ViewmodelScale;
            bool showGun = visible && scope < 0.9f;
            if (m.Root.activeSelf != showGun) m.Root.SetActive(showGun);

            if (flashRoot.gameObject.activeSelf && now >= flashUntil) flashRoot.gameObject.SetActive(false);
        }

        void AnimateReload(GunModel m, float rp, float now)
        {
            if (m.Mag == null) return;
            if (rp <= 0f)
            {
                m.Mag.localPosition = m.MagHome;
                m.Mag.localRotation = Quaternion.identity;
                if (!m.Mag.gameObject.activeSelf) m.Mag.gameObject.SetActive(true);
                return;
            }
            switch (m.Id)
            {
                case "hammer":
                {
                    // The drum swings out to the left, spins, and swings back.
                    float o = Bump(rp, 0.12f, 0.85f);
                    o = Mathf.Clamp01(o * 1.6f);
                    m.Mag.localPosition = m.MagHome + new Vector3(-0.045f * o, -0.012f * o, 0f);
                    m.Mag.localRotation = Quaternion.Euler(0f, 0f, rp * 720f * o);
                    if (Cross(rp, 0.3f)) { audioSrc.PlayOneShot(magOut, 0.5f); Eject(m, 6, 1f); }
                    if (Cross(rp, 0.72f)) audioSrc.PlayOneShot(magIn, 0.55f);
                    break;
                }
                case "brick":
                {
                    // A shell pushed up into the loading port, three times.
                    float t = Mathf.Clamp01((rp - 0.15f) / 0.7f);
                    float frac = Mathf.Repeat(t * 3f, 1f);
                    bool active = rp > 0.15f && rp < 0.85f;
                    if (m.Mag.gameObject.activeSelf != active) m.Mag.gameObject.SetActive(active);
                    m.Mag.localPosition = m.MagHome + new Vector3(0f, -0.07f * (1f - frac), -0.02f * (1f - frac));
                    if (Cross(rp, 0.15f + 0.7f / 3f) || Cross(rp, 0.15f + 1.4f / 3f) || Cross(rp, 0.85f)) audioSrc.PlayOneShot(shellClip, 0.5f);
                    break;
                }
                default:
                {
                    // Magazine drops out, a new one rises in and is seated.
                    Vector3 off = Vector3.zero;
                    bool vis = true;
                    if (rp >= 0.12f && rp < 0.34f)
                    {
                        float t = (rp - 0.12f) / 0.22f;
                        off = new Vector3(0f, -0.28f * t * t, -0.03f * t);
                    }
                    else if (rp >= 0.34f && rp < 0.45f) vis = false;
                    else if (rp >= 0.45f && rp < 0.68f)
                    {
                        float t = 1f - Ease((rp - 0.45f) / 0.23f);
                        off = new Vector3(0f, -0.2f * t, -0.02f * t);
                    }
                    else if (rp >= 0.68f && rp < 0.74f) off = new Vector3(0f, 0.004f * Bump(rp, 0.68f, 0.74f), 0f);
                    if (m.Mag.gameObject.activeSelf != vis) m.Mag.gameObject.SetActive(vis);
                    m.Mag.localPosition = m.MagHome + off;
                    m.Mag.localRotation = Quaternion.Euler(rp < 0.34f ? 10f * Mathf.Clamp01((rp - 0.12f) / 0.22f) : 0f, 0f, 0f);
                    if (Cross(rp, 0.12f)) audioSrc.PlayOneShot(magOut, 0.5f);
                    if (Cross(rp, 0.68f)) audioSrc.PlayOneShot(magIn, 0.6f);
                    if (Cross(rp, 0.8f)) audioSrc.PlayOneShot(slideClip, 0.5f);
                    break;
                }
            }
        }

        bool Cross(float rp, float at)
        {
            return lastReloadP < at && rp >= at;
        }

        void Eject(GunModel m, int count, float scale)
        {
            if (Impacts == null || m.EjectPoint == null) return;
            Transform cam = transform;
            float side = Settings.ViewmodelLeft ? -1f : 1f;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = cam.right * side * Random.Range(1.4f, 2.2f) + cam.up * Random.Range(1.0f, 1.8f) - cam.forward * Random.Range(0.1f, 0.5f);
                Impacts.Shell(m.EjectPoint.position, v, scale);
            }
        }

        /// <summary>A shot left the gun: recoil kick, flash, sound, slide cycle and the shell.</summary>
        public void OnShot(WeaponInfo w)
        {
            GunModel m = ModelFor(w.Id);
            GunDef d = m.Def;
            float scoped = Gun != null ? Gun.Scope01 : 0f;
            kickZVel += d.KickBack * 22f * (1f - 0.6f * scoped);
            kickPitchVel += d.KickPitch * 22f * (1f - 0.6f * scoped);
            flashRoot.gameObject.SetActive(scoped < 0.9f);
            flashRoot.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float s = d.FlashSize * Random.Range(0.85f, 1.15f);
            flashRoot.localScale = new Vector3(s, s, s * d.FlashLength);
            flashMat.color = d.FlashColor;
            flashLight.color = d.FlashColor;
            flashLight.intensity = 2f + d.FlashSize * 18f;
            flashUntil = Time.unscaledTime + (w.Class == WeaponClass.Shotgun || w.Class == WeaponClass.Sniper ? 0.06f : 0.04f);
            if (shotClips.TryGetValue(w.Class, out AudioClip clip))
            {
                audioSrc.pitch = Random.Range(0.96f, 1.04f);
                audioSrc.PlayOneShot(clip, w.Class == WeaponClass.Sniper || w.Class == WeaponClass.Shotgun ? 1f : 0.85f);
            }
            if (m.Slide != null)
            {
                cycleAt = Time.unscaledTime + d.CycleDelay;
                cycleEjected = w.Id == "hammer";
            }
            else if (w.Id != "hammer") Eject(m, 1, 1f);
        }
    }
}

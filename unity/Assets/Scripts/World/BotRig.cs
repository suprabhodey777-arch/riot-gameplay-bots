// A stylised armoured humanoid built from chamfered boxes: a dark suit, armour
// plates in the team colour (the player's ally / enemy setting), shoulder
// stripes, pack and helmet crest in the agent's colour, and a glowing visor.
// Each bone is one merged vertex-coloured mesh on one shared material, so a bot
// costs about fourteen draw calls. Procedural animation: idle breathing, a run
// cycle that follows the direction of travel (forward, back, strafe), crouch,
// a recoil kick when it fires, and a fall when it dies. Standing height 1.8 m,
// head at 1.65 m, matching the hit colliders.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    public class BotRig
    {
        static Material bodyMat;
        static readonly Dictionary<Color, Material> visorMats = new Dictionary<Color, Material>();
        static Material flashMat;

        public readonly Transform Root;
        readonly Transform pelvis, chest, head, armL, foreL, armR, foreR, thighL, shinL, thighR, shinR, gunMount;
        readonly List<MeshFilter> filters = new List<MeshFilter>();
        readonly List<Mesh> meshes = new List<Mesh>();
        Color team;
        Color accent;
        GunModel gun;
        string gunId;
        Transform flash;
        float flashUntil;

        float phase;
        float moveBlend;
        float fwdBlend, sideBlend;
        float crouchBlend;
        float shotAt = -10f;
        float lean;

        const float HipY = 0.95f;

        public static Material BodyMaterial
        {
            get
            {
                if (bodyMat == null) bodyMat = Mats.World(Color.white, Textures.Grain(), null, 0f, 0.28f, 0.55f, 0.6f, 1f);
                return bodyMat;
            }
        }

        static Material VisorMat(Color c)
        {
            if (visorMats.TryGetValue(c, out Material m) && m != null) return m;
            m = Mats.Glow(c, 2f);
            visorMats[c] = m;
            return m;
        }

        public BotRig(Transform parent, Color teamColor, Color agentColor)
        {
            team = teamColor;
            accent = agentColor;
            Root = new GameObject("Rig").transform;
            Root.SetParent(parent, false);
            pelvis = Bone("Pelvis", Root, new Vector3(0f, HipY, 0f));
            chest = Bone("Chest", pelvis, new Vector3(0f, 0.12f, 0f));
            head = Bone("Head", chest, new Vector3(0f, 0.5f, 0f));
            armL = Bone("UpperArm L", chest, new Vector3(-0.25f, 0.42f, 0f));
            foreL = Bone("Forearm L", armL, new Vector3(0f, -0.27f, 0f));
            armR = Bone("UpperArm R", chest, new Vector3(0.25f, 0.42f, 0f));
            foreR = Bone("Forearm R", armR, new Vector3(0f, -0.27f, 0f));
            thighL = Bone("Thigh L", pelvis, new Vector3(-0.1f, -0.03f, 0f));
            shinL = Bone("Shin L", thighL, new Vector3(0f, -0.44f, 0f));
            thighR = Bone("Thigh R", pelvis, new Vector3(0.1f, -0.03f, 0f));
            shinR = Bone("Shin R", thighR, new Vector3(0f, -0.44f, 0f));
            gunMount = Bone("Gun", chest, new Vector3(0.1f, 0.3f, 0.3f));
            BuildMeshes();
            if (flashMat == null) flashMat = Mats.Additive(Textures.FlashStar(), new Color(1f, 0.8f, 0.45f), 2f, 3200);
        }

        static Transform Bone(string name, Transform parent, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        void BuildMeshes()
        {
            foreach (Mesh m in meshes) if (m != null) Object.Destroy(m);
            meshes.Clear();
            foreach (MeshFilter f in filters) if (f != null) Object.Destroy(f.gameObject);
            filters.Clear();

            Color suit = new Color(0.15f, 0.16f, 0.19f, 0f);
            Color suitLight = new Color(0.24f, 0.25f, 0.29f, 0f);
            Color plate = new Color(team.r, team.g, team.b, 0.6f);
            Color plateDark = new Color(team.r * 0.6f, team.g * 0.6f, team.b * 0.6f, 0.6f);
            Color acc = new Color(accent.r, accent.g, accent.b, 0f);
            Color metal = new Color(0.32f, 0.33f, 0.36f, 1f);

            MeshKit k;
            // Pelvis: belt, hips, a pouch.
            k = Kit();
            k.Box(0, MeshKit.At(new Vector3(0f, 0f, 0f), Vector3.zero), new Vector3(0.34f, 0.17f, 0.22f), 0.04f, suit);
            k.Box(0, MeshKit.At(new Vector3(0f, 0.07f, 0f), Vector3.zero), new Vector3(0.36f, 0.05f, 0.24f), 0.015f, metal);
            k.Box(0, MeshKit.At(new Vector3(0.14f, -0.02f, 0.09f), Vector3.zero), new Vector3(0.08f, 0.1f, 0.06f), 0.015f, suitLight);
            k.Box(0, MeshKit.At(new Vector3(0f, -0.02f, 0.115f), Vector3.zero), new Vector3(0.16f, 0.12f, 0.02f), 0.01f, plateDark);
            Attach(pelvis, k, "pelvis");

            // Chest: abdomen, chest plate, shoulder pads with agent stripes, pack, collar.
            k = Kit();
            k.Box(0, MeshKit.At(new Vector3(0f, 0.12f, 0f), Vector3.zero), new Vector3(0.3f, 0.24f, 0.19f), 0.04f, suit);
            k.Box(0, MeshKit.At(new Vector3(0f, 0.36f, 0.01f), Vector3.zero), new Vector3(0.44f, 0.3f, 0.27f), 0.05f, plate);
            k.Box(0, MeshKit.At(new Vector3(0f, 0.33f, 0.14f), new Vector3(-6f, 0f, 0f)), new Vector3(0.3f, 0.18f, 0.03f), 0.012f, plateDark);
            k.Box(0, MeshKit.At(new Vector3(0f, 0.22f, 0.12f), Vector3.zero), new Vector3(0.24f, 0.05f, 0.03f), 0.01f, acc);
            k.Box(0, MeshKit.At(new Vector3(-0.27f, 0.47f, 0f), new Vector3(0f, 0f, 14f)), new Vector3(0.17f, 0.11f, 0.2f), 0.035f, plate);
            k.Box(0, MeshKit.At(new Vector3(0.27f, 0.47f, 0f), new Vector3(0f, 0f, -14f)), new Vector3(0.17f, 0.11f, 0.2f), 0.035f, plate);
            k.Box(0, MeshKit.At(new Vector3(-0.29f, 0.45f, 0f), new Vector3(0f, 0f, 14f)), new Vector3(0.16f, 0.025f, 0.205f), 0.006f, acc);
            k.Box(0, MeshKit.At(new Vector3(0.29f, 0.45f, 0f), new Vector3(0f, 0f, -14f)), new Vector3(0.16f, 0.025f, 0.205f), 0.006f, acc);
            k.Box(0, MeshKit.At(new Vector3(0f, 0.36f, -0.2f), Vector3.zero), new Vector3(0.32f, 0.36f, 0.13f), 0.04f, suitLight);
            k.Box(0, MeshKit.At(new Vector3(0f, 0.42f, -0.27f), Vector3.zero), new Vector3(0.26f, 0.1f, 0.02f), 0.008f, acc);
            k.Box(0, MeshKit.At(new Vector3(0f, 0.53f, 0f), Vector3.zero), new Vector3(0.14f, 0.07f, 0.14f), 0.02f, suit);
            Attach(chest, k, "chest");

            // Head: helmet shell, a jaw guard, the agent crest and the visor (glowing, its own material).
            k = Kit();
            k.Box(0, MeshKit.At(new Vector3(0f, 0.03f, -0.005f), Vector3.zero), new Vector3(0.26f, 0.27f, 0.29f), 0.07f, plateDark);
            k.Box(0, MeshKit.At(new Vector3(0f, -0.07f, 0.06f), Vector3.zero), new Vector3(0.2f, 0.07f, 0.17f), 0.025f, suit);
            k.Box(0, MeshKit.At(new Vector3(0f, 0.17f, -0.02f), Vector3.zero), new Vector3(0.04f, 0.03f, 0.22f), 0.01f, acc);
            k.Box(0, MeshKit.At(new Vector3(0.14f, 0.02f, -0.02f), Vector3.zero), new Vector3(0.03f, 0.08f, 0.08f), 0.01f, metal);
            Attach(head, k, "head");
            var vk = Kit();
            vk.Box(0, MeshKit.At(new Vector3(0f, 0.03f, 0.135f), Vector3.zero), new Vector3(0.21f, 0.065f, 0.035f), 0.015f, Color.white);
            Attach(head, vk, "visor", VisorMat(Color.Lerp(accent, Color.white, 0.35f)), false);

            // Arms.
            foreach (Transform arm in new[] { armL, armR })
            {
                k = Kit();
                k.Box(0, MeshKit.At(new Vector3(0f, -0.13f, 0f), Vector3.zero), new Vector3(0.1f, 0.28f, 0.1f), 0.03f, suit);
                k.Box(0, MeshKit.At(new Vector3(0f, -0.08f, 0f), Vector3.zero), new Vector3(0.115f, 0.1f, 0.115f), 0.025f, plateDark);
                Attach(arm, k, "upper arm");
            }
            foreach (Transform fore in new[] { foreL, foreR })
            {
                k = Kit();
                k.Box(0, MeshKit.At(new Vector3(0f, -0.12f, 0f), Vector3.zero), new Vector3(0.09f, 0.25f, 0.09f), 0.025f, suitLight);
                k.Box(0, MeshKit.At(new Vector3(0f, -0.07f, 0f), Vector3.zero), new Vector3(0.1f, 0.1f, 0.1f), 0.02f, plate);
                k.Box(0, MeshKit.At(new Vector3(0f, -0.27f, 0.01f), Vector3.zero), new Vector3(0.085f, 0.08f, 0.1f), 0.025f, suit);
                Attach(fore, k, "forearm");
            }
            // Legs.
            foreach (Transform thigh in new[] { thighL, thighR })
            {
                k = Kit();
                k.Box(0, MeshKit.At(new Vector3(0f, -0.21f, 0f), Vector3.zero), new Vector3(0.14f, 0.44f, 0.15f), 0.04f, suit);
                k.Box(0, MeshKit.At(new Vector3(0f, -0.18f, 0.06f), Vector3.zero), new Vector3(0.12f, 0.2f, 0.05f), 0.015f, plateDark);
                Attach(thigh, k, "thigh");
            }
            foreach (Transform shin in new[] { shinL, shinR })
            {
                k = Kit();
                k.Box(0, MeshKit.At(new Vector3(0f, -0.2f, 0f), Vector3.zero), new Vector3(0.12f, 0.42f, 0.13f), 0.035f, suit);
                k.Box(0, MeshKit.At(new Vector3(0f, -0.02f, 0.06f), Vector3.zero), new Vector3(0.12f, 0.1f, 0.05f), 0.02f, plate);
                k.Box(0, MeshKit.At(new Vector3(0f, -0.2f, 0.06f), Vector3.zero), new Vector3(0.1f, 0.24f, 0.03f), 0.01f, plateDark);
                k.Box(0, MeshKit.At(new Vector3(0f, -0.45f, 0.045f), Vector3.zero), new Vector3(0.12f, 0.08f, 0.26f), 0.025f, metal);
                Attach(shin, k, "shin");
            }
        }

        static MeshKit Kit()
        {
            return new MeshKit(1) { UvScale = 4f };
        }

        void Attach(Transform bone, MeshKit k, string name, Material mat = null, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(bone, false);
            Mesh m = k.ToMesh("bot " + name);
            meshes.Add(m);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = m;
            filters.Add(mf);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat ?? BodyMaterial;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.lightProbeUsage = LightProbeUsage.Off;
        }

        /// <summary>New team colour (settings changed): rebuilds the vertex colours.</summary>
        public void Recolor(Color teamColor)
        {
            if (teamColor == team) return;
            team = teamColor;
            BuildMeshes();
        }

        /// <summary>The weapon in the bot's hands (null keeps the current one; the Lancer when none was ever known).</summary>
        public void SetWeapon(string id)
        {
            if (string.IsNullOrEmpty(id)) id = gunId ?? Weapons.Default;
            if (id == gunId && gun != null) return;
            if (flash != null) flash.SetParent(Root, false);
            if (gun != null) Object.Destroy(gun.Root);
            gunId = id;
            gun = GunModels.Build(id, gunMount, 0, false, true);
            GunDef d = gun.Def;
            gun.Root.transform.localPosition = -d.Grip;
            gun.Root.transform.localRotation = Quaternion.identity;
            if (flash == null)
            {
                var k = new MeshKit(1);
                k.QuadUv(0, new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), Vector3.back, Color.white,
                         new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
                k.QuadUv(0, new Vector3(0f, -0.5f, -0.2f), new Vector3(0f, -0.5f, 1.2f), new Vector3(0f, 0.5f, 1.2f), new Vector3(0f, 0.5f, -0.2f), Vector3.right, Color.white,
                         new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
                var go = new GameObject("Flash");
                go.AddComponent<MeshFilter>().sharedMesh = k.ToMesh("bot flash");
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = flashMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                flash = go.transform;
                flash.gameObject.SetActive(false);
            }
            flash.SetParent(gun.MuzzlePoint, false);
            flash.localPosition = Vector3.zero;
            flash.localScale = Vector3.one * d.FlashSize * 1.6f;
        }

        public void Shot()
        {
            shotAt = Time.time;
            if (flash != null)
            {
                flash.gameObject.SetActive(true);
                flash.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                flashUntil = Time.time + 0.05f;
            }
        }

        public Vector3 MuzzlePos => gun != null && gun.MuzzlePoint != null ? gun.MuzzlePoint.position : Root.position + Vector3.up * 1.4f;

        /// <summary>
        /// Per frame. `vel` is the bot's ground velocity (world, m/s), `heading` its facing in degrees, `crouch` the
        /// sim's crouch flag when known, `alive` false while the death fall plays (the root tilt is WorldView's).
        /// </summary>
        public void Animate(float dt, Vector3 vel, float heading, bool crouch, bool alive)
        {
            float t = Time.time;
            if (flash != null && flash.gameObject.activeSelf && t >= flashUntil) flash.gameObject.SetActive(false);
            var fwd = new Vector3(Mathf.Sin(heading * Mathf.Deg2Rad), 0f, Mathf.Cos(heading * Mathf.Deg2Rad));
            var right = new Vector3(fwd.z, 0f, -fwd.x);
            float speed = alive ? new Vector2(vel.x, vel.z).magnitude : 0f;
            float f = speed > 0.05f ? Vector3.Dot(vel, fwd) / speed : 0f;
            float s = speed > 0.05f ? Vector3.Dot(vel, right) / speed : 0f;
            float k = 1f - Mathf.Exp(-dt * 10f);
            moveBlend = Mathf.Lerp(moveBlend, Mathf.Clamp01(speed / 3.5f), k);
            fwdBlend = Mathf.Lerp(fwdBlend, f, k);
            sideBlend = Mathf.Lerp(sideBlend, s, k);
            crouchBlend = Mathf.MoveTowards(crouchBlend, crouch && alive ? 1f : 0f, dt * 5f);
            phase += dt * Mathf.Lerp(2f, 9.5f, Mathf.Clamp01(speed / 6f)) * (speed > 0.15f ? 1f : 0f);
            lean = Mathf.Lerp(lean, alive ? sideBlend * moveBlend * -6f : 0f, k);

            float m = moveBlend;
            float swing = Mathf.Sin(phase);
            float swing2 = Mathf.Sin(phase + Mathf.PI);
            float bob = Mathf.Abs(Mathf.Cos(phase)) * 0.05f * m;
            float breathe = Mathf.Sin(t * 1.8f) * 0.008f * (1f - m);
            float crouchDrop = 0.36f * crouchBlend;
            pelvis.localPosition = new Vector3(0f, HipY - crouchDrop + bob - 0.03f * m + breathe, 0f);
            pelvis.localRotation = Quaternion.Euler(0f, sideBlend * m * 25f, 0f);

            // Legs follow the direction of travel: pitch for forward/back, roll for strafing.
            float pitchAmp = 34f * m * Mathf.Clamp(Mathf.Abs(fwdBlend) + 0.3f, 0f, 1f) * (fwdBlend < -0.2f ? -1f : 1f);
            float rollAmp = 16f * m * Mathf.Abs(sideBlend);
            float crouchThigh = -62f * crouchBlend, crouchShin = 105f * crouchBlend;
            thighL.localRotation = Quaternion.Euler(-swing * pitchAmp + crouchThigh, 0f, swing * rollAmp * Mathf.Sign(sideBlend + 1e-4f) - 3f);
            thighR.localRotation = Quaternion.Euler(-swing2 * pitchAmp + crouchThigh, 0f, swing2 * rollAmp * Mathf.Sign(sideBlend + 1e-4f) + 3f);
            shinL.localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Sin(phase + 0.9f)) * 50f * m + crouchShin, 0f, 0f);
            shinR.localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Sin(phase + 0.9f + Mathf.PI)) * 50f * m + crouchShin, 0f, 0f);

            // Torso: a forward lean into a run, breathing, a back-kick when firing; counter-rotate the hips' twist.
            float kick = Mathf.Clamp01(1f - (t - shotAt) / 0.12f);
            chest.localRotation = Quaternion.Euler(8f * m * Mathf.Max(0f, fwdBlend) + 12f * crouchBlend - kick * 5f + Mathf.Sin(t * 1.8f) * 1.2f * (1f - m), -sideBlend * m * 25f, lean);
            head.localRotation = Quaternion.Euler(-8f * m * Mathf.Max(0f, fwdBlend) - 12f * crouchBlend, 0f, -lean);

            // Arms hold the weapon forward; the gun bobs a little with the stride and kicks when firing.
            float aimSway = Mathf.Sin(phase * 0.5f) * 3f * m;
            if (alive)
            {
                armR.localRotation = Quaternion.Euler(-62f + aimSway + kick * 6f, 0f, -12f);
                foreR.localRotation = Quaternion.Euler(-28f, 0f, 0f);
                armL.localRotation = Quaternion.Euler(-74f + aimSway + kick * 6f, 30f, 18f);
                foreL.localRotation = Quaternion.Euler(-34f, 0f, 0f);
                gunMount.localPosition = new Vector3(0.1f, 0.3f + Mathf.Sin(phase) * 0.008f * m, 0.3f - kick * 0.05f);
                gunMount.localRotation = Quaternion.Euler(-kick * 8f, 0f, 0f);
            }
            else
            {
                // Limp: arms fall, the gun drops away.
                armR.localRotation = Quaternion.Slerp(armR.localRotation, Quaternion.Euler(-10f, 0f, -20f), k);
                armL.localRotation = Quaternion.Slerp(armL.localRotation, Quaternion.Euler(-10f, 0f, 20f), k);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.identity, k);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.identity, k);
                gunMount.localPosition = Vector3.Lerp(gunMount.localPosition, new Vector3(0.25f, -0.3f, 0.35f), k);
                gunMount.localRotation = Quaternion.Slerp(gunMount.localRotation, Quaternion.Euler(60f, 20f, 70f), k);
            }
        }

        /// <summary>The head's height above the floor (moves down with the crouch).</summary>
        public float HeadHeight => 1.65f - 0.4f * crouchBlend;

        public void Destroy()
        {
            foreach (Mesh m in meshes) if (m != null) Object.Destroy(m);
            meshes.Clear();
        }
    }
}

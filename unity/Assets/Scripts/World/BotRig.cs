// A stylised armoured humanoid built from code: a segmented body (pelvis,
// abdomen and chest, neck, head, upper and lower arms with gloves, thighs,
// shins and boots, tapered so it reads as a figure, not boxes), armour plates
// and pauldrons in the team colour, trim in the agent colour, a helmet or hood
// per silhouette, a visor glowing in the agent colour and a pack per kit. The
// four agents and the five personas each have their own silhouette. Each bone
// is one merged vertex-coloured mesh on one shared material (about fourteen
// draw calls a figure).
//
// Procedural animation: breathing idle, a run cycle with a free arm swing and
// the rifle at low ready, strafe lean, crouch walk, the upper body leading
// turns while the legs catch up (aim tracks yaw), a recoil kick, a hit flinch
// away from the shot, and a ragdoll-like collapse on death (knees buckle,
// arms fling, head lolls; BotView tips the body over in the hit direction).
// Standing height 1.8 m, head at 1.65 m, matching BotView's hit colliders.
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
        readonly Color accent;
        readonly Color accent2;
        /// <summary>Silhouette id: an agent ("rook", "haze", "wren", "pike") or a persona ("zephyr", "atlas", "ember", "wisp", "nova").</summary>
        readonly string look;
        /// <summary>The agent whose kit sets the pack.</summary>
        readonly string kit;
        GunModel gun;
        string gunId;
        Transform flash;
        Mesh flashMesh;
        float flashUntil;

        float phase;
        float moveBlend;
        float fwdBlend, sideBlend;
        float crouchBlend;
        float aimBlend = 1f;
        float shotAt = -10f;
        float lean;
        float legYaw;
        bool yawKnown;
        float hitAt = -10f;
        Vector3 hitLocal;
        bool wasAlive = true;
        float diedAt = -10f;

        /// <summary>The direction of the last hit (world, attacker to victim) and when it landed.</summary>
        public Vector3 LastHitDir { get; private set; }
        public float LastHitAt => hitAt;

        const float HipY = 0.95f;

        public static Material BodyMaterial
        {
            get
            {
                if (bodyMat == null) bodyMat = Mats.World(Color.white, Textures.Grain(), null, 0f, 0.3f, 0.6f, 0.62f, 1f);
                return bodyMat;
            }
        }

        static Material VisorMat(Color c)
        {
            if (visorMats.TryGetValue(c, out Material m) && m != null) return m;
            m = Mats.Glow(c, 2.4f);
            visorMats[c] = m;
            return m;
        }

        public BotRig(Transform parent, Color teamColor, Color agentColor, string lookId = null)
        {
            team = teamColor;
            accent = agentColor;
            Persona p = Personas.Get(lookId);
            look = lookId ?? "";
            kit = p != null ? (p.Agent ?? "") : look;
            accent2 = p != null ? p.Secondary : Color.Lerp(agentColor, Color.white, 0.45f);
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

        // ---- geometry helpers ------------------------------------------------------------------------
        static MeshKit Kit()
        {
            return new MeshKit(1) { UvScale = 4f };
        }

        static void Face(MeshKit k, Vector3 centre, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(c - a, d - a);
            if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize();
            if (Vector3.Dot(n, (a + b + c + d) * 0.25f - centre) < 0f) n = -n;
            k.Quad(0, a, b, c, d, n, col);
        }

        /// <summary>A tapered box from y0 (size `bottom`: x width, y depth) to y1 (size `top`), the top shifted by `shift` (x, z).</summary>
        static void Taper(MeshKit k, Matrix4x4 m, float y0, float y1, Vector2 bottom, Vector2 top, Color col, Vector2 shift = default)
        {
            float bx = bottom.x / 2f, bz = bottom.y / 2f, tx = top.x / 2f, tz = top.y / 2f;
            Vector3 p0 = m.MultiplyPoint3x4(new Vector3(-bx, y0, -bz)), p1 = m.MultiplyPoint3x4(new Vector3(bx, y0, -bz));
            Vector3 p2 = m.MultiplyPoint3x4(new Vector3(bx, y0, bz)), p3 = m.MultiplyPoint3x4(new Vector3(-bx, y0, bz));
            Vector3 p4 = m.MultiplyPoint3x4(new Vector3(-tx + shift.x, y1, -tz + shift.y)), p5 = m.MultiplyPoint3x4(new Vector3(tx + shift.x, y1, -tz + shift.y));
            Vector3 p6 = m.MultiplyPoint3x4(new Vector3(tx + shift.x, y1, tz + shift.y)), p7 = m.MultiplyPoint3x4(new Vector3(-tx + shift.x, y1, tz + shift.y));
            Vector3 c = (p0 + p1 + p2 + p3 + p4 + p5 + p6 + p7) / 8f;
            Face(k, c, p0, p1, p2, p3, col);
            Face(k, c, p4, p5, p6, p7, col);
            Face(k, c, p0, p1, p5, p4, col);
            Face(k, c, p1, p2, p6, p5, col);
            Face(k, c, p2, p3, p7, p6, col);
            Face(k, c, p3, p0, p4, p7, col);
        }

        static Matrix4x4 At(float x, float y, float z, float rx = 0f, float ry = 0f, float rz = 0f)
        {
            return MeshKit.At(new Vector3(x, y, z), new Vector3(rx, ry, rz));
        }

        static void Box(MeshKit k, float x, float y, float z, float sx, float sy, float sz, float bevel, Color c, float rx = 0f, float ry = 0f, float rz = 0f)
        {
            k.Box(0, At(x, y, z, rx, ry, rz), new Vector3(sx, sy, sz), bevel, c);
        }

        /// <summary>A cylinder standing along local y.</summary>
        static void Can(MeshKit k, float x, float y, float z, float radius, float length, Color c, float rx = 0f, float rz = 0f)
        {
            k.Cylinder(0, At(x, y, z, 90f + rx, 0f, rz), radius, length, 10, c, false, true);
        }

        // ---- the figure ---------------------------------------------------------------------------------
        void BuildMeshes()
        {
            foreach (Mesh m in meshes) if (m != null) Object.Destroy(m);
            meshes.Clear();
            foreach (MeshFilter f in filters) if (f != null) Object.Destroy(f.gameObject);
            filters.Clear();

            // Vertex colour RGB is the albedo, A the metal mask (1 metal, 0 paint / polymer).
            Color suit = new Color(0.1f, 0.11f, 0.14f, 0f);
            Color suitLight = new Color(0.19f, 0.2f, 0.24f, 0f);
            Color rubber = new Color(0.06f, 0.06f, 0.07f, 0f);
            Color plate = new Color(team.r, team.g, team.b, 0.55f);
            Color armour = new Color(Mathf.Lerp(0.14f, accent.r, 0.35f), Mathf.Lerp(0.15f, accent.g, 0.35f), Mathf.Lerp(0.18f, accent.b, 0.35f), 0.4f);
            Color armourDark = new Color(armour.r * 0.6f, armour.g * 0.6f, armour.b * 0.6f, 0.4f);
            Color trim = new Color(Mathf.Min(1f, team.r * 1.15f), Mathf.Min(1f, team.g * 1.15f), Mathf.Min(1f, team.b * 1.15f), 0f);
            Color acc = new Color(accent.r, accent.g, accent.b, 0f);
            Color acc2 = new Color(accent2.r, accent2.g, accent2.b, 0f);
            Color metal = new Color(0.32f, 0.33f, 0.36f, 1f);
            bool hooded = look == "haze" || look == "wisp";
            bool heavy = look == "pike" || look == "atlas";
            bool slim = look == "wisp" || look == "zephyr" || look == "rook";

            // Pelvis: hips, belt with a team-colour buckle, tassets, pouches, a holster.
            MeshKit k = Kit();
            Taper(k, Matrix4x4.identity, -0.1f, 0.08f, new Vector2(0.3f, 0.19f), new Vector2(0.32f, 0.2f), suit);
            Box(k, 0f, 0.075f, 0f, 0.35f, 0.045f, 0.23f, 0.012f, metal);
            Box(k, 0f, 0.075f, 0.118f, 0.08f, 0.04f, 0.012f, 0.004f, trim);
            Box(k, -0.11f, -0.07f, 0.1f, 0.12f, 0.14f, 0.025f, 0.008f, armourDark, 8f, 0f, 4f);
            Box(k, 0.11f, -0.07f, 0.1f, 0.12f, 0.14f, 0.025f, 0.008f, armourDark, 8f, 0f, -4f);
            Box(k, 0.17f, 0f, 0.03f, 0.06f, 0.1f, 0.09f, 0.012f, suitLight);
            Box(k, -0.17f, 0f, -0.04f, 0.06f, 0.09f, 0.08f, 0.012f, suitLight);
            if (hooded)
            {
                // Coat tails down the back of the thighs.
                Box(k, -0.09f, -0.26f, -0.11f, 0.15f, 0.42f, 0.02f, 0.006f, look == "wisp" ? rubber : suitLight, -8f, 0f, 3f);
                Box(k, 0.09f, -0.26f, -0.11f, 0.15f, 0.42f, 0.02f, 0.006f, look == "wisp" ? rubber : suitLight, -8f, 0f, -3f);
            }
            Attach(pelvis, k, "pelvis");

            // Chest: abdomen, a V-tapered ribcage, the team-colour chest plate with agent trim, collar, neck,
            // pauldrons, and the pack.
            k = Kit();
            Taper(k, Matrix4x4.identity, 0f, 0.24f, new Vector2(0.27f, 0.18f), new Vector2(slim ? 0.3f : 0.32f, 0.2f), suit);
            Taper(k, Matrix4x4.identity, 0.22f, 0.5f, new Vector2(0.34f, 0.21f), new Vector2(heavy ? 0.46f : slim ? 0.4f : 0.43f, 0.24f), suitLight);
            Box(k, 0f, 0.12f, 0.095f, 0.2f, 0.07f, 0.03f, 0.008f, armourDark);
            Box(k, 0f, 0.05f, 0.09f, 0.18f, 0.05f, 0.03f, 0.008f, armourDark);
            Box(k, 0f, 0.37f, 0.11f, heavy ? 0.38f : 0.33f, 0.25f, 0.05f, 0.02f, plate, -6f);
            Box(k, 0f, 0.245f, 0.125f, 0.3f, 0.018f, 0.03f, 0.004f, acc);
            Box(k, -0.1f, 0.44f, 0.135f, 0.08f, 0.015f, 0.02f, 0.003f, acc, -6f);
            Box(k, 0.1f, 0.44f, 0.135f, 0.08f, 0.015f, 0.02f, 0.003f, acc, -6f);
            Box(k, 0f, 0.52f, -0.01f, 0.2f, 0.06f, 0.18f, 0.02f, suit);
            Box(k, 0f, 0.52f, 0.085f, 0.18f, 0.025f, 0.02f, 0.004f, trim);
            Can(k, 0f, 0.56f, 0.005f, 0.055f, 0.08f, suit);
            if (look != "wisp")
            {
                float pw = heavy ? 0.21f : 0.17f, ph = heavy ? 0.13f : 0.1f;
                Box(k, -0.27f, 0.47f, 0f, pw, ph, 0.21f, 0.035f, plate, 0f, 0f, 16f);
                Box(k, 0.27f, 0.47f, 0f, pw, ph, 0.21f, 0.035f, plate, 0f, 0f, -16f);
                Box(k, -0.29f, 0.445f, 0f, pw - 0.01f, 0.022f, 0.215f, 0.005f, acc, 0f, 0f, 16f);
                Box(k, 0.29f, 0.445f, 0f, pw - 0.01f, 0.022f, 0.215f, 0.005f, acc, 0f, 0f, -16f);
                if (heavy)
                {
                    Box(k, -0.31f, 0.4f, 0f, pw - 0.03f, 0.08f, 0.2f, 0.02f, armourDark, 0f, 0f, 22f);
                    Box(k, 0.31f, 0.4f, 0f, pw - 0.03f, 0.08f, 0.2f, 0.02f, armourDark, 0f, 0f, -22f);
                }
            }
            Pack(k, suitLight, metal, acc, rubber);
            ChestExtras(k, suitLight, metal, acc, acc2, rubber, armourDark);
            Attach(chest, k, "chest");

            // Head: a balaclava'd head, then the helmet or hood; the visor (and a halo) glow on their own material.
            k = Kit();
            Box(k, 0f, 0.02f, 0.01f, 0.19f, 0.23f, 0.21f, 0.06f, suit);
            var vk = Kit();
            Helmet(k, vk, armour, armourDark, suitLight, metal, acc, acc2, rubber);
            Attach(head, k, "head");
            Attach(head, vk, "visor", VisorMat(Color.Lerp(accent, Color.white, 0.3f)), false);

            // Arms: tapered sleeves, a bicep plate, elbow pad, vambrace in the team colour, a wrist band, gloves.
            foreach (Transform arm in new[] { armL, armR })
            {
                k = Kit();
                Taper(k, Matrix4x4.identity, -0.27f, 0.02f, new Vector2(0.085f, 0.09f), new Vector2(0.11f, 0.11f), suit);
                Box(k, 0f, -0.09f, 0f, 0.12f, 0.1f, 0.12f, 0.025f, armourDark);
                Box(k, 0f, -0.26f, -0.04f, 0.08f, 0.06f, 0.05f, 0.015f, rubber);
                Attach(arm, k, "upper arm");
            }
            foreach (Transform fore in new[] { foreL, foreR })
            {
                k = Kit();
                Taper(k, Matrix4x4.identity, -0.23f, 0f, new Vector2(0.07f, 0.075f), new Vector2(0.088f, 0.09f), suitLight);
                Box(k, 0f, -0.1f, 0.005f, 0.1f, 0.12f, 0.1f, 0.02f, plate);
                Box(k, 0f, -0.18f, 0f, 0.088f, 0.02f, 0.088f, 0.004f, acc);
                Box(k, 0f, -0.27f, 0.012f, 0.078f, 0.085f, 0.1f, 0.025f, rubber);
                Box(k, 0f, -0.315f, 0.035f, 0.07f, 0.035f, 0.06f, 0.012f, rubber);
                Attach(fore, k, "forearm");
            }
            // Legs: tapered thighs with a plate and knee pad, shins with a guard, boots with a team band and sole.
            foreach (Transform thigh in new[] { thighL, thighR })
            {
                k = Kit();
                Taper(k, Matrix4x4.identity, -0.44f, 0.02f, new Vector2(0.12f, 0.13f), new Vector2(0.16f, 0.17f), suit);
                Box(k, 0f, -0.17f, 0.07f, 0.12f, 0.2f, 0.04f, 0.012f, armourDark);
                Box(k, 0f, -0.41f, 0.065f, 0.12f, 0.1f, 0.05f, 0.018f, plate);
                if (thigh == thighR) Box(k, 0.09f, -0.16f, 0f, 0.04f, 0.15f, 0.08f, 0.01f, rubber);
                Attach(thigh, k, "thigh");
            }
            foreach (Transform shin in new[] { shinL, shinR })
            {
                k = Kit();
                Taper(k, Matrix4x4.identity, -0.37f, 0f, new Vector2(0.1f, 0.11f), new Vector2(0.12f, 0.13f), suit);
                Box(k, 0f, -0.17f, 0.055f, 0.1f, 0.22f, 0.035f, 0.01f, armourDark);
                Box(k, 0f, -0.415f, 0.04f, 0.125f, 0.11f, 0.27f, 0.03f, rubber);
                Box(k, 0f, -0.375f, 0.04f, 0.13f, 0.022f, 0.2f, 0.004f, trim);
                Box(k, 0f, -0.468f, 0.04f, 0.13f, 0.02f, 0.28f, 0.006f, metal);
                Attach(shin, k, "shin");
            }
        }

        /// <summary>The pack on the back, by kit: dash thrusters, smoke canisters, a drone cradle or a dart quiver.</summary>
        void Pack(MeshKit k, Color body, Color metal, Color acc, Color rubber)
        {
            switch (kit)
            {
                case "rook":
                    Box(k, 0f, 0.36f, -0.17f, 0.26f, 0.3f, 0.08f, 0.02f, body);
                    Can(k, -0.08f, 0.3f, -0.23f, 0.045f, 0.3f, metal);
                    Can(k, 0.08f, 0.3f, -0.23f, 0.045f, 0.3f, metal);
                    Box(k, -0.08f, 0.13f, -0.23f, 0.06f, 0.03f, 0.06f, 0.01f, acc);
                    Box(k, 0.08f, 0.13f, -0.23f, 0.06f, 0.03f, 0.06f, 0.01f, acc);
                    break;
                case "haze":
                    Box(k, 0f, 0.36f, -0.18f, 0.3f, 0.34f, 0.1f, 0.025f, body);
                    for (int i = -1; i <= 1; i++)
                    {
                        Can(k, i * 0.09f, 0.34f, -0.26f, 0.04f, 0.26f, rubber);
                        Box(k, i * 0.09f, 0.49f, -0.26f, 0.05f, 0.03f, 0.05f, 0.01f, acc);
                    }
                    break;
                case "wren":
                    Box(k, 0f, 0.38f, -0.18f, 0.28f, 0.24f, 0.1f, 0.03f, body);
                    Box(k, 0f, 0.38f, -0.25f, 0.2f, 0.14f, 0.05f, 0.02f, metal);
                    Can(k, -0.13f, 0.52f, -0.2f, 0.07f, 0.015f, acc);
                    Can(k, 0.13f, 0.52f, -0.2f, 0.07f, 0.015f, acc);
                    Box(k, 0.1f, 0.6f, -0.2f, 0.012f, 0.16f, 0.012f, 0f, metal);
                    break;
                case "pike":
                    Box(k, 0f, 0.34f, -0.19f, 0.3f, 0.38f, 0.12f, 0.03f, body);
                    Can(k, 0.13f, 0.44f, -0.25f, 0.05f, 0.42f, rubber, -18f, -10f);
                    Box(k, 0.17f, 0.66f, -0.3f, 0.03f, 0.06f, 0.03f, 0.008f, acc, -18f, 0f, -10f);
                    Box(k, 0.11f, 0.65f, -0.31f, 0.03f, 0.06f, 0.03f, 0.008f, acc, -18f, 0f, -10f);
                    Box(k, -0.05f, 0.25f, -0.26f, 0.14f, 0.08f, 0.04f, 0.01f, metal);
                    break;
                default:
                    Box(k, 0f, 0.36f, -0.17f, 0.28f, 0.32f, 0.1f, 0.03f, body);
                    break;
            }
        }

        /// <summary>Persona extras on the torso: Zephyr's scarf, Atlas's cape and radio, Ember's shoulder cloak, Wisp's blades, Nova's side pods.</summary>
        void ChestExtras(MeshKit k, Color body, Color metal, Color acc, Color acc2, Color rubber, Color dark)
        {
            switch (look)
            {
                case "zephyr":
                    Box(k, 0.03f, 0.52f, -0.06f, 0.22f, 0.07f, 0.2f, 0.02f, acc);
                    Box(k, 0.08f, 0.33f, -0.2f, 0.09f, 0.34f, 0.015f, 0.004f, acc, -14f, 0f, 9f);
                    Box(k, 0.12f, 0.12f, -0.25f, 0.08f, 0.14f, 0.012f, 0.003f, acc2, -24f, 0f, 14f);
                    break;
                case "atlas":
                    Box(k, 0f, 0.1f, -0.23f, 0.46f, 0.8f, 0.02f, 0.006f, new Color(acc.r * 0.5f, acc.g * 0.5f, acc.b * 0.55f, 0f), -5f);
                    Box(k, 0f, 0.49f, -0.23f, 0.46f, 0.03f, 0.03f, 0.005f, acc2);
                    Box(k, -0.12f, 0.72f, -0.22f, 0.012f, 0.32f, 0.012f, 0f, metal);
                    Box(k, 0f, 0.36f, 0.14f, 0.07f, 0.07f, 0.015f, 0.004f, acc2, -6f, 0f, 45f);
                    break;
                case "ember":
                    for (int i = 0; i < 6; i++)
                    {
                        float side = i < 3 ? -1f : 1f;
                        int j = i % 3;
                        Box(k, side * (0.2f + j * 0.04f), 0.5f - j * 0.07f, -0.02f - j * 0.02f, 0.14f, 0.1f, 0.24f, 0.01f, j == 0 ? acc : body, 6f, 0f, side * (22f + j * 8f));
                    }
                    break;
                case "wisp":
                    Box(k, -0.08f, 0.3f, -0.23f, 0.03f, 0.42f, 0.02f, 0.004f, metal, 0f, 0f, 20f);
                    Box(k, 0.08f, 0.3f, -0.23f, 0.03f, 0.42f, 0.02f, 0.004f, metal, 0f, 0f, -20f);
                    Box(k, 0f, 0.47f, -0.05f, 0.42f, 0.09f, 0.24f, 0.02f, rubber, 0f, 0f, 0f);
                    break;
                case "nova":
                    Box(k, -0.2f, 0.2f, -0.1f, 0.06f, 0.14f, 0.12f, 0.02f, dark);
                    Box(k, 0.2f, 0.2f, -0.1f, 0.06f, 0.14f, 0.12f, 0.02f, dark);
                    Box(k, -0.235f, 0.2f, -0.1f, 0.01f, 0.1f, 0.08f, 0.002f, acc);
                    Box(k, 0.235f, 0.2f, -0.1f, 0.01f, 0.1f, 0.08f, 0.002f, acc);
                    break;
            }
        }

        /// <summary>Helmet or hood into `k`, the visor (and Nova's halo) into `v`, per silhouette.</summary>
        void Helmet(MeshKit k, MeshKit v, Color armour, Color dark, Color cloth, Color metal, Color acc, Color acc2, Color rubber)
        {
            switch (look)
            {
                case "rook":
                    Box(k, 0f, 0.07f, -0.01f, 0.235f, 0.19f, 0.27f, 0.07f, armour);
                    Box(k, 0f, 0.185f, -0.03f, 0.03f, 0.07f, 0.24f, 0.01f, acc);
                    Box(k, -0.122f, 0.01f, 0f, 0.03f, 0.1f, 0.1f, 0.01f, metal);
                    Box(k, 0.122f, 0.01f, 0f, 0.03f, 0.1f, 0.1f, 0.01f, metal);
                    Box(v, 0f, 0.035f, 0.125f, 0.21f, 0.055f, 0.04f, 0.015f, Color.white);
                    break;
                case "haze":
                    Taper(k, Matrix4x4.identity, -0.11f, 0.19f, new Vector2(0.3f, 0.32f), new Vector2(0.24f, 0.27f), cloth, new Vector2(0f, -0.02f));
                    Box(k, 0f, 0.17f, 0.06f, 0.22f, 0.04f, 0.14f, 0.015f, cloth, 22f);
                    Box(k, 0f, -0.15f, -0.12f, 0.26f, 0.12f, 0.08f, 0.02f, cloth, -10f);
                    Box(k, 0f, -0.06f, 0.11f, 0.15f, 0.08f, 0.05f, 0.015f, metal);
                    Box(v, 0f, 0.03f, 0.13f, 0.16f, 0.035f, 0.03f, 0.01f, Color.white);
                    break;
                case "wren":
                    Box(k, 0f, 0.06f, 0f, 0.25f, 0.21f, 0.26f, 0.1f, armour);
                    Box(k, 0.11f, 0.22f, -0.06f, 0.012f, 0.2f, 0.012f, 0f, metal);
                    Box(k, 0.11f, 0.33f, -0.06f, 0.025f, 0.025f, 0.025f, 0.008f, acc);
                    Box(v, 0f, 0.035f, 0.11f, 0.23f, 0.08f, 0.06f, 0.03f, Color.white);
                    break;
                case "pike":
                    Box(k, 0f, 0.06f, -0.01f, 0.27f, 0.23f, 0.29f, 0.05f, armour);
                    Box(k, 0f, -0.03f, 0.13f, 0.2f, 0.12f, 0.04f, 0.012f, metal);
                    Box(k, 0f, 0.19f, -0.02f, 0.2f, 0.03f, 0.2f, 0.01f, dark);
                    Box(v, 0f, 0.05f, 0.152f, 0.18f, 0.035f, 0.02f, 0.006f, Color.white);
                    Box(v, 0f, 0f, 0.152f, 0.035f, 0.09f, 0.02f, 0.006f, Color.white);
                    break;
                case "zephyr":
                    Box(k, 0f, 0.06f, -0.01f, 0.225f, 0.19f, 0.26f, 0.07f, armour);
                    Box(k, -0.07f, 0.15f, -0.1f, 0.02f, 0.05f, 0.24f, 0.006f, acc, -32f);
                    Box(k, 0.07f, 0.15f, -0.1f, 0.02f, 0.05f, 0.24f, 0.006f, acc, -32f);
                    Box(k, 0f, -0.07f, 0.09f, 0.17f, 0.06f, 0.07f, 0.015f, metal);
                    Box(v, -0.05f, 0.04f, 0.125f, 0.11f, 0.04f, 0.03f, 0.008f, Color.white, 0f, 0f, -12f);
                    Box(v, 0.05f, 0.04f, 0.125f, 0.11f, 0.04f, 0.03f, 0.008f, Color.white, 0f, 0f, 12f);
                    break;
                case "atlas":
                    Box(k, 0f, 0.06f, 0f, 0.26f, 0.2f, 0.27f, 0.06f, armour);
                    Box(k, 0f, 0.175f, 0f, 0.05f, 0.05f, 0.28f, 0.012f, acc2);
                    Box(k, -0.13f, 0.02f, 0.02f, 0.03f, 0.08f, 0.08f, 0.01f, metal);
                    Box(k, -0.13f, -0.02f, 0.08f, 0.012f, 0.012f, 0.1f, 0f, metal);
                    Box(v, 0f, 0.035f, 0.127f, 0.2f, 0.06f, 0.035f, 0.012f, Color.white);
                    break;
                case "ember":
                    Box(k, 0f, 0.1f, 0f, 0.23f, 0.12f, 0.25f, 0.06f, cloth);
                    Box(k, 0f, 0.055f, 0.16f, 0.22f, 0.02f, 0.12f, 0.004f, cloth);
                    Box(k, 0f, -0.06f, 0.1f, 0.16f, 0.07f, 0.06f, 0.015f, rubber);
                    Box(v, -0.045f, 0.02f, 0.115f, 0.075f, 0.05f, 0.05f, 0.02f, Color.white);
                    Box(v, 0.045f, 0.02f, 0.115f, 0.075f, 0.05f, 0.05f, 0.02f, Color.white);
                    break;
                case "wisp":
                    Taper(k, Matrix4x4.identity, -0.12f, 0.2f, new Vector2(0.29f, 0.31f), new Vector2(0.2f, 0.24f), rubber, new Vector2(0f, -0.04f));
                    Box(k, 0f, 0.15f, 0.07f, 0.2f, 0.03f, 0.12f, 0.01f, rubber, 26f);
                    Box(k, 0f, -0.03f, 0.11f, 0.18f, 0.13f, 0.04f, 0.015f, metal);
                    Box(v, -0.045f, 0.03f, 0.135f, 0.06f, 0.018f, 0.02f, 0.004f, Color.white);
                    Box(v, 0.045f, 0.03f, 0.135f, 0.06f, 0.018f, 0.02f, 0.004f, Color.white);
                    break;
                case "nova":
                    Box(k, 0f, 0.06f, 0f, 0.24f, 0.2f, 0.26f, 0.09f, armour);
                    Box(k, -0.125f, 0.04f, 0f, 0.025f, 0.08f, 0.08f, 0.01f, acc2);
                    Box(k, 0.125f, 0.04f, 0f, 0.025f, 0.08f, 0.08f, 0.01f, acc2);
                    Box(v, 0f, 0.035f, 0.12f, 0.18f, 0.07f, 0.04f, 0.025f, Color.white);
                    // The halo: twelve short segments in a ring over the head.
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i * 30f;
                        float r = 0.17f;
                        Box(v, Mathf.Sin(a * Mathf.Deg2Rad) * r, 0.28f, Mathf.Cos(a * Mathf.Deg2Rad) * r - 0.03f, 0.08f, 0.015f, 0.015f, 0f, Color.white, 0f, a, 0f);
                    }
                    break;
                default:
                    Box(k, 0f, 0.06f, -0.005f, 0.25f, 0.21f, 0.28f, 0.07f, armour);
                    Box(k, 0f, 0.17f, -0.02f, 0.04f, 0.03f, 0.22f, 0.01f, acc);
                    Box(v, 0f, 0.035f, 0.13f, 0.21f, 0.065f, 0.035f, 0.015f, Color.white);
                    break;
            }
        }

        void Attach(Transform bone, MeshKit k, string name, Material mat = null, bool shadows = true)
        {
            if (k.Empty) return;
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
                Mesh fm = k.ToMesh("bot flash");
                flashMesh = fm;
                go.AddComponent<MeshFilter>().sharedMesh = fm;
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

        /// <summary>A hit: `dir` is the bullet's travel (world, attacker to victim). The torso flinches away from it.</summary>
        public void Hit(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = -Root.forward;
            dir.Normalize();
            LastHitDir = dir;
            hitAt = Time.time;
            hitLocal = Root.InverseTransformDirection(dir);
        }

        public Vector3 MuzzlePos => gun != null && gun.MuzzlePoint != null ? gun.MuzzlePoint.position : Root.position + Vector3.up * 1.4f;

        /// <summary>
        /// Per frame. `vel` is the bot's ground velocity (world, m/s), `heading` its facing in degrees, `crouch` the
        /// sim's crouch flag when known, `alive` false while the death fall plays (the body's tip-over is BotView's).
        /// </summary>
        public void Animate(float dt, Vector3 vel, float heading, bool crouch, bool alive)
        {
            float t = Time.time;
            if (flash != null && flash.gameObject.activeSelf && t >= flashUntil) flash.gameObject.SetActive(false);
            if (wasAlive && !alive) diedAt = t;
            wasAlive = alive;
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
            float rate = Mathf.Lerp(2f, 9.5f, Mathf.Clamp01(speed / 6f)) * Mathf.Lerp(1f, 0.8f, crouchBlend);
            if (speed > 0.15f) phase += dt * rate;
            lean = Mathf.Lerp(lean, alive ? -sideBlend * moveBlend * 9f : 0f, k);
            // Low ready while running and not shooting; the rifle comes up to aim when slow or just fired.
            bool aimNow = t - shotAt < 1.4f || speed < 2.4f || crouchBlend > 0.5f;
            aimBlend = Mathf.MoveTowards(aimBlend, aimNow ? 1f : 0f, dt * (aimNow ? 7f : 2.5f));
            // The legs lag behind a fast turn; the upper body stays on the heading.
            if (!yawKnown) { legYaw = heading; yawKnown = true; }
            legYaw = Mathf.LerpAngle(legYaw, heading, 1f - Mathf.Exp(-dt * (speed > 0.5f ? 14f : 5f)));
            float twist = Mathf.DeltaAngle(heading, legYaw);
            if (Mathf.Abs(twist) > 50f)
            {
                twist = Mathf.Sign(twist) * 50f;
                legYaw = heading + twist;
            }
            float fl = Mathf.Clamp01(1f - (t - hitAt) / 0.3f);
            fl *= fl;

            float m = moveBlend;
            float swing = Mathf.Sin(phase);
            float swing2 = Mathf.Sin(phase + Mathf.PI);
            float bob = Mathf.Abs(Mathf.Cos(phase)) * 0.05f * m * Mathf.Lerp(1f, 0.5f, crouchBlend);
            float breathe = Mathf.Sin(t * 1.8f) * 0.008f * (1f - m);
            float crouchDrop = 0.36f * crouchBlend;
            float pelvisYaw = twist + sideBlend * m * 22f;
            pelvis.localPosition = new Vector3(0f, HipY - crouchDrop + bob - 0.03f * m + breathe, 0f);
            pelvis.localRotation = Quaternion.Euler(0f, pelvisYaw, lean * 0.4f);

            float deathT = alive ? 0f : Mathf.Clamp01((t - diedAt) / 0.45f);
            float dE = deathT * deathT * (3f - 2f * deathT);

            // Legs: pitch for forward and back, roll for strafing; a shorter, lower stride crouched; knees buckle on death.
            float stride = Mathf.Lerp(34f, 20f, crouchBlend);
            float pitchAmp = stride * m * Mathf.Clamp(Mathf.Abs(fwdBlend) + 0.3f, 0f, 1f) * (fwdBlend < -0.2f ? -1f : 1f);
            float rollAmp = 16f * m * Mathf.Abs(sideBlend);
            float crouchThigh = -62f * crouchBlend, crouchShin = 105f * crouchBlend;
            float sgn = Mathf.Sign(sideBlend + 1e-4f);
            thighL.localRotation = Quaternion.Euler(-swing * pitchAmp + crouchThigh - 38f * dE, 0f, swing * rollAmp * sgn - 3f - 6f * dE);
            thighR.localRotation = Quaternion.Euler(-swing2 * pitchAmp + crouchThigh - 26f * dE, 0f, swing2 * rollAmp * sgn + 3f + 8f * dE);
            shinL.localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Sin(phase + 0.9f)) * 50f * m + crouchShin + 70f * dE, 0f, 0f);
            shinR.localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Sin(phase + 0.9f + Mathf.PI)) * 50f * m + crouchShin + 55f * dE, 0f, 0f);

            // Torso: leans into a run, breathes, kicks back when firing, flinches away from a hit, counter-turns the hips.
            float kick = Mathf.Clamp01(1f - (t - shotAt) / 0.12f);
            float pitch = 8f * m * Mathf.Max(0f, fwdBlend) + 12f * crouchBlend - kick * 5f + Mathf.Sin(t * 1.8f) * 1.2f * (1f - m);
            pitch += hitLocal.z * 16f * fl;
            float roll = lean - hitLocal.x * 12f * fl;
            chest.localRotation = Quaternion.Euler(pitch - 14f * dE, -pelvisYaw, roll);
            head.localRotation = Quaternion.Euler(-pitch * 0.8f + 10f * fl - 28f * dE, 18f * dE, -lean * 0.6f + 14f * dE);

            if (alive)
            {
                float aimSway = Mathf.Sin(phase * 0.5f) * 3f * m;
                Quaternion aimR = Quaternion.Euler(-62f + aimSway + kick * 6f, 0f, -12f);
                Quaternion aimFR = Quaternion.Euler(-28f, 0f, 0f);
                Quaternion aimL = Quaternion.Euler(-74f + aimSway + kick * 6f, 30f, 18f);
                Quaternion aimFL = Quaternion.Euler(-34f, 0f, 0f);
                // Low ready: the right hand keeps the grip with the muzzle down, the left arm swings with the stride.
                Quaternion lowR = Quaternion.Euler(-48f + swing2 * 4f, 0f, -12f);
                Quaternion lowFR = Quaternion.Euler(-40f, 0f, 0f);
                Quaternion lowL = Quaternion.Euler(swing * 32f * m - 14f, 0f, 9f);
                Quaternion lowFL = Quaternion.Euler(-22f - Mathf.Max(0f, swing) * 34f * m, 0f, 0f);
                armR.localRotation = Quaternion.Slerp(lowR, aimR, aimBlend);
                foreR.localRotation = Quaternion.Slerp(lowFR, aimFR, aimBlend);
                armL.localRotation = Quaternion.Slerp(lowL, aimL, aimBlend);
                foreL.localRotation = Quaternion.Slerp(lowFL, aimFL, aimBlend);
                Vector3 aimPos = new Vector3(0.1f, 0.3f + Mathf.Sin(phase) * 0.008f * m, 0.3f - kick * 0.05f);
                Vector3 lowPos = new Vector3(0.14f, 0.22f + Mathf.Sin(phase * 2f) * 0.012f * m, 0.27f);
                gunMount.localPosition = Vector3.Lerp(lowPos, aimPos, aimBlend);
                gunMount.localRotation = Quaternion.Slerp(Quaternion.Euler(22f, -8f, 0f), Quaternion.Euler(-kick * 8f, 0f, 0f), aimBlend);
            }
            else
            {
                // Collapse: the arms fling up and out, then settle; the gun drops away.
                float ks = 1f - Mathf.Exp(-dt * 9f);
                armR.localRotation = Quaternion.Slerp(armR.localRotation, Quaternion.Euler(-130f * dE + 10f, 0f, -45f), ks);
                armL.localRotation = Quaternion.Slerp(armL.localRotation, Quaternion.Euler(-110f * dE + 10f, 0f, 50f), ks);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-30f, 0f, 0f), ks);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-45f, 0f, 0f), ks);
                gunMount.localPosition = Vector3.Lerp(gunMount.localPosition, new Vector3(0.3f, -0.3f, 0.4f), ks);
                gunMount.localRotation = Quaternion.Slerp(gunMount.localRotation, Quaternion.Euler(60f, 20f, 70f), ks);
            }
        }

        /// <summary>The head's height above the floor (moves down with the crouch).</summary>
        public float HeadHeight => 1.65f - 0.4f * crouchBlend;

        public void Destroy()
        {
            foreach (Mesh m in meshes) if (m != null) Object.Destroy(m);
            meshes.Clear();
            if (flashMesh != null) Object.Destroy(flashMesh);
            flashMesh = null;
        }
    }
}

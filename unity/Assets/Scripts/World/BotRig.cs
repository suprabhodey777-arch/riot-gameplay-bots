// Original stylised hero characters built in code. Every body part is a smooth
// loft (Loft.cs: tapered superelliptic sections with domed joints, so limbs
// read as skinned, not as boxes): a head with a face (jaw, nose, brows, eyes,
// mouth, ears), hair or a hood or a helmet, a jacket or coat with a collar,
// belt, holster and pouches, gloved hands, boots with soles, shoulder gear and
// a back item per kit. The four agents and the five personas each have their
// own build, palette, headgear, coat and accessories (see StyleFor), and the
// player's team colours (ally / enemy highlight) sit on the shoulder gear, an
// armband and a chest mark so a figure reads at a glance. One merged mesh per
// bone on one shared toon material (Saltworks/HeroToon: two-tone ramp, tinted
// shadow side, rim light); about twenty draw calls a figure.
//
// Secondary motion: coat panels follow the thighs, a scarf trails in three
// segments and a cape lags behind the shoulders, all driven by speed.
//
// Procedural animation: idle breathing and a slow weight shift, a run with
// arm swing, hip and shoulder counter-rotation and a forward lean, strafing
// with a cross-step and a lean into the move, crouch, aim (the upper body
// leads turns, the legs catch up), recoil kick, a reload (gun rolls, the
// support hand goes to the belt and back to the magazine), a hit flinch with a
// stagger away from the shot, and a collapse on death (knees buckle, arms go
// limp, head lolls; BotView tips the body over in the hit direction).
// Standing height 1.8 m, head centre at 1.65 m, matching BotView's colliders.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    public class BotRig
    {
        static Material bodyMat, heroMat;
        static readonly Dictionary<Color, Material> visorMats = new Dictionary<Color, Material>();
        static Material flashMat;

        public readonly Transform Root;
        readonly Transform pelvis, chest, head, armL, foreL, armR, foreR, thighL, shinL, thighR, shinR, gunMount;
        readonly List<MeshFilter> filters = new List<MeshFilter>();
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Transform> extraBones = new List<Transform>();
        Color team;
        readonly Color accent;
        readonly Color accent2;
        /// <summary>Silhouette id: an agent ("rook", "haze", "wren", "pike") or a persona ("zephyr", "atlas", "ember", "wisp", "nova").</summary>
        readonly string look;
        /// <summary>The agent whose kit sets the back item.</summary>
        readonly string kit;
        readonly Style style;
        GunModel gun;
        string gunId;
        Transform flash;
        Mesh flashMesh;
        float flashUntil;

        // Secondary-motion bones (rebuilt with the meshes).
        struct Panel
        {
            public Transform T;
            public bool Front;
            public bool Left;
        }

        readonly List<Panel> panels = new List<Panel>();
        readonly List<Transform> scarf = new List<Transform>();
        Transform cape;

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
        bool reloading;
        float reloadAt = -10f;
        float reloadBlend;
        float speedNow;

        /// <summary>The direction of the last hit (world, attacker to victim) and when it landed.</summary>
        public Vector3 LastHitDir { get; private set; }
        public float LastHitAt => hitAt;

        const float HipY = 0.95f;

        /// <summary>A plain lit world material (turntables, sign frames); characters use HeroMaterial.</summary>
        public static Material BodyMaterial
        {
            get
            {
                if (bodyMat == null) bodyMat = Mats.World(Color.white, Textures.Grain(), null, 0f, 0.3f, 0.6f, 0.62f, 1f);
                return bodyMat;
            }
        }

        /// <summary>The character material: Saltworks/HeroToon, or the world material if that shader is missing.</summary>
        public static Material HeroMaterial
        {
            get
            {
                if (heroMat != null) return heroMat;
                Shader s = Shader.Find("Saltworks/HeroToon");
                if (s != null && s.isSupported)
                {
                    heroMat = new Material(s) { name = "Hero toon", hideFlags = HideFlags.DontSave };
                    heroMat.SetTexture("_MainTex", Textures.Grain());
                }
                else
                {
                    Debug.LogWarning("Saltworks: Saltworks/HeroToon not found, characters use the world material.");
                    heroMat = BodyMaterial;
                }
                return heroMat;
            }
        }

        static Material VisorMat(Color c)
        {
            if (visorMats.TryGetValue(c, out Material m) && m != null) return m;
            m = Mats.Glow(c, 1.3f);
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
            style = StyleFor(look);
            float sw = 0.2f + 0.045f * style.W;
            Root = new GameObject("Rig").transform;
            Root.SetParent(parent, false);
            pelvis = Bone("Pelvis", Root, new Vector3(0f, HipY, 0f));
            chest = Bone("Chest", pelvis, new Vector3(0f, 0.12f, 0f));
            head = Bone("Head", chest, new Vector3(0f, 0.5f, 0f));
            armL = Bone("UpperArm L", chest, new Vector3(-sw, 0.42f, 0f));
            foreL = Bone("Forearm L", armL, new Vector3(0f, -0.27f, 0f));
            armR = Bone("UpperArm R", chest, new Vector3(sw, 0.42f, 0f));
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

        // ---- styles -----------------------------------------------------------------------------------
        enum CoatKind { None, Cropped, Long, Armoured, Field, Cloak, Tails }
        enum HairKind { Short, Swept, Crest, Ponytail }
        enum GearKind { None, Goggles, Hood, ScoutHelmet, ClosedHelmet, Cap, DeepHood, Halo, Headband }
        enum MaskKind { None, Respirator, Wraps }
        enum BackKind { Pack, None, Thrusters, Canisters, Antenna, Quiver, Radio, Blades, Pods, Case }

        class Style
        {
            public float W = 1f, D = 1f, Limb = 1f;
            public CoatKind Coat;
            public float CoatLen = 0.4f;
            public HairKind Hair;
            public GearKind Gear;
            public MaskKind Mask;
            public BackKind Back;
            public bool Scarf, Cape, HighCollar, Rolled, Vest, ChestPlate, Monocle, Cowl;
            /// <summary>0 none, 1 one pad (left), 2 both, 3 heavy layered plates.</summary>
            public int Pads = 2;
            public Color Jacket = C(0.2f, 0.22f, 0.25f), Shirt = C(0.3f, 0.3f, 0.3f), Pants = C(0.2f, 0.2f, 0.22f);
            public Color Boots = C(0.1f, 0.095f, 0.09f, 0.3f), Leather = C(0.24f, 0.17f, 0.12f, 0.4f);
            public Color Skin = C(0.75f, 0.56f, 0.44f), HairC = C(0.1f, 0.08f, 0.07f, 0.25f), Gear1 = C(0.3f, 0.31f, 0.33f, 0.85f);
        }

        static Color C(float r, float g, float b, float spec = 0f)
        {
            return new Color(r, g, b, spec);
        }

        static Color Shade(Color c, float k, float spec = -1f)
        {
            return new Color(c.r * k, c.g * k, c.b * k, spec < 0f ? c.a : spec);
        }

        static Color Spec(Color c, float spec)
        {
            return new Color(c.r, c.g, c.b, spec);
        }

        /// <summary>The look of each original character. Agents read their role; personas their style.</summary>
        static Style StyleFor(string id)
        {
            switch (id)
            {
                case "rook":
                    // Duelist: a light sprinter in a cropped jacket, swept hair, goggles up, a long scarf trailing.
                    return new Style
                    {
                        W = 0.94f, D = 0.95f, Limb = 0.92f, Coat = CoatKind.Cropped, Hair = HairKind.Swept, Gear = GearKind.Goggles, Back = BackKind.Thrusters,
                        Scarf = true, Pads = 1, Jacket = C(0.2f, 0.22f, 0.2f), Shirt = C(0.34f, 0.32f, 0.28f), Pants = C(0.25f, 0.23f, 0.19f),
                        Skin = C(0.76f, 0.56f, 0.42f), HairC = C(0.16f, 0.1f, 0.07f, 0.3f), Gear1 = C(0.2f, 0.21f, 0.22f, 0.85f),
                    };
                case "haze":
                    // Controller: a heavy knee-length coat with a high collar, hood and respirator, smoke canisters on the back.
                    return new Style
                    {
                        W = 1.1f, D = 1.12f, Limb = 1.08f, Coat = CoatKind.Long, CoatLen = 0.52f, Hair = HairKind.Short, Gear = GearKind.Hood, Mask = MaskKind.Respirator,
                        Back = BackKind.Canisters, HighCollar = true, Pads = 0, Jacket = C(0.17f, 0.19f, 0.22f), Shirt = C(0.12f, 0.12f, 0.13f),
                        Pants = C(0.13f, 0.13f, 0.14f), Skin = C(0.88f, 0.72f, 0.62f), Gear1 = C(0.3f, 0.31f, 0.33f, 0.9f),
                    };
                case "wren":
                    // Initiator: a scout in a utility vest, wraparound visor helmet and an antenna pack.
                    return new Style
                    {
                        W = 1f, Coat = CoatKind.None, Vest = true, Hair = HairKind.Short, Gear = GearKind.ScoutHelmet, Back = BackKind.Antenna, Pads = 2,
                        Jacket = C(0.2f, 0.26f, 0.27f), Shirt = C(0.2f, 0.26f, 0.27f), Pants = C(0.27f, 0.29f, 0.22f), Skin = C(0.42f, 0.28f, 0.2f),
                        HairC = C(0.05f, 0.04f, 0.04f, 0.2f), Gear1 = C(0.5f, 0.45f, 0.34f, 0.1f),
                    };
                case "pike":
                    // Sentinel: an armoured long coat, layered shoulder plates, a closed helmet and a dart quiver.
                    return new Style
                    {
                        W = 1.12f, D = 1.08f, Limb = 1.05f, Coat = CoatKind.Armoured, CoatLen = 0.62f, Hair = HairKind.Short, Gear = GearKind.ClosedHelmet,
                        Back = BackKind.Quiver, HighCollar = true, ChestPlate = true, Pads = 3, Jacket = C(0.12f, 0.14f, 0.19f), Shirt = C(0.1f, 0.1f, 0.12f),
                        Pants = C(0.1f, 0.1f, 0.12f), Skin = C(0.66f, 0.48f, 0.38f), Gear1 = C(0.42f, 0.44f, 0.48f, 0.9f),
                    };
                case "zephyr":
                    // Hyper-aggressive duelist: a puffy cropped bomber, sleeves pushed up, a bleached crest and headband.
                    return new Style
                    {
                        W = 0.97f, D = 1.06f, Limb = 0.95f, Coat = CoatKind.Cropped, Rolled = true, Hair = HairKind.Crest, Gear = GearKind.Headband,
                        Back = BackKind.Thrusters, Pads = 1, Jacket = C(0.36f, 0.12f, 0.14f), Shirt = C(0.84f, 0.83f, 0.79f), Pants = C(0.14f, 0.14f, 0.16f),
                        Skin = C(0.86f, 0.68f, 0.55f), HairC = C(0.86f, 0.78f, 0.58f, 0.3f), Gear1 = C(0.24f, 0.24f, 0.26f, 0.8f),
                    };
                case "atlas":
                    // In-game leader: a broad build in a field coat, a cape as the back piece, a cap and headset.
                    return new Style
                    {
                        W = 1.08f, D = 1.04f, Limb = 1.04f, Coat = CoatKind.Field, CoatLen = 0.34f, Hair = HairKind.Short, Gear = GearKind.Cap, Back = BackKind.None,
                        Cape = true, Pads = 2, Jacket = C(0.16f, 0.2f, 0.28f), Shirt = C(0.7f, 0.72f, 0.74f), Pants = C(0.2f, 0.21f, 0.23f),
                        Skin = C(0.52f, 0.35f, 0.25f), HairC = C(0.04f, 0.035f, 0.03f, 0.2f), Gear1 = C(0.24f, 0.25f, 0.27f, 0.8f),
                    };
                case "ember":
                    // Operator anchor: a short shoulder cloak and cowl, a ponytail, a scope monocle and a long rifle case.
                    return new Style
                    {
                        W = 0.98f, Coat = CoatKind.Cloak, Cowl = true, Hair = HairKind.Ponytail, Gear = GearKind.None, Monocle = true,
                        Back = BackKind.Case, Pads = 0, Jacket = C(0.38f, 0.22f, 0.13f), Shirt = C(0.22f, 0.2f, 0.18f), Pants = C(0.2f, 0.18f, 0.16f),
                        Skin = C(0.93f, 0.78f, 0.68f), HairC = C(0.48f, 0.18f, 0.1f, 0.3f), Gear1 = C(0.3f, 0.3f, 0.32f, 0.9f),
                    };
                case "wisp":
                    // Lurker: slim, a deep hood, face wraps, cloth tails and two short blades crossed on the back.
                    return new Style
                    {
                        W = 0.92f, D = 0.95f, Limb = 0.9f, Coat = CoatKind.Tails, CoatLen = 0.5f, Hair = HairKind.Short, Gear = GearKind.DeepHood, Mask = MaskKind.Wraps,
                        Back = BackKind.Blades, Pads = 0, Jacket = C(0.12f, 0.11f, 0.15f), Shirt = C(0.3f, 0.3f, 0.32f), Pants = C(0.1f, 0.1f, 0.12f),
                        Skin = C(0.7f, 0.55f, 0.48f), Gear1 = C(0.38f, 0.39f, 0.42f, 0.95f),
                    };
                case "nova":
                    // Utility general: a structured light jacket with short tails, a sleek helmet with a halo, drone pods.
                    return new Style
                    {
                        W = 1f, Coat = CoatKind.Tails, CoatLen = 0.3f, Hair = HairKind.Short, Gear = GearKind.Halo, Back = BackKind.Pods, ChestPlate = true, Pads = 2,
                        Jacket = C(0.78f, 0.8f, 0.82f), Shirt = C(0.2f, 0.22f, 0.24f), Pants = C(0.24f, 0.26f, 0.28f), Skin = C(0.66f, 0.47f, 0.36f),
                        Gear1 = C(0.9f, 0.91f, 0.92f, 0.6f),
                    };
                default:
                    return new Style { Coat = CoatKind.None, Vest = true, Hair = HairKind.Short, Gear = GearKind.ScoutHelmet, Back = BackKind.Pack };
            }
        }

        // ---- geometry helpers ------------------------------------------------------------------------
        static MeshKit Kit()
        {
            return new MeshKit(1) { UvScale = 4f };
        }

        static readonly Matrix4x4 I = Matrix4x4.identity;

        static List<Sec> S(params Sec[] a)
        {
            return new List<Sec>(a);
        }

        static Matrix4x4 At(float x, float y, float z, float rx = 0f, float ry = 0f, float rz = 0f)
        {
            return MeshKit.At(new Vector3(x, y, z), new Vector3(rx, ry, rz));
        }

        static void Box(MeshKit k, float x, float y, float z, float sx, float sy, float sz, float bevel, Color c, float rx = 0f, float ry = 0f, float rz = 0f)
        {
            k.Box(0, At(x, y, z, rx, ry, rz), new Vector3(sx, sy, sz), bevel, c);
        }

        static void Blob(MeshKit k, float x, float y, float z, float rx, float ry, float rz, Color c, float ex = 0f, float ey = 0f, float ez = 0f, int sides = 14, float sq = 2f)
        {
            Loft.Blob(k, I, new Vector3(x, y, z), new Vector3(rx, ry, rz), c, new Vector3(ex, ey, ez), sides, 8, sq);
        }

        static void Rod(MeshKit k, Vector3 a, Vector3 b, float ra, float rb, Color c, int sides = 10)
        {
            Loft.Rod(k, I, a, b, ra, rb, c, sides);
        }

        static Vector3 V(float x, float y, float z)
        {
            return new Vector3(x, y, z);
        }

        // ---- the figure ---------------------------------------------------------------------------------
        void BuildMeshes()
        {
            foreach (Mesh m in meshes) if (m != null) Object.Destroy(m);
            meshes.Clear();
            foreach (MeshFilter f in filters) if (f != null) Object.Destroy(f.gameObject);
            filters.Clear();
            foreach (Transform t in extraBones) if (t != null) Object.Destroy(t.gameObject);
            extraBones.Clear();
            panels.Clear();
            scarf.Clear();
            cape = null;

            Style st = style;
            float w = st.W, d = st.D, lb = st.Limb;
            Color teamC = C(team.r, team.g, team.b, 0.55f);
            Color teamDark = Shade(teamC, 0.7f);
            Color acc = C(accent.r, accent.g, accent.b, 0.2f);
            Color acc2 = C(accent2.r, accent2.g, accent2.b, 0.2f);
            Color jacket = st.Jacket, shirt = st.Shirt, pants = st.Pants, boots = st.Boots, leather = st.Leather, skin = st.Skin;
            Color metal = C(0.36f, 0.37f, 0.4f, 1f);
            Color darkMetal = C(0.12f, 0.125f, 0.135f, 0.9f);
            Color rubber = C(0.07f, 0.07f, 0.075f, 0.15f);
            Color sole = C(0.05f, 0.05f, 0.05f, 0.1f);
            Color glove = Shade(leather, 0.55f, 0.35f);
            bool coat = st.Coat == CoatKind.Long || st.Coat == CoatKind.Armoured || st.Coat == CoatKind.Field;

            // ---- pelvis: trousers, belt with a buckle, pouches, a back pouch; coat panels as their own bones -----
            MeshKit k = Kit();
            Loft.Tube(k, I, S(new Sec(-0.14f, 0.12f * w, 0.085f, 0f, 0f, 2.4f), new Sec(-0.08f, 0.15f * w, 0.1f, 0f, 0f, 2.4f), new Sec(0.02f, 0.158f * w, 0.105f, 0f, 0f, 2.4f),
                                 new Sec(0.1f, 0.145f * w, 0.098f, 0f, 0f, 2.4f), new Sec(0.14f, 0.13f * w, 0.09f, 0f, 0f, 2.4f)), 18, pants, true, false);
            Loft.Band(k, I, 0.03f, 0.078f, 0.162f * w, 0.11f, leather, 0f, 0f, 2.4f, 20);
            Box(k, 0f, 0.054f, 0.108f, 0.05f, 0.036f, 0.014f, 0.004f, metal);
            Box(k, 0.13f * w, 0.02f, 0.07f, 0.05f, 0.07f, 0.04f, 0.012f, Shade(leather, 0.9f), 0f, 30f, 0f);
            Box(k, -0.13f * w, 0.02f, 0.07f, 0.05f, 0.07f, 0.04f, 0.012f, Shade(leather, 0.9f), 0f, -30f, 0f);
            Box(k, 0f, 0.02f, -0.11f, 0.1f, 0.07f, 0.04f, 0.014f, Shade(leather, 0.85f));
            Attach(pelvis, k, "pelvis");
            BuildCoat(st, w, jacket, acc);

            // ---- chest -------------------------------------------------------------------------------------
            k = Kit();
            float hem = st.Coat == CoatKind.Cropped ? -0.035f : coat ? -0.12f : -0.075f;
            List<Sec> torso = S(new Sec(hem, 0.142f * w, 0.1f * d, 0f, 0f, 2.6f), new Sec(0.06f, 0.145f * w, 0.104f * d, 0f, 0.004f, 2.6f), new Sec(0.18f, 0.163f * w, 0.115f * d, 0f, 0.008f, 2.6f),
                                new Sec(0.29f, 0.184f * w, 0.12f * d, 0f, 0.006f, 2.6f), new Sec(0.38f, 0.19f * w, 0.112f * d, 0f, 0f, 2.6f), new Sec(0.44f, 0.162f * w, 0.094f * d, 0f, -0.004f, 2.4f),
                                new Sec(0.475f, 0.1f, 0.075f, 0f, -0.006f, 2f));
            Loft.Tube(k, I, torso, 20, jacket, true, true);
            if (coat)
            {
                // An open coat: the shirt shows down the front, lapels either side.
                Loft.Tube(k, I, S(new Sec(-0.06f, 0.15f * w, 0.108f * d, 0f, 0f, 2.6f), new Sec(0.18f, 0.168f * w, 0.12f * d, 0f, 0.008f, 2.6f), new Sec(0.36f, 0.19f * w, 0.114f * d, 0f, 0f, 2.6f)),
                          12, shirt, false, false, -16f, 16f, 0.006f);
                Loft.Tube(k, I, S(new Sec(0.22f, 0.172f * w, 0.124f * d, 0f, 0.008f, 2.6f), new Sec(0.4f, 0.19f * w, 0.114f * d, 0f, 0f, 2.6f)), 6, Shade(jacket, 0.8f), false, false, -30f, -14f, 0.008f);
                Loft.Tube(k, I, S(new Sec(0.22f, 0.172f * w, 0.124f * d, 0f, 0.008f, 2.6f), new Sec(0.4f, 0.19f * w, 0.114f * d, 0f, 0f, 2.6f)), 6, Shade(jacket, 0.8f), false, false, 14f, 30f, 0.008f);
                Loft.Band(k, I, -0.115f, -0.075f, 0.152f * w, 0.104f * d, leather, 0f, 0f, 2.6f, 20);
            }
            else
            {
                // A zipped jacket: a metal zip line and a ribbed hem.
                Box(k, 0f, (hem + 0.44f) * 0.5f, 0.112f * d + 0.006f, 0.008f, 0.44f - hem - 0.04f, 0.006f, 0.002f, metal);
                Loft.Band(k, I, hem - 0.004f, hem + 0.035f, 0.148f * w, 0.106f * d, Shade(jacket, 0.75f), 0f, 0f, 2.6f, 20);
            }
            if (st.Vest)
            {
                // Utility vest: a padded shell over the shirt, a row of pouches and a radio on the left strap.
                Loft.Tube(k, I, S(new Sec(0.02f, 0.155f * w, 0.112f * d, 0f, 0.004f, 3f), new Sec(0.18f, 0.172f * w, 0.124f * d, 0f, 0.008f, 3f), new Sec(0.36f, 0.19f * w, 0.12f * d, 0f, 0.002f, 3f),
                                     new Sec(0.43f, 0.15f * w, 0.1f * d, 0f, -0.004f, 3f)), 18, st.Gear1, true, false);
                for (int i = -1; i <= 1; i++) Box(k, i * 0.07f, 0.11f, 0.128f * d, 0.06f, 0.08f, 0.035f, 0.01f, Shade(st.Gear1, 0.82f));
                Box(k, -0.07f, 0.33f, 0.125f * d, 0.035f, 0.07f, 0.03f, 0.006f, darkMetal);
                Rod(k, V(-0.08f, 0.37f, 0.13f * d), V(-0.08f, 0.45f, 0.12f * d), 0.004f, 0.003f, darkMetal, 6);
            }
            if (st.ChestPlate)
            {
                Loft.Tube(k, I, S(new Sec(0.17f, 0.165f * w, 0.122f * d, 0f, 0.012f, 3.2f), new Sec(0.29f, 0.186f * w, 0.128f * d, 0f, 0.012f, 3.2f), new Sec(0.39f, 0.186f * w, 0.118f * d, 0f, 0.004f, 3.2f)),
                          12, Spec(st.Gear1, 0.85f), false, false, -58f, 58f, 0.01f);
            }
            // Team mark on the chest (left), so friend or foe reads from the front.
            Loft.Tube(k, I, S(new Sec(0.27f, 0.186f * w, 0.126f * d, 0f, 0.012f, 2.6f), new Sec(0.33f, 0.19f * w, 0.126f * d, 0f, 0.01f, 2.6f)), 4, teamC, false, false, -46f, -22f, 0.004f);
            // Collar.
            if (st.HighCollar)
                Loft.Tube(k, I, S(new Sec(0.4f, 0.13f, 0.1f, 0f, -0.01f), new Sec(0.5f, 0.1f, 0.085f, 0f, -0.006f), new Sec(0.6f, 0.108f, 0.095f, 0f, -0.004f)), 16, Shade(jacket, 0.9f), false, false, 34f, 326f, 0.012f);
            else
                Loft.Tube(k, I, S(new Sec(0.42f, 0.115f, 0.09f, 0f, -0.008f), new Sec(0.5f, 0.088f, 0.075f, 0f, -0.006f), new Sec(0.53f, 0.096f, 0.083f, 0f, -0.006f)), 14, Shade(jacket, 0.85f), false, false, 30f, 330f, 0.01f);
            if (st.Cowl)
                Loft.Tube(k, I, S(new Sec(0.38f, 0.16f, 0.12f, 0f, -0.02f), new Sec(0.46f, 0.15f, 0.12f, 0f, -0.03f), new Sec(0.52f, 0.11f, 0.1f, 0f, -0.035f)), 18, Shade(jacket, 0.9f), false, false, 0f, 360f, 0.014f);
            // Neck.
            Rod(k, V(0f, 0.43f, -0.004f), V(0f, 0.57f, 0.008f), 0.047f, 0.044f, skin, 12);
            if (st.Scarf) Loft.Band(k, I, 0.46f, 0.53f, 0.07f, 0.065f, acc, 0f, 0.0f, 2f, 16);
            // Shoulder gear in the team colour.
            float sx = 0.2f + 0.045f * w;
            if (st.Pads >= 1) Pauldron(k, -sx, teamC, teamDark, st.Pads >= 3);
            if (st.Pads >= 2) Pauldron(k, sx, teamC, teamDark, st.Pads >= 3);
            if (st.Coat == CoatKind.Cloak)
            {
                // A short cloak over the shoulders, longer at the back.
                Loft.Tube(k, I, S(new Sec(0.1f, 0.3f * w, 0.21f, 0f, -0.03f), new Sec(0.3f, 0.29f * w, 0.19f, 0f, -0.02f), new Sec(0.44f, 0.26f * w, 0.15f, 0f, -0.012f),
                                     new Sec(0.5f, 0.18f * w, 0.11f, 0f, -0.01f), new Sec(0.54f, 0.1f, 0.085f, 0f, -0.01f)), 24, jacket, false, false, 80f, 280f, 0.014f);
                Loft.Tube(k, I, S(new Sec(0.06f, 0.302f * w, 0.213f, 0f, -0.03f), new Sec(0.1f, 0.3f * w, 0.21f, 0f, -0.03f)), 24, acc, false, false, 80f, 280f, 0.016f);
            }
            BackItem(k, st, w, metal, darkMetal, rubber, leather, acc, acc2);
            Attach(chest, k, "chest");
            if (st.Scarf) BuildScarf(acc, acc2);
            if (st.Cape) BuildCape(st, acc, acc2, w);

            // ---- head -----------------------------------------------------------------------------------------
            k = Kit();
            var vk = Kit();
            HeadAndFace(k, vk, st, skin, acc, acc2, metal, darkMetal, rubber, teamC);
            Attach(head, k, "head");
            Attach(head, vk, "visor", VisorMat(Color.Lerp(accent, Color.white, 0.35f)), false);

            // ---- arms ------------------------------------------------------------------------------------------
            foreach (Transform arm in new[] { armL, armR })
            {
                bool left = arm == armL;
                k = Kit();
                var s = S(new Sec(-0.27f, 0.044f * lb, 0.046f * lb), new Sec(-0.14f, 0.052f * lb, 0.056f * lb), new Sec(-0.03f, 0.058f * lb, 0.06f * lb), new Sec(0.0f, 0.06f * lb, 0.062f * lb));
                Loft.DomeUp(s, 0.055f, 3);
                Loft.DomeDown(s, 0.035f, 2);
                Loft.Tube(k, I, s, 14, jacket, false, false);
                if (left) Loft.Band(k, I, -0.13f, -0.09f, 0.056f * lb, 0.06f * lb, teamC, 0f, 0f, 2f, 14);
                if (coat || st.Coat == CoatKind.Cloak) Loft.Band(k, I, -0.27f, -0.24f, 0.048f * lb, 0.05f * lb, Shade(jacket, 0.8f), 0f, 0f, 2f, 14);
                Attach(arm, k, "upper arm");
            }
            foreach (Transform fore in new[] { foreL, foreR })
            {
                bool left = fore == foreL;
                k = Kit();
                Color sleeve = st.Rolled ? skin : jacket;
                var s = S(new Sec(-0.22f, 0.034f * lb, 0.032f * lb), new Sec(-0.13f, 0.042f * lb, 0.044f * lb), new Sec(-0.03f, 0.046f * lb, 0.047f * lb));
                Loft.DomeUp(s, 0.04f, 3);
                Loft.DomeDown(s, 0.02f, 2);
                Loft.Tube(k, I, s, 12, sleeve, false, false);
                if (st.Rolled) Loft.Band(k, I, -0.04f, 0.02f, 0.052f * lb, 0.053f * lb, Shade(jacket, 0.9f), 0f, 0f, 2f, 12);
                else Loft.Band(k, I, -0.21f, -0.18f, 0.039f * lb, 0.038f * lb, Shade(jacket, 0.8f), 0f, 0f, 2f, 12);
                // Glove: cuff, palm, curled fingers, thumb and a knuckle plate.
                Loft.Band(k, I, -0.235f, -0.2f, 0.04f, 0.039f, glove, 0f, 0f, 2f, 12);
                Blob(k, 0f, -0.262f, 0.008f, 0.03f, 0.042f, 0.04f, glove, 0f, 0f, 0f, 12);
                Blob(k, 0f, -0.302f, 0.026f, 0.028f, 0.028f, 0.032f, glove, 20f, 0f, 0f, 12);
                float th = left ? 0.022f : -0.022f;
                Rod(k, V(th, -0.25f, 0.03f), V(th * 1.1f, -0.285f, 0.058f), 0.012f, 0.01f, glove, 8);
                Box(k, 0f, -0.29f, 0.05f, 0.05f, 0.022f, 0.012f, 0.005f, Shade(glove, 0.7f, 0.5f), 20f);
                Attach(fore, k, "forearm");
            }

            // ---- legs ------------------------------------------------------------------------------------------
            foreach (Transform thigh in new[] { thighL, thighR })
            {
                bool left = thigh == thighL;
                float o = left ? -1f : 1f;
                k = Kit();
                var s = S(new Sec(-0.43f, 0.06f * lb, 0.062f * lb), new Sec(-0.26f, 0.073f * lb, 0.078f * lb, 0f, 0.004f), new Sec(-0.09f, 0.086f * lb, 0.09f * lb, 0f, 0.006f),
                          new Sec(0.0f, 0.088f * lb, 0.09f * lb));
                Loft.DomeUp(s, 0.06f, 3);
                Loft.DomeDown(s, 0.045f, 2);
                Loft.Tube(k, I, s, 14, pants, false, false);
                // Knee pad, a cargo pocket outside, and a drop holster on the right.
                Blob(k, 0f, -0.43f, 0.05f, 0.05f, 0.058f, 0.028f, Spec(Shade(st.Gear1, 0.6f), 0.5f), 0f, 0f, 0f, 12, 2.6f);
                Box(k, o * 0.085f * lb, -0.2f, 0.0f, 0.024f, 0.1f, 0.085f, 0.01f, Shade(pants, 0.85f));
                if (!left)
                {
                    Loft.Band(k, I, -0.13f, -0.11f, 0.089f * lb, 0.093f * lb, leather, 0f, 0.004f, 2f, 14);
                    Loft.Band(k, I, -0.25f, -0.23f, 0.08f * lb, 0.084f * lb, leather, 0f, 0.004f, 2f, 14);
                    Box(k, 0.1f * lb, -0.2f, 0.01f, 0.03f, 0.16f, 0.09f, 0.014f, leather);
                    Box(k, 0.1f * lb, -0.1f, -0.005f, 0.026f, 0.06f, 0.035f, 0.008f, darkMetal, -12f);
                }
                Attach(thigh, k, "thigh");
            }
            foreach (Transform shin in new[] { shinL, shinR })
            {
                k = Kit();
                var s = S(new Sec(-0.32f, 0.046f * lb, 0.05f * lb), new Sec(-0.2f, 0.055f * lb, 0.062f * lb, 0f, -0.006f), new Sec(-0.08f, 0.06f * lb, 0.064f * lb, 0f, -0.004f),
                          new Sec(0.0f, 0.058f * lb, 0.06f * lb));
                Loft.DomeUp(s, 0.04f, 3);
                Loft.Tube(k, I, s, 14, pants, true, false);
                // Boot: shaft, cuff, the foot lofted toe to heel, a thick sole and a heel block.
                Loft.Tube(k, I, S(new Sec(-0.43f, 0.058f, 0.07f, 0f, 0.012f, 2.4f), new Sec(-0.32f, 0.056f, 0.064f, 0f, 0.004f), new Sec(-0.22f, 0.06f, 0.066f)), 14, boots, false, false);
                Loft.Band(k, I, -0.235f, -0.205f, 0.063f, 0.069f, Shade(boots, 1.4f), 0f, 0f, 2f, 14);
                var foot = S(new Sec(-0.06f, 0.046f, 0.036f, 0f, 0f, 2.8f), new Sec(-0.02f, 0.053f, 0.046f, 0f, 0f, 2.8f), new Sec(0.07f, 0.055f, 0.04f, 0f, 0.006f, 2.8f),
                             new Sec(0.14f, 0.047f, 0.03f, 0f, 0.012f, 2.8f));
                Loft.DomeUp(foot, 0.032f, 3);
                Loft.DomeDown(foot, 0.02f, 2);
                Loft.Tube(k, Matrix4x4.TRS(V(0f, -0.418f, 0.0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one), foot, 14, boots, false, false);
                Box(k, 0f, -0.457f, 0.048f, 0.11f, 0.022f, 0.27f, 0.008f, sole);
                Box(k, 0f, -0.45f, -0.045f, 0.1f, 0.03f, 0.075f, 0.008f, sole);
                Box(k, 0f, -0.39f, 0.14f, 0.07f, 0.012f, 0.04f, 0.004f, Shade(boots, 1.5f), -25f);
                Attach(shin, k, "shin");
            }
        }

        void Pauldron(MeshKit k, float x, Color plate, Color dark, bool heavy)
        {
            float side = Mathf.Sign(x);
            float big = heavy ? 1.25f : 1f;
            Blob(k, x * 1.02f, 0.44f, 0f, 0.075f * big, 0.042f * big, 0.085f * big, plate, 0f, 0f, -side * 24f, 14, 2.6f);
            Blob(k, x * 1.06f, 0.418f, 0f, 0.072f * big, 0.012f, 0.082f * big, dark, 0f, 0f, -side * 24f, 14, 2.6f);
            if (heavy)
            {
                Blob(k, x * 1.14f, 0.39f, 0f, 0.07f, 0.035f, 0.085f, dark, 0f, 0f, -side * 34f, 14, 2.6f);
                Blob(k, x * 1.22f, 0.34f, 0f, 0.06f, 0.03f, 0.08f, plate, 0f, 0f, -side * 44f, 14, 2.6f);
            }
        }

        /// <summary>The back item, by kit and silhouette.</summary>
        void BackItem(MeshKit k, Style st, float w, Color metal, Color dark, Color rubber, Color leather, Color acc, Color acc2)
        {
            float bz = -0.115f * st.D;
            switch (st.Back)
            {
                case BackKind.Thrusters:
                    // A compact dash rig: a rounded housing and two angled nozzles.
                    Blob(k, 0f, 0.3f, bz - 0.04f, 0.1f, 0.12f, 0.045f, Spec(st.Gear1, 0.6f), 0f, 0f, 0f, 14, 3.5f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Rod(k, V(s * 0.065f, 0.26f, bz - 0.07f), V(s * 0.08f, 0.12f, bz - 0.1f), 0.026f, 0.034f, metal, 12);
                        Loft.Band(k, Loft.Along(I, V(s * 0.065f, 0.26f, bz - 0.07f), V(s * 0.08f, 0.12f, bz - 0.1f)), 0.1f, 0.13f, 0.037f, 0.037f, dark, 0f, 0f, 2f, 12);
                    }
                    Box(k, 0f, 0.38f, bz - 0.08f, 0.1f, 0.015f, 0.01f, 0.003f, acc);
                    break;
                case BackKind.Canisters:
                    Box(k, 0f, 0.3f, bz - 0.02f, 0.22f, 0.3f, 0.025f, 0.008f, leather);
                    for (int i = -1; i <= 1; i++)
                    {
                        Rod(k, V(i * 0.075f, 0.17f, bz - 0.065f), V(i * 0.075f, 0.43f, bz - 0.065f), 0.034f, 0.034f, Spec(Shade(st.Gear1, 0.8f), 0.8f), 14);
                        Blob(k, i * 0.075f, 0.45f, bz - 0.065f, 0.026f, 0.014f, 0.026f, acc, 0f, 0f, 0f, 10);
                        Loft.Band(k, Loft.Along(I, V(i * 0.075f, 0.17f, bz - 0.065f), V(i * 0.075f, 0.43f, bz - 0.065f)), 0.1f, 0.13f, 0.037f, 0.037f, dark, 0f, 0f, 2f, 12);
                    }
                    break;
                case BackKind.Antenna:
                    Blob(k, 0f, 0.29f, bz - 0.05f, 0.115f, 0.14f, 0.055f, Spec(st.Gear1, 0.15f), 0f, 0f, 0f, 14, 3.5f);
                    Blob(k, 0f, 0.3f, bz - 0.1f, 0.07f, 0.08f, 0.02f, dark, 0f, 0f, 0f, 12, 3f);
                    Rod(k, V(0.08f, 0.38f, bz - 0.06f), V(0.11f, 0.8f, bz - 0.1f), 0.007f, 0.003f, dark, 6);
                    Blob(k, 0.11f, 0.81f, bz - 0.1f, 0.01f, 0.01f, 0.01f, acc, 0f, 0f, 0f, 8);
                    Rod(k, V(-0.06f, 0.38f, bz - 0.06f), V(-0.08f, 0.58f, bz - 0.08f), 0.006f, 0.003f, dark, 6);
                    Blob(k, -0.13f, 0.32f, bz - 0.04f, 0.012f, 0.05f, 0.05f, metal, 0f, 0f, 0f, 12);
                    break;
                case BackKind.Quiver:
                case BackKind.Case:
                {
                    bool longCase = st.Back == BackKind.Case;
                    Vector3 a = longCase ? V(-0.12f, 0.05f, bz - 0.14f) : V(0.05f, 0.08f, bz - 0.05f);
                    Vector3 b = longCase ? V(0.14f, 0.66f, bz - 0.17f) : V(0.15f, 0.5f, bz - 0.08f);
                    Rod(k, a, b, longCase ? 0.05f : 0.045f, longCase ? 0.045f : 0.05f, leather, 12);
                    Loft.Band(k, Loft.Along(I, a, b), 0.08f, 0.11f, 0.053f, 0.053f, metal, 0f, 0f, 2f, 12);
                    Loft.Band(k, Loft.Along(I, a, b), 0.36f, 0.39f, 0.053f, 0.053f, metal, 0f, 0f, 2f, 12);
                    // A strap across the chest.
                    Loft.Tube(k, I, S(new Sec(0.06f, 0.158f * w, 0.112f, 0f, 0f, 2.6f), new Sec(0.1f, 0.162f * w, 0.115f, 0f, 0f, 2.6f)), 16, leather, false, false, -60f, 60f, 0.006f);
                    if (!longCase)
                        for (int i = 0; i < 3; i++)
                        {
                            Vector3 top = b + V(-0.02f + i * 0.02f, 0.02f, 0.005f * i);
                            Rod(k, top, top + V(0.005f, 0.07f, -0.005f), 0.006f, 0.004f, acc, 6);
                        }
                    break;
                }
                case BackKind.Radio:
                    Box(k, 0f, 0.3f, bz - 0.055f, 0.2f, 0.22f, 0.09f, 0.02f, Spec(Shade(st.Gear1, 0.9f), 0.5f));
                    Box(k, 0.04f, 0.33f, bz - 0.103f, 0.08f, 0.06f, 0.008f, 0.003f, dark);
                    Rod(k, V(-0.07f, 0.4f, bz - 0.06f), V(-0.09f, 0.72f, bz - 0.08f), 0.006f, 0.003f, dark, 6);
                    break;
                case BackKind.Blades:
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Box(k, s * 0.01f, 0.28f, bz - 0.04f, 0.026f, 0.46f, 0.006f, 0.002f, Spec(st.Gear1, 1f), 0f, 0f, s * 24f);
                        Matrix4x4 gm = At(s * 0.01f, 0.28f, bz - 0.04f, 0f, 0f, s * 24f);
                        Vector3 g0 = gm.MultiplyPoint3x4(V(0f, 0.23f, 0f)), g1 = gm.MultiplyPoint3x4(V(0f, 0.33f, 0f));
                        Rod(k, g0, g1, 0.014f, 0.014f, Shade(leather, 0.6f), 8);
                        Box(k, s * 0.01f, 0.28f, bz - 0.045f, 0.012f, 0.06f, 0.012f, 0.003f, dark, 0f, 0f, s * 24f);
                    }
                    break;
                case BackKind.Pods:
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Blob(k, s * 0.17f * w, 0.24f, bz - 0.01f, 0.04f, 0.075f, 0.06f, Spec(st.Gear1, 0.6f), 0f, 0f, 0f, 14, 2.6f);
                        Blob(k, s * 0.205f * w, 0.24f, bz - 0.01f, 0.008f, 0.05f, 0.04f, acc, 0f, 0f, 0f, 10, 2.6f);
                    }
                    Blob(k, 0f, 0.3f, bz - 0.03f, 0.08f, 0.1f, 0.03f, Spec(Shade(st.Gear1, 0.85f), 0.6f), 0f, 0f, 0f, 12, 3f);
                    break;
                case BackKind.None:
                    break;
                default:
                    Blob(k, 0f, 0.3f, bz - 0.05f, 0.11f, 0.14f, 0.05f, Spec(st.Gear1, 0.2f), 0f, 0f, 0f, 14, 3.5f);
                    break;
            }
        }

        /// <summary>Head, face, hair and headgear into `k`; glowing visor parts into `v`.</summary>
        void HeadAndFace(MeshKit k, MeshKit v, Style st, Color skin, Color acc, Color acc2, Color metal, Color dark, Color rubber, Color teamC)
        {
            Color skinDark = Shade(skin, 0.86f);
            Color hair = st.HairC;
            Color eye = C(0.05f, 0.045f, 0.045f, 1f);
            Color white = C(0.9f, 0.88f, 0.85f, 0.3f);
            bool closed = st.Gear == GearKind.ClosedHelmet;
            // Skull and jaw.
            Blob(k, 0f, 0.08f, 0f, 0.092f, 0.112f, 0.102f, skin, 0f, 0f, 0f, 18);
            Blob(k, 0f, 0.022f, 0.022f, 0.072f, 0.066f, 0.08f, skin, 0f, 0f, 0f, 16);
            if (!closed)
            {
                // The face: nose, brows, eyes with a catch light, mouth, ears.
                Rod(k, V(0f, 0.078f, 0.098f), V(0f, 0.046f, 0.113f), 0.011f, 0.015f, skinDark, 8);
                for (int s = -1; s <= 1; s += 2)
                {
                    Rod(k, V(s * 0.056f, 0.103f, 0.088f), V(s * 0.017f, 0.107f, 0.099f), 0.0065f, 0.0075f, Spec(Shade(hair, 0.9f), 0.1f), 6);
                    Blob(k, s * 0.034f, 0.083f, 0.095f, 0.017f, 0.0115f, 0.008f, white, 0f, 0f, 0f, 10);
                    Blob(k, s * 0.033f, 0.083f, 0.1005f, 0.0085f, 0.0095f, 0.004f, eye, 0f, 0f, 0f, 8);
                    Blob(k, s * 0.092f, 0.07f, 0f, 0.012f, 0.026f, 0.019f, skinDark, 0f, 0f, 0f, 8);
                }
                Rod(k, V(-0.02f, 0.031f, 0.1f), V(0.02f, 0.031f, 0.1f), 0.0045f, 0.0045f, Shade(skin, 0.62f), 6);
            }
            // Hair.
            if (st.Gear != GearKind.ClosedHelmet && st.Gear != GearKind.Halo && st.Gear != GearKind.DeepHood && st.Gear != GearKind.Hood)
            {
                Blob(k, 0f, 0.115f, -0.016f, 0.1f, 0.1f, 0.11f, hair, 0f, 0f, 0f, 16);
                switch (st.Hair)
                {
                    case HairKind.Swept:
                        for (int i = -1; i <= 1; i++)
                            Rod(k, V(i * 0.05f, 0.17f, 0.03f), V(i * 0.065f, 0.2f, -0.14f), 0.035f, 0.006f, hair, 8);
                        Rod(k, V(0.03f, 0.165f, 0.07f), V(0.075f, 0.12f, 0.1f), 0.022f, 0.005f, hair, 8);
                        break;
                    case HairKind.Crest:
                        for (int i = 0; i < 4; i++)
                            Rod(k, V(0f, 0.18f, 0.06f - i * 0.05f), V(0f, 0.28f - i * 0.02f, -0.02f - i * 0.065f), 0.03f, 0.004f, hair, 8);
                        break;
                    case HairKind.Ponytail:
                        Rod(k, V(0f, 0.15f, -0.095f), V(0f, -0.08f, -0.16f), 0.032f, 0.012f, hair, 10);
                        Loft.Band(k, Loft.Along(I, V(0f, 0.15f, -0.095f), V(0f, -0.08f, -0.16f)), 0.03f, 0.05f, 0.03f, 0.03f, acc, 0f, 0f, 2f, 10);
                        break;
                }
            }
            switch (st.Gear)
            {
                case GearKind.Goggles:
                    Loft.Band(k, I, 0.125f, 0.148f, 0.104f, 0.112f, rubber, 0f, -0.01f, 2f, 18);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Blob(k, s * 0.036f, 0.152f, 0.093f, 0.03f, 0.021f, 0.016f, Spec(Shade(acc, 0.35f), 1f), -25f, 0f, 0f, 12);
                        Blob(k, s * 0.036f, 0.152f, 0.089f, 0.034f, 0.025f, 0.014f, dark, -25f, 0f, 0f, 12);
                    }
                    break;
                case GearKind.Headband:
                    Loft.Band(k, I, 0.12f, 0.15f, 0.104f, 0.112f, acc, 0f, -0.01f, 2f, 18);
                    Loft.Ribbon(k, I, V(0f, 0.135f, -0.12f), V(0.03f, 0.08f, -0.2f), 0.03f, 0.02f, V(0.8f, 0f, -0.6f), acc);
                    Loft.Ribbon(k, I, V(0f, 0.135f, -0.12f), V(-0.02f, 0.06f, -0.19f), 0.03f, 0.02f, V(-0.8f, 0f, -0.6f), acc);
                    break;
                case GearKind.Hood:
                case GearKind.DeepHood:
                {
                    bool deep = st.Gear == GearKind.DeepHood;
                    float open = deep ? 40f : 52f;
                    Color cloth = Shade(st.Jacket, deep ? 1f : 0.92f);
                    Loft.Tube(k, I, S(new Sec(-0.03f, 0.12f, 0.125f, 0f, -0.012f), new Sec(0.08f, 0.128f, 0.132f, 0f, -0.004f), new Sec(0.16f, 0.114f, 0.124f, 0f, 0.004f),
                                      new Sec(0.215f, 0.075f, deep ? 0.11f : 0.085f, 0f, deep ? 0.015f : -0.012f), new Sec(0.24f, 0.02f, 0.03f, 0f, deep ? 0.04f : -0.02f)),
                              20, cloth, false, false, open, 360f - open, 0.012f);
                    Loft.Tube(k, I, S(new Sec(-0.13f, 0.15f, 0.13f, 0f, -0.02f), new Sec(-0.03f, 0.122f, 0.127f, 0f, -0.012f)), 16, cloth, false, false, 70f, 290f, 0.012f);
                    if (deep)
                    {
                        // Shadow inside the hood and two faint eye glints.
                        Blob(k, 0f, 0.09f, 0.03f, 0.095f, 0.105f, 0.08f, C(0.03f, 0.03f, 0.04f), 0f, 0f, 0f, 14);
                        for (int s = -1; s <= 1; s += 2) Blob(v, s * 0.032f, 0.085f, 0.111f, 0.012f, 0.005f, 0.004f, Color.white, 0f, 0f, 0f, 8);
                    }
                    else Blob(k, 0f, 0.115f, -0.01f, 0.098f, 0.1f, 0.104f, Shade(hair, 1f), 0f, 0f, 0f, 14);
                    break;
                }
                case GearKind.ScoutHelmet:
                    Blob(k, 0f, 0.118f, -0.008f, 0.108f, 0.095f, 0.118f, Spec(st.Gear1, 0.45f), 0f, 0f, 0f, 18, 2.2f);
                    Loft.Band(k, I, 0.1f, 0.118f, 0.111f, 0.121f, Spec(Shade(st.Gear1, 0.7f), 0.4f), 0f, -0.008f, 2.2f, 20);
                    for (int s = -1; s <= 1; s += 2) Blob(k, s * 0.104f, 0.07f, 0f, 0.022f, 0.036f, 0.036f, dark, 0f, 0f, 0f, 12);
                    // The wraparound scout visor.
                    Loft.Tube(v, I, S(new Sec(0.066f, 0.103f, 0.113f, 0f, 0.003f), new Sec(0.104f, 0.107f, 0.118f, 0f, 0.0f)), 16, Color.white, false, false, -78f, 78f, 0.006f);
                    Loft.Tube(k, I, S(new Sec(0.058f, 0.106f, 0.116f, 0f, 0.003f), new Sec(0.066f, 0.106f, 0.116f, 0f, 0.003f)), 16, dark, false, false, -82f, 82f, 0.006f);
                    Rod(k, V(0.1f, 0.12f, -0.04f), V(0.12f, 0.27f, -0.08f), 0.004f, 0.002f, dark, 6);
                    break;
                case GearKind.ClosedHelmet:
                    Blob(k, 0f, 0.086f, 0.002f, 0.113f, 0.13f, 0.12f, Spec(st.Gear1, 0.9f), 0f, 0f, 0f, 18, 2.3f);
                    Blob(k, 0f, 0.03f, 0.07f, 0.084f, 0.072f, 0.062f, Spec(Shade(st.Gear1, 0.55f), 0.8f), 0f, 0f, 0f, 16, 2.6f);
                    Rod(k, V(0f, 0.2f, 0.09f), V(0f, 0.16f, -0.12f), 0.014f, 0.01f, Spec(teamC, 0.6f), 8);
                    Rod(k, V(-0.064f, 0.09f, 0.112f), V(0.064f, 0.09f, 0.112f), 0.0085f, 0.0085f, Color.white, 8);
                    Rod(k, V(0f, 0.088f, 0.116f), V(0f, 0.036f, 0.118f), 0.0065f, 0.0055f, Color.white, 8);
                    Loft.Tube(k, I, S(new Sec(-0.03f, 0.118f, 0.125f), new Sec(0.02f, 0.116f, 0.124f)), 18, dark, false, false, 60f, 300f, 0.01f);
                    break;
                case GearKind.Cap:
                    Blob(k, 0f, 0.135f, -0.005f, 0.103f, 0.07f, 0.11f, Shade(st.Jacket, 0.85f), 0f, 0f, 0f, 16);
                    Blob(k, 0f, 0.132f, 0.1f, 0.082f, 0.008f, 0.06f, Shade(st.Jacket, 0.65f), -8f, 0f, 0f, 14);
                    for (int s = -1; s <= 1; s += 2) Blob(k, s * 0.1f, 0.075f, 0f, 0.022f, 0.036f, 0.036f, dark, 0f, 0f, 0f, 12);
                    for (int i = 0; i < 6; i++)
                    {
                        float a0 = (-90f + i * 30f) * Mathf.Deg2Rad, a1 = (-60f + i * 30f) * Mathf.Deg2Rad;
                        Rod(k, V(Mathf.Sin(a0) * 0.108f, 0.075f + Mathf.Cos(a0) * 0.138f, -0.012f), V(Mathf.Sin(a1) * 0.108f, 0.075f + Mathf.Cos(a1) * 0.138f, -0.012f), 0.006f, 0.006f, dark, 6);
                    }
                    Rod(k, V(0.1f, 0.06f, 0.02f), V(0.045f, 0.03f, 0.1f), 0.004f, 0.004f, dark, 6);
                    Blob(k, 0.04f, 0.03f, 0.104f, 0.01f, 0.01f, 0.012f, dark, 0f, 0f, 0f, 8);
                    break;
                case GearKind.Halo:
                    Blob(k, 0f, 0.092f, 0f, 0.106f, 0.12f, 0.114f, Spec(st.Gear1, 0.7f), 0f, 0f, 0f, 18, 2.2f);
                    Blob(k, 0f, 0.08f, 0.08f, 0.086f, 0.04f, 0.042f, dark, 0f, 0f, 0f, 14, 2.6f);
                    Blob(v, 0f, 0.082f, 0.088f, 0.076f, 0.028f, 0.036f, Color.white, 0f, 0f, 0f, 14, 2.6f);
                    for (int s = -1; s <= 1; s += 2) Blob(k, s * 0.105f, 0.07f, 0f, 0.012f, 0.04f, 0.04f, acc, 0f, 0f, 0f, 10);
                    for (int i = 0; i < 16; i++)
                    {
                        float a0 = i * Mathf.PI * 2f / 16f, a1 = (i + 0.7f) * Mathf.PI * 2f / 16f;
                        Rod(v, V(Mathf.Sin(a0) * 0.15f, 0.27f, Mathf.Cos(a0) * 0.15f - 0.03f), V(Mathf.Sin(a1) * 0.15f, 0.27f, Mathf.Cos(a1) * 0.15f - 0.03f), 0.006f, 0.006f, Color.white, 6);
                    }
                    break;
            }
            switch (st.Mask)
            {
                case MaskKind.Respirator:
                    Blob(k, 0f, 0.033f, 0.083f, 0.066f, 0.05f, 0.042f, rubber, 0f, 0f, 0f, 14, 2.4f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Rod(k, V(s * 0.05f, 0.025f, 0.1f), V(s * 0.082f, 0.012f, 0.128f), 0.026f, 0.026f, Spec(st.Gear1, 0.9f), 12);
                        Blob(k, s * 0.084f, 0.011f, 0.13f, 0.02f, 0.02f, 0.006f, dark, 0f, s * 40f, 0f, 10);
                    }
                    Blob(k, 0f, 0.02f, 0.124f, 0.018f, 0.02f, 0.012f, dark, 0f, 0f, 0f, 10);
                    break;
                case MaskKind.Wraps:
                    Loft.Band(k, I, -0.01f, 0.065f, 0.082f, 0.094f, Shade(st.Shirt, 0.9f), 0f, 0.02f, 2f, 16);
                    break;
            }
            if (st.Monocle)
            {
                Rod(k, V(0.035f, 0.084f, 0.094f), V(0.038f, 0.087f, 0.132f), 0.018f, 0.016f, metal, 12);
                Blob(v, 0.038f, 0.087f, 0.133f, 0.012f, 0.012f, 0.003f, Color.white, 0f, 0f, 0f, 10);
                Rod(k, V(0.06f, 0.09f, 0.1f), V(0.09f, 0.08f, 0.02f), 0.004f, 0.004f, dark, 6);
            }
        }

        /// <summary>Coat panels as pelvis bones (they swing with the thighs), or cloth tails at the back.</summary>
        void BuildCoat(Style st, float w, Color jacket, Color acc)
        {
            if (st.Coat == CoatKind.Long || st.Coat == CoatKind.Armoured || st.Coat == CoatKind.Field)
            {
                AddPanel(st, w, "Coat FL", -98f, -6f, true, true, jacket, acc);
                AddPanel(st, w, "Coat FR", 6f, 98f, true, false, jacket, acc);
                AddPanel(st, w, "Coat BL", -178f, -88f, false, true, jacket, acc);
                AddPanel(st, w, "Coat BR", 88f, 178f, false, false, jacket, acc);
            }
            else if (st.Coat == CoatKind.Tails)
            {
                AddPanel(st, w, "Tail L", -168f, -126f, false, true, jacket, acc);
                AddPanel(st, w, "Tail R", 126f, 168f, false, false, jacket, acc);
            }
        }

        void AddPanel(Style st, float w, string name, float a0, float a1, bool front, bool left, Color cloth, Color acc)
        {
            float len = st.CoatLen;
            var t = Bone(name, pelvis, new Vector3(0f, 0.05f, 0f));
            extraBones.Add(t);
            MeshKit k = Kit();
            float flare = len * 0.14f;
            var s = S(new Sec(-len, 0.2f * w + flare, 0.14f + flare * 0.8f, 0f, 0f, 2.4f), new Sec(-len * 0.5f, 0.19f * w + flare * 0.5f, 0.128f + flare * 0.4f, 0f, 0f, 2.4f),
                      new Sec(0.02f, 0.172f * w, 0.115f, 0f, 0f, 2.4f));
            Loft.Tube(k, I, s, 8, cloth, false, false, a0, a1, 0.012f);
            Loft.Tube(k, I, S(new Sec(-len, 0.203f * w + flare, 0.143f + flare * 0.8f, 0f, 0f, 2.4f), new Sec(-len + 0.035f, 0.2f * w + flare * 0.94f, 0.14f + flare * 0.75f, 0f, 0f, 2.4f)),
                      8, Shade(cloth, 0.7f), false, false, a0, a1, 0.014f);
            if (st.Coat == CoatKind.Armoured)
            {
                // Riveted plates on the coat.
                float b0 = -len * 0.72f, b1 = -len * 0.18f;
                Loft.Tube(k, I, S(new Sec(b0, 0.206f * w + flare * 0.72f, 0.145f + flare * 0.6f, 0f, 0f, 2.4f), new Sec(b1, 0.193f * w + flare * 0.2f, 0.132f + flare * 0.16f, 0f, 0f, 2.4f)),
                          6, Spec(st.Gear1, 0.85f), false, false, a0 + 8f, a1 - 8f, 0.008f);
            }
            if (st.Coat == CoatKind.Tails)
                Loft.Tube(k, I, S(new Sec(-len, 0.204f * w + flare, 0.144f + flare * 0.8f, 0f, 0f, 2.4f), new Sec(-len + 0.03f, 0.2f * w + flare, 0.14f + flare * 0.78f, 0f, 0f, 2.4f)),
                          4, acc, false, false, a0, a1, 0.014f);
            Attach(t, k, name);
            panels.Add(new Panel { T = t, Front = front, Left = left });
        }

        void BuildScarf(Color acc, Color acc2)
        {
            Transform parent = chest;
            Vector3 at = new Vector3(0.035f, 0.47f, -0.12f);
            for (int i = 0; i < 3; i++)
            {
                var t = Bone("Scarf " + i, parent, at);
                if (i == 0) extraBones.Add(t);
                MeshKit k = Kit();
                float w0 = 0.085f - i * 0.006f, w1 = 0.08f - i * 0.006f;
                Loft.Ribbon(k, I, Vector3.zero, new Vector3(0f, -0.2f, 0f), w0, w1, Vector3.back, i == 2 ? acc2 : acc);
                Attach(t, k, "scarf");
                scarf.Add(t);
                parent = t;
                at = new Vector3(0f, -0.19f, 0f);
            }
        }

        void BuildCape(Style st, Color acc, Color acc2, float w)
        {
            cape = Bone("Cape", chest, new Vector3(0f, 0.45f, -0.06f));
            extraBones.Add(cape);
            MeshKit k = Kit();
            Color cloth = Shade(acc, 0.45f, 0f);
            Loft.Tube(k, I, S(new Sec(-0.9f, 0.27f * w, 0.17f, 0f, 0.06f), new Sec(-0.45f, 0.24f * w, 0.15f, 0f, 0.06f), new Sec(-0.1f, 0.2f * w, 0.12f, 0f, 0.05f),
                              new Sec(0.02f, 0.16f * w, 0.1f, 0f, 0.05f)), 12, cloth, false, false, 112f, 248f, 0.012f);
            Loft.Tube(k, I, S(new Sec(-0.9f, 0.273f * w, 0.173f, 0f, 0.06f), new Sec(-0.86f, 0.27f * w, 0.171f, 0f, 0.06f)), 12, acc2, false, false, 112f, 248f, 0.014f);
            Attach(cape, k, "cape");
        }

        void Attach(Transform bone, MeshKit k, string name, Material mat = null, bool shadows = true)
        {
            if (k.Empty) return;
            var go = new GameObject(name);
            go.transform.SetParent(bone, false);
            Mesh m = k.ToMesh("hero " + name);
            meshes.Add(m);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = m;
            filters.Add(mf);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat ?? HeroMaterial;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
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

        /// <summary>Is the bot reloading (the sim's reloadingUntilTick)? Plays the reload while true.</summary>
        public void SetReloading(bool r)
        {
            if (r && !reloading) reloadAt = Time.time;
            reloading = r;
        }

        public Vector3 MuzzlePos => gun != null && gun.MuzzlePoint != null ? gun.MuzzlePoint.position : Root.position + Vector3.up * 1.4f;

        /// <summary>Ground speed from the last Animate (m/s).</summary>
        public float Speed => speedNow;

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
            speedNow = speed;
            float f = speed > 0.05f ? Vector3.Dot(vel, fwd) / speed : 0f;
            float s = speed > 0.05f ? Vector3.Dot(vel, right) / speed : 0f;
            float k = 1f - Mathf.Exp(-dt * 10f);
            moveBlend = Mathf.Lerp(moveBlend, Mathf.Clamp01(speed / 3.5f), k);
            fwdBlend = Mathf.Lerp(fwdBlend, f, k);
            sideBlend = Mathf.Lerp(sideBlend, s, k);
            crouchBlend = Mathf.MoveTowards(crouchBlend, crouch && alive ? 1f : 0f, dt * 5f);
            reloadBlend = Mathf.MoveTowards(reloadBlend, reloading && alive ? 1f : 0f, dt * 6f);
            // Stride-locked cadence: one gait cycle (two steps) per CycleLength metres, so feet do not skate.
            float strideLen = CycleLength(speed, crouchBlend);
            if (speed > 0.15f) phase += dt * speed / strideLen * Mathf.PI * 2f;
            else if (speed <= 0.05f) phase = Mathf.Lerp(phase, Mathf.Round(phase / Mathf.PI) * Mathf.PI, k * 0.5f);
            float sprint = Mathf.Clamp01((speed - 3.2f) / 2.2f);
            lean = Mathf.Lerp(lean, alive ? -sideBlend * moveBlend * 10f : 0f, k);
            // Low ready while running and not shooting; the rifle comes up to aim when slow, crouched or just fired.
            bool aimNow = (t - shotAt < 1.4f || speed < 2.4f || crouchBlend > 0.5f) && reloadBlend < 0.5f;
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
            float fl = Mathf.Clamp01(1f - (t - hitAt) / 0.32f);
            fl *= fl;

            float m = moveBlend;
            float idle = 1f - m;
            float swing = Mathf.Sin(phase);
            float swing2 = Mathf.Sin(phase + Mathf.PI);
            float bob = Mathf.Abs(Mathf.Cos(phase)) * Mathf.Lerp(0.03f, 0.055f, sprint) * m * Mathf.Lerp(1f, 0.5f, crouchBlend);
            float breathe = Mathf.Sin(t * 1.7f);
            float shift = Mathf.Sin(t * 0.55f) * idle;
            float crouchDrop = 0.36f * crouchBlend;
            float pelvisYaw = twist + sideBlend * m * 24f + swing * 6f * m;
            Vector3 stagger = new Vector3(hitLocal.x, 0f, hitLocal.z) * (0.05f * fl);
            pelvis.localPosition = new Vector3(shift * 0.012f, HipY - crouchDrop + bob - 0.035f * m + breathe * 0.004f * idle, 0f) + stagger;
            pelvis.localRotation = Quaternion.Euler(5f * m * Mathf.Max(0f, fwdBlend) * (0.5f + sprint), pelvisYaw, lean * 0.45f + shift * 1.5f);

            float deathT = alive ? 0f : Mathf.Clamp01((t - diedAt) / 0.45f);
            float dE = deathT * deathT * (3f - 2f * deathT);

            // Legs: pitch for forward and back, roll for strafing (a cross-step); a shorter, lower stride crouched.
            float stride = Mathf.Lerp(Mathf.Lerp(28f, 40f, sprint), 20f, crouchBlend);
            float pitchAmp = stride * m * Mathf.Clamp(Mathf.Abs(fwdBlend) + 0.3f, 0f, 1f) * (fwdBlend < -0.2f ? -1f : 1f);
            float rollAmp = 16f * m * Mathf.Abs(sideBlend);
            float crouchThigh = -62f * crouchBlend, crouchShin = 105f * crouchBlend;
            float sgn = Mathf.Sign(sideBlend + 1e-4f);
            float tLx = -swing * pitchAmp + crouchThigh - 38f * dE - shift * 2f;
            float tRx = -swing2 * pitchAmp + crouchThigh - 26f * dE + shift * 2f;
            thighL.localRotation = Quaternion.Euler(tLx, 0f, swing * rollAmp * sgn - 3f - 6f * dE - shift * 1.5f);
            thighR.localRotation = Quaternion.Euler(tRx, 0f, swing2 * rollAmp * sgn + 3f + 8f * dE - shift * 1.5f);
            float knee = Mathf.Lerp(48f, 72f, sprint);
            shinL.localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Sin(phase + 0.9f)) * knee * m + crouchShin + 70f * dE + 2f, 0f, 0f);
            shinR.localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Sin(phase + 0.9f + Mathf.PI)) * knee * m + crouchShin + 55f * dE + 2f, 0f, 0f);

            // Torso: leans into a run, counter-rotates against the hips, breathes, kicks back when firing, flinches.
            float kick = Mathf.Clamp01(1f - (t - shotAt) / 0.12f);
            float pitch = Mathf.Lerp(7f, 14f, sprint) * m * Mathf.Max(0f, fwdBlend) + 12f * crouchBlend - kick * 5f + breathe * 1.3f * idle;
            pitch += hitLocal.z * 18f * fl;
            float roll = lean - hitLocal.x * 14f * fl;
            float counter = -swing * 9f * m * (1f - aimBlend * 0.6f);
            chest.localRotation = Quaternion.Euler(pitch - 14f * dE, -pelvisYaw + counter, roll);
            head.localRotation = Quaternion.Euler(-pitch * 0.8f + 12f * fl - 28f * dE, -counter * 0.7f + 18f * dE + Mathf.Sin(t * 0.4f) * 4f * idle, -lean * 0.6f + 14f * dE);

            if (alive)
            {
                float aimSway = Mathf.Sin(phase * 0.5f) * 3f * m + breathe * 0.8f * idle;
                float shrug = breathe * 1.5f * idle;
                Quaternion aimR = Quaternion.Euler(-62f + aimSway + kick * 6f, 0f, -12f - shrug);
                Quaternion aimFR = Quaternion.Euler(-28f, 0f, 0f);
                Quaternion aimL = Quaternion.Euler(-74f + aimSway + kick * 6f, 30f, 18f + shrug);
                Quaternion aimFL = Quaternion.Euler(-34f, 0f, 0f);
                // Low ready: the right hand keeps the grip with the muzzle down, the left arm swings with the stride.
                float armSwing = Mathf.Lerp(30f, 46f, sprint);
                Quaternion lowR = Quaternion.Euler(-48f + swing2 * 6f * m, 0f, -12f);
                Quaternion lowFR = Quaternion.Euler(-40f - Mathf.Max(0f, swing2) * 10f * m, 0f, 0f);
                Quaternion lowL = Quaternion.Euler(swing * armSwing * m - 14f, 0f, 9f);
                Quaternion lowFL = Quaternion.Euler(-24f - Mathf.Max(0f, swing) * 44f * m, 0f, 0f);
                Quaternion qR = Quaternion.Slerp(lowR, aimR, aimBlend);
                Quaternion qFR = Quaternion.Slerp(lowFR, aimFR, aimBlend);
                Quaternion qL = Quaternion.Slerp(lowL, aimL, aimBlend);
                Quaternion qFL = Quaternion.Slerp(lowFL, aimFL, aimBlend);
                Vector3 aimPos = new Vector3(0.1f, 0.3f + Mathf.Sin(phase) * 0.008f * m, 0.3f - kick * 0.05f);
                Vector3 lowPos = new Vector3(0.14f, 0.22f + Mathf.Sin(phase * 2f) * 0.012f * m, 0.27f);
                Vector3 gp = Vector3.Lerp(lowPos, aimPos, aimBlend);
                Quaternion gq = Quaternion.Slerp(Quaternion.Euler(22f, -8f, 0f), Quaternion.Euler(-kick * 8f, 0f, 0f), aimBlend);
                if (reloadBlend > 0.001f)
                {
                    // Reload: the gun rolls in, the support hand drops to the belt for a magazine and brings it up.
                    float rt = Mathf.Repeat(t - reloadAt, 1.8f);
                    float down = Textures.Smooth(0.15f, 0.5f, rt) * (1f - Textures.Smooth(0.75f, 1.1f, rt));
                    float seat = Textures.Smooth(0.95f, 1.25f, rt) * (1f - Textures.Smooth(1.45f, 1.75f, rt));
                    Quaternion relGun = Quaternion.Euler(-18f, -10f, 28f);
                    Vector3 relPos = new Vector3(0.06f, 0.24f, 0.26f);
                    Quaternion pouchL = Quaternion.Euler(8f, 0f, 14f);
                    Quaternion pouchFL = Quaternion.Euler(-35f, 0f, 0f);
                    Quaternion magL = Quaternion.Euler(-48f, 35f, 22f);
                    Quaternion magFL = Quaternion.Euler(-72f, 0f, 0f);
                    Quaternion holdL = Quaternion.Slerp(Quaternion.Slerp(aimL, pouchL, down), magL, seat);
                    Quaternion holdFL = Quaternion.Slerp(Quaternion.Slerp(aimFL, pouchFL, down), magFL, seat);
                    qL = Quaternion.Slerp(qL, holdL, reloadBlend);
                    qFL = Quaternion.Slerp(qFL, holdFL, reloadBlend);
                    qR = Quaternion.Slerp(qR, Quaternion.Euler(-50f, 0f, -16f), reloadBlend);
                    qFR = Quaternion.Slerp(qFR, Quaternion.Euler(-48f, 0f, 0f), reloadBlend);
                    gp = Vector3.Lerp(gp, relPos + new Vector3(0f, seat * 0.015f, 0f), reloadBlend);
                    gq = Quaternion.Slerp(gq, relGun * Quaternion.Euler(-seat * 6f, 0f, 0f), reloadBlend);
                }
                armR.localRotation = qR;
                foreR.localRotation = qFR;
                armL.localRotation = qL;
                foreL.localRotation = qFL;
                gunMount.localPosition = gp;
                gunMount.localRotation = gq;
            }
            else
            {
                // Collapse: the arms fling up and out, then go limp; the gun drops away.
                float ks = 1f - Mathf.Exp(-dt * 9f);
                armR.localRotation = Quaternion.Slerp(armR.localRotation, Quaternion.Euler(-130f * dE + 10f, 0f, -45f), ks);
                armL.localRotation = Quaternion.Slerp(armL.localRotation, Quaternion.Euler(-110f * dE + 10f, 0f, 50f), ks);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-20f, 0f, 0f), ks);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-35f, 0f, 0f), ks);
                gunMount.localPosition = Vector3.Lerp(gunMount.localPosition, new Vector3(0.3f, -0.3f, 0.4f), ks);
                gunMount.localRotation = Quaternion.Slerp(gunMount.localRotation, Quaternion.Euler(60f, 20f, 70f), ks);
            }

            // Secondary motion: coat panels ride the thighs, the scarf and cape trail with speed and flutter.
            float trail = Mathf.Clamp01(speed / 5.4f);
            for (int i = 0; i < panels.Count; i++)
            {
                Panel p = panels[i];
                if (p.T == null) continue;
                float tx = p.Left ? tLx : tRx;
                float x = p.Front ? Mathf.Min(0f, tx) * 0.92f - 2f : Mathf.Max(0f, tx) * 0.9f + 3f + trail * 12f;
                if (!p.Front) x += Mathf.Sin(t * 9f + i) * 2.5f * trail;
                float z = (p.Left ? -1f : 1f) * (2f + rollAmp * 0.3f);
                p.T.localRotation = Quaternion.Slerp(p.T.localRotation, Quaternion.Euler(x - 20f * dE * (p.Front ? 1f : -0.5f), 0f, z), 1f - Mathf.Exp(-dt * 18f));
            }
            for (int i = 0; i < scarf.Count; i++)
            {
                Transform sc = scarf[i];
                if (sc == null) continue;
                float lift = (i == 0 ? 18f : 10f) * trail + Mathf.Sin(t * (6f + 4f * trail) - i * 1.3f) * (2f + 10f * trail);
                float side = Mathf.Sin(t * 1.3f + i) * 5f + sideBlend * m * 14f;
                // At rest the first segment clears the back item and the others hang back down.
                float rest = i == 0 ? 28f : i == 1 ? -18f : -8f;
                sc.localRotation = Quaternion.Slerp(sc.localRotation, Quaternion.Euler(rest + lift + (i == 0 ? pitch * -0.5f : 0f), 0f, side), 1f - Mathf.Exp(-dt * 10f));
            }
            if (cape != null)
            {
                float cx = 6f + trail * 22f + Mathf.Sin(t * 7f) * 3f * trail + breathe * 0.8f * idle - pitch * 0.4f;
                cape.localRotation = Quaternion.Slerp(cape.localRotation, Quaternion.Euler(cx, 0f, sideBlend * m * 10f), 1f - Mathf.Exp(-dt * 8f));
            }
        }

        /// <summary>Metres travelled per gait cycle (two footfalls) at `speed`; AbilityFx spaces footstep sounds with it.</summary>
        public static float CycleLength(float speed, float crouch)
        {
            return Mathf.Lerp(1.4f, 3.0f, Mathf.Clamp01(speed / 5.4f)) * Mathf.Lerp(1f, 0.75f, Mathf.Clamp01(crouch));
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

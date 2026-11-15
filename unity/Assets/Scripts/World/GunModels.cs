// Original procedural weapon models. Each weapon is a list of parts (chamfered
// boxes and cylinders) in gun space: +z towards the muzzle, +y up, origin at
// the top of the grip, metres. The same list builds the merged 3D meshes (body,
// magazine and slide/bolt/pump groups for the viewmodel's animations, or one
// merged mesh for a bot's hands) and the side-view icon used by the kill feed,
// the buy menu and the HUD, so the silhouettes always agree.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    public enum GunMat
    {
        Metal,
        Polymer,
        Paint,
        Glow,
        Dark,
        Brass,
        Light,
    }

    public struct GunPart
    {
        public bool Cyl;
        public int Sides;
        public Vector3 Pos;
        /// <summary>Box: full size. Cylinder: x = radius, z = length.</summary>
        public Vector3 Size;
        public Vector3 Euler;
        public GunMat Mat;
        public float Bevel;
        /// <summary>0 body, 1 magazine (drops on reload), 2 slide / pump / bolt (cycles on fire).</summary>
        public int Group;
    }

    public class GunDef
    {
        public string Id;
        public readonly List<GunPart> Parts = new List<GunPart>();
        public Color Paint;
        public Color GlowColor;
        public Vector3 Muzzle;
        public Vector3 Eject;
        public Vector3 Grip;
        public Vector3 Support;
        /// <summary>Viewmodel position of the gun's origin in the weapon camera's space.</summary>
        public Vector3 Hold;
        public float FlashSize = 0.1f;
        public float FlashLength = 1f;
        public Color FlashColor = new Color(1f, 0.8f, 0.45f);
        /// <summary>How far the slide/bolt/pump group travels when it cycles (metres, along -z).</summary>
        public float CycleTravel = 0.02f;
        /// <summary>The group cycles after each shot this long (s); 0 = with the shot (a pistol slide).</summary>
        public float CycleDelay;
        public float CycleS = 0.08f;
        public float KickBack = 0.03f;
        public float KickPitch = 4f;

        public GunDef Box(float x, float y, float z, float sx, float sy, float sz, GunMat m, float bevel = 0.004f, float rx = 0f, int group = 0, float ry = 0f)
        {
            Parts.Add(new GunPart { Pos = new Vector3(x, y, z), Size = new Vector3(sx, sy, sz), Euler = new Vector3(rx, ry, 0f), Mat = m, Bevel = bevel, Group = group });
            return this;
        }

        public GunDef Cyl(float x, float y, float z, float r, float len, GunMat m, int sides = 12, int group = 0, float rx = 0f, float ry = 0f)
        {
            Parts.Add(new GunPart { Cyl = true, Sides = sides, Pos = new Vector3(x, y, z), Size = new Vector3(r, r, len), Euler = new Vector3(rx, ry, 0f), Mat = m, Group = group });
            return this;
        }
    }

    public class GunModel
    {
        public string Id;
        public GunDef Def;
        public GameObject Root;
        public Transform Body;
        public Transform Mag;
        public Transform Slide;
        public Vector3 MagHome;
        public Vector3 SlideHome;
        public Transform MuzzlePoint;
        public Transform EjectPoint;
    }

    public static class GunModels
    {
        static readonly Dictionary<string, GunDef> defs = new Dictionary<string, GunDef>();
        static readonly Dictionary<string, Mesh[]> splitMeshes = new Dictionary<string, Mesh[]>();
        static readonly Dictionary<string, Vector3[]> splitPivots = new Dictionary<string, Vector3[]>();
        static readonly Dictionary<string, Mesh> mergedMeshes = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Material> glowMats = new Dictionary<string, Material>();
        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        static Material gunMat;

        public static Material GunMaterial
        {
            get
            {
                if (gunMat == null) gunMat = Mats.World(Color.white, Textures.Grain(), null, 0f, 0.3f, 0.85f, 0.72f, 1f);
                return gunMat;
            }
        }

        public static Material GlowMaterial(GunDef d)
        {
            if (glowMats.TryGetValue(d.Id, out Material m) && m != null) return m;
            m = Mats.Glow(d.GlowColor, 1.3f);
            glowMats[d.Id] = m;
            return m;
        }

        // ---- the seven designs ----------------------------------------------------------------------
        public static GunDef Def(string id)
        {
            if (id != null && defs.TryGetValue(id, out GunDef d)) return d;
            d = Make(id);
            defs[d.Id] = d;
            if (id != null && id != d.Id) defs[id] = d;
            return d;
        }

        static GunDef Make(string id)
        {
            GunDef d;
            switch (id)
            {
                case "pin":
                    // A compact polymer sidearm: angular slide with rear serrations, sand grip panels, green front dot.
                    d = new GunDef { Id = "pin", Paint = new Color(0.66f, 0.58f, 0.45f), GlowColor = new Color(0.35f, 1f, 0.45f) };
                    d.Box(0f, -0.004f, 0.03f, 0.032f, 0.03f, 0.17f, GunMat.Polymer, 0.006f)
                     .Box(0f, 0.027f, 0.035f, 0.03f, 0.034f, 0.18f, GunMat.Metal, 0.005f, 0f, 2)
                     .Box(0f, 0.03f, -0.035f, 0.031f, 0.022f, 0.004f, GunMat.Dark, 0f, 0f, 2)
                     .Box(0f, 0.03f, -0.045f, 0.031f, 0.022f, 0.004f, GunMat.Dark, 0f, 0f, 2)
                     .Box(0f, 0.03f, -0.025f, 0.031f, 0.022f, 0.004f, GunMat.Dark, 0f, 0f, 2)
                     .Box(0f, 0.032f, 0.07f, 0.012f, 0.012f, 0.03f, GunMat.Dark, 0.002f, 0f, 2)
                     .Cyl(0f, 0.027f, 0.128f, 0.0075f, 0.012f, GunMat.Dark, 10)
                     .Box(0f, -0.058f, -0.03f, 0.03f, 0.095f, 0.046f, GunMat.Polymer, 0.008f, 14f)
                     .Box(0f, -0.058f, -0.03f, 0.0335f, 0.06f, 0.03f, GunMat.Paint, 0.004f, 14f)
                     .Box(0f, -0.03f, 0.035f, 0.012f, 0.007f, 0.05f, GunMat.Polymer, 0.002f)
                     .Box(0f, -0.022f, 0.06f, 0.012f, 0.02f, 0.008f, GunMat.Polymer, 0.002f)
                     .Box(0f, -0.07f, -0.027f, 0.025f, 0.09f, 0.034f, GunMat.Dark, 0.002f, 14f, 1)
                     .Box(0f, -0.113f, -0.039f, 0.032f, 0.012f, 0.044f, GunMat.Paint, 0.003f, 14f, 1)
                     .Box(0f, 0.048f, -0.045f, 0.024f, 0.01f, 0.008f, GunMat.Dark, 0.001f, 0f, 2)
                     .Box(0f, 0.048f, 0.115f, 0.006f, 0.01f, 0.006f, GunMat.Glow, 0.001f, 0f, 2)
                     .Box(0.0165f, -0.003f, 0.065f, 0.002f, 0.005f, 0.07f, GunMat.Paint, 0f);
                    d.Muzzle = new Vector3(0f, 0.027f, 0.14f);
                    d.Eject = new Vector3(0.02f, 0.035f, 0.03f);
                    d.Grip = new Vector3(0f, -0.05f, -0.03f);
                    d.Support = new Vector3(-0.012f, -0.075f, -0.01f);
                    d.Hold = new Vector3(0.105f, -0.105f, 0.27f);
                    d.FlashSize = 0.07f; d.FlashLength = 0.9f; d.FlashColor = new Color(1f, 0.85f, 0.5f);
                    d.CycleTravel = 0.028f; d.CycleS = 0.07f; d.KickBack = 0.025f; d.KickPitch = 7f;
                    return d;

                case "hammer":
                    // A heavy six-shot hand cannon: hexagonal drum, square barrel with an oxblood under-lug, vented top.
                    d = new GunDef { Id = "hammer", Paint = new Color(0.42f, 0.14f, 0.12f), GlowColor = new Color(1f, 0.55f, 0.15f) };
                    d.Box(0f, 0.012f, 0.02f, 0.034f, 0.05f, 0.1f, GunMat.Metal, 0.006f)
                     .Cyl(0f, 0.01f, 0.025f, 0.027f, 0.06f, GunMat.Metal, 6, 1)
                     .Cyl(0f, 0.01f, 0.025f, 0.009f, 0.062f, GunMat.Dark, 8, 1)
                     .Box(0f, 0.03f, 0.13f, 0.026f, 0.026f, 0.16f, GunMat.Metal, 0.004f)
                     .Box(0f, 0.007f, 0.12f, 0.022f, 0.018f, 0.14f, GunMat.Paint, 0.004f)
                     .Box(0f, 0.044f, 0.09f, 0.02f, 0.004f, 0.012f, GunMat.Dark, 0f)
                     .Box(0f, 0.044f, 0.12f, 0.02f, 0.004f, 0.012f, GunMat.Dark, 0f)
                     .Box(0f, 0.044f, 0.15f, 0.02f, 0.004f, 0.012f, GunMat.Dark, 0f)
                     .Cyl(0f, 0.03f, 0.213f, 0.01f, 0.008f, GunMat.Dark, 10)
                     .Box(0f, -0.052f, -0.045f, 0.033f, 0.095f, 0.048f, GunMat.Polymer, 0.012f, 24f)
                     .Box(0f, -0.052f, -0.045f, 0.0355f, 0.065f, 0.032f, GunMat.Paint, 0.006f, 24f)
                     .Box(0f, 0.042f, -0.035f, 0.012f, 0.02f, 0.022f, GunMat.Dark, 0.003f, -30f, 2)
                     .Box(0f, -0.024f, 0.02f, 0.012f, 0.008f, 0.045f, GunMat.Metal, 0.002f)
                     .Box(0f, 0.047f, -0.02f, 0.02f, 0.008f, 0.01f, GunMat.Dark, 0.001f)
                     .Box(0f, 0.048f, 0.2f, 0.005f, 0.012f, 0.012f, GunMat.Glow, 0.001f);
                    d.Muzzle = new Vector3(0f, 0.03f, 0.22f);
                    d.Eject = new Vector3(0.03f, 0.01f, 0.025f);
                    d.Grip = new Vector3(0f, -0.05f, -0.045f);
                    d.Support = new Vector3(-0.012f, -0.08f, -0.03f);
                    d.Hold = new Vector3(0.105f, -0.105f, 0.26f);
                    d.FlashSize = 0.12f; d.FlashLength = 1.1f; d.FlashColor = new Color(1f, 0.7f, 0.35f);
                    d.CycleTravel = 0.012f; d.CycleS = 0.12f; d.KickBack = 0.05f; d.KickPitch = 14f;
                    return d;

                case "wasp":
                    // A stubby boxy SMG: amber top cover, ported shroud, vertical foregrip, wire stock, red-dot sight.
                    d = new GunDef { Id = "wasp", Paint = new Color(0.64f, 0.46f, 0.2f), GlowColor = new Color(1f, 0.25f, 0.2f) };
                    d.Box(0f, 0.02f, 0.06f, 0.045f, 0.07f, 0.26f, GunMat.Polymer, 0.01f)
                     .Box(0f, 0.058f, 0.05f, 0.042f, 0.012f, 0.22f, GunMat.Paint, 0.004f)
                     .Box(0f, 0.022f, 0.235f, 0.036f, 0.04f, 0.1f, GunMat.Metal, 0.006f)
                     .Box(0f, 0.022f, 0.21f, 0.0375f, 0.012f, 0.016f, GunMat.Dark, 0f)
                     .Box(0f, 0.022f, 0.235f, 0.0375f, 0.012f, 0.016f, GunMat.Dark, 0f)
                     .Box(0f, 0.022f, 0.26f, 0.0375f, 0.012f, 0.016f, GunMat.Dark, 0f)
                     .Cyl(0f, 0.022f, 0.3f, 0.009f, 0.03f, GunMat.Dark, 10)
                     .Box(0f, -0.05f, -0.025f, 0.034f, 0.09f, 0.042f, GunMat.Polymer, 0.008f, 12f)
                     .Box(0f, -0.075f, 0.085f, 0.026f, 0.12f, 0.038f, GunMat.Dark, 0.003f, -6f, 1)
                     .Box(0f, -0.04f, 0.19f, 0.028f, 0.07f, 0.03f, GunMat.Polymer, 0.006f)
                     .Box(0.016f, 0.01f, -0.14f, 0.008f, 0.008f, 0.18f, GunMat.Metal, 0.002f)
                     .Box(-0.016f, 0.01f, -0.14f, 0.008f, 0.008f, 0.18f, GunMat.Metal, 0.002f)
                     .Box(0f, -0.005f, -0.235f, 0.042f, 0.065f, 0.014f, GunMat.Polymer, 0.004f)
                     .Box(0f, 0.076f, 0.02f, 0.03f, 0.022f, 0.04f, GunMat.Dark, 0.004f)
                     .Box(0f, 0.079f, 0.041f, 0.022f, 0.014f, 0.003f, GunMat.Glow, 0f)
                     .Box(0.024f, 0.03f, 0.03f, 0.012f, 0.012f, 0.02f, GunMat.Dark, 0.002f, 0f, 2)
                     .Box(0.0235f, 0.008f, 0.1f, 0.002f, 0.005f, 0.09f, GunMat.Paint, 0f);
                    d.Muzzle = new Vector3(0f, 0.022f, 0.32f);
                    d.Eject = new Vector3(0.025f, 0.035f, 0.06f);
                    d.Grip = new Vector3(0f, -0.05f, -0.025f);
                    d.Support = new Vector3(0f, -0.05f, 0.19f);
                    d.Hold = new Vector3(0.115f, -0.115f, 0.24f);
                    d.FlashSize = 0.075f; d.FlashLength = 1.2f; d.FlashColor = new Color(1f, 0.82f, 0.45f);
                    d.CycleTravel = 0.02f; d.CycleS = 0.05f; d.KickBack = 0.016f; d.KickPitch = 2.5f;
                    return d;

                case "brick":
                    // A chunky pump shotgun: square olive heat shield over the barrel, tube magazine, ribbed pump, side shell carrier.
                    d = new GunDef { Id = "brick", Paint = new Color(0.34f, 0.37f, 0.24f), GlowColor = new Color(1f, 0.85f, 0.2f) };
                    d.Box(0f, 0.015f, 0.03f, 0.05f, 0.07f, 0.22f, GunMat.Metal, 0.01f)
                     .Cyl(0f, 0.036f, 0.33f, 0.016f, 0.42f, GunMat.Dark, 12)
                     .Box(0f, 0.052f, 0.31f, 0.042f, 0.02f, 0.36f, GunMat.Paint, 0.005f)
                     .Box(0f, 0.063f, 0.24f, 0.03f, 0.002f, 0.03f, GunMat.Dark, 0f)
                     .Box(0f, 0.063f, 0.31f, 0.03f, 0.002f, 0.03f, GunMat.Dark, 0f)
                     .Box(0f, 0.063f, 0.38f, 0.03f, 0.002f, 0.03f, GunMat.Dark, 0f)
                     .Cyl(0f, 0.0f, 0.3f, 0.013f, 0.34f, GunMat.Metal, 12)
                     .Box(0f, 0.0f, 0.27f, 0.052f, 0.042f, 0.13f, GunMat.Polymer, 0.01f, 0f, 2)
                     .Box(0f, -0.022f, 0.24f, 0.054f, 0.006f, 0.008f, GunMat.Dark, 0f, 0f, 2)
                     .Box(0f, -0.022f, 0.27f, 0.054f, 0.006f, 0.008f, GunMat.Dark, 0f, 0f, 2)
                     .Box(0f, -0.022f, 0.30f, 0.054f, 0.006f, 0.008f, GunMat.Dark, 0f, 0f, 2)
                     .Box(0f, -0.05f, -0.065f, 0.036f, 0.09f, 0.046f, GunMat.Polymer, 0.01f, 20f)
                     .Box(0f, -0.004f, -0.22f, 0.045f, 0.07f, 0.26f, GunMat.Polymer, 0.012f, -4f)
                     .Box(0f, -0.016f, -0.36f, 0.05f, 0.105f, 0.022f, GunMat.Dark, 0.006f, -4f)
                     .Box(0.027f, 0.018f, 0.0f, 0.004f, 0.04f, 0.09f, GunMat.Polymer, 0.002f)
                     .Cyl(0.03f, 0.03f, -0.02f, 0.006f, 0.022f, GunMat.Brass, 8, 0, 90f)
                     .Cyl(0.03f, 0.03f, 0.0f, 0.006f, 0.022f, GunMat.Brass, 8, 0, 90f)
                     .Cyl(0.03f, 0.03f, 0.02f, 0.006f, 0.022f, GunMat.Brass, 8, 0, 90f)
                     .Cyl(0f, -0.03f, 0.03f, 0.007f, 0.03f, GunMat.Brass, 8, 1)
                     .Box(0f, 0.064f, 0.48f, 0.006f, 0.008f, 0.006f, GunMat.Glow, 0.001f);
                    d.Muzzle = new Vector3(0f, 0.036f, 0.545f);
                    d.Eject = new Vector3(0.03f, 0.03f, 0.05f);
                    d.Grip = new Vector3(0f, -0.05f, -0.065f);
                    d.Support = new Vector3(0f, -0.02f, 0.27f);
                    d.Hold = new Vector3(0.12f, -0.12f, 0.23f);
                    d.FlashSize = 0.18f; d.FlashLength = 0.8f; d.FlashColor = new Color(1f, 0.62f, 0.25f);
                    d.CycleTravel = 0.07f; d.CycleDelay = 0.22f; d.CycleS = 0.32f; d.KickBack = 0.07f; d.KickPitch = 12f;
                    return d;

                case "viper":
                    // A sleek teal bullpup: one long rounded shell, magazine behind the grip, top rail, green holo window.
                    d = new GunDef { Id = "viper", Paint = new Color(0.12f, 0.36f, 0.34f), GlowColor = new Color(0.3f, 1f, 0.75f) };
                    d.Box(0f, 0.02f, -0.02f, 0.05f, 0.09f, 0.46f, GunMat.Paint, 0.02f)
                     .Box(0f, 0.012f, 0.255f, 0.044f, 0.062f, 0.12f, GunMat.Polymer, 0.015f)
                     .Cyl(0f, 0.025f, 0.35f, 0.009f, 0.1f, GunMat.Dark, 10)
                     .Cyl(0f, 0.025f, 0.41f, 0.0125f, 0.035f, GunMat.Metal, 6)
                     .Box(0f, 0.078f, 0.02f, 0.02f, 0.016f, 0.3f, GunMat.Metal, 0.003f)
                     .Box(0f, 0.07f, -0.1f, 0.016f, 0.02f, 0.02f, GunMat.Metal, 0.003f)
                     .Box(0f, 0.07f, 0.14f, 0.016f, 0.02f, 0.02f, GunMat.Metal, 0.003f)
                     .Box(0f, -0.06f, 0.07f, 0.032f, 0.085f, 0.04f, GunMat.Polymer, 0.008f, 14f)
                     .Box(0f, -0.04f, 0.13f, 0.03f, 0.02f, 0.07f, GunMat.Polymer, 0.006f)
                     .Box(0f, -0.065f, -0.085f, 0.028f, 0.105f, 0.06f, GunMat.Dark, 0.004f, -8f, 1)
                     .Box(0f, 0.0f, -0.265f, 0.052f, 0.115f, 0.04f, GunMat.Polymer, 0.012f)
                     .Box(0f, 0.105f, 0.07f, 0.03f, 0.03f, 0.05f, GunMat.Dark, 0.004f)
                     .Box(0f, 0.107f, 0.096f, 0.024f, 0.022f, 0.003f, GunMat.Glow, 0f)
                     .Box(0.0255f, 0.03f, 0.0f, 0.002f, 0.006f, 0.2f, GunMat.Light, 0f)
                     .Box(-0.0255f, 0.03f, 0.0f, 0.002f, 0.006f, 0.2f, GunMat.Light, 0f)
                     .Box(0.027f, 0.04f, -0.12f, 0.01f, 0.012f, 0.025f, GunMat.Dark, 0.002f, 0f, 2);
                    d.Muzzle = new Vector3(0f, 0.025f, 0.43f);
                    d.Eject = new Vector3(0.03f, 0.04f, -0.12f);
                    d.Grip = new Vector3(0f, -0.055f, 0.07f);
                    d.Support = new Vector3(0f, -0.02f, 0.25f);
                    d.Hold = new Vector3(0.12f, -0.12f, 0.2f);
                    d.FlashSize = 0.08f; d.FlashLength = 1.3f; d.FlashColor = new Color(1f, 0.9f, 0.6f);
                    d.CycleTravel = 0.02f; d.CycleS = 0.05f; d.KickBack = 0.018f; d.KickPitch = 2.5f;
                    return d;

                case "longshot":
                    // A long bolt-action: fluted heavy barrel, big ported brake, large scope, skeleton stock, folded bipod.
                    d = new GunDef { Id = "longshot", Paint = new Color(0.56f, 0.32f, 0.17f), GlowColor = new Color(1f, 0.5f, 0.15f) };
                    d.Box(0f, 0.02f, 0f, 0.045f, 0.05f, 0.26f, GunMat.Metal, 0.006f)
                     .Box(0f, 0.004f, 0.25f, 0.052f, 0.05f, 0.3f, GunMat.Polymer, 0.012f)
                     .Box(0.0265f, 0.004f, 0.25f, 0.003f, 0.014f, 0.24f, GunMat.Paint, 0f)
                     .Box(-0.0265f, 0.004f, 0.25f, 0.003f, 0.014f, 0.24f, GunMat.Paint, 0f)
                     .Cyl(0f, 0.025f, 0.6f, 0.012f, 0.42f, GunMat.Dark, 8)
                     .Box(0f, 0.025f, 0.83f, 0.034f, 0.03f, 0.065f, GunMat.Metal, 0.005f)
                     .Box(0f, 0.025f, 0.82f, 0.036f, 0.008f, 0.012f, GunMat.Dark, 0f)
                     .Box(0f, 0.025f, 0.845f, 0.036f, 0.008f, 0.012f, GunMat.Dark, 0f)
                     .Cyl(0f, 0.088f, 0.03f, 0.021f, 0.26f, GunMat.Dark, 14)
                     .Cyl(0f, 0.088f, 0.19f, 0.03f, 0.07f, GunMat.Dark, 14)
                     .Cyl(0f, 0.088f, -0.125f, 0.026f, 0.05f, GunMat.Polymer, 14)
                     .Box(0f, 0.115f, 0.03f, 0.022f, 0.016f, 0.022f, GunMat.Paint, 0.003f)
                     .Box(0.026f, 0.088f, 0.03f, 0.016f, 0.022f, 0.022f, GunMat.Metal, 0.003f)
                     .Box(0f, 0.058f, -0.05f, 0.02f, 0.022f, 0.02f, GunMat.Metal, 0.003f)
                     .Box(0f, 0.058f, 0.1f, 0.02f, 0.022f, 0.02f, GunMat.Metal, 0.003f)
                     .Cyl(0f, 0.088f, 0.2265f, 0.026f, 0.002f, GunMat.Glow, 14)
                     .Box(0f, -0.04f, 0.03f, 0.034f, 0.06f, 0.08f, GunMat.Dark, 0.004f, 0f, 1)
                     .Box(0f, -0.052f, -0.12f, 0.034f, 0.09f, 0.042f, GunMat.Polymer, 0.01f, 20f)
                     .Box(0.045f, 0.03f, -0.07f, 0.05f, 0.012f, 0.012f, GunMat.Metal, 0.003f, 0f, 2)
                     .Box(0.072f, 0.026f, -0.07f, 0.018f, 0.018f, 0.018f, GunMat.Dark, 0.006f, 0f, 2)
                     .Box(0f, 0.02f, -0.3f, 0.035f, 0.03f, 0.28f, GunMat.Polymer, 0.008f)
                     .Box(0f, -0.055f, -0.32f, 0.03f, 0.025f, 0.24f, GunMat.Polymer, 0.006f, 8f)
                     .Box(0f, -0.02f, -0.455f, 0.04f, 0.14f, 0.03f, GunMat.Polymer, 0.008f)
                     .Box(0f, 0.043f, -0.32f, 0.03f, 0.018f, 0.14f, GunMat.Paint, 0.006f)
                     .Box(0.018f, -0.028f, 0.37f, 0.008f, 0.008f, 0.2f, GunMat.Metal, 0.002f)
                     .Box(-0.018f, -0.028f, 0.37f, 0.008f, 0.008f, 0.2f, GunMat.Metal, 0.002f);
                    d.Muzzle = new Vector3(0f, 0.025f, 0.87f);
                    d.Eject = new Vector3(0.03f, 0.04f, -0.02f);
                    d.Grip = new Vector3(0f, -0.05f, -0.12f);
                    d.Support = new Vector3(0f, -0.03f, 0.24f);
                    d.Hold = new Vector3(0.125f, -0.125f, 0.25f);
                    d.FlashSize = 0.2f; d.FlashLength = 1.6f; d.FlashColor = new Color(1f, 0.75f, 0.4f);
                    d.CycleTravel = 0.06f; d.CycleDelay = 0.35f; d.CycleS = 0.45f; d.KickBack = 0.09f; d.KickPitch = 16f;
                    return d;

                default:
                    // The Lancer: angular upper with a slate lower, slotted handguard, forward-leaning magazine, cyan optic.
                    d = new GunDef { Id = "lancer", Paint = new Color(0.26f, 0.32f, 0.42f), GlowColor = new Color(0.25f, 0.9f, 1f) };
                    d.Box(0f, 0.03f, 0.05f, 0.04f, 0.045f, 0.3f, GunMat.Metal, 0.006f)
                     .Box(0f, -0.005f, 0.03f, 0.038f, 0.035f, 0.2f, GunMat.Paint, 0.006f)
                     .Box(0f, 0.025f, 0.3f, 0.046f, 0.055f, 0.22f, GunMat.Polymer, 0.012f)
                     .Box(0f, 0.025f, 0.25f, 0.0475f, 0.014f, 0.03f, GunMat.Dark, 0f)
                     .Box(0f, 0.025f, 0.3f, 0.0475f, 0.014f, 0.03f, GunMat.Dark, 0f)
                     .Box(0f, 0.025f, 0.35f, 0.0475f, 0.014f, 0.03f, GunMat.Dark, 0f)
                     .Cyl(0f, 0.03f, 0.47f, 0.009f, 0.14f, GunMat.Dark, 10)
                     .Box(0f, 0.03f, 0.55f, 0.022f, 0.022f, 0.04f, GunMat.Metal, 0.004f)
                     .Box(0f, -0.075f, 0.09f, 0.028f, 0.14f, 0.055f, GunMat.Dark, 0.004f, -12f, 1)
                     .Box(0f, -0.148f, 0.118f, 0.027f, 0.05f, 0.05f, GunMat.Dark, 0.004f, -24f, 1)
                     .Box(0f, -0.055f, -0.03f, 0.032f, 0.085f, 0.04f, GunMat.Polymer, 0.008f, 18f)
                     .Box(0f, 0.0f, -0.22f, 0.04f, 0.06f, 0.24f, GunMat.Polymer, 0.01f)
                     .Box(0f, -0.02f, -0.34f, 0.042f, 0.12f, 0.03f, GunMat.Polymer, 0.008f)
                     .Box(0f, 0.04f, -0.2f, 0.03f, 0.02f, 0.14f, GunMat.Paint, 0.006f)
                     .Box(0f, 0.075f, 0.06f, 0.032f, 0.035f, 0.09f, GunMat.Dark, 0.006f)
                     .Box(0f, 0.077f, 0.106f, 0.024f, 0.022f, 0.003f, GunMat.Glow, 0f)
                     .Box(0.021f, 0.03f, 0.08f, 0.002f, 0.006f, 0.16f, GunMat.Paint, 0f)
                     .Box(0.025f, 0.035f, -0.05f, 0.012f, 0.012f, 0.02f, GunMat.Dark, 0.002f, 0f, 2);
                    d.Muzzle = new Vector3(0f, 0.03f, 0.575f);
                    d.Eject = new Vector3(0.025f, 0.035f, 0.02f);
                    d.Grip = new Vector3(0f, -0.05f, -0.03f);
                    d.Support = new Vector3(0f, -0.005f, 0.3f);
                    d.Hold = new Vector3(0.12f, -0.12f, 0.22f);
                    d.FlashSize = 0.095f; d.FlashLength = 1.4f; d.FlashColor = new Color(1f, 0.8f, 0.45f);
                    d.CycleTravel = 0.025f; d.CycleS = 0.06f; d.KickBack = 0.022f; d.KickPitch = 3.5f;
                    return d;
            }
        }

        public static Color MatColor(GunDef d, GunMat m)
        {
            switch (m)
            {
                case GunMat.Metal: return new Color(0.24f, 0.25f, 0.27f, 1f);
                case GunMat.Polymer: return new Color(0.135f, 0.14f, 0.145f, 0f);
                case GunMat.Paint: return new Color(d.Paint.r, d.Paint.g, d.Paint.b, 0f);
                case GunMat.Dark: return new Color(0.075f, 0.08f, 0.085f, 1f);
                case GunMat.Brass: return new Color(0.82f, 0.62f, 0.28f, 1f);
                case GunMat.Light: return new Color(0.62f, 0.64f, 0.68f, 1f);
                default: return Color.white;
            }
        }

        static void AddPart(MeshKit k, GunDef d, GunPart p, Vector3 pivot)
        {
            int sub = p.Mat == GunMat.Glow ? 1 : 0;
            Color c = MatColor(d, p.Mat);
            Matrix4x4 m = MeshKit.At(p.Pos - pivot, p.Euler);
            if (p.Cyl) k.Cylinder(sub, m, p.Size.x, p.Size.z, p.Sides, c, p.Sides <= 8);
            else k.Box(sub, m, p.Size, p.Bevel, c);
        }

        static void BuildMeshes(GunDef d)
        {
            if (splitMeshes.ContainsKey(d.Id)) return;
            var pivots = new Vector3[3];
            var counts = new int[3];
            foreach (GunPart p in d.Parts)
            {
                pivots[p.Group] += p.Pos;
                counts[p.Group]++;
            }
            pivots[0] = Vector3.zero;
            for (int g = 1; g < 3; g++) pivots[g] = counts[g] > 0 ? pivots[g] / counts[g] : Vector3.zero;
            var kits = new MeshKit[3];
            var all = new MeshKit(2) { UvScale = 6f };
            for (int g = 0; g < 3; g++) kits[g] = new MeshKit(2) { UvScale = 6f };
            foreach (GunPart p in d.Parts)
            {
                AddPart(kits[p.Group], d, p, pivots[p.Group]);
                AddPart(all, d, p, Vector3.zero);
            }
            var meshes = new Mesh[3];
            for (int g = 0; g < 3; g++) meshes[g] = kits[g].Empty ? null : kits[g].ToMesh(d.Id + " group " + g);
            splitMeshes[d.Id] = meshes;
            splitPivots[d.Id] = pivots;
            mergedMeshes[d.Id] = all.ToMesh(d.Id + " merged");
        }

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, GunDef d, int layer, bool shadows)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { GunMaterial, GlowMaterial(d) };
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = shadows;
            mr.lightProbeUsage = LightProbeUsage.Off;
            return go;
        }

        /// <summary>
        /// Builds a model under `parent`. Split: body, magazine and slide as separate transforms (the viewmodel);
        /// otherwise one merged mesh (a bot's weapon).
        /// </summary>
        public static GunModel Build(string id, Transform parent, int layer, bool split, bool shadows)
        {
            GunDef d = Def(id);
            BuildMeshes(d);
            var gm = new GunModel { Id = d.Id, Def = d };
            gm.Root = new GameObject("Gun " + d.Id) { layer = layer };
            gm.Root.transform.SetParent(parent, false);
            if (split)
            {
                Mesh[] meshes = splitMeshes[d.Id];
                Vector3[] piv = splitPivots[d.Id];
                gm.Body = MeshObject("Body", gm.Root.transform, meshes[0], d, layer, shadows).transform;
                if (meshes[1] != null)
                {
                    gm.Mag = MeshObject("Magazine", gm.Root.transform, meshes[1], d, layer, shadows).transform;
                    gm.Mag.localPosition = piv[1];
                    gm.MagHome = piv[1];
                }
                if (meshes[2] != null)
                {
                    gm.Slide = MeshObject("Slide", gm.Root.transform, meshes[2], d, layer, shadows).transform;
                    gm.Slide.localPosition = piv[2];
                    gm.SlideHome = piv[2];
                }
            }
            else
            {
                gm.Body = MeshObject("Body", gm.Root.transform, mergedMeshes[d.Id], d, layer, shadows).transform;
            }
            gm.MuzzlePoint = new GameObject("Muzzle").transform;
            gm.MuzzlePoint.SetParent(gm.Root.transform, false);
            gm.MuzzlePoint.localPosition = d.Muzzle;
            gm.EjectPoint = new GameObject("Eject").transform;
            gm.EjectPoint.SetParent(gm.Root.transform, false);
            gm.EjectPoint.localPosition = d.Eject;
            return gm;
        }

        // ---- icons --------------------------------------------------------------------------------
        public const int IconW = 160, IconH = 56;

        /// <summary>The weapon's side silhouette, white on transparent (tint with GUI.color), muzzle to the right.</summary>
        public static Texture2D Icon(string id)
        {
            GunDef d = Def(id ?? Weapons.Default);
            if (icons.TryGetValue(d.Id, out Texture2D t) && t != null) return t;
            float minZ = float.MaxValue, maxZ = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (GunPart p in d.Parts)
            {
                float hz = p.Cyl ? p.Size.z * 0.5f : p.Size.z * 0.5f;
                float hy = p.Cyl ? p.Size.x : p.Size.y * 0.5f;
                float r = Mathf.Sqrt(hz * hz + hy * hy);
                bool rotated = Mathf.Abs(p.Euler.x) > 0.5f;
                float ez = rotated ? r : hz, ey = rotated ? r : hy;
                minZ = Mathf.Min(minZ, p.Pos.z - ez); maxZ = Mathf.Max(maxZ, p.Pos.z + ez);
                minY = Mathf.Min(minY, p.Pos.y - ey); maxY = Mathf.Max(maxY, p.Pos.y + ey);
            }
            const float pad = 3f;
            float sx = (IconW - 2f * pad) / Mathf.Max(1e-3f, maxZ - minZ);
            float sy = (IconH - 2f * pad) / Mathf.Max(1e-3f, maxY - minY);
            float s = Mathf.Min(sx, sy);
            float offX = (IconW - (maxZ - minZ) * s) * 0.5f, offY = (IconH - (maxY - minY) * s) * 0.5f;
            var px = new Color32[IconW * IconH];
            for (int y = 0; y < IconH; y++)
            {
                for (int x = 0; x < IconW; x++)
                {
                    int hits = 0;
                    for (int sy2 = 0; sy2 < 2; sy2++)
                    for (int sx2 = 0; sx2 < 2; sx2++)
                    {
                        float gz = minZ + (x + 0.25f + sx2 * 0.5f - offX) / s;
                        float gy = minY + (y + 0.25f + sy2 * 0.5f - offY) / s;
                        if (Inside(d, gz, gy)) hits++;
                    }
                    byte a = (byte)(hits * 255 / 4);
                    px[y * IconW + x] = new Color32(255, 255, 255, a);
                }
            }
            t = new Texture2D(IconW, IconH, TextureFormat.RGBA32, false) { name = "Icon " + d.Id, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            t.SetPixels32(px);
            t.Apply(false);
            icons[d.Id] = t;
            return t;
        }

        static bool Inside(GunDef d, float z, float y)
        {
            foreach (GunPart p in d.Parts)
            {
                if (p.Mat == GunMat.Glow) continue;
                float dz = z - p.Pos.z, dy = y - p.Pos.y;
                float a = p.Euler.x * Mathf.Deg2Rad;
                // Into the part's frame: undo its rotation about x (y' = y cos - z sin, z' = y sin + z cos).
                float ly = dy * Mathf.Cos(a) + dz * Mathf.Sin(a);
                float lz = -dy * Mathf.Sin(a) + dz * Mathf.Cos(a);
                float hz = p.Size.z * 0.5f;
                float hy = p.Cyl ? p.Size.x : p.Size.y * 0.5f;
                if (Mathf.Abs(lz) <= hz && Mathf.Abs(ly) <= hy) return true;
            }
            return false;
        }
    }
}

// World palettes (Day, Dusk, Night, High contrast): sun, ambient, fog, sky and
// the tint of walls and floors. Map materials register their base colour here
// so a palette change applies live without rebuilding the map.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    public enum Surface
    {
        Wall,
        Floor,
        Ground,
        Trim,
        Decal,
    }

    public static class WorldLook
    {
        struct Look
        {
            public Color Sky, Horizon, Fog, Sun, AmbSky, AmbEq, AmbGround, Wall, Floor, Ground;
            public float FogDensity, SunIntensity, ShadowStrength, Exposure;
            public Vector3 SunEuler;
        }

        struct Entry
        {
            public Material Mat;
            public Color Base;
            public Surface Kind;
        }

        static readonly List<Entry> registry = new List<Entry>();
        static Light sun;
        static Camera cam;
        static Material sky;
        static WorldPaletteId applied = (WorldPaletteId)(-1);

        static Look Get(WorldPaletteId id)
        {
            switch (id)
            {
                case WorldPaletteId.Dusk:
                    return new Look
                    {
                        Sky = new Color(0.55f, 0.50f, 0.72f), Horizon = new Color(0.98f, 0.66f, 0.50f), Fog = new Color(0.90f, 0.66f, 0.56f), FogDensity = 0.013f,
                        Sun = new Color(1f, 0.66f, 0.40f), SunIntensity = 1.05f, SunEuler = new Vector3(17f, -62f, 0f), ShadowStrength = 0.72f,
                        AmbSky = new Color(0.60f, 0.52f, 0.68f), AmbEq = new Color(0.72f, 0.56f, 0.50f), AmbGround = new Color(0.34f, 0.27f, 0.29f),
                        Wall = new Color(1f, 0.92f, 0.86f), Floor = new Color(1f, 0.90f, 0.84f), Ground = new Color(0.9f, 0.78f, 0.72f), Exposure = 1.1f,
                    };
                case WorldPaletteId.Night:
                    return new Look
                    {
                        Sky = new Color(0.05f, 0.07f, 0.15f), Horizon = new Color(0.14f, 0.18f, 0.30f), Fog = new Color(0.11f, 0.14f, 0.23f), FogDensity = 0.014f,
                        Sun = new Color(0.62f, 0.72f, 1f), SunIntensity = 0.6f, SunEuler = new Vector3(58f, 25f, 0f), ShadowStrength = 0.65f,
                        AmbSky = new Color(0.32f, 0.37f, 0.55f), AmbEq = new Color(0.24f, 0.27f, 0.37f), AmbGround = new Color(0.12f, 0.13f, 0.18f),
                        Wall = new Color(0.78f, 0.82f, 0.96f), Floor = new Color(0.70f, 0.74f, 0.88f), Ground = new Color(0.45f, 0.48f, 0.6f), Exposure = 0.35f,
                    };
                case WorldPaletteId.HighContrast:
                    return new Look
                    {
                        Sky = new Color(0.90f, 0.92f, 0.95f), Horizon = new Color(0.96f, 0.96f, 0.97f), Fog = new Color(0.94f, 0.95f, 0.96f), FogDensity = 0.004f,
                        Sun = Color.white, SunIntensity = 1.2f, SunEuler = new Vector3(55f, -30f, 0f), ShadowStrength = 0.4f,
                        AmbSky = new Color(0.92f, 0.92f, 0.94f), AmbEq = new Color(0.85f, 0.85f, 0.86f), AmbGround = new Color(0.6f, 0.6f, 0.6f),
                        Wall = new Color(1.05f, 1.05f, 1.05f), Floor = new Color(0.50f, 0.51f, 0.54f), Ground = new Color(0.42f, 0.43f, 0.45f), Exposure = 1.2f,
                    };
                default:
                    return new Look
                    {
                        Sky = new Color(0.62f, 0.76f, 0.92f), Horizon = Palette.Sky, Fog = Palette.Fog, FogDensity = 0.011f,
                        Sun = new Color(1f, 0.96f, 0.9f), SunIntensity = 1.15f, SunEuler = new Vector3(52f, -35f, 0f), ShadowStrength = 0.55f,
                        AmbSky = new Color(0.80f, 0.84f, 0.92f), AmbEq = new Color(0.74f, 0.72f, 0.70f), AmbGround = new Color(0.48f, 0.46f, 0.45f),
                        Wall = Color.white, Floor = Color.white, Ground = Color.white, Exposure = 1.3f,
                    };
            }
        }

        /// <summary>Creates the sun and the sky material once; call from GameController.Awake.</summary>
        public static void Setup(Camera camera)
        {
            cam = camera;
            if (sun == null)
            {
                var sunGo = new GameObject("Sun");
                sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.shadowBias = 0.04f;
                sun.shadowNormalBias = 0.3f;
                RenderSettings.sun = sun;
            }
            if (sky == null)
            {
                Shader s = Shader.Find("Skybox/Procedural");
                if (s != null) sky = new Material(s) { hideFlags = HideFlags.DontSave };
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            ApplyQualityExtras();
            applied = (WorldPaletteId)(-1);
            Apply();
        }

        /// <summary>Soft shadows out to 70 m with two cascades, unless the quality level turned shadows off.</summary>
        public static void ApplyQualityExtras()
        {
            QualitySettings.shadowDistance = 70f;
            if (QualitySettings.shadows != ShadowQuality.Disable) QualitySettings.shadows = ShadowQuality.All;
            if (QualitySettings.shadowCascades > 2) QualitySettings.shadowCascades = 2;
        }

        public static void Register(Material m, Color baseColor, Surface kind)
        {
            if (m == null) return;
            registry.Add(new Entry { Mat = m, Base = baseColor, Kind = kind });
            Tint(registry[registry.Count - 1], Get(Settings.World));
        }

        /// <summary>Forgets the previous map's materials (call before building a map).</summary>
        public static void Prune()
        {
            registry.Clear();
        }

        static void Tint(Entry e, Look l)
        {
            Color t;
            switch (e.Kind)
            {
                case Surface.Wall: case Surface.Trim: t = l.Wall; break;
                case Surface.Floor: t = l.Floor; break;
                case Surface.Ground: t = l.Ground; break;
                default: t = Color.white; break;
            }
            Color c = new Color(Mathf.Clamp01(e.Base.r * t.r), Mathf.Clamp01(e.Base.g * t.g), Mathf.Clamp01(e.Base.b * t.b), e.Base.a);
            if (e.Mat.HasProperty("_Color")) e.Mat.color = c;
        }

        /// <summary>Applies the palette in Settings.World when it changed.</summary>
        public static void Apply()
        {
            if (applied == Settings.World) return;
            applied = Settings.World;
            Look l = Get(Settings.World);
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(l.SunEuler);
                sun.color = l.Sun;
                sun.intensity = l.SunIntensity;
                sun.shadowStrength = l.ShadowStrength;
            }
            RenderSettings.ambientSkyColor = l.AmbSky;
            RenderSettings.ambientEquatorColor = l.AmbEq;
            RenderSettings.ambientGroundColor = l.AmbGround;
            RenderSettings.fogColor = l.Fog;
            RenderSettings.fogDensity = l.FogDensity;
            if (sky != null)
            {
                if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", l.Sky);
                if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", l.Fog);
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", l.Exposure);
                if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", Settings.World == WorldPaletteId.Dusk ? 1.6f : 0.9f);
                if (sky.HasProperty("_SunSize")) sky.SetFloat("_SunSize", 0.03f);
                RenderSettings.skybox = sky;
            }
            if (cam != null)
            {
                cam.backgroundColor = l.Horizon;
                cam.clearFlags = sky != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            }
            for (int i = 0; i < registry.Count; i++)
                if (registry[i].Mat != null) Tint(registry[i], l);
        }
    }
}

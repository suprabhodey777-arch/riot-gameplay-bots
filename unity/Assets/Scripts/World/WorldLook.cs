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
            public float FogDensity, SunIntensity, ShadowStrength, Exposure, SunGlow, SunDisc;
            public Vector3 SunEuler;
            /// <summary>Grading: x saturation, y contrast, z vignette, w on; lift (shadow tint) and gain (highlight tint).</summary>
            public Vector4 Grade;
            public Color Lift, Gain;
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
                    // The last light: the sun almost on the horizon, deep orange against violet shade.
                    return new Look
                    {
                        Sky = new Color(0.24f, 0.25f, 0.45f), Horizon = new Color(1f, 0.56f, 0.36f), Fog = new Color(0.66f, 0.46f, 0.42f), FogDensity = 0.004f,
                        Sun = new Color(1f, 0.6f, 0.36f), SunIntensity = 1.4f, SunEuler = new Vector3(14f, -62f, 0f), ShadowStrength = 0.88f,
                        AmbSky = new Color(0.46f, 0.46f, 0.66f), AmbEq = new Color(0.46f, 0.41f, 0.52f), AmbGround = new Color(0.27f, 0.22f, 0.24f),
                        Wall = new Color(1f, 0.95f, 0.9f), Floor = new Color(1f, 0.93f, 0.88f), Ground = new Color(0.88f, 0.78f, 0.72f), Exposure = 1f,
                        SunGlow = 1.1f, SunDisc = 3f,
                        Grade = new Vector4(1.02f, 1.1f, 0.36f, 1f), Lift = new Color(0.016f, 0.008f, 0.034f), Gain = new Color(1.04f, 0.99f, 0.93f),
                    };
                case WorldPaletteId.Night:
                    return new Look
                    {
                        Sky = new Color(0.02f, 0.03f, 0.08f), Horizon = new Color(0.08f, 0.11f, 0.2f), Fog = new Color(0.06f, 0.08f, 0.14f), FogDensity = 0.0062f,
                        Sun = new Color(0.62f, 0.72f, 1f), SunIntensity = 0.8f, SunEuler = new Vector3(52f, 25f, 0f), ShadowStrength = 0.75f,
                        AmbSky = new Color(0.27f, 0.32f, 0.5f), AmbEq = new Color(0.2f, 0.23f, 0.34f), AmbGround = new Color(0.1f, 0.11f, 0.16f),
                        Wall = new Color(0.8f, 0.84f, 0.96f), Floor = new Color(0.74f, 0.78f, 0.9f), Ground = new Color(0.45f, 0.48f, 0.6f), Exposure = 1f,
                        SunGlow = 0.25f, SunDisc = 1.6f,
                        Grade = new Vector4(0.9f, 1.08f, 0.38f, 1f), Lift = new Color(0f, 0.008f, 0.028f), Gain = new Color(0.96f, 0.99f, 1.05f),
                    };
                case WorldPaletteId.HighContrast:
                    // Readability first: neutral light, weak shadows, no grade (the tonemap still stops clipping).
                    return new Look
                    {
                        Sky = new Color(0.78f, 0.82f, 0.88f), Horizon = new Color(0.9f, 0.9f, 0.91f), Fog = new Color(0.88f, 0.89f, 0.9f), FogDensity = 0.003f,
                        Sun = Color.white, SunIntensity = 1.15f, SunEuler = new Vector3(55f, -30f, 0f), ShadowStrength = 0.4f,
                        AmbSky = new Color(0.8f, 0.8f, 0.82f), AmbEq = new Color(0.74f, 0.74f, 0.75f), AmbGround = new Color(0.55f, 0.55f, 0.55f),
                        Wall = new Color(1.05f, 1.05f, 1.05f), Floor = new Color(0.6f, 0.61f, 0.64f), Ground = new Color(0.5f, 0.51f, 0.53f), Exposure = 1f,
                        SunGlow = 0f, SunDisc = 0f,
                        Grade = Vector4.zero, Lift = Color.black, Gain = Color.white,
                    };
                default:
                    // Day is golden hour (docs/art-direction.md): a warm sun about 28 degrees up, long soft shadows,
                    // cool blue shade, a warm horizon and a light warm haze far away.
                    return new Look
                    {
                        Sky = new Color(0.27f, 0.41f, 0.66f), Horizon = new Color(0.99f, 0.78f, 0.55f), Fog = new Color(0.86f, 0.72f, 0.6f), FogDensity = 0.003f,
                        Sun = new Color(1f, 0.79f, 0.56f), SunIntensity = 1.55f, SunEuler = new Vector3(28f, -38f, 0f), ShadowStrength = 0.92f,
                        AmbSky = new Color(0.56f, 0.64f, 0.8f), AmbEq = new Color(0.5f, 0.55f, 0.66f), AmbGround = new Color(0.4f, 0.35f, 0.31f),
                        Wall = Color.white, Floor = Color.white, Ground = Color.white, Exposure = 1f,
                        SunGlow = 0.7f, SunDisc = 2.4f,
                        Grade = new Vector4(1.06f, 1.06f, 0.3f, 1f), Lift = new Color(0.004f, 0.012f, 0.03f), Gain = new Color(1.03f, 1f, 0.95f),
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
                sun.shadowBias = 0.03f;
                sun.shadowNormalBias = 0.25f;
                RenderSettings.sun = sun;
            }
            if (sky == null)
            {
                // Our own gradient sky; Unity's procedural one if that shader is missing from a build.
                Shader s = Shader.Find("Saltworks/Sky");
                if (s == null || !s.isSupported) s = Shader.Find("Skybox/Procedural");
                if (s != null) sky = new Material(s) { hideFlags = HideFlags.DontSave };
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            Textures.WeatherNoise();
            ApplyQualityExtras();
            applied = (WorldPaletteId)(-1);
            Apply();
        }

        /// <summary>Shadows, MSAA and the rest of the graphics preset (Gfx), after the quality level is set.</summary>
        public static void ApplyQualityExtras()
        {
            Gfx.ApplyQuality();
        }

        // ---- glowing materials and reflections ------------------------------------------------------
        struct GlowEntry
        {
            public Material Mat;
            public Color Color;
            public float Intensity;
        }

        static readonly List<GlowEntry> glows = new List<GlowEntry>();
        static ReflectionProbe probe;

        /// <summary>Emission scales with the palette (light strips read brighter at dusk and night).</summary>
        public static void RegisterGlow(Material m, Color c, float intensity)
        {
            if (m == null || !m.HasProperty("_EmissionColor")) return;
            glows.Add(new GlowEntry { Mat = m, Color = c, Intensity = intensity });
            m.SetColor("_EmissionColor", c * intensity * GlowBoost(Settings.World));
        }

        static float GlowBoost(WorldPaletteId id)
        {
            switch (id)
            {
                case WorldPaletteId.Dusk: return 1.5f;
                case WorldPaletteId.Night: return 2.2f;
                case WorldPaletteId.HighContrast: return 0.8f;
                default: return 1f;
            }
        }

        /// <summary>
        /// One realtime reflection probe over the whole map, rendered once after a build or a palette change, so metal
        /// (guns, trims, pipes) reflects the map and sky instead of black.
        /// </summary>
        public static void RefreshReflections(MapDef map)
        {
            if (map == null) return;
            float w = map.bounds != null && map.bounds.width > 0 ? map.bounds.width : 100f;
            float h = map.bounds != null && map.bounds.height > 0 ? map.bounds.height : 100f;
            if (probe == null)
            {
                var go = new GameObject("Reflection probe");
                probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Realtime;
                probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                probe.resolution = 128;
                probe.boxProjection = false;
                probe.clearFlags = ReflectionProbeClearFlags.Skybox;
                probe.cullingMask = ~(1 << Viewmodel.Layer);
                probe.shadowDistance = 0f;
                probe.importance = 1;
            }
            probe.transform.position = new Vector3(w * 0.5f, 3f, h * 0.5f);
            probe.size = new Vector3(w + 60f, 60f, h + 60f);
            probeMap = map;
            if (QualitySettings.realtimeReflectionProbes) probe.RenderProbe();
        }

        static MapDef probeMap;

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
            glows.Clear();
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
                if (sky.HasProperty("_Zenith"))
                {
                    sky.SetColor("_Zenith", l.Sky);
                    sky.SetColor("_Horizon", l.Horizon);
                    sky.SetColor("_Ground", l.Fog);
                    sky.SetColor("_SunColor", l.Sun);
                    sky.SetFloat("_SunGlow", l.SunGlow);
                    sky.SetFloat("_SunDisc", l.SunDisc);
                    Vector3 toSun = -(Quaternion.Euler(l.SunEuler) * Vector3.forward);
                    sky.SetVector("_SunDir", new Vector4(toSun.x, toSun.y, toSun.z, 0f));
                }
                if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", l.Sky);
                if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", l.Fog);
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", l.Exposure);
                if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", Settings.World == WorldPaletteId.Dusk ? 1.6f : 0.9f);
                if (sky.HasProperty("_SunSize")) sky.SetFloat("_SunSize", 0.045f);
                if (sky.HasProperty("_SunSizeConvergence")) sky.SetFloat("_SunSizeConvergence", 4f);
                RenderSettings.skybox = sky;
            }
            Shader.SetGlobalVector("_SaltGrade", l.Grade);
            Shader.SetGlobalVector("_SaltGradeLift", new Vector4(l.Lift.r, l.Lift.g, l.Lift.b, 0f));
            Shader.SetGlobalVector("_SaltGradeGain", new Vector4(l.Gain.r, l.Gain.g, l.Gain.b, l.Exposure > 0f ? l.Exposure : 1f));
            if (cam != null)
            {
                cam.backgroundColor = l.Horizon;
                cam.clearFlags = sky != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            }
            for (int i = 0; i < registry.Count; i++)
                if (registry[i].Mat != null) Tint(registry[i], l);
            float boost = GlowBoost(Settings.World);
            for (int i = 0; i < glows.Count; i++)
                if (glows[i].Mat != null) glows[i].Mat.SetColor("_EmissionColor", glows[i].Color * glows[i].Intensity * boost);
            if (probe != null && probeMap != null && QualitySettings.realtimeReflectionProbes) probe.RenderProbe();
        }
    }
}

// Bloom on the main camera (OnRenderImage, Hidden/Saltworks/Bloom): a bright
// pass at half resolution, four box-filtered levels down and back up, and a
// composite. About 10 full-screen-equivalent texture fetches at half size and
// below, so it stays well inside a 120 fps budget. Its last pass also tonemaps
// and grades. On Low and Medium there is no bloom: one full-screen pass with
// one texture fetch does the tonemap, grade and vignette.
//
// Gfx: the graphics presets (Low / Medium / High / Ultra) applied to
// QualitySettings and the cameras: shadows, MSAA, HDR, bloom, resolution scale
// and the map's prop density.
using UnityEngine;

namespace Saltworks
{
    [RequireComponent(typeof(Camera))]
    public class Glow : MonoBehaviour
    {
        public float Threshold = 0.95f;
        public float Knee = 0.45f;
        public float Intensity = 0.55f;
        public int Levels = 4;

        Material mat;
        readonly RenderTexture[] chain = new RenderTexture[8];

        void Awake()
        {
            Shader s = Shader.Find("Hidden/Saltworks/Bloom");
            if (s != null && s.isSupported) mat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            else Debug.LogWarning("Saltworks: the bloom shader is missing or unsupported; bloom is off.");
            // On from the start (tonemap and grade only), so the menu backdrop is graded too.
            enabled = mat != null;
        }

        public bool Available => mat != null;
        /// <summary>False: one tonemap-and-grade pass only (Low and Medium). True: bloom, then the same grade.</summary>
        public bool BloomOn;

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (mat == null)
            {
                Graphics.Blit(src, dst);
                return;
            }
            if (!BloomOn)
            {
                Graphics.Blit(src, dst, mat, 4);
                return;
            }
            mat.SetVector("_Params", new Vector4(Threshold, Knee, Intensity, 0f));
            RenderTextureFormat fmt = src.format;
            int w = Mathf.Max(1, src.width / 2), h = Mathf.Max(1, src.height / 2);
            chain[0] = RenderTexture.GetTemporary(w, h, 0, fmt);
            Graphics.Blit(src, chain[0], mat, 0);
            int n = 1;
            for (; n < Mathf.Min(Levels, chain.Length); n++)
            {
                w /= 2;
                h /= 2;
                if (w < 8 || h < 8) break;
                chain[n] = RenderTexture.GetTemporary(w, h, 0, fmt);
                Graphics.Blit(chain[n - 1], chain[n], mat, 1);
            }
            // Back up: each level = the level below (upsampled) + itself, into a fresh target.
            RenderTexture up = chain[n - 1];
            bool upOwned = false;
            for (int i = n - 2; i >= 0; i--)
            {
                RenderTexture next = RenderTexture.GetTemporary(chain[i].width, chain[i].height, 0, fmt);
                mat.SetTexture("_BloomTex", chain[i]);
                Graphics.Blit(up, next, mat, 2);
                if (upOwned) RenderTexture.ReleaseTemporary(up);
                up = next;
                upOwned = true;
            }
            mat.SetTexture("_BloomTex", up);
            Graphics.Blit(src, dst, mat, 3);
            if (upOwned) RenderTexture.ReleaseTemporary(up);
            for (int i = 0; i < n; i++)
            {
                RenderTexture.ReleaseTemporary(chain[i]);
                chain[i] = null;
            }
        }
    }

    public static class Gfx
    {
        public static int Preset => Mathf.Clamp(Settings.GfxPreset, 0, 3);

        /// <summary>Props per metre of wall, relative (Low 0.35 to Ultra 1.3).</summary>
        public static float PropDensity
        {
            get
            {
                switch (Preset)
                {
                    case 0: return 0.35f;
                    case 1: return 0.7f;
                    case 3: return 1.3f;
                    default: return 1f;
                }
            }
        }

        public static float ResolutionScale
        {
            get
            {
                switch (Preset)
                {
                    case 0: return 0.75f;
                    case 1: return 0.9f;
                    default: return 1f;
                }
            }
        }

        public static bool BloomOn => Preset >= 2;

        /// <summary>QualitySettings for the preset (call after any SetQualityLevel).</summary>
        public static void ApplyQuality()
        {
            int p = Preset;
            // Golden hour lives on its long shadows, so every preset keeps them: hard and short-range on Low.
            ShadowQuality sq = p == 0 ? ShadowQuality.HardOnly : ShadowQuality.All;
            if (QualitySettings.shadows != sq) QualitySettings.shadows = sq;
            QualitySettings.shadowResolution = p == 0 ? ShadowResolution.Medium : p == 1 ? ShadowResolution.High : ShadowResolution.VeryHigh;
            QualitySettings.shadowDistance = p == 0 ? 45f : p == 1 ? 65f : p == 2 ? 90f : 120f;
            QualitySettings.shadowCascades = p == 0 ? 2 : 4;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            int aa = p == 0 ? 0 : p == 1 ? 2 : 4;
            if (QualitySettings.antiAliasing != aa) QualitySettings.antiAliasing = aa;
            QualitySettings.pixelLightCount = p == 0 ? 1 : p == 1 ? 2 : 4;
            QualitySettings.realtimeReflectionProbes = p >= 1;
            QualitySettings.anisotropicFiltering = p == 0 ? AnisotropicFiltering.Disable : AnisotropicFiltering.Enable;
            QualitySettings.softParticles = false;
        }

        /// <summary>Camera-side settings: HDR, bloom and the render scale.</summary>
        public static void ApplyCamera(Camera main, Camera weapon, Glow glow)
        {
            int p = Preset;
            if (main != null)
            {
                // HDR on every preset: the tonemap needs the values above 1 to roll them off instead of clipping.
                main.allowHDR = true;
                main.allowMSAA = p >= 1;
                float s = ResolutionScale;
                main.allowDynamicResolution = s < 0.999f;
                if (weapon != null) weapon.allowDynamicResolution = s < 0.999f;
                ScalableBufferManager.ResizeBuffers(s, s);
            }
            if (glow != null)
            {
                glow.enabled = glow.Available;
                glow.BloomOn = BloomOn;
                glow.Levels = p >= 3 ? 5 : 4;
                glow.Intensity = p >= 3 ? 0.65f : 0.55f;
                // Without HDR the brightest pixels are 1.0, so the bright pass starts lower.
                glow.Threshold = main != null && main.allowHDR ? 1.0f : 0.82f;
            }
        }
    }
}

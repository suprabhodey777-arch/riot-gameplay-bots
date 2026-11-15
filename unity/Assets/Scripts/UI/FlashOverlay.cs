// Flashbang screen effect: a white-out curve over the blind's duration plus a
// desaturated afterimage of the frame the flash went off. Self-contained: it
// watches the blind time jump up to recognise a new (or extended) blind, grabs
// one screen frame through the camera, and releases the texture when the
// afterimage has faded. Call from OnGUI: once over the world, then once more
// after the HUD with hudPass = true so the HUD is washed out too. Drawing goes
// through GUI.DrawTexture (not GL), because IMGUI draws queued GUI elements in
// call order: that is the only way the second pass can land on top of the HUD.
using UnityEngine;

namespace Saltworks
{
    public static class FlashOverlay
    {
        const float RiseS = 0.06f;
        const float HoldShare = 0.35f;
        const float ShortBlindS = 0.8f;
        const float ShortPeak = 0.75f;
        const float AfterimageLife = 1.3f;
        const float NewBlindJumpS = 0.1f;

        static float T = 1f;
        static float bornAt = -1000f;
        static float prevLeft;
        static bool capturePending;
        static bool hooked;
        static Texture2D afterimage;

        /// <summary>True while the white-out or its afterimage still needs drawing.</summary>
        public static bool Active
        {
            get
            {
                if (prevLeft > 0f) return true;
                float elapsed = Time.unscaledTime - bornAt;
                return elapsed >= 0f && elapsed < AfterimageLife * T + 0.05f;
            }
        }

        /// <summary>
        /// Draws the effect. `screen` is the full-screen rect in the HUD's GUI coordinates
        /// (ClientUI's <c>new Rect(0f, 0f, W, H)</c>), so GUI.matrix scaling applies as for the rest
        /// of the HUD. Call with hudPass = false over the world, then with hudPass = true after the HUD.
        /// </summary>
        public static void Draw(Rect screen, float blindSecondsLeft, float blindTotalSeconds, bool hudPass)
        {
            Update(blindSecondsLeft, blindTotalSeconds);
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (screen.width < 1f || screen.height < 1f) return;

            float left = Mathf.Max(0f, blindSecondsLeft);
            float white = WhiteAlpha(left);
            if (hudPass)
            {
                // After the HUD: wash it out with the same white, at 60% of its strength.
                if (white > 0.001f) Fill(screen, new Color(1f, 1f, 0.97f, 0.6f * white));
                return;
            }

            if (capturePending) EnsureHook();

            float elapsed = Time.unscaledTime - bornAt;
            if (afterimage != null)
            {
                float k = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, AfterimageLife * T));
                float fade = 1f - Mathf.SmoothStep(0f, 1f, k);
                if (fade > 0.001f) DrawAfterimage(screen, fade * 0.85f);
                else ReleaseAfterimage();
            }
            if (white > 0.001f) Fill(screen, new Color(1f, 1f, 0.97f, white));
        }

        // ---- state -------------------------------------------------------------------------------
        static void Update(float left, float total)
        {
            left = Mathf.Max(0f, left);
            total = Mathf.Max(0.05f, total);
            bool fresh = left > 0f && (prevLeft <= 0f || left > prevLeft + NewBlindJumpS);
            if (fresh)
            {
                // A new or extended blind restarts both the curve and the afterimage.
                T = Mathf.Max(total, left);
                bornAt = Time.unscaledTime;
                capturePending = true;
                ReleaseAfterimage();
            }
            prevLeft = left;
            if (T < total) T = total;
        }

        /// <summary>0..1 white strength: quick rise, hold, then a smooth ease out to zero at T.</summary>
        static float WhiteAlpha(float left)
        {
            if (left <= 0f) return 0f;
            float elapsed = Mathf.Max(0f, Time.unscaledTime - bornAt);
            float peak = T < ShortBlindS ? ShortPeak : 1f;
            float holdEnd = T < ShortBlindS ? RiseS : RiseS + HoldShare * T;
            if (elapsed < RiseS) return peak * Mathf.Clamp01(elapsed / RiseS);
            if (elapsed < holdEnd) return peak;
            float k = Mathf.Clamp01((elapsed - holdEnd) / Mathf.Max(0.001f, T - holdEnd));
            return peak * (1f - Mathf.SmoothStep(0f, 1f, k));
        }

        // ---- capture -----------------------------------------------------------------------------
        static void EnsureHook()
        {
            if (hooked) return;
            hooked = true;
            Camera.onPostRender += OnPostRender;
        }

        /// <summary>The built-in pipeline's per-camera post-render callback: the safe readback point.</summary>
        static void OnPostRender(Camera cam)
        {
            if (!capturePending || cam == null) return;
            CaptureNow();
        }

        /// <summary>
        /// Grabs the current render target into a half-size grey texture. Called from OnPostRender:
        /// that is the documented readback point in the built-in pipeline (which this project uses),
        /// runs before the IMGUI HUD is drawn, and needs no end-of-frame coroutine.
        /// </summary>
        internal static void CaptureNow()
        {
            if (!capturePending) return;
            capturePending = false;
            if (Time.unscaledTime - bornAt > 0.25f) return; // took too long: drop it rather than flash late
            int sw = Screen.width, sh = Screen.height;
            if (sw < 2 || sh < 2) return;
            Texture2D full = null;
            try
            {
                full = new Texture2D(sw, sh, TextureFormat.RGB24, false);
                full.ReadPixels(new Rect(0f, 0f, sw, sh), 0, 0);
                full.Apply(false);
                Color32[] src = full.GetPixels32();
                int dw = Mathf.Max(1, sw / 2), dh = Mathf.Max(1, sh / 2);
                var dst = new Color32[dw * dh];
                for (int y = 0; y < dh; y++)
                {
                    int sy = Mathf.Min(sh - 1, y * 2) * sw;
                    int dy = y * dw;
                    for (int x = 0; x < dw; x++)
                    {
                        Color32 c = src[sy + Mathf.Min(sw - 1, x * 2)];
                        // Desaturate and brighten a little: a burnt-in afterimage of the flash.
                        byte v = (byte)Mathf.Clamp(Mathf.RoundToInt((0.299f * c.r + 0.587f * c.g + 0.114f * c.b) * 1.15f + 14f), 0, 255);
                        dst[dy + x] = new Color32(v, v, v, 255);
                    }
                }
                ReleaseAfterimage();
                afterimage = new Texture2D(dw, dh, TextureFormat.RGB24, false)
                {
                    name = "Flash afterimage",
                    hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                afterimage.SetPixels32(dst);
                afterimage.Apply(false);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Flash afterimage capture failed: " + e.Message);
                ReleaseAfterimage();
            }
            finally
            {
                if (full != null) Object.Destroy(full);
            }
        }

        static void ReleaseAfterimage()
        {
            if (afterimage == null) return;
            Object.Destroy(afterimage);
            afterimage = null;
        }

        // ---- drawing -----------------------------------------------------------------------------
        static void DrawAfterimage(Rect screen, float alpha)
        {
            Color old = GUI.color;
            GUI.color = new Color(old.r, old.g, old.b, alpha * old.a);
            GUI.DrawTexture(screen, afterimage, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = new Color(c.r * old.r, c.g * old.g, c.b * old.b, c.a * old.a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}

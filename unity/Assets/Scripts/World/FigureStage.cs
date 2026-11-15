// A small photo studio far outside the map that renders the agents and the
// personas on turntables into one render texture, for the select screens and
// the streamer camera frame. One orthographic camera sees a row of slots; each
// slot is a BotRig on a lit disc. The UI calls Use() every frame it shows the
// figures; the camera renders only then (every frame in menus, every third
// frame in a round), so it costs nothing otherwise.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    public struct FigureSpec
    {
        /// <summary>Agent or persona id (the silhouette, pack and visor).</summary>
        public string Look;
        public Color Accent;
        public Color Team;
        public string Weapon;
    }

    public class FigureStage : MonoBehaviour
    {
        static readonly Vector3 Origin = new Vector3(-4000f, 0f, -4000f);
        const float SlotW = 1.3f, SlotH = 2.5f;
        const int PixelsHigh = 640;

        Camera cam;
        RenderTexture rt;
        readonly List<Slot> slots = new List<Slot>();
        readonly List<Object> owned = new List<Object>();
        string key;
        float lastUse = -10f;
        bool inRound;
        int frame;
        Light keyLight, rimLight;

        class Slot
        {
            public Transform Spin;
            public BotRig Rig;
            public Material Ring;
            public Color Accent;
            public float Angle;
        }

        /// <summary>The slot drawn larger and turning faster (the hovered or selected card), or -1.</summary>
        public int Hero = -1;
        public Texture Texture => rt;
        public int Count => slots.Count;

        /// <summary>The texture coordinates of slot `i` inside Texture.</summary>
        public Rect Uv(int i)
        {
            int n = Mathf.Max(1, slots.Count);
            return new Rect(i / (float)n, 0f, 1f / n, 1f);
        }

        /// <summary>Width / height of one slot, to size the rect it is drawn into.</summary>
        public static float Aspect => SlotW / SlotH;

        void Awake()
        {
            transform.position = Origin;
            var cgo = new GameObject("Figure camera");
            cgo.transform.SetParent(transform, false);
            cam = cgo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = SlotH / 2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.025f, 0.04f, 1f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.useOcclusionCulling = false;
            cgo.transform.localPosition = new Vector3(0f, SlotH / 2f + 0.25f, -8f);
            cgo.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);

            keyLight = new GameObject("Key light").AddComponent<Light>();
            keyLight.transform.SetParent(transform, false);
            keyLight.transform.localPosition = new Vector3(-2f, 3.2f, -3f);
            keyLight.type = LightType.Point;
            keyLight.range = 14f;
            keyLight.intensity = 1.6f;
            keyLight.color = new Color(1f, 0.96f, 0.92f);
            keyLight.shadows = LightShadows.None;
            rimLight = new GameObject("Rim light").AddComponent<Light>();
            rimLight.transform.SetParent(transform, false);
            rimLight.transform.localPosition = new Vector3(1.5f, 2.6f, 2.5f);
            rimLight.type = LightType.Point;
            rimLight.range = 12f;
            rimLight.intensity = 2.2f;
            rimLight.color = new Color(0.45f, 0.8f, 1f);
            rimLight.shadows = LightShadows.None;
            var fill = new GameObject("Fill light").AddComponent<Light>();
            fill.transform.SetParent(transform, false);
            fill.transform.localPosition = new Vector3(2.4f, 1.3f, -2.6f);
            fill.type = LightType.Point;
            fill.range = 10f;
            fill.intensity = 0.55f;
            fill.color = new Color(1f, 0.9f, 0.8f);
            fill.shadows = LightShadows.None;
        }

        void OnDestroy()
        {
            ClearSlots();
            if (rt != null) rt.Release();
            if (rt != null) Destroy(rt);
        }

        void ClearSlots()
        {
            foreach (Slot s in slots)
            {
                s.Rig?.Destroy();
                if (s.Spin != null) Destroy(s.Spin.parent.gameObject);
            }
            slots.Clear();
            foreach (Object o in owned) if (o != null) Destroy(o);
            owned.Clear();
        }

        /// <summary>
        /// Shows `specs` (rebuilt only when `setKey` changes). Call every frame the figures are on screen; `round` true
        /// while playing (renders every third frame).
        /// </summary>
        public void Use(string setKey, FigureSpec[] specs, bool round = false)
        {
            lastUse = Time.unscaledTime;
            inRound = round;
            if (setKey == key && slots.Count == (specs?.Length ?? 0)) return;
            key = setKey;
            ClearSlots();
            if (specs == null || specs.Length == 0) return;
            int n = specs.Length;
            int w = Mathf.RoundToInt(PixelsHigh * Aspect * n);
            if (rt == null || rt.width != w)
            {
                if (rt != null)
                {
                    rt.Release();
                    Destroy(rt);
                }
                rt = new RenderTexture(w, PixelsHigh, 24, RenderTextureFormat.ARGB32) { name = "Figures", antiAliasing = QualitySettings.antiAliasing > 1 ? 4 : 1, hideFlags = HideFlags.DontSave };
                rt.Create();
            }
            cam.targetTexture = rt;
            cam.aspect = SlotW * n / SlotH;
            Shader add = Shader.Find("Saltworks/FxAdditive");
            for (int i = 0; i < n; i++)
            {
                FigureSpec sp = specs[i];
                var holder = new GameObject("Slot " + i).transform;
                holder.SetParent(transform, false);
                holder.localPosition = new Vector3((i - (n - 1) / 2f) * SlotW, 0f, 0f);
                // The turntable: a dark disc with a glowing ring in the figure's colour.
                var k = new MeshKit(1);
                k.Cylinder(0, MeshKit.At(new Vector3(0f, -0.04f, 0f), new Vector3(90f, 0f, 0f)), 0.55f, 0.08f, 32, new Color(0.08f, 0.09f, 0.11f, 1f), false, true);
                Mesh disc = k.ToMesh("Turntable");
                owned.Add(disc);
                var dgo = new GameObject("Turntable");
                dgo.transform.SetParent(holder, false);
                dgo.AddComponent<MeshFilter>().sharedMesh = disc;
                var dmr = dgo.AddComponent<MeshRenderer>();
                dmr.sharedMaterial = BotRig.BodyMaterial;
                dmr.shadowCastingMode = ShadowCastingMode.Off;
                var rk = new MeshKit(1);
                rk.Cylinder(0, MeshKit.At(new Vector3(0f, 0.005f, 0f), new Vector3(90f, 0f, 0f)), 0.58f, 0.02f, 32, Color.white, false, false);
                Mesh ring = rk.ToMesh("Turntable ring");
                owned.Add(ring);
                Material ringMat = Mats.Glow(sp.Accent, 1.1f);
                owned.Add(ringMat);
                var rgo = new GameObject("Ring");
                rgo.transform.SetParent(holder, false);
                rgo.AddComponent<MeshFilter>().sharedMesh = ring;
                var rmr = rgo.AddComponent<MeshRenderer>();
                rmr.sharedMaterial = ringMat;
                rmr.shadowCastingMode = ShadowCastingMode.Off;
                // A soft light pool on the disc.
                if (add != null)
                {
                    var pk = new MeshKit(1);
                    pk.QuadUv(0, new Vector3(-0.9f, 0.01f, -0.9f), new Vector3(0.9f, 0.01f, -0.9f), new Vector3(0.9f, 0.01f, 0.9f), new Vector3(-0.9f, 0.01f, 0.9f), Vector3.up,
                              new Color(sp.Accent.r, sp.Accent.g, sp.Accent.b, 0.5f), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
                    Mesh pool = pk.ToMesh("Light pool");
                    owned.Add(pool);
                    var pm = new Material(add) { hideFlags = HideFlags.DontSave };
                    pm.SetTexture("_MainTex", Textures.SoftDot());
                    owned.Add(pm);
                    var pgo = new GameObject("Pool");
                    pgo.transform.SetParent(holder, false);
                    pgo.AddComponent<MeshFilter>().sharedMesh = pool;
                    var pmr = pgo.AddComponent<MeshRenderer>();
                    pmr.sharedMaterial = pm;
                    pmr.shadowCastingMode = ShadowCastingMode.Off;
                }
                var spin = new GameObject("Spin").transform;
                spin.SetParent(holder, false);
                var rig = new BotRig(spin, sp.Team, sp.Accent, sp.Look);
                rig.SetWeapon(string.IsNullOrEmpty(sp.Weapon) ? Weapons.Default : sp.Weapon);
                slots.Add(new Slot { Spin = spin, Rig = rig, Ring = ringMat, Accent = sp.Accent, Angle = 200f + i * 23f });
            }
        }

        void LateUpdate()
        {
            bool active = Time.unscaledTime - lastUse < 0.25f && slots.Count > 0 && rt != null;
            if (!active) return;
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < slots.Count; i++)
            {
                Slot s = slots[i];
                bool hero = i == Hero;
                s.Angle += dt * (hero ? 38f : 14f);
                s.Spin.localRotation = Quaternion.Euler(0f, s.Angle, 0f);
                float target = hero ? 1.08f : 1f;
                float sc = Mathf.Lerp(s.Spin.localScale.x, target, 1f - Mathf.Exp(-dt * 8f));
                s.Spin.localScale = new Vector3(sc, sc, sc);
                s.Rig.Animate(dt, Vector3.zero, 0f, false, true);
                if (s.Ring != null && s.Ring.HasProperty("_EmissionColor")) s.Ring.SetColor("_EmissionColor", s.Accent * (hero ? 1.8f : 0.8f + 0.15f * Mathf.Sin(Time.unscaledTime * 2f + i)));
            }
            frame++;
            if (inRound && frame % 3 != 0) return;
            cam.Render();
        }
    }
}

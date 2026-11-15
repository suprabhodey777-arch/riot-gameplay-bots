// Ability and footstep effects driven by sim events: the flash's arc and pop,
// the recon dart and its pulse ring, dash streaks, positional footsteps, and the
// ground marker shown while an aimed ability key is held. Purely visual and
// audible; nothing here feeds back into the sim.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    public class AbilityFx : MonoBehaviour
    {
        /// <summary>Height at which a flash pops above its floor point.</summary>
        public const float FlashPopHeight = 1.6f;

        abstract class Anim
        {
            public float Born;
            /// <summary>Advances the effect; false when it is finished and has cleaned up.</summary>
            public abstract bool Step(AbilityFx fx, float now);
            public abstract void Kill();
        }

        readonly List<Anim> anims = new List<Anim>();
        Transform root;
        Material lineMat;
        Material flashCore;
        Material dartMat;
        Material popShell;
        AudioClip popClip, throwClip, dashClip, thunkClip, pingClip, ringClip;
        AudioClip[][] stepClips;
        AudioClip[] scuffClips;
        AudioSource[] pool;
        int poolNext;
        AudioSource ownSource, stepSource;
        readonly Dictionary<string, StepTrack> tracks = new Dictionary<string, StepTrack>();

        // Aim marker.
        GameObject marker;
        LineRenderer markerRing, markerDot, markerBeam, markerArc;
        const int ArcPoints = 24;

        void Awake()
        {
            root = new GameObject("Ability effects").transform;
            lineMat = Mats.Line();
            flashCore = Mats.Emissive(new Color(1f, 0.97f, 0.8f), 3f);
            dartMat = Mats.Emissive(Agents.AbilityColor(Ability.Recon), 2f);
            popShell = Mats.Transparent(new Color(1f, 1f, 0.95f, 0f), 3250);
            popClip = Synth.FlashPop();
            throwClip = Synth.Whoosh(0.22f, 1);
            dashClip = Synth.Whoosh(0.32f, 2);
            thunkClip = Synth.DartThunk();
            pingClip = Synth.ReconPing();
            ringClip = Synth.BlindRing();
            // Five soft footfalls per surface (concrete, metal, wood) and three quiet scuffs.
            stepClips = new AudioClip[3][];
            for (int s = 0; s < 3; s++)
            {
                stepClips[s] = new AudioClip[5];
                for (int i = 0; i < 5; i++) stepClips[s][i] = Synth.Step((StepSurface)s, i);
            }
            scuffClips = new AudioClip[3];
            for (int i = 0; i < scuffClips.Length; i++) scuffClips[i] = Synth.Scuff(i);

            pool = new AudioSource[14];
            for (int i = 0; i < pool.Length; i++)
            {
                var go = new GameObject("Fx audio " + i);
                go.transform.SetParent(root, false);
                AudioSource a = go.AddComponent<AudioSource>();
                a.playOnAwake = false;
                a.spatialBlend = 1f;
                a.rolloffMode = AudioRolloffMode.Linear;
                a.dopplerLevel = 0f;
                pool[i] = a;
            }
            ownSource = gameObject.AddComponent<AudioSource>();
            ownSource.playOnAwake = false;
            ownSource.spatialBlend = 0f;
            stepSource = gameObject.AddComponent<AudioSource>();
            stepSource.playOnAwake = false;
            stepSource.spatialBlend = 0f;
            BuildMarker();
        }

        public void Clear()
        {
            foreach (Anim a in anims) a.Kill();
            anims.Clear();
            tracks.Clear();
            HideMarker();
        }

        void Update()
        {
            float now = Time.time;
            for (int i = anims.Count - 1; i >= 0; i--)
            {
                if (anims[i].Step(this, now)) continue;
                anims.RemoveAt(i);
            }
        }

        // ---- sound -----------------------------------------------------------------------------

        /// <summary>A positional one-shot from the pool. Linear rolloff between min and max distance.</summary>
        public void Play3D(AudioClip clip, Vector3 pos, float volume, float minDist, float maxDist, float pitch = 1f)
        {
            if (clip == null) return;
            AudioSource a = pool[poolNext];
            poolNext = (poolNext + 1) % pool.Length;
            a.transform.position = pos;
            a.minDistance = minDist;
            a.maxDistance = Mathf.Max(minDist + 0.1f, maxDist);
            a.pitch = pitch;
            a.volume = volume;
            a.clip = clip;
            a.Play();
        }

        public void PlayBlindRing(float seconds)
        {
            ownSource.pitch = 1f;
            ownSource.PlayOneShot(ringClip, Mathf.Clamp01(0.4f + seconds * 0.35f));
        }

        public void PlayOwnDash()
        {
            ownSource.pitch = 1f;
            ownSource.PlayOneShot(dashClip, 0.8f);
        }

        /// <summary>
        /// One footstep event from the sim (it sends one per moving tick). Sounds follow the stride, not a timer: the
        /// distance between an agent's events accumulates and a footfall plays every half gait cycle
        /// (BotRig.CycleLength), so cadence matches the animation and speed. Each footfall picks one of five soft
        /// samples for the surface underfoot (MapBuilder.StepSurfaceAt), never the same twice in a row, with random
        /// pitch and volume. Other agents are heard only on loud (running) steps, positional, fading out at
        /// `runHearingM`. The player's own running steps are 2D and quiet; walking or crouching gives only a faint
        /// scuff every other step.
        /// </summary>
        public void Footstep(string agentId, Vector3 pos, bool loud, bool own, float runHearingM)
        {
            if (stepClips == null) return;
            if (!own && !loud) return;
            float now = Time.time;
            string key = agentId ?? "";
            if (!tracks.TryGetValue(key, out StepTrack tr))
            {
                tr = new StepTrack();
                tracks[key] = tr;
            }
            Vector3 flat = new Vector3(pos.x, 0f, pos.z);
            if (!tr.Has || now - tr.LastEvent > 0.45f)
            {
                // First event of a new movement: the first footfall comes after a short half step.
                tr.Has = true;
                tr.Last = flat;
                tr.LastEvent = now;
                tr.Acc = 0.35f;
                return;
            }
            float dist = (flat - tr.Last).magnitude;
            float dt = Mathf.Max(1e-3f, now - tr.LastEvent);
            tr.Last = flat;
            tr.LastEvent = now;
            if (dist > 2.5f)
            {
                // A dash or a respawn, not a stride.
                tr.Acc = 0f;
                return;
            }
            float speed = dist / dt;
            float step = BotRig.CycleLength(speed, loud ? 0f : 0.5f) * 0.5f;
            tr.Acc += dist;
            if (tr.Acc < step) return;
            tr.Acc = Mathf.Min(tr.Acc - step, step * 0.5f);
            tr.Foot = !tr.Foot;
            float pitch = Random.Range(0.9f, 1.07f);
            float vol = Random.Range(0.82f, 1f);
            if (own)
            {
                stepSource.pitch = pitch;
                if (loud) stepSource.PlayOneShot(PickStep(pos, tr), 0.13f * vol);
                else if (tr.Foot) stepSource.PlayOneShot(scuffClips[Random.Range(0, scuffClips.Length)], 0.035f * vol);
                return;
            }
            float range = Mathf.Max(6f, runHearingM);
            Play3D(PickStep(pos, tr), pos + Vector3.up * 0.05f, 0.5f * vol, 2f, range, pitch);
        }

        AudioClip PickStep(Vector3 pos, StepTrack tr)
        {
            AudioClip[] set = stepClips[(int)MapBuilder.StepSurfaceAt(pos)];
            int i;
            if (tr.LastClip >= 0 && tr.LastClip < set.Length)
            {
                i = Random.Range(0, set.Length - 1);
                if (i >= tr.LastClip) i++;
            }
            else i = Random.Range(0, set.Length);
            tr.LastClip = i;
            return set[i];
        }

        class StepTrack
        {
            public bool Has;
            public Vector3 Last;
            public float LastEvent;
            public float Acc;
            public bool Foot;
            public int LastClip = -1;
        }

        // ---- flash -----------------------------------------------------------------------------

        static float ArcHeight(Vector3 a, Vector3 b)
        {
            return Mathf.Clamp(new Vector2(b.x - a.x, b.z - a.z).magnitude * 0.16f, 0.5f, 3.5f);
        }

        public static Vector3 ArcPoint(Vector3 a, Vector3 b, float h, float t)
        {
            return Vector3.Lerp(a, b, t) + Vector3.up * (4f * h * t * (1f - t));
        }

        static TrailRenderer Trail(GameObject go, Material m, Color c, float width, float time)
        {
            TrailRenderer tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = m;
            tr.time = time;
            tr.widthMultiplier = width;
            tr.minVertexDistance = 0.05f;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.startColor = c;
            tr.endColor = new Color(c.r, c.g, c.b, 0f);
            return tr;
        }

        GameObject Primitive(PrimitiveType t, Vector3 scale, Material m, string name)
        {
            GameObject go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.localScale = scale;
            Collider c = go.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c); // immediately, so this frame's raycasts cannot hit it
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        class FlashAnim : Anim
        {
            public Vector3 From, To;
            public float Flight, H;
            public GameObject Ball;
            public GameObject Shell;
            public Material ShellMat;
            public Light Light;
            public bool Popped;
            public float PopAt;

            public override bool Step(AbilityFx fx, float now)
            {
                float t = (now - Born) / Mathf.Max(0.01f, Flight);
                if (!Popped)
                {
                    if (t < 1f)
                    {
                        Ball.transform.position = ArcPoint(From, To, H, t);
                        float s = 0.09f * (1f + 0.25f * Mathf.Sin(now * 40f));
                        Ball.transform.localScale = Vector3.one * s;
                        return true;
                    }
                    Popped = true;
                    PopAt = now;
                    Ball.GetComponent<MeshRenderer>().enabled = false;
                    Ball.transform.position = To;
                    Shell.SetActive(true);
                    Shell.transform.position = To;
                    Light.transform.position = To;
                    Light.enabled = true;
                    fx.Play3D(fx.popClip, To, 1f, 4f, 60f);
                }
                float k = (now - PopAt) / 0.45f;
                if (k >= 1f)
                {
                    Kill();
                    return false;
                }
                float e = 1f - (1f - k) * (1f - k);
                Shell.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 3.2f, e);
                Color c = ShellMat.color;
                c.a = 0.9f * (1f - k) * (1f - k);
                ShellMat.color = c;
                Light.intensity = 9f * (1f - k) * (1f - k);
                return true;
            }

            public override void Kill()
            {
                if (Ball != null) Object.Destroy(Ball);
                if (Shell != null) Object.Destroy(Shell);
                if (ShellMat != null) Object.Destroy(ShellMat);
                if (Light != null) Object.Destroy(Light.gameObject);
            }
        }

        /// <summary>A flash thrown from `hand` that pops above `floorPoint` after `fuseS` seconds.</summary>
        public void Flash(Vector3 hand, Vector3 floorPoint, float fuseS)
        {
            Vector3 to = new Vector3(floorPoint.x, FlashPopHeight, floorPoint.z);
            GameObject ball = Primitive(PrimitiveType.Sphere, Vector3.one * 0.09f, flashCore, "Flash");
            ball.transform.position = hand;
            Trail(ball, lineMat, new Color(1f, 0.95f, 0.7f, 0.85f), 0.05f, 0.25f);
            Material shellMat = new Material(popShell);
            GameObject shell = Primitive(PrimitiveType.Sphere, Vector3.one, shellMat, "Flash pop");
            shell.SetActive(false);
            var lgo = new GameObject("Flash light");
            lgo.transform.SetParent(root, false);
            Light l = lgo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.97f, 0.88f);
            l.range = 18f;
            l.intensity = 0f;
            l.shadows = LightShadows.None;
            l.enabled = false;
            Play3D(throwClip, hand, 0.6f, 2f, 25f, 1.1f);
            anims.Add(new FlashAnim
            {
                Born = Time.time, From = hand, To = to, Flight = Mathf.Max(0.1f, fuseS), H = ArcHeight(hand, to),
                Ball = ball, Shell = shell, ShellMat = shellMat, Light = l,
            });
        }

        // ---- recon -----------------------------------------------------------------------------

        class ReconAnim : Anim
        {
            public Vector3 From, To;
            public float Flight, H, Radius, Hold;
            public GameObject Dart;
            public Light Blink;
            public LineRenderer[] Rings;
            public Color Color;
            public bool Landed;
            public float LandAt;

            public override bool Step(AbilityFx fx, float now)
            {
                float t = (now - Born) / Mathf.Max(0.01f, Flight);
                if (!Landed)
                {
                    if (t < 1f)
                    {
                        Vector3 p = ArcPoint(From, To, H, t);
                        Vector3 next = ArcPoint(From, To, H, Mathf.Min(1f, t + 0.02f));
                        Dart.transform.position = p;
                        if ((next - p).sqrMagnitude > 1e-8f) Dart.transform.rotation = Quaternion.LookRotation(next - p);
                        return true;
                    }
                    Landed = true;
                    LandAt = now;
                    // Stuck in the floor, tail up, at the angle it came in.
                    Vector3 fwd = To - ArcPoint(From, To, H, 0.95f);
                    if (fwd.sqrMagnitude > 1e-6f) Dart.transform.rotation = Quaternion.LookRotation(fwd.normalized);
                    Dart.transform.position = To + Vector3.up * 0.08f;
                    Blink.enabled = true;
                    fx.Play3D(fx.thunkClip, To, 0.9f, 2f, 30f);
                    fx.Play3D(fx.pingClip, To, 0.8f, 4f, 45f);
                }
                float since = now - LandAt;
                Blink.intensity = Mathf.PingPong(since * 6f, 1f) > 0.5f ? 1.6f : 0.2f;
                for (int i = 0; i < Rings.Length; i++)
                {
                    float k = (since - i * 0.22f) / 0.8f;
                    LineRenderer r = Rings[i];
                    if (k < 0f || k >= 1f)
                    {
                        if (r.enabled) r.enabled = false;
                        continue;
                    }
                    r.enabled = true;
                    float e = 1f - (1f - k) * (1f - k) * (1f - k);
                    r.transform.localScale = Vector3.one * Mathf.Max(0.01f, Radius * e);
                    Color c = Color;
                    c.a = 0.95f * (1f - k);
                    r.startColor = c;
                    r.endColor = c;
                    r.widthMultiplier = Mathf.Lerp(0.35f, 0.12f, k);
                }
                if (since > Hold)
                {
                    Kill();
                    return false;
                }
                return true;
            }

            public override void Kill()
            {
                if (Dart != null) Object.Destroy(Dart);
                if (Rings != null) foreach (LineRenderer r in Rings) if (r != null) Object.Destroy(r.gameObject);
            }
        }

        LineRenderer Circle(string name, Color c, float width, int segments)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = lineMat;
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                // Unit circle in the local XY plane; the 90 degree X rotation below lays it on the floor (local Y -> world Z).
                lr.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
            }
            lr.widthMultiplier = width;
            lr.startColor = c;
            lr.endColor = c;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            // TransformZ: the strip faces along the transform's Z, which the rotation points straight down, so it lies flat.
            lr.alignment = LineAlignment.TransformZ;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            return lr;
        }

        /// <summary>A recon dart thrown from `hand` that sticks at `floorPoint` and pulses a ring of `radius` after `fuseS`.</summary>
        public void Recon(Vector3 hand, Vector3 floorPoint, float fuseS, float radius, float revealS)
        {
            Vector3 to = new Vector3(floorPoint.x, 0.02f, floorPoint.z);
            Color c = Agents.AbilityColor(Ability.Recon);
            GameObject dart = Primitive(PrimitiveType.Cube, new Vector3(0.05f, 0.05f, 0.32f), dartMat, "Recon dart");
            dart.transform.position = hand;
            Trail(dart, lineMat, new Color(c.r, c.g, c.b, 0.8f), 0.04f, 0.2f);
            var lgo = new GameObject("Dart light");
            lgo.transform.SetParent(dart.transform, false);
            lgo.transform.localPosition = new Vector3(0f, 0f, -0.2f);
            Light l = lgo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = 3f;
            l.shadows = LightShadows.None;
            l.enabled = false;
            var rings = new LineRenderer[2];
            for (int i = 0; i < rings.Length; i++)
            {
                rings[i] = Circle("Recon pulse", c, 0.3f, 72);
                rings[i].transform.position = to + Vector3.up * (0.05f + i * 0.01f);
                rings[i].enabled = false;
            }
            Play3D(throwClip, hand, 0.5f, 2f, 25f, 0.8f);
            anims.Add(new ReconAnim
            {
                Born = Time.time, From = hand, To = to, Flight = Mathf.Max(0.1f, fuseS), H = ArcHeight(hand, to) * 0.7f,
                Radius = Mathf.Max(0.5f, radius), Hold = Mathf.Max(1.2f, revealS + 1f), Dart = dart, Blink = l, Rings = rings, Color = c,
            });
        }

        // ---- dash ------------------------------------------------------------------------------

        class StreakAnim : Anim
        {
            public LineRenderer[] Lines;
            public Color Color;
            public float Life;

            public override bool Step(AbilityFx fx, float now)
            {
                float k = (now - Born) / Life;
                if (k >= 1f)
                {
                    Kill();
                    return false;
                }
                foreach (LineRenderer l in Lines)
                {
                    Color c = Color;
                    c.a *= 1f - k;
                    l.startColor = new Color(c.r, c.g, c.b, 0f);
                    l.endColor = c;
                }
                return true;
            }

            public override void Kill()
            {
                foreach (LineRenderer l in Lines) if (l != null) Object.Destroy(l.gameObject);
            }
        }

        /// <summary>One segment of someone else's dash: wind streaks along the path, and a whoosh when it starts.</summary>
        public void DashSegment(Vector3 from, Vector3 to, bool starts)
        {
            if ((to - from).sqrMagnitude < 1e-4f) return;
            Color c = Agents.AbilityColor(Ability.Dash);
            c.a = 0.7f;
            Vector3 side = Vector3.Cross(Vector3.up, (to - from).normalized) * 0.25f;
            float[] ys = { 0.5f, 1.05f, 1.55f };
            var lines = new LineRenderer[ys.Length];
            for (int i = 0; i < ys.Length; i++)
            {
                var go = new GameObject("Dash streak");
                go.transform.SetParent(root, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = lineMat;
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                Vector3 off = side * (i - 1);
                lr.SetPosition(0, from + Vector3.up * ys[i] + off);
                lr.SetPosition(1, to + Vector3.up * ys[i] + off);
                lr.widthMultiplier = 0.04f;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lines[i] = lr;
            }
            anims.Add(new StreakAnim { Born = Time.time, Lines = lines, Color = c, Life = 0.35f });
            if (starts) Play3D(dashClip, from + Vector3.up, 0.9f, 2f, 30f);
        }

        // ---- aim marker --------------------------------------------------------------------------

        void BuildMarker()
        {
            marker = new GameObject("Aim marker");
            marker.transform.SetParent(root, false);
            markerRing = Circle("Aim ring", Color.white, 0.12f, 64);
            markerRing.transform.SetParent(marker.transform, true);
            markerDot = Circle("Aim dot", Color.white, 0.1f, 24);
            markerDot.transform.SetParent(marker.transform, true);

            var beamGo = new GameObject("Aim beam");
            beamGo.transform.SetParent(marker.transform, false);
            markerBeam = beamGo.AddComponent<LineRenderer>();
            markerBeam.sharedMaterial = lineMat;
            markerBeam.useWorldSpace = true;
            markerBeam.positionCount = 2;
            markerBeam.widthMultiplier = 0.05f;
            markerBeam.shadowCastingMode = ShadowCastingMode.Off;

            var arcGo = new GameObject("Aim arc");
            arcGo.transform.SetParent(marker.transform, false);
            markerArc = arcGo.AddComponent<LineRenderer>();
            markerArc.sharedMaterial = lineMat;
            markerArc.useWorldSpace = true;
            markerArc.positionCount = ArcPoints;
            markerArc.widthMultiplier = 0.025f;
            markerArc.shadowCastingMode = ShadowCastingMode.Off;
            marker.SetActive(false);
        }

        /// <summary>
        /// Shows where an aimed ability will land: a ring of its effect radius on the floor, a dot and a short beam at
        /// the point, and the throw's arc from the hand. Call every frame while the key is held.
        /// </summary>
        public void ShowMarker(Ability ability, Vector3 hand, Vector3 floorPoint, float effectRadius, bool clamped)
        {
            if (!marker.activeSelf) marker.SetActive(true);
            Color c = Agents.AbilityColor(ability);
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 8f);
            Color ringC = new Color(c.r, c.g, c.b, (clamped ? 0.55f : 0.85f) * pulse);
            Vector3 p = new Vector3(floorPoint.x, 0.04f, floorPoint.z);

            markerRing.transform.position = p;
            markerRing.transform.localScale = Vector3.one * Mathf.Max(0.3f, effectRadius);
            markerRing.startColor = markerRing.endColor = ringC;
            markerDot.transform.position = p + Vector3.up * 0.005f;
            markerDot.transform.localScale = Vector3.one * 0.3f;
            markerDot.startColor = markerDot.endColor = new Color(c.r, c.g, c.b, 0.95f);

            float top = ability == Ability.Flash ? FlashPopHeight : 1.2f;
            markerBeam.SetPosition(0, p);
            markerBeam.SetPosition(1, p + Vector3.up * top);
            markerBeam.startColor = new Color(c.r, c.g, c.b, 0.9f);
            markerBeam.endColor = new Color(c.r, c.g, c.b, 0f);

            Vector3 end = ability == Ability.Flash ? new Vector3(p.x, FlashPopHeight, p.z) : p;
            float h = ArcHeight(hand, end) * (ability == Ability.Recon ? 0.7f : 1f);
            for (int i = 0; i < ArcPoints; i++) markerArc.SetPosition(i, ArcPoint(hand, end, h, i / (float)(ArcPoints - 1)));
            markerArc.startColor = new Color(c.r, c.g, c.b, 0.05f);
            markerArc.endColor = new Color(c.r, c.g, c.b, 0.7f);
        }

        public void HideMarker()
        {
            if (marker != null && marker.activeSelf) marker.SetActive(false);
        }
    }
}

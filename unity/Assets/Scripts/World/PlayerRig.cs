// First-person camera, procedural gun viewmodel, muzzle flash, the client-side
// shot raycast and the smoke aim point. Lives on the main camera.
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    public struct ShotOutcome
    {
        public string TargetId;
        /// <summary>"head", "body" or "miss".</summary>
        public string Result;
        public Vector3 End;
    }

    [RequireComponent(typeof(Camera))]
    public class PlayerRig : MonoBehaviour
    {
        public const float EyeStand = 1.6f;
        public const float EyeCrouch = 1.1f;
        public const float MaxShotRange = 200f;
        public const float HorizontalFov = 103f;
        /// <summary>
        /// Aim points stay this far inside an ability's range: the sim checks the range after this tick's movement
        /// (up to 0.34 m at run speed) from a position the client sees one tick late.
        /// </summary>
        public const float RangeMarginM = 0.75f;

        public float Sensitivity = 1f;
        public Camera Cam { get; private set; }

        /// <summary>Unity heading (eulerAngles.y) in degrees.</summary>
        public float Heading;
        public float Pitch;

        float eye = EyeStand;
        float deathLerp;
        Transform viewmodel;
        Transform muzzle;
        GameObject flash;
        Light flashLight;
        float flashUntil;
        float recoil;
        float bobPhase;
        Vector3 lastGround;
        AudioSource audioSrc;
        AudioClip shotClip;
        AudioClip tickClip;

        void Awake()
        {
            Cam = GetComponent<Camera>();
            Cam.nearClipPlane = 0.03f;
            Cam.farClipPlane = 500f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = Palette.Sky;
            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false;
            audioSrc.spatialBlend = 0f;
            shotClip = Synth.Shot();
            tickClip = Synth.Tick();
            BuildViewmodel();
            SetViewmodelVisible(false);
        }

        void BuildViewmodel()
        {
            viewmodel = new GameObject("Viewmodel").transform;
            viewmodel.SetParent(transform, false);
            viewmodel.localPosition = new Vector3(0.2f, -0.2f, 0.42f);
            Material body = Mats.Lit(Palette.Gun);
            Material trim = Mats.Lit(new Color(0.85f, 0.86f, 0.9f));
            Material accent = Mats.Emissive(new Color(0.35f, 0.95f, 0.9f), 0.8f);
            Piece(PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.07f, 0.09f, 0.34f), body);
            Piece(PrimitiveType.Cube, new Vector3(0f, 0.005f, 0.24f), new Vector3(0.035f, 0.035f, 0.2f), trim);
            Piece(PrimitiveType.Cube, new Vector3(0f, -0.09f, -0.06f), new Vector3(0.05f, 0.12f, 0.06f), body).transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            Piece(PrimitiveType.Cube, new Vector3(0f, -0.08f, 0.07f), new Vector3(0.045f, 0.1f, 0.05f), trim);
            Piece(PrimitiveType.Cube, new Vector3(0f, 0.06f, 0.02f), new Vector3(0.025f, 0.03f, 0.09f), body);
            Piece(PrimitiveType.Cube, new Vector3(0.036f, 0.01f, 0.05f), new Vector3(0.004f, 0.02f, 0.2f), accent);
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(viewmodel, false);
            muzzle.localPosition = new Vector3(0f, 0.005f, 0.36f);
            flash = Piece(PrimitiveType.Sphere, new Vector3(0f, 0.005f, 0.38f), Vector3.one * 0.07f, Mats.Unlit(new Color(1f, 0.85f, 0.45f)));
            flash.name = "Muzzle flash";
            var lgo = new GameObject("Flash light");
            lgo.transform.SetParent(muzzle, false);
            flashLight = lgo.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.8f, 0.5f);
            flashLight.range = 6f;
            flashLight.intensity = 2.5f;
            flashLight.shadows = LightShadows.None;
            flash.SetActive(false);
            flashLight.enabled = false;
        }

        GameObject Piece(PrimitiveType t, Vector3 pos, Vector3 scale, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(t);
            go.transform.SetParent(viewmodel, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Collider c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
            return go;
        }

        public void SetViewmodelVisible(bool v)
        {
            if (viewmodel != null && viewmodel.gameObject.activeSelf != v) viewmodel.gameObject.SetActive(v);
        }

        public void ResetFor(AgentState me)
        {
            Heading = me != null ? Geo.YawToHeading(me.yaw) : 90f;
            Pitch = 0f;
            eye = EyeStand;
            deathLerp = 0f;
            recoil = 0f;
            fovKick = 0f;
            if (me != null) lastGround = Geo.ToWorld(me.pos);
        }

        public void Look(Vector2 deltaDeg)
        {
            float s = Settings.RigSensitivity;
            Heading = Mathf.Repeat(Heading + deltaDeg.x * s, 360f);
            Pitch = Mathf.Clamp(Pitch - deltaDeg.y * s, -85f, 85f);
        }

        /// <summary>The sim yaw for the current look direction (spec: atan2(f.z, f.x) of the flattened forward).</summary>
        public float SimYaw
        {
            get
            {
                Vector3 f = Quaternion.Euler(0f, Heading, 0f) * Vector3.forward;
                return Geo.DirToYaw(f);
            }
        }

        /// <summary>Sim-frame move vector for WASD relative to the look direction, length at most 1.</summary>
        public Vector2 MoveVector(float fwd, float right)
        {
            float yaw = SimYaw;
            var f = new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw));
            var r = new Vector2(Mathf.Sin(yaw), -Mathf.Cos(yaw));
            Vector2 m = f * fwd + r * right;
            return m.sqrMagnitude > 1f ? m.normalized : m;
        }

        /// <summary>Places the eye over the interpolated sim position. Call every frame during a drill.</summary>
        public void Place(Vector3 ground, bool crouch, bool alive)
        {
            float target = crouch ? EyeCrouch : EyeStand;
            eye = Mathf.MoveTowards(eye, target, Time.deltaTime * 4f);
            deathLerp = Mathf.MoveTowards(deathLerp, alive ? 0f : 1f, Time.deltaTime * 2f);

            float speed = Time.deltaTime > 0f ? (new Vector3(ground.x - lastGround.x, 0f, ground.z - lastGround.z)).magnitude / Time.deltaTime : 0f;
            lastGround = ground;
            float moving = Mathf.Clamp01(speed / 6f);
            bobPhase += Time.deltaTime * Mathf.Lerp(0f, 11f, moving);
            float bob = Mathf.Sin(bobPhase) * 0.025f * moving;

            float y = Mathf.Lerp(eye + bob, 0.35f, deathLerp);
            transform.position = new Vector3(ground.x, y, ground.z);
            transform.rotation = Quaternion.Euler(Pitch - recoil * 1.2f, Heading, 25f * deathLerp);

            recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 9f);
            if (viewmodel != null)
            {
                viewmodel.localPosition = new Vector3(0.2f + Mathf.Cos(bobPhase * 0.5f) * 0.006f * moving, -0.2f + bob * 0.4f, 0.42f - recoil * 0.06f);
                viewmodel.localRotation = Quaternion.Euler(-recoil * 6f, 0f, 0f);
            }
            if (flash.activeSelf && Time.time >= flashUntil)
            {
                flash.SetActive(false);
                flashLight.enabled = false;
            }
        }

        /// <summary>Overview camera for the menus: a slow orbit above the map.</summary>
        public void Orbit(MapDef map)
        {
            float w = map?.bounds != null && map.bounds.width > 0 ? map.bounds.width : 110f;
            float h = map?.bounds != null && map.bounds.height > 0 ? map.bounds.height : 80f;
            var c = new Vector3(w / 2f, 0f, h / 2f);
            float a = Time.time * 0.05f;
            float r = Mathf.Max(w, h) * 0.75f;
            transform.position = c + new Vector3(Mathf.Cos(a) * r, Mathf.Max(w, h) * 0.45f, Mathf.Sin(a) * r);
            transform.rotation = Quaternion.LookRotation(c - transform.position, Vector3.up);
        }

        void LateUpdate()
        {
            fovKick = Mathf.MoveTowards(fovKick, 0f, Time.deltaTime / 0.35f);
            // A quick widen that eases back: the dash's sense of speed.
            float kick = DashFovKickDeg * fovKick * fovKick;
            if (Cam != null) Cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(Mathf.Min(150f, Settings.FovH + kick), Mathf.Max(0.1f, Cam.aspect));
        }

        public const float DashFovKickDeg = 16f;
        float fovKick;

        /// <summary>Starts the dash FOV kick (1 = full, eases out over ~0.35 s).</summary>
        public void DashKick()
        {
            fovKick = 1f;
        }

        /// <summary>0..1, how much of the dash kick is left (the HUD draws wind streaks with it).</summary>
        public float DashKickAmount => fovKick;

        /// <summary>Current recoil (0 at rest, up to 1.5 while firing), for the crosshair's dynamic spread.</summary>
        public float Recoil => recoil;

        public Ray AimRay()
        {
            return Cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        }

        /// <summary>
        /// The shot, per docs/unity-bridge.md: a ray from the camera through the crosshair against walls and bot
        /// colliders. Head sphere = head, body capsule = body; walls, teammates or nothing = miss, reported against
        /// the living enemy closest to the aim direction.
        /// </summary>
        public ShotOutcome Fire(WorldView world, string myTeam)
        {
            Physics.SyncTransforms();
            Ray ray = AimRay();
            var outcome = new ShotOutcome { Result = "miss", TargetId = "", End = ray.origin + ray.direction * 80f };
            if (Physics.Raycast(ray, out RaycastHit hit, MaxShotRange, ~0, QueryTriggerInteraction.Ignore))
            {
                outcome.End = hit.point;
                BotPart part = hit.collider.GetComponent<BotPart>();
                if (part != null && part.Bot != null && part.Bot.Alive && part.Bot.Team != myTeam)
                {
                    bool head = part.Head;
                    // The head sphere sits inside the capsule's top, so the ray meets the capsule first: test the head too.
                    if (!head && part.Bot.HeadCollider != null && part.Bot.HeadCollider.enabled)
                        head = part.Bot.HeadCollider.Raycast(ray, out RaycastHit _, MaxShotRange);
                    outcome.TargetId = part.Bot.Id;
                    outcome.Result = head ? "head" : "body";
                }
            }
            if (outcome.Result == "miss") outcome.TargetId = NearestAimed(world, myTeam, ray);

            recoil = Mathf.Min(1.5f, recoil + 0.6f);
            flash.SetActive(true);
            flash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            flash.transform.localScale = Vector3.one * Random.Range(0.05f, 0.09f);
            flashLight.enabled = true;
            flashUntil = Time.time + 0.05f;
            audioSrc.PlayOneShot(shotClip, 0.7f);
            world.Tracer(muzzle.position, outcome.End, new Color(1f, 0.92f, 0.7f, 0.9f), 0.07f);
            return outcome;
        }

        public void PlayHitTick()
        {
            audioSrc.PlayOneShot(tickClip, 0.8f);
        }

        static string NearestAimed(WorldView world, string myTeam, Ray ray)
        {
            string best = "";
            float bestAngle = float.MaxValue;
            foreach (BotView b in world.Bots.Values)
            {
                if (!b.Alive || b.Team == myTeam) continue;
                float ang = Vector3.Angle(ray.direction, b.ChestPos - ray.origin);
                if (ang < bestAngle)
                {
                    bestAngle = ang;
                    best = b.Id;
                }
            }
            return best;
        }

        /// <summary>
        /// Where an aimed ability thrown now lands: the floor point under the crosshair (or under the wall it hits),
        /// pulled back to within `maxRange` of the player and kept inside the map. Returns sim coordinates;
        /// `clamped` is true when the crosshair point was further than the range.
        /// </summary>
        public Vector2 AimPoint(Vector2 player, float maxRange, MapDef map, out bool clamped)
        {
            Physics.SyncTransforms();
            Ray ray = AimRay();
            Vector3 p;
            if (Physics.Raycast(ray, out RaycastHit hit, 400f, ~0, QueryTriggerInteraction.Ignore))
            {
                p = hit.point;
                // On a wall face, step back off it so the point is on open floor, not inside the wall.
                var n = new Vector3(hit.normal.x, 0f, hit.normal.z);
                if (n.sqrMagnitude > 0.25f) p += n.normalized * 0.4f;
            }
            else if (ray.direction.y < -1e-3f)
                p = ray.origin + ray.direction * (-ray.origin.y / ray.direction.y);
            else
            {
                Vector3 f = ray.direction;
                f.y = 0f;
                p = ray.origin + (f.sqrMagnitude > 1e-6f ? f.normalized : transform.forward) * maxRange;
            }
            var target = new Vector2(p.x, p.z);
            Vector2 d = target - player;
            float limit = Mathf.Max(0f, maxRange - RangeMarginM);
            clamped = d.magnitude > limit;
            if (clamped) target = player + d.normalized * limit;
            if (map?.bounds != null && map.bounds.width > 0)
            {
                target.x = Mathf.Clamp(target.x, 0f, map.bounds.width);
                target.y = Mathf.Clamp(target.y, 0f, map.bounds.height);
            }
            return target;
        }
    }
}

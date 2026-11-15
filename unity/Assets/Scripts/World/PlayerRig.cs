// First-person camera, the weapon viewmodel (Viewmodel), the client-side shot
// raycast with spray, spread and pellets, camera recoil and the smoke aim
// point. Lives on the main camera.
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
        /// <summary>Extra sensitivity factor (the scope sets it).</summary>
        public float SensScale = 1f;
        public Camera Cam { get; private set; }
        public Viewmodel Weapon { get; private set; }
        public Glow Bloom { get; private set; }
        /// <summary>The player's weapons (set by GameController).</summary>
        public Gunplay Gun;

        /// <summary>Unity heading (eulerAngles.y) in degrees.</summary>
        public float Heading;
        public float Pitch;

        float eye = EyeStand;
        float deathLerp;
        float recoil;
        float shake;
        float bobPhase;
        Vector3 lastGround;
        Vector2 lastLook;
        AudioSource audioSrc;
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
            tickClip = Synth.Tick();
            Weapon = gameObject.AddComponent<Viewmodel>();
            Weapon.Init(Cam);
            Bloom = gameObject.AddComponent<Glow>();
            SetViewmodelVisible(false);
        }

        public void SetViewmodelVisible(bool v)
        {
            if (Weapon != null) Weapon.SetVisible(v);
        }

        public void ResetFor(AgentState me)
        {
            Heading = me != null ? Geo.YawToHeading(me.yaw) : 90f;
            Pitch = 0f;
            eye = EyeStand;
            deathLerp = 0f;
            recoil = 0f;
            shake = 0f;
            fovKick = 0f;
            if (me != null) lastGround = Geo.ToWorld(me.pos);
        }

        public void Look(Vector2 deltaDeg)
        {
            float s = Settings.RigSensitivity * SensScale;
            if (Settings.InvertY) deltaDeg.y = -deltaDeg.y;
            lastLook = deltaDeg;
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
            float dt = Time.deltaTime;
            float target = crouch ? EyeCrouch : EyeStand;
            eye = Mathf.MoveTowards(eye, target, dt * 4f);
            deathLerp = Mathf.MoveTowards(deathLerp, alive ? 0f : 1f, dt * 2f);

            float speed = dt > 0f ? (new Vector3(ground.x - lastGround.x, 0f, ground.z - lastGround.z)).magnitude / dt : 0f;
            lastGround = ground;
            float moving = Mathf.Clamp01(speed / 6f);
            // No camera bob: the eye stays level while moving, so the world never wobbles under the crosshair.
            float bob = 0f;

            Vector2 kick = Gun != null ? Gun.Kick : Vector2.zero;
            float y = Mathf.Lerp(eye + bob, 0.35f, deathLerp);
            transform.position = new Vector3(ground.x, y, ground.z);
            transform.rotation = Quaternion.Euler(Pitch - kick.x - shake, Heading + kick.y, 25f * deathLerp);

            recoil = Mathf.MoveTowards(recoil, 0f, dt * 9f);
            shake = Mathf.MoveTowards(shake, 0f, dt * 30f);
            if (Weapon != null) Weapon.Frame(dt, moving, crouch, lastLook);
            lastLook = Vector2.zero;
        }

        /// <summary>Overview camera for the menus: a slow, low cinematic orbit over the map that drifts in height and aim.</summary>
        public void Orbit(MapDef map)
        {
            float w = map?.bounds != null && map.bounds.width > 0 ? map.bounds.width : 110f;
            float h = map?.bounds != null && map.bounds.height > 0 ? map.bounds.height : 80f;
            var c = new Vector3(w / 2f, 0f, h / 2f);
            float t = Time.unscaledTime;
            float a = t * 0.035f;
            float size = Mathf.Max(w, h);
            float r = size * (0.62f + 0.06f * Mathf.Sin(t * 0.05f));
            float y = size * (0.26f + 0.05f * Mathf.Sin(t * 0.07f + 1f));
            transform.position = c + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            // Look a little past the centre, so the map sits right of the menu column.
            Vector3 look = c + new Vector3(Mathf.Cos(a + 1.9f) * size * 0.12f, 0f, Mathf.Sin(a + 1.9f) * size * 0.12f);
            transform.rotation = Quaternion.LookRotation(look - transform.position, Vector3.up);
        }

        void LateUpdate()
        {
            fovKick = Mathf.MoveTowards(fovKick, 0f, Time.deltaTime / 0.35f);
            // A quick widen that eases back: the dash's sense of speed.
            float kick = DashFovKickDeg * fovKick * fovKick;
            float h = Mathf.Min(150f, Settings.FovH + kick);
            float scope = Gun != null ? Gun.Scope01 : 0f;
            if (scope > 0f)
            {
                float t0 = Mathf.Tan(h * 0.5f * Mathf.Deg2Rad);
                float t1 = t0 / Gunplay.ScopeZoom;
                h = 2f * Mathf.Atan(Mathf.Lerp(t0, t1, scope)) * Mathf.Rad2Deg;
            }
            if (Cam != null) Cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(h, Mathf.Max(0.1f, Cam.aspect));
        }

        /// <summary>Sensitivity factor while scoped: the zoom's ratio times the scoped-sensitivity setting.</summary>
        public float ScopeSensFactor()
        {
            float scope = Gun != null ? Gun.Scope01 : 0f;
            if (scope <= 0.5f) return 1f;
            return Settings.ScopedSens / Gunplay.ScopeZoom;
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

        /// <summary>The aim direction (without the camera kick) turned by a spray/spread offset (x pitch up, y yaw right).</summary>
        Vector3 ShotDir(Vector2 off)
        {
            return Quaternion.Euler(Pitch - off.x, Heading + off.y, 0f) * Vector3.forward;
        }

        /// <summary>
        /// The shot, per docs/unity-bridge.md and docs/weapons-economy.md: rays from the eye along the aim plus this
        /// bullet's spray offset and spread (one per pellet), against walls and bot colliders. Head sphere = head, body
        /// capsule = body; walls, teammates or nothing = miss, reported against the living enemy closest to the aim
        /// direction. For pellets the target hit most is reported, "head" if any pellet found its head.
        /// </summary>
        public ShotOutcome Fire(WorldView world, string myTeam, Gunplay gun)
        {
            Physics.SyncTransforms();
            WeaponInfo w = gun.Weapon;
            Vector3 origin = transform.position;
            Vector3 muzzle = Weapon != null ? Weapon.MuzzleWorld(transform) : origin;
            var outcome = new ShotOutcome { Result = "miss", TargetId = "", End = origin + ShotDir(Vector2.zero) * 80f };
            string bestId = null;
            int bestHits = 0;
            bool bestHead = false;
            string curId = null;
            int curHits = 0;
            bool curHead = false;
            int n = Mathf.Max(1, gun.OffsetCount);
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = ShotDir(gun.Offsets[i]);
                var ray = new Ray(origin, dir);
                Vector3 end = origin + dir * 80f;
                if (Physics.Raycast(ray, out RaycastHit hit, MaxShotRange, ~0, QueryTriggerInteraction.Ignore))
                {
                    end = hit.point;
                    BotPart part = hit.collider.GetComponent<BotPart>();
                    if (part != null && part.Bot != null)
                    {
                        if (part.Bot.Alive && part.Bot.Team != myTeam)
                        {
                            bool head = part.Head;
                            // The head sphere sits inside the capsule's top, so the ray meets the capsule first: test the head too.
                            if (!head && part.Bot.HeadCollider != null && part.Bot.HeadCollider.enabled)
                                head = part.Bot.HeadCollider.Raycast(ray, out RaycastHit _, MaxShotRange);
                            // Tally per target (pellets): the first target, or a second one, keeps a count.
                            if (curId == null || curId == part.Bot.Id) { curId = part.Bot.Id; curHits++; curHead |= head; }
                            else if (bestId == null || bestId == part.Bot.Id) { bestId = part.Bot.Id; bestHits++; bestHead |= head; }
                            world.Impacts?.BodyHit(hit.point, dir, Palette.Relation(part.Bot.Team, myTeam));
                        }
                    }
                    else world.Impacts?.Impact(hit.point, hit.normal, dir);
                }
                if (i == 0) outcome.End = end;
                // One tracer per trigger pull for single-bullet guns; every other pellet for the Brick.
                if (i == 0 || (n > 1 && i % 2 == 0))
                    world.Tracer(muzzle, end, new Color(1f, 0.9f, 0.62f, n > 1 ? 0.55f : 0.9f), n > 1 ? 0.05f : 0.07f, true);
            }
            if (bestId != null && (bestHits > curHits || (bestHits == curHits && bestHead && !curHead)))
            {
                curId = bestId;
                curHead = bestHead;
            }
            if (curId != null)
            {
                outcome.TargetId = curId;
                outcome.Result = curHead ? "head" : "body";
            }
            else outcome.TargetId = NearestAimed(world, myTeam, new Ray(origin, ShotDir(Vector2.zero)));

            recoil = Mathf.Min(1.5f, recoil + (w.Auto ? 0.45f : 0.8f));
            // No screen shake on firing: recoil (Gun.Kick) already moves the aim, and shake on top made the view wobble.
            if (Weapon != null) Weapon.OnShot(w);
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

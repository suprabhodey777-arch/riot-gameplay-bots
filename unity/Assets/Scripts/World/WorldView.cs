// Everything in the world that the sim moves: bots (interpolated between
// steps), smokes, the spike and shot tracers. Read-only view of SimState.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    /// <summary>Marks a bot collider so the shot raycast knows what it hit.</summary>
    public class BotPart : MonoBehaviour
    {
        public BotView Bot;
        public bool Head;
    }

    public class BotView
    {
        public const float Radius = 0.4f;
        public const float Height = 1.8f;
        public const float HeadY = 1.65f;
        public const float HeadRadius = 0.15f;

        public string Id;
        public string Team;
        public string Archetype;
        public string Agent;
        public string Thought = "";
        public bool Alive = true;
        public bool Revealed;
        public bool Blind;
        public GameObject Root;
        public CapsuleCollider BodyCollider;
        public SphereCollider HeadCollider;

        Transform tilt;
        float deathStart = -1f;
        GameObject revealShell;
        Transform dazed;
        float blindSince;
        MeshRenderer bodyR, headR, bandR;
        /// <summary>Cached HUD strings (built once per bot, or when their input changes).</summary>
        public string LabelLine;
        public string ThoughtShown;
        string thoughtSource;
        public int RevealLabelM = -1;
        public string RevealLabel;

        /// <summary>The bot's thought with underscores as spaces, rebuilt only when it changes.</summary>
        public string ThoughtText()
        {
            if (!ReferenceEquals(thoughtSource, Thought))
            {
                thoughtSource = Thought;
                ThoughtShown = string.IsNullOrEmpty(Thought) ? "" : Thought.Replace('_', ' ');
            }
            return ThoughtShown;
        }

        /// <summary>Re-tints body, head and band (enemy highlight or ally colour from the settings).</summary>
        public void Recolor(Color tc)
        {
            if (bodyR != null) bodyR.sharedMaterial = Mats.Lit(Color.Lerp(tc, Color.white, 0.15f));
            if (headR != null) headR.sharedMaterial = Mats.Lit(Color.Lerp(tc, Color.white, 0.55f));
            if (bandR != null) bandR.sharedMaterial = Mats.Lit(tc * 0.8f);
        }

        public Vector3 HeadPos => Root.transform.position + Vector3.up * HeadY;
        public Vector3 ChestPos => Root.transform.position + Vector3.up * 1.2f;

        static GameObject Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            if (!keepCollider)
            {
                Collider c = go.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }
            return go;
        }

        public BotView(string id, string team, string archetype, string agent, Transform parent, Material revealMat, Color tc)
        {
            Id = id;
            Team = team;
            Archetype = archetype;
            Agent = agent;
            Root = new GameObject("Bot " + id);
            Root.transform.SetParent(parent, false);
            tilt = new GameObject("Tilt").transform;
            tilt.SetParent(Root.transform, false);

            // Capsule primitive: radius 0.5, height 2. Scaled to radius 0.4, height 1.8.
            GameObject body = Part(PrimitiveType.Capsule, tilt, new Vector3(0f, Height / 2f, 0f), new Vector3(Radius * 2f, Height / 2f, Radius * 2f), Mats.Lit(Color.Lerp(tc, Color.white, 0.15f)), true);
            body.name = "Body";
            BodyCollider = body.GetComponent<CapsuleCollider>();
            body.AddComponent<BotPart>().Bot = this;
            bodyR = body.GetComponent<MeshRenderer>();

            // Sphere primitive: radius 0.5. Scaled to radius 0.15 at y 1.65.
            GameObject head = Part(PrimitiveType.Sphere, tilt, new Vector3(0f, HeadY, 0f), Vector3.one * HeadRadius * 2f, Mats.Lit(Color.Lerp(tc, Color.white, 0.55f)), true);
            head.name = "Head";
            HeadCollider = head.GetComponent<SphereCollider>();
            headR = head.GetComponent<MeshRenderer>();
            var hp = head.AddComponent<BotPart>();
            hp.Bot = this;
            hp.Head = true;

            Material dark = Mats.Lit(new Color(0.18f, 0.19f, 0.23f));
            Part(PrimitiveType.Cube, tilt, new Vector3(0f, HeadY + 0.01f, 0.13f), new Vector3(0.22f, 0.07f, 0.06f), Mats.Emissive(new Color(0.95f, 0.98f, 1f), 0.6f), false).name = "Visor";
            GameObject bandGo = Part(PrimitiveType.Cube, tilt, new Vector3(0f, 1.25f, 0.0f), new Vector3(0.84f, 0.12f, 0.84f), Mats.Lit(tc * 0.8f), false);
            bandGo.name = "Band";
            bandR = bandGo.GetComponent<MeshRenderer>();
            Part(PrimitiveType.Cube, tilt, new Vector3(0.18f, 1.25f, 0.5f), new Vector3(0.08f, 0.1f, 0.55f), dark, false).name = "Gun";
            // The pack carries the agent's colour, so Rook, Haze, Wren and Pike read apart at a glance.
            Material pack = string.IsNullOrEmpty(agent) ? dark : Mats.Lit(Color.Lerp(Agents.ColorOf(agent), dark.color, 0.25f));
            Part(PrimitiveType.Cube, tilt, new Vector3(0f, 1.45f, -0.4f), new Vector3(0.4f, 0.45f, 0.14f), pack, false).name = "Pack";

            // Recon silhouette: a slightly larger shell drawn through walls, off until revealed.
            revealShell = new GameObject("Reveal");
            revealShell.transform.SetParent(tilt, false);
            Shell(PrimitiveType.Capsule, revealShell.transform, new Vector3(0f, Height / 2f, 0f), new Vector3(Radius * 2.15f, Height / 2f * 1.04f, Radius * 2.15f), revealMat);
            Shell(PrimitiveType.Sphere, revealShell.transform, new Vector3(0f, HeadY, 0f), Vector3.one * HeadRadius * 2.5f, revealMat);
            revealShell.SetActive(false);

            // Dazed: small glints circling the head while blinded.
            dazed = new GameObject("Dazed").transform;
            dazed.SetParent(tilt, false);
            dazed.localPosition = new Vector3(0f, HeadY + 0.32f, 0f);
            Material glint = Mats.Emissive(new Color(1f, 0.96f, 0.7f), 2.5f);
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                Shell(PrimitiveType.Sphere, dazed, new Vector3(Mathf.Cos(a) * 0.22f, 0f, Mathf.Sin(a) * 0.22f), Vector3.one * 0.06f, glint);
            }
            dazed.gameObject.SetActive(false);
        }

        static void Shell(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            GameObject go = Part(type, parent, pos, scale, m, false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>Shows or hides the through-walls silhouette and the dazed glints. Call every frame.</summary>
        public void SetEffects(bool revealed, bool blind)
        {
            bool showReveal = revealed && Alive;
            if (revealShell != null && revealShell.activeSelf != showReveal) revealShell.SetActive(showReveal);
            Revealed = revealed;
            bool showDaze = blind && Alive;
            if (showDaze && !Blind) blindSince = Time.time;
            Blind = blind;
            if (dazed == null) return;
            if (dazed.gameObject.activeSelf != showDaze) dazed.gameObject.SetActive(showDaze);
            if (showDaze)
            {
                float t = Time.time - blindSince;
                dazed.localRotation = Quaternion.Euler(Mathf.Sin(t * 5f) * 12f, t * 320f, 0f);
                dazed.localScale = Vector3.one * (0.8f + 0.2f * Mathf.Sin(t * 9f));
            }
        }

        public void SetPose(Vector3 pos, float headingDeg)
        {
            Root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, headingDeg, 0f));
        }

        public void SetAlive(bool alive)
        {
            if (alive == Alive) return;
            Alive = alive;
            BodyCollider.enabled = alive;
            HeadCollider.enabled = alive;
            if (!alive) deathStart = Time.time;
            else
            {
                deathStart = -1f;
                tilt.localRotation = Quaternion.identity;
                tilt.localPosition = Vector3.zero;
            }
        }

        public void Animate()
        {
            if (Alive || deathStart < 0f) return;
            float t = Mathf.Clamp01((Time.time - deathStart) / 0.55f);
            float e = t * t * (3f - 2f * t);
            // Falls backwards around the feet and settles on the floor.
            tilt.localRotation = Quaternion.Euler(-88f * e, 0f, 12f * e);
            tilt.localPosition = new Vector3(0f, Radius * e * 0.9f, -0.1f * e);
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
        }
    }

    class SmokeView
    {
        public GameObject Go;
        public Material Mat;
        public float Born;
        public SmokeState State;
    }

    class TracerView
    {
        public LineRenderer Line;
        public float Born;
        public float Life;
        public Color Color;
    }

    /// <summary>Runs before GameController so the shot raycast sees the bots where this frame draws them.</summary>
    [DefaultExecutionOrder(-50)]
    public class WorldView : MonoBehaviour
    {
        public SimState Prev;
        public SimState Curr;
        public float TickInterval = 0.05f;
        public string PlayerId = "player";
        public string PlayerTeam = "attack";
        public readonly Dictionary<string, BotView> Bots = new Dictionary<string, BotView>();

        float stepTime;
        readonly Dictionary<string, AgentState> prevMap = new Dictionary<string, AgentState>();
        readonly Dictionary<string, AgentState> currMap = new Dictionary<string, AgentState>();
        readonly Dictionary<string, SmokeView> smokes = new Dictionary<string, SmokeView>();
        readonly List<TracerView> tracers = new List<TracerView>();
        /// <summary>Reveal timers from `revealed` events, in case a state lacks revealedUntilTick.</summary>
        readonly Dictionary<string, int> revealedByEvent = new Dictionary<string, int>();
        Transform botsRoot;
        Transform fxRoot;
        Material revealMat;
        static readonly Color RevealColor = new Color(1f, 0.25f, 0.42f, 0.95f);

        GameObject spike;
        Light spikeLight;
        AudioSource spikeAudio;
        AudioClip beep;
        Material spikeGlow;
        float nextBeep;
        float pulse;
        Material lineMat;

        static readonly Color SpikeGlowColor = Palette.Spike;

        void Awake()
        {
            botsRoot = new GameObject("Bots").transform;
            fxRoot = new GameObject("Effects").transform;
            lineMat = Mats.Line();
            revealMat = Mats.Reveal(RevealColor);
            BuildSpike();
        }

        void BuildSpike()
        {
            spike = new GameObject("Spike");
            spike.transform.SetParent(fxRoot, false);
            Material shell = Mats.Lit(new Color(0.25f, 0.26f, 0.30f));
            spikeGlow = Mats.Emissive(SpikeGlowColor, 1.5f);
            Make(PrimitiveType.Cylinder, spike.transform, new Vector3(0f, 0.08f, 0f), new Vector3(0.36f, 0.08f, 0.36f), shell);
            Make(PrimitiveType.Cylinder, spike.transform, new Vector3(0f, 0.2f, 0f), new Vector3(0.22f, 0.06f, 0.22f), spikeGlow);
            Make(PrimitiveType.Cube, spike.transform, new Vector3(0f, 0.34f, 0f), new Vector3(0.06f, 0.18f, 0.06f), spikeGlow);
            var lgo = new GameObject("Spike light");
            lgo.transform.SetParent(spike.transform, false);
            lgo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            spikeLight = lgo.AddComponent<Light>();
            spikeLight.type = LightType.Point;
            spikeLight.color = SpikeGlowColor;
            spikeLight.range = 5f;
            spikeLight.intensity = 1.5f;
            spikeLight.shadows = LightShadows.None;
            spikeAudio = spike.AddComponent<AudioSource>();
            spikeAudio.playOnAwake = false;
            spikeAudio.spatialBlend = 1f;
            spikeAudio.rolloffMode = AudioRolloffMode.Linear;
            spikeAudio.minDistance = 4f;
            spikeAudio.maxDistance = 70f;
            beep = Synth.Beep();
            spike.SetActive(false);
        }

        static GameObject Make(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Collider c = go.GetComponent<Collider>();
            if (c != null) Object.Destroy(c);
            return go;
        }

        public void Clear()
        {
            foreach (BotView b in Bots.Values) b.Destroy();
            Bots.Clear();
            foreach (SmokeView s in smokes.Values)
            {
                Object.Destroy(s.Go);
                Object.Destroy(s.Mat);
            }
            smokes.Clear();
            revealedByEvent.Clear();
            Prev = Curr = null;
            prevMap.Clear();
            currMap.Clear();
            if (spike != null) spike.SetActive(false);
        }

        public void StartDrill(DrillMsg d, float tickInterval)
        {
            Clear();
            TickInterval = tickInterval;
            PlayerId = d.playerId ?? "player";
            PlayerTeam = d.playerTeam ?? "attack";
            var arch = new Dictionary<string, string>();
            var agentOf = new Dictionary<string, string>();
            if (d.bots != null)
                foreach (DrillBotInfo b in d.bots)
                {
                    arch[b.id] = b.archetype;
                    if (!string.IsNullOrEmpty(b.agent)) agentOf[b.id] = b.agent;
                }
            if (d.spec != null && d.spec.bots != null)
                foreach (DrillBotSpec b in d.spec.bots)
                    if (!arch.ContainsKey(b.id)) arch[b.id] = b.archetypeId;
            SimState s = d.state ?? new SimState();
            foreach (AgentState a in s.agents)
            {
                if (a.id == PlayerId) continue;
                arch.TryGetValue(a.id, out string archetype);
                if (!agentOf.TryGetValue(a.id, out string agent)) agent = a.agent;
                var bv = new BotView(a.id, a.team, archetype ?? "bot", agent, botsRoot, revealMat, Palette.Relation(a.team, PlayerTeam));
                bv.LabelLine = GameController.ArchetypeName(bv.Archetype) + (string.IsNullOrEmpty(agent) ? "" : "  |  " + Agents.NameOf(agent));
                bv.SetPose(Geo.ToWorld(a.pos), Geo.YawToHeading(a.yaw));
                Bots[a.id] = bv;
            }
            Prev = s;
            Curr = s;
            Index(prevMap, s);
            Index(currMap, s);
            stepTime = Time.time;
            SyncAlive();
        }

        static void Index(Dictionary<string, AgentState> map, SimState s)
        {
            map.Clear();
            if (s?.agents == null) return;
            foreach (AgentState a in s.agents) map[a.id] = a;
        }

        public void ApplyStep(SimState s, Dictionary<string, string> thoughts)
        {
            if (s == null) return;
            Prev = Curr ?? s;
            Curr = s;
            Index(prevMap, Prev);
            Index(currMap, Curr);
            stepTime = Time.time;
            if (thoughts != null)
                foreach (KeyValuePair<string, string> kv in thoughts)
                    if (Bots.TryGetValue(kv.Key, out BotView b)) b.Thought = kv.Value ?? "";
            SyncAlive();
        }

        void SyncAlive()
        {
            foreach (BotView b in Bots.Values)
                if (currMap.TryGetValue(b.Id, out AgentState a)) b.SetAlive(a.alive);
        }

        /// <summary>0 at the moment a step arrived, 1 one tick later.</summary>
        public float Alpha => TickInterval <= 0f ? 1f : Mathf.Clamp01((Time.time - stepTime) / TickInterval);

        public AgentState Agent(string id)
        {
            return id != null && currMap.TryGetValue(id, out AgentState a) ? a : null;
        }

        /// <summary>The sim tick with the interpolation fraction added, for smooth countdowns.</summary>
        public float TickNow => Curr == null ? 0f : Curr.tick + Alpha;

        public void NoteRevealed(string id, int untilTick)
        {
            if (string.IsNullOrEmpty(id)) return;
            revealedByEvent.TryGetValue(id, out int old);
            revealedByEvent[id] = Mathf.Max(old, untilTick);
        }

        /// <summary>Is this agent revealed by recon right now (state timer, or a `revealed` event)?</summary>
        public bool IsRevealed(string id)
        {
            if (Curr == null || id == null) return false;
            AgentState a = Agent(id);
            if (a != null && a.revealedUntilTick > Curr.tick) return true;
            return revealedByEvent.TryGetValue(id, out int until) && until > Curr.tick;
        }

        public bool IsBlind(string id)
        {
            AgentState a = Agent(id);
            return a != null && Curr != null && a.blindUntilTick > Curr.tick;
        }

        /// <summary>Where the agent's hand is: the bot's chest, or for the player a point below and right of the eye.</summary>
        public Vector3 HandPos(string id, Transform playerEye)
        {
            if (id == PlayerId && playerEye != null)
                return playerEye.position + playerEye.right * 0.18f - playerEye.up * 0.15f + playerEye.forward * 0.3f;
            return InterpPos(id) + Vector3.up * 1.4f;
        }

        /// <summary>Interpolated sim position of an agent, in Unity floor coordinates.</summary>
        public Vector3 InterpPos(string id)
        {
            if (id == null || !currMap.TryGetValue(id, out AgentState c)) return Vector3.zero;
            if (!prevMap.TryGetValue(id, out AgentState p)) p = c;
            return Vector3.Lerp(Geo.ToWorld(p.pos), Geo.ToWorld(c.pos), Alpha);
        }

        float InterpHeading(string id)
        {
            if (!currMap.TryGetValue(id, out AgentState c)) return 0f;
            if (!prevMap.TryGetValue(id, out AgentState p)) p = c;
            return Mathf.LerpAngle(Geo.YawToHeading(p.yaw), Geo.YawToHeading(c.yaw), Alpha);
        }

        public void Tracer(Vector3 from, Vector3 to, Color color, float life)
        {
            TracerView t = null;
            foreach (TracerView x in tracers)
                if (!x.Line.gameObject.activeSelf) { t = x; break; }
            if (t == null)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(fxRoot, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = lineMat;
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.widthMultiplier = 0.03f;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                t = new TracerView { Line = lr };
                tracers.Add(t);
            }
            t.Line.gameObject.SetActive(true);
            t.Line.SetPosition(0, from);
            t.Line.SetPosition(1, to);
            t.Born = Time.time;
            t.Life = life;
            t.Color = color;
        }

        /// <summary>How deep `p` is inside a live smoke, 0 (outside) to 1 (centre).</summary>
        public float SmokeDensityAt(Vector3 p)
        {
            float best = 0f;
            foreach (SmokeView s in smokes.Values)
            {
                Vector3 c = Geo.ToWorld(s.State.pos, 0f);
                float r = Mathf.Max(0.1f, s.State.radius);
                float d = new Vector2(p.x - c.x, p.z - c.z).magnitude / r;
                if (d < 1f) best = Mathf.Max(best, Mathf.Clamp01((1f - d) * 2.5f) * s.Mat.color.a / 0.82f);
            }
            return Mathf.Clamp01(best);
        }

        /// <summary>Does the segment pass through any live smoke?</summary>
        public bool SmokeBlocks(Vector3 a, Vector3 b)
        {
            if (Curr?.smokes == null) return false;
            foreach (SmokeState s in Curr.smokes)
            {
                // The sim's smoke is a circle on the floor plan; test it as a tall cylinder.
                Vector3 a2 = new Vector3(a.x, 0f, a.z), b2 = new Vector3(b.x, 0f, b.z);
                if (Geo.SegmentHitsSphere(a2, b2, Geo.ToWorld(s.pos), s.radius)) return true;
            }
            return false;
        }

        void Update()
        {
            if (Curr == null) return;
            if (colorVersion != Settings.Version)
            {
                colorVersion = Settings.Version;
                foreach (BotView b in Bots.Values) b.Recolor(Palette.Relation(b.Team, PlayerTeam));
            }
            foreach (BotView b in Bots.Values)
            {
                if (!currMap.ContainsKey(b.Id)) continue;
                if (b.Alive) b.SetPose(InterpPos(b.Id), InterpHeading(b.Id));
                b.Animate();
                // Only enemies revealed for the player's team are drawn through walls.
                b.SetEffects(b.Team != PlayerTeam && IsRevealed(b.Id), IsBlind(b.Id));
            }
            if (revealMat != null)
            {
                Color rc = Settings.Enemy;
                rc.a = RevealColor.a * (0.75f + 0.25f * Mathf.Sin(Time.time * 10f));
                revealMat.color = rc;
            }
            UpdateSmokes();
            UpdateSpike();
            UpdateTracers();
        }

        int colorVersion = -1;
        readonly HashSet<string> liveSmokes = new HashSet<string>();
        readonly List<string> goneSmokes = new List<string>();

        void UpdateSmokes()
        {
            HashSet<string> live = liveSmokes;
            live.Clear();
            float tickRate = Curr.tickRate > 0 ? Curr.tickRate : 20f;
            if (Curr.smokes != null)
            {
                foreach (SmokeState s in Curr.smokes)
                {
                    live.Add(s.id);
                    if (!smokes.TryGetValue(s.id, out SmokeView v))
                    {
                        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        go.name = "Smoke " + s.id;
                        go.transform.SetParent(fxRoot, false);
                        // Immediate, so this frame's shot raycast cannot hit the cloud.
                        Collider c = go.GetComponent<Collider>();
                        if (c != null) Object.DestroyImmediate(c);
                        var mr = go.GetComponent<MeshRenderer>();
                        Material m = Mats.Transparent(new Color(Palette.Smoke.r, Palette.Smoke.g, Palette.Smoke.b, 0f), 3200);
                        mr.sharedMaterial = m;
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        v = new SmokeView { Go = go, Mat = m, Born = Time.time };
                        smokes[s.id] = v;
                    }
                    v.State = s;
                    float grow = Mathf.Clamp01((Time.time - v.Born) / 0.5f);
                    float leftS = (s.endsAtTick - (Curr.tick + Alpha)) / tickRate;
                    float alpha = 0.82f * Mathf.Clamp01(leftS / 1.5f) * Mathf.Clamp01((Time.time - v.Born) / 0.35f);
                    float d = s.radius * 2f * Mathf.Lerp(0.55f, 1f, 1f - (1f - grow) * (1f - grow));
                    // A dome centred on the floor: its footprint is exactly the sim's smoke circle.
                    v.Go.transform.localPosition = Geo.ToWorld(s.pos, 0f);
                    v.Go.transform.localScale = new Vector3(d, d, d);
                    Color col = v.Mat.color;
                    col.a = alpha;
                    v.Mat.color = col;
                }
            }
            goneSmokes.Clear();
            foreach (string id in smokes.Keys)
                if (!live.Contains(id)) goneSmokes.Add(id);
            foreach (string id in goneSmokes)
            {
                Object.Destroy(smokes[id].Go);
                Object.Destroy(smokes[id].Mat);
                smokes.Remove(id);
            }
        }

        void UpdateSpike()
        {
            SpikeState sp = Curr.spike;
            bool planted = sp != null && sp.status == "planted" && sp.pos != null;
            bool dropped = sp != null && sp.status == "dropped" && sp.pos != null;
            if (!planted && !dropped)
            {
                if (spike.activeSelf) spike.SetActive(false);
                return;
            }
            if (!spike.activeSelf)
            {
                spike.SetActive(true);
                nextBeep = Time.time;
            }
            spike.transform.position = Geo.ToWorld(sp.pos, 0f);
            pulse = Mathf.Max(0f, pulse - Time.deltaTime * 4f);
            if (planted)
            {
                float left = Mathf.Max(0f, Curr.roundTimeLeftS);
                if (Time.time >= nextBeep && Curr.phase != "ended")
                {
                    spikeAudio.PlayOneShot(beep);
                    pulse = 1f;
                    nextBeep = Time.time + Mathf.Lerp(0.12f, 1.0f, Mathf.Clamp01(left / 45f));
                }
                spikeLight.intensity = 0.8f + pulse * 4f;
                spikeGlow.SetColor("_EmissionColor", SpikeGlowColor * (0.6f + pulse * 3f));
            }
            else
            {
                spike.transform.Rotate(0f, 60f * Time.deltaTime, 0f, Space.World);
                spikeLight.intensity = 1.2f + Mathf.Sin(Time.time * 3f) * 0.3f;
                spikeGlow.SetColor("_EmissionColor", SpikeGlowColor * 1.2f);
            }
        }

        void UpdateTracers()
        {
            foreach (TracerView t in tracers)
            {
                if (!t.Line.gameObject.activeSelf) continue;
                float k = (Time.time - t.Born) / Mathf.Max(0.01f, t.Life);
                if (k >= 1f)
                {
                    t.Line.gameObject.SetActive(false);
                    continue;
                }
                Color c = t.Color;
                c.a *= 1f - k;
                t.Line.startColor = c;
                t.Line.endColor = new Color(c.r, c.g, c.b, c.a * 0.4f);
            }
        }
    }
}

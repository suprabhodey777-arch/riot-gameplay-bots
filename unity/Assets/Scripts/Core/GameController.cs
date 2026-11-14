// The client's flow: connect, pick a sample player, report, drill intro, play,
// result. Owns the bridge link and the fixed 50 ms input loop. The bridge
// decides everything; this only shows the state and sends what the player does.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks
{
    public enum AppScreen
    {
        Connect,
        Choose,
        Report,
        Intro,
        Playing,
        Result,
    }

    public class FeedItem
    {
        public string Text;
        public Color Color;
        public float At;
    }

    public struct Choice
    {
        public string Id;
        public string Label;
        public string Blurb;

        public Choice(string id, string label, string blurb)
        {
            Id = id;
            Label = label;
            Blurb = blurb;
        }
    }

    public class GameController : MonoBehaviour
    {
        public const int SampleSeed = 11;

        public static readonly Choice[] Samples =
        {
            new Choice("entry", "Weak entry", "Dies in opening duels."),
            new Choice("clutch", "Weak clutch", "Loses 1vX situations."),
            new Choice("time_pressure", "Weak time pressure", "Slow plants and defuses late in the round."),
            new Choice("smoke_usage", "Weak smoke usage", "Smokes too late or off target."),
        };

        public static readonly Choice[] Archetypes =
        {
            new Choice("baseline", "Baseline", "Average aim, average timing, no adaptation."),
            new Choice("entry-duelist", "Entry Duelist", "Swings first and fast, little patience."),
            new Choice("patient-lurker", "Patient Lurker", "Waits away from the pack and commits late."),
            new Choice("off-angle-anchor", "Off-Angle Anchor", "Holds a site from an unusual angle and stays put."),
            new Choice("utility-controller", "Utility Controller", "Smokes the choke before it commits."),
        };

        public static readonly string[] Skills = { "entry", "clutch", "time_pressure", "smoke_usage" };

        // ---- state the UI reads -------------------------------------------------------------
        public AppScreen Screen { get; private set; } = AppScreen.Connect;
        public BridgeClient Client { get; private set; }
        public WorldView World { get; private set; }
        public PlayerRig Rig { get; private set; }
        public MapDef Map { get; private set; }
        public Rules Rules { get; private set; } = new Rules();
        public ReportMsg Report { get; private set; }
        public DrillMsg Drill { get; private set; }
        public ResultMsg Result { get; private set; }
        public DebriefMsg Debrief { get; private set; }
        public float ResultAt { get; private set; }
        public string WaitingFor { get; private set; }
        public bool Paused { get; private set; }
        public bool DrillDone { get; private set; }
        public string PlayerId { get; private set; } = "player";
        public string PlayerTeam { get; private set; } = "attack";
        public readonly List<FeedItem> Feed = new List<FeedItem>();
        public string Toast { get; private set; }
        public float ToastUntil { get; private set; }
        public float HitMarkerAt { get; private set; } = -10f;
        public bool HitMarkerHead { get; private set; }
        public bool HitMarkerKill { get; private set; }
        public float DamageAt { get; private set; } = -10f;
        public float VerdictUntil { get; private set; }
        public int PlantTicks { get; private set; }
        public int VersusSkill = 0;

        public AgentState Me => World != null ? World.Agent(PlayerId) : null;
        public SimState State => World != null ? World.Curr : null;
        public bool Connected => Client != null && Client.State == LinkState.Connected;
        /// <summary>Link state and error, sampled once per frame so IMGUI's layout and repaint passes agree.</summary>
        public LinkState Link { get; private set; } = LinkState.Idle;
        public string LinkError { get; private set; } = "";

        // ---- internals ----------------------------------------------------------------------
        readonly Queue<Action> pending = new Queue<Action>();
        LinkState lastLink = LinkState.Idle;
        GameObject mapRoot;
        Font font;
        Material textMat;
        float sendAcc;
        int ticksSent;
        int lastFireTick = -1000;
        bool fireArmed;
        bool lastSentPlantHeld;
        ShotOutcome? pendingFire;
        Vector2? pendingSmoke;
        bool abandoning;

        /// <summary>Runs `a` at the start of the next Update (keeps IMGUI layout passes consistent).</summary>
        public void Do(Action a)
        {
            if (a != null) pending.Enqueue(a);
        }

        void Awake()
        {
            Application.runInBackground = true;
            SetupCamera();
            SetupLighting();
            World = new GameObject("World").AddComponent<WorldView>();
            font = Mats.DefaultFont();
            textMat = Mats.WorldText(font);
            Font.textureRebuilt += OnFontTextureRebuilt;
            var ui = gameObject.AddComponent<ClientUI>();
            ui.Game = this;
            Client = new BridgeClient();
        }

        void Start()
        {
            ConnectNow();
        }

        void OnDestroy()
        {
            Font.textureRebuilt -= OnFontTextureRebuilt;
            Client?.Dispose();
        }

        void OnApplicationQuit()
        {
            Client?.Dispose();
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && Screen == AppScreen.Playing && !DrillDone) Paused = true;
        }

        void OnFontTextureRebuilt(Font f)
        {
            if (f == font && textMat != null) textMat.mainTexture = f.material.mainTexture;
        }

        void SetupCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            foreach (Camera c in FindObjectsByType<Camera>())
                if (c != cam) c.enabled = false;
            if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            foreach (AudioListener l in FindObjectsByType<AudioListener>())
                if (l.gameObject != cam.gameObject) l.enabled = false;
            PlayerRig rig = cam.GetComponent<PlayerRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<PlayerRig>();
            Rig = rig;
        }

        static void SetupLighting()
        {
            foreach (Light l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional) l.enabled = false;
            var sunGo = new GameObject("Sun");
            sunGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.80f, 0.84f, 0.92f);
            RenderSettings.ambientEquatorColor = new Color(0.74f, 0.72f, 0.70f);
            RenderSettings.ambientGroundColor = new Color(0.48f, 0.46f, 0.45f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.011f;
            RenderSettings.fogColor = Palette.Fog;
            QualitySettings.shadowDistance = 70f;
        }

        // ---- actions (called through Do from the UI) ------------------------------------------
        public void ConnectNow()
        {
            Screen = AppScreen.Connect;
            WaitingFor = null;
            Client.Connect(BridgeClient.DefaultHost, BridgeClient.PortFromEnv());
        }

        public void ChooseSample(string skill)
        {
            Send(Wire.LoadSample(skill, SampleSeed), "report");
        }

        public void RequestNextDrill()
        {
            Send(Wire.NextDrill(), "drill");
        }

        public void RequestRehearsal(int index)
        {
            Send(Wire.Rehearse(index), "drill");
        }

        public void RequestVersus(string archetype)
        {
            Send(Wire.Versus(archetype, Skills[Mathf.Clamp(VersusSkill, 0, Skills.Length - 1)]), "drill");
        }

        public void Begin()
        {
            if (Screen != AppScreen.Intro || Drill == null) return;
            Screen = AppScreen.Playing;
            Paused = false;
            sendAcc = 0f;
            fireArmed = false;
        }

        public void Resume()
        {
            if (Screen != AppScreen.Playing) return;
            Paused = false;
            sendAcc = 0f;
            fireArmed = false;
        }

        public void AbandonDrill()
        {
            if (Drill == null || DrillDone) return;
            abandoning = true;
            DrillDone = true;
            Send(Wire.Abandon(), "result");
        }

        public void BackToReport()
        {
            Screen = Report != null ? AppScreen.Report : AppScreen.Choose;
        }

        public void BackToChoose()
        {
            Screen = AppScreen.Choose;
        }

        void Send(JObject msg, string waitFor)
        {
            if (Client.Send(msg)) WaitingFor = waitFor;
            else ShowToast("Not connected to the bridge.");
        }

        public void ShowToast(string text)
        {
            Toast = text;
            ToastUntil = Time.unscaledTime + 5f;
        }

        // ---- frame ----------------------------------------------------------------------------
        void Update()
        {
            while (pending.Count > 0) pending.Dequeue()();
            CheckLink();
            int guard = 0;
            while (guard++ < 500 && Client.TryDequeue(out JObject msg)) Handle(msg);

            switch (Screen)
            {
                case AppScreen.Playing:
                    TickPlaying();
                    break;
                case AppScreen.Intro:
                    if (GameInput.Pressed(GameKey.Confirm)) Begin();
                    AgentState me = Me;
                    Rig.SetViewmodelVisible(true);
                    Rig.Place(World.InterpPos(PlayerId), false, me == null || me.alive);
                    break;
                default:
                    Rig.SetViewmodelVisible(false);
                    Rig.Orbit(Map);
                    break;
            }
            bool lockIt = Screen == AppScreen.Playing && !Paused && Result == null;
            Cursor.lockState = lockIt ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockIt;
        }

        void CheckLink()
        {
            LinkState s = Client.State;
            Link = s;
            LinkError = Client.LastError;
            if (s == lastLink) return;
            lastLink = s;
            if (s == LinkState.Failed || s == LinkState.Closed || s == LinkState.Connecting)
            {
                Screen = AppScreen.Connect;
                Paused = false;
                WaitingFor = null;
                if (s != LinkState.Connecting)
                {
                    Drill = null;
                    Result = null;
                    World.Clear();
                }
            }
        }

        void TickPlaying()
        {
            if (!DrillDone && GameInput.Pressed(GameKey.Pause))
            {
                if (Paused) Resume();
                else Paused = true;
            }
            if (!Paused && !fireArmed && !GameInput.FireHeld) fireArmed = true;

            AgentState me = Me;
            bool alive = me != null && me.alive;
            bool active = !Paused && !DrillDone && Connected;
            if (active && alive) Rig.Look(GameInput.LookDelta());
            bool crouch = active && alive && GameInput.Held(GameKey.Crouch);
            Rig.Place(World.InterpPos(PlayerId), crouch, alive);
            Rig.SetViewmodelVisible(alive);

            if (active && alive)
            {
                QueueFire();
                QueueSmoke(me);
            }
            if (active) SendLoop();

            if (Result != null && Time.unscaledTime >= VerdictUntil) Screen = AppScreen.Result;
        }

        void QueueFire()
        {
            if (!fireArmed || !GameInput.FireHeld || pendingFire.HasValue) return;
            int needTicks = Mathf.Max(1, Mathf.CeilToInt(Rules.fireIntervalS * Rules.tickRate - 0.001f));
            // The shot goes out with the next tick; keep sent shots at least fireIntervalS apart in sim time.
            if (ticksSent + 1 - lastFireTick < needTicks) return;
            pendingFire = Rig.Fire(World, PlayerTeam);
            lastFireTick = ticksSent + 1;
        }

        void QueueSmoke(AgentState me)
        {
            if (pendingSmoke.HasValue || me.smokesLeft <= 0) return;
            if (!GameInput.Pressed(GameKey.SmokeQ) && !GameInput.Pressed(GameKey.SmokeE)) return;
            pendingSmoke = Rig.SmokePoint(new Vector2(me.pos.x, me.pos.y), Rules.smokeMaxRangeM, Map);
        }

        void SendLoop()
        {
            float dt = 1f / Mathf.Max(1f, Rules.tickRate);
            sendAcc += Time.unscaledDeltaTime;
            // After a hitch, catch up a few ticks at most rather than flooding the bridge.
            if (sendAcc > dt * 4f) sendAcc = dt * 4f;
            while (sendAcc >= dt && !DrillDone)
            {
                sendAcc -= dt;
                SendTick();
            }
        }

        void SendTick()
        {
            AgentState me = Me;
            bool alive = me != null && me.alive;
            var input = new PlayerInputMsg();
            float fwd = (GameInput.Held(GameKey.Forward) ? 1f : 0f) - (GameInput.Held(GameKey.Back) ? 1f : 0f);
            float right = (GameInput.Held(GameKey.Right) ? 1f : 0f) - (GameInput.Held(GameKey.Left) ? 1f : 0f);
            Vector2 mv = alive ? Rig.MoveVector(fwd, right) : Vector2.zero;
            input.moveX = mv.x;
            input.moveY = mv.y;
            input.yaw = Rig.SimYaw;
            input.walk = alive && GameInput.Held(GameKey.Walk);
            input.crouch = alive && GameInput.Held(GameKey.Crouch);
            bool use = alive && GameInput.Held(GameKey.Use);
            input.plantHeld = use && PlayerTeam == "attack";
            input.defuseHeld = use && PlayerTeam == "defense";
            if (pendingFire.HasValue)
            {
                input.fireTargetId = pendingFire.Value.TargetId ?? "";
                input.fireResult = pendingFire.Value.Result;
                pendingFire = null;
            }
            if (pendingSmoke.HasValue)
            {
                input.hasSmoke = true;
                input.smokeX = pendingSmoke.Value.x;
                input.smokeY = pendingSmoke.Value.y;
                pendingSmoke = null;
            }
            if (Client.Send(Wire.Input(input)))
            {
                ticksSent++;
                lastSentPlantHeld = input.plantHeld;
            }
        }

        // ---- messages ---------------------------------------------------------------------------
        void Handle(JObject o)
        {
            string type = (string)o["type"];
            try
            {
                switch (type)
                {
                    case "hello": OnHello(Wire.Read<HelloMsg>(o)); break;
                    case "report": OnReport(Wire.Read<ReportMsg>(o)); break;
                    case "drill": OnDrill(Wire.Read<DrillMsg>(o)); break;
                    case "step": OnStep(Wire.Read<StepMsg>(o)); break;
                    case "result": OnResult(Wire.Read<ResultMsg>(o)); break;
                    case "debrief": Debrief = Wire.Read<DebriefMsg>(o); break;
                    case "error":
                        ErrorMsg e = Wire.Read<ErrorMsg>(o);
                        // Inputs already in flight when a drill ends are answered with errors; those are expected.
                        if (!(DrillDone && Screen == AppScreen.Playing)) ShowToast("Bridge: " + (e?.message ?? "error"));
                        if (WaitingFor != "result") WaitingFor = null;
                        break;
                    default:
                        Debug.Log("Saltworks: ignoring message type " + type);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Saltworks: could not handle '" + type + "': " + ex);
                ShowToast("Could not read a '" + type + "' message (see Console).");
            }
        }

        void OnHello(HelloMsg h)
        {
            if (h.protocol != Wire.Protocol) ShowToast("The bridge speaks " + h.protocol + ", this client expects " + Wire.Protocol + ".");
            Rules = h.rules ?? new Rules();
            if (Rules.tickRate <= 0f) Rules.tickRate = 20f;
            if (h.map != null)
            {
                Map = h.map;
                if (mapRoot != null) Destroy(mapRoot);
                mapRoot = MapBuilder.Build(Map, font, textMat);
                textMat.mainTexture = font.material.mainTexture;
            }
            World.Clear();
            Report = null;
            Drill = null;
            Result = null;
            Debrief = null;
            WaitingFor = null;
            Screen = AppScreen.Choose;
        }

        void OnReport(ReportMsg r)
        {
            bool first = Report == null;
            Report = r;
            if (first && r?.report?.findings != null && r.report.findings.Count > 0)
            {
                int i = Array.IndexOf(Skills, r.report.findings[0].skill);
                if (i >= 0) VersusSkill = i;
            }
            if (WaitingFor == "report") WaitingFor = null;
            if (Screen == AppScreen.Choose) Screen = AppScreen.Report;
        }

        void OnDrill(DrillMsg d)
        {
            Drill = d;
            Result = null;
            Debrief = null;
            DrillDone = false;
            Paused = false;
            abandoning = false;
            Feed.Clear();
            PlantTicks = 0;
            pendingFire = null;
            pendingSmoke = null;
            ticksSent = 0;
            lastFireTick = -1000;
            sendAcc = 0f;
            HitMarkerAt = -10f;
            DamageAt = -10f;
            PlayerId = d.playerId ?? d.spec?.player?.id ?? "player";
            PlayerTeam = d.playerTeam ?? d.spec?.player?.team ?? "attack";
            World.StartDrill(d, 1f / Rules.tickRate);
            Rig.ResetFor(World.Agent(PlayerId));
            WaitingFor = null;
            Screen = AppScreen.Intro;
        }

        void OnStep(StepMsg st)
        {
            if (Drill == null || st.state == null) return;
            World.ApplyStep(st.state, st.thoughts);
            if (st.events != null) foreach (SimEvent e in st.events) OnEvent(e);
            UpdatePlant();
            if (st.done) DrillDone = true;
        }

        void OnResult(ResultMsg r)
        {
            Result = r;
            ResultAt = Time.unscaledTime;
            DrillDone = true;
            if (WaitingFor == "result") WaitingFor = null;
            if (Screen == AppScreen.Playing && !abandoning)
                VerdictUntil = Time.unscaledTime + 2.5f;
            else
            {
                VerdictUntil = Time.unscaledTime;
                Screen = AppScreen.Result;
            }
            abandoning = false;
        }

        void UpdatePlant()
        {
            AgentState me = Me;
            SimState s = State;
            bool eligible = PlayerTeam == "attack" && me != null && me.alive && me.carryingSpike && OnSite(me.pos)
                            && s?.spike != null && s.spike.status == "carried";
            PlantTicks = eligible && lastSentPlantHeld ? PlantTicks + 1 : 0;
        }

        public bool OnSite(Vec2 p)
        {
            if (Map == null || p == null) return false;
            var v = new Vector2(p.x, p.y);
            foreach (KeyValuePair<string, List<Vec2>> site in Map.SiteList())
                if (Geo.PointInPolygon(v, site.Value)) return true;
            return false;
        }

        public bool NearPlantedSpike(Vec2 p, float range = 2f)
        {
            SpikeState sp = State?.spike;
            if (p == null || sp == null || sp.status != "planted" || sp.pos == null) return false;
            return new Vector2(p.x - sp.pos.x, p.y - sp.pos.y).magnitude <= range;
        }

        void OnEvent(SimEvent e)
        {
            float now = Time.unscaledTime;
            switch (e.type)
            {
                case "damage":
                    if (e.attacker == PlayerId)
                    {
                        HitMarkerAt = now;
                        HitMarkerHead = e.part == "head";
                        HitMarkerKill = false;
                        Rig.PlayHitTick();
                    }
                    if (e.victim == PlayerId) DamageAt = now;
                    if (e.attacker != PlayerId && e.attackerPos != null && e.victimPos != null)
                    {
                        AgentState a = World.Agent(e.attacker);
                        Color c = Palette.Team(a != null ? a.team : "defense");
                        World.Tracer(Geo.ToWorld(e.attackerPos, 1.45f), Geo.ToWorld(e.victimPos, 1.3f), new Color(c.r, c.g, c.b, 0.9f), 0.12f);
                    }
                    break;
                case "kill":
                    if (e.killer == PlayerId)
                    {
                        HitMarkerAt = now;
                        HitMarkerKill = true;
                        HitMarkerHead = e.part == "head";
                    }
                    if (e.victim == PlayerId) DamageAt = now;
                    AddFeed(Name(e.killer) + "  killed  " + Name(e.victim) + (e.part == "head" ? "  (headshot)" : ""), TeamColorOf(e.killer));
                    break;
                case "smoke":
                    AddFeed(Name(e.agent) + " threw a smoke", new Color(0.85f, 0.83f, 0.95f));
                    break;
                case "plant":
                    AddFeed(Name(e.agent) + " planted the spike on " + e.site, Palette.Spike);
                    break;
                case "defuse":
                    AddFeed(Name(e.agent) + " defused the spike", Palette.Defense);
                    break;
                case "round_end":
                    AddFeed("Round over: " + e.winner + " wins (" + e.reason + ")", Color.white);
                    break;
            }
        }

        void AddFeed(string text, Color c)
        {
            Feed.Add(new FeedItem { Text = text, Color = c, At = Time.unscaledTime });
            if (Feed.Count > 8) Feed.RemoveAt(0);
        }

        Color TeamColorOf(string id)
        {
            AgentState a = World.Agent(id);
            return Palette.Team(a != null ? a.team : "attack");
        }

        public string Name(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            if (id == PlayerId) return "You";
            if (World.Bots.TryGetValue(id, out BotView b)) return id + " " + ArchetypeName(b.Archetype);
            return id;
        }

        public static string ArchetypeName(string id)
        {
            foreach (Choice c in Archetypes)
                if (c.Id == id) return c.Label;
            return id ?? "";
        }

        public static string SkillName(string skill)
        {
            return string.IsNullOrEmpty(skill) ? "" : skill.Replace('_', ' ');
        }
    }
}

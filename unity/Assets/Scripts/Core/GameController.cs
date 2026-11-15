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
        MapSelect,
        AgentSelect,
        Report,
        Intro,
        Playing,
        Result,
        // round 4
        Menu,
        MatchSetup,
        Buy,
        MatchEnd,
        // round 5
        PersonaSelect,
        DuelSetup,
        DuelEnd,
    }

    public class FeedItem
    {
        public string Text;
        public Color Color;
        public float At;
        /// <summary>Measured text width (virtual pixels), cached by the HUD; -1 until measured.</summary>
        public float Width = -1f;
        // A kill: drawn as "killer [weapon icon] (headshot) victim".
        public bool IsKill;
        public string Killer;
        public string Victim;
        public string Weapon;
        public bool Head;
        /// <summary>The kill went through a wall (the event's flag, else the floor plan).</summary>
        public bool Wallbang;
        public Color KillerColor;
        public Color VictimColor;
        public float KillerW = -1f;
        public float VictimW = -1f;
    }

    /// <summary>A floating damage number over a hit bot (the damage-numbers setting).</summary>
    public struct DamageNumber
    {
        public Vector3 World;
        public int Amount;
        public bool Head;
        public float At;
        public string Text;
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

    public partial class GameController : MonoBehaviour
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

        // ---- round 2: maps, agents, abilities -----------------------------------------------------
        /// <summary>The bridge speaks bots-wire.2: maps, agents and abilities are available.</summary>
        public bool V2 { get; private set; }
        /// <summary>From the `maps` answer; null until it arrives.</summary>
        public List<MapInfo> Maps { get; private set; }
        /// <summary>The map and agent picked this session (re-sent after a reconnect). Null until picked.</summary>
        public string PickedMapId { get; private set; }
        public string PickedAgentId { get; private set; }
        /// <summary>The bridge's answer to pick_agent.</summary>
        public AbilityCharges PickedKit { get; private set; }
        /// <summary>The agent the player has in the current drill (a drill may override the pick).</summary>
        public string PlayerAgent { get; private set; }
        public AbilityFx Fx { get; private set; }
        /// <summary>The aimed ability whose key is held, if any.</summary>
        public Ability? Aiming { get; private set; }
        public bool AimClamped { get; private set; }
        /// <summary>Per ability: when the player last pressed its key with no charge left (the HUD flashes the slot).</summary>
        public readonly float[] DeniedAt = { -10f, -10f, -10f, -10f };
        public readonly float[] UsedAt = { -10f, -10f, -10f, -10f };
        public float RevealedAt { get; private set; } = -10f;
        /// <summary>Movement mode for the HUD: "run", "walk" or "crouch"; Moving is true while a move key is held.</summary>
        public string MoveMode { get; private set; } = "run";
        public bool Moving { get; private set; }

        public string MapName => Map != null ? (Map.name ?? Map.id ?? "") : "";

        // ---- round 3: prediction, settings, minimap -------------------------------------------------
        /// <summary>The map's walls on the floor plan (movement prediction, line of sight). Null before a map.</summary>
        public Walls2D Walls { get; private set; }
        public PlayerPredictor Predictor { get; } = new PlayerPredictor();
        public Minimap Minimap { get; } = new Minimap();
        /// <summary>Where the player's eye is drawn this frame (predicted while alive), sim coordinates.</summary>
        public Vector2 EyePos { get; private set; }
        public bool SettingsOpen { get; private set; }
        /// <summary>The full map is on screen: the tactical map (M) is open, or Tab is held to peek.</summary>
        public bool FullMap { get; private set; }
        /// <summary>The tactical map overlay is open (M toggles it, Esc closes it).</summary>
        public bool MapOpen { get; private set; }
        /// <summary>Tab is held during a drill (a peek at the full map).</summary>
        public bool MapPeek { get; private set; }
        /// <summary>Smoothed frames per second and the worst frame time of the last second (ms), for F3.</summary>
        public float Fps { get; private set; }
        public float WorstMs { get; private set; }
        public float LastShotAt { get; private set; } = -10f;
        /// <summary>The bridge's advice: main and practice agent ids (null when the bridge sent none).</summary>
        public string AdviceMain => Report?.agentAdvice?.main?.agent?.id;
        public string AdvicePractice => Report?.agentAdvice?.practice?.agent?.id;

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
        Vector2? pendingFlash;
        Vector2? pendingRecon;
        bool pendingDash;
        bool abandoning;
        /// <summary>0: the bots-wire.3 hello is out; 1: fell back to .2; 2: fell back to .1.</summary>
        int helloFallback;
        bool mapChosen;
        bool agentChosen;
        int blindSeenUntil;
        int blindTotalTicks = 1;
        readonly Dictionary<string, int> lastDashTick = new Dictionary<string, int>();
        readonly List<List<Vec2>> sitePolys = new List<List<Vec2>>();
        /// <summary>Movement accumulated since the last sent tick (metres, sim frame).</summary>
        Vector2 tickDisp;
        float fpsAcc;
        int fpsFrames;
        float worstAcc;
        const string PrefMap = "saltworks.map";
        const string PrefAgent = "saltworks.agent";

        /// <summary>The map and agent last picked on this machine, to highlight on the select screens.</summary>
        public string RememberedMap => PlayerPrefs.GetString(PrefMap, "");
        public string RememberedAgent => PlayerPrefs.GetString(PrefAgent, "");

        /// <summary>Runs `a` at the start of the next Update (keeps IMGUI layout passes consistent).</summary>
        public void Do(Action a)
        {
            if (a != null) pending.Enqueue(a);
        }

        void Awake()
        {
            Application.runInBackground = true;
            Settings.Load();
            Settings.ApplyEngine();
            SetupCamera();
            SetupLighting();
            WorldLook.Setup(Rig.Cam);
            World = new GameObject("World").AddComponent<WorldView>();
            Fx = World.gameObject.AddComponent<AbilityFx>();
            SetupRound5();
            Rig.Gun = Gun;
            if (Rig.Weapon != null) Rig.Weapon.Gun = Gun;
            if (Rig.Weapon != null) Rig.Weapon.Impacts = World.Impacts;
            ApplyGraphics();
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
            StartTourIfAsked();
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
            if (!focus && Screen == AppScreen.Playing && !DrillDone && !TourActive) Paused = true;
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

        /// <summary>Turns off scene directional lights; WorldLook creates the sun, sky, ambient and fog.</summary>
        static void SetupLighting()
        {
            foreach (Light l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional) l.enabled = false;
        }

        // ---- actions (called through Do from the UI) ------------------------------------------
        public void ConnectNow()
        {
            Screen = AppScreen.Connect;
            WaitingFor = null;
            helloFallback = 0;
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

        public void RequestRetry()
        {
            Send(Wire.Retry(), "drill");
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
            tickDisp = Vector2.zero;
            fireArmed = false;
        }

        public void Resume()
        {
            if (Screen != AppScreen.Playing && Screen != AppScreen.Buy) return;
            SettingsOpen = false;
            Paused = false;
            // sendAcc and tickDisp are kept: the eye was drawn with them, so resuming does not jump.
            fireArmed = false;
        }

        /// <summary>Esc on the buy screen: the pause overlay (Resume / Settings / Leave match), as in a round.</summary>
        public void TogglePauseMenu()
        {
            if (Screen != AppScreen.Playing && Screen != AppScreen.Buy) return;
            if (Paused) Resume();
            else Paused = true;
        }

        public void OpenSettings()
        {
            SettingsOpen = true;
            if (Screen == AppScreen.Playing && !DrillDone) Paused = true;
        }

        public void CloseSettings()
        {
            if (!SettingsOpen) return;
            SettingsOpen = false;
            Settings.Save();
        }

        /// <summary>Opens or closes the tactical map (only during a round).</summary>
        public void ToggleMap()
        {
            MapOpen = !MapOpen && Screen == AppScreen.Playing;
        }

        public void CloseMap()
        {
            MapOpen = false;
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

        /// <summary>The main menu (after the hello, and from the setup screens).</summary>
        public void OpenMenu()
        {
            if (Screen == AppScreen.Connect) return;
            Screen = AppScreen.Menu;
        }

        /// <summary>Practice: the coaching flow (sample player, map, agent, report, drills).</summary>
        public void OpenPractice()
        {
            RehearseFocus = false;
            Screen = Report != null ? AppScreen.Report : AppScreen.Choose;
        }

        /// <summary>Rehearse: the report's rehearsals (a sample player is picked first if there is no report yet).</summary>
        public void OpenRehearse()
        {
            RehearseFocus = true;
            Screen = Report != null ? AppScreen.Report : AppScreen.Choose;
        }

        /// <summary>Set when the menu's Rehearse opened the report, so it can scroll to the rehearsals.</summary>
        public bool RehearseFocus;

        /// <summary>After a report: the map choice, then the agent select, then the report (each only once per session).</summary>
        AppScreen NextSetupScreen()
        {
            if (V2 && !mapChosen) return AppScreen.MapSelect;
            if (V2 && !agentChosen) return AppScreen.AgentSelect;
            return Report != null ? AppScreen.Report : AppScreen.Choose;
        }

        public void OpenMapSelect()
        {
            if (!V2) return;
            if (Maps == null) Client.Send(Wire.Maps());
            Screen = AppScreen.MapSelect;
        }

        public void OpenAgentSelect()
        {
            if (V2) Screen = AppScreen.AgentSelect;
        }

        public void PickMap(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            Send(Wire.PickMap(id), "map");
        }

        /// <summary>Keeps the map the bridge already sent (used when the map list is missing or failed).</summary>
        public void KeepCurrentMap()
        {
            mapChosen = true;
            Screen = NextSetupScreen();
        }

        public void PickAgent(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            Send(Wire.PickAgent(id), "agent");
        }

        public void SkipAgent()
        {
            agentChosen = true;
            Screen = NextSetupScreen();
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
            WorldLook.Apply();
            TrackFps();
            UpdateGraphics();
            if (GameInput.Pressed(GameKey.Fps))
            {
                Settings.ShowFps = !Settings.ShowFps;
                Settings.Save();
            }
            // Esc closes the settings first, on any screen; then the tactical map or an open buy menu in a round;
            // on every other screen it runs that screen's own Back / Menu action (so "Esc closes menus" holds).
            bool esc = GameInput.Pressed(GameKey.Pause);
            bool escUsed = esc && SettingsOpen;
            if (escUsed) CloseSettings();
            else if (esc && MapOpen && Screen == AppScreen.Playing)
            {
                MapOpen = false;
                escUsed = true;
            }
            else if (esc && BuyOpen && Screen == AppScreen.Playing)
            {
                BuyOpen = false;
                escUsed = true;
            }
            else if (esc && Screen != AppScreen.Playing) escUsed = EscBack();

            switch (Screen)
            {
                case AppScreen.Playing:
                    TickPlaying(escUsed);
                    break;
                case AppScreen.Intro:
                    if (!SettingsOpen && GameInput.Pressed(GameKey.Confirm)) Begin();
                    AgentState me = Me;
                    Rig.SetViewmodelVisible(true);
                    Vector3 g = World.InterpPos(PlayerId);
                    EyePos = new Vector2(g.x, g.z);
                    Rig.Place(g, false, me == null || me.alive);
                    break;
                default:
                    Rig.SetViewmodelVisible(false);
                    Rig.Orbit(Map);
                    break;
            }
            TickMatch();
            TickRound5();
            if (Screen != AppScreen.Playing || Paused) MapOpen = false;
            MapPeek = Screen == AppScreen.Playing && !Paused && GameInput.Held(GameKey.FullMap);
            FullMap = MapOpen || MapPeek;
            if ((Screen == AppScreen.Playing || Screen == AppScreen.Intro) && !Paused) Minimap.Tick(this, EyePos, Rig.Heading);
            if (Screen != AppScreen.Playing && Aiming.HasValue) CancelAim();
            bool lockIt = Screen == AppScreen.Playing && !Paused && Result == null && !SettingsOpen && !BuyOpen && !(InMatch && DrillDone && MatchEnd != null);
            Cursor.lockState = lockIt ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockIt;
        }

        /// <summary>Esc outside a round: run the action the screen's own Back / Menu button runs.</summary>
        bool EscBack()
        {
            switch (Screen)
            {
                case AppScreen.Intro:
                case AppScreen.Result:
                    BackToReport();
                    return true;
                case AppScreen.Choose:
                case AppScreen.Report:
                case AppScreen.MatchSetup:
                case AppScreen.PersonaSelect:
                    OpenMenu();
                    return true;
                case AppScreen.MapSelect:
                    KeepCurrentMap();
                    return true;
                case AppScreen.AgentSelect:
                    OpenMapSelect();
                    return true;
                case AppScreen.Buy:
                    TogglePauseMenu();
                    return true;
                case AppScreen.MatchEnd:
                    BackFromMatchEnd();
                    return true;
                case AppScreen.DuelSetup:
                    OpenPersonas();
                    return true;
                case AppScreen.DuelEnd:
                    BackFromDuelEnd();
                    return true;
                default:
                    return false;
            }
        }

        void TrackFps()
        {
            float dt = Time.unscaledDeltaTime;
            fpsAcc += dt;
            fpsFrames++;
            worstAcc = Mathf.Max(worstAcc, dt);
            if (fpsAcc < 0.5f) return;
            Fps = fpsFrames / fpsAcc;
            WorstMs = worstAcc * 1000f;
            fpsAcc = 0f;
            fpsFrames = 0;
            worstAcc = 0f;
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
                    Fx.Clear();
                    ResetMatch();
                }
            }
        }

        void TickPlaying(bool escUsed)
        {
            if (!DrillDone && !escUsed && GameInput.Pressed(GameKey.Pause))
            {
                if (Paused) Resume();
                else Paused = true;
            }
            if (!Paused && !fireArmed && !GameInput.FireHeld) fireArmed = true;
            if (!GameInput.FireHeld) semiReleased = true;
            // M opens and closes the tactical map (the corner minimap is a setting).
            if (!Paused && GameInput.Pressed(GameKey.Minimap)) ToggleMap();

            AgentState me = Me;
            bool alive = me != null && me.alive;
            bool active = !Paused && !DrillDone && Connected;
            bool control = active && alive && !BuyOpen;
            float now = Time.unscaledTime;
            if (!Paused && GameInput.Pressed(GameKey.Buy) && BuyPhaseActive) BuyOpen = !BuyOpen;
            Rig.SensScale = Rig.ScopeSensFactor();
            if (control) Rig.Look(GameInput.LookDelta());
            if (Settings.ToggleCrouch) { if (control && GameInput.Pressed(GameKey.Crouch)) crouchToggled = !crouchToggled; }
            else crouchToggled = false;
            if (Settings.ToggleWalk) { if (control && GameInput.Pressed(GameKey.Walk)) walkToggled = !walkToggled; }
            else walkToggled = false;
            bool crouch = control && (Settings.ToggleCrouch ? crouchToggled : GameInput.Held(GameKey.Crouch));
            bool walk = control && (Settings.ToggleWalk ? walkToggled : GameInput.Held(GameKey.Walk));
            crouchNow = crouch;
            walkNow = walk;
            float fwd = (GameInput.Held(GameKey.Forward) ? 1f : 0f) - (GameInput.Held(GameKey.Back) ? 1f : 0f);
            float right = (GameInput.Held(GameKey.Right) ? 1f : 0f) - (GameInput.Held(GameKey.Left) ? 1f : 0f);
            Vector2 mv = control ? Rig.MoveVector(fwd, right) : Vector2.zero;
            MoveMode = crouch ? "crouch" : walk ? "walk" : "run";
            Moving = control && (fwd != 0f || right != 0f);

            if (control) WeaponInput(now);
            Gun.Tick(now, Time.unscaledDeltaTime, control && GameInput.FireHeld, Moving, MoveMode, Rig.DashKickAmount > 0.35f, control && GameInput.AimHeld);

            // Movement goes out with the next ticks (averaged over each 50 ms window), and the eye moves now.
            if (active) SendLoop(mv * SpeedFor(crouch, walk));
            Vector3 ground;
            if (alive && Predictor.Ready)
            {
                Vector2 p = Predictor.Render(tickDisp, sendAcc * Mathf.Max(1f, Rules.tickRate), Time.unscaledDeltaTime);
                ground = new Vector3(p.x, 0f, p.y);
            }
            else ground = World.InterpPos(PlayerId);
            EyePos = new Vector2(ground.x, ground.z);
            Rig.Place(ground, crouch, alive);
            Rig.SetViewmodelVisible(alive);

            if (control)
            {
                QueueFire();
                QueueAbilities(me);
            }
            else CancelAim();

            if (Result != null && !InMatch && Time.unscaledTime >= VerdictUntil) Screen = AppScreen.Result;
        }

        float SpeedFor(bool crouch, bool walk)
        {
            if (crouch) return Rules.crouchSpeedMps > 0f ? Rules.crouchSpeedMps : 2f;
            if (walk) return Rules.walkSpeedMps > 0f ? Rules.walkSpeedMps : 3f;
            return Rules.moveSpeedMps > 0f ? Rules.moveSpeedMps : 6.75f;
        }

        void CancelAim()
        {
            Aiming = null;
            Fx.HideMarker();
        }

        /// <summary>Has a charge, nothing of this kind already queued, and the last use has had time to reach the state.</summary>
        bool Ready(Ability a, AgentState me)
        {
            if (me.Charges(a) <= 0) return false;
            if (Time.unscaledTime - UsedAt[(int)a] < 0.3f) return false;
            switch (a)
            {
                case Ability.Smoke: return !pendingSmoke.HasValue;
                case Ability.Flash: return !pendingFlash.HasValue;
                case Ability.Recon: return !pendingRecon.HasValue;
                default: return !pendingDash;
            }
        }

        public float EffectRadius(Ability a)
        {
            switch (a)
            {
                case Ability.Smoke: return 4.5f;
                case Ability.Recon: return Mathf.Max(0.5f, Rules.reconRadiusM);
                default: return 1f;
            }
        }

        /// <summary>
        /// C smoke, Q flash, X recon: hold to show the landing point (the crosshair's floor hit within the ability's
        /// range), release to throw. E dash goes out on press.
        /// </summary>
        void QueueAbilities(AgentState me)
        {
            float now = Time.unscaledTime;
            if (GameInput.Pressed(GameKey.Dash))
            {
                if (Ready(Ability.Dash, me))
                {
                    pendingDash = true;
                    UsedAt[(int)Ability.Dash] = now;
                }
                else if (me.Charges(Ability.Dash) <= 0) DeniedAt[(int)Ability.Dash] = now;
            }
            foreach (Ability a in Agents.AllAbilities)
            {
                if (!Agents.Aimed(a) || !GameInput.Pressed(Agents.Key(a))) continue;
                if (Ready(a, me)) Aiming = a;
                else if (me.Charges(a) <= 0) DeniedAt[(int)a] = now;
            }
            if (!Aiming.HasValue) return;

            Ability ab = Aiming.Value;
            if (me.Charges(ab) <= 0)
            {
                CancelAim();
                return;
            }
            var mine = EyePos;
            Vector2 pt = Rig.AimPoint(mine, Rules.MaxRange(ab), Map, out bool clamped);
            AimClamped = clamped;
            if (GameInput.Held(Agents.Key(ab)))
            {
                Fx.ShowMarker(ab, World.HandPos(PlayerId, Rig.transform), new Vector3(pt.x, 0f, pt.y), EffectRadius(ab), clamped);
                return;
            }
            // Released: throw.
            switch (ab)
            {
                case Ability.Smoke: pendingSmoke = pt; break;
                case Ability.Flash: pendingFlash = pt; break;
                case Ability.Recon: pendingRecon = pt; break;
            }
            UsedAt[(int)ab] = now;
            CancelAim();
        }

        /// <summary>0..1 white-out for the player's blindness, easing out over the remaining blind time.</summary>
        public float BlindAmount()
        {
            AgentState me = Me;
            if (me == null || State == null || me.blindUntilTick <= State.tick) return 0f;
            float left = me.blindUntilTick - World.TickNow;
            float x = Mathf.Clamp01(left / Mathf.Max(1f, blindTotalTicks));
            // Fully white for the first part, then an ease-out to clear.
            const float hold = 0.6f;
            if (x >= hold) return 1f;
            float k = x / hold;
            return 1f - (1f - k) * (1f - k);
        }

        /// <summary>The player's remaining blindness in seconds (0 when not blinded).</summary>
        public float BlindSecondsLeft
        {
            get
            {
                AgentState me = Me;
                if (me == null || State == null || me.blindUntilTick <= State.tick) return 0f;
                return Mathf.Max(0f, (me.blindUntilTick - World.TickNow) / Mathf.Max(1f, Rules.tickRate));
            }
        }

        /// <summary>The length of the player's current (or last) blind, in seconds.</summary>
        public float BlindTotalSeconds => blindTotalTicks / Mathf.Max(1f, Rules.tickRate);

        void QueueFire()
        {
            if (!fireArmed || !GameInput.FireHeld || pendingFire.HasValue) return;
            float now = Time.unscaledTime;
            WeaponInfo w = Gun.Weapon;
            // Semi-automatic weapons fire once per click.
            if (!w.Auto && !semiReleased) return;
            if (!Gun.CanFire(now))
            {
                if (Gun.Ammo <= 0 && GameInput.FireHeld) Gun.RequestReload(now);
                return;
            }
            float interval = w.FireIntervalS > 0f ? w.FireIntervalS : Rules.fireIntervalS;
            int needTicks = Mathf.Max(1, Mathf.CeilToInt(interval * Rules.tickRate - 0.001f));
            // The shot goes out with the next tick; keep sent shots at least the weapon's interval apart in sim time.
            if (ticksSent + 1 - lastFireTick < needTicks) return;
            Gun.PlanShot(now);
            pendingFire = Rig.Fire(World, PlayerTeam, Gun);
            if (Rig.Weapon != null) FxLights.Flash(Rig.Weapon.MuzzleWorld(Rig.transform), new Color(1f, 0.8f, 0.5f), 1.6f, 4f, 0.05f);
            lastFireTick = ticksSent + 1;
            LastShotAt = now;
            semiReleased = false;
            ShotsFired++;
        }

        /// <summary>
        /// Accumulates this frame's movement (`vel`, m/s in the sim frame) and sends one input per elapsed tick. Each
        /// tick's move vector is the average over its 50 ms window, so the sim covers exactly the distance the eye
        /// was drawn moving, even when keys change between ticks.
        /// </summary>
        void SendLoop(Vector2 vel)
        {
            float tick = 1f / Mathf.Max(1f, Rules.tickRate);
            float dt = Time.unscaledDeltaTime;
            sendAcc += dt;
            tickDisp += vel * dt;
            // After a hitch, catch up a few ticks at most rather than flooding the bridge.
            if (sendAcc > tick * 4f) sendAcc = tick * 4f;
            while (sendAcc >= tick && !DrillDone)
            {
                float excess = sendAcc - tick;
                Vector2 carry = vel * Mathf.Min(excess, dt);
                Vector2 window = tickDisp - carry;
                sendAcc = excess;
                tickDisp = carry;
                SendTick(window / tick);
            }
        }

        void SendTick(Vector2 windowVel)
        {
            AgentState me = Me;
            bool alive = me != null && me.alive;
            var input = new PlayerInputMsg();
            input.walk = alive && walkNow;
            input.crouch = alive && crouchNow;
            float speed = SpeedFor(input.crouch, input.walk);
            Vector2 mv = alive && speed > 0f ? windowVel / speed : Vector2.zero;
            if (mv.sqrMagnitude > 1f) mv.Normalize();
            if (mv.sqrMagnitude < 1e-6f) mv = Vector2.zero;
            input.moveX = mv.x;
            input.moveY = mv.y;
            input.yaw = Rig.SimYaw;
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
            if (pendingFlash.HasValue)
            {
                input.hasFlash = true;
                input.flashX = pendingFlash.Value.x;
                input.flashY = pendingFlash.Value.y;
                pendingFlash = null;
            }
            if (pendingRecon.HasValue)
            {
                input.hasRecon = true;
                input.reconX = pendingRecon.Value.x;
                input.reconY = pendingRecon.Value.y;
                pendingRecon = null;
            }
            if (pendingDash)
            {
                input.dash = alive;
                pendingDash = false;
            }
            // bots-wire.3 weapon fields, only to a bridge that speaks it.
            if (V3 && alive)
            {
                if (Gun.PendingReload) input.reload = true;
                if (!string.IsNullOrEmpty(Gun.PendingSlot)) input.slot = Gun.PendingSlot;
                input.scoped = Gun.Scoped;
            }
            Gun.PendingReload = false;
            Gun.PendingSlot = null;
            if (Client.Send(Wire.Input(input)))
            {
                ticksSent++;
                lastSentPlantHeld = input.plantHeld;
                Predictor.Apply(mv, speed, input.dash, input.yaw);
                if (input.dash)
                {
                    // The predicted dash starts now, so its kick and whoosh do too (not when the bridge echoes it).
                    Rig.DashKick();
                    Fx.PlayOwnDash();
                }
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
                    case "difficulty": break; // the bridge confirming the level Settings sent
                    case "maps": OnMaps(Wire.Read<MapsMsg>(o)); break;
                    case "map": OnMap(Wire.Read<MapMsg>(o)); break;
                    case "agent": OnAgent(o); break;
                    case "report": OnReport(ReadReport(o)); break;
                    case "drill": OnDrill(Wire.Read<DrillMsg>(o)); break;
                    case "step": OnStep(Wire.Read<StepMsg>(o)); break;
                    case "result": OnResult(Wire.Read<ResultMsg>(o)); break;
                    case "debrief": Debrief = Wire.Read<DebriefMsg>(o); break;
                    case "buy_phase": OnBuyPhase(o); break;
                    case "loadout": OnLoadout(o); break;
                    case "round_result": OnRoundResult(o); break;
                    case "match_end": OnMatchEnd(o); break;
                    case "error":
                        ErrorMsg e = Wire.Read<ErrorMsg>(o);
                        if (Screen == AppScreen.Connect && helloFallback < 3)
                        {
                            // An error before any hello: an older bridge refusing our hello. Step down: .4, then .3, .2, .1.
                            helloFallback++;
                            Client.Send(Wire.Hello(helloFallback == 1 ? Wire.Protocol : helloFallback == 2 ? Wire.Protocol2 : Wire.LegacyProtocol));
                            break;
                        }
                        if (WaitingFor == "match" || WaitingFor == "duel")
                        {
                            InMatch = false;
                            ResetDuel();
                            WaitingFor = null;
                        }
                        if (WaitingFor == "buy") WaitingFor = null;
                        // Inputs already in flight when a drill ends are answered with errors; those are expected.
                        if (!(DrillDone && Screen == AppScreen.Playing)) ShowToast("Bridge: " + (e?.message ?? "error"));
                        if (WaitingFor != "result") WaitingFor = null;
                        break;
                    default:
                        if (!HandleV4(type, o)) Debug.Log("Saltworks: ignoring message type " + type);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Saltworks: could not handle '" + type + "': " + ex);
                ShowToast("Could not read a '" + type + "' message (see Console).");
            }
        }

        /// <summary>Reads a report; an agentAdvice block of an unexpected shape is dropped instead of losing the report.</summary>
        static ReportMsg ReadReport(JObject o)
        {
            try
            {
                return Wire.Read<ReportMsg>(o);
            }
            catch (Exception ex)
            {
                if (o["agentAdvice"] == null) throw;
                Debug.LogWarning("Saltworks: could not read report.agentAdvice, hiding the advice panel: " + ex.Message);
                var copy = (JObject)o.DeepClone();
                copy.Remove("agentAdvice");
                return Wire.Read<ReportMsg>(copy);
            }
        }

        /// <summary>Tells the bridge the bot difficulty from Settings (on connect and when it changes).</summary>
        public void SendDifficulty()
        {
            if (!V2) return;
            Client.Send(Wire.Difficulty(Settings.DifficultyIds[Mathf.Clamp(Settings.BotDifficulty, 0, 3)]));
        }

        void OnHello(HelloMsg h)
        {
            V3 = h.protocol == Wire.Protocol || h.protocol == Wire.Protocol4 || (h.rules != null && h.rules.weapons != null && h.rules.weapons.Type != JTokenType.Null);
            V2 = h.protocol == Wire.Protocol2 || V3;
            SendDifficulty();
            if (!V2 && h.protocol != Wire.LegacyProtocol)
                ShowToast("The bridge speaks " + h.protocol + ", this client expects " + Wire.Protocol4 + ".");
            Rules = h.rules ?? new Rules();
            if (Rules.tickRate <= 0f) Rules.tickRate = 20f;
            Agents.MergeFromRules(Rules.agents);
            Weapons.MergeFromRules(Rules.weapons, Rules.armor);
            if (h.map != null) LoadMap(h.map);
            World.Clear();
            Fx.Clear();
            Report = null;
            Drill = null;
            Result = null;
            Debrief = null;
            WaitingFor = null;
            Maps = null;
            ResetMatch();
            Screen = AppScreen.Menu;
            HelloRound5(h);
            if (!V2)
            {
                // A bots-wire.1 bridge: one map, no agents. Skip both select screens.
                mapChosen = true;
                agentChosen = true;
                return;
            }
            Client.Send(Wire.Maps());
            // Picks made earlier this session survive a reconnect: the bridge is told again.
            if (PickedMapId != null) Client.Send(Wire.PickMap(PickedMapId));
            if (PickedAgentId != null) Client.Send(Wire.PickAgent(PickedAgentId));
            mapChosen = PickedMapId != null;
            agentChosen = PickedAgentId != null;
        }

        void LoadMap(MapDef m)
        {
            Map = m;
            if (mapRoot != null) Destroy(mapRoot);
            mapRoot = MapBuilder.Build(Map, font, textMat);
            textMat.mainTexture = font.material.mainTexture;
            Walls = new Walls2D(m);
            Predictor.Configure(Walls, Rules);
            Minimap.Build(m);
            RebuildProps();
            Atmos.Build(m, font, textMat);
            WorldLook.RefreshReflections(m);
            sitePolys.Clear();
            foreach (KeyValuePair<string, List<Vec2>> site in m.SiteList()) sitePolys.Add(site.Value);
        }

        void OnMaps(MapsMsg m)
        {
            Maps = m?.maps ?? new List<MapInfo>();
        }

        void OnMap(MapMsg m)
        {
            if (WaitingFor == "map") WaitingFor = null;
            if (m?.map == null) return;
            if (Drill != null && !DrillDone && Screen == AppScreen.Playing) return; // never swap the floor mid-drill
            World.Clear();
            Fx.Clear();
            LoadMap(m.map);
            PickedMapId = m.map.id;
            if (!string.IsNullOrEmpty(m.map.id)) PlayerPrefs.SetString(PrefMap, m.map.id);
            mapChosen = true;
            if (Screen == AppScreen.MapSelect) Screen = NextSetupScreen();
        }

        void OnAgent(JObject o)
        {
            if (WaitingFor == "agent") WaitingFor = null;
            AgentMsg a = Wire.Read<AgentMsg>(o);
            if (a == null || string.IsNullOrEmpty(a.agent)) return;
            PickedAgentId = a.agent;
            PickedKit = Agents.ReadKit(a.kit) ?? Agents.Get(a.agent)?.Kit;
            PlayerPrefs.SetString(PrefAgent, a.agent);
            agentChosen = true;
            if (Screen == AppScreen.AgentSelect) Screen = NextSetupScreen();
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
            if (Screen == AppScreen.Choose) Screen = NextSetupScreen();
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
            pendingFlash = null;
            pendingRecon = null;
            pendingDash = false;
            Aiming = null;
            for (int i = 0; i < UsedAt.Length; i++) UsedAt[i] = DeniedAt[i] = -10f;
            RevealedAt = -10f;
            lastDashTick.Clear();
            blindSeenUntil = 0;
            blindTotalTicks = 1;
            ticksSent = 0;
            lastFireTick = -1000;
            sendAcc = 0f;
            HitMarkerAt = -10f;
            DamageAt = -10f;
            PlayerId = d.playerId ?? d.spec?.player?.id ?? "player";
            PlayerTeam = d.playerTeam ?? d.spec?.player?.team ?? "attack";
            Fx.Clear();
            World.StartDrill(d, 1f / Rules.tickRate);
            PlayerAgent = d.playerAgent ?? d.spec?.player?.agent ?? World.Agent(PlayerId)?.agent ?? PickedAgentId;
            Rig.ResetFor(World.Agent(PlayerId));
            AgentState mine = World.Agent(PlayerId);
            Predictor.Configure(Walls, Rules);
            if (mine != null) Predictor.Reset(Geo.ToV2(mine.pos), d.state != null ? d.state.tick : 0);
            else Predictor.Stop();
            tickDisp = Vector2.zero;
            EyePos = mine != null ? Geo.ToV2(mine.pos) : Vector2.zero;
            Minimap.Clear();
            LastShotAt = -10f;
            WaitingFor = null;
            ResetGunFor(d, mine);
            DamageNumbers.Clear();
            RoundStartRound5();
            if (InMatch)
            {
                // A match round: no intro card, straight into the round (the buy menu may still be open over it).
                MatchRoundStarted(d);
                Screen = AppScreen.Playing;
                Begin2();
            }
            else Screen = AppScreen.Intro;
        }

        /// <summary>Begin without the intro check (match rounds).</summary>
        void Begin2()
        {
            Paused = false;
            sendAcc = 0f;
            tickDisp = Vector2.zero;
            fireArmed = false;
        }

        void OnStep(StepMsg st)
        {
            if (Drill == null || st.state == null) return;
            World.ApplyStep(st.state, st.thoughts);
            AgentState me = st.state.Agent(PlayerId);
            if (me != null) Predictor.Reconcile(st.state.tick, Geo.ToV2(me.pos), me.alive);
            if (me != null) Gun.Sync(me, st.state, Rules.tickRate, Time.unscaledTime);
            if (me != null && me.credits.HasValue) Credits = me.credits.Value;
            if (st.events != null) foreach (SimEvent e in st.events) OnEvent(e);
            TrackBlind(st.state);
            UpdatePlant();
            if (st.done) DrillDone = true;
        }

        /// <summary>Remembers how long the current blindness is in total, so the white-out can ease over it.</summary>
        void TrackBlind(SimState s)
        {
            AgentState me = Me;
            if (me == null || me.blindUntilTick <= s.tick || me.blindUntilTick <= blindSeenUntil) return;
            // A new or extended blind: the fade restarts from full over the time now left.
            blindTotalTicks = Mathf.Max(1, me.blindUntilTick - s.tick);
            blindSeenUntil = me.blindUntilTick;
        }

        void OnResult(ResultMsg r)
        {
            Result = r;
            ResultAt = Time.unscaledTime;
            DrillDone = true;
            if (WaitingFor == "result") WaitingFor = null;
            if (InMatch)
            {
                // Match rounds end with round_result; the drill-style result screen is not shown.
                NoteDuelResult(r);
                Result = null;
                abandoning = false;
                return;
            }
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
            for (int i = 0; i < sitePolys.Count; i++)
                if (Geo.PointInPolygon(v, sitePolys[i])) return true;
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
                        AddDamageNumber(e);
                    }
                    if (e.victim == PlayerId) DamageAt = now;
                    if (e.attacker != PlayerId && e.attackerPos != null && e.victimPos != null)
                    {
                        AgentState a = World.Agent(e.attacker);
                        Color c = Palette.Team(a != null ? a.team : "defense");
                        World.NoteShot(e.attacker);
                        Vector3 from = World.MuzzleOf(e.attacker, Geo.ToWorld(e.attackerPos, 1.45f));
                        World.Tracer(from, Geo.ToWorld(e.victimPos, 1.3f), new Color(c.r, c.g, c.b, 0.9f), 0.12f);
                        FxLights.Flash(from, new Color(1f, 0.78f, 0.45f), 2.2f, 5f, 0.06f);
                    }
                    DamageRound5(e);
                    break;
                case "kill":
                    KillRound5(e);
                    if (e.killer == PlayerId)
                    {
                        HitMarkerAt = now;
                        HitMarkerKill = true;
                        HitMarkerHead = e.part == "head";
                        MatchKills++;
                        if (e.part == "head") MatchHeadKills++;
                    }
                    if (e.victim == PlayerId)
                    {
                        DamageAt = now;
                        MatchDeaths++;
                    }
                    AddKill(e);
                    break;
                case "reload":
                case "buy":
                case "switch":
                    // bots-wire.3 bookkeeping events: the next state already carries their effect.
                    break;
                case "smoke":
                    AddFeed(Name(e.agent) + " threw a smoke", new Color(0.85f, 0.83f, 0.95f));
                    break;
                case "flash":
                    Fx.Flash(World.HandPos(e.thrower, Rig.transform), Geo.ToWorld(e.pos), Rules.flashFuseS);
                    AddFeed(Name(e.thrower) + " threw a flash", Agents.AbilityColor(Ability.Flash));
                    break;
                case "blinded":
                    if (e.agent == PlayerId)
                    {
                        float secs = Mathf.Max(0f, (e.untilTick - e.tick) / Mathf.Max(1f, Rules.tickRate));
                        Fx.PlayBlindRing(secs);
                        AddFeed(e.by == PlayerId ? "Your own flash blinded you" : "Blinded by " + Name(e.by), Agents.AbilityColor(Ability.Flash));
                    }
                    else if (e.by == PlayerId)
                    {
                        AddFeed("Your flash blinded " + Name(e.agent), Agents.AbilityColor(Ability.Flash));
                    }
                    break;
                case "dash":
                {
                    string id = e.agent ?? "";
                    // The sim sends one dash event per tick of the 0.2 s dash; consecutive ticks are one dash.
                    bool starts = !(lastDashTick.TryGetValue(id, out int last) && e.tick - last <= 1);
                    lastDashTick[id] = e.tick;
                    // The player's own kick and sound already played when the dash was sent (client-side prediction).
                    if (id != PlayerId)
                    {
                        Fx.DashSegment(Geo.ToWorld(e.from), Geo.ToWorld(e.to), starts);
                        if (starts) AddFeed(Name(id) + " dashed", Agents.AbilityColor(Ability.Dash));
                    }
                    break;
                }
                case "recon":
                    Fx.Recon(World.HandPos(e.thrower, Rig.transform), Geo.ToWorld(e.pos), Rules.ReconFuseS, Rules.reconRadiusM, Rules.reconRevealS);
                    AddFeed(Name(e.thrower) + " threw a recon dart", Agents.AbilityColor(Ability.Recon));
                    break;
                case "revealed":
                    World.NoteRevealed(e.target, e.untilTick);
                    if (e.target == PlayerId)
                    {
                        RevealedAt = now;
                        AddFeed("Revealed by " + Name(e.by) + "'s recon", Agents.AbilityColor(Ability.Recon));
                    }
                    else if (TeamOf(e.by) == PlayerTeam)
                    {
                        AddFeed("Recon revealed " + Name(e.target), Agents.AbilityColor(Ability.Recon));
                    }
                    break;
                case "footstep":
                    Fx.Footstep(e.agent, Geo.ToWorld(e.pos), e.loud, e.agent == PlayerId, Rules.runHearingM);
                    if (e.loud && e.pos != null && e.agent != PlayerId && TeamOf(e.agent) != PlayerTeam)
                    {
                        var at = new Vector2(e.pos.x, e.pos.y);
                        if ((at - EyePos).magnitude <= Rules.runHearingM) Minimap.Ping(e.agent, at);
                    }
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

        string TeamOf(string id)
        {
            if (id == PlayerId) return PlayerTeam;
            AgentState a = World.Agent(id);
            return a?.team;
        }

        /// <summary>The agent a bot plays in this drill (drill message, else its state), or null.</summary>
        public string BotAgent(string id)
        {
            if (World.Bots.TryGetValue(id, out BotView b) && !string.IsNullOrEmpty(b.Agent)) return b.Agent;
            return World.Agent(id)?.agent;
        }

        /// <summary>The player's kit for the HUD's pips: the bridge's answer to pick_agent when it is the agent in play, else the catalogue's.</summary>
        public AbilityCharges PlayerKit()
        {
            string id = PlayerAgent ?? PickedAgentId;
            if (id != null && id == PickedAgentId && PickedKit != null) return PickedKit;
            return Agents.Get(id)?.Kit;
        }

        public string Name(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            if (id == PlayerId) return "You";
            return DisplayName(id);
        }

        /// <summary>
        /// The name a player sees for `id`: the persona's name, else the name of the agent the bot plays, else
        /// "Bot N" (its 1-based place in the drill's bots). Never the bridge's raw id.
        /// </summary>
        public string DisplayName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            if (id == PlayerId) return "You";
            string persona = PersonaName(id);
            if (!string.IsNullOrEmpty(persona)) return persona;
            if (World == null) return id;
            if (World.Bots.TryGetValue(id, out BotView b))
            {
                string agent = BotAgent(id);
                if (!string.IsNullOrEmpty(agent)) return Agents.NameOf(agent);
                int n = BotNumber(id);
                if (n > 0) return "Bot " + n;
            }
            return id;
        }

        /// <summary>`id`'s 1-based place among the drill's bots, or 0 when it is not one.</summary>
        public int BotNumber(string id)
        {
            if (World == null) return 0;
            int n = 0;
            foreach (KeyValuePair<string, BotView> kv in World.Bots)
            {
                n++;
                if (kv.Key == id) return n;
            }
            return 0;
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

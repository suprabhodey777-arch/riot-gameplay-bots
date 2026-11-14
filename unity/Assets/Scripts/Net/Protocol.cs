// Wire protocol bots-wire.2 (docs/unity-bridge.md, "Round 2 additions"), which
// still reads a bots-wire.1 bridge. DTO field names match the JSON keys exactly,
// so Newtonsoft maps them without attributes. Unknown keys are ignored, missing
// keys keep their defaults, so every round-2 field degrades to "not there".
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Saltworks
{
    public static class Wire
    {
        public const string Protocol = "bots-wire.2";
        public const string LegacyProtocol = "bots-wire.1";

        static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
        });

        public static T Read<T>(JObject o) where T : class
        {
            return o.ToObject<T>(Serializer);
        }

        public static JObject V2(float x, float y)
        {
            return new JObject { ["x"] = x, ["y"] = y };
        }

        // ---- client -> bridge -------------------------------------------------
        public static JObject Hello(string protocol = Protocol)
        {
            return new JObject { ["type"] = "hello", ["protocol"] = protocol };
        }

        public static JObject LoadSample(string skill, int seed)
        {
            return new JObject
            {
                ["type"] = "load_history",
                ["sample"] = new JObject { ["skill"] = skill, ["seed"] = seed },
            };
        }

        public static JObject NextDrill()
        {
            return new JObject { ["type"] = "next_drill" };
        }

        public static JObject Rehearse(int index)
        {
            return new JObject { ["type"] = "rehearse", ["index"] = index };
        }

        public static JObject Versus(string archetype, string skill)
        {
            return new JObject { ["type"] = "versus", ["archetype"] = archetype, ["skill"] = skill };
        }

        public static JObject Abandon()
        {
            return new JObject { ["type"] = "abandon" };
        }

        public static JObject Maps()
        {
            return new JObject { ["type"] = "maps" };
        }

        public static JObject PickMap(string id)
        {
            return new JObject { ["type"] = "pick_map", ["map"] = id };
        }

        public static JObject PickAgent(string id)
        {
            return new JObject { ["type"] = "pick_agent", ["agent"] = id };
        }

        public static JObject Input(PlayerInputMsg i)
        {
            JToken fire = JValue.CreateNull();
            if (i.fireTargetId != null)
                fire = new JObject { ["targetId"] = i.fireTargetId, ["result"] = i.fireResult };
            JToken smoke = JValue.CreateNull();
            if (i.hasSmoke) smoke = V2(i.smokeX, i.smokeY);
            var input = new JObject
            {
                ["move"] = V2(i.moveX, i.moveY),
                ["yaw"] = i.yaw,
                ["walk"] = i.walk,
                ["crouch"] = i.crouch,
                ["fire"] = fire,
                ["smokeAt"] = smoke,
                ["plantHeld"] = i.plantHeld,
                ["defuseHeld"] = i.defuseHeld,
            };
            // Round-2 fields are optional: absent means "not used this tick".
            if (i.hasFlash) input["flashAt"] = V2(i.flashX, i.flashY);
            if (i.dash) input["dash"] = true;
            if (i.hasRecon) input["reconAt"] = V2(i.reconX, i.reconY);
            return new JObject { ["type"] = "input", ["input"] = input };
        }
    }

    /// <summary>One tick of input, before it is turned into JSON.</summary>
    public class PlayerInputMsg
    {
        public float moveX, moveY, yaw;
        public bool walk, crouch, plantHeld, defuseHeld;
        /// <summary>Null when not firing this tick.</summary>
        public string fireTargetId;
        public string fireResult;
        public bool hasSmoke;
        public float smokeX, smokeY;
        public bool hasFlash;
        public float flashX, flashY;
        public bool dash;
        public bool hasRecon;
        public float reconX, reconY;
    }

    public enum Ability
    {
        Smoke,
        Flash,
        Dash,
        Recon,
    }

    /// <summary>Charges per ability (contract v4 AbilityCharges); also the shape of an agent's kit.</summary>
    public class AbilityCharges
    {
        public int smoke;
        public int flash;
        public int dash;
        public int recon;

        public int Get(Ability a)
        {
            switch (a)
            {
                case Ability.Smoke: return smoke;
                case Ability.Flash: return flash;
                case Ability.Dash: return dash;
                default: return recon;
            }
        }

        public int Total => smoke + flash + dash + recon;
    }

    // ---- shared/contract.ts -----------------------------------------------------
    public class Vec2
    {
        public float x;
        public float y;
    }

    public class MapBounds
    {
        public float width;
        public float height;
    }

    public class WallDef
    {
        public string id;
        public List<Vec2> points = new List<Vec2>();
        public float height = 4f;
    }

    public class SitesDef
    {
        public List<Vec2> A;
        public List<Vec2> B;
        public List<Vec2> C;
    }

    public class SpawnsDef
    {
        public List<Vec2> attack = new List<Vec2>();
        public List<Vec2> defense = new List<Vec2>();
    }

    public class CalloutDef
    {
        public string name;
        public List<Vec2> polygon = new List<Vec2>();
    }

    public class MapDef
    {
        public string id;
        public string name;
        public MapBounds bounds = new MapBounds();
        public List<WallDef> walls = new List<WallDef>();
        public SitesDef sites = new SitesDef();
        public SpawnsDef spawns = new SpawnsDef();
        public List<CalloutDef> callouts = new List<CalloutDef>();

        /// <summary>Sites as (letter, polygon), skipping absent ones.</summary>
        public IEnumerable<KeyValuePair<string, List<Vec2>>> SiteList()
        {
            if (sites == null) yield break;
            if (sites.A != null && sites.A.Count >= 3) yield return new KeyValuePair<string, List<Vec2>>("A", sites.A);
            if (sites.B != null && sites.B.Count >= 3) yield return new KeyValuePair<string, List<Vec2>>("B", sites.B);
            if (sites.C != null && sites.C.Count >= 3) yield return new KeyValuePair<string, List<Vec2>>("C", sites.C);
        }
    }

    public class Rules
    {
        public float tickRate = 20f;
        public float fireIntervalS = 0.15f;
        public float smokeMaxRangeM = 25f;
        public float plantTimeS = 4f;
        public float defuseTimeS = 7f;
        public float moveSpeedMps = 5.4f;

        // bots-wire.2 (defaults are the sim/sim.ts constants, used when an older bridge leaves them out).
        public float flashMaxRangeM = 20f;
        public float flashFuseS = 0.6f;
        public float flashBlindS = 1.75f;
        public float dashDistanceM = 6f;
        public float reconMaxRangeM = 30f;
        public float reconRadiusM = 10f;
        public float reconRevealS = 2f;
        public float runHearingM = 15f;
        public float walkSpeedMps = 3f;
        public float crouchSpeedMps = 2f;
        /// <summary>The four agents with role and kit; shape read leniently by Agents.MergeFromRules.</summary>
        public JToken agents;

        /// <summary>Not in rules: sim/sim.ts RECON_FUSE_S.</summary>
        public const float ReconFuseS = 0.5f;

        public float MaxRange(Ability a)
        {
            switch (a)
            {
                case Ability.Smoke: return smokeMaxRangeM;
                case Ability.Flash: return flashMaxRangeM;
                case Ability.Recon: return reconMaxRangeM;
                default: return dashDistanceM;
            }
        }
    }

    public class AgentState
    {
        public string id;
        public string team;
        public bool isBot;
        public Vec2 pos = new Vec2();
        public float yaw;
        public float hp = 100f;
        public bool alive = true;
        public int smokesLeft;
        public bool carryingSpike;
        // contract v4; absent from a bots-wire.1 bridge.
        public string agent;
        public AbilityCharges abilities;
        public int blindUntilTick;
        public int revealedUntilTick;

        /// <summary>Charges left; without v4 abilities only the smoke count (smokesLeft) is known.</summary>
        public int Charges(Ability a)
        {
            if (abilities != null) return abilities.Get(a);
            return a == Ability.Smoke ? smokesLeft : 0;
        }
    }

    public class SmokeState
    {
        public string id;
        public string ownerId;
        public Vec2 pos = new Vec2();
        public float radius = 4.5f;
        public int endsAtTick;
    }

    /// <summary>The SpikeState union flattened: read the fields that match `status`.</summary>
    public class SpikeState
    {
        public string status = "detonated";
        public string carrierId;
        public Vec2 pos;
        public string site;
        public int plantedAtTick;
        public float defuseProgressS;
    }

    public class SimState
    {
        public int tick;
        public float tickRate = 20f;
        public float roundTimeLeftS;
        public string phase = "live";
        public List<AgentState> agents = new List<AgentState>();
        public List<SmokeState> smokes = new List<SmokeState>();
        public SpikeState spike = new SpikeState();
        public string winner;

        public AgentState Agent(string id)
        {
            if (agents == null) return null;
            for (int i = 0; i < agents.Count; i++)
                if (agents[i].id == id) return agents[i];
            return null;
        }
    }

    /// <summary>The SimEvent union flattened: read the fields that match `type`.</summary>
    public class SimEvent
    {
        public string type;
        public int tick;
        public string observer;
        public string target;
        public string attacker;
        public string victim;
        public float amount;
        public string part;
        public string killer;
        public string agent;
        public string site;
        public string winner;
        public string reason;
        public Vec2 pos;
        public Vec2 attackerPos;
        public Vec2 victimPos;
        public Vec2 killerPos;
        // contract v4 events: flash, blinded, dash, recon, revealed, footstep.
        public string thrower;
        public string by;
        public int untilTick;
        public Vec2 from;
        public Vec2 to;
        public bool loud;
    }

    // ---- shared/coaching.ts -----------------------------------------------------
    public class WeaknessEvidence
    {
        public string matchId;
        public int round;
        public int tick;
        public string note;
    }

    public class WeaknessFinding
    {
        public string skill;
        public float rate;
        public float benchmark;
        public float severity;
        public int sampleSize;
        public string summary;
        public List<WeaknessEvidence> evidence = new List<WeaknessEvidence>();
    }

    public class WeaknessReport
    {
        public string playerId;
        public string role;
        public int matchesRead;
        public List<WeaknessFinding> findings = new List<WeaknessFinding>();
    }

    public class DrillPlayer
    {
        public string id;
        public string team;
        public Vec2 spawn;
        public float hp = 100f;
        public bool carryingSpike;
        /// <summary>Possibly present in v4 drill specs; DrillMsg.playerAgent wins.</summary>
        public string agent;
    }

    public class DrillBotSpec
    {
        public string id;
        public string team;
        public Vec2 spawn;
        public string archetypeId;
    }

    public class SpikePlantedSpec
    {
        public string site;
        public Vec2 pos;
        public float secondsLeft;
    }

    public class DrillSpec
    {
        public string id;
        public string skill;
        public string title;
        public string goal;
        public string mapId;
        public int seed;
        public float difficulty;
        public DrillPlayer player;
        public List<DrillBotSpec> bots = new List<DrillBotSpec>();
        public float roundTimeLeftS;
        public SpikePlantedSpec spikePlanted;
    }

    public class DrillResult
    {
        public string drillId;
        public string skill;
        public bool success;
        public int ticks;
        public List<SimEvent> events = new List<SimEvent>();
    }

    // ---- bridge -> client messages ---------------------------------------------
    public class HelloMsg
    {
        public string protocol;
        public MapDef map;
        public Rules rules;
    }

    public class RehearsalInfo
    {
        public int index;
        public string title;
        public string brief;
        public string skill;
    }

    public class StaircaseInfo
    {
        public float difficulty;
    }

    public class ReportMsg
    {
        public WeaknessReport report = new WeaknessReport();
        public List<RehearsalInfo> rehearsals = new List<RehearsalInfo>();
        public Dictionary<string, StaircaseInfo> staircases = new Dictionary<string, StaircaseInfo>();
        public int played;
        /// <summary>Absent on an older bridge (then the "Recommended for you" panel is hidden).</summary>
        public AgentAdvice agentAdvice;
    }

    // ---- round 3: agent advice on the report -------------------------------------------------
    public class AdviceAgent
    {
        public string id;
        public string name;
        public string role;
        /// <summary>A string ("3 Smokes") per the spec; read leniently in case it arrives as a charges object.</summary>
        public JToken kit;
        public List<string> valorantLike = new List<string>();

        public string KitText()
        {
            if (kit == null || kit.Type == JTokenType.Null) return Agents.KitText(Agents.Get(id)?.Kit);
            if (kit.Type == JTokenType.String) return (string)kit;
            return Agents.KitText(Agents.ReadKit(kit));
        }
    }

    public class AdvicePick
    {
        public AdviceAgent agent;
        public string why;
    }

    public class AdviceFit
    {
        public AdviceAgent agent;
        public float fit;
    }

    public class Playstyle
    {
        public int rounds;
        public float firstDuelShare;
        public float firstDuelWinRate;
        public float meanKillDistanceM;
        public float smokesPerRound;
        public float objectiveShare;
        public int kills;
        public int deaths;
    }

    public class AgentAdvice
    {
        public AdvicePick main;
        public AdvicePick practice;
        public List<AdviceFit> ranked = new List<AdviceFit>();
        public Playstyle playstyle;
    }

    public class DrillBotInfo
    {
        public string id;
        public string team;
        public string archetype;
        public string agent;
    }

    public class DrillMsg
    {
        public DrillSpec spec = new DrillSpec();
        public string intro;
        public string playerId = "player";
        public string playerTeam = "attack";
        public string playerAgent;
        public List<DrillBotInfo> bots = new List<DrillBotInfo>();
        public SimState state = new SimState();
    }

    public class StepMsg
    {
        public SimState state;
        public List<SimEvent> events = new List<SimEvent>();
        public Dictionary<string, string> thoughts = new Dictionary<string, string>();
        public bool done;
    }

    public class ResultMsg
    {
        public DrillResult result;
        public bool success;
        public string headline;
        public float? staircaseBefore;
        public float? staircaseAfter;
    }

    public class DebriefMsg
    {
        public string text;
        public string source;
    }

    public class ErrorMsg
    {
        public string message;
    }

    public class MapInfo
    {
        public string id;
        public string name;
    }

    public class MapsMsg
    {
        public List<MapInfo> maps = new List<MapInfo>();
    }

    /// <summary>Answer to pick_map: the MapDef the next drills use.</summary>
    public class MapMsg
    {
        public MapDef map;
    }

    /// <summary>Answer to pick_agent. `kit` is read leniently by Agents.ReadKit.</summary>
    public class AgentMsg
    {
        public string agent;
        public JToken kit;
    }
}

// Round 5 (docs/streamer-mode.md, bots-wire.4): the streamer overlay's data
// (chat, alerts, persona callouts, the viewer count and the goal), pro persona
// duels and persona matches, the round-start sweep, damage direction and the
// world effects set up for the round. Everything degrades: without bots-wire.4
// the persona buttons explain what is missing, the overlay still runs with
// alerts made here from the kill events, and no bots-wire.4 message is sent.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Saltworks
{
    public class ChatLine
    {
        public string User;
        public string Text;
        /// <summary>"sub", "mod" or null.</summary>
        public string Badge;
        public Color Color;
        public float At;
        /// <summary>Measured widths (virtual pixels), cached by the overlay; -1 until measured.</summary>
        public float UserW = -1f;
        /// <summary>The overlay's rich-text line and its wrapped height, built once.</summary>
        public string Composed;
        public float Height = -1f;
    }

    public class AlertItem
    {
        public string Kind;
        public string Text;
        public bool Big;
        public float At;
        public float Duration;
    }

    public class Callout
    {
        public string Persona;
        public string Name;
        public string Text;
        public Color Color;
        public float At;
    }

    public struct DamageIndicator
    {
        /// <summary>Where the shot came from (sim floor plan).</summary>
        public Vector2 From;
        public float At;
    }

    public partial class GameController
    {
        /// <summary>The bridge speaks bots-wire.4 (or sent personas): duels, persona matches, chat and alerts.</summary>
        public bool V4 { get; private set; }

        // ---- the overlay ---------------------------------------------------------------------------
        public readonly List<ChatLine> Chat = new List<ChatLine>();
        /// <summary>The alert on screen now, or null.</summary>
        public AlertItem Alert { get; private set; }
        readonly Queue<AlertItem> alertQueue = new Queue<AlertItem>();
        public readonly List<Callout> Callouts = new List<Callout>();
        /// <summary>Simulated viewers: rises with good plays, wanders a little.</summary>
        public int Viewers => Mathf.RoundToInt(viewerBase + Mathf.Sin(Time.unscaledTime * 0.37f) * 9f + Mathf.Sin(Time.unscaledTime * 1.3f) * 3f);
        public float ViewerBumpAt { get; private set; } = -10f;
        /// <summary>0..1, how hot the stream is (decays); the overlay glows with it.</summary>
        public float Hype { get; private set; }
        /// <summary>Kills by the player this session (the drill goal bar).</summary>
        public int SessionKills { get; private set; }
        float viewerBase = 1180f;
        int roundKills;
        bool roundFirstBlood;

        // ---- HUD signals ----------------------------------------------------------------------------
        public readonly DamageIndicator[] Indicators = new DamageIndicator[6];
        int nextIndicator;
        public float RoundBannerAt { get; private set; } = -10f;
        public string RoundBannerText { get; private set; }
        public string RoundBannerSub { get; private set; }
        int bannerRound = -1;

        // ---- duels and persona matches ------------------------------------------------------------
        public bool InDuel { get; private set; }
        public string DuelPersona { get; private set; }
        public string DuelSite = "A";
        public string DuelMapId;
        public DuelEndMsg DuelEnd { get; private set; }
        public float DuelEndAt { get; private set; } = -10f;
        readonly ScoreInfo duelTally = new ScoreInfo();
        /// <summary>The duel's score: the bridge's (round_result / buy_phase) when it sends one, else counted here from results.</summary>
        public ScoreInfo DuelScore => Score ?? duelTally;
        public string DuelPersonaName => Personas.NameOf(DuelPersona);
        /// <summary>The match setup's enemy team is made of personas (bots-wire.4).</summary>
        public bool PersonaEnemies { get; set; }
        public readonly List<string> EnemyPersonas = new List<string>();
        bool personasAsked;

        // ---- round 5 world -------------------------------------------------------------------------
        public FigureStage Stage { get; private set; }
        public WorldFx Atmos { get; private set; }
        DamageFx damageFx;

        /// <summary>From Awake: the figure stage, the world effects, the light pool and the damage post effect.</summary>
        void SetupRound5()
        {
            Stage = new GameObject("Figure stage").AddComponent<FigureStage>();
            Atmos = World.gameObject.AddComponent<WorldFx>();
            World.gameObject.AddComponent<FxLights>();
            damageFx = Rig.gameObject.AddComponent<DamageFx>();
            viewerBase = 1100f + Mathf.Abs(SystemInfo.deviceUniqueIdentifier.GetHashCode() % 400);
        }

        // ---- messages ------------------------------------------------------------------------------
        /// <summary>bots-wire.4 messages; true when `type` was one of them.</summary>
        bool HandleV4(string type, JObject o)
        {
            switch (type)
            {
                case "personas": OnPersonas(o); return true;
                case "chat": OnChat(o); return true;
                case "alert": OnAlert(o); return true;
                case "persona_line": OnPersonaLine(o); return true;
                case "duel_end": OnDuelEnd(o); return true;
                default: return false;
            }
        }

        /// <summary>From OnHello: bots-wire.4 or not, ask for the personas, and turn the overlay's feed on if wanted.</summary>
        void HelloRound5(HelloMsg h)
        {
            V4 = h.protocol == Wire.Protocol4;
            personasAsked = false;
            if (!V4) return;
            Client.Send(Wire.Personas());
            personasAsked = true;
            if (Settings.StreamerMode) Client.Send(Wire.Streamer(true));
        }

        void OnPersonas(JObject o)
        {
            PersonasMsg m = ReadLenient<PersonasMsg>(o, "personas");
            if (m?.personas is JArray arr)
            {
                foreach (JToken t in arr)
                {
                    if (!(t is JObject po)) continue;
                    Personas.Merge(ReadLenient<PersonaInfo>(po, "personas[]"));
                }
                V4 = true;
            }
        }

        void OnChat(JObject o)
        {
            ChatMsg m = ReadLenient<ChatMsg>(o, "chat");
            if (m == null || string.IsNullOrEmpty(m.text)) return;
            string user = string.IsNullOrEmpty(m.user) ? "viewer" : m.user;
            // A persona speaking in chat shows under the player's name for it.
            Persona speaker = Personas.Match(user);
            if (speaker != null) user = speaker.Name;
            Chat.Add(new ChatLine
            {
                User = user,
                Text = Personas.Rename(m.text),
                Badge = Lenient.Str(m.badge),
                Color = Lenient.Color(m.color, ChatColor(user)),
                At = Time.unscaledTime,
            });
            if (Chat.Count > 60) Chat.RemoveAt(0);
            viewerBase += 1.5f;
        }

        /// <summary>A stable colour for a username the bridge sent without one.</summary>
        static Color ChatColor(string user)
        {
            float h = (MapBuilder.StableHash(user) % 360) / 360f;
            return Color.HSVToRGB(h, 0.55f, 1f);
        }

        void OnAlert(JObject o)
        {
            AlertMsg m = ReadLenient<AlertMsg>(o, "alert");
            if (m == null) return;
            PushAlert(m.kind, string.IsNullOrEmpty(m.text) ? AlertText(m.kind) : Personas.Rename(m.text), Lenient.Bool(m.big));
        }

        static string AlertText(string kind)
        {
            switch (kind)
            {
                case "first_blood": return "FIRST BLOOD";
                case "multi_kill": return "MULTI KILL";
                case "clutch": return "CLUTCH";
                case "ace": return "ACE";
                case "eco_win": return "ECO WIN";
                default: return string.IsNullOrEmpty(kind) ? "" : kind.Replace('_', ' ').ToUpperInvariant();
            }
        }

        void PushAlert(string kind, string text, bool big)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (alertQueue.Count >= 4) alertQueue.Dequeue();
            alertQueue.Enqueue(new AlertItem { Kind = kind ?? "", Text = text, Big = big, Duration = big ? 3.2f : 2.4f });
            viewerBase += big ? 90f : 40f;
            ViewerBumpAt = Time.unscaledTime;
            Hype = Mathf.Min(1f, Hype + (big ? 0.5f : 0.3f));
        }

        void OnPersonaLine(JObject o)
        {
            PersonaLineMsg m = ReadLenient<PersonaLineMsg>(o, "persona_line");
            if (m == null || string.IsNullOrEmpty(m.text)) return;
            Persona p = Personas.Get(m.persona) ?? Personas.Match(m.persona);
            Callouts.Add(new Callout
            {
                Persona = m.persona,
                Name = p != null ? p.Name : Agents.NameOf(m.persona),
                Text = Personas.Rename(m.text),
                Color = p != null ? p.Primary : Settings.Enemy,
                At = Time.unscaledTime,
            });
            if (Callouts.Count > 3) Callouts.RemoveAt(0);
        }

        void OnDuelEnd(JObject o)
        {
            DuelEndMsg m = ReadLenient<DuelEndMsg>(o, "duel_end");
            if (m == null) return;
            if (WaitingFor == "duel") WaitingFor = null;
            DuelEnd = m;
            DuelEndAt = Time.unscaledTime;
            if (m.score is JObject)
            {
                duelTally.player = m.PlayerScore;
                duelTally.enemy = m.EnemyScore;
            }
            BuyOpen = false;
            DrillDone = true;
            if (Screen != AppScreen.Playing) Screen = AppScreen.DuelEnd;
        }

        // ---- actions (called through Do from the UI) ------------------------------------------------
        public void SetStreamer(bool on)
        {
            Settings.StreamerMode = on;
            Settings.Save();
            if (V4 && Connected) Client.Send(Wire.Streamer(on));
        }

        public void OpenPersonas()
        {
            if (Screen == AppScreen.Connect) return;
            if (V4 && !personasAsked && Client.Send(Wire.Personas())) personasAsked = true;
            Screen = AppScreen.PersonaSelect;
        }

        public void OpenDuel(string persona)
        {
            if (string.IsNullOrEmpty(persona)) return;
            DuelPersona = persona;
            if (V2 && Maps == null) Client.Send(Wire.Maps());
            if (string.IsNullOrEmpty(DuelMapId)) DuelMapId = PickedMapId ?? (Map != null ? Map.id : null) ?? RememberedMap;
            List<string> sites = DuelSites();
            if (sites.Count > 0 && !sites.Contains(DuelSite)) DuelSite = sites[0];
            Screen = AppScreen.DuelSetup;
        }

        /// <summary>The site letters of the duel's map (the loaded map when it is that one; A and B otherwise).</summary>
        public List<string> DuelSites()
        {
            var list = new List<string>();
            if (Map != null && (string.IsNullOrEmpty(DuelMapId) || DuelMapId == Map.id))
                foreach (KeyValuePair<string, List<Vec2>> s in Map.SiteList()) list.Add(s.Key);
            if (list.Count == 0) { list.Add("A"); list.Add("B"); }
            return list;
        }

        public void StartDuel()
        {
            if (!V4)
            {
                ShowToast("This bridge has no persona duels (it needs bots-wire.4).");
                return;
            }
            if (string.IsNullOrEmpty(DuelPersona)) return;
            ResetMatch();
            InMatch = true;
            InDuel = true;
            Send(Wire.Duel(DuelPersona, DuelMapId, DuelSite), "duel");
            if (WaitingFor != "duel")
            {
                InMatch = false;
                InDuel = false;
            }
        }

        public void BackFromDuelEnd()
        {
            if (InMatch && Connected && !DrillDone) Client.Send(Wire.Abandon());
            ResetMatch();
            Drill = null;
            World.Clear();
            Fx.Clear();
            Screen = AppScreen.PersonaSelect;
        }

        public void OpenPersonaMatch()
        {
            SetPersonaEnemies(true);
            OpenMatchSetup();
        }

        public void SetPersonaEnemies(bool on)
        {
            PersonaEnemies = on && V4;
            if (PersonaEnemies && EnemyPersonas.Count == 0)
                foreach (Persona p in Personas.All)
                    if (EnemyPersonas.Count < 5) EnemyPersonas.Add(p.Id);
        }

        public void ToggleEnemyPersona(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (EnemyPersonas.Contains(id)) EnemyPersonas.Remove(id);
            else if (EnemyPersonas.Count < 5) EnemyPersonas.Add(id);
        }

        /// <summary>The match message for the setup: with the persona enemy team when chosen and the bridge speaks .4.</summary>
        JObject MatchMessage()
        {
            if (V4 && PersonaEnemies && EnemyPersonas.Count > 0) return Wire.Match(MatchMapId, MatchAgentId, EnemyPersonas);
            return Wire.Match(MatchMapId, MatchAgentId);
        }

        /// <summary>From ResetMatch: the duel's per-match state (the chosen persona, map and site stay).</summary>
        void ResetDuel()
        {
            InDuel = false;
            DuelEnd = null;
            DuelEndAt = -10f;
            duelTally.player = 0;
            duelTally.enemy = 0;
            bannerRound = -1;
            Alert = null;
            alertQueue.Clear();
            Callouts.Clear();
        }

        // ---- per round and per event ----------------------------------------------------------------
        /// <summary>From OnDrill: a new round starts.</summary>
        void RoundStartRound5()
        {
            roundKills = 0;
            roundFirstBlood = false;
            for (int i = 0; i < Indicators.Length; i++) Indicators[i].At = -10f;
        }

        /// <summary>The "ROUND 3 — FORCE BUY" sweep, once per round number.</summary>
        void ShowRoundBanner()
        {
            if (Round <= 0 || Round == bannerRound) return;
            bannerRound = Round;
            string label = Weapons.RoundTypeLabel(RoundType);
            RoundBannerText = "ROUND " + Round + (InDuel ? "  \u2014  DUEL" : label.Length > 0 ? "  \u2014  " + label : "");
            if (InDuel) RoundBannerSub = "F I R S T   T O   5   \u00b7   V S   " + DuelPersonaName.ToUpperInvariant();
            else RoundBannerSub = (string.IsNullOrEmpty(MatchSide) ? "" : MatchSide.ToUpperInvariant() + "   \u00b7   ") + "FIRST TO 7";
            RoundBannerAt = Time.unscaledTime;
        }

        /// <summary>From OnResult during a duel: counts the round when the bridge sends no score of its own.</summary>
        void NoteDuelResult(ResultMsg r)
        {
            if (!InDuel || r == null || Score != null) return;
            if (r.success) duelTally.player++;
            else duelTally.enemy++;
        }

        /// <summary>From OnEvent("damage"): the bot flinches, and a hit on the player marks the direction it came from.</summary>
        void DamageRound5(SimEvent e)
        {
            if (e.victim == PlayerId)
            {
                if (e.attackerPos != null)
                {
                    Indicators[nextIndicator] = new DamageIndicator { From = Geo.ToV2(e.attackerPos), At = Time.unscaledTime };
                    nextIndicator = (nextIndicator + 1) % Indicators.Length;
                }
                return;
            }
            Vector3 to = e.victimPos != null ? Geo.ToWorld(e.victimPos) : World.InterpPos(e.victim);
            Vector3 from = e.attackerPos != null ? Geo.ToWorld(e.attackerPos) : (e.attacker == PlayerId ? Rig.transform.position : to - Vector3.forward);
            World.NoteHit(e.victim, to - from);
            if (e.attacker == PlayerId) viewerBase += e.part == "head" ? 3f : 1f;
        }

        /// <summary>From OnEvent("kill"): viewers and hype, and (without a bots-wire.4 feed) the overlay's own alerts.</summary>
        void KillRound5(SimEvent e)
        {
            bool first = !roundFirstBlood;
            roundFirstBlood = true;
            if (e.killer != PlayerId) return;
            SessionKills++;
            roundKills++;
            viewerBase += e.part == "head" ? 45f : 28f;
            ViewerBumpAt = Time.unscaledTime;
            Hype = Mathf.Min(1f, Hype + 0.2f);
            if (V4 || !Settings.StreamerMode) return;
            if (roundKills >= 5) PushAlert("ace", "ACE", true);
            else if (roundKills >= 2) PushAlert("multi_kill", roundKills == 2 ? "DOUBLE KILL" : roundKills == 3 ? "TRIPLE KILL" : "QUADRA KILL", roundKills >= 3);
            else if (first) PushAlert("first_blood", "FIRST BLOOD", false);
        }

        /// <summary>Did a kill go through a tall wall (the kill event's flag, else the floor plan between the two)?</summary>
        bool IsWallbang(SimEvent e)
        {
            if (e.wallbang != null) return Lenient.Bool(e.wallbang);
            if (Map?.walls == null || e.killerPos == null || e.victimPos == null) return false;
            var a = new Vector2(e.killerPos.x, e.killerPos.y);
            var b = new Vector2(e.victimPos.x, e.victimPos.y);
            foreach (WallDef w in Map.walls)
            {
                // Low cover is shot over, not through.
                if (w.points == null || w.points.Count < 3 || w.height < 1.8f) continue;
                if (w.id != null && w.id.StartsWith("outer")) continue;
                for (int i = 0, j = w.points.Count - 1; i < w.points.Count; j = i++)
                {
                    var p = new Vector2(w.points[j].x, w.points[j].y);
                    var q = new Vector2(w.points[i].x, w.points[i].y);
                    if (Cross(b - a, p - a) * Cross(b - a, q - a) < 0f && Cross(q - p, a - p) * Cross(q - p, b - p) < 0f) return true;
                }
            }
            return false;
        }

        static float Cross(Vector2 u, Vector2 v)
        {
            return u.x * v.y - u.y * v.x;
        }

        /// <summary>Per frame: alerts and callouts age, hype cools, the duel end screen follows its banner, the damage effect.</summary>
        void TickRound5()
        {
            float now = Time.unscaledTime;
            if (Alert != null && now - Alert.At > Alert.Duration) Alert = null;
            if (Alert == null && alertQueue.Count > 0)
            {
                Alert = alertQueue.Dequeue();
                Alert.At = now;
            }
            for (int i = Callouts.Count - 1; i >= 0; i--)
                if (now - Callouts[i].At > 5f) Callouts.RemoveAt(i);
            Hype = Mathf.MoveTowards(Hype, 0f, Time.unscaledDeltaTime * 0.08f);
            if (DuelEnd != null && Screen == AppScreen.Playing && now >= DuelEndAt + 3f) Screen = AppScreen.DuelEnd;
            if (damageFx != null)
            {
                float dmg = 1f - Mathf.Clamp01((now - DamageAt) / 0.6f);
                AgentState me = Me;
                float low = Screen == AppScreen.Playing && me != null && me.alive && me.hp <= 30f ? 0.25f : 0f;
                damageFx.Set(Screen == AppScreen.Playing ? Mathf.Max(dmg, low) : 0f);
            }
        }
    }
}

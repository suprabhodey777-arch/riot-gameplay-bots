// Match mode, the buy phase, the economy, the player's weapons and the
// graphics preset (docs/weapons-economy.md, bots-wire.3). Everything degrades:
// a bridge without bots-wire.3 never sees a match button, a buy, a sell, a
// ready or the new input fields, and every message field may be absent.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Saltworks
{
    public partial class GameController
    {
        // ---- weapons ------------------------------------------------------------------------------
        /// <summary>The player's weapons (slots, ammo, reload, spray), predicted here and synced from the state.</summary>
        public Gunplay Gun { get; } = new Gunplay();
        /// <summary>The bridge speaks bots-wire.3 (or sent a weapon table): matches, buying and weapon inputs.</summary>
        public bool V3 { get; private set; }
        public int ShotsFired { get; private set; }

        bool semiReleased = true;
        bool crouchToggled, walkToggled;
        bool crouchNow, walkNow;
        float lastScrollAt = -10f;

        // ---- match --------------------------------------------------------------------------------
        public bool InMatch { get; private set; }
        public BuyPhaseMsg BuyPhase { get; private set; }
        public float BuyEndsAt { get; private set; }
        /// <summary>The buy menu is shown (B toggles it during the buy phase).</summary>
        public bool BuyOpen { get; set; }
        public LoadoutInfo Loadout { get; private set; }
        /// <summary>The player's credits (-1 = unknown).</summary>
        public int Credits { get; private set; } = -1;
        public ScoreInfo Score { get; private set; }
        public int Round { get; private set; }
        public string RoundType { get; private set; }
        /// <summary>The player's side this round ("attack" / "defense"), from buy_phase.side or the drill.</summary>
        public string MatchSide { get; private set; }
        public float SideSwapAt { get; private set; } = -10f;
        public RoundResultMsg RoundResult { get; private set; }
        public float RoundResultAt { get; private set; } = -10f;
        public MatchEndMsg MatchEnd { get; private set; }
        public float MatchEndAt { get; private set; } = -10f;
        public int MatchKills { get; private set; }
        public int MatchDeaths { get; private set; }
        public int MatchHeadKills { get; private set; }
        /// <summary>Per finished round: did the player's team win it.</summary>
        public readonly List<bool> RoundHistory = new List<bool>();
        public string MatchMapId;
        public string MatchAgentId;
        bool readySent;

        /// <summary>The buy phase is open for the player (not readied, timer running).</summary>
        public bool BuyPhaseActive => InMatch && BuyPhase != null && !readySent && Time.unscaledTime < BuyEndsAt;
        public float BuyTimeLeft => BuyPhase == null ? 0f : Mathf.Max(0f, BuyEndsAt - Time.unscaledTime);
        public bool Readied => readySent;

        // ---- damage numbers -------------------------------------------------------------------------
        public readonly List<DamageNumber> DamageNumbers = new List<DamageNumber>();

        // ---- graphics -------------------------------------------------------------------------------
        GameObject propsRoot;
        float propsDensity = -1f;
        int gfxVersion = -1;

        void ApplyGraphics()
        {
            Gfx.ApplyQuality();
            if (Rig != null) Gfx.ApplyCamera(Rig.Cam, Rig.Weapon != null ? Rig.Weapon.WeaponCam : null, Rig.Bloom);
        }

        /// <summary>Applies the graphics preset when the settings changed; rebuilds the props when their density did.</summary>
        void UpdateGraphics()
        {
            if (gfxVersion == Settings.Version) return;
            gfxVersion = Settings.Version;
            ApplyGraphics();
            if (Map != null && Mathf.Abs(propsDensity - Gfx.PropDensity) > 0.01f) RebuildProps();
        }

        void RebuildProps()
        {
            if (propsRoot != null)
            {
                // Runtime meshes are not freed with their GameObject.
                foreach (MeshFilter mf in propsRoot.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
                Destroy(propsRoot);
            }
            propsRoot = null;
            if (Map == null) return;
            propsDensity = Gfx.PropDensity;
            try
            {
                propsRoot = PropBuilder.Build(Map, propsDensity);
                if (mapRoot != null) propsRoot.transform.SetParent(mapRoot.transform, false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Saltworks: could not build the map's props: " + ex);
            }
        }

        // ---- weapon input ------------------------------------------------------------------------------
        void WeaponInput(float now)
        {
            if (GameInput.Pressed(GameKey.Primary)) Gun.RequestSlot(true, now);
            if (GameInput.Pressed(GameKey.Secondary)) Gun.RequestSlot(false, now);
            float sc = GameInput.Scroll;
            if (Mathf.Abs(sc) > 0.01f && now - lastScrollAt > 0.15f)
            {
                lastScrollAt = now;
                Gun.Cycle(now);
            }
            if (GameInput.Pressed(GameKey.Reload)) Gun.RequestReload(now);
        }

        /// <summary>The loadout for a new round: the sim's state, else the drill's loadout, else the match loadout, else Lancer and Pin.</summary>
        void ResetGunFor(DrillMsg d, AgentState mine)
        {
            string p = Weapons.Default, s = Weapons.Sidearm;
            if (mine != null && !string.IsNullOrEmpty(mine.secondary))
            {
                p = mine.primary;
                s = mine.secondary;
            }
            else if (d?.spec?.loadout != null && (d.spec.loadout.primary != null || d.spec.loadout.secondary != null))
            {
                p = d.spec.loadout.primary;
                s = d.spec.loadout.secondary ?? Weapons.Sidearm;
            }
            else if (InMatch && Loadout != null)
            {
                p = Loadout.primary;
                s = Loadout.secondary ?? Weapons.Sidearm;
            }
            Gun.Legacy = !V3;
            Gun.Reset(p, s);
            if (mine != null) Gun.Sync(mine, d?.state, Rules.tickRate, Time.unscaledTime);
            if (mine != null && mine.credits.HasValue) Credits = mine.credits.Value;
            else if (d?.spec?.loadout?.credits != null) Credits = d.spec.loadout.credits.Value;
            ShotsFired = 0;
            semiReleased = false;
            crouchToggled = false;
            walkToggled = false;
        }

        // ---- the kill feed and damage numbers -----------------------------------------------------------
        string ShortName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            return id == PlayerId ? "You" : id;
        }

        void AddKill(SimEvent e)
        {
            string weapon = e.weapon;
            if (string.IsNullOrEmpty(weapon))
            {
                if (e.killer == PlayerId) weapon = Gun.Current;
                else weapon = World.Agent(e.killer)?.WeaponInHand ?? Weapons.Default;
            }
            var item = new FeedItem
            {
                IsKill = true,
                Killer = ShortName(e.killer),
                Victim = ShortName(e.victim),
                Weapon = weapon,
                Head = e.part == "head",
                KillerColor = Palette.Relation(TeamOf(e.killer) ?? "attack", PlayerTeam),
                VictimColor = Palette.Relation(TeamOf(e.victim) ?? "defense", PlayerTeam),
                Color = TeamColorOf(e.killer),
                At = Time.unscaledTime,
            };
            item.Text = item.Killer + "  " + Weapons.NameOf(weapon) + "  " + item.Victim;
            Feed.Add(item);
            if (Feed.Count > 8) Feed.RemoveAt(0);
        }

        void AddDamageNumber(SimEvent e)
        {
            if (!Settings.DamageNumbers || e.victimPos == null) return;
            int amount = Mathf.RoundToInt(e.amount);
            if (amount <= 0) return;
            if (DamageNumbers.Count >= 16) DamageNumbers.RemoveAt(0);
            float jitter = (DamageNumbers.Count % 3 - 1) * 0.18f;
            Vector3 at = Geo.ToWorld(e.victimPos, e.part == "head" ? 1.95f : 1.7f);
            if (World.Bots.TryGetValue(e.victim ?? "", out BotView b)) at = b.Root.transform.position + Vector3.up * (e.part == "head" ? 1.95f : 1.7f);
            DamageNumbers.Add(new DamageNumber { World = at + Rig.transform.right * jitter, Amount = amount, Head = e.part == "head", At = Time.unscaledTime, Text = Theme.Int(amount) });
        }

        // ---- match flow ---------------------------------------------------------------------------------
        public void OpenMatchSetup()
        {
            if (V2 && Maps == null) Client.Send(Wire.Maps());
            if (string.IsNullOrEmpty(MatchMapId)) MatchMapId = PickedMapId ?? (Map != null ? Map.id : null) ?? RememberedMap;
            if (string.IsNullOrEmpty(MatchAgentId)) MatchAgentId = PickedAgentId ?? (string.IsNullOrEmpty(RememberedAgent) ? "rook" : RememberedAgent);
            Screen = AppScreen.MatchSetup;
        }

        public void StartMatch()
        {
            if (!V3)
            {
                ShowToast("This bridge has no match mode (it needs bots-wire.3).");
                return;
            }
            ResetMatch();
            InMatch = true;
            if (!string.IsNullOrEmpty(MatchAgentId)) PlayerPrefs.SetString(PrefAgent, MatchAgentId);
            if (!string.IsNullOrEmpty(MatchMapId)) PlayerPrefs.SetString(PrefMap, MatchMapId);
            Send(Wire.Match(MatchMapId, MatchAgentId), "match");
            if (WaitingFor != "match") InMatch = false;
        }

        public void LeaveMatch()
        {
            if (InMatch && Connected) Client.Send(Wire.Abandon());
            ResetMatch();
            Drill = null;
            Result = null;
            DrillDone = true;
            Paused = false;
            World.Clear();
            Fx.Clear();
            Screen = AppScreen.Menu;
        }

        public void BuyItem(string item)
        {
            if (!BuyPhaseActive || string.IsNullOrEmpty(item)) return;
            Client.Send(Wire.Buy(item));
        }

        public void SellItem(string item)
        {
            if (!BuyPhaseActive || string.IsNullOrEmpty(item)) return;
            Client.Send(Wire.Sell(item));
        }

        public void ReadyUp()
        {
            if (!InMatch || BuyPhase == null || readySent) return;
            Client.Send(Wire.Ready());
            readySent = true;
            BuyOpen = false;
        }

        public void CloseBuyMenu()
        {
            BuyOpen = false;
        }

        public void ToggleBuyMenu()
        {
            if (BuyPhaseActive) BuyOpen = !BuyOpen;
        }

        public void BackFromMatchEnd()
        {
            ResetMatch();
            Drill = null;
            World.Clear();
            Fx.Clear();
            Screen = AppScreen.Menu;
        }

        void ResetMatch()
        {
            InMatch = false;
            BuyPhase = null;
            BuyOpen = false;
            readySent = false;
            Loadout = null;
            Credits = -1;
            Score = null;
            Round = 0;
            RoundType = null;
            MatchSide = null;
            RoundResult = null;
            RoundResultAt = -10f;
            MatchEnd = null;
            MatchEndAt = -10f;
            MatchKills = 0;
            MatchDeaths = 0;
            MatchHeadKills = 0;
            RoundHistory.Clear();
            SideSwapAt = -10f;
        }

        public string OwnedPrimary
        {
            get
            {
                if (Loadout != null) return string.IsNullOrEmpty(Loadout.primary) ? null : Loadout.primary;
                AgentState me = Me;
                return me != null && !string.IsNullOrEmpty(me.primary) ? me.primary : null;
            }
        }

        public string OwnedSecondary
        {
            get
            {
                if (Loadout != null && !string.IsNullOrEmpty(Loadout.secondary)) return Loadout.secondary;
                AgentState me = Me;
                return me != null && !string.IsNullOrEmpty(me.secondary) ? me.secondary : Weapons.Sidearm;
            }
        }

        public string OwnedArmor
        {
            get
            {
                if (Loadout != null) return Loadout.ArmorKind;
                return Me?.ArmorKind;
            }
        }

        public bool Owns(string item)
        {
            if (string.IsNullOrEmpty(item)) return false;
            if (item == "light" || item == "heavy") return OwnedArmor == item;
            return OwnedPrimary == item || OwnedSecondary == item;
        }

        public int PriceOf(string item)
        {
            if (item == "light" || item == "heavy") return Weapons.ArmorPrice(item);
            WeaponInfo w = Weapons.Find(item);
            return w != null ? w.Price : 0;
        }

        public bool CanAfford(string item)
        {
            return Credits < 0 || PriceOf(item) <= Credits;
        }

        static T ReadLenient<T>(JObject o, string what) where T : class
        {
            try
            {
                return Wire.Read<T>(o);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Saltworks: could not read '" + what + "': " + ex.Message);
                return null;
            }
        }

        void OnBuyPhase(JObject o)
        {
            BuyPhaseMsg b = ReadLenient<BuyPhaseMsg>(o, "buy_phase");
            if (b == null) return;
            if (WaitingFor == "match") WaitingFor = null;
            InMatch = true;
            BuyPhase = b;
            BuyEndsAt = Time.unscaledTime + Mathf.Max(1f, b.timeS > 0f ? b.timeS : 20f);
            readySent = false;
            BuyOpen = true;
            Credits = b.credits;
            if (b.round > 0) Round = b.round;
            else Round++;
            RoundType = b.roundType;
            if (b.score != null) Score = b.score;
            if (!string.IsNullOrEmpty(b.side))
            {
                if (!string.IsNullOrEmpty(MatchSide) && MatchSide != b.side) SideSwapAt = Time.unscaledTime;
                MatchSide = b.side;
            }
            if (b.loadout != null) Loadout = b.loadout;
            if (b.team != null)
            {
                foreach (TeamMember m in b.team)
                {
                    if (m == null || m.id != PlayerId) continue;
                    if (m.loadout != null) Loadout = m.loadout;
                    break;
                }
            }
            if (Loadout != null && Loadout.credits.HasValue) Credits = Loadout.credits.Value;
            bool roundLive = Screen == AppScreen.Playing && Drill != null && !DrillDone;
            if (!roundLive)
            {
                Screen = AppScreen.Buy;
                Paused = false;
            }
        }

        void OnLoadout(JObject o)
        {
            LoadoutInfo l = ReadLenient<LoadoutInfo>(o, "loadout");
            if (l == null) return;
            if (WaitingFor == "buy") WaitingFor = null;
            Loadout = l;
            if (l.credits.HasValue) Credits = l.credits.Value;
            Gun.SetLoadout(l.primary, l.secondary);
        }

        void OnRoundResult(JObject o)
        {
            RoundResultMsg r = ReadLenient<RoundResultMsg>(o, "round_result");
            if (r == null) return;
            RoundResult = r;
            RoundResultAt = Time.unscaledTime;
            if (r.score != null) Score = r.score;
            RoundHistory.Add(RoundWon(r));
            DrillDone = true;
        }

        /// <summary>Did the player's team win this round (by side, else by the score moving).</summary>
        public bool RoundWon(RoundResultMsg r)
        {
            if (r == null) return false;
            string side = MatchSide ?? PlayerTeam;
            if (!string.IsNullOrEmpty(r.winner) && (r.winner == "attack" || r.winner == "defense")) return r.winner == side;
            return r.winner == "player" || r.winner == PlayerTeam;
        }

        void OnMatchEnd(JObject o)
        {
            MatchEndMsg m = ReadLenient<MatchEndMsg>(o, "match_end");
            if (m == null) return;
            MatchEnd = m;
            MatchEndAt = Time.unscaledTime;
            if (m.score != null) Score = m.score;
            BuyOpen = false;
            DrillDone = true;
            if (Screen != AppScreen.Playing) Screen = AppScreen.MatchEnd;
        }

        /// <summary>A match round's drill message arrived.</summary>
        void MatchRoundStarted(DrillMsg d)
        {
            if (!string.IsNullOrEmpty(d.playerTeam)) MatchSide = d.playerTeam;
            // The round is live, so the buy window is over even if the local timer had time left
            // (the bridge starts the round when its own 20 s run out).
            BuyEndsAt = Time.unscaledTime;
            BuyOpen = false;
            if (Round <= 0) Round = 1;
            if (d?.spec?.loadout != null)
            {
                Loadout = d.spec.loadout;
                if (Loadout.credits.HasValue) Credits = Loadout.credits.Value;
            }
        }

        /// <summary>Per frame: the buy timer, and the match end screen after its banner.</summary>
        void TickMatch()
        {
            float now = Time.unscaledTime;
            // Damage numbers age out.
            for (int i = DamageNumbers.Count - 1; i >= 0; i--)
                if (now - DamageNumbers[i].At > 1.1f) DamageNumbers.RemoveAt(i);
            if (!InMatch) return;
            if (BuyOpen && BuyPhase != null && !BuyPhaseActive && Screen == AppScreen.Playing) BuyOpen = false;
            if (MatchEnd != null && Screen == AppScreen.Playing && now >= MatchEndAt + 3f) Screen = AppScreen.MatchEnd;
        }

        /// <summary>The player's team's average credits from the buy phase (for the round-type explanation).</summary>
        public int TeamAverageCredits()
        {
            if (BuyPhase?.team == null || BuyPhase.team.Count == 0) return Credits;
            int sum = 0, n = 0;
            foreach (TeamMember m in BuyPhase.team)
            {
                if (m == null) continue;
                sum += m.credits;
                n++;
            }
            bool hasMe = false;
            foreach (TeamMember m in BuyPhase.team) if (m != null && m.id == PlayerId) hasMe = true;
            if (!hasMe && Credits >= 0) { sum += Credits; n++; }
            return n > 0 ? sum / n : Credits;
        }
    }
}

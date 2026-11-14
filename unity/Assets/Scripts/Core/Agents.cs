// The four playable agents (docs/abilities.md) and the four abilities: names,
// colours, keys and one-line descriptions for the screens and the HUD. Roles and
// kits default to the doc's table and are replaced by hello.rules.agents when the
// bridge sends them.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Saltworks
{
    public class AgentInfo
    {
        public string Id;
        public string Name;
        public string Role;
        public string Blurb;
        public Color Color;
        public AbilityCharges Kit = new AbilityCharges();
    }

    public static class Agents
    {
        public static readonly Ability[] AllAbilities = { Ability.Smoke, Ability.Flash, Ability.Dash, Ability.Recon };

        public static readonly List<AgentInfo> All = new List<AgentInfo>
        {
            new AgentInfo
            {
                Id = "rook", Name = "Rook", Role = "duelist", Blurb = "Takes the first fight: dash into the angle, flash the holder.",
                Color = new Color(1.00f, 0.60f, 0.16f), Kit = new AbilityCharges { dash = 1, flash = 1 },
            },
            new AgentInfo
            {
                Id = "haze", Name = "Haze", Role = "controller", Blurb = "Cuts sight lines for the team with three smokes.",
                Color = new Color(0.66f, 0.52f, 1.00f), Kit = new AbilityCharges { smoke = 3 },
            },
            new AgentInfo
            {
                Id = "wren", Name = "Wren", Role = "initiator", Blurb = "Finds defenders with recon, then blinds them for the entry.",
                Color = new Color(0.62f, 0.90f, 0.28f), Kit = new AbilityCharges { flash = 2, recon = 1 },
            },
            new AgentInfo
            {
                Id = "pike", Name = "Pike", Role = "sentinel", Blurb = "Holds a site and watches the flank with two recon darts.",
                Color = new Color(0.30f, 0.64f, 1.00f), Kit = new AbilityCharges { recon = 2, smoke = 1 },
            },
        };

        public static AgentInfo Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (AgentInfo a in All)
                if (a.Id == id) return a;
            return null;
        }

        public static string NameOf(string id)
        {
            AgentInfo a = Get(id);
            return a != null ? a.Name : (string.IsNullOrEmpty(id) ? "" : char.ToUpperInvariant(id[0]) + id.Substring(1));
        }

        public static Color ColorOf(string id)
        {
            AgentInfo a = Get(id);
            return a != null ? a.Color : new Color(0.8f, 0.8f, 0.85f);
        }

        /// <summary>"Dash x1, Flash x1" in the doc's order of the kit.</summary>
        public static string KitText(AbilityCharges kit)
        {
            if (kit == null) return "";
            var parts = new List<string>();
            foreach (Ability ab in AllAbilities)
            {
                int n = kit.Get(ab);
                if (n > 0) parts.Add(AbilityName(ab) + " \u00d7" + n);
            }
            return parts.Count == 0 ? "no abilities" : string.Join(", ", parts);
        }

        /// <summary>
        /// Reads hello.rules.agents. Accepts an array of { id, role, kit, name? } or an object keyed by agent id
        /// ({ rook: { role, kit } }); a kit is { smoke, flash, dash, recon } or a list of ability names.
        /// Anything unreadable keeps the built-in table.
        /// </summary>
        public static void MergeFromRules(JToken agents)
        {
            if (agents == null || agents.Type == JTokenType.Null) return;
            try
            {
                if (agents is JArray arr)
                {
                    foreach (JToken t in arr)
                        if (t is JObject o) Merge((string)o["id"], o);
                }
                else if (agents is JObject obj)
                {
                    foreach (JProperty p in obj.Properties())
                        if (p.Value is JObject o) Merge(p.Name, o);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("Saltworks: could not read rules.agents, keeping the built-in table: " + ex.Message);
            }
        }

        static void Merge(string id, JObject o)
        {
            if (string.IsNullOrEmpty(id)) return;
            AgentInfo a = Get(id);
            if (a == null)
            {
                a = new AgentInfo { Id = id, Name = NameOf(id), Blurb = "", Color = new Color(0.8f, 0.8f, 0.85f) };
                All.Add(a);
            }
            if (o["name"] != null && o["name"].Type == JTokenType.String) a.Name = (string)o["name"];
            if (o["role"] != null && o["role"].Type == JTokenType.String) a.Role = (string)o["role"];
            AbilityCharges kit = ReadKit(o["kit"]);
            if (kit != null) a.Kit = kit;
        }

        public static AbilityCharges ReadKit(JToken t)
        {
            if (t == null) return null;
            if (t is JObject ko)
            {
                return new AbilityCharges
                {
                    smoke = Int(ko["smoke"]),
                    flash = Int(ko["flash"]),
                    dash = Int(ko["dash"]),
                    recon = Int(ko["recon"]),
                };
            }
            if (t is JArray ka)
            {
                var k = new AbilityCharges();
                foreach (JToken e in ka)
                {
                    switch (e.Type == JTokenType.String ? (string)e : "")
                    {
                        case "smoke": k.smoke++; break;
                        case "flash": k.flash++; break;
                        case "dash": k.dash++; break;
                        case "recon": k.recon++; break;
                    }
                }
                return k;
            }
            return null;
        }

        static int Int(JToken t)
        {
            if (t == null) return 0;
            return t.Type == JTokenType.Integer || t.Type == JTokenType.Float ? (int)(double)t : 0;
        }

        // ---- abilities ---------------------------------------------------------------------------

        public static string AbilityName(Ability a)
        {
            switch (a)
            {
                case Ability.Smoke: return "Smoke";
                case Ability.Flash: return "Flash";
                case Ability.Dash: return "Dash";
                default: return "Recon";
            }
        }

        public static string KeyLabel(Ability a)
        {
            switch (a)
            {
                case Ability.Smoke: return "C";
                case Ability.Flash: return "Q";
                case Ability.Dash: return "E";
                default: return "X";
            }
        }

        public static GameKey Key(Ability a)
        {
            switch (a)
            {
                case Ability.Smoke: return GameKey.Smoke;
                case Ability.Flash: return GameKey.Flash;
                case Ability.Dash: return GameKey.Dash;
                default: return GameKey.Recon;
            }
        }

        public static Color AbilityColor(Ability a)
        {
            switch (a)
            {
                case Ability.Smoke: return Palette.Smoke;
                case Ability.Flash: return new Color(1.00f, 0.95f, 0.62f);
                case Ability.Dash: return new Color(0.55f, 0.95f, 1.00f);
                default: return new Color(1.00f, 0.42f, 0.55f);
            }
        }

        /// <summary>Thrown at a floor point (as opposed to Dash, used on the spot).</summary>
        public static bool Aimed(Ability a)
        {
            return a != Ability.Dash;
        }
    }
}

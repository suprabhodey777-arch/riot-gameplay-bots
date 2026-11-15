// The five pro personas (docs/streamer-mode.md): original characters that play
// a famous style, never a real person. The built-in table carries the doc's
// names, styles, kits and weapon preferences so the screens work before the
// bridge answers; the bridge's `personas` message replaces taglines, voice
// lines, colours, the signature stat and the "how to beat them" tip.
using System.Collections.Generic;
using UnityEngine;

namespace Saltworks
{
    public class Persona
    {
        public string Id;
        /// <summary>The persona's own name (the built-in table's, or the bridge's).</summary>
        public string DefaultName;

        /// <summary>
        /// The name shown everywhere in the client: the player's custom name for this persona when one is set
        /// (Personas.SetCustomName), else the default. Setting it sets the default.
        /// </summary>
        public string Name
        {
            get
            {
                string custom = Personas.CustomName(Id);
                return string.IsNullOrEmpty(custom) ? DefaultName : custom;
            }
            set => DefaultName = value;
        }

        public string Style;
        public string Tagline;
        /// <summary>The agent whose kit the persona plays (its figure carries that agent's pack).</summary>
        public string Agent;
        /// <summary>The weapon the figure holds on the select screens.</summary>
        public string Weapon;
        public string Signature;
        public string BeatTip;
        public Color Primary;
        public Color Secondary;
        public List<string> Lines = new List<string>();
        /// <summary>True once the bridge has described this persona.</summary>
        public bool FromBridge;
    }

    public static class Personas
    {
        public static readonly List<Persona> All = new List<Persona>
        {
            new Persona
            {
                Id = "zephyr", Name = "Zephyr", Style = "Hyper-aggressive flick duelist", Agent = "rook", Weapon = "viper",
                Tagline = "First in, first fight, every round.", Signature = "Takes the opening duel most rounds",
                BeatTip = "He wide-swings: hold a tight angle off the corner, not the corner itself.",
                Primary = new Color(1f, 0.27f, 0.33f), Secondary = new Color(1f, 0.82f, 0.4f),
            },
            new Persona
            {
                Id = "atlas", Name = "Atlas", Style = "Calm in-game leader", Agent = "haze", Weapon = "lancer",
                Tagline = "Nobody peeks alone.", Signature = "Trades a fallen teammate within 1.5 s",
                BeatTip = "He trades fast: after a kill, fall back off the angle instead of holding it for the second player.",
                Primary = new Color(0.36f, 0.62f, 1f), Secondary = new Color(0.86f, 0.9f, 1f),
            },
            new Persona
            {
                Id = "ember", Name = "Ember", Style = "Operator anchor", Agent = "pike", Weapon = "longshot",
                Tagline = "One angle. One shot. New spot.", Signature = "Never takes two shots from the same spot",
                BeatTip = "She re-positions after every shot: do not re-peek where she just was, flash or smoke the long angle.",
                Primary = new Color(1f, 0.55f, 0.18f), Secondary = new Color(1f, 0.86f, 0.55f),
            },
            new Persona
            {
                Id = "wisp", Name = "Wisp", Style = "Lurker", Agent = "pike", Weapon = "wasp",
                Tagline = "You will hear the other lane. Not me.", Signature = "Most kills from behind the push",
                BeatTip = "He waits for your push: leave one player watching the flank and clear behind before you commit.",
                Primary = new Color(0.62f, 0.5f, 1f), Secondary = new Color(0.4f, 1f, 0.85f),
            },
            new Persona
            {
                Id = "nova", Name = "Nova", Style = "Utility general", Agent = "wren", Weapon = "lancer",
                Tagline = "I light the way, you take the site.", Signature = "Recon before every push",
                BeatTip = "She recons before the push: break line of sight with the dart's ring, then punish the flash that follows.",
                Primary = new Color(0.4f, 1f, 0.6f), Secondary = new Color(1f, 1f, 1f),
            },
        };

        // ---- custom display names (PlayerPrefs "sw.persona.name.<id>") ------------------------------
        public const int MaxNameLength = 18;
        const string NamePref = "sw.persona.name.";
        static readonly Dictionary<string, string> customNames = new Dictionary<string, string>();
        static readonly HashSet<string> namesLoaded = new HashSet<string>();

        /// <summary>The player's name for persona `id`, or null when none is set.</summary>
        public static string CustomName(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (!namesLoaded.Contains(id))
            {
                namesLoaded.Add(id);
                string saved = Clean(PlayerPrefs.GetString(NamePref + id, ""));
                if (saved.Length > 0) customNames[id] = saved;
            }
            return customNames.TryGetValue(id, out string n) ? n : null;
        }

        /// <summary>Sets (or with an empty name clears) the player's name for persona `id`; `flush` writes PlayerPrefs to disk.</summary>
        public static void SetCustomName(string id, string name, bool flush = true)
        {
            if (string.IsNullOrEmpty(id)) return;
            namesLoaded.Add(id);
            string clean = Clean(name);
            Persona p = Get(id);
            if (clean.Length == 0 || (p != null && clean == p.DefaultName))
            {
                customNames.Remove(id);
                PlayerPrefs.DeleteKey(NamePref + id);
            }
            else
            {
                customNames[id] = clean;
                PlayerPrefs.SetString(NamePref + id, clean);
            }
            if (flush) PlayerPrefs.Save();
        }

        /// <summary>What a name field keeps while the player types: no control characters or brackets, capped (spaces kept).</summary>
        public static string FilterTyped(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if (char.IsControl(ch) || ch == '<' || ch == '>') continue;
                if (sb.Length >= MaxNameLength) break;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>A typed name made safe to show: printable characters only, no rich-text brackets, trimmed, capped.</summary>
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if (char.IsControl(ch) || ch == '<' || ch == '>') continue;
                sb.Append(ch);
                if (sb.Length >= MaxNameLength) break;
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Text from the bridge with every persona's default name (whole words, any case) replaced by the player's
        /// custom name, so chat and callouts use the names the player chose. Unchanged when no custom name is set.
        /// </summary>
        public static string Rename(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (Persona p in All)
            {
                string custom = CustomName(p.Id);
                if (string.IsNullOrEmpty(custom) || string.IsNullOrEmpty(p.DefaultName)) continue;
                text = ReplaceWord(text, p.DefaultName, custom);
                if (!string.Equals(p.Id, p.DefaultName, System.StringComparison.OrdinalIgnoreCase)) text = ReplaceWord(text, p.Id, custom);
            }
            return text;
        }

        static string ReplaceWord(string text, string word, string with)
        {
            int at = 0;
            while (at < text.Length)
            {
                int i = text.IndexOf(word, at, System.StringComparison.OrdinalIgnoreCase);
                if (i < 0) break;
                int end = i + word.Length;
                bool startOk = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
                bool endOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (startOk && endOk)
                {
                    text = text.Substring(0, i) + with + text.Substring(end);
                    at = i + with.Length;
                }
                else at = i + 1;
            }
            return text;
        }

        /// <summary>The persona a chat user name or id refers to (id or default name, any case), or null.</summary>
        public static Persona Match(string nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId)) return null;
            string s = nameOrId.Trim();
            foreach (Persona p in All)
                if (string.Equals(p.Id, s, System.StringComparison.OrdinalIgnoreCase) || string.Equals(p.DefaultName, s, System.StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        public static Persona Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (Persona p in All)
                if (p.Id == id) return p;
            return null;
        }

        public static string NameOf(string id)
        {
            Persona p = Get(id);
            return p != null ? p.Name : Agents.NameOf(id);
        }

        /// <summary>Folds one entry of the bridge's `personas` answer into the table (new ids are added).</summary>
        public static void Merge(PersonaInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.id)) return;
            Persona p = Get(info.id);
            if (p == null)
            {
                p = new Persona { Id = info.id, Name = Agents.NameOf(info.id), Agent = "rook", Weapon = Weapons.Default, Primary = Settings.Accent, Secondary = Color.white };
                All.Add(p);
            }
            p.FromBridge = true;
            if (!string.IsNullOrEmpty(info.name)) p.Name = info.name;
            if (!string.IsNullOrEmpty(info.style)) p.Style = info.style;
            if (!string.IsNullOrEmpty(info.tagline)) p.Tagline = info.tagline;
            if (!string.IsNullOrEmpty(info.agent)) p.Agent = info.agent;
            if (!string.IsNullOrEmpty(info.beatTip)) p.BeatTip = info.beatTip;
            string sig = Lenient.Text(info.signature);
            if (!string.IsNullOrEmpty(sig)) p.Signature = sig;
            List<string> lines = Lenient.Strings(info.lines);
            if (lines.Count > 0) p.Lines = lines;
            List<string> colors = Lenient.Strings(info.colors);
            if (colors.Count > 0) p.Primary = Lenient.Color(colors[0], p.Primary);
            if (colors.Count > 1) p.Secondary = Lenient.Color(colors[1], p.Secondary);
        }

        /// <summary>The persona a drill bot plays: its `persona` field, else an archetype that names a persona, else null.</summary>
        public static Persona ForBot(DrillBotInfo b)
        {
            if (b == null) return null;
            Persona p = Get(Lenient.Str(b.persona));
            if (p == null && b.persona is Newtonsoft.Json.Linq.JObject o) p = Get(Lenient.Str(o["id"]));
            return p ?? Get(b.archetype);
        }
    }
}

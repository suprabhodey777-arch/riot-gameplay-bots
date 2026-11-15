// The seven weapons and two armours of docs/weapons-economy.md. The built-in
// table is the doc's numbers plus our own spray patterns; hello.rules.weapons
// replaces any field the bridge sends (read leniently: an object keyed by id or
// an array of { id, ... }; several plausible key spellings per field). The
// client uses spray, recoveryS, baseSpreadDeg, pellets, the fire interval, the
// magazine and the reload time; damage and falloff are shown, never applied
// (the sim decides damage).
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Saltworks
{
    public enum WeaponClass
    {
        Sidearm,
        HeavyPistol,
        Smg,
        Shotgun,
        Rifle,
        Sniper,
    }

    public class WeaponInfo
    {
        public string Id;
        public string Name;
        public WeaponClass Class;
        public int Price;
        public float Head;
        public float Body;
        public float FireIntervalS;
        public int Mag;
        public float ReloadS;
        public int Pellets = 1;
        /// <summary>Random cone of each pellet (degrees), shotguns only. Not in the doc: a client constant.</summary>
        public float PelletConeDeg;
        public float BaseSpreadDeg;
        public float RecoveryS;
        /// <summary>Per bullet of a held spray: x = pitch up, y = yaw right, in degrees from the aim point.</summary>
        public List<Vector2> Spray = new List<Vector2>();
        public string Falloff = "";
        /// <summary>Held trigger repeats (rifles, SMG); otherwise one shot per click.</summary>
        public bool Auto;
        /// <summary>How much of the spray offset the camera shows (the rest is where bullets go above the crosshair).</summary>
        public float KickFraction = 0.4f;
        public float DrawS = 0.5f;
        /// <summary>0..1 for the buy menu's mobility bar.</summary>
        public float Mobility = 0.8f;

        public bool IsPrimary => Class != WeaponClass.Sidearm && Class != WeaponClass.HeavyPistol;
        public bool Scopes => Class == WeaponClass.Sniper;
        public string ClassName => Weapons.ClassName(Class);

        /// <summary>The spray offset for a (fractional) bullet index, interpolated, the last entry held past the end.</summary>
        public Vector2 SprayAt(float index)
        {
            if (Spray == null || Spray.Count == 0 || index <= 0f) return Vector2.zero;
            int n = Spray.Count;
            int i = Mathf.FloorToInt(index);
            float f = index - i;
            Vector2 a = Spray[Mathf.Clamp(i, 0, n - 1)];
            Vector2 b = Spray[Mathf.Clamp(i + 1, 0, n - 1)];
            return Vector2.Lerp(a, b, f);
        }

        /// <summary>Damage for the buy menu's bar: body damage per trigger pull (all pellets for a shotgun).</summary>
        public float BodyPerShot => Body * Mathf.Max(1, Pellets);
    }

    public static class Weapons
    {
        public static readonly string[] Order = { "pin", "hammer", "wasp", "brick", "lancer", "viper", "longshot" };
        public const string Default = "lancer";
        public const string Sidearm = "pin";

        public const int LightArmorPrice = 400;
        public const int HeavyArmorPrice = 1000;
        public static int LightPrice = LightArmorPrice;
        public static int HeavyPrice = HeavyArmorPrice;
        public static int LightPoints = 25;
        public static int HeavyPoints = 50;

        static readonly Dictionary<string, WeaponInfo> table = new Dictionary<string, WeaponInfo>();
        static bool built;

        static List<Vector2> P(params float[] xy)
        {
            var l = new List<Vector2>(xy.Length / 2);
            for (int i = 0; i + 1 < xy.Length; i += 2) l.Add(new Vector2(xy[i], xy[i + 1]));
            return l;
        }

        static void Build()
        {
            if (built) return;
            built = true;
            Add(new WeaponInfo
            {
                Id = "pin", Name = "Pin", Class = WeaponClass.Sidearm, Price = 0, Head = 75, Body = 26, FireIntervalS = 0.16f, Mag = 12, ReloadS = 1.5f,
                BaseSpreadDeg = 0.9f, RecoveryS = 0.22f, DrawS = 0.3f, Mobility = 1f, KickFraction = 0.5f,
                Spray = P(0f, 0f, 0.9f, 0.1f, 1.5f, -0.15f, 1.9f, 0.2f, 2.2f, -0.25f, 2.4f, 0.3f, 2.5f, -0.2f, 2.6f, 0.25f, 2.7f, -0.3f, 2.75f, 0.2f, 2.8f, -0.1f, 2.85f, 0.15f),
                Falloff = "",
            });
            Add(new WeaponInfo
            {
                Id = "hammer", Name = "Hammer", Class = WeaponClass.HeavyPistol, Price = 500, Head = 150, Body = 50, FireIntervalS = 0.40f, Mag = 6, ReloadS = 2.0f,
                BaseSpreadDeg = 1.4f, RecoveryS = 0.36f, DrawS = 0.4f, Mobility = 0.95f, KickFraction = 0.55f,
                Spray = P(0f, 0f, 2.6f, 0.25f, 4.2f, -0.35f, 5.2f, 0.4f, 5.8f, -0.3f, 6.1f, 0.3f),
                Falloff = "head 110 beyond 15 m",
            });
            Add(new WeaponInfo
            {
                Id = "wasp", Name = "Wasp", Class = WeaponClass.Smg, Price = 950, Head = 70, Body = 27, FireIntervalS = 0.075f, Mag = 25, ReloadS = 2.2f,
                BaseSpreadDeg = 0.55f, RecoveryS = 0.24f, Auto = true, DrawS = 0.45f, Mobility = 0.95f, KickFraction = 0.35f,
                Spray = P(0f, 0f, 0.3f, 0.12f, 0.6f, -0.15f, 0.85f, 0.2f, 1.0f, -0.22f, 1.15f, 0.26f, 1.25f, -0.2f, 1.32f, 0.3f, 1.38f, -0.32f, 1.42f, 0.28f,
                          1.46f, -0.24f, 1.5f, 0.34f, 1.52f, -0.36f, 1.55f, 0.3f, 1.57f, -0.26f, 1.6f, 0.36f, 1.62f, -0.34f, 1.64f, 0.3f, 1.66f, -0.3f, 1.68f, 0.34f,
                          1.7f, -0.36f, 1.72f, 0.32f, 1.74f, -0.3f, 1.76f, 0.36f, 1.78f, -0.34f),
                Falloff = "x0.8 beyond 15 m",
            });
            Add(new WeaponInfo
            {
                Id = "brick", Name = "Brick", Class = WeaponClass.Shotgun, Price = 1800, Head = 18, Body = 12, FireIntervalS = 0.90f, Mag = 5, ReloadS = 2.5f,
                Pellets = 8, PelletConeDeg = 3.4f, BaseSpreadDeg = 1.2f, RecoveryS = 0.5f, DrawS = 0.55f, Mobility = 0.85f, KickFraction = 0.6f,
                Spray = P(0f, 0f, 3.2f, 0.2f, 4.6f, -0.3f, 5.4f, 0.3f, 5.8f, -0.2f),
                Falloff = "pellets x0.5 beyond 8 m",
            });
            Add(new WeaponInfo
            {
                Id = "lancer", Name = "Lancer", Class = WeaponClass.Rifle, Price = 2900, Head = 150, Body = 40, FireIntervalS = 0.15f, Mag = 25, ReloadS = 2.5f,
                BaseSpreadDeg = 1.2f, RecoveryS = 0.42f, Auto = true, DrawS = 0.6f, Mobility = 0.75f, KickFraction = 0.4f,
                // Rises for ten bullets, then drifts left, right and left again near the top.
                Spray = P(0f, 0f, 0.5f, 0f, 1.1f, 0.05f, 1.8f, 0.1f, 2.5f, 0.05f, 3.1f, -0.1f, 3.6f, -0.3f, 3.9f, -0.6f, 4.1f, -0.9f, 4.2f, -1.15f,
                          4.3f, -0.85f, 4.35f, -0.35f, 4.4f, 0.25f, 4.45f, 0.8f, 4.5f, 1.15f, 4.5f, 0.95f, 4.55f, 0.45f, 4.6f, -0.15f, 4.6f, -0.7f, 4.65f, -1.05f,
                          4.65f, -0.8f, 4.7f, -0.3f, 4.7f, 0.3f, 4.75f, 0.75f, 4.75f, 1.0f),
                Falloff = "",
            });
            Add(new WeaponInfo
            {
                Id = "viper", Name = "Viper", Class = WeaponClass.Rifle, Price = 2900, Head = 140, Body = 38, FireIntervalS = 0.09f, Mag = 30, ReloadS = 2.5f,
                BaseSpreadDeg = 1.0f, RecoveryS = 0.34f, Auto = true, DrawS = 0.55f, Mobility = 0.78f, KickFraction = 0.35f,
                // Softer and faster: a shallow climb with a slow right-left sway.
                Spray = P(0f, 0f, 0.35f, 0.02f, 0.75f, 0.06f, 1.15f, 0.12f, 1.5f, 0.18f, 1.8f, 0.22f, 2.05f, 0.2f, 2.25f, 0.1f, 2.4f, -0.05f, 2.5f, -0.22f,
                          2.58f, -0.38f, 2.64f, -0.5f, 2.68f, -0.55f, 2.72f, -0.48f, 2.75f, -0.32f, 2.78f, -0.12f, 2.8f, 0.1f, 2.82f, 0.3f, 2.84f, 0.46f, 2.86f, 0.55f,
                          2.88f, 0.5f, 2.9f, 0.34f, 2.92f, 0.12f, 2.94f, -0.1f, 2.96f, -0.3f, 2.98f, -0.45f, 3.0f, -0.52f, 3.02f, -0.44f, 3.04f, -0.26f, 3.06f, -0.05f),
                Falloff = "x0.85 beyond 20 m",
            });
            Add(new WeaponInfo
            {
                Id = "longshot", Name = "Longshot", Class = WeaponClass.Sniper, Price = 4700, Head = 255, Body = 150, FireIntervalS = 1.25f, Mag = 5, ReloadS = 3.5f,
                BaseSpreadDeg = 3.0f, RecoveryS = 0.8f, DrawS = 0.75f, Mobility = 0.55f, KickFraction = 0.6f,
                Spray = P(0f, 0f, 5f, 0.4f, 7f, -0.5f, 8f, 0.4f, 8.5f, -0.3f),
                Falloff = "",
            });
        }

        static void Add(WeaponInfo w)
        {
            table[w.Id] = w;
        }

        public static IEnumerable<WeaponInfo> All
        {
            get
            {
                Build();
                foreach (string id in Order)
                    if (table.TryGetValue(id, out WeaponInfo w)) yield return w;
                foreach (KeyValuePair<string, WeaponInfo> kv in table)
                    if (System.Array.IndexOf(Order, kv.Key) < 0) yield return kv.Value;
            }
        }

        /// <summary>The weapon, or null for an unknown id.</summary>
        public static WeaponInfo Find(string id)
        {
            Build();
            return id != null && table.TryGetValue(id, out WeaponInfo w) ? w : null;
        }

        /// <summary>The weapon, or the Lancer (the drill default) for an unknown or missing id.</summary>
        public static WeaponInfo Get(string id)
        {
            return Find(id) ?? Find(Default);
        }

        public static string NameOf(string id)
        {
            WeaponInfo w = Find(id);
            return w != null ? w.Name : (string.IsNullOrEmpty(id) ? "" : char.ToUpperInvariant(id[0]) + id.Substring(1));
        }

        public static string ClassName(WeaponClass c)
        {
            switch (c)
            {
                case WeaponClass.Sidearm: return "Sidearm";
                case WeaponClass.HeavyPistol: return "Heavy pistol";
                case WeaponClass.Smg: return "SMG";
                case WeaponClass.Shotgun: return "Shotgun";
                case WeaponClass.Rifle: return "Rifle";
                default: return "Sniper";
            }
        }

        static WeaponClass ParseClass(string s, WeaponClass fallback)
        {
            switch ((s ?? "").ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", ""))
            {
                case "sidearm": case "pistol": return WeaponClass.Sidearm;
                case "heavypistol": case "heavy": return WeaponClass.HeavyPistol;
                case "smg": return WeaponClass.Smg;
                case "shotgun": return WeaponClass.Shotgun;
                case "rifle": return WeaponClass.Rifle;
                case "sniper": return WeaponClass.Sniper;
                default: return fallback;
            }
        }

        public static string ArmorName(string kind)
        {
            switch (kind)
            {
                case "light": return "Light armour";
                case "heavy": return "Heavy armour";
                default: return "No armour";
            }
        }

        public static int ArmorPrice(string kind)
        {
            return kind == "heavy" ? HeavyPrice : kind == "light" ? LightPrice : 0;
        }

        public static int ArmorPointsOf(string kind)
        {
            return kind == "heavy" ? HeavyPoints : kind == "light" ? LightPoints : 0;
        }

        /// <summary>"PISTOL", "ECO", "FORCE", "FULL BUY" for a round type id.</summary>
        public static string RoundTypeLabel(string t)
        {
            switch ((t ?? "").ToLowerInvariant().Replace("_", "").Replace(" ", "").Replace("-", ""))
            {
                case "pistol": return "PISTOL ROUND";
                case "eco": return "ECO";
                case "force": return "FORCE BUY";
                case "full": case "fullbuy": return "FULL BUY";
                default: return string.IsNullOrEmpty(t) ? "" : t.ToUpperInvariant();
            }
        }

        public static string RoundTypeAdvice(string t)
        {
            switch ((t ?? "").ToLowerInvariant().Replace("_", "").Replace(" ", "").Replace("-", ""))
            {
                case "pistol": return "Everyone starts on 800. A Hammer or light armour.";
                case "eco": return "Under 2000 on average: save for next round. Pin or Hammer.";
                case "force": return "2000 to 3900: buy what you can. Wasp or Brick and light armour.";
                case "full": case "fullbuy": return "3900 and up: a rifle and heavy armour.";
                default: return "";
            }
        }

        // ---- hello.rules.weapons ------------------------------------------------------------------
        /// <summary>Reads rules.weapons into the table. Unreadable fields keep the built-in values.</summary>
        public static void MergeFromRules(JToken weapons, JToken armor)
        {
            Build();
            try
            {
                if (weapons is JArray arr)
                {
                    foreach (JToken t in arr)
                        if (t is JObject o) Merge(Str(o, "id"), o);
                }
                else if (weapons is JObject obj)
                {
                    foreach (JProperty p in obj.Properties())
                        if (p.Value is JObject o) Merge(Str(o, "id") ?? p.Name, o);
                }
                if (armor is JObject ao) MergeArmor(ao);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("Saltworks: could not read rules.weapons, keeping the built-in table: " + ex.Message);
            }
        }

        static void MergeArmor(JObject o)
        {
            if (o["light"] is JObject l)
            {
                LightPrice = (int)Num(l, LightPrice, "price", "cost");
                LightPoints = (int)Num(l, LightPoints, "points", "hp", "value");
            }
            if (o["heavy"] is JObject h)
            {
                HeavyPrice = (int)Num(h, HeavyPrice, "price", "cost");
                HeavyPoints = (int)Num(h, HeavyPoints, "points", "hp", "value");
            }
        }

        static void Merge(string id, JObject o)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!table.TryGetValue(id, out WeaponInfo w))
            {
                w = new WeaponInfo { Id = id, Name = NameOf(id), Class = WeaponClass.Rifle, FireIntervalS = 0.15f, Mag = 25, ReloadS = 2.5f, BaseSpreadDeg = 1f, RecoveryS = 0.4f, Auto = true };
                table[id] = w;
            }
            w.Name = Str(o, "name") ?? w.Name;
            w.Class = ParseClass(Str(o, "class", "kind", "type", "category"), w.Class);
            w.Price = (int)Num(o, w.Price, "price", "cost");
            w.Pellets = Mathf.Max(1, (int)Num(o, w.Pellets, "pellets", "pelletCount"));
            w.Head = Damage(o, w.Head, "head", "headDamage", "damageHead");
            w.Body = Damage(o, w.Body, "body", "bodyDamage", "damageBody");
            JToken dmg = o["damage"];
            if (dmg is JObject d)
            {
                w.Head = Damage(d, w.Head, "head");
                w.Body = Damage(d, w.Body, "body");
            }
            float interval = Num(o, -1f, "fireIntervalS", "fireInterval", "intervalS", "interval", "fireIntervalSeconds");
            if (interval <= 0f)
            {
                float rpm = Num(o, -1f, "rpm", "roundsPerMinute");
                if (rpm > 0f) interval = 60f / rpm;
                float rps = Num(o, -1f, "fireRate", "rateOfFire");
                if (rps > 0f) interval = 1f / rps;
            }
            if (interval > 0f) w.FireIntervalS = interval;
            w.Mag = Mathf.Max(1, (int)Num(o, w.Mag, "mag", "magazine", "magSize", "magazineSize", "ammo"));
            w.ReloadS = Mathf.Max(0.1f, Num(o, w.ReloadS, "reloadS", "reload", "reloadTimeS", "reloadTime"));
            w.BaseSpreadDeg = Mathf.Max(0f, Num(o, w.BaseSpreadDeg, "baseSpreadDeg", "baseSpread", "spreadDeg"));
            w.RecoveryS = Mathf.Max(0.02f, Num(o, w.RecoveryS, "recoveryS", "recovery"));
            if (o["auto"] != null && o["auto"].Type == JTokenType.Boolean) w.Auto = (bool)o["auto"];
            JToken f = o["falloff"];
            if (f != null && f.Type != JTokenType.Null) w.Falloff = FalloffText(f, w.Falloff);
            JToken spray = o["spray"];
            if (spray is JObject so)
            {
                // { pattern: [...], recoveryS } is accepted too.
                w.RecoveryS = Mathf.Max(0.02f, Num(so, w.RecoveryS, "recoveryS", "recovery"));
                spray = so["pattern"] ?? so["offsets"] ?? so["points"];
            }
            List<Vector2> pattern = ReadPattern(spray);
            if (pattern != null && pattern.Count > 0) w.Spray = pattern;
        }

        /// <summary>A spray pattern: [[pitch, yaw], ...], [{ pitch, yaw }, ...] or [{ x, y }, ...] (x = yaw, y = pitch).</summary>
        static List<Vector2> ReadPattern(JToken t)
        {
            if (!(t is JArray a)) return null;
            var l = new List<Vector2>(a.Count);
            foreach (JToken e in a)
            {
                if (e is JArray pair && pair.Count >= 2)
                {
                    l.Add(new Vector2(F(pair[0]), F(pair[1])));
                }
                else if (e is JObject po)
                {
                    if (po["pitch"] != null || po["yaw"] != null)
                        l.Add(new Vector2(Num(po, 0f, "pitch", "pitchDeg"), Num(po, 0f, "yaw", "yawDeg")));
                    else
                        l.Add(new Vector2(Num(po, 0f, "y", "up"), Num(po, 0f, "x", "right")));
                }
            }
            return l;
        }

        static string FalloffText(JToken f, string fallback)
        {
            if (f.Type == JTokenType.String) return (string)f;
            if (f is JObject o)
            {
                float range = Num(o, -1f, "rangeM", "beyondM", "range", "distanceM");
                float mult = Num(o, -1f, "multiplier", "mult", "factor", "scale");
                if (range > 0f && mult > 0f) return "x" + mult.ToString("0.##") + " beyond " + range.ToString("0") + " m";
            }
            if (f is JArray arr && arr.Count > 0 && arr[0] is JObject first) return FalloffText(first, fallback);
            return fallback;
        }

        static float Damage(JObject o, float fallback, params string[] keys)
        {
            foreach (string k in keys)
            {
                JToken t = o[k];
                if (t == null) continue;
                if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) return (float)(double)t;
                if (t is JObject d) return Num(d, fallback, "near", "base", "value", "damage");
            }
            return fallback;
        }

        static float F(JToken t)
        {
            return t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (float)(double)t : 0f;
        }

        static float Num(JObject o, float fallback, params string[] keys)
        {
            foreach (string k in keys)
            {
                JToken t = o[k];
                if (t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)) return (float)(double)t;
            }
            return fallback;
        }

        static string Str(JObject o, params string[] keys)
        {
            foreach (string k in keys)
            {
                JToken t = o[k];
                if (t != null && t.Type == JTokenType.String) return (string)t;
            }
            return null;
        }
    }
}

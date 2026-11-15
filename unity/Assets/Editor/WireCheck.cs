// Parses recorded bridge messages with the client's own message types, so a shape the bridge sends
// and the client cannot read is caught before anyone plays. Run headless:
//   Unity -batchmode -nographics -projectPath unity -executeMethod WireCheck.Run -wireFile <file.ndjson> -logFile -
// Record the file against a live bridge (see unity/README.md). Exits 1 on any failure.
//
// Beyond parsing, it checks what the screens read: the HUD's ammo, the personas' lists, the duel score,
// the map data the minimap and the tactical map (M) draw (walls, sites, callouts, spawns), and that
// chat and persona lines go through the custom persona-name pass.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Saltworks;
using UnityEditor;
using UnityEngine;

public static class WireCheck
{
    static readonly Dictionary<string, Type> Typed = new Dictionary<string, Type>
    {
        ["hello"] = typeof(HelloMsg),
        ["maps"] = typeof(MapsMsg),
        ["map"] = typeof(MapMsg),
        ["agent"] = typeof(AgentMsg),
        ["drill"] = typeof(DrillMsg),
        ["step"] = typeof(StepMsg),
        ["result"] = typeof(ResultMsg),
        ["debrief"] = typeof(DebriefMsg),
        ["buy_phase"] = typeof(BuyPhaseMsg),
        ["loadout"] = typeof(LoadoutInfo),
        ["round_result"] = typeof(RoundResultMsg),
        ["match_end"] = typeof(MatchEndMsg),
        ["error"] = typeof(ErrorMsg),
        // bots-wire.4 (docs/streamer-mode.md)
        ["personas"] = typeof(PersonasMsg),
        ["chat"] = typeof(ChatMsg),
        ["alert"] = typeof(AlertMsg),
        ["persona_line"] = typeof(PersonaLineMsg),
        ["duel_end"] = typeof(DuelEndMsg),
    };

    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        string file = null;
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-wireFile") file = args[i + 1];
        if (file == null || !File.Exists(file))
        {
            Debug.LogError("WIRECHECK: pass -wireFile <recorded.ndjson>");
            EditorApplication.Exit(2);
            return;
        }
        MethodInfo readGeneric = typeof(Wire).GetMethod("Read");
        MethodInfo readReport = typeof(GameController).GetMethod("ReadReport", BindingFlags.NonPublic | BindingFlags.Static);
        int ok = 0, failed = 0;
        var unknown = new HashSet<string>();
        foreach (string line in File.ReadAllLines(file))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JObject o;
            string type;
            try
            {
                o = JObject.Parse(line);
                type = (string)o["type"];
            }
            catch (Exception ex)
            {
                failed++;
                Debug.LogError("WIRECHECK FAIL (unparseable line): " + ex.Message);
                continue;
            }
            if (type == null)
            {
                failed++;
                Debug.LogError("WIRECHECK FAIL: a message without a type");
                continue;
            }
            try
            {
                if (type == "report")
                {
                    if (readReport == null) throw new Exception("GameController.ReadReport not found");
                    if (readReport.Invoke(null, new object[] { o }) == null) throw new Exception("ReadReport returned null");
                }
                else if (Typed.TryGetValue(type, out Type t))
                {
                    if (readGeneric.MakeGenericMethod(t).Invoke(null, new object[] { o }) == null) throw new Exception("Read returned null");
                    if (type == "personas")
                    {
                        // Each persona must read as a PersonaInfo with an id, and its lists must come out.
                        PersonasMsg pm = Wire.Read<PersonasMsg>(o);
                        if (!(pm.personas is JArray arr)) throw new Exception("personas is not an array");
                        foreach (JToken pt in arr)
                        {
                            if (!(pt is JObject po)) throw new Exception("a persona is not an object");
                            PersonaInfo p = Wire.Read<PersonaInfo>(po);
                            if (string.IsNullOrEmpty(p.id)) throw new Exception("a persona has no id");
                            if (po["lines"] != null && Lenient.Strings(p.lines).Count == 0) throw new Exception("lines of " + p.id + " did not resolve");
                            if (po["colors"] != null && Lenient.Strings(p.colors).Count == 0) throw new Exception("colors of " + p.id + " did not resolve");
                            // The name the client shows must survive the display-name cleaning.
                            if (!string.IsNullOrEmpty(p.name) && Personas.Clean(p.name).Length == 0) throw new Exception("the name of " + p.id + " is empty once cleaned");
                        }
                    }
                    if (type == "duel_end" && o["score"] != null && !(Wire.Read<DuelEndMsg>(o).score is JObject))
                        throw new Exception("duel_end.score is not an object");
                    if (type == "step")
                    {
                        // The values the HUD reads every frame must come out, not just parse.
                        StepMsg s = Wire.Read<StepMsg>(o);
                        foreach (AgentState a in s.state.agents)
                            if (o["state"]["agents"][0]["ammo"] != null && a.AmmoInHand == null)
                                throw new Exception("ammo for " + a.id + " (" + a.WeaponInHand + ") did not resolve");
                    }
                    if (type == "map") CheckMap(Wire.Read<MapMsg>(o).map, "map");
                    if (type == "hello" && o["map"] != null) CheckMap(Wire.Read<HelloMsg>(o).map, "hello.map");
                    if (type == "chat")
                    {
                        // The speaker and the text go through the custom persona-name pass.
                        ChatMsg c = Wire.Read<ChatMsg>(o);
                        Personas.Match(c.user);
                        Personas.Rename(c.text);
                    }
                    if (type == "persona_line")
                    {
                        PersonaLineMsg pl = Wire.Read<PersonaLineMsg>(o);
                        if (string.IsNullOrEmpty(pl.text)) throw new Exception("persona_line has no text");
                        if (Personas.Get(pl.persona) == null && Personas.Match(pl.persona) == null)
                            Debug.LogWarning("WIRECHECK: persona_line from '" + pl.persona + "', not a persona the client knows (shown under that id)");
                        Personas.Rename(pl.text);
                    }
                }
                else
                {
                    unknown.Add(type);
                    continue;
                }
                ok++;
            }
            catch (Exception ex)
            {
                failed++;
                Exception inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                Debug.LogError("WIRECHECK FAIL " + type + ": " + inner.Message);
            }
        }
        Debug.Log("WIRECHECK RESULT ok=" + ok + " failed=" + failed + " unchecked=" + string.Join(",", unknown));
        EditorApplication.Exit(failed == 0 ? 0 : 1);
    }

    /// <summary>The map data the minimap and the tactical map draw: bounds, walls, sites, callouts and spawns.</summary>
    static void CheckMap(MapDef m, string what)
    {
        if (m == null) throw new Exception(what + " is null");
        if (m.bounds == null || m.bounds.width <= 0f || m.bounds.height <= 0f) throw new Exception(what + ".bounds is missing or empty");
        if (m.walls == null || m.walls.Count == 0) throw new Exception(what + " has no walls");
        foreach (WallDef w in m.walls)
            if (w == null || w.points == null || w.points.Count < 3) throw new Exception(what + " wall " + (w?.id ?? "?") + " has fewer than 3 points");
        int sites = 0;
        foreach (KeyValuePair<string, List<Vec2>> s in m.SiteList()) sites++;
        if (sites < 2) throw new Exception(what + " has " + sites + " plant sites (the map view expects A and B at least)");
        var mm = new Minimap();
        mm.Build(m);
        if (!mm.Ready) throw new Exception(what + ": the minimap mesh was not built");
        if (mm.Sites.Count != sites) throw new Exception(what + ": " + mm.Sites.Count + " site letters for " + sites + " sites");
        int named = 0;
        if (m.callouts != null)
            foreach (CalloutDef c in m.callouts)
                if (c != null && !string.IsNullOrEmpty(c.name) && c.polygon != null && c.polygon.Count >= 3) named++;
        if (mm.Callouts.Count != named) throw new Exception(what + ": " + mm.Callouts.Count + " callout labels for " + named + " named callouts");
        if (named == 0) Debug.LogWarning("WIRECHECK: " + what + " has no named callouts; the tactical map shows no area names");
        if (m.spawns == null || m.spawns.attack == null || m.spawns.attack.Count == 0 || m.spawns.defense == null || m.spawns.defense.Count == 0)
            Debug.LogWarning("WIRECHECK: " + what + " is missing a team's spawns; the tactical map shows only the spawns it has");
    }
}

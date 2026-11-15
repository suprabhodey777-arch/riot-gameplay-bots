// Parses recorded bridge messages with the client's own message types, so a shape the bridge sends
// and the client cannot read is caught before anyone plays. Run headless:
//   Unity -batchmode -nographics -projectPath unity -executeMethod WireCheck.Run -wireFile <file.ndjson> -logFile -
// Record the file against a live bridge (see unity/README.md). Exits 1 on any failure.
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
        ["drill"] = typeof(DrillMsg),
        ["step"] = typeof(StepMsg),
        ["result"] = typeof(ResultMsg),
        ["debrief"] = typeof(DebriefMsg),
        ["buy_phase"] = typeof(BuyPhaseMsg),
        ["loadout"] = typeof(LoadoutInfo),
        ["round_result"] = typeof(RoundResultMsg),
        ["match_end"] = typeof(MatchEndMsg),
        ["error"] = typeof(ErrorMsg),
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
            JObject o = JObject.Parse(line);
            string type = (string)o["type"];
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
                    if (type == "step")
                    {
                        // The values the HUD reads every frame must come out, not just parse.
                        StepMsg s = Wire.Read<StepMsg>(o);
                        foreach (AgentState a in s.state.agents)
                            if (o["state"]["agents"][0]["ammo"] != null && a.AmmoInHand == null)
                                throw new Exception("ammo for " + a.id + " (" + a.WeaponInHand + ") did not resolve");
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
}

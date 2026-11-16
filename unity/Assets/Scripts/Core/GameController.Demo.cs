// Demo recording: started with "-demo <dir>", the app plays itself through the
// main features (report, a rehearsal, a match round, a persona duel with the
// streamer overlay) with an autopilot that aims and fires, and saves every
// frame as raw RGB24 at 30 fps to <dir>/frames.raw, with <dir>/meta.txt
// (width height) and <dir>/markers.tsv (frame, label) for cutting a video.
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Saltworks
{
    public partial class GameController
    {
        string demoDir;
        int demoFrames;
        bool demoRecording;
        StreamWriter demoMarks;
        BlockingCollection<KeyValuePair<byte[], int>> demoQueue;
        Thread demoWriter;
        /// <summary>0 off, 1 seek the nearest enemy, 2 follow the nearest ally, 3 hold and face the enemy.</summary>
        int demoPilot;
        readonly HashSet<GameKey> demoHeld = new HashSet<GameKey>();
        GameKey? demoPress;

        void StartDemoIfAsked()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-demo") demoDir = args[i + 1];
            if (string.IsNullOrEmpty(demoDir)) return;
            demoDir = Path.GetFullPath(demoDir);
            Directory.CreateDirectory(demoDir);
            TourActive = true;
            GameInput.SyntheticHeld = k => demoHeld.Contains(k);
            GameInput.SyntheticPressed = k => demoPress.HasValue && demoPress.Value == k;
            StartCoroutine(DemoRecorder());
            StartCoroutine(DemoPilotLoop());
            StartCoroutine(Demo());
        }

        void DemoMark(string label)
        {
            demoMarks.WriteLine(demoFrames + "\t" + label);
            demoMarks.Flush();
            Debug.Log("DEMO: " + demoFrames + " " + label);
        }

        IEnumerator DemoRecorder()
        {
            int w = UnityEngine.Screen.width, h = UnityEngine.Screen.height;
            File.WriteAllText(Path.Combine(demoDir, "meta.txt"), w + " " + h + "\n");
            demoMarks = new StreamWriter(Path.Combine(demoDir, "markers.tsv"));
            demoQueue = new BlockingCollection<KeyValuePair<byte[], int>>(64);
            string rawPath = Path.Combine(demoDir, "frames.raw");
            demoWriter = new Thread(() =>
            {
                using (var fs = new FileStream(rawPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                    foreach (KeyValuePair<byte[], int> f in demoQueue.GetConsumingEnumerable())
                        for (int i = 0; i < f.Value; i++) fs.Write(f.Key, 0, f.Key.Length);
            });
            demoWriter.Start();
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var eof = new WaitForEndOfFrame();
            const double dt = 1.0 / 30.0;
            double next = Time.realtimeSinceStartupAsDouble;
            demoRecording = true;
            while (demoRecording)
            {
                yield return eof;
                double now = Time.realtimeSinceStartupAsDouble;
                if (now < next) continue;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                byte[] raw = tex.GetRawTextureData();
                int n = 0;
                while (next <= now && n < 4) { n++; next += dt; }
                if (next < now) next = now;
                demoQueue.Add(new KeyValuePair<byte[], int>(raw, n));
                demoFrames += n;
            }
            demoQueue.CompleteAdding();
        }

        /// <summary>Every frame: aim at the nearest visible enemy and fire; otherwise move by the pilot mode.</summary>
        IEnumerator DemoPilotLoop()
        {
            while (true)
            {
                yield return null;
                demoHeld.Remove(GameKey.Forward);
                GameInput.SyntheticFire = false;
                if (demoPilot == 0 || Screen != AppScreen.Playing || World == null || World.Curr == null || Walls == null || Paused) continue;
                AgentState me = World.Agent(PlayerId);
                if (me == null || !me.alive) continue;
                Vector2 eye = EyePos;
                AgentState seen = null, enemy = null, ally = null;
                float seenD = 1e9f, enemyD = 1e9f, allyD = 1e9f;
                foreach (AgentState a in World.Curr.agents)
                {
                    if (a == null || !a.alive || a.id == PlayerId) continue;
                    float d = Vector2.Distance(eye, Geo.ToV2(a.pos));
                    if (a.team == me.team) { if (d > 3f && d < allyD) { allyD = d; ally = a; } continue; }
                    if (d < enemyD) { enemyD = d; enemy = a; }
                    if (d < seenD && Walls.LineOfSight(eye, Geo.ToV2(a.pos), World.Curr.smokes)) { seenD = d; seen = a; }
                }
                float dtf = Time.unscaledDeltaTime;
                AgentState face = seen ?? (demoPilot == 2 && ally != null ? ally : enemy);
                if (face == null) continue;
                Vector2 to = Geo.ToV2(face.pos) - eye;
                float want = Geo.YawToHeading(Mathf.Atan2(to.y, to.x));
                Rig.Heading = Mathf.MoveTowardsAngle(Rig.Heading, want, (seen != null ? 420f : 160f) * dtf);
                float pitch = seen != null ? Mathf.Atan2(0.12f, Mathf.Max(1f, seenD)) * Mathf.Rad2Deg : 0f;
                Rig.Pitch = Mathf.MoveTowards(Rig.Pitch, pitch, 120f * dtf);
                if (seen != null) GameInput.SyntheticFire = Mathf.Abs(Mathf.DeltaAngle(Rig.Heading, want)) < 1.6f;
                else if (demoPilot != 3 && Mathf.Abs(Mathf.DeltaAngle(Rig.Heading, want)) < 30f) demoHeld.Add(GameKey.Forward);
            }
        }

        IEnumerator DemoPress(GameKey k)
        {
            demoPress = k;
            yield return null;
            yield return null;
            demoPress = null;
        }

        IEnumerator DemoHold(float seconds, Func<bool> stop = null)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until && (stop == null || !stop())) yield return null;
        }

        IEnumerator Demo()
        {
            while (!demoRecording) yield return null;
            yield return TourWait("menu", () => Screen == AppScreen.Menu, 0.5f);
            DemoMark("menu");
            yield return DemoHold(4f);

            Screen = AppScreen.AgentSelect;
            DemoMark("agents");
            yield return DemoHold(4f);

            mapChosen = true;
            agentChosen = true;
            Screen = AppScreen.Choose;
            ChooseSample(Samples[0].Id);
            yield return TourWait("report", () => Report != null && WaitingFor == null, 0.2f);
            Screen = AppScreen.Report;
            DemoMark("report");
            yield return DemoHold(6f);

            // A rehearsal of a lost moment.
            RequestNextDrill();
            yield return TourWait("intro", () => Screen == AppScreen.Intro && Drill != null, 0.2f);
            DemoMark("intro");
            yield return DemoHold(3.5f);
            Begin();
            demoPilot = 1;
            DemoMark("drill");
            yield return DemoHold(6f, () => DrillDone);
            if (!DrillDone)
            {
                MapOpen = true;
                DemoMark("map");
                yield return DemoHold(2.5f, () => DrillDone);
                MapOpen = false;
                DemoMark("drill2");
                yield return DemoHold(10f, () => DrillDone);
            }
            demoPilot = 0;
            if (!DrillDone) AbandonDrill();
            yield return TourWait("result", () => Screen == AppScreen.Result && Result != null, 0.2f);
            DemoMark("result");
            yield return DemoHold(7f);

            // A match: buy phase, then a round with the team.
            if (V3)
            {
                OpenMatchSetup();
                yield return TourWait("match setup", () => Screen == AppScreen.MatchSetup, 0.2f);
                DemoMark("matchsetup");
                yield return DemoHold(3f);
                StartMatch();
                yield return TourWait("buy", () => InMatch && BuyPhase != null && (Screen == AppScreen.Buy || BuyOpen), 0.3f);
                DemoMark("buy");
                yield return DemoHold(2f);
                BuyItem("hammer");
                yield return DemoHold(2.5f);
                ReadyUp();
                yield return TourWait("round", () => Screen == AppScreen.Playing && Drill != null && !DrillDone && !BuyOpen, 0.1f);
                demoPilot = 2;
                DemoMark("match");
                yield return DemoHold(1.5f);
                yield return DemoPress(GameKey.Dash);
                yield return DemoHold(24f, () => DrillDone || RoundResult != null);
                DemoMark("matchend");
                yield return DemoHold(2.5f);
                demoPilot = 0;
                if (InMatch) LeaveMatch();
            }

            // Streamer mode and a persona duel.
            if (V4)
            {
                Screen = AppScreen.Menu;
                SetStreamer(true);
                OpenPersonas();
                yield return TourWait("personas", () => Screen == AppScreen.PersonaSelect, 0.3f);
                DemoMark("personas");
                yield return DemoHold(4f);
                OpenDuel(Personas.All[0].Id);
                yield return TourWait("duel setup", () => Screen == AppScreen.DuelSetup, 0.3f);
                DemoMark("duelsetup");
                yield return DemoHold(3f);
                StartDuel();
                yield return TourWait("duel", () => InDuel && (Screen == AppScreen.Playing || Screen == AppScreen.Buy), 0.1f);
                if (Screen == AppScreen.Buy || BuyOpen) ReadyUp();
                demoPilot = 3;
                DemoMark("duel");
                yield return DemoHold(30f, () => Screen != AppScreen.Playing && Screen != AppScreen.Buy);
                DemoMark("duelend");
                yield return DemoHold(3f);
                demoPilot = 0;
            }

            if (Screen == AppScreen.Playing) Paused = true;
            OpenSettings();
            DemoMark("settings");
            yield return DemoHold(3.5f);
            CloseSettings();
            DemoMark("end");

            demoRecording = false;
            yield return DemoHold(0.5f);
            demoWriter.Join(20000);
            demoMarks.Close();
            if (InMatch && Connected) Client.Send(Wire.Abandon());
            yield return DemoHold(0.3f);
            Application.Quit();
        }
    }
}

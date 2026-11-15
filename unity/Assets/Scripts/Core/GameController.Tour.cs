// The screenshot tour: started with the command-line flag "-tour <dir>", the
// built app drives itself through every screen against the real bridge (no
// mouse or keyboard needed), saves a PNG of each into <dir> (01-menu.png,
// 02-connect.png, ...) and quits. Each step waits for its screen to be ready;
// a screen that never comes is logged ("TOUR: timed out ...") after 15 s and
// skipped, keeping its number, so the files stay in order.
using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Saltworks
{
    public partial class GameController
    {
        /// <summary>True while the screenshot tour drives the client (the launch sting is skipped, focus loss never pauses).</summary>
        public static bool TourActive { get; private set; }

        const float TourTimeoutS = 15f;
        string tourDir;
        int tourShot;
        bool tourReady;

        void StartTourIfAsked()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != "-tour") continue;
                if (i + 1 >= args.Length || args[i + 1].StartsWith("-"))
                {
                    Debug.LogError("TOUR: -tour needs a directory, e.g. -tour /tmp/shots");
                    return;
                }
                tourDir = args[i + 1];
            }
            if (string.IsNullOrEmpty(tourDir)) return;
            try
            {
                tourDir = Path.GetFullPath(tourDir);
                Directory.CreateDirectory(tourDir);
            }
            catch (Exception ex)
            {
                Debug.LogError("TOUR: cannot use '" + tourDir + "': " + ex.Message);
                return;
            }
            TourActive = true;
            Debug.Log("TOUR: writing screenshots to " + tourDir);
            StartCoroutine(Tour());
        }

        /// <summary>Waits until `ready` (then `settle` seconds more for the screen's animations); sets tourReady.</summary>
        IEnumerator TourWait(string what, Func<bool> ready, float settle = 0.8f)
        {
            float until = Time.unscaledTime + TourTimeoutS;
            tourReady = false;
            while (true)
            {
                bool ok;
                try
                {
                    ok = ready();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("TOUR: check for " + what + " failed: " + ex.Message);
                    ok = false;
                }
                if (ok) break;
                if (Time.unscaledTime > until)
                {
                    Debug.LogWarning("TOUR: timed out after " + TourTimeoutS + " s waiting for " + what + " (screen is " + Screen + ")");
                    yield break;
                }
                yield return null;
            }
            tourReady = true;
            yield return TourSettle(settle);
        }

        static IEnumerator TourSettle(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until) yield return null;
        }

        /// <summary>Saves screenshot number n as "nn-name.png" when the last wait succeeded; the number is used either way.</summary>
        IEnumerator TourShot(string name)
        {
            tourShot++;
            string file = Path.Combine(tourDir, tourShot.ToString("00") + "-" + name + ".png");
            if (!tourReady)
            {
                Debug.LogWarning("TOUR: skipped " + Path.GetFileName(file));
                yield break;
            }
            ScreenCapture.CaptureScreenshot(file);
            // The capture happens at the end of this frame; give it a few frames to reach the disk.
            for (int i = 0; i < 4; i++) yield return null;
            yield return TourSettle(0.2f);
            Debug.Log("TOUR: saved " + file);
        }

        /// <summary>Holds forward and turns slowly (as if a player walked and looked around).</summary>
        static void TourMove(bool on)
        {
            GameInput.SyntheticHeld = on ? (Func<GameKey, bool>)(k => k == GameKey.Forward) : null;
            GameInput.SyntheticLook = on ? new Vector2(0.35f, 0f) : Vector2.zero;
        }

        IEnumerator Tour()
        {
            // 1. The main menu, after the bridge's hello.
            yield return TourWait("the main menu (bridge hello)", () => Screen == AppScreen.Menu, 1.2f);
            yield return TourShot("menu");

            // 2. The connect screen, shown on purpose while the link stays up.
            tourReady = Connected;
            if (tourReady)
            {
                Screen = AppScreen.Connect;
                yield return TourSettle(0.8f);
            }
            yield return TourShot("connect");
            if (Screen == AppScreen.Connect && Connected) Screen = AppScreen.Menu;

            // 3. Map select (bots-wire.2).
            tourReady = false;
            if (V2)
            {
                OpenMapSelect();
                yield return TourWait("map select with the bridge's maps", () => Screen == AppScreen.MapSelect && Maps != null);
            }
            else Debug.LogWarning("TOUR: the bridge has no map select (bots-wire.1)");
            yield return TourShot("map-select");

            // 4. Agent select (bots-wire.2).
            tourReady = false;
            if (V2)
            {
                Screen = AppScreen.AgentSelect;
                yield return TourWait("agent select", () => Screen == AppScreen.AgentSelect, 1.4f);
            }
            yield return TourShot("agent-select");

            // 5. The weakness report for a sample player.
            mapChosen = true;
            agentChosen = true;
            Screen = AppScreen.Choose;
            ChooseSample(Samples[0].Id);
            yield return TourWait("the weakness report", () => Report != null && WaitingFor == null);
            if (tourReady)
            {
                Screen = AppScreen.Report;
                yield return TourSettle(0.8f);
            }
            yield return TourShot("report");

            // 6. Persona select.
            OpenPersonas();
            yield return TourWait("persona select", () => Screen == AppScreen.PersonaSelect, 1.4f);
            yield return TourShot("persona-select");

            // 7. A practice drill's intro card.
            Screen = Report != null ? AppScreen.Report : AppScreen.Choose;
            if (Report != null) RequestNextDrill();
            yield return TourWait("the practice drill intro", () => Screen == AppScreen.Intro && Drill != null);
            yield return TourShot("drill-intro");

            // 8. The drill HUD, moving for a few seconds.
            bool inDrill = tourReady;
            if (inDrill)
            {
                Begin();
                TourMove(true);
            }
            yield return TourWait("the drill HUD", () => inDrill && Screen == AppScreen.Playing, 2.6f);
            yield return TourShot("drill-hud");

            // 9. The tactical map (M).
            tourReady = false;
            if (Screen == AppScreen.Playing && !DrillDone)
            {
                MapOpen = true;
                yield return TourWait("the tactical map", () => MapOpen && Screen == AppScreen.Playing, 0.6f);
            }
            yield return TourShot("map-view");
            MapOpen = false;
            TourMove(false);

            // 10-11. Pause, then the settings over it.
            tourReady = false;
            if (Screen == AppScreen.Playing && !DrillDone)
            {
                Paused = true;
                yield return TourWait("the pause menu", () => Paused && Screen == AppScreen.Playing, 0.6f);
            }
            yield return TourShot("pause");
            tourReady = false;
            if (Screen == AppScreen.Playing)
            {
                OpenSettings();
                yield return TourWait("the settings", () => SettingsOpen, 0.6f);
            }
            yield return TourShot("settings");
            CloseSettings();
            if (Screen == AppScreen.Playing) Resume();

            // 12. The drill result (the drill is abandoned, which the bridge answers with a result).
            tourReady = false;
            if (Screen == AppScreen.Playing && Drill != null)
            {
                if (!DrillDone) AbandonDrill();
                yield return TourWait("the drill result", () => Screen == AppScreen.Result && Result != null, 1f);
            }
            yield return TourShot("drill-result");

            // 13-15. A match: setup, the buy phase, then a round's HUD.
            tourReady = false;
            if (V3)
            {
                OpenMatchSetup();
                yield return TourWait("match setup", () => Screen == AppScreen.MatchSetup);
            }
            else Debug.LogWarning("TOUR: the bridge has no match mode (bots-wire.3)");
            yield return TourShot("match-setup");
            tourReady = false;
            if (V3)
            {
                StartMatch();
                yield return TourWait("the match buy phase", () => InMatch && BuyPhase != null && (Screen == AppScreen.Buy || BuyOpen), 1f);
            }
            yield return TourShot("match-buy");
            tourReady = false;
            if (V3 && InMatch)
            {
                ReadyUp();
                yield return TourWait("a match round", () => Screen == AppScreen.Playing && Drill != null && !DrillDone && !BuyOpen, 0.2f);
                if (tourReady)
                {
                    TourMove(true);
                    yield return TourSettle(2.4f);
                    TourMove(false);
                }
            }
            yield return TourShot("match-hud");
            if (InMatch) LeaveMatch();

            // 16-17. A persona duel: the intro card, then its first round.
            Screen = AppScreen.Menu;
            OpenPersonas();
            string persona = Personas.All.Count > 0 ? Personas.All[0].Id : null;
            if (persona != null) OpenDuel(persona);
            yield return TourWait("the duel intro", () => Screen == AppScreen.DuelSetup, 1.4f);
            yield return TourShot("duel-intro");
            tourReady = false;
            if (V4 && Screen == AppScreen.DuelSetup)
            {
                StartDuel();
                yield return TourWait("the duel's first round or buy phase", () => InDuel && (Screen == AppScreen.Playing || Screen == AppScreen.Buy), 0.2f);
                if (tourReady && (Screen == AppScreen.Buy || BuyOpen))
                {
                    ReadyUp();
                    yield return TourWait("the duel's first round", () => InDuel && Screen == AppScreen.Playing && Drill != null, 0.2f);
                }
                if (tourReady) yield return TourSettle(1.6f);
            }
            yield return TourShot("duel-round");

            TourMove(false);
            Debug.Log("TOUR: done, " + tourShot + " screens");
            if (InMatch && Connected) Client.Send(Wire.Abandon());
            yield return TourSettle(0.3f);
            Application.Quit();
        }
    }
}

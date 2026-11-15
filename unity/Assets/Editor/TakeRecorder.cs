// Showcase capture: press F10 in Play mode to start/stop recording the Game view to an MP4
// (1920x1080, 60 fps, game audio) in <repo>/recordings/. Editor-only; not part of builds.
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace ValoDrill.EditorTools
{
    [InitializeOnLoad]
    public static class TakeRecorder
    {
        static RecorderController controller;
        static string current;

        static TakeRecorder()
        {

            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingPlayMode) Stop(); };
        }

        public static bool Recording => controller != null && controller.IsRecording();


        [MenuItem("ValoDrill/Toggle Take Recording _F10")]
        public static void Toggle()
        {
            if (Recording) Stop(); else Start();
        }

        public static string Start()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("TakeRecorder: enter Play mode first."); return null; }
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "recordings"));
            Directory.CreateDirectory(dir);
            current = Path.Combine(dir, "take_" + System.DateTime.Now.ToString("HHmmss"));

            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "Take";
            movie.Enabled = true;
            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };
            movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1920, OutputHeight = 1080 };
            movie.AudioInputSettings.PreserveAudio = true;
            movie.OutputFile = current;
            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRate = 60f;
            settings.CapFrameRate = true;

            controller = new RecorderController(settings);
            controller.PrepareRecording();
            controller.StartRecording();
            Debug.Log("TakeRecorder: recording " + current + ".mp4");
            ShowNote("REC  (F10 to stop)");
            return current + ".mp4";
        }

        static void ShowNote(string text)
        {
            foreach (EditorWindow w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (w.GetType().Name == "GameView") w.ShowNotification(new GUIContent(text), 1.5);
        }

        public static string Stop()
        {
            if (!Recording) return null;
            controller.StopRecording();
            controller = null;
            Debug.Log("TakeRecorder: saved " + current + ".mp4");
            ShowNote("Saved " + Path.GetFileName(current) + ".mp4");
            return current + ".mp4";
        }
    }
}
#endif

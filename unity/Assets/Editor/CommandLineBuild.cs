using UnityEditor;
using UnityEngine;

public static class CommandLineBuild
{
    public static void MacApp()
    {
        Saltworks.EditorTools.BuildMenu.SaveMainScene();
        PlayerSettings.productName = "Gameplay Coach";
        PlayerSettings.companyName = "DevHacks";
        PlayerSettings.defaultIsNativeResolution = false;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Main.unity" },
            locationPathName = "Build/GameplayCoach.app",
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        });
        Debug.Log("BUILD RESULT: " + r.summary.result + " errors=" + r.summary.totalErrors);
        EditorApplication.Exit(r.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}

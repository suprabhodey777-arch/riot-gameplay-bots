using UnityEngine;

namespace Saltworks
{
    /// <summary>
    /// Builds the whole client from code after the first scene loads, so pressing Play in Unity's default
    /// empty scene (or the scene saved by Saltworks > Save Main Scene) runs the game with no setup.
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindAnyObjectByType<GameController>() != null) return;
            var go = new GameObject("Saltworks Client");
            go.AddComponent<GameController>();
        }
    }
}

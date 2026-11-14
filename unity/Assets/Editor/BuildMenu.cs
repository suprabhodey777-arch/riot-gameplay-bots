// Editor menu: saves an (empty) Main scene, puts it first in Build Settings,
// and writes small material assets into Assets/Resources so the built-in
// shaders that the runtime finds with Shader.Find are included in player builds.
// Not needed for pressing Play in the editor.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltworks.EditorTools
{
    public static class BuildMenu
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string MaterialDir = "Assets/Resources/SaltworksShaderRefs";

        [MenuItem("Saltworks/Save Main Scene and Add to Build Settings")]
        public static void SaveMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory("Assets/Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Scene lighting settings, so fog shader variants survive build stripping. The bootstrap
            // (Saltworks.Bootstrap) builds the camera, lights, map and UI at runtime.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.011f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("Saltworks: could not save " + ScenePath);
                return;
            }

            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            WriteShaderReferences();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Saltworks: saved " + ScenePath + " and added it to Build Settings.");
        }

        [MenuItem("Saltworks/Write Shader References for Builds")]
        public static void WriteShaderReferences()
        {
            Directory.CreateDirectory(MaterialDir);
            Write("Standard", "Standard", null);
            Write("StandardEmission", "Standard", m =>
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.white);
            });
            Write("TransparentDiffuse", "Legacy Shaders/Transparent/Diffuse", null);
            Write("SpritesDefault", "Sprites/Default", null);
            Write("UnlitColor", "Unlit/Color", null);
            Write("UnlitTexture", "Unlit/Texture", null);
            Write("WorldText", "Saltworks/WorldText", null);
            Write("RevealThroughWalls", "Saltworks/RevealThroughWalls", null);
            Write("Overlay", "Saltworks/Overlay", null);
            Write("SkyboxProcedural", "Skybox/Procedural", null);
            AssetDatabase.SaveAssets();
        }

        static void Write(string name, string shaderName, System.Action<Material> setup)
        {
            Shader s = Shader.Find(shaderName);
            if (s == null)
            {
                Debug.LogWarning("Saltworks: shader not found: " + shaderName);
                return;
            }
            string path = MaterialDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            var m = new Material(s);
            setup?.Invoke(m);
            AssetDatabase.CreateAsset(m, path);
        }
    }
}

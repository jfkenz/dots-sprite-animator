using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>
    /// Opens the relocated Crowd GPU sample scene under Assets/Samples/CrowdGpuExample.
    /// </summary>
    public static class CrowdGpuExampleMenu
    {
        public const string ScenePath =
            "Assets/Samples/CrowdGpuExample/Scenes/CrowdGpuExample.unity";

        [MenuItem("Tools/DOTS Sprite Animator/Open Crowd GPU Sample")]
        public static void Open()
        {
            if (!System.IO.File.Exists(ScenePath))
            {
                Debug.LogError(
                    "[Crowd GPU Sample] Missing scene at " + ScenePath +
                    ". Expected relocated SpawnerExample / CrowdStress demo.");
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
                if (asset != null)
                    Selection.activeObject = asset;
                Debug.Log(
                    "[Crowd GPU Sample] Opened " + ScenePath +
                    ". Play: SpawnOnStart crowd, 1-9/[ ] clips, inspector spawn buttons, left HUD stats.");
            }
        }
    }
}
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InvertLab.Sprites.DOTS.Editor
{
    public static class PartsCombatDemoMenu
    {
        const string AtlasPath = "Packages/com.invertlab.spriteanimator/Runtime/DemoArt/PartsCombatAtlas.png";
        public const string SampleScenePath = "Packages/com.invertlab.spriteanimator/Samples~/Complete/PartsCombatExample/PartsCombatExample.unity";

        [MenuItem("Tools/DOTS Sprite Animator/Create Parts Combat Example", false, 61)]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateSceneContents();
            Debug.Log("[Parts Combat] Press Play. WASD/arrows move; click/space fires. On-screen controls support mouse/touch.");
        }

        static Scene CreateSceneContents()
        {
            // Import settings ship in the atlas .meta. Scene creation must also
            // work with read-only PackageCache installs.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 3.5f;
            camera.transform.position = new Vector3(-0.7f, 0.2f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.065f, 0.085f);
            var demo = new GameObject("Parts Combat Playground").AddComponent<PartsCombatDemo>();
            demo.Atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            demo.ViewCamera = camera;
            Selection.activeGameObject = demo.gameObject;
            return scene;
        }

        // Batch preparation writes the distributable sample, never the user's current scene.
        public static void BuildSampleScene()
        {
            var scene = CreateSceneContents();
            Directory.CreateDirectory(Path.GetDirectoryName(SampleScenePath));
            if (!EditorSceneManager.SaveScene(scene, SampleScenePath))
                throw new System.InvalidOperationException("Could not save Parts Combat sample scene.");
            AssetDatabase.SaveAssets();
        }

        public static int CapturePreview(Camera camera, string path)
        {
            bool ownsTarget = camera.targetTexture == null;
            var target = camera.targetTexture != null ? camera.targetTexture : new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            var oldActive = RenderTexture.active;
            var oldTarget = camera.targetTexture;
            try
            {
                if (ownsTarget) { camera.targetTexture = target; camera.Render(); }
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                var colors = pixels.GetPixels32();
                Color32 background = colors[0];
                int colored = 0;
                foreach (var color in colors)
                    if (Mathf.Abs(color.r - background.r) + Mathf.Abs(color.g - background.g) + Mathf.Abs(color.b - background.b) > 40)
                        colored++;
                return colored;
            }
            finally
            {
                RenderTexture.active = oldActive;
                camera.targetTexture = oldTarget;
                Object.Destroy(pixels);
                if (ownsTarget) { target.Release(); Object.Destroy(target); }
            }
        }
    }
}

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>
    /// Builds Assets/Samples/PlaybackApiExample/Scenes/PlaybackApiExample.unity with a
    /// Warrior-profile character plus Bootstrap / Driver for GO playback APIs.
    /// </summary>
    public static class PlaybackApiExampleBuilder
    {
        const string Root = "Assets/Samples/PlaybackApiExample";
        const string ScenePath = Root + "/Scenes/PlaybackApiExample.unity";
        const string ProfilePath =
            "Assets/Samples/Showcase/Clembod/Warrior free set/Sprite Sheet/Warrior_Sheet-Effect_profile.asset";
        const string BringerProfilePath =
            "Assets/Samples/Showcase/Clembod/Bringer Of Death/Sprite Sheet/Bringer-of-Death-SpritSheet_profile.asset";

        [MenuItem("Tools/DOTS Sprite Animator/Build Playback API Sample")]
        public static void Build()
        {
            var profile = LoadProfile();
            if (profile == null)
                throw new System.InvalidOperationException(
                    "Missing Showcase profile. Expected:\n  " + ProfilePath + "\nor\n  " + BringerProfilePath);

            Directory.CreateDirectory(Root + "/Scenes");
            Directory.CreateDirectory(Root + "/Components");
            Directory.CreateDirectory(Root + "/Systems");
            Directory.CreateDirectory(Root + "/Editor");

            EnsureReadme();
            BuildScene(profile);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            Debug.Log(
                "[Playback API Sample] Built " + ScenePath +
                ". Enter Play. Keys: 1 Walk · 2 PlayOneShot Attack · 3 Queue · 4 Priority · 5 Hitstop · 6 Hold · 0 Idle.");
        }

        static ScriptableSpriteSheetProfile LoadProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(ProfilePath);
            if (profile != null)
                return profile;
            return AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(BringerProfilePath);
        }

        static void EnsureReadme()
        {
            const string readmePath = Root + "/Systems/README.md";
            if (File.Exists(readmePath))
                return;
            File.WriteAllText(
                readmePath,
                "Reserved for Burst ECS playback consumers.\n");
            AssetDatabase.ImportAsset(readmePath);
        }

        static void BuildScene(ScriptableSpriteSheetProfile profile)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 1.85f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            cameraObject.transform.position = new Vector3(0f, 0.2f, -10f);

            var character = CreateCharacter(profile);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new System.InvalidOperationException("Failed to save " + ScenePath);

            Selection.activeGameObject = character;
        }

        static GameObject CreateCharacter(ScriptableSpriteSheetProfile profile)
        {
            GameObject go;
            try
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            }
            catch
            {
                go = new GameObject("Playback Character");
                go.AddComponent<MeshFilter>().sharedMesh =
                    Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                go.AddComponent<MeshRenderer>();
            }

            go.name = "Playback Character";
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);

            var set = go.AddComponent<SpriteAnimSetAuthoring>();
            set.Profile = profile;
            set.InitialClipIndex = 0;
            set.ShowSpriteInScene = true;
            set.BakeUnityColliders = false;
            set.BakeFrameColliders = false;
            set.BakeUnitySockets = false;
            set.ApplyFromProfile();
            set.ApplyQuadPreview();

            var player = go.AddComponent<SpriteAnimPlayerAuthoring>();
            player.ClipIndex = 0;
            player.Speed = 1f;
            player.Playing = true;
            player.PlayOnEnable = true;

            var bootstrap = go.AddComponent<PlaybackApiExampleBootstrap>();
            bootstrap.PreferredProfile = profile;
            bootstrap.IdleClipIndex = 0;
            bootstrap.WalkClipIndex = 1;
            bootstrap.AttackClipIndex = Mathf.Min(13, (profile.Data?.Clips?.Count ?? 1) - 1);
            bootstrap.LocomotionPriority = 0;
            bootstrap.AttackPriority = 10;

            var driver = go.AddComponent<PlaybackApiExampleDriver>();
            driver.IdleClipIndex = bootstrap.IdleClipIndex;
            driver.WalkClipIndex = bootstrap.WalkClipIndex;
            driver.AttackClipIndex = bootstrap.AttackClipIndex;

            // World scale from sheet height when available.
            var data = profile.Data;
            if (data != null)
            {
                data.EnsureSheets();
                if (data.Clips != null && data.Clips.Count > 0)
                {
                    var sheet = data.SheetForClip(data.Clips[0]);
                    if (sheet != null)
                    {
                        float worldSize = SpriteSheetProfile.GetWorldHeight(sheet);
                        go.transform.localScale = new Vector3(worldSize, worldSize, worldSize);
                    }
                }
            }

            return go;
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using InvertLab.Sprites.DOTS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>
    /// Wires Assets/Scenes/authoringScene.unity with a preview Quad, profile,
    /// and the AuthoringCrowdDemo companion used by the play-mode crowd driver.
    /// </summary>
    [InitializeOnLoad]
    public static class AuthoringExampleSceneSetup
    {
        const string ScenePath = "Assets/Scenes/authoringScene.unity";
        const string ProfilePath = "Assets/Samples/Showcase/Sword Character Prototype_All Frames_profile.asset";
        const string SpriteGoName = "Authoring Sprite";
        const string DemoGoName = "Authoring Crowd Demo";
        const string SessionFlag = "InvertLab.Sprites.DOTS.AuthoringExampleSceneSetup.Attempted";

        static AuthoringExampleSceneSetup()
        {
            EditorApplication.delayCall += TryAutoSetup;
        }

        [MenuItem("Tools/DOTS Sprite Animator/Setup Authoring Example Scene")]
        public static void SetupMenu() => Setup();

        static void TryAutoSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (SessionState.GetBool(SessionFlag, false))
                return;

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                return;
            if (FindAuthoringCrowdDemo() != null)
                return;

            SessionState.SetBool(SessionFlag, true);
            Setup();
        }

        public static void Setup()
        {
            if (!EnsureSceneOpen())
                return;

            var created = new List<string>();

            var spriteGo = GameObject.Find(SpriteGoName);
            if (spriteGo == null)
            {
                spriteGo = CreatePreviewQuad();
                created.Add(SpriteGoName + " (Quad)");
            }

            var set = spriteGo.GetComponent<SpriteAnimSetAuthoring>();
            if (set == null)
            {
                set = spriteGo.AddComponent<SpriteAnimSetAuthoring>();
                created.Add("SpriteAnimSetAuthoring");
            }

            if (spriteGo.GetComponent<SpriteAnimPlayerAuthoring>() == null)
            {
                spriteGo.AddComponent<SpriteAnimPlayerAuthoring>();
                created.Add("SpriteAnimPlayerAuthoring");
            }

            var profile = AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(ProfilePath);
            if (profile == null)
            {
                Debug.LogWarning("[AuthoringExampleSceneSetup] profile not found at " + ProfilePath);
            }
            else
            {
                Undo.RecordObject(set, "Setup Authoring Example Scene");
                var renderer = set.GetComponent<MeshRenderer>();
                if (renderer != null)
                    Undo.RecordObject(renderer, "Setup Authoring Example Scene");
                set.Profile = profile;
                set.ShowSpriteInScene = true;
                set.ApplyFromProfile();
                set.ApplyQuadPreview();
                EditorUtility.SetDirty(set);
                created.Add("profile assigned");
            }

            var demoType = FindAuthoringCrowdDemoType();
            Component demo = FindAuthoringCrowdDemo();
            if (demo == null)
            {
                var demoGo = GameObject.Find(DemoGoName);
                if (demoGo == null)
                {
                    demoGo = new GameObject(DemoGoName);
                    Undo.RegisterCreatedObjectUndo(demoGo, "Setup Authoring Example Scene");
                    created.Add(DemoGoName);
                }

                if (demoType != null)
                {
                    demoGo.AddComponent(demoType);
                    created.Add("AuthoringCrowdDemo");
                }
                else
                {
                    Debug.LogWarning(
                        "[AuthoringExampleSceneSetup] AuthoringCrowdDemo type not found. " +
                        "Add Assets/Samples/CrowdStress/AuthoringCrowdDemo.cs and re-run the menu.");
                }
            }

            var crowd = UnityEngine.Object.FindFirstObjectByType<SpriteCrowdSpawnerAuthoring>();
            if (crowd != null && crowd.Source == null && set != null)
            {
                Undo.RecordObject(crowd, "Setup Authoring Example Scene");
                crowd.Source = set;
                EditorUtility.SetDirty(crowd);
                created.Add("spawner Source assigned");
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

            Debug.Log("[AuthoringExampleSceneSetup] " + ScenePath + " — " +
                      (created.Count > 0 ? string.Join(", ", created) : "already set up") +
                      (profile != null ? " | profile: " + profile.name : ""));
        }

        static bool EnsureSceneOpen()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.path == ScenePath)
                return true;

            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("[AuthoringExampleSceneSetup] failed to save new scene at " + ScenePath);
                return false;
            }

            AssetDatabase.Refresh();
            Debug.Log("[AuthoringExampleSceneSetup] created " + ScenePath + " (kept default camera/light)");
            return true;
        }

        static GameObject CreatePreviewQuad()
        {
            GameObject go;
            try
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            }
            catch
            {
                go = new GameObject(SpriteGoName);
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                go.AddComponent<MeshRenderer>();
            }

            go.name = SpriteGoName;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;

            var collider = go.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);

            Undo.RegisterCreatedObjectUndo(go, "Setup Authoring Example Scene");
            return go;
        }

        static Type FindAuthoringCrowdDemoType()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try
                {
                    t = asm.GetType("InvertLab.Sprites.DOTS.AuthoringCrowdDemo");
                }
                catch
                {
                    continue;
                }
                if (t != null)
                    return t;
            }
            return null;
        }

        static Component FindAuthoringCrowdDemo()
        {
            var type = FindAuthoringCrowdDemoType();
            if (type == null)
                return null;
            return UnityEngine.Object.FindObjectOfType(type) as Component;
        }
    }
}

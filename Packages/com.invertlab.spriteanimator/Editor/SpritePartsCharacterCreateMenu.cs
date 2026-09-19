using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>
    /// Create Character for Parts: configured ECS authoring inside a loaded SubScene.
    /// Outside a SubScene, offers explicit Create ECS SubScene — never a silent dead GO.
    /// Create Demo Scene always opens a NEW dedicated scene (never overwrites the user's scene asset).
    /// </summary>
    public static class SpritePartsCharacterCreateMenu
    {
        const string MenuPath = "GameObject/DOTS Sprite Animator/Create Parts Character";
        const string ToolsPath = "Tools/DOTS Sprite Animator/Create Parts Character";
        const string DemoPath = "Tools/DOTS Sprite Animator/Create Parts Demo Scene";

        [MenuItem(MenuPath, false, 10)]
        [MenuItem(ToolsPath, false, 50)]
        public static void CreatePartsCharacter()
        {
            var subScene = FindLoadedSubScene();
            if (subScene == null)
            {
                bool create = EditorUtility.DisplayDialog(
                    "Create Parts Character",
                    "Parts characters must live in an ECS SubScene to bake and animate.\n\n" +
                    "Create an ECS SubScene host in the active scene? You can then open it and place the character inside.",
                    "Create ECS SubScene Host",
                    "Cancel");
                if (!create)
                    return;
                subScene = CreateSubSceneHostInActiveScene();
                if (subScene == null)
                    return;
                EditorUtility.DisplayDialog(
                    "ECS SubScene Host Created",
                    "Select the SubScene asset in the inspector (Scene Asset), open it for edit, then run Create Parts Character again.",
                    "OK");
                return;
            }

            if (!TryGetEditableSubScene(subScene, out Scene scene))
            {
                EditorUtility.DisplayDialog(
                    "Create Parts Character",
                    "Open the SubScene for editing (SubScene inspector > Open), then retry.",
                    "OK");
                return;
            }

            var go = new GameObject("Parts Character");
            Undo.RegisterCreatedObjectUndo(go, "Create Parts Character");
            SceneManager.MoveGameObjectToScene(go, scene);
            var authoring = Undo.AddComponent<SpritePartsCharacterAuthoring>(go);
            var selected = Selection.activeObject as ScriptableSpriteSheetProfile;
            if (selected != null && selected.Data != null && selected.Data.AnimKind == SpriteAnimKind.Parts)
                authoring.Profile = selected;

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            if (authoring.Profile == null)
            {
                EditorUtility.DisplayDialog(
                    "Parts Character Created",
                    "Authoring was created inside the SubScene. Assign a Parts profile (AnimKind=Parts) on SpritePartsCharacterAuthoring.",
                    "OK");
            }
        }

        [MenuItem(DemoPath, false, 52)]
        public static void CreatePartsDemoScene()
        {
            // Never overwrite the user's current scene asset: optionally prompt to save, then NewScene.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 3f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.05f, 0.08f);

            var demo = new GameObject("Cutout Parts Demo");
            demo.AddComponent<CutoutPartsDemoBootstrap>();
            Selection.activeGameObject = demo;

            // Dedicated sample path optional save — user chooses; default does not touch prior scene file.
            string suggested = "Assets/CutoutPartsDemo.unity";
            EditorUtility.DisplayDialog(
                "Parts Demo Scene Created",
                "Opened a NEW empty scene with CutoutPartsDemoBootstrap (generated art, Walk, sword/spear skins, pointer/touch).\n\n" +
                "This did not overwrite your previous scene asset. Save As if you want to keep it (e.g. " + suggested + ").\n\n" +
                "Enter Play Mode to run the pure-DOTS demo.",
                "OK");
            // Keep scene untitled unless user saves — avoids clobbering any existing asset path.
            _ = scene;
        }

        [MenuItem("Tools/DOTS Sprite Animator/Fix Parts Authoring Conflict", false, 51)]
        public static void FixPartsAuthoringConflict()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                EditorUtility.DisplayDialog("Fix Parts Conflict", "Select a GameObject with Parts/frame authoring conflict.", "OK");
                return;
            }
            var parts = go.GetComponent<SpritePartsCharacterAuthoring>();
            var set = go.GetComponent<SpriteAnimSetAuthoring>();
            var profile = parts != null ? parts.Profile : set != null ? set.Profile : null;
            bool isParts = profile != null && profile.Data != null && profile.Data.AnimKind == SpriteAnimKind.Parts;
            if (!isParts)
            {
                EditorUtility.DisplayDialog("Fix Parts Conflict", "Selected object is not a Parts-profile conflict.", "OK");
                return;
            }
            Undo.SetCurrentGroupName("Fix Parts Authoring Conflict");
            int group = Undo.GetCurrentGroup();
            if (parts == null)
                parts = Undo.AddComponent<SpritePartsCharacterAuthoring>(go);
            if (parts.Profile == null && set != null)
                parts.Profile = set.Profile;
            SpritePartsAuthoringBundle.StripConflictingFrameAuthoring(go);
            Undo.CollapseUndoOperations(group);
            EditorUtility.DisplayDialog(
                "Fix Parts Authoring Conflict",
                "Ensured SpritePartsCharacterAuthoring and removed frame Set/Player authoring.",
                "OK");
        }

        static Unity.Scenes.SubScene FindLoadedSubScene()
        {
            var scenes = Object.FindObjectsByType<Unity.Scenes.SubScene>(FindObjectsInactive.Exclude);
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i] == null) continue;
                if (TryGetEditableSubScene(scenes[i], out _))
                    return scenes[i];
            }
            return null;
        }

        static bool TryGetEditableSubScene(Unity.Scenes.SubScene subScene, out Scene scene)
        {
            scene = default;
            if (subScene == null) return false;
            scene = subScene.EditingScene;
            return scene.IsValid() && scene.isLoaded;
        }

        static Unity.Scenes.SubScene CreateSubSceneHostInActiveScene()
        {
            var active = SceneManager.GetActiveScene();
            if (!active.IsValid())
            {
                EditorUtility.DisplayDialog("Create ECS SubScene", "No valid active scene.", "OK");
                return null;
            }
            var host = new GameObject("Parts Characters SubScene");
            Undo.RegisterCreatedObjectUndo(host, "Create ECS SubScene Host");
            var sub = Undo.AddComponent<Unity.Scenes.SubScene>(host);
            Selection.activeGameObject = host;
            return sub;
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>
    /// Dirties Parts character authoring so SubScene live conversion rebakes
    /// after a profile save (hide, delete slot, etc.).
    /// </summary>
    public static class SpritePartsSceneSync
    {
        public static void RefreshCharactersUsing(ScriptableSpriteSheetProfile profile)
        {
            if (profile == null)
                return;
            var authors = Object.FindObjectsByType<SpritePartsCharacterAuthoring>(
                FindObjectsInactive.Include);
            for (int i = 0; i < authors.Length; i++)
            {
                var authoring = authors[i];
                if (authoring == null || authoring.Profile != profile)
                    continue;
                RefreshCharacter(authoring);
            }
        }

        public static void RefreshCharacter(SpritePartsCharacterAuthoring authoring, bool recordUndo = false)
        {
            if (authoring == null)
                return;
            if (recordUndo)
                Undo.RecordObject(authoring, "Reload Parts Profile");
            authoring.EditorProfileSyncRevision++;
            EditorUtility.SetDirty(authoring);
            EditorUtility.SetDirty(authoring.gameObject);
            var scene = authoring.gameObject.scene;
            if (scene.IsValid() && scene.isLoaded)
                EditorSceneManager.MarkSceneDirty(scene);
            SceneView.RepaintAll();
        }
    }

    class SpritePartsProfileReimportHook : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (importedAssets == null || importedAssets.Length == 0)
                return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var profiles = new System.Collections.Generic.List<ScriptableSpriteSheetProfile>();
            for (int i = 0; i < importedAssets.Length; i++)
            {
                var profile = AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(importedAssets[i]);
                if (profile != null)
                    profiles.Add(profile);
            }
            if (profiles.Count == 0)
                return;

            EditorApplication.delayCall += () =>
            {
                for (int i = 0; i < profiles.Count; i++)
                    SpritePartsSceneSync.RefreshCharactersUsing(profiles[i]);
            };
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InvertLab.Sprites.DOTS.Editor
{
    public static class SpriteProfileSceneSetup
    {
        public static bool Apply(GameObject target, ScriptableSpriteSheetProfile profile, SpriteAnimKind kind)
        {
            if (target == null || profile?.Data == null) return false;
            if (profile.Data.AnimKind != kind || !SpritePartsAuthoringOps.CanSetAnimKind(profile.Data, kind, out _))
            {
                EditorUtility.DisplayDialog("Apply Runtime Mode", "Activate a valid runtime mode in the profile first.", "OK");
                return false;
            }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply Sprite Runtime Mode");
            // User explicitly requested conversion. Preserve unrelated colliders/gameplay components.
            var frame = target.GetComponent<SpriteAnimPlayerAuthoring>();
            var set = target.GetComponent<SpriteAnimSetAuthoring>();
            var parts = target.GetComponent<SpritePartsCharacterAuthoring>();
            var still = target.GetComponent<SpriteStaticAuthoring>();
            if (kind != SpriteAnimKind.Frame)
            {
                if (frame != null) Undo.DestroyObjectImmediate(frame);
                if (set != null) Undo.DestroyObjectImmediate(set);
            }
            if (kind != SpriteAnimKind.Parts && parts != null) Undo.DestroyObjectImmediate(parts);
            if (kind != SpriteAnimKind.Static && still != null) Undo.DestroyObjectImmediate(still);
            if (kind == SpriteAnimKind.Static)
            {
                still = target.GetComponent<SpriteStaticAuthoring>() ?? Undo.AddComponent<SpriteStaticAuthoring>(target);
                Undo.RecordObject(still, "Configure Static Sprite");
                still.Profile = profile;
                still.UseProfileDefaultCell = true;
                still.SizeUnits = SpriteProfileSheetOps.ResolveStaticSizeUnits(profile.Data);
                still.UpdatePreview();
                EditorUtility.SetDirty(still);
                PrefabUtility.RecordPrefabInstancePropertyModifications(still);
            }
            else if (kind == SpriteAnimKind.Parts)
            {
                parts = target.GetComponent<SpritePartsCharacterAuthoring>() ?? Undo.AddComponent<SpritePartsCharacterAuthoring>(target);
                Undo.RecordObject(parts, "Configure Parts Character");
                parts.Profile = profile;
                EditorUtility.SetDirty(parts);
                PrefabUtility.RecordPrefabInstancePropertyModifications(parts);
            }
            else
            {
                set = target.GetComponent<SpriteAnimSetAuthoring>() ?? Undo.AddComponent<SpriteAnimSetAuthoring>(target);
                Undo.RecordObject(set, "Configure Frame Animation");
                set.Profile = profile;
                set.ApplyFromProfile();
                if (target.GetComponent<SpriteAnimPlayerAuthoring>() == null)
                    Undo.AddComponent<SpriteAnimPlayerAuthoring>(target);
                EditorUtility.SetDirty(set);
                PrefabUtility.RecordPrefabInstancePropertyModifications(set);
            }
            Undo.CollapseUndoOperations(group);
            if (target.scene.IsValid()) EditorSceneManager.MarkSceneDirty(target.scene);
            return true;
        }

        public static GameObject Create(ScriptableSpriteSheetProfile profile)
        {
            if (profile?.Data == null) return null;
            Scene scene = Selection.activeGameObject != null ? Selection.activeGameObject.scene : SceneManager.GetActiveScene();
            if (!scene.isSubScene)
            {
                // Never choose an arbitrary SubScene when several are open.
                Scene only = default;
                int count = 0;
                foreach (var sub in Object.FindObjectsByType<Unity.Scenes.SubScene>(FindObjectsInactive.Exclude))
                    if (sub.EditingScene.IsValid() && sub.EditingScene.isLoaded)
                    { only = sub.EditingScene; count++; }
                if (count != 1)
                {
                    EditorUtility.DisplayDialog("Choose ECS SubScene", "Open a SubScene for editing and select an object inside it, then create the sprite. This ensures the object is baked for runtime.", "OK");
                    return null;
                }
                scene = only;
            }
            var go = new GameObject(profile.name + " " + profile.Data.AnimKind);
            Undo.RegisterCreatedObjectUndo(go, "Create Sprite Object");
            SceneManager.MoveGameObjectToScene(go, scene);
            if (!Apply(go, profile, profile.Data.AnimKind))
            { Undo.DestroyObjectImmediate(go); return null; }
            Selection.activeGameObject = go;
            return go;
        }
    }
}

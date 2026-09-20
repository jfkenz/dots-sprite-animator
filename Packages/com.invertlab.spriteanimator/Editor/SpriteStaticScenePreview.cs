using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Live conversion assigns a zero culling mask to open authoring SubScenes.
    // Their MeshRenderers cannot draw there; preview the source sprite explicitly.
    [InitializeOnLoad]
    static class SpriteStaticScenePreview
    {
        static SpriteStaticScenePreview()
        {
            SceneView.duringSceneGui += Draw;
            EditorSceneManager.sceneSaving += BeforeSave;
            EditorSceneManager.sceneSaved += AfterSave;
            EditorSceneManager.sceneOpened += AfterOpen;
            EditorApplication.playModeStateChanged += OnPlayMode;
            Undo.undoRedoPerformed += Refresh;
            EditorApplication.delayCall += Refresh;
        }

        static void BeforeSave(Scene scene, string path)
        {
            foreach (var authoring in Object.FindObjectsByType<SpriteStaticAuthoring>(FindObjectsInactive.Include))
                if (authoring.gameObject.scene == scene) authoring.PreparePreviewForSceneSave();
        }

        static void AfterSave(Scene scene) => Refresh();
        static void AfterOpen(Scene scene, OpenSceneMode mode) => Refresh();
        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Refresh;
        }

        static void Refresh()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var authoring in Object.FindObjectsByType<SpriteStaticAuthoring>(FindObjectsInactive.Include))
                authoring.UpdatePreview();
            SceneView.RepaintAll();
        }

        static void Draw(SceneView view)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Event.current.type != EventType.Repaint)
                return;
            var camera = view.camera;
            if (camera == null) return;
            foreach (var authoring in Object.FindObjectsByType<SpriteStaticAuthoring>(FindObjectsInactive.Exclude))
            {
                var go = authoring.gameObject;
                if (!authoring.isActiveAndEnabled || !authoring.ShowSpriteInScene || authoring.HasAuthoringConflict ||
                    !go.scene.IsValid() || EditorSceneManager.GetSceneCullingMask(go.scene) != 0 ||
                    SceneVisibilityManager.instance.IsHidden(go) || (camera.cullingMask & (1 << go.layer)) == 0 ||
                    StageUtility.GetStageHandle(go) != StageUtility.GetCurrentStageHandle()) continue;

                var filter = go.GetComponent<MeshFilter>();
                var renderer = go.GetComponent<MeshRenderer>();
                if (filter == null || renderer == null || !renderer.enabled) continue;
                if (filter.sharedMesh == null || renderer.sharedMaterial == null) authoring.UpdatePreview();
                if (filter.sharedMesh == null || renderer.sharedMaterial == null) continue;
                if (renderer.sharedMaterial.SetPass(0))
                    Graphics.DrawMeshNow(filter.sharedMesh, go.transform.localToWorldMatrix);
            }
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Samples.Editor
{
    /// <summary>Sample-local Create Demo Scene (same contract as package Tools menu).</summary>
    public static class CutoutPartsExampleMenu
    {
        [MenuItem("Tools/DOTS Sprite Animator/Samples/Cutout Parts Example Scene", false, 60)]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 3f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.05f, 0.08f);
            var go = new GameObject("Cutout Parts Example");
            go.AddComponent<CutoutPartsExampleController>();
            Selection.activeGameObject = go;
            Debug.Log("[CutoutPartsExample] New demo scene created (did not overwrite prior scene asset). Enter Play.");
        }
    }
}

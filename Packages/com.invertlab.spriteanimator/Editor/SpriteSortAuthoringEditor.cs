using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    [CustomEditor(typeof(SpriteSortAuthoring))]
    [CanEditMultipleObjects]
    public sealed class SpriteSortAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            bool changed = DrawDefaultInspector();
            changed |= serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                // Keep the GameObject's world z in lockstep with the baked
                // depth so the scene view, nav/tools, and baking all agree.
                foreach (var t in targets)
                    SyncTransformZ((SpriteSortAuthoring)t);
            }

            var sort = (SpriteSortAuthoring)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Baked Depth (entity world z)",
                $"{sort.BakedDepth:0.###}");

            var camera = Camera.main;
            if (camera == null)
                camera = Object.FindAnyObjectByType<Camera>();
            var status = SpriteSortAuthoring.CheckDepth(sort.BakedDepth, camera, out var message);
            if (status == SpriteSortAuthoring.DepthStatus.Invisible)
                EditorGUILayout.HelpBox(message, MessageType.Error);
            else if (status == SpriteSortAuthoring.DepthStatus.Risky)
                EditorGUILayout.HelpBox(message, MessageType.Warning);
            else
                EditorGUILayout.HelpBox(
                    "All fields: higher = on top. Editing them also writes the GameObject's " +
                    "world z (undo-able). Baked z is what the entity uses; lower world z = closer " +
                    "to the default 2D camera (z −10, looking +z).",
                    MessageType.None);
        }

        static void SyncTransformZ(SpriteSortAuthoring sort)
        {
            if (sort == null) return;
            var tr = sort.transform;
            var wp = tr.position;
            if (Mathf.Approximately(wp.z, sort.BakedDepth))
                return;
            Undo.RecordObject(tr, "Sprite Sort Depth");
            wp.z = sort.BakedDepth;
            tr.position = wp;
        }
    }
}

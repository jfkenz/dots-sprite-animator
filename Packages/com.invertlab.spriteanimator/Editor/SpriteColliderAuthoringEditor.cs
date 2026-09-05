using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    [CustomEditor(typeof(SpriteColliderAuthoring))]
    [CanEditMultipleObjects]
    public sealed class SpriteColliderAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var authoring = (SpriteColliderAuthoring)target;
            var set = authoring.GetComponent<SpriteAnimSetAuthoring>();
            EditorGUILayout.Space();

            if (set == null)
            {
                EditorGUILayout.HelpBox("Requires a Sprite Anim Set Authoring on this object.",
                    MessageType.Warning);
                return;
            }

            // resolved scope summary (Auto = detected from the profile's boxes)
            var data = set.Profile != null ? set.Profile.Data : null;
            byte mask = authoring.ResolveLifetimeMask(data);
            string detected = data != null && data.Hitboxes != null && data.Hitboxes.Count > 0
                ? DescribeMask(mask)
                : "no baked boxes";

            EditorGUILayout.LabelField("Resolved", DescribeMask(mask) +
                (authoring.Scope == SpriteColliderScope.Auto ? $"  (auto — found: {detected})" : ""));
            EditorGUILayout.LabelField("Unity Colliders",
                authoring.Method == SpriteColliderMethod.Query ? "off (query only)" : "on");
            EditorGUILayout.LabelField("Frame Windows",
                set.BakeFrameColliders ? "on" : "off");
        }

        static string DescribeMask(byte mask)
        {
            if (mask == 0)
                return "none";
            var parts = new System.Collections.Generic.List<string>();
            if ((mask & SpriteColliderAuthoring.LifetimeFrame) != 0) parts.Add("Frame");
            if ((mask & SpriteColliderAuthoring.LifetimeCharacter) != 0) parts.Add("Character");
            if ((mask & SpriteColliderAuthoring.LifetimeClip) != 0) parts.Add("Clip");
            return string.Join(" + ", parts);
        }
    }
}

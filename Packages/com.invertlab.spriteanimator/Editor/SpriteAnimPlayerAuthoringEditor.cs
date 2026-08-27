using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    [CustomEditor(typeof(SpriteAnimPlayerAuthoring))]
    public sealed class SpriteAnimPlayerAuthoringEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint()
        {
            var player = (SpriteAnimPlayerAuthoring)target;
            return player != null && player.Playing;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            var authoring = (SpriteAnimPlayerAuthoring)target;
            var set = authoring.GetComponent<SpriteAnimSetAuthoring>();
            string clipName = "(none)";
            if (set != null && set.Clips != null &&
                authoring.ClipIndex >= 0 && authoring.ClipIndex < set.Clips.Length)
            {
                clipName = string.IsNullOrEmpty(set.Clips[authoring.ClipIndex].Name)
                    ? ("clip" + authoring.ClipIndex)
                    : set.Clips[authoring.ClipIndex].Name;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Current", $"{clipName}  frame {authoring.Frame}");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Play"))
                {
                    Undo.RecordObject(authoring, "Play Sprite Animation");
                    PlayClip(authoring, authoring.ClipIndex);
                }

                if (GUILayout.Button("Pause"))
                {
                    Undo.RecordObject(authoring, "Pause Sprite Animation");
                    authoring.Pause();
                    EditorUtility.SetDirty(authoring);
                }

                if (GUILayout.Button("Stop"))
                {
                    Undo.RecordObject(authoring, "Stop Sprite Animation");
                    authoring.Stop();
                    EditorUtility.SetDirty(authoring);
                }
            }

            if (set != null && set.Clips != null && set.Clips.Length > 0)
            {
                var names = new string[set.Clips.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    names[i] = (i + 1) + "  " + (string.IsNullOrEmpty(set.Clips[i].Name)
                        ? ("clip" + i)
                        : set.Clips[i].Name);
                }

                int current = Mathf.Clamp(authoring.ClipIndex, 0, names.Length - 1);
                int next = EditorGUILayout.Popup("Clip", current, names);
                if (next != current)
                    PlayClip(authoring, next);

                int shown = Mathf.Min(8, set.Clips.Length);
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int i = 0; i < shown; i++)
                    {
                        GUI.backgroundColor = i == authoring.ClipIndex
                            ? new Color(0.45f, 0.85f, 0.45f)
                            : Color.white;
                        if (GUILayout.Button((i + 1).ToString(), GUILayout.Height(22)))
                            PlayClip(authoring, i);
                    }
                    GUI.backgroundColor = Color.white;
                }
            }

            EditorGUILayout.HelpBox(
                "Play / keys 1-8 switch this Quad and every spawned crowd sprite. " +
                "Same clips as SpriteAnimSetAuthoring.",
                MessageType.Info);
        }

        static void PlayClip(SpriteAnimPlayerAuthoring authoring, int clipIndex)
        {
            Undo.RecordObject(authoring, "Play Sprite Animation Clip");
            authoring.Play(clipIndex);
            EditorUtility.SetDirty(authoring);

            if (!Application.isPlaying)
                return;
            var spawner = Object.FindFirstObjectByType<SpriteCrowdSpawnerAuthoring>();
            if (spawner != null)
                spawner.SetAllClips(clipIndex);
        }
    }
}

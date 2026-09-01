using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    [CustomEditor(typeof(SpriteAnimPlayerAuthoring))]
    public sealed class SpriteAnimPlayerAuthoringEditor : UnityEditor.Editor
    {
        const int ButtonsPerRow = 8;

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
            int clipCount = set != null && set.Clips != null ? set.Clips.Length : 0;
            string clipName = "(none)";
            if (clipCount > 0 && authoring.ClipIndex >= 0 && authoring.ClipIndex < clipCount)
            {
                clipName = string.IsNullOrEmpty(set.Clips[authoring.ClipIndex].Name)
                    ? ("clip" + authoring.ClipIndex)
                    : set.Clips[authoring.ClipIndex].Name;
            }

            HandleInspectorClipKeys(authoring, clipCount);

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

            if (clipCount > 0)
            {
                var names = new string[clipCount];
                for (int i = 0; i < clipCount; i++)
                {
                    names[i] = (i + 1) + "  " + (string.IsNullOrEmpty(set.Clips[i].Name)
                        ? ("clip" + i)
                        : set.Clips[i].Name);
                }

                int current = Mathf.Clamp(authoring.ClipIndex, 0, clipCount - 1);
                int next = EditorGUILayout.Popup("Clip", current, names);
                if (next != current)
                    PlayClip(authoring, next);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("[", GUILayout.Width(28), GUILayout.Height(22)))
                        StepClip(authoring, clipCount, -1);

                    EditorGUI.BeginChangeCheck();
                    int typed = EditorGUILayout.IntField(current + 1, GUILayout.Width(48), GUILayout.Height(22));
                    if (EditorGUI.EndChangeCheck())
                    {
                        int index = Mathf.Clamp(typed - 1, 0, clipCount - 1);
                        if (index != authoring.ClipIndex)
                            PlayClip(authoring, index);
                    }

                    if (GUILayout.Button("]", GUILayout.Width(28), GUILayout.Height(22)))
                        StepClip(authoring, clipCount, 1);

                    GUILayout.Label($"{current + 1} / {clipCount}", GUILayout.Width(56));
                }

                int rows = (clipCount + ButtonsPerRow - 1) / ButtonsPerRow;
                for (int row = 0; row < rows; row++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        int start = row * ButtonsPerRow;
                        int end = Mathf.Min(start + ButtonsPerRow, clipCount);
                        for (int i = start; i < end; i++)
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
            }

            EditorGUILayout.HelpBox(
                "Flip X / Flip Y mirror this instance only. Buttons match the clip count. " +
                "[ and ] step clips. Type a 1-based number. Play switches this Quad and every spawned crowd sprite.",
                MessageType.Info);
        }

        static void HandleInspectorClipKeys(SpriteAnimPlayerAuthoring authoring, int clipCount)
        {
            if (clipCount <= 0)
                return;
            var e = Event.current;
            if (e == null || e.type != EventType.KeyDown)
                return;
            if (EditorGUIUtility.editingTextField)
                return;

            if (e.keyCode == KeyCode.LeftBracket)
            {
                StepClip(authoring, clipCount, -1);
                e.Use();
            }
            else if (e.keyCode == KeyCode.RightBracket)
            {
                StepClip(authoring, clipCount, 1);
                e.Use();
            }
        }

        static void StepClip(SpriteAnimPlayerAuthoring authoring, int clipCount, int delta)
        {
            if (clipCount <= 0)
                return;
            int index = authoring.ClipIndex + delta;
            index %= clipCount;
            if (index < 0)
                index += clipCount;
            PlayClip(authoring, index);
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

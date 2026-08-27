using InvertLab.Sprites.DOTS;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    [CustomEditor(typeof(SpriteCrowdSpawnerAuthoring), true)]
    public sealed class SpriteCrowdSpawnerAuthoringEditor : UnityEditor.Editor
    {
        // IMGUI inspector only. Do not add CreateInspectorGUI / UIToolkit.
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Source"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("UseGpuAnim"),
                new GUIContent("GPU + Burst",
                    "Shader clock + Burst spawn. Leave on so big crowds are not laggy. Uncheck for CPU playback (events, sockets, ping-pong)."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("SpawnOnStartCount"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("BatchSize"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Grid"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("SizeUnits"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Spread"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("NumberKeysSwitchClips"));
            serializedObject.ApplyModifiedProperties();

            var spawner = (SpriteCrowdSpawnerAuthoring)target;
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                EditorGUILayout.LabelField("Live sprites", Application.isPlaying
                    ? spawner.LiveCount.ToString("N0")
                    : "(press Play)");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Spawn Batch"))
                        spawner.SpawnBatch();
                    if (GUILayout.Button("Spawn 1M"))
                        spawner.Spawn(1_000_000, spawner.Grid);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("+10k"))
                        spawner.Spawn(10_000, spawner.Grid);
                    if (GUILayout.Button("+100k"))
                        spawner.Spawn(100_000, spawner.Grid);
                    if (GUILayout.Button("Despawn All"))
                        spawner.DespawnAll();
                }
            }

            EditorGUILayout.HelpBox(
                "GPU + Burst is on by default: one shader draw, Burst spawn/place, no CPU tick. " +
                "Assign Source, press Play, spawn. Keys 1-8 switch clips.",
                MessageType.Info);
        }
    }
}

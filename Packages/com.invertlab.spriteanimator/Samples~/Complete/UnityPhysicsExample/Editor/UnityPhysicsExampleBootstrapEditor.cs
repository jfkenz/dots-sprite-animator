using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Inspector for the Unity Physics example bootstrap.
    /// </summary>
    [CustomEditor(typeof(UnityPhysicsExampleBootstrap))]
    public sealed class UnityPhysicsExampleBootstrapEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "This sample's player/enemy live in the open scene (not the SubScene), " +
                "so Unity Physics body boxes are created at runtime on DOTS entities — " +
                "you will NOT see Collider2D children under the GameObjects.\n\n" +
                "Player does not need a body collider (attack is OverlapAabb). " +
                "Enemy needs the runtime hurtbox for hits.\n\n" +
                "View bodies: Window → Analysis → Physics Debugger.",
                MessageType.Info);

            var enemies = FindObjectsByType<UnityPhysicsExampleEnemy>(FindObjectsInactive.Exclude);
            int attached = 0;
            foreach (var e in enemies)
            {
                if (e != null && e.HasColliderAttached)
                    attached++;
            }
            EditorGUILayout.LabelField("Enemy hurtboxes", Application.isPlaying
                ? $"{attached} / {enemies.Length}"
                : "(enter Play)");

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(enemies.Length == 0 || !Application.isPlaying))
                {
                    if (GUILayout.Button(new GUIContent(
                            "Re-apply Body Colliders",
                            "Rebuild each enemy's profile body box on its runtime Physics entity.")))
                    {
                        int n = 0;
                        foreach (var enemy in enemies)
                        {
                            if (enemy != null && enemy.TryAttachUnityPhysicsCollider())
                                n++;
                        }
                        Debug.Log($"[UnityPhysicsExample] body colliders on {n} enemies");
                    }

                    using (new EditorGUI.DisabledScope(attached == 0))
                    {
                        if (GUILayout.Button(new GUIContent(
                                "Clear", "Remove PhysicsColliders from enemy hurtbox entities.")))
                        {
                            foreach (var enemy in enemies)
                                enemy?.ClearUnityPhysicsCollider();
                        }
                    }
                }
            }
        }
    }
}

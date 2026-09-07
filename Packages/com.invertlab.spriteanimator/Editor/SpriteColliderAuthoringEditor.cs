using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    [CustomEditor(typeof(SpriteColliderAuthoring))]
    [CanEditMultipleObjects]
    public sealed class SpriteColliderAuthoringEditor : UnityEditor.Editor
    {
        static readonly List<FrameBoxDef> BodyScratch = new();

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

            var data = set.Profile != null ? set.Profile.Data : null;
            byte mask = authoring.ResolveLifetimeMask(data);
            string detected = data != null && data.Hitboxes != null && data.Hitboxes.Count > 0
                ? DescribeMask(mask)
                : "no baked boxes";

            EditorGUILayout.LabelField("Resolved", DescribeMask(mask) +
                (authoring.Scope == SpriteColliderScope.Auto ? "  (auto - found: " + detected + ")" : ""));
            EditorGUILayout.LabelField("Unity 2D Colliders",
                authoring.Method == SpriteColliderMethod.Query ||
                authoring.Method == SpriteColliderMethod.UnityPhysics
                    ? "off"
                    : "on");

            if (authoring.Method != SpriteColliderMethod.UnityPhysics)
                return;

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Unity Physics", EditorStyles.boldLabel);

            SpriteUnityPhysicsShape.CollectCharacterBodyBoxes(data, BodyScratch);
            SpriteUnityPhysicsShape.CountShapes(BodyScratch, out int nBox, out int nSphere, out int nConvex);
            EditorGUILayout.LabelField("Profile body shapes",
                BodyScratch.Count == 0
                    ? "none -> Bake will add a full-cell Box"
                    : string.Format("{0} -> Box {1} / Sphere {2} / Convex {3}",
                        BodyScratch.Count, nBox, nSphere, nConvex));
            EditorGUILayout.LabelField("Baked on object",
                authoring.HasBakedUnityPhysicsColliders ? "yes (see UnityPhysicsColliders child)" : "no");

            EditorGUILayout.HelpBox(
                "Two workflows:\n" +
                "1) BAKE NOW - press Bake Colliders. Adds Box / Sphere / Convex colliders under UnityPhysicsColliders on this GameObject.\n" +
                "2) SKIP BAKE - leave empty. On Play the runtime recreates the DOTS hurtbox automatically.\n\n" +
                "Square->Box, Circle->Sphere, Polygon->Convex.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(
                        "Bake Colliders",
                        "Add BoxCollider / SphereCollider / MeshCollider(convex) under UnityPhysicsColliders.")))
                {
                    foreach (Object t in targets)
                    {
                        var a = t as SpriteColliderAuthoring;
                        if (a == null)
                            continue;
                        Undo.RegisterFullObjectHierarchyUndo(a.gameObject, "Bake Unity Physics Colliders");
                        a.Method = SpriteColliderMethod.UnityPhysics;
                        a.ApplyToAnimSet();
                        int n = a.BakeUnityPhysicsPreview();
                        EditorUtility.SetDirty(a);
                        EditorUtility.SetDirty(a.gameObject);
                        Selection.activeGameObject = a.gameObject;
                        var bakedRoot = a.transform.Find(SpriteUnityPhysicsPreview.RootName);
                        if (bakedRoot != null)
                            EditorGUIUtility.PingObject(bakedRoot);
                        Debug.Log("[SpriteCollider] Baked " + n + " Unity Physics collider(s) on " + a.name +
                                  " under " + SpriteUnityPhysicsPreview.RootName, a);
                    }
                }

                if (GUILayout.Button(new GUIContent(
                        "Clear",
                        "Remove UnityPhysicsColliders children and baked marker.")))
                {
                    foreach (Object t in targets)
                    {
                        var a = t as SpriteColliderAuthoring;
                        if (a == null)
                            continue;
                        Undo.RegisterFullObjectHierarchyUndo(a.gameObject, "Clear Unity Physics Colliders");
                        a.ClearUnityPhysicsPreview();
                        EditorUtility.SetDirty(a);
                    }
                }
            }
        }

        static string DescribeMask(byte mask)
        {
            if (mask == 0)
                return "none";
            var parts = new List<string>();
            if ((mask & SpriteColliderAuthoring.LifetimeFrame) != 0) parts.Add("Frame");
            if ((mask & SpriteColliderAuthoring.LifetimeCharacter) != 0) parts.Add("Character");
            if ((mask & SpriteColliderAuthoring.LifetimeClip) != 0) parts.Add("Clip");
            return string.Join(" + ", parts);
        }
    }
}

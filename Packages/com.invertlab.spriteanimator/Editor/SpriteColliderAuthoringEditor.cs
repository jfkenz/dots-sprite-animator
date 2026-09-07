using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    [CustomEditor(typeof(SpriteColliderAuthoring))]
    [CanEditMultipleObjects]
    public sealed class SpriteColliderAuthoringEditor : UnityEditor.Editor
    {
        static readonly List<FrameBoxDef> BodyScratch = new();
        static Type _previewType;
        static bool _previewResolved;
        static MethodInfo _bakeMethod;
        static MethodInfo _clearMethod;
        static MethodInfo _hasBakedMethod;
        static MethodInfo _collectMethod;
        static MethodInfo _countMethod;

        static bool TryResolvePhysicsPreview()
        {
            if (_previewResolved)
                return _previewType != null;
            _previewResolved = true;
            _previewType = Type.GetType(
                "InvertLab.Sprites.DOTS.SpriteUnityPhysicsPreview, InvertLab.SpriteAnimator.UnityPhysics");
            if (_previewType == null)
                return false;
            _bakeMethod = _previewType.GetMethod("Bake", BindingFlags.Public | BindingFlags.Static);
            _clearMethod = _previewType.GetMethod("Clear", BindingFlags.Public | BindingFlags.Static);
            _hasBakedMethod = _previewType.GetMethod("HasBaked", BindingFlags.Public | BindingFlags.Static);
            var shapeType = Type.GetType(
                "InvertLab.Sprites.DOTS.SpriteUnityPhysicsShape, InvertLab.SpriteAnimator.UnityPhysics");
            if (shapeType != null)
            {
                _collectMethod = shapeType.GetMethod("CollectCharacterBodyBoxes",
                    BindingFlags.Public | BindingFlags.Static);
                _countMethod = shapeType.GetMethod("CountShapes",
                    BindingFlags.Public | BindingFlags.Static);
            }
            return true;
        }

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

            if (!TryResolvePhysicsPreview())
            {
                EditorGUILayout.HelpBox(
                    "com.unity.physics is not installed. Animation still works. " +
                    "Install Unity Physics to enable Character body bake (Box/Sphere/Convex) " +
                    "and OverlapAabb hits.",
                    MessageType.Warning);
                return;
            }

            int nBox = 0, nSphere = 0, nConvex = 0, bodyCount = 0;
            if (_collectMethod != null)
            {
                BodyScratch.Clear();
                _collectMethod.Invoke(null, new object[] { data, BodyScratch });
                bodyCount = BodyScratch.Count;
                if (_countMethod != null)
                {
                    object[] args = { BodyScratch, 0, 0, 0 };
                    _countMethod.Invoke(null, args);
                    nBox = (int)args[1];
                    nSphere = (int)args[2];
                    nConvex = (int)args[3];
                }
            }

            EditorGUILayout.LabelField("Profile body shapes (Character)",
                bodyCount == 0
                    ? "none -> Bake will add a full-cell Box"
                    : string.Format("{0} -> Box {1} / Sphere {2} / Convex {3}",
                        bodyCount, nBox, nSphere, nConvex));

            bool baked = _hasBakedMethod != null &&
                         (bool)_hasBakedMethod.Invoke(null, new object[] { authoring.transform });
            EditorGUILayout.LabelField("Baked on object",
                baked ? "yes (UnityPhysicsColliders child)" : "no");

            EditorGUILayout.HelpBox(
                "Two workflows:\n" +
                "1) BAKE NOW - Bake Colliders adds Box/Sphere/Convex under UnityPhysicsColliders " +
                "(Character lifetime only).\n" +
                "2) SKIP BAKE - on Play, SpriteUnityPhysicsHurtbox.Ensure recreates the DOTS body.\n\n" +
                "Frame boxes stay AABB query (SpriteHitboxQuery) — not baked.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Bake Colliders",
                        "Character body -> Box / Sphere / Convex under UnityPhysicsColliders.")))
                {
                    foreach (Object t in targets)
                    {
                        var a = t as SpriteColliderAuthoring;
                        if (a == null) continue;
                        Undo.RegisterFullObjectHierarchyUndo(a.gameObject, "Bake Unity Physics Colliders");
                        a.Method = SpriteColliderMethod.UnityPhysics;
                        a.ApplyToAnimSet();
                        float sizeUnits = set != null ? set.SizeUnits : 1f;
                        var setA = a.GetComponent<SpriteAnimSetAuthoring>();
                        var dataA = setA != null && setA.Profile != null ? setA.Profile.Data : null;
                        float su = setA != null ? setA.SizeUnits : 1f;
                        int n = (int)_bakeMethod.Invoke(null, new object[] { a.transform, dataA, su });
                        EditorUtility.SetDirty(a);
                        var root = a.transform.Find("UnityPhysicsColliders");
                        if (root != null) EditorGUIUtility.PingObject(root);
                        Debug.Log("[SpriteCollider] Baked " + n + " collider(s) on " + a.name, a);
                    }
                }

                if (GUILayout.Button(new GUIContent("Clear", "Remove UnityPhysicsColliders.")))
                {
                    foreach (Object t in targets)
                    {
                        var a = t as SpriteColliderAuthoring;
                        if (a == null) continue;
                        Undo.RegisterFullObjectHierarchyUndo(a.gameObject, "Clear Unity Physics Colliders");
                        _clearMethod.Invoke(null, new object[] { a.transform });
                        EditorUtility.SetDirty(a);
                    }
                }
            }
        }

        static string DescribeMask(byte mask)
        {
            if (mask == 0) return "none";
            var parts = new List<string>();
            if ((mask & SpriteColliderAuthoring.LifetimeFrame) != 0) parts.Add("Frame");
            if ((mask & SpriteColliderAuthoring.LifetimeCharacter) != 0) parts.Add("Character");
            if ((mask & SpriteColliderAuthoring.LifetimeClip) != 0) parts.Add("Clip");
            return string.Join(" + ", parts);
        }
    }
}

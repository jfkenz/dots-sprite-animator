using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    [CustomEditor(typeof(SpriteSortAuthoring))]
    [CanEditMultipleObjects]
    public sealed class SpriteSortAuthoringEditor : UnityEditor.Editor
    {
        static SpriteSortLayerList _layerListCache;

        static SpriteSortLayerList FindLayerList()
        {
            if (_layerListCache != null)
                return _layerListCache;
            string[] guids = AssetDatabase.FindAssets("t:SpriteSortLayerList");
            if (guids == null || guids.Length == 0)
                return null;
            _layerListCache = AssetDatabase.LoadAssetAtPath<SpriteSortLayerList>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            return _layerListCache;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Total index entry: 1 = 0.001 z everywhere. total = layer×1000 +
            // order + offset. DelayedIntField commits once on Enter/focus-loss;
            // the write is applied immediately so nothing later can drop it.
            var layerProp = serializedObject.FindProperty("SortLayer");
            var orderProp = serializedObject.FindProperty("OrderInLayer");
            var offsetProp = serializedObject.FindProperty("DepthOffset");
            if (targets.Length == 1)
            {
                int current = SpriteSortDepth.ToIndex(
                    layerProp.intValue, orderProp.intValue, offsetProp.intValue);
                EditorGUI.BeginChangeCheck();
                int index = EditorGUILayout.DelayedIntField(new GUIContent("Sort Index",
                    $"Total depth coordinate where 1 = 0.001 z (higher = on top). " +
                    $"Index = Layer × {SpriteSortDepth.OrdersPerLayer} + Order + Offset; " +
                    "editing it fills Layer/Order below and clears Offset."),
                    current);
                if (EditorGUI.EndChangeCheck() && index != current)
                {
                    SpriteSortDepth.DecomposeIndex(index, out int layer, out int order);
                    layerProp.intValue = layer;
                    orderProp.intValue = order;
                    offsetProp.intValue = 0;
                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();
                    // the immediate apply consumes the change, so the shared
                    // sync below would skip this edit — sync here instead
                    SyncTransformZ((SpriteSortAuthoring)target);
                }
                float labelZ = SpriteSortDepth.FromLayerOrder(
                    layerProp.intValue, orderProp.intValue, offsetProp.intValue);
                EditorGUILayout.LabelField(" ",
                    $"= Layer {layerProp.intValue} · Order {orderProp.intValue} · " +
                    $"Offset {offsetProp.intValue}  →  z {labelZ:0.###}",
                    EditorStyles.miniLabel);
            }

            bool changed = false;

            // Sort Layer: named dropdown driven by the project's
            // SpriteSortLayerList asset (falls back to a raw int + create
            // button when none exists)
            var sortLayerProp = serializedObject.FindProperty("SortLayer");
            var layerList = FindLayerList();
            if (layerList != null && layerList.Layers.Count > 0)
            {
                var options = new string[layerList.Layers.Count + 1];
                var values = new int[layerList.Layers.Count];
                for (int i = 0; i < layerList.Layers.Count; i++)
                {
                    var entry = layerList.Layers[i];
                    options[i] = $"{entry.Name}  ({entry.Index})";
                    values[i] = entry.Index;
                }
                int current = sortLayerProp.intValue;
                int selected = Array.IndexOf(values, current);
                options[options.Length - 1] = $"Custom  ({current})";
                EditorGUI.BeginChangeCheck();
                int choice = EditorGUILayout.Popup("Sort Layer",
                    selected >= 0 ? selected : options.Length - 1, options);
                if (EditorGUI.EndChangeCheck() && choice < values.Length)
                    sortLayerProp.intValue = values[choice];
                changed = true; // popup draws applied state below
            }
            else
            {
                changed = EditorGUILayout.PropertyField(sortLayerProp);
                if (GUILayout.Button(new GUIContent(
                        "Create Sort Layers Asset",
                        "Create the project's named sorting layers (Assets/SpriteSortLayers.asset).")))
                {
                    var asset = CreateInstance<SpriteSortLayerList>();
                    AssetDatabase.CreateAsset(asset, "Assets/" +
                        SpriteSortLayerList.DefaultAssetName + ".asset");
                    Selection.activeObject = asset;
                }
            }

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
                camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            var status = SpriteSortAuthoring.CheckDepth(sort.BakedDepth, camera, out var message);
            if (status == SpriteSortAuthoring.DepthStatus.Invisible)
                EditorGUILayout.HelpBox(message, MessageType.Error);
            else if (status == SpriteSortAuthoring.DepthStatus.Risky)
                EditorGUILayout.HelpBox(message, MessageType.Warning);
            else if (camera != null)
            {
                float nearEdge = camera.transform.position.z + camera.nearClipPlane;
                float farEdge = camera.transform.position.z + camera.farClipPlane;
                float z = sort.BakedDepth;
                EditorGUILayout.HelpBox(
                    $"Camera '{camera.name}' z {camera.transform.position.z:0.###}, near " +
                    $"{camera.nearClipPlane:0.##}, far {camera.farClipPlane:0.#} → visible z " +
                    $"{nearEdge:0.###} … {farEdge:0.#}.\n" +
                    $"Baked z {z:0.###}: {z - nearEdge:0.##} from the front edge, " +
                    $"{farEdge - z:0.#} from the back edge.\n" +
                    "All fields: higher = on top. Editing them also writes the GameObject's " +
                    "world z (undo-able).", MessageType.Info);
            }
            else
                EditorGUILayout.HelpBox(
                    "No camera in scene — clip range cannot be checked.\n" +
                    "All fields: higher = on top; editing them also writes the GameObject's " +
                    "world z (undo-able).", MessageType.Info);
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

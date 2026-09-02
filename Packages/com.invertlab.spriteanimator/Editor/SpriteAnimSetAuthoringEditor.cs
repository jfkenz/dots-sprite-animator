using UnityEditor;
using UnityEngine;



namespace InvertLab.Sprites.DOTS.Editor
{
    [CustomEditor(typeof(SpriteAnimSetAuthoring))]
    public sealed class SpriteAnimSetAuthoringEditor : UnityEditor.Editor
    {
        SerializedProperty _profile;
        SerializedProperty _sheet;
        SerializedProperty _columns;
        SerializedProperty _rows;
        SerializedProperty _clips;
        SerializedProperty _initialClipIndex;
        SerializedProperty _sizeUnits;
        SerializedProperty _tint;
        SerializedProperty _showSpriteInScene;
        SerializedProperty _bakeUnityColliders;
        SerializedProperty _bakeFrameColliders;
        SerializedProperty _showSceneColliderGizmos;
        SerializedProperty _bakeUnitySockets;
        SerializedProperty _showSceneSocketGizmos;
        SerializedProperty _bakePivot;
        SerializedProperty _showScenePivotGizmos;



        void OnEnable()
        {
            _profile = serializedObject.FindProperty("Profile");
            _sheet = serializedObject.FindProperty("Sheet");
            _columns = serializedObject.FindProperty("Columns");
            _rows = serializedObject.FindProperty("Rows");
            _clips = serializedObject.FindProperty("Clips");
            _initialClipIndex = serializedObject.FindProperty("InitialClipIndex");
            _sizeUnits = serializedObject.FindProperty("SizeUnits");
            _tint = serializedObject.FindProperty("Tint");
            _showSpriteInScene = serializedObject.FindProperty("ShowSpriteInScene");
            _bakeUnityColliders = serializedObject.FindProperty("BakeUnityColliders");
            _bakeFrameColliders = serializedObject.FindProperty("BakeFrameColliders");
            _showSceneColliderGizmos = serializedObject.FindProperty("ShowSceneColliderGizmos");
            _bakeUnitySockets = serializedObject.FindProperty("BakeUnitySockets");
            _showSceneSocketGizmos = serializedObject.FindProperty("ShowSceneSocketGizmos");
            _bakePivot = serializedObject.FindProperty("BakePivot");
            _showScenePivotGizmos = serializedObject.FindProperty("ShowScenePivotGizmos");



            // Ensure crop mesh/material are applied when the inspector opens.
            var authoring = target as SpriteAnimSetAuthoring;
            if (authoring != null)
                authoring.RefreshScenePreview();
        }



        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var authoring = (SpriteAnimSetAuthoring)target;



            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_profile);
            bool profileChanged = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();



            bool reloadClicked = false;
            if (authoring.Profile != null)
                reloadClicked = GUILayout.Button("Reload From Profile");



            if (profileChanged || reloadClicked)
            {
                Undo.RecordObject(authoring, "Load Sprite Animator Profile");
                RecordPreviewTargets(authoring, "Load Sprite Animator Profile");
                if (authoring.ApplyFromProfile())
                {
                    EditorUtility.SetDirty(authoring);
                    serializedObject.Update();
                    authoring.RefreshScenePreview();
                    if (authoring.Sheet == null)
                        Debug.LogWarning(
                            "Profile has no Texture. Assign a sheet in Window > DOTS Sprite Animator and Save Profile.",
                            authoring);
                }
            }



            if (authoring.Profile != null)
            {
                EditorGUILayout.HelpBox(
                    authoring.Sheet != null
                        ? "Sheet, Columns, Rows, and Clips load from this Profile. Baker uses the Profile at bake. Tint, Size Units, Initial Clip Index, and Show Sprite stay on this component. Show Sprite draws the current clip frame (cropped) on this Quad. Uncheck to hide this Quad without affecting Play mode. Does not draw the full sheet."
                        : "Profile is assigned but has no Texture. Open Window > DOTS Sprite Animator, assign the sheet, Save Profile, then click Reload From Profile.",
                    authoring.Sheet != null ? MessageType.Info : MessageType.Warning);
            }



            bool locked = authoring.Profile != null;
            EditorGUI.BeginDisabledGroup(locked);
            EditorGUILayout.PropertyField(_sheet);
            EditorGUILayout.PropertyField(_columns);
            EditorGUILayout.PropertyField(_rows);
            EditorGUILayout.PropertyField(_clips, true);
            EditorGUI.EndDisabledGroup();



            EditorGUILayout.PropertyField(_initialClipIndex);
            EditorGUILayout.PropertyField(_sizeUnits);
            EditorGUILayout.PropertyField(_tint);



            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_showSpriteInScene, new GUIContent("Show Sprite", "Edit Mode Scene Quad only. Uses bottom-center cell pivot like the animator preview. Uncheck to hide the sprite mesh. Does not affect Play mode ECS."));
            EditorGUILayout.PropertyField(_showScenePivotGizmos, new GUIContent("Show Scene Pivot", "Draw a crosshair + Pivot label at the authored profile.Pivot (matches editor green pivot)."));
            bool previewChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.HelpBox(
                "Shows the current clip frame (cropped) with bottom-center cell pivot like the animator preview. Cyan wire is the full cell bounds (padding included). Uncheck to hide this Quad without affecting Play mode. Does not draw the full sheet. Orange boxes under SpriteColliders are separate collider gizmos, not the sheet texture.",
                MessageType.Info);



            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh Preview"))
                {
                    RecordPreviewTargets(authoring, "Refresh Show Sprite");
                    authoring.RefreshScenePreview();
                    EditorUtility.SetDirty(authoring);
                }
            }



            serializedObject.ApplyModifiedProperties();



            if (previewChanged)
            {
                Undo.RecordObject(authoring, "Toggle Show Sprite");
                RecordPreviewTargets(authoring, "Toggle Show Sprite");
                authoring.RefreshScenePreview();
            }



            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Colliders", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_bakeUnityColliders);
            using (new EditorGUI.DisabledScope(!_bakeUnityColliders.boolValue))
                EditorGUILayout.PropertyField(_bakeFrameColliders);
            EditorGUILayout.PropertyField(_showSceneColliderGizmos);
            bool colliderSettingsChanged = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox(
                "Query boxes stay on the profile for custom physics. Unity 2D spawns BoxCollider2D / CircleCollider2D / PolygonCollider2D children you can see in the Scene view. Scene gizmos follow the authored shape (box, circle, or polygon). Character and This Clip boxes spawn with Bake Unity Colliders; Bake Frame Colliders adds slash windows. Socket profiles keep their own collider data and draw in the Sprite Animator debug overlay.",
                MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Sync Unity Colliders"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(authoring.gameObject, "Bake Sprite Colliders");
                    authoring.BakeUnityColliders = true;
                    authoring.SyncUnityColliders();
                    EditorUtility.SetDirty(authoring);
                }
                if (GUILayout.Button("Clear Unity Colliders"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(authoring.gameObject, "Clear Sprite Colliders");
                    SpriteColliderWorld.ClearUnityColliders(authoring.transform);
                    EditorUtility.SetDirty(authoring);
                }
            }
            if (colliderSettingsChanged && authoring.BakeUnityColliders)
            {
                Undo.RegisterFullObjectHierarchyUndo(authoring.gameObject, "Bake Sprite Colliders");
                authoring.SyncUnityColliders();
                EditorUtility.SetDirty(authoring);
            }



            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Sockets", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_bakeUnitySockets);
            EditorGUILayout.PropertyField(_showSceneSocketGizmos);
            EditorGUILayout.PropertyField(_bakePivot, new GUIContent("Bake Pivot", "Create/update an empty Pivot child at the authored profile.Pivot in mesh-local space."));
            bool socketSettingsChanged = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox(
                "Spawns Transform children under SpriteSockets from frame sockets and independent motion tracks. Socket LocalPosition is pixels from profile.Pivot; Scene converts to mesh-local (bottom-center origin). Pivot child sits at profile.Pivot. Sync is animator to scene only.",
                MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Sync Unity Sockets"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(authoring.gameObject, "Bake Sprite Sockets");
                    authoring.BakeUnitySockets = true;
                    authoring.SyncUnitySockets();
                    EditorUtility.SetDirty(authoring);
                }
                if (GUILayout.Button("Clear Unity Sockets"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(authoring.gameObject, "Clear Sprite Sockets");
                    SpriteSocketWorld.ClearUnitySockets(authoring.transform);
                    EditorUtility.SetDirty(authoring);
                }
            }
            if (socketSettingsChanged)
            {
                Undo.RegisterFullObjectHierarchyUndo(authoring.gameObject, "Bake Sprite Sockets");
                if (authoring.BakeUnitySockets)
                    authoring.SyncUnitySockets();
                else
                {
                    SpriteSocketWorld.ClearUnitySockets(authoring.transform);
                    if (authoring.BakePivot)
                    {
                        var data = authoring.Profile?.Data;
                        var player = authoring.GetComponent<SpriteAnimPlayerAuthoring>();
                        string clipName = "clip";
                        int clipIndex = authoring.InitialClipIndex;
                        if (player != null)
                            clipIndex = player.ClipIndex;
                        if (authoring.Clips != null && clipIndex >= 0 && clipIndex < authoring.Clips.Length)
                            clipName = string.IsNullOrEmpty(authoring.Clips[clipIndex].Name)
                                ? "clip" : authoring.Clips[clipIndex].Name;
                        else if (data?.Clips != null && clipIndex >= 0 && clipIndex < data.Clips.Count)
                            clipName = data.Clips[clipIndex].Name;
                        SpriteSocketWorld.SyncPivotMarker(
                            authoring.transform, data, clipName,
                            player != null && player.FlipX,
                            player != null && player.FlipY);
                    }
                    else
                        SpriteSocketWorld.ClearPivotMarker(authoring.transform);
                }
                EditorUtility.SetDirty(authoring);
            }
        }



        static void RecordPreviewTargets(SpriteAnimSetAuthoring authoring, string undoName)
        {
            var renderer = authoring.GetComponent<MeshRenderer>();
            if (renderer != null)
                Undo.RecordObject(renderer, undoName);
            var filter = authoring.GetComponent<MeshFilter>();
            if (filter != null)
                Undo.RecordObject(filter, undoName);
        }
    }
}


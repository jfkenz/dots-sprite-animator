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
        SerializedProperty _showScenePreview;

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
            _showScenePreview = serializedObject.FindProperty("ShowScenePreview");
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
                var renderer = authoring.GetComponent<MeshRenderer>();
                if (renderer != null)
                    Undo.RecordObject(renderer, "Load Sprite Animator Profile");
                if (authoring.ApplyFromProfile())
                {
                    EditorUtility.SetDirty(authoring);
                    serializedObject.Update();
                    authoring.ApplyQuadPreview();
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
                        ? "Sheet, Columns, Rows, and Clips load from this Profile. Baker uses the Profile at bake. Tint, Size Units, Initial Clip Index, and Scene Preview stay on this component. Uncheck Scene Preview to hide the Quad in the Scene view. With it on, only the top clip first frame is shown (not the whole sheet)."
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
            EditorGUILayout.PropertyField(_showScenePreview);
            bool previewChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.HelpBox(
                "Uncheck Scene Preview to hide the Quad in Scene view. With it on, only the top clip first frame is shown (not the whole sheet).",
                MessageType.Info);

            serializedObject.ApplyModifiedProperties();

            if (previewChanged)
            {
                Undo.RecordObject(authoring, "Toggle Scene Preview");
                var previewRenderer = authoring.GetComponent<MeshRenderer>();
                if (previewRenderer != null)
                    Undo.RecordObject(previewRenderer, "Toggle Scene Preview");
                authoring.ApplyQuadPreview();
            }
        }
    }
}

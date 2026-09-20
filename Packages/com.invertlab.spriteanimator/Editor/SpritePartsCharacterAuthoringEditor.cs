using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>
    /// Starting Clip / Skin pick from the assigned Parts profile instead of free text.
    /// </summary>
    [InitializeOnLoad]
    [CustomEditor(typeof(SpritePartsCharacterAuthoring))]
    [CanEditMultipleObjects]
    public sealed class SpritePartsCharacterAuthoringEditor : UnityEditor.Editor
    {
        static Mesh s_PreviewQuad;
        static Material s_PreviewMaterial;

        static SpritePartsCharacterAuthoringEditor()
        {
            SceneView.duringSceneGui -= DrawAllPartsScenePreviews;
            SceneView.duringSceneGui += DrawAllPartsScenePreviews;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var authoring = (SpritePartsCharacterAuthoring)target;
            if (!serializedObject.isEditingMultipleObjects
                && SpritePartsAuthoringBundle.HasFrameAuthoringConflict(authoring.gameObject))
            {
                EditorGUILayout.HelpBox(
                    "This Parts character also has frame Set/Player authoring. Bake will fight itself until those are removed.",
                    MessageType.Error);
                if (GUILayout.Button("Fix Parts Authoring Conflict"))
                {
                    Undo.SetCurrentGroupName("Fix Parts Authoring Conflict");
                    int group = Undo.GetCurrentGroup();
                    SpritePartsAuthoringBundle.StripConflictingFrameAuthoring(authoring.gameObject);
                    Undo.CollapseUndoOperations(group);
                }
                EditorGUILayout.Space();
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Profile"));
            bool profileChanged = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            if (!serializedObject.isEditingMultipleObjects && authoring.Profile != null)
            {
                if (GUILayout.Button("Reload From Profile"))
                {
                    SpritePartsSceneSync.RefreshCharacter(authoring, recordUndo: true);
                    serializedObject.Update();
                }
                DrawProfileStatus(authoring);
            }

            if (profileChanged && !serializedObject.isEditingMultipleObjects)
                SpritePartsSceneSync.RefreshCharacter(authoring);

            if (serializedObject.isEditingMultipleObjects)
            {
                serializedObject.Update();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("StartingClipName"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("StartingSkinId"));
                DrawRest();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            var profile = authoring.Profile != null ? authoring.Profile.Data : null;
            if (profile != null)
                profile.EnsurePartsRig();

            DrawClipPopup(authoring, profile);
            DrawSkinPopup(authoring, profile);

            serializedObject.Update();
            DrawRest();
            serializedObject.ApplyModifiedProperties();
        }

        static void DrawAllPartsScenePreviews(SceneView sceneView)
        {
            if (Application.isPlaying || Event.current.type != EventType.Repaint)
                return;
            var characters = Object.FindObjectsByType<SpritePartsCharacterAuthoring>(
                FindObjectsInactive.Exclude);
            for (int i = 0; i < characters.Length; i++)
            {
                var character = characters[i];
                if (character != null && character.isActiveAndEnabled &&
                    character.gameObject.scene.IsValid())
                    DrawPartsScenePreview(character);
            }
        }

        static void DrawPartsScenePreview(SpritePartsCharacterAuthoring authoring)
        {
            var profile = authoring != null && authoring.Profile != null
                ? authoring.Profile.Data
                : null;
            if (profile == null || profile.AnimKind != SpriteAnimKind.Parts ||
                profile.PartsSlots == null || profile.PartsSlots.Count == 0)
                return;

            int clipIndex = ResolvePreviewClip(profile, authoring.StartingClipName);
            if (!SpritePartsOnion.TrySampleCharacter(
                    profile, clipIndex, 0f, Allocator.Temp,
                    out var blob, out var poses, out var matrices, out _))
                return;

            try
            {
                EnsurePreviewResources();
                if (s_PreviewQuad == null || s_PreviewMaterial == null)
                    return;

                string skinId = string.IsNullOrWhiteSpace(authoring.StartingSkinId)
                    ? profile.PartsDefaultSkinId
                    : authoring.StartingSkinId;
                var skin = SpritePartsAuthoringOps.ApplySkinPreview(profile, skinId);
                var facing = Matrix4x4.Scale(new Vector3(
                    authoring.FlipX ? -1f : 1f,
                    authoring.FlipY ? -1f : 1f,
                    1f));
                var root = authoring.transform.localToWorldMatrix * facing;

                for (int i = 0; i < blob.Value.Slots.Length && i < matrices.Length; i++)
                {
                    string slotId = blob.Value.Slots[i].SlotId.ToString();
                    var slot = SpritePartsAuthoringOps.FindSlot(profile, slotId);
                    if (slot == null || SpritePartsAuthoringOps.SlotOrAncestorHidden(profile, slotId))
                        continue;
                    string appearanceId = SpritePartsAuthoringOps.ResolvePreviewAppearanceId(
                        profile, slot, clipIndex, 0f, skin);
                    var appearance = SpritePartsAuthoringOps.FindAppearance(profile, appearanceId);
                    if (appearance == null ||
                        !SpritePartsGeometry.TryResolve(profile, appearance, false, out var geo, out _))
                        continue;
                    var sheet = profile.SheetAt(appearance.SheetIndex);
                    if (sheet?.Texture == null)
                        continue;

                    Rect uv = SpriteSheetProfile.GetCellUvRect(sheet, appearance.CellIndex);
                    s_PreviewQuad.uv = new[]
                    {
                        new Vector2(uv.xMin, uv.yMin),
                        new Vector2(uv.xMax, uv.yMin),
                        new Vector2(uv.xMax, uv.yMax),
                        new Vector2(uv.xMin, uv.yMax),
                    };
                    s_PreviewMaterial.mainTexture = sheet.Texture;
                    s_PreviewMaterial.color = authoring.Tint;

                    float depth = SpriteSortDepth.FromIndex(blob.Value.Slots[i].DrawRank);
                    var art = Matrix4x4.TRS(
                        new Vector3(geo.FrameOffset.x, geo.FrameOffset.y, depth),
                        Quaternion.identity,
                        new Vector3(geo.LogicalWorldSize.x, geo.LogicalWorldSize.y, 1f));
                    s_PreviewMaterial.SetPass(0);
                    Graphics.DrawMeshNow(s_PreviewQuad, root * ToMatrix(matrices[i]) * art);
                }

                DrawRootGuide(root, authoring.transform.position, profile, blob, matrices, clipIndex, skin);
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        static void DrawRootGuide(
            Matrix4x4 root, Vector3 origin, SpriteSheetProfile profile,
            BlobAssetReference<SpritePartsSetBlob> blob,
            NativeArray<float4x4> matrices, int clipIndex,
            Dictionary<string, string> skin)
        {
            Vector3 T(float x, float y) => root.MultiplyPoint3x4(new Vector3(x, y, 0f));
            var cyan = new Color(0.35f, 0.85f, 0.9f, 0.95f);
            Vector3[] box =
            {
                T(-0.5f, -0.5f), T(0.5f, -0.5f), T(0.5f, 0.5f), T(-0.5f, 0.5f), T(-0.5f, -0.5f),
            };
            Handles.color = cyan;
            Handles.DrawAAPolyLine(3f, box);
            Handles.color = new Color(0.85f, 0.32f, 0.32f, 0.95f);
            Handles.DrawLine(T(-0.5f, 0f), T(0.5f, 0f));
            Handles.color = new Color(0.32f, 0.82f, 0.42f, 0.95f);
            Handles.DrawLine(T(0f, -0.5f), T(0f, 0.5f));
            Handles.color = cyan;
            Handles.DrawSolidDisc(origin, Vector3.forward, HandleUtility.GetHandleSize(origin) * 0.04f);
            Handles.Label(T(0.08f, 0.08f), "Root");

            float2 center;
            float2 size;
            bool stored = SpritePartsAuthoringOps.HasStoredRootBounds(profile);
            if (stored)
            {
                center = new float2(profile.PartsRootBoundsCenter.x, profile.PartsRootBoundsCenter.y);
                size = new float2(profile.PartsRootBoundsSize.x, profile.PartsRootBoundsSize.y);
            }
            else if (blob.IsCreated &&
                     SpritePartsAuthoringOps.TryEncapsulateWorldAabb(
                         profile, skin, ref blob.Value, matrices, clipIndex, 0f,
                         out var min, out var max))
            {
                center = 0.5f * (min + max);
                size = max - min;
            }
            else
            {
                return;
            }

            float2 half = size * 0.5f;
            Vector3[] bounds =
            {
                T(center.x - half.x, center.y - half.y),
                T(center.x + half.x, center.y - half.y),
                T(center.x + half.x, center.y + half.y),
                T(center.x - half.x, center.y + half.y),
                T(center.x - half.x, center.y - half.y),
            };
            Handles.color = stored
                ? new Color(1f, 0.38f, 0.32f, 0.95f)
                : new Color(1f, 0.55f, 0.2f, 0.55f);
            Handles.DrawAAPolyLine(stored ? 3f : 2f, bounds);
            Handles.Label(T(center.x - half.x, center.y + half.y), stored ? "Bounds" : "Bounds (Fit to store)");
        }

        static int ResolvePreviewClip(SpriteSheetProfile profile, string startingName)
        {
            var clips = profile.PartsClips;
            if (clips == null || clips.Count == 0)
                return 0;
            if (!string.IsNullOrWhiteSpace(startingName))
            {
                for (int i = 0; i < clips.Count; i++)
                    if (clips[i] != null &&
                        string.Equals(clips[i].Name, startingName, System.StringComparison.Ordinal))
                        return i;
            }
            string defaultId = SpritePartIdUtility.Canonical(profile.PartsDefaultClipId);
            if (!string.IsNullOrEmpty(defaultId))
            {
                for (int i = 0; i < clips.Count; i++)
                    if (clips[i] != null &&
                        SpritePartIdUtility.Canonical(clips[i].ClipId, clips[i].Name) == defaultId)
                        return i;
            }
            return 0;
        }

        static void EnsurePreviewResources()
        {
            if (s_PreviewQuad == null)
            {
                s_PreviewQuad = new Mesh
                {
                    name = "InvertLab Parts Scene Preview",
                    hideFlags = HideFlags.HideAndDontSave,
                    vertices = new[]
                    {
                        new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f),
                        new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f),
                    },
                    triangles = new[] { 0, 2, 1, 0, 3, 2 },
                };
            }
            if (s_PreviewMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    return;
                s_PreviewMaterial = new Material(shader)
                {
                    name = "InvertLab Parts Scene Preview",
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
        }

        static Matrix4x4 ToMatrix(float4x4 value)
            => new(
                new Vector4(value.c0.x, value.c0.y, value.c0.z, value.c0.w),
                new Vector4(value.c1.x, value.c1.y, value.c1.z, value.c1.w),
                new Vector4(value.c2.x, value.c2.y, value.c2.z, value.c2.w),
                new Vector4(value.c3.x, value.c3.y, value.c3.z, value.c3.w));

        static void DrawProfileStatus(SpritePartsCharacterAuthoring authoring)
        {
            var profile = authoring.Profile != null ? authoring.Profile.Data : null;
            if (profile == null)
            {
                EditorGUILayout.HelpBox("Assign a Parts profile, then Save Profile or Reload From Profile so the scene matches.", MessageType.Info);
                return;
            }
            profile.EnsurePartsRig();
            if (profile.PartsSlots == null || profile.PartsSlots.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "This profile has no parts. The scene character is empty until you add slots and Save Profile.",
                    MessageType.Info);
                return;
            }
            var validation = SpritePartsValidation.Validate(profile);
            if (!validation.Ok)
            {
                EditorGUILayout.HelpBox(
                    "Profile will not bake until these are fixed:\n" + string.Join("\n", validation.Errors),
                    MessageType.Error);
                return;
            }
            if (profile.AnimKind != SpriteAnimKind.Parts)
            {
                EditorGUILayout.HelpBox(
                    $"Profile runtime mode is {profile.AnimKind}, so this character bakes nothing. "
                    + "Switch it to Parts here, or use 'Use Parts for Character' in the DOTS Sprite Animator window.",
                    MessageType.Warning);
                if (GUILayout.Button("Use Parts For This Character"))
                    SwitchProfileToParts(authoring);
            }
        }

        static void SwitchProfileToParts(SpritePartsCharacterAuthoring authoring)
        {
            var asset = authoring.Profile;
            Undo.RecordObject(asset, "Use Parts For Character");
            var result = SpritePartsAuthoringOps.TrySetAnimKind(asset.Data, SpriteAnimKind.Parts);
            if (!result.Ok)
            {
                EditorUtility.DisplayDialog("Cannot use Parts", result.Reason, "OK");
                return;
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            SpritePartsSceneSync.RefreshCharactersUsing(asset);
        }

        void DrawRest()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("FlipX"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("FlipY"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("CharacterOrder"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("PlayOnEnable"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("PlaybackTimeScale"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Tint"));
        }

        void DrawClipPopup(SpritePartsCharacterAuthoring authoring, SpriteSheetProfile profile)
        {
            var clips = profile?.PartsClips;
            if (profile == null)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.Popup("Starting Clip", 0, new[] { "(assign a Parts profile)" });
                return;
            }

            if (clips == null || clips.Count == 0)
            {
                EditorGUILayout.HelpBox("This profile has no Parts clips yet.", MessageType.Warning);
                return;
            }

            var labels = new List<string>(clips.Count + 2);
            var values = new List<string>(clips.Count + 2);
            labels.Add("Default (profile / first clip)");
            values.Add("");

            int current = 0;
            string currentName = authoring.StartingClipName ?? "";
            bool matched = string.IsNullOrEmpty(currentName);
            for (int i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];
                string name = clip != null ? clip.Name : null;
                if (string.IsNullOrWhiteSpace(name))
                    name = clip != null && !string.IsNullOrWhiteSpace(clip.ClipId)
                        ? clip.ClipId
                        : ("clip " + i);
                labels.Add(name);
                values.Add(clip != null ? (clip.Name ?? "") : "");
                if (!matched &&
                    string.Equals(clip != null ? clip.Name : null, currentName, System.StringComparison.Ordinal))
                {
                    current = values.Count - 1;
                    matched = true;
                }
            }

            if (!matched)
            {
                labels.Add(currentName + " (not on profile)");
                values.Add(currentName);
                current = values.Count - 1;
            }

            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(
                new GUIContent("Starting Clip",
                    "Parts clip to play on bake. Default uses the profile default, then the first clip. Left/right facing is Flip X, not a second clip."),
                current, labels.ToArray());
            if (EditorGUI.EndChangeCheck() && next >= 0 && next < values.Count)
            {
                Undo.RecordObject(authoring, "Set Starting Clip");
                authoring.StartingClipName = values[next];
                EditorUtility.SetDirty(authoring);
            }
        }

        void DrawSkinPopup(SpritePartsCharacterAuthoring authoring, SpriteSheetProfile profile)
        {
            var skins = profile?.PartsSkins;
            if (profile == null)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.Popup("Starting Skin", 0, new[] { "(assign a Parts profile)" });
                return;
            }

            var labels = new List<string>();
            var values = new List<string>();
            labels.Add("Default (profile / slot defaults)");
            values.Add("");

            int current = 0;
            string currentId = authoring.StartingSkinId ?? "";
            bool matched = string.IsNullOrEmpty(currentId);
            if (skins != null)
            {
                for (int i = 0; i < skins.Count; i++)
                {
                    var skin = skins[i];
                    string id = skin != null ? skin.SkinId : null;
                    string name = skin != null ? skin.Name : null;
                    if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
                        continue;
                    string value = !string.IsNullOrWhiteSpace(id) ? id : name;
                    string label = !string.IsNullOrWhiteSpace(name) && name != value
                        ? name + "  (" + value + ")"
                        : value;
                    labels.Add(label);
                    values.Add(value);
                    if (!matched && string.Equals(value, currentId, System.StringComparison.Ordinal))
                    {
                        current = values.Count - 1;
                        matched = true;
                    }
                }
            }

            if (!matched)
            {
                labels.Add(currentId + " (not on profile)");
                values.Add(currentId);
                current = values.Count - 1;
            }

            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(
                new GUIContent("Starting Skin", "Optional skin id. Empty uses profile default appearances."),
                current, labels.ToArray());
            if (EditorGUI.EndChangeCheck() && next >= 0 && next < values.Count)
            {
                Undo.RecordObject(authoring, "Set Starting Skin");
                authoring.StartingSkinId = values[next];
                EditorUtility.SetDirty(authoring);
            }
        }
    }
}

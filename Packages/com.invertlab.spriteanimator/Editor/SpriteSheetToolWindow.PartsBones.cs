using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Bones (Spine-style): joints with no image. Drawn as a tapered bone along the joint's +X axis,
    // selected / moved / rotated / keyed like any part, used by mesh weights and IK. Never drawn in the game.
    public sealed partial class SpriteSheetToolWindow
    {
        static readonly Color PartsBoneFill = new Color(0.85f, 0.85f, 0.95f, 0.55f);
        static readonly Color PartsBoneSelected = new Color(0.35f, 0.9f, 0.55f, 0.85f);

        void AddPartsBone()
        {
            string parentId = _partsSelectedSlotIds.Count == 1 ? PrimarySelectedPartsSlotId() : null;
            RecordPartsUndo("Add Bone");
            var result = SpritePartsAuthoringOps.TryAddBone(_profile, parentId, out var created);
            if (!result.Ok || created == null)
            {
                _status = result.Reason;
                return;
            }
            if (!string.IsNullOrEmpty(parentId))
                _partsExpandedSlotIds.Add(SpritePartIdUtility.Canonical(parentId));
            SaveDirty();
            SelectPartsSlotId(created.SlotId, false, false);
            BeginPartsRename(created);
            _status = "Added bone " + created.Name + (string.IsNullOrEmpty(parentId) ? "" : " under the selected part")
                      + ". Parts added under it follow it.";
        }

        /// <summary>A bone on the canvas: a tapered shape from the joint along its +X axis.</summary>
        void DrawPartsBone(Vector2 joint, float4x4 localToCanvasWorld, float length, bool selected, bool pickable)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            float2 x = localToCanvasWorld.c0.xy;
            float scale = math.length(x);
            if (scale < 1e-5f)
                return;
            Vector2 dir = new Vector2(x.x, -x.y) / scale; // world y up -> GUI y down
            float px = Mathf.Max(12f, (length > 1e-4f ? length : 1f) * scale * 64f * _previewZoom);
            Vector2 tip = joint + dir * px;
            Vector2 side = new Vector2(-dir.y, dir.x) * Mathf.Clamp(px * 0.12f, 3f, 10f);
            Vector2 mid = joint + dir * Mathf.Min(px * 0.22f, 24f);
            Handles.BeginGUI();
            Handles.color = selected ? PartsBoneSelected : PartsBoneFill;
            Handles.DrawAAConvexPolygon(joint, mid + side, tip, mid - side);
            Handles.color = selected ? new Color(0.6f, 1f, 0.7f, 1f) : new Color(1f, 1f, 1f, 0.8f);
            Handles.DrawAAPolyLine(1.5f, joint, mid + side, tip, mid - side, joint);
            Handles.DrawWireDisc(joint, Vector3.forward, 4f);
            Handles.EndGUI();
        }

        void DrawPartsBoneInspector(SpritePartSlotDef slot, bool partLocked)
        {
            if (slot == null || !slot.IsBone)
                return;
            GUILayout.Space(6f);
            GUILayout.Label("BONE", _sectionStyle);
            EditorGUILayout.LabelField("A joint with no image: never drawn in the game. Parts under it follow it; it can drive mesh weights and IK.",
                EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(partLocked))
            {
                EditorGUI.BeginChangeCheck();
                float length = EditorGUILayout.FloatField(new GUIContent("Length", "World units along the bone's +X axis"),
                    slot.BoneLength > 1e-4f ? slot.BoneLength : 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    RecordPartsUndo("Bone Length");
                    slot.BoneLength = Mathf.Max(0.01f, length);
                    SaveDirty();
                }
            }
        }
    }
}

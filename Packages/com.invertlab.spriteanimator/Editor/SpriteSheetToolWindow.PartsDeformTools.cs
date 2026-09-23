using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Deform key tools (Warp):
    //   Copy Deform      the current key's vertex offsets (only the selected vertices when some are selected)
    //   Paste            onto this key, or another part with the same vertex count
    //   Paste Mirrored   flipped across the Mirror axis through the mirror partners (left smile -> right smile)
    //   Ghost            the previous / next deform key's mesh as faint wireframes while warping
    public sealed partial class SpriteSheetToolWindow
    {
        Vector2[] _partsDeformClip;
        bool[] _partsDeformClipMask;
        string _partsDeformClipFrom;
        [SerializeField] bool _partsDeformOnion = true;

        static readonly Color PartsOnionBefore = new Color(0.35f, 0.6f, 1f, 0.45f);
        static readonly Color PartsOnionAfter = new Color(0.4f, 1f, 0.5f, 0.45f);

        void CopyPartsDeform()
        {
            var slot = CurrentPartsSlot;
            var pose = slot != null ? SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime) : default;
            if (slot == null || !pose.Lattice.HasMesh)
            {
                _status = "Copy Deform needs a part with a mesh.";
                return;
            }
            int n = pose.Lattice.PointCount;
            _partsDeformClip = new Vector2[n];
            _partsDeformClipMask = new bool[n];
            bool onlySelected = _partsWarpSelection.Count > 0
                                && _partsWarpSelectionSlotId == SpritePartIdUtility.Canonical(slot.SlotId);
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                var off = pose.Lattice.GetOffset(i);
                _partsDeformClip[i] = new Vector2(off.x, off.y);
                _partsDeformClipMask[i] = !onlySelected || _partsWarpSelection.Contains(i);
                if (_partsDeformClipMask[i])
                    count++;
            }
            _partsDeformClipFrom = slot.Name;
            _status = "Copied the deform of " + count + " vertices from " + slot.Name + ".";
        }

        void PastePartsDeform(bool mirrored)
        {
            var slot = CurrentPartsSlot;
            if (_partsDeformClip == null || slot == null)
                return;
            var pose = SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
            var lattice = pose.Lattice;
            int n = lattice.PointCount;
            if (!lattice.HasMesh || n != _partsDeformClip.Length)
            {
                _status = "Paste needs a part with the same vertex count (" + _partsDeformClip.Length + "); this one has " + n + ".";
                return;
            }
            int[] partner = mirrored ? MirrorPartnersFor(slot.SlotId) : null;
            int pasted = 0;
            for (int i = 0; i < n; i++)
            {
                int src = mirrored ? (i < partner.Length ? partner[i] : -1) : i;
                if (src < 0 || src >= n || !_partsDeformClipMask[src] || IsWarpPinned(slot.SlotId, i))
                    continue;
                Vector2 off = _partsDeformClip[src];
                if (mirrored)
                    off = MirrorPasteOffset(off, src == i); // on the axis: stays on it
                lattice.SetPoint(i, lattice.GetRest(i) + new float2(off.x, off.y));
                pasted++;
            }
            if (pasted == 0)
            {
                _status = mirrored ? "No mirror pairs to paste through: check the Mirror axis." : "Nothing to paste.";
                return;
            }
            pose.Lattice = lattice;
            RecordPartsUndo(mirrored ? "Paste Deform Mirrored" : "Paste Deform");
            ApplyPartsPoseEdit(slot.SlotId, pose);
            _status = "Pasted the deform onto " + pasted + " vertices" + (mirrored ? ", mirrored." : ".");
        }

        /// <summary>Times of the deform keys just before and after the playhead on this part (-1 = none).</summary>
        void NeighbourDeformKeys(string slotId, int vertexCount, out float before, out float after)
        {
            before = after = -1f;
            var clip = CurrentPartsClip;
            if (clip?.Tracks == null)
                return;
            string id = SpritePartIdUtility.Canonical(slotId);
            float eps = 0.5f / Mathf.Max(1f, _partsDisplayFps);
            foreach (var track in clip.Tracks)
            {
                if (track?.Keys == null || track.Kind != SpritePartsTrackKind.Pose || SpritePartIdUtility.Canonical(track.SlotId) != id)
                    continue;
                foreach (var key in track.Keys)
                {
                    if (key?.Deform == null || key.Deform.Length != vertexCount)
                        continue;
                    if (key.Time < _partsPreviewTime - eps && key.Time > before)
                        before = key.Time;
                    if (key.Time > _partsPreviewTime + eps && (after < 0f || key.Time < after))
                        after = key.Time;
                }
            }
        }

        /// <summary>Onion skin for deforms: neighbouring deform keys as faint wireframes (call inside Handles.BeginGUI).</summary>
        void DrawPartsDeformOnion(SpritePartSlotDef slot, Rect rect, Vector2 joint, float guiDeg, bool flipX, bool flipY)
        {
            if (!_partsDeformOnion || slot?.Mesh == null || !slot.Mesh.HasMesh || Event.current.type != EventType.Repaint)
                return;
            NeighbourDeformKeys(slot.SlotId, slot.Mesh.VertexCount, out float before, out float after);
            DrawDeformGhost(slot.SlotId, before, rect, joint, guiDeg, flipX, flipY, PartsOnionBefore);
            DrawDeformGhost(slot.SlotId, after, rect, joint, guiDeg, flipX, flipY, PartsOnionAfter);
        }

        void DrawDeformGhost(string slotId, float time, Rect rect, Vector2 joint, float guiDeg, bool flipX, bool flipY, Color color)
        {
            if (time < 0f)
                return;
            var lattice = SampleLocalPoseForSlot(slotId, time).Lattice;
            if (!lattice.HasMesh)
                return;
            var pts = new Vector3[lattice.PointCount];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = PartsWarpPointGui(rect, joint, guiDeg, flipX, flipY, lattice.GetPoint(i));
            Handles.color = color;
            int tris = lattice.IndexCount - lattice.IndexCount % 3;
            for (int t = 0; t < tris; t += 3)
            {
                int a = lattice.GetIndex(t), b = lattice.GetIndex(t + 1), c = lattice.GetIndex(t + 2);
                if ((uint)a < (uint)pts.Length && (uint)b < (uint)pts.Length && (uint)c < (uint)pts.Length)
                    Handles.DrawAAPolyLine(1.25f, pts[a], pts[b], pts[c], pts[a]);
            }
        }

        void DrawPanelDeformToolsSection()
        {
            GUILayout.Space(6f);
            EditorGUILayout.LabelField("DEFORM KEYS", _sectionStyle);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Copy", "Copy this key's deform (only the selected vertices when some are selected)")))
                CopyPartsDeform();
            using (new EditorGUI.DisabledScope(_partsDeformClip == null))
            {
                if (GUILayout.Button(new GUIContent("Paste", "Paste onto this key (same vertex count)")))
                    PastePartsDeform(false);
                if (GUILayout.Button(new GUIContent("Paste Mirrored", "Paste flipped across the Mirror axis through the mirror partners")))
                    PastePartsDeform(true);
            }
            EditorGUILayout.EndHorizontal();
            if (_partsDeformClip != null)
                EditorGUILayout.LabelField("Clipboard: " + _partsDeformClipFrom + " (" + _partsDeformClip.Length + " vertices)", EditorStyles.wordWrappedMiniLabel);
            _partsDeformOnion = EditorGUILayout.ToggleLeft(new GUIContent("Ghost neighbouring keys",
                "Show the previous (blue) and next (green) deform key's mesh while warping"), _partsDeformOnion);
        }
    }
}

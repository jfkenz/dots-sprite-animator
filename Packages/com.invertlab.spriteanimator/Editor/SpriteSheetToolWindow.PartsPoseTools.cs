using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Pose tools (timeline bar, Animate mode). Shift+click = the selected parts only.
    //   Copy Pose    the character's pose at the playhead (kept across clips)
    //   Paste Pose   keys the copied pose at the playhead (any clip)
    //   Mirror Pose  keys the pose flipped left-right: "Arm L" takes "Arm R"'s motion mirrored (walk cycles' second half)
    public sealed partial class SpriteSheetToolWindow
    {
        static Dictionary<string, SpritePartsAuthoringOps.PoseValue> s_partsPoseClipboard;

        // Ghost clip: another clip's pose drawn under the one being edited (match the end of Idle to the start of Run).
        [SerializeField] string _partsGhostClipId = string.Empty;
        [SerializeField] int _partsGhostAt = 1; // 0 start, 1 end, 2 seconds
        [SerializeField] float _partsGhostSeconds;

        /// <summary>The ghost clip's index and time, or false when off.</summary>
        bool TryPartsGhostClip(out int clipIndex, out float time)
        {
            clipIndex = string.IsNullOrEmpty(_partsGhostClipId) ? -1 : SpritePartsAuthoringOps.FindClipIndex(_profile, _partsGhostClipId);
            time = 0f;
            if (clipIndex < 0 || _partsMode != SpritePartsStudioMode.Animate)
                return false;
            float duration = Mathf.Max(1e-3f, _profile.PartsClips[clipIndex].Duration);
            time = _partsGhostAt == 0 ? 0f : _partsGhostAt == 1 ? duration : Mathf.Clamp(_partsGhostSeconds, 0f, duration);
            return true;
        }

        void DrawPartsGhostClipInspector()
        {
            if (_profile == null || _partsMode != SpritePartsStudioMode.Animate)
                return;
            bool on = TryPartsGhostClip(out int ghostClip, out _);
            if (!PartsSection("GHOST CLIP", on ? _profile.PartsClips[ghostClip].Name : "off"))
                return;
            var names = new List<string> { "(off)" };
            var ids = new List<string> { string.Empty };
            foreach (var c in _profile.PartsClips)
            {
                if (c == null)
                    continue;
                names.Add(c.Name);
                ids.Add(SpritePartIdUtility.Canonical(c.ClipId));
            }
            EditorGUI.BeginChangeCheck();
            int choice = EditorGUILayout.Popup(new GUIContent("Clip", "Draw this clip's pose as a green ghost under the canvas"),
                Mathf.Max(0, ids.IndexOf(SpritePartIdUtility.Canonical(_partsGhostClipId ?? string.Empty))), names.ToArray());
            int at = GUILayout.Toolbar(_partsGhostAt, new[] { "Start", "End", "Time" }, EditorStyles.miniButton);
            float seconds = _partsGhostSeconds;
            if (at == 2)
                seconds = EditorGUILayout.FloatField(new GUIContent("Seconds"), _partsGhostSeconds);
            if (EditorGUI.EndChangeCheck())
            {
                _partsGhostClipId = ids[choice];
                _partsGhostAt = at;
                _partsGhostSeconds = Mathf.Max(0f, seconds);
                Repaint();
            }
            EditorGUILayout.LabelField("Match poses between clips: the end of one to the start of the next.", EditorStyles.wordWrappedMiniLabel);
        }

        void DrawPartsPoseTools(Rect r)
        {
            if (CurrentPartsClip == null || _partsMode != SpritePartsStudioMode.Animate)
                return;
            bool selectedOnly = Event.current.shift;
            if (GUI.Button(new Rect(r.x, r.y, 62f, r.height), new GUIContent("Copy Pose",
                    "Copy the pose at the playhead (Shift: the selected parts only)"), EditorStyles.miniButtonLeft))
            {
                s_partsPoseClipboard = CapturePartsPose(selectedOnly);
                _status = "Copied the pose of " + s_partsPoseClipboard.Count + " part" + (s_partsPoseClipboard.Count == 1 ? "" : "s");
            }
            using (new EditorGUI.DisabledScope(s_partsPoseClipboard == null))
            {
                if (GUI.Button(new Rect(r.x + 62f, r.y, 40f, r.height), new GUIContent("Paste",
                        "Key the copied pose at the playhead (Shift: onto the selected parts only)"), EditorStyles.miniButtonMid))
                    KeyPartsPose(s_partsPoseClipboard, selectedOnly ? new HashSet<string>(SelectedSlotIdsForMask()) : null, "Paste Pose");
            }
            if (GUI.Button(new Rect(r.x + 102f, r.y, 48f, r.height), new GUIContent("Mirror",
                    "Key the pose at the playhead flipped left-right: left and right parts swap their motion, mirrored " +
                    "(Shift: the selected parts and their partners only)"), EditorStyles.miniButtonRight))
            {
                var pose = CapturePartsPose(false);
                var mirrored = SpritePartsAuthoringOps.MirrorPose(_profile, pose);
                HashSet<string> only = null;
                if (selectedOnly)
                {
                    only = new HashSet<string>();
                    foreach (string id in SelectedSlotIdsForMask())
                    {
                        only.Add(id);
                        var partner = SpritePartsAuthoringOps.MirrorPartner(_profile, SpritePartsAuthoringOps.FindSlot(_profile, id));
                        if (partner != null)
                            only.Add(SpritePartIdUtility.Canonical(partner.SlotId));
                    }
                }
                KeyPartsPose(mirrored, only, "Mirror Pose");
            }
        }

        void KeyPartsPose(Dictionary<string, SpritePartsAuthoringOps.PoseValue> pose, ICollection<string> only, string what)
        {
            RecordPartsUndo(what);
            var result = SpritePartsAuthoringOps.PastePose(_profile, _partsSelectedClip, _partsPreviewTime, pose, only);
            _status = result.Ok ? what + ": keyed " + result.Affected + " part" + (result.Affected == 1 ? "" : "s") : result.Reason;
            if (result.Ok)
                SaveDirty();
            Repaint();
        }

        /// <summary>The keyed pose at the playhead (no IK / jiggle), per part.</summary>
        Dictionary<string, SpritePartsAuthoringOps.PoseValue> CapturePartsPose(bool selectedOnly)
        {
            var result = new Dictionary<string, SpritePartsAuthoringOps.PoseValue>();
            var selected = selectedOnly ? new HashSet<string>(SelectedSlotIdsForMask()) : null;
            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), _partsPreviewTime, Allocator.Temp, true,
                    out var blob, out var poses, out var matrices, out _))
                return result;
            try
            {
                foreach (var slot in _profile.PartsSlots)
                {
                    if (slot == null || slot.IsClipShape || slot.IsPath)
                        continue;
                    string id = SpritePartIdUtility.Canonical(slot.SlotId);
                    if (selected != null && !selected.Contains(id))
                        continue;
                    int i = BlobSlotIndex(ref blob.Value, slot.SlotId);
                    if (i < 0 || i >= poses.Length)
                        continue;
                    var p = poses[i];
                    result[id] = new SpritePartsAuthoringOps.PoseValue
                    {
                        Position = new Vector2(p.Position.x, p.Position.y),
                        Rotation = p.Rotation,
                        Scale = new Vector2(p.Scale.x, p.Scale.y),
                        Shear = new Vector2(p.Shear.x, p.Shear.y),
                        Deform = p.Lattice.OffsetArray(),
                    };
                }
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
            return result;
        }
    }
}

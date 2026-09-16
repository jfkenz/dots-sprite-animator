using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>
    /// Import Frame Sequence to Part: pick a frame clip, target Parts clip/slot,
    /// start time (default playhead), optional duration extend.
    /// </summary>
    sealed class SpritePartsFrameSequencePopup : PopupWindowContent
    {
        readonly SpriteSheetToolWindow _host;
        readonly string _slotId;
        int _frameClipIndex;
        int _partsClipIndex;
        float _startTime;
        int _repeat = 1;
        bool _extendDuration = true;

        public SpritePartsFrameSequencePopup(SpriteSheetToolWindow host, string slotId)
        {
            _host = host;
            _slotId = slotId;
            _startTime = host.PartsPreviewTime;
            _partsClipIndex = Mathf.Max(0, host.PartsSelectedClipIndex);
        }

        public override Vector2 GetWindowSize() => new Vector2(360f, 290f);

        public override void OnGUI(Rect rect)
        {
            var profile = _host.EditingProfile;
            if (profile == null)
            {
                editorWindow.Close();
                return;
            }

            GUILayout.BeginArea(new Rect(8f, 8f, rect.width - 16f, rect.height - 16f));
            GUILayout.Label("IMPORT FRAME SEQUENCE TO PART", EditorStyles.boldLabel);
            GUILayout.Label(
                "Copies frame cells onto this slot's independent appearance channel. Existing pose keys are never touched, so this works on animated parts.",
                EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(6f);

            var frameNames = new List<string>();
            var frames = profile.Clips ?? new List<SpriteClipDef>();
            for (int i = 0; i < frames.Count; i++)
                frameNames.Add(frames[i] != null ? frames[i].Name : ("Clip " + i));
            if (frameNames.Count == 0)
                GUILayout.Label("No frame clips in this profile.", EditorStyles.miniLabel);
            else
                _frameClipIndex = EditorGUILayout.Popup("Frame Clip", Mathf.Clamp(_frameClipIndex, 0, frameNames.Count - 1), frameNames.ToArray());

            var partsNames = new List<string>();
            var parts = profile.PartsClips ?? new List<SpritePartsClipDef>();
            for (int i = 0; i < parts.Count; i++)
                partsNames.Add(parts[i] != null ? parts[i].Name : ("Parts " + i));
            if (partsNames.Count == 0)
                GUILayout.Label("No Parts clips in this profile.", EditorStyles.miniLabel);
            else
                _partsClipIndex = EditorGUILayout.Popup("Parts Clip", Mathf.Clamp(_partsClipIndex, 0, partsNames.Count - 1), partsNames.ToArray());

            GUILayout.Label("Slot: " + _slotId, EditorStyles.miniLabel);
            _startTime = EditorGUILayout.FloatField("Start Time (s)", _startTime);
            _repeat = Mathf.Clamp(EditorGUILayout.IntField("Repeat", _repeat), 1, 256);
            GUILayout.Label("Repeats stamp the same cell sequence back-to-back.", EditorStyles.miniLabel);
            _extendDuration = EditorGUILayout.ToggleLeft("Extend Parts clip duration if needed", _extendDuration);

            var plan = SpritePartsFrameSequenceImport.PlanImport(
                profile, _frameClipIndex, _partsClipIndex, _slotId, _startTime, _extendDuration, _repeat);
            if (plan.Ok)
                GUILayout.Label(
                    string.Format("{0} keys (x{1}), {2:F2}s -> {3:F2}s", plan.KeyCount, plan.RepeatCount, plan.StartTime, plan.EndTime),
                    EditorStyles.miniLabel);
            else if (!string.IsNullOrEmpty(plan.Reason))
                EditorGUILayout.HelpBox(plan.Reason, MessageType.Warning);

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!plan.Ok))
            {
                if (GUILayout.Button("Import", GUILayout.Height(24f)))
                {
                    _host.ImportFrameSequenceToPart(
                        _frameClipIndex, _partsClipIndex, _slotId, _startTime, _extendDuration, _repeat);
                    editorWindow.Close();
                    return;
                }
            }
            if (GUILayout.Button("Cancel"))
                editorWindow.Close();
            GUILayout.EndArea();
        }
    }
}

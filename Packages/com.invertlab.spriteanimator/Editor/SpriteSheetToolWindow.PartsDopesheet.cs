using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Dopesheet tools (Spine):
    //   Stepped        preview holds each key's pose until the next key (blocking)
    //   Range [ ]      play only between In and Out (preview only; the clip is unchanged)
    //   Scale time     stretch / squeeze the selected keys around the first one (or the playhead)
    //   Offset         each selected part's keys start N frames after the part above (overlap), wrapping on loops
    public sealed partial class SpriteSheetToolWindow
    {
        [SerializeField] bool _partsSteppedPreview;
        [SerializeField] bool _partsRangeOn;
        [SerializeField] float _partsRangeIn;
        [SerializeField] float _partsRangeOut = 1f;
        [SerializeField] float _partsKeyScalePercent = 100f;
        [SerializeField] bool _partsKeyScaleFromPlayhead;
        [SerializeField] int _partsKeyOffsetFrames = 2;

        bool PartsRangeValid(float duration)
            => _partsRangeOn && _partsRangeOut - _partsRangeIn > 1e-3f && _partsRangeIn < duration;

        void DrawPartsDopesheetToggles(Rect r)
        {
            var clip = CurrentPartsClip;
            if (clip == null)
                return;
            float x = r.x;
            bool stepped = GUI.Toggle(new Rect(x, r.y, 58f, r.height), _partsSteppedPreview,
                new GUIContent("Stepped", "Preview holds each key's pose until the next key (no in-betweens), to check the key poses"),
                EditorStyles.miniButton);
            if (stepped != _partsSteppedPreview)
            {
                _partsSteppedPreview = stepped;
                Repaint();
            }
            x += 62f;
            bool range = GUI.Toggle(new Rect(x, r.y, 48f, r.height), _partsRangeOn,
                new GUIContent("Range", "Play only between In and Out (preview only)"), EditorStyles.miniButtonLeft);
            if (GUI.Button(new Rect(x + 48f, r.y, 22f, r.height), new GUIContent("[", "Set In at the playhead"), EditorStyles.miniButtonMid))
            {
                _partsRangeIn = Mathf.Min(_partsPreviewTime, _partsRangeOut - 1e-3f);
                range = true;
            }
            if (GUI.Button(new Rect(x + 70f, r.y, 22f, r.height), new GUIContent("]", "Set Out at the playhead"), EditorStyles.miniButtonRight))
            {
                _partsRangeOut = Mathf.Max(_partsPreviewTime, _partsRangeIn + 1e-3f);
                range = true;
            }
            if (range != _partsRangeOn)
            {
                _partsRangeOn = range;
                if (range && _partsRangeOut <= _partsRangeIn)
                {
                    _partsRangeIn = 0f;
                    _partsRangeOut = Mathf.Max(1e-3f, clip.Duration);
                }
                Repaint();
            }
        }

        /// <summary>The loop range on the ruler: a band with In / Out lines.</summary>
        void DrawPartsRange(Rect ruler, float duration)
        {
            if (!PartsRangeValid(duration) || Event.current.type != EventType.Repaint)
                return;
            float a = Mathf.Lerp(ruler.x, ruler.xMax, Mathf.Clamp01(_partsRangeIn / duration));
            float b = Mathf.Lerp(ruler.x, ruler.xMax, Mathf.Clamp01(_partsRangeOut / duration));
            var band = new Color(1f, 0.85f, 0.3f, 0.12f);
            EditorGUI.DrawRect(new Rect(a, ruler.y, b - a, ruler.height), band);
            EditorGUI.DrawRect(new Rect(a - 1f, ruler.y, 2f, ruler.height), new Color(1f, 0.85f, 0.3f, 0.9f));
            EditorGUI.DrawRect(new Rect(b - 1f, ruler.y, 2f, ruler.height), new Color(1f, 0.85f, 0.3f, 0.9f));
        }

        /// <summary>Playback inside the range: past Out wraps back to In; before In jumps to In.</summary>
        void ApplyPartsRange(float duration)
        {
            if (!PartsRangeValid(duration))
                return;
            float inT = Mathf.Clamp(_partsRangeIn, 0f, duration);
            float outT = Mathf.Clamp(_partsRangeOut, inT + 1e-3f, duration);
            if (_partsPreviewTime < inT)
                _partsPreviewTime = inT;
            else if (_partsPreviewTime > outT)
                _partsPreviewTime = inT + (_partsPreviewTime - outT) % (outT - inT);
            _partsPlaying = true; // a range loops even on Once clips
        }

        /// <summary>KEY section: Scale and Offset for the selected keys.</summary>
        void DrawPartsKeyTimingTools(List<SpritePartsKeyDef> keys)
        {
            if (keys.Count < 2)
                return;
            GUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Scale", "Stretch (above 100%) or squeeze the selected keys' timing"), GUILayout.Width(40f));
            _partsKeyScalePercent = Mathf.Clamp(EditorGUILayout.FloatField(_partsKeyScalePercent, GUILayout.Width(44f)), 1f, 1000f);
            GUILayout.Label("%", GUILayout.Width(14f));
            _partsKeyScaleFromPlayhead = GUILayout.Toggle(_partsKeyScaleFromPlayhead,
                new GUIContent(_partsKeyScaleFromPlayhead ? "from playhead" : "from first key", "What stays in place"), EditorStyles.miniButton);
            if (GUILayout.Button("Apply", EditorStyles.miniButton, GUILayout.Width(44f)))
                ScaleSelectedPartsKeys(_partsKeyScalePercent / 100f);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Offset", "Each part's keys start this many frames after the part above (overlap). Loops wrap."),
                GUILayout.Width(40f));
            _partsKeyOffsetFrames = EditorGUILayout.IntField(_partsKeyOffsetFrames, GUILayout.Width(44f));
            GUILayout.Label("frames per part", _mutedStyle);
            if (GUILayout.Button("Apply", EditorStyles.miniButton, GUILayout.Width(44f)))
                OffsetSelectedPartsKeys(_partsKeyOffsetFrames);
            EditorGUILayout.EndHorizontal();
        }

        void ScaleSelectedPartsKeys(float factor)
        {
            if (_partsSelectedKeys.Count < 2)
            {
                _status = "Select two or more keys to scale their timing.";
                return;
            }
            float pivot = float.MaxValue;
            foreach (var k in _partsSelectedKeys)
                if (k != null) pivot = Mathf.Min(pivot, k.Time);
            if (_partsKeyScaleFromPlayhead)
                pivot = _partsPreviewTime;
            RecordPartsUndo("Scale Key Timing");
            var result = SpritePartsAuthoringOps.ScaleKeyTimes(_profile, _partsSelectedClip, _partsSelectedKeys, pivot, factor, _partsDisplayFps);
            PrunePartsKeySelection();
            SaveDirty();
            _status = result.Ok ? "Scaled " + result.Affected + " keys to " + Mathf.RoundToInt(factor * 100f) + "%." : result.Reason;
            Repaint();
        }

        void OffsetSelectedPartsKeys(int frames)
        {
            var order = new List<string>();
            foreach (var s in _profile.PartsSlots)
                if (s != null) order.Add(s.SlotId);
            RecordPartsUndo("Offset Keys");
            float step = frames / Mathf.Max(1f, _partsDisplayFps);
            var result = SpritePartsAuthoringOps.OffsetKeysByPart(_profile, _partsSelectedClip, _partsSelectedKeys, order, step, _partsDisplayFps);
            PrunePartsKeySelection();
            SaveDirty();
            _status = result.Ok ? "Offset " + result.Affected + " keys by " + frames + " frame" + (frames == 1 ? "" : "s") + " per part." : result.Reason;
            Repaint();
        }

        void AddPartsKeyTimingMenu(GenericMenu menu)
        {
            foreach (int p in new[] { 50, 75, 125, 150, 200 })
            {
                int percent = p;
                menu.AddItem(new GUIContent("Scale Time/" + percent + "%"), false, () => ScaleSelectedPartsKeys(percent / 100f));
            }
            foreach (int f in new[] { 1, 2, 3, 4, 6 })
            {
                int frames = f;
                menu.AddItem(new GUIContent("Offset Parts/" + frames + " frame" + (frames == 1 ? "" : "s")), false, () => OffsetSelectedPartsKeys(frames));
            }
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Breakdown (tween machine): drag the slider to key the selected parts at the playhead between their keys
    // before and after it. 0 = favor the previous pose, 1 = the next, the ends overshoot a little.
    public sealed partial class SpriteSheetToolWindow
    {
        [SerializeField] float _partsBreakdown = 0.5f;
        bool _partsBreakdownDragging;

        void DrawPartsBreakdown(Rect r)
        {
            if (CurrentPartsClip == null || _partsMode != SpritePartsStudioMode.Animate)
                return;
            GUI.Label(new Rect(r.x, r.y, 56f, r.height), new GUIContent("Breakdown",
                "Key the selected parts between their previous and next keys: left favors the previous pose, right the next"), _mutedStyle);
            var slider = new Rect(r.x + 60f, r.y, r.width - 100f, r.height);
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && slider.Contains(evt.mousePosition))
            {
                _partsBreakdownDragging = true;
                _partsPlaying = false;
                BeginPartsDragUndo("Breakdown");
            }
            float value = GUI.HorizontalSlider(slider, _partsBreakdown, -0.25f, 1.25f);
            GUI.Label(new Rect(slider.xMax + 4f, r.y, 36f, r.height), Mathf.RoundToInt(value * 100f) + "%", _mutedStyle);
            if (!Mathf.Approximately(value, _partsBreakdown))
            {
                _partsBreakdown = value;
                if (_partsBreakdownDragging)
                {
                    var result = SpritePartsAuthoringOps.KeyBreakdown(_profile, _partsSelectedClip, SelectedSlotIdsForMask(),
                        _partsPreviewTime, value);
                    _status = result.Ok
                        ? "Breakdown " + Mathf.RoundToInt(value * 100f) + "% keyed on " + result.Affected + " part" + (result.Affected == 1 ? "" : "s")
                        : result.Reason;
                    if (_asset != null)
                        EditorUtility.SetDirty(_asset);
                }
                Repaint();
            }
            if (_partsBreakdownDragging && evt.rawType == EventType.MouseUp)
            {
                _partsBreakdownDragging = false;
                EndPartsDragUndo();
                Repaint();
            }
        }
    }
}

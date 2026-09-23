using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // KEY section (Animate): what happens between this key and the next, and the key's extra channels.
    //   Ease         preset, or Bezier with a small curve editor (drag the two handles)
    //   Colour key   tint + alpha; colour keys blend with each other
    //   Draw order   the part's draw rank from this key on (held)
    // Edits the selected timeline keys, or the current part's key at the playhead.
    public sealed partial class SpriteSheetToolWindow
    {
        int _partsCurveHandle = -1; // 0 = first handle, 1 = second, while dragging

        static readonly Vector4 PartsCurveDefault = new Vector4(0.33f, 0f, 0.67f, 1f);

        /// <summary>Draw rank in the preview: a draw-order key when one is active, else the slot's own rank.</summary>
        int PreviewDrawRank(ref SpritePartsSetBlob set, int slotIndex, float time)
        {
            int keyed = SpritePartsSampler.SampleDrawOrder(ref set, PartsEvaluationClipIndex(), slotIndex, time);
            return keyed >= 0 ? keyed : set.Slots[slotIndex].DrawRank;
        }

        /// <summary>Preview tint: colour keys multiplied into <paramref name="tint"/>.</summary>
        Color PreviewTint(ref SpritePartsSetBlob set, int slotIndex, float time, Color tint)
        {
            if (!SpritePartsSampler.SampleColor(ref set, PartsEvaluationClipIndex(), slotIndex, time, out var c))
                return tint;
            return new Color(tint.r * c.x, tint.g * c.y, tint.b * c.z, tint.a * c.w);
        }

        /// <summary>The keys the KEY section edits: the selection, else this part's key at the playhead.</summary>
        List<SpritePartsKeyDef> KeyInspectorTargets(SpritePartSlotDef slot, out bool fromSelection)
        {
            var list = new List<SpritePartsKeyDef>();
            fromSelection = _partsSelectedKeys.Count > 0;
            if (fromSelection)
            {
                list.AddRange(_partsSelectedKeys);
                return list;
            }
            var clip = CurrentPartsClip;
            if (clip?.Tracks == null || slot == null)
                return list;
            string id = SpritePartIdUtility.Canonical(slot.SlotId);
            float halfFrame = 0.5f / Mathf.Max(1f, _partsDisplayFps);
            foreach (var track in clip.Tracks)
            {
                if (track?.Keys == null || track.Kind != SpritePartsTrackKind.Pose
                    || SpritePartIdUtility.Canonical(track.SlotId) != id)
                    continue;
                foreach (var key in track.Keys)
                {
                    if (key != null && Mathf.Abs(key.Time - _partsPreviewTime) <= halfFrame)
                        list.Add(key);
                }
            }
            return list;
        }

        void DrawPartsKeyInspector(SpritePartSlotDef slot, bool partLocked)
        {
            if (_partsMode != SpritePartsStudioMode.Animate || slot == null)
                return;
            var keys = KeyInspectorTargets(slot, out bool fromSelection);
            bool hasKey = keys.Count > 0;
            string summary = fromSelection ? keys.Count + " selected key" + (keys.Count == 1 ? "" : "s")
                : hasKey ? "Key at " + _partsPreviewTime.ToString("0.###") + "s"
                : _partsPlaying ? "playing" : "no key here";
            if (!PartsSection("KEY", summary))
                return;
            // One fixed layout whether or not the playhead sits on a key, so nothing below jumps while playing.
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(hasKey ? string.Empty : "No key on this part at the playhead.", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(hasKey || partLocked || _partsPlaying || CurrentPartsClip == null))
            {
                if (GUILayout.Button(new GUIContent("Key here", "Key the part's current pose at the playhead, then set ease / colour / draw order"),
                        EditorStyles.miniButton, GUILayout.Width(70f)))
                {
                    RecordPartsUndo("Key Here");
                    var pose = SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
                    SpritePartsAuthoringOps.ApplyPoseEdit(_profile, _partsMode, _partsSelectedClip, slot.SlotId,
                        _partsPreviewTime, pose, true, _partsDisplayFps);
                    SnapPartsPlayheadToFrame();
                    SaveDirty();
                }
            }
            EditorGUILayout.EndHorizontal();
            if (!hasKey)
                keys = new List<SpritePartsKeyDef> { PartsPlaceholderKey };
            var first = keys[0];
            using (new EditorGUI.DisabledScope(partLocked || !hasKey || _partsPlaying))
            {
                DrawPartsKeyChannelToggles(keys);
                if (fromSelection)
                    DrawPartsKeyTimingTools(keys);

                // Ease to the next key.
                var ease = SpriteEase.IsValidMode(first.EaseMode) ? (SpriteEaseMode)first.EaseMode : SpriteEaseMode.Linear;
                EditorGUI.BeginChangeCheck();
                ease = (SpriteEaseMode)EditorGUILayout.EnumPopup(new GUIContent("Ease", "How the part moves from this key to the next. Bezier = your own curve."), ease);
                if (EditorGUI.EndChangeCheck())
                    EditKeys(keys, "Key Ease", k => k.EaseMode = (byte)ease);
                if (first.EaseMode == (byte)SpriteEaseMode.Bezier && hasKey)
                    DrawPartsCurveEditor(keys);

                // Colour key.
                GUILayout.Space(4f);
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                bool hasColor = EditorGUILayout.ToggleLeft(new GUIContent("Colour", "Colour key: tint the part, alpha fades it. Blends with the next colour key."),
                    first.HasColor, GUILayout.Width(70f));
                Color color;
                using (new EditorGUI.DisabledScope(!first.HasColor))
                    color = EditorGUILayout.ColorField(GUIContent.none, first.HasColor ? first.Color : Color.white, true, true, false);
                if (EditorGUI.EndChangeCheck())
                    EditKeys(keys, "Key Colour", k =>
                    {
                        k.HasColor = hasColor;
                        k.Color = hasColor ? color : Color.white;
                    });
                EditorGUILayout.EndHorizontal();

                // Draw-order key.
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                bool hasOrder = EditorGUILayout.ToggleLeft(new GUIContent("Draw order", "From this key on, draw the part at this rank (higher = in front). Held until the next draw-order key."),
                    first.HasDrawOrder, GUILayout.Width(82f));
                int rank;
                using (new EditorGUI.DisabledScope(!first.HasDrawOrder))
                    rank = Mathf.Max(0, EditorGUILayout.IntField(first.HasDrawOrder ? first.DrawOrder : slot.DrawRank, GUILayout.Width(40f)));
                bool front = GUILayout.Button(new GUIContent("Front", "In front of every part"), GUILayout.Width(46f));
                bool back = GUILayout.Button(new GUIContent("Back", "Behind every part"), GUILayout.Width(40f));
                if (EditorGUI.EndChangeCheck() || front || back)
                {
                    if (front || back)
                    {
                        hasOrder = true;
                        rank = front ? MaxDrawRank() + 1 : 0;
                    }
                    EditKeys(keys, "Key Draw Order", k =>
                    {
                        k.HasDrawOrder = hasOrder;
                        k.DrawOrder = rank;
                    });
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>Shown (greyed) when there is no key, so the section keeps its size.</summary>
        static readonly SpritePartsKeyDef PartsPlaceholderKey = new SpritePartsKeyDef();

        int MaxDrawRank()
        {
            int max = 0;
            if (_profile?.PartsSlots != null)
                foreach (var s in _profile.PartsSlots)
                    if (s != null)
                        max = Mathf.Max(max, s.DrawRank);
            return max;
        }

        void EditKeys(List<SpritePartsKeyDef> keys, string label, System.Action<SpritePartsKeyDef> edit)
        {
            RecordPartsUndo(label);
            foreach (var k in keys)
                if (k != null)
                    edit(k);
            SaveDirty();
            Repaint();
        }

        static Vector4 KeyCurve(SpritePartsKeyDef key)
            => key.Curve == Vector4.zero ? PartsCurveDefault : key.Curve;

        /// <summary>Bezier curve editor: time left to right, value bottom to top, two draggable handles.</summary>
        void DrawPartsCurveEditor(List<SpritePartsKeyDef> keys)
        {
            var curve = KeyCurve(keys[0]);
            Rect area = GUILayoutUtility.GetRect(10f, 120f, GUILayout.ExpandWidth(true));
            area = new Rect(area.x + 4f, area.y + 14f, area.width - 8f, area.height - 28f); // room for overshoot
            var evt = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive, area);
            Vector2 ToGui(float x, float y) => new Vector2(Mathf.Lerp(area.xMin, area.xMax, x), Mathf.Lerp(area.yMax, area.yMin, y));
            Vector2 FromGui(Vector2 p) => new Vector2(Mathf.InverseLerp(area.xMin, area.xMax, p.x),
                (area.yMax - p.y) / Mathf.Max(1f, area.height));
            Vector2 h1 = ToGui(curve.x, curve.y), h2 = ToGui(curve.z, curve.w);

            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown when evt.button == 0:
                {
                    float d1 = Vector2.Distance(evt.mousePosition, h1), d2 = Vector2.Distance(evt.mousePosition, h2);
                    if (Mathf.Min(d1, d2) <= 10f)
                    {
                        _partsCurveHandle = d1 <= d2 ? 0 : 1;
                        GUIUtility.hotControl = id;
                        RecordPartsUndo("Key Curve");
                        evt.Use();
                    }
                    break;
                }
                case EventType.MouseDrag when GUIUtility.hotControl == id && _partsCurveHandle >= 0:
                {
                    Vector2 v = FromGui(evt.mousePosition);
                    v.x = Mathf.Clamp01(v.x);
                    v.y = Mathf.Clamp(v.y, -0.5f, 1.5f);
                    if (_partsCurveHandle == 0) { curve.x = v.x; curve.y = v.y; }
                    else { curve.z = v.x; curve.w = v.y; }
                    foreach (var k in keys)
                        if (k != null)
                            k.Curve = curve;
                    if (_asset != null)
                        EditorUtility.SetDirty(_asset);
                    evt.Use();
                    Repaint();
                    break;
                }
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    _partsCurveHandle = -1;
                    SaveDirty();
                    evt.Use();
                    break;
                case EventType.Repaint:
                {
                    EditorGUI.DrawRect(new Rect(area.x, area.y - 12f, area.width, area.height + 24f), new Color(0.07f, 0.08f, 0.1f, 1f));
                    Handles.BeginGUI();
                    Handles.color = new Color(1f, 1f, 1f, 0.12f);
                    Handles.DrawAAPolyLine(1f, ToGui(0f, 0f), ToGui(1f, 0f));
                    Handles.DrawAAPolyLine(1f, ToGui(0f, 1f), ToGui(1f, 1f));
                    Handles.DrawAAPolyLine(1f, ToGui(0f, 0f), ToGui(1f, 1f));
                    var pts = new Vector3[33];
                    for (int i = 0; i <= 32; i++)
                    {
                        float t = i / 32f;
                        pts[i] = ToGui(t, SpriteEase.EvaluateBezier(new float4(curve.x, curve.y, curve.z, curve.w), t));
                    }
                    Handles.color = new Color(0.35f, 0.85f, 1f, 1f);
                    Handles.DrawAAPolyLine(2.5f, pts);
                    Handles.color = new Color(1f, 0.6f, 0.2f, 0.9f);
                    Handles.DrawAAPolyLine(1.5f, ToGui(0f, 0f), h1);
                    Handles.DrawAAPolyLine(1.5f, ToGui(1f, 1f), h2);
                    Handles.DrawSolidDisc(h1, Vector3.forward, 5f);
                    Handles.DrawSolidDisc(h2, Vector3.forward, 5f);
                    Handles.EndGUI();
                    break;
                }
            }

            // Presets and exact values.
            EditorGUILayout.BeginHorizontal();
            Vector4? preset = null;
            if (GUILayout.Button(new GUIContent("Linear"), EditorStyles.miniButtonLeft)) preset = new Vector4(1f / 3f, 1f / 3f, 2f / 3f, 2f / 3f);
            if (GUILayout.Button(new GUIContent("In"), EditorStyles.miniButtonMid)) preset = new Vector4(0.42f, 0f, 1f, 1f);
            if (GUILayout.Button(new GUIContent("Out"), EditorStyles.miniButtonMid)) preset = new Vector4(0f, 0f, 0.58f, 1f);
            if (GUILayout.Button(new GUIContent("In-Out"), EditorStyles.miniButtonMid)) preset = new Vector4(0.42f, 0f, 0.58f, 1f);
            if (GUILayout.Button(new GUIContent("Overshoot", "Goes past the next key a little, then settles"), EditorStyles.miniButtonRight)) preset = new Vector4(0.34f, 1.56f, 0.64f, 1f);
            EditorGUILayout.EndHorizontal();
            EditorGUI.BeginChangeCheck();
            var typed = EditorGUILayout.Vector4Field(GUIContent.none, curve);
            if (EditorGUI.EndChangeCheck())
                preset = new Vector4(Mathf.Clamp01(typed.x), typed.y, Mathf.Clamp01(typed.z), typed.w);
            if (preset.HasValue)
            {
                var value = preset.Value;
                EditKeys(keys, "Key Curve", k => k.Curve = value);
            }
        }
    }
}

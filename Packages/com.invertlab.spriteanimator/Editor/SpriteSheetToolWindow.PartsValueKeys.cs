using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Keyed IK Mix / bend, jiggle Mix and parameter values (Spine constraint timelines):
    //   ◆ button   Animate mode: key the value at the playhead (◆ = a key here, ◇ = keyed elsewhere in the clip)
    //   editing    once the clip keys a value, changing it keys the playhead; otherwise it changes the setup value
    public sealed partial class SpriteSheetToolWindow
    {
        static GUIStyle s_valueKeyStyle;

        /// <summary>The clip that value keys go into: the open clip in Animate mode, else none (setup edits).</summary>
        SpritePartsClipDef ValueKeyClip => _partsMode == SpritePartsStudioMode.Animate ? CurrentPartsClip : null;

        /// <summary>The value shown: the clip's keyed value at the playhead, else <paramref name="setup"/>.</summary>
        bool TryKeyedValue(SpritePartsValueKind kind, string target, float setup, out float shown)
        {
            shown = setup;
            var clip = ValueKeyClip;
            return clip != null && SpritePartsAuthoringOps.TrySampleValue(clip, kind, target, _partsPreviewTime, out shown);
        }

        /// <summary>The key button for a value (Animate mode only). Clicking adds a key at the playhead or removes the one there.</summary>
        void ValueKeyButton(SpritePartsValueKind kind, string target, float shown, string what)
        {
            var clip = ValueKeyClip;
            if (clip == null)
                return;
            s_valueKeyStyle ??= new GUIStyle(EditorStyles.miniButton) { fixedWidth = 22f, padding = new RectOffset(0, 0, 0, 1) };
            bool here = SpritePartsAuthoringOps.ValueKeyAt(clip, kind, target, _partsPreviewTime) != null;
            bool tracked = SpritePartsAuthoringOps.FindValueTrack(clip, kind, target) != null;
            var old = GUI.contentColor;
            GUI.contentColor = here ? new Color(1f, 0.82f, 0.3f) : tracked ? new Color(0.95f, 0.75f, 0.35f, 0.8f) : new Color(0.7f, 0.7f, 0.7f, 0.8f);
            bool click = GUILayout.Button(new GUIContent(here ? "◆" : "◇", here
                ? "Remove the " + what + " key at the playhead"
                : "Key " + what + " at the playhead in '" + clip.Name + "' (then changes key it)"), s_valueKeyStyle);
            GUI.contentColor = old;
            if (!click)
                return;
            RecordPartsUndo(here ? "Remove " + what + " Key" : "Key " + what);
            if (here)
                SpritePartsAuthoringOps.RemoveValueKey(clip, kind, target, _partsPreviewTime);
            else
                SpritePartsAuthoringOps.SetValueKey(clip, kind, target, _partsPreviewTime, shown);
            SaveDirty();
        }

        static string ValueKindLabel(SpritePartsValueKind kind) => kind switch
        {
            SpritePartsValueKind.IkMix => "IK Mix",
            SpritePartsValueKind.IkBend => "IK Bend",
            SpritePartsValueKind.JiggleMix => "Jiggle",
            _ => "Param",
        };

        static Color ValueKindColor(SpritePartsValueKind kind) => kind switch
        {
            SpritePartsValueKind.IkMix => new Color(1f, 0.45f, 0.75f),
            SpritePartsValueKind.IkBend => new Color(0.85f, 0.4f, 1f),
            SpritePartsValueKind.JiggleMix => new Color(0.4f, 0.9f, 0.75f),
            _ => new Color(0.45f, 0.75f, 1f),
        };

        /// <summary>Timeline rows under the parts: one per keyed value, its keys as small diamonds.</summary>
        void DrawPartsValueRows(Rect rect, SpritePartsClipDef clip, float duration, float labelW, float rowH, float top)
        {
            if (clip?.ValueTracks == null || Event.current.type != EventType.Repaint)
                return;
            for (int v = 0; v < clip.ValueTracks.Count; v++)
            {
                var track = clip.ValueTracks[v];
                float rowY = top + v * rowH;
                if (track == null || rowY > rect.yMax - rowH)
                    continue;
                var color = ValueKindColor(track.Kind);
                GUI.Label(new Rect(rect.x + 2f, rowY, labelW - 6f, rowH),
                    new GUIContent(ValueKindLabel(track.Kind) + " " + track.Target, ValueKindLabel(track.Kind) + " of '" + track.Target + "'"),
                    _mutedStyle);
                var trackRect = new Rect(rect.x + labelW, rowY + 2f, rect.width - labelW, rowH - 4f);
                EditorGUI.DrawRect(trackRect, new Color(0.1f, 0.11f, 0.14f));
                if (track.Keys == null)
                    continue;
                Handles.BeginGUI();
                foreach (var key in track.Keys)
                {
                    if (key == null)
                        continue;
                    float kx = Mathf.Lerp(trackRect.x, trackRect.xMax, Mathf.Clamp01(key.Time / duration));
                    float cy = rowY + rowH * 0.5f;
                    bool here = Mathf.Abs(key.Time - _partsPreviewTime) <= 1e-4f;
                    Handles.color = here ? new Color(1f, 0.85f, 0.2f) : color;
                    Handles.DrawAAConvexPolygon(new Vector3(kx, cy - 5f), new Vector3(kx + 5f, cy), new Vector3(kx, cy + 5f), new Vector3(kx - 5f, cy));
                }
                Handles.EndGUI();
            }
        }

        /// <summary>Clicks on value-row keys: left jumps the playhead to the key, right opens Delete / ease. True when used.</summary>
        bool HandlePartsValueRowInput(Rect rect, SpritePartsClipDef clip, float duration, float labelW, float rowH, float top)
        {
            var evt = Event.current;
            if (evt.type != EventType.MouseDown || clip?.ValueTracks == null || clip.ValueTracks.Count == 0)
                return false;
            int v = Mathf.FloorToInt((evt.mousePosition.y - top) / rowH);
            if (v < 0 || v >= clip.ValueTracks.Count || evt.mousePosition.x < rect.x + labelW)
                return false;
            var track = clip.ValueTracks[v];
            if (track?.Keys == null)
                return false;
            float width = rect.width - labelW;
            SpritePartsValueKeyDef hit = null;
            float best = 7f;
            foreach (var key in track.Keys)
            {
                if (key == null)
                    continue;
                float kx = rect.x + labelW + Mathf.Clamp01(key.Time / duration) * width;
                float d = Mathf.Abs(kx - evt.mousePosition.x);
                if (d <= best)
                {
                    best = d;
                    hit = key;
                }
            }
            if (hit == null)
                return false;
            if (evt.button == 1)
                ShowValueKeyMenu(clip, track, hit);
            else
            {
                _partsPlaying = false;
                _partsPreviewTime = hit.Time;
            }
            evt.Use();
            Repaint();
            return true;
        }

        void ShowValueKeyMenu(SpritePartsClipDef clip, SpritePartsValueTrackDef track, SpritePartsValueKeyDef key)
        {
            var menu = new GenericMenu();
            menu.AddDisabledItem(new GUIContent(ValueKindLabel(track.Kind) + " " + track.Target + " = " + key.Value.ToString("0.###")
                                                + " at " + key.Time.ToString("0.###") + "s"));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Delete Key"), false, () =>
            {
                RecordPartsUndo("Delete Value Key");
                SpritePartsAuthoringOps.RemoveValueKey(clip, track.Kind, track.Target, key.Time);
                SaveDirty();
                Repaint();
            });
            foreach (var mode in new[] { SpriteEaseMode.Linear, SpriteEaseMode.Step, SpriteEaseMode.EaseInOut, SpriteEaseMode.EaseIn, SpriteEaseMode.EaseOut })
            {
                var m = mode;
                menu.AddItem(new GUIContent("Ease/" + ObjectNames.NicifyVariableName(m.ToString())), key.EaseMode == (byte)m, () =>
                {
                    RecordPartsUndo("Value Key Ease");
                    key.EaseMode = (byte)m;
                    SaveDirty();
                    Repaint();
                });
            }
            menu.ShowAsContext();
        }

        /// <summary>Writes an edited value: a key at the playhead when the open clip keys it, else false (edit the setup).</summary>
        bool KeyEditedValue(SpritePartsValueKind kind, string target, float value, string what)
        {
            var clip = ValueKeyClip;
            if (clip == null || SpritePartsAuthoringOps.FindValueTrack(clip, kind, target) == null)
                return false;
            RecordPartsUndo("Key " + what);
            SpritePartsAuthoringOps.SetValueKey(clip, kind, target, _partsPreviewTime, value);
            SaveDirty();
            return true;
        }
    }
}

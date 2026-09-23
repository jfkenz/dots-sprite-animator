using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Separate key channels (Spine timelines): a key holds Move, Rotate, Scale and/or Deform, and each channel blends
    // only with keys that hold it, so a rotation can lag behind a move (overlap, follow-through).
    //   editing        only the channels an edit changes are keyed
    //   timeline       keys are coloured by channel; the filter shows one channel, and then dragging a key moves just
    //                  that channel's timing (split off the key) and Delete removes just that channel
    //   KEY section    toggles to add / remove a channel on the selected keys
    public sealed partial class SpriteSheetToolWindow
    {
        [SerializeField] SpritePartsKeyChannel _partsKeyChannelFilter = SpritePartsKeyChannel.All;

        static readonly Color ChannelMoveColor = new Color(0.35f, 0.65f, 1f);
        static readonly Color ChannelRotateColor = new Color(0.35f, 0.9f, 0.4f);
        static readonly Color ChannelScaleColor = new Color(1f, 0.6f, 0.2f);
        static readonly Color ChannelDeformColor = new Color(0.75f, 0.5f, 1f);

        static readonly SpritePartsKeyChannel[] PartsChannelList =
        {
            SpritePartsKeyChannel.Position, SpritePartsKeyChannel.Rotation, SpritePartsKeyChannel.Scale, SpritePartsKeyChannel.Deform,
        };

        static string PartsChannelName(SpritePartsKeyChannel c) => c switch
        {
            SpritePartsKeyChannel.Position => "Move",
            SpritePartsKeyChannel.Rotation => "Rotate",
            SpritePartsKeyChannel.Scale => "Scale",
            SpritePartsKeyChannel.Deform => "Deform",
            _ => "All",
        };

        static Color PartsChannelColor(SpritePartsKeyChannel c) => c switch
        {
            SpritePartsKeyChannel.Position => ChannelMoveColor,
            SpritePartsKeyChannel.Rotation => ChannelRotateColor,
            SpritePartsKeyChannel.Scale => ChannelScaleColor,
            SpritePartsKeyChannel.Deform => ChannelDeformColor,
            _ => new Color(0.92f, 0.92f, 0.95f),
        };

        /// <summary>A key's diamond: its channel's colour, white for several, pink for colour-only, grey for order-only.</summary>
        Color PartsKeyColor(SpritePartsKeyDef key)
        {
            var shown = _partsKeyChannelFilter == SpritePartsKeyChannel.All ? key.Channels : key.Channels & _partsKeyChannelFilter;
            foreach (var c in PartsChannelList)
                if (shown == c)
                    return PartsChannelColor(c);
            if (shown != SpritePartsKeyChannel.None)
                return PartsChannelColor(SpritePartsKeyChannel.All);
            if (key.HasColor)
                return new Color(1f, 0.5f, 0.75f);
            return new Color(0.6f, 0.62f, 0.68f);
        }

        bool PartsKeyVisible(SpritePartsKeyDef key)
            => key != null && (_partsKeyChannelFilter == SpritePartsKeyChannel.All || (key.Channels & _partsKeyChannelFilter) != 0);

        void DrawPartsChannelFilter(Rect r)
        {
            if (CurrentPartsClip == null)
                return;
            GUI.Label(new Rect(r.x, r.y, 44f, r.height), new GUIContent("Show", "Show one channel's keys; dragging then moves only that channel"), _mutedStyle);
            float x = r.x + 40f;
            var options = new[] { SpritePartsKeyChannel.All, SpritePartsKeyChannel.Position, SpritePartsKeyChannel.Rotation,
                SpritePartsKeyChannel.Scale, SpritePartsKeyChannel.Deform };
            foreach (var c in options)
            {
                float w = c == SpritePartsKeyChannel.All ? 30f : 52f;
                var cell = new Rect(x, r.y, w, r.height);
                bool on = _partsKeyChannelFilter == c;
                var prev = GUI.backgroundColor;
                if (c != SpritePartsKeyChannel.All)
                    GUI.backgroundColor = Color.Lerp(Color.white, PartsChannelColor(c), on ? 0.9f : 0.35f);
                bool pressed = GUI.Toggle(cell, on, new GUIContent(PartsChannelName(c),
                    c == SpritePartsKeyChannel.All ? "Every key" : "Only " + PartsChannelName(c) + " keys: drag moves that channel alone, Delete removes it"),
                    EditorStyles.miniButton);
                GUI.backgroundColor = prev;
                if (pressed && !on)
                {
                    _partsKeyChannelFilter = c;
                    Repaint();
                }
                x += w + 2f;
            }
        }

        /// <summary>With a filter on, the dragged keys give up that channel to new keys, which are what moves.</summary>
        void SplitDraggedKeysForFilter()
        {
            if (_partsKeyChannelFilter == SpritePartsKeyChannel.All || _partsKeyDragKeys.Count == 0)
                return;
            var originals = new List<SpritePartsKeyDef>(_partsKeyDragKeys);
            var moving = SpritePartsAuthoringOps.SplitKeyChannels(_profile, _partsSelectedClip, originals, _partsKeyChannelFilter);
            _partsKeyDragKeys.Clear();
            _partsKeyDragStartTimes.Clear();
            foreach (var k in originals)
                _partsSelectedKeys.Remove(k);
            foreach (var k in moving)
            {
                _partsKeyDragKeys.Add(k);
                _partsKeyDragStartTimes.Add(k.Time);
                _partsSelectedKeys.Add(k);
            }
        }

        /// <summary>Drops selected keys that no longer exist (merged or deleted).</summary>
        void PrunePartsKeySelection()
        {
            var clip = CurrentPartsClip;
            var alive = new HashSet<SpritePartsKeyDef>();
            if (clip?.Tracks != null)
                foreach (var t in clip.Tracks)
                    if (t?.Keys != null)
                        foreach (var k in t.Keys)
                            alive.Add(k);
            _partsSelectedKeys.RemoveWhere(k => !alive.Contains(k));
            _partsKeyDragKeys.RemoveAll(k => !alive.Contains(k));
        }

        /// <summary>The channels <paramref name="pose"/> changes on the part at the playhead.</summary>
        SpritePartsKeyChannel ChangedPartsChannels(string slotId, SpritePartsAuthoringOps.PoseEdit pose)
        {
            var before = SampleLocalPoseForSlot(slotId, _partsPreviewTime);
            var changed = SpritePartsKeyChannel.None;
            if ((before.Position - pose.Position).sqrMagnitude > 1e-12f)
                changed |= SpritePartsKeyChannel.Position;
            if (Mathf.Abs(before.Rotation - pose.Rotation) > 1e-5f)
                changed |= SpritePartsKeyChannel.Rotation;
            if ((before.Scale - pose.Scale).sqrMagnitude > 1e-12f)
                changed |= SpritePartsKeyChannel.Scale;
            var a = before.Lattice;
            var b = pose.Lattice;
            if (a.PointCount != b.PointCount)
                changed |= SpritePartsKeyChannel.Deform;
            else
            {
                for (int i = 0; i < a.PointCount; i++)
                {
                    if (Unity.Mathematics.math.distancesq(a.GetPoint(i), b.GetPoint(i)) > 1e-12f)
                    {
                        changed |= SpritePartsKeyChannel.Deform;
                        break;
                    }
                }
            }
            return changed;
        }

        /// <summary>KEY section: which channels the keys hold; click to add (at the current value) or remove one.</summary>
        void DrawPartsKeyChannelToggles(List<SpritePartsKeyDef> keys)
        {
            var clip = CurrentPartsClip;
            if (clip?.Tracks == null || keys.Count == 0)
                return;
            var slotOf = new Dictionary<SpritePartsKeyDef, string>();
            foreach (var t in clip.Tracks)
                if (t?.Keys != null && t.Kind == SpritePartsTrackKind.Pose)
                    foreach (var k in t.Keys)
                        if (k != null) slotOf[k] = t.SlotId;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Holds", "The channels these keys hold. Each channel blends only with keys that hold it."),
                GUILayout.Width(40f));
            foreach (var c in PartsChannelList)
            {
                bool all = keys.TrueForAll(k => (k.Channels & c) != 0);
                bool any = keys.Exists(k => (k.Channels & c) != 0);
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = Color.Lerp(Color.white, PartsChannelColor(c), all ? 0.9f : any ? 0.5f : 0.15f);
                bool next = GUILayout.Toggle(all, new GUIContent(PartsChannelName(c) + (any && !all ? "~" : ""),
                    all ? "Remove " + PartsChannelName(c) + " from these keys" : "Key " + PartsChannelName(c) + " here at its current value"),
                    EditorStyles.miniButton);
                GUI.backgroundColor = prev;
                if (next == all)
                    continue;
                RecordPartsUndo(next ? "Key " + PartsChannelName(c) : "Unkey " + PartsChannelName(c));
                if (next)
                {
                    foreach (var k in keys)
                    {
                        if ((k.Channels & c) != 0 || !slotOf.TryGetValue(k, out string sid))
                            continue;
                        var value = SampleLocalPoseForSlot(sid, k.Time); // what shows there now
                        if (c == SpritePartsKeyChannel.Position) k.Position = value.Position;
                        if (c == SpritePartsKeyChannel.Rotation) k.Rotation = value.Rotation;
                        if (c == SpritePartsKeyChannel.Scale) k.Scale = value.Scale;
                        if (c == SpritePartsKeyChannel.Deform) k.Deform = value.Lattice.OffsetArray();
                        k.Channels |= c;
                    }
                }
                else
                    SpritePartsAuthoringOps.RemoveKeyChannels(_profile, _partsSelectedClip, new HashSet<SpritePartsKeyDef>(keys), c);
                PrunePartsKeySelection();
                SaveDirty();
                Repaint();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}

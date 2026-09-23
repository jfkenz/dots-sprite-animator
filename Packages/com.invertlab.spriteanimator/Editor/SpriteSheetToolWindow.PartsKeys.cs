using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    public sealed partial class SpriteSheetToolWindow
    {
        readonly HashSet<SpritePartsKeyDef> _partsSelectedKeys = new();
        readonly List<SpritePartsKeyDef> _partsKeyDragKeys = new();
        readonly List<float> _partsKeyDragStartTimes = new();
        readonly List<(string slotId, float relativeTime, SpritePartsKeyDef template)> _partsKeyClipboard =
            new();
        readonly HashSet<SpritePartsKeyDef> _partsKeyMarqueeBaseline = new();

        bool _partsKeyDragging;
        bool _partsKeyDragUndoRecorded;
        float _partsKeyDragStartX;
        float _partsKeyDragTrackWidth = 1f;
        float _partsKeyDragDuration = 1f;
        int _partsKeyHotControl;
        bool _partsKeyMarqueeActive;
        bool _partsKeyMarqueeMoved;
        int _partsKeyMarqueeHotControl;
        Vector2 _partsKeyMarqueeStart;
        Rect _partsKeyMarqueeRect;
        SelectionOp _partsKeyMarqueeOp = SelectionOp.Replace;

        void ClearPartsKeySelection()
        {
            _partsSelectedKeys.Clear();
            _partsKeyDragging = false;
            _partsKeyDragKeys.Clear();
            _partsKeyDragStartTimes.Clear();
            EndPartsKeyMarquee();
        }

        void HandleActivePartsKeyDrag(int controlId)
        {
            if (!_partsKeyDragging || _studioTab != StudioTab.Parts)
                return;
            var evt = Event.current;
            EventType raw = evt.rawType;
            if (raw != EventType.MouseDrag && raw != EventType.MouseUp &&
                raw != EventType.MouseLeaveWindow)
                return;

            int id = _partsKeyHotControl != 0 ? _partsKeyHotControl : controlId;
            if (GUIUtility.hotControl != id)
                GUIUtility.hotControl = id;
            _partsKeyHotControl = id;

            if (raw == EventType.MouseDrag)
            {
                if (!_partsKeyDragUndoRecorded)
                {
                    BeginPartsDragUndo(_partsKeyChannelFilter == SpritePartsKeyChannel.All ? "Move Parts Keys" : "Move " + PartsChannelName(_partsKeyChannelFilter) + " Keys");
                    _partsKeyDragUndoRecorded = true;
                    SplitDraggedKeysForFilter();
                }
                float deltaSec = (evt.mousePosition.x - _partsKeyDragStartX) /
                                 Mathf.Max(1f, _partsKeyDragTrackWidth) * _partsKeyDragDuration;
                bool snap = !evt.shift;
                SpritePartsAuthoringOps.MoveKeys(
                    _profile, _partsSelectedClip, _partsKeyDragKeys, _partsKeyDragStartTimes,
                    deltaSec, _partsDisplayFps, snap, merge: false);
                if (_partsKeyDragKeys.Count > 0)
                    _partsPreviewTime = _partsKeyDragKeys[0].Time;
                evt.Use();
                Repaint();
                return;
            }

            if (_partsKeyDragUndoRecorded)
            {
                // Keys that landed on another key's time merge now (not while passing over them).
                SpritePartsAuthoringOps.MergeKeyCollisions(_profile, _partsSelectedClip, _partsKeyDragKeys);
                PrunePartsKeySelection();
                EndPartsDragUndo();
            }
            GUIUtility.hotControl = 0;
            _partsKeyHotControl = 0;
            _partsKeyDragging = false;
            _partsKeyDragUndoRecorded = false;
            evt.Use();
            Repaint();
        }

        bool TryHitPartsKey(
            Rect tracksRect, SpritePartsClipDef clip, Vector2 mouse,
            out SpritePartsKeyDef key, out string slotId, out Rect trackLane, out float duration)
        {
            key = null;
            slotId = null;
            trackLane = default;
            duration = 1f;
            if (_profile?.PartsSlots == null || clip == null) return false;
            duration = Mathf.Max(1e-3f, clip.Duration);
            const float labelW = 90f;
            const float rowH = 18f;
            const float rulerH = 26f;
            float tracksTop = tracksRect.y + rulerH;
            float hitPad = 9f;

            for (int i = 0; i < _profile.PartsSlots.Count; i++)
            {
                var slot = _profile.PartsSlots[i];
                if (slot == null) continue;
                float rowY = tracksTop + i * rowH;
                if (rowY > tracksRect.yMax - rowH) break;
                var lane = new Rect(tracksRect.x + labelW, rowY, tracksRect.width - labelW, rowH);
                var track = SpritePartsAuthoringOps.FindTrack(clip, slot.SlotId);
                if (track?.Keys == null) continue;
                for (int k = 0; k < track.Keys.Count; k++)
                {
                    var cand = track.Keys[k];
                    if (cand == null || !PartsKeyVisible(cand)) continue;
                    float u = cand.Time / duration;
                    float kx = Mathf.Lerp(lane.x, lane.xMax, u);
                    var hit = new Rect(kx - hitPad, rowY, hitPad * 2f, rowH);
                    if (!hit.Contains(mouse)) continue;
                    key = cand;
                    slotId = slot.SlotId;
                    trackLane = lane;
                    return true;
                }
            }
            return false;
        }

        void BeginPartsKeyDrag(int controlId, float trackWidth, float duration)
        {
            if (ImportPreviewActive)
            {
                PauseForImportPreview("key edit");
                return;
            }
            _partsKeyDragging = true;
            _partsKeyDragUndoRecorded = false;
            _partsKeyHotControl = controlId;
            GUIUtility.hotControl = controlId;
            GUIUtility.keyboardControl = 0;
            _partsKeyDragStartX = Event.current.mousePosition.x;
            _partsKeyDragTrackWidth = Mathf.Max(1f, trackWidth);
            _partsKeyDragDuration = Mathf.Max(1e-3f, duration);
            _partsKeyDragKeys.Clear();
            _partsKeyDragStartTimes.Clear();
            foreach (var k in _partsSelectedKeys)
            {
                if (k == null) continue;
                _partsKeyDragKeys.Add(k);
                _partsKeyDragStartTimes.Add(k.Time);
            }
            _partsPlaying = false;
        }

        void DeleteSelectedPartsKeys()
        {
            if (ImportPreviewActive)
            {
                PauseForImportPreview("key edit");
                return;
            }
            if (_partsSelectedKeys.Count == 0) return;
            if (_partsKeyChannelFilter != SpritePartsKeyChannel.All)
            {
                RecordPartsUndo("Delete " + PartsChannelName(_partsKeyChannelFilter) + " Keys");
                int n = SpritePartsAuthoringOps.RemoveKeyChannels(_profile, _partsSelectedClip, _partsSelectedKeys, _partsKeyChannelFilter);
                ClearPartsKeySelection();
                SaveDirty();
                _status = "Removed " + PartsChannelName(_partsKeyChannelFilter) + " from " + n + " key" + (n == 1 ? "" : "s") + ".";
                Repaint();
                return;
            }
            RecordPartsUndo("Delete Parts Keys");
            var result = SpritePartsAuthoringOps.DeleteKeys(
                _profile, _partsSelectedClip, _partsSelectedKeys);
            ClearPartsKeySelection();
            SaveDirty();
            _status = result.Ok
                ? $"Deleted {result.Affected} Parts key{(result.Affected == 1 ? "" : "s")}"
                : (result.Reason ?? "Delete keys failed");
            Repaint();
        }

        void InsertPartsKeyAtTime(string slotId, float time)
        {
            if (ImportPreviewActive)
            {
                PauseForImportPreview("key edit");
                return;
            }
            if (_profile == null || CurrentPartsClip == null || string.IsNullOrEmpty(slotId))
                return;
            var pose = SampleLocalPoseForSlot(slotId, time);
            RecordPartsUndo("Add Parts Key");
            var result = SpritePartsAuthoringOps.WriteKeyPose(
                _profile, _partsSelectedClip, slotId, time, pose, _partsDisplayFps);
            SaveDirty();
            var track = SpritePartsAuthoringOps.FindTrack(CurrentPartsClip, slotId);
            var key = SpritePartsAuthoringOps.FindKeyAtTime(track, time, 1e-3f);
            ClearPartsKeySelection();
            if (key != null)
                _partsSelectedKeys.Add(key);
            _partsPreviewTime = key != null ? key.Time : time; // the needle on the key's frame
            _status = result.WroteKey ? $"Keyed at {time:F3}s" : (result.Reason ?? "Key failed");
            Repaint();
        }

        void CopySelectedPartsKeys()
        {
            _partsKeyClipboard.Clear();
            if (_partsSelectedKeys.Count == 0 || CurrentPartsClip == null) return;
            float earliest = float.MaxValue;
            foreach (var k in _partsSelectedKeys)
                if (k != null) earliest = Mathf.Min(earliest, k.Time);
            if (!(earliest < float.MaxValue)) return;

            var clip = CurrentPartsClip;
            for (int t = 0; t < (clip.Tracks?.Count ?? 0); t++)
            {
                var track = clip.Tracks[t];
                if (track?.Keys == null) continue;
                for (int k = 0; k < track.Keys.Count; k++)
                {
                    var key = track.Keys[k];
                    if (key == null || !_partsSelectedKeys.Contains(key)) continue;
                    var template = new SpritePartsKeyDef
                    {
                        Time = key.Time,
                        Position = key.Position,
                        Rotation = key.Rotation,
                        Scale = key.Scale,
                        Deform = key.Deform == null ? null : (Vector2[])key.Deform.Clone(),
                        EaseMode = key.EaseMode,
                        HasColor = key.HasColor,
                        Color = key.Color,
                        HasDrawOrder = key.HasDrawOrder,
                        DrawOrder = key.DrawOrder,
                        Channels = key.Channels,
                        HasClipActive = key.HasClipActive,
                        ClipActive = key.ClipActive,
                        Curve = key.Curve,
                        Separate = key.Separate,
                        Shear = key.Shear,
                        AppearanceId = key.AppearanceId ?? string.Empty,
                    };
                    _partsKeyClipboard.Add((
                        SpritePartIdUtility.Canonical(track.SlotId),
                        key.Time - earliest,
                        template));
                }
            }
            _status = $"Copied {_partsKeyClipboard.Count} Parts key{(_partsKeyClipboard.Count == 1 ? "" : "s")}";
        }

        void PastePartsKeysAtPlayhead()
        {
            if (ImportPreviewActive)
            {
                PauseForImportPreview("key edit");
                return;
            }
            if (_partsKeyClipboard.Count == 0 || CurrentPartsClip == null) return;
            RecordPartsUndo("Paste Parts Keys");
            var created = new List<SpritePartsKeyDef>();
            var result = SpritePartsAuthoringOps.PasteKeysAtTime(
                _profile, _partsSelectedClip, _partsKeyClipboard, _partsPreviewTime,
                _partsDisplayFps, snap: true, created);
            ClearPartsKeySelection();
            for (int i = 0; i < created.Count; i++)
                _partsSelectedKeys.Add(created[i]);
            SaveDirty();
            _status = result.Ok
                ? $"Pasted {result.Affected} Parts key{(result.Affected == 1 ? "" : "s")}"
                : (result.Reason ?? "Paste failed");
            Repaint();
        }

        void DuplicateSelectedPartsKeys()
        {
            if (ImportPreviewActive)
            {
                PauseForImportPreview("key edit");
                return;
            }
            if (_partsSelectedKeys.Count == 0 || CurrentPartsClip == null) return;
            float frame = 1f / Mathf.Max(1f, _partsDisplayFps);
            RecordPartsUndo("Duplicate Parts Keys");
            var created = new List<SpritePartsKeyDef>();
            var result = SpritePartsAuthoringOps.DuplicateKeys(
                _profile, _partsSelectedClip, _partsSelectedKeys, frame,
                _partsDisplayFps, snap: true, created);
            ClearPartsKeySelection();
            for (int i = 0; i < created.Count; i++)
                _partsSelectedKeys.Add(created[i]);
            SaveDirty();
            _status = result.Ok
                ? $"Duplicated {result.Affected} Parts key{(result.Affected == 1 ? "" : "s")}"
                : (result.Reason ?? "Duplicate keys failed");
            Repaint();
        }

        void NudgeSelectedPartsKeys(int frameDelta)
        {
            if (ImportPreviewActive)
            {
                PauseForImportPreview("key edit");
                return;
            }
            if (_partsSelectedKeys.Count == 0 || CurrentPartsClip == null) return;
            float frame = 1f / Mathf.Max(1f, _partsDisplayFps);
            var keys = new List<SpritePartsKeyDef>(_partsSelectedKeys);
            var starts = new List<float>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
                starts.Add(keys[i].Time);
            RecordPartsUndo("Nudge Parts Keys");
            SpritePartsAuthoringOps.MoveKeys(
                _profile, _partsSelectedClip, keys, starts,
                frameDelta * frame, _partsDisplayFps, snap: true);
            SaveDirty();
            Repaint();
        }

        void SelectAllPartsKeysInClip()
        {
            ClearPartsKeySelection();
            var clip = CurrentPartsClip;
            if (clip?.Tracks == null) return;
            for (int t = 0; t < clip.Tracks.Count; t++)
            {
                var track = clip.Tracks[t];
                if (track?.Keys == null) continue;
                for (int k = 0; k < track.Keys.Count; k++)
                    if (track.Keys[k] != null)
                        _partsSelectedKeys.Add(track.Keys[k]);
            }
            _status = $"Selected {_partsSelectedKeys.Count} Parts keys";
            Repaint();
        }

        void ShowPartsKeyContextMenu(SpritePartsKeyDef key)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Delete"), false, () =>
            {
                if (!_partsSelectedKeys.Contains(key))
                {
                    ClearPartsKeySelection();
                    _partsSelectedKeys.Add(key);
                }
                DeleteSelectedPartsKeys();
            });
            menu.AddItem(new GUIContent("Duplicate"), false, () =>
            {
                if (!_partsSelectedKeys.Contains(key))
                {
                    ClearPartsKeySelection();
                    _partsSelectedKeys.Add(key);
                }
                DuplicateSelectedPartsKeys();
            });
            menu.AddItem(new GUIContent("Copy"), false, () =>
            {
                if (!_partsSelectedKeys.Contains(key))
                {
                    ClearPartsKeySelection();
                    _partsSelectedKeys.Add(key);
                }
                CopySelectedPartsKeys();
            });
            if (_partsSelectedKeys.Count >= 2)
            {
                menu.AddSeparator(string.Empty);
                AddPartsKeyTimingMenu(menu);
            }
            menu.ShowAsContext();
        }

        void ShowPartsTrackContextMenu(string slotId, float time)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add Key Here"), false, () => InsertPartsKeyAtTime(slotId, time));
            if (_partsKeyClipboard.Count > 0)
                menu.AddItem(new GUIContent("Paste Keys"), false, PastePartsKeysAtPlayhead);
            else
                menu.AddDisabledItem(new GUIContent("Paste Keys"));
            menu.ShowAsContext();
        }

        void EndPartsKeyMarquee()
        {
            if (GUIUtility.hotControl == _partsKeyMarqueeHotControl)
                GUIUtility.hotControl = 0;
            _partsKeyMarqueeActive = false;
            _partsKeyMarqueeMoved = false;
            _partsKeyMarqueeHotControl = 0;
            _partsKeyMarqueeRect = default;
            _partsKeyMarqueeBaseline.Clear();
        }

        void RestorePartsKeyMarqueeBaseline()
        {
            _partsSelectedKeys.Clear();
            foreach (var k in _partsKeyMarqueeBaseline)
                if (k != null) _partsSelectedKeys.Add(k);
        }

        bool HandlePartsKeyShortcuts(Event evt)
        {
            if (_studioTab != StudioTab.Parts) return false;
            if (evt.type != EventType.KeyDown) return false;
            if (IsRenamingAnything() || IsEditingAnyTextField()) return false;

            bool ctrl = evt.control || evt.command;

            if (evt.keyCode == KeyCode.Escape && _partsSelectedKeys.Count > 0)
            {
                ClearPartsKeySelection();
                evt.Use();
                Repaint();
                return true;
            }

            if (evt.keyCode is KeyCode.Delete or KeyCode.Backspace)
            {
                if (_partsSelectedKeys.Count > 0)
                {
                    DeleteSelectedPartsKeys();
                    evt.Use();
                    return true;
                }
                return false;
            }

            if (evt.keyCode == KeyCode.I && !ctrl)
            {
                var slot = CurrentPartsSlot;
                if (slot != null)
                {
                    InsertPartsKeyAtTime(slot.SlotId, _partsPreviewTime);
                    evt.Use();
                    return true;
                }
            }

            if (ctrl && evt.keyCode == KeyCode.A)
            {
                SelectAllPartsKeysInClip();
                evt.Use();
                return true;
            }
            if (ctrl && evt.keyCode == KeyCode.C && _partsSelectedKeys.Count > 0)
            {
                CopySelectedPartsKeys();
                evt.Use();
                return true;
            }
            if (ctrl && evt.keyCode == KeyCode.V && _partsKeyClipboard.Count > 0)
            {
                PastePartsKeysAtPlayhead();
                evt.Use();
                return true;
            }
            if (ctrl && evt.keyCode == KeyCode.D && _partsSelectedKeys.Count > 0)
            {
                DuplicateSelectedPartsKeys();
                evt.Use();
                return true;
            }
            if (_partsSelectedKeys.Count > 0 &&
                (evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow))
            {
                NudgeSelectedPartsKeys(evt.keyCode == KeyCode.LeftArrow ? -1 : 1);
                evt.Use();
                return true;
            }
            return false;
        }
    }
}

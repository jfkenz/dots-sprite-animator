using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    public sealed partial class SpriteSheetToolWindow
    {

        void AddClip()
        {
            RecordDiscreteUndo("Add Sprite Animation Clip");
            _profile.EnsureSheets(_selectedSheet);
            int rows = Mathf.Max(1, _profile.SheetAt(_selectedSheet) != null ? _profile.SheetAt(_selectedSheet).Rows : _profile.Rows);
            int existingOnSheet = CountClipsOnSheet(_selectedSheet);
            var clip = new SpriteClipDef
            {
                Name = $"Clip {_profile.Clips.Count + 1}",
                SheetIndex = _selectedSheet,
                Row = existingOnSheet % rows,
                Frames = CreateDefaultFrames(_selectedSheet, existingOnSheet % rows),
            };
            clip.EnsureFrameData();
            _profile.Clips.Add(clip);
            _selectedClip = _profile.Clips.Count - 1;
            _collapsedSheets.Remove(_selectedSheet);
            SelectOnlyFrame(0);
            ClearColliderSelection();
            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
            _selectedOnionFrame = -1;
            ClearSocketToolState();
            _previewTime = 0f;
            SaveDirty();
            SealUndoGroup();
        }

        void DuplicateSelectedClips()
        {
            SyncClipMultiSelection();
            if (_selectedClips.Count <= 1)
            {
                DuplicateClip();
                return;
            }
            var ordered = new List<int>(_selectedClips);
            ordered.Sort();
            // Duplicate high→low so inserts after each index don't scramble remaining sources.
            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                _selectedClip = ordered[i];
                DuplicateClip();
            }
            SyncClipMultiSelection();
        }

        void DeleteSelectedClips()
        {
            SyncClipMultiSelection();
            if (_selectedClips.Count <= 1)
            {
                DeleteClip();
                return;
            }
            var ordered = new List<int>(_selectedClips);
            ordered.Sort();
            for (int i = ordered.Count - 1; i >= 0; i--)
                DeleteClipAt(ordered[i]);
            SyncClipMultiSelection();
        }

        void DrawOnCompleteClipField(SpriteClipDef clip)
        {
            if (clip == null || _profile?.Clips == null)
                return;

            var names = new List<string> { "(None)" };
            var indices = new List<int> { -1 };
            int selected = 0;
            for (int i = 0; i < _profile.Clips.Count; i++)
            {
                var other = _profile.Clips[i];
                if (other == null)
                    continue;
                string label = string.IsNullOrWhiteSpace(other.Name) ? $"Clip {i}" : other.Name;
                names.Add($"{i}: {label}");
                indices.Add(i);
                if (i == clip.OnCompleteClipIndex)
                    selected = names.Count - 1;
            }

            // Allow typed index that is not in list (legacy / deleted).
            if (clip.OnCompleteClipIndex >= 0 && selected == 0)
            {
                names.Add($"{clip.OnCompleteClipIndex}: (missing)");
                indices.Add(clip.OnCompleteClipIndex);
                selected = names.Count - 1;
            }

            EditorGUI.BeginChangeCheck();
            int pick = EditorGUILayout.Popup(
                new GUIContent("On Complete Clip",
                    "When Once ends and no one-shot resume / queue: auto-Play this clip. -1 = none."),
                selected, names.ToArray());
            if (EditorGUI.EndChangeCheck() && pick >= 0 && pick < indices.Count)
            {
                RecordProfileUndo("Set Sprite Clip On Complete");
                clip.OnCompleteClipIndex = indices[pick];
                SaveDirty();
            }
        }

        void DuplicateClip()
        {
            var source = CurrentClip;
            if (source == null) return;
            RecordProfileUndo("Duplicate Sprite Animation Clip");
            var clone = new SpriteClipDef
            {
                Name = source.Name + " Copy",
                SheetIndex = source.SheetIndex,
                Row = source.Row,
                Frames = (int[])source.Frames.Clone(),
                FrameRows = source.FrameRows != null ? (int[])source.FrameRows.Clone() : null,
                FrameRate = source.FrameRate,
                WrapMode = source.WrapMode,
                Interrupt = source.Interrupt,
                CancelAfter = source.CancelAfter,
                Priority = source.Priority,
                OnCompleteClipIndex = source.OnCompleteClipIndex,
                FrameDurationScales = (float[])source.FrameDurationScales.Clone(),
                EventIds = (byte[])source.EventIds.Clone(),
                EventNormalizedTimes = (float[])source.EventNormalizedTimes.Clone(),
                EventMarkers = source.CloneEventMarkers(),
                OnionOffsets = (Vector2[])source.OnionOffsets.Clone(),
                FrameScales = (Vector2[])source.FrameScales.Clone(),
                FrameRotations = (float[])source.FrameRotations.Clone(),
                FrameTweenModes = (byte[])source.FrameTweenModes.Clone(),
                FacingGroup = source.FacingGroup,
                Facing = source.Facing,
                Sockets = new List<FrameSocketDef>(),
            };
            if (source.Sockets != null)
            {
                for (int i = 0; i < source.Sockets.Count; i++)
                {
                    var socket = source.Sockets[i];
                    clone.Sockets.Add(new FrameSocketDef
                    {
                        Name = socket.Name,
                        FrameIndex = socket.FrameIndex,
                        LocalPosition = socket.LocalPosition,
                        LocalAngle = socket.LocalAngle,
                        LocalScale = socket.LocalScale,
                        DrawLayer = socket.DrawLayer,
                    });
                }
            }
            var copiedHitboxes = new List<FrameBoxDef>();
            for (int i = 0; i < _profile.Hitboxes.Count; i++)
            {
                var box = _profile.Hitboxes[i];
                if (box.IsCharacter || box.ClipName != source.Name)
                    continue;
                copiedHitboxes.Add(box.Clone(clone.Name));
            }
            _profile.Hitboxes.AddRange(copiedHitboxes);
            _profile.Clips.Insert(_selectedClip + 1, clone);
            _selectedClip++;
            _selectedClips.Clear();
            _selectedClips.Add(_selectedClip);
            _collapsedSheets.Remove(clone.SheetIndex);
            SelectOnlyFrame(0);
            ClearColliderSelection();
            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
            _selectedOnionFrame = -1;
            ClearSocketToolState();
            _previewTime = 0f;
            SaveDirty();
        }

        void DeleteClip()
        {
            DeleteClipAt(_selectedClip);
        }

        void DeleteClipAt(int clipIndex)
        {
            if (clipIndex < 0 || clipIndex >= _profile.Clips.Count)
                return;
            var clip = _profile.Clips[clipIndex];
            bool deletedSelected = clipIndex == _selectedClip;
            RecordDiscreteUndo("Delete Clip");
            _profile.Hitboxes.RemoveAll(box =>
                box != null && !box.IsCharacter && box.ClipName == clip.Name);
            _profile.Clips.RemoveAt(clipIndex);
            _selectedClips.Remove(clipIndex);
            var remapped = new HashSet<int>();
            foreach (int i in _selectedClips)
            {
                if (i > clipIndex) remapped.Add(i - 1);
                else remapped.Add(i);
            }
            _selectedClips.Clear();
            foreach (int i in remapped)
                _selectedClips.Add(i);
            if (_profile.Clips.Count == 0)
            {
                _selectedClip = -1;
                _selectedClips.Clear();
            }
            else
            {
                if (clipIndex < _selectedClip)
                    _selectedClip--;
                if (_selectedClip >= _profile.Clips.Count)
                    _selectedClip = _profile.Clips.Count - 1;
                var remaining = _selectedClip >= 0 && _selectedClip < _profile.Clips.Count
                    ? _profile.Clips[_selectedClip]
                    : null;
                if (remaining == null || remaining.SheetIndex != _selectedSheet)
                    _selectedClip = FirstClipIndexOfSheet(_selectedSheet);
                if (_selectedClip >= 0)
                    _selectedClips.Add(_selectedClip);
            }
            if (_renamingClip == clipIndex)
                ClearClipRename();
            else if (_renamingClip > clipIndex)
                _renamingClip--;
            if (deletedSelected)
            {
                SelectOnlyFrame(0);
                ClearColliderSelection();
                _selectedEventFrame = -1;
            _selectedEventIndex = -1;
                _selectedOnionFrame = -1;
                ClearSocketToolState();
                _previewTime = 0f;
            }
            _status = $"Deleted clip {clip.Name}";
            SaveDirty();
            Repaint();
        }

        int NextUnusedOccupiedColumn(SpriteClipDef clip)
        {
            if (clip?.Frames == null || clip.Frames.Length == 0)
                return 0;
            var def = _profile.SheetForClip(clip);
            int cols = def != null && def.Columns > 0 ? def.Columns : Mathf.Max(1, _profile.Columns);
            int rows = def != null && def.Rows > 0 ? def.Rows : Mathf.Max(1, _profile.Rows);
            int row = Mathf.Clamp(clip.Row, 0, Mathf.Max(0, rows - 1));
            int start = 0;
            if (_selectedFrame >= 0 && _selectedFrame < clip.Frames.Length)
                start = clip.Frames[_selectedFrame] + 1;
            var used = new HashSet<int>(clip.Frames);
            bool canSample = TryEnsureSheetPixelCache(clip);
            for (int n = 0; n < cols; n++)
            {
                int col = (start + n) % cols;
                if (used.Contains(col))
                    continue;
                if (canSample && IsSheetCellEmpty(col, row))
                    continue;
                return col;
            }
            return -1;
        }

        void InsertFrameAfter(SpriteClipDef clip)
        {
            if (clip == null)
                return;
            clip.EnsureFrameData();
            int nextCol = NextUnusedOccupiedColumn(clip);
            if (nextCol < 0)
            {
                _status = "No more drawn cells on this row";
                return;
            }

            bool replaceEmptyOnly = clip.Frames.Length == 1 &&
                _selectedFrame == 0 &&
                TryEnsureSheetPixelCache(clip) &&
                IsClipFrameCellEmpty(clip, 0);
            if (replaceEmptyOnly)
            {
                RecordProfileUndo("Insert Sprite Animation Frame");
                clip.Frames[0] = nextCol;
                SelectOnlyFrame(0);
                SaveDirty();
                _status = $"Filled empty frame with column {nextCol}";
                return;
            }

            RecordProfileUndo("Insert Sprite Animation Frame");
            int insert = _selectedFrame + 1;
            var frames = new List<int>(clip.Frames);
            frames.Insert(insert, nextCol);
            var frameRows = new List<int>(clip.FrameRows ?? Array.Empty<int>());
            while (frameRows.Count < clip.Frames.Length)
                frameRows.Add(SpriteClipDef.InheritClipRow);
            frameRows.Insert(insert, SpriteClipDef.InheritClipRow);
            var durations = new List<float>(clip.FrameDurationScales);
            durations.Insert(insert, clip.FrameDurationScales[_selectedFrame]);
            var events = new List<byte>(clip.EventIds);
            events.Insert(insert, 0);
            var eventTimes = new List<float>(clip.EventNormalizedTimes);
            eventTimes.Insert(insert, 0f);
            var onionOffsets = new List<Vector2>(clip.OnionOffsets);
            onionOffsets.Insert(insert, Vector2.zero);
            var frameScales = new List<Vector2>(clip.FrameScales);
            frameScales.Insert(insert, Vector2.one);
            var frameRotations = new List<float>(clip.FrameRotations);
            frameRotations.Insert(insert, 0f);
            var frameTweens = new List<byte>(clip.FrameTweenModes);
            frameTweens.Insert(insert, (byte)SpriteEaseMode.Linear);
            clip.Frames = frames.ToArray();
            clip.FrameRows = frameRows.ToArray();
            clip.FrameDurationScales = durations.ToArray();
            clip.EventIds = events.ToArray();
            clip.EventNormalizedTimes = eventTimes.ToArray();
            clip.OnionOffsets = onionOffsets.ToArray();
            clip.FrameScales = frameScales.ToArray();
            clip.FrameRotations = frameRotations.ToArray();
            clip.FrameTweenModes = frameTweens.ToArray();
            clip.ShiftEventMarkersAfterInsert(insert);
            if (clip.Sockets != null)
            {
                for (int i = 0; i < clip.Sockets.Count; i++)
                {
                    if (clip.Sockets[i].FrameIndex >= insert)
                        clip.Sockets[i].FrameIndex++;
                }
            }
            if (_selectedOnionFrame >= insert)
                _selectedOnionFrame++;
            if (_selectedEventFrame >= insert)
                _selectedEventFrame++;
            foreach (var box in _profile.Hitboxes)
                if (box.ClipName == clip.Name && box.FrameIndex >= insert)
                    box.FrameIndex++;
            SelectOnlyFrame(insert);
            SaveDirty();
        }

        void DuplicateSelectedFrames(SpriteClipDef clip)
        {
            if (clip == null)
                return;
            clip.EnsureFrameData();
            EnsureFrameSelection(clip.Frames.Length);
            var selected = new List<int>(_selectedFrames);
            selected.RemoveAll(index => index < 0 || index >= clip.Frames.Length);
            selected.Sort();
            if (selected.Count == 0)
                return;

            int insert = selected[selected.Count - 1] + 1;
            int count = selected.Count;
            RecordDiscreteUndo(count == 1
                ? "Duplicate Sprite Animation Frame"
                : "Duplicate Sprite Animation Frames");

            var frames = new List<int>(clip.Frames);
            var frameRows = new List<int>(clip.FrameRows);
            var durations = new List<float>(clip.FrameDurationScales);
            var events = new List<byte>(clip.EventIds);
            var eventTimes = new List<float>(clip.EventNormalizedTimes);
            var onionOffsets = new List<Vector2>(clip.OnionOffsets);
            var frameScales = new List<Vector2>(clip.FrameScales);
            var frameRotations = new List<float>(clip.FrameRotations);
            var frameTweens = new List<byte>(clip.FrameTweenModes);
            for (int s = 0; s < count; s++)
            {
                int src = selected[s];
                int dest = insert + s;
                frames.Insert(dest, clip.Frames[src]);
                frameRows.Insert(dest, clip.FrameRows[src]);
                durations.Insert(dest, clip.FrameDurationScales[src]);
                events.Insert(dest, 0);
                eventTimes.Insert(dest, 0f);
                onionOffsets.Insert(dest, clip.OnionOffsets[src]);
                frameScales.Insert(dest, clip.FrameScales[src]);
                frameRotations.Insert(dest, clip.FrameRotations[src]);
                frameTweens.Insert(dest, clip.FrameTweenModes[src]);
            }
            clip.Frames = frames.ToArray();
            clip.FrameRows = frameRows.ToArray();
            clip.FrameDurationScales = durations.ToArray();
            clip.EventIds = events.ToArray();
            clip.EventNormalizedTimes = eventTimes.ToArray();
            clip.OnionOffsets = onionOffsets.ToArray();
            clip.FrameScales = frameScales.ToArray();
            clip.FrameRotations = frameRotations.ToArray();
            clip.FrameTweenModes = frameTweens.ToArray();
            ShiftClipAttachmentsAfterInsert(clip, insert, count);

            if (clip.Sockets != null)
            {
                int socketCount = clip.Sockets.Count;
                for (int i = 0; i < socketCount; i++)
                {
                    var socket = clip.Sockets[i];
                    int destOffset = selected.IndexOf(socket.FrameIndex);
                    if (destOffset < 0)
                        continue;
                    clip.Sockets.Add(new FrameSocketDef
                    {
                        Name = socket.Name,
                        FrameIndex = insert + destOffset,
                        LocalPosition = socket.LocalPosition,
                        LocalAngle = socket.LocalAngle,
                        LocalScale = socket.LocalScale,
                        DrawLayer = socket.DrawLayer,
                    });
                }
            }

            clip.EnsureEventMarkers();
            int markerCount = clip.EventMarkers.Count;
            for (int i = 0; i < markerCount; i++)
            {
                var marker = clip.EventMarkers[i];
                if (marker == null)
                    continue;
                int destOffset = selected.IndexOf(marker.FrameIndex);
                if (destOffset < 0)
                    continue;
                var clone = marker.Clone();
                clone.FrameIndex = insert + destOffset;
                clip.EventMarkers.Add(clone);
            }
            clip.SyncLegacyEventsFromMarkers();

            if (_profile.Hitboxes != null)
            {
                int boxCount = _profile.Hitboxes.Count;
                for (int i = 0; i < boxCount; i++)
                {
                    var box = _profile.Hitboxes[i];
                    if (box == null || box.ClipName != clip.Name || !box.IsFrame)
                        continue;
                    int destOffset = selected.IndexOf(box.FrameIndex);
                    if (destOffset < 0)
                        continue;
                    _profile.Hitboxes.Add(box.Clone(clip.Name, insert + destOffset));
                }
            }

            _selectedFrames.Clear();
            for (int i = 0; i < count; i++)
                _selectedFrames.Add(insert + i);
            _selectedFrame = insert;
            _frameListAnchor = insert;
            _previewTime = PreviewTimeForAuthoredTime(clip, AuthoredStartTime(clip, insert));
            SaveDirty();
            _status = count == 1
                ? $"Duplicated frame {selected[0] + 1}"
                : $"Duplicated {count} frames";
            Repaint();
        }

        void ShiftClipAttachmentsAfterInsert(SpriteClipDef clip, int insert, int count)
        {
            if (clip == null || count <= 0)
                return;
            clip.ShiftEventMarkersAfterInsert(insert, count);
            if (clip.Sockets != null)
            {
                for (int i = 0; i < clip.Sockets.Count; i++)
                {
                    if (clip.Sockets[i].FrameIndex >= insert)
                        clip.Sockets[i].FrameIndex += count;
                }
            }
            if (_selectedOnionFrame >= insert)
                _selectedOnionFrame += count;
            if (_selectedEventFrame >= insert)
                _selectedEventFrame += count;
            if (_profile.Hitboxes == null)
                return;
            foreach (var box in _profile.Hitboxes)
            {
                if (box != null && box.ClipName == clip.Name && box.IsFrame && box.FrameIndex >= insert)
                    box.FrameIndex += count;
            }
        }

        void CreateClipsFromSheetRows()
        {
            _profile.EnsureSheets(_selectedSheet);
            WriteActiveSheetFromLegacy();
            var def = _profile.SheetAt(_selectedSheet);
            if (def?.Texture == null && _profile.Sheet == null)
            {
                _status = "Assign a sprite sheet before creating clips";
                return;
            }

            int cols = Mathf.Max(1, def != null && def.Columns > 0 ? def.Columns : _profile.Columns);
            int rows = Mathf.Max(1, def != null && def.Rows > 0 ? def.Rows : _profile.Rows);
            TryEnsureSheetPixelCache(def);
            string sheetName = SheetDisplayName(def, _selectedSheet);
            RecordDiscreteUndo("Create Clips From Sheet Rows");
            int created = 0;
            int skippedExisting = 0;
            int skippedEmpty = 0;
            for (int r = 0; r < rows; r++)
            {
                if (SheetHasRowClip(_selectedSheet, r))
                {
                    skippedExisting++;
                    continue;
                }
                int[] frames = OccupiedColumnsOnRow(_selectedSheet, r, cols);
                if (frames == null || frames.Length == 0)
                {
                    skippedEmpty++;
                    continue;
                }
                var clip = new SpriteClipDef
                {
                    Name = UniqueClipName($"{sheetName} row {r + 1}", -1),
                    SheetIndex = _selectedSheet,
                    Row = r,
                    Frames = frames,
                };
                clip.EnsureFrameData();
                _profile.Clips.Add(clip);
                created++;
            }
            if (created > 0)
            {
                _selectedClip = _profile.Clips.Count - 1;
                _collapsedSheets.Remove(_selectedSheet);
                SelectOnlyFrame(0);
                ClearColliderSelection();
                _selectedEventFrame = -1;
                _selectedEventIndex = -1;
                _selectedOnionFrame = -1;
                ClearSocketToolState();
                _previewTime = 0f;
                SaveDirty();
            }
            SealUndoGroup();
            _status = created == 0
                ? (skippedExisting == rows
                    ? "Every row already has a clip"
                    : "No occupied rows left to turn into clips")
                : $"Created {created} clip{Plural(created)} from rows"
                    + (skippedExisting > 0 ? $"  •  skipped {skippedExisting} existing" : string.Empty)
                    + (skippedEmpty > 0 ? $"  •  skipped {skippedEmpty} empty" : string.Empty);
            Repaint();
        }

        void CreateClipsFromSheetColumns()
        {
            _profile.EnsureSheets(_selectedSheet);
            WriteActiveSheetFromLegacy();
            var def = _profile.SheetAt(_selectedSheet);
            if (def?.Texture == null && _profile.Sheet == null)
            {
                _status = "Assign a sprite sheet before creating clips";
                return;
            }

            int cols = Mathf.Max(1, def != null && def.Columns > 0 ? def.Columns : _profile.Columns);
            int rows = Mathf.Max(1, def != null && def.Rows > 0 ? def.Rows : _profile.Rows);
            TryEnsureSheetPixelCache(def);
            string sheetName = SheetDisplayName(def, _selectedSheet);
            RecordDiscreteUndo("Create Clips From Sheet Columns");
            int created = 0;
            int skippedExisting = 0;
            int skippedEmpty = 0;
            for (int c = 0; c < cols; c++)
            {
                var occupiedRows = OccupiedRowsOnColumn(_selectedSheet, c, rows);
                if (occupiedRows.Count == 0)
                {
                    skippedEmpty++;
                    continue;
                }
                if (SheetHasColumnClip(_selectedSheet, c, occupiedRows, cols, rows))
                {
                    skippedExisting++;
                    continue;
                }
                var frames = new int[occupiedRows.Count];
                var frameRows = new int[occupiedRows.Count];
                for (int i = 0; i < occupiedRows.Count; i++)
                {
                    frames[i] = c;
                    frameRows[i] = occupiedRows[i];
                }
                var clip = new SpriteClipDef
                {
                    Name = UniqueClipName($"{sheetName} col {c + 1}", -1),
                    SheetIndex = _selectedSheet,
                    Row = occupiedRows[0],
                    Frames = frames,
                    FrameRows = frameRows,
                };
                clip.EnsureFrameData();
                _profile.Clips.Add(clip);
                created++;
            }
            if (created > 0)
            {
                _selectedClip = _profile.Clips.Count - 1;
                _collapsedSheets.Remove(_selectedSheet);
                SelectOnlyFrame(0);
                ClearColliderSelection();
                _selectedEventFrame = -1;
                _selectedEventIndex = -1;
                _selectedOnionFrame = -1;
                ClearSocketToolState();
                _previewTime = 0f;
                SaveDirty();
            }
            SealUndoGroup();
            _status = created == 0
                ? (skippedExisting == cols
                    ? "Every column already has a clip"
                    : "No occupied columns left to turn into clips")
                : $"Created {created} clip{Plural(created)} from columns"
                    + (skippedExisting > 0 ? $"  •  skipped {skippedExisting} existing" : string.Empty)
                    + (skippedEmpty > 0 ? $"  •  skipped {skippedEmpty} empty" : string.Empty);
            Repaint();
        }

        string SheetDisplayName(SpriteSheetDef def, int sheetIndex)
        {
            if (def != null && !string.IsNullOrWhiteSpace(def.Name))
                return def.Name.Trim();
            if (def?.Texture != null && !string.IsNullOrEmpty(def.Texture.name))
                return def.Texture.name;
            return $"Sheet {sheetIndex + 1}";
        }

        bool SheetHasRowClip(int sheetIndex, int row)
        {
            if (_profile?.Clips == null)
                return false;
            for (int i = 0; i < _profile.Clips.Count; i++)
            {
                var clip = _profile.Clips[i];
                if (clip == null || clip.SheetIndex != sheetIndex || clip.UsesMixedSheetRows())
                    continue;
                if (clip.Row == row)
                    return true;
            }
            return false;
        }

        bool SheetHasColumnClip(int sheetIndex, int column, List<int> occupiedRows,
            int columns, int rows)
        {
            if (_profile?.Clips == null || occupiedRows == null || occupiedRows.Count == 0)
                return false;
            for (int i = 0; i < _profile.Clips.Count; i++)
            {
                var clip = _profile.Clips[i];
                if (clip?.Frames == null || clip.SheetIndex != sheetIndex ||
                    clip.Frames.Length != occupiedRows.Count)
                    continue;
                bool match = true;
                for (int f = 0; f < clip.Frames.Length; f++)
                {
                    clip.ResolveSheetCell(f, columns, rows, out int cellRow, out int cellCol);
                    if (cellCol != column || cellRow != occupiedRows[f])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return true;
            }
            return false;
        }

        int[] OccupiedColumnsOnRow(int sheetIndex, int row, int columns)
        {
            columns = Mathf.Max(1, columns);
            var occupied = new List<int>();
            var probe = new SpriteClipDef
            {
                SheetIndex = sheetIndex,
                Row = row,
                Frames = new[] { 0 },
            };
            bool canSample = TryEnsureSheetPixelCache(probe);
            for (int c = 0; c < columns; c++)
            {
                if (!canSample || !IsSheetCellEmpty(c, row))
                    occupied.Add(c);
            }
            return occupied.ToArray();
        }

        List<int> OccupiedRowsOnColumn(int sheetIndex, int column, int rows)
        {
            rows = Mathf.Max(1, rows);
            var occupied = new List<int>();
            var probe = new SpriteClipDef
            {
                SheetIndex = sheetIndex,
                Row = 0,
                Frames = new[] { column },
            };
            bool canSample = TryEnsureSheetPixelCache(probe);
            for (int r = 0; r < rows; r++)
            {
                if (!canSample || !IsSheetCellEmpty(column, r))
                    occupied.Add(r);
            }
            return occupied;
        }

        void OpenSheetCellPicker()
        {
            var clip = CurrentClip;
            if (clip == null)
            {
                _status = "Select a clip before picking sheet cells";
                return;
            }
            var def = _profile.SheetForClip(clip) ?? _profile.SheetAt(_selectedSheet);
            var tex = def?.Texture ?? _profile.Sheet;
            if (tex == null)
            {
                _status = "Assign a sprite sheet first";
                return;
            }
            _showSheetCellPicker = true;
            _sheetCellPickerSelection.Clear();
            _sheetCellPickerAnchor = -1;
            _sheetCellPickerScroll = Vector2.zero;
            _sheetCellPickerRect = new Rect(
                Mathf.Max(24f, (position.width - 560f) * 0.5f),
                Mathf.Max(48f, (position.height - 500f) * 0.5f),
                560f, 500f);
            _status = "Pick sheet cells, then OK to add them as frames";
            Repaint();
        }

        void CloseSheetCellPicker()
        {
            _showSheetCellPicker = false;
            _sheetCellPickerDragging = false;
            _sheetCellPickerSelection.Clear();
            _sheetCellPickerAnchor = -1;
        }

        void ConfirmSheetCellPicker()
        {
            var clip = CurrentClip;
            if (clip == null || _sheetCellPickerSelection.Count == 0)
            {
                CloseSheetCellPicker();
                return;
            }
            AppendSheetCells(clip, new List<int>(_sheetCellPickerSelection));
            CloseSheetCellPicker();
        }

        void AppendSheetCells(SpriteClipDef clip, List<int> cells)
        {
            if (clip == null || cells == null || cells.Count == 0)
                return;
            clip.EnsureFrameData();
            int cols = ClipSheetColumns(clip);
            bool replaceEmpty = clip.Frames.Length == 1 &&
                TryEnsureSheetPixelCache(clip) &&
                IsClipFrameCellEmpty(clip, 0);

            RecordDiscreteUndo(cells.Count == 1
                ? "Add Frame From Sheet"
                : "Add Frames From Sheet");

            int start = 0;
            if (replaceEmpty)
            {
                ApplySheetCellToFrame(clip, 0, cells[0], cols);
                start = 1;
                if (cells.Count == 1)
                {
                    SelectOnlyFrame(0);
                    SaveDirty();
                    _status = "Set frame 1 from sheet cell";
                    SealUndoGroup();
                    return;
                }
            }

            int insert = replaceEmpty ? 1 : Mathf.Clamp(_selectedFrame + 1, 0, clip.Frames.Length);
            InsertSheetCellsAt(clip, insert, cells, start, cols);
            SealUndoGroup();
        }

        void ApplySheetCellToFrame(SpriteClipDef clip, int frame, int cell, int columns)
        {
            columns = Mathf.Max(1, columns);
            int col = Mathf.Clamp(cell % columns, 0, columns - 1);
            int row = cell / columns;
            clip.Frames[frame] = col;
            if (clip.FrameRows != null && frame < clip.FrameRows.Length)
                clip.FrameRows[frame] = row == clip.Row
                    ? SpriteClipDef.InheritClipRow
                    : row;
        }

        void InsertSheetCellsAt(SpriteClipDef clip, int insert, List<int> cells, int start, int columns)
        {
            int add = cells.Count - start;
            if (add <= 0)
                return;
            insert = Mathf.Clamp(insert, 0, clip.Frames.Length);
            float duration = clip.FrameDurationScales[
                Mathf.Clamp(_selectedFrame, 0, clip.FrameDurationScales.Length - 1)];

            var frames = new List<int>(clip.Frames);
            var frameRows = new List<int>(clip.FrameRows);
            var durations = new List<float>(clip.FrameDurationScales);
            var events = new List<byte>(clip.EventIds);
            var eventTimes = new List<float>(clip.EventNormalizedTimes);
            var onionOffsets = new List<Vector2>(clip.OnionOffsets);
            var frameScales = new List<Vector2>(clip.FrameScales);
            var frameRotations = new List<float>(clip.FrameRotations);
            var frameTweens = new List<byte>(clip.FrameTweenModes);
            columns = Mathf.Max(1, columns);
            for (int i = 0; i < add; i++)
            {
                int cell = cells[start + i];
                int col = Mathf.Clamp(cell % columns, 0, columns - 1);
                int row = cell / columns;
                int dest = insert + i;
                frames.Insert(dest, col);
                frameRows.Insert(dest, row == clip.Row
                    ? SpriteClipDef.InheritClipRow
                    : row);
                durations.Insert(dest, duration);
                events.Insert(dest, 0);
                eventTimes.Insert(dest, 0f);
                onionOffsets.Insert(dest, Vector2.zero);
                frameScales.Insert(dest, Vector2.one);
                frameRotations.Insert(dest, 0f);
                frameTweens.Insert(dest, (byte)SpriteEaseMode.Linear);
            }
            clip.Frames = frames.ToArray();
            clip.FrameRows = frameRows.ToArray();
            clip.FrameDurationScales = durations.ToArray();
            clip.EventIds = events.ToArray();
            clip.EventNormalizedTimes = eventTimes.ToArray();
            clip.OnionOffsets = onionOffsets.ToArray();
            clip.FrameScales = frameScales.ToArray();
            clip.FrameRotations = frameRotations.ToArray();
            clip.FrameTweenModes = frameTweens.ToArray();
            ShiftClipAttachmentsAfterInsert(clip, insert, add);
            _selectedFrames.Clear();
            for (int i = 0; i < add; i++)
                _selectedFrames.Add(insert + i);
            _selectedFrame = insert;
            _frameListAnchor = insert;
            _previewTime = PreviewTimeForAuthoredTime(clip, AuthoredStartTime(clip, insert));
            SaveDirty();
            _status = add == 1
                ? $"Added 1 frame from the sheet  •  {clip.Frames.Length} total"
                : $"Added {add} frames from the sheet  •  {clip.Frames.Length} total";
            Repaint();
        }

        void RemoveSelectedFrames(SpriteClipDef clip)
        {
            if (clip == null)
                return;
            EnsureFrameSelection(clip.Frames.Length);
            RemoveFrames(clip, new List<int>(_selectedFrames));
        }

        void RemoveFrames(SpriteClipDef clip, List<int> indices)
        {
            if (clip == null || indices == null)
                return;
            clip.EnsureFrameData();
            if (clip.Frames.Length <= 1)
            {
                _status = "A clip must keep at least one frame";
                return;
            }

            var remove = new HashSet<int>();
            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if (index >= 0 && index < clip.Frames.Length)
                    remove.Add(index);
            }
            if (remove.Count == 0)
                return;

            if (remove.Count >= clip.Frames.Length)
                remove.Remove(0);
            if (remove.Count == 0)
            {
                _status = "A clip must keep at least one frame";
                return;
            }

            int oldCount = clip.Frames.Length;
            var remap = new int[oldCount];
            int newCount = 0;
            for (int i = 0; i < oldCount; i++)
            {
                if (remove.Contains(i))
                    remap[i] = -1;
                else
                    remap[i] = newCount++;
            }

            RecordProfileUndo(remove.Count == 1
                ? "Remove Sprite Animation Frame"
                : "Remove Sprite Animation Frames");

            clip.Frames = CompactArray(clip.Frames, remap, newCount);
            clip.FrameRows = CompactArray(clip.FrameRows, remap, newCount);
            clip.FrameDurationScales = CompactArray(clip.FrameDurationScales, remap, newCount);
            clip.EventIds = CompactArray(clip.EventIds, remap, newCount);
            clip.EventNormalizedTimes = CompactArray(clip.EventNormalizedTimes, remap, newCount);
            clip.OnionOffsets = CompactArray(clip.OnionOffsets, remap, newCount);
            clip.FrameScales = CompactArray(clip.FrameScales, remap, newCount);
            clip.FrameRotations = CompactArray(clip.FrameRotations, remap, newCount);
            clip.FrameTweenModes = CompactArray(clip.FrameTweenModes, remap, newCount);
            SpriteClipEventMarker keepEvent = null;
            if (_selectedEventIndex >= 0 && clip.EventMarkers != null &&
                _selectedEventIndex < clip.EventMarkers.Count)
                keepEvent = clip.EventMarkers[_selectedEventIndex];
            clip.CompactEventMarkers(remap);
            _selectedEventIndex = keepEvent != null ? clip.EventMarkers.IndexOf(keepEvent) : -1;

            if (clip.Sockets != null)
            {
                clip.Sockets.RemoveAll(socket =>
                    socket.FrameIndex < 0 || socket.FrameIndex >= oldCount || remap[socket.FrameIndex] < 0);
                for (int i = 0; i < clip.Sockets.Count; i++)
                {
                    int mapped = remap[clip.Sockets[i].FrameIndex];
                    if (mapped >= 0)
                        clip.Sockets[i].FrameIndex = mapped;
                }
            }

            if (_selectedOnionFrame >= 0 && _selectedOnionFrame < oldCount)
                _selectedOnionFrame = remap[_selectedOnionFrame];
            else
                _selectedOnionFrame = -1;

            if (_selectedEventFrame >= 0 && _selectedEventFrame < oldCount)
                _selectedEventFrame = remap[_selectedEventFrame];
            else
            {
                _selectedEventFrame = -1;
                _selectedEventIndex = -1;
            }

            _profile.Hitboxes ??= new List<FrameBoxDef>();
            _profile.Hitboxes.RemoveAll(box =>
                box.ClipName == clip.Name &&
                (box.FrameIndex < 0 || box.FrameIndex >= oldCount || remap[box.FrameIndex] < 0));
            foreach (var box in _profile.Hitboxes)
            {
                if (box.ClipName == clip.Name && box.FrameIndex >= 0 && box.FrameIndex < oldCount)
                    box.FrameIndex = remap[box.FrameIndex];
            }

            int landing = -1;
            int primary = Mathf.Clamp(_selectedFrame, 0, oldCount - 1);
            if (remap[primary] >= 0)
            {
                landing = remap[primary];
            }
            else
            {
                for (int i = primary + 1; i < oldCount; i++)
                {
                    if (remap[i] >= 0)
                    {
                        landing = remap[i];
                        break;
                    }
                }
                if (landing < 0)
                {
                    for (int i = primary - 1; i >= 0; i--)
                    {
                        if (remap[i] >= 0)
                        {
                            landing = remap[i];
                            break;
                        }
                    }
                }
            }

            SelectOnlyFrame(Mathf.Clamp(landing, 0, newCount - 1));
            _previewTime = PreviewTimeForAuthoredTime(clip, AuthoredStartTime(clip, _selectedFrame));
            PruneColliderSelection(clip, _selectedFrame);
            // Keep SO Data pointing at the same working profile so SaveProfile
            // cannot write a stale Frames array from a diverged _asset.Data.
            SyncWorkingProfileToAsset();
            SaveDirty();
            _status = remove.Count == 1
                ? $"Removed frame {FirstRemovedIndex(remove) + 1}  •  {clip.Frames.Length} remaining"
                : $"Removed {remove.Count} frames  •  {clip.Frames.Length} remaining";
            Repaint();
        }

        static T[] CompactArray<T>(T[] source, int[] remap, int newCount)
        {
            var dest = new T[newCount];
            if (source == null)
                return dest;
            int limit = Mathf.Min(source.Length, remap.Length);
            for (int i = 0; i < limit; i++)
            {
                int mapped = remap[i];
                if (mapped >= 0)
                    dest[mapped] = source[i];
            }
            return dest;
        }

        static int FirstRemovedIndex(HashSet<int> remove)
        {
            int first = int.MaxValue;
            foreach (int index in remove)
                if (index < first)
                    first = index;
            return first == int.MaxValue ? 0 : first;
        }

        void DeleteEmptyFrames(SpriteClipDef clip)
        {
            if (clip == null)
                return;
            clip.EnsureFrameData();
            var empty = CollectEmptyFrameIndices(clip);
            if (empty.Count == 0)
            {
                _status = "No empty frames in this clip";
                return;
            }

            int before = clip.Frames.Length;
            RemoveFrames(clip, empty);
            int removed = before - clip.Frames.Length;
            _status = removed == 0
                ? "A clip must keep at least one frame"
                : $"Deleted {removed} empty frame{(removed == 1 ? string.Empty : "s")}  •  {clip.Frames.Length} remaining";
        }

        int CountEmptyFrames(SpriteClipDef clip)
            => CollectEmptyFrameIndices(clip).Count;

        List<int> CollectEmptyFrameIndices(SpriteClipDef clip)
        {
            var empty = new List<int>();
            if (clip?.Frames == null || !TryEnsureSheetPixelCache(clip))
                return empty;
            for (int i = 0; i < clip.Frames.Length; i++)
            {
                if (IsClipFrameCellEmpty(clip, i))
                    empty.Add(i);
            }
            return empty;
        }

        bool IsClipFrameCellEmpty(SpriteClipDef clip, int frame)
        {
            if (clip?.Frames == null || frame < 0 || frame >= clip.Frames.Length)
                return false;
            var def = _profile.SheetForClip(clip);
            int columns = def != null && def.Columns > 0 ? def.Columns : Mathf.Max(1, _profile.Columns);
            int rows = def != null && def.Rows > 0 ? def.Rows : Mathf.Max(1, _profile.Rows);
            clip.ResolveSheetCell(frame, columns, rows, out int row, out int column);
            return IsSheetCellEmpty(column, row);
        }

        bool IsSheetCellEmpty(int column, int row)
        {
            if (_sheetCellEmpty == null)
                return false;
            int columns = Mathf.Max(1, _sheetPixelsColumns);
            int rows = Mathf.Max(1, _sheetPixelsRows);
            column = Mathf.Clamp(column, 0, columns - 1);
            row = Mathf.Clamp(row, 0, rows - 1);
            int index = row * columns + column;
            return index >= 0 && index < _sheetCellEmpty.Length && _sheetCellEmpty[index];
        }

    }
}

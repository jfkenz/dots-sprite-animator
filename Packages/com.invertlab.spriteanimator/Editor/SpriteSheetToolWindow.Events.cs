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

        void DrawEventMarkerInspector(SpriteClipDef clip)
        {
            GUILayout.Space(9f);
            SectionLabel("EVENT MARKER");
            clip.EnsureFrameData();

            int viewFrame = Mathf.Clamp(_selectedFrame, 0, clip.Frames.Length - 1);
            var firstOnFrame = clip.FirstMarkerOnFrame(viewFrame);
            int firstIndex = clip.IndexOfFirstMarkerOnFrame(viewFrame);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("This Frame", $"{viewFrame + 1} of {clip.Frames.Length}");
                using (new EditorGUI.DisabledScope(firstIndex < 0 ||
                    EventMarkerIsSelected(clip, firstIndex)))
                {
                    if (GUILayout.Button(EventGoToContent(),
                        EditorStyles.miniButton, GUILayout.Width(27f), GUILayout.Height(18f)))
                        JumpToEventHome(clip, firstIndex >= 0 ? firstIndex : -1, viewFrame);
                }
            }

            byte currentId = firstOnFrame != null ? firstOnFrame.EventId : (byte)0;
            int nextId = Mathf.Clamp(EditorGUILayout.IntField(
                new GUIContent("Event ID",
                    "0 = none. Stamps the first marker on this frame. Extra markers stay unless you delete them."),
                currentId), 0, byte.MaxValue);
            if (nextId != currentId)
            {
                SetFrameEvent(clip, viewFrame, (byte)nextId,
                    firstOnFrame != null ? firstOnFrame.NormalizedTime : 0f);
                currentId = (byte)nextId;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_profile.Events == null || _profile.Events.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("Add Marker",
                        "Place another event on this frame. Footstep + land can share a cell."),
                        EditorStyles.miniButton))
                    {
                        byte addId = currentId != 0
                            ? currentId
                            : NextPlacedOrCatalogEventId();
                        if (addId != 0)
                            PlaceEventMarker(clip, viewFrame, addId);
                    }
                }
            }

            if (currentId == 0 && clip.MarkerCountOnFrame(viewFrame) == 0)
            {
                EditorGUILayout.HelpBox(
                    "No marker on this frame. Enter an Event ID, Add Marker, or right-click the timeline event lane.",
                    MessageType.None);
            }

            var thisClipMarkers = new List<(SpriteClipDef clip, int markerIndex)>();
            var otherClipMarkers = new List<(SpriteClipDef clip, int markerIndex)>();
            CollectEventMarkers(clip, thisClipMarkers, otherClipMarkers);
            GUILayout.Label(
                $"{thisClipMarkers.Count} on this clip  •  {otherClipMarkers.Count} on other clips",
                _mutedStyle);

            if (DrawEventMarkerScope(thisClipMarkers, ref _eventThisClipExpanded,
                    "THIS CLIP",
                    "Every event marker on this clip, including extras on the same frame. Search jumps the preview."))
                return;
            if (otherClipMarkers.Count > 0 &&
                DrawEventMarkerScope(otherClipMarkers, ref _eventOtherClipsExpanded,
                    "OTHER CLIPS",
                    "Event markers on other clips. Search jumps to that clip and frame."))
                return;

            DrawEventTypeCatalog();
        }

        byte NextPlacedOrCatalogEventId()
        {
            if (_profile.Events != null)
            {
                for (int i = 0; i < _profile.Events.Count; i++)
                {
                    if (_profile.Events[i] != null && _profile.Events[i].Id != 0)
                        return _profile.Events[i].Id;
                }
            }
            return 1;
        }

        void CollectEventMarkers(SpriteClipDef current,
            List<(SpriteClipDef clip, int markerIndex)> thisClip,
            List<(SpriteClipDef clip, int markerIndex)> otherClips)
        {
            if (_profile?.Clips == null)
                return;
            for (int c = 0; c < _profile.Clips.Count; c++)
            {
                var source = _profile.Clips[c];
                if (source == null)
                    continue;
                source.EnsureEventMarkers();
                bool same = source == current ||
                    (current != null && string.Equals(source.Name, current.Name));
                for (int i = 0; i < source.EventMarkers.Count; i++)
                {
                    var marker = source.EventMarkers[i];
                    if (marker == null || marker.EventId == 0)
                        continue;
                    if (same)
                        thisClip.Add((source, i));
                    else
                        otherClips.Add((source, i));
                }
            }
        }

        bool DrawEventMarkerScope(List<(SpriteClipDef clip, int markerIndex)> group,
            ref bool expanded, string title, string description)
        {
            int selectedCount = 0;
            for (int i = 0; i < group.Count; i++)
            {
                if (EventMarkerIsSelected(group[i].clip, group[i].markerIndex))
                    selectedCount++;
            }

            string summary = $"{title}  ({group.Count})";
            if (selectedCount > 0)
                summary += $"  •  {selectedCount} selected";
            expanded = EditorGUILayout.Foldout(expanded, new GUIContent(summary, description), true);
            if (!expanded)
                return false;

            GUILayout.Label(description, _mutedWrapStyle);
            if (group.Count == 0)
            {
                GUILayout.Label("No event markers in this scope.", _mutedStyle);
                GUILayout.Space(3f);
                return false;
            }

            for (int i = 0; i < group.Count; i++)
            {
                var owner = group[i].clip;
                int markerIndex = group[i].markerIndex;
                if (owner == null)
                    continue;
                owner.EnsureEventMarkers();
                if (markerIndex < 0 || markerIndex >= owner.EventMarkers.Count)
                    continue;
                var marker = owner.EventMarkers[markerIndex];
                if (marker == null || marker.EventId == 0)
                    continue;
                int frame = marker.FrameIndex;
                bool selected = EventMarkerIsSelected(owner, markerIndex);
                bool detailsOpen = selected && _eventRowDetailsExpanded;
                bool here = owner == CurrentClip && frame == _selectedFrame;
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(here && selected))
                    {
                        if (GUILayout.Button(EventGoToContent(),
                            EditorStyles.miniButton, GUILayout.Width(27f), GUILayout.Height(22f)))
                        {
                            JumpToEventHome(owner, markerIndex, frame);
                            Repaint();
                        }
                    }

                    Color previous = GUI.backgroundColor;
                    if (selected)
                        GUI.backgroundColor = AccentColor;
                    string fire = marker.FiresOnce ? "Once" : "Loop";
                    marker.EnsurePayloads();
                    string payloadBit = marker.Payloads.Count == 0
                        ? string.Empty
                        : marker.Payloads.Count == 1
                            ? "  •  1 payload"
                            : $"  •  {marker.Payloads.Count} payloads";
                    string label = here
                        ? $"{i + 1}. {EventName(marker.EventId)}  •  Frame {frame + 1}  •  {EventAuthoredTime(owner, marker):0.000}s  •  {fire}{payloadBit}"
                        : $"{i + 1}. {EventName(marker.EventId)}  •  {EventMarkerHomeLabel(owner, frame)}  •  {fire}{payloadBit}";
                    bool rowClicked = GUILayout.Button(new GUIContent(label,
                            "Click to select and edit. Search jumps to this marker."),
                        EditorStyles.miniButton, GUILayout.Height(22f));
                    GUI.backgroundColor = previous;
                    if (rowClicked)
                    {
                        bool wasSole = selected && _eventRowDetailsExpanded;
                        SelectEventMarker(owner, markerIndex, EventAuthoredTime(owner, marker), jump: false);
                        _eventRowDetailsExpanded = !wasSole;
                        Repaint();
                    }

                    if (GUILayout.Button(new GUIContent("×", "Delete this event marker."),
                        EditorStyles.miniButton, GUILayout.Width(27f), GUILayout.Height(22f)))
                    {
                        RemoveEventMarkerAt(owner, markerIndex, focusPreview: owner == CurrentClip);
                        return true;
                    }
                }

                if (!detailsOpen)
                    continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(16f);
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        DrawEventMarkerDetails(owner, markerIndex);
                }
            }
            GUILayout.Space(4f);
            return false;
        }

        void DrawEventMarkerDetails(SpriteClipDef clip, int markerIndex)
        {
            if (clip == null)
                return;
            clip.EnsureEventMarkers();
            if (markerIndex < 0 || markerIndex >= clip.EventMarkers.Count)
                return;
            var marker = clip.EventMarkers[markerIndex];
            if (marker == null || marker.EventId == 0)
                return;
            int frame = Mathf.Clamp(marker.FrameIndex, 0, Mathf.Max(0, clip.Frames.Length - 1));

            int nextId = Mathf.Clamp(EditorGUILayout.IntField("Event ID", marker.EventId), 1, byte.MaxValue);
            if (nextId != marker.EventId)
            {
                RecordProfileUndo("Set Sprite Animation Event");
                marker.EventId = (byte)nextId;
                clip.SyncLegacyEventsFromMarkers();
                SaveDirty();
            }

            int fire = Mathf.Clamp(marker.FireMode, 0, 1);
            int nextFire = EditorGUILayout.Popup(
                new GUIContent("Fire", "Loop = every wrap. Once = until clip changes or Play()."),
                fire, new[] { "Loop", "Once" });
            if (nextFire != fire)
            {
                RecordProfileUndo("Set Sprite Animation Event Fire Mode");
                marker.FireMode = (byte)nextFire;
                clip.SyncLegacyEventsFromMarkers();
                SaveDirty();
            }

            float frameStart = AuthoredStartTime(clip, frame);
            float duration = FrameDuration(clip, frame);
            float exactTime = frameStart + Mathf.Clamp01(marker.NormalizedTime) * duration;
            float nextTime = EditorGUILayout.FloatField("Time (sec)", exactTime);
            if (!Mathf.Approximately(nextTime, exactTime))
            {
                float clampedTime = Mathf.Clamp(nextTime, frameStart,
                    Mathf.Max(frameStart, frameStart + duration - 0.0001f));
                RecordProfileUndo("Set Sprite Animation Event Time");
                marker.NormalizedTime = Mathf.Clamp01((clampedTime - frameStart) /
                    Mathf.Max(0.001f, duration));
                marker.FrameIndex = frame;
                clip.SyncLegacyEventsFromMarkers();
                _selectedEventFrame = frame;
                _selectedEventIndex = markerIndex;
                _selectedEventClipName = clip.Name;
                if (clip == CurrentClip)
                {
                    _selectedFrame = frame;
                    _previewTime = PreviewTimeForAuthoredTime(clip, clampedTime);
                }
                _status = $"Set {EventName(marker.EventId)} to {clampedTime:F3}s";
                SaveDirty();
            }

            DrawEventPayloadList(marker);

            DrawEventDefinition(marker.EventId);
            EditorGUILayout.LabelField("Binding", EventMarkerHomeLabel(clip, frame));
        }

        void DrawEventPayloadList(SpriteClipEventMarker marker)
        {
            marker.EnsurePayloads();
            GUILayout.Space(4f);
            EditorGUILayout.LabelField("PAYLOAD",
                $"{marker.Payloads.Count} / {SpriteEventPayloads.Max}",
                EditorStyles.miniBoldLabel);
            if (marker.Payloads.Count == 0)
            {
                GUILayout.Label(
                    "No payload. Named rows are struct fields. Type Asset to load a ScriptableObject, clip, or TextAsset.",
                    _mutedWrapStyle);
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Name", _mutedStyle, GUILayout.Width(72f));
                    GUILayout.Label("Type", _mutedStyle, GUILayout.Width(78f));
                    GUILayout.Label("Value", _mutedStyle);
                    GUILayout.Space(31f);
                }
            }

            for (int i = 0; i < marker.Payloads.Count; i++)
            {
                var entry = marker.Payloads[i];
                if (entry == null)
                    continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    string nextName = EditorGUILayout.TextField(entry.Name ?? string.Empty,
                        GUILayout.Width(72f), GUILayout.Height(18f));
                    if (nextName != (entry.Name ?? string.Empty))
                    {
                        RecordProfileUndo("Set Event Payload Name");
                        entry.Name = nextName;
                        SaveDirty();
                    }

                    int kind = Mathf.Clamp(entry.Kind, 0, EventPayloadKindLabels.Length - 1);
                    int nextKind = EditorGUILayout.Popup(kind, EventPayloadKindLabels,
                        GUILayout.Width(78f));
                    if (nextKind != kind)
                    {
                        RecordProfileUndo("Set Event Payload Type");
                        entry.Kind = (byte)nextKind;
                        marker.SyncConveniencePayloads();
                        SaveDirty();
                    }

                    DrawEventPayloadValue(marker, entry);

                    if (GUILayout.Button(new GUIContent("×", "Remove this payload."),
                        EditorStyles.miniButton, GUILayout.Width(27f), GUILayout.Height(18f)))
                    {
                        RecordProfileUndo("Remove Event Payload");
                        marker.RemovePayload(entry);
                        SaveDirty();
                        GUIUtility.ExitGUI();
                    }
                }
            }

            using (new EditorGUI.DisabledScope(marker.Payloads.Count >= SpriteEventPayloads.Max))
            {
                if (GUILayout.Button(new GUIContent("Add Payload",
                    marker.Payloads.Count >= SpriteEventPayloads.Max
                        ? $"Max {SpriteEventPayloads.Max} payloads per marker."
                        : "Add an Int, then change Type (Float2, Color, Byte, …)."),
                    EditorStyles.miniButton))
                {
                    RecordProfileUndo("Add Event Payload");
                    marker.AddPayload(SpriteEventPayloadKind.Int);
                    SaveDirty();
                }
            }
        }

        void DrawEventPayloadValue(SpriteClipEventMarker marker, SpriteEventPayloadEntry entry)
        {
            var kind = (SpriteEventPayloadKind)SpriteEventPayloads.ClampKind(entry.Kind);
            switch (kind)
            {
                case SpriteEventPayloadKind.Float:
                case SpriteEventPayloadKind.Half:
                    DrawPayloadFloats(marker, entry, 1);
                    break;
                case SpriteEventPayloadKind.Float2:
                    DrawPayloadFloats(marker, entry, 2);
                    break;
                case SpriteEventPayloadKind.Float3:
                    DrawPayloadFloats(marker, entry, 3);
                    break;
                case SpriteEventPayloadKind.Float4:
                    DrawPayloadFloats(marker, entry, 4);
                    break;
                case SpriteEventPayloadKind.Asset:
                    DrawPayloadAsset(marker, entry);
                    break;
                case SpriteEventPayloadKind.Color:
                {
                    var current = new Color(entry.FloatValue, entry.FloatY, entry.FloatZ, entry.FloatW);
                    if (entry.FloatW == 0f && entry.FloatValue == 0f && entry.FloatY == 0f &&
                        entry.FloatZ == 0f)
                        current.a = 1f;
                    Color next = EditorGUILayout.ColorField(GUIContent.none, current);
                    if (next != current)
                    {
                        RecordProfileUndo("Set Event Payload");
                        entry.FloatValue = next.r;
                        entry.FloatY = next.g;
                        entry.FloatZ = next.b;
                        entry.FloatW = next.a;
                        marker.SyncConveniencePayloads();
                        SaveDirty();
                    }
                    break;
                }
                case SpriteEventPayloadKind.Text:
                {
                    string next = EditorGUILayout.TextField(entry.TextValue ?? string.Empty);
                    if (next != (entry.TextValue ?? string.Empty))
                    {
                        RecordProfileUndo("Set Event Payload");
                        entry.TextValue = next;
                        marker.SyncConveniencePayloads();
                        SaveDirty();
                    }
                    break;
                }
                case SpriteEventPayloadKind.Bool:
                {
                    bool next = EditorGUILayout.Toggle(entry.IntValue != 0, GUILayout.Width(18f));
                    GUILayout.FlexibleSpace();
                    if (next != (entry.IntValue != 0))
                    {
                        RecordProfileUndo("Set Event Payload");
                        entry.IntValue = next ? 1 : 0;
                        marker.SyncConveniencePayloads();
                        SaveDirty();
                    }
                    break;
                }
                case SpriteEventPayloadKind.Byte:
                {
                    int next = Mathf.Clamp(EditorGUILayout.IntField(entry.IntValue), 0, 255);
                    if (next != entry.IntValue)
                    {
                        RecordProfileUndo("Set Event Payload");
                        entry.IntValue = next;
                        marker.SyncConveniencePayloads();
                        SaveDirty();
                    }
                    break;
                }
                case SpriteEventPayloadKind.Int2:
                    DrawPayloadInts(marker, entry, 2);
                    break;
                case SpriteEventPayloadKind.Int3:
                    DrawPayloadInts(marker, entry, 3);
                    break;
                case SpriteEventPayloadKind.Int4:
                    DrawPayloadInts(marker, entry, 4);
                    break;
                default:
                    DrawPayloadInts(marker, entry, 1);
                    break;
            }
        }

        void DrawPayloadInts(SpriteClipEventMarker marker, SpriteEventPayloadEntry entry, int count)
        {
            int x = entry.IntValue;
            int y = entry.IntY;
            int z = entry.IntZ;
            int w = entry.IntW;
            if (count >= 1) x = EditorGUILayout.IntField(x, GUILayout.MinWidth(28f));
            if (count >= 2) y = EditorGUILayout.IntField(y, GUILayout.MinWidth(28f));
            if (count >= 3) z = EditorGUILayout.IntField(z, GUILayout.MinWidth(28f));
            if (count >= 4) w = EditorGUILayout.IntField(w, GUILayout.MinWidth(28f));
            if (x == entry.IntValue && y == entry.IntY && z == entry.IntZ && w == entry.IntW)
                return;
            RecordProfileUndo("Set Event Payload");
            entry.IntValue = x;
            entry.IntY = y;
            entry.IntZ = z;
            entry.IntW = w;
            marker.SyncConveniencePayloads();
            SaveDirty();
        }

        void DrawPayloadFloats(SpriteClipEventMarker marker, SpriteEventPayloadEntry entry, int count)
        {
            float x = entry.FloatValue;
            float y = entry.FloatY;
            float z = entry.FloatZ;
            float w = entry.FloatW;
            if (count >= 1) x = EditorGUILayout.FloatField(x, GUILayout.MinWidth(28f));
            if (count >= 2) y = EditorGUILayout.FloatField(y, GUILayout.MinWidth(28f));
            if (count >= 3) z = EditorGUILayout.FloatField(z, GUILayout.MinWidth(28f));
            if (count >= 4) w = EditorGUILayout.FloatField(w, GUILayout.MinWidth(28f));
            if (Mathf.Approximately(x, entry.FloatValue) && Mathf.Approximately(y, entry.FloatY) &&
                Mathf.Approximately(z, entry.FloatZ) && Mathf.Approximately(w, entry.FloatW))
                return;
            RecordProfileUndo("Set Event Payload");
            entry.FloatValue = x;
            entry.FloatY = y;
            entry.FloatZ = z;
            entry.FloatW = w;
            marker.SyncConveniencePayloads();
            SaveDirty();
        }

        void DrawPayloadAsset(SpriteClipEventMarker marker, SpriteEventPayloadEntry entry)
        {
            UnityEngine.Object current = null;
            if (!string.IsNullOrEmpty(entry.AssetGuid))
            {
                string path = AssetDatabase.GUIDToAssetPath(entry.AssetGuid);
                if (!string.IsNullOrEmpty(path))
                    current = AssetDatabase.LoadMainAssetAtPath(path);
            }
            UnityEngine.Object next = EditorGUILayout.ObjectField(current, typeof(UnityEngine.Object), false);
            if (next == current)
                return;
            RecordProfileUndo("Set Event Payload Asset");
            if (next == null)
            {
                entry.AssetGuid = string.Empty;
                entry.TextValue = string.Empty;
            }
            else
            {
                string path = AssetDatabase.GetAssetPath(next);
                entry.AssetGuid = AssetDatabase.AssetPathToGUID(path);
                entry.TextValue = entry.AssetGuid;
                if (string.IsNullOrWhiteSpace(entry.Name))
                    entry.Name = next.name;
            }
            marker.SyncConveniencePayloads();
            SaveDirty();
        }

        void DrawEventTypeCatalog()
        {
            GUILayout.Space(6f);
            _profile.Events ??= new List<SpriteEventDef>();
            SyncEventTypeSelection();

            int unused = 0;
            var visible = new List<int>();
            for (int i = 0; i < _profile.Events.Count; i++)
            {
                var definition = _profile.Events[i];
                if (definition == null || definition.Id == 0)
                    continue;
                visible.Add(i);
                if (CountEventPlacements(definition.Id) == 0)
                    unused++;
            }

            int selectedCount = 0;
            for (int v = 0; v < visible.Count; v++)
            {
                if (_selectedEventTypeIndices.Contains(visible[v]))
                    selectedCount++;
            }

            string headerRight = unused > 0
                ? $"{_profile.Events.Count}  •  {unused} unused"
                : $"{_profile.Events.Count}";
            if (selectedCount > 0)
                headerRight += $"  •  {selectedCount} selected";

            var headerRect = EditorGUILayout.GetControlRect(false, 18f);
            EditorGUI.LabelField(headerRect, "EVENT TYPES", headerRight, EditorStyles.miniBoldLabel);
            var headerEvt = Event.current;
            if (headerEvt.type == EventType.MouseDown && headerEvt.button == 0 &&
                headerRect.Contains(headerEvt.mousePosition))
            {
                ClearEventTypeSelection();
                headerEvt.Use();
                Repaint();
            }

            const float rowH = 20f;
            const float delW = ClipRowDeleteWidth;
            int pendingDelete = -1;
            Color selectedColor = new Color(0.12f, 0.34f, 0.47f, 1f);

            for (int v = 0; v < visible.Count; v++)
            {
                int index = visible[v];
                var definition = _profile.Events[index];
                int placed = CountEventPlacements(definition.Id);
                bool isPrimary = index == _selectedEventTypeIndex;
                bool isSelected = _selectedEventTypeIndices.Contains(index) || isPrimary;
                bool renaming = _renamingEventId != 0 && _renamingEventId == definition.Id;

                var row = GUILayoutUtility.GetRect(0f, rowH, GUILayout.ExpandWidth(true), GUILayout.Height(rowH));
                EditorGUI.DrawRect(row, isSelected || renaming ? selectedColor : PanelAltColor);

                var deleteRect = new Rect(row.xMax - delW - 2f, row.y + 2f, delW, 16f);
                var nameRect = new Rect(row.x + 4f, row.y + 2f,
                    Mathf.Max(20f, deleteRect.x - row.x - 6f), 16f);

                string label = placed == 0
                    ? $"{definition.Name}  •  ID {definition.Id}  •  not placed"
                    : $"{definition.Name}  •  ID {definition.Id}  •  {placed} placed";

                if (renaming)
                {
                    DrawInlineRenameField(nameRect, EventRenameControl,
                        ref _renameEventValue, ref _focusEventRename, EditorStyles.textField);
                }
                else
                {
                    string tip = isSelected
                        ? $"{definition.Name}\nSelected. Click again to unselect. Ctrl/Cmd multi, Shift range. F2 / double-click to rename. ✕ / Delete removes."
                        : $"{definition.Name}\nClick to select. Ctrl/Cmd toggle, Shift range. F2 / double-click to rename.";
                    GUI.Label(nameRect, new GUIContent(label, tip),
                        isSelected ? EditorStyles.whiteLabel : _mutedStyle);
                }

                if (!renaming)
                {
                    Color prevGui = GUI.color;
                    if (deleteRect.Contains(Event.current.mousePosition))
                        GUI.color = new Color(1f, 0.42f, 0.42f, 1f);
                    if (GUI.Button(deleteRect,
                        new GUIContent("✕",
                            selectedCount > 1 && isSelected
                                ? $"Delete {selectedCount} selected event types."
                                : "Delete event type"),
                        EditorStyles.miniButton))
                        pendingDelete = index;
                    GUI.color = prevGui;
                }

                var input = Event.current;
                if (!renaming &&
                    input.type == EventType.MouseDown &&
                    input.button == 0 &&
                    row.Contains(input.mousePosition) &&
                    !deleteRect.Contains(input.mousePosition))
                {
                    bool toggle = input.control || input.command;
                    bool range = input.shift;
                    SelectEventTypeCard(index, visible, toggle, range);
                    if (!toggle && !range && input.clickCount >= 2)
                    {
                        BeginEventRename(definition.Id);
                        input.Use();
                        GUIUtility.ExitGUI();
                    }
                    input.Use();
                    Repaint();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Add Event Type",
                    "Create a new named Event ID for the timeline right-click menu.")))
                {
                    byte id = NextEventId();
                    if (id != 0)
                    {
                        RecordProfileUndo("Add Sprite Event Type");
                        _profile.Events.Add(new SpriteEventDef { Id = id, Name = $"Event {id}" });
                        int added = _profile.Events.Count - 1;
                        SelectEventTypeCard(added, null, false, false);
                        SaveDirty();
                    }
                }
                using (new EditorGUI.DisabledScope(selectedCount == 0))
                {
                    string delLabel = selectedCount > 1
                        ? $"Delete {selectedCount}"
                        : "Delete";
                    if (GUILayout.Button(new GUIContent(delLabel,
                        selectedCount > 1
                            ? $"Delete {selectedCount} selected event types."
                            : "Delete selected event type."),
                        GUILayout.Width(selectedCount > 1 ? 88f : 64f)))
                    {
                        pendingDelete = _selectedEventTypeIndex >= 0
                            ? _selectedEventTypeIndex
                            : (selectedCount > 0 ? FirstSelectedEventTypeIndex() : -1);
                    }
                }
            }

            if (pendingDelete >= 0)
            {
                if (_renamingEventId != 0)
                    CancelEventRename();
                SyncEventTypeSelection();
                if (_selectedEventTypeIndices.Count > 1 &&
                    _selectedEventTypeIndices.Contains(pendingDelete))
                {
                    int n = _selectedEventTypeIndices.Count;
                    if (EditorUtility.DisplayDialog(
                        "Delete Event Types",
                        $"Delete {n} selected event types?\nMarkers and socket triggers that use these IDs will also be removed. This cannot be undone except via Undo.",
                        "Delete", "Cancel"))
                        DeleteSelectedEventTypes();
                }
                else
                {
                    if (!_selectedEventTypeIndices.Contains(pendingDelete))
                        SelectEventTypeCard(pendingDelete, visible, false, false);
                    DeleteSelectedEventTypes();
                }
            }
        }

        void SyncEventTypeSelection()
        {
            if (_profile?.Events == null)
            {
                _selectedEventTypeIndices.Clear();
                _selectedEventTypeIndex = -1;
                _eventTypeListAnchor = -1;
                return;
            }
            _selectedEventTypeIndices.RemoveWhere(i =>
                i < 0 || i >= _profile.Events.Count ||
                _profile.Events[i] == null || _profile.Events[i].Id == 0);
            if (_eventTypeListAnchor >= 0 &&
                (_eventTypeListAnchor >= _profile.Events.Count ||
                 _profile.Events[_eventTypeListAnchor] == null ||
                 _profile.Events[_eventTypeListAnchor].Id == 0))
                _eventTypeListAnchor = -1;
            if (_selectedEventTypeIndex >= 0 &&
                _selectedEventTypeIndex < _profile.Events.Count &&
                _profile.Events[_selectedEventTypeIndex] != null &&
                _profile.Events[_selectedEventTypeIndex].Id != 0)
            {
                if (_selectedEventTypeIndices.Count == 0)
                    _selectedEventTypeIndices.Add(_selectedEventTypeIndex);
                else if (!_selectedEventTypeIndices.Contains(_selectedEventTypeIndex))
                {
                    _selectedEventTypeIndices.Clear();
                    _selectedEventTypeIndices.Add(_selectedEventTypeIndex);
                }
            }
            else if (_selectedEventTypeIndices.Count > 0)
            {
                int primary = int.MaxValue;
                foreach (int i in _selectedEventTypeIndices)
                    if (i < primary) primary = i;
                _selectedEventTypeIndex = primary == int.MaxValue ? -1 : primary;
            }
            else
                _selectedEventTypeIndex = -1;
        }

        int FirstSelectedEventTypeIndex()
        {
            int best = int.MaxValue;
            foreach (int i in _selectedEventTypeIndices)
                if (i < best) best = i;
            return best == int.MaxValue ? -1 : best;
        }

        void ClearEventTypeSelection()
        {
            _selectedEventTypeIndices.Clear();
            _selectedEventTypeIndex = -1;
            _eventTypeListAnchor = -1;
        }

        void SelectEventTypeCard(int index, List<int> visible, bool toggle, bool range)
        {
            if (_profile?.Events == null || index < 0 || index >= _profile.Events.Count)
                return;
            var definition = _profile.Events[index];
            if (definition == null || definition.Id == 0)
                return;
            if (_renamingEventId != 0 && _renamingEventId != definition.Id)
                CommitEventRename();
            if (IsRenamingAnything() && _renamingEventId == 0)
                CommitAllRenames();

            visible ??= BuildVisibleEventTypeIndices();

            if (range && _eventTypeListAnchor >= 0)
            {
                int anchorPos = visible.IndexOf(_eventTypeListAnchor);
                int indexPos = visible.IndexOf(index);
                if (anchorPos < 0)
                    anchorPos = indexPos;
                if (indexPos >= 0 && anchorPos >= 0)
                {
                    int a = Mathf.Min(anchorPos, indexPos);
                    int b = Mathf.Max(anchorPos, indexPos);
                    if (!toggle)
                        _selectedEventTypeIndices.Clear();
                    for (int p = a; p <= b; p++)
                        _selectedEventTypeIndices.Add(visible[p]);
                    _selectedEventTypeIndex = index;
                    ReleaseShortcutKeyboardFocus();
                    return;
                }
            }

            if (toggle)
            {
                if (_selectedEventTypeIndices.Contains(index) && _selectedEventTypeIndices.Count > 1)
                {
                    _selectedEventTypeIndices.Remove(index);
                    if (_selectedEventTypeIndex == index)
                        _selectedEventTypeIndex = FirstSelectedEventTypeIndex();
                    _eventTypeListAnchor = index;
                    ReleaseShortcutKeyboardFocus();
                    return;
                }
                if (_selectedEventTypeIndices.Contains(index) && _selectedEventTypeIndices.Count == 1)
                {
                    ClearEventTypeSelection();
                    ReleaseShortcutKeyboardFocus();
                    return;
                }
                _selectedEventTypeIndices.Add(index);
                _selectedEventTypeIndex = index;
                _eventTypeListAnchor = index;
                ReleaseShortcutKeyboardFocus();
                return;
            }

            // Exclusive select — click again on the sole selection clears it.
            if (_selectedEventTypeIndex == index &&
                _selectedEventTypeIndices.Count == 1 &&
                _selectedEventTypeIndices.Contains(index))
            {
                ClearEventTypeSelection();
                if (!IsRenamingAnything())
                    ReleaseShortcutKeyboardFocus();
                return;
            }

            _selectedEventTypeIndices.Clear();
            _selectedEventTypeIndices.Add(index);
            _selectedEventTypeIndex = index;
            _eventTypeListAnchor = index;
            ReleaseShortcutKeyboardFocus();
        }

        List<int> BuildVisibleEventTypeIndices()
        {
            var visible = new List<int>();
            if (_profile?.Events == null)
                return visible;
            for (int i = 0; i < _profile.Events.Count; i++)
            {
                var definition = _profile.Events[i];
                if (definition == null || definition.Id == 0)
                    continue;
                visible.Add(i);
            }
            return visible;
        }

        void DeleteSelectedEventTypes()
        {
            SyncEventTypeSelection();
            if (_selectedEventTypeIndices.Count == 0 || _profile?.Events == null)
                return;

            var ordered = new List<int>(_selectedEventTypeIndices);
            ordered.Sort();
            var deletedIds = new HashSet<byte>();
            for (int i = 0; i < ordered.Count; i++)
            {
                int index = ordered[i];
                if (index < 0 || index >= _profile.Events.Count)
                    continue;
                var definition = _profile.Events[index];
                if (definition == null || definition.Id == 0)
                    continue;
                deletedIds.Add(definition.Id);
            }
            if (deletedIds.Count == 0)
                return;

            string undoName = deletedIds.Count == 1
                ? "Delete Sprite Event Type"
                : "Delete Sprite Event Types";
            SyncWorkingProfileToAsset();
            RecordDiscreteUndo(undoName);

            int removedMarkers = RemoveMarkersAndTriggersForEventIds(deletedIds);

            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                int index = ordered[i];
                if (index < 0 || index >= _profile.Events.Count)
                    continue;
                var definition = _profile.Events[index];
                if (definition == null || !deletedIds.Contains(definition.Id))
                    continue;
                if (_renamingEventId == definition.Id)
                    ClearEventRename();
                _profile.Events.RemoveAt(index);
            }

            ClearEventTypeSelection();
            SyncWorkingProfileToAsset();
            _status = removedMarkers > 0
                ? $"Deleted {deletedIds.Count} event type{(deletedIds.Count == 1 ? string.Empty : "s")} and {removedMarkers} placement{(removedMarkers == 1 ? string.Empty : "s")}"
                : $"Deleted {deletedIds.Count} event type{(deletedIds.Count == 1 ? string.Empty : "s")}";
            SaveDirty();
            SealUndoGroup();
            Repaint();
        }

        int RemoveMarkersAndTriggersForEventIds(HashSet<byte> deletedIds)
        {
            int removed = 0;
            if (deletedIds == null || deletedIds.Count == 0)
                return 0;

            if (_profile?.Clips != null)
            {
                for (int c = 0; c < _profile.Clips.Count; c++)
                {
                    var clip = _profile.Clips[c];
                    if (clip == null)
                        continue;
                    clip.EnsureEventMarkers();
                    bool changed = false;
                    for (int m = clip.EventMarkers.Count - 1; m >= 0; m--)
                    {
                        var marker = clip.EventMarkers[m];
                        if (marker == null || !deletedIds.Contains(marker.EventId))
                            continue;
                        clip.RemoveEventMarker(marker);
                        removed++;
                        changed = true;
                    }
                    if (changed)
                        clip.SyncLegacyEventsFromMarkers();
                }
            }

            if (_profile?.SocketMotions != null)
            {
                for (int t = 0; t < _profile.SocketMotions.Count; t++)
                {
                    var track = _profile.SocketMotions[t];
                    if (track?.Triggers == null)
                        continue;
                    for (int i = track.Triggers.Count - 1; i >= 0; i--)
                    {
                        var trigger = track.Triggers[i];
                        if (trigger == null || !deletedIds.Contains(trigger.EventId))
                            continue;
                        track.Triggers.RemoveAt(i);
                        removed++;
                    }
                }
            }

            if (_selectedEventIndex >= 0 || _selectedEventFrame >= 0)
                PruneEventSelection(CurrentClip);
            if (_selectedSocketTriggerTrack >= 0)
            {
                _selectedSocketTriggerTrack = -1;
                _selectedSocketTriggerIndex = -1;
            }
            return removed;
        }

        int CountEventPlacements(byte eventId)
        {
            if (eventId == 0 || _profile?.Clips == null)
                return 0;
            int count = 0;
            for (int c = 0; c < _profile.Clips.Count; c++)
            {
                var source = _profile.Clips[c];
                if (source == null)
                    continue;
                source.EnsureEventMarkers();
                for (int i = 0; i < source.EventMarkers.Count; i++)
                {
                    var marker = source.EventMarkers[i];
                    if (marker != null && marker.EventId == eventId)
                        count++;
                }
            }
            return count;
        }

        static string EventMarkerHomeLabel(SpriteClipDef clip, int frame)
        {
            string clipName = clip == null || string.IsNullOrEmpty(clip.Name) ? "Clip" : clip.Name;
            return $"{clipName}  •  Frame {frame + 1}";
        }

        bool EventMarkerIsSelected(SpriteClipDef clip, int markerIndex)
        {
            return clip != null &&
                markerIndex >= 0 &&
                _selectedEventIndex == markerIndex &&
                !string.IsNullOrEmpty(_selectedEventClipName) &&
                string.Equals(_selectedEventClipName, clip.Name);
        }

        SpriteClipEventMarker SelectedEventMarker(SpriteClipDef clip)
        {
            if (clip == null)
                return null;
            clip.EnsureEventMarkers();
            if (_selectedEventIndex < 0 || _selectedEventIndex >= clip.EventMarkers.Count)
                return null;
            return clip.EventMarkers[_selectedEventIndex];
        }

        void JumpToEventHome(SpriteClipDef clip, int markerIndex, int fallbackFrame)
        {
            if (clip == null || _profile?.Clips == null || _profile.Clips.Count == 0)
                return;
            clip.EnsureFrameData();
            clip.EnsureEventMarkers();
            SpriteClipEventMarker marker = null;
            if (markerIndex >= 0 && markerIndex < clip.EventMarkers.Count)
                marker = clip.EventMarkers[markerIndex];
            int frame = marker != null ? marker.FrameIndex : fallbackFrame;
            if (frame < 0 || frame >= clip.Frames.Length)
                return;

            int clipIndex = FindClipIndexByName(clip.Name);
            if (clipIndex < 0)
            {
                for (int i = 0; i < _profile.Clips.Count; i++)
                {
                    if (_profile.Clips[i] == clip)
                    {
                        clipIndex = i;
                        break;
                    }
                }
            }
            if (clipIndex < 0)
                return;

            if (_renamingClip >= 0 && _renamingClip != clipIndex)
                CancelClipRename();
            if (_renamingSheet >= 0)
                CommitSheetRename();

            if (_selectedClip != clipIndex)
            {
                _selectedOnionFrame = -1;
                ClearColliderSelection();
                ClearSocketSelection();
                _selectedClip = clipIndex;
                var destClip = _profile.Clips[clipIndex];
                if (destClip != null && _profile.Sheets != null && _profile.Sheets.Count > 0)
                {
                    _selectedSheet = Mathf.Clamp(destClip.SheetIndex, 0, _profile.Sheets.Count - 1);
                    _collapsedSheets.Remove(_selectedSheet);
                    _profile.SyncLegacyFromSheet(_selectedSheet);
                    InvalidateSheetPixelCache();
                }
            }

            SelectOnlyFrame(frame);
            _selectedEventFrame = marker != null ? frame : -1;
            _selectedEventIndex = marker != null ? markerIndex : -1;
            _selectedEventClipName = marker != null ? clip.Name : null;
            float authored = marker != null ? EventAuthoredTime(clip, marker) : AuthoredStartTime(clip, frame);
            _previewTime = PreviewTimeForAuthoredTime(clip, authored);
            _playing = false;
            _eventThisClipExpanded = true;
            _eventRowDetailsExpanded = true;
            _status = marker == null
                ? $"Jumped to {EventMarkerHomeLabel(clip, frame)}"
                : $"Jumped to {EventName(marker.EventId)}  •  {EventMarkerHomeLabel(clip, frame)}";
            ReleaseShortcutKeyboardFocus();
            Repaint();
        }

    }
}

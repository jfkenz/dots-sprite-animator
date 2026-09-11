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


        void DrawSocketDrawKeyInspector(SpriteClipDef clip)
        {
            if (clip == null || _selectedSocketDrawFrame < 0 ||
                string.IsNullOrEmpty(_selectedSocketDrawName))
                return;
            var key = SpriteSocketKeys.FindOnFrame(
                clip.Sockets, _selectedSocketDrawName, _selectedSocketDrawFrame);
            if (key == null || key.DrawLayer == SpriteSocketKeys.DrawUnset)
            {
                _selectedSocketDrawFrame = -1;
                _selectedSocketDrawName = null;
                return;
            }

            GUILayout.Space(9f);
            SectionLabel("SOCKET DRAW KEY");
            EditorGUILayout.LabelField("Socket", _selectedSocketDrawName);
            float time = AuthoredStartTime(clip, _selectedSocketDrawFrame);
            float nextTime = EditorGUILayout.DelayedFloatField(
                new GUIContent("Time (sec)",
                    "Type a time to move this Socket Draw key. Snaps to the frame at that time."),
                time);
            EditorGUILayout.LabelField("Frame", $"{_selectedSocketDrawFrame + 1} of {clip.Frames.Length}");
            if (!Mathf.Approximately(nextTime, time))
            {
                MoveSocketDrawKeyToTime(clip, _selectedSocketDrawFrame, _selectedSocketDrawName,
                    key.DrawLayer, nextTime);
                return;
            }
            int popup = key.DrawLayer == SpriteSocketKeys.DrawBehind ? 0
                : key.DrawLayer == SpriteSocketKeys.DrawFront ? 1 : 2;
            int next = EditorGUILayout.Popup(
                new GUIContent("Draw", "Behind = purple. Front = amber. Default = catalog."),
                popup, new[] { "Behind", "In Front", "Default" });
            if (next != popup)
            {
                RecordProfileUndo("Edit Socket Draw Key");
                key.DrawLayer = next == 0
                    ? SpriteSocketKeys.DrawBehind
                    : next == 1
                        ? SpriteSocketKeys.DrawFront
                        : SpriteSocketKeys.DrawCatalog;
                SaveDirty();
            }
            if (GUILayout.Button("Delete Socket Draw Key"))
                ClearSocketDrawKey(clip, _selectedSocketDrawFrame, _selectedSocketDrawName);
        }

        void DrawEventDefinition(byte eventId)
        {
            if (eventId == 0)
            {
                EditorGUILayout.LabelField("Event", "None");
                return;
            }

            var definition = _profile.Events.Find(e => e.Id == eventId);
            if (definition == null)
            {
                RecordProfileUndo("Create Event Definition");
                definition = new SpriteEventDef { Id = eventId, Name = $"Event {eventId}" };
                _profile.Events.Add(definition);
                SaveDirty();
            }
            bool renamingEvent = _renamingEventId != 0 && _renamingEventId == definition.Id;
            if (renamingEvent)
            {
                // EVENT TYPES list owns EventRenameControl — a second field with the
                // same control name steals focus every frame (same class as clip rename).
                EditorGUILayout.LabelField("Event Name", _renameEventValue);
            }
            else if (DrawRenameLabelRow("Event Name", definition.Name,
                "Double-click to rename this event type.", out _))
            {
                int typeIndex = FindEventTypeIndex(definition.Id);
                if (typeIndex >= 0)
                    QueueEventTypeRename(typeIndex);
                else
                    BeginEventRename(definition.Id);
                GUIUtility.ExitGUI();
            }
            definition.Color = EditorGUILayout.ColorField("Event Color", definition.Color);
        }

        void DrawSocketInspector(SpriteClipDef clip)
        {
            GUILayout.Space(9f);
            clip.Sockets ??= new List<FrameSocketDef>();
            _profile.EnsureSocketCatalog();
            PruneSocketSelection(clip);

            bool nextShowPreviews = EditorGUILayout.Toggle(
                new GUIContent("Show Previews",
                    "Draw assigned socket images on the preview canvas. Turn off while placing sockets."),
                _showSocketPreviews);
            if (nextShowPreviews != _showSocketPreviews)
            {
                RecordWindowUndo("Toggle Socket Previews");
                _showSocketPreviews = nextShowPreviews;
            }

            DrawSocketSection(clip, independentView: false);
            GUILayout.Space(12f);
            DrawSocketSection(clip, independentView: true);
        }

        void DrawIndependentTimelineSettings()
        {
            EditorGUI.BeginChangeCheck();
            float duration = Mathf.Max(0.01f,
                EditorGUILayout.FloatField("Duration (sec)", _profile.IndependentMotionDuration));
            float speed = Mathf.Max(0.01f,
                EditorGUILayout.FloatField("Playback Speed", _profile.IndependentMotionSpeed));
            bool loop = EditorGUILayout.Toggle("Loop Timeline", _profile.IndependentMotionLoop);
            if (EditorGUI.EndChangeCheck())
            {
                RecordProfileUndo("Edit Independent Motion Timeline");
                _profile.IndependentMotionDurationSeconds = duration;
                _profile.IndependentTimelineUsesSeconds = true;
                _profile.IndependentMotionSpeed = speed;
                _profile.IndependentMotionLoop = loop;
                _profile.EnsureSocketMotions();
                _socketPreviewTime = Mathf.Clamp(
                    _socketPreviewTime, 0f, _profile.IndependentMotionDuration);
                SaveDirty();
            }

            DrawIndependentKeyStepSettings();
        }

        void DrawIndependentKeyStepSettings()
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Key Insertion", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            var mode = (IndependentKeyStepMode)EditorGUILayout.EnumPopup(
                new GUIContent("Step Mode", "Choose a seconds or authoring-frame offset."),
                _independentKeyStepMode);
            float seconds = _independentKeyStepSeconds;
            float fps = _independentKeyStepFps;
            if (mode == IndependentKeyStepMode.Seconds)
                seconds = Mathf.Max(0.001f, EditorGUILayout.FloatField(
                    new GUIContent("Seconds Step", "Seconds advanced by one step."),
                    seconds));
            else
                fps = Mathf.Max(1f, EditorGUILayout.FloatField(
                    new GUIContent("Step FPS", "Authoring grid only; runtime remains continuous."),
                    fps));
            int count = Mathf.Max(1, EditorGUILayout.IntField(
                new GUIContent("Step Count", "Number of seconds steps or frames to advance."),
                _independentKeyStepCount));
            if (EditorGUI.EndChangeCheck())
            {
                RecordWindowUndo("Edit Independent Key Step");
                _independentKeyStepMode = mode;
                _independentKeyStepSeconds = seconds;
                _independentKeyStepFps = fps;
                _independentKeyStepCount = count;
            }
            EditorGUILayout.LabelField(
                $"Next offset: +{ResolvedIndependentKeyStepSeconds():0.###}s",
                _mutedStyle);
        }

        float ResolvedIndependentKeyStepSeconds()
        {
            return SpriteSocketMotionTimeUtility.ResolveStepSeconds(
                _independentKeyStepMode == IndependentKeyStepMode.Frames,
                _independentKeyStepSeconds,
                _independentKeyStepFps,
                _independentKeyStepCount);
        }

        string IndependentKeyStepLabel()
        {
            if (_independentKeyStepMode == IndependentKeyStepMode.Frames)
                return $"+{_independentKeyStepCount} frame{Plural(_independentKeyStepCount)}";
            return $"+{ResolvedIndependentKeyStepSeconds():0.###}s";
        }

        void DrawSocketSection(SpriteClipDef clip, bool independentView)
        {
            SectionLabel(independentView
                ? "INDEPENDENT MOTION"
                : $"SOCKET — FRAME ATTACHED — FRAME {_selectedFrame + 1}");
            var names = VisibleSocketNames(clip, independentView);
            int visibleSelected = CountSelectedSocketNames(names);
            GUILayout.Label(
                $"{names.Count} {(independentView ? "independent track" : "frame-attached socket")}{Plural(names.Count)} • {visibleSelected} selected",
                _mutedStyle);
            if (independentView)
                DrawIndependentTimelineSettings();

            bool sectionArmed = _socketPlacementArmed &&
                                _socketPlacementIndependent == independentView;
            Color previous = GUI.backgroundColor;
            if (sectionArmed)
                GUI.backgroundColor = new Color(0.18f, 0.55f, 0.82f, 1f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(sectionArmed
                        ? "Click Preview to Place…"
                            : independentView ? "Add Motion Track" : "Add Frame Socket"))
                {
                    if (sectionArmed)
                        CancelSocketPlacement("Socket placement cancelled");
                    else
                    {
                        if (_socketPlacementArmed)
                            CancelSocketPlacement(null);
                        ArmSocketPlacement(independentView);
                    }
                }
                GUI.backgroundColor = previous;
                using (new EditorGUI.DisabledScope(names.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("Select All",
                            $"Select every {(independentView ? "independent motion track" : "frame-attached socket")} in this section."),
                            GUILayout.Width(72f)))
                        SelectAllVisibleSockets(names, independentView);
                }
                using (new EditorGUI.DisabledScope(visibleSelected == 0))
                {
                    if (GUILayout.Button(new GUIContent("Delete",
                            "Delete selected sockets. Delete / Backspace also works."), GUILayout.Width(56f)))
                    {
                        _selectedSockets.RemoveWhere(selected =>
                            !ListContainsSocketName(names, selected));
                        SyncSocketPrimaryFromSelection();
                        _selectedColliders.Clear();
                        ClearColliderTransform();
                        DeleteSelectedPreviewObjects();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            GUI.backgroundColor = previous;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (!independentView)
                {
                    using (new EditorGUI.DisabledScope(names.Count == 0))
                    {
                        if (GUILayout.Button(new GUIContent("Delete This Clip…",
                                "Remove every Frame-Attached socket from this clip only. Independent Motion tracks remain.")))
                            DeleteAllFrameAttachedSockets(clip);
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(names.Count == 0))
                    {
                        if (GUILayout.Button(new GUIContent("Delete Independent…",
                                "Remove every Independent Motion track and its legacy clip keys.")))
                            DeleteAllIndependentSockets();
                    }
                }
                using (new EditorGUI.DisabledScope(!HasAnySocketData()))
                {
                    if (GUILayout.Button(new GUIContent("Delete All — All Clips…",
                            "Profile-wide reset: remove Frame-Attached sockets, Independent Motion tracks, and socket catalog entries from every clip.")))
                        DeleteAllSocketsAcrossProfile();
                }
            }

            if (sectionArmed)
            {
                EditorGUILayout.HelpBox(
                    independentView
                        ? "Click the preview to place an Independent Motion socket. Its offset is measured from the player pivot. Escape or right-click cancels."
                        : "Click the frame to place a Frame-Attached socket. Escape or right-click cancels.",
                    MessageType.Info);
            }

            if (independentView)
                DrawEllipticalOrbitTools(clip);

            if (names.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Add Socket and click the preview, or pick a Pattern and Create below.",
                    MessageType.None);
                return;
            }

            EditorGUILayout.HelpBox(
                "Preview only. Scene attachments are still created manually (or later from this catalog).",
                MessageType.None);

            DrawSocketSelectionProfileField();
            GUILayout.Label(
                "Click or drag a box. Shift = range, Ctrl/Cmd = toggle, Alt = subtract, Shift+Alt = intersect.",
                _mutedStyle);

            _socketListRowRects.Clear();
            DrawSocketInventoryList(clip, names, independentView);
            if ((!_socketListMarqueePending && !_socketListMarqueeActive) ||
                _socketListMarqueeIndependent == independentView)
                HandleSocketListMarquee(names);

            if (visibleSelected == 1 &&
                !SocketSelectionBusy &&
                !string.IsNullOrEmpty(_selectedSocketName) &&
                IsSocketSelected(_selectedSocketName) &&
                ListContainsSocketName(names, _selectedSocketName))
            {
                GUILayout.Space(8f);
                GUILayout.Label($"SOCKET  {_selectedSocketName}", _sectionStyle);
                DrawSocketIdentityInspector(clip, _selectedSocketName);
            }
            else if (visibleSelected > 1)
            {
                GUILayout.Space(6f);
                GUILayout.Label(
                    $"{visibleSelected} selected  •  right-click for Transform / Pattern",
                    _mutedStyle);
            }
            DrawQuickMotionPresets(clip, independentView, names);
            if (independentView)
            {
                DrawSelectedSocketMotionKeyInspector();
                DrawSelectedSocketTriggerInspector();
            }
        }

        void DrawSelectedSocketMotionKeyInspector()
        {
            if (!TryGetSocketMotionKey(
                    _selectedSocketMotionTrack, _selectedSocketMotionKey,
                    out var track, out var key))
                return;
            CollectIndependentMotionEditKeys(key);
            int selectedCount = _independentMotionEditKeys.Count;
            GUILayout.Space(8f);
            SectionLabel(selectedCount > 1
                ? $"MOTION KEY  •  {selectedCount} SELECTED"
                : "MOTION KEY");
            if (selectedCount > 1)
                GUILayout.Label(
                    "Changing one option updates selected keys only. Other fields stay as they are.",
                    _mutedStyle);

            SpriteEaseMode currentEase = SpriteEase.IsValidMode(key.EaseMode)
                ? (SpriteEaseMode)key.EaseMode
                : SpriteEaseMode.SmoothStep;
            var currentPath = key.PathMode <= (byte)SpriteSocketPathMode.None
                ? (SpriteSocketPathMode)key.PathMode
                : SpriteSocketPathMode.SmoothPath;
            var currentRotation = key.RotationMode <=
                                  (byte)SpriteSocketRotationMode.None
                ? (SpriteSocketRotationMode)key.RotationMode
                : SpriteSocketRotationMode.Shortest;

            EditorGUI.showMixedValue = HasMixedIndependentMotionEase();
            EditorGUI.BeginChangeCheck();
            var ease = (SpriteEaseMode)EditorGUILayout.EnumPopup(
                new GUIContent("Timing Ease",
                    "Timing from this key to the next key."), currentEase);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                ApplyIndependentMotionField(
                    "Set Independent Motion Easing", key, track,
                    IndependentMotionApplyScope.Selected, ease: ease);

            EditorGUI.showMixedValue = HasMixedIndependentMotionPath();
            EditorGUI.BeginChangeCheck();
            var pathMode = (SpriteSocketPathMode)EditorGUILayout.EnumPopup(
                new GUIContent("Position Path",
                    "Spatial interpolation from this key to the next."),
                currentPath);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                ApplyIndependentMotionField(
                    "Set Independent Motion Position Path", key, track,
                    IndependentMotionApplyScope.Selected, pathMode: pathMode);

            if (pathMode is SpriteSocketPathMode.CubicBezier or
                SpriteSocketPathMode.Hermite)
            {
                EditorGUI.BeginChangeCheck();
                Vector2 inTangent = EditorGUILayout.Vector2Field(
                    "Incoming Handle", key.InTangent);
                Vector2 outTangent = EditorGUILayout.Vector2Field(
                    "Outgoing Handle", key.OutTangent);
                if (EditorGUI.EndChangeCheck())
                    ApplyIndependentMotionField(
                        "Edit Independent Motion Handles", key, track,
                        IndependentMotionApplyScope.Selected,
                        inTangent: inTangent, outTangent: outTangent);
            }
            else if (pathMode == SpriteSocketPathMode.Arc)
            {
                EditorGUI.BeginChangeCheck();
                float arcBulge = EditorGUILayout.FloatField(
                    "Arc Bulge (px)", key.ArcBulge);
                bool arcClockwise = EditorGUILayout.Toggle(
                    "Clockwise Arc", key.ArcClockwise);
                if (EditorGUI.EndChangeCheck())
                    ApplyIndependentMotionField(
                        "Edit Independent Motion Arc", key, track,
                        IndependentMotionApplyScope.Selected,
                        arcBulge: arcBulge, arcClockwise: arcClockwise);
            }

            EditorGUI.showMixedValue = HasMixedIndependentMotionRotation();
            EditorGUI.BeginChangeCheck();
            var rotationMode = (SpriteSocketRotationMode)EditorGUILayout.EnumPopup(
                new GUIContent("Rotation Mode",
                    "How rotation travels from this key to the next."),
                currentRotation);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                ApplyIndependentMotionField(
                    "Set Independent Motion Rotation", key, track,
                    IndependentMotionApplyScope.Selected,
                    rotationMode: rotationMode);

            if (rotationMode == SpriteSocketRotationMode.ContinuousTurns)
            {
                EditorGUI.BeginChangeCheck();
                int rotationTurns = EditorGUILayout.IntField(
                    "Turn Count", key.RotationTurns);
                if (EditorGUI.EndChangeCheck())
                    ApplyIndependentMotionField(
                        "Edit Independent Motion Turns", key, track,
                        IndependentMotionApplyScope.Selected,
                        rotationTurns: rotationTurns);
            }
            else if (rotationMode == SpriteSocketRotationMode.FacePath)
            {
                EditorGUI.BeginChangeCheck();
                float facingOffset = EditorGUILayout.FloatField(
                    "Facing Offset", key.FacingAngleOffset);
                if (EditorGUI.EndChangeCheck())
                    ApplyIndependentMotionField(
                        "Edit Independent Motion Facing Offset", key, track,
                        IndependentMotionApplyScope.Selected,
                        facingOffset: facingOffset);
            }

            EditorGUI.showMixedValue = HasMixedIndependentMotionOvershoot();
            EditorGUI.BeginChangeCheck();
            bool allowOvershoot = EditorGUILayout.Toggle(
                new GUIContent("Allow Overshoot",
                    "Allow Back, Elastic, or custom timing to pass/reverse endpoints."),
                key.AllowOvershoot);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                ApplyIndependentMotionField(
                    "Toggle Independent Motion Overshoot", key, track,
                    IndependentMotionApplyScope.Selected,
                    allowOvershoot: allowOvershoot);

            EditorGUI.showMixedValue = HasMixedIndependentMotionCustomEase();
            EditorGUI.BeginChangeCheck();
            bool useCustomEase = EditorGUILayout.Toggle(
                new GUIContent("Custom Curve",
                    "Override the timing preset with an editable sampled curve."),
                key.UseCustomEase);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                ApplyIndependentMotionField(
                    "Toggle Independent Motion Custom Curve", key, track,
                    IndependentMotionApplyScope.Selected,
                    useCustomEase: useCustomEase);
            AnimationCurve customCurve = CloneAnimationCurve(key.CustomEaseCurve);
            if (useCustomEase)
            {
                customCurve ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
                EditorGUI.BeginChangeCheck();
                customCurve = EditorGUILayout.CurveField(
                    new GUIContent("Ease Curve",
                        "The curve is sampled into eight Burst-compatible values."),
                    customCurve, Color.cyan, new Rect(0f, 0f, 1f, 1f));
                if (EditorGUI.EndChangeCheck())
                    ApplyIndependentMotionField(
                        "Edit Independent Motion Ease Curve", key, track,
                        IndependentMotionApplyScope.Selected,
                        useCustomEase: true, customCurve: customCurve);
            }

            DrawIndependentMotionApplyPanel(key, track, selectedCount);

            if (pathMode is SpriteSocketPathMode.CubicBezier or
                SpriteSocketPathMode.Hermite or SpriteSocketPathMode.Arc)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Auto Handles", EditorStyles.miniButton))
                        AutoSetIndependentMotionHandles(
                            _selectedSocketMotionTrack, _selectedSocketMotionKey);
                    if (pathMode == SpriteSocketPathMode.Arc &&
                        GUILayout.Button("Reset Arc", EditorStyles.miniButton))
                    {
                        RecordDiscreteUndo("Reset Independent Motion Arc");
                        key.ArcBulge = 0f;
                        key.ArcClockwise = false;
                        SaveDirty();
                        SealUndoGroup();
                        Repaint();
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset Ease-In-Out", EditorStyles.miniButton))
                    ResetSelectedIndependentEaseCurves(key);
                if (GUILayout.Button("Copy Curve", EditorStyles.miniButton))
                    _socketEaseCurveClipboard = CloneAnimationCurve(
                        key.CustomEaseCurve);
                using (new EditorGUI.DisabledScope(_socketEaseCurveClipboard == null))
                    if (GUILayout.Button("Paste Curve", EditorStyles.miniButton))
                        PasteSelectedIndependentEaseCurves(key);
            }
            GUILayout.Label("These settings control the segment leaving this key.",
                _mutedStyle);

            EditorGUI.BeginChangeCheck();
            float seconds = EditorGUILayout.FloatField(
                "Time (sec)", key.NormalizedTime * _profile.IndependentMotionDuration);
            Vector2 position = EditorGUILayout.Vector2Field("Position", key.LocalPosition);
            float angle = EditorGUILayout.FloatField("Angle", key.LocalAngle);
            Vector2 scale = EditorGUILayout.Vector2Field("Scale", key.LocalScale);
            if (!EditorGUI.EndChangeCheck())
                return;
            RecordProfileUndo("Edit Independent Motion Key");
            key.NormalizedTime = Mathf.Clamp01(
                seconds / _profile.IndependentMotionDuration);
            key.LocalPosition = position;
            key.LocalAngle = angle;
            key.LocalScale = scale;
            track.Normalize(Mathf.Max(1, _profile.Sheets?.Count ?? 0));
            _selectedSocketMotionKey = track.Keys.IndexOf(key);
            _selectedSocketMotionKeys.Clear();
            _selectedSocketMotionKeys.Add(key);
            _socketPreviewTime = key.NormalizedTime * _profile.IndependentMotionDuration;
            SaveDirty();
            Repaint();
        }

        void DrawIndependentMotionApplyPanel(
            SpriteSocketMotionKey key, SpriteSocketMotionTrack track, int selectedCount)
        {
            GUILayout.Space(6f);
            GUILayout.Label("APPLY TO OTHER KEYS", _sectionStyle);
            GUILayout.Label(
                "Apply the current key's setting to the selection or every key on this track. A key list is not needed — the timeline already picks keys.",
                _mutedStyle);
            int trackCount = track.Keys?.Count ?? 0;
            using (new EditorGUI.DisabledScope(selectedCount <= 1))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(
                            new GUIContent("Ease → Selected",
                                "Copy this timing ease onto the selected keys only."),
                            EditorStyles.miniButton))
                        ApplyIndependentMotionField(
                            "Apply Ease to Selected Keys", key, track,
                            IndependentMotionApplyScope.Selected,
                            ease: ResolvedEaseMode(key));
                    if (GUILayout.Button(
                            new GUIContent("Path → Selected",
                                "Copy this position path onto the selected keys only."),
                            EditorStyles.miniButton))
                        ApplyIndependentMotionField(
                            "Apply Path to Selected Keys", key, track,
                            IndependentMotionApplyScope.Selected,
                            pathMode: ResolvedPathMode(key));
                    if (GUILayout.Button(
                            new GUIContent("Rotation → Selected",
                                "Copy this rotation mode onto the selected keys only."),
                            EditorStyles.miniButton))
                        ApplyIndependentMotionField(
                            "Apply Rotation to Selected Keys", key, track,
                            IndependentMotionApplyScope.Selected,
                            rotationMode: ResolvedRotationMode(key));
                }
            }
            using (new EditorGUI.DisabledScope(trackCount <= 1))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(
                            new GUIContent("Ease → Track",
                                "Copy this timing ease onto every key on this track."),
                            EditorStyles.miniButton))
                        ApplyIndependentMotionField(
                            "Apply Ease to Track", key, track,
                            IndependentMotionApplyScope.Track,
                            ease: ResolvedEaseMode(key));
                    if (GUILayout.Button(
                            new GUIContent("Path → Track",
                                "Copy this position path onto every key on this track."),
                            EditorStyles.miniButton))
                        ApplyIndependentMotionField(
                            "Apply Path to Track", key, track,
                            IndependentMotionApplyScope.Track,
                            pathMode: ResolvedPathMode(key));
                    if (GUILayout.Button(
                            new GUIContent("Rotation → Track",
                                "Copy this rotation mode onto every key on this track."),
                            EditorStyles.miniButton))
                        ApplyIndependentMotionField(
                            "Apply Rotation to Track", key, track,
                            IndependentMotionApplyScope.Track,
                            rotationMode: ResolvedRotationMode(key));
                }
                if (GUILayout.Button(
                        new GUIContent("Apply All Three to Track",
                            "Copy timing ease, position path, and rotation mode onto every key on this track."),
                        EditorStyles.miniButton))
                    ApplyIndependentMotionField(
                        "Apply Motion Styles to Track", key, track,
                        IndependentMotionApplyScope.Track,
                        ease: ResolvedEaseMode(key),
                        pathMode: ResolvedPathMode(key),
                        rotationMode: ResolvedRotationMode(key));
            }
        }

        void CollectIndependentMotionEditKeys(SpriteSocketMotionKey primary)
        {
            _independentMotionEditKeys.Clear();
            if (_selectedSocketMotionKeys.Count > 0)
            {
                foreach (var selected in _selectedSocketMotionKeys)
                    if (selected != null)
                        _independentMotionEditKeys.Add(selected);
            }
            if (_independentMotionEditKeys.Count == 0 && primary != null)
                _independentMotionEditKeys.Add(primary);
        }

        bool HasMixedIndependentMotionEase()
        {
            if (_independentMotionEditKeys.Count <= 1)
                return false;
            byte first = _independentMotionEditKeys[0].EaseMode;
            for (int i = 1; i < _independentMotionEditKeys.Count; i++)
                if (_independentMotionEditKeys[i].EaseMode != first)
                    return true;
            return false;
        }

        bool HasMixedIndependentMotionPath()
        {
            if (_independentMotionEditKeys.Count <= 1)
                return false;
            byte first = _independentMotionEditKeys[0].PathMode;
            for (int i = 1; i < _independentMotionEditKeys.Count; i++)
                if (_independentMotionEditKeys[i].PathMode != first)
                    return true;
            return false;
        }

        bool HasMixedIndependentMotionRotation()
        {
            if (_independentMotionEditKeys.Count <= 1)
                return false;
            byte first = _independentMotionEditKeys[0].RotationMode;
            for (int i = 1; i < _independentMotionEditKeys.Count; i++)
                if (_independentMotionEditKeys[i].RotationMode != first)
                    return true;
            return false;
        }

        bool HasMixedIndependentMotionOvershoot()
        {
            if (_independentMotionEditKeys.Count <= 1)
                return false;
            bool first = _independentMotionEditKeys[0].AllowOvershoot;
            for (int i = 1; i < _independentMotionEditKeys.Count; i++)
                if (_independentMotionEditKeys[i].AllowOvershoot != first)
                    return true;
            return false;
        }

        bool HasMixedIndependentMotionCustomEase()
        {
            if (_independentMotionEditKeys.Count <= 1)
                return false;
            bool first = _independentMotionEditKeys[0].UseCustomEase;
            for (int i = 1; i < _independentMotionEditKeys.Count; i++)
                if (_independentMotionEditKeys[i].UseCustomEase != first)
                    return true;
            return false;
        }

        static SpriteEaseMode ResolvedEaseMode(SpriteSocketMotionKey key)
            => SpriteEase.IsValidMode(key.EaseMode)
                ? (SpriteEaseMode)key.EaseMode
                : SpriteEaseMode.SmoothStep;

        static SpriteSocketPathMode ResolvedPathMode(SpriteSocketMotionKey key)
            => key.PathMode <= (byte)SpriteSocketPathMode.None
                ? (SpriteSocketPathMode)key.PathMode
                : SpriteSocketPathMode.SmoothPath;

        static SpriteSocketRotationMode ResolvedRotationMode(SpriteSocketMotionKey key)
            => key.RotationMode <= (byte)SpriteSocketRotationMode.None
                ? (SpriteSocketRotationMode)key.RotationMode
                : SpriteSocketRotationMode.Shortest;

        void ApplyIndependentMotionField(
            string undoName,
            SpriteSocketMotionKey primary,
            SpriteSocketMotionTrack track,
            IndependentMotionApplyScope scope,
            SpriteEaseMode? ease = null,
            SpriteSocketPathMode? pathMode = null,
            Vector2? inTangent = null,
            Vector2? outTangent = null,
            float? arcBulge = null,
            bool? arcClockwise = null,
            SpriteSocketRotationMode? rotationMode = null,
            int? rotationTurns = null,
            float? facingOffset = null,
            bool? allowOvershoot = null,
            bool? useCustomEase = null,
            AnimationCurve customCurve = null)
        {
            RecordDiscreteUndo(undoName);
            int changed = 0;
            if (scope == IndependentMotionApplyScope.Track && track?.Keys != null)
            {
                for (int i = 0; i < track.Keys.Count; i++)
                {
                    if (ApplyIndependentMotionFieldsToKey(
                            track.Keys[i], ease, pathMode, inTangent, outTangent,
                            arcBulge, arcClockwise, rotationMode, rotationTurns,
                            facingOffset, allowOvershoot, useCustomEase, customCurve))
                        changed++;
                }
                if (ease.HasValue)
                    track.DefaultEaseMode = (byte)ease.Value;
                if (pathMode.HasValue)
                    track.DefaultPathMode = (byte)pathMode.Value;
                if (rotationMode.HasValue)
                    track.DefaultRotationMode = (byte)rotationMode.Value;
            }
            else
            {
                CollectIndependentMotionEditKeys(primary);
                for (int i = 0; i < _independentMotionEditKeys.Count; i++)
                {
                    if (ApplyIndependentMotionFieldsToKey(
                            _independentMotionEditKeys[i], ease, pathMode,
                            inTangent, outTangent, arcBulge, arcClockwise,
                            rotationMode, rotationTurns, facingOffset,
                            allowOvershoot, useCustomEase, customCurve))
                        changed++;
                }
            }
            _status = scope == IndependentMotionApplyScope.Track
                ? $"Applied to {changed} key{Plural(changed)} on {track.SocketName}"
                : $"Applied to {changed} selected key{Plural(changed)}";
            SaveDirty();
            SealUndoGroup();
            Repaint();
        }

        static bool ApplyIndependentMotionFieldsToKey(
            SpriteSocketMotionKey key,
            SpriteEaseMode? ease,
            SpriteSocketPathMode? pathMode,
            Vector2? inTangent,
            Vector2? outTangent,
            float? arcBulge,
            bool? arcClockwise,
            SpriteSocketRotationMode? rotationMode,
            int? rotationTurns,
            float? facingOffset,
            bool? allowOvershoot,
            bool? useCustomEase,
            AnimationCurve customCurve)
        {
            if (key == null)
                return false;
            if (ease.HasValue)
                key.EaseMode = (byte)ease.Value;
            if (pathMode.HasValue)
                key.PathMode = (byte)pathMode.Value;
            if (inTangent.HasValue)
                key.InTangent = inTangent.Value;
            if (outTangent.HasValue)
                key.OutTangent = outTangent.Value;
            if (arcBulge.HasValue)
                key.ArcBulge = arcBulge.Value;
            if (arcClockwise.HasValue)
                key.ArcClockwise = arcClockwise.Value;
            if (rotationMode.HasValue)
                key.RotationMode = (byte)rotationMode.Value;
            if (rotationTurns.HasValue)
                key.RotationTurns = Mathf.Clamp(rotationTurns.Value, -100, 100);
            if (facingOffset.HasValue)
                key.FacingAngleOffset = facingOffset.Value;
            if (allowOvershoot.HasValue)
                key.AllowOvershoot = allowOvershoot.Value;
            if (useCustomEase.HasValue)
                key.UseCustomEase = useCustomEase.Value;
            if (customCurve != null)
            {
                key.UseCustomEase = true;
                key.CustomEaseCurve = CloneAnimationCurve(customCurve);
            }
            if (key.UseCustomEase &&
                (useCustomEase.HasValue || customCurve != null || allowOvershoot.HasValue))
                key.RebuildCustomEaseSamples();
            return true;
        }

        void ApplyIndependentMotionSegmentSettings(
            SpriteSocketMotionKey primary, SpriteEaseMode ease,
            SpriteSocketPathMode pathMode, bool useCustomEase,
            AnimationCurve customCurve)
        {
            TryGetSocketMotionKey(
                _selectedSocketMotionTrack, _selectedSocketMotionKey,
                out var track, out _);
            ApplyIndependentMotionField(
                "Edit Independent Motion Segment", primary, track,
                IndependentMotionApplyScope.Selected,
                ease: ease, pathMode: pathMode, useCustomEase: useCustomEase,
                customCurve: customCurve);
        }

        void ApplyIndependentRotationMode(
            SpriteSocketMotionKey primary, SpriteSocketRotationMode mode)
        {
            bool applied = false;
            foreach (var selectedKey in _selectedSocketMotionKeys)
            {
                if (selectedKey == null)
                    continue;
                selectedKey.RotationMode = (byte)mode;
                applied = true;
            }
            if (!applied)
                primary.RotationMode = (byte)mode;
        }

        void ApplyIndependentOvershoot(SpriteSocketMotionKey primary, bool allow)
        {
            bool applied = false;
            foreach (var selectedKey in _selectedSocketMotionKeys)
            {
                if (selectedKey == null)
                    continue;
                selectedKey.AllowOvershoot = allow;
                if (selectedKey.UseCustomEase)
                    selectedKey.RebuildCustomEaseSamples();
                applied = true;
            }
            if (applied)
                return;
            primary.AllowOvershoot = allow;
            if (primary.UseCustomEase)
                primary.RebuildCustomEaseSamples();
        }

        void AutoSetIndependentMotionHandles(int trackIndex, int keyIndex)
        {
            if (!TryGetSocketMotionKey(
                    trackIndex, keyIndex, out var track, out var key))
                return;
            RecordDiscreteUndo("Auto Independent Motion Handles");
            bool applied = false;
            for (int i = 0; i < _profile.SocketMotions.Count; i++)
            {
                var candidateTrack = _profile.SocketMotions[i];
                for (int k = 0; k < candidateTrack.Keys.Count; k++)
                {
                    if (!_selectedSocketMotionKeys.Contains(candidateTrack.Keys[k]))
                        continue;
                    SetAutomaticMotionHandles(candidateTrack, k);
                    applied = true;
                }
            }
            if (!applied)
                SetAutomaticMotionHandles(track, keyIndex);
            SaveDirty();
            SealUndoGroup();
            Repaint();
        }

        static void SetAutomaticMotionHandles(
            SpriteSocketMotionTrack track, int keyIndex)
        {
            int count = track.Keys.Count;
            if (count < 2 || keyIndex < 0 || keyIndex >= count)
                return;
            int previous = keyIndex > 0 ? keyIndex - 1 : track.Loop ? count - 1 : 0;
            int next = keyIndex + 1 < count ? keyIndex + 1 : track.Loop ? 0 : count - 1;
            var key = track.Keys[keyIndex];
            Vector2 tangent = (track.Keys[next].LocalPosition -
                               track.Keys[previous].LocalPosition) * 0.5f;
            if (key.PathMode == (byte)SpriteSocketPathMode.CubicBezier)
            {
                key.InTangent = -tangent / 3f;
                key.OutTangent = tangent / 3f;
            }
            else
            {
                key.InTangent = tangent;
                key.OutTangent = tangent;
            }
            if (Mathf.Abs(key.ArcBulge) < 0.001f)
                key.ArcBulge = Vector2.Distance(
                    key.LocalPosition, track.Keys[next].LocalPosition) * 0.25f;
        }

        void ResetSelectedIndependentEaseCurves(SpriteSocketMotionKey primary)
        {
            RecordDiscreteUndo("Reset Independent Motion Ease Curve");
            ApplyIndependentMotionCurve(primary,
                AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
            SaveDirty();
            SealUndoGroup();
            Repaint();
        }

        void PasteSelectedIndependentEaseCurves(SpriteSocketMotionKey primary)
        {
            if (_socketEaseCurveClipboard == null)
                return;
            RecordDiscreteUndo("Paste Independent Motion Ease Curve");
            ApplyIndependentMotionCurve(primary, _socketEaseCurveClipboard);
            SaveDirty();
            SealUndoGroup();
            Repaint();
        }

        void ApplyIndependentMotionCurve(
            SpriteSocketMotionKey primary, AnimationCurve curve)
        {
            bool applied = false;
            if (_selectedSocketMotionKeys.Count > 0)
            {
                foreach (var selectedKey in _selectedSocketMotionKeys)
                {
                    if (selectedKey == null)
                        continue;
                    selectedKey.UseCustomEase = true;
                    selectedKey.CustomEaseCurve = CloneAnimationCurve(curve);
                    selectedKey.RebuildCustomEaseSamples();
                    applied = true;
                }
            }
            if (applied)
                return;
            primary.UseCustomEase = true;
            primary.CustomEaseCurve = CloneAnimationCurve(curve);
            primary.RebuildCustomEaseSamples();
        }

        void DrawQuickMotionPresets(
            SpriteClipDef clip, bool independent, IList<string> visibleNames)
        {
            if (CountSelectedSocketNames(visibleNames) == 0)
                return;
            GUILayout.Space(7f);
            GUILayout.Label("MOTION PRESETS", _sectionStyle);
            using (new EditorGUILayout.HorizontalScope())
            {
                string[] labels = { "Orbit", "Float", "Shake", "Recoil" };
                for (int i = 0; i < labels.Length; i++)
                {
                    int preset = i;
                    if (GUILayout.Button(labels[i], EditorStyles.miniButton))
                        ApplyQuickMotionPreset(clip, independent, visibleNames, preset);
                }
            }
        }

        void ApplyQuickMotionPreset(
            SpriteClipDef clip, bool independent, IList<string> visibleNames, int preset)
        {
            if (clip == null)
                return;
            RecordProfileUndo($"Apply {QuickMotionPresetName(preset)} Preset");
            int changed = 0;
            float amplitude = Mathf.Max(4f,
                _socketOrbitRadius > 1f ? _socketOrbitRadius : DefaultSocketOrbitRadius());
            for (int n = 0; n < visibleNames.Count; n++)
            {
                string name = visibleNames[n];
                if (!IsSocketSelected(name) ||
                    !TryGetPreviewSocketPose(clip, name, _selectedFrame,
                        out var basePosition, out var baseAngle, out var baseScale, out _))
                    continue;
                if (independent)
                {
                    var track = _profile.FindSocketMotion(name);
                    if (track == null)
                        continue;
                    float referencePpu = SpriteSheetProfile.GetPixelsPerUnit(
                        _profile.SheetAt(track.ReferenceSheetIndex));
                    float targetPpu = SpriteSheetProfile.GetPixelsPerUnit(
                        _profile.SheetAt(clip.SheetIndex));
                    Vector2 referenceBase = basePosition *
                                            (referencePpu / Mathf.Max(1f, targetPpu));
                    float[] times = preset switch
                    {
                        0 => new[] { 0f, 0.125f, 0.25f, 0.375f, 0.5f, 0.625f, 0.75f, 0.875f, 1f },
                        1 => new[] { 0f, 0.25f, 0.5f, 0.75f, 1f },
                        2 => new[] { 0f, 0.125f, 0.25f, 0.375f, 0.5f, 0.625f, 0.75f, 0.875f, 1f },
                        _ => new[] { 0f, 0.12f, 0.35f, 1f },
                    };
                    track.Keys.Clear();
                    for (int i = 0; i < times.Length; i++)
                    {
                        float t = times[i];
                        track.Keys.Add(new SpriteSocketMotionKey
                        {
                            NormalizedTime = t,
                            LocalPosition = referenceBase + QuickMotionOffset(preset, t, amplitude),
                            LocalAngle = baseAngle,
                            LocalScale = baseScale,
                            EaseMode = (byte)(preset == 2
                                ? SpriteEaseMode.Linear
                                : SpriteEaseMode.SmoothStep),
                        });
                    }
                    track.Normalize(Mathf.Max(1, _profile.Sheets?.Count ?? 0));
                }
                else
                {
                    for (int frame = 0; frame < clip.Frames.Length; frame++)
                    {
                        float t = clip.Frames.Length <= 1
                            ? 0f
                            : frame / (float)(clip.Frames.Length - 1);
                        var key = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, name, frame);
                        key.LocalPosition = basePosition +
                                            QuickMotionOffset(preset, t, amplitude);
                        key.LocalAngle = baseAngle;
                        key.LocalScale = baseScale;
                    }
                }
                changed++;
            }
            _status = $"Applied {QuickMotionPresetName(preset)} to {changed} " +
                      (independent ? "motion track" : "frame socket") + Plural(changed);
            SaveDirty();
            Repaint();
        }

        static string QuickMotionPresetName(int preset)
            => preset switch
            {
                0 => "Orbit",
                1 => "Float",
                2 => "Shake",
                _ => "Recoil",
            };

        static Vector2 QuickMotionOffset(int preset, float t, float amplitude)
        {
            float phase = t * Mathf.PI * 2f;
            return preset switch
            {
                0 => new Vector2(Mathf.Cos(phase), Mathf.Sin(phase)) * amplitude,
                1 => new Vector2(0f, Mathf.Sin(phase) * amplitude * 0.45f),
                2 => new Vector2(Mathf.Sin(t * Mathf.PI * 8f) * amplitude * 0.35f, 0f),
                _ => t <= 0.35f
                    ? new Vector2(-Mathf.Sin(t / 0.35f * Mathf.PI) * amplitude * 0.7f, 0f)
                    : Vector2.zero,
            };
        }

        void DrawSelectedSocketTriggerInspector()
        {
            if (!TryGetSelectedSocketTrigger(
                    _selectedSocketTriggerTrack, _selectedSocketTriggerIndex,
                    out var track, out var trigger))
                return;
            GUILayout.Space(8f);
            SectionLabel("INDEPENDENT TRIGGER");
            EditorGUILayout.LabelField("Socket", track.SocketName);
            EditorGUI.BeginChangeCheck();
            float seconds = EditorGUILayout.FloatField(
                new GUIContent("Time (sec)", "Trigger time on this independent track."),
                trigger.NormalizedTime * track.Duration);
            int eventId = Mathf.Clamp(EditorGUILayout.IntField(
                new GUIContent("Event ID", "References the profile Event list."),
                trigger.EventId), 1, byte.MaxValue);
            if (EditorGUI.EndChangeCheck())
            {
                RecordProfileUndo("Edit Independent Socket Trigger");
                trigger.NormalizedTime = Mathf.Clamp01(seconds / Mathf.Max(0.01f, track.Duration));
                trigger.EventId = (byte)eventId;
                track.Normalize(Mathf.Max(1, _profile.Sheets?.Count ?? 0));
                _selectedSocketTriggerIndex = track.Triggers.IndexOf(trigger);
                _status = $"{track.SocketName} trigger = {EventName(trigger.EventId)} at {seconds:0.###}s";
                SaveDirty();
            }
            if (GUILayout.Button("Delete Trigger"))
            {
                DeleteSocketTrigger(_selectedSocketTriggerTrack, _selectedSocketTriggerIndex);
                GUIUtility.ExitGUI();
            }
        }

        void DrawSocketIdentityInspector(SpriteClipDef clip, string name)
        {
            name = SpriteSocketKeys.CanonicalName(name);
            SpriteSocketKeys.TryGetPose(clip.Sockets, name, _selectedFrame,
                out var pose, out var angle, out var poseScale, out bool onFrame);
            var catalogItem = _profile.SocketCatalog.Find(name);
            var independentTrack = _profile.FindSocketMotion(name);
            bool editingIndependent = independentTrack != null &&
                                      catalogItem != null && catalogItem.UsesOwnClock;
            if (editingIndependent && catalogItem != null)
            {
                TrySampleIndependentSocketMotion(
                    clip, name, catalogItem, out pose, out angle, out poseScale);
                float motionTime = CurrentIndependentMotionTime();
                onFrame = IndependentKeyIndexAtTime(independentTrack, motionTime) >= 0;
            }

                    if (editingIndependent)
                        GUILayout.Label(
                            "Independent Motion • player-pivot anchored • character clip timing does not affect this track.",
                            _mutedWrapStyle);
                    else if (!onFrame)
                        GUILayout.Label("No key on this frame yet. Drag or edit to add one.", _mutedStyle);

                    // Name: label by default; F2 / double-click begins inline rename.
                    bool renamingName = !string.IsNullOrEmpty(_renamingSocketName) &&
                        SpriteSocketKeys.NamesEqual(_renamingSocketName, name);
                    if (renamingName)
                    {
                        var nameRow = EditorGUILayout.GetControlRect();
                        var nameLabelRect = new Rect(nameRow.x, nameRow.y, EditorGUIUtility.labelWidth, nameRow.height);
                        var nameFieldRect = new Rect(nameRow.x + EditorGUIUtility.labelWidth, nameRow.y,
                            Mathf.Max(20f, nameRow.width - EditorGUIUtility.labelWidth), nameRow.height);
                        GUI.Label(nameLabelRect, "Name");
                        DrawInlineRenameField(nameFieldRect, SocketNameRenameControl,
                            ref _renameSocketNameValue, ref _focusSocketNameRename, EditorStyles.textField);
                    }
                    else if (DrawRenameLabelRow("Name", name,
                        "Double-click or press F2 to rename this socket.", out _))
                    {
                        BeginSocketNameRename(name);
                        GUIUtility.ExitGUI();
                    }

                    catalogItem ??= _profile.SocketCatalog.Ensure(name);
                    string socketId = catalogItem.SocketId ?? string.Empty;
                    bool renamingId = !string.IsNullOrEmpty(_renamingSocketId) &&
                        SpriteSocketKeys.NamesEqual(_renamingSocketId, name);
                    if (renamingId)
                    {
                        var idRow = EditorGUILayout.GetControlRect();
                        var idLabelRect = new Rect(idRow.x, idRow.y, EditorGUIUtility.labelWidth, idRow.height);
                        var idFieldRect = new Rect(idRow.x + EditorGUIUtility.labelWidth, idRow.y,
                            Mathf.Max(20f, idRow.width - EditorGUIUtility.labelWidth), idRow.height);
                        GUI.Label(idLabelRect, new GUIContent("ID",
                            "Stable code ID shared by Frame-Attached and Independent Motion sockets."));
                        DrawInlineRenameField(idFieldRect, SocketIdRenameControl,
                            ref _renameSocketIdValue, ref _focusSocketIdRename, EditorStyles.textField);
                    }
                    else if (DrawRenameLabelRow("ID", socketId,
                        "Double-click to rename the stable socket ID. F2 renames Name.", out _))
                    {
                        BeginSocketIdRename(name);
                        GUIUtility.ExitGUI();
                    }
                    GUILayout.Label($"Code: SpriteSockets.Hash(\"{catalogItem.SocketId}\")", _mutedStyle);

                    EditorGUI.BeginChangeCheck();
                    bool closedPath = catalogItem == null || catalogItem.ClosedPath;
                    bool nextClosedPath = EditorGUILayout.Toggle(
                        new GUIContent("Closed Path",
                            "On: last key returns to the first. Off: hold the last pose. Independent companions usually stay closed."),
                        closedPath);
                    if (EditorGUI.EndChangeCheck())
                    {
                        RecordProfileUndo("Toggle Socket Closed Path");
                        _profile.SocketCatalog.Ensure(name).PathWrap = nextClosedPath ? (byte)0 : (byte)1;
                        catalogItem = _profile.SocketCatalog.Find(name);
                        var motion = _profile.FindSocketMotion(name);
                        if (motion != null)
                        {
                            _profile.IndependentMotionLoop = nextClosedPath;
                            _profile.EnsureSocketMotions();
                        }
                    }

                    int clockPopup = catalogItem != null && catalogItem.UsesOwnClock ? 1 : 0;
                    int nextClock = EditorGUILayout.Popup(
                        new GUIContent("Motion",
                            "Frame-Attached: follows character frames (helmet, weapon). Independent: its own timeline (companion, orbit, effect). Behind/Front still apply."),
                        clockPopup, SocketClockModeLabels);
                    if (nextClock != clockPopup)
                    {
                        RecordProfileUndo("Set Socket Motion");
                        var item = _profile.SocketCatalog.Ensure(name);
                        item.MotionMode = (byte)nextClock;
                        if (nextClock == 1)
                        {
                            item.PathWrap = 0;
                            if (item.Speed <= 0.0001f)
                                item.Speed = 1f;
                            CaptureSocketMotionsFromClip(clip, new[] { name }, replaceTiming: true);
                        }
                        else
                        {
                            var motion = _profile.FindSocketMotion(name);
                            if (motion != null)
                                _profile.SocketMotions.Remove(motion);
                        }
                        catalogItem = item;
                        _status = nextClock == 1
                            ? $"{name}  Independent  (own timeline and speed)"
                            : $"{name}  Frame-Attached  (follows character frames)";
                        SaveDirty();
                    }

                    if (catalogItem != null && catalogItem.UsesOwnClock)
                    {
                        GUILayout.Label(
                            $"Shared timeline speed: {_profile.IndependentMotionSpeed:0.##}×",
                            _mutedStyle);
                    }

                    _socketOrbitTilt = EditorGUILayout.Popup(
                        new GUIContent("Orbit Tilt",
                            "Apply this tilt to the selected socket. 0° is horizontal, 90° is vertical."),
                        Mathf.Clamp(_socketOrbitTilt, 0, SocketOrbitTiltLabels.Length - 1),
                        SocketOrbitTiltLabels);
                    if (DrawOrbitCreateRow(
                            "Apply Orbit to This Socket",
                            "Restamp this socket as an elliptical orbit at the tilt above. Count > 1 adds coplanar orbs on the same ring, evenly phased.",
                            ref _socketCoplanarCount, 1, 12))
                        ApplySocketOrbitShape(clip, name, _socketCoplanarCount);

                    EditorGUI.BeginChangeCheck();
                    float offsetX = EditorGUILayout.FloatField("Offset X (px)", pose.x);
                    float offsetY = EditorGUILayout.FloatField("Offset Y (px)", pose.y);
                    float nextAngle = EditorGUILayout.FloatField("Angle (deg)", angle);
                    float scaleX = EditorGUILayout.FloatField("Scale X", poseScale.x);
                    float scaleY = EditorGUILayout.FloatField("Scale Y", poseScale.y);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (editingIndependent)
                        {
                            var key = EnsureIndependentMotionKey(
                                independentTrack, CurrentIndependentMotionTime(),
                                pose, angle, poseScale);
                            key.LocalPosition = new Vector2(offsetX, offsetY);
                            key.LocalAngle = nextAngle;
                            var nextScale = new Vector2(scaleX, scaleY);
                            key.LocalScale = nextScale;
                            if (!Mathf.Approximately(scaleX, poseScale.x) ||
                                !Mathf.Approximately(scaleY, poseScale.y))
                                ApplyIndependentTrackScale(independentTrack, clip, name, nextScale);
                        }
                        else
                        {
                            var key = SpriteSocketKeys.EnsureFrameKey(
                                clip.Sockets, name, _selectedFrame);
                            key.LocalPosition = new Vector2(offsetX, offsetY);
                            key.LocalAngle = nextAngle;
                            key.LocalScale = new Vector2(scaleX, scaleY);
                        }
                        _status = $"Socket {name}  ({offsetX:0.##}, {offsetY:0.##})  {nextAngle:0.##}°  scale {scaleX:0.##},{scaleY:0.##}";
                    }

                    var independentDrawKey = editingIndependent
                        ? IndependentKeyAtTime(independentTrack, CurrentIndependentMotionTime())
                        : null;
                    var drawKey = editingIndependent
                        ? null
                        : SpriteSocketKeys.FindOnFrame(clip.Sockets, name, _selectedFrame);
                    byte drawLayer = editingIndependent
                        ? independentDrawKey?.DrawLayer ?? SpriteSocketKeys.DrawUnset
                        : drawKey?.DrawLayer ?? SpriteSocketKeys.DrawUnset;
                    int drawPopup = drawLayer == SpriteSocketKeys.DrawUnset
                        ? 0
                        : drawLayer == SpriteSocketKeys.DrawBehind
                            ? 1
                            : drawLayer == SpriteSocketKeys.DrawFront
                                ? 2
                                : 0;
                    int nextDrawPopup = EditorGUILayout.Popup(
                        new GUIContent("Draw This Frame",
                            "Hold until the next draw key. Default follows the catalog Behind/In Front. Do not use timeline events for this."),
                        drawPopup, new[] { "Default", "Behind", "In Front" });
                    if (nextDrawPopup != drawPopup)
                    {
                        RecordProfileUndo("Set Socket Draw Layer");
                        byte nextLayer = nextDrawPopup == 1
                            ? SpriteSocketKeys.DrawBehind
                            : nextDrawPopup == 2
                                ? SpriteSocketKeys.DrawFront
                                : SpriteSocketKeys.DrawCatalog;
                        if (editingIndependent)
                            EnsureIndependentMotionKey(independentTrack,
                                CurrentIndependentMotionTime(), pose, angle, poseScale).DrawLayer =
                                nextLayer;
                        else
                            SpriteSocketKeys.EnsureFrameKey(
                                clip.Sockets, name, _selectedFrame).DrawLayer = nextLayer;
                        _status = nextDrawPopup == 0
                            ? $"{name} draw uses catalog default from frame {_selectedFrame + 1}"
                            : $"{name} draws {(nextDrawPopup == 1 ? "behind" : "in front")} from frame {_selectedFrame + 1}";
                        SaveDirty();
                    }
                    else if (drawLayer == SpriteSocketKeys.DrawUnset)
                    {
                        bool heldBehind = editingIndependent
                            ? SpriteSocketKeys.IsIndependentDrawnBehind(
                                independentTrack, CurrentIndependentMotionTime(),
                                SpriteSocketKeys.CatalogDrawsBehind(catalogItem))
                            : SpriteSocketKeys.IsDrawnBehind(
                                clip.Sockets, name, _selectedFrame,
                                SpriteSocketKeys.CatalogDrawsBehind(catalogItem),
                                SocketSampleClosed(clip, name));
                        GUILayout.Label(
                            heldBehind ? "Held: Behind (from an earlier frame or default)"
                                       : "Held: In Front (from an earlier frame or default)",
                            _mutedStyle);
                    }

                    if (!editingIndependent && GUILayout.Button(new GUIContent(
                            "Apply to Frames…",
                            "Open the frame list to copy position, rotation, and/or scale onto other frames.")))
                    {
                        OpenSocketInheritPanel(clip, name, _selectedFrame);
                    }

                    catalogItem ??= _profile.SocketCatalog.Find(name);
                    var currentProfile = catalogItem?.Profile;
                    var nextProfile = (ScriptableSpriteSheetProfile)EditorGUILayout.ObjectField(
                        new GUIContent("Profile",
                            "Optional animation profile for this socket. Use a clip instead of a still sheet cell."),
                        currentProfile, typeof(ScriptableSpriteSheetProfile), false);
                    if (nextProfile != currentProfile)
                    {
                        if (nextProfile == null && catalogItem?.Texture == null)
                        {
                            _profile.SocketCatalog.Remove(name);
                            catalogItem = null;
                        }
                        else
                        {
                            bool firstProfile = currentProfile == null && nextProfile != null;
                            catalogItem = _profile.SocketCatalog.Ensure(name);
                            catalogItem.Profile = nextProfile;
                            if (firstProfile)
                                ApplyDefaultSocketPreviewClip(catalogItem);
                        }
                        catalogItem = _profile.SocketCatalog.Find(name);
                        _status = nextProfile == null
                            ? $"Cleared profile on {name}"
                            : $"Profile {nextProfile.name} on {name}";
                    }

                    Texture2D currentPreview = catalogItem?.Texture;
                    if (catalogItem?.Profile == null)
                    {
                        var nextPreview = (Texture2D)EditorGUILayout.ObjectField(
                            new GUIContent("Preview",
                                "Still image drawn on this socket. Drop a profile instead to play a clip."),
                            currentPreview, typeof(Texture2D), false);
                        if (nextPreview != currentPreview)
                        {
                            if (nextPreview == null)
                                _profile.SocketCatalog.Remove(name);
                            else
                                _profile.SocketCatalog.Ensure(name).Texture = nextPreview;
                            catalogItem = _profile.SocketCatalog.Find(name);
                            _status = nextPreview == null
                                ? $"Cleared preview on {name}"
                                : $"Preview {nextPreview.name} on {name}";
                        }
                    }

                    if (catalogItem == null || !catalogItem.HasPreview)
                    {
                        GUILayout.Label("Drop a sprite or profile here to preview on this socket.", _mutedStyle);
                    }
                    else
                    {
                        catalogItem.Normalize();
                        if (catalogItem.Profile != null)
                            DrawSocketProfilePreviewFields(catalogItem, clip);
                        else
                        {
                            catalogItem.Columns = Mathf.Max(1, EditorGUILayout.IntField("Columns", catalogItem.Columns));
                            catalogItem.Rows = Mathf.Max(1, EditorGUILayout.IntField("Rows", catalogItem.Rows));
                            catalogItem.Normalize();
                            if (catalogItem.CellCount > 1)
                            {
                                catalogItem.CellIndex = EditorGUILayout.IntSlider(
                                    "Cell", catalogItem.CellIndex, 0, catalogItem.CellCount - 1);
                            }
                        }

                        catalogItem.GripPixels = EditorGUILayout.Vector2Field(
                            new GUIContent("Grip (px)",
                                "Extra source-pixel offset so the item grip sits on the socket marker."),
                            catalogItem.GripPixels);
                        catalogItem.Pivot = EditorGUILayout.Vector2Field(
                            new GUIContent("Pivot", "Normalized pivot on the preview sprite (0-1)."),
                            catalogItem.Pivot);
                        catalogItem.Scale = Mathf.Max(0.01f, EditorGUILayout.FloatField(
                            new GUIContent("Item Scale", "Uniform size of the preview art. Pose Scale X/Y is per frame."),
                            catalogItem.Scale));
                        catalogItem.FlipX = EditorGUILayout.Toggle(
                            new GUIContent("Flip X", "Mirror the preview horizontally."),
                            catalogItem.FlipX);
                        int drawIndex = catalogItem.SortingOffset < 0 ? 0 : 1;
                        int nextDraw = EditorGUILayout.Popup(
                            new GUIContent("Default Draw",
                                "Fallback behind/in front when a frame does not override Draw This Frame."),
                            drawIndex, new[] { "Behind", "In Front" });
                        catalogItem.SortingOffset = nextDraw == 0 ? -1 : 0;
                        if (GUILayout.Button(new GUIContent("Clear Preview",
                                "Unbind the preview image and profile. The socket pose stays.")))
                        {
                            RecordProfileUndo("Clear Socket Preview");
                            _profile.SocketCatalog.Remove(name);
                            _status = $"Cleared preview on {name}";
                            SaveDirty();
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(new GUIContent("Clear Frame Offset",
                                "Reset this frame's socket position and angle to 0. Adds a key if this frame did not have one.")))
                        {
                            RecordProfileUndo("Clear Sprite Socket Frame Offset");
                            var key = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, name, _selectedFrame);
                            key.LocalPosition = Vector2.zero;
                            key.LocalAngle = 0f;
                            key.LocalScale = Vector2.one;
                            _status = $"Cleared {name} offset on frame {_selectedFrame + 1}";
                            SaveDirty();
                        }
                        if (GUILayout.Button(new GUIContent("Delete",
                                "Delete this socket identity from every frame.")))
                        {
                            RecordProfileUndo("Delete Sprite Socket");
                            SpriteSocketKeys.DeleteIdentity(clip.Sockets, name);
                            bool stillUsed = SpriteSocketKeys.NameExistsOnAnyClip(_profile.Clips, name);
                            _profile.SocketCatalog.SyncDelete(name, stillUsed);
                            _status = $"Deleted socket {name}";
                            _selectedSockets.Remove(SpriteSocketKeys.CanonicalName(name));
                            if (SpriteSocketKeys.NamesEqual(_selectedSocketName, name))
                                _selectedSocketName = null;
                            SyncSocketPrimaryFromSelection();
                            _draggingSocket = false;
                            _socketHandleKind = ColliderHandleKind.None;
                            SaveDirty();
                            GUIUtility.ExitGUI();
                        }
                    }
        }

        void OpenSocketInheritPanel(SpriteClipDef clip, string socketName, int sourceFrame,
            Vector2 guiPoint = default, bool exitGui = true)
        {
            if (clip?.Frames == null || clip.Frames.Length == 0 || string.IsNullOrEmpty(socketName))
                return;

            _socketInheritNames.Clear();
            if (_selectedSockets.Count > 0)
            {
                foreach (string name in _selectedSockets)
                    _socketInheritNames.Add(SpriteSocketKeys.CanonicalName(name));
            }
            if (_socketInheritNames.Count == 0 ||
                !_socketInheritNames.Exists(name => SpriteSocketKeys.NamesEqual(name, socketName)))
                _socketInheritNames.Add(SpriteSocketKeys.CanonicalName(socketName));

            _socketInheritClipIndex = _selectedClip;
            _socketInheritSourceFrame = Mathf.Clamp(sourceFrame, 0, clip.Frames.Length - 1);
            _socketInheritFrames.Clear();
            if (_selectedFrames.Count > 1)
            {
                foreach (int frame in _selectedFrames)
                    _socketInheritFrames.Add(frame);
            }
            else
            {
                for (int i = 0; i < clip.Frames.Length; i++)
                {
                    if (i != _socketInheritSourceFrame)
                        _socketInheritFrames.Add(i);
                }
                if (_socketInheritFrames.Count == 0)
                    _socketInheritFrames.Add(_socketInheritSourceFrame);
            }

            _socketInheritRangeAnchor = _socketInheritSourceFrame;
            _socketInheritPosition = true;
            _socketInheritRotation = true;
            _socketInheritScale = true;
            _showSocketInheritPanel = true;
            CloseSocketTransformPanel();

            float width = 380f;
            float height = Mathf.Clamp(position.height - 80f, 360f, 540f);
            _socketInheritPanelRect = new Rect(
                Mathf.Max(8f, (position.width - width) * 0.5f),
                Mathf.Max(48f, (position.height - height) * 0.35f),
                width, height);
            Repaint();

            _status = _socketInheritNames.Count == 1
                ? $"Socket {_socketInheritNames[0]}  — pick frames to inherit pose"
                : $"{_socketInheritNames.Count} sockets  — pick frames to inherit pose";
            if (exitGui)
                GUIUtility.ExitGUI();
        }

        SpriteClipDef SocketInheritClip()
        {
            if (_profile?.Clips != null && _socketInheritClipIndex >= 0 &&
                _socketInheritClipIndex < _profile.Clips.Count)
                return _profile.Clips[_socketInheritClipIndex];
            return CurrentClip;
        }

        bool SocketInheritBlocksEditorInput()
            => OverlayBlocksEditorInput();

        bool OverlayBlocksEditorInput()
        {
            if (!_showSocketInheritPanel && !_showSocketTransformPanel && !_showSheetCellPicker)
                return false;
            return Event.current.type is EventType.MouseDown or EventType.MouseUp
                or EventType.MouseDrag or EventType.MouseMove or EventType.ContextClick
                or EventType.ScrollWheel;
        }

        static bool IsPreviewContextClick(Event evt)
            => evt != null && (evt.type == EventType.ContextClick ||
                               (evt.type == EventType.MouseDown && evt.button == 1));

        void HandleWindowSocketContextClick(Rect previewRect)
        {
            var evt = Event.current;
            if (!IsPreviewContextClick(evt))
                return;

            var canvas = new Rect(
                previewRect.x + 10f, previewRect.y + 54f,
                previewRect.width - 20f, previewRect.height - 66f);
            if (!canvas.Contains(evt.mousePosition))
                return;

            var clip = CurrentClip;
            if (clip == null || _profile?.Sheet == null)
                return;

            var localCanvas = new Rect(0f, 0f, canvas.width, canvas.height);
            if (!TryComputePreviewLayout(localCanvas, out Rect cell, out _, out _))
                return;

            Vector2 contentMouse = evt.mousePosition - canvas.position + _previewScroll;
            int frame = EvaluatePreview(clip, _previewTime).Frame;
            string hit = FindSocketAt(clip, frame, cell, contentMouse, includeLocked: true);
            if (hit == null &&
                HitSelectedSocketHandle(cell, clip, frame, contentMouse) != ColliderHandleKind.None)
                hit = _selectedSocketName;
            if (hit == null && !string.IsNullOrEmpty(_selectedSocketName))
            {
                var bounds = SocketWorldAabb(clip, _selectedSocketName, frame, cell);
                if (bounds.Contains(contentMouse))
                    hit = _selectedSocketName;
            }
            // Right-click on the preview with a socket already selected still opens
            // the panel, even if the pin hit-test misses.
            if (hit == null)
                hit = _selectedSocketName;
            if (string.IsNullOrEmpty(hit))
                return;

            if (_draggingSocket)
                EndSocketDrag(GUIUtility.hotControl, save: false);
            ShowSocketContextMenu(clip, hit);
            evt.Use();
            Repaint();
        }

        void DrawSelectedSocketBar(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.18f, 0.24f, 1f));
            DrawBorder(rect, AccentColor, 1f);
            bool locked = IsSocketLocked(_selectedSocketName);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 52f, 18f),
                $"SOCKET  {_selectedSocketName}" + (locked ? "  •  LOCKED" : string.Empty),
                EditorStyles.boldLabel);
            if (GUI.Button(new Rect(rect.xMax - 36f, rect.y + 3f, 28f, 20f),
                    SocketLockContent(locked), EditorStyles.miniButton))
            {
                SetSocketsLocked(new[] { _selectedSocketName }, !locked);
            }
            float x = rect.x + 8f;
            if (GUI.Button(new Rect(x, rect.y + 26f, 140f, 22f),
                    new GUIContent("Apply to Frames…",
                        "Copy this socket's position, rotation, and scale onto other frames."),
                    EditorStyles.miniButton))
            {
                OpenSocketInheritPanel(CurrentClip, _selectedSocketName, _selectedFrame);
            }
            x += 148f;
            if (GUI.Button(new Rect(x, rect.y + 26f, 110f, 22f),
                    new GUIContent("Socket Actions…",
                        "Lock, snap presets, Character collider, opaque bounds, copy/paste."),
                    EditorStyles.miniButton))
            {
                ShowSocketContextMenu(CurrentClip, _selectedSocketName);
            }
        }

        bool TryHandleSocketContextClick(SpriteClipDef clip, int frame, Rect cell, Vector2? mouseOverride = null)
        {
            var evt = Event.current;
            if (!IsPreviewContextClick(evt))
                return false;

            Vector2 mouse = mouseOverride ?? evt.mousePosition;
            string hit = FindSocketAt(clip, frame, cell, mouse, includeLocked: true);
            if (hit == null &&
                HitSelectedSocketHandle(cell, clip, frame, mouse) != ColliderHandleKind.None)
                hit = _selectedSocketName;
            if (hit == null && !string.IsNullOrEmpty(_selectedSocketName))
            {
                var bounds = SocketWorldAabb(clip, _selectedSocketName, frame, cell);
                if (bounds.Contains(mouse))
                    hit = _selectedSocketName;
            }
            if (hit == null)
                return false;

            if (_draggingSocket)
                EndSocketDrag(GUIUtility.hotControl, save: false);
            ShowSocketContextMenu(clip, hit);
            evt.Use();
            Repaint();
            return true;
        }

        void ShowSocketContextMenu(SpriteClipDef clip, string hit)
        {
            if (clip == null || string.IsNullOrEmpty(hit))
                return;
            string name = SpriteSocketKeys.CanonicalName(hit);
            if (IsSocketLocked(name))
            {
                // Locked sockets cannot enter selection; still open unlock/actions menu.
                var lockedSelected = new List<string> { name };
                var lockedMenu = new GenericMenu();
                PopulateSocketContextMenu(lockedMenu, clip, name, lockedSelected, 1);
                lockedMenu.ShowAsContext();
                return;
            }
            if (!IsSocketSelected(hit))
                SelectPreviewSocket(hit, SelectionOp.Replace);
            else
                _selectedSocketName = name;

            var selected = new List<string>(_selectedSockets);
            int count = selected.Count;
            var menu = new GenericMenu();
            PopulateSocketContextMenu(menu, clip, name, selected, count);
            menu.ShowAsContext();
        }

        void PopulateSocketContextMenu(GenericMenu menu, SpriteClipDef clip, string name,
            List<string> selected, int count)
        {
            bool anyLocked = false;
            bool allLocked = selected.Count > 0;
            for (int i = 0; i < selected.Count; i++)
            {
                bool locked = IsSocketLocked(selected[i]);
                anyLocked |= locked;
                allLocked &= locked;
            }
            if (selected.Count == 0)
                allLocked = IsSocketLocked(name);

            if (allLocked)
            {
                menu.AddItem(new GUIContent(count > 1 ? $"Unlock {count} Sockets" : "Unlock Socket"),
                    false, () => SetSocketsLocked(selected.Count > 0 ? selected : new List<string> { name }, false));
                menu.AddSeparator(string.Empty);
                menu.AddDisabledItem(new GUIContent("Snap / edit actions (unlock first)"));
                menu.AddSeparator(string.Empty);
            }
            else
            {
                menu.AddItem(new GUIContent(count > 1 ? $"Lock {count} Sockets" : "Lock Socket"),
                    false, () => SetSocketsLocked(selected.Count > 0 ? selected : new List<string> { name }, true));
                if (anyLocked)
                {
                    menu.AddItem(new GUIContent("Unlock Selected"),
                        false, () => SetSocketsLocked(selected, false));
                }
                menu.AddSeparator(string.Empty);
                AddSocketSnapMenuItems(menu, clip, selected, name);
                menu.AddSeparator(string.Empty);
            }

            var catalogItem = _profile?.SocketCatalog?.Find(name);
            if (catalogItem != null && catalogItem.UsesOwnClock)
            {
                int motionTrack = _profile.SocketMotions.IndexOf(
                    _profile.FindSocketMotion(name));
                if (!allLocked)
                {
                    menu.AddItem(new GUIContent("Insert Key Here"), false,
                        () => InsertIndependentMotionKey(false, motionTrack));
                    menu.AddItem(new GUIContent(
                            $"Insert Next Key ({IndependentKeyStepLabel()})"),
                        false, () => InsertIndependentMotionKey(true, motionTrack));
                    AddIndependentDrawLayerMenuItems(menu, selected, CurrentIndependentMotionTime());
                    menu.AddItem(new GUIContent("Snap Motion Center to Character Pivot"), false,
                        () => SnapIndependentMotionCenterToPivot(selected));
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("Insert Key Here"));
                    menu.AddDisabledItem(new GUIContent(
                        $"Insert Next Key ({IndependentKeyStepLabel()})"));
                    menu.AddDisabledItem(new GUIContent("Snap Motion Center to Character Pivot"));
                }
                menu.AddSeparator(string.Empty);
            }
            if (!allLocked)
            {
                menu.AddItem(new GUIContent("Set Transform…"), false, OpenSocketTransformPanel);
                menu.AddItem(new GUIContent("Apply to Frames…"), false,
                    () => OpenSocketInheritPanel(clip, name, _selectedFrame, default, exitGui: false));
                AddSocketPatternMenuItems(menu, clip);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Set Transform…"));
                menu.AddDisabledItem(new GUIContent("Apply to Frames…"));
            }
            menu.AddSeparator(string.Empty);
            if (!allLocked)
            {
                menu.AddItem(new GUIContent(count > 1
                        ? $"Assign Profile to {count} Sockets…"
                        : "Assign Profile…"),
                    false, () => ShowSocketProfilePicker(selected));
                menu.AddItem(new GUIContent(count > 1 ? "Clear Profiles" : "Clear Profile"),
                    false, () => ClearSocketPreviewOnNames(selected));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(count > 1
                    ? $"Assign Profile to {count} Sockets…"
                    : "Assign Profile…"));
                menu.AddDisabledItem(new GUIContent(count > 1 ? "Clear Profiles" : "Clear Profile"));
            }
            menu.AddSeparator(string.Empty);
            if (!allLocked)
                menu.AddItem(new GUIContent("Duplicate"), false,
                    () => DuplicateSocketIdentity(clip, name));
            else
                menu.AddDisabledItem(new GUIContent("Duplicate"));
            bool canGroup = count >= 2 && !allLocked;
            bool canUngroup = false;
            for (int s = 0; s < selected.Count; s++)
            {
                if (_profile != null && _profile.FindSocketInventory(selected[s]) != null)
                    canUngroup = true;
            }
            bool independentGroup = SelectedSocketsUseIndependentGroup(selected, name);
            string groupLabel = independentGroup
                ? "Group Independent Sockets"
                : "Group Frame Sockets";
            string ungroupLabel = independentGroup
                ? "Ungroup Independent Sockets"
                : "Ungroup Frame Sockets";
            if (canGroup)
                menu.AddItem(new GUIContent(groupLabel), false,
                    GroupSelectedSocketInventory);
            else
                menu.AddDisabledItem(new GUIContent(groupLabel));
            if (canUngroup && !allLocked)
                menu.AddItem(new GUIContent(ungroupLabel), false,
                    UngroupSelectedSocketInventory);
            else
                menu.AddDisabledItem(new GUIContent(ungroupLabel));
            if (!allLocked)
            {
                menu.AddItem(new GUIContent(count > 1 ? $"Delete {count} Sockets" : "Delete"),
                    false, () =>
                    {
                        ClearColliderSelection();
                        DeleteSelectedPreviewObjects();
                    });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(count > 1
                    ? $"Delete {count} Sockets (unlock first)"
                    : "Delete (unlock first)"));
            }
        }

        void AddSocketSnapMenuItems(GenericMenu menu, SpriteClipDef clip,
            List<string> selected, string primaryName)
        {
            menu.AddItem(new GUIContent("Snap/Profile Pivot (0, 0)"), false,
                () => SnapSelectedSocketsToLocalPixels(Vector2.zero, "Snap Socket to Profile Pivot",
                    "Snapped socket(s) to profile pivot"));
            menu.AddItem(new GUIContent("Snap/Cell Center"), false,
                () => SnapSelectedSocketsToCellUv(new Vector2(0.5f, 0.5f), "Snap Socket to Cell Center"));
            menu.AddItem(new GUIContent("Snap/Bottom Center — Feet"), false,
                () => SnapSelectedSocketsToCellUv(new Vector2(0.5f, 0f), "Snap Socket to Bottom Center"));
            menu.AddItem(new GUIContent("Snap/Top Center"), false,
                () => SnapSelectedSocketsToCellUv(new Vector2(0.5f, 1f), "Snap Socket to Top Center"));
            menu.AddItem(new GUIContent("Snap/Left Center"), false,
                () => SnapSelectedSocketsToCellUv(new Vector2(0f, 0.5f), "Snap Socket to Left Center"));
            menu.AddItem(new GUIContent("Snap/Right Center"), false,
                () => SnapSelectedSocketsToCellUv(new Vector2(1f, 0.5f), "Snap Socket to Right Center"));
            menu.AddItem(new GUIContent("Snap/Corners/Bottom Left"), false,
                () => SnapSelectedSocketsToCellUv(new Vector2(0f, 0f), "Snap Socket to Bottom Left"));
            menu.AddItem(new GUIContent("Snap/Corners/Bottom Right"), false,
                () => SnapSelectedSocketsToCellUv(new Vector2(1f, 0f), "Snap Socket to Bottom Right"));
            menu.AddItem(new GUIContent("Snap/Corners/Top Left"), false,
                () => SnapSelectedSocketsToCellUv(new Vector2(0f, 1f), "Snap Socket to Top Left"));
            menu.AddItem(new GUIContent("Snap/Corners/Top Right"), false,
                () => SnapSelectedSocketsToCellUv(new Vector2(1f, 1f), "Snap Socket to Top Right"));
            menu.AddSeparator(string.Empty);

            FrameBoxDef character = ResolveCharacterColliderForPivotSnap();
            if (character != null)
            {
                menu.AddItem(new GUIContent("Snap to Character Collider Center",
                        "Uses the selected Character collider, else the first Character box visible on this clip."),
                    false, SnapSelectedSocketsToCharacterColliderCenter);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(
                    "Snap to Character Collider Center (add Character collider first)"));
            }
            menu.AddItem(new GUIContent("Add Character Collider…",
                    "Create a Character (body) square collider on this profile."),
                false, () => PromptAddCharacterColliderForPivot(snapAfter: false));
            menu.AddSeparator(string.Empty);

            if (TryGetOpaqueContentPivot(bottomCenter: false, out _))
            {
                menu.AddItem(new GUIContent("Snap to Opaque Content Center",
                        "Tight AABB center of opaque pixels in the active cell (Grid or Cropped)."),
                    false, () => SnapSelectedSocketsToOpaqueContent(bottomCenter: false));
                menu.AddItem(new GUIContent("Snap to Opaque Content Bottom Center",
                        "Feet of the art — bottom-center of the opaque AABB in the active cell."),
                    false, () => SnapSelectedSocketsToOpaqueContent(bottomCenter: true));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(
                    "Snap to Opaque Content Center (no readable pixels)"));
                menu.AddDisabledItem(new GUIContent(
                    "Snap to Opaque Content Bottom Center (no readable pixels)"));
            }
            menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent("Copy Socket Pose"), false, CopySelectedSocketPose);
            if (_socketPoseClipboardValid)
            {
                menu.AddItem(new GUIContent(
                        $"Paste Socket Pose ({_socketPoseClipboardPosition.x:F1}, {_socketPoseClipboardPosition.y:F1})"),
                    false, PasteSelectedSocketPose);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste Socket Pose"));
            }

            if (selected != null && selected.Count >= 2)
            {
                menu.AddSeparator(string.Empty);
                string primary = string.IsNullOrEmpty(primaryName)
                    ? selected[0]
                    : SpriteSocketKeys.CanonicalName(primaryName);
                for (int i = 0; i < selected.Count; i++)
                {
                    string target = SpriteSocketKeys.CanonicalName(selected[i]);
                    if (SpriteSocketKeys.NamesEqual(target, primary) && selected.Count == 2)
                        continue;
                    string label = SpriteSocketKeys.NamesEqual(target, primary)
                        ? $"Snap Others to {target}"
                        : $"Snap Selected to {target}";
                    string captured = target;
                    menu.AddItem(new GUIContent($"Snap to Socket/{label}"), false,
                        () => SnapSelectedSocketsToSocket(captured));
                }
            }
        }

        bool IsSocketLocked(string socketName)
        {
            if (string.IsNullOrEmpty(socketName) || _profile == null)
                return false;
            _profile.EnsureSocketCatalog();
            var item = _profile.SocketCatalog.Find(socketName);
            return item != null && item.Locked;
        }

        void SetSocketsLocked(IEnumerable<string> socketNames, bool locked)
        {
            if (_profile == null || socketNames == null)
                return;
            _profile.EnsureSocketCatalog();
            var names = new List<string>();
            foreach (string raw in socketNames)
            {
                if (string.IsNullOrEmpty(raw))
                    continue;
                string name = SpriteSocketKeys.CanonicalName(raw);
                if (!names.Exists(n => SpriteSocketKeys.NamesEqual(n, name)))
                    names.Add(name);
            }
            if (names.Count == 0 && !string.IsNullOrEmpty(_selectedSocketName))
                names.Add(SpriteSocketKeys.CanonicalName(_selectedSocketName));
            if (names.Count == 0)
                return;

            RecordProfileUndo(locked
                ? (names.Count == 1 ? "Lock Sprite Socket" : "Lock Sprite Sockets")
                : (names.Count == 1 ? "Unlock Sprite Socket" : "Unlock Sprite Sockets"));
            for (int i = 0; i < names.Count; i++)
            {
                var item = _profile.SocketCatalog.Ensure(names[i]);
                item.Locked = locked;
                if (locked)
                    _selectedSockets.Remove(names[i]);
            }
            if (locked)
            {
                if (!string.IsNullOrEmpty(_selectedSocketName) &&
                    IsSocketLocked(_selectedSocketName))
                    _selectedSocketName = null;
                SyncSocketPrimaryFromSelection();
                _draggingSocket = false;
                _socketHandleKind = ColliderHandleKind.None;
                if (GUIUtility.hotControl != 0)
                    GUIUtility.hotControl = 0;
            }
            SaveDirty();
            SealUndoGroup();
            _status = locked
                ? (names.Count == 1 ? $"Locked socket {names[0]}" : $"Locked {names.Count} sockets")
                : (names.Count == 1 ? $"Unlocked socket {names[0]}" : $"Unlocked {names.Count} sockets");
            Repaint();
        }

        static GUIContent SocketLockContent(bool locked)
        {
            var icon = EditorGUIUtility.IconContent(locked ? "LockIcon-On" : "LockIcon");
            string tooltip = locked
                ? "Unlock socket — allow select and drag in the preview"
                : "Lock socket — prevent select and drag";
            if (icon != null && icon.image != null)
            {
                icon.tooltip = tooltip;
                return icon;
            }
            return new GUIContent(locked ? "L" : "U", tooltip);
        }

        Rect SocketLockBadgeRect(Vector2 screenCenter)
            => new(screenCenter.x + 8f, screenCenter.y - 18f, 14f, 14f);

        void DrawSocketLockBadge(Vector2 screenCenter, bool locked)
        {
            Rect badge = SocketLockBadgeRect(screenCenter);
            EditorGUI.DrawRect(badge, new Color(0.08f, 0.1f, 0.14f, 0.85f));
            GUI.Label(badge, SocketLockContent(locked));
        }

        bool TryHandleSocketLockBadgeClick(SpriteClipDef clip, int frame, Rect cell,
            Vector2? mouseOverride = null)
        {
            var evt = Event.current;
            if (evt.type != EventType.MouseDown || evt.button != 0)
                return false;
            Vector2 mouse = mouseOverride ?? evt.mousePosition;
            var names = CachedUniqueSocketNames(clip);
            for (int i = names.Count - 1; i >= 0; i--)
            {
                string name = names[i];
                bool locked = IsSocketLocked(name);
                bool selected = IsSocketSelected(name);
                if (!locked && !selected)
                    continue;
                if (!TryGetPreviewSocketPose(clip, name, frame, out var position, out _, out _, out _))
                    continue;
                Vector2 screen = SocketToScreen(position, cell);
                if (!SocketLockBadgeRect(screen).Contains(mouse))
                    continue;
                SetSocketsLocked(new[] { name }, !locked);
                evt.Use();
                Repaint();
                return true;
            }
            return false;
        }

        Vector2 SocketLocalFromCellUv(Vector2 uvBottomLeft)
        {
            if (_profile?.Sheet == null)
                return Vector2.zero;
            float sourceWidth = _profile.Sheet.width / (float)Mathf.Max(1, _profile.Columns);
            float sourceHeight = _profile.Sheet.height / (float)Mathf.Max(1, _profile.Rows);
            Vector2 pivot = _profile.Pivot;
            return new Vector2(
                (uvBottomLeft.x - pivot.x) * sourceWidth,
                (uvBottomLeft.y - pivot.y) * sourceHeight);
        }

        void SnapSelectedSocketsToCellUv(Vector2 uvBottomLeft, string undoName)
        {
            SnapSelectedSocketsToLocalPixels(SocketLocalFromCellUv(uvBottomLeft), undoName,
                $"Snapped socket(s) to cell UV {uvBottomLeft.x:F2}, {uvBottomLeft.y:F2}");
        }

        void SnapSelectedSocketsToCharacterColliderCenter()
        {
            var box = ResolveCharacterColliderForPivotSnap();
            if (box == null)
            {
                PromptAddCharacterColliderForPivot(snapAfter: false);
                return;
            }
            Vector2 pivotUv = ColliderCenterAsPivot(box);
            SnapSelectedSocketsToLocalPixels(SocketLocalFromCellUv(pivotUv),
                "Snap Socket to Character Collider",
                $"Snapped socket(s) to Character collider #{box.Id} center");
        }

        void SnapSelectedSocketsToOpaqueContent(bool bottomCenter)
        {
            if (!TryGetOpaqueContentPivot(bottomCenter, out Vector2 pivotUv))
            {
                _status = "Opaque content snap needs a readable sheet texture";
                Repaint();
                return;
            }
            SnapSelectedSocketsToLocalPixels(SocketLocalFromCellUv(pivotUv),
                bottomCenter
                    ? "Snap Socket to Opaque Bottom Center"
                    : "Snap Socket to Opaque Center",
                bottomCenter
                    ? "Snapped socket(s) to opaque bottom-center"
                    : "Snapped socket(s) to opaque center");
        }

        void SnapSelectedSocketsToSocket(string targetName)
        {
            var clip = CurrentClip;
            if (clip == null || string.IsNullOrEmpty(targetName))
                return;
            int frame = Mathf.Clamp(_selectedFrame, 0, Mathf.Max(0, clip.Frames.Length - 1));
            if (!TryGetPreviewSocketPose(clip, targetName, frame,
                    out var position, out var angle, out var scale, out _))
            {
                _status = $"Socket {targetName} has no pose to snap to";
                Repaint();
                return;
            }
            ApplyPoseToSelectedSockets(position, angle, scale, keepRotationScale: true,
                "Snap Sockets to Socket",
                $"Snapped selected sockets to {targetName}");
        }

        void CopySelectedSocketPose()
        {
            var clip = CurrentClip;
            string name = _selectedSocketName;
            if (clip == null || string.IsNullOrEmpty(name))
            {
                foreach (string selected in _selectedSockets)
                {
                    name = selected;
                    break;
                }
            }
            if (clip == null || string.IsNullOrEmpty(name))
                return;
            int frame = Mathf.Clamp(_selectedFrame, 0, Mathf.Max(0, clip.Frames.Length - 1));
            if (!TryGetPreviewSocketPose(clip, name, frame,
                    out var position, out var angle, out var scale, out _))
            {
                _status = "No socket pose to copy";
                return;
            }
            _socketPoseClipboardPosition = position;
            _socketPoseClipboardAngle = angle;
            _socketPoseClipboardScale = scale;
            _socketPoseClipboardValid = true;
            _status = $"Copied socket pose ({position.x:F1}, {position.y:F1})";
            Repaint();
        }

        void PasteSelectedSocketPose()
        {
            if (!_socketPoseClipboardValid)
                return;
            ApplyPoseToSelectedSockets(
                _socketPoseClipboardPosition,
                _socketPoseClipboardAngle,
                _socketPoseClipboardScale,
                keepRotationScale: true,
                "Paste Socket Pose",
                $"Pasted socket pose ({_socketPoseClipboardPosition.x:F1}, {_socketPoseClipboardPosition.y:F1})");
        }

        void SnapSelectedSocketsToLocalPixels(Vector2 targetClipPixels, string undoName, string status)
        {
            ApplyPoseToSelectedSockets(targetClipPixels, 0f, Vector2.one,
                keepRotationScale: false, undoName, status);
        }

        void ApplyPoseToSelectedSockets(Vector2 targetClipPixels, float angle, Vector2 scale,
            bool keepRotationScale, string undoName, string status)
        {
            var clip = CurrentClip;
            if (clip == null || _profile == null)
                return;
            int frame = Mathf.Clamp(_selectedFrame, 0, Mathf.Max(0, clip.Frames.Length - 1));
            var names = new List<string>();
            if (_selectedSockets.Count > 0)
            {
                foreach (string selected in _selectedSockets)
                    names.Add(SpriteSocketKeys.CanonicalName(selected));
            }
            else if (!string.IsNullOrEmpty(_selectedSocketName))
                names.Add(SpriteSocketKeys.CanonicalName(_selectedSocketName));
            if (names.Count == 0)
                return;

            // Drop locked sockets from the edit set.
            names.RemoveAll(IsSocketLocked);
            if (names.Count == 0)
            {
                _status = "Selected sockets are locked — unlock to edit";
                Repaint();
                return;
            }

            RecordProfileUndo(string.IsNullOrEmpty(undoName) ? "Edit Sprite Socket" : undoName);
            int changed = 0;
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (IsIndependentSocketName(name))
                {
                    if (ApplyIndependentSocketAbsolutePose(clip, name, targetClipPixels,
                            angle, scale, keepRotationScale))
                        changed++;
                    continue;
                }

                clip.Sockets ??= new List<FrameSocketDef>();
                var key = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, name, frame);
                key.LocalPosition = new Vector2(
                    Mathf.Round(targetClipPixels.x), Mathf.Round(targetClipPixels.y));
                if (keepRotationScale)
                {
                    key.LocalAngle = angle;
                    key.LocalScale = scale;
                }
                changed++;
            }
            if (changed == 0)
            {
                _status = "No socket keys updated";
                Repaint();
                return;
            }
            SaveDirty();
            SealUndoGroup();
            _status = status ?? $"Updated {changed} socket{Plural(changed)}";
            Repaint();
        }

        bool ApplyIndependentSocketAbsolutePose(SpriteClipDef clip, string name,
            Vector2 targetClipPixels, float angle, Vector2 scale, bool keepRotationScale)
        {
            var track = _profile?.FindSocketMotion(name);
            if (track?.Keys == null || track.Keys.Count == 0)
                return false;
            if (!TryGetPreviewSocketPose(clip, name, _selectedFrame,
                    out var currentClip, out _, out _, out _))
                currentClip = MotionKeyToClipPixels(clip, track, track.Keys[0].LocalPosition);

            Vector2 deltaClip = targetClipPixels - currentClip;
            if (deltaClip.sqrMagnitude <= 0.000001f && !keepRotationScale)
                return false;

            for (int k = 0; k < track.Keys.Count; k++)
            {
                var key = track.Keys[k];
                if (key == null)
                    continue;
                Vector2 clipPos = MotionKeyToClipPixels(clip, track, key.LocalPosition) + deltaClip;
                Vector2 reference = ClipPixelsToMotionKey(clip, track, clipPos);
                key.LocalPosition = new Vector2(Mathf.Round(reference.x), Mathf.Round(reference.y));
                if (keepRotationScale)
                {
                    // Absolute paste: set every key to the pasted rotation/scale so the
                    // independent path keeps a consistent pose after paste.
                    key.LocalAngle = angle;
                    key.LocalScale = scale;
                }
            }
            return true;
        }

        void SnapIndependentMotionCenterToPivot(IEnumerable<string> socketNames)
        {
            var tracks = new List<SpriteSocketMotionTrack>();
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
            foreach (string socketName in socketNames)
            {
                var track = _profile?.FindSocketMotion(socketName);
                if (track?.Keys == null || track.Keys.Count == 0 || tracks.Contains(track))
                    continue;
                tracks.Add(track);
                for (int i = 0; i < track.Keys.Count; i++)
                {
                    min = Vector2.Min(min, track.Keys[i].LocalPosition);
                    max = Vector2.Max(max, track.Keys[i].LocalPosition);
                }
            }

            if (tracks.Count == 0)
            {
                _status = "No independent motion keys to snap";
                return;
            }

            Vector2 center = (min + max) * 0.5f;
            if (center.sqrMagnitude <= 0.000001f)
            {
                _status = "Independent motion is already centered on the character pivot";
                return;
            }

            RecordProfileUndo("Snap Independent Motion to Character Pivot");
            for (int t = 0; t < tracks.Count; t++)
            {
                for (int k = 0; k < tracks[t].Keys.Count; k++)
                    tracks[t].Keys[k].LocalPosition -= center;
            }
            SaveDirty();
            SealUndoGroup();
            _status = $"Centered {tracks.Count} independent motion track{Plural(tracks.Count)} on the character pivot";
            Repaint();
        }

        void OpenSocketTransformPanel()
        {
            var clip = CurrentClip;
            if (clip == null)
                return;

            _socketTransformNames.Clear();
            if (_selectedSockets.Count > 0)
            {
                foreach (string name in _selectedSockets)
                    _socketTransformNames.Add(SpriteSocketKeys.CanonicalName(name));
            }
            if (_socketTransformNames.Count == 0 && !string.IsNullOrEmpty(_selectedSocketName))
                _socketTransformNames.Add(SpriteSocketKeys.CanonicalName(_selectedSocketName));
            if (_socketTransformNames.Count == 0)
                return;

            CloseSocketInheritPanel();
            _socketTransformAllFrames = false;
            _showSocketTransformPanel = true;
            float width = 308f;
            float height = 352f;
            _socketTransformPanelRect = new Rect(
                Mathf.Max(8f, (position.width - width) * 0.5f),
                Mathf.Max(48f, (position.height - height) * 0.28f),
                width, height);
            _status = _socketTransformNames.Count == 1
                ? $"Set Transform  •  {_socketTransformNames[0]}"
                : $"Set Transform  •  {_socketTransformNames.Count} sockets";
            Repaint();
        }

        void CloseSocketTransformPanel()
        {
            _showSocketTransformPanel = false;
            _socketTransformDragging = false;
            if (GUIUtility.hotControl != 0)
                GUIUtility.hotControl = 0;
        }

        void CloseSocketInheritPanel()
        {
            _showSocketInheritPanel = false;
            _socketInheritDragging = false;
            if (GUIUtility.hotControl != 0)
                GUIUtility.hotControl = 0;
        }

        void SelectSocketInheritFrames(SpriteClipDef clip, string mode)
        {
            if (clip?.Frames == null)
                return;
            _socketInheritFrames.Clear();
            int count = clip.Frames.Length;
            int source = Mathf.Clamp(_socketInheritSourceFrame, 0, count - 1);
            switch (mode)
            {
                case "all":
                    for (int i = 0; i < count; i++)
                        _socketInheritFrames.Add(i);
                    break;
                case "none":
                    break;
                case "missing":
                    for (int i = 0; i < count; i++)
                    {
                        for (int n = 0; n < _socketInheritNames.Count; n++)
                        {
                            if (SpriteSocketKeys.FindOnFrame(clip.Sockets, _socketInheritNames[n], i) == null)
                            {
                                _socketInheritFrames.Add(i);
                                break;
                            }
                        }
                    }
                    break;
                case "rest":
                    for (int i = source; i < count; i++)
                        _socketInheritFrames.Add(i);
                    break;
                case "timeline":
                    foreach (int frame in _selectedFrames)
                        _socketInheritFrames.Add(frame);
                    break;
            }
        }

        void ToggleSocketInheritFrame(int frame, SelectionOp op)
        {
            switch (op)
            {
                case SelectionOp.Range:
                case SelectionOp.RangeAdd:
                    if (_socketInheritRangeAnchor >= 0)
                    {
                        int a = Mathf.Min(_socketInheritRangeAnchor, frame);
                        int b = Mathf.Max(_socketInheritRangeAnchor, frame);
                        if (op == SelectionOp.Range)
                            _socketInheritFrames.Clear();
                        for (int i = a; i <= b; i++)
                            _socketInheritFrames.Add(i);
                        return;
                    }
                    _socketInheritFrames.Add(frame);
                    _socketInheritRangeAnchor = frame;
                    return;
                case SelectionOp.Add:
                    _socketInheritFrames.Add(frame);
                    _socketInheritRangeAnchor = frame;
                    return;
                case SelectionOp.Toggle:
                    if (!_socketInheritFrames.Add(frame))
                        _socketInheritFrames.Remove(frame);
                    _socketInheritRangeAnchor = frame;
                    return;
                case SelectionOp.Subtract:
                    _socketInheritFrames.Remove(frame);
                    return;
                case SelectionOp.Intersect:
                {
                    bool keep = _socketInheritFrames.Contains(frame);
                    _socketInheritFrames.Clear();
                    if (keep)
                        _socketInheritFrames.Add(frame);
                    _socketInheritRangeAnchor = frame;
                    return;
                }
                default:
                    _socketInheritFrames.Clear();
                    _socketInheritFrames.Add(frame);
                    _socketInheritRangeAnchor = frame;
                    return;
            }
        }

        void JumpPreviewToFrame(SpriteClipDef clip, int frame)
        {
            if (clip?.Frames == null || frame < 0 || frame >= clip.Frames.Length)
                return;
            _playing = false;
            SelectOnlyFrame(frame);
            _previewTime = PreviewTimeAtFrame(clip, frame);
            _selectedOnionFrame = -1;
            Repaint();
        }

        int ApplySocketInherit(SpriteClipDef clip, bool position, bool rotation, bool scale,
            ICollection<int> frames, string undoName)
        {
            if (clip?.Frames == null || frames == null || frames.Count == 0)
                return 0;
            if (!position && !rotation && !scale)
                return 0;

            RecordProfileUndo(undoName);
            int changed = 0;
            int sourceFrame = Mathf.Clamp(_socketInheritSourceFrame, 0, clip.Frames.Length - 1);
            for (int n = 0; n < _socketInheritNames.Count; n++)
            {
                string name = _socketInheritNames[n];
                if (!SpriteSocketKeys.TryGetPose(clip.Sockets, name, sourceFrame,
                        out var pose, out var angle, out var poseScale, out _))
                    continue;
                foreach (int frame in frames)
                {
                    if (frame < 0 || frame >= clip.Frames.Length)
                        continue;
                    var key = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, name, frame);
                    if (position)
                        key.LocalPosition = pose;
                    if (rotation)
                        key.LocalAngle = angle;
                    if (scale)
                        key.LocalScale = poseScale;
                    changed++;
                }
            }
            SaveDirty();
            Repaint();
            return changed;
        }

        int ResetSocketInherit(SpriteClipDef clip, ICollection<int> frames)
        {
            if (clip?.Frames == null || frames == null || frames.Count == 0)
                return 0;
            if (!_socketInheritPosition && !_socketInheritRotation && !_socketInheritScale)
                return 0;

            RecordProfileUndo("Reset Sprite Socket Pose");
            int changed = 0;
            for (int n = 0; n < _socketInheritNames.Count; n++)
            {
                string name = _socketInheritNames[n];
                foreach (int frame in frames)
                {
                    if (frame < 0 || frame >= clip.Frames.Length)
                        continue;
                    var key = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, name, frame);
                    if (_socketInheritPosition)
                        key.LocalPosition = Vector2.zero;
                    if (_socketInheritRotation)
                        key.LocalAngle = 0f;
                    if (_socketInheritScale)
                        key.LocalScale = Vector2.one;
                    changed++;
                }
            }
            SaveDirty();
            Repaint();
            return changed;
        }

        int ClearSocketInheritKeys(SpriteClipDef clip, ICollection<int> frames)
        {
            if (clip?.Sockets == null || frames == null || frames.Count == 0)
                return 0;

            RecordProfileUndo("Clear Sprite Socket Frame Keys");
            int changed = 0;
            for (int n = 0; n < _socketInheritNames.Count; n++)
            {
                string name = _socketInheritNames[n];
                foreach (int frame in frames)
                {
                    if (SpriteSocketKeys.RemoveFrameKey(clip.Sockets, name, frame))
                        changed++;
                }
            }
            SaveDirty();
            Repaint();
            return changed;
        }

        void ArmSocketPlacement(bool independent)
        {
            CancelColliderCreation(null);
            _socketPlacementArmed = true;
            _socketPlacementIndependent = independent;
            _draggingSocket = false;
            _socketHandleKind = ColliderHandleKind.None;
            _status = _socketPlacementIndependent
                ? "Independent Motion tool armed — click the preview to place"
                : "Frame-Attached Socket tool armed — click the preview to place";
            Repaint();
        }

        void CancelSocketPlacement(string status)
        {
            _socketPlacementArmed = false;
            if (!string.IsNullOrEmpty(status))
                _status = status;
            Repaint();
        }

        void ClearSocketToolState()
        {
            _socketPlacementArmed = false;
            ClearSocketSelection();
            _draggingSocket = false;
            _socketHandleKind = ColliderHandleKind.None;
            _socketTransformName = null;
            _socketMoveNames.Clear();
            _socketMoveStarts.Clear();
            _socketMoveUndoRecorded = false;
        }

        void ClearSocketSelection()
        {
            _selectedSockets.Clear();
            _selectedSocketName = null;
            _socketListAnchor = -1;
            _socketListMarqueePending = false;
            _socketListMarqueeActive = false;
        }

        void HandleSocketListMarquee(List<string> names)
        {
            var evt = Event.current;
            if (evt == null || names == null || names.Count == 0 ||
                _socketListRowRects.Count != names.Count)
                return;

            if (_socketListMarqueePending && evt.type == EventType.MouseDrag && evt.button == 0)
            {
                if (!_socketListMarqueeActive &&
                    Vector2.Distance(_socketListMarqueeStart, evt.mousePosition) >= 4f)
                    _socketListMarqueeActive = true;
                if (_socketListMarqueeActive)
                {
                    var box = RectFromPoints(_socketListMarqueeStart, evt.mousePosition);
                    _selectionScratchNames.Clear();
                    for (int i = 0; i < names.Count; i++)
                    {
                        if (IsSocketLocked(names[i]))
                            continue;
                        if (box.Overlaps(_socketListRowRects[i], true))
                            _selectionScratchNames.Add(SpriteSocketKeys.CanonicalName(names[i]));
                    }
                    ApplyMarqueeOnto(_selectedSockets, _socketListMarqueeBaseline,
                        _selectionScratchNames, _socketListMarqueeOp);
                    SyncSocketPrimaryFromSelection();
                    evt.Use();
                }
            }

            if (_socketListMarqueeActive && evt.type == EventType.Repaint)
            {
                var box = RectFromPoints(_socketListMarqueeStart, evt.mousePosition);
                EditorGUI.DrawRect(box, new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.14f));
                DrawBorder(box, AccentColor, 1f);
            }

            if (_socketListMarqueePending &&
                (evt.type == EventType.MouseUp || evt.rawType == EventType.MouseUp))
            {
                _socketListMarqueePending = false;
                _socketListMarqueeActive = false;
                _status = PreviewSelectionStatus("Marquee selected");
                if (evt.type == EventType.MouseUp)
                    evt.Use();
            }
        }

        static void DrawSocketListCheckbox(Rect rowCheck, bool on)
        {
            var box = new Rect(rowCheck.x + 2f, rowCheck.y + 9f, 14f, 14f);
            EditorGUI.DrawRect(box, new Color(0.12f, 0.12f, 0.12f, 1f));
            DrawBorder(box, new Color(0.62f, 0.62f, 0.62f, 1f), 1f);
            if (!on)
                return;
            EditorGUI.DrawRect(new Rect(box.x + 3f, box.y + 3f, 8f, 8f), AccentColor);
        }

        static bool PointerHasShift(Event evt)
            => evt != null && (evt.shift || (evt.modifiers & EventModifiers.Shift) != 0);

        static bool PointerHasAction(Event evt)
            => evt != null && (evt.control || evt.command ||
                               (evt.modifiers & EventModifiers.Control) != 0 ||
                               (evt.modifiers & EventModifiers.Command) != 0);

        static bool PointerHasAlt(Event evt)
            => evt != null && (evt.alt || (evt.modifiers & EventModifiers.Alt) != 0);

        static SelectionOp ReadSelectionOp(Event evt, bool orderedList = false)
        {
            bool shift = PointerHasShift(evt);
            bool ctrl = PointerHasAction(evt);
            bool alt = PointerHasAlt(evt);
            if (shift && alt)
                return SelectionOp.Intersect;
            if (alt)
                return SelectionOp.Subtract;
            if (ctrl && shift && orderedList)
                return SelectionOp.RangeAdd;
            if (ctrl)
                return SelectionOp.Toggle;
            if (shift && orderedList)
                return SelectionOp.Range;
            if (shift)
                return SelectionOp.Add;
            return SelectionOp.Replace;
        }

        static bool SelectionOpAllowsMarquee(SelectionOp op)
            => op is SelectionOp.Replace or SelectionOp.Add or SelectionOp.Subtract
                or SelectionOp.Toggle or SelectionOp.Intersect;

        static void ApplyMarqueeOnto<T>(HashSet<T> dest, HashSet<T> baseline, List<T> hits, SelectionOp op)
        {
            dest.Clear();
            switch (op)
            {
                case SelectionOp.Add:
                case SelectionOp.RangeAdd:
                    foreach (var item in baseline)
                        dest.Add(item);
                    for (int i = 0; i < hits.Count; i++)
                        dest.Add(hits[i]);
                    break;
                case SelectionOp.Subtract:
                    foreach (var item in baseline)
                        dest.Add(item);
                    for (int i = 0; i < hits.Count; i++)
                        dest.Remove(hits[i]);
                    break;
                case SelectionOp.Toggle:
                    foreach (var item in baseline)
                        dest.Add(item);
                    for (int i = 0; i < hits.Count; i++)
                    {
                        T hit = hits[i];
                        if (baseline.Contains(hit))
                            dest.Remove(hit);
                        else
                            dest.Add(hit);
                    }
                    break;
                case SelectionOp.Intersect:
                    for (int i = 0; i < hits.Count; i++)
                    {
                        T hit = hits[i];
                        if (baseline.Contains(hit))
                            dest.Add(hit);
                    }
                    break;
                default:
                    for (int i = 0; i < hits.Count; i++)
                        dest.Add(hits[i]);
                    break;
            }
        }

        void SelectSocketsFromListRow(List<string> names, int index, SelectionOp op)
        {
            if (names == null || index < 0 || index >= names.Count)
                return;
            ReleaseShortcutKeyboardFocus();
            _selectedSocketDrawFrame = -1;
            _selectedSocketDrawName = null;
            string name = SpriteSocketKeys.CanonicalName(names[index]);
            if (op is SelectionOp.Range or SelectionOp.RangeAdd)
            {
                // Keep the socket anchor and selection intact. ClearColliderSelection()
                // also clears sockets, which previously erased the range anchor and made
                // Shift-click select only the clicked row.
                _selectedColliders.Clear();
                ClearColliderTransform();
                if (op == SelectionOp.Range)
                    _selectedSockets.Clear();
                if (_socketListAnchor >= 0 && _socketListAnchor < names.Count)
                {
                    int a = Mathf.Min(_socketListAnchor, index);
                    int b = Mathf.Max(_socketListAnchor, index);
                    for (int i = a; i <= b; i++)
                    {
                        string ranged = SpriteSocketKeys.CanonicalName(names[i]);
                        if (IsSocketLocked(ranged))
                            continue;
                        _selectedSockets.Add(ranged);
                    }
                }
                else if (!IsSocketLocked(name))
                    _selectedSockets.Add(name);
                if (_selectedSockets.Count == 0)
                {
                    _selectedSocketName = null;
                    _status = "No unlocked sockets in range";
                }
                else
                {
                    if (!_selectedSockets.Contains(name) || IsSocketLocked(name))
                    {
                        foreach (string selected in _selectedSockets)
                        {
                            name = selected;
                            break;
                        }
                    }
                    _selectedSocketName = name;
                }
                _selectedSockets.RemoveWhere(IsSocketLocked);
                if (_selectedSockets.Count == 0)
                    _selectedSocketName = null;
                else if (string.IsNullOrEmpty(_selectedSocketName) ||
                         !_selectedSockets.Contains(_selectedSocketName))
                {
                    foreach (string selected in _selectedSockets)
                    {
                        _selectedSocketName = selected;
                        break;
                    }
                }
                SyncSocketPrimaryFromSelection();
                _selectedEventFrame = -1;
                _selectedEventIndex = -1;
                _selectedOnionFrame = -1;
                if (_socketListAnchor < 0)
                    _socketListAnchor = index;
                _status = PreviewSelectionStatus();
            }
            else
            {
                SelectPreviewSocket(name, op);
                if (op is not (SelectionOp.Subtract or SelectionOp.Intersect))
                    _socketListAnchor = index;
            }
            Repaint();
        }

        void DrawSocketInventoryList(SpriteClipDef clip, List<string> names, bool independentView)
        {
            _profile.EnsureSocketInventories();
            var drawn = new HashSet<SpriteSocketInventory>();
            var memberRects = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
            float rowH = independentView ? 52f : 32f;
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                var inventory = _profile.FindSocketInventory(name);
                if (inventory == null)
                {
                    var row = DrawSocketListCard(clip, names, i, independentView, rowH, 0f, true);
                    _socketListRowRects.Add(row);
                    continue;
                }
                if (drawn.Add(inventory))
                    DrawSocketInventoryCard(clip, names, inventory, independentView, memberRects);
                string key = SpriteSocketKeys.CanonicalName(name);
                _socketListRowRects.Add(memberRects.TryGetValue(key, out var cached) ? cached : new Rect());
            }
        }

        void DrawSocketInventoryCard(SpriteClipDef clip, List<string> names,
            SpriteSocketInventory inventory, bool independentView,
            Dictionary<string, Rect> memberRects)
        {
            var visible = new List<int>();
            for (int i = 0; i < names.Count; i++)
            {
                if (inventory.SocketNames.Exists(n => SpriteSocketKeys.NamesEqual(n, names[i])))
                    visible.Add(i);
            }
            if (visible.Count == 0)
                return;

            bool hasIndependent = false;
            for (int v = 0; v < visible.Count; v++)
            {
                if (_profile.FindSocketMotion(names[visible[v]]) != null)
                    hasIndependent = true;
            }

            var header = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true), GUILayout.Height(28f));
            GUILayout.Space(2f);
            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.helpBox.Draw(header, false, false, false, false);
                EditorGUI.DrawRect(header, new Color(0.28f, 0.16f, 0.16f, 0.45f));
            }

            var foldRect = new Rect(header.x + 4f, header.y + 4f, 16f, 20f);
            bool expanded = !inventory.Folded;
            bool nextExpanded = EditorGUI.Foldout(foldRect, expanded, GUIContent.none, true);
            if (nextExpanded != expanded)
                inventory.Folded = !nextExpanded;

            bool anySelected = false;
            for (int v = 0; v < visible.Count; v++)
                anySelected |= IsSocketSelected(names[visible[v]]);
            var checkRect = new Rect(foldRect.xMax + 2f, header.y, 18f, 28f);
            DrawSocketListCheckbox(checkRect, anySelected);

            int inventoryIndex = _profile.SocketInventories != null
                ? _profile.SocketInventories.IndexOf(inventory)
                : -1;
            var nameRect = new Rect(checkRect.xMax + 6f, header.y + 5f, 160f, 18f);
            bool renamingInv = inventoryIndex >= 0 && inventoryIndex == _renamingInventoryIndex;
            if (renamingInv)
            {
                DrawInlineRenameField(nameRect, InventoryRenameControl,
                    ref _renameInventoryValue, ref _focusInventoryRename,
                    EditorStyles.boldLabel);
            }
            else
            {
                GUI.Label(nameRect, new GUIContent(inventory.Name,
                    "Click to select group. Double-click or F2 to rename."),
                    EditorStyles.boldLabel);
            }
            GUI.Label(new Rect(nameRect.xMax + 6f, header.y + 6f, 80f, 16f),
                $"{visible.Count} sockets", _mutedStyle);

            if (independentView && hasIndependent)
            {
                var spaceLabel = new Rect(header.xMax - 168f, header.y + 5f, 38f, 18f);
                var spaceRect = new Rect(spaceLabel.xMax, header.y + 4f, 120f, 20f);
                GUI.Label(spaceLabel, new GUIContent("Space",
                    "Shared by every Independent Motion socket in this group."),
                    EditorStyles.miniLabel);
                int anchorSpace = Mathf.Clamp(inventory.AnchorSpace, 0, SocketAnchorSpaceLabels.Length - 1);
                int nextAnchor = EditorGUI.Popup(spaceRect, anchorSpace, SocketAnchorSpaceLabels);
                if (nextAnchor != anchorSpace)
                {
                    RecordProfileUndo("Set Socket Group Space");
                    _profile.SetInventorySpace(inventory, (byte)nextAnchor);
                    SaveDirty();
                    SealUndoGroup();
                    _status = $"{inventory.Name} space: {SocketAnchorSpaceLabels[nextAnchor]}";
                }
            }

            var headerEvt = Event.current;
            if (headerEvt.type == EventType.MouseDown && headerEvt.button == 0 &&
                header.Contains(headerEvt.mousePosition) &&
                !foldRect.Contains(headerEvt.mousePosition))
            {
                if (inventoryIndex >= 0)
                    _renameInventoryTargetIndex = inventoryIndex;
                SelectInventoryMembers(names, visible, ReadSelectionOp(headerEvt, orderedList: true));
                if (!renamingInv && nameRect.Contains(headerEvt.mousePosition) &&
                    headerEvt.clickCount >= 2 && inventoryIndex >= 0)
                    BeginInventoryRename(inventoryIndex);
                headerEvt.Use();
            }
            if (headerEvt.type == EventType.ContextClick && header.Contains(headerEvt.mousePosition) ||
                headerEvt.type == EventType.MouseDown && headerEvt.button == 1 &&
                header.Contains(headerEvt.mousePosition))
            {
                SelectInventoryMembers(names, visible, SelectionOp.Replace);
                var selected = new List<string>(_selectedSockets);
                var menu = new GenericMenu();
                PopulateSocketContextMenu(menu, clip, names[visible[0]], selected, selected.Count);
                menu.ShowAsContext();
                headerEvt.Use();
            }

            float memberH = independentView ? 36f : 32f;
            if (!inventory.Folded)
            {
                for (int v = 0; v < visible.Count; v++)
                {
                    int index = visible[v];
                    var row = DrawSocketListCard(clip, names, index, independentView, memberH, 10f, false);
                    memberRects[SpriteSocketKeys.CanonicalName(names[index])] = row;
                }
            }
            else
            {
                for (int v = 0; v < visible.Count; v++)
                    memberRects[SpriteSocketKeys.CanonicalName(names[visible[v]])] = header;
            }
        }

        void SelectInventoryMembers(List<string> names, List<int> visible, SelectionOp op)
        {
            if (visible.Count == 0)
                return;
            if (op == SelectionOp.Replace)
                _selectedSockets.Clear();
            for (int i = 0; i < visible.Count; i++)
                _selectedSockets.Add(SpriteSocketKeys.CanonicalName(names[visible[i]]));
            _selectedSocketName = SpriteSocketKeys.CanonicalName(names[visible[0]]);
            SyncSocketPrimaryFromSelection();
            _socketListAnchor = visible[0];
            _status = PreviewSelectionStatus();
            Repaint();
        }

        Rect DrawSocketListCard(SpriteClipDef clip, List<string> names, int i,
            bool independentView, float rowH, float indent, bool showSpace)
        {
            const float rowTopH = 32f;
            const float checkW = 18f;
            const float lockW = 28f;
            const float dupW = 72f;
            const float thumbW = 32f;
            string name = names[i];
            bool locked = IsSocketLocked(name);
            bool selected = IsSocketSelected(name);
            SpriteSocketKeys.TryGetPose(clip.Sockets, name, _selectedFrame,
                out _, out _, out _, out bool onFrame);
            Color swatch = SpriteSocketKeys.ColorForIndex(i);
            var catalogItem = _profile.SocketCatalog.Find(name);
            var row = GUILayoutUtility.GetRect(0f, rowH, GUILayout.ExpandWidth(true), GUILayout.Height(rowH));
            GUILayout.Space(3f);
            if (indent > 0f)
                row = new Rect(row.x + indent, row.y, row.width - indent, row.height);

            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.helpBox.Draw(row, false, false, false, false);
                if (selected)
                    EditorGUI.DrawRect(row, new Color(0.22f, 0.4f, 0.55f, 0.55f));
            }

            var checkRect = new Rect(row.x + 4f, row.y, checkW, rowTopH);
            var chipRect = new Rect(checkRect.xMax + 2f, row.y + 10f, 12f, 12f);
            var dupRect = new Rect(row.xMax - dupW - 4f, row.y + 2f, dupW, rowTopH - 4f);
            var lockRect = new Rect(dupRect.x - lockW - 4f, row.y + 2f, lockW, rowTopH - 4f);
            var thumbRect = new Rect(lockRect.x - thumbW - 4f, row.y, thumbW, rowTopH);
            var labelRect = new Rect(chipRect.xMax + 6f, row.y,
                Mathf.Max(8f, thumbRect.x - chipRect.xMax - 8f), rowTopH);
            var spaceLabelRect = showSpace && independentView
                ? new Rect(labelRect.x, row.y + 32f, 38f, 17f)
                : default;
            var spaceRect = showSpace && independentView
                ? new Rect(spaceLabelRect.xMax + 2f, row.y + 31f,
                    Mathf.Max(48f, row.xMax - spaceLabelRect.xMax - 6f), 19f)
                : default;

            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 &&
                row.Contains(evt.mousePosition) && !dupRect.Contains(evt.mousePosition) &&
                !lockRect.Contains(evt.mousePosition) &&
                (!showSpace || !independentView || !spaceRect.Contains(evt.mousePosition)))
            {
                if (locked)
                {
                    _status = $"Socket {name} is locked — unlock to select";
                    evt.Use();
                    Repaint();
                }
                else
                {
                    if (_socketListAnchor >= 0 &&
                        _socketListAnchorIndependent != independentView)
                        _socketListAnchor = -1;
                    bool onCheck = checkRect.Contains(evt.mousePosition);
                    var op = onCheck ? SelectionOp.Toggle : ReadSelectionOp(evt, orderedList: true);
                    bool canMarquee = !onCheck && SelectionOpAllowsMarquee(op);
                    if (canMarquee)
                    {
                        _socketListMarqueeOp = op;
                        _socketListMarqueeBaseline.Clear();
                        foreach (string selectedName in _selectedSockets)
                            _socketListMarqueeBaseline.Add(selectedName);
                    }
                    SelectSocketsFromListRow(names, i, op);
                    _socketListAnchorIndependent = independentView;
                    _socketListMarqueeIndependent = independentView;
                    _socketListMarqueePending = canMarquee;
                    _socketListMarqueeActive = false;
                    _socketListMarqueeStart = evt.mousePosition;
                    evt.Use();
                }
            }

            DrawSocketListCheckbox(checkRect, selected);
            EditorGUI.DrawRect(chipRect, swatch);
            var motionTrack = independentView ? _profile.FindSocketMotion(name) : null;
            string rowLabel = independentView
                ? $"{i}. {name}  •  {motionTrack?.Keys?.Count ?? 0} keys"
                : onFrame ? $"{i}. {name}" : $"{i}. {name}  (other frame)";
            GUI.Label(labelRect, rowLabel, selected ? EditorStyles.whiteLabel : EditorStyles.label);
            if (showSpace && independentView && motionTrack != null)
            {
                GUI.Label(spaceLabelRect, new GUIContent("Space",
                    "Character follows the character pivot. World stays anchored to the character's spawn-time world pivot."),
                    EditorStyles.miniLabel);
                int anchorSpace = Mathf.Clamp(motionTrack.AnchorSpace, 0,
                    SocketAnchorSpaceLabels.Length - 1);
                int nextAnchorSpace = EditorGUI.Popup(spaceRect, anchorSpace,
                    SocketAnchorSpaceLabels);
                if (nextAnchorSpace != anchorSpace)
                {
                    RecordProfileUndo("Set Independent Motion Space");
                    motionTrack.AnchorSpace = (byte)nextAnchorSpace;
                    SaveDirty();
                    SealUndoGroup();
                    _status = $"{name} space: {SocketAnchorSpaceLabels[nextAnchorSpace]}";
                }
            }
            DrawSocketPreviewThumbnail(thumbRect, catalogItem);
            if (GUI.Button(lockRect, SocketLockContent(locked), EditorStyles.miniButton))
            {
                SetSocketsLocked(new[] { name }, !locked);
                GUIUtility.ExitGUI();
            }
            using (new EditorGUI.DisabledScope(locked))
            {
                if (GUI.Button(dupRect, new GUIContent("Duplicate",
                        "Copy this socket's keys and catalog onto a new name."),
                    EditorStyles.miniButton))
                {
                    DuplicateSocketIdentity(clip, name);
                    GUIUtility.ExitGUI();
                }
            }

            HandleSocketListRowContext(row, clip, names, i);
            HandleSocketPreviewDragDrop(row, name);
            return row;
        }

        bool SelectedSocketsUseIndependentGroup(IEnumerable<string> selected, string fallbackName)
        {
            if (_timelineView == TimelineView.Sockets)
                return true;
            if (!string.IsNullOrEmpty(fallbackName) && IsIndependentSocketName(fallbackName))
                return true;
            if (selected == null)
                return false;
            foreach (string name in selected)
            {
                if (IsIndependentSocketName(name))
                    return true;
            }
            return false;
        }

        string UniqueSocketGroupName(bool independent)
        {
            _profile.EnsureSocketInventories();
            string stem = independent ? "Independent Group" : "Frame Group";
            for (int n = 1; n < 99; n++)
            {
                string candidate = n == 1 ? stem : $"{stem} {n}";
                bool used = false;
                for (int i = 0; i < _profile.SocketInventories.Count; i++)
                {
                    if (string.Equals(_profile.SocketInventories[i].Name, candidate,
                            StringComparison.OrdinalIgnoreCase))
                        used = true;
                }
                if (!used)
                    return candidate;
            }
            return stem;
        }

        void GroupSelectedSocketInventory()
        {
            if (_selectedSockets.Count < 2)
            {
                _status = "Select 2+ sockets to group";
                return;
            }
            bool independent = SelectedSocketsUseIndependentGroup(
                _selectedSockets, _selectedSocketName);
            RecordProfileUndo(independent
                ? "Group Independent Sockets"
                : "Group Frame Sockets");
            _profile.EnsureSocketInventories();
            var names = new List<string>(_selectedSockets);
            byte space = 0;
            var firstTrack = _profile.FindSocketMotion(names[0]);
            if (firstTrack != null)
                space = firstTrack.AnchorSpace;
            for (int i = 0; i < names.Count; i++)
                _profile.RemoveSocketFromInventories(names[i]);
            var inventory = new SpriteSocketInventory
            {
                Name = UniqueSocketGroupName(independent),
                Folded = false,
                AnchorSpace = space,
                SocketNames = new List<string>(names),
            };
            _profile.SocketInventories.Add(inventory);
            _profile.SetInventorySpace(inventory, space);
            _profile.EnsureSocketInventories();
            SaveDirty();
            SealUndoGroup();
            _status = independent
                ? $"{inventory.Name}  •  {inventory.SocketNames.Count} Independent sockets"
                : $"{inventory.Name}  •  {inventory.SocketNames.Count} Frame sockets";
            Repaint();
        }

        void UngroupSelectedSocketInventory()
        {
            if (_selectedSockets.Count == 0)
            {
                _status = "Select a socket group to ungroup";
                return;
            }
            bool independent = SelectedSocketsUseIndependentGroup(
                _selectedSockets, _selectedSocketName);
            RecordProfileUndo(independent
                ? "Ungroup Independent Sockets"
                : "Ungroup Frame Sockets");
            var names = new List<string>(_selectedSockets);
            int removed = 0;
            for (int i = 0; i < names.Count; i++)
            {
                if (_profile.FindSocketInventory(names[i]) != null)
                {
                    _profile.RemoveSocketFromInventories(names[i]);
                    removed++;
                }
            }
            _profile.EnsureSocketInventories();
            SaveDirty();
            SealUndoGroup();
            _status = removed == 0 ? "Nothing to ungroup" : $"Ungrouped {removed} sockets";
            Repaint();
        }

        void HandleSocketListRowContext(Rect rowRect, SpriteClipDef clip, List<string> names, int index)
        {
            var evt = Event.current;
            if (evt.type != EventType.ContextClick &&
                !(evt.type == EventType.MouseDown && evt.button == 1))
                return;
            if (!rowRect.Contains(evt.mousePosition))
                return;
            string name = names[index];
            if (IsSocketLocked(name))
            {
                var lockedSelected = new List<string> { SpriteSocketKeys.CanonicalName(name) };
                var lockedMenu = new GenericMenu();
                PopulateSocketContextMenu(lockedMenu, clip, name, lockedSelected, 1);
                lockedMenu.ShowAsContext();
                evt.Use();
                return;
            }
            if (!IsSocketSelected(name))
                SelectSocketsFromListRow(names, index, SelectionOp.Replace);
            var selected = new List<string>(_selectedSockets);
            int count = selected.Count;
            var menu = new GenericMenu();
            PopulateSocketContextMenu(menu, clip, name, selected, count);
            menu.ShowAsContext();
            evt.Use();
        }

        void ClearPreviewObjectSelection()
        {
            ClearColliderSelection();
            ClearSocketSelection();
        }

        bool IsSocketSelected(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (_selectedSockets.Contains(name))
                return true;
            return _selectedSockets.Contains(SpriteSocketKeys.CanonicalName(name));
        }

        bool SocketSelectionBusy
            => _socketListMarqueeActive || _draggingColliderMarquee || _colliderMarqueePending;

        List<string> CachedUniqueSocketNames(SpriteClipDef clip)
        {
            var sockets = clip?.Sockets;
            if (_cachedSocketNamesGui == _guiPass &&
                ReferenceEquals(_cachedSocketNamesSource, sockets) &&
                _cachedSocketNamesCount == (sockets?.Count ?? 0))
                return _cachedSocketNames;

            _cachedSocketNamesGui = _guiPass;
            _cachedSocketNamesSource = sockets;
            _cachedSocketNamesCount = sockets?.Count ?? 0;
            if (sockets != null)
                SpriteSocketKeys.FillUniqueNamesInOrder(sockets, _cachedSocketNames);
            else
                _cachedSocketNames.Clear();
            AppendIndependentSocketNames(_cachedSocketNames);
            return _cachedSocketNames;
        }

        List<string> VisibleSocketNames(SpriteClipDef clip, bool independent)
        {
            var all = CachedUniqueSocketNames(clip);
            _visibleSocketNames.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                string name = all[i];
                var item = _profile?.SocketCatalog?.Find(name);
                bool isIndependent = item != null && item.UsesOwnClock;
                if (!isIndependent && _profile?.FindSocketMotion(name) != null)
                    isIndependent = true;
                if (isIndependent == independent)
                    _visibleSocketNames.Add(name);
            }
            return _visibleSocketNames;
        }

        static bool ListContainsSocketName(IList<string> names, string target)
        {
            if (names == null)
                return false;
            for (int i = 0; i < names.Count; i++)
            {
                if (SpriteSocketKeys.NamesEqual(names[i], target))
                    return true;
            }
            return false;
        }

        int CountSelectedSocketNames(IList<string> names)
        {
            int count = 0;
            for (int i = 0; i < names.Count; i++)
            {
                if (IsSocketSelected(names[i]))
                    count++;
            }
            return count;
        }

        bool SocketIdUsedByOther(string socketId, SpriteSocketCatalogItem except)
        {
            _profile.EnsureSocketCatalog();
            for (int i = 0; i < _profile.SocketCatalog.Items.Count; i++)
            {
                var item = _profile.SocketCatalog.Items[i];
                if (item == null || ReferenceEquals(item, except))
                    continue;
                if (string.Equals(item.SocketId, socketId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        void SelectAllVisibleSockets(IList<string> names, bool independent)
        {
            ClearColliderSelection();
            _selectedSockets.Clear();
            for (int i = 0; i < names.Count; i++)
            {
                string n = SpriteSocketKeys.CanonicalName(names[i]);
                if (IsSocketLocked(n))
                    continue;
                _selectedSockets.Add(n);
            }
            _selectedSocketName = null;
            foreach (string selected in _selectedSockets)
            {
                _selectedSocketName = selected;
                break;
            }
            _socketListAnchor = names.Count > 0 ? 0 : -1;
            _socketListAnchorIndependent = independent;
            _status = $"Selected {names.Count} socket{Plural(names.Count)}";
            Repaint();
        }

        void AppendIndependentSocketNames(List<string> names)
        {
            _profile?.EnsureSocketMotions();
            if (_profile?.SocketMotions == null)
                return;
            for (int i = 0; i < _profile.SocketMotions.Count; i++)
            {
                string name = SpriteSocketKeys.CanonicalName(
                    _profile.SocketMotions[i]?.SocketName);
                if (string.IsNullOrEmpty(name))
                    continue;
                bool exists = false;
                for (int n = 0; n < names.Count; n++)
                {
                    if (string.Equals(names[n], name, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    names.Add(name);
            }
        }

        void SyncSocketPrimaryFromSelection()
        {
            if (_selectedSockets.Count == 0)
            {
                _selectedSocketName = null;
                return;
            }

            if (string.IsNullOrEmpty(_selectedSocketName) ||
                !_selectedSockets.Contains(SpriteSocketKeys.CanonicalName(_selectedSocketName)))
            {
                string first = null;
                foreach (string name in _selectedSockets)
                {
                    first = name;
                    break;
                }
                _selectedSocketName = first;
            }
        }

        void SelectPreviewSocket(string name, SelectionOp op)
        {
            name = SpriteSocketKeys.CanonicalName(name);
            if (IsSocketLocked(name) &&
                op is SelectionOp.Add or SelectionOp.Replace or SelectionOp.Toggle)
            {
                // Locked sockets stay unselectable from the preview; unlock via badge/menu/list.
                if (op == SelectionOp.Replace)
                {
                    ClearColliderSelection();
                    _selectedSockets.Clear();
                    _selectedSocketName = null;
                }
                _status = $"Socket {name} is locked — unlock to select";
                Repaint();
                return;
            }
            _selectedSocketMotionTrack = -1;
            _selectedSocketMotionKey = -1;
            _selectedSocketMotionKeys.Clear();
            _selectedSocketTriggerTrack = -1;
            _selectedSocketTriggerIndex = -1;
            switch (op)
            {
                case SelectionOp.Add:
                    _selectedSockets.Add(name);
                    break;
                case SelectionOp.Toggle:
                    if (_selectedSockets.Contains(name))
                        _selectedSockets.Remove(name);
                    else
                        _selectedSockets.Add(name);
                    break;
                case SelectionOp.Subtract:
                    _selectedSockets.Remove(name);
                    break;
                case SelectionOp.Intersect:
                    bool keep = _selectedSockets.Contains(name);
                    ClearColliderSelection();
                    _selectedSockets.Clear();
                    if (keep)
                        _selectedSockets.Add(name);
                    break;
                default:
                    ClearColliderSelection();
                    _selectedSockets.Clear();
                    _selectedSockets.Add(name);
                    break;
            }
            _selectedSocketName = _selectedSockets.Contains(name) ? name : null;
            SyncSocketPrimaryFromSelection();
            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
            _selectedOnionFrame = -1;
            _selectedSocketDrawFrame = -1;
            _selectedSocketDrawName = null;
            _pivotSelected = false;
            _status = PreviewSelectionStatus();
        }

        void PruneSocketSelection(SpriteClipDef clip)
        {
            _profile?.EnsureSocketMotions();
            if (clip?.Sockets == null && (_profile?.SocketMotions == null ||
                                         _profile.SocketMotions.Count == 0))
            {
                ClearSocketSelection();
                return;
            }

            _selectedSockets.RemoveWhere(name =>
                !SocketExistsInClipOrMotion(clip, name) || IsSocketLocked(name));
            if (!string.IsNullOrEmpty(_selectedSocketName) &&
                (!SocketExistsInClipOrMotion(clip, _selectedSocketName) ||
                 IsSocketLocked(_selectedSocketName)))
                _selectedSocketName = null;
            SyncSocketPrimaryFromSelection();
            if (_selectedSockets.Count == 0)
            {
                _draggingSocket = false;
                _socketHandleKind = ColliderHandleKind.None;
            }
        }

        bool SocketExistsInClipOrMotion(SpriteClipDef clip, string name)
        {
            if (clip?.Sockets != null && SpriteSocketKeys.NameExists(clip.Sockets, name))
                return true;
            return _profile?.FindSocketMotion(name) != null;
        }

        void DrawSocketPlacementBalloon(Rect canvas)
        {
            const string text = "Click on the frame to place a socket.";
            float width = Mathf.Min(canvas.width - 24f, 280f);
            var balloon = new Rect(canvas.center.x - width * 0.5f, canvas.y + 8f, width, 28f);
            EditorGUI.DrawRect(balloon, new Color(0.07f, 0.1f, 0.16f, 0.94f));
            DrawBorder(balloon, AccentColor, 1f);
            GUI.Label(balloon, text, _socketBalloonStyle);
        }

        void DrawSockets(Rect cell, SpriteClipDef clip, int frame)
        {
            if (!_showPreviewDebug)
                return;
            clip.Sockets ??= new List<FrameSocketDef>();
            var names = CachedUniqueSocketNames(clip);
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                bool selected = IsSocketSelected(name);
                if (!TryGetPreviewSocketPose(clip, name, frame,
                        out var position, out var angle, out _, out bool onFrame))
                    continue;
                DrawSocketGizmo(cell, position, angle, $"{i}:{name}",
                    SpriteSocketKeys.ColorForIndex(i), selected, !onFrame);
                bool locked = IsSocketLocked(name);
                if (locked || selected)
                    DrawSocketLockBadge(SocketToScreen(position, cell), locked);
            }

            if (_selectedSockets.Count >= 2)
            {
                if (TryGetSocketGroupTransformLayout(clip, frame, cell, out var groupLayout))
                    DrawSocketTransformGizmo(groupLayout, true, boxMoves: false);
                DrawSocketGroupPivot(clip, frame, cell);
                return;
            }

            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (!IsSocketSelected(name))
                    continue;
                if (!TryGetSocketTransformLayout(clip, name, frame, cell, out var layout))
                    continue;
                bool primary = SpriteSocketKeys.NamesEqual(name, _selectedSocketName);
                DrawSocketTransformGizmo(layout, primary);
            }
        }

        bool TryGetSocketGroupCentroid(SpriteClipDef clip, int frame, out Vector2 source)
        {
            source = default;
            if (clip == null || _selectedSockets.Count < 2)
                return false;
            if (_draggingSocket && _socketMoveWholePath &&
                _socketHandleKind == ColliderHandleKind.Body)
            {
                source = _socketGroupCentroidCurrent;
                return true;
            }
            if (TryGetSocketGroupBoundsCenter(clip, frame, out source))
                return true;

            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (string name in _selectedSockets)
            {
                if (!TryGetPreviewSocketPose(clip, name, frame, out var position, out _, out _, out _))
                    continue;
                sum += position;
                count++;
            }
            if (count < 2)
                return false;
            source = sum / count;
            return true;
        }

        bool TryGetSocketGroupBoundsCenter(SpriteClipDef clip, int frame, out Vector2 source)
        {
            source = default;
            float xMin = float.MaxValue;
            float yMin = float.MaxValue;
            float xMax = float.MinValue;
            float yMax = float.MinValue;
            int count = 0;
            foreach (string name in _selectedSockets)
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                EncapsulateSocketGroupPath(clip, name, frame, ref xMin, ref yMin, ref xMax, ref yMax,
                    ref count);
            }
            if (count < 2)
                return false;
            source = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
            return true;
        }

        void EncapsulateSocketGroupPath(SpriteClipDef clip, string name, int frame,
            ref float xMin, ref float yMin, ref float xMax, ref float yMax, ref int count)
        {
            var track = _profile?.FindSocketMotion(name);
            if (track?.Keys != null && track.Keys.Count > 0 &&
                SpriteSocketKeys.UsesOwnClock(_profile.SocketCatalog, name))
            {
                for (int i = 0; i < track.Keys.Count; i++)
                {
                    var key = track.Keys[i];
                    if (key == null)
                        continue;
                    EncapsulateSocketGroupPoint(
                        MotionKeyToClipPixels(clip, track, key.LocalPosition),
                        ref xMin, ref yMin, ref xMax, ref yMax, ref count);
                }
                return;
            }

            if (clip?.Sockets != null)
            {
                bool found = false;
                for (int i = 0; i < clip.Sockets.Count; i++)
                {
                    var key = clip.Sockets[i];
                    if (key == null || !SpriteSocketKeys.NamesEqual(key.Name, name))
                        continue;
                    EncapsulateSocketGroupPoint(key.LocalPosition,
                        ref xMin, ref yMin, ref xMax, ref yMax, ref count);
                    found = true;
                }
                if (found)
                    return;
            }

            if (TryGetPreviewSocketPose(clip, name, frame, out var position, out _, out _, out _))
                EncapsulateSocketGroupPoint(position, ref xMin, ref yMin, ref xMax, ref yMax, ref count);
        }

        static void EncapsulateSocketGroupPoint(Vector2 point,
            ref float xMin, ref float yMin, ref float xMax, ref float yMax, ref int count)
        {
            xMin = Mathf.Min(xMin, point.x);
            yMin = Mathf.Min(yMin, point.y);
            xMax = Mathf.Max(xMax, point.x);
            yMax = Mathf.Max(yMax, point.y);
            count++;
        }

        Vector2 MotionKeyToClipPixels(SpriteClipDef clip, SpriteSocketMotionTrack track, Vector2 localPosition)
        {
            if (clip == null || track == null || _profile == null)
                return localPosition;
            float referencePpu = SpriteSheetProfile.GetPixelsPerUnit(
                _profile.SheetAt(track.ReferenceSheetIndex));
            float targetPpu = SpriteSheetProfile.GetPixelsPerUnit(
                _profile.SheetAt(clip.SheetIndex));
            return localPosition * (targetPpu / Mathf.Max(1f, referencePpu));
        }

        Vector2 ClipPixelsToMotionKey(SpriteClipDef clip, SpriteSocketMotionTrack track, Vector2 clipPixels)
        {
            if (clip == null || track == null || _profile == null)
                return clipPixels;
            float referencePpu = SpriteSheetProfile.GetPixelsPerUnit(
                _profile.SheetAt(track.ReferenceSheetIndex));
            float targetPpu = SpriteSheetProfile.GetPixelsPerUnit(
                _profile.SheetAt(clip.SheetIndex));
            return clipPixels * (referencePpu / Mathf.Max(1f, targetPpu));
        }

        bool TryGetSocketGroupPivot(SpriteClipDef clip, int frame, Rect cell, out Vector2 screen)
        {
            screen = default;
            if (!TryGetSocketGroupCentroid(clip, frame, out var source))
                return false;
            screen = SocketToScreen(source, cell);
            return true;
        }

        bool TryGetSocketGroupTransformLayout(SpriteClipDef clip, int frame, Rect cell,
            out SocketTransformLayout layout)
        {
            layout = default;
            if (!TryGetSocketGroupCentroid(clip, frame, out var source))
                return false;

            Vector2 pivot = SocketToScreen(source, cell);
            float radius = SocketGroupGizmoMinHalf;
            foreach (string name in _selectedSockets)
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                var track = _profile?.FindSocketMotion(name);
                if (track?.Keys != null && track.Keys.Count > 0 &&
                    SpriteSocketKeys.UsesOwnClock(_profile.SocketCatalog, name))
                {
                    for (int i = 0; i < track.Keys.Count; i++)
                    {
                        var key = track.Keys[i];
                        if (key == null)
                            continue;
                        Vector2 pin = SocketToScreen(
                            MotionKeyToClipPixels(clip, track, key.LocalPosition), cell);
                        radius = Mathf.Max(radius, (pin - pivot).magnitude);
                    }
                    continue;
                }

                if (clip?.Sockets != null)
                {
                    bool found = false;
                    for (int i = 0; i < clip.Sockets.Count; i++)
                    {
                        var key = clip.Sockets[i];
                        if (key == null || !SpriteSocketKeys.NamesEqual(key.Name, name))
                            continue;
                        Vector2 pin = SocketToScreen(key.LocalPosition, cell);
                        radius = Mathf.Max(radius, (pin - pivot).magnitude);
                        found = true;
                    }
                    if (found)
                        continue;
                }

                if (TryGetPreviewSocketPose(clip, name, frame, out var position, out _, out _, out _))
                {
                    Vector2 pin = SocketToScreen(position, cell);
                    radius = Mathf.Max(radius, (pin - pivot).magnitude);
                }
            }

            radius += SocketGroupGizmoPad;
            var box = new Rect(pivot.x - radius, pivot.y - radius, radius * 2f, radius * 2f);
            layout = new SocketTransformLayout(box.center, box, 0f, Vector2.one, source);
            return true;
        }

        bool SocketGroupPivotContains(SpriteClipDef clip, int frame, Rect cell, Vector2 mouse)
        {
            if (!TryGetSocketGroupPivot(clip, frame, cell, out var screen))
                return false;
            return (mouse - screen).sqrMagnitude <= SocketGroupPivotHit * SocketGroupPivotHit;
        }

        void DrawSocketGroupPivot(SpriteClipDef clip, int frame, Rect cell)
        {
            if (!TryGetSocketGroupPivot(clip, frame, cell, out var point))
                return;
            float radius = 7f;
            Handles.BeginGUI();
            Handles.color = new Color(0.06f, 0.18f, 0.28f, 1f);
            Handles.DrawSolidDisc(point, Vector3.forward, radius + 1.4f);
            Handles.color = AccentColor;
            Handles.DrawSolidDisc(point, Vector3.forward, radius);
            Handles.color = Color.white;
            Handles.DrawAAPolyLine(1.6f,
                point + new Vector2(-10f, 0f), point + new Vector2(10f, 0f));
            Handles.DrawAAPolyLine(1.6f,
                point + new Vector2(0f, -10f), point + new Vector2(0f, 10f));
            Handles.EndGUI();
            EditorGUIUtility.AddCursorRect(
                new Rect(point.x - SocketGroupPivotHit, point.y - SocketGroupPivotHit,
                    SocketGroupPivotHit * 2f, SocketGroupPivotHit * 2f),
                MouseCursor.MoveArrow);
        }

        void SyncOrbitCenterFromSelection(SpriteClipDef clip)
        {
            if (clip?.Sockets == null || _selectedSockets.Count == 0)
                return;
            Vector2 sum = Vector2.zero;
            int count = 0;
            for (int i = 0; i < clip.Sockets.Count; i++)
            {
                var key = clip.Sockets[i];
                if (key == null || !IsSocketSelected(key.Name))
                    continue;
                sum += key.LocalPosition;
                count++;
            }
            if (count == 0)
                return;
            _socketOrbitCenter = new Vector2(Mathf.Round(sum.x / count), Mathf.Round(sum.y / count));
            _socketOrbitCenterSet = true;
        }

        bool TryGetPreviewSocketPose(SpriteClipDef clip, string name, int frame,
            out Vector2 position, out float angle, out Vector2 scale, out bool onFrame)
        {
            position = Vector2.zero;
            angle = 0f;
            scale = Vector2.one;
            onFrame = false;
            if (clip == null || string.IsNullOrEmpty(name))
                return false;
            var item = _profile?.SocketCatalog?.Find(name);
            if (_draggingSocket && IsSocketSelected(name))
            {
                // Independent sockets are drawn from motion sampling. Do not switch to
                // frame-key poses during drag (that caused a visible jump / offset).
                if (item != null && item.UsesOwnClock)
                {
                    if (_socketMoveMotionKeys.Count == 0)
                    {
                        for (int i = 0; i < _socketMoveNames.Count &&
                                            i < _socketMoveKeys.Count; i++)
                        {
                            if (!SpriteSocketKeys.NamesEqual(_socketMoveNames[i], name) ||
                                _socketMoveKeys[i] == null)
                                continue;
                            position = _socketMoveKeys[i].LocalPosition;
                            angle = _socketMoveKeys[i].LocalAngle;
                            scale = SpriteSocketKeys.ResolvedScale(
                                _socketMoveKeys[i].LocalScale);
                            return true;
                        }
                    }
                    // Motion keys are live-updated by ApplySocketMotionKeyBodyMove;
                    // fall through to TrySampleIndependentSocketMotion.
                }
                else if (clip.Sockets != null &&
                         SpriteSocketKeys.TryGetPose(clip.Sockets, name, frame,
                             out position, out angle, out scale, out onFrame))
                {
                    return true;
                }
            }
            if (item != null && item.UsesOwnClock &&
                TrySampleIndependentSocketMotion(clip, name, item,
                    out position, out angle, out scale))
                return true;
            if (clip.Sockets == null)
                return false;
            float sampleTime = SpriteSocketKeys.ResolveSampleTime(clip, item, _previewTime, _previewLoop);
            bool ok = SpriteSocketKeys.TrySampleAtTime(clip.Sockets, name, clip, sampleTime,
                SocketSampleClosed(clip, name), item != null && item.UsesOwnClock,
                out position, out angle, out scale, out _);
            onFrame = SpriteSocketKeys.FindOnFrame(clip.Sockets, name, frame) != null;
            return ok;
        }

        bool TrySampleIndependentSocketMotion(SpriteClipDef clip, string name,
            SpriteSocketCatalogItem item, out Vector2 position, out float angle,
            out Vector2 scale)
        {
            position = Vector2.zero;
            angle = 0f;
            scale = Vector2.one;
            var track = _profile?.FindSocketMotion(name);
            if (track?.Keys == null || track.Keys.Count == 0)
                return false;

            float duration = Mathf.Max(0.01f, track.Duration);
            float t = _socketPreviewTime / duration;
            t = track.Loop ? Mathf.Repeat(t, 1f) : Mathf.Clamp01(t);

            SpriteSocketMotionKey a = track.Keys[0];
            SpriteSocketMotionKey b = a;
            int fromIndex = 0;
            int toIndex = 0;
            float blend = 0f;
            if (track.Keys.Count > 1)
            {
                int last = track.Keys.Count - 1;
                if (t < track.Keys[0].NormalizedTime && track.Loop)
                {
                    fromIndex = last;
                    toIndex = 0;
                    a = track.Keys[last];
                    b = track.Keys[0];
                    float span = 1f - a.NormalizedTime + b.NormalizedTime;
                    blend = span > 0.0001f ? (t + 1f - a.NormalizedTime) / span : 0f;
                }
                else if (t >= track.Keys[last].NormalizedTime)
                {
                    if (track.Loop && t < 1f)
                    {
                        fromIndex = last;
                        toIndex = 0;
                        a = track.Keys[last];
                        b = track.Keys[0];
                        float span = 1f - a.NormalizedTime + b.NormalizedTime;
                        blend = span > 0.0001f ? (t - a.NormalizedTime) / span : 0f;
                    }
                    else
                    {
                        fromIndex = toIndex = last;
                        a = b = track.Keys[last];
                    }
                }
                else
                {
                    for (int k = 0; k < last; k++)
                    {
                        if (t < track.Keys[k + 1].NormalizedTime)
                        {
                            fromIndex = k;
                            toIndex = k + 1;
                            a = track.Keys[k];
                            b = track.Keys[k + 1];
                            float span = b.NormalizedTime - a.NormalizedTime;
                            blend = span > 0.0001f
                                ? Mathf.Clamp01((t - a.NormalizedTime) / span)
                                : 0f;
                            break;
                        }
                    }
                }
            }

            float referencePpu = SpriteSheetProfile.GetPixelsPerUnit(
                _profile.SheetAt(track.ReferenceSheetIndex));
            float targetPpu = SpriteSheetProfile.GetPixelsPerUnit(
                _profile.SheetAt(clip.SheetIndex));
            Vector2 sampledPosition;
            blend = a.UseCustomEase
                ? a.EvaluateCustomEase(blend)
                : SpriteEase.Evaluate(
                    SpriteEase.IsValidMode(a.EaseMode)
                        ? (SpriteEaseMode)a.EaseMode
                        : SpriteEaseMode.SmoothStep,
                    blend, a.AllowOvershoot);
            Vector2 pathDerivative = b.LocalPosition - a.LocalPosition;
            if (fromIndex == toIndex)
            {
                sampledPosition = a.LocalPosition;
            }
            else
            {
                int count = track.Keys.Count;
                int before = track.Loop
                    ? (fromIndex - 1 + count) % count
                    : Mathf.Max(0, fromIndex - 1);
                int after = track.Loop
                    ? (toIndex + 1) % count
                    : Mathf.Min(count - 1, toIndex + 1);
                sampledPosition = EvaluateEditorMotionPosition(
                    a,
                    track.Keys[before].LocalPosition,
                    a.LocalPosition,
                    b.LocalPosition,
                    track.Keys[after].LocalPosition,
                    b.InTangent,
                    blend);
                pathDerivative = EvaluateEditorMotionDerivative(
                    a,
                    track.Keys[before].LocalPosition,
                    a.LocalPosition,
                    b.LocalPosition,
                    track.Keys[after].LocalPosition,
                    b.InTangent,
                    blend);
            }
            position = sampledPosition * (targetPpu / Mathf.Max(1f, referencePpu));
            angle = SpriteSocketMotionInterpolation.Rotation(
                a.RotationMode, a.LocalAngle, b.LocalAngle, a.RotationTurns,
                a.FacingAngleOffset,
                new Unity.Mathematics.float2(pathDerivative.x, pathDerivative.y),
                blend);
            scale = Vector2.LerpUnclamped(
                SpriteSocketKeys.ResolvedScale(a.LocalScale),
                SpriteSocketKeys.ResolvedScale(b.LocalScale), blend);
            return true;
        }

        static Vector2 EvaluateEditorMotionPosition(
            SpriteSocketMotionKey key, Vector2 p0, Vector2 p1,
            Vector2 p2, Vector2 p3, Vector2 nextInTangent, float t)
        {
            var value = SpriteSocketMotionInterpolation.Position(
                key.PathMode,
                new Unity.Mathematics.float2(p0.x, p0.y),
                new Unity.Mathematics.float2(p1.x, p1.y),
                new Unity.Mathematics.float2(p2.x, p2.y),
                new Unity.Mathematics.float2(p3.x, p3.y),
                new Unity.Mathematics.float2(key.OutTangent.x, key.OutTangent.y),
                new Unity.Mathematics.float2(nextInTangent.x, nextInTangent.y),
                key.ArcBulge, key.ArcClockwise ? (byte)1 : (byte)0, t);
            return new Vector2(value.x, value.y);
        }

        static Vector2 EvaluateEditorMotionDerivative(
            SpriteSocketMotionKey key, Vector2 p0, Vector2 p1,
            Vector2 p2, Vector2 p3, Vector2 nextInTangent, float t)
        {
            var value = SpriteSocketMotionInterpolation.Derivative(
                key.PathMode,
                new Unity.Mathematics.float2(p0.x, p0.y),
                new Unity.Mathematics.float2(p1.x, p1.y),
                new Unity.Mathematics.float2(p2.x, p2.y),
                new Unity.Mathematics.float2(p3.x, p3.y),
                new Unity.Mathematics.float2(key.OutTangent.x, key.OutTangent.y),
                new Unity.Mathematics.float2(nextInTangent.x, nextInTangent.y),
                key.ArcBulge, key.ArcClockwise ? (byte)1 : (byte)0, t);
            return new Vector2(value.x, value.y);
        }

        static Vector2 CatmullMotionPosition(
            Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            t = Mathf.Clamp01(t);
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1) +
                           (-p0 + p2) * t +
                           (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                           (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        float SocketPreviewSampleTime(SpriteClipDef clip, string name)
        {
            var item = _profile?.SocketCatalog?.Find(name);
            return SpriteSocketKeys.ResolveSampleTime(clip, item, _previewTime, _previewLoop);
        }

        bool SocketSampleClosed(SpriteClipDef clip, string name)
        {
            if (!SpriteSocketKeys.UsesClosedPath(_profile?.SocketCatalog, name))
                return false;
            if (SpriteSocketKeys.UsesOwnClock(_profile?.SocketCatalog, name))
                return true;
            if (clip == null || clip.WrapMode == SpriteAnimWrap.PingPong)
                return false;
            return _previewLoop
                || (clip.WrapMode != SpriteAnimWrap.Once
                    && clip.WrapMode != SpriteAnimWrap.ReverseOnce);
        }

        void DrawEllipticalOrbitTools(SpriteClipDef clip)
        {
            GUILayout.Space(6f);
            GUILayout.Label("ORBIT PATTERN", _sectionStyle);
            _socketOrbitShape = EditorGUILayout.Popup(
                new GUIContent("Shape", "Circle, or a flattened ellipse around the chest."),
                Mathf.Clamp(_socketOrbitShape, 0, SocketOrbitShapeLabels.Length - 1),
                SocketOrbitShapeLabels);
            float orbitRadius = _socketOrbitRadius > 1f ? _socketOrbitRadius : DefaultSocketOrbitRadius();
            float nextOrbitRadius = EditorGUILayout.FloatField(
                new GUIContent("Radius (px)", "How far the ring sits from the center."),
                orbitRadius);
            if (!Mathf.Approximately(nextOrbitRadius, orbitRadius))
                _socketOrbitRadius = Mathf.Max(4f, nextOrbitRadius);
            Vector2 orbitCenter = _socketOrbitCenterSet
                ? _socketOrbitCenter
                : DefaultSocketOrbitCenter();
            Vector2 nextOrbitCenter = EditorGUILayout.Vector2Field(
                new GUIContent("Center (px)", "Nucleus of the rings. Default is the chest."),
                orbitCenter);
            if (nextOrbitCenter != orbitCenter)
            {
                _socketOrbitCenter = nextOrbitCenter;
                _socketOrbitCenterSet = true;
            }
            using (new EditorGUI.DisabledScope(_selectedSockets.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Apply to Selected",
                        "Move and/or scale the selected sockets to this Radius and Center. Does not create new sockets.")))
                    ApplyOrbitSettingsToSelected(clip);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _socketOrbitPattern = EditorGUILayout.Popup(
                    new GUIContent("Pattern", SocketOrbitPatternTooltip(_socketOrbitPattern)),
                    Mathf.Clamp(_socketOrbitPattern, 0, SocketOrbitPatternLabels.Length - 1),
                    SocketOrbitPatternLabels);
                _socketOrbitCount = Mathf.Clamp(
                    EditorGUILayout.IntField(_socketOrbitCount, GUILayout.Width(40f)),
                    1, 12);
                if (GUILayout.Button(new GUIContent("Create", SocketOrbitPatternTooltip(_socketOrbitPattern)),
                        GUILayout.Width(64f)))
                {
                    ApplySocketOrbitPattern(clip, _socketOrbitPattern, restamp: false, _socketOrbitCount);
                    GUIUtility.ExitGUI();
                }
            }
            using (new EditorGUI.DisabledScope(_selectedSockets.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Restamp Selected",
                        "Rebuild the selected sockets onto the Pattern above. Does not add sockets.")))
                {
                    ApplySocketOrbitPattern(clip, _socketOrbitPattern, restamp: true, _selectedSockets.Count);
                    GUIUtility.ExitGUI();
                }
            }
        }

        void AddSocketPatternMenuItems(GenericMenu menu, SpriteClipDef clip)
        {
            int n = _selectedSockets.Count;
            if (n == 0)
            {
                menu.AddDisabledItem(new GUIContent("Pattern/Select sockets first"));
                return;
            }

            for (int i = 0; i < SocketOrbitPatternLabels.Length; i++)
            {
                int pattern = i;
                menu.AddItem(new GUIContent($"Pattern/{SocketOrbitPatternLabels[i]}"),
                    false,
                    () => ApplySocketOrbitPattern(clip, pattern, restamp: true, createCount: n));
            }
        }

        static string SocketOrbitPatternTooltip(int pattern)
        {
            int i = Mathf.Clamp(pattern, 0, SocketOrbitPatternTips.Length - 1);
            return SocketOrbitPatternTips[i];
        }

        static bool DrawOrbitCreateRow(string label, string tooltip, ref int count, int min, int max)
        {
            bool clicked;
            using (new EditorGUILayout.HorizontalScope())
            {
                clicked = GUILayout.Button(new GUIContent(label, tooltip));
                count = Mathf.Clamp(
                    EditorGUILayout.IntField(count, GUILayout.Width(48f)),
                    min, max);
            }
            return clicked;
        }

        void ApplyOrbitSettingsToSelected(SpriteClipDef clip)
        {
            PruneSocketSelection(clip);
            if (clip?.Sockets == null || _selectedSockets.Count == 0)
            {
                _status = "Select sockets to apply radius and center";
                return;
            }

            Vector2 targetCenter = _socketOrbitCenterSet
                ? _socketOrbitCenter
                : DefaultSocketOrbitCenter();
            bool scaleOrbit = _socketOrbitRadius > 1f;
            float targetRadius = scaleOrbit ? _socketOrbitRadius : 0f;

            Vector2 centroid = Vector2.zero;
            int count = 0;
            float maxDist = 0f;
            for (int i = 0; i < clip.Sockets.Count; i++)
            {
                var key = clip.Sockets[i];
                if (key == null || !IsSocketSelected(key.Name))
                    continue;
                centroid += key.LocalPosition;
                count++;
            }
            if (count == 0)
            {
                _status = "Selected sockets have no keys to apply";
                return;
            }
            centroid /= count;
            for (int i = 0; i < clip.Sockets.Count; i++)
            {
                var key = clip.Sockets[i];
                if (key == null || !IsSocketSelected(key.Name))
                    continue;
                maxDist = Mathf.Max(maxDist, (key.LocalPosition - centroid).magnitude);
            }

            float scale = scaleOrbit && maxDist > 0.5f ? targetRadius / maxDist : 1f;
            RecordProfileUndo("Apply Orbit Settings");
            for (int i = 0; i < clip.Sockets.Count; i++)
            {
                var key = clip.Sockets[i];
                if (key == null || !IsSocketSelected(key.Name))
                    continue;
                Vector2 pos = targetCenter + (key.LocalPosition - centroid) * scale;
                key.LocalPosition = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));
                Vector2 local = pos - targetCenter;
                key.DrawLayer = local.y > 0.5f
                    ? SpriteSocketKeys.DrawBehind
                    : SpriteSocketKeys.DrawFront;
            }

            _socketOrbitCenter = targetCenter;
            _socketOrbitCenterSet = true;
            CaptureSocketMotionsFromClip(clip, OrderedSelectedSocketNames(clip));
            _status = scaleOrbit
                ? $"Applied orbit  r={targetRadius:0}px  center ({targetCenter.x:0}, {targetCenter.y:0})  to {_selectedSockets.Count} sockets"
                : $"Moved {_selectedSockets.Count} sockets to center ({targetCenter.x:0}, {targetCenter.y:0})";
            SaveDirty();
            GUIUtility.ExitGUI();
        }

        void ApplySocketOrbitPattern(SpriteClipDef clip, int pattern, bool restamp, int createCount)
        {
            if (clip?.Frames == null || clip.Frames.Length < 4)
            {
                _status = "Orbit patterns need at least 4 frames in this clip";
                return;
            }

            pattern = Mathf.Clamp(pattern, 0, SocketOrbitPatternLabels.Length - 1);
            var names = new List<string>();
            if (restamp)
            {
                names.AddRange(OrderedSelectedSocketNames(clip));
                if (names.Count == 0)
                {
                    _status = "Select sockets to restamp";
                    return;
                }
            }
            else
            {
                int min = pattern == 1 ? 1 : 2;
                createCount = Mathf.Clamp(createCount, min, 12);
                clip.Sockets ??= new List<FrameSocketDef>();
                for (int i = 0; i < createCount; i++)
                    names.Add(UniquePatternSocketName(clip, names, pattern, i, createCount));
            }

            RecordProfileUndo(restamp
                ? $"Restamp {SocketOrbitPatternLabels[pattern]}"
                : $"Create {SocketOrbitPatternLabels[pattern]}");
            _profile.EnsureSocketCatalog();
            clip.Sockets ??= new List<FrameSocketDef>();
            float radius = _socketOrbitRadius > 1f ? _socketOrbitRadius : DefaultSocketOrbitRadius();
            Vector2 center = _socketOrbitCenterSet ? _socketOrbitCenter : DefaultSocketOrbitCenter();
            float tilt = SocketOrbitTiltDegrees(_socketOrbitTilt);
            StampSocketOrbitPattern(clip, pattern, names, radius, center, tilt);
            CaptureSocketMotionsFromClip(clip, names, replaceTiming: true);

            _selectedSockets.Clear();
            for (int i = 0; i < names.Count; i++)
                _selectedSockets.Add(names[i]);
            _selectedSocketName = names[0];
            _socketOrbitCenter = center;
            _socketOrbitCenterSet = true;
            _status = restamp
                ? $"{SocketOrbitPatternLabels[pattern]}  •  restamped {names.Count} sockets"
                : $"{SocketOrbitPatternLabels[pattern]}  •  created {names.Count} sockets";
            SaveDirty();
            Repaint();
        }

        void ApplySelectedSocketsToAllClips(SpriteClipDef sourceClip)
        {
            if (sourceClip?.Frames == null || sourceClip.Frames.Length == 0 ||
                _profile?.Clips == null || _profile.Clips.Count < 2)
                return;

            var names = OrderedSelectedSocketNames(sourceClip);
            if (names.Count == 0)
                return;

            int targetCount = 0;
            for (int i = 0; i < _profile.Clips.Count; i++)
            {
                var target = _profile.Clips[i];
                if (target != null && !ReferenceEquals(target, sourceClip) &&
                    target.Frames != null && target.Frames.Length > 0)
                    targetCount++;
            }
            if (targetCount == 0)
                return;

            string socketText = names.Count == 1
                ? $"socket \"{names[0]}\""
                : $"{names.Count} selected sockets";
            if (!EditorUtility.DisplayDialog(
                    "Apply Sockets to All Clips",
                    $"Copy {socketText} from \"{sourceClip.Name}\" to {targetCount} other clip{Plural(targetCount)}?\n\n" +
                    "Existing keys with the same socket names will be replaced. The motion is retimed to each clip and remains relative to its player pivot.",
                    "Apply All",
                    "Cancel"))
                return;

            RecordProfileUndo("Apply Sprite Sockets to All Clips");
            _profile.EnsureSheets(_selectedSheet);
            _profile.EnsureSocketCatalog();

            float sourceDuration = Mathf.Max(0.0001f, TotalAuthoredDuration(sourceClip));
            float sourcePpu = SpriteSheetProfile.GetPixelsPerUnit(_profile.SheetForClip(sourceClip));
            int changedClips = 0;

            for (int c = 0; c < _profile.Clips.Count; c++)
            {
                var target = _profile.Clips[c];
                if (target == null || ReferenceEquals(target, sourceClip) ||
                    target.Frames == null || target.Frames.Length == 0)
                    continue;

                target.EnsureFrameData();
                target.Sockets ??= new List<FrameSocketDef>();
                float targetDuration = Mathf.Max(0.0001f, TotalAuthoredDuration(target));
                float targetPpu = SpriteSheetProfile.GetPixelsPerUnit(_profile.SheetForClip(target));
                float pixelScale = targetPpu / sourcePpu;

                for (int n = 0; n < names.Count; n++)
                {
                    string name = names[n];
                    var item = _profile.SocketCatalog.Find(name);
                    bool closed = SpriteSocketKeys.UsesClosedPath(_profile.SocketCatalog, name);
                    bool curved = item != null && item.UsesOwnClock;
                    SpriteSocketKeys.DeleteIdentity(target.Sockets, name);

                    for (int frame = 0; frame < target.Frames.Length; frame++)
                    {
                        float phase = AuthoredStartTime(target, frame) / targetDuration;
                        float sourceTime = Mathf.Clamp01(phase) * sourceDuration;
                        if (!SpriteSocketKeys.TrySampleAtTime(
                                sourceClip.Sockets, name, sourceClip, sourceTime, closed, curved,
                                out var position, out var angle, out var scale, out _))
                            continue;

                        int sourceFrame = SpriteAnimPlayback.AuthoredFrameAtTime(
                            sourceClip, sourceTime, out _);
                        target.Sockets.Add(new FrameSocketDef
                        {
                            Name = name,
                            FrameIndex = frame,
                            LocalPosition = new Vector2(
                                Mathf.Round(position.x * pixelScale),
                                Mathf.Round(position.y * pixelScale)),
                            LocalAngle = angle,
                            LocalScale = scale,
                            DrawLayer = SpriteSocketKeys.ResolveDrawLayer(
                                sourceClip.Sockets, name, sourceFrame, closed),
                        });
                    }
                }
                changedClips++;
            }

            _status = $"Applied {names.Count} socket{Plural(names.Count)} to {changedClips} clip{Plural(changedClips)} using player pivots";
            SaveDirty();
            Repaint();
        }

        void CaptureSelectedSocketMotions(SpriteClipDef sourceClip)
        {
            var names = OrderedSelectedSocketNames(sourceClip);
            if (names.Count == 0)
                return;
            RecordProfileUndo("Capture Independent Socket Motion");
            _profile.EnsureSocketCatalog();
            for (int i = 0; i < names.Count; i++)
            {
                var item = _profile.SocketCatalog.Ensure(names[i]);
                item.MotionMode = (byte)SpriteSocketClockMode.OwnClock;
                if (item.Speed <= 0.0001f)
                    item.Speed = 1f;
            }
            CaptureSocketMotionsFromClip(sourceClip, names, replaceTiming: true);
            _timelineView = TimelineView.Sockets;
            _status = $"Captured {names.Count} independent socket track{Plural(names.Count)} from player pivot";
            SaveDirty();
            Repaint();
        }

        void CaptureSocketMotionsFromClip(SpriteClipDef sourceClip, IList<string> names,
            bool replaceTiming = false)
        {
            if (sourceClip?.Sockets == null || sourceClip.Frames == null ||
                sourceClip.Frames.Length == 0 || names == null)
                return;
            _profile.EnsureSocketMotions();
            float duration = Mathf.Max(0.01f, TotalAuthoredDuration(sourceClip));

            for (int n = 0; n < names.Count; n++)
            {
                string name = SpriteSocketKeys.CanonicalName(names[n]);
                var item = _profile.SocketCatalog.Find(name);
                if (item == null || !item.UsesOwnClock)
                    continue;

                var track = _profile.FindSocketMotion(name);
                bool created = track == null;
                track ??= _profile.EnsureSocketMotion(name);
                track.SocketName = name;
                track.Loop = _profile.IndependentMotionLoop;
                SpriteSocketKeys.CollectKeysSorted(sourceClip.Sockets, name, _socketPathKeys);
                bool rebuildTiming = created || replaceTiming ||
                                     track.Keys.Count != _socketPathKeys.Count;
                if (rebuildTiming)
                {
                    track.ReferenceSheetIndex = sourceClip.SheetIndex;
                    track.Duration = _profile.IndependentMotionDuration;
                    track.Keys.Clear();
                }

                float sourcePpu = SpriteSheetProfile.GetPixelsPerUnit(
                    _profile.SheetAt(sourceClip.SheetIndex));
                float referencePpu = SpriteSheetProfile.GetPixelsPerUnit(
                    _profile.SheetAt(track.ReferenceSheetIndex));
                for (int k = 0; k < _socketPathKeys.Count; k++)
                {
                    var source = _socketPathKeys[k];
                    Vector2 referencePosition = source.LocalPosition *
                                                (referencePpu / Mathf.Max(1f, sourcePpu));
                    if (rebuildTiming)
                    {
                        track.Keys.Add(new SpriteSocketMotionKey
                        {
                            NormalizedTime = Mathf.Clamp01(
                                AuthoredStartTime(sourceClip, source.FrameIndex) / duration),
                            LocalPosition = referencePosition,
                            LocalAngle = source.LocalAngle,
                            LocalScale = SpriteSocketKeys.ResolvedScale(source.LocalScale),
                            DrawLayer = source.DrawLayer,
                        });
                    }
                    else
                    {
                        var target = track.Keys[k];
                        target.LocalPosition = referencePosition;
                        target.LocalAngle = source.LocalAngle;
                        target.LocalScale = SpriteSocketKeys.ResolvedScale(source.LocalScale);
                        target.DrawLayer = source.DrawLayer;
                    }
                }
                track.Normalize(Mathf.Max(1, _profile.Sheets?.Count ?? 0));
            }
        }

        List<string> OrderedSelectedSocketNames(SpriteClipDef clip)
        {
            var ordered = new List<string>();
            if (clip?.Sockets == null)
                return ordered;
            var names = SpriteSocketKeys.UniqueNamesInOrder(clip.Sockets);
            for (int i = 0; i < names.Count; i++)
            {
                if (IsSocketSelected(names[i]))
                    ordered.Add(SpriteSocketKeys.CanonicalName(names[i]));
            }
            return ordered;
        }

        string UniquePatternSocketName(SpriteClipDef clip, List<string> pending, int pattern, int index, int count)
        {
            if (pattern == 0)
            {
                float tilt = count == 3 ? index * 60f : index * (360f / Mathf.Max(1, count));
                string orbitName = UniqueOrbitTiltName(clip, tilt);
                if (!pending.Contains(orbitName))
                    return orbitName;
            }

            string prefix = SocketOrbitPatternPrefixes[
                Mathf.Clamp(pattern, 0, SocketOrbitPatternPrefixes.Length - 1)];
            int n = index;
            while (true)
            {
                string candidate = SpriteSocketKeys.CanonicalName($"{prefix} {n}");
                if (SpriteSocketKeys.IdentityIndex(clip.Sockets, candidate) < 0 &&
                    !pending.Contains(candidate))
                    return candidate;
                n++;
            }
        }

        void StampSocketOrbitPattern(SpriteClipDef clip, int pattern, List<string> names,
            float radius, Vector2 center, float tilt)
        {
            switch (pattern)
            {
                case 0:
                    StampAtomicPattern(clip, names, radius, center);
                    break;
                case 1:
                    StampCoplanarPattern(clip, names, radius, center, tilt);
                    break;
                case 2:
                    StampNestedShellPattern(clip, names, radius, center, tilt);
                    break;
                case 3:
                    StampFigureEightPattern(clip, names, radius, center, tilt);
                    break;
                case 4:
                    StampSpiralPattern(clip, names, radius, center, tilt);
                    break;
                case 5:
                    StampFibonacciPattern(clip, names, radius, center, tilt);
                    break;
                default:
                    StampVesicaPattern(clip, names, radius, center, tilt);
                    break;
            }
        }

        void StampAtomicPattern(SpriteClipDef clip, List<string> names, float radius, Vector2 center)
        {
            int count = names.Count;
            for (int i = 0; i < count; i++)
            {
                float tilt = count == 3 ? i * 60f : i * (360f / count);
                SocketOrbitAxes(1, radius, tilt, EllipticalOrbitFlatten, out float rx, out float ry, out tilt);
                StampSocketOrbit(clip, names[i], rx, ry, tilt, center, i / (float)count);
            }
        }

        void StampCoplanarPattern(SpriteClipDef clip, List<string> names, float radius, Vector2 center, float tilt)
        {
            SocketOrbitAxes(_socketOrbitShape, radius, tilt, EllipticalOrbitFlatten,
                out float rx, out float ry, out tilt);
            int count = names.Count;
            for (int i = 0; i < count; i++)
                StampSocketOrbit(clip, names[i], rx, ry, tilt, center, i / (float)count);
        }

        void StampNestedShellPattern(SpriteClipDef clip, List<string> names, float radius, Vector2 center, float tilt)
        {
            int count = names.Count;
            int shells = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(count)), 1, 4);
            if (count <= 3)
                shells = count == 1 ? 1 : 2;
            int cursor = 0;
            int remaining = count;
            for (int s = 0; s < shells && cursor < count; s++)
            {
                int take = s == shells - 1 ? remaining : Mathf.Max(1, remaining / (shells - s));
                take = Mathf.Min(take, remaining);
                float ringRadius = radius * (s + 1) / shells;
                SocketOrbitAxes(_socketOrbitShape, ringRadius, tilt, EllipticalOrbitFlatten,
                    out float rx, out float ry, out float ringTilt);
                for (int o = 0; o < take; o++)
                    StampSocketOrbit(clip, names[cursor + o], rx, ry, ringTilt, center, o / (float)take);
                cursor += take;
                remaining -= take;
            }
        }

        void StampFigureEightPattern(SpriteClipDef clip, List<string> names, float radius, Vector2 center, float tilt)
        {
            SocketOrbitAxes(_socketOrbitShape, radius, 0f, EllipticalOrbitFlatten,
                out float rx, out float ry, out _);
            int count = names.Count;
            for (int i = 0; i < count; i++)
            {
                float phase = i / (float)count;
                StampSocketPath(clip, names[i], center, t =>
                {
                    float a = (t + phase) * Mathf.PI * 2f;
                    var point = new Vector2(rx * Mathf.Sin(a), ry * Mathf.Sin(a) * Mathf.Cos(a));
                    return RotateSocketOffset(point, tilt);
                });
            }
        }

        void StampSpiralPattern(SpriteClipDef clip, List<string> names, float radius, Vector2 center, float tilt)
        {
            int count = names.Count;
            for (int i = 0; i < count; i++)
            {
                float u = count == 1 ? 1f : (i + 1f) / count;
                float ringRadius = radius * Mathf.Lerp(0.28f, 1f, u);
                float ringTilt = tilt + u * 40f;
                SocketOrbitAxes(_socketOrbitShape, ringRadius, ringTilt, EllipticalOrbitFlatten,
                    out float rx, out float ry, out ringTilt);
                StampSocketOrbit(clip, names[i], rx, ry, ringTilt, center, i / (float)count);
            }
        }

        void StampFibonacciPattern(SpriteClipDef clip, List<string> names, float radius, Vector2 center, float tilt)
        {
            int count = names.Count;
            float small = Mathf.Max(6f, radius * 0.22f);
            SocketOrbitAxes(0, small, 0f, EllipticalOrbitFlatten, out float rx, out float ry, out _);
            for (int i = 0; i < count; i++)
            {
                float homeR = radius * Mathf.Sqrt((i + 0.5f) / count);
                float ang = (i * FibonacciGoldenAngle + tilt) * Mathf.Deg2Rad;
                Vector2 home = center + new Vector2(Mathf.Cos(ang) * homeR, Mathf.Sin(ang) * homeR);
                StampSocketOrbit(clip, names[i], rx, ry, 0f, home, i / (float)count);
            }
        }

        void StampVesicaPattern(SpriteClipDef clip, List<string> names, float radius, Vector2 center, float tilt)
        {
            int count = names.Count;
            int left = Mathf.Max(1, (count + 1) / 2);
            int right = Mathf.Max(1, count - left);
            Vector2 offset = RotateSocketOffset(new Vector2(radius * 0.55f, 0f), tilt);
            SocketOrbitAxes(_socketOrbitShape, radius, tilt, EllipticalOrbitFlatten,
                out float rx, out float ry, out float ringTilt);
            for (int i = 0; i < count; i++)
            {
                bool onLeft = i < left;
                int group = onLeft ? left : right;
                int local = onLeft ? i : i - left;
                Vector2 ringCenter = onLeft ? center - offset : center + offset;
                StampSocketOrbit(clip, names[i], rx, ry, ringTilt, ringCenter, local / (float)group);
            }
        }

        static Vector2 RotateSocketOffset(Vector2 point, float tiltDegrees)
        {
            if (Mathf.Abs(tiltDegrees) < 0.01f)
                return point;
            float r = tiltDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r);
            float s = Mathf.Sin(r);
            return new Vector2(point.x * c - point.y * s, point.x * s + point.y * c);
        }

        string UniqueOrbitTiltName(SpriteClipDef clip, float tilt)
        {
            string baseName = $"Orbit {Mathf.RoundToInt(Mathf.Repeat(tilt, 360f))}°";
            if (SpriteSocketKeys.IdentityIndex(clip.Sockets, baseName) < 0)
                return SpriteSocketKeys.CanonicalName(baseName);
            int n = 2;
            while (true)
            {
                string candidate = $"{baseName} {n}";
                if (SpriteSocketKeys.IdentityIndex(clip.Sockets, candidate) < 0)
                    return SpriteSocketKeys.CanonicalName(candidate);
                n++;
            }
        }

        void DuplicateSocketIdentity(SpriteClipDef clip, string name)
        {
            name = SpriteSocketKeys.CanonicalName(name);
            if (clip?.Sockets == null || string.IsNullOrEmpty(name))
                return;
            string copyName = UniqueSocketCopyName(clip, name);
            RecordProfileUndo("Duplicate Socket");
            var copies = new List<FrameSocketDef>();
            for (int i = 0; i < clip.Sockets.Count; i++)
            {
                var src = clip.Sockets[i];
                if (src == null || !SpriteSocketKeys.NamesEqual(src.Name, name))
                    continue;
                copies.Add(new FrameSocketDef
                {
                    Name = copyName,
                    FrameIndex = src.FrameIndex,
                    LocalPosition = src.LocalPosition,
                    LocalAngle = src.LocalAngle,
                    LocalScale = src.LocalScale,
                    DrawLayer = src.DrawLayer,
                });
            }

            if (copies.Count == 0)
            {
                SpriteSocketKeys.TryGetPose(clip.Sockets, name, _selectedFrame,
                    out var pose, out var angle, out var scale, out _);
                copies.Add(new FrameSocketDef
                {
                    Name = copyName,
                    FrameIndex = _selectedFrame,
                    LocalPosition = pose,
                    LocalAngle = angle,
                    LocalScale = scale,
                });
            }

            clip.Sockets.AddRange(copies);
            CopySocketOrbitCatalog(name, copyName);
            _selectedSockets.Clear();
            _selectedSockets.Add(copyName);
            _selectedSocketName = copyName;
            _status = $"Duplicated {name} → {copyName}";
            SaveDirty();
            Repaint();
        }

        string UniqueSocketCopyName(SpriteClipDef clip, string name)
        {
            string baseName = $"{name} copy";
            if (SpriteSocketKeys.IdentityIndex(clip.Sockets, baseName) < 0)
                return SpriteSocketKeys.CanonicalName(baseName);
            int n = 2;
            while (true)
            {
                string candidate = $"{name} copy {n}";
                if (SpriteSocketKeys.IdentityIndex(clip.Sockets, candidate) < 0)
                    return SpriteSocketKeys.CanonicalName(candidate);
                n++;
            }
        }

        void DeleteAllSockets(SpriteClipDef clip)
        {
            var names = SpriteSocketKeys.UniqueNamesInOrder(clip?.Sockets);
            if (names.Count == 0)
                return;
            if (!EditorUtility.DisplayDialog(
                    "Delete All Sockets",
                    $"Delete {names.Count} socket{(names.Count == 1 ? string.Empty : "s")} on this clip?",
                    "Delete All",
                    "Cancel"))
            {
                GUIUtility.ExitGUI();
                return;
            }

            RecordProfileUndo("Delete All Sockets");
            for (int i = 0; i < names.Count; i++)
            {
                SpriteSocketKeys.DeleteIdentity(clip.Sockets, names[i]);
                bool stillUsed = SpriteSocketKeys.NameExistsOnAnyClip(_profile.Clips, names[i]) ||
                                 _profile.FindSocketMotion(names[i]) != null;
                _profile.SocketCatalog.SyncDelete(names[i], stillUsed);
            }

            _selectedSockets.Clear();
            _selectedSocketName = null;
            _draggingSocket = false;
            _socketHandleKind = ColliderHandleKind.None;
            _status = "Deleted all sockets on this clip";
            SaveDirty();
            GUIUtility.ExitGUI();
        }

        void DeleteAllFrameAttachedSockets(SpriteClipDef clip)
        {
            var all = SpriteSocketKeys.UniqueNamesInOrder(clip?.Sockets);
            var names = new List<string>();
            for (int i = 0; i < all.Count; i++)
            {
                var item = _profile.SocketCatalog.Find(all[i]);
                if (item == null || !item.UsesOwnClock)
                    names.Add(all[i]);
            }
            if (names.Count == 0)
                return;
            if (!EditorUtility.DisplayDialog(
                    "Delete Frame-Attached Sockets From This Clip",
                    $"Delete {names.Count} Frame-Attached socket{Plural(names.Count)} from '{clip.Name}'?\n\nIndependent Motion tracks are not affected.",
                    "Delete This Clip",
                    "Cancel"))
            {
                GUIUtility.ExitGUI();
                return;
            }

            RecordProfileUndo("Delete Frame-Attached Sockets From Clip");
            for (int i = 0; i < names.Count; i++)
            {
                SpriteSocketKeys.DeleteIdentity(clip.Sockets, names[i]);
                bool stillUsed = SpriteSocketKeys.NameExistsOnAnyClip(_profile.Clips, names[i]) ||
                                 _profile.FindSocketMotion(names[i]) != null;
                _profile.SocketCatalog.SyncDelete(names[i], stillUsed);
            }
            ClearSocketSelection();
            _status = $"Deleted Frame-Attached sockets from {clip.Name}";
            SaveDirty();
            GUIUtility.ExitGUI();
        }

        void DeleteAllIndependentSockets()
        {
            var names = new List<string>();
            _profile.EnsureSocketMotions();
            _profile.EnsureSocketCatalog();
            for (int i = 0; i < _profile.SocketMotions.Count; i++)
                AddUniqueSocketName(names, _profile.SocketMotions[i]?.SocketName);
            for (int i = 0; i < _profile.SocketCatalog.Items.Count; i++)
            {
                var item = _profile.SocketCatalog.Items[i];
                if (item != null && item.UsesOwnClock)
                    AddUniqueSocketName(names, item.SocketName);
            }
            if (names.Count == 0)
                return;
            if (!EditorUtility.DisplayDialog(
                    "Delete Independent Motion",
                    $"Delete {names.Count} Independent Motion socket{Plural(names.Count)}?\n\nTheir profile tracks and matching legacy keys will be removed from every clip.",
                    "Delete Independent",
                    "Cancel"))
            {
                GUIUtility.ExitGUI();
                return;
            }

            RecordDiscreteUndo("Delete All Independent Socket Motion");
            for (int c = 0; c < _profile.Clips.Count; c++)
            {
                var sockets = _profile.Clips[c]?.Sockets;
                if (sockets == null)
                    continue;
                for (int i = 0; i < names.Count; i++)
                    SpriteSocketKeys.DeleteIdentity(sockets, names[i]);
            }
            for (int i = 0; i < names.Count; i++)
                _profile.SocketCatalog.Remove(names[i]);
            _profile.SocketMotions.Clear();
            ClearSocketSelection();
            _status = $"Deleted {names.Count} Independent Motion socket{Plural(names.Count)}";
            SaveDirty();
            SealUndoGroup();
            GUIUtility.ExitGUI();
        }

        void DeleteAllSocketsAcrossProfile()
        {
            var names = new List<string>();
            if (_profile.Clips != null)
            {
                for (int c = 0; c < _profile.Clips.Count; c++)
                {
                    var clipNames = SpriteSocketKeys.UniqueNamesInOrder(_profile.Clips[c]?.Sockets);
                    for (int i = 0; i < clipNames.Count; i++)
                        AddUniqueSocketName(names, clipNames[i]);
                }
            }
            _profile.EnsureSocketMotions();
            for (int i = 0; i < _profile.SocketMotions.Count; i++)
                AddUniqueSocketName(names, _profile.SocketMotions[i]?.SocketName);
            _profile.EnsureSocketCatalog();
            for (int i = 0; i < _profile.SocketCatalog.Items.Count; i++)
                AddUniqueSocketName(names, _profile.SocketCatalog.Items[i]?.SocketName);
            if (names.Count == 0)
                return;

            if (!EditorUtility.DisplayDialog(
                    "Delete All Sockets From All Clips",
                    $"Permanently delete all {names.Count} socket identit{(names.Count == 1 ? "y" : "ies")} from this profile?\n\nThis removes Frame-Attached keys from every clip, all Independent Motion tracks, and all socket catalog previews.",
                    "Delete Everything",
                    "Cancel"))
            {
                GUIUtility.ExitGUI();
                return;
            }

            RecordProfileUndo("Delete All Sockets From All Clips");
            for (int c = 0; c < _profile.Clips.Count; c++)
            {
                if (_profile.Clips[c]?.Sockets != null)
                    _profile.Clips[c].Sockets.Clear();
            }
            _profile.SocketMotions.Clear();
            _profile.SocketCatalog.Items.Clear();
            ClearSocketSelection();
            _socketPlacementArmed = false;
            _draggingSocket = false;
            _socketHandleKind = ColliderHandleKind.None;
            _status = $"Deleted all sockets from all {_profile.Clips.Count} clips";
            SaveDirty();
            GUIUtility.ExitGUI();
        }

        bool HasAnySocketData()
        {
            if (_profile?.SocketMotions != null && _profile.SocketMotions.Count > 0)
                return true;
            if (_profile?.SocketCatalog?.Items != null && _profile.SocketCatalog.Items.Count > 0)
                return true;
            if (_profile?.Clips == null)
                return false;
            for (int i = 0; i < _profile.Clips.Count; i++)
            {
                if (_profile.Clips[i]?.Sockets != null && _profile.Clips[i].Sockets.Count > 0)
                    return true;
            }
            return false;
        }

        static void AddUniqueSocketName(List<string> names, string candidate)
        {
            candidate = SpriteSocketKeys.CanonicalName(candidate);
            if (string.IsNullOrEmpty(candidate) || ListContainsSocketName(names, candidate))
                return;
            names.Add(candidate);
        }

        float DefaultSocketOrbitRadius()
        {
            if (_profile?.Sheet == null)
                return 32f;
            float w = _profile.Sheet.width / (float)Mathf.Max(1, _profile.Columns);
            float h = _profile.Sheet.height / (float)Mathf.Max(1, _profile.Rows);
            return Mathf.Max(16f, Mathf.Min(w, h) * 0.55f);
        }

        Vector2 DefaultSocketOrbitCenter()
        {
            if (_profile?.Sheet == null)
                return new Vector2(0f, 24f);
            float h = _profile.Sheet.height / (float)Mathf.Max(1, _profile.Rows);
            return new Vector2(0f, Mathf.Round(h * 0.32f));
        }

        void ApplySocketOrbitShape(SpriteClipDef clip, string name, int orbs)
        {
            name = SpriteSocketKeys.CanonicalName(name);
            if (clip?.Frames == null || clip.Frames.Length == 0 || string.IsNullOrEmpty(name))
                return;
            if (clip.Frames.Length < 4)
            {
                _status = "Orbit needs at least 4 frames in this clip";
                return;
            }

            orbs = Mathf.Clamp(orbs, 1, 12);
            RecordProfileUndo("Apply Elliptical Orbit");
            _profile.EnsureSocketCatalog();
            clip.Sockets ??= new List<FrameSocketDef>();

            float radius = _socketOrbitRadius > 1f ? _socketOrbitRadius : DefaultSocketOrbitRadius();
            Vector2 center = _socketOrbitCenterSet ? _socketOrbitCenter : DefaultSocketOrbitCenter();
            float tilt = SocketOrbitTiltDegrees(_socketOrbitTilt);
            SocketOrbitAxes(_socketOrbitShape, radius, tilt, EllipticalOrbitFlatten,
                out float rx, out float ry, out tilt);

            var names = new string[orbs];
            names[0] = name;
            for (int o = 1; o < orbs; o++)
                names[o] = NextOrbitSocketName(clip, name, o + 1);

            for (int o = 0; o < orbs; o++)
            {
                StampSocketOrbit(clip, names[o], rx, ry, tilt, center, o / (float)orbs);
                if (o > 0)
                    CopySocketOrbitCatalog(name, names[o]);
            }
            CaptureSocketMotionsFromClip(clip, names, replaceTiming: true);

            _selectedSockets.Clear();
            for (int o = 0; o < orbs; o++)
                _selectedSockets.Add(names[o]);
            _selectedSocketName = names[0];
            string shape = SocketOrbitShapeLabels[Mathf.Clamp(_socketOrbitShape, 0, SocketOrbitShapeLabels.Length - 1)];
            _status = orbs == 1
                ? $"{name}  {shape}  {tilt:0}°  r={radius:0}px"
                : $"{name}  {orbs} coplanar orbs  {shape}  {tilt:0}°  {360f / orbs:0.#}° phase";
            SaveDirty();
            GUIUtility.ExitGUI();
        }

        string NextOrbitSocketName(SpriteClipDef clip, string baseName, int index)
        {
            string candidate = $"{baseName} {index}";
            if (SpriteSocketKeys.IdentityIndex(clip.Sockets, candidate) < 0)
                return SpriteSocketKeys.CanonicalName(candidate);
            return SpriteSocketKeys.NextDefaultName(clip.Sockets);
        }

        void CopySocketOrbitCatalog(string fromName, string toName)
        {
            var source = _profile.SocketCatalog.Find(fromName);
            var dest = _profile.SocketCatalog.Ensure(toName);
            dest.MotionMode = (byte)SpriteSocketClockMode.OwnClock;
            dest.PathWrap = 0;
            if (source == null)
            {
                dest.Speed = 1f;
                return;
            }
            dest.Texture = source.Texture;
            dest.Profile = source.Profile;
            dest.ClipName = source.ClipName;
            dest.PlayMode = source.PlayMode;
            dest.Columns = source.Columns;
            dest.Rows = source.Rows;
            dest.Pivot = source.Pivot;
            dest.CellIndex = source.CellIndex;
            dest.GripPixels = source.GripPixels;
            dest.Scale = source.Scale;
            dest.FlipX = source.FlipX;
            dest.SortingOffset = source.SortingOffset;
            dest.PreviewEnabled = source.PreviewEnabled;
            dest.Speed = source.ResolvedSpeed;
        }

        void StampSocketOrbit(SpriteClipDef clip, string name, float rx, float ry, float tilt,
            Vector2 center, float phase)
        {
            StampSocketPath(clip, name, center, t => SocketOrbitPoint(t + phase, rx, ry, tilt));
        }

        void StampSocketPath(SpriteClipDef clip, string name, Vector2 center, Func<float, Vector2> localAt)
        {
            var item = _profile.SocketCatalog.Ensure(name);
            item.MotionMode = (byte)SpriteSocketClockMode.OwnClock;
            item.PathWrap = 0;
            if (item.Speed <= 0.0001f)
                item.Speed = 1f;

            int n = clip.Frames.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 local = localAt(i / (float)n);
                Vector2 pos = center + local;
                var key = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, name, i);
                key.LocalPosition = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));
                key.LocalAngle = 0f;
                key.LocalScale = Vector2.one;
                key.DrawLayer = local.y > 0.5f
                    ? SpriteSocketKeys.DrawBehind
                    : SpriteSocketKeys.DrawFront;
            }
        }

        static void SocketOrbitAxes(int shape, float radius, float tiltDegrees, float flatten,
            out float rx, out float ry, out float tilt)
        {
            radius = Mathf.Max(4f, radius);
            tilt = tiltDegrees;
            if (shape == 0)
            {
                rx = radius;
                ry = radius;
                return;
            }

            flatten = Mathf.Clamp(flatten, 0.12f, 1f);
            rx = radius;
            ry = radius * flatten;
        }

        static float SocketOrbitTiltDegrees(int index)
            => Mathf.Clamp(index, 0, 11) * 15f;

        static Vector2 SocketOrbitPoint(float t, float rx, float ry, float tiltDegrees)
        {
            float a = t * Mathf.PI * 2f;
            var point = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
            if (Mathf.Abs(tiltDegrees) < 0.01f)
                return point;
            float r = tiltDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r);
            float s = Mathf.Sin(r);
            return new Vector2(point.x * c - point.y * s, point.x * s + point.y * c);
        }

        void DrawSocketMotionPaths(Rect cell, SpriteClipDef clip, int frame)
        {
            if (!_showPreviewDebug || clip?.Sockets == null || _profile?.Sheet == null)
                return;
            var names = CachedUniqueSocketNames(clip);
            if (names.Count == 0)
                return;

            Handles.BeginGUI();
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                bool selected = IsSocketSelected(name);
                if (_selectedSockets.Count > 0 && !selected)
                    continue;
                bool ownClock = SpriteSocketKeys.UsesOwnClock(_profile.SocketCatalog, name);
                if (ownClock)
                {
                    if (!_showIndependentMotionPaths)
                        continue;
                    var track = _profile.FindSocketMotion(name);
                    if (track?.Keys == null || track.Keys.Count == 0)
                        continue;
                    Color motionColor = SpriteSocketKeys.ColorForIndex(i);
                    motionColor.a = selected ? 0.95f : 0.45f;
                    Handles.color = motionColor;
                    float referencePpu = SpriteSheetProfile.GetPixelsPerUnit(
                        _profile.SheetAt(track.ReferenceSheetIndex));
                    float targetPpu = SpriteSheetProfile.GetPixelsPerUnit(
                        _profile.SheetAt(clip.SheetIndex));
                    float ppuScale = targetPpu / Mathf.Max(1f, referencePpu);
                    if (track.Keys.Count >= 2)
                    {
                        const int steps = 64;
                        for (int s = 0; s <= steps; s++)
                        {
                            Vector2 point = SampleIndependentTrackPathPosition(
                                track, s / (float)steps) * ppuScale;
                            _socketPathPointBuffer[s] = SocketToScreen(point, cell);
                        }
                        Handles.DrawAAPolyLine(
                            selected ? 3f : 1.6f, _socketPathPointBuffer);
                    }
                    for (int k = 0; k < track.Keys.Count; k++)
                    {
                        Vector2 screen = SocketToScreen(
                            track.Keys[k].LocalPosition * ppuScale, cell);
                        bool isCurrent = Mathf.Abs(
                            track.Keys[k].NormalizedTime -
                            CurrentIndependentMotionTime()) <= 0.0001f;
                        Handles.color = isCurrent ? Color.white : motionColor;
                        Handles.DrawSolidDisc(
                            screen, Vector3.forward, isCurrent ? 4.5f : 3f);
                    }
                    if (selected)
                        DrawIndependentMotionPathHandles(
                            track, ppuScale, cell);
                    if (TryGetPreviewSocketPose(
                            clip, name, frame, out var motionLive, out _, out _, out _))
                    {
                        Vector2 motionTraveler = SocketToScreen(motionLive, cell);
                        Handles.color = Color.white;
                        Handles.DrawSolidDisc(motionTraveler, Vector3.forward, 5.5f);
                        Handles.color = motionColor;
                        Handles.DrawSolidDisc(motionTraveler, Vector3.forward, 3.4f);
                    }
                    continue;
                }
                SpriteSocketKeys.CollectKeysSorted(clip.Sockets, name, _socketPathKeys);
                if (_socketPathKeys.Count == 0)
                    continue;

                Color color = SpriteSocketKeys.ColorForIndex(i);
                color.a = selected ? 0.95f : 0.45f;
                _socketPathPoints.Clear();
                Handles.color = color;
                bool closed = SpriteSocketKeys.UsesClosedPath(_profile.SocketCatalog, name);
                for (int k = 0; k < _socketPathKeys.Count; k++)
                    _socketPathPoints.Add(SocketToScreen(_socketPathKeys[k].LocalPosition, cell));
                if (closed && _socketPathPoints.Count >= 2)
                    _socketPathPoints.Add(_socketPathPoints[0]);

                if (_socketPathPoints.Count >= 2)
                    Handles.DrawAAPolyLine(selected ? 3f : 1.6f, _socketPathPoints.ToArray());

                for (int k = 0; k < _socketPathKeys.Count; k++)
                {
                    Vector2 screen = SocketToScreen(_socketPathKeys[k].LocalPosition, cell);
                    bool isCurrent = _socketPathKeys[k].FrameIndex == frame;
                    Handles.color = isCurrent ? Color.white : color;
                    Handles.DrawSolidDisc(screen, Vector3.forward, isCurrent ? 4.5f : 3f);
                }

                if (!TryGetPreviewSocketPose(clip, name, frame,
                        out var live, out _, out _, out _))
                    continue;
                Vector2 traveler = SocketToScreen(live, cell);
                Handles.color = new Color(1f, 1f, 1f, selected ? 0.95f : 0.55f);
                Handles.DrawSolidDisc(traveler, Vector3.forward, 5.5f);
                Handles.color = color;
                Handles.DrawSolidDisc(traveler, Vector3.forward, 3.4f);
            }
            Handles.EndGUI();
        }

        void DrawIndependentMotionPathHandles(
            SpriteSocketMotionTrack track, float ppuScale, Rect cell)
        {
            SpriteSocketMotionKey key = null;
            int keyIndex = -1;
            for (int i = 0; i < track.Keys.Count; i++)
            {
                if (!_selectedSocketMotionKeys.Contains(track.Keys[i]) &&
                    !(_profile.SocketMotions.IndexOf(track) == _selectedSocketMotionTrack &&
                      i == _selectedSocketMotionKey))
                    continue;
                key = track.Keys[i];
                keyIndex = i;
                break;
            }
            if (key == null)
                return;

            Vector2 anchor = SocketToScreen(key.LocalPosition * ppuScale, cell);
            Vector2 inPoint = SocketToScreen(
                (key.LocalPosition + key.InTangent) * ppuScale, cell);
            Vector2 outPoint = SocketToScreen(
                (key.LocalPosition + key.OutTangent) * ppuScale, cell);
            Vector2 arcPoint = anchor;
            bool showTangents = key.PathMode is
                (byte)SpriteSocketPathMode.CubicBezier or
                (byte)SpriteSocketPathMode.Hermite;
            bool showArc = key.PathMode == (byte)SpriteSocketPathMode.Arc &&
                           track.Keys.Count > 1;
            if (showArc)
            {
                int next = keyIndex + 1 < track.Keys.Count
                    ? keyIndex + 1
                    : track.Loop ? 0 : keyIndex;
                Vector2 from = key.LocalPosition;
                Vector2 to = track.Keys[next].LocalPosition;
                Vector2 delta = to - from;
                Vector2 normal = delta.sqrMagnitude > 0.0001f
                    ? new Vector2(-delta.y, delta.x).normalized
                    : Vector2.up;
                float sign = key.ArcClockwise ? -1f : 1f;
                Vector2 control = (from + to) * 0.5f +
                                  normal * Mathf.Abs(key.ArcBulge) * sign;
                arcPoint = SocketToScreen(control * ppuScale, cell);
            }

            Handles.color = new Color(0.3f, 0.85f, 1f, 0.9f);
            if (showTangents)
            {
                Handles.DrawAAPolyLine(1.2f, inPoint, anchor, outPoint);
                Handles.DrawSolidDisc(inPoint, Vector3.forward, 4.5f);
                Handles.DrawSolidDisc(outPoint, Vector3.forward, 4.5f);
            }
            if (showArc)
            {
                Handles.DrawAAPolyLine(1.2f, anchor, arcPoint);
                Handles.DrawSolidDisc(arcPoint, Vector3.forward, 5f);
            }

            var evt = Event.current;
            int kind = showTangents && (evt.mousePosition - inPoint).sqrMagnitude <= 64f
                ? 1
                : showTangents && (evt.mousePosition - outPoint).sqrMagnitude <= 64f
                    ? 2
                    : showArc && (evt.mousePosition - arcPoint).sqrMagnitude <= 81f
                        ? 3
                        : 0;
            int controlId = GUIUtility.GetControlID(
                ("IndependentPathHandle" + track.SocketName).GetHashCode(),
                FocusType.Passive);
            if (evt.type == EventType.MouseDown && evt.button == 0 && kind != 0)
            {
                RecordProfileUndo(kind == 3
                    ? "Move Independent Motion Arc Handle"
                    : "Move Independent Motion Tangent");
                _motionPathHandleKey = key;
                _motionPathHandleKind = kind;
                _motionPathHandleHotControl = controlId;
                _motionPathHandleOriginalIn = key.InTangent;
                _motionPathHandleOriginalOut = key.OutTangent;
                _motionPathHandleOriginalBulge = key.ArcBulge;
                _motionPathHandleOriginalClockwise = key.ArcClockwise;
                GUIUtility.hotControl = controlId;
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag &&
                     GUIUtility.hotControl == _motionPathHandleHotControl &&
                     _motionPathHandleKey == key)
            {
                Vector2 mouseLocal = ScreenToSocketLocal(evt.mousePosition, cell) /
                                     Mathf.Max(0.0001f, ppuScale);
                if (_motionPathHandleKind == 1)
                    key.InTangent = mouseLocal - key.LocalPosition;
                else if (_motionPathHandleKind == 2)
                    key.OutTangent = mouseLocal - key.LocalPosition;
                else
                {
                    int next = keyIndex + 1 < track.Keys.Count
                        ? keyIndex + 1
                        : track.Loop ? 0 : keyIndex;
                    Vector2 to = track.Keys[next].LocalPosition;
                    Vector2 delta = to - key.LocalPosition;
                    Vector2 normal = delta.sqrMagnitude > 0.0001f
                        ? new Vector2(-delta.y, delta.x).normalized
                        : Vector2.up;
                    float signed = Vector2.Dot(
                        mouseLocal - (key.LocalPosition + to) * 0.5f, normal);
                    key.ArcClockwise = signed < 0f;
                    key.ArcBulge = Mathf.Abs(signed);
                }
                evt.Use();
                Repaint();
            }
            else if (evt.type == EventType.KeyDown &&
                     evt.keyCode == KeyCode.Escape &&
                     GUIUtility.hotControl == _motionPathHandleHotControl &&
                     _motionPathHandleKey == key)
            {
                key.InTangent = _motionPathHandleOriginalIn;
                key.OutTangent = _motionPathHandleOriginalOut;
                key.ArcBulge = _motionPathHandleOriginalBulge;
                key.ArcClockwise = _motionPathHandleOriginalClockwise;
                EndIndependentMotionPathHandleDrag();
                evt.Use();
                Repaint();
            }
            else if (evt.type == EventType.MouseUp && evt.button == 0 &&
                     GUIUtility.hotControl == _motionPathHandleHotControl)
            {
                SaveDirty();
                SealUndoGroup();
                EndIndependentMotionPathHandleDrag();
                evt.Use();
                Repaint();
            }
        }

        void EndIndependentMotionPathHandleDrag()
        {
            if (GUIUtility.hotControl == _motionPathHandleHotControl)
                GUIUtility.hotControl = 0;
            _motionPathHandleKey = null;
            _motionPathHandleKind = 0;
            _motionPathHandleHotControl = 0;
        }

        static Vector2 SampleIndependentTrackPathPosition(
            SpriteSocketMotionTrack track, float normalizedTime)
        {
            int count = track.Keys.Count;
            if (count == 1)
                return track.Keys[0].LocalPosition;
            float t = track.Loop
                ? Mathf.Repeat(normalizedTime, 1f)
                : Mathf.Clamp01(normalizedTime);
            int from = 0;
            int to = 1;
            float blend = 0f;
            int last = count - 1;
            if (t < track.Keys[0].NormalizedTime && track.Loop)
            {
                from = last;
                to = 0;
                float span = 1f - track.Keys[last].NormalizedTime +
                             track.Keys[0].NormalizedTime;
                blend = span > 0.0001f
                    ? (t + 1f - track.Keys[last].NormalizedTime) / span
                    : 0f;
            }
            else if (t >= track.Keys[last].NormalizedTime)
            {
                if (!track.Loop)
                    return track.Keys[last].LocalPosition;
                from = last;
                to = 0;
                float span = 1f - track.Keys[last].NormalizedTime +
                             track.Keys[0].NormalizedTime;
                blend = span > 0.0001f
                    ? (t - track.Keys[last].NormalizedTime) / span
                    : 0f;
            }
            else
            {
                for (int i = 0; i < last; i++)
                {
                    if (t >= track.Keys[i + 1].NormalizedTime)
                        continue;
                    from = i;
                    to = i + 1;
                    float span = track.Keys[to].NormalizedTime -
                                 track.Keys[from].NormalizedTime;
                    blend = span > 0.0001f
                        ? (t - track.Keys[from].NormalizedTime) / span
                        : 0f;
                    break;
                }
            }
            var a = track.Keys[from];
            blend = a.UseCustomEase
                ? a.EvaluateCustomEase(blend)
                : SpriteEase.Evaluate(
                    SpriteEase.IsValidMode(a.EaseMode)
                        ? (SpriteEaseMode)a.EaseMode
                        : SpriteEaseMode.SmoothStep,
                    blend, a.AllowOvershoot);
            int before = track.Loop
                ? (from - 1 + count) % count
                : Mathf.Max(0, from - 1);
            int after = track.Loop
                ? (to + 1) % count
                : Mathf.Min(last, to + 1);
            return EvaluateEditorMotionPosition(
                a,
                track.Keys[before].LocalPosition,
                a.LocalPosition,
                track.Keys[to].LocalPosition,
                track.Keys[after].LocalPosition,
                track.Keys[to].InTangent,
                blend);
        }

        void DrawSocketProfilePreviewFields(SpriteSocketCatalogItem item, SpriteClipDef hostClip)
        {
            var data = item.Profile?.Data;
            data?.EnsureSheets();
            bool hasClips = data?.Clips != null && data.Clips.Count > 0;
            using (new EditorGUI.DisabledScope(!hasClips))
            {
                item.PlayMode = (byte)EditorGUILayout.Popup(
                    new GUIContent("Play",
                        "Cell: still sheet cell. Play Clip: this item's clip follows the preview clock. Follow Character: same frame index as the host clip."),
                    item.PlayMode, SocketPreviewPlayModeLabels);
            }

            if (hasClips)
            {
                string[] clipNames = new string[data.Clips.Count];
                int clipIndex = 0;
                for (int i = 0; i < data.Clips.Count; i++)
                {
                    clipNames[i] = string.IsNullOrEmpty(data.Clips[i]?.Name)
                        ? $"Clip {i + 1}"
                        : data.Clips[i].Name;
                    if (string.Equals(data.Clips[i]?.Name, item.ClipName, StringComparison.Ordinal))
                        clipIndex = i;
                }
                int nextClip = EditorGUILayout.Popup(
                    new GUIContent("Clip", "Which clip this socket preview plays."),
                    clipIndex, clipNames);
                item.ClipName = data.Clips[nextClip]?.Name ?? string.Empty;
            }
            else
            {
                GUILayout.Label("This profile has no clips. Using sheet cells.", _mutedStyle);
                item.PlayMode = (byte)SpriteSocketPreviewPlayMode.Cell;
            }

            if (item.PreviewPlayMode == SpriteSocketPreviewPlayMode.Cell)
            {
                int cellCount = SocketPreviewCellCount(item);
                if (cellCount > 1)
                    item.CellIndex = EditorGUILayout.IntSlider("Cell", item.CellIndex, 0, cellCount - 1);
            }

            if (TryResolveSocketPreview(item, hostClip, _selectedFrame,
                    out _, out _, out _, out int cellIndex, out string clipLabel,
                    out int playFrame, out int playCount))
            {
                string playing = item.PreviewPlayMode == SpriteSocketPreviewPlayMode.Cell
                    ? $"Showing cell {cellIndex}" + (string.IsNullOrEmpty(clipLabel) ? string.Empty : $"  •  {clipLabel}")
                    : $"Playing {clipLabel}  •  frame {playFrame + 1}/{Mathf.Max(1, playCount)}";
                GUILayout.Label(playing, _mutedStyle);
            }
        }

        static int SocketPreviewCellCount(SpriteSocketCatalogItem item)
        {
            var data = item?.Profile?.Data;
            if (data == null)
                return item != null ? item.CellCount : 1;
            data.EnsureSheets();
            var clip = data.FindClip(item.ClipName);
            var sheet = data.SheetForClip(clip) ?? data.SheetAt(0);
            int columns = sheet != null && sheet.Columns > 0 ? sheet.Columns : Mathf.Max(1, data.Columns);
            int rows = sheet != null && sheet.Rows > 0 ? sheet.Rows : Mathf.Max(1, data.Rows);
            return Mathf.Max(1, columns * rows);
        }

        void ApplyDefaultSocketPreviewClip(SpriteSocketCatalogItem item)
        {
            if (item?.Profile?.Data == null)
                return;
            var data = item.Profile.Data;
            data.EnsureSheets();
            var clip = data.FindClip(item.ClipName);
            if (clip != null)
            {
                item.ClipName = clip.Name ?? string.Empty;
                if (item.PlayMode == (byte)SpriteSocketPreviewPlayMode.Cell &&
                    data.Clips != null && data.Clips.Count > 0)
                    item.PlayMode = (byte)SpriteSocketPreviewPlayMode.PlayClip;
            }
        }

        void DrawSocketPreviewThumbnail(Rect rect, SpriteSocketCatalogItem item)
        {
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.09f, 0.11f, 1f));
            DrawBorder(rect, new Color(0.28f, 0.3f, 0.34f, 1f), 1f);
            if (SocketSelectionBusy || item == null || !item.HasPreview)
                return;
            if (!TryResolveSocketPreview(item, CurrentClip, _selectedFrame,
                    out var texture, out int columns, out int rows, out int cellIndex,
                    out _, out _, out _))
                return;
            var inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            DrawCellTinted(texture, cellIndex, inner, Color.white, columns, rows);
        }

        void HandleSocketPreviewDragDrop(Rect dropRect, string socketName)
        {
            var evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform &&
                evt.type != EventType.Repaint)
                return;
            if (!dropRect.Contains(evt.mousePosition))
                return;

            bool hasProfile = TryGetDraggedSocketPreviewProfile(out var profile);
            bool hasTexture = TryGetDraggedSocketPreviewTexture(out var texture);
            if (!hasProfile && !hasTexture)
                return;

            if (evt.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(dropRect, new Color(0.18f, 0.55f, 0.82f, 0.18f));
                DrawBorder(dropRect, AccentColor, 1f);
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (hasProfile)
                {
                    if (IsSocketSelected(socketName) && _selectedSockets.Count > 1)
                        AssignSocketPreviewProfileToNames(_selectedSockets, profile);
                    else
                        AssignSocketPreviewProfile(socketName, profile);
                }
                else if (IsSocketSelected(socketName) && _selectedSockets.Count > 1)
                    AssignSocketPreviewTextureToNames(_selectedSockets, texture);
                else
                    AssignSocketPreviewTexture(socketName, texture);
            }
            evt.Use();
        }

        static bool TryGetDraggedSocketPreviewTexture(out Texture2D texture)
        {
            texture = null;
            var objects = DragAndDrop.objectReferences;
            if (objects == null)
                return false;
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] is Texture2D tex)
                {
                    texture = tex;
                    return true;
                }
                if (objects[i] is Sprite sprite && sprite.texture != null)
                {
                    texture = sprite.texture;
                    return true;
                }
            }
            return false;
        }

        static bool TryGetDraggedSocketPreviewProfile(out ScriptableSpriteSheetProfile profile)
        {
            profile = null;
            var objects = DragAndDrop.objectReferences;
            if (objects == null)
                return false;
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] is ScriptableSpriteSheetProfile asset)
                {
                    profile = asset;
                    return true;
                }
            }
            return false;
        }

        void AssignSocketPreviewTexture(string socketName, Texture2D texture)
        {
            AssignSocketPreviewTextureToNames(new[] { socketName }, texture);
        }

        void AssignSocketPreviewProfile(string socketName, ScriptableSpriteSheetProfile profile)
        {
            AssignSocketPreviewProfileToNames(new[] { socketName }, profile);
        }

        void AssignSocketPreviewTextureToNames(IEnumerable<string> names, Texture2D texture)
        {
            if (texture == null)
                return;
            var list = CollectSocketNames(names);
            if (list.Count == 0)
                return;
            RecordProfileUndo(list.Count == 1 ? "Assign Socket Preview" : "Assign Socket Previews");
            _profile.EnsureSocketCatalog();
            for (int i = 0; i < list.Count; i++)
                _profile.SocketCatalog.Ensure(list[i]).Texture = texture;
            _status = list.Count == 1
                ? $"Preview {texture.name} on {list[0]}"
                : $"Preview {texture.name} on {list.Count} sockets";
            SaveDirty();
            Repaint();
        }

        void AssignSocketPreviewProfileToNames(IEnumerable<string> names, ScriptableSpriteSheetProfile profile)
        {
            if (profile == null)
                return;
            var list = CollectSocketNames(names);
            if (list.Count == 0)
                return;
            RecordProfileUndo(list.Count == 1 ? "Assign Socket Profile" : "Assign Socket Profiles");
            _profile.EnsureSocketCatalog();
            for (int i = 0; i < list.Count; i++)
            {
                var item = _profile.SocketCatalog.Ensure(list[i]);
                bool firstProfile = item.Profile == null;
                item.Profile = profile;
                if (firstProfile)
                    ApplyDefaultSocketPreviewClip(item);
            }
            _status = list.Count == 1
                ? $"Profile {profile.name} on {list[0]}"
                : $"Profile {profile.name} on {list.Count} sockets";
            SaveDirty();
            Repaint();
        }

        void ClearSocketPreviewOnNames(IEnumerable<string> names)
        {
            var list = CollectSocketNames(names);
            if (list.Count == 0)
                return;
            RecordProfileUndo(list.Count == 1 ? "Clear Socket Profile" : "Clear Socket Profiles");
            _profile.EnsureSocketCatalog();
            for (int i = 0; i < list.Count; i++)
            {
                var item = _profile.SocketCatalog.Find(list[i]);
                if (item == null)
                    continue;
                if (item.Texture == null)
                    _profile.SocketCatalog.Remove(list[i]);
                else
                    item.Profile = null;
            }
            _status = list.Count == 1
                ? $"Cleared profile on {list[0]}"
                : $"Cleared profiles on {list.Count} sockets";
            SaveDirty();
            Repaint();
        }

        static List<string> CollectSocketNames(IEnumerable<string> names)
        {
            var list = new List<string>();
            if (names == null)
                return list;
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                string canonical = SpriteSocketKeys.CanonicalName(name);
                if (!list.Contains(canonical))
                    list.Add(canonical);
            }
            return list;
        }

        void DrawSocketSelectionProfileField()
        {
            if (_selectedSockets.Count == 0)
                return;
            _profile.EnsureSocketCatalog();
            ScriptableSpriteSheetProfile shared = null;
            bool hasShared = false;
            bool mixed = false;
            foreach (string name in _selectedSockets)
            {
                var profile = _profile.SocketCatalog.Find(name)?.Profile;
                if (!hasShared)
                {
                    shared = profile;
                    hasShared = true;
                }
                else if (profile != shared)
                    mixed = true;
            }

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = mixed;
            var next = (ScriptableSpriteSheetProfile)EditorGUILayout.ObjectField(
                new GUIContent("Profile",
                    "Assign this animation profile to every selected socket."),
                mixed ? null : shared, typeof(ScriptableSpriteSheetProfile), false);
            EditorGUI.showMixedValue = false;
            if (!EditorGUI.EndChangeCheck())
                return;
            if (next == null)
                ClearSocketPreviewOnNames(_selectedSockets);
            else
                AssignSocketPreviewProfileToNames(_selectedSockets, next);
        }

        void ShowSocketProfilePicker(IEnumerable<string> names)
        {
            _socketProfileAssignNames.Clear();
            _socketProfileAssignNames.AddRange(CollectSocketNames(names));
            if (_socketProfileAssignNames.Count == 0)
                return;
            ScriptableSpriteSheetProfile current = _socketProfileAssignNames.Count == 1
                ? _profile.SocketCatalog.Find(_socketProfileAssignNames[0])?.Profile
                : null;
            EditorGUIUtility.ShowObjectPicker<ScriptableSpriteSheetProfile>(
                current, false, string.Empty, SocketProfilePickerId);
        }

        void PollSocketProfilePicker()
        {
            var evt = Event.current;
            if (evt.type != EventType.ExecuteCommand)
                return;
            if (evt.commandName != "ObjectSelectorClosed")
                return;
            if (EditorGUIUtility.GetObjectPickerControlID() != SocketProfilePickerId)
                return;
            var picked = EditorGUIUtility.GetObjectPickerObject() as ScriptableSpriteSheetProfile;
            evt.Use();
            if (picked != null && _socketProfileAssignNames.Count > 0)
                AssignSocketPreviewProfileToNames(_socketProfileAssignNames, picked);
        }

        bool TryResolveSocketPreview(SpriteSocketCatalogItem item, SpriteClipDef hostClip, int hostFrame,
            out Texture2D texture, out int columns, out int rows, out int cellIndex,
            out string clipLabel, out int playFrame, out int playCount)
        {
            texture = null;
            columns = 1;
            rows = 1;
            cellIndex = 0;
            clipLabel = string.Empty;
            playFrame = 0;
            playCount = 1;
            if (item == null)
                return false;
            item.Normalize();

            var data = item.Profile?.Data;
            if (data != null)
            {
                data.EnsureSheets();
                var previewClip = data.FindClip(item.ClipName);
                clipLabel = previewClip?.Name ?? string.Empty;
                var mode = item.PreviewPlayMode;
                if (mode != SpriteSocketPreviewPlayMode.Cell && previewClip != null)
                {
                    previewClip.EnsureFrameData();
                    playCount = Mathf.Max(1, previewClip.Frames.Length);
                    if (mode == SpriteSocketPreviewPlayMode.FollowHost)
                    {
                        int hostCount = hostClip?.Frames != null
                            ? Mathf.Max(1, hostClip.Frames.Length)
                            : playCount;
                        playFrame = hostCount == playCount
                            ? Mathf.Clamp(hostFrame, 0, playCount - 1)
                            : Mathf.Clamp(Mathf.FloorToInt(hostFrame / (float)hostCount * playCount),
                                0, playCount - 1);
                    }
                    else
                    {
                        playFrame = EvaluatePreview(previewClip, _previewTime).Frame;
                    }
                    return data.TryGetClipDrawCell(previewClip, playFrame,
                        out texture, out columns, out rows, out cellIndex);
                }

                var sheet = data.SheetForClip(previewClip) ?? data.SheetAt(0);
                texture = sheet?.Texture ?? data.Sheet;
                if (texture == null)
                    return false;
                columns = sheet != null && sheet.Columns > 0 ? sheet.Columns : Mathf.Max(1, data.Columns);
                rows = sheet != null && sheet.Rows > 0 ? sheet.Rows : Mathf.Max(1, data.Rows);
                playCount = Mathf.Max(1, columns * rows);
                cellIndex = Mathf.Clamp(item.CellIndex, 0, playCount - 1);
                playFrame = cellIndex;
                return true;
            }

            if (item.Texture == null)
                return false;
            texture = item.Texture;
            columns = item.Columns;
            rows = item.Rows;
            playCount = item.CellCount;
            cellIndex = item.CellIndex;
            playFrame = cellIndex;
            return true;
        }

        bool SocketPreviewDrawsBehind(SpriteClipDef clip, string name,
            SpriteSocketCatalogItem item)
        {
            bool catalogBehind = SpriteSocketKeys.CatalogDrawsBehind(item);
            if (item != null && item.UsesOwnClock)
                return SpriteSocketKeys.IsIndependentDrawnBehind(
                    _profile?.FindSocketMotion(name), CurrentIndependentMotionTime(),
                    catalogBehind);
            return SpriteSocketKeys.IsDrawnBehindAtTime(
                clip?.Sockets, name, clip, SocketPreviewSampleTime(clip, name),
                catalogBehind, SocketSampleClosed(clip, name));
        }

        void DrawSocketCatalogPreviews(Rect cell, SpriteClipDef clip, int frame, bool behind)
        {
            if (SocketSelectionBusy)
                return;
            if (!_showSocketPreviews || _profile.Sheet == null || clip == null)
                return;
            _profile.EnsureSocketCatalog();
            clip.Sockets ??= new List<FrameSocketDef>();
            var names = CachedUniqueSocketNames(clip);
            for (int i = 0; i < names.Count; i++)
            {
                var item = _profile.SocketCatalog.Find(names[i]);
                if (item == null || !item.HasPreview || !item.PreviewEnabled)
                    continue;
                bool itemBehind = SocketPreviewDrawsBehind(clip, names[i], item);
                if (itemBehind != behind)
                    continue;
                if (!TryResolveSocketPreview(item, clip, frame,
                        out var texture, out int columns, out int rows, out int cellIndex,
                        out _, out _, out int playFrame))
                    continue;
                if (!TryGetPreviewSocketPose(clip, names[i], frame,
                        out var position, out var angle, out var scale, out _))
                    continue;
                DrawSocketCatalogItem(cell, item, texture, columns, rows, cellIndex,
                    position, angle, scale, 1f, playFrame);
            }
        }

        void DrawSocketCatalogItem(Rect cell, SpriteSocketCatalogItem item, Texture2D texture,
            int columns, int rows, int cellIndex, Vector2 localPixels, float angleDegrees,
            Vector2 poseScale, float alpha, int playFrame = 0)
        {
            if (!TryBuildSocketPreviewScreen(cell, item, texture, columns, rows, localPixels,
                    angleDegrees, poseScale, out var attachScreen, out var spriteRect,
                    out float signX, out float signY))
                return;

            Matrix4x4 previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(-angleDegrees, attachScreen);
            if (!Mathf.Approximately(signX, 1f) || !Mathf.Approximately(signY, 1f))
                GUIUtility.ScaleAroundPivot(new Vector2(signX, signY), attachScreen);
            DrawCellTinted(texture, cellIndex, spriteRect, new Color(1f, 1f, 1f, alpha),
                columns, rows);
            if (_showHitboxes)
                DrawSocketProfileColliders(item, spriteRect, playFrame);
            GUI.matrix = previous;
        }

        void DrawSocketProfileColliders(SpriteSocketCatalogItem item, Rect spriteRect, int playFrame)
        {
            var data = item?.Profile?.Data;
            if (data?.Hitboxes == null || data.Hitboxes.Count == 0)
                return;
            string clipName = item.ClipName;
            if (string.IsNullOrEmpty(clipName) && data.Clips != null && data.Clips.Count > 0)
                clipName = data.Clips[0].Name;
            var color = new Color(1f, 0.72f, 0.22f, 0.4f);
            foreach (var box in SpriteColliderWorld.VisibleOn(data.Hitboxes, clipName, playFrame))
            {
                if (box.Hidden)
                    continue;
                DrawColliderUV(box, spriteRect, box.IsCharacter
                    ? new Color(0.35f, 0.95f, 0.7f, 0.38f)
                    : box.IsClip
                        ? new Color(0.95f, 0.72f, 0.22f, 0.4f)
                        : color);
            }
        }

        bool TryBuildSocketPreviewScreen(Rect cell, SpriteSocketCatalogItem item, Texture2D texture,
            int columns, int rows, Vector2 localPixels, float angleDegrees, Vector2 poseScale,
            out Vector2 attachScreen, out Rect spriteRect, out float signX, out float signY)
        {
            attachScreen = default;
            spriteRect = default;
            signX = 1f;
            signY = 1f;
            if (item == null || texture == null || _profile?.Sheet == null)
                return false;
            item.Normalize();
            poseScale = SpriteSocketKeys.ResolvedScale(poseScale);
            float rad = angleDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            var rotatedGrip = new Vector2(
                item.GripPixels.x * cos - item.GripPixels.y * sin,
                item.GripPixels.x * sin + item.GripPixels.y * cos);
            attachScreen = SocketToScreen(localPixels + rotatedGrip, cell);

            float sourceWidth = _profile.Sheet.width / (float)Mathf.Max(1, _profile.Columns);
            float sourceHeight = _profile.Sheet.height / (float)Mathf.Max(1, _profile.Rows);
            float itemWidth = texture.width / (float)Mathf.Max(1, columns);
            float itemHeight = texture.height / (float)Mathf.Max(1, rows);
            signX = (item.FlipX ? -1f : 1f) * Mathf.Sign(poseScale.x == 0f ? 1f : poseScale.x);
            signY = Mathf.Sign(poseScale.y == 0f ? 1f : poseScale.y);
            float screenW = itemWidth / Mathf.Max(1f, sourceWidth) * cell.width *
                            item.Scale * Mathf.Abs(poseScale.x);
            float screenH = itemHeight / Mathf.Max(1f, sourceHeight) * cell.height *
                            item.Scale * Mathf.Abs(poseScale.y);
            if (screenW < 1f || screenH < 1f)
                return false;

            Vector2 pivotGui = new Vector2(item.Pivot.x * screenW, (1f - item.Pivot.y) * screenH);
            spriteRect = new Rect(
                attachScreen.x - pivotGui.x,
                attachScreen.y - pivotGui.y,
                screenW,
                screenH);
            return true;
        }

        static Rect FlipRectAround(Rect rect, Vector2 pivot, float signX, float signY)
        {
            if (Mathf.Approximately(signX, 1f) && Mathf.Approximately(signY, 1f))
                return rect;
            float x0 = pivot.x + (rect.xMin - pivot.x) * signX;
            float x1 = pivot.x + (rect.xMax - pivot.x) * signX;
            float y0 = pivot.y + (rect.yMin - pivot.y) * signY;
            float y1 = pivot.y + (rect.yMax - pivot.y) * signY;
            return Rect.MinMaxRect(
                Mathf.Min(x0, x1), Mathf.Min(y0, y1),
                Mathf.Max(x0, x1), Mathf.Max(y0, y1));
        }

        void DrawSocketGizmo(Rect cell, Vector2 localPixels, float angleDegrees, string label,
                             Color color, bool selected, bool ghost)
        {
            Vector2 center = SocketToScreen(localPixels, cell);
            float radius = selected ? 9f : 7f;
            Color fill = color;
            fill.a = ghost ? 0.28f : (selected ? 0.88f : 0.7f);
            Color outline = color;
            outline.a = ghost ? 0.55f : 0.98f;

            const int segments = 24;
            var ring = new Vector3[segments + 1];
            for (int i = 0; i < segments; i++)
            {
                float step = Mathf.PI * 2f * i / segments;
                ring[i] = new Vector3(
                    center.x + Mathf.Cos(step) * radius,
                    center.y + Mathf.Sin(step) * radius);
            }
            ring[segments] = ring[0];

            Handles.BeginGUI();
            Handles.color = fill;
            Handles.DrawSolidDisc(center, Vector3.forward, radius);
            Handles.color = outline;
            Handles.DrawAAPolyLine(selected ? 2.6f : 1.6f, ring);
            if (!Mathf.Approximately(angleDegrees, 0f))
            {
                float rad = angleDegrees * Mathf.Deg2Rad;
                Vector2 tip = center + new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad)) * (radius + 12f);
                Vector2 dir = (tip - center).normalized;
                Vector2 ortho = new Vector2(-dir.y, dir.x);
                Handles.DrawAAPolyLine(2.2f, center, tip);
                Handles.DrawAAPolyLine(2.2f,
                    tip,
                    tip - dir * 6f + ortho * 3.5f,
                    tip,
                    tip - dir * 6f - ortho * 3.5f);
            }
            Handles.EndGUI();

            Vector2 labelSize = _socketLabelStyle.CalcSize(new GUIContent(label));
            var labelRect = new Rect(center.x + radius + 4f, center.y - labelSize.y * 0.5f,
                labelSize.x + 2f, labelSize.y);
            EditorGUI.DrawRect(labelRect, new Color(0.05f, 0.06f, 0.08f, ghost ? 0.45f : 0.82f));
            GUI.Label(labelRect, label, _socketLabelStyle);
        }

        Vector2 PivotScreen(Rect cell)
        {
            return new Vector2(
                cell.x + Mathf.Clamp01(_profile.Pivot.x) * cell.width,
                cell.y + (1f - Mathf.Clamp01(_profile.Pivot.y)) * cell.height);
        }

        Vector2 SocketToScreen(Vector2 localPixels, Rect cell)
        {
            return PivotScreen(cell) + SourcePixelsToScreenOffset(localPixels, cell);
        }

        Rect SocketWorldAabb(Vector2 localPixels, Rect cell, string label)
        {
            Vector2 center = SocketToScreen(localPixels, cell);
            const float radius = 12f;
            var bounds = new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f);
            if (!string.IsNullOrEmpty(label) && _socketLabelStyle != null)
            {
                Vector2 labelSize = _socketLabelStyle.CalcSize(new GUIContent(label));
                var labelRect = new Rect(center.x + radius - 3f, center.y - labelSize.y * 0.5f,
                    labelSize.x + 2f, labelSize.y);
                bounds = Rect.MinMaxRect(
                    Mathf.Min(bounds.xMin, labelRect.xMin),
                    Mathf.Min(bounds.yMin, labelRect.yMin),
                    Mathf.Max(bounds.xMax, labelRect.xMax),
                    Mathf.Max(bounds.yMax, labelRect.yMax));
            }
            return bounds;
        }

        Rect SocketWorldAabb(SpriteClipDef clip, string name, int frame, Rect cell)
        {
            if (TryGetSocketTransformLayout(clip, name, frame, cell, out var layout))
            {
                var corners = PivotRotatedRectCorners(layout.Unrotated, layout.Pivot, layout.GuiAngle, false);
                float xMin = corners[0].x;
                float yMin = corners[0].y;
                float xMax = corners[0].x;
                float yMax = corners[0].y;
                for (int i = 1; i < 4; i++)
                {
                    xMin = Mathf.Min(xMin, corners[i].x);
                    yMin = Mathf.Min(yMin, corners[i].y);
                    xMax = Mathf.Max(xMax, corners[i].x);
                    yMax = Mathf.Max(yMax, corners[i].y);
                }
                var box = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
                var pin = SocketWorldAabb(layout.Position, cell, name);
                return Rect.MinMaxRect(
                    Mathf.Min(box.xMin, pin.xMin),
                    Mathf.Min(box.yMin, pin.yMin),
                    Mathf.Max(box.xMax, pin.xMax),
                    Mathf.Max(box.yMax, pin.yMax));
            }

            if (TryGetPreviewSocketPose(clip, name, frame, out var position, out _, out _, out _))
                return SocketWorldAabb(position, cell, name);
            return default;
        }

        Vector2 ScreenToSocketLocal(Vector2 screen, Rect cell)
        {
            return ScreenToSourcePixelDelta(screen - PivotScreen(cell), cell);
        }

        string FindSocketAt(SpriteClipDef clip, int frame, Rect cell, Vector2 point,
            bool includeLocked = false)
        {
            if (clip == null)
                return null;
            const float hitRadius = 14f;
            var names = CachedUniqueSocketNames(clip);
            for (int i = names.Count - 1; i >= 0; i--)
            {
                string name = names[i];
                if (!includeLocked && IsSocketLocked(name))
                    continue;
                if (!TryGetPreviewSocketPose(clip, name, frame, out var position, out _, out _, out _))
                    continue;
                bool inBox = TryGetSocketTransformLayout(clip, name, frame, cell, out var layout) &&
                             SocketTransformContains(layout, point);
                if (inBox || Vector2.Distance(SocketToScreen(position, cell), point) <= hitRadius)
                    return name;
            }
            return null;
        }

        void HandleSocketPlacementInput(int controlId, Rect cell, SpriteClipDef clip, int frame)
        {
            var evt = Event.current;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                CancelSocketPlacement("Socket placement cancelled");
                if (GUIUtility.hotControl == controlId)
                    GUIUtility.hotControl = 0;
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 1 && cell.Contains(evt.mousePosition))
            {
                CancelSocketPlacement("Socket placement cancelled");
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type != EventType.MouseDown || evt.button != 0 || !cell.Contains(evt.mousePosition))
                return;

            _playing = false;
            _selectedFrame = frame;
            _selectedOnionFrame = -1;
            GUIUtility.keyboardControl = controlId;

            string hit = FindSocketAt(clip, frame, cell, evt.mousePosition);
            if (hit != null)
            {
                SelectPreviewSocket(hit, SelectionOp.Replace);
                _socketPlacementArmed = false;
                _status = $"Selected socket {hit}";
                evt.Use();
                Repaint();
                return;
            }

            RecordProfileUndo("Place Sprite Socket");
            clip.Sockets ??= new List<FrameSocketDef>();
            _profile.EnsureSocketCatalog();
            var selectedCatalogItem = string.IsNullOrEmpty(_selectedSocketName)
                ? null
                : _profile.SocketCatalog.Find(_selectedSocketName);
            bool placingExisting = !string.IsNullOrEmpty(_selectedSocketName) &&
                SpriteSocketKeys.IdentityIndex(clip.Sockets, _selectedSocketName) >= 0 &&
                selectedCatalogItem != null &&
                selectedCatalogItem.UsesOwnClock == _socketPlacementIndependent;
            string name = placingExisting
                ? _selectedSocketName
                : NextSocketName(_socketPlacementIndependent);
            Vector2 local = ScreenToSocketLocal(evt.mousePosition, cell);
            var placed = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, name, frame);
            placed.LocalPosition = new Vector2(Mathf.Round(local.x), Mathf.Round(local.y));
            if (!placingExisting)
            {
                placed.LocalAngle = 0f;
                placed.LocalScale = Vector2.one;
            }
            _selectedSocketName = name;
            SelectPreviewSocket(name, SelectionOp.Replace);
            if (_socketPlacementIndependent)
            {
                _profile.EnsureSocketCatalog();
                var item = _profile.SocketCatalog.Ensure(name);
                item.MotionMode = (byte)SpriteSocketClockMode.OwnClock;
                item.PathWrap = 0;
                if (item.Speed <= 0.0001f)
                    item.Speed = 1f;
                CaptureSocketMotionsFromClip(clip, new[] { name }, replaceTiming: true);
            }
            _socketPlacementArmed = false;
            _status = _socketPlacementIndependent
                ? $"Placed Independent Motion socket {name}"
                : $"Placed Frame-Attached socket {name} on frame {frame + 1}";
            SaveDirty();
            evt.Use();
            Repaint();
        }

        string NextSocketName(bool independent)
        {
            string prefix = independent ? "Independent Motion" : "Socket";
            _profile.EnsureSocketCatalog();
            int number = 0;
            while (true)
            {
                string candidate = $"{prefix} {number}";
                bool used = _profile.SocketCatalog.Find(candidate) != null ||
                            _profile.FindSocketMotion(candidate) != null ||
                            SpriteSocketKeys.NameExistsOnAnyClip(_profile.Clips, candidate);
                if (!used)
                    return candidate;
                number++;
            }
        }

        bool HandleSocketManipulationInput(int controlId, Rect cell, SpriteClipDef clip, int frame)
        {
            var evt = Event.current;
            bool ownsDrag = _draggingSocket &&
                            (GUIUtility.hotControl == 0 ||
                             GUIUtility.hotControl == controlId ||
                             (_socketHotControl != 0 && GUIUtility.hotControl == _socketHotControl));

            if (ownsDrag && GUIUtility.hotControl == 0 && _socketHotControl != 0)
                GUIUtility.hotControl = _socketHotControl;

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && _draggingSocket)
            {
                RestoreSocketTransform(clip, frame);
                EndSocketDrag(controlId, save: false);
                evt.Use();
                Repaint();
                return true;
            }

            if (evt.type == EventType.MouseDrag && ownsDrag)
            {
                if (SocketDragPassedThreshold(evt.mousePosition))
                {
                    if (_socketHandleKind == ColliderHandleKind.None ||
                        _socketHandleKind == ColliderHandleKind.Body)
                        ApplySocketBodyMove(clip, frame, evt.mousePosition, cell);
                    else
                        ApplySocketTransform(clip, frame, evt.mousePosition, evt.shift);
                }
                evt.Use();
                Repaint();
                return true;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0 && ownsDrag)
            {
                if (SocketDragPassedThreshold(evt.mousePosition))
                {
                    if (_socketHandleKind == ColliderHandleKind.None ||
                        _socketHandleKind == ColliderHandleKind.Body)
                        ApplySocketBodyMove(clip, frame, evt.mousePosition, cell);
                    else
                        ApplySocketTransform(clip, frame, evt.mousePosition, evt.shift);
                }
                EndSocketDrag(controlId, save: true);
                evt.Use();
                Repaint();
                return true;
            }

            if (TryHandleSocketLockBadgeClick(clip, frame, cell))
                return true;

            if (TryHandleSocketContextClick(clip, frame, cell))
                return true;

            // Preview art and handles often sit outside the character cell, so do not
            // require cell.Contains — hit-test the gizmo / socket instead.
            if (evt.type != EventType.MouseDown || evt.button != 0)
                return false;

            var handle = HitSelectedSocketHandle(cell, clip, frame, evt.mousePosition);
            var op = ReadSelectionOp(evt);
            bool modify = op != SelectionOp.Replace;
            if (!modify && handle != ColliderHandleKind.None && handle != ColliderHandleKind.Body)
            {
                if (_selectedSockets.Count >= 2)
                    BeginSocketGroupTransform(clip, frame, handle, cell, evt.mousePosition, controlId);
                else
                    BeginSocketTransform(clip, frame, handle, cell, evt.mousePosition, controlId);
                evt.Use();
                Repaint();
                return true;
            }

            if (!modify && handle == ColliderHandleKind.Body)
            {
                _playing = false;
                _selectedFrame = frame;
                BeginSocketGroupMove(clip, frame, evt.mousePosition, cell, controlId,
                    wholePath: _selectedSockets.Count >= 2);
                _status = PreviewSelectionStatus();
                evt.Use();
                Repaint();
                return true;
            }

            string hit = FindSocketAt(clip, frame, cell, evt.mousePosition);
            if (hit == null)
                return false;

            _playing = false;
            _selectedFrame = frame;
            bool alreadySelected = IsSocketSelected(hit);
            if (modify)
            {
                SelectPreviewSocket(hit, op);
                evt.Use();
                Repaint();
                return true;
            }

            if (!alreadySelected)
                SelectPreviewSocket(hit, SelectionOp.Replace);
            else
            {
                _selectedSocketName = SpriteSocketKeys.CanonicalName(hit);
                _selectedOnionFrame = -1;
            }

            BeginSocketGroupMove(clip, frame, evt.mousePosition, cell, controlId, wholePath: false);
            _status = PreviewSelectionStatus();
            evt.Use();
            Repaint();
            return true;
        }

        void ApplySocketBodyMove(SpriteClipDef clip, int frame, Vector2 mouse, Rect cell)
        {
            Vector2 sourceDelta = ScreenToSourcePixelDelta(mouse - _socketDragStart, cell);
            if (_socketMoveWholePath)
                _socketGroupCentroidCurrent = _socketGroupCentroidStart + sourceDelta;
            if (sourceDelta.sqrMagnitude <= 0.0001f && !_socketMoveUndoRecorded)
                return;
            if (!_socketMoveUndoRecorded)
            {
                RecordProfileUndo(_socketMoveNames.Count == 1 ? "Move Sprite Socket" : "Move Sprite Sockets");
                _socketMoveUndoRecorded = true;
            }
            for (int i = 0; i < _socketMoveKeys.Count; i++)
            {
                var key = _socketMoveKeys[i];
                if (key == null)
                    continue;
                Vector2 next = _socketMoveStarts[i] + sourceDelta;
                key.LocalPosition = _socketMoveWholePath
                    ? next
                    : new Vector2(Mathf.Round(next.x), Mathf.Round(next.y));
            }
            ApplySocketMotionKeyBodyMove(clip, sourceDelta);
        }

        bool SocketDragPassedThreshold(Vector2 mouse)
        {
            return _socketMoveUndoRecorded ||
                   (mouse - _socketDragStart).sqrMagnitude >= SocketDragThreshold * SocketDragThreshold;
        }

        void ApplySocketMotionKeyBodyMove(SpriteClipDef clip, Vector2 sourceDelta)
        {
            for (int i = 0; i < _socketMoveMotionKeys.Count; i++)
            {
                var key = _socketMoveMotionKeys[i];
                var track = i < _socketMoveMotionTracks.Count ? _socketMoveMotionTracks[i] : null;
                if (key == null || track == null)
                    continue;
                Vector2 clipPos = MotionKeyToClipPixels(clip, track, _socketMoveMotionStarts[i]) + sourceDelta;
                Vector2 reference = ClipPixelsToMotionKey(clip, track, clipPos);
                key.LocalPosition = _socketMoveWholePath
                    ? reference
                    : new Vector2(Mathf.Round(reference.x), Mathf.Round(reference.y));
            }
        }

        void CaptureSocketPathDragKeys(SpriteClipDef clip, string name)
        {
            if (clip?.Sockets != null)
            {
                for (int i = 0; i < clip.Sockets.Count; i++)
                {
                    var key = clip.Sockets[i];
                    if (key == null || !SpriteSocketKeys.NamesEqual(key.Name, name))
                        continue;
                    _socketMoveNames.Add(name);
                    _socketMoveKeys.Add(key);
                    _socketMoveStarts.Add(key.LocalPosition);
                    _socketMoveStartScales.Add(key.LocalScale);
                    _socketMoveStartAngles.Add(key.LocalAngle);
                }
            }

            var track = _profile?.FindSocketMotion(name);
            if (track?.Keys == null)
                return;
            for (int i = 0; i < track.Keys.Count; i++)
            {
                var key = track.Keys[i];
                if (key == null)
                    continue;
                _socketMoveMotionTracks.Add(track);
                _socketMoveMotionKeys.Add(key);
                _socketMoveMotionStarts.Add(key.LocalPosition);
                _socketMoveMotionStartScales.Add(key.LocalScale);
                _socketMoveMotionStartAngles.Add(key.LocalAngle);
            }
        }

        void CaptureSelectedSocketDragKeys(SpriteClipDef clip, int frame, bool wholePath = false)
        {
            _socketMoveWholePath = wholePath;
            _socketMoveNames.Clear();
            _socketMoveKeys.Clear();
            _socketMoveStarts.Clear();
            _socketMoveStartScales.Clear();
            _socketMoveStartAngles.Clear();
            _socketMoveMotionTracks.Clear();
            _socketMoveMotionKeys.Clear();
            _socketMoveMotionStarts.Clear();
            _socketMoveMotionStartScales.Clear();
            _socketMoveMotionStartAngles.Clear();
            if (_selectedSockets.Count == 0 && !string.IsNullOrEmpty(_selectedSocketName))
                _selectedSockets.Add(_selectedSocketName);
            clip.Sockets ??= new List<FrameSocketDef>();
            foreach (string name in _selectedSockets)
            {
                if (string.IsNullOrEmpty(name) || IsSocketLocked(name))
                    continue;
                bool independent = SpriteSocketKeys.UsesOwnClock(_profile?.SocketCatalog, name);
                if (!wholePath && !independent)
                    continue;
                CaptureSocketPathDragKeys(clip, name);
            }
            if (_socketMoveKeys.Count > 0 || _socketMoveMotionKeys.Count > 0)
                return;
            if (_timelineView == TimelineView.Sockets)
            {
                foreach (string name in _selectedSockets)
                {
                    if (IsSocketLocked(name))
                        continue;
                    var track = _profile.FindSocketMotion(name);
                    var item = _profile.SocketCatalog.Find(name);
                    if (track == null || item == null ||
                        !TrySampleIndependentSocketMotion(
                            clip, name, item, out var pose, out var angle, out var scale))
                        continue;
                    var key = new FrameSocketDef
                    {
                        Name = name,
                        FrameIndex = frame,
                        LocalPosition = pose,
                        LocalAngle = angle,
                        LocalScale = scale,
                    };
                    _socketMoveNames.Add(name);
                    _socketMoveKeys.Add(key);
                    _socketMoveStarts.Add(key.LocalPosition);
                    _socketMoveStartScales.Add(key.LocalScale);
                    _socketMoveStartAngles.Add(key.LocalAngle);
                }
                return;
            }
            foreach (string name in _selectedSockets)
            {
                if (IsSocketLocked(name))
                    continue;
                if (!TryGetPreviewSocketPose(clip, name, frame,
                        out var pose, out var angle, out var scale, out bool onFrame))
                    continue;
                var key = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, name, frame);
                if (!onFrame)
                {
                    key.LocalPosition = pose;
                    key.LocalAngle = angle;
                    key.LocalScale = scale;
                }
                _socketMoveNames.Add(name);
                _socketMoveKeys.Add(key);
                _socketMoveStarts.Add(key.LocalPosition);
                _socketMoveStartScales.Add(key.LocalScale);
                _socketMoveStartAngles.Add(key.LocalAngle);
            }
        }

        void BeginSocketGroupMove(SpriteClipDef clip, int frame, Vector2 mouse, Rect cell,
            int controlId, bool wholePath)
        {
            CaptureSelectedSocketDragKeys(clip, frame, wholePath);
            if (_socketMoveKeys.Count == 0 && _socketMoveMotionKeys.Count == 0)
                return;
            Vector2 liveCentroid = default;
            bool hasLiveCentroid = wholePath && TryGetSocketGroupCentroid(clip, frame, out liveCentroid);
            _draggingSocket = true;
            _socketGroupTransform = false;
            _socketHandleKind = ColliderHandleKind.Body;
            _socketTransformName = _selectedSocketName;
            _socketDragStart = mouse;
            _socketMoveUndoRecorded = false;
            _socketHotControl = controlId;
            GUIUtility.hotControl = controlId;
            GUIUtility.keyboardControl = controlId;
            _playing = false;
            if (wholePath)
            {
                _socketGroupCentroidStart = hasLiveCentroid
                    ? liveCentroid
                    : ScreenToSocketLocal(mouse, cell);
                _socketGroupCentroidCurrent = _socketGroupCentroidStart;
            }
        }

        void BeginSocketGroupTransform(SpriteClipDef clip, int frame, ColliderHandleKind kind,
            Rect cell, Vector2 mouse, int controlId)
        {
            if (!TryGetSocketGroupTransformLayout(clip, frame, cell, out var layout) ||
                !TryGetSocketGroupCentroid(clip, frame, out _socketGroupCentroidStart))
                return;

            CaptureSelectedSocketDragKeys(clip, frame, wholePath: true);
            if (_socketMoveKeys.Count == 0 && _socketMoveMotionKeys.Count == 0)
                return;

            _draggingSocket = true;
            _socketGroupTransform = true;
            _socketHandleKind = kind;
            _socketTransformName = _selectedSocketName;
            _socketDragStart = mouse;
            _socketMoveUndoRecorded = false;
            _socketScaleStart = Vector2.one;
            _socketAngleStart = 0f;
            _socketPivotStart = layout.Pivot;
            _socketStartAtan = Mathf.Atan2(mouse.y - _socketPivotStart.y, mouse.x - _socketPivotStart.x);
            Vector2 handle = SocketHandlePosition(layout, kind);
            _socketHandleLocalStart = UnrotateAround(handle, layout.Pivot, layout.GuiAngle) - layout.Pivot;
            _socketHotControl = controlId;
            GUIUtility.hotControl = controlId;
            GUIUtility.keyboardControl = controlId;
            _playing = false;
            _selectedOnionFrame = -1;
        }

        bool TryGetSocketTransformLayout(SpriteClipDef clip, string name, int frame, Rect cell,
            out SocketTransformLayout layout)
        {
            layout = default;
            if (clip?.Sockets == null || string.IsNullOrEmpty(name) || _profile?.Sheet == null)
                return false;
            if (!TryGetPreviewSocketPose(clip, name, frame,
                    out var position, out var angle, out var scale, out _))
                return false;

            Vector2 pin = SocketToScreen(position, cell);
            Rect unrotated;
            // Selection handles / crosshair must sit on the same screen point as the
            // orange socket icon (pin). Preview art may be grip-offset from that pin.
            Vector2 pivot = pin;
            bool usedPreview = false;
            if (_showSocketPreviews)
            {
                _profile.EnsureSocketCatalog();
                var item = _profile.SocketCatalog.Find(name);
                if (item != null && item.HasPreview && item.PreviewEnabled &&
                    TryResolveSocketPreview(item, clip, frame,
                        out var texture, out int columns, out int rows, out _,
                        out _, out _, out _) &&
                    TryBuildSocketPreviewScreen(cell, item, texture, columns, rows, position,
                        angle, scale, out var attachScreen, out var spriteRect,
                        out float signX, out float signY))
                {
                    unrotated = FlipRectAround(spriteRect, attachScreen, signX, signY);
                    pivot = pin;
                    usedPreview = true;
                }
                else
                    unrotated = default;
            }
            else
                unrotated = default;

            if (!usedPreview)
            {
                Vector2 resolved = SpriteSocketKeys.ResolvedScale(scale);
                float baseSize = Mathf.Clamp(cell.width * 0.16f, 28f, 64f);
                float w = Mathf.Max(12f, baseSize * Mathf.Abs(resolved.x));
                float h = Mathf.Max(12f, baseSize * Mathf.Abs(resolved.y));
                unrotated = new Rect(pin.x - w * 0.5f, pin.y - h * 0.5f, w, h);
                pivot = pin;
            }

            layout = new SocketTransformLayout(pivot, unrotated, angle,
                SpriteSocketKeys.ResolvedScale(scale), position);
            return true;
        }

        static bool SocketTransformContains(in SocketTransformLayout layout, Vector2 point)
        {
            Vector2 local = UnrotateAround(point, layout.Pivot, layout.GuiAngle);
            return layout.Unrotated.Contains(local);
        }

        static Vector3[] PivotRotatedRectCorners(Rect rect, Vector2 pivot, float degrees, bool close)
        {
            var local = new[]
            {
                new Vector2(rect.xMin, rect.yMin),
                new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax),
                new Vector2(rect.xMin, rect.yMax),
            };
            var points = new Vector3[close ? 5 : 4];
            for (int i = 0; i < 4; i++)
                points[i] = RotateAround(local[i], pivot, degrees);
            if (close)
                points[4] = points[0];
            return points;
        }

        static Vector2 SocketHandlePosition(in SocketTransformLayout layout, ColliderHandleKind kind)
        {
            Rect rect = layout.Unrotated;
            Vector2 center = rect.center;
            Vector2 local = kind switch
            {
                ColliderHandleKind.CornerTL => new Vector2(rect.xMin, rect.yMin),
                ColliderHandleKind.CornerTR => new Vector2(rect.xMax, rect.yMin),
                ColliderHandleKind.CornerBR => new Vector2(rect.xMax, rect.yMax),
                ColliderHandleKind.CornerBL => new Vector2(rect.xMin, rect.yMax),
                ColliderHandleKind.EdgeT => new Vector2(center.x, rect.yMin),
                ColliderHandleKind.EdgeR => new Vector2(rect.xMax, center.y),
                ColliderHandleKind.EdgeB => new Vector2(center.x, rect.yMax),
                ColliderHandleKind.EdgeL => new Vector2(rect.xMin, center.y),
                ColliderHandleKind.Rotate => new Vector2(center.x, rect.yMin - ColliderRotateHandleDistance),
                _ => layout.Pivot,
            };
            return RotateAround(local, layout.Pivot, layout.GuiAngle);
        }

        static ColliderHandleKind HitSocketTransformHandles(in SocketTransformLayout layout, Vector2 mouse)
        {
            float rotateHit = SocketHandleHit * SocketHandleHit;
            float knobHit = SocketHandleHit * SocketHandleHit;
            for (int i = 0; i < SocketGizmoHandleKinds.Length; i++)
            {
                var kind = SocketGizmoHandleKinds[i];
                float limit = kind == ColliderHandleKind.Rotate ? rotateHit : knobHit;
                if ((mouse - SocketHandlePosition(layout, kind)).sqrMagnitude <= limit)
                    return kind;
            }
            return ColliderHandleKind.None;
        }

        ColliderHandleKind HitSelectedSocketHandle(Rect cell, SpriteClipDef clip, int frame, Vector2 mouse)
        {
            if (_selectedSockets.Count >= 2)
            {
                if (TryGetSocketGroupTransformLayout(clip, frame, cell, out var groupLayout))
                {
                    var kind = HitSocketTransformHandles(groupLayout, mouse);
                    if (kind != ColliderHandleKind.None)
                        return kind;
                }
                if (SocketGroupPivotContains(clip, frame, cell, mouse))
                    return ColliderHandleKind.Body;
                return ColliderHandleKind.None;
            }

            if (string.IsNullOrEmpty(_selectedSocketName) ||
                !TryGetSocketTransformLayout(clip, _selectedSocketName, frame, cell, out var layout))
                return ColliderHandleKind.None;

            var handle = HitSocketTransformHandles(layout, mouse);
            if (handle != ColliderHandleKind.None)
                return handle;
            if (SocketTransformContains(layout, mouse))
                return ColliderHandleKind.Body;
            return ColliderHandleKind.None;
        }

        void DrawSocketTransformGizmo(in SocketTransformLayout layout, bool handles, bool boxMoves = true)
        {
            var outline = PivotRotatedRectCorners(layout.Unrotated, layout.Pivot, layout.GuiAngle, true);
            var fill = PivotRotatedRectCorners(layout.Unrotated, layout.Pivot, layout.GuiAngle, false);
            Handles.BeginGUI();
            Handles.color = handles
                ? new Color(1f, 1f, 1f, 0.08f)
                : new Color(1f, 1f, 1f, 0.04f);
            Handles.DrawAAConvexPolygon(fill);
            Handles.color = handles
                ? Color.white
                : new Color(1f, 1f, 1f, 0.45f);
            Handles.DrawAAPolyLine(handles ? 1.8f : 1.2f, outline);
            if (handles)
            {
                Vector2 top = SocketHandlePosition(layout, ColliderHandleKind.EdgeT);
                Vector2 rotate = SocketHandlePosition(layout, ColliderHandleKind.Rotate);
                Handles.DrawAAPolyLine(1.6f, top, rotate);
                Handles.DrawSolidDisc(rotate, Vector3.forward, 5f);
                Handles.color = AccentColor;
                Handles.DrawWireDisc(rotate, Vector3.forward, 7f);
                Handles.color = Color.white;
                Handles.DrawAAPolyLine(1.2f,
                    layout.Pivot + new Vector2(-5f, 0f),
                    layout.Pivot + new Vector2(5f, 0f));
                Handles.DrawAAPolyLine(1.2f,
                    layout.Pivot + new Vector2(0f, -5f),
                    layout.Pivot + new Vector2(0f, 5f));
            }
            Handles.EndGUI();

            if (!handles)
                return;

            DrawHandleKnob(SocketHandlePosition(layout, ColliderHandleKind.CornerTL), false, 11f);
            DrawHandleKnob(SocketHandlePosition(layout, ColliderHandleKind.CornerTR), false, 11f);
            DrawHandleKnob(SocketHandlePosition(layout, ColliderHandleKind.CornerBR), false, 11f);
            DrawHandleKnob(SocketHandlePosition(layout, ColliderHandleKind.CornerBL), false, 11f);
            DrawHandleKnob(SocketHandlePosition(layout, ColliderHandleKind.EdgeT), true, 9f);
            DrawHandleKnob(SocketHandlePosition(layout, ColliderHandleKind.EdgeR), true, 9f);
            DrawHandleKnob(SocketHandlePosition(layout, ColliderHandleKind.EdgeB), true, 9f);
            DrawHandleKnob(SocketHandlePosition(layout, ColliderHandleKind.EdgeL), true, 9f);

            Vector2 rotatePos = SocketHandlePosition(layout, ColliderHandleKind.Rotate);
            var aabb = Rect.MinMaxRect(
                Mathf.Min(fill[0].x, Mathf.Min(fill[1].x, Mathf.Min(fill[2].x, fill[3].x))),
                Mathf.Min(fill[0].y, Mathf.Min(fill[1].y, Mathf.Min(fill[2].y, fill[3].y))),
                Mathf.Max(fill[0].x, Mathf.Max(fill[1].x, Mathf.Max(fill[2].x, fill[3].x))),
                Mathf.Max(fill[0].y, Mathf.Max(fill[1].y, Mathf.Max(fill[2].y, fill[3].y))));
            EditorGUIUtility.AddCursorRect(HandleCursorRect(rotatePos, SocketHandleHit), MouseCursor.RotateArrow);
            AddSocketScaleCursors(layout);
            if (boxMoves)
                EditorGUIUtility.AddCursorRect(aabb, MouseCursor.MoveArrow);
        }

        void AddSocketScaleCursors(in SocketTransformLayout layout)
        {
            float a = Mathf.Abs(Mathf.Repeat(layout.GuiAngle, 180f));
            bool swapped = a > 45f && a < 135f;
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(SocketHandlePosition(layout, ColliderHandleKind.CornerTL), SocketHandleHit),
                swapped ? MouseCursor.ResizeUpRight : MouseCursor.ResizeUpLeft);
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(SocketHandlePosition(layout, ColliderHandleKind.CornerTR), SocketHandleHit),
                swapped ? MouseCursor.ResizeUpLeft : MouseCursor.ResizeUpRight);
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(SocketHandlePosition(layout, ColliderHandleKind.CornerBR), SocketHandleHit),
                swapped ? MouseCursor.ResizeUpRight : MouseCursor.ResizeUpLeft);
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(SocketHandlePosition(layout, ColliderHandleKind.CornerBL), SocketHandleHit),
                swapped ? MouseCursor.ResizeUpLeft : MouseCursor.ResizeUpRight);
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(SocketHandlePosition(layout, ColliderHandleKind.EdgeT), SocketHandleHit),
                swapped ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(SocketHandlePosition(layout, ColliderHandleKind.EdgeB), SocketHandleHit),
                swapped ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(SocketHandlePosition(layout, ColliderHandleKind.EdgeL), SocketHandleHit),
                swapped ? MouseCursor.ResizeVertical : MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(SocketHandlePosition(layout, ColliderHandleKind.EdgeR), SocketHandleHit),
                swapped ? MouseCursor.ResizeVertical : MouseCursor.ResizeHorizontal);
        }

        void BeginSocketTransform(SpriteClipDef clip, int frame, ColliderHandleKind kind,
            Rect cell, Vector2 mouse, int controlId)
        {
            if (string.IsNullOrEmpty(_selectedSocketName) ||
                !TryGetSocketTransformLayout(clip, _selectedSocketName, frame, cell, out var layout))
                return;

            CaptureSelectedSocketDragKeys(clip, frame);
            if (_socketMoveKeys.Count == 0 && _socketMoveMotionKeys.Count == 0)
                return;

            _draggingSocket = true;
            _socketGroupTransform = false;
            _socketHandleKind = kind;
            _socketTransformName = _selectedSocketName;
            _socketDragStart = mouse;
            _socketMoveUndoRecorded = false;
            _socketScaleStart = layout.Scale;
            _socketAngleStart = layout.Angle;
            _socketPivotStart = kind == ColliderHandleKind.Rotate
                ? SocketToScreen(layout.Position, cell)
                : layout.Pivot;
            _socketStartAtan = Mathf.Atan2(mouse.y - _socketPivotStart.y, mouse.x - _socketPivotStart.x);
            Vector2 handle = SocketHandlePosition(layout, kind);
            _socketHandleLocalStart = UnrotateAround(handle, layout.Pivot, layout.GuiAngle) - layout.Pivot;
            _socketHotControl = controlId;
            GUIUtility.hotControl = controlId;
            GUIUtility.keyboardControl = controlId;
            _playing = false;
            _selectedOnionFrame = -1;
        }

        void ApplySocketTransform(SpriteClipDef clip, int frame, Vector2 mouse, bool snap)
        {
            if (_socketGroupTransform)
            {
                ApplySocketGroupTransform(clip, mouse, snap);
                return;
            }
            if (string.IsNullOrEmpty(_socketTransformName) ||
                (_socketMoveKeys.Count == 0 && _socketMoveMotionKeys.Count == 0))
                return;
            if (!_socketMoveUndoRecorded)
            {
                RecordProfileUndo(_socketHandleKind == ColliderHandleKind.Rotate
                    ? "Rotate Sprite Socket"
                    : "Scale Sprite Socket");
                _socketMoveUndoRecorded = true;
            }

            if (_socketHandleKind == ColliderHandleKind.Rotate)
            {
                float atan = Mathf.Atan2(mouse.y - _socketPivotStart.y, mouse.x - _socketPivotStart.x);
                float guiDelta = (atan - _socketStartAtan) * Mathf.Rad2Deg;
                float angle = _socketAngleStart - guiDelta;
                if (snap)
                    angle = Mathf.Round(angle / 15f) * 15f;
                float delta = angle - _socketAngleStart;
                for (int i = 0; i < _socketMoveKeys.Count; i++)
                {
                    if (_socketMoveKeys[i] == null)
                        continue;
                    _socketMoveKeys[i].LocalAngle =
                        (i < _socketMoveStartAngles.Count ? _socketMoveStartAngles[i] : _socketAngleStart) +
                        delta;
                }
                for (int i = 0; i < _socketMoveMotionKeys.Count; i++)
                {
                    if (_socketMoveMotionKeys[i] == null)
                        continue;
                    _socketMoveMotionKeys[i].LocalAngle =
                        (i < _socketMoveMotionStartAngles.Count
                            ? _socketMoveMotionStartAngles[i]
                            : _socketAngleStart) + delta;
                }
                _status = $"Socket {_socketTransformName}  {angle:0.#}°";
                return;
            }

            ResolveDragScale(mouse, snap, out float sx, out float sy);
            var nextScale = new Vector2(ClampAbsScale(sx), ClampAbsScale(sy));
            for (int i = 0; i < _socketMoveKeys.Count; i++)
            {
                if (_socketMoveKeys[i] == null)
                    continue;
                Vector2 start = SpriteSocketKeys.ResolvedScale(
                    i < _socketMoveStartScales.Count ? _socketMoveStartScales[i] : _socketScaleStart);
                _socketMoveKeys[i].LocalScale = new Vector2(
                    ClampAbsScale(start.x * (sx / Mathf.Max(0.0001f, _socketScaleStart.x))),
                    ClampAbsScale(start.y * (sy / Mathf.Max(0.0001f, _socketScaleStart.y))));
            }
            for (int i = 0; i < _socketMoveMotionKeys.Count; i++)
            {
                if (_socketMoveMotionKeys[i] == null)
                    continue;
                Vector2 start = SpriteSocketKeys.ResolvedScale(
                    i < _socketMoveMotionStartScales.Count
                        ? _socketMoveMotionStartScales[i]
                        : _socketScaleStart);
                _socketMoveMotionKeys[i].LocalScale = new Vector2(
                    ClampAbsScale(start.x * (sx / Mathf.Max(0.0001f, _socketScaleStart.x))),
                    ClampAbsScale(start.y * (sy / Mathf.Max(0.0001f, _socketScaleStart.y))));
            }
            _status = $"Socket {_socketTransformName}  scale {nextScale.x:0.##}, {nextScale.y:0.##}";
        }

        void ApplyIndependentTrackScale(SpriteSocketMotionTrack track, SpriteClipDef clip,
            string name, Vector2 scale)
        {
            if (track?.Keys != null)
            {
                for (int i = 0; i < track.Keys.Count; i++)
                {
                    if (track.Keys[i] != null)
                        track.Keys[i].LocalScale = scale;
                }
            }
            if (clip?.Sockets == null)
                return;
            for (int i = 0; i < clip.Sockets.Count; i++)
            {
                var key = clip.Sockets[i];
                if (key == null || !SpriteSocketKeys.NamesEqual(key.Name, name))
                    continue;
                key.LocalScale = scale;
            }
        }

        void ApplySocketGroupTransform(SpriteClipDef clip, Vector2 mouse, bool snap)
        {
            if (_socketMoveKeys.Count == 0 && _socketMoveMotionKeys.Count == 0)
                return;
            if (!_socketMoveUndoRecorded)
            {
                RecordProfileUndo(_socketHandleKind == ColliderHandleKind.Rotate
                    ? "Rotate Sprite Sockets"
                    : "Scale Sprite Sockets");
                _socketMoveUndoRecorded = true;
            }

            if (_socketHandleKind == ColliderHandleKind.Rotate)
            {
                float atan = Mathf.Atan2(mouse.y - _socketPivotStart.y, mouse.x - _socketPivotStart.x);
                float guiDelta = (atan - _socketStartAtan) * Mathf.Rad2Deg;
                float angle = _socketAngleStart - guiDelta;
                if (snap)
                    angle = Mathf.Round(angle / 15f) * 15f;
                for (int i = 0; i < _socketMoveKeys.Count; i++)
                {
                    var key = _socketMoveKeys[i];
                    if (key == null)
                        continue;
                    key.LocalPosition = RotateAround(
                        _socketMoveStarts[i], _socketGroupCentroidStart, angle);
                    key.LocalAngle = _socketMoveStartAngles[i] + angle;
                }
                ApplySocketMotionKeyGroupRotate(clip, angle);
                _status = $"Selection  {angle:0.#}°";
                return;
            }

            ResolveDragScale(mouse, snap, out float sx, out float sy);
            for (int i = 0; i < _socketMoveKeys.Count; i++)
            {
                var key = _socketMoveKeys[i];
                if (key == null)
                    continue;
                Vector2 delta = _socketMoveStarts[i] - _socketGroupCentroidStart;
                key.LocalPosition = new Vector2(
                    Mathf.Round(_socketGroupCentroidStart.x + delta.x * sx),
                    Mathf.Round(_socketGroupCentroidStart.y + delta.y * sy));
                Vector2 startScale = SpriteSocketKeys.ResolvedScale(
                    i < _socketMoveStartScales.Count ? _socketMoveStartScales[i] : Vector2.one);
                key.LocalScale = new Vector2(
                    ClampAbsScale(startScale.x * sx),
                    ClampAbsScale(startScale.y * sy));
            }
            ApplySocketMotionKeyGroupScale(clip, sx, sy);
            _status = $"Selection scale  {sx:0.##}, {sy:0.##}";
        }

        void ApplySocketMotionKeyGroupRotate(SpriteClipDef clip, float angle)
        {
            for (int i = 0; i < _socketMoveMotionKeys.Count; i++)
            {
                var key = _socketMoveMotionKeys[i];
                var track = i < _socketMoveMotionTracks.Count ? _socketMoveMotionTracks[i] : null;
                if (key == null || track == null)
                    continue;
                Vector2 startClip = MotionKeyToClipPixels(clip, track, _socketMoveMotionStarts[i]);
                key.LocalPosition = ClipPixelsToMotionKey(clip, track,
                    RotateAround(startClip, _socketGroupCentroidStart, angle));
                key.LocalAngle = _socketMoveMotionStartAngles[i] + angle;
            }
        }

        void ApplySocketMotionKeyGroupScale(SpriteClipDef clip, float sx, float sy)
        {
            for (int i = 0; i < _socketMoveMotionKeys.Count; i++)
            {
                var key = _socketMoveMotionKeys[i];
                var track = i < _socketMoveMotionTracks.Count ? _socketMoveMotionTracks[i] : null;
                if (key == null || track == null)
                    continue;
                Vector2 startClip = MotionKeyToClipPixels(clip, track, _socketMoveMotionStarts[i]);
                Vector2 delta = startClip - _socketGroupCentroidStart;
                key.LocalPosition = ClipPixelsToMotionKey(clip, track, new Vector2(
                    Mathf.Round(_socketGroupCentroidStart.x + delta.x * sx),
                    Mathf.Round(_socketGroupCentroidStart.y + delta.y * sy)));
                Vector2 startScale = SpriteSocketKeys.ResolvedScale(
                    i < _socketMoveMotionStartScales.Count ? _socketMoveMotionStartScales[i] : Vector2.one);
                key.LocalScale = new Vector2(
                    ClampAbsScale(startScale.x * sx),
                    ClampAbsScale(startScale.y * sy));
            }
        }

        void ResolveDragScale(Vector2 mouse, bool snap, out float sx, out float sy)
        {
            Vector2 local = UnrotateAround(mouse, _socketPivotStart, -_socketAngleStart) - _socketPivotStart;
            sx = _socketScaleStart.x;
            sy = _socketScaleStart.y;
            bool scaleX = _socketHandleKind is ColliderHandleKind.CornerTL or ColliderHandleKind.CornerTR
                or ColliderHandleKind.CornerBR or ColliderHandleKind.CornerBL
                or ColliderHandleKind.EdgeL or ColliderHandleKind.EdgeR;
            bool scaleY = _socketHandleKind is ColliderHandleKind.CornerTL or ColliderHandleKind.CornerTR
                or ColliderHandleKind.CornerBR or ColliderHandleKind.CornerBL
                or ColliderHandleKind.EdgeT or ColliderHandleKind.EdgeB;

            if (snap && scaleX && scaleY)
            {
                float startDist = _socketHandleLocalStart.magnitude;
                if (startDist > 1f)
                {
                    float r = local.magnitude / startDist;
                    if (Vector2.Dot(local, _socketHandleLocalStart) < 0f)
                        r = -r;
                    sx = _socketScaleStart.x * r;
                    sy = _socketScaleStart.y * r;
                }
                return;
            }

            if (scaleX && Mathf.Abs(_socketHandleLocalStart.x) > 1f)
                sx = _socketScaleStart.x * (local.x / _socketHandleLocalStart.x);
            if (scaleY && Mathf.Abs(_socketHandleLocalStart.y) > 1f)
                sy = _socketScaleStart.y * (local.y / _socketHandleLocalStart.y);
        }

        static float ClampAbsScale(float value)
        {
            float sign = value < 0f ? -1f : 1f;
            float abs = Mathf.Clamp(Mathf.Abs(value), SocketMinAbsScale, SocketMaxAbsScale);
            return sign * abs;
        }

        void RestoreSocketMotionKeys()
        {
            for (int i = 0; i < _socketMoveMotionKeys.Count; i++)
            {
                var key = _socketMoveMotionKeys[i];
                if (key == null)
                    continue;
                key.LocalPosition = _socketMoveMotionStarts[i];
                if (i < _socketMoveMotionStartAngles.Count)
                    key.LocalAngle = _socketMoveMotionStartAngles[i];
                if (i < _socketMoveMotionStartScales.Count)
                    key.LocalScale = _socketMoveMotionStartScales[i];
            }
        }

        void RestoreSocketTransform(SpriteClipDef clip, int frame)
        {
            if (clip?.Sockets == null)
                return;
            if (_socketHandleKind == ColliderHandleKind.Body ||
                _socketHandleKind == ColliderHandleKind.None)
            {
                for (int i = 0; i < _socketMoveKeys.Count; i++)
                {
                    if (_socketMoveKeys[i] == null)
                        continue;
                    _socketMoveKeys[i].LocalPosition = _socketMoveStarts[i];
                }
                RestoreSocketMotionKeys();
                _status = "Socket move cancelled";
                return;
            }

            if (_socketGroupTransform)
            {
                for (int i = 0; i < _socketMoveKeys.Count; i++)
                {
                    if (_socketMoveKeys[i] == null)
                        continue;
                    _socketMoveKeys[i].LocalPosition = _socketMoveStarts[i];
                    if (i < _socketMoveStartAngles.Count)
                        _socketMoveKeys[i].LocalAngle = _socketMoveStartAngles[i];
                    if (i < _socketMoveStartScales.Count)
                        _socketMoveKeys[i].LocalScale = _socketMoveStartScales[i];
                }
                RestoreSocketMotionKeys();
                _status = "Socket transform cancelled";
                return;
            }

            if (string.IsNullOrEmpty(_socketTransformName) ||
                _socketMoveStarts.Count == 0 || _socketMoveKeys.Count == 0 ||
                _socketMoveKeys[0] == null)
                return;
            var restore = _socketMoveKeys[0];
            restore.LocalPosition = _socketMoveStarts[0];
            restore.LocalAngle = _socketAngleStart;
            restore.LocalScale = _socketScaleStart;
            _status = "Socket transform cancelled";
        }

        void EndSocketDrag(int controlId, bool save)
        {
            bool dirty = save && _socketMoveUndoRecorded;
            bool wholePath = _socketMoveWholePath;
            bool wroteMotionPath = _socketMoveMotionKeys.Count > 0;
            bool independent = dirty && _timelineView == TimelineView.Sockets &&
                               !wholePath && !wroteMotionPath;
            if (independent)
            {
                for (int i = 0; i < _socketMoveKeys.Count && i < _socketMoveNames.Count; i++)
                {
                    var track = _profile.FindSocketMotion(_socketMoveNames[i]);
                    var source = _socketMoveKeys[i];
                    if (track == null || source == null)
                        continue;
                    float targetPpu = SpriteSheetProfile.GetPixelsPerUnit(
                        _profile.SheetAt(CurrentClip?.SheetIndex ?? 0));
                    float referencePpu = SpriteSheetProfile.GetPixelsPerUnit(
                        _profile.SheetAt(track.ReferenceSheetIndex));
                    Vector2 referencePosition = source.LocalPosition *
                                                (referencePpu / Mathf.Max(1f, targetPpu));
                    var key = EnsureIndependentMotionKey(
                        track, CurrentIndependentMotionTime(),
                        referencePosition, source.LocalAngle, source.LocalScale);
                    key.LocalPosition = referencePosition;
                    key.LocalAngle = source.LocalAngle;
                    key.LocalScale = source.LocalScale;
                }
            }
            bool syncOrbit = dirty &&
                (wholePath || wroteMotionPath || !independent) &&
                (_socketHandleKind == ColliderHandleKind.Body || _socketGroupTransform);
            int hot = _socketHotControl;
            _draggingSocket = false;
            _socketGroupTransform = false;
            _socketHandleKind = ColliderHandleKind.None;
            _socketTransformName = null;
            _socketHotControl = 0;
            _socketMoveNames.Clear();
            _socketMoveKeys.Clear();
            _socketMoveStarts.Clear();
            _socketMoveStartScales.Clear();
            _socketMoveStartAngles.Clear();
            _socketMoveMotionTracks.Clear();
            _socketMoveMotionKeys.Clear();
            _socketMoveMotionStarts.Clear();
            _socketMoveMotionStartScales.Clear();
            _socketMoveMotionStartAngles.Clear();
            _socketMoveWholePath = false;
            _socketMoveUndoRecorded = false;
            if (GUIUtility.hotControl == controlId || (hot != 0 && GUIUtility.hotControl == hot))
                GUIUtility.hotControl = 0;
            if (syncOrbit)
                SyncOrbitCenterFromSelection(CurrentClip);
            if (dirty)
            {
                if (!independent && !wholePath && !wroteMotionPath)
                    CaptureSocketMotionsFromClip(
                        CurrentClip, OrderedSelectedSocketNames(CurrentClip));
                SaveDirty();
            }
        }

    }
}

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

        void DrawTimeline(Rect rect, int controlId)
        {
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, 68f, 20f), "TIMELINE", _sectionStyle);
            var frameTab = new Rect(rect.x + 78f, rect.y + 6f, 62f, 22f);
            var socketTab = new Rect(frameTab.xMax, frameTab.y, 126f, frameTab.height);
            bool frames = GUI.Toggle(frameTab, _timelineView == TimelineView.Frames,
                new GUIContent("Frames", "Character frames, events, and Frame-Attached sockets."),
                EditorStyles.miniButtonLeft);
            bool sockets = GUI.Toggle(socketTab, _timelineView == TimelineView.Sockets,
                new GUIContent("Independent Motion",
                    "Companions, orbitals, and effects on their own timeline, anchored to the player pivot."),
                EditorStyles.miniButtonRight);
            TimelineView nextView = _timelineView;
            if (frames && _timelineView != TimelineView.Frames)
                nextView = TimelineView.Frames;
            if (sockets && _timelineView != TimelineView.Sockets)
                nextView = TimelineView.Sockets;
            if (nextView != _timelineView)
            {
                if (_timelineDragMode != TimelineDragMode.None)
                    EndTimelineDrag();
                _timelineView = nextView;
                GUI.FocusControl(null);
            }

            var bothRect = new Rect(socketTab.xMax + 8f, frameTab.y, 92f, frameTab.height);
            bool nextBoth = GUI.Toggle(bothRect, _spacePlaysBothClocks,
                new GUIContent("Space: Both",
                    "Space starts or pauses Frames and Independent Motion together. Off = Space only plays the selected tab."),
                EditorStyles.miniButton);
            if (nextBoth != _spacePlaysBothClocks)
            {
                RecordWindowUndo("Toggle Space Plays Both Clocks");
                _spacePlaysBothClocks = nextBoth;
                _status = _spacePlaysBothClocks
                    ? "Space plays Frames and Independent Motion"
                    : "Space plays the selected timeline only";
            }

            var clip = CurrentClip;
            if (_timelineView == TimelineView.Sockets)
            {
                DrawSocketTimeline(rect, clip);
                return;
            }
            if (clip == null)
            {
                if (_timelineDragMode != TimelineDragMode.None)
                    EndTimelineDrag();
                GUI.Label(new Rect(rect.x + 12f, rect.y + 34f, rect.width - 24f, 30f),
                    "Add a clip to build its timeline.", _mutedStyle);
                return;
            }

            BuildTimelineMetrics(clip, out float total, out float pixelsPerSecond,
                out Rect[] cards, out Rect[] thumbnails, out float[] frameTimes,
                out float[] durations, out float[] eventXs);
            int frameCount = cards.Length;
            PruneEventSelection(clip);
            string markerSelection = SelectedEventMarker(clip) is { } selectedMarker
                ? $"   •   marker {EventAuthoredTime(clip, selectedMarker):F3}s selected"
                : string.Empty;
            const float addFrameWidth = 82f;
            const float pickSheetWidth = 118f;
            const float deleteEmptyWidth = 148f;
            const float headerBtnGap = 4f;
            var deleteEmptyRect = new Rect(rect.xMax - deleteEmptyWidth - 8f, rect.y + 7f, deleteEmptyWidth, 20f);
            var addFrameRect = new Rect(deleteEmptyRect.x - addFrameWidth - headerBtnGap, rect.y + 7f, addFrameWidth, 20f);
            var pickSheetRect = new Rect(addFrameRect.x - pickSheetWidth - headerBtnGap, rect.y + 7f, pickSheetWidth, 20f);
            // Editable clip FPS near timeline toolbar (shared with inspector / clip list).
            const float fpsCaptionW = 26f;
            const float fpsFieldW = 44f;
            float headerInfoX = bothRect.xMax + 8f;
            var fpsCaptionRect = new Rect(headerInfoX, rect.y + 7f, fpsCaptionW, 20f);
            GUI.Label(fpsCaptionRect, new GUIContent("FPS", ClipFpsTooltip), _mutedStyle);
            var fpsFieldRect = new Rect(fpsCaptionRect.xMax + 2f, rect.y + 7f, fpsFieldW, 20f);
            DrawClipFpsField(fpsFieldRect, clip);
            headerInfoX = fpsFieldRect.xMax + 8f;
            float headerInfoWidth = pickSheetRect.x - headerBtnGap - headerInfoX;
            if (headerInfoWidth > 24f)
            {
                string summary = $"{clip.Frames.Length} frames   •   {total:F3}s{markerSelection}";
                string details = $"{summary}   •   drag = marquee   •   Alt+drag image = reorder   •   frame edge = duration   •   right-click lane = event";
                GUI.Label(new Rect(headerInfoX, rect.y + 10f, headerInfoWidth, 16f),
                    new GUIContent(headerInfoWidth >= 720f ? details : summary, details),
                    _mutedStyle);
            }
            int emptyFrameCount = CountEmptyFrames(clip);
            if (GUI.Button(pickSheetRect,
                new GUIContent("1×1 from texture",
                    "Open a clickable grid of this sheet. Select one or more cells, then OK to add them as frames."),
                EditorStyles.miniButton))
                OpenSheetCellPicker();
            if (GUI.Button(addFrameRect,
                new GUIContent("Add Frame",
                    "Insert a new frame after the selected one (next sheet column). Same as + Frame After in the inspector."),
                EditorStyles.miniButton))
                InsertFrameAfter(clip);
            using (new EditorGUI.DisabledScope(clip.Frames.Length <= 1 || emptyFrameCount == 0))
            {
                if (GUI.Button(deleteEmptyRect,
                    new GUIContent("Delete empty frames",
                        emptyFrameCount == 0
                            ? "No sheet cells in this clip are empty of opaque pixels."
                            : $"Remove {emptyFrameCount} frame{(emptyFrameCount == 1 ? string.Empty : "s")} whose sheet cell has no opaque pixels."),
                    EditorStyles.miniButton))
                    DeleteEmptyFrames(clip);
            }

            var viewport = new Rect(rect.x + 8f, rect.y + 34f, rect.width - 16f, rect.height - 42f);
            _timelineViewportGui = viewport;
            float contentWidth = Mathf.Max(viewport.width - 16f, total * pixelsPerSecond + 52f);
            _timelineContentWidth = contentWidth;
            var content = new Rect(0f, 0f, contentWidth, 192f);
            Vector2 viewportScreenPosition = GUIUtility.GUIToScreenPoint(viewport.position);
            var viewportScreen = new Rect(viewportScreenPosition, viewport.size);

            var preview = EvaluatePreview(clip, _previewTime);
            float playheadX = SpriteAnimPlayback.PlayheadX(preview.TimelineTime, 48f, pixelsPerSecond);
            _timelineScroll = GUI.BeginScrollView(viewport, _timelineScroll, content);
            HandleTimelineInput(controlId, clip, total, pixelsPerSecond,
                contentWidth, viewport.width, viewportScreen, cards, thumbnails,
                frameTimes, durations, eventXs, playheadX);
            preview = EvaluatePreview(clip, _previewTime);
            playheadX = SpriteAnimPlayback.PlayheadX(preview.TimelineTime, 48f, pixelsPerSecond);
            if (_timelineDragMode is TimelineDragMode.Scrub or TimelineDragMode.Reorder or
                    TimelineDragMode.ResizeFrame or TimelineDragMode.Event or
                    TimelineDragMode.SocketDraw)
                playheadX = Mathf.Max(48f, _timelineDragContentMouse.x);

            DrawRuler(contentWidth, total, pixelsPerSecond);
            EditorGUI.DrawRect(new Rect(0f, TimelineEventLaneY, contentWidth, TimelineEventLaneH),
                new Color(0.08f, 0.095f, 0.12f));
            GUI.Label(new Rect(6f, TimelineEventLaneY + 4f, 48f, 16f), "EVENT", _mutedStyle);
            EditorGUI.DrawRect(new Rect(0f, TimelineDrawLaneY, contentWidth, TimelineDrawLaneH),
                new Color(0.09f, 0.1f, 0.13f));
            GUI.Label(new Rect(2f, TimelineDrawLaneY, 46f, TimelineDrawLaneH),
                "SOCKET\nDRAW", _mutedWrapStyle);
            EditorGUIUtility.AddCursorRect(new Rect(0f, 0f, contentWidth, TimelineEventLaneY), MouseCursor.SlideArrow);
            EditorGUIUtility.AddCursorRect(new Rect(0f, TimelineEventLaneY, contentWidth, TimelineEventLaneH),
                MouseCursor.SlideArrow);
            EditorGUIUtility.AddCursorRect(new Rect(0f, TimelineDrawLaneY, contentWidth, TimelineDrawLaneH),
                MouseCursor.Arrow);
            EditorGUIUtility.AddCursorRect(new Rect(0f, TimelineCardsY, contentWidth, 118f), MouseCursor.Arrow);
            for (int i = 0; i < frameCount; i++)
            {
                EditorGUIUtility.AddCursorRect(cards[i], MouseCursor.MoveArrow);
                EditorGUIUtility.AddCursorRect(FrameResizeHandle(cards[i]), MouseCursor.ResizeHorizontal);
            }

            clip.EnsureEventMarkers();
            for (int i = 0; i < clip.EventMarkers.Count; i++)
            {
                var marker = clip.EventMarkers[i];
                if (marker == null || marker.EventId == 0)
                    continue;
                int frame = marker.FrameIndex;
                if (frame < 0 || frame >= frameCount)
                    continue;
                float markerTime = _timelineDragMode == TimelineDragMode.Event &&
                                   _dragEventMarkerIndex == i
                    ? _dragEventAuthoredTime
                    : frameTimes[frame] + Mathf.Clamp01(marker.NormalizedTime) * durations[frame];
                float markerX = 48f + markerTime * pixelsPerSecond;
                Color markerColor = EventMarkerColor(marker.EventId);
                Color guideColor = markerColor;
                guideColor.a = 0.38f;
                EditorGUI.DrawRect(new Rect(markerX - 0.5f, TimelineEventLaneY, 1f, 145f), guideColor);
                if (EventMarkerIsSelected(clip, i))
                    DrawDiamond(new Vector2(markerX, TimelineEventLaneY + 13f), 9f, Color.white);
                DrawDiamond(new Vector2(markerX, TimelineEventLaneY + 13f), 6f, markerColor);
                GUI.Label(new Rect(markerX + 8f, TimelineEventLaneY + 2f, 76f, 16f), $"{markerTime:F3}s", _mutedStyle);
                EditorGUIUtility.AddCursorRect(
                    new Rect(markerX - 10f, TimelineEventLaneY + 3f, 20f, 18f), MouseCursor.MoveArrow);
            }

            DrawTimelineSocketDrawKeys(clip, frameTimes, pixelsPerSecond);

            for (int i = 0; i < frameCount; i++)
            {
                float duration = durations[i];
                var card = cards[i];
                var thumb = thumbnails[i];
                bool draggedSource = _timelineDragMode == TimelineDragMode.Reorder &&
                                     _reorderMoved && i == _dragFrameIndex;

                bool selected = IsFrameSelected(i);
                Color cardColor = selected
                    ? new Color(0.16f, 0.4f, 0.56f)
                    : PanelAltColor;
                if (draggedSource) cardColor.a = 0.35f;
                EditorGUI.DrawRect(card, cardColor);
                DrawBorder(card, selected ? AccentColor : BorderColor, selected ? 2f : 1f);
                GUI.Label(new Rect(card.x + 6f, card.y + 4f, card.width - 12f, 16f),
                    $"F{i + 1}  •  {duration:F3}s", _mutedStyle);
                var thumbArea = new Rect(card.x + 7f, TimelineCardsY + 23f, card.width - 14f, 62f);
                DrawCheckerboard(thumbArea, 9f);
                DrawClipFrame(clip, i, thumb, 1f);
                bool hovered = ThumbnailContains(thumb, Event.current.mousePosition);
                DrawThumbnailHitShape(thumb, selected, hovered);
                EditorGUIUtility.AddCursorRect(thumb,
                    Event.current.alt ? MouseCursor.MoveArrow : MouseCursor.Arrow);
                ResolveClipSheetCell(clip, i, out int cellCol, out int cellRow, out int cellIndex);
                string cellLabel = FormatSheetCellCompact(cellCol, cellRow, cellIndex);
                int frameEventCount = clip.MarkerCountOnFrame(i);
                string cellTip = FormatSheetCellFull(cellCol, cellRow, cellIndex);
                if (frameEventCount == 1)
                    cellTip += $"   •   {EventName(clip.FirstMarkerOnFrame(i).EventId)}";
                else if (frameEventCount > 1)
                    cellTip += $"   •   {frameEventCount} events";
                GUI.Label(new Rect(card.x + 6f, card.y + 85f, card.width - 12f, 14f),
                    new GUIContent(cellLabel, cellTip),
                    _mutedStyle);
                if (draggedSource)
                    EditorGUI.DrawRect(card, new Color(0.05f, 0.06f, 0.075f, 0.55f));

                var resizeHandle = FrameResizeHandle(card);
                EditorGUI.DrawRect(new Rect(card.xMax - 2f, card.y, 2f, card.height),
                    i == _resizeFrameIndex ? AccentColor : BorderColor);
                EditorGUIUtility.AddCursorRect(resizeHandle, MouseCursor.ResizeHorizontal);
            }

            EditorGUI.DrawRect(new Rect(playheadX, 2f, 2f, 178f), new Color(1f, 0.28f, 0.3f));
            DrawTriangle(new Vector2(playheadX + 1f, 2f), 6f, new Color(1f, 0.28f, 0.3f));

            if (_timelineDragMode == TimelineDragMode.Reorder && _reorderMoved && _dragFrameIndex >= 0)
            {
                float insertionX = DropSlotX(_dropFrameSlot, cards);
                EditorGUI.DrawRect(new Rect(insertionX - 2f, TimelineCardsY, 4f, 112f), AccentColor);
                DrawTriangle(new Vector2(insertionX, TimelineCardsY - 1f), 7f, AccentColor);

                var source = cards[Mathf.Clamp(_dragFrameIndex, 0, cards.Length - 1)];
                var ghost = new Rect(
                    _timelineDragContentMouse.x - source.width * 0.5f,
                    source.y,
                    source.width,
                    source.height);
                EditorGUI.DrawRect(ghost, new Color(0.12f, 0.42f, 0.58f, 0.82f));
                DrawBorder(ghost, AccentColor, 2f);
                GUI.Label(new Rect(ghost.x + 7f, ghost.y + 5f, ghost.width - 14f, 18f),
                    $"Move frame {_dragFrameIndex + 1}", EditorStyles.boldLabel);
                var ghostThumbArea = new Rect(ghost.x + 7f, ghost.y + 25f, ghost.width - 14f, 60f);
                var ghostThumb = TimelineSpriteRect(ghostThumbArea);
                DrawCheckerboard(ghostThumbArea, 9f);
                DrawClipFrame(clip, _dragFrameIndex, ghostThumb, 0.9f);
                GUI.Label(new Rect(ghost.x + 7f, ghost.y + 86f, ghost.width - 14f, 14f),
                    "release to place", _mutedStyle);
            }

            if (_timelineDragMode == TimelineDragMode.Marquee && _timelineMarqueeMoved)
            {
                EditorGUI.DrawRect(_timelineMarqueeRect,
                    new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.12f));
                DrawBorder(_timelineMarqueeRect, AccentColor, 1.5f);
            }
            GUI.EndScrollView();
        }

        void DrawSocketTimeline(Rect rect, SpriteClipDef clip)
        {
            _profile.EnsureSocketCatalog();
            _profile.EnsureSocketMotions();

            float duration = _profile.IndependentMotionDuration;
            const float captureWidth = 164f;
            var captureRect = new Rect(
                rect.xMax - captureWidth - 8f, rect.y + 7f, captureWidth, 20f);
            var playRect = new Rect(rect.x + 374f, rect.y + 7f, 42f, 20f);
            var startRect = new Rect(playRect.xMax + 3f, playRect.y, 32f, 20f);
            if (GUI.Button(playRect, _socketPlaying ? "Pause" : "Play",
                    EditorStyles.miniButtonLeft))
                _socketPlaying = !_socketPlaying;
            if (GUI.Button(startRect, "|<", EditorStyles.miniButtonRight))
            {
                _socketPlaying = false;
                _socketPreviewTime = 0f;
            }
            using (new EditorGUI.DisabledScope(
                       clip == null || _selectedSockets.Count == 0))
            {
                if (GUI.Button(captureRect,
                        new GUIContent("Capture Selected Motion",
                            "Promote selected socket keys into independent tracks. Their position is stored from the player pivot and no longer needs copying to every clip."),
                        EditorStyles.miniButton))
                    CaptureSelectedSocketMotions(clip);
            }

            float statusX = startRect.xMax + 8f;
            float statusWidth = captureRect.x - statusX - 8f;
            if (statusWidth > 24f)
            {
                int trackCount = _profile.SocketMotions.Count;
                string status = statusWidth >= 150f
                    ? $"{_socketPreviewTime:0.###}s / {duration:0.###}s  •  {trackCount} track{Plural(trackCount)}"
                    : $"{_socketPreviewTime:0.##}/{duration:0.##}s  •  {trackCount}t";
                GUI.Label(new Rect(statusX, rect.y + 10f, statusWidth, 16f), status, _mutedStyle);
            }

            var viewport = new Rect(rect.x + 8f, rect.y + 34f, rect.width - 16f, rect.height - 42f);
            const float labelWidth = 190f;
            float pixelsPerSecond = 120f * Mathf.Clamp(_independentTimelineZoom, 0.25f, 8f);
            float trackWidth = Mathf.Max(240f, duration * pixelsPerSecond);
            float contentHeight = Mathf.Max(viewport.height - 16f,
                IndependentTracksY + _profile.SocketMotions.Count * IndependentTrackRowH + 8f);
            var content = new Rect(0f, 0f,
                Mathf.Max(viewport.width - 16f, labelWidth + trackWidth + 28f), contentHeight);
            _socketTimelineScroll = GUI.BeginScrollView(
                viewport, _socketTimelineScroll, content);
            var navigationEvent = Event.current;
            if (navigationEvent.type == EventType.ScrollWheel)
            {
                if (navigationEvent.control || navigationEvent.command)
                {
                    _independentTimelineZoom = Mathf.Clamp(
                        _independentTimelineZoom * (1f - navigationEvent.delta.y * 0.08f),
                        0.25f, 8f);
                }
                else
                {
                    _socketTimelineScroll.x = Mathf.Clamp(
                        _socketTimelineScroll.x + navigationEvent.delta.y * 32f,
                        0f, Mathf.Max(0f, content.width - viewport.width));
                }
                navigationEvent.Use();
                Repaint();
            }
            else if (navigationEvent.type == EventType.MouseDown &&
                     navigationEvent.button == 2)
            {
                _independentTimelinePanning = true;
                _independentTimelinePanStartMouse = navigationEvent.mousePosition;
                _independentTimelinePanStartScroll = _socketTimelineScroll;
                navigationEvent.Use();
            }
            else if (navigationEvent.type == EventType.MouseDrag &&
                     _independentTimelinePanning)
            {
                Vector2 delta = navigationEvent.mousePosition -
                                _independentTimelinePanStartMouse;
                _socketTimelineScroll.x = Mathf.Clamp(
                    _independentTimelinePanStartScroll.x - delta.x,
                    0f, Mathf.Max(0f, content.width - viewport.width));
                navigationEvent.Use();
                Repaint();
            }
            else if (navigationEvent.type == EventType.MouseUp &&
                     navigationEvent.button == 2 && _independentTimelinePanning)
            {
                _independentTimelinePanning = false;
                navigationEvent.Use();
            }

            EditorGUI.DrawRect(new Rect(0f, 0f, content.width, IndependentRulerH),
                new Color(0.08f, 0.095f, 0.12f));
            GUI.Label(new Rect(8f, 5f, labelWidth - 12f, 16f),
                new GUIContent("INDEPENDENT MOTION",
                    "◆ = motion key, ▲ = optional gameplay event trigger, DRAW = Behind/Front"),
                _mutedStyle);
            EditorGUI.DrawRect(new Rect(0f, IndependentDrawLaneY, content.width, IndependentDrawLaneH),
                new Color(0.09f, 0.1f, 0.13f));
            GUI.Label(new Rect(2f, IndependentDrawLaneY, labelWidth - 8f, IndependentDrawLaneH),
                new GUIContent("DRAW",
                    "Independent Motion Behind/Front keys. Frame-Attached draw keys stay on the Frames timeline."),
                _mutedWrapStyle);
            EditorGUIUtility.AddCursorRect(
                new Rect(labelWidth, IndependentDrawLaneY, trackWidth, IndependentDrawLaneH),
                MouseCursor.Arrow);
            float tickStep = IndependentTimelineTickStep(duration);
            int tickCount = Mathf.CeilToInt(duration / tickStep);
            for (int tick = 0; tick <= tickCount; tick++)
            {
                float seconds = Mathf.Min(duration, tick * tickStep);
                float x = labelWidth + seconds / duration * trackWidth;
                bool major = tick == 0 || tick == tickCount || (tick & 1) == 0;
                EditorGUI.DrawRect(new Rect(x, 24f, 1f, contentHeight - 24f),
                    new Color(1f, 1f, 1f, major ? 0.18f : 0.065f));
                if (major)
                    GUI.Label(new Rect(x - 24f, 4f, 52f, 16f),
                        $"{seconds:0.##}s", _mutedStyle);
            }

            int scrubControl = GUIUtility.GetControlID(
                "IndependentMotionScrub".GetHashCode(), FocusType.Passive);
            var rulerRect = new Rect(labelWidth - 12f, 0f, trackWidth + 24f, IndependentRulerH);
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 &&
                rulerRect.Contains(evt.mousePosition))
            {
                GUIUtility.hotControl = scrubControl;
                _socketPlaying = false;
                SetSocketPreviewFromTimelineX(evt.mousePosition.x, labelWidth, trackWidth);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == scrubControl)
            {
                SetSocketPreviewFromTimelineX(evt.mousePosition.x, labelWidth, trackWidth);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == scrubControl)
            {
                SetSocketPreviewFromTimelineX(evt.mousePosition.x, labelWidth, trackWidth);
                GUIUtility.hotControl = 0;
                evt.Use();
            }

            DrawIndependentTimelineDrawKeys(labelWidth, trackWidth);

            if (_profile.SocketMotions.Count == 0)
            {
                GUI.Label(new Rect(12f, IndependentTracksY + 4f, labelWidth - 24f, 58f),
                    "No motion tracks.\nAdd Independent Motion in the inspector.",
                    _mutedWrapStyle);
                float emptyPlayheadX = labelWidth +
                    Mathf.Clamp01(_socketPreviewTime / duration) * trackWidth;
                EditorGUI.DrawRect(new Rect(emptyPlayheadX - 1f, 0f, 2f, contentHeight),
                    new Color(1f, 0.28f, 0.3f));
                DrawTriangle(new Vector2(emptyPlayheadX, 2f), 6f,
                    new Color(1f, 0.28f, 0.3f));
                GUI.EndScrollView();
                return;
            }

            int keyControl = GUIUtility.GetControlID(
                "IndependentMotionKey".GetHashCode(), FocusType.Passive);
            HandleIndependentTimelineDrawInput(keyControl, labelWidth, trackWidth, content.width);
            HandleSocketMotionKeyDrag(keyControl, labelWidth, trackWidth);
            int triggerControl = GUIUtility.GetControlID(
                "IndependentMotionTrigger".GetHashCode(), FocusType.Passive);
            HandleSocketTriggerDrag(triggerControl, labelWidth, trackWidth);
            HandleIndependentMotionKeyMarquee(labelWidth, trackWidth, contentHeight);

            for (int i = 0; i < _profile.SocketMotions.Count; i++)
            {
                var track = _profile.SocketMotions[i];
                float y = IndependentTrackRowY(i);
                var row = new Rect(0f, y, content.width, 38f);
                bool selected = IsSocketSelected(track.SocketName);
                EditorGUI.DrawRect(row, selected
                    ? new Color(0.16f, 0.4f, 0.56f, 0.65f)
                    : new Color(0.11f, 0.125f, 0.15f, 0.9f));
                DrawBorder(row, selected ? AccentColor : BorderColor, selected ? 1.5f : 1f);

                var labelRect = new Rect(6f, y + 2f, labelWidth - 10f, 30f);
                bool renamingTrack = !string.IsNullOrEmpty(_renamingSocketName) &&
                    SpriteSocketKeys.NamesEqual(_renamingSocketName, track.SocketName);
                var trackLabelEvent = Event.current;
                if (renamingTrack)
                {
                    var renameRect = new Rect(labelRect.x, labelRect.y, labelRect.width, 18f);
                    DrawInlineRenameField(renameRect, SocketNameRenameControl,
                        ref _renameSocketNameValue, ref _focusSocketNameRename,
                        EditorStyles.boldLabel);
                    GUI.Label(new Rect(labelRect.x, labelRect.y + 16f, labelRect.width, 14f),
                        $"{track.Keys.Count} keys  •  master clock", _mutedStyle);
                }
                else
                {
                    if (trackLabelEvent.type == EventType.MouseDown &&
                        trackLabelEvent.button == 0 &&
                        labelRect.Contains(trackLabelEvent.mousePosition))
                    {
                        SelectPreviewSocket(track.SocketName, SelectionOp.Replace);
                        if (trackLabelEvent.clickCount >= 2)
                            BeginSocketNameRename(track.SocketName);
                        trackLabelEvent.Use();
                    }
                    GUI.Label(labelRect,
                        new GUIContent(
                            $"{track.SocketName}\n{track.Keys.Count} keys  •  master clock",
                            "Click to select. Double-click or F2 to rename the socket."),
                        _mutedWrapStyle);
                }
                if (labelRect.Contains(trackLabelEvent.mousePosition) &&
                    (trackLabelEvent.type == EventType.ContextClick ||
                     trackLabelEvent.type == EventType.MouseDown &&
                     trackLabelEvent.button == 1))
                {
                    ShowSocketMotionTrackMenu(i);
                    trackLabelEvent.Use();
                }

                for (int tick = 0; tick <= tickCount; tick++)
                {
                    if ((tick & 1) == 0)
                        continue;
                    float seconds = Mathf.Min(duration, tick * tickStep);
                    float x = labelWidth + seconds / duration * trackWidth;
                    float stripeWidth = tickStep / duration * trackWidth;
                    EditorGUI.DrawRect(new Rect(x - stripeWidth, y,
                        stripeWidth, row.height), new Color(1f, 1f, 1f, 0.018f));
                }

                for (int k = 0; k < track.Keys.Count; k++)
                {
                    var key = track.Keys[k];
                    float x = labelWidth + Mathf.Clamp01(key.NormalizedTime) * trackWidth;
                    var hit = new Rect(x - 9f, y + 14f, 18f, 18f);
                    Color keyColor = SpriteSocketKeys.ColorForIndex(i);
                    bool keySelected = _selectedSocketMotionKeys.Contains(key) ||
                                       _selectedSocketMotionTrack == i &&
                                       _selectedSocketMotionKey == k;
                    Vector2 diamond = new(x, y + 23f);
                    DrawIndependentMotionKeyDiamond(key, diamond, keyColor, keySelected);
                    EditorGUIUtility.AddCursorRect(hit, MouseCursor.MoveArrow);
                    if (hit.Contains(Event.current.mousePosition))
                        GUI.Label(hit, new GUIContent(string.Empty,
                            IndependentMotionKeyTooltip(key)));
                    var keyEvent = Event.current;
                    if (hit.Contains(keyEvent.mousePosition) &&
                        keyEvent.type == EventType.MouseDown && keyEvent.button == 0)
                    {
                        bool add = keyEvent.shift;
                        bool toggle = keyEvent.control || keyEvent.command;
                        if (!add && !toggle)
                            SelectPreviewSocket(track.SocketName, SelectionOp.Replace);
                        else
                        {
                            _selectedSockets.Add(
                                SpriteSocketKeys.CanonicalName(track.SocketName));
                            _selectedSocketName = track.SocketName;
                        }
                        if (toggle && _selectedSocketMotionKeys.Contains(key))
                        {
                            _selectedSocketMotionKeys.Remove(key);
                            _selectedSocketMotionTrack = -1;
                            _selectedSocketMotionKey = -1;
                            keyEvent.Use();
                            Repaint();
                            continue;
                        }
                        _selectedSocketMotionKeys.Add(key);
                        _socketPlaying = false;
                        _socketPreviewTime = key.NormalizedTime * duration;
                        _selectedSocketMotionTrack = i;
                        _selectedSocketMotionKey = k;
                        _socketMotionDragKeys.Clear();
                        _socketMotionDragTimes.Clear();
                        foreach (var selectedKey in _selectedSocketMotionKeys)
                        {
                            _socketMotionDragKeys.Add(selectedKey);
                            _socketMotionDragTimes.Add(selectedKey.NormalizedTime);
                        }
                        _socketMotionDragStartX = keyEvent.mousePosition.x;
                        _draggingSocketMotionKey = true;
                        _socketMotionHotControl = keyControl;
                        GUIUtility.hotControl = keyControl;
                        RecordProfileUndo("Move Independent Motion Key");
                        _status = $"{track.SocketName}  •  independent key {k + 1}/{track.Keys.Count}";
                        keyEvent.Use();
                        Repaint();
                    }
                    else if (hit.Contains(keyEvent.mousePosition) &&
                             (keyEvent.type == EventType.ContextClick ||
                              keyEvent.type == EventType.MouseDown && keyEvent.button == 1))
                    {
                        ShowSocketMotionKeyMenu(i, k);
                        keyEvent.Use();
                    }
                }

                for (int t = 0; t < track.Triggers.Count; t++)
                {
                    var trigger = track.Triggers[t];
                    float x = labelWidth + Mathf.Clamp01(trigger.NormalizedTime) * trackWidth;
                    var hit = new Rect(x - 10f, y - 1f, 20f, 18f);
                    bool triggerSelected = _selectedSocketTriggerTrack == i &&
                                           _selectedSocketTriggerIndex == t;
                    if (triggerSelected)
                        DrawTriangle(new Vector2(x, y + 9f), 9f, Color.white);
                    DrawTriangle(new Vector2(x, y + 9f), triggerSelected ? 7f : 5f,
                        EventMarkerColor(trigger.EventId));
                    EditorGUIUtility.AddCursorRect(hit, MouseCursor.Link);
                    var triggerEvent = Event.current;
                    if (hit.Contains(triggerEvent.mousePosition) &&
                        triggerEvent.type == EventType.MouseDown &&
                        triggerEvent.button == 0)
                    {
                        SelectPreviewSocket(track.SocketName, SelectionOp.Replace);
                        _selectedSocketTriggerTrack = i;
                        _selectedSocketTriggerIndex = t;
                        _socketPlaying = false;
                        _socketPreviewTime = trigger.NormalizedTime * duration;
                        _draggingSocketTrigger = true;
                        _socketTriggerHotControl = triggerControl;
                        _socketTriggerUndoRecorded = false;
                        _socketTriggerStartTime = trigger.NormalizedTime;
                        GUIUtility.hotControl = triggerControl;
                        _status = $"{track.SocketName} trigger: {EventName(trigger.EventId)}";
                        triggerEvent.Use();
                        Repaint();
                    }
                    else if (hit.Contains(triggerEvent.mousePosition) &&
                             (triggerEvent.type == EventType.ContextClick ||
                              triggerEvent.type == EventType.MouseDown &&
                              triggerEvent.button == 1))
                    {
                        ShowSocketTriggerMenu(i, t);
                        triggerEvent.Use();
                    }
                }

                var rowEvent = Event.current;
                var triggerLane = new Rect(labelWidth, y, trackWidth, 13f);
                if (triggerLane.Contains(rowEvent.mousePosition) &&
                    (rowEvent.type == EventType.ContextClick ||
                     rowEvent.type == EventType.MouseDown && rowEvent.button == 1))
                {
                    float normalized = Mathf.Clamp01(
                        (rowEvent.mousePosition.x - labelWidth) / trackWidth);
                    ShowAddSocketTriggerMenu(i, normalized);
                    rowEvent.Use();
                }
                else
                {
                    var keyLane = new Rect(labelWidth, y + 13f, trackWidth, row.height - 13f);
                    if (keyLane.Contains(rowEvent.mousePosition) &&
                        (rowEvent.type == EventType.ContextClick ||
                         rowEvent.type == EventType.MouseDown && rowEvent.button == 1))
                    {
                        float normalized = Mathf.Clamp01(
                            (rowEvent.mousePosition.x - labelWidth) / trackWidth);
                        ShowAddSocketMotionKeyMenu(i, normalized);
                        rowEvent.Use();
                    }
                    else if (keyLane.Contains(rowEvent.mousePosition) &&
                             rowEvent.type == EventType.MouseDown && rowEvent.button == 0)
                    {
                        SetSocketPreviewFromTimelineX(
                            rowEvent.mousePosition.x, labelWidth, trackWidth);
                        SelectPreviewSocket(track.SocketName, SelectionOp.Replace);
                        rowEvent.Use();
                    }
                }
            }
            float playheadX = labelWidth +
                              Mathf.Clamp01(_socketPreviewTime / duration) * trackWidth;
            if (_socketMotionMarqueeActive)
            {
                EditorGUI.DrawRect(_socketMotionMarqueeRect,
                    new Color(0.25f, 0.62f, 0.9f, 0.16f));
                DrawBorder(_socketMotionMarqueeRect,
                    new Color(0.35f, 0.72f, 1f, 0.9f), 1f);
            }
            EditorGUI.DrawRect(new Rect(playheadX - 1f, 0f, 2f, contentHeight),
                new Color(1f, 0.28f, 0.3f));
            DrawTriangle(new Vector2(playheadX, 2f), 6f, new Color(1f, 0.28f, 0.3f));
            GUI.EndScrollView();
        }

        void SetSocketPreviewFromTimelineX(float x, float labelWidth, float trackWidth)
        {
            float normalized = Mathf.Clamp01((x - labelWidth) / Mathf.Max(1f, trackWidth));
            _socketPreviewTime = normalized * _profile.IndependentMotionDuration;
            Repaint();
        }

        static float IndependentTimelineTickStep(float duration)
        {
            if (duration <= 1f) return 0.1f;
            if (duration <= 2.5f) return 0.25f;
            if (duration <= 5f) return 0.5f;
            if (duration <= 10f) return 1f;
            return 2f;
        }

        void HandleIndependentMotionKeyMarquee(
            float labelWidth, float trackWidth, float contentHeight)
        {
            var evt = Event.current;
            var keyArea = new Rect(labelWidth, IndependentTracksY, trackWidth,
                contentHeight - IndependentTracksY);
            int controlId = GUIUtility.GetControlID(
                "IndependentMotionKeyMarquee".GetHashCode(), FocusType.Passive, keyArea);
            if (evt.type == EventType.MouseDown && evt.button == 0 &&
                keyArea.Contains(evt.mousePosition) &&
                !IndependentMotionKeyContains(evt.mousePosition, labelWidth, trackWidth) &&
                !IndependentMotionTriggerContains(evt.mousePosition, labelWidth, trackWidth) &&
                !IndependentDrawKeyContains(evt.mousePosition, labelWidth, trackWidth))
            {
                _socketMotionMarqueeActive = true;
                _socketMotionMarqueeMoved = false;
                _socketMotionMarqueeHotControl = controlId;
                GUIUtility.hotControl = controlId;
                _socketMotionMarqueeStart = evt.mousePosition;
                _socketMotionMarqueeRect = new Rect(evt.mousePosition, Vector2.zero);
                _socketMotionMarqueeOp = evt.alt
                    ? SelectionOp.Subtract
                    : evt.control || evt.command
                        ? SelectionOp.Toggle
                        : evt.shift ? SelectionOp.Add : SelectionOp.Replace;
                _socketMotionMarqueeBaseline.Clear();
                foreach (var selectedKey in _selectedSocketMotionKeys)
                    _socketMotionMarqueeBaseline.Add(selectedKey);
                evt.Use();
                return;
            }
            if (!_socketMotionMarqueeActive)
                return;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                RestoreIndependentMotionMarqueeBaseline();
                EndIndependentMotionKeyMarquee();
                evt.Use();
                Repaint();
                return;
            }
            if (evt.type == EventType.MouseDrag &&
                GUIUtility.hotControl == _socketMotionMarqueeHotControl)
            {
                _socketMotionMarqueeMoved |=
                    (evt.mousePosition - _socketMotionMarqueeStart).sqrMagnitude >= 9f;
                _socketMotionMarqueeRect = Rect.MinMaxRect(
                    Mathf.Min(_socketMotionMarqueeStart.x, evt.mousePosition.x),
                    Mathf.Min(_socketMotionMarqueeStart.y, evt.mousePosition.y),
                    Mathf.Max(_socketMotionMarqueeStart.x, evt.mousePosition.x),
                    Mathf.Max(_socketMotionMarqueeStart.y, evt.mousePosition.y));
                evt.Use();
                Repaint();
                return;
            }
            if (evt.type != EventType.MouseUp || evt.button != 0 ||
                GUIUtility.hotControl != _socketMotionMarqueeHotControl)
                return;

            if (!_socketMotionMarqueeMoved)
            {
                if (_socketMotionMarqueeOp == SelectionOp.Replace)
                    _selectedSocketMotionKeys.Clear();
                SetSocketPreviewFromTimelineX(
                    evt.mousePosition.x, labelWidth, trackWidth);
                int clickedTrack = IndependentMotionTrackAtY(evt.mousePosition.y);
                if (clickedTrack >= 0)
                    SelectPreviewSocket(
                        _profile.SocketMotions[clickedTrack].SocketName,
                        _socketMotionMarqueeOp);
                EndIndependentMotionKeyMarquee();
                evt.Use();
                Repaint();
                return;
            }

            RestoreIndependentMotionMarqueeBaseline();
            if (_socketMotionMarqueeOp == SelectionOp.Replace)
            {
                _selectedSocketMotionKeys.Clear();
                _selectedSockets.Clear();
                _selectedSocketName = null;
            }
            for (int i = 0; i < _profile.SocketMotions.Count; i++)
            {
                var track = _profile.SocketMotions[i];
                float y = IndependentTrackRowY(i) + 23f;
                for (int k = 0; k < track.Keys.Count; k++)
                {
                    var key = track.Keys[k];
                    var point = new Vector2(
                        labelWidth + Mathf.Clamp01(key.NormalizedTime) * trackWidth, y);
                    if (!_socketMotionMarqueeRect.Contains(point))
                        continue;
                    if (_socketMotionMarqueeOp == SelectionOp.Subtract)
                        _selectedSocketMotionKeys.Remove(key);
                    else if (_socketMotionMarqueeOp == SelectionOp.Toggle &&
                             _selectedSocketMotionKeys.Contains(key))
                        _selectedSocketMotionKeys.Remove(key);
                    else
                        _selectedSocketMotionKeys.Add(key);
                    _selectedSocketMotionTrack = i;
                    _selectedSocketMotionKey = k;
                    _selectedSockets.Add(SpriteSocketKeys.CanonicalName(track.SocketName));
                    _selectedSocketName = track.SocketName;
                }
            }
            SyncIndependentMotionKeySelection();
            if (_socketMotionMarqueeOp == SelectionOp.Replace ||
                _selectedSocketMotionKeys.Count > 0)
            {
                _selectedSocketTriggerTrack = -1;
                _selectedSocketTriggerIndex = -1;
            }
            EndIndependentMotionKeyMarquee();
            evt.Use();
            Repaint();
        }

        void RestoreIndependentMotionMarqueeBaseline()
        {
            _selectedSocketMotionKeys.Clear();
            foreach (var key in _socketMotionMarqueeBaseline)
                if (key != null)
                    _selectedSocketMotionKeys.Add(key);
        }

        void EndIndependentMotionKeyMarquee()
        {
            if (GUIUtility.hotControl == _socketMotionMarqueeHotControl)
                GUIUtility.hotControl = 0;
            _socketMotionMarqueeActive = false;
            _socketMotionMarqueeMoved = false;
            _socketMotionMarqueeHotControl = 0;
            _socketMotionMarqueeRect = default;
            _socketMotionMarqueeBaseline.Clear();
        }

        static float IndependentTrackRowY(int index)
            => IndependentTracksY + index * IndependentTrackRowH;

        int IndependentMotionTrackAtY(float y)
        {
            int index = Mathf.FloorToInt((y - IndependentTracksY) / IndependentTrackRowH);
            if (index < 0 || index >= _profile.SocketMotions.Count)
                return -1;
            float rowY = IndependentTrackRowY(index);
            return y <= rowY + 38f ? index : -1;
        }

        void SyncIndependentMotionKeySelection()
        {
            _selectedSocketMotionTrack = -1;
            _selectedSocketMotionKey = -1;
            for (int i = 0; i < _profile.SocketMotions.Count; i++)
            {
                var track = _profile.SocketMotions[i];
                for (int k = 0; k < track.Keys.Count; k++)
                {
                    if (!_selectedSocketMotionKeys.Contains(track.Keys[k]))
                        continue;
                    _selectedSocketMotionTrack = i;
                    _selectedSocketMotionKey = k;
                    return;
                }
            }
        }

        bool IndependentMotionKeyContains(
            Vector2 point, float labelWidth, float trackWidth)
        {
            for (int i = 0; i < _profile.SocketMotions.Count; i++)
            {
                var track = _profile.SocketMotions[i];
                float y = IndependentTrackRowY(i) + 23f;
                for (int k = 0; k < track.Keys.Count; k++)
                {
                    float x = labelWidth +
                              Mathf.Clamp01(track.Keys[k].NormalizedTime) * trackWidth;
                    if (new Rect(x - 9f, y - 9f, 18f, 18f).Contains(point))
                        return true;
                }
            }
            return false;
        }

        bool IndependentMotionTriggerContains(
            Vector2 point, float labelWidth, float trackWidth)
        {
            for (int i = 0; i < _profile.SocketMotions.Count; i++)
            {
                var track = _profile.SocketMotions[i];
                float y = IndependentTrackRowY(i);
                for (int t = 0; t < track.Triggers.Count; t++)
                {
                    float x = labelWidth +
                              Mathf.Clamp01(track.Triggers[t].NormalizedTime) * trackWidth;
                    if (new Rect(x - 10f, y - 1f, 20f, 18f).Contains(point))
                        return true;
                }
            }
            return false;
        }

        bool IsIndependentSocketName(string name)
            => SpriteSocketKeys.UsesOwnClock(_profile?.SocketCatalog, name);

        bool IsFrameAttachedDrawKey(FrameSocketDef key)
            => key != null &&
               key.DrawLayer != SpriteSocketKeys.DrawUnset &&
               !IsIndependentSocketName(key.Name);

        void DrawIndependentTimelineDrawKeys(float labelWidth, float trackWidth)
        {
            if (_profile?.SocketMotions == null)
                return;
            float duration = _profile.IndependentMotionDuration;
            float laneY = IndependentDrawLaneY + IndependentDrawLaneH * 0.5f;
            for (int i = 0; i < _profile.SocketMotions.Count; i++)
            {
                var track = _profile.SocketMotions[i];
                if (track?.Keys == null)
                    continue;
                for (int k = 0; k < track.Keys.Count; k++)
                {
                    var key = track.Keys[k];
                    if (key == null || key.DrawLayer == SpriteSocketKeys.DrawUnset)
                        continue;
                    float x = labelWidth + Mathf.Clamp01(key.NormalizedTime) * trackWidth +
                              IndependentDrawStackOffsetX(key.NormalizedTime, track.SocketName);
                    Color color = SocketDrawKeyColor(key.DrawLayer);
                    Color guide = color;
                    guide.a = 0.22f;
                    EditorGUI.DrawRect(
                        new Rect(x - 0.5f, IndependentDrawLaneY, 1f,
                            IndependentTracksY - IndependentDrawLaneY + 8f), guide);
                    bool selected = _selectedSocketMotionKeys.Contains(key) ||
                                    _selectedSocketMotionTrack == i &&
                                    _selectedSocketMotionKey == k;
                    if (selected)
                        DrawDiamond(new Vector2(x, laneY), 8f, Color.white);
                    DrawDiamond(new Vector2(x, laneY), 5.5f, color);
                    if (selected)
                    {
                        string side = key.DrawLayer == SpriteSocketKeys.DrawBehind ? "Behind"
                            : key.DrawLayer == SpriteSocketKeys.DrawFront ? "Front" : "Default";
                        GUI.Label(new Rect(x + 8f, IndependentDrawLaneY + 2f, 120f, 16f),
                            $"{track.SocketName}  {side}", _mutedStyle);
                    }
                    var hit = new Rect(x - 10f, IndependentDrawLaneY, 20f, IndependentDrawLaneH);
                    EditorGUIUtility.AddCursorRect(hit, MouseCursor.MoveArrow);
                    if (hit.Contains(Event.current.mousePosition))
                    {
                        string side = key.DrawLayer == SpriteSocketKeys.DrawBehind ? "Behind"
                            : key.DrawLayer == SpriteSocketKeys.DrawFront ? "Front" : "Default";
                        GUI.Label(hit, new GUIContent(string.Empty,
                            $"{track.SocketName}  {side}  •  {key.NormalizedTime * duration:0.###}s"));
                    }
                }
            }
        }

        float IndependentDrawStackOffsetX(float normalizedTime, string name)
        {
            int index = 0;
            if (_profile?.SocketMotions == null)
                return 0f;
            for (int i = 0; i < _profile.SocketMotions.Count; i++)
            {
                var track = _profile.SocketMotions[i];
                if (track?.Keys == null)
                    continue;
                for (int k = 0; k < track.Keys.Count; k++)
                {
                    var key = track.Keys[k];
                    if (key == null || key.DrawLayer == SpriteSocketKeys.DrawUnset ||
                        Mathf.Abs(key.NormalizedTime - normalizedTime) > 0.0001f)
                        continue;
                    if (SpriteSocketKeys.NamesEqual(track.SocketName, name))
                        return index * 10f;
                    index++;
                }
            }
            return 0f;
        }

        bool TryHitIndependentDrawKey(float labelWidth, float trackWidth, Vector2 point,
            out int trackIndex, out int keyIndex)
        {
            trackIndex = -1;
            keyIndex = -1;
            if (_profile?.SocketMotions == null)
                return false;
            if (point.y < IndependentDrawLaneY ||
                point.y > IndependentDrawLaneY + IndependentDrawLaneH)
                return false;
            float laneY = IndependentDrawLaneY + IndependentDrawLaneH * 0.5f;
            float best = 110f;
            for (int i = _profile.SocketMotions.Count - 1; i >= 0; i--)
            {
                var track = _profile.SocketMotions[i];
                if (track?.Keys == null)
                    continue;
                for (int k = track.Keys.Count - 1; k >= 0; k--)
                {
                    var key = track.Keys[k];
                    if (key == null || key.DrawLayer == SpriteSocketKeys.DrawUnset)
                        continue;
                    float x = labelWidth + Mathf.Clamp01(key.NormalizedTime) * trackWidth +
                              IndependentDrawStackOffsetX(key.NormalizedTime, track.SocketName);
                    float sqr = (point - new Vector2(x, laneY)).sqrMagnitude;
                    if (sqr > best)
                        continue;
                    best = sqr;
                    trackIndex = i;
                    keyIndex = k;
                }
            }
            return trackIndex >= 0;
        }

        bool IndependentDrawKeyContains(Vector2 point, float labelWidth, float trackWidth)
            => TryHitIndependentDrawKey(labelWidth, trackWidth, point, out _, out _);

        void HandleIndependentTimelineDrawInput(
            int keyControl, float labelWidth, float trackWidth, float contentWidth)
        {
            var evt = Event.current;
            var lane = new Rect(0f, IndependentDrawLaneY, contentWidth, IndependentDrawLaneH);
            if (!lane.Contains(evt.mousePosition))
                return;
            bool hit = TryHitIndependentDrawKey(
                labelWidth, trackWidth, evt.mousePosition, out int trackIndex, out int keyIndex);
            if (evt.type == EventType.MouseDown && evt.button == 0 && hit &&
                TryGetSocketMotionKey(trackIndex, keyIndex, out var track, out var key))
            {
                bool add = evt.shift;
                bool toggle = evt.control || evt.command;
                if (!add && !toggle)
                    SelectPreviewSocket(track.SocketName, SelectionOp.Replace);
                else
                {
                    _selectedSockets.Add(SpriteSocketKeys.CanonicalName(track.SocketName));
                    _selectedSocketName = track.SocketName;
                }
                if (toggle && _selectedSocketMotionKeys.Contains(key))
                {
                    _selectedSocketMotionKeys.Remove(key);
                    _selectedSocketMotionTrack = -1;
                    _selectedSocketMotionKey = -1;
                    evt.Use();
                    Repaint();
                    return;
                }
                _selectedSocketMotionKeys.Add(key);
                _socketPlaying = false;
                _socketPreviewTime = key.NormalizedTime * _profile.IndependentMotionDuration;
                _selectedSocketMotionTrack = trackIndex;
                _selectedSocketMotionKey = keyIndex;
                _socketMotionDragKeys.Clear();
                _socketMotionDragTimes.Clear();
                foreach (var selectedKey in _selectedSocketMotionKeys)
                {
                    _socketMotionDragKeys.Add(selectedKey);
                    _socketMotionDragTimes.Add(selectedKey.NormalizedTime);
                }
                _socketMotionDragStartX = evt.mousePosition.x;
                _draggingSocketMotionKey = true;
                _socketMotionHotControl = keyControl;
                GUIUtility.hotControl = keyControl;
                RecordProfileUndo("Move Independent Motion Key");
                _status = key.DrawLayer == SpriteSocketKeys.DrawBehind
                    ? $"{track.SocketName}  Behind"
                    : key.DrawLayer == SpriteSocketKeys.DrawFront
                        ? $"{track.SocketName}  Front"
                        : $"{track.SocketName}  Default draw";
                evt.Use();
                Repaint();
                return;
            }
            if ((evt.type == EventType.ContextClick ||
                 evt.type == EventType.MouseDown && evt.button == 1) &&
                evt.mousePosition.x >= labelWidth)
            {
                float normalized = Mathf.Clamp01(
                    (evt.mousePosition.x - labelWidth) / Mathf.Max(1f, trackWidth));
                if (hit)
                    ShowSocketMotionKeyMenu(trackIndex, keyIndex);
                else
                    ShowIndependentTimelineDrawMenu(normalized);
                evt.Use();
                Repaint();
                return;
            }
            if (evt.type == EventType.MouseDown && evt.button == 0 &&
                evt.mousePosition.x >= labelWidth)
            {
                _socketPlaying = false;
                SetSocketPreviewFromTimelineX(evt.mousePosition.x, labelWidth, trackWidth);
                evt.Use();
                Repaint();
            }
        }

        void ShowIndependentTimelineDrawMenu(float normalizedTime)
        {
            var menu = new GenericMenu();
            var names = IndependentTimelineSocketNames();
            if (names.Count == 0)
            {
                _status = "Add an Independent Motion socket first to place Draw keys";
                return;
            }
            if (!string.IsNullOrEmpty(_selectedSocketName) &&
                names.Exists(n => SpriteSocketKeys.NamesEqual(n, _selectedSocketName)))
            {
                AddIndependentDrawLayerMenuItems(menu,
                    new[] { SpriteSocketKeys.CanonicalName(_selectedSocketName) },
                    normalizedTime, "Draw");
                menu.AddSeparator(string.Empty);
            }
            for (int i = 0; i < names.Count; i++)
            {
                if (!string.IsNullOrEmpty(_selectedSocketName) &&
                    SpriteSocketKeys.NamesEqual(names[i], _selectedSocketName))
                    continue;
                AddIndependentDrawLayerMenuItems(menu, new[] { names[i] }, normalizedTime,
                    names[i]);
            }
            menu.ShowAsContext();
        }

        List<string> IndependentTimelineSocketNames()
        {
            var names = new List<string>();
            if (_profile?.SocketMotions == null)
                return names;
            for (int i = 0; i < _profile.SocketMotions.Count; i++)
            {
                string name = SpriteSocketKeys.CanonicalName(_profile.SocketMotions[i]?.SocketName);
                if (string.IsNullOrEmpty(name) || names.Contains(name))
                    continue;
                names.Add(name);
            }
            return names;
        }

        void HandleSocketMotionKeyDrag(int controlId, float labelWidth,
            float trackWidth)
        {
            if (!_draggingSocketMotionKey || GUIUtility.hotControl != controlId ||
                _selectedSocketMotionTrack < 0 ||
                _selectedSocketMotionTrack >= _profile.SocketMotions.Count)
                return;
            var track = _profile.SocketMotions[_selectedSocketMotionTrack];
            if (_selectedSocketMotionKey < 0 ||
                _selectedSocketMotionKey >= track.Keys.Count)
                return;
            var key = track.Keys[_selectedSocketMotionKey];
            var evt = Event.current;
            if (evt.type == EventType.MouseDrag)
            {
                float delta = (evt.mousePosition.x - _socketMotionDragStartX) /
                              Mathf.Max(1f, trackWidth);
                float minDelta = -1f;
                float maxDelta = 1f;
                for (int i = 0; i < _socketMotionDragTimes.Count; i++)
                {
                    minDelta = Mathf.Max(minDelta, -_socketMotionDragTimes[i]);
                    maxDelta = Mathf.Min(maxDelta, 1f - _socketMotionDragTimes[i]);
                }
                delta = Mathf.Clamp(delta, minDelta, maxDelta);
                for (int i = 0;
                     i < _socketMotionDragKeys.Count && i < _socketMotionDragTimes.Count;
                     i++)
                    _socketMotionDragKeys[i].NormalizedTime =
                        _socketMotionDragTimes[i] + delta;
                _socketPreviewTime = key.NormalizedTime * _profile.IndependentMotionDuration;
                evt.Use();
                Repaint();
            }
            else if (evt.type == EventType.MouseUp)
            {
                track.Normalize(Mathf.Max(1, _profile.Sheets?.Count ?? 0));
                _selectedSocketMotionKey = track.Keys.IndexOf(key);
                _draggingSocketMotionKey = false;
                _socketMotionHotControl = 0;
                GUIUtility.hotControl = 0;
                _socketMotionDragKeys.Clear();
                _socketMotionDragTimes.Clear();
                SaveDirty();
                evt.Use();
            }
        }

        void HandleSocketTriggerDrag(int controlId, float labelWidth,
            float trackWidth)
        {
            if (!_draggingSocketTrigger || GUIUtility.hotControl != controlId ||
                !TryGetSelectedSocketTrigger(
                    _selectedSocketTriggerTrack, _selectedSocketTriggerIndex,
                    out var track, out var trigger))
                return;
            var evt = Event.current;
            if (evt.type == EventType.MouseDrag)
            {
                if (!_socketTriggerUndoRecorded)
                {
                    RecordProfileUndo("Move Independent Trigger");
                    _socketTriggerUndoRecorded = true;
                }
                trigger.NormalizedTime = Mathf.Clamp01(
                    (evt.mousePosition.x - labelWidth) / Mathf.Max(1f, trackWidth));
                _socketPreviewTime = trigger.NormalizedTime *
                                     _profile.IndependentMotionDuration;
                evt.Use();
                Repaint();
            }
            else if (evt.type == EventType.MouseUp)
            {
                track.Normalize(Mathf.Max(1, _profile.Sheets?.Count ?? 0));
                _selectedSocketTriggerIndex = track.Triggers.IndexOf(trigger);
                _draggingSocketTrigger = false;
                _socketTriggerHotControl = 0;
                GUIUtility.hotControl = 0;
                if (_socketTriggerUndoRecorded)
                {
                    SaveDirty();
                    SealUndoGroup();
                }
                _socketTriggerUndoRecorded = false;
                evt.Use();
            }
        }

        void ShowAddSocketMotionKeyMenu(int trackIndex, float normalizedTime)
        {
            float seconds = Mathf.Clamp01(normalizedTime) * _profile.IndependentMotionDuration;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Insert Key Here"), false,
                () =>
                {
                    _socketPreviewTime = seconds;
                    InsertIndependentMotionKey(false, trackIndex);
                });
            menu.AddItem(new GUIContent($"Insert Next Key ({IndependentKeyStepLabel()})"), false,
                () =>
                {
                    _socketPreviewTime = seconds;
                    InsertIndependentMotionKey(true, trackIndex);
                });
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent($"Add Key at {seconds:0.###}s"), false,
                () => AddSocketMotionKey(trackIndex, normalizedTime, null));
            if (_socketMotionClipboard != null)
                menu.AddItem(new GUIContent($"Paste Key at {seconds:0.###}s"), false,
                    () => AddSocketMotionKey(trackIndex, normalizedTime, _socketMotionClipboard));
            else
                menu.AddDisabledItem(new GUIContent("Paste Key"));
            if (_profile?.SocketMotions != null &&
                trackIndex >= 0 && trackIndex < _profile.SocketMotions.Count &&
                !string.IsNullOrEmpty(_profile.SocketMotions[trackIndex]?.SocketName))
            {
                menu.AddSeparator(string.Empty);
                AddIndependentDrawLayerMenuItems(menu,
                    new[] { _profile.SocketMotions[trackIndex].SocketName },
                    Mathf.Clamp01(normalizedTime));
            }
            menu.ShowAsContext();
        }

        void ShowSocketMotionKeyMenu(int trackIndex, int keyIndex)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Insert Key Here"), false,
                () => InsertIndependentMotionKey(false, trackIndex));
            menu.AddItem(new GUIContent($"Insert Next Key ({IndependentKeyStepLabel()})"), false,
                () => InsertIndependentMotionKey(true, trackIndex));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Copy Key"), false, () =>
            {
                if (TryGetSocketMotionKey(trackIndex, keyIndex, out _, out var key))
                    _socketMotionClipboard = CloneSocketMotionKey(key);
            });
            menu.AddItem(new GUIContent("Duplicate +0.1 Seconds"), false, () =>
            {
                if (!TryGetSocketMotionKey(trackIndex, keyIndex, out _, out var key))
                    return;
                float nextTime = Mathf.Min(_profile.IndependentMotionDuration,
                    key.NormalizedTime * _profile.IndependentMotionDuration + 0.1f);
                AddSocketMotionKey(trackIndex,
                    nextTime / _profile.IndependentMotionDuration, key);
            });
            TryGetSocketMotionKey(trackIndex, keyIndex, out _, out var selectedKey);
            if (selectedKey != null)
            {
                menu.AddSeparator(string.Empty);
                foreach (SpriteEaseMode mode in Enum.GetValues(typeof(SpriteEaseMode)))
                {
                    SpriteEaseMode capturedMode = mode;
                    menu.AddItem(new GUIContent(EaseMenuPath(mode)),
                        selectedKey.EaseMode == (byte)mode, () =>
                        {
                            if (!TryGetSocketMotionKey(
                                    trackIndex, keyIndex, out var track, out var key))
                                return;
                            ApplyIndependentMotionField(
                                "Set Independent Motion Easing", key, track,
                                IndependentMotionApplyScope.Selected,
                                ease: capturedMode);
                        });
                }
                foreach (SpriteSocketPathMode mode in
                         Enum.GetValues(typeof(SpriteSocketPathMode)))
                {
                    SpriteSocketPathMode capturedMode = mode;
                    menu.AddItem(new GUIContent($"Position Path/{mode}"),
                        selectedKey.PathMode == (byte)mode, () =>
                        {
                            if (!TryGetSocketMotionKey(
                                    trackIndex, keyIndex, out var track, out var key))
                                return;
                            ApplyIndependentMotionField(
                                "Set Independent Motion Position Path", key, track,
                                IndependentMotionApplyScope.Selected,
                                pathMode: capturedMode);
                        });
                }
                foreach (SpriteSocketRotationMode mode in
                         Enum.GetValues(typeof(SpriteSocketRotationMode)))
                {
                    SpriteSocketRotationMode capturedMode = mode;
                    menu.AddItem(new GUIContent($"Rotation/{mode}"),
                        selectedKey.RotationMode == (byte)mode, () =>
                        {
                            if (!TryGetSocketMotionKey(
                                    trackIndex, keyIndex, out var track, out var key))
                                return;
                            ApplyIndependentMotionField(
                                "Set Independent Motion Rotation", key, track,
                                IndependentMotionApplyScope.Selected,
                                rotationMode: capturedMode);
                        });
                }
                menu.AddItem(new GUIContent("Timing/Allow Overshoot"),
                    selectedKey.AllowOvershoot, () =>
                    {
                        if (!TryGetSocketMotionKey(
                                trackIndex, keyIndex, out var track, out var key))
                            return;
                        ApplyIndependentMotionField(
                            "Toggle Independent Motion Overshoot", key, track,
                            IndependentMotionApplyScope.Selected,
                            allowOvershoot: !key.AllowOvershoot);
                    });
                menu.AddItem(new GUIContent("Position Path/Auto Handles"), false,
                    () => AutoSetIndependentMotionHandles(trackIndex, keyIndex));
            }
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Draw/Behind"),
                selectedKey != null && selectedKey.DrawLayer == SpriteSocketKeys.DrawBehind,
                () => SetSocketMotionKeyDrawLayer(
                    trackIndex, keyIndex, SpriteSocketKeys.DrawBehind));
            menu.AddItem(new GUIContent("Draw/Front"),
                selectedKey != null && selectedKey.DrawLayer == SpriteSocketKeys.DrawFront,
                () => SetSocketMotionKeyDrawLayer(
                    trackIndex, keyIndex, SpriteSocketKeys.DrawFront));
            menu.AddItem(new GUIContent("Draw/Default"),
                selectedKey == null ||
                selectedKey.DrawLayer == SpriteSocketKeys.DrawUnset ||
                selectedKey.DrawLayer == SpriteSocketKeys.DrawCatalog,
                () => SetSocketMotionKeyDrawLayer(
                    trackIndex, keyIndex, SpriteSocketKeys.DrawCatalog));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Delete Key"), false,
                () => DeleteSocketMotionKey(trackIndex, keyIndex));
            menu.ShowAsContext();
        }

        static string EaseMenuPath(SpriteEaseMode mode)
        {
            string value = mode.ToString();
            string[] families =
            {
                "Sine", "Quad", "Cubic", "Quart", "Quint",
                "Expo", "Circ", "Back", "Elastic", "Bounce",
            };
            for (int i = 0; i < families.Length; i++)
            {
                string family = families[i];
                if (value.StartsWith(family, StringComparison.Ordinal))
                    return $"Easing/{family}/{value.Substring(family.Length)}";
            }
            return $"Easing/Basic/{value}";
        }

        void AddSocketMotionKey(int trackIndex, float normalizedTime,
            SpriteSocketMotionKey source)
        {
            if (_profile?.SocketMotions == null || trackIndex < 0 ||
                trackIndex >= _profile.SocketMotions.Count)
                return;
            var track = _profile.SocketMotions[trackIndex];
            float normalized = Mathf.Clamp01(normalizedTime);
            for (int i = 0; i < track.Keys.Count; i++)
            {
                if (Mathf.Abs(track.Keys[i].NormalizedTime - normalized) > 0.0001f)
                    continue;
                _selectedSocketMotionTrack = trackIndex;
                _selectedSocketMotionKey = i;
                _selectedSocketMotionKeys.Clear();
                _selectedSocketMotionKeys.Add(track.Keys[i]);
                _socketPreviewTime = normalized * _profile.IndependentMotionDuration;
                Repaint();
                return;
            }

            RecordProfileUndo("Add Independent Motion Key");
            SpriteSocketMotionKey basis = source;
            if (basis == null && track.Keys.Count > 0)
            {
                basis = track.Keys[0];
                float best = Mathf.Abs(basis.NormalizedTime - normalized);
                for (int i = 1; i < track.Keys.Count; i++)
                {
                    float distance = Mathf.Abs(track.Keys[i].NormalizedTime - normalized);
                    if (distance >= best)
                        continue;
                    best = distance;
                    basis = track.Keys[i];
                }
            }
            var key = basis == null
                ? CreateIndependentMotionKey(track)
                : CloneSocketMotionKey(basis);
            key.NormalizedTime = normalized;
            track.Keys.Add(key);
            track.Normalize(Mathf.Max(1, _profile.Sheets?.Count ?? 0));
            _selectedSocketMotionTrack = trackIndex;
            _selectedSocketMotionKey = track.Keys.IndexOf(key);
            _selectedSocketMotionKeys.Clear();
            _selectedSocketMotionKeys.Add(key);
            _socketPreviewTime = normalized * _profile.IndependentMotionDuration;
            _status = $"Added {track.SocketName} key at {_socketPreviewTime:0.###}s";
            SaveDirty();
            Repaint();
        }

        void DeleteSocketMotionKey(int trackIndex, int keyIndex)
        {
            if (!TryGetSocketMotionKey(trackIndex, keyIndex, out var track, out _))
                return;
            RecordProfileUndo("Delete Independent Motion Key");
            track.Keys.RemoveAt(keyIndex);
            _selectedSocketMotionTrack = -1;
            _selectedSocketMotionKey = -1;
            _selectedSocketMotionKeys.Clear();
            _status = $"Deleted key from {track.SocketName}";
            SaveDirty();
            Repaint();
        }

        void DeleteSelectedSocketMotionKeys()
        {
            if (_selectedSocketMotionKeys.Count == 0)
                return;
            RecordDiscreteUndo("Delete Independent Motion Keys");
            int removed = 0;
            for (int i = 0; i < _profile.SocketMotions.Count; i++)
                removed += _profile.SocketMotions[i].Keys.RemoveAll(
                    key => key != null && _selectedSocketMotionKeys.Contains(key));
            _selectedSocketMotionKeys.Clear();
            _selectedSocketMotionTrack = -1;
            _selectedSocketMotionKey = -1;
            _status = $"Deleted {removed} independent motion key{Plural(removed)}";
            SaveDirty();
            SealUndoGroup();
            Repaint();
        }

        bool TryGetSocketMotionKey(int trackIndex, int keyIndex,
            out SpriteSocketMotionTrack track, out SpriteSocketMotionKey key)
        {
            track = null;
            key = null;
            if (_profile?.SocketMotions == null || trackIndex < 0 ||
                trackIndex >= _profile.SocketMotions.Count)
                return false;
            track = _profile.SocketMotions[trackIndex];
            if (track?.Keys == null || keyIndex < 0 || keyIndex >= track.Keys.Count)
                return false;
            key = track.Keys[keyIndex];
            return key != null;
        }

        void AddIndependentDrawLayerMenuItems(GenericMenu menu, IList<string> names,
            float normalizedTime, string pathPrefix = "Draw")
        {
            if (menu == null || names == null || names.Count == 0)
                return;
            var captured = new List<string>(names.Count);
            for (int i = 0; i < names.Count; i++)
            {
                string name = SpriteSocketKeys.CanonicalName(names[i]);
                if (string.IsNullOrEmpty(name) || captured.Contains(name))
                    continue;
                captured.Add(name);
            }
            if (captured.Count == 0)
                return;
            if (string.IsNullOrEmpty(pathPrefix))
                pathPrefix = captured.Count == 1 ? captured[0] : "Draw";

            byte current = SpriteSocketKeys.DrawUnset;
            if (captured.Count == 1)
            {
                current = SpriteSocketKeys.ResolveIndependentDrawLayer(
                    _profile?.FindSocketMotion(captured[0]), normalizedTime);
            }

            menu.AddItem(new GUIContent($"{pathPrefix}/Behind"),
                current == SpriteSocketKeys.DrawBehind,
                () => ApplyIndependentDrawLayer(captured, normalizedTime,
                    SpriteSocketKeys.DrawBehind));
            menu.AddItem(new GUIContent($"{pathPrefix}/Front"),
                current == SpriteSocketKeys.DrawFront,
                () => ApplyIndependentDrawLayer(captured, normalizedTime,
                    SpriteSocketKeys.DrawFront));
            menu.AddItem(new GUIContent($"{pathPrefix}/Default"),
                current == SpriteSocketKeys.DrawUnset ||
                current == SpriteSocketKeys.DrawCatalog,
                () => ApplyIndependentDrawLayer(captured, normalizedTime,
                    SpriteSocketKeys.DrawCatalog));
        }

        void ApplyIndependentDrawLayer(IList<string> names, float normalizedTime, byte layer)
        {
            if (names == null || names.Count == 0)
                return;
            RecordProfileUndo(layer == SpriteSocketKeys.DrawBehind
                ? "Draw Independent Socket Behind"
                : layer == SpriteSocketKeys.DrawFront
                    ? "Draw Independent Socket In Front"
                    : "Draw Independent Socket Default");
            int changed = 0;
            for (int i = 0; i < names.Count; i++)
            {
                string name = SpriteSocketKeys.CanonicalName(names[i]);
                var track = _profile?.FindSocketMotion(name);
                var item = _profile?.SocketCatalog?.Find(name);
                if (track == null || item == null || !item.UsesOwnClock)
                    continue;
                if (!TryGetPreviewSocketPose(CurrentClip, name, _selectedFrame,
                        out var pose, out var angle, out var scale, out _) &&
                    !TrySampleIndependentSocketMotion(CurrentClip, name, item,
                        out pose, out angle, out scale))
                {
                    pose = Vector2.zero;
                    angle = 0f;
                    scale = Vector2.one;
                }
                EnsureIndependentMotionKey(track, normalizedTime, pose, angle, scale)
                    .DrawLayer = layer;
                changed++;
            }
            if (changed == 0)
                return;
            _status = layer == SpriteSocketKeys.DrawBehind
                ? "Independent Motion  Behind"
                : layer == SpriteSocketKeys.DrawFront
                    ? "Independent Motion  Front"
                    : "Independent Motion  Default draw";
            SaveDirty();
            Repaint();
        }

        void SetSocketMotionKeyDrawLayer(int trackIndex, int keyIndex, byte layer)
        {
            if (!TryGetSocketMotionKey(trackIndex, keyIndex, out var track, out var key))
                return;
            RecordProfileUndo(layer == SpriteSocketKeys.DrawBehind
                ? "Draw Independent Key Behind"
                : layer == SpriteSocketKeys.DrawFront
                    ? "Draw Independent Key In Front"
                    : "Draw Independent Key Default");
            key.DrawLayer = layer;
            _status = layer == SpriteSocketKeys.DrawBehind
                ? $"{track.SocketName} key  Behind"
                : layer == SpriteSocketKeys.DrawFront
                    ? $"{track.SocketName} key  Front"
                    : $"{track.SocketName} key  Default draw";
            SaveDirty();
            Repaint();
        }

        static SpriteSocketMotionKey CreateIndependentMotionKey(
            SpriteSocketMotionTrack track)
            => new()
            {
                EaseMode = track != null && SpriteEase.IsValidMode(track.DefaultEaseMode)
                    ? track.DefaultEaseMode
                    : (byte)SpriteEaseMode.SmoothStep,
                PathMode = track != null &&
                           track.DefaultPathMode <= (byte)SpriteSocketPathMode.None
                    ? track.DefaultPathMode
                    : (byte)SpriteSocketPathMode.SmoothPath,
                RotationMode = track != null &&
                               track.DefaultRotationMode <=
                               (byte)SpriteSocketRotationMode.None
                    ? track.DefaultRotationMode
                    : (byte)SpriteSocketRotationMode.Shortest,
            };

        static SpriteSocketMotionKey CloneSocketMotionKey(SpriteSocketMotionKey source)
            => new()
            {
                NormalizedTime = source.NormalizedTime,
                LocalPosition = source.LocalPosition,
                LocalAngle = source.LocalAngle,
                LocalScale = source.LocalScale,
                DrawLayer = source.DrawLayer,
                EaseMode = source.EaseMode,
                PathMode = source.PathMode,
                InTangent = source.InTangent,
                OutTangent = source.OutTangent,
                ArcBulge = source.ArcBulge,
                ArcClockwise = source.ArcClockwise,
                RotationMode = source.RotationMode,
                RotationTurns = source.RotationTurns,
                FacingAngleOffset = source.FacingAngleOffset,
                AllowOvershoot = source.AllowOvershoot,
                UseCustomEase = source.UseCustomEase,
                CustomEaseCurve = CloneAnimationCurve(source.CustomEaseCurve),
                CustomEaseSamplesA = source.CustomEaseSamplesA,
                CustomEaseSamplesB = source.CustomEaseSamplesB,
            };

        static AnimationCurve CloneAnimationCurve(AnimationCurve source)
        {
            if (source == null)
                return null;
            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
        }

        void ShowSocketMotionTrackMenu(int trackIndex)
        {
            if (_profile?.SocketMotions == null || trackIndex < 0 ||
                trackIndex >= _profile.SocketMotions.Count)
                return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Insert Key Here"), false,
                () => InsertIndependentMotionKey(false, trackIndex));
            menu.AddItem(new GUIContent($"Insert Next Key ({IndependentKeyStepLabel()})"), false,
                () => InsertIndependentMotionKey(true, trackIndex));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Copy Complete Track"), false,
                () => _socketTrackClipboard = CloneSocketMotionTrack(
                    _profile.SocketMotions[trackIndex]));
            if (_socketTrackClipboard != null)
                menu.AddItem(new GUIContent("Paste Complete Track"), false,
                    () => PasteSocketMotionTrack(trackIndex));
            else
                menu.AddDisabledItem(new GUIContent("Paste Complete Track"));
            menu.ShowAsContext();
        }

        void InsertIndependentMotionKey(bool advance, int preferredTrackIndex = -1)
        {
            if (_profile?.SocketMotions == null || CurrentClip == null)
                return;
            int trackIndex = preferredTrackIndex;
            if (trackIndex < 0 && !string.IsNullOrEmpty(_selectedSocketName))
            {
                var selectedTrack = _profile.FindSocketMotion(_selectedSocketName);
                trackIndex = _profile.SocketMotions.IndexOf(selectedTrack);
            }
            if (trackIndex < 0 || trackIndex >= _profile.SocketMotions.Count)
            {
                _status = "Select an Independent Motion socket first";
                Repaint();
                return;
            }
            var track = _profile.SocketMotions[trackIndex];
            var item = _profile.SocketCatalog?.Find(track.SocketName);
            if (item == null || !item.UsesOwnClock ||
                !TryGetPreviewSocketPose(CurrentClip, track.SocketName, _selectedFrame,
                    out var visiblePosition, out float visibleAngle,
                    out var visibleScale, out _))
            {
                _status = $"No visible Independent Motion pose for {track.SocketName}";
                Repaint();
                return;
            }

            float oldDuration = _profile.IndependentMotionDuration;
            float currentTime = Mathf.Clamp(_socketPreviewTime, 0f, oldDuration);
            float targetTime = currentTime +
                               (advance ? ResolvedIndependentKeyStepSeconds() : 0f);
            float currentNormalized = currentTime / oldDuration;
            SpriteSocketMotionKey basis = null;
            if (track.Keys.Count > 0)
            {
                basis = track.Keys[0];
                float nearest = Mathf.Abs(basis.NormalizedTime - currentNormalized);
                for (int i = 1; i < track.Keys.Count; i++)
                {
                    float distance = Mathf.Abs(
                        track.Keys[i].NormalizedTime - currentNormalized);
                    if (distance >= nearest)
                        continue;
                    basis = track.Keys[i];
                    nearest = distance;
                }
            }

            string undoName = advance
                ? "Insert Next Independent Motion Key"
                : "Insert Independent Motion Key";
            RecordDiscreteUndo(undoName);
            float requiredDuration = targetTime > oldDuration + 0.000001f
                ? targetTime + ResolvedIndependentKeyStepSeconds()
                : targetTime;
            _profile.ExtendIndependentMotionDurationPreserveTimes(requiredDuration);
            float duration = _profile.IndependentMotionDuration;
            float normalized = Mathf.Clamp01(targetTime / duration);
            int existingIndex = IndependentKeyIndexAtTime(track, normalized);
            SpriteSocketMotionKey key;
            if (existingIndex >= 0)
            {
                key = track.Keys[existingIndex];
            }
            else
            {
                key = basis == null
                    ? CreateIndependentMotionKey(track)
                    : CloneSocketMotionKey(basis);
                key.NormalizedTime = normalized;
                track.Keys.Add(key);
            }

            float referencePpu = SpriteSheetProfile.GetPixelsPerUnit(
                _profile.SheetAt(track.ReferenceSheetIndex));
            float previewPpu = SpriteSheetProfile.GetPixelsPerUnit(
                _profile.SheetAt(CurrentClip.SheetIndex));
            key.LocalPosition = visiblePosition *
                                (referencePpu / Mathf.Max(1f, previewPpu));
            key.LocalAngle = visibleAngle;
            key.LocalScale = visibleScale;
            track.Normalize(Mathf.Max(1, _profile.Sheets?.Count ?? 0));
            _selectedSocketName = track.SocketName;
            _selectedSockets.Clear();
            _selectedSockets.Add(track.SocketName);
            _selectedSocketMotionTrack = trackIndex;
            _selectedSocketMotionKey = track.Keys.IndexOf(key);
            _selectedSocketMotionKeys.Clear();
            _selectedSocketMotionKeys.Add(key);
            if (advance)
                _socketPreviewTime = targetTime;
            _status = existingIndex >= 0
                ? $"Replaced {track.SocketName} key at {targetTime:0.###}s"
                : $"Inserted {track.SocketName} key at {targetTime:0.###}s";
            SaveDirty();
            SealUndoGroup();
            Repaint();
        }

        static SpriteSocketMotionTrack CloneSocketMotionTrack(
            SpriteSocketMotionTrack source)
        {
            var clone = new SpriteSocketMotionTrack
            {
                SocketName = source.SocketName,
                ReferenceSheetIndex = source.ReferenceSheetIndex,
                Duration = source.Duration,
                Loop = source.Loop,
                DefaultEaseMode = source.DefaultEaseMode,
                DefaultPathMode = source.DefaultPathMode,
                DefaultRotationMode = source.DefaultRotationMode,
                AnchorSpace = source.AnchorSpace,
                Keys = new List<SpriteSocketMotionKey>(),
                Triggers = new List<SpriteSocketTriggerDef>(),
            };
            for (int i = 0; i < source.Keys.Count; i++)
                clone.Keys.Add(CloneSocketMotionKey(source.Keys[i]));
            for (int i = 0; i < source.Triggers.Count; i++)
            {
                var trigger = source.Triggers[i];
                clone.Triggers.Add(new SpriteSocketTriggerDef
                {
                    NormalizedTime = trigger.NormalizedTime,
                    EventId = trigger.EventId,
                });
            }
            return clone;
        }

        void PasteSocketMotionTrack(int trackIndex)
        {
            if (_socketTrackClipboard == null || trackIndex < 0 ||
                trackIndex >= _profile.SocketMotions.Count)
                return;
            RecordProfileUndo("Paste Independent Motion Track");
            var target = _profile.SocketMotions[trackIndex];
            var source = CloneSocketMotionTrack(_socketTrackClipboard);
            target.ReferenceSheetIndex = source.ReferenceSheetIndex;
            target.Keys = source.Keys;
            target.Triggers = source.Triggers;
            target.Normalize(Mathf.Max(1, _profile.Sheets?.Count ?? 0));
            _selectedSocketMotionKeys.Clear();
            _status = $"Pasted complete motion onto {target.SocketName}";
            SaveDirty();
            Repaint();
        }

        float CurrentIndependentMotionTime()
        {
            return Mathf.Clamp01(_socketPreviewTime / _profile.IndependentMotionDuration);
        }

        int IndependentKeyIndexAtTime(SpriteSocketMotionTrack track, float normalizedTime)
        {
            if (track?.Keys == null)
                return -1;
            for (int i = 0; i < track.Keys.Count; i++)
            {
                if (Mathf.Abs(track.Keys[i].NormalizedTime - normalizedTime) <= 0.0001f)
                    return i;
            }
            return -1;
        }

        SpriteSocketMotionKey IndependentKeyAtTime(
            SpriteSocketMotionTrack track, float normalizedTime)
        {
            int index = IndependentKeyIndexAtTime(track, normalizedTime);
            return index >= 0 ? track.Keys[index] : null;
        }

        SpriteSocketMotionKey EnsureIndependentMotionKey(
            SpriteSocketMotionTrack track, float normalizedTime, Vector2 position,
            float angle, Vector2 scale)
        {
            normalizedTime = Mathf.Clamp01(normalizedTime);
            int existing = IndependentKeyIndexAtTime(track, normalizedTime);
            if (existing >= 0)
                return track.Keys[existing];
            var key = CreateIndependentMotionKey(track);
            key.NormalizedTime = normalizedTime;
            key.LocalPosition = position;
            key.LocalAngle = angle;
            key.LocalScale = scale;
            track.Keys.Add(key);
            track.Normalize(Mathf.Max(1, _profile.Sheets?.Count ?? 0));
            _selectedSocketMotionTrack = _profile.SocketMotions.IndexOf(track);
            _selectedSocketMotionKey = track.Keys.IndexOf(key);
            return key;
        }

        void ShowAddSocketTriggerMenu(int trackIndex, float normalizedTime)
        {
            var menu = new GenericMenu();
            if (_profile.Events != null)
            {
                for (int i = 0; i < _profile.Events.Count; i++)
                {
                    var definition = _profile.Events[i];
                    if (definition == null || definition.Id == 0)
                        continue;
                    byte eventId = definition.Id;
                    string eventName = string.IsNullOrWhiteSpace(definition.Name)
                        ? $"Event {eventId}"
                        : definition.Name;
                    menu.AddItem(new GUIContent($"Add Trigger/{eventId}: {eventName}"), false,
                        () => AddSocketTrigger(trackIndex, normalizedTime, eventId));
                }
            }
            menu.AddItem(new GUIContent("Add Trigger/New Event..."), false,
                () => CreateEventAndAddSocketTrigger(trackIndex, normalizedTime));
            menu.ShowAsContext();
        }

        void ShowSocketTriggerMenu(int trackIndex, int triggerIndex)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Delete Trigger"), false,
                () => DeleteSocketTrigger(trackIndex, triggerIndex));
            if (_profile.Events != null && _profile.Events.Count > 0)
            {
                menu.AddSeparator(string.Empty);
                for (int i = 0; i < _profile.Events.Count; i++)
                {
                    var definition = _profile.Events[i];
                    if (definition == null || definition.Id == 0)
                        continue;
                    byte eventId = definition.Id;
                    string eventName = string.IsNullOrWhiteSpace(definition.Name)
                        ? $"Event {eventId}"
                        : definition.Name;
                    menu.AddItem(new GUIContent($"Set Event/{eventId}: {eventName}"), false,
                        () => SetSocketTriggerEvent(trackIndex, triggerIndex, eventId));
                }
            }
            menu.AddItem(new GUIContent("Set Event/New Event..."), false,
                () => CreateEventAndSetSocketTrigger(trackIndex, triggerIndex));
            menu.ShowAsContext();
        }

        void CreateEventAndAddSocketTrigger(int trackIndex, float normalizedTime)
        {
            byte eventId = NextEventId();
            if (eventId == 0)
            {
                _status = "All event IDs are already in use";
                return;
            }
            RecordProfileUndo("Create Independent Motion Event");
            _profile.Events ??= new List<SpriteEventDef>();
            _profile.Events.Add(new SpriteEventDef
            {
                Id = eventId,
                Name = $"Event {eventId}",
                Color = Color.HSVToRGB(Mathf.Repeat(eventId * 0.137f, 1f), 0.72f, 1f),
            });
            AddSocketTrigger(trackIndex, normalizedTime, eventId, false);
        }

        void CreateEventAndSetSocketTrigger(int trackIndex, int triggerIndex)
        {
            byte eventId = NextEventId();
            if (eventId == 0)
            {
                _status = "All event IDs are already in use";
                return;
            }
            RecordProfileUndo("Create Independent Motion Event");
            _profile.Events ??= new List<SpriteEventDef>();
            _profile.Events.Add(new SpriteEventDef
            {
                Id = eventId,
                Name = $"Event {eventId}",
                Color = Color.HSVToRGB(Mathf.Repeat(eventId * 0.137f, 1f), 0.72f, 1f),
            });
            SetSocketTriggerEvent(trackIndex, triggerIndex, eventId, false);
        }

        void AddSocketTrigger(
            int trackIndex, float normalizedTime, byte eventId, bool recordUndo = true)
        {
            if (trackIndex < 0 || trackIndex >= _profile.SocketMotions.Count || eventId == 0)
                return;
            if (recordUndo)
                RecordProfileUndo("Add Independent Socket Trigger");
            var track = _profile.SocketMotions[trackIndex];
            track.Triggers ??= new List<SpriteSocketTriggerDef>();
            track.Triggers.Add(new SpriteSocketTriggerDef
            {
                NormalizedTime = Mathf.Clamp01(normalizedTime),
                EventId = eventId,
            });
            track.Normalize(Mathf.Max(1, _profile.Sheets?.Count ?? 0));
            _selectedSocketTriggerTrack = trackIndex;
            _selectedSocketTriggerIndex = track.Triggers.FindIndex(trigger =>
                trigger.EventId == eventId &&
                Mathf.Approximately(trigger.NormalizedTime, Mathf.Clamp01(normalizedTime)));
            _status = $"Added {EventName(eventId)} trigger to {track.SocketName}";
            SaveDirty();
            Repaint();
        }

        void SetSocketTriggerEvent(
            int trackIndex, int triggerIndex, byte eventId, bool recordUndo = true)
        {
            if (!TryGetSelectedSocketTrigger(trackIndex, triggerIndex, out _, out var trigger))
                return;
            if (recordUndo)
                RecordProfileUndo("Set Independent Socket Trigger Event");
            trigger.EventId = eventId;
            _status = $"Trigger event = {EventName(eventId)}";
            SaveDirty();
            Repaint();
        }

        void DeleteSocketTrigger(int trackIndex, int triggerIndex)
        {
            if (!TryGetSelectedSocketTrigger(trackIndex, triggerIndex, out var track, out _))
                return;
            RecordDiscreteUndo("Delete Independent Socket Trigger");
            track.Triggers.RemoveAt(triggerIndex);
            _selectedSocketTriggerTrack = -1;
            _selectedSocketTriggerIndex = -1;
            _status = $"Deleted trigger from {track.SocketName}";
            SaveDirty();
            SealUndoGroup();
            Repaint();
        }

        bool TryGetSelectedSocketTrigger(int trackIndex, int triggerIndex,
            out SpriteSocketMotionTrack track, out SpriteSocketTriggerDef trigger)
        {
            track = null;
            trigger = null;
            if (_profile?.SocketMotions == null || trackIndex < 0 ||
                trackIndex >= _profile.SocketMotions.Count)
                return false;
            track = _profile.SocketMotions[trackIndex];
            if (track?.Triggers == null || triggerIndex < 0 ||
                triggerIndex >= track.Triggers.Count)
                return false;
            trigger = track.Triggers[triggerIndex];
            return trigger != null;
        }

        void BuildTimelineMetrics(SpriteClipDef clip, out float total, out float pixelsPerSecond,
                                  out Rect[] cards, out Rect[] thumbnails, out float[] frameTimes,
                                  out float[] durations, out float[] eventXs)
        {
            total = TotalAuthoredDuration(clip);
            pixelsPerSecond = TimelinePixelsPerSecond(clip);
            int frameCount = clip.Frames.Length;
            frameTimes = new float[frameCount];
            durations = new float[frameCount];
            cards = new Rect[frameCount];
            thumbnails = new Rect[frameCount];
            eventXs = new float[frameCount];
            float time = 0f;
            for (int i = 0; i < frameCount; i++)
            {
                float duration = clip.FrameDurationScales[i] / clip.FrameRate;
                float x = 48f + time * pixelsPerSecond;
                float width = Mathf.Max(54f, duration * pixelsPerSecond - 5f);
                frameTimes[i] = time;
                durations[i] = duration;
                cards[i] = new Rect(x, TimelineCardsY, width, 102f);
                thumbnails[i] = TimelineSpriteRect(new Rect(x + 7f, TimelineCardsY + 23f, width - 14f, 62f));
                eventXs[i] = 48f + (time + Mathf.Clamp01(clip.EventNormalizedTimes[i]) * duration) *
                    pixelsPerSecond;
                time += duration;
            }
        }

        Vector2 TimelineContentMouse(Vector2 windowMouse)
            => windowMouse - _timelineViewportGui.position + _timelineScroll;

        Rect TimelineViewportScreenRect()
        {
            Vector2 screenPos = GUIUtility.GUIToScreenPoint(_timelineViewportGui.position);
            return new Rect(screenPos, _timelineViewportGui.size);
        }

        void HandleActiveTimelineDrag(int controlId)
        {
            if (_timelineDragMode == TimelineDragMode.None)
                return;

            if (GUIUtility.hotControl != controlId)
                GUIUtility.hotControl = controlId;

            var clip = CurrentClip;
            if (clip == null)
            {
                EndTimelineDrag();
                return;
            }

            var evt = Event.current;
            EventType raw = evt.rawType;
            Vector2 contentMouse = TimelineContentMouse(evt.mousePosition);

            if (raw == EventType.MouseDown && evt.button == 0)
            {
                CommitTimelineDrag(clip, contentMouse);
                return;
            }

            if (raw == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                CancelTimelineDrag();
                evt.Use();
                Repaint();
                return;
            }

            if ((raw == EventType.MouseUp && evt.button == 0) || raw == EventType.MouseLeaveWindow)
            {
                CommitTimelineDrag(clip, contentMouse);
                evt.Use();
                Repaint();
                return;
            }

            if (raw != EventType.MouseDrag)
                return;

            BuildTimelineMetrics(clip, out float total, out float pixelsPerSecond,
                out Rect[] cards, out _, out _, out _, out _);
            float maxScroll = Mathf.Max(0f, _timelineContentWidth - _timelineViewportGui.width);
            Vector2 screenMouse = GUIUtility.GUIToScreenPoint(evt.mousePosition);
            Rect viewportScreen = TimelineViewportScreenRect();
            _timelineDragContentMouse = contentMouse;

            switch (_timelineDragMode)
            {
                case TimelineDragMode.Pan:
                    if (!_panMoved &&
                        Vector2.Distance(screenMouse, _timelineDragStartScreen) >= TimelineDragMoveThreshold)
                        _panMoved = true;
                    if (_panMoved)
                        _timelineScroll.x = Mathf.Clamp(
                            _timelineDragStartScrollX - (screenMouse.x - _timelineDragStartScreen.x),
                            0f, maxScroll);
                    break;

                case TimelineDragMode.Scrub:
                    ScrubTimeline(clip, contentMouse.x, total, pixelsPerSecond);
                    break;

                case TimelineDragMode.Reorder:
                    if (!_reorderMoved &&
                        Vector2.Distance(screenMouse, _timelineDragStartScreen) >= TimelineDragMoveThreshold)
                        _reorderMoved = true;
                    if (_reorderMoved)
                    {
                        _dropFrameSlot = DropSlotAtX(contentMouse.x, cards);
                        AutoScrollTimelineAtScreenEdge(screenMouse, viewportScreen, maxScroll);
                    }
                    break;

                case TimelineDragMode.ResizeFrame:
                    if (_resizeFrameIndex >= 0)
                    {
                        Vector2 delta = screenMouse - _timelineDragStartScreen;
                        if (!_timelineResizeCommitted)
                        {
                            if (delta.magnitude < TimelineDragMoveThreshold)
                                break;
                            RecordDiscreteUndo("Change Frame Duration");
                            _timelineResizeCommitted = true;
                        }

                        float deltaSeconds = delta.x / Mathf.Max(1f, _resizePixelsPerSecond);
                        float duration = Mathf.Max(0.02f, _resizeStartDuration + deltaSeconds);
                        var scales = (float[])clip.FrameDurationScales.Clone();
                        scales[_resizeFrameIndex] = duration * clip.FrameRate;
                        clip.FrameDurationScales = scales;
                        float edgeTime = AuthoredStartTime(clip, _resizeFrameIndex) + duration;
                        float currentTotal = TotalAuthoredDuration(clip);
                        _previewTime = PreviewTimeForAuthoredTime(clip,
                            Mathf.Clamp(edgeTime, 0f, Mathf.Max(0f, currentTotal - 0.0001f)));
                        _status = $"Frame {_resizeFrameIndex + 1} hold: {duration:F3}s";
                    }
                    break;

                case TimelineDragMode.Event:
                    _dragEventAuthoredTime = Mathf.Clamp(
                        (contentMouse.x - 48f) / pixelsPerSecond,
                        0f,
                        Mathf.Max(0f, total - 0.0001f));
                    if (!_eventDragMoved &&
                        Vector2.Distance(screenMouse, _timelineDragStartScreen) >= TimelineDragMoveThreshold)
                        _eventDragMoved = true;
                    _previewTime = PreviewTimeForAuthoredTime(clip, _dragEventAuthoredTime);
                    _selectedFrame = AuthoredFrameAtTime(clip, _dragEventAuthoredTime, out _);
                    if (_eventDragMoved)
                        AutoScrollTimelineAtScreenEdge(screenMouse, viewportScreen, maxScroll);
                    break;

                case TimelineDragMode.SocketDraw:
                    _timelineDragContentMouse = contentMouse;
                    if (!_drawDragMoved &&
                        Vector2.Distance(screenMouse, _timelineDragStartScreen) >= TimelineDragMoveThreshold)
                        _drawDragMoved = true;
                    _previewTime = PreviewTimeForAuthoredTime(clip,
                        Mathf.Clamp((contentMouse.x - 48f) / pixelsPerSecond, 0f, Mathf.Max(0f, total - 0.0001f)));
                    _selectedFrame = AuthoredFrameAtTime(clip,
                        Mathf.Clamp((contentMouse.x - 48f) / pixelsPerSecond, 0f, Mathf.Max(0f, total - 0.0001f)),
                        out _);
                    if (_drawDragMoved)
                        AutoScrollTimelineAtScreenEdge(screenMouse, viewportScreen, maxScroll);
                    break;

                case TimelineDragMode.Marquee:
                    if (!_timelineMarqueeMoved &&
                        Vector2.Distance(screenMouse, _timelineDragStartScreen) >= TimelineDragMoveThreshold)
                        _timelineMarqueeMoved = true;
                    if (_timelineMarqueeMoved)
                    {
                        _timelineMarqueeRect = RectFromPoints(_timelineMarqueeStart, contentMouse);
                        ApplyTimelineMarqueeSelection(cards);
                        AutoScrollTimelineAtScreenEdge(screenMouse, viewportScreen, maxScroll);
                    }
                    break;
            }

            evt.Use();
            Repaint();
        }

        void AutoScrollTimelineAtScreenEdge(Vector2 screenMouse, Rect viewportScreen, float maxScroll)
        {
            const float edge = 34f;
            if (screenMouse.x < viewportScreen.xMin + edge)
                _timelineScroll.x = Mathf.Max(0f, _timelineScroll.x - 13f);
            else if (screenMouse.x > viewportScreen.xMax - edge)
                _timelineScroll.x = Mathf.Min(maxScroll, _timelineScroll.x + 13f);
        }

        void ConvertTimelineResizeToMarquee(Vector2 contentMouse, Rect[] cards, SelectionOp op)
        {
            _resizeFrameIndex = -1;
            _timelineResizeCommitted = false;
            _timelineDragMode = TimelineDragMode.Marquee;
            _timelineMarqueeStart = _timelineDragStartContent;
            _timelineMarqueeMoved = true;
            _timelineMarqueeOp = op;
            _timelineMarqueeBaseline.Clear();
            foreach (int index in _selectedFrames)
                _timelineMarqueeBaseline.Add(index);
            _timelineMarqueeRect = RectFromPoints(_timelineMarqueeStart, contentMouse);
            ApplyTimelineMarqueeSelection(cards);
        }

        void CommitTimelineDrag(SpriteClipDef clip, Vector2 contentMouse)
        {
            if (_timelineDragMode == TimelineDragMode.None)
                return;
            if (clip != null)
            {
                if (_timelineDragMode == TimelineDragMode.Reorder && _reorderMoved)
                    CommitFrameReorder(clip, _dragFrameIndex, _dropFrameSlot);
                else if (_timelineDragMode == TimelineDragMode.ResizeFrame &&
                         _timelineResizeCommitted && _resizeFrameIndex >= 0)
                {
                    SaveDirty();
                    SealUndoGroup();
                }
                else if (_timelineDragMode == TimelineDragMode.Event && _eventDragMoved)
                    CommitEventMove(clip, _dragEventMarkerIndex, _dragEventAuthoredTime);
                else if (_timelineDragMode == TimelineDragMode.SocketDraw && _drawDragMoved)
                    CommitSocketDrawMove(clip, _dragDrawSourceFrame, _dragDrawSocketName, _dragDrawLayer,
                        _timelineDragContentMouse.x);
                else if (_timelineDragMode == TimelineDragMode.Pan && !_panMoved &&
                         _panClickPlacesPlayhead)
                    ScrubTimeline(clip, contentMouse.x, TotalAuthoredDuration(clip),
                        TimelinePixelsPerSecond(clip));
            }
            EndTimelineDrag();
        }

        void CancelTimelineDrag()
        {
            var clip = CurrentClip;
            if (_timelineDragMode == TimelineDragMode.ResizeFrame && clip != null &&
                _resizeFrameIndex >= 0 && _timelineResizeCommitted)
                clip.FrameDurationScales[_resizeFrameIndex] = _resizeStartDuration * clip.FrameRate;
            else if (_timelineDragMode == TimelineDragMode.Marquee)
                RestoreFrameSelectionFromBaseline();
            EndTimelineDrag();
        }

        void HandleTimelineInput(int controlId, SpriteClipDef clip, float total,
                                 float pixelsPerSecond, float contentWidth,
                                 float viewportWidth, Rect viewportScreen,
                                 Rect[] cards, Rect[] thumbnails,
                                 float[] frameTimes, float[] durations,
                                 float[] eventXs, float playheadX)
        {
            var evt = Event.current;
            Vector2 mouse = evt.mousePosition;
            float maxScroll = Mathf.Max(0f, contentWidth - viewportWidth);
            _ = viewportScreen;
            _ = eventXs;

            int markerIndex = EventMarkerAt(clip, frameTimes, durations, pixelsPerSecond, mouse);
            bool hitDraw = TryHitSocketDrawKey(clip, frameTimes, pixelsPerSecond, mouse,
                out int drawFrame, out string drawName);

            if (evt.type == EventType.MouseDown && evt.button == 0 && hitDraw)
            {
                if (_timelineDragMode != TimelineDragMode.None)
                    CommitTimelineDrag(clip, mouse);
                SelectSocketDrawKey(clip, drawFrame, drawName);
                BeginTimelineDrag(controlId, TimelineDragMode.SocketDraw, mouse);
                _dragDrawSourceFrame = drawFrame;
                _dragDrawSocketName = drawName;
                _dragDrawLayer = SpriteSocketKeys.FindOnFrame(
                    clip.Sockets, drawName, drawFrame)?.DrawLayer ?? SpriteSocketKeys.DrawFront;
                _drawDragMoved = false;
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 1 &&
                mouse.y >= TimelineDrawLaneY && mouse.y <= TimelineDrawLaneY + TimelineDrawLaneH &&
                mouse.x >= 48f)
            {
                if (hitDraw)
                    SelectSocketDrawKey(clip, drawFrame, drawName);
                ShowTimelineSocketDrawMenu(clip, mouse.x, total, pixelsPerSecond,
                    hitDraw ? drawName : null, hitDraw ? drawFrame : -1);
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && markerIndex >= 0)
            {
                if (_timelineDragMode != TimelineDragMode.None)
                    CommitTimelineDrag(clip, mouse);
                var marker = clip.EventMarkers[markerIndex];
                float markerTime = EventAuthoredTime(clip, marker);
                SelectEventMarker(clip, markerIndex, markerTime);
                BeginTimelineDrag(controlId, TimelineDragMode.Event, mouse);
                _dragEventMarkerIndex = markerIndex;
                _dragEventSourceFrame = marker.FrameIndex;
                _dragEventId = marker.EventId;
                _dragEventAuthoredTime = markerTime;
                _eventDragMoved = false;
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 1 &&
                mouse.y >= TimelineEventLaneY && mouse.y < TimelineDrawLaneY && mouse.x >= 48f)
            {
                if (markerIndex >= 0)
                    SelectEventMarker(clip, markerIndex, EventAuthoredTime(clip, clip.EventMarkers[markerIndex]));
                ShowTimelineEventMenu(clip, mouse.x, total, pixelsPerSecond, markerIndex);
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 1 &&
                mouse.y > TimelineCardsY - 4f)
            {
                int card = FrameAt(cards, thumbnails, mouse);
                if (card >= 0)
                {
                    if (!IsFrameSelected(card))
                        SelectOnlyFrame(card);
                    else
                        _selectedFrame = card;
                    _previewTime = PreviewTimeForAuthoredTime(clip, frameTimes[card]);
                    ShowTimelineFrameMenu(clip);
                    evt.Use();
                    Repaint();
                    return;
                }
            }

            if (evt.type == EventType.ScrollWheel)
            {
                if (evt.control || evt.command)
                {
                    _frameTimelineZoom = Mathf.Clamp(
                        _frameTimelineZoom * (1f - evt.delta.y * 0.08f), 0.25f, 8f);
                }
                else
                {
                    _timelineScroll.x = Mathf.Clamp(
                        _timelineScroll.x + evt.delta.y * 32f, 0f, maxScroll);
                }
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDown && (evt.button == 0 || evt.button == 2))
            {
                if (_timelineDragMode != TimelineDragMode.None)
                    CommitTimelineDrag(clip, mouse);

                ReleaseShortcutKeyboardFocus();
                if (evt.button == 0 && mouse.y >= TimelineEventLaneY && mouse.y < TimelineCardsY)
                {
                    _selectedEventFrame = -1;
            _selectedEventIndex = -1;
                    if (mouse.y >= TimelineDrawLaneY)
                    {
                        _selectedSocketDrawFrame = -1;
                        _selectedSocketDrawName = null;
                    }
                    ClearColliderSelection();
                }
                bool onPlayhead = new Rect(playheadX - 7f, 0f, 14f, 30f).Contains(mouse);
                if (evt.button == 0 && onPlayhead)
                {
                    BeginTimelineDrag(controlId, TimelineDragMode.Scrub, mouse);
                    ScrubTimeline(clip, mouse.x, total, pixelsPerSecond);
                    evt.Use();
                    return;
                }

                if (evt.button == 0 && mouse.y >= TimelineEventLaneY && mouse.y < TimelineCardsY)
                {
                    BeginTimelineDrag(controlId, TimelineDragMode.Scrub, mouse);
                    ScrubTimeline(clip, mouse.x, total, pixelsPerSecond);
                    evt.Use();
                    Repaint();
                    return;
                }

                if (evt.button == 0 && mouse.y > TimelineCardsY - 4f)
                {
                    int card = FrameAt(cards, thumbnails, mouse);
                    if (card >= 0)
                    {
                        bool onThumb = thumbnails[card].Contains(mouse);
                        bool onEdge = FrameResizeHandle(cards[card]).Contains(mouse);
                        if (onEdge && !onThumb)
                        {
                            BeginTimelineDrag(controlId, TimelineDragMode.ResizeFrame, mouse);
                            _resizeFrameIndex = card;
                            _resizeStartDuration = durations[card];
                            _resizePixelsPerSecond = pixelsPerSecond;
                            _timelineResizeCommitted = false;
                            SelectOnlyFrame(card);
                            _previewTime = PreviewTimeForAuthoredTime(clip, frameTimes[card]);
                            ClearColliderSelection();
                            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
                            _selectedSocketDrawFrame = -1;
                            evt.Use();
                            Repaint();
                            return;
                        }

                        var op = ReadSelectionOp(evt, orderedList: true);
                        bool preserveGroupForDrag = op == SelectionOp.Replace &&
                                                    _selectedFrames.Count > 1 &&
                                                    _selectedFrames.Contains(card);
                        if (preserveGroupForDrag)
                            _selectedFrame = card;
                        else
                            ApplyFrameModifierClick(card, op);
                        _previewTime = PreviewTimeForAuthoredTime(clip, frameTimes[card]);
                        ClearColliderSelection();
                        _selectedEventFrame = -1;
            _selectedEventIndex = -1;
                        _selectedSocketDrawFrame = -1;
                        if (op == SelectionOp.Replace)
                        {
                            BeginTimelineDrag(controlId, TimelineDragMode.Reorder, mouse);
                            _dragFrameIndex = card;
                            _dropFrameSlot = card;
                            _reorderMoved = false;
                        }
                        evt.Use();
                        Repaint();
                        return;
                    }

                    BeginTimelineMarquee(controlId, mouse, ReadSelectionOp(evt));
                    ClearColliderSelection();
                    _selectedEventFrame = -1;
            _selectedEventIndex = -1;
                    _selectedSocketDrawFrame = -1;
                    evt.Use();
                    Repaint();
                    return;
                }

                if (evt.button == 0 && mouse.y <= 27f)
                {
                    BeginTimelineDrag(controlId, TimelineDragMode.Scrub, mouse);
                    ScrubTimeline(clip, mouse.x, total, pixelsPerSecond);
                }
                else
                {
                    BeginTimelineDrag(controlId, TimelineDragMode.Pan, mouse);
                    _timelineDragStartScrollX = _timelineScroll.x;
                    _panMoved = false;
                    _panClickPlacesPlayhead = evt.button == 0;
                }
                evt.Use();
                return;
            }
        }

        void BeginTimelineDrag(int controlId, TimelineDragMode mode, Vector2 contentMouse)
        {
            GUIUtility.hotControl = controlId;
            _timelineDragMode = mode;
            _timelineDragContentMouse = contentMouse;
            _timelineDragStartContent = contentMouse;
            _timelineDragStartScreen = GUIUtility.GUIToScreenPoint(contentMouse);
            _timelineResizeCommitted = false;
            _playing = false;
        }

        void ShowTimelineEventMenu(SpriteClipDef clip, float contentX, float total,
                                   float pixelsPerSecond, int markerIndex = -1)
        {
            clip.EnsureEventMarkers();
            ClearColliderSelection();
            _selectedOnionFrame = -1;
            _playing = false;

            // Marker hit: keep the selection made by the RMB handler and show
            // collider-style marker ops. Empty lane: place / clear at mouse time.
            if (markerIndex >= 0 &&
                markerIndex < clip.EventMarkers.Count &&
                clip.EventMarkers[markerIndex] != null &&
                clip.EventMarkers[markerIndex].EventId != 0)
            {
                ShowTimelineEventMarkerMenu(clip, markerIndex);
                return;
            }

            float authoredTime = Mathf.Clamp(
                (contentX - 48f) / pixelsPerSecond,
                0f,
                Mathf.Max(0f, total - 0.0001f));
            int frame = AuthoredFrameAtTime(clip, authoredTime, out float normalizedTime);
            SelectOnlyFrame(frame);
            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
            _selectedEventClipName = null;
            _previewTime = PreviewTimeForAuthoredTime(clip, authoredTime);

            var menu = new GenericMenu();
            foreach (var definition in _profile.Events)
            {
                if (definition == null || definition.Id == 0) continue;
                byte eventId = definition.Id;
                string eventName = string.IsNullOrWhiteSpace(definition.Name)
                    ? $"Event {eventId}"
                    : definition.Name;
                menu.AddItem(new GUIContent($"Add Event Marker/{eventName}"), false,
                    () => PlaceEventMarker(clip, frame, eventId, normalizedTime));
            }
            menu.AddItem(new GUIContent("Add Event Marker/New Event..."), false, () =>
            {
                byte eventId = NextEventId();
                if (eventId == 0)
                {
                    _status = "All event IDs are already in use";
                    return;
                }
                RecordEventUndo("Create Sprite Animation Event");
                _profile.Events.Add(new SpriteEventDef
                {
                    Id = eventId,
                    Name = $"Event {eventId}",
                    Color = Color.HSVToRGB(Mathf.Repeat(eventId * 0.137f, 1f), 0.72f, 1f),
                });
                PlaceEventMarker(clip, frame, eventId, normalizedTime, recordUndo: false);
                SealEventUndo();
            });
            if (clip.IndexOfFirstMarkerOnFrame(frame) >= 0)
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Clear Event Marker"), false,
                    () => SetFrameEvent(clip, frame, 0));
            }
            menu.ShowAsContext();
        }

        void ShowTimelineEventMarkerMenu(SpriteClipDef clip, int markerIndex)
        {
            if (clip == null)
                return;
            clip.EnsureEventMarkers();
            if (markerIndex < 0 || markerIndex >= clip.EventMarkers.Count)
                return;
            var marker = clip.EventMarkers[markerIndex];
            if (marker == null || marker.EventId == 0)
                return;

            int eventTypeIndex = FindEventTypeIndex(marker.EventId);

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Go to Marker Frame"), false,
                () => SeekTimelineToEventMarker(clip, markerIndex));
            menu.AddItem(new GUIContent("Go to Clip Start"), false,
                () => SeekTimelineToClipBoundary(clip, toEnd: false));
            menu.AddItem(new GUIContent("Go to Clip End"), false,
                () => SeekTimelineToClipBoundary(clip, toEnd: true));
            menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent("Snap to Frame Start"), false,
                () => SnapEventMarkerNormalizedTime(clip, markerIndex, 0f));
            menu.AddItem(new GUIContent("Snap to Frame End"), false,
                () => SnapEventMarkerNormalizedTime(clip, markerIndex, 1f));
            menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent("Rename"), false, () =>
            {
                // GenericMenu callbacks run outside layout — BeginEventRename +
                // FocusTextInControl here loses to menu-close focus steal. Queue
                // via delayCall so the EVENT TYPES inline field draws first.
                byte renameId = marker.EventId;
                int typeIndex = FindEventTypeIndex(renameId);
                EditorApplication.delayCall += () =>
                {
                    if (this == null)
                        return;
                    if (typeIndex >= 0)
                        QueueEventTypeRename(typeIndex);
                    else
                    {
                        // Catalog row missing: still try by id on next OnGUI.
                        SelectEventTypeInCatalog(renameId);
                        BeginEventRename(renameId);
                        _status = $"Renaming {EventName(renameId)} (F2)";
                    }
                    Repaint();
                };
            });
            menu.AddItem(new GUIContent("Duplicate Marker"), false,
                () => DuplicateEventMarkerAt(clip, markerIndex));
            menu.AddSeparator(string.Empty);

            if (eventTypeIndex >= 0)
            {
                menu.AddItem(new GUIContent("Select Event Type"), false,
                    () =>
                    {
                        SelectEventTypeInCatalog(marker.EventId);
                        _eventThisClipExpanded = true;
                        _eventRowDetailsExpanded = true;
                        _status = $"Selected event type {EventName(marker.EventId)}";
                        Repaint();
                    });
            }
            else
                menu.AddDisabledItem(new GUIContent("Select Event Type"));

            menu.AddItem(new GUIContent("Fire Mode/Loop"), !marker.FiresOnce,
                () => SetEventMarkerFireMode(clip, markerIndex, (byte)SpriteEventFireMode.Loop));
            menu.AddItem(new GUIContent("Fire Mode/Once"), marker.FiresOnce,
                () => SetEventMarkerFireMode(clip, markerIndex, (byte)SpriteEventFireMode.Once));

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Delete Event Marker"), false,
                () => RemoveEventMarkerAt(clip, markerIndex));
            menu.ShowAsContext();
        }

        int FindEventTypeIndex(byte eventId)
        {
            if (eventId == 0 || _profile?.Events == null)
                return -1;
            for (int i = 0; i < _profile.Events.Count; i++)
            {
                var definition = _profile.Events[i];
                if (definition != null && definition.Id == eventId)
                    return i;
            }
            return -1;
        }

        void SelectEventTypeInCatalog(byte eventId)
        {
            int index = FindEventTypeIndex(eventId);
            if (index < 0)
                return;
            // Clear first so SelectEventTypeCard does not toggle-deselect
            // when the type is already the sole selection.
            ClearEventTypeSelection();
            SelectEventTypeCard(index, null, false, false);
        }

        void SeekTimelineToEventMarker(SpriteClipDef clip, int markerIndex)
        {
            if (clip == null)
                return;
            clip.EnsureEventMarkers();
            if (markerIndex < 0 || markerIndex >= clip.EventMarkers.Count)
                return;
            var marker = clip.EventMarkers[markerIndex];
            if (marker == null || marker.EventId == 0)
                return;
            SelectEventMarker(clip, markerIndex, EventAuthoredTime(clip, marker));
            _status = $"Jumped to marker frame {marker.FrameIndex + 1}";
            Repaint();
        }

        void SeekTimelineToClipBoundary(SpriteClipDef clip, bool toEnd)
        {
            if (clip == null || clip.Frames == null || clip.Frames.Length == 0)
                return;
            int frame = toEnd ? clip.Frames.Length - 1 : 0;
            SelectOnlyFrame(frame);
            _previewTime = PreviewTimeForAuthoredTime(clip, AuthoredStartTime(clip, frame));
            _playing = false;
            _status = toEnd ? "Jumped to clip end" : "Jumped to clip start";
            Repaint();
        }

        void SnapEventMarkerNormalizedTime(SpriteClipDef clip, int markerIndex, float normalizedTime)
        {
            if (clip == null)
                return;
            clip.EnsureEventMarkers();
            if (markerIndex < 0 || markerIndex >= clip.EventMarkers.Count)
                return;
            var marker = clip.EventMarkers[markerIndex];
            if (marker == null || marker.EventId == 0)
                return;
            float next = Mathf.Clamp01(normalizedTime);
            if (Mathf.Approximately(marker.NormalizedTime, next))
            {
                SelectEventMarker(clip, markerIndex, EventAuthoredTime(clip, marker));
                return;
            }
            RecordEventUndo("Snap Sprite Animation Event");
            marker.NormalizedTime = next;
            clip.SyncLegacyEventsFromMarkers();
            SelectEventMarker(clip, markerIndex, EventAuthoredTime(clip, marker));
            _status = next <= 0f
                ? $"Snapped {EventName(marker.EventId)} to frame start"
                : $"Snapped {EventName(marker.EventId)} to frame end";
            SealEventUndo();
            Repaint();
        }

        void DuplicateEventMarkerAt(SpriteClipDef clip, int markerIndex)
        {
            if (clip == null)
                return;
            clip.EnsureEventMarkers();
            if (markerIndex < 0 || markerIndex >= clip.EventMarkers.Count)
                return;
            var source = clip.EventMarkers[markerIndex];
            if (source == null || source.EventId == 0)
                return;
            RecordEventUndo("Duplicate Sprite Animation Event");
            var clone = source.Clone();
            clip.EventMarkers.Add(clone);
            clip.SyncLegacyEventsFromMarkers();
            int newIndex = clip.EventMarkers.Count - 1;
            SelectEventMarker(clip, newIndex, EventAuthoredTime(clip, clone));
            _status = $"Duplicated {EventName(clone.EventId)} on frame {clone.FrameIndex + 1}";
            SealEventUndo();
            Repaint();
        }

        void SetEventMarkerFireMode(SpriteClipDef clip, int markerIndex, byte fireMode)
        {
            if (clip == null)
                return;
            clip.EnsureEventMarkers();
            if (markerIndex < 0 || markerIndex >= clip.EventMarkers.Count)
                return;
            var marker = clip.EventMarkers[markerIndex];
            if (marker == null || marker.EventId == 0)
                return;
            fireMode = (byte)Mathf.Clamp(fireMode, 0, 1);
            if (marker.FireMode == fireMode)
                return;
            RecordEventUndo("Set Sprite Animation Event Fire Mode");
            marker.FireMode = fireMode;
            clip.SyncLegacyEventsFromMarkers();
            _status = marker.FiresOnce
                ? $"Fire mode Once · {EventName(marker.EventId)}"
                : $"Fire mode Loop · {EventName(marker.EventId)}";
            SealEventUndo();
            Repaint();
        }

        void ShowTimelineFrameMenu(SpriteClipDef clip)
        {
            if (clip?.Frames == null || clip.Frames.Length == 0)
                return;
            EnsureFrameSelection(clip.Frames.Length);
            int selectedCount = Mathf.Max(1, _selectedFrames.Count);
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(selectedCount > 1 ? "Duplicate Frames" : "Duplicate Frame"),
                false, () => DuplicateSelectedFrames(clip));
            if (clip.Frames.Length > 1)
                menu.AddItem(new GUIContent(selectedCount > 1 ? "Delete Frames" : "Delete Frame"),
                    false, () => RemoveSelectedFrames(clip));
            else
                menu.AddDisabledItem(new GUIContent("Delete Frame"));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Add Frame After"), false, () => InsertFrameAfter(clip));
            menu.AddItem(new GUIContent("1×1 from texture..."), false, OpenSheetCellPicker);
            menu.ShowAsContext();
        }

        void PlaceEventMarker(SpriteClipDef clip, int frame, byte eventId,
                              float normalizedTime = 0f, bool recordUndo = true, bool focusPreview = true)
        {
            if (clip == null || eventId == 0)
                return;
            clip.EnsureFrameData();
            frame = Mathf.Clamp(frame, 0, clip.Frames.Length - 1);
            if (recordUndo)
                RecordEventUndo("Add Sprite Animation Event");
            var marker = clip.AddEventMarker(frame, eventId, normalizedTime);
            int index = clip.EventMarkers.IndexOf(marker);
            _selectedEventFrame = frame;
            _selectedEventIndex = index;
            _selectedEventClipName = clip.Name;
            ClearColliderSelection();
            _selectedOnionFrame = -1;
            float authoredTime = EventAuthoredTime(clip, marker);
            if (focusPreview && clip == CurrentClip)
            {
                _selectedFrame = frame;
                _previewTime = PreviewTimeForAuthoredTime(clip, authoredTime);
            }
            _status = $"Added {EventName(eventId)} at {authoredTime:F3}s";
            if (recordUndo)
                SealEventUndo();
            else
                SaveDirty();
            Repaint();
        }

        void SetFrameEvent(SpriteClipDef clip, int frame, byte eventId,
                           float normalizedTime = 0f, bool recordUndo = true, bool focusPreview = true)
        {
            if (clip == null)
                return;
            clip.EnsureFrameData();
            if (frame < 0 || frame >= clip.Frames.Length)
                return;
            if (eventId == 0)
            {
                int first = clip.IndexOfFirstMarkerOnFrame(frame);
                if (first >= 0)
                    RemoveEventMarkerAt(clip, first, recordUndo, focusPreview);
                return;
            }

            int existing = clip.IndexOfFirstMarkerOnFrame(frame);
            if (existing < 0)
            {
                PlaceEventMarker(clip, frame, eventId, normalizedTime, recordUndo, focusPreview);
                return;
            }

            if (recordUndo)
                RecordEventUndo("Set Sprite Animation Event");
            var marker = clip.EventMarkers[existing];
            marker.EventId = eventId;
            marker.NormalizedTime = Mathf.Clamp01(normalizedTime);
            clip.SyncLegacyEventsFromMarkers();
            _selectedEventFrame = frame;
            _selectedEventIndex = existing;
            _selectedEventClipName = clip.Name;
            ClearColliderSelection();
            _selectedOnionFrame = -1;
            float authoredTime = EventAuthoredTime(clip, marker);
            if (focusPreview && clip == CurrentClip)
            {
                _selectedFrame = frame;
                _previewTime = PreviewTimeForAuthoredTime(clip, authoredTime);
            }
            _status = $"Set {EventName(eventId)} at {authoredTime:F3}s";
            if (recordUndo)
                SealEventUndo();
            else
                SaveDirty();
            Repaint();
        }

        void RemoveEventMarkerAt(SpriteClipDef clip, int markerIndex,
                                 bool recordUndo = true, bool focusPreview = true)
        {
            if (clip == null)
                return;
            clip.EnsureEventMarkers();
            if (markerIndex < 0 || markerIndex >= clip.EventMarkers.Count)
                return;
            var marker = clip.EventMarkers[markerIndex];
            if (marker == null)
                return;
            if (recordUndo)
                RecordEventUndo("Clear Sprite Animation Event");
            int frame = marker.FrameIndex;
            byte eventId = marker.EventId;
            clip.RemoveEventMarker(marker);
            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
            _selectedEventClipName = null;
            if (focusPreview && clip == CurrentClip && frame >= 0 && frame < clip.Frames.Length)
                _selectedFrame = frame;
            _status = $"Cleared {EventName(eventId)} on frame {frame + 1}";
            if (recordUndo)
                SealEventUndo();
            else
                SaveDirty();
            Repaint();
        }

        byte NextEventId()
        {
            for (int candidate = 1; candidate <= byte.MaxValue; candidate++)
                if (_profile.Events.Find(definition => definition != null && definition.Id == candidate) == null)
                    return (byte)candidate;
            return 0;
        }

        void EndTimelineDrag()
        {
            if (_timelineDragMode != TimelineDragMode.None)
                GUIUtility.hotControl = 0;
            _timelineDragMode = TimelineDragMode.None;
            _dragFrameIndex = -1;
            _dropFrameSlot = -1;
            _reorderMoved = false;
            _resizeFrameIndex = -1;
            _resizeStartDuration = 0f;
            _resizePixelsPerSecond = 0f;
            _timelineResizeCommitted = false;
            _dragEventSourceFrame = -1;
            _dragEventMarkerIndex = -1;
            _dragEventId = 0;
            _dragEventAuthoredTime = 0f;
            _eventDragMoved = false;
            _dragDrawSourceFrame = -1;
            _dragDrawSocketName = null;
            _dragDrawLayer = 0;
            _drawDragMoved = false;
            _panMoved = false;
            _panClickPlacesPlayhead = false;
            _timelineMarqueeMoved = false;
            _timelineMarqueeOp = SelectionOp.Replace;
            _timelineMarqueeRect = default;
            _timelineMarqueeBaseline.Clear();
        }

        void BeginTimelineMarquee(int controlId, Vector2 contentMouse, SelectionOp op)
        {
            BeginTimelineDrag(controlId, TimelineDragMode.Marquee, contentMouse);
            _timelineMarqueeStart = contentMouse;
            _timelineMarqueeRect = new Rect(contentMouse, Vector2.zero);
            _timelineMarqueeMoved = false;
            _timelineMarqueeOp = op;
            _timelineMarqueeBaseline.Clear();
            foreach (int index in _selectedFrames)
                _timelineMarqueeBaseline.Add(index);
        }

        static int FrameCardAt(Rect[] cards, Vector2 point)
        {
            return FrameAt(cards, null, point);
        }

        static int FrameAt(Rect[] cards, Rect[] thumbnails, Vector2 point)
        {
            for (int i = cards.Length - 1; i >= 0; i--)
            {
                var row = cards[i];
                row.yMin = 54f;
                if (row.Contains(point))
                    return i;
                if (thumbnails != null && thumbnails[i].Contains(point))
                    return i;
            }
            return -1;
        }

        void ApplyTimelineMarqueeSelection(Rect[] cards)
        {
            _selectionScratchFrames.Clear();
            for (int i = 0; i < cards.Length; i++)
            {
                if (cards[i].Overlaps(_timelineMarqueeRect))
                    _selectionScratchFrames.Add(i);
            }

            ApplyMarqueeOnto(_selectedFrames, _timelineMarqueeBaseline, _selectionScratchFrames,
                _timelineMarqueeOp);

            if (_selectedFrames.Count == 0)
            {
                int fallback = Mathf.Clamp(_selectedFrame, 0, Mathf.Max(0, cards.Length - 1));
                _selectedFrames.Add(fallback);
                _selectedFrame = fallback;
                return;
            }

            if (!_selectedFrames.Contains(_selectedFrame))
                _selectedFrame = LowestSelectedFrame();
        }

        void RestoreFrameSelectionFromBaseline()
        {
            _selectedFrames.Clear();
            foreach (int index in _timelineMarqueeBaseline)
                _selectedFrames.Add(index);
            if (_selectedFrames.Count == 0)
                _selectedFrames.Add(Mathf.Max(0, _selectedFrame));
            else if (!_selectedFrames.Contains(_selectedFrame))
                _selectedFrame = LowestSelectedFrame();
        }

        bool ThumbnailContains(Rect thumbnail, Vector2 point)
        {
            if (_profile.TimelineHitShape == SpriteTimelineHitShape.Circle)
            {
                float radius = Mathf.Min(thumbnail.width, thumbnail.height) * 0.48f;
                return (point - thumbnail.center).sqrMagnitude <= radius * radius;
            }

            var polygon = _profile.TimelineHitPolygon;
            bool inside = false;
            for (int i = 0, previous = polygon.Length - 1; i < polygon.Length; previous = i++)
            {
                Vector2 a = new(
                    thumbnail.x + polygon[i].x * thumbnail.width,
                    thumbnail.y + polygon[i].y * thumbnail.height);
                Vector2 b = new(
                    thumbnail.x + polygon[previous].x * thumbnail.width,
                    thumbnail.y + polygon[previous].y * thumbnail.height);
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) /
                              (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        void DrawThumbnailHitShape(Rect thumbnail, bool selected, bool hovered)
        {
            if (!selected && !hovered) return;
            Color color = selected ? AccentColor : new Color(0.8f, 0.9f, 1f, 0.7f);
            int count = _profile.TimelineHitShape == SpriteTimelineHitShape.Circle
                ? 28
                : _profile.TimelineHitPolygon.Length;
            var points = new Vector3[count + 1];
            if (_profile.TimelineHitShape == SpriteTimelineHitShape.Circle)
            {
                float radius = Mathf.Min(thumbnail.width, thumbnail.height) * 0.48f;
                for (int i = 0; i < count; i++)
                {
                    float angle = Mathf.PI * 2f * i / count;
                    points[i] = new Vector3(
                        thumbnail.center.x + Mathf.Cos(angle) * radius,
                        thumbnail.center.y + Mathf.Sin(angle) * radius);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                    points[i] = new Vector3(
                        thumbnail.x + _profile.TimelineHitPolygon[i].x * thumbnail.width,
                        thumbnail.y + _profile.TimelineHitPolygon[i].y * thumbnail.height);
            }
            points[count] = points[0];
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawAAPolyLine(selected ? 2.5f : 1.5f, points);
            Handles.EndGUI();
        }

        void ScrubTimeline(SpriteClipDef clip, float contentX, float total,
                           float pixelsPerSecond)
        {
            float authoredTime = Mathf.Clamp(
                (contentX - 48f) / pixelsPerSecond,
                0f,
                Mathf.Max(0f, total - 0.0001f));
            _previewTime = PreviewTimeForAuthoredTime(clip, authoredTime);
            SelectOnlyFrame(AuthoredFrameAtTime(clip, authoredTime, out _));
            ClearColliderSelection();
            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
        }

        float PreviewTimeForAuthoredTime(SpriteClipDef clip, float authoredTime)
            => SpriteAnimPlayback.PreviewTimeForAuthoredTime(clip, authoredTime);

        int AuthoredFrameAtTime(SpriteClipDef clip, float authoredTime, out float fraction)
            => SpriteAnimPlayback.AuthoredFrameAtTime(clip, authoredTime, out fraction);

        static int DropSlotAtX(float x, Rect[] cards)
        {
            for (int i = 0; i < cards.Length; i++)
                if (x < cards[i].center.x)
                    return i;
            return cards.Length;
        }

        static float DropSlotX(int slot, Rect[] cards)
        {
            if (cards.Length == 0) return 48f;
            if (slot <= 0) return cards[0].x - 4f;
            if (slot >= cards.Length) return cards[cards.Length - 1].xMax + 4f;
            return (cards[slot - 1].xMax + cards[slot].x) * 0.5f;
        }

        void CommitFrameReorder(SpriteClipDef clip, int fromIndex, int insertionSlot)
        {
            if (fromIndex < 0 || fromIndex >= clip.Frames.Length) return;
            if (_selectedFrames.Count > 1 && _selectedFrames.Contains(fromIndex))
            {
                CommitSelectedFrameReorder(clip, insertionSlot);
                return;
            }
            insertionSlot = Mathf.Clamp(insertionSlot, 0, clip.Frames.Length);
            int toIndex = insertionSlot > fromIndex ? insertionSlot - 1 : insertionSlot;
            toIndex = Mathf.Clamp(toIndex, 0, clip.Frames.Length - 1);
            if (toIndex == fromIndex) return;

            RecordProfileUndo("Reorder Sprite Animation Frame");

            foreach (var box in _profile.Hitboxes)
            {
                if (box.ClipName != clip.Name) continue;
                if (box.FrameIndex == fromIndex)
                    box.FrameIndex = toIndex;
                else if (fromIndex < toIndex && box.FrameIndex > fromIndex && box.FrameIndex <= toIndex)
                    box.FrameIndex--;
                else if (toIndex < fromIndex && box.FrameIndex >= toIndex && box.FrameIndex < fromIndex)
                    box.FrameIndex++;
            }

            if (_selectedOnionFrame == fromIndex)
                _selectedOnionFrame = toIndex;
            else if (fromIndex < toIndex && _selectedOnionFrame > fromIndex && _selectedOnionFrame <= toIndex)
                _selectedOnionFrame--;
            else if (toIndex < fromIndex && _selectedOnionFrame >= toIndex && _selectedOnionFrame < fromIndex)
                _selectedOnionFrame++;

            _selectedEventFrame = RemapIndexAfterMove(_selectedEventFrame, fromIndex, toIndex);

            clip.MoveFrame(fromIndex, toIndex);
            SelectOnlyFrame(toIndex);
            float authoredTime = 0f;
            for (int i = 0; i < toIndex; i++)
                authoredTime += FrameDuration(clip, i);
            _previewTime = PreviewTimeForAuthoredTime(clip, authoredTime);
            _status = $"Moved frame {fromIndex + 1} to {toIndex + 1}";
            SaveDirty();
        }

        void CommitSelectedFrameReorder(SpriteClipDef clip, int insertionSlot)
        {
            clip.EnsureFrameData();
            int count = clip.Frames.Length;
            insertionSlot = Mathf.Clamp(insertionSlot, 0, count);
            var selected = new List<int>(_selectedFrames);
            selected.RemoveAll(index => index < 0 || index >= count);
            selected.Sort();
            if (selected.Count == 0)
                return;

            var remaining = new List<int>(count - selected.Count);
            for (int i = 0; i < count; i++)
                if (!_selectedFrames.Contains(i))
                    remaining.Add(i);
            int destination = 0;
            for (int i = 0; i < remaining.Count && remaining[i] < insertionSlot; i++)
                destination++;
            var newToOld = new List<int>(remaining);
            newToOld.InsertRange(destination, selected);
            bool changed = false;
            for (int i = 0; i < count; i++)
                changed |= newToOld[i] != i;
            if (!changed)
                return;

            RecordProfileUndo("Reorder Selected Animation Frames");
            var oldToNew = new int[count];
            for (int i = 0; i < count; i++)
                oldToNew[newToOld[i]] = i;
            clip.Frames = ReorderByMap(clip.Frames, newToOld);
            clip.FrameRows = ReorderByMap(clip.FrameRows, newToOld);
            clip.FrameDurationScales = ReorderByMap(clip.FrameDurationScales, newToOld);
            clip.EventIds = ReorderByMap(clip.EventIds, newToOld);
            clip.EventNormalizedTimes = ReorderByMap(clip.EventNormalizedTimes, newToOld);
            clip.OnionOffsets = ReorderByMap(clip.OnionOffsets, newToOld);
            clip.FrameScales = ReorderByMap(clip.FrameScales, newToOld);
            clip.FrameRotations = ReorderByMap(clip.FrameRotations, newToOld);
            clip.FrameTweenModes = ReorderByMap(clip.FrameTweenModes, newToOld);
            if (clip.Sockets != null)
            {
                for (int i = 0; i < clip.Sockets.Count; i++)
                {
                    int socketFrame = clip.Sockets[i].FrameIndex;
                    if (socketFrame >= 0 && socketFrame < count)
                        clip.Sockets[i].FrameIndex = oldToNew[socketFrame];
                }
            }
            clip.RemapEventMarkerFrames(oldToNew);
            for (int i = 0; i < _profile.Hitboxes.Count; i++)
            {
                var box = _profile.Hitboxes[i];
                if (box.ClipName == clip.Name &&
                    box.FrameIndex >= 0 && box.FrameIndex < count)
                    box.FrameIndex = oldToNew[box.FrameIndex];
            }
            if (_selectedOnionFrame >= 0 && _selectedOnionFrame < count)
                _selectedOnionFrame = oldToNew[_selectedOnionFrame];
            if (_selectedEventFrame >= 0 && _selectedEventFrame < count)
                _selectedEventFrame = oldToNew[_selectedEventFrame];
            _selectedFrames.Clear();
            for (int i = 0; i < selected.Count; i++)
                _selectedFrames.Add(oldToNew[selected[i]]);
            _selectedFrame = oldToNew[_selectedFrame];
            _frameListAnchor = _selectedFrame;
            _previewTime = PreviewTimeForAuthoredTime(
                clip, AuthoredStartTime(clip, _selectedFrame));
            _status = $"Moved {selected.Count} selected frames";
            SaveDirty();
        }

        static T[] ReorderByMap<T>(T[] source, IList<int> newToOld)
        {
            var result = new T[newToOld.Count];
            for (int i = 0; i < newToOld.Count; i++)
                result[i] = source[newToOld[i]];
            return result;
        }

        void CommitEventMove(SpriteClipDef clip, int markerIndex, float authoredTime)
        {
            clip.EnsureEventMarkers();
            if (markerIndex < 0 || markerIndex >= clip.EventMarkers.Count)
                return;
            var marker = clip.EventMarkers[markerIndex];
            if (marker == null || marker.EventId == 0)
                return;

            int destinationFrame = AuthoredFrameAtTime(clip, authoredTime, out float destinationNormalizedTime);
            RecordProfileUndo("Move Sprite Animation Event");
            int sourceFrame = marker.FrameIndex;
            marker.FrameIndex = destinationFrame;
            marker.NormalizedTime = Mathf.Clamp01(destinationNormalizedTime);
            clip.SyncLegacyEventsFromMarkers();
            _selectedEventFrame = destinationFrame;
            _selectedEventIndex = markerIndex;
            _selectedEventClipName = clip.Name;
            _selectedFrame = destinationFrame;
            _previewTime = PreviewTimeForAuthoredTime(clip, authoredTime);
            _status = destinationFrame == sourceFrame
                ? $"Moved {EventName(marker.EventId)} to {authoredTime:F3}s"
                : $"Moved {EventName(marker.EventId)} to frame {destinationFrame + 1} at {authoredTime:F3}s";
            SaveDirty();
        }

        void DrawTimelineSocketDrawKeys(SpriteClipDef clip, float[] frameTimes, float pixelsPerSecond)
        {
            if (clip?.Sockets == null || frameTimes == null)
                return;
            float laneY = TimelineDrawLaneY + TimelineDrawLaneH * 0.5f;
            for (int i = 0; i < clip.Sockets.Count; i++)
            {
                var key = clip.Sockets[i];
                if (!IsFrameAttachedDrawKey(key))
                    continue;
                int frame = key.FrameIndex;
                if (frame < 0 || frame >= frameTimes.Length)
                    continue;
                string name = SpriteSocketKeys.CanonicalName(key.Name);
                bool dragging = _timelineDragMode == TimelineDragMode.SocketDraw &&
                                _drawDragMoved &&
                                frame == _dragDrawSourceFrame &&
                                SpriteSocketKeys.NamesEqual(name, _dragDrawSocketName);
                float time = dragging
                    ? Mathf.Max(0f, (_timelineDragContentMouse.x - 48f) / Mathf.Max(0.01f, pixelsPerSecond))
                    : frameTimes[frame];
                float x = 48f + time * pixelsPerSecond + SocketDrawStackOffsetX(clip.Sockets, frame, name);
                Color color = SocketDrawKeyColor(key.DrawLayer);
                Color guide = color;
                guide.a = 0.28f;
                EditorGUI.DrawRect(new Rect(x - 0.5f, TimelineDrawLaneY, 1f, 146f), guide);
                bool selected = frame == _selectedSocketDrawFrame &&
                                SpriteSocketKeys.NamesEqual(name, _selectedSocketDrawName);
                if (selected)
                    DrawDiamond(new Vector2(x, laneY), 8f, Color.white);
                DrawDiamond(new Vector2(x, laneY), 5.5f, color);
                string side = key.DrawLayer == SpriteSocketKeys.DrawBehind ? "Behind"
                    : key.DrawLayer == SpriteSocketKeys.DrawFront ? "Front" : "Default";
                GUI.Label(new Rect(x + 8f, TimelineDrawLaneY + 2f, 92f, 16f),
                    $"{name}  {side}", _mutedStyle);
                EditorGUIUtility.AddCursorRect(new Rect(x - 10f, TimelineDrawLaneY, 20f, TimelineDrawLaneH),
                    MouseCursor.MoveArrow);
            }
        }

        float SocketDrawStackOffsetX(IList<FrameSocketDef> sockets, int frame, string name)
        {
            int index = 0;
            if (sockets == null)
                return 0f;
            for (int i = 0; i < sockets.Count; i++)
            {
                var key = sockets[i];
                if (key == null || key.FrameIndex != frame || !IsFrameAttachedDrawKey(key))
                    continue;
                if (SpriteSocketKeys.NamesEqual(key.Name, name))
                    return index * 10f;
                index++;
            }
            return 0f;
        }

        static Color SocketDrawKeyColor(byte layer)
        {
            if (layer == SpriteSocketKeys.DrawBehind)
                return SocketDrawBehindColor;
            if (layer == SpriteSocketKeys.DrawFront)
                return SocketDrawFrontColor;
            return new Color(0.62f, 0.66f, 0.72f);
        }

        bool TryHitSocketDrawKey(SpriteClipDef clip, float[] frameTimes, float pixelsPerSecond,
            Vector2 point, out int frame, out string name)
        {
            frame = -1;
            name = null;
            if (clip?.Sockets == null || frameTimes == null)
                return false;
            if (point.y < TimelineDrawLaneY || point.y > TimelineDrawLaneY + TimelineDrawLaneH)
                return false;
            float laneY = TimelineDrawLaneY + TimelineDrawLaneH * 0.5f;
            float best = 110f;
            for (int i = clip.Sockets.Count - 1; i >= 0; i--)
            {
                var key = clip.Sockets[i];
                if (!IsFrameAttachedDrawKey(key))
                    continue;
                int keyFrame = key.FrameIndex;
                if (keyFrame < 0 || keyFrame >= frameTimes.Length)
                    continue;
                string keyName = SpriteSocketKeys.CanonicalName(key.Name);
                float x = 48f + frameTimes[keyFrame] * pixelsPerSecond +
                          SocketDrawStackOffsetX(clip.Sockets, keyFrame, keyName);
                float sqr = (point - new Vector2(x, laneY)).sqrMagnitude;
                if (sqr <= best)
                {
                    best = sqr;
                    frame = keyFrame;
                    name = keyName;
                }
            }
            return frame >= 0;
        }

        void SelectSocketDrawKey(SpriteClipDef clip, int frame, string socketName)
        {
            socketName = SpriteSocketKeys.CanonicalName(socketName);
            if (clip == null || string.IsNullOrEmpty(socketName) ||
                frame < 0 || frame >= clip.Frames.Length ||
                IsIndependentSocketName(socketName))
                return;
            _selectedSocketDrawFrame = frame;
            _selectedSocketDrawName = socketName;
            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
            _selectedFrame = Mathf.Max(0, frame);
            _selectedFrames.Clear();
            _selectedFrames.Add(_selectedFrame);
            _selectedSocketName = socketName;
            _selectedSockets.Clear();
            _selectedSockets.Add(socketName);
            _previewTime = PreviewTimeAtFrame(clip, frame);
            _playing = false;
            float time = AuthoredStartTime(clip, frame);
            var key = SpriteSocketKeys.FindOnFrame(clip.Sockets, socketName, frame);
            bool behind = key != null && key.DrawLayer == SpriteSocketKeys.DrawBehind;
            bool front = key != null && key.DrawLayer == SpriteSocketKeys.DrawFront;
            _status = front
                ? $"{socketName}  Front at {time:0.00}s"
                : behind
                    ? $"{socketName}  Behind at {time:0.00}s"
                    : $"{socketName}  draw at {time:0.00}s";
        }

        void ShowTimelineSocketDrawMenu(SpriteClipDef clip, float contentX, float total,
                                        float pixelsPerSecond, string hitName, int hitFrame)
        {
            float authoredTime = Mathf.Clamp(
                (contentX - 48f) / pixelsPerSecond, 0f, Mathf.Max(0f, total - 0.0001f));
            int frame = hitFrame >= 0 ? hitFrame : AuthoredFrameAtTime(clip, authoredTime, out _);
            SelectOnlyFrame(frame);
            _previewTime = PreviewTimeForAuthoredTime(clip, authoredTime);
            _playing = false;
            var names = FrameAttachedSocketNames(clip);
            if (names.Count == 0)
            {
                _status = "Add a Frame-Attached socket first to place Socket Draw keys";
                return;
            }

            var menu = new GenericMenu();
            if (!string.IsNullOrEmpty(_selectedSocketName) &&
                names.Exists(n => SpriteSocketKeys.NamesEqual(n, _selectedSocketName)))
            {
                string selected = SpriteSocketKeys.CanonicalName(_selectedSocketName);
                menu.AddItem(new GUIContent($"{selected}/Behind here"), false,
                    () => KeySocketDrawAtTime(clip, frame, behind: true, selected));
                menu.AddItem(new GUIContent($"{selected}/Front here"), false,
                    () => KeySocketDrawAtTime(clip, frame, behind: false, selected));
                menu.AddSeparator(string.Empty);
            }
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (!string.IsNullOrEmpty(_selectedSocketName) &&
                    SpriteSocketKeys.NamesEqual(name, _selectedSocketName))
                    continue;
                string captured = name;
                menu.AddItem(new GUIContent($"{captured}/Behind here"), false,
                    () => KeySocketDrawAtTime(clip, frame, behind: true, captured));
                menu.AddItem(new GUIContent($"{captured}/Front here"), false,
                    () => KeySocketDrawAtTime(clip, frame, behind: false, captured));
            }
            if (!string.IsNullOrEmpty(hitName) && hitFrame >= 0)
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent($"Clear {hitName}"), false,
                    () => ClearSocketDrawKey(clip, hitFrame, hitName));
            }
            menu.ShowAsContext();
        }

        List<string> ClipSocketNames(SpriteClipDef clip)
        {
            var names = SpriteSocketKeys.UniqueNamesInOrder(clip?.Sockets);
            var catalog = _profile?.SocketCatalog?.Items;
            if (catalog == null)
                return names;
            for (int i = 0; i < catalog.Count; i++)
            {
                var item = catalog[i];
                if (item == null || string.IsNullOrWhiteSpace(item.SocketName))
                    continue;
                string name = SpriteSocketKeys.CanonicalName(item.SocketName);
                bool exists = false;
                for (int n = 0; n < names.Count; n++)
                {
                    if (SpriteSocketKeys.NamesEqual(names[n], name))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    names.Add(name);
            }
            return names;
        }

        List<string> FrameAttachedSocketNames(SpriteClipDef clip)
        {
            var names = ClipSocketNames(clip);
            for (int i = names.Count - 1; i >= 0; i--)
            {
                if (IsIndependentSocketName(names[i]))
                    names.RemoveAt(i);
            }
            return names;
        }

        void CommitSocketDrawMove(SpriteClipDef clip, int sourceFrame, string socketName, byte layer,
            float contentX)
        {
            float authoredTime = Mathf.Clamp(
                (contentX - 48f) / Mathf.Max(0.01f, TimelinePixelsPerSecond(clip)),
                0f, Mathf.Max(0f, TotalAuthoredDuration(clip) - 0.0001f));
            MoveSocketDrawKeyToTime(clip, sourceFrame, socketName, layer, authoredTime);
        }

        void MoveSocketDrawKeyToTime(SpriteClipDef clip, int sourceFrame, string socketName, byte layer,
            float authoredTime)
        {
            socketName = SpriteSocketKeys.CanonicalName(socketName);
            if (clip?.Frames == null || string.IsNullOrEmpty(socketName) ||
                sourceFrame < 0 || sourceFrame >= clip.Frames.Length)
                return;
            authoredTime = Mathf.Clamp(authoredTime, 0f,
                Mathf.Max(0f, TotalAuthoredDuration(clip) - 0.0001f));
            int dest = AuthoredFrameAtTime(clip, authoredTime, out _);
            if (dest < 0 || dest >= clip.Frames.Length)
                return;
            RecordProfileUndo("Move Socket Draw Key");
            if (dest != sourceFrame)
            {
                var sourceKey = SpriteSocketKeys.FindOnFrame(clip.Sockets, socketName, sourceFrame);
                if (sourceKey != null)
                    sourceKey.DrawLayer = SpriteSocketKeys.DrawUnset;
            }
            var destKey = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, socketName, dest);
            destKey.DrawLayer = layer == SpriteSocketKeys.DrawUnset ? SpriteSocketKeys.DrawFront : layer;
            _selectedSocketDrawFrame = dest;
            _selectedSocketDrawName = socketName;
            _selectedFrame = dest;
            _selectedSocketName = socketName;
            _previewTime = PreviewTimeForAuthoredTime(clip, authoredTime);
            _status = $"Moved {socketName} draw key to {authoredTime:0.00}s";
            SaveDirty();
        }

        void ClearSocketDrawKey(SpriteClipDef clip, int frame, string socketName = null)
        {
            socketName = string.IsNullOrEmpty(socketName) ? _selectedSocketDrawName : socketName;
            socketName = SpriteSocketKeys.CanonicalName(socketName);
            if (clip == null || string.IsNullOrEmpty(socketName))
                return;
            var key = SpriteSocketKeys.FindOnFrame(clip.Sockets, socketName, frame);
            if (key == null || key.DrawLayer == SpriteSocketKeys.DrawUnset)
                return;
            RecordProfileUndo("Clear Socket Draw Key");
            key.DrawLayer = SpriteSocketKeys.DrawUnset;
            if (_selectedSocketDrawFrame == frame &&
                SpriteSocketKeys.NamesEqual(_selectedSocketDrawName, socketName))
            {
                _selectedSocketDrawFrame = -1;
                _selectedSocketDrawName = null;
            }
            _status = $"Cleared Socket Draw on {socketName}";
            SaveDirty();
            Repaint();
        }

        void DrawRuler(float width, float duration, float pixelsPerSecond)
        {
            EditorGUI.DrawRect(new Rect(0f, 0f, width, 27f), new Color(0.095f, 0.11f, 0.135f));
            int twentieths = Mathf.CeilToInt(duration * 20f);
            for (int i = 0; i <= twentieths; i++)
            {
                float seconds = i / 20f;
                float x = 48f + seconds * pixelsPerSecond;
                bool major = i % 10 == 0;
                bool medium = !major && i % 2 == 0;
                float tickY = major ? 8f : medium ? 14f : 19f;
                float tickHeight = 27f - tickY;
                Color tickColor = major ? TextMuted : medium ? new Color(0.38f, 0.43f, 0.5f) : BorderColor;
                EditorGUI.DrawRect(new Rect(x, tickY, major ? 2f : 1f, tickHeight), tickColor);
                if (major)
                    GUI.Label(new Rect(x + 5f, 1f, 58f, 16f), $"{seconds:F1}s", _mutedStyle);
            }
        }

        static Rect FrameResizeHandle(Rect card)
            => new(card.xMax - 5f, card.y, 6f, card.height);

        Rect TimelineSpriteRect(Rect area)
        {
            float cellAspect = 1f;
            if (_profile.Sheet != null)
            {
                float sourceWidth = _profile.Sheet.width / (float)Mathf.Max(1, _profile.Columns);
                float sourceHeight = _profile.Sheet.height / (float)Mathf.Max(1, _profile.Rows);
                cellAspect = sourceWidth / Mathf.Max(1f, sourceHeight);
            }

            // Duration expands the card/checkerboard only. The actual image keeps a
            // stable, aspect-correct footprint so long holds never stretch artwork.
            float width = Mathf.Min(50f, Mathf.Max(1f, area.width));
            float height = width / Mathf.Max(0.01f, cellAspect);
            if (height > area.height)
            {
                height = area.height;
                width = height * cellAspect;
            }
            return new Rect(
                area.center.x - width * 0.5f,
                area.center.y - height * 0.5f,
                width,
                height);
        }

        static Rect FitAspectRect(Rect area, float aspect)
        {
            aspect = Mathf.Max(0.01f, aspect);
            float width = Mathf.Max(1f, area.width);
            float height = width / aspect;
            if (height > area.height)
            {
                height = Mathf.Max(1f, area.height);
                width = height * aspect;
            }
            return new Rect(
                area.center.x - width * 0.5f,
                area.center.y - height * 0.5f,
                width,
                height);
        }

        void HandleColliderCreationInput(int controlId, Rect cell, SpriteClipDef clip, int frame)
        {
            // Creation is event-driven and impossible while ColliderCreationMode is None.
            if (_colliderCreationMode == ColliderCreationMode.Polygon)
            {
                HandlePolygonCreationInput(controlId, cell, clip, frame);
                return;
            }

            var evt = Event.current;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                CancelColliderCreation("Collider creation cancelled");
                if (GUIUtility.hotControl == controlId)
                    GUIUtility.hotControl = 0;
                evt.Use();
                Repaint();
                return;
            }

            if (IsPreviewToolCancelClick(evt))
            {
                if (_draggingBox && GUIUtility.hotControl == controlId)
                    GUIUtility.hotControl = 0;
                CancelColliderCreation("Collider creation cancelled");
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                FrameBoxDef existing = FindColliderAt(clip, frame, cell, evt.mousePosition);
                if (existing != null)
                {
                    _playing = false;
                    _selectedFrame = frame;
                    _selectedOnionFrame = -1;
                    _selectedEventFrame = -1;
                    _selectedEventIndex = -1;
                    GUIUtility.keyboardControl = controlId;
                    CancelColliderCreation(null);
                    SelectCollider(existing, SelectionOp.Replace);
                    FocusColliderInInspector(existing);
                    BeginColliderTransform(controlId, existing, ColliderHandleKind.Body, cell, evt.mousePosition);
                    _status = $"Selected {existing.Shape} collider #{existing.Id}";
                    evt.Use();
                    Repaint();
                    return;
                }

                _playing = false;
                _selectedFrame = frame;
                _selectedOnionFrame = -1;
                _draggingBox = true;
                _boxStart = evt.mousePosition;
                _liveBox = CenteredSquareRect(_boxStart, _boxStart, cell, 0f);
                GUIUtility.hotControl = controlId;
                GUIUtility.keyboardControl = controlId;
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDrag && _draggingBox && GUIUtility.hotControl == controlId)
            {
                _liveBox = CenteredSquareRect(_boxStart, evt.mousePosition, cell, 0f);
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0 && _draggingBox &&
                GUIUtility.hotControl == controlId)
            {
                _draggingBox = false;
                _liveBox = CenteredSquareRect(_boxStart, evt.mousePosition, cell, 0f);
                if (_liveBox.width < 5f || _liveBox.height < 5f)
                {
                    float defaultRadius = Mathf.Min(cell.width, cell.height) * 0.12f;
                    _liveBox = CenteredSquareRect(_boxStart, _boxStart, cell, defaultRadius);
                }

                var definition = new FrameBoxDef
                {
                    ClipName = clip.Name,
                    FrameIndex = frame,
                    Id = (byte)_newHitboxId,
                    Shape = ColliderShapeOf(_colliderCreationMode),
                    RectUV = ScreenToUv(_liveBox, cell),
                    Lifetime = _newColliderLifetime,
                    Physics = _newColliderPhysics,
                    IsTrigger = _newColliderIsTrigger,
                };
                definition.BindLifetime(clip.Name, frame);
                AddCreatedCollider(definition, frame);

                GUIUtility.hotControl = 0;
                if (!_continuousColliderPlacement)
                    _colliderCreationMode = ColliderCreationMode.None;
                evt.Use();
                Repaint();
            }
        }

        void HandlePolygonCreationInput(int controlId, Rect cell, SpriteClipDef clip, int frame)
        {
            var evt = Event.current;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                CancelColliderCreation("Polygon creation cancelled");
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Backspace &&
                _polygonDraftUV.Count > 0)
            {
                RemoveLastPolygonVertex();
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.KeyDown &&
                evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter && _polygonDraftUV.Count >= 3)
            {
                CompletePolygonCollider(clip, frame);
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 &&
                _polygonDraftUV.Count == 0)
            {
                FrameBoxDef existing = FindColliderAt(clip, frame, cell, evt.mousePosition);
                if (existing != null)
                {
                    _playing = false;
                    _selectedFrame = frame;
                    _selectedOnionFrame = -1;
                    _selectedEventFrame = -1;
                    _selectedEventIndex = -1;
                    GUIUtility.keyboardControl = controlId;
                    CancelColliderCreation(null);
                    SelectCollider(existing, SelectionOp.Replace);
                    FocusColliderInInspector(existing);
                    BeginColliderTransform(controlId, existing, ColliderHandleKind.Body, cell, evt.mousePosition);
                    _status = $"Selected {existing.Shape} collider #{existing.Id}";
                    evt.Use();
                    Repaint();
                    return;
                }
            }

            if (evt.type == EventType.MouseMove)
            {
                bool wasHovering = _polygonHasHover;
                _polygonHasHover = true;
                _polygonHoverUV = ScreenPointToCellUV(evt.mousePosition, cell);
                if (wasHovering || _polygonHasHover)
                    Repaint();
            }

            if (IsPreviewToolCancelClick(evt))
            {
                if (_polygonDraftUV.Count > 0)
                    RemoveLastPolygonVertex();
                else
                    CancelColliderCreation("Polygon creation cancelled");
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type != EventType.MouseDown || evt.button != 0)
                return;

            _playing = false;
            _selectedFrame = frame;
            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
            _selectedOnionFrame = -1;
            GUIUtility.keyboardControl = controlId;

            Vector2 pointUV = ScreenPointToCellUV(evt.mousePosition, cell);
            bool closesAtFirst = _polygonDraftUV.Count >= 3 &&
                Vector2.Distance(CellUVToScreenPoint(_polygonDraftUV[0], cell), evt.mousePosition) <= 11f;
            if (closesAtFirst || (evt.clickCount >= 2 && _polygonDraftUV.Count >= 3))
            {
                CompletePolygonCollider(clip, frame);
            }
            else if (_polygonDraftUV.Count >= 64)
            {
                _status = "Polygon vertex limit reached; click the first point or press Enter to close";
            }
            else if (_polygonDraftUV.Count == 0 ||
                     Vector2.Distance(CellUVToScreenPoint(
                         _polygonDraftUV[_polygonDraftUV.Count - 1], cell), evt.mousePosition) >= 3f)
            {
                _polygonDraftUV.Add(pointUV);
                _status = _polygonDraftUV.Count < 3
                    ? $"Polygon vertex {_polygonDraftUV.Count} placed"
                    : $"Polygon has {_polygonDraftUV.Count} vertices; click the first point to close";
            }

            evt.Use();
            Repaint();
        }

    }
}

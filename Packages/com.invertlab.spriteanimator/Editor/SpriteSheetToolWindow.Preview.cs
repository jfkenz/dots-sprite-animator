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

        void DrawPreview(Rect rect)
        {
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 20f), "PREVIEW", _sectionStyle);
            var viewRect = new Rect(rect.xMax - 74f, rect.y + 7f, 62f, 22f);
            if (GUI.Button(viewRect, new GUIContent("View ▾", "Preview offsets, motion paths, and socket frames."), EditorStyles.miniButton))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Authored offsets"), _previewOffsetMode == PreviewOffsetMode.Authored, () =>
                {
                    RecordWindowUndo("Change Sprite Offset Preview");
                    _previewOffsetMode = PreviewOffsetMode.Authored;
                    Repaint();
                });
                menu.AddItem(new GUIContent("Centered cells"), _previewOffsetMode == PreviewOffsetMode.Centered, () =>
                {
                    RecordWindowUndo("Change Sprite Offset Preview");
                    _previewOffsetMode = PreviewOffsetMode.Centered;
                    Repaint();
                });
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Independent motion paths"), _showIndependentMotionPaths, () =>
                {
                    RecordWindowUndo("Toggle Independent Motion Paths");
                    _showIndependentMotionPaths = !_showIndependentMotionPaths;
                    Repaint();
                });
                if (!string.IsNullOrEmpty(_selectedSocketName))
                    menu.AddItem(new GUIContent("Socket frames…"), false, () =>
                        OpenSocketInheritPanel(CurrentClip, _selectedSocketName, _selectedFrame));
                else
                    menu.AddDisabledItem(new GUIContent("Socket frames…"));
                menu.DropDown(viewRect);
            }

            GUI.Label(new Rect(rect.x + 12f, rect.y + 35f, 34f, 20f), "Zoom", _mutedStyle);
            float zoomWidth = Mathf.Max(24f, rect.width - 158f);
            _previewZoom = GUI.HorizontalSlider(new Rect(rect.x + 49f, rect.y + 40f, zoomWidth, 14f), _previewZoom, 0.25f, 8f);
            GUI.Label(new Rect(rect.xMax - 105f, rect.y + 35f, 46f, 20f), $"{_previewZoom:F2}x", _mutedStyle);
            var canvas = new Rect(rect.x + 10f, rect.y + 82f, rect.width - 20f, rect.height - 94f);
            EventType previewEvent = Event.current.type;
            bool debugBlocksPreview = PreviewDebugToggleBlocksEditorInput(canvas);
            if (debugBlocksPreview)
                Event.current.type = EventType.Ignore;
            var localCanvas = new Rect(0f, 0f, canvas.width, canvas.height);
            TryComputePreviewLayout(localCanvas, out _, out float contentW, out float contentH);
            Vector2 centeredScroll = CenteredPreviewScroll(contentW, contentH, localCanvas);
            bool zoomAtDefault = Mathf.Approximately(_previewZoom, 1f);
            bool panAtDefault = _previewPan.sqrMagnitude < 0.01f && _previewScroll.sqrMagnitude < 1f;
            using (new EditorGUI.DisabledScope(zoomAtDefault && panAtDefault))
            {
                if (GUI.Button(new Rect(rect.xMax - 60f, rect.y + 33f, 48f, 22f),
                    new GUIContent("Reset", "Reset preview zoom and pan."), EditorStyles.miniButton))
                {
                    _previewZoom = 1f;
                    _previewPan = Vector2.zero;
                    _previewScroll = Vector2.zero;
                    _status = "Reset preview zoom and pan";
                }
            }
            bool alreadyCentered = (_previewScroll - centeredScroll).sqrMagnitude < 1f &&
                                   _previewPan.sqrMagnitude < 0.01f;
            using (new EditorGUI.DisabledScope(_profile.Sheet == null || alreadyCentered))
            {
                if (GUI.Button(new Rect(rect.xMax - 146f, rect.y + 7f, 68f, 22f),
                    new GUIContent("Recenter", "Keep zoom and center the sprite in the preview pane."), EditorStyles.miniButton))
                    RecenterPreview(localCanvas);
            }
            var clip = CurrentClip;
            var state = EvaluatePreview(clip, _previewTime);
            _socketSampleFraction = _draggingSocket ? 0f : state.Fraction;
            string frameText = clip == null ? "No clip" :
                $"Frame {state.Frame + 1}/{clip.Frames.Length}   •   {_previewTime:F2}s";
            if (clip != null)
                frameText += _previewOffsetMode == PreviewOffsetMode.Authored
                    ? $"   •   offset {clip.OnionOffsets[state.Frame]} px"
                    : "   •   centered view";
            if (_colliderCreationMode != ColliderCreationMode.None)
                frameText += $"   •   {_colliderCreationMode} tool armed";
            else if (_socketPlacementArmed)
                frameText += "   •   Socket tool armed";
            if (_colliderCreationMode == ColliderCreationMode.Polygon && _polygonDraftUV.Count > 0)
                frameText += $"   •   {_polygonDraftUV.Count} vertices";
            GUI.Label(new Rect(rect.x + 12f, rect.y + 61f, rect.width - 24f, 16f), new GUIContent(frameText, frameText), _mutedStyle);

            HandlePreviewNavigationInput(canvas);
            HandlePreviewSheetDragDrop(canvas);
            DrawCheckerboard(canvas, 18f);
            EditorGUI.DrawRect(new Rect(canvas.x, canvas.y, canvas.width, 1f), BorderColor);

            if (_profile.Sheet == null || clip == null)
            {
                GUI.Label(canvas, "Drop a sprite sheet or profile here", _frameLabelStyle);
                FinishPreviewDebugToggle(canvas, previewEvent, debugBlocksPreview);
                return;
            }

            GUI.BeginGroup(canvas);
            clip.EnsureFrameData();
            if (!OnionSelectionIsVisible(clip, state.Frame))
                _selectedOnionFrame = -1;
            else
                _selectedOnionDelta = _selectedOnionFrame - state.Frame;

            TryComputePreviewLayout(localCanvas, out Rect cell, out contentW, out contentH);
            _previewScroll = GUI.BeginScrollView(
                localCanvas, _previewScroll, new Rect(0f, 0f, contentW, contentH), false, false);

            var onionGhosts = BuildOnionGhostLayouts(clip, state.Frame, cell);
            PruneColliderSelection(clip, state.Frame);
            if (_profile.OnionSkinEnabled)
                DrawOnionGhostSprites(clip, onionGhosts);
            Vector2 activeScreenOffset = _previewOffsetMode == PreviewOffsetMode.Authored
                ? SourcePixelsToScreenOffset(clip.OnionOffsets[state.Frame], cell)
                : Vector2.zero;
            var activeSpriteRect = new Rect(cell.position + activeScreenOffset, cell.size);
            DrawSocketCatalogPreviews(cell, clip, state.Frame, behind: true);
            DrawClipFrame(clip, state.Frame, activeSpriteRect, 1f);
            if (_showPreviewSize)
                DrawPreviewSpriteSizeBox(clip, state.Frame, activeSpriteRect);

            if (_showHitboxes)
            {
                foreach (var box in BoxesFor(clip, state.Frame))
                {
                    bool selected = _selectedColliders.Contains(box);
                    if (box.Hidden && !selected)
                        continue;
                    Color color = selected
                        ? new Color(0.18f, 0.72f, 1f, box.Hidden ? 0.16f : 0.42f)
                        : box.IsCharacter
                            ? new Color(0.25f, 0.85f, 0.78f, 0.34f)
                            : box.IsClip
                                ? new Color(0.95f, 0.72f, 0.22f, 0.34f)
                                : new Color(1f, 0.27f, 0.25f, 0.34f);
                    DrawColliderUV(box, cell, color, selected);
                    if (box.Locked)
                        DrawColliderLockBadge(box, cell);
                    if (selected)
                        DrawColliderSelectionBadge(box, cell);
                }
                FrameBoxDef gizmoBox = PrimarySelectedCollider();
                if (gizmoBox != null && _colliderCreationMode == ColliderCreationMode.None)
                    DrawColliderTransformGizmo(gizmoBox, cell);
                if (_draggingBox)
                    DrawColliderShape(_liveBox, ColliderShapeOf(_colliderCreationMode),
                        null,
                        new Color(1f, 0.45f, 0.25f, 0.38f), true);
                if (_colliderCreationMode == ColliderCreationMode.Polygon)
                    DrawPolygonDraft(cell);
            }

            if (_profile.OnionSkinEnabled)
                DrawOnionGhostBadges(onionGhosts);

            DrawSocketCatalogPreviews(cell, clip, state.Frame, behind: false);
            if (_showPreviewDebug)
                DrawSocketMotionPaths(cell, clip, state.Frame);
            if (_showPreviewDebug)
                DrawSockets(cell, clip, state.Frame);
            DrawPivot(cell);
            if (_socketPlacementArmed && _colliderCreationMode == ColliderCreationMode.None)
                DrawSocketPlacementBalloon(canvas);
            if (_draggingColliderMarquee)
            {
                EditorGUI.DrawRect(_colliderMarqueeRect,
                    new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.12f));
                DrawBorder(_colliderMarqueeRect, AccentColor, 1.5f);
            }

            int previewControlId = GUIUtility.GetControlID(
                "InvertLabSpriteAnimatorPreview".GetHashCode(), FocusType.Keyboard, canvas);
            if ((!_showPivot || _pivotLocked) && _draggingPivot)
            {
                _draggingPivot = false;
                if (_pivotLocked)
                    _pivotSelected = false;
                else if (!_showPivot)
                    _pivotSelected = false;
            }
            // Input arbitration: an armed creation tool owns the canvas. Otherwise
            // selected-collider handles, sockets, and pivot win, then preview marquee
            // (colliders + sockets). Timeline marquee is a separate surface.
            if (_showHitboxes && _colliderCreationMode != ColliderCreationMode.None)
            {
                EditorGUIUtility.AddCursorRect(cell, MouseCursor.ArrowPlus);
                HandleColliderCreationInput(previewControlId, cell, clip, state.Frame);
            }
            else if (_socketPlacementArmed)
            {
                EditorGUIUtility.AddCursorRect(cell, MouseCursor.ArrowPlus);
                HandleSocketPlacementInput(previewControlId, cell, clip, state.Frame);
            }
            else
            {
                // A captured marquee owns all pointer events until release. Selection-
                // dependent gizmos can change IMGUI control IDs while the box crosses
                // sockets, so route this before any socket/collider hit testing.
                if (_colliderMarqueePending)
                {
                    HandlePreviewObjectSelectionInput(
                        previewControlId, cell, new Rect(0f, 0f, contentW, contentH),
                        clip, state.Frame, onionGhosts);
                }
                else if (_showPreviewDebug &&
                         TryHandleSocketLockBadgeClick(clip, state.Frame, cell))
                {
                }
                else if (_showPivot && TryHandlePivotContextClick(cell))
                {
                }
                else if (_showPreviewDebug && TryHandleSocketContextClick(clip, state.Frame, cell))
                {
                }
                else if (_draggingColliderTransform)
                    HandleColliderTransformInput(previewControlId, cell, clip, state.Frame);
                else if (_draggingPivot && !_pivotLocked)
                    HandlePivotInput(previewControlId, cell);
                else if (_draggingSocket)
                    HandleSocketManipulationInput(previewControlId, cell, clip, state.Frame);
                // Pivot handle wins over collider/socket bodies so a pivot inside a
                // selected collider or near a socket pin stays grabbable by mouse.
                else if (_showPivot && Event.current.type == EventType.MouseDown &&
                         Event.current.button == 0 &&
                         PivotHandleHitTest(cell, Event.current.mousePosition))
                {
                    if (_pivotLocked)
                    {
                        _status = "Pivot is locked — unlock in the inspector (lock icon) to drag";
                        Event.current.Use();
                        Repaint();
                    }
                    else
                    {
                        HandlePivotInput(previewControlId, cell);
                    }
                }
                else if (_showHitboxes && Event.current.type == EventType.MouseDown &&
                         Event.current.button == 0 &&
                         ShouldBeginSelectedColliderTransform(cell, clip, state.Frame,
                             Event.current.mousePosition))
                    HandleColliderTransformInput(previewControlId, cell, clip, state.Frame);
                else if (_showPreviewDebug && Event.current.type == EventType.MouseDown &&
                         Event.current.button == 0 &&
                         HitSelectedSocketHandle(cell, clip, state.Frame, Event.current.mousePosition) !=
                         ColliderHandleKind.None)
                    HandleSocketManipulationInput(previewControlId, cell, clip, state.Frame);
                else if (_showPreviewDebug && Event.current.type == EventType.MouseDown &&
                         Event.current.button == 0 &&
                         FindSocketAt(clip, state.Frame, cell, Event.current.mousePosition) != null)
                    HandleSocketManipulationInput(previewControlId, cell, clip, state.Frame);
                else
                {
                    bool selectionConsumed = HandlePreviewObjectSelectionInput(
                        previewControlId, cell, new Rect(0f, 0f, contentW, contentH),
                        clip, state.Frame, onionGhosts);
                    if (!selectionConsumed)
                    {
                        bool pivotConsumed = _showPivot && !_pivotLocked &&
                            HandlePivotInput(previewControlId, cell);
                        if (!pivotConsumed && _profile.OnionSkinEnabled)
                            HandleOnionInput(previewControlId, cell, clip, state.Frame, onionGhosts);
                    }
                    else if (_showPivot && Event.current.type == EventType.MouseDown)
                        _pivotSelected = false;
                }
            }
            GUI.EndScrollView();
            GUI.EndGroup();
            FinishPreviewDebugToggle(canvas, previewEvent, debugBlocksPreview);
            // ContextClick is often delivered in window space after the group clip.
            if (!debugBlocksPreview &&
                _colliderCreationMode != ColliderCreationMode.None &&
                IsPreviewToolCancelClick(Event.current) &&
                canvas.Contains(Event.current.mousePosition))
            {
                if (_colliderCreationMode == ColliderCreationMode.Polygon && _polygonDraftUV.Count > 0)
                    RemoveLastPolygonVertex();
                else
                    CancelColliderCreation(_colliderCreationMode == ColliderCreationMode.Polygon
                        ? "Polygon creation cancelled"
                        : "Collider creation cancelled");
                Event.current.Use();
                Repaint();
            }
            else if (Event.current.type != EventType.Used &&
                _colliderCreationMode == ColliderCreationMode.None &&
                canvas.Contains(Event.current.mousePosition))
            {
                Vector2 contentMouse = Event.current.mousePosition - canvas.position + _previewScroll;
                if (_showPreviewDebug && clip != null &&
                         TryHandleSocketLockBadgeClick(clip, state.Frame, cell, contentMouse))
                {
                }
                else if (_showPivot && TryHandlePivotContextClick(cell, contentMouse))
                {
                }
                else if (_showPreviewDebug && clip != null)
                    TryHandleSocketContextClick(clip, state.Frame, cell, contentMouse);
            }
        }

        static Rect PreviewDebugToggleRect(Rect canvas)
            => new(canvas.xMax - 88f, canvas.yMax - 30f, 80f, 22f);

        static Rect PreviewPivotToggleRect(Rect canvas)
            => new(canvas.xMax - 176f, canvas.yMax - 30f, 80f, 22f);

        static Rect PreviewSizeToggleRect(Rect canvas)
            => new(canvas.xMax - 264f, canvas.yMax - 30f, 80f, 22f);

        static Rect PreviewCollidersToggleRect(Rect canvas)
            => new(canvas.xMax - 368f, canvas.yMax - 30f, 96f, 22f);

        static bool PreviewOverlayToggleContains(Rect canvas, Vector2 mouse)
            => PreviewDebugToggleRect(canvas).Contains(mouse) ||
               PreviewPivotToggleRect(canvas).Contains(mouse) ||
               PreviewSizeToggleRect(canvas).Contains(mouse) ||
               PreviewCollidersToggleRect(canvas).Contains(mouse);

        static bool PreviewDebugToggleBlocksEditorInput(Rect canvas)
        {
            if (!PreviewOverlayToggleContains(canvas, Event.current.mousePosition))
                return false;
            return Event.current.type is EventType.MouseDown or EventType.MouseUp
                or EventType.MouseDrag or EventType.MouseMove or EventType.ContextClick
                or EventType.ScrollWheel;
        }

        void FinishPreviewDebugToggle(Rect canvas, EventType previewEvent, bool debugBlocksPreview)
        {
            if (debugBlocksPreview)
                Event.current.type = previewEvent;
            DrawPreviewCollidersToggle(canvas);
            DrawPreviewSizeToggle(canvas);
            DrawPreviewPivotToggle(canvas);
            DrawPreviewDebugToggle(canvas);
        }

        void DrawPreviewCollidersToggle(Rect canvas)
        {
            if (!GUI.Button(PreviewCollidersToggleRect(canvas),
                    new GUIContent(_showHitboxes ? "Colliders: On" : "Colliders: Off",
                        "Master show/hide for all collider debug in preview, including socket-loaded profiles. Independent of Debug and Size."),
                    EditorStyles.miniButton))
                return;
            RecordWindowUndo("Toggle Preview Colliders");
            _showHitboxes = !_showHitboxes;
            _status = _showHitboxes
                ? "Collider debug visible"
                : "Collider debug hidden";
            Repaint();
        }

        void DrawPreviewSizeToggle(Rect canvas)
        {
            if (!GUI.Button(PreviewSizeToggleRect(canvas),
                    new GUIContent(_showPreviewSize ? "Size: On" : "Size: Off",
                        "Show the sprite cell box: pixel size, world size (cell / PPU), and sheet col / row / index. Independent of Debug."),
                    EditorStyles.miniButton))
                return;
            RecordWindowUndo("Toggle Preview Size Box");
            _showPreviewSize = !_showPreviewSize;
            _status = _showPreviewSize
                ? "Sprite size box visible"
                : "Sprite size box hidden";
            Repaint();
        }

        void DrawPreviewPivotToggle(Rect canvas)
        {
            if (!GUI.Button(PreviewPivotToggleRect(canvas),
                    new GUIContent(_showPivot ? "Pivot: On" : "Pivot: Off",
                "Show or hide the green sheet pivot handle in the preview. Independent of Colliders, Size, and Debug. Use the inspector lock next to Show Pivot to prevent select/drag."),
                    EditorStyles.miniButton))
                return;
            RecordWindowUndo("Toggle Show Pivot");
            _showPivot = !_showPivot;
            if (!_showPivot)
            {
                _draggingPivot = false;
                _pivotSelected = false;
            }
            _status = _showPivot
                ? "Pivot handle visible"
                : "Pivot handle hidden";
            Repaint();
        }

        void DrawPreviewDebugToggle(Rect canvas)
        {
            if (!GUI.Button(PreviewDebugToggleRect(canvas),
                    new GUIContent(_showPreviewDebug ? "Debug: On" : "Debug: Off",
                        "Show or hide preview debug overlays: socket pins, labels, transform gizmos, and Independent Motion / frame paths."),
                    EditorStyles.miniButton))
                return;
            RecordWindowUndo("Toggle Preview Debug");
            _showPreviewDebug = !_showPreviewDebug;
            _status = _showPreviewDebug
                ? "Preview debug overlays visible"
                : "Preview debug overlays hidden";
            Repaint();
        }

        void DrawPreviewSpriteSizeBox(SpriteClipDef clip, int frame, Rect spriteRect)
        {
            var sheet = _profile.SheetForClip(clip);
            int cellIndex = CellIndexOf(clip, frame);
            if (!SpriteSheetProfile.TryGetActiveCellPixels(sheet, cellIndex, out float cellW, out float cellH))
                return;
            float ppu = SpriteSheetProfile.GetPixelsPerUnit(sheet);
            float worldW = cellW / ppu;
            float worldH = cellH / ppu;
            Color box = new(0.28f, 0.92f, 0.82f, 0.95f);
            DrawBorder(spriteRect, box, 1.5f);
            DrawPreviewSizeCorners(spriteRect, 11f, box);

            Vector2 pivot = new(
                spriteRect.x + Mathf.Clamp01(_profile.Pivot.x) * spriteRect.width,
                spriteRect.y + (1f - Mathf.Clamp01(_profile.Pivot.y)) * spriteRect.height);
            EditorGUI.DrawRect(new Rect(pivot.x - 6f, pivot.y - 0.5f, 12f, 1f), box);
            EditorGUI.DrawRect(new Rect(pivot.x - 0.5f, pivot.y - 6f, 1f, 12f), box);

            ResolveClipSheetCell(clip, frame, out int column, out int row, out int index);
            string sizeText = $"{cellW:0.#} × {cellH:0.#} px   •   {worldW:0.###} × {worldH:0.###} u";
            string cellText = FormatSheetCellFull(column, row, index);
            var label = new Rect(spriteRect.x, spriteRect.y - 32f, 248f, 30f);
            if (label.y < 2f)
                label.y = spriteRect.yMax + 2f;
            EditorGUI.DrawRect(label, new Color(0.05f, 0.07f, 0.09f, 0.72f));
            GUI.Label(new Rect(label.x + 4f, label.y, label.width - 6f, 14f),
                new GUIContent(sizeText, "Pixel size of this sheet cell, and world size (pixels / PPU)."),
                _mutedStyle);
            GUI.Label(new Rect(label.x + 4f, label.y + 13f, label.width - 6f, 14f),
                new GUIContent(cellText, "Sheet location of this frame. Index is row-major: row × columns + column."),
                _mutedStyle);
        }

        static void DrawPreviewSizeCorners(Rect rect, float arm, Color color)
        {
            arm = Mathf.Min(arm, rect.width * 0.25f, rect.height * 0.25f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, arm, 2f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, arm), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - arm, rect.y, arm, 2f), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 2f, rect.y, 2f, arm), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, arm, 2f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - arm, 2f, arm), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - arm, rect.yMax - 2f, arm, 2f), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 2f, rect.yMax - arm, 2f, arm), color);
        }

        void HandlePreviewNavigationInput(Rect canvas)
        {
            var evt = Event.current;
            if (evt.type == EventType.ScrollWheel && canvas.Contains(evt.mousePosition))
            {
                float previousZoom = _previewZoom;
                _previewZoom = Mathf.Clamp(_previewZoom * (1f - evt.delta.y * 0.08f), 0.25f, 8f);
                if (!Mathf.Approximately(previousZoom, _previewZoom))
                {
                    Vector2 pivot = evt.mousePosition - canvas.center;
                    float ratio = _previewZoom / Mathf.Max(0.001f, previousZoom);
                    _previewScroll = pivot - (pivot - _previewScroll) * ratio;
                    Repaint();
                }
                evt.Use();
                return;
            }

            bool beginPan = evt.type == EventType.MouseDown &&
                evt.button == 2 &&
                canvas.Contains(evt.mousePosition);
            if (beginPan)
            {
                _previewPanning = true;
                _previewPanStartMouse = evt.mousePosition;
                _previewPanStartOffset = _previewScroll;
                evt.Use();
                return;
            }

            if (evt.type == EventType.MouseDrag && _previewPanning)
            {
                _previewScroll = _previewPanStartOffset - (evt.mousePosition - _previewPanStartMouse);
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseUp && _previewPanning)
            {
                _previewPanning = false;
                evt.Use();
            }
        }

    }
}

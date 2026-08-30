using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>Unified authoring studio for sheets, clips, events, timing, and hitboxes.</summary>
    public sealed class SpriteSheetToolWindow : EditorWindow
    {
        enum TimelineDragMode
        {
            None,
            Pan,
            Scrub,
            Reorder,
            ResizeFrame,
            Event,
            SocketDraw,
            Marquee,
        }

        enum TimelineView
        {
            Frames,
            Sockets,
        }

        enum IndependentKeyStepMode
        {
            Seconds,
            Frames,
        }

        enum ColliderCreationMode
        {
            None = -1,
            Square = (int)SpriteColliderShape.Square,
            Circle = (int)SpriteColliderShape.Circle,
            Polygon = (int)SpriteColliderShape.Polygon,
        }

        enum PreviewOffsetMode
        {
            Authored,
            Centered,
        }

        enum SelectionOp
        {
            Replace,
            Add,
            Subtract,
            Toggle,
            Intersect,
            Range,
            RangeAdd,
        }

        enum IndependentMotionApplyScope
        {
            Selected,
            Track,
        }

        enum ColliderHandleKind
        {
            None,
            Body,
            CornerTL,
            CornerTR,
            CornerBR,
            CornerBL,
            EdgeT,
            EdgeR,
            EdgeB,
            EdgeL,
            Rotate,
        }

        readonly struct OnionGhostLayout
        {
            public readonly int Frame;
            public readonly int Delta;
            public readonly Rect SpriteRect;
            public readonly Rect BadgeRect;
            public readonly Color Color;

            public OnionGhostLayout(int frame, int delta, Rect spriteRect, Rect badgeRect, Color color)
            {
                Frame = frame;
                Delta = delta;
                SpriteRect = spriteRect;
                BadgeRect = badgeRect;
                Color = color;
            }
        }

        readonly struct SocketTransformLayout
        {
            public readonly Vector2 Pivot;
            public readonly Rect Unrotated;
            public readonly float Angle;
            public readonly Vector2 Scale;
            public readonly Vector2 Position;

            public float GuiAngle => -Angle;

            public SocketTransformLayout(Vector2 pivot, Rect unrotated, float angle,
                Vector2 scale, Vector2 position)
            {
                Pivot = pivot;
                Unrotated = unrotated;
                Angle = angle;
                Scale = scale;
                Position = position;
            }
        }

        const string PackageVersion = "0.8.0";
        const float ToolbarHeight = 48f;
        const float TimelineHeight = 244f;
        const float TimelineEventLaneY = 27f;
        const float TimelineEventLaneH = 23f;
        const float TimelineDrawLaneY = 50f;
        const float TimelineDrawLaneH = 22f;
        const float TimelineCardsY = 76f;
        const float IndependentRulerH = 30f;
        const float IndependentDrawLaneY = 30f;
        const float IndependentDrawLaneH = 22f;
        const float IndependentTracksY = 52f;
        const float IndependentTrackRowH = 42f;
        const float DefaultClipPanelWidth = 220f;
        const float DefaultInspectorPanelWidth = 340f;
        const float MinClipPanelWidth = 196f;
        const float MinPreviewPanelWidth = 220f;
        const float MinInspectorPanelWidth = 260f;
        const float Gap = 8f;
        const float PixelsPerSecond = 520f;
        const float TimelineDragMoveThreshold = 1f;
        const float DefaultPreviewSpeed = 1f;
        const float PivotHandleHitRadius = 14f;
        const float ColliderHandleSize = 8f;
        const float ColliderRotateHandleDistance = 26f;
        const float ColliderMinScreenHalf = 6f;
        const float SocketMinAbsScale = 0.05f;
        const float SocketMaxAbsScale = 32f;
        const float SocketHandleHit = 14f;
        const float SocketGroupPivotHit = 16f;
        const float SocketGroupGizmoPad = 28f;
        const float SocketGroupGizmoMinHalf = 36f;
        const int SocketProfilePickerId = 0x5A0C3701;
        const string ClipRenameControl = "BallForgeSpriteAnimator.ClipRename";
        const string SheetRenameControl = "InvertLabSpriteAnimator.SheetRename";
        const string StringFieldControlPrefix = "BallForgeSpriteAnimator.Text.";
        const float SheetRowHeight = 38f;
        const float NestedClipRowHeight = 36f;
        const float ClipNestIndent = 14f;

        static readonly Color WindowColor = new(0.075f, 0.086f, 0.105f);
        static readonly Color PanelColor = new(0.105f, 0.12f, 0.145f);
        static readonly Color PanelAltColor = new(0.13f, 0.15f, 0.18f);
        static readonly Color BorderColor = new(0.22f, 0.25f, 0.3f);
        static readonly Color AccentColor = new(0.18f, 0.66f, 0.92f);
        static readonly Color SocketDrawBehindColor = new(0.62f, 0.42f, 0.98f);
        static readonly Color SocketDrawFrontColor = new(1f, 0.76f, 0.22f);
        static readonly Color EventColor = new(1f, 0.61f, 0.2f);
        static readonly Color TextMuted = new(0.58f, 0.64f, 0.72f);

        [SerializeField] SpriteSheetProfile _profile;
        [SerializeField] float _clipPanelWidth = DefaultClipPanelWidth;
        [SerializeField] float _inspectorPanelWidth = DefaultInspectorPanelWidth;
        [SerializeField] PreviewOffsetMode _previewOffsetMode = PreviewOffsetMode.Authored;
        ScriptableSpriteSheetProfile _asset;
        ScriptableSpriteSheetProfile _undoProxy;
        bool _showHistoryPanel;
        Rect _historyWindowRect = new(40f, 56f, 280f, 340f);
        Vector2 _historyScroll;
        readonly List<string> _undoNames = new();
        readonly List<string> _redoNames = new();
        readonly List<int> _sheetClipCounts = new();
        int _selectedClip;
        int _selectedSheet;
        bool _showTimelineInputHelp;
        bool _sheetFoldInitialized;
        readonly HashSet<int> _collapsedSheets = new();
        int _renamingSheet = -1;
        string _renameSheetValue = string.Empty;
        string _renameSheetOriginal = string.Empty;
        bool _focusSheetRename;
        int _selectedFrame;
        readonly HashSet<int> _selectedFrames = new();
        int _frameListAnchor = -1;
        int _selectedEventFrame = -1;
        int _selectedSocketDrawFrame = -1;
        string _selectedSocketDrawName;
        int _dragDrawSourceFrame = -1;
        string _dragDrawSocketName;
        byte _dragDrawLayer;
        bool _drawDragMoved;
        int _newHitboxId = 1;
        ColliderCreationMode _colliderCreationMode = ColliderCreationMode.None;
        bool _continuousColliderPlacement;
        bool _socketPlacementArmed;
        bool _socketPlacementIndependent;
        string _selectedSocketName;
        readonly HashSet<string> _selectedSockets = new();
        int _socketListAnchor = -1;
        bool _socketListAnchorIndependent;
        bool _socketListMarqueePending;
        bool _socketListMarqueeActive;
        bool _socketListMarqueeIndependent;
        Vector2 _socketListMarqueeStart;
        SelectionOp _socketListMarqueeOp = SelectionOp.Replace;
        readonly HashSet<string> _socketListMarqueeBaseline = new();
        readonly List<string> _selectionScratchNames = new();
        readonly List<int> _selectionScratchFrames = new();
        readonly List<FrameBoxDef> _selectionScratchColliders = new();
        readonly List<Rect> _socketListRowRects = new();
        readonly List<string> _cachedSocketNames = new();
        readonly List<string> _visibleSocketNames = new();
        int _cachedSocketNamesGui = -1;
        int _cachedSocketNamesCount = -1;
        List<FrameSocketDef> _cachedSocketNamesSource;
        int _guiPass;
        readonly List<string> _previewMarqueeSocketNames = new();
        readonly List<Vector2> _previewMarqueeSocketPins = new();
        readonly List<string> _socketMoveNames = new();
        readonly List<FrameSocketDef> _socketMoveKeys = new();
        readonly List<Vector2> _socketMoveStarts = new();
        readonly List<Vector2> _socketMoveStartScales = new();
        readonly List<float> _socketMoveStartAngles = new();
        readonly List<SpriteSocketMotionTrack> _socketMoveMotionTracks = new();
        readonly List<SpriteSocketMotionKey> _socketMoveMotionKeys = new();
        readonly List<Vector2> _socketMoveMotionStarts = new();
        readonly List<Vector2> _socketMoveMotionStartScales = new();
        readonly List<float> _socketMoveMotionStartAngles = new();
        bool _socketMoveWholePath;
        readonly List<string> _socketProfileAssignNames = new();
        bool _socketMoveUndoRecorded;
        bool _draggingSocket;
        Vector2 _socketDragStart;
        ColliderHandleKind _socketHandleKind = ColliderHandleKind.None;
        string _socketTransformName;
        Vector2 _socketScaleStart = Vector2.one;
        float _socketAngleStart;
        Vector2 _socketPivotStart;
        float _socketStartAtan;
        Vector2 _socketHandleLocalStart;
        Vector2 _socketGroupCentroidStart;
        Vector2 _socketGroupCentroidCurrent;
        bool _socketGroupTransform;
        int _socketHotControl;
        int _socketInheritRangeAnchor = -1;
        bool _showSocketInheritPanel;
        Rect _socketInheritPanelRect = new(40f, 56f, 328f, 500f);
        Vector2 _socketInheritScroll;
        readonly HashSet<int> _socketInheritFrames = new();
        readonly List<string> _socketInheritNames = new();
        int _socketInheritSourceFrame;
        int _socketInheritClipIndex = -1;
        bool _socketInheritPosition = true;
        bool _socketInheritRotation = true;
        bool _socketInheritScale = true;
        bool _socketInheritDragging;
        Vector2 _socketInheritDragOffset;
        bool _showSocketTransformPanel;
        Rect _socketTransformPanelRect = new(80f, 72f, 300f, 348f);
        bool _socketTransformDragging;
        Vector2 _socketTransformDragOffset;
        bool _socketTransformAllFrames;
        readonly List<string> _socketTransformNames = new();
        float _socketSampleFraction;
        readonly List<FrameSocketDef> _socketPathKeys = new();
        readonly List<Vector3> _socketPathPoints = new();
        readonly Vector3[] _socketPathPointBuffer = new Vector3[65];
        SpriteSocketMotionKey _motionPathHandleKey;
        int _motionPathHandleKind;
        int _motionPathHandleHotControl;
        Vector2 _motionPathHandleOriginalIn;
        Vector2 _motionPathHandleOriginalOut;
        float _motionPathHandleOriginalBulge;
        bool _motionPathHandleOriginalClockwise;
        [SerializeField] int _socketOrbitShape = 1;
        [SerializeField] int _socketOrbitTilt;
        [SerializeField] int _socketOrbitPattern;
        [SerializeField] int _socketOrbitCount = 3;
        [SerializeField] int _socketCoplanarCount = 3;
        [SerializeField] float _socketOrbitRadius;
        [SerializeField] Vector2 _socketOrbitCenter;
        [SerializeField] bool _socketOrbitCenterSet;
        [SerializeField] TimelineView _timelineView;
        [SerializeField] bool _spacePlaysBothClocks = true;

        bool _playing = false;
        bool _previewLoop = true;
        bool _showHitboxes = true;
        float _speed = 1f;
        [SerializeField] float _previewZoom = 1f;
        [SerializeField] Vector2 _previewPan = Vector2.zero;
        [SerializeField] bool _showPivot = true;
        [SerializeField] bool _showSocketPreviews = true;
        Vector2 _previewScroll;
        bool _previewPanning;
        Vector2 _previewPanStartMouse;
        Vector2 _previewPanStartOffset;
        bool _draggingPivot;
        bool _pivotSelected;
        double _lastEditorTime;
        double _lastSpaceToggleTime = -1d;
        float _previewTime;

        Vector2 _clipScroll;
        Vector2 _inspectorScroll;
        Vector2 _timelineScroll;
        Vector2 _socketTimelineScroll;
        [SerializeField] float _frameTimelineZoom = 1f;
        [SerializeField] float _independentTimelineZoom = 1f;
        [SerializeField] bool _showIndependentMotionPaths = true;
        [SerializeField] bool _showPreviewDebug = true;
        [SerializeField] IndependentKeyStepMode _independentKeyStepMode;
        [SerializeField] float _independentKeyStepSeconds = 0.1f;
        [SerializeField] float _independentKeyStepFps = 12f;
        [SerializeField] int _independentKeyStepCount = 1;
        bool _independentTimelinePanning;
        Vector2 _independentTimelinePanStartMouse;
        Vector2 _independentTimelinePanStartScroll;
        float _socketPreviewTime;
        bool _socketPlaying;
        int _selectedSocketMotionTrack = -1;
        int _selectedSocketMotionKey = -1;
        readonly HashSet<SpriteSocketMotionKey> _selectedSocketMotionKeys = new();
        readonly List<SpriteSocketMotionKey> _independentMotionEditKeys = new();
        readonly List<SpriteSocketMotionKey> _socketMotionDragKeys = new();
        readonly List<float> _socketMotionDragTimes = new();
        float _socketMotionDragStartX;
        bool _socketMotionMarqueeActive;
        bool _socketMotionMarqueeMoved;
        int _socketMotionMarqueeHotControl;
        Vector2 _socketMotionMarqueeStart;
        Rect _socketMotionMarqueeRect;
        SelectionOp _socketMotionMarqueeOp;
        readonly HashSet<SpriteSocketMotionKey> _socketMotionMarqueeBaseline = new();
        bool _draggingSocketMotionKey;
        int _socketMotionHotControl;
        SpriteSocketMotionKey _socketMotionClipboard;
        SpriteSocketMotionTrack _socketTrackClipboard;
        AnimationCurve _socketEaseCurveClipboard;
        int _selectedSocketTriggerTrack = -1;
        int _selectedSocketTriggerIndex = -1;
        bool _draggingSocketTrigger;
        bool _socketTriggerUndoRecorded;
        float _socketTriggerStartTime;
        int _socketTriggerHotControl;
        int _renamingClip = -1;
        string _renameClipValue = string.Empty;
        string _renameClipOriginal = string.Empty;
        bool _focusClipRename;
        TimelineDragMode _timelineDragMode;
        Vector2 _timelineDragStartScreen;
        Vector2 _timelineDragContentMouse;
        float _timelineDragStartScrollX;
        bool _panMoved;
        bool _panClickPlacesPlayhead;
        float _panelResizeMouseStartX;
        float _panelResizeWidthStart;
        int _dragFrameIndex = -1;
        int _dropFrameSlot = -1;
        bool _reorderMoved;
        int _resizeFrameIndex = -1;
        float _resizeStartDuration;
        float _resizePixelsPerSecond;
        bool _timelineResizeCommitted;
        Rect _timelineViewportGui;
        float _timelineContentWidth;
        Vector2 _timelineDragStartContent;
        Vector2 _timelineMarqueeStart;
        Rect _timelineMarqueeRect;
        bool _timelineMarqueeMoved;
        SelectionOp _timelineMarqueeOp = SelectionOp.Replace;
        readonly HashSet<int> _timelineMarqueeBaseline = new();
        int _dragEventSourceFrame = -1;
        byte _dragEventId;
        float _dragEventAuthoredTime;
        bool _eventDragMoved;
        bool _draggingBox;
        Vector2 _boxStart;
        Rect _liveBox;
        readonly List<Vector2> _polygonDraftUV = new(16);
        Vector2 _polygonHoverUV;
        bool _polygonHasHover;
        bool _colliderMarqueePending;
        bool _draggingColliderMarquee;
        SelectionOp _previewMarqueeOp = SelectionOp.Replace;
        int _previewMarqueeHotControl;
        int _colliderListAnchor = -1;
        Vector2 _colliderMarqueeStart;
        Rect _colliderMarqueeRect;
        readonly HashSet<FrameBoxDef> _previewMarqueeColliderBaseline = new();
        readonly HashSet<string> _previewMarqueeSocketBaseline = new();
        bool _draggingColliderTransform;
        ColliderHandleKind _colliderHandleKind;
        FrameBoxDef _colliderTransformBox;
        Vector2 _colliderTransformStartMouse;
        Vector2 _colliderTransformStartCenter;
        float _colliderTransformStartAngle;
        float _colliderTransformStartAtan;
        bool _colliderTransformUndoRecorded;
        readonly List<FrameBoxDef> _colliderMoveBoxes = new();
        readonly List<Rect> _colliderMoveStartRects = new();
        int _selectedOnionFrame = -1;
        int _selectedOnionDelta;
        bool _draggingOnion;
        Vector2 _onionDragStart;
        Vector2 _onionOffsetStart;
        string _status = "Choose a sprite sheet to begin";
        Rect _loadProfileButtonRect;
        bool _createSeparateProfileOnSave;

        GUIStyle _titleStyle;
        GUIStyle _sectionStyle;
        GUIStyle _mutedStyle;
        GUIStyle _mutedWrapStyle;
        GUIStyle _clipStyle;
        GUIStyle _clipSelectedStyle;
        GUIStyle _transportStyle;
        GUIStyle _frameLabelStyle;
        GUIStyle _onionBadgeStyle;
        GUIStyle _socketLabelStyle;
        GUIStyle _socketBalloonStyle;
        readonly List<Texture2D> _styleTextures = new();
        readonly List<OnionGhostLayout> _onionGhostLayouts = new(16);
        readonly HashSet<FrameBoxDef> _selectedColliders = new();
        Color32[] _sheetPixels;
        EntityId _sheetPixelsId;
        int _sheetPixelsWidth;
        int _sheetPixelsHeight;
        int _sheetPixelsColumns;
        int _sheetPixelsRows;
        bool[] _sheetCellEmpty;

        public static void Open()
        {
            var window = GetWindow<SpriteSheetToolWindow>();
            window.titleContent = new GUIContent("DOTS Sprite Animator " + PackageVersion);
            window.minSize = new Vector2(860f, 610f);
            window.Show();
        }

        void OnEnable()
        {
            titleContent = new GUIContent("DOTS Sprite Animator " + PackageVersion);
            _profile ??= new SpriteSheetProfile();
            if (Selection.activeObject is ScriptableSpriteSheetProfile selected)
                LoadAsset(selected);
            EnsureProfile();
            wantsMouseMove = true;
            wantsMouseEnterLeaveWindow = true;
            EditorApplication.update += TickPreview;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
            Undo.undoRedoEvent -= OnUndoRedoEvent;
            Undo.undoRedoEvent += OnUndoRedoEvent;
            _lastEditorTime = EditorApplication.timeSinceStartup;
        }

        void OnDisable()
        {
            EditorApplication.update -= TickPreview;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoEvent -= OnUndoRedoEvent;
            foreach (var texture in _styleTextures)
                if (texture != null)
                    DestroyImmediate(texture);
            _styleTextures.Clear();
            InvalidateSheetPixelCache();
        }

        void TickPreview()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = Mathf.Min(0.1f, (float)(now - _lastEditorTime));
            _lastEditorTime = now;
            bool changed = false;
            if (_playing && CurrentClip != null)
            {
                _previewTime += delta * Mathf.Max(0.05f, _speed);
                var state = EvaluatePreview(CurrentClip, _previewTime);
                if (state.Ended && !_previewLoop)
                    _playing = false;
                changed = true;
            }
            if (_socketPlaying && _profile != null)
            {
                _profile.EnsureSocketMotions();
                float duration = _profile.IndependentMotionDuration;
                _socketPreviewTime += delta * _profile.IndependentMotionSpeed;
                if (_socketPreviewTime > duration)
                {
                    if (_profile.IndependentMotionLoop)
                        _socketPreviewTime %= duration;
                    else
                    {
                        _socketPreviewTime = duration;
                        _socketPlaying = false;
                    }
                }
                changed = true;
            }
            if (changed)
                Repaint();
        }

        void OnGUI()
        {
            _guiPass++;
            EnsureProfile();
            EnsureStyles();
            HandleGlobalShortcuts();
            PollSocketProfilePicker();
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                Focus();

            // Keep Layout/Repaint call order stable, but hide mouse events from the
            // rest of the window so the inherit panel is modal until it closes.
            EventType editorEvent = Event.current.type;
            bool inheritBlocksEditor = SocketInheritBlocksEditorInput();
            if (inheritBlocksEditor)
                Event.current.type = EventType.Ignore;

            int timelineControlId = GUIUtility.GetControlID(
                "BallForgeSpriteAnimatorTimeline".GetHashCode(), FocusType.Passive);
            HandleActiveTimelineDrag(timelineControlId);
            EditorGUI.DrawRect(new Rect(Vector2.zero, position.size), WindowColor);

            DrawToolbar(new Rect(0f, 0f, position.width, ToolbarHeight));

            float timelineHeight = Mathf.Min(TimelineHeight, position.height * 0.38f);
            var workRect = new Rect(
                Gap,
                ToolbarHeight + Gap,
                position.width - Gap * 2f,
                Mathf.Max(230f, position.height - ToolbarHeight - timelineHeight - Gap * 3f));
            ClampPanelWidths(workRect.width);
            float centerWidth = workRect.width - _clipPanelWidth - _inspectorPanelWidth - Gap * 2f;
            var clipsRect = new Rect(workRect.x, workRect.y, _clipPanelWidth, workRect.height);
            var leftSplitter = new Rect(clipsRect.xMax, workRect.y, Gap, workRect.height);
            var previewRect = new Rect(leftSplitter.xMax, workRect.y, centerWidth, workRect.height);
            var rightSplitter = new Rect(previewRect.xMax, workRect.y, Gap, workRect.height);
            var inspectorRect = new Rect(rightSplitter.xMax, workRect.y, _inspectorPanelWidth, workRect.height);
            var timelineRect = new Rect(
                Gap,
                workRect.yMax + Gap,
                position.width - Gap * 2f,
                position.height - workRect.yMax - Gap * 2f);

            DrawPanel(clipsRect);
            DrawPanel(previewRect);
            DrawPanel(inspectorRect);
            DrawPanel(timelineRect);

            HandleWindowSocketContextClick(previewRect);

            DrawClipBrowser(clipsRect);
            DrawInspector(inspectorRect);
            DrawPreview(previewRect);
            DrawPanelSplitter(leftSplitter, true, workRect.width);
            DrawPanelSplitter(rightSplitter, false, workRect.width);
            DrawTimeline(timelineRect, timelineControlId);
            DrawHistoryOverlay();

            if (inheritBlocksEditor)
                Event.current.type = editorEvent;
            DrawSocketInheritOverlay();
            DrawSocketTransformOverlay();
        }

        void ClampPanelWidths(float workWidth)
        {
            float usableWidth = Mathf.Max(1f, workWidth - Gap * 2f);
            float maxClipWidth = Mathf.Max(MinClipPanelWidth,
                usableWidth - MinPreviewPanelWidth - MinInspectorPanelWidth);
            _clipPanelWidth = Mathf.Clamp(_clipPanelWidth, MinClipPanelWidth, maxClipWidth);

            float maxInspectorWidth = Mathf.Max(MinInspectorPanelWidth,
                usableWidth - MinPreviewPanelWidth - _clipPanelWidth);
            _inspectorPanelWidth = Mathf.Clamp(
                _inspectorPanelWidth, MinInspectorPanelWidth, maxInspectorWidth);
        }

        void DrawPanelSplitter(Rect rect, bool resizeClipPanel, float workWidth)
        {
            int controlId = GUIUtility.GetControlID(
                (resizeClipPanel ? "BallForgeClipSplitter" : "BallForgeInspectorSplitter").GetHashCode(),
                FocusType.Passive, rect);
            var evt = Event.current;
            bool active = GUIUtility.hotControl == controlId;
            bool hovered = rect.Contains(evt.mousePosition);

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            Color grip = active || hovered ? AccentColor : BorderColor;
            EditorGUI.DrawRect(new Rect(rect.center.x - 1f, rect.y + 4f, 2f, rect.height - 8f), grip);

            if (evt.type == EventType.MouseDown && evt.button == 0 && hovered)
            {
                if (evt.clickCount >= 2)
                {
                    if (resizeClipPanel)
                        _clipPanelWidth = DefaultClipPanelWidth;
                    else
                        _inspectorPanelWidth = DefaultInspectorPanelWidth;
                    ClampPanelWidths(workWidth);
                    _status = resizeClipPanel
                        ? "Reset clip panel width"
                        : "Reset inspector panel width";
                }
                else
                {
                    GUIUtility.hotControl = controlId;
                    _panelResizeMouseStartX = evt.mousePosition.x;
                    _panelResizeWidthStart = resizeClipPanel
                        ? _clipPanelWidth
                        : _inspectorPanelWidth;
                }
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDrag && active)
            {
                float delta = evt.mousePosition.x - _panelResizeMouseStartX;
                if (resizeClipPanel)
                    _clipPanelWidth = _panelResizeWidthStart + delta;
                else
                    _inspectorPanelWidth = _panelResizeWidthStart - delta;
                ClampPanelWidths(workWidth);
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0 && active)
            {
                GUIUtility.hotControl = 0;
                evt.Use();
                Repaint();
            }
        }

        SpriteClipDef CurrentClip
        {
            get
            {
                if (_profile?.Clips == null || _profile.Clips.Count == 0)
                    return null;
                if (_selectedClip < 0 || _selectedClip >= _profile.Clips.Count)
                    return null;
                var clip = _profile.Clips[_selectedClip];
                if (clip == null)
                    return null;
                clip.EnsureFrameData();
                _selectedFrame = Mathf.Clamp(_selectedFrame, 0, clip.Frames.Length - 1);
                EnsureFrameSelection(clip.Frames.Length);
                return clip;
            }
        }

        bool IsFrameSelected(int frame) => _selectedFrames.Contains(frame);

        void EnsureFrameSelection(int frameCount)
        {
            if (frameCount <= 0)
            {
                _selectedFrames.Clear();
                _selectedFrame = 0;
                return;
            }

            _selectedFrame = Mathf.Clamp(_selectedFrame, 0, frameCount - 1);
            _selectedFrames.RemoveWhere(index => index < 0 || index >= frameCount);
            if (_selectedFrames.Count == 0)
                _selectedFrames.Add(_selectedFrame);
            else if (!_selectedFrames.Contains(_selectedFrame))
                _selectedFrames.Add(_selectedFrame);
        }

        void SelectOnlyFrame(int frame)
        {
            _selectedFrame = Mathf.Max(0, frame);
            _selectedFrames.Clear();
            _selectedFrames.Add(_selectedFrame);
            _frameListAnchor = _selectedFrame;
        }

        int LowestSelectedFrame()
        {
            int lowest = int.MaxValue;
            foreach (int index in _selectedFrames)
                if (index < lowest)
                    lowest = index;
            return lowest == int.MaxValue ? _selectedFrame : lowest;
        }

        void ApplyFrameModifierClick(int frame, SelectionOp op)
        {
            frame = Mathf.Max(0, frame);
            int count = CurrentClip?.Frames?.Length ?? (frame + 1);
            frame = Mathf.Min(frame, Mathf.Max(0, count - 1));
            int anchor = _frameListAnchor >= 0 ? Mathf.Clamp(_frameListAnchor, 0, Mathf.Max(0, count - 1))
                : _selectedFrame;

            switch (op)
            {
                case SelectionOp.Add:
                    _selectedFrames.Add(frame);
                    _selectedFrame = frame;
                    _frameListAnchor = frame;
                    break;
                case SelectionOp.Toggle:
                    if (_selectedFrames.Contains(frame) && _selectedFrames.Count > 1)
                    {
                        _selectedFrames.Remove(frame);
                        if (!_selectedFrames.Contains(_selectedFrame))
                            _selectedFrame = LowestSelectedFrame();
                    }
                    else
                    {
                        _selectedFrames.Add(frame);
                        _selectedFrame = frame;
                    }
                    _frameListAnchor = frame;
                    break;
                case SelectionOp.Subtract:
                    if (_selectedFrames.Contains(frame) && _selectedFrames.Count > 1)
                    {
                        _selectedFrames.Remove(frame);
                        if (!_selectedFrames.Contains(_selectedFrame))
                            _selectedFrame = LowestSelectedFrame();
                    }
                    break;
                case SelectionOp.Range:
                case SelectionOp.RangeAdd:
                {
                    if (op == SelectionOp.Range)
                        _selectedFrames.Clear();
                    int a = Mathf.Min(anchor, frame);
                    int b = Mathf.Max(anchor, frame);
                    for (int i = a; i <= b; i++)
                        _selectedFrames.Add(i);
                    _selectedFrame = frame;
                    if (_frameListAnchor < 0)
                        _frameListAnchor = frame;
                    break;
                }
                case SelectionOp.Intersect:
                {
                    bool keep = _selectedFrames.Contains(frame);
                    _selectedFrames.Clear();
                    _selectedFrames.Add(keep ? frame : Mathf.Clamp(_selectedFrame, 0, Mathf.Max(0, count - 1)));
                    _selectedFrame = keep ? frame : LowestSelectedFrame();
                    break;
                }
                default:
                    SelectOnlyFrame(frame);
                    break;
            }

            if (_selectedFrames.Count == 0)
                SelectOnlyFrame(frame);
        }

        void DrawToolbar(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.09f, 0.105f, 0.13f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), BorderColor);

            GUI.Label(new Rect(14f, 8f, 280f, 24f), "◆  SPRITE ANIMATOR", _titleStyle);
            GUI.Label(new Rect(15f, 29f, 260f, 14f), "v" + PackageVersion + "  ·  DOTS AUTHORING STUDIO", _mutedStyle);

            var clip = CurrentClip;
            bool hasClip = clip != null;

            float x = 252f;
            if (GUI.Button(new Rect(x, 10f, 86f, 28f), new GUIContent("New Profile", "Create a fresh in-memory profile."), _transportStyle))
                NewProfile();
            x += 92f;
            _loadProfileButtonRect = new Rect(x, 10f, 96f, 28f);
            if (GUI.Button(_loadProfileButtonRect,
                new GUIContent("Load Profile", "Related and recent profiles. Ctrl/Cmd+O. Browse is inside the list."),
                _transportStyle))
                ShowLoadProfilePopup();
            x += 102f;
            HandleToolbarProfileDragDrop(rect);

            using (new EditorGUI.DisabledScope(!hasClip))
            {
                if (GUI.Button(new Rect(x, 10f, 28f, 28f), new GUIContent("|<", "Jump to first frame."), _transportStyle))
                    StepToBoundary(clip, forward: false);
                x += 32f;
                if (GUI.Button(new Rect(x, 10f, 24f, 28f), new GUIContent("<", "Step one frame backward."), _transportStyle))
                    StepFrame(clip, -1);
                x += 28f;
                if (GUI.Button(new Rect(x, 10f, 24f, 28f), new GUIContent(">", "Step one frame forward."), _transportStyle))
                    StepFrame(clip, +1);
                x += 28f;
                if (GUI.Button(new Rect(x, 10f, 28f, 28f), new GUIContent(">|", "Jump to last frame."), _transportStyle))
                    StepToBoundary(clip, forward: true);
                x += 34f;
            }

            using (new EditorGUI.DisabledScope(!hasClip))
            {
                if (GUI.Button(new Rect(x, 10f, 70f, 28f),
                    new GUIContent(_playing ? "Pause" : "Play", _playing
                        ? "Pause frame playback."
                        : _spacePlaysBothClocks
                            ? "Play frame playback. Space starts both clocks when Space: Both is on."
                            : "Play frame playback (Space, this tab only)."),
                    _transportStyle))
                    _playing = !_playing;
            }
            x += 76f;
            if (GUI.Button(new Rect(x, 10f, 58f, 28f),
                new GUIContent("Stop", "Stop playback and return to time 0."), _transportStyle))
            {
                _playing = false;
                _previewTime = 0f;
                Repaint();
            }
            x += 66f;
            _previewLoop = GUI.Toggle(new Rect(x, 14f, 58f, 22f),
                _previewLoop, new GUIContent("Loop", "Loop preview playback."));
            x += 64f;
            GUI.Label(new Rect(x, 15f, 40f, 20f), "Speed", _mutedStyle);
            x += 42f;
            _speed = GUI.HorizontalSlider(new Rect(x, 20f, 90f, 16f), _speed, 0.1f, 3f);
            x += 96f;
            GUI.Label(new Rect(x, 15f, 42f, 20f), $"{_speed:F1}x", _mutedStyle);
            x += 44f;
            using (new EditorGUI.DisabledScope(Mathf.Approximately(_speed, DefaultPreviewSpeed)))
            {
                if (GUI.Button(new Rect(x, 11f, 38f, 26f),
                    new GUIContent("1x", "Reset preview speed to its 1x default."), _transportStyle))
                    _speed = DefaultPreviewSpeed;
            }
            x += 44f;

            float checkX = rect.xMax - 266f;
            const float undoW = 46f;
            const float redoW = 46f;
            const float listW = 50f;
            const float undoGap = 4f;
            float clusterW = undoW + undoGap + redoW + undoGap + listW;
            float undoX = x;
            if (undoX + clusterW + 8f > checkX)
                undoX = checkX - clusterW - 8f;

            if (GUI.Button(new Rect(undoX, 11f, undoW, 26f),
                new GUIContent("Undo", "Undo (Ctrl/Cmd+Z)"), _transportStyle))
                Undo.PerformUndo();
            float redoX = undoX + undoW + undoGap;
            if (GUI.Button(new Rect(redoX, 11f, redoW, 26f),
                new GUIContent("Redo", "Redo (Ctrl/Cmd+Shift+Z or Ctrl+Y)"), _transportStyle))
                Undo.PerformRedo();
            float listX = redoX + redoW + undoGap;
            if (GUI.Button(new Rect(listX, 11f, listW, 26f),
                new GUIContent(_showHistoryPanel ? "Hide" : "List",
                    "Show the undo/redo history panel."), _transportStyle))
                _showHistoryPanel = !_showHistoryPanel;
            x = listX + listW + 8f;

            var validateRect = new Rect(checkX, 10f, 52f, 28f);
            if (GUI.Button(validateRect, new GUIContent("Check", "Validate package dependencies and shader setup."), _transportStyle))
                SpriteAnimatorToolsMenu.ValidateInstallation();

            var helpRect = new Rect(rect.xMax - 210f, 10f, 48f, 28f);
            if (GUI.Button(helpRect, new GUIContent("Help", "Open DOTS Sprite Animator quick start docs."), _transportStyle))
                SpriteAnimatorToolsMenu.OpenHelp();

            var saveRect = new Rect(rect.xMax - 154f, 10f, 140f, 28f);
            using (new EditorGUI.DisabledScope(!CanSaveProfile()))
            {
                if (GUI.Button(saveRect,
                    new GUIContent("Save Profile", "Save to <SheetName>_profile.asset and matching json."),
                    _transportStyle))
                    SaveProfile();
            }

            float statusWidth = Mathf.Max(0f, saveRect.x - x - 8f);
            if (statusWidth > 20f)
                GUI.Label(new Rect(x, 15f, statusWidth, 20f), _status, _mutedStyle);
        }

        void NewProfile()
        {
            _asset = null;
            _profile = new SpriteSheetProfile();
            _selectedSheet = 0;
            _sheetFoldInitialized = false;
            _collapsedSheets.Clear();
            EnsureProfile();
            _selectedClip = -1;
            SelectOnlyFrame(0);
            _selectedEventFrame = -1;
            _selectedSocketDrawFrame = -1;
            _selectedOnionFrame = -1;
            _previewTime = 0f;
            _playing = false;
            ClearColliderSelection();
            _createSeparateProfileOnSave = false;
            _status = "Created new profile";
            Repaint();
        }

        internal ScriptableSpriteSheetProfile ProfileAsset => _asset;

        internal List<Texture2D> ProfileSheetTextures()
        {
            var textures = new List<Texture2D>();
            var seen = new HashSet<EntityId>();
            void Add(Texture2D texture)
            {
                if (texture == null || !seen.Add(texture.GetEntityId()))
                    return;
                textures.Add(texture);
            }

            if (_profile != null)
            {
                Add(_profile.Sheet);
                if (_profile.Sheets != null)
                {
                    for (int i = 0; i < _profile.Sheets.Count; i++)
                        Add(_profile.Sheets[i]?.Texture);
                }
            }
            return textures;
        }

        internal void ApplyLoadedProfile(ScriptableSpriteSheetProfile asset)
        {
            if (asset == null)
                return;
            LoadAsset(asset);
            _playing = false;
            ShowNotification(new GUIContent($"Loaded {asset.name}"));
            Repaint();
        }

        internal void BrowseAndLoadProfile()
        {
            LoadProfileFromPicker();
        }

        bool AcceptSheetTexture(Texture2D texture, bool promptIfSibling)
        {
            if (texture == null)
            {
                ApplySheetTexture(null);
                return true;
            }

            var existing = promptIfSibling ? SpriteSheetProfileRecents.FindSibling(texture) : null;
            if (existing != null && existing != _asset)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Profile Found",
                    $"A profile already exists for '{texture.name}'.\n\n{AssetDatabase.GetAssetPath(existing)}\n\nLoad it, or start a new profile with this sheet?",
                    "Load Profile",
                    "Cancel",
                    "New Profile");
                if (choice == 0)
                    ApplyLoadedProfile(existing);
                else if (choice == 2)
                    NewProfileWithSheet(texture);
                GUIUtility.ExitGUI();
                return choice != 1;
            }

            ApplySheetTexture(texture);
            return true;
        }

        void NewProfileWithSheet(Texture2D texture)
        {
            NewProfile();
            _createSeparateProfileOnSave = true;
            ApplySheetTexture(texture);
            _status = $"New profile with {texture.name}";
        }

        void ApplySheetTexture(Texture2D texture)
        {
            EnsureProfile();
            _profile.EnsureSheets(_selectedSheet);
            _profile.Sheet = texture;
            WriteActiveSheetFromLegacy();
            if (texture != null)
                RematchSheetsWorldSize(WorldSizeSourceForTextureAssign(_selectedSheet));
            var activeSheet = _profile.SheetAt(_selectedSheet);
            if (texture != null && activeSheet != null &&
                (string.IsNullOrWhiteSpace(activeSheet.Name) ||
                 activeSheet.Name == "Sheet" || activeSheet.Name.StartsWith("Sheet ")))
                activeSheet.Name = UniqueSheetName(texture.name, _selectedSheet);
            InvalidateSheetPixelCache();
            SaveDirty();
            Repaint();
        }

        void ShowLoadProfilePopup()
        {
            PopupWindow.Show(_loadProfileButtonRect, new SpriteSheetProfileLoadPopup(this));
        }

        void HandleToolbarProfileDragDrop(Rect toolbar)
        {
            HandleProfileOrSheetDrop(toolbar, armedToolsBlock: false);
        }

        void HandlePreviewSheetDragDrop(Rect dropRect)
        {
            HandleProfileOrSheetDrop(dropRect, armedToolsBlock: true);
        }

        void HandleProfileOrSheetDrop(Rect dropRect, bool armedToolsBlock)
        {
            if (armedToolsBlock &&
                (_socketPlacementArmed || _colliderCreationMode != ColliderCreationMode.None))
                return;
            var evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
                return;
            if (!dropRect.Contains(evt.mousePosition))
                return;

            bool hasProfile = TryGetDraggedSocketPreviewProfile(out var profile);
            bool hasSheet = TryGetDraggedSocketPreviewTexture(out var sheet);
            if (!hasProfile && !hasSheet)
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (hasProfile)
                    ApplyLoadedProfile(profile);
                else
                    AcceptSheetTexture(sheet, promptIfSibling: true);
            }
            evt.Use();
        }

        void LoadProfileFromPicker()
        {
            string absolutePath = EditorUtility.OpenFilePanel(
                "Load DOTS Sprite Animator Profile", Application.dataPath, "asset");
            if (string.IsNullOrWhiteSpace(absolutePath))
                return;
            string assetPath = FileUtil.GetProjectRelativePath(absolutePath);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                _status = "Selected profile must be inside this Unity project";
                ShowNotification(new GUIContent(_status));
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(assetPath);
            if (asset == null)
            {
                _status = "Selected file is not a ScriptableSpriteSheetProfile";
                ShowNotification(new GUIContent(_status));
                return;
            }
            LoadAsset(asset);
            _playing = false;
            ShowNotification(new GUIContent($"Loaded {asset.name}"));
        }

        void StepToBoundary(SpriteClipDef clip, bool forward)
        {
            if (clip == null || clip.Frames.Length == 0)
                return;
            int targetFrame = forward ? clip.Frames.Length - 1 : 0;
            SelectOnlyFrame(targetFrame);
            _previewTime = PreviewTimeForAuthoredTime(clip, AuthoredStartTime(clip, targetFrame));
            _playing = false;
            ClearColliderSelection();
            _selectedEventFrame = -1;
            _status = forward ? "Jumped to last frame" : "Jumped to first frame";
            Repaint();
        }

        void StepFrame(SpriteClipDef clip, int delta)
        {
            if (clip == null || clip.Frames.Length == 0 || delta == 0)
                return;

            int current = EvaluatePreview(clip, _previewTime).Frame;
            int next = current + delta;
            if (clip.WrapMode == SpriteAnimWrap.Once)
            {
                next = Mathf.Clamp(next, 0, clip.Frames.Length - 1);
            }
            else
            {
                if (next < 0)
                    next += clip.Frames.Length * (1 + Mathf.FloorToInt(-next / (float)clip.Frames.Length));
                next %= clip.Frames.Length;
            }

            SelectOnlyFrame(next);
            _previewTime = PreviewTimeForAuthoredTime(clip, AuthoredStartTime(clip, next));
            _playing = false;
            ClearColliderSelection();
            _selectedEventFrame = -1;
            _status = $"Stepped to frame {next + 1}";
            Repaint();
        }

        void DrawClipBrowser(Rect rect)
        {
            _profile.EnsureSheets(_selectedSheet);
            if (_profile.Sheets.Count > 0)
                _selectedSheet = Mathf.Clamp(_selectedSheet, 0, _profile.Sheets.Count - 1);
            EnsureSheetFoldState();

            int sheetCount = _profile.Sheets.Count;
            int clipCount = _profile.Clips != null ? _profile.Clips.Count : 0;
            CacheSheetClipCounts(sheetCount);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 20f), "CLIPS", _sectionStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 31f, rect.width - 24f, 16f),
                $"{sheetCount} sheet{(sheetCount == 1 ? "" : "s")} · {clipCount} clip{(clipCount == 1 ? "" : "s")}",
                _mutedStyle);

            const float cardPad = 8f;
            const float headerH = 24f;
            const float insetMargin = 8f;
            const float clipRowH = NestedClipRowHeight;
            const float actionH = 28f;
            const float addSheetH = 28f;
            const float addSheetW = 72f;
            const float cardGap = 6f;
            bool stackAddSheet = sheetCount > 1;
            const float columnFooterH = 38f;

            var listRect = new Rect(rect.x + 8f, rect.y + 52f, rect.width - 16f,
                Mathf.Max(24f, rect.height - 52f - (stackAddSheet ? columnFooterH : 4f)));

            float contentHeight = 4f;
            for (int s = 0; s < sheetCount; s++)
            {
                bool expanded = s == _selectedSheet;
                int n = expanded ? _sheetClipCounts[s] : 0;
                float cardH = cardPad + headerH + cardPad;
                if (expanded)
                {
                    float insetH = insetMargin + n * clipRowH + 4f + actionH + insetMargin;
                    cardH = cardPad + headerH + 6f + insetH + cardPad;
                    if (!stackAddSheet)
                        cardH += 4f + addSheetH;
                }
                contentHeight += cardH + cardGap;
            }
            contentHeight = Mathf.Max(listRect.height, contentHeight);

            _clipScroll = GUI.BeginScrollView(listRect, _clipScroll,
                new Rect(0f, 0f, listRect.width - 15f, contentHeight));

            var input = Event.current;
            HandleBrowserRenameKeys(input);

            float y = 4f;
            float rowW = listRect.width - 21f;
            var sheetCardColor = new Color(0.155f, 0.172f, 0.205f, 1f);
            var clipInsetColor = new Color(0.068f, 0.078f, 0.098f, 1f);
            var quietBorder = new Color(0.2f, 0.225f, 0.265f, 1f);
            var insetBorder = new Color(0.155f, 0.175f, 0.21f, 1f);

            for (int s = 0; s < sheetCount; s++)
            {
                var def = _profile.Sheets[s];
                bool expanded = s == _selectedSheet;
                int clipsOnSheet = _sheetClipCounts[s];

                float insetH = 0f;
                if (expanded)
                    insetH = insetMargin + clipsOnSheet * clipRowH + 4f + actionH + insetMargin;
                float cardH = cardPad + headerH + cardPad;
                if (expanded)
                {
                    cardH = cardPad + headerH + 6f + insetH + cardPad;
                    if (!stackAddSheet)
                        cardH += 4f + addSheetH;
                }

                var cardRect = new Rect(2f, y, rowW, cardH);
                EditorGUI.DrawRect(cardRect, sheetCardColor);
                DrawBorder(cardRect, quietBorder, 1f);

                var headerRect = new Rect(cardRect.x + cardPad, cardRect.y + cardPad,
                    cardRect.width - cardPad * 2f, headerH);
                float countW = expanded ? 0f : 58f;
                var nameRect = new Rect(headerRect.x, headerRect.y,
                    Mathf.Max(20f, headerRect.width - countW), headerH);

                bool renaming = s == _renamingSheet;
                if (renaming)
                {
                    GUI.SetNextControlName(SheetRenameControl);
                    _renameSheetValue = GUI.TextField(nameRect, _renameSheetValue, EditorStyles.boldLabel);
                    if (_focusSheetRename || GUI.GetNameOfFocusedControl() != SheetRenameControl)
                    {
                        EditorGUI.FocusTextInControl(SheetRenameControl);
                        if (GUI.GetNameOfFocusedControl() == SheetRenameControl)
                            _focusSheetRename = false;
                    }
                }
                else
                {
                    string sheetName = string.IsNullOrWhiteSpace(def?.Name)
                        ? (def?.Texture != null && !string.IsNullOrEmpty(def.Texture.name)
                            ? def.Texture.name
                            : $"Sheet {s + 1}")
                        : def.Name;
                    GUI.Label(nameRect, new GUIContent(sheetName,
                        "Click to select this sheet. F2 or double-click the name to rename."),
                        EditorStyles.boldLabel);
                }

                if (!expanded)
                {
                    GUI.Label(new Rect(headerRect.xMax - countW, headerRect.y + 4f, countW, 16f),
                        $"{clipsOnSheet} clip{(clipsOnSheet == 1 ? "" : "s")}", _mutedStyle);
                }

                if (!renaming && input.type == EventType.MouseDown && input.button == 0 &&
                    headerRect.Contains(input.mousePosition))
                {
                    SelectSheetRow(s);
                    if (nameRect.Contains(input.mousePosition) && input.clickCount >= 2)
                        BeginSheetRename(s);
                    input.Use();
                }

                if (expanded)
                {
                    var inset = new Rect(cardRect.x + insetMargin, headerRect.yMax + 6f,
                        cardRect.width - insetMargin * 2f, insetH);
                    EditorGUI.DrawRect(inset, clipInsetColor);
                    DrawBorder(inset, insetBorder, 1f);

                    float clipY = inset.y + insetMargin;
                    if (_profile.Clips != null)
                    {
                        for (int i = 0; i < clipCount; i++)
                        {
                            var clip = _profile.Clips[i];
                            if (clip == null || clip.SheetIndex != s)
                                continue;
                            var itemRect = new Rect(inset.x + 4f, clipY, inset.width - 8f, clipRowH - 2f);
                            var clipNameRect = new Rect(itemRect.x + 8f, itemRect.y + 2f,
                                itemRect.width - 12f, 16f);

                            bool isRenamingClip = i == _renamingClip;
                            if (isRenamingClip)
                            {
                                GUI.Box(itemRect, GUIContent.none, _clipSelectedStyle);
                                GUI.SetNextControlName(ClipRenameControl);
                                _renameClipValue = GUI.TextField(clipNameRect, _renameClipValue, EditorStyles.boldLabel);
                                if (_focusClipRename || GUI.GetNameOfFocusedControl() != ClipRenameControl)
                                {
                                    EditorGUI.FocusTextInControl(ClipRenameControl);
                                    if (GUI.GetNameOfFocusedControl() == ClipRenameControl)
                                        _focusClipRename = false;
                                }
                            }
                            else
                            {
                                GUI.Box(itemRect, GUIContent.none,
                                    i == _selectedClip ? _clipSelectedStyle : _clipStyle);
                                if (input.type == EventType.MouseDown && input.button == 0 &&
                                    itemRect.Contains(input.mousePosition))
                                {
                                    SelectClipCard(i);
                                    if (clipNameRect.Contains(input.mousePosition) && input.clickCount >= 2)
                                        BeginClipRename(i);
                                    input.Use();
                                }
                                string clipName = string.IsNullOrWhiteSpace(clip.Name) ? $"Clip {i + 1}" : clip.Name;
                                GUI.Label(clipNameRect,
                                    new GUIContent(clipName, "Click to select. F2 or double-click the name to rename."),
                                    EditorStyles.boldLabel);
                            }
                            int frameCount = clip.Frames?.Length ?? 0;
                            GUI.Label(new Rect(itemRect.x + 8f, itemRect.y + 18f, itemRect.width - 12f, 13f),
                                $"{frameCount} frames   {clip.FrameRate:F1} fps", _mutedStyle);
                            clipY += clipRowH;
                        }
                    }

                    var actionBar = new Rect(inset.x + 4f, inset.yMax - insetMargin - actionH,
                        inset.width - 8f, actionH);
                    bool canMutate = CurrentClip != null && CurrentClip.SheetIndex == s;
                    DrawClipInsetActions(actionBar, canMutate);

                    if (!stackAddSheet)
                    {
                        var addRect = new Rect(cardRect.xMax - cardPad - addSheetW,
                            inset.yMax + 4f, addSheetW, addSheetH);
                        if (GUI.Button(addRect, "+ Sheet", _transportStyle))
                        {
                            CommitAllRenames();
                            AddSheet();
                        }
                    }
                }

                y += cardH + cardGap;
            }
            GUI.EndScrollView();

            if (stackAddSheet)
            {
                float btnW = Mathf.Min(addSheetW, Mathf.Max(48f, rect.width - 16f));
                var addRect = new Rect(rect.xMax - 8f - btnW, rect.yMax - 34f, btnW, addSheetH);
                if (GUI.Button(addRect, "+ Sheet", _transportStyle))
                {
                    CommitAllRenames();
                    AddSheet();
                }
            }
        }

        void DrawClipInsetActions(Rect bar, bool canMutateClip)
        {
            float gap = 3f;
            float w1 = 52f, w2 = 70f, w3 = 52f;
            float need = w1 + w2 + w3 + gap * 2f;
            if (need > bar.width && bar.width > 40f)
            {
                float scale = bar.width / need;
                w1 *= scale;
                w2 *= scale;
                w3 *= scale;
            }
            float x = bar.x;
            if (GUI.Button(new Rect(x, bar.y, w1, bar.height), "+ Clip", _transportStyle))
            {
                CommitAllRenames();
                AddClip();
            }
            x += w1 + gap;
            using (new EditorGUI.DisabledScope(!canMutateClip))
            {
                if (GUI.Button(new Rect(x, bar.y, w2, bar.height), "Duplicate", _transportStyle))
                {
                    CommitAllRenames();
                    DuplicateClip();
                }
                x += w2 + gap;
                if (GUI.Button(new Rect(x, bar.y, w3, bar.height), "Delete", _transportStyle))
                {
                    CancelAllRenames();
                    DeleteClip();
                }
            }
        }

        void HandleBrowserRenameKeys(Event input)
        {
            string focused = GUI.GetNameOfFocusedControl();
            if (_renamingSheet >= 0 && input.type == EventType.KeyDown &&
                focused == SheetRenameControl)
            {
                if (input.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                {
                    CommitSheetRename();
                    input.Use();
                }
                else if (input.keyCode == KeyCode.Escape)
                {
                    CancelSheetRename();
                    input.Use();
                }
            }
            else if (_renamingClip >= 0 && input.type == EventType.KeyDown &&
                     focused == ClipRenameControl)
            {
                if (input.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                {
                    CommitClipRename();
                    input.Use();
                }
                else if (input.keyCode == KeyCode.Escape)
                {
                    CancelClipRename();
                    input.Use();
                }
            }
            else if (_renamingClip < 0 && _renamingSheet < 0 &&
                     input.type == EventType.KeyDown && input.keyCode == KeyCode.F2 &&
                     !IsEditingStringTextField())
            {
                if (CurrentClip != null)
                    BeginClipRename(_selectedClip);
                else if (_profile.Sheets != null && _profile.Sheets.Count > 0)
                    BeginSheetRename(_selectedSheet);
                input.Use();
            }

        }

        void EnsureSheetFoldState()
        {
            if (_sheetFoldInitialized || _profile?.Sheets == null)
                return;
            _sheetFoldInitialized = true;
            _collapsedSheets.Clear();
            if (_profile.Sheets.Count > 1)
            {
                for (int i = 0; i < _profile.Sheets.Count; i++)
                {
                    if (i != _selectedSheet)
                        _collapsedSheets.Add(i);
                }
            }
        }

        bool IsSheetExpanded(int sheetIndex) => !_collapsedSheets.Contains(sheetIndex);

        void ToggleSheetFold(int sheetIndex)
        {
            if (_collapsedSheets.Contains(sheetIndex))
                _collapsedSheets.Remove(sheetIndex);
            else
                _collapsedSheets.Add(sheetIndex);
        }

        int CountClipsOnSheet(int sheetIndex)
        {
            int n = 0;
            if (_profile?.Clips == null)
                return 0;
            for (int i = 0; i < _profile.Clips.Count; i++)
            {
                if (_profile.Clips[i] != null && _profile.Clips[i].SheetIndex == sheetIndex)
                    n++;
            }
            return n;
        }

        void CacheSheetClipCounts(int sheetCount)
        {
            _sheetClipCounts.Clear();
            for (int i = 0; i < sheetCount; i++)
                _sheetClipCounts.Add(0);
            if (_profile?.Clips == null)
                return;
            for (int i = 0; i < _profile.Clips.Count; i++)
            {
                var clip = _profile.Clips[i];
                if (clip != null && clip.SheetIndex >= 0 && clip.SheetIndex < sheetCount)
                    _sheetClipCounts[clip.SheetIndex]++;
            }
        }

        int FirstClipIndexOfSheet(int sheetIndex)
        {
            if (_profile?.Clips == null)
                return -1;
            for (int i = 0; i < _profile.Clips.Count; i++)
            {
                if (_profile.Clips[i] != null && _profile.Clips[i].SheetIndex == sheetIndex)
                    return i;
            }
            return -1;
        }

        void SelectSheetRow(int index)
        {
            if (_profile?.Sheets == null || index < 0 || index >= _profile.Sheets.Count)
                return;
            CommitAllRenames();
            if (_selectedSheet == index)
            {
                ReleaseShortcutKeyboardFocus();
                return;
            }
            _selectedSheet = index;
            _collapsedSheets.Clear();
            if (_profile.Sheets.Count > 1)
            {
                for (int i = 0; i < _profile.Sheets.Count; i++)
                {
                    if (i != index)
                        _collapsedSheets.Add(i);
                }
            }
            _profile.SyncLegacyFromSheet(_selectedSheet);
            InvalidateSheetPixelCache();
            var current = CurrentClip;
            if (current == null || current.SheetIndex != _selectedSheet)
            {
                int first = FirstClipIndexOfSheet(_selectedSheet);
                if (first >= 0)
                    SelectClipCard(first);
                else
                    ClearClipSelection();
            }
            ReleaseShortcutKeyboardFocus();
            Repaint();
        }

        void ClearClipSelection()
        {
            _selectedClip = -1;
            _selectedFrames.Clear();
            _selectedFrame = 0;
            ClearColliderSelection();
            _selectedEventFrame = -1;
            _selectedOnionFrame = -1;
            _previewTime = 0f;
        }

        void DrawSheetRowThumb(SpriteSheetDef def, Rect rect)
        {
            if (def?.Texture == null)
            {
                EditorGUI.DrawRect(rect, PanelAltColor);
                DrawBorder(rect, BorderColor, 1f);
                return;
            }
            int columns = Mathf.Max(1, def.Columns);
            int rows = Mathf.Max(1, def.Rows);
            DrawCellTinted(def.Texture, 0, rect, Color.white, columns, rows);
            DrawBorder(rect, BorderColor, 1f);
        }

        void AddSheet()
        {
            RecordProfileUndo("Add Sprite Sheet");
            _profile.EnsureSheets(_selectedSheet);
            int n = _profile.Sheets.Count + 1;
            _profile.Sheets.Add(new SpriteSheetDef
            {
                Name = UniqueSheetName($"Sheet {n}"),
                Texture = null,
                Columns = SpriteSheetProfile.DefaultColumns,
                Rows = SpriteSheetProfile.DefaultRows,
                PixelsPerUnit = SpriteSheetProfile.DefaultPixelsPerUnit,
                Pivot = SpriteSheetProfile.DefaultPivot,
            });
            int index = _profile.Sheets.Count - 1;
            _collapsedSheets.Remove(index);
            _selectedSheet = index;
            _profile.SyncLegacyFromSheet(_selectedSheet);
            InvalidateSheetPixelCache();
            ClearClipSelection();
            _status = $"Added {_profile.Sheets[index].Name}";
            SaveDirty();
            Repaint();
        }

        void DeleteSheetAt(int index)
        {
            if (_profile?.Sheets == null || index < 0 || index >= _profile.Sheets.Count)
                return;
            if (_profile.Sheets.Count <= 1)
                return;

            RecordProfileUndo("Delete Sprite Sheet");
            string sheetName = _profile.Sheets[index]?.Name ?? $"Sheet {index + 1}";
            if (_profile.Clips != null)
            {
                for (int i = _profile.Clips.Count - 1; i >= 0; i--)
                {
                    var clip = _profile.Clips[i];
                    if (clip == null || clip.SheetIndex != index)
                        continue;
                    if (_profile.Hitboxes != null)
                        _profile.Hitboxes.RemoveAll(box => box.ClipName == clip.Name);
                    _profile.Clips.RemoveAt(i);
                    if (i < _selectedClip)
                        _selectedClip--;
                    else if (i == _selectedClip)
                        _selectedClip = -1;
                    if (_renamingClip == i)
                        ClearClipRename();
                    else if (_renamingClip > i)
                        _renamingClip--;
                }
                for (int i = 0; i < _profile.Clips.Count; i++)
                {
                    if (_profile.Clips[i] != null && _profile.Clips[i].SheetIndex > index)
                        _profile.Clips[i].SheetIndex--;
                }
            }
            _profile.Sheets.RemoveAt(index);
            var nextCollapsed = new HashSet<int>();
            foreach (int collapsed in _collapsedSheets)
            {
                if (collapsed == index)
                    continue;
                nextCollapsed.Add(collapsed > index ? collapsed - 1 : collapsed);
            }
            _collapsedSheets.Clear();
            foreach (int collapsed in nextCollapsed)
                _collapsedSheets.Add(collapsed);

            if (_selectedSheet == index)
                _selectedSheet = Mathf.Clamp(index, 0, _profile.Sheets.Count - 1);
            else if (_selectedSheet > index)
                _selectedSheet--;
            _selectedSheet = Mathf.Clamp(_selectedSheet, 0, Mathf.Max(0, _profile.Sheets.Count - 1));
            if (_renamingSheet == index)
                ClearSheetRename();
            else if (_renamingSheet > index)
                _renamingSheet--;

            _profile.SyncLegacyFromSheet(_selectedSheet);
            InvalidateSheetPixelCache();
            var current = CurrentClip;
            if (current == null || current.SheetIndex != _selectedSheet)
            {
                int first = FirstClipIndexOfSheet(_selectedSheet);
                if (first >= 0)
                {
                    _selectedClip = first;
                    SelectOnlyFrame(0);
                }
                else
                    ClearClipSelection();
            }
            _status = $"Deleted sheet {sheetName}";
            SaveDirty();
            Repaint();
        }

        void BeginSheetRename(int sheetIndex)
        {
            if (_profile?.Sheets == null || sheetIndex < 0 || sheetIndex >= _profile.Sheets.Count)
                return;
            CommitAllRenames();
            _selectedSheet = sheetIndex;
            _renamingSheet = sheetIndex;
            _renameSheetOriginal = _profile.Sheets[sheetIndex]?.Name ?? string.Empty;
            _renameSheetValue = string.IsNullOrWhiteSpace(_renameSheetOriginal)
                ? $"Sheet {sheetIndex + 1}"
                : _renameSheetOriginal;
            _focusSheetRename = true;
            Repaint();
        }

        void CommitSheetRename()
        {
            if (_renamingSheet < 0 || _profile?.Sheets == null ||
                _renamingSheet >= _profile.Sheets.Count)
            {
                ClearSheetRename();
                return;
            }
            var def = _profile.Sheets[_renamingSheet];
            string newName = UniqueSheetName(_renameSheetValue, _renamingSheet);
            if (def != null && !string.Equals(def.Name, newName, StringComparison.Ordinal))
            {
                RecordProfileUndo("Rename Sprite Sheet");
                def.Name = newName;
                _status = $"Renamed sheet to {newName}";
                SaveDirty();
            }
            ClearSheetRename();
        }

        void CancelSheetRename()
        {
            if (_renamingSheet >= 0)
                _status = $"Kept sheet name {_renameSheetOriginal}";
            ClearSheetRename();
        }

        void ClearSheetRename()
        {
            _renamingSheet = -1;
            _renameSheetValue = string.Empty;
            _renameSheetOriginal = string.Empty;
            _focusSheetRename = false;
            GUI.FocusControl(null);
            Repaint();
        }

        string UniqueSheetName(string requestedName, int ignoredSheetIndex = -1)
        {
            string baseName = string.IsNullOrWhiteSpace(requestedName)
                ? $"Sheet {Mathf.Max(1, ignoredSheetIndex + 1)}"
                : requestedName.Trim();
            string candidate = baseName;
            int suffix = 2;
            while (SheetNameExists(candidate, ignoredSheetIndex))
                candidate = $"{baseName} {suffix++}";
            return candidate;
        }

        bool SheetNameExists(string candidate, int ignoredSheetIndex)
        {
            if (_profile?.Sheets == null)
                return false;
            for (int i = 0; i < _profile.Sheets.Count; i++)
            {
                if (i == ignoredSheetIndex || _profile.Sheets[i] == null)
                    continue;
                if (string.Equals(_profile.Sheets[i].Name, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        void CommitAllRenames()
        {
            if (_renamingClip >= 0)
                CommitClipRename();
            if (_renamingSheet >= 0)
                CommitSheetRename();
        }

        void CancelAllRenames()
        {
            if (_renamingClip >= 0)
                CancelClipRename();
            if (_renamingSheet >= 0)
                CancelSheetRename();
        }

        void WriteActiveSheetFromLegacy()
        {
            if (_profile?.Sheets == null || _profile.Sheets.Count == 0)
                return;
            int index = Mathf.Clamp(_selectedSheet, 0, _profile.Sheets.Count - 1);
            var before = _profile.Sheets[index];
            Texture2D oldTex = before != null ? before.Texture : null;
            int oldCols = before != null ? before.Columns : 0;
            int oldRows = before != null ? before.Rows : 0;
            _profile.WriteLegacyIntoSheet(index);
            var after = _profile.Sheets[index];
            if (after == null || after.Texture != oldTex || after.Columns != oldCols || after.Rows != oldRows)
                InvalidateSheetPixelCache();
        }

        int WorldSizeSourceForTextureAssign(int assignedIndex)
        {
            if (_profile?.Sheets == null)
                return assignedIndex;
            for (int i = 0; i < _profile.Sheets.Count; i++)
            {
                if (i == assignedIndex)
                    continue;
                if (_profile.Sheets[i]?.Texture != null)
                    return i;
            }
            return assignedIndex;
        }

        void RematchSheetsWorldSize(int sourceSheetIndex)
        {
            if (_profile?.Sheets == null || _profile.Sheets.Count == 0)
                return;
            var source = _profile.SheetAt(sourceSheetIndex);
            if (source?.Texture == null)
                return;
            _profile.MatchSheetsWorldSize(sourceSheetIndex);
            _profile.SyncLegacyFromSheet(Mathf.Clamp(_selectedSheet, 0, _profile.Sheets.Count - 1));
        }


        void SelectClipCard(int index)
        {
            if (_profile?.Clips == null || index < 0 || index >= _profile.Clips.Count)
                return;
            if (_renamingClip >= 0 && _renamingClip != index)
                CancelClipRename();
            if (_renamingSheet >= 0)
                CommitSheetRename();
            if (_selectedClip == index)
            {
                ReleaseShortcutKeyboardFocus();
                return;
            }
            if (_selectedClip != index)
            {
                _selectedOnionFrame = -1;
                ClearColliderSelection();
                _selectedEventFrame = -1;
            }
            _selectedClip = index;
            var clip = _profile.Clips[index];
            if (clip != null && _profile.Sheets != null && _profile.Sheets.Count > 0)
            {
                _selectedSheet = Mathf.Clamp(clip.SheetIndex, 0, _profile.Sheets.Count - 1);
                _collapsedSheets.Remove(_selectedSheet);
                _profile.SyncLegacyFromSheet(_selectedSheet);
                InvalidateSheetPixelCache();
            }
            SelectOnlyFrame(0);
            _previewTime = 0f;
            ReleaseShortcutKeyboardFocus();
        }

        void BeginClipRename(int clipIndex)
        {
            if (clipIndex < 0 || clipIndex >= _profile.Clips.Count)
                return;

            if (_renamingClip >= 0 && _renamingClip != clipIndex)
                CommitClipRename();
            if (_renamingSheet >= 0)
                CommitSheetRename();

            _selectedClip = clipIndex;
            _renamingClip = clipIndex;
            _renameClipOriginal = _profile.Clips[clipIndex].Name;
            _renameClipValue = string.IsNullOrWhiteSpace(_renameClipOriginal)
                ? $"Clip {clipIndex + 1}"
                : _renameClipOriginal;
            _focusClipRename = true;
            Repaint();
        }

        void CommitClipRename()
        {
            if (_renamingClip < 0 || _renamingClip >= _profile.Clips.Count)
            {
                ClearClipRename();
                return;
            }

            int clipIndex = _renamingClip;
            var clip = _profile.Clips[clipIndex];
            string oldName = clip.Name;
            string newName = UniqueClipName(_renameClipValue, clipIndex);
            if (!string.Equals(oldName, newName, StringComparison.Ordinal))
            {
                RecordProfileUndo("Rename Sprite Animation Clip");
                clip.Name = newName;
                RenameHitboxClip(oldName, newName);
                _status = $"Renamed clip to {newName}";
                SaveDirty();
            }
            ClearClipRename();
        }

        void CancelClipRename()
        {
            if (_renamingClip >= 0)
                _status = $"Kept clip name {_renameClipOriginal}";
            ClearClipRename();
        }

        void ClearClipRename()
        {
            _renamingClip = -1;
            _renameClipValue = string.Empty;
            _renameClipOriginal = string.Empty;
            _focusClipRename = false;
            GUI.FocusControl(null);
            Repaint();
        }

        string UniqueClipName(string requestedName, int ignoredClipIndex)
        {
            string baseName = string.IsNullOrWhiteSpace(requestedName)
                ? $"Clip {ignoredClipIndex + 1}"
                : requestedName.Trim();
            string candidate = baseName;
            int suffix = 2;
            while (ClipNameExists(candidate, ignoredClipIndex))
                candidate = $"{baseName} {suffix++}";
            return candidate;
        }

        bool ClipNameExists(string candidate, int ignoredClipIndex)
        {
            for (int i = 0; i < _profile.Clips.Count; i++)
                if (i != ignoredClipIndex &&
                    string.Equals(_profile.Clips[i].Name, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        void DrawPreview(Rect rect)
        {
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 20f), "PREVIEW", _sectionStyle);
            GUI.Label(new Rect(rect.x + 186f, rect.y + 9f, 36f, 20f), "Zoom", _mutedStyle);
            _previewZoom = GUI.HorizontalSlider(new Rect(rect.x + 222f, rect.y + 14f, 86f, 14f), _previewZoom, 0.25f, 8f);
            GUI.Label(new Rect(rect.x + 312f, rect.y + 9f, 46f, 20f), $"{_previewZoom:F2}x", _mutedStyle);
            var canvas = new Rect(rect.x + 10f, rect.y + 54f, rect.width - 20f, rect.height - 66f);
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
                if (GUI.Button(new Rect(rect.x + 356f, rect.y + 7f, 52f, 22f),
                        new GUIContent("Reset", "Reset preview zoom and pan."),
                        EditorStyles.miniButton))
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
                if (GUI.Button(new Rect(rect.x + 412f, rect.y + 7f, 68f, 22f),
                        new GUIContent("Recenter",
                            "Keep zoom and center the sprite in the preview pane."),
                        EditorStyles.miniButton))
                {
                    RecenterPreview(localCanvas);
                }
            }
            var offsetModeRect = new Rect(rect.xMax - 126f, rect.y + 7f, 114f, 22f);
            var motionPathRect = new Rect(rect.xMax - 310f, rect.y + 7f, 90f, 22f);
            if (GUI.Button(motionPathRect,
                    new GUIContent(_showIndependentMotionPaths ? "Paths: On" : "Paths: Off",
                        "Toggle Independent Motion path lines when Preview Debug is on."),
                    EditorStyles.miniButton))
            {
                RecordWindowUndo("Toggle Independent Motion Paths");
                _showIndependentMotionPaths = !_showIndependentMotionPaths;
                _status = _showIndependentMotionPaths
                    ? "Independent Motion paths visible"
                    : "Independent Motion paths hidden";
                Repaint();
            }
            if (!string.IsNullOrEmpty(_selectedSocketName))
            {
                if (GUI.Button(new Rect(rect.xMax - 214f, rect.y + 7f, 82f, 22f),
                        new GUIContent("Frames…",
                            "Open the socket frame panel to copy position, rotation, and scale."),
                        EditorStyles.miniButton))
                {
                    OpenSocketInheritPanel(CurrentClip, _selectedSocketName, _selectedFrame);
                }
            }
            string offsetModeLabel = _previewOffsetMode == PreviewOffsetMode.Authored
                ? "View: Offsets"
                : "View: Centered";
            if (GUI.Button(offsetModeRect, new GUIContent(offsetModeLabel,
                    "Toggle between authored per-frame playback offsets and centered source cells."),
                EditorStyles.miniButton))
            {
                RecordWindowUndo("Change Sprite Offset Preview");
                _previewOffsetMode = _previewOffsetMode == PreviewOffsetMode.Authored
                    ? PreviewOffsetMode.Centered
                    : PreviewOffsetMode.Authored;
                _status = _previewOffsetMode == PreviewOffsetMode.Authored
                    ? "Preview applies authored frame offsets"
                    : "Preview centers the active frame";
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
            GUI.Label(new Rect(rect.x + 12f, rect.y + 31f, rect.width - 24f, 16f), frameText, _mutedStyle);

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

            if (_showHitboxes)
            {
                foreach (var box in BoxesFor(clip, state.Frame))
                {
                    bool selected = _selectedColliders.Contains(box);
                    if (box.Hidden && !selected)
                        continue;
                    Color color = selected
                        ? new Color(0.18f, 0.72f, 1f, box.Hidden ? 0.16f : 0.42f)
                        : new Color(1f, 0.27f, 0.25f, 0.34f);
                    DrawColliderUV(box, cell, color, selected);
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
                "BallForgeSpriteAnimatorPreview".GetHashCode(), FocusType.Keyboard, canvas);
            if (!_showPivot && _draggingPivot)
            {
                _draggingPivot = false;
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
                else if (_showPreviewDebug && TryHandleSocketContextClick(clip, state.Frame, cell))
                {
                }
                else if (_draggingColliderTransform)
                    HandleColliderTransformInput(previewControlId, cell, clip, state.Frame);
                else if (_draggingPivot)
                    HandlePivotInput(previewControlId, cell);
                else if (_draggingSocket)
                    HandleSocketManipulationInput(previewControlId, cell, clip, state.Frame);
                else if (_showHitboxes && Event.current.type == EventType.MouseDown &&
                         Event.current.button == 0 &&
                         HitSelectedColliderHandle(cell, Event.current.mousePosition) != ColliderHandleKind.None)
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
                else if (_showPivot && Event.current.type == EventType.MouseDown &&
                         Event.current.button == 0 &&
                         PivotHandleContains(cell, Event.current.mousePosition))
                    HandlePivotInput(previewControlId, cell);
                else
                {
                    bool selectionConsumed = HandlePreviewObjectSelectionInput(
                        previewControlId, cell, new Rect(0f, 0f, contentW, contentH),
                        clip, state.Frame, onionGhosts);
                    if (!selectionConsumed)
                    {
                        bool pivotConsumed = _showPivot &&
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
            if (_showPreviewDebug && !debugBlocksPreview && clip != null &&
                canvas.Contains(Event.current.mousePosition))
            {
                Vector2 contentMouse = Event.current.mousePosition - canvas.position + _previewScroll;
                TryHandleSocketContextClick(clip, state.Frame, cell, contentMouse);
            }
        }

        static Rect PreviewDebugToggleRect(Rect canvas)
            => new(canvas.xMax - 88f, canvas.yMax - 30f, 80f, 22f);

        static bool PreviewDebugToggleBlocksEditorInput(Rect canvas)
        {
            if (!PreviewDebugToggleRect(canvas).Contains(Event.current.mousePosition))
                return false;
            return Event.current.type is EventType.MouseDown or EventType.MouseUp
                or EventType.MouseDrag or EventType.MouseMove or EventType.ContextClick
                or EventType.ScrollWheel;
        }

        void FinishPreviewDebugToggle(Rect canvas, EventType previewEvent, bool debugBlocksPreview)
        {
            if (debugBlocksPreview)
                Event.current.type = previewEvent;
            DrawPreviewDebugToggle(canvas);
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

        void DrawInspector(Rect rect)
        {
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 20f), "INSPECTOR", _sectionStyle);
            PrepareInspectorUndo();
            // Always reserve the socket action bar so selecting a socket does not
            // shrink the inspector and jump the list under the next click.
            float top = 90f;
            if (!string.IsNullOrEmpty(_selectedSocketName) && CurrentClip != null)
            {
                var bar = new Rect(rect.x + 9f, rect.y + 32f, rect.width - 18f, 54f);
                DrawSelectedSocketBar(bar);
            }
            var area = new Rect(rect.x + 9f, rect.y + top, rect.width - 18f, rect.height - top - 10f);
            int colliderRows = CurrentClip == null ? 0 : CurrentFrameColliders(CurrentClip, _selectedFrame).Count;
            int socketRows = 0;
            if (CurrentClip?.Sockets != null)
                socketRows = CachedUniqueSocketNames(CurrentClip).Count;
            float socketExtra = 72f + socketRows * 48f + 340f +
                (_selectedSockets.Count == 1 ? 520f : 0f);
            var inspectorContent = new Rect(0f, 0f, area.width - 15f,
                Mathf.Max(area.height, 1640f + colliderRows * 26f + socketExtra));
            _inspectorScroll = GUI.BeginScrollView(area, _inspectorScroll, inspectorContent);
            GUILayout.BeginArea(inspectorContent);
            EditorGUI.BeginChangeCheck();

            SectionLabel("PLAYBACK");
            _playing = EditorGUILayout.Toggle("Preview Playing", _playing);
            _speed = EditorGUILayout.Slider("Playback Rate", _speed, 0.1f, 3f);
            if (GUILayout.Button("Reset Playback Rate to 1x"))
                _speed = 1f;

            GUILayout.Space(9f);
            SectionLabel("SHEET");
            var activeSheet = _profile.SheetAt(_selectedSheet);
            if (activeSheet != null)
            {
                string sheetName = EditorGUILayout.TextField("Name", activeSheet.Name ?? string.Empty);
                if (sheetName != activeSheet.Name)
                    activeSheet.Name = sheetName;
            }
            var newSheet = (Texture2D)EditorGUILayout.ObjectField("Texture", _profile.Sheet, typeof(Texture2D), false);
            if (newSheet != _profile.Sheet)
                AcceptSheetTexture(newSheet, promptIfSibling: _selectedSheet == 0);
            DrawSheetTextureInfo();
            _profile.Columns = Mathf.Max(1, EditorGUILayout.IntField("Columns", _profile.Columns));
            _profile.Rows = Mathf.Max(1, EditorGUILayout.IntField("Rows", _profile.Rows));
            using (new EditorGUILayout.HorizontalScope())
            {
                _profile.PixelsPerUnit = Mathf.Max(SpriteSheetProfile.MinPixelsPerUnit,
                    EditorGUILayout.FloatField("Pixels / Unit", _profile.PixelsPerUnit));
                using (new EditorGUI.DisabledScope(
                    Mathf.Approximately(_profile.PixelsPerUnit, SpriteSheetProfile.DefaultPixelsPerUnit)))
                {
                    if (ResetValueButton("Reset Pixels Per Unit to 100."))
                    {
                        RecordProfileUndo("Reset Sprite Pixels Per Unit");
                        _profile.PixelsPerUnit = SpriteSheetProfile.DefaultPixelsPerUnit;
                        _status = "Reset Pixels Per Unit to 100";
                    }
                }
            }
            DrawPixelsPerUnitSize();
            using (new EditorGUILayout.HorizontalScope())
            {
                _profile.Pivot = EditorGUILayout.Vector2Field("Pivot", _profile.Pivot);
                using (new EditorGUI.DisabledScope(_profile.Pivot == SpriteSheetProfile.DefaultPivot))
                {
                    if (ResetValueButton("Reset the pivot to centered (0.5, 0.5)."))
                    {
                        RecordProfileUndo("Reset Sprite Pivot");
                        _profile.Pivot = SpriteSheetProfile.DefaultPivot;
                        _status = "Reset pivot to center";
                    }
                }
                bool nextShowPivot = GUILayout.Toggle(_showPivot,
                    new GUIContent("Show Pivot",
                        "Draw the sheet pivot as a green dot in the preview. Drag it to move."),
                    GUILayout.Width(92f));
                if (nextShowPivot != _showPivot)
                {
                    RecordWindowUndo("Toggle Show Pivot");
                    _showPivot = nextShowPivot;
                    if (!_showPivot)
                    {
                        _draggingPivot = false;
                        _pivotSelected = false;
                    }
                }
            }
            if (GUILayout.Button("Auto-detect transparent grid"))
            {
                AutoDetect();
                WriteActiveSheetFromLegacy();
                RematchSheetsWorldSize(_selectedSheet);
            }

            GUILayout.Space(9f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("TIMELINE INPUT", _sectionStyle);
                GUILayout.FlexibleSpace();
                _showTimelineInputHelp = GUILayout.Toggle(_showTimelineInputHelp,
                    new GUIContent("?", "Show timeline input shortcuts."),
                    EditorStyles.miniButton, GUILayout.Width(22f));
            }
            var timelineRule = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(timelineRule, BorderColor);
            GUILayout.Space(3f);
            using (new EditorGUILayout.HorizontalScope())
            {
                _profile.TimelineHitShape = (SpriteTimelineHitShape)EditorGUILayout.EnumPopup(
                    "Thumbnail Hit Shape", _profile.TimelineHitShape);
                bool defaultHitShape = _profile.TimelineHitShape == SpriteTimelineHitShape.Circle &&
                    _profile.TimelineHitPolygon.Length == SpriteSheetProfile.DefaultTimelineHitPolygonVertices;
                using (new EditorGUI.DisabledScope(defaultHitShape))
                {
                    if (ResetValueButton("Restore circular thumbnail hit-testing and the default 8-point polygon."))
                    {
                        RecordProfileUndo("Reset Timeline Hit Shape");
                        _profile.TimelineHitShape = SpriteTimelineHitShape.Circle;
                        _profile.TimelineHitPolygon = SpriteSheetProfile.CreateRegularHitPolygon(
                            SpriteSheetProfile.DefaultTimelineHitPolygonVertices);
                        _status = "Reset timeline hit target to Circle";
                    }
                }
            }
            if (_profile.TimelineHitShape == SpriteTimelineHitShape.Polygon)
            {
                int oldCount = _profile.TimelineHitPolygon.Length;
                int newCount = EditorGUILayout.IntSlider("Polygon Vertices", oldCount, 3, 16);
                if (newCount != oldCount)
                    _profile.TimelineHitPolygon = SpriteSheetProfile.CreateRegularHitPolygon(newCount);
                for (int i = 0; i < _profile.TimelineHitPolygon.Length; i++)
                {
                    Vector2 point = EditorGUILayout.Vector2Field($"Point {i + 1}",
                        _profile.TimelineHitPolygon[i]);
                    _profile.TimelineHitPolygon[i] = new Vector2(
                        Mathf.Clamp01(point.x), Mathf.Clamp01(point.y));
                }
                if (GUILayout.Button(new GUIContent("Reset Regular Polygon",
                    "Replace edited polygon points with an evenly spaced regular polygon.")))
                {
                    RecordProfileUndo("Reset Timeline Hit Polygon");
                    _profile.TimelineHitPolygon = SpriteSheetProfile.CreateRegularHitPolygon(newCount);
                }
            }
            if (_showTimelineInputHelp)
            {
                EditorGUILayout.HelpBox(
                    "Drag a frame to reorder. Drag empty track to box-select. Drag the right edge to change hold. Drag the ruler or playhead to scrub. Middle-mouse pans. Shift = add/range, Ctrl/Cmd = toggle, Alt = subtract, Shift+Alt = intersect.",
                    MessageType.None);
            }

            var clip = CurrentClip;
            if (clip != null)
            {
                GUILayout.Space(9f);
                SectionLabel("CLIP");
                string oldName = clip.Name;
                clip.Name = DrawStringTextField("Name", clip.Name, "ClipName");
                if (oldName != clip.Name)
                    RenameHitboxClip(oldName, clip.Name);
                clip.Row = Mathf.Clamp(EditorGUILayout.IntField("Sheet Row", clip.Row), 0,
                    Mathf.Max(0, _profile.Rows - 1));
                using (new EditorGUILayout.HorizontalScope())
                {
                    clip.FrameRate = Mathf.Max(0.1f, EditorGUILayout.FloatField("Frame Rate", clip.FrameRate));
                    using (new EditorGUI.DisabledScope(
                        Mathf.Approximately(clip.FrameRate, SpriteClipDef.DefaultFrameRate)))
                    {
                        if (ResetValueButton("Reset the clip frame rate to 8 fps."))
                        {
                            RecordProfileUndo("Reset Sprite Clip Frame Rate");
                            clip.FrameRate = SpriteClipDef.DefaultFrameRate;
                            _status = "Reset clip frame rate to 8 fps";
                        }
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    clip.WrapMode = (byte)EditorGUILayout.Popup("Wrap Mode", clip.WrapMode,
                        new[] { "Loop", "Once", "Ping Pong", "Reverse Loop" });
                    using (new EditorGUI.DisabledScope(clip.WrapMode == SpriteClipDef.DefaultWrapMode))
                    {
                        if (ResetValueButton("Reset playback wrapping to Loop."))
                        {
                            RecordProfileUndo("Reset Sprite Clip Wrap Mode");
                            clip.WrapMode = SpriteClipDef.DefaultWrapMode;
                            _status = "Reset wrap mode to Loop";
                        }
                    }
                }
                clip.FacingGroup = DrawStringTextField(
                    new GUIContent("Facing Group", "Optional logical group name (e.g. Walk, Idle)."),
                    clip.FacingGroup, "FacingGroup");
                clip.Facing = (SpriteFacingDirection)EditorGUILayout.EnumPopup(
                    new GUIContent("Facing", "Direction variant inside the facing group."),
                    clip.Facing);

                bool gpuEligible = SpriteGpuEligibility.IsGpuEligible(clip, out string gpuReason);
                Color badgeColor = gpuEligible
                    ? new Color(0.17f, 0.5f, 0.2f, 0.92f)
                    : new Color(0.52f, 0.16f, 0.14f, 0.92f);
                var badgeRect = GUILayoutUtility.GetRect(1f, 24f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(badgeRect, badgeColor);
                DrawBorder(badgeRect, BorderColor, 1f);
                GUI.Label(badgeRect,
                    gpuEligible ? "  GPU clock OK" : "  CPU only",
                    EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(gpuReason, MessageType.None);

                GUILayout.Space(9f);
                int selectedCount = Mathf.Max(1, _selectedFrames.Count);
                SectionLabel(selectedCount > 1
                    ? $"FRAME {_selectedFrame + 1}  •  {selectedCount} selected"
                    : $"FRAME {_selectedFrame + 1}");
                clip.Frames[_selectedFrame] = Mathf.Clamp(
                    EditorGUILayout.IntField("Sheet Column", clip.Frames[_selectedFrame]),
                    0, Mathf.Max(0, _profile.Columns - 1));
                float duration = clip.FrameDurationScales[_selectedFrame] / clip.FrameRate;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginDisabledGroup(_timelineDragMode == TimelineDragMode.ResizeFrame);
                    EditorGUI.BeginChangeCheck();
                    duration = Mathf.Max(0.001f, EditorGUILayout.FloatField("Duration (sec)", duration));
                    if (EditorGUI.EndChangeCheck() && _timelineDragMode != TimelineDragMode.ResizeFrame)
                    {
                        RecordDiscreteUndo("Change Frame Duration");
                        clip.FrameDurationScales[_selectedFrame] = duration * clip.FrameRate;
                    }
                    EditorGUI.EndDisabledGroup();
                    using (new EditorGUI.DisabledScope(Mathf.Approximately(
                        clip.FrameDurationScales[_selectedFrame], SpriteClipDef.DefaultFrameDurationScale)))
                    {
                        if (ResetValueButton("Reset this frame to one normal frame interval."))
                        {
                            RecordProfileUndo("Reset Sprite Frame Duration");
                            clip.FrameDurationScales[_selectedFrame] = SpriteClipDef.DefaultFrameDurationScale;
                            _status = "Reset frame duration";
                        }
                    }
                }
                clip.OnionOffsets[_selectedFrame] = EditorGUILayout.Vector2Field(
                    new GUIContent("Position Offset (px)",
                        "Per-frame position offset in source pixels (baked to runtime)."),
                    clip.OnionOffsets[_selectedFrame]);
                clip.FrameScales[_selectedFrame] = EditorGUILayout.Vector2Field(
                    new GUIContent("Scale", "Per-frame local scale multiplier."),
                    clip.FrameScales[_selectedFrame]);
                clip.FrameRotations[_selectedFrame] = EditorGUILayout.FloatField(
                    new GUIContent("Rotation (deg)", "Per-frame local z rotation in degrees."),
                    clip.FrameRotations[_selectedFrame]);
                clip.FrameTweenModes[_selectedFrame] = (byte)(SpriteEaseMode)EditorGUILayout.EnumPopup(
                    new GUIContent("TRS Tween", "Easing from this frame's TRS key to the next frame."),
                    (SpriteEaseMode)clip.FrameTweenModes[_selectedFrame]);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("+ Frame After"))
                        InsertFrameAfter(clip);
                    using (new EditorGUI.DisabledScope(clip.Frames.Length <= 1))
                        if (GUILayout.Button(selectedCount > 1 ? "Remove Frames" : "Remove Frame"))
                            RemoveSelectedFrames(clip);
                }

                DrawEventMarkerInspector(clip);
                DrawSocketDrawKeyInspector(clip);
                DrawSocketInspector(clip);

                GUILayout.Space(9f);
                SectionLabel("ONION SKIN");
                _profile.OnionSkinEnabled = EditorGUILayout.Toggle("Enabled", _profile.OnionSkinEnabled);
                if (!_profile.OnionSkinEnabled)
                {
                    _selectedOnionFrame = -1;
                    _draggingOnion = false;
                }
                using (new EditorGUI.DisabledScope(!_profile.OnionSkinEnabled))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUILayout.VerticalScope())
                        {
                            GUILayout.Label("Past Frames (Left)", _mutedStyle);
                            _profile.OnionPastFrames = Mathf.Clamp(
                                EditorGUILayout.IntField(_profile.OnionPastFrames), 0,
                                Mathf.Max(0, clip.Frames.Length - 1));
                        }
                        using (new EditorGUILayout.VerticalScope())
                        {
                            GUILayout.Label("Future Frames (Right)", _mutedStyle);
                            _profile.OnionFutureFrames = Mathf.Clamp(
                                EditorGUILayout.IntField(_profile.OnionFutureFrames), 0,
                                Mathf.Max(0, clip.Frames.Length - 1));
                        }
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("All Past"))
                            _profile.OnionPastFrames = Mathf.Max(0, clip.Frames.Length - 1);
                        if (GUILayout.Button("All Future"))
                            _profile.OnionFutureFrames = Mathf.Max(0, clip.Frames.Length - 1);
                    }
                    _profile.ShowOnionLayerNumbers = EditorGUILayout.Toggle(
                        "Show Layer Numbers", _profile.ShowOnionLayerNumbers);
                    PreviewOffsetMode nextPreviewMode = (PreviewOffsetMode)EditorGUILayout.EnumPopup(
                        "Playback Preview", _previewOffsetMode);
                    if (nextPreviewMode != _previewOffsetMode)
                    {
                        RecordWindowUndo("Change Sprite Offset Preview");
                        _previewOffsetMode = nextPreviewMode;
                        _status = _previewOffsetMode == PreviewOffsetMode.Authored
                            ? "Preview applies authored frame offsets"
                            : "Preview centers the active frame";
                    }

                    bool validOnionSelection = OnionSelectionIsVisible(clip, _selectedFrame);
                    if (validOnionSelection)
                    {
                        GUILayout.Label(
                            $"Selected ghost  {SignedFrameDelta(_selectedOnionDelta)}  •  frame {_selectedOnionFrame + 1}",
                            EditorStyles.boldLabel);
                        clip.OnionOffsets[_selectedOnionFrame] = EditorGUILayout.Vector2Field(
                            "Playback Offset (px)", clip.OnionOffsets[_selectedOnionFrame]);
                        using (new EditorGUI.DisabledScope(
                            clip.OnionOffsets[_selectedOnionFrame] == Vector2.zero))
                        {
                            if (GUILayout.Button(new GUIContent("Recenter Selected",
                                "Reset this onion ghost offset to (0, 0).")))
                                RecenterOnion(clip, _selectedOnionFrame);
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "Click a ghost to select its frame. Drag or use arrow keys to align it; the source-pixel offset is baked into runtime playback.",
                            MessageType.None);
                    }

                    bool onionDefaults = _profile.OnionPastFrames == SpriteSheetProfile.DefaultOnionFrameCount &&
                        _profile.OnionFutureFrames == SpriteSheetProfile.DefaultOnionFrameCount &&
                        _profile.ShowOnionLayerNumbers;
                    using (new EditorGUI.DisabledScope(onionDefaults))
                    {
                        if (GUILayout.Button(new GUIContent("Reset Onion Defaults",
                            "Restore 3 past frames, 3 future frames, and visible layer numbers.")))
                        {
                            RecordProfileUndo("Reset Onion Skin Settings");
                            _profile.OnionPastFrames = SpriteSheetProfile.DefaultOnionFrameCount;
                            _profile.OnionFutureFrames = SpriteSheetProfile.DefaultOnionFrameCount;
                            _profile.ShowOnionLayerNumbers = true;
                        }
                    }
                }

                GUILayout.Space(9f);
                SectionLabel("COLLIDER CREATION");
                _showHitboxes = EditorGUILayout.Toggle("Show Colliders", _showHitboxes);
                if (!_showHitboxes)
                {
                    _colliderCreationMode = ColliderCreationMode.None;
                    _draggingBox = false;
                    ClearPolygonDraft();
                    _selectedColliders.Clear();
                }
                using (new EditorGUI.DisabledScope(!_showHitboxes))
                {
                    GUILayout.Label("Select a shape before placing it", _mutedStyle);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawColliderModeButton(ColliderCreationMode.Square, "Square", EditorStyles.miniButtonLeft);
                        DrawColliderModeButton(ColliderCreationMode.Circle, "Circle", EditorStyles.miniButtonMid);
                        DrawColliderModeButton(ColliderCreationMode.Polygon, "Polygon", EditorStyles.miniButtonRight);
                    }
                    _continuousColliderPlacement = EditorGUILayout.Toggle(
                        "Continuous Placement", _continuousColliderPlacement);
                    _newHitboxId = Mathf.Clamp(EditorGUILayout.IntField(
                        "New Collider ID", _newHitboxId), 1, 255);

                    using (new EditorGUI.DisabledScope(_colliderCreationMode == ColliderCreationMode.None))
                    {
                        if (GUILayout.Button("Cancel Creation Tool"))
                            CancelColliderCreation("Collider creation cancelled");
                    }
                }
                EditorGUILayout.HelpBox(
                    _colliderCreationMode switch
                    {
                        ColliderCreationMode.None =>
                            "Choose a shape to create, or click existing colliders to select them. Drag empty preview space for marquee. Shift adds, Ctrl/Cmd toggles, Alt subtracts, Shift+Alt intersects.",
                        ColliderCreationMode.Polygon =>
                            "Click to draw polygon vertices. Click the first point, double-click, or press Enter to close. Right-click/Backspace removes the last point; Escape cancels.",
                        _ =>
                            "Click for a default-size collider or drag from its center to size it. Right-click cancels the active creation tool.",
                    },
                    MessageType.None);

                GUILayout.Space(7f);
                DrawColliderList(clip);
            }

            if (EditorGUI.EndChangeCheck())
            {
                WriteActiveSheetFromLegacy();
                RematchSheetsWorldSize(_selectedSheet);
                SaveDirty();
            }
            GUILayout.EndArea();
            GUI.EndScrollView();
            ConsumeInspectorPointer(rect);
        }

        static void ConsumeInspectorPointer(Rect inspectorRect)
        {
            var evt = Event.current;
            if (evt == null || !inspectorRect.Contains(evt.mousePosition))
                return;
            if (evt.type is EventType.MouseDown or EventType.MouseUp or EventType.MouseDrag
                or EventType.ScrollWheel or EventType.ContextClick)
                evt.Use();
        }

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
            string markerSelection = _selectedEventFrame >= 0
                ? $"   •   marker {EventAuthoredTime(clip, _selectedEventFrame):F3}s selected"
                : string.Empty;
            const float addFrameWidth = 82f;
            const float deleteEmptyWidth = 148f;
            const float headerBtnGap = 4f;
            var deleteEmptyRect = new Rect(rect.xMax - deleteEmptyWidth - 8f, rect.y + 7f, deleteEmptyWidth, 20f);
            var addFrameRect = new Rect(deleteEmptyRect.x - addFrameWidth - headerBtnGap, rect.y + 7f, addFrameWidth, 20f);
            GUI.Label(new Rect(rect.x + 274f, rect.y + 10f,
                    Mathf.Max(40f, addFrameRect.x - rect.x - 282f), 16f),
                $"{clip.Frames.Length} frames   •   {total:F3}s   •   drag = marquee   •   Alt+drag image = reorder   •   frame edge = duration   •   right-click lane = event{markerSelection}",
                _mutedStyle);
            int emptyFrameCount = CountEmptyFrames(clip);
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

            for (int i = 0; i < frameCount; i++)
            {
                if (clip.EventIds[i] == 0)
                    continue;
                float markerTime = _timelineDragMode == TimelineDragMode.Event &&
                                   _dragEventSourceFrame == i
                    ? _dragEventAuthoredTime
                    : frameTimes[i] + Mathf.Clamp01(clip.EventNormalizedTimes[i]) * durations[i];
                float markerX = 48f + markerTime * pixelsPerSecond;
                Color markerColor = EventMarkerColor(clip.EventIds[i]);
                Color guideColor = markerColor;
                guideColor.a = 0.38f;
                EditorGUI.DrawRect(new Rect(markerX - 0.5f, TimelineEventLaneY, 1f, 145f), guideColor);
                if (i == _selectedEventFrame)
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
                GUI.Label(new Rect(card.x + 6f, card.y + 85f, card.width - 12f, 14f),
                    clip.EventIds[i] == 0 ? $"column {clip.Frames[i]}" : EventName(clip.EventIds[i]),
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

            GUI.Label(new Rect(startRect.xMax + 8f, rect.y + 10f,
                    Mathf.Max(40f, captureRect.x - startRect.xMax - 16f), 16f),
                $"{_socketPreviewTime:0.###}s / {duration:0.###}s  •  {_profile.SocketMotions.Count} track{Plural(_profile.SocketMotions.Count)}",
                _mutedStyle);

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
                if (GUI.Button(labelRect,
                        new GUIContent(
                            $"{track.SocketName}\n{track.Keys.Count} keys  •  master clock",
                            "Select this independent socket track."),
                        _mutedWrapStyle))
                    SelectPreviewSocket(track.SocketName, SelectionOp.Replace);
                var trackLabelEvent = Event.current;
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
                    CommitEventMove(clip, _dragEventSourceFrame, _dragEventId,
                        _dragEventAuthoredTime);
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

            int markerFrame = EventMarkerAt(clip, eventXs, mouse);
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

            if (evt.type == EventType.MouseDown && evt.button == 0 && markerFrame >= 0)
            {
                if (_timelineDragMode != TimelineDragMode.None)
                    CommitTimelineDrag(clip, mouse);
                float markerTime = EventAuthoredTime(clip, markerFrame);
                SelectEventMarker(clip, markerFrame, markerTime);
                BeginTimelineDrag(controlId, TimelineDragMode.Event, mouse);
                _dragEventSourceFrame = markerFrame;
                _dragEventId = clip.EventIds[markerFrame];
                _dragEventAuthoredTime = markerTime;
                _eventDragMoved = false;
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 1 &&
                mouse.y >= TimelineEventLaneY && mouse.y < TimelineDrawLaneY && mouse.x >= 48f)
            {
                if (markerFrame >= 0)
                    SelectEventMarker(clip, markerFrame, EventAuthoredTime(clip, markerFrame));
                ShowTimelineEventMenu(clip, markerFrame >= 0 ? eventXs[markerFrame] : mouse.x,
                    total, pixelsPerSecond);
                evt.Use();
                Repaint();
                return;
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
                                   float pixelsPerSecond)
        {
            float authoredTime = Mathf.Clamp(
                (contentX - 48f) / pixelsPerSecond,
                0f,
                Mathf.Max(0f, total - 0.0001f));
            int frame = AuthoredFrameAtTime(clip, authoredTime, out float normalizedTime);
            SelectOnlyFrame(frame);
            _selectedEventFrame = clip.EventIds[frame] == 0 ? -1 : frame;
            ClearColliderSelection();
            _selectedOnionFrame = -1;
            _previewTime = PreviewTimeForAuthoredTime(clip, authoredTime);
            _playing = false;

            var menu = new GenericMenu();
            foreach (var definition in _profile.Events)
            {
                if (definition == null || definition.Id == 0) continue;
                byte eventId = definition.Id;
                string eventName = string.IsNullOrWhiteSpace(definition.Name)
                    ? $"Event {eventId}"
                    : definition.Name;
                menu.AddItem(new GUIContent($"Add Event Marker/{eventName}"),
                    clip.EventIds[frame] == eventId,
                    () => SetFrameEvent(clip, frame, eventId, normalizedTime));
            }
            menu.AddItem(new GUIContent("Add Event Marker/New Event..."), false, () =>
            {
                byte eventId = NextEventId();
                if (eventId == 0)
                {
                    _status = "All event IDs are already in use";
                    return;
                }
                RecordProfileUndo("Create Sprite Animation Event");
                _profile.Events.Add(new SpriteEventDef
                {
                    Id = eventId,
                    Name = $"Event {eventId}",
                    Color = Color.HSVToRGB(Mathf.Repeat(eventId * 0.137f, 1f), 0.72f, 1f),
                });
                SetFrameEvent(clip, frame, eventId, normalizedTime, false);
            });
            if (clip.EventIds[frame] != 0)
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Clear Event Marker"), false,
                    () => SetFrameEvent(clip, frame, 0));
            }
            menu.ShowAsContext();
        }

        void SetFrameEvent(SpriteClipDef clip, int frame, byte eventId,
                           float normalizedTime = 0f, bool recordUndo = true)
        {
            if (frame < 0 || frame >= clip.EventIds.Length)
                return;
            if (recordUndo)
                RecordProfileUndo(eventId == 0 ? "Clear Sprite Animation Event" : "Add Sprite Animation Event");
            clip.EventIds[frame] = eventId;
            clip.EventNormalizedTimes[frame] = eventId == 0 ? 0f : Mathf.Clamp01(normalizedTime);
            _selectedFrame = frame;
            _selectedEventFrame = eventId == 0 ? -1 : frame;
            ClearColliderSelection();
            _selectedOnionFrame = -1;
            float authoredTime = EventAuthoredTime(clip, frame);
            _previewTime = PreviewTimeForAuthoredTime(clip, authoredTime);
            _status = eventId == 0
                ? $"Cleared event marker on frame {frame + 1}"
                : $"Added {EventName(eventId)} at {authoredTime:F3}s";
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

        void CommitEventMove(SpriteClipDef clip, int sourceFrame, byte eventId,
                             float authoredTime)
        {
            if (sourceFrame < 0 || sourceFrame >= clip.EventIds.Length || eventId == 0)
                return;

            int destinationFrame = AuthoredFrameAtTime(clip, authoredTime, out float destinationNormalizedTime);
            RecordProfileUndo("Move Sprite Animation Event");

            if (destinationFrame != sourceFrame)
            {
                byte displacedId = clip.EventIds[destinationFrame];
                float displacedTime = clip.EventNormalizedTimes[destinationFrame];
                clip.EventIds[sourceFrame] = displacedId;
                clip.EventNormalizedTimes[sourceFrame] = displacedId == 0 ? 0f : displacedTime;
            }

            clip.EventIds[destinationFrame] = eventId;
            clip.EventNormalizedTimes[destinationFrame] = Mathf.Clamp01(destinationNormalizedTime);
            _selectedEventFrame = destinationFrame;
            _selectedFrame = destinationFrame;
            _previewTime = PreviewTimeForAuthoredTime(clip, authoredTime);
            _status = destinationFrame == sourceFrame
                ? $"Moved {EventName(eventId)} to {authoredTime:F3}s"
                : $"Moved {EventName(eventId)} to frame {destinationFrame + 1} at {authoredTime:F3}s";
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

            if (evt.type == EventType.MouseDown && evt.button == 1 && cell.Contains(evt.mousePosition))
            {
                CancelColliderCreation("Collider creation cancelled");
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && cell.Contains(evt.mousePosition))
            {
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
                };
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

            if (evt.type == EventType.MouseMove)
            {
                bool wasHovering = _polygonHasHover;
                _polygonHasHover = cell.Contains(evt.mousePosition);
                if (_polygonHasHover)
                    _polygonHoverUV = ScreenPointToCellUV(evt.mousePosition, cell);
                if (wasHovering || _polygonHasHover)
                    Repaint();
            }

            if (evt.type == EventType.MouseDown && evt.button == 1 && cell.Contains(evt.mousePosition))
            {
                if (_polygonDraftUV.Count > 0)
                    RemoveLastPolygonVertex();
                else
                    CancelColliderCreation("Polygon creation cancelled");
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type != EventType.MouseDown || evt.button != 0 || !cell.Contains(evt.mousePosition))
                return;

            _playing = false;
            _selectedFrame = frame;
            _selectedEventFrame = -1;
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

        void DrawPolygonDraft(Rect cell)
        {
            if (_polygonDraftUV.Count == 0)
                return;

            var line = new List<Vector3>(_polygonDraftUV.Count + 1);
            foreach (Vector2 pointUV in _polygonDraftUV)
            {
                Vector2 point = CellUVToScreenPoint(pointUV, cell);
                line.Add(new Vector3(point.x, point.y));
            }

            if (_polygonHasHover)
            {
                Vector2 hover = CellUVToScreenPoint(_polygonHoverUV, cell);
                if (_polygonDraftUV.Count >= 3 &&
                    Vector2.Distance(hover, CellUVToScreenPoint(_polygonDraftUV[0], cell)) <= 11f)
                    hover = CellUVToScreenPoint(_polygonDraftUV[0], cell);
                line.Add(new Vector3(hover.x, hover.y));
            }

            Handles.BeginGUI();
            Handles.color = new Color(1f, 0.55f, 0.2f, 0.98f);
            if (line.Count >= 2)
                Handles.DrawAAPolyLine(2.5f, line.ToArray());
            for (int i = 0; i < _polygonDraftUV.Count; i++)
            {
                Vector2 point = CellUVToScreenPoint(_polygonDraftUV[i], cell);
                Handles.color = i == 0 && _polygonDraftUV.Count >= 3
                    ? new Color(0.3f, 1f, 0.55f, 1f)
                    : new Color(1f, 0.68f, 0.25f, 1f);
                Handles.DrawSolidDisc(point, Vector3.forward, i == 0 ? 5f : 3.5f);
            }
            Handles.EndGUI();
        }

        void CompletePolygonCollider(SpriteClipDef clip, int frame)
        {
            if (_polygonDraftUV.Count < 3)
            {
                _status = "A polygon needs at least 3 vertices";
                return;
            }

            Vector2 min = _polygonDraftUV[0];
            Vector2 max = _polygonDraftUV[0];
            foreach (Vector2 point in _polygonDraftUV)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            Vector2 size = max - min;
            if (size.x < 0.001f || size.y < 0.001f)
            {
                _status = "Polygon needs width and height before it can close";
                return;
            }

            var localPoints = new Vector2[_polygonDraftUV.Count];
            for (int i = 0; i < _polygonDraftUV.Count; i++)
            {
                Vector2 point = _polygonDraftUV[i];
                localPoints[i] = new Vector2(
                    (point.x - min.x) / size.x,
                    (point.y - min.y) / size.y);
            }

            var definition = new FrameBoxDef
            {
                ClipName = clip.Name,
                FrameIndex = frame,
                Id = (byte)_newHitboxId,
                Shape = SpriteColliderShape.Polygon,
                RectUV = new Rect(min, size),
                PolygonUV = localPoints,
            };
            AddCreatedCollider(definition, frame);
            ClearPolygonDraft();
            if (!_continuousColliderPlacement)
                _colliderCreationMode = ColliderCreationMode.None;
        }

        void AddCreatedCollider(FrameBoxDef definition, int frame)
        {
            RecordProfileUndo("Create Sprite Collider");
            _profile.Hitboxes.Add(definition);
            _selectedColliders.Clear();
            _selectedColliders.Add(definition);
            _selectedEventFrame = -1;
            _status = $"Created {definition.Shape} collider on frame {frame + 1}";
            SaveDirty();
        }

        void RemoveLastPolygonVertex()
        {
            if (_polygonDraftUV.Count == 0)
                return;
            _polygonDraftUV.RemoveAt(_polygonDraftUV.Count - 1);
            _status = _polygonDraftUV.Count == 0
                ? "Polygon is empty; click to place the first vertex"
                : $"Removed vertex; {_polygonDraftUV.Count} remaining";
        }

        void ClearPolygonDraft()
        {
            _polygonDraftUV.Clear();
            _polygonHasHover = false;
        }

        void CancelColliderCreation(string status)
        {
            _draggingBox = false;
            _colliderCreationMode = ColliderCreationMode.None;
            ClearPolygonDraft();
            if (!string.IsNullOrEmpty(status))
                _status = status;
        }

        static Vector2 ScreenPointToCellUV(Vector2 point, Rect cell)
        {
            return new Vector2(
                Mathf.Clamp01((point.x - cell.x) / Mathf.Max(1f, cell.width)),
                Mathf.Clamp01((point.y - cell.y) / Mathf.Max(1f, cell.height)));
        }

        static Vector2 CellUVToScreenPoint(Vector2 pointUV, Rect cell)
        {
            return new Vector2(
                cell.x + pointUV.x * cell.width,
                cell.y + pointUV.y * cell.height);
        }

        bool HandlePreviewObjectSelectionInput(int controlId, Rect cell, Rect canvasContent, SpriteClipDef clip, int frame,
                                          List<OnionGhostLayout> ghosts)
        {
            var evt = Event.current;
            bool ownsDrag = _colliderMarqueePending &&
                            (GUIUtility.hotControl == _previewMarqueeHotControl ||
                             GUIUtility.hotControl == controlId ||
                             GUIUtility.hotControl == 0);

            if (ownsDrag && GUIUtility.hotControl == 0 && _previewMarqueeHotControl != 0)
                GUIUtility.hotControl = _previewMarqueeHotControl;

            if (evt.type == EventType.MouseDrag && ownsDrag)
            {
                if (!_draggingColliderMarquee &&
                    Vector2.Distance(_colliderMarqueeStart, evt.mousePosition) >= 4f)
                    _draggingColliderMarquee = true;
                if (_draggingColliderMarquee)
                    _colliderMarqueeRect = RectFromPoints(_colliderMarqueeStart, evt.mousePosition);
                // Do not mutate selection here. Showing/hiding selection gizmos changes
                // IMGUI's control allocation and made the marquee stutter or lose capture.
                // Resolve the final box once on MouseUp instead.
                evt.Use();
                Repaint();
                return true;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0 && ownsDrag)
            {
                if (_draggingColliderMarquee)
                {
                    SelectPreviewObjectsInMarquee(clip, frame, cell, _colliderMarqueeRect,
                        _previewMarqueeOp);
                }
                else
                {
                    if (_previewMarqueeOp == SelectionOp.Replace)
                        ClearPreviewObjectSelection();
                    SelectOnionAtPoint(clip, ghosts, evt.mousePosition);
                }

                EndColliderMarquee(controlId);
                _status = PreviewSelectionStatus("Marquee selected");
                evt.Use();
                Repaint();
                return true;
            }

            // Marquee starts on the whole preview canvas, not only the fitted sprite cell.
            if (evt.type != EventType.MouseDown)
                return false;
            if (!canvasContent.Contains(evt.mousePosition) && !cell.Contains(evt.mousePosition))
                return false;

            FrameBoxDef found = _showHitboxes ? FindColliderAt(clip, frame, cell, evt.mousePosition) : null;
            if (found != null)
            {
                _playing = false;
                _selectedFrame = frame;
                _selectedEventFrame = -1;
                _selectedOnionFrame = -1;
                GUIUtility.keyboardControl = controlId;

                if (evt.button == 0)
                {
                    var op = ReadSelectionOp(evt);
                    SelectCollider(found, op);
                    if (op == SelectionOp.Replace)
                        BeginColliderTransform(controlId, found, ColliderHandleKind.Body, cell, evt.mousePosition);
                    evt.Use();
                    Repaint();
                    return true;
                }

                if (evt.button == 1)
                {
                    if (!_selectedColliders.Contains(found))
                    {
                        _selectedColliders.Clear();
                        _selectedColliders.Add(found);
                        ClearSocketSelection();
                    }
                    ShowColliderContextMenu(clip, frame, found);
                    evt.Use();
                    Repaint();
                    return true;
                }
            }

            if (evt.button != 0)
                return false;

            // An exact onion badge or the already-selected onion owns direct manipulation.
            if (_profile.OnionSkinEnabled && OnionPointerHasPriority(ghosts, evt.mousePosition))
                return false;

            _playing = false;
            _colliderMarqueePending = true;
            _draggingColliderMarquee = false;
            _previewMarqueeOp = ReadSelectionOp(evt);
            _colliderMarqueeStart = evt.mousePosition;
            _colliderMarqueeRect = new Rect(evt.mousePosition, Vector2.zero);
            CapturePreviewMarqueeBaseline();
            CapturePreviewMarqueePins(clip, frame, cell);
            _previewMarqueeHotControl = controlId;
            GUIUtility.hotControl = controlId;
            GUIUtility.keyboardControl = controlId;
            evt.Use();
            return true;
        }

        void ShowColliderContextMenu(SpriteClipDef clip, int frame, FrameBoxDef clicked)
        {
            int selectedCount = _selectedColliders.Count;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(selectedCount > 1
                    ? $"Delete Selected Colliders ({selectedCount})"
                    : $"Delete {clicked.Shape} Collider"),
                false, DeleteSelectedColliders);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Select All Colliders on Frame"), false,
                () => SelectAllFrameColliders(clip, frame));
            menu.AddItem(new GUIContent("Delete All Colliders on Frame"), false,
                () => DeleteAllFrameColliders(clip, frame));
            menu.ShowAsContext();
        }

        FrameBoxDef FindColliderAt(SpriteClipDef clip, int frame, Rect cell, Vector2 point)
        {
            FrameBoxDef found = null;
            foreach (var box in BoxesFor(clip, frame))
            {
                if (box.Hidden)
                    continue;
                if (ColliderContains(box, cell, point))
                    found = box;
            }
            return found;
        }

        void SelectCollider(FrameBoxDef box, SelectionOp op)
        {
            if (box == null)
                return;
            switch (op)
            {
                case SelectionOp.Add:
                    _selectedColliders.Add(box);
                    break;
                case SelectionOp.Toggle:
                    if (!_selectedColliders.Add(box))
                        _selectedColliders.Remove(box);
                    break;
                case SelectionOp.Subtract:
                    _selectedColliders.Remove(box);
                    break;
                case SelectionOp.Intersect:
                    bool keep = _selectedColliders.Contains(box);
                    ClearColliderSelection();
                    if (keep)
                        _selectedColliders.Add(box);
                    break;
                default:
                    ClearColliderSelection();
                    _selectedColliders.Add(box);
                    break;
            }
            _status = PreviewSelectionStatus();
        }

        void SelectColliderFromList(List<FrameBoxDef> colliders, int index, SelectionOp op)
        {
            if (colliders == null || index < 0 || index >= colliders.Count)
                return;
            if (op is SelectionOp.Range or SelectionOp.RangeAdd)
            {
                if (_colliderListAnchor < 0 || _colliderListAnchor >= colliders.Count)
                    _colliderListAnchor = index;
                int a = Mathf.Min(_colliderListAnchor, index);
                int b = Mathf.Max(_colliderListAnchor, index);
                if (op == SelectionOp.Range)
                    ClearColliderSelection();
                for (int i = a; i <= b; i++)
                    _selectedColliders.Add(colliders[i]);
                if (_colliderListAnchor < 0)
                    _colliderListAnchor = index;
                _status = PreviewSelectionStatus();
                return;
            }

            SelectCollider(colliders[index], op);
            if (op is not (SelectionOp.Subtract or SelectionOp.Intersect))
                _colliderListAnchor = index;
        }

        void CapturePreviewMarqueeBaseline()
        {
            _previewMarqueeColliderBaseline.Clear();
            foreach (var box in _selectedColliders)
                _previewMarqueeColliderBaseline.Add(box);
            _previewMarqueeSocketBaseline.Clear();
            foreach (string name in _selectedSockets)
                _previewMarqueeSocketBaseline.Add(name);
        }

        void CapturePreviewMarqueePins(SpriteClipDef clip, int frame, Rect cell)
        {
            _previewMarqueeSocketNames.Clear();
            _previewMarqueeSocketPins.Clear();
            if (!_showPreviewDebug || clip?.Sockets == null)
                return;
            var names = CachedUniqueSocketNames(clip);
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (!TryGetPreviewSocketPose(clip, name, frame, out var position, out _, out _, out _))
                    continue;
                _previewMarqueeSocketNames.Add(SpriteSocketKeys.CanonicalName(name));
                _previewMarqueeSocketPins.Add(SocketToScreen(position, cell));
            }
        }

        void SelectPreviewObjectsInMarquee(SpriteClipDef clip, int frame, Rect cell, Rect marquee,
            SelectionOp op)
        {
            _selectionScratchColliders.Clear();
            _selectionScratchNames.Clear();

            if (_showHitboxes)
            {
                foreach (var box in BoxesFor(clip, frame))
                {
                    if (box.Hidden)
                        continue;
                    if (marquee.Overlaps(ColliderWorldAabb(box, cell), true))
                        _selectionScratchColliders.Add(box);
                }
            }

            const float pin = 14f;
            for (int i = 0; i < _previewMarqueeSocketPins.Count; i++)
            {
                Vector2 p = _previewMarqueeSocketPins[i];
                var hit = new Rect(p.x - pin, p.y - pin, pin * 2f, pin * 2f);
                if (marquee.Overlaps(hit, true))
                    _selectionScratchNames.Add(_previewMarqueeSocketNames[i]);
            }

            ApplyMarqueeOnto(_selectedColliders, _previewMarqueeColliderBaseline,
                _selectionScratchColliders, op);
            ApplyMarqueeOnto(_selectedSockets, _previewMarqueeSocketBaseline,
                _selectionScratchNames, op);

            _selectedEventFrame = -1;
            _selectedOnionFrame = -1;
            SyncSocketPrimaryFromSelection();
        }

        string PreviewSelectionStatus(string prefix = "Selected")
        {
            int colliders = _selectedColliders.Count;
            int sockets = _selectedSockets.Count;
            if (colliders == 0 && sockets == 0)
                return "Preview selection cleared";
            if (sockets == 0)
                return $"{prefix} {colliders} collider{(colliders == 1 ? string.Empty : "s")}";
            if (colliders == 0)
                return $"{prefix} {sockets} socket{(sockets == 1 ? string.Empty : "s")}";
            return $"{prefix} {colliders} collider{(colliders == 1 ? string.Empty : "s")} and {sockets} socket{(sockets == 1 ? string.Empty : "s")}";
        }

        void EndColliderMarquee(int controlId)
        {
            _colliderMarqueePending = false;
            _draggingColliderMarquee = false;
            _colliderMarqueeRect = default;
            _previewMarqueeColliderBaseline.Clear();
            _previewMarqueeSocketBaseline.Clear();
            _previewMarqueeSocketNames.Clear();
            _previewMarqueeSocketPins.Clear();
            if (GUIUtility.hotControl == controlId ||
                (_previewMarqueeHotControl != 0 &&
                 GUIUtility.hotControl == _previewMarqueeHotControl))
                GUIUtility.hotControl = 0;
            _previewMarqueeHotControl = 0;
        }

        void SelectOnionAtPoint(SpriteClipDef clip, List<OnionGhostLayout> ghosts, Vector2 point)
        {
            if (!_profile.OnionSkinEnabled)
            {
                _selectedOnionFrame = -1;
                return;
            }

            OnionGhostLayout? hit = FindOnionGhostAt(ghosts, point);
            if (!hit.HasValue)
            {
                _selectedOnionFrame = -1;
                return;
            }

            var ghost = hit.Value;
            _selectedOnionFrame = ghost.Frame;
            _selectedOnionDelta = ghost.Delta;
            _selectedEventFrame = -1;
            _status = $"Selected onion {SignedFrameDelta(ghost.Delta)} (frame {ghost.Frame + 1}); drag again to move";
        }

        bool OnionPointerHasPriority(List<OnionGhostLayout> ghosts, Vector2 point)
        {
            if (_profile.ShowOnionLayerNumbers)
                foreach (var ghost in ghosts)
                    if (ghost.BadgeRect.Contains(point))
                        return true;
            foreach (var ghost in ghosts)
                if (ghost.Frame == _selectedOnionFrame && ghost.SpriteRect.Contains(point))
                    return true;
            return false;
        }

        static Rect RectFromPoints(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(
                Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        List<OnionGhostLayout> BuildOnionGhostLayouts(SpriteClipDef clip, int currentFrame, Rect cell)
        {
            // Draw far layers first so nearer ghosts and their badges remain legible.
            _onionGhostLayouts.Clear();
            if (!_profile.OnionSkinEnabled)
                return _onionGhostLayouts;

            int greatestDistance = Mathf.Min(
                Mathf.Max(_profile.OnionPastFrames, _profile.OnionFutureFrames),
                Mathf.Max(0, clip.Frames.Length - 1));
            for (int distance = greatestDistance; distance >= 1; distance--)
            {
                if (distance <= _profile.OnionPastFrames)
                    AddOnionGhostLayout(clip, currentFrame, -distance, cell);
                if (distance <= _profile.OnionFutureFrames)
                    AddOnionGhostLayout(clip, currentFrame, distance, cell);
            }
            return _onionGhostLayouts;
        }

        void AddOnionGhostLayout(SpriteClipDef clip, int currentFrame, int delta, Rect cell)
        {
            int frame = currentFrame + delta;
            if (frame < 0 || frame >= clip.Frames.Length)
                return;

            Vector2 screenOffset = SourcePixelsToScreenOffset(clip.OnionOffsets[frame], cell);
            var spriteRect = new Rect(cell.position + screenOffset, cell.size);
            float badgeCenterX = Mathf.Clamp(
                spriteRect.center.x + delta * 20f,
                cell.xMin + 16f,
                cell.xMax - 16f);
            float badgeY = Mathf.Clamp(spriteRect.y + 6f, cell.y + 4f, cell.yMax - 23f);
            var badgeRect = new Rect(badgeCenterX - 15f, badgeY, 30f, 19f);
            _onionGhostLayouts.Add(new OnionGhostLayout(
                frame, delta, spriteRect, badgeRect, OnionColor(delta)));
        }

        void DrawOnionGhostSprites(SpriteClipDef clip, List<OnionGhostLayout> ghosts)
        {
            foreach (var ghost in ghosts)
            {
                DrawCellTinted(_profile.Sheet, CellIndexOf(clip, ghost.Frame), ghost.SpriteRect, ghost.Color);
                if (ghost.Frame == _selectedOnionFrame)
                    DrawBorder(ghost.SpriteRect, new Color(ghost.Color.r, ghost.Color.g, ghost.Color.b, 0.95f), 2f);
            }
        }

        void DrawOnionGhostBadges(List<OnionGhostLayout> ghosts)
        {
            if (!_profile.ShowOnionLayerNumbers)
                return;

            foreach (var ghost in ghosts)
            {
                Color badge = ghost.Color;
                badge.a = ghost.Frame == _selectedOnionFrame ? 0.95f : 0.72f;
                EditorGUI.DrawRect(ghost.BadgeRect, badge);
                DrawBorder(ghost.BadgeRect,
                    ghost.Frame == _selectedOnionFrame ? Color.white : new Color(1f, 1f, 1f, 0.45f),
                    ghost.Frame == _selectedOnionFrame ? 2f : 1f);
                GUI.Label(ghost.BadgeRect, SignedFrameDelta(ghost.Delta), _onionBadgeStyle);
                EditorGUIUtility.AddCursorRect(ghost.BadgeRect, MouseCursor.MoveArrow);
            }
        }

        bool HandleOnionInput(int controlId, Rect cell, SpriteClipDef clip, int currentFrame,
                              List<OnionGhostLayout> ghosts)
        {
            // No polling: mouse and keyboard events mutate only the explicitly selected layer.
            var evt = Event.current;
            bool validSelection = _selectedOnionFrame >= 0 &&
                _selectedOnionFrame < clip.Frames.Length && _selectedOnionFrame != currentFrame;

            if (evt.type == EventType.KeyDown && validSelection && GUIUtility.keyboardControl == controlId)
            {
                Vector2 direction = evt.keyCode switch
                {
                    KeyCode.LeftArrow => Vector2.left,
                    KeyCode.RightArrow => Vector2.right,
                    KeyCode.UpArrow => Vector2.up,
                    KeyCode.DownArrow => Vector2.down,
                    _ => Vector2.zero,
                };
                if (direction != Vector2.zero)
                {
                    float step = evt.shift ? 5f : 1f;
                    RecordProfileUndo("Nudge Onion Skin Offset");
                    clip.OnionOffsets[_selectedOnionFrame] += direction * step;
                    _status = $"Onion {SignedFrameDelta(_selectedOnionDelta)} offset {clip.OnionOffsets[_selectedOnionFrame]} px";
                    SaveDirty();
                    evt.Use();
                    Repaint();
                    return true;
                }
            }

            if (evt.type == EventType.MouseDown && evt.button == 1 && validSelection)
            {
                foreach (var ghost in ghosts)
                {
                    if (ghost.Frame != _selectedOnionFrame) continue;
                    if (!ghost.SpriteRect.Contains(evt.mousePosition) &&
                        !ghost.BadgeRect.Contains(evt.mousePosition)) continue;
                    RecenterOnion(clip, ghost.Frame);
                    evt.Use();
                    Repaint();
                    return true;
                }
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && cell.Contains(evt.mousePosition))
            {
                OnionGhostLayout? hit = FindOnionManipulationGhostAt(ghosts, evt.mousePosition);
                if (hit.HasValue)
                {
                    var ghost = hit.Value;
                    _playing = false;
                    _selectedOnionFrame = ghost.Frame;
                    _selectedOnionDelta = ghost.Delta;
                    _draggingOnion = true;
                    _onionDragStart = evt.mousePosition;
                    _onionOffsetStart = clip.OnionOffsets[ghost.Frame];
                    RecordProfileUndo("Move Onion Skin Offset");
                    GUIUtility.hotControl = controlId;
                    GUIUtility.keyboardControl = controlId;
                    _status = $"Selected onion {SignedFrameDelta(ghost.Delta)} (frame {ghost.Frame + 1})";
                    evt.Use();
                    Repaint();
                    return true;
                }
            }

            if (evt.type == EventType.MouseDrag && _draggingOnion && GUIUtility.hotControl == controlId)
            {
                Vector2 sourceDelta = ScreenToSourcePixelDelta(evt.mousePosition - _onionDragStart, cell);
                clip.OnionOffsets[_selectedOnionFrame] = new Vector2(
                    Mathf.Round(_onionOffsetStart.x + sourceDelta.x),
                    Mathf.Round(_onionOffsetStart.y + sourceDelta.y));
                evt.Use();
                Repaint();
                return true;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0 && _draggingOnion &&
                GUIUtility.hotControl == controlId)
            {
                _draggingOnion = false;
                GUIUtility.hotControl = 0;
                _status = $"Onion {SignedFrameDelta(_selectedOnionDelta)} offset {clip.OnionOffsets[_selectedOnionFrame]} px";
                SaveDirty();
                evt.Use();
                Repaint();
                return true;
            }

            return false;
        }

        OnionGhostLayout? FindOnionManipulationGhostAt(List<OnionGhostLayout> ghosts, Vector2 mouse)
        {
            if (_profile.ShowOnionLayerNumbers)
                foreach (var ghost in ghosts)
                    if (ghost.BadgeRect.Contains(mouse))
                        return ghost;
            foreach (var ghost in ghosts)
                if (ghost.Frame == _selectedOnionFrame && ghost.SpriteRect.Contains(mouse))
                    return ghost;
            return null;
        }

        OnionGhostLayout? FindOnionGhostAt(List<OnionGhostLayout> ghosts, Vector2 mouse)
        {
            if (_profile.ShowOnionLayerNumbers)
                foreach (var ghost in ghosts)
                    if (ghost.BadgeRect.Contains(mouse))
                        return ghost;

            int greatestDistance = 0;
            foreach (var ghost in ghosts)
                greatestDistance = Mathf.Max(greatestDistance, Mathf.Abs(ghost.Delta));
            OnionGhostLayout? first = null;
            bool selectNext = false;
            for (int distance = 1; distance <= greatestDistance; distance++)
            {
                foreach (var ghost in ghosts)
                {
                    if (Mathf.Abs(ghost.Delta) != distance || !ghost.SpriteRect.Contains(mouse))
                        continue;
                    first ??= ghost;
                    if (selectNext)
                        return ghost;
                    if (ghost.Frame == _selectedOnionFrame)
                        selectNext = true;
                }
            }
            return first;
        }

        void RecenterOnion(SpriteClipDef clip, int frame)
        {
            if (frame < 0 || frame >= clip.OnionOffsets.Length)
                return;
            RecordProfileUndo("Recenter Onion Skin");
            clip.OnionOffsets[frame] = Vector2.zero;
            _status = $"Recentered onion for frame {frame + 1}";
            SaveDirty();
        }

        void DrawEventMarkerInspector(SpriteClipDef clip)
        {
            GUILayout.Space(9f);
            SectionLabel("EVENT MARKER");

            int frame = Mathf.Clamp(_selectedEventFrame >= 0 ? _selectedEventFrame : _selectedFrame,
                0, clip.Frames.Length - 1);
            EditorGUILayout.LabelField("Frame", $"{frame + 1} of {clip.Frames.Length}");

            byte currentId = clip.EventIds[frame];
            int nextId = Mathf.Clamp(EditorGUILayout.IntField("Event ID", currentId), 0, byte.MaxValue);
            if (nextId != currentId)
            {
                SetFrameEvent(clip, frame, (byte)nextId, clip.EventNormalizedTimes[frame]);
                currentId = (byte)nextId;
            }

            if (currentId == 0)
            {
                EditorGUILayout.HelpBox(
                    "No marker on this frame. Enter an Event ID, or right-click the timeline event lane at an exact time.",
                    MessageType.None);
                return;
            }

            float frameStart = AuthoredStartTime(clip, frame);
            float duration = FrameDuration(clip, frame);
            float exactTime = frameStart + Mathf.Clamp01(clip.EventNormalizedTimes[frame]) * duration;
            float nextTime = EditorGUILayout.FloatField("Time (sec)", exactTime);
            if (!Mathf.Approximately(nextTime, exactTime))
            {
                float clampedTime = Mathf.Clamp(nextTime, frameStart,
                    Mathf.Max(frameStart, frameStart + duration - 0.0001f));
                RecordProfileUndo("Set Sprite Animation Event Time");
                clip.EventNormalizedTimes[frame] = Mathf.Clamp01((clampedTime - frameStart) /
                    Mathf.Max(0.001f, duration));
                _selectedEventFrame = frame;
                _selectedFrame = frame;
                _previewTime = PreviewTimeForAuthoredTime(clip, clampedTime);
                _status = $"Set {EventName(currentId)} to {clampedTime:F3}s";
                SaveDirty();
            }

            DrawEventDefinition(currentId);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"Selected: {EventName(currentId)}", _mutedStyle);
                if (GUILayout.Button("Delete Marker", GUILayout.Width(104f)))
                {
                    _selectedEventFrame = frame;
                    DeleteSelectedEventMarker();
                }
            }
        }

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
                definition = new SpriteEventDef { Id = eventId, Name = $"Event {eventId}" };
                _profile.Events.Add(definition);
            }
            definition.Name = DrawStringTextField("Event Name", definition.Name, "EventName");
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

            const float rowH = 32f;
            const float checkW = 18f;
            const float dupW = 72f;
            const float thumbW = 32f;
            _socketListRowRects.Clear();
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                bool selected = IsSocketSelected(name);
                SpriteSocketKeys.TryGetPose(clip.Sockets, name, _selectedFrame,
                    out _, out _, out _, out bool onFrame);
                Color swatch = SpriteSocketKeys.ColorForIndex(i);
                var catalogItem = _profile.SocketCatalog.Find(name);
                var row = GUILayoutUtility.GetRect(0f, rowH, GUILayout.ExpandWidth(true), GUILayout.Height(rowH));
                GUILayout.Space(3f);
                _socketListRowRects.Add(row);

                if (Event.current.type == EventType.Repaint)
                {
                    EditorStyles.helpBox.Draw(row, false, false, false, false);
                    if (selected)
                        EditorGUI.DrawRect(row, new Color(0.22f, 0.4f, 0.55f, 0.55f));
                }

                var checkRect = new Rect(row.x + 4f, row.y, checkW, rowH);
                var chipRect = new Rect(checkRect.xMax + 2f, row.y + 10f, 12f, 12f);
                var dupRect = new Rect(row.xMax - dupW - 4f, row.y + 2f, dupW, rowH - 4f);
                var thumbRect = new Rect(dupRect.x - thumbW - 4f, row.y, thumbW, rowH);
                var labelRect = new Rect(chipRect.xMax + 6f, row.y,
                    Mathf.Max(8f, thumbRect.x - chipRect.xMax - 8f), rowH);

                var evt = Event.current;
                if (evt.type == EventType.MouseDown && evt.button == 0 &&
                    row.Contains(evt.mousePosition) && !dupRect.Contains(evt.mousePosition))
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

                DrawSocketListCheckbox(checkRect, selected);
                EditorGUI.DrawRect(chipRect, swatch);
                var motionTrack = independentView ? _profile.FindSocketMotion(name) : null;
                string rowLabel = independentView
                    ? $"{i}. {name}  •  {motionTrack?.Keys?.Count ?? 0} keys"
                    : onFrame ? $"{i}. {name}" : $"{i}. {name}  (other frame)";
                GUI.Label(labelRect, rowLabel, selected ? EditorStyles.whiteLabel : EditorStyles.label);
                DrawSocketPreviewThumbnail(thumbRect, catalogItem);
                if (GUI.Button(dupRect, new GUIContent("Duplicate",
                        "Copy this socket's keys and catalog onto a new name."),
                    EditorStyles.miniButton))
                {
                    DuplicateSocketIdentity(clip, name);
                    GUIUtility.ExitGUI();
                }

                HandleSocketListRowContext(row, clip, names, i);
                HandleSocketPreviewDragDrop(row, name);
            }
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

                    string nextName = DrawStringTextField("Name", name, "SocketName");
                    if (!SpriteSocketKeys.NamesEqual(nextName, name))
                    {
                        string previousName = name;
                        SpriteSocketKeys.RenameIdentity(clip.Sockets, previousName, nextName);
                        name = SpriteSocketKeys.CanonicalName(nextName);
                        var motion = _profile.FindSocketMotion(previousName);
                        if (motion != null)
                            motion.SocketName = name;
                        _selectedSockets.Remove(SpriteSocketKeys.CanonicalName(previousName));
                        _selectedSockets.Add(name);
                        _selectedSocketName = name;
                        bool oldStillUsed = SpriteSocketKeys.NameExistsOnAnyClip(_profile.Clips, previousName);
                        _profile.SocketCatalog.SyncRename(previousName, name, oldStillUsed);
                        catalogItem = _profile.SocketCatalog.Find(name);
                    }

                    catalogItem ??= _profile.SocketCatalog.Ensure(name);
                    string socketId = catalogItem.SocketId;
                    string nextSocketId = EditorGUILayout.DelayedTextField(
                        new GUIContent("ID",
                            "Stable code ID shared by Frame-Attached and Independent Motion sockets. Example: equipment.head or combat.muzzle."),
                        socketId);
                    if (!string.Equals(nextSocketId, socketId, StringComparison.Ordinal))
                    {
                        string canonical = SpriteSocketIdUtility.Canonical(nextSocketId, name);
                        if (SocketIdUsedByOther(canonical, catalogItem))
                        {
                            _status = $"Socket ID '{canonical}' is already used";
                        }
                        else
                        {
                            RecordProfileUndo("Set Socket ID");
                            catalogItem.SocketId = canonical;
                            _status = $"{name} ID = {canonical}";
                            SaveDirty();
                        }
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
        {
            if (!_showSocketInheritPanel && !_showSocketTransformPanel)
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
            string hit = FindSocketAt(clip, frame, cell, contentMouse);
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
            GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 18f),
                $"SOCKET  {_selectedSocketName}", EditorStyles.boldLabel);
            if (GUI.Button(new Rect(rect.x + 8f, rect.y + 26f, 140f, 22f),
                    new GUIContent("Apply to Frames…",
                        "Copy this socket's position, rotation, and scale onto other frames."),
                    EditorStyles.miniButton))
            {
                OpenSocketInheritPanel(CurrentClip, _selectedSocketName, _selectedFrame);
            }
        }

        bool TryHandleSocketContextClick(SpriteClipDef clip, int frame, Rect cell, Vector2? mouseOverride = null)
        {
            var evt = Event.current;
            if (!IsPreviewContextClick(evt))
                return false;

            Vector2 mouse = mouseOverride ?? evt.mousePosition;
            string hit = FindSocketAt(clip, frame, cell, mouse);
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
            if (!IsSocketSelected(hit))
                SelectPreviewSocket(hit, SelectionOp.Replace);
            else
                _selectedSocketName = SpriteSocketKeys.CanonicalName(hit);

            var selected = new List<string>(_selectedSockets);
            int count = selected.Count;
            string name = SpriteSocketKeys.CanonicalName(hit);
            var menu = new GenericMenu();
            PopulateSocketContextMenu(menu, clip, name, selected, count);
            menu.ShowAsContext();
        }

        void PopulateSocketContextMenu(GenericMenu menu, SpriteClipDef clip, string name,
            List<string> selected, int count)
        {
            var catalogItem = _profile?.SocketCatalog?.Find(name);
            if (catalogItem != null && catalogItem.UsesOwnClock)
            {
                int motionTrack = _profile.SocketMotions.IndexOf(
                    _profile.FindSocketMotion(name));
                menu.AddItem(new GUIContent("Insert Key Here"), false,
                    () => InsertIndependentMotionKey(false, motionTrack));
                menu.AddItem(new GUIContent(
                        $"Insert Next Key ({IndependentKeyStepLabel()})"),
                    false, () => InsertIndependentMotionKey(true, motionTrack));
                AddIndependentDrawLayerMenuItems(menu, selected, CurrentIndependentMotionTime());
                menu.AddSeparator(string.Empty);
            }
            menu.AddItem(new GUIContent("Set Transform…"), false, OpenSocketTransformPanel);
            menu.AddItem(new GUIContent("Apply to Frames…"), false,
                () => OpenSocketInheritPanel(clip, name, _selectedFrame, default, exitGui: false));
            AddSocketPatternMenuItems(menu, clip);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent(count > 1
                    ? $"Assign Profile to {count} Sockets…"
                    : "Assign Profile…"),
                false, () => ShowSocketProfilePicker(selected));
            menu.AddItem(new GUIContent(count > 1 ? "Clear Profiles" : "Clear Profile"),
                false, () => ClearSocketPreviewOnNames(selected));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Duplicate"), false,
                () => DuplicateSocketIdentity(clip, name));
            menu.AddItem(new GUIContent(count > 1 ? $"Delete {count} Sockets" : "Delete"),
                false, () =>
                {
                    ClearColliderSelection();
                    DeleteSelectedPreviewObjects();
                });
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
                        _selectedSockets.Add(SpriteSocketKeys.CanonicalName(names[i]));
                }
                else
                    _selectedSockets.Add(name);
                _selectedSocketName = name;
                SyncSocketPrimaryFromSelection();
                _selectedEventFrame = -1;
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

        void HandleSocketListRowContext(Rect rowRect, SpriteClipDef clip, List<string> names, int index)
        {
            var evt = Event.current;
            if (evt.type != EventType.ContextClick &&
                !(evt.type == EventType.MouseDown && evt.button == 1))
                return;
            if (!rowRect.Contains(evt.mousePosition))
                return;
            if (!IsSocketSelected(names[index]))
                SelectSocketsFromListRow(names, index, SelectionOp.Replace);
            var selected = new List<string>(_selectedSockets);
            int count = selected.Count;
            string name = names[index];
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
                _selectedSockets.Add(SpriteSocketKeys.CanonicalName(names[i]));
            _selectedSocketName = names.Count > 0
                ? SpriteSocketKeys.CanonicalName(names[0])
                : null;
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
            _selectedOnionFrame = -1;
            _selectedSocketDrawFrame = -1;
            _selectedSocketDrawName = null;
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

            _selectedSockets.RemoveWhere(name => !SocketExistsInClipOrMotion(clip, name));
            if (!string.IsNullOrEmpty(_selectedSocketName) &&
                !SocketExistsInClipOrMotion(clip, _selectedSocketName))
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
                if (item != null && item.UsesOwnClock && _socketMoveMotionKeys.Count == 0)
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
            return _previewLoop || clip.WrapMode != SpriteAnimWrap.Once;
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

        const float EllipticalOrbitFlatten = 0.58f;
        const float FibonacciGoldenAngle = 137.508f;

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

        static readonly string[] SocketClockModeLabels = { "Frame-Attached", "Independent" };
        static readonly string[] SocketOrbitShapeLabels =
        {
            "Circle",
            "Elliptical",
        };
        static readonly string[] SocketOrbitPatternLabels =
        {
            "Atomic",
            "Coplanar",
            "Nested Shells",
            "Figure-8",
            "Spiral",
            "Fibonacci",
            "Vesica",
        };
        static readonly string[] SocketOrbitPatternPrefixes =
        {
            "Atomic Orbit",
            "Orb",
            "Shell",
            "Loop",
            "Spiral",
            "Cloud",
            "Vesica",
        };
        static readonly string[] SocketOrbitPatternTips =
        {
            "Intersecting orbital planes (Bohr). 3 = 0°, 60°, 120°.",
            "N sockets on one ellipse, evenly phased.",
            "Concentric rings, like electron shells.",
            "Infinity / lemniscate path, evenly phased.",
            "Growing ellipses with a slow tilt, like a spiral arm.",
            "Golden-angle cloud of small orbits around the nucleus.",
            "Two overlapping rings (vesica piscis).",
        };
        static readonly string[] SocketOrbitTiltLabels =
        {
            "0°", "15°", "30°", "45°", "60°", "75°", "90°",
            "105°", "120°", "135°", "150°", "165°",
        };
        static readonly string[] SocketPreviewPlayModeLabels = { "Cell", "Play Clip", "Follow Character" };

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
                        out _, out _, out _))
                    continue;
                if (!TryGetPreviewSocketPose(clip, names[i], frame,
                        out var position, out var angle, out var scale, out _))
                    continue;
                DrawSocketCatalogItem(cell, item, texture, columns, rows, cellIndex,
                    position, angle, scale, 1f);
            }
        }

        void DrawSocketCatalogItem(Rect cell, SpriteSocketCatalogItem item, Texture2D texture,
            int columns, int rows, int cellIndex, Vector2 localPixels, float angleDegrees,
            Vector2 poseScale, float alpha)
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
            GUI.matrix = previous;
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

        string FindSocketAt(SpriteClipDef clip, int frame, Rect cell, Vector2 point)
        {
            if (clip?.Sockets == null)
                return null;
            const float hitRadius = 14f;
            var names = SpriteSocketKeys.UniqueNamesInOrder(clip.Sockets);
            for (int i = names.Count - 1; i >= 0; i--)
            {
                string name = names[i];
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
                if (_socketHandleKind == ColliderHandleKind.None ||
                    _socketHandleKind == ColliderHandleKind.Body)
                    ApplySocketBodyMove(clip, frame, evt.mousePosition, cell);
                else
                    ApplySocketTransform(clip, frame, evt.mousePosition, evt.shift);
                evt.Use();
                Repaint();
                return true;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0 && ownsDrag)
            {
                if (_socketHandleKind == ColliderHandleKind.None ||
                    _socketHandleKind == ColliderHandleKind.Body)
                    ApplySocketBodyMove(clip, frame, evt.mousePosition, cell);
                else
                    ApplySocketTransform(clip, frame, evt.mousePosition, evt.shift);
                EndSocketDrag(controlId, save: true);
                evt.Use();
                Repaint();
                return true;
            }

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
            Vector2 sourceDelta = _socketMoveWholePath
                ? ScreenToSocketLocal(mouse, cell) - _socketGroupCentroidStart
                : ScreenToSourcePixelDelta(mouse - _socketDragStart, cell);
            if (_socketMoveWholePath)
                _socketGroupCentroidCurrent = ScreenToSocketLocal(mouse, cell);
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
                if (string.IsNullOrEmpty(name))
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
                _socketGroupCentroidCurrent = ScreenToSocketLocal(mouse, cell);
                ApplySocketBodyMove(clip, frame, mouse, cell);
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
                        angle, scale, out pivot, out var spriteRect, out float signX, out float signY))
                {
                    unrotated = FlipRectAround(spriteRect, pivot, signX, signY);
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

        static readonly ColliderHandleKind[] SocketGizmoHandleKinds =
        {
            ColliderHandleKind.Rotate,
            ColliderHandleKind.CornerTL, ColliderHandleKind.CornerTR,
            ColliderHandleKind.CornerBR, ColliderHandleKind.CornerBL,
            ColliderHandleKind.EdgeT, ColliderHandleKind.EdgeR,
            ColliderHandleKind.EdgeB, ColliderHandleKind.EdgeL,
        };

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
            _selectedOnionFrame = -1;
            ClearSocketToolState();
            _previewTime = 0f;
            SaveDirty();
            SealUndoGroup();
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
                FrameRate = source.FrameRate,
                WrapMode = source.WrapMode,
                FrameDurationScales = (float[])source.FrameDurationScales.Clone(),
                EventIds = (byte[])source.EventIds.Clone(),
                EventNormalizedTimes = (float[])source.EventNormalizedTimes.Clone(),
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
                if (box.ClipName != source.Name)
                    continue;
                copiedHitboxes.Add(new FrameBoxDef
                {
                    ClipName = clone.Name,
                    FrameIndex = box.FrameIndex,
                    RectUV = box.RectUV,
                    Id = box.Id,
                    Shape = box.Shape,
                    PolygonUV = box.PolygonUV == null
                        ? null
                        : (Vector2[])box.PolygonUV.Clone(),
                    Angle = box.Angle,
                    Hidden = box.Hidden,
                });
            }
            _profile.Hitboxes.AddRange(copiedHitboxes);
            _profile.Clips.Insert(_selectedClip + 1, clone);
            _selectedClip++;
            _collapsedSheets.Remove(clone.SheetIndex);
            SelectOnlyFrame(0);
            ClearColliderSelection();
            _selectedEventFrame = -1;
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
            RecordProfileUndo("Delete Sprite Animation Clip");
            _profile.Hitboxes.RemoveAll(box => box.ClipName == clip.Name);
            _profile.Clips.RemoveAt(clipIndex);
            if (_profile.Clips.Count == 0)
                _selectedClip = -1;
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
            clip.FrameDurationScales = durations.ToArray();
            clip.EventIds = events.ToArray();
            clip.EventNormalizedTimes = eventTimes.ToArray();
            clip.OnionOffsets = onionOffsets.ToArray();
            clip.FrameScales = frameScales.ToArray();
            clip.FrameRotations = frameRotations.ToArray();
            clip.FrameTweenModes = frameTweens.ToArray();
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
            clip.FrameDurationScales = CompactArray(clip.FrameDurationScales, remap, newCount);
            clip.EventIds = CompactArray(clip.EventIds, remap, newCount);
            clip.EventNormalizedTimes = CompactArray(clip.EventNormalizedTimes, remap, newCount);
            clip.OnionOffsets = CompactArray(clip.OnionOffsets, remap, newCount);
            clip.FrameScales = CompactArray(clip.FrameScales, remap, newCount);
            clip.FrameRotations = CompactArray(clip.FrameRotations, remap, newCount);
            clip.FrameTweenModes = CompactArray(clip.FrameTweenModes, remap, newCount);

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
                _selectedEventFrame = -1;

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
            int row = Mathf.Clamp(clip.Row, 0, rows - 1);
            int column = Mathf.Clamp(clip.Frames[frame], 0, columns - 1);
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

        bool TryEnsureSheetPixelCache()
            => TryEnsureSheetPixelCache(_profile.SheetAt(_selectedSheet));

        bool TryEnsureSheetPixelCache(SpriteClipDef clip)
            => TryEnsureSheetPixelCache(_profile.SheetForClip(clip) ?? _profile.SheetAt(_selectedSheet));

        bool TryEnsureSheetPixelCache(SpriteSheetDef def)
        {
            var sheet = def?.Texture ?? _profile?.Sheet;
            if (sheet == null)
            {
                InvalidateSheetPixelCache();
                return false;
            }

            EntityId id = sheet.GetEntityId();
            int columns = def != null && def.Columns > 0 ? Mathf.Max(1, def.Columns) : Mathf.Max(1, _profile.Columns);
            int rows = def != null && def.Rows > 0 ? Mathf.Max(1, def.Rows) : Mathf.Max(1, _profile.Rows);
            bool sameTexture = _sheetPixels != null &&
                _sheetPixelsId == id &&
                _sheetPixelsWidth == sheet.width &&
                _sheetPixelsHeight == sheet.height;
            if (sameTexture && _sheetCellEmpty != null &&
                _sheetPixelsColumns == columns &&
                _sheetPixelsRows == rows)
                return true;

            if (sameTexture)
            {
                _sheetPixelsColumns = columns;
                _sheetPixelsRows = rows;
                RebuildSheetCellEmptyFlags();
                return _sheetCellEmpty != null;
            }

            Texture2D readable = null;
            bool destroy = false;
            try
            {
                readable = sheet.isReadable ? sheet : DuplicateReadable(sheet);
                destroy = readable != sheet;
                _sheetPixels = readable.GetPixels32();
                _sheetPixelsId = id;
                _sheetPixelsWidth = readable.width;
                _sheetPixelsHeight = readable.height;
                _sheetPixelsColumns = columns;
                _sheetPixelsRows = rows;
                RebuildSheetCellEmptyFlags();
                return _sheetCellEmpty != null;
            }
            catch
            {
                InvalidateSheetPixelCache();
                return false;
            }
            finally
            {
                if (destroy && readable != null)
                    DestroyImmediate(readable);
            }
        }

        void RebuildSheetCellEmptyFlags()
        {
            int columns = Mathf.Max(1, _sheetPixelsColumns);
            int rows = Mathf.Max(1, _sheetPixelsRows);
            _sheetCellEmpty = new bool[columns * rows];
            if (_sheetPixels == null)
                return;

            int cellWidth = _sheetPixelsWidth / columns;
            int cellHeight = _sheetPixelsHeight / rows;
            if (cellWidth <= 0 || cellHeight <= 0)
                return;

            const byte alphaThreshold = 8;
            for (int row = 0; row < rows; row++)
            {
                int pixelY0 = (rows - 1 - row) * cellHeight;
                for (int column = 0; column < columns; column++)
                {
                    int pixelX0 = column * cellWidth;
                    bool empty = true;
                    for (int y = 0; y < cellHeight && empty; y++)
                    {
                        int rowStart = (pixelY0 + y) * _sheetPixelsWidth + pixelX0;
                        for (int x = 0; x < cellWidth; x++)
                        {
                            if (_sheetPixels[rowStart + x].a > alphaThreshold)
                            {
                                empty = false;
                                break;
                            }
                        }
                    }
                    _sheetCellEmpty[row * columns + column] = empty;
                }
            }
        }

        void InvalidateSheetPixelCache()
        {
            _sheetPixels = null;
            _sheetPixelsId = default;
            _sheetPixelsWidth = 0;
            _sheetPixelsHeight = 0;
            _sheetPixelsColumns = 0;
            _sheetPixelsRows = 0;
            _sheetCellEmpty = null;
        }

        int[] CreateDefaultFrames(int sheetIndex, int row)
        {
            var def = _profile.SheetAt(sheetIndex);
            int cols = def != null && def.Columns > 0 ? def.Columns : Mathf.Max(1, _profile.Columns);
            int rows = def != null && def.Rows > 0 ? def.Rows : Mathf.Max(1, _profile.Rows);
            row = Mathf.Clamp(row, 0, Mathf.Max(0, rows - 1));
            var occupied = new List<int>();
            var probe = new SpriteClipDef { SheetIndex = sheetIndex, Row = row, Frames = new[] { 0 } };
            if (TryEnsureSheetPixelCache(probe))
            {
                for (int c = 0; c < cols; c++)
                {
                    if (!IsSheetCellEmpty(c, row))
                        occupied.Add(c);
                }
            }
            if (occupied.Count > 0)
                return occupied.ToArray();
            var frames = new int[Mathf.Max(1, cols)];
            for (int i = 0; i < frames.Length; i++) frames[i] = i;
            return frames;
        }

        PreviewState EvaluatePreview(SpriteClipDef clip, float time)
        {
            if (clip == null)
                return default;
            var sample = SpriteAnimPlayback.EvaluatePreview(clip, time, _previewLoop);
            return new PreviewState
            {
                Frame = sample.Frame,
                Fraction = sample.Fraction,
                TimelineTime = sample.TimelineTime,
                Ended = sample.Ended,
            };
        }

        float FrameDuration(SpriteClipDef clip, int frame)
            => SpriteAnimPlayback.FrameDuration(clip, frame);

        float AuthoredStartTime(SpriteClipDef clip, int frame)
            => SpriteAnimPlayback.AuthoredStartTime(clip, frame);

        float EventAuthoredTime(SpriteClipDef clip, int frame)
        {
            if (clip == null || frame < 0 || frame >= clip.Frames.Length)
                return 0f;
            return AuthoredStartTime(clip, frame) +
                Mathf.Clamp01(clip.EventNormalizedTimes[frame]) * FrameDuration(clip, frame);
        }

        float TotalAuthoredDuration(SpriteClipDef clip)
            => SpriteAnimPlayback.TotalAuthoredDuration(clip);

        float TimelinePixelsPerSecond(SpriteClipDef clip)
        {
            float shortest = float.MaxValue;
            for (int i = 0; i < clip.Frames.Length; i++)
                shortest = Mathf.Min(shortest, FrameDuration(clip, i));
            float baseScale = Mathf.Clamp(
                64f / Mathf.Max(0.001f, shortest), PixelsPerSecond, 5000f);
            return baseScale * Mathf.Clamp(_frameTimelineZoom, 0.25f, 8f);
        }

        void AutoDetect()
        {
            if (_profile.Sheet == null) return;
            Texture2D readable = null;
            bool destroy = false;
            try
            {
                readable = _profile.Sheet.isReadable ? _profile.Sheet : DuplicateReadable(_profile.Sheet);
                destroy = readable != _profile.Sheet;
                var pixels = readable.GetPixels32();
                int width = readable.width;
                int height = readable.height;
                var columns = new bool[width];
                var rows = new bool[height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        if (pixels[y * width + x].a > 8)
                        {
                            columns[x] = true;
                            rows[y] = true;
                        }
                int columnCount = CountBands(columns);
                int rowCount = CountBands(rows);
                if (columnCount > 0 && rowCount > 0)
                {
                    _profile.Columns = columnCount;
                    _profile.Rows = rowCount;
                    _status = $"Detected {columnCount} × {rowCount} grid";
                }
                else _status = "No transparent gaps detected; set grid manually";
            }
            catch (Exception exception)
            {
                _status = "Grid detection failed: " + exception.Message;
            }
            finally
            {
                if (destroy && readable != null) DestroyImmediate(readable);
            }
        }

        static int CountBands(bool[] values)
        {
            int count = 0;
            bool inside = false;
            foreach (bool value in values)
            {
                if (value && !inside) { count++; inside = true; }
                else if (!value) inside = false;
            }
            return count;
        }

        static Texture2D DuplicateReadable(Texture2D source)
        {
            var previous = RenderTexture.active;
            var temporary = RenderTexture.GetTemporary(source.width, source.height, 0,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm);
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            return copy;
        }

        void SaveDirty()
        {
            if (_asset != null)
                EditorUtility.SetDirty(_asset);
        }

        bool CanSaveProfile()
        {
            return ResolveSaveTexture() != null;
        }

        Texture2D ResolveSaveTexture()
        {
            if (_profile == null)
                return null;
            _profile.EnsureSheets(_selectedSheet);
            if (_profile.Sheets != null && _profile.Sheets.Count > 0 &&
                _profile.Sheets[0] != null && _profile.Sheets[0].Texture != null)
                return _profile.Sheets[0].Texture;
            var active = _profile.SheetAt(_selectedSheet);
            if (active?.Texture != null)
                return active.Texture;
            return _profile.Sheet;
        }

        void SaveProfile()
        {
            Texture2D saveTex = ResolveSaveTexture();
            if (saveTex == null)
            {
                _status = "Assign a sprite sheet before saving";
                ShowNotification(new GUIContent(_status));
                return;
            }

            _profile.EnsureSheets(_selectedSheet);
            WriteActiveSheetFromLegacy();
            string texturePath = AssetDatabase.GetAssetPath(saveTex);
            string directory = Path.GetDirectoryName(texturePath)?.Replace('\\', '/');
            if (_asset == null && !_createSeparateProfileOnSave)
                TryLoadExistingAsset();
            if (_asset == null)
            {
                string assetPath = UniqueProfileAssetPath(directory, saveTex.name);
                _asset = CreateInstance<ScriptableSpriteSheetProfile>();
                AssetDatabase.CreateAsset(_asset, assetPath);
            }
            _createSeparateProfileOnSave = false;
            _asset.Data = _profile;
            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssets();

            string savedPath = AssetDatabase.GetAssetPath(_asset);
            string jsonPath = savedPath.Replace(".asset", ".json");
            File.WriteAllText(jsonPath, _profile.ToJson());
            AssetDatabase.ImportAsset(jsonPath);
            _status = $"Saved {_asset.name}";
            ShowNotification(new GUIContent("Profile saved"));
            SpriteSheetProfileRecents.Remember(_asset);
        }

        static string UniqueProfileAssetPath(string directory, string sheetName)
        {
            string path = $"{directory}/{sheetName}_profile.asset";
            int n = 2;
            while (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
            {
                path = $"{directory}/{sheetName}_profile {n}.asset";
                n++;
            }
            return path;
        }

        void TryLoadExistingAsset()
        {
            if (_profile.Sheet == null) return;
            string path = AssetDatabase.GetAssetPath(_profile.Sheet);
            string directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string assetPath = $"{directory}/{_profile.Sheet.name}_profile.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(assetPath);
            if (existing != null)
                LoadAsset(existing);
        }

        void LoadAsset(ScriptableSpriteSheetProfile asset)
        {
            _asset = asset;
            _profile = asset.Data ?? new SpriteSheetProfile();
            if (asset.Data == null)
                asset.Data = _profile;
            _sheetFoldInitialized = false;
            EnsureProfile();
            if (_profile.Clips != null && _profile.Clips.Count > 0 && _profile.Clips[0] != null)
            {
                _selectedClip = 0;
                _selectedSheet = _profile.Clips[0].SheetIndex;
            }
            else
            {
                _selectedClip = -1;
                _selectedSheet = 0;
            }
            _profile.EnsureSheets(_selectedSheet);
            if (_profile.SheetsWorldHeightsDiffer())
            {
                int source = _selectedSheet;
                var selected = _profile.SheetAt(_selectedSheet);
                if (selected?.Texture == null)
                    source = 0;
                RematchSheetsWorldSize(source);
                SaveDirty();
            }
            InvalidateSheetPixelCache();
            SelectOnlyFrame(0);
            ClearColliderSelection();
            _selectedEventFrame = -1;
            _selectedOnionFrame = -1;
            _previewTime = 0f;
            _createSeparateProfileOnSave = false;
            _status = $"Loaded {asset.name}";
            SpriteSheetProfileRecents.Remember(asset);
        }

        void EnsureProfile()
        {
            _profile ??= new SpriteSheetProfile();
            _profile.Clips ??= new List<SpriteClipDef>();
            _profile.Events ??= new List<SpriteEventDef>();
            _profile.Hitboxes ??= new List<FrameBoxDef>();
            _profile.EnsureSheets(_selectedSheet);
            if (_profile.Sheets != null && _profile.Sheets.Count > 0)
                _selectedSheet = Mathf.Clamp(_selectedSheet, 0, _profile.Sheets.Count - 1);
            _profile.EnsureTimelineHitPolygon();
            _profile.EnsureSocketCatalog();
            _profile.EnsureSocketMotions();
            if (!_profile.OnionSettingsInitialized)
            {
                _profile.OnionSettingsInitialized = true;
                _profile.OnionPastFrames = SpriteSheetProfile.DefaultOnionFrameCount;
                _profile.OnionFutureFrames = SpriteSheetProfile.DefaultOnionFrameCount;
                _profile.ShowOnionLayerNumbers = true;
            }
            _profile.OnionPastFrames = Mathf.Clamp(_profile.OnionPastFrames, 0, 4096);
            _profile.OnionFutureFrames = Mathf.Clamp(_profile.OnionFutureFrames, 0, 4096);
            foreach (var clip in _profile.Clips)
                clip?.EnsureFrameData();
            if (_profile.Events.Count == 0)
            {
                _profile.Events.Add(new SpriteEventDef { Id = 1, Name = "Footstep" });
                _profile.Events.Add(new SpriteEventDef
                {
                    Id = 2,
                    Name = "Attack",
                    Color = new Color(1f, 0.35f, 0.3f),
                });
            }
        }

        void RenameHitboxClip(string oldName, string newName)
        {
            foreach (var box in _profile.Hitboxes)
                if (box.ClipName == oldName)
                    box.ClipName = newName;
        }

        IEnumerable<FrameBoxDef> BoxesFor(SpriteClipDef clip, int frame)
        {
            foreach (var box in _profile.Hitboxes)
                if (box.ClipName == clip.Name && box.FrameIndex == frame)
                    yield return box;
        }

        List<FrameBoxDef> CurrentFrameColliders(SpriteClipDef clip, int frame)
        {
            var result = new List<FrameBoxDef>();
            if (clip == null)
                return result;
            foreach (var box in BoxesFor(clip, frame))
                result.Add(box);
            return result;
        }

        void DrawColliderList(SpriteClipDef clip)
        {
            PruneColliderSelection(clip, _selectedFrame);
            var colliders = CurrentFrameColliders(clip, _selectedFrame);
            SectionLabel($"COLLIDERS — FRAME {_selectedFrame + 1}");
            GUILayout.Label(
                $"{colliders.Count} collider{(colliders.Count == 1 ? string.Empty : "s")} • {_selectedColliders.Count} selected",
                _mutedStyle);

            if (colliders.Count == 0)
            {
                EditorGUILayout.HelpBox("This frame has no colliders. Arm a shape above, then click the preview.",
                    MessageType.None);
                return;
            }

            for (int i = 0; i < colliders.Count; i++)
            {
                FrameBoxDef box = colliders[i];
                bool selected = _selectedColliders.Contains(box);
                using (new EditorGUILayout.HorizontalScope())
                {
                    Color previous = GUI.backgroundColor;
                    if (GUILayout.Button(ColliderVisibilityContent(box.Hidden),
                        EditorStyles.miniButton, GUILayout.Width(28f), GUILayout.Height(22f)))
                    {
                        RecordProfileUndo(box.Hidden ? "Show Sprite Collider" : "Hide Sprite Collider");
                        box.Hidden = !box.Hidden;
                        _status = box.Hidden
                            ? $"Hid {box.Shape} collider #{box.Id}"
                            : $"Showed {box.Shape} collider #{box.Id}";
                        SaveDirty();
                        Repaint();
                    }
                    if (selected)
                        GUI.backgroundColor = AccentColor;
                    if (GUILayout.Button(new GUIContent(
                            $"{i + 1}. {box.Shape}   •   ID {box.Id}{(box.Hidden ? "  (hidden)" : string.Empty)}",
                            "Select this collider. Shift = add/range, Ctrl/Cmd = toggle, Alt = subtract."),
                        EditorStyles.miniButton, GUILayout.Height(22f)))
                    {
                        var evt = Event.current;
                        _playing = false;
                        _previewTime = PreviewTimeAtFrame(clip, _selectedFrame);
                        SelectColliderFromList(colliders, i, ReadSelectionOp(Event.current, orderedList: true));
                        _selectedEventFrame = -1;
                        _selectedOnionFrame = -1;
                        Repaint();
                    }
                    GUI.backgroundColor = previous;

                    if (GUILayout.Button(new GUIContent("×", "Delete this collider."),
                        EditorStyles.miniButton, GUILayout.Width(27f), GUILayout.Height(22f)))
                    {
                        DeleteCollider(box);
                        return;
                    }
                }
            }

            FrameBoxDef primary = PrimarySelectedCollider();
            if (primary != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    float nextAngle = EditorGUILayout.FloatField(
                        new GUIContent("Angle (deg)", "Rotation around the collider center. Saved on the profile."),
                        primary.Angle);
                    if (!Mathf.Approximately(nextAngle, primary.Angle))
                    {
                        RecordProfileUndo("Rotate Sprite Collider");
                        primary.Angle = nextAngle;
                        _status = $"Collider angle {primary.Angle:0.##}°";
                    }
                    using (new EditorGUI.DisabledScope(Mathf.Approximately(primary.Angle, 0f)))
                    {
                        if (ResetValueButton("Reset this collider's rotation to 0°."))
                        {
                            RecordProfileUndo("Reset Sprite Collider Angle");
                            primary.Angle = 0f;
                            _status = "Reset collider angle to 0°";
                        }
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select All"))
                    SelectAllFrameColliders(clip, _selectedFrame);
                using (new EditorGUI.DisabledScope(_selectedColliders.Count == 0))
                    if (GUILayout.Button("Delete Selected"))
                        DeleteSelectedColliders();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_selectedFrame >= clip.Frames.Length - 1))
                {
                    if (GUILayout.Button(new GUIContent("Copy to Next Frame",
                        "Clone current-frame colliders onto the next frame.")))
                        CopyCollidersToNextFrame(clip, _selectedFrame);
                }
                if (GUILayout.Button(new GUIContent("Copy to All Frames",
                    "Clone current-frame colliders onto every other frame in this clip.")))
                    CopyCollidersToAllFrames(clip, _selectedFrame);
            }
            if (GUILayout.Button(new GUIContent("Delete All on This Frame",
                "Delete every collider on the current frame. This action supports Undo.")))
                DeleteAllFrameColliders(clip, _selectedFrame);
        }

        void SelectAllFrameColliders(SpriteClipDef clip, int frame)
        {
            _playing = false;
            _selectedFrame = frame;
            _previewTime = PreviewTimeAtFrame(clip, frame);
            _selectedColliders.Clear();
            foreach (var box in BoxesFor(clip, frame))
                _selectedColliders.Add(box);
            ClearSocketSelection();
            _selectedEventFrame = -1;
            _selectedOnionFrame = -1;
            _status = $"Selected all {_selectedColliders.Count} collider{(_selectedColliders.Count == 1 ? string.Empty : "s")} on frame {frame + 1}";
            Repaint();
        }

        void SelectAllPreviewObjects(SpriteClipDef clip, int frame)
        {
            if (clip == null)
                return;
            _playing = false;
            _selectedFrame = frame;
            _previewTime = PreviewTimeAtFrame(clip, frame);
            _selectedColliders.Clear();
            if (_showHitboxes)
            {
                foreach (var box in BoxesFor(clip, frame))
                    _selectedColliders.Add(box);
            }
            _selectedSockets.Clear();
            if (clip.Sockets != null)
            {
                var names = SpriteSocketKeys.UniqueNamesInOrder(clip.Sockets);
                for (int i = 0; i < names.Count; i++)
                {
                    if (!SpriteSocketKeys.TryGetPose(clip.Sockets, names[i], frame, out _, out _, out _))
                        continue;
                    _selectedSockets.Add(SpriteSocketKeys.CanonicalName(names[i]));
                }
            }
            SyncSocketPrimaryFromSelection();
            _selectedEventFrame = -1;
            _selectedOnionFrame = -1;
            _status = PreviewSelectionStatus("Selected all");
            Repaint();
        }

        void SelectAllSockets(SpriteClipDef clip)
        {
            if (clip?.Sockets == null)
                return;
            ReleaseShortcutKeyboardFocus();
            _playing = false;
            ClearColliderSelection();
            _selectedSockets.Clear();
            var names = SpriteSocketKeys.UniqueNamesInOrder(clip.Sockets);
            for (int i = 0; i < names.Count; i++)
                _selectedSockets.Add(SpriteSocketKeys.CanonicalName(names[i]));
            _socketListAnchor = names.Count > 0 ? 0 : -1;
            SyncSocketPrimaryFromSelection();
            _selectedEventFrame = -1;
            _selectedSocketDrawFrame = -1;
            _selectedSocketDrawName = null;
            _selectedOnionFrame = -1;
            _status = PreviewSelectionStatus("Selected all");
            Repaint();
        }

        void DeleteCollider(FrameBoxDef box)
        {
            if (box == null || !_profile.Hitboxes.Contains(box))
                return;
            RecordProfileUndo("Delete Sprite Collider");
            _profile.Hitboxes.Remove(box);
            _selectedColliders.Remove(box);
            _status = $"Deleted {box.Shape} collider";
            SaveDirty();
            Repaint();
        }

        void DeleteSelectedColliders()
        {
            PruneColliderSelection(CurrentClip, _selectedFrame);
            if (_selectedColliders.Count == 0)
                return;
            DeleteSelectedPreviewObjects(includeSockets: false);
        }

        void DeleteSelectedPreviewObjects(bool includeSockets = true)
        {
            var clip = CurrentClip;
            PruneColliderSelection(clip, _selectedFrame);
            if (includeSockets)
                PruneSocketSelection(clip);
            int colliderCount = _selectedColliders.Count;
            int socketCount = includeSockets ? _selectedSockets.Count : 0;
            if (colliderCount == 0 && socketCount == 0)
                return;

            string undoName;
            if (colliderCount > 0 && socketCount > 0)
                undoName = "Delete Sprite Colliders and Sockets";
            else if (socketCount > 0)
                undoName = socketCount == 1 ? "Delete Sprite Socket" : "Delete Sprite Sockets";
            else
                undoName = colliderCount == 1 ? "Delete Sprite Collider" : "Delete Sprite Colliders";
            RecordDiscreteUndo(undoName);

            if (colliderCount > 0)
                _profile.Hitboxes.RemoveAll(box => _selectedColliders.Contains(box));
            _selectedColliders.Clear();

            if (socketCount > 0)
            {
                _profile.EnsureSocketCatalog();
                var names = new List<string>(_selectedSockets);
                for (int i = 0; i < names.Count; i++)
                {
                    var catalogItem = _profile.SocketCatalog.Find(names[i]);
                    bool independent = catalogItem != null && catalogItem.UsesOwnClock ||
                                       _profile.FindSocketMotion(names[i]) != null;
                    if (independent)
                    {
                        for (int c = 0; c < _profile.Clips.Count; c++)
                            SpriteSocketKeys.DeleteIdentity(_profile.Clips[c].Sockets, names[i]);
                        _profile.SocketMotions.RemoveAll(track =>
                            track != null && SpriteSocketKeys.NamesEqual(track.SocketName, names[i]));
                    }
                    else if (clip?.Sockets != null)
                    {
                        SpriteSocketKeys.DeleteIdentity(clip.Sockets, names[i]);
                    }
                    bool stillUsed = SpriteSocketKeys.NameExistsOnAnyClip(_profile.Clips, names[i]) ||
                                     _profile.FindSocketMotion(names[i]) != null;
                    _profile.SocketCatalog.SyncDelete(names[i], stillUsed);
                }
            }
            if (includeSockets)
                ClearSocketSelection();
            _draggingSocket = false;
            _socketHandleKind = ColliderHandleKind.None;
            _socketTransformName = null;
            _socketMoveNames.Clear();
            _socketMoveStarts.Clear();
            _status = colliderCount > 0 && socketCount > 0
                ? $"Deleted {colliderCount} collider{(colliderCount == 1 ? string.Empty : "s")} and {socketCount} socket{(socketCount == 1 ? string.Empty : "s")}"
                : socketCount > 0
                    ? $"Deleted {socketCount} socket{(socketCount == 1 ? string.Empty : "s")}"
                    : $"Deleted {colliderCount} collider{(colliderCount == 1 ? string.Empty : "s")}";
            SaveDirty();
            SealUndoGroup();
            Repaint();
        }

        void DeleteAllFrameColliders(SpriteClipDef clip, int frame)
        {
            if (clip == null)
                return;
            int count = CurrentFrameColliders(clip, frame).Count;
            if (count == 0)
                return;
            RecordProfileUndo("Delete All Sprite Colliders on Frame");
            _profile.Hitboxes.RemoveAll(box => box.ClipName == clip.Name && box.FrameIndex == frame);
            _selectedColliders.Clear();
            _status = $"Deleted all {count} colliders on frame {frame + 1}";
            SaveDirty();
            Repaint();
        }

        void CopyCollidersToNextFrame(SpriteClipDef clip, int sourceFrame)
        {
            if (clip == null || sourceFrame < 0 || sourceFrame >= clip.Frames.Length - 1)
                return;
            CopyCollidersToFrame(clip, sourceFrame, sourceFrame + 1);
            _status = $"Copied colliders from frame {sourceFrame + 1} to {sourceFrame + 2}";
        }

        void CopyCollidersToAllFrames(SpriteClipDef clip, int sourceFrame)
        {
            if (clip == null || sourceFrame < 0 || sourceFrame >= clip.Frames.Length)
                return;
            RecordProfileUndo("Copy Sprite Colliders to All Frames");
            int copied = 0;
            for (int frame = 0; frame < clip.Frames.Length; frame++)
            {
                if (frame == sourceFrame)
                    continue;
                copied += CopyCollidersToFrameInternal(clip, sourceFrame, frame);
            }
            SaveDirty();
            _status = copied == 0
                ? "No colliders copied"
                : $"Copied {copied} collider{(copied == 1 ? string.Empty : "s")} to all frames";
            Repaint();
        }

        void CopyCollidersToFrame(SpriteClipDef clip, int sourceFrame, int destinationFrame)
        {
            RecordProfileUndo("Copy Sprite Colliders to Next Frame");
            int copied = CopyCollidersToFrameInternal(clip, sourceFrame, destinationFrame);
            SaveDirty();
            _status = copied == 0
                ? "No colliders copied"
                : $"Copied {copied} collider{(copied == 1 ? string.Empty : "s")}";
            Repaint();
        }

        int CopyCollidersToFrameInternal(SpriteClipDef clip, int sourceFrame, int destinationFrame)
        {
            _profile.Hitboxes.RemoveAll(box => box.ClipName == clip.Name && box.FrameIndex == destinationFrame);
            var source = CurrentFrameColliders(clip, sourceFrame);
            for (int i = 0; i < source.Count; i++)
            {
                var box = source[i];
                var clone = new FrameBoxDef
                {
                    ClipName = clip.Name,
                    FrameIndex = destinationFrame,
                    RectUV = box.RectUV,
                    Id = box.Id,
                    Shape = box.Shape,
                    PolygonUV = box.PolygonUV == null ? null : (Vector2[])box.PolygonUV.Clone(),
                    Angle = box.Angle,
                    Hidden = box.Hidden,
                };
                _profile.Hitboxes.Add(clone);
            }
            return source.Count;
        }

        void ClearColliderSelection()
        {
            _selectedColliders.Clear();
            ClearColliderTransform();
            ClearSocketSelection();
        }

        void PruneColliderSelection(SpriteClipDef clip, int frame)
        {
            _selectedColliders.RemoveWhere(box => box == null ||
                !_profile.Hitboxes.Contains(box) || clip == null ||
                box.ClipName != clip.Name || box.FrameIndex != frame);
        }

        void SelectEventMarker(SpriteClipDef clip, int frame, float authoredTime)
        {
            if (clip == null || frame < 0 || frame >= clip.EventIds.Length || clip.EventIds[frame] == 0)
                return;
            _selectedEventFrame = frame;
            _selectedSocketDrawFrame = -1;
            _selectedSocketDrawName = null;
            _selectedFrame = frame;
            _previewTime = PreviewTimeForAuthoredTime(clip, authoredTime);
            _playing = false;
            ClearColliderSelection();
            _selectedOnionFrame = -1;
            _status = $"Selected {EventName(clip.EventIds[frame])} at {authoredTime:F3}s";
        }

        void DeleteSelectedEventMarker()
        {
            var clip = CurrentClip;
            if (clip == null || _selectedEventFrame < 0 ||
                _selectedEventFrame >= clip.EventIds.Length || clip.EventIds[_selectedEventFrame] == 0)
                return;
            SetFrameEvent(clip, _selectedEventFrame, 0);
        }

        void PruneEventSelection(SpriteClipDef clip)
        {
            if (clip == null || _selectedEventFrame < 0 ||
                _selectedEventFrame >= clip.EventIds.Length || clip.EventIds[_selectedEventFrame] == 0)
                _selectedEventFrame = -1;
        }

        static int EventMarkerAt(SpriteClipDef clip, float[] eventXs, Vector2 point)
        {
            if (point.y < TimelineEventLaneY || point.y >= TimelineDrawLaneY)
                return -1;
            float laneY = TimelineEventLaneY + 13f;
            for (int i = eventXs.Length - 1; i >= 0; i--)
            {
                if (clip.EventIds[i] == 0)
                    continue;
                Vector2 center = new(eventXs[i], laneY);
                if ((point - center).sqrMagnitude <= 100f)
                    return i;
            }
            return -1;
        }

        static int RemapIndexAfterMove(int index, int fromIndex, int toIndex)
        {
            if (index < 0)
                return index;
            if (index == fromIndex)
                return toIndex;
            if (fromIndex < toIndex && index > fromIndex && index <= toIndex)
                return index - 1;
            if (toIndex < fromIndex && index >= toIndex && index < fromIndex)
                return index + 1;
            return index;
        }

        float PreviewTimeAtFrame(SpriteClipDef clip, int frame)
        {
            float authoredTime = 0f;
            for (int i = 0; i < Mathf.Clamp(frame, 0, clip.Frames.Length - 1); i++)
                authoredTime += FrameDuration(clip, i);
            return PreviewTimeForAuthoredTime(clip, authoredTime);
        }

        string EventName(byte id)
        {
            var definition = _profile.Events.Find(e => e != null && e.Id == id);
            return definition == null ? $"Event {id}" : definition.Name;
        }

        Color EventMarkerColor(byte id)
        {
            var definition = _profile.Events.Find(e => e != null && e.Id == id);
            return definition == null ? EventColor : definition.Color;
        }

        int ClipSheetColumns(SpriteClipDef clip)
        {
            var def = _profile.SheetForClip(clip);
            return def != null && def.Columns > 0 ? def.Columns : Mathf.Max(1, _profile.Columns);
        }

        int ClipSheetRows(SpriteClipDef clip)
        {
            var def = _profile.SheetForClip(clip);
            return def != null && def.Rows > 0 ? def.Rows : Mathf.Max(1, _profile.Rows);
        }

        int CellIndexOf(SpriteClipDef clip, int frame)
        {
            int columns = ClipSheetColumns(clip);
            int rows = ClipSheetRows(clip);
            frame = Mathf.Clamp(frame, 0, clip.Frames.Length - 1);
            int row = Mathf.Clamp(clip.Row, 0, Mathf.Max(0, rows - 1));
            int column = Mathf.Clamp(clip.Frames[frame], 0, Mathf.Max(0, columns - 1));
            return row * columns + column;
        }

        void DrawClipFrame(SpriteClipDef clip, int frame, Rect rect, float alpha)
        {
            var def = _profile.SheetForClip(clip);
            var tex = def?.Texture ?? _profile.Sheet;
            DrawCellTinted(tex, CellIndexOf(clip, frame), rect, new Color(1f, 1f, 1f, alpha),
                ClipSheetColumns(clip), ClipSheetRows(clip));
        }

        void DrawCell(Texture2D sheet, int cellIndex, Rect rect, float alpha)
            => DrawCellTinted(sheet, cellIndex, rect, new Color(1f, 1f, 1f, alpha));

        void DrawCellTinted(Texture2D sheet, int cellIndex, Rect rect, Color tint)
            => DrawCellTinted(sheet, cellIndex, rect, tint, _profile.Columns, _profile.Rows);

        void DrawCellTinted(Texture2D sheet, int cellIndex, Rect rect, Color tint, int columns, int rows)
        {
            if (sheet == null) return;
            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            int column = cellIndex % columns;
            int row = cellIndex / columns;
            var uv = new Rect(
                column / (float)columns,
                1f - (row + 1f) / rows,
                1f / columns,
                1f / rows);
            Color previous = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, sheet, uv, true);
            GUI.color = previous;
        }

        Vector2 SourcePixelsToScreenOffset(Vector2 sourcePixels, Rect cell)
        {
            float sourceWidth = _profile.Sheet.width / (float)Mathf.Max(1, _profile.Columns);
            float sourceHeight = _profile.Sheet.height / (float)Mathf.Max(1, _profile.Rows);
            return new Vector2(
                sourcePixels.x / Mathf.Max(1f, sourceWidth) * cell.width,
                -sourcePixels.y / Mathf.Max(1f, sourceHeight) * cell.height);
        }

        Vector2 ScreenToSourcePixelDelta(Vector2 screenDelta, Rect cell)
        {
            float sourceWidth = _profile.Sheet.width / (float)Mathf.Max(1, _profile.Columns);
            float sourceHeight = _profile.Sheet.height / (float)Mathf.Max(1, _profile.Rows);
            return new Vector2(
                screenDelta.x / Mathf.Max(1f, cell.width) * sourceWidth,
                -screenDelta.y / Mathf.Max(1f, cell.height) * sourceHeight);
        }

        static Color OnionColor(int delta)
        {
            int distance = Mathf.Abs(delta);
            float hue = Mathf.Repeat((delta + 8) * 0.137f, 1f);
            Color color = Color.HSVToRGB(hue, 0.78f, 1f);
            color.a = Mathf.Clamp(0.32f - (distance - 1) * 0.045f, 0.1f, 0.32f);
            return color;
        }

        static string SignedFrameDelta(int delta) => delta > 0 ? $"+{delta}" : delta.ToString();

        bool OnionSelectionIsVisible(SpriteClipDef clip, int currentFrame)
        {
            if (!_profile.OnionSkinEnabled || _selectedOnionFrame < 0 ||
                _selectedOnionFrame >= clip.Frames.Length || _selectedOnionFrame == currentFrame)
                return false;
            int delta = _selectedOnionFrame - currentFrame;
            return delta < 0
                ? -delta <= _profile.OnionPastFrames
                : delta <= _profile.OnionFutureFrames;
        }

        void DrawPivot(Rect cell)
        {
            if (!_showPivot)
                return;

            Vector2 point = PivotScreen(cell);
            bool active = _draggingPivot || _pivotSelected;
            float radius = active ? 6.5f : 5.5f;
            Color fill = active
                ? new Color(0.45f, 1f, 0.48f, 1f)
                : new Color(0.22f, 0.82f, 0.3f, 1f);
            Color outline = new Color(0.06f, 0.32f, 0.1f, 1f);

            Handles.BeginGUI();
            Handles.color = outline;
            Handles.DrawSolidDisc(point, Vector3.forward, radius + 1.15f);
            Handles.color = fill;
            Handles.DrawSolidDisc(point, Vector3.forward, radius);
            Handles.EndGUI();

            EditorGUIUtility.AddCursorRect(
                new Rect(point.x - PivotHandleHitRadius, point.y - PivotHandleHitRadius,
                    PivotHandleHitRadius * 2f, PivotHandleHitRadius * 2f),
                MouseCursor.MoveArrow);
        }

        void DrawSheetTextureInfo()
        {
            if (_profile.Sheet == null)
            {
                EditorGUILayout.LabelField("File name", "—");
                EditorGUILayout.LabelField("Size", "—");
                return;
            }

            EditorGUILayout.LabelField("File name", SheetTextureFileName(_profile.Sheet));
            EditorGUILayout.LabelField("Size",
                $"{_profile.Sheet.width} × {_profile.Sheet.height}");
            int columns = Mathf.Max(1, _profile.Columns);
            int rows = Mathf.Max(1, _profile.Rows);
            EditorGUILayout.LabelField("Cell size",
                $"{_profile.Sheet.width / columns} × {_profile.Sheet.height / rows} px");
        }

        void DrawPixelsPerUnitSize()
        {
            if (_profile.Sheet == null)
            {
                EditorGUILayout.LabelField("Cell in world", "—");
                return;
            }

            int columns = Mathf.Max(1, _profile.Columns);
            int rows = Mathf.Max(1, _profile.Rows);
            float cellW = _profile.Sheet.width / (float)columns;
            float cellH = _profile.Sheet.height / (float)rows;
            float ppu = Mathf.Max(SpriteSheetProfile.MinPixelsPerUnit, _profile.PixelsPerUnit);
            float worldW = cellW / ppu;
            float worldH = cellH / ppu;
            EditorGUILayout.LabelField("Cell in world",
                $"{worldW:0.###} × {worldH:0.###} units");
            GUILayout.Label($"{cellW:0.#} px / {ppu:0.#} PPU", _mutedStyle);
            if (_profile.Sheets != null && _profile.Sheets.Count > 1)
                GUILayout.Label("PPU is per sheet so every sheet is the same world size.", _mutedStyle);
        }

        static string SheetTextureFileName(Texture2D sheet)
        {
            if (sheet == null)
                return "—";
            string path = AssetDatabase.GetAssetPath(sheet);
            if (!string.IsNullOrEmpty(path))
            {
                string fileName = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(fileName))
                    return fileName;
            }
            return string.IsNullOrEmpty(sheet.name) ? "—" : sheet.name;
        }

        bool TryComputePreviewLayout(Rect localCanvas, out Rect cell, out float contentW, out float contentH)
        {
            contentW = Mathf.Max(1f, localCanvas.width);
            contentH = Mathf.Max(1f, localCanvas.height);
            cell = new Rect(0f, 0f, contentW, contentH);
            if (_profile?.Sheet == null)
                return false;

            float availableWidth = Mathf.Max(40f, localCanvas.width - 52f);
            float availableHeight = Mathf.Max(40f, localCanvas.height - 52f);
            float cellAspect = (_profile.Sheet.width / (float)Mathf.Max(1, _profile.Columns)) /
                               (_profile.Sheet.height / (float)Mathf.Max(1, _profile.Rows));
            float fitWidth = availableWidth;
            float fitHeight = fitWidth / Mathf.Max(0.01f, cellAspect);
            if (fitHeight > availableHeight)
            {
                fitHeight = availableHeight;
                fitWidth = fitHeight * cellAspect;
            }
            float zoom = Mathf.Clamp(_previewZoom, 0.25f, 8f);
            float cellWidth = fitWidth * zoom;
            float cellHeight = fitHeight * zoom;
            contentW = Mathf.Max(localCanvas.width, cellWidth + 16f);
            contentH = Mathf.Max(localCanvas.height, cellHeight + 16f);
            cell = new Rect(
                (contentW - cellWidth) * 0.5f,
                (contentH - cellHeight) * 0.5f,
                cellWidth,
                cellHeight);
            return true;
        }

        static Vector2 CenteredPreviewScroll(float contentW, float contentH, Rect localCanvas)
        {
            return new Vector2(
                Mathf.Max(0f, (contentW - localCanvas.width) * 0.5f),
                Mathf.Max(0f, (contentH - localCanvas.height) * 0.5f));
        }

        void RecenterPreview(Rect localCanvas)
        {
            TryComputePreviewLayout(localCanvas, out _, out float contentW, out float contentH);
            _previewScroll = CenteredPreviewScroll(contentW, contentH, localCanvas);
            _previewPan = Vector2.zero;
            _status = "Recentered preview";
            Repaint();
        }

        bool PivotHandleContains(Rect cell, Vector2 mouse)
            => (mouse - PivotScreen(cell)).sqrMagnitude <= PivotHandleHitRadius * PivotHandleHitRadius;

        static Vector2 ScreenToPivot(Vector2 screen, Rect cell)
        {
            return new Vector2(
                Mathf.Clamp01((screen.x - cell.x) / Mathf.Max(1f, cell.width)),
                Mathf.Clamp01(1f - (screen.y - cell.y) / Mathf.Max(1f, cell.height)));
        }

        bool HandlePivotInput(int controlId, Rect cell)
        {
            if (!_showPivot)
                return false;

            var evt = Event.current;
            bool overHandle = PivotHandleContains(cell, evt.mousePosition);
            bool ownsDrag = _draggingPivot && GUIUtility.hotControl == controlId;

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && ownsDrag)
            {
                EndPivotDrag(controlId, save: true);
                evt.Use();
                Repaint();
                return true;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && overHandle)
            {
                RecordProfileUndo("Move Sprite Pivot");
                _draggingPivot = true;
                _pivotSelected = true;
                _playing = false;
                _selectedOnionFrame = -1;
                ClearColliderSelection();
                _selectedEventFrame = -1;
                GUIUtility.hotControl = controlId;
                GUIUtility.keyboardControl = controlId;
                _status = $"Pivot {_profile.Pivot.x:F2}, {_profile.Pivot.y:F2}";
                evt.Use();
                Repaint();
                return true;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                _pivotSelected = false;
                return false;
            }

            if (evt.type == EventType.MouseDrag && ownsDrag)
            {
                _profile.Pivot = ScreenToPivot(evt.mousePosition, cell);
                _status = $"Pivot {_profile.Pivot.x:F2}, {_profile.Pivot.y:F2}";
                evt.Use();
                Repaint();
                return true;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0 && ownsDrag)
            {
                EndPivotDrag(controlId, save: true);
                evt.Use();
                Repaint();
                return true;
            }

            return ownsDrag;
        }

        void EndPivotDrag(int controlId, bool save)
        {
            _draggingPivot = false;
            if (GUIUtility.hotControl == controlId)
                GUIUtility.hotControl = 0;
            if (save)
                SaveDirty();
        }

        static Rect CenteredSquareRect(Vector2 center, Vector2 edge, Rect bounds, float minimumRadius)
        {
            center = new Vector2(
                Mathf.Clamp(center.x, bounds.xMin, bounds.xMax),
                Mathf.Clamp(center.y, bounds.yMin, bounds.yMax));
            float radius = Mathf.Max(Mathf.Abs(edge.x - center.x), Mathf.Abs(edge.y - center.y));
            radius = Mathf.Max(radius, minimumRadius);
            float availableRadius = Mathf.Min(
                Mathf.Min(center.x - bounds.xMin, bounds.xMax - center.x),
                Mathf.Min(center.y - bounds.yMin, bounds.yMax - center.y));
            radius = Mathf.Clamp(radius, 0f, Mathf.Max(0f, availableRadius));
            return new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f);
        }

        static Rect UvToScreen(Rect uv, Rect cell) => new(
            cell.x + uv.x * cell.width,
            cell.y + uv.y * cell.height,
            uv.width * cell.width,
            uv.height * cell.height);

        static Rect ScreenToUv(Rect screen, Rect cell) => new(
            (screen.x - cell.x) / Mathf.Max(1f, cell.width),
            (screen.y - cell.y) / Mathf.Max(1f, cell.height),
            screen.width / Mathf.Max(1f, cell.width),
            screen.height / Mathf.Max(1f, cell.height));

        static SpriteColliderShape ColliderShapeOf(ColliderCreationMode mode)
            => mode == ColliderCreationMode.None
                ? SpriteColliderShape.Square
                : (SpriteColliderShape)(int)mode;

        static void DrawColliderUV(FrameBoxDef box, Rect cell, Color color, bool selected = false)
        {
            Vector2[] polygon = box.Shape == SpriteColliderShape.Polygon &&
                                (box.PolygonUV == null || box.PolygonUV.Length < 3)
                ? FrameBoxDef.CreateRegularPolygon()
                : box.PolygonUV;
            DrawColliderShape(UvToScreen(box.RectUV, cell), box.Shape, polygon, color, selected, box.Angle);
        }

        void DrawColliderSelectionBadge(FrameBoxDef box, Rect cell)
        {
            Vector2 top = ColliderHandlePosition(box, cell, ColliderHandleKind.EdgeT);
            var badge = new Rect(top.x - 35f, Mathf.Max(cell.yMin, top.y - 22f), 70f, 17f);
            EditorGUI.DrawRect(badge, new Color(0.06f, 0.12f, 0.18f, 0.94f));
            DrawBorder(badge, AccentColor, 1f);
            GUI.Label(badge, $" {box.Shape} #{box.Id}", _mutedStyle);
        }

        static void DrawColliderShape(Rect rect, SpriteColliderShape shape, Vector2[] polygon,
                                      Color color, bool selected, float angle = 0f)
        {
            if (shape == SpriteColliderShape.Square)
            {
                DrawRotatedScreenBox(rect, angle, color, selected ? 2f : 1f);
                return;
            }

            if (shape == SpriteColliderShape.Circle)
            {
                float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
                const int segments = 40;
                var points = new Vector3[segments + 1];
                for (int i = 0; i < segments; i++)
                {
                    float a = Mathf.PI * 2f * i / segments;
                    points[i] = new Vector3(
                        rect.center.x + Mathf.Cos(a) * radius,
                        rect.center.y + Mathf.Sin(a) * radius);
                }
                points[segments] = points[0];
                Handles.BeginGUI();
                Handles.color = color;
                Handles.DrawSolidDisc(rect.center, Vector3.forward, radius);
                Handles.color = new Color(color.r, color.g, color.b, 0.98f);
                Handles.DrawAAPolyLine(selected ? 2.5f : 1.5f, points);
                Handles.EndGUI();
                return;
            }

            polygon ??= FrameBoxDef.CreateRegularPolygon();
            var polygonOutline = PolygonScreenPoints(rect, polygon, true, angle);
            Handles.BeginGUI();
            Handles.color = new Color(color.r, color.g, color.b, 0.98f);
            Handles.DrawAAPolyLine(selected ? 2.5f : 1.5f, polygonOutline);
            Handles.EndGUI();
        }

        static bool ColliderContains(FrameBoxDef box, Rect cell, Vector2 point)
        {
            Rect rect = UvToScreen(box.RectUV, cell);
            Vector2 local = UnrotateAround(point, rect.center, box.Angle);
            if (box.Shape == SpriteColliderShape.Square)
                return rect.Contains(local);
            if (box.Shape == SpriteColliderShape.Circle)
            {
                float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
                return (local - rect.center).sqrMagnitude <= radius * radius;
            }

            Vector2[] polygon = box.PolygonUV != null && box.PolygonUV.Length >= 3
                ? box.PolygonUV
                : FrameBoxDef.CreateRegularPolygon();
            var points = PolygonScreenPoints(rect, polygon, false, 0f);
            bool inside = false;
            for (int i = 0, previous = points.Length - 1; i < points.Length; previous = i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[previous];
                if ((a.y > local.y) != (b.y > local.y) &&
                    local.x < (b.x - a.x) * (local.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        static Vector3[] PolygonScreenPoints(Rect rect, Vector2[] polygon, bool close, float angle = 0f)
        {
            int count = polygon.Length;
            var points = new Vector3[count + (close ? 1 : 0)];
            Vector2 center = rect.center;
            for (int i = 0; i < count; i++)
            {
                Vector2 p = new(
                    rect.x + polygon[i].x * rect.width,
                    rect.y + polygon[i].y * rect.height);
                if (Mathf.Abs(angle) > 0.01f)
                    p = RotateAround(p, center, angle);
                points[i] = p;
            }
            if (close)
                points[count] = points[0];
            return points;
        }

        static void DrawRotatedScreenBox(Rect rect, float angle, Color color, float thickness)
        {
            if (Mathf.Abs(angle) < 0.01f)
            {
                DrawScreenBox(rect, color, thickness);
                return;
            }
            var corners = RotatedRectCorners(rect, angle, false);
            var closed = new Vector3[] { corners[0], corners[1], corners[2], corners[3], corners[0] };
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawAAConvexPolygon(corners);
            Handles.color = new Color(color.r, color.g, color.b, 0.95f);
            Handles.DrawAAPolyLine(Mathf.Max(1.5f, thickness + 0.5f), closed);
            Handles.EndGUI();
        }

        static void DrawScreenBox(Rect rect, Color color, float thickness = 1f)
        {
            EditorGUI.DrawRect(rect, color);
            var border = new Color(color.r, color.g, color.b, 0.95f);
            DrawBorder(rect, border, thickness);
        }

        static Vector3[] RotatedRectCorners(Rect rect, float angle, bool close)
        {
            Vector2 center = rect.center;
            var local = new[]
            {
                new Vector2(rect.xMin, rect.yMin),
                new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax),
                new Vector2(rect.xMin, rect.yMax),
            };
            var points = new Vector3[close ? 5 : 4];
            for (int i = 0; i < 4; i++)
                points[i] = RotateAround(local[i], center, angle);
            if (close)
                points[4] = points[0];
            return points;
        }

        static Vector2 RotateAround(Vector2 point, Vector2 center, float degrees)
        {
            if (Mathf.Abs(degrees) < 0.01f)
                return point;
            float rad = degrees * Mathf.Deg2Rad;
            float s = Mathf.Sin(rad);
            float c = Mathf.Cos(rad);
            Vector2 d = point - center;
            return center + new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
        }

        static Vector2 UnrotateAround(Vector2 point, Vector2 center, float degrees)
            => RotateAround(point, center, -degrees);

        static Rect ColliderWorldAabb(FrameBoxDef box, Rect cell)
        {
            Rect rect = UvToScreen(box.RectUV, cell);
            if (Mathf.Abs(box.Angle) < 0.01f)
                return rect;
            var corners = RotatedRectCorners(rect, box.Angle, false);
            float xMin = corners[0].x, xMax = corners[0].x, yMin = corners[0].y, yMax = corners[0].y;
            for (int i = 1; i < 4; i++)
            {
                xMin = Mathf.Min(xMin, corners[i].x);
                xMax = Mathf.Max(xMax, corners[i].x);
                yMin = Mathf.Min(yMin, corners[i].y);
                yMax = Mathf.Max(yMax, corners[i].y);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        FrameBoxDef PrimarySelectedCollider()
        {
            if (_selectedColliders.Count != 1)
                return null;
            foreach (var box in _selectedColliders)
                return box;
            return null;
        }

        static GUIContent ColliderVisibilityContent(bool hidden)
        {
            var icon = EditorGUIUtility.IconContent(hidden
                ? "animationvisibilitytoggleoff"
                : "animationvisibilitytoggleon");
            if (icon != null && icon.image != null)
            {
                icon.tooltip = hidden
                    ? "Show this collider in the preview"
                    : "Hide this collider in the preview";
                return icon;
            }
            return new GUIContent(hidden ? "Show" : "Hide",
                hidden ? "Show this collider in the preview" : "Hide this collider in the preview");
        }

        static Vector2 ColliderHandlePosition(FrameBoxDef box, Rect cell, ColliderHandleKind kind)
        {
            Rect rect = UvToScreen(box.RectUV, cell);
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
                _ => center,
            };
            return RotateAround(local, center, box.Angle);
        }

        ColliderHandleKind HitSelectedColliderHandle(Rect cell, Vector2 mouse)
        {
            var box = PrimarySelectedCollider();
            if (box == null)
            {
                foreach (var selected in _selectedColliders)
                {
                    if (!selected.Hidden && ColliderContains(selected, cell, mouse))
                        return ColliderHandleKind.Body;
                }
                return ColliderHandleKind.None;
            }

            var kinds = new[]
            {
                ColliderHandleKind.Rotate,
                ColliderHandleKind.CornerTL, ColliderHandleKind.CornerTR,
                ColliderHandleKind.CornerBR, ColliderHandleKind.CornerBL,
                ColliderHandleKind.EdgeT, ColliderHandleKind.EdgeR,
                ColliderHandleKind.EdgeB, ColliderHandleKind.EdgeL,
            };
            float rotateHit = 10f * 10f;
            float knobHit = ColliderHandleSize * ColliderHandleSize;
            foreach (var kind in kinds)
            {
                float limit = kind == ColliderHandleKind.Rotate ? rotateHit : knobHit;
                if ((mouse - ColliderHandlePosition(box, cell, kind)).sqrMagnitude <= limit)
                    return kind;
            }
            if (ColliderContains(box, cell, mouse))
                return ColliderHandleKind.Body;
            return ColliderHandleKind.None;
        }

        void DrawColliderTransformGizmo(FrameBoxDef box, Rect cell)
        {
            Rect rect = UvToScreen(box.RectUV, cell);
            Vector2 center = rect.center;
            var outline = RotatedRectCorners(rect, box.Angle, true);
            Handles.BeginGUI();
            Handles.color = Color.white;
            Handles.DrawAAPolyLine(1.6f, outline);
            Vector2 top = ColliderHandlePosition(box, cell, ColliderHandleKind.EdgeT);
            Vector2 rotate = ColliderHandlePosition(box, cell, ColliderHandleKind.Rotate);
            Handles.DrawAAPolyLine(1.6f, top, rotate);
            Handles.DrawSolidDisc(rotate, Vector3.forward, 5f);
            Handles.color = AccentColor;
            Handles.DrawWireDisc(rotate, Vector3.forward, 7f);
            Handles.EndGUI();

            DrawHandleKnob(ColliderHandlePosition(box, cell, ColliderHandleKind.CornerTL));
            DrawHandleKnob(ColliderHandlePosition(box, cell, ColliderHandleKind.CornerTR));
            DrawHandleKnob(ColliderHandlePosition(box, cell, ColliderHandleKind.CornerBR));
            DrawHandleKnob(ColliderHandlePosition(box, cell, ColliderHandleKind.CornerBL));
            DrawHandleKnob(ColliderHandlePosition(box, cell, ColliderHandleKind.EdgeT), true);
            DrawHandleKnob(ColliderHandlePosition(box, cell, ColliderHandleKind.EdgeR), true);
            DrawHandleKnob(ColliderHandlePosition(box, cell, ColliderHandleKind.EdgeB), true);
            DrawHandleKnob(ColliderHandlePosition(box, cell, ColliderHandleKind.EdgeL), true);

            EditorGUIUtility.AddCursorRect(HandleCursorRect(rotate, 10f), MouseCursor.RotateArrow);
            EditorGUIUtility.AddCursorRect(HandleCursorRect(center, 12f), MouseCursor.MoveArrow);
            AddScaleCursors(box, cell);
        }

        static void DrawHandleKnob(Vector2 pos, bool edge = false, float size = 0f)
        {
            float s = size > 0.01f ? size : (edge ? 7f : 8f);
            EditorGUI.DrawRect(new Rect(pos.x - s * 0.5f, pos.y - s * 0.5f, s, s), new Color(0.05f, 0.06f, 0.08f, 0.95f));
            EditorGUI.DrawRect(new Rect(pos.x - s * 0.5f + 1f, pos.y - s * 0.5f + 1f, s - 2f, s - 2f), Color.white);
        }

        static Rect HandleCursorRect(Vector2 pos, float radius)
            => new(pos.x - radius, pos.y - radius, radius * 2f, radius * 2f);

        void AddScaleCursors(FrameBoxDef box, Rect cell)
        {
            float a = Mathf.Abs(Mathf.Repeat(box.Angle, 180f));
            bool swapped = a > 45f && a < 135f;
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(ColliderHandlePosition(box, cell, ColliderHandleKind.CornerTL), 8f),
                swapped ? MouseCursor.ResizeUpRight : MouseCursor.ResizeUpLeft);
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(ColliderHandlePosition(box, cell, ColliderHandleKind.CornerTR), 8f),
                swapped ? MouseCursor.ResizeUpLeft : MouseCursor.ResizeUpRight);
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(ColliderHandlePosition(box, cell, ColliderHandleKind.CornerBR), 8f),
                swapped ? MouseCursor.ResizeUpRight : MouseCursor.ResizeUpLeft);
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(ColliderHandlePosition(box, cell, ColliderHandleKind.CornerBL), 8f),
                swapped ? MouseCursor.ResizeUpLeft : MouseCursor.ResizeUpRight);
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(ColliderHandlePosition(box, cell, ColliderHandleKind.EdgeT), 8f),
                swapped ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(ColliderHandlePosition(box, cell, ColliderHandleKind.EdgeB), 8f),
                swapped ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(ColliderHandlePosition(box, cell, ColliderHandleKind.EdgeL), 8f),
                swapped ? MouseCursor.ResizeVertical : MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(
                HandleCursorRect(ColliderHandlePosition(box, cell, ColliderHandleKind.EdgeR), 8f),
                swapped ? MouseCursor.ResizeVertical : MouseCursor.ResizeHorizontal);
        }

        void HandleColliderTransformInput(int controlId, Rect cell, SpriteClipDef clip, int frame)
        {
            var evt = Event.current;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && _draggingColliderTransform)
            {
                RestoreColliderTransform();
                EndColliderTransform(controlId, save: false);
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                var kind = HitSelectedColliderHandle(cell, evt.mousePosition);
                if (kind == ColliderHandleKind.None)
                    return;
                FrameBoxDef box = PrimarySelectedCollider();
                if (box == null)
                {
                    foreach (var selected in _selectedColliders)
                    {
                        if (!selected.Hidden && ColliderContains(selected, cell, evt.mousePosition))
                        {
                            box = selected;
                            break;
                        }
                    }
                }
                if (box == null)
                    return;
                BeginColliderTransform(controlId, box, kind, cell, evt.mousePosition);
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDrag && _draggingColliderTransform &&
                GUIUtility.hotControl == controlId)
            {
                ApplyColliderTransform(cell, evt.mousePosition, evt.shift);
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0 && _draggingColliderTransform &&
                GUIUtility.hotControl == controlId)
            {
                EndColliderTransform(controlId, save: true);
                evt.Use();
                Repaint();
            }
        }

        void BeginColliderTransform(int controlId, FrameBoxDef box, ColliderHandleKind kind,
                                    Rect cell, Vector2 mouse)
        {
            _draggingColliderTransform = true;
            _colliderHandleKind = kind;
            _colliderTransformBox = box;
            _colliderTransformStartMouse = mouse;
            _colliderTransformUndoRecorded = false;
            Rect startRect = UvToScreen(box.RectUV, cell);
            _colliderTransformStartCenter = startRect.center;
            _colliderTransformStartAngle = box.Angle;
            _colliderTransformStartAtan = Mathf.Atan2(
                mouse.y - startRect.center.y, mouse.x - startRect.center.x);
            _colliderMoveBoxes.Clear();
            _colliderMoveStartRects.Clear();
            if (kind == ColliderHandleKind.Body)
            {
                foreach (var selected in _selectedColliders)
                {
                    _colliderMoveBoxes.Add(selected);
                    _colliderMoveStartRects.Add(selected.RectUV);
                }
            }
            else
            {
                _colliderMoveBoxes.Add(box);
                _colliderMoveStartRects.Add(box.RectUV);
            }
            GUIUtility.hotControl = controlId;
            GUIUtility.keyboardControl = controlId;
            _playing = false;
            _selectedOnionFrame = -1;
            _selectedEventFrame = -1;
        }

        void ApplyColliderTransform(Rect cell, Vector2 mouse, bool snap)
        {
            if (_colliderTransformBox == null || _colliderMoveBoxes.Count == 0)
                return;
            if (!_colliderTransformUndoRecorded)
            {
                RecordProfileUndo(_colliderHandleKind == ColliderHandleKind.Rotate
                    ? "Rotate Sprite Collider"
                    : _colliderHandleKind == ColliderHandleKind.Body
                        ? "Move Sprite Collider"
                        : "Scale Sprite Collider");
                _colliderTransformUndoRecorded = true;
            }

            if (_colliderHandleKind == ColliderHandleKind.Body)
            {
                Vector2 deltaUv = new(
                    (mouse.x - _colliderTransformStartMouse.x) / Mathf.Max(1f, cell.width),
                    (mouse.y - _colliderTransformStartMouse.y) / Mathf.Max(1f, cell.height));
                for (int i = 0; i < _colliderMoveBoxes.Count; i++)
                {
                    Rect start = _colliderMoveStartRects[i];
                    _colliderMoveBoxes[i].RectUV = new Rect(
                        start.x + deltaUv.x, start.y + deltaUv.y, start.width, start.height);
                }
                _status = "Moved collider";
                return;
            }

            var box = _colliderTransformBox;
            if (_colliderHandleKind == ColliderHandleKind.Rotate)
            {
                float atan = Mathf.Atan2(
                    mouse.y - _colliderTransformStartCenter.y,
                    mouse.x - _colliderTransformStartCenter.x);
                float delta = (atan - _colliderTransformStartAtan) * Mathf.Rad2Deg;
                float angle = _colliderTransformStartAngle + delta;
                if (snap)
                    angle = Mathf.Round(angle / 15f) * 15f;
                box.Angle = angle;
                _status = $"Collider angle {box.Angle:0.#}°";
                return;
            }

            Vector2 local = UnrotateAround(mouse, _colliderTransformStartCenter, _colliderTransformStartAngle)
                            - _colliderTransformStartCenter;
            Rect startScreen = UvToScreen(_colliderMoveStartRects[0], cell);
            float halfW = startScreen.width * 0.5f;
            float halfH = startScreen.height * 0.5f;
            Vector2 fixedLocal = _colliderHandleKind switch
            {
                ColliderHandleKind.CornerTL => new Vector2(halfW, halfH),
                ColliderHandleKind.CornerTR => new Vector2(-halfW, halfH),
                ColliderHandleKind.CornerBR => new Vector2(-halfW, -halfH),
                ColliderHandleKind.CornerBL => new Vector2(halfW, -halfH),
                ColliderHandleKind.EdgeT => new Vector2(0f, halfH),
                ColliderHandleKind.EdgeB => new Vector2(0f, -halfH),
                ColliderHandleKind.EdgeL => new Vector2(halfW, 0f),
                ColliderHandleKind.EdgeR => new Vector2(-halfW, 0f),
                _ => Vector2.zero,
            };

            float newHalfW = halfW;
            float newHalfH = halfH;
            Vector2 localCenter = Vector2.zero;
            switch (_colliderHandleKind)
            {
                case ColliderHandleKind.CornerTL:
                case ColliderHandleKind.CornerTR:
                case ColliderHandleKind.CornerBR:
                case ColliderHandleKind.CornerBL:
                    newHalfW = Mathf.Max(ColliderMinScreenHalf, Mathf.Abs(local.x - fixedLocal.x) * 0.5f);
                    newHalfH = Mathf.Max(ColliderMinScreenHalf, Mathf.Abs(local.y - fixedLocal.y) * 0.5f);
                    localCenter = (local + fixedLocal) * 0.5f;
                    break;
                case ColliderHandleKind.EdgeT:
                case ColliderHandleKind.EdgeB:
                    newHalfH = Mathf.Max(ColliderMinScreenHalf, Mathf.Abs(local.y - fixedLocal.y) * 0.5f);
                    localCenter = new Vector2(0f, (local.y + fixedLocal.y) * 0.5f);
                    break;
                case ColliderHandleKind.EdgeL:
                case ColliderHandleKind.EdgeR:
                    newHalfW = Mathf.Max(ColliderMinScreenHalf, Mathf.Abs(local.x - fixedLocal.x) * 0.5f);
                    localCenter = new Vector2((local.x + fixedLocal.x) * 0.5f, 0f);
                    break;
            }

            if (box.Shape == SpriteColliderShape.Circle)
            {
                float uniform = Mathf.Max(newHalfW, newHalfH);
                newHalfW = uniform;
                newHalfH = uniform;
            }

            Vector2 newCenter = _colliderTransformStartCenter +
                RotateAround(localCenter, Vector2.zero, _colliderTransformStartAngle);
            float uvW = (newHalfW * 2f) / Mathf.Max(1f, cell.width);
            float uvH = (newHalfH * 2f) / Mathf.Max(1f, cell.height);
            Vector2 uvCenter = ScreenToUvPoint(newCenter, cell);
            box.RectUV = new Rect(uvCenter.x - uvW * 0.5f, uvCenter.y - uvH * 0.5f, uvW, uvH);
            _status = "Scaled collider";
        }

        static Vector2 ScreenToUvPoint(Vector2 screen, Rect cell)
        {
            return new Vector2(
                (screen.x - cell.x) / Mathf.Max(1f, cell.width),
                (screen.y - cell.y) / Mathf.Max(1f, cell.height));
        }

        void RestoreColliderTransform()
        {
            for (int i = 0; i < _colliderMoveBoxes.Count; i++)
                _colliderMoveBoxes[i].RectUV = _colliderMoveStartRects[i];
            if (_colliderTransformBox != null)
                _colliderTransformBox.Angle = _colliderTransformStartAngle;
            _status = "Collider transform cancelled";
        }

        void EndColliderTransform(int controlId, bool save)
        {
            bool dirty = save && _colliderTransformUndoRecorded;
            _draggingColliderTransform = false;
            _colliderHandleKind = ColliderHandleKind.None;
            _colliderTransformBox = null;
            _colliderTransformUndoRecorded = false;
            _colliderMoveBoxes.Clear();
            _colliderMoveStartRects.Clear();
            if (GUIUtility.hotControl == controlId)
                GUIUtility.hotControl = 0;
            if (dirty)
                SaveDirty();
        }

        void ClearColliderTransform()
        {
            _draggingColliderTransform = false;
            _colliderHandleKind = ColliderHandleKind.None;
            _colliderTransformBox = null;
            _colliderTransformUndoRecorded = false;
            _colliderMoveBoxes.Clear();
            _colliderMoveStartRects.Clear();
        }

        static void DrawPanel(Rect rect)
        {
            EditorGUI.DrawRect(rect, PanelColor);
            DrawBorder(rect, BorderColor, 1f);
        }

        static void DrawBorder(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        static void DrawCheckerboard(Rect rect, float size)
        {
            var a = new Color(0.13f, 0.145f, 0.17f);
            var b = new Color(0.17f, 0.185f, 0.215f);
            int columns = Mathf.CeilToInt(rect.width / size);
            int rows = Mathf.CeilToInt(rect.height / size);
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                    EditorGUI.DrawRect(new Rect(
                        rect.x + x * size,
                        rect.y + y * size,
                        Mathf.Min(size, rect.xMax - (rect.x + x * size)),
                        Mathf.Min(size, rect.yMax - (rect.y + y * size))),
                        ((x + y) & 1) == 0 ? a : b);
        }

        static void DrawDiamond(Vector2 center, float radius, Color color)
        {
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawAAConvexPolygon(
                new Vector3(center.x, center.y - radius),
                new Vector3(center.x + radius, center.y),
                new Vector3(center.x, center.y + radius),
                new Vector3(center.x - radius, center.y));
            Handles.EndGUI();
        }

        void DrawIndependentMotionKeyDiamond(
            SpriteSocketMotionKey key, Vector2 center, Color color, bool selected)
        {
            if (selected)
                DrawDiamond(center, 9f, Color.white);
            float radius = selected ? 6f : 5f;
            DrawDiamond(center, radius, color);
            DrawIndependentMotionKeyBadges(key, center, radius);
        }

        void DrawIndependentMotionKeyBadges(
            SpriteSocketMotionKey key, Vector2 center, float radius)
        {
            Handles.BeginGUI();
            Color ink = new(0.08f, 0.1f, 0.12f, 0.95f);
            Handles.color = ink;
            byte path = key.PathMode;
            if (path == (byte)SpriteSocketPathMode.Hold ||
                path == (byte)SpriteSocketPathMode.None)
            {
                EditorGUI.DrawRect(new Rect(center.x - 1.6f, center.y - 1.6f, 3.2f, 3.2f), ink);
            }
            else if (path == (byte)SpriteSocketPathMode.Linear)
            {
                Handles.DrawAAPolyLine(1.6f,
                    new Vector3(center.x - radius * 0.45f, center.y),
                    new Vector3(center.x + radius * 0.45f, center.y));
            }
            else if (path == (byte)SpriteSocketPathMode.CubicBezier)
            {
                Handles.DrawSolidDisc(
                    new Vector3(center.x - 2.2f, center.y), Vector3.forward, 1.15f);
                Handles.DrawSolidDisc(
                    new Vector3(center.x + 2.2f, center.y), Vector3.forward, 1.15f);
            }
            else if (path == (byte)SpriteSocketPathMode.Hermite)
            {
                Handles.DrawAAPolyLine(1.4f,
                    new Vector3(center.x - 2.2f, center.y),
                    new Vector3(center.x + 2.2f, center.y));
                Handles.DrawAAPolyLine(1.4f,
                    new Vector3(center.x, center.y - 2.2f),
                    new Vector3(center.x, center.y + 2.2f));
            }
            else if (path == (byte)SpriteSocketPathMode.Arc)
            {
                Handles.DrawAAPolyLine(1.5f,
                    new Vector3(center.x - 2.3f, center.y + 1.1f),
                    new Vector3(center.x - 1.4f, center.y - 1.4f),
                    new Vector3(center.x + 1.4f, center.y - 1.4f),
                    new Vector3(center.x + 2.3f, center.y + 1.1f));
            }
            else
            {
                Handles.DrawSolidDisc(center, Vector3.forward, 1.35f);
            }

            Handles.color = IndependentMotionEasePipColor(key);
            Handles.DrawSolidDisc(
                new Vector3(center.x, center.y - radius + 0.4f),
                Vector3.forward, 1.7f);

            if (key.RotationMode != (byte)SpriteSocketRotationMode.Shortest &&
                key.RotationMode != (byte)SpriteSocketRotationMode.None)
            {
                Handles.color = new Color(1f, 0.86f, 0.35f, 0.98f);
                if (key.RotationMode == (byte)SpriteSocketRotationMode.Hold)
                    EditorGUI.DrawRect(
                        new Rect(center.x - 1.3f, center.y + radius - 2.2f, 2.6f, 2.6f),
                        Handles.color);
                else if (key.RotationMode == (byte)SpriteSocketRotationMode.FacePath)
                    Handles.DrawAAConvexPolygon(
                        new Vector3(center.x - 2f, center.y + radius - 0.2f),
                        new Vector3(center.x + 2f, center.y + radius - 0.2f),
                        new Vector3(center.x, center.y + radius + 2.4f));
                else
                    Handles.DrawSolidDisc(
                        new Vector3(center.x, center.y + radius - 0.2f),
                        Vector3.forward, 1.55f);
            }
            Handles.EndGUI();
        }

        static Color IndependentMotionEasePipColor(SpriteSocketMotionKey key)
        {
            if (key.UseCustomEase)
                return new Color(0.35f, 0.9f, 1f, 1f);
            if (key.AllowOvershoot ||
                key.EaseMode >= (byte)SpriteEaseMode.BackIn)
                return new Color(1f, 0.78f, 0.28f, 1f);
            if (key.EaseMode == (byte)SpriteEaseMode.Linear ||
                key.EaseMode == (byte)SpriteEaseMode.Step ||
                key.EaseMode == (byte)SpriteEaseMode.None)
                return new Color(0.78f, 0.8f, 0.84f, 1f);
            return Color.white;
        }

        static string IndependentMotionKeyTooltip(SpriteSocketMotionKey key)
        {
            string ease = key.UseCustomEase
                ? "Custom Curve"
                : ResolvedEaseMode(key).ToString();
            if (key.AllowOvershoot)
                ease += " + Overshoot";
            return $"{ease}  •  {ResolvedPathMode(key)}  •  {ResolvedRotationMode(key)}";
        }

        static void DrawTriangle(Vector2 top, float radius, Color color)
        {
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawAAConvexPolygon(
                new Vector3(top.x - radius, top.y),
                new Vector3(top.x + radius, top.y),
                new Vector3(top.x, top.y + radius));
            Handles.EndGUI();
        }

        void SectionLabel(string text)
        {
            GUILayout.Label(text, _sectionStyle);
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BorderColor);
            GUILayout.Space(3f);
        }

        void DrawColliderModeButton(ColliderCreationMode mode, string label, GUIStyle style)
        {
            bool active = _colliderCreationMode == mode;
            bool next = GUILayout.Toggle(active,
                new GUIContent(label, active
                    ? $"{label} creation is armed. Click again to cancel."
                    : $"Arm the {label} collider creation tool."),
                style);
            if (next != active)
            {
                if (next)
                {
                    _colliderCreationMode = mode;
                    _draggingBox = false;
                    ClearPolygonDraft();
                    ClearColliderSelection();
                    _selectedEventFrame = -1;
                    _selectedOnionFrame = -1;
                    CancelSocketPlacement(null);
                    _status = $"{label} collider tool armed";
                }
                else
                {
                    CancelColliderCreation("Collider creation cancelled");
                }
                Repaint();
            }
        }

        static bool ResetValueButton(string tooltip)
        {
            return GUILayout.Button(new GUIContent("Reset", tooltip), GUILayout.Width(48f));
        }

        static bool IsSpaceKey(Event evt)
            => evt.keyCode == KeyCode.Space || evt.character == ' ';

        static string DrawStringTextField(string label, string value, string id)
        {
            GUI.SetNextControlName(StringFieldControlPrefix + id);
            return EditorGUILayout.DelayedTextField(label, value ?? string.Empty);
        }

        static string DrawStringTextField(GUIContent label, string value, string id)
        {
            GUI.SetNextControlName(StringFieldControlPrefix + id);
            return EditorGUILayout.DelayedTextField(label, value ?? string.Empty);
        }

        bool IsEditingStringTextField()
        {
            if (_renamingClip >= 0)
                return true;
            if (!EditorGUIUtility.editingTextField)
                return false;
            string focused = GUI.GetNameOfFocusedControl();
            return focused == ClipRenameControl ||
                   (!string.IsNullOrEmpty(focused) && focused.StartsWith(StringFieldControlPrefix));
        }

        bool IsEditingAnyTextField()
            => _renamingClip >= 0 || _renamingSheet >= 0 || EditorGUIUtility.editingTextField;

        void ReleaseShortcutKeyboardFocus()
        {
            GUIUtility.keyboardControl = 0;
            GUI.FocusControl(null);
            Focus();
        }

        bool TryTogglePlaybackFromSpace()
        {
            double now = EditorApplication.timeSinceStartup;
            // KeyCode.Space and character == ' ' can arrive as one event or a pair.
            if (now - _lastSpaceToggleTime < 0.08d)
                return false;
            _lastSpaceToggleTime = now;
            if (_spacePlaysBothClocks)
            {
                bool anyPlaying = _playing || _socketPlaying;
                bool next = !anyPlaying;
                if (CurrentClip != null)
                    _playing = next;
                _socketPlaying = next;
                _lastEditorTime = now;
                _status = next
                    ? "Frames and Independent Motion playing"
                    : "Playback paused";
                return CurrentClip != null || next;
            }
            if (_timelineView == TimelineView.Sockets)
            {
                _socketPlaying = !_socketPlaying;
                _playing = false;
            }
            else
            {
                if (CurrentClip == null)
                    return false;
                _playing = !_playing;
                _socketPlaying = false;
            }
            _lastEditorTime = now;
            bool active = _timelineView == TimelineView.Sockets
                ? _socketPlaying
                : _playing;
            _status = active ? "Playback started" : "Playback paused";
            return true;
        }

        void HandleGlobalShortcuts()
        {
            var evt = Event.current;
            if (evt.type != EventType.KeyDown)
                return;

            if (IsSpaceKey(evt))
            {
                if (IsEditingStringTextField())
                    return;

                ReleaseShortcutKeyboardFocus();
                TryTogglePlaybackFromSpace();
                evt.Use();
                Repaint();
                return;
            }

            if (_timelineView == TimelineView.Sockets &&
                evt.keyCode == KeyCode.K && !evt.control && !evt.command && !evt.alt)
            {
                if (IsEditingAnyTextField())
                    return;
                ReleaseShortcutKeyboardFocus();
                InsertIndependentMotionKey(evt.shift);
                evt.Use();
                return;
            }

            if ((evt.control || evt.command) && evt.keyCode == KeyCode.O)
            {
                if (IsEditingAnyTextField())
                    return;
                evt.Use();
                ShowLoadProfilePopup();
                return;
            }

            if (evt.keyCode == KeyCode.F2)
            {
                if (_renamingClip >= 0 || _renamingSheet >= 0)
                    return;
                if (CurrentClip == null)
                {
                    if (_profile?.Sheets != null && _profile.Sheets.Count > 0)
                    {
                        BeginSheetRename(_selectedSheet);
                        evt.Use();
                        Repaint();
                    }
                    return;
                }
                string focused = GUI.GetNameOfFocusedControl();
                bool editingOther = EditorGUIUtility.editingTextField &&
                    !string.IsNullOrEmpty(focused) &&
                    focused.StartsWith(StringFieldControlPrefix) &&
                    focused != StringFieldControlPrefix + "ClipName";
                if (editingOther)
                    return;
                BeginClipRename(_selectedClip);
                evt.Use();
                Repaint();
                return;
            }

            if (evt.keyCode is KeyCode.Delete or KeyCode.Backspace)
            {
                if (IsEditingAnyTextField())
                    return;
                if (_showSocketTransformPanel || _showSocketInheritPanel)
                    return;

                if (evt.keyCode == KeyCode.Backspace &&
                    _colliderCreationMode == ColliderCreationMode.Polygon && _polygonDraftUV.Count > 0)
                {
                    RemoveLastPolygonVertex();
                    evt.Use();
                    Repaint();
                    return;
                }

                ReleaseShortcutKeyboardFocus();
                PruneColliderSelection(CurrentClip, _selectedFrame);
                PruneSocketSelection(CurrentClip);
                PruneEventSelection(CurrentClip);
                PruneSocketDrawSelection(CurrentClip);
                if (_selectedSocketTriggerTrack >= 0 && _selectedSocketTriggerIndex >= 0)
                {
                    DeleteSocketTrigger(_selectedSocketTriggerTrack, _selectedSocketTriggerIndex);
                    evt.Use();
                    return;
                }
                if (_selectedSocketMotionKeys.Count > 0)
                {
                    DeleteSelectedSocketMotionKeys();
                    evt.Use();
                    return;
                }
                if (_selectedSocketDrawFrame >= 0)
                {
                    ClearSocketDrawKey(CurrentClip, _selectedSocketDrawFrame, _selectedSocketDrawName);
                    evt.Use();
                    return;
                }
                if (_selectedColliders.Count > 0 || _selectedSockets.Count > 0)
                {
                    DeleteSelectedPreviewObjects();
                    evt.Use();
                    return;
                }
                if (_selectedEventFrame >= 0)
                {
                    DeleteSelectedEventMarker();
                    evt.Use();
                    return;
                }

                var clip = CurrentClip;
                if (clip != null)
                {
                    if (clip.Frames.Length > 1)
                        RemoveSelectedFrames(clip);
                    else
                        _status = "A clip must keep at least one frame";
                }
                evt.Use();
                Repaint();
                return;
            }

            if (evt.keyCode == KeyCode.Escape && _timelineDragMode != TimelineDragMode.None)
            {
                CancelTimelineDrag();
                evt.Use();
                Repaint();
                return;
            }

            if (IsEditingAnyTextField())
                return;

            if (_selectedOnionFrame < 0 && CurrentClip != null &&
                evt.keyCode is KeyCode.LeftArrow or KeyCode.RightArrow)
            {
                StepFrame(CurrentClip, evt.keyCode == KeyCode.LeftArrow ? -1 : 1);
                evt.Use();
                return;
            }

            bool actionModifier = evt.control || evt.command;
            if (actionModifier && evt.keyCode == KeyCode.A && CurrentClip != null)
            {
                SelectAllPreviewObjects(CurrentClip, _selectedFrame);
                evt.Use();
                return;
            }

            if (evt.keyCode == KeyCode.Escape)
            {
                if (_showSocketTransformPanel)
                {
                    CloseSocketTransformPanel();
                    _status = "Socket transform panel closed";
                    evt.Use();
                    Repaint();
                    return;
                }
                if (_showSocketInheritPanel)
                {
                    CloseSocketInheritPanel();
                    _status = "Socket frame panel closed";
                    evt.Use();
                    Repaint();
                    return;
                }
                if (_draggingColliderTransform)
                    RestoreColliderTransform();
                bool hadSelection = _selectedColliders.Count > 0 || _selectedSockets.Count > 0 ||
                                    _selectedEventFrame >= 0 || _selectedSocketDrawFrame >= 0 ||
                                    _selectedOnionFrame >= 0 || _colliderCreationMode != ColliderCreationMode.None ||
                                    _colliderMarqueePending || _socketPlacementArmed ||
                                    !string.IsNullOrEmpty(_selectedSocketName) ||
                                    _draggingPivot || _pivotSelected || _draggingColliderTransform ||
                                    _draggingSocket;
                ClearColliderSelection();
                _selectedEventFrame = -1;
                _selectedSocketDrawFrame = -1;
                _selectedSocketDrawName = null;
                _selectedOnionFrame = -1;
                CancelColliderCreation("Selection and active tools cleared");
                CancelSocketPlacement(null);
                _draggingSocket = false;
                _socketHandleKind = ColliderHandleKind.None;
                _socketTransformName = null;
                _socketMoveNames.Clear();
                _socketMoveStarts.Clear();
                _draggingOnion = false;
                _draggingPivot = false;
                _pivotSelected = false;
                if (_colliderMarqueePending)
                    GUIUtility.hotControl = 0;
                _colliderMarqueePending = false;
                _draggingColliderMarquee = false;
                _previewMarqueeHotControl = 0;
                if (hadSelection)
                {
                    _status = "Selection and active tools cleared";
                    evt.Use();
                    Repaint();
                }
            }
        }

        void OnUndoRedo()
        {
            var undoSource = _asset != null ? _asset : _undoProxy;
            if (undoSource != null)
            {
                _profile = undoSource.Data ?? new SpriteSheetProfile();
                if (undoSource.Data == null)
                    undoSource.Data = _profile;
            }
            EnsureProfile();
            if (_profile.Clips == null || _profile.Clips.Count == 0)
                _selectedClip = -1;
            else if (_selectedClip >= 0)
                _selectedClip = Mathf.Clamp(_selectedClip, 0, _profile.Clips.Count - 1);
            if (_profile.Sheets != null && _profile.Sheets.Count > 0)
                _selectedSheet = Mathf.Clamp(_selectedSheet, 0, _profile.Sheets.Count - 1);
            if (CurrentClip != null)
            {
                _selectedSheet = CurrentClip.SheetIndex;
                _profile.SyncLegacyFromSheet(_selectedSheet);
            }
            InvalidateSheetPixelCache();
            var clip = CurrentClip;
            if (clip != null)
            {
                _selectedFrame = Mathf.Clamp(_selectedFrame, 0, clip.Frames.Length - 1);
                EnsureFrameSelection(clip.Frames.Length);
            }
            ClearColliderSelection();
            PruneEventSelection(clip);
            _colliderMarqueePending = false;
            _draggingColliderMarquee = false;
            _previewMarqueeHotControl = 0;
            _draggingBox = false;
            _draggingOnion = false;
            _draggingSocket = false;
            _socketHandleKind = ColliderHandleKind.None;
            _socketTransformName = null;
            _draggingPivot = false;
            _pivotSelected = false;
            ClearColliderTransform();
            CancelSocketPlacement(null);
            _selectedSocketName = null;
            ClearPolygonDraft();
            if (_timelineDragMode != TimelineDragMode.None)
                EndTimelineDrag();
            _status = "Undo/Redo applied";
            Repaint();
        }

        void PrepareInspectorUndo()
        {
            var evt = Event.current;
            if (evt.type is EventType.ExecuteCommand or EventType.ValidateCommand)
                return;
            if (_timelineDragMode != TimelineDragMode.None)
                return;
            if (evt.type is EventType.MouseDown or EventType.KeyDown or EventType.DragPerform)
                RecordProfileUndo("Edit Sprite Animator");
        }


        ScriptableSpriteSheetProfile UndoTarget
        {
            get
            {
                BindProfileToUndoTarget();
                return _asset != null ? _asset : _undoProxy;
            }
        }

        void BindProfileToUndoTarget()
        {
            if (_profile == null)
                return;
            if (_asset != null)
            {
                _asset.Data = _profile;
                return;
            }
            if (_undoProxy == null)
            {
                _undoProxy = CreateInstance<ScriptableSpriteSheetProfile>();
                _undoProxy.hideFlags = HideFlags.HideAndDontSave;
                _undoProxy.name = "DOTS Sprite Animator Undo";
            }
            _undoProxy.Data = _profile;
        }

        void RecordDiscreteUndo(string operation)
        {
            var target = UndoTarget;
            if (target == null)
                return;
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(target, operation);
            Undo.SetCurrentGroupName(operation);
            Undo.FlushUndoRecordObjects();
            EditorUtility.SetDirty(target);
            PushUndoName(operation);
        }

        void SealUndoGroup()
        {
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
        }

        void RecordProfileUndo(string operation)
        {
            BindProfileToUndoTarget();
            if (_asset != null)
                Undo.RecordObjects(new UnityEngine.Object[] { _asset, this }, operation);
            else if (_undoProxy != null)
                Undo.RecordObjects(new UnityEngine.Object[] { _undoProxy, this }, operation);
            else
                Undo.RecordObject(this, operation);
            PushUndoName(operation);
        }

        void RecordWindowUndo(string operation)
        {
            Undo.RecordObject(this, operation);
            PushUndoName(operation);
        }


        void PushUndoName(string operation)
        {
            if (string.IsNullOrEmpty(operation) || operation == "Edit Sprite Animator")
                return;
            _undoNames.Add(operation);
            _redoNames.Clear();
        }

        void OnUndoRedoEvent(in UndoRedoInfo info)
        {
            if (info.isRedo)
            {
                if (_redoNames.Count == 0 || _redoNames[^1] != info.undoName)
                    return;
                int last = _redoNames.Count - 1;
                _undoNames.Add(_redoNames[last]);
                _redoNames.RemoveAt(last);
            }
            else if (_undoNames.Count > 0 && _undoNames[^1] == info.undoName)
            {
                int last = _undoNames.Count - 1;
                _redoNames.Add(_undoNames[last]);
                _undoNames.RemoveAt(last);
            }
            Repaint();
        }

        void DrawSocketInheritOverlay()
        {
            if (!_showSocketInheritPanel)
                return;
            var clip = SocketInheritClip();
            if (clip?.Frames == null)
                return;

            float width = Mathf.Clamp(_socketInheritPanelRect.width, 360f, Mathf.Max(360f, position.width - 16f));
            float height = Mathf.Clamp(_socketInheritPanelRect.height, 320f, Mathf.Max(320f, position.height - 24f));
            float x = Mathf.Clamp(_socketInheritPanelRect.x, 8f, Mathf.Max(8f, position.width - width - 8f));
            float y = Mathf.Clamp(_socketInheritPanelRect.y, 8f, Mathf.Max(8f, position.height - height - 8f));
            _socketInheritPanelRect = new Rect(x, y, width, height);

            var evt = Event.current;
            var shade = new Rect(Vector2.zero, position.size);
            int controlId = GUIUtility.GetControlID(
                "SpriteSocketInheritOverlay".GetHashCode(), FocusType.Passive, shade);
            bool owns = GUIUtility.hotControl == controlId;

            if (evt.type == EventType.Repaint)
                EditorGUI.DrawRect(shade, new Color(0f, 0f, 0f, 0.45f));

            var title = new Rect(_socketInheritPanelRect.x, _socketInheritPanelRect.y, width, 26f);
            if (evt.GetTypeForControl(controlId) == EventType.MouseDown && evt.button == 0 &&
                title.Contains(evt.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                _socketInheritDragging = true;
                _socketInheritDragOffset = evt.mousePosition - _socketInheritPanelRect.position;
                GUI.FocusControl(null);
                evt.Use();
            }
            else if (evt.GetTypeForControl(controlId) == EventType.MouseDrag && owns && _socketInheritDragging)
            {
                _socketInheritPanelRect.position = evt.mousePosition - _socketInheritDragOffset;
                evt.Use();
                Repaint();
            }
            else if (evt.GetTypeForControl(controlId) == EventType.MouseUp && owns && _socketInheritDragging)
            {
                GUIUtility.hotControl = 0;
                _socketInheritDragging = false;
                evt.Use();
            }

            EditorGUI.DrawRect(_socketInheritPanelRect, new Color(0.09f, 0.11f, 0.14f, 0.98f));
            DrawBorder(_socketInheritPanelRect, AccentColor, 2f);
            EditorGUI.DrawRect(title, new Color(0.14f, 0.22f, 0.3f, 1f));
            GUI.Label(new Rect(title.x + 8f, title.y + 4f, title.width - 16f, 18f),
                "Socket Frames", EditorStyles.boldLabel);

            var body = new Rect(
                _socketInheritPanelRect.x + 8f,
                _socketInheritPanelRect.y + 30f,
                _socketInheritPanelRect.width - 16f,
                _socketInheritPanelRect.height - 38f);
            GUILayout.BeginArea(body);
            DrawSocketInheritContents(clip);
            GUILayout.EndArea();

            EventType forControl = evt.GetTypeForControl(controlId);
            if (forControl == EventType.MouseDown && !_socketInheritDragging)
            {
                if (!_socketInheritPanelRect.Contains(evt.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    GUI.FocusControl(null);
                    CloseSocketInheritPanel();
                    evt.Use();
                    Repaint();
                }
                return;
            }

            if (forControl == EventType.MouseUp && owns)
            {
                GUIUtility.hotControl = 0;
                evt.Use();
            }
            else if (forControl is EventType.MouseDrag or EventType.ScrollWheel or EventType.ContextClick)
            {
                evt.Use();
            }
        }

        void DrawSocketTransformOverlay()
        {
            if (!_showSocketTransformPanel)
                return;
            var clip = CurrentClip;
            if (clip == null || _socketTransformNames.Count == 0)
            {
                CloseSocketTransformPanel();
                return;
            }

            float width = Mathf.Clamp(_socketTransformPanelRect.width, 280f, Mathf.Max(280f, position.width - 16f));
            float height = Mathf.Clamp(_socketTransformPanelRect.height, 300f, Mathf.Max(300f, position.height - 24f));
            float x = Mathf.Clamp(_socketTransformPanelRect.x, 8f, Mathf.Max(8f, position.width - width - 8f));
            float y = Mathf.Clamp(_socketTransformPanelRect.y, 8f, Mathf.Max(8f, position.height - height - 8f));
            _socketTransformPanelRect = new Rect(x, y, width, height);

            var evt = Event.current;
            var shade = new Rect(Vector2.zero, position.size);
            int controlId = GUIUtility.GetControlID(
                "SpriteSocketTransformOverlay".GetHashCode(), FocusType.Passive, shade);
            bool owns = GUIUtility.hotControl == controlId;

            if (evt.type == EventType.Repaint)
                EditorGUI.DrawRect(shade, new Color(0f, 0f, 0f, 0.45f));

            var title = new Rect(_socketTransformPanelRect.x, _socketTransformPanelRect.y, width, 26f);
            if (evt.GetTypeForControl(controlId) == EventType.MouseDown && evt.button == 0 &&
                title.Contains(evt.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                _socketTransformDragging = true;
                _socketTransformDragOffset = evt.mousePosition - _socketTransformPanelRect.position;
                GUI.FocusControl(null);
                evt.Use();
            }
            else if (evt.GetTypeForControl(controlId) == EventType.MouseDrag && owns && _socketTransformDragging)
            {
                _socketTransformPanelRect.position = evt.mousePosition - _socketTransformDragOffset;
                evt.Use();
                Repaint();
            }
            else if (evt.GetTypeForControl(controlId) == EventType.MouseUp && owns && _socketTransformDragging)
            {
                GUIUtility.hotControl = 0;
                _socketTransformDragging = false;
                evt.Use();
            }

            EditorGUI.DrawRect(_socketTransformPanelRect, new Color(0.09f, 0.11f, 0.14f, 0.98f));
            DrawBorder(_socketTransformPanelRect, AccentColor, 2f);
            EditorGUI.DrawRect(title, new Color(0.14f, 0.22f, 0.3f, 1f));
            GUI.Label(new Rect(title.x + 8f, title.y + 4f, title.width - 16f, 18f),
                "Set Transform", EditorStyles.boldLabel);

            var body = new Rect(
                _socketTransformPanelRect.x + 10f,
                _socketTransformPanelRect.y + 32f,
                _socketTransformPanelRect.width - 20f,
                _socketTransformPanelRect.height - 42f);
            GUILayout.BeginArea(body);
            DrawSocketTransformContents(clip);
            GUILayout.EndArea();

            EventType forControl = evt.GetTypeForControl(controlId);
            if (forControl == EventType.MouseDown && !_socketTransformDragging)
            {
                if (!_socketTransformPanelRect.Contains(evt.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    GUI.FocusControl(null);
                    CloseSocketTransformPanel();
                    evt.Use();
                    Repaint();
                }
                return;
            }

            if (forControl == EventType.MouseUp && owns)
            {
                GUIUtility.hotControl = 0;
                evt.Use();
            }
            else if (forControl is EventType.MouseDrag or EventType.ScrollWheel or EventType.ContextClick)
            {
                evt.Use();
            }
        }

        void DrawSocketTransformContents(SpriteClipDef clip)
        {
            if (clip?.Sockets == null || _socketTransformNames.Count == 0)
            {
                GUILayout.Label("No sockets.", _mutedStyle);
                return;
            }

            _profile.EnsureSocketCatalog();
            string label = _socketTransformNames.Count == 1
                ? _socketTransformNames[0]
                : $"{_socketTransformNames.Count} sockets";
            GUILayout.Label($"{label}  •  {clip.Name}  •  frame {_selectedFrame + 1}", EditorStyles.boldLabel);
            GUILayout.Label("Pose is per frame. Pivot is the preview art grip (0–1).", _mutedStyle);

            Vector2 position = Vector2.zero;
            float angle = 0f;
            Vector2 scale = Vector2.one;
            Vector2 pivot = new Vector2(0.5f, 0.5f);
            bool mixedPos = false;
            bool mixedAngle = false;
            bool mixedScale = false;
            bool mixedPivot = false;
            bool hasPose = false;
            bool hasPivot = false;
            for (int i = 0; i < _socketTransformNames.Count; i++)
            {
                string name = _socketTransformNames[i];
                if (SpriteSocketKeys.TryGetPose(clip.Sockets, name, _selectedFrame,
                        out var pose, out var poseAngle, out var poseScale, out _))
                {
                    if (!hasPose)
                    {
                        position = pose;
                        angle = poseAngle;
                        scale = SpriteSocketKeys.ResolvedScale(poseScale);
                        hasPose = true;
                    }
                    else
                    {
                        if (pose != position)
                            mixedPos = true;
                        if (!Mathf.Approximately(poseAngle, angle))
                            mixedAngle = true;
                        if (SpriteSocketKeys.ResolvedScale(poseScale) != scale)
                            mixedScale = true;
                    }
                }

                var item = _profile.SocketCatalog.Find(name);
                Vector2 itemPivot = item != null ? item.Pivot : new Vector2(0.5f, 0.5f);
                if (!hasPivot)
                {
                    pivot = itemPivot;
                    hasPivot = true;
                }
                else if (itemPivot != pivot)
                    mixedPivot = true;
            }

            _socketTransformAllFrames = EditorGUILayout.Toggle(
                new GUIContent("All Frames",
                    "Write position, rotation, and scale onto every key of the selected sockets."),
                _socketTransformAllFrames);

            EditorGUI.showMixedValue = mixedPos;
            EditorGUI.BeginChangeCheck();
            Vector2 nextPos = EditorGUILayout.Vector2Field("Position (px)", position);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                WriteSocketTransformPose(clip, nextPos, angle, scale, writePos: true, writeAngle: false, writeScale: false);

            EditorGUI.showMixedValue = mixedAngle;
            EditorGUI.BeginChangeCheck();
            float nextAngle = EditorGUILayout.FloatField("Rotation (deg)", angle);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                WriteSocketTransformPose(clip, nextPos, nextAngle, scale, writePos: false, writeAngle: true, writeScale: false);

            EditorGUI.showMixedValue = mixedScale;
            EditorGUI.BeginChangeCheck();
            Vector2 nextScale = EditorGUILayout.Vector2Field("Scale", scale);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                WriteSocketTransformPose(clip, nextPos, nextAngle, nextScale,
                    writePos: false, writeAngle: false, writeScale: true);

            EditorGUI.showMixedValue = mixedPivot;
            EditorGUI.BeginChangeCheck();
            Vector2 nextPivot = EditorGUILayout.Vector2Field(
                new GUIContent("Pivot", "Normalized sprite pivot (0-1). Same for every frame."),
                pivot);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                WriteSocketTransformPivot(nextPivot);

            GUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Reset Pose",
                        "Position 0,0  •  rotation 0°  •  scale 1,1")))
                    WriteSocketTransformPose(clip, Vector2.zero, 0f, Vector2.one,
                        writePos: true, writeAngle: true, writeScale: true);
                if (GUILayout.Button(new GUIContent("Reset Pivot", "Pivot 0.5, 0.5")))
                    WriteSocketTransformPivot(new Vector2(0.5f, 0.5f));
            }
            if (GUILayout.Button("Close"))
            {
                CloseSocketTransformPanel();
                GUIUtility.ExitGUI();
            }
        }

        void WriteSocketTransformPose(SpriteClipDef clip, Vector2 position, float angle, Vector2 scale,
            bool writePos, bool writeAngle, bool writeScale)
        {
            if (clip == null || _socketTransformNames.Count == 0)
                return;
            RecordProfileUndo("Set Socket Transform");
            scale = SpriteSocketKeys.ResolvedScale(scale);
            for (int n = 0; n < _socketTransformNames.Count; n++)
            {
                string name = _socketTransformNames[n];
                if (_socketTransformAllFrames)
                {
                    SpriteSocketKeys.CollectKeysSorted(clip.Sockets, name, _socketPathKeys);
                    if (_socketPathKeys.Count == 0)
                    {
                        WriteSocketTransformKey(
                            SpriteSocketKeys.EnsureFrameKey(clip.Sockets, name, _selectedFrame),
                            position, angle, scale, writePos, writeAngle, writeScale);
                        continue;
                    }
                    for (int k = 0; k < _socketPathKeys.Count; k++)
                        WriteSocketTransformKey(_socketPathKeys[k], position, angle, scale,
                            writePos, writeAngle, writeScale);
                }
                else
                {
                    WriteSocketTransformKey(
                        SpriteSocketKeys.EnsureFrameKey(clip.Sockets, name, _selectedFrame),
                        position, angle, scale, writePos, writeAngle, writeScale);
                }
            }

            _status = _socketTransformAllFrames
                ? $"Set transform on {_socketTransformNames.Count} sockets  •  all frames"
                : $"Set transform on {_socketTransformNames.Count} sockets  •  frame {_selectedFrame + 1}";
            SaveDirty();
            Repaint();
        }

        static void WriteSocketTransformKey(FrameSocketDef key, Vector2 position, float angle, Vector2 scale,
            bool writePos, bool writeAngle, bool writeScale)
        {
            if (key == null)
                return;
            if (writePos)
                key.LocalPosition = new Vector2(Mathf.Round(position.x * 100f) / 100f, Mathf.Round(position.y * 100f) / 100f);
            if (writeAngle)
                key.LocalAngle = angle;
            if (writeScale)
                key.LocalScale = scale;
        }

        void WriteSocketTransformPivot(Vector2 pivot)
        {
            if (_socketTransformNames.Count == 0)
                return;
            RecordProfileUndo("Set Socket Pivot");
            pivot = new Vector2(Mathf.Clamp01(pivot.x), Mathf.Clamp01(pivot.y));
            _profile.EnsureSocketCatalog();
            for (int i = 0; i < _socketTransformNames.Count; i++)
                _profile.SocketCatalog.Ensure(_socketTransformNames[i]).Pivot = pivot;
            _status = $"Pivot {pivot.x:0.##}, {pivot.y:0.##}  •  {_socketTransformNames.Count} sockets";
            SaveDirty();
            Repaint();
        }

        static bool DrawSocketInheritChannelToggle(string label, string tooltip, bool on, GUIStyle style)
        {
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = on
                ? new Color(0.22f, 0.55f, 0.92f, 1f)
                : new Color(0.32f, 0.32f, 0.32f, 1f);
            bool next = GUILayout.Toggle(on, new GUIContent(label, tooltip), style);
            GUI.backgroundColor = prev;
            return next;
        }

        void DrawSocketInheritContents(SpriteClipDef clip)
        {
            if (clip?.Frames == null || _socketInheritNames.Count == 0)
            {
                GUILayout.Label("No clip.", _mutedStyle);
                return;
            }

            string socketLabel = _socketInheritNames.Count == 1
                ? _socketInheritNames[0]
                : $"{_socketInheritNames.Count} sockets";
            int source = Mathf.Clamp(_socketInheritSourceFrame, 0, clip.Frames.Length - 1);
            SpriteSocketKeys.TryGetPose(clip.Sockets, _socketInheritNames[0], source,
                out var sourcePos, out var sourceAngle, out var sourceScale, out bool sourceKeyed);
            bool sourceBehind = SocketInheritDrawsBehind(clip, _socketInheritNames[0], source);
            float sourceTime = AuthoredStartTime(clip, source);
            float sourceDur = FrameDuration(clip, source);
            GUILayout.Label($"{socketLabel}  •  {clip.Name}", EditorStyles.boldLabel);
            GUILayout.Label(
                $"Source  {sourceTime:0.00}s  ({sourceDur:0.00}s)  •  frame {source + 1}" +
                (sourceKeyed ? "  key" : "  inherited") +
                $"   ({sourcePos.x:0.#}, {sourcePos.y:0.#})  {sourceAngle:0.#}°  {sourceScale.x:0.##},{sourceScale.y:0.##}",
                _mutedStyle);
            GUILayout.Label(
                sourceBehind
                    ? "Now  Behind  (purple)  under the character"
                    : "Now  Front  (amber)  over the character",
                _mutedStyle);
            if (_selectedFrame != source)
            {
                float previewTime = AuthoredStartTime(clip, _selectedFrame);
                if (GUILayout.Button($"Use preview {previewTime:0.00}s as source", EditorStyles.miniButton))
                    _socketInheritSourceFrame = _selectedFrame;
            }

            int previewFrame = Mathf.Clamp(_selectedFrame, 0, clip.Frames.Length - 1);
            float drawTime = AuthoredStartTime(clip, previewFrame);
            bool previewBehind = SocketInheritDrawsBehind(clip, _socketInheritNames[0], previewFrame);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(
                        $"Draw Behind at {drawTime:0.00}s",
                        "Key this socket behind the character at the current preview time."),
                        EditorStyles.miniButtonLeft))
                    KeySocketDrawAtTime(clip, previewFrame, behind: true);
                if (GUILayout.Button(new GUIContent(
                        $"Draw Front at {drawTime:0.00}s",
                        "Key this socket in front of the character at the current preview time."),
                        EditorStyles.miniButtonRight))
                    KeySocketDrawAtTime(clip, previewFrame, behind: false);
            }
            GUILayout.Label(
                previewBehind
                    ? $"Current {drawTime:0.00}s is Behind"
                    : $"Current {drawTime:0.00}s is Front",
                _mutedStyle);

            using (new EditorGUILayout.HorizontalScope())
            {
                _socketInheritPosition = DrawSocketInheritChannelToggle(
                    "Position", "Copy Offset X/Y from the source time.",
                    _socketInheritPosition, EditorStyles.miniButtonLeft);
                _socketInheritRotation = DrawSocketInheritChannelToggle(
                    "Rotation", "Copy angle from the source time.",
                    _socketInheritRotation, EditorStyles.miniButtonMid);
                _socketInheritScale = DrawSocketInheritChannelToggle(
                    "Scale", "Copy Scale X/Y from the source time.",
                    _socketInheritScale, EditorStyles.miniButtonRight);
            }

            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("All", "Select every frame."), EditorStyles.miniButton))
                    SelectSocketInheritFrames(clip, "all");
                if (GUILayout.Button(new GUIContent("None", "Clear the frame selection."), EditorStyles.miniButton))
                    SelectSocketInheritFrames(clip, "none");
                if (GUILayout.Button(new GUIContent("Missing", "Times that have no key yet."), EditorStyles.miniButton))
                    SelectSocketInheritFrames(clip, "missing");
                if (GUILayout.Button(new GUIContent("This→End", "From the source time to the end of the clip."),
                        EditorStyles.miniButton))
                    SelectSocketInheritFrames(clip, "rest");
                if (GUILayout.Button(new GUIContent("Timeline", "Use the timeline selection."),
                        EditorStyles.miniButton))
                    SelectSocketInheritFrames(clip, "timeline");
            }

            GUILayout.Space(4f);
            GUILayout.Label(
                $"Time  •  {_socketInheritFrames.Count} selected  •  click a row to preview",
                _mutedStyle);
            _socketInheritScroll = GUILayout.BeginScrollView(_socketInheritScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < clip.Frames.Length; i++)
            {
                bool keyed = false;
                for (int n = 0; n < _socketInheritNames.Count; n++)
                {
                    if (SpriteSocketKeys.FindOnFrame(clip.Sockets, _socketInheritNames[n], i) != null)
                    {
                        keyed = true;
                        break;
                    }
                }
                bool chosen = _socketInheritFrames.Contains(i);
                bool isSource = i == source;
                bool behind = SocketInheritDrawsBehind(clip, _socketInheritNames[0], i);
                float time = AuthoredStartTime(clip, i);
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool nextChosen = GUILayout.Toggle(chosen, GUIContent.none, GUILayout.Width(18f));
                    if (nextChosen != chosen)
                        ToggleSocketInheritFrame(i, SelectionOp.Toggle);

                    var evt = Event.current;
                    var row = GUILayoutUtility.GetRect(1f, 22f, GUILayout.ExpandWidth(true));
                    var swatch = new Rect(row.xMax - 12f, row.y + 6f, 10f, 10f);
                    var labelRect = new Rect(row.x, row.y, Mathf.Max(8f, row.width - 16f), row.height);
                    string text = isSource
                        ? $"{time:0.00}s  source  {(behind ? "Behind" : "Front")}"
                        : keyed
                            ? $"{time:0.00}s  key  {(behind ? "Behind" : "Front")}"
                            : $"{time:0.00}s  inherit  {(behind ? "Behind" : "Front")}";
                    if (i == _selectedFrame)
                        EditorGUI.DrawRect(row, new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.18f));
                    else if (chosen)
                        EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.06f));
                    GUI.Label(labelRect, text, isSource ? EditorStyles.boldLabel : EditorStyles.label);
                    EditorGUI.DrawRect(swatch, behind ? SocketDrawBehindColor : SocketDrawFrontColor);
                    if (evt.type == EventType.MouseDown && evt.button == 0 && row.Contains(evt.mousePosition))
                    {
                        ToggleSocketInheritFrame(i, ReadSelectionOp(evt, orderedList: true));
                        JumpPreviewToFrame(clip, i);
                        _status = $"{socketLabel}  {time:0.00}s  (frame {i + 1})  •  " +
                                  (behind ? "Behind" : "In Front");
                        evt.Use();
                        GUI.FocusControl(null);
                    }
                }
            }
            GUILayout.EndScrollView();

            bool canApply = _socketInheritFrames.Count > 0 &&
                            (_socketInheritPosition || _socketInheritRotation || _socketInheritScale);
            using (new EditorGUI.DisabledScope(!canApply))
            {
                if (GUILayout.Button(new GUIContent("Apply to selected",
                        "Copy checked channels from the source time onto the checked times.")))
                {
                    int changed = ApplySocketInherit(clip, _socketInheritPosition, _socketInheritRotation,
                        _socketInheritScale, _socketInheritFrames, "Inherit Sprite Socket Pose");
                    _status = $"Inherited pose onto {changed} frame key{Plural(changed)}";
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Next",
                            "Copy checked channels onto the following time.")))
                    {
                        var next = new[] { source + 1 };
                        int changed = ApplySocketInherit(clip, _socketInheritPosition, _socketInheritRotation,
                            _socketInheritScale, next, "Copy Sprite Socket to Next Frame");
                        _status = changed > 0
                            ? $"Copied pose to {AuthoredStartTime(clip, source + 1):0.00}s"
                            : "No next frame";
                    }
                    if (GUILayout.Button(new GUIContent("Rest of clip",
                            "Copy checked channels from this time through the end of the clip.")))
                    {
                        var rest = new List<int>();
                        for (int i = source + 1; i < clip.Frames.Length; i++)
                            rest.Add(i);
                        int changed = ApplySocketInherit(clip, _socketInheritPosition, _socketInheritRotation,
                            _socketInheritScale, rest, "Copy Sprite Socket to Rest of Clip");
                        _status = $"Copied pose to {changed} frame key{Plural(changed)}";
                    }
                    if (GUILayout.Button(new GUIContent("Fill missing",
                            "Write keys only on times that do not have one yet.")))
                    {
                        SelectSocketInheritFrames(clip, "missing");
                        int changed = ApplySocketInherit(clip, _socketInheritPosition, _socketInheritRotation,
                            _socketInheritScale, _socketInheritFrames, "Fill Sprite Socket Missing Frames");
                        _status = $"Filled {changed} missing key{Plural(changed)}";
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Reset selected",
                            "Set checked channels to identity (0,0 / 0° / 1,1) on checked times.")))
                    {
                        int changed = ResetSocketInherit(clip, _socketInheritFrames);
                        _status = $"Reset {changed} frame key{Plural(changed)}";
                    }
                    if (GUILayout.Button(new GUIContent("Clear keys",
                            "Delete keys on checked times so they fall back to the last pose.")))
                    {
                        int changed = ClearSocketInheritKeys(clip, _socketInheritFrames);
                        _status = $"Cleared {changed} socket key{Plural(changed)}";
                    }
                }
            }

            if (GUILayout.Button("Close"))
                CloseSocketInheritPanel();
        }

        bool SocketInheritDrawsBehind(SpriteClipDef clip, string name, int frame)
        {
            var item = _profile?.SocketCatalog?.Find(name);
            return SpriteSocketKeys.IsDrawnBehind(
                clip?.Sockets, name, frame,
                SpriteSocketKeys.CatalogDrawsBehind(item),
                SocketSampleClosed(clip, name));
        }

        void PruneSocketDrawSelection(SpriteClipDef clip)
        {
            if (clip == null || string.IsNullOrEmpty(_selectedSocketDrawName) ||
                _selectedSocketDrawFrame < 0 || _selectedSocketDrawFrame >= clip.Frames.Length ||
                IsIndependentSocketName(_selectedSocketDrawName))
            {
                _selectedSocketDrawFrame = -1;
                _selectedSocketDrawName = null;
                return;
            }
            var key = SpriteSocketKeys.FindOnFrame(
                clip.Sockets, _selectedSocketDrawName, _selectedSocketDrawFrame);
            if (key == null || key.DrawLayer == SpriteSocketKeys.DrawUnset)
            {
                _selectedSocketDrawFrame = -1;
                _selectedSocketDrawName = null;
            }
        }

        void KeySocketDrawAtTime(SpriteClipDef clip, int frame, bool behind, string socketName = null)
        {
            if (clip?.Frames == null || frame < 0 || frame >= clip.Frames.Length)
                return;
            socketName = string.IsNullOrEmpty(socketName) ? null : SpriteSocketKeys.CanonicalName(socketName);
            bool fromInherit = string.IsNullOrEmpty(socketName) &&
                               _showSocketInheritPanel && _socketInheritNames.Count > 0;
            if (string.IsNullOrEmpty(socketName) && !fromInherit)
                socketName = SpriteSocketKeys.CanonicalName(_selectedSocketName);
            if (!fromInherit && string.IsNullOrEmpty(socketName))
            {
                _status = "Add a Frame-Attached socket first to place Socket Draw keys";
                return;
            }
            if (!fromInherit && IsIndependentSocketName(socketName))
            {
                _status = "Independent Motion draw keys belong on the Independent Motion timeline";
                return;
            }
            if (fromInherit)
            {
                bool anyAttached = false;
                for (int n = 0; n < _socketInheritNames.Count; n++)
                {
                    if (!IsIndependentSocketName(_socketInheritNames[n]))
                    {
                        anyAttached = true;
                        break;
                    }
                }
                if (!anyAttached)
                {
                    _status = "Independent Motion draw keys belong on the Independent Motion timeline";
                    return;
                }
            }
            RecordProfileUndo(behind ? "Draw Socket Behind" : "Draw Socket In Front");
            byte layer = behind ? SpriteSocketKeys.DrawBehind : SpriteSocketKeys.DrawFront;
            if (fromInherit)
            {
                for (int n = 0; n < _socketInheritNames.Count; n++)
                {
                    string inheritName = SpriteSocketKeys.CanonicalName(_socketInheritNames[n]);
                    if (IsIndependentSocketName(inheritName))
                        continue;
                    var key = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, inheritName, frame);
                    key.DrawLayer = layer;
                    if (string.IsNullOrEmpty(socketName))
                        socketName = inheritName;
                }
            }
            else
            {
                var key = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, socketName, frame);
                key.DrawLayer = layer;
            }
            SelectSocketDrawKey(clip, frame, socketName);
            float time = AuthoredStartTime(clip, frame);
            _status = behind
                ? $"{socketName}  Behind at {time:0.00}s"
                : $"{socketName}  Front at {time:0.00}s";
            SaveDirty();
            Repaint();
        }

        static string Plural(int count) => count == 1 ? string.Empty : "s";

        void DrawHistoryOverlay()
        {
            if (!_showHistoryPanel)
                return;
            if (_historyWindowRect.width < 80f)
                _historyWindowRect = new Rect(position.width - 300f, 52f, 280f, 340f);
            _historyWindowRect = GUI.Window(99221, _historyWindowRect, DrawHistoryWindow, "Undo / Redo");
        }

        void DrawHistoryWindow(int id)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Undo"))
                    Undo.PerformUndo();
                if (GUILayout.Button("Redo"))
                    Undo.PerformRedo();
            }
            GUILayout.Space(4f);
            GUILayout.Label("Done", EditorStyles.boldLabel);
            _historyScroll = GUILayout.BeginScrollView(_historyScroll, GUILayout.ExpandHeight(true));
            if (_undoNames.Count == 0)
                GUILayout.Label("No actions yet.", _mutedStyle);
            for (int i = _undoNames.Count - 1; i >= 0; i--)
            {
                string mark = i == _undoNames.Count - 1 ? "▸ " : "   ";
                GUILayout.Label(mark + _undoNames[i]);
            }
            if (_redoNames.Count > 0)
            {
                GUILayout.Space(8f);
                GUILayout.Label("Redo", EditorStyles.boldLabel);
                for (int i = _redoNames.Count - 1; i >= 0; i--)
                    GUILayout.Label("   " + _redoNames[i]);
            }
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        void EnsureStyles()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 15,
                    normal = { textColor = Color.white },
                };
                _sectionStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 11,
                    normal = { textColor = new Color(0.76f, 0.83f, 0.91f) },
                };
                _mutedStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = TextMuted },
                    clipping = TextClipping.Clip,
                };
                _mutedWrapStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true,
                    fontSize = 9,
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Overflow,
                    normal = { textColor = TextMuted },
                };
                _frameLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13,
                    normal = { textColor = TextMuted },
                };
                _onionBadgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    normal = { textColor = Color.white },
                };
                _transportStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 11,
                    fixedHeight = 28f,
                    normal = { background = SolidTexture(new Color(0.16f, 0.18f, 0.215f)), textColor = Color.white },
                    hover = { background = SolidTexture(new Color(0.2f, 0.24f, 0.29f)), textColor = Color.white },
                    active = { background = SolidTexture(new Color(0.14f, 0.52f, 0.72f)), textColor = Color.white },
                };
                _clipStyle = new GUIStyle(GUI.skin.button)
                {
                    alignment = TextAnchor.MiddleLeft,
                    normal = { background = SolidTexture(PanelAltColor) },
                    hover = { background = SolidTexture(new Color(0.17f, 0.195f, 0.235f)) },
                };
                _clipSelectedStyle = new GUIStyle(_clipStyle)
                {
                    normal = { background = SolidTexture(new Color(0.12f, 0.34f, 0.47f)) },
                    hover = { background = SolidTexture(new Color(0.14f, 0.4f, 0.55f)) },
                };
            }

            if (_socketLabelStyle == null)
            {
                _socketLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 10,
                    padding = new RectOffset(4, 4, 1, 1),
                    normal = { textColor = Color.white },
                };
                _socketBalloonStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    wordWrap = true,
                    normal = { textColor = Color.white },
                };
            }
        }

        Texture2D SolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            _styleTextures.Add(texture);
            return texture;
        }

        struct PreviewState
        {
            public int Frame;
            public float Fraction;
            public float TimelineTime;
            public bool Ended;
        }
    }
}

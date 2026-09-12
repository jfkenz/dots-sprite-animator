using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>Unified authoring studio for sheets, clips, events, timing, and hitboxes.</summary>
    public sealed partial class SpriteSheetToolWindow : EditorWindow, ISpriteSheetSliceHost
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

        const string PackageVersion = "1.0.0";
        const float ToolbarHeight = 108f;
        const float DefaultTimelineHeight = 244f;
        const float MinTimelineHeight = 120f;
        const float MinWorkAreaHeight = 230f;
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
        const float MinClipPanelWidth = 180f;
        const float MinPreviewPanelWidth = 220f;
        const float MinInspectorPanelWidth = 260f;
        const float Gap = 8f;
        const float PixelsPerSecond = 520f;
        const float TimelineDragMoveThreshold = 1f;
        const float DefaultPreviewSpeed = 1f;
        const float PivotHandleHitRadius = 14f;
        const string PivotLockedPrefsKey = "InvertLab.SpriteAnimator.PivotLocked";
        const string AutoSaveEnabledPrefsKey = "InvertLab.SpriteAnimator.AutoSaveEnabled";
        const string AutoSaveMinutesPrefsKey = "InvertLab.SpriteAnimator.AutoSaveMinutes";
        const int AutoSaveMinutesDefault = 10;
        const int AutoSaveMinutesMin = 1;
        const int AutoSaveMinutesMax = 120;
        const float ColliderHandleSize = 8f;
        const float ColliderRotateHandleDistance = 26f;
        const float ColliderMinScreenHalf = 6f;
        const float SocketMinAbsScale = 0.05f;
        const float SocketMaxAbsScale = 32f;
        const float SocketHandleHit = 14f;
        const float SocketDragThreshold = 3f;
        const float SocketGroupPivotHit = 16f;
        const float SocketGroupGizmoPad = 28f;
        const float SocketGroupGizmoMinHalf = 36f;
        const int SocketProfilePickerId = 0x5A0C3701;
        const string ClipRenameControl = "InvertLabSpriteAnimator.ClipRename";
        const string SheetRenameControl = "InvertLabSpriteAnimator.SheetRename";
        const string SocketNameRenameControl = "InvertLabSpriteAnimator.SocketNameRename";
        const string SocketIdRenameControl = "InvertLabSpriteAnimator.SocketIdRename";
        const string InventoryRenameControl = "InvertLabSpriteAnimator.InventoryRename";
        const string EventRenameControl = "InvertLabSpriteAnimator.EventRename";
        const string StringFieldControlPrefix = "InvertLabSpriteAnimator.Text.";
        const float SheetRowHeight = 38f;
        const float NestedClipRowHeight = 22f;
        const float ClipNestIndent = 14f;
        const float ClipRowDeleteWidth = 18f;
        const float ClipRowFoldWidth = 14f;

        static readonly Color WindowColor = new(0.067f, 0.078f, 0.094f);
        static readonly Color PanelColor = new(0.105f, 0.12f, 0.145f);
        static readonly Color PanelAltColor = new(0.13f, 0.15f, 0.18f);
        static readonly Color BorderColor = new(0.19f, 0.225f, 0.265f);
        static readonly Color AccentColor = new(0.28f, 0.76f, 0.79f);
        static readonly Color SocketDrawBehindColor = new(0.62f, 0.42f, 0.98f);
        static readonly Color SocketDrawFrontColor = new(1f, 0.76f, 0.22f);
        static readonly Color EventColor = new(1f, 0.61f, 0.2f);
        static readonly Color TextMuted = new(0.66f, 0.72f, 0.79f);

        [SerializeField] SpriteSheetProfile _profile;
        [SerializeField] float _clipPanelWidth = DefaultClipPanelWidth;
        [SerializeField] float _inspectorPanelWidth = DefaultInspectorPanelWidth;
        [SerializeField] float _timelinePanelHeight = DefaultTimelineHeight;
        [SerializeField] bool _clipRowDetailsExpanded = true;
        [SerializeField] PreviewOffsetMode _previewOffsetMode = PreviewOffsetMode.Authored;
        [SerializeField] ScriptableSpriteSheetProfile _asset;
        ScriptableSpriteSheetProfile _undoProxy;
        bool _showHistoryPanel;
        Rect _historyWindowRect = new(40f, 56f, 280f, 340f);
        Vector2 _historyScroll;
        readonly List<string> _undoNames = new();
        readonly List<string> _redoNames = new();
        readonly List<int> _sheetClipCounts = new();
        int _selectedClip;
        readonly HashSet<int> _selectedClips = new();
        int _selectedSheet;
        bool _showTimelineInputHelp;
        bool _showSheetCellPicker;
        Rect _sheetCellPickerRect = new(80f, 56f, 520f, 460f);
        Vector2 _sheetCellPickerScroll;
        readonly List<int> _sheetCellPickerSelection = new();
        int _sheetCellPickerAnchor = -1;
        bool _sheetCellPickerDragging;
        Vector2 _sheetCellPickerDragOffset;
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
        int _selectedEventIndex = -1;
        string _selectedEventClipName;
        [SerializeField] bool _eventThisClipExpanded = true;
        [SerializeField] bool _eventOtherClipsExpanded = true;
        [SerializeField] bool _eventRowDetailsExpanded = true;
        int _selectedSocketDrawFrame = -1;
        string _selectedSocketDrawName;
        int _dragDrawSourceFrame = -1;
        string _dragDrawSocketName;
        byte _dragDrawLayer;
        bool _drawDragMoved;
        int _newHitboxId = 1;
        ColliderCreationMode _colliderCreationMode = ColliderCreationMode.None;
        [SerializeField] bool _continuousColliderPlacement = false;
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
        [SerializeField] bool _showHitboxes = true;
        byte _newColliderLifetime;
        byte _newColliderPhysics;
        bool _newColliderIsTrigger = true;
        static readonly string[] ColliderLifetimeLabels = { "This Frame", "This Clip", "Character" };
        static readonly string[] ColliderPhysicsLabels = { "Query AABB", "Unity 2D", "Both" };
        static readonly string[] EventPayloadKindLabels =
        {
            "Int", "Float", "Text", "Bool",
            "Int2", "Int3", "Int4",
            "Float2", "Float3", "Float4",
            "Byte", "Color", "Half", "Asset",
        };
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
        bool _pivotLocked;
        Vector2 _pivotClipboard = SpriteSheetProfile.DefaultPivot;
        bool _pivotClipboardValid;
        Vector2 _socketPoseClipboardPosition;
        float _socketPoseClipboardAngle;
        Vector2 _socketPoseClipboardScale = Vector2.one;
        bool _socketPoseClipboardValid;
        double _lastEditorTime;
        bool _autoSaveEnabled = true;
        int _autoSaveIntervalMinutes = AutoSaveMinutesDefault;
        double _autoSaveNextDue;
        string _autoSaveLastStatus = "";
        Rect _settingsButtonRect;
        double _lastSpaceToggleTime = -1d;
        float _previewTime;

        Vector2 _clipScroll;
        Vector2 _inspectorScroll;
        float _inspectorMeasuredHeight;
        Vector2 _timelineScroll;
        Vector2 _socketTimelineScroll;
        [SerializeField] float _frameTimelineZoom = 1f;
        [SerializeField] float _independentTimelineZoom = 1f;
        [SerializeField] bool _showIndependentMotionPaths = true;
        [SerializeField] bool _showPreviewDebug = true;
        [SerializeField] bool _showPreviewSize = true;
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
        Rect _clipRenameFieldRect;
        bool _hasClipRenameFieldRect;
        string _renamingSocketName;
        string _renameSocketNameValue = string.Empty;
        string _renameSocketNameOriginal = string.Empty;
        bool _focusSocketNameRename;
        string _renamingSocketId;
        string _renameSocketIdValue = string.Empty;
        string _renameSocketIdOriginal = string.Empty;
        bool _focusSocketIdRename;
        int _renamingInventoryIndex = -1;
        string _renameInventoryValue = string.Empty;
        string _renameInventoryOriginal = string.Empty;
        bool _focusInventoryRename;
        int _renameInventoryTargetIndex = -1;
        byte _renamingEventId;
        string _renameEventValue = string.Empty;
        string _renameEventOriginal = string.Empty;
        bool _focusEventRename;
        // GenericMenu Rename queues this; ProcessPendingEventRename begins
        // inline rename on a later OnGUI so FocusTextInControl runs after the
        // TextField is drawn (menu close otherwise steals focus same-frame).
        bool _pendingEventRename;
        int _pendingEventRenameIndex = -1;
        int _selectedEventTypeIndex = -1;
        readonly HashSet<int> _selectedEventTypeIndices = new();
        int _eventTypeListAnchor = -1;
        TimelineDragMode _timelineDragMode;
        Vector2 _timelineDragStartScreen;
        Vector2 _timelineDragContentMouse;
        float _timelineDragStartScrollX;
        bool _panMoved;
        bool _panClickPlacesPlayhead;
        float _panelResizeMouseStartX;
        float _panelResizeWidthStart;
        float _panelResizeMouseStartY;
        float _panelResizeHeightStart;
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
        int _dragEventMarkerIndex = -1;
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
        [SerializeField] bool _colliderFrameExpanded = true;
        [SerializeField] bool _colliderOnThisFrameExpanded = true;
        [SerializeField] bool _colliderOtherFramesExpanded = true;
        [SerializeField] bool _colliderClipExpanded = true;
        [SerializeField] bool _colliderCharacterExpanded = true;
        [SerializeField] bool _colliderOtherClipsExpanded = true;
        [SerializeField] bool _colliderRowDetailsExpanded = true;
        FrameBoxDef _colliderDetailsBox;
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
        GUIStyle _primaryStyle;
        GUIStyle _panelStyle;
        GUIStyle _saveStatusStyle;
        GUIStyle _frameLabelStyle;
        GUIStyle _onionBadgeStyle;
        GUIStyle _socketLabelStyle;
        GUIStyle _socketBalloonStyle;
        readonly List<Texture2D> _styleTextures = new();
        readonly List<OnionGhostLayout> _onionGhostLayouts = new(16);
        readonly HashSet<FrameBoxDef> _selectedColliders = new();
        readonly HashSet<string> _characterIncludeSelected = new();
        readonly HashSet<string> _characterExcludeSelected = new();
        FrameBoxDef _characterFilterDetailsBox;
        bool _pendingInspectorScrollToColliders;
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
            _pivotLocked = EditorPrefs.GetBool(PivotLockedPrefsKey, false);
            LoadAutoSavePrefs();
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
            _titleStyle = null;
            _socketLabelStyle = null;
            _socketBalloonStyle = null;
            InvalidateSheetPixelCache();
        }

        void TickPreview()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = Mathf.Min(0.1f, (float)(now - _lastEditorTime));
            _lastEditorTime = now;
            TickAutoSave(now);
            bool changed = false;
            if (_playing && CurrentClip != null)
            {
                var clip = CurrentClip;
                float step = delta * Mathf.Max(0.05f, _speed);
                if (clip.WrapMode == SpriteAnimWrap.ReverseOnce)
                {
                    float total = SpriteAnimPlayback.TotalAuthoredDuration(clip);
                    // Auto-seek to end when starting from the beginning.
                    if (_previewTime <= 1e-4f)
                        _previewTime = total;
                    _previewTime -= step;
                    if (_previewTime <= 0f)
                    {
                        if (_previewLoop)
                            _previewTime = total;
                        else
                        {
                            _previewTime = 0f;
                            _playing = false;
                        }
                    }
                }
                else
                {
                    _previewTime += step;
                    var state = EvaluatePreview(clip, _previewTime);
                    if (state.Ended && !_previewLoop)
                        _playing = false;
                }
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
            ProcessPendingEventRename();
            HandleGlobalShortcuts();
            PollSocketProfilePicker();
            // Focusing the window clears IMGUI text-field focus. While an inline rename is
            // active that makes HandleBrowserRenameKeys treat the click as click-away and
            // immediately commit - so skip Focus() until rename finishes.
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                !IsRenamingAnything())
                Focus();

            // Keep Layout/Repaint call order stable, but hide mouse events from the
            // rest of the window so the inherit panel is modal until it closes.
            EventType editorEvent = Event.current.type;
            bool overlayBlocksEditor = OverlayBlocksEditorInput();
            if (overlayBlocksEditor)
                Event.current.type = EventType.Ignore;

            int timelineControlId = GUIUtility.GetControlID(
                "InvertLabSpriteAnimatorTimeline".GetHashCode(), FocusType.Passive);
            HandleActiveTimelineDrag(timelineControlId);
            EditorGUI.DrawRect(new Rect(Vector2.zero, position.size), WindowColor);

            DrawToolbar(new Rect(0f, 0f, position.width, ToolbarHeight));

            float maxTimeline = Mathf.Max(MinTimelineHeight,
                position.height - ToolbarHeight - MinWorkAreaHeight - Gap * 3f);
            if (_timelinePanelHeight < 1f)
                _timelinePanelHeight = DefaultTimelineHeight;
            _timelinePanelHeight = Mathf.Clamp(_timelinePanelHeight, MinTimelineHeight, maxTimeline);
            float timelineHeight = _timelinePanelHeight;
            var workRect = new Rect(
                Gap,
                ToolbarHeight + Gap,
                position.width - Gap * 2f,
                Mathf.Max(MinWorkAreaHeight,
                    position.height - ToolbarHeight - timelineHeight - Gap * 3f));
            ClampPanelWidths(workRect.width);
            float centerWidth = workRect.width - _clipPanelWidth - _inspectorPanelWidth - Gap * 2f;
            var clipsRect = new Rect(workRect.x, workRect.y, _clipPanelWidth, workRect.height);
            var leftSplitter = new Rect(clipsRect.xMax, workRect.y, Gap, workRect.height);
            var previewRect = new Rect(leftSplitter.xMax, workRect.y, centerWidth, workRect.height);
            var rightSplitter = new Rect(previewRect.xMax, workRect.y, Gap, workRect.height);
            var inspectorRect = new Rect(rightSplitter.xMax, workRect.y, _inspectorPanelWidth, workRect.height);
            var timelineSplitter = new Rect(Gap, workRect.yMax, position.width - Gap * 2f, Gap);
            var timelineRect = new Rect(
                Gap,
                timelineSplitter.yMax,
                position.width - Gap * 2f,
                Mathf.Max(MinTimelineHeight, position.height - timelineSplitter.yMax - Gap));

            DrawPanel(clipsRect);
            DrawPanel(previewRect);
            DrawPanel(inspectorRect);
            DrawPanel(timelineRect);

            // Pivot handle wins over socket selected-fallback so RMB on the green
            // pivot opens Pivot Actions even when a socket was previously selected.
            HandleWindowPivotContextClick(previewRect);
            HandleWindowSocketContextClick(previewRect);

            DrawClipBrowser(clipsRect);
            DrawInspector(inspectorRect);
            DrawPreview(previewRect);
            DrawPanelSplitter(leftSplitter, true, workRect.width);
            DrawPanelSplitter(rightSplitter, false, workRect.width);
            DrawTimelineSplitter(timelineSplitter);
            DrawTimeline(timelineRect, timelineControlId);
            DrawHistoryOverlay();

            if (overlayBlocksEditor)
                Event.current.type = editorEvent;
            DrawSocketInheritOverlay();
            DrawSocketTransformOverlay();
            DrawSheetCellPickerOverlay();
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
            var hit = new Rect(rect.x - 2f, rect.y, rect.width + 4f, rect.height);
            int controlId = GUIUtility.GetControlID(
                (resizeClipPanel ? "InvertLabClipSplitter" : "InvertLabInspectorSplitter").GetHashCode(),
                FocusType.Passive, hit);
            var evt = Event.current;
            bool active = GUIUtility.hotControl == controlId;
            bool hovered = hit.Contains(evt.mousePosition);

            EditorGUIUtility.AddCursorRect(hit, MouseCursor.ResizeHorizontal);
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

        void DrawTimelineSplitter(Rect rect)
        {
            // Slightly taller hit target than the visual gap for easier vertical drag.
            var hit = new Rect(rect.x, rect.y - 2f, rect.width, rect.height + 4f);
            int controlId = GUIUtility.GetControlID(
                "InvertLabTimelineSplitter".GetHashCode(), FocusType.Passive, hit);
            var evt = Event.current;
            bool active = GUIUtility.hotControl == controlId;
            bool hovered = hit.Contains(evt.mousePosition);

            EditorGUIUtility.AddCursorRect(hit, MouseCursor.ResizeVertical);
            Color grip = active || hovered ? AccentColor : BorderColor;
            EditorGUI.DrawRect(new Rect(rect.x + 8f, rect.center.y - 1f, rect.width - 16f, 2f), grip);

            if (evt.type == EventType.MouseDown && evt.button == 0 && hovered)
            {
                if (evt.clickCount >= 2)
                {
                    _timelinePanelHeight = DefaultTimelineHeight;
                    _status = "Reset timeline height";
                }
                else
                {
                    GUIUtility.hotControl = controlId;
                    _panelResizeMouseStartY = evt.mousePosition.y;
                    _panelResizeHeightStart = _timelinePanelHeight;
                }
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDrag && active)
            {
                // Dragging the bar down grows the timeline (upper work area shrinks).
                float delta = evt.mousePosition.y - _panelResizeMouseStartY;
                _timelinePanelHeight = _panelResizeHeightStart - delta;
                float maxTimeline = Mathf.Max(MinTimelineHeight,
                    position.height - ToolbarHeight - MinWorkAreaHeight - Gap * 3f);
                _timelinePanelHeight = Mathf.Clamp(
                    _timelinePanelHeight, MinTimelineHeight, maxTimeline);
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

            // Keep transport separate from file actions, even at the minimum window width.
            x = 14f;

            using (new EditorGUI.DisabledScope(!hasClip))
            {
                if (GUI.Button(new Rect(x, 52f, 28f, 28f), new GUIContent("|<", "Jump to first frame."), _transportStyle))
                    StepToBoundary(clip, forward: false);
                x += 32f;
                if (GUI.Button(new Rect(x, 52f, 24f, 28f), new GUIContent("<", "Step one frame backward."), _transportStyle))
                    StepFrame(clip, -1);
                x += 28f;
                if (GUI.Button(new Rect(x, 52f, 24f, 28f), new GUIContent(">", "Step one frame forward."), _transportStyle))
                    StepFrame(clip, +1);
                x += 28f;
                if (GUI.Button(new Rect(x, 52f, 28f, 28f), new GUIContent(">|", "Jump to last frame."), _transportStyle))
                    StepToBoundary(clip, forward: true);
                x += 34f;
            }

            using (new EditorGUI.DisabledScope(!hasClip))
            {
                if (GUI.Button(new Rect(x, 52f, 70f, 28f),
                    new GUIContent(_playing ? "Pause" : "Play", _playing
                        ? "Pause frame playback."
                        : _spacePlaysBothClocks
                            ? "Play frame playback. Space starts both clocks when Space: Both is on."
                            : "Play frame playback (Space, this tab only)."),
                    _primaryStyle))
                {
                    bool starting = !_playing;
                    _playing = !_playing;
                    if (starting && _playing && CurrentClip != null
                        && CurrentClip.WrapMode == SpriteAnimWrap.ReverseOnce)
                    {
                        _previewTime = SpriteAnimPlayback.TotalAuthoredDuration(CurrentClip);
                    }
                }
            }
            x += 76f;
            if (GUI.Button(new Rect(x, 52f, 58f, 28f),
                new GUIContent("Stop", "Stop playback and return to time 0."), _transportStyle))
            {
                _playing = false;
                _previewTime = 0f;
                Repaint();
            }
            x += 66f;
            _previewLoop = GUI.Toggle(new Rect(x, 55f, 58f, 22f),
                _previewLoop, new GUIContent("Loop", "Loop preview playback."));
            x += 64f;
            GUI.Label(new Rect(x, 54f, 40f, 20f), "Speed", _mutedStyle);
            x += 42f;
            _speed = GUI.HorizontalSlider(new Rect(x, 61f, 90f, 16f), _speed, 0.1f, 3f);
            x += 96f;
            GUI.Label(new Rect(x, 54f, 42f, 20f), $"{_speed:F1}x", _mutedStyle);
            x += 44f;
            using (new EditorGUI.DisabledScope(Mathf.Approximately(_speed, DefaultPreviewSpeed)))
            {
                if (GUI.Button(new Rect(x, 52f, 38f, 26f),
                    new GUIContent("1x", "Reset preview speed to its 1x default."), _transportStyle))
                    _speed = DefaultPreviewSpeed;
            }
            x += 44f;

            // Right cluster: Settings, Check, Help, Save Profile
            float checkX = rect.xMax - 338f;
            const float undoW = 46f;
            const float redoW = 46f;
            const float listW = 58f;
            const float undoGap = 4f;
            float clusterW = undoW + undoGap + redoW + undoGap + listW;
            float undoX = rect.xMax - clusterW - 14f;

            if (GUI.Button(new Rect(undoX, 52f, undoW, 26f),
                new GUIContent("Undo", "Undo (Ctrl/Cmd+Z)"), _transportStyle))
                Undo.PerformUndo();
            float redoX = undoX + undoW + undoGap;
            if (GUI.Button(new Rect(redoX, 52f, redoW, 26f),
                new GUIContent("Redo", "Redo (Ctrl/Cmd+Shift+Z or Ctrl+Y)"), _transportStyle))
                Undo.PerformRedo();
            float listX = redoX + redoW + undoGap;
            if (GUI.Button(new Rect(listX, 52f, listW, 26f),
                new GUIContent(_showHistoryPanel ? "Hide" : "History",
                    "Show the undo/redo history panel."), _transportStyle))
                _showHistoryPanel = !_showHistoryPanel;
            x = listX + listW + 8f;

            _settingsButtonRect = new Rect(checkX, 10f, 66f, 28f);
            if (GUI.Button(_settingsButtonRect,
                new GUIContent("Settings", "Auto-save and tool preferences."),
                _transportStyle))
                ShowSettingsPopup();

            var validateRect = new Rect(rect.xMax - 266f, 10f, 52f, 28f);
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
                    _primaryStyle))
                    SaveProfile();
            }

            EditorGUI.DrawRect(new Rect(14f, 44f, rect.width - 28f, 1f), BorderColor);
            GUI.Label(new Rect(14f, 86f, rect.width - 252f, 18f),
                new GUIContent(_status, _status), _mutedStyle);
            string saveState = _asset == null ? "Unsaved profile"
                : EditorUtility.IsDirty(_asset) ? "Unsaved changes" : "Profile saved";
            GUI.Label(new Rect(rect.xMax - 230f, 86f, 216f, 18f), saveState, _saveStatusStyle);
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
            _selectedEventIndex = -1;
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
            RecordProfileUndo(texture == null ? "Clear Sheet Texture" : "Assign Sheet Texture");
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
            _selectedEventIndex = -1;
            _status = forward ? "Jumped to last frame" : "Jumped to first frame";
            Repaint();
        }

        void StepFrame(SpriteClipDef clip, int delta)
        {
            if (clip == null || clip.Frames.Length == 0 || delta == 0)
                return;

            int current = EvaluatePreview(clip, _previewTime).Frame;
            int next = current + delta;
            if (clip.WrapMode == SpriteAnimWrap.Once
                || clip.WrapMode == SpriteAnimWrap.ReverseOnce)
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
            _selectedEventIndex = -1;
            _status = $"Stepped to frame {next + 1}";
            Repaint();
        }

        void DrawInspector(Rect rect)
        {
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 20f), "INSPECTOR", _sectionStyle);
            PrepareInspectorUndo();
            // Always reserve the socket/pivot action bar so selecting one does not
            // shrink the inspector and jump the list under the next click.
            float top = 90f;
            if (_pivotSelected && _showPivot)
            {
                var bar = new Rect(rect.x + 9f, rect.y + 32f, rect.width - 18f, 54f);
                DrawSelectedPivotBar(bar);
            }
            else if (!string.IsNullOrEmpty(_selectedSocketName) && CurrentClip != null)
            {
                var bar = new Rect(rect.x + 9f, rect.y + 32f, rect.width - 18f, 54f);
                DrawSelectedSocketBar(bar);
            }
            var area = new Rect(rect.x + 9f, rect.y + top, rect.width - 18f, rect.height - top - 10f);
            int colliderRows = _profile?.Hitboxes != null ? _profile.Hitboxes.Count : 0;
            int socketRows = 0;
            if (CurrentClip?.Sockets != null)
                socketRows = CachedUniqueSocketNames(CurrentClip).Count;
            float socketExtra = 72f + socketRows * 48f + 340f +
                (_selectedSockets.Count == 1 ? 520f : 0f);
            float colliderGroupExtra = CurrentClip == null ? 0f : 90f +
                (_colliderFrameExpanded ? 42f : 0f) +
                (_colliderOnThisFrameExpanded ? 28f : 0f) +
                (_colliderOtherFramesExpanded ? 28f : 0f) +
                (_colliderClipExpanded ? 42f : 0f) +
                (_colliderCharacterExpanded ? 42f : 0f) +
                (_colliderOtherClipsExpanded ? 42f : 0f);
            FrameBoxDef primaryCollider = PrimarySelectedCollider();
            float colliderDetailsExtra = _colliderRowDetailsExpanded &&
                _selectedColliders.Count == 1
                ? (primaryCollider != null && primaryCollider.IsCharacter ? 560f : 360f)
                : 0f;
            float estimatedHeight = 1640f + colliderRows * 28f +
                colliderGroupExtra + colliderDetailsExtra + socketExtra;
            var inspectorContent = new Rect(0f, 0f, area.width - 15f,
                Mathf.Max(area.height, estimatedHeight, _inspectorMeasuredHeight));
            if (_pendingInspectorScrollToColliders)
            {
                _inspectorScroll.y = Mathf.Max(0f, estimatedHeight - socketExtra - 240f);
                _pendingInspectorScrollToColliders = false;
            }
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
                bool renamingThisSheet = _renamingSheet == _selectedSheet && _renamingSheet >= 0;
                if (renamingThisSheet)
                {
                    var sheetNameRow = EditorGUILayout.GetControlRect();
                    var sheetNameLabel = new Rect(sheetNameRow.x, sheetNameRow.y, EditorGUIUtility.labelWidth, sheetNameRow.height);
                    var sheetNameField = new Rect(sheetNameRow.x + EditorGUIUtility.labelWidth, sheetNameRow.y,
                        Mathf.Max(20f, sheetNameRow.width - EditorGUIUtility.labelWidth), sheetNameRow.height);
                    GUI.Label(sheetNameLabel, "Name");
                    DrawInlineRenameField(sheetNameField, SheetRenameControl,
                        ref _renameSheetValue, ref _focusSheetRename, EditorStyles.textField);
                }
                else if (DrawRenameLabelRow("Name",
                    string.IsNullOrWhiteSpace(activeSheet.Name) ? $"Sheet {_selectedSheet + 1}" : activeSheet.Name,
                    "Double-click or press F2 to rename this sheet.", out _))
                {
                    BeginSheetRename(_selectedSheet);
                    GUIUtility.ExitGUI();
                }
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
                using (new EditorGUI.DisabledScope(_pivotLocked))
                {
                    EditorGUI.BeginChangeCheck();
                    Vector2 nextPivot = EditorGUILayout.Vector2Field(
                        new GUIContent("Pivot",
                            "Normalized cell pivot (0–1). (0,0)=bottom-left, (0.5,0.5)=center, (0.5,0)=feet. "
                            + "In Cropped mode this is relative to the active cropped cell."),
                        _profile.Pivot);
                    if (EditorGUI.EndChangeCheck())
                    {
                        nextPivot.x = Mathf.Clamp01(nextPivot.x);
                        nextPivot.y = Mathf.Clamp01(nextPivot.y);
                        if (nextPivot != _profile.Pivot)
                        {
                            // PrepareInspectorUndo already recorded on mouse/key down.
                            _profile.Pivot = nextPivot;
                            WriteActiveSheetFromLegacy();
                            _status = $"Pivot {_profile.Pivot.x:F3}, {_profile.Pivot.y:F3}";
                        }
                    }
                }
                using (new EditorGUI.DisabledScope(_pivotLocked ||
                    _profile.Pivot == SpriteSheetProfile.DefaultPivot))
                {
                    if (ResetValueButton("Reset the pivot to centered (0.5, 0.5). Unlock first if locked."))
                        SetProfilePivot(SpriteSheetProfile.DefaultPivot, "Reset Sprite Pivot",
                            "Reset pivot to center");
                }
                bool nextShowPivot = GUILayout.Toggle(_showPivot,
                    new GUIContent("Show Pivot",
                        "Draw the sheet pivot as a green handle in the preview. "
                        + "Also available as Pivot: On/Off on the preview overlay next to Colliders/Size/Debug. "
                        + "Drag to move when unlocked; right-click (or select then Actions) for snap presets. "
                        + "Use the lock icon beside this toggle to prevent select/drag. "
                        + "FlipX/FlipY mirror around this pivot so off-center art does not jump."),
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
                if (GUILayout.Button(PivotLockContent(_pivotLocked),
                        EditorStyles.miniButton, GUILayout.Width(28f), GUILayout.Height(18f)))
                    SetPivotLocked(!_pivotLocked);
            }
            if (_pivotSelected && _showPivot)
            {
                using (new EditorGUI.DisabledScope(_pivotLocked))
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    float px = EditorGUILayout.FloatField(
                        new GUIContent("Pivot X", "Normalized 0–1 across the active cell."),
                        _profile.Pivot.x);
                    float py = EditorGUILayout.FloatField(
                        new GUIContent("Pivot Y", "Normalized 0–1 up the active cell (0 = bottom / feet)."),
                        _profile.Pivot.y);
                    if (EditorGUI.EndChangeCheck())
                    {
                        // PrepareInspectorUndo already recorded on mouse/key down.
                        _profile.Pivot = new Vector2(Mathf.Clamp01(px), Mathf.Clamp01(py));
                        WriteActiveSheetFromLegacy();
                        _status = $"Pivot {_profile.Pivot.x:F3}, {_profile.Pivot.y:F3}";
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Pivot Actions…",
                            "Snap presets, Character collider snap, opaque bounds, copy/paste, lock."),
                        EditorStyles.miniButton))
                        ShowPivotContextMenu();
                    using (new EditorGUI.DisabledScope(_pivotLocked))
                    {
                        if (GUILayout.Button(new GUIContent("Snap Feet",
                                "Snap pivot to bottom-center (0.5, 0) — platformer default."),
                            EditorStyles.miniButton, GUILayout.Width(84f)))
                            SetProfilePivot(new Vector2(0.5f, 0f), "Snap Pivot to Bottom Center");
                        if (GUILayout.Button(new GUIContent("Snap Center",
                                "Snap pivot to cell center (0.5, 0.5)."),
                            EditorStyles.miniButton, GUILayout.Width(92f)))
                            SetProfilePivot(SpriteSheetProfile.DefaultPivot, "Snap Pivot to Cell Center");
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_profile.Sheet == null))
                {
                    if (GUILayout.Button(new GUIContent("Flip Horizontal",
                        "Overwrite this sheet's texture file, mirroring every grid cell left-right. Clip frames stay on the same cells. Pivot, sockets, colliders, and Independent Motion on this sheet are mirrored to match. Undo restores the previous file.")))
                    {
                        FlipActiveSheet(true, false);
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button(new GUIContent("Flip Vertical",
                        "Overwrite this sheet's texture file, mirroring every grid cell top-bottom. Clip frames stay on the same cells. Pivot, sockets, colliders, and Independent Motion on this sheet are mirrored to match. Undo restores the previous file.")))
                    {
                        FlipActiveSheet(false, true);
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (GUILayout.Button("Auto-detect transparent grid"))
            {
                AutoDetect();
                WriteActiveSheetFromLegacy();
                if (_profile.SheetsWorldHeightsDiffer())
                {
                    // AutoDetect already recorded when the grid changed; this covers
                    // rematch-only (detect failed / no grid change) under the same group.
                    RecordProfileUndo("Match Sheets World Size");
                    RematchSheetsWorldSize(_selectedSheet);
                    SaveDirty();
                }
                else
                    RematchSheetsWorldSize(_selectedSheet);
            }

            EditorGUI.BeginChangeCheck();
            var nextLayout = (SpriteSheetCellLayoutMode)EditorGUILayout.EnumPopup(
                new GUIContent("Cell Layout",
                    "Grid: uniform Columns×Rows UV cells (current behavior). "
                    + "Cropped: per-cell tight opaque rects that remove transparent spacing/gutters from sampled UVs. "
                    + "Switching modes keeps the other mode's data when practical."),
                _profile.CellLayoutMode);
            if (EditorGUI.EndChangeCheck() && nextLayout != _profile.CellLayoutMode)
            {
                RecordProfileUndo("Change Sprite Cell Layout");
                _profile.CellLayoutMode = nextLayout;
                WriteActiveSheetFromLegacy();
                if (nextLayout == SpriteSheetCellLayoutMode.Cropped &&
                    !SpriteSheetProfile.HasCroppedCellData(_profile.SheetAt(_selectedSheet)))
                {
                    DetectSpacingAndCropCells(setMode: false);
                    WriteActiveSheetFromLegacy();
                }
                else
                {
                    _status = nextLayout == SpriteSheetCellLayoutMode.Cropped
                        ? "Cell layout: Cropped (using stored crop rects)"
                        : "Cell layout: Grid (uniform cells; crop rects kept)";
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_profile.Sheet == null))
                {
                    if (GUILayout.Button(new GUIContent("Detect spacing & crop cells",
                        "Scan each grid cell for opaque bounds (alpha > 8), store tight crop rects, "
                        + "and switch Cell Layout to Cropped. Transparent gutters are removed from UVs; "
                        + "Columns/Rows stay as the coarse grid.")))
                    {
                        DetectSpacingAndCropCells(setMode: true);
                        WriteActiveSheetFromLegacy();
                        RematchSheetsWorldSize(_selectedSheet);
                    }
                }
                if (_profile.CellLayoutMode == SpriteSheetCellLayoutMode.Cropped &&
                    SpriteSheetProfile.HasCroppedCellData(_profile.SheetAt(_selectedSheet)))
                {
                    if (GUILayout.Button(new GUIContent("Recompute crops",
                        "Re-run opaque-bound detect for every cell and replace stored crop rects."),
                        GUILayout.Width(130f)))
                    {
                        DetectSpacingAndCropCells(setMode: true);
                        WriteActiveSheetFromLegacy();
                        RematchSheetsWorldSize(_selectedSheet);
                    }
                }
            }
            DrawCroppedLayoutStatus();

            int gridCols = Mathf.Max(1, _profile.Columns);
            int gridRows = Mathf.Max(1, _profile.Rows);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent($"Create {gridRows} clips from rows",
                    $"One clip per sheet row. Typical spritesheet layout: {gridRows} clips of {gridCols} frames. Skips empty rows and rows that already have a clip.")))
                    CreateClipsFromSheetRows();
                if (GUILayout.Button(new GUIContent($"Create {gridCols} clips from columns",
                    $"One clip per sheet column, playing top-to-bottom. Skips empty columns.")))
                    CreateClipsFromSheetColumns();
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
                bool renamingThisClip = _renamingClip == _selectedClip && _renamingClip >= 0;
                if (renamingThisClip)
                {
                    // Left clip list owns ClipRenameControl - a second field with the same
                    // control name steals focus every frame and makes typing impossible.
                    EditorGUILayout.LabelField("Name", _renameClipValue);
                }
                else if (DrawRenameLabelRow("Name",
                    string.IsNullOrWhiteSpace(clip.Name) ? $"Clip {_selectedClip + 1}" : clip.Name,
                    "Double-click or press F2 to rename this clip.", out _))
                {
                    BeginClipRename(_selectedClip);
                    GUIUtility.ExitGUI();
                }
                clip.Row = Mathf.Clamp(EditorGUILayout.IntField("Sheet Row", clip.Row), 0,
                    Mathf.Max(0, ClipSheetRows(clip) - 1));
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    float clipFps = EditorGUILayout.FloatField(
                        new GUIContent("FPS", ClipFpsTooltip), clip.FrameRate);
                    if (EditorGUI.EndChangeCheck())
                        ApplyClipFrameRate(clip, clipFps);
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
                        new[] { "Loop", "Once", "Ping Pong", "Reverse Loop", "Reverse Once" });
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
                EditorGUI.BeginChangeCheck();
                byte interrupt = (byte)EditorGUILayout.Popup(
                    new GUIContent("Interrupt",
                        "Always = locomotion. Never = hard cast/death. AfterTime = cancel window after startup frames."),
                    clip.Interrupt,
                    new[] { "Always", "Never", "After Time" });
                if (EditorGUI.EndChangeCheck())
                {
                    RecordProfileUndo("Set Sprite Clip Interrupt");
                    clip.Interrupt = interrupt;
                    SaveDirty();
                }
                if (clip.Interrupt == (byte)SpriteClipInterrupt.AfterTime)
                {
                    EditorGUI.BeginChangeCheck();
                    float cancelAfter = EditorGUILayout.Slider(
                        new GUIContent("Cancel After",
                            "Normalized 0-1. Play() is blocked until playback reaches this point unless force=true."),
                        clip.CancelAfter, 0f, 1f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        RecordProfileUndo("Set Sprite Clip Cancel After");
                        clip.CancelAfter = cancelAfter;
                        SaveDirty();
                    }
                }
                EditorGUI.BeginChangeCheck();
                int priority = EditorGUILayout.IntField(
                    new GUIContent("Priority",
                        "Higher priority clips block lower-priority Play() while still playing (!force). Equal priority uses Interrupt."),
                    clip.Priority);
                if (EditorGUI.EndChangeCheck())
                {
                    RecordProfileUndo("Set Sprite Clip Priority");
                    clip.Priority = priority;
                    SaveDirty();
                }
                EditorGUI.BeginChangeCheck();
                int comboStart = EditorGUILayout.IntField(
                    new GUIContent("Combo Window Start",
                        "Inclusive start frame. Combo window is disabled while End < 0."),
                    clip.ComboWindowStartFrame);
                int comboEnd = EditorGUILayout.IntField(
                    new GUIContent("Combo Window End",
                        "Inclusive end frame. Set to -1 to disable (InComboWindow = false)."),
                    clip.ComboWindowEndFrame);
                int comboBoost = EditorGUILayout.IntField(
                    new GUIContent("Combo Priority Boost",
                        "While inside the window, subtract this from current Priority for Play gating. Interrupt is also treated as Always."),
                    clip.ComboWindowPriorityBoost);
                if (EditorGUI.EndChangeCheck())
                {
                    RecordProfileUndo("Set Sprite Clip Combo Window");
                    clip.ComboWindowStartFrame = comboStart;
                    clip.ComboWindowEndFrame = comboEnd;
                    clip.ComboWindowPriorityBoost = comboBoost;
                    SaveDirty();
                }
                DrawOnCompleteClipField(clip);
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
                    0, Mathf.Max(0, ClipSheetColumns(clip) - 1));
                int resolvedRow = clip.Row;
                clip.ResolveSheetCell(_selectedFrame, ClipSheetColumns(clip), ClipSheetRows(clip),
                    out resolvedRow, out _);
                EditorGUI.BeginChangeCheck();
                int frameRow = EditorGUILayout.IntField(
                    new GUIContent("Frame Row",
                        "Sheet row for this frame. Same as Sheet Row unless this clip mixes cells from more than one row."),
                    resolvedRow);
                if (EditorGUI.EndChangeCheck() &&
                    clip.FrameRows != null && _selectedFrame < clip.FrameRows.Length)
                {
                    frameRow = Mathf.Clamp(frameRow, 0, Mathf.Max(0, ClipSheetRows(clip) - 1));
                    RecordProfileUndo("Change Sprite Frame Row");
                    clip.FrameRows[_selectedFrame] = frameRow == clip.Row
                        ? SpriteClipDef.InheritClipRow
                        : frameRow;
                    SaveDirty();
                }
                ResolveClipSheetCell(clip, _selectedFrame, out _, out _, out int cellIndex);
                EditorGUILayout.LabelField(
                    new GUIContent("Cell Index",
                        "Row-major cell on this sheet: row * columns + column. Same index the sheet-cell picker uses."),
                    new GUIContent(cellIndex.ToString()));
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
                    if (GUILayout.Button(new GUIContent("1×1 from texture",
                        "Pick one or more sheet cells and add them as frames.")))
                        OpenSheetCellPicker();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(selectedCount > 1 ? "Duplicate Frames" : "Duplicate Frame"))
                        DuplicateSelectedFrames(clip);
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
                        {
                            RecordProfileUndo("Onion Skin All Past");
                            _profile.OnionPastFrames = Mathf.Max(0, clip.Frames.Length - 1);
                            SaveDirty();
                        }
                        if (GUILayout.Button("All Future"))
                        {
                            RecordProfileUndo("Onion Skin All Future");
                            _profile.OnionFutureFrames = Mathf.Max(0, clip.Frames.Length - 1);
                            SaveDirty();
                        }
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
                _showHitboxes = EditorGUILayout.Toggle(
                    new GUIContent("Show Colliders",
                        "Same as the preview Colliders: On/Off overlay. Off hides every collider debug, including socket profiles."),
                    _showHitboxes);
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
                        new GUIContent("Continuous Placement",
                            "Keep the shape tool armed after each place. Off (default) returns to select/edit like sockets. Right-click or Escape also returns to select."),
                        _continuousColliderPlacement);
                    _newHitboxId = Mathf.Clamp(EditorGUILayout.IntField(
                        "New Collider ID", _newHitboxId), 1, 255);
                    _newColliderLifetime = ColliderLifetimeFromPopup(EditorGUILayout.Popup(
                        new GUIContent("Lives On",
                            "This Frame = slash on this cell. This Clip = whole time that clip plays (crouch vs stand hurt). Character = body across clips; use Include / Exclude clip lists to skip Attack or projectile clips."),
                        ColliderLifetimeToPopup(_newColliderLifetime), ColliderLifetimeLabels));
                    _newColliderPhysics = (byte)EditorGUILayout.Popup(
                        new GUIContent("Physics",
                            "Query AABB = custom live boxes. Unity 2D = BoxCollider2D / CircleCollider2D on the authoring object. Both = both."),
                        _newColliderPhysics, ColliderPhysicsLabels);
                    _newColliderIsTrigger = EditorGUILayout.Toggle(
                        new GUIContent("Unity Trigger",
                            "Only Unity 2D colliders. Query AABB ignores this."),
                        _newColliderIsTrigger);

                    using (new EditorGUI.DisabledScope(_colliderCreationMode == ColliderCreationMode.None))
                    {
                        if (GUILayout.Button(new GUIContent("Cancel Creation Tool",
                                "Drop the armed shape tool. Same as right-click on the preview or Escape.")))
                            CancelColliderCreation("Collider creation cancelled");
                    }
                }
                EditorGUILayout.HelpBox(
                    _colliderCreationMode switch
                    {
                        ColliderCreationMode.None =>
                            "Choose a shape to create, or click existing colliders to select them. After create, the new collider stays selected for move/scale/edit. Drag empty preview space for marquee. Shift adds, Ctrl/Cmd toggles, Alt subtracts, Shift+Alt intersects.",
                        ColliderCreationMode.Polygon =>
                            "Click to place vertices. Click the first point, double-click, or press Enter to close. After close, the polygon is selected for edit. Right-click/Backspace removes the last point; right-click on empty or Escape cancels the tool.",
                        _ =>
                            "Click to place or drag to size. After place, select/edit is armed (sockets-style). Turn on Continuous Placement to keep creating. Click an existing collider to select it. Right-click or Escape cancels the tool.",
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
            if (Event.current.type == EventType.Repaint)
            {
                float measuredHeight = Mathf.Max(area.height,
                    GUILayoutUtility.GetLastRect().yMax + 28f);
                if (Mathf.Abs(measuredHeight - _inspectorMeasuredHeight) > 1f)
                {
                    _inspectorMeasuredHeight = measuredHeight;
                    Repaint();
                }
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
                Lifetime = _newColliderLifetime,
                Physics = _newColliderPhysics,
                IsTrigger = _newColliderIsTrigger,
            };
            definition.BindLifetime(clip.Name, frame);
            AddCreatedCollider(definition, frame);
            ClearPolygonDraft();
            if (!_continuousColliderPlacement)
                _colliderCreationMode = ColliderCreationMode.None;
        }

        void AddCreatedCollider(FrameBoxDef definition, int frame)
        {
            RecordProfileUndo("Create Sprite Collider");
            _profile.Hitboxes.Add(definition);
            ClearSocketSelection();
            _selectedColliders.Clear();
            _selectedColliders.Add(definition);
            FocusColliderInInspector(definition);
            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
            _status = definition.IsCharacter
                ? $"Created {definition.Shape} Character collider (Include/Exclude clips in details)"
                : definition.IsClip
                    ? $"Created {definition.Shape} This Clip collider"
                    : $"Created {definition.Shape} collider on frame {frame + 1}";
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

        static bool IsPreviewToolCancelClick(Event evt)
        {
            return evt != null &&
                ((evt.type == EventType.MouseDown && evt.button == 1) ||
                 evt.type == EventType.ContextClick);
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
            // Unclamped: polygon verts / hitboxes may sit outside the cell UV.
            return new Vector2(
                (point.x - cell.x) / Mathf.Max(1f, cell.width),
                (point.y - cell.y) / Mathf.Max(1f, cell.height));
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
            _selectedEventIndex = -1;
                _selectedOnionFrame = -1;
                GUIUtility.keyboardControl = controlId;

                if (evt.button == 0)
                {
                    var op = ReadSelectionOp(evt);
                    SelectCollider(found, op);
                    if (op == SelectionOp.Replace || _selectedColliders.Count == 1)
                        FocusColliderInInspector(found);
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
                        ClearColliderSelection();
                        _selectedColliders.Add(found);
                        ClearSocketSelection();
                    }
                    FocusColliderInInspector(found);
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
            menu.AddItem(new GUIContent("Edit Details"), false,
                () => FocusColliderInInspector(clicked));
            menu.AddItem(new GUIContent(selectedCount > 1
                    ? $"Duplicate Selected ({selectedCount})"
                    : "Duplicate"),
                false, DuplicateSelectedColliders);
            menu.AddSeparator(string.Empty);

            AddColliderLifetimeMenuItem(menu, "Lives On/Current Frame",
                (byte)SpriteColliderLifetime.Frame, clip, frame);
            AddColliderLifetimeMenuItem(menu, "Lives On/This Clip",
                (byte)SpriteColliderLifetime.Clip, clip, frame);
            AddColliderLifetimeMenuItem(menu, "Lives On/Character",
                (byte)SpriteColliderLifetime.Character, clip, frame);
            menu.AddSeparator("Lives On/");

            AddColliderShapeMenuItem(menu, "Shape/Square", SpriteColliderShape.Square);
            AddColliderShapeMenuItem(menu, "Shape/Circle", SpriteColliderShape.Circle);
            AddColliderShapeMenuItem(menu, "Shape/Polygon", SpriteColliderShape.Polygon);

            AddColliderPhysicsMenuItem(menu, "Physics/Query AABB",
                (byte)SpriteColliderPhysics.Query);
            AddColliderPhysicsMenuItem(menu, "Physics/Unity 2D",
                (byte)SpriteColliderPhysics.Unity2D);
            AddColliderPhysicsMenuItem(menu, "Physics/Both",
                (byte)SpriteColliderPhysics.Both);
            if (SelectedCollidersMatch(box => box.UsesUnity2D))
            {
                menu.AddItem(new GUIContent("Physics/Unity Trigger"),
                    SelectedCollidersMatch(box => box.IsTrigger),
                    ToggleSelectedColliderTrigger);
            }
            menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent("Transform/Reset Angle"), false,
                ResetSelectedColliderAngle);
            menu.AddItem(new GUIContent("Transform/Flip Horizontal"), false,
                () => FlipSelectedColliders(true, false));
            menu.AddItem(new GUIContent("Transform/Flip Vertical"), false,
                () => FlipSelectedColliders(false, true));
            menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent("Visibility/Show Selected"),
                SelectedCollidersMatch(box => !box.Hidden),
                () => SetSelectedColliderVisibility(false));
            menu.AddItem(new GUIContent("Visibility/Hide Selected"),
                SelectedCollidersMatch(box => box.Hidden),
                () => SetSelectedColliderVisibility(true));
            menu.AddItem(new GUIContent(selectedCount > 1
                    ? $"Lock Selected ({selectedCount})"
                    : "Lock Collider"),
                false, LockSelectedColliders);
            menu.AddSeparator(string.Empty);

            bool anyCharacter = false;
            foreach (var box in _selectedColliders)
            {
                if (box != null && box.IsCharacter)
                {
                    anyCharacter = true;
                    break;
                }
            }
            if (anyCharacter && clip != null)
            {
                string clipName = clip.Name;
                menu.AddItem(new GUIContent($"Character Filter/Exclude \"{clipName}\""),
                    false, () => AddCurrentClipToCharacterFilter(exclude: true));
                menu.AddItem(new GUIContent($"Character Filter/Include \"{clipName}\""),
                    false, () => AddCurrentClipToCharacterFilter(exclude: false));
                menu.AddItem(new GUIContent("Character Filter/Remove Current From Filters"),
                    false, RemoveCurrentClipFromCharacterFilters);
                menu.AddItem(new GUIContent("Character Filter/Clear Include + Exclude"),
                    false, ClearSelectedCharacterClipFilters);
                menu.AddSeparator(string.Empty);
            }

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


        void FocusColliderInInspector(FrameBoxDef box)
        {
            if (box == null || box.Locked)
                return;
            if (!_showHitboxes)
            {
                RecordWindowUndo("Show Preview Colliders");
                _showHitboxes = true;
            }
            if (!_selectedColliders.Contains(box))
            {
                ClearColliderSelection();
                _selectedColliders.Add(box);
                ClearSocketSelection();
            }
            OpenColliderRowDetails(box);
            _pendingInspectorScrollToColliders = true;
            _status = box.IsCharacter
                ? $"Editing Character collider #{box.Id} · {ColliderHomeLabel(box)}"
                : $"Editing {box.Shape} collider #{box.Id}";
            Repaint();
        }

        void AddColliderShapeMenuItem(GenericMenu menu, string path, SpriteColliderShape shape)
        {
            menu.AddItem(new GUIContent(path),
                SelectedCollidersMatch(box => box.Shape == shape),
                () => SetSelectedColliderShape(shape));
        }

        void SetSelectedColliderShape(SpriteColliderShape shape)
        {
            if (_selectedColliders.Count == 0)
                return;
            RecordProfileUndo("Set Selected Collider Shape");
            int changed = 0;
            foreach (var box in _selectedColliders)
            {
                if (box == null || box.Locked || box.Shape == shape)
                    continue;
                box.Shape = shape;
                if (shape == SpriteColliderShape.Polygon)
                    box.EnsurePolygon();
                changed++;
            }
            if (changed == 0)
                return;
            _status = $"Set {changed} collider{Plural(changed)} to {shape}";
            SaveDirty();
            Repaint();
        }

        void ToggleSelectedColliderTrigger()
        {
            if (_selectedColliders.Count == 0)
                return;
            bool next = !SelectedCollidersMatch(box => box.IsTrigger);
            RecordProfileUndo("Set Selected Collider Trigger");
            foreach (var box in _selectedColliders)
            {
                if (box == null || box.Locked || !box.UsesUnity2D)
                    continue;
                box.IsTrigger = next;
            }
            _status = next ? "Unity Trigger on" : "Unity Trigger off";
            SaveDirty();
            Repaint();
        }

        void ResetSelectedColliderAngle()
        {
            if (_selectedColliders.Count == 0)
                return;
            RecordProfileUndo("Reset Selected Collider Angle");
            int changed = 0;
            foreach (var box in _selectedColliders)
            {
                if (box == null || box.Locked || Mathf.Approximately(box.Angle, 0f))
                    continue;
                box.Angle = 0f;
                changed++;
            }
            if (changed == 0)
                return;
            _status = $"Reset angle on {changed} collider{Plural(changed)}";
            SaveDirty();
            Repaint();
        }

        void FlipSelectedColliders(bool horizontal, bool vertical)
        {
            if (_selectedColliders.Count == 0 || (!horizontal && !vertical))
                return;
            RecordProfileUndo(horizontal && vertical
                ? "Flip Selected Colliders"
                : horizontal ? "Flip Selected Colliders Horizontal" : "Flip Selected Colliders Vertical");
            int changed = 0;
            foreach (var box in _selectedColliders)
            {
                if (box == null || box.Locked)
                    continue;
                Rect r = box.RectUV;
                if (horizontal)
                    r.x = 1f - (r.x + r.width);
                if (vertical)
                    r.y = 1f - (r.y + r.height);
                box.RectUV = r;
                if (horizontal != vertical)
                    box.Angle = -box.Angle;
                if (box.Shape == SpriteColliderShape.Polygon && box.PolygonUV != null)
                {
                    for (int i = 0; i < box.PolygonUV.Length; i++)
                    {
                        Vector2 p = box.PolygonUV[i];
                        if (horizontal)
                            p.x = 1f - p.x;
                        if (vertical)
                            p.y = 1f - p.y;
                        box.PolygonUV[i] = p;
                    }
                }
                changed++;
            }
            if (changed == 0)
                return;
            _status = $"Flipped {changed} collider{Plural(changed)}";
            SaveDirty();
            Repaint();
        }

        void DuplicateSelectedColliders()
        {
            PruneColliderSelection(CurrentClip, _selectedFrame);
            if (_selectedColliders.Count == 0)
                return;
            RecordProfileUndo(_selectedColliders.Count == 1
                ? "Duplicate Sprite Collider"
                : "Duplicate Sprite Colliders");
            var copies = new List<FrameBoxDef>();
            foreach (var box in _selectedColliders)
            {
                if (box == null || box.Locked)
                    continue;
                var copy = box.Clone();
                copy.RectUV = new Rect(
                    Mathf.Clamp01(box.RectUV.x + 0.03f),
                    Mathf.Clamp01(box.RectUV.y + 0.03f),
                    box.RectUV.width,
                    box.RectUV.height);
                if (copy.RectUV.xMax > 1f)
                    copy.RectUV.x = Mathf.Max(0f, 1f - copy.RectUV.width);
                if (copy.RectUV.yMax > 1f)
                    copy.RectUV.y = Mathf.Max(0f, 1f - copy.RectUV.height);
                _profile.Hitboxes.Add(copy);
                copies.Add(copy);
            }
            if (copies.Count == 0)
                return;
            _selectedColliders.Clear();
            for (int i = 0; i < copies.Count; i++)
                _selectedColliders.Add(copies[i]);
            if (copies.Count == 1)
                FocusColliderInInspector(copies[0]);
            _status = $"Duplicated {copies.Count} collider{Plural(copies.Count)}";
            SaveDirty();
            Repaint();
        }

        void AddCurrentClipToCharacterFilter(bool exclude)
        {
            var clip = CurrentClip;
            if (clip == null || string.IsNullOrEmpty(clip.Name))
                return;
            string clipName = clip.Name;
            RecordProfileUndo(exclude
                ? "Exclude Clip From Character Collider"
                : "Include Clip On Character Collider");
            int changed = 0;
            foreach (var box in _selectedColliders)
            {
                if (box == null || box.Locked || !box.IsCharacter)
                    continue;
                box.EnsureCharacterClipFilters();
                if (exclude)
                {
                    box.CharacterIncludeClips.RemoveAll(n => string.Equals(n, clipName));
                    if (!box.CharacterExcludeClips.Contains(clipName))
                        box.CharacterExcludeClips.Add(clipName);
                }
                else
                {
                    box.CharacterExcludeClips.RemoveAll(n => string.Equals(n, clipName));
                    if (!box.CharacterIncludeClips.Contains(clipName))
                        box.CharacterIncludeClips.Add(clipName);
                }
                changed++;
            }
            if (changed == 0)
                return;
            _status = exclude
                ? $"Excluded \"{clipName}\" from {changed} Character collider{Plural(changed)}"
                : $"Included \"{clipName}\" on {changed} Character collider{Plural(changed)}";
            SaveDirty();
            Repaint();
        }

        void RemoveCurrentClipFromCharacterFilters()
        {
            var clip = CurrentClip;
            if (clip == null || string.IsNullOrEmpty(clip.Name))
                return;
            string clipName = clip.Name;
            RecordProfileUndo("Remove Clip From Character Collider Filters");
            int changed = 0;
            foreach (var box in _selectedColliders)
            {
                if (box == null || box.Locked || !box.IsCharacter)
                    continue;
                box.EnsureCharacterClipFilters();
                int before = box.CharacterIncludeClips.Count + box.CharacterExcludeClips.Count;
                box.CharacterIncludeClips.RemoveAll(n => string.Equals(n, clipName));
                box.CharacterExcludeClips.RemoveAll(n => string.Equals(n, clipName));
                if (box.CharacterIncludeClips.Count + box.CharacterExcludeClips.Count != before)
                    changed++;
            }
            if (changed == 0)
                return;
            _status = $"Removed \"{clipName}\" from {changed} Character filter list{Plural(changed)}";
            SaveDirty();
            Repaint();
        }

        void ClearSelectedCharacterClipFilters()
        {
            RecordProfileUndo("Clear Character Collider Clip Filters");
            int changed = 0;
            foreach (var box in _selectedColliders)
            {
                if (box == null || box.Locked || !box.IsCharacter)
                    continue;
                box.EnsureCharacterClipFilters();
                if (box.CharacterIncludeClips.Count == 0 && box.CharacterExcludeClips.Count == 0)
                    continue;
                box.CharacterIncludeClips.Clear();
                box.CharacterExcludeClips.Clear();
                changed++;
            }
            if (changed == 0)
                return;
            _status = $"Cleared filters on {changed} Character collider{Plural(changed)}";
            SaveDirty();
            Repaint();
        }


        void AddColliderLifetimeMenuItem(GenericMenu menu, string path, byte lifetime,
            SpriteClipDef clip, int frame)
        {
            menu.AddItem(new GUIContent(path),
                SelectedCollidersMatch(box => box.Lifetime == lifetime),
                () => SetSelectedColliderLifetime(lifetime, clip, frame));
        }

        void AddColliderPhysicsMenuItem(GenericMenu menu, string path, byte physics)
        {
            menu.AddItem(new GUIContent(path),
                SelectedCollidersMatch(box => box.Physics == physics),
                () => SetSelectedColliderPhysics(physics));
        }

        bool SelectedCollidersMatch(Predicate<FrameBoxDef> predicate)
        {
            if (_selectedColliders.Count == 0)
                return false;
            foreach (var box in _selectedColliders)
            {
                if (box == null || !predicate(box))
                    return false;
            }
            return true;
        }

        void SetSelectedColliderLifetime(byte lifetime, SpriteClipDef clip, int frame)
        {
            if (clip == null || _selectedColliders.Count == 0)
                return;
            RecordProfileUndo("Set Selected Collider Lifetime");
            int changed = 0;
            foreach (var box in _selectedColliders)
            {
                if (box == null || box.Locked)
                    continue;
                box.Lifetime = lifetime;
                box.BindLifetime(clip.Name, frame);
                changed++;
            }
            SaveDirty();
            SealUndoGroup();
            _status = $"Set {changed} collider{Plural(changed)} to {ColliderLifetimeDisplayName(lifetime)}";
            if (_selectedColliders.Count == 1)
            {
                foreach (var box in _selectedColliders)
                {
                    OpenColliderRowDetails(box);
                    break;
                }
            }
            Repaint();
        }

        void SetSelectedColliderPhysics(byte physics)
        {
            if (_selectedColliders.Count == 0)
                return;
            RecordProfileUndo("Set Selected Collider Physics");
            int changed = 0;
            foreach (var box in _selectedColliders)
            {
                if (box == null || box.Locked)
                    continue;
                box.Physics = physics;
                changed++;
            }
            SaveDirty();
            SealUndoGroup();
            _status = $"Changed physics on {changed} collider{Plural(changed)}";
            Repaint();
        }

        void SetSelectedColliderVisibility(bool hidden)
        {
            if (_selectedColliders.Count == 0)
                return;
            RecordProfileUndo(hidden ? "Hide Selected Colliders" : "Show Selected Colliders");
            foreach (var box in _selectedColliders)
            {
                if (box != null)
                    box.Hidden = hidden;
            }
            SaveDirty();
            SealUndoGroup();
            _status = hidden ? "Hid selected colliders" : "Showed selected colliders";
            Repaint();
        }

        void LockSelectedColliders()
        {
            if (_selectedColliders.Count == 0)
                return;
            RecordProfileUndo("Lock Selected Colliders");
            int count = 0;
            foreach (var box in _selectedColliders)
            {
                if (box == null)
                    continue;
                box.Locked = true;
                count++;
            }
            _selectedColliders.Clear();
            ClearColliderTransform();
            SaveDirty();
            SealUndoGroup();
            _status = $"Locked {count} collider{Plural(count)}";
            Repaint();
        }

        static string ColliderLifetimeDisplayName(byte lifetime)
        {
            if (lifetime == (byte)SpriteColliderLifetime.Clip)
                return "This Clip";
            if (lifetime == (byte)SpriteColliderLifetime.Character)
                return "Character";
            return "Current Frame";
        }

        FrameBoxDef FindColliderAt(SpriteClipDef clip, int frame, Rect cell, Vector2 point)
        {
            FrameBoxDef found = null;
            float bestArea = float.MaxValue;
            foreach (var box in BoxesFor(clip, frame))
            {
                if (box.Hidden || box.Locked)
                    continue;
                if (!ColliderContains(box, cell, point))
                    continue;
                // Prefer the tightest hit so a circle overlapping a larger selected
                // square can be clicked without unselecting first.
                float area = Mathf.Abs(box.RectUV.width * box.RectUV.height);
                if (found == null || area <= bestArea)
                {
                    found = box;
                    bestArea = area;
                }
            }
            return found;
        }

        void SelectCollider(FrameBoxDef box, SelectionOp op)
        {
            if (box == null || box.Locked)
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
            if (_selectedColliders.Count == 1)
            {
                foreach (var selected in _selectedColliders)
                {
                    OpenColliderRowDetails(selected);
                    break;
                }
            }
            if (_selectedColliders.Count > 0)
                _pivotSelected = false;
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
                {
                    if (!colliders[i].Locked)
                        _selectedColliders.Add(colliders[i]);
                }
                if (_colliderListAnchor < 0)
                    _colliderListAnchor = index;
                if (_selectedColliders.Count > 0)
                    _pivotSelected = false;
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
                if (IsSocketLocked(name))
                    continue;
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
                    if (box.Hidden || box.Locked)
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
            _selectedEventIndex = -1;
            _selectedOnionFrame = -1;
            if (_selectedColliders.Count > 0 || _selectedSockets.Count > 0)
                _pivotSelected = false;
            SyncSocketPrimaryFromSelection();
        }

        string PreviewSelectionStatus(string prefix = "Selected")
        {
            int colliders = _selectedColliders.Count;
            int sockets = _selectedSockets.Count;
            if (colliders == 0 && sockets == 0)
            {
                if (_pivotSelected)
                    return $"{prefix} profile pivot {_profile.Pivot.x:F2}, {_profile.Pivot.y:F2}";
                return "Preview selection cleared";
            }
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
            _selectedEventIndex = -1;
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

        const float EllipticalOrbitFlatten = 0.58f;
        const float FibonacciGoldenAngle = 137.508f;

        static readonly string[] SocketClockModeLabels = { "Frame-Attached", "Independent" };
        static readonly string[] SocketAnchorSpaceLabels = { "Character", "World" };
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

        static readonly ColliderHandleKind[] SocketGizmoHandleKinds =
        {
            ColliderHandleKind.Rotate,
            ColliderHandleKind.CornerTL, ColliderHandleKind.CornerTR,
            ColliderHandleKind.CornerBR, ColliderHandleKind.CornerBL,
            ColliderHandleKind.EdgeT, ColliderHandleKind.EdgeR,
            ColliderHandleKind.EdgeB, ColliderHandleKind.EdgeL,
        };

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
            var marker = clip.FirstMarkerOnFrame(frame);
            if (marker != null)
                return EventAuthoredTime(clip, marker);
            return AuthoredStartTime(clip, frame) +
                Mathf.Clamp01(clip.EventNormalizedTimes[frame]) * FrameDuration(clip, frame);
        }

        float EventAuthoredTime(SpriteClipDef clip, SpriteClipEventMarker marker)
        {
            if (clip == null || marker == null || clip.Frames == null || clip.Frames.Length == 0)
                return 0f;
            int frame = Mathf.Clamp(marker.FrameIndex, 0, clip.Frames.Length - 1);
            return AuthoredStartTime(clip, frame) +
                Mathf.Clamp01(marker.NormalizedTime) * FrameDuration(clip, frame);
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

        void FlipActiveSheet(bool flipX, bool flipY)
        {
            if (!flipX && !flipY)
                return;
            EnsureProfile();
            if (_profile.Sheets == null || _profile.Sheets.Count == 0)
                _profile.EnsureSheets(_selectedSheet);
            else
                WriteActiveSheetFromLegacy();
            var sheet = _profile.SheetAt(_selectedSheet);
            Texture2D texture = sheet?.Texture ?? _profile.Sheet;
            if (texture == null)
            {
                _status = "Assign a sprite sheet before flipping";
                return;
            }

            int columns = Mathf.Max(1, sheet != null && sheet.Columns > 0 ? sheet.Columns : _profile.Columns);
            int rows = Mathf.Max(1, sheet != null && sheet.Rows > 0 ? sheet.Rows : _profile.Rows);
            string axis = flipX && flipY ? "horizontal and vertical" : flipX ? "horizontal" : "vertical";
            string fileName = SheetTextureFileName(texture);
            if (!EditorUtility.DisplayDialog(
                    "Flip Sprite Sheet",
                    $"Overwrite '{fileName}' on disk? Every {columns}×{rows} grid cell is mirrored {axis} in place, so clip frames stay on the same cells.\n\nPivot, sockets, colliders, and Independent Motion on this sheet are mirrored to match. Undo restores the previous texture and authored poses.",
                    flipX ? "Flip Horizontal" : "Flip Vertical",
                    "Cancel"))
                return;

            if (!SpriteSheetTextureFlip.TryReadPixels(
                    texture, out var pixels, out int width, out int height,
                    out string assetPath, out byte[] originalBytes, out string error))
            {
                _status = error;
                ShowNotification(new GUIContent(_status));
                return;
            }

            SpriteSheetPixelFlip.FlipCells(pixels, width, height, columns, rows, flipX, flipY);

            string undoName = flipX ? "Flip Sprite Sheet Horizontal" : "Flip Sprite Sheet Vertical";
            var fileUndo = SpriteSheetTextureFlip.UndoState;
            fileUndo.AssetPath = assetPath;
            fileUndo.Bytes = originalBytes;

            var target = UndoTarget;
            Undo.IncrementCurrentGroup();
            if (target != null)
                Undo.RegisterCompleteObjectUndo(target, undoName);
            Undo.RegisterCompleteObjectUndo(fileUndo, undoName);
            Undo.SetCurrentGroupName(undoName);
            Undo.FlushUndoRecordObjects();
            if (target != null)
                EditorUtility.SetDirty(target);
            PushUndoName(undoName);

            if (!SpriteSheetTextureFlip.TryWritePixels(
                    assetPath, pixels, width, height, out byte[] writtenBytes, out error))
            {
                _status = error;
                ShowNotification(new GUIContent(_status));
                return;
            }

            fileUndo.Bytes = writtenBytes;
            SpriteSheetPixelFlip.RemapProfileAfterCellFlip(_profile, _selectedSheet, flipX, flipY);
            _profile.SyncLegacyFromSheet(_selectedSheet);
            InvalidateSheetPixelCache();
            SaveDirty();
            _status = $"Flipped {fileName} {axis}";
            Repaint();
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
                    RecordProfileUndo("Auto-detect Transparent Grid");
                    _profile.Columns = columnCount;
                    _profile.Rows = rowCount;
                    WriteActiveSheetFromLegacy();
                    _status = $"Detected {columnCount} × {rowCount} grid";
                    SaveDirty();
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

        string _cropStatusDetail = "";

        void DrawCroppedLayoutStatus()
        {
            if (_profile == null)
                return;
            var def = _profile.SheetAt(_selectedSheet);
            if (_profile.CellLayoutMode != SpriteSheetCellLayoutMode.Cropped)
            {
                if (SpriteSheetProfile.HasCroppedCellData(def))
                    GUILayout.Label("Cropped rects stored (ignored in Grid mode).", _mutedStyle);
                return;
            }
            if (!SpriteSheetProfile.HasCroppedCellData(def))
            {
                GUILayout.Label("Cropped mode: no crop rects yet — run Detect spacing & crop cells.", _mutedStyle);
                return;
            }
            int cells = def.CroppedCellRects.Length;
            int cols = Mathf.Max(1, _profile.Columns);
            int rows = Mathf.Max(1, _profile.Rows);
            GUILayout.Label(
                $"Cropped: {cells} cell rects  •  grid {cols}×{rows}  •  {_cropStatusDetail}",
                _mutedStyle);
        }

        internal Texture2D SliceTargetTexture => _profile?.Sheet;

        internal ScriptableSpriteSheetProfile SliceProfileAsset => ProfileAsset;

        internal int SliceActiveSheetIndex => _selectedSheet;

        internal void SliceStepSheet(int delta)
        {
            if (_profile?.Sheets == null || _profile.Sheets.Count == 0)
                return;
            _selectedSheet = Mathf.Clamp(_selectedSheet + delta, 0, _profile.Sheets.Count - 1);
            _profile.SyncLegacyFromSheet(_selectedSheet);
            if (_profile.SheetsWorldHeightsDiffer())
            {
                RecordProfileUndo("Match Sheets World Size");
                RematchSheetsWorldSize(_selectedSheet);
                SaveDirty();
            }
            Repaint();
        }

        internal void SliceNotifyProfileEdited()
        {
            EditorUtility.SetDirty(ProfileAsset);
            Repaint();
        }

        internal void SliceReloadLegacyFromActiveSheet()
        {
            _profile.SyncLegacyFromSheet(_selectedSheet);
            Repaint();
        }

        /// <summary>Derived cell size for pixel-unit pivots (Automatic: none yet).</summary>
        internal bool TryGetSliceCellMetrics(SpriteSheetSliceRequest request,
            out int texW, out int texH, out int cellW, out int cellH)
        {
            var tex = _profile?.Sheet;
            if (tex == null)
            {
                texW = texH = cellW = cellH = 0;
                return false;
            }
            texW = tex.width;
            texH = tex.height;
            if (request.Type == SpriteSheetSliceType.GridByCellSize)
            {
                cellW = request.CellSize.x;
                cellH = request.CellSize.y;
                return true;
            }
            cellW = Mathf.Max(1, (texW - request.Offset.x -
                                  request.Padding.x * (request.Columns - 1)) / request.Columns);
            cellH = Mathf.Max(1, (texH - request.Offset.y -
                                  request.Padding.y * (request.Rows - 1)) / request.Rows);
            return true;
        }

        /// <summary>
        /// Unity Sprite Editor-style slice: computes cell rects for the
        /// requested mode and writes grid + CroppedCellRects + pivot into the
        /// active sheet (Cropped layout, like Detect spacing & crop cells).
        /// </summary>
        internal void RunSheetSlice(SpriteSheetSliceRequest request)
        {
            if (_profile?.Sheet == null)
            {
                _status = "Assign a sheet texture before slicing";
                return;
            }

            Texture2D owned = null;
            try
            {
                var pixels = SpriteSheetSlicing.GetPixels32(_profile.Sheet, out owned);
                int texW = _profile.Sheet.width;
                int texH = _profile.Sheet.height;

                var (rects, cols, rows, empty) =
                    SpriteSheetSlicing.SliceGrid(pixels, texW, texH, request);

                RecordProfileUndo("Slice Sheet");
                _profile.Columns = cols;
                _profile.Rows = rows;
                _profile.CroppedCellRects = rects; // dormant in Grid layout
                _profile.CellLayoutMode = SpriteSheetCellLayoutMode.Grid;
                _profile.Pivot = new Vector2(
                    Mathf.Clamp01(request.PivotNormalized.x),
                    Mathf.Clamp01(request.PivotNormalized.y));
                WriteActiveSheetFromLegacy();
                RematchSheetsWorldSize(_selectedSheet);

                string typeLabel = request.Type == SpriteSheetSliceType.GridByCellSize
                    ? request.CellSize.x + "x" + request.CellSize.y + "px"
                    : cols + "x" + rows;
                _status = empty > 0
                    ? "Sliced (" + typeLabel + "): " + rects.Length + " cells, " + empty + " empty"
                    : "Sliced (" + typeLabel + "): " + rects.Length + " cells, layout Grid";
                Repaint();
            }
            finally
            {
                if (owned != null)
                    DestroyImmediate(owned);
            }
        }

        Texture2D ISpriteSheetSliceHost.SliceTargetTexture => SliceTargetTexture;

        bool ISpriteSheetSliceHost.TryGetSliceCellMetrics(SpriteSheetSliceRequest request,
            out int texW, out int texH, out int cellW, out int cellH)
            => TryGetSliceCellMetrics(request, out texW, out texH, out cellW, out cellH);

        void ISpriteSheetSliceHost.RunSheetSlice(SpriteSheetSliceRequest request)
            => RunSheetSlice(request);

        void DetectSpacingAndCropCells(bool setMode)
        {
            if (_profile?.Sheet == null)
            {
                _status = "Assign a sheet texture before cropping cells";
                return;
            }

            Texture2D readable = null;
            bool destroy = false;
            try
            {
                RecordProfileUndo("Detect Spacing & Crop Cells");
                readable = _profile.Sheet.isReadable ? _profile.Sheet : DuplicateReadable(_profile.Sheet);
                destroy = readable != _profile.Sheet;
                var pixels = readable.GetPixels32();
                int width = readable.width;
                int height = readable.height;
                int columns = Mathf.Max(1, _profile.Columns);
                int rows = Mathf.Max(1, _profile.Rows);

                SpriteSheetProfile.EstimateBandSpacing(
                    pixels, width, height, SpriteSheetProfile.CroppedAlphaThreshold,
                    out float avgColGutter, out float avgRowGutter,
                    out int opaqueCols, out int opaqueRows);

                var rects = SpriteSheetProfile.BuildCroppedCellRects(
                    pixels, width, height, columns, rows,
                    SpriteSheetProfile.CroppedAlphaThreshold);

                _profile.CroppedCellRects = rects;
                if (setMode)
                    _profile.CellLayoutMode = SpriteSheetCellLayoutMode.Cropped;

                int nonempty = 0;
                int minW = int.MaxValue, minH = int.MaxValue, maxW = 0, maxH = 0;
                for (int i = 0; i < rects.Length; i++)
                {
                    var r = rects[i];
                    if (r.width <= 1 && r.height <= 1)
                        continue;
                    nonempty++;
                    if (r.width < minW) minW = r.width;
                    if (r.height < minH) minH = r.height;
                    if (r.width > maxW) maxW = r.width;
                    if (r.height > maxH) maxH = r.height;
                }
                if (nonempty == 0)
                {
                    minW = 0;
                    minH = 0;
                }

                _cropStatusDetail =
                    $"spacing ~{avgColGutter:0.#}px H / {avgRowGutter:0.#}px V  •  "
                    + $"opaque bands {opaqueCols}×{opaqueRows}  •  "
                    + $"crop size {minW}–{maxW} × {minH}–{maxH} px";

                _status = setMode
                    ? $"Cropped {nonempty}/{rects.Length} cells  •  {_cropStatusDetail}"
                    : $"Stored crops for {nonempty}/{rects.Length} cells  •  {_cropStatusDetail}";
                InvalidateSheetPixelCache();
                Repaint();
            }
            catch (System.Exception exception)
            {
                _status = "Crop detect failed: " + exception.Message;
            }
            finally
            {
                if (destroy && readable != null) DestroyImmediate(readable);
            }
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

        internal bool AutoSaveEnabled => _autoSaveEnabled;
        internal int AutoSaveIntervalMinutes => _autoSaveIntervalMinutes;
        internal string AutoSaveLastStatus => _autoSaveLastStatus;

        void ShowSettingsPopup()
        {
            PopupWindow.Show(_settingsButtonRect, new SpriteSheetToolSettingsPopup(this));
        }

        void SelectEventMarker(SpriteClipDef clip, int markerIndex, float authoredTime, bool jump = true)
        {
            if (clip == null)
                return;
            clip.EnsureEventMarkers();
            if (markerIndex < 0 || markerIndex >= clip.EventMarkers.Count)
                return;
            var marker = clip.EventMarkers[markerIndex];
            if (marker == null || marker.EventId == 0)
                return;
            _selectedEventFrame = marker.FrameIndex;
            _selectedEventIndex = markerIndex;
            _selectedEventClipName = clip.Name;
            _selectedSocketDrawFrame = -1;
            _selectedSocketDrawName = null;
            _playing = false;
            ClearColliderSelection();
            _selectedOnionFrame = -1;
            _eventRowDetailsExpanded = true;
            if (jump)
            {
                if (clip == CurrentClip)
                {
                    _selectedFrame = Mathf.Clamp(marker.FrameIndex, 0, clip.Frames.Length - 1);
                    _previewTime = PreviewTimeForAuthoredTime(clip, authoredTime);
                }
                else
                    JumpToEventHome(clip, markerIndex, marker.FrameIndex);
            }
            _status = $"Selected {EventName(marker.EventId)} at {authoredTime:F3}s";
        }

        void DeleteSelectedEventMarker()
        {
            var clip = FindClipByEventSelection() ?? CurrentClip;
            if (clip == null)
                return;
            clip.EnsureEventMarkers();
            if (_selectedEventIndex >= 0 && _selectedEventIndex < clip.EventMarkers.Count)
            {
                RemoveEventMarkerAt(clip, _selectedEventIndex, focusPreview: clip == CurrentClip);
                return;
            }
            if (_selectedEventFrame >= 0)
                SetFrameEvent(clip, _selectedEventFrame, 0, focusPreview: clip == CurrentClip);
        }

        void PruneEventSelection(SpriteClipDef clip)
        {
            var owner = FindClipByEventSelection() ?? clip;
            if (owner == null)
            {
                _selectedEventFrame = -1;
                _selectedEventIndex = -1;
                _selectedEventClipName = null;
                return;
            }
            owner.EnsureEventMarkers();
            if (_selectedEventIndex >= 0 && _selectedEventIndex < owner.EventMarkers.Count)
            {
                var marker = owner.EventMarkers[_selectedEventIndex];
                if (marker != null && marker.EventId != 0)
                {
                    _selectedEventFrame = marker.FrameIndex;
                    return;
                }
            }
            int first = owner.IndexOfFirstMarkerOnFrame(_selectedEventFrame);
            if (first >= 0)
            {
                _selectedEventIndex = first;
                return;
            }
            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
            _selectedEventClipName = null;
        }

        SpriteClipDef FindClipByEventSelection()
        {
            int index = FindClipIndexByName(_selectedEventClipName);
            if (index < 0 || _profile?.Clips == null)
                return null;
            return _profile.Clips[index];
        }

        int EventMarkerAt(SpriteClipDef clip, float[] frameTimes, float[] durations,
                          float pixelsPerSecond, Vector2 point)
        {
            if (clip == null || frameTimes == null || durations == null)
                return -1;
            if (point.y < TimelineEventLaneY || point.y >= TimelineDrawLaneY)
                return -1;
            clip.EnsureEventMarkers();
            float laneY = TimelineEventLaneY + 13f;
            int hit = -1;
            for (int i = 0; i < clip.EventMarkers.Count; i++)
            {
                var marker = clip.EventMarkers[i];
                if (marker == null || marker.EventId == 0)
                    continue;
                int frame = marker.FrameIndex;
                if (frame < 0 || frame >= frameTimes.Length)
                    continue;
                float markerTime = _timelineDragMode == TimelineDragMode.Event &&
                                   _dragEventMarkerIndex == i
                    ? _dragEventAuthoredTime
                    : frameTimes[frame] + Mathf.Clamp01(marker.NormalizedTime) * durations[frame];
                float markerX = 48f + markerTime * pixelsPerSecond;
                Vector2 center = new(markerX, laneY);
                if ((point - center).sqrMagnitude <= 100f)
                    hit = i;
            }
            return hit;
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
            ResolveClipSheetCell(clip, frame, out _, out _, out int index);
            return index;
        }

        void ResolveClipSheetCell(SpriteClipDef clip, int frame, out int column, out int row, out int index)
        {
            int columns = ClipSheetColumns(clip);
            int rows = ClipSheetRows(clip);
            if (clip?.Frames == null || clip.Frames.Length == 0)
            {
                column = 0;
                row = 0;
                index = 0;
                return;
            }
            frame = Mathf.Clamp(frame, 0, clip.Frames.Length - 1);
            clip.ResolveSheetCell(frame, columns, rows, out row, out column);
            index = row * columns + column;
        }

        static string FormatSheetCellCompact(int column, int row, int index)
            => $"c{column} r{row}  i{index}";

        static string FormatSheetCellFull(int column, int row, int index)
            => $"col {column}  row {row}  •  index {index}";

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
            Rect uv = ResolveCellUvRect(sheet, cellIndex, columns, rows);
            Color previous = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, sheet, uv, true);
            GUI.color = previous;
        }

        Rect ResolveCellUvRect(Texture2D sheet, int cellIndex, int columns, int rows)
        {
            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            var def = _profile != null ? _profile.SheetAt(_selectedSheet) : null;
            if (def != null &&
                def.Texture == sheet &&
                def.CellLayoutMode == SpriteSheetCellLayoutMode.Cropped)
            {
                // Prefer the active sheet def (includes CroppedCellRects).
                return SpriteSheetProfile.GetCellUvRect(def, cellIndex);
            }
            if (_profile != null &&
                _profile.Sheet == sheet &&
                _profile.CellLayoutMode == SpriteSheetCellLayoutMode.Cropped &&
                _profile.CroppedCellRects != null &&
                _profile.CroppedCellRects.Length > 0)
            {
                var temp = new SpriteSheetDef
                {
                    Texture = sheet,
                    Columns = columns,
                    Rows = rows,
                    CellLayoutMode = SpriteSheetCellLayoutMode.Cropped,
                    CroppedCellRects = _profile.CroppedCellRects,
                };
                return SpriteSheetProfile.GetCellUvRect(temp, cellIndex);
            }
            return SpriteSheetProfile.GetUniformCellUvRect(columns, rows, cellIndex);
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
            bool active = !_pivotLocked && (_draggingPivot || _pivotSelected);
            float radius = active ? 6.5f : 5.5f;
            Color fill = _pivotLocked
                ? new Color(0.35f, 0.55f, 0.38f, 1f)
                : active
                    ? new Color(0.45f, 1f, 0.48f, 1f)
                    : new Color(0.22f, 0.82f, 0.3f, 1f);
            Color outline = new Color(0.06f, 0.32f, 0.1f, 1f);

            Handles.BeginGUI();
            if (active)
            {
                Handles.color = new Color(0.55f, 1f, 0.6f, 0.35f);
                Handles.DrawSolidDisc(point, Vector3.forward, radius + 4.5f);
                Handles.color = new Color(0.2f, 0.85f, 0.35f, 0.95f);
                Handles.DrawWireDisc(point, Vector3.forward, radius + 5.5f);
            }
            Handles.color = outline;
            Handles.DrawSolidDisc(point, Vector3.forward, radius + 1.15f);
            Handles.color = fill;
            Handles.DrawSolidDisc(point, Vector3.forward, radius);
            // Crosshair for selected / dragging pivot.
            if (active)
            {
                Handles.color = new Color(0.05f, 0.28f, 0.08f, 0.95f);
                Handles.DrawLine(point + new Vector2(-9f, 0f), point + new Vector2(9f, 0f));
                Handles.DrawLine(point + new Vector2(0f, -9f), point + new Vector2(0f, 9f));
            }
            Handles.EndGUI();

            // Lock control lives in the inspector only (SHEET Pivot / PIVOT bar).
            // Do not draw a lock badge here: GUI.Label would allocate an extra IMGUI
            // control ID after MouseDown selects the pivot and break hotControl drag.

            if (!_pivotLocked)
            {
                EditorGUIUtility.AddCursorRect(
                    new Rect(point.x - PivotHandleHitRadius, point.y - PivotHandleHitRadius,
                        PivotHandleHitRadius * 2f, PivotHandleHitRadius * 2f),
                    MouseCursor.MoveArrow);
            }
        }

        void DrawSheetTextureInfo()
        {
            if (_profile.Sheet == null)
            {
                EditorGUILayout.LabelField("File name", "—");
                EditorGUILayout.LabelField("Size", "—");
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("File name", SheetTextureFileName(_profile.Sheet));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Cells",
                        "Open the cell editor: click a cell, drag its pivot, edit its rect."),
                        EditorStyles.miniButton, GUILayout.Width(52f)))
                {
                    SpriteCellEditorWindow.Show(this);
                }
            }
            EditorGUILayout.LabelField("Size",
                $"{_profile.Sheet.width} × {_profile.Sheet.height}");
            int columns = Mathf.Max(1, _profile.Columns);
            int rows = Mathf.Max(1, _profile.Rows);
            EditorGUILayout.LabelField("Cell size",
                _profile.CellLayoutMode == SpriteSheetCellLayoutMode.Cropped
                    ? "Cropped per cell (see Detect spacing)"
                    : $"{_profile.Sheet.width / columns} × {_profile.Sheet.height / rows} px");
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
            string sizeNote = "grid";
            if (_profile.CellLayoutMode == SpriteSheetCellLayoutMode.Cropped)
            {
                var def = _profile.SheetAt(_selectedSheet);
                var clip = CurrentClip;
                int cellIndex = clip != null
                    ? CellIndexOf(clip, Mathf.Clamp(_selectedFrame, 0, Mathf.Max(0, clip.Frames.Length - 1)))
                    : 0;
                if (SpriteSheetProfile.TryGetActiveCellPixels(def, cellIndex, out float cw, out float ch))
                {
                    cellW = cw;
                    cellH = ch;
                    sizeNote = "cropped";
                }
            }
            float ppu = Mathf.Max(SpriteSheetProfile.MinPixelsPerUnit, _profile.PixelsPerUnit);
            float worldW = cellW / ppu;
            float worldH = cellH / ppu;
            EditorGUILayout.LabelField("Cell in world",
                $"{worldW:0.###} × {worldH:0.###} units ({sizeNote})");
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
            if (_profile.CellLayoutMode == SpriteSheetCellLayoutMode.Cropped)
            {
                var clip = CurrentClip;
                if (clip != null)
                {
                    int cellIndex = CellIndexOf(clip, Mathf.Clamp(_selectedFrame, 0, Mathf.Max(0, clip.Frames.Length - 1)));
                    var def = _profile.SheetAt(_selectedSheet);
                    if (SpriteSheetProfile.TryGetActiveCellPixels(def, cellIndex, out float cw, out float ch) &&
                        ch > 0.01f)
                        cellAspect = cw / ch;
                }
            }
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
        {
            if (_pivotLocked)
                return false;
            return (mouse - PivotScreen(cell)).sqrMagnitude <=
                   PivotHandleHitRadius * PivotHandleHitRadius;
        }

        bool PivotHandleHitTest(Rect cell, Vector2 mouse)
        {
            // Geometric hit only (ignores lock). Locked pivots must not select/drag;
            // callers gate on !_pivotLocked for edit, and skip preview unlock entirely.
            return (mouse - PivotScreen(cell)).sqrMagnitude <=
                   PivotHandleHitRadius * PivotHandleHitRadius;
        }

        static Vector2 ScreenToPivot(Vector2 screen, Rect cell)
        {
            return new Vector2(
                Mathf.Clamp01((screen.x - cell.x) / Mathf.Max(1f, cell.width)),
                Mathf.Clamp01(1f - (screen.y - cell.y) / Mathf.Max(1f, cell.height)));
        }

        bool HandlePivotInput(int controlId, Rect cell)
        {
            if (!_showPivot || _pivotLocked)
                return false;

            var evt = Event.current;
            bool overHandle = PivotHandleContains(cell, evt.mousePosition);
            // Reclaim hotControl if conditional GUI (badges/gizmos) shifted the
            // preview control id between MouseDown and MouseDrag/MouseUp.
            if (_draggingPivot && GUIUtility.hotControl != 0 &&
                GUIUtility.hotControl != controlId)
                GUIUtility.hotControl = controlId;
            bool ownsDrag = _draggingPivot && GUIUtility.hotControl == controlId;

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && ownsDrag)
            {
                EndPivotDrag(controlId, save: true);
                evt.Use();
                Repaint();
                return true;
            }

            if (IsPreviewContextClick(evt) && (overHandle || _pivotSelected))
            {
                if (_draggingPivot)
                    EndPivotDrag(controlId, save: true);
                SelectProfilePivot();
                ShowPivotContextMenu();
                evt.Use();
                Repaint();
                return true;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && overHandle)
            {
                RecordProfileUndo("Move Sprite Pivot");
                _draggingPivot = true;
                SelectProfilePivot();
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
                // Legacy mirrors the active sheet: push through or the next
                // OnGUI's EnsureSheets -> SyncLegacyFromSheet reverts the drag.
                WriteActiveSheetFromLegacy();
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

        void SelectProfilePivot()
        {
            if (_pivotLocked)
                return;
            _pivotSelected = true;
            _playing = false;
            _selectedOnionFrame = -1;
            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
            ClearColliderSelection();
            // ClearColliderSelection also clears sockets.
        }

        void DrawSelectedPivotBar(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.1f, 0.2f, 0.14f, 1f));
            DrawBorder(rect, new Color(0.35f, 0.9f, 0.4f, 1f), 1f);
            string lockNote = _pivotLocked ? "  •  LOCKED" : string.Empty;
            GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 18f),
                $"PIVOT  {_profile.Pivot.x:F3}, {_profile.Pivot.y:F3}{lockNote}",
                EditorStyles.boldLabel);
            float x = rect.x + 8f;
            if (GUI.Button(new Rect(x, rect.y + 26f, 120f, 22f),
                    new GUIContent("Pivot Actions…",
                        "Snap presets, Character collider, opaque bounds, copy/paste, lock."),
                    EditorStyles.miniButton))
                ShowPivotContextMenu();
            x += 128f;
            using (new EditorGUI.DisabledScope(_pivotLocked))
            {
                if (GUI.Button(new Rect(x, rect.y + 26f, 100f, 22f),
                        new GUIContent("Snap Feet", "Bottom-center (0.5, 0). Unlock first if locked."),
                        EditorStyles.miniButton))
                    SetProfilePivot(new Vector2(0.5f, 0f), "Snap Pivot to Bottom Center");
            }
            x += 108f;
            bool nextShow = GUI.Toggle(new Rect(x, rect.y + 26f, 92f, 22f), _showPivot,
                new GUIContent("Show Pivot",
                    "Same as Pivot: On/Off on the preview overlay (next to Colliders / Size / Debug)."));
            if (nextShow != _showPivot)
            {
                RecordWindowUndo("Toggle Show Pivot");
                _showPivot = nextShow;
                if (!_showPivot)
                {
                    _draggingPivot = false;
                    _pivotSelected = false;
                }
            }
            x += 96f;
            if (GUI.Button(new Rect(x, rect.y + 26f, 28f, 22f), PivotLockContent(_pivotLocked),
                    EditorStyles.miniButton))
                SetPivotLocked(!_pivotLocked);
        }

        void HandleWindowPivotContextClick(Rect previewRect)
        {
            var evt = Event.current;
            if (!_showPivot || !IsPreviewContextClick(evt))
                return;
            if (_colliderCreationMode != ColliderCreationMode.None || _socketPlacementArmed)
                return;

            var canvas = new Rect(
                previewRect.x + 10f, previewRect.y + 54f,
                previewRect.width - 20f, previewRect.height - 66f);
            if (!canvas.Contains(evt.mousePosition))
                return;
            if (_profile?.Sheet == null)
                return;

            var localCanvas = new Rect(0f, 0f, canvas.width, canvas.height);
            if (!TryComputePreviewLayout(localCanvas, out Rect cell, out _, out _))
                return;

            Vector2 contentMouse = evt.mousePosition - canvas.position + _previewScroll;
            if (_pivotLocked)
                return; // Unlock only from inspector lock toggle.
            bool overPivot = PivotHandleHitTest(cell, contentMouse);
            if (!overPivot && !_pivotSelected)
                return;
            // Prefer socket pin hit when not directly on the pivot handle.
            if (!overPivot && _showPreviewDebug)
            {
                var clip = CurrentClip;
                if (clip != null)
                {
                    int frame = EvaluatePreview(clip, _previewTime).Frame;
                    if (FindSocketAt(clip, frame, cell, contentMouse) != null)
                        return;
                }
            }
            if (!TryHandlePivotContextClick(cell, contentMouse))
                return;
        }

        bool TryHandlePivotContextClick(Rect cell, Vector2? mouseOverride = null)
        {
            if (!_showPivot)
                return false;
            var evt = Event.current;
            if (!IsPreviewContextClick(evt))
                return false;

            Vector2 mouse = mouseOverride ?? evt.mousePosition;
            if (_pivotLocked)
                return false; // No preview unlock; use inspector lock toggle.
            bool overHandle = PivotHandleHitTest(cell, mouse);
            if (!overHandle && !_pivotSelected)
                return false;
            // When selected, require click inside the cell (or on the handle) so we
            // do not steal socket/collider context menus elsewhere in the canvas.
            if (!overHandle && !cell.Contains(mouse))
                return false;

            if (_draggingPivot)
                EndPivotDrag(GUIUtility.hotControl, save: true);
            SelectProfilePivot();
            ShowPivotContextMenu();
            evt.Use();
            Repaint();
            return true;
        }

        void ShowPivotContextMenu()
        {
            if (_profile == null)
                return;
            var menu = new GenericMenu();
            if (_pivotLocked)
            {
                menu.AddItem(new GUIContent("Unlock Pivot"), false, () => SetPivotLocked(false));
                menu.AddSeparator(string.Empty);
                menu.AddDisabledItem(new GUIContent("Snap/Cell Center (locked)"));
                menu.AddDisabledItem(new GUIContent("Snap/Bottom Center — Feet (locked)"));
                menu.AddDisabledItem(new GUIContent("Other snap / edit actions (unlock first)"));
                menu.ShowAsContext();
                return;
            }
            menu.AddItem(new GUIContent("Lock Pivot"), false, () => SetPivotLocked(true));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Snap/Cell Center (0.5, 0.5)"), false,
                () => SetProfilePivot(new Vector2(0.5f, 0.5f), "Snap Pivot to Cell Center"));
            menu.AddItem(new GUIContent("Snap/Bottom Center — Feet (0.5, 0)"), false,
                () => SetProfilePivot(new Vector2(0.5f, 0f), "Snap Pivot to Bottom Center"));
            menu.AddItem(new GUIContent("Snap/Top Center (0.5, 1)"), false,
                () => SetProfilePivot(new Vector2(0.5f, 1f), "Snap Pivot to Top Center"));
            menu.AddItem(new GUIContent("Snap/Left Center (0, 0.5)"), false,
                () => SetProfilePivot(new Vector2(0f, 0.5f), "Snap Pivot to Left Center"));
            menu.AddItem(new GUIContent("Snap/Right Center (1, 0.5)"), false,
                () => SetProfilePivot(new Vector2(1f, 0.5f), "Snap Pivot to Right Center"));
            menu.AddItem(new GUIContent("Snap/Corners/Bottom Left (0, 0)"), false,
                () => SetProfilePivot(new Vector2(0f, 0f), "Snap Pivot to Bottom Left"));
            menu.AddItem(new GUIContent("Snap/Corners/Bottom Right (1, 0)"), false,
                () => SetProfilePivot(new Vector2(1f, 0f), "Snap Pivot to Bottom Right"));
            menu.AddItem(new GUIContent("Snap/Corners/Top Left (0, 1)"), false,
                () => SetProfilePivot(new Vector2(0f, 1f), "Snap Pivot to Top Left"));
            menu.AddItem(new GUIContent("Snap/Corners/Top Right (1, 1)"), false,
                () => SetProfilePivot(new Vector2(1f, 1f), "Snap Pivot to Top Right"));
            menu.AddSeparator(string.Empty);

            FrameBoxDef character = ResolveCharacterColliderForPivotSnap();
            if (character != null)
            {
                menu.AddItem(new GUIContent("Snap to Character Collider Center",
                        "Uses the selected Character collider, else the first Character box visible on this clip."),
                    false, SnapPivotToCharacterColliderCenter);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(
                    "Snap to Character Collider Center (add Character collider first)"));
            }
            menu.AddItem(new GUIContent("Add Character Collider…",
                    "Create a Character (body) square collider on this profile, then snap pivot to its center."),
                false, () => PromptAddCharacterColliderForPivot(snapAfter: true));
            menu.AddSeparator(string.Empty);

            if (TryGetOpaqueContentPivot(bottomCenter: false, out _))
            {
                menu.AddItem(new GUIContent("Snap to Opaque Content Center",
                        "Tight AABB center of opaque pixels in the active cell (Grid or Cropped)."),
                    false, () => SnapPivotToOpaqueContent(bottomCenter: false));
                menu.AddItem(new GUIContent("Snap to Opaque Content Bottom Center",
                        "Feet of the art — bottom-center of the opaque AABB in the active cell."),
                    false, () => SnapPivotToOpaqueContent(bottomCenter: true));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(
                    "Snap to Opaque Content Center (no readable pixels)"));
                menu.AddDisabledItem(new GUIContent(
                    "Snap to Opaque Content Bottom Center (no readable pixels)"));
            }
            menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent("Copy Pivot"), false, CopyProfilePivot);
            if (_pivotClipboardValid)
                menu.AddItem(new GUIContent(
                        $"Paste Pivot ({_pivotClipboard.x:F2}, {_pivotClipboard.y:F2})"),
                    false, PasteProfilePivot);
            else
                menu.AddDisabledItem(new GUIContent("Paste Pivot"));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Reset to Default (0.5, 0.5)"), false,
                () => SetProfilePivot(SpriteSheetProfile.DefaultPivot, "Reset Sprite Pivot",
                    "Reset pivot to center"));
            menu.ShowAsContext();
        }

        void SetProfilePivot(Vector2 pivot, string undoName, string status = null)
        {
            if (_profile == null)
                return;
            if (_pivotLocked)
            {
                _status = "Pivot is locked — unlock to edit";
                Repaint();
                return;
            }
            pivot = new Vector2(Mathf.Clamp01(pivot.x), Mathf.Clamp01(pivot.y));
            if (pivot == _profile.Pivot && string.IsNullOrEmpty(status))
            {
                _status = $"Pivot already {_profile.Pivot.x:F3}, {_profile.Pivot.y:F3}";
                return;
            }
            RecordProfileUndo(string.IsNullOrEmpty(undoName) ? "Edit Sprite Pivot" : undoName);
            _profile.Pivot = pivot;
            WriteActiveSheetFromLegacy();
            _pivotSelected = true;
            _status = status ?? $"Pivot {_profile.Pivot.x:F3}, {_profile.Pivot.y:F3}";
            SaveDirty();
            Repaint();
        }

        FrameBoxDef ResolveCharacterColliderForPivotSnap()
        {
            if (_profile?.Hitboxes == null)
                return null;
            foreach (var box in _selectedColliders)
            {
                if (box != null && box.IsCharacter)
                    return box;
            }

            var clip = CurrentClip;
            if (clip != null)
            {
                foreach (var box in BoxesFor(clip, _selectedFrame))
                {
                    if (box != null && box.IsCharacter)
                        return box;
                }
            }

            for (int i = 0; i < _profile.Hitboxes.Count; i++)
            {
                var box = _profile.Hitboxes[i];
                if (box != null && box.IsCharacter)
                    return box;
            }
            return null;
        }

        static Vector2 ColliderCenterAsPivot(FrameBoxDef box)
        {
            Rect r = box.RectUV;
            // RectUV is top-left y-down in cell space; profile Pivot is bottom-left y-up.
            return new Vector2(
                r.x + r.width * 0.5f,
                1f - (r.y + r.height * 0.5f));
        }

        void SnapPivotToCharacterColliderCenter()
        {
            var box = ResolveCharacterColliderForPivotSnap();
            if (box == null)
            {
                PromptAddCharacterColliderForPivot(snapAfter: true);
                return;
            }
            SetProfilePivot(ColliderCenterAsPivot(box), "Snap Pivot to Character Collider",
                $"Pivot snapped to Character collider #{box.Id} center");
        }

        void PromptAddCharacterColliderForPivot(bool snapAfter)
        {
            if (_profile == null)
                return;
            bool create = EditorUtility.DisplayDialog("Add Character Collider", "No Character (body) collider was found on this profile.\n\nCreate a Character square collider now" + (snapAfter ? " and snap the pivot to its center?" : "?"), "Create", "Cancel");
            if (!create)
                return;
            CreateCharacterColliderForPivot(snapAfter);
        }

        void CreateCharacterColliderForPivot(bool snapAfter)
        {
            EnsureProfile();
            var clip = CurrentClip;
            string clipName = clip != null ? clip.Name : string.Empty;
            int frame = clip != null ? Mathf.Clamp(_selectedFrame, 0, Mathf.Max(0, clip.Frames.Length - 1)) : 0;

            RecordProfileUndo(snapAfter
                ? "Add Character Collider & Snap Pivot"
                : "Create Sprite Collider");
            var definition = new FrameBoxDef
            {
                ClipName = clipName,
                FrameIndex = frame,
                Id = (byte)_newHitboxId,
                Shape = SpriteColliderShape.Square,
                // Authoring UV is top-left y-down; ~body box in cell.
                RectUV = new Rect(0.2f, 0.15f, 0.6f, 0.7f),
                Lifetime = (byte)SpriteColliderLifetime.Character,
                Physics = _newColliderPhysics,
                IsTrigger = _newColliderIsTrigger,
            };
            definition.BindLifetime(clipName, frame);
            _profile.Hitboxes.Add(definition);
            ClearSocketSelection();
            _selectedColliders.Clear();
            _selectedColliders.Add(definition);
            FocusColliderInInspector(definition);
            if (snapAfter)
            {
                _profile.Pivot = ColliderCenterAsPivot(definition);
                WriteActiveSheetFromLegacy();
                _pivotSelected = true;
                // Keep collider selected for filter edits; pivot stays selected flag for UI.
                _status = $"Created Character collider #{definition.Id} and snapped pivot to its center";
            }
            else
            {
                _status = $"Created {definition.Shape} Character collider (Include/Exclude clips in details)";
            }
            if (!_showHitboxes)
                _showHitboxes = true;
            SaveDirty();
            Repaint();
        }

        void SnapPivotToOpaqueContent(bool bottomCenter)
        {
            if (!TryGetOpaqueContentPivot(bottomCenter, out Vector2 pivot))
            {
                _status = "Opaque content snap needs a readable sheet texture";
                Repaint();
                return;
            }
            SetProfilePivot(pivot,
                bottomCenter
                    ? "Snap Pivot to Opaque Bottom Center"
                    : "Snap Pivot to Opaque Center",
                bottomCenter
                    ? $"Pivot snapped to opaque bottom-center ({pivot.x:F3}, {pivot.y:F3})"
                    : $"Pivot snapped to opaque center ({pivot.x:F3}, {pivot.y:F3})");
        }

        bool TryGetOpaqueContentPivot(bool bottomCenter, out Vector2 pivot)
        {
            pivot = SpriteSheetProfile.DefaultPivot;
            var clip = CurrentClip;
            var def = clip != null
                ? (_profile.SheetForClip(clip) ?? _profile.SheetAt(_selectedSheet))
                : _profile.SheetAt(_selectedSheet);
            if (!TryEnsureSheetPixelCache(def) || _sheetPixels == null)
                return false;
            if (!TryGetActiveCellPixelRect(def, clip, out RectInt cellPx))
                return false;
            if (cellPx.width <= 0 || cellPx.height <= 0)
                return false;

            const byte alphaThreshold = SpriteSheetProfile.CroppedAlphaThreshold;
            int minX = cellPx.xMax, minY = cellPx.yMax, maxX = cellPx.xMin - 1, maxY = cellPx.yMin - 1;
            bool found = false;
            for (int y = cellPx.yMin; y < cellPx.yMax; y++)
            {
                int rowOff = y * _sheetPixelsWidth;
                for (int x = cellPx.xMin; x < cellPx.xMax; x++)
                {
                    if (_sheetPixels[rowOff + x].a > alphaThreshold)
                    {
                        found = true;
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (!found)
                return false;

            // GetPixels32: y=0 at texture bottom. Pivot is bottom-left in active cell.
            float nx = ((minX + maxX + 1) * 0.5f - cellPx.xMin) / cellPx.width;
            float nyCenter = ((minY + maxY + 1) * 0.5f - cellPx.yMin) / cellPx.height;
            float nyBottom = (minY - cellPx.yMin) / (float)cellPx.height;
            pivot = new Vector2(
                Mathf.Clamp01(nx),
                Mathf.Clamp01(bottomCenter ? nyBottom : nyCenter));
            return true;
        }

        bool TryGetActiveCellPixelRect(SpriteSheetDef def, SpriteClipDef clip, out RectInt pixelRect)
        {
            pixelRect = default;
            if (def == null || _sheetPixelsWidth <= 0 || _sheetPixelsHeight <= 0)
                return false;
            int cellIndex = 0;
            if (clip != null)
                cellIndex = CellIndexOf(clip, Mathf.Clamp(_selectedFrame, 0, Mathf.Max(0, clip.Frames.Length - 1)));
            if (SpriteSheetProfile.TryGetCroppedCellPixelRect(def, cellIndex, out pixelRect))
                return pixelRect.width > 0 && pixelRect.height > 0;

            int columns = def.Columns > 0 ? def.Columns : Mathf.Max(1, _profile.Columns);
            int rows = def.Rows > 0 ? def.Rows : Mathf.Max(1, _profile.Rows);
            var uv = SpriteSheetProfile.GetUniformCellUvRect(columns, rows, cellIndex);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(uv.x * _sheetPixelsWidth), 0, _sheetPixelsWidth);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(uv.y * _sheetPixelsHeight), 0, _sheetPixelsHeight);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((uv.x + uv.width) * _sheetPixelsWidth), 0, _sheetPixelsWidth);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((uv.y + uv.height) * _sheetPixelsHeight), 0, _sheetPixelsHeight);
            if (x1 <= x0) x1 = Mathf.Min(_sheetPixelsWidth, x0 + 1);
            if (y1 <= y0) y1 = Mathf.Min(_sheetPixelsHeight, y0 + 1);
            pixelRect = new RectInt(x0, y0, x1 - x0, y1 - y0);
            return pixelRect.width > 0 && pixelRect.height > 0;
        }

        void CopyProfilePivot()
        {
            if (_profile == null)
                return;
            _pivotClipboard = _profile.Pivot;
            _pivotClipboardValid = true;
            _status = $"Copied pivot {_pivotClipboard.x:F3}, {_pivotClipboard.y:F3}";
            Repaint();
        }

        void PasteProfilePivot()
        {
            if (!_pivotClipboardValid)
                return;
            SetProfilePivot(_pivotClipboard, "Paste Sprite Pivot",
                $"Pasted pivot {_pivotClipboard.x:F3}, {_pivotClipboard.y:F3}");
        }

        static Rect CenteredSquareRect(Vector2 center, Vector2 edge, Rect bounds, float minimumRadius)
        {
            // Free create: do not clamp to the cell frame. Hitboxes/hurtboxes often
            // extend past the sprite cell; transform/move already allowed that —
            // create must match so the click center is not pulled inward (offset).
            _ = bounds;
            float radius = Mathf.Max(Mathf.Abs(edge.x - center.x), Mathf.Abs(edge.y - center.y));
            radius = Mathf.Max(radius, minimumRadius);
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

        static int ColliderLifetimeToPopup(byte lifetime)
        {
            if (lifetime == (byte)SpriteColliderLifetime.Clip)
                return 1;
            if (lifetime == (byte)SpriteColliderLifetime.Character)
                return 2;
            return 0;
        }

        static byte ColliderLifetimeFromPopup(int index)
        {
            if (index == 1)
                return (byte)SpriteColliderLifetime.Clip;
            if (index == 2)
                return (byte)SpriteColliderLifetime.Character;
            return (byte)SpriteColliderLifetime.Frame;
        }

        static string ColliderLifetimeListLabel(FrameBoxDef box)
        {
            if (box == null)
                return "Collider";
            string life = box.IsCharacter ? "Body" : box.IsClip ? "Clip" : "Frame";
            return $"{life} {box.Shape}";
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

        static GUIContent EventGoToContent()
        {
            const string tooltip = "Go to the clip and frame this event marker lives on";
            var icon = EditorGUIUtility.IconContent("Search Icon");
            if (icon == null || icon.image == null)
                icon = EditorGUIUtility.IconContent("ViewToolZoom");
            if (icon != null && icon.image != null)
            {
                icon.tooltip = tooltip;
                return icon;
            }
            return new GUIContent("?", tooltip);
        }

        static GUIContent ColliderGoToContent(FrameBoxDef box)
        {
            string tooltip = box != null && box.IsClip
                ? "Go to the clip this collider lives on"
                : "Go to the clip and frame this collider lives on";
            var icon = EditorGUIUtility.IconContent("Search Icon");
            if (icon == null || icon.image == null)
                icon = EditorGUIUtility.IconContent("ViewToolZoom");
            if (icon != null && icon.image != null)
            {
                icon.tooltip = tooltip;
                return icon;
            }
            return new GUIContent("?", tooltip);
        }

        static GUIContent PivotLockContent(bool locked)
        {
            var icon = EditorGUIUtility.IconContent(locked ? "LockIcon-On" : "LockIcon");
            string tooltip = locked
                ? "Unlock pivot — allow select and drag in the preview (inspector only)"
                : "Lock pivot — prevent select and drag (inspector only; persists via EditorPrefs)";
            if (icon != null && icon.image != null)
            {
                icon.tooltip = tooltip;
                return icon;
            }
            return new GUIContent(locked ? "L" : "U", tooltip);
        }

        void SetPivotLocked(bool locked)
        {
            if (_pivotLocked == locked)
                return;
            _pivotLocked = locked;
            EditorPrefs.SetBool(PivotLockedPrefsKey, locked);
            if (locked)
            {
                _draggingPivot = false;
                _pivotSelected = false;
                if (GUIUtility.hotControl != 0)
                    GUIUtility.hotControl = 0;
            }
            _status = locked ? "Pivot locked" : "Pivot unlocked";
            Repaint();
        }

        static GUIContent ColliderLockContent(bool locked)
        {
            var icon = EditorGUIUtility.IconContent(locked ? "LockIcon-On" : "LockIcon");
            string tooltip = locked
                ? "Unlock this collider to select, transform, or delete it"
                : "Lock this collider against selection, transforms, and deletion";
            if (icon != null && icon.image != null)
            {
                icon.tooltip = tooltip;
                return icon;
            }
            return new GUIContent(locked ? "L" : "U", tooltip);
        }

        static void DrawColliderLockBadge(FrameBoxDef box, Rect cell)
        {
            Rect bounds = ColliderWorldAabb(box, cell);
            var badge = new Rect(bounds.xMax - 15f, bounds.yMin + 2f, 14f, 14f);
            GUI.Label(badge, ColliderLockContent(true));
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

        static bool IsColliderKnobHandle(ColliderHandleKind kind)
            => kind != ColliderHandleKind.None && kind != ColliderHandleKind.Body;

        /// <summary>
        /// True when the click should start transforming the current selection.
        /// Scale/rotate knobs always win. Body drag only if nothing else is under
        /// the cursor (so picking another overlapping collider works in one click).
        /// </summary>
        bool ShouldBeginSelectedColliderTransform(Rect cell, SpriteClipDef clip, int frame, Vector2 mouse)
        {
            var kind = HitSelectedColliderHandle(cell, mouse);
            if (kind == ColliderHandleKind.None)
                return false;
            if (IsColliderKnobHandle(kind))
                return true;

            // Body hit on the selection — defer if another collider is on top / tighter.
            FrameBoxDef under = FindColliderAt(clip, frame, cell, mouse);
            if (under == null)
                return true;
            return _selectedColliders.Contains(under);
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
                        if (!selected.Hidden && !selected.Locked &&
                            ColliderContains(selected, cell, evt.mousePosition))
                        {
                            box = selected;
                            break;
                        }
                    }
                }
                if (box == null || box.Locked)
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
            if (box == null || box.Locked)
                return;
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
                    if (selected.Locked)
                        continue;
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
            _selectedEventIndex = -1;
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

        void DrawPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, _panelStyle);
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
            _selectedEventIndex = -1;
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
            if (IsRenamingAnything())
                return true;
            if (!EditorGUIUtility.editingTextField)
                return false;
            string focused = GUI.GetNameOfFocusedControl();
            return focused == ClipRenameControl ||
                   focused == SheetRenameControl ||
                   focused == SocketNameRenameControl ||
                   focused == SocketIdRenameControl ||
                   focused == InventoryRenameControl ||
                   focused == EventRenameControl ||
                   (!string.IsNullOrEmpty(focused) && focused.StartsWith(StringFieldControlPrefix));
        }

        bool IsEditingAnyTextField()
            => IsRenamingAnything() || EditorGUIUtility.editingTextField;

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

            if ((evt.control || evt.command) && evt.keyCode == KeyCode.G)
            {
                if (IsEditingAnyTextField())
                    return;
                if (evt.shift)
                    UngroupSelectedSocketInventory();
                else
                    GroupSelectedSocketInventory();
                evt.Use();
                return;
            }

            if (evt.keyCode == KeyCode.F2)
            {
                if (IsRenamingAnything())
                    return;
                if (IsEditingStringTextField() && !string.IsNullOrEmpty(GUI.GetNameOfFocusedControl()) &&
                    GUI.GetNameOfFocusedControl().StartsWith(StringFieldControlPrefix))
                    return;
                if (TryBeginPreferredRename())
                {
                    evt.Use();
                    Repaint();
                }
                return;
            }

            if (evt.keyCode is KeyCode.Delete or KeyCode.Backspace)
            {
                if (IsEditingAnyTextField())
                    return;
                if (_showSocketTransformPanel || _showSocketInheritPanel || _showSheetCellPicker)
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
                SyncEventTypeSelection();
                if (_selectedEventTypeIndices.Count > 0)
                {
                    if (_selectedEventTypeIndices.Count > 1)
                    {
                        int n = _selectedEventTypeIndices.Count;
                        if (!EditorUtility.DisplayDialog(
                            "Delete Event Types",
                            $"Delete {n} selected event types?\nMarkers and socket triggers that use these IDs will also be removed. This cannot be undone except via Undo.",
                            "Delete", "Cancel"))
                        {
                            evt.Use();
                            return;
                        }
                    }
                    DeleteSelectedEventTypes();
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
                if (_showSheetCellPicker)
                {
                    CloseSheetCellPicker();
                    _status = "Sheet cell picker closed";
                    evt.Use();
                    Repaint();
                    return;
                }
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
            _selectedEventIndex = -1;
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


        const string ChangeClipFpsUndoName = "Change Clip FPS";
        const string ClipFpsTooltip =
            "Clip playback frame rate. Runtime Speed multiplies this.";

        bool ApplyClipFrameRate(SpriteClipDef clip, float newFrameRate)
        {
            if (clip == null)
                return false;
            float clamped = Mathf.Max(0.1f, newFrameRate);
            if (Mathf.Approximately(clip.FrameRate, clamped))
                return false;
            // Discrete complete-object undo + seal (same pattern as rename) so
            // PrepareInspectorUndo / other MouseDown records cannot swallow Ctrl+Z.
            SyncWorkingProfileToAsset();
            RecordDiscreteUndo(ChangeClipFpsUndoName);
            clip.FrameRate = clamped;
            SyncWorkingProfileToAsset();
            SaveDirty();
            SealUndoGroup();
            return true;
        }

        void DrawClipFpsField(Rect fieldRect, SpriteClipDef clip)
        {
            if (clip == null)
                return;
            EditorGUI.BeginChangeCheck();
            float fps = EditorGUI.FloatField(fieldRect,
                new GUIContent(string.Empty, ClipFpsTooltip), clip.FrameRate);
            if (EditorGUI.EndChangeCheck())
                ApplyClipFrameRate(clip, fps);
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

        void DrawSheetCellPickerOverlay()
        {
            if (!_showSheetCellPicker)
                return;
            var clip = CurrentClip;
            var def = clip != null
                ? (_profile.SheetForClip(clip) ?? _profile.SheetAt(_selectedSheet))
                : _profile.SheetAt(_selectedSheet);
            var tex = def?.Texture ?? _profile.Sheet;
            if (clip == null || tex == null)
            {
                CloseSheetCellPicker();
                return;
            }

            int columns = ClipSheetColumns(clip);
            int rows = ClipSheetRows(clip);
            float width = Mathf.Clamp(_sheetCellPickerRect.width, 420f, Mathf.Max(420f, position.width - 16f));
            float height = Mathf.Clamp(_sheetCellPickerRect.height, 360f, Mathf.Max(360f, position.height - 24f));
            float x = Mathf.Clamp(_sheetCellPickerRect.x, 8f, Mathf.Max(8f, position.width - width - 8f));
            float y = Mathf.Clamp(_sheetCellPickerRect.y, 8f, Mathf.Max(8f, position.height - height - 8f));
            _sheetCellPickerRect = new Rect(x, y, width, height);

            var evt = Event.current;
            var shade = new Rect(Vector2.zero, position.size);
            int controlId = GUIUtility.GetControlID(
                "SpriteSheetCellPickerOverlay".GetHashCode(), FocusType.Passive, shade);
            bool owns = GUIUtility.hotControl == controlId;

            if (evt.type == EventType.Repaint)
                EditorGUI.DrawRect(shade, new Color(0f, 0f, 0f, 0.45f));

            var title = new Rect(_sheetCellPickerRect.x, _sheetCellPickerRect.y, width, 26f);
            if (evt.GetTypeForControl(controlId) == EventType.MouseDown && evt.button == 0 &&
                title.Contains(evt.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                _sheetCellPickerDragging = true;
                _sheetCellPickerDragOffset = evt.mousePosition - _sheetCellPickerRect.position;
                GUI.FocusControl(null);
                evt.Use();
            }
            else if (evt.GetTypeForControl(controlId) == EventType.MouseDrag && owns && _sheetCellPickerDragging)
            {
                _sheetCellPickerRect.position = evt.mousePosition - _sheetCellPickerDragOffset;
                evt.Use();
                Repaint();
            }
            else if (evt.GetTypeForControl(controlId) == EventType.MouseUp && owns && _sheetCellPickerDragging)
            {
                GUIUtility.hotControl = 0;
                _sheetCellPickerDragging = false;
                evt.Use();
            }

            if (evt.type == EventType.KeyDown && evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
            {
                ConfirmSheetCellPicker();
                evt.Use();
                Repaint();
                return;
            }

            EditorGUI.DrawRect(_sheetCellPickerRect, new Color(0.09f, 0.11f, 0.14f, 0.98f));
            DrawBorder(_sheetCellPickerRect, AccentColor, 2f);
            EditorGUI.DrawRect(title, new Color(0.14f, 0.22f, 0.3f, 1f));
            GUI.Label(new Rect(title.x + 8f, title.y + 4f, title.width - 16f, 18f),
                "1×1 from texture", EditorStyles.boldLabel);

            var body = new Rect(
                _sheetCellPickerRect.x + 8f,
                _sheetCellPickerRect.y + 30f,
                _sheetCellPickerRect.width - 16f,
                _sheetCellPickerRect.height - 38f);
            const float footerH = 58f;
            var gridViewport = new Rect(body.x, body.y, body.width, Mathf.Max(80f, body.height - footerH - 4f));
            var footer = new Rect(body.x, gridViewport.yMax + 4f, body.width, footerH);

            float gap = 2f;
            float cellAspect = 1f;
            if (SpriteSheetProfile.TryGetCellPixels(def, out float srcW, out float srcH) && srcH > 0.01f)
                cellAspect = srcW / srcH;
            else
                cellAspect = (tex.width / (float)columns) / Mathf.Max(1f, tex.height / (float)rows);
            cellAspect = Mathf.Max(0.01f, cellAspect);
            float cellW = Mathf.Clamp((gridViewport.width - 18f - gap * (columns - 1)) / columns, 28f, 96f);
            float cellH = cellW / cellAspect;
            float contentW = columns * cellW + gap * (columns - 1);
            float contentH = rows * cellH + gap * (rows - 1);
            _sheetCellPickerScroll = GUI.BeginScrollView(gridViewport, _sheetCellPickerScroll,
                new Rect(0f, 0f, Mathf.Max(contentW, gridViewport.width - 18f), contentH));
            var clipRow = clip.Row;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    int cell = r * columns + c;
                    var cellRect = new Rect(c * (cellW + gap), r * (cellH + gap), cellW, cellH);
                    DrawCheckerboard(cellRect, 8f);
                    DrawCellTinted(tex, cell, FitAspectRect(cellRect, cellAspect), Color.white, columns, rows);
                    int order = _sheetCellPickerSelection.IndexOf(cell);
                    bool selected = order >= 0;
                    bool onClipRow = r == clipRow;
                    if (!onClipRow)
                        EditorGUI.DrawRect(cellRect, new Color(0f, 0f, 0f, 0.18f));
                    DrawBorder(cellRect, selected ? AccentColor : BorderColor, selected ? 2f : 1f);
                    if (selected)
                    {
                        var badge = new Rect(cellRect.x + 2f, cellRect.y + 2f, 18f, 14f);
                        EditorGUI.DrawRect(badge, AccentColor);
                        GUI.Label(badge, (order + 1).ToString(), EditorStyles.miniLabel);
                    }
                    if (evt.type == EventType.MouseDown && evt.button == 0 &&
                        cellRect.Contains(evt.mousePosition))
                    {
                        ApplySheetCellPickerClick(cell, columns * rows, evt);
                        evt.Use();
                        Repaint();
                    }
                }
            }
            GUI.EndScrollView();

            GUILayout.BeginArea(footer);
            GUILayout.Label(
                $"{_sheetCellPickerSelection.Count} selected   •   click = replace   •   Ctrl = toggle   •   Shift = range   •   Enter / OK adds after the current frame",
                _mutedStyle);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("This row", EditorStyles.miniButton, GUILayout.Width(72f)))
                {
                    SelectSheetPickerRow(clipRow, columns);
                    GUI.FocusControl(null);
                }
                if (GUILayout.Button("Occupied", EditorStyles.miniButton, GUILayout.Width(72f)))
                {
                    SelectSheetPickerOccupied(clip, columns, rows);
                    GUI.FocusControl(null);
                }
                if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(52f)))
                {
                    _sheetCellPickerSelection.Clear();
                    _sheetCellPickerAnchor = -1;
                    GUI.FocusControl(null);
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(72f)))
                {
                    CloseSheetCellPicker();
                    _status = "Sheet cell picker closed";
                }
                using (new EditorGUI.DisabledScope(_sheetCellPickerSelection.Count == 0))
                {
                    if (GUILayout.Button("OK", GUILayout.Width(72f)))
                        ConfirmSheetCellPicker();
                }
            }
            GUILayout.EndArea();

            EventType forControl = evt.GetTypeForControl(controlId);
            if (forControl == EventType.MouseDown && !_sheetCellPickerDragging)
            {
                if (!_sheetCellPickerRect.Contains(evt.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    GUI.FocusControl(null);
                    CloseSheetCellPicker();
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

        void ApplySheetCellPickerClick(int cell, int cellCount, Event evt)
        {
            cell = Mathf.Clamp(cell, 0, Mathf.Max(0, cellCount - 1));
            bool toggle = evt.control || evt.command;
            bool range = evt.shift && _sheetCellPickerAnchor >= 0;
            if (range)
            {
                int from = Mathf.Min(_sheetCellPickerAnchor, cell);
                int to = Mathf.Max(_sheetCellPickerAnchor, cell);
                if (!toggle)
                    _sheetCellPickerSelection.Clear();
                for (int i = from; i <= to; i++)
                {
                    if (!_sheetCellPickerSelection.Contains(i))
                        _sheetCellPickerSelection.Add(i);
                }
                return;
            }

            if (toggle)
            {
                int existing = _sheetCellPickerSelection.IndexOf(cell);
                if (existing >= 0)
                    _sheetCellPickerSelection.RemoveAt(existing);
                else
                    _sheetCellPickerSelection.Add(cell);
                _sheetCellPickerAnchor = cell;
                return;
            }

            _sheetCellPickerSelection.Clear();
            _sheetCellPickerSelection.Add(cell);
            _sheetCellPickerAnchor = cell;
        }

        void SelectSheetPickerRow(int row, int columns)
        {
            columns = Mathf.Max(1, columns);
            _sheetCellPickerSelection.Clear();
            for (int c = 0; c < columns; c++)
                _sheetCellPickerSelection.Add(row * columns + c);
            _sheetCellPickerAnchor = row * columns;
        }

        void SelectSheetPickerOccupied(SpriteClipDef clip, int columns, int rows)
        {
            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            TryEnsureSheetPixelCache(clip);
            _sheetCellPickerSelection.Clear();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    if (!IsSheetCellEmpty(c, r))
                        _sheetCellPickerSelection.Add(r * columns + c);
                }
            }
            _sheetCellPickerAnchor = _sheetCellPickerSelection.Count > 0
                ? _sheetCellPickerSelection[0]
                : -1;
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
    }
}

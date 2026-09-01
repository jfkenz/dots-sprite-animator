using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BallForge.Sprites.DOTS.Editor
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
            Marquee,
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

        const string PackageVersion = "0.7.0";
        const float ToolbarHeight = 48f;
        const float TimelineHeight = 226f;
        const float DefaultClipPanelWidth = 210f;
        const float DefaultInspectorPanelWidth = 340f;
        const float MinClipPanelWidth = 180f;
        const float MinPreviewPanelWidth = 220f;
        const float MinInspectorPanelWidth = 260f;
        const float Gap = 8f;
        const float PixelsPerSecond = 520f;
        const float TimelineDragMoveThreshold = 3f;
        const float DefaultPreviewSpeed = 1f;
        const float PivotHandleHitRadius = 14f;
        const float ColliderHandleSize = 8f;
        const float ColliderRotateHandleDistance = 26f;
        const float ColliderMinScreenHalf = 6f;
        const string ClipRenameControl = "BallForgeSpriteAnimator.ClipRename";
        const string StringFieldControlPrefix = "BallForgeSpriteAnimator.Text.";

        static readonly Color WindowColor = new(0.075f, 0.086f, 0.105f);
        static readonly Color PanelColor = new(0.105f, 0.12f, 0.145f);
        static readonly Color PanelAltColor = new(0.13f, 0.15f, 0.18f);
        static readonly Color BorderColor = new(0.22f, 0.25f, 0.3f);
        static readonly Color AccentColor = new(0.18f, 0.66f, 0.92f);
        static readonly Color EventColor = new(1f, 0.61f, 0.2f);
        static readonly Color TextMuted = new(0.58f, 0.64f, 0.72f);

        [SerializeField] SpriteSheetProfile _profile;
        [SerializeField] float _clipPanelWidth = DefaultClipPanelWidth;
        [SerializeField] float _inspectorPanelWidth = DefaultInspectorPanelWidth;
        [SerializeField] PreviewOffsetMode _previewOffsetMode = PreviewOffsetMode.Authored;
        ScriptableSpriteSheetProfile _asset;
        int _selectedClip;
        int _selectedFrame;
        readonly HashSet<int> _selectedFrames = new();
        int _selectedEventFrame = -1;
        int _newHitboxId = 1;
        ColliderCreationMode _colliderCreationMode = ColliderCreationMode.None;
        bool _continuousColliderPlacement;
        bool _socketPlacementArmed;
        string _selectedSocketName;
        bool _socketDeleteArmed;
        readonly HashSet<string> _selectedSockets = new();
        readonly List<string> _socketMoveNames = new();
        readonly List<Vector2> _socketMoveStarts = new();
        bool _socketMoveUndoRecorded;
        bool _draggingSocket;
        Vector2 _socketDragStart;

        bool _playing = false;
        bool _previewLoop = true;
        bool _showHitboxes = true;
        float _speed = 1f;
        [SerializeField] float _previewZoom = 1f;
        [SerializeField] Vector2 _previewPan = Vector2.zero;
        [SerializeField] bool _showPivot = true;
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
        bool _timelineMarqueeAdditive;
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
        bool _colliderMarqueeAdditive;
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

        GUIStyle _titleStyle;
        GUIStyle _sectionStyle;
        GUIStyle _mutedStyle;
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
        int _sheetPixelsId = -1;
        int _sheetPixelsWidth;
        int _sheetPixelsHeight;
        int _sheetPixelsColumns;
        int _sheetPixelsRows;
        bool[] _sheetCellEmpty;

        [MenuItem("Window/DOTS Sprite Animator")]
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
            _lastEditorTime = EditorApplication.timeSinceStartup;
        }

        void OnDisable()
        {
            EditorApplication.update -= TickPreview;
            Undo.undoRedoPerformed -= OnUndoRedo;
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
            if (!_playing || CurrentClip == null)
                return;

            _previewTime += delta * Mathf.Max(0.05f, _speed);
            var state = EvaluatePreview(CurrentClip, _previewTime);
            if (state.Ended && !_previewLoop)
                _playing = false;
            Repaint();
        }

        void OnGUI()
        {
            EnsureProfile();
            EnsureStyles();
            HandleGlobalShortcuts();
            if (Event.current.type == EventType.MouseDown)
                Focus();
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

            DrawClipBrowser(clipsRect);
            DrawPreview(previewRect);
            DrawInspector(inspectorRect);
            DrawPanelSplitter(leftSplitter, true, workRect.width);
            DrawPanelSplitter(rightSplitter, false, workRect.width);
            DrawTimeline(timelineRect, timelineControlId);
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
                _selectedClip = Mathf.Clamp(_selectedClip, 0, _profile.Clips.Count - 1);
                var clip = _profile.Clips[_selectedClip];
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
        }

        int LowestSelectedFrame()
        {
            int lowest = int.MaxValue;
            foreach (int index in _selectedFrames)
                if (index < lowest)
                    lowest = index;
            return lowest == int.MaxValue ? _selectedFrame : lowest;
        }

        void ApplyFrameModifierClick(int frame, bool additive, bool toggle)
        {
            frame = Mathf.Max(0, frame);
            if (!additive)
            {
                SelectOnlyFrame(frame);
                return;
            }

            if (toggle && _selectedFrames.Contains(frame))
            {
                if (_selectedFrames.Count > 1)
                {
                    _selectedFrames.Remove(frame);
                    if (!_selectedFrames.Contains(_selectedFrame))
                        _selectedFrame = LowestSelectedFrame();
                }
                return;
            }

            _selectedFrames.Add(frame);
            _selectedFrame = frame;
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
            if (GUI.Button(new Rect(x, 10f, 96f, 28f), new GUIContent("Load Profile…", "Load a ScriptableSpriteSheetProfile asset."), _transportStyle))
                LoadProfileFromPicker();
            x += 102f;

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
                        ? "Pause preview playback."
                        : "Play preview playback (Space)."),
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

            if (GUI.Button(new Rect(x, 11f, 46f, 26f),
                new GUIContent("Undo", "Undo (Ctrl/Cmd+Z)"), _transportStyle))
                Undo.PerformUndo();
            x += 50f;
            if (GUI.Button(new Rect(x, 11f, 46f, 26f),
                new GUIContent("Redo", "Redo (Ctrl/Cmd+Shift+Z or Ctrl+Y)"), _transportStyle))
                Undo.PerformRedo();
            x += 52f;

            var validateRect = new Rect(rect.xMax - 266f, 10f, 52f, 28f);
            if (GUI.Button(validateRect, new GUIContent("Check", "Validate package dependencies and shader setup."), _transportStyle))
                SpriteAnimatorToolsMenu.ValidateInstallation();

            var helpRect = new Rect(rect.xMax - 210f, 10f, 48f, 28f);
            if (GUI.Button(helpRect, new GUIContent("Help", "Open DOTS Sprite Animator quick start docs."), _transportStyle))
                SpriteAnimatorToolsMenu.OpenHelp();

            var saveRect = new Rect(rect.xMax - 154f, 10f, 140f, 28f);
            using (new EditorGUI.DisabledScope(_profile.Sheet == null))
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
            EnsureProfile();
            _selectedClip = 0;
            SelectOnlyFrame(0);
            _selectedEventFrame = -1;
            _selectedOnionFrame = -1;
            _previewTime = 0f;
            _playing = false;
            ClearColliderSelection();
            _status = "Created new profile";
            Repaint();
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
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 20f), "CLIPS", _sectionStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 31f, rect.width - 24f, 16f),
                $"{_profile.Clips.Count} animation clips", _mutedStyle);

            var listRect = new Rect(rect.x + 8f, rect.y + 54f, rect.width - 16f, rect.height - 100f);
            float contentHeight = Mathf.Max(listRect.height, _profile.Clips.Count * 46f + 6f);
            _clipScroll = GUI.BeginScrollView(listRect, _clipScroll,
                new Rect(0f, 0f, listRect.width - 15f, contentHeight));

            var input = Event.current;
            if (_renamingClip >= 0 && input.type == EventType.KeyDown &&
                GUI.GetNameOfFocusedControl() == ClipRenameControl)
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
            else if (_renamingClip < 0 && input.type == EventType.KeyDown && input.keyCode == KeyCode.F2 &&
                     CurrentClip != null && !IsEditingStringTextField())
            {
                BeginClipRename(_selectedClip);
                input.Use();
            }

            if (_renamingClip >= 0 && input.type == EventType.MouseDown)
            {
                var activeItemRect = new Rect(2f, 2f + _renamingClip * 46f, listRect.width - 21f, 40f);
                var activeNameRect = new Rect(activeItemRect.x + 10f, activeItemRect.y + 4f,
                    activeItemRect.width - 20f, 19f);
                if (!activeNameRect.Contains(input.mousePosition))
                    CommitClipRename();
            }

            int deleteClipIndex = -1;
            for (int i = 0; i < _profile.Clips.Count; i++)
            {
                var clip = _profile.Clips[i];
                clip.EnsureFrameData();
                var itemRect = new Rect(2f, 2f + i * 46f, listRect.width - 21f, 40f);
                var deleteRect = new Rect(itemRect.xMax - 27f, itemRect.y + 8f, 21f, 22f);
                var selectRect = new Rect(itemRect.x, itemRect.y, itemRect.width - 31f, itemRect.height);
                var nameRect = new Rect(itemRect.x + 10f, itemRect.y + 4f, itemRect.width - 48f, 19f);

                bool isRenaming = i == _renamingClip;
                if (isRenaming)
                {
                    GUI.Box(itemRect, GUIContent.none, _clipSelectedStyle);
                    GUI.SetNextControlName(ClipRenameControl);
                    _renameClipValue = GUI.TextField(nameRect, _renameClipValue, EditorStyles.boldLabel);
                    if (_focusClipRename)
                    {
                        EditorGUI.FocusTextInControl(ClipRenameControl);
                        _focusClipRename = false;
                    }
                }
                else
                {
                    GUI.Box(itemRect, GUIContent.none,
                        i == _selectedClip ? _clipSelectedStyle : _clipStyle);
                    if (input.type == EventType.MouseDown && input.button == 0 &&
                        selectRect.Contains(input.mousePosition) &&
                        !deleteRect.Contains(input.mousePosition))
                    {
                        SelectClipCard(i);
                        input.Use();
                    }
                    string clipName = string.IsNullOrWhiteSpace(clip.Name) ? $"Clip {i + 1}" : clip.Name;
                    GUI.Label(nameRect, new GUIContent(clipName, "Select this clip. Press F2 to rename."),
                        EditorStyles.boldLabel);
                }
                GUI.Label(new Rect(itemRect.x + 10f, itemRect.y + 22f, itemRect.width - 48f, 14f),
                    $"{clip.Frames.Length} frames   {clip.FrameRate:F1} fps", _mutedStyle);
                if (GUI.Button(deleteRect, new GUIContent("×", $"Delete {clip.Name}. Undo is supported."),
                    EditorStyles.miniButton))
                {
                    deleteClipIndex = i;
                    break;
                }
            }
            GUI.EndScrollView();

            if (deleteClipIndex >= 0)
                DeleteClipAt(deleteClipIndex);

            float y = rect.yMax - 38f;
            if (GUI.Button(new Rect(rect.x + 10f, y, 62f, 26f), "+ Clip", _transportStyle))
            {
                CommitClipRename();
                AddClip();
            }
            using (new EditorGUI.DisabledScope(CurrentClip == null))
            {
                if (GUI.Button(new Rect(rect.x + 77f, y, 68f, 26f), "Duplicate", _transportStyle))
                {
                    CommitClipRename();
                    DuplicateClip();
                }
                if (GUI.Button(new Rect(rect.x + 150f, y, 50f, 26f), "Delete", _transportStyle))
                {
                    CancelClipRename();
                    DeleteClip();
                }
            }
        }

        void SelectClipCard(int index)
        {
            if (_profile?.Clips == null || index < 0 || index >= _profile.Clips.Count)
                return;
            if (_renamingClip >= 0 && _renamingClip != index)
                CancelClipRename();
            if (_selectedClip != index)
            {
                _selectedOnionFrame = -1;
                ClearColliderSelection();
                _selectedEventFrame = -1;
            }
            _selectedClip = index;
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

            _selectedClip = clipIndex;
            SelectOnlyFrame(0);
            ClearColliderSelection();
            _selectedEventFrame = -1;
            _selectedOnionFrame = -1;
            _previewTime = 0f;
            _playing = false;
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
            string offsetModeLabel = _previewOffsetMode == PreviewOffsetMode.Authored
                ? "View: Offsets"
                : "View: Centered";
            if (GUI.Button(offsetModeRect, new GUIContent(offsetModeLabel,
                    "Toggle between authored per-frame playback offsets and centered source cells."),
                EditorStyles.miniButton))
            {
                Undo.RecordObject(this, "Change Sprite Offset Preview");
                _previewOffsetMode = _previewOffsetMode == PreviewOffsetMode.Authored
                    ? PreviewOffsetMode.Centered
                    : PreviewOffsetMode.Authored;
                _status = _previewOffsetMode == PreviewOffsetMode.Authored
                    ? "Preview applies authored frame offsets"
                    : "Preview centers the active frame";
            }
            var clip = CurrentClip;
            var state = EvaluatePreview(clip, _previewTime);
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
            DrawCheckerboard(canvas, 18f);
            EditorGUI.DrawRect(new Rect(canvas.x, canvas.y, canvas.width, 1f), BorderColor);

            if (_profile.Sheet == null || clip == null)
            {
                GUI.Label(canvas, "Drop a sprite sheet in the Inspector", _frameLabelStyle);
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
            DrawCell(_profile.Sheet, CellIndexOf(clip, state.Frame), activeSpriteRect, 1f);

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
                if (_draggingColliderTransform)
                    HandleColliderTransformInput(previewControlId, cell, clip, state.Frame);
                else if (_draggingPivot)
                    HandlePivotInput(previewControlId, cell);
                else if (_draggingSocket)
                    HandleSocketManipulationInput(previewControlId, cell, clip, state.Frame);
                else if (_showHitboxes && Event.current.type == EventType.MouseDown &&
                         HitSelectedColliderHandle(cell, Event.current.mousePosition) != ColliderHandleKind.None)
                    HandleColliderTransformInput(previewControlId, cell, clip, state.Frame);
                else if (Event.current.type == EventType.MouseDown &&
                         FindSocketAt(clip, state.Frame, cell, Event.current.mousePosition) != null)
                    HandleSocketManipulationInput(previewControlId, cell, clip, state.Frame);
                else if (_showPivot && Event.current.type == EventType.MouseDown &&
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
                (evt.button == 2 || (evt.button == 0 && evt.alt)) &&
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
            var area = new Rect(rect.x + 9f, rect.y + 38f, rect.width - 18f, rect.height - 48f);
            int colliderRows = CurrentClip == null ? 0 : CurrentFrameColliders(CurrentClip, _selectedFrame).Count;
            var inspectorContent = new Rect(0f, 0f, area.width - 15f,
                Mathf.Max(area.height, 1640f + colliderRows * 26f));
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
            var newSheet = (Texture2D)EditorGUILayout.ObjectField("Texture", _profile.Sheet, typeof(Texture2D), false);
            if (newSheet != _profile.Sheet)
            {
                _profile.Sheet = newSheet;
                if (newSheet != null)
                    TryLoadExistingAsset();
            }
            DrawSheetTextureInfo();
            _profile.Columns = Mathf.Max(1, EditorGUILayout.IntField("Columns", _profile.Columns));
            _profile.Rows = Mathf.Max(1, EditorGUILayout.IntField("Rows", _profile.Rows));
            using (new EditorGUI.DisabledScope(
                _profile.Columns == SpriteSheetProfile.DefaultColumns &&
                _profile.Rows == SpriteSheetProfile.DefaultRows))
            {
                if (GUILayout.Button(new GUIContent("Reset Grid to 4 × 4",
                    "Reset the sheet grid to 4 columns by 4 rows.")))
                {
                    RecordProfileUndo("Reset Sprite Sheet Grid");
                    _profile.Columns = SpriteSheetProfile.DefaultColumns;
                    _profile.Rows = SpriteSheetProfile.DefaultRows;
                    _status = "Reset sheet grid to 4 x 4";
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _profile.PixelsPerUnit = Mathf.Max(0.01f,
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
                    Undo.RecordObject(this, "Toggle Show Pivot");
                    _showPivot = nextShowPivot;
                    if (!_showPivot)
                    {
                        _draggingPivot = false;
                        _pivotSelected = false;
                    }
                }
            }
            if (GUILayout.Button("Auto-detect transparent grid"))
                AutoDetect();

            GUILayout.Space(9f);
            SectionLabel("TIMELINE INPUT");
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
            EditorGUILayout.HelpBox(
                "Circle is the default thumbnail hit target. Drag a thumbnail to reorder; drag empty track space to pan; drag the ruler or red playhead to scrub.",
                MessageType.None);

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
                    duration = Mathf.Max(0.001f, EditorGUILayout.FloatField("Duration (sec)", duration));
                    clip.FrameDurationScales[_selectedFrame] = duration * clip.FrameRate;
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
                        Undo.RecordObject(this, "Change Sprite Offset Preview");
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
                            "Choose a shape to create, or click existing colliders to select them. Drag empty preview space for marquee selection.",
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
                SaveDirty();
            GUILayout.EndArea();
            GUI.EndScrollView();
        }

        void DrawTimeline(Rect rect, int controlId)
        {
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, 140f, 20f), "TIMELINE", _sectionStyle);
            var clip = CurrentClip;
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
            const float deleteEmptyWidth = 148f;
            var deleteEmptyRect = new Rect(rect.xMax - deleteEmptyWidth - 8f, rect.y + 7f, deleteEmptyWidth, 20f);
            GUI.Label(new Rect(rect.x + 105f, rect.y + 10f,
                    Mathf.Max(40f, deleteEmptyRect.x - rect.x - 113f), 16f),
                $"{clip.Frames.Length} frames   •   {total:F3}s   •   drag = marquee   •   Alt+drag image = reorder   •   frame edge = duration   •   right-click lane = event{markerSelection}",
                _mutedStyle);
            int emptyFrameCount = CountEmptyFrames(clip);
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
            var content = new Rect(0f, 0f, contentWidth, 172f);
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
                    TimelineDragMode.ResizeFrame or TimelineDragMode.Event)
                playheadX = Mathf.Max(48f, _timelineDragContentMouse.x);

            DrawRuler(contentWidth, total, pixelsPerSecond);
            EditorGUI.DrawRect(new Rect(0f, 27f, contentWidth, 27f), new Color(0.08f, 0.095f, 0.12f));
            GUI.Label(new Rect(6f, 31f, 48f, 18f), "EVENT", _mutedStyle);
            EditorGUIUtility.AddCursorRect(new Rect(0f, 0f, contentWidth, 27f), MouseCursor.SlideArrow);
            EditorGUIUtility.AddCursorRect(new Rect(0f, 27f, contentWidth, 27f), MouseCursor.SlideArrow);
            EditorGUIUtility.AddCursorRect(new Rect(0f, 54f, contentWidth, 118f), MouseCursor.Arrow);

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
                EditorGUI.DrawRect(new Rect(markerX - 0.5f, 27f, 1f, 135f), guideColor);
                if (i == _selectedEventFrame)
                    DrawDiamond(new Vector2(markerX, 40f), 9f, Color.white);
                DrawDiamond(new Vector2(markerX, 40f), 6f, markerColor);
                GUI.Label(new Rect(markerX + 8f, 29f, 76f, 16f), $"{markerTime:F3}s", _mutedStyle);
                EditorGUIUtility.AddCursorRect(
                    new Rect(markerX - 10f, 30f, 20f, 20f), MouseCursor.MoveArrow);
            }

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
                var thumbArea = new Rect(card.x + 7f, 83f, card.width - 14f, 62f);
                DrawCheckerboard(thumbArea, 9f);
                DrawCell(_profile.Sheet, CellIndexOf(clip, i), thumb, 1f);
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

            EditorGUI.DrawRect(new Rect(playheadX, 2f, 2f, 160f), new Color(1f, 0.28f, 0.3f));
            DrawTriangle(new Vector2(playheadX + 1f, 2f), 6f, new Color(1f, 0.28f, 0.3f));

            if (_timelineDragMode == TimelineDragMode.Reorder && _reorderMoved && _dragFrameIndex >= 0)
            {
                float insertionX = DropSlotX(_dropFrameSlot, cards);
                EditorGUI.DrawRect(new Rect(insertionX - 2f, 54f, 4f, 112f), AccentColor);
                DrawTriangle(new Vector2(insertionX, 53f), 7f, AccentColor);

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
                DrawCell(_profile.Sheet, CellIndexOf(clip, _dragFrameIndex), ghostThumb, 0.9f);
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
                cards[i] = new Rect(x, 60f, width, 102f);
                thumbnails[i] = TimelineSpriteRect(new Rect(x + 7f, 83f, width - 14f, 62f));
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
                            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
                            {
                                RecordProfileUndo("Resize Sprite Animation Frame");
                                _timelineResizeCommitted = true;
                            }
                            else
                            {
                                ConvertTimelineResizeToMarquee(contentMouse, cards,
                                    evt.shift || evt.control || evt.command);
                                AutoScrollTimelineAtScreenEdge(screenMouse, viewportScreen, maxScroll);
                                break;
                            }
                        }

                        float deltaSeconds = delta.x / Mathf.Max(1f, _resizePixelsPerSecond);
                        float duration = Mathf.Max(0.02f, _resizeStartDuration + deltaSeconds);
                        clip.FrameDurationScales[_resizeFrameIndex] = duration * clip.FrameRate;
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

        void ConvertTimelineResizeToMarquee(Vector2 contentMouse, Rect[] cards, bool additive)
        {
            _resizeFrameIndex = -1;
            _timelineResizeCommitted = false;
            _timelineDragMode = TimelineDragMode.Marquee;
            _timelineMarqueeStart = _timelineDragStartContent;
            _timelineMarqueeMoved = true;
            _timelineMarqueeAdditive = additive;
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
                    SaveDirty();
                else if (_timelineDragMode == TimelineDragMode.Event && _eventDragMoved)
                    CommitEventMove(clip, _dragEventSourceFrame, _dragEventId,
                        _dragEventAuthoredTime);
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
                mouse.y <= 54f && mouse.x >= 48f)
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
                _timelineScroll.x = Mathf.Clamp(_timelineScroll.x + evt.delta.y * 32f, 0f, maxScroll);
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.MouseDown && (evt.button == 0 || evt.button == 2))
            {
                if (_timelineDragMode != TimelineDragMode.None)
                    CommitTimelineDrag(clip, mouse);

                ReleaseShortcutKeyboardFocus();
                if (evt.button == 0 && mouse.y >= 27f && mouse.y <= 54f)
                {
                    _selectedEventFrame = -1;
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

                if (evt.button == 0 && mouse.y >= 27f && mouse.y <= 54f)
                {
                    BeginTimelineDrag(controlId, TimelineDragMode.Scrub, mouse);
                    ScrubTimeline(clip, mouse.x, total, pixelsPerSecond);
                    evt.Use();
                    Repaint();
                    return;
                }

                if (evt.button == 0)
                {
                    for (int i = thumbnails.Length - 1; i >= 0; i--)
                    {
                        if (!evt.alt || !ThumbnailContains(thumbnails[i], mouse)) continue;
                        BeginTimelineDrag(controlId, TimelineDragMode.Reorder, mouse);
                        _dragFrameIndex = i;
                        _dropFrameSlot = i;
                        SelectOnlyFrame(i);
                        ClearColliderSelection();
                        _selectedEventFrame = -1;
                        _socketDeleteArmed = false;
                        _previewTime = PreviewTimeForAuthoredTime(clip, frameTimes[i]);
                        _reorderMoved = false;
                        evt.Use();
                        Repaint();
                        return;
                    }

                    if (!evt.alt)
                    {
                        for (int i = cards.Length - 1; i >= 0; i--)
                        {
                            if (!FrameResizeHandle(cards[i]).Contains(mouse)) continue;
                            BeginTimelineDrag(controlId, TimelineDragMode.ResizeFrame, mouse);
                            _resizeFrameIndex = i;
                            _resizeStartDuration = durations[i];
                            _resizePixelsPerSecond = pixelsPerSecond;
                            _timelineResizeCommitted = false;
                            SelectOnlyFrame(i);
                            _previewTime = PreviewTimeForAuthoredTime(clip, frameTimes[i]);
                            ClearColliderSelection();
                            _selectedEventFrame = -1;
                            _socketDeleteArmed = false;
                            evt.Use();
                            Repaint();
                            return;
                        }
                    }

                    if (mouse.y > 54f)
                    {
                        bool additive = evt.shift || evt.control || evt.command;
                        int card = FrameCardAt(cards, mouse);
                        if (card >= 0)
                        {
                            ApplyFrameModifierClick(card, additive, toggle: evt.control || evt.command);
                            _previewTime = PreviewTimeForAuthoredTime(clip, frameTimes[card]);
                        }
                        BeginTimelineMarquee(controlId, mouse, additive);
                        ClearColliderSelection();
                        _selectedEventFrame = -1;
                        _socketDeleteArmed = false;
                        evt.Use();
                        Repaint();
                        return;
                    }
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
            _panMoved = false;
            _panClickPlacesPlayhead = false;
            _timelineMarqueeMoved = false;
            _timelineMarqueeAdditive = false;
            _timelineMarqueeRect = default;
            _timelineMarqueeBaseline.Clear();
        }

        void BeginTimelineMarquee(int controlId, Vector2 contentMouse, bool additive)
        {
            BeginTimelineDrag(controlId, TimelineDragMode.Marquee, contentMouse);
            _timelineMarqueeStart = contentMouse;
            _timelineMarqueeRect = new Rect(contentMouse, Vector2.zero);
            _timelineMarqueeMoved = false;
            _timelineMarqueeAdditive = additive;
            _timelineMarqueeBaseline.Clear();
            foreach (int index in _selectedFrames)
                _timelineMarqueeBaseline.Add(index);
        }

        static int FrameCardAt(Rect[] cards, Vector2 point)
        {
            for (int i = cards.Length - 1; i >= 0; i--)
                if (cards[i].Contains(point))
                    return i;
            return -1;
        }

        void ApplyTimelineMarqueeSelection(Rect[] cards)
        {
            _selectedFrames.Clear();
            if (_timelineMarqueeAdditive)
            {
                foreach (int index in _timelineMarqueeBaseline)
                    _selectedFrames.Add(index);
            }

            for (int i = 0; i < cards.Length; i++)
            {
                if (cards[i].Overlaps(_timelineMarqueeRect))
                    _selectedFrames.Add(i);
            }

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
            => new(card.xMax - 6f, card.y, 6f, card.height);

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

        bool HandlePreviewObjectSelectionInput(int controlId, Rect cell, Rect content, SpriteClipDef clip, int frame,
                                          List<OnionGhostLayout> ghosts)
        {
            var evt = Event.current;
            bool ownsDrag = GUIUtility.hotControl == controlId && _colliderMarqueePending;

            if (evt.type == EventType.MouseDrag && ownsDrag)
            {
                if (!_draggingColliderMarquee &&
                    Vector2.Distance(_colliderMarqueeStart, evt.mousePosition) >= 4f)
                    _draggingColliderMarquee = true;
                if (_draggingColliderMarquee)
                {
                    _colliderMarqueeRect = RectFromPoints(_colliderMarqueeStart, evt.mousePosition);
                    SelectPreviewObjectsInMarquee(clip, frame, cell, _colliderMarqueeRect,
                        _colliderMarqueeAdditive);
                }
                evt.Use();
                Repaint();
                return true;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0 && ownsDrag)
            {
                if (_draggingColliderMarquee)
                {
                    SelectPreviewObjectsInMarquee(clip, frame, cell, _colliderMarqueeRect,
                        _colliderMarqueeAdditive);
                }
                else
                {
                    if (!_colliderMarqueeAdditive)
                        ClearPreviewObjectSelection();
                    SelectOnionAtPoint(clip, ghosts, evt.mousePosition);
                }

                EndColliderMarquee(controlId);
                evt.Use();
                Repaint();
                return true;
            }

            // Marquee may start anywhere on the preview canvas, including empty
            // checkerboard left/right of the fitted sprite cell.
            if (evt.type != EventType.MouseDown || !content.Contains(evt.mousePosition))
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
                    bool additive = evt.shift || evt.control || evt.command;
                    SelectCollider(found, evt.shift, evt.control || evt.command);
                    if (!additive)
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
            _colliderMarqueeAdditive = evt.shift || evt.control || evt.command;
            _colliderMarqueeStart = evt.mousePosition;
            _colliderMarqueeRect = new Rect(evt.mousePosition, Vector2.zero);
            CapturePreviewMarqueeBaseline();
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

        void SelectCollider(FrameBoxDef box, bool additive, bool toggle)
        {
            if (!additive && !toggle)
                ClearColliderSelection();
            if (toggle && _selectedColliders.Contains(box))
                _selectedColliders.Remove(box);
            else
                _selectedColliders.Add(box);
            _status = PreviewSelectionStatus();
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

        void SelectPreviewObjectsInMarquee(SpriteClipDef clip, int frame, Rect cell, Rect marquee, bool additive)
        {
            _selectedColliders.Clear();
            _selectedSockets.Clear();
            if (additive)
            {
                foreach (var box in _previewMarqueeColliderBaseline)
                    _selectedColliders.Add(box);
                foreach (string name in _previewMarqueeSocketBaseline)
                    _selectedSockets.Add(name);
            }

            if (_showHitboxes)
            {
                foreach (var box in BoxesFor(clip, frame))
                {
                    if (box.Hidden)
                        continue;
                    if (marquee.Overlaps(ColliderWorldAabb(box, cell), true))
                        _selectedColliders.Add(box);
                }
            }

            if (clip?.Sockets != null)
            {
                var names = SpriteSocketKeys.UniqueNamesInOrder(clip.Sockets);
                for (int i = 0; i < names.Count; i++)
                {
                    string name = names[i];
                    if (!SpriteSocketKeys.TryGetPose(clip.Sockets, name, frame,
                            out var position, out _, out _))
                        continue;
                    if (marquee.Overlaps(SocketWorldAabb(position, cell, name), true))
                        _selectedSockets.Add(SpriteSocketKeys.CanonicalName(name));
                }
            }

            _selectedEventFrame = -1;
            _selectedOnionFrame = -1;
            SyncSocketPrimaryFromSelection();
            _status = PreviewSelectionStatus("Marquee selected");
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
            if (GUIUtility.hotControl == controlId)
                GUIUtility.hotControl = 0;
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
            SectionLabel($"SOCKETS — FRAME {_selectedFrame + 1}");
            clip.Sockets ??= new List<FrameSocketDef>();
            PruneSocketSelection(clip);
            var names = SpriteSocketKeys.UniqueNamesInOrder(clip.Sockets);
            GUILayout.Label(
                $"{names.Count} socket{(names.Count == 1 ? string.Empty : "s")} • {_selectedSockets.Count} selected",
                _mutedStyle);

            Color previous = GUI.backgroundColor;
            if (_socketPlacementArmed)
                GUI.backgroundColor = new Color(0.18f, 0.55f, 0.82f, 1f);
            if (GUILayout.Button(_socketPlacementArmed
                    ? "Click Preview to Place…"
                    : "Add Socket"))
            {
                if (_socketPlacementArmed)
                    CancelSocketPlacement("Socket placement cancelled");
                else
                    ArmSocketPlacement();
            }
            GUI.backgroundColor = previous;

            if (_socketPlacementArmed)
            {
                EditorGUILayout.HelpBox("Click on the frame to place a socket. Escape or right-click cancels.",
                    MessageType.Info);
            }

            if (names.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Add Socket, then click the preview to place a named attach point.",
                    MessageType.None);
                return;
            }

            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                bool selected = IsSocketSelected(name);
                SpriteSocketKeys.TryGetPose(clip.Sockets, name, _selectedFrame,
                    out var pose, out var angle, out bool onFrame);
                Color swatch = SpriteSocketKeys.ColorForIndex(i);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var chip = GUILayoutUtility.GetRect(12f, 12f, GUILayout.Width(16f), GUILayout.Height(18f));
                        EditorGUI.DrawRect(new Rect(chip.x, chip.y + 4f, 12f, 12f), swatch);
                        string rowLabel = onFrame ? $"{i}:{name}" : $"{i}:{name}  (other frame)";
                        if (GUILayout.Button(rowLabel, selected ? EditorStyles.miniButtonMid : EditorStyles.miniButton))
                        {
                            var click = Event.current;
                            SelectPreviewSocket(name, click.shift,
                                click.control || click.command);
                        }
                    }

                    if (!selected || !SpriteSocketKeys.NamesEqual(name, _selectedSocketName))
                        continue;

                    if (!onFrame)
                        GUILayout.Label("No key on this frame yet. Drag or edit to add one.", _mutedStyle);

                    string nextName = DrawStringTextField("Name", name, "SocketName");
                    if (!SpriteSocketKeys.NamesEqual(nextName, name))
                    {
                        SpriteSocketKeys.RenameIdentity(clip.Sockets, name, nextName);
                        _selectedSocketName = SpriteSocketKeys.CanonicalName(nextName);
                        name = _selectedSocketName;
                    }

                    EditorGUI.BeginChangeCheck();
                    float offsetX = EditorGUILayout.FloatField("Offset X (px)", pose.x);
                    float offsetY = EditorGUILayout.FloatField("Offset Y (px)", pose.y);
                    float nextAngle = EditorGUILayout.FloatField("Angle (deg)", angle);
                    if (EditorGUI.EndChangeCheck())
                    {
                        var key = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, name, _selectedFrame);
                        key.LocalPosition = new Vector2(offsetX, offsetY);
                        key.LocalAngle = nextAngle;
                        _status = $"Socket {name}  ({offsetX:0.##}, {offsetY:0.##})  {nextAngle:0.##}°";
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
                            _status = $"Cleared {name} offset on frame {_selectedFrame + 1}";
                            SaveDirty();
                        }
                        if (GUILayout.Button(new GUIContent("Delete",
                                "Delete this socket identity from every frame.")))
                        {
                            RecordProfileUndo("Delete Sprite Socket");
                            SpriteSocketKeys.DeleteIdentity(clip.Sockets, name);
                            _status = $"Deleted socket {name}";
                            _selectedSockets.Remove(SpriteSocketKeys.CanonicalName(name));
                            if (SpriteSocketKeys.NamesEqual(_selectedSocketName, name))
                                _selectedSocketName = null;
                            SyncSocketPrimaryFromSelection();
                            _draggingSocket = false;
                            SaveDirty();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }

        void ArmSocketPlacement()
        {
            CancelColliderCreation(null);
            _socketPlacementArmed = true;
            _draggingSocket = false;
            _status = "Socket tool armed — click the preview to place";
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
            _socketMoveNames.Clear();
            _socketMoveStarts.Clear();
        }

        void ClearSocketSelection()
        {
            _selectedSockets.Clear();
            _selectedSocketName = null;
            _socketDeleteArmed = false;
        }

        void ClearPreviewObjectSelection()
        {
            ClearColliderSelection();
            ClearSocketSelection();
        }

        bool IsSocketSelected(string name)
            => !string.IsNullOrEmpty(name) &&
               _selectedSockets.Contains(SpriteSocketKeys.CanonicalName(name));

        void SyncSocketPrimaryFromSelection()
        {
            if (_selectedSockets.Count == 0)
            {
                _selectedSocketName = null;
                _socketDeleteArmed = false;
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
            _socketDeleteArmed = true;
        }

        void SelectPreviewSocket(string name, bool additive, bool toggle)
        {
            name = SpriteSocketKeys.CanonicalName(name);
            if (!additive && !toggle)
                ClearColliderSelection();
            if (toggle && _selectedSockets.Contains(name))
                _selectedSockets.Remove(name);
            else
                _selectedSockets.Add(name);
            _selectedSocketName = _selectedSockets.Contains(name) ? name : null;
            SyncSocketPrimaryFromSelection();
            _selectedEventFrame = -1;
            _selectedOnionFrame = -1;
            _status = PreviewSelectionStatus();
        }

        void PruneSocketSelection(SpriteClipDef clip)
        {
            if (clip?.Sockets == null)
            {
                ClearSocketSelection();
                return;
            }

            _selectedSockets.RemoveWhere(name => SpriteSocketKeys.IdentityIndex(clip.Sockets, name) < 0);
            if (!string.IsNullOrEmpty(_selectedSocketName) &&
                SpriteSocketKeys.IdentityIndex(clip.Sockets, _selectedSocketName) < 0)
                _selectedSocketName = null;
            SyncSocketPrimaryFromSelection();
            if (_selectedSockets.Count == 0)
                _draggingSocket = false;
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
            clip.Sockets ??= new List<FrameSocketDef>();
            var names = SpriteSocketKeys.UniqueNamesInOrder(clip.Sockets);
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                bool selected = IsSocketSelected(name);
                if (!SpriteSocketKeys.TryGetPose(clip.Sockets, name, frame,
                        out var position, out var angle, out bool onFrame))
                    continue;
                DrawSocketGizmo(cell, position, angle, $"{i}:{name}",
                    SpriteSocketKeys.ColorForIndex(i), selected, !onFrame);
            }
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
            if (selected)
            {
                Handles.color = Color.white;
                Handles.DrawWireDisc(center, Vector3.forward, radius + 3f);
            }
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

        Vector2 ScreenToSocketLocal(Vector2 screen, Rect cell)
        {
            return ScreenToSourcePixelDelta(screen - PivotScreen(cell), cell);
        }

        string FindSocketAt(SpriteClipDef clip, int frame, Rect cell, Vector2 point)
        {
            if (clip?.Sockets == null)
                return null;
            const float hitRadius = 14f;
            string found = null;
            var names = SpriteSocketKeys.UniqueNamesInOrder(clip.Sockets);
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (!SpriteSocketKeys.TryGetPose(clip.Sockets, name, frame,
                        out var position, out _, out _))
                    continue;
                if (Vector2.Distance(SocketToScreen(position, cell), point) <= hitRadius)
                    found = name;
            }
            return found;
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
                SelectPreviewSocket(hit, additive: false, toggle: false);
                _socketPlacementArmed = false;
                _status = $"Selected socket {hit}";
                evt.Use();
                Repaint();
                return;
            }

            RecordProfileUndo("Place Sprite Socket");
            clip.Sockets ??= new List<FrameSocketDef>();
            bool placingExisting = !string.IsNullOrEmpty(_selectedSocketName) &&
                SpriteSocketKeys.IdentityIndex(clip.Sockets, _selectedSocketName) >= 0;
            string name = placingExisting
                ? _selectedSocketName
                : SpriteSocketKeys.NextDefaultName(clip.Sockets);
            Vector2 local = ScreenToSocketLocal(evt.mousePosition, cell);
            var placed = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, name, frame);
            placed.LocalPosition = new Vector2(Mathf.Round(local.x), Mathf.Round(local.y));
            if (!placingExisting)
                placed.LocalAngle = 0f;
            _selectedSocketName = name;
            SelectPreviewSocket(name, additive: false, toggle: false);
            _socketPlacementArmed = false;
            _status = $"Placed {name} on frame {frame + 1}";
            SaveDirty();
            evt.Use();
            Repaint();
        }

        bool HandleSocketManipulationInput(int controlId, Rect cell, SpriteClipDef clip, int frame)
        {
            var evt = Event.current;
            if (evt.type == EventType.MouseDrag && _draggingSocket && GUIUtility.hotControl == controlId)
            {
                if (!_socketMoveUndoRecorded)
                {
                    RecordProfileUndo(_socketMoveNames.Count == 1 ? "Move Sprite Socket" : "Move Sprite Sockets");
                    _socketMoveUndoRecorded = true;
                }
                Vector2 sourceDelta = ScreenToSourcePixelDelta(evt.mousePosition - _socketDragStart, cell);
                for (int i = 0; i < _socketMoveNames.Count; i++)
                {
                    var key = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, _socketMoveNames[i], frame);
                    key.LocalPosition = new Vector2(
                        Mathf.Round(_socketMoveStarts[i].x + sourceDelta.x),
                        Mathf.Round(_socketMoveStarts[i].y + sourceDelta.y));
                }
                evt.Use();
                Repaint();
                return true;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0 && _draggingSocket &&
                GUIUtility.hotControl == controlId)
            {
                _draggingSocket = false;
                _socketMoveNames.Clear();
                _socketMoveStarts.Clear();
                _socketMoveUndoRecorded = false;
                GUIUtility.hotControl = 0;
                SaveDirty();
                evt.Use();
                Repaint();
                return true;
            }

            if (evt.type != EventType.MouseDown || evt.button != 0 || !cell.Contains(evt.mousePosition))
                return false;

            string hit = FindSocketAt(clip, frame, cell, evt.mousePosition);
            if (hit == null)
                return false;

            _playing = false;
            _selectedFrame = frame;
            bool additive = evt.shift || evt.control || evt.command;
            bool alreadySelected = IsSocketSelected(hit);
            if (additive)
            {
                SelectPreviewSocket(hit, additive: true, toggle: evt.control || evt.command);
                evt.Use();
                Repaint();
                return true;
            }

            if (!alreadySelected)
                SelectPreviewSocket(hit, additive: false, toggle: false);
            else
            {
                _selectedSocketName = SpriteSocketKeys.CanonicalName(hit);
                _socketDeleteArmed = true;
                _selectedOnionFrame = -1;
            }

            BeginSocketGroupMove(clip, frame, evt.mousePosition, controlId);
            _status = PreviewSelectionStatus();
            evt.Use();
            Repaint();
            return true;
        }

        void BeginSocketGroupMove(SpriteClipDef clip, int frame, Vector2 mouse, int controlId)
        {
            _socketMoveNames.Clear();
            _socketMoveStarts.Clear();
            if (_selectedSockets.Count == 0 && !string.IsNullOrEmpty(_selectedSocketName))
                _selectedSockets.Add(_selectedSocketName);
            foreach (string name in _selectedSockets)
            {
                if (!SpriteSocketKeys.TryGetPose(clip.Sockets, name, frame, out var pose, out _, out _))
                    continue;
                _socketMoveNames.Add(name);
                _socketMoveStarts.Add(pose);
            }
            if (_socketMoveNames.Count == 0)
                return;
            _draggingSocket = true;
            _socketDragStart = mouse;
            _socketMoveUndoRecorded = false;
            GUIUtility.hotControl = controlId;
            GUIUtility.keyboardControl = controlId;
        }

        void AddClip()
        {
            RecordProfileUndo("Add Sprite Animation Clip");
            var clip = new SpriteClipDef
            {
                Name = $"Clip {_profile.Clips.Count + 1}",
                Row = _profile.Clips.Count % Mathf.Max(1, _profile.Rows),
                Frames = CreateDefaultFrames(),
            };
            clip.EnsureFrameData();
            _profile.Clips.Add(clip);
            _selectedClip = _profile.Clips.Count - 1;
            SelectOnlyFrame(0);
            ClearColliderSelection();
            _selectedEventFrame = -1;
            _selectedOnionFrame = -1;
            ClearSocketToolState();
            _previewTime = 0f;
            SaveDirty();
        }

        void DuplicateClip()
        {
            var source = CurrentClip;
            if (source == null) return;
            RecordProfileUndo("Duplicate Sprite Animation Clip");
            var clone = new SpriteClipDef
            {
                Name = source.Name + " Copy",
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
                    });
                }
            }
            _profile.Clips.Insert(_selectedClip + 1, clone);
            _selectedClip++;
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
            if (clipIndex < _selectedClip)
                _selectedClip--;
            _selectedClip = Mathf.Clamp(_selectedClip, 0, Mathf.Max(0, _profile.Clips.Count - 1));
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

        void InsertFrameAfter(SpriteClipDef clip)
        {
            RecordProfileUndo("Insert Sprite Animation Frame");
            int insert = _selectedFrame + 1;
            var frames = new List<int>(clip.Frames);
            frames.Insert(insert, Mathf.Min(_profile.Columns - 1, clip.Frames[_selectedFrame] + 1));
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
            if (clip?.Frames == null || !TryEnsureSheetPixelCache())
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
            int columns = Mathf.Max(1, _profile.Columns);
            int rows = Mathf.Max(1, _profile.Rows);
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
        {
            var sheet = _profile?.Sheet;
            if (sheet == null)
            {
                InvalidateSheetPixelCache();
                return false;
            }

            int id = sheet.GetInstanceID();
            int columns = Mathf.Max(1, _profile.Columns);
            int rows = Mathf.Max(1, _profile.Rows);
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
            _sheetPixelsId = -1;
            _sheetPixelsWidth = 0;
            _sheetPixelsHeight = 0;
            _sheetPixelsColumns = 0;
            _sheetPixelsRows = 0;
            _sheetCellEmpty = null;
        }

        int[] CreateDefaultFrames()
        {
            int count = Mathf.Max(1, _profile.Columns);
            var frames = new int[count];
            for (int i = 0; i < count; i++) frames[i] = i;
            return frames;
        }

        PreviewState EvaluatePreview(SpriteClipDef clip, float time)
        {
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
            return Mathf.Clamp(64f / Mathf.Max(0.001f, shortest), PixelsPerSecond, 5000f);
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

        void SaveProfile()
        {
            if (_profile.Sheet == null)
            {
                _status = "Assign a sprite sheet before saving";
                ShowNotification(new GUIContent(_status));
                return;
            }

            string texturePath = AssetDatabase.GetAssetPath(_profile.Sheet);
            string directory = Path.GetDirectoryName(texturePath)?.Replace('\\', '/');
            string assetPath = $"{directory}/{_profile.Sheet.name}_profile.asset";
            if (_asset == null)
                TryLoadExistingAsset();
            if (_asset == null)
            {
                _asset = CreateInstance<ScriptableSpriteSheetProfile>();
                AssetDatabase.CreateAsset(_asset, assetPath);
            }
            _asset.Data = _profile;
            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssets();

            string jsonPath = assetPath.Replace(".asset", ".json");
            File.WriteAllText(jsonPath, _profile.ToJson());
            AssetDatabase.ImportAsset(jsonPath);
            _status = $"Saved {_asset.name}";
            ShowNotification(new GUIContent("Profile saved"));
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
            EnsureProfile();
            _selectedClip = 0;
            SelectOnlyFrame(0);
            ClearColliderSelection();
            _selectedEventFrame = -1;
            _selectedOnionFrame = -1;
            _previewTime = 0f;
            _status = $"Loaded {asset.name}";
        }

        void EnsureProfile()
        {
            _profile ??= new SpriteSheetProfile();
            _profile.Clips ??= new List<SpriteClipDef>();
            _profile.Events ??= new List<SpriteEventDef>();
            _profile.Hitboxes ??= new List<FrameBoxDef>();
            _profile.EnsureTimelineHitPolygon();
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
                            "Select this collider. Shift adds; Ctrl/Cmd toggles."),
                        EditorStyles.miniButton, GUILayout.Height(22f)))
                    {
                        var evt = Event.current;
                        _playing = false;
                        _previewTime = PreviewTimeAtFrame(clip, _selectedFrame);
                        SelectCollider(box, evt.shift, evt.control || evt.command);
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
            RecordProfileUndo(undoName);

            if (colliderCount > 0)
                _profile.Hitboxes.RemoveAll(box => _selectedColliders.Contains(box));
            _selectedColliders.Clear();

            if (socketCount > 0 && clip?.Sockets != null)
            {
                var names = new List<string>(_selectedSockets);
                for (int i = 0; i < names.Count; i++)
                    SpriteSocketKeys.DeleteIdentity(clip.Sockets, names[i]);
            }
            if (includeSockets)
                ClearSocketSelection();
            _draggingSocket = false;
            _socketMoveNames.Clear();
            _socketMoveStarts.Clear();
            _status = colliderCount > 0 && socketCount > 0
                ? $"Deleted {colliderCount} collider{(colliderCount == 1 ? string.Empty : "s")} and {socketCount} socket{(socketCount == 1 ? string.Empty : "s")}"
                : socketCount > 0
                    ? $"Deleted {socketCount} socket{(socketCount == 1 ? string.Empty : "s")}"
                    : $"Deleted {colliderCount} collider{(colliderCount == 1 ? string.Empty : "s")}";
            SaveDirty();
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
            if (point.y < 27f || point.y > 54f)
                return -1;
            for (int i = eventXs.Length - 1; i >= 0; i--)
            {
                if (clip.EventIds[i] == 0)
                    continue;
                Vector2 center = new(eventXs[i], 40f);
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

        int CellIndexOf(SpriteClipDef clip, int frame)
        {
            frame = Mathf.Clamp(frame, 0, clip.Frames.Length - 1);
            int row = Mathf.Clamp(clip.Row, 0, Mathf.Max(0, _profile.Rows - 1));
            int column = Mathf.Clamp(clip.Frames[frame], 0, Mathf.Max(0, _profile.Columns - 1));
            return row * Mathf.Max(1, _profile.Columns) + column;
        }

        void DrawCell(Texture2D sheet, int cellIndex, Rect rect, float alpha)
            => DrawCellTinted(sheet, cellIndex, rect, new Color(1f, 1f, 1f, alpha));

        void DrawCellTinted(Texture2D sheet, int cellIndex, Rect rect, Color tint)
        {
            if (sheet == null) return;
            int columns = Mathf.Max(1, _profile.Columns);
            int rows = Mathf.Max(1, _profile.Rows);
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
                $"{_profile.Sheet.width / columns} × {_profile.Sheet.height / rows}");
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

        static void DrawHandleKnob(Vector2 pos, bool edge = false)
        {
            float s = edge ? 7f : 8f;
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
            return EditorGUILayout.TextField(label, value);
        }

        static string DrawStringTextField(GUIContent label, string value, string id)
        {
            GUI.SetNextControlName(StringFieldControlPrefix + id);
            return EditorGUILayout.TextField(label, value);
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
            => _renamingClip >= 0 || EditorGUIUtility.editingTextField;

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
            if (CurrentClip == null)
                return false;
            _playing = !_playing;
            _lastEditorTime = now;
            _status = _playing ? "Playback started" : "Playback paused";
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

            if (evt.keyCode == KeyCode.F2)
            {
                if (IsEditingStringTextField() || CurrentClip == null)
                    return;
                BeginClipRename(_selectedClip);
                evt.Use();
                Repaint();
                return;
            }

            if (evt.keyCode is KeyCode.Delete or KeyCode.Backspace)
            {
                if (IsEditingStringTextField())
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
                if (_selectedColliders.Count > 0 || _selectedSockets.Count > 0)
                {
                    DeleteSelectedPreviewObjects();
                    evt.Use();
                    return;
                }
                PruneEventSelection(CurrentClip);
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
                if (_draggingColliderTransform)
                    RestoreColliderTransform();
                bool hadSelection = _selectedColliders.Count > 0 || _selectedSockets.Count > 0 ||
                                    _selectedEventFrame >= 0 ||
                                    _selectedOnionFrame >= 0 || _colliderCreationMode != ColliderCreationMode.None ||
                                    _colliderMarqueePending || _socketPlacementArmed ||
                                    !string.IsNullOrEmpty(_selectedSocketName) ||
                                    _draggingPivot || _pivotSelected || _draggingColliderTransform;
                ClearColliderSelection();
                _selectedEventFrame = -1;
                _selectedOnionFrame = -1;
                CancelColliderCreation("Selection and active tools cleared");
                CancelSocketPlacement(null);
                _draggingSocket = false;
                _socketMoveNames.Clear();
                _socketMoveStarts.Clear();
                _draggingOnion = false;
                _draggingPivot = false;
                _pivotSelected = false;
                if (_colliderMarqueePending)
                    GUIUtility.hotControl = 0;
                _colliderMarqueePending = false;
                _draggingColliderMarquee = false;
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
            if (_asset != null)
            {
                _profile = _asset.Data ?? new SpriteSheetProfile();
                if (_asset.Data == null)
                    _asset.Data = _profile;
            }
            EnsureProfile();
            _selectedClip = Mathf.Clamp(_selectedClip, 0, Mathf.Max(0, _profile.Clips.Count - 1));
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
            _draggingBox = false;
            _draggingOnion = false;
            _draggingSocket = false;
            _draggingPivot = false;
            _pivotSelected = false;
            ClearColliderTransform();
            CancelSocketPlacement(null);
            _selectedSocketName = null;
            _socketDeleteArmed = false;
            ClearPolygonDraft();
            if (_timelineDragMode != TimelineDragMode.None)
                EndTimelineDrag();
            _status = "Undo/Redo applied";
            Repaint();
        }

        void PrepareInspectorUndo()
        {
            EventType type = Event.current.type;
            if (type is EventType.MouseDown or EventType.KeyDown or
                EventType.ExecuteCommand or EventType.DragPerform)
                RecordProfileUndo("Edit Sprite Animator");
        }

        void RecordProfileUndo(string operation)
        {
            if (_asset != null)
                Undo.RecordObjects(new UnityEngine.Object[] { _asset, this }, operation);
            else
                Undo.RecordObject(this, operation);
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

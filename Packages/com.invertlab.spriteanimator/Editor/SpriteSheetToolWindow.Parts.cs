using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    public sealed partial class SpriteSheetToolWindow
    {
        enum StudioTab
        {
            Clips = 0,
            Parts = 1,
            Static = 2,
        }

        enum PartsCanvasTool
        {
            Move = 0,
            Rotate = 1,
            Scale = 2,
            Warp = 3,
        }

        enum PartsBrowserFocus
        {
            Clips = 0,
            Tree = 1,
        }

        struct PartsGroupMoveMember
        {
            public string SlotId;
            public SpritePartsAuthoringOps.PoseEdit StartPose;
            public float2 StartWorld;
            public float4x4 ParentToRoot;
            public bool HasParent;
        }

        [SerializeField] StudioTab _studioTab;
        [SerializeField] SpritePartsStudioMode _partsMode = SpritePartsStudioMode.Animate;
        [SerializeField] int _partsSelectedClip;
        [SerializeField] int _partsSelectedSlot = -1;
        // Leaving Parts stashes the primary selected slot id + clip id; returning
        // to Parts restores them by id (clears when the target no longer exists).
        [SerializeField] string _partsLastSelectedSlotId;
        [SerializeField] string _partsLastSelectedClipId;
        // Leaving Frames stashes clip index + frame; returning restores if still valid.
        [SerializeField] int _framesLastSelectedClip = -1;
        [SerializeField] int _framesLastSelectedFrame = -1;
        [SerializeField] float _partsPreviewTime;
        [SerializeField] bool _partsPlaying;
        [SerializeField] bool _partsAutoKey = true;
        [SerializeField] string _partsKeyAppearanceId = string.Empty;
        [SerializeField] bool _partsKeyPoseIncludesAppearance;
        [SerializeField] bool _partsOnionEnabled = true;
        [SerializeField] int _partsOnionBefore = SpritePartsAuthoringOps.DefaultOnionBefore;
        [SerializeField] int _partsOnionAfter = SpritePartsAuthoringOps.DefaultOnionAfter;
        [SerializeField] int _partsOnionSpacing = SpritePartsAuthoringOps.DefaultOnionSpacingFrames;
        [SerializeField] float _partsOnionOpacity = SpritePartsAuthoringOps.DefaultOnionOpacity;
        [SerializeField] bool _partsShowArt = true;
        [SerializeField] bool _partsShowDebug = true;
        [SerializeField] bool _partsShowRoot = true;
        [SerializeField] bool _partsShowAxes = true;
        [SerializeField] SpritePartsAlignPivot _partsAlignPivot = SpritePartsAlignPivot.Center;
        [SerializeField] float _partsDisplayFps = SpritePartsAuthoringOps.DefaultDisplayFps;
        // Preview: wrap playhead at last key time instead of Duration.
        [SerializeField] bool _partsLoopLastKey;
        [SerializeField] int _partsFrameStep = 1;
        [SerializeField] bool _partsOnionShowWhilePlaying;
        [SerializeField] PartsCanvasTool _partsCanvasTool = PartsCanvasTool.Move;
        [SerializeField] string _partsPreviewSkinId = "default";
        [SerializeField] bool _partsLinkedScale = true;
        // Move axis locks (persistent). Shift still does Affinity-style dominant-axis constrain.
        [SerializeField] bool _partsLockMoveX;
        [SerializeField] bool _partsLockMoveY;
        [SerializeField] bool _partsGizmoLocal;
        float2 _partsDragAxisWorld;
        [SerializeField] int _partsArtColumns = 1;
        [SerializeField] int _partsArtRows = 1;
        [SerializeField] int _partsArtCell;
        // Per-workspace camera state: the live _previewZoom/_previewPan pair is
        // swapped with these on every Frames/Parts tab switch so each workspace
        // keeps its own zoom and pan.
        [SerializeField] float _framesPreviewZoom = 1f;
        [SerializeField] Vector2 _framesPreviewPan;
        [SerializeField] float _partsCanvasZoom = 1f;
        [SerializeField] Vector2 _partsCanvasPan;
        SpriteArtLibrary _pendingLibraryAttach;

        readonly Dictionary<string, string> _partsSkinPreviewOverrides = new(StringComparer.Ordinal);
        string _partsArtSyncedSlotId;
        bool _partsDragActive;
        bool _partsMarqueeActive;
        bool _partsMarqueeAdditive;
        Vector2 _partsMarqueeStart;
        Vector2 _partsMarqueeCurrent;
        string _partsDragSlotId;
        // 0 = unset, 1 = horizontal (X), 2 = vertical (Y). Sticky for the current drag.
        int _partsDragShiftAxis;
        Vector2 _partsDragStartMouse;
        Vector2 _partsDragStartJoint;
        float _partsDragStartGuiDeg;
        float2 _partsDragStartWorld;
        float4x4 _partsDragParentToRoot;
        bool _partsDragHasParent;
        SpritePartsAuthoringOps.PoseEdit _partsDragStartPose;
        readonly List<PartsGroupMoveMember> _partsGroupMoveMembers = new();
        readonly Dictionary<string, SpritePartsAuthoringOps.PoseEdit> _partsGroupTempPoses =
            new(StringComparer.Ordinal);
        ColliderHandleKind _partsTransformHandle;
        int _partsDragUndoGroup = -1;
        int _partsCanvasHotControl;
        int _partsScrubHotControl;
        bool _partsScrubbing;
        const float PartsRotateHandleDistance = 26f;
        const float PartsHandleHit = 10f;
        const float PartsScaleHandleHit = 20f; // Unity-like: grab knobs, not the body
        Vector2 _partsBrowserScroll;
        Vector2 _partsInspectorScroll;
        PartsBrowserFocus _partsBrowserFocus = PartsBrowserFocus.Tree;
        int _partsRenamingClip = -1;
        string _partsClipRenameDraft;
        bool _partsClipRenameFocus;
        // Temporary unkeyed pose when Auto Key is off (Animate).
        bool _partsHasTempPose;
        string _partsTempSlotId;
        SpritePartsAuthoringOps.PoseEdit _partsTempPose;

        // Inspector Transform section clipboard (Copy / Paste per-row or all).
        bool _partsXformClipboardHasPos;
        Vector2 _partsXformClipboardPos;
        bool _partsXformClipboardHasRot;
        float _partsXformClipboardRot;
        bool _partsXformClipboardHasScale;
        Vector2 _partsXformClipboardScale;

        // Appearance pivot clipboard (normalized 0-1).
        bool _partsPivotClipboardValid;
        Vector2 _partsPivotClipboard = new(0.5f, 0.5f);
        string _partsPivotFocusSlotId;
        bool _partsPivotDrag;

        SpritePartsClipDef CurrentPartsClip
        {
            get
            {
                if (_profile?.PartsClips == null || _profile.PartsClips.Count == 0)
                    return null;
                _partsSelectedClip = Mathf.Clamp(_partsSelectedClip, 0, _profile.PartsClips.Count - 1);
                return _profile.PartsClips[_partsSelectedClip];
            }
        }

        SpritePartSlotDef CurrentPartsSlot
        {
            get
            {
                if (_profile?.PartsSlots == null || _partsSelectedSlot < 0 ||
                    _partsSelectedSlot >= _profile.PartsSlots.Count)
                    return null;
                return _profile.PartsSlots[_partsSelectedSlot];
            }
        }

        // Transient Parts clip import preview: a cloned clip sampled against
        // this rig without ever entering the profile. Rest/rig data is never
        // touched; editing is paused while the preview runs.
        SpritePartsClipDef _importPreviewClip;
        string _importPreviewSourceName;
        string _importPreviewClipName;
        string _importPreviewBanner;

        bool ImportPreviewActive => _importPreviewClip != null;

        internal bool ImportPreviewing => ImportPreviewActive;
        internal float ImportPreviewTime => _partsPreviewTime;
        internal float ImportPreviewDuration =>
            ImportPreviewActive ? Mathf.Max(1e-3f, _importPreviewClip.Duration) : 0f;
        internal bool ImportPreviewPlaying => _partsPlaying;

        internal void StartImportPreview(
            string sourceName, string clipName, SpritePartsClipDef transientClip,
            string status = null, string banner = null)
        {
            if (transientClip == null) return;
            _importPreviewClip = transientClip;
            _importPreviewSourceName = sourceName ?? string.Empty;
            _importPreviewClipName = clipName ?? string.Empty;
            _importPreviewBanner = banner;
            _partsHasTempPose = false;
            _partsPreviewTime = 0f;
            _partsPlaying = true;
            _lastEditorTime = EditorApplication.timeSinceStartup;
            _status = status ??
                      $"Previewing '{_importPreviewClipName}' from {_importPreviewSourceName} on this rig (motion copy, no retargeting)";
            Repaint();
        }

        internal void SetImportPreviewPlaying(bool playing)
        {
            if (!ImportPreviewActive) return;
            _partsPlaying = playing;
            _lastEditorTime = EditorApplication.timeSinceStartup;
            Repaint();
        }

        internal void SetImportPreviewTime(float time)
        {
            if (!ImportPreviewActive) return;
            _partsPreviewTime = Mathf.Clamp(time, 0f, ImportPreviewDuration);
            Repaint();
        }

        internal void ClearImportPreview(string status = null)
        {
            if (!ImportPreviewActive)
                return;
            _importPreviewClip = null;
            _importPreviewSourceName = null;
            _importPreviewClipName = null;
            _importPreviewBanner = null;
            _partsPlaying = false;
            if (!string.IsNullOrEmpty(status))
                _status = status;
            Repaint();
        }

        void PauseForImportPreview(string attempted)
        {
            _status = "Import preview active - editing paused. Commit or stop the preview first. (" + attempted + ")";
        }


        internal float PartsPreviewTime => _partsPreviewTime;
        internal int PartsSelectedClipIndex => _partsSelectedClip;

        void DrawPartsStudioTabToggle(Rect toolbarRect)
        {
            _ = toolbarRect;
            float tabX = 460f;
            var staticRect = new Rect(tabX, 10f, 64f, 28f);
            var clipsRect = new Rect(tabX + 68f, 10f, 68f, 28f);
            var partsRect = new Rect(tabX + 140f, 10f, 64f, 28f);
            var clipsStyle = _studioTab == StudioTab.Clips ? _primaryStyle : _transportStyle;
            var partsStyle = _studioTab == StudioTab.Parts ? _primaryStyle : _transportStyle;
            var staticStyle = _studioTab == StudioTab.Static ? _primaryStyle : _transportStyle;
            if (GUI.Button(staticRect, new GUIContent("Static", "One still image or sheet cell. No animation clips."), staticStyle))
                SwitchStudioTab(StudioTab.Static);
            if (GUI.Button(clipsRect, new GUIContent("Frames", "Frame flipbook authoring (frame clips)."), clipsStyle))
                SwitchStudioTab(StudioTab.Clips);
            if (GUI.Button(partsRect, new GUIContent("Parts", "Cutout Parts rig / animate / skins."), partsStyle))
                SwitchStudioTab(StudioTab.Parts);

            // Runtime badge: the Frames/Parts/Static buttons above only change the
            // editor workspace. The profile's runtime mode is separate and explicit.
            var badgeRect = new Rect(tabX + 210f, 15f, 128f, 18f);
            GUI.Label(badgeRect, new GUIContent(
                RuntimeKindBadgeLabel(_profile != null ? _profile.AnimKind : SpriteAnimKind.Frame),
                "Runtime playback mode stored on the profile. Switch it with 'Use ... for Character' in the inactive workspace banner; switching workspaces never changes it."),
                _mutedStyle);
        }

        static string RuntimeKindBadgeLabel(SpriteAnimKind kind)
            => kind switch
            {
                SpriteAnimKind.Parts => "Runtime: Parts",
                SpriteAnimKind.Static => "Runtime: Static",
                _ => "Runtime: Frames",
            };

        static string StudioTabUndoLabel(StudioTab tab)
            => tab switch
            {
                StudioTab.Parts => "Switch to Parts tab",
                StudioTab.Static => "Switch to Static tab",
                _ => "Switch to Frames tab",
            };

        void SwitchStudioTab(StudioTab next)
        {
            if (_studioTab == next || !TryResolveTempPoseForSwitch())
                return;
            ClearImportPreview();
            RecordWindowUndo(StudioTabUndoLabel(next));
            bool wasParts = _studioTab == StudioTab.Parts;
            bool goingParts = next == StudioTab.Parts;
            if (_studioTab == StudioTab.Clips)
                StashFramesSelection();
            if (_studioTab == StudioTab.Parts)
                StashPartsSelection();
            if (wasParts != goingParts)
                SwapWorkspaceCameraState(toParts: goingParts);
            _studioTab = next;
            _playing = false;
            _partsPlaying = false;
            if (next == StudioTab.Clips)
                RestoreFramesSelection();
            else if (next == StudioTab.Parts)
            {
                RestorePartsSelectionById();
                EnsurePartsSession();
            }
            else
                EnsureStaticSession();
        }

        /// <summary>Leaving Frames: remember clip index + frame for restore.</summary>
        void StashFramesSelection()
        {
            _framesLastSelectedClip = _selectedClip;
            _framesLastSelectedFrame = _selectedFrame;
        }

        /// <summary>Leaving Parts: remember primary slot + clip by stable id.</summary>
        void StashPartsSelection()
        {
            var slot = CurrentPartsSlot;
            _partsLastSelectedSlotId = slot != null
                ? SpritePartIdUtility.Canonical(slot.SlotId, slot.Name)
                : string.Empty;
            var clip = CurrentPartsClip;
            _partsLastSelectedClipId = clip != null
                ? SpritePartIdUtility.Canonical(clip.ClipId, clip.Name)
                : string.Empty;
        }

        /// <summary>Returning to Parts: restore stashed clip + slot by id.</summary>
        void RestorePartsSelectionById()
        {
            if (_profile == null) return;
            if (!string.IsNullOrEmpty(_partsLastSelectedClipId) && _profile.PartsClips != null)
            {
                int clipIndex = SpritePartsAuthoringOps.FindClipIndex(_profile, _partsLastSelectedClipId);
                if (clipIndex >= 0)
                    _partsSelectedClip = clipIndex;
            }
            if (_profile.PartsSlots == null || string.IsNullOrEmpty(_partsLastSelectedSlotId))
            {
                // Keep current slot clamp via EnsurePartsSession.
            }
            else
            {
                _partsSelectedSlot = SpritePartsAuthoringOps.FindSlotIndex(
                    _profile, _partsLastSelectedSlotId);
            }
        }

        /// <summary>
        /// Returning to Frames: restore stashed clip + frame when still valid;
        /// otherwise clamp into range.
        /// </summary>
        void RestoreFramesSelection()
        {
            if (_profile?.Clips == null || _profile.Clips.Count == 0)
            {
                _selectedClip = -1;
                _selectedFrame = 0;
                return;
            }
            int clip = _framesLastSelectedClip >= 0 ? _framesLastSelectedClip : _selectedClip;
            _selectedClip = Mathf.Clamp(clip, 0, _profile.Clips.Count - 1);
            var frames = _profile.Clips[_selectedClip]?.Frames;
            int maxFrame = frames != null && frames.Length > 0 ? frames.Length - 1 : 0;
            int frame = _framesLastSelectedFrame >= 0 ? _framesLastSelectedFrame : _selectedFrame;
            _selectedFrame = Mathf.Clamp(frame, 0, maxFrame);
        }

        void SwapWorkspaceCameraState(bool toParts)
        {
            // Preview time is already per workspace: Frames uses _previewTime,
            // Parts uses _partsPreviewTime. Only the shared zoom/pan pair is swapped.
            if (toParts)
            {
                _framesPreviewZoom = _previewZoom;
                _framesPreviewPan = _previewPan;
                _previewZoom = _partsCanvasZoom;
                _previewPan = _partsCanvasPan;
            }
            else
            {
                _partsCanvasZoom = _previewZoom;
                _partsCanvasPan = _previewPan;
                _previewZoom = _framesPreviewZoom;
                _previewPan = _framesPreviewPan;
            }
        }

        /// <summary>
        /// Resolve a staged unkeyed pose before a workspace or document switch.
        /// Key / Discard / Cancel; Cancel aborts the switch.
        /// </summary>
        bool TryResolveTempPoseForSwitch()
        {
            if (!_partsHasTempPose)
                return true;
            int choice = EditorUtility.DisplayDialogComplex("Unkeyed pose edit",
                "An unkeyed pose edit is staged in Parts Animate. Key it into the clip, discard it, or cancel the switch?",
                "Key Pose", "Cancel", "Discard");
            if (choice == 0)
            {
                CommitKeyPose();
                _partsHasTempPose = false;
                return true;
            }
            if (choice == 2)
            {
                _partsHasTempPose = false;
                _status = "Discarded temporary unkeyed pose";
                return true;
            }
            return false;
        }

        /// <summary>
        /// Explicit, Undoable runtime-mode change. Validates first so a rejected
        /// switch records no Undo and changes nothing; the failure dialog names
        /// the fix. Both data sets are always preserved.
        /// </summary>
        void SwitchRuntimeKind(SpriteAnimKind kind, string actionLabel)
        {
            if (_profile == null)
                return;
            if (!SpritePartsAuthoringOps.CanSetAnimKind(_profile, kind, out string reason))
            {
                _status = reason;
                EditorUtility.DisplayDialog(actionLabel,
                    reason + "\n\nNothing was changed. Fix the data listed above, then try again.", "OK");
                return;
            }
            RecordPartsUndo(actionLabel);
            var result = SpritePartsAuthoringOps.TrySetAnimKind(_profile, kind);
            if (!result.Ok)
            {
                _status = result.Reason;
                return;
            }
            SaveDirty();
            _status = kind switch
            {
                SpriteAnimKind.Parts => "Runtime mode: Parts (frame clips kept, not baked)",
                SpriteAnimKind.Static => "Runtime mode: Static (one cell, clips and Parts kept)",
                _ => "Runtime mode: Frames (Parts data kept, not baked)",
            };
            Repaint();
        }

        void CreatePartsCharacter(bool floating)
        {
            RecordPartsUndo(floating ? "Create Floating Parts Character" : "Create Empty Parts Rig");
            if (floating)
            {
                SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(_profile);
                _partsSelectedClip = 0;
                _partsSelectedSlot = 0;
                _partsPreviewTime = 0f;
                _partsSkinPreviewOverrides.Clear();
                SaveDirty();
                _status = "Created Floating Parts character";
                return;
            }
            SpritePartsAuthoringOps.CreateEmptyPartsRig(_profile);
            var check = SpritePartsValidation.Validate(_profile);
            if (!check.Ok)
            {
                // Empty rig is not playable yet: keep Frames runtime until art exists.
                _profile.AnimKind = SpriteAnimKind.Frame;
                _status = "Empty Parts rig created. Add parts and art, then 'Use Parts for Character'.";
            }
            else
            {
                SaveDirty();
                _status = "Created empty Parts rig";
            }
        }

        void EnsurePartsSession()
        {
            if (_profile == null) return;
            _profile.EnsurePartsRig();
            if (_partsSelectedClip < 0) _partsSelectedClip = 0;
            if (_profile.PartsClips.Count > 0)
                _partsSelectedClip = Mathf.Clamp(_partsSelectedClip, 0, _profile.PartsClips.Count - 1);
            if (string.IsNullOrEmpty(_partsPreviewSkinId) && !string.IsNullOrEmpty(_profile.PartsDefaultSkinId))
                _partsPreviewSkinId = _profile.PartsDefaultSkinId;
            if (_partsExpandedSlotIds.Count == 0 && _profile.PartsSlots != null)
            {
                for (int i = 0; i < _profile.PartsSlots.Count; i++)
                {
                    var s = _profile.PartsSlots[i];
                    if (s != null) _partsExpandedSlotIds.Add(SpritePartIdUtility.Canonical(s.SlotId));
                }
            }
            EnsurePartsTreeSelectionSynced();
        }

        void TickPartsPreview(float delta)
        {
            if (_studioTab != StudioTab.Parts || !_partsPlaying)
                return;
            var clip = ImportPreviewActive ? _importPreviewClip : CurrentPartsClip;
            if (clip == null)
            {
                _partsPlaying = false;
                return;
            }
            float duration = Mathf.Max(1e-3f, clip.Duration);
            float authored = clip.Speed;
            if (float.IsNaN(authored) || float.IsInfinity(authored))
                authored = 1f;
            float eventsFrom = _partsPreviewTime;
            try
            {
            _partsPreviewTime += delta * Mathf.Max(0.05f, _speed) * authored;
            // Loop Last: wrap at last keyframe time back to first (preview only).
            float lastKey = 0f;
            if (_partsLoopLastKey && !ImportPreviewActive)
                lastKey = GetPartsClipLastKeyTime(clip);
            if (_partsLoopLastKey && !ImportPreviewActive && lastKey > 1e-5f)
            {
                float wrapEnd = Mathf.Max(1e-3f, lastKey);
                if (_partsPreviewTime >= wrapEnd)
                    _partsPreviewTime = SpritePartsSampler.WrapTime(_partsPreviewTime, wrapEnd, (byte)SpritePartsWrap.Loop);
            }
            else if (clip.WrapMode == (byte)SpritePartsWrap.Once)
            {
                if (_partsPreviewTime >= duration)
                {
                    _partsPreviewTime = duration;
                    _partsPlaying = false;
                }
            }
            else
            {
                _partsPreviewTime = SpritePartsSampler.WrapTime(_partsPreviewTime, duration, clip.WrapMode);
            }
            ApplyPartsRange(duration);
            }
            finally
            {
                FlashPartsEvents(clip, eventsFrom, _partsPreviewTime);
            }
        }

        void DrawPartsBrowser(Rect rect)
        {
            EnsurePartsSession();
            Event bev = Event.current;
            if (bev.type == EventType.MouseDown
                && bev.button == 0
                && rect.Contains(bev.mousePosition))
                ReleasePartsCanvasCapture();
            HandlePartsRenameClickAway(Event.current);
            GUILayout.BeginArea(rect);
            _partsBrowserScroll = EditorGUILayout.BeginScrollView(_partsBrowserScroll);
            GUILayout.Label("PARTS CLIPS", _sectionStyle);
            if (_profile.AnimKind != SpriteAnimKind.Parts)
            {
                bool hasPartsData = (_profile.PartsSlots?.Count ?? 0) > 0 ||
                                    (_profile.PartsClips?.Count ?? 0) > 0 ||
                                    (_profile.PartsAppearances?.Count ?? 0) > 0;
                string runtimeName = _profile.AnimKind == SpriteAnimKind.Static ? "Static" : "Frames";
                EditorGUILayout.HelpBox(
                    $"Preview only. Character currently uses {runtimeName}. Parts edits here are saved with the profile but do not bake until the runtime mode changes.",
                    MessageType.Info);
                if (hasPartsData)
                {
                    if (GUILayout.Button("Use Parts for Character", GUILayout.Height(22f)))
                        SwitchRuntimeKind(SpriteAnimKind.Parts, "Use Parts for Character");
                }
                else
                {
                    if (GUILayout.Button("Create Parts Character (Floating Parts)"))
                        CreatePartsCharacter(floating: true);
                    if (GUILayout.Button("Create Empty Parts Rig"))
                        CreatePartsCharacter(floating: false);
                }
                GUILayout.Space(8f);
                // Fall through: the Parts clip list and tree stay editable while
                // the runtime mode is Frames.
            }

            for (int i = 0; i < _profile.PartsClips.Count; i++)
            {
                var clip = _profile.PartsClips[i];
                if (clip == null) continue;
                DrawPartsClipRow(i, clip);
            }
            HandlePartsClipRenameHotkeys();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Clip", GUILayout.Width(70f)))
                AddPartsClip();
            if (GUILayout.Button("Delete", GUILayout.Width(60f)) && CurrentPartsClip != null && _profile.PartsClips.Count > 1)
            {
                CancelPartsClipRename();
                RecordPartsUndo("Delete Parts Clip");
                _profile.PartsClips.RemoveAt(_partsSelectedClip);
                _partsSelectedClip = Mathf.Clamp(_partsSelectedClip, 0, _profile.PartsClips.Count - 1);
                SaveDirty();
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("PARTS TREE", _sectionStyle);
            DrawPartsTreeToolbar();
            DrawPartsTree();
            DrawPartsLayers();

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawPartsInspector(Rect rect)
        {
            EnsurePartsSession();
            Event iev = Event.current;
            if (iev.type == EventType.MouseDown
                && iev.button == 0
                && rect.Contains(iev.mousePosition))
                ReleasePartsCanvasCapture();
            GUILayout.BeginArea(rect);
            _partsInspectorScroll = EditorGUILayout.BeginScrollView(_partsInspectorScroll);
            GUILayout.Label("PARTS", _sectionStyle);
            GUILayout.Space(4f);
            DrawPartsModeToolbar();
            GUILayout.Space(8f);
            DrawArtLibrariesInspector();
            GUILayout.Space(4f);
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Max(72f, Mathf.Min(96f, rect.width * 0.38f));

            if (_partsMode == SpritePartsStudioMode.Rig)
                EditorGUILayout.HelpBox("Rig changes affect every clip.", MessageType.Warning);

            if (_profile.AnimKind != SpriteAnimKind.Parts)
            {
                string runtimeName = _profile.AnimKind == SpriteAnimKind.Static ? "Static" : "Frames";
                EditorGUILayout.HelpBox(
                    $"Preview only. Character currently uses {runtimeName}. Use 'Use Parts for Character' in the left panel to activate this rig.",
                    MessageType.None);
            }

            var clip = CurrentPartsClip;
            if (clip != null && _partsMode == SpritePartsStudioMode.Animate
                && PartsSection("CLIP", clip.Name + "  " + clip.Duration.ToString("0.##") + "s"))
            {
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.TextField("Clip Name", clip.Name);
                float duration = EditorGUILayout.FloatField("Duration", clip.Duration);
                float timeScale = EditorGUILayout.FloatField(
                    new GUIContent("Time Scale",
                        "Parts playback multiplier. 1 = normal, 2 = twice as fast, 0.5 = half speed, 0 = paused, negative = reverse."),
                    clip.Speed);
                byte wrap = (byte)EditorGUILayout.Popup("Wrap", clip.WrapMode,
                    new[] { "Loop", "Once" });
                if (EditorGUI.EndChangeCheck())
                {
                    RecordPartsUndo("Edit Parts Clip");
                    clip.Name = name;
                    clip.Duration = Mathf.Max(1e-3f, duration);
                    clip.Speed = float.IsNaN(timeScale) || float.IsInfinity(timeScale)
                        ? 1f
                        : timeScale;
                    clip.WrapMode = wrap;
                    SaveDirty();
                }
                DrawImportProvenanceLabel(clip.Import);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Retarget Parts Clip...",
                        "Explicitly remap this (or another) clip's keys onto this rig's rest/hierarchy. Not Spine-style retargeting."),
                    GUILayout.Height(20f)))
                {
                    var anchor = GUILayoutUtility.GetLastRect();
                    PopupWindow.Show(anchor, new SpritePartsClipRetargetPopup(this));
                }
                if (GUILayout.Button(new GUIContent("Bake to Frame Clip...",
                        "Sample this Parts clip at a chosen FPS into a new frame clip. Does not change runtime AnimKind."),
                    GUILayout.Height(20f)))
                {
                    var anchor = GUILayoutUtility.GetLastRect();
                    PopupWindow.Show(anchor, new SpritePartsToFrameBakePopup(this, _partsSelectedClip));
                }
                EditorGUILayout.EndHorizontal();
            }

            var slot = CurrentPartsSlot;
            if (slot != null)
            {
                bool partLocked = slot.EditorLocked ||
                    SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, slot.SlotId);

                DrawPartsMeshInspector(slot);

                // Transform first - most edited while posing.
                GUILayout.Space(6f);
                DrawPartsTransformInspector(slot, partLocked);
                DrawPartsBoneInspector(slot, partLocked);
                DrawPartsClipShapeInspector(slot, partLocked);
                DrawPartsPathPartInspector(slot, partLocked);
                DrawPartsKeyInspector(slot, partLocked);

                bool partOpen = PartsSection("SELECTED PART", slot.Name);
                if (partOpen)
                {
                using (new EditorGUI.DisabledScope(partLocked))
                {
                    EditorGUI.BeginChangeCheck();
                    string slotName = EditorGUILayout.TextField("Name", slot.Name);
                    if (EditorGUI.EndChangeCheck())
                    {
                        RecordPartsUndo("Rename Parts Slot");
                        var rename = SpritePartsAuthoringOps.TryRenameDisplayName(_profile, slot.SlotId, slotName);
                        if (!rename.Ok)
                            _status = rename.Reason;
                        else
                            SaveDirty();
                    }
                }
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField("Slot Id", slot.SlotId);
                EditorGUI.EndDisabledGroup();
                EditorGUI.BeginChangeCheck();
                var nextRole = (SpritePartSemanticRole)EditorGUILayout.EnumPopup(
                    new GUIContent("Semantic Role",
                        "Optional outfit role for Apply Outfit (Body/Head/Weapon/Offhand). None = ignored."),
                    slot.SemanticRole);
                if (EditorGUI.EndChangeCheck())
                {
                    RecordPartsUndo("Set Slot Semantic Role");
                    slot.SemanticRole = nextRole;
                    SaveDirty();
                }
                EditorGUILayout.LabelField("Sibling Order", slot.SiblingOrder.ToString());
                DrawPartsClipMaskInspector(slot, partLocked);
                if (_partsMode == SpritePartsStudioMode.Rig)
                {
                    using (new EditorGUI.DisabledScope(partLocked || _partsDragActive))
                    {
                        EditorGUI.BeginChangeCheck();
                        string parent = DrawParentPopup(slot);
                        string appearance = EditorGUILayout.TextField("Default Appearance Id", slot.DefaultAppearanceId);
                        if (EditorGUI.EndChangeCheck() && !_partsDragActive)
                        {
                            RecordPartsUndo("Edit Parts Slot");
                            string newParent = parent ?? string.Empty;
                            string oldParent = slot.ParentSlotId ?? string.Empty;
                            if (SpritePartIdUtility.Canonical(newParent) !=
                                SpritePartIdUtility.Canonical(oldParent))
                            {
                                var kind = string.IsNullOrEmpty(newParent)
                                    ? SpritePartsAuthoringOps.TreeDropKind.MoveToRoot
                                    : SpritePartsAuthoringOps.TreeDropKind.ParentUnder;
                                var rel = string.IsNullOrEmpty(newParent) ? string.Empty : newParent;
                                var move = SpritePartsAuthoringOps.TryCommitTreeMove(
                                    _profile, slot.SlotId, kind, rel, confirmAnimationReview: true);
                                if (!move.Ok)
                                    _status = move.Reason;
                            }
                            slot.DefaultAppearanceId = appearance;
                            SpritePartsValidation.CanonicalizeIds(_profile);
                            SaveDirty();
                        }
                    }
                }
                }
                DrawPartsZOrderInspector(slot);
                DrawPartsArtInspector(slot);
            }

            if (_partsMode == SpritePartsStudioMode.Skins)
                DrawPartsSkinsInspector();
            else
            {
                DrawPartsEventInspector();
                DrawPartsIkInspector();
                DrawPartsTransformConstraintsInspector();
                DrawPartsPathConstraintsInspector();
                DrawPartsJiggleInspector();
                DrawPartsParamsInspector();
                DrawPartsTransitionsInspector();
                DrawPartsLayerPreviewInspector();
                DrawPartsMasksInspector();
                DrawPartsSpriteGroupsInspector();
                DrawPartsBlendSpacesInspector();
            }

            EditorGUIUtility.labelWidth = prevLabelWidth;
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawPartsModeToolbar()
        {
            int current = (int)_partsMode;
            int next = GUILayout.Toolbar(
                current,
                new[] { "Rig", "Animate", "Skins" },
                _partsTabStyle,
                GUILayout.Height(22f),
                GUILayout.ExpandWidth(true));
            if (next == current)
                return;
            SwitchPartsMode((SpritePartsStudioMode)next, "Change Parts Mode");
        }


        /// <summary>
        /// Unity-Transform-style Position / Rotation / Scale for the selected part.
        /// Rig edits rest; Animate edits the sampled (or temp) pose. Each row has Copy / Paste / Reset.
        /// </summary>
        void DrawPartsTransformInspector(SpritePartSlotDef slot, bool partLocked)
        {
            if (slot == null) return;

            if (!PartsSection("TRANSFORM"))
                return;

            if (_partsMode == SpritePartsStudioMode.Skins)
            {
                EditorGUILayout.HelpBox(
                    "Skins edits art and pivot. Double-click a part to drag its pivot. Move, Rotate, and Scale stay in Rig and Animate.",
                    MessageType.None);
                using (new EditorGUI.DisabledScope(partLocked))
                    DrawPartsAppearancePivotInspector(slot);
                return;
            }

            bool isRig = _partsMode == SpritePartsStudioMode.Rig;
            string modeHint = isRig
                ? "Rest pose (shared by every clip)"
                : (_partsPlaying
                    ? "Playing - transform fields locked (avoids baking keys while scrubbing)"
                    : (_partsAutoKey
                        ? "Clip pose at playhead (Auto Key ON)"
                        : "Clip pose at playhead (Auto Key OFF = temp until Key Pose)"));
            EditorGUILayout.LabelField(modeHint, EditorStyles.miniLabel);

            var pose = isRig
                ? new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = slot.RestPosition,
                    Rotation = slot.RestRotation,
                    Scale = slot.RestScale,
                }
                : SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);

            var rest = new SpritePartsAuthoringOps.PoseEdit
            {
                Position = slot.RestPosition,
                Rotation = slot.RestRotation,
                Scale = slot.RestScale,
            };

            using (new EditorGUI.DisabledScope(partLocked || _partsDragActive || _partsPlaying))
            {
                // Header actions
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Copy All", "Copy Position, Rotation, and Scale"), GUILayout.Height(18f)))
                {
                    _partsXformClipboardPos = pose.Position;
                    _partsXformClipboardRot = pose.Rotation;
                    _partsXformClipboardScale = pose.Scale;
                    _partsXformClipboardHasPos = _partsXformClipboardHasRot = _partsXformClipboardHasScale = true;
                    _status = "Copied transform";
                }
                using (new EditorGUI.DisabledScope(
                           !_partsXformClipboardHasPos && !_partsXformClipboardHasRot && !_partsXformClipboardHasScale))
                {
                    if (GUILayout.Button(new GUIContent("Paste All", "Paste copied transform components"), GUILayout.Height(18f)))
                    {
                        var next = pose;
                        if (_partsXformClipboardHasPos) next.Position = _partsXformClipboardPos;
                        if (_partsXformClipboardHasRot) next.Rotation = _partsXformClipboardRot;
                        if (_partsXformClipboardHasScale) next.Scale = _partsXformClipboardScale;
                        CommitPartsTransformInspectorEdit(slot, next, isRig, "Paste Parts Transform");
                    }
                }
                if (GUILayout.Button(new GUIContent("Reset All",
                        isRig ? "Reset rest to 0 / 0 / 1" : "Reset pose to this part's rest"), GUILayout.Height(18f)))
                {
                    var next = isRig
                        ? new SpritePartsAuthoringOps.PoseEdit
                        {
                            Position = Vector2.zero,
                            Rotation = 0f,
                            Scale = Vector2.one,
                        }
                        : rest;
                    CommitPartsTransformInspectorEdit(slot, next, isRig, "Reset Parts Transform");
                }
                EditorGUILayout.EndHorizontal();

                // Position
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                Vector2 pos = EditorGUILayout.Vector2Field("Position", pose.Position);
                if (EditorGUI.EndChangeCheck())
                {
                    pose.Position = pos;
                    CommitPartsTransformInspectorEdit(slot, pose, isRig, isRig ? "Edit Rest Position" : "Edit Key Position");
                }
                if (GUILayout.Button(new GUIContent("C", "Copy Position"), GUILayout.Width(22f), GUILayout.Height(18f)))
                {
                    _partsXformClipboardPos = pose.Position;
                    _partsXformClipboardHasPos = true;
                    _status = "Copied Position";
                }
                using (new EditorGUI.DisabledScope(!_partsXformClipboardHasPos))
                {
                    if (GUILayout.Button(new GUIContent("P", "Paste Position"), GUILayout.Width(22f), GUILayout.Height(18f)))
                    {
                        pose.Position = _partsXformClipboardPos;
                        CommitPartsTransformInspectorEdit(slot, pose, isRig, "Paste Position");
                    }
                }
                if (GUILayout.Button(new GUIContent("R", isRig ? "Reset Position to 0,0" : "Reset Position to rest"),
                        GUILayout.Width(22f), GUILayout.Height(18f)))
                {
                    pose.Position = isRig ? Vector2.zero : rest.Position;
                    CommitPartsTransformInspectorEdit(slot, pose, isRig, "Reset Position");
                }
                EditorGUILayout.EndHorizontal();

                // Rotation
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                float rot = EditorGUILayout.FloatField("Rotation", pose.Rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    pose.Rotation = rot;
                    CommitPartsTransformInspectorEdit(slot, pose, isRig, isRig ? "Edit Rest Rotation" : "Edit Key Rotation");
                }
                if (GUILayout.Button(new GUIContent("C", "Copy Rotation"), GUILayout.Width(22f), GUILayout.Height(18f)))
                {
                    _partsXformClipboardRot = pose.Rotation;
                    _partsXformClipboardHasRot = true;
                    _status = "Copied Rotation";
                }
                using (new EditorGUI.DisabledScope(!_partsXformClipboardHasRot))
                {
                    if (GUILayout.Button(new GUIContent("P", "Paste Rotation"), GUILayout.Width(22f), GUILayout.Height(18f)))
                    {
                        pose.Rotation = _partsXformClipboardRot;
                        CommitPartsTransformInspectorEdit(slot, pose, isRig, "Paste Rotation");
                    }
                }
                if (GUILayout.Button(new GUIContent("R", isRig ? "Reset Rotation to 0" : "Reset Rotation to rest"),
                        GUILayout.Width(22f), GUILayout.Height(18f)))
                {
                    pose.Rotation = isRig ? 0f : rest.Rotation;
                    CommitPartsTransformInspectorEdit(slot, pose, isRig, "Reset Rotation");
                }
                EditorGUILayout.EndHorizontal();

                // Scale (+ link toggle outside change-check so toggling Link alone does not rewrite pose)
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                Vector2 scale = EditorGUILayout.Vector2Field("Scale", pose.Scale);
                if (EditorGUI.EndChangeCheck())
                {
                    if (_partsLinkedScale)
                    {
                        float ax = Mathf.Abs(pose.Scale.x) > 1e-5f ? Mathf.Abs(pose.Scale.x) : 1f;
                        float ay = Mathf.Abs(pose.Scale.y) > 1e-5f ? Mathf.Abs(pose.Scale.y) : 1f;
                        bool xChanged = !Mathf.Approximately(scale.x, pose.Scale.x);
                        bool yChanged = !Mathf.Approximately(scale.y, pose.Scale.y);
                        if (xChanged && !yChanged)
                        {
                            float ratio = scale.x / ax;
                            scale.y = Mathf.Sign(pose.Scale.y == 0f ? 1f : pose.Scale.y) * ay * Mathf.Abs(ratio);
                        }
                        else if (yChanged && !xChanged)
                        {
                            float ratio = scale.y / ay;
                            scale.x = Mathf.Sign(pose.Scale.x == 0f ? 1f : pose.Scale.x) * ax * Mathf.Abs(ratio);
                        }
                    }
                    pose.Scale = scale;
                    CommitPartsTransformInspectorEdit(slot, pose, isRig, isRig ? "Edit Rest Scale" : "Edit Key Scale");
                }
                bool linked = GUILayout.Toggle(_partsLinkedScale, new GUIContent("Link", "Keep X/Y scale proportional when editing"),
                    GUILayout.Width(40f));
                if (linked != _partsLinkedScale)
                    _partsLinkedScale = linked;
                if (GUILayout.Button(new GUIContent("C", "Copy Scale"), GUILayout.Width(22f), GUILayout.Height(18f)))
                {
                    _partsXformClipboardScale = pose.Scale;
                    _partsXformClipboardHasScale = true;
                    _status = "Copied Scale";
                }
                using (new EditorGUI.DisabledScope(!_partsXformClipboardHasScale))
                {
                    if (GUILayout.Button(new GUIContent("P", "Paste Scale"), GUILayout.Width(22f), GUILayout.Height(18f)))
                    {
                        pose.Scale = _partsXformClipboardScale;
                        CommitPartsTransformInspectorEdit(slot, pose, isRig, "Paste Scale");
                    }
                }
                if (GUILayout.Button(new GUIContent("R", isRig ? "Reset Scale to 1,1" : "Reset Scale to rest"),
                        GUILayout.Width(22f), GUILayout.Height(18f)))
                {
                    pose.Scale = isRig ? Vector2.one : rest.Scale;
                    CommitPartsTransformInspectorEdit(slot, pose, isRig, "Reset Scale");
                }
                EditorGUILayout.EndHorizontal();

                DrawPartsShearRow(slot, isRig);
            }

            // Pivot is art registration on the joint - shown here with TRS for one-stop editing.
            using (new EditorGUI.DisabledScope(partLocked))
                DrawPartsAppearancePivotInspector(slot);

        }

        /// <summary>Shear (Spine): Rig edits the setup shear, Animate keys it (its own channel) at the playhead.</summary>
        void DrawPartsShearRow(SpritePartSlotDef slot, bool isRig)
        {
            Vector2 shown = isRig ? slot.RestShear : SampleShearForSlot(slot.SlotId, _partsPreviewTime);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            Vector2 shear = EditorGUILayout.Vector2Field(new GUIContent("Shear",
                "Tilt the part's X and Y axes (degrees): skew for squash, lean and smears"), shown);
            bool changed = EditorGUI.EndChangeCheck();
            if (GUILayout.Button(new GUIContent("R", isRig ? "Reset Shear to 0,0" : "Key the setup shear"),
                    GUILayout.Width(22f), GUILayout.Height(18f)))
            {
                shear = isRig ? Vector2.zero : slot.RestShear;
                changed = true;
            }
            EditorGUILayout.EndHorizontal();
            if (!changed || _partsPlaying)
                return;
            RecordPartsUndo(isRig ? "Edit Rest Shear" : "Key Shear");
            if (isRig)
                slot.RestShear = shear;
            else
                SpritePartsAuthoringOps.SetShearKey(_profile, _partsSelectedClip, slot.SlotId, _partsPreviewTime, shear);
            SaveDirty();
            Repaint();
        }

        Vector2 SampleShearForSlot(string slotId, float time)
        {
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, slotId);
            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), time, Allocator.Temp, true,
                    out var blob, out var poses, out var matrices, out _))
                return slot?.RestShear ?? Vector2.zero;
            try
            {
                int idx = BlobSlotIndex(ref blob.Value, slotId);
                return idx >= 0 && idx < poses.Length ? new Vector2(poses[idx].Shear.x, poses[idx].Shear.y) : slot?.RestShear ?? Vector2.zero;
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        void CommitPartsTransformInspectorEdit(
            SpritePartSlotDef slot, SpritePartsAuthoringOps.PoseEdit pose, bool isRig, string undoName)
        {
            if (slot == null || _partsDragActive) return;
            // Playing drives the fields every frame; committing would bake/flatten clip keys.
            if (_partsPlaying) return;
            if (isRig)
            {
                RecordPartsUndo(undoName);
                slot.RestPosition = pose.Position;
                slot.RestRotation = pose.Rotation;
                slot.RestScale = pose.Scale;
                SaveDirty();
                _status = "Rig: updated rest pose (affects every clip)";
            }
            else
            {
                BeginPartsDragUndo(undoName);
                ApplyPartsPoseEdit(slot.SlotId, pose);
                EndPartsDragUndo();
            }
            Repaint();
        }

        void DrawPartsZOrderInspector(SpritePartSlotDef slot)
        {
            if (slot == null) return;
            if (!PartsSection("Z ORDER / LAYER", "Rank " + slot.DrawRank))
                return;
            var list = SpritePartsAuthoringOps.GetSlotsSortedByDrawRank(_profile, frontFirst: true);
            int index = list.FindIndex(s => s != null &&
                SpritePartIdUtility.Canonical(s.SlotId) == SpritePartIdUtility.Canonical(slot.SlotId));
            string place = index == 0 ? "Front" : (index == list.Count - 1 ? "Back" : "Middle");
            EditorGUILayout.LabelField("Draw Rank", slot.DrawRank + "  (" + place + ", higher = in front)");
            EditorGUILayout.BeginHorizontal();
            string id = SpritePartIdUtility.Canonical(slot.SlotId);
            if (GUILayout.Button("To Front", GUILayout.Height(20f)))
                MovePartsLayer(id, 0);
            if (GUILayout.Button("Forward", GUILayout.Height(20f)))
                NudgePartsLayer(id, -1);
            if (GUILayout.Button("Backward", GUILayout.Height(20f)))
                NudgePartsLayer(id, +1);
            if (GUILayout.Button("To Back", GUILayout.Height(20f)))
                MovePartsLayer(id, Mathf.Max(0, list.Count - 1));
            EditorGUILayout.EndHorizontal();
        }

        void SyncPartsArtDraft(SpritePartSlotDef slot)
        {
            string sid = SpritePartIdUtility.Canonical(slot.SlotId);
            if (_partsArtSyncedSlotId == sid) return;
            _partsArtSyncedSlotId = sid;
            _partsArtColumns = 1;
            _partsArtRows = 1;
            _partsArtCell = 0;
            var app = SpritePartsAuthoringOps.FindAppearance(_profile, slot.DefaultAppearanceId);
            if (app == null) return;
            var sheet = _profile.SheetAt(app.SheetIndex);
            _partsArtColumns = sheet != null ? Mathf.Max(1, sheet.Columns) : 1;
            _partsArtRows = sheet != null ? Mathf.Max(1, sheet.Rows) : 1;
            int count = _partsArtColumns * _partsArtRows;
            _partsArtCell = count > 0 ? ((app.CellIndex % count) + count) % count : 0;
        }


        /// <summary>
        /// Pivot is art-relative (where the sprite hangs on the joint). Edited from TRANSFORM
        /// so registration sits next to Position/Rotation/Scale; data still lives on the appearance.
        /// SheetDefault / Cell use sheet data; Override stores a normalized 0-1 UV.
        /// </summary>
        void DrawPartsAppearancePivotInspector(SpritePartSlotDef slot)
        {
            if (slot == null || _profile == null) return;

            GUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Pivot", EditorStyles.boldLabel, GUILayout.Width(48f));
            if (GUILayout.Button(new GUIContent("Focus", "Double-click the part on the canvas, or click here, to drag its pivot."),
                    GUILayout.Width(52f), GUILayout.Height(18f)))
                EnterPartsPivotFocus(slot.SlotId);
            EditorGUILayout.EndHorizontal();

            var app = SpritePartsAuthoringOps.FindAppearance(_profile, slot.DefaultAppearanceId);
            if (app == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign art under SPRITE / ART first. Pivot is stored on the appearance (where the sprite hangs on this joint).",
                    MessageType.Info);
                return;
            }

            var sheet = _profile.SheetAt(app.SheetIndex);


            EditorGUI.BeginChangeCheck();
            var source = (SpritePartPivotSource)EditorGUILayout.EnumPopup(
                new GUIContent("Pivot Source",
                    "Sheet Default = sheet pivot. Cell = per-cell pivot when authored. Override = custom 0-1 UV on this appearance."),
                app.PivotSource);
            if (EditorGUI.EndChangeCheck())
            {
                RecordPartsUndo("Set Appearance Pivot Source");
                app.PivotSource = source;
                SaveDirty();
            }

            var resolved = SpritePartsGeometry.ResolvePivot(sheet, app);
            EditorGUILayout.LabelField(
                "Resolved",
                $"{resolved.x:0.###}, {resolved.y:0.###}  (0-1, bottom-left origin)");

            using (new EditorGUI.DisabledScope(app.PivotSource != SpritePartPivotSource.Override))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                Vector2 ov = EditorGUILayout.Vector2Field(
                    new GUIContent("Override", "Normalized pivot on the sprite quad. (0.5, 0.5) = center."),
                    app.PivotOverride);
                if (EditorGUI.EndChangeCheck())
                {
                    RecordPartsUndo("Set Appearance Pivot Override");
                    app.PivotOverride = new Vector2(
                        Mathf.Clamp01(ov.x),
                        Mathf.Clamp01(ov.y));
                    SaveDirty();
                }
                if (GUILayout.Button(new GUIContent("C", "Copy override pivot"), GUILayout.Width(22f), GUILayout.Height(18f)))
                {
                    _partsPivotClipboard = app.PivotOverride;
                    _partsPivotClipboardValid = true;
                    _status = "Copied pivot override";
                }
                using (new EditorGUI.DisabledScope(!_partsPivotClipboardValid))
                {
                    if (GUILayout.Button(new GUIContent("P", "Paste into override"), GUILayout.Width(22f), GUILayout.Height(18f)))
                    {
                        RecordPartsUndo("Paste Appearance Pivot");
                        app.PivotSource = SpritePartPivotSource.Override;
                        app.PivotOverride = new Vector2(
                            Mathf.Clamp01(_partsPivotClipboard.x),
                            Mathf.Clamp01(_partsPivotClipboard.y));
                        SaveDirty();
                        _status = "Pasted pivot override";
                    }
                }
                if (GUILayout.Button(new GUIContent("R", "Reset override to center (0.5, 0.5)"), GUILayout.Width(22f), GUILayout.Height(18f)))
                {
                    RecordPartsUndo("Reset Appearance Pivot");
                    app.PivotSource = SpritePartPivotSource.Override;
                    app.PivotOverride = new Vector2(0.5f, 0.5f);
                    SaveDirty();
                    _status = "Reset pivot override to center";
                }
                EditorGUILayout.EndHorizontal();
            }

            if (app.PivotSource != SpritePartPivotSource.Override)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(
                        new GUIContent("Make Override from Resolved",
                            "Copy the current resolved pivot into Override so you can tweak it."),
                        GUILayout.Height(18f)))
                {
                    RecordPartsUndo("Override Pivot from Resolved");
                    app.PivotSource = SpritePartPivotSource.Override;
                    app.PivotOverride = new Vector2(
                        Mathf.Clamp01(resolved.x),
                        Mathf.Clamp01(resolved.y));
                    SaveDirty();
                    _status = "Pivot source set to Override from resolved";
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        void DrawPartsArtInspector(SpritePartSlotDef slot)
        {
            if (slot == null || _profile == null) return;
            SyncPartsArtDraft(slot);
            if (!PartsSection("SPRITE / ART", slot.DefaultAppearanceId))
                return;

            var app = SpritePartsAuthoringOps.FindAppearance(_profile, slot.DefaultAppearanceId);
            var sheet = app != null ? _profile.SheetAt(app.SheetIndex) : null;
            Texture2D tex = sheet?.Texture;

            EditorGUI.BeginChangeCheck();
            var nextTex = (Texture2D)EditorGUILayout.ObjectField("Texture", tex, typeof(Texture2D), false);
            _partsArtColumns = Mathf.Max(1, EditorGUILayout.IntField("Columns", _partsArtColumns));
            _partsArtRows = Mathf.Max(1, EditorGUILayout.IntField("Rows", _partsArtRows));
            int cellCount = Mathf.Max(1, _partsArtColumns * _partsArtRows);
            _partsArtCell = Mathf.Clamp(EditorGUILayout.IntField("Cell (0 = first)", _partsArtCell), 0, cellCount - 1);
            bool changed = EditorGUI.EndChangeCheck();

            // Pixels Per Unit under Cell; drives pose-canvas / bake size when LogicalWorldSize is zero.
            if (!string.IsNullOrEmpty(slot.DefaultAppearanceId))
                DrawPartsAppearancePixelsPerUnit(slot.DefaultAppearanceId);

            if (nextTex != null)
            {
                var preview = GUILayoutUtility.GetRect(72f, 72f, GUILayout.ExpandWidth(false));
                DrawPartsSheetCell(nextTex, sheet, _partsArtColumns, _partsArtRows, _partsArtCell, preview, Color.white);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Pick a texture. 1x1 uses the whole image as this part. Columns x Rows + Cell picks a sheet cell. Load Profile (toolbar) is for .asset profiles, not part art.",
                    MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Browse...", GUILayout.Height(20f)))
            {
                int pickerId = GUIUtility.GetControlID(FocusType.Passive);
                EditorGUIUtility.ShowObjectPicker<Texture2D>(nextTex, false, "t:Texture2D", pickerId);
            }
            using (new EditorGUI.DisabledScope(nextTex == null))
            {
                if (GUILayout.Button("Apply to Part", GUILayout.Height(20f)))
                    ApplyPartsSlotArt(slot, nextTex);
            }
            if (!string.IsNullOrEmpty(slot.DefaultAppearanceId) &&
                GUILayout.Button("Clear", GUILayout.Width(52f), GUILayout.Height(20f)))
            {
                RecordPartsUndo("Clear Part Art");
                slot.DefaultAppearanceId = string.Empty;
                SaveDirty();
                _status = "Cleared part art";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("From This Profile",
                    "Bind art already in this profile: appearances, sheet cells, or one static frame of a frame clip."), GUILayout.Height(20f)))
            {
                var anchor = GUILayoutUtility.GetLastRect();
                PopupWindow.Show(anchor, new SpritePartsArtPickerPopup(this, slot.SlotId));
            }
            if (GUILayout.Button(new GUIContent("Import from Profile...",
                    "Copy art, frame clips, and Parts clips from another profile as local copies. Texture assets are reused."), GUILayout.Height(20f)))
            {
                var anchor = GUILayoutUtility.GetLastRect();
                PopupWindow.Show(anchor, new SpriteProfileImportPopup(this));
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button(new GUIContent("Import Frame Sequence to Part...",
                    "Bake a frame clip cell sequence into appearance keys on this slot. Pose stays at rest; empty pose track required."), GUILayout.Height(20f)))
            {
                var anchor = GUILayoutUtility.GetLastRect();
                PopupWindow.Show(anchor, new SpritePartsFrameSequencePopup(this, slot.SlotId));
            }

            if (Event.current.commandName == "ObjectSelectorClosed")
            {
                var picked = EditorGUIUtility.GetObjectPickerObject() as Texture2D;
                if (picked != null)
                    ApplyPartsSlotArt(slot, picked);
            }

            if (changed && nextTex != null)
                ApplyPartsSlotArt(slot, nextTex);

            if (!string.IsNullOrEmpty(slot.DefaultAppearanceId))
            {
                EditorGUILayout.LabelField("Appearance Id", slot.DefaultAppearanceId);
                DrawImportProvenanceLabel(app?.Import);
                if (app != null)
                {
                    EditorGUI.BeginChangeCheck();
                    var nextRole = (SpritePartSemanticRole)EditorGUILayout.EnumPopup(
                        new GUIContent("Appearance Role",
                            "Optional. Apply Outfit uses this when set; otherwise the slot role."),
                        app.SemanticRole);
                    if (EditorGUI.EndChangeCheck())
                    {
                        RecordPartsUndo("Set Appearance Semantic Role");
                        app.SemanticRole = nextRole;
                        SaveDirty();
                    }
                }
            }
        }

        /// <summary>
        /// Display-only provenance line for imported/copied items. Never
        /// resolves or refreshes anything; absent on old data.
        /// </summary>
        void DrawImportProvenanceLabel(SpriteImportProvenance provenance)
        {
            if (provenance == null ||
                (string.IsNullOrEmpty(provenance.SourceItem) &&
                 string.IsNullOrEmpty(provenance.SourceGuid) &&
                 string.IsNullOrEmpty(provenance.SourcePath)))
                return;
            string origin = !string.IsNullOrEmpty(provenance.SourcePath)
                ? provenance.SourcePath
                : provenance.SourceGuid;
            string when = string.IsNullOrEmpty(provenance.ImportedUtc)
                ? string.Empty
                : " | " + provenance.ImportedUtc;
            bool fromLibrary = SpriteArtLibraryOps.HasLibraryProvenance(provenance);
            EditorGUILayout.LabelField(
                fromLibrary ? "From Library" : "Imported From",
                provenance.SourceItem + (string.IsNullOrEmpty(origin) ? string.Empty : " | " + origin) + when,
                EditorStyles.wordWrappedMiniLabel);
        }

        void ApplyPartsSlotArt(SpritePartSlotDef slot, Texture2D tex)
        {
            if (slot == null || tex == null || _profile == null) return;
            RecordPartsUndo("Assign Part Art");
            var result = SpritePartsAuthoringOps.AssignSlotArt(
                _profile, slot.SlotId, tex, _partsArtColumns, _partsArtRows, _partsArtCell);
            if (!result.Ok)
            {
                _status = result.Reason ?? "Assign art failed";
                return;
            }
            _status = $"Bound {tex.name} cell {result.CellIndex} -> {result.AppearanceId}";
            SaveDirty();
            Repaint();
        }

        /// <summary>
        /// Import art from another profile into the open document. Adds content;
        /// never replaces the editing document (that is Open job). One Undo
        /// transaction; a failed apply changes nothing.
        /// </summary>
        internal void ImportArtFromProfile(
            ScriptableSpriteSheetProfile sourceAsset, List<int> sheetIndices, List<int> appearanceIndices)
        {
            ImportFromProfile(sourceAsset, sheetIndices, appearanceIndices, null, null, null, null);
        }

        /// <summary>
        /// Import art and/or clips from another profile. One Undo transaction;
        /// a failed apply rolls the snapshot back. Conflict policy defaults to
        /// unique copies; ReplaceExisting keeps destination ids/names and
        /// overwrites content (previewed in the popup before commit).
        /// </summary>
        internal void ImportFromProfile(
            ScriptableSpriteSheetProfile sourceAsset,
            List<int> sheetIndices,
            List<int> appearanceIndices,
            List<int> frameClipIndices,
            List<int> partsClipIndices,
            Dictionary<string, string> slotMap,
            HashSet<string> excludedSlots,
            SpriteProfileImportConflictPolicy conflictPolicy = SpriteProfileImportConflictPolicy.UniqueCopy)
        {
            if (sourceAsset?.Data == null || _profile == null)
                return;
            if (ReferenceEquals(sourceAsset.Data, _profile))
            {
                _status = "Source profile is the open profile. Use 'From This Profile' instead.";
                return;
            }
            ClearImportPreview();
            var plan = SpriteProfileClipImport.PlanImport(
                sourceAsset.Data, _profile,
                sheetIndices, appearanceIndices,
                frameClipIndices, partsClipIndices,
                slotMap, excludedSlots, conflictPolicy);
            if (!plan.Ok)
            {
                _status = plan.Reason;
                EditorUtility.DisplayDialog("Import from Profile", plan.Reason, "OK");
                return;
            }
            RecordPartsUndo("Import from " + sourceAsset.name);
            string sourceAssetPath = AssetDatabase.GetAssetPath(sourceAsset);
            var result = SpriteProfileClipImport.Apply(plan, sourceAsset.Data, _profile,
                new SpriteProfileArtImport.ImportSourceInfo
                {
                    Guid = AssetDatabase.AssetPathToGUID(sourceAssetPath) ?? string.Empty,
                    Path = sourceAssetPath ?? string.Empty,
                });
            if (!result.Ok)
            {
                Undo.PerformUndo();
                _status = result.Reason ?? "Import failed";
                return;
            }
            SaveDirty();
            _status = "Imported from " + sourceAsset.name + ": " + result.Summary + ". Use 'From This Profile' to bind.";
            Repaint();
        }

        /// <summary>
        /// Bake a frame clip cell sequence into appearance keys on a Parts track.
        /// </summary>
        internal void ImportFrameSequenceToPart(
            int frameClipIndex, int partsClipIndex, string slotId, float startTime, bool extendDuration,
            int repeatCount = 1)
        {
            if (_profile == null) return;
            var plan = SpritePartsFrameSequenceImport.PlanImport(
                _profile, frameClipIndex, partsClipIndex, slotId, startTime, extendDuration, repeatCount);
            if (!plan.Ok)
            {
                _status = plan.Reason;
                EditorUtility.DisplayDialog("Import Frame Sequence to Part", plan.Reason, "OK");
                return;
            }
            if (plan.ExcludedFeatures.Count > 0)
            {
                string details = string.Join(Environment.NewLine + "- ", plan.ExcludedFeatures.ToArray());
                if (!EditorUtility.DisplayDialog(
                    "Import Frame Sequence to Part",
                    "This converts sprite appearance only." + Environment.NewLine + Environment.NewLine +
                    "Not imported:" + Environment.NewLine + "- " + details + Environment.NewLine + Environment.NewLine +
                    "Continue with art-only keys?",
                    "Import Art Only", "Cancel"))
                    return;
            }
            RecordPartsUndo("Import Frame Sequence to Part");
            string ownAssetPath = _asset != null ? AssetDatabase.GetAssetPath(_asset) : string.Empty;
            var result = SpritePartsFrameSequenceImport.Apply(plan, _profile,
                new SpriteProfileArtImport.ImportSourceInfo
                {
                    Guid = string.IsNullOrEmpty(ownAssetPath)
                        ? string.Empty
                        : AssetDatabase.AssetPathToGUID(ownAssetPath) ?? string.Empty,
                    Path = ownAssetPath ?? string.Empty,
                });
            if (!result.Ok)
            {
                Undo.PerformUndo();
                _status = result.Reason ?? "Sequence import failed";
                return;
            }
            SaveDirty();
            _status = result.Summary;
            Repaint();
        }

        internal void BindSlotAppearanceFromPicker(string slotId, string appearanceId)
        {
            if (_profile == null) return;
            RecordPartsUndo("Bind Part Appearance");
            var result = SpritePartsAuthoringOps.SetSlotDefaultAppearance(_profile, slotId, appearanceId);
            if (!result.Ok)
            {
                _status = result.Reason;
                return;
            }
            _status = $"Bound appearance '{result.AppearanceId}'";
            SaveDirty();
            Repaint();
        }

        /// <summary>
        /// From This Profile batch mode: create one appearance per selected
        /// cell (exact sheet+cell matches reused) in one Undo; optionally bind
        /// the last selected cell's appearance as the slot's default art.
        /// </summary>
        internal void CreateAppearancesFromPicker(
            string slotId, int sheetIndex, List<int> cells, bool bindLast)
        {
            if (_profile == null || cells == null || cells.Count == 0) return;
            RecordPartsUndo("Batch Create Appearances");
            var result = SpritePartsAuthoringOps.CreateAppearancesForCells(_profile, sheetIndex, cells);
            if (!result.Ok)
            {
                _status = result.Reason;
                return;
            }
            string bound = string.Empty;
            if (bindLast && !string.IsNullOrEmpty(slotId) && result.AppearanceIds.Count > 0)
            {
                string lastId = result.AppearanceIds[result.AppearanceIds.Count - 1];
                var bind = SpritePartsAuthoringOps.SetSlotDefaultAppearance(_profile, slotId, lastId);
                if (bind.Ok)
                    bound = ", bound '" + lastId + "' as default";
            }
            SaveDirty();
            _status = $"Created {result.Created}, reused {result.Reused} appearance(s){bound}";
            Repaint();
        }

        internal void BindSlotArtCellFromPicker(string slotId, int sheetIndex, int cellIndex, string statusLabel)
        {
            if (_profile == null) return;
            RecordPartsUndo("Bind Part Art Cell");
            var result = SpritePartsAuthoringOps.BindSlotArtFromCell(_profile, slotId, sheetIndex, cellIndex);
            if (!result.Ok)
            {
                _status = result.Reason;
                return;
            }
            _status = statusLabel;
            SaveDirty();
            Repaint();
        }

        void DrawPartsSheetCell(
            Texture2D tex, SpriteSheetDef sheet, int columns, int rows, int cell, Rect rect, Color tint)
        {
            if (tex == null) return;
            Rect uv;
            if (sheet != null && sheet.Texture == tex)
                uv = SpriteSheetProfile.GetCellUvRect(sheet, cell);
            else
                uv = SpriteSheetProfile.GetUniformCellUvRect(Mathf.Max(1, columns), Mathf.Max(1, rows), cell);
            Color previous = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, tex, uv, true);
            GUI.color = previous;
        }

        string DrawParentPopup(SpritePartSlotDef slot)
        {
            var options = new List<string> { "(Character root)" };
            var ids = new List<string> { string.Empty };
            string current = slot.ParentSlotId ?? string.Empty;
            int selected = 0;
            for (int i = 0; i < _profile.PartsSlots.Count; i++)
            {
                var s = _profile.PartsSlots[i];
                if (s == null || ReferenceEquals(s, slot)) continue;
                options.Add(s.Name + " (" + s.SlotId + ")");
                ids.Add(s.SlotId);
                if (SpritePartIdUtility.Canonical(s.SlotId) == SpritePartIdUtility.Canonical(current))
                    selected = ids.Count - 1;
            }
            int next = EditorGUILayout.Popup("Parent", selected, options.ToArray());
            return ids[Mathf.Clamp(next, 0, ids.Count - 1)];
        }

        void DrawPartsSkinsInspector()
        {
            if (!PartsSection("SKINS", _profile.PartsSkins.Count + " skin" + (_profile.PartsSkins.Count == 1 ? "" : "s")))
                return;
            var names = new List<string>();
            int selected = 0;
            for (int i = 0; i < _profile.PartsSkins.Count; i++)
            {
                var skin = _profile.PartsSkins[i];
                if (skin == null) continue;
                names.Add(skin.Name);
                if (SpritePartIdUtility.Canonical(skin.SkinId) ==
                    SpritePartIdUtility.Canonical(_partsPreviewSkinId))
                    selected = names.Count - 1;
            }
            if (names.Count == 0)
            {
                if (GUILayout.Button("+ Default Skin"))
                {
                    RecordPartsUndo("Add Parts Skin");
                    _profile.PartsSkins.Add(new SpritePartsSkinDef
                    {
                        Name = "Default", SkinId = "default",
                        Bindings = new List<SpritePartsSkinBindingDef>(),
                    });
                    SaveDirty();
                }
                return;
            }

            int next = EditorGUILayout.Popup("Preview Skin", selected, names.ToArray());
            if (next != selected)
            {
                _partsPreviewSkinId = _profile.PartsSkins[next].SkinId;
                _partsSkinPreviewOverrides.Clear();
                var preview = SpritePartsAuthoringOps.ApplySkinPreview(_profile, _partsPreviewSkinId);
                foreach (var kv in preview)
                    _partsSkinPreviewOverrides[kv.Key] = kv.Value;
            }

            var slot = CurrentPartsSlot;
            if (slot != null)
            {
                string currentApp = SpritePartsAuthoringOps.ResolveAppearanceId(slot, _partsSkinPreviewOverrides);
                EditorGUI.BeginChangeCheck();
                string app = EditorGUILayout.TextField("Appearance Id (preview)", currentApp);
                if (EditorGUI.EndChangeCheck())
                {
                    // Transient preview does not serialize until Save Skin.
                    _partsSkinPreviewOverrides[SpritePartIdUtility.Canonical(slot.SlotId)] =
                        SpritePartIdUtility.Canonical(app);
                    _status = "Skin preview (unsaved)";
                }
                DrawPartsAppearancePixelsPerUnit(currentApp);
            }

            if (GUILayout.Button("Save Skin"))
            {
                RecordPartsUndo("Save Parts Skin");
                SpritePartsAuthoringOps.SaveSkinFromPreview(
                    _profile, _partsPreviewSkinId, _partsPreviewSkinId, _partsSkinPreviewOverrides);
                SaveDirty();
                _status = "Saved skin " + _partsPreviewSkinId;
            }
            if (GUILayout.Button("Clear Preview Overrides"))
            {
                _partsSkinPreviewOverrides.Clear();
                _status = "Cleared skin preview";
            }
            GUILayout.Space(6f);
            if (GUILayout.Button(new GUIContent("Apply Outfit...",
                    "Map a source skin/outfit onto this profile by semantic role (Body/Head/Weapon/Offhand). Appearance binding only - not motion retargeting."),
                GUILayout.Height(22f)))
            {
                var anchor = GUILayoutUtility.GetLastRect();
                PopupWindow.Show(anchor, new SpritePartsOutfitPopup(this));
            }
        }

        void DrawPartsAppearancePixelsPerUnit(string appearanceId)
        {
            var appearance = SpritePartsAuthoringOps.FindAppearance(_profile, appearanceId);
            var sheet = appearance != null ? _profile.SheetAt(appearance.SheetIndex) : null;
            if (sheet == null)
                return;

            float current = SpriteSheetProfile.GetPixelsPerUnit(sheet);
            EditorGUI.BeginChangeCheck();
            float next = Mathf.Max(SpriteSheetProfile.MinPixelsPerUnit,
                EditorGUILayout.FloatField(
                    new GUIContent("Pixels Per Unit",
                        "World size of the selected cell = cell pixels / PPU. Higher = smaller on canvas and in scene. Shared by appearances on this sheet. Save Profile to rebake scene characters."),
                    current));
            if (EditorGUI.EndChangeCheck() && !Mathf.Approximately(next, current))
            {
                RecordPartsUndo("Set Parts Pixels Per Unit");
                sheet.PixelsPerUnit = next;
                // Explicit world size overrides PPU; clear so the new PPU drives canvas / bake size.
                if (appearance.LogicalWorldSize != Vector2.zero)
                    appearance.LogicalWorldSize = Vector2.zero;
                SaveDirty();
                Repaint();
                _status = $"Parts sheet '{sheet.Name}' set to {next:g} PPU. Save Profile to update the scene.";
            }

            if (appearance.LogicalWorldSize != Vector2.zero)
            {
                EditorGUILayout.HelpBox(
                    "This appearance has an explicit World Size, so Pixels Per Unit does not control its size.",
                    MessageType.Info);
            }
            else if (SpriteSheetProfile.TryGetActiveCellPixels(
                         sheet, appearance.CellIndex, out float cellW, out float cellH))
            {
                EditorGUILayout.LabelField(
                    "Scene Size",
                    $"{cellW / next:0.###} x {cellH / next:0.###} units");
            }
        }

        void DrawPartsPreview(Rect rect, int partsCanvasControlId)
        {
            EnsurePartsSession();
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, 120f, 20f), "POSE CANVAS", _sectionStyle);

            // Tool row
            float tx = rect.x + 12f;
            float ty = rect.y + 34f;
            // Tool strip sits above the canvas. A leftover canvas hotControl makes Toggle miss clicks.
            {
                Event tev = Event.current;
                Rect toolStrip = new Rect(rect.x, rect.y, rect.width, 84f);
                if (tev.type == EventType.MouseDown
                    && tev.button == 0
                    && toolStrip.Contains(tev.mousePosition))
                    ReleasePartsCanvasCapture();
            }
            DrawPartsToolToggle(ref tx, ty, "Q Move", PartsCanvasTool.Move);
            DrawPartsToolToggle(ref tx, ty, "W Rotate", PartsCanvasTool.Rotate);
            DrawPartsToolToggle(ref tx, ty, "E Scale", PartsCanvasTool.Scale);
            DrawPartsToolToggle(ref tx, ty, "R Warp", PartsCanvasTool.Warp);
            if (_partsCanvasTool == PartsCanvasTool.Warp && _partsMode == SpritePartsStudioMode.Animate)
            {
                if (GUI.Button(new Rect(tx, ty, 72f, 20f), new GUIContent("Edit Mesh", "Setup mesh of the selected part: hull, interior vertices, edges. Same as double-clicking the part.")))
                {
                    if (CurrentPartsSlot != null)
                        EnterPartsMeshEdit(CurrentPartsSlot.SlotId);
                }
                tx += 74f;
                if (GUI.Button(new Rect(tx, ty, 90f, 20f), new GUIContent("Reset Deform", "Selected vertices, or all of them, back to the setup mesh on this key.")))
                    ResetSelectedPartsDeform();
                tx += 94f;
                _partsSoftSelect = GUI.Toggle(new Rect(tx, ty, 48f, 20f), _partsSoftSelect,
                    new GUIContent("Soft", "Soft selection: neighbours follow with a falloff. Size / Feather in the inspector."));
                tx += 52f;
                // What dragging a vertex selection does (also in the Mesh panel). One vertex always moves.
                var vt = (PartsVertexTool)GUI.Toolbar(new Rect(tx, ty, 168f, 20f), (int)_partsVertexTool,
                    new[]
                    {
                        new GUIContent("Move", "Drag moves the selected vertices."),
                        new GUIContent("Rotate", "Drag turns 2+ selected vertices around their centre."),
                        new GUIContent("Scale", "Drag grows / shrinks 2+ selected vertices from their centre."),
                    });
                if (vt != _partsVertexTool)
                {
                    _partsVertexTool = vt;
                    Repaint();
                }
                tx += 172f;
                if (PartsFfdActive())
                {
                    if (GUI.Button(new Rect(tx, ty, 58f, 20f), new GUIContent("Apply", "Keep the FFD result (Enter)."), _primaryStyle))
                        ApplyPartsFfd();
                    tx += 60f;
                    if (GUI.Button(new Rect(tx, ty, 50f, 20f), new GUIContent("Reset", "Back to how the vertices were before this FFD; stay in FFD.")))
                        ResetPartsFfd();
                    tx += 52f;
                    if (GUI.Button(new Rect(tx, ty, 58f, 20f), new GUIContent("Cancel", "Reset and leave FFD (Esc).")))
                        CancelPartsFfd();
                    tx += 62f;
                }
                else
                {
                    if (GUI.Button(new Rect(tx, ty, 44f, 20f), new GUIContent("FFD",
                            "Free Form Deformation: a grid of points around the selected vertices (or the whole mesh). " +
                            "Drag a point to bend everything inside. Grid size in the Mesh panel.")))
                        BeginPartsFfd();
                    tx += 48f;
                }
            }
            if (_partsCanvasTool == PartsCanvasTool.Move && _partsMode == SpritePartsStudioMode.Animate)
            {
                _partsIkOn = GUI.Toggle(new Rect(tx, ty, 40f, 20f), _partsIkOn,
                    new GUIContent("IK", "Drag a part: it and its parents turn so the grabbed point follows the mouse (Auto Key)."));
                tx += 42f;
                using (new EditorGUI.DisabledScope(!_partsIkOn))
                {
                    GUI.Label(new Rect(tx, ty + 2f, 40f, 18f), "Chain", _mutedStyle);
                    _partsIkChain = Mathf.Clamp(EditorGUI.IntField(new Rect(tx + 40f, ty + 1f, 26f, 18f),
                        new GUIContent(string.Empty, "How many joints turn: 1 = the part only, 2 = part + parent (elbow + shoulder), ..."),
                        _partsIkChain), 1, 6);
                }
                tx += 72f;
            }
            if (_partsCanvasTool == PartsCanvasTool.Move || _partsCanvasTool == PartsCanvasTool.Rotate)
            {
                _partsGizmoLocal = GUI.Toggle(new Rect(tx, ty, 58f, 20f), _partsGizmoLocal,
                    new GUIContent(_partsGizmoLocal ? "Local" : "Global",
                        "Gizmo space. Local follows the part; Global uses world axes."));
                tx += 62f;
            }
            _partsLinkedScale = GUI.Toggle(new Rect(tx, ty, 70f, 20f), _partsLinkedScale, "Link XY");
            tx += 74f;
            bool lockX = GUI.Toggle(new Rect(tx, ty, 58f, 20f), _partsLockMoveX,
                new GUIContent("Lock X", "Only vertical move (block X). Wins over Shift."));
            tx += 60f;
            bool lockY = GUI.Toggle(new Rect(tx, ty, 58f, 20f), _partsLockMoveY,
                new GUIContent("Lock Y", "Only horizontal move (block Y). Wins over Shift."));
            if (lockX != _partsLockMoveX)
            {
                _partsLockMoveX = lockX;
                if (lockX) _partsLockMoveY = false;
            }
            if (lockY != _partsLockMoveY)
            {
                _partsLockMoveY = lockY;
                if (lockY) _partsLockMoveX = false;
            }

            // Onion amount stays in the header; show/hide toggles sit on the canvas.
            float ox = rect.x + 12f;
            float oy = rect.y + 58f;
            using (new EditorGUI.DisabledScope(_partsMode == SpritePartsStudioMode.Rig))
            {
                GUI.Label(new Rect(ox, oy, 44f, 18f), "Before", _mutedStyle);
                ox += 46f;
                _partsOnionBefore = Mathf.Clamp(Mathf.RoundToInt(
                    GUI.HorizontalSlider(new Rect(ox, oy + 4f, 40f, 14f), _partsOnionBefore, 0f, 3f)), 0, 3);
                ox += 48f;
                GUI.Label(new Rect(ox, oy, 36f, 18f), "After", _mutedStyle);
                ox += 38f;
                _partsOnionAfter = Mathf.Clamp(Mathf.RoundToInt(
                    GUI.HorizontalSlider(new Rect(ox, oy + 4f, 40f, 14f), _partsOnionAfter, 0f, 3f)), 0, 3);
            }

            ox += 56f;
            DrawPartsRootBoundsToolbar(ox, oy);

            DrawPartsCanvasZoomToolbar(rect);

            var canvas = new Rect(rect.x + 10f, rect.y + 84f, rect.width - 20f, rect.height - 96f);
            var meshPanel = PartsMeshPanelRect(canvas);
            var overlay = PartsCanvasVisibilityOverlayRect(canvas);
            var dialBar = PartsDialBarRect(canvas);
            Event overlayEvt = Event.current;
            if (overlayEvt.type == EventType.MouseDown
                && overlayEvt.button == 0
                && (overlay.Contains(overlayEvt.mousePosition) || meshPanel.Contains(overlayEvt.mousePosition)
                    || dialBar.Contains(overlayEvt.mousePosition)))
                ReleasePartsCanvasCapture();

            EditorGUI.DrawRect(canvas, new Color(0.07f, 0.08f, 0.1f));
            // Input in window space; draw clipped so art cannot spill into the timeline.
            if (_partsMarqueeActive || _partsDragActive ||
                (!overlay.Contains(Event.current.mousePosition) && !meshPanel.Contains(Event.current.mousePosition)
                 && !dialBar.Contains(Event.current.mousePosition)))
                HandlePartsCanvasInput(canvas, partsCanvasControlId);
            GUI.BeginClip(canvas);
            try
            {
                DrawPartsCanvasContents(new Rect(0f, 0f, canvas.width, canvas.height));
            }
            finally
            {
                GUI.EndClip();
            }
            if (Event.current.type == EventType.Repaint && !IsPartsMeshEdit() && !IsPartsPivotFocus())
                DrawPartsPoseWarpOverlay(canvas);

            DrawPartsMarquee(canvas);
            DrawPartsWarpBox(canvas);
            DrawPartsCanvasVisibilityOverlay(overlay);
            DrawPartsMeshPanel(meshPanel);
            DrawPartsDialBar(canvas);
            DrawPartsIkOverlay();
            DrawPartsIkTargets(canvas);
            DrawPartsEventFlash(canvas);
        }

        /// <summary>Warp and Edit Mesh swap Onion / Debug / Root for what matters there: vertices, lines, triangles, FFD.</summary>
        bool PartsMeshDisplayOverlay()
            => IsPartsMeshEdit() || (_partsCanvasTool == PartsCanvasTool.Warp && _partsMode == SpritePartsStudioMode.Animate);

        Rect PartsCanvasVisibilityOverlayRect(Rect canvas)
        {
            const float pad = 8f;
            const float h = 24f;
            float w = PartsMeshDisplayOverlay() ? 390f : 296f;
            return new Rect(canvas.xMax - w - pad, canvas.yMax - h - pad, w, h);
        }

        void DrawPartsCanvasVisibilityOverlay(Rect overlay)
        {
            EditorGUI.DrawRect(overlay, new Color(0.08f, 0.09f, 0.12f, 0.92f));
            float x = overlay.x + 2f;
            float y = overlay.y + 1f;
            if (PartsMeshDisplayOverlay())
            {
                DrawPartsVisibilityToggle(ref x, y, 44f, ref _partsShowArt,
                    "Art", "Show or hide the current pose sprites.");
                DrawPartsVisibilityToggle(ref x, y, 70f, ref _partsWarpShowVerts,
                    "Vertices", "Show the mesh vertices (Warp).");
                DrawPartsVisibilityToggle(ref x, y, 50f, ref _partsWarpShowLines,
                    "Lines", "Show the outline and the edges you drew.");
                DrawPartsVisibilityToggle(ref x, y, 78f, ref _partsMeshShowHiddenLines,
                    "Triangles", "Show the yellow hidden lines Make Polygons adds inside your outline (AnyPortrait's hidden edges).");
                DrawPartsVisibilityToggle(ref x, y, 44f, ref _partsShowFfd,
                    "FFD", "Show the FFD grid while FFD is on.");
                DrawPartsVisibilityToggle(ref x, y, 54f, ref _partsDeformOnion,
                    "Ghost", "Warp: the previous (blue) and next (green) deform key's mesh as faint wireframes.");
                return;
            }
            using (new EditorGUI.DisabledScope(_partsMode == SpritePartsStudioMode.Rig))
            {
                DrawPartsVisibilityToggle(ref x, y, 62f, ref _partsOnionEnabled,
                    "Onion", "Show previous/next pose ghosts on the canvas.");
            }
            DrawPartsVisibilityToggle(ref x, y, 44f, ref _partsShowArt,
                "Art", "Show or hide the current pose sprites.");
            DrawPartsVisibilityToggle(ref x, y, 58f, ref _partsShowDebug,
                "Debug", "Show or hide part names, onion labels, and transform gizmos.");
            DrawPartsVisibilityToggle(ref x, y, 48f, ref _partsShowRoot,
                "Root", "Show the character origin square (same pivot as the scene GameObject).");
            DrawPartsVisibilityToggle(ref x, y, 48f, ref _partsShowAxes,
                "Axes", "Show the X / Y axes through the character origin (the middle), like Spine.");
        }

        /// <summary>The X (red) and Y (green) axes through the character origin, across the whole canvas.</summary>
        void DrawPartsOriginAxes(Rect canvas)
        {
            if (!_partsShowAxes || Event.current.type != EventType.Repaint)
                return;
            Vector2 o = WorldToCanvas(canvas, float2.zero);
            if (o.y >= canvas.yMin && o.y <= canvas.yMax)
                EditorGUI.DrawRect(new Rect(canvas.xMin, Mathf.Round(o.y), canvas.width, 1f), new Color(1f, 0.35f, 0.35f, 0.45f));
            if (o.x >= canvas.xMin && o.x <= canvas.xMax)
                EditorGUI.DrawRect(new Rect(Mathf.Round(o.x), canvas.yMin, 1f, canvas.height), new Color(0.4f, 1f, 0.45f, 0.45f));
        }

        void DrawPartsToolToggle(ref float x, float y, string label, PartsCanvasTool tool)
        {
            float w = Mathf.Max(62f, label.Length * 7.5f);
            bool on = _partsCanvasTool == tool;
            var style = on ? _primaryStyle : _partsTabStyle;
            // Force a visible selected look even if Toggle onNormal is muted.
            if (on)
            {
                var r = new Rect(x, y, w, 22f);
                EditorGUI.DrawRect(r, new Color(0.18f, 0.48f, 0.52f, 0.95f));
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2f, r.width, 2f), new Color(0.35f, 0.85f, 0.9f, 1f));
            }
            if (GUI.Toggle(new Rect(x, y, w, 22f), on, label, style) && !on)
                SetPartsCanvasTool(tool);
            x += w + 4f;
        }

        void DrawPartsVisibilityToggle(ref float x, float y, float width, ref bool value,
            string label, string tooltip)
        {
            var r = new Rect(x, y, width, 22f);
            if (value)
            {
                EditorGUI.DrawRect(r, new Color(0.18f, 0.48f, 0.52f, 0.95f));
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2f, r.width, 2f), new Color(0.35f, 0.85f, 0.9f, 1f));
            }
            var style = value ? _primaryStyle : _partsTabStyle;
            value = GUI.Toggle(r, value, new GUIContent(label, tooltip), style);
            x += width + 4f;
        }

        void SwitchPartsMode(SpritePartsStudioMode mode, string undoLabel)
        {
            if (_partsMode == mode) return;
            RecordWindowUndo(undoLabel);
            if (_partsHasTempPose)
                ResolveOrWarnTempPose();
            _partsMode = mode;
            if (_partsMode == SpritePartsStudioMode.Skins)
                _partsSkinPreviewOverrides.Clear();
            _status = mode == SpritePartsStudioMode.Animate
                ? "Animate: transform tools enabled"
                : mode == SpritePartsStudioMode.Rig
                    ? "Rig: transform tools enabled"
                    : "Skins";
            Repaint();
        }

        void SetPartsCanvasTool(PartsCanvasTool tool)
        {
            if (_partsCanvasTool == tool) return;
            if (_partsMode == SpritePartsStudioMode.Skins)
            {
                _status = "Skins edits pivot. Double-click a part.";
                Repaint();
                return;
            }
            if (tool == PartsCanvasTool.Warp && _partsMode != SpritePartsStudioMode.Animate)
            {
                _status = "Warp is stored on clip keys. Switch to Animate.";
                Repaint();
                return;
            }
            RecordWindowUndo("Change Parts Tool");
            _partsCanvasTool = tool;
            // Switching tools mid-drag (or with a stale hotControl) must free the canvas grab
            // so Q/W/E toggles and the left tree stay clickable.
            ReleasePartsCanvasCapture();
            TryExitPartsMeshEdit();
            _status = tool == PartsCanvasTool.Move ? "Move (Q)"
                : tool == PartsCanvasTool.Rotate ? "Rotate (W)"
                : tool == PartsCanvasTool.Warp ? "Warp (R): drag mesh vertices to deform on this key. Double-click a part to edit its mesh."
                : "Scale (E)";
            Repaint();
        }

        /// <summary>
        /// Canvas drag owns GUIUtility.hotControl. If MouseUp is missed (or the user clicks
        /// Q/W/E / tree / splitter while grab is still held), that hotControl swallows every
        /// later GUI.Button/Toggle until something clears it. Call on MouseDown outside the
        /// canvas, on tool switch, and when ending/cancelling a drag.
        /// </summary>
        void ReleasePartsCanvasCapture()
        {
            bool held =
                _partsDragActive
                || (_partsCanvasHotControl != 0 && GUIUtility.hotControl == _partsCanvasHotControl)
                || (_partsCanvasHotControl != 0 && GUIUtility.hotControl == 0);
            if (!held && GUIUtility.hotControl == 0 && !_partsDragActive)
                return;

            _partsDragActive = false;
            _partsWarpActive = false;
            _partsWarpNeedsMesh = false;
            _partsVertexAxis = 0;
            _partsMeshPanning = false;
            _partsFfdDrag = -1;
            _partsFfdDragB = -1;
            EndPartsBrush();
            EndPartsIk();
            _partsDragUndoPending = null;
            _partsDialDragging = false;
            _partsMarqueeActive = false;
            _partsWarpBox = false;
            _partsMeshDrag = false;
            _partsMeshMoved = false;
            _partsMeshEdgeFrom = -1;
            _partsWeightPainting = false;
            _partsDragSlotId = null;
            _partsDragShiftAxis = 0;
            _partsCanvasHotControl = 0;
            _partsGroupMoveMembers.Clear();
            _partsGroupTempPoses.Clear();
            if (GUIUtility.hotControl != 0)
                GUIUtility.hotControl = 0;
            // Auto Key already wrote keys during drag; drop the live overlay.
            if (_partsMode == SpritePartsStudioMode.Animate && _partsAutoKey && _partsHasTempPose)
                _partsHasTempPose = false;
        }

        void CenterSelectedPartsSlot()
            => AlignSelectedPartsToRoot(SpritePartsAlignPivot.Center);

        void CenterSelectedPartsOnRoot()
            => AlignSelectedPartsToRoot(SpritePartsAlignPivot.Center);

        void CenterSelectedPartsWithBounds()
            => AlignSelectedPartsToBounds(SpritePartsAlignPivot.Center);

        enum PartsAlignTarget : byte
        {
            Bounds = 0,
            Root = 1,
        }

        void AlignSelectedPartsToBounds(object pivotObj)
        {
            if (pivotObj is SpritePartsAlignPivot pivot)
                AlignSelectedParts(pivot, PartsAlignTarget.Bounds);
        }

        void AlignSelectedPartsToRoot(object pivotObj)
        {
            if (pivotObj is SpritePartsAlignPivot pivot)
                AlignSelectedParts(pivot, PartsAlignTarget.Root);
        }

        void AlignSelectedPartsToBounds(SpritePartsAlignPivot pivot)
            => AlignSelectedParts(pivot, PartsAlignTarget.Bounds);

        void AlignSelectedPartsToRoot(SpritePartsAlignPivot pivot)
            => AlignSelectedParts(pivot, PartsAlignTarget.Root);

        void AlignSelectedParts(SpritePartsAlignPivot pivot, PartsAlignTarget target)
        {
            EnsurePartsTreeSelectionSynced();
            if (_partsSelectedSlotIds.Count == 0)
            {
                _status = "Select parts first (drag a box, or click the group).";
                return;
            }
            if (_partsMode == SpritePartsStudioMode.Skins)
            {
                _status = "Transform tools off in Skins.";
                return;
            }

            _partsAlignPivot = pivot;
            int clipIndex = PartsEvaluationClipIndex();
            if (!SpritePartsOnion.TrySampleCharacter(
                    _profile, clipIndex, _partsPreviewTime, Allocator.Temp,
                    out var blob, out var poses, out var matrices, out _))
                return;

            try
            {
                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                if (!TryEncapsulateSelectedWorldAabb(
                        ref blob.Value, matrices, clipIndex, _partsPreviewTime,
                        out var selMin, out var selMax) &&
                    !TryEncapsulateSelectedJointAabb(ref blob.Value, matrices, out selMin, out selMax))
                {
                    _status = "Could not measure the selection.";
                    return;
                }
                if (!TryGetAlignTargetWorldAabb(ref blob.Value, matrices, clipIndex, target,
                        out var bMin, out var bMax))
                {
                    _status = target == PartsAlignTarget.Root
                        ? "Root origin is missing."
                        : "No Bounds to align to. Turn Root on and Fit Pose, or save Bounds size.";
                    return;
                }

                float2 from = SpritePartsAuthoringOps.PivotOnAabb(selMin, selMax, pivot);
                float2 to = SpritePartsAuthoringOps.PivotOnAabb(bMin, bMax, pivot);
                float2 delta = to - from;
                if (math.lengthsq(delta) < 1e-8f)
                {
                    _status = "Selection is already aligned to " +
                              (target == PartsAlignTarget.Root ? "Root" : "Bounds") +
                              " (" + SpritePartsAuthoringOps.AlignPivotLabel(pivot) + ").";
                    return;
                }

                string op = target == PartsAlignTarget.Root
                    ? "Center Selection On Root " + SpritePartsAuthoringOps.AlignPivotLabel(pivot)
                    : "Align Selection To Bounds " + SpritePartsAuthoringOps.AlignPivotLabel(pivot);
                BeginPartsDragUndo(op);
                int moved = OffsetSelectedPartsByWorldDelta(ref blob.Value, matrices, delta);
                _partsHasTempPose = false;
                _partsGroupTempPoses.Clear();
                EndPartsDragUndo();
                SaveDirty();
                _status = moved > 0
                    ? "Aligned " + moved + " part(s) to " +
                      (target == PartsAlignTarget.Root ? "Root" : "Bounds") + " " +
                      SpritePartsAuthoringOps.AlignPivotLabel(pivot) +
                      " (rest + all clip keys)."
                    : "No unlocked parts to align.";
                Repaint();
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        int OffsetSelectedPartsByWorldDelta(
            ref SpritePartsSetBlob set,
            NativeArray<float4x4> matrices,
            float2 delta)
        {
            int moved = 0;
            foreach (var id in _partsSelectedSlotIds)
            {
                var slot = SpritePartsAuthoringOps.FindSlot(_profile, id);
                if (slot == null || slot.EditorLocked ||
                    SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, id))
                    continue;
                int idx = BlobSlotIndex(ref set, id);
                float2 localDelta = delta;
                if (idx >= 0 && idx < set.Slots.Length)
                {
                    int parent = set.Slots[idx].ParentSlotIndex;
                    if (parent >= 0 && parent < matrices.Length)
                    {
                        float3x3 rs = new float3x3(
                            matrices[parent].c0.xyz,
                            matrices[parent].c1.xyz,
                            matrices[parent].c2.xyz);
                        localDelta = math.mul(math.inverse(rs), new float3(delta.x, delta.y, 0f)).xy;
                    }
                }
                int wrote = SpritePartsAuthoringOps.OffsetSlotLocalPosition(
                    _profile, id, new Vector2(localDelta.x, localDelta.y),
                    includeRest: true, clipIndex: -1);
                if (wrote > 0)
                    moved++;
            }
            return moved;
        }

        bool TryGetAlignTargetWorldAabb(
            ref SpritePartsSetBlob set,
            NativeArray<float4x4> matrices,
            int clipIndex,
            PartsAlignTarget target,
            out float2 min,
            out float2 max)
        {
            if (target == PartsAlignTarget.Root)
            {
                min = new float2(-0.5f, -0.5f);
                max = new float2(0.5f, 0.5f);
                return true;
            }
            if (SpritePartsAuthoringOps.HasStoredRootBounds(_profile))
            {
                float2 center = new float2(_profile.PartsRootBoundsCenter.x, _profile.PartsRootBoundsCenter.y);
                float2 half = new float2(_profile.PartsRootBoundsSize.x, _profile.PartsRootBoundsSize.y) * 0.5f;
                min = center - half;
                max = center + half;
                return true;
            }
            if (SpritePartsAuthoringOps.TryEncapsulateWorldAabb(
                    _profile, _partsSkinPreviewOverrides, ref set, matrices,
                    clipIndex, _partsPreviewTime, out min, out max))
                return true;
            min = new float2(-0.5f, -0.5f);
            max = new float2(0.5f, 0.5f);
            return true;
        }

        bool TryEncapsulateSelectedJointAabb(
            ref SpritePartsSetBlob set,
            NativeArray<float4x4> matrices,
            out float2 min,
            out float2 max)
        {
            min = new float2(float.PositiveInfinity, float.PositiveInfinity);
            max = new float2(float.NegativeInfinity, float.NegativeInfinity);
            bool any = false;
            int n = math.min(set.Slots.Length, matrices.Length);
            for (int i = 0; i < n; i++)
            {
                string id = SpritePartIdUtility.Canonical(set.Slots[i].SlotId.ToString());
                if (!_partsSelectedSlotIds.Contains(id))
                    continue;
                float2 p = matrices[i].c3.xy;
                min = math.min(min, p);
                max = math.max(max, p);
                any = true;
            }
            if (!any || !math.all(math.isfinite(min)) || !math.all(math.isfinite(max)))
                return false;
            if (math.lengthsq(max - min) < 1e-8f)
            {
                min -= new float2(0.01f, 0.01f);
                max += new float2(0.01f, 0.01f);
            }
            return true;
        }

        void PropagatePlayheadOffsetToAllKeys()
        {
            EnsurePartsTreeSelectionSynced();
            if (_partsSelectedSlotIds.Count == 0)
            {
                _status = "Select parts first (drag a box, or Shift-click).";
                return;
            }
            if (_partsMode == SpritePartsStudioMode.Skins)
            {
                _status = "Transform tools off in Skins.";
                return;
            }

            var clip = CurrentPartsClip;
            int clipIndex = clip != null ? _partsSelectedClip : -1;
            BeginPartsDragUndo("Sync Other Keys With Playhead Offset");
            int parts = 0;
            int keys = 0;
            foreach (var id in _partsSelectedSlotIds)
            {
                var slot = SpritePartsAuthoringOps.FindSlot(_profile, id);
                if (slot == null || slot.EditorLocked ||
                    SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, id))
                    continue;
                var now = SampleLocalPoseForSlot(id, _partsPreviewTime);
                var track = clip != null
                    ? SpritePartsAuthoringOps.FindTrack(clip, id, SpritePartsTrackKind.Pose)
                    : null;
                Vector2 oldPos = SpritePartsAuthoringOps.EvaluateSlotLocalPosition(
                    slot, track, _partsPreviewTime, _partsPreviewTime);
                Vector2 delta = now.Position - oldPos;
                if (delta.sqrMagnitude < 1e-8f)
                    continue;
                keys += SpritePartsAuthoringOps.OffsetSlotLocalPosition(
                    _profile, id, delta, includeRest: true, clipIndex: clipIndex,
                    skipTime: _partsPreviewTime);
                parts++;
            }
            _partsHasTempPose = false;
            EndPartsDragUndo();
            SaveDirty();
            _status = parts > 0
                ? $"Synced {keys} other key(s) on {parts} part(s) to the playhead offset."
                : "Playhead already matches the other keys.";
            Repaint();
        }

        bool TryEncapsulateSelectedWorldAabb(
            ref SpritePartsSetBlob set,
            NativeArray<float4x4> matrices,
            int clipIndex,
            float sampleTime,
            out float2 min,
            out float2 max)
        {
            min = new float2(float.PositiveInfinity, float.PositiveInfinity);
            max = new float2(float.NegativeInfinity, float.NegativeInfinity);
            bool any = false;
            int n = math.min(set.Slots.Length, matrices.Length);
            for (int i = 0; i < n; i++)
            {
                string id = SpritePartIdUtility.Canonical(set.Slots[i].SlotId.ToString());
                if (!_partsSelectedSlotIds.Contains(id))
                    continue;
                if (SpritePartsAuthoringOps.SlotOrAncestorHidden(_profile, id))
                    continue;
                var slot = SpritePartsAuthoringOps.FindSlot(_profile, id);
                var app = SpritePartsAuthoringOps.FindAppearance(_profile,
                    SpritePartsAuthoringOps.ResolvePreviewAppearanceId(
                        _profile, slot, clipIndex, sampleTime, _partsSkinPreviewOverrides));
                if (app == null ||
                    !SpritePartsGeometry.TryResolve(_profile, app, false, out var geo, out _))
                    continue;
                float4x4 m = matrices[i];
                EncapsulateSelectedCorner(ref min, ref max, m, geo, new float2(-0.5f, -0.5f));
                EncapsulateSelectedCorner(ref min, ref max, m, geo, new float2(0.5f, -0.5f));
                EncapsulateSelectedCorner(ref min, ref max, m, geo, new float2(0.5f, 0.5f));
                EncapsulateSelectedCorner(ref min, ref max, m, geo, new float2(-0.5f, 0.5f));
                any = true;
            }

            return any && math.all(math.isfinite(min)) && math.all(math.isfinite(max));
        }

        static void EncapsulateSelectedCorner(
            ref float2 min, ref float2 max, float4x4 matrix,
            SpritePartsGeometry.Resolved geo, float2 quad)
        {
            float2 local = SpritePartsGeometry.VisualPoint(quad, geo.Pivot, geo.LogicalWorldSize);
            float4 world = math.mul(matrix, new float4(local.x, local.y, 0f, 1f));
            min = math.min(min, world.xy);
            max = math.max(max, world.xy);
        }

        void ShowPartsCanvasContextMenu()
        {
            var slot = CurrentPartsSlot;
            var menu = new GenericMenu();
            if (slot == null)
            {
                if (_partsSelectedSlotIds.Count == 0)
                    menu.AddDisabledItem(new GUIContent("Center Selection On Root (no selection)"));
                else
                    menu.AddItem(new GUIContent("Center Selection On Root"), false,
                        CenterSelectedPartsOnRoot);
                if (_partsSelectedSlotIds.Count == 0)
                    menu.AddDisabledItem(new GUIContent("Center Selection With Bounds (no selection)"));
                else
                    menu.AddItem(new GUIContent("Center Selection With Bounds"), false,
                        CenterSelectedPartsWithBounds);
                if (_partsSelectedSlotIds.Count == 0)
                    menu.AddDisabledItem(new GUIContent("Sync Other Keys With Playhead Offset (no selection)"));
                else
                    menu.AddItem(new GUIContent("Sync Other Keys With Playhead Offset"), false,
                        PropagatePlayheadOffsetToAllKeys);
                AddPartsAlignToBoundsMenuItems(menu);
                AddPartsGroupMenuItems(menu, null);
                menu.ShowAsContext();
                return;
            }
            bool locked = slot.EditorLocked ||
                SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, slot.SlotId);
            string sid = SpritePartIdUtility.Canonical(slot.SlotId);
            if (_partsMode == SpritePartsStudioMode.Skins || locked)
                menu.AddDisabledItem(new GUIContent("Center Selection On Root"));
            else
                menu.AddItem(new GUIContent("Center Selection On Root"), false, CenterSelectedPartsOnRoot);
            if (_partsMode == SpritePartsStudioMode.Skins)
                menu.AddDisabledItem(new GUIContent("Center Selection With Bounds"));
            else
                menu.AddItem(new GUIContent("Center Selection With Bounds"), false,
                    CenterSelectedPartsWithBounds);
            if (_partsMode == SpritePartsStudioMode.Skins)
                menu.AddDisabledItem(new GUIContent("Sync Other Keys With Playhead Offset"));
            else
                menu.AddItem(new GUIContent("Sync Other Keys With Playhead Offset"), false,
                    PropagatePlayheadOffsetToAllKeys);
            AddPartsAlignToBoundsMenuItems(menu);
            AddPartsGroupMenuItems(menu, slot);
            if (!locked)
            {
                menu.AddItem(new GUIContent("Duplicate"), false, () => DuplicatePartsSlot(sid, false));
                menu.AddItem(new GUIContent("Duplicate Mirrored Horizontal"), false,
                    () => DuplicatePartsSlot(sid, true, false));
                menu.AddItem(new GUIContent("Duplicate Mirrored Vertical"), false,
                    () => DuplicatePartsSlot(sid, false, true));
                if (_partsMode != SpritePartsStudioMode.Skins)
                {
                    menu.AddItem(new GUIContent("Mirror Horizontal (East-West)"), false,
                        () => FlipPartsSlot(sid, true, false));
                    menu.AddItem(new GUIContent("Mirror Vertical (North-South)"), false,
                        () => FlipPartsSlot(sid, false, true));
                }
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Duplicate (locked)"));
                menu.AddDisabledItem(new GUIContent("Mirror (locked)"));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Tool/Move (Q)"), _partsCanvasTool == PartsCanvasTool.Move,
                () => SetPartsCanvasTool(PartsCanvasTool.Move));
            menu.AddItem(new GUIContent("Tool/Rotate (W)"), _partsCanvasTool == PartsCanvasTool.Rotate,
                () => SetPartsCanvasTool(PartsCanvasTool.Rotate));
            menu.AddItem(new GUIContent("Tool/Scale (E)"), _partsCanvasTool == PartsCanvasTool.Scale,
                () => SetPartsCanvasTool(PartsCanvasTool.Scale));
            menu.AddItem(new GUIContent("Tool/Warp (R)"), _partsCanvasTool == PartsCanvasTool.Warp,
                () => SetPartsCanvasTool(PartsCanvasTool.Warp));
            menu.ShowAsContext();
        }


        /// <summary>
        /// Rig mode always evaluates rest (clipIndex -1). Animate uses the selected clip.
        /// Sampling the selected clip while editing Rig hid rest-transform drags whenever keys exist.
        /// </summary>
        int PartsEvaluationClipIndex()
        {
            if (_partsMode == SpritePartsStudioMode.Rig)
                return -1;
            return CurrentPartsClip != null ? _partsSelectedClip : -1;
        }

        /// <summary>
        /// Auto Key OFF stores a temp pose; push it into the live sample so the canvas moves.
        /// </summary>
        void ApplyTempPoseToSample(
            ref SpritePartsSetBlob set,
            NativeArray<SpritePartsSampler.Pose> localPoses,
            NativeArray<float4x4> matrices)
        {
            if (!localPoses.IsCreated || !matrices.IsCreated)
                return;
            bool any = false;
            if (_partsGroupTempPoses.Count > 0)
            {
                foreach (var kv in _partsGroupTempPoses)
                    any |= ApplyOneTempPose(ref set, localPoses, kv.Key, kv.Value);
            }
            if (_partsHasTempPose && !string.IsNullOrEmpty(_partsTempSlotId) &&
                !_partsGroupTempPoses.ContainsKey(SpritePartIdUtility.Canonical(_partsTempSlotId)))
                any |= ApplyOneTempPose(ref set, localPoses, _partsTempSlotId, _partsTempPose);
            if (any)
                SpritePartsHierarchy.ComposeLocalToRoot(ref set, localPoses, matrices);
        }

        static bool ApplyOneTempPose(
            ref SpritePartsSetBlob set,
            NativeArray<SpritePartsSampler.Pose> localPoses,
            string slotId,
            SpritePartsAuthoringOps.PoseEdit pose)
        {
            int idx = BlobSlotIndex(ref set, slotId);
            if (idx < 0 || idx >= localPoses.Length)
                return false;
            var p = localPoses[idx];
            p.Position = new float2(pose.Position.x, pose.Position.y);
            p.Rotation = pose.Rotation;
            p.Scale = new float2(pose.Scale.x, pose.Scale.y);
            p.Lattice = pose.Lattice;
            localPoses[idx] = p;
            return true;
        }

        void DrawPartsCanvasContents(Rect canvas)
        {
            // Jiggle springs move only while the preview plays, so a paused frame shows exactly what is keyed.
            SpritePartsOnion.PreviewPhysics = _partsPlaying && _partsPhysicsPreview;
            SpritePartsOnion.PreviewStepped = _partsSteppedPreview;
            if (_profile?.PartsSlots == null || _profile.PartsSlots.Count == 0)
            {
                GUI.Label(new Rect(canvas.x + 12f, canvas.y + 12f, canvas.width - 24f, 40f),
                    "No parts yet. Create a Parts Character.", _mutedStyle);
                return;
            }

            if (IsPartsPivotFocus())
            {
                DrawPartsPivotFocus(canvas);
                return;
            }

            if (IsPartsMeshEdit())
            {
                DrawPartsMeshEdit(canvas);
                return;
            }

            DrawPartsOriginAxes(canvas); // behind the art

            var clip = CurrentPartsClip;
            int clipIndex = PartsEvaluationClipIndex();
            float time = _partsPreviewTime;
            bool importPreview = ImportPreviewActive;
            bool sampled;
            BlobAssetReference<SpritePartsSetBlob> blob;
            NativeArray<SpritePartsSampler.Pose> poses;
            NativeArray<float4x4> matrices;
            if (importPreview)
            {
                sampled = SpritePartsOnion.TrySampleClipOnRig(
                    _profile, _importPreviewClip, time, Allocator.Temp,
                    out blob, out poses, out matrices, out _);
            }
            else
            {
                sampled = SpritePartsOnion.TrySampleCharacter(
                    _profile, clipIndex, time, Allocator.Temp,
                    out blob, out poses, out matrices, out _);
            }
            if (!sampled)
                return;

            try
            {
                if (importPreview)
                {
                    // Read-only preview: no onion (ghosts track the selected
                    // profile clip), no handles, nothing pickable.
                    DrawPartsRootGuide(canvas, ref blob.Value, matrices, clipIndex, time);
                    DrawPartsPoseQuads(canvas, ref blob.Value, poses, matrices,
                        new Color(0.85f, 0.95f, 1f, 1f), pickable: false,
                        sampleTime: time, drawArt: true);
                    var banner = new Rect(canvas.x + 8f, canvas.yMax - 30f, canvas.width - 16f, 22f);
                    EditorGUI.DrawRect(banner, new Color(0.12f, 0.16f, 0.22f, 0.94f));
                    GUI.Label(new Rect(banner.x + 6f, banner.y + 3f, banner.width - 12f, 16f),
                        _importPreviewBanner ??
                        $" IMPORT PREVIEW: '{_importPreviewClipName}' from {_importPreviewSourceName} - motion copied onto this rig. Editing paused.",
                        _mutedStyle);
                    return;
                }

                bool drawOnion = _partsOnionEnabled &&
                                 _partsMode == SpritePartsStudioMode.Animate &&
                                 (!_partsPlaying || _partsOnionShowWhilePlaying);
                if (drawOnion && clip != null)
                {
                    var ghosts = SpritePartsOnion.CollectGhostTimes(
                        time, Mathf.Max(1e-3f, clip.Duration), clip.WrapMode,
                        _partsOnionBefore, _partsOnionAfter, _partsOnionSpacing,
                        _partsDisplayFps, _partsPlaying, _partsOnionShowWhilePlaying);
                    foreach (var ghost in ghosts)
                    {
                        if (!SpritePartsOnion.TrySampleCharacter(_profile, clipIndex, ghost.Time, Allocator.Temp,
                                out var gBlob, out var gPoses, out var gMats, out _))
                            continue;
                        try
                        {
                            if (SpritePartsOnion.MatricesApproximatelyEqual(gMats, matrices))
                                continue;
                            Color tint = ghost.IsPast
                                ? new Color(0.35f, 0.55f, 1f, _partsOnionOpacity)
                                : new Color(1f, 0.55f, 0.25f, _partsOnionOpacity);
                            DrawPartsPoseQuads(canvas, ref gBlob.Value, gPoses, gMats, tint,
                                pickable: false, sampleTime: ghost.Time, drawArt: true);
                            if (_partsShowDebug)
                                DrawOnionBadge(canvas, gMats, ghost);
                        }
                        finally
                        {
                            SpritePartsOnion.DisposeSample(gBlob, gPoses, gMats);
                        }
                    }
                }

                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                if (_partsShowRoot)
                    DrawPartsRootGuide(canvas, ref blob.Value, matrices, clipIndex, time);
                DrawPartsPoseQuads(canvas, ref blob.Value, poses, matrices, Color.white,
                    pickable: true, sampleTime: time, drawArt: _partsShowArt);
                if (_partsShowDebug)
                    DrawPartsTransformGizmo(canvas, ref blob.Value, poses, matrices);
                DrawPartsSelectionAlignWidget(canvas, ref blob.Value, matrices, clipIndex, time);
                DrawPartsIsolateBanner(canvas);
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        void DrawPartsIsolateBanner(Rect canvas)
        {
            if (!IsPartsIsolating())
                return;
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, _partsIsolatedSlotId);
            string name = slot?.Name ?? _partsIsolatedSlotId;
            var banner = new Rect(canvas.x + 8f, canvas.y + 8f, Mathf.Min(canvas.width - 16f, 360f), 22f);
            EditorGUI.DrawRect(banner, new Color(0.12f, 0.18f, 0.28f, 0.92f));
            GUI.Label(new Rect(banner.x + 6f, banner.y + 3f, banner.width - 12f, 16f),
                "Isolating " + name + "  (Esc returns to group)", _mutedStyle);
        }

        bool IsPartsPivotFocus()
            => !string.IsNullOrEmpty(_partsPivotFocusSlotId);

        void EnterPartsPivotFocus(string slotId)
        {
            if (_profile == null || string.IsNullOrEmpty(slotId))
                return;
            string id = SpritePartIdUtility.Canonical(slotId);
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, id);
            if (slot == null)
                return;
            var app = ResolvePartsPreviewAppearance(slot, _partsPreviewTime);
            if (app == null)
            {
                _status = "Assign art before editing pivot.";
                return;
            }
            ReleasePartsCanvasCapture();
            SelectPartsSlotId(id, false, false);
            _partsPivotFocusSlotId = id;
            _partsPivotDrag = false;
            _status = "Pivot focus: drag on the sprite. Esc returns.";
            Repaint();
        }

        bool TryExitPartsPivotFocus()
        {
            if (!IsPartsPivotFocus())
                return false;
            _partsPivotFocusSlotId = null;
            _partsPivotDrag = false;
            _status = "Left pivot focus";
            Repaint();
            return true;
        }

        bool TryGetPartsPivotFocusLayout(
            Rect canvas,
            out Rect sprite,
            out SpritePartAppearanceDef app,
            out SpriteSheetDef sheet,
            out Vector2 pivot)
        {
            sprite = default;
            app = null;
            sheet = null;
            pivot = new Vector2(0.5f, 0.5f);
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, _partsPivotFocusSlotId);
            if (slot == null)
                return false;
            app = ResolvePartsPreviewAppearance(slot, _partsPreviewTime);
            if (app == null)
                return false;
            sheet = _profile.SheetAt(app.SheetIndex);
            float2 resolved = SpritePartsGeometry.ResolvePivot(sheet, app);
            pivot = new Vector2(resolved.x, resolved.y);
            float aspect = 1f;
            if (SpritePartsGeometry.TryResolve(_profile, app, false, out var geo, out _))
                aspect = geo.LogicalWorldSize.x / math.max(1e-4f, geo.LogicalWorldSize.y);
            aspect = Mathf.Max(0.05f, aspect);

            const float pad = 36f;
            float availW = Mathf.Max(32f, canvas.width - pad * 2f);
            float availH = Mathf.Max(32f, canvas.height - pad * 2f - 28f);
            float w = availW;
            float h = w / aspect;
            if (h > availH)
            {
                h = availH;
                w = h * aspect;
            }
            sprite = new Rect(
                canvas.x + (canvas.width - w) * 0.5f,
                canvas.y + 36f + (availH - h) * 0.5f,
                w, h);
            return true;
        }

        void DrawPartsPivotFocus(Rect canvas)
        {
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, _partsPivotFocusSlotId);
            string name = slot != null && !string.IsNullOrEmpty(slot.Name) ? slot.Name : _partsPivotFocusSlotId;
            var banner = new Rect(canvas.x + 8f, canvas.y + 8f, Mathf.Min(canvas.width - 16f, 420f), 22f);
            EditorGUI.DrawRect(banner, new Color(0.12f, 0.18f, 0.28f, 0.94f));
            if (GUI.Button(new Rect(banner.x + 4f, banner.y + 2f, 46f, 18f), "Back", _partsTabStyle))
                TryExitPartsPivotFocus();
            GUI.Label(new Rect(banner.x + 54f, banner.y + 3f, banner.width - 60f, 16f),
                name + " pivot  —  drag on the sprite, Esc returns", _mutedStyle);

            if (!TryGetPartsPivotFocusLayout(canvas, out var sprite, out var app, out var sheet, out var pivot))
            {
                GUI.Label(new Rect(canvas.x + 12f, canvas.y + 40f, canvas.width - 24f, 20f),
                    "This part has no art to pivot.", _mutedStyle);
                return;
            }

            EditorGUI.DrawRect(sprite, new Color(0.12f, 0.13f, 0.16f, 1f));
            if (sheet?.Texture != null && app != null)
                DrawPartsSheetCell(sheet.Texture, sheet, sheet.Columns, sheet.Rows, app.CellIndex, sprite, Color.white);
            DrawGuiRectOutline(sprite, new Color(0.35f, 0.9f, 0.55f, 0.9f), 1f);

            Vector2 handle = new Vector2(
                Mathf.Lerp(sprite.xMin, sprite.xMax, pivot.x),
                Mathf.Lerp(sprite.yMax, sprite.yMin, pivot.y));
            var cross = new Color(1f, 0.85f, 0.2f, 0.95f);
            DrawGuiLine(new Vector2(sprite.xMin, handle.y), new Vector2(sprite.xMax, handle.y), cross, 1f);
            DrawGuiLine(new Vector2(handle.x, sprite.yMin), new Vector2(handle.x, sprite.yMax), cross, 1f);
            EditorGUI.DrawRect(new Rect(handle.x - 4f, handle.y - 4f, 8f, 8f), cross);
            GUI.Label(new Rect(sprite.x, sprite.yMax + 4f, sprite.width, 16f),
                $"Pivot {pivot.x:0.00}, {pivot.y:0.00}", _mutedStyle);
        }

        void HandlePartsPivotFocusInput(Rect canvas, Event evt, int controlId)
        {
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                TryExitPartsPivotFocus();
                evt.Use();
                return;
            }
            var banner = new Rect(canvas.x + 8f, canvas.y + 8f, Mathf.Min(canvas.width - 16f, 420f), 22f);
            if (banner.Contains(evt.mousePosition))
                return;
            if (!TryGetPartsPivotFocusLayout(canvas, out var sprite, out _, out _, out _))
                return;
            bool ours = _partsPivotDrag &&
                        (GUIUtility.hotControl == controlId || GUIUtility.hotControl == _partsCanvasHotControl);
            if (evt.type == EventType.MouseDown && evt.button == 0 && canvas.Contains(evt.mousePosition))
            {
                GUIUtility.keyboardControl = 0;
                GUI.FocusControl(null);
                _partsPivotDrag = true;
                _partsCanvasHotControl = controlId;
                GUIUtility.hotControl = controlId;
                SetPartsPivotFromGui(sprite, evt.mousePosition, true);
                evt.Use();
                Repaint();
                return;
            }
            if (!ours)
                return;
            if (evt.type == EventType.MouseDrag)
            {
                SetPartsPivotFromGui(sprite, evt.mousePosition, false);
                evt.Use();
                Repaint();
                return;
            }
            if (evt.type == EventType.MouseUp || evt.rawType == EventType.MouseUp)
            {
                SetPartsPivotFromGui(sprite, evt.mousePosition, false);
                _partsPivotDrag = false;
                _partsCanvasHotControl = 0;
                if (GUIUtility.hotControl == controlId)
                    GUIUtility.hotControl = 0;
                SaveDirty();
                evt.Use();
                Repaint();
            }
        }

        void SetPartsPivotFromGui(Rect sprite, Vector2 mouse, bool record)
        {
            if (sprite.width < 1f || sprite.height < 1f)
                return;
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, _partsPivotFocusSlotId);
            var app = ResolvePartsPreviewAppearance(slot, _partsPreviewTime);
            if (app == null)
                return;
            if (record)
                RecordPartsUndo("Move Appearance Pivot");
            float u = Mathf.Clamp01(Mathf.InverseLerp(sprite.xMin, sprite.xMax, mouse.x));
            float v = Mathf.Clamp01(Mathf.InverseLerp(sprite.yMax, sprite.yMin, mouse.y));
            app.PivotSource = SpritePartPivotSource.Override;
            app.PivotOverride = new Vector2(u, v);
        }

        void DrawOnionBadge(Rect canvas, NativeArray<float4x4> matrices, SpritePartsOnion.GhostSample ghost)
        {
            if (!matrices.IsCreated || matrices.Length == 0) return;
            Vector2 p = WorldToCanvas(canvas, matrices[0].c3.xy);
            string label = ghost.FrameDelta > 0 ? $"+{ghost.FrameDelta}" : $"{ghost.FrameDelta}";
            var r = new Rect(p.x - 10f, p.y - 28f, 28f, 16f);
            EditorGUI.DrawRect(r, ghost.IsPast
                ? new Color(0.2f, 0.35f, 0.7f, 0.85f)
                : new Color(0.7f, 0.4f, 0.15f, 0.85f));
            GUI.Label(r, label, _mutedStyle);
        }

        void DrawPartsRootGuide(Rect canvas,
            ref SpritePartsSetBlob set,
            NativeArray<float4x4> matrices,
            int clipIndex,
            float sampleTime)
        {
            if (!_partsShowRoot)
                return;

            Vector2 o = WorldToCanvas(canvas, float2.zero);
            float unit = 64f * Mathf.Max(0.001f, _previewZoom);
            var cyan = new Color(0.35f, 0.85f, 0.9f, 0.95f);
            var xCol = new Color(0.85f, 0.32f, 0.32f, 0.9f);
            var yCol = new Color(0.32f, 0.82f, 0.42f, 0.9f);

            // Small origin square: the scene GameObject pivot.
            DrawGuiRectOutline(new Rect(o.x - unit * 0.5f, o.y - unit * 0.5f, unit, unit), cyan, 2f);
            DrawGuiLine(new Vector2(o.x - unit, o.y), new Vector2(o.x + unit, o.y), xCol, 1f);
            DrawGuiLine(new Vector2(o.x, o.y - unit), new Vector2(o.x, o.y + unit), yCol, 1f);
            float d = 5f;
            EditorGUI.DrawRect(new Rect(o.x - d, o.y - 1f, d * 2f, 2f), cyan);
            EditorGUI.DrawRect(new Rect(o.x - 1f, o.y - d, 2f, d * 2f), cyan);
            GUI.Label(new Rect(o.x + 8f, o.y - 20f, 72f, 16f),
                new GUIContent("Root", "Character origin. Same pivot as the scene GameObject."),
                _mutedStyle);

            if (!TryGetPartsRootBoundsRect(ref set, matrices, clipIndex, sampleTime,
                    out float2 center, out float2 size, out bool stored))
                return;

            Vector2 min = WorldToCanvas(canvas, center - size * 0.5f);
            Vector2 max = WorldToCanvas(canvas, center + size * 0.5f);
            var bounds = Rect.MinMaxRect(
                math.min(min.x, max.x), math.min(min.y, max.y),
                math.max(min.x, max.x), math.max(min.y, max.y));
            var boundsColor = stored
                ? new Color(1f, 0.38f, 0.32f, 0.95f)
                : new Color(1f, 0.55f, 0.2f, 0.55f);
            DrawGuiRectOutline(bounds, boundsColor, stored ? 2f : 1f);
            GUI.Label(new Rect(bounds.x, bounds.y - 16f, 120f, 16f),
                stored ? "Bounds" : "Bounds (Fit to store)",
                _mutedStyle);
        }

        bool TryGetPartsRootBoundsRect(
            ref SpritePartsSetBlob set,
            NativeArray<float4x4> matrices,
            int clipIndex,
            float sampleTime,
            out float2 center,
            out float2 size,
            out bool stored)
        {
            stored = SpritePartsAuthoringOps.HasStoredRootBounds(_profile);
            if (stored)
            {
                center = new float2(_profile.PartsRootBoundsCenter.x, _profile.PartsRootBoundsCenter.y);
                size = new float2(_profile.PartsRootBoundsSize.x, _profile.PartsRootBoundsSize.y);
                return true;
            }

            if (SpritePartsAuthoringOps.TryEncapsulateWorldAabb(
                    _profile, _partsSkinPreviewOverrides, ref set, matrices,
                    clipIndex, sampleTime, out var min, out var max))
            {
                center = 0.5f * (min + max);
                size = max - min;
                return true;
            }

            center = float2.zero;
            size = new float2(1f, 1f);
            return false;
        }

        void DrawPartsRootBoundsToolbar(float x, float y)
        {
            if (!_partsShowRoot || _profile == null)
                return;

            GUI.Label(new Rect(x, y, 48f, 18f),
                new GUIContent("Bounds", "Character boundary size in world units. Fit frames the current pose without moving keys."),
                _mutedStyle);
            x += 50f;
            EditorGUI.BeginChangeCheck();
            float w = EditorGUI.FloatField(new Rect(x, y, 44f, 16f),
                SpritePartsAuthoringOps.HasStoredRootBounds(_profile)
                    ? _profile.PartsRootBoundsSize.x
                    : 0f);
            x += 48f;
            float h = EditorGUI.FloatField(new Rect(x, y, 44f, 16f),
                SpritePartsAuthoringOps.HasStoredRootBounds(_profile)
                    ? _profile.PartsRootBoundsSize.y
                    : 0f);
            if (EditorGUI.EndChangeCheck())
            {
                RecordPartsUndo("Set Root Bounds Size");
                if (!SpritePartsAuthoringOps.HasStoredRootBounds(_profile))
                    TryFitPartsRootBounds(writeOnly: true);
                SpritePartsAuthoringOps.SetRootBounds(
                    _profile,
                    new float2(_profile.PartsRootBoundsCenter.x, _profile.PartsRootBoundsCenter.y),
                    new float2(Mathf.Max(0.01f, w), Mathf.Max(0.01f, h)));
                SaveDirty();
            }

            x += 50f;
            if (GUI.Button(new Rect(x, y, 70f, 16f),
                    new GUIContent("Fit Pose",
                        "Center and size the bounds around the current pose. Does not change animation keys or rest poses."),
                    EditorStyles.miniButton))
                TryFitPartsRootBounds(writeOnly: false);
        }

        void TryFitPartsRootBounds(bool writeOnly)
        {
            if (_profile == null)
                return;
            if (!writeOnly)
                RecordPartsUndo("Fit Root Bounds To Pose");
            int clipIndex = PartsEvaluationClipIndex();
            if (!SpritePartsOnion.TrySampleCharacter(
                    _profile, clipIndex, _partsPreviewTime, Allocator.Temp,
                    out var blob, out var poses, out var matrices, out _))
                return;
            try
            {
                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                if (!SpritePartsAuthoringOps.TryEncapsulateWorldAabb(
                        _profile, _partsSkinPreviewOverrides, ref blob.Value, matrices,
                        clipIndex, _partsPreviewTime, out var min, out var max))
                    return;
                SpritePartsAuthoringOps.FitRootBoundsToAabb(_profile, min, max);
                if (!writeOnly)
                {
                    SaveDirty();
                    _status = "Root bounds fitted to the current pose. Keys were not changed.";
                }
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        const float PartsAlignPivotHit = 12f;

        void DrawPartsSelectionAlignWidget(
            Rect canvas,
            ref SpritePartsSetBlob set,
            NativeArray<float4x4> matrices,
            int clipIndex,
            float sampleTime)
        {
            EnsurePartsTreeSelectionSynced();
            if (_partsSelectedSlotIds.Count < 2)
                return;
            if (!TryEncapsulateSelectedWorldAabb(ref set, matrices, clipIndex, sampleTime,
                    out var selMin, out var selMax))
                return;

            Vector2 a = WorldToCanvas(canvas, selMin);
            Vector2 b = WorldToCanvas(canvas, selMax);
            var box = Rect.MinMaxRect(
                math.min(a.x, b.x), math.min(a.y, b.y),
                math.max(a.x, b.x), math.max(a.y, b.y));
            DrawGuiRectOutline(box, new Color(0.4f, 0.95f, 0.7f, 0.9f), 1f);

            for (int i = 0; i < 9; i++)
            {
                var pivot = (SpritePartsAlignPivot)i;
                Vector2 p = WorldToCanvas(canvas,
                    SpritePartsAuthoringOps.PivotOnAabb(selMin, selMax, pivot));
                bool on = pivot == _partsAlignPivot;
                float s = on ? 8f : 5f;
                var fill = on
                    ? new Color(1f, 0.85f, 0.2f, 1f)
                    : new Color(0.95f, 0.95f, 0.95f, 0.95f);
                var edge = on
                    ? new Color(1f, 0.55f, 0.1f, 1f)
                    : new Color(0.15f, 0.7f, 0.45f, 1f);
                EditorGUI.DrawRect(new Rect(p.x - s, p.y - s, s * 2f, s * 2f), edge);
                EditorGUI.DrawRect(new Rect(p.x - s + 1.5f, p.y - s + 1.5f, s * 2f - 3f, s * 2f - 3f), fill);
            }

            Vector2 active = WorldToCanvas(canvas,
                SpritePartsAuthoringOps.PivotOnAabb(selMin, selMax, _partsAlignPivot));
            EditorGUI.DrawRect(new Rect(active.x - 10f, active.y - 1f, 20f, 2f), new Color(1f, 0.8f, 0.15f, 1f));
            EditorGUI.DrawRect(new Rect(active.x - 1f, active.y - 10f, 2f, 20f), new Color(1f, 0.8f, 0.15f, 1f));
            GUI.Label(new Rect(box.x, box.yMax + 2f, 280f, 16f),
                "Pivot " + SpritePartsAuthoringOps.AlignPivotLabel(_partsAlignPivot) +
                "  (click = Root, Ctrl+click = Bounds)",
                _mutedStyle);
        }

        int HitPartsAlignPivot(Rect canvas, Vector2 mouse)
        {
            EnsurePartsTreeSelectionSynced();
            if (_partsSelectedSlotIds.Count < 2)
                return -1;
            if (!SpritePartsOnion.TrySampleCharacter(
                    _profile, PartsEvaluationClipIndex(), _partsPreviewTime, Allocator.Temp,
                    out var blob, out var poses, out var matrices, out _))
                return -1;
            try
            {
                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                if (!TryEncapsulateSelectedWorldAabb(
                        ref blob.Value, matrices, PartsEvaluationClipIndex(), _partsPreviewTime,
                        out var selMin, out var selMax))
                    return -1;
                float best = PartsAlignPivotHit * PartsAlignPivotHit;
                int hit = -1;
                for (int i = 0; i < 9; i++)
                {
                    Vector2 p = WorldToCanvas(canvas,
                        SpritePartsAuthoringOps.PivotOnAabb(selMin, selMax, (SpritePartsAlignPivot)i));
                    float d = (p - mouse).sqrMagnitude;
                    if (d <= best)
                    {
                        best = d;
                        hit = i;
                    }
                }
                return hit;
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        void AddPartsAlignToBoundsMenuItems(GenericMenu menu)
        {
            bool can = _partsMode != SpritePartsStudioMode.Skins && _partsSelectedSlotIds.Count > 0;
            for (int i = 0; i < 9; i++)
            {
                var pivot = (SpritePartsAlignPivot)i;
                string path = "Align To Root/" + SpritePartsAuthoringOps.AlignPivotLabel(pivot);
                if (!can)
                    menu.AddDisabledItem(new GUIContent(path));
                else
                    menu.AddItem(new GUIContent(path), pivot == _partsAlignPivot,
                        AlignSelectedPartsToRoot, pivot);
            }
            for (int i = 0; i < 9; i++)
            {
                var pivot = (SpritePartsAlignPivot)i;
                string path = "Align To Bounds/" + SpritePartsAuthoringOps.AlignPivotLabel(pivot);
                if (!can)
                    menu.AddDisabledItem(new GUIContent(path));
                else
                    menu.AddItem(new GUIContent(path), pivot == _partsAlignPivot,
                        AlignSelectedPartsToBounds, pivot);
            }
        }

        static void DrawGuiRectOutline(Rect r, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, thickness, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
        }

        static void DrawGuiLine(Vector2 a, Vector2 b, Color color, float thickness)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f)
                return;
            float ang = Vector2.SignedAngle(Vector2.right, d);
            var prev = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, a);
            EditorGUI.DrawRect(new Rect(a.x, a.y - thickness * 0.5f, len, thickness), color);
            GUI.matrix = prev;
        }

        SpritePartSlotDef SlotDefFromBlob(ref SpritePartsSetBlob set, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= set.Slots.Length) return null;
            return SpritePartsAuthoringOps.FindSlot(_profile, set.Slots[slotIndex].SlotId.ToString());
        }

        SpritePartAppearanceDef ResolvePartsPreviewAppearance(SpritePartSlotDef slot, float sampleTime)
        {
            if (slot == null) return null;
            // Group previews, layer preview and group keys too, in the game's order.
            return SpritePartsAuthoringOps.FindAppearance(_profile, ResolvePartsPreviewAppearanceId(slot, sampleTime));
        }

        bool TryGetPartsSlotDrawRect(
            Rect canvas, ref SpritePartsSetBlob set, NativeArray<float4x4> matrices,
            int slotIndex, float sampleTime, out Rect rect, out Vector2 joint, out float worldDeg,
            out SpritePartAppearanceDef app, out SpriteSheetDef sheet,
            NativeArray<SpritePartsSampler.Pose> poses = default)
        {
            rect = default;
            joint = default;
            worldDeg = 0f;
            app = null;
            sheet = null;
            if (slotIndex < 0 || slotIndex >= matrices.Length) return false;
            float4x4 m = matrices[slotIndex];
            joint = WorldToCanvas(canvas, m.c3.xy);
            // Raw matrix angle (includes reflection). Gizmo/hit must match this basis.
            worldDeg = math.degrees(math.atan2(m.c0.y, m.c0.x));
            var slot = SlotDefFromBlob(ref set, slotIndex);
            app = ResolvePartsPreviewAppearance(slot, sampleTime);
            if (app != null)
                sheet = _profile != null ? _profile.SheetAt(app.SheetIndex) : null;

            float2 pivot = new float2(0.5f, 0.5f);
            float2 worldSize;
            bool hasArt = false;
            if (app != null && SpritePartsGeometry.TryResolve(_profile, app, false, out var geo, out _))
            {
                pivot = geo.Pivot;
                worldSize = geo.LogicalWorldSize;
                hasArt = sheet != null && sheet.Texture != null;
            }
            else
            {
                float placeholder = 36f / (64f * Mathf.Max(0.001f, _previewZoom));
                worldSize = new float2(placeholder, placeholder);
            }

            float sx = math.length(m.c0.xy);
            float sy = math.length(m.c1.xy);
            float absSx = sx > 1e-5f ? sx : 1f;
            float absSy = sy > 1e-5f ? sy : 1f;
            float pixelW = worldSize.x * 64f * _previewZoom * absSx;
            float pixelH = worldSize.y * 64f * _previewZoom * absSy;
            if (!hasArt)
            {
                pixelW = 36f * _previewZoom * absSx;
                pixelH = 36f * _previewZoom * absSy;
                pivot = new float2(0.5f, 0.5f);
            }

            rect = new Rect(
                joint.x - pivot.x * pixelW,
                joint.y - (1f - pivot.y) * pixelH,
                Mathf.Max(4f, pixelW),
                Mathf.Max(4f, pixelH));
            return true;
        }

        void DrawPartsPoseQuads(
            Rect canvas, ref SpritePartsSetBlob set,
            NativeArray<SpritePartsSampler.Pose> poses, NativeArray<float4x4> matrices,
            Color tint, bool pickable, float sampleTime, bool drawArt)
        {
            // A clipped part is a mesh now, so the flat pass must skip it (the mesh pass draws it cut).
            var artPoses = ClippedArtPoses(ref set, poses, matrices, out bool ownArt);
            try
            {
                DrawPartsPoseQuadsCore(canvas, ref set, poses, artPoses, matrices, tint, pickable, sampleTime, drawArt);
            }
            finally
            {
                if (ownArt)
                    artPoses.Dispose();
            }
        }

        void DrawPartsPoseQuadsCore(
            Rect canvas, ref SpritePartsSetBlob set,
            NativeArray<SpritePartsSampler.Pose> poses, NativeArray<SpritePartsSampler.Pose> artPoses, NativeArray<float4x4> matrices,
            Color tint, bool pickable, float sampleTime, bool drawArt)
        {
            // Draw by DrawRank ascending (back to front).
            int n = set.Slots.Length;
            var order = new int[n];
            var ranks = new int[n];
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
                ranks[i] = PreviewDrawRank(ref set, i, sampleTime); // draw-order keys
            }
            System.Array.Sort(ranks, order);

            for (int o = 0; o < order.Length; o++)
            {
                int i = order[o];
                string sid = set.Slots[i].SlotId.ToString();
                if (SpritePartsAuthoringOps.SlotOrAncestorHidden(_profile, sid))
                    continue;
                if (!TryGetPartsSlotDrawRect(canvas, ref set, matrices, i, sampleTime,
                        out var r, out var joint, out float worldDeg, out var app, out var sheet, poses))
                    continue;

                // Bones have no image: draw the bone shape on the main pose only (not in onion ghosts).
                var boneDef = SpritePartsAuthoringOps.FindSlot(_profile, sid);
                if (boneDef != null && boneDef.IsBone)
                {
                    if (pickable)
                        DrawPartsBone(joint, matrices[i], boneDef.BoneLength, IsPartsBlobSlotSelected(ref set, i), pickable);
                    continue;
                }
                if (boneDef != null && boneDef.IsClipShape)
                {
                    // Clip shapes have no image: their outline on the main pose only.
                    if (pickable)
                    {
                        // Its keyed deform at this time, as the game clips with it.
                        SpritePartsSampler.SampleDeformOffsets(ref set, PartsEvaluationClipIndex(), i, sampleTime,
                            boneDef.ClipPolygon?.Length ?? 0, out var clipOffsets);
                        DrawPartsClipShape(canvas, matrices[i], boneDef, IsPartsBlobSlotSelected(ref set, i), clipOffsets);
                    }
                    continue;
                }
                if (boneDef != null && boneDef.IsPath)
                {
                    // Paths have no image: their curve on the main pose only.
                    if (pickable)
                        DrawPartsPath(canvas, matrices[i], boneDef, IsPartsBlobSlotSelected(ref set, i));
                    continue;
                }

                // Pose scale signs: Mirror H = Scale.x < 0 (east-west), Mirror V = Scale.y < 0 (north-south).
                // Use RAW worldDeg from the matrix (same as gizmo/hit). Do not +180 here - that
                // desynced handles from the sprite and broke Move/Rotate/Scale.
                float flipSx = 1f;
                float flipSy = 1f;
                if (poses.IsCreated && i < poses.Length)
                {
                    if (poses[i].Scale.x < 0f) flipSx = -1f;
                    if (poses[i].Scale.y < 0f) flipSy = -1f;
                }
                else
                {
                    float2 c0b = matrices[i].c0.xy;
                    float2 c1b = matrices[i].c1.xy;
                    if (c0b.x * c1b.y - c0b.y * c1b.x < 0f)
                        flipSx = -1f;
                }
                Texture2D tex = sheet?.Texture;
                var lattice = artPoses.IsCreated && i < artPoses.Length ? artPoses[i].Lattice : default;
                if (IsSheared(matrices[i]))
                {
                    // Shear cannot be drawn as a turned rectangle: draw the image by its true corners.
                    var partMatrix = matrices[i];
                    Rect partRect = r;
                    Vector2 partJoint = joint;
                    System.Func<float2, Vector2> toGui = p => ShearedGui(canvas, partRect, partJoint, partMatrix, p);
                    if (drawArt && tex != null && app != null && !lattice.HasMesh)
                        DrawPartsWarpedSprite(tex, sheet, app.CellIndex, r, UnitQuadLattice, PreviewTint(ref set, i, sampleTime, tint), toGui);
                    var corners = new Vector3[]
                    {
                        toGui(new float2(-0.5f, -0.5f)), toGui(new float2(0.5f, -0.5f)), toGui(new float2(0.5f, 0.5f)), toGui(new float2(-0.5f, 0.5f)),
                    };
                    bool picked = pickable && IsPartsBlobSlotSelected(ref set, i);
                    if (Event.current.type == EventType.Repaint && (picked || (drawArt && (tex == null || app == null))))
                    {
                        Handles.BeginGUI();
                        if (drawArt && (tex == null || app == null))
                        {
                            Handles.color = picked ? new Color(0.45f, 0.9f, 0.55f, tint.a) : new Color(0.75f, 0.78f, 0.85f, tint.a);
                            Handles.DrawAAConvexPolygon(corners);
                        }
                        if (picked)
                        {
                            Handles.color = new Color(0.35f, 0.9f, 0.55f, 0.95f);
                            Handles.DrawAAPolyLine(2f, corners[0], corners[1], corners[2], corners[3], corners[0]);
                        }
                        Handles.EndGUI();
                    }
                    continue;
                }
                Matrix4x4 prev = GUI.matrix;
                GUIUtility.RotateAroundPivot(-worldDeg, joint);
                if (flipSx < 0f || flipSy < 0f)
                    GUIUtility.ScaleAroundPivot(new Vector2(flipSx, flipSy), joint);
                if (drawArt && tex != null && app != null)
                {
                    // AnyPortrait keeps the whole image on screen. A partial polygon must not clip the rest away.
                    // The flat cell is the rigid picture. The bent mesh is drawn after EndClip,
                    // otherwise this texture is flushed on top of it and the drag looks frozen.
                    if (!lattice.HasMesh)
                        DrawPartsSheetCell(tex, sheet, sheet.Columns, sheet.Rows, app.CellIndex, r,
                            PreviewTint(ref set, i, sampleTime, tint)); // colour keys
                }
                else if (drawArt)
                {
                    var col = tint;
                    if (pickable && IsPartsBlobSlotSelected(ref set, i))
                        col = new Color(0.45f, 0.9f, 0.55f, tint.a);
                    else if (pickable)
                        col = new Color(0.75f, 0.78f, 0.85f, tint.a);
                    EditorGUI.DrawRect(r, col);
                }
                GUI.matrix = prev;

                if (pickable && IsPartsBlobSlotSelected(ref set, i))
                {
                    float guiDeg = -worldDeg;
                    Vector2 c0 = RotateAround(new Vector2(r.xMin, r.yMin), joint, guiDeg);
                    Vector2 c1 = RotateAround(new Vector2(r.xMax, r.yMin), joint, guiDeg);
                    Vector2 c2 = RotateAround(new Vector2(r.xMax, r.yMax), joint, guiDeg);
                    Vector2 c3 = RotateAround(new Vector2(r.xMin, r.yMax), joint, guiDeg);
                    var sel = new Color(0.35f, 0.9f, 0.55f, 0.95f);
                    DrawGuiLine(c0, c1, sel, 2f);
                    DrawGuiLine(c1, c2, sel, 2f);
                    DrawGuiLine(c2, c3, sel, 2f);
                    DrawGuiLine(c3, c0, sel, 2f);
                }

                if (_partsShowDebug && pickable)
                {
                    string name = set.Slots[i].Name.ToString();
                    GUI.Label(new Rect(joint.x - 24f,
                            joint.y + Mathf.Max(10f, r.height * 0.5f) + 2f, 80f, 14f),
                        name, _mutedStyle);
                }
            }
        }

        Vector2 PartsGizmoHandle(Rect unrotated, Vector2 joint, float guiDeg, ColliderHandleKind kind)
        {
            Vector2 local = kind switch
            {
                ColliderHandleKind.CornerTL => new Vector2(unrotated.xMin, unrotated.yMin),
                ColliderHandleKind.CornerTR => new Vector2(unrotated.xMax, unrotated.yMin),
                ColliderHandleKind.CornerBR => new Vector2(unrotated.xMax, unrotated.yMax),
                ColliderHandleKind.CornerBL => new Vector2(unrotated.xMin, unrotated.yMax),
                ColliderHandleKind.EdgeT => new Vector2(unrotated.center.x, unrotated.yMin),
                ColliderHandleKind.EdgeR => new Vector2(unrotated.xMax, unrotated.center.y),
                ColliderHandleKind.EdgeB => new Vector2(unrotated.center.x, unrotated.yMax),
                ColliderHandleKind.EdgeL => new Vector2(unrotated.xMin, unrotated.center.y),
                ColliderHandleKind.Rotate => new Vector2(unrotated.center.x, unrotated.yMin - PartsRotateHandleDistance),
                _ => joint,
            };
            return RotateAround(local, joint, guiDeg);
        }

        static readonly ColliderHandleKind[] PartsGizmoHandleOrder =
        {
            ColliderHandleKind.Rotate,
            ColliderHandleKind.CornerTL, ColliderHandleKind.CornerTR,
            ColliderHandleKind.CornerBR, ColliderHandleKind.CornerBL,
            ColliderHandleKind.EdgeT, ColliderHandleKind.EdgeR,
            ColliderHandleKind.EdgeB, ColliderHandleKind.EdgeL,
        };

        bool IsPartsBlobSlotSelected(ref SpritePartsSetBlob set, int blobIndex)
        {
            if (blobIndex < 0 || blobIndex >= set.Slots.Length) return false;
            EnsurePartsTreeSelectionSynced();
            string id = SpritePartIdUtility.Canonical(set.Slots[blobIndex].SlotId.ToString());
            return _partsSelectedSlotIds.Contains(id);
        }

        int SelectedPartsBlobIndex(ref SpritePartsSetBlob set)
        {
            var selected = CurrentPartsSlot;
            if (selected == null) return -1;
            return BlobSlotIndex(ref set, selected.SlotId);
        }

        static Rect FlipRectAroundJoint(Rect r, Vector2 joint, bool flipX, bool flipY)
        {
            float xMin = r.xMin, xMax = r.xMax, yMin = r.yMin, yMax = r.yMax;
            if (flipX)
            {
                float a = joint.x - (xMax - joint.x);
                float b = joint.x + (joint.x - xMin);
                xMin = Mathf.Min(a, b);
                xMax = Mathf.Max(a, b);
            }
            if (flipY)
            {
                float a = joint.y - (yMax - joint.y);
                float b = joint.y + (joint.y - yMin);
                yMin = Mathf.Min(a, b);
                yMax = Mathf.Max(a, b);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        bool TryGetSelectedPartsGizmo(Rect canvas, out Rect unrotated, out Vector2 joint, out float guiDeg)
        {
            unrotated = default;
            joint = default;
            guiDeg = 0f;
            if (CurrentPartsSlot == null || _profile?.PartsSlots == null)
                return false;
            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
                return false;
            try
            {
                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                int idx = SelectedPartsBlobIndex(ref blob.Value);
                if (idx < 0 || idx >= matrices.Length) return false;
                if (!TryGetPartsSlotDrawRect(canvas, ref blob.Value, matrices, idx,
                        _partsPreviewTime, out unrotated, out joint, out float worldDeg, out _, out _, poses))
                    return false;
                guiDeg = -worldDeg;
                // Keep gizmo/hit aligned with DrawPartsPoseQuads ScaleAroundPivot flips.
                if (poses.IsCreated && idx < poses.Length)
                {
                    unrotated = FlipRectAroundJoint(unrotated, joint,
                        poses[idx].Scale.x < 0f, poses[idx].Scale.y < 0f);
                }
                return true;
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        ColliderHandleKind HitPartsTransformHandle(Rect canvas, Vector2 mouse)
        {
            if (_partsMode == SpritePartsStudioMode.Skins) return ColliderHandleKind.None;
            if (!TryGetSelectedPartsGizmo(canvas, out var r, out var joint, out float guiDeg))
                return ColliderHandleKind.None;
            float hit = PartsHandleHit * PartsHandleHit;
            float worldDeg = -guiDeg;

            if (_partsCanvasTool == PartsCanvasTool.Scale)
            {
                float scaleHit = PartsScaleHandleHit * PartsScaleHandleHit;
                foreach (var kind in PartsGizmoHandleOrder)
                {
                    if (kind == ColliderHandleKind.Rotate) continue;
                    if ((mouse - PartsGizmoHandle(r, joint, guiDeg, kind)).sqrMagnitude <= scaleHit)
                        return kind;
                }
                Vector2 localScale = UnrotateAround(mouse, joint, guiDeg);
                if (r.Contains(localScale))
                    return ColliderHandleKind.Body;
                return ColliderHandleKind.None;
            }

            if (_partsCanvasTool == PartsCanvasTool.Move)
            {
                float worldLen = 0.55f / Mathf.Max(0.25f, _previewZoom);
                float2 ax = PartsGizmoAxisX(worldDeg);
                float2 ay = PartsGizmoAxisY(worldDeg);
                Vector2 xTip = PartsAxisTipGui(canvas, joint, ax, worldLen);
                Vector2 yTip = PartsAxisTipGui(canvas, joint, ay, worldLen);
                if ((mouse - xTip).sqrMagnitude <= hit) return ColliderHandleKind.AxisX;
                if ((mouse - yTip).sqrMagnitude <= hit) return ColliderHandleKind.AxisY;
                if (DistPointToSegment(mouse, joint, xTip) <= 7f) return ColliderHandleKind.AxisX;
                if (DistPointToSegment(mouse, joint, yTip) <= 7f) return ColliderHandleKind.AxisY;
                float s = 8f;
                if (new Rect(joint.x - s, joint.y - s, s * 2f, s * 2f).Contains(mouse))
                    return ColliderHandleKind.FreeMove;
                Vector2 localMove = UnrotateAround(mouse, joint, guiDeg);
                if (r.Contains(localMove))
                    return ColliderHandleKind.Body;
                return ColliderHandleKind.None;
            }

            if (_partsCanvasTool == PartsCanvasTool.Rotate)
            {
                float rr = 56f;
                float orient = _partsGizmoLocal ? -worldDeg : 0f;
                if (DistPointToCircle(mouse, joint, rr + 7f) <= 8f)
                    return ColliderHandleKind.RotateSphere;
                if (DistPointToCircle(mouse, joint, rr) <= 8f)
                    return ColliderHandleKind.RotateZ;
                if (DistPointToEllipse(mouse, joint, rr, 0.38f, orient) <= 8f)
                    return ColliderHandleKind.RotateX;
                if (DistPointToEllipse(mouse, joint, rr, 0.38f, orient + 90f) <= 8f)
                    return ColliderHandleKind.RotateY;
                if ((mouse - PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.Rotate)).sqrMagnitude <= hit)
                    return ColliderHandleKind.Rotate;
                Vector2 localR = UnrotateAround(mouse, joint, guiDeg);
                if (r.Contains(localR))
                    return ColliderHandleKind.Body;
                return ColliderHandleKind.None;
            }

            Vector2 local = UnrotateAround(mouse, joint, guiDeg);
            if (r.Contains(local))
                return ColliderHandleKind.Body;
            return ColliderHandleKind.None;
        }


        
        float2 PartsGizmoAxisX(float worldDeg)
        {
            if (!_partsGizmoLocal)
                return new float2(1f, 0f);
            float rad = worldDeg * Mathf.Deg2Rad;
            return new float2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        float2 PartsGizmoAxisY(float worldDeg)
        {
            if (!_partsGizmoLocal)
                return new float2(0f, 1f);
            float rad = worldDeg * Mathf.Deg2Rad;
            return new float2(-Mathf.Sin(rad), Mathf.Cos(rad));
        }

        Vector2 PartsAxisTipGui(Rect canvas, Vector2 jointGui, float2 axisWorld, float worldLen)
        {
            float2 origin = CanvasToWorld(canvas, jointGui);
            return WorldToCanvas(canvas, origin + axisWorld * worldLen);
        }

        static float DistPointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        static float DistPointToCircle(Vector2 p, Vector2 center, float radius)
        {
            return Mathf.Abs((p - center).magnitude - radius);
        }

        static float DistPointToEllipse(Vector2 p, Vector2 center, float radius, float squash, float guiDeg)
        {
            float best = float.MaxValue;
            float rad = guiDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            Vector2 prev = Vector2.zero;
            for (int i = 0; i <= 32; i++)
            {
                float a = (Mathf.PI * 2f) * (i / 32f);
                float lx = Mathf.Cos(a) * radius;
                float ly = Mathf.Sin(a) * radius * squash;
                Vector2 cur = center + new Vector2(lx * c - ly * s, lx * s + ly * c);
                if (i > 0)
                {
                    Vector2 ab = cur - prev;
                    float t = Mathf.Clamp01(Vector2.Dot(p - prev, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                    float d = (p - (prev + ab * t)).magnitude;
                    if (d < best) best = d;
                }
                prev = cur;
            }
            return best;
        }

        void DrawPartsArrowGui(Vector2 from, Vector2 to, Color color, float head)
        {
            // Unity Scene View style: opaque stem + filled cone tip (not wire outline).
            Color solid = color;
            solid.a = 1f;
            Handles.color = solid;
            Vector2 dir = to - from;
            float len = dir.magnitude;
            if (len < 1f) return;
            dir /= len;
            // Shorten stem so it meets the base of the filled head.
            Vector2 stemEnd = to - dir * (head * 0.72f);
            Handles.DrawAAPolyLine(4.5f, from, stemEnd);
            Vector2 n = new Vector2(-dir.y, dir.x);
            Vector3 p0 = to;
            Vector3 p1 = to - dir * head + n * (head * 0.5f);
            Vector3 p2 = to - dir * head - n * (head * 0.5f);
            Handles.DrawAAConvexPolygon(p0, p1, p2);
        }

        void DrawPartsMoveAxisGizmo(Rect canvas, Vector2 joint, float worldDeg)
        {
            // Match Unity Move tool: screen-aligned Global X=red / Y=green, Local follows part.
            float worldLen = 0.65f / Mathf.Max(0.25f, _previewZoom);
            float2 ax = PartsGizmoAxisX(worldDeg);
            float2 ay = PartsGizmoAxisY(worldDeg);
            Vector2 xTip = PartsAxisTipGui(canvas, joint, ax, worldLen);
            Vector2 yTip = PartsAxisTipGui(canvas, joint, ay, worldLen);
            // Handles.*AxisColor = exact Unity editor axis palette.
            DrawPartsArrowGui(joint, yTip, Handles.yAxisColor, 12f);
            DrawPartsArrowGui(joint, xTip, Handles.xAxisColor, 12f);
            float s = 5.5f;
            Handles.DrawSolidRectangleWithOutline(
                new Vector3[]
                {
                    joint + new Vector2(-s, -s), joint + new Vector2(s, -s),
                    joint + new Vector2(s, s), joint + new Vector2(-s, s)
                },
                new Color(Handles.centerColor.r, Handles.centerColor.g, Handles.centerColor.b, 0.55f),
                new Color(Handles.centerColor.r, Handles.centerColor.g, Handles.centerColor.b, 0.95f));
        }

        void DrawPartsGizmoEllipse(Vector2 center, float radius, float squash, float guiDeg, Color color, int segments = 48)
        {
            Handles.color = color;
            float rad = guiDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            var pts = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float a = (Mathf.PI * 2f) * (i / (float)segments);
                float lx = Mathf.Cos(a) * radius;
                float ly = Mathf.Sin(a) * radius * squash;
                float gx = lx * c - ly * s;
                float gy = lx * s + ly * c;
                pts[i] = new Vector3(center.x + gx, center.y + gy, 0f);
            }
            Handles.DrawAAPolyLine(2.4f, pts);
        }

        void DrawPartsRotateSphereGizmo(Vector2 joint, float worldDeg)
        {
            float r = 56f;
            float orient = _partsGizmoLocal ? -worldDeg : 0f;
            Handles.color = new Color(1f, 1f, 1f, 0.4f);
            Handles.DrawWireDisc(joint, Vector3.forward, r + 7f);
            Handles.color = new Color(0.25f, 0.5f, 1f, 0.98f);
            Handles.DrawWireDisc(joint, Vector3.forward, r);
            DrawPartsGizmoEllipse(joint, r, 0.38f, orient, new Color(0.95f, 0.25f, 0.2f, 0.92f));
            DrawPartsGizmoEllipse(joint, r, 0.38f, orient + 90f, new Color(0.25f, 0.85f, 0.3f, 0.92f));
            Vector2 knob = joint + new Vector2(0f, -r);
            if (_partsGizmoLocal)
            {
                float rad = orient * Mathf.Deg2Rad;
                knob = joint + new Vector2(Mathf.Sin(rad) * r, -Mathf.Cos(rad) * r);
            }
            Handles.color = new Color(0.35f, 0.6f, 1f, 1f);
            Handles.DrawSolidDisc(knob, Vector3.forward, 5f);
            Handles.color = Color.white;
            Handles.DrawWireDisc(knob, Vector3.forward, 6.5f);
        }


        void DrawPartsTransformGizmo(Rect canvas, ref SpritePartsSetBlob set,
            NativeArray<SpritePartsSampler.Pose> poses, NativeArray<float4x4> matrices)
        {
            int sel = SelectedPartsBlobIndex(ref set);
            if (sel < 0 || sel >= matrices.Length) return;
            if (!TryGetPartsSlotDrawRect(canvas, ref set, matrices, sel, _partsPreviewTime,
                    out var r, out var joint, out float worldDeg, out _, out _, poses))
                return;
            float guiDeg = -worldDeg;
            // Warp draws its vertex gizmo after the meshed art (DrawPartsPoseWarpOverlay) so it stays on top.
            if (_partsCanvasTool == PartsCanvasTool.Warp && _partsMode == SpritePartsStudioMode.Animate)
                return;
            var outline = new Vector3[5];
            outline[0] = PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerTL);
            outline[1] = PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerTR);
            outline[2] = PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerBR);
            outline[3] = PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerBL);
            outline[4] = outline[0];
            Vector2 top = PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.EdgeT);
            Vector2 rotate = PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.Rotate);

            Handles.BeginGUI();
            Handles.color = new Color(0.35f, 0.9f, 0.45f, 0.95f);
            Handles.DrawAAPolyLine(1.8f, outline);
            // Sphere gizmo replaces the old stem/knob outside Skins.
            if (_partsCanvasTool == PartsCanvasTool.Rotate && _partsMode == SpritePartsStudioMode.Skins)
            {
                Handles.color = Color.white;
                Handles.DrawAAPolyLine(1.6f, top, rotate);
                Handles.DrawSolidDisc(rotate, Vector3.forward, 5f);
                Handles.color = new Color(1f, 0.85f, 0.2f, 1f);
                Handles.DrawWireDisc(rotate, Vector3.forward, 7f);
            }
            Handles.color = Color.yellow;
            Handles.DrawWireDisc(joint, Vector3.forward, 6f);
            Handles.DrawAAPolyLine(1.2f, joint + new Vector2(-5f, 0f), joint + new Vector2(5f, 0f));
            Handles.DrawAAPolyLine(1.2f, joint + new Vector2(0f, -5f), joint + new Vector2(0f, 5f));
            
            if (_partsCanvasTool == PartsCanvasTool.Move && _partsMode != SpritePartsStudioMode.Skins)
                DrawPartsMoveAxisGizmo(canvas, joint, worldDeg);
            if (_partsCanvasTool == PartsCanvasTool.Rotate && _partsMode != SpritePartsStudioMode.Skins)
                DrawPartsRotateSphereGizmo(joint, worldDeg);
            Handles.EndGUI();

            if (_partsMode == SpritePartsStudioMode.Skins) return;

            if (_partsCanvasTool == PartsCanvasTool.Scale)
            {
                DrawHandleKnob(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerTL));
                DrawHandleKnob(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerTR));
                DrawHandleKnob(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerBR));
                DrawHandleKnob(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerBL));
                DrawHandleKnob(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.EdgeT), true);
                DrawHandleKnob(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.EdgeR), true);
                DrawHandleKnob(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.EdgeB), true);
                DrawHandleKnob(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.EdgeL), true);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerTL), 12f), MouseCursor.ResizeUpLeft);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerTR), 12f), MouseCursor.ResizeUpRight);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerBR), 12f), MouseCursor.ResizeUpLeft);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerBL), 12f), MouseCursor.ResizeUpRight);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.EdgeT), 12f), MouseCursor.ResizeVertical);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.EdgeB), 12f), MouseCursor.ResizeVertical);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.EdgeL), 12f), MouseCursor.ResizeHorizontal);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.EdgeR), 12f), MouseCursor.ResizeHorizontal);
            }
            else if (_partsCanvasTool == PartsCanvasTool.Rotate)
            {
                EditorGUIUtility.AddCursorRect(HandleCursorRect(rotate, 10f), MouseCursor.RotateArrow);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(joint, 12f), MouseCursor.RotateArrow);
            }
            else
            {
                EditorGUIUtility.AddCursorRect(HandleCursorRect(joint, 12f), MouseCursor.MoveArrow);
                EditorGUIUtility.AddCursorRect(r, MouseCursor.MoveArrow);
            }
        }

        Vector2 WorldToCanvas(Rect canvas, float2 world)
        {
            Vector2 center = canvas.center + _previewPan;
            return center + new Vector2(world.x, -world.y) * (64f * _previewZoom);
        }

        float2 CanvasToWorld(Rect canvas, Vector2 gui)
        {
            Vector2 center = canvas.center + _previewPan;
            Vector2 d = (gui - center) / (64f * Mathf.Max(0.001f, _previewZoom));
            return new float2(d.x, -d.y);
        }

        /// <summary>
        /// Same pattern as HandleActiveTimelineDrag / pivot reclaim: keep MouseDrag alive
        /// even when inspector fields or conditional GUI cleared hotControl.
        /// controlId must be the one allocated once at the start of OnGUI.
        /// </summary>
        void HandleActivePartsCanvasDrag(int controlId)
        {
            if (!_partsDragActive || string.IsNullOrEmpty(_partsDragSlotId))
                return;
            if (_studioTab != StudioTab.Parts)
                return;
            if (ImportPreviewActive)
            {
                // Preview pose is not profile data; never write it anywhere.
                // Also free hotControl - leaving it stuck blocks Q/W/E and the tree.
                ReleasePartsCanvasCapture();
                return;
            }

            var evt = Event.current;
            EventType raw = evt.rawType;

            // Missed MouseUp leaves us "dragging" forever. End on a real MouseDown only -
            // never Event.rawType alone: during Layout/Repaint rawType can still be MouseDown
            // from the press that STARTED the drag, which immediately killed Move/Rotate auto-key.
            if (evt.type == EventType.MouseDown)
            {
                EndPartsDragUndo();
                ReleasePartsCanvasCapture();
                Repaint();
                return;
            }

            if (raw != EventType.MouseDrag && raw != EventType.MouseUp &&
                raw != EventType.MouseLeaveWindow &&
                !(raw == EventType.KeyDown && evt.keyCode == KeyCode.Escape))
                return;

            // Rebuild the same canvas rect the preview uses (work area center panel).
            float timelineHeight = _timelinePanelHeight;
            var workRect = new Rect(
                Gap,
                ToolbarHeight + Gap,
                position.width - Gap * 2f,
                Mathf.Max(MinWorkAreaHeight,
                    position.height - ToolbarHeight - timelineHeight - Gap * 3f));
            float centerWidth = workRect.width - _clipPanelWidth - _inspectorPanelWidth - Gap * 2f;
            var previewRect = new Rect(
                workRect.x + _clipPanelWidth + Gap, workRect.y, centerWidth, workRect.height);
            var canvas = new Rect(
                previewRect.x + 10f, previewRect.y + 84f,
                previewRect.width - 20f, previewRect.height - 96f);

            // Reclaim only while the pointer is still dragging / releasing.
            if ((raw == EventType.MouseDrag || raw == EventType.MouseUp)
                && GUIUtility.hotControl != controlId)
                GUIUtility.hotControl = controlId;
            _partsCanvasHotControl = controlId;

            // Edit Mesh works on the big flat image, not the posed part rect.
            if (IsPartsMeshEdit())
            {
                if (!HandlePartsMeshEditDrag(canvas, evt, raw)
                    && (raw == EventType.MouseUp || raw == EventType.MouseLeaveWindow
                        || (raw == EventType.KeyDown && evt.keyCode == KeyCode.Escape)))
                {
                    ReleasePartsCanvasCapture();
                    evt.Use();
                }
                return;
            }

            if (raw == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                if (_partsGroupMoveMembers.Count > 1)
                {
                    _partsGroupTempPoses.Clear();
                    _partsHasTempPose = false;
                }
                else if (_partsDragUndoGroup >= 0)
                    ApplyPartsPoseEdit(_partsDragSlotId, _partsDragStartPose);
                // Only undo when this drag changed something; otherwise PerformUndo would take back an older edit.
                bool changed = _partsDragUndoGroup >= 0;
                _partsDragUndoPending = null;
                EndPartsDragUndo();
                if (changed)
                    Undo.PerformUndo();
                ReleasePartsCanvasCapture();
                _status = "Cancelled drag";
                evt.Use();
                Repaint();
                return;
            }

            if (raw == EventType.MouseDrag)
            {
                if (_partsWarpBox)
                {
                    _partsWarpBoxEnd = evt.mousePosition;
                    evt.Use();
                    Repaint();
                    return;
                }
                var slot = SpritePartsAuthoringOps.FindSlot(_profile, _partsDragSlotId);
                if (slot != null)
                    ApplyPartsTransformDrag(canvas, evt.mousePosition);
                evt.Use();
                Repaint();
                return;
            }

            if (raw == EventType.MouseUp || raw == EventType.MouseLeaveWindow)
            {
                if (_partsWarpBox)
                {
                    if (raw == EventType.MouseLeaveWindow)
                    {
                        _partsWarpBox = false;
                        ReleasePartsCanvasCapture();
                        evt.Use();
                        Repaint();
                        return;
                    }
                    FinishPartsWarpBox(canvas, evt.mousePosition, evt.shift);
                    _partsWarpBox = false;
                    ReleasePartsCanvasCapture();
                    evt.Use();
                    Repaint();
                    return;
                }
                string moved = _partsDragSlotId ?? "part";
                if (_partsGroupMoveMembers.Count > 1)
                {
                    FlushPartsDragUndo();
                    CommitPartsGroupMoveOffsets();
                }
                bool warpDrag = _partsCanvasTool == PartsCanvasTool.Warp;
                EndPartsDragUndo();
                ReleasePartsCanvasCapture();
                if (!warpDrag)
                    _status = "Moved " + moved;
                evt.Use();
                Repaint();
            }
        }


        void DrawPartsCanvasZoomToolbar(Rect previewRect)
        {
            float z = Mathf.Clamp(_previewZoom, (float)0.1, (float)8);
            if (!Mathf.Approximately(z, _previewZoom))
            {
                _previewZoom = z;
                _partsCanvasZoom = z;
            }

            var canvas = new Rect(
                previewRect.x + 10f, previewRect.y + 84f,
                previewRect.width - 20f, previewRect.height - 96f);
            var row = new Rect(previewRect.x + 10f, previewRect.y + 58f, previewRect.width - 20f, 20f);
            Vector2 mid = canvas.center;
            float x = row.xMax - 168f;
            if (GUI.Button(new Rect(x, row.y, 28f, 18f),
                    new GUIContent("-", "Zoom out (-)"), EditorStyles.miniButtonLeft))
                PartsCanvasZoomToward(mid, mid, 1f / 1.15f);
            x += 28f;
            if (GUI.Button(new Rect(x, row.y, 56f, 18f),
                    new GUIContent(string.Format("{0}%", Mathf.RoundToInt(_previewZoom * 100f)), "Actual size 100% (1)"),
                    EditorStyles.miniButtonMid))
                PartsCanvasResetView();
            x += 56f;
            if (GUI.Button(new Rect(x, row.y, 28f, 18f),
                    new GUIContent("+", "Zoom in (+)"), EditorStyles.miniButtonMid))
                PartsCanvasZoomToward(mid, mid, 1.15f);
            x += 28f;
            if (GUI.Button(new Rect(x, row.y, 40f, 18f),
                    new GUIContent("Fit", "Fit all in view (F). Shift+F = selection"),
                    EditorStyles.miniButtonRight))
                PartsCanvasFit(canvas, selectionOnly: false);
        }

        void PartsCanvasResetView()
        {
            _previewZoom = 1f;
            _previewPan = Vector2.zero;
            _partsCanvasZoom = 1f;
            _partsCanvasPan = Vector2.zero;
            Repaint();
        }

        void PartsCanvasZoomToward(Vector2 canvasRectCenter, Vector2 mouseGui, float factor)
        {
            float oldZ = Mathf.Max((float)0.0001, _previewZoom);
            float newZ = Mathf.Clamp(oldZ * factor, (float)0.1, (float)8);
            if (Mathf.Approximately(oldZ, newZ))
                return;
            Vector2 fromCenter = mouseGui - canvasRectCenter;
            Vector2 world = (fromCenter - _previewPan) / (64f * oldZ);
            _previewPan = fromCenter - world * (64f * newZ);
            _previewZoom = newZ;
            _partsCanvasZoom = newZ;
            _partsCanvasPan = _previewPan;
            Repaint();
        }

        void PartsCanvasFit(Rect canvas, bool selectionOnly)
        {
            if (canvas.width < 8f || canvas.height < 8f)
                return;
            Rect content = default;
            if (!TryGetPartsViewGuiBounds(canvas, selectionOnly, out content) && selectionOnly)
                TryGetPartsViewGuiBounds(canvas, selectionOnly: false, out content);
            if (content.width < 1f || content.height < 1f)
            {
                PartsCanvasResetView();
                return;
            }

            const float pad = 28f;
            float availW = Mathf.Max(8f, canvas.width - pad * 2f);
            float availH = Mathf.Max(8f, canvas.height - pad * 2f);
            float factor = Mathf.Min(availW / content.width, availH / content.height);
            factor = Mathf.Clamp(factor, (float)0.05, (float)32);
            Vector2 focus = content.center;
            PartsCanvasZoomToward(canvas.center, focus, factor);
            _previewPan += canvas.center - focus;
            _partsCanvasPan = _previewPan;
            Repaint();
        }

        void PartsCanvasCenter(Rect canvas, bool preferSelection)
        {
            Rect content = default;
            bool have = preferSelection && TryGetPartsViewGuiBounds(canvas, selectionOnly: true, out content);
            if (!have)
                have = TryGetPartsViewGuiBounds(canvas, selectionOnly: false, out content);
            Vector2 target;
            if (have && content.width >= 1f && content.height >= 1f)
                target = content.center;
            else
                target = WorldToCanvas(canvas, new float2(0f, 0f));
            _previewPan += canvas.center - target;
            _partsCanvasPan = _previewPan;
            Repaint();
        }

        bool TryGetPartsViewGuiBounds(Rect canvas, bool selectionOnly, out Rect bounds)
        {
            bounds = default;
            if (_profile?.PartsSlots == null || _profile.PartsSlots.Count == 0)
                return false;

            int clipIndex = PartsEvaluationClipIndex();
            float time = _partsPreviewTime;
            bool importPreview = ImportPreviewActive;
            bool sampled;
            BlobAssetReference<SpritePartsSetBlob> blob;
            NativeArray<SpritePartsSampler.Pose> poses;
            NativeArray<float4x4> matrices;
            if (importPreview)
            {
                sampled = SpritePartsOnion.TrySampleClipOnRig(
                    _profile, _importPreviewClip, time, Allocator.Temp,
                    out blob, out poses, out matrices, out _);
            }
            else
            {
                sampled = SpritePartsOnion.TrySampleCharacter(
                    _profile, clipIndex, time, Allocator.Temp,
                    out blob, out poses, out matrices, out _);
            }
            if (!sampled)
                return false;

            try
            {
                ref var set = ref blob.Value;
                int sel = SelectedPartsBlobIndex(ref set);
                bool any = false;
                float minX = 0f, minY = 0f, maxX = 0f, maxY = 0f;
                for (int i = 0; i < set.Slots.Length; i++)
                {
                    if (selectionOnly && i != sel)
                        continue;
                    string sid = set.Slots[i].SlotId.ToString();
                    if (SpritePartsAuthoringOps.SlotOrAncestorHidden(_profile, sid))
                        continue;
                    if (!TryGetPartsSlotDrawRect(canvas, ref set, matrices, i, time,
                            out var r, out var joint, out float worldDeg, out _, out _, poses))
                        continue;

                    bool flipX = false, flipY = false;
                    if (poses.IsCreated && i < poses.Length)
                    {
                        if (poses[i].Scale.x < 0f) flipX = true;
                        if (poses[i].Scale.y < 0f) flipY = true;
                    }

                    EncapsulatePartsGuiQuad(
                        r, joint, -worldDeg, flipX, flipY,
                        ref any, ref minX, ref minY, ref maxX, ref maxY);
                }

                if (!any)
                    return false;
                bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
                return bounds.width > 0.5f && bounds.height > 0.5f;
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        static void EncapsulatePartsGuiQuad(
            Rect r, Vector2 joint, float guiDeg, bool flipX, bool flipY,
            ref bool any, ref float minX, ref float minY, ref float maxX, ref float maxY)
        {
            float rad = guiDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            var corners = new Vector2[]
            {
                new Vector2(r.xMin, r.yMin),
                new Vector2(r.xMax, r.yMin),
                new Vector2(r.xMax, r.yMax),
                new Vector2(r.xMin, r.yMax),
            };
            for (int i = 0; i < 4; i++)
            {
                Vector2 q = corners[i];
                if (flipX) q.x = joint.x - (q.x - joint.x);
                if (flipY) q.y = joint.y - (q.y - joint.y);
                Vector2 d = q - joint;
                Vector2 w = joint + new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
                if (!any)
                {
                    minX = maxX = w.x;
                    minY = maxY = w.y;
                    any = true;
                }
                else
                {
                    minX = Mathf.Min(minX, w.x);
                    minY = Mathf.Min(minY, w.y);
                    maxX = Mathf.Max(maxX, w.x);
                    maxY = Mathf.Max(maxY, w.y);
                }
            }
        }

        bool HandlePartsCanvasHotkeys(Rect canvas, Event evt)
        {
            if (evt.type != EventType.KeyDown)
                return false;
            if (EditorGUIUtility.editingTextField)
                return false;
            if (evt.control || evt.command)
                return false;

            switch (evt.keyCode)
            {
                case KeyCode.F:
                    PartsCanvasFit(canvas, selectionOnly: evt.shift);
                    evt.Use();
                    return true;
                case KeyCode.Home:
                case KeyCode.Period:
                    PartsCanvasCenter(canvas, preferSelection: true);
                    evt.Use();
                    return true;
                case KeyCode.Alpha1:
                case KeyCode.Keypad1:
                    if (evt.shift) return false;
                    PartsCanvasResetView();
                    evt.Use();
                    return true;
                case KeyCode.Equals:
                case KeyCode.Plus:
                case KeyCode.KeypadPlus:
                    PartsCanvasZoomToward(canvas.center, canvas.center, 1.15f);
                    evt.Use();
                    return true;
                case KeyCode.Minus:
                case KeyCode.KeypadMinus:
                    PartsCanvasZoomToward(canvas.center, canvas.center, 1f / 1.15f);
                    evt.Use();
                    return true;
                case KeyCode.LeftBracket:
                    JumpPartsPlayheadToNeighborKey(-1);
                    evt.Use();
                    return true;
                case KeyCode.RightBracket:
                    JumpPartsPlayheadToNeighborKey(1);
                    evt.Use();
                    return true;
                case KeyCode.LeftArrow:
                    StepPartsPlayhead(-1, _partsFrameStep);
                    evt.Use();
                    return true;
                case KeyCode.RightArrow:
                    StepPartsPlayhead(1, _partsFrameStep);
                    evt.Use();
                    return true;

                default:
                    return false;
            }
        }

        bool HandlePartsCanvasNavigation(Rect canvas, Event evt, int controlId)
        {
            if (evt.type == EventType.ScrollWheel && canvas.Contains(evt.mousePosition))
            {
                float factor = evt.delta.y > 0f ? (1f / 1.1f) : 1.1f;
                PartsCanvasZoomToward(canvas.center, evt.mousePosition, factor);
                evt.Use();
                return true;
            }

            bool panButton = evt.button == 2 || (evt.button == 0 && evt.alt);
            if (evt.type == EventType.MouseDown && panButton && canvas.Contains(evt.mousePosition))
            {
                _previewPanning = true;
                _previewPanStartMouse = evt.mousePosition;
                _previewPanStartOffset = _previewPan;
                GUIUtility.hotControl = controlId;
                evt.Use();
                return true;
            }

            if (_previewPanning && GUIUtility.hotControl == controlId)
            {
                if (evt.type == EventType.MouseDrag)
                {
                    _previewPan = _previewPanStartOffset + (evt.mousePosition - _previewPanStartMouse);
                    _partsCanvasPan = _previewPan;
                    evt.Use();
                    Repaint();
                    return true;
                }
                if (evt.type == EventType.MouseUp || evt.rawType == EventType.MouseUp)
                {
                    _previewPanning = false;
                    if (GUIUtility.hotControl == controlId)
                        GUIUtility.hotControl = 0;
                    evt.Use();
                    return true;
                }
            }

            return false;
        }

        void BeginPartsMarquee(Rect canvas, Event evt, int controlId)
        {
            _partsMarqueeActive = true;
            _partsMarqueeAdditive = evt.shift || evt.control;
            _partsMarqueeStart = evt.mousePosition;
            _partsMarqueeCurrent = evt.mousePosition;
            _partsCanvasHotControl = controlId;
            GUIUtility.hotControl = controlId;
            GUIUtility.keyboardControl = 0;
            GUI.FocusControl(null);
            Repaint();
        }

        bool HandlePartsMarquee(Rect canvas, Event evt, int controlId)
        {
            if (!_partsMarqueeActive)
                return false;

            if (evt.type == EventType.MouseDrag)
            {
                _partsMarqueeCurrent = evt.mousePosition;
                evt.Use();
                Repaint();
                return true;
            }

            if (evt.type == EventType.MouseUp ||
                (evt.rawType == EventType.MouseUp && GUIUtility.hotControl == controlId))
            {
                var box = PartsMarqueeRect();
                bool tiny = box.width < 4f && box.height < 4f;
                if (tiny)
                {
                    if (IsPartsIsolating())
                    {
                        TryExitPartsIsolate();
                    }
                    else if (!_partsMarqueeAdditive)
                    {
                        ClearPartsSelection();
                        _status = "Selection cleared.";
                    }
                }
                else
                {
                    var hits = CollectPartsSlotIdsInGuiRect(canvas, box);
                    if (!_partsMarqueeAdditive)
                        ClearPartsSelection();
                    for (int i = 0; i < hits.Count; i++)
                        SelectPartsSlotId(hits[i], true, false);
                    ExpandMarqueeHitsToGroups();
                    _status = hits.Count == 0
                        ? "Marquee: nothing selected."
                        : $"Marquee selected {_partsSelectedSlotIds.Count} part(s). Right-click to Center Selection With Bounds.";
                }
                _partsMarqueeActive = false;
                if (GUIUtility.hotControl == controlId || GUIUtility.hotControl == _partsCanvasHotControl)
                    GUIUtility.hotControl = 0;
                _partsCanvasHotControl = 0;
                evt.Use();
                Repaint();
                return true;
            }

            return evt.type == EventType.Ignore;
        }

        static Rect PartsMarqueeRect(Vector2 a, Vector2 b)
            => Rect.MinMaxRect(math.min(a.x, b.x), math.min(a.y, b.y),
                math.max(a.x, b.x), math.max(a.y, b.y));

        Rect PartsMarqueeRect()
            => PartsMarqueeRect(_partsMarqueeStart, _partsMarqueeCurrent);

        void DrawPartsWarpBox(Rect canvas)
        {
            if (!_partsWarpBox)
                return;
            var box = Rect.MinMaxRect(
                Mathf.Min(_partsWarpBoxStart.x, _partsWarpBoxEnd.x),
                Mathf.Min(_partsWarpBoxStart.y, _partsWarpBoxEnd.y),
                Mathf.Max(_partsWarpBoxStart.x, _partsWarpBoxEnd.x),
                Mathf.Max(_partsWarpBoxStart.y, _partsWarpBoxEnd.y));
            box = Rect.MinMaxRect(
                math.max(box.xMin, canvas.xMin), math.max(box.yMin, canvas.yMin),
                math.min(box.xMax, canvas.xMax), math.min(box.yMax, canvas.yMax));
            if (box.width < 1f || box.height < 1f)
                return;
            EditorGUI.DrawRect(box, new Color(0.25f, 0.7f, 0.95f, 0.12f));
            DrawGuiRectOutline(box, new Color(0.35f, 0.85f, 1f, 0.95f), 1f);
        }

        void DrawPartsMarquee(Rect canvas)
        {
            if (!_partsMarqueeActive)
                return;
            var box = PartsMarqueeRect();
            box = Rect.MinMaxRect(
                math.max(box.xMin, canvas.xMin), math.max(box.yMin, canvas.yMin),
                math.min(box.xMax, canvas.xMax), math.min(box.yMax, canvas.yMax));
            if (box.width < 1f || box.height < 1f)
                return;
            EditorGUI.DrawRect(box, new Color(0.25f, 0.7f, 0.95f, 0.12f));
            DrawGuiRectOutline(box, new Color(0.35f, 0.85f, 1f, 0.95f), 1f);
        }

        System.Collections.Generic.List<string> CollectPartsSlotIdsInGuiRect(Rect canvas, Rect guiRect)
        {
            var hits = new System.Collections.Generic.List<string>();
            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
                return hits;
            try
            {
                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                for (int i = 0; i < blob.Value.Slots.Length; i++)
                {
                    string sid = blob.Value.Slots[i].SlotId.ToString();
                    if (SpritePartsAuthoringOps.SlotOrAncestorHidden(_profile, sid))
                        continue;
                    if (!TryGetPartsSlotDrawRect(canvas, ref blob.Value, matrices, i, _partsPreviewTime,
                            out var r, out var joint, out float worldDeg, out _, out _, poses))
                        continue;
                    float guiDeg = -worldDeg;
                    Vector2 c0 = RotateAround(new Vector2(r.xMin, r.yMin), joint, guiDeg);
                    Vector2 c1 = RotateAround(new Vector2(r.xMax, r.yMin), joint, guiDeg);
                    Vector2 c2 = RotateAround(new Vector2(r.xMax, r.yMax), joint, guiDeg);
                    Vector2 c3 = RotateAround(new Vector2(r.xMin, r.yMax), joint, guiDeg);
                    var aabb = Rect.MinMaxRect(
                        math.min(math.min(c0.x, c1.x), math.min(c2.x, c3.x)),
                        math.min(math.min(c0.y, c1.y), math.min(c2.y, c3.y)),
                        math.max(math.max(c0.x, c1.x), math.max(c2.x, c3.x)),
                        math.max(math.max(c0.y, c1.y), math.max(c2.y, c3.y)));
                    if (aabb.Overlaps(guiRect, true))
                        hits.Add(sid);
                }
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
            return hits;
        }

        void HandlePartsCanvasInput(Rect canvas, int controlId)
        {
            Event evt = Event.current;

            // Design-app camera keys when pointer is over the canvas (ignore text fields).
            if (canvas.Contains(evt.mousePosition) && HandlePartsCanvasHotkeys(canvas, evt))
                return;

            bool ours = (_partsDragActive || _partsMarqueeActive) &&
                        (GUIUtility.hotControl == controlId ||
                         GUIUtility.hotControl == _partsCanvasHotControl ||
                         GUIUtility.hotControl == 0);

            if (!canvas.Contains(evt.mousePosition) && !ours && !_partsDragActive && !_partsMarqueeActive && !_partsMeshPanning)
                return;

            // Canvas camera: scroll zooms toward cursor; MMB / Alt+LMB pans. Edit Mesh has its own camera.
            if (IsPartsMeshEdit()
                    ? HandlePartsMeshNavigation(canvas, evt, controlId)
                    : HandlePartsCanvasNavigation(canvas, evt, controlId))
                return;

            if (!IsPartsMeshEdit() && !IsPartsPivotFocus() && !_partsDragActive && HandleClipShapeInput(canvas, evt))
                return;
            if (!IsPartsMeshEdit() && !IsPartsPivotFocus() && !_partsDragActive && HandlePathInput(canvas, evt))
                return;

            if (HandlePartsMarquee(canvas, evt, controlId))
                return;

            if (IsPartsMeshEdit() &&
                (evt.type == EventType.ContextClick ||
                 (evt.button == 1 && (evt.type == EventType.MouseDown || evt.type == EventType.MouseUp))))
            {
                // Edit Mesh owns right-click (context menu with edge / vertex actions).
                HandlePartsMeshEditInput(canvas, evt, controlId);
                return;
            }

            if (evt.type == EventType.ContextClick && canvas.Contains(evt.mousePosition) && !IsPartsPivotFocus())
            {
                int hitCtx = HitTestPartsSlot(canvas, evt.mousePosition);
                if (hitCtx >= 0)
                {
                    string sid = SlotIdFromHit(hitCtx);
                    if (!string.IsNullOrEmpty(sid))
                    {
                        string id = SpritePartIdUtility.Canonical(sid);
                        EnsurePartsTreeSelectionSynced();
                        if (!_partsSelectedSlotIds.Contains(id))
                            SelectPartsCanvasClicked(sid, false, false, false);
                    }
                }
                ShowPartsCanvasContextMenu();
                evt.Use();
                return;
            }

            if (IsPartsPivotFocus())
            {
                HandlePartsPivotFocusInput(canvas, evt, controlId);
                return;
            }

            if (IsPartsMeshEdit())
            {
                HandlePartsMeshEditInput(canvas, evt, controlId);
                return;
            }

            if (_partsMode == SpritePartsStudioMode.Skins)
            {
                if (evt.type == EventType.MouseDown && evt.button == 0 && canvas.Contains(evt.mousePosition))
                {
                    if (evt.alt)
                        return;
                    GUIUtility.keyboardControl = 0;
                    GUI.FocusControl(null);
                    int hit = HitTestPartsSlot(canvas, evt.mousePosition);
                    string slotId = hit >= 0 ? SlotIdFromHit(hit) : null;
                    if (!string.IsNullOrEmpty(slotId))
                    {
                        if (evt.clickCount >= 2)
                            EnterPartsPivotFocus(slotId);
                        else
                            SelectPartsCanvasClicked(slotId, evt.shift || evt.control, false, false);
                    }
                    else
                        _status = "Double-click a part to edit its pivot.";
                    evt.Use();
                }
                return;
            }

            // Active drag is owned by HandleActivePartsCanvasDrag (runs first in OnGUI).
            if (_partsDragActive)
                return;

            if (evt.type == EventType.MouseMove
                && _partsCanvasTool == PartsCanvasTool.Warp
                && _partsMode == SpritePartsStudioMode.Animate
                && canvas.Contains(evt.mousePosition))
            {
                UpdatePartsWarpHover(canvas, evt.mousePosition);
                if (_partsSoftSelect)
                    Repaint(); // soft-selection radius follows the cursor
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && canvas.Contains(evt.mousePosition))
            {
                // Any outside click closes the preview popup (its OnClose clears
                // the preview). A canvas click while a preview lingers therefore
                // means the popup is already gone - end the preview and let this
                // click work normally instead of swallowing every interaction.
                if (ImportPreviewActive)
                    ClearImportPreview("Import preview ended");

                // Always release inspector float-field focus so Vector2Field cannot eat MouseDrag.
                GUIUtility.keyboardControl = 0;
                GUI.FocusControl(null);

                if (_partsCanvasTool == PartsCanvasTool.Warp
                    && _partsMode == SpritePartsStudioMode.Animate
                    && evt.clickCount >= 2)
                {
                    string focusId = null;
                    if (TryPickPartsWarpVertex(canvas, evt.mousePosition, out string focusSlot, out _))
                        focusId = focusSlot;
                    else
                    {
                        int focusHit = HitTestPartsSlot(canvas, evt.mousePosition);
                        focusId = focusHit >= 0 ? SlotIdFromHit(focusHit) : null;
                    }
                    if (string.IsNullOrEmpty(focusId) && CurrentPartsSlot != null)
                        focusId = CurrentPartsSlot.SlotId;
                    if (!string.IsNullOrEmpty(focusId))
                    {
                        EnterPartsMeshEdit(focusId);
                        evt.Use();
                        Repaint();
                        return;
                    }
                }

                int alignHit = HitPartsAlignPivot(canvas, evt.mousePosition);
                if (alignHit >= 0)
                {
                    var pivot = (SpritePartsAlignPivot)alignHit;
                    _partsAlignPivot = pivot;
                    if (!evt.shift)
                    {
                        if (evt.control || evt.command)
                            AlignSelectedPartsToBounds(pivot);
                        else
                            AlignSelectedPartsToRoot(pivot);
                    }
                    evt.Use();
                    Repaint();
                    return;
                }

                // FFD owns the canvas while it is on: grab a grid point, anything else is ignored.
                if (PartsFfdActive())
                {
                    if (!TryBeginPartsFfdDrag(canvas, evt.mousePosition, controlId))
                        _status = "FFD: drag a white grid point. Apply (Enter) or Cancel (Esc) to leave FFD.";
                    evt.Use();
                    Repaint();
                    return;
                }

                // Twist / Pinch / Bloat: click places a dial with a -1..1 bar.
                if (TryBeginPartsDial(canvas, evt, controlId))
                {
                    evt.Use();
                    Repaint();
                    return;
                }

                // A Liquify brush paints over the selected part.
                if (TryBeginPartsBrush(canvas, evt, controlId))
                {
                    evt.Use();
                    Repaint();
                    return;
                }

                // X / Y arrows on the selected vertices: move along one axis (the square moves freely).
                if (_partsCanvasTool == PartsCanvasTool.Warp
                    && _partsMode == SpritePartsStudioMode.Animate
                    && TryGetWarpSelectionCentre(canvas, out var axisOrigin))
                {
                    int axis = HitPartsAxisGizmo(axisOrigin, evt.mousePosition);
                    if (axis != 0)
                    {
                        BeginPartsWarpDrag(controlId, CurrentPartsSlot.SlotId, _partsWarpSelection[0], evt.mousePosition, false);
                        _partsVertexAxis = axis;
                        evt.Use();
                        Repaint();
                        return;
                    }
                }

                if (_partsCanvasTool == PartsCanvasTool.Warp
                    && _partsMode == SpritePartsStudioMode.Animate
                    && TryPickPartsWarpVertex(canvas, evt.mousePosition, out string warpSlot, out int warpPoint))
                {
                    BeginPartsWarpDrag(controlId, warpSlot, warpPoint, evt.mousePosition, evt.shift);
                    evt.Use();
                    Repaint();
                    return;
                }

                // A line of the selected part: both of its ends (Shift adds them), dragged together.
                if (_partsCanvasTool == PartsCanvasTool.Warp
                    && _partsMode == SpritePartsStudioMode.Animate
                    && TryPickPartsWarpLine(canvas, evt.mousePosition, out int lineA, out int lineB))
                {
                    SetWarpSelectionSlot(CurrentPartsSlot.SlotId);
                    if (!evt.shift)
                        _partsWarpSelection.Clear();
                    if (!_partsWarpSelection.Contains(lineA))
                        _partsWarpSelection.Add(lineA);
                    if (!_partsWarpSelection.Contains(lineB))
                        _partsWarpSelection.Add(lineB);
                    BeginPartsWarpDrag(controlId, CurrentPartsSlot.SlotId, lineA, evt.mousePosition, false);
                    evt.Use();
                    Repaint();
                    return;
                }

                // Inside the selection box: transform the selected vertices as a group.
                if (_partsCanvasTool == PartsCanvasTool.Warp
                    && _partsMode == SpritePartsStudioMode.Animate
                    && !evt.shift
                    && TryGetWarpSelectionBox(canvas, out var warpBox)
                    && warpBox.Contains(evt.mousePosition))
                {
                    BeginPartsWarpDrag(controlId, CurrentPartsSlot.SlotId, _partsWarpSelection[0], evt.mousePosition, false);
                    evt.Use();
                    Repaint();
                    return;
                }

                // Warp on a meshed part: any other press starts a vertex box on THAT part, even over empty
                // space or another part (which used to switch parts). A plain click on another part switches.
                var warpPart = CurrentPartsSlot;
                if (_partsCanvasTool == PartsCanvasTool.Warp
                    && _partsMode == SpritePartsStudioMode.Animate
                    && warpPart?.Mesh != null && warpPart.Mesh.HasMesh
                    && !warpPart.EditorLocked && !SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, warpPart.SlotId))
                {
                    int under = HitTestPartsSlot(canvas, evt.mousePosition);
                    _partsWarpBoxClickSlot = under >= 0 ? SlotIdFromHit(under) : null;
                    _partsWarpBox = true;
                    _partsWarpBoxStart = evt.mousePosition;
                    _partsWarpBoxEnd = evt.mousePosition;
                    _partsDragActive = true;
                    _partsCanvasHotControl = controlId;
                    GUIUtility.hotControl = controlId;
                    _partsDragSlotId = warpPart.SlotId;
                    _partsDragStartMouse = evt.mousePosition;
                    evt.Use();
                    Repaint();
                    return;
                }

                var handle = HitPartsTransformHandle(canvas, evt.mousePosition);
                string slotId = null;
                if (handle != ColliderHandleKind.None)
                {
                    var selected = CurrentPartsSlot;
                    slotId = selected != null ? selected.SlotId : null;
                }
                else
                {
                    int hit = HitTestPartsSlot(canvas, evt.mousePosition);
                    slotId = hit >= 0 ? SlotIdFromHit(hit) : null;
                    if (string.IsNullOrEmpty(slotId))
                    {
                        BeginPartsMarquee(canvas, evt, controlId);
                        evt.Use();
                        return;
                    }
                    bool isolate = evt.clickCount >= 2 && _partsCanvasTool != PartsCanvasTool.Warp;
                    if (isolate)
                    {
                        EnterPartsPivotFocus(slotId);
                        evt.Use();
                        Repaint();
                        return;
                    }
                    SelectPartsCanvasClicked(
                        slotId, evt.shift || evt.control, false, evt.alt);
                }

                if (string.IsNullOrEmpty(slotId))
                    return;
                var slot = SpritePartsAuthoringOps.FindSlot(_profile, slotId);
                bool locked = slot != null && (slot.EditorLocked ||
                    SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, slot.SlotId));
                if (locked || slot == null)
                {
                    _status = "Part is locked.";
                    evt.Use();
                    return;
                }

                if (_partsCanvasTool == PartsCanvasTool.Warp)
                {
                    _partsWarpBox = true;
                    _partsWarpBoxStart = evt.mousePosition;
                    _partsWarpBoxEnd = evt.mousePosition;
                    _partsDragActive = true;
                    _partsCanvasHotControl = controlId;
                    GUIUtility.hotControl = controlId;
                    _partsDragSlotId = slot.SlotId;
                    _partsDragStartMouse = evt.mousePosition;
                    evt.Use();
                    Repaint();
                    return;
                }

                // Move with IK on: the part and its parents turn so the grabbed point follows the mouse.
                if (_partsCanvasTool == PartsCanvasTool.Move && _partsIkOn
                    && (handle == ColliderHandleKind.None || handle == ColliderHandleKind.Body)
                    && TryBeginPartsIk(canvas, slot, evt, controlId))
                {
                    evt.Use();
                    Repaint();
                    return;
                }

                if (handle == ColliderHandleKind.None)
                    handle = ColliderHandleKind.Body;
                _partsTransformHandle = handle;
                _partsDragActive = true;
                _partsDragShiftAxis = 0;
                _partsCanvasHotControl = controlId;
                GUIUtility.hotControl = controlId;
                _partsDragSlotId = slot.SlotId;
                _partsDragStartMouse = evt.mousePosition;
                _partsDragStartPose = SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
                CapturePartsDragStartTransform(canvas, slot.SlotId);
                BeginPartsGroupMoveCapture(handle, slot);
                if (handle == ColliderHandleKind.AxisX || handle == ColliderHandleKind.AxisY)
                {
                    float wd = -_partsDragStartGuiDeg;
                    _partsDragAxisWorld = handle == ColliderHandleKind.AxisX
                        ? PartsGizmoAxisX(wd) : PartsGizmoAxisY(wd);
                }
                else
                    _partsDragAxisWorld = default;
                bool isRotateHandle =
                    handle == ColliderHandleKind.Rotate ||
                    handle == ColliderHandleKind.RotateX ||
                    handle == ColliderHandleKind.RotateY ||
                    handle == ColliderHandleKind.RotateZ ||
                    handle == ColliderHandleKind.RotateSphere;
                bool isAxisMove =
                    handle == ColliderHandleKind.AxisX || handle == ColliderHandleKind.AxisY;
                bool isFreeMove = handle == ColliderHandleKind.FreeMove;
                string op = _partsCanvasTool == PartsCanvasTool.Rotate || isRotateHandle
                    ? "Rotate Parts"
                    : (_partsCanvasTool == PartsCanvasTool.Scale && !isAxisMove && !isFreeMove) ||
                      (handle != ColliderHandleKind.Body && handle != ColliderHandleKind.None &&
                       !isRotateHandle && !isAxisMove && !isFreeMove)
                        ? "Scale Parts"
                        : "Move Parts";
                BeginPartsDragUndoDeferred(_partsMode == SpritePartsStudioMode.Rig
                    ? op + " Rest"
                    : op + " Key");
                _status = op + ": " + (slot.Name ?? slot.SlotId);
                evt.Use();
                Repaint();
            }
        }

        string SlotIdFromHit(int hitIndex)
        {
            if (hitIndex < 0 || _profile?.PartsSlots == null) return null;
            // Prefer blob SlotId when sampling so order mismatches don't pick the wrong part.
            if (SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
            {
                try
                {
                    if (hitIndex < blob.Value.Slots.Length)
                        return blob.Value.Slots[hitIndex].SlotId.ToString();
                }
                finally
                {
                    SpritePartsOnion.DisposeSample(blob, poses, matrices);
                }
            }
            if (hitIndex < _profile.PartsSlots.Count && _profile.PartsSlots[hitIndex] != null)
                return _profile.PartsSlots[hitIndex].SlotId;
            return null;
        }

        static int BlobSlotIndex(ref SpritePartsSetBlob set, string slotId)
        {
            string id = SpritePartIdUtility.Canonical(slotId);
            for (int i = 0; i < set.Slots.Length; i++)
            {
                if (SpritePartIdUtility.Canonical(set.Slots[i].SlotId.ToString()) == id)
                    return i;
            }
            return -1;
        }

        void CapturePartsDragStartTransform(Rect canvas, string slotId)
        {
            _partsDragStartJoint = Event.current != null ? Event.current.mousePosition : canvas.center;
            _partsDragStartGuiDeg = 0f;
            _partsDragStartWorld = default;
            _partsDragParentToRoot = float4x4.identity;
            _partsDragHasParent = false;
            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
                return;
            try
            {
                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                int idx = BlobSlotIndex(ref blob.Value, slotId);
                if (idx < 0 || idx >= matrices.Length) return;
                _partsDragStartWorld = matrices[idx].c3.xy;
                if (TryGetPartsSlotDrawRect(canvas, ref blob.Value, matrices, idx, _partsPreviewTime,
                        out _, out var joint, out float worldDeg, out _, out _, poses))
                {
                    _partsDragStartJoint = joint;
                    _partsDragStartGuiDeg = -worldDeg;
                }
                int parent = blob.Value.Slots[idx].ParentSlotIndex;
                if (parent >= 0 && parent < matrices.Length)
                {
                    _partsDragHasParent = true;
                    _partsDragParentToRoot = matrices[parent];
                }
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        void ApplyPartsTransformDrag(Rect canvas, Vector2 mouse)
        {
            if (_partsIkActive)
            {
                ApplyPartsIk(mouse);
                return;
            }
            if (_partsCanvasTool == PartsCanvasTool.Warp)
            {
                if (_partsWarpActive)
                    ApplyPartsWarpDrag(canvas, mouse);
                return;
            }
            var pose = _partsDragStartPose;
            var handle = _partsTransformHandle;
            bool rotate = handle == ColliderHandleKind.Rotate ||
                          handle == ColliderHandleKind.RotateX ||
                          handle == ColliderHandleKind.RotateY ||
                          handle == ColliderHandleKind.RotateZ ||
                          handle == ColliderHandleKind.RotateSphere ||
                          (handle == ColliderHandleKind.Body && _partsCanvasTool == PartsCanvasTool.Rotate);
            bool axisMove = handle == ColliderHandleKind.AxisX || handle == ColliderHandleKind.AxisY;
            // Scale from knobs, OR Body + Scale tool (uniform linked scale).
            bool scale = (handle != ColliderHandleKind.None &&
                          handle != ColliderHandleKind.Body &&
                          handle != ColliderHandleKind.FreeMove &&
                          handle != ColliderHandleKind.Rotate &&
                          handle != ColliderHandleKind.RotateX &&
                          handle != ColliderHandleKind.RotateY &&
                          handle != ColliderHandleKind.RotateZ &&
                          handle != ColliderHandleKind.RotateSphere &&
                          handle != ColliderHandleKind.AxisX &&
                          handle != ColliderHandleKind.AxisY) ||
                         (handle == ColliderHandleKind.Body && _partsCanvasTool == PartsCanvasTool.Scale);

            if (rotate)
            {
                Vector2 a = _partsDragStartMouse - _partsDragStartJoint;
                Vector2 b = mouse - _partsDragStartJoint;
                float deg = (Mathf.Atan2(b.y, b.x) - Mathf.Atan2(a.y, a.x)) * Mathf.Rad2Deg;
                pose.Rotation = _partsDragStartPose.Rotation - deg;
            }
            else if (scale)
            {
                Vector2 s = UnrotateAround(_partsDragStartMouse, _partsDragStartJoint, _partsDragStartGuiDeg)
                            - _partsDragStartJoint;
                Vector2 n = UnrotateAround(mouse, _partsDragStartJoint, _partsDragStartGuiDeg)
                            - _partsDragStartJoint;
                float sx = _partsDragStartPose.Scale.x;
                float sy = _partsDragStartPose.Scale.y;
                bool edgeX = handle == ColliderHandleKind.EdgeL || handle == ColliderHandleKind.EdgeR;
                bool edgeY = handle == ColliderHandleKind.EdgeT || handle == ColliderHandleKind.EdgeB;
                bool corner = !edgeX && !edgeY;
                bool bodyUniform = handle == ColliderHandleKind.Body &&
                                   _partsCanvasTool == PartsCanvasTool.Scale;
                if (_partsLinkedScale || corner || bodyUniform)
                {
                    float f = n.magnitude / Mathf.Max(1e-3f, s.magnitude);
                    f = Mathf.Max(0.01f, f);
                    float signX = sx < 0f ? -1f : 1f;
                    float signY = sy < 0f ? -1f : 1f;
                    sx = signX * Mathf.Max(0.01f, Mathf.Abs(_partsDragStartPose.Scale.x) * f);
                    sy = signY * Mathf.Max(0.01f, Mathf.Abs(_partsDragStartPose.Scale.y) * f);
                }
                else
                {
                    float fx = Mathf.Abs(s.x) < 1e-3f ? 1f : n.x / s.x;
                    float fy = Mathf.Abs(s.y) < 1e-3f ? 1f : n.y / s.y;
                    float absFx = Mathf.Max(0.01f, Mathf.Abs(fx));
                    float absFy = Mathf.Max(0.01f, Mathf.Abs(fy));
                    float signX = sx < 0f ? -1f : 1f;
                    float signY = sy < 0f ? -1f : 1f;
                    if (!edgeY) sx = signX * Mathf.Max(0.01f, Mathf.Abs(_partsDragStartPose.Scale.x) * absFx);
                    if (!edgeX) sy = signY * Mathf.Max(0.01f, Mathf.Abs(_partsDragStartPose.Scale.y) * absFy);
                }
                pose.Scale = new Vector2(sx, sy);
            }
            else if (axisMove)
            {
                float2 deltaWorld = CanvasToWorld(canvas, mouse) -
                                    CanvasToWorld(canvas, _partsDragStartMouse);
                float2 axis = handle == ColliderHandleKind.AxisX
                    ? PartsGizmoAxisX(-_partsDragStartGuiDeg)
                    : PartsGizmoAxisY(-_partsDragStartGuiDeg);
                if (math.lengthsq(_partsDragAxisWorld) > 1e-6f)
                    axis = math.normalizesafe(_partsDragAxisWorld);
                else
                    axis = math.normalizesafe(axis);
                float amount = math.dot(deltaWorld, axis);
                deltaWorld = axis * amount;
                float2 newWorld = _partsDragStartWorld + deltaWorld;
                if (!_partsDragHasParent)
                    pose.Position = new Vector2(newWorld.x, newWorld.y);
                else
                {
                    float4x4 inv = math.inverse(_partsDragParentToRoot);
                    float4 local = math.mul(inv, new float4(newWorld.x, newWorld.y, 0f, 1f));
                    pose.Position = new Vector2(local.x, local.y);
                }
            }
            else
            {
                // Frozen start world + mouse delta (never re-sample live matrices mid-drag).
                float2 deltaWorld = CanvasToWorld(canvas, mouse) -
                                    CanvasToWorld(canvas, _partsDragStartMouse);
                // Center FreeMove = Unity plane handle: any XY (skip Lock X/Y + Shift sticky).
                if (handle != ColliderHandleKind.FreeMove)
                    deltaWorld = ConstrainPartsMoveDelta(deltaWorld);
                float2 newWorld = _partsDragStartWorld + deltaWorld;
                if (!_partsDragHasParent)
                    pose.Position = new Vector2(newWorld.x, newWorld.y);
                else
                {
                    float4x4 inv = math.inverse(_partsDragParentToRoot);
                    float4 local = math.mul(inv, new float4(newWorld.x, newWorld.y, 0f, 1f));
                    pose.Position = new Vector2(local.x, local.y);
                }
            }
            if (_partsGroupMoveMembers.Count > 1 && !rotate && !scale)
            {
                ApplyPartsGroupMovePreview(pose, canvas, mouse, handle, axisMove);
                return;
            }
            ApplyPartsPoseEdit(_partsDragSlotId, pose);
        }

        void BeginPartsGroupMoveCapture(ColliderHandleKind handle, SpritePartSlotDef slot)
        {
            _partsGroupMoveMembers.Clear();
            _partsGroupTempPoses.Clear();
            bool moveHandle = handle == ColliderHandleKind.Body ||
                              handle == ColliderHandleKind.FreeMove ||
                              handle == ColliderHandleKind.AxisX ||
                              handle == ColliderHandleKind.AxisY ||
                              handle == ColliderHandleKind.None;
            if (!moveHandle || IsPartsIsolating() || slot == null)
                return;
            string gid = SpritePartsAuthoringOps.SlotGroupId(slot);
            if (string.IsNullOrEmpty(gid) ||
                SpritePartsAuthoringOps.FindGroup(_profile, gid) == null)
                return;
            var members = SpritePartsAuthoringOps.GetGroupMemberSlotIds(_profile, gid);
            if (members.Count < 2)
                return;
            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(),
                    _partsPreviewTime, Allocator.Temp,
                    out var blob, out var poses, out var matrices, out _))
                return;
            try
            {
                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                for (int i = 0; i < members.Count; i++)
                {
                    string id = members[i];
                    var memberSlot = SpritePartsAuthoringOps.FindSlot(_profile, id);
                    if (memberSlot == null || memberSlot.EditorLocked ||
                        SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, id))
                        continue;
                    int idx = BlobSlotIndex(ref blob.Value, id);
                    var m = new PartsGroupMoveMember
                    {
                        SlotId = id,
                        StartPose = SampleLocalPoseForSlot(id, _partsPreviewTime),
                    };
                    if (idx >= 0 && idx < matrices.Length)
                    {
                        m.StartWorld = matrices[idx].c3.xy;
                        int parent = blob.Value.Slots[idx].ParentSlotIndex;
                        if (parent >= 0 && parent < matrices.Length)
                        {
                            m.HasParent = true;
                            m.ParentToRoot = matrices[parent];
                        }
                    }
                    _partsGroupMoveMembers.Add(m);
                }
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        void ApplyPartsGroupMovePreview(
            SpritePartsAuthoringOps.PoseEdit draggedPose,
            Rect canvas, Vector2 mouse,
            ColliderHandleKind handle, bool axisMove)
        {
            float2 deltaWorld = CanvasToWorld(canvas, mouse) -
                                CanvasToWorld(canvas, _partsDragStartMouse);
            if (axisMove)
            {
                float2 axis = handle == ColliderHandleKind.AxisX
                    ? PartsGizmoAxisX(-_partsDragStartGuiDeg)
                    : PartsGizmoAxisY(-_partsDragStartGuiDeg);
                if (math.lengthsq(_partsDragAxisWorld) > 1e-6f)
                    axis = math.normalizesafe(_partsDragAxisWorld);
                else
                    axis = math.normalizesafe(axis);
                deltaWorld = axis * math.dot(deltaWorld, axis);
            }
            else if (handle != ColliderHandleKind.FreeMove)
                deltaWorld = ConstrainPartsMoveDelta(deltaWorld);

            _partsGroupTempPoses.Clear();
            for (int i = 0; i < _partsGroupMoveMembers.Count; i++)
            {
                var m = _partsGroupMoveMembers[i];
                var pose = m.StartPose;
                float2 newWorld = m.StartWorld + deltaWorld;
                if (!m.HasParent)
                    pose.Position = new Vector2(newWorld.x, newWorld.y);
                else
                {
                    float4x4 inv = math.inverse(m.ParentToRoot);
                    float4 local = math.mul(inv, new float4(newWorld.x, newWorld.y, 0f, 1f));
                    pose.Position = new Vector2(local.x, local.y);
                }
                _partsGroupTempPoses[SpritePartIdUtility.Canonical(m.SlotId)] = pose;
                if (SpritePartIdUtility.Canonical(m.SlotId) ==
                    SpritePartIdUtility.Canonical(_partsDragSlotId))
                    draggedPose = pose;
            }
            _partsHasTempPose = true;
            _partsTempSlotId = _partsDragSlotId;
            _partsTempPose = draggedPose;
        }

        void CommitPartsGroupMoveOffsets()
        {
            int moved = 0;
            for (int i = 0; i < _partsGroupMoveMembers.Count; i++)
            {
                var m = _partsGroupMoveMembers[i];
                string id = SpritePartIdUtility.Canonical(m.SlotId);
                if (!_partsGroupTempPoses.TryGetValue(id, out var pose))
                    continue;
                Vector2 delta = pose.Position - m.StartPose.Position;
                if (delta.sqrMagnitude < 1e-8f)
                    continue;
                int wrote = SpritePartsAuthoringOps.OffsetSlotLocalPosition(
                    _profile, id, delta, includeRest: true, clipIndex: -1);
                if (wrote > 0)
                    moved++;
            }
            _partsGroupTempPoses.Clear();
            _partsHasTempPose = false;
            if (moved > 0)
                SaveDirty();
        }

        int HitTestPartsSlot(Rect canvas, Vector2 mouse)
        {
            // Sample live pose for hit tests (ghosts never pickable).
            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
                return -1;
            try
            {
                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                int n = blob.Value.Slots.Length;
                var order = new int[n];
                var ranks = new int[n];
                for (int i = 0; i < n; i++)
                {
                    order[i] = i;
                    ranks[i] = -PreviewDrawRank(ref blob.Value, i, _partsPreviewTime); // front-first hit
                }
                System.Array.Sort(ranks, order);
                for (int o = 0; o < order.Length; o++)
                {
                    int i = order[o];
                    string sid = blob.Value.Slots[i].SlotId.ToString();
                    if (SpritePartsAuthoringOps.SlotOrAncestorHidden(_profile, sid))
                        continue;
                    if (!TryGetPartsSlotDrawRect(canvas, ref blob.Value, matrices, i, _partsPreviewTime,
                            out var r, out var joint, out float worldDeg, out _, out _, poses))
                        continue;
                    float guiDeg = -worldDeg;
                    Vector2 local = UnrotateAround(mouse, joint, guiDeg);
                    // Match DrawPartsPoseQuads ScaleAroundPivot flips so Body hit tracks the sprite.
                    if (poses.IsCreated && i < poses.Length)
                    {
                        if (poses[i].Scale.x < 0f)
                            local.x = joint.x - (local.x - joint.x);
                        if (poses[i].Scale.y < 0f)
                            local.y = joint.y - (local.y - joint.y);
                    }
                    if (r.Contains(local))
                        return i;
                    // Forgiving AABB of the four rotated corners (covers pivot/sign quirks).
                    Vector2 c0 = RotateAround(new Vector2(r.xMin, r.yMin), joint, guiDeg);
                    Vector2 c1 = RotateAround(new Vector2(r.xMax, r.yMin), joint, guiDeg);
                    Vector2 c2 = RotateAround(new Vector2(r.xMax, r.yMax), joint, guiDeg);
                    Vector2 c3 = RotateAround(new Vector2(r.xMin, r.yMax), joint, guiDeg);
                    float minX = Mathf.Min(Mathf.Min(c0.x, c1.x), Mathf.Min(c2.x, c3.x));
                    float maxX = Mathf.Max(Mathf.Max(c0.x, c1.x), Mathf.Max(c2.x, c3.x));
                    float minY = Mathf.Min(Mathf.Min(c0.y, c1.y), Mathf.Min(c2.y, c3.y));
                    float maxY = Mathf.Max(Mathf.Max(c0.y, c1.y), Mathf.Max(c2.y, c3.y));
                    if (mouse.x >= minX && mouse.x <= maxX && mouse.y >= minY && mouse.y <= maxY)
                        return i;
                }
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
            return -1;
        }

        SpritePartsAuthoringOps.PoseEdit SampleLocalPoseForSlot(string slotId, float time)
        {
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, slotId);
            var fallback = new SpritePartsAuthoringOps.PoseEdit
            {
                Position = slot?.RestPosition ?? Vector2.zero,
                Rotation = slot?.RestRotation ?? 0f,
                Scale = slot?.RestScale ?? Vector2.one,
            };
            if (_partsHasTempPose &&
                SpritePartIdUtility.Canonical(_partsTempSlotId) == SpritePartIdUtility.Canonical(slotId))
                return _partsTempPose;

            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), time, Allocator.Temp, true,
                    out var blob, out var poses, out var matrices, out _))
                return fallback;
            try
            {
                int idx = BlobSlotIndex(ref blob.Value, slotId);
                if (idx < 0 || idx >= poses.Length) return fallback;
                var p = poses[idx];
                return new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(p.Position.x, p.Position.y),
                    Rotation = p.Rotation,
                    Scale = new Vector2(p.Scale.x, p.Scale.y),
                    Lattice = p.Lattice,
                };
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
        }

        /// <summary>
        /// Affinity-style Move constrain: Lock X/Y checkboxes win; else Shift picks
        /// sticky dominant axis after a small threshold so the drag does not flicker.
        /// </summary>
        float2 ConstrainPartsMoveDelta(float2 deltaWorld)
        {
            if (_partsLockMoveX && _partsLockMoveY)
                return float2.zero;
            if (_partsLockMoveX)
                return new float2(0f, deltaWorld.y);
            if (_partsLockMoveY)
                return new float2(deltaWorld.x, 0f);

            bool shift = Event.current != null && Event.current.shift;
            if (!shift)
            {
                _partsDragShiftAxis = 0;
                return deltaWorld;
            }

            const float threshold = 4f; // world units; short nudges stay free briefly
            if (_partsDragShiftAxis == 0)
            {
                float ax = math.abs(deltaWorld.x);
                float ay = math.abs(deltaWorld.y);
                if (ax < threshold && ay < threshold)
                    return deltaWorld;
                _partsDragShiftAxis = ax >= ay ? 1 : 2;
            }

            if (_partsDragShiftAxis == 1)
                return new float2(deltaWorld.x, 0f);
            return new float2(0f, deltaWorld.y);
        }

        /// <summary>
        /// Animate needs a clip to write keys. Creates a default Idle clip when
        /// the profile has none so canvas drags are not silently rejected.
        /// </summary>
        bool EnsurePartsClipForAnimate()
        {
            if (_profile == null) return false;
            _profile.EnsurePartsRig();
            if (_profile.PartsClips == null)
                _profile.PartsClips = new System.Collections.Generic.List<SpritePartsClipDef>();
            if (_profile.PartsClips.Count == 0)
            {
                RecordPartsUndo("Create Parts Clip");
                _profile.PartsClips.Add(new SpritePartsClipDef
                {
                    Name = "Idle",
                    ClipId = "idle",
                    Duration = 1f,
                    Speed = 1f,
                    WrapMode = (byte)SpritePartsWrap.Loop,
                });
                _partsSelectedClip = 0;
                SaveDirty();
                _status = "Created Idle clip for Animate";
            }
            if (_partsSelectedClip < 0 || _partsSelectedClip >= _profile.PartsClips.Count)
                _partsSelectedClip = 0;
            return CurrentPartsClip != null;
        }

        void ApplyPartsPoseEdit(string slotId, SpritePartsAuthoringOps.PoseEdit pose)
        {
            if (ImportPreviewActive)
            {
                PauseForImportPreview("pose edit");
                return;
            }
            FlushPartsDragUndo(); // first real change of a drag: its undo step starts here

            // Animate: always keep a live temp overlay so the canvas moves even when
            // Auto Key is on (key->blob sample can lag / reject without a clip).
            if (_partsMode == SpritePartsStudioMode.Animate)
            {
                // Spine-style: only the channels this edit changed are keyed (a rotation does not pin the position).
                var channels = ChangedPartsChannels(slotId, pose);
                if (!EnsurePartsClipForAnimate())
                {
                    _partsHasTempPose = true;
                    _partsTempSlotId = slotId;
                    _partsTempPose = pose;
                    return;
                }

                _partsHasTempPose = true;
                _partsTempSlotId = slotId;
                _partsTempPose = pose;
                if (!_partsAutoKey)
                {
                    _status = "Temporary pose (Auto Key OFF) - Key Pose to commit";
                    return;
                }

                if (channels == SpritePartsKeyChannel.None)
                    return; // nothing moved
                var keyResult = SpritePartsAuthoringOps.ApplyPoseEdit(
                    _profile, _partsMode, _partsSelectedClip, slotId, _partsPreviewTime, pose, true,
                    _partsDisplayFps, channels);
                if (keyResult.Rejected)
                {
                    _status = keyResult.Reason;
                    return; // temp overlay still active so the drag remains visible
                }
                SnapPartsPlayheadToFrame(); // the key went on the nearest frame: show it under the needle
                if (!_partsDragActive)
                {
                    _partsHasTempPose = false;
                    SaveDirty();
                    _status = keyResult.InsertedRestAnchorAtZero
                        ? "Animate: keyed pose (with rest anchor at 0)"
                        : "Animate: keyed pose";
                }
                else if (_asset != null)
                    EditorUtility.SetDirty(_asset);
                return;
            }

            var result = SpritePartsAuthoringOps.ApplyPoseEdit(
                _profile, _partsMode, _partsSelectedClip, slotId, _partsPreviewTime, pose, _partsAutoKey,
                _partsDisplayFps);
            if (result.Rejected)
            {
                _status = result.Reason;
                return;
            }
            _partsHasTempPose = false;
            if (!_partsDragActive)
                SaveDirty();
            else if (_asset != null)
                EditorUtility.SetDirty(_asset);
            if (!_partsDragActive)
            {
                if (result.WroteRest)
                    _status = "Rig: updated rest pose (affects every clip)";
                else if (result.WroteKey)
                    _status = result.InsertedRestAnchorAtZero
                        ? "Animate: keyed pose (with rest anchor at 0)"
                        : "Animate: keyed pose";
            }
        }

        /// <summary>
        /// Drags that start on a press: the undo step opens on the first real change
        /// (<see cref="FlushPartsDragUndo"/>), so a click that moves nothing leaves no empty undo / History entry
        /// - those used to sit on top of the real keyframe edit, so Undo seemed to skip it.
        /// </summary>
        void BeginPartsDragUndoDeferred(string operation)
        {
            _partsDragUndoPending = operation;
        }

        /// <summary>Opens the pending drag undo step, if any. True when it was opened now.</summary>
        bool FlushPartsDragUndo()
        {
            if (string.IsNullOrEmpty(_partsDragUndoPending))
                return false;
            string op = _partsDragUndoPending;
            _partsDragUndoPending = null;
            BeginPartsDragUndo(op);
            return true;
        }

        string _partsDragUndoPending;

        void BeginPartsDragUndo(string operation)
        {
            // RegisterCompleteObjectUndo snapshots nested PartsSlots / clip keys.
            // RecordObject alone misses multi-frame canvas transform drags.
            BindProfileToUndoTarget();
            var target = UndoTarget;
            Undo.IncrementCurrentGroup();
            _partsDragUndoGroup = Undo.GetCurrentGroup();
            if (target != null)
            {
                Undo.RegisterCompleteObjectUndo(target, operation);
                Undo.SetCurrentGroupName(operation);
                Undo.FlushUndoRecordObjects();
                EditorUtility.SetDirty(target);
            }
            PushUndoName(operation);
        }

        void EndPartsDragUndo()
        {
            if (_partsDragUndoGroup >= 0)
            {
                Undo.CollapseUndoOperations(_partsDragUndoGroup);
                Undo.SetCurrentGroupName(Undo.GetCurrentGroupName());
            }
            SealUndoGroup();
            _partsDragUndoGroup = -1;
            SaveDirty();
        }

        void DrawArtLibrariesInspector()
        {
            if (_profile == null) return;
            int libraries = _profile.ArtLibraries?.Count ?? 0;
            if (!PartsSection("ART LIBRARIES", libraries == 0 ? "none" : libraries + " linked"))
                return;
            GUILayout.Label(
                "Opt-in shared sheets/appearances. Import from Profile remains the default copy path. Pull/Sync updates local art in one Undo. Bake flattens into the blob - play never looks up the library.",
                EditorStyles.wordWrappedMiniLabel);
            _profile.ArtLibraries ??= new List<SpriteArtLibraryLink>();
            for (int i = 0; i < _profile.ArtLibraries.Count; i++)
            {
                var link = _profile.ArtLibraries[i];
                if (link == null) continue;
                EditorGUILayout.BeginHorizontal();
                string label = string.IsNullOrEmpty(link.LibraryName) ? link.LibraryGuid : link.LibraryName;
                GUILayout.Label(label, EditorStyles.miniLabel);
                if (GUILayout.Button("Sync", GUILayout.Width(48f)))
                    SyncAttachedLibrary(link);
                if (GUILayout.Button("Detach", GUILayout.Width(56f)))
                    DetachArtLibrary(link.LibraryGuid);
                EditorGUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(link.LibraryPath))
                    GUILayout.Label(link.LibraryPath, EditorStyles.miniLabel);
            }

            EditorGUILayout.BeginHorizontal();
            _pendingLibraryAttach = (SpriteArtLibrary)EditorGUILayout.ObjectField(
                _pendingLibraryAttach, typeof(SpriteArtLibrary), false);
            using (new EditorGUI.DisabledScope(_pendingLibraryAttach == null))
            {
                if (GUILayout.Button("Attach", GUILayout.Width(56f)))
                {
                    AttachArtLibrary(_pendingLibraryAttach);
                    _pendingLibraryAttach = null;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        internal void AttachArtLibrary(SpriteArtLibrary library)
        {
            if (library == null || _profile == null) return;
            string guid = SpriteArtLibraryOps.IdentityOf(library);
            string path = SpriteArtLibraryOps.PathOf(library);
            var preview = SpriteArtLibraryOps.Attach(
                CloneProfileForPlan(_profile), library, guid, path);
            if (!preview.Ok)
            {
                _status = preview.Reason;
                EditorUtility.DisplayDialog("Attach Art Library", preview.Reason, "OK");
                return;
            }
            RecordPartsUndo("Attach Art Library");
            var result = SpriteArtLibraryOps.Attach(_profile, library, guid, path);
            if (!result.Ok)
            {
                Undo.PerformUndo();
                _status = result.Reason;
                return;
            }
            SaveDirty();
            _status = result.Summary;
            Repaint();
        }

        internal void DetachArtLibrary(string guid)
        {
            if (_profile == null) return;
            RecordPartsUndo("Detach Art Library");
            var result = SpriteArtLibraryOps.Detach(_profile, guid);
            if (!result.Ok)
            {
                Undo.PerformUndo();
                _status = result.Reason;
                return;
            }
            SaveDirty();
            _status = result.Summary;
            Repaint();
        }

        internal void SyncAttachedLibrary(SpriteArtLibraryLink link)
        {
            if (link == null || _profile == null) return;
            var library = SpriteArtLibraryOps.LoadByGuid(link.LibraryGuid);
            if (library == null)
            {
                _status = "Library asset is missing: " +
                          (string.IsNullOrEmpty(link.LibraryName) ? link.LibraryGuid : link.LibraryName);
                EditorUtility.DisplayDialog("Sync Art Library", _status, "OK");
                return;
            }
            SyncArtLibrary(library, link.LibraryGuid, link.LibraryPath);
        }

        internal void SyncArtLibrary(SpriteArtLibrary library, string guid = null, string path = null)
        {
            if (library == null || _profile == null) return;
            guid = string.IsNullOrWhiteSpace(guid) ? SpriteArtLibraryOps.IdentityOf(library) : guid;
            path = path ?? SpriteArtLibraryOps.PathOf(library);
            var plan = SpriteArtLibraryOps.PlanSync(_profile, library, guid);
            if (!plan.Ok)
            {
                _status = plan.Reason;
                EditorUtility.DisplayDialog("Sync Art Library", plan.Reason, "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                    "Pull / Sync from Library",
                    plan.Summary + Environment.NewLine + Environment.NewLine +
                    (plan.Added.Count > 0 ? "Added: " + string.Join(", ", plan.Added.ToArray()) + Environment.NewLine : "") +
                    (plan.Updated.Count > 0 ? "Updated: " + string.Join(", ", plan.Updated.ToArray()) + Environment.NewLine : "") +
                    (plan.Reused.Count > 0 ? "Reused: " + string.Join(", ", plan.Reused.ToArray()) + Environment.NewLine : "") +
                    "One Undo restores the profile. Local-only art is not deleted.",
                    "Sync", "Cancel"))
                return;
            RecordPartsUndo("Sync Art Library " + library.name);
            var result = SpriteArtLibraryOps.ApplySync(
                plan, _profile, library,
                new SpriteProfileArtImport.ImportSourceInfo
                {
                    Guid = guid ?? string.Empty,
                    Path = path ?? string.Empty,
                });
            if (!result.Ok)
            {
                Undo.PerformUndo();
                _status = result.Reason;
                return;
            }
            SaveDirty();
            _status = "Synced " + library.name + ": " + result.Summary;
            Repaint();
        }

        internal void ApplyPartsClipRetarget(
            ScriptableSpriteSheetProfile sourceAsset,
            int sourceClipIndex,
            Dictionary<string, string> slotMap,
            SpritePartsClipRetarget.RestDeltaMode mode)
        {
            if (sourceAsset?.Data == null || _profile == null) return;
            ClearImportPreview();
            var plan = SpritePartsClipRetarget.PlanRetarget(
                sourceAsset.Data, sourceClipIndex, _profile, slotMap, mode);
            if (!plan.Ok)
            {
                _status = plan.Reason;
                EditorUtility.DisplayDialog("Retarget Parts Clip", plan.Reason, "OK");
                return;
            }
            RecordPartsUndo("Retarget Parts Clip");
            string path = AssetDatabase.GetAssetPath(sourceAsset);
            var result = SpritePartsClipRetarget.Apply(
                plan, sourceAsset.Data, _profile,
                new SpriteProfileArtImport.ImportSourceInfo
                {
                    Guid = AssetDatabase.AssetPathToGUID(path) ?? string.Empty,
                    Path = path ?? string.Empty,
                });
            if (!result.Ok)
            {
                Undo.PerformUndo();
                _status = result.Reason;
                return;
            }
            _partsSelectedClip = result.DestinationClipIndex;
            SaveDirty();
            _status = result.Summary;
            Repaint();
        }

        internal void ApplyOutfitMapping(
            ScriptableSpriteSheetProfile sourceAsset, int sourceSkinIndex)
        {
            if (sourceAsset?.Data == null || _profile == null) return;
            var plan = SpritePartsOutfitMapping.PlanApply(sourceAsset.Data, sourceSkinIndex, _profile);
            if (!plan.Ok)
            {
                _status = plan.Reason;
                EditorUtility.DisplayDialog("Apply Outfit", plan.Reason, "OK");
                return;
            }
            string extras = plan.Unmapped.Count > 0
                ? Environment.NewLine + Environment.NewLine + "Unmapped (unchanged):" + Environment.NewLine +
                  "- " + string.Join(Environment.NewLine + "- ", plan.Unmapped.ToArray())
                : string.Empty;
            if (!EditorUtility.DisplayDialog(
                    "Apply Outfit",
                    plan.Summary + extras + Environment.NewLine + Environment.NewLine +
                    "Appearance/skin binding only. Motion curves are not retargeted.",
                    "Apply", "Cancel"))
                return;
            RecordPartsUndo("Apply Outfit");
            string path = AssetDatabase.GetAssetPath(sourceAsset);
            var result = SpritePartsOutfitMapping.Apply(
                plan, sourceAsset.Data, _profile,
                new SpriteProfileArtImport.ImportSourceInfo
                {
                    Guid = AssetDatabase.AssetPathToGUID(path) ?? string.Empty,
                    Path = path ?? string.Empty,
                });
            if (!result.Ok)
            {
                Undo.PerformUndo();
                _status = result.Reason;
                return;
            }
            SaveDirty();
            _status = result.Summary;
            Repaint();
        }

        internal void BakePartsClipToFrame(int partsClipIndex, float fps)
        {
            if (_profile == null) return;
            var plan = SpritePartsToFrameBake.PlanBake(_profile, partsClipIndex, fps);
            if (!plan.Ok)
            {
                _status = plan.Reason;
                EditorUtility.DisplayDialog("Bake Parts Clip to Frame Clip", plan.Reason, "OK");
                return;
            }
            string unsupported = plan.Unsupported.Count > 0
                ? Environment.NewLine + Environment.NewLine + "Not transferred:" + Environment.NewLine +
                  "- " + string.Join(Environment.NewLine + "- ", plan.Unsupported.ToArray())
                : string.Empty;
            if (!EditorUtility.DisplayDialog(
                    "Bake Parts Clip to Frame Clip",
                    plan.Summary + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, plan.Notes.ToArray()) +
                    unsupported,
                    "Bake", "Cancel"))
                return;
            RecordPartsUndo("Bake Parts Clip to Frame Clip");
            string ownPath = _asset != null ? AssetDatabase.GetAssetPath(_asset) : string.Empty;
            var result = SpritePartsToFrameBake.Apply(
                plan, _profile,
                new SpriteProfileArtImport.ImportSourceInfo
                {
                    Guid = string.IsNullOrEmpty(ownPath) ? string.Empty : AssetDatabase.AssetPathToGUID(ownPath),
                    Path = ownPath,
                });
            if (!result.Ok)
            {
                Undo.PerformUndo();
                _status = result.Reason;
                return;
            }
            PersistBakedFrameTexture(result.Texture, plan.DestinationSheetName);
            SaveDirty();
            _status = result.Summary + ". Runtime AnimKind is unchanged.";
            Repaint();
        }

        void PersistBakedFrameTexture(Texture2D texture, string sheetName)
        {
            if (texture == null || _asset == null)
                return;
            string assetPath = AssetDatabase.GetAssetPath(_asset);
            if (string.IsNullOrEmpty(assetPath))
                return;
            string dir = System.IO.Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(dir))
                return;
            string file = SanitizeFileName(sheetName) + ".png";
            string pngPath = dir.Replace("\\", "/") + "/" + file;
            pngPath = AssetDatabase.GenerateUniqueAssetPath(pngPath);
            System.IO.File.WriteAllBytes(pngPath, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(pngPath);
            var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            if (imported == null || _profile.Sheets == null || _profile.Sheets.Count == 0)
                return;
            _profile.Sheets[_profile.Sheets.Count - 1].Texture = imported;
        }

        static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "PartsBake";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        static SpriteSheetProfile CloneProfileForPlan(SpriteSheetProfile profile)
        {
            var copy = new SpriteSheetProfile();
            copy.ArtLibraries = new List<SpriteArtLibraryLink>();
            if (profile.ArtLibraries != null)
            {
                for (int i = 0; i < profile.ArtLibraries.Count; i++)
                {
                    var link = profile.ArtLibraries[i];
                    if (link == null) continue;
                    copy.ArtLibraries.Add(new SpriteArtLibraryLink
                    {
                        LibraryGuid = link.LibraryGuid,
                        LibraryPath = link.LibraryPath,
                        LibraryName = link.LibraryName,
                    });
                }
            }
            return copy;
        }

        /// <summary>One-shot Parts mutation: full SO snapshot so transform/key/tree edits undo reliably.</summary>
        void RecordPartsUndo(string operation)
        {
            RecordDiscreteUndo(operation);
        }

        void ResolveOrWarnTempPose()
        {
            if (!_partsHasTempPose) return;
            // Scrub/clip change: require resolve - Key Pose commits, otherwise discard with status.
            _status = "Discarded temporary unkeyed pose";
            _partsHasTempPose = false;
        }

        void DrawPartsTimeline(Rect rect, int scrubControlId, int keyControlId)
        {
            if (HandlePartsTimelineHotkeys())
                return;

            // A missed canvas MouseUp can leave its passive hotControl alive and
            // swallow clicks on timeline text fields. Reclaim header clicks before
            // drawing Duration, Display FPS, Sprite Key, and Time Scale.
            var headerRect = new Rect(rect.x, rect.y, rect.width, 48f);
            var headerEvent = Event.current;
            if (headerEvent.type == EventType.MouseDown &&
                headerEvent.button == 0 &&
                headerRect.Contains(headerEvent.mousePosition))
                ReleasePartsCanvasCapture();

            EnsurePartsSession();
            EditorGUI.DrawRect(rect, new Color(0.09f, 0.1f, 0.12f));
            var clip = CurrentPartsClip;
            float y = rect.y + 6f;
            GUI.Label(new Rect(rect.x + 8f, y, 80f, 18f), "Duration", _mutedStyle);
            if (clip != null)
            {
                EditorGUI.BeginChangeCheck();
                float dur = EditorGUI.FloatField(new Rect(rect.x + 70f, y, 50f, 18f), clip.Duration);
                if (EditorGUI.EndChangeCheck())
                {
                    RecordPartsUndo("Set Parts Duration");
                    clip.Duration = Mathf.Max(1e-3f, dur);
                    SaveDirty();
                }
            }
            GUI.Label(new Rect(rect.x + 130f, y, 70f, 18f), "Display FPS", _mutedStyle);
            _partsDisplayFps = Mathf.Clamp(
                EditorGUI.FloatField(new Rect(rect.x + 205f, y, 40f, 18f), _partsDisplayFps), 1f, 120f);

            _partsAutoKey = GUI.Toggle(new Rect(rect.x + 260f, y, 70f, 18f), _partsAutoKey,
                new GUIContent("Auto Key", "Animate drags write keys (default ON)."));
            _partsKeyPoseIncludesAppearance = GUI.Toggle(new Rect(rect.x + 332f, y, 70f, 18f),
                _partsKeyPoseIncludesAppearance,
                new GUIContent("Incl. Sprite", "Key Pose also writes AppearanceId from the dropdown."));
            if (GUI.Button(new Rect(rect.x + 405f, y, 70f, 18f),
                new GUIContent("Key Pose", "Insert/update TRS key at playhead for selection."), EditorStyles.miniButton))
            {
                CommitKeyPose();
            }
            if (GUI.Button(new Rect(rect.x + 478f, y, 78f, 18f),
                new GUIContent("Key Sprite", "Upsert appearance key from profile Appearances."), EditorStyles.miniButton))
            {
                CommitKeySprite();
            }

            _partsLoopLastKey = GUI.Toggle(new Rect(rect.x + 562f, y, 78f, 18f), _partsLoopLastKey,
                new GUIContent("Loop Last",
                    "While playing, when the playhead reaches the last keyframe it wraps to the first frame (ignores trailing hold after the last key)."));
            if (GUI.Button(new Rect(rect.x + 644f, y, 64f, 18f),
                new GUIContent("Fit Dur",
                    "Set Duration to the last keyframe time across all tracks."), EditorStyles.miniButton))
            {
                FitPartsDurationToLastKey();
            }


            float navX = rect.x + 712f;
            if (GUI.Button(new Rect(navX, y, 22f, 18f),
                new GUIContent("[", "Previous keyframe ([)"), EditorStyles.miniButtonLeft))
                JumpPartsPlayheadToNeighborKey(-1);
            navX += 22f;
            if (GUI.Button(new Rect(navX, y, 22f, 18f),
                new GUIContent("]", "Next keyframe (])"), EditorStyles.miniButtonRight))
                JumpPartsPlayheadToNeighborKey(1);
            navX += 28f;
            _partsFrameStep = Mathf.Clamp(
                EditorGUI.IntField(new Rect(navX, y, 28f, 18f), _partsFrameStep), 1, 120);
            navX += 30f;
            if (GUI.Button(new Rect(navX, y, 22f, 18f),
                new GUIContent("<", "Step back N frames (Left Arrow)"), EditorStyles.miniButtonLeft))
                StepPartsPlayhead(-1, _partsFrameStep);
            navX += 22f;
            if (GUI.Button(new Rect(navX, y, 22f, 18f),
                new GUIContent(">", "Step forward N frames (Right Arrow)"), EditorStyles.miniButtonRight))
                StepPartsPlayhead(1, _partsFrameStep);

            float timeLabelX = rect.xMax - 120f;
            GUI.Label(new Rect(timeLabelX, y, 110f, 18f),
                $"t={_partsPreviewTime:F3}s", _mutedStyle);

            // Appearance picker bound to profile Appearances (same-profile sheet+cell).
            float appY = y + 20f;
            GUI.Label(new Rect(rect.x + 8f, appY, 70f, 16f), "Sprite Key", _mutedStyle);
            DrawPartsAppearancePopup(new Rect(rect.x + 80f, appY, 220f, 16f));
            if (clip != null)
            {
                GUI.Label(
                    new Rect(rect.x + 314f, appY, 72f, 16f),
                    new GUIContent("Time Scale",
                        "Parts playback multiplier. 1 = normal, 2 = twice as fast, 0.5 = half speed, 0 = paused, negative = reverse."),
                    _mutedStyle);
                EditorGUI.BeginChangeCheck();
                float timeScale = EditorGUI.FloatField(
                    new Rect(rect.x + 386f, appY, 48f, 16f), clip.Speed);
                if (EditorGUI.EndChangeCheck())
                {
                    RecordPartsUndo("Set Parts Time Scale");
                    clip.Speed = float.IsNaN(timeScale) || float.IsInfinity(timeScale)
                        ? 1f
                        : timeScale;
                    SaveDirty();
                }
            }

            DrawPartsChannelFilter(new Rect(rect.x + 446f, appY, 330f, 16f));
            DrawPartsDopesheetToggles(new Rect(rect.x + 780f, appY, 160f, 16f));
            DrawPartsGraphToggle(new Rect(rect.x + 944f, appY, 52f, 16f));
            DrawPartsBreakdown(new Rect(rect.x + 1004f, appY, 230f, 16f));
            DrawPartsPoseTools(new Rect(rect.x + 1240f, appY, 150f, 16f));
            float tracksTop = rect.y + 48f;
            float tracksHeight = rect.height - 54f;
            var tracksRect = new Rect(rect.x + 8f, tracksTop, rect.width - 16f, tracksHeight);
            DrawPartsTracks(tracksRect, clip, scrubControlId, keyControlId);
        }

        static float GetPartsClipLastKeyTime(SpritePartsClipDef clip)
        {
            if (clip?.Tracks == null) return 0f;
            float last = 0f;
            for (int t = 0; t < clip.Tracks.Count; t++)
            {
                var track = clip.Tracks[t];
                if (track?.Keys == null) continue;
                for (int k = 0; k < track.Keys.Count; k++)
                {
                    var key = track.Keys[k];
                    if (key == null) continue;
                    if (key.Time > last) last = key.Time;
                }
            }
            return last;
        }

        void FitPartsDurationToLastKey()
        {
            var clip = CurrentPartsClip;
            if (clip == null) return;
            float last = GetPartsClipLastKeyTime(clip);
            if (!(last > 1e-5f))
            {
                _status = "No keyframes to fit Duration";
                return;
            }
            float next = Mathf.Max(1e-3f, last);
            if (Mathf.Abs(clip.Duration - next) < 1e-5f)
            {
                _status = $"Duration already {next:F3}s (last key)";
                return;
            }
            RecordPartsUndo("Fit Parts Duration to Last Key");
            clip.Duration = next;
            if (_partsPreviewTime > next)
                _partsPreviewTime = next;
            SaveDirty();
            _status = $"Duration set to last key ({next:F3}s)";
        }

        void CommitKeyPose()
        {
            var slot = CurrentPartsSlot;
            if (slot == null || CurrentPartsClip == null) return;
            var pose = _partsHasTempPose &&
                       SpritePartIdUtility.Canonical(_partsTempSlotId) ==
                       SpritePartIdUtility.Canonical(slot.SlotId)
                ? _partsTempPose
                : SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
            RecordPartsUndo("Key Parts Pose");
            var result = SpritePartsAuthoringOps.WriteKeyPose(
                _profile, _partsSelectedClip, slot.SlotId, _partsPreviewTime, pose, _partsDisplayFps,
                appearanceId: _partsKeyAppearanceId,
                includeAppearance: _partsKeyPoseIncludesAppearance);
            _partsHasTempPose = false;
            SaveDirty();
            if (result.WroteKey)
                SnapPartsPlayheadToFrame();
            if (!result.WroteKey && result.Rejected)
                _status = result.Reason ?? "Key Pose failed";
            else if (result.WroteAppearance)
                _status = "Keyed pose + sprite";
            else
                _status = result.WroteKey ? "Keyed pose" : (result.Reason ?? "Key Pose failed");
        }

        void CommitKeySprite()
        {
            var slot = CurrentPartsSlot;
            if (slot == null || CurrentPartsClip == null) return;
            // Appearance channel only. Pose stays untouched; use Key Pose (+ Incl. Sprite)
            // when both channels should write at the playhead.
            RecordPartsUndo("Key Parts Sprite");
            var result = SpritePartsAuthoringOps.WriteKeySprite(
                _profile, _partsSelectedClip, slot.SlotId, _partsPreviewTime,
                _partsKeyAppearanceId, snapFps: _partsDisplayFps);
            SaveDirty();
            _status = result.WroteAppearance
                ? (string.IsNullOrEmpty(_partsKeyAppearanceId)
                    ? "Keyed sprite hold (cleared AppearanceId)"
                    : "Keyed sprite " + _partsKeyAppearanceId)
                : (result.Reason ?? "Key Sprite failed");
        }

        void DrawPartsAppearancePopup(Rect rect)
        {
            var apps = _profile?.PartsAppearances;
            var labels = new List<string> { "(hold / none)" };
            var ids = new List<string> { string.Empty };
            int selected = 0;
            if (apps != null)
            {
                for (int i = 0; i < apps.Count; i++)
                {
                    var app = apps[i];
                    if (app == null) continue;
                    string id = SpritePartIdUtility.Canonical(app.AppearanceId, app.Name);
                    string label = string.IsNullOrEmpty(app.Name) ? id : $"{app.Name} [{id}] s{app.SheetIndex}:c{app.CellIndex}";
                    labels.Add(label);
                    ids.Add(id);
                    if (SpritePartIdUtility.Canonical(_partsKeyAppearanceId) == id)
                        selected = labels.Count - 1;
                }
            }
            int next = EditorGUI.Popup(rect, selected, labels.ToArray());
            if (next >= 0 && next < ids.Count)
                _partsKeyAppearanceId = ids[next];
        }

        void DrawPartsTracks(Rect rect, SpritePartsClipDef clip, int scrubControlId, int keyControlId)
        {
            if (_profile?.PartsSlots == null || clip == null) return;
            float duration = Mathf.Max(1e-3f, clip.Duration);
            float labelW = 90f;
            float rowH = 18f;
            float rulerH = 26f;
            EditorGUI.DrawRect(rect, new Color(0.06f, 0.07f, 0.09f));

            var rulerRect = new Rect(rect.x, rect.y, rect.width, rulerH);
            // Scrub only on the ruler so key diamonds remain clickable.
            var scrubRect = new Rect(rect.x + labelW, rect.y, rect.width - labelW, rulerH);
            if (!HandlePartsEventMarkers(scrubRect, clip, duration))
                HandlePartsTimelineScrub(scrubRect, duration, scrubControlId);
            DrawPartsTimelineRuler(rulerRect, labelW, duration);
            DrawPartsRange(scrubRect, duration);
            DrawPartsEventMarkers(scrubRect, clip, duration);

            if (_partsGraphView)
            {
                DrawPartsGraph(new Rect(rect.x, rect.y + rulerH, rect.width, Mathf.Max(0f, rect.height - rulerH)), clip, duration, labelW);
                DrawPartsPlayheadNeedle(rect, labelW, duration);
                return;
            }

            var evt = Event.current;
            float tracksTop = rect.y + rulerH;
            var keyArea = new Rect(rect.x + labelW, tracksTop, rect.width - labelW,
                Mathf.Max(0f, rect.height - rulerH));

            // Keyed IK / jiggle / parameter rows sit under the part rows.
            float valueTop = tracksTop + _profile.PartsSlots.Count * rowH;
            bool valueHit = !_partsKeyDragging && HandlePartsValueRowInput(rect, clip, duration, labelW, rowH, valueTop);

            // Active key drag is owned by HandleActivePartsKeyDrag (early OnGUI).
            if (!_partsKeyDragging && !valueHit)
                HandlePartsKeyAreaInput(rect, clip, duration, labelW, rowH, rulerH, keyControlId, keyArea);

            for (int i = 0; i < _profile.PartsSlots.Count; i++)
            {
                var slot = _profile.PartsSlots[i];
                if (slot == null) continue;
                float rowY = tracksTop + i * rowH;
                if (rowY > rect.yMax - rowH) break;
                bool rowSelected = i == _partsSelectedSlot;
                if (rowSelected)
                    EditorGUI.DrawRect(new Rect(rect.x, rowY, rect.width, rowH), new Color(0.15f, 0.22f, 0.18f));

                var labelRect = new Rect(rect.x + 2f, rowY, labelW - 6f, rowH);
                GUI.Label(labelRect,
                    string.IsNullOrEmpty(slot.Name) ? slot.SlotId : slot.Name, _mutedStyle);
                if (evt.type == EventType.MouseDown && evt.button == 0 &&
                    labelRect.Contains(evt.mousePosition) && !_partsKeyDragging)
                {
                    SelectPartsSlotId(slot.SlotId, false, false);
                    evt.Use();
                    Repaint();
                }

                var track = SpritePartsAuthoringOps.FindTrack(clip, slot.SlotId);
                var trackRect = new Rect(rect.x + labelW, rowY + 2f, rect.width - labelW, rowH - 4f);
                EditorGUI.DrawRect(trackRect, new Color(0.12f, 0.13f, 0.16f));
                DrawPartsTrackFrameGrid(trackRect, duration);
                if (track?.Keys != null)
                {
                    for (int k = 0; k < track.Keys.Count; k++)
                    {
                        var key = track.Keys[k];
                        if (key == null) continue;
                        float u = key.Time / duration;
                        float kx = Mathf.Lerp(trackRect.x, trackRect.xMax, u);
                        if (!PartsKeyVisible(key))
                            continue;
                        bool keySelected = _partsSelectedKeys.Contains(key);
                        bool hasSprite = !string.IsNullOrWhiteSpace(key.AppearanceId);
                        DrawPartsKeyDiamond(kx, rowY + rowH * 0.5f, keySelected, hasSprite, PartsKeyColor(key));
                        var hit = new Rect(kx - 9f, rowY, 18f, rowH);
                        EditorGUIUtility.AddCursorRect(hit, MouseCursor.MoveArrow);
                        if (hit.Contains(evt.mousePosition))
                            GUI.Label(hit, new GUIContent(string.Empty,
                                "Click to select. Drag to move in time. Delete removes selected keys."));
                    }
                }
                else
                {
                    GUI.Label(trackRect, "  (inherits; no keys)", _mutedStyle);
                }

                // Independent appearance channel: sprite-swap keys drawn as
                // smaller squares under the pose row. Display + tooltip only -
                // they are edited through Key Sprite / sequence import.
                var channel = SpritePartsAuthoringOps.FindAppearanceTrack(clip, slot.SlotId);
                if (channel?.Keys != null)
                {
                    for (int k = 0; k < channel.Keys.Count; k++)
                    {
                        var key = channel.Keys[k];
                        if (key == null) continue;
                        float u = key.Time / duration;
                        float kx = Mathf.Lerp(trackRect.x, trackRect.xMax, u);
                        var r = new Rect(kx - 4f, rowY + rowH - 8f, 8f, 6f);
                        EditorGUI.DrawRect(r, new Color(0.95f, 0.75f, 0.2f, 0.95f));
                        if (r.Contains(evt.mousePosition))
                            GUI.Label(r, new GUIContent(string.Empty,
                                "Appearance key: " + key.AppearanceId + " (independent channel; pose untouched)"));
                    }
                }
            }

            DrawPartsValueRows(rect, clip, duration, labelW, rowH, valueTop);

            if (_partsKeyMarqueeActive)
            {
                EditorGUI.DrawRect(_partsKeyMarqueeRect, new Color(0.25f, 0.62f, 0.9f, 0.16f));
                DrawBorder(_partsKeyMarqueeRect, new Color(0.35f, 0.72f, 1f, 0.9f), 1f);
            }

            DrawPartsPlayheadNeedle(rect, labelW, duration);
        }

        void DrawPartsPlayheadNeedle(Rect rect, float labelW, float duration)
        {
            // Playhead needle + head (Clips-style yellow)
            float pu = Mathf.Clamp01(_partsPreviewTime / duration);
            float px = Mathf.Lerp(rect.x + labelW, rect.xMax, pu);
            var needle = new Color(1f, 0.85f, 0.2f, 0.95f);
            EditorGUI.DrawRect(new Rect(px - 1f, rect.y, 2f, rect.height), needle);
            Handles.BeginGUI();
            Handles.color = needle;
            Handles.DrawAAConvexPolygon(
                new Vector3(px, rect.y + 2f),
                new Vector3(px + 6f, rect.y + 12f),
                new Vector3(px - 6f, rect.y + 12f));
            Handles.EndGUI();
        }

        void HandlePartsKeyAreaInput(
            Rect rect, SpritePartsClipDef clip, float duration,
            float labelW, float rowH, float rulerH, int keyControl, Rect keyArea)
        {
            var evt = Event.current;
            if (_partsKeyMarqueeActive)
            {
                HandlePartsKeyMarqueeContinue(rect, clip, duration, labelW, rowH, rulerH, keyArea);
                return;
            }

            if (evt.type == EventType.MouseDown && keyArea.Contains(evt.mousePosition))
            {
                if (TryHitPartsKey(rect, clip, evt.mousePosition, out var key, out var slotId,
                        out var lane, out _))
                {
                    if (evt.button == 1)
                    {
                        if (!_partsSelectedKeys.Contains(key))
                        {
                            ClearPartsKeySelection();
                            _partsSelectedKeys.Add(key);
                        }
                        SelectPartsSlotId(slotId, false, false);
                        ShowPartsKeyContextMenu(key);
                        evt.Use();
                        Repaint();
                        return;
                    }
                    if (evt.button != 0) return;

                    bool add = evt.shift;
                    bool toggle = evt.control || evt.command;
                    if (toggle && _partsSelectedKeys.Contains(key))
                    {
                        _partsSelectedKeys.Remove(key);
                        evt.Use();
                        Repaint();
                        return;
                    }
                    if (!add && !toggle)
                        ClearPartsKeySelection();
                    _partsSelectedKeys.Add(key);
                    SelectPartsSlotId(slotId, false, false);
                    _partsPreviewTime = key.Time;
                    _partsPlaying = false;
                    BeginPartsKeyDrag(keyControl, lane.width, duration);
                    evt.Use();
                    Repaint();
                    return;
                }

                if (evt.button == 1)
                {
                    int row = Mathf.FloorToInt((evt.mousePosition.y - (rect.y + rulerH)) / rowH);
                    if (row >= 0 && row < _profile.PartsSlots.Count)
                    {
                        var slot = _profile.PartsSlots[row];
                        if (slot != null)
                        {
                            float u = Mathf.InverseLerp(keyArea.x, keyArea.xMax, evt.mousePosition.x);
                            float time = Mathf.Clamp01(u) * duration;
                            _partsPreviewTime = time;
                            ShowPartsTrackContextMenu(slot.SlotId, time);
                            evt.Use();
                            return;
                        }
                    }
                }

                if (evt.button == 0)
                {
                    if (evt.clickCount >= 2)
                    {
                        int row = Mathf.FloorToInt((evt.mousePosition.y - (rect.y + rulerH)) / rowH);
                        if (row >= 0 && row < _profile.PartsSlots.Count)
                        {
                            var slot = _profile.PartsSlots[row];
                            if (slot != null)
                            {
                                float u = Mathf.InverseLerp(keyArea.x, keyArea.xMax, evt.mousePosition.x);
                                float time = SpritePartsAuthoringOps.SnapTime(
                                    Mathf.Clamp01(u) * duration, _partsDisplayFps, duration);
                                SelectPartsSlotId(slot.SlotId, false, false);
                                InsertPartsKeyAtTime(slot.SlotId, time);
                                evt.Use();
                                return;
                            }
                        }
                    }

                    // Start marquee / empty click scrub-to-time.
                    _partsKeyMarqueeActive = true;
                    _partsKeyMarqueeMoved = false;
                    _partsKeyMarqueeHotControl = keyControl;
                    GUIUtility.hotControl = keyControl;
                    _partsKeyMarqueeStart = evt.mousePosition;
                    _partsKeyMarqueeRect = new Rect(evt.mousePosition, Vector2.zero);
                    _partsKeyMarqueeOp = evt.alt
                        ? SelectionOp.Subtract
                        : evt.control || evt.command
                            ? SelectionOp.Toggle
                            : evt.shift ? SelectionOp.Add : SelectionOp.Replace;
                    _partsKeyMarqueeBaseline.Clear();
                    foreach (var k in _partsSelectedKeys)
                        _partsKeyMarqueeBaseline.Add(k);
                    evt.Use();
                }
            }
        }

        void HandlePartsKeyMarqueeContinue(
            Rect rect, SpritePartsClipDef clip, float duration,
            float labelW, float rowH, float rulerH, Rect keyArea)
        {
            var evt = Event.current;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                RestorePartsKeyMarqueeBaseline();
                EndPartsKeyMarquee();
                evt.Use();
                Repaint();
                return;
            }
            if (evt.type == EventType.MouseDrag &&
                GUIUtility.hotControl == _partsKeyMarqueeHotControl)
            {
                _partsKeyMarqueeMoved |=
                    (evt.mousePosition - _partsKeyMarqueeStart).sqrMagnitude >= 9f;
                _partsKeyMarqueeRect = Rect.MinMaxRect(
                    Mathf.Min(_partsKeyMarqueeStart.x, evt.mousePosition.x),
                    Mathf.Min(_partsKeyMarqueeStart.y, evt.mousePosition.y),
                    Mathf.Max(_partsKeyMarqueeStart.x, evt.mousePosition.x),
                    Mathf.Max(_partsKeyMarqueeStart.y, evt.mousePosition.y));
                evt.Use();
                Repaint();
                return;
            }
            if (evt.type != EventType.MouseUp || evt.button != 0 ||
                GUIUtility.hotControl != _partsKeyMarqueeHotControl)
                return;

            if (!_partsKeyMarqueeMoved)
            {
                if (_partsKeyMarqueeOp == SelectionOp.Replace)
                    ClearPartsKeySelection();
                float u = Mathf.InverseLerp(keyArea.x, keyArea.xMax, evt.mousePosition.x);
                _partsPreviewTime = evt.shift
                    ? Mathf.Clamp01(u) * duration
                    : SpritePartsAuthoringOps.SnapTime(Mathf.Clamp01(u) * duration, _partsDisplayFps, duration);
                _partsPlaying = false;
                EndPartsKeyMarquee();
                evt.Use();
                Repaint();
                return;
            }

            RestorePartsKeyMarqueeBaseline();
            if (_partsKeyMarqueeOp == SelectionOp.Replace)
                _partsSelectedKeys.Clear();

            float tracksTop = rect.y + rulerH;
            for (int i = 0; i < _profile.PartsSlots.Count; i++)
            {
                var slot = _profile.PartsSlots[i];
                if (slot == null) continue;
                float rowY = tracksTop + i * rowH;
                var track = SpritePartsAuthoringOps.FindTrack(clip, slot.SlotId);
                if (track?.Keys == null) continue;
                for (int k = 0; k < track.Keys.Count; k++)
                {
                    var key = track.Keys[k];
                    if (key == null || !PartsKeyVisible(key)) continue;
                    float kx = Mathf.Lerp(rect.x + labelW, rect.xMax, key.Time / duration);
                    var point = new Vector2(kx, rowY + rowH * 0.5f);
                    if (!_partsKeyMarqueeRect.Contains(point)) continue;
                    if (_partsKeyMarqueeOp == SelectionOp.Subtract)
                        _partsSelectedKeys.Remove(key);
                    else if (_partsKeyMarqueeOp == SelectionOp.Toggle &&
                             _partsSelectedKeys.Contains(key))
                        _partsSelectedKeys.Remove(key);
                    else
                        _partsSelectedKeys.Add(key);
                }
            }
            EndPartsKeyMarquee();
            evt.Use();
            Repaint();
        }

        void HandleActivePartsTimelineScrub(int controlId)
        {
            if (!_partsScrubbing || _studioTab != StudioTab.Parts)
                return;
            var evt = Event.current;
            EventType raw = evt.rawType;
            if (raw != EventType.MouseDrag && raw != EventType.MouseUp &&
                raw != EventType.MouseLeaveWindow)
                return;

            // Rebuild scrub track rect (same math as DrawPartsTimeline / DrawPartsTracks).
            float timelineHeight = _timelinePanelHeight;
            var workRect = new Rect(
                Gap, ToolbarHeight + Gap, position.width - Gap * 2f,
                Mathf.Max(MinWorkAreaHeight,
                    position.height - ToolbarHeight - timelineHeight - Gap * 3f));
            var timelineRect = new Rect(
                Gap, workRect.yMax + Gap, position.width - Gap * 2f,
                Mathf.Max(MinTimelineHeight, position.height - (workRect.yMax + Gap) - Gap));
            float tracksTop = timelineRect.y + 48f;
            float tracksHeight = timelineRect.height - 54f;
            var tracksRect = new Rect(timelineRect.x + 8f, tracksTop, timelineRect.width - 16f, tracksHeight);
            const float labelW = 90f;
            var scrubRect = new Rect(tracksRect.x + labelW, tracksRect.y,
                tracksRect.width - labelW, tracksRect.height);
            float duration = 1f;
            var clip = CurrentPartsClip;
            if (clip != null)
                duration = Mathf.Max(1e-3f, clip.Duration);

            if (GUIUtility.hotControl != controlId)
                GUIUtility.hotControl = controlId;
            _partsScrubHotControl = controlId;

            if (raw == EventType.MouseDrag)
            {
                // Frames (so keys land under the needle); Shift scrubs freely.
                ScrubPartsPlayhead(scrubRect, duration, evt.mousePosition.x, snap: !evt.shift);
                evt.Use();
                Repaint();
                return;
            }

            ScrubPartsPlayhead(scrubRect, duration, evt.mousePosition.x, snap: !evt.shift);
            GUIUtility.hotControl = 0;
            _partsScrubHotControl = 0;
            _partsScrubbing = false;
            evt.Use();
            Repaint();
        }

        void HandlePartsTimelineScrub(Rect scrubRect, float duration, int controlId)
        {
            Event evt = Event.current;
            // Active drag is owned by HandleActivePartsTimelineScrub (runs first in OnGUI).
            if (_partsScrubbing && evt.type != EventType.MouseDown)
                return;

            if (evt.type == EventType.MouseDown && evt.button == 0 &&
                scrubRect.Contains(evt.mousePosition))
            {
                if (_partsHasTempPose) ResolveOrWarnTempPose();
                _partsScrubbing = true;
                _partsScrubHotControl = controlId;
                GUIUtility.hotControl = controlId;
                GUIUtility.keyboardControl = 0;
                GUI.FocusControl(null);
                // Scrub snaps to Display FPS frames (where keys go); hold Shift to scrub freely.
                ScrubPartsPlayhead(scrubRect, duration, evt.mousePosition.x, snap: !evt.shift);
                _partsPlaying = false;
                evt.Use();
                Repaint();
            }
        }

        /// <summary>Moves the needle onto the nearest Display FPS frame (where keys are written).</summary>
        void SnapPartsPlayheadToFrame()
        {
            var clip = CurrentPartsClip;
            if (clip == null)
                return;
            _partsPreviewTime = SpritePartsAuthoringOps.SnapTime(_partsPreviewTime, _partsDisplayFps, Mathf.Max(1e-3f, clip.Duration));
        }

        void ScrubPartsPlayhead(Rect scrubRect, float duration, float mouseX, bool snap)
        {
            float u = Mathf.InverseLerp(scrubRect.x, scrubRect.xMax, mouseX);
            float t = Mathf.Clamp01(u) * duration;
            _partsPreviewTime = snap
                ? SpritePartsAuthoringOps.SnapTime(t, _partsDisplayFps, duration)
                : Mathf.Clamp(t, 0f, duration);
        }

        void DrawPartsTimelineRuler(Rect rect, float labelW, float duration)
        {
            EditorGUI.DrawRect(rect, new Color(0.095f, 0.11f, 0.135f));
            GUI.Label(new Rect(rect.x + 4f, rect.y + 4f, labelW - 8f, 16f), "Time", _mutedStyle);

            var track = new Rect(rect.x + labelW, rect.y, rect.width - labelW, rect.height);
            float pps = track.width / Mathf.Max(1e-3f, duration);

            // Time ticks like Clips DrawRuler (0.05s minor / 0.1s medium / 0.5s major).
            int twentieths = Mathf.Max(1, Mathf.CeilToInt(duration * 20f));
            for (int i = 0; i <= twentieths; i++)
            {
                float seconds = i / 20f;
                if (seconds > duration + 1e-4f) break;
                float x = track.x + seconds * pps;
                bool major = i % 10 == 0;      // 0.5s
                bool medium = !major && i % 2 == 0; // 0.1s
                float tickY = major ? rect.y + 6f : medium ? rect.y + 12f : rect.y + 16f;
                float tickHeight = rect.yMax - tickY;
                Color tickColor = major
                    ? TextMuted
                    : medium ? new Color(0.38f, 0.43f, 0.5f) : BorderColor;
                EditorGUI.DrawRect(new Rect(x, tickY, major ? 2f : 1f, tickHeight), tickColor);
                if (major)
                    GUI.Label(new Rect(x + 4f, rect.y + 1f, 52f, 14f), $"{seconds:F1}s", _mutedStyle);
            }

            // Frame marks at Display FPS (extra detail between time ticks).
            float fps = Mathf.Max(1f, _partsDisplayFps);
            int frames = Mathf.Max(1, Mathf.CeilToInt(duration * fps));
            for (int f = 0; f <= frames; f++)
            {
                float seconds = f / fps;
                if (seconds > duration + 1e-4f) break;
                // Skip marks that already coincide with 0.05s time ticks to reduce clutter.
                float twentieth = seconds * 20f;
                if (Mathf.Abs(twentieth - Mathf.Round(twentieth)) < 0.001f)
                    continue;
                float x = track.x + seconds * pps;
                EditorGUI.DrawRect(new Rect(x, rect.yMax - 6f, 1f, 6f),
                    new Color(0.45f, 0.55f, 0.65f, 0.55f));
            }

            // Live readout under playhead
            float pu = Mathf.Clamp01(_partsPreviewTime / duration);
            float px = Mathf.Lerp(track.x, track.xMax, pu);
            int frame = Mathf.RoundToInt(_partsPreviewTime * fps);
            string tip = $"{_partsPreviewTime:F3}s  f{frame}";
            var tipSize = _mutedStyle.CalcSize(new GUIContent(tip));
            float tipX = Mathf.Clamp(px + 8f, track.x, track.xMax - tipSize.x - 2f);
            GUI.Label(new Rect(tipX, rect.y + 10f, tipSize.x + 4f, 14f), tip, _mutedStyle);
        }

        void DrawPartsTrackFrameGrid(Rect trackRect, float duration)
        {
            float fps = Mathf.Max(1f, _partsDisplayFps);
            int frames = Mathf.Max(1, Mathf.CeilToInt(duration * fps));
            // Cap density so a long clip at high FPS does not flood draw calls.
            int step = frames > 120 ? Mathf.CeilToInt(frames / 120f) : 1;
            for (int f = step; f < frames; f += step)
            {
                float u = (f / fps) / duration;
                float x = Mathf.Lerp(trackRect.x, trackRect.xMax, u);
                EditorGUI.DrawRect(new Rect(x, trackRect.y, 1f, trackRect.height),
                    new Color(1f, 1f, 1f, 0.04f));
            }
        }

        void DrawPartsKeyDiamond(float x, float y, bool selected, bool hasSpriteChange = false, Color? channelColor = null)
        {
            float s = selected ? 5.5f : 4.5f;
            Handles.BeginGUI();
            if (selected)
            {
                // Selected: a bright outline around the channel colour.
                Handles.color = new Color(0.4f, 1f, 0.55f);
                Handles.DrawAAConvexPolygon(
                    new Vector3(x, y - s - 1.5f), new Vector3(x + s + 1.5f, y),
                    new Vector3(x, y + s + 1.5f), new Vector3(x - s - 1.5f, y));
            }
            if (hasSpriteChange)
                Handles.color = new Color(1f, 0.55f, 0.15f);
            else
                Handles.color = channelColor ?? new Color(0.7f, 0.85f, 1f);
            Handles.DrawAAConvexPolygon(
                new Vector3(x, y - s), new Vector3(x + s, y),
                new Vector3(x, y + s), new Vector3(x - s, y));
            Handles.EndGUI();
        }


        const string PartsClipRenameControl = "PartsClipRenameField";
        const string PartsSlotRenameControl = "PartsRenameField";

        void HandlePartsRenameClickAway(Event input)
        {
            if (input == null || input.type != EventType.MouseDown || input.button != 0)
                return;
            if (_partsRenamingClip < 0 &&
                string.IsNullOrEmpty(_partsRenameSlotId) &&
                string.IsNullOrEmpty(_partsRenameGroupId))
                return;
            string focused = GUI.GetNameOfFocusedControl();
            if (focused == PartsClipRenameControl || focused == PartsSlotRenameControl)
                return;
            if (_partsRenamingClip >= 0)
                CommitPartsClipRename();
            if (!string.IsNullOrEmpty(_partsRenameSlotId) ||
                !string.IsNullOrEmpty(_partsRenameGroupId))
                CommitPartsRename();
        }


        void DrawPartsClipRow(int index, SpritePartsClipDef clip)
        {
            var row = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            var evt = Event.current;
            bool selected = index == _partsSelectedClip;
            bool renaming = index == _partsRenamingClip;
            Color bg = selected
                ? new Color(0.22f, 0.45f, 0.75f, 0.55f)
                : (row.Contains(evt.mousePosition) ? new Color(1f, 1f, 1f, 0.06f) : Color.clear);
            if (bg.a > 0f)
                EditorGUI.DrawRect(row, bg);

            var nameRect = new Rect(row.x + 8f, row.y + 1f, Mathf.Max(40f, row.width - 12f), 20f);
            if (renaming)
            {
                GUI.SetNextControlName(PartsClipRenameControl);
                _partsClipRenameDraft = GUI.TextField(nameRect, _partsClipRenameDraft ?? string.Empty);
                if (_partsClipRenameFocus)
                {
                    EditorGUI.FocusTextInControl(PartsClipRenameControl);
                    _partsClipRenameFocus = false;
                }
            }
            else
            {
                GUI.Label(nameRect, new GUIContent(
                    string.IsNullOrEmpty(clip.Name) ? $"Clip {index + 1}" : clip.Name,
                    "Click to select. F2 or double-click to rename."));
            }

            if (!renaming && evt.type == EventType.MouseDown && evt.button == 0 &&
                row.Contains(evt.mousePosition))
            {
                SelectPartsClip(index);
                if (evt.clickCount >= 2)
                    BeginPartsClipRename(index);
                evt.Use();
                GUI.changed = true;
                Repaint();
            }
        }

        void SelectPartsClip(int index)
        {
            if (_profile?.PartsClips == null || _profile.PartsClips.Count == 0)
                return;
            index = Mathf.Clamp(index, 0, _profile.PartsClips.Count - 1);
            if (_partsHasTempPose && index != _partsSelectedClip)
                ResolveOrWarnTempPose();
            if (_partsRenamingClip >= 0 && _partsRenamingClip != index)
                CommitPartsClipRename();
            _partsSelectedClip = index;
            _partsPreviewTime = 0f;
            _partsBrowserFocus = PartsBrowserFocus.Clips;
            ClearPartsKeySelection();
        }

        void HandlePartsClipRenameHotkeys()
        {
            if (_partsRenamingClip >= 0) return;
            if (_partsBrowserFocus != PartsBrowserFocus.Clips) return;
            var evt = Event.current;
            if (evt.type != EventType.KeyDown) return;
            if (EditorGUIUtility.editingTextField) return;
            if (evt.keyCode != KeyCode.F2) return;
            if (_profile?.PartsClips == null || _profile.PartsClips.Count == 0) return;
            BeginPartsClipRename(_partsSelectedClip);
            evt.Use();
        }

        void BeginPartsClipRename(int index)
        {
            if (_profile?.PartsClips == null || index < 0 || index >= _profile.PartsClips.Count)
                return;
            var clip = _profile.PartsClips[index];
            if (clip == null) return;
            CancelPartsRename();
            _partsRenamingClip = index;
            _partsClipRenameDraft = clip.Name ?? string.Empty;
            _partsClipRenameFocus = true;
            _partsBrowserFocus = PartsBrowserFocus.Clips;
            _partsSelectedClip = index;
            GUI.FocusControl(PartsClipRenameControl);
            Repaint();
        }

        void CommitPartsClipRename()
        {
            if (_partsRenamingClip < 0) return;
            int index = _partsRenamingClip;
            string draft = (_partsClipRenameDraft ?? string.Empty).Trim();
            _partsRenamingClip = -1;
            _partsClipRenameDraft = null;
            _partsClipRenameFocus = false;
            GUIUtility.keyboardControl = 0;
            GUI.FocusControl(null);
            if (_profile?.PartsClips == null || index < 0 || index >= _profile.PartsClips.Count)
                return;
            var clip = _profile.PartsClips[index];
            if (clip == null) return;
            if (string.IsNullOrEmpty(draft))
            {
                _status = "Clip name cannot be empty.";
                return;
            }
            if (clip.Name == draft) return;
            RecordPartsUndo("Rename Parts Clip");
            clip.Name = draft;
            SaveDirty();
            _status = "Renamed clip";
            Repaint();
        }

        void CancelPartsClipRename()
        {
            _partsRenamingClip = -1;
            _partsClipRenameDraft = null;
            _partsClipRenameFocus = false;
            GUIUtility.keyboardControl = 0;
            GUI.FocusControl(null);
            Repaint();
        }

        void AddPartsClip()
        {
            RecordPartsUndo("Add Parts Clip");
            _profile.EnsurePartsRig();
            int n = _profile.PartsClips.Count + 1;
            _profile.PartsClips.Add(new SpritePartsClipDef
            {
                Name = "Clip " + n,
                ClipId = "clip." + n,
                Duration = 1f,
                Speed = 1f,
                WrapMode = (byte)SpritePartsWrap.Loop,
                Tracks = new List<SpritePartsTrackDef>(),
            });
            SpritePartsValidation.CanonicalizeIds(_profile);
            _partsSelectedClip = _profile.PartsClips.Count - 1;
            _partsBrowserFocus = PartsBrowserFocus.Clips;
            SaveDirty();
            BeginPartsClipRename(_partsSelectedClip);
        }

        
        void StepPartsPlayhead(int direction, int frames = 1)
        {
            var clip = CurrentPartsClip;
            if (clip == null) return;
            float duration = Mathf.Max(1e-3f, clip.Duration);
            float wrapEnd = duration;
            if (_partsLoopLastKey)
            {
                float lastKey = GetPartsClipLastKeyTime(clip);
                if (lastKey > 1e-5f)
                    wrapEnd = Mathf.Max(1e-3f, lastKey);
            }
            int n = Mathf.Max(1, frames);
            float step = n / Mathf.Max(1f, _partsDisplayFps);
            _partsPreviewTime = SpritePartsAuthoringOps.SnapTime(
                _partsPreviewTime + direction * step, _partsDisplayFps, wrapEnd);
            if (_partsLoopLastKey || clip.WrapMode != (byte)SpritePartsWrap.Once)
                _partsPreviewTime = SpritePartsSampler.WrapTime(_partsPreviewTime, wrapEnd,
                    _partsLoopLastKey ? (byte)SpritePartsWrap.Loop : clip.WrapMode);
            else
                _partsPreviewTime = Mathf.Clamp(_partsPreviewTime, 0f, wrapEnd);
            _partsPlaying = false;
            Repaint();
        }

        static void CollectPartsKeyTimes(SpritePartsClipDef clip, List<float> dst)
        {
            dst.Clear();
            if (clip == null || clip.Tracks == null) return;
            for (int ti = 0; ti < clip.Tracks.Count; ti++)
            {
                var track = clip.Tracks[ti];
                if (track == null || track.Keys == null) continue;
                for (int k = 0; k < track.Keys.Count; k++)
                {
                    var key = track.Keys[k];
                    if (key == null) continue;
                    float time = key.Time;
                    bool found = false;
                    for (int i = 0; i < dst.Count; i++)
                    {
                        if (Mathf.Abs(dst[i] - time) <= 1e-4f)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                        dst.Add(time);
                }
            }
            dst.Sort();
        }

        void JumpPartsPlayheadToNeighborKey(int direction)
        {
            var clip = CurrentPartsClip;
            if (clip == null) return;
            var times = new List<float>(64);
            CollectPartsKeyTimes(clip, times);
            if (times.Count == 0)
            {
                _status = "No keyframes";
                return;
            }

            float t = _partsPreviewTime;
            const float eps = 1e-4f;
            if (direction < 0)
            {
                float best = float.NaN;
                for (int i = 0; i < times.Count; i++)
                {
                    if (times[i] < t - eps)
                        best = times[i];
                }
                if (float.IsNaN(best))
                {
                    _status = "Already at first key";
                    return;
                }
                _partsPreviewTime = best;
            }
            else
            {
                float best = float.NaN;
                for (int i = 0; i < times.Count; i++)
                {
                    if (times[i] > t + eps)
                    {
                        best = times[i];
                        break;
                    }
                }
                if (float.IsNaN(best))
                {
                    _status = "Already at last key";
                    return;
                }
                _partsPreviewTime = best;
            }

            _partsPlaying = false;
            _status = string.Format("t={0:F3}s", _partsPreviewTime);
            Repaint();
        }

        bool HandlePartsTimelineHotkeys()
        {
            Event evt = Event.current;
            if (evt.type != EventType.KeyDown)
                return false;
            if (EditorGUIUtility.editingTextField)
                return false;
            if (evt.control || evt.command)
                return false;
            switch (evt.keyCode)
            {
                case KeyCode.LeftBracket:
                    JumpPartsPlayheadToNeighborKey(-1);
                    evt.Use();
                    return true;
                case KeyCode.RightBracket:
                    JumpPartsPlayheadToNeighborKey(1);
                    evt.Use();
                    return true;
                case KeyCode.LeftArrow:
                    StepPartsPlayhead(-1, _partsFrameStep);
                    evt.Use();
                    return true;
                case KeyCode.RightArrow:
                    StepPartsPlayhead(1, _partsFrameStep);
                    evt.Use();
                    return true;
                default:
                    return false;
            }
        }

    }
}

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
        }

        enum PartsCanvasTool
        {
            Move = 0,
            Rotate = 1,
            Scale = 2,
        }

        enum PartsBrowserFocus
        {
            Clips = 0,
            Tree = 1,
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
        [SerializeField] float _partsDisplayFps = SpritePartsAuthoringOps.DefaultDisplayFps;
        // Preview: wrap playhead at last key time instead of Duration.
        [SerializeField] bool _partsLoopLastKey;
        [SerializeField] bool _partsOnionShowWhilePlaying;
        [SerializeField] PartsCanvasTool _partsCanvasTool = PartsCanvasTool.Move;
        [SerializeField] string _partsPreviewSkinId = "default";
        [SerializeField] bool _partsLinkedScale = true;
        // Move axis locks (persistent). Shift still does Affinity-style dominant-axis constrain.
        [SerializeField] bool _partsLockMoveX;
        [SerializeField] bool _partsLockMoveY;
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
            float tabX = 460f;
            var clipsRect = new Rect(tabX, 10f, 68f, 28f);
            var partsRect = new Rect(tabX + 72f, 10f, 64f, 28f);
            var clipsStyle = _studioTab == StudioTab.Clips ? _primaryStyle : _transportStyle;
            var partsStyle = _studioTab == StudioTab.Parts ? _primaryStyle : _transportStyle;
            if (GUI.Button(clipsRect, new GUIContent("Frames", "Frame flipbook authoring (frame clips)."), clipsStyle))
            {
                if (_studioTab != StudioTab.Clips && TryResolveTempPoseForSwitch())
                {
                    ClearImportPreview();
                    RecordWindowUndo("Switch to Frames tab");
                    SwapWorkspaceCameraState(toParts: false);
                    StashPartsSelection();
                    _studioTab = StudioTab.Clips;
                    _partsPlaying = false;
                    RestoreFramesSelection();
                }
            }
            if (GUI.Button(partsRect, new GUIContent("Parts", "Cutout Parts rig / animate / skins."), partsStyle))
            {
                if (_studioTab != StudioTab.Parts && TryResolveTempPoseForSwitch())
                {
                    ClearImportPreview();
                    RecordWindowUndo("Switch to Parts tab");
                    StashFramesSelection();
                    SwapWorkspaceCameraState(toParts: true);
                    _studioTab = StudioTab.Parts;
                    _playing = false;
                    RestorePartsSelectionById();
                    EnsurePartsSession();
                }
            }

            // Runtime badge: the Frames/Parts buttons above only change the editor
            // workspace. The profile's runtime mode is separate and explicit.
            bool runtimeParts = _profile != null && _profile.AnimKind == SpriteAnimKind.Parts;
            var badgeRect = new Rect(tabX + 142f, 15f, 120f, 18f);
            GUI.Label(badgeRect, new GUIContent(
                runtimeParts ? "Runtime: Parts" : "Runtime: Frames",
                "Runtime playback mode stored on the profile. Switch it with 'Use Parts for Character' / 'Use Frames for Character' in the inactive workspace banner; switching workspaces never changes it."),
                _mutedStyle);
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
            _status = kind == SpriteAnimKind.Parts
                ? "Runtime mode: Parts (frame clips kept, not baked)"
                : "Runtime mode: Frames (Parts data kept, not baked)";
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
            if (!(authored > 0f) || float.IsNaN(authored) || float.IsInfinity(authored))
                authored = 1f;
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
                EditorGUILayout.HelpBox(
                    "Preview only. Character currently uses Frames. Parts edits here are saved with the profile but do not bake until the runtime mode changes.",
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
                EditorGUILayout.HelpBox(
                    "Preview only. Character currently uses Frames. Use 'Use Parts for Character' in the left panel to activate this rig.",
                    MessageType.None);
            }

            var clip = CurrentPartsClip;
            if (clip != null && _partsMode == SpritePartsStudioMode.Animate)
            {
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.TextField("Clip Name", clip.Name);
                float duration = EditorGUILayout.FloatField("Duration", clip.Duration);
                float speed = EditorGUILayout.FloatField("Speed", clip.Speed);
                byte wrap = (byte)EditorGUILayout.Popup("Wrap", clip.WrapMode,
                    new[] { "Loop", "Once" });
                if (EditorGUI.EndChangeCheck())
                {
                    RecordPartsUndo("Edit Parts Clip");
                    clip.Name = name;
                    clip.Duration = Mathf.Max(1e-3f, duration);
                    clip.Speed = speed;
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

                // Transform first — most edited while posing.
                GUILayout.Space(6f);
                DrawPartsTransformInspector(slot, partLocked);

                GUILayout.Space(6f);
                GUILayout.Label("SELECTED PART", _sectionStyle);
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
                DrawPartsZOrderInspector(slot);
                DrawPartsArtInspector(slot);
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

            if (_partsMode == SpritePartsStudioMode.Skins)
                DrawPartsSkinsInspector();

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
            RecordWindowUndo("Change Parts Mode");
            if (_partsHasTempPose)
                ResolveOrWarnTempPose();
            _partsMode = (SpritePartsStudioMode)next;
            if (_partsMode == SpritePartsStudioMode.Skins)
                _partsSkinPreviewOverrides.Clear();
        }


        /// <summary>
        /// Unity-Transform-style Position / Rotation / Scale for the selected part.
        /// Rig edits rest; Animate edits the sampled (or temp) pose. Each row has Copy / Paste / Reset.
        /// </summary>
        void DrawPartsTransformInspector(SpritePartSlotDef slot, bool partLocked)
        {
            if (slot == null) return;

            GUILayout.Space(6f);
            GUILayout.Label("TRANSFORM", _sectionStyle);

            if (_partsMode == SpritePartsStudioMode.Skins)
            {
                EditorGUILayout.HelpBox(
                    "Joint Position/Rotation/Scale are off in Skins. Pivot (art on joint) stays editable below.",
                    MessageType.Info);
                using (new EditorGUI.DisabledScope(partLocked))
                    DrawPartsAppearancePivotInspector(slot);
                return;
            }

            bool isRig = _partsMode == SpritePartsStudioMode.Rig;
            string modeHint = isRig
                ? "Rest pose (shared by every clip)"
                : (_partsPlaying
                    ? "Playing — transform fields locked (avoids baking keys while scrubbing)"
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
            }

            // Pivot is art registration on the joint — shown here with TRS for one-stop editing.
            using (new EditorGUI.DisabledScope(partLocked))
                DrawPartsAppearancePivotInspector(slot);

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
            GUILayout.Space(6f);
            GUILayout.Label("Z ORDER / LAYER", _sectionStyle);
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
            EditorGUILayout.LabelField("Pivot", EditorStyles.boldLabel);

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
            GUILayout.Space(6f);
            GUILayout.Label("SPRITE / ART", _sectionStyle);

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
            GUILayout.Space(6f);
            GUILayout.Label("SKINS", _sectionStyle);
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
                    "Map a source skin/outfit onto this profile by semantic role (Body/Head/Weapon/Offhand). Appearance binding only — not motion retargeting."),
                GUILayout.Height(22f)))
            {
                var anchor = GUILayoutUtility.GetLastRect();
                PopupWindow.Show(anchor, new SpritePartsOutfitPopup(this));
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

            // Onion controls
            float ox = rect.x + 12f;
            float oy = rect.y + 58f;
            using (new EditorGUI.DisabledScope(_partsMode == SpritePartsStudioMode.Rig))
            {
                _partsOnionEnabled = GUI.Toggle(new Rect(ox, oy, 70f, 18f), _partsOnionEnabled, "Onion");
                ox += 74f;
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

            var canvas = new Rect(rect.x + 10f, rect.y + 84f, rect.width - 20f, rect.height - 96f);
            EditorGUI.DrawRect(canvas, new Color(0.07f, 0.08f, 0.1f));
            // Input first so Layout/Repaint still paint contents after Use().
            HandlePartsCanvasInput(canvas, partsCanvasControlId);
            DrawPartsCanvasContents(canvas);
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

        void SetPartsCanvasTool(PartsCanvasTool tool)
        {
            if (_partsCanvasTool == tool) return;
            RecordWindowUndo("Change Parts Tool");
            _partsCanvasTool = tool;
            // Switching tools mid-drag (or with a stale hotControl) must free the canvas grab
            // so Q/W/E toggles and the left tree stay clickable.
            ReleasePartsCanvasCapture();
            _status = tool == PartsCanvasTool.Move ? "Move (Q)"
                : tool == PartsCanvasTool.Rotate ? "Rotate (W)"
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
            _partsDragSlotId = null;
            _partsDragShiftAxis = 0;
            _partsCanvasHotControl = 0;
            if (GUIUtility.hotControl != 0)
                GUIUtility.hotControl = 0;
        }

        void CenterSelectedPartsSlot()
        {
            var slot = CurrentPartsSlot;
            if (slot == null) return;
            if (slot.EditorLocked ||
                SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, slot.SlotId))
            {
                _status = "Part is locked.";
                return;
            }
            if (_partsMode == SpritePartsStudioMode.Skins)
            {
                _status = "Transform tools off in Skins.";
                return;
            }
            var pose = SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
            if (pose.Position == Vector2.zero)
            {
                _status = "Already centered.";
                return;
            }
            BeginPartsDragUndo(_partsMode == SpritePartsStudioMode.Rig
                ? "Center Parts Rest" : "Center Parts Key");
            pose.Position = Vector2.zero;
            ApplyPartsPoseEdit(slot.SlotId, pose);
            EndPartsDragUndo();
            _status = "Centered " + (slot.Name ?? slot.SlotId);
            Repaint();
        }

        void ShowPartsCanvasContextMenu()
        {
            var slot = CurrentPartsSlot;
            var menu = new GenericMenu();
            if (slot == null)
            {
                menu.AddDisabledItem(new GUIContent("Center (no selection)"));
                menu.ShowAsContext();
                return;
            }
            bool locked = slot.EditorLocked ||
                SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, slot.SlotId);
            string sid = SpritePartIdUtility.Canonical(slot.SlotId);
            if (_partsMode == SpritePartsStudioMode.Skins || locked)
                menu.AddDisabledItem(new GUIContent("Center"));
            else
                menu.AddItem(new GUIContent("Center"), false, CenterSelectedPartsSlot);
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
            if (!_partsHasTempPose || string.IsNullOrEmpty(_partsTempSlotId))
                return;
            if (!localPoses.IsCreated || !matrices.IsCreated)
                return;
            int idx = BlobSlotIndex(ref set, _partsTempSlotId);
            if (idx < 0 || idx >= localPoses.Length || idx >= matrices.Length)
                return;
            var p = localPoses[idx];
            p.Position = new float2(_partsTempPose.Position.x, _partsTempPose.Position.y);
            p.Rotation = _partsTempPose.Rotation;
            p.Scale = new float2(_partsTempPose.Scale.x, _partsTempPose.Scale.y);
            localPoses[idx] = p;
            SpritePartsHierarchy.ComposeLocalToRoot(ref set, localPoses, matrices);
        }

        void DrawPartsCanvasContents(Rect canvas)
        {
            if (_profile?.PartsSlots == null || _profile.PartsSlots.Count == 0)
            {
                GUI.Label(new Rect(canvas.x + 12f, canvas.y + 12f, canvas.width - 24f, 40f),
                    "No parts yet. Create a Parts Character.", _mutedStyle);
                return;
            }

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
                    DrawPartsPoseQuads(canvas, ref blob.Value, poses, matrices,
                        new Color(0.85f, 0.95f, 1f, 1f), pickable: false, sampleTime: time);
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
                            DrawPartsPoseQuads(canvas, ref gBlob.Value, gPoses, gMats, tint, pickable: false, sampleTime: ghost.Time);
                            DrawOnionBadge(canvas, gMats, ghost);
                        }
                        finally
                        {
                            SpritePartsOnion.DisposeSample(gBlob, gPoses, gMats);
                        }
                    }
                }

                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                DrawPartsPoseQuads(canvas, ref blob.Value, poses, matrices, Color.white, pickable: true, sampleTime: time);
                DrawPartsTransformGizmo(canvas, ref blob.Value, poses, matrices);
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
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

        SpritePartSlotDef SlotDefFromBlob(ref SpritePartsSetBlob set, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= set.Slots.Length) return null;
            return SpritePartsAuthoringOps.FindSlot(_profile, set.Slots[slotIndex].SlotId.ToString());
        }

        SpritePartAppearanceDef ResolvePartsPreviewAppearance(SpritePartSlotDef slot, float sampleTime)
        {
            if (slot == null) return null;
            string id = SpritePartsAuthoringOps.ResolvePreviewAppearanceId(
                _profile, slot, _partsSelectedClip, sampleTime, _partsSkinPreviewOverrides);
            return SpritePartsAuthoringOps.FindAppearance(_profile, id);
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
            Color tint, bool pickable, float sampleTime)
        {
            // Draw by DrawRank ascending (back to front).
            int n = set.Slots.Length;
            var order = new int[n];
            var ranks = new int[n];
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
                ranks[i] = set.Slots[i].DrawRank;
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

                // Pose scale signs: Mirror H = Scale.x < 0 (east-west), Mirror V = Scale.y < 0 (north-south).
                // Use RAW worldDeg from the matrix (same as gizmo/hit). Do not +180 here — that
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
                Matrix4x4 prev = GUI.matrix;
                GUIUtility.RotateAroundPivot(-worldDeg, joint);
                if (flipSx < 0f || flipSy < 0f)
                    GUIUtility.ScaleAroundPivot(new Vector2(flipSx, flipSy), joint);
                Texture2D tex = sheet?.Texture;
                if (tex != null && app != null)
                {
                    // Keep art colors. Selection outline is drawn by the transform gizmo
                    // (Handles ignore GUI.matrix — drawing here caused a second unrotated box).
                    DrawPartsSheetCell(tex, sheet, sheet.Columns, sheet.Rows, app.CellIndex, r, tint);
                }
                else
                {
                    var col = tint;
                    if (pickable && IsPartsBlobSlotSelected(ref set, i))
                        col = new Color(0.45f, 0.9f, 0.55f, tint.a);
                    else if (pickable)
                        col = new Color(0.75f, 0.78f, 0.85f, tint.a);
                    EditorGUI.DrawRect(r, col);
                }
                GUI.matrix = prev;

                string name = set.Slots[i].Name.ToString();
                GUI.Label(new Rect(joint.x - 24f, joint.y + Mathf.Max(10f, r.height * 0.5f) + 2f, 80f, 14f),
                    name, _mutedStyle);
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
            var selected = CurrentPartsSlot;
            if (selected == null) return false;
            return SpritePartIdUtility.Canonical(set.Slots[blobIndex].SlotId.ToString()) ==
                   SpritePartIdUtility.Canonical(selected.SlotId);
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
            if (_partsCanvasTool == PartsCanvasTool.Scale)
            {
                // Unity Scale: only corner/edge knobs. Body click selects, does not scale.
                float scaleHit = PartsScaleHandleHit * PartsScaleHandleHit;
                foreach (var kind in PartsGizmoHandleOrder)
                {
                    if (kind == ColliderHandleKind.Rotate) continue;
                    if ((mouse - PartsGizmoHandle(r, joint, guiDeg, kind)).sqrMagnitude <= scaleHit)
                        return kind;
                }
                Vector2 localScale = UnrotateAround(mouse, joint, guiDeg);
                if (r.Contains(localScale))
                    return ColliderHandleKind.Body; // select / no-op scale
                return ColliderHandleKind.None;
            }
            if (_partsCanvasTool == PartsCanvasTool.Rotate)
            {
                if ((mouse - PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.Rotate)).sqrMagnitude <= hit)
                    return ColliderHandleKind.Rotate;
            }
            Vector2 local = UnrotateAround(mouse, joint, guiDeg);
            if (r.Contains(local))
                return ColliderHandleKind.Body;
            return ColliderHandleKind.None;
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
            if (_partsCanvasTool == PartsCanvasTool.Rotate)
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
                // Also free hotControl — leaving it stuck blocks Q/W/E and the tree.
                ReleasePartsCanvasCapture();
                return;
            }

            var evt = Event.current;
            EventType raw = evt.rawType;

            // Missed MouseUp leaves us "dragging" forever. End on a real MouseDown only —
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

            if (raw == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                ApplyPartsPoseEdit(_partsDragSlotId, _partsDragStartPose);
                EndPartsDragUndo();
                Undo.PerformUndo();
                ReleasePartsCanvasCapture();
                _status = "Cancelled drag";
                evt.Use();
                Repaint();
                return;
            }

            if (raw == EventType.MouseDrag)
            {
                var slot = SpritePartsAuthoringOps.FindSlot(_profile, _partsDragSlotId);
                if (slot != null)
                    ApplyPartsTransformDrag(canvas, evt.mousePosition);
                evt.Use();
                Repaint();
                return;
            }

            if (raw == EventType.MouseUp || raw == EventType.MouseLeaveWindow)
            {
                string moved = _partsDragSlotId ?? "part";
                EndPartsDragUndo();
                ReleasePartsCanvasCapture();
                _status = "Moved " + moved;
                evt.Use();
                Repaint();
            }
        }

        void HandlePartsCanvasInput(Rect canvas, int controlId)
        {
            Event evt = Event.current;
            bool ours = _partsDragActive &&
                        (GUIUtility.hotControl == controlId ||
                         GUIUtility.hotControl == _partsCanvasHotControl ||
                         GUIUtility.hotControl == 0);

            if (!canvas.Contains(evt.mousePosition) && !ours && !_partsDragActive)
                return;

            // Middle / alt pan reuses preview pan.
            if (evt.type == EventType.ScrollWheel && canvas.Contains(evt.mousePosition))
            {
                _previewZoom = Mathf.Clamp(_previewZoom * (1f - evt.delta.y * 0.08f), 0.25f, 8f);
                evt.Use();
                Repaint();
                return;
            }

            if (evt.type == EventType.ContextClick && canvas.Contains(evt.mousePosition))
            {
                int hitCtx = HitTestPartsSlot(canvas, evt.mousePosition);
                if (hitCtx >= 0)
                {
                    string sid = SlotIdFromHit(hitCtx);
                    if (!string.IsNullOrEmpty(sid))
                        SelectPartsSlotId(sid, false, false);
                }
                ShowPartsCanvasContextMenu();
                evt.Use();
                return;
            }

            if (_partsMode == SpritePartsStudioMode.Skins)
                return; // transform tools off

            // Active drag is owned by HandleActivePartsCanvasDrag (runs first in OnGUI).
            if (_partsDragActive)
                return;

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
                    // Empty miss still Body-drags the selection (Move/Rotate/Scale-uniform).
                    if (string.IsNullOrEmpty(slotId) && CurrentPartsSlot != null)
                    {
                        slotId = CurrentPartsSlot.SlotId;
                        handle = ColliderHandleKind.Body;
                    }
                }
                if (!string.IsNullOrEmpty(slotId))
                {
                    var slot = SpritePartsAuthoringOps.FindSlot(_profile, slotId);
                    SelectPartsSlotId(slotId, false, false);
                    bool locked = slot != null && (slot.EditorLocked ||
                        SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, slot.SlotId));
                    if (locked || slot == null)
                    {
                        _status = "Part is locked.";
                        evt.Use();
                    }
                    else
                    {
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
                        string op = _partsCanvasTool == PartsCanvasTool.Rotate || handle == ColliderHandleKind.Rotate
                            ? "Rotate Parts"
                            : (_partsCanvasTool == PartsCanvasTool.Scale ||
                               (handle != ColliderHandleKind.Body && handle != ColliderHandleKind.None &&
                                handle != ColliderHandleKind.Rotate))
                                ? "Scale Parts"
                                : "Move Parts";
                        BeginPartsDragUndo(_partsMode == SpritePartsStudioMode.Rig
                            ? op + " Rest"
                            : op + " Key");
                        _status = op + ": " + (slot.Name ?? slot.SlotId);
                        evt.Use();
                        Repaint();
                    }
                }
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
            var pose = _partsDragStartPose;
            var handle = _partsTransformHandle;
            bool rotate = handle == ColliderHandleKind.Rotate ||
                          (handle == ColliderHandleKind.Body && _partsCanvasTool == PartsCanvasTool.Rotate);
            // Scale from knobs, OR Body + Scale tool (uniform linked scale).
            bool scale = (handle != ColliderHandleKind.None &&
                          handle != ColliderHandleKind.Body &&
                          handle != ColliderHandleKind.Rotate) ||
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
            else
            {
                // Frozen start world + mouse delta (never re-sample live matrices mid-drag).
                float2 deltaWorld = CanvasToWorld(canvas, mouse) -
                                    CanvasToWorld(canvas, _partsDragStartMouse);
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
            ApplyPartsPoseEdit(_partsDragSlotId, pose);
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
                    ranks[i] = -blob.Value.Slots[i].DrawRank; // front-first hit
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

            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), time, Allocator.Temp,
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

        void ApplyPartsPoseEdit(string slotId, SpritePartsAuthoringOps.PoseEdit pose)
        {
            if (ImportPreviewActive)
            {
                PauseForImportPreview("pose edit");
                return;
            }
            if (_partsMode == SpritePartsStudioMode.Animate && !_partsAutoKey)
            {
                _partsHasTempPose = true;
                _partsTempSlotId = slotId;
                _partsTempPose = pose;
                _status = "Temporary pose (Auto Key OFF) - Key Pose to commit";
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
                        ? "Animate: keyed pose (+ rest anchor at 0)"
                        : "Animate: keyed pose";
            }
        }

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
            GUILayout.Label("ART LIBRARIES", _sectionStyle);
            GUILayout.Label(
                "Opt-in shared sheets/appearances. Import from Profile remains the default copy path. Pull/Sync updates local art in one Undo. Bake flattens into the blob — play never looks up the library.",
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

            float timeLabelX = rect.xMax - 120f;
            GUI.Label(new Rect(timeLabelX, y, 110f, 18f),
                $"t={_partsPreviewTime:F3}s", _mutedStyle);

            // Appearance picker bound to profile Appearances (same-profile sheet+cell).
            float appY = y + 20f;
            GUI.Label(new Rect(rect.x + 8f, appY, 70f, 16f), "Sprite Key", _mutedStyle);
            DrawPartsAppearancePopup(new Rect(rect.x + 80f, appY, 220f, 16f));

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
            HandlePartsTimelineScrub(scrubRect, duration, scrubControlId);
            DrawPartsTimelineRuler(rulerRect, labelW, duration);

            var evt = Event.current;
            float tracksTop = rect.y + rulerH;
            var keyArea = new Rect(rect.x + labelW, tracksTop, rect.width - labelW,
                Mathf.Max(0f, rect.height - rulerH));

            // Active key drag is owned by HandleActivePartsKeyDrag (early OnGUI).
            if (!_partsKeyDragging)
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
                        bool keySelected = _partsSelectedKeys.Contains(key);
                        bool hasSprite = !string.IsNullOrWhiteSpace(key.AppearanceId);
                        DrawPartsKeyDiamond(kx, rowY + rowH * 0.5f, keySelected, hasSprite);
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

            if (_partsKeyMarqueeActive)
            {
                EditorGUI.DrawRect(_partsKeyMarqueeRect, new Color(0.25f, 0.62f, 0.9f, 0.16f));
                DrawBorder(_partsKeyMarqueeRect, new Color(0.35f, 0.72f, 1f, 0.9f), 1f);
            }

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
                _partsPreviewTime = Mathf.Clamp01(u) * duration;
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
                    if (key == null) continue;
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
                ScrubPartsPlayhead(scrubRect, duration, evt.mousePosition.x, snap: false);
                evt.Use();
                Repaint();
                return;
            }

            if (evt.shift)
                ScrubPartsPlayhead(scrubRect, duration, evt.mousePosition.x, snap: true);
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
                // Continuous scrub while dragging (Clips-style). No SnapTime here "
                // Display FPS only affects step buttons / keyed snap, not the needle.
                ScrubPartsPlayhead(scrubRect, duration, evt.mousePosition.x, snap: false);
                _partsPlaying = false;
                evt.Use();
                Repaint();
            }
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

        void DrawPartsKeyDiamond(float x, float y, bool selected, bool hasSpriteChange = false)
        {
            float s = selected ? 5f : 4f;
            Handles.BeginGUI();
            if (hasSpriteChange)
                Handles.color = selected ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.55f, 0.15f);
            else
                Handles.color = selected ? new Color(0.4f, 1f, 0.55f) : new Color(0.7f, 0.85f, 1f);
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
            if (_partsRenamingClip < 0 && string.IsNullOrEmpty(_partsRenameSlotId))
                return;
            string focused = GUI.GetNameOfFocusedControl();
            if (focused == PartsClipRenameControl || focused == PartsSlotRenameControl)
                return;
            if (_partsRenamingClip >= 0)
                CommitPartsClipRename();
            if (!string.IsNullOrEmpty(_partsRenameSlotId))
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

        void StepPartsPlayhead(int direction)
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
            float step = 1f / Mathf.Max(1f, _partsDisplayFps);
            _partsPreviewTime = SpritePartsAuthoringOps.SnapTime(
                _partsPreviewTime + direction * step, _partsDisplayFps, wrapEnd);
            if (_partsLoopLastKey || clip.WrapMode != (byte)SpritePartsWrap.Once)
                _partsPreviewTime = SpritePartsSampler.WrapTime(_partsPreviewTime, wrapEnd,
                    _partsLoopLastKey ? (byte)SpritePartsWrap.Loop : clip.WrapMode);
            else
                _partsPreviewTime = Mathf.Clamp(_partsPreviewTime, 0f, wrapEnd);
            Repaint();
        }
    }
}

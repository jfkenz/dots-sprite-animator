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
        [SerializeField] bool _partsOnionShowWhilePlaying;
        [SerializeField] PartsCanvasTool _partsCanvasTool = PartsCanvasTool.Move;
        [SerializeField] string _partsPreviewSkinId = "default";
        [SerializeField] bool _partsLinkedScale = true;
        [SerializeField] int _partsArtColumns = 1;
        [SerializeField] int _partsArtRows = 1;
        [SerializeField] int _partsArtCell;

        readonly Dictionary<string, string> _partsSkinPreviewOverrides = new(StringComparer.Ordinal);
        string _partsArtSyncedSlotId;
        bool _partsDragActive;
        string _partsDragSlotId;
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

        void DrawPartsStudioTabToggle(Rect toolbarRect)
        {
            float tabX = 460f;
            var clipsRect = new Rect(tabX, 10f, 64f, 28f);
            var partsRect = new Rect(tabX + 68f, 10f, 64f, 28f);
            var clipsStyle = _studioTab == StudioTab.Clips ? _primaryStyle : _transportStyle;
            var partsStyle = _studioTab == StudioTab.Parts ? _primaryStyle : _transportStyle;
            if (GUI.Button(clipsRect, new GUIContent("Clips", "Frame flipbook authoring."), clipsStyle))
            {
                if (_studioTab != StudioTab.Clips)
                {
                    RecordWindowUndo("Switch to Clips tab");
                    _studioTab = StudioTab.Clips;
                    _partsPlaying = false;
                }
            }
            if (GUI.Button(partsRect, new GUIContent("Parts", "Cutout Parts rig / animate / skins."), partsStyle))
            {
                if (_studioTab != StudioTab.Parts)
                {
                    RecordWindowUndo("Switch to Parts tab");
                    _studioTab = StudioTab.Parts;
                    _playing = false;
                    EnsurePartsSession();
                }
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
            var clip = CurrentPartsClip;
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
            if (clip.WrapMode == (byte)SpritePartsWrap.Once)
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
            HandlePartsRenameClickAway(Event.current);
            GUILayout.BeginArea(rect);
            _partsBrowserScroll = EditorGUILayout.BeginScrollView(_partsBrowserScroll);
            GUILayout.Label("PARTS CLIPS", _sectionStyle);
            if (_profile.AnimKind != SpriteAnimKind.Parts)
            {
                EditorGUILayout.HelpBox(
                    "This profile is a flipbook. Create Parts Rig to author a cutout (frame clips stay on the asset but will not bake).",
                    MessageType.Info);
                if (GUILayout.Button("Create Parts Character (Floating Parts)"))
                {
                    RecordPartsUndo("Create Floating Parts Character");
                    SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(_profile);
                    _partsSelectedClip = 0;
                    _partsSelectedSlot = 0;
                    _partsPreviewTime = 0f;
                    _partsSkinPreviewOverrides.Clear();
                    SaveDirty();
                    _status = "Created Floating Parts character";
                }
                if (GUILayout.Button("Create Empty Parts Rig"))
                {
                    RecordPartsUndo("Create Empty Parts Rig");
                    SpritePartsAuthoringOps.CreateEmptyPartsRig(_profile);
                    SaveDirty();
                }
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
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
            GUILayout.BeginArea(rect);
            _partsInspectorScroll = EditorGUILayout.BeginScrollView(_partsInspectorScroll);
            GUILayout.Label("PARTS", _sectionStyle);
            GUILayout.Space(4f);
            DrawPartsModeToolbar();
            GUILayout.Space(8f);
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Max(72f, Mathf.Min(96f, rect.width * 0.38f));

            if (_partsMode == SpritePartsStudioMode.Rig)
                EditorGUILayout.HelpBox("Rig changes affect every clip.", MessageType.Warning);

            if (_profile.AnimKind != SpriteAnimKind.Parts)
            {
                EditorGUILayout.HelpBox("Create a Parts Character from the left panel.", MessageType.None);
                EditorGUIUtility.labelWidth = prevLabelWidth;
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
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
            }

            var slot = CurrentPartsSlot;
            if (slot != null)
            {
                GUILayout.Space(6f);
                GUILayout.Label("SELECTED PART", _sectionStyle);
                bool partLocked = slot.EditorLocked ||
                    SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, slot.SlotId);
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
                EditorGUILayout.LabelField("Sibling Order", slot.SiblingOrder.ToString());
                DrawPartsZOrderInspector(slot);
                DrawPartsArtInspector(slot);
                if (_partsMode == SpritePartsStudioMode.Rig)
                {
                    using (new EditorGUI.DisabledScope(partLocked))
                    {
                        EditorGUI.BeginChangeCheck();
                        string parent = DrawParentPopup(slot);
                        Vector2 restPos = EditorGUILayout.Vector2Field("Rest Position", slot.RestPosition);
                        float restRot = EditorGUILayout.FloatField("Rest Rotation", slot.RestRotation);
                        Vector2 restScale = EditorGUILayout.Vector2Field("Rest Scale", slot.RestScale);
                        string appearance = EditorGUILayout.TextField("Default Appearance Id", slot.DefaultAppearanceId);
                        if (EditorGUI.EndChangeCheck())
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
                            slot.RestPosition = restPos;
                            slot.RestRotation = restRot;
                            slot.RestScale = restScale;
                            slot.DefaultAppearanceId = appearance;
                            SpritePartsValidation.CanonicalizeIds(_profile);
                            SaveDirty();
                        }
                    }
                }
                else if (_partsMode == SpritePartsStudioMode.Animate)
                {
                    var pose = SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
                    using (new EditorGUI.DisabledScope(_partsDragActive))
                    {
                        EditorGUI.BeginChangeCheck();
                        Vector2 pos = EditorGUILayout.Vector2Field("Position", pose.Position);
                        float rot = EditorGUILayout.FloatField("Rotation", pose.Rotation);
                        Vector2 scale = EditorGUILayout.Vector2Field("Scale", pose.Scale);
                        if (EditorGUI.EndChangeCheck() && !_partsDragActive)
                        {
                            BeginPartsDragUndo("Edit Parts Key Pose");
                            ApplyPartsPoseEdit(slot.SlotId, new SpritePartsAuthoringOps.PoseEdit
                            {
                                Position = pos, Rotation = rot, Scale = scale,
                            });
                            EndPartsDragUndo();
                        }
                    }
                }
                else
                {
                    EditorGUI.EndChangeCheck();
                    EditorGUILayout.HelpBox("Transform tools off in Skins. Edit appearance bindings below.", MessageType.Info);
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

            if (Event.current.commandName == "ObjectSelectorClosed")
            {
                var picked = EditorGUIUtility.GetObjectPickerObject() as Texture2D;
                if (picked != null)
                    ApplyPartsSlotArt(slot, picked);
            }

            if (changed && nextTex != null)
                ApplyPartsSlotArt(slot, nextTex);

            if (!string.IsNullOrEmpty(slot.DefaultAppearanceId))
                EditorGUILayout.LabelField("Appearance Id", slot.DefaultAppearanceId);
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
        }

        void DrawPartsPreview(Rect rect, int partsCanvasControlId)
        {
            EnsurePartsSession();
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, 120f, 20f), "POSE CANVAS", _sectionStyle);

            // Tool row
            float tx = rect.x + 12f;
            float ty = rect.y + 34f;
            DrawPartsToolToggle(ref tx, ty, "Q Move", PartsCanvasTool.Move);
            DrawPartsToolToggle(ref tx, ty, "W Rotate", PartsCanvasTool.Rotate);
            DrawPartsToolToggle(ref tx, ty, "E Scale", PartsCanvasTool.Scale);
            _partsLinkedScale = GUI.Toggle(new Rect(tx, ty, 70f, 20f), _partsLinkedScale, "Link XY");

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
            _status = tool == PartsCanvasTool.Move ? "Move (Q)"
                : tool == PartsCanvasTool.Rotate ? "Rotate (W)"
                : "Scale (E)";
            Repaint();
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
                menu.AddItem(new GUIContent("Duplicate Mirrored"), false, () => DuplicatePartsSlot(sid, true));
                if (_partsMode != SpritePartsStudioMode.Skins)
                {
                    menu.AddItem(new GUIContent("Flip Horizontal"), false, () => FlipPartsSlot(sid, true, false));
                    menu.AddItem(new GUIContent("Flip Vertical"), false, () => FlipPartsSlot(sid, false, true));
                }
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Duplicate (locked)"));
                menu.AddDisabledItem(new GUIContent("Flip (locked)"));
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

        void DrawPartsCanvasContents(Rect canvas)
        {
            if (_profile?.PartsSlots == null || _profile.PartsSlots.Count == 0)
            {
                GUI.Label(new Rect(canvas.x + 12f, canvas.y + 12f, canvas.width - 24f, 40f),
                    "No parts yet. Create a Parts Character.", _mutedStyle);
                return;
            }

            var clip = CurrentPartsClip;
            int clipIndex = clip != null ? _partsSelectedClip : -1;
            float time = _partsPreviewTime;
            if (!SpritePartsOnion.TrySampleCharacter(_profile, clipIndex, time, Allocator.Temp,
                    out var blob, out var poses, out var matrices, out _))
                return;

            try
            {
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
                            DrawPartsPoseQuads(canvas, ref gBlob.Value, gMats, tint, pickable: false, sampleTime: ghost.Time);
                            DrawOnionBadge(canvas, gMats, ghost);
                        }
                        finally
                        {
                            SpritePartsOnion.DisposeSample(gBlob, gPoses, gMats);
                        }
                    }
                }

                DrawPartsPoseQuads(canvas, ref blob.Value, matrices, Color.white, pickable: true, sampleTime: time);
                DrawPartsTransformGizmo(canvas, ref blob.Value, matrices);
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
            out SpritePartAppearanceDef app, out SpriteSheetDef sheet)
        {
            rect = default;
            joint = default;
            worldDeg = 0f;
            app = null;
            sheet = null;
            if (slotIndex < 0 || slotIndex >= matrices.Length) return false;
            float4x4 m = matrices[slotIndex];
            joint = WorldToCanvas(canvas, m.c3.xy);
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
            Rect canvas, ref SpritePartsSetBlob set, NativeArray<float4x4> matrices,
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
                        out var r, out var joint, out float worldDeg, out var app, out var sheet))
                    continue;

                Matrix4x4 prev = GUI.matrix;
                GUIUtility.RotateAroundPivot(-worldDeg, joint);
                // Negative det (Flip H / mirrored dup) flips the sprite around the joint.
                float2 c0 = matrices[i].c0.xy;
                float2 c1 = matrices[i].c1.xy;
                float det2 = c0.x * c1.y - c0.y * c1.x;
                bool flipX = det2 < 0f;
                if (flipX)
                    GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), joint);
                Texture2D tex = sheet?.Texture;
                if (tex != null && app != null)
                {
                    // Keep art colors. Selection is outline-only; onion uses tint.a.
                    DrawPartsSheetCell(tex, sheet, sheet.Columns, sheet.Rows, app.CellIndex, r, tint);
                    if (pickable && IsPartsBlobSlotSelected(ref set, i))
                    {
                        Handles.BeginGUI();
                        Handles.color = new Color(0.2f, 0.9f, 0.35f, 0.9f);
                        Handles.DrawAAPolyLine(2f,
                            new Vector3(r.xMin, r.yMin), new Vector3(r.xMax, r.yMin),
                            new Vector3(r.xMax, r.yMax), new Vector3(r.xMin, r.yMax),
                            new Vector3(r.xMin, r.yMin));
                        Handles.EndGUI();
                    }
                }
                else
                {
                    var col = tint;
                    if (pickable && IsPartsBlobSlotSelected(ref set, i))
                        col = new Color(0.45f, 0.9f, 0.55f, tint.a);
                    else if (pickable)
                        col = new Color(0.75f, 0.78f, 0.85f, tint.a);
                    EditorGUI.DrawRect(r, col);
                    Handles.BeginGUI();
                    Handles.color = new Color(0f, 0f, 0f, tint.a * 0.6f);
                    Handles.DrawAAPolyLine(2f,
                        new Vector3(r.xMin, r.yMin), new Vector3(r.xMax, r.yMin),
                        new Vector3(r.xMax, r.yMax), new Vector3(r.xMin, r.yMax),
                        new Vector3(r.xMin, r.yMin));
                    Handles.EndGUI();
                }
                GUI.matrix = prev;

                string name = set.Slots[i].Name.ToString();
                GUI.Label(new Rect(r.x, r.yMax + 1f, r.width + 20f, 14f), name, _mutedStyle);
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

        bool TryGetSelectedPartsGizmo(Rect canvas, out Rect unrotated, out Vector2 joint, out float guiDeg)
        {
            unrotated = default;
            joint = default;
            guiDeg = 0f;
            if (CurrentPartsSlot == null || _profile?.PartsSlots == null)
                return false;
            if (!SpritePartsOnion.TrySampleCharacter(_profile, _partsSelectedClip, _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
                return false;
            try
            {
                int idx = SelectedPartsBlobIndex(ref blob.Value);
                if (idx < 0 || idx >= matrices.Length) return false;
                if (!TryGetPartsSlotDrawRect(canvas, ref blob.Value, matrices, idx,
                        _partsPreviewTime, out unrotated, out joint, out float worldDeg, out _, out _))
                    return false;
                guiDeg = -worldDeg;
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
                foreach (var kind in PartsGizmoHandleOrder)
                {
                    if (kind == ColliderHandleKind.Rotate) continue;
                    if ((mouse - PartsGizmoHandle(r, joint, guiDeg, kind)).sqrMagnitude <= hit)
                        return kind;
                }
            }
            else if (_partsCanvasTool == PartsCanvasTool.Rotate)
            {
                if ((mouse - PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.Rotate)).sqrMagnitude <= hit)
                    return ColliderHandleKind.Rotate;
            }
            Vector2 local = UnrotateAround(mouse, joint, guiDeg);
            if (r.Contains(local))
                return ColliderHandleKind.Body;
            return ColliderHandleKind.None;
        }

        void DrawPartsTransformGizmo(Rect canvas, ref SpritePartsSetBlob set, NativeArray<float4x4> matrices)
        {
            int sel = SelectedPartsBlobIndex(ref set);
            if (sel < 0 || sel >= matrices.Length) return;
            if (!TryGetPartsSlotDrawRect(canvas, ref set, matrices, sel, _partsPreviewTime,
                    out var r, out var joint, out float worldDeg, out _, out _))
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
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerTL), 8f), MouseCursor.ResizeUpLeft);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerTR), 8f), MouseCursor.ResizeUpRight);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerBR), 8f), MouseCursor.ResizeUpLeft);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.CornerBL), 8f), MouseCursor.ResizeUpRight);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.EdgeT), 8f), MouseCursor.ResizeVertical);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.EdgeB), 8f), MouseCursor.ResizeVertical);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.EdgeL), 8f), MouseCursor.ResizeHorizontal);
                EditorGUIUtility.AddCursorRect(HandleCursorRect(PartsGizmoHandle(r, joint, guiDeg, ColliderHandleKind.EdgeR), 8f), MouseCursor.ResizeHorizontal);
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

            var evt = Event.current;
            EventType raw = evt.rawType;
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

            if (GUIUtility.hotControl != controlId)
                GUIUtility.hotControl = controlId;
            _partsCanvasHotControl = controlId;

            if (raw == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                ApplyPartsPoseEdit(_partsDragSlotId, _partsDragStartPose);
                EndPartsDragUndo();
                Undo.PerformUndo();
                GUIUtility.hotControl = 0;
                _partsCanvasHotControl = 0;
                _partsDragActive = false;
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
                GUIUtility.hotControl = 0;
                _partsCanvasHotControl = 0;
                EndPartsDragUndo();
                _partsDragActive = false;
                _status = "Moved " + (_partsDragSlotId ?? "part");
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
                    // Move/Rotate/Scale: if hit-test misses (rotation/AABB quirks) but a part is
                    // selected, still start a Body drag on the selection - matches user intent.
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
                        _partsCanvasHotControl = controlId;
                        GUIUtility.hotControl = controlId;
                        _partsDragSlotId = slot.SlotId;
                        _partsDragStartMouse = evt.mousePosition;
                        _partsDragStartPose = SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
                        CapturePartsDragStartTransform(canvas, slot.SlotId);
                        string op = _partsCanvasTool == PartsCanvasTool.Rotate || handle == ColliderHandleKind.Rotate
                            ? "Rotate Parts"
                            : _partsCanvasTool == PartsCanvasTool.Scale ||
                              (handle != ColliderHandleKind.Body && handle != ColliderHandleKind.None)
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
            if (SpritePartsOnion.TrySampleCharacter(_profile, _partsSelectedClip, _partsPreviewTime,
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
            if (!SpritePartsOnion.TrySampleCharacter(_profile, _partsSelectedClip, _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
                return;
            try
            {
                int idx = BlobSlotIndex(ref blob.Value, slotId);
                if (idx < 0 || idx >= matrices.Length) return;
                _partsDragStartWorld = matrices[idx].c3.xy;
                if (TryGetPartsSlotDrawRect(canvas, ref blob.Value, matrices, idx, _partsPreviewTime,
                        out _, out var joint, out float worldDeg, out _, out _))
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
            bool scaleKnob = handle != ColliderHandleKind.None &&
                             handle != ColliderHandleKind.Body &&
                             handle != ColliderHandleKind.Rotate;
            bool scale = scaleKnob ||
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
                if (_partsLinkedScale || handle == ColliderHandleKind.Body)
                {
                    float f = n.magnitude / Mathf.Max(1e-3f, s.magnitude);
                    sx = Mathf.Max(0.01f, _partsDragStartPose.Scale.x * f);
                    sy = Mathf.Max(0.01f, _partsDragStartPose.Scale.y * f);
                }
                else
                {
                    float fx = Mathf.Abs(s.x) < 1e-3f ? 1f : n.x / s.x;
                    float fy = Mathf.Abs(s.y) < 1e-3f ? 1f : n.y / s.y;
                    fx = Mathf.Max(0.01f, Mathf.Abs(fx));
                    fy = Mathf.Max(0.01f, Mathf.Abs(fy));
                    if (!edgeY) sx = Mathf.Max(0.01f, _partsDragStartPose.Scale.x * fx);
                    if (!edgeX) sy = Mathf.Max(0.01f, _partsDragStartPose.Scale.y * fy);
                }
                pose.Scale = new Vector2(sx, sy);
            }
            else
            {
                // Frozen start world + mouse delta (never re-sample live matrices mid-drag).
                float2 deltaWorld = CanvasToWorld(canvas, mouse) -
                                    CanvasToWorld(canvas, _partsDragStartMouse);
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
            if (!SpritePartsOnion.TrySampleCharacter(_profile, _partsSelectedClip, _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
                return -1;
            try
            {
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
                            out var r, out var joint, out float worldDeg, out _, out _))
                        continue;
                    float guiDeg = -worldDeg;
                    Vector2 local = UnrotateAround(mouse, joint, guiDeg);
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

            if (!SpritePartsOnion.TrySampleCharacter(_profile, _partsSelectedClip, time, Allocator.Temp,
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

        void ApplyPartsPoseEdit(string slotId, SpritePartsAuthoringOps.PoseEdit pose)
        {
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
            var pose = _partsHasTempPose &&
                       SpritePartIdUtility.Canonical(_partsTempSlotId) ==
                       SpritePartIdUtility.Canonical(slot.SlotId)
                ? _partsTempPose
                : SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
            RecordPartsUndo("Key Parts Sprite");
            var result = SpritePartsAuthoringOps.WriteKeySprite(
                _profile, _partsSelectedClip, slot.SlotId, _partsPreviewTime,
                _partsKeyAppearanceId, pose, _partsDisplayFps);
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
            float step = 1f / Mathf.Max(1f, _partsDisplayFps);
            _partsPreviewTime = SpritePartsAuthoringOps.SnapTime(
                _partsPreviewTime + direction * step, _partsDisplayFps, duration);
            if (clip.WrapMode != (byte)SpritePartsWrap.Once)
                _partsPreviewTime = SpritePartsSampler.WrapTime(_partsPreviewTime, duration, clip.WrapMode);
            else
                _partsPreviewTime = Mathf.Clamp(_partsPreviewTime, 0f, duration);
            Repaint();
        }
    }
}

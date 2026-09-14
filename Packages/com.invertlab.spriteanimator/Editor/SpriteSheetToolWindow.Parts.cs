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

        [SerializeField] StudioTab _studioTab;
        [SerializeField] SpritePartsStudioMode _partsMode = SpritePartsStudioMode.Animate;
        [SerializeField] int _partsSelectedClip;
        [SerializeField] int _partsSelectedSlot = -1;
        [SerializeField] float _partsPreviewTime;
        [SerializeField] bool _partsPlaying;
        [SerializeField] bool _partsAutoKey = true;
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

        readonly Dictionary<string, string> _partsSkinPreviewOverrides = new(StringComparer.Ordinal);
        bool _partsDragActive;
        string _partsDragSlotId;
        Vector2 _partsDragStartMouse;
        SpritePartsAuthoringOps.PoseEdit _partsDragStartPose;
        int _partsDragUndoGroup = -1;
        Vector2 _partsBrowserScroll;
        Vector2 _partsInspectorScroll;
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
                    RecordProfileUndo("Create Floating Parts Character");
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
                    RecordProfileUndo("Create Empty Parts Rig");
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
                bool selected = i == _partsSelectedClip;
                var style = selected ? _clipSelectedStyle : _clipStyle;
                if (GUILayout.Button(clip.Name, style, GUILayout.Height(22f)))
                {
                    if (_partsHasTempPose)
                        ResolveOrWarnTempPose();
                    _partsSelectedClip = i;
                    _partsPreviewTime = 0f;
                }
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Clip", GUILayout.Width(70f)))
                AddPartsClip();
            if (GUILayout.Button("Delete", GUILayout.Width(60f)) && CurrentPartsClip != null && _profile.PartsClips.Count > 1)
            {
                RecordProfileUndo("Delete Parts Clip");
                _profile.PartsClips.RemoveAt(_partsSelectedClip);
                _partsSelectedClip = Mathf.Clamp(_partsSelectedClip, 0, _profile.PartsClips.Count - 1);
                SaveDirty();
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("PARTS TREE", _sectionStyle);
            DrawPartsTree();
            if (GUILayout.Button("+ Part", GUILayout.Width(70f)))
                AddPartsSlot();

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawPartsTree()
        {
            // Indent by parent depth; list in draw-rank then hierarchy.
            var roots = new List<int>();
            for (int i = 0; i < _profile.PartsSlots.Count; i++)
            {
                var s = _profile.PartsSlots[i];
                if (s == null) continue;
                if (string.IsNullOrEmpty(s.ParentSlotId))
                    roots.Add(i);
            }
            roots.Sort((a, b) => _profile.PartsSlots[a].DrawRank.CompareTo(_profile.PartsSlots[b].DrawRank));
            foreach (int root in roots)
                DrawPartsTreeNode(root, 0);
            // Orphans with missing parents
            for (int i = 0; i < _profile.PartsSlots.Count; i++)
            {
                var s = _profile.PartsSlots[i];
                if (s == null || string.IsNullOrEmpty(s.ParentSlotId)) continue;
                if (SpritePartsAuthoringOps.FindSlot(_profile, s.ParentSlotId) == null)
                    DrawPartsTreeNode(i, 0);
            }
        }

        void DrawPartsTreeNode(int index, int depth)
        {
            var slot = _profile.PartsSlots[index];
            if (slot == null) return;
            bool selected = index == _partsSelectedSlot;
            var style = selected ? _clipSelectedStyle : _clipStyle;
            string label = new string(' ', depth * 2) + slot.Name;
            if (GUILayout.Button(label, style, GUILayout.Height(20f)))
                _partsSelectedSlot = index;
            for (int i = 0; i < _profile.PartsSlots.Count; i++)
            {
                var child = _profile.PartsSlots[i];
                if (child == null) continue;
                if (SpritePartIdUtility.Canonical(child.ParentSlotId) ==
                    SpritePartIdUtility.Canonical(slot.SlotId))
                    DrawPartsTreeNode(i, depth + 1);
            }
        }

        void DrawPartsInspector(Rect rect)
        {
            EnsurePartsSession();
            GUILayout.BeginArea(rect);
            _partsInspectorScroll = EditorGUILayout.BeginScrollView(_partsInspectorScroll);
            GUILayout.Label("PARTS", _sectionStyle);

            EditorGUILayout.BeginHorizontal();
            DrawPartsModeButton("Rig", SpritePartsStudioMode.Rig);
            DrawPartsModeButton("Animate", SpritePartsStudioMode.Animate);
            DrawPartsModeButton("Skins", SpritePartsStudioMode.Skins);
            EditorGUILayout.EndHorizontal();

            if (_partsMode == SpritePartsStudioMode.Rig)
                EditorGUILayout.HelpBox("Rig changes affect every clip.", MessageType.Warning);

            if (_profile.AnimKind != SpriteAnimKind.Parts)
            {
                EditorGUILayout.HelpBox("Create a Parts Character from the left panel.", MessageType.None);
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
                    RecordProfileUndo("Edit Parts Clip");
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
                EditorGUI.BeginChangeCheck();
                string slotName = EditorGUILayout.TextField("Name", slot.Name);
                string slotId = EditorGUILayout.TextField("Slot Id", slot.SlotId);
                string parent = DrawParentPopup(slot);
                if (_partsMode == SpritePartsStudioMode.Rig)
                {
                    Vector2 restPos = EditorGUILayout.Vector2Field("Rest Position", slot.RestPosition);
                    float restRot = EditorGUILayout.FloatField("Rest Rotation", slot.RestRotation);
                    Vector2 restScale = EditorGUILayout.Vector2Field("Rest Scale", slot.RestScale);
                    string appearance = EditorGUILayout.TextField("Default Appearance Id", slot.DefaultAppearanceId);
                    int rank = EditorGUILayout.IntField("Draw Rank", slot.DrawRank);
                    if (EditorGUI.EndChangeCheck())
                    {
                        RecordProfileUndo("Edit Parts Slot");
                        slot.Name = slotName;
                        slot.SlotId = slotId;
                        slot.ParentSlotId = parent;
                        slot.RestPosition = restPos;
                        slot.RestRotation = restRot;
                        slot.RestScale = restScale;
                        slot.DefaultAppearanceId = appearance;
                        slot.DrawRank = rank;
                        SpritePartsValidation.CanonicalizeIds(_profile);
                        SaveDirty();
                    }
                }
                else if (_partsMode == SpritePartsStudioMode.Animate)
                {
                    EditorGUI.EndChangeCheck();
                    var pose = SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
                    EditorGUI.BeginChangeCheck();
                    Vector2 pos = EditorGUILayout.Vector2Field("Position", pose.Position);
                    float rot = EditorGUILayout.FloatField("Rotation", pose.Rotation);
                    Vector2 scale = EditorGUILayout.Vector2Field("Scale", pose.Scale);
                    if (EditorGUI.EndChangeCheck())
                    {
                        BeginPartsDragUndo("Edit Parts Key Pose");
                        ApplyPartsPoseEdit(slot.SlotId, new SpritePartsAuthoringOps.PoseEdit
                        {
                            Position = pos, Rotation = rot, Scale = scale,
                        });
                        EndPartsDragUndo();
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

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawPartsModeButton(string label, SpritePartsStudioMode mode)
        {
            var style = _partsMode == mode ? _primaryStyle : _transportStyle;
            if (GUILayout.Button(label, style, GUILayout.Height(22f)))
            {
                if (_partsMode != mode)
                {
                    RecordWindowUndo("Change Parts Mode");
                    if (_partsHasTempPose)
                        ResolveOrWarnTempPose();
                    _partsMode = mode;
                    if (mode == SpritePartsStudioMode.Skins)
                        _partsSkinPreviewOverrides.Clear();
                }
            }
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
                    RecordProfileUndo("Add Parts Skin");
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
                    // Transient — does not serialize until Save Skin.
                    _partsSkinPreviewOverrides[SpritePartIdUtility.Canonical(slot.SlotId)] =
                        SpritePartIdUtility.Canonical(app);
                    _status = "Skin preview (unsaved)";
                }
            }

            if (GUILayout.Button("Save Skin"))
            {
                RecordProfileUndo("Save Parts Skin");
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

        void DrawPartsPreview(Rect rect)
        {
            EnsurePartsSession();
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, 120f, 20f), "POSE CANVAS", _sectionStyle);

            // Tool row
            float tx = rect.x + 12f;
            float ty = rect.y + 34f;
            DrawPartsToolToggle(ref tx, ty, "Move", PartsCanvasTool.Move);
            DrawPartsToolToggle(ref tx, ty, "Rotate", PartsCanvasTool.Rotate);
            DrawPartsToolToggle(ref tx, ty, "Scale", PartsCanvasTool.Scale);
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
            HandlePartsCanvasInput(canvas);
            DrawPartsCanvasContents(canvas);
        }

        void DrawPartsToolToggle(ref float x, float y, string label, PartsCanvasTool tool)
        {
            var style = _partsCanvasTool == tool ? _primaryStyle : _transportStyle;
            if (GUI.Button(new Rect(x, y, 54f, 20f), label, style))
                _partsCanvasTool = tool;
            x += 58f;
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
                            Color tint = ghost.IsPast
                                ? new Color(0.35f, 0.55f, 1f, _partsOnionOpacity)
                                : new Color(1f, 0.55f, 0.25f, _partsOnionOpacity);
                            DrawPartsPoseQuads(canvas, ref gBlob.Value, gMats, tint, pickable: false);
                            DrawOnionBadge(canvas, gMats, ghost);
                        }
                        finally
                        {
                            SpritePartsOnion.DisposeSample(gBlob, gPoses, gMats);
                        }
                    }
                }

                DrawPartsPoseQuads(canvas, ref blob.Value, matrices, Color.white, pickable: true);
                DrawPartsSelectionHandle(canvas, matrices);
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

        void DrawPartsPoseQuads(
            Rect canvas, ref SpritePartsSetBlob set, NativeArray<float4x4> matrices,
            Color tint, bool pickable)
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
                float4x4 m = matrices[i];
                Vector2 center = WorldToCanvas(canvas, m.c3.xy);
                float size = 36f * _previewZoom;
                var r = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
                var col = tint;
                if (pickable && i == _partsSelectedSlot)
                    col = new Color(0.45f, 0.9f, 0.55f, tint.a);
                else if (pickable)
                    col = new Color(0.75f, 0.78f, 0.85f, tint.a);
                EditorGUI.DrawRect(r, col);
                // Outline
                Handles.BeginGUI();
                Handles.color = new Color(0f, 0f, 0f, tint.a * 0.6f);
                Handles.DrawAAPolyLine(2f,
                    new Vector3(r.xMin, r.yMin), new Vector3(r.xMax, r.yMin),
                    new Vector3(r.xMax, r.yMax), new Vector3(r.xMin, r.yMax),
                    new Vector3(r.xMin, r.yMin));
                Handles.EndGUI();

                string name = set.Slots[i].Name.ToString();
                GUI.Label(new Rect(r.x, r.yMax + 1f, r.width + 20f, 14f), name, _mutedStyle);
            }
        }

        void DrawPartsSelectionHandle(Rect canvas, NativeArray<float4x4> matrices)
        {
            if (_partsSelectedSlot < 0 || _partsSelectedSlot >= matrices.Length) return;
            Vector2 p = WorldToCanvas(canvas, matrices[_partsSelectedSlot].c3.xy);
            Handles.BeginGUI();
            Handles.color = Color.yellow;
            Handles.DrawWireDisc(p, Vector3.forward, 8f);
            Handles.EndGUI();
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

        void HandlePartsCanvasInput(Rect canvas)
        {
            Event evt = Event.current;
            if (!canvas.Contains(evt.mousePosition) && !_partsDragActive)
                return;

            // Middle / alt pan reuses preview pan.
            if (evt.type == EventType.ScrollWheel && canvas.Contains(evt.mousePosition))
            {
                _previewZoom = Mathf.Clamp(_previewZoom * (1f - evt.delta.y * 0.08f), 0.25f, 8f);
                evt.Use();
                Repaint();
                return;
            }

            if (_partsMode == SpritePartsStudioMode.Skins)
                return; // transform tools off

            if (evt.type == EventType.MouseDown && evt.button == 0 && canvas.Contains(evt.mousePosition))
            {
                int hit = HitTestPartsSlot(canvas, evt.mousePosition);
                if (hit >= 0)
                {
                    _partsSelectedSlot = hit;
                    var slot = _profile.PartsSlots[hit];
                    _partsDragActive = true;
                    _partsDragSlotId = slot.SlotId;
                    _partsDragStartMouse = evt.mousePosition;
                    _partsDragStartPose = SampleLocalPoseForSlot(slot.SlotId, _partsPreviewTime);
                    BeginPartsDragUndo(_partsMode == SpritePartsStudioMode.Rig
                        ? "Move Parts Rest"
                        : "Move Parts Key");
                    evt.Use();
                }
            }
            else if (evt.type == EventType.MouseDrag && _partsDragActive && evt.button == 0)
            {
                var slot = SpritePartsAuthoringOps.FindSlot(_profile, _partsDragSlotId);
                if (slot != null)
                {
                    var pose = _partsDragStartPose;
                    Vector2 deltaGui = evt.mousePosition - _partsDragStartMouse;
                    if (_partsCanvasTool == PartsCanvasTool.Move)
                    {
                        float2 deltaWorld = CanvasToWorld(canvas, _partsDragStartMouse + deltaGui) -
                                            CanvasToWorld(canvas, _partsDragStartMouse);
                        pose.Position = _partsDragStartPose.Position + new Vector2(deltaWorld.x, deltaWorld.y);
                    }
                    else if (_partsCanvasTool == PartsCanvasTool.Rotate)
                    {
                        pose.Rotation = _partsDragStartPose.Rotation - deltaGui.x * 0.5f;
                    }
                    else
                    {
                        float f = 1f + deltaGui.x * 0.01f;
                        float sx = Mathf.Max(0.01f, _partsDragStartPose.Scale.x * f);
                        float sy = _partsLinkedScale
                            ? sx
                            : Mathf.Max(0.01f, _partsDragStartPose.Scale.y * (1f + deltaGui.y * 0.01f));
                        pose.Scale = new Vector2(sx, sy);
                    }
                    ApplyPartsPoseEdit(_partsDragSlotId, pose);
                    evt.Use();
                    Repaint();
                }
            }
            else if (evt.type == EventType.MouseUp && _partsDragActive && evt.button == 0)
            {
                EndPartsDragUndo();
                _partsDragActive = false;
                evt.Use();
            }
            else if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && _partsDragActive)
            {
                // Cancel: restore start pose
                ApplyPartsPoseEdit(_partsDragSlotId, _partsDragStartPose);
                EndPartsDragUndo();
                Undo.PerformUndo(); // drop the group
                _partsDragActive = false;
                evt.Use();
            }
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
                    Vector2 center = WorldToCanvas(canvas, matrices[i].c3.xy);
                    float size = 36f * _previewZoom;
                    var r = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
                    if (r.Contains(mouse))
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
                int idx = SpritePartsAuthoringOps.FindSlotIndex(_profile, slotId);
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
                _status = "Temporary pose (Auto Key OFF) — Key Pose to commit";
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
            SaveDirty();
            if (result.WroteRest)
                _status = "Rig: updated rest pose (affects every clip)";
            else if (result.WroteKey)
                _status = result.InsertedRestAnchorAtZero
                    ? "Animate: keyed pose (+ rest anchor at 0)"
                    : "Animate: keyed pose";
        }

        void BeginPartsDragUndo(string operation)
        {
            Undo.IncrementCurrentGroup();
            _partsDragUndoGroup = Undo.GetCurrentGroup();
            RecordProfileUndo(operation);
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
        }

        void ResolveOrWarnTempPose()
        {
            if (!_partsHasTempPose) return;
            // Scrub/clip change: require resolve — Key Pose commits, otherwise discard with status.
            _status = "Discarded temporary unkeyed pose";
            _partsHasTempPose = false;
        }

        void DrawPartsTimeline(Rect rect, int controlId)
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
                    RecordProfileUndo("Set Parts Duration");
                    clip.Duration = Mathf.Max(1e-3f, dur);
                    SaveDirty();
                }
            }
            GUI.Label(new Rect(rect.x + 130f, y, 70f, 18f), "Display FPS", _mutedStyle);
            _partsDisplayFps = Mathf.Clamp(
                EditorGUI.FloatField(new Rect(rect.x + 205f, y, 40f, 18f), _partsDisplayFps), 1f, 120f);

            _partsAutoKey = GUI.Toggle(new Rect(rect.x + 260f, y, 90f, 18f), _partsAutoKey,
                new GUIContent("Auto Key", "Animate drags write keys (default ON)."));
            if (GUI.Button(new Rect(rect.x + 355f, y, 70f, 18f),
                new GUIContent("Key Pose", "Insert/update key at playhead for selection."), EditorStyles.miniButton))
            {
                CommitKeyPose();
            }

            float timeLabelX = rect.xMax - 120f;
            GUI.Label(new Rect(timeLabelX, y, 110f, 18f),
                $"t={_partsPreviewTime:F3}s", _mutedStyle);

            float tracksTop = rect.y + 28f;
            float tracksHeight = rect.height - 34f;
            var tracksRect = new Rect(rect.x + 8f, tracksTop, rect.width - 16f, tracksHeight);
            DrawPartsTracks(tracksRect, clip, controlId);
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
            RecordProfileUndo("Key Parts Pose");
            var result = SpritePartsAuthoringOps.WriteKeyPose(
                _profile, _partsSelectedClip, slot.SlotId, _partsPreviewTime, pose, _partsDisplayFps);
            _partsHasTempPose = false;
            SaveDirty();
            _status = result.WroteKey ? "Keyed pose" : (result.Reason ?? "Key Pose failed");
        }

        void DrawPartsTracks(Rect rect, SpritePartsClipDef clip, int controlId)
        {
            if (_profile?.PartsSlots == null || clip == null) return;
            float duration = Mathf.Max(1e-3f, clip.Duration);
            float labelW = 90f;
            float rowH = 18f;
            EditorGUI.DrawRect(rect, new Color(0.06f, 0.07f, 0.09f));

            // Scrub on background
            Event evt = Event.current;
            var scrubRect = new Rect(rect.x + labelW, rect.y, rect.width - labelW, rect.height);
            if (evt.type == EventType.MouseDown && scrubRect.Contains(evt.mousePosition) && evt.clickCount == 1)
            {
                if (_partsHasTempPose) ResolveOrWarnTempPose();
                float u = Mathf.InverseLerp(scrubRect.x, scrubRect.xMax, evt.mousePosition.x);
                _partsPreviewTime = SpritePartsAuthoringOps.SnapTime(u * duration, _partsDisplayFps, duration);
                GUIUtility.hotControl = controlId;
                evt.Use();
                Repaint();
            }
            if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == controlId)
            {
                float u = Mathf.InverseLerp(scrubRect.x, scrubRect.xMax, evt.mousePosition.x);
                _partsPreviewTime = SpritePartsAuthoringOps.SnapTime(
                    Mathf.Clamp01(u) * duration, _partsDisplayFps, duration);
                evt.Use();
                Repaint();
            }
            if (evt.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
            {
                GUIUtility.hotControl = 0;
                evt.Use();
            }

            for (int i = 0; i < _profile.PartsSlots.Count; i++)
            {
                var slot = _profile.PartsSlots[i];
                if (slot == null) continue;
                float rowY = rect.y + i * rowH;
                if (rowY > rect.yMax - rowH) break;
                bool selected = i == _partsSelectedSlot;
                if (selected)
                    EditorGUI.DrawRect(new Rect(rect.x, rowY, rect.width, rowH), new Color(0.15f, 0.22f, 0.18f));
                GUI.Label(new Rect(rect.x, rowY, labelW - 4f, rowH), slot.Name, _mutedStyle);

                var track = SpritePartsAuthoringOps.FindTrack(clip, slot.SlotId);
                var trackRect = new Rect(rect.x + labelW, rowY + 2f, rect.width - labelW, rowH - 4f);
                EditorGUI.DrawRect(trackRect, new Color(0.12f, 0.13f, 0.16f));
                if (track?.Keys != null)
                {
                    for (int k = 0; k < track.Keys.Count; k++)
                    {
                        float u = track.Keys[k].Time / duration;
                        float kx = Mathf.Lerp(trackRect.x, trackRect.xMax, u);
                        DrawPartsKeyDiamond(kx, rowY + rowH * 0.5f, selected);
                    }
                }
                else
                {
                    GUI.Label(trackRect, "  (inherits; no keys)", _mutedStyle);
                }
            }

            // Playhead
            float pu = Mathf.Clamp01(_partsPreviewTime / duration);
            float px = Mathf.Lerp(rect.x + labelW, rect.xMax, pu);
            EditorGUI.DrawRect(new Rect(px - 1f, rect.y, 2f, rect.height), new Color(1f, 0.85f, 0.2f, 0.9f));
        }

        void DrawPartsKeyDiamond(float x, float y, bool selected)
        {
            float s = selected ? 5f : 4f;
            Handles.BeginGUI();
            Handles.color = selected ? new Color(0.4f, 1f, 0.55f) : new Color(0.7f, 0.85f, 1f);
            Handles.DrawAAConvexPolygon(
                new Vector3(x, y - s), new Vector3(x + s, y),
                new Vector3(x, y + s), new Vector3(x - s, y));
            Handles.EndGUI();
        }

        void AddPartsClip()
        {
            RecordProfileUndo("Add Parts Clip");
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
            SaveDirty();
        }

        void AddPartsSlot()
        {
            RecordProfileUndo("Add Parts Slot");
            _profile.EnsurePartsRig();
            int n = _profile.PartsSlots.Count + 1;
            string parent = CurrentPartsSlot?.SlotId ?? string.Empty;
            _profile.PartsSlots.Add(new SpritePartSlotDef
            {
                Name = "Part " + n,
                SlotId = "part." + n,
                ParentSlotId = parent,
                RestScale = Vector2.one,
                DrawRank = n - 1,
            });
            SpritePartsValidation.CanonicalizeIds(_profile);
            _partsSelectedSlot = _profile.PartsSlots.Count - 1;
            SaveDirty();
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

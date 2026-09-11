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

        void DrawColliderList(SpriteClipDef clip)
        {
            PruneColliderSelection(clip, _selectedFrame);
            SyncColliderRowDetails(PrimarySelectedCollider());
            var onThisFrame = new List<FrameBoxDef>();
            var otherFrames = new List<FrameBoxDef>();
            var clipColliders = new List<FrameBoxDef>();
            var characterColliders = new List<FrameBoxDef>();
            var otherClips = new List<FrameBoxDef>();
            CollectColliderScopes(clip, _selectedFrame,
                onThisFrame, otherFrames, clipColliders, characterColliders, otherClips);
            int listed = onThisFrame.Count + otherFrames.Count + clipColliders.Count +
                characterColliders.Count + otherClips.Count;
            SectionLabel("COLLIDERS");
            GUILayout.Label(
                $"Frame {_selectedFrame + 1} • {onThisFrame.Count} on this frame • {otherFrames.Count} other frames • {_selectedColliders.Count} selected",
                _mutedStyle);

            if (listed == 0)
            {
                EditorGUILayout.HelpBox("This profile has no colliders. Arm a shape above, then click the preview.",
                    MessageType.None);
                return;
            }

            _colliderFrameExpanded = EditorGUILayout.Foldout(_colliderFrameExpanded,
                new GUIContent(
                    $"CURRENT FRAME  {_selectedFrame + 1}  ({onThisFrame.Count} here • {otherFrames.Count} other)",
                    "Frame-lifetime slash windows. On this frame vs other frames of this clip."),
                true);
            if (_colliderFrameExpanded)
            {
                EditorGUI.indentLevel++;
                if (DrawColliderScopeGroup(clip, onThisFrame, ref _colliderOnThisFrameExpanded,
                        "ON THIS FRAME",
                        "Only active on this animation frame. Best for attacks, parries, and one-frame contacts."))
                    return;
                if (DrawColliderScopeGroup(clip, otherFrames, ref _colliderOtherFramesExpanded,
                        "OTHER FRAMES IN THIS CLIP",
                        "Frame colliders that live on another frame of this clip. Click the search icon next to lock to jump there."))
                    return;
                EditorGUI.indentLevel--;
            }
            if (DrawColliderScopeGroup(clip, clipColliders, ref _colliderClipExpanded,
                    "THIS CLIP",
                    "Active throughout this clip. Best for stance-specific hurtboxes and interaction zones."))
                return;
            if (DrawColliderScopeGroup(clip, characterColliders, ref _colliderCharacterExpanded,
                    "CHARACTER — ALL CLIPS",
                    "Shared by every clip. Best for the main body, pickup radius, and persistent sensors."))
                return;
            if (otherClips.Count > 0 &&
                DrawColliderScopeGroup(clip, otherClips, ref _colliderOtherClipsExpanded,
                    "OTHER CLIPS",
                    "Frame or clip colliders that belong to another clip. Click the search icon next to lock to jump there."))
                return;

            if (_selectedColliders.Count > 1)
                EditorGUILayout.HelpBox(
                    $"{_selectedColliders.Count} colliders selected. Click one row to open its details, or right-click for bulk Lives On, Physics, visibility, and lock.",
                    MessageType.None);

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
                        "Clone this-frame slash colliders onto the next frame. This Clip and Character boxes stay as they are.")))
                        CopyCollidersToNextFrame(clip, _selectedFrame);
                }
                if (GUILayout.Button(new GUIContent("Copy to All Frames",
                    "Clone this-frame slash colliders onto every other frame. Prefer This Clip for crouch vs stand hurt.")))
                    CopyCollidersToAllFrames(clip, _selectedFrame);
            }
            if (GUILayout.Button(new GUIContent("Delete All on This Frame",
                "Delete this-frame slash colliders only. This Clip and Character boxes stay.")))
                DeleteAllFrameColliders(clip, _selectedFrame);
        }

        void SyncColliderRowDetails(FrameBoxDef primary)
        {
            if (primary == null || _selectedColliders.Count != 1)
                return;
            if (_colliderDetailsBox != primary)
                OpenColliderRowDetails(primary);
        }

        void OpenColliderRowDetails(FrameBoxDef box)
        {
            if (box == null || box.Locked)
                return;
            _colliderDetailsBox = box;
            _colliderRowDetailsExpanded = true;
            if (box.IsCharacter)
                _colliderCharacterExpanded = true;
            else if (box.IsClip)
            {
                if (CurrentClip != null && string.Equals(box.ClipName, CurrentClip.Name))
                    _colliderClipExpanded = true;
                else
                    _colliderOtherClipsExpanded = true;
            }
            else
            {
                _colliderFrameExpanded = true;
                if (CurrentClip != null && string.Equals(box.ClipName, CurrentClip.Name) &&
                    box.FrameIndex == _selectedFrame)
                    _colliderOnThisFrameExpanded = true;
                else if (CurrentClip != null && string.Equals(box.ClipName, CurrentClip.Name))
                    _colliderOtherFramesExpanded = true;
                else
                    _colliderOtherClipsExpanded = true;
            }
        }

        void DrawColliderBasicDetails(SpriteClipDef clip, FrameBoxDef box)
        {
            int nextId = Mathf.Clamp(EditorGUILayout.IntField(
                new GUIContent("Collider ID", "Gameplay collider identifier, from 1 to 255."),
                box.Id), 1, byte.MaxValue);
            if (nextId != box.Id)
            {
                RecordProfileUndo("Set Sprite Collider ID");
                box.Id = (byte)nextId;
                SaveDirty();
            }

            var nextShape = (SpriteColliderShape)EditorGUILayout.EnumPopup(
                new GUIContent("Shape", "Change the collider geometry type while keeping its bounds."),
                box.Shape);
            if (nextShape != box.Shape)
            {
                RecordProfileUndo("Change Sprite Collider Shape");
                box.Shape = nextShape;
                if (box.Shape == SpriteColliderShape.Polygon)
                    box.EnsurePolygon();
                SaveDirty();
            }

            var sheet = _profile.SheetForClip(clip);
            if (SpriteSheetProfile.TryGetCellPixels(sheet, out float cellW, out float cellH))
            {
                Vector2 centerPixels = new(
                    box.RectUV.center.x * cellW, box.RectUV.center.y * cellH);
                Vector2 sizePixels = new(
                    box.RectUV.width * cellW, box.RectUV.height * cellH);
                EditorGUI.BeginChangeCheck();
                Vector2 nextCenter = EditorGUILayout.Vector2Field(
                    new GUIContent("Center (px)", "Collider center measured from the cell's top-left corner."),
                    centerPixels);
                Vector2 nextSize = EditorGUILayout.Vector2Field(
                    new GUIContent("Size (px)", "Collider width and height in source pixels."),
                    sizePixels);
                if (EditorGUI.EndChangeCheck())
                {
                    nextSize.x = Mathf.Max(1f, nextSize.x);
                    nextSize.y = Mathf.Max(1f, nextSize.y);
                    RecordProfileUndo("Edit Sprite Collider Bounds");
                    box.RectUV = new Rect(
                        (nextCenter.x - nextSize.x * 0.5f) / cellW,
                        (nextCenter.y - nextSize.y * 0.5f) / cellH,
                        nextSize.x / cellW,
                        nextSize.y / cellH);
                    SaveDirty();
                }
                EditorGUILayout.LabelField("Bounds (UV)",
                    $"{box.RectUV.x:0.###}, {box.RectUV.y:0.###}, {box.RectUV.width:0.###}, {box.RectUV.height:0.###}");
            }
            else
            {
                Rect nextRect = EditorGUILayout.RectField("Bounds (UV)", box.RectUV);
                if (nextRect != box.RectUV)
                {
                    RecordProfileUndo("Edit Sprite Collider Bounds");
                    box.RectUV = nextRect;
                    SaveDirty();
                }
            }

            int nextLife = EditorGUILayout.Popup(
                new GUIContent("Lives On",
                    "Editing a This Clip or Character collider updates that same collider everywhere its scope is active."),
                ColliderLifetimeToPopup(box.Lifetime), ColliderLifetimeLabels);
            byte life = ColliderLifetimeFromPopup(nextLife);
            if (life != box.Lifetime)
                SetSelectedColliderLifetime(life, clip, _selectedFrame);

            int nextPhysics = EditorGUILayout.Popup(
                new GUIContent("Physics"), Mathf.Clamp(box.Physics, 0, 2),
                ColliderPhysicsLabels);
            if (nextPhysics != box.Physics)
                SetSelectedColliderPhysics((byte)nextPhysics);

            if (box.UsesUnity2D)
            {
                bool nextTrigger = EditorGUILayout.Toggle("Unity Trigger", box.IsTrigger);
                if (nextTrigger != box.IsTrigger)
                {
                    RecordProfileUndo("Set Collider Trigger");
                    box.IsTrigger = nextTrigger;
                    SaveDirty();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                float nextAngle = EditorGUILayout.FloatField(
                    new GUIContent("Angle (deg)", "Rotation around the collider center."),
                    box.Angle);
                if (!Mathf.Approximately(nextAngle, box.Angle))
                {
                    RecordProfileUndo("Rotate Sprite Collider");
                    box.Angle = nextAngle;
                    SaveDirty();
                }
                using (new EditorGUI.DisabledScope(Mathf.Approximately(box.Angle, 0f)))
                {
                    if (ResetValueButton("Reset this collider's rotation to 0°."))
                    {
                        RecordProfileUndo("Reset Sprite Collider Angle");
                        box.Angle = 0f;
                        SaveDirty();
                    }
                }
            }

            DrawColliderBinding(clip, box);
            if (box.IsCharacter)
                DrawCharacterColliderClipFilters(box);
        }

        void DrawCharacterColliderClipFilters(FrameBoxDef box)
        {
            box.EnsureCharacterClipFilters();
            if (_characterFilterDetailsBox != box)
            {
                _characterFilterDetailsBox = box;
                _characterIncludeSelected.Clear();
                _characterExcludeSelected.Clear();
            }

            GUILayout.Space(4f);
            EditorGUILayout.LabelField(new GUIContent("Character Clip Filter",
                "Empty Include = every clip. Exclude always wins. Use this when one sheet mixes body clips with Attack or projectile clips."),
                EditorStyles.boldLabel);
            GUILayout.Label(
                "Include empty = all clips. Exclude removes Attack / projectile clips from this body collider.",
                _mutedWrapStyle);

            DrawCharacterClipFilterList(
                "Include Clips",
                "Clips that keep this Character collider. Empty means all clips.",
                box.CharacterIncludeClips,
                _characterIncludeSelected,
                box.CharacterExcludeClips,
                "Add Character Include Clip",
                "Remove Character Include Clip");
            GUILayout.Space(3f);
            DrawCharacterClipFilterList(
                "Exclude Clips",
                "Clips that never get this Character collider, even if listed in Include.",
                box.CharacterExcludeClips,
                _characterExcludeSelected,
                box.CharacterIncludeClips,
                "Add Character Exclude Clip",
                "Remove Character Exclude Clip");
        }

        void DrawCharacterClipFilterList(
            string title,
            string tooltip,
            List<string> list,
            HashSet<string> selected,
            List<string> otherList,
            string addLabel,
            string removeUndo)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent($"{title}  ({list.Count})", tooltip),
                    EditorStyles.miniBoldLabel);
                using (new EditorGUI.DisabledScope(selected.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("Delete",
                            "Remove selected clip names from this list."),
                        EditorStyles.miniButton, GUILayout.Width(54f)))
                    {
                        RecordProfileUndo(removeUndo);
                        list.RemoveAll(selected.Contains);
                        selected.Clear();
                        SaveDirty();
                        Repaint();
                    }
                }
            }

            if (list.Count == 0)
                GUILayout.Label("Empty", _mutedStyle);
            else
            {
                for (int i = 0; i < list.Count; i++)
                {
                    string name = list[i];
                    bool isSelected = selected.Contains(name);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        Color previous = GUI.backgroundColor;
                        if (isSelected)
                            GUI.backgroundColor = AccentColor;
                        if (GUILayout.Button(new GUIContent(name,
                                "Click to select. Shift/Ctrl/Cmd for multi-select."),
                            EditorStyles.miniButton, GUILayout.Height(20f)))
                        {
                            SelectionOp op = ReadSelectionOp(Event.current, orderedList: true);
                            ApplyCharacterClipFilterSelection(list, selected, name, op);
                            Repaint();
                        }
                        GUI.backgroundColor = previous;
                        if (GUILayout.Button(new GUIContent("×", "Remove this clip from the list."),
                            EditorStyles.miniButton, GUILayout.Width(22f), GUILayout.Height(20f)))
                        {
                            RecordProfileUndo(removeUndo);
                            list.RemoveAt(i);
                            selected.Remove(name);
                            SaveDirty();
                            Repaint();
                            return;
                        }
                    }
                }
            }

            var profileClips = _profile?.Clips;
            if (profileClips == null || profileClips.Count == 0)
                return;

            var menu = new GenericMenu();
            int added = 0;
            for (int i = 0; i < profileClips.Count; i++)
            {
                string clipName = profileClips[i]?.Name;
                if (string.IsNullOrEmpty(clipName))
                    continue;
                if (list.Contains(clipName) || (otherList != null && otherList.Contains(clipName)))
                    continue;
                string captured = clipName;
                menu.AddItem(new GUIContent(captured), false, () =>
                {
                    RecordProfileUndo(addLabel);
                    list.Add(captured);
                    SaveDirty();
                    Repaint();
                });
                added++;
            }
            using (new EditorGUI.DisabledScope(added == 0))
            {
                Rect addRect = GUILayoutUtility.GetRect(new GUIContent(addLabel), EditorStyles.miniButton);
                if (GUI.Button(addRect, new GUIContent(addLabel,
                        added == 0
                            ? "Every clip is already listed, or the other list already owns them."
                            : "Add a profile clip to this list."),
                    EditorStyles.miniButton) && added > 0)
                    menu.DropDown(addRect);
            }
        }

        void ApplyCharacterClipFilterSelection(
            List<string> list, HashSet<string> selected, string name, SelectionOp op)
        {
            switch (op)
            {
                case SelectionOp.Add:
                    selected.Add(name);
                    break;
                case SelectionOp.Toggle:
                    if (!selected.Add(name))
                        selected.Remove(name);
                    break;
                case SelectionOp.Subtract:
                    selected.Remove(name);
                    break;
                case SelectionOp.Intersect:
                    bool keep = selected.Contains(name);
                    selected.Clear();
                    if (keep)
                        selected.Add(name);
                    break;
                default:
                    selected.Clear();
                    selected.Add(name);
                    break;
            }
        }

        void CollectColliderScopes(SpriteClipDef clip, int frame,
            List<FrameBoxDef> onThisFrame, List<FrameBoxDef> otherFrames,
            List<FrameBoxDef> thisClip, List<FrameBoxDef> character, List<FrameBoxDef> otherClips)
        {
            if (_profile?.Hitboxes == null || clip == null)
                return;
            for (int i = 0; i < _profile.Hitboxes.Count; i++)
            {
                var box = _profile.Hitboxes[i];
                if (box == null)
                    continue;
                if (box.IsCharacter)
                    character.Add(box);
                else if (string.Equals(box.ClipName, clip.Name))
                {
                    if (box.IsClip)
                        thisClip.Add(box);
                    else if (box.FrameIndex == frame)
                        onThisFrame.Add(box);
                    else
                        otherFrames.Add(box);
                }
                else
                    otherClips.Add(box);
            }
            if (otherFrames.Count > 1)
                otherFrames.Sort((a, b) => a.FrameIndex.CompareTo(b.FrameIndex));
        }

        static bool ColliderLivesHere(FrameBoxDef box, SpriteClipDef clip, int frame)
        {
            if (box == null || clip == null)
                return false;
            if (box.IsCharacter)
                return box.AppliesToClip(clip.Name);
            if (!string.Equals(box.ClipName, clip.Name))
                return false;
            return box.IsClip || box.FrameIndex == frame;
        }

        static string ColliderHomeLabel(FrameBoxDef box)
        {
            if (box == null)
                return "Collider";
            if (box.IsCharacter)
            {
                box.EnsureCharacterClipFilters();
                int include = box.CharacterIncludeClips.Count;
                int exclude = box.CharacterExcludeClips.Count;
                if (include == 0 && exclude == 0)
                    return "All clips";
                if (include > 0 && exclude == 0)
                    return include == 1
                        ? box.CharacterIncludeClips[0]
                        : $"Include {include} clips";
                if (include == 0)
                    return exclude == 1
                        ? $"All except {box.CharacterExcludeClips[0]}"
                        : $"All except {exclude}";
                return $"Include {include} · Exclude {exclude}";
            }
            string clipName = string.IsNullOrEmpty(box.ClipName) ? "Clip" : box.ClipName;
            if (box.IsClip)
                return clipName;
            return $"{clipName}  •  Frame {box.FrameIndex + 1}";
        }

        void DrawColliderBinding(SpriteClipDef clip, FrameBoxDef box)
        {
            bool here = ColliderLivesHere(box, clip, _selectedFrame);
            string home = ColliderHomeLabel(box);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("Binding",
                    "Jump to the clip and frame this collider lives on."));
                using (new EditorGUI.DisabledScope(here && !box.IsCharacter))
                {
                    if (GUILayout.Button(new GUIContent(here ? $"{home}  •  here" : home,
                            box.IsCharacter
                                ? "Character colliders use Include / Exclude clip lists. Empty Include = all clips."
                                : here
                                    ? "Already viewing the clip and frame this collider lives on."
                                    : "Jump to the clip and frame this collider lives on.")))
                        JumpToColliderHome(box);
                }
            }
        }

        int FindClipIndexByName(string clipName)
        {
            if (_profile?.Clips == null || string.IsNullOrEmpty(clipName))
                return -1;
            for (int i = 0; i < _profile.Clips.Count; i++)
            {
                if (_profile.Clips[i] != null && string.Equals(_profile.Clips[i].Name, clipName))
                    return i;
            }
            return -1;
        }

        void JumpToColliderHome(FrameBoxDef box)
        {
            if (box == null || _profile?.Clips == null || _profile.Clips.Count == 0)
                return;

            int clipIndex = _selectedClip;
            if (box.IsCharacter)
            {
                box.EnsureCharacterClipFilters();
                if (!box.AppliesToClip(CurrentClip?.Name))
                {
                    int found = -1;
                    if (box.CharacterIncludeClips.Count > 0)
                        found = FindClipIndexByName(box.CharacterIncludeClips[0]);
                    if (found < 0)
                    {
                        for (int i = 0; i < _profile.Clips.Count; i++)
                        {
                            string name = _profile.Clips[i]?.Name;
                            if (!string.IsNullOrEmpty(name) && box.AppliesToClip(name))
                            {
                                found = i;
                                break;
                            }
                        }
                    }
                    if (found >= 0)
                        clipIndex = found;
                }
            }
            else
            {
                int found = FindClipIndexByName(box.ClipName);
                if (found >= 0)
                    clipIndex = found;
            }
            clipIndex = Mathf.Clamp(clipIndex, 0, _profile.Clips.Count - 1);

            if (_renamingClip >= 0 && _renamingClip != clipIndex)
                CancelClipRename();
            if (_renamingSheet >= 0)
                CommitSheetRename();

            bool clipChanged = _selectedClip != clipIndex;
            if (clipChanged)
            {
                _selectedOnionFrame = -1;
                _selectedEventFrame = -1;
            _selectedEventIndex = -1;
                ClearSocketSelection();
                _selectedClip = clipIndex;
                var destClip = _profile.Clips[clipIndex];
                if (destClip != null && _profile.Sheets != null && _profile.Sheets.Count > 0)
                {
                    _selectedSheet = Mathf.Clamp(destClip.SheetIndex, 0, _profile.Sheets.Count - 1);
                    _collapsedSheets.Remove(_selectedSheet);
                    _profile.SyncLegacyFromSheet(_selectedSheet);
                    InvalidateSheetPixelCache();
                }
            }

            var clip = _profile.Clips[_selectedClip];
            int frame = _selectedFrame;
            if (box.IsFrame && box.FrameIndex >= 0)
                frame = box.FrameIndex;
            else if (clipChanged)
                frame = 0;
            if (clip?.Frames != null && clip.Frames.Length > 0)
                frame = Mathf.Clamp(frame, 0, clip.Frames.Length - 1);
            else
                frame = Mathf.Max(0, frame);

            SelectOnlyFrame(frame);
            _previewTime = clip != null ? PreviewTimeAtFrame(clip, frame) : 0f;
            _playing = false;
            _selectedColliders.Clear();
            if (!box.Locked)
                _selectedColliders.Add(box);
            OpenColliderRowDetails(box);
            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
            _status = box.IsCharacter
                ? $"Character collider · {ColliderHomeLabel(box)}"
                : $"Jumped to {ColliderHomeLabel(box)}";
            ReleaseShortcutKeyboardFocus();
            Repaint();
        }

        bool DrawColliderScopeGroup(SpriteClipDef clip, List<FrameBoxDef> group,
            ref bool expanded, string title, string description)
        {
            int selectedCount = 0;
            int lockedCount = 0;
            for (int i = 0; i < group.Count; i++)
            {
                selectedCount += _selectedColliders.Contains(group[i]) ? 1 : 0;
                lockedCount += group[i].Locked ? 1 : 0;
            }

            string summary = $"{title}  ({group.Count})";
            if (selectedCount > 0)
                summary += $"  •  {selectedCount} selected";
            if (lockedCount > 0)
                summary += $"  •  {lockedCount} locked";
            expanded = EditorGUILayout.Foldout(expanded, new GUIContent(summary, description), true);
            if (!expanded)
                return false;

            GUILayout.Label(description, _mutedWrapStyle);
            if (group.Count == 0)
            {
                GUILayout.Label("No colliders in this scope.", _mutedStyle);
                GUILayout.Space(3f);
                return false;
            }

            for (int i = 0; i < group.Count; i++)
            {
                FrameBoxDef box = group[i];
                bool selected = _selectedColliders.Contains(box);
                bool foldOpen = !box.Locked &&
                    _colliderDetailsBox == box &&
                    _colliderRowDetailsExpanded &&
                    _selectedColliders.Count == 1 &&
                    selected;
                using (new EditorGUILayout.HorizontalScope())
                {
                    Color previous = GUI.backgroundColor;
                    if (GUILayout.Button(ColliderVisibilityContent(box.Hidden),
                        EditorStyles.miniButton, GUILayout.Width(27f), GUILayout.Height(22f)))
                    {
                        RecordProfileUndo(box.Hidden ? "Show Sprite Collider" : "Hide Sprite Collider");
                        box.Hidden = !box.Hidden;
                        _status = box.Hidden
                            ? $"Hid {box.Shape} collider #{box.Id}"
                            : $"Showed {box.Shape} collider #{box.Id}";
                        SaveDirty();
                        Repaint();
                    }
                    if (GUILayout.Button(ColliderLockContent(box.Locked),
                        EditorStyles.miniButton, GUILayout.Width(27f), GUILayout.Height(22f)))
                    {
                        RecordProfileUndo(box.Locked ? "Unlock Sprite Collider" : "Lock Sprite Collider");
                        box.Locked = !box.Locked;
                        if (box.Locked)
                        {
                            _selectedColliders.Remove(box);
                            if (_colliderTransformBox == box)
                                ClearColliderTransform();
                            if (_colliderDetailsBox == box)
                                _colliderRowDetailsExpanded = false;
                        }
                        _status = box.Locked
                            ? $"Locked {box.Shape} collider #{box.Id}"
                            : $"Unlocked {box.Shape} collider #{box.Id}";
                        SaveDirty();
                        Repaint();
                    }
                    {
                        bool here = ColliderLivesHere(box, clip, _selectedFrame);
                        using (new EditorGUI.DisabledScope(here && !box.IsCharacter))
                        {
                            if (GUILayout.Button(ColliderGoToContent(box),
                                EditorStyles.miniButton, GUILayout.Width(27f), GUILayout.Height(22f)))
                            {
                                JumpToColliderHome(box);
                                Repaint();
                            }
                        }
                    }

                    using (new EditorGUI.DisabledScope(box.Locked))
                    {
                        Rect foldRect = GUILayoutUtility.GetRect(16f, 22f, GUILayout.Width(16f));
                        bool nextOpen = EditorGUI.Foldout(foldRect, foldOpen, GUIContent.none, true);
                        if (nextOpen != foldOpen && !box.Locked)
                        {
                            if (nextOpen)
                            {
                                if (!selected || _selectedColliders.Count != 1)
                                    SelectColliderFromList(group, i, SelectionOp.Replace);
                                _colliderDetailsBox = box;
                                _colliderRowDetailsExpanded = true;
                            }
                            else
                            {
                                _colliderDetailsBox = box;
                                _colliderRowDetailsExpanded = false;
                            }
                            _playing = false;
                            _previewTime = PreviewTimeAtFrame(clip, _selectedFrame);
                            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
                            _selectedOnionFrame = -1;
                            Repaint();
                        }
                    }

                    if (selected)
                        GUI.backgroundColor = AccentColor;
                    using (new EditorGUI.DisabledScope(box.Locked))
                    {
                        string state = box.Hidden ? "  (hidden)" : string.Empty;
                        bool away = !ColliderLivesHere(box, clip, _selectedFrame);
                        bool rowClicked = GUILayout.Button(new GUIContent(
                                $"{i + 1}. {box.Shape}  •  ID {box.Id}{(away ? "  •  " + ColliderHomeLabel(box) : string.Empty)}{state}",
                                box.Locked
                                    ? "Unlock this collider before selecting or editing it."
                                    : "Click to select and open details. Click again to collapse. Use the search icon to jump to this collider's clip and frame."),
                            EditorStyles.miniButton, GUILayout.Height(22f));
                        Rect selectionRect = GUILayoutUtility.GetLastRect();
                        if (rowClicked)
                        {
                            bool wasSolePrimary = selected && _selectedColliders.Count == 1;
                            SelectionOp op = ReadSelectionOp(Event.current, orderedList: true);
                            _playing = false;
                            _previewTime = PreviewTimeAtFrame(clip, _selectedFrame);
                            SelectColliderFromList(group, i, op);
                            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
                            _selectedOnionFrame = -1;
                            if (_selectedColliders.Count == 1 && _selectedColliders.Contains(box))
                            {
                                _colliderDetailsBox = box;
                                bool collapse = wasSolePrimary &&
                                    op == SelectionOp.Replace && _colliderRowDetailsExpanded;
                                _colliderRowDetailsExpanded = !collapse;
                                if (_colliderRowDetailsExpanded)
                                    _pendingInspectorScrollToColliders = true;
                            }
                            Repaint();
                        }
                        HandleColliderListRowContext(selectionRect, clip, box);
                    }
                    GUI.backgroundColor = previous;

                    using (new EditorGUI.DisabledScope(box.Locked))
                    {
                        if (GUILayout.Button(new GUIContent("×",
                                box.Locked ? "Unlock this collider before deleting it." : "Delete this collider."),
                            EditorStyles.miniButton, GUILayout.Width(27f), GUILayout.Height(22f)))
                        {
                            DeleteCollider(box);
                            return true;
                        }
                    }
                }

                if (box.Locked ||
                    _colliderDetailsBox != box ||
                    !_colliderRowDetailsExpanded ||
                    _selectedColliders.Count != 1 ||
                    !_selectedColliders.Contains(box))
                    continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(16f);
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        DrawColliderBasicDetails(clip, box);
                }
            }
            GUILayout.Space(4f);
            return false;
        }

        void HandleColliderListRowContext(Rect rowRect, SpriteClipDef clip, FrameBoxDef box)
        {
            var evt = Event.current;
            if (box == null || box.Locked ||
                (evt.type != EventType.ContextClick &&
                 !(evt.type == EventType.MouseDown && evt.button == 1)) ||
                !rowRect.Contains(evt.mousePosition))
                return;
            if (!_selectedColliders.Contains(box))
            {
                ClearColliderSelection();
                _selectedColliders.Add(box);
                ClearSocketSelection();
            }
            FocusColliderInInspector(box);
            ShowColliderContextMenu(clip, _selectedFrame, box);
            evt.Use();
            Repaint();
        }

        void SelectAllFrameColliders(SpriteClipDef clip, int frame)
        {
            _playing = false;
            _selectedFrame = frame;
            _previewTime = PreviewTimeAtFrame(clip, frame);
            _selectedColliders.Clear();
            foreach (var box in BoxesFor(clip, frame))
            {
                if (!box.Locked)
                    _selectedColliders.Add(box);
            }
            ClearSocketSelection();
            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
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
                {
                    if (!box.Locked)
                        _selectedColliders.Add(box);
                }
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
            _selectedEventIndex = -1;
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
            _selectedEventIndex = -1;
            _selectedSocketDrawFrame = -1;
            _selectedSocketDrawName = null;
            _selectedOnionFrame = -1;
            _status = PreviewSelectionStatus("Selected all");
            Repaint();
        }

        void DeleteCollider(FrameBoxDef box)
        {
            if (box == null || box.Locked || !_profile.Hitboxes.Contains(box))
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
            var socketNames = new List<string>();
            if (includeSockets)
            {
                foreach (string selected in _selectedSockets)
                {
                    if (!IsSocketLocked(selected))
                        socketNames.Add(SpriteSocketKeys.CanonicalName(selected));
                }
            }
            int socketCount = socketNames.Count;
            if (colliderCount == 0 && socketCount == 0)
            {
                if (includeSockets && _selectedSockets.Count > 0)
                {
                    _status = "Selected sockets are locked — unlock to delete";
                    Repaint();
                }
                return;
            }

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
                var names = socketNames;
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
                    _profile.RemoveSocketFromInventories(names[i]);
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
            int count = 0;
            var visible = CurrentFrameColliders(clip, frame);
            for (int i = 0; i < visible.Count; i++)
            {
                if (visible[i] != null && visible[i].IsFrame && !visible[i].Locked)
                    count++;
            }
            if (count == 0)
                return;
            RecordProfileUndo("Delete All Sprite Colliders on Frame");
            _profile.Hitboxes.RemoveAll(box =>
                box != null && box.IsFrame &&
                !box.Locked && box.ClipName == clip.Name && box.FrameIndex == frame);
            PruneColliderSelection(clip, frame);
            _status = $"Deleted {count} unlocked collider{Plural(count)} on frame {frame + 1}";
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
            _profile.Hitboxes.RemoveAll(box =>
                box != null && box.IsFrame &&
                box.ClipName == clip.Name && box.FrameIndex == destinationFrame);
            var source = CurrentFrameColliders(clip, sourceFrame);
            int copied = 0;
            for (int i = 0; i < source.Count; i++)
            {
                var box = source[i];
                if (!box.IsFrame)
                    continue;
                _profile.Hitboxes.Add(box.Clone(clip.Name, destinationFrame));
                copied++;
            }
            return copied;
        }

        void ClearColliderSelection()
        {
            _selectedColliders.Clear();
            ClearColliderTransform();
            ClearSocketSelection();
        }

        void PruneColliderSelection(SpriteClipDef clip, int frame)
        {
            _selectedColliders.RemoveWhere(box => box == null || box.Locked ||
                !_profile.Hitboxes.Contains(box) || clip == null ||
                (box.IsCharacter
                    ? !box.AppliesToClip(clip.Name)
                    : box.IsClip
                        ? box.ClipName != clip.Name
                        : box.ClipName != clip.Name || box.FrameIndex != frame));
        }

    }
}

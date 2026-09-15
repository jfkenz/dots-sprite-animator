using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    public sealed partial class SpriteSheetToolWindow
    {
        const float PartsTreeDragThreshold = 5f;
        const float PartsTreeAutoExpandMs = 500f;

        readonly HashSet<string> _partsSelectedSlotIds = new(StringComparer.Ordinal);
        readonly HashSet<string> _partsExpandedSlotIds = new(StringComparer.Ordinal);
        readonly List<string> _partsTreeRowIds = new();

        string _partsRenameSlotId;
        string _partsRenameDraft;
        bool _partsRenameFocus;
        int _partsRenameControlId = -1;

        bool _partsTreeDragActive;
        bool _partsTreeDragStarted;
        Vector2 _partsTreeDragStartMouse;
        string _partsTreeDragSlotId;
        SpritePartsAuthoringOps.TreeDropKind _partsTreeDropKind;
        string _partsTreeDropRelativeId;
        string _partsTreeDropReason;
        string _partsTreeHoverExpandId;
        double _partsTreeHoverExpandStart;

        void EnsurePartsTreeSelectionSynced()
        {
            if (_profile?.PartsSlots == null)
            {
                _partsSelectedSlotIds.Clear();
                _partsSelectedSlot = -1;
                return;
            }
            _partsSelectedSlotIds.RemoveWhere(id =>
                SpritePartsAuthoringOps.FindSlot(_profile, id) == null);
            if (_partsSelectedSlotIds.Count == 0 &&
                _partsSelectedSlot >= 0 &&
                _partsSelectedSlot < _profile.PartsSlots.Count &&
                _profile.PartsSlots[_partsSelectedSlot] != null)
            {
                _partsSelectedSlotIds.Add(
                    SpritePartIdUtility.Canonical(_profile.PartsSlots[_partsSelectedSlot].SlotId));
            }
            if (_partsSelectedSlotIds.Count > 0)
            {
                string primary = null;
                foreach (var id in _partsSelectedSlotIds) { primary = id; break; }
                _partsSelectedSlot = SpritePartsAuthoringOps.FindSlotIndex(_profile, primary);
            }
            else
            {
                _partsSelectedSlot = -1;
            }
        }

        void SelectPartsSlotId(string slotId, bool additive, bool range)
        {
            string id = SpritePartIdUtility.Canonical(slotId);
            if (string.IsNullOrEmpty(id)) return;

            if (range && _partsSelectedSlotIds.Count > 0 && _partsTreeRowIds.Count > 0)
            {
                string anchor = null;
                foreach (var s in _partsSelectedSlotIds) { anchor = s; break; }
                int a = _partsTreeRowIds.IndexOf(anchor);
                int b = _partsTreeRowIds.IndexOf(id);
                if (a >= 0 && b >= 0)
                {
                    if (!additive) _partsSelectedSlotIds.Clear();
                    int lo = Math.Min(a, b), hi = Math.Max(a, b);
                    for (int i = lo; i <= hi; i++)
                        _partsSelectedSlotIds.Add(_partsTreeRowIds[i]);
                    _partsSelectedSlot = SpritePartsAuthoringOps.FindSlotIndex(_profile, id);
                    return;
                }
            }

            if (additive)
            {
                if (!_partsSelectedSlotIds.Add(id))
                    _partsSelectedSlotIds.Remove(id);
            }
            else
            {
                _partsSelectedSlotIds.Clear();
                _partsSelectedSlotIds.Add(id);
            }
            _partsSelectedSlot = SpritePartsAuthoringOps.FindSlotIndex(_profile, id);
        }

        string PrimarySelectedPartsSlotId()
        {
            EnsurePartsTreeSelectionSynced();
            if (_partsSelectedSlotIds.Count == 0) return null;
            foreach (var id in _partsSelectedSlotIds)
                return id;
            return null;
        }

        void DrawPartsTree()
        {
            EnsurePartsTreeSelectionSynced();
            if (_profile.PartsSlots == null || _profile.PartsSlots.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Add a part or drag a sprite onto the canvas.",
                    MessageType.None);
                return;
            }

            // Character virtual root
            var rootRect = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rootRect, new Color(0.18f, 0.2f, 0.24f, 0.6f));
            GUI.Label(new Rect(rootRect.x + 6f, rootRect.y + 1f, rootRect.width - 12f, 18f),
                "Character", EditorStyles.miniLabel);
            HandlePartsTreeRootDrop(rootRect);

            _partsTreeRowIds.Clear();
            var rows = SpritePartsAuthoringOps.BuildTreeRows(_profile);
            var visible = new List<(SpritePartSlotDef slot, int depth)>();
            for (int i = 0; i < rows.Count; i++)
            {
                var (slot, depth) = rows[i];
                if (slot == null) continue;
                if (!IsPartsTreeRowVisible(slot))
                    continue;
                visible.Add((slot, depth));
                _partsTreeRowIds.Add(SpritePartIdUtility.Canonical(slot.SlotId));
            }

            for (int i = 0; i < visible.Count; i++)
                DrawPartsTreeRow(visible[i].slot, visible[i].depth);

            var rootDrop = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
            GUI.Label(rootDrop, "Move to root", EditorStyles.centeredGreyMiniLabel);
            HandlePartsTreeRootDrop(rootDrop);

            HandlePartsTreeDragEvents();
            HandlePartsTreeRenameHotkeys();
            HandlePartsTreeDeleteHotkeys();
        }

        bool IsPartsTreeRowVisible(SpritePartSlotDef slot)
        {
            string parent = slot.ParentSlotId;
            var guard = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrWhiteSpace(parent) && guard.Add(parent))
            {
                string pid = SpritePartIdUtility.Canonical(parent);
                if (!_partsExpandedSlotIds.Contains(pid))
                    return false;
                var p = SpritePartsAuthoringOps.FindSlot(_profile, pid);
                if (p == null) break;
                parent = p.ParentSlotId;
            }
            return true;
        }

        void DrawPartsTreeRow(SpritePartSlotDef slot, int depth)
        {
            string id = SpritePartIdUtility.Canonical(slot.SlotId);
            bool selected = _partsSelectedSlotIds.Contains(id);
            bool hasChildren = SpritePartsAuthoringOps.GetChildrenSorted(_profile, slot.SlotId).Count > 0;
            bool expanded = _partsExpandedSlotIds.Contains(id);
            bool renaming = _partsRenameSlotId == id;
            bool hiddenByAncestor = SpritePartsAuthoringOps.SlotOrAncestorHidden(_profile, id) && slot.Enabled;
            bool lockedByAncestor = SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, id) && !slot.EditorLocked;

            var row = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            Color bg = selected
                ? new Color(0.22f, 0.45f, 0.75f, 0.55f)
                : (row.Contains(Event.current.mousePosition)
                    ? new Color(1f, 1f, 1f, 0.06f)
                    : Color.clear);
            if (_partsTreeDragActive && _partsTreeDropRelativeId == id)
            {
                if (_partsTreeDropKind == SpritePartsAuthoringOps.TreeDropKind.ParentUnder)
                    bg = string.IsNullOrEmpty(_partsTreeDropReason)
                        ? new Color(0.2f, 0.6f, 0.35f, 0.45f)
                        : new Color(0.7f, 0.25f, 0.2f, 0.45f);
            }
            if (bg.a > 0f) EditorGUI.DrawRect(row, bg);

            // Insertion lines
            if (_partsTreeDragActive && _partsTreeDropRelativeId == id)
            {
                if (_partsTreeDropKind == SpritePartsAuthoringOps.TreeDropKind.InsertBefore)
                    EditorGUI.DrawRect(new Rect(row.x + 8f + depth * 14f, row.y, row.width - 16f, 2f), Color.cyan);
                else if (_partsTreeDropKind == SpritePartsAuthoringOps.TreeDropKind.InsertAfter)
                    EditorGUI.DrawRect(new Rect(row.x + 8f + depth * 14f, row.yMax - 2f, row.width - 16f, 2f), Color.cyan);
            }

            float x = row.x + 4f + depth * 14f;
            // Chevron
            var chevronRect = new Rect(x, row.y + 2f, 16f, 18f);
            if (hasChildren)
            {
                if (GUI.Button(chevronRect, expanded ? "▼" : "▶", EditorStyles.miniLabel))
                {
                    if (expanded) _partsExpandedSlotIds.Remove(id);
                    else _partsExpandedSlotIds.Add(id);
                }
            }
            x += 16f;

            // Thumbnail placeholder
            var thumb = new Rect(x, row.y + 3f, 16f, 16f);
            EditorGUI.DrawRect(thumb, new Color(0.3f, 0.32f, 0.36f, 1f));
            x += 20f;

            // Name / rename field
            float iconsW = 44f;
            var nameRect = new Rect(x, row.y + 1f, Mathf.Max(40f, row.xMax - iconsW - x - 4f), 20f);
            if (renaming)
            {
                GUI.SetNextControlName("PartsRenameField");
                _partsRenameDraft = GUI.TextField(nameRect, _partsRenameDraft ?? string.Empty);
                if (_partsRenameFocus)
                {
                    EditorGUI.FocusTextInControl("PartsRenameField");
                    _partsRenameFocus = false;
                }
                var e = Event.current;
                if (e.type == EventType.KeyDown)
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    {
                        CommitPartsRename();
                        e.Use();
                    }
                    else if (e.keyCode == KeyCode.Escape)
                    {
                        CancelPartsRename();
                        e.Use();
                    }
                }
                if (e.type == EventType.MouseDown && !nameRect.Contains(e.mousePosition))
                    CommitPartsRename();
            }
            else
            {
                string label = slot.Name ?? string.Empty;
                if (hiddenByAncestor || !slot.Enabled)
                    GUI.contentColor = new Color(1f, 1f, 1f, 0.45f);
                GUI.Label(nameRect, new GUIContent(label, label));
                GUI.contentColor = Color.white;
            }
            x = nameRect.xMax + 2f;

            // Eye
            var eyeRect = new Rect(row.xMax - 40f, row.y + 2f, 18f, 18f);
            if (GUI.Button(eyeRect, slot.Enabled ? "V" : "H", EditorStyles.miniButton))
            {
                RecordProfileUndo(slot.Enabled ? "Hide Parts Slot" : "Show Parts Slot");
                slot.Enabled = !slot.Enabled;
                SaveDirty();
            }

            // Lock
            var lockRect = new Rect(row.xMax - 20f, row.y + 2f, 18f, 18f);
            if (GUI.Button(lockRect, slot.EditorLocked ? "L" : "·", EditorStyles.miniButton))
            {
                RecordProfileUndo(slot.EditorLocked ? "Unlock Parts Slot" : "Lock Parts Slot");
                slot.EditorLocked = !slot.EditorLocked;
                SaveDirty();
            }

            if (!string.IsNullOrEmpty(_partsTreeDropReason) &&
                _partsTreeDragActive && _partsTreeDropRelativeId == id)
            {
                var tip = new Rect(row.x + 8f, row.yMax - 1f, row.width - 16f, 14f);
                // reason shown via status; keep row clean
            }

            HandlePartsTreeRowEvents(row, slot, id, nameRect, chevronRect, eyeRect, lockRect);
        }

        void HandlePartsTreeRowEvents(
            Rect row, SpritePartSlotDef slot, string id,
            Rect nameRect, Rect chevronRect, Rect eyeRect, Rect lockRect)
        {
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && row.Contains(evt.mousePosition))
            {
                if (chevronRect.Contains(evt.mousePosition) ||
                    eyeRect.Contains(evt.mousePosition) ||
                    lockRect.Contains(evt.mousePosition))
                    return;

                bool additive = evt.control || evt.command;
                bool range = evt.shift;
                SelectPartsSlotId(id, additive, range);

                if (evt.clickCount == 2 && nameRect.Contains(evt.mousePosition))
                {
                    BeginPartsRename(slot);
                    evt.Use();
                    return;
                }

                // Prepare potential tree drag (Rig only)
                if (_partsMode == SpritePartsStudioMode.Rig &&
                    !slot.EditorLocked &&
                    string.IsNullOrEmpty(_partsRenameSlotId))
                {
                    _partsTreeDragStarted = true;
                    _partsTreeDragActive = false;
                    _partsTreeDragStartMouse = evt.mousePosition;
                    _partsTreeDragSlotId = id;
                }
                evt.Use();
                Repaint();
            }
            else if (evt.type == EventType.ContextClick && row.Contains(evt.mousePosition))
            {
                SelectPartsSlotId(id, false, false);
                ShowPartsTreeContextMenu(slot);
                evt.Use();
            }

            if (_partsTreeDragActive || _partsTreeDragStarted)
                UpdatePartsTreeDropTarget(row, id, evt.mousePosition);
        }

        void HandlePartsTreeRootDrop(Rect rect)
        {
            if (!_partsTreeDragActive && !_partsTreeDragStarted) return;
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;
            _partsTreeDropKind = SpritePartsAuthoringOps.TreeDropKind.MoveToRoot;
            _partsTreeDropRelativeId = string.Empty;
            var v = SpritePartsAuthoringOps.ValidateTreeMove(
                _profile, _partsTreeDragSlotId,
                SpritePartsAuthoringOps.TreeDropKind.MoveToRoot, string.Empty);
            _partsTreeDropReason = v.Ok ? null : v.Reason;
            if (evt.type == EventType.Repaint && v.Ok)
                EditorGUI.DrawRect(rect, new Color(0.2f, 0.55f, 0.4f, 0.35f));
        }

        void UpdatePartsTreeDropTarget(Rect row, string id, Vector2 mouse)
        {
            if (!_partsTreeDragActive) return;
            if (!row.Contains(mouse)) return;
            if (id == _partsTreeDragSlotId)
            {
                _partsTreeDropKind = SpritePartsAuthoringOps.TreeDropKind.None;
                _partsTreeDropReason = "Cannot drop on self.";
                _partsTreeDropRelativeId = id;
                return;
            }

            float y = mouse.y - row.y;
            float h = row.height;
            SpritePartsAuthoringOps.TreeDropKind kind;
            if (y < h * 0.25f)
                kind = SpritePartsAuthoringOps.TreeDropKind.InsertBefore;
            else if (y > h * 0.75f)
                kind = SpritePartsAuthoringOps.TreeDropKind.InsertAfter;
            else
                kind = SpritePartsAuthoringOps.TreeDropKind.ParentUnder;

            _partsTreeDropKind = kind;
            _partsTreeDropRelativeId = id;
            var v = SpritePartsAuthoringOps.ValidateTreeMove(
                _profile, _partsTreeDragSlotId, kind, id);
            _partsTreeDropReason = v.Ok ? null : v.Reason;

            // Auto-expand collapsed valid parent on hover ~500ms
            if (kind == SpritePartsAuthoringOps.TreeDropKind.ParentUnder && v.Ok)
            {
                if (_partsTreeHoverExpandId != id)
                {
                    _partsTreeHoverExpandId = id;
                    _partsTreeHoverExpandStart = EditorApplication.timeSinceStartup;
                }
                else if ((EditorApplication.timeSinceStartup - _partsTreeHoverExpandStart) * 1000.0 >=
                         PartsTreeAutoExpandMs)
                {
                    _partsExpandedSlotIds.Add(id);
                }
            }
        }

        void HandlePartsTreeDragEvents()
        {
            var evt = Event.current;
            if (_partsTreeDragStarted && !_partsTreeDragActive &&
                evt.type == EventType.MouseDrag && evt.button == 0)
            {
                if ((evt.mousePosition - _partsTreeDragStartMouse).magnitude >= PartsTreeDragThreshold)
                {
                    if (_partsMode != SpritePartsStudioMode.Rig)
                    {
                        _status = "Switch to Rig to edit hierarchy.";
                        _partsTreeDragStarted = false;
                        return;
                    }
                    _partsTreeDragActive = true;
                    evt.Use();
                    Repaint();
                }
            }

            if (_partsTreeDragActive && evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                CancelPartsTreeDrag();
                evt.Use();
                return;
            }

            if ((_partsTreeDragActive || _partsTreeDragStarted) &&
                evt.type == EventType.MouseUp && evt.button == 0)
            {
                if (_partsTreeDragActive &&
                    _partsTreeDropKind != SpritePartsAuthoringOps.TreeDropKind.None &&
                    string.IsNullOrEmpty(_partsTreeDropReason))
                {
                    CommitPartsTreeDrag();
                }
                else if (_partsTreeDragActive && !string.IsNullOrEmpty(_partsTreeDropReason))
                {
                    _status = _partsTreeDropReason;
                }
                CancelPartsTreeDrag();
                evt.Use();
            }
        }

        void CancelPartsTreeDrag()
        {
            _partsTreeDragActive = false;
            _partsTreeDragStarted = false;
            _partsTreeDragSlotId = null;
            _partsTreeDropKind = SpritePartsAuthoringOps.TreeDropKind.None;
            _partsTreeDropRelativeId = null;
            _partsTreeDropReason = null;
            _partsTreeHoverExpandId = null;
            Repaint();
        }

        void CommitPartsTreeDrag()
        {
            string moving = _partsTreeDragSlotId;
            var kind = _partsTreeDropKind;
            string relative = _partsTreeDropRelativeId;
            var preview = SpritePartsAuthoringOps.ValidateTreeMove(_profile, moving, kind, relative);
            if (!preview.Ok)
            {
                _status = preview.Reason;
                return;
            }
            bool confirm = true;
            if (preview.NeedsAnimationReview)
            {
                confirm = EditorUtility.DisplayDialog(
                    "Reparent Parts",
                    $"Keep rest pose; animation motion may change.\n\nMoved: {moving}\nAffected clips: {preview.AffectedClipCount}",
                    "Reparent",
                    "Cancel");
            }
            if (!confirm) return;

            RecordProfileUndo("Reparent Parts");
            var result = SpritePartsAuthoringOps.TryCommitTreeMove(
                _profile, moving, kind, relative, confirmAnimationReview: true);
            if (!result.Ok)
            {
                _status = result.Reason ?? "Reparent failed.";
                return;
            }
            SaveDirty();
            _status = kind == SpritePartsAuthoringOps.TreeDropKind.ParentUnder
                ? $"Parented under {relative}"
                : "Hierarchy updated";
            SelectPartsSlotId(moving, false, false);
        }

        void HandlePartsTreeRenameHotkeys()
        {
            if (!string.IsNullOrEmpty(_partsRenameSlotId)) return;
            var evt = Event.current;
            if (evt.type != EventType.KeyDown) return;
            if (EditorGUIUtility.editingTextField) return;
            if (evt.keyCode == KeyCode.F2 && _partsSelectedSlotIds.Count == 1)
            {
                var slot = SpritePartsAuthoringOps.FindSlot(_profile, PrimarySelectedPartsSlotId());
                if (slot != null) BeginPartsRename(slot);
                evt.Use();
            }
        }

        void BeginPartsRename(SpritePartSlotDef slot)
        {
            if (slot == null) return;
            if (slot.EditorLocked ||
                SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, slot.SlotId))
            {
                _status = "Part is locked.";
                return;
            }
            // Rename is metadata — allowed in all modes.
            _partsRenameSlotId = SpritePartIdUtility.Canonical(slot.SlotId);
            _partsRenameDraft = slot.Name;
            _partsRenameFocus = true;
            Repaint();
        }

        void CommitPartsRename()
        {
            if (string.IsNullOrEmpty(_partsRenameSlotId)) return;
            string id = _partsRenameSlotId;
            string draft = _partsRenameDraft;
            _partsRenameSlotId = null;
            _partsRenameDraft = null;
            RecordProfileUndo("Rename Parts Slot");
            var result = SpritePartsAuthoringOps.TryRenameDisplayName(_profile, id, draft);
            if (!result.Ok)
            {
                _status = result.Reason;
                // keep previous name
            }
            else
            {
                SaveDirty();
                _status = "Renamed part";
            }
            GUI.FocusControl(null);
            Repaint();
        }

        void CancelPartsRename()
        {
            _partsRenameSlotId = null;
            _partsRenameDraft = null;
            GUI.FocusControl(null);
            Repaint();
        }

        void ShowPartsTreeContextMenu(SpritePartSlotDef slot)
        {
            var menu = new GenericMenu();
            string id = SpritePartIdUtility.Canonical(slot.SlotId);
            bool isRig = _partsMode == SpritePartsStudioMode.Rig;
            bool locked = slot.EditorLocked ||
                          SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, slot.SlotId);

            menu.AddItem(new GUIContent("Rename"), false, () => BeginPartsRename(slot));

            if (isRig && !locked)
            {
                menu.AddItem(new GUIContent("Add Child"), false, () => AddPartsChildOf(slot.SlotId));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Break from Parent"), false, () => BreakPartsFromParent(id));
                menu.AddItem(new GUIContent("Move Up One Level"), false, () => MovePartsUpOneLevel(id));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Move to Top of Siblings"), false, () =>
                {
                    RecordProfileUndo("Move Parts Sibling To Top");
                    var r = SpritePartsAuthoringOps.TryMoveSiblingToTop(_profile, id);
                    if (!r.Ok) _status = r.Reason;
                    else { SaveDirty(); _status = "Moved to top of siblings"; }
                });
                menu.AddItem(new GUIContent("Move to Bottom of Siblings"), false, () =>
                {
                    RecordProfileUndo("Move Parts Sibling To Bottom");
                    var r = SpritePartsAuthoringOps.TryMoveSiblingToBottom(_profile, id);
                    if (!r.Ok) _status = r.Reason;
                    else { SaveDirty(); _status = "Moved to bottom of siblings"; }
                });
            }
            else if (!isRig)
            {
                menu.AddDisabledItem(new GUIContent("Add Child (Switch to Rig…)"));
                menu.AddDisabledItem(new GUIContent("Break from Parent (Switch to Rig…)"));
                menu.AddDisabledItem(new GUIContent("Move Up One Level (Switch to Rig…)"));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Add Child (Part is locked)"));
                menu.AddDisabledItem(new GUIContent("Break from Parent (Part is locked)"));
                menu.AddDisabledItem(new GUIContent("Move Up One Level (Part is locked)"));
            }

            menu.AddSeparator("");
            // Delete always listed; disabled with clear reason when blocked.
            string deleteReason = null;
            if (!isRig)
                deleteReason = "Switch to Rig to edit hierarchy.";
            else
            {
                var v = SpritePartsAuthoringOps.ValidateDeleteSubtree(_profile, id);
                if (!v.Ok) deleteReason = v.Reason;
            }
            if (deleteReason == null)
            {
                menu.AddItem(new GUIContent("Delete Subtree"), false, () => DeletePartsSubtree(id));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Delete Subtree (" + deleteReason + ")"));
            }

            if (!isRig)
            {
                menu.AddSeparator("");
                menu.AddDisabledItem(new GUIContent("Animate/Skins: Switch to Rig to edit hierarchy…"));
            }

            menu.ShowAsContext();
        }
        void DrawPartsTreeToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            bool canAdd = _partsMode == SpritePartsStudioMode.Rig ||
                          _profile.AnimKind == SpriteAnimKind.Parts;
            // + Part always root-level when Rig; in other modes still allow metadata? Contract: structural = Rig only.
            using (new EditorGUI.DisabledScope(_partsMode != SpritePartsStudioMode.Rig))
            {
                if (GUILayout.Button("+ Part", GUILayout.Width(60f)))
                    AddPartsSlot();
                if (GUILayout.Button("▾", GUILayout.Width(22f)))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Add Root Part"), false, AddPartsSlot);
                    string parentId = null;
                    if (_partsSelectedSlotIds.Count == 1)
                        parentId = PrimarySelectedPartsSlotId();
                    var parent = parentId != null
                        ? SpritePartsAuthoringOps.FindSlot(_profile, parentId)
                        : null;
                    if (parent != null && !parent.EditorLocked &&
                        !SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, parent.SlotId))
                    {
                        menu.AddItem(new GUIContent("Add Child of " + parent.Name), false,
                            () => AddPartsChildOf(parent.SlotId));
                    }
                    else
                    {
                        menu.AddDisabledItem(new GUIContent("Add Child of [select one unlocked parent]"));
                    }
                    menu.ShowAsContext();
                }
            }
            if (_partsMode != SpritePartsStudioMode.Rig)
                GUILayout.Label("Switch to Rig to edit hierarchy.", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        void AddPartsSlot()
        {
            if (_partsMode != SpritePartsStudioMode.Rig)
            {
                _status = "Switch to Rig to edit hierarchy.";
                return;
            }
            RecordProfileUndo("Add Parts Slot");
            var result = SpritePartsAuthoringOps.TryAddRootPart(_profile, out var created);
            if (!result.Ok)
            {
                _status = result.Reason;
                return;
            }
            SaveDirty();
            SelectPartsSlotId(created.SlotId, false, false);
            _partsExpandedSlotIds.Clear(); // keep; expand nothing needed for root
            BeginPartsRename(created);
            _status = "Added root part " + created.Name;
        }

        void AddPartsChildOf(string parentSlotId)
        {
            if (_partsMode != SpritePartsStudioMode.Rig)
            {
                _status = "Switch to Rig to edit hierarchy.";
                return;
            }
            RecordProfileUndo("Add Child Parts Slot");
            var result = SpritePartsAuthoringOps.TryAddChildPart(_profile, parentSlotId, out var created);
            if (!result.Ok)
            {
                _status = result.Reason;
                return;
            }
            _partsExpandedSlotIds.Add(SpritePartIdUtility.Canonical(parentSlotId));
            SaveDirty();
            SelectPartsSlotId(created.SlotId, false, false);
            BeginPartsRename(created);
            _status = "Added child part " + created.Name;
        }

        void HandlePartsTreeDeleteHotkeys()
        {
            if (!string.IsNullOrEmpty(_partsRenameSlotId)) return;
            if (EditorGUIUtility.editingTextField) return;
            var evt = Event.current;
            if (evt.type != EventType.KeyDown) return;
            if (evt.keyCode != KeyCode.Delete && evt.keyCode != KeyCode.Backspace) return;
            if (_partsSelectedSlotIds.Count == 0) return;

            // Consume always when Parts selection is active so frame-clip Delete does not steal it.
            TryDeleteSelectedPartsFromHotkey();
            evt.Use();
            Repaint();
        }

        /// <summary>Shared by tree hotkey and window-level Delete binding.</summary>
        internal bool TryDeleteSelectedPartsFromHotkey()
        {
            string id = PrimarySelectedPartsSlotId();
            if (string.IsNullOrEmpty(id))
            {
                _status = "No part selected.";
                return false;
            }
            if (!string.IsNullOrEmpty(_partsRenameSlotId))
            {
                _status = "Finish renaming before delete.";
                return false;
            }

            string block = SpritePartsAuthoringOps.DescribeHierarchyEditBlock(
                _profile, id, _partsMode, "delete");
            if (!string.IsNullOrEmpty(block))
            {
                _status = block;
                return false;
            }

            DeletePartsSubtree(id);
            return true;
        }

        void BreakPartsFromParent(string slotId)
        {
            if (_partsMode != SpritePartsStudioMode.Rig)
            {
                _status = "Switch to Rig to edit hierarchy.";
                return;
            }
            var preview = SpritePartsAuthoringOps.ValidateTreeMove(
                _profile, slotId,
                SpritePartsAuthoringOps.TreeDropKind.MoveToRoot, string.Empty);
            if (!preview.Ok)
            {
                _status = preview.Reason;
                return;
            }
            bool confirm = true;
            if (preview.NeedsAnimationReview)
            {
                confirm = EditorUtility.DisplayDialog(
                    "Break from Parent",
                    $"Keep rest pose; animation motion may change.\n\nMoved: {slotId}\nAffected clips: {preview.AffectedClipCount}",
                    "Break",
                    "Cancel");
            }
            if (!confirm) return;
            RecordProfileUndo("Break Parts From Parent");
            var result = SpritePartsAuthoringOps.TryBreakFromParent(
                _profile, slotId, confirmAnimationReview: true);
            if (!result.Ok)
            {
                _status = result.Reason ?? "Break from parent failed.";
                return;
            }
            SaveDirty();
            _status = "Broke from parent → Character root";
            SelectPartsSlotId(slotId, false, false);
        }

        void MovePartsUpOneLevel(string slotId)
        {
            if (_partsMode != SpritePartsStudioMode.Rig)
            {
                _status = "Switch to Rig to edit hierarchy.";
                return;
            }
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, slotId);
            if (slot == null)
            {
                _status = "Slot not found.";
                return;
            }
            if (string.IsNullOrWhiteSpace(slot.ParentSlotId))
            {
                _status = "Already at Character root.";
                return;
            }

            // Preview via the same commit path's validation.
            var parent = SpritePartsAuthoringOps.FindSlot(_profile, slot.ParentSlotId);
            SpritePartsAuthoringOps.TreeDropKind kind;
            string relative;
            if (parent == null || string.IsNullOrWhiteSpace(parent.ParentSlotId))
            {
                kind = SpritePartsAuthoringOps.TreeDropKind.MoveToRoot;
                relative = string.Empty;
            }
            else
            {
                kind = SpritePartsAuthoringOps.TreeDropKind.ParentUnder;
                relative = parent.ParentSlotId;
            }
            var preview = SpritePartsAuthoringOps.ValidateTreeMove(_profile, slotId, kind, relative);
            if (!preview.Ok)
            {
                _status = preview.Reason;
                return;
            }
            bool confirm = true;
            if (preview.NeedsAnimationReview)
            {
                confirm = EditorUtility.DisplayDialog(
                    "Move Up One Level",
                    $"Keep rest pose; animation motion may change.\n\nMoved: {slotId}\nAffected clips: {preview.AffectedClipCount}",
                    "Move Up",
                    "Cancel");
            }
            if (!confirm) return;
            RecordProfileUndo("Move Parts Up One Level");
            var result = SpritePartsAuthoringOps.TryMoveUpOneLevel(
                _profile, slotId, confirmAnimationReview: true);
            if (!result.Ok)
            {
                _status = result.Reason ?? "Move up failed.";
                return;
            }
            SaveDirty();
            _status = "Moved up one level";
            SelectPartsSlotId(slotId, false, false);
        }
        void DeletePartsSubtree(string slotId)
        {
            if (_partsMode != SpritePartsStudioMode.Rig)
            {
                _status = "Switch to Rig to edit hierarchy.";
                return;
            }
            var subtree = SpritePartsAuthoringOps.CollectSubtreeSlotIds(_profile, slotId);
            int clips = SpritePartsAuthoringOps.CountClipsWithKeysInSubtree(_profile, subtree);
            int bindings = 0;
            var idSet = new HashSet<string>(subtree, StringComparer.Ordinal);
            if (_profile.PartsSkins != null)
            {
                for (int i = 0; i < _profile.PartsSkins.Count; i++)
                {
                    var skin = _profile.PartsSkins[i];
                    if (skin?.Bindings == null) continue;
                    for (int b = 0; b < skin.Bindings.Count; b++)
                    {
                        var bind = skin.Bindings[b];
                        if (bind != null && idSet.Contains(SpritePartIdUtility.Canonical(bind.SlotId)))
                            bindings++;
                    }
                }
            }
            if (!EditorUtility.DisplayDialog(
                    "Delete Part / Delete Subtree",
                    $"Delete {subtree.Count} part(s).\nClips with keys: {clips}\nSkin bindings: {bindings}\n\nThis cannot leave dangling tracks.",
                    "Delete",
                    "Cancel"))
                return;

            RecordProfileUndo("Delete Parts Subtree");
            var result = SpritePartsAuthoringOps.TryDeleteSubtree(_profile, slotId);
            if (!result.Ok)
            {
                _status = result.Reason;
                return;
            }
            _partsSelectedSlotIds.Clear();
            _partsSelectedSlot = -1;
            SaveDirty();
            _status =
                $"Deleted {result.DeletedSlotCount} part(s), {result.DeletedTrackCount} track(s), {result.DeletedBindingCount} binding(s)";
        }
    }
}

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
        readonly HashSet<string> _partsCollapsedGroupIds = new(StringComparer.Ordinal);
        readonly List<string> _partsTreeRowIds = new();

        string _partsRenameSlotId;
        string _partsRenameGroupId;
        string _partsRenameDraft;
        bool _partsRenameFocus;
        int _partsRenameControlId = -1;
        string _partsIsolatedSlotId;
        string _partsActiveGroupId;

        bool _partsTreeDragActive;
        bool _partsTreeDragStarted;
        Vector2 _partsTreeDragStartMouse;
        string _partsTreeDragSlotId;
        SpritePartsAuthoringOps.TreeDropKind _partsTreeDropKind;
        string _partsTreeDropRelativeId;
        string _partsTreeDropReason;
        string _partsTreeHoverExpandId;
        double _partsTreeHoverExpandStart;
        int _partsTreeDragControlId;
        int _partsLayerDragControlId;
        bool _partsLayerDragStarted;
        bool _partsLayerDragActive;
        Vector2 _partsLayerDragStartMouse;
        string _partsLayerDragSlotId;
        int _partsLayerDropIndex = -1;

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
            if (!string.IsNullOrEmpty(_partsIsolatedSlotId) &&
                SpritePartsAuthoringOps.FindSlot(_profile, _partsIsolatedSlotId) == null)
                _partsIsolatedSlotId = null;
            if (!string.IsNullOrEmpty(_partsPivotFocusSlotId) &&
                SpritePartsAuthoringOps.FindSlot(_profile, _partsPivotFocusSlotId) == null)
                _partsPivotFocusSlotId = null;
            if (!string.IsNullOrEmpty(_partsActiveGroupId) &&
                SpritePartsAuthoringOps.FindGroup(_profile, _partsActiveGroupId) == null)
                _partsActiveGroupId = null;
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

        void ClearPartsSelection()
        {
            _partsSelectedSlotIds.Clear();
            _partsSelectedSlot = -1;
            _partsIsolatedSlotId = null;
            _partsActiveGroupId = null;
        }

        void SelectPartsSlotId(string slotId, bool additive, bool range)
        {
            string id = SpritePartIdUtility.Canonical(slotId);
            if (string.IsNullOrEmpty(id)) return;
            _partsBrowserFocus = PartsBrowserFocus.Tree;
            if (!additive && !range)
            {
                _partsIsolatedSlotId = null;
                _partsActiveGroupId = null;
            }

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
                    _partsIsolatedSlotId = null;
                    _partsActiveGroupId = null;
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

        void SelectPartsGroup(string groupId, string primarySlotId = null)
        {
            string gid = SpritePartsAuthoringOps.CanonicalGroupId(groupId);
            var members = SpritePartsAuthoringOps.GetGroupMemberSlotIds(_profile, gid);
            if (members.Count == 0)
                return;
            _partsBrowserFocus = PartsBrowserFocus.Tree;
            _partsIsolatedSlotId = null;
            _partsActiveGroupId = gid;
            _partsSelectedSlotIds.Clear();
            for (int i = 0; i < members.Count; i++)
                _partsSelectedSlotIds.Add(members[i]);
            string primary = !string.IsNullOrEmpty(primarySlotId)
                ? SpritePartIdUtility.Canonical(primarySlotId)
                : members[0];
            if (!_partsSelectedSlotIds.Contains(primary))
                primary = members[0];
            _partsSelectedSlot = SpritePartsAuthoringOps.FindSlotIndex(_profile, primary);
        }

        void IsolatePartsSlot(string slotId)
        {
            string id = SpritePartIdUtility.Canonical(slotId);
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, id);
            if (slot == null) return;
            string gid = SpritePartsAuthoringOps.SlotGroupId(slot);
            _partsBrowserFocus = PartsBrowserFocus.Tree;
            _partsIsolatedSlotId = id;
            _partsActiveGroupId = SpritePartsAuthoringOps.FindGroup(_profile, gid) != null
                ? gid
                : null;
            _partsSelectedSlotIds.Clear();
            _partsSelectedSlotIds.Add(id);
            _partsSelectedSlot = SpritePartsAuthoringOps.FindSlotIndex(_profile, id);
        }

        bool IsPartsIsolating()
            => !string.IsNullOrEmpty(_partsIsolatedSlotId);

        bool TryExitPartsIsolate()
        {
            if (!IsPartsIsolating())
                return false;
            string gid = _partsActiveGroupId;
            if (!string.IsNullOrEmpty(gid) &&
                SpritePartsAuthoringOps.FindGroup(_profile, gid) != null)
                SelectPartsGroup(gid, _partsIsolatedSlotId);
            else
                _partsIsolatedSlotId = null;
            _status = "Left isolate";
            Repaint();
            return true;
        }

        void SelectPartsTreeSlot(SpritePartSlotDef slot, bool additive, bool range)
        {
            if (slot == null) return;
            string id = SpritePartIdUtility.Canonical(slot.SlotId);
            string gid = SpritePartsAuthoringOps.SlotGroupId(slot);
            bool grouped = !additive && !range &&
                           SpritePartsAuthoringOps.FindGroup(_profile, gid) != null;
            if (grouped)
                IsolatePartsSlot(id);
            else
                SelectPartsSlotId(id, additive, range);
        }

        void SelectPartsCanvasClicked(string slotId, bool additive, bool isolate, bool altPick)
        {
            string id = SpritePartIdUtility.Canonical(slotId);
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, id);
            if (slot == null)
            {
                SelectPartsSlotId(id, additive, false);
                return;
            }
            string gid = SpritePartsAuthoringOps.SlotGroupId(slot);
            bool grouped = SpritePartsAuthoringOps.FindGroup(_profile, gid) != null;

            if (altPick)
            {
                SelectPartsSlotId(id, additive, false);
                _partsIsolatedSlotId = null;
                _partsActiveGroupId = grouped && !additive ? gid : null;
                return;
            }

            if (isolate)
            {
                IsolatePartsSlot(id);
                return;
            }

            if (IsPartsIsolating())
            {
                if (grouped && gid == _partsActiveGroupId)
                {
                    IsolatePartsSlot(id);
                    return;
                }
                _partsIsolatedSlotId = null;
            }

            if (grouped && !additive)
                SelectPartsGroup(gid, id);
            else
                SelectPartsSlotId(id, additive, false);
        }

        void ExpandMarqueeHitsToGroups()
        {
            if (IsPartsIsolating() || _profile?.PartsSlots == null)
                return;
            var extra = new List<string>();
            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in _partsSelectedSlotIds)
            {
                var slot = SpritePartsAuthoringOps.FindSlot(_profile, id);
                if (slot == null) continue;
                string gid = SpritePartsAuthoringOps.SlotGroupId(slot);
                if (string.IsNullOrEmpty(gid) ||
                    SpritePartsAuthoringOps.FindGroup(_profile, gid) == null)
                    continue;
                groupIds.Add(gid);
                var members = SpritePartsAuthoringOps.GetGroupMemberSlotIds(_profile, gid);
                for (int i = 0; i < members.Count; i++)
                    extra.Add(members[i]);
            }
            for (int i = 0; i < extra.Count; i++)
                _partsSelectedSlotIds.Add(extra[i]);
            _partsActiveGroupId = groupIds.Count == 1 ? FirstHashSetValue(groupIds) : null;
            _partsIsolatedSlotId = null;
        }

        static string FirstHashSetValue(HashSet<string> set)
        {
            foreach (var v in set)
                return v;
            return null;
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

            _partsTreeDragControlId = GUIUtility.GetControlID(FocusType.Passive);

            // Character virtual root
            var rootRect = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rootRect, new Color(0.18f, 0.2f, 0.24f, 0.6f));
            GUI.Label(new Rect(rootRect.x + 6f, rootRect.y + 1f, rootRect.width - 12f, 18f),
                "Character", EditorStyles.miniLabel);
            HandlePartsTreeRootDrop(rootRect, isCharacterHeader: true);

            _partsTreeRowIds.Clear();
            var items = new List<PartsTreeDrawItem>();
            EmitPartsTreeItems(string.Empty, 0, items);
            var known = new HashSet<string>(StringComparer.Ordinal);
            if (_profile.PartsSlots != null)
            {
                for (int i = 0; i < _profile.PartsSlots.Count; i++)
                {
                    var s = _profile.PartsSlots[i];
                    if (s != null) known.Add(SpritePartIdUtility.Canonical(s.SlotId));
                }
                var emitted = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Slot != null)
                        emitted.Add(SpritePartIdUtility.Canonical(items[i].Slot.SlotId));
                }
                for (int i = 0; i < _profile.PartsSlots.Count; i++)
                {
                    var s = _profile.PartsSlots[i];
                    if (s == null || string.IsNullOrWhiteSpace(s.ParentSlotId)) continue;
                    string p = SpritePartIdUtility.Canonical(s.ParentSlotId);
                    string sid = SpritePartIdUtility.Canonical(s.SlotId);
                    if (!known.Contains(p) && emitted.Add(sid))
                        items.Add(new PartsTreeDrawItem { Slot = s, Depth = 0 });
                }
            }

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Group != null)
                    DrawPartsGroupRow(item.Group, item.Depth);
                else if (item.Slot != null)
                {
                    _partsTreeRowIds.Add(SpritePartIdUtility.Canonical(item.Slot.SlotId));
                    DrawPartsTreeRow(item.Slot, item.Depth);
                    if (IsPartsMeshEdit() &&
                        SpritePartIdUtility.Canonical(item.Slot.SlotId) ==
                        SpritePartIdUtility.Canonical(_partsMeshEditSlotId))
                        DrawMeshVertexRows(item.Depth + 1);
                }
            }

            var rootDrop = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
            GUI.Label(rootDrop, "Move to root", EditorStyles.centeredGreyMiniLabel);
            HandlePartsTreeRootDrop(rootDrop, isCharacterHeader: false);

            HandlePartsTreeDragEvents();
            HandlePartsTreeRenameHotkeys();
            HandlePartsTreeDeleteHotkeys();
        }

        struct PartsTreeDrawItem
        {
            public SpritePartSlotDef Slot;
            public SpritePartsGroupDef Group;
            public int Depth;
        }

        void EmitPartsTreeItems(string parentId, int depth, List<PartsTreeDrawItem> items)
        {
            var kids = SpritePartsAuthoringOps.GetChildrenSorted(_profile, parentId);
            var seenGroups = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < kids.Count; i++)
            {
                var child = kids[i];
                if (child == null) continue;
                string gid = SpritePartsAuthoringOps.SlotGroupId(child);
                var group = !string.IsNullOrEmpty(gid)
                    ? SpritePartsAuthoringOps.FindGroup(_profile, gid)
                    : null;
                if (group != null)
                {
                    if (!seenGroups.Add(gid))
                        continue;
                    items.Add(new PartsTreeDrawItem { Group = group, Depth = depth });
                    if (_partsCollapsedGroupIds.Contains(gid))
                        continue;
                    for (int m = 0; m < kids.Count; m++)
                    {
                        var member = kids[m];
                        if (member == null) continue;
                        if (SpritePartsAuthoringOps.SlotGroupId(member) != gid)
                            continue;
                        items.Add(new PartsTreeDrawItem { Slot = member, Depth = depth + 1 });
                        string mid = SpritePartIdUtility.Canonical(member.SlotId);
                        if (_partsExpandedSlotIds.Contains(mid))
                            EmitPartsTreeItems(mid, depth + 2, items);
                    }
                }
                else
                {
                    items.Add(new PartsTreeDrawItem { Slot = child, Depth = depth });
                    string cid = SpritePartIdUtility.Canonical(child.SlotId);
                    if (_partsExpandedSlotIds.Contains(cid))
                        EmitPartsTreeItems(cid, depth + 1, items);
                }
            }
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
            if (_partsTreeDragActive && id == _partsTreeDragSlotId)
                bg = new Color(1f, 1f, 1f, 0.08f);
            if (bg.a > 0f) EditorGUI.DrawRect(row, bg);

            float x = row.x + 4f + depth * 14f;
            // Chevron
            var chevronRect = new Rect(x, row.y + 2f, 16f, 18f);
            if (hasChildren)
            {
                if (GUI.Button(chevronRect, expanded ? "v" : ">", EditorStyles.miniLabel))
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
            float iconsW = 78f;
            var nameRect = new Rect(x, row.y + 1f, Mathf.Max(40f, row.xMax - iconsW - x - 4f), 20f);
            if (renaming)
            {
                GUI.SetNextControlName(PartsSlotRenameControl);
                _partsRenameDraft = GUI.TextField(nameRect, _partsRenameDraft ?? string.Empty);
                if (_partsRenameFocus)
                {
                    EditorGUI.FocusTextInControl(PartsSlotRenameControl);
                    _partsRenameFocus = false;
                }
            }
            else
            {
                string label = slot.Name ?? string.Empty;
                if (slot.EditorLocked || lockedByAncestor)
                    label = "# " + label;
                if (hiddenByAncestor || !slot.Enabled)
                    GUI.contentColor = new Color(1f, 1f, 1f, 0.45f);
                else if (slot.EditorLocked || lockedByAncestor)
                    GUI.contentColor = new Color(1f, 0.85f, 0.55f, 0.95f);
                GUI.Label(nameRect, new GUIContent(label, label));
                GUI.contentColor = Color.white;
            }
            x = nameRect.xMax + 2f;

            EnsurePartsRowIconStyles();
            // Right-edge chrome: Z | x | eye | lock (text always visible).
            var zRect = new Rect(row.xMax - 78f, row.y + 2f, 20f, 16f);
            var deleteRect = new Rect(row.xMax - 56f, row.y + 2f, 18f, 18f);
            var eyeRect = new Rect(row.xMax - 36f, row.y + 2f, 18f, 18f);
            var lockRect = new Rect(row.xMax - 18f, row.y + 2f, 18f, 18f);
            GUI.Label(zRect, new GUIContent(slot.DrawRank.ToString(),
                "Z-order / layer. Higher draws in front. Use Layers panel to reorder."), _mutedStyle);

            bool locked = slot.EditorLocked ||
                          SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, slot.SlotId);
            using (new EditorGUI.DisabledScope(locked))
            {
                if (GUI.Button(deleteRect, new GUIContent("x", "Delete this part and its children."), _partsRowIconStyle))
                    DeletePartsSubtree(id);
            }

            string eyeTip = Event.current.alt
                ? "Solo: show only this part (Alt+click)"
                : (slot.Enabled
                    ? "Hide part (Alt+click = solo)"
                    : "Show part (Alt+click = solo)");
            var eyePrev = GUI.color;
            if (!slot.Enabled || hiddenByAncestor)
                GUI.color = new Color(1f, 0.55f, 0.5f, 1f);
            if (GUI.Button(eyeRect, new GUIContent(slot.Enabled ? "O" : "-", eyeTip), _partsRowIconStyle))
            {
                if (Event.current.alt)
                    SoloPartsVisibility(id);
                else
                    TogglePartsVisibility(id, !slot.Enabled);
            }
            GUI.color = eyePrev;

            string lockTip = lockedByAncestor
                ? "Locked by parent"
                : (slot.EditorLocked
                    ? "Unlock (allows Move/Rotate/Scale)"
                    : "Lock (blocks transform + reparent)");
            var lockPrev = GUI.color;
            if (slot.EditorLocked || lockedByAncestor)
                GUI.color = new Color(1f, 0.82f, 0.35f, 1f);
            using (new EditorGUI.DisabledScope(lockedByAncestor))
            {
                if (GUI.Button(lockRect,
                        new GUIContent(slot.EditorLocked || lockedByAncestor ? "#" : "=", lockTip),
                        _partsRowIconStyle))
                    TogglePartsLock(id, !slot.EditorLocked);
            }
            GUI.color = lockPrev;

            if (!string.IsNullOrEmpty(_partsTreeDropReason) &&
                _partsTreeDragActive && _partsTreeDropRelativeId == id)
            {
                var tip = new Rect(row.x + 8f, row.yMax - 1f, row.width - 16f, 14f);
                // reason shown via status; keep row clean
            }

            DrawPartsTreeDropCue(row, depth, id);
            if (_partsTreeDragActive)
                EditorGUIUtility.AddCursorRect(row, MouseCursor.MoveArrow);
            HandlePartsTreeRowEvents(row, slot, id, nameRect, chevronRect, eyeRect, lockRect, deleteRect);
        }

        void DrawPartsGroupRow(SpritePartsGroupDef group, int depth)
        {
            if (group == null) return;
            string gid = SpritePartsAuthoringOps.CanonicalGroupId(group.GroupId);
            bool selected = !IsPartsIsolating() && _partsActiveGroupId == gid;
            bool expanded = !_partsCollapsedGroupIds.Contains(gid);
            bool renaming = _partsRenameGroupId == gid;
            var members = SpritePartsAuthoringOps.GetGroupMembers(_profile, gid);

            var row = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            Color bg = selected
                ? new Color(0.28f, 0.38f, 0.55f, 0.55f)
                : (row.Contains(Event.current.mousePosition)
                    ? new Color(1f, 1f, 1f, 0.06f)
                    : Color.clear);
            if (bg.a > 0f) EditorGUI.DrawRect(row, bg);

            float x = row.x + 4f + depth * 14f;
            var chevronRect = new Rect(x, row.y + 2f, 16f, 18f);
            if (GUI.Button(chevronRect, expanded ? "v" : ">", EditorStyles.miniLabel))
            {
                if (expanded) _partsCollapsedGroupIds.Add(gid);
                else _partsCollapsedGroupIds.Remove(gid);
            }
            x += 16f;

            var thumb = new Rect(x, row.y + 3f, 16f, 16f);
            EditorGUI.DrawRect(thumb, new Color(0.22f, 0.36f, 0.48f, 1f));
            GUI.Label(thumb, "G", EditorStyles.miniLabel);
            x += 20f;

            float iconsW = 58f;
            var nameRect = new Rect(x, row.y + 1f, Mathf.Max(40f, row.xMax - iconsW - x - 4f), 20f);
            if (renaming)
            {
                GUI.SetNextControlName(PartsSlotRenameControl);
                _partsRenameDraft = GUI.TextField(nameRect, _partsRenameDraft ?? string.Empty);
                if (_partsRenameFocus)
                {
                    EditorGUI.FocusTextInControl(PartsSlotRenameControl);
                    _partsRenameFocus = false;
                }
            }
            else
            {
                string label = (group.Name ?? "Group") + "  (" + members.Count + ")";
                if (group.EditorLocked)
                    label = "# " + label;
                if (!group.Enabled)
                    GUI.contentColor = new Color(1f, 1f, 1f, 0.45f);
                else if (group.EditorLocked)
                    GUI.contentColor = new Color(1f, 0.85f, 0.55f, 0.95f);
                GUI.Label(nameRect, new GUIContent(label, "Sibling group. Click to select all members."));
                GUI.contentColor = Color.white;
            }

            EnsurePartsRowIconStyles();
            var eyeRect = new Rect(row.xMax - 36f, row.y + 2f, 18f, 18f);
            var lockRect = new Rect(row.xMax - 18f, row.y + 2f, 18f, 18f);

            var eyePrev = GUI.color;
            if (!group.Enabled)
                GUI.color = new Color(1f, 0.55f, 0.5f, 1f);
            if (GUI.Button(eyeRect,
                    new GUIContent(group.Enabled ? "O" : "-",
                        group.Enabled ? "Hide group members" : "Show group members"),
                    _partsRowIconStyle))
                TogglePartsGroupVisibility(gid, !group.Enabled);
            GUI.color = eyePrev;

            var lockPrev = GUI.color;
            if (group.EditorLocked)
                GUI.color = new Color(1f, 0.82f, 0.35f, 1f);
            if (GUI.Button(lockRect,
                    new GUIContent(group.EditorLocked ? "#" : "=",
                        group.EditorLocked ? "Unlock group members" : "Lock group members"),
                    _partsRowIconStyle))
                TogglePartsGroupLock(gid, !group.EditorLocked);
            GUI.color = lockPrev;

            HandlePartsGroupRowEvents(row, group, gid, nameRect, chevronRect, eyeRect, lockRect);
        }

        void HandlePartsGroupRowEvents(
            Rect row, SpritePartsGroupDef group, string gid,
            Rect nameRect, Rect chevronRect, Rect eyeRect, Rect lockRect)
        {
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && row.Contains(evt.mousePosition))
            {
                if (chevronRect.Contains(evt.mousePosition) ||
                    eyeRect.Contains(evt.mousePosition) ||
                    lockRect.Contains(evt.mousePosition))
                    return;
                SelectPartsGroup(gid);
                _partsBrowserFocus = PartsBrowserFocus.Tree;
                if (evt.clickCount == 2 && nameRect.Contains(evt.mousePosition))
                {
                    BeginPartsGroupRename(group);
                    evt.Use();
                    return;
                }
                evt.Use();
                Repaint();
            }
            else if (evt.type == EventType.ContextClick && row.Contains(evt.mousePosition))
            {
                SelectPartsGroup(gid);
                ShowPartsGroupContextMenu(group);
                evt.Use();
            }
        }

        void DrawPartsTreeDropCue(Rect row, int depth, string id)
        {
            if (!_partsTreeDragActive || _partsTreeDropRelativeId != id)
                return;
            bool ok = string.IsNullOrEmpty(_partsTreeDropReason);
            var line = ok
                ? new Color(0.30f, 0.55f, 1f, 1f)
                : new Color(0.90f, 0.28f, 0.24f, 1f);
            var fill = ok
                ? new Color(0.30f, 0.55f, 1f, 0.28f)
                : new Color(0.90f, 0.28f, 0.24f, 0.32f);
            var kind = _partsTreeDropKind;
            if (kind == SpritePartsAuthoringOps.TreeDropKind.ParentUnder ||
                kind == SpritePartsAuthoringOps.TreeDropKind.MoveToRoot)
            {
                EditorGUI.DrawRect(row, fill);
                DrawBorder(row, line, 1f);
                return;
            }
            if (kind != SpritePartsAuthoringOps.TreeDropKind.InsertBefore &&
                kind != SpritePartsAuthoringOps.TreeDropKind.InsertAfter)
                return;
            float y = kind == SpritePartsAuthoringOps.TreeDropKind.InsertBefore
                ? row.y
                : row.yMax - 2f;
            float x = row.x + 6f + depth * 14f;
            EditorGUI.DrawRect(new Rect(x, y, Mathf.Max(8f, row.xMax - x - 4f), 2f), line);
            EditorGUI.DrawRect(new Rect(x - 3f, y - 2f, 6f, 6f), line);
        }

        void HandlePartsTreeRowEvents(
            Rect row, SpritePartSlotDef slot, string id,
            Rect nameRect, Rect chevronRect, Rect eyeRect, Rect lockRect, Rect deleteRect)
        {
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && row.Contains(evt.mousePosition))
            {
                if (chevronRect.Contains(evt.mousePosition) ||
                    eyeRect.Contains(evt.mousePosition) ||
                    lockRect.Contains(evt.mousePosition) ||
                    deleteRect.Contains(evt.mousePosition))
                    return;

                bool additive = evt.control || evt.command;
                bool range = evt.shift;
                SelectPartsTreeSlot(slot, additive, range);
                _partsBrowserFocus = PartsBrowserFocus.Tree;

                if (evt.clickCount == 2 && nameRect.Contains(evt.mousePosition))
                {
                    BeginPartsRename(slot);
                    evt.Use();
                    return;
                }

                if (!slot.EditorLocked &&
                    !SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, slot.SlotId) &&
                    string.IsNullOrEmpty(_partsRenameSlotId))
                {
                    _partsTreeDragStarted = true;
                    _partsTreeDragActive = false;
                    _partsTreeDragStartMouse = evt.mousePosition;
                    _partsTreeDragSlotId = id;
                    GUIUtility.hotControl = _partsTreeDragControlId;
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

        void HandlePartsTreeRootDrop(Rect rect, bool isCharacterHeader)
        {
            if (!_partsTreeDragActive && !_partsTreeDragStarted) return;
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;
            if (_partsTreeDragActive)
            {
                _partsTreeDropKind = SpritePartsAuthoringOps.TreeDropKind.MoveToRoot;
                _partsTreeDropRelativeId = isCharacterHeader ? "__character__" : string.Empty;
                var v = SpritePartsAuthoringOps.ValidateTreeMove(
                    _profile, _partsTreeDragSlotId,
                    SpritePartsAuthoringOps.TreeDropKind.MoveToRoot, string.Empty);
                _partsTreeDropReason = v.Ok ? null : v.Reason;
            }
            if (evt.type == EventType.Repaint && _partsTreeDragActive)
            {
                bool ok = string.IsNullOrEmpty(_partsTreeDropReason);
                EditorGUI.DrawRect(rect, ok
                    ? new Color(0.30f, 0.55f, 1f, 0.28f)
                    : new Color(0.90f, 0.28f, 0.24f, 0.32f));
                DrawBorder(rect, ok
                    ? new Color(0.30f, 0.55f, 1f, 1f)
                    : new Color(0.90f, 0.28f, 0.24f, 1f), 1f);
            }
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
            bool expandedKids = _partsExpandedSlotIds.Contains(id) &&
                SpritePartsAuthoringOps.GetChildrenSorted(_profile, id).Count > 0;
            SpritePartsAuthoringOps.TreeDropKind kind;
            if (y < h * 0.28f)
                kind = SpritePartsAuthoringOps.TreeDropKind.InsertBefore;
            else if (y > h * 0.72f && !expandedKids)
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
                    _partsTreeDragActive = true;
                    GUIUtility.hotControl = _partsTreeDragControlId;
                    evt.Use();
                    Repaint();
                }
            }

            bool ours = GUIUtility.hotControl == _partsTreeDragControlId ||
                        _partsTreeDragActive || _partsTreeDragStarted;

            if (ours && evt.type == EventType.MouseDrag && evt.button == 0)
            {
                evt.Use();
                Repaint();
            }

            if (_partsTreeDragActive && evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                if (GUIUtility.hotControl == _partsTreeDragControlId)
                    GUIUtility.hotControl = 0;
                CancelPartsTreeDrag();
                evt.Use();
                return;
            }

            if (ours && evt.type == EventType.MouseUp && evt.button == 0)
            {
                if (GUIUtility.hotControl == _partsTreeDragControlId)
                    GUIUtility.hotControl = 0;
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
            if (kind == SpritePartsAuthoringOps.TreeDropKind.MoveToRoot ||
                relative == "__character__")
            {
                kind = SpritePartsAuthoringOps.TreeDropKind.MoveToRoot;
                relative = string.Empty;
            }
            var preview = SpritePartsAuthoringOps.ValidateTreeMove(_profile, moving, kind, relative);
            if (!preview.Ok)
            {
                _status = preview.Reason;
                return;
            }
            RecordPartsUndo("Reparent Parts");
            var result = SpritePartsAuthoringOps.TryCommitTreeMove(
                _profile, moving, kind, relative, confirmAnimationReview: true);
            if (!result.Ok)
            {
                _status = result.Reason ?? "Reparent failed.";
                return;
            }
            SaveDirty();
            if (kind == SpritePartsAuthoringOps.TreeDropKind.ParentUnder)
                _status = "Parented under " + relative;
            else if (kind == SpritePartsAuthoringOps.TreeDropKind.MoveToRoot)
                _status = "Moved to root";
            else
                _status = "Reordered";
            SelectPartsSlotId(moving, false, false);
        }

        void HandlePartsTreeRenameHotkeys()
        {
            if (!string.IsNullOrEmpty(_partsRenameSlotId) ||
                !string.IsNullOrEmpty(_partsRenameGroupId)) return;
            var evt = Event.current;
            if (evt.type != EventType.KeyDown) return;
            if (EditorGUIUtility.editingTextField) return;
            if (evt.keyCode == KeyCode.F2 &&
                _partsBrowserFocus == PartsBrowserFocus.Tree)
            {
                if (!IsPartsIsolating() &&
                    !string.IsNullOrEmpty(_partsActiveGroupId))
                {
                    var group = SpritePartsAuthoringOps.FindGroup(_profile, _partsActiveGroupId);
                    if (group != null) BeginPartsGroupRename(group);
                    evt.Use();
                    return;
                }
                if (_partsSelectedSlotIds.Count == 1)
                {
                    var slot = SpritePartsAuthoringOps.FindSlot(_profile, PrimarySelectedPartsSlotId());
                    if (slot != null) BeginPartsRename(slot);
                    evt.Use();
                }
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
            // Rename is metadata - allowed in all modes.
            CancelPartsClipRename();
            _partsRenameGroupId = null;
            _partsBrowserFocus = PartsBrowserFocus.Tree;
            _partsRenameSlotId = SpritePartIdUtility.Canonical(slot.SlotId);
            _partsRenameDraft = slot.Name;
            _partsRenameFocus = true;
            Repaint();
        }

        void BeginPartsGroupRename(SpritePartsGroupDef group)
        {
            if (group == null) return;
            CancelPartsClipRename();
            _partsRenameSlotId = null;
            _partsBrowserFocus = PartsBrowserFocus.Tree;
            _partsRenameGroupId = SpritePartsAuthoringOps.CanonicalGroupId(group.GroupId);
            _partsRenameDraft = group.Name;
            _partsRenameFocus = true;
            Repaint();
        }

        void CommitPartsRename()
        {
            if (!string.IsNullOrEmpty(_partsRenameGroupId))
            {
                CommitPartsGroupRename();
                return;
            }
            if (string.IsNullOrEmpty(_partsRenameSlotId)) return;
            string id = _partsRenameSlotId;
            string draft = (_partsRenameDraft ?? string.Empty).Trim();
            _partsRenameSlotId = null;
            _partsRenameDraft = null;
            _partsRenameFocus = false;
            GUIUtility.keyboardControl = 0;
            GUI.FocusControl(null);
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, id);
            if (slot != null && slot.Name == draft)
                return;
            RecordPartsUndo("Rename Parts Slot");
            var result = SpritePartsAuthoringOps.TryRenameDisplayName(_profile, id, draft);
            if (!result.Ok)
                _status = result.Reason;
            else
            {
                SaveDirty();
                _status = "Renamed part";
            }
            Repaint();
        }

        void CommitPartsGroupRename()
        {
            if (string.IsNullOrEmpty(_partsRenameGroupId)) return;
            string id = _partsRenameGroupId;
            string draft = (_partsRenameDraft ?? string.Empty).Trim();
            _partsRenameGroupId = null;
            _partsRenameDraft = null;
            _partsRenameFocus = false;
            GUIUtility.keyboardControl = 0;
            GUI.FocusControl(null);
            var group = SpritePartsAuthoringOps.FindGroup(_profile, id);
            if (group != null && group.Name == draft)
                return;
            RecordPartsUndo("Rename Parts Group");
            var result = SpritePartsAuthoringOps.TryRenameGroup(_profile, id, draft);
            if (!result.Ok)
                _status = result.Reason;
            else
            {
                SaveDirty();
                _status = "Renamed group";
            }
            Repaint();
        }

        void CancelPartsRename()
        {
            _partsRenameSlotId = null;
            _partsRenameGroupId = null;
            _partsRenameDraft = null;
            _partsRenameFocus = false;
            GUIUtility.keyboardControl = 0;
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
            if (!locked && _partsMode != SpritePartsStudioMode.Skins)
                menu.AddItem(new GUIContent("Center On Root"), false, CenterSelectedPartsOnRoot);
            else
                menu.AddDisabledItem(new GUIContent("Center On Root"));

            menu.AddSeparator("");
            if (!locked)
            {
                menu.AddItem(new GUIContent("Duplicate"), false, () => DuplicatePartsSlot(id, false));
                menu.AddItem(new GUIContent("Duplicate Mirrored Horizontal"), false,
                    () => DuplicatePartsSlot(id, true, false));
                menu.AddItem(new GUIContent("Duplicate Mirrored Vertical"), false,
                    () => DuplicatePartsSlot(id, false, true));
                if (_partsMode != SpritePartsStudioMode.Skins)
                {
                    menu.AddItem(new GUIContent("Mirror Horizontal (East-West)"), false,
                        () => FlipPartsSlot(id, true, false));
                    menu.AddItem(new GUIContent("Mirror Vertical (North-South)"), false,
                        () => FlipPartsSlot(id, false, true));
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("Mirror Horizontal (not in Skins)"));
                    menu.AddDisabledItem(new GUIContent("Mirror Vertical (not in Skins)"));
                }
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Duplicate (Part is locked)"));
                menu.AddDisabledItem(new GUIContent("Duplicate Mirrored (Part is locked)"));
                menu.AddDisabledItem(new GUIContent("Mirror Horizontal (Part is locked)"));
                menu.AddDisabledItem(new GUIContent("Mirror Vertical (Part is locked)"));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Z-Order/Bring to Front"), false, () => MovePartsLayer(id, 0));
            menu.AddItem(new GUIContent("Z-Order/Bring Forward"), false, () => NudgePartsLayer(id, -1));
            menu.AddItem(new GUIContent("Z-Order/Send Backward"), false, () => NudgePartsLayer(id, +1));
            menu.AddItem(new GUIContent("Z-Order/Send to Back"), false, () =>
            {
                int n = SpritePartsAuthoringOps.GetSlotsSortedByDrawRank(_profile, true).Count;
                MovePartsLayer(id, Mathf.Max(0, n - 1));
            });

            if (!locked)
                menu.AddItem(new GUIContent("Add Child"), false, () => AddPartsChildOf(slot.SlotId));
            else
                menu.AddDisabledItem(new GUIContent("Add Child (Part is locked)"));

            if (isRig && !locked)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Break from Parent"), false, () => BreakPartsFromParent(id));
                menu.AddItem(new GUIContent("Move Up One Level"), false, () => MovePartsUpOneLevel(id));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Move to Top of Siblings"), false, () =>
                {
                    RecordPartsUndo("Move Parts Sibling To Top");
                    var r = SpritePartsAuthoringOps.TryMoveSiblingToTop(_profile, id);
                    if (!r.Ok) _status = r.Reason;
                    else { SaveDirty(); _status = "Moved to top of siblings"; }
                });
                menu.AddItem(new GUIContent("Move to Bottom of Siblings"), false, () =>
                {
                    RecordPartsUndo("Move Parts Sibling To Bottom");
                    var r = SpritePartsAuthoringOps.TryMoveSiblingToBottom(_profile, id);
                    if (!r.Ok) _status = r.Reason;
                    else { SaveDirty(); _status = "Moved to bottom of siblings"; }
                });
            }
            else if (!isRig)
            {
                menu.AddDisabledItem(new GUIContent("Break from Parent (Switch to Rig to reparent)"));
                menu.AddDisabledItem(new GUIContent("Move Up One Level (Switch to Rig to reparent)"));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Break from Parent (Part is locked)"));
                menu.AddDisabledItem(new GUIContent("Move Up One Level (Part is locked)"));
            }

            menu.AddSeparator("");
            AddPartsGroupMenuItems(menu, slot);
            menu.AddSeparator("");
            string deleteReason = null;
            var v = SpritePartsAuthoringOps.ValidateDeleteSubtree(_profile, id);
            if (!v.Ok) deleteReason = v.Reason;
            if (deleteReason == null)
                menu.AddItem(new GUIContent("Delete Subtree"), false, () => DeletePartsSubtree(id));
            else
                menu.AddDisabledItem(new GUIContent("Delete Subtree (" + deleteReason + ")"));

            menu.ShowAsContext();
        }

        void AddPartsGroupMenuItems(GenericMenu menu, SpritePartSlotDef slot)
        {
            bool canGroup = _partsSelectedSlotIds.Count >= 2;
            if (canGroup)
                menu.AddItem(new GUIContent("Group (Ctrl+G)"), false, GroupSelectedParts);
            else
                menu.AddDisabledItem(new GUIContent("Group (select 2+ siblings)"));

            string gid = slot != null
                ? SpritePartsAuthoringOps.SlotGroupId(slot)
                : _partsActiveGroupId;
            bool grouped = SpritePartsAuthoringOps.FindGroup(_profile, gid) != null;
            if (grouped)
            {
                menu.AddItem(new GUIContent("Ungroup (Ctrl+Shift+G)"), false, UngroupSelectedParts);
                if (IsPartsIsolating())
                    menu.AddItem(new GUIContent("Select Group"), false, () => SelectPartsGroup(gid));
            }
            else
                menu.AddDisabledItem(new GUIContent("Ungroup (not grouped)"));
        }

        void ShowPartsGroupContextMenu(SpritePartsGroupDef group)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Rename"), false, () => BeginPartsGroupRename(group));
            menu.AddItem(new GUIContent("Ungroup (Ctrl+Shift+G)"), false, UngroupSelectedParts);
            menu.AddSeparator("");
            if (_partsMode == SpritePartsStudioMode.Skins)
            {
                menu.AddDisabledItem(new GUIContent("Center On Root (not in Skins)"));
                menu.AddDisabledItem(new GUIContent("Align To Bounds (not in Skins)"));
            }
            else
            {
                menu.AddItem(new GUIContent("Center On Root"), false, CenterSelectedPartsOnRoot);
                AddPartsAlignToBoundsMenuItems(menu);
            }
            if (_partsMode == SpritePartsStudioMode.Skins)
                menu.AddDisabledItem(new GUIContent("Sync Other Keys With Playhead Offset"));
            else
                menu.AddItem(new GUIContent("Sync Other Keys With Playhead Offset"), false,
                    PropagatePlayheadOffsetToAllKeys);
            menu.ShowAsContext();
        }

        void GroupSelectedParts()
        {
            EnsurePartsTreeSelectionSynced();
            if (_partsSelectedSlotIds.Count < 2)
            {
                _status = "Select 2 or more sibling parts.";
                return;
            }
            RecordPartsUndo("Group Parts");
            var result = SpritePartsAuthoringOps.TryGroupSiblings(
                _profile, _partsSelectedSlotIds, out var created);
            if (!result.Ok)
            {
                _status = result.Reason;
                return;
            }
            SaveDirty();
            _partsCollapsedGroupIds.Remove(created.GroupId);
            SelectPartsGroup(created.GroupId);
            BeginPartsGroupRename(created);
            _status = "Grouped as " + created.Name + " (siblings, not a parent joint)";
        }

        void UngroupSelectedParts()
        {
            EnsurePartsTreeSelectionSynced();
            string gid = _partsActiveGroupId;
            if (string.IsNullOrEmpty(gid))
            {
                var primary = CurrentPartsSlot;
                if (primary != null)
                    gid = SpritePartsAuthoringOps.SlotGroupId(primary);
            }
            RecordPartsUndo("Ungroup Parts");
            var result = !string.IsNullOrEmpty(gid)
                ? SpritePartsAuthoringOps.TryUngroup(_profile, gid)
                : SpritePartsAuthoringOps.TryUngroupSlots(_profile, _partsSelectedSlotIds);
            if (!result.Ok)
            {
                _status = result.Reason;
                return;
            }
            SaveDirty();
            _partsActiveGroupId = null;
            _partsIsolatedSlotId = null;
            _status = "Ungrouped (keys unchanged)";
            Repaint();
        }

        void TogglePartsGroupVisibility(string groupId, bool enabled)
        {
            var group = SpritePartsAuthoringOps.FindGroup(_profile, groupId);
            if (group == null || group.Enabled == enabled) return;
            RecordPartsUndo(enabled ? "Show Parts Group" : "Hide Parts Group");
            group.Enabled = enabled;
            SaveDirty();
            _status = (enabled ? "Shown " : "Hidden ") + (group.Name ?? group.GroupId);
            Repaint();
        }

        void TogglePartsGroupLock(string groupId, bool locked)
        {
            var group = SpritePartsAuthoringOps.FindGroup(_profile, groupId);
            if (group == null || group.EditorLocked == locked) return;
            RecordPartsUndo(locked ? "Lock Parts Group" : "Unlock Parts Group");
            group.EditorLocked = locked;
            SaveDirty();
            _status = (locked ? "Locked " : "Unlocked ") + (group.Name ?? group.GroupId);
            Repaint();
        }

        void DrawPartsLayers()
        {
            GUILayout.Space(8f);
            GUILayout.Label("LAYERS", _sectionStyle);
            GUILayout.Label("Front on top. Drag to change z-order (not hierarchy).", _mutedStyle);
            _partsLayerDragControlId = GUIUtility.GetControlID(FocusType.Passive);
            var list = SpritePartsAuthoringOps.GetSlotsSortedByDrawRank(_profile, frontFirst: true);
            for (int i = 0; i < list.Count; i++)
                DrawPartsLayerRow(list[i], i, list.Count);
            HandlePartsLayerDragEvents(list.Count);
        }

        void DrawPartsLayerRow(SpritePartSlotDef slot, int index, int count)
        {
            if (slot == null) return;
            string id = SpritePartIdUtility.Canonical(slot.SlotId);
            var row = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
            bool selected = _partsSelectedSlotIds.Contains(id);
            var evt = Event.current;
            Color bg = selected
                ? new Color(0.22f, 0.45f, 0.75f, 0.55f)
                : (row.Contains(evt.mousePosition) ? new Color(1f, 1f, 1f, 0.06f) : Color.clear);
            if (_partsLayerDragActive && id == _partsLayerDragSlotId)
                bg = new Color(1f, 1f, 1f, 0.08f);
            if (bg.a > 0f) EditorGUI.DrawRect(row, bg);

            string caption = index == 0 ? "Front" : (index == count - 1 ? "Back" : "");
            GUI.Label(new Rect(row.x + 6f, row.y + 1f, 36f, 18f),
                slot.DrawRank.ToString(), _mutedStyle);
            GUI.Label(new Rect(row.x + 28f, row.y + 1f, row.width - 90f, 18f),
                string.IsNullOrEmpty(caption) ? slot.Name : slot.Name + "  (" + caption + ")");
            var up = new Rect(row.xMax - 40f, row.y + 1f, 18f, 18f);
            var down = new Rect(row.xMax - 20f, row.y + 1f, 18f, 18f);
            if (GUI.Button(up, new GUIContent("\u25b2", "Bring forward"), EditorStyles.miniLabel) && index > 0)
                MovePartsLayer(id, index - 1);
            if (GUI.Button(down, new GUIContent("\u25bc", "Send backward"), EditorStyles.miniLabel) && index < count - 1)
                MovePartsLayer(id, index + 1);

            if (_partsLayerDragActive && _partsLayerDropIndex >= 0)
            {
                var lineCol = new Color(0.30f, 0.55f, 1f, 1f);
                if (_partsLayerDropIndex == index)
                    EditorGUI.DrawRect(new Rect(row.x + 4f, row.y, row.width - 8f, 2f), lineCol);
                else if (_partsLayerDropIndex == index + 1 && index == count - 1)
                    EditorGUI.DrawRect(new Rect(row.x + 4f, row.yMax - 2f, row.width - 8f, 2f), lineCol);
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && row.Contains(evt.mousePosition) &&
                !up.Contains(evt.mousePosition) && !down.Contains(evt.mousePosition))
            {
                SelectPartsSlotId(id, false, false);
                _partsBrowserFocus = PartsBrowserFocus.Tree;
                _partsLayerDragStarted = true;
                _partsLayerDragActive = false;
                _partsLayerDragStartMouse = evt.mousePosition;
                _partsLayerDragSlotId = id;
                GUIUtility.hotControl = _partsLayerDragControlId;
                evt.Use();
                Repaint();
            }
            if (_partsLayerDragActive && row.Contains(evt.mousePosition))
            {
                _partsLayerDropIndex = (evt.mousePosition.y - row.y) < row.height * 0.5f ? index : index + 1;
                EditorGUIUtility.AddCursorRect(row, MouseCursor.MoveArrow);
            }
        }

        void HandlePartsLayerDragEvents(int count)
        {
            var evt = Event.current;
            bool ours = GUIUtility.hotControl == _partsLayerDragControlId ||
                        _partsLayerDragActive || _partsLayerDragStarted;
            if (!ours) return;
            if (evt.type == EventType.MouseDrag && evt.button == 0)
            {
                if (_partsLayerDragStarted && !_partsLayerDragActive &&
                    (evt.mousePosition - _partsLayerDragStartMouse).magnitude >= PartsTreeDragThreshold)
                    _partsLayerDragActive = true;
                evt.Use();
                Repaint();
            }
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                if (GUIUtility.hotControl == _partsLayerDragControlId)
                    GUIUtility.hotControl = 0;
                CancelPartsLayerDrag();
                evt.Use();
                return;
            }
            if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                if (GUIUtility.hotControl == _partsLayerDragControlId)
                    GUIUtility.hotControl = 0;
                if (_partsLayerDragActive && !string.IsNullOrEmpty(_partsLayerDragSlotId) &&
                    _partsLayerDropIndex >= 0)
                {
                    var list = SpritePartsAuthoringOps.GetSlotsSortedByDrawRank(_profile, true);
                    int from = list.FindIndex(s => s != null &&
                        SpritePartIdUtility.Canonical(s.SlotId) == _partsLayerDragSlotId);
                    int dest = _partsLayerDropIndex;
                    if (from >= 0 && from < dest) dest--;
                    dest = Mathf.Clamp(dest, 0, Mathf.Max(0, count - 1));
                    if (from >= 0 && dest != from)
                        MovePartsLayer(_partsLayerDragSlotId, dest);
                }
                CancelPartsLayerDrag();
                evt.Use();
            }
        }

        void CancelPartsLayerDrag()
        {
            _partsLayerDragActive = false;
            _partsLayerDragStarted = false;
            _partsLayerDragSlotId = null;
            _partsLayerDropIndex = -1;
            Repaint();
        }

        void MovePartsLayer(string slotId, int frontIndex)
        {
            RecordPartsUndo("Change Parts Z-Order");
            var r = SpritePartsAuthoringOps.TryMoveDrawRankToFrontIndex(_profile, slotId, frontIndex);
            if (!r.Ok) _status = r.Reason;
            else
            {
                SaveDirty();
                _status = "Z-order updated";
            }
            GUI.FocusControl(null);
            Repaint();
        }

        void NudgePartsLayer(string slotId, int deltaFront)
        {
            var list = SpritePartsAuthoringOps.GetSlotsSortedByDrawRank(_profile, true);
            int from = list.FindIndex(s => s != null &&
                SpritePartIdUtility.Canonical(s.SlotId) == SpritePartIdUtility.Canonical(slotId));
            if (from < 0) return;
            MovePartsLayer(slotId, Mathf.Clamp(from + deltaFront, 0, list.Count - 1));
        }

        void DrawPartsTreeToolbar()
        {
            EnsurePartsRowIconStyles();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Part", GUILayout.Width(60f)))
                AddPartsSlot();
            if (GUILayout.Button(new GUIContent("+ Bone", "A joint with no image, under the selected part (or at the root). Parts under it follow it."),
                    GUILayout.Width(56f)))
                AddPartsBone();
            if (GUILayout.Button(new GUIContent("+ Clip", "A clip shape (Spine clipping): an invisible polygon that clips the selected part (and, with End, the parts above it)."),
                    GUILayout.Width(48f)))
                AddPartsClipShape();
            if (GUILayout.Button(new GUIContent("PSD", "Import a Photoshop file: every layer becomes a part where it sits, groups become bones."),
                    GUILayout.Width(40f)))
                OpenPsdImport();
            using (new EditorGUI.DisabledScope(CurrentPartsSlot == null))
            {
                if (GUILayout.Button(new GUIContent("Dup", "Duplicate selected part + children. Ctrl+D / Ctrl+Shift+D mirror."),
                        GUILayout.Width(40f)))
                {
                    var sel = CurrentPartsSlot;
                    if (sel != null)
                        DuplicatePartsSlot(SpritePartIdUtility.Canonical(sel.SlotId), Event.current.shift, false);
                }
            }
            if (GUILayout.Button("v", GUILayout.Width(22f)))
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

            GUILayout.FlexibleSpace();
            var primary = CurrentPartsSlot;
            using (new EditorGUI.DisabledScope(primary == null))
            {
                bool vis = primary == null || primary.Enabled;
                if (GUILayout.Button(new GUIContent(vis ? "O" : "-",
                        "Toggle visibility of selection. Alt+click a row eye to solo."),
                        _partsRowIconStyle, GUILayout.Width(22f), GUILayout.Height(18f)))
                {
                    if (primary != null)
                        TogglePartsVisibility(SpritePartIdUtility.Canonical(primary.SlotId), !primary.Enabled);
                }
                bool loc = primary != null && primary.EditorLocked;
                if (GUILayout.Button(new GUIContent(loc ? "#" : "=",
                        "Toggle lock on selection (blocks Move/Rotate/Scale)."),
                        _partsRowIconStyle, GUILayout.Width(22f), GUILayout.Height(18f)))
                {
                    if (primary != null)
                        TogglePartsLock(SpritePartIdUtility.Canonical(primary.SlotId), !primary.EditorLocked);
                }
            }
            if (GUILayout.Button(new GUIContent("All", "Show every part (clear hides)."),
                    EditorStyles.miniButton, GUILayout.Width(32f)))
                ShowAllPartsVisibility();
            EditorGUILayout.EndHorizontal();
        }

        void TogglePartsVisibility(string slotId, bool enabled)
        {
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, slotId);
            if (slot == null) return;
            if (slot.Enabled == enabled) return;
            RecordPartsUndo(enabled ? "Show Parts Slot" : "Hide Parts Slot");
            slot.Enabled = enabled;
            SaveDirty();
            _status = (enabled ? "Shown " : "Hidden ") + (slot.Name ?? slot.SlotId);
            Repaint();
        }

        void TogglePartsLock(string slotId, bool locked)
        {
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, slotId);
            if (slot == null) return;
            if (SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, slot.SlotId) && !slot.EditorLocked)
            {
                _status = "Locked by parent.";
                return;
            }
            if (slot.EditorLocked == locked) return;
            RecordPartsUndo(locked ? "Lock Parts Slot" : "Unlock Parts Slot");
            slot.EditorLocked = locked;
            SaveDirty();
            _status = (locked ? "Locked " : "Unlocked ") + (slot.Name ?? slot.SlotId);
            Repaint();
        }

        void SoloPartsVisibility(string slotId)
        {
            if (_profile?.PartsSlots == null) return;
            string id = SpritePartIdUtility.Canonical(slotId);
            RecordPartsUndo("Solo Parts Visibility");
            for (int i = 0; i < _profile.PartsSlots.Count; i++)
            {
                var s = _profile.PartsSlots[i];
                if (s == null) continue;
                s.Enabled = SpritePartIdUtility.Canonical(s.SlotId) == id;
            }
            SaveDirty();
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, id);
            _status = "Solo " + (slot?.Name ?? id);
            Repaint();
        }

        void ShowAllPartsVisibility()
        {
            if (_profile?.PartsSlots == null) return;
            RecordPartsUndo("Show All Parts");
            for (int i = 0; i < _profile.PartsSlots.Count; i++)
            {
                var s = _profile.PartsSlots[i];
                if (s != null) s.Enabled = true;
            }
            if (_profile.PartsGroups != null)
            {
                for (int i = 0; i < _profile.PartsGroups.Count; i++)
                {
                    var g = _profile.PartsGroups[i];
                    if (g != null) g.Enabled = true;
                }
            }
            SaveDirty();
            _status = "Shown all parts";
            Repaint();
        }

        void AddPartsSlot()
        {
            RecordPartsUndo("Add Parts Slot");
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
            RecordPartsUndo("Add Child Parts Slot");
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
            if (!string.IsNullOrEmpty(_partsRenameSlotId) ||
                !string.IsNullOrEmpty(_partsRenameGroupId)) return;
            if (EditorGUIUtility.editingTextField) return;
            var evt = Event.current;
            if (evt.type != EventType.KeyDown) return;
            if (evt.keyCode != KeyCode.Delete && evt.keyCode != KeyCode.Backspace) return;
            if (_partsSelectedKeys.Count > 0)
            {
                DeleteSelectedPartsKeys();
                evt.Use();
                Repaint();
                return;
            }
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
            if (!string.IsNullOrEmpty(_partsRenameSlotId) ||
                !string.IsNullOrEmpty(_partsRenameGroupId))
            {
                _status = "Finish renaming before delete.";
                return false;
            }
            if (!IsPartsIsolating() && !string.IsNullOrEmpty(_partsActiveGroupId))
            {
                _status = "Ungroup first (Ctrl+Shift+G), or isolate a part to delete.";
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
            RecordPartsUndo("Break Parts From Parent");
            var result = SpritePartsAuthoringOps.TryBreakFromParent(
                _profile, slotId, confirmAnimationReview: true);
            if (!result.Ok)
            {
                _status = result.Reason ?? "Break from parent failed.";
                return;
            }
            SaveDirty();
            _status = "Broke from parent -> Character root";
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
            RecordPartsUndo("Move Parts Up One Level");
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
        void DuplicatePartsSlot(string slotId, bool mirrorHorizontal, bool mirrorVertical = false)
        {
            string undo = mirrorHorizontal ? "Duplicate Parts Mirrored Horizontal"
                : mirrorVertical ? "Duplicate Parts Mirrored Vertical"
                : "Duplicate Parts Slot";
            RecordPartsUndo(undo);
            var result = SpritePartsAuthoringOps.TryDuplicatePart(
                _profile, slotId, out var created, mirrorHorizontal, mirrorVertical);
            if (!result.Ok || created == null)
            {
                _status = result.Reason ?? "Duplicate failed";
                return;
            }
            SaveDirty();
            SelectPartsSlotId(created.SlotId, false, false);
            _status = (mirrorHorizontal ? "Mirrored H " : mirrorVertical ? "Mirrored V " : "Duplicated ") +
                      (created.Name ?? created.SlotId) +
                      (result.DeletedSlotCount > 1 ? (" +" + (result.DeletedSlotCount - 1) + " children") : "");
            Repaint();
        }

        void FlipPartsSlot(string slotId, bool flipX, bool flipY)
        {
            RecordPartsUndo(flipX && flipY ? "Mirror Parts Both"
                : flipX ? "Mirror Parts Horizontal" : "Mirror Parts Vertical");
            var result = SpritePartsAuthoringOps.TryFlipPart(_profile, slotId, flipX, flipY);
            if (!result.Ok)
            {
                _status = result.Reason ?? "Flip failed";
                return;
            }
            SaveDirty();
            string axis = flipX && flipY ? "H+V" : flipX ? "Horizontal" : "Vertical";
            var slot = SpritePartsAuthoringOps.FindSlot(_profile, slotId);
            _status = "Mirrored " + axis + " " + (slot?.Name ?? slotId) +
                      (result.AffectedClipCount > 0
                          ? $" ({result.AffectedClipCount} clip tracks)"
                          : " (rest only)");
            Repaint();
        }

        void DeletePartsSubtree(string slotId)
        {
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

            RecordPartsUndo("Delete Parts Subtree");
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

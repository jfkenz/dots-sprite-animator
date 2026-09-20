using System;
using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Sibling selection groups. Not motion parents and not baked.</summary>
    public static partial class SpritePartsAuthoringOps
    {
        public static string CanonicalGroupId(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
                return string.Empty;
            return SpritePartIdUtility.Canonical(groupId, "group");
        }

        public static SpritePartsGroupDef FindGroup(SpriteSheetProfile profile, string groupId)
        {
            string id = CanonicalGroupId(groupId);
            if (profile?.PartsGroups == null || string.IsNullOrEmpty(id))
                return null;
            for (int i = 0; i < profile.PartsGroups.Count; i++)
            {
                var g = profile.PartsGroups[i];
                if (g != null && CanonicalGroupId(g.GroupId) == id)
                    return g;
            }
            return null;
        }

        public static bool IsPartsGroupHidden(SpriteSheetProfile profile, string groupId)
        {
            var g = FindGroup(profile, groupId);
            return g != null && !g.Enabled;
        }

        public static bool IsPartsGroupLocked(SpriteSheetProfile profile, string groupId)
        {
            var g = FindGroup(profile, groupId);
            return g != null && g.EditorLocked;
        }

        public static List<SpritePartSlotDef> GetGroupMembers(
            SpriteSheetProfile profile, string groupId)
        {
            var list = new List<SpritePartSlotDef>();
            string id = CanonicalGroupId(groupId);
            if (profile?.PartsSlots == null || string.IsNullOrEmpty(id))
                return list;
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var s = profile.PartsSlots[i];
                if (s == null) continue;
                if (CanonicalGroupId(s.GroupId) == id)
                    list.Add(s);
            }
            list.Sort((a, b) =>
            {
                int cmp = a.SiblingOrder.CompareTo(b.SiblingOrder);
                return cmp != 0 ? cmp : string.CompareOrdinal(
                    SpritePartIdUtility.Canonical(a.SlotId),
                    SpritePartIdUtility.Canonical(b.SlotId));
            });
            return list;
        }

        public static List<string> GetGroupMemberSlotIds(
            SpriteSheetProfile profile, string groupId)
        {
            var members = GetGroupMembers(profile, groupId);
            var ids = new List<string>(members.Count);
            for (int i = 0; i < members.Count; i++)
                ids.Add(SpritePartIdUtility.Canonical(members[i].SlotId));
            return ids;
        }

        public static string SlotGroupId(SpritePartSlotDef slot)
            => slot == null ? string.Empty : CanonicalGroupId(slot.GroupId);

        public static HierarchyEditResult TryRenameGroup(
            SpriteSheetProfile profile, string groupId, string newDisplayName)
        {
            var result = new HierarchyEditResult();
            var group = FindGroup(profile, groupId);
            if (group == null)
            {
                result.Reason = "Group not found.";
                return result;
            }
            string trimmed = (newDisplayName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                result.Reason = "Name cannot be empty.";
                return result;
            }
            group.Name = trimmed;
            result.Ok = true;
            return result;
        }

        /// <summary>
        /// Folder the selected slots together. Members must share one parent.
        /// Does not create a joint or rewrite keys.
        /// </summary>
        public static HierarchyEditResult TryGroupSiblings(
            SpriteSheetProfile profile,
            IEnumerable<string> slotIds,
            out SpritePartsGroupDef created)
        {
            created = null;
            var result = new HierarchyEditResult();
            if (profile == null)
            {
                result.Reason = "Profile is null.";
                return result;
            }
            profile.EnsurePartsRig();
            var unique = new List<SpritePartSlotDef>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (slotIds != null)
            {
                foreach (var raw in slotIds)
                {
                    var slot = FindSlot(profile, raw);
                    if (slot == null) continue;
                    string sid = SpritePartIdUtility.Canonical(slot.SlotId);
                    if (!seen.Add(sid)) continue;
                    unique.Add(slot);
                }
            }
            if (unique.Count < 2)
            {
                result.Reason = "Select 2 or more sibling parts.";
                return result;
            }

            string parent = string.IsNullOrWhiteSpace(unique[0].ParentSlotId)
                ? string.Empty
                : SpritePartIdUtility.Canonical(unique[0].ParentSlotId);
            for (int i = 0; i < unique.Count; i++)
            {
                var slot = unique[i];
                string p = string.IsNullOrWhiteSpace(slot.ParentSlotId)
                    ? string.Empty
                    : SpritePartIdUtility.Canonical(slot.ParentSlotId);
                if (p != parent)
                {
                    result.Reason = "Groups need the same parent. Parent them first, or pick siblings.";
                    return result;
                }
                if (slot.EditorLocked || SlotOrAncestorLocked(profile, slot.SlotId))
                {
                    result.Reason = "Part is locked.";
                    return result;
                }
            }

            var existingIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < unique.Count; i++)
            {
                string gid = CanonicalGroupId(unique[i].GroupId);
                if (!string.IsNullOrEmpty(gid) && FindGroup(profile, gid) != null)
                    existingIds.Add(gid);
            }

            SpritePartsGroupDef target = null;
            if (existingIds.Count == 1)
            {
                string only = null;
                foreach (var id in existingIds) { only = id; break; }
                target = FindGroup(profile, only);
            }

            if (target == null)
            {
                string display = UniqueGroupDisplayName(profile, "Group");
                string gid = UniqueGroupId(profile, display);
                target = new SpritePartsGroupDef
                {
                    Name = display,
                    GroupId = gid,
                    Enabled = true,
                    EditorLocked = false,
                };
                profile.PartsGroups.Add(target);
            }

            string assign = CanonicalGroupId(target.GroupId);
            for (int i = 0; i < unique.Count; i++)
                unique[i].GroupId = assign;

            PruneEmptyPartsGroups(profile);
            created = FindGroup(profile, assign) ?? target;
            result.Ok = true;
            return result;
        }

        public static HierarchyEditResult TryUngroup(
            SpriteSheetProfile profile, string groupId)
        {
            var result = new HierarchyEditResult();
            if (profile == null)
            {
                result.Reason = "Profile is null.";
                return result;
            }
            profile.EnsurePartsRig();
            string id = CanonicalGroupId(groupId);
            if (string.IsNullOrEmpty(id) || FindGroup(profile, id) == null)
            {
                result.Reason = "Group not found.";
                return result;
            }
            if (profile.PartsSlots != null)
            {
                for (int i = 0; i < profile.PartsSlots.Count; i++)
                {
                    var s = profile.PartsSlots[i];
                    if (s == null) continue;
                    if (CanonicalGroupId(s.GroupId) == id)
                        s.GroupId = string.Empty;
                }
            }
            profile.PartsGroups.RemoveAll(g =>
                g != null && CanonicalGroupId(g.GroupId) == id);
            result.Ok = true;
            return result;
        }

        public static HierarchyEditResult TryUngroupSlots(
            SpriteSheetProfile profile, IEnumerable<string> slotIds)
        {
            var result = new HierarchyEditResult();
            if (profile == null)
            {
                result.Reason = "Profile is null.";
                return result;
            }
            profile.EnsurePartsRig();
            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            if (slotIds != null)
            {
                foreach (var raw in slotIds)
                {
                    var slot = FindSlot(profile, raw);
                    if (slot == null) continue;
                    string gid = CanonicalGroupId(slot.GroupId);
                    if (!string.IsNullOrEmpty(gid) && FindGroup(profile, gid) != null)
                        groupIds.Add(gid);
                }
            }
            if (groupIds.Count == 0)
            {
                result.Reason = "Selection is not grouped.";
                return result;
            }
            foreach (var gid in groupIds)
            {
                var r = TryUngroup(profile, gid);
                if (!r.Ok)
                    return r;
            }
            result.Ok = true;
            return result;
        }

        public static void PruneEmptyPartsGroups(SpriteSheetProfile profile)
        {
            if (profile?.PartsGroups == null) return;
            profile.PartsGroups.RemoveAll(g => g == null);
            if (profile.PartsSlots == null)
            {
                profile.PartsGroups.Clear();
                return;
            }

            for (int i = profile.PartsGroups.Count - 1; i >= 0; i--)
            {
                var g = profile.PartsGroups[i];
                string gid = CanonicalGroupId(g.GroupId);
                if (string.IsNullOrEmpty(gid))
                {
                    profile.PartsGroups.RemoveAt(i);
                    continue;
                }
                string parent = null;
                int members = 0;
                bool mixedParent = false;
                for (int s = 0; s < profile.PartsSlots.Count; s++)
                {
                    var slot = profile.PartsSlots[s];
                    if (slot == null || CanonicalGroupId(slot.GroupId) != gid)
                        continue;
                    string p = string.IsNullOrWhiteSpace(slot.ParentSlotId)
                        ? string.Empty
                        : SpritePartIdUtility.Canonical(slot.ParentSlotId);
                    if (parent == null)
                        parent = p;
                    else if (parent != p)
                        mixedParent = true;
                    members++;
                }
                if (mixedParent || members < 2)
                {
                    for (int s = 0; s < profile.PartsSlots.Count; s++)
                    {
                        var slot = profile.PartsSlots[s];
                        if (slot != null && CanonicalGroupId(slot.GroupId) == gid)
                            slot.GroupId = string.Empty;
                    }
                    profile.PartsGroups.RemoveAt(i);
                }
            }
        }

        static string UniqueGroupDisplayName(SpriteSheetProfile profile, string baseName)
        {
            string stem = string.IsNullOrWhiteSpace(baseName) ? "Group" : baseName.Trim();
            if (!HasGroupDisplayName(profile, stem, null))
                return stem;
            for (int n = 2; n < 1000; n++)
            {
                string candidate = stem + " " + n;
                if (!HasGroupDisplayName(profile, candidate, null))
                    return candidate;
            }
            return stem + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        static bool HasGroupDisplayName(SpriteSheetProfile profile, string name, string exceptGroupId)
        {
            if (profile?.PartsGroups == null || string.IsNullOrWhiteSpace(name))
                return false;
            string trimmed = name.Trim();
            string except = CanonicalGroupId(exceptGroupId);
            for (int i = 0; i < profile.PartsGroups.Count; i++)
            {
                var g = profile.PartsGroups[i];
                if (g == null) continue;
                if (!string.IsNullOrEmpty(except) && CanonicalGroupId(g.GroupId) == except)
                    continue;
                if (string.Equals((g.Name ?? string.Empty).Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static string UniqueGroupId(SpriteSheetProfile profile, string desired)
        {
            string id = SpritePartIdUtility.Canonical(desired, "group");
            int suffix = 2;
            while (FindGroup(profile, id) != null)
            {
                id = SpritePartIdUtility.Canonical(desired, "group") + "." + suffix;
                suffix++;
            }
            return id;
        }
    }
}

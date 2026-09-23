using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Parts tree hierarchy edits (rename / reparent / sibling order / delete).</summary>
    public static partial class SpritePartsAuthoringOps
    {
        public struct HierarchyEditResult
        {
            public bool Ok;
            public string Reason;
            public bool NeedsAnimationReview;
            public int AffectedClipCount;
            public int DeletedSlotCount;
            public int DeletedTrackCount;
            public int DeletedBindingCount;
        }

        /// <summary>Drop destination for tree drag.</summary>
        public enum TreeDropKind : byte
        {
            None = 0,
            ParentUnder = 1,
            InsertBefore = 2,
            InsertAfter = 3,
            MoveToRoot = 4,
        }

        /// <summary>
        /// Normalize SiblingOrder within each parent group to 0..n-1.
        /// Call only from committed edits, never during repaint.
        /// Stable sort: SiblingOrder, then list index.
        /// </summary>
        public static void NormalizeSiblingOrders(SpriteSheetProfile profile)
        {
            if (profile?.PartsSlots == null) return;
            var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var slot = profile.PartsSlots[i];
                if (slot == null) continue;
                string parent = string.IsNullOrWhiteSpace(slot.ParentSlotId)
                    ? string.Empty
                    : SpritePartIdUtility.Canonical(slot.ParentSlotId);
                if (!groups.TryGetValue(parent, out var list))
                {
                    list = new List<int>();
                    groups[parent] = list;
                }
                list.Add(i);
            }

            foreach (var kv in groups)
            {
                var indices = kv.Value;
                indices.Sort((a, b) =>
                {
                    int cmp = profile.PartsSlots[a].SiblingOrder.CompareTo(profile.PartsSlots[b].SiblingOrder);
                    return cmp != 0 ? cmp : a.CompareTo(b);
                });
                for (int o = 0; o < indices.Count; o++)
                    profile.PartsSlots[indices[o]].SiblingOrder = o;
            }
        }

        /// <summary>Children of parent (empty = Character root), sorted by SiblingOrder.</summary>
        public static List<SpritePartSlotDef> GetChildrenSorted(
            SpriteSheetProfile profile, string parentSlotId)
        {
            var result = new List<SpritePartSlotDef>();
            if (profile?.PartsSlots == null) return result;
            string parent = string.IsNullOrWhiteSpace(parentSlotId)
                ? string.Empty
                : SpritePartIdUtility.Canonical(parentSlotId);
            var indexed = new List<(int index, SpritePartSlotDef slot)>();
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var slot = profile.PartsSlots[i];
                if (slot == null) continue;
                string p = string.IsNullOrWhiteSpace(slot.ParentSlotId)
                    ? string.Empty
                    : SpritePartIdUtility.Canonical(slot.ParentSlotId);
                if (p == parent)
                    indexed.Add((i, slot));
            }
            indexed.Sort((a, b) =>
            {
                int cmp = a.slot.SiblingOrder.CompareTo(b.slot.SiblingOrder);
                return cmp != 0 ? cmp : a.index.CompareTo(b.index);
            });
            for (int i = 0; i < indexed.Count; i++)
                result.Add(indexed[i].slot);
            return result;
        }

        /// <summary>Depth-first walk in sibling order (for tree UI).</summary>
        public static List<(SpritePartSlotDef slot, int depth)> BuildTreeRows(SpriteSheetProfile profile)
        {
            var rows = new List<(SpritePartSlotDef, int)>();
            if (profile?.PartsSlots == null) return rows;
            void Walk(string parentId, int depth)
            {
                var kids = GetChildrenSorted(profile, parentId);
                for (int i = 0; i < kids.Count; i++)
                {
                    var child = kids[i];
                    rows.Add((child, depth));
                    Walk(child.SlotId, depth + 1);
                }
            }
            Walk(string.Empty, 0);
            // Orphans with missing parents — show at root so they remain editable.
            var known = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var s = profile.PartsSlots[i];
                if (s != null) known.Add(SpritePartIdUtility.Canonical(s.SlotId));
            }
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var s = profile.PartsSlots[i];
                if (s == null || string.IsNullOrWhiteSpace(s.ParentSlotId)) continue;
                string p = SpritePartIdUtility.Canonical(s.ParentSlotId);
                if (!known.Contains(p))
                    rows.Add((s, 0));
            }
            return rows;
        }

        public static bool IsDescendantOf(
            SpriteSheetProfile profile, string ancestorSlotId, string candidateSlotId)
        {
            if (profile == null || string.IsNullOrWhiteSpace(ancestorSlotId) ||
                string.IsNullOrWhiteSpace(candidateSlotId))
                return false;
            string ancestor = SpritePartIdUtility.Canonical(ancestorSlotId);
            string cur = SpritePartIdUtility.Canonical(candidateSlotId);
            var guard = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrEmpty(cur) && guard.Add(cur))
            {
                if (cur == ancestor) return true;
                var slot = FindSlot(profile, cur);
                if (slot == null || string.IsNullOrWhiteSpace(slot.ParentSlotId))
                    return false;
                cur = SpritePartIdUtility.Canonical(slot.ParentSlotId);
            }
            return false;
        }

        public static bool SlotOrAncestorLocked(SpriteSheetProfile profile, string slotId)
        {
            string cur = SpritePartIdUtility.Canonical(slotId);
            var guard = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrEmpty(cur) && guard.Add(cur))
            {
                var slot = FindSlot(profile, cur);
                if (slot == null) return false;
                if (slot.EditorLocked) return true;
                if (IsPartsGroupLocked(profile, slot.GroupId)) return true;
                if (string.IsNullOrWhiteSpace(slot.ParentSlotId)) return false;
                cur = SpritePartIdUtility.Canonical(slot.ParentSlotId);
            }
            return false;
        }

        public static bool SlotOrAncestorHidden(SpriteSheetProfile profile, string slotId)
        {
            string cur = SpritePartIdUtility.Canonical(slotId);
            var guard = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrEmpty(cur) && guard.Add(cur))
            {
                var slot = FindSlot(profile, cur);
                if (slot == null) return false;
                if (!slot.Enabled) return true;
                if (IsPartsGroupHidden(profile, slot.GroupId)) return true;
                if (string.IsNullOrWhiteSpace(slot.ParentSlotId)) return false;
                cur = SpritePartIdUtility.Canonical(slot.ParentSlotId);
            }
            return false;
        }

        /// <summary>Subtree includes the root slot itself.</summary>
        public static List<string> CollectSubtreeSlotIds(SpriteSheetProfile profile, string rootSlotId)
        {
            var ids = new List<string>();
            if (profile?.PartsSlots == null || string.IsNullOrWhiteSpace(rootSlotId))
                return ids;
            string root = SpritePartIdUtility.Canonical(rootSlotId);
            if (FindSlot(profile, root) == null) return ids;
            ids.Add(root);
            for (int pass = 0; pass < SpritePartIdUtility.MaxParts + 2; pass++)
            {
                bool added = false;
                for (int i = 0; i < profile.PartsSlots.Count; i++)
                {
                    var s = profile.PartsSlots[i];
                    if (s == null || string.IsNullOrWhiteSpace(s.ParentSlotId)) continue;
                    string sid = SpritePartIdUtility.Canonical(s.SlotId);
                    string parent = SpritePartIdUtility.Canonical(s.ParentSlotId);
                    if (ids.Contains(parent) && !ids.Contains(sid))
                    {
                        ids.Add(sid);
                        added = true;
                    }
                }
                if (!added) break;
            }
            return ids;
        }

        public static bool HasSiblingDisplayNameCollision(
            SpriteSheetProfile profile, string parentSlotId, string displayName, string exceptSlotId)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return true;
            string parent = string.IsNullOrWhiteSpace(parentSlotId)
                ? string.Empty
                : SpritePartIdUtility.Canonical(parentSlotId);
            string except = string.IsNullOrWhiteSpace(exceptSlotId)
                ? null
                : SpritePartIdUtility.Canonical(exceptSlotId);
            string trimmed = displayName.Trim();
            var siblings = GetChildrenSorted(profile, parent);
            for (int i = 0; i < siblings.Count; i++)
            {
                var s = siblings[i];
                if (except != null && SpritePartIdUtility.Canonical(s.SlotId) == except)
                    continue;
                if (string.Equals((s.Name ?? string.Empty).Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static string UniqueSiblingDisplayName(
            SpriteSheetProfile profile, string parentSlotId, string baseName)
        {
            string stem = string.IsNullOrWhiteSpace(baseName) ? "Part" : baseName.Trim();
            if (!HasSiblingDisplayNameCollision(profile, parentSlotId, stem, null))
                return stem;
            for (int n = 2; n < 1000; n++)
            {
                string candidate = stem + " " + n;
                if (!HasSiblingDisplayNameCollision(profile, parentSlotId, candidate, null))
                    return candidate;
            }
            return stem + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        /// <summary>Rename display Name only. SlotId / tracks / skins unchanged.</summary>
        public static HierarchyEditResult TryRenameDisplayName(
            SpriteSheetProfile profile, string slotId, string newDisplayName)
        {
            var result = new HierarchyEditResult();
            var slot = FindSlot(profile, slotId);
            if (slot == null)
            {
                result.Reason = "Slot not found.";
                return result;
            }
            if (slot.EditorLocked || SlotOrAncestorLocked(profile, slot.SlotId))
            {
                result.Reason = "Part is locked.";
                return result;
            }
            string trimmed = (newDisplayName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                result.Reason = "Name cannot be empty.";
                return result;
            }
            if (HasSiblingDisplayNameCollision(profile, slot.ParentSlotId, trimmed, slot.SlotId))
            {
                result.Reason = "A sibling already uses that name.";
                return result;
            }
            slot.Name = trimmed;
            result.Ok = true;
            return result;
        }

        /// <summary>+ Part: always root-level. Does not parent to selection.</summary>
        public static HierarchyEditResult TryAddRootPart(
            SpriteSheetProfile profile, out SpritePartSlotDef created)
        {
            return TryAddPart(profile, parentSlotId: string.Empty, out created);
        }

        /// <summary>Adds a bone (a joint with no image) under <paramref name="parentSlotId"/> (empty = root).</summary>
        public static HierarchyEditResult TryAddBone(
            SpriteSheetProfile profile, string parentSlotId, out SpritePartSlotDef created)
        {
            var result = string.IsNullOrWhiteSpace(parentSlotId)
                ? TryAddPart(profile, string.Empty, out created)
                : TryAddChildPart(profile, parentSlotId, out created);
            if (created == null)
                return result;
            created.IsBone = true;
            created.BoneLength = 1f;
            created.Name = UniqueSiblingDisplayName(profile, created.ParentSlotId, "Bone");
            return result;
        }

        /// <summary>Add Child under an unlocked parent.</summary>
        public static HierarchyEditResult TryAddChildPart(
            SpriteSheetProfile profile, string parentSlotId, out SpritePartSlotDef created)
        {
            created = null;
            var parent = FindSlot(profile, parentSlotId);
            if (parent == null)
                return new HierarchyEditResult { Reason = "Parent not found." };
            if (parent.EditorLocked || SlotOrAncestorLocked(profile, parent.SlotId))
                return new HierarchyEditResult { Reason = "Parent is locked." };
            return TryAddPart(profile, parent.SlotId, out created);
        }

        public static HierarchyEditResult TryAddPart(
            SpriteSheetProfile profile, string parentSlotId, out SpritePartSlotDef created)
        {
            created = null;
            var result = new HierarchyEditResult();
            if (profile == null)
            {
                result.Reason = "Profile is null.";
                return result;
            }
            profile.EnsurePartsRig();
            if (profile.PartsSlots.Count >= SpritePartIdUtility.MaxParts)
            {
                result.Reason = $"Parts supports at most {SpritePartIdUtility.MaxParts} slots.";
                return result;
            }

            string parent = string.IsNullOrWhiteSpace(parentSlotId)
                ? string.Empty
                : SpritePartIdUtility.Canonical(parentSlotId);
            if (!string.IsNullOrEmpty(parent) && FindSlot(profile, parent) == null)
            {
                result.Reason = "Parent not found.";
                return result;
            }

            string display = UniqueSiblingDisplayName(profile, parent, "Part");
            string slotIdBase = SpritePartIdUtility.Canonical(display, "part");
            string slotId = slotIdBase;
            int suffix = 2;
            while (FindSlot(profile, slotId) != null)
            {
                slotId = slotIdBase + "." + suffix;
                suffix++;
            }

            int nextRank = 0;
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var s = profile.PartsSlots[i];
                if (s != null) nextRank = Math.Max(nextRank, s.DrawRank + 1);
            }

            var siblings = GetChildrenSorted(profile, parent);
            created = new SpritePartSlotDef
            {
                Name = display,
                SlotId = slotId,
                ParentSlotId = parent,
                SiblingOrder = siblings.Count,
                RestPosition = Vector2.zero,
                RestRotation = 0f,
                RestScale = Vector2.one,
                DrawRank = Mathf.Clamp(nextRank, 0, SpritePartIdUtility.MaxParts - 1),
                Enabled = true,
                EditorLocked = false,
            };
            profile.PartsSlots.Add(created);
            NormalizeSiblingOrders(profile);
            SpritePartsValidation.CanonicalizeIds(profile);
            // Re-find after canonicalize (SlotId stable if already canonical).
            created = FindSlot(profile, slotId) ?? created;
            result.Ok = true;
            return result;
        }

        /// <summary>
        /// Duplicate one part (not subtree). Copies rest pose, appearance, DrawRank+1,
        /// and every clip track for that SlotId. Optional horizontal mirror for opposite limbs.
        /// </summary>
        public static HierarchyEditResult TryDuplicatePart(
            SpriteSheetProfile profile,
            string slotId,
            out SpritePartSlotDef created,
            bool mirrorHorizontal = false,
            bool mirrorVertical = false)
        {
            created = null;
            var result = new HierarchyEditResult();
            if (profile == null)
            {
                result.Reason = "Profile is null.";
                return result;
            }
            profile.EnsurePartsRig();
            var srcRoot = FindSlot(profile, slotId);
            if (srcRoot == null)
            {
                result.Reason = "Slot not found.";
                return result;
            }
            if (srcRoot.EditorLocked || SlotOrAncestorLocked(profile, srcRoot.SlotId))
            {
                result.Reason = "Part is locked.";
                return result;
            }

            var subtree = CollectSubtreeSlots(profile, srcRoot.SlotId);
            if (subtree.Count == 0)
            {
                result.Reason = "Nothing to duplicate.";
                return result;
            }
            if (profile.PartsSlots.Count + subtree.Count > SpritePartIdUtility.MaxParts)
            {
                result.Reason = $"Parts supports at most {SpritePartIdUtility.MaxParts} slots.";
                return result;
            }

            int nextRank = 0;
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var s = profile.PartsSlots[i];
                if (s != null) nextRank = Math.Max(nextRank, s.DrawRank + 1);
            }

            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            SpritePartSlotDef newRoot = null;

            for (int n = 0; n < subtree.Count; n++)
            {
                var src = subtree[n];
                bool isRoot = n == 0;
                string srcId = SpritePartIdUtility.Canonical(src.SlotId);
                string parentNew = isRoot
                    ? (src.ParentSlotId ?? string.Empty)
                    : (idMap.TryGetValue(SpritePartIdUtility.Canonical(src.ParentSlotId), out var mapped)
                        ? mapped
                        : (src.ParentSlotId ?? string.Empty));

                string srcName = string.IsNullOrWhiteSpace(src.Name) ? "Part" : src.Name.Trim();
                string stem;
                if (isRoot)
                {
                    if (mirrorHorizontal || mirrorVertical)
                        stem = SuggestMirroredDisplayName(srcName);
                    else if (srcName.EndsWith(" Copy", StringComparison.Ordinal))
                        stem = srcName;
                    else
                        stem = srcName + " Copy";
                }
                else
                {
                    stem = (mirrorHorizontal || mirrorVertical)
                        ? SuggestMirroredDisplayName(srcName) : srcName;
                }

                string display = UniqueSiblingDisplayName(profile, parentNew, stem);
                string slotIdBase = SpritePartIdUtility.Canonical(display, "part");
                string newId = slotIdBase;
                int suffix = 2;
                while (FindSlot(profile, newId) != null || ContainsMappedId(idMap, newId))
                {
                    newId = slotIdBase + "." + suffix;
                    suffix++;
                }
                idMap[srcId] = newId;

                var createdSlot = new SpritePartSlotDef
                {
                    Name = display,
                    SlotId = newId,
                    ParentSlotId = parentNew,
                    SiblingOrder = src.SiblingOrder,
                    RestPosition = src.RestPosition,
                    RestRotation = src.RestRotation,
                    RestScale = SanitizeScale(src.RestScale),
                    DefaultAppearanceId = src.DefaultAppearanceId ?? string.Empty,
                    DrawRank = Mathf.Clamp(nextRank++, 0, SpritePartIdUtility.MaxParts - 1),
                    Enabled = src.Enabled,
                    EditorLocked = false,
                    Mesh = src.Mesh?.Clone() ?? new SpritePartMeshDef(),
                };

                if (isRoot)
                {
                    if (mirrorHorizontal || mirrorVertical)
                    {
                        float sx = mirrorHorizontal ? -1f : 1f;
                        float sy = mirrorVertical ? -1f : 1f;
                        createdSlot.RestPosition = new Vector2(
                            createdSlot.RestPosition.x * sx, createdSlot.RestPosition.y * sy);
                        createdSlot.RestScale = SanitizeScale(new Vector2(
                            createdSlot.RestScale.x * sx, createdSlot.RestScale.y * sy));
                        // Odd reflection (H xor V) mirrors local rotation.
                        if (mirrorHorizontal ^ mirrorVertical)
                            createdSlot.RestRotation = -createdSlot.RestRotation;
                    }
                    else
                    {
                        // Nudge so the copy is not stacked exactly on the original.
                        createdSlot.RestPosition = new Vector2(
                            createdSlot.RestPosition.x + 0.45f,
                            createdSlot.RestPosition.y);
                    }

                    var siblings = GetChildrenSorted(profile, parentNew);
                    int insertAt = src.SiblingOrder + 1;
                    for (int s = 0; s < siblings.Count; s++)
                    {
                        if (siblings[s] != null && siblings[s].SiblingOrder >= insertAt)
                            siblings[s].SiblingOrder++;
                    }
                    createdSlot.SiblingOrder = insertAt;
                    newRoot = createdSlot;
                }
                // Children keep local TRS; root mirror flips the whole subtree visually.

                profile.PartsSlots.Add(createdSlot);
            }

            if (profile.PartsClips != null)
            {
                foreach (var kv in idMap)
                {
                    string srcId = kv.Key;
                    string dstId = kv.Value;
                    for (int c = 0; c < profile.PartsClips.Count; c++)
                    {
                        var clip = profile.PartsClips[c];
                        if (clip == null) continue;
                        var srcTrack = FindTrack(clip, srcId);
                        if (srcTrack?.Keys == null || srcTrack.Keys.Count == 0) continue;
                        var dst = GetOrCreateTrack(clip, dstId);
                        dst.Keys = new List<SpritePartsKeyDef>(srcTrack.Keys.Count);
                        for (int k = 0; k < srcTrack.Keys.Count; k++)
                        {
                            var key = srcTrack.Keys[k];
                            if (key == null) continue;
                            var copy = new SpritePartsKeyDef
                            {
                                Time = key.Time,
                                Position = key.Position,
                                Rotation = key.Rotation,
                                Scale = SanitizeScale(key.Scale),
                                Deform = key.Deform == null ? null : (Vector2[])key.Deform.Clone(),
                                EaseMode = key.EaseMode,
                                HasColor = key.HasColor,
                                Color = key.Color,
                                HasDrawOrder = key.HasDrawOrder,
                                DrawOrder = key.DrawOrder,
                                Curve = key.Curve,
                                AppearanceId = key.AppearanceId ?? string.Empty,
                            };
                            // Mirror clip keys only for the duplicated root slot.
                            if ((mirrorHorizontal || mirrorVertical) &&
                                string.Equals(srcId, SpritePartIdUtility.Canonical(srcRoot.SlotId), StringComparison.Ordinal))
                            {
                                float sx = mirrorHorizontal ? -1f : 1f;
                                float sy = mirrorVertical ? -1f : 1f;
                                copy.Position = new Vector2(copy.Position.x * sx, copy.Position.y * sy);
                                copy.Scale = SanitizeScale(new Vector2(copy.Scale.x * sx, copy.Scale.y * sy));
                                if (mirrorHorizontal ^ mirrorVertical)
                                    copy.Rotation = -copy.Rotation;
                            }
                            dst.Keys.Add(copy);
                        }
                        dst.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
                    }
                }
            }

            NormalizeSiblingOrders(profile);
            SpritePartsValidation.CanonicalizeIds(profile);
            created = newRoot != null ? FindSlot(profile, newRoot.SlotId) ?? newRoot : null;
            result.Ok = created != null;
            result.DeletedSlotCount = subtree.Count; // reused: number of slots copied
            result.AffectedClipCount = profile.PartsClips?.Count ?? 0;
            if (!result.Ok)
                result.Reason = "Duplicate created no slots.";
            return result;
        }

        static bool ContainsMappedId(Dictionary<string, string> map, string value)
        {
            foreach (var kv in map)
                if (string.Equals(kv.Value, value, StringComparison.Ordinal))
                    return true;
            return false;
        }

        static List<SpritePartSlotDef> CollectSubtreeSlots(SpriteSheetProfile profile, string rootId)
        {
            var list = new List<SpritePartSlotDef>();
            var root = FindSlot(profile, rootId);
            if (root == null) return list;
            void Walk(SpritePartSlotDef s)
            {
                list.Add(s);
                var kids = GetChildrenSorted(profile, s.SlotId);
                for (int i = 0; i < kids.Count; i++)
                    Walk(kids[i]);
            }
            Walk(root);
            return list;
        }

        static string SuggestMirroredDisplayName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Part";
            string n = name.Trim();
            // Common Left/Right and L/R swaps for limb pairing.
            string[][] pairs =
            {
                new[] { "Left ", "Right " }, new[] { "left ", "right " },
                new[] { "LEFT ", "RIGHT " }, new[] { "L ", "R " },
                new[] { "Left", "Right" }, new[] { "left", "right" },
                new[] { "_L", "_R" }, new[] { ".L", ".R" },
                new[] { " L", " R" },
            };
            for (int i = 0; i < pairs.Length; i++)
            {
                string a = pairs[i][0], b = pairs[i][1];
                if (n.Contains(a)) return ReplaceFirst(n, a, b);
                if (n.Contains(b)) return ReplaceFirst(n, b, a);
            }
            return n + " Mirrored";
        }

        static string ReplaceFirst(string text, string search, string replace)
        {
            int i = text.IndexOf(search, StringComparison.Ordinal);
            if (i < 0) return text;
            return text.Substring(0, i) + replace + text.Substring(i + search.Length);
        }

        /// <summary>
        /// Flip a part in place (rest + all clip keys). Negative scale is the sprite flip;
        /// local position/rotation on that axis are mirrored so the joint stays visually opposite.
        /// </summary>
        public static HierarchyEditResult TryFlipPart(
            SpriteSheetProfile profile, string slotId, bool flipX, bool flipY)
        {
            var result = new HierarchyEditResult();
            if (!flipX && !flipY)
            {
                result.Reason = "Nothing to flip.";
                return result;
            }
            if (profile == null)
            {
                result.Reason = "Profile is null.";
                return result;
            }
            profile.EnsurePartsRig();
            var slot = FindSlot(profile, slotId);
            if (slot == null)
            {
                result.Reason = "Slot not found.";
                return result;
            }
            if (slot.EditorLocked || SlotOrAncestorLocked(profile, slot.SlotId))
            {
                result.Reason = "Part is locked.";
                return result;
            }

            float sx = flipX ? -1f : 1f;
            float sy = flipY ? -1f : 1f;
            slot.RestPosition = new Vector2(slot.RestPosition.x * sx, slot.RestPosition.y * sy);
            slot.RestScale = SanitizeScale(new Vector2(slot.RestScale.x * sx, slot.RestScale.y * sy));
            if (flipX ^ flipY)
                slot.RestRotation = -slot.RestRotation;

            string id = SpritePartIdUtility.Canonical(slot.SlotId);
            int clipsTouched = 0;
            if (profile.PartsClips != null)
            {
                for (int c = 0; c < profile.PartsClips.Count; c++)
                {
                    var clip = profile.PartsClips[c];
                    var track = FindTrack(clip, id);
                    if (track?.Keys == null || track.Keys.Count == 0) continue;
                    clipsTouched++;
                    for (int k = 0; k < track.Keys.Count; k++)
                    {
                        var key = track.Keys[k];
                        if (key == null) continue;
                        key.Position = new Vector2(key.Position.x * sx, key.Position.y * sy);
                        key.Scale = SanitizeScale(new Vector2(key.Scale.x * sx, key.Scale.y * sy));
                        if (flipX ^ flipY)
                            key.Rotation = -key.Rotation;
                    }
                }
            }

            result.Ok = true;
            result.AffectedClipCount = clipsTouched;
            return result;
        }

        /// <summary>
        /// Pure sibling reorder under the same parent. Does NOT change DrawRank.
        /// </summary>
        public static HierarchyEditResult TryReorderSibling(
            SpriteSheetProfile profile, string slotId, int newSiblingIndex)
        {
            var result = new HierarchyEditResult();
            var slot = FindSlot(profile, slotId);
            if (slot == null)
            {
                result.Reason = "Slot not found.";
                return result;
            }
            if (slot.EditorLocked || SlotOrAncestorLocked(profile, slot.SlotId))
            {
                result.Reason = "Part is locked.";
                return result;
            }
            string parent = slot.ParentSlotId ?? string.Empty;
            var siblings = GetChildrenSorted(profile, parent);
            int from = siblings.FindIndex(s =>
                SpritePartIdUtility.Canonical(s.SlotId) == SpritePartIdUtility.Canonical(slot.SlotId));
            if (from < 0)
            {
                result.Reason = "Slot not in sibling list.";
                return result;
            }
            newSiblingIndex = Mathf.Clamp(newSiblingIndex, 0, siblings.Count - 1);
            if (from == newSiblingIndex)
            {
                result.Ok = true;
                return result;
            }
            siblings.RemoveAt(from);
            siblings.Insert(newSiblingIndex, slot);
            for (int i = 0; i < siblings.Count; i++)
                siblings[i].SiblingOrder = i;
            result.Ok = true;
            return result;
        }

        public static int CountClipsWithKeysInSubtree(SpriteSheetProfile profile, IList<string> slotIds)
        {
            if (profile?.PartsClips == null || slotIds == null || slotIds.Count == 0)
                return 0;
            var set = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < slotIds.Count; i++)
                set.Add(SpritePartIdUtility.Canonical(slotIds[i]));
            int count = 0;
            for (int c = 0; c < profile.PartsClips.Count; c++)
            {
                var clip = profile.PartsClips[c];
                if (clip?.Tracks == null) continue;
                bool hit = false;
                for (int t = 0; t < clip.Tracks.Count && !hit; t++)
                {
                    var track = clip.Tracks[t];
                    if (track?.Keys == null || track.Keys.Count == 0) continue;
                    if (set.Contains(SpritePartIdUtility.Canonical(track.SlotId)))
                        hit = true;
                }
                if (hit) count++;
            }
            return count;
        }

        /// <summary>
        /// Validate a hierarchy move without mutating. Sibling reorder (same parent) is allowed.
        /// </summary>
        public static HierarchyEditResult ValidateTreeMove(
            SpriteSheetProfile profile,
            string movingSlotId,
            TreeDropKind kind,
            string relativeSlotId)
        {
            var result = new HierarchyEditResult();
            var moving = FindSlot(profile, movingSlotId);
            if (moving == null)
            {
                result.Reason = "Slot not found.";
                return result;
            }
            if (moving.EditorLocked || SlotOrAncestorLocked(profile, moving.SlotId))
            {
                result.Reason = "Part is locked.";
                return result;
            }
            var subtree = CollectSubtreeSlotIds(profile, moving.SlotId);
            for (int i = 0; i < subtree.Count; i++)
            {
                var s = FindSlot(profile, subtree[i]);
                if (s != null && s.EditorLocked)
                {
                    result.Reason = "Subtree contains a locked part.";
                    return result;
                }
            }

            string destParent;
            if (kind == TreeDropKind.MoveToRoot)
                destParent = string.Empty;
            else if (kind == TreeDropKind.ParentUnder)
            {
                var target = FindSlot(profile, relativeSlotId);
                if (target == null)
                {
                    result.Reason = "Drop target not found.";
                    return result;
                }
                if (target.EditorLocked || SlotOrAncestorLocked(profile, target.SlotId))
                {
                    result.Reason = "Cannot parent under a locked part.";
                    return result;
                }
                destParent = SpritePartIdUtility.Canonical(target.SlotId);
            }
            else if (kind == TreeDropKind.InsertBefore || kind == TreeDropKind.InsertAfter)
            {
                var relative = FindSlot(profile, relativeSlotId);
                if (relative == null)
                {
                    result.Reason = "Drop target not found.";
                    return result;
                }
                destParent = string.IsNullOrWhiteSpace(relative.ParentSlotId)
                    ? string.Empty
                    : SpritePartIdUtility.Canonical(relative.ParentSlotId);
                if (!string.IsNullOrEmpty(destParent))
                {
                    var parentSlot = FindSlot(profile, destParent);
                    if (parentSlot != null &&
                        (parentSlot.EditorLocked || SlotOrAncestorLocked(profile, parentSlot.SlotId)))
                    {
                        result.Reason = "Cannot insert under a locked parent.";
                        return result;
                    }
                }
            }
            else
            {
                result.Reason = "Invalid drop.";
                return result;
            }

            string movingId = SpritePartIdUtility.Canonical(moving.SlotId);
            if (!string.IsNullOrEmpty(destParent))
            {
                if (destParent == movingId)
                {
                    result.Reason = "Cannot parent a part to itself.";
                    return result;
                }
                if (IsDescendantOf(profile, movingId, destParent) ||
                    CollectSubtreeSlotIds(profile, movingId).Contains(destParent))
                {
                    result.Reason = "Cannot parent under a descendant (cycle).";
                    return result;
                }
                if (FindSlot(profile, destParent) == null)
                {
                    result.Reason = "Destination parent is missing.";
                    return result;
                }
            }

            // Sibling name collision at destination (except when staying under same parent).
            string currentParent = string.IsNullOrWhiteSpace(moving.ParentSlotId)
                ? string.Empty
                : SpritePartIdUtility.Canonical(moving.ParentSlotId);
            if (currentParent != destParent)
            {
                if (HasSiblingDisplayNameCollision(profile, destParent, moving.Name, moving.SlotId))
                {
                    result.Reason = "Rename first: a sibling at the destination already uses that name.";
                    return result;
                }
            }

            result.Ok = true;
            bool parentChanging = currentParent != destParent;
            if (parentChanging)
            {
                int clips = CountClipsWithKeysInSubtree(profile, subtree);
                result.NeedsAnimationReview = clips > 0;
                result.AffectedClipCount = clips;
            }
            return result;
        }

        /// <summary>
        /// Commit reparent / sibling insert. Preserves moved root REST world when representable.
        /// Pass confirmAnimationReview=true after the review dialog when Needed.
        /// Pure sibling reorder does not touch rest pose or DrawRank.
        /// </summary>
        public static HierarchyEditResult TryCommitTreeMove(
            SpriteSheetProfile profile,
            string movingSlotId,
            TreeDropKind kind,
            string relativeSlotId,
            bool confirmAnimationReview = false)
        {
            var validation = ValidateTreeMove(profile, movingSlotId, kind, relativeSlotId);
            if (!validation.Ok)
                return validation;
            if (validation.NeedsAnimationReview && !confirmAnimationReview)
                return validation;

            var moving = FindSlot(profile, movingSlotId);
            string oldParent = string.IsNullOrWhiteSpace(moving.ParentSlotId)
                ? string.Empty
                : SpritePartIdUtility.Canonical(moving.ParentSlotId);

            string destParent;
            int insertIndex;
            ResolveDestination(profile, moving, kind, relativeSlotId, out destParent, out insertIndex);

            bool parentChanging = oldParent != destParent;
            int oldDrawRank = moving.DrawRank;

            if (parentChanging)
            {
                if (!TryComputeReparentLocalRest(profile, moving, destParent,
                        out Vector2 newPos, out float newRot, out Vector2 newScale, out string reason))
                {
                    return new HierarchyEditResult { Reason = reason };
                }
                moving.RestPosition = newPos;
                moving.RestRotation = newRot;
                moving.RestScale = newScale;
                moving.ParentSlotId = destParent;
                moving.GroupId = string.Empty;
            }

            // Remove from old sibling list conceptually, insert at destination index.
            var destSiblings = GetChildrenSorted(profile, destParent);
            destSiblings.RemoveAll(s =>
                SpritePartIdUtility.Canonical(s.SlotId) == SpritePartIdUtility.Canonical(moving.SlotId));
            insertIndex = Mathf.Clamp(insertIndex, 0, destSiblings.Count);
            destSiblings.Insert(insertIndex, moving);
            for (int i = 0; i < destSiblings.Count; i++)
                destSiblings[i].SiblingOrder = i;

            if (!string.IsNullOrEmpty(oldParent) || oldParent != destParent)
                NormalizeSiblingOrders(profile);

            // DrawRank must stay unchanged on sibling reorder AND reparent.
            moving.DrawRank = oldDrawRank;
            if (parentChanging)
                PruneEmptyPartsGroups(profile);

            var ok = new HierarchyEditResult
            {
                Ok = true,
                NeedsAnimationReview = false,
                AffectedClipCount = validation.AffectedClipCount,
            };
            return ok;
        }

        static void ResolveDestination(
            SpriteSheetProfile profile,
            SpritePartSlotDef moving,
            TreeDropKind kind,
            string relativeSlotId,
            out string destParent,
            out int insertIndex)
        {
            destParent = string.Empty;
            insertIndex = 0;
            if (kind == TreeDropKind.MoveToRoot)
            {
                destParent = string.Empty;
                insertIndex = GetChildrenSorted(profile, string.Empty).Count;
                // Append at root (exclude self if already root).
                var roots = GetChildrenSorted(profile, string.Empty);
                roots.RemoveAll(s =>
                    SpritePartIdUtility.Canonical(s.SlotId) ==
                    SpritePartIdUtility.Canonical(moving.SlotId));
                insertIndex = roots.Count;
                return;
            }
            if (kind == TreeDropKind.ParentUnder)
            {
                var target = FindSlot(profile, relativeSlotId);
                destParent = SpritePartIdUtility.Canonical(target.SlotId);
                var kids = GetChildrenSorted(profile, destParent);
                kids.RemoveAll(s =>
                    SpritePartIdUtility.Canonical(s.SlotId) ==
                    SpritePartIdUtility.Canonical(moving.SlotId));
                insertIndex = kids.Count; // append as child
                return;
            }
            var relative = FindSlot(profile, relativeSlotId);
            destParent = string.IsNullOrWhiteSpace(relative.ParentSlotId)
                ? string.Empty
                : SpritePartIdUtility.Canonical(relative.ParentSlotId);
            var siblings = GetChildrenSorted(profile, destParent);
            siblings.RemoveAll(s =>
                SpritePartIdUtility.Canonical(s.SlotId) ==
                SpritePartIdUtility.Canonical(moving.SlotId));
            int relIndex = siblings.FindIndex(s =>
                SpritePartIdUtility.Canonical(s.SlotId) ==
                SpritePartIdUtility.Canonical(relative.SlotId));
            if (relIndex < 0) relIndex = siblings.Count;
            insertIndex = kind == TreeDropKind.InsertBefore ? relIndex : relIndex + 1;
        }

        /// <summary>
        /// new local = inv(newParentRestWorld) * oldRestWorld. Rejects singular / shear.
        /// </summary>
        public static bool TryComputeReparentLocalRest(
            SpriteSheetProfile profile,
            SpritePartSlotDef moving,
            string newParentSlotId,
            out Vector2 localPos,
            out float localRot,
            out Vector2 localScale,
            out string reason)
        {
            localPos = moving.RestPosition;
            localRot = moving.RestRotation;
            localScale = moving.RestScale;
            reason = null;

            if (!TryBuildRestWorldMatrices(profile, out var worlds, out reason))
                return false;

            string movingId = SpritePartIdUtility.Canonical(moving.SlotId);
            if (!worlds.TryGetValue(movingId, out float4x4 oldWorld))
            {
                reason = "Missing rest world for moved part.";
                return false;
            }

            float4x4 newParentWorld = float4x4.identity;
            if (!string.IsNullOrWhiteSpace(newParentSlotId))
            {
                string pid = SpritePartIdUtility.Canonical(newParentSlotId);
                if (!worlds.TryGetValue(pid, out newParentWorld))
                {
                    reason = "Missing rest world for new parent.";
                    return false;
                }
            }

            float4x4 invParent = math.inverse(newParentWorld);
            // Reject near-singular parent.
            float det = math.determinant(newParentWorld);
            if (!(math.abs(det) > 1e-8f) || !math.isfinite(det))
            {
                reason = "New parent rest matrix is singular.";
                return false;
            }

            float4x4 newLocal = math.mul(invParent, oldWorld);
            if (!TryDecomposeNoShear(newLocal, out localPos, out localRot, out localScale))
            {
                reason = "Reparent cannot be represented without shear (or non-positive scale).";
                return false;
            }
            return true;
        }

        public static bool TryBuildRestWorldMatrices(
            SpriteSheetProfile profile,
            out Dictionary<string, float4x4> localToRoot,
            out string reason)
        {
            var map = new Dictionary<string, float4x4>(StringComparer.Ordinal);
            localToRoot = map;
            reason = null;
            if (profile?.PartsSlots == null)
            {
                reason = "No slots.";
                return false;
            }

            var done = new HashSet<string>(StringComparer.Ordinal);
            bool Ensure(string slotId)
            {
                string id = SpritePartIdUtility.Canonical(slotId);
                if (done.Contains(id)) return map.ContainsKey(id);
                var slot = FindSlot(profile, id);
                if (slot == null) return false;
                done.Add(id);
                float4x4 local = SpritePartsHierarchy.LocalMatrix(
                    new float2(slot.RestPosition.x, slot.RestPosition.y),
                    slot.RestRotation,
                    new float2(slot.RestScale.x, slot.RestScale.y));
                if (string.IsNullOrWhiteSpace(slot.ParentSlotId))
                {
                    map[id] = local;
                    return true;
                }
                string parent = SpritePartIdUtility.Canonical(slot.ParentSlotId);
                if (!Ensure(parent))
                {
                    // Orphan: treat as root-local.
                    map[id] = local;
                    return true;
                }
                map[id] = math.mul(map[parent], local);
                return true;
            }

            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var s = profile.PartsSlots[i];
                if (s == null) continue;
                if (!Ensure(s.SlotId))
                {
                    reason = "Failed to compose rest world.";
                    return false;
                }
            }
            return true;
        }

        public static bool TryDecomposeNoShear(
            float4x4 m, out Vector2 position, out float rotationDeg, out Vector2 scale)
        {
            position = new Vector2(m.c3.x, m.c3.y);
            rotationDeg = 0f;
            scale = Vector2.one;
            if (!math.isfinite(m.c3.x) || !math.isfinite(m.c3.y))
                return false;

            float2 c0 = m.c0.xy;
            float2 c1 = m.c1.xy;
            float sx = math.length(c0);
            float sy = math.length(c1);
            if (!(sx > 1e-6f) || !(sy > 1e-6f) || !math.isfinite(sx) || !math.isfinite(sy))
                return false;

            float2 x = c0 / sx;
            float2 y = c1 / sy;
            if (math.abs(math.dot(x, y)) > 1e-3f)
                return false; // shear

            float det2 = x.x * y.y - x.y * y.x;
            if (!math.isfinite(det2) || math.abs(det2) < 1e-6f)
                return false;

            // Reflection (negative scale) is legal for Flip H/V; return signed sx.
            if (det2 < 0f)
                sx = -sx;

            rotationDeg = math.degrees(math.atan2(x.y, x.x));
            if (!math.isfinite(rotationDeg))
                return false;
            scale = new Vector2(sx, sy);
            return true;
        }

        /// <summary>
        /// Delete part and all descendants. Removes tracks and skin bindings. No child promotion.
        /// </summary>
        
        /// <summary>Validate delete without mutating. Used by hotkeys / context menu disabled reasons.</summary>
        public static HierarchyEditResult ValidateDeleteSubtree(SpriteSheetProfile profile, string slotId)
        {
            var result = new HierarchyEditResult();
            if (profile?.PartsSlots == null)
            {
                result.Reason = "No slots.";
                return result;
            }
            var root = FindSlot(profile, slotId);
            if (root == null)
            {
                result.Reason = "Slot not found.";
                return result;
            }
            if (root.EditorLocked || SlotOrAncestorLocked(profile, root.SlotId))
            {
                result.Reason = "Part is locked.";
                return result;
            }
            var subtree = CollectSubtreeSlotIds(profile, root.SlotId);
            for (int i = 0; i < subtree.Count; i++)
            {
                var s = FindSlot(profile, subtree[i]);
                if (s != null && s.EditorLocked)
                {
                    result.Reason = "Subtree contains a locked part.";
                    return result;
                }
            }
            result.Ok = true;
            result.DeletedSlotCount = subtree.Count;
            return result;
        }

        /// <summary>Break from parent → Character root, preserve rest world pose.</summary>
        public static HierarchyEditResult TryBreakFromParent(
            SpriteSheetProfile profile, string slotId, bool confirmAnimationReview = false)
        {
            return TryCommitTreeMove(
                profile, slotId, TreeDropKind.MoveToRoot, string.Empty, confirmAnimationReview);
        }

        /// <summary>Move up one level → grandparent or Character root, preserve rest world pose.</summary>
        public static HierarchyEditResult TryMoveUpOneLevel(
            SpriteSheetProfile profile, string slotId, bool confirmAnimationReview = false)
        {
            var result = new HierarchyEditResult();
            var slot = FindSlot(profile, slotId);
            if (slot == null)
            {
                result.Reason = "Slot not found.";
                return result;
            }
            if (string.IsNullOrWhiteSpace(slot.ParentSlotId))
            {
                result.Reason = "Already at Character root.";
                return result;
            }
            var parent = FindSlot(profile, slot.ParentSlotId);
            if (parent == null)
            {
                // Orphan → treat as break to root.
                return TryCommitTreeMove(
                    profile, slotId, TreeDropKind.MoveToRoot, string.Empty, confirmAnimationReview);
            }
            if (string.IsNullOrWhiteSpace(parent.ParentSlotId))
            {
                return TryCommitTreeMove(
                    profile, slotId, TreeDropKind.MoveToRoot, string.Empty, confirmAnimationReview);
            }
            // Parent under grandparent (append as child).
            return TryCommitTreeMove(
                profile, slotId, TreeDropKind.ParentUnder, parent.ParentSlotId, confirmAnimationReview);
        }

        /// <summary>SiblingOrder only: move to first among siblings.</summary>
        public static HierarchyEditResult TryMoveSiblingToTop(SpriteSheetProfile profile, string slotId)
        {
            return TryReorderSibling(profile, slotId, 0);
        }

        /// <summary>SiblingOrder only: move to last among siblings.</summary>
        public static HierarchyEditResult TryMoveSiblingToBottom(SpriteSheetProfile profile, string slotId)
        {
            var slot = FindSlot(profile, slotId);
            if (slot == null)
                return new HierarchyEditResult { Reason = "Slot not found." };
            var siblings = GetChildrenSorted(profile, slot.ParentSlotId ?? string.Empty);
            return TryReorderSibling(profile, slotId, Math.Max(0, siblings.Count - 1));
        }

        /// <summary>Human-readable reason when hierarchy structural edits are blocked in the editor.</summary>
        public static string DescribeHierarchyEditBlock(
            SpriteSheetProfile profile, string slotId, SpritePartsStudioMode mode, string action)
        {
            _ = mode;
            if (string.IsNullOrWhiteSpace(slotId))
                return "No part selected.";
            var slot = FindSlot(profile, slotId);
            if (slot == null)
                return "Slot not found.";
            if (slot.EditorLocked || SlotOrAncestorLocked(profile, slot.SlotId))
                return "Part is locked.";
            if (string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase))
            {
                var v = ValidateDeleteSubtree(profile, slotId);
                return v.Ok ? null : v.Reason;
            }
            return null;
        }
public static HierarchyEditResult TryDeleteSubtree(SpriteSheetProfile profile, string slotId)
        {
            var result = new HierarchyEditResult();
            if (profile?.PartsSlots == null)
            {
                result.Reason = "No slots.";
                return result;
            }
            var root = FindSlot(profile, slotId);
            if (root == null)
            {
                result.Reason = "Slot not found.";
                return result;
            }
            if (root.EditorLocked || SlotOrAncestorLocked(profile, root.SlotId))
            {
                result.Reason = "Part is locked.";
                return result;
            }

            var subtree = CollectSubtreeSlotIds(profile, root.SlotId);
            for (int i = 0; i < subtree.Count; i++)
            {
                var s = FindSlot(profile, subtree[i]);
                if (s != null && s.EditorLocked)
                {
                    result.Reason = "Subtree contains a locked part.";
                    return result;
                }
            }

            var idSet = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < subtree.Count; i++)
                idSet.Add(SpritePartIdUtility.Canonical(subtree[i]));

            int tracksRemoved = 0;
            if (profile.PartsClips != null)
            {
                for (int c = 0; c < profile.PartsClips.Count; c++)
                {
                    var clip = profile.PartsClips[c];
                    if (clip?.Tracks == null) continue;
                    tracksRemoved += clip.Tracks.RemoveAll(t =>
                        t != null && idSet.Contains(SpritePartIdUtility.Canonical(t.SlotId)));
                }
            }

            int bindingsRemoved = 0;
            if (profile.PartsSkins != null)
            {
                for (int s = 0; s < profile.PartsSkins.Count; s++)
                {
                    var skin = profile.PartsSkins[s];
                    if (skin?.Bindings == null) continue;
                    bindingsRemoved += skin.Bindings.RemoveAll(b =>
                        b != null && idSet.Contains(SpritePartIdUtility.Canonical(b.SlotId)));
                }
            }

            int deleted = profile.PartsSlots.RemoveAll(s =>
                s != null && idSet.Contains(SpritePartIdUtility.Canonical(s.SlotId)));
            NormalizeSiblingOrders(profile);
            PruneEmptyPartsGroups(profile);

            result.Ok = true;
            result.DeletedSlotCount = deleted;
            result.DeletedTrackCount = tracksRemoved;
            result.DeletedBindingCount = bindingsRemoved;
            return result;
        }


        /// <summary>Slots sorted by DrawRank. frontFirst: highest rank first (drawn last / on top).</summary>
        public static List<SpritePartSlotDef> GetSlotsSortedByDrawRank(
            SpriteSheetProfile profile, bool frontFirst)
        {
            var list = new List<SpritePartSlotDef>();
            if (profile?.PartsSlots == null) return list;
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var s = profile.PartsSlots[i];
                if (s != null) list.Add(s);
            }
            list.Sort((a, b) =>
            {
                int cmp = a.DrawRank.CompareTo(b.DrawRank);
                if (cmp == 0)
                    cmp = string.CompareOrdinal(
                        SpritePartIdUtility.Canonical(a.SlotId),
                        SpritePartIdUtility.Canonical(b.SlotId));
                return frontFirst ? -cmp : cmp;
            });
            return list;
        }

        /// <summary>
        /// Place slot at front-first index (0 = in front). Reassigns unique DrawRank 0..n-1
        /// with n-1 = front. Does not change hierarchy / SiblingOrder.
        /// </summary>
        public static HierarchyEditResult TryMoveDrawRankToFrontIndex(
            SpriteSheetProfile profile, string slotId, int frontIndex)
        {
            var result = new HierarchyEditResult();
            var slot = FindSlot(profile, slotId);
            if (slot == null)
            {
                result.Reason = "Slot not found.";
                return result;
            }
            var list = GetSlotsSortedByDrawRank(profile, frontFirst: true);
            int from = list.FindIndex(s =>
                SpritePartIdUtility.Canonical(s.SlotId) == SpritePartIdUtility.Canonical(slot.SlotId));
            if (from < 0)
            {
                result.Reason = "Slot not in layer list.";
                return result;
            }
            frontIndex = Mathf.Clamp(frontIndex, 0, list.Count - 1);
            if (from == frontIndex)
            {
                result.Ok = true;
                return result;
            }
            list.RemoveAt(from);
            list.Insert(frontIndex, slot);
            for (int i = 0; i < list.Count; i++)
                list[i].DrawRank = list.Count - 1 - i;
            result.Ok = true;
            return result;
        }

        /// <summary>Bump DrawRank toward front (higher) or back (lower) without changing SiblingOrder.</summary>
        public static HierarchyEditResult TryNudgeDrawRank(
            SpriteSheetProfile profile, string slotId, int delta)
        {
            var result = new HierarchyEditResult();
            var slot = FindSlot(profile, slotId);
            if (slot == null)
            {
                result.Reason = "Slot not found.";
                return result;
            }
            int target = Mathf.Clamp(slot.DrawRank + delta, 0, SpritePartIdUtility.MaxParts - 1);
            if (target == slot.DrawRank)
            {
                result.Ok = true;
                return result;
            }
            // Swap with occupant of target rank if any.
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var other = profile.PartsSlots[i];
                if (other == null || ReferenceEquals(other, slot)) continue;
                if (other.DrawRank == target)
                {
                    other.DrawRank = slot.DrawRank;
                    break;
                }
            }
            slot.DrawRank = target;
            result.Ok = true;
            return result;
        }
    }
}


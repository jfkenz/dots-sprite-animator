using System;
using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Frame clip and Parts clip import between profiles. Builds on
    /// <see cref="SpriteProfileArtImport"/> for sheet/appearance dependency
    /// closure. Planning is read-only; Apply is atomic. Parts clips require an
    /// explicit SlotId map (exact id matches may be suggested by the UI).
    /// </summary>
    public static class SpriteProfileClipImport
    {
        public sealed class FrameClipAction
        {
            public int SourceClipIndex;
            public string SourceName;
            public string DestinationName;
            public int SourceSheetIndex;
            /// <summary>Replace policy: overwrite this destination clip slot, keeping name and index.</summary>
            public bool ReplaceExisting;
            public int DestinationClipIndex = -1;
            /// <summary>Replace preview: destination references to this clip.</summary>
            public readonly List<string> UsedBy = new List<string>();
        }

        public sealed class PartsClipAction
        {
            public int SourceClipIndex;
            public string SourceName;
            public string SourceClipId;
            public string DestinationName;
            public string DestinationClipId;
            /// <summary>Replace policy: overwrite this destination clip slot, keeping ClipId, name and index.</summary>
            public bool ReplaceExisting;
            public int DestinationClipListIndex = -1;
            /// <summary>Replace preview: destination references to this clip.</summary>
            public readonly List<string> UsedBy = new List<string>();
            public List<string> ExcludedSourceSlotIds = new List<string>();
        }

        /// <summary>Event catalog entry to append to the destination at apply.</summary>
        public sealed class EventCatalogAction
        {
            public byte DestinationId;
            public string Name;
            public Color Color;
        }

        /// <summary>Socket catalog closure for one socket name used by imported clips.</summary>
        public sealed class SocketCatalogAction
        {
            public string SocketName;
            /// <summary>Destination already catalogs this name: keep its stable identity.</summary>
            public bool KeepExisting;
            /// <summary>Copy of the source item to add when !KeepExisting (Profile back-reference blanked).</summary>
            public SpriteSocketCatalogItem ItemToAdd;
            /// <summary>SocketId the clip sockets resolve to after apply.</summary>
            public string DestinationSocketId;
        }

        public sealed class Plan
        {
            public bool Ok;
            public string Reason;
            public SpriteProfileImportConflictPolicy ConflictPolicy;
            public List<string> Notes = new List<string>();
            public SpriteProfileArtImport.Plan Art = new SpriteProfileArtImport.Plan();
            public List<FrameClipAction> FrameClips = new List<FrameClipAction>();
            public List<PartsClipAction> PartsClips = new List<PartsClipAction>();
            public Dictionary<string, string> SlotMap = new Dictionary<string, string>(StringComparer.Ordinal);
            /// <summary>Frame-clip event closure: source event id -> destination id (meaning preserved).</summary>
            public Dictionary<byte, byte> EventIdRemap = new Dictionary<byte, byte>();
            public List<EventCatalogAction> EventsToAdd = new List<EventCatalogAction>();
            public List<SocketCatalogAction> SocketCatalogActions = new List<SocketCatalogAction>();
            public int FrameClipsReplaced;
            public int PartsClipsReplaced;
            public string Summary
            {
                get
                {
                    if (!Ok) return Reason ?? "Invalid plan.";
                    var closure = new List<string>();
                    if (EventsToAdd.Count > 0)
                        closure.Add("+" + EventsToAdd.Count + " event catalog entr" + (EventsToAdd.Count == 1 ? "y" : "ies"));
                    if (CountSocketAdds() > 0)
                        closure.Add("+" + CountSocketAdds() + " socket catalog entr" + (CountSocketAdds() == 1 ? "y" : "ies"));
                    string closureText = closure.Count > 0 ? " (" + string.Join(", ", closure.ToArray()) + ")" : string.Empty;
                    return string.Format("{0}; +{1} frame clip(s){2}{5}, +{3} Parts clip(s){4}",
                        Art.Summary, FrameClips.Count,
                        FrameClipsReplaced > 0 ? string.Format(", {0} replaced", FrameClipsReplaced) : string.Empty,
                        PartsClips.Count,
                        PartsClipsReplaced > 0 ? string.Format(", {0} replaced", PartsClipsReplaced) : string.Empty,
                        closureText);
                }
            }

            int CountSocketAdds()
            {
                int n = 0;
                for (int i = 0; i < SocketCatalogActions.Count; i++)
                    if (!SocketCatalogActions[i].KeepExisting) n++;
                return n;
            }
        }

        public sealed class ApplyResult
        {
            public bool Ok;
            public string Reason;
            public string Summary;
            public List<KeyValuePair<string, string>> AppearanceIdRemaps =
                new List<KeyValuePair<string, string>>();
        }

        /// <summary>
        /// Gate 7 preview data: how a source slot's motion will compose
        /// differently on the mapped destination slot. Read-only analysis.
        /// </summary>
        public sealed class SlotMapMismatch
        {
            public string SourceSlotId;
            public string DestinationSlotId;
            /// <summary>"rest" or "parent".</summary>
            public string Kind;
            public string Detail;
        }

        /// <summary>
        /// Transient preview clones of the planned Parts clips, mapped onto
        /// destination slots and appearance ids. Read-only against both
        /// profiles; used to preview motion before commit. This is a motion
        /// copy, not a retarget: keys stay local, so differing rest poses or
        /// hierarchies change the resulting world motion.
        /// </summary>
        public static List<SpritePartsClipDef> BuildPreviewClips(
            Plan plan, SpriteSheetProfile source, SpriteSheetProfile destination)
        {
            var result = new List<SpritePartsClipDef>();
            if (plan == null || !plan.Ok || source?.PartsClips == null)
                return result;
            var appearanceRemap = new Dictionary<string, string>(StringComparer.Ordinal);
            if (plan.Art?.Appearances != null)
            {
                for (int i = 0; i < plan.Art.Appearances.Count; i++)
                {
                    var a = plan.Art.Appearances[i];
                    if (!string.IsNullOrEmpty(a.DestinationAppearanceId))
                        appearanceRemap[a.SourceAppearanceId] = a.DestinationAppearanceId;
                }
            }
            for (int i = 0; i < plan.PartsClips.Count; i++)
            {
                var action = plan.PartsClips[i];
                if (action.SourceClipIndex < 0 || action.SourceClipIndex >= source.PartsClips.Count)
                    continue;
                var excluded = new HashSet<string>(action.ExcludedSourceSlotIds, StringComparer.Ordinal);
                result.Add(ClonePartsClip(
                    source.PartsClips[action.SourceClipIndex],
                    action.DestinationName, action.DestinationClipId,
                    plan.SlotMap, excluded, appearanceRemap));
            }
            return result;
        }

        /// <summary>Canonical-id lookup into a slot map whose keys may be non-canonical.</summary>
        static bool TryResolveCanonicalMapping(
            Dictionary<string, string> slotMap, string canonicalSourceId, out string canonicalDestination)
        {
            canonicalDestination = string.Empty;
            foreach (var kv in slotMap)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                if (SpritePartIdUtility.Canonical(kv.Key) == canonicalSourceId)
                {
                    canonicalDestination = SpritePartIdUtility.Canonical(kv.Value);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Read-only report of rest-pose and parent mismatches a slot map
        /// introduces. Mismatches do not block the import (motion copy is
        /// allowed to compose differently); they must be previewed.
        /// </summary>
        public static List<SlotMapMismatch> CollectSlotMapMismatches(
            SpriteSheetProfile source, SpriteSheetProfile destination, Dictionary<string, string> slotMap)
        {
            var result = new List<SlotMapMismatch>();
            if (source?.PartsSlots == null || destination?.PartsSlots == null || slotMap == null)
                return result;
            foreach (var kv in slotMap)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value))
                    continue; // blank rows are excluded rows, never a mismatch source
                string sourceId = SpritePartIdUtility.Canonical(kv.Key);
                string destId = SpritePartIdUtility.Canonical(kv.Value);
                var src = FindSlot(source, sourceId);
                var dst = FindSlot(destination, destId);
                if (src == null || dst == null)
                    continue;

                bool restDiffers =
                    Vector2.Distance(src.RestPosition, dst.RestPosition) > 1e-4f ||
                    Mathf.Abs(Mathf.DeltaAngle(src.RestRotation, dst.RestRotation)) > 1e-3f ||
                    Vector2.Distance(src.RestScale, dst.RestScale) > 1e-4f;
                if (restDiffers)
                {
                    result.Add(new SlotMapMismatch
                    {
                        SourceSlotId = sourceId,
                        DestinationSlotId = destId,
                        Kind = "rest",
                        Detail = $"source rest ({src.RestPosition.x:F2}, {src.RestPosition.y:F2}) r{src.RestRotation:F0} " +
                                 $"vs destination rest ({dst.RestPosition.x:F2}, {dst.RestPosition.y:F2}) r{dst.RestRotation:F0}; " +
                                 "keys stay local, so world motion shifts",
                    });
                }

                string srcParent = string.IsNullOrWhiteSpace(src.ParentSlotId)
                    ? string.Empty
                    : SpritePartIdUtility.Canonical(src.ParentSlotId);
                string dstParent = string.IsNullOrWhiteSpace(dst.ParentSlotId)
                    ? string.Empty
                    : SpritePartIdUtility.Canonical(dst.ParentSlotId);
                // Resolve the source parent through the same map by CANONICAL id:
                // raw keys can be non-canonical ("Hand.R"), and Canonical("")
                // falls back to "part", so blanks are filtered here. A source
                // parent missing from the map resolves to root - and still
                // reports when the destination parent is not root.
                string mappedSrcParent = string.Empty;
                if (srcParent.Length > 0 &&
                    TryResolveCanonicalMapping(slotMap, srcParent, out string mappedParent) &&
                    mappedParent.Length > 0)
                {
                    mappedSrcParent = mappedParent;
                }
                bool parentDiffers = !string.Equals(mappedSrcParent, dstParent, StringComparison.Ordinal);
                if (parentDiffers)
                {
                    result.Add(new SlotMapMismatch
                    {
                        SourceSlotId = sourceId,
                        DestinationSlotId = destId,
                        Kind = "parent",
                        Detail = $"source parent '{(srcParent.Length == 0 ? "(root)" : srcParent)}' maps to " +
                                 $"'{(mappedSrcParent.Length == 0 ? "(root)" : mappedSrcParent)}' but destination parent is " +
                                 $"'{(dstParent.Length == 0 ? "(root)" : dstParent)}'; hierarchy composition differs",
                    });
                }
            }
            return result;
        }

        public static Dictionary<string, string> SuggestSlotMap(
            SpriteSheetProfile source, SpriteSheetProfile destination, IEnumerable<int> partsClipIndices)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (source?.PartsClips == null || destination?.PartsSlots == null || partsClipIndices == null)
                return map;
            var destIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < destination.PartsSlots.Count; i++)
            {
                var slot = destination.PartsSlots[i];
                if (slot != null)
                    destIds.Add(SpritePartIdUtility.Canonical(slot.SlotId, slot.Name));
            }
            foreach (int index in partsClipIndices)
            {
                if (index < 0 || index >= source.PartsClips.Count) continue;
                var clip = source.PartsClips[index];
                if (clip?.Tracks == null) continue;
                for (int t = 0; t < clip.Tracks.Count; t++)
                {
                    var track = clip.Tracks[t];
                    if (track == null) continue;
                    string sid = SpritePartIdUtility.Canonical(track.SlotId);
                    if (string.IsNullOrEmpty(sid) || map.ContainsKey(sid)) continue;
                    if (destIds.Contains(sid))
                        map[sid] = sid;
                }
            }
            return map;
        }

        public sealed class SlotNameSuggestion
        {
            public string SourceSlotId;
            public string DestinationSlotId;
            public string DestinationName;
        }

        /// <summary>
        /// Name-based suggestions on top of the exact-id auto-map: case-insensitive
        /// display-Name matches. Never auto-applied - the popup shows them and the
        /// user confirms. A destination is suggested for at most one source, and
        /// never for a source that already exact-id auto-maps. Mapping two sources
        /// onto one destination is still blocked by PlanImport validation.
        /// </summary>
        public static List<SlotNameSuggestion> SuggestSlotMapByName(
            SpriteSheetProfile source, SpriteSheetProfile destination, IEnumerable<int> partsClipIndices)
        {
            var result = new List<SlotNameSuggestion>();
            if (source?.PartsClips == null || destination?.PartsSlots == null || partsClipIndices == null)
                return result;

            var sourceOrder = new List<string>();
            foreach (int index in partsClipIndices)
            {
                if (index < 0 || index >= source.PartsClips.Count) continue;
                var clip = source.PartsClips[index];
                if (clip?.Tracks == null) continue;
                for (int t = 0; t < clip.Tracks.Count; t++)
                {
                    var track = clip.Tracks[t];
                    if (track == null) continue;
                    string sid = SpritePartIdUtility.Canonical(track.SlotId);
                    if (string.IsNullOrEmpty(sid) || sourceOrder.Contains(sid)) continue;
                    sourceOrder.Add(sid);
                }
            }

            var exact = SuggestSlotMap(source, destination, partsClipIndices);
            var exactSources = new HashSet<string>(StringComparer.Ordinal);
            var takenDestinations = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in exact)
            {
                exactSources.Add(kv.Key);
                takenDestinations.Add(SpritePartIdUtility.Canonical(kv.Value));
            }

            for (int i = 0; i < sourceOrder.Count; i++)
            {
                string sid = sourceOrder[i];
                if (exactSources.Contains(sid)) continue;
                var srcSlot = FindSlot(source, sid);
                if (srcSlot == null || string.IsNullOrWhiteSpace(srcSlot.Name)) continue;
                for (int d = 0; d < destination.PartsSlots.Count; d++)
                {
                    var dst = destination.PartsSlots[d];
                    if (dst == null || string.IsNullOrWhiteSpace(dst.Name)) continue;
                    if (!string.Equals(dst.Name.Trim(), srcSlot.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;
                    string destId = SpritePartIdUtility.Canonical(dst.SlotId, dst.Name);
                    if (takenDestinations.Contains(destId)) continue;
                    takenDestinations.Add(destId);
                    result.Add(new SlotNameSuggestion
                    {
                        SourceSlotId = sid,
                        DestinationSlotId = destId,
                        DestinationName = dst.Name,
                    });
                    break;
                }
            }
            return result;
        }

        public static HashSet<string> CollectRequiredSourceSlots(
            SpriteSheetProfile source, IEnumerable<int> partsClipIndices)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (source?.PartsClips == null || partsClipIndices == null) return set;
            foreach (int index in partsClipIndices)
            {
                if (index < 0 || index >= source.PartsClips.Count) continue;
                var clip = source.PartsClips[index];
                if (clip?.Tracks == null) continue;
                for (int t = 0; t < clip.Tracks.Count; t++)
                {
                    var track = clip.Tracks[t];
                    if (track == null) continue;
                    string sid = SpritePartIdUtility.Canonical(track.SlotId);
                    if (!string.IsNullOrEmpty(sid))
                        set.Add(sid);
                }
            }
            return set;
        }

        public static Plan PlanImport(
            SpriteSheetProfile source,
            SpriteSheetProfile destination,
            List<int> sheetIndices,
            List<int> appearanceIndices,
            List<int> frameClipIndices,
            List<int> partsClipIndices,
            Dictionary<string, string> slotMap,
            HashSet<string> excludedSourceSlotIds)
            => PlanImport(source, destination, sheetIndices, appearanceIndices, frameClipIndices,
                partsClipIndices, slotMap, excludedSourceSlotIds,
                SpriteProfileImportConflictPolicy.UniqueCopy);

        public static Plan PlanImport(
            SpriteSheetProfile source,
            SpriteSheetProfile destination,
            List<int> sheetIndices,
            List<int> appearanceIndices,
            List<int> frameClipIndices,
            List<int> partsClipIndices,
            Dictionary<string, string> slotMap,
            HashSet<string> excludedSourceSlotIds,
            SpriteProfileImportConflictPolicy conflictPolicy)
        {
            var plan = new Plan { ConflictPolicy = conflictPolicy };
            if (source == null || destination == null)
            {
                plan.Reason = "Source and destination profiles must not be null.";
                return plan;
            }
            if (ReferenceEquals(source, destination))
            {
                plan.Reason = "Source and destination are the same profile.";
                return plan;
            }

            sheetIndices ??= new List<int>();
            appearanceIndices ??= new List<int>();
            frameClipIndices ??= new List<int>();
            partsClipIndices ??= new List<int>();
            excludedSourceSlotIds ??= new HashSet<string>(StringComparer.Ordinal);
            slotMap ??= new Dictionary<string, string>(StringComparer.Ordinal);

            var neededSheets = new SortedSet<int>(sheetIndices);
            var neededApps = new SortedSet<int>(appearanceIndices);
            var srcApps = source.PartsAppearances ?? new List<SpritePartAppearanceDef>();
            var srcFrameClips = source.Clips ?? new List<SpriteClipDef>();
            var srcPartsClips = source.PartsClips ?? new List<SpritePartsClipDef>();

            for (int i = 0; i < frameClipIndices.Count; i++)
            {
                int index = frameClipIndices[i];
                if (index < 0 || index >= srcFrameClips.Count || srcFrameClips[index] == null)
                {
                    plan.Reason = string.Format("Frame clip index {0} is out of range.", index);
                    return plan;
                }
                var clip = srcFrameClips[index];
                if (clip.Frames == null || clip.Frames.Length == 0)
                {
                    plan.Reason = string.Format("Frame clip '{0}' has no frames.", clip.Name);
                    return plan;
                }
                neededSheets.Add(Mathf.Max(0, clip.SheetIndex));
            }

            // Frame clip dependency closure: event markers must resolve in the
            // destination Event catalog, and clip sockets must resolve to a
            // destination socket catalog entry (the bake reads SocketId from
            // it). Profile-level socket MOTION tracks are not clip data and
            // stay unimported (noted below, never silent).
            if (!PlanFrameClipEventClosure(source, srcFrameClips, frameClipIndices, destination, plan))
                return plan;
            PlanFrameClipSocketClosure(source, srcFrameClips, frameClipIndices, destination, plan);

            var requiredSlots = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < partsClipIndices.Count; i++)
            {
                int index = partsClipIndices[i];
                if (index < 0 || index >= srcPartsClips.Count || srcPartsClips[index] == null)
                {
                    plan.Reason = string.Format("Parts clip index {0} is out of range.", index);
                    return plan;
                }
                var clip = srcPartsClips[index];
                if (clip.Tracks == null) continue;
                for (int t = 0; t < clip.Tracks.Count; t++)
                {
                    var track = clip.Tracks[t];
                    if (track == null) continue;
                    string sid = SpritePartIdUtility.Canonical(track.SlotId);
                    if (string.IsNullOrEmpty(sid) || excludedSourceSlotIds.Contains(sid))
                        continue;
                    requiredSlots.Add(sid);
                    if (track.Keys == null) continue;
                    for (int k = 0; k < track.Keys.Count; k++)
                    {
                        var key = track.Keys[k];
                        if (key == null || string.IsNullOrWhiteSpace(key.AppearanceId))
                            continue;
                        string aid = SpritePartIdUtility.Canonical(key.AppearanceId);
                        int appIndex = FindAppearanceIndex(srcApps, aid);
                        if (appIndex < 0)
                        {
                            plan.Reason = string.Format(
                                "Parts clip '{0}' key appearance '{1}' is missing on the source.",
                                clip.Name, aid);
                            return plan;
                        }
                        neededApps.Add(appIndex);
                        neededSheets.Add(srcApps[appIndex].SheetIndex);
                    }
                }
            }

            foreach (string sid in requiredSlots)
            {
                if (!slotMap.TryGetValue(sid, out string dest) || string.IsNullOrWhiteSpace(dest))
                {
                    plan.Reason = string.Format(
                        "Parts clip import needs a SlotId map for '{0}'. Map it to a destination slot or exclude that track.",
                        sid);
                    return plan;
                }
                string destId = SpritePartIdUtility.Canonical(dest);
                if (FindSlot(destination, destId) == null)
                {
                    plan.Reason = string.Format(
                        "Mapped destination slot '{0}' (from '{1}') is not on the open profile.",
                        destId, sid);
                    return plan;
                }
                foreach (var kv in slotMap)
                {
                    if (kv.Key == sid) continue;
                    if (excludedSourceSlotIds.Contains(kv.Key)) continue;
                    if (!requiredSlots.Contains(kv.Key)) continue;
                    if (SpritePartIdUtility.Canonical(kv.Value) == destId)
                    {
                        plan.Reason = string.Format(
                            "Many-to-one SlotId map blocked: '{0}' and '{1}' both map to '{2}'.",
                            sid, kv.Key, destId);
                        return plan;
                    }
                }
                plan.SlotMap[sid] = destId;
            }

            plan.Art = SpriteProfileArtImport.PlanImport(
                source, destination, new List<int>(neededSheets), new List<int>(neededApps), conflictPolicy);
            if (!plan.Art.Ok)
            {
                plan.Reason = plan.Art.Reason;
                return plan;
            }
            plan.Notes.AddRange(plan.Art.Notes);

            var takenFrameNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destClips = destination.Clips ?? new List<SpriteClipDef>();
            for (int i = 0; i < destClips.Count; i++)
                if (destClips[i] != null && !string.IsNullOrWhiteSpace(destClips[i].Name))
                    takenFrameNames.Add(destClips[i].Name);

            for (int i = 0; i < frameClipIndices.Count; i++)
            {
                int index = frameClipIndices[i];
                var clip = srcFrameClips[index];
                var action = new FrameClipAction
                {
                    SourceClipIndex = index,
                    SourceName = clip.Name,
                    SourceSheetIndex = clip.SheetIndex,
                };
                int conflict = conflictPolicy == SpriteProfileImportConflictPolicy.ReplaceExisting
                    ? FindFrameClipByName(destClips, clip.Name)
                    : -1;
                if (conflict >= 0)
                {
                    // Replace keeps the destination clip's name and list slot, so
                    // name references (hitboxes) and index references (OnComplete)
                    // keep resolving. The plan previews what uses it.
                    action.ReplaceExisting = true;
                    action.DestinationClipIndex = conflict;
                    action.DestinationName = destClips[conflict].Name;
                    CollectFrameClipUsedBy(destination, action.DestinationName, conflict, action.UsedBy);
                    plan.FrameClipsReplaced++;
                }
                else
                {
                    action.DestinationName = UniqueName(clip.Name, "Clip", takenFrameNames);
                    takenFrameNames.Add(action.DestinationName);
                }
                plan.FrameClips.Add(action);
            }

            var takenPartsNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var takenPartsIds = new HashSet<string>(StringComparer.Ordinal);
            var destParts = destination.PartsClips ?? new List<SpritePartsClipDef>();
            for (int i = 0; i < destParts.Count; i++)
            {
                var c = destParts[i];
                if (c == null) continue;
                if (!string.IsNullOrWhiteSpace(c.Name)) takenPartsNames.Add(c.Name);
                takenPartsIds.Add(SpritePartIdUtility.Canonical(c.ClipId, c.Name));
            }

            for (int i = 0; i < partsClipIndices.Count; i++)
            {
                int index = partsClipIndices[i];
                var clip = srcPartsClips[index];
                string sourceId = SpritePartIdUtility.Canonical(clip.ClipId, clip.Name);
                var action = new PartsClipAction
                {
                    SourceClipIndex = index,
                    SourceName = clip.Name,
                    SourceClipId = sourceId,
                };
                int conflict = -1;
                if (conflictPolicy == SpriteProfileImportConflictPolicy.ReplaceExisting)
                {
                    for (int d = 0; d < destParts.Count; d++)
                    {
                        if (destParts[d] != null &&
                            SpritePartIdUtility.Canonical(destParts[d].ClipId, destParts[d].Name) == sourceId)
                        {
                            conflict = d;
                            break;
                        }
                    }
                }
                if (conflict >= 0)
                {
                    // Replace keeps the destination ClipId, display name and slot;
                    // duration/speed/wrap/tracks come from the source via the slot map.
                    action.ReplaceExisting = true;
                    action.DestinationClipListIndex = conflict;
                    action.DestinationName = destParts[conflict].Name;
                    action.DestinationClipId = SpritePartIdUtility.Canonical(
                        destParts[conflict].ClipId, destParts[conflict].Name);
                    CollectPartsClipUsedBy(destination, action.DestinationClipId, destParts[conflict], action.UsedBy);
                    plan.PartsClipsReplaced++;
                }
                else
                {
                    action.DestinationClipId = UniqueId(sourceId, takenPartsIds);
                    action.DestinationName = UniqueName(clip.Name, action.DestinationClipId, takenPartsNames);
                    takenPartsIds.Add(action.DestinationClipId);
                    takenPartsNames.Add(action.DestinationName);
                }
                foreach (string sid in excludedSourceSlotIds)
                    action.ExcludedSourceSlotIds.Add(sid);
                plan.PartsClips.Add(action);
            }

            if (plan.FrameClips.Count == 0 && plan.PartsClips.Count == 0 &&
                plan.Art.Sheets.Count == 0 && plan.Art.Appearances.Count == 0)
            {
                plan.Reason = "Select at least one sheet, appearance, frame clip, or Parts clip.";
                return plan;
            }

            plan.Ok = true;
            return plan;
        }

        /// <summary>
        /// Event closure: every event id used by the selected frame clips must
        /// keep its meaning on the destination. Same id + same name stays; a
        /// destination entry with the same NAME remaps the marker id; anything
        /// else appends a copy of the source catalog entry under a free id.
        /// Read-only; fails the plan only when the destination catalog is full.
        /// </summary>
        static bool PlanFrameClipEventClosure(
            SpriteSheetProfile source,
            List<SpriteClipDef> srcFrameClips,
            List<int> frameClipIndices,
            SpriteSheetProfile destination,
            Plan plan)
        {
            var usedIds = new SortedSet<byte>();
            foreach (int index in frameClipIndices)
            {
                var clip = srcFrameClips[index];
                if (clip?.EventMarkers != null)
                {
                    for (int m = 0; m < clip.EventMarkers.Count; m++)
                    {
                        var marker = clip.EventMarkers[m];
                        if (marker != null && marker.EventId != 0)
                            usedIds.Add(marker.EventId);
                    }
                }
                if (clip?.EventIds != null)
                {
                    for (int e = 0; e < clip.EventIds.Length; e++)
                    {
                        if (clip.EventIds[e] != 0)
                            usedIds.Add(clip.EventIds[e]);
                    }
                }
            }
            if (usedIds.Count == 0)
                return true;

            var sourceById = new Dictionary<byte, SpriteEventDef>();
            if (source.Events != null)
            {
                for (int i = 0; i < source.Events.Count; i++)
                {
                    var e = source.Events[i];
                    if (e != null && !sourceById.ContainsKey(e.Id))
                        sourceById[e.Id] = e;
                }
            }
            var destById = new Dictionary<byte, SpriteEventDef>();
            var destByName = new Dictionary<string, SpriteEventDef>(StringComparer.OrdinalIgnoreCase);
            if (destination.Events != null)
            {
                for (int i = 0; i < destination.Events.Count; i++)
                {
                    var e = destination.Events[i];
                    if (e == null || destById.ContainsKey(e.Id)) continue;
                    destById[e.Id] = e;
                    if (!string.IsNullOrWhiteSpace(e.Name) && !destByName.ContainsKey(e.Name))
                        destByName[e.Name] = e;
                }
            }
            var plannedIds = new HashSet<byte>();
            int remapped = 0;
            int added = 0;
            foreach (byte sourceId in usedIds)
            {
                sourceById.TryGetValue(sourceId, out var srcEntry);
                if (srcEntry == null)
                {
                    plan.Reason = string.Format(
                        "Event id {0} has no catalog entry on the source profile; cannot preserve meaning. " +
                        "Deselect this frame clip to import art only, or add the event to the source catalog.",
                        sourceId);
                    return false;
                }

                if (destById.TryGetValue(sourceId, out var sameId) &&
                    string.Equals(sameId.Name, srcEntry.Name, StringComparison.OrdinalIgnoreCase))
                {
                    plan.EventIdRemap[sourceId] = sourceId;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(srcEntry.Name) &&
                    destByName.TryGetValue(srcEntry.Name, out var byName))
                {
                    plan.EventIdRemap[sourceId] = byName.Id;
                    remapped++;
                    continue;
                }

                byte freeId = 0;
                for (int candidate = 1; candidate <= byte.MaxValue; candidate++)
                {
                    var id = (byte)candidate;
                    if (destById.ContainsKey(id) || plannedIds.Contains(id)) continue;
                    freeId = id;
                    break;
                }
                if (freeId == 0)
                {
                    plan.Reason = string.Format(
                        "Destination Event catalog is full (ids 1-255 in use); cannot import event '{0}' (id {1}). " +
                        "Deselect this frame clip to import art only, or free an event id.",
                        srcEntry.Name, sourceId);
                    return false;
                }
                plannedIds.Add(freeId);
                plan.EventsToAdd.Add(new EventCatalogAction
                {
                    DestinationId = freeId,
                    Name = srcEntry.Name ?? string.Empty,
                    Color = srcEntry.Color,
                });
                plan.EventIdRemap[sourceId] = freeId;
                added++;
            }
            if (remapped > 0)
                plan.Notes.Add(string.Format(
                    "{0} event id(s) remapped onto existing destination catalog entries (matched by name).",
                    remapped));
            if (added > 0)
                plan.Notes.Add(string.Format(
                    "{0} event catalog entrie(s) copied from the source so imported markers keep their meaning.",
                    added));
            return true;
        }

        /// <summary>
        /// Socket closure: the bake resolves clip sockets' stable SocketId
        /// through the destination socket catalog, so each socket name used by
        /// the selected clips must exist there. Existing destination entries
        /// keep their identity; missing names copy the source entry (its
        /// profile back-reference is blanked - no cross-profile references).
        /// </summary>
        static void PlanFrameClipSocketClosure(
            SpriteSheetProfile source,
            List<SpriteClipDef> srcFrameClips,
            List<int> frameClipIndices,
            SpriteSheetProfile destination,
            Plan plan)
        {
            var names = new List<string>();
            foreach (int index in frameClipIndices)
            {
                var sockets = srcFrameClips[index]?.Sockets;
                if (sockets == null) continue;
                for (int s = 0; s < sockets.Count; s++)
                {
                    var socket = sockets[s];
                    if (socket == null || string.IsNullOrWhiteSpace(socket.Name)) continue;
                    string canonical = SpriteSocketKeys.CanonicalName(socket.Name);
                    if (!names.Contains(canonical))
                        names.Add(canonical);
                }
            }
            if (names.Count == 0)
                return;

            var takenSocketIds = new HashSet<string>(StringComparer.Ordinal);
            var destCatalog = destination.SocketCatalog;
            if (destCatalog?.Items != null)
            {
                for (int i = 0; i < destCatalog.Items.Count; i++)
                {
                    var item = destCatalog.Items[i];
                    if (item != null)
                        takenSocketIds.Add(SpriteSocketIdUtility.Canonical(item.SocketId, item.SocketName));
                }
            }
            int kept = 0;
            int copied = 0;
            foreach (string name in names)
            {
                var existing = FindSocketCatalogItem(destCatalog, name);
                if (existing != null)
                {
                    plan.SocketCatalogActions.Add(new SocketCatalogAction
                    {
                        SocketName = name,
                        KeepExisting = true,
                        DestinationSocketId = SpriteSocketIdUtility.Canonical(existing.SocketId, existing.SocketName),
                    });
                    kept++;
                    continue;
                }

                var srcItem = FindSocketCatalogItem(source.SocketCatalog, name);
                var copy = srcItem == null
                    ? new SpriteSocketCatalogItem { SocketName = name }
                    : new SpriteSocketCatalogItem
                    {
                        SocketName = srcItem.SocketName,
                        SocketId = srcItem.SocketId ?? string.Empty,
                        Texture = srcItem.Texture,
                        Profile = null, // never create a cross-profile reference
                        ClipName = srcItem.ClipName ?? string.Empty,
                        PlayMode = srcItem.PlayMode,
                        Columns = srcItem.Columns,
                        Rows = srcItem.Rows,
                        Pivot = srcItem.Pivot,
                        CellIndex = srcItem.CellIndex,
                        GripPixels = srcItem.GripPixels,
                        Scale = srcItem.Scale,
                        FlipX = srcItem.FlipX,
                        SortingOffset = srcItem.SortingOffset,
                        PreviewEnabled = srcItem.PreviewEnabled,
                        Locked = srcItem.Locked,
                        PathWrap = srcItem.PathWrap,
                        MotionMode = srcItem.MotionMode,
                        Speed = srcItem.Speed,
                    };
                string root = SpriteSocketIdUtility.Canonical(copy.SocketId, name);
                string unique = root;
                int suffix = 2;
                while (takenSocketIds.Contains(unique))
                    unique = root + "." + suffix++;
                takenSocketIds.Add(unique);
                copy.SocketId = unique;
                plan.SocketCatalogActions.Add(new SocketCatalogAction
                {
                    SocketName = name,
                    KeepExisting = false,
                    ItemToAdd = copy,
                    DestinationSocketId = unique,
                });
                copied++;
            }
            if (kept > 0)
                plan.Notes.Add(string.Format(
                    "{0} clip socket name(s) resolve to existing destination socket catalog entries.", kept));
            if (copied > 0)
                plan.Notes.Add(string.Format(
                    "{0} socket catalog entrie(s) copied so clip sockets keep a stable SocketId (profile back-references blanked).",
                    copied));
            plan.Notes.Add(
                "Profile-level socket motion tracks and socket inventories are not clip data and are not imported.");
        }

        public static ApplyResult Apply(
            Plan plan, SpriteSheetProfile source, SpriteSheetProfile destination,
            SpriteProfileArtImport.ImportSourceInfo sourceInfo = default, string importedUtc = null)
        {
            if (plan == null || !plan.Ok)
                return new ApplyResult { Reason = plan?.Reason ?? "Plan is null." };
            if (source == null || destination == null)
                return new ApplyResult { Reason = "Source and destination profiles must not be null." };

            var frameIdx = new List<int>();
            for (int i = 0; i < plan.FrameClips.Count; i++)
                frameIdx.Add(plan.FrameClips[i].SourceClipIndex);
            var partsIdx = new List<int>();
            for (int i = 0; i < plan.PartsClips.Count; i++)
                partsIdx.Add(plan.PartsClips[i].SourceClipIndex);
            var excluded = new HashSet<string>(StringComparer.Ordinal);
            if (plan.PartsClips.Count > 0)
            {
                for (int e = 0; e < plan.PartsClips[0].ExcludedSourceSlotIds.Count; e++)
                    excluded.Add(plan.PartsClips[0].ExcludedSourceSlotIds[e]);
            }

            var sheetSel = new List<int>();
            for (int i = 0; i < plan.Art.Sheets.Count; i++)
                sheetSel.Add(plan.Art.Sheets[i].SourceSheetIndex);
            var appSel = new List<int>();
            for (int i = 0; i < plan.Art.Appearances.Count; i++)
                appSel.Add(plan.Art.Appearances[i].SourceAppearanceIndex);

            var applied = PlanImport(
                source, destination, sheetSel, appSel, frameIdx, partsIdx, plan.SlotMap, excluded,
                plan.ConflictPolicy);
            if (!applied.Ok)
                return new ApplyResult { Reason = applied.Reason };

            destination.Sheets ??= new List<SpriteSheetDef>();
            destination.PartsAppearances ??= new List<SpritePartAppearanceDef>();
            destination.Clips ??= new List<SpriteClipDef>();
            destination.PartsClips ??= new List<SpritePartsClipDef>();

            int sheetMark = destination.Sheets.Count;
            int appMark = destination.PartsAppearances.Count;
            int frameMark = destination.Clips.Count;
            int partsMark = destination.PartsClips.Count;
            bool eventsWasNull = destination.Events == null;
            int eventMark = destination.Events?.Count ?? 0;
            bool socketCatalogWasNull = destination.SocketCatalog == null;
            int socketCatalogMark = destination.SocketCatalog?.Items?.Count ?? 0;
            var replacedFrameClips = new List<KeyValuePair<int, SpriteClipDef>>();
            var replacedPartsClips = new List<KeyValuePair<int, SpritePartsClipDef>>();

            try
            {
                var artResult = SpriteProfileArtImport.Apply(applied.Art, source, destination,
                    sourceInfo, importedUtc);
                if (!artResult.Ok)
                    throw new InvalidOperationException(artResult.Reason ?? "Art import failed.");

                var appearanceRemap = new Dictionary<string, string>(StringComparer.Ordinal);
                if (artResult.AppearanceIdRemaps != null)
                {
                    for (int i = 0; i < artResult.AppearanceIdRemaps.Count; i++)
                        appearanceRemap[artResult.AppearanceIdRemaps[i].Key] = artResult.AppearanceIdRemaps[i].Value;
                }

                // Event catalog closure: appended entries give remapped marker
                // ids their meaning; socket catalog closure gives clip sockets
                // their stable SocketId (read by the bake).
                if (applied.EventsToAdd.Count > 0)
                {
                    destination.Events ??= new List<SpriteEventDef>();
                    for (int i = 0; i < applied.EventsToAdd.Count; i++)
                    {
                        var entry = applied.EventsToAdd[i];
                        destination.Events.Add(new SpriteEventDef
                        {
                            Id = entry.DestinationId,
                            Name = entry.Name,
                            Color = entry.Color,
                        });
                    }
                }
                if (applied.SocketCatalogActions != null)
                {
                    for (int i = 0; i < applied.SocketCatalogActions.Count; i++)
                    {
                        var action = applied.SocketCatalogActions[i];
                        if (action.KeepExisting || action.ItemToAdd == null) continue;
                        destination.SocketCatalog ??= new SpriteSocketCatalog();
                        destination.SocketCatalog.EnsureItems();
                        destination.SocketCatalog.Items.Add(action.ItemToAdd);
                    }
                }

                var sheetMap = new Dictionary<int, int>();
                for (int i = 0; i < applied.Art.Sheets.Count; i++)
                {
                    var a = applied.Art.Sheets[i];
                    sheetMap[a.SourceSheetIndex] = a.DestinationSheetIndex;
                }

                var frameIndexRemap = new Dictionary<int, int>();
                for (int i = 0; i < applied.FrameClips.Count; i++)
                {
                    var action = applied.FrameClips[i];
                    var src = source.Clips[action.SourceClipIndex];
                    if (!sheetMap.TryGetValue(src.SheetIndex, out int destSheet))
                        destSheet = sheetMap.ContainsKey(0) ? sheetMap[0] : 0;
                    var copy = CloneFrameClip(src, action.DestinationName, destSheet, applied.EventIdRemap);
                    copy.Import = SpriteProfileArtImport.StampProvenance(sourceInfo,
                        $"frame clip '{action.SourceName}'", importedUtc);
                    if (action.ReplaceExisting)
                    {
                        if (action.DestinationClipIndex < 0 || action.DestinationClipIndex >= destination.Clips.Count)
                            throw new InvalidOperationException(
                                $"Replace target for frame clip '{action.SourceName}' is out of range.");
                        replacedFrameClips.Add(new KeyValuePair<int, SpriteClipDef>(
                            action.DestinationClipIndex, destination.Clips[action.DestinationClipIndex]));
                        destination.Clips[action.DestinationClipIndex] = copy;
                        frameIndexRemap[action.SourceClipIndex] = action.DestinationClipIndex;
                    }
                    else
                    {
                        destination.Clips.Add(copy);
                        frameIndexRemap[action.SourceClipIndex] = destination.Clips.Count - 1;
                    }
                }
                foreach (var kv in frameIndexRemap)
                {
                    var destClip = destination.Clips[kv.Value];
                    int srcComplete = source.Clips[kv.Key].OnCompleteClipIndex;
                    if (srcComplete >= 0 && frameIndexRemap.TryGetValue(srcComplete, out int destComplete))
                        destClip.OnCompleteClipIndex = destComplete;
                    else
                        destClip.OnCompleteClipIndex = -1;
                }

                for (int i = 0; i < applied.PartsClips.Count; i++)
                {
                    var action = applied.PartsClips[i];
                    var src = source.PartsClips[action.SourceClipIndex];
                    var excludedSet = new HashSet<string>(action.ExcludedSourceSlotIds, StringComparer.Ordinal);
                    var copy = ClonePartsClip(
                        src, action.DestinationName, action.DestinationClipId,
                        applied.SlotMap, excludedSet, appearanceRemap);
                    copy.Import = SpriteProfileArtImport.StampProvenance(sourceInfo,
                        $"Parts clip '{action.SourceName}' [{action.SourceClipId}]", importedUtc);
                    if (action.ReplaceExisting)
                    {
                        if (action.DestinationClipListIndex < 0 ||
                            action.DestinationClipListIndex >= destination.PartsClips.Count)
                            throw new InvalidOperationException(
                                $"Replace target for Parts clip '{action.SourceName}' is out of range.");
                        replacedPartsClips.Add(new KeyValuePair<int, SpritePartsClipDef>(
                            action.DestinationClipListIndex, destination.PartsClips[action.DestinationClipListIndex]));
                        destination.PartsClips[action.DestinationClipListIndex] = copy;
                    }
                    else
                    {
                        destination.PartsClips.Add(copy);
                    }
                }

                SpritePartsValidation.CanonicalizeIds(destination);
                return new ApplyResult
                {
                    Ok = true,
                    Summary = applied.Summary,
                    AppearanceIdRemaps = artResult.AppearanceIdRemaps ?? new List<KeyValuePair<string, string>>(),
                };
            }
            catch (Exception ex)
            {
                for (int i = replacedPartsClips.Count - 1; i >= 0; i--)
                    destination.PartsClips[replacedPartsClips[i].Key] = replacedPartsClips[i].Value;
                for (int i = replacedFrameClips.Count - 1; i >= 0; i--)
                    destination.Clips[replacedFrameClips[i].Key] = replacedFrameClips[i].Value;
                while (destination.PartsClips.Count > partsMark)
                    destination.PartsClips.RemoveAt(destination.PartsClips.Count - 1);
                while (destination.Clips.Count > frameMark)
                    destination.Clips.RemoveAt(destination.Clips.Count - 1);
                while (destination.PartsAppearances.Count > appMark)
                    destination.PartsAppearances.RemoveAt(destination.PartsAppearances.Count - 1);
                while (destination.Sheets.Count > sheetMark)
                    destination.Sheets.RemoveAt(destination.Sheets.Count - 1);
                if (eventsWasNull)
                    destination.Events = null;
                else if (destination.Events != null)
                {
                    while (destination.Events.Count > eventMark)
                        destination.Events.RemoveAt(destination.Events.Count - 1);
                }
                if (socketCatalogWasNull)
                    destination.SocketCatalog = null;
                else if (destination.SocketCatalog?.Items != null)
                {
                    while (destination.SocketCatalog.Items.Count > socketCatalogMark)
                        destination.SocketCatalog.Items.RemoveAt(destination.SocketCatalog.Items.Count - 1);
                }
                return new ApplyResult { Reason = "Import aborted, destination unchanged: " + ex.Message };
            }
        }

        static SpriteClipDef CloneFrameClip(
            SpriteClipDef src, string name, int sheetIndex, Dictionary<byte, byte> eventIdRemap)
        {
            if (src == null || src.Frames == null || src.Frames.Length == 0)
                throw new InvalidOperationException("Frame clip has no frames.");
            var copy = new SpriteClipDef
            {
                Name = name,
                SheetIndex = sheetIndex,
                Row = src.Row,
                Frames = (int[])src.Frames.Clone(),
                FrameRows = src.FrameRows == null ? null : (int[])src.FrameRows.Clone(),
                FrameRate = src.FrameRate,
                WrapMode = src.WrapMode,
                Interrupt = src.Interrupt,
                CancelAfter = src.CancelAfter,
                Priority = src.Priority,
                OnCompleteClipIndex = -1,
                ComboWindowStartFrame = src.ComboWindowStartFrame,
                ComboWindowEndFrame = src.ComboWindowEndFrame,
                ComboWindowPriorityBoost = src.ComboWindowPriorityBoost,
                FrameDurationScales = src.FrameDurationScales == null ? null : (float[])src.FrameDurationScales.Clone(),
                EventIds = src.EventIds == null ? null : RemapEventIds(src.EventIds, eventIdRemap),
                EventNormalizedTimes = src.EventNormalizedTimes == null ? null : (float[])src.EventNormalizedTimes.Clone(),
                OnionOffsets = src.OnionOffsets == null ? null : (Vector2[])src.OnionOffsets.Clone(),
                FrameScales = src.FrameScales == null ? null : (Vector2[])src.FrameScales.Clone(),
                FrameRotations = src.FrameRotations == null ? null : (float[])src.FrameRotations.Clone(),
                FrameTweenModes = src.FrameTweenModes == null ? null : (byte[])src.FrameTweenModes.Clone(),
                FacingGroup = src.FacingGroup ?? string.Empty,
                Facing = src.Facing,
                Sockets = new List<FrameSocketDef>(),
                EventMarkers = new List<SpriteClipEventMarker>(),
            };
            if (src.Sockets != null)
            {
                for (int i = 0; i < src.Sockets.Count; i++)
                {
                    var s = src.Sockets[i];
                    if (s == null) continue;
                    copy.Sockets.Add(new FrameSocketDef
                    {
                        Name = s.Name,
                        FrameIndex = s.FrameIndex,
                        LocalPosition = s.LocalPosition,
                        LocalAngle = s.LocalAngle,
                        LocalScale = s.LocalScale,
                        DrawLayer = s.DrawLayer,
                    });
                }
            }
            if (src.EventMarkers != null)
            {
                for (int i = 0; i < src.EventMarkers.Count; i++)
                {
                    var m = src.EventMarkers[i];
                    if (m == null) continue;
                    var marker = new SpriteClipEventMarker
                    {
                        FrameIndex = m.FrameIndex,
                        NormalizedTime = m.NormalizedTime,
                        EventId = RemapEventId(m.EventId, eventIdRemap),
                        FireMode = m.FireMode,
                        IntPayload = m.IntPayload,
                        FloatPayload = m.FloatPayload,
                        TextPayload = m.TextPayload ?? string.Empty,
                        Payloads = new List<SpriteEventPayloadEntry>(),
                    };
                    if (m.Payloads != null)
                    {
                        for (int p = 0; p < m.Payloads.Count; p++)
                        {
                            var e = m.Payloads[p];
                            if (e == null) continue;
                            marker.Payloads.Add(new SpriteEventPayloadEntry
                            {
                                Kind = e.Kind,
                                IntValue = e.IntValue,
                                FloatValue = e.FloatValue,
                                TextValue = e.TextValue,
                            });
                        }
                    }
                    copy.EventMarkers.Add(marker);
                }
            }
            copy.EnsureFrameData();
            return copy;
        }

        static SpritePartsClipDef ClonePartsClip(
            SpritePartsClipDef src,
            string name,
            string clipId,
            Dictionary<string, string> slotMap,
            HashSet<string> excluded,
            Dictionary<string, string> appearanceRemap)
        {
            var copy = new SpritePartsClipDef
            {
                Name = name,
                ClipId = clipId,
                Duration = src.Duration,
                Speed = src.Speed,
                WrapMode = src.WrapMode,
                Tracks = new List<SpritePartsTrackDef>(),
            };
            if (src.Tracks == null) return copy;
            for (int t = 0; t < src.Tracks.Count; t++)
            {
                var track = src.Tracks[t];
                if (track == null) continue;
                string sid = SpritePartIdUtility.Canonical(track.SlotId);
                if (excluded.Contains(sid)) continue;
                if (!slotMap.TryGetValue(sid, out string destSlot))
                    continue;
                var destTrack = new SpritePartsTrackDef
                {
                    SlotId = destSlot,
                    Kind = track.Kind,
                    Keys = new List<SpritePartsKeyDef>(),
                };
                if (track.Keys != null)
                {
                    for (int k = 0; k < track.Keys.Count; k++)
                    {
                        var key = track.Keys[k];
                        if (key == null) continue;
                        string aid = key.AppearanceId ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(aid))
                        {
                            string canon = SpritePartIdUtility.Canonical(aid);
                            if (appearanceRemap.TryGetValue(canon, out string remapped))
                                aid = remapped;
                            else
                                aid = canon;
                        }
                        destTrack.Keys.Add(new SpritePartsKeyDef
                        {
                            Time = key.Time,
                            Position = key.Position,
                            Rotation = key.Rotation,
                            Scale = key.Scale,
                            Deform = key.Deform == null ? null : (Vector2[])key.Deform.Clone(),
                            EaseMode = key.EaseMode,
                            HasColor = key.HasColor,
                            Color = key.Color,
                            HasDrawOrder = key.HasDrawOrder,
                            DrawOrder = key.DrawOrder,
                            Channels = key.Channels,
                            Curve = key.Curve,
                            AppearanceId = aid,
                        });
                    }
                }
                copy.Tracks.Add(destTrack);
            }
            return copy;
        }

        static byte RemapEventId(byte id, Dictionary<byte, byte> remap)
        {
            return id != 0 && remap != null && remap.TryGetValue(id, out byte remapped)
                ? remapped
                : id;
        }

        static byte[] RemapEventIds(byte[] ids, Dictionary<byte, byte> remap)
        {
            var copy = (byte[])ids.Clone();
            for (int i = 0; i < copy.Length; i++)
                copy[i] = RemapEventId(copy[i], remap);
            return copy;
        }


        /// <summary>Read-only catalog lookup. Does not EnsureItems, so planning stays immutable.</summary>
        static SpriteSocketCatalogItem FindSocketCatalogItem(SpriteSocketCatalog catalog, string name)
        {
            if (catalog?.Items == null) return null;
            for (int i = 0; i < catalog.Items.Count; i++)
            {
                var item = catalog.Items[i];
                if (item != null && SpriteSocketKeys.NamesEqual(item.SocketName, name))
                    return item;
            }
            return null;
        }

        static int FindAppearanceIndex(List<SpritePartAppearanceDef> apps, string appearanceId)
        {
            for (int i = 0; i < apps.Count; i++)
            {
                var app = apps[i];
                if (app == null) continue;
                if (SpritePartIdUtility.Canonical(app.AppearanceId, app.Name) == appearanceId)
                    return i;
            }
            return -1;
        }

        static SpritePartSlotDef FindSlot(SpriteSheetProfile profile, string slotId)
        {
            if (profile?.PartsSlots == null) return null;
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var slot = profile.PartsSlots[i];
                if (slot == null) continue;
                if (SpritePartIdUtility.Canonical(slot.SlotId, slot.Name) == slotId)
                    return slot;
            }
            return null;
        }

        static int FindFrameClipByName(List<SpriteClipDef> destClips, string name)
        {
            for (int i = 0; i < destClips.Count; i++)
            {
                var clip = destClips[i];
                if (clip != null && !string.IsNullOrWhiteSpace(clip.Name) &&
                    string.Equals(clip.Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Read-only preview of destination references to a frame clip: hitboxes
        /// bound to its name, character-lifetime filters mentioning it, and
        /// other clips whose OnComplete chain points at its list slot.
        /// </summary>
        static void CollectFrameClipUsedBy(
            SpriteSheetProfile destination, string clipName, int destClipIndex, List<string> usedBy)
        {
            int hitboxes = 0;
            int characterFilters = 0;
            if (destination.Hitboxes != null)
            {
                for (int i = 0; i < destination.Hitboxes.Count; i++)
                {
                    var box = destination.Hitboxes[i];
                    if (box == null) continue;
                    if (box.IsCharacter)
                    {
                        if (ListMentions(box.CharacterIncludeClips, clipName) ||
                            ListMentions(box.CharacterExcludeClips, clipName))
                            characterFilters++;
                    }
                    else if (string.Equals(box.ClipName, clipName, StringComparison.Ordinal))
                    {
                        hitboxes++;
                    }
                }
            }
            if (hitboxes > 0)
                usedBy.Add($"{hitboxes} hitbox(es) bound to this clip name");
            if (characterFilters > 0)
                usedBy.Add($"{characterFilters} character hitbox filter(s) mention this clip");
            if (destination.Clips != null)
            {
                for (int i = 0; i < destination.Clips.Count; i++)
                {
                    if (i == destClipIndex) continue;
                    var clip = destination.Clips[i];
                    if (clip != null && clip.OnCompleteClipIndex == destClipIndex)
                        usedBy.Add($"clip '{clip.Name}' chains into this clip (OnComplete)");
                }
            }
        }

        static bool ListMentions(List<string> names, string clipName)
        {
            if (names == null) return false;
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], clipName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>Read-only preview of destination references to a Parts clip id.</summary>
        static void CollectPartsClipUsedBy(
            SpriteSheetProfile destination, string canonicalClipId, SpritePartsClipDef destClip, List<string> usedBy)
        {
            if (!string.IsNullOrWhiteSpace(destination.PartsDefaultClipId) &&
                SpritePartIdUtility.Canonical(destination.PartsDefaultClipId) == canonicalClipId)
            {
                usedBy.Add("this is the default Parts clip");
            }
            int tracks = destClip?.Tracks?.Count ?? 0;
            int keys = 0;
            if (destClip?.Tracks != null)
            {
                for (int t = 0; t < destClip.Tracks.Count; t++)
                    keys += destClip.Tracks[t]?.Keys?.Count ?? 0;
            }
            usedBy.Add($"current content: {tracks} track(s), {keys} key(s) will be overwritten");
        }

        static string UniqueId(string desired, HashSet<string> taken)
        {
            string id = desired;
            int n = 2;
            while (taken.Contains(id))
                id = desired + n++;
            return id;
        }

        static string UniqueName(string sourceName, string fallback, HashSet<string> taken)
        {
            string baseName = string.IsNullOrWhiteSpace(sourceName) ? fallback : sourceName.Trim();
            if (!taken.Contains(baseName))
                return baseName;
            string name = baseName + " 2";
            int n = 3;
            while (taken.Contains(name))
                name = baseName + " " + n++;
            return name;
        }
    }
}

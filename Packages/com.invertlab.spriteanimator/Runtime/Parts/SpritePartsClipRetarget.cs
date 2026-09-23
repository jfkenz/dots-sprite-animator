using System;
using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Explicit Parts clip retarget: remap keyed local TRS from a source clip
    /// onto a destination rig/rest. This adapts keys to a different rest or
    /// hierarchy; it is not Spine-style retargeting, IK, or mesh deformation.
    /// Destination rest and hierarchy are never rewritten. Unresolved slot maps
    /// block Apply.
    /// </summary>
    public static class SpritePartsClipRetarget
    {
        public enum RestDeltaMode : byte
        {
            /// <summary>Copy keys as authored (absolute parent-local). Dest rest differences shift world motion.</summary>
            CopyLocal = 0,
            /// <summary>Remap each key as destRest ⊕ (key ⊖ sourceRest) so rest-only differences stay sensible.</summary>
            RestDelta = 1,
        }

        public sealed class Plan
        {
            public bool Ok;
            public string Reason;
            public int SourceClipIndex = -1;
            public string SourceClipName;
            public string SourceClipId;
            public string DestinationName;
            public string DestinationClipId;
            public RestDeltaMode Mode = RestDeltaMode.RestDelta;
            public readonly Dictionary<string, string> SlotMap =
                new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly List<string> Notes = new List<string>();
            public readonly List<SpriteProfileClipImport.SlotMapMismatch> Mismatches =
                new List<SpriteProfileClipImport.SlotMapMismatch>();
            public int KeyCount;
            public int TrackCount;

            public string Summary =>
                Ok
                    ? $"Retarget '{SourceClipName}' → '{DestinationName}' ({Mode}): {TrackCount} track(s), {KeyCount} key(s)"
                    : (Reason ?? "Invalid plan.");
        }

        public struct ApplyResult
        {
            public bool Ok;
            public string Reason;
            public string Summary;
            public int DestinationClipIndex;
        }

        public static Dictionary<string, string> SuggestSlotMap(
            SpriteSheetProfile source, SpriteSheetProfile destination, int sourceClipIndex)
            => SpriteProfileClipImport.SuggestSlotMap(
                source, destination, new[] { sourceClipIndex });

        public static List<SpriteProfileClipImport.SlotNameSuggestion> SuggestSlotMapByName(
            SpriteSheetProfile source, SpriteSheetProfile destination, int sourceClipIndex)
            => SpriteProfileClipImport.SuggestSlotMapByName(
                source, destination, new[] { sourceClipIndex });

        public static Plan PlanRetarget(
            SpriteSheetProfile source,
            int sourceClipIndex,
            SpriteSheetProfile destination,
            Dictionary<string, string> slotMap,
            RestDeltaMode mode = RestDeltaMode.RestDelta)
        {
            var plan = new Plan
            {
                SourceClipIndex = sourceClipIndex,
                Mode = mode,
            };
            if (source == null || destination == null)
            {
                plan.Reason = "Source and destination profiles must not be null.";
                return plan;
            }
            if (source.PartsClips == null || sourceClipIndex < 0 ||
                sourceClipIndex >= source.PartsClips.Count ||
                source.PartsClips[sourceClipIndex] == null)
            {
                plan.Reason = "Source Parts clip is missing.";
                return plan;
            }

            var srcClip = source.PartsClips[sourceClipIndex];
            plan.SourceClipName = srcClip.Name;
            plan.SourceClipId = SpritePartIdUtility.Canonical(srcClip.ClipId, srcClip.Name);
            slotMap ??= new Dictionary<string, string>(StringComparer.Ordinal);

            var required = SpriteProfileClipImport.CollectRequiredSourceSlots(
                source, new[] { sourceClipIndex });
            if (required.Count == 0)
            {
                plan.Notes.Add("Source clip has no tracks; retarget will create an empty clip.");
            }

            var usedDest = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string sid in required)
            {
                if (!TryResolveMap(slotMap, sid, out string destId) || string.IsNullOrWhiteSpace(destId))
                {
                    plan.Reason =
                        $"Unresolved slot map for '{sid}'. Map it to a destination slot before Apply.";
                    return plan;
                }
                destId = SpritePartIdUtility.Canonical(destId);
                if (FindSlot(destination, destId) == null)
                {
                    plan.Reason =
                        $"Mapped destination slot '{destId}' (from '{sid}') is not on the destination rig.";
                    return plan;
                }
                if (usedDest.TryGetValue(destId, out string other))
                {
                    plan.Reason =
                        $"Many-to-one SlotId map blocked: '{other}' and '{sid}' both map to '{destId}'.";
                    return plan;
                }
                usedDest[destId] = sid;
                plan.SlotMap[sid] = destId;
            }

            plan.Mismatches.AddRange(
                SpriteProfileClipImport.CollectSlotMapMismatches(source, destination, plan.SlotMap));
            for (int i = 0; i < plan.Mismatches.Count; i++)
            {
                var m = plan.Mismatches[i];
                if (m.Kind == "parent")
                    plan.Notes.Add(
                        $"Hierarchy differs for '{m.SourceSlotId}': {m.Detail}. Keys stay parent-local (not Spine IK).");
            }

            if (srcClip.Tracks != null)
            {
                for (int t = 0; t < srcClip.Tracks.Count; t++)
                {
                    var track = srcClip.Tracks[t];
                    if (track == null) continue;
                    plan.TrackCount++;
                    if (track.Keys != null)
                        plan.KeyCount += track.Keys.Count;
                }
            }

            UniqueClipIdentity(destination, srcClip.Name, plan.SourceClipId,
                out plan.DestinationName, out plan.DestinationClipId);
            plan.Notes.Add(
                "This adapts keys to a different rest/hierarchy. It is not Spine-style retargeting. Destination rest and hierarchy are not rewritten.");
            plan.Ok = true;
            return plan;
        }

        /// <summary>Transient retargeted clip for canvas preview. Does not mutate either profile.</summary>
        public static SpritePartsClipDef BuildPreviewClip(
            Plan plan, SpriteSheetProfile source, SpriteSheetProfile destination)
        {
            if (plan == null || !plan.Ok || source == null || destination == null)
                return null;
            if (source.PartsClips == null || plan.SourceClipIndex < 0 ||
                plan.SourceClipIndex >= source.PartsClips.Count)
                return null;
            return RemapClip(
                source.PartsClips[plan.SourceClipIndex],
                source, destination, plan.SlotMap, plan.Mode,
                plan.DestinationName, plan.DestinationClipId);
        }

        public static ApplyResult Apply(
            Plan plan, SpriteSheetProfile source, SpriteSheetProfile destination,
            SpriteProfileArtImport.ImportSourceInfo sourceInfo = default,
            string importedUtc = null)
        {
            if (plan == null || !plan.Ok)
                return new ApplyResult { Reason = plan?.Reason ?? "Plan is null.", DestinationClipIndex = -1 };
            if (source == null || destination == null)
                return new ApplyResult { Reason = "Source and destination profiles must not be null.", DestinationClipIndex = -1 };

            var replay = PlanRetarget(source, plan.SourceClipIndex, destination, plan.SlotMap, plan.Mode);
            if (!replay.Ok)
                return new ApplyResult { Reason = replay.Reason, DestinationClipIndex = -1 };

            destination.EnsurePartsRig();
            int restSlots = destination.PartsSlots?.Count ?? 0;
            var restSnapshot = SnapshotRests(destination);
            var clip = RemapClip(
                source.PartsClips[replay.SourceClipIndex],
                source, destination, replay.SlotMap, replay.Mode,
                replay.DestinationName, replay.DestinationClipId);
            clip.Import = SpriteProfileArtImport.StampProvenance(
                sourceInfo,
                $"retarget '{replay.SourceClipName}' ({replay.Mode})",
                importedUtc);

            destination.PartsClips ??= new List<SpritePartsClipDef>();
            destination.PartsClips.Add(clip);
            SpritePartsValidation.CanonicalizeIds(destination);

            if (!RestsUnchanged(destination, restSnapshot) ||
                (destination.PartsSlots?.Count ?? 0) != restSlots)
            {
                destination.PartsClips.RemoveAt(destination.PartsClips.Count - 1);
                RestoreRests(destination, restSnapshot);
                return new ApplyResult
                {
                    Reason = "Retarget aborted: destination rest/hierarchy would have changed.",
                    DestinationClipIndex = -1,
                };
            }

            return new ApplyResult
            {
                Ok = true,
                Summary = replay.Summary,
                DestinationClipIndex = destination.PartsClips.Count - 1,
            };
        }

        static SpritePartsClipDef RemapClip(
            SpritePartsClipDef src,
            SpriteSheetProfile source,
            SpriteSheetProfile destination,
            Dictionary<string, string> slotMap,
            RestDeltaMode mode,
            string name,
            string clipId)
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
            if (src.Tracks == null)
                return copy;
            for (int t = 0; t < src.Tracks.Count; t++)
            {
                var track = src.Tracks[t];
                if (track == null) continue;
                string sid = SpritePartIdUtility.Canonical(track.SlotId);
                if (!slotMap.TryGetValue(sid, out string destSlot))
                    continue;
                var srcSlot = FindSlot(source, sid);
                var dstSlot = FindSlot(destination, destSlot);
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
                        var remapped = new SpritePartsKeyDef
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
                            AppearanceId = RemapAppearanceId(destination, key.AppearanceId),
                        };
                        if (mode == RestDeltaMode.RestDelta &&
                            track.Kind != SpritePartsTrackKind.Appearance &&
                            srcSlot != null && dstSlot != null)
                        {
                            RemapRestDelta(srcSlot, dstSlot, remapped);
                        }
                        destTrack.Keys.Add(remapped);
                    }
                }
                copy.Tracks.Add(destTrack);
            }
            return copy;
        }

        /// <summary>
        /// destKey = destRest ⊕ (sourceKey ⊖ sourceRest) in parent-local TRS.
        /// Position additive, rotation shortest-arc delta, scale multiplicative.
        /// </summary>
        public static void RemapRestDelta(
            SpritePartSlotDef sourceRest, SpritePartSlotDef destRest, SpritePartsKeyDef key)
        {
            key.Position = destRest.RestPosition + (key.Position - sourceRest.RestPosition);
            key.Rotation = destRest.RestRotation +
                           Mathf.DeltaAngle(sourceRest.RestRotation, key.Rotation);
            key.Scale = new Vector2(
                ScaleComponent(destRest.RestScale.x, sourceRest.RestScale.x, key.Scale.x),
                ScaleComponent(destRest.RestScale.y, sourceRest.RestScale.y, key.Scale.y));
        }

        static float ScaleComponent(float destRest, float sourceRest, float key)
        {
            float src = Mathf.Abs(sourceRest) < 1e-8f ? 1f : sourceRest;
            return destRest * (key / src);
        }

        static string RemapAppearanceId(SpriteSheetProfile destination, string appearanceId)
        {
            if (string.IsNullOrWhiteSpace(appearanceId))
                return string.Empty;
            string id = SpritePartIdUtility.Canonical(appearanceId);
            return SpritePartsAuthoringOps.FindAppearance(destination, id) != null
                ? id
                : string.Empty;
        }

        static bool TryResolveMap(Dictionary<string, string> slotMap, string canonicalSource, out string dest)
        {
            dest = null;
            if (slotMap.TryGetValue(canonicalSource, out dest) && !string.IsNullOrWhiteSpace(dest))
                return true;
            foreach (var kv in slotMap)
            {
                if (SpritePartIdUtility.Canonical(kv.Key) == canonicalSource &&
                    !string.IsNullOrWhiteSpace(kv.Value))
                {
                    dest = kv.Value;
                    return true;
                }
            }
            return false;
        }

        static void UniqueClipIdentity(
            SpriteSheetProfile destination, string sourceName, string sourceId,
            out string name, out string clipId)
        {
            var takenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var takenIds = new HashSet<string>(StringComparer.Ordinal);
            if (destination.PartsClips != null)
            {
                for (int i = 0; i < destination.PartsClips.Count; i++)
                {
                    var clip = destination.PartsClips[i];
                    if (clip == null) continue;
                    if (!string.IsNullOrWhiteSpace(clip.Name))
                        takenNames.Add(clip.Name);
                    takenIds.Add(SpritePartIdUtility.Canonical(clip.ClipId, clip.Name));
                }
            }
            string baseName = string.IsNullOrWhiteSpace(sourceName) ? "Retargeted" : sourceName.Trim();
            name = takenNames.Contains(baseName) ? baseName + " Retarget" : baseName;
            int n = 2;
            while (takenNames.Contains(name))
                name = baseName + " Retarget " + n++;

            string baseId = string.IsNullOrEmpty(sourceId) ? "retarget" : sourceId + ".retarget";
            clipId = baseId;
            n = 2;
            while (takenIds.Contains(clipId))
                clipId = baseId + n++;
        }

        static SpritePartSlotDef FindSlot(SpriteSheetProfile profile, string slotId)
        {
            if (profile?.PartsSlots == null) return null;
            string id = SpritePartIdUtility.Canonical(slotId);
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var slot = profile.PartsSlots[i];
                if (slot != null && SpritePartIdUtility.Canonical(slot.SlotId, slot.Name) == id)
                    return slot;
            }
            return null;
        }

        struct RestSnap
        {
            public string SlotId;
            public string ParentSlotId;
            public Vector2 Position;
            public float Rotation;
            public Vector2 Scale;
        }

        static List<RestSnap> SnapshotRests(SpriteSheetProfile profile)
        {
            var list = new List<RestSnap>();
            if (profile?.PartsSlots == null) return list;
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var s = profile.PartsSlots[i];
                if (s == null) continue;
                list.Add(new RestSnap
                {
                    SlotId = s.SlotId,
                    ParentSlotId = s.ParentSlotId,
                    Position = s.RestPosition,
                    Rotation = s.RestRotation,
                    Scale = s.RestScale,
                });
            }
            return list;
        }

        static bool RestsUnchanged(SpriteSheetProfile profile, List<RestSnap> snapshot)
        {
            if (profile?.PartsSlots == null) return snapshot.Count == 0;
            if (profile.PartsSlots.Count != snapshot.Count) return false;
            for (int i = 0; i < snapshot.Count; i++)
            {
                var s = profile.PartsSlots[i];
                var snap = snapshot[i];
                if (s == null) return false;
                if (s.SlotId != snap.SlotId || s.ParentSlotId != snap.ParentSlotId)
                    return false;
                if (Vector2.Distance(s.RestPosition, snap.Position) > 1e-6f)
                    return false;
                if (Mathf.Abs(Mathf.DeltaAngle(s.RestRotation, snap.Rotation)) > 1e-4f)
                    return false;
                if (Vector2.Distance(s.RestScale, snap.Scale) > 1e-6f)
                    return false;
            }
            return true;
        }

        static void RestoreRests(SpriteSheetProfile profile, List<RestSnap> snapshot)
        {
            if (profile?.PartsSlots == null) return;
            int n = Mathf.Min(profile.PartsSlots.Count, snapshot.Count);
            for (int i = 0; i < n; i++)
            {
                var s = profile.PartsSlots[i];
                if (s == null) continue;
                s.RestPosition = snapshot[i].Position;
                s.RestRotation = snapshot[i].Rotation;
                s.RestScale = snapshot[i].Scale;
                s.ParentSlotId = snapshot[i].ParentSlotId;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Convert one frame clip's cell sequence into appearance keys on a Part's
    /// independent appearance channel (Import Frame Sequence to Part).
    /// Art-only: pose keys are never created, modified or cleared, so the
    /// import works on slots with existing eased motion. Timing uses the Parts
    /// clip clock; default start is playhead. RepeatCount stamps the same finite
    /// traversal back-to-back; it is not a wrap-mode loop.
    /// </summary>
    public static class SpritePartsFrameSequenceImport
    {
        public sealed class Plan
        {
            public bool Ok;
            public string Reason;
            public List<string> Notes = new List<string>();
            public int SourceClipIndex = -1;
            public string SourceClipName;
            public int DestPartsClipIndex = -1;
            public string DestSlotId;
            public float StartTime;
            public float EndTime;
            public int KeyCount;
            /// <summary>Full traversals of the authored frames, laid end-to-end. Clamped to >= 1.</summary>
            public int RepeatCount = 1;
            public bool ExtendDuration;
            public float ResultDuration;
            public List<string> ExcludedFeatures = new List<string>();
        }

        public sealed class ApplyResult
        {
            public bool Ok;
            public string Reason;
            public string Summary;
        }

        public static Plan PlanImport(
            SpriteSheetProfile profile,
            int frameClipIndex,
            int partsClipIndex,
            string slotId,
            float startTimeSeconds,
            bool extendDuration,
            int repeatCount = 1)
        {
            var plan = new Plan
            {
                SourceClipIndex = frameClipIndex,
                DestPartsClipIndex = partsClipIndex,
                StartTime = Mathf.Max(0f, startTimeSeconds),
                ExtendDuration = extendDuration,
            };
            if (profile == null)
            {
                plan.Reason = "Profile is null.";
                return plan;
            }
            if (profile.Clips == null || frameClipIndex < 0 || frameClipIndex >= profile.Clips.Count ||
                profile.Clips[frameClipIndex] == null)
            {
                plan.Reason = "Source frame clip is missing.";
                return plan;
            }
            if (profile.PartsClips == null || partsClipIndex < 0 || partsClipIndex >= profile.PartsClips.Count ||
                profile.PartsClips[partsClipIndex] == null)
            {
                plan.Reason = "Destination Parts clip is missing.";
                return plan;
            }
            var slot = SpritePartsAuthoringOps.FindSlot(profile, slotId);
            if (slot == null)
            {
                plan.Reason = string.Format("Destination slot '{0}' is missing.", slotId);
                return plan;
            }
            plan.DestSlotId = SpritePartIdUtility.Canonical(slot.SlotId, slot.Name);

            var frameClip = profile.Clips[frameClipIndex];
            // Plan is read-only: never EnsureFrameData (mutates EventIds/arrays).
            plan.SourceClipName = frameClip.Name;
            if (frameClip.Frames == null || frameClip.Frames.Length == 0)
            {
                plan.Reason = "Source frame clip has no frames.";
                return plan;
            }
            // Cap repeats so a typo cannot freeze the editor with millions of keys.
            const int MaxRepeat = 256;
            if (repeatCount < 1)
            {
                plan.RepeatCount = 1;
                plan.Notes.Add("Repeat count below 1 treated as 1.");
            }
            else if (repeatCount > MaxRepeat)
            {
                plan.RepeatCount = MaxRepeat;
                plan.Notes.Add(string.Format("Repeat count capped at {0}.", MaxRepeat));
            }
            else
                plan.RepeatCount = repeatCount;

            CollectExcludedFeatures(frameClip, plan.ExcludedFeatures);
            if (plan.ExcludedFeatures.Count > 0)
            {
                plan.Notes.Add("Art-only import: " + string.Join("; ", plan.ExcludedFeatures.ToArray()) +
                               " are not converted to appearance keys.");
            }

            // Wrap-mode looping is never a substitute for Repeat: we stamp
            // RepeatCount finite traversals of the authored frames.
            if (frameClip.WrapMode > 1)
                plan.Notes.Add("Source wrap/control beyond Loop/Once is ignored; Repeat stamps finite traversals only.");

            // Pose state is irrelevant: appearance keys land on the independent
            // channel and never touch pose keys (existing motion is preserved).
            var partsClip = profile.PartsClips[partsClipIndex];
            float duration = Mathf.Max(1e-3f, partsClip.Duration);

            float onePass = 0f;
            for (int i = 0; i < frameClip.Frames.Length; i++)
                onePass += SpriteAnimPlayback.FrameDuration(frameClip, i);
            plan.EndTime = plan.StartTime + onePass * plan.RepeatCount;
            plan.KeyCount = frameClip.Frames.Length * plan.RepeatCount;
            plan.ResultDuration = duration;
            if (plan.EndTime > duration + 1e-4f)
            {
                if (!extendDuration)
                {
                    plan.Reason = string.Format(
                        "Sequence ends at {0:F3}s but Parts clip duration is {1:F3}s. Enable 'Extend duration' or lower the start time.",
                        plan.EndTime, duration);
                    return plan;
                }
                plan.ResultDuration = plan.EndTime;
                plan.Notes.Add(string.Format("Parts clip duration will extend to {0:F3}s.", plan.ResultDuration));
            }

            plan.Ok = true;
            return plan;
        }

        public static ApplyResult Apply(
            Plan plan, SpriteSheetProfile profile,
            SpriteProfileArtImport.ImportSourceInfo sourceInfo = default, string importedUtc = null)
        {
            if (plan == null || !plan.Ok)
                return new ApplyResult { Reason = plan?.Reason ?? "Plan is null." };
            if (profile == null)
                return new ApplyResult { Reason = "Profile is null." };

            // Re-validate against current profile.
            var applied = PlanImport(
                profile, plan.SourceClipIndex, plan.DestPartsClipIndex,
                plan.DestSlotId, plan.StartTime, plan.ExtendDuration, plan.RepeatCount);
            if (!applied.Ok)
                return new ApplyResult { Reason = applied.Reason };

            var frameClip = profile.Clips[applied.SourceClipIndex];
            frameClip.EnsureFrameData();
            var partsClip = profile.PartsClips[applied.DestPartsClipIndex];
            if (SpritePartsAuthoringOps.FindSlot(profile, applied.DestSlotId) == null)
                return new ApplyResult { Reason = "Destination slot disappeared." };

            profile.PartsAppearances ??= new List<SpritePartAppearanceDef>();
            partsClip.Tracks ??= new List<SpritePartsTrackDef>();

            // Rollback marks. Pose keys are never written; the only pose-side
            // effect is legacy id migration when the channel is first created,
            // snapshotted here and restored on failure.
            int appMark = profile.PartsAppearances.Count;
            float durationBefore = partsClip.Duration;
            var channelBefore = SpritePartsAuthoringOps.FindAppearanceTrack(partsClip, applied.DestSlotId);
            bool channelExisted = channelBefore != null;
            var channelKeysBefore = channelExisted
                ? new List<SpritePartsKeyDef>(channelBefore.Keys ?? new List<SpritePartsKeyDef>())
                : null;
            var poseIdsBefore = new List<KeyValuePair<SpritePartsKeyDef, string>>();
            var poseTrackBefore = SpritePartsAuthoringOps.FindTrack(partsClip, applied.DestSlotId);
            if (poseTrackBefore?.Keys != null)
            {
                for (int i = 0; i < poseTrackBefore.Keys.Count; i++)
                {
                    var key = poseTrackBefore.Keys[i];
                    if (key != null && !string.IsNullOrWhiteSpace(key.AppearanceId))
                        poseIdsBefore.Add(new KeyValuePair<SpritePartsKeyDef, string>(key, key.AppearanceId));
                }
            }

            try
            {
                if (applied.ExtendDuration && applied.ResultDuration > partsClip.Duration)
                    partsClip.Duration = applied.ResultDuration;

                var channel = SpritePartsAuthoringOps.EnsureAppearanceTrack(partsClip, applied.DestSlotId);
                channel.Keys ??= new List<SpritePartsKeyDef>();

                float time = applied.StartTime;
                int sheetIndex = Mathf.Max(0, frameClip.SheetIndex);
                var sheet = profile.SheetAt(sheetIndex);
                int cells = (sheet != null ? Mathf.Max(1, sheet.Columns) : Mathf.Max(1, profile.Columns)) *
                            (sheet != null ? Mathf.Max(1, sheet.Rows) : Mathf.Max(1, profile.Rows));
                for (int repeat = 0; repeat < applied.RepeatCount; repeat++)
                {
                    for (int i = 0; i < frameClip.Frames.Length; i++)
                    {
                        int cell = frameClip.Frames[i];
                        int row = frameClip.FrameRows != null && i < frameClip.FrameRows.Length
                            ? frameClip.FrameRows[i]
                            : SpriteClipDef.InheritClipRow;
                        // Resolve cell the same way frame playback does when row is inherited.
                        int resolvedCell = cell;
                        if (row != SpriteClipDef.InheritClipRow)
                        {
                            int cols = sheet != null ? Mathf.Max(1, sheet.Columns) : Mathf.Max(1, profile.Columns);
                            resolvedCell = row * cols + cell;
                        }
                        if (resolvedCell < 0 || resolvedCell >= cells)
                            throw new InvalidOperationException(string.Format(
                                "Frame {0} resolves to cell {1} outside sheet '{2}' ({3} cells).",
                                i, resolvedCell, sheet != null ? sheet.Name : "Sheet", cells));

                        // Same sheet/cell reuses the same appearance id across repeats.
                        string appearanceId = EnsureAppearanceForCell(
                            profile, sheetIndex, resolvedCell,
                            string.Format("{0}_{1}", frameClip.Name, i),
                            SpriteProfileArtImport.StampProvenance(sourceInfo,
                                string.Format("frame sequence '{0}' frame {1} cell {2}",
                                    frameClip.Name, i, resolvedCell), importedUtc));

                        RemoveChannelKeyAt(channel, time);
                        channel.Keys.Add(new SpritePartsKeyDef
                        {
                            Time = time,
                            EaseMode = (byte)SpriteEaseMode.Step,
                            AppearanceId = appearanceId,
                        });
                        time += SpriteAnimPlayback.FrameDuration(frameClip, i);
                    }
                }
                channel.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));

                SpritePartsValidation.CanonicalizeIds(profile);
                return new ApplyResult
                {
                    Ok = true,
                    Summary = string.Format(
                        "Wrote {0} appearance key(s) on '{1}' from frame clip '{2}' ({3:F2}s-{4:F2}s, repeat x{5}). Pose keys untouched.",
                        applied.KeyCount, applied.DestSlotId, applied.SourceClipName,
                        applied.StartTime, applied.EndTime, applied.RepeatCount),
                };
            }
            catch (Exception ex)
            {
                partsClip.Duration = durationBefore;
                while (profile.PartsAppearances.Count > appMark)
                    profile.PartsAppearances.RemoveAt(profile.PartsAppearances.Count - 1);
                for (int i = 0; i < poseIdsBefore.Count; i++)
                    poseIdsBefore[i].Key.AppearanceId = poseIdsBefore[i].Value;
                if (channelExisted && channelBefore != null)
                {
                    channelBefore.Keys.Clear();
                    channelBefore.Keys.AddRange(channelKeysBefore);
                }
                else
                {
                    // Remove the channel this apply created.
                    for (int i = partsClip.Tracks.Count - 1; i >= 0; i--)
                    {
                        var t = partsClip.Tracks[i];
                        if (t != null && t.Kind == SpritePartsTrackKind.Appearance &&
                            SpritePartIdUtility.Canonical(t.SlotId) == applied.DestSlotId)
                        {
                            partsClip.Tracks.RemoveAt(i);
                            break;
                        }
                    }
                }
                return new ApplyResult { Reason = "Sequence import aborted, destination unchanged: " + ex.Message };
            }
        }

        static void RemoveChannelKeyAt(SpritePartsTrackDef channel, float time)
        {
            channel.Keys?.RemoveAll(k =>
                k != null && Mathf.Abs(k.Time - time) <= 1e-4f);
        }

        static void CollectExcludedFeatures(SpriteClipDef clip, List<string> list)
        {
            if (clip.OnionOffsets != null)
            {
                for (int i = 0; i < clip.OnionOffsets.Length; i++)
                    if (clip.OnionOffsets[i].sqrMagnitude > 1e-8f) { list.Add("frame offsets"); break; }
            }
            if (clip.FrameRotations != null)
            {
                for (int i = 0; i < clip.FrameRotations.Length; i++)
                    if (Mathf.Abs(clip.FrameRotations[i]) > 1e-4f) { list.Add("frame rotations"); break; }
            }
            if (clip.FrameScales != null)
            {
                for (int i = 0; i < clip.FrameScales.Length; i++)
                    if ((clip.FrameScales[i] - Vector2.one).sqrMagnitude > 1e-8f) { list.Add("frame scales"); break; }
            }
            if (clip.Sockets != null && clip.Sockets.Count > 0) list.Add("sockets");
            if (clip.EventMarkers != null && clip.EventMarkers.Count > 0) list.Add("events");
            else if (clip.EventIds != null)
            {
                for (int i = 0; i < clip.EventIds.Length; i++)
                    if (clip.EventIds[i] != 0) { list.Add("events"); break; }
            }
        }

        static string EnsureAppearanceForCell(
            SpriteSheetProfile profile, int sheetIndex, int cellIndex, string nameHint,
            SpriteImportProvenance provenance = null)
        {
            for (int i = 0; i < profile.PartsAppearances.Count; i++)
            {
                var app = profile.PartsAppearances[i];
                if (app == null) continue;
                if (app.SheetIndex == sheetIndex && app.CellIndex == cellIndex &&
                    app.LogicalWorldSize == Vector2.zero &&
                    app.PivotSource == SpritePartPivotSource.SheetDefault)
                    return SpritePartIdUtility.Canonical(app.AppearanceId, app.Name);
            }
            var taken = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < profile.PartsAppearances.Count; i++)
            {
                var app = profile.PartsAppearances[i];
                if (app != null)
                    taken.Add(SpritePartIdUtility.Canonical(app.AppearanceId, app.Name));
            }
            string baseId = SpritePartIdUtility.Canonical(nameHint);
            string id = baseId;
            int n = 2;
            while (taken.Contains(id))
                id = baseId + n++;
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = nameHint,
                AppearanceId = id,
                SheetIndex = sheetIndex,
                CellIndex = cellIndex,
                LogicalWorldSize = Vector2.zero,
                PivotSource = SpritePartPivotSource.SheetDefault,
                // Only freshly created appearances get this import's
                // provenance; reused cell appearances keep what they had.
                Import = provenance,
            });
            return id;
        }
    }
}

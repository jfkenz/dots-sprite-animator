using System;
using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Art-only import between profiles: deep-copies sheet and Parts appearance
    /// definitions into the destination while reusing texture asset references
    /// (texture files are never duplicated). The source profile is read-only
    /// during planning and applying, and never becomes a runtime dependency.
    /// Plan first, validate, then apply once: a failed apply leaves both
    /// profiles unchanged. Definitions deduplicate only on a complete metadata
    /// match (texture, grid, crop rects, pivots, PPU) — never by name.
    /// </summary>
    /// <summary>
    /// What happens when an imported item conflicts with an existing destination
    /// item (same stable id/name, different content). Exact-definition matches
    /// always reuse and are not affected by this policy.
    /// </summary>
    public enum SpriteProfileImportConflictPolicy : byte
    {
        /// <summary>Default: the import becomes a new copy with a unique id/name.</summary>
        UniqueCopy = 0,
        /// <summary>
        /// The destination item keeps its stable identity (id/name/list slot)
        /// and its content is overwritten from the source. Explicit only; the
        /// plan previews everything the replaced item is used by. Never a
        /// silent refresh: re-importing defaults to another copy.
        /// </summary>
        ReplaceExisting = 1,
    }

    public static class SpriteProfileArtImport
    {
        public enum SheetResolution { AddCopy, ReuseExisting }

        public enum AppearanceResolution { AddCopy, ReuseExisting, ReplaceExisting }

        public sealed class SheetAction
        {
            public int SourceSheetIndex;
            public string SourceName;
            public SheetResolution Resolution;
            /// <summary>Destination index for AddCopy (assigned in plan order); reuse index otherwise.</summary>
            public int DestinationSheetIndex = -1;
            public bool RequiredByAppearance;
        }

        public sealed class AppearanceAction
        {
            public int SourceAppearanceIndex;
            public string SourceAppearanceId;
            /// <summary>Unique destination id for AddCopy; existing destination id for ReuseExisting.</summary>
            public string DestinationAppearanceId;
            /// <summary>Unique display name for AddCopy copies.</summary>
            public string DestinationName;
            public AppearanceResolution Resolution;
            /// <summary>ReplaceExisting: destination list index whose content gets overwritten.</summary>
            public int ReplaceTargetIndex = -1;
            /// <summary>ReplaceExisting preview: destination references to the replaced appearance id.</summary>
            public readonly List<string> UsedBy = new();
        }

        public sealed class Plan
        {
            public bool Ok;
            public string Reason;
            public SpriteProfileImportConflictPolicy ConflictPolicy;
            public readonly List<string> Notes = new();
            public readonly List<SheetAction> Sheets = new();
            public readonly List<AppearanceAction> Appearances = new();
            public int SheetsAdded;
            public int SheetsReused;
            public int AppearancesAdded;
            public int AppearancesReused;
            public int AppearancesReplaced;

            public string Summary =>
                $"{SheetsAdded} sheet{(SheetsAdded == 1 ? "" : "s")} added, {SheetsReused} reused | " +
                $"{AppearancesAdded} appearance{(AppearancesAdded == 1 ? "" : "s")} added, {AppearancesReused} reused" +
                (AppearancesReplaced > 0
                    ? $", {AppearancesReplaced} replaced"
                    : string.Empty);
        }

        public struct ApplyResult
        {
            public bool Ok;
            public string Reason;
            public string Summary;
            public List<KeyValuePair<string, string>> AppearanceIdRemaps;
        }

        /// <summary>
        /// Editor-supplied identity of the source profile asset, for
        /// display-only provenance stamping. Empty values are fine; nothing
        /// ever resolves back to the source.
        /// </summary>
        public struct ImportSourceInfo
        {
            public string Guid;
            public string Path;
        }

        internal static SpriteImportProvenance StampProvenance(
            ImportSourceInfo sourceInfo, string sourceItem, string importedUtc,
            string libraryGuid = null, string libraryItemId = null)
        {
            return new SpriteImportProvenance
            {
                SourceGuid = sourceInfo.Guid ?? string.Empty,
                SourcePath = sourceInfo.Path ?? string.Empty,
                SourceItem = sourceItem ?? string.Empty,
                ImportedUtc = string.IsNullOrEmpty(importedUtc)
                    ? DateTime.UtcNow.ToString("o")
                    : importedUtc,
                LibraryGuid = libraryGuid ?? string.Empty,
                LibraryItemId = libraryItemId ?? string.Empty,
            };
        }

        /// <summary>
        /// Build an import plan against immutable source data. Mutates nothing —
        /// the source profile is never normalized, so it stays byte-for-byte
        /// unchanged. Requested items missing a usable texture fail the whole
        /// plan with a specific reason (the picker should filter those first).
        /// </summary>
        public static Plan PlanImport(
            SpriteSheetProfile source,
            SpriteSheetProfile destination,
            IReadOnlyCollection<int> sourceSheetIndices,
            IReadOnlyCollection<int> sourceAppearanceIndices)
            => PlanImport(source, destination, sourceSheetIndices, sourceAppearanceIndices,
                SpriteProfileImportConflictPolicy.UniqueCopy);

        public static Plan PlanImport(
            SpriteSheetProfile source,
            SpriteSheetProfile destination,
            IReadOnlyCollection<int> sourceSheetIndices,
            IReadOnlyCollection<int> sourceAppearanceIndices,
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
                plan.Reason = "Source and destination are the same profile. Use 'From This Profile' instead.";
                return plan;
            }
            var srcSheets = SourceSheets(source);
            var srcApps = source.PartsAppearances ?? new List<SpritePartAppearanceDef>();
            // Planning is read-only on both profiles. Never EnsureSheets / EnsurePartsRig
            // here: the import picker rebuilds the plan every GUI frame.
            var destSheets = destination.Sheets ?? new List<SpriteSheetDef>();
            var destApps = destination.PartsAppearances ?? new List<SpritePartAppearanceDef>();

            // Collect every sheet the import needs: requested + appearance dependencies.
            var neededSheets = new SortedSet<int>();
            if (sourceSheetIndices != null)
            {
                foreach (int index in sourceSheetIndices)
                {
                    if (!IsUsableSheetIndex(srcSheets, index, plan))
                        return plan;
                    neededSheets.Add(index);
                }
            }
            if (sourceAppearanceIndices != null)
            {
                foreach (int index in sourceAppearanceIndices)
                {
                    if (index < 0 || index >= srcApps.Count)
                    {
                        plan.Reason = $"Source appearance index {index} is out of range.";
                        return plan;
                    }
                    var app = srcApps[index];
                    if (app == null)
                    {
                        plan.Reason = $"Source appearance[{index}] is null.";
                        return plan;
                    }
                    if (!IsUsableSheetIndex(srcSheets, app.SheetIndex, plan))
                        return plan;
                    // An appearance needs its sheet: pull the sheet along as a
                    // dependency even when it was not explicitly selected.
                    neededSheets.Add(app.SheetIndex);
                    int cells = Mathf.Max(1, srcSheets[app.SheetIndex].Columns) *
                                Mathf.Max(1, srcSheets[app.SheetIndex].Rows);
                    int cell = ((app.CellIndex % cells) + cells) % cells;
                    if (app.CellIndex < 0 || app.CellIndex >= cells)
                        plan.Notes.Add($"Appearance '{app.AppearanceId}' cell {app.CellIndex} wraps to {cell} on the destination sheet.");
                }
            }

            var requestedSheets = new HashSet<int>(sourceSheetIndices ?? (IEnumerable<int>)Array.Empty<int>());

            // Resolve sheets: exact definition match reuses the destination sheet,
            // anything else becomes a deep copy appended after the existing sheets.
            int nextAddedIndex = destSheets.Count;
            var sheetActions = new Dictionary<int, SheetAction>();
            foreach (int sourceIndex in neededSheets)
            {
                var sourceSheet = srcSheets[sourceIndex];
                var action = new SheetAction
                {
                    SourceSheetIndex = sourceIndex,
                    SourceName = sourceSheet.Name,
                    RequiredByAppearance = !requestedSheets.Contains(sourceIndex),
                };
                int existing = FindExactSheet(destSheets, sourceSheet);
                if (existing >= 0)
                {
                    action.Resolution = SheetResolution.ReuseExisting;
                    action.DestinationSheetIndex = existing;
                    plan.SheetsReused++;
                }
                else
                {
                    action.Resolution = SheetResolution.AddCopy;
                    action.DestinationSheetIndex = nextAddedIndex++;
                    plan.SheetsAdded++;
                }
                sheetActions[sourceIndex] = action;
                plan.Sheets.Add(action);
            }

            // Resolve appearances: same canonical id AND identical definition reuses
            // the destination appearance; otherwise a copy with a unique id/name.
            var takenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var destApp in destApps)
            {
                if (destApp != null)
                    takenIds.Add(SpritePartIdUtility.Canonical(destApp.AppearanceId, destApp.Name));
            }
            var takenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var destApp in destApps)
            {
                if (destApp != null && !string.IsNullOrWhiteSpace(destApp.Name))
                    takenNames.Add(destApp.Name);
            }

            if (sourceAppearanceIndices != null)
            {
                foreach (int index in sourceAppearanceIndices)
                {
                    var sourceApp = srcApps[index];
                    string sourceId = SpritePartIdUtility.Canonical(sourceApp.AppearanceId, sourceApp.Name);
                    var action = new AppearanceAction
                    {
                        SourceAppearanceIndex = index,
                        SourceAppearanceId = sourceId,
                    };
                    int reuse = FindExactAppearance(destSheets, destApps, srcSheets, sourceApp, sheetActions[sourceApp.SheetIndex]);
                    if (reuse >= 0)
                    {
                        var destApp = destApps[reuse];
                        action.Resolution = AppearanceResolution.ReuseExisting;
                        action.DestinationAppearanceId = SpritePartIdUtility.Canonical(destApp.AppearanceId, destApp.Name);
                        plan.AppearancesReused++;
                    }
                    else if (conflictPolicy == SpriteProfileImportConflictPolicy.ReplaceExisting)
                    {
                        int conflict = FindAppearanceById(destApps, sourceId);
                        if (conflict < 0)
                        {
                            action.Resolution = AppearanceResolution.AddCopy;
                            action.DestinationAppearanceId = UniqueId(sourceId, takenIds);
                            action.DestinationName = UniqueName(sourceApp.Name, action.DestinationAppearanceId, takenNames);
                            takenIds.Add(action.DestinationAppearanceId);
                            takenNames.Add(action.DestinationName);
                            plan.AppearancesAdded++;
                        }
                        else
                        {
                        // Replace in place: the destination keeps its stable id and
                            // display name; only the art definition is overwritten. The
                            // plan previews every destination use of that id.
                            var target = destApps[conflict];
                            action.Resolution = AppearanceResolution.ReplaceExisting;
                            action.DestinationAppearanceId = SpritePartIdUtility.Canonical(target.AppearanceId, target.Name);
                            action.ReplaceTargetIndex = conflict;
                            CollectAppearanceUsedBy(destination, action.DestinationAppearanceId, action.UsedBy);
                            plan.AppearancesReplaced++;
                        }
                    }
                    else
                    {
                        action.Resolution = AppearanceResolution.AddCopy;
                        action.DestinationAppearanceId = UniqueId(sourceId, takenIds);
                        action.DestinationName = UniqueName(sourceApp.Name, action.DestinationAppearanceId, takenNames);
                        takenIds.Add(action.DestinationAppearanceId);
                        takenNames.Add(action.DestinationName);
                        plan.AppearancesAdded++;
                    }
                    plan.Appearances.Add(action);
                }
            }

            plan.Ok = true;
            return plan;
        }

        /// <summary>
        /// Apply a plan in one pass. Re-resolves destinations so a drifted
        /// destination cannot receive misplaced copies; hard inconsistencies
        /// fail without touching either profile.
        /// </summary>
        public static ApplyResult Apply(
            Plan plan, SpriteSheetProfile source, SpriteSheetProfile destination,
            ImportSourceInfo sourceInfo = default, string importedUtc = null)
        {
            if (plan == null || !plan.Ok)
                return new ApplyResult { Reason = plan?.Reason ?? "Plan is null." };
            if (source == null || destination == null)
                return new ApplyResult { Reason = "Source and destination profiles must not be null." };

            var applied = PlanImport(
                source, destination,
                CollectSourceSheets(plan),
                CollectSourceAppearances(plan),
                plan.ConflictPolicy);
            if (!applied.Ok)
                return new ApplyResult { Reason = applied.Reason };

            destination.Sheets ??= new List<SpriteSheetDef>();
            destination.PartsAppearances ??= new List<SpritePartAppearanceDef>();
            var srcSheets = SourceSheets(source);
            var srcApps = source.PartsAppearances ?? new List<SpritePartAppearanceDef>();
            var remaps = new List<KeyValuePair<string, string>>();
            var sheetMap = new Dictionary<int, int>();
            var replacedAppearances = new List<KeyValuePair<int, SpritePartAppearanceDef>>();
            int sheetMark = destination.Sheets.Count;
            int appMark = destination.PartsAppearances.Count;
            try
            {
                foreach (var action in applied.Sheets)
                {
                    if (action.Resolution == SheetResolution.ReuseExisting)
                    {
                        sheetMap[action.SourceSheetIndex] = action.DestinationSheetIndex;
                        continue;
                    }
                    var sourceSheet = srcSheets[action.SourceSheetIndex];
                    var copy = CloneSheet(sourceSheet, UniqueSheetName(sourceSheet.Name, destination));
                    copy.Import = StampProvenance(sourceInfo,
                        $"sheet '{sourceSheet.Name}' [{action.SourceSheetIndex}]", importedUtc);
                    destination.Sheets.Add(copy);
                    sheetMap[action.SourceSheetIndex] = destination.Sheets.Count - 1;
                }

                foreach (var action in applied.Appearances)
                {
                    if (action.Resolution == AppearanceResolution.ReuseExisting)
                    {
                        remaps.Add(new KeyValuePair<string, string>(action.SourceAppearanceId, action.DestinationAppearanceId));
                        continue;
                    }
                    var sourceApp = srcApps[action.SourceAppearanceIndex];
                    if (!sheetMap.TryGetValue(sourceApp.SheetIndex, out int destSheetIndex))
                        throw new InvalidOperationException(
                            $"Import lost sheet remap for appearance '{action.SourceAppearanceId}'.");

                    if (action.Resolution == AppearanceResolution.ReplaceExisting)
                    {
                        if (action.ReplaceTargetIndex < 0 || action.ReplaceTargetIndex >= destination.PartsAppearances.Count)
                            throw new InvalidOperationException(
                                $"Replace target for appearance '{action.SourceAppearanceId}' is out of range.");
                        var target = destination.PartsAppearances[action.ReplaceTargetIndex];
                        // Snapshot for rollback, then swap the slot content while
                        // keeping the destination's stable id and display name.
                        replacedAppearances.Add(new KeyValuePair<int, SpritePartAppearanceDef>(
                            action.ReplaceTargetIndex, target));
                        destination.PartsAppearances[action.ReplaceTargetIndex] = new SpritePartAppearanceDef
                        {
                            Name = target.Name,
                            AppearanceId = target.AppearanceId,
                            SheetIndex = destSheetIndex,
                            CellIndex = sourceApp.CellIndex,
                            LogicalWorldSize = sourceApp.LogicalWorldSize,
                            PivotSource = sourceApp.PivotSource,
                            PivotOverride = sourceApp.PivotOverride,
                            SemanticRole = sourceApp.SemanticRole,
                            Import = StampProvenance(sourceInfo,
                                $"appearance '{action.SourceAppearanceId}' (replaced)", importedUtc),
                        };
                    }
                    else
                    {
                        var copiedAppearance = new SpritePartAppearanceDef
                        {
                            Name = action.DestinationName,
                            AppearanceId = action.DestinationAppearanceId,
                            SheetIndex = destSheetIndex,
                            CellIndex = sourceApp.CellIndex,
                            LogicalWorldSize = sourceApp.LogicalWorldSize,
                            PivotSource = sourceApp.PivotSource,
                            PivotOverride = sourceApp.PivotOverride,
                            SemanticRole = sourceApp.SemanticRole,
                            Import = StampProvenance(sourceInfo,
                                $"appearance '{action.SourceAppearanceId}'", importedUtc),
                        };
                        destination.PartsAppearances.Add(copiedAppearance);
                    }
                    remaps.Add(new KeyValuePair<string, string>(action.SourceAppearanceId, action.DestinationAppearanceId));
                }

                SpritePartsValidation.CanonicalizeIds(destination);
            }
            catch (Exception ex)
            {
                while (destination.Sheets.Count > sheetMark)
                    destination.Sheets.RemoveAt(destination.Sheets.Count - 1);
                while (destination.PartsAppearances.Count > appMark)
                    destination.PartsAppearances.RemoveAt(destination.PartsAppearances.Count - 1);
                for (int i = replacedAppearances.Count - 1; i >= 0; i--)
                    destination.PartsAppearances[replacedAppearances[i].Key] = replacedAppearances[i].Value;
                return new ApplyResult { Reason = "Import aborted, destination unchanged: " + ex.Message };
            }
            return new ApplyResult
            {
                Ok = true,
                Summary = applied.Summary,
                AppearanceIdRemaps = remaps,
            };
        }

        /// <summary>
        /// Complete render-metadata equality: same texture asset, grid, PPU, pivot,
        /// layout mode, per-cell pivots, and crop rects while in Cropped mode
        /// (rects are ignored while the layout is Grid, matching SpriteSheetDef).
        /// Name is deliberately not part of equality. Textureless sheets never match.
        /// </summary>
        public static bool SheetDefinitionsEqual(SpriteSheetDef a, SpriteSheetDef b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null)
                return false;
            if (a.Texture == null || a.Texture != b.Texture)
                return false;
            if (Mathf.Max(1, a.Columns) != Mathf.Max(1, b.Columns))
                return false;
            if (Mathf.Max(1, a.Rows) != Mathf.Max(1, b.Rows))
                return false;
            if (a.PixelsPerUnit != b.PixelsPerUnit)
                return false;
            if (a.Pivot != b.Pivot)
                return false;
            if (a.CellLayoutMode != b.CellLayoutMode)
                return false;
            if (a.CellLayoutMode == SpriteSheetCellLayoutMode.Cropped &&
                !RectArraysEqual(a.CroppedCellRects, b.CroppedCellRects))
                return false;
            if (!CellPivotsEqual(a.CellPivots, b.CellPivots))
                return false;
            return true;
        }

        /// <summary>
        /// Read-only sheet view of the source. Prefers the Sheets list; an old
        /// legacy profile (empty Sheets, but Sheet/Columns/Rows authored) is
        /// presented as one synthesized sheet without touching the source asset.
        /// </summary>
        static List<SpriteSheetDef> SourceSheets(SpriteSheetProfile source)
        {
            if (source.Sheets != null && source.Sheets.Count > 0)
                return source.Sheets;
            if (source.Sheet == null)
                return new List<SpriteSheetDef>();
            return new List<SpriteSheetDef>
            {
                new SpriteSheetDef
                {
                    Name = string.IsNullOrWhiteSpace(source.Sheet.name) ? "Sheet" : source.Sheet.name,
                    Texture = source.Sheet,
                    Columns = source.Columns,
                    Rows = source.Rows,
                    PixelsPerUnit = source.PixelsPerUnit,
                    Pivot = source.Pivot,
                    CellLayoutMode = source.CellLayoutMode,
                    CroppedCellRects = source.CroppedCellRects,
                    CellPivots = source.CellPivots,
                },
            };
        }

        static bool IsUsableSheetIndex(List<SpriteSheetDef> srcSheets, int index, Plan plan)
        {
            if (index < 0 || index >= srcSheets.Count)
            {
                plan.Reason = $"Source sheet index {index} is out of range.";
                return false;
            }
            var sheet = srcSheets[index];
            if (sheet == null || sheet.Texture == null)
            {
                plan.Reason = $"Source sheet '{sheet?.Name ?? index.ToString()}' has no texture and cannot be imported.";
                return false;
            }
            return true;
        }

        static int FindExactSheet(List<SpriteSheetDef> destSheets, SpriteSheetDef sourceSheet)
        {
            for (int i = 0; i < destSheets.Count; i++)
            {
                if (SheetDefinitionsEqual(destSheets[i], sourceSheet))
                    return i;
            }
            return -1;
        }

        /// <summary>First destination appearance with this canonical id, or -1. Definition is not compared.</summary>
        static int FindAppearanceById(List<SpritePartAppearanceDef> destApps, string canonicalId)
        {
            for (int i = 0; i < destApps.Count; i++)
            {
                var destApp = destApps[i];
                if (destApp != null &&
                    SpritePartIdUtility.Canonical(destApp.AppearanceId, destApp.Name) == canonicalId)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Read-only preview of every destination reference to an appearance id:
        /// slot defaults, skin bindings, and keyed sprites inside Parts clips.
        /// </summary>
        static void CollectAppearanceUsedBy(SpriteSheetProfile destination, string canonicalId, List<string> usedBy)
        {
            if (destination.PartsSlots != null)
            {
                for (int i = 0; i < destination.PartsSlots.Count; i++)
                {
                    var slot = destination.PartsSlots[i];
                    if (slot != null && !string.IsNullOrWhiteSpace(slot.DefaultAppearanceId) &&
                        SpritePartIdUtility.Canonical(slot.DefaultAppearanceId) == canonicalId)
                        usedBy.Add($"slot '{slot.Name}' default art");
                }
            }
            if (destination.PartsSkins != null)
            {
                for (int i = 0; i < destination.PartsSkins.Count; i++)
                {
                    var skin = destination.PartsSkins[i];
                    if (skin?.Bindings == null) continue;
                    for (int b = 0; b < skin.Bindings.Count; b++)
                    {
                        var binding = skin.Bindings[b];
                        if (binding != null && !string.IsNullOrWhiteSpace(binding.AppearanceId) &&
                            SpritePartIdUtility.Canonical(binding.AppearanceId) == canonicalId)
                            usedBy.Add($"skin '{skin.Name}' binding '{binding.SlotId}'");
                    }
                }
            }
            if (destination.PartsClips != null)
            {
                for (int i = 0; i < destination.PartsClips.Count; i++)
                {
                    var clip = destination.PartsClips[i];
                    if (clip?.Tracks == null) continue;
                    int keyed = 0;
                    for (int t = 0; t < clip.Tracks.Count; t++)
                    {
                        var track = clip.Tracks[t];
                        if (track?.Keys == null) continue;
                        for (int k = 0; k < track.Keys.Count; k++)
                        {
                            var key = track.Keys[k];
                            if (key != null && !string.IsNullOrWhiteSpace(key.AppearanceId) &&
                                SpritePartIdUtility.Canonical(key.AppearanceId) == canonicalId)
                                keyed++;
                        }
                    }
                    if (keyed > 0)
                        usedBy.Add($"Parts clip '{clip.Name}': {keyed} keyed sprite(s)");
                }
            }
        }

        /// <summary>Same canonical id and identical art definition resolves to the destination appearance.</summary>
        static int FindExactAppearance(
            List<SpriteSheetDef> destSheets,
            List<SpritePartAppearanceDef> destApps,
            List<SpriteSheetDef> srcSheets,
            SpritePartAppearanceDef sourceApp,
            SheetAction sheetAction)
        {
            string sourceId = SpritePartIdUtility.Canonical(sourceApp.AppearanceId, sourceApp.Name);
            var sourceSheet = srcSheets[sourceApp.SheetIndex];
            for (int i = 0; i < destApps.Count; i++)
            {
                var destApp = destApps[i];
                if (destApp == null)
                    continue;
                string destId = SpritePartIdUtility.Canonical(destApp.AppearanceId, destApp.Name);
                if (destId != sourceId)
                    continue;
                if (destApp.CellIndex != sourceApp.CellIndex)
                    continue;
                if (destApp.LogicalWorldSize != sourceApp.LogicalWorldSize)
                    continue;
                if (destApp.PivotSource != sourceApp.PivotSource || destApp.PivotOverride != sourceApp.PivotOverride)
                    continue;
                if (destApp.SemanticRole != sourceApp.SemanticRole)
                    continue;
                var destSheet = destApp.SheetIndex >= 0 && destApp.SheetIndex < destSheets.Count
                    ? destSheets[destApp.SheetIndex]
                    : null;
                if (sheetAction.Resolution == SheetResolution.ReuseExisting)
                {
                    if (destApp.SheetIndex != sheetAction.DestinationSheetIndex)
                        continue;
                    if (destSheet != null && SheetDefinitionsEqual(destSheet, sourceSheet))
                        return i;
                }
                else if (SheetDefinitionsEqual(destSheet, sourceSheet))
                {
                    return i;
                }
            }
            return -1;
        }

        internal static SpriteSheetDef CloneSheet(SpriteSheetDef source, string name)
        {
            return new SpriteSheetDef
            {
                Name = name,
                Texture = source.Texture,
                Columns = source.Columns,
                Rows = source.Rows,
                PixelsPerUnit = source.PixelsPerUnit,
                Pivot = source.Pivot,
                CellLayoutMode = source.CellLayoutMode,
                CroppedCellRects = source.CroppedCellRects == null
                    ? null
                    : (RectInt[])source.CroppedCellRects.Clone(),
                CellPivots = source.CellPivots == null
                    ? null
                    : new List<SpriteCellPivot>(source.CellPivots),
            };
        }

        internal static SpritePartAppearanceDef CloneAppearance(
            SpritePartAppearanceDef source, string name, string appearanceId, int destSheetIndex)
        {
            return new SpritePartAppearanceDef
            {
                Name = name,
                AppearanceId = appearanceId,
                SheetIndex = destSheetIndex,
                CellIndex = source.CellIndex,
                LogicalWorldSize = source.LogicalWorldSize,
                PivotSource = source.PivotSource,
                PivotOverride = source.PivotOverride,
                SemanticRole = source.SemanticRole,
            };
        }

        static string UniqueId(string desired, HashSet<string> taken)
        {
            string id = desired;
            int n = 2;
            while (taken.Contains(id))
                id = desired + n++;
            return id;
        }

        static string UniqueName(string sourceName, string fallbackId, HashSet<string> taken)
        {
            string baseName = string.IsNullOrWhiteSpace(sourceName) ? fallbackId : sourceName.Trim();
            if (!taken.Contains(baseName))
                return baseName;
            string name = baseName + " 2";
            int n = 3;
            while (taken.Contains(name))
                name = baseName + " " + n++;
            return name;
        }

        static string UniqueSheetName(string sourceName, SpriteSheetProfile destination)
        {
            string baseName = string.IsNullOrWhiteSpace(sourceName) ? "Sheet" : sourceName.Trim();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destSheetsForNames = destination.Sheets;
            if (destSheetsForNames != null)
            {
                for (int i = 0; i < destSheetsForNames.Count; i++)
                {
                    var sheet = destSheetsForNames[i];
                    if (sheet != null && !string.IsNullOrWhiteSpace(sheet.Name))
                        taken.Add(sheet.Name);
                }
            }
            if (!taken.Contains(baseName))
                return baseName;
            string name = baseName + " 2";
            int n = 3;
            while (taken.Contains(name))
                name = baseName + " " + n++;
            return name;
        }

        static bool RectArraysEqual(RectInt[] a, RectInt[] b)
        {
            if (a == null || b == null)
                return a == b;
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].x != b[i].x || a[i].y != b[i].y ||
                    a[i].width != b[i].width || a[i].height != b[i].height)
                    return false;
            }
            return true;
        }

        static bool CellPivotsEqual(List<SpriteCellPivot> a, List<SpriteCellPivot> b)
        {
            if (a == null || b == null)
                return a == b;
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].CellIndex != b[i].CellIndex ||
                    a[i].X != b[i].X || a[i].Y != b[i].Y)
                    return false;
            }
            return true;
        }

        static List<int> CollectSourceSheets(Plan plan)
        {
            var list = new List<int>(plan.Sheets.Count);
            for (int i = 0; i < plan.Sheets.Count; i++)
                list.Add(plan.Sheets[i].SourceSheetIndex);
            return list;
        }

        static List<int> CollectSourceAppearances(Plan plan)
        {
            var list = new List<int>(plan.Appearances.Count);
            for (int i = 0; i < plan.Appearances.Count; i++)
                list.Add(plan.Appearances[i].SourceAppearanceIndex);
            return list;
        }
    }
}

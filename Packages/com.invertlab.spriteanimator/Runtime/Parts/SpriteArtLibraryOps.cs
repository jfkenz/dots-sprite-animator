using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Attach / detach / Pull-Sync shared <see cref="SpriteArtLibrary"/> assets
    /// onto a profile. Planning is read-only; Apply is atomic. Cycles in nested
    /// library graphs are refused. Bake uses <see cref="ResolveForBake"/> so the
    /// blob embeds concrete sheet/appearance data and never looks up the library
    /// ScriptableObject during play.
    /// </summary>
    public static class SpriteArtLibraryOps
    {
        public delegate SpriteArtLibrary LibraryResolver(string guid);

        public struct AttachResult
        {
            public bool Ok;
            public string Reason;
            public string Summary;
        }

        public struct DetachResult
        {
            public bool Ok;
            public string Reason;
            public string Summary;
        }

        public sealed class SyncPlan
        {
            public bool Ok;
            public string Reason;
            public string LibraryGuid;
            public string LibraryName;
            public readonly List<string> Notes = new();
            public readonly List<string> Added = new();
            public readonly List<string> Updated = new();
            public readonly List<string> Reused = new();
            public int SheetsAdded;
            public int SheetsUpdated;
            public int SheetsReused;
            public int AppearancesAdded;
            public int AppearancesUpdated;
            public int AppearancesReused;

            public string Summary =>
                $"{SheetsAdded} sheet(s) added, {SheetsUpdated} updated, {SheetsReused} reused | " +
                $"{AppearancesAdded} appearance(s) added, {AppearancesUpdated} updated, {AppearancesReused} reused";
        }

        public struct ApplyResult
        {
            public bool Ok;
            public string Reason;
            public string Summary;
        }

        /// <summary>Editor AssetDatabase GUID lookup. Null / unused in play mode.</summary>
        public static LibraryResolver EditorResolver
        {
            get
            {
#if UNITY_EDITOR
                return LoadByGuid;
#else
                return null;
#endif
            }
        }

#if UNITY_EDITOR
        public static SpriteArtLibrary LoadByGuid(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
                return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                return null;
            return AssetDatabase.LoadAssetAtPath<SpriteArtLibrary>(path);
        }

        public static string IdentityOf(SpriteArtLibrary library)
        {
            if (library == null)
                return string.Empty;
            string path = AssetDatabase.GetAssetPath(library);
            if (!string.IsNullOrEmpty(path))
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid))
                    return guid;
            }
            return "instance:" + library.GetEntityId();
        }

        public static string PathOf(SpriteArtLibrary library)
        {
            if (library == null)
                return string.Empty;
            string path = AssetDatabase.GetAssetPath(library);
            return string.IsNullOrEmpty(path) ? string.Empty : path;
        }
#else
        public static string IdentityOf(SpriteArtLibrary library)
            => library == null ? string.Empty : "instance:" + library.GetEntityId();

        public static string PathOf(SpriteArtLibrary library) => string.Empty;
#endif

        public static bool TryDetectCycle(
            SpriteArtLibrary root,
            LibraryResolver resolver,
            out string path,
            string rootGuid = null)
        {
            path = string.Empty;
            if (root == null)
                return false;
            string origin = string.IsNullOrWhiteSpace(rootGuid) ? IdentityOf(root) : rootGuid.Trim();
            if (string.IsNullOrEmpty(origin))
                return false;
            var stack = new List<string>();
            return WalkCycle(root, origin, origin, resolver, stack, out path);
        }

        public static bool WouldCreateCycle(
            SpriteArtLibrary from,
            SpriteArtLibrary to,
            LibraryResolver resolver,
            out string path,
            string fromGuid = null,
            string toGuid = null)
        {
            path = string.Empty;
            if (from == null || to == null)
                return false;
            fromGuid = string.IsNullOrWhiteSpace(fromGuid) ? IdentityOf(from) : fromGuid.Trim();
            toGuid = string.IsNullOrWhiteSpace(toGuid) ? IdentityOf(to) : toGuid.Trim();
            if (string.IsNullOrEmpty(fromGuid) || string.IsNullOrEmpty(toGuid))
                return false;
            if (string.Equals(fromGuid, toGuid, StringComparison.Ordinal))
            {
                path = (from.name ?? fromGuid) + " → " + (to.name ?? toGuid);
                return true;
            }
            var stack = new List<string> { fromGuid };
            return WalkCycle(to, toGuid, fromGuid, resolver, stack, out path);
        }

        static bool WalkCycle(
            SpriteArtLibrary node,
            string nodeId,
            string originId,
            LibraryResolver resolver,
            List<string> stack,
            out string path)
        {
            path = string.Empty;
            if (node == null || string.IsNullOrEmpty(nodeId))
                return false;
            if (stack.Contains(nodeId))
            {
                path = FormatCyclePath(stack, nodeId, node.name);
                return true;
            }
            stack.Add(nodeId);
            var nested = node.NestedLibraries;
            if (nested != null)
            {
                for (int i = 0; i < nested.Count; i++)
                {
                    var link = nested[i];
                    if (link == null || string.IsNullOrWhiteSpace(link.LibraryGuid))
                        continue;
                    string childId = link.LibraryGuid.Trim();
                    if (string.Equals(childId, originId, StringComparison.Ordinal) ||
                        stack.Contains(childId))
                    {
                        var cycleStack = new List<string>(stack) { childId };
                        path = FormatCyclePath(cycleStack, originId, link.LibraryName);
                        return true;
                    }
                    var child = resolver != null ? resolver(childId) : null;
                    if (child == null)
                        continue;
                    if (WalkCycle(child, childId, originId, resolver, stack, out path))
                        return true;
                }
            }
            stack.RemoveAt(stack.Count - 1);
            return false;
        }

        static string FormatCyclePath(List<string> stack, string closingId, string closingName)
        {
            var parts = new List<string>(stack);
            if (!string.IsNullOrEmpty(closingName))
                parts.Add(closingName);
            else
                parts.Add(closingId);
            return string.Join(" → ", parts.ToArray());
        }

        /// <summary>Attach a library reference. Refuses nested cycles. Idempotent on the same GUID.</summary>
        public static AttachResult Attach(
            SpriteSheetProfile profile,
            SpriteArtLibrary library,
            string guid = null,
            string path = null,
            LibraryResolver resolver = null)
        {
            if (profile == null)
                return new AttachResult { Reason = "Profile is null." };
            if (library == null)
                return new AttachResult { Reason = "Library is null." };
            profile.EnsurePartsRig();
            guid = string.IsNullOrWhiteSpace(guid) ? IdentityOf(library) : guid.Trim();
            if (string.IsNullOrEmpty(guid))
                return new AttachResult { Reason = "Library has no identity (save the asset or pass a GUID)." };
            path = path ?? PathOf(library);
            resolver ??= DefaultResolver(library, guid);

            if (TryDetectCycle(library, resolver, out string cycle, guid))
            {
                return new AttachResult
                {
                    Reason = "Refused: library graph contains a cycle (" + cycle + ").",
                };
            }

            profile.ArtLibraries ??= new List<SpriteArtLibraryLink>();
            for (int i = 0; i < profile.ArtLibraries.Count; i++)
            {
                var existing = profile.ArtLibraries[i];
                if (existing != null &&
                    string.Equals(existing.LibraryGuid, guid, StringComparison.Ordinal))
                {
                    existing.LibraryPath = path ?? existing.LibraryPath;
                    existing.LibraryName = library.name;
                    return new AttachResult
                    {
                        Ok = true,
                        Summary = "Library already attached: " + library.name,
                    };
                }
            }

            profile.ArtLibraries.Add(new SpriteArtLibraryLink
            {
                LibraryGuid = guid,
                LibraryPath = path ?? string.Empty,
                LibraryName = library.name,
            });
            return new AttachResult
            {
                Ok = true,
                Summary = "Attached library '" + library.name + "'",
            };
        }

        public static DetachResult Detach(SpriteSheetProfile profile, string guid)
        {
            if (profile == null)
                return new DetachResult { Reason = "Profile is null." };
            if (string.IsNullOrWhiteSpace(guid))
                return new DetachResult { Reason = "Library GUID is empty." };
            profile.ArtLibraries ??= new List<SpriteArtLibraryLink>();
            for (int i = 0; i < profile.ArtLibraries.Count; i++)
            {
                var link = profile.ArtLibraries[i];
                if (link == null || !string.Equals(link.LibraryGuid, guid, StringComparison.Ordinal))
                    continue;
                string name = string.IsNullOrEmpty(link.LibraryName) ? guid : link.LibraryName;
                profile.ArtLibraries.RemoveAt(i);
                return new DetachResult
                {
                    Ok = true,
                    Summary = "Detached library '" + name + "'. Local copies stay; Pull/Sync no longer updates them.",
                };
            }
            return new DetachResult { Reason = "Library '" + guid + "' is not attached." };
        }

        public static SpriteSheetProfile AsProfileView(SpriteArtLibrary library)
        {
            var view = new SpriteSheetProfile
            {
                AnimKind = SpriteAnimKind.Parts,
                Sheets = library?.Sheets ?? new List<SpriteSheetDef>(),
                PartsAppearances = library?.Appearances ?? new List<SpritePartAppearanceDef>(),
            };
            return view;
        }

        /// <summary>
        /// Plan a Pull/Sync: copy new library art and replace previously-pulled
        /// items from this library. Local-only art is never deleted.
        /// </summary>
        public static SyncPlan PlanSync(
            SpriteSheetProfile destination,
            SpriteArtLibrary library,
            string guid = null,
            LibraryResolver resolver = null)
        {
            var plan = new SyncPlan();
            if (destination == null || library == null)
            {
                plan.Reason = "Destination profile and library must not be null.";
                return plan;
            }
            guid = string.IsNullOrWhiteSpace(guid) ? IdentityOf(library) : guid.Trim();
            plan.LibraryGuid = guid;
            plan.LibraryName = library.name;
            resolver ??= DefaultResolver(library, guid);

            if (TryDetectCycle(library, resolver, out string cycle, guid))
            {
                plan.Reason = "Refused: library graph contains a cycle (" + cycle + ").";
                return plan;
            }

            var flattened = FlattenLibraryArt(library, guid, resolver, plan);
            if (!plan.Ok && !string.IsNullOrEmpty(plan.Reason))
                return plan;

            destination.EnsureSheets();
            destination.EnsurePartsRig();
            var destSheets = destination.Sheets ?? new List<SpriteSheetDef>();
            var destApps = destination.PartsAppearances ?? new List<SpritePartAppearanceDef>();

            for (int i = 0; i < flattened.Sheets.Count; i++)
            {
                var sourceSheet = flattened.Sheets[i];
                if (sourceSheet == null || sourceSheet.Texture == null)
                {
                    plan.Notes.Add("Skipped textureless library sheet '" + (sourceSheet?.Name ?? i.ToString()) + "'.");
                    continue;
                }
                string itemId = SheetItemId(sourceSheet, i);
                int existing = FindLibrarySheet(destSheets, guid, itemId);
                if (existing < 0)
                    existing = FindExactSheet(destSheets, sourceSheet);
                if (existing >= 0)
                {
                    if (SpriteProfileArtImport.SheetDefinitionsEqual(destSheets[existing], sourceSheet) &&
                        LibraryItemMatches(destSheets[existing].Import, guid, itemId))
                    {
                        plan.SheetsReused++;
                        plan.Reused.Add("sheet '" + destSheets[existing].Name + "'");
                    }
                    else
                    {
                        plan.SheetsUpdated++;
                        plan.Updated.Add("sheet '" + (destSheets[existing].Name ?? sourceSheet.Name) + "'");
                    }
                }
                else
                {
                    plan.SheetsAdded++;
                    plan.Added.Add("sheet '" + sourceSheet.Name + "'");
                }
            }

            for (int i = 0; i < flattened.Appearances.Count; i++)
            {
                var sourceApp = flattened.Appearances[i];
                if (sourceApp == null)
                    continue;
                string sourceId = SpritePartIdUtility.Canonical(sourceApp.AppearanceId, sourceApp.Name);
                int existing = FindLibraryAppearance(destApps, guid, sourceId);
                if (existing >= 0)
                {
                    plan.AppearancesUpdated++;
                    plan.Updated.Add("appearance '" + destApps[existing].Name + "'");
                }
                else
                {
                    int exact = FindExactAppearance(destApps, destSheets, sourceApp, flattened.Sheets);
                    if (exact >= 0)
                    {
                        plan.AppearancesReused++;
                        plan.Reused.Add("appearance '" + destApps[exact].Name + "'");
                    }
                    else
                    {
                        plan.AppearancesAdded++;
                        plan.Added.Add("appearance '" + (sourceApp.Name ?? sourceId) + "'");
                    }
                }
            }

            plan.Ok = true;
            return plan;
        }

        public static ApplyResult Sync(
            SpriteSheetProfile destination,
            SpriteArtLibrary library,
            SpriteProfileArtImport.ImportSourceInfo sourceInfo = default,
            string guid = null,
            string importedUtc = null,
            LibraryResolver resolver = null)
        {
            var plan = PlanSync(destination, library, guid, resolver);
            if (!plan.Ok)
                return new ApplyResult { Reason = plan.Reason };
            return ApplySync(plan, destination, library, sourceInfo, importedUtc, resolver);
        }

        public static ApplyResult ApplySync(
            SyncPlan plan,
            SpriteSheetProfile destination,
            SpriteArtLibrary library,
            SpriteProfileArtImport.ImportSourceInfo sourceInfo = default,
            string importedUtc = null,
            LibraryResolver resolver = null)
        {
            if (plan == null || !plan.Ok)
                return new ApplyResult { Reason = plan?.Reason ?? "Plan is null." };
            if (destination == null || library == null)
                return new ApplyResult { Reason = "Destination profile and library must not be null." };

            string guid = string.IsNullOrWhiteSpace(plan.LibraryGuid)
                ? IdentityOf(library)
                : plan.LibraryGuid;
            resolver ??= DefaultResolver(library, guid);
            var flattened = FlattenLibraryArt(library, guid, resolver, new SyncPlan());
            destination.EnsureSheets();
            destination.EnsurePartsRig();
            destination.Sheets ??= new List<SpriteSheetDef>();
            destination.PartsAppearances ??= new List<SpritePartAppearanceDef>();

            int sheetMark = destination.Sheets.Count;
            int appMark = destination.PartsAppearances.Count;
            var replacedSheets = new List<KeyValuePair<int, SpriteSheetDef>>();
            var replacedApps = new List<KeyValuePair<int, SpritePartAppearanceDef>>();
            var sheetMap = new Dictionary<int, int>();
            try
            {
                for (int i = 0; i < flattened.Sheets.Count; i++)
                {
                    var sourceSheet = flattened.Sheets[i];
                    if (sourceSheet == null || sourceSheet.Texture == null)
                        continue;
                    string itemId = SheetItemId(sourceSheet, i);
                    int existing = FindLibrarySheet(destination.Sheets, guid, itemId);
                    if (existing < 0)
                        existing = FindExactSheet(destination.Sheets, sourceSheet);

                    var stamp = SpriteProfileArtImport.StampProvenance(
                        sourceInfo,
                        "library sheet '" + sourceSheet.Name + "' [" + i + "]",
                        importedUtc, guid, itemId);

                    if (existing >= 0 &&
                        SpriteProfileArtImport.SheetDefinitionsEqual(destination.Sheets[existing], sourceSheet) &&
                        LibraryItemMatches(destination.Sheets[existing].Import, guid, itemId))
                    {
                        sheetMap[i] = existing;
                        continue;
                    }

                    if (existing >= 0)
                    {
                        replacedSheets.Add(new KeyValuePair<int, SpriteSheetDef>(
                            existing, destination.Sheets[existing]));
                        var copy = SpriteProfileArtImport.CloneSheet(
                            sourceSheet, destination.Sheets[existing].Name);
                        copy.Import = stamp;
                        destination.Sheets[existing] = copy;
                        sheetMap[i] = existing;
                    }
                    else
                    {
                        var copy = SpriteProfileArtImport.CloneSheet(
                            sourceSheet, UniqueSheetName(sourceSheet.Name, destination));
                        copy.Import = stamp;
                        destination.Sheets.Add(copy);
                        sheetMap[i] = destination.Sheets.Count - 1;
                    }
                }

                var takenIds = new HashSet<string>(StringComparer.Ordinal);
                var takenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < destination.PartsAppearances.Count; i++)
                {
                    var destApp = destination.PartsAppearances[i];
                    if (destApp == null) continue;
                    takenIds.Add(SpritePartIdUtility.Canonical(destApp.AppearanceId, destApp.Name));
                    if (!string.IsNullOrWhiteSpace(destApp.Name))
                        takenNames.Add(destApp.Name);
                }

                for (int i = 0; i < flattened.Appearances.Count; i++)
                {
                    var sourceApp = flattened.Appearances[i];
                    if (sourceApp == null) continue;
                    if (!sheetMap.TryGetValue(sourceApp.SheetIndex, out int destSheet))
                        continue;
                    string sourceId = SpritePartIdUtility.Canonical(sourceApp.AppearanceId, sourceApp.Name);
                    int existing = FindLibraryAppearance(destination.PartsAppearances, guid, sourceId);
                    var stamp = SpriteProfileArtImport.StampProvenance(
                        sourceInfo,
                        "library appearance '" + sourceId + "'",
                        importedUtc, guid, sourceId);

                    if (existing >= 0)
                    {
                        replacedApps.Add(new KeyValuePair<int, SpritePartAppearanceDef>(
                            existing, destination.PartsAppearances[existing]));
                        var target = destination.PartsAppearances[existing];
                        var copy = SpriteProfileArtImport.CloneAppearance(
                            sourceApp, target.Name, target.AppearanceId, destSheet);
                        copy.Import = stamp;
                        destination.PartsAppearances[existing] = copy;
                    }
                    else
                    {
                        int exact = FindExactAppearance(
                            destination.PartsAppearances, destination.Sheets, sourceApp, flattened.Sheets);
                        if (exact >= 0)
                        {
                            var destApp = destination.PartsAppearances[exact];
                            destApp.Import = stamp;
                            destApp.SemanticRole = sourceApp.SemanticRole;
                            continue;
                        }

                        string id = UniqueCopiedId(sourceId, takenIds);
                        string name = UniqueCopiedName(sourceApp.Name, id, takenNames);
                        takenIds.Add(id);
                        takenNames.Add(name);
                        var copy = SpriteProfileArtImport.CloneAppearance(sourceApp, name, id, destSheet);
                        copy.Import = stamp;
                        destination.PartsAppearances.Add(copy);
                    }
                }

                SpritePartsValidation.CanonicalizeIds(destination);
            }
            catch (Exception ex)
            {
                while (destination.Sheets.Count > sheetMark)
                    destination.Sheets.RemoveAt(destination.Sheets.Count - 1);
                while (destination.PartsAppearances.Count > appMark)
                    destination.PartsAppearances.RemoveAt(destination.PartsAppearances.Count - 1);
                for (int i = replacedSheets.Count - 1; i >= 0; i--)
                    destination.Sheets[replacedSheets[i].Key] = replacedSheets[i].Value;
                for (int i = replacedApps.Count - 1; i >= 0; i--)
                    destination.PartsAppearances[replacedApps[i].Key] = replacedApps[i].Value;
                return new ApplyResult { Reason = "Sync aborted, destination unchanged: " + ex.Message };
            }

            return new ApplyResult { Ok = true, Summary = plan.Summary };
        }

        /// <summary>
        /// Bake-time flatten: if local sheets/appearances already have textures,
        /// returns <paramref name="profile"/> unchanged (library asset not required).
        /// Otherwise copies library art into a throwaway profile so the blob embeds
        /// concrete data. Never a live ScriptableObject lookup during play.
        /// </summary>
        public static SpriteSheetProfile ResolveForBake(
            SpriteSheetProfile profile,
            out string error,
            LibraryResolver resolver = null)
        {
            error = null;
            if (profile == null)
            {
                error = "Profile is null.";
                return null;
            }
            if (HasCompleteLocalArt(profile))
                return profile;

            var links = profile.ArtLibraries;
            if (links == null || links.Count == 0)
            {
                error = "Profile art is incomplete and no art libraries are attached. Sync from a library or import art first.";
                return null;
            }

            resolver ??= EditorResolver;
            var copy = CloneProfileArt(profile);
            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                if (link == null || string.IsNullOrWhiteSpace(link.LibraryGuid))
                    continue;
                var library = resolver != null ? resolver(link.LibraryGuid) : null;
                if (library == null)
                {
                    if (HasCompleteLocalArt(copy))
                        continue;
                    error = "Bake needs library '" +
                            (string.IsNullOrEmpty(link.LibraryName) ? link.LibraryGuid : link.LibraryName) +
                            "' to flatten unsynced art. Pull/Sync first, or keep the library asset available at bake.";
                    return null;
                }
                var synced = Sync(copy, library, default, link.LibraryGuid, null, resolver);
                if (!synced.Ok)
                {
                    error = synced.Reason;
                    return null;
                }
            }

            if (!HasCompleteLocalArt(copy))
            {
                error = "Bake could not flatten library art into concrete sheets/appearances.";
                return null;
            }
            return copy;
        }

        public static bool HasCompleteLocalArt(SpriteSheetProfile profile)
        {
            if (profile?.PartsAppearances == null || profile.PartsAppearances.Count == 0)
                return true;
            profile.EnsureSheets();
            var sheets = profile.Sheets;
            for (int i = 0; i < profile.PartsAppearances.Count; i++)
            {
                var app = profile.PartsAppearances[i];
                if (app == null)
                    continue;
                if (sheets == null || app.SheetIndex < 0 || app.SheetIndex >= sheets.Count)
                    return false;
                if (sheets[app.SheetIndex]?.Texture == null)
                    return false;
            }
            return true;
        }

        public static bool HasLibraryProvenance(SpriteImportProvenance provenance)
        {
            return provenance != null && !string.IsNullOrEmpty(provenance.LibraryGuid);
        }

        static LibraryResolver DefaultResolver(SpriteArtLibrary library, string guid)
        {
            var editor = EditorResolver;
            return id =>
            {
                if (!string.IsNullOrEmpty(guid) &&
                    string.Equals(id, guid, StringComparison.Ordinal))
                    return library;
                return editor != null ? editor(id) : null;
            };
        }

        sealed class FlattenedArt
        {
            public readonly List<SpriteSheetDef> Sheets = new();
            public readonly List<SpritePartAppearanceDef> Appearances = new();
        }

        static FlattenedArt FlattenLibraryArt(
            SpriteArtLibrary library, string guid, LibraryResolver resolver, SyncPlan plan)
        {
            var result = new FlattenedArt();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (!AppendLibraryArt(library, guid, resolver, seen, result, plan))
                return result;
            if (string.IsNullOrEmpty(plan.Reason))
                plan.Ok = true;
            return result;
        }

        static bool AppendLibraryArt(
            SpriteArtLibrary library,
            string guid,
            LibraryResolver resolver,
            HashSet<string> seen,
            FlattenedArt result,
            SyncPlan plan)
        {
            if (library == null)
                return true;
            string id = string.IsNullOrEmpty(guid) ? IdentityOf(library) : guid;
            if (!string.IsNullOrEmpty(id) && !seen.Add(id))
            {
                plan.Reason = "Refused: library graph contains a cycle at '" +
                              (string.IsNullOrEmpty(library.name) ? id : library.name) + "'.";
                plan.Ok = false;
                return false;
            }

            if (library.NestedLibraries != null && resolver != null)
            {
                for (int i = 0; i < library.NestedLibraries.Count; i++)
                {
                    var link = library.NestedLibraries[i];
                    if (link == null || string.IsNullOrWhiteSpace(link.LibraryGuid))
                        continue;
                    var nested = resolver(link.LibraryGuid);
                    if (nested == null)
                    {
                        plan.Notes.Add("Nested library '" +
                                       (string.IsNullOrEmpty(link.LibraryName) ? link.LibraryGuid : link.LibraryName) +
                                       "' is missing and was skipped.");
                        continue;
                    }
                    if (!AppendLibraryArt(nested, link.LibraryGuid, resolver, seen, result, plan))
                        return false;
                }
            }

            int sheetBase = result.Sheets.Count;
            if (library.Sheets != null)
            {
                for (int i = 0; i < library.Sheets.Count; i++)
                    result.Sheets.Add(library.Sheets[i]);
            }
            if (library.Appearances != null)
            {
                for (int i = 0; i < library.Appearances.Count; i++)
                {
                    var app = library.Appearances[i];
                    if (app == null)
                    {
                        result.Appearances.Add(null);
                        continue;
                    }
                    result.Appearances.Add(new SpritePartAppearanceDef
                    {
                        Name = app.Name,
                        AppearanceId = app.AppearanceId,
                        SheetIndex = app.SheetIndex + sheetBase,
                        CellIndex = app.CellIndex,
                        LogicalWorldSize = app.LogicalWorldSize,
                        PivotSource = app.PivotSource,
                        PivotOverride = app.PivotOverride,
                        SemanticRole = app.SemanticRole,
                        Import = app.Import,
                    });
                }
            }
            return true;
        }

        static SpriteSheetProfile CloneProfileArt(SpriteSheetProfile source)
        {
            var copy = new SpriteSheetProfile
            {
                AnimKind = source.AnimKind,
                PartsSchemaVersion = source.PartsSchemaVersion,
                PartsDefaultClipId = source.PartsDefaultClipId,
                PartsDefaultSkinId = source.PartsDefaultSkinId,
                Sheets = new List<SpriteSheetDef>(),
                PartsAppearances = new List<SpritePartAppearanceDef>(),
                PartsSlots = source.PartsSlots,
                PartsClips = source.PartsClips,
                PartsSkins = source.PartsSkins,
                PartsIkConstraints = source.PartsIkConstraints,
                PartsJiggles = source.PartsJiggles,
                PartsParams = source.PartsParams,
                PartsMixes = source.PartsMixes,
                PartsDefaultMix = source.PartsDefaultMix,
                PartsDefaultMixEase = source.PartsDefaultMixEase,
                PartsFadeOutEvents = source.PartsFadeOutEvents,
                PartsMasks = source.PartsMasks,
                PartsBlendSpaces = source.PartsBlendSpaces,
                ArtLibraries = source.ArtLibraries,
            };
            if (source.Sheets != null)
            {
                for (int i = 0; i < source.Sheets.Count; i++)
                {
                    var sheet = source.Sheets[i];
                    copy.Sheets.Add(sheet == null
                        ? null
                        : SpriteProfileArtImport.CloneSheet(sheet, sheet.Name));
                    if (copy.Sheets[i] != null)
                        copy.Sheets[i].Import = sheet.Import;
                }
            }
            if (source.PartsAppearances != null)
            {
                for (int i = 0; i < source.PartsAppearances.Count; i++)
                {
                    var app = source.PartsAppearances[i];
                    if (app == null)
                    {
                        copy.PartsAppearances.Add(null);
                        continue;
                    }
                    var cloned = SpriteProfileArtImport.CloneAppearance(
                        app, app.Name, app.AppearanceId, app.SheetIndex);
                    cloned.Import = app.Import;
                    copy.PartsAppearances.Add(cloned);
                }
            }
            return copy;
        }

        static string SheetItemId(SpriteSheetDef sheet, int index)
            => "sheet[" + index + "]:" + (sheet?.Name ?? "Sheet");

        static bool LibraryItemMatches(SpriteImportProvenance provenance, string guid, string itemId)
        {
            return provenance != null &&
                   string.Equals(provenance.LibraryGuid, guid, StringComparison.Ordinal) &&
                   string.Equals(provenance.LibraryItemId, itemId, StringComparison.Ordinal);
        }

        static int FindLibrarySheet(List<SpriteSheetDef> destSheets, string guid, string itemId)
        {
            for (int i = 0; i < destSheets.Count; i++)
            {
                if (LibraryItemMatches(destSheets[i]?.Import, guid, itemId))
                    return i;
            }
            return -1;
        }

        static int FindLibraryAppearance(List<SpritePartAppearanceDef> destApps, string guid, string itemId)
        {
            for (int i = 0; i < destApps.Count; i++)
            {
                if (LibraryItemMatches(destApps[i]?.Import, guid, itemId))
                    return i;
            }
            return -1;
        }

        static int FindExactSheet(List<SpriteSheetDef> destSheets, SpriteSheetDef sourceSheet)
        {
            for (int i = 0; i < destSheets.Count; i++)
            {
                if (SpriteProfileArtImport.SheetDefinitionsEqual(destSheets[i], sourceSheet))
                    return i;
            }
            return -1;
        }

        static int FindExactAppearance(
            List<SpritePartAppearanceDef> destApps,
            List<SpriteSheetDef> destSheets,
            SpritePartAppearanceDef sourceApp,
            List<SpriteSheetDef> sourceSheets)
        {
            string sourceId = SpritePartIdUtility.Canonical(sourceApp.AppearanceId, sourceApp.Name);
            var sourceSheet = sourceApp.SheetIndex >= 0 && sourceApp.SheetIndex < sourceSheets.Count
                ? sourceSheets[sourceApp.SheetIndex]
                : null;
            for (int i = 0; i < destApps.Count; i++)
            {
                var destApp = destApps[i];
                if (destApp == null)
                    continue;
                if (SpritePartIdUtility.Canonical(destApp.AppearanceId, destApp.Name) != sourceId)
                    continue;
                if (destApp.CellIndex != sourceApp.CellIndex ||
                    destApp.LogicalWorldSize != sourceApp.LogicalWorldSize ||
                    destApp.PivotSource != sourceApp.PivotSource ||
                    destApp.PivotOverride != sourceApp.PivotOverride ||
                    destApp.SemanticRole != sourceApp.SemanticRole)
                    continue;
                var destSheet = destApp.SheetIndex >= 0 && destApp.SheetIndex < destSheets.Count
                    ? destSheets[destApp.SheetIndex]
                    : null;
                if (SpriteProfileArtImport.SheetDefinitionsEqual(destSheet, sourceSheet))
                    return i;
            }
            return -1;
        }

        static string UniqueSheetName(string sourceName, SpriteSheetProfile destination)
        {
            string baseName = string.IsNullOrWhiteSpace(sourceName) ? "Sheet" : sourceName.Trim();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (destination.Sheets != null)
            {
                for (int i = 0; i < destination.Sheets.Count; i++)
                {
                    var sheet = destination.Sheets[i];
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

        static string UniqueCopiedId(string desired, HashSet<string> taken)
        {
            string id = desired;
            int n = 2;
            while (taken.Contains(id))
                id = desired + n++;
            return id;
        }

        static string UniqueCopiedName(string sourceName, string fallbackId, HashSet<string> taken)
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
    }
}

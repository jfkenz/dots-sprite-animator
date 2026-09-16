using System;
using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Semantic outfit mapping: bind a source skin/outfit onto a destination
    /// profile by <see cref="SpritePartSemanticRole"/>, not only SlotId.
    /// Appearance/skin binding only — does not retarget motion curves.
    /// Many-to-one role conflicts block Apply; unmapped dest slots stay unchanged.
    /// </summary>
    public static class SpritePartsOutfitMapping
    {
        public sealed class SlotBinding
        {
            public string DestinationSlotId;
            public SpritePartSemanticRole Role;
            public string SourceAppearanceId;
            public string DestinationAppearanceId;
            public bool Unmapped;
        }

        public sealed class Plan
        {
            public bool Ok;
            public string Reason;
            public int SourceSkinIndex = -1;
            public string SourceSkinName;
            public readonly List<SlotBinding> Bindings = new List<SlotBinding>();
            public readonly List<string> Conflicts = new List<string>();
            public readonly List<string> Unmapped = new List<string>();
            public readonly List<int> AppearancesToImport = new List<int>();
            public SpriteProfileArtImport.Plan Art = new SpriteProfileArtImport.Plan { Ok = true };

            public string Summary
            {
                get
                {
                    if (!Ok) return Reason ?? "Invalid plan.";
                    int mapped = 0;
                    for (int i = 0; i < Bindings.Count; i++)
                        if (!Bindings[i].Unmapped) mapped++;
                    return $"Apply Outfit: {mapped} slot(s) by role, {Unmapped.Count} unmapped" +
                           (Art != null && Art.Ok && (Art.SheetsAdded + Art.AppearancesAdded) > 0
                               ? "; " + Art.Summary
                               : string.Empty);
                }
            }
        }

        public struct ApplyResult
        {
            public bool Ok;
            public string Reason;
            public string Summary;
        }

        public static Plan PlanApply(
            SpriteSheetProfile source,
            int sourceSkinIndex,
            SpriteSheetProfile destination)
        {
            var plan = new Plan { SourceSkinIndex = sourceSkinIndex };
            if (source == null || destination == null)
            {
                plan.Reason = "Source and destination profiles must not be null.";
                return plan;
            }
            destination.EnsurePartsRig();
            source.EnsurePartsRig();

            SpritePartsSkinDef skin = null;
            if (sourceSkinIndex >= 0)
            {
                if (source.PartsSkins == null || sourceSkinIndex >= source.PartsSkins.Count ||
                    source.PartsSkins[sourceSkinIndex] == null)
                {
                    plan.Reason = "Source skin is missing.";
                    return plan;
                }
                skin = source.PartsSkins[sourceSkinIndex];
                plan.SourceSkinName = skin.Name;
            }

            var sourceByRole = new Dictionary<SpritePartSemanticRole, List<string>>();
            CollectSourceAppearancesByRole(source, skin, sourceByRole);

            var destSlots = destination.PartsSlots ?? new List<SpritePartSlotDef>();
            bool anyRole = false;
            for (int i = 0; i < destSlots.Count; i++)
            {
                var slot = destSlots[i];
                if (slot == null || slot.SemanticRole == SpritePartSemanticRole.None)
                    continue;
                anyRole = true;
                string destId = SpritePartIdUtility.Canonical(slot.SlotId, slot.Name);
                var binding = new SlotBinding
                {
                    DestinationSlotId = destId,
                    Role = slot.SemanticRole,
                };

                if (!sourceByRole.TryGetValue(slot.SemanticRole, out var candidates) ||
                    candidates.Count == 0)
                {
                    binding.Unmapped = true;
                    plan.Unmapped.Add(
                        $"slot '{slot.Name}' ({slot.SemanticRole}) has no source appearance with that role");
                    plan.Bindings.Add(binding);
                    continue;
                }

                if (candidates.Count > 1)
                {
                    plan.Conflicts.Add(
                        $"Many-to-one blocked: role {slot.SemanticRole} has {candidates.Count} source appearances " +
                        $"({string.Join(", ", candidates.ToArray())}) for destination slot '{slot.Name}'.");
                    plan.Bindings.Add(binding);
                    continue;
                }

                binding.SourceAppearanceId = candidates[0];
                plan.Bindings.Add(binding);
            }

            if (!anyRole)
            {
                plan.Reason = "Destination has no slots with a semantic role. Assign Body/Head/Weapon/Offhand first.";
                return plan;
            }

            if (plan.Conflicts.Count > 0)
            {
                plan.Reason = plan.Conflicts[0];
                return plan;
            }

            bool crossProfile = !ReferenceEquals(source, destination);
            if (crossProfile)
            {
                var needed = new SortedSet<int>();
                for (int i = 0; i < plan.Bindings.Count; i++)
                {
                    var b = plan.Bindings[i];
                    if (b.Unmapped || string.IsNullOrEmpty(b.SourceAppearanceId))
                        continue;
                    int index = FindAppearanceIndex(source, b.SourceAppearanceId);
                    if (index < 0)
                    {
                        plan.Reason = $"Source appearance '{b.SourceAppearanceId}' is missing.";
                        return plan;
                    }
                    needed.Add(index);
                }
                plan.AppearancesToImport.AddRange(needed);
                plan.Art = SpriteProfileArtImport.PlanImport(
                    source, destination, null, plan.AppearancesToImport);
                if (!plan.Art.Ok)
                {
                    plan.Reason = plan.Art.Reason;
                    return plan;
                }
                for (int i = 0; i < plan.Bindings.Count; i++)
                {
                    var b = plan.Bindings[i];
                    if (b.Unmapped) continue;
                    b.DestinationAppearanceId = RemapPlannedAppearance(plan.Art, b.SourceAppearanceId);
                }
            }
            else
            {
                for (int i = 0; i < plan.Bindings.Count; i++)
                {
                    var b = plan.Bindings[i];
                    if (b.Unmapped) continue;
                    b.DestinationAppearanceId = b.SourceAppearanceId;
                }
            }

            plan.Ok = true;
            return plan;
        }

        public static ApplyResult Apply(
            Plan plan,
            SpriteSheetProfile source,
            SpriteSheetProfile destination,
            SpriteProfileArtImport.ImportSourceInfo sourceInfo = default,
            string importedUtc = null)
        {
            if (plan == null || !plan.Ok)
                return new ApplyResult { Reason = plan?.Reason ?? "Plan is null." };
            if (source == null || destination == null)
                return new ApplyResult { Reason = "Source and destination profiles must not be null." };

            var replay = PlanApply(source, plan.SourceSkinIndex, destination);
            if (!replay.Ok)
                return new ApplyResult { Reason = replay.Reason };

            var unchanged = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < replay.Bindings.Count; i++)
            {
                var b = replay.Bindings[i];
                if (!b.Unmapped) continue;
                var slot = SpritePartsAuthoringOps.FindSlot(destination, b.DestinationSlotId);
                unchanged[b.DestinationSlotId] = slot?.DefaultAppearanceId ?? string.Empty;
            }

            if (!ReferenceEquals(source, destination) &&
                replay.AppearancesToImport.Count > 0)
            {
                var art = SpriteProfileArtImport.Apply(
                    replay.Art, source, destination, sourceInfo, importedUtc);
                if (!art.Ok)
                    return new ApplyResult { Reason = art.Reason };
                for (int i = 0; i < replay.Bindings.Count; i++)
                {
                    var b = replay.Bindings[i];
                    if (b.Unmapped) continue;
                    b.DestinationAppearanceId = RemapAppliedAppearance(
                        art.AppearanceIdRemaps, b.SourceAppearanceId, b.DestinationAppearanceId);
                }
            }

            for (int i = 0; i < replay.Bindings.Count; i++)
            {
                var b = replay.Bindings[i];
                if (b.Unmapped || string.IsNullOrEmpty(b.DestinationAppearanceId))
                    continue;
                var bind = SpritePartsAuthoringOps.SetSlotDefaultAppearance(
                    destination, b.DestinationSlotId, b.DestinationAppearanceId);
                if (!bind.Ok)
                    return new ApplyResult { Reason = bind.Reason };
            }

            for (int i = 0; i < replay.Bindings.Count; i++)
            {
                var b = replay.Bindings[i];
                if (!b.Unmapped) continue;
                var slot = SpritePartsAuthoringOps.FindSlot(destination, b.DestinationSlotId);
                if (slot == null) continue;
                string expected = unchanged[b.DestinationSlotId];
                if ((slot.DefaultAppearanceId ?? string.Empty) != (expected ?? string.Empty))
                    slot.DefaultAppearanceId = expected;
            }

            SpritePartsValidation.CanonicalizeIds(destination);
            return new ApplyResult { Ok = true, Summary = replay.Summary };
        }

        static void CollectSourceAppearancesByRole(
            SpriteSheetProfile source,
            SpritePartsSkinDef skin,
            Dictionary<SpritePartSemanticRole, List<string>> into)
        {
            if (skin?.Bindings != null && skin.Bindings.Count > 0)
            {
                for (int i = 0; i < skin.Bindings.Count; i++)
                {
                    var binding = skin.Bindings[i];
                    if (binding == null || string.IsNullOrWhiteSpace(binding.AppearanceId))
                        continue;
                    string aid = SpritePartIdUtility.Canonical(binding.AppearanceId);
                    var app = SpritePartsAuthoringOps.FindAppearance(source, aid);
                    var slot = SpritePartsAuthoringOps.FindSlot(source, binding.SlotId);
                    var role = RoleOf(app, slot);
                    if (role == SpritePartSemanticRole.None)
                        continue;
                    AddRole(into, role, aid);
                }
                return;
            }

            if (source.PartsAppearances == null)
                return;
            for (int i = 0; i < source.PartsAppearances.Count; i++)
            {
                var app = source.PartsAppearances[i];
                if (app == null || app.SemanticRole == SpritePartSemanticRole.None)
                    continue;
                AddRole(into, app.SemanticRole,
                    SpritePartIdUtility.Canonical(app.AppearanceId, app.Name));
            }
        }

        static SpritePartSemanticRole RoleOf(SpritePartAppearanceDef app, SpritePartSlotDef slot)
        {
            if (app != null && app.SemanticRole != SpritePartSemanticRole.None)
                return app.SemanticRole;
            if (slot != null)
                return slot.SemanticRole;
            return SpritePartSemanticRole.None;
        }

        static void AddRole(
            Dictionary<SpritePartSemanticRole, List<string>> into,
            SpritePartSemanticRole role,
            string appearanceId)
        {
            if (!into.TryGetValue(role, out var list))
            {
                list = new List<string>();
                into[role] = list;
            }
            if (!list.Contains(appearanceId))
                list.Add(appearanceId);
        }

        static int FindAppearanceIndex(SpriteSheetProfile profile, string appearanceId)
        {
            if (profile?.PartsAppearances == null) return -1;
            string id = SpritePartIdUtility.Canonical(appearanceId);
            for (int i = 0; i < profile.PartsAppearances.Count; i++)
            {
                var app = profile.PartsAppearances[i];
                if (app != null && SpritePartIdUtility.Canonical(app.AppearanceId, app.Name) == id)
                    return i;
            }
            return -1;
        }

        static string RemapPlannedAppearance(SpriteProfileArtImport.Plan art, string sourceId)
        {
            if (art?.Appearances == null) return sourceId;
            for (int i = 0; i < art.Appearances.Count; i++)
            {
                var action = art.Appearances[i];
                if (action != null && action.SourceAppearanceId == sourceId)
                    return action.DestinationAppearanceId;
            }
            return sourceId;
        }

        static string RemapAppliedAppearance(
            List<KeyValuePair<string, string>> remaps, string sourceId, string fallback)
        {
            if (remaps == null) return fallback;
            for (int i = 0; i < remaps.Count; i++)
            {
                if (remaps[i].Key == sourceId)
                    return remaps[i].Value;
            }
            return fallback;
        }
    }
}

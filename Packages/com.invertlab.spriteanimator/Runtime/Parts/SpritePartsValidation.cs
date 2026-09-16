using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Authoring / bake validation for Parts profiles. No silent coerce.</summary>
    public static class SpritePartsValidation
    {
        public struct Result
        {
            public bool Ok;
            public List<string> Errors;

            public static Result Success() => new Result { Ok = true, Errors = new List<string>() };
            public static Result Fail(params string[] errors)
                => new Result { Ok = false, Errors = new List<string>(errors ?? Array.Empty<string>()) };
        }

        public static Result Validate(SpriteSheetProfile profile)
        {
            var errors = new List<string>();
            if (profile == null)
                return Result.Fail("Profile is null.");

            // Read-only: peeks (CanSetAnimKind) and failed switches must not
            // migrate legacy sheet fields or allocate Parts lists.
            if (profile.AnimKind != SpriteAnimKind.Parts)
                return Result.Success();

            ValidateSlots(profile, errors);
            ValidateAppearances(profile, errors);
            ValidateClips(profile, errors);
            ValidateSkins(profile, errors);
            return errors.Count == 0
                ? Result.Success()
                : new Result { Ok = false, Errors = errors };
        }

        public static void EnsureLists(SpriteSheetProfile profile)
        {
            if (profile == null) return;
            profile.PartsSlots ??= new List<SpritePartSlotDef>();
            profile.PartsAppearances ??= new List<SpritePartAppearanceDef>();
            profile.PartsClips ??= new List<SpritePartsClipDef>();
            profile.PartsSkins ??= new List<SpritePartsSkinDef>();
            profile.PartsDefaultClipId ??= string.Empty;
            profile.PartsDefaultSkinId ??= string.Empty;
            if (profile.PartsSchemaVersion <= 0)
                profile.PartsSchemaVersion = 1;
        }

        /// <summary>Canonicalize slot/appearance/skin IDs. Does not touch frame clip Names.</summary>
        public static void CanonicalizeIds(SpriteSheetProfile profile)
        {
            EnsureLists(profile);
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var slot = profile.PartsSlots[i];
                if (slot == null) continue;
                slot.SlotId = SpritePartIdUtility.Canonical(slot.SlotId, slot.Name);
                if (!string.IsNullOrWhiteSpace(slot.ParentSlotId))
                    slot.ParentSlotId = SpritePartIdUtility.Canonical(slot.ParentSlotId);
                else
                    slot.ParentSlotId = string.Empty;
                if (!string.IsNullOrWhiteSpace(slot.DefaultAppearanceId))
                    slot.DefaultAppearanceId = SpritePartIdUtility.Canonical(slot.DefaultAppearanceId);
            }
            for (int i = 0; i < profile.PartsAppearances.Count; i++)
            {
                var app = profile.PartsAppearances[i];
                if (app == null) continue;
                app.AppearanceId = SpritePartIdUtility.Canonical(app.AppearanceId, app.Name);
            }
            for (int i = 0; i < profile.PartsClips.Count; i++)
            {
                var clip = profile.PartsClips[i];
                if (clip == null) continue;
                clip.ClipId = SpritePartIdUtility.Canonical(clip.ClipId, clip.Name);
                clip.Tracks ??= new List<SpritePartsTrackDef>();
                for (int t = 0; t < clip.Tracks.Count; t++)
                {
                    var track = clip.Tracks[t];
                    if (track == null) continue;
                    track.SlotId = SpritePartIdUtility.Canonical(track.SlotId);
                    track.Keys ??= new List<SpritePartsKeyDef>();
                    for (int k = 0; k < track.Keys.Count; k++)
                    {
                        var key = track.Keys[k];
                        if (key == null) continue;
                        if (!string.IsNullOrWhiteSpace(key.AppearanceId))
                            key.AppearanceId = SpritePartIdUtility.Canonical(key.AppearanceId);
                        else
                            key.AppearanceId = string.Empty;
                    }
                }
            }
            for (int i = 0; i < profile.PartsSkins.Count; i++)
            {
                var skin = profile.PartsSkins[i];
                if (skin == null) continue;
                skin.SkinId = SpritePartIdUtility.Canonical(skin.SkinId, skin.Name);
                skin.Bindings ??= new List<SpritePartsSkinBindingDef>();
                for (int b = 0; b < skin.Bindings.Count; b++)
                {
                    var binding = skin.Bindings[b];
                    if (binding == null) continue;
                    binding.SlotId = SpritePartIdUtility.Canonical(binding.SlotId);
                    binding.AppearanceId = SpritePartIdUtility.Canonical(binding.AppearanceId);
                }
            }
            if (!string.IsNullOrWhiteSpace(profile.PartsDefaultClipId))
                profile.PartsDefaultClipId = SpritePartIdUtility.Canonical(profile.PartsDefaultClipId);
            if (!string.IsNullOrWhiteSpace(profile.PartsDefaultSkinId))
                profile.PartsDefaultSkinId = SpritePartIdUtility.Canonical(profile.PartsDefaultSkinId);
        }

        static void ValidateSlots(SpriteSheetProfile profile, List<string> errors)
        {
            var slots = profile.PartsSlots;
            if (slots == null || slots.Count == 0)
            {
                errors.Add("Parts profile has no slots.");
                return;
            }
            if (slots.Count > SpritePartIdUtility.MaxParts)
                errors.Add($"Parts supports at most {SpritePartIdUtility.MaxParts} slots (found {slots.Count}).");

            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            var hashes = new Dictionary<ulong, string>();
            var ranks = new HashSet<int>();

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null)
                {
                    errors.Add($"Slot[{i}] is null.");
                    continue;
                }
                string id = SpritePartIdUtility.Canonical(slot.SlotId, slot.Name);
                if (ids.ContainsKey(id))
                    errors.Add($"Duplicate SlotId '{id}'.");
                else
                    ids[id] = i;

                ulong hash = SpritePartIdUtility.Hash(id);
                if (hashes.TryGetValue(hash, out var other) && other != id)
                    errors.Add($"SlotId hash collision between '{other}' and '{id}'.");
                else
                    hashes[hash] = id;

                if (!IsFinite(slot.RestPosition) || !math.isfinite(slot.RestRotation) || !IsFinite(slot.RestScale))
                    errors.Add($"Slot '{id}' has non-finite rest transform.");
                if (math.abs(slot.RestScale.x) < 1e-5f || math.abs(slot.RestScale.y) < 1e-5f)
                    errors.Add($"Slot '{id}' rest scale axes must be non-zero (got {slot.RestScale}).");
                if (slot.DrawRank < 0 || slot.DrawRank >= SpritePartIdUtility.MaxParts)
                    errors.Add($"Slot '{id}' DrawRank must be in 0..{SpritePartIdUtility.MaxParts - 1}.");
                else if (!ranks.Add(slot.DrawRank))
                    errors.Add($"Duplicate DrawRank {slot.DrawRank} on slot '{id}'.");
            }

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;
                string id = SpritePartIdUtility.Canonical(slot.SlotId, slot.Name);
                if (string.IsNullOrWhiteSpace(slot.ParentSlotId))
                    continue;
                string parent = SpritePartIdUtility.Canonical(slot.ParentSlotId);
                if (!ids.ContainsKey(parent))
                    errors.Add($"Slot '{id}' ParentSlotId '{parent}' is missing.");
                else if (parent == id)
                    errors.Add($"Slot '{id}' cannot parent to itself.");
            }

            if (HasCycle(slots, ids))
                errors.Add("Parts slot hierarchy contains a cycle.");

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null || string.IsNullOrWhiteSpace(slot.DefaultAppearanceId))
                    continue;
                string appId = SpritePartIdUtility.Canonical(slot.DefaultAppearanceId);
                if (FindAppearanceIndex(profile, appId) < 0)
                    errors.Add($"Slot '{SpritePartIdUtility.Canonical(slot.SlotId, slot.Name)}' default appearance '{appId}' is missing.");
            }
        }

        static void ValidateAppearances(SpriteSheetProfile profile, List<string> errors)
        {
            var apps = profile.PartsAppearances;
            if (apps == null)
                return;
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            var hashes = new Dictionary<ulong, string>();
            int sheetCount = profile.Sheets?.Count ?? 0;

            for (int i = 0; i < apps.Count; i++)
            {
                var app = apps[i];
                if (app == null)
                {
                    errors.Add($"Appearance[{i}] is null.");
                    continue;
                }
                string id = SpritePartIdUtility.Canonical(app.AppearanceId, app.Name);
                if (ids.ContainsKey(id))
                    errors.Add($"Duplicate AppearanceId '{id}'.");
                else
                    ids[id] = i;

                ulong hash = SpritePartIdUtility.Hash(id);
                if (hashes.TryGetValue(hash, out var other) && other != id)
                    errors.Add($"AppearanceId hash collision between '{other}' and '{id}'.");
                else
                    hashes[hash] = id;

                if (sheetCount <= 0 || app.SheetIndex < 0 || app.SheetIndex >= sheetCount)
                    errors.Add($"Appearance '{id}' SheetIndex {app.SheetIndex} is out of range.");
                else
                {
                    var sheet = profile.Sheets[app.SheetIndex];
                    if (sheet == null || sheet.Texture == null)
                        errors.Add($"Appearance '{id}' references a sheet with no texture.");
                    else
                    {
                        int cells = math.max(1, sheet.Columns) * math.max(1, sheet.Rows);
                        if (app.CellIndex < 0 || app.CellIndex >= cells)
                            errors.Add($"Appearance '{id}' CellIndex {app.CellIndex} is out of range (0..{cells - 1}).");
                    }
                }

                if (app.LogicalWorldSize != Vector2.zero)
                {
                    if (!IsFinite(app.LogicalWorldSize) ||
                        app.LogicalWorldSize.x <= 0f || app.LogicalWorldSize.y <= 0f)
                        errors.Add($"Appearance '{id}' LogicalWorldSize must be > 0 when set.");
                }
            }
        }

        static void ValidateClips(SpriteSheetProfile profile, List<string> errors)
        {
            var clips = profile.PartsClips;
            if (clips == null)
                return;
            var clipIds = new Dictionary<string, int>(StringComparer.Ordinal);
            var hashes = new Dictionary<ulong, string>();
            var slotIds = BuildSlotSet(profile);

            for (int i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];
                if (clip == null)
                {
                    errors.Add($"PartsClip[{i}] is null.");
                    continue;
                }
                string id = SpritePartIdUtility.Canonical(clip.ClipId, clip.Name);
                if (clipIds.ContainsKey(id))
                    errors.Add($"Duplicate Parts ClipId '{id}'.");
                else
                    clipIds[id] = i;

                ulong hash = SpritePartIdUtility.Hash(id);
                if (hashes.TryGetValue(hash, out var other) && other != id)
                    errors.Add($"Parts ClipId hash collision between '{other}' and '{id}'.");
                else
                    hashes[hash] = id;

                // Preserve case-sensitive display Name for Play lookup (no canonicalize of Name).
                if (string.IsNullOrWhiteSpace(clip.Name))
                    errors.Add($"Parts clip '{id}' has an empty display Name.");

                if (!math.isfinite(clip.Duration) || clip.Duration <= 0f)
                    errors.Add($"Parts clip '{clip.Name}' Duration must be > 0.");
                if (!math.isfinite(clip.Speed))
                    errors.Add($"Parts clip '{clip.Name}' Speed must be finite.");
                if (clip.WrapMode != (byte)SpritePartsWrap.Loop &&
                    clip.WrapMode != (byte)SpritePartsWrap.Once)
                    errors.Add($"Parts clip '{clip.Name}' WrapMode must be Loop or Once.");

                var tracks = clip.Tracks;
                if (tracks == null)
                    continue;
                // One pose track and one appearance track per slot at most.
                var trackSlots = new HashSet<string>(StringComparer.Ordinal);
                var appearanceSlots = new HashSet<string>(StringComparer.Ordinal);
                for (int t = 0; t < clip.Tracks.Count; t++)
                {
                    var track = clip.Tracks[t];
                    if (track == null)
                    {
                        errors.Add($"Parts clip '{clip.Name}' track[{t}] is null.");
                        continue;
                    }
                    bool isAppearance = track.Kind == SpritePartsTrackKind.Appearance;
                    string slotId = SpritePartIdUtility.Canonical(track.SlotId);
                    var used = isAppearance ? appearanceSlots : trackSlots;
                    if (!used.Add(slotId))
                        errors.Add($"Parts clip '{clip.Name}' has duplicate {(isAppearance ? "appearance" : "pose")} track for slot '{slotId}'.");
                    if (!slotIds.Contains(slotId))
                        errors.Add($"Parts clip '{clip.Name}' track slot '{slotId}' is missing from the rig.");

                    var keys = track.Keys;
                    if (keys == null)
                        continue;
                    for (int k = 0; k < keys.Count; k++)
                    {
                        var key = track.Keys[k];
                        if (key == null)
                        {
                            errors.Add($"Parts clip '{clip.Name}' track '{slotId}' key[{k}] is null.");
                            continue;
                        }
                        if (!math.isfinite(key.Time) || key.Time < 0f || key.Time > clip.Duration + 1e-5f)
                            errors.Add($"Parts clip '{clip.Name}' track '{slotId}' key time {key.Time} must be in [0, Duration={clip.Duration}].");
                        if (isAppearance)
                        {
                            // Appearance keys carry no pose: only timing and the
                            // id need to be sane.
                            if (!string.IsNullOrWhiteSpace(key.AppearanceId))
                            {
                                string aid = SpritePartIdUtility.Canonical(key.AppearanceId);
                                if (FindAppearanceIndex(profile, aid) < 0)
                                    errors.Add($"Parts clip '{clip.Name}' track '{slotId}' key[{k}] appearance '{aid}' is missing from the profile.");
                            }
                            continue;
                        }
                        if (!IsFinite(key.Position) || !math.isfinite(key.Rotation) || !IsFinite(key.Scale))
                            errors.Add($"Parts clip '{clip.Name}' track '{slotId}' key[{k}] has non-finite values.");
                        if (math.abs(key.Scale.x) < 1e-5f || math.abs(key.Scale.y) < 1e-5f)
                            errors.Add($"Parts clip '{clip.Name}' track '{slotId}' key[{k}] scale axes must be non-zero.");
                        if (!SpriteEase.IsValidMode(key.EaseMode))
                            errors.Add($"Parts clip '{clip.Name}' track '{slotId}' key[{k}] has invalid EaseMode.");
                        if (!string.IsNullOrWhiteSpace(key.AppearanceId))
                        {
                            string aid = SpritePartIdUtility.Canonical(key.AppearanceId);
                            if (FindAppearanceIndex(profile, aid) < 0)
                                errors.Add($"Parts clip '{clip.Name}' track '{slotId}' key[{k}] appearance '{aid}' is missing from the profile.");
                        }
                    }
                }
            }
        }

        static void ValidateSkins(SpriteSheetProfile profile, List<string> errors)
        {
            var skins = profile.PartsSkins;
            if (skins == null)
                return;
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            var hashes = new Dictionary<ulong, string>();
            var slotIds = BuildSlotSet(profile);

            for (int i = 0; i < skins.Count; i++)
            {
                var skin = skins[i];
                if (skin == null)
                {
                    errors.Add($"PartsSkin[{i}] is null.");
                    continue;
                }
                string id = SpritePartIdUtility.Canonical(skin.SkinId, skin.Name);
                if (ids.ContainsKey(id))
                    errors.Add($"Duplicate SkinId '{id}'.");
                else
                    ids[id] = i;
                ulong hash = SpritePartIdUtility.Hash(id);
                if (hashes.TryGetValue(hash, out var other) && other != id)
                    errors.Add($"SkinId hash collision between '{other}' and '{id}'.");
                else
                    hashes[hash] = id;

                var bindings = skin.Bindings;
                if (bindings == null)
                    continue;
                for (int b = 0; b < bindings.Count; b++)
                {
                    var binding = bindings[b];
                    if (binding == null) continue;
                    string slotId = SpritePartIdUtility.Canonical(binding.SlotId);
                    string appId = SpritePartIdUtility.Canonical(binding.AppearanceId);
                    if (!slotIds.Contains(slotId))
                        errors.Add($"Skin '{id}' binding slot '{slotId}' is missing.");
                    if (FindAppearanceIndex(profile, appId) < 0)
                        errors.Add($"Skin '{id}' appearance '{appId}' is missing.");
                }
            }
        }

        static HashSet<string> BuildSlotSet(SpriteSheetProfile profile)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (profile.PartsSlots == null) return set;
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var slot = profile.PartsSlots[i];
                if (slot != null)
                    set.Add(SpritePartIdUtility.Canonical(slot.SlotId, slot.Name));
            }
            return set;
        }

        static int FindAppearanceIndex(SpriteSheetProfile profile, string appearanceId)
        {
            if (profile.PartsAppearances == null) return -1;
            for (int i = 0; i < profile.PartsAppearances.Count; i++)
            {
                var app = profile.PartsAppearances[i];
                if (app == null) continue;
                if (SpritePartIdUtility.Canonical(app.AppearanceId, app.Name) == appearanceId)
                    return i;
            }
            return -1;
        }

        static bool HasCycle(List<SpritePartSlotDef> slots, Dictionary<string, int> ids)
        {
            var state = new int[slots.Count]; // 0=unseen 1=visiting 2=done
            for (int i = 0; i < slots.Count; i++)
            {
                if (state[i] == 0 && Visit(i, slots, ids, state))
                    return true;
            }
            return false;
        }

        static bool Visit(int index, List<SpritePartSlotDef> slots,
            Dictionary<string, int> ids, int[] state)
        {
            state[index] = 1;
            var slot = slots[index];
            if (slot != null && !string.IsNullOrWhiteSpace(slot.ParentSlotId))
            {
                string parent = SpritePartIdUtility.Canonical(slot.ParentSlotId);
                if (ids.TryGetValue(parent, out int p))
                {
                    if (state[p] == 1) return true;
                    if (state[p] == 0 && Visit(p, slots, ids, state)) return true;
                }
            }
            state[index] = 2;
            return false;
        }

        static bool IsFinite(Vector2 v)
            => math.isfinite(v.x) && math.isfinite(v.y);
    }
}

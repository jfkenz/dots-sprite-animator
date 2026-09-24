using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Parts character authoring. Bakes pure-DOTS hierarchy: gameplay root,
    /// visual-root child for facing, and one entity per slot (max 32).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("DOTS Sprite Animator/Sprite Parts Character")]
    public class SpritePartsCharacterAuthoring : MonoBehaviour
    {
        [Tooltip("Parts profile (AnimKind must be Parts).")]
        public ScriptableSpriteSheetProfile Profile;

        [Tooltip("Starting Parts clip name (case-sensitive Play lookup). Empty = profile default / first.")]
        public string StartingClipName;

        [Tooltip("Starting skin id (canonical). Empty = defaults / profile default.")]
        public string StartingSkinId;

        public bool FlipX;
        public bool FlipY;

        [Tooltip("Character Order for Parts draw groups: drawIndex = order*64 + partRank.")]
        public int CharacterOrder;

        public bool PlayOnEnable = true;

        [Tooltip("Per-character playback multiplier. Multiplies the Parts clip Time Scale. " +
                 "1 = normal, 2 = twice as fast, 0.5 = half speed, 0 = paused, negative = reverse.")]
        public float PlaybackTimeScale = 1f;

        public static bool OwnsAnimation(GameObject gameObject)
        {
            var parts = gameObject.GetComponent<SpritePartsCharacterAuthoring>();
            return parts != null && parts.Profile != null && parts.Profile.Data != null &&
                   parts.Profile.Data.AnimKind == SpriteAnimKind.Parts;
        }

        [Tooltip("Optional tint applied to every part.")]
        public Color Tint = Color.white;

        /// <summary>
        /// Editor bump so live conversion rebakes after a profile save/reload
        /// even when the Profile reference itself did not change.
        /// </summary>
        [HideInInspector] public int EditorProfileSyncRevision;

        [Tooltip("Baked gameplay overrides. Weight 0 disables. LookAt uses Target in the chosen Space.")]
        public SpritePartsOverrideAuthoring[] Overrides = Array.Empty<SpritePartsOverrideAuthoring>();

        [Tooltip("Masked clip layers applied after the base clip and before gameplay overrides. Empty Slot Ids = all slots.")]
        public SpritePartsLayerAuthoring[] Layers = Array.Empty<SpritePartsLayerAuthoring>();

#if UNITY_EDITOR
        void Reset() => SpritePartsAuthoringBundle.Ensure(gameObject);

        void OnValidate()
        {
            // Never DestroyImmediate / AddComponent here — defer via delayCall.
            SpritePartsAuthoringBundle.EnsureDeferred(gameObject);
            if (Profile != null && Profile.Data != null
                && Profile.Data.AnimKind == SpriteAnimKind.Parts)
                Profile.Data.EnsurePartsRig();
        }

        // Scene footprint only — MeshRenderer preview fights Entities Graphics / BRG.
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.9f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(1f, 1f, 0.01f));
        }
#endif

        class Baker : Baker<SpritePartsCharacterAuthoring>
        {
            public override void Bake(SpritePartsCharacterAuthoring authoring)
            {
                if (GetComponent<SpriteStaticAuthoring>() != null)
                    return; // Static baker reports the explicit repair requirement.

                var profileAsset = authoring.Profile;
                var profile = profileAsset != null ? profileAsset.Data : null;
                if (profile == null)
                    return;
                // Frames runtime mode is a valid profile state, not a bake failure.
                // Live conversion rebakes constantly, so the inspector reports it
                // instead of the console.
                if (profile.AnimKind != SpriteAnimKind.Parts)
                {
                    DependsOn(profileAsset);
                    return;
                }
                if (!SpriteBatchSpawner.LayoutXy)
                {
                    Debug.LogError(
                        "[SpritePartsCharacterAuthoring] Parts require XY layout. XZ is rejected.",
                        authoring);
                    return;
                }

                DependsOn(profileAsset);
                _ = authoring.EditorProfileSyncRevision;
#if UNITY_EDITOR
                if (profile.ArtLibraries != null)
                {
                    for (int i = 0; i < profile.ArtLibraries.Count; i++)
                    {
                        var link = profile.ArtLibraries[i];
                        if (link == null || string.IsNullOrWhiteSpace(link.LibraryGuid))
                            continue;
                        var library = SpriteArtLibraryOps.LoadByGuid(link.LibraryGuid);
                        if (library != null)
                            DependsOn(library);
                    }
                }
#endif
                profile.EnsureSheets();
                profile.EnsurePartsRig();
                if (profile.PartsSlots == null || profile.PartsSlots.Count == 0)
                    return;

                if (!SpritePartsClipConversion.TryBuildBlob(profile, Allocator.Persistent,
                        out var partsBlob, out string blobError))
                {
                    Debug.LogError(
                        $"[SpritePartsCharacterAuthoring] '{authoring.name}' blob failed: {blobError}",
                        authoring);
                    return;
                }

                // Track leftovers so stripping Set/Player retriggers bake. Parts owns this entity.
                GetComponent<SpriteAnimPlayerAuthoring>();
                GetComponent<SpriteAnimSetAuthoring>();

                var root = GetEntity(TransformUsageFlags.Dynamic);
                // No drawn sprite / no frame player on gameplay root.
                AddComponent(root, new DisableRendering());
                AddComponent(root, new SpritePartsSetRef { Set = partsBlob });
                AddComponent(root, new SpritePartsDrawGroup { CharacterOrder = authoring.CharacterOrder });
                AddComponent(root, new SpritePartsFacing
                {
                    FlipX = authoring.FlipX ? (byte)1 : (byte)0,
                    FlipY = authoring.FlipY ? (byte)1 : (byte)0,
                });
                AddComponent(root, new SpritePartsEnabled());

                int startClip = ResolveStartClip(ref partsBlob.Value, profile, authoring.StartingClipName);
                var player = SpritePartsPoseWriter.DefaultPlayer(math.max(0, startClip), authoring.PlayOnEnable);
                player.SpeedMultiplier = float.IsNaN(authoring.PlaybackTimeScale) ||
                                         float.IsInfinity(authoring.PlaybackTimeScale)
                    ? 1f
                    : authoring.PlaybackTimeScale;
                AddComponent(root, player);
                var ovBuf = AddBuffer<SpritePartsPoseOverride>(root);
                AddBuffer<SpritePartBasePose>(root);
                AddBuffer<SpritePartFinalPose>(root);
                AddBuffer<SpritePartPoseSource>(root);
                var layerBuf = AddBuffer<SpritePartsAnimLayer>(root);
                AddBuffer<SpritePartSocketBinding>(root);
                AddBuffer<SpritePartSocketWorld>(root);
                AddBuffer<SpritePartHitboxBinding>(root);
                AddBuffer<SpritePartHitboxWorld>(root);
                AddBuffer<SpritePartsMixEntry>(root);
                AddComponent(root, new SpritePartsPoseDiagnostics { PreviousClipIndex = -1 });
                // Auto blink: the first sprite group with a blink state.
                foreach (var group in profile.PartsSpriteGroups ?? new List<SpritePartsSpriteGroupDef>())
                {
                    if (group == null || string.IsNullOrEmpty(group.BlinkState))
                        continue;
                    int g = SpriteParts.FindSpriteGroup(ref partsBlob.Value, group.Name);
                    int s = SpriteParts.FindSpriteGroupState(ref partsBlob.Value, g, group.BlinkState);
                    if (s >= 0)
                        AddComponent(root, SpriteParts.NewAutoBlink(g, s, group.BlinkEvery.x, group.BlinkEvery.y, group.BlinkClose,
                            (uint)authoring.name.GetHashCode() * 2654435761u + 1u));
                    break;
                }
                // Event sounds: the clips the blob's AudioIndex values point at.
                var eventAudio = SpritePartsClipConversion.EventAudio(profile.PartsClips);
                if (eventAudio.Length > 0)
                {
                    foreach (var sound in eventAudio)
                        DependsOn(sound);
                    AddComponentObject(root, new SpritePartsAudioBank { Clips = eventAudio });
                }
                BakeOverrides(authoring, ovBuf, ref partsBlob.Value);
                BakeLayers(authoring, layerBuf, ref partsBlob.Value);

                // Visual root for facing (does not count toward 32 parts).
                var visualRoot = CreateAdditionalEntity(TransformUsageFlags.Dynamic);
                AddComponent(visualRoot, LocalTransform.Identity);
                AddComponent(visualRoot, new Parent { Value = root });
                AddComponent(visualRoot, new PostTransformMatrix
                {
                    Value = SpritePartsPlayback.FacingMatrix(authoring.FlipX, authoring.FlipY),
                });
                AddComponent(visualRoot, new SpritePartsVisualRoot { Root = root });
                AddComponent(visualRoot, new SpritePartsOwner { Root = root });
                AddComponent(visualRoot, new DisableRendering());
                AddComponent(root, new SpritePartsVisualRootRef { VisualRoot = visualRoot });

                // Sheet entities for each profile sheet.
                var sheetEntities = BakeSheets(profile);
                var sheetBuf = AddBuffer<SpritePartSheetEntry>(root);
                for (int i = 0; i < sheetEntities.Count; i++)
                {
                    sheetBuf.Add(new SpritePartSheetEntry
                    {
                        Sheet = sheetEntities[i],
                        SheetTableIndex = i,
                    });
                }

                // Skin substitutions for initial appearance.
                int[] appearanceForSlot = ResolveInitialAppearances(ref partsBlob.Value, profile, authoring.StartingSkinId);

                var links = AddBuffer<SpritePartLink>(root);
                var partEntities = new Entity[partsBlob.Value.Slots.Length];

                // First pass: create part entities.
                for (int i = 0; i < partsBlob.Value.Slots.Length; i++)
                {
                    ref var slot = ref partsBlob.Value.Slots[i];
                    var part = CreateAdditionalEntity(TransformUsageFlags.Dynamic);
                    partEntities[i] = part;

                    SpritePartsSampler.SampleSlot(ref partsBlob.Value, startClip, i, 0f, out var pose);
                    AddComponent(part, new LocalTransform
                    {
                        Position = new float3(pose.Position.x, pose.Position.y, 0f),
                        Rotation = quaternion.RotateZ(math.radians(pose.Rotation)),
                        Scale = 1f,
                    });
                    AddComponent(part, new PostTransformMatrix
                    {
                        Value = SpritePartsPlayback.ScaleMatrix(pose.Scale.x, pose.Scale.y),
                    });
                    AddComponent(part, new SpritePartSlot
                    {
                        Root = root,
                        SlotIndex = i,
                        ParentSlotIndex = slot.ParentSlotIndex,
                        SlotIdHash = slot.SlotIdHash,
                        Hidden = slot.Hidden,
                    });
                    AddComponent(part, new SpritePartsOwner { Root = root });
                    AddComponent(part, new SpritePartRenderDepth { Value = 0f });
                    AddComponent(part, new SpriteAnimEnabled());
                    if (slot.Hidden != 0)
                        SetComponentEnabled<SpriteAnimEnabled>(part, false);
                    AddComponent(part, new SpriteTint
                    {
                        Value = new float4(authoring.Tint.r, authoring.Tint.g, authoring.Tint.b, authoring.Tint.a),
                    });
                    AddComponent(part, new SpritePartKeyedTint { Value = new float4(1f) });
                    AddComponent(part, new SpriteFlip
                    {
                        X = 0,
                        Y = 0,
                        Pivot = new float2(0.5f, 0.5f),
                    });

                    int appIndex = appearanceForSlot[i];
                    float2 frameOffset = float2.zero;
                    float2 frameScale = new float2(1f, 1f);
                    float2 logical = new float2(1f, 1f);
                    float2 pivot = new float2(0.5f, 0.5f);
                    int sheetTable = 0;
                    int cell = 0;
                    if (appIndex >= 0 && appIndex < partsBlob.Value.Appearances.Length)
                    {
                        ref var app = ref partsBlob.Value.Appearances[appIndex];
                        frameOffset = app.FrameOffset;
                        frameScale = app.FrameScale;
                        logical = app.LogicalWorldSize;
                        pivot = app.Pivot;
                        sheetTable = app.SheetTableIndex;
                        cell = app.CellIndex;
                    }
                    AddComponent(part, new SpritePartAppearanceState
                    {
                        AppearanceIndex = appIndex,
                        SheetTableIndex = sheetTable,
                        CellIndex = cell,
                        LogicalWorldSize = logical,
                        Pivot = pivot,
                        FrameOffset = frameOffset,
                        FrameScale = frameScale,
                    });
                    AddComponent(part, new SpriteAnimFrame
                    {
                        Slot = cell,
                        Offset = frameOffset,
                        Scale = frameScale,
                        Rotation = 0f,
                    });

                    Entity sheetEntity = Entity.Null;
                    if (sheetTable >= 0 && sheetTable < sheetEntities.Count)
                        sheetEntity = sheetEntities[sheetTable];
                    AddComponent(part, new SpriteSheetBinding { Sheet = sheetEntity });

                    links.Add(new SpritePartLink
                    {
                        Part = part,
                        SlotIdHash = slot.SlotIdHash,
                        SlotIndex = i,
                    });
                }

                // Second pass: Parent links (to part parent or visual root).
                for (int i = 0; i < partsBlob.Value.Slots.Length; i++)
                {
                    int parentSlot = partsBlob.Value.Slots[i].ParentSlotIndex;
                    Entity parentEntity = visualRoot;
                    if (parentSlot >= 0 && parentSlot < partEntities.Length)
                        parentEntity = partEntities[parentSlot];
                    AddComponent(partEntities[i], new Parent { Value = parentEntity });
                }
            }

            List<Entity> BakeSheets(SpriteSheetProfile profile)
            {
                var result = new List<Entity>();
                profile.EnsureSheets();
                for (int i = 0; i < profile.Sheets.Count; i++)
                {
                    var def = profile.Sheets[i];
                    if (def?.Texture == null)
                    {
                        result.Add(Entity.Null);
                        continue;
                    }
                    DependsOn(def.Texture);
                    int cols = Mathf.Max(1, def.Columns);
                    int rows = Mathf.Max(1, def.Rows);
                    float aspect = SpriteSheetProfile.GetCellAspect(def.Texture, cols, rows);
                    float4[] crops = null;
                    byte useCrops = 0;
                    if (def.CellLayoutMode == SpriteSheetCellLayoutMode.Cropped &&
                        SpriteSheetProfile.HasCroppedCellData(def))
                    {
                        var cropVecs = SpriteSheetProfile.BuildCellCropSTArray(def);
                        if (cropVecs != null && cropVecs.Length == cols * rows)
                        {
                            crops = new float4[cropVecs.Length];
                            for (int c = 0; c < cropVecs.Length; c++)
                                crops[c] = new float4(cropVecs[c].x, cropVecs[c].y, cropVecs[c].z, cropVecs[c].w);
                            useCrops = 1;
                        }
                    }

                    var sheetEntity = CreateAdditionalEntity(TransformUsageFlags.None);
                    AddComponent(sheetEntity, new SpriteSheetDefinition
                    {
                        Cols = cols,
                        Rows = rows,
                        CellAspect = aspect > 0.01f ? aspect : 1f,
                        UseCellCrops = useCrops,
                    });
                    AddComponentObject(sheetEntity, new SpriteSheetAsset { Texture = def.Texture });
                    if (useCrops != 0)
                    {
                        var cropBuffer = AddBuffer<SpriteAnimCellCrop>(sheetEntity);
                        foreach (var crop in crops)
                            cropBuffer.Add(new SpriteAnimCellCrop { Value = crop });
                    }
                    result.Add(sheetEntity);
                }
                return result;
            }

            static void BakeOverrides(
                SpritePartsCharacterAuthoring authoring,
                DynamicBuffer<SpritePartsPoseOverride> buf,
                ref SpritePartsSetBlob set)
            {
                if (authoring.Overrides == null)
                    return;
                for (int i = 0; i < authoring.Overrides.Length; i++)
                {
                    var src = authoring.Overrides[i];
                    if (src == null || !src.Enabled)
                        continue;
                    int slot = SpritePartsPlayback.FindSlotIndexById(ref set, src.SlotId);
                    if (slot < 0)
                        continue;
                    float2 scale = src.Scale.sqrMagnitude <= 1e-8f
                        ? new float2(1f, 1f)
                        : new float2(src.Scale.x, src.Scale.y);
                    buf.Add(new SpritePartsPoseOverride
                    {
                        Id = src.Id,
                        SlotIndex = slot,
                        Channels = (byte)(src.Channels == 0 ? SpritePartsPoseChannel.All : src.Channels),
                        Mode = (byte)src.Mode,
                        Space = (byte)src.Space,
                        Priority = src.Priority,
                        Weight = math.saturate(src.Weight),
                        Position = src.Position,
                        Rotation = src.Rotation,
                        Scale = scale,
                        Target = src.Target,
                    });
                }
            }

            static void BakeLayers(
                SpritePartsCharacterAuthoring authoring,
                DynamicBuffer<SpritePartsAnimLayer> buf,
                ref SpritePartsSetBlob set)
            {
                if (authoring.Layers == null)
                    return;
                for (int i = 0; i < authoring.Layers.Length; i++)
                {
                    var src = authoring.Layers[i];
                    if (src == null || !src.Enabled || string.IsNullOrWhiteSpace(src.ClipName))
                        continue;
                    int clip = SpritePartsPlayback.FindClipIndexByName(ref set, src.ClipName);
                    if (clip < 0)
                        clip = SpritePartsPlayback.FindClipIndexById(ref set, src.ClipName);
                    if (clip < 0)
                        continue;
                    uint mask = 0;
                    if (src.SlotIds != null)
                    {
                        for (int s = 0; s < src.SlotIds.Length; s++)
                        {
                            int slot = SpritePartsPlayback.FindSlotIndexById(ref set, src.SlotIds[s]);
                            if (slot >= 0 && slot < 32)
                                mask |= 1u << slot;
                        }
                    }
                    buf.Add(new SpritePartsAnimLayer
                    {
                        ClipIndex = clip,
                        Weight = math.saturate(src.Weight),
                        SlotMask = mask,
                    });
                }
            }

            static int ResolveStartClip(ref SpritePartsSetBlob set, SpriteSheetProfile profile, string startingName)
            {
                if (!string.IsNullOrWhiteSpace(startingName))
                {
                    int byName = SpritePartsPlayback.FindClipIndexByName(ref set, startingName);
                    if (byName >= 0) return byName;
                }
                if (!string.IsNullOrWhiteSpace(profile.PartsDefaultClipId))
                {
                    int byId = SpritePartsPlayback.FindClipIndexById(ref set, profile.PartsDefaultClipId);
                    if (byId >= 0) return byId;
                }
                return set.Clips.Length > 0 ? 0 : -1;
            }

            static int[] ResolveInitialAppearances(ref SpritePartsSetBlob set, SpriteSheetProfile profile, string skinId)
            {
                var result = new int[set.Slots.Length];
                for (int i = 0; i < set.Slots.Length; i++)
                    result[i] = set.Slots[i].DefaultAppearanceIndex;

                string id = string.IsNullOrWhiteSpace(skinId) ? profile.PartsDefaultSkinId : skinId;
                if (string.IsNullOrWhiteSpace(id))
                    return result;
                ulong hash = SpritePartIdUtility.Hash(SpritePartIdUtility.Canonical(id));
                for (int s = 0; s < set.SkinPatches.Length; s++)
                {
                    if (set.SkinPatches[s].SkinIdHash != hash)
                        continue;
                    ref var skin = ref set.SkinPatches[s];
                    for (int b = 0; b < skin.Bindings.Length; b++)
                    {
                        int slot = skin.Bindings[b].SlotIndex;
                        int app = skin.Bindings[b].AppearanceIndex;
                        if (slot >= 0 && slot < result.Length)
                            result[slot] = app;
                    }
                    break;
                }
                return result;
            }
        }
    }

    [Serializable]
    public class SpritePartsOverrideAuthoring
    {
        public bool Enabled = true;
        public int Id = 1;
        public string SlotId;
        public SpritePartsPoseMode Mode = SpritePartsPoseMode.Replace;
        public SpritePartsPoseSpace Space = SpritePartsPoseSpace.Local;
        public SpritePartsPoseChannel Channels = SpritePartsPoseChannel.All;
        public int Priority;
        [Range(0f, 1f)] public float Weight = 1f;
        public Vector2 Position;
        public float Rotation;
        public Vector2 Scale = Vector2.one;
        public Vector2 Target;
    }

    [Serializable]
    public class SpritePartsLayerAuthoring
    {
        public bool Enabled = true;
        public string ClipName;
        [Range(0f, 1f)] public float Weight = 1f;
        public string[] SlotIds = Array.Empty<string>();
    }

#if UNITY_EDITOR
    /// <summary>
    /// Related authoring when you add Sprite Parts Character.
    /// Adds Sort only — MeshRenderer quads are not used (Preview shaders lack
    /// DOTS_INSTANCING_ON and Entities Graphics / BRG reject them).
    /// </summary>
    public static class SpritePartsAuthoringBundle
    {
        static readonly string[] PreviewMeshNames =
        {
            "InvertLab Parts Preview Quad",
            "InvertLab Preview Quad",
        };
        static readonly string[] PreviewMaterialNames =
        {
            "InvertLab Parts Preview",
            "InvertLab Sprite Preview",
            "Default-Particle",
        };
        static bool _adding;

        public static void EnsureDeferred(GameObject gameObject)
        {
            if (gameObject == null || Application.isPlaying)
                return;
            var go = gameObject;
            EditorApplication.delayCall += () =>
            {
                if (go != null)
                    Ensure(go);
            };
        }

        public static void Ensure(GameObject gameObject)
        {
            if (_adding || gameObject == null || Application.isPlaying)
                return;

            _adding = true;
            try
            {
                // Strip leftover MeshFilter/MeshRenderer previews from earlier builds.
                StripLegacyPreviewMesh(gameObject);
                StripConflictingFrameAuthoring(gameObject);
                if (gameObject.GetComponent<SpriteSortAuthoring>() == null)
                    Undo.AddComponent<SpriteSortAuthoring>(gameObject);
            }
            finally
            {
                _adding = false;
            }
        }

        public static bool HasFrameAuthoringConflict(GameObject gameObject)
        {
            if (gameObject == null || gameObject.GetComponent<SpritePartsCharacterAuthoring>() == null)
                return false;
            return gameObject.GetComponent<SpriteAnimPlayerAuthoring>() != null
                || gameObject.GetComponent<SpriteAnimSetAuthoring>() != null;
        }

        public static int StripConflictingFrameAuthoring(GameObject gameObject)
        {
            if (gameObject == null)
                return 0;
            int removed = 0;
            var player = gameObject.GetComponent<SpriteAnimPlayerAuthoring>();
            if (player != null)
            {
                Undo.DestroyObjectImmediate(player);
                removed++;
            }
            var set = gameObject.GetComponent<SpriteAnimSetAuthoring>();
            if (set != null)
            {
                Undo.DestroyObjectImmediate(set);
                removed++;
            }
            return removed;
        }

        static bool IsLegacyPreview(GameObject gameObject, out MeshFilter filter, out MeshRenderer renderer)
        {
            filter = gameObject.GetComponent<MeshFilter>();
            renderer = gameObject.GetComponent<MeshRenderer>();
            if (filter != null && filter.sharedMesh != null)
            {
                string meshName = filter.sharedMesh.name;
                for (int i = 0; i < PreviewMeshNames.Length; i++)
                {
                    if (meshName == PreviewMeshNames[i])
                        return true;
                }
            }
            if (renderer != null && renderer.sharedMaterial != null)
            {
                string matName = renderer.sharedMaterial.name;
                for (int i = 0; i < PreviewMaterialNames.Length; i++)
                {
                    if (matName == PreviewMaterialNames[i] ||
                        matName.StartsWith(PreviewMaterialNames[i]))
                        return true;
                }
            }
            return false;
        }

        static void StripLegacyPreviewMesh(GameObject gameObject)
        {
            if (!IsLegacyPreview(gameObject, out var filter, out var renderer))
                return;

            // Hide immediately so BRG stops drawing; destroy on next editor tick
            // (DestroyObjectImmediate is illegal during OnValidate).
            if (renderer != null)
                renderer.enabled = false;

            var go = gameObject;
            EditorApplication.delayCall += () =>
            {
                if (go == null)
                    return;
                if (!IsLegacyPreview(go, out var f2, out var r2))
                    return;
                if (r2 != null)
                    Undo.DestroyObjectImmediate(r2);
                if (f2 != null)
                    Undo.DestroyObjectImmediate(f2);
            };
        }
    }
#endif

}

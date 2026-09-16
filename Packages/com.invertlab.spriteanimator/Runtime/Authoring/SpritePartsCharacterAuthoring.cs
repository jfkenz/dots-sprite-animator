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

        [Tooltip("Optional tint applied to every part.")]
        public Color Tint = Color.white;

#if UNITY_EDITOR
        void OnValidate()
        {
            // Never delete components here â€” conflict repair is Undoable via inspector/menu.
            if (Profile != null && Profile.Data != null)
                Profile.Data.EnsurePartsRig();
        }
#endif

        class Baker : Baker<SpritePartsCharacterAuthoring>
        {
            public override void Bake(SpritePartsCharacterAuthoring authoring)
            {
                var profileAsset = authoring.Profile;
                var profile = profileAsset != null ? profileAsset.Data : null;
                if (profile == null)
                    return;
                if (profile.AnimKind != SpriteAnimKind.Parts)
                {
                    Debug.LogError(
                        $"[SpritePartsCharacterAuthoring] '{authoring.name}': Profile.AnimKind is {profile.AnimKind}, expected Parts.",
                        authoring);
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

                if (!SpritePartsClipConversion.TryBuildBlob(profile, Allocator.Persistent,
                        out var partsBlob, out string blobError))
                {
                    Debug.LogError(
                        $"[SpritePartsCharacterAuthoring] '{authoring.name}' blob failed: {blobError}",
                        authoring);
                    return;
                }

                // Frame player on same GO is a repairable authoring error â€” do not bake frame path.
                var framePlayer = GetComponent<SpriteAnimPlayerAuthoring>();
                if (framePlayer != null)
                {
                    Debug.LogError(
                        $"[SpritePartsCharacterAuthoring] '{authoring.name}': Parts profile plus frame player is invalid. Use Fix Parts Authoring Conflict.",
                        authoring);
                }

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
                var player = new SpritePartsPlayer
                {
                    ClipIndex = math.max(0, startClip),
                    TimeSeconds = 0f,
                    SpeedMultiplier = 1f,
                    Playing = authoring.PlayOnEnable ? (byte)1 : (byte)0,
                    Completed = 0,
                };
                AddComponent(root, player);

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
                    });
                    AddComponent(part, new SpritePartsOwner { Root = root });
                    AddComponent(part, new SpritePartRenderDepth { Value = 0f });
                    AddComponent(part, new SpriteAnimEnabled());
                    AddComponent(part, new SpriteTint
                    {
                        Value = new float4(authoring.Tint.r, authoring.Tint.g, authoring.Tint.b, authoring.Tint.a),
                    });
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
}

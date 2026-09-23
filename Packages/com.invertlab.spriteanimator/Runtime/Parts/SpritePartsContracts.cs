using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Parts playback clock on the gameplay root. Seconds, not frames.</summary>
    public struct SpritePartsPlayer : IComponentData
    {
        public int ClipIndex;
        public float TimeSeconds;
        public float SpeedMultiplier;
        public byte Playing;
        public byte Completed;
        /// <summary>Explicit pause also freezes an unfinished fade after a Once clip completes.</summary>
        public byte Paused;
        /// <summary>-1 when not blending. Outgoing clip holds last Once pose if it ends mid-fade.</summary>
        public int PreviousClipIndex;
        public float PreviousTimeSeconds;
        public float BlendDuration;
        public float BlendElapsed;
        public byte SpriteSwitch;
        /// <summary>Ease of the crossfade into the current clip (SpriteEaseMode).</summary>
        public byte BlendEase;
        /// <summary>
        /// The previous clip's own fade-in over older clips (still running when this crossfade began; the older clips
        /// are in <see cref="SpritePartsMixEntry"/>). 0 = the previous clip is fully in.
        /// </summary>
        public float PreviousBlendDuration;
        public float PreviousBlendElapsed;
        public byte PreviousBlendEase;
        /// <summary>Seconds the current clip has played (not wrapped): queued clips start from it.</summary>
        public float PlayedSeconds;
    }

    public struct SpritePartsSetRef : IComponentData
    {
        public BlobAssetReference<SpritePartsSetBlob> Set;
    }

    public struct SpritePartsEnabled : IComponentData, IEnableableComponent { }

    public struct SpritePartsCompleted : IComponentData { }

    /// <summary>Root buffer: remapped part entities for each baked slot.</summary>
    [InternalBufferCapacity(8)]
    public struct SpritePartLink : IBufferElementData
    {
        public Entity Part;
        public ulong SlotIdHash;
        public int SlotIndex;
    }

    /// <summary>Child part identity. Permanently CPU-pose.</summary>
    public struct SpritePartSlot : IComponentData
    {
        public Entity Root;
        public int SlotIndex;
        public int ParentSlotIndex;
        public ulong SlotIdHash;
        /// <summary>1 = profile hide (eye off or hidden ancestor). Never drawn.</summary>
        public byte Hidden;
    }

    public struct SpritePartAppearanceState : IComponentData
    {
        public int AppearanceIndex;
        public int SheetTableIndex;
        public int CellIndex;
        public float2 LogicalWorldSize;
        public float2 Pivot;
        public float2 FrameOffset;
        public float2 FrameScale;
        /// <summary>1 when a clip appearance key is driving this part; 0 when skin/default owns it.</summary>
        public byte KeyedOverride;
    }

    /// <summary>World-local sheet entity for each baked sheet-table index.</summary>
    [InternalBufferCapacity(4)]
    public struct SpritePartSheetEntry : IBufferElementData
    {
        public Entity Sheet;
        public int SheetTableIndex;
    }

    /// <summary>
    /// Clip colour keys for this part (white when none). The renderer multiplies it into <see cref="SpriteTint"/>,
    /// so gameplay tints and tint tweens keep working on top.
    /// </summary>
    public struct SpritePartKeyedTint : IComponentData
    {
        public float4 Value;
    }

    /// <summary>Render-only depth consumed by CPU instance pack / bounds.</summary>
    public struct SpritePartRenderDepth : IComponentData
    {
        public float Value;
    }

    public struct SpritePartsDrawGroup : IComponentData
    {
        public int CharacterOrder;
    }

    /// <summary>Internal visual-root child: facing / visual scale via PostTransformMatrix.</summary>
    public struct SpritePartsVisualRoot : IComponentData
    {
        public Entity Root;
    }

    /// <summary>Gameplay-root facing. Applied to visual-root PostTransformMatrix, not physics scale.</summary>
    public struct SpritePartsFacing : IComponentData
    {
        public byte FlipX;
        public byte FlipY;
    }

    /// <summary>Link from part/visual-root back to gameplay root for lifecycle queries.</summary>
    public struct SpritePartsOwner : IComponentData
    {
        public Entity Root;
    }

    /// <summary>Gameplay root -> visual-root child entity.</summary>
    public struct SpritePartsVisualRootRef : IComponentData
    {
        public Entity VisualRoot;
    }

    /// <summary>
    /// Last successfully applied named skin patch (0 = defaults / ResetSkin).
    /// Patches are not complete outfits; unlisted slots stay as previously written.
    /// </summary>
    public struct SpritePartsActiveSkin : IComponentData
    {
        public ulong SkinIdHash;
    }
}

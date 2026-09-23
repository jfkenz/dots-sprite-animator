using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    [System.Flags]
    public enum SpritePartsPoseChannel : byte
    {
        None = 0,
        Position = 1 << 0,
        Rotation = 1 << 1,
        Scale = 1 << 2,
        All = Position | Rotation | Scale,
    }

    public enum SpritePartsPoseMode : byte
    {
        Replace = 0,
        Add = 1,
        LookAt = 2,
    }

    public enum SpritePartsPoseSpace : byte
    {
        Local = 0,
        Parent = 1,
        Character = 2,
        World = 3,
    }

    public enum SpritePartsSpriteSwitch : byte
    {
        UseIncomingImmediately = 0,
        SwitchAtMidpoint = 1,
        HoldUntilFadeEnd = 2,
        UseHighestWeightClip = 3,
    }

    public enum SpritePartPoseWriterKind : byte
    {
        Clip = 0,
        Layer = 1,
        Replace = 2,
        Add = 3,
        Aim = 4,
        Facing = 5,
    }

    /// <summary>
    /// Gameplay pose request. The pose writer is the only joint transform writer.
    /// Weight 0 disables the effect and restores the blended clip automatically.
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct SpritePartsPoseOverride : IBufferElementData
    {
        public int Id;
        public int SlotIndex;
        public byte Channels;
        public byte Mode;
        public byte Space;
        public int Priority;
        public float Weight;
        public float2 Position;
        public float Rotation;
        public float2 Scale;
        /// <summary>LookAt point in <see cref="Space"/>.</summary>
        public float2 Target;
    }

    [InternalBufferCapacity(8)]
    public struct SpritePartBasePose : IBufferElementData
    {
        public float2 Position;
        public float Rotation;
        public float2 Scale;
    }

    [InternalBufferCapacity(8)]
    public struct SpritePartFinalPose : IBufferElementData
    {
        public float2 Position;
        public float Rotation;
        public float2 Scale;
        public byte PhysicsSkipped;
    }

    [InternalBufferCapacity(8)]
    public struct SpritePartPoseSource : IBufferElementData
    {
        public byte Writer;
        public byte Space;
        public float Weight;
        public int OverrideId;
    }

    /// <summary>Masked clip layers (base clip -&gt; layers -&gt; gameplay overrides).</summary>
    [InternalBufferCapacity(0)]
    public struct SpritePartsAnimLayer : IBufferElementData
    {
        public int ClipIndex;
        public float Weight;
        public uint SlotMask;
        /// <summary>Weight fades toward this at <see cref="FadeSpeed"/> per second (SpriteParts.FadeLayer).</summary>
        public float TargetWeight;
        public float FadeSpeed;
        /// <summary>1 = the layer adds its change from the setup pose on top (breathing); 0 = it replaces.</summary>
        public byte Additive;
        /// <summary>1 = the layer runs its own clock (<see cref="Time"/>); 0 = it follows the main clip's time.</summary>
        public byte OwnClock;
        public float Time;
        /// <summary>1 = the layer is removed when a fade to 0 ends.</summary>
        public byte RemoveAtZero;
        /// <summary>Track number (Spine's tracks 1, 2...): a new clip on the same track crossfades the old one out. 0 = none.</summary>
        public int Track;
        /// <summary>Seconds to fade out when an own-clock clip that plays once reaches its end (0 = hold the last pose).</summary>
        public float EndFade;
    }

    [InternalBufferCapacity(4)]
    public struct SpritePartSocketBinding : IBufferElementData
    {
        public ulong SocketIdHash;
        public int SlotIndex;
        public float2 LocalOffset;
        public float LocalRotation;
    }

    [InternalBufferCapacity(4)]
    public struct SpritePartSocketWorld : IBufferElementData
    {
        public ulong SocketIdHash;
        public int SlotIndex;
        public float2 WorldPosition;
        public float WorldRotation;
    }

    [InternalBufferCapacity(4)]
    public struct SpritePartHitboxBinding : IBufferElementData
    {
        public int SlotIndex;
        public float2 LocalCenter;
        public float2 LocalSize;
        public float LocalRotation;
        public byte Kind;
    }

    [InternalBufferCapacity(4)]
    public struct SpritePartHitboxWorld : IBufferElementData
    {
        public int SlotIndex;
        public float2 Center;
        public float2 Extents;
        public byte Kind;
    }

    public struct SpritePartsPoseDiagnostics : IComponentData
    {
        public const byte MissingVisualRoot = 1 << 0;
        public const byte LinkMismatch = 1 << 1;
        public const byte GameplayWroteTransform = 1 << 2;
        public const byte MissingSet = 1 << 3;
        public const byte UnsupportedOverride = 1 << 4;

        public byte Flags;
        public int ClipIndex;
        public int PreviousClipIndex;
        public float Blend01;
        public byte LoggedFlags;
    }

    /// <summary>
    /// Physics/gameplay owns this part's LocalTransform and PostTransformMatrix.
    /// Attachment export follows its current ECS Parent hierarchy. This marker
    /// grants transform ownership; it does not create a collider or physics body.
    /// </summary>
    public struct SpritePartPhysicsOwned : IComponentData { }
}

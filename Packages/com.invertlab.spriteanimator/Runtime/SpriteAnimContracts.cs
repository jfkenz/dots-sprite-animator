using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Shared contracts between the animation brain, the instanced renderer,
    /// the culling system and tooling. Components ONLY live here so parallel
    /// workstreams never touch each other's files.
    ///
    /// Ownership map (do not cross):
    ///   SpriteAnimPlayerSystem/SpriteAnimSet ....... animation brain
    ///   Instanced/* ................................ GPU-instancing renderer
    ///   Culling/*, Spawn/*, Sorting/* .............. world utilities
    ///   Editor/*, SpriteSheetProfile ............... tooling
    /// </summary>

    /// <summary>Current atlas cell plus per-frame visual offset. Written every animation tick.</summary>
    public struct SpriteAnimFrame : IComponentData
    {
        public int Slot;
        public float2 Offset;
        public float2 Scale;
        public float Rotation;
    }

    /// <summary>Enable-flag for animation ticking; culling toggles it off when off-screen/far.</summary>
    public struct SpriteAnimEnabled : IComponentData, IEnableableComponent { }

    /// <summary>Added when a non-looping clip reaches its final frame; removed by Play().</summary>
    public struct SpriteAnimCompleted : IComponentData { }

    /// <summary>Animation event raised this tick. Read while SpriteAnimEventsPending is enabled.</summary>
    [InternalBufferCapacity(2)]
    public struct SpriteAnimEventBuffer : IBufferElementData
    {
        public byte Id;
        public int ClipIndex;
        public int FrameIndex;
    }

    /// <summary>Enableable tag: this entity emitted one or more events this tick.</summary>
    public struct SpriteAnimEventsPending : IComponentData, IEnableableComponent { }

    /// <summary>One authored collider for one frame (UV space, origin bottom-left).</summary>
    public struct FrameBox
    {
        public float2 Center;                         // uv center
        public float2 Extents;                        // half-size / circle radii
        /// <summary>
        /// Rotation in degrees around Center, y-up runtime UV (authoring Angle is negated on bake).
        /// SpriteHitboxActivationSystem copies this onto SpriteHitboxLive but still treats
        /// Center/Extents as an AABB for consumers that have not been updated to OBB tests.
        /// Do not drop this field — rotated boxes must survive bake/reload.
        /// </summary>
        public float Angle;
        public byte Id;                               // gameplay collider id
        public SpriteColliderShape Shape;
        public FixedList128Bytes<float2> Polygon;     // absolute cell UV points for polygons
    }

    /// <summary>Optional per-entity UV flip flags for non-EG render paths.</summary>
    public struct SpriteFlip : IComponentData
    {
        public byte X;
        public byte Y;
    }

    /// <summary>Runtime-facing sockets on the currently displayed frame.</summary>
    [InternalBufferCapacity(2)]
    public struct SpriteSocketBuffer : IBufferElementData
    {
        public FixedString64Bytes Name;
        public float2 LocalPosition;
        public float LocalAngle;
        public float2 LocalScale;
    }

    /// <summary>Moves this child entity to a named socket on its animated source.</summary>
    public struct SpriteSocketAttachment : IComponentData
    {
        public Entity Source;
        public FixedString64Bytes SocketName;
        public float2 PositionOffset;
        public float AngleOffset;
        public float BaseScale;
    }
}

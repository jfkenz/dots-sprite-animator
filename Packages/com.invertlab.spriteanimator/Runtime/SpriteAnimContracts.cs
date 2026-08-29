using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

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
        public FixedString64Bytes SocketId;
        public ulong SocketIdHash;
        public float2 LocalPosition;
        public float LocalAngle;
        public float2 LocalScale;
    }

    /// <summary>
    /// Continuous profile-level clock for independent socket motion. It is not
    /// reset when the character changes animation clips.
    /// </summary>
    public struct SpriteSocketMotionPlayer : IComponentData
    {
        public float Time;
        public byte Playing;
    }

    [InternalBufferCapacity(2)]
    public struct SpriteSocketEventBuffer : IBufferElementData
    {
        public FixedString64Bytes SocketId;
        public ulong SocketIdHash;
        public byte EventId;
        public float NormalizedTime;
        public int LoopSequence;
    }

    public struct SpriteSocketEventsPending : IComponentData, IEnableableComponent { }

    /// <summary>Moves this child entity to a named socket on its animated source.</summary>
    public struct SpriteSocketAttachment : IComponentData
    {
        public Entity Source;
        public FixedString64Bytes SocketName;
        public FixedString64Bytes SocketId;
        public ulong SocketIdHash;
        public float2 PositionOffset;
        public float AngleOffset;
        public float BaseScale;
    }

    public struct SpriteSocketWorldPose
    {
        public float3 Position;
        public quaternion Rotation;
        public float2 Scale;
    }

    /// <summary>Burst-friendly socket lookup for gameplay and custom systems.</summary>
    public static class SpriteSockets
    {
        public static ulong Hash(string socketId)
        {
            string canonical = SpriteSocketIdUtility.Canonical(socketId);
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < canonical.Length; i++)
            {
                hash ^= (byte)canonical[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }

        public static ulong Hash(in FixedString64Bytes socketId)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < socketId.Length; i++)
            {
                hash ^= socketId[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }

        public static bool TryGetPose(DynamicBuffer<SpriteSocketBuffer> sockets,
            ulong socketIdHash, out SpriteSocketBuffer pose)
        {
            for (int i = 0; i < sockets.Length; i++)
            {
                if (sockets[i].SocketIdHash == socketIdHash)
                {
                    pose = sockets[i];
                    return true;
                }
            }
            pose = default;
            return false;
        }

        public static bool TryGetPose(DynamicBuffer<SpriteSocketBuffer> sockets,
            in FixedString64Bytes socketId, out SpriteSocketBuffer pose)
        {
            ulong hash = Hash(socketId);
            for (int i = 0; i < sockets.Length; i++)
            {
                if (sockets[i].SocketIdHash == hash && sockets[i].SocketId.Equals(socketId))
                {
                    pose = sockets[i];
                    return true;
                }
            }
            pose = default;
            return false;
        }

        public static bool TryGetPose(EntityManager entityManager, Entity source,
            string socketId, out SpriteSocketBuffer pose)
        {
            if (!entityManager.HasBuffer<SpriteSocketBuffer>(source))
            {
                pose = default;
                return false;
            }
            var id = new FixedString64Bytes(SpriteSocketIdUtility.Canonical(socketId));
            return TryGetPose(entityManager.GetBuffer<SpriteSocketBuffer>(source), id, out pose);
        }

        public static bool TryGetWorldPose(DynamicBuffer<SpriteSocketBuffer> sockets,
            ulong socketIdHash, in LocalToWorld source, out SpriteSocketWorldPose worldPose)
        {
            if (!TryGetPose(sockets, socketIdHash, out var local))
            {
                worldPose = default;
                return false;
            }

            float3 x = math.normalizesafe(source.Value.c0.xyz, new float3(1f, 0f, 0f));
            float3 y = math.normalizesafe(source.Value.c1.xyz, new float3(0f, 1f, 0f));
            float3 position = source.Position + x * local.LocalPosition.x + y * local.LocalPosition.y;
            float sourceAngle = math.degrees(math.atan2(x.y, x.x));
            worldPose = new SpriteSocketWorldPose
            {
                Position = position,
                Rotation = quaternion.RotateZ(math.radians(sourceAngle + local.LocalAngle)),
                Scale = local.LocalScale,
            };
            return true;
        }
    }
}

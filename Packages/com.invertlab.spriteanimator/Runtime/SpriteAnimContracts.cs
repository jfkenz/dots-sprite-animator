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

    /// <summary>
    /// One authored payload. Scalars live in Ints.x / Floats.x. Vectors use .xy/.xyz/.xyzw.
    /// Half is still a float at runtime; cast with <c>new half(payload.Floats.x)</c>.
    /// </summary>
    public struct SpriteAnimEventPayload
    {
        public byte Kind;
        public int4 Ints;
        public float4 Floats;
        public ulong TextHash;
        public ulong NameHash;

        public int IntValue => Ints.x;
        public float FloatValue => Floats.x;
        public int2 Int2 => Ints.xy;
        public int3 Int3 => Ints.xyz;
        public float2 Float2 => Floats.xy;
        public float3 Float3 => Floats.xyz;
    }

    /// <summary>Animation event raised this tick. Read while SpriteAnimEventsPending is enabled.</summary>
    [InternalBufferCapacity(4)]
    public struct SpriteAnimEventBuffer : IBufferElementData
    {
        public byte Id;
        public int ClipIndex;
        public int FrameIndex;
        public byte FireMode;
        public int IntPayload;
        public float FloatPayload;
        public ulong TextHash;
        public FixedList512Bytes<SpriteAnimEventPayload> Payloads;

        public bool TryPayload(int index, out SpriteAnimEventPayload payload)
        {
            if (index < 0 || index >= Payloads.Length)
            {
                payload = default;
                return false;
            }
            payload = Payloads[index];
            return true;
        }

        public bool TryNamed(ulong nameHash, out SpriteAnimEventPayload payload)
        {
            if (nameHash == 0)
            {
                payload = default;
                return false;
            }
            for (int i = 0; i < Payloads.Length; i++)
            {
                if (Payloads[i].NameHash == nameHash)
                {
                    payload = Payloads[i];
                    return true;
                }
            }
            payload = default;
            return false;
        }
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

    /// <summary>
    /// Optional per-entity UV flip. Author from SpriteAnimPlayerAuthoring or SpriteAnims.SetFlip.
    /// <see cref="Pivot"/> is the normalized cell-UV flip axis (profile/sheet pivot).
    /// Default (0.5, 0.5) mirrors around cell center — legacy behavior. Move the authored
    /// pivot onto the character so FlipX/Y mirrors in place without a visual jump.
    /// </summary>
    public struct SpriteFlip : IComponentData
    {
        public byte X;
        public byte Y;
        /// <summary>Normalized cell UV flip axis. (0.5,0.5) = cell center.</summary>
        public float2 Pivot;

        /// <summary>Cell-center pivot when <see cref="Pivot"/> was never authored (0,0).</summary>
        public float2 ResolvedPivot =>
            math.all(Pivot == float2.zero) ? new float2(0.5f, 0.5f) : Pivot;

        public static SpriteFlip Identity => new SpriteFlip { Pivot = new float2(0.5f, 0.5f) };
    }

    /// <summary>Mirrors sprite-local gameplay geometry with the rendered instance.</summary>
    public static class SpriteFlipUtility
    {
        public static float2 LocalPosition(float2 value, in SpriteFlip flip)
        {
            // Socket / offset positions are pivot-relative, so negate around origin.
            if (flip.X != 0) value.x = -value.x;
            if (flip.Y != 0) value.y = -value.y;
            return value;
        }

        public static float Angle(float value, in SpriteFlip flip)
        {
            if ((flip.X != 0) != (flip.Y != 0))
                value = -value;
            return value;
        }

        public static float2 Scale(float2 value, in SpriteFlip flip)
        {
            if (flip.X != 0) value.x = -value.x;
            if (flip.Y != 0) value.y = -value.y;
            return value;
        }

        public static FrameBox Box(FrameBox value, in SpriteFlip flip)
        {
            float2 pivot = flip.ResolvedPivot;
            if (flip.X != 0) value.Center.x = 2f * pivot.x - value.Center.x;
            if (flip.Y != 0) value.Center.y = 2f * pivot.y - value.Center.y;
            value.Angle = Angle(value.Angle, flip);
            for (int i = 0; i < value.Polygon.Length; i++)
            {
                float2 point = value.Polygon[i];
                if (flip.X != 0) point.x = 2f * pivot.x - point.x;
                if (flip.Y != 0) point.y = 2f * pivot.y - point.y;
                value.Polygon[i] = point;
            }
            return value;
        }

        public static SpriteSocketBuffer Socket(SpriteSocketBuffer value, in SpriteFlip flip)
        {
            value.LocalPosition = LocalPosition(value.LocalPosition, flip);
            value.LocalAngle = Angle(value.LocalAngle, flip);
            value.LocalScale = Scale(value.LocalScale, flip);
            return value;
        }
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
        public float Speed;
        public byte Playing;
        public float3 OriginPosition;
        public float OriginAngle;
        public byte OriginInitialized;
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

    /// <summary>Present when this character has one or more socket inventories.</summary>
    public struct SpriteSocketInventoryTag : IComponentData { }

    /// <summary>
    /// Frame sockets stay glued to the clip. Independent sockets run their own motion clock.
    /// </summary>
    public enum SpriteSocketInventoryKind : byte
    {
        Frame = 0,
        Independent = 1,
    }

    /// <summary>One socket inside a named inventory. Query by GroupHash then Kind.</summary>
    [InternalBufferCapacity(8)]
    public struct SpriteSocketInventoryMember : IBufferElementData
    {
        public uint GroupHash;
        public FixedString32Bytes GroupName;
        public ulong SocketIdHash;
        public FixedString64Bytes SocketId;
        public FixedString64Bytes SocketName;
        public byte Kind;
    }

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

    public static class SpriteSocketTriggerUtility
    {
        public static int CountCrossings(float previousClock, float currentClock,
            float duration, float normalizedTime, bool loop, out int firstSequence)
        {
            firstSequence = 0;
            if (currentClock <= previousClock)
                return 0;
            float from = previousClock / math.max(0.01f, duration);
            float to = currentClock / math.max(0.01f, duration);
            float marker = math.saturate(normalizedTime);
            if (!loop)
                return marker > math.saturate(from) + 1e-6f &&
                       marker <= math.saturate(to) + 1e-6f ? 1 : 0;
            int first = (int)math.floor(from - marker + 1e-6f) + 1;
            int last = (int)math.floor(to - marker + 1e-6f);
            firstSequence = first;
            return math.max(0, last - first + 1);
        }
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

        public static uint InventoryHash(string name)
            => (uint)Hash(SpriteSocketIdUtility.Canonical(name, "inventory"));

        public static uint InventoryHash(in FixedString32Bytes name)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < name.Length; i++)
            {
                hash ^= name[i];
                hash *= 1099511628211UL;
            }
            return (uint)hash;
        }

        /// <summary>
        /// Copy live poses in a named inventory that match <paramref name="kind"/>.
        /// Use Frame for clip-glued slots, Independent for own-clock tracks.
        /// </summary>
        public static int CollectInventory(
            DynamicBuffer<SpriteSocketInventoryMember> members,
            DynamicBuffer<SpriteSocketBuffer> sockets,
            uint inventoryHash,
            NativeList<SpriteSocketBuffer> dest,
            SpriteSocketInventoryKind kind)
        {
            byte kindByte = (byte)kind;
            int wrote = 0;
            for (int i = 0; i < members.Length; i++)
            {
                var member = members[i];
                if (member.GroupHash != inventoryHash || member.Kind != kindByte)
                    continue;
                if (!TryGetPose(sockets, member.SocketIdHash, out var pose))
                    continue;
                dest.Add(pose);
                wrote++;
            }
            return wrote;
        }

        public static int CollectInventory(
            DynamicBuffer<SpriteSocketInventoryMember> members,
            DynamicBuffer<SpriteSocketBuffer> sockets,
            string inventoryName,
            NativeList<SpriteSocketBuffer> dest,
            SpriteSocketInventoryKind kind)
            => CollectInventory(members, sockets, InventoryHash(inventoryName), dest, kind);

        public static int CollectFrameInventory(
            DynamicBuffer<SpriteSocketInventoryMember> members,
            DynamicBuffer<SpriteSocketBuffer> sockets,
            uint inventoryHash,
            NativeList<SpriteSocketBuffer> dest)
            => CollectInventory(members, sockets, inventoryHash, dest, SpriteSocketInventoryKind.Frame);

        public static int CollectFrameInventory(
            DynamicBuffer<SpriteSocketInventoryMember> members,
            DynamicBuffer<SpriteSocketBuffer> sockets,
            string inventoryName,
            NativeList<SpriteSocketBuffer> dest)
            => CollectInventory(members, sockets, inventoryName, dest, SpriteSocketInventoryKind.Frame);

        public static int CollectIndependentInventory(
            DynamicBuffer<SpriteSocketInventoryMember> members,
            DynamicBuffer<SpriteSocketBuffer> sockets,
            uint inventoryHash,
            NativeList<SpriteSocketBuffer> dest)
            => CollectInventory(members, sockets, inventoryHash, dest, SpriteSocketInventoryKind.Independent);

        public static int CollectIndependentInventory(
            DynamicBuffer<SpriteSocketInventoryMember> members,
            DynamicBuffer<SpriteSocketBuffer> sockets,
            string inventoryName,
            NativeList<SpriteSocketBuffer> dest)
            => CollectInventory(members, sockets, inventoryName, dest, SpriteSocketInventoryKind.Independent);

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

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Samples profile-level socket tracks on their own clock. These poses are
    /// anchored to the player's local origin (the authored sheet pivot) and are
    /// written after clip-bound sockets, so an independent track wins by name.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpriteAnimPlayerSystem))]
    [BurstCompile]
    public partial struct SpriteSocketMotionSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = math.max(0f, SystemAPI.Time.DeltaTime);
            var buffers = SystemAPI.GetBufferLookup<SpriteSocketBuffer>();
            var eventBuffers = SystemAPI.GetBufferLookup<SpriteSocketEventBuffer>();
            var eventPending = SystemAPI.GetComponentLookup<SpriteSocketEventsPending>();

            foreach (var (player, setRef, entity) in
                     SystemAPI.Query<RefRW<SpriteSocketMotionPlayer>, RefRO<SpriteAnimSetRef>>()
                         .WithEntityAccess())
            {
                if (!buffers.HasBuffer(entity))
                    continue;
                float previousTime = player.ValueRO.Time;
                if (player.ValueRO.Playing != 0)
                    player.ValueRW.Time += dt * math.select(
                        1f, player.ValueRO.Speed, player.ValueRO.Speed > 0.0001f);
                float currentTime = player.ValueRO.Time;

                ref var set = ref setRef.ValueRO.Set.Value;
                var buffer = buffers[entity];
                for (int i = 0; i < set.SocketMotions.Length; i++)
                {
                    ref var motion = ref set.SocketMotions[i];
                    if (motion.Keys.Length == 0)
                        continue;
                    if (player.ValueRO.Playing != 0)
                        EmitTriggers(entity, ref motion, previousTime, currentTime,
                            ref eventBuffers, ref eventPending);

                    float duration = math.max(0.01f, motion.Duration);
                    float normalized = player.ValueRO.Time / duration;
                    normalized = motion.Loop != 0
                        ? normalized - math.floor(normalized)
                        : math.saturate(normalized);
                    Sample(ref motion, normalized, out float2 position, out float angle,
                        out float2 scale);

                    int existing = -1;
                    for (int b = 0; b < buffer.Length; b++)
                    {
                        if (buffer[b].SocketIdHash == motion.SocketIdHash &&
                            buffer[b].SocketId.Equals(motion.SocketId))
                        {
                            existing = b;
                            break;
                        }
                    }

                    var pose = new SpriteSocketBuffer
                    {
                        Name = motion.Name,
                        SocketId = motion.SocketId,
                        SocketIdHash = motion.SocketIdHash,
                        LocalPosition = position,
                        LocalAngle = angle,
                        LocalScale = scale,
                    };
                    if (existing >= 0)
                        buffer[existing] = pose;
                    else
                        buffer.Add(pose);
                }
            }
        }

        static void EmitTriggers(Entity entity, ref SpriteSocketMotionBlob motion,
            float previousClock, float currentClock,
            ref BufferLookup<SpriteSocketEventBuffer> eventBuffers,
            ref ComponentLookup<SpriteSocketEventsPending> pending)
        {
            if (motion.Triggers.Length == 0 || !eventBuffers.HasBuffer(entity) ||
                !pending.HasComponent(entity) || currentClock <= previousClock)
                return;

            var output = eventBuffers[entity];
            for (int i = 0; i < motion.Triggers.Length; i++)
            {
                int count = SpriteSocketTriggerUtility.CountCrossings(
                    previousClock, currentClock, motion.Duration,
                    motion.Triggers[i].NormalizedTime, motion.Loop != 0,
                    out int firstSequence);
                count = math.min(count, 4096);
                for (int crossing = 0; crossing < count; crossing++)
                    AddTrigger(ref output, ref motion, i, firstSequence + crossing);
            }
            if (output.Length > 0)
                pending.SetComponentEnabled(entity, true);
        }

        static void AddTrigger(ref DynamicBuffer<SpriteSocketEventBuffer> output,
            ref SpriteSocketMotionBlob motion, int triggerIndex, int sequence)
        {
            var trigger = motion.Triggers[triggerIndex];
            if (trigger.EventId == 0)
                return;
            output.Add(new SpriteSocketEventBuffer
            {
                SocketId = motion.SocketId,
                SocketIdHash = motion.SocketIdHash,
                EventId = trigger.EventId,
                NormalizedTime = trigger.NormalizedTime,
                LoopSequence = sequence,
            });
        }

        static void Sample(ref SpriteSocketMotionBlob motion, float t,
            out float2 position, out float angle, out float2 scale)
        {
            int count = motion.Keys.Length;
            if (count == 1)
            {
                var only = motion.Keys[0];
                position = only.LocalPosition;
                angle = only.LocalAngle;
                scale = only.LocalScale;
                return;
            }

            int from = 0;
            int to = 1;
            float localT;
            if (t < motion.Keys[0].NormalizedTime)
            {
                if (motion.Loop == 0)
                {
                    from = to = 0;
                    localT = 0f;
                }
                else
                {
                    from = count - 1;
                    to = 0;
                    float start = motion.Keys[from].NormalizedTime;
                    float span = 1f - start + motion.Keys[0].NormalizedTime;
                    localT = span > 1e-6f ? (t + 1f - start) / span : 0f;
                }
            }
            else if (t >= motion.Keys[count - 1].NormalizedTime)
            {
                if (motion.Loop == 0 || t >= 1f)
                {
                    from = to = count - 1;
                    localT = 0f;
                }
                else
                {
                    from = count - 1;
                    to = 0;
                    float start = motion.Keys[from].NormalizedTime;
                    float span = 1f - start + motion.Keys[0].NormalizedTime;
                    localT = span > 1e-6f ? (t - start) / span : 0f;
                }
            }
            else
            {
                for (int i = 0; i < count - 1; i++)
                {
                    if (t < motion.Keys[i + 1].NormalizedTime)
                    {
                        from = i;
                        to = i + 1;
                        break;
                    }
                }
                float start = motion.Keys[from].NormalizedTime;
                float end = motion.Keys[to].NormalizedTime;
                localT = end > start ? math.saturate((t - start) / (end - start)) : 0f;
            }

            var a = motion.Keys[from];
            var b = motion.Keys[to];
            localT = a.UseCustomEase != 0
                ? SpriteEase.EvaluateSamples(
                    a.CustomEaseSamplesA, a.CustomEaseSamplesB, localT,
                    a.AllowOvershoot != 0)
                : SpriteEase.Evaluate(
                    SpriteEase.IsValidMode(a.EaseMode)
                        ? (SpriteEaseMode)a.EaseMode
                        : SpriteEaseMode.SmoothStep,
                    localT, a.AllowOvershoot != 0);
            float2 derivative = b.LocalPosition - a.LocalPosition;
            if (from == to)
            {
                position = a.LocalPosition;
            }
            else
            {
                int before = MotionKeyIndex(from - 1, count, motion.Loop != 0);
                int after = MotionKeyIndex(to + 1, count, motion.Loop != 0);
                position = SpriteSocketMotionInterpolation.Position(
                    a.PathMode,
                    motion.Keys[before].LocalPosition,
                    a.LocalPosition,
                    b.LocalPosition,
                    motion.Keys[after].LocalPosition,
                    a.OutTangent,
                    b.InTangent,
                    a.ArcBulge,
                    a.ArcClockwise,
                    localT);
                derivative = SpriteSocketMotionInterpolation.Derivative(
                    a.PathMode,
                    motion.Keys[before].LocalPosition,
                    a.LocalPosition,
                    b.LocalPosition,
                    motion.Keys[after].LocalPosition,
                    a.OutTangent,
                    b.InTangent,
                    a.ArcBulge,
                    a.ArcClockwise,
                    localT);
            }
            angle = SpriteSocketMotionInterpolation.Rotation(
                a.RotationMode, a.LocalAngle, b.LocalAngle,
                a.RotationTurns, a.FacingAngleOffset, derivative, localT);
            scale = math.lerp(a.LocalScale, b.LocalScale, localT);
        }

        static int MotionKeyIndex(int index, int count, bool loop)
        {
            if (!loop)
                return math.clamp(index, 0, count - 1);
            int wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }

    }

    /// <summary>Applies current-frame socket poses to baked child attachments.</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpriteSocketMotionSystem))]
    [BurstCompile]
    public partial struct SpriteSocketAttachmentSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var socketBuffers = SystemAPI.GetBufferLookup<SpriteSocketBuffer>(true);
            var sourceTransforms = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var postTransforms = SystemAPI.GetComponentLookup<PostTransformMatrix>();

            foreach (var (attachment, transform, entity) in
                     SystemAPI.Query<RefRO<SpriteSocketAttachment>, RefRW<LocalTransform>>()
                         .WithEntityAccess())
            {
                Entity source = attachment.ValueRO.Source;
                if (!socketBuffers.HasBuffer(source) || !sourceTransforms.HasComponent(source))
                    continue;

                var sockets = socketBuffers[source];
                float sourceScale = sourceTransforms[source].Scale;
                if (math.abs(sourceScale) < 0.0001f)
                    sourceScale = 1f;
                for (int i = 0; i < sockets.Length; i++)
                {
                    var socket = sockets[i];
                    bool hashMatch = socket.SocketIdHash == attachment.ValueRO.SocketIdHash;
                    bool idMatch = hashMatch && socket.SocketId.Equals(attachment.ValueRO.SocketId);
                    bool legacyNameMatch = attachment.ValueRO.SocketIdHash == 0 &&
                                           socket.Name.Equals(attachment.ValueRO.SocketName);
                    if (!idMatch && !legacyNameMatch)
                        continue;

                    float angle = socket.LocalAngle + attachment.ValueRO.AngleOffset;
                    quaternion rotation = quaternion.RotateZ(math.radians(angle));
                    float2 offset = math.mul(
                        rotation,
                        new float3(attachment.ValueRO.PositionOffset, 0f)).xy;
                    float3 position = transform.ValueRO.Position;
                    position.xy = (socket.LocalPosition + offset) / sourceScale;

                    // The source Quad is scaled to its rendered world size. Cancel that local
                    // scale here because socket positions were already converted from pixels
                    // to world units during baking. A missing key performs no write, retaining
                    // the last valid socket pose automatically.
                    transform.ValueRW.Position = position;
                    transform.ValueRW.Rotation = rotation;
                    transform.ValueRW.Scale = attachment.ValueRO.BaseScale / sourceScale;
                    if (postTransforms.HasComponent(entity))
                    {
                        float2 socketScale = math.all(socket.LocalScale == float2.zero)
                            ? new float2(1f, 1f)
                            : socket.LocalScale;
                        postTransforms[entity] = new PostTransformMatrix
                        {
                            Value = float4x4.Scale(new float3(socketScale, 1f)),
                        };
                    }
                    break;
                }
            }
        }
    }
}

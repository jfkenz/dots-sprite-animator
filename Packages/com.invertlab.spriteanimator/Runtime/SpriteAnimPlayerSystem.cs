using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Advances animation phase, applies per-frame dwell times, emits every
    /// crossed frame event, and writes the render slot. Playback performs no
    /// structural changes; event components are installed ahead of it.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpriteAnimEventClearSystem))]
    [BurstCompile]
    public partial struct SpriteAnimPlayerSystem : ISystem
    {
        const int MaxTransitionsPerTick = 4096;

        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var eventBuffers = SystemAPI.GetBufferLookup<SpriteAnimEventBuffer>();
            var pending = SystemAPI.GetComponentLookup<SpriteAnimEventsPending>();
            var socketBuffers = SystemAPI.GetBufferLookup<SpriteSocketBuffer>();
            var flips = SystemAPI.GetComponentLookup<SpriteFlip>(true);
            float dt = SystemAPI.Time.DeltaTime;

            // Entities without the optional culling flag always tick.
            // SpriteGpuDriven entities are excluded: the shader picks their
            // frame from the global clock; the CPU clock stays parked and
            // resumes untouched when converted back via SpriteGpuAnimSwitch.
            foreach (var (player, setRef, frame, entity) in
                     SystemAPI.Query<RefRW<SpriteAnimPlayer>, RefRO<SpriteAnimSetRef>,
                                     RefRW<SpriteAnimFrame>>()
                              .WithNone<SpriteAnimEnabled, SpriteAnimCompleted>()
                              .WithNone<SpriteGpuDriven>()
                              .WithEntityAccess())
            {
                if (player.ValueRO.Playing != 0)
                    Advance(ref ecb, ref eventBuffers, ref pending, ref socketBuffers, ref flips,
                        player, setRef, frame, entity, dt);
            }

            // Enableable SpriteAnimEnabled provides chunk-level culling.
            foreach (var (player, setRef, frame, entity) in
                     SystemAPI.Query<RefRW<SpriteAnimPlayer>, RefRO<SpriteAnimSetRef>,
                                     RefRW<SpriteAnimFrame>>()
                              .WithAll<SpriteAnimEnabled>()
                              .WithNone<SpriteAnimCompleted>()
                              .WithNone<SpriteGpuDriven>()
                              .WithEntityAccess())
            {
                if (player.ValueRO.Playing != 0)
                    Advance(ref ecb, ref eventBuffers, ref pending, ref socketBuffers, ref flips,
                        player, setRef, frame, entity, dt);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        static void Advance(ref EntityCommandBuffer ecb,
                            ref BufferLookup<SpriteAnimEventBuffer> eventBuffers,
                            ref ComponentLookup<SpriteAnimEventsPending> pending,
                            ref BufferLookup<SpriteSocketBuffer> socketBuffers,
                            ref ComponentLookup<SpriteFlip> flips,
                            RefRW<SpriteAnimPlayer> player,
                            RefRO<SpriteAnimSetRef> setRef,
                            RefRW<SpriteAnimFrame> frame,
                            Entity entity, float dt)
        {
            ref var set = ref setRef.ValueRO.Set.Value;
            if (set.Clips.Length == 0)
            {
                player.ValueRW.Playing = 0;
                return;
            }

            int clipIndex = math.clamp(player.ValueRO.ClipIndex, 0, set.Clips.Length - 1);
            ref var def = ref set.Clips[clipIndex];
            if (def.FrameCount <= 0 || def.FrameRate <= 0f)
            {
                player.ValueRW.Playing = 0;
                return;
            }

            int frameCount = def.FrameCount;
            float phase = math.max(0f, player.ValueRO.Time);
            int phaseStep = (int)math.floor(phase);
            float fraction = math.saturate(phase - phaseStep);
            int lastEventStep = player.ValueRO.LastEventStep;
            ulong firedMask = player.ValueRO.EventFiredMask;
            int onceClip = player.ValueRO.OnceEventClip;
            var onceFired = player.ValueRO.OnceFiredKeys;
            if (onceClip != clipIndex)
            {
                onceFired.Clear();
                onceClip = clipIndex;
            }
            float remaining = math.max(0f, dt) * def.FrameRate * math.max(0.01f, player.ValueRO.Speed);
            bool finished = false;
            int transitions = 0;

            while (remaining > 1e-6f && transitions < MaxTransitionsPerTick)
            {
                int displayed = DisplayFrame(phaseStep, frameCount, def.WrapMode);
                float dwell = def.DurationScales.Length > displayed
                    ? math.max(0.01f, def.DurationScales[displayed])
                    : 1f;
                float toBoundary = (1f - fraction) * dwell;
                float consumed = math.min(remaining, toBoundary);
                float nextFraction = math.min(1f, fraction + consumed / dwell);
                EmitIfCrossed(entity, clipIndex, phaseStep, displayed, fraction, nextFraction,
                    ref def, ref eventBuffers, ref pending, ref lastEventStep, ref firedMask,
                    ref onceFired);

                if (remaining + 1e-6f < toBoundary)
                {
                    fraction = nextFraction;
                    remaining = 0f;
                    break;
                }

                remaining -= toBoundary;
                fraction = 0f;
                transitions++;

                if (def.WrapMode == SpriteAnimWrap.Once && phaseStep + 1 >= frameCount)
                {
                    phaseStep = frameCount - 1;
                    finished = true;
                    break;
                }

                phaseStep++;
                int entered = DisplayFrame(phaseStep, frameCount, def.WrapMode);
                EmitIfCrossed(entity, clipIndex, phaseStep, entered, 0f, 0f,
                    ref def, ref eventBuffers, ref pending, ref lastEventStep, ref firedMask,
                    ref onceFired);
            }

            phase = phaseStep + fraction;
            int cycle = CycleLength(frameCount, def.WrapMode);
            if (def.WrapMode != SpriteAnimWrap.Once && cycle > 0 && phase > cycle * 1024f)
            {
                phase %= cycle;
                lastEventStep = int.MinValue;
            }

            int drawFrame = DisplayFrame((int)math.floor(phase), frameCount, def.WrapMode);
            player.ValueRW.Time = phase;
            player.ValueRW.LastEventStep = lastEventStep;
            player.ValueRW.EventFiredMask = firedMask;
            player.ValueRW.OnceEventClip = onceClip;
            player.ValueRW.OnceFiredKeys = onceFired;
            float4 frameData = set.Frames[def.FirstFrame + drawFrame];
            frame.ValueRW.Slot = (int)frameData.x;
            frame.ValueRW.Offset = frameData.yz;
            int phaseStepForFrame = (int)math.floor(phase);
            float tweenFraction = math.saturate(phase - phaseStepForFrame);
            int nextPhaseStep = phaseStepForFrame + 1;
            if (def.WrapMode == SpriteAnimWrap.Once && nextPhaseStep >= frameCount)
                nextPhaseStep = phaseStepForFrame;
            int nextFrame = DisplayFrame(nextPhaseStep, frameCount, def.WrapMode);
            float2 startScale = ReadFrameScale(ref def, drawFrame);
            float2 nextScale = ReadFrameScale(ref def, nextFrame);
            float startRotation = ReadFrameRotation(ref def, drawFrame);
            float nextRotation = ReadFrameRotation(ref def, nextFrame);
            var tweenMode = ReadTweenMode(ref def, drawFrame);
            float eased = SpriteEase.Evaluate(tweenMode, tweenFraction);
            frame.ValueRW.Scale = math.lerp(startScale, nextScale, eased);
            frame.ValueRW.Rotation = math.lerp(startRotation, nextRotation, eased);
            UpdateSockets(entity, drawFrame, ref def, ref socketBuffers, ref flips);

            if (finished)
            {
                player.ValueRW.Playing = 0;
                ecb.AddComponent<SpriteAnimCompleted>(entity);
            }
        }

        static void EmitIfCrossed(Entity entity, int clipIndex, int phaseStep, int frameIndex,
                                  float fromFraction, float toFraction,
                                  ref SpriteAnimDef def,
                                  ref BufferLookup<SpriteAnimEventBuffer> eventBuffers,
                                  ref ComponentLookup<SpriteAnimEventsPending> pending,
                                  ref int lastEventStep, ref ulong firedMask,
                                  ref FixedList128Bytes<ushort> onceFired)
        {
            if (frameIndex < 0)
                return;
            if (lastEventStep != phaseStep)
            {
                firedMask = 0;
                lastEventStep = phaseStep;
            }

            bool atPoint = math.abs(toFraction - fromFraction) <= 1e-6f;
            int keyCount = def.EventKeys.Length;
            if (keyCount == 0)
            {
                EmitLegacy(entity, clipIndex, frameIndex, fromFraction, toFraction,
                    atPoint, ref def, ref eventBuffers, ref pending, ref firedMask);
                return;
            }

            for (int k = 0; k < keyCount; k++)
            {
                var key = def.EventKeys[k];
                if (key.EventId == 0 || key.FrameIndex != frameIndex)
                    continue;
                float marker = math.saturate(key.NormalizedTime);
                bool hit = atPoint
                    ? math.abs(marker - fromFraction) <= 1e-6f
                    : fromFraction < marker - 1e-6f && toFraction + 1e-6f >= marker;
                if (!hit)
                    continue;
                ulong bit = k < 64 ? 1UL << k : 0UL;
                if (bit != 0 && (firedMask & bit) != 0)
                    continue;
                if (key.FireMode == (byte)SpriteEventFireMode.Once && ContainsKey(onceFired, (ushort)k))
                    continue;
                if (!Emit(entity, clipIndex, key, ref eventBuffers, ref pending))
                    continue;
                if (bit != 0)
                    firedMask |= bit;
                if (key.FireMode == (byte)SpriteEventFireMode.Once &&
                    onceFired.Length < onceFired.Capacity)
                    onceFired.Add((ushort)k);
            }
        }

        static void EmitLegacy(Entity entity, int clipIndex, int frameIndex,
                               float fromFraction, float toFraction, bool atPoint,
                               ref SpriteAnimDef def,
                               ref BufferLookup<SpriteAnimEventBuffer> eventBuffers,
                               ref ComponentLookup<SpriteAnimEventsPending> pending,
                               ref ulong firedMask)
        {
            if (frameIndex >= def.EventIds.Length || def.EventIds[frameIndex] == 0)
                return;
            float marker = frameIndex < def.EventNormalizedTimes.Length
                ? math.saturate(def.EventNormalizedTimes[frameIndex])
                : 0f;
            bool hit = atPoint
                ? math.abs(marker - fromFraction) <= 1e-6f
                : fromFraction < marker - 1e-6f && toFraction + 1e-6f >= marker;
            if (!hit || (firedMask & 1UL) != 0)
                return;
            if (!Emit(entity, clipIndex, new SpriteAnimEventKey
                {
                    FrameIndex = frameIndex,
                    NormalizedTime = marker,
                    EventId = def.EventIds[frameIndex],
                }, ref eventBuffers, ref pending))
                return;
            firedMask |= 1UL;
        }

        static bool ContainsKey(in FixedList128Bytes<ushort> list, ushort key)
        {
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i] == key)
                    return true;
            }
            return false;
        }

        static bool Emit(Entity entity, int clipIndex, in SpriteAnimEventKey key,
                         ref BufferLookup<SpriteAnimEventBuffer> eventBuffers,
                         ref ComponentLookup<SpriteAnimEventsPending> pending)
        {
            if (key.EventId == 0 || !eventBuffers.HasBuffer(entity) || !pending.HasComponent(entity))
                return false;
            eventBuffers[entity].Add(new SpriteAnimEventBuffer
            {
                Id = key.EventId,
                ClipIndex = clipIndex,
                FrameIndex = key.FrameIndex,
                FireMode = key.FireMode,
                IntPayload = key.IntPayload,
                FloatPayload = key.FloatPayload,
                TextHash = key.TextHash,
                Payloads = key.Payloads,
            });
            pending.SetComponentEnabled(entity, true);
            return true;
        }

        static void UpdateSockets(Entity entity, int drawFrame,
                                  ref SpriteAnimDef def,
                                  ref BufferLookup<SpriteSocketBuffer> socketBuffers,
                                  ref ComponentLookup<SpriteFlip> flips)
        {
            if (!socketBuffers.HasBuffer(entity))
                return;
            var buffer = socketBuffers[entity];
            buffer.Clear();
            var flip = flips.HasComponent(entity) ? flips[entity] : default;
            for (int i = 0; i < def.FrameSockets.Length; i++)
            {
                var socket = def.FrameSockets[i];
                if (socket.FrameIndex != drawFrame)
                    continue;
                buffer.Add(SpriteFlipUtility.Socket(new SpriteSocketBuffer
                {
                    Name = socket.Name,
                    SocketId = socket.SocketId,
                    SocketIdHash = socket.SocketIdHash,
                    LocalPosition = socket.LocalPosition,
                    LocalAngle = socket.LocalAngle,
                    LocalScale = socket.LocalScale,
                }, flip));
            }
        }

        static float2 ReadFrameScale(ref SpriteAnimDef def, int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= def.FrameScales.Length)
                return new float2(1f, 1f);
            return def.FrameScales[frameIndex];
        }

        static float ReadFrameRotation(ref SpriteAnimDef def, int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= def.FrameRotations.Length)
                return 0f;
            return def.FrameRotations[frameIndex];
        }

        static SpriteEaseMode ReadTweenMode(ref SpriteAnimDef def, int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= def.FrameTweenModes.Length)
                return SpriteEaseMode.Linear;
            byte mode = def.FrameTweenModes[frameIndex];
            return SpriteEase.IsValidMode(mode)
                ? (SpriteEaseMode)mode
                : SpriteEaseMode.Linear;
        }

        internal static int DisplayFrame(int phaseStep, int frameCount, byte wrapMode)
        {
            if (frameCount <= 1)
                return 0;
            if (wrapMode == SpriteAnimWrap.Once)
                return math.clamp(phaseStep, 0, frameCount - 1);

            int raw;
            if (wrapMode == SpriteAnimWrap.PingPong)
            {
                int span = frameCount - 1;
                int cycle = span * 2;
                raw = PositiveMod(phaseStep, cycle);
                return raw <= span ? raw : cycle - raw;
            }

            raw = PositiveMod(phaseStep, frameCount);
            return wrapMode == SpriteAnimWrap.ReverseLoop ? frameCount - 1 - raw : raw;
        }

        static int CycleLength(int frameCount, byte wrapMode)
        {
            if (frameCount <= 1) return 1;
            return wrapMode == SpriteAnimWrap.PingPong ? (frameCount - 1) * 2 : frameCount;
        }

        static int PositiveMod(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
    }
}

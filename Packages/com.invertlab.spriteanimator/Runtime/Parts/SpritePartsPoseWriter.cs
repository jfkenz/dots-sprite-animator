using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Single Parts pose writer: sample clips, blend, local overrides, compose,
    /// LookAt, facing, sockets/hitboxes, then joint transforms. Gameplay writes
    /// <see cref="SpritePartsPoseOverride"/>, never part LocalTransform.
    /// </summary>
    public static class SpritePartsPoseWriter
    {
        public static SpritePartsPlayer DefaultPlayer(int clipIndex, bool playing = true)
        {
            return new SpritePartsPlayer
            {
                ClipIndex = clipIndex,
                TimeSeconds = 0f,
                SpeedMultiplier = 1f,
                Playing = playing ? (byte)1 : (byte)0,
                Completed = 0,
                PreviousClipIndex = -1,
                PreviousTimeSeconds = 0f,
                BlendDuration = 0f,
                BlendElapsed = 0f,
                SpriteSwitch = (byte)SpritePartsSpriteSwitch.UseIncomingImmediately,
            };
        }

        public static float IncomingWeight(in SpritePartsPlayer player)
        {
            if (!(player.BlendDuration > 1e-8f) || player.PreviousClipIndex < 0)
                return 1f;
            return math.saturate(player.BlendElapsed / player.BlendDuration);
        }

        public static bool IsPaused(in SpritePartsPlayer player)
            => player.Paused != 0 || (player.Playing == 0 && player.Completed == 0);

        public static void TickClocks(ref SpritePartsPlayer player, ref SpritePartsSetBlob set, float dt)
        {
            dt = math.max(0f, dt);
            bool paused = IsPaused(player);
            if (paused)
                return;

            if (player.ClipIndex >= 0 && player.ClipIndex < set.Clips.Length)
            {
                ref var clip = ref set.Clips[player.ClipIndex];
                var tick = SpritePartsPlayback.Tick(
                    player.TimeSeconds, player.SpeedMultiplier, clip.SpeedMultiplier,
                    clip.Duration, clip.WrapMode, player.Playing, player.Completed, dt);
                player.TimeSeconds = tick.TimeSeconds;
                player.Playing = tick.Playing;
                player.Completed = tick.AlreadyCompleted;
            }

            if (player.PreviousClipIndex >= 0 && player.PreviousClipIndex < set.Clips.Length
                && player.BlendDuration > 1e-8f)
            {
                ref var prev = ref set.Clips[player.PreviousClipIndex];
                byte prevPlaying = paused ? (byte)0 : (byte)1;
                byte prevDone = 0;
                if (prev.WrapMode == (byte)SpritePartsWrap.Once || prev.WrapMode == SpriteAnimWrap.Once)
                {
                    if (player.PreviousTimeSeconds >= prev.Duration - 1e-6f)
                    {
                        prevPlaying = 0;
                        prevDone = 1;
                    }
                }
                var prevTick = SpritePartsPlayback.Tick(
                    player.PreviousTimeSeconds, player.SpeedMultiplier, prev.SpeedMultiplier,
                    prev.Duration, prev.WrapMode, prevPlaying, prevDone, dt);
                player.PreviousTimeSeconds = prevTick.TimeSeconds;
                if (!paused)
                    player.BlendElapsed += dt;
                if (player.BlendElapsed >= player.BlendDuration)
                {
                    player.PreviousClipIndex = -1;
                    player.BlendDuration = 0f;
                    player.BlendElapsed = 0f;
                }
            }
            else
            {
                player.PreviousClipIndex = -1;
                player.BlendDuration = 0f;
                player.BlendElapsed = 0f;
            }
        }

        public static int ResolveAppearanceClip(in SpritePartsPlayer player)
        {
            float w = IncomingWeight(player);
            var rule = (SpritePartsSpriteSwitch)player.SpriteSwitch;
            bool hasPrev = player.PreviousClipIndex >= 0 && player.BlendDuration > 1e-8f;
            if (!hasPrev)
                return player.ClipIndex;
            switch (rule)
            {
                case SpritePartsSpriteSwitch.HoldUntilFadeEnd:
                    return player.PreviousClipIndex;
                case SpritePartsSpriteSwitch.SwitchAtMidpoint:
                case SpritePartsSpriteSwitch.UseHighestWeightClip:
                    return w >= 0.5f ? player.ClipIndex : player.PreviousClipIndex;
                default:
                    return player.ClipIndex;
            }
        }

        public static void Evaluate(
            ref SpritePartsSetBlob set,
            in SpritePartsPlayer player,
            NativeArray<SpritePartsPoseOverride> overrides,
            NativeArray<SpritePartsSampler.Pose> basePoses,
            NativeArray<SpritePartsSampler.Pose> finalLocal,
            NativeArray<float4x4> localToRoot,
            NativeArray<SpritePartPoseSource> sources,
            NativeArray<int> appearances,
            float4x4 rootWorld,
            bool flipX,
            bool flipY)
            => Evaluate(ref set, player, overrides, default, basePoses, finalLocal, localToRoot, sources,
                appearances, rootWorld, flipX, flipY);

        public static void Evaluate(
            ref SpritePartsSetBlob set,
            in SpritePartsPlayer player,
            NativeArray<SpritePartsPoseOverride> overrides,
            NativeArray<SpritePartsAnimLayer> layers,
            NativeArray<SpritePartsSampler.Pose> basePoses,
            NativeArray<SpritePartsSampler.Pose> finalLocal,
            NativeArray<float4x4> localToRoot,
            NativeArray<SpritePartPoseSource> sources,
            NativeArray<int> appearances,
            float4x4 rootWorld,
            bool flipX,
            bool flipY)
            => Evaluate(ref set, player, overrides, layers, basePoses, finalLocal, localToRoot, sources, appearances,
                rootWorld, flipX, flipY, default);

        public static void Evaluate(
            ref SpritePartsSetBlob set,
            in SpritePartsPlayer player,
            NativeArray<SpritePartsPoseOverride> overrides,
            NativeArray<SpritePartsAnimLayer> layers,
            NativeArray<SpritePartsSampler.Pose> basePoses,
            NativeArray<SpritePartsSampler.Pose> finalLocal,
            NativeArray<float4x4> localToRoot,
            NativeArray<SpritePartPoseSource> sources,
            NativeArray<int> appearances,
            float4x4 rootWorld,
            bool flipX,
            bool flipY,
            in SpritePartsEvalExtras extras)
        {
            int n = set.Slots.Length;
            float incoming = IncomingWeight(player);
            int appearClip = ResolveAppearanceClip(player);

            for (int i = 0; i < n; i++)
            {
                SpritePartsSampler.SampleSlot(ref set, player.ClipIndex, i, player.TimeSeconds, out var pose);
                if (player.PreviousClipIndex >= 0 && incoming < 1f)
                {
                    SpritePartsSampler.SampleSlot(
                        ref set, player.PreviousClipIndex, i, player.PreviousTimeSeconds, out var prev);
                    pose = BlendPose(prev, pose, incoming);
                }
                if (i < basePoses.Length)
                    basePoses[i] = pose;
                if (i < finalLocal.Length)
                    finalLocal[i] = pose;
                if (i < sources.Length)
                    sources[i] = new SpritePartPoseSource { Writer = (byte)SpritePartPoseWriterKind.Clip };
                if (i < appearances.Length)
                    appearances[i] = SpritePartsSampler.SampleAppearanceIndex(ref set, appearClip, i,
                        appearClip == player.ClipIndex ? player.TimeSeconds : player.PreviousTimeSeconds);
            }

            ApplyAnimationLayers(ref set, player, layers, finalLocal, sources);
            if (set.Params.Length > 0 && !extras.KeyedPoseOnly)
                SpritePartsParams.Apply(ref set, player.ClipIndex, extras.ParamValues, finalLocal);
            ApplyLocalOverrides(overrides, finalLocal, sources, n);
            SpritePartsHierarchy.ComposeLocalToRoot(ref set, finalLocal, localToRoot);
            ApplySpaceOverrides(ref set, overrides, finalLocal, localToRoot, sources, n, rootWorld, flipX, flipY);
            SpritePartsHierarchy.ComposeLocalToRoot(ref set, finalLocal, localToRoot);
            ApplyLookAtOverrides(ref set, overrides, finalLocal, localToRoot, sources, n, rootWorld, flipX, flipY);
            SpritePartsHierarchy.ComposeLocalToRoot(ref set, finalLocal, localToRoot);
            // IK constraints last, so they reach targets that clips or gameplay overrides moved.
            if (set.IkConstraints.Length > 0 && !extras.KeyedPoseOnly)
                SpritePartsIk.Apply(ref set, finalLocal, localToRoot);
            // Jiggle after IK: springs swing behind the final animated pose.
            if (set.Jiggles.Length > 0 && !extras.KeyedPoseOnly && extras.Jiggle.IsCreated)
                SpritePartsJiggle.Apply(ref set, finalLocal, localToRoot, extras.Jiggle, extras.DeltaTime,
                    math.mul(rootWorld, SpritePartsPlayback.FacingMatrix(flipX, flipY)));
            if (flipX || flipY)
            {
                for (int i = 0; i < sources.Length && i < n; i++)
                {
                    if (sources[i].Writer == (byte)SpritePartPoseWriterKind.Clip)
                        continue;
                    var src = sources[i];
                    if (src.Writer == 0)
                        src.Writer = (byte)SpritePartPoseWriterKind.Facing;
                    sources[i] = src;
                }
            }
        }

        /// <summary>
        /// Masked clip layers between the base clip and gameplay overrides.
        /// SlotMask 0 means every slot. Layers share the player's time clock.
        /// </summary>
        static void ApplyAnimationLayers(
            ref SpritePartsSetBlob set,
            in SpritePartsPlayer player,
            NativeArray<SpritePartsAnimLayer> layers,
            NativeArray<SpritePartsSampler.Pose> finalLocal,
            NativeArray<SpritePartPoseSource> sources)
        {
            if (!layers.IsCreated || layers.Length == 0)
                return;
            int n = set.Slots.Length;
            for (int L = 0; L < layers.Length; L++)
            {
                var layer = layers[L];
                if (layer.Weight <= 1e-6f || layer.ClipIndex < 0 || layer.ClipIndex >= set.Clips.Length)
                    continue;
                float w = math.saturate(layer.Weight);
                for (int i = 0; i < n && i < finalLocal.Length; i++)
                {
                    if (layer.SlotMask != 0 && i < 32 && (layer.SlotMask & (1u << i)) == 0)
                        continue;
                    SpritePartsSampler.SampleSlot(ref set, layer.ClipIndex, i, player.TimeSeconds, out var pose);
                    finalLocal[i] = BlendPose(finalLocal[i], pose, w);
                    if (i < sources.Length)
                    {
                        sources[i] = new SpritePartPoseSource
                        {
                            Writer = (byte)SpritePartPoseWriterKind.Layer,
                            Weight = w,
                        };
                    }
                }
            }
        }

        static SpritePartsSampler.Pose BlendPose(
            in SpritePartsSampler.Pose from, in SpritePartsSampler.Pose to, float t)
        {
            return new SpritePartsSampler.Pose
            {
                Position = math.lerp(from.Position, to.Position, t),
                Scale = math.lerp(from.Scale, to.Scale, t),
                Rotation = SpritePartsSampler.LerpAngleShortest(from.Rotation, to.Rotation, t),
                Lattice = SpritePartsLattice.Lerp(from.Lattice, to.Lattice, t),
            };
        }

        static void ApplyLocalOverrides(
            NativeArray<SpritePartsPoseOverride> overrides,
            NativeArray<SpritePartsSampler.Pose> finalLocal,
            NativeArray<SpritePartPoseSource> sources,
            int slotCount)
        {
            if (!overrides.IsCreated || overrides.Length == 0)
                return;
            var order = new NativeArray<int>(overrides.Length, Allocator.Temp);
            try
            {
                SortOverrideIndices(overrides, order);
                for (int o = 0; o < order.Length; o++)
                {
                    var ov = overrides[order[o]];
                    if (ov.Weight <= 1e-6f || ov.SlotIndex < 0 || ov.SlotIndex >= slotCount)
                        continue;
                    if (ov.SlotIndex >= finalLocal.Length)
                        continue;
                    var mode = (SpritePartsPoseMode)ov.Mode;
                    if (mode == SpritePartsPoseMode.LookAt)
                        continue;
                    byte channels = ov.Channels == 0 ? (byte)SpritePartsPoseChannel.All : ov.Channels;
                    var space = (SpritePartsPoseSpace)ov.Space;
                    bool spaceTrs = space == SpritePartsPoseSpace.World || space == SpritePartsPoseSpace.Character;
                    if (spaceTrs && (channels & (byte)SpritePartsPoseChannel.Scale) == 0)
                        continue;

                    var pose = finalLocal[ov.SlotIndex];
                    float w = math.saturate(ov.Weight);
                    if (spaceTrs)
                    {
                        if (mode == SpritePartsPoseMode.Replace)
                            pose.Scale = math.lerp(pose.Scale, ov.Scale, w);
                        else
                            pose.Scale += ov.Scale * w;
                        finalLocal[ov.SlotIndex] = pose;
                        MarkOverrideSource(sources, ov, mode, w);
                        continue;
                    }
                    if (mode == SpritePartsPoseMode.Replace)
                    {
                        if ((channels & (byte)SpritePartsPoseChannel.Position) != 0)
                            pose.Position = math.lerp(pose.Position, ov.Position, w);
                        if ((channels & (byte)SpritePartsPoseChannel.Rotation) != 0)
                            pose.Rotation = SpritePartsSampler.LerpAngleShortest(pose.Rotation, ov.Rotation, w);
                        if ((channels & (byte)SpritePartsPoseChannel.Scale) != 0)
                            pose.Scale = math.lerp(pose.Scale, ov.Scale, w);
                    }
                    else
                    {
                        if ((channels & (byte)SpritePartsPoseChannel.Position) != 0)
                            pose.Position += ov.Position * w;
                        if ((channels & (byte)SpritePartsPoseChannel.Rotation) != 0)
                            pose.Rotation += ov.Rotation * w;
                        if ((channels & (byte)SpritePartsPoseChannel.Scale) != 0)
                            pose.Scale += ov.Scale * w;
                    }
                    finalLocal[ov.SlotIndex] = pose;
                    MarkOverrideSource(sources, ov, mode, w);
                }
            }
            finally
            {
                order.Dispose();
            }
        }

        static void MarkOverrideSource(
            NativeArray<SpritePartPoseSource> sources,
            in SpritePartsPoseOverride ov,
            SpritePartsPoseMode mode,
            float weight)
        {
            if (ov.SlotIndex < 0 || ov.SlotIndex >= sources.Length)
                return;
            sources[ov.SlotIndex] = new SpritePartPoseSource
            {
                Writer = mode == SpritePartsPoseMode.Add
                    ? (byte)SpritePartPoseWriterKind.Add
                    : (byte)SpritePartPoseWriterKind.Replace,
                Space = ov.Space,
                Weight = weight,
                OverrideId = ov.Id,
            };
        }

        static void ApplySpaceOverrides(
            ref SpritePartsSetBlob set,
            NativeArray<SpritePartsPoseOverride> overrides,
            NativeArray<SpritePartsSampler.Pose> finalLocal,
            NativeArray<float4x4> localToRoot,
            NativeArray<SpritePartPoseSource> sources,
            int slotCount,
            float4x4 rootWorld,
            bool flipX,
            bool flipY)
        {
            if (!overrides.IsCreated || overrides.Length == 0)
                return;
            var order = new NativeArray<int>(overrides.Length, Allocator.Temp);
            try
            {
                SortOverrideIndices(overrides, order);
                float4x4 facing = SpritePartsPlayback.FacingMatrix(flipX, flipY);
                float4x4 worldFromCharacter = math.mul(rootWorld, facing);
                for (int o = 0; o < order.Length; o++)
                {
                    var ov = overrides[order[o]];
                    var mode = (SpritePartsPoseMode)ov.Mode;
                    if (mode == SpritePartsPoseMode.LookAt)
                        continue;
                    var space = (SpritePartsPoseSpace)ov.Space;
                    if (space != SpritePartsPoseSpace.World && space != SpritePartsPoseSpace.Character)
                        continue;
                    if (ov.Weight <= 1e-6f || ov.SlotIndex < 0 || ov.SlotIndex >= slotCount)
                        continue;
                    if (ov.SlotIndex >= finalLocal.Length || ov.SlotIndex >= localToRoot.Length)
                        continue;

                    byte channels = ov.Channels == 0 ? (byte)SpritePartsPoseChannel.All : ov.Channels;
                    bool writePos = (channels & (byte)SpritePartsPoseChannel.Position) != 0;
                    bool writeRot = (channels & (byte)SpritePartsPoseChannel.Rotation) != 0;
                    if (!writePos && !writeRot)
                        continue;

                    float w = math.saturate(ov.Weight);
                    float2 currentChar = SpritePartsHierarchy.TransformPoint(localToRoot[ov.SlotIndex], float2.zero);
                    float currentCharRot = SpritePartsHierarchy.ExtractRotationDeg(localToRoot[ov.SlotIndex]);
                    float2 desiredChar = currentChar;
                    float desiredCharRot = currentCharRot;

                    if (space == SpritePartsPoseSpace.Character)
                    {
                        if (writePos)
                        {
                            desiredChar = mode == SpritePartsPoseMode.Replace
                                ? math.lerp(currentChar, ov.Position, w)
                                : currentChar + ov.Position * w;
                        }
                        if (writeRot)
                        {
                            desiredCharRot = mode == SpritePartsPoseMode.Replace
                                ? SpritePartsSampler.LerpAngleShortest(currentCharRot, ov.Rotation, w)
                                : currentCharRot + ov.Rotation * w;
                        }
                    }
                    else
                    {
                        float2 currentWorld = SpritePartsHierarchy.TransformPoint(worldFromCharacter, currentChar);
                        float currentWorldRot = SpritePartsHierarchy.ExtractRotationDeg(
                            math.mul(worldFromCharacter, localToRoot[ov.SlotIndex]));
                        if (writePos)
                        {
                            float2 desiredWorld = mode == SpritePartsPoseMode.Replace
                                ? math.lerp(currentWorld, ov.Position, w)
                                : currentWorld + ov.Position * w;
                            desiredChar = SpritePartsHierarchy.InverseTransformPoint(worldFromCharacter, desiredWorld);
                        }
                        if (writeRot)
                        {
                            float desiredWorldRot = mode == SpritePartsPoseMode.Replace
                                ? SpritePartsSampler.LerpAngleShortest(currentWorldRot, ov.Rotation, w)
                                : currentWorldRot + ov.Rotation * w;
                            desiredCharRot = DirectionToLocalAngle(worldFromCharacter, desiredWorldRot, 1f);
                        }
                    }

                    int parentSlot = set.Slots[ov.SlotIndex].ParentSlotIndex;
                    float4x4 parentM = parentSlot >= 0 && parentSlot < localToRoot.Length
                        ? localToRoot[parentSlot]
                        : float4x4.identity;
                    var pose = finalLocal[ov.SlotIndex];
                    if (writePos)
                        pose.Position = SpritePartsHierarchy.InverseTransformPoint(parentM, desiredChar);
                    if (writeRot)
                    {
                        pose.Rotation = DirectionToLocalAngle(parentM, desiredCharRot, pose.Scale.x);
                    }
                    finalLocal[ov.SlotIndex] = pose;
                    SpritePartsHierarchy.ComposeLocalToRoot(ref set, finalLocal, localToRoot);
                    if (ov.SlotIndex < sources.Length)
                    {
                        sources[ov.SlotIndex] = new SpritePartPoseSource
                        {
                            Writer = mode == SpritePartsPoseMode.Add
                                ? (byte)SpritePartPoseWriterKind.Add
                                : (byte)SpritePartPoseWriterKind.Replace,
                            Space = ov.Space,
                            Weight = w,
                            OverrideId = ov.Id,
                        };
                    }
                }
            }
            finally
            {
                order.Dispose();
            }
        }

        static void ApplyLookAtOverrides(
            ref SpritePartsSetBlob set,
            NativeArray<SpritePartsPoseOverride> overrides,
            NativeArray<SpritePartsSampler.Pose> finalLocal,
            NativeArray<float4x4> localToRoot,
            NativeArray<SpritePartPoseSource> sources,
            int slotCount,
            float4x4 rootWorld,
            bool flipX,
            bool flipY)
        {
            if (!overrides.IsCreated || overrides.Length == 0)
                return;
            var order = new NativeArray<int>(overrides.Length, Allocator.Temp);
            try
            {
                SortOverrideIndices(overrides, order);
                float4x4 facing = SpritePartsPlayback.FacingMatrix(flipX, flipY);
                float4x4 worldFromCharacter = math.mul(rootWorld, facing);
                float4x4 characterFromWorld = math.inverse(worldFromCharacter);
                for (int o = 0; o < order.Length; o++)
                {
                    var ov = overrides[order[o]];
                    if ((SpritePartsPoseMode)ov.Mode != SpritePartsPoseMode.LookAt)
                        continue;
                    if (ov.Weight <= 1e-6f || ov.SlotIndex < 0 || ov.SlotIndex >= slotCount)
                        continue;
                    if (ov.SlotIndex >= finalLocal.Length || ov.SlotIndex >= localToRoot.Length)
                        continue;

                    float2 targetCharacter = ov.Target;
                    var space = (SpritePartsPoseSpace)ov.Space;
                    if (space == SpritePartsPoseSpace.World)
                        targetCharacter = SpritePartsHierarchy.TransformPoint(characterFromWorld, ov.Target);
                    else if (space == SpritePartsPoseSpace.Local || space == SpritePartsPoseSpace.Parent)
                    {
                        int parent = set.Slots[ov.SlotIndex].ParentSlotIndex;
                        float4x4 parentM = parent >= 0 && parent < localToRoot.Length
                            ? localToRoot[parent]
                            : float4x4.identity;
                        targetCharacter = SpritePartsHierarchy.TransformPoint(parentM, ov.Target);
                    }

                    float2 slotPos = SpritePartsHierarchy.TransformPoint(localToRoot[ov.SlotIndex], float2.zero);
                    float2 dir = targetCharacter - slotPos;
                    if (math.lengthsq(dir) < 1e-10f)
                        continue;
                    dir = math.normalizesafe(dir, new float2(1f, 0f));
                    float desired = math.degrees(math.atan2(dir.y, dir.x)) + ov.Rotation;

                    int parentSlot = set.Slots[ov.SlotIndex].ParentSlotIndex;
                    float4x4 aimParent = parentSlot >= 0 && parentSlot < localToRoot.Length
                        ? localToRoot[parentSlot] : float4x4.identity;
                    var pose = finalLocal[ov.SlotIndex];
                    float localRot = DirectionToLocalAngle(aimParent, desired, pose.Scale.x);
                    float w = math.saturate(ov.Weight);
                    pose.Rotation = SpritePartsSampler.LerpAngleShortest(pose.Rotation, localRot, w);
                    finalLocal[ov.SlotIndex] = pose;
                    SpritePartsHierarchy.ComposeLocalToRoot(ref set, finalLocal, localToRoot);
                    if (ov.SlotIndex < sources.Length)
                    {
                        sources[ov.SlotIndex] = new SpritePartPoseSource
                        {
                            Writer = (byte)SpritePartPoseWriterKind.Aim,
                            Space = ov.Space,
                            Weight = w,
                            OverrideId = ov.Id,
                        };
                    }
                }
            }
            finally
            {
                order.Dispose();
            }
        }

        static float DirectionToLocalAngle(float4x4 parent, float angle, float scaleX)
        {
            float2 direction = Rotate2(new float2(1f, 0f), angle);
            float2 local = math.mul(math.inverse(parent), new float4(direction, 0f, 0f)).xy;
            // The rendered/socket +X axis also includes the joint's own mirror.
            if (scaleX < 0f)
                local = -local;
            return math.degrees(math.atan2(local.y, local.x));
        }

        static void SortOverrideIndices(NativeArray<SpritePartsPoseOverride> overrides, NativeArray<int> order)
        {
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            for (int i = 1; i < order.Length; i++)
            {
                int key = order[i];
                int j = i - 1;
                while (j >= 0 && CompareOverride(overrides[order[j]], overrides[key]) > 0)
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = key;
            }
        }

        static int CompareOverride(in SpritePartsPoseOverride a, in SpritePartsPoseOverride b)
        {
            int p = a.Priority.CompareTo(b.Priority);
            return p != 0 ? p : a.Id.CompareTo(b.Id);
        }

        public static void EvaluateEditor(
            ref SpritePartsSetBlob set,
            int clipIndex,
            float timeSeconds,
            NativeArray<SpritePartsSampler.Pose> localPoses,
            NativeArray<float4x4> localToRoot)
            => EvaluateEditor(ref set, clipIndex, timeSeconds, localPoses, localToRoot, default);

        public static void EvaluateEditor(
            ref SpritePartsSetBlob set,
            int clipIndex,
            float timeSeconds,
            NativeArray<SpritePartsSampler.Pose> localPoses,
            NativeArray<float4x4> localToRoot,
            in SpritePartsEvalExtras extras)
        {
            int n = set.Slots.Length;
            var player = DefaultPlayer(clipIndex, playing: false);
            player.ClipIndex = clipIndex;
            player.TimeSeconds = timeSeconds;
            var emptyOv = new NativeArray<SpritePartsPoseOverride>(0, Allocator.Temp);
            var basePoses = new NativeArray<SpritePartsSampler.Pose>(n, Allocator.Temp);
            var sources = new NativeArray<SpritePartPoseSource>(n, Allocator.Temp);
            var apps = new NativeArray<int>(n, Allocator.Temp);
            try
            {
                Evaluate(ref set, player, emptyOv, default, basePoses, localPoses, localToRoot, sources, apps,
                    float4x4.identity, false, false, extras);
            }
            finally
            {
                emptyOv.Dispose();
                basePoses.Dispose();
                sources.Dispose();
                apps.Dispose();
            }
        }

        public static void Apply(EntityManager em, Entity root, EntityCommandBuffer commands, bool deferred)
            => Apply(em, root, commands, deferred, 0f);

        /// <param name="deltaTime">Frame time for jiggle springs (0 holds them still).</param>
        public static void Apply(EntityManager em, Entity root, EntityCommandBuffer commands, bool deferred, float deltaTime)
        {
            if (!em.HasComponent<SpritePartsSetRef>(root) || !em.HasComponent<SpritePartsPlayer>(root))
                return;
            var blob = em.GetComponentData<SpritePartsSetRef>(root).Set;
            if (!blob.IsCreated)
                return;
            EnsureBuffers(em, root);
            ref var set = ref blob.Value;
            int n = set.Slots.Length;
            var player = em.GetComponentData<SpritePartsPlayer>(root);
            var overrides = em.HasBuffer<SpritePartsPoseOverride>(root)
                ? em.GetBuffer<SpritePartsPoseOverride>(root).AsNativeArray()
                : default;
            var layers = em.HasBuffer<SpritePartsAnimLayer>(root)
                ? em.GetBuffer<SpritePartsAnimLayer>(root).AsNativeArray()
                : default;

            byte diag = 0;
            if (em.HasBuffer<SpritePartLink>(root) && em.HasBuffer<SpritePartFinalPose>(root))
            {
                var prevFinal = em.GetBuffer<SpritePartFinalPose>(root);
                var links = em.GetBuffer<SpritePartLink>(root);
                if (prevFinal.Length == n)
                {
                    for (int i = 0; i < links.Length; i++)
                    {
                        var part = links[i].Part;
                        int slot = links[i].SlotIndex;
                        if (part == Entity.Null || part == root || !em.Exists(part) || slot < 0 || slot >= n)
                            continue;
                        if (em.HasComponent<SpritePartPhysicsOwned>(part))
                            continue;
                        if (prevFinal[slot].PhysicsSkipped != 0)
                            continue; // First animation write after physics releases ownership.
                        if (!em.HasComponent<LocalTransform>(part))
                            continue;
                        var last = new SpritePartsSampler.Pose
                        {
                            Position = prevFinal[slot].Position,
                            Rotation = prevFinal[slot].Rotation,
                            Scale = prevFinal[slot].Scale,
                        };
                        if (TransformsFought(em.GetComponentData<LocalTransform>(part), last))
                            diag |= SpritePartsPoseDiagnostics.GameplayWroteTransform;
                    }
                }
            }

            var basePoses = new NativeArray<SpritePartsSampler.Pose>(n, Allocator.Temp);
            var finalLocal = new NativeArray<SpritePartsSampler.Pose>(n, Allocator.Temp);
            var localToRoot = new NativeArray<float4x4>(n, Allocator.Temp);
            var sources = new NativeArray<SpritePartPoseSource>(n, Allocator.Temp);
            var apps = new NativeArray<int>(n, Allocator.Temp);
            try
            {
                bool flipX = false, flipY = false;
                if (em.HasComponent<SpritePartsFacing>(root))
                {
                    var facing = em.GetComponentData<SpritePartsFacing>(root);
                    flipX = facing.FlipX != 0;
                    flipY = facing.FlipY != 0;
                }
                float4x4 rootWorld = CurrentEntityWorld(em, root);

                var extras = new SpritePartsEvalExtras { DeltaTime = deltaTime };
                if (set.Params.Length > 0 && em.HasBuffer<SpritePartsParamValue>(root))
                    extras.ParamValues = em.GetBuffer<SpritePartsParamValue>(root).AsNativeArray().Reinterpret<float>();
                if (set.Jiggles.Length > 0 && em.HasBuffer<SpritePartJiggleState>(root))
                {
                    var jiggle = em.GetBuffer<SpritePartJiggleState>(root);
                    if (jiggle.Length != set.Jiggles.Length)
                    {
                        jiggle.Clear();
                        jiggle.Resize(set.Jiggles.Length, NativeArrayOptions.ClearMemory);
                    }
                    extras.Jiggle = jiggle.AsNativeArray();
                }

                Evaluate(ref set, player, overrides, layers, basePoses, finalLocal, localToRoot, sources, apps,
                    rootWorld, flipX, flipY, extras);

                WritePoseBuffers(em, root, basePoses, finalLocal, sources, n);
                if (!em.HasComponent<SpritePartsVisualRootRef>(root)
                    || em.GetComponentData<SpritePartsVisualRootRef>(root).VisualRoot == Entity.Null)
                    diag |= SpritePartsPoseDiagnostics.MissingVisualRoot;
                if (!em.HasBuffer<SpritePartLink>(root) || em.GetBuffer<SpritePartLink>(root).Length != n)
                    diag |= SpritePartsPoseDiagnostics.LinkMismatch;

                if (em.HasBuffer<SpritePartLink>(root))
                {
                    var links = em.GetBuffer<SpritePartLink>(root);
                    for (int i = 0; i < links.Length; i++)
                    {
                        var part = links[i].Part;
                        int slot = links[i].SlotIndex;
                        if (part == Entity.Null || part == root || !em.Exists(part))
                            continue;
                        if (slot < 0 || slot >= n)
                            continue;
                        bool physics = em.HasComponent<SpritePartPhysicsOwned>(part);
                        if (em.HasBuffer<SpritePartFinalPose>(root) && slot < em.GetBuffer<SpritePartFinalPose>(root).Length)
                        {
                            var finals = em.GetBuffer<SpritePartFinalPose>(root);
                            var fp = finals[slot];
                            fp.PhysicsSkipped = physics ? (byte)1 : (byte)0;
                            finals[slot] = fp;
                        }
                        if (physics)
                            continue;
                        // Weighted meshes follow their bound bones (Spine weights); keys stay pre-skin.
                        var partPose = finalLocal[slot];
                        SpritePartsSkinning.Apply(ref set, slot, localToRoot, ref partPose.Lattice);
                        if (deferred)
                            SpritePartsPoseUtility.ApplyPartTransform(em, part, partPose, commands);
                        else
                            SpritePartsPoseUtility.ApplyPartTransform(em, part, partPose);
                        if (deferred)
                            SpriteParts.ApplySampledAppearance(em, root, part, slot, apps[slot], ref set, commands);
                        else
                            SpriteParts.ApplySampledAppearance(em, root, part, slot, apps[slot], ref set);
                    }
                }

                if (deferred)
                    SpritePartsPoseUtility.ApplyFacing(em, root, commands);
                else
                    SpritePartsPoseUtility.ApplyFacing(em, root);
                ComposeAttachmentWorld(em, root, ref set, finalLocal, localToRoot, rootWorld, flipX, flipY);
                WriteSockets(em, root, localToRoot);
                WriteHitboxes(em, root, localToRoot);
                WriteDiagnostics(em, root, player, diag, commands, deferred);
            }
            finally
            {
                basePoses.Dispose();
                finalLocal.Dispose();
                localToRoot.Dispose();
                sources.Dispose();
                apps.Dispose();
            }
        }

        public static void Apply(EntityManager em, Entity root)
            => Apply(em, root, default, false);

        public static void EnsureBuffers(EntityManager em, Entity root)
        {
            EnsureBuffer<SpritePartsPoseOverride>(em, root);
            EnsureBuffer<SpritePartBasePose>(em, root);
            EnsureBuffer<SpritePartFinalPose>(em, root);
            EnsureBuffer<SpritePartPoseSource>(em, root);
            EnsureBuffer<SpritePartsAnimLayer>(em, root);
            EnsureBuffer<SpritePartSocketBinding>(em, root);
            EnsureBuffer<SpritePartSocketWorld>(em, root);
            EnsureBuffer<SpritePartHitboxBinding>(em, root);
            EnsureBuffer<SpritePartHitboxWorld>(em, root);
            if (BlobHasJiggles(em, root))
                EnsureBuffer<SpritePartJiggleState>(em, root);
            if (em.HasComponent<SpritePartsSetRef>(root))
            {
                var set = em.GetComponentData<SpritePartsSetRef>(root).Set;
                if (set.IsCreated && set.Value.Params.Length > 0)
                    SpritePartsParams.EnsureValues(em, root, ref set.Value);
            }
            if (!em.HasComponent<SpritePartsPoseDiagnostics>(root))
                em.AddComponentData(root, new SpritePartsPoseDiagnostics { PreviousClipIndex = -1 });
        }

        static bool BlobHasJiggles(EntityManager em, Entity root)
        {
            if (!em.HasComponent<SpritePartsSetRef>(root))
                return false;
            var blob = em.GetComponentData<SpritePartsSetRef>(root).Set;
            return blob.IsCreated && blob.Value.Jiggles.Length > 0;
        }

        static void EnsureBuffer<T>(EntityManager em, Entity root)
            where T : unmanaged, IBufferElementData
        {
            if (!em.HasBuffer<T>(root))
                em.AddBuffer<T>(root);
        }

        static void WritePoseBuffers(
            EntityManager em, Entity root,
            NativeArray<SpritePartsSampler.Pose> basePoses,
            NativeArray<SpritePartsSampler.Pose> finalLocal,
            NativeArray<SpritePartPoseSource> sources,
            int n)
        {
            if (em.HasBuffer<SpritePartBasePose>(root))
            {
                var buf = em.GetBuffer<SpritePartBasePose>(root);
                buf.ResizeUninitialized(n);
                for (int i = 0; i < n; i++)
                    buf[i] = new SpritePartBasePose
                    {
                        Position = basePoses[i].Position,
                        Rotation = basePoses[i].Rotation,
                        Scale = basePoses[i].Scale,
                    };
            }
            if (em.HasBuffer<SpritePartFinalPose>(root))
            {
                var buf = em.GetBuffer<SpritePartFinalPose>(root);
                buf.ResizeUninitialized(n);
                for (int i = 0; i < n; i++)
                    buf[i] = new SpritePartFinalPose
                    {
                        Position = finalLocal[i].Position,
                        Rotation = finalLocal[i].Rotation,
                        Scale = finalLocal[i].Scale,
                    };
            }
            if (em.HasBuffer<SpritePartPoseSource>(root))
            {
                var buf = em.GetBuffer<SpritePartPoseSource>(root);
                buf.ResizeUninitialized(n);
                for (int i = 0; i < n; i++)
                    buf[i] = sources[i];
            }
        }

        static bool TransformsFought(in LocalTransform lt, in SpritePartsSampler.Pose pose)
        {
            if (math.abs(lt.Scale - 1f) > 1e-3f)
                return true;
            if (math.distance(lt.Position.xy, pose.Position) > 1e-3f)
                return true;
            float z = math.degrees(2f * math.atan2(lt.Rotation.value.z, lt.Rotation.value.w));
            float diff = pose.Rotation - z;
            diff = math.fmod(diff + 180f, 360f);
            if (diff < 0f) diff += 360f;
            diff -= 180f;
            return math.abs(diff) > 1.5f;
        }

        static float4x4 CurrentEntityWorld(EntityManager em, Entity entity)
        {
            float4x4 world = EntityLocalMatrix(em, entity);
            Entity current = entity;
            int guard = 0;
            while (em.HasComponent<Parent>(current) && guard++ < 64)
            {
                var parent = em.GetComponentData<Parent>(current).Value;
                if (parent == Entity.Null || !em.Exists(parent))
                    break;
                world = math.mul(EntityLocalMatrix(em, parent), world);
                current = parent;
            }
            return world;
        }

        static float4x4 EntityLocalMatrix(EntityManager em, Entity entity)
        {
            float4x4 local = float4x4.identity;
            if (em.HasComponent<LocalTransform>(entity))
            {
                var lt = em.GetComponentData<LocalTransform>(entity);
                local = float4x4.TRS(lt.Position, lt.Rotation, new float3(lt.Scale));
            }
            if (em.HasComponent<PostTransformMatrix>(entity))
                local = math.mul(local, em.GetComponentData<PostTransformMatrix>(entity).Value);
            return local;
        }

        static bool IsPhysicsOwned(EntityManager em, Entity part)
            => part != Entity.Null && em.Exists(part) && em.HasComponent<SpritePartPhysicsOwned>(part);

        /// <summary>
        /// World matrices for attachments. Physics-owned slots (and therefore their
        /// descendants) use the live parent-local physical pose and ECS hierarchy; animated slots use
        /// the pose just written this update so export is not one LocalToWorld behind.
        /// </summary>
        static void ComposeAttachmentWorld(
            EntityManager em, Entity root, ref SpritePartsSetBlob set,
            NativeArray<SpritePartsSampler.Pose> finalLocal,
            NativeArray<float4x4> slotWorld,
            float4x4 rootWorld, bool flipX, bool flipY)
        {
            int n = set.Slots.Length;
            var partsBySlot = new NativeArray<Entity>(n, Allocator.Temp);
            var done = new NativeArray<bool>(n, Allocator.Temp);
            try
            {
                for (int i = 0; i < n; i++)
                    partsBySlot[i] = Entity.Null;
                if (em.HasBuffer<SpritePartLink>(root))
                {
                    var links = em.GetBuffer<SpritePartLink>(root);
                    for (int i = 0; i < links.Length; i++)
                    {
                        int slot = links[i].SlotIndex;
                        if (slot >= 0 && slot < n)
                            partsBySlot[slot] = links[i].Part;
                    }
                }
                float4x4 visualWorld = math.mul(rootWorld, SpritePartsPlayback.FacingMatrix(flipX, flipY));
                Entity visualRoot = em.HasComponent<SpritePartsVisualRootRef>(root)
                    ? em.GetComponentData<SpritePartsVisualRootRef>(root).VisualRoot
                    : Entity.Null;
                for (int i = 0; i < n; i++)
                    EnsureAttachmentWorld(em, ref set, finalLocal, slotWorld, partsBySlot, done, visualRoot, visualWorld, i);
            }
            finally
            {
                partsBySlot.Dispose();
                done.Dispose();
            }
        }

        static void EnsureAttachmentWorld(
            EntityManager em, ref SpritePartsSetBlob set,
            NativeArray<SpritePartsSampler.Pose> finalLocal,
            NativeArray<float4x4> slotWorld,
            NativeArray<Entity> partsBySlot,
            NativeArray<bool> done,
            Entity visualRoot,
            float4x4 visualWorld,
            int index)
        {
            if (index < 0 || index >= set.Slots.Length || done[index])
                return;
            // Mark before recursion so malformed runtime Parent cycles cannot
            // overflow the stack. Valid hierarchies resolve parent before child.
            done[index] = true;
            slotWorld[index] = float4x4.identity;
            Entity part = index < partsBySlot.Length ? partsBySlot[index] : Entity.Null;
            int parent = set.Slots[index].ParentSlotIndex;
            float4x4 parentWorld = visualWorld;
            if (part != Entity.Null && em.Exists(part))
            {
                Entity actualParent = em.HasComponent<Parent>(part)
                    ? em.GetComponentData<Parent>(part).Value : Entity.Null;
                parent = -1;
                if (actualParent == Entity.Null || !em.Exists(actualParent))
                    parentWorld = float4x4.identity;
                else if (actualParent != visualRoot)
                {
                    for (int i = 0; i < partsBySlot.Length; i++)
                        if (partsBySlot[i] == actualParent) { parent = i; break; }
                    if (parent < 0)
                        parentWorld = CurrentEntityWorld(em, actualParent);
                }
            }
            if (parent >= 0 && parent < slotWorld.Length)
            {
                EnsureAttachmentWorld(em, ref set, finalLocal, slotWorld, partsBySlot, done, visualRoot, visualWorld, parent);
                parentWorld = slotWorld[parent];
            }

            float4x4 local;
            if (IsPhysicsOwned(em, part))
                local = EntityLocalMatrix(em, part);
            else if (index < finalLocal.Length)
            {
                var pose = finalLocal[index];
                local = SpritePartsHierarchy.LocalMatrix(pose.Position, pose.Rotation, pose.Scale);
            }
            else
                local = float4x4.identity;

            slotWorld[index] = math.mul(parentWorld, local);
        }

        static float2 Rotate2(float2 p, float degrees)
        {
            float rad = math.radians(degrees);
            float c = math.cos(rad);
            float s = math.sin(rad);
            return new float2(c * p.x - s * p.y, s * p.x + c * p.y);
        }

        static void WriteSockets(EntityManager em, Entity root, NativeArray<float4x4> slotWorld)
        {
            if (!em.HasBuffer<SpritePartSocketBinding>(root) || !em.HasBuffer<SpritePartSocketWorld>(root))
                return;
            var bind = em.GetBuffer<SpritePartSocketBinding>(root);
            var world = em.GetBuffer<SpritePartSocketWorld>(root);
            world.ResizeUninitialized(bind.Length);
            for (int i = 0; i < bind.Length; i++)
            {
                var b = bind[i];
                float4x4 m = b.SlotIndex >= 0 && b.SlotIndex < slotWorld.Length
                    ? slotWorld[b.SlotIndex]
                    : float4x4.identity;
                // Transform the socket's axis before extracting its angle. Adding
                // angles loses reflections and the effect of non-uniform scale.
                float2 localDirection = Rotate2(new float2(1f, 0f), b.LocalRotation);
                float2 worldDirection = math.mul(m, new float4(localDirection, 0f, 0f)).xy;
                world[i] = new SpritePartSocketWorld
                {
                    SocketIdHash = b.SocketIdHash,
                    SlotIndex = b.SlotIndex,
                    WorldPosition = SpritePartsHierarchy.TransformPoint(m, b.LocalOffset),
                    WorldRotation = math.degrees(math.atan2(worldDirection.y, worldDirection.x)),
                };
            }
        }

        static void WriteHitboxes(EntityManager em, Entity root, NativeArray<float4x4> slotWorld)
        {
            if (!em.HasBuffer<SpritePartHitboxBinding>(root) || !em.HasBuffer<SpritePartHitboxWorld>(root))
                return;
            var bind = em.GetBuffer<SpritePartHitboxBinding>(root);
            var world = em.GetBuffer<SpritePartHitboxWorld>(root);
            world.ResizeUninitialized(bind.Length);
            for (int i = 0; i < bind.Length; i++)
            {
                var b = bind[i];
                float4x4 m = b.SlotIndex >= 0 && b.SlotIndex < slotWorld.Length
                    ? slotWorld[b.SlotIndex]
                    : float4x4.identity;
                float2 h = b.LocalSize * 0.5f;
                float2 min = new float2(float.MaxValue);
                float2 max = new float2(float.MinValue);
                for (int k = 0; k < 4; k++)
                {
                    float sx = (k == 1 || k == 2) ? h.x : -h.x;
                    float sy = (k >= 2) ? h.y : -h.y;
                    float2 local = b.LocalCenter + Rotate2(new float2(sx, sy), b.LocalRotation);
                    float2 p = SpritePartsHierarchy.TransformPoint(m, local);
                    min = math.min(min, p);
                    max = math.max(max, p);
                }
                world[i] = new SpritePartHitboxWorld
                {
                    SlotIndex = b.SlotIndex,
                    Center = (min + max) * 0.5f,
                    Extents = (max - min) * 0.5f,
                    Kind = b.Kind,
                };
            }
        }

        static void WriteDiagnostics(EntityManager em, Entity root, in SpritePartsPlayer player, byte flags,
            EntityCommandBuffer commands, bool deferred)
        {
            var diag = em.HasComponent<SpritePartsPoseDiagnostics>(root)
                ? em.GetComponentData<SpritePartsPoseDiagnostics>(root)
                : new SpritePartsPoseDiagnostics { PreviousClipIndex = -1 };
            diag.Flags = flags;
            diag.ClipIndex = player.ClipIndex;
            diag.PreviousClipIndex = player.PreviousClipIndex;
            diag.Blend01 = IncomingWeight(player);
            if (deferred && !em.HasComponent<SpritePartsPoseDiagnostics>(root))
                commands.AddComponent(root, diag);
            else if (!em.HasComponent<SpritePartsPoseDiagnostics>(root))
                em.AddComponentData(root, diag);
            else
                em.SetComponentData(root, diag);
        }
    }
}

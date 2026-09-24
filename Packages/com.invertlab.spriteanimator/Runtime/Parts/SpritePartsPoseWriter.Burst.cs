using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Pose evaluation compiled by Burst (the per-character hot path of <see cref="SpritePartsPoseWriter.Apply(Unity.Entities.EntityManager, Unity.Entities.Entity, Unity.Entities.EntityCommandBuffer, bool, float)"/>).</summary>
    public static partial class SpritePartsPoseWriter
    {
        /// <summary>
        /// True (default): characters evaluate their pose Burst-compiled. False: plain C# (step through it when
        /// debugging). Falls back to C# by itself if the Burst job ever fails.
        /// </summary>
        public static bool BurstEvaluate = true;

        [BurstCompile]
        struct EvaluateJob : IJob
        {
            [ReadOnly] public Unity.Entities.BlobAssetReference<SpritePartsSetBlob> Set;
            public SpritePartsPlayer Player;
            [ReadOnly, NativeDisableContainerSafetyRestriction] public NativeArray<SpritePartsPoseOverride> Overrides;
            [ReadOnly, NativeDisableContainerSafetyRestriction] public NativeArray<SpritePartsAnimLayer> Layers;
            [NativeDisableContainerSafetyRestriction] public NativeArray<SpritePartsSampler.Pose> BasePoses;
            [NativeDisableContainerSafetyRestriction] public NativeArray<SpritePartsSampler.Pose> FinalLocal;
            [NativeDisableContainerSafetyRestriction] public NativeArray<float4x4> LocalToRoot;
            [NativeDisableContainerSafetyRestriction] public NativeArray<SpritePartPoseSource> Sources;
            [NativeDisableContainerSafetyRestriction] public NativeArray<int> Apps;
            [NativeDisableContainerSafetyRestriction] public NativeArray<SpritePartJiggleState> Jiggle;
            [ReadOnly, NativeDisableContainerSafetyRestriction] public NativeArray<float> ParamValues;
            [ReadOnly, NativeDisableContainerSafetyRestriction] public NativeArray<SpritePartsMixEntry> MixChain;
            [ReadOnly, NativeDisableContainerSafetyRestriction] public NativeArray<int> GroupStates;
            public float4x4 RootWorld;
            public bool FlipX;
            public bool FlipY;
            public float DeltaTime;
            public bool HasJiggle;
            public bool HasParams;
            public bool HasMixChain;
            public bool HasGroupStates;
            public SpritePartsBlendState Blend;
            public bool HasBlend;

            public void Execute()
            {
                var extras = new SpritePartsEvalExtras { DeltaTime = DeltaTime, Clip = true, Blend = Blend, HasBlend = HasBlend };
                if (HasJiggle)
                    extras.Jiggle = Jiggle;
                if (HasParams)
                    extras.ParamValues = ParamValues;
                if (HasMixChain)
                    extras.MixChain = MixChain;
                if (HasGroupStates)
                    extras.GroupStates = GroupStates;
                Evaluate(ref Set.Value, Player, Overrides, Layers, BasePoses, FinalLocal, LocalToRoot, Sources, Apps,
                    RootWorld, FlipX, FlipY, extras);
            }
        }

        /// <summary>
        /// <see cref="Evaluate(ref SpritePartsSetBlob, in SpritePartsPlayer, NativeArray{SpritePartsPoseOverride}, NativeArray{SpritePartsAnimLayer}, NativeArray{SpritePartsSampler.Pose}, NativeArray{SpritePartsSampler.Pose}, NativeArray{float4x4}, NativeArray{SpritePartPoseSource}, NativeArray{int}, float4x4, bool, bool, in SpritePartsEvalExtras)"/>
        /// through Burst when <see cref="BurstEvaluate"/> is on. The output arrays must not be Temp (use TempJob).
        /// </summary>
        static void EvaluateFast(Unity.Entities.BlobAssetReference<SpritePartsSetBlob> blob, in SpritePartsPlayer player,
            NativeArray<SpritePartsPoseOverride> overrides, NativeArray<SpritePartsAnimLayer> layers,
            NativeArray<SpritePartsSampler.Pose> basePoses, NativeArray<SpritePartsSampler.Pose> finalLocal,
            NativeArray<float4x4> localToRoot, NativeArray<SpritePartPoseSource> sources, NativeArray<int> apps,
            float4x4 rootWorld, bool flipX, bool flipY, in SpritePartsEvalExtras extras)
        {
            if (!BurstEvaluate)
            {
                Evaluate(ref blob.Value, player, overrides, layers, basePoses, finalLocal, localToRoot, sources, apps,
                    rootWorld, flipX, flipY, extras);
                return;
            }
            // Every container a job holds must exist: stand-ins for the missing ones (the flags keep "missing").
            var noOverrides = overrides.IsCreated ? default : new NativeArray<SpritePartsPoseOverride>(0, Allocator.TempJob);
            var noLayers = layers.IsCreated ? default : new NativeArray<SpritePartsAnimLayer>(0, Allocator.TempJob);
            var noJiggle = extras.Jiggle.IsCreated ? default : new NativeArray<SpritePartJiggleState>(0, Allocator.TempJob);
            var noParams = extras.ParamValues.IsCreated ? default : new NativeArray<float>(0, Allocator.TempJob);
            var noChain = extras.MixChain.IsCreated ? default : new NativeArray<SpritePartsMixEntry>(0, Allocator.TempJob);
            var noGroups = extras.GroupStates.IsCreated ? default : new NativeArray<int>(0, Allocator.TempJob);
            try
            {
                new EvaluateJob
                {
                    Set = blob,
                    Player = player,
                    Overrides = overrides.IsCreated ? overrides : noOverrides,
                    Layers = layers.IsCreated ? layers : noLayers,
                    BasePoses = basePoses,
                    FinalLocal = finalLocal,
                    LocalToRoot = localToRoot,
                    Sources = sources,
                    Apps = apps,
                    Jiggle = extras.Jiggle.IsCreated ? extras.Jiggle : noJiggle,
                    ParamValues = extras.ParamValues.IsCreated ? extras.ParamValues : noParams,
                    MixChain = extras.MixChain.IsCreated ? extras.MixChain : noChain,
                    GroupStates = extras.GroupStates.IsCreated ? extras.GroupStates : noGroups,
                    RootWorld = rootWorld,
                    FlipX = flipX,
                    FlipY = flipY,
                    DeltaTime = extras.DeltaTime,
                    HasJiggle = extras.Jiggle.IsCreated,
                    HasParams = extras.ParamValues.IsCreated,
                    HasMixChain = extras.MixChain.IsCreated,
                    HasGroupStates = extras.GroupStates.IsCreated,
                    Blend = extras.Blend,
                    HasBlend = extras.HasBlend,
                }.Run();
            }
            catch (Exception e)
            {
                BurstEvaluate = false;
                Debug.LogWarning("[Parts] Burst pose evaluation failed; using C# from now on. " + e.Message);
                Evaluate(ref blob.Value, player, overrides, layers, basePoses, finalLocal, localToRoot, sources, apps,
                    rootWorld, flipX, flipY, extras);
            }
            finally
            {
                if (noOverrides.IsCreated) noOverrides.Dispose();
                if (noLayers.IsCreated) noLayers.Dispose();
                if (noJiggle.IsCreated) noJiggle.Dispose();
                if (noParams.IsCreated) noParams.Dispose();
                if (noChain.IsCreated) noChain.Dispose();
                if (noGroups.IsCreated) noGroups.Dispose();
            }
        }
    }
}

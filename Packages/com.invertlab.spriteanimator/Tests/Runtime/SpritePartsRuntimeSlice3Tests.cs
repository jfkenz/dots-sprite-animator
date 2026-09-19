using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePartsRuntimeSlice3Tests
    {
        [Test]
        public void PlayerSystem_DefersMissingMatricesAndCompletionAcrossMultipleRoots()
        {
            using var world = new World("Parts structural regression");
            var em = world.EntityManager;
            var blob = BuildNestedRig((byte)SpritePartsWrap.Once, 0.1f);
            var a = SpritePartsEntityFactory.Create(em, blob, float3.zero, playing: true);
            var b = SpritePartsEntityFactory.Create(em, blob, float3.zero, playing: true);
            try
            {
                foreach (var part in a.Parts) em.RemoveComponent<PostTransformMatrix>(part);
                em.RemoveComponent<PostTransformMatrix>(a.VisualRoot);
                world.SetTime(new Unity.Core.TimeData(1, 0.2f));
                var system = world.GetOrCreateSystem<SpritePartsPlayerSystem>();
                Assert.DoesNotThrow(() => system.Update(world.Unmanaged));
                foreach (var part in a.Parts)
                    Assert.IsTrue(em.HasComponent<PostTransformMatrix>(part));
                Assert.IsTrue(em.HasComponent<PostTransformMatrix>(a.VisualRoot));
                Assert.IsTrue(em.HasComponent<SpritePartsCompleted>(a.Root));
                Assert.IsTrue(em.HasComponent<SpritePartsCompleted>(b.Root));
                Assert.AreEqual(0, em.GetComponentData<SpritePartsPlayer>(b.Root).Playing);
                Assert.DoesNotThrow(() => system.Update(world.Unmanaged));
            }
            finally
            {
                a.Parts.Dispose();
                b.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        BlobAssetReference<SpritePartsSetBlob> BuildNestedRig(
            byte wrap = (byte)SpritePartsWrap.Loop,
            float duration = 1f,
            float clipSpeed = 1f,
            int hiddenSlotIndex = -1)
        {
            var appearances = new[]
            {
                new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = "body.art",
                    SheetTableIndex = 0,
                    CellIndex = 0,
                    LogicalWorldSize = new float2(1f, 1f),
                    Pivot = new float2(0.5f, 0.5f),
                    FrameOffset = float2.zero,
                    FrameScale = new float2(1f, 1f),
                },
                new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = "hand.art",
                    SheetTableIndex = 0,
                    CellIndex = 1,
                    LogicalWorldSize = new float2(0.5f, 0.5f),
                    Pivot = new float2(0.5f, 0.5f),
                    FrameOffset = float2.zero,
                    FrameScale = new float2(0.5f, 0.5f),
                },
                new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = "weapon.art",
                    SheetTableIndex = 0,
                    CellIndex = 2,
                    LogicalWorldSize = new float2(0.25f, 1f),
                    Pivot = new float2(0.5f, 0.1f),
                    FrameOffset = (new float2(0.5f, 0.5f) - new float2(0.5f, 0.1f)) * new float2(0.25f, 1f),
                    FrameScale = new float2(0.25f / (0.25f / 1f), 1f),
                },
            };
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Body", SlotId = "body",
                    RestPosition = new float2(0f, 0f), RestRotation = 0f, RestScale = new float2(1f, 1f),
                    DefaultAppearanceId = "body.art", DrawRank = 0,
                },
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Hand", SlotId = "hand", ParentSlotId = "body",
                    RestPosition = new float2(1f, 0f), RestRotation = 0f, RestScale = new float2(1f, 1f),
                    DefaultAppearanceId = "hand.art", DrawRank = 1,
                },
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Weapon", SlotId = "weapon", ParentSlotId = "hand",
                    RestPosition = new float2(0.5f, 0f), RestRotation = 0f, RestScale = new float2(1f, 1f),
                    DefaultAppearanceId = "weapon.art", DrawRank = 2,
                },
            };
            if (hiddenSlotIndex >= 0 && hiddenSlotIndex < slots.Length)
                slots[hiddenSlotIndex].Hidden = 1;
            var tracks = new[]
            {
                new SpritePartsSetBuilder.TrackInput
                {
                    SlotId = "body",
                    Keys = new[]
                    {
                        new SpritePartsSetBuilder.KeyInput
                        {
                            Time = 0f, Position = new float2(0f, 0f), Rotation = 0f,
                            Scale = new float2(2f, 1f), EaseMode = (byte)SpriteEaseMode.Linear,
                        },
                        new SpritePartsSetBuilder.KeyInput
                        {
                            Time = duration, Position = new float2(0f, 0f), Rotation = 90f,
                            Scale = new float2(2f, 1f), EaseMode = (byte)SpriteEaseMode.Linear,
                        },
                    },
                },
                new SpritePartsSetBuilder.TrackInput
                {
                    SlotId = "hand",
                    Keys = new[]
                    {
                        new SpritePartsSetBuilder.KeyInput
                        {
                            Time = 0f, Position = new float2(1f, 0f), Rotation = 0f,
                            Scale = new float2(1f, 1f), EaseMode = (byte)SpriteEaseMode.Linear,
                        },
                    },
                },
            };
            var clips = new[]
            {
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "Walk", ClipId = "walk", Duration = duration,
                    SpeedMultiplier = clipSpeed, WrapMode = wrap, Tracks = tracks,
                },
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "OnceClip", ClipId = "once", Duration = duration,
                    SpeedMultiplier = 1f, WrapMode = (byte)SpritePartsWrap.Once,
                    Tracks = System.Array.Empty<SpritePartsSetBuilder.TrackInput>(),
                },
            };
            return SpritePartsSetBuilder.Build(Allocator.Temp, slots, appearances, clips,
                System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
        }

        [Test]
        public void HierarchyMatrix_RotatedScaledParent_NestedWeapon()
        {
            var blob = BuildNestedRig();
            try
            {
                var poses = new NativeArray<SpritePartsSampler.Pose>(3, Allocator.Temp);
                var mats = new NativeArray<float4x4>(3, Allocator.Temp);
                try
                {
                    SpritePartsSampler.SampleAll(ref blob.Value, 0, 0f, poses);
                    SpritePartsHierarchy.ComposeLocalToRoot(ref blob.Value, poses, mats);
                    // Body at rest key: scale (2,1), rot 0; hand local (1,0); weapon local (0.5,0)
                    // World hand = bodyScale * (1,0) = (2,0)
                    // World weapon = handWorld + bodyScale * handRot * (0.5,0) = (2,0) + (1,0) = (3,0) with body scale 2 on X through parent chain
                    float2 hand = SpritePartsHierarchy.TransformPoint(mats[1], float2.zero);
                    float2 weapon = SpritePartsHierarchy.TransformPoint(mats[2], float2.zero);
                    Assert.AreEqual(2f, hand.x, 1e-4f);
                    Assert.AreEqual(0f, hand.y, 1e-4f);
                    Assert.AreEqual(3f, weapon.x, 1e-4f);
                    Assert.AreEqual(0f, weapon.y, 1e-4f);

                    // Mid-clip: body rotated 45deg with nonuniform scale — full matrix, not translation-only.
                    SpritePartsSampler.SampleAll(ref blob.Value, 0, 0.5f, poses);
                    SpritePartsHierarchy.ComposeLocalToRoot(ref blob.Value, poses, mats);
                    float2 weaponMid = SpritePartsHierarchy.TransformPoint(mats[2], float2.zero);
                    Assert.Greater(math.length(weaponMid), 0.1f);
                    // Rotation present on body matrix.
                    float bodyRot = SpritePartsHierarchy.ExtractRotationDeg(mats[0]);
                    Assert.AreEqual(45f, bodyRot, 0.5f);
                }
                finally
                {
                    poses.Dispose();
                    mats.Dispose();
                }
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void Factory_CreatesRootVisualParts_NoNegativePhysicsScale_OnFacing()
        {
            var blob = BuildNestedRig();
            var previous = World.DefaultGameObjectInjectionWorld;
            using var world = new World("Parts facing");
            World.DefaultGameObjectInjectionWorld = world;
            try
            {
                var em = world.EntityManager;
                var created = SpritePartsEntityFactory.Create(em, blob, new float3(1f, 2f, 0f),
                    characterOrder: 3, flipX: true, flipY: false, clipIndex: 0, playing: false);
                try
                {
                    var rootLt = em.GetComponentData<LocalTransform>(created.Root);
                    Assert.AreEqual(1f, rootLt.Scale, 1e-5f);
                    Assert.GreaterOrEqual(rootLt.Scale, 0f);
                    var facing = em.GetComponentData<SpritePartsFacing>(created.Root);
                    Assert.AreEqual(1, facing.FlipX);
                    var ptm = em.GetComponentData<PostTransformMatrix>(created.VisualRoot);
                    Assert.Less(ptm.Value.c0.x, 0f); // mirrored X via matrix, not LocalTransform.Scale
                    Assert.AreEqual(1f, em.GetComponentData<LocalTransform>(created.VisualRoot).Scale, 1e-5f);

                    // Part local z stays 0; depth component present.
                    for (int i = 0; i < created.Parts.Length; i++)
                    {
                        Assert.AreEqual(0f, em.GetComponentData<LocalTransform>(created.Parts[i]).Position.z, 1e-5f);
                        Assert.IsTrue(em.HasComponent<SpritePartRenderDepth>(created.Parts[i]));
                        Assert.IsTrue(em.HasComponent<PostTransformMatrix>(created.Parts[i]));
                        Assert.AreEqual(1f, em.GetComponentData<LocalTransform>(created.Parts[i]).Scale, 1e-5f);
                    }

                    int expected = SpritePartsPlayback.DrawIndex(3, blob.Value.Slots[2].DrawRank);
                    Assert.AreEqual(3 * 64 + 2, expected);
                }
                finally
                {
                    created.Parts.Dispose();
                }
            }
            finally
            {
                World.DefaultGameObjectInjectionWorld = previous;
                blob.Dispose();
            }
        }

        [Test]
        public void GpuRejection_PartsRootAndChildren()
        {
            var blob = BuildNestedRig();
            var previous = World.DefaultGameObjectInjectionWorld;
            using var world = new World("Parts gpu reject");
            World.DefaultGameObjectInjectionWorld = world;
            try
            {
                var em = world.EntityManager;
                var created = SpritePartsEntityFactory.Create(em, blob, float3.zero);
                try
                {
                    Assert.IsFalse(SpriteGpuEligibility.IsGpuEligible(em, created.Root, out var reason));
                    Assert.That(reason.ToString(), Does.Contain("Parts"));
                    Assert.IsFalse(SpriteGpuEligibility.IsGpuEligible(em, created.Parts[0], out _));
                    Assert.IsFalse(SpriteGpuAnimSwitch.ToGpu(em, created.Root, 0f));
                    Assert.IsFalse(SpriteAnims.TryToGpu(em, created.Root));
                }
                finally { created.Parts.Dispose(); }
            }
            finally
            {
                World.DefaultGameObjectInjectionWorld = previous;
                blob.Dispose();
            }
        }

        [Test]
        public void Controls_PlayPauseSeekStop_OnceCompletionOnce_LoopWrap()
        {
            var blob = BuildNestedRig(wrap: (byte)SpritePartsWrap.Loop, duration: 1f);
            var previous = World.DefaultGameObjectInjectionWorld;
            using var world = new World("Parts controls");
            World.DefaultGameObjectInjectionWorld = world;
            try
            {
                var em = world.EntityManager;
                var created = SpritePartsEntityFactory.Create(em, blob, float3.zero, playing: true);
                try
                {
                    Entity root = created.Root;
                    Assert.IsTrue(SpriteAnims.Play(em, root, "Walk"));
                    // Crossfade unsupported — no state change.
                    var before = em.GetComponentData<SpritePartsPlayer>(root);
                    Assert.IsFalse(SpriteAnims.Play(em, root, "Walk", force: false, crossfadeSeconds: 0.2f));
                    var after = em.GetComponentData<SpritePartsPlayer>(root);
                    Assert.AreEqual(before.TimeSeconds, after.TimeSeconds, 1e-6f);
                    Assert.AreEqual(before.ClipIndex, after.ClipIndex);

                    SpriteAnims.Pause(em, root);
                    Assert.AreEqual(0, em.GetComponentData<SpritePartsPlayer>(root).Playing);
                    SpriteAnims.Resume(em, root);
                    Assert.AreEqual(1, em.GetComponentData<SpritePartsPlayer>(root).Playing);

                    SpriteAnims.SeekNormalized(em, root, 0.5f);
                    Assert.AreEqual(0.5f, em.GetComponentData<SpritePartsPlayer>(root).TimeSeconds, 1e-4f);

                    SpriteAnims.Stop(em, root);
                    var stopped = em.GetComponentData<SpritePartsPlayer>(root);
                    Assert.AreEqual(0, stopped.Playing);
                    Assert.AreEqual(0f, stopped.TimeSeconds, 1e-5f);

                    // Loop wrap via pure tick helper.
                    var wrap = SpritePartsPlayback.Tick(0.9f, 1f, 1f, 1f, (byte)SpritePartsWrap.Loop, 1, 0, 0.2f);
                    Assert.AreEqual(0.1f, wrap.TimeSeconds, 1e-4f);
                    Assert.AreEqual(0, wrap.CompletedThisTick);

                    // Once completion once.
                    var once1 = SpritePartsPlayback.Tick(0.9f, 1f, 1f, 1f, (byte)SpritePartsWrap.Once, 1, 0, 0.2f);
                    Assert.AreEqual(1, once1.CompletedThisTick);
                    Assert.AreEqual(0, once1.Playing);
                    var once2 = SpritePartsPlayback.Tick(1f, 1f, 1f, 1f, (byte)SpritePartsWrap.Once, 0, 1, 0.2f);
                    Assert.AreEqual(0, once2.CompletedThisTick);

                    Assert.IsTrue(SpriteAnims.Play(em, root, "OnceClip", force: true));
                    SpriteAnims.Restart(em, root);
                    Assert.AreEqual(1, em.GetComponentData<SpritePartsPlayer>(root).Playing);
                }
                finally { created.Parts.Dispose(); }
            }
            finally
            {
                World.DefaultGameObjectInjectionWorld = previous;
                blob.Dispose();
            }
        }

        [Test]
        public void Lifecycle_TwoInstances_DestroyOne_KeepsOtherAndSharedBlob()
        {
            var blob = BuildNestedRig();
            var previous = World.DefaultGameObjectInjectionWorld;
            using var world = new World("Parts lifecycle");
            World.DefaultGameObjectInjectionWorld = world;
            try
            {
                var em = world.EntityManager;
                var a = SpritePartsEntityFactory.Create(em, blob, new float3(0f, 0f, 0f), characterOrder: 0);
                var b = SpritePartsEntityFactory.Create(em, blob, new float3(2f, 0f, 0f), characterOrder: 1);
                try
                {
                    Assert.IsTrue(em.Exists(a.Root));
                    Assert.IsTrue(em.Exists(b.Root));
                    Assert.IsTrue(em.HasBuffer<LinkedEntityGroup>(a.Root));
                    // Destroy A including linked group.
                    em.DestroyEntity(a.Root);
                    Assert.IsFalse(em.Exists(a.Root));
                    Assert.IsTrue(em.Exists(b.Root));
                    Assert.IsTrue(em.Exists(b.Parts[0]));
                    Assert.IsTrue(blob.IsCreated);
                    Assert.AreEqual(3, blob.Value.Slots.Length);
                }
                finally
                {
                    a.Parts.Dispose();
                    b.Parts.Dispose();
                }
            }
            finally
            {
                World.DefaultGameObjectInjectionWorld = previous;
                blob.Dispose();
            }
        }

        [Test]
        public void Factory_HiddenSlotDisablesDraw()
        {
            var blob = BuildNestedRig(hiddenSlotIndex: 1);
            var previous = World.DefaultGameObjectInjectionWorld;
            using var world = new World("Parts hidden factory");
            World.DefaultGameObjectInjectionWorld = world;
            try
            {
                var em = world.EntityManager;
                var created = SpritePartsEntityFactory.Create(em, blob, float3.zero, playing: false);
                try
                {
                    Assert.AreEqual(0, em.GetComponentData<SpritePartSlot>(created.Parts[0]).Hidden);
                    Assert.IsTrue(em.IsComponentEnabled<SpriteAnimEnabled>(created.Parts[0]));
                    Assert.AreEqual(1, em.GetComponentData<SpritePartSlot>(created.Parts[1]).Hidden);
                    Assert.IsFalse(em.IsComponentEnabled<SpriteAnimEnabled>(created.Parts[1]));
                }
                finally { created.Parts.Dispose(); }
            }
            finally
            {
                World.DefaultGameObjectInjectionWorld = previous;
                blob.Dispose();
            }
        }

        [Test]
        public void CullingKeepsHiddenPartsDisabled()
        {
            var blob = BuildNestedRig(hiddenSlotIndex: 1);
            var previous = World.DefaultGameObjectInjectionWorld;
            using var world = new World("Parts hidden cull");
            World.DefaultGameObjectInjectionWorld = world;
            var host = new GameObject("Parts cull camera");
            bool previousLayout = SpriteBatchSpawner.LayoutXy;
            try
            {
                SpriteBatchSpawner.LayoutXy = true;
                host.tag = "MainCamera";
                var camera = host.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 20f;
                host.transform.SetPositionAndRotation(new Vector3(0, 0, -10), Quaternion.identity);

                var em = world.EntityManager;
                world.GetOrCreateSystem<SpriteAnimCullingSystem>();
                using (var settings = em.CreateEntityQuery(typeof(SpriteCullSettings)))
                {
                    if (settings.IsEmptyIgnoreFilter)
                    {
                        em.AddComponentData(em.CreateEntity(),
                            new SpriteCullSettings { MarginUnits = 8f, MaxDistanceSq = 0f });
                    }
                }
                var created = SpritePartsEntityFactory.Create(em, blob, float3.zero, playing: false);
                try
                {
                    var hidden = created.Parts[1];
                    var culler = world.GetOrCreateSystem<SpritePartsCullingSystem>();
                    culler.Update(world.Unmanaged);
                    em.CompleteAllTrackedJobs();
                    Assert.IsFalse(em.IsComponentEnabled<SpriteAnimEnabled>(hidden));
                    Assert.IsTrue(em.IsComponentEnabled<SpriteAnimEnabled>(created.Parts[0]));
                }
                finally { created.Parts.Dispose(); }
            }
            finally
            {
                SpriteBatchSpawner.LayoutXy = previousLayout;
                Object.DestroyImmediate(host);
                World.DefaultGameObjectInjectionWorld = previous;
                blob.Dispose();
            }
        }

        [Test]
        public void AppearanceGeometry_WrittenOnParts()
        {
            var blob = BuildNestedRig();
            var previous = World.DefaultGameObjectInjectionWorld;
            using var world = new World("Parts geometry");
            World.DefaultGameObjectInjectionWorld = world;
            try
            {
                var em = world.EntityManager;
                var created = SpritePartsEntityFactory.Create(em, blob, float3.zero);
                try
                {
                    var weapon = created.Parts[2];
                    var frame = em.GetComponentData<SpriteAnimFrame>(weapon);
                    var app = em.GetComponentData<SpritePartAppearanceState>(weapon);
                    Assert.AreEqual(app.CellIndex, frame.Slot);
                    Assert.AreEqual(app.FrameOffset.x, frame.Offset.x, 1e-5f);
                    Assert.AreEqual(app.FrameScale.y, frame.Scale.y, 1e-5f);
                    Assert.AreEqual(0f, frame.Rotation, 1e-5f);
                    // visualPoint contract smoke
                    float2 q = new float2(-0.5f, -0.5f);
                    float2 vp = SpritePartsGeometry.VisualPoint(q, app.Pivot, app.LogicalWorldSize);
                    Assert.IsTrue(math.all(math.isfinite(vp)));
                }
                finally { created.Parts.Dispose(); }
            }
            finally
            {
                World.DefaultGameObjectInjectionWorld = previous;
                blob.Dispose();
            }
        }

        [Test]
        public void RenderDepth_UsesCharacterOrderTimes64PlusRank()
        {
            Assert.AreEqual(0, SpritePartsPlayback.DrawIndex(0, 0));
            Assert.AreEqual(64, SpritePartsPlayback.DrawIndex(1, 0));
            Assert.AreEqual(66, SpritePartsPlayback.DrawIndex(1, 2));
            float d0 = SpriteSortDepth.FromIndex(0);
            float d1 = SpriteSortDepth.FromIndex(1);
            Assert.AreEqual(SpriteSortDepth.OrderStep, math.abs(d1 - d0), 1e-6f);
        }
    }
}

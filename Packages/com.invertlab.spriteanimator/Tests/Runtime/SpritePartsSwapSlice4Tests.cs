using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePartsSwapSlice4Tests
    {
        BlobAssetReference<SpritePartsSetBlob> BuildRigWithWeapons()
        {
            // 32px/32PPU sword => 1wu; 96px/48PPU spear => 2wu; off-center grips.
            float2 swordSize = new float2(1f, 1f);
            float2 swordPivot = new float2(0.2f, 0.1f);
            float2 spearSize = new float2(2f, 2f);
            float2 spearPivot = new float2(0.15f, 0.05f);
            Assert.IsTrue(SpritePartsGeometry.TryResolveExplicit(swordSize, swordPivot, 0, 0,
                out var swordGeo, out _));
            Assert.IsTrue(SpritePartsGeometry.TryResolveExplicit(spearSize, spearPivot, 1, 0,
                out var spearGeo, out _));
            Assert.IsTrue(SpritePartsGeometry.TryResolveExplicit(new float2(0.8f, 0.8f), new float2(0.5f, 0.5f), 0, 1,
                out var headGeo, out _));
            Assert.IsTrue(SpritePartsGeometry.TryResolveExplicit(new float2(1f, 1.2f), new float2(0.5f, 0.5f), 0, 2,
                out var bodyGeo, out _));

            var appearances = new[]
            {
                new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = "head.default",
                    SheetTableIndex = headGeo.SheetIndex,
                    CellIndex = headGeo.CellIndex,
                    LogicalWorldSize = headGeo.LogicalWorldSize,
                    Pivot = headGeo.Pivot,
                    FrameOffset = headGeo.FrameOffset,
                    FrameScale = headGeo.FrameScale,
                },
                new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = "body.default",
                    SheetTableIndex = bodyGeo.SheetIndex,
                    CellIndex = bodyGeo.CellIndex,
                    LogicalWorldSize = bodyGeo.LogicalWorldSize,
                    Pivot = bodyGeo.Pivot,
                    FrameOffset = bodyGeo.FrameOffset,
                    FrameScale = bodyGeo.FrameScale,
                },
                new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = "sword.iron",
                    SheetTableIndex = swordGeo.SheetIndex,
                    CellIndex = swordGeo.CellIndex,
                    LogicalWorldSize = swordGeo.LogicalWorldSize,
                    Pivot = swordGeo.Pivot,
                    FrameOffset = swordGeo.FrameOffset,
                    FrameScale = swordGeo.FrameScale,
                },
                new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = "spear.oak",
                    SheetTableIndex = spearGeo.SheetIndex,
                    CellIndex = spearGeo.CellIndex,
                    LogicalWorldSize = spearGeo.LogicalWorldSize,
                    Pivot = spearGeo.Pivot,
                    FrameOffset = spearGeo.FrameOffset,
                    FrameScale = spearGeo.FrameScale,
                },
                // Intentionally invalid geometry for atomic-fail tests.
                new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = "weapon.broken",
                    SheetTableIndex = 0,
                    CellIndex = 0,
                    LogicalWorldSize = new float2(0f, 1f),
                    Pivot = new float2(0.5f, 0.5f),
                    FrameOffset = float2.zero,
                    FrameScale = new float2(1f, 1f),
                },
            };

            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Body", SlotId = "body",
                    RestPosition = float2.zero, RestRotation = 0f, RestScale = new float2(1f, 1f),
                    DefaultAppearanceId = "body.default", DrawRank = 0,
                },
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Head", SlotId = "head", ParentSlotId = "body",
                    RestPosition = new float2(0f, 0.6f), RestRotation = 0f, RestScale = new float2(1f, 1f),
                    DefaultAppearanceId = "head.default", DrawRank = 1,
                },
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Weapon", SlotId = "weapon", ParentSlotId = "body",
                    RestPosition = new float2(0.4f, 0.1f), RestRotation = 12f, RestScale = new float2(1f, 1f),
                    DefaultAppearanceId = "sword.iron", DrawRank = 2,
                },
            };

            var tracks = new[]
            {
                new SpritePartsSetBuilder.TrackInput
                {
                    SlotId = "body",
                    Keys = new[]
                    {
                        new SpritePartsSetBuilder.KeyInput
                        {
                            Time = 0f, Position = float2.zero, Rotation = 0f,
                            Scale = new float2(1f, 1f), EaseMode = (byte)SpriteEaseMode.Linear,
                        },
                        new SpritePartsSetBuilder.KeyInput
                        {
                            Time = 0.6f, Position = new float2(0.2f, 0f), Rotation = 0f,
                            Scale = new float2(1f, 1f), EaseMode = (byte)SpriteEaseMode.Linear,
                        },
                    },
                },
                new SpritePartsSetBuilder.TrackInput
                {
                    SlotId = "weapon",
                    Keys = new[]
                    {
                        new SpritePartsSetBuilder.KeyInput
                        {
                            Time = 0f, Position = new float2(0.4f, 0.1f), Rotation = 12f,
                            Scale = new float2(1f, 1f), EaseMode = (byte)SpriteEaseMode.Linear,
                        },
                        new SpritePartsSetBuilder.KeyInput
                        {
                            Time = 0.6f, Position = new float2(0.45f, 0.15f), Rotation = 40f,
                            Scale = new float2(1f, 1f), EaseMode = (byte)SpriteEaseMode.Linear,
                        },
                    },
                },
            };

            var clips = new[]
            {
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "Walk", ClipId = "walk", Duration = 0.6f,
                    SpeedMultiplier = 1f, WrapMode = (byte)SpritePartsWrap.Loop, Tracks = tracks,
                },
            };

            var skins = new[]
            {
                new SpritePartsSetBuilder.SkinInput
                {
                    SkinId = "weapon.sword",
                    Bindings = new[]
                    {
                        new SpritePartsSetBuilder.SkinBindingInput
                        {
                            SlotId = "weapon", AppearanceId = "sword.iron",
                        },
                    },
                },
                new SpritePartsSetBuilder.SkinInput
                {
                    SkinId = "weapon.spear",
                    Bindings = new[]
                    {
                        new SpritePartsSetBuilder.SkinBindingInput
                        {
                            SlotId = "weapon", AppearanceId = "spear.oak",
                        },
                    },
                },
                new SpritePartsSetBuilder.SkinInput
                {
                    SkinId = "weapon.bad",
                    Bindings = new[]
                    {
                        new SpritePartsSetBuilder.SkinBindingInput
                        {
                            SlotId = "weapon", AppearanceId = "weapon.broken",
                        },
                    },
                },
            };

            return SpritePartsSetBuilder.Build(Allocator.Temp, slots, appearances, clips, skins);
        }

        [Test]
        public void Swap_LeavesWalkTimeIndexKeysAndJointPose()
        {
            var blob = BuildRigWithWeapons();
            var previous = World.DefaultGameObjectInjectionWorld;
            using var world = new World("Parts swap pose");
            World.DefaultGameObjectInjectionWorld = world;
            try
            {
                var em = world.EntityManager;
                var created = SpritePartsEntityFactory.Create(em, blob, float3.zero, playing: true);
                try
                {
                    Entity root = created.Root;
                    Assert.IsTrue(SpriteParts.Play(em, root, "Walk"));
                    SpriteParts.SeekNormalized(em, root, 0.5f);
                    var playerBefore = em.GetComponentData<SpritePartsPlayer>(root);
                    Assert.AreEqual(0, playerBefore.ClipIndex);
                    Assert.AreEqual(0.3f, playerBefore.TimeSeconds, 1e-4f);
                    Assert.AreEqual(1f, playerBefore.SpeedMultiplier, 1e-5f);

                    Assert.IsTrue(SpriteParts.TryGetSlot(em, root, "weapon", out Entity weapon));
                    var jointBefore = em.GetComponentData<LocalTransform>(weapon);
                    var postBefore = em.GetComponentData<PostTransformMatrix>(weapon);
                    var appBefore = em.GetComponentData<SpritePartAppearanceState>(weapon);

                    Assert.IsTrue(SpriteParts.SetSlotAppearance(em, root, "weapon", "spear.oak"));

                    var playerAfter = em.GetComponentData<SpritePartsPlayer>(root);
                    Assert.AreEqual(playerBefore.ClipIndex, playerAfter.ClipIndex);
                    Assert.AreEqual(playerBefore.TimeSeconds, playerAfter.TimeSeconds, 1e-6f);
                    Assert.AreEqual(playerBefore.SpeedMultiplier, playerAfter.SpeedMultiplier, 1e-6f);
                    Assert.AreEqual(playerBefore.Playing, playerAfter.Playing);

                    var jointAfter = em.GetComponentData<LocalTransform>(weapon);
                    Assert.AreEqual(jointBefore.Position.x, jointAfter.Position.x, 1e-5f);
                    Assert.AreEqual(jointBefore.Position.y, jointAfter.Position.y, 1e-5f);
                    Assert.AreEqual(jointBefore.Rotation.value.z, jointAfter.Rotation.value.z, 1e-5f);
                    Assert.AreEqual(1f, jointAfter.Scale, 1e-5f);
                    var postAfter = em.GetComponentData<PostTransformMatrix>(weapon);
                    Assert.AreEqual(postBefore.Value.c0.x, postAfter.Value.c0.x, 1e-5f);
                    Assert.AreEqual(postBefore.Value.c1.y, postAfter.Value.c1.y, 1e-5f);

                    // Keys still sample the same joint pose at current time.
                    SpritePartsSampler.SampleSlot(ref blob.Value, playerAfter.ClipIndex, 2,
                        playerAfter.TimeSeconds, out var sampled);
                    Assert.AreEqual(sampled.Position.x, jointAfter.Position.x, 1e-4f);
                    Assert.AreEqual(sampled.Rotation, math.degrees(math.Euler(jointAfter.Rotation).z), 0.5f);

                    var appAfter = em.GetComponentData<SpritePartAppearanceState>(weapon);
                    Assert.AreNotEqual(appBefore.AppearanceIndex, appAfter.AppearanceIndex);
                    Assert.AreEqual(2f, appAfter.LogicalWorldSize.x, 1e-4f);
                    Assert.AreEqual(2f, appAfter.LogicalWorldSize.y, 1e-4f);
                    Assert.AreEqual(0.15f, appAfter.Pivot.x, 1e-4f);
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
        public void WeaponPatch_LeavesHeadAppearance()
        {
            var blob = BuildRigWithWeapons();
            var previous = World.DefaultGameObjectInjectionWorld;
            using var world = new World("Parts patch head");
            World.DefaultGameObjectInjectionWorld = world;
            try
            {
                var em = world.EntityManager;
                var created = SpritePartsEntityFactory.Create(em, blob, float3.zero);
                try
                {
                    Entity root = created.Root;
                    Assert.IsTrue(SpriteParts.TryGetSlot(em, root, "head", out Entity head));
                    Assert.IsTrue(SpriteParts.TryGetSlot(em, root, "weapon", out Entity weapon));
                    var headBefore = em.GetComponentData<SpritePartAppearanceState>(head);
                    var weaponBefore = em.GetComponentData<SpritePartAppearanceState>(weapon);

                    Assert.IsTrue(SpriteParts.ApplySkin(em, root, "weapon.spear"));

                    var headAfter = em.GetComponentData<SpritePartAppearanceState>(head);
                    var weaponAfter = em.GetComponentData<SpritePartAppearanceState>(weapon);
                    Assert.AreEqual(headBefore.AppearanceIndex, headAfter.AppearanceIndex);
                    Assert.AreEqual(headBefore.CellIndex, headAfter.CellIndex);
                    Assert.AreEqual(headBefore.LogicalWorldSize.x, headAfter.LogicalWorldSize.x, 1e-5f);
                    Assert.AreNotEqual(weaponBefore.AppearanceIndex, weaponAfter.AppearanceIndex);
                    Assert.AreEqual(2f, weaponAfter.LogicalWorldSize.x, 1e-4f);
                    Assert.AreEqual(em.GetComponentData<SpritePartsActiveSkin>(root).SkinIdHash,
                        SpritePartIdUtility.Hash("weapon.spear"));
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
        public void InvalidAppearance_FailsAtomically()
        {
            var blob = BuildRigWithWeapons();
            var previous = World.DefaultGameObjectInjectionWorld;
            using var world = new World("Parts atomic fail");
            World.DefaultGameObjectInjectionWorld = world;
            try
            {
                var em = world.EntityManager;
                var created = SpritePartsEntityFactory.Create(em, blob, float3.zero);
                try
                {
                    Entity root = created.Root;
                    Assert.IsTrue(SpriteParts.TryGetSlot(em, root, "weapon", out Entity weapon));
                    Assert.IsTrue(SpriteParts.TryGetSlot(em, root, "head", out Entity head));
                    var weaponBefore = em.GetComponentData<SpritePartAppearanceState>(weapon);
                    var headBefore = em.GetComponentData<SpritePartAppearanceState>(head);
                    var playerBefore = em.GetComponentData<SpritePartsPlayer>(root);

                    Assert.IsFalse(SpriteParts.ApplySkin(em, root, "weapon.bad"));
                    Assert.IsFalse(SpriteParts.ApplySkin(em, root, "skin.missing"));
                    Assert.IsFalse(SpriteParts.SetSlotAppearance(em, root, "weapon", "weapon.broken"));
                    Assert.IsFalse(SpriteParts.SetSlotAppearance(em, root, "weapon", "no.such.art"));

                    var weaponAfter = em.GetComponentData<SpritePartAppearanceState>(weapon);
                    var headAfter = em.GetComponentData<SpritePartAppearanceState>(head);
                    var playerAfter = em.GetComponentData<SpritePartsPlayer>(root);
                    Assert.AreEqual(weaponBefore.AppearanceIndex, weaponAfter.AppearanceIndex);
                    Assert.AreEqual(weaponBefore.LogicalWorldSize.x, weaponAfter.LogicalWorldSize.x, 1e-5f);
                    Assert.AreEqual(headBefore.AppearanceIndex, headAfter.AppearanceIndex);
                    Assert.AreEqual(playerBefore.TimeSeconds, playerAfter.TimeSeconds, 1e-6f);
                    Assert.AreEqual(playerBefore.ClipIndex, playerAfter.ClipIndex);
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
        public void GeometryAfterSwap_MixedSizePivot_JointUnchanged()
        {
            var blob = BuildRigWithWeapons();
            var previous = World.DefaultGameObjectInjectionWorld;
            using var world = new World("Parts geometry swap");
            World.DefaultGameObjectInjectionWorld = world;
            try
            {
                var em = world.EntityManager;
                var created = SpritePartsEntityFactory.Create(em, blob, float3.zero);
                try
                {
                    Entity root = created.Root;
                    Assert.IsTrue(SpriteParts.TryGetSlot(em, root, "weapon", out Entity weapon));
                    var joint = em.GetComponentData<LocalTransform>(weapon);

                    Assert.IsTrue(SpriteParts.SetSlotAppearance(em, root, "weapon", "sword.iron"));
                    var sword = em.GetComponentData<SpritePartAppearanceState>(weapon);
                    var swordFrame = em.GetComponentData<SpriteAnimFrame>(weapon);
                    Assert.AreEqual(1f, sword.LogicalWorldSize.x, 1e-4f);
                    Assert.AreEqual(0.2f, sword.Pivot.x, 1e-4f);
                    Assert.AreEqual(sword.FrameOffset.x, swordFrame.Offset.x, 1e-5f);
                    Assert.AreEqual(0f, swordFrame.Rotation, 1e-5f);

                    Assert.IsTrue(SpriteParts.SetSlotAppearance(em, root, "weapon", "spear.oak"));
                    var spear = em.GetComponentData<SpritePartAppearanceState>(weapon);
                    var spearFrame = em.GetComponentData<SpriteAnimFrame>(weapon);
                    Assert.AreEqual(2f, spear.LogicalWorldSize.x, 1e-4f);
                    Assert.AreEqual(0.15f, spear.Pivot.x, 1e-4f);
                    Assert.AreEqual(spear.FrameOffset.x, spearFrame.Offset.x, 1e-5f);

                    var jointAfter = em.GetComponentData<LocalTransform>(weapon);
                    Assert.AreEqual(joint.Position.x, jointAfter.Position.x, 1e-5f);
                    Assert.AreEqual(joint.Position.y, jointAfter.Position.y, 1e-5f);
                    Assert.AreEqual(12f, math.degrees(math.Euler(jointAfter.Rotation).z), 0.5f);

                    // SetSlotSheet with explicit metadata (different PPU art path).
                    var sheet = em.CreateEntity();
                    Assert.IsTrue(SpriteParts.SetSlotSheet(em, root, "weapon", sheet, cellIndex: 3,
                        logicalWorldSize: new float2(1.5f, 0.5f), pivot: new float2(0.1f, 0.2f)));
                    var adhoc = em.GetComponentData<SpritePartAppearanceState>(weapon);
                    Assert.AreEqual(-1, adhoc.AppearanceIndex);
                    Assert.AreEqual(1.5f, adhoc.LogicalWorldSize.x, 1e-4f);
                    Assert.AreEqual(0.1f, adhoc.Pivot.x, 1e-4f);
                    Assert.AreEqual(sheet, em.GetComponentData<SpriteSheetBinding>(weapon).Sheet);
                    Assert.AreEqual(joint.Position.x, em.GetComponentData<LocalTransform>(weapon).Position.x, 1e-5f);

                    Assert.IsFalse(SpriteParts.SetSlotSheet(em, root, "weapon", sheet, 0,
                        new float2(0f, 1f), new float2(0.5f, 0.5f)));
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
        public void ResetSkin_RestoresDefaults()
        {
            var blob = BuildRigWithWeapons();
            var previous = World.DefaultGameObjectInjectionWorld;
            using var world = new World("Parts reset skin");
            World.DefaultGameObjectInjectionWorld = world;
            try
            {
                var em = world.EntityManager;
                var created = SpritePartsEntityFactory.Create(em, blob, float3.zero);
                try
                {
                    Entity root = created.Root;
                    Assert.IsTrue(SpriteParts.ApplySkin(em, root, "weapon.spear"));
                    Assert.IsTrue(SpriteParts.ResetSkin(em, root));
                    Assert.IsTrue(SpriteParts.TryGetSlot(em, root, "weapon", out Entity weapon));
                    var app = em.GetComponentData<SpritePartAppearanceState>(weapon);
                    Assert.AreEqual(1f, app.LogicalWorldSize.x, 1e-4f);
                    Assert.AreEqual(0.2f, app.Pivot.x, 1e-4f);
                    Assert.AreEqual(0UL, em.GetComponentData<SpritePartsActiveSkin>(root).SkinIdHash);
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
        public void PlatformChecks_PartsCpuOnly_XyLayout_MaxParts()
        {
            Assert.AreEqual(32, SpritePartIdUtility.MaxParts);
            Assert.IsTrue(SpriteBatchSpawner.LayoutXy, "Parts require XY layout for this package baseline.");

            var blob = BuildRigWithWeapons();
            var previous = World.DefaultGameObjectInjectionWorld;
            using var world = new World("Parts platform");
            World.DefaultGameObjectInjectionWorld = world;
            try
            {
                var em = world.EntityManager;
                var created = SpritePartsEntityFactory.Create(em, blob, float3.zero);
                try
                {
                    Assert.IsFalse(SpriteGpuEligibility.IsGpuEligible(em, created.Root, out var reason));
                    Assert.That(reason.ToString(), Does.Contain("Parts"));
                    Assert.IsFalse(SpriteAnims.TryToGpu(em, created.Root));
                    Assert.IsTrue(SpriteAnims.Play(em, created.Root, "Walk", force: false, crossfadeSeconds: 0.1f),
                        "Parts supports crossfade requests while remaining CPU-pose driven.");
                    Assert.IsTrue(em.HasComponent<SpritePartsPlayer>(created.Root));
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
        public void FramePath_StillBuildsFrameBlob_NoPartsMix()
        {
            // Regression: frame baker path stays available; Parts AnimKind is separate.
            var profile = new SpriteSheetProfile { AnimKind = SpriteAnimKind.Frame };
            Assert.AreEqual(SpriteAnimKind.Frame, profile.AnimKind);

            var (setRef, player) = SpriteAnimSetBuilder.Build(Allocator.Temp, new[]
            {
                new SpriteAnimSetBuilder.ClipInput
                {
                    Name = "Idle",
                    FrameRate = 8f,
                    WrapMode = SpriteAnimWrap.Loop,
                    GlobalFrameIndices = new[] { 0, 1, 2, 3 },
                    OnCompleteClipIndex = -1,
                },
            });
            try
            {
                Assert.IsTrue(setRef.Set.IsCreated);
                Assert.Greater(setRef.Set.Value.Clips.Length, 0);
                Assert.AreEqual(0, player.ClipIndex);
            }
            finally
            {
                if (setRef.Set.IsCreated) setRef.Set.Dispose();
            }
        }
    }
}



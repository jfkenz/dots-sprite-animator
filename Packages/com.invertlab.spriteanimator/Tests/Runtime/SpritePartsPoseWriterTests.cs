using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePartsPoseWriterTests
    {
        BlobAssetReference<SpritePartsSetBlob> Build()
        {
            var appearances = new[]
            {
                new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = "walk.art", SheetTableIndex = 0, CellIndex = 0,
                    LogicalWorldSize = new float2(1f, 1f), Pivot = new float2(0.5f, 0.5f),
                    FrameOffset = float2.zero, FrameScale = new float2(1f, 1f),
                },
                new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = "aim.art", SheetTableIndex = 0, CellIndex = 1,
                    LogicalWorldSize = new float2(1f, 1f), Pivot = new float2(0.5f, 0.5f),
                    FrameOffset = float2.zero, FrameScale = new float2(1f, 1f),
                },
            };
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Body", SlotId = "body",
                    RestPosition = float2.zero, RestRotation = 0f, RestScale = new float2(1f, 1f),
                    DefaultAppearanceId = "walk.art", DrawRank = 0,
                },
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Weapon", SlotId = "weapon", ParentSlotId = "body",
                    RestPosition = new float2(1f, 0f), RestRotation = 0f, RestScale = new float2(1f, 1f),
                    DefaultAppearanceId = "walk.art", DrawRank = 1,
                },
            };
            var walkKeys = new[]
            {
                new SpritePartsSetBuilder.KeyInput
                {
                    Time = 0f, Position = float2.zero, Rotation = 0f, Scale = new float2(1f, 1f),
                    EaseMode = (byte)SpriteEaseMode.Linear, AppearanceId = "walk.art",
                },
                new SpritePartsSetBuilder.KeyInput
                {
                    Time = 1f, Position = new float2(4f, 0f), Rotation = 0f, Scale = new float2(1f, 1f),
                    EaseMode = (byte)SpriteEaseMode.Linear, AppearanceId = "walk.art",
                },
            };
            var aimKeys = new[]
            {
                new SpritePartsSetBuilder.KeyInput
                {
                    Time = 0f, Position = new float2(8f, 0f), Rotation = 90f, Scale = new float2(1f, 1f),
                    EaseMode = (byte)SpriteEaseMode.Linear, AppearanceId = "aim.art",
                },
            };
            var clips = new[]
            {
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "Walk", ClipId = "walk", Duration = 1f, SpeedMultiplier = 1f,
                    WrapMode = (byte)SpritePartsWrap.Loop,
                    Tracks = new[]
                    {
                        new SpritePartsSetBuilder.TrackInput { SlotId = "body", Keys = walkKeys },
                    },
                },
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "Aim", ClipId = "aim", Duration = 1f, SpeedMultiplier = 1f,
                    WrapMode = (byte)SpritePartsWrap.Once,
                    Tracks = new[]
                    {
                        new SpritePartsSetBuilder.TrackInput { SlotId = "body", Keys = aimKeys },
                    },
                },
            };
            return SpritePartsSetBuilder.Build(Allocator.Temp, slots, appearances, clips,
                System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
        }

        [Test]
        public void ReplaceWeightZeroRestoresClip()
        {
            var blob = Build();
            try
            {
                Evaluate(ref blob.Value, out var player, out var basePoses, out var finalLocal, out var sources,
                    new[]
                    {
                        Ov(1, 0, SpritePartsPoseMode.Replace, SpritePartsPoseChannel.Rotation, 0f, rot: 45f),
                    });
                try
                {
                    Assert.AreEqual(0f, basePoses[0].Rotation, 1e-4f);
                    Assert.AreEqual(0f, finalLocal[0].Rotation, 1e-4f);
                    Assert.AreEqual((byte)SpritePartPoseWriterKind.Clip, sources[0].Writer);
                    _ = player;
                }
                finally
                {
                    basePoses.Dispose();
                    finalLocal.Dispose();
                    sources.Dispose();
                }
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void AdditiveRecoilOffsetsWeapon()
        {
            var blob = Build();
            try
            {
                Evaluate(ref blob.Value, out _, out var basePoses, out var finalLocal, out var sources,
                    new[]
                    {
                        Ov(2, 1, SpritePartsPoseMode.Add, SpritePartsPoseChannel.Position, 1f,
                            pos: new float2(0f, 0.25f)),
                    });
                try
                {
                    Assert.AreEqual(basePoses[1].Position.x, finalLocal[1].Position.x, 1e-4f);
                    Assert.AreEqual(basePoses[1].Position.y + 0.25f, finalLocal[1].Position.y, 1e-4f);
                    Assert.AreEqual((byte)SpritePartPoseWriterKind.Add, sources[1].Writer);
                }
                finally
                {
                    basePoses.Dispose();
                    finalLocal.Dispose();
                    sources.Dispose();
                }
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void LookAtReplacesWeaponRotation()
        {
            var blob = Build();
            try
            {
                Evaluate(ref blob.Value, out _, out var basePoses, out var finalLocal, out var sources,
                    new[]
                    {
                        Ov(3, 1, SpritePartsPoseMode.LookAt, SpritePartsPoseChannel.Rotation, 1f,
                            target: new float2(1f, 10f), space: SpritePartsPoseSpace.Character),
                    });
                try
                {
                    Assert.AreEqual(0f, basePoses[1].Rotation, 1e-3f);
                    Assert.AreEqual(90f, finalLocal[1].Rotation, 0.5f);
                    Assert.AreEqual((byte)SpritePartPoseWriterKind.Aim, sources[1].Writer);
                }
                finally
                {
                    basePoses.Dispose();
                    finalLocal.Dispose();
                    sources.Dispose();
                }
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void CrossfadeBlendsBodyPosition()
        {
            var blob = Build();
            try
            {
                var player = SpritePartsPoseWriter.DefaultPlayer(1);
                player.PreviousClipIndex = 0;
                player.PreviousTimeSeconds = 0f;
                player.BlendDuration = 1f;
                player.BlendElapsed = 0.5f;
                Evaluate(ref blob.Value, player, System.Array.Empty<SpritePartsPoseOverride>(),
                    out var finalLocal, out var sources);
                try
                {
                    Assert.AreEqual(4f, finalLocal[0].Position.x, 1e-3f);
                }
                finally
                {
                    finalLocal.Dispose();
                    sources.Dispose();
                }
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void SpriteSwitchHoldUsesOutgoingAppearance()
        {
            var blob = Build();
            try
            {
                var player = SpritePartsPoseWriter.DefaultPlayer(1);
                player.PreviousClipIndex = 0;
                player.BlendDuration = 1f;
                player.BlendElapsed = 0.1f;
                player.SpriteSwitch = (byte)SpritePartsSpriteSwitch.HoldUntilFadeEnd;
                Assert.AreEqual(0, SpritePartsPoseWriter.ResolveAppearanceClip(player));
                player.SpriteSwitch = (byte)SpritePartsSpriteSwitch.UseIncomingImmediately;
                Assert.AreEqual(1, SpritePartsPoseWriter.ResolveAppearanceClip(player));
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void PlayCrossfadeAndOverrides_DoNotWriteRoot()
        {
            var blob = Build();
            using var world = new World("pose writer ecs");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, new float3(9f, 7f, 0f), playing: false);
            try
            {
                Entity root = created.Root;
                Assert.IsTrue(SpriteAnims.Play(em, root, "Aim", force: false, crossfadeSeconds: 0.2f));
                var player = em.GetComponentData<SpritePartsPlayer>(root);
                Assert.AreEqual(1, player.ClipIndex);
                Assert.AreEqual(0, player.PreviousClipIndex);
                Assert.AreEqual(0.2f, player.BlendDuration, 1e-4f);

                SpriteParts.SetOverride(em, root, Ov(4, 1, SpritePartsPoseMode.Add,
                    SpritePartsPoseChannel.Position, 1f, pos: new float2(0f, 1f)));
                SpriteParts.BindSocket(em, root, "muzzle", "weapon", float2.zero);
                SpritePartsPoseWriter.Apply(em, root);

                var rootLt = em.GetComponentData<LocalTransform>(root);
                Assert.AreEqual(9f, rootLt.Position.x, 1e-4f);
                Assert.AreEqual(7f, rootLt.Position.y, 1e-4f);

                var weapon = created.Parts[1];
                var wlt = em.GetComponentData<LocalTransform>(weapon);
                Assert.AreEqual(1f, wlt.Scale, 1e-4f);
                Assert.AreEqual(1f, wlt.Position.y, 1e-3f);
                Assert.IsTrue(em.HasComponent<PostTransformMatrix>(weapon));

                Assert.IsTrue(SpriteParts.TryGetSocketWorld(em, root, "muzzle", out var muzzle, out _));
                Assert.Greater(muzzle.y, 0.5f);

                em.AddComponentData(weapon, new SpritePartPhysicsOwned());
                var before = em.GetComponentData<LocalTransform>(weapon);
                before.Position = new float3(3f, 3f, 0f);
                em.SetComponentData(weapon, before);
                SpritePartsPoseWriter.Apply(em, root);
                var after = em.GetComponentData<LocalTransform>(weapon);
                Assert.AreEqual(3f, after.Position.x, 1e-4f);
                Assert.AreEqual(1, em.GetBuffer<SpritePartFinalPose>(root)[1].PhysicsSkipped);
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        [Test]
        public void PauseFreezesOutgoingCrossfade()
        {
            var blob = Build();
            try
            {
                var player = SpritePartsPoseWriter.DefaultPlayer(1);
                player.PreviousClipIndex = 0;
                player.PreviousTimeSeconds = 0.1f;
                player.BlendDuration = 1f;
                player.BlendElapsed = 0.25f;
                SpritePartsPoseWriter.TickClocks(ref player, ref blob.Value, 0.1f);
                Assert.Greater(player.BlendElapsed, 0.3f);
                Assert.Greater(player.PreviousTimeSeconds, 0.1f);

                player.Playing = 0;
                player.Completed = 0;
                float blend = player.BlendElapsed;
                float prevT = player.PreviousTimeSeconds;
                SpritePartsPoseWriter.TickClocks(ref player, ref blob.Value, 0.2f);
                Assert.AreEqual(blend, player.BlendElapsed, 1e-5f);
                Assert.AreEqual(prevT, player.PreviousTimeSeconds, 1e-5f);
                Assert.AreEqual(0, player.PreviousClipIndex);
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void OnceIncomingCompleteStillAdvancesCrossfade()
        {
            var blob = Build();
            try
            {
                var player = SpritePartsPoseWriter.DefaultPlayer(1);
                player.TimeSeconds = 1f;
                player.Playing = 0;
                player.Completed = 1;
                player.PreviousClipIndex = 0;
                player.PreviousTimeSeconds = 0.2f;
                player.BlendDuration = 1f;
                player.BlendElapsed = 0.4f;
                SpritePartsPoseWriter.TickClocks(ref player, ref blob.Value, 0.3f);
                Assert.AreEqual(0.7f, player.BlendElapsed, 1e-4f);
                Assert.AreEqual(0, player.PreviousClipIndex);
                SpritePartsPoseWriter.TickClocks(ref player, ref blob.Value, 0.4f);
                Assert.AreEqual(-1, player.PreviousClipIndex);
                Assert.AreEqual(0f, player.BlendElapsed, 1e-5f);
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void CharacterReplaceMovesWeaponInRootSpace()
        {
            var blob = Build();
            try
            {
                Evaluate(ref blob.Value, out _, out var basePoses, out var finalLocal, out var sources,
                    new[]
                    {
                        Ov(7, 1, SpritePartsPoseMode.Replace, SpritePartsPoseChannel.Position, 1f,
                            pos: new float2(2f, 3f), space: SpritePartsPoseSpace.Character),
                    });
                try
                {
                    Assert.AreEqual(2f, finalLocal[1].Position.x, 1e-3f);
                    Assert.AreEqual(3f, finalLocal[1].Position.y, 1e-3f);
                    Assert.AreEqual((byte)SpritePartPoseWriterKind.Replace, sources[1].Writer);
                }
                finally
                {
                    basePoses.Dispose();
                    finalLocal.Dispose();
                    sources.Dispose();
                }
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void AnimationLayerBlendsMaskedClip()
        {
            var blob = Build();
            try
            {
                var player = SpritePartsPoseWriter.DefaultPlayer(0, playing: false);
                var layers = new[]
                {
                    new SpritePartsAnimLayer { ClipIndex = 1, Weight = 0.5f, SlotMask = 1u },
                };
                Evaluate(ref blob.Value, player, System.Array.Empty<SpritePartsPoseOverride>(), layers,
                    out var finalLocal, out var sources);
                try
                {
                    Assert.AreEqual(4f, finalLocal[0].Position.x, 1e-3f);
                    Assert.AreEqual((byte)SpritePartPoseWriterKind.Layer, sources[0].Writer);
                    Assert.AreEqual((byte)SpritePartPoseWriterKind.Clip, sources[1].Writer);
                }
                finally
                {
                    finalLocal.Dispose();
                    sources.Dispose();
                }
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void MotionRecoilPresetAddsLocalKick()
        {
            var blob = Build();
            try
            {
                Evaluate(ref blob.Value, out _, out var basePoses, out var finalLocal, out var sources,
                    new[] { SpritePartsMotion.Recoil(1, new float2(0f, 0.25f)) });
                try
                {
                    Assert.AreEqual(basePoses[1].Position.y + 0.25f, finalLocal[1].Position.y, 1e-4f);
                }
                finally
                {
                    basePoses.Dispose();
                    finalLocal.Dispose();
                    sources.Dispose();
                }
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void SocketFollowsMovingRootThisFrame()
        {
            var blob = Build();
            using var world = new World("socket moving root");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, new float3(9f, 7f, 0f), playing: false);
            try
            {
                Entity root = created.Root;
                Assert.IsTrue(SpriteParts.BindSocket(em, root, "muzzle", "weapon", float2.zero));
                SpritePartsPoseWriter.Apply(em, root);
                Assert.IsTrue(SpriteParts.TryGetSocketWorld(em, root, "muzzle", out var before, out _));
                Assert.AreEqual(10f, before.x, 1e-3f);
                Assert.AreEqual(7f, before.y, 1e-3f);

                var lt = em.GetComponentData<LocalTransform>(root);
                lt.Position = new float3(19f, 7f, 0f);
                em.SetComponentData(root, lt);
                SpritePartsPoseWriter.Apply(em, root);
                Assert.IsTrue(SpriteParts.TryGetSocketWorld(em, root, "muzzle", out var after, out _));
                Assert.AreEqual(20f, after.x, 1e-3f);
                Assert.AreEqual(7f, after.y, 1e-3f);
                Assert.AreEqual(9f, em.GetComponentData<LocalToWorld>(root).Value.c3.x, 1e-3f);
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        [Test]
        public void SocketFollowsRotatedScaledParent()
        {
            var blob = Build();
            using var world = new World("socket rotated parent");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, new float3(9f, 7f, 0f), playing: false);
            try
            {
                Entity root = created.Root;
                SpriteParts.SetOverride(em, root, Ov(8, 0, SpritePartsPoseMode.Replace,
                    SpritePartsPoseChannel.Rotation | SpritePartsPoseChannel.Scale, 1f,
                    rot: 90f, scale: new float2(2f, 2f)));
                Assert.IsTrue(SpriteParts.BindSocket(em, root, "muzzle", "weapon", float2.zero));
                SpritePartsPoseWriter.Apply(em, root);
                Assert.IsTrue(SpriteParts.TryGetSocketWorld(em, root, "muzzle", out var muzzle, out var rot));
                Assert.AreEqual(9f, muzzle.x, 1e-3f);
                Assert.AreEqual(9f, muzzle.y, 1e-3f);
                Assert.AreEqual(90f, rot, 0.5f);
                Assert.AreEqual(9f, em.GetComponentData<LocalTransform>(root).Position.x, 1e-4f);
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        [Test]
        public void PhysicsOwnedSocketUsesParentHierarchy()
        {
            var blob = Build();
            using var world = new World("physics socket hierarchy");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, new float3(9f, 7f, 0f), playing: false);
            try
            {
                Entity root = created.Root;
                var weapon = created.Parts[1];
                Assert.IsTrue(SpriteParts.BindSocket(em, root, "muzzle", "weapon", float2.zero));
                em.AddComponentData(weapon, new SpritePartPhysicsOwned());
                var phys = em.GetComponentData<LocalTransform>(weapon);
                phys.Position = new float3(3f, 3f, 0f);
                em.SetComponentData(weapon, phys);
                SpritePartsPoseWriter.Apply(em, root);

                Assert.AreEqual(3f, em.GetComponentData<LocalTransform>(weapon).Position.x, 1e-4f);
                Assert.AreEqual(3f, em.GetComponentData<LocalTransform>(weapon).Position.y, 1e-4f);
                Assert.AreEqual(1, em.GetBuffer<SpritePartFinalPose>(root)[1].PhysicsSkipped);
                Assert.IsTrue(SpriteParts.TryGetSocketWorld(em, root, "muzzle", out var muzzle, out _));
                Assert.AreEqual(12f, muzzle.x, 1e-3f);
                Assert.AreEqual(10f, muzzle.y, 1e-3f);
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        [Test]
        public void PhysicsOwnedParentDrivesAnimatedChildSocket()
        {
            var blob = Build();
            using var world = new World("physics parent animated child");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, new float3(9f, 7f, 0f), playing: false);
            try
            {
                Entity root = created.Root;
                var body = created.Parts[0];
                var weapon = created.Parts[1];
                Assert.IsTrue(SpriteParts.BindSocket(em, root, "muzzle", "weapon", float2.zero));
                em.AddComponentData(body, new SpritePartPhysicsOwned());
                var phys = em.GetComponentData<LocalTransform>(body);
                phys.Position = new float3(5f, 1f, 0f);
                em.SetComponentData(body, phys);
                SpritePartsPoseWriter.Apply(em, root);

                Assert.AreEqual(5f, em.GetComponentData<LocalTransform>(body).Position.x, 1e-4f);
                Assert.AreEqual(1f, em.GetComponentData<LocalTransform>(weapon).Position.x, 1e-3f);
                Assert.AreEqual(0f, em.GetComponentData<LocalTransform>(weapon).Position.y, 1e-3f);
                Assert.AreEqual(1, em.GetBuffer<SpritePartFinalPose>(root)[0].PhysicsSkipped);
                Assert.AreEqual(0, em.GetBuffer<SpritePartFinalPose>(root)[1].PhysicsSkipped);
                Assert.IsTrue(SpriteParts.TryGetSocketWorld(em, root, "muzzle", out var muzzle, out _));
                Assert.AreEqual(15f, muzzle.x, 1e-3f);
                Assert.AreEqual(8f, muzzle.y, 1e-3f);
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        [Test]
        public void RotatedHitboxUsesAuthoredLocalRotation()
        {
            var blob = Build();
            using var world = new World("rotated hitbox");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, new float3(9f, 7f, 0f), playing: false);
            try
            {
                Entity root = created.Root;
                Assert.IsTrue(SpriteParts.BindHitbox(em, root, "weapon", float2.zero, new float2(2f, 0.2f), 0f));
                SpritePartsPoseWriter.Apply(em, root);
                float axisY = em.GetBuffer<SpritePartHitboxWorld>(root)[0].Extents.y;
                em.GetBuffer<SpritePartHitboxBinding>(root).Clear();
                Assert.IsTrue(SpriteParts.BindHitbox(em, root, "weapon", float2.zero, new float2(2f, 0.2f), 45f));
                SpritePartsPoseWriter.Apply(em, root);
                float rotatedY = em.GetBuffer<SpritePartHitboxWorld>(root)[0].Extents.y;
                Assert.Greater(rotatedY, axisY + 0.2f);
                Assert.AreEqual(9f, em.GetComponentData<LocalTransform>(root).Position.x, 1e-4f);
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        [TestCase(false, 1f, 135f)]
        [TestCase(true, 1f, 45f)]
        [TestCase(false, 2f, 116.56505f)]
        [TestCase(true, 2f, 63.43495f)]
        public void SocketDirectionFollowsFacingAndNonUniformScale(bool flipX, float scaleX, float expectedAngle)
        {
            var blob = Build();
            using var world = new World("socket transformed direction");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, new float3(9f, 7f, 0f), playing: false);
            try
            {
                Entity root = created.Root;
                SpriteParts.SetFacing(em, root, flipX);
                SpriteParts.SetOverride(em, root, Ov(8, 0, SpritePartsPoseMode.Replace,
                    SpritePartsPoseChannel.Rotation | SpritePartsPoseChannel.Scale, 1f,
                    rot: 90f, scale: new float2(scaleX, 1f)));
                Assert.IsTrue(SpriteParts.BindSocket(em, root, "muzzle", "weapon", float2.zero, 45f));
                SpritePartsPoseWriter.Apply(em, root);
                Assert.IsTrue(SpriteParts.TryGetSocketWorld(em, root, "muzzle", out _, out var angle));
                Assert.AreEqual(0f, UnityEngine.Mathf.DeltaAngle(expectedAngle, angle), 1e-3f);
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        [Test]
        public void SocketUsesCurrentRootParentTransform()
        {
            var blob = Build();
            using var world = new World("socket root parent");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, new float3(9f, 7f, 0f), playing: false);
            try
            {
                Entity parent = em.CreateEntity();
                em.AddComponentData(parent, LocalTransform.FromPositionRotationScale(
                    new float3(100f, 20f, 0f), quaternion.RotateZ(math.radians(90f)), 1f));
                em.AddComponentData(parent, new LocalToWorld { Value = float4x4.identity });
                em.AddComponentData(created.Root, new Parent { Value = parent });
                Assert.IsTrue(SpriteParts.BindSocket(em, created.Root, "muzzle", "weapon", float2.zero));
                SpritePartsPoseWriter.Apply(em, created.Root);
                Assert.IsTrue(SpriteParts.TryGetSocketWorld(em, created.Root, "muzzle", out var position, out var angle));
                Assert.AreEqual(93f, position.x, 1e-3f);
                Assert.AreEqual(30f, position.y, 1e-3f);
                Assert.AreEqual(90f, angle, 1e-3f);
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        [Test]
        public void PauseAndResumeCompletedClipPreserveUnfinishedFade()
        {
            var blob = Build();
            using var world = new World("pause completed crossfade");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, float3.zero);
            try
            {
                Assert.IsTrue(SpriteParts.Play(em, created.Root, 1, crossfadeSeconds: 2f));
                var system = world.GetOrCreateSystem<SpritePartsPlayerSystem>();
                world.SetTime(new Unity.Core.TimeData(1.1, 1.1f));
                system.Update(world.Unmanaged);
                var before = em.GetComponentData<SpritePartsPlayer>(created.Root);
                Assert.IsTrue(em.HasComponent<SpritePartsCompleted>(created.Root));
                Assert.AreEqual(1, before.Completed);
                SpriteParts.Pause(em, created.Root);
                world.SetTime(new Unity.Core.TimeData(1.3, 0.2f));
                system.Update(world.Unmanaged);
                var paused = em.GetComponentData<SpritePartsPlayer>(created.Root);
                Assert.AreEqual(before.BlendElapsed, paused.BlendElapsed, 1e-5f);
                Assert.AreEqual(before.PreviousTimeSeconds, paused.PreviousTimeSeconds, 1e-5f);
                SpriteParts.Resume(em, created.Root);
                system.Update(world.Unmanaged);
                var resumed = em.GetComponentData<SpritePartsPlayer>(created.Root);
                Assert.AreEqual(before.BlendElapsed + 0.2f, resumed.BlendElapsed, 1e-5f);
                Assert.AreEqual(1f, resumed.TimeSeconds, 1e-5f);
                Assert.AreEqual(0, resumed.Playing);
                Assert.IsTrue(em.HasComponent<SpritePartsCompleted>(created.Root));
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        [Test]
        public void RestartDuringFadeShowsCurrentClipStartImmediately()
        {
            var blob = Build();
            using var world = new World("restart crossfade");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, float3.zero);
            try
            {
                Assert.IsTrue(SpriteParts.Play(em, created.Root, 1, crossfadeSeconds: 2f));
                SpriteParts.Pause(em, created.Root);
                SpriteParts.Restart(em, created.Root);
                var player = em.GetComponentData<SpritePartsPlayer>(created.Root);
                Assert.AreEqual(-1, player.PreviousClipIndex);
                Assert.AreEqual(0f, player.BlendDuration);
                Assert.AreEqual(0f, player.TimeSeconds);
                Assert.IsFalse(SpritePartsPoseWriter.IsPaused(player));
                Assert.AreEqual(8f, em.GetComponentData<LocalTransform>(created.Parts[0]).Position.x, 1e-4f);
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        [TestCase(false, 4f, 4f)]
        [TestCase(true, 42f, 28f)]
        public void PhysicsOwnedParentExportsAttachmentsFromActualHierarchy(bool reparent, float x, float y)
        {
            var blob = Build();
            using var world = new World("physics detached hierarchy");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, new float3(9f, 7f, 0f));
            try
            {
                Entity body = created.Parts[0];
                em.AddComponentData(body, new SpritePartPhysicsOwned());
                em.SetComponentData(body, LocalTransform.FromPosition(new float3(3f, 4f, 5f)));
                em.RemoveComponent<Parent>(body);
                if (reparent)
                {
                    Entity parent = em.CreateEntity();
                    em.AddComponentData(parent, LocalTransform.FromPositionRotationScale(
                        new float3(50f, 20f, 0f), quaternion.RotateZ(math.radians(90f)), 2f));
                    em.AddComponentData(parent, new LocalToWorld { Value = float4x4.identity });
                    em.AddComponentData(body, new Parent { Value = parent });
                }
                SpriteParts.SetFacing(em, created.Root, true);
                Assert.IsTrue(SpriteParts.BindSocket(em, created.Root, "muzzle", "weapon", float2.zero));
                Assert.IsTrue(SpriteParts.BindHitbox(em, created.Root, "weapon", float2.zero, new float2(2f, 1f)));
                SpritePartsPoseWriter.Apply(em, created.Root);
                Assert.IsTrue(SpriteParts.TryGetSocketWorld(em, created.Root, "muzzle", out var position, out _));
                Assert.AreEqual(x, position.x, 1e-3f);
                Assert.AreEqual(y, position.y, 1e-3f);
                var hitbox = em.GetBuffer<SpritePartHitboxWorld>(created.Root)[0];
                Assert.AreEqual(x, hitbox.Center.x, 1e-3f);
                Assert.AreEqual(y, hitbox.Center.y, 1e-3f);
                Assert.AreEqual(1f, hitbox.Extents.x, 1e-3f);
                Assert.AreEqual(reparent ? 2f : 0.5f, hitbox.Extents.y, 1e-3f);
                Assert.AreEqual(5f, em.GetComponentData<LocalTransform>(body).Position.z);
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        [Test]
        public void RenderDepthDoesNotModifyPhysicsTransform()
        {
            var blob = Build();
            using var world = new World("physics render depth");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, float3.zero);
            try
            {
                Entity body = created.Parts[0];
                em.AddComponentData(body, new SpritePartPhysicsOwned());
                var physical = LocalTransform.FromPositionRotationScale(
                    new float3(3f, 4f, 5f), quaternion.RotateZ(0.4f), 2f);
                em.SetComponentData(body, physical);
                var system = world.GetOrCreateSystem<SpritePartsRenderDepthSystem>();
                system.Update(world.Unmanaged);
                Assert.AreEqual(physical, em.GetComponentData<LocalTransform>(body));
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        [Test]
        public void ReleasingPhysicsOwnershipResumesAnimationWithoutConflictDiagnostic()
        {
            var blob = Build();
            using var world = new World("physics release ownership");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, float3.zero);
            try
            {
                Entity body = created.Parts[0];
                em.AddComponentData(body, new SpritePartPhysicsOwned());
                em.SetComponentData(body, LocalTransform.FromPosition(new float3(3f, 4f, 0f)));
                SpritePartsPoseWriter.Apply(em, created.Root);
                em.RemoveComponent<SpritePartPhysicsOwned>(body);
                SpritePartsPoseWriter.Apply(em, created.Root);
                Assert.AreEqual(0f, em.GetComponentData<LocalTransform>(body).Position.x, 1e-4f);
                Assert.AreEqual(0, em.GetBuffer<SpritePartFinalPose>(created.Root)[0].PhysicsSkipped);
                Assert.AreEqual(0, em.GetComponentData<SpritePartsPoseDiagnostics>(created.Root).Flags
                    & SpritePartsPoseDiagnostics.GameplayWroteTransform);
                // A subsequent unauthorized edit must still be diagnosed.
                em.SetComponentData(body, LocalTransform.FromPosition(new float3(3f, 4f, 0f)));
                SpritePartsPoseWriter.Apply(em, created.Root);
                Assert.AreNotEqual(0, em.GetComponentData<SpritePartsPoseDiagnostics>(created.Root).Flags
                    & SpritePartsPoseDiagnostics.GameplayWroteTransform);
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        [Test]
        public void PlayByNameWithMissingBlobReturnsFalse()
        {
            using var world = new World("missing parts profile");
            var em = world.EntityManager;
            Entity root = em.CreateEntity(typeof(SpritePartsPlayer), typeof(SpritePartsSetRef));
            Assert.IsFalse(SpriteParts.Play(em, root, "Walk"));
        }

        [TestCase(false, 2f, 1f)]
        [TestCase(true, 2f, 1f)]
        [TestCase(false, -2f, 1f)]
        [TestCase(true, -2f, -1f)]
        public void WorldAimPointsSocketAtTargetUnderScaledMirroredParent(bool flip, float parentScaleX, float partScaleX)
        {
            var blob = Build();
            using var world = new World("world aim transformed parent");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, new float3(9f, 7f, 0f));
            try
            {
                SpriteParts.SetFacing(em, created.Root, flip);
                SpriteParts.SetOverride(em, created.Root, Ov(8, 0, SpritePartsPoseMode.Replace,
                    SpritePartsPoseChannel.Rotation | SpritePartsPoseChannel.Scale, 1f,
                    rot: 35f, scale: new float2(parentScaleX, 1.3f)));
                SpriteParts.SetOverride(em, created.Root, Ov(9, 1, SpritePartsPoseMode.Replace,
                    SpritePartsPoseChannel.Scale, 1f, scale: new float2(partScaleX, 1f)));
                float2 target = new float2(12f, 11f);
                SpriteParts.SetOverride(em, created.Root, Ov(10, 1, SpritePartsPoseMode.LookAt,
                    SpritePartsPoseChannel.Rotation, 1f, target: target, space: SpritePartsPoseSpace.World));
                Assert.IsTrue(SpriteParts.BindSocket(em, created.Root, "origin", "weapon", float2.zero));
                Assert.IsTrue(SpriteParts.BindSocket(em, created.Root, "tip", "weapon", new float2(1f, 0f)));
                SpritePartsPoseWriter.Apply(em, created.Root);
                Assert.IsTrue(SpriteParts.TryGetSocketWorld(em, created.Root, "origin", out var origin, out _));
                Assert.IsTrue(SpriteParts.TryGetSocketWorld(em, created.Root, "tip", out var tip, out _));
                Assert.Greater(math.dot(math.normalize(target - origin), math.normalize(tip - origin)), 0.99999f);
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        [TestCase(false, 2f, 1f)]
        [TestCase(true, -2f, -1f)]
        public void WorldRotationOverrideUsesRenderedDirection(bool flip, float parentScaleX, float partScaleX)
        {
            var blob = Build();
            using var world = new World("world rotation override");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, new float3(9f, 7f, 0f));
            try
            {
                SpriteParts.SetFacing(em, created.Root, flip);
                SpriteParts.SetOverride(em, created.Root, Ov(8, 0, SpritePartsPoseMode.Replace,
                    SpritePartsPoseChannel.Rotation | SpritePartsPoseChannel.Scale, 1f,
                    rot: 35f, scale: new float2(parentScaleX, 1.3f)));
                SpriteParts.SetOverride(em, created.Root, Ov(9, 1, SpritePartsPoseMode.Replace,
                    SpritePartsPoseChannel.Scale, 1f, scale: new float2(partScaleX, 1f)));
                SpriteParts.SetOverride(em, created.Root, Ov(10, 1, SpritePartsPoseMode.Replace,
                    SpritePartsPoseChannel.Rotation, 1f, rot: -20f, space: SpritePartsPoseSpace.World));
                Assert.IsTrue(SpriteParts.BindSocket(em, created.Root, "muzzle", "weapon", float2.zero));
                SpritePartsPoseWriter.Apply(em, created.Root);
                Assert.IsTrue(SpriteParts.TryGetSocketWorld(em, created.Root, "muzzle", out _, out var angle));
                Assert.AreEqual(0f, UnityEngine.Mathf.DeltaAngle(-20f, angle), 1e-3f);
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        [Test]
        public void ChildAimUsesParentAimFromSameUpdate()
        {
            var blob = Build();
            using var world = new World("parent child aim");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, new float3(9f, 7f, 0f));
            try
            {
                SpriteParts.SetOverride(em, created.Root, Ov(8, 0, SpritePartsPoseMode.LookAt,
                    SpritePartsPoseChannel.Rotation, 1f, target: new float2(9f, 17f), space: SpritePartsPoseSpace.World));
                SpriteParts.SetOverride(em, created.Root, Ov(9, 1, SpritePartsPoseMode.LookAt,
                    SpritePartsPoseChannel.Rotation, 1f, target: new float2(13f, 8f), space: SpritePartsPoseSpace.World));
                Assert.IsTrue(SpriteParts.BindSocket(em, created.Root, "muzzle", "weapon", float2.zero));
                SpritePartsPoseWriter.Apply(em, created.Root);
                Assert.IsTrue(SpriteParts.TryGetSocketWorld(em, created.Root, "muzzle", out var origin, out var angle));
                Assert.AreEqual(9f, origin.x, 1e-3f);
                Assert.AreEqual(8f, origin.y, 1e-3f);
                Assert.AreEqual(0f, UnityEngine.Mathf.DeltaAngle(0f, angle), 1e-3f);
            }
            finally
            {
                created.Parts.Dispose();
                em.DestroyEntity(em.UniversalQuery);
                blob.Dispose();
            }
        }

        static SpritePartsPoseOverride Ov(
            int id, int slot, SpritePartsPoseMode mode, SpritePartsPoseChannel channels, float weight,
            float2 pos = default, float rot = 0f, float2 scale = default, float2 target = default,
            SpritePartsPoseSpace space = SpritePartsPoseSpace.Local)
        {
            if (scale.x == 0f && scale.y == 0f)
                scale = new float2(1f, 1f);
            return new SpritePartsPoseOverride
            {
                Id = id,
                SlotIndex = slot,
                Mode = (byte)mode,
                Channels = (byte)channels,
                Weight = weight,
                Position = pos,
                Rotation = rot,
                Scale = scale,
                Target = target,
                Space = (byte)space,
                Priority = id,
            };
        }

        static void Evaluate(
            ref SpritePartsSetBlob set,
            out SpritePartsPlayer player,
            out NativeArray<SpritePartsSampler.Pose> basePoses,
            out NativeArray<SpritePartsSampler.Pose> finalLocal,
            out NativeArray<SpritePartPoseSource> sources,
            SpritePartsPoseOverride[] overrides)
        {
            player = SpritePartsPoseWriter.DefaultPlayer(0, playing: false);
            Evaluate(ref set, player, overrides, out finalLocal, out sources, out basePoses);
        }

        static void Evaluate(
            ref SpritePartsSetBlob set,
            SpritePartsPlayer player,
            SpritePartsPoseOverride[] overrides,
            out NativeArray<SpritePartsSampler.Pose> finalLocal,
            out NativeArray<SpritePartPoseSource> sources)
        {
            Evaluate(ref set, player, overrides, null, out finalLocal, out sources, out var basePoses);
            basePoses.Dispose();
        }

        static void Evaluate(
            ref SpritePartsSetBlob set,
            SpritePartsPlayer player,
            SpritePartsPoseOverride[] overrides,
            SpritePartsAnimLayer[] layers,
            out NativeArray<SpritePartsSampler.Pose> finalLocal,
            out NativeArray<SpritePartPoseSource> sources)
        {
            Evaluate(ref set, player, overrides, layers, out finalLocal, out sources, out var basePoses);
            basePoses.Dispose();
        }

        static void Evaluate(
            ref SpritePartsSetBlob set,
            SpritePartsPlayer player,
            SpritePartsPoseOverride[] overrides,
            out NativeArray<SpritePartsSampler.Pose> finalLocal,
            out NativeArray<SpritePartPoseSource> sources,
            out NativeArray<SpritePartsSampler.Pose> basePoses)
            => Evaluate(ref set, player, overrides, null, out finalLocal, out sources, out basePoses);

        static void Evaluate(
            ref SpritePartsSetBlob set,
            SpritePartsPlayer player,
            SpritePartsPoseOverride[] overrides,
            SpritePartsAnimLayer[] layers,
            out NativeArray<SpritePartsSampler.Pose> finalLocal,
            out NativeArray<SpritePartPoseSource> sources,
            out NativeArray<SpritePartsSampler.Pose> basePoses)
        {
            int n = set.Slots.Length;
            basePoses = new NativeArray<SpritePartsSampler.Pose>(n, Allocator.Temp);
            finalLocal = new NativeArray<SpritePartsSampler.Pose>(n, Allocator.Temp);
            var localToRoot = new NativeArray<float4x4>(n, Allocator.Temp);
            sources = new NativeArray<SpritePartPoseSource>(n, Allocator.Temp);
            var apps = new NativeArray<int>(n, Allocator.Temp);
            var ov = new NativeArray<SpritePartsPoseOverride>(overrides.Length, Allocator.Temp);
            for (int i = 0; i < overrides.Length; i++)
                ov[i] = overrides[i];
            NativeArray<SpritePartsAnimLayer> layerArr = default;
            if (layers != null)
            {
                layerArr = new NativeArray<SpritePartsAnimLayer>(layers.Length, Allocator.Temp);
                for (int i = 0; i < layers.Length; i++)
                    layerArr[i] = layers[i];
            }
            SpritePartsPoseWriter.Evaluate(ref set, player, ov, layerArr, basePoses, finalLocal, localToRoot, sources,
                apps, float4x4.identity, false, false);
            ov.Dispose();
            if (layerArr.IsCreated)
                layerArr.Dispose();
            localToRoot.Dispose();
            apps.Dispose();
        }
    }
}

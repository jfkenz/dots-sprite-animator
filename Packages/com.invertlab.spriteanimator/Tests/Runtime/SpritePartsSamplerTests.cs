using NUnit.Framework;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePartsSamplerTests
    {
        BlobAssetReference<SpritePartsSetBlob> Build(
            SpritePartsSetBuilder.TrackInput[] tracks = null,
            byte wrap = (byte)SpritePartsWrap.Loop,
            float duration = 1f)
        {
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Body",
                    SlotId = "body",
                    RestPosition = new float2(1f, 2f),
                    RestRotation = 10f,
                    RestScale = new float2(1f, 1f),
                    DrawRank = 0,
                },
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Hand",
                    SlotId = "hand",
                    ParentSlotId = "body",
                    RestPosition = new float2(3f, 0f),
                    RestRotation = 0f,
                    RestScale = new float2(1f, 1f),
                    DrawRank = 1,
                },
            };
            var clips = new[]
            {
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "Walk",
                    ClipId = "walk",
                    Duration = duration,
                    SpeedMultiplier = 1f,
                    WrapMode = wrap,
                    Tracks = tracks ?? System.Array.Empty<SpritePartsSetBuilder.TrackInput>(),
                },
            };
            return SpritePartsSetBuilder.Build(Allocator.Temp, slots,
                System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(), clips,
                System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
        }

        [Test]
        public void MissingTrackUsesRestPose()
        {
            var blob = Build();
            try
            {
                SpritePartsSampler.SampleSlot(ref blob.Value, 0, 0, 0.5f, out var pose);
                Assert.AreEqual(1f, pose.Position.x, 1e-5f);
                Assert.AreEqual(2f, pose.Position.y, 1e-5f);
                Assert.AreEqual(10f, pose.Rotation, 1e-5f);
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void OneKeyHoldsPose()
        {
            var tracks = new[]
            {
                new SpritePartsSetBuilder.TrackInput
                {
                    SlotId = "body",
                    Keys = new[]
                    {
                        new SpritePartsSetBuilder.KeyInput
                        {
                            Time = 0.25f,
                            Position = new float2(9f, 8f),
                            Rotation = 45f,
                            Scale = new float2(2f, 2f),
                            EaseMode = (byte)SpriteEaseMode.Linear,
                        },
                    },
                },
            };
            var blob = Build(tracks);
            try
            {
                SpritePartsSampler.SampleSlot(ref blob.Value, 0, 0, 0f, out var a);
                SpritePartsSampler.SampleSlot(ref blob.Value, 0, 0, 1f, out var b);
                Assert.AreEqual(9f, a.Position.x, 1e-5f);
                Assert.AreEqual(9f, b.Position.x, 1e-5f);
                Assert.AreEqual(45f, a.Rotation, 1e-5f);
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void LinearAndSmoothStepAtQuarter()
        {
            Assert.AreEqual(0.25f, SpriteEase.Evaluate(SpriteEaseMode.Linear, 0.25f), 1e-6f);
            Assert.AreEqual(0.15625f, SpriteEase.Evaluate(SpriteEaseMode.SmoothStep, 0.25f), 1e-6f);

            var linear = Build(new[]
            {
                new SpritePartsSetBuilder.TrackInput
                {
                    SlotId = "body",
                    Keys = new[]
                    {
                        new SpritePartsSetBuilder.KeyInput
                        {
                            Time = 0f, Position = float2.zero, Scale = new float2(1f, 1f),
                            EaseMode = (byte)SpriteEaseMode.Linear,
                        },
                        new SpritePartsSetBuilder.KeyInput
                        {
                            Time = 1f, Position = new float2(1f, 0f), Scale = new float2(1f, 1f),
                            EaseMode = (byte)SpriteEaseMode.Linear,
                        },
                    },
                },
            });
            var smooth = Build(new[]
            {
                new SpritePartsSetBuilder.TrackInput
                {
                    SlotId = "body",
                    Keys = new[]
                    {
                        new SpritePartsSetBuilder.KeyInput
                        {
                            Time = 0f, Position = float2.zero, Scale = new float2(1f, 1f),
                            EaseMode = (byte)SpriteEaseMode.SmoothStep,
                        },
                        new SpritePartsSetBuilder.KeyInput
                        {
                            Time = 1f, Position = new float2(1f, 0f), Scale = new float2(1f, 1f),
                            EaseMode = (byte)SpriteEaseMode.Linear,
                        },
                    },
                },
            });
            try
            {
                SpritePartsSampler.SampleSlot(ref linear.Value, 0, 0, 0.25f, out var lp);
                SpritePartsSampler.SampleSlot(ref smooth.Value, 0, 0, 0.25f, out var sp);
                Assert.AreEqual(0.25f, lp.Position.x, 1e-5f);
                Assert.AreEqual(0.15625f, sp.Position.x, 1e-5f);
            }
            finally
            {
                linear.Dispose();
                smooth.Dispose();
            }
        }

        [Test]
        public void AngleWrapUsesShortestPath()
        {
            var blob = Build(new[]
            {
                new SpritePartsSetBuilder.TrackInput
                {
                    SlotId = "body",
                    Keys = new[]
                    {
                        new SpritePartsSetBuilder.KeyInput
                        {
                            Time = 0f, Rotation = 10f, Scale = new float2(1f, 1f),
                            EaseMode = (byte)SpriteEaseMode.Linear,
                        },
                        new SpritePartsSetBuilder.KeyInput
                        {
                            Time = 1f, Rotation = 350f, Scale = new float2(1f, 1f),
                            EaseMode = (byte)SpriteEaseMode.Linear,
                        },
                    },
                },
            });
            try
            {
                SpritePartsSampler.SampleSlot(ref blob.Value, 0, 0, 0.5f, out var pose);
                // Shortest from 10 -> 350 is -20, midpoint = 0.
                Assert.AreEqual(0f, pose.Rotation, 1e-4f);
                Assert.AreEqual(-180f, SpritePartsSampler.LerpAngleShortest(0f, 180f, 1f), 1e-4f);
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void LoopAndOnceEndpoints()
        {
            var loop = Build(wrap: (byte)SpritePartsWrap.Loop, duration: 1f);
            var once = Build(wrap: (byte)SpritePartsWrap.Once, duration: 1f);
            try
            {
                Assert.AreEqual(0.25f, SpritePartsSampler.WrapTime(1.25f, 1f, (byte)SpritePartsWrap.Loop), 1e-5f);
                Assert.AreEqual(0.75f, SpritePartsSampler.WrapTime(-0.25f, 1f, (byte)SpritePartsWrap.Loop), 1e-5f);
                Assert.AreEqual(1f, SpritePartsSampler.WrapTime(1.25f, 1f, (byte)SpritePartsWrap.Once), 1e-5f);
                Assert.AreEqual(0f, SpritePartsSampler.WrapTime(-0.25f, 1f, (byte)SpritePartsWrap.Once), 1e-5f);
            }
            finally
            {
                loop.Dispose();
                once.Dispose();
            }
        }

        [Test]
        public void SampleAllFillsDensePosesWithoutManagedAllocHotPath()
        {
            var blob = Build();
            var poses = new NativeArray<SpritePartsSampler.Pose>(2, Allocator.Temp);
            try
            {
                SpritePartsSampler.SampleAll(ref blob.Value, 0, 0.1f, poses);
                Assert.AreEqual(1f, poses[0].Position.x, 1e-5f);
                Assert.AreEqual(3f, poses[1].Position.x, 1e-5f);
            }
            finally
            {
                poses.Dispose();
                blob.Dispose();
            }
        }
    }
}

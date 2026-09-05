using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;



namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteSocketApiTests
    {
        [Test]
        public void CatalogMigrationCreatesStableUniqueIds()
        {
            var profile = new SpriteSheetProfile
            {
                SocketCatalog = new SpriteSocketCatalog
                {
                    Items = new List<SpriteSocketCatalogItem>
                    {
                        new() { SocketName = "Head Slot" },
                        new() { SocketName = "Head Slot" },
                    },
                },
            };



            profile.EnsureSocketCatalog();



            Assert.AreEqual("head.slot", profile.SocketCatalog.Items[0].SocketId);
            Assert.AreEqual("head.slot.2", profile.SocketCatalog.Items[1].SocketId);
            string stableId = profile.SocketCatalog.Items[0].SocketId;
            profile.SocketCatalog.Items[0].SocketName = "Helmet";
            profile.EnsureSocketCatalog();
            Assert.AreEqual(stableId, profile.SocketCatalog.Items[0].SocketId);
        }



        [Test]
        public void IndependentTracksMigrateToOneMasterTimeline()
        {
            var profile = new SpriteSheetProfile
            {
                IndependentTimelineInitialized = false,
                IndependentMotionFrameRate = 10f,
                SocketMotions = new List<SpriteSocketMotionTrack>
                {
                    new() { SocketName = "Orb A", Duration = 2f, Loop = true },
                    new() { SocketName = "Orb B", Duration = 0.5f, Loop = false },
                },
            };



            profile.EnsureSocketMotions();



            Assert.AreEqual(21, profile.IndependentMotionFrameCount);
            Assert.AreEqual(2f, profile.IndependentMotionDuration, 0.0001f);
            Assert.AreEqual(profile.IndependentMotionDuration,
                profile.SocketMotions[0].Duration, 0.0001f);
            Assert.AreEqual(profile.IndependentMotionDuration,
                profile.SocketMotions[1].Duration, 0.0001f);
            Assert.IsTrue(profile.SocketMotions[0].Loop);
            Assert.IsTrue(profile.SocketMotions[1].Loop);
        }



        [Test]
        public void IndependentDrawLayerHoldsUntilNextKey()
        {
            var track = new SpriteSocketMotionTrack
            {
                Loop = true,
                Keys = new List<SpriteSocketMotionKey>
                {
                    new() { NormalizedTime = 0.2f, DrawLayer = SpriteSocketKeys.DrawBehind },
                    new() { NormalizedTime = 0.8f, DrawLayer = SpriteSocketKeys.DrawFront },
                },
            };



            Assert.AreEqual(SpriteSocketKeys.DrawFront,
                SpriteSocketKeys.ResolveIndependentDrawLayer(track, 0.1f));
            Assert.AreEqual(SpriteSocketKeys.DrawBehind,
                SpriteSocketKeys.ResolveIndependentDrawLayer(track, 0.2f));
            Assert.AreEqual(SpriteSocketKeys.DrawBehind,
                SpriteSocketKeys.ResolveIndependentDrawLayer(track, 0.5f));
            Assert.AreEqual(SpriteSocketKeys.DrawFront,
                SpriteSocketKeys.ResolveIndependentDrawLayer(track, 0.8f));
            Assert.IsTrue(SpriteSocketKeys.IsIndependentDrawnBehind(track, 0.4f, false));
            Assert.IsFalse(SpriteSocketKeys.IsIndependentDrawnBehind(track, 0.9f, true));



            track.Loop = false;
            Assert.AreEqual(SpriteSocketKeys.DrawUnset,
                SpriteSocketKeys.ResolveIndependentDrawLayer(track, 0.1f));
            Assert.IsTrue(SpriteSocketKeys.IsIndependentDrawnBehind(track, 0.1f, true));
        }



        [Test]
        public void TriggerCrossingsIncludeEveryHighSpeedLoop()
        {
            int count = SpriteSocketTriggerUtility.CountCrossings(
                0.1f, 3.6f, 1f, 0.5f, true, out int firstSequence);



            Assert.AreEqual(4, count);
            Assert.AreEqual(0, firstSequence);
            Assert.AreEqual(0, SpriteSocketTriggerUtility.CountCrossings(
                0.5f, 0.6f, 1f, 0.5f, false, out _));
        }



        [Test]
        public void IndependentKeyStepConvertsSecondsAndFrames()
        {
            Assert.AreEqual(0.3f,
                SpriteSocketMotionTimeUtility.ResolveStepSeconds(
                    false, 0.1f, 12f, 3),
                0.0001f);
            Assert.AreEqual(0.25f,
                SpriteSocketMotionTimeUtility.ResolveStepSeconds(
                    true, 0.1f, 12f, 3),
                0.0001f);
        }



        [Test]
        public void NaturalEasePresetsKeepStableEndpoints()
        {
            foreach (SpriteEaseMode mode in
                     System.Enum.GetValues(typeof(SpriteEaseMode)))
            {
                Assert.AreEqual(0f,
                    SpriteEase.Evaluate(mode, 0f, true), 0.0001f, mode.ToString());
                Assert.AreEqual(1f,
                    SpriteEase.Evaluate(mode, 1f, true), 0.0001f, mode.ToString());
                for (int i = 0; i <= 20; i++)
                {
                    float value = SpriteEase.Evaluate(mode, i / 20f);
                    Assert.IsFalse(float.IsNaN(value), mode.ToString());
                    Assert.That(value, Is.InRange(0f, 1f), mode.ToString());
                }
            }
            Assert.AreEqual(0.5f,
                SpriteEase.Evaluate(SpriteEaseMode.EaseInOut, 0.5f), 0.0001f);
        }



        [Test]
        public void NoneMotionOptionsDisableEasePathAndRotation()
        {
            Assert.AreEqual(0.4f,
                SpriteEase.Evaluate(SpriteEaseMode.None, 0.4f), 0.0001f);



            var p1 = new float2(0f, 0f);
            var p2 = new float2(4f, 2f);
            Assert.AreEqual(p1, SpriteSocketMotionInterpolation.Position(
                (byte)SpriteSocketPathMode.None,
                float2.zero, p1, p2, p2, 0.5f));
            Assert.AreEqual(p2, SpriteSocketMotionInterpolation.Position(
                (byte)SpriteSocketPathMode.None,
                float2.zero, p1, p2, p2, 1f));
            Assert.AreEqual(12f, SpriteSocketMotionInterpolation.Rotation(
                (byte)SpriteSocketRotationMode.None,
                12f, 90f, 0, 0f, new float2(1f, 0f), 0.5f), 0.0001f);
        }



        [Test]
        public void OvershootPolicyClampsOrPreservesBackEase()
        {
            float preserved = SpriteEase.Evaluate(
                SpriteEaseMode.BackOut, 0.7f, true);
            float clamped = SpriteEase.Evaluate(
                SpriteEaseMode.BackOut, 0.7f, false);
            Assert.Greater(preserved, 1f);
            Assert.AreEqual(1f, clamped, 0.0001f);
        }



        [Test]
        public void LinearAndSmoothSocketPathsUseDifferentSpatialInterpolation()
        {
            var p0 = new float2(0f, 0f);
            var p1 = new float2(0f, 0f);
            var p2 = new float2(1f, 1f);
            var p3 = new float2(3f, 0f);



            float2 linear = SpriteSocketMotionInterpolation.Position(
                (byte)SpriteSocketPathMode.Linear, p0, p1, p2, p3, 0.5f);
            float2 smooth = SpriteSocketMotionInterpolation.Position(
                (byte)SpriteSocketPathMode.SmoothPath, p0, p1, p2, p3, 0.5f);



            Assert.AreEqual(new float2(0.5f, 0.5f), linear);
            Assert.AreNotEqual(linear, smooth);
        }



        [Test]
        public void EverySocketPathHasStableEndpointsAndFiniteDerivative()
        {
            var p0 = new float2(-1f, 0f);
            var p1 = new float2(0f, 0f);
            var p2 = new float2(2f, 1f);
            var p3 = new float2(3f, 0f);
            foreach (SpriteSocketPathMode mode in
                     System.Enum.GetValues(typeof(SpriteSocketPathMode)))
            {
                float2 start = SpriteSocketMotionInterpolation.Position(
                    (byte)mode, p0, p1, p2, p3,
                    new float2(1f, 2f), new float2(-1f, 1f),
                    2f, 0, 0f);
                float2 end = SpriteSocketMotionInterpolation.Position(
                    (byte)mode, p0, p1, p2, p3,
                    new float2(1f, 2f), new float2(-1f, 1f),
                    2f, 0, 1f);
                float2 derivative = SpriteSocketMotionInterpolation.Derivative(
                    (byte)mode, p0, p1, p2, p3,
                    new float2(1f, 2f), new float2(-1f, 1f),
                    2f, 0, 0.5f);
                Assert.AreEqual(p1, start, mode.ToString());
                Assert.AreEqual(p2, end, mode.ToString());
                Assert.IsTrue(math.all(math.isfinite(derivative)), mode.ToString());
            }
        }



        [Test]
        public void RotationModesRespectDirectionTurnsAndPathFacing()
        {
            float clockwise = SpriteSocketMotionInterpolation.Rotation(
                (byte)SpriteSocketRotationMode.Clockwise,
                10f, 350f, 0, 0f, new float2(1f, 0f), 0.5f);
            float counterClockwise = SpriteSocketMotionInterpolation.Rotation(
                (byte)SpriteSocketRotationMode.CounterClockwise,
                10f, 350f, 0, 0f, new float2(1f, 0f), 0.5f);
            float turns = SpriteSocketMotionInterpolation.Rotation(
                (byte)SpriteSocketRotationMode.ContinuousTurns,
                0f, 0f, 2, 0f, new float2(1f, 0f), 0.5f);
            float facing = SpriteSocketMotionInterpolation.Rotation(
                (byte)SpriteSocketRotationMode.FacePath,
                0f, 0f, 0, 15f, new float2(0f, 1f), 0.5f);
            Assert.Less(clockwise, 10f);
            Assert.Greater(counterClockwise, 10f);
            Assert.AreEqual(360f, turns, 0.0001f);
            Assert.AreEqual(105f, facing, 0.0001f);
        }



        [Test]
        public void CustomEaseCacheMatchesBurstSampleEvaluation()
        {
            var key = new SpriteSocketMotionKey
            {
                UseCustomEase = true,
                CustomEaseCurve = UnityEngine.AnimationCurve.EaseInOut(
                    0f, 0f, 1f, 1f),
            };
            key.RebuildCustomEaseSamples();
            var samplesA = new float4(
                key.CustomEaseSamplesA.x, key.CustomEaseSamplesA.y,
                key.CustomEaseSamplesA.z, key.CustomEaseSamplesA.w);
            var samplesB = new float4(
                key.CustomEaseSamplesB.x, key.CustomEaseSamplesB.y,
                key.CustomEaseSamplesB.z, key.CustomEaseSamplesB.w);



            for (int i = 0; i <= 20; i++)
            {
                float t = i / 20f;
                float managed = key.EvaluateCustomEase(t);
                float burst = SpriteEase.EvaluateSamples(samplesA, samplesB, t);
                Assert.AreEqual(managed, burst, 0.0001f);
                Assert.That(burst, Is.InRange(0f, 1f));
            }
        }



        [Test]
        public void CustomEaseCacheClampsOvershootAndReverseMotion()
        {
            var key = new SpriteSocketMotionKey
            {
                UseCustomEase = true,
                CustomEaseCurve = new UnityEngine.AnimationCurve(
                    new UnityEngine.Keyframe(0f, 0f),
                    new UnityEngine.Keyframe(0.3f, 2f),
                    new UnityEngine.Keyframe(0.6f, -1f),
                    new UnityEngine.Keyframe(1f, 1f)),
            };



            key.RebuildCustomEaseSamples();



            float previous = -1f;
            for (int i = 0; i <= 20; i++)
            {
                float value = key.EvaluateCustomEase(i / 20f);
                Assert.That(value, Is.InRange(0f, 1f));
                Assert.GreaterOrEqual(value, previous);
                previous = value;
            }
            Assert.AreEqual(0f, key.EvaluateCustomEase(0f), 0.0001f);
            Assert.AreEqual(1f, key.EvaluateCustomEase(1f), 0.0001f);
        }



        [Test]
        public void CustomEaseCanPreserveOvershootWhenEnabled()
        {
            var key = new SpriteSocketMotionKey
            {
                UseCustomEase = true,
                AllowOvershoot = true,
                CustomEaseCurve = new UnityEngine.AnimationCurve(
                    new UnityEngine.Keyframe(0f, 0f),
                    new UnityEngine.Keyframe(0.5f, 1.5f),
                    new UnityEngine.Keyframe(0.75f, 0.5f),
                    new UnityEngine.Keyframe(1f, 1f)),
            };
            key.RebuildCustomEaseSamples();



            Assert.Greater(key.EvaluateCustomEase(0.5f), 1f);
            Assert.Less(key.EvaluateCustomEase(0.75f),
                key.EvaluateCustomEase(0.5f));
        }



        [Test]
        public void InvalidSocketPathMigratesToSmoothPath()
        {
            var track = new SpriteSocketMotionTrack
            {
                Keys = new List<SpriteSocketMotionKey>
                {
                    new() { PathMode = 99, RotationMode = 99 },
                },
            };



            track.Normalize(1);



            Assert.AreEqual((byte)SpriteSocketPathMode.SmoothPath,
                track.Keys[0].PathMode);
            Assert.AreEqual((byte)SpriteSocketRotationMode.Shortest,
                track.Keys[0].RotationMode);
        }



        [Test]
        public void TrackStyleDefaultsSurviveNormalizeAndRejectInvalidValues()
        {
            var track = new SpriteSocketMotionTrack
            {
                DefaultEaseMode = (byte)SpriteEaseMode.BounceOut,
                DefaultPathMode = (byte)SpriteSocketPathMode.Arc,
                DefaultRotationMode = (byte)SpriteSocketRotationMode.FacePath,
                AnchorSpace = (byte)SpriteSocketAnchorSpace.World,
            };



            track.Normalize(1);



            Assert.AreEqual((byte)SpriteEaseMode.BounceOut, track.DefaultEaseMode);
            Assert.AreEqual((byte)SpriteSocketPathMode.Arc, track.DefaultPathMode);
            Assert.AreEqual((byte)SpriteSocketRotationMode.FacePath,
                track.DefaultRotationMode);
            Assert.AreEqual((byte)SpriteSocketAnchorSpace.World, track.AnchorSpace);



            track.DefaultEaseMode = 99;
            track.DefaultPathMode = 99;
            track.DefaultRotationMode = 99;
            track.AnchorSpace = 99;
            track.Normalize(1);



            Assert.AreEqual((byte)SpriteEaseMode.SmoothStep, track.DefaultEaseMode);
            Assert.AreEqual((byte)SpriteSocketPathMode.SmoothPath, track.DefaultPathMode);
            Assert.AreEqual((byte)SpriteSocketRotationMode.Shortest,
                track.DefaultRotationMode);
            Assert.AreEqual((byte)SpriteSocketAnchorSpace.CharacterPivot,
                track.AnchorSpace);
        }



        [Test]
        public void ExtendingIndependentTimelinePreservesAbsoluteTimes()
        {
            var profile = new SpriteSheetProfile
            {
                IndependentTimelineInitialized = true,
                IndependentTimelineUsesSeconds = true,
                IndependentMotionDurationSeconds = 1f,
                SocketMotions = new List<SpriteSocketMotionTrack>
                {
                    new()
                    {
                        SocketName = "Orb",
                        Duration = 1f,
                        Keys = new List<SpriteSocketMotionKey>
                        {
                            new() { NormalizedTime = 0.5f },
                        },
                        Triggers = new List<SpriteSocketTriggerDef>
                        {
                            new() { NormalizedTime = 0.25f, EventId = 1 },
                        },
                    },
                },
            };



            Assert.IsTrue(profile.ExtendIndependentMotionDurationPreserveTimes(2.5f));



            var track = profile.SocketMotions[0];
            Assert.AreEqual(2.5f, profile.IndependentMotionDuration, 0.0001f);
            Assert.AreEqual(0.5f,
                track.Keys[0].NormalizedTime * profile.IndependentMotionDuration,
                0.0001f);
            Assert.AreEqual(0.25f,
                track.Triggers[0].NormalizedTime * profile.IndependentMotionDuration,
                0.0001f);
        }



        [Test]
        public void FrameAttachedSocketMovesBetweenFrameKeys()
        {
            var clip = new SpriteClipDef
            {
                FrameRate = 1f,
                Frames = new[] { 0, 1 },
                Sockets = new List<FrameSocketDef>
                {
                    new()
                    {
                        Name = "Helmet",
                        FrameIndex = 0,
                        LocalPosition = new UnityEngine.Vector2(0f, 0f),
                        LocalScale = UnityEngine.Vector2.one,
                    },
                    new()
                    {
                        Name = "Helmet",
                        FrameIndex = 1,
                        LocalPosition = new UnityEngine.Vector2(1f, 1f),
                        LocalScale = UnityEngine.Vector2.one,
                    },
                },
            };
            clip.EnsureFrameData();



            Assert.IsTrue(SpriteSocketKeys.TrySampleAtTime(
                clip.Sockets, "Helmet", clip, 0.5f, false, false,
                out var position, out _, out _, out _));
            Assert.AreEqual(new UnityEngine.Vector2(0.5f, 0.5f), position);
        }



        [Test]
        public void BuilderBakesSocketIdsAndTriggers()
        {
            var clips = new[]
            {
                new SpriteAnimSetBuilder.ClipInput
                {
                    Name = "Idle",
                    Loop = true,
                    FrameRate = 8f,
                    GlobalFrameIndices = new[] { 0 },
                    FrameSockets = new[]
                    {
                        new SpriteAnimSetBuilder.ClipInput.FrameSocketInput
                        {
                            FrameIndex = 0,
                            Name = "Muzzle",
                            SocketId = "combat.muzzle",
                            LocalScale = new float2(1f),
                        },
                    },
                },
            };
            var motions = new[]
            {
                new SpriteAnimSetBuilder.SocketMotionInput
                {
                    Name = "Orb",
                    SocketId = "effect.orb",
                    Duration = 1f,
                    Speed = 1f,
                    Loop = true,
                    AnchorSpace = (byte)SpriteSocketAnchorSpace.World,
                    Keys = new[]
                    {
                        new SpriteAnimSetBuilder.SocketMotionInput.SocketMotionPointInput
                        {
                            NormalizedTime = 0f,
                            LocalScale = new float2(1f),
                            EaseMode = (byte)SpriteEaseMode.EaseOut,
                            PathMode = (byte)SpriteSocketPathMode.Linear,
                            UseCustomEase = 1,
                            CustomEaseSamplesA = new float4(0f, 0.05f, 0.15f, 0.3f),
                            CustomEaseSamplesB = new float4(0.5f, 0.72f, 0.9f, 1f),
                            AllowOvershoot = 1,
                            InTangent = new float2(-1f, 2f),
                            OutTangent = new float2(3f, 4f),
                            ArcBulge = 5f,
                            ArcClockwise = 1,
                            RotationMode = (byte)SpriteSocketRotationMode.ContinuousTurns,
                            RotationTurns = 2,
                            FacingAngleOffset = 15f,
                        },
                    },
                    Triggers = new[]
                    {
                        new SpriteAnimSetBuilder.SocketMotionInput.SocketTriggerInput
                        {
                            NormalizedTime = 0.5f,
                            EventId = 7,
                        },
                    },
                },
            };



            var (setRef, _) = SpriteAnimSetBuilder.Build(Allocator.Temp, clips, motions);
            Assert.AreEqual(SpriteSockets.Hash("combat.muzzle"),
                setRef.Set.Value.Clips[0].FrameSockets[0].SocketIdHash);
            Assert.AreEqual(SpriteSockets.Hash("effect.orb"),
                setRef.Set.Value.SocketMotions[0].SocketIdHash);
            Assert.AreEqual((byte)SpriteSocketAnchorSpace.World,
                setRef.Set.Value.SocketMotions[0].AnchorSpace);
            Assert.AreEqual((byte)SpriteEaseMode.EaseOut,
                setRef.Set.Value.SocketMotions[0].Keys[0].EaseMode);
            Assert.AreEqual((byte)SpriteSocketPathMode.Linear,
                setRef.Set.Value.SocketMotions[0].Keys[0].PathMode);
            Assert.AreEqual(1,
                setRef.Set.Value.SocketMotions[0].Keys[0].UseCustomEase);
            Assert.AreEqual(0.72f,
                setRef.Set.Value.SocketMotions[0].Keys[0].CustomEaseSamplesB.y,
                0.0001f);
            Assert.AreEqual(1,
                setRef.Set.Value.SocketMotions[0].Keys[0].AllowOvershoot);
            Assert.AreEqual(new float2(3f, 4f),
                setRef.Set.Value.SocketMotions[0].Keys[0].OutTangent);
            Assert.AreEqual(5f,
                setRef.Set.Value.SocketMotions[0].Keys[0].ArcBulge);
            Assert.AreEqual((byte)SpriteSocketRotationMode.ContinuousTurns,
                setRef.Set.Value.SocketMotions[0].Keys[0].RotationMode);
            Assert.AreEqual(2,
                setRef.Set.Value.SocketMotions[0].Keys[0].RotationTurns);
            Assert.AreEqual(7, setRef.Set.Value.SocketMotions[0].Triggers[0].EventId);
            setRef.Set.Dispose();
        }



        [Test]
        public void WorldAnchorCompensatesForCharacterTranslationAndRotation()
        {
            float2 position = new(2f, 0f);
            float angle = 15f;
            float4x4 movedCharacter = float4x4.TRS(
                new float3(13f, 5f, 0f),
                quaternion.RotateZ(math.radians(90f)),
                new float3(1f));



            SpriteSocketMotionSystem.ResolveWorldAnchor(
                new float3(10f, 5f, 0f), 0f, movedCharacter, ref position, ref angle);



            Assert.AreEqual(0f, position.x, 0.0001f);
            Assert.AreEqual(1f, position.y, 0.0001f);
            Assert.AreEqual(-75f, angle, 0.0001f);
        }



        [Test]
        public void LookupResolvesLocalAndWorldPose()
        {
            using var world = new World("Sprite socket API test");
            Entity entity = world.EntityManager.CreateEntity();
            var buffer = world.EntityManager.AddBuffer<SpriteSocketBuffer>(entity);
            string id = "combat.muzzle";
            buffer.Add(new SpriteSocketBuffer
            {
                Name = new FixedString64Bytes("Muzzle"),
                SocketId = new FixedString64Bytes(id),
                SocketIdHash = SpriteSockets.Hash(id),
                LocalPosition = new float2(2f, 3f),
                LocalScale = new float2(1f),
            });



            Assert.IsTrue(SpriteSockets.TryGetPose(buffer, SpriteSockets.Hash(id), out var local));
            Assert.AreEqual(new float2(2f, 3f), local.LocalPosition);



            var localToWorld = new LocalToWorld
            {
                Value = float4x4.TRS(new float3(10f, 20f, 0f), quaternion.identity,
                    new float3(4f)),
            };
            Assert.IsTrue(SpriteSockets.TryGetWorldPose(
                buffer, SpriteSockets.Hash(id), localToWorld, out var worldPose));
            Assert.That(worldPose.Position.x, Is.EqualTo(12f).Within(0.0001f));
            Assert.That(worldPose.Position.y, Is.EqualTo(23f).Within(0.0001f));
        }



        [Test]
        public void PlayDoesNotResetIndependentSocketClock()
        {
            var clips = new[]
            {
                new SpriteAnimSetBuilder.ClipInput
                {
                    Name = "Idle",
                    Loop = true,
                    FrameRate = 8f,
                    GlobalFrameIndices = new[] { 0 },
                },
                new SpriteAnimSetBuilder.ClipInput
                {
                    Name = "Run",
                    Loop = true,
                    FrameRate = 8f,
                    GlobalFrameIndices = new[] { 0 },
                },
            };
            var (setRef, player) = SpriteAnimSetBuilder.Build(Allocator.Persistent, clips);
            using var world = new World("Sprite socket play test");
            Entity entity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(entity, setRef);
            world.EntityManager.AddComponentData(entity, player);
            world.EntityManager.AddComponentData(entity, new SpriteAnimFrame());
            world.EntityManager.AddBuffer<SpriteSocketBuffer>(entity);
            world.EntityManager.AddComponentData(entity,
                new SpriteSocketMotionPlayer { Time = 3.25f, Speed = 2f, Playing = 1 });



            Assert.IsTrue(SpriteAnims.Play(world.EntityManager, entity, 1));
            var motionPlayer =
                world.EntityManager.GetComponentData<SpriteSocketMotionPlayer>(entity);
            Assert.AreEqual(3.25f, motionPlayer.Time);
            Assert.AreEqual(2f, motionPlayer.Speed);
            setRef.Set.Dispose();
        }



        
        [Test]
        public void MirrorAroundCellCenter_WithoutTexture_UsesUnitCellHeight()
        {
            var sheet = new SpriteSheetDef
            {
                Columns = 1,
                Rows = 1,
                PixelsPerUnit = 100f,
            };
            var p = new UnityEngine.Vector2(0.25f, 0.25f);
            var mirroredX = SpriteSocketWorld.MirrorAroundCellCenter(p, sheet, true, false);
            Assert.AreEqual(-0.25f, mirroredX.x, 0.0001f);
            Assert.AreEqual(0.25f, mirroredX.y, 0.0001f);

            var mirroredY = SpriteSocketWorld.MirrorAroundCellCenter(p, sheet, false, true);
            Assert.AreEqual(0.25f, mirroredY.x, 0.0001f);
            Assert.AreEqual(0.75f, mirroredY.y, 0.0001f);

            var both = SpriteSocketWorld.MirrorAroundCellCenter(p, sheet, true, true);
            Assert.AreEqual(-0.25f, both.x, 0.0001f);
            Assert.AreEqual(0.75f, both.y, 0.0001f);
        }

        [Test]
        public void PixelsFromPivotToMeshLocal_WithoutTexture_ReturnsPixelsOverPpu()
        {
            var sheet = new SpriteSheetDef
            {
                Columns = 1,
                Rows = 1,
                PixelsPerUnit = 50f,
            };
            // no texture -> unit cell: the (0.5, 0.5) pivot sits 0.5 cells
            // above the bottom-center mesh origin, so its offset contributes
            // 0.5 world units on y before the pixel offset
            var local = SpriteSocketWorld.PixelsFromPivotToMeshLocal(
                sheet, new UnityEngine.Vector2(0.5f, 0.5f), new UnityEngine.Vector2(10f, -20f));
            Assert.AreEqual(10f / 50f, local.x, 0.0001f);
            Assert.AreEqual(0.5f + -20f / 50f, local.y, 0.0001f);
        }




    }
}


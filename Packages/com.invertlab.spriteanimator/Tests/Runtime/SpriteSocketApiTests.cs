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
                    Keys = new[]
                    {
                        new SpriteAnimSetBuilder.SocketMotionInput.SocketMotionPointInput
                        {
                            NormalizedTime = 0f,
                            LocalScale = new float2(1f),
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
            Assert.AreEqual(7, setRef.Set.Value.SocketMotions[0].Triggers[0].EventId);
            setRef.Set.Dispose();
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
                new SpriteSocketMotionPlayer { Time = 3.25f, Playing = 1 });

            Assert.IsTrue(SpriteAnims.Play(world.EntityManager, entity, 1));
            Assert.AreEqual(3.25f,
                world.EntityManager.GetComponentData<SpriteSocketMotionPlayer>(entity).Time);
            setRef.Set.Dispose();
        }
    }
}

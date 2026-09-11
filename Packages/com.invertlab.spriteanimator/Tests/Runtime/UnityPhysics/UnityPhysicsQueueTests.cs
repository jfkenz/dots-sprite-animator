using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class UnityPhysicsQueueTests
    {
        static readonly Aabb Bounds = new Aabb { Min = new float3(-2), Max = new float3(2) };

        sealed class Fixture : System.IDisposable
        {
            public readonly World World = new World("Overlap queue regression");
            public readonly UnityPhysicsOverlapBridge Bridge;
            public readonly Entity Body;
            public readonly Entity Singleton;
            PhysicsWorld _physics;
            BlobAssetReference<Collider> _collider;

            public Fixture()
            {
                Body = World.EntityManager.CreateEntity();
                _collider = SphereCollider.Create(new SphereGeometry { Center = float3.zero, Radius = 1 });
                _physics = new PhysicsWorld(1, 0, 0);
                var bodies = _physics.Bodies;
                bodies[0] = new RigidBody
                {
                    Entity = Body, Collider = _collider, Scale = 1,
                    WorldFromBody = RigidTransform.identity,
                };
                _physics.UpdateIndexMaps();
                _physics.CollisionWorld.BuildBroadphase(ref _physics, 0, float3.zero);
                Singleton = World.EntityManager.CreateEntity();
                World.EntityManager.AddComponentData(Singleton, new PhysicsWorldSingleton { PhysicsWorld = _physics });
                Bridge = World.GetOrCreateSystemManaged<UnityPhysicsOverlapBridge>();
            }

            public void Dispose()
            {
                World.Dispose();
                _physics.Dispose();
                _collider.Dispose();
            }
        }

        [Test]
        public void EveryRequestKeepsItsAttackAndDamageAndCallbacksDeferNewWork()
        {
            using var f = new Fixture();
            var hits = new List<(int, int)>();
            f.Bridge.Hit += (entity, attack, damage) =>
            {
                Assert.AreEqual(f.Body, entity);
                hits.Add((attack, damage));
                if (attack == 1) f.Bridge.Enqueue(Bounds, 3, 30);
            };
            f.Bridge.Enqueue(Bounds, 1, 10);
            UnityPhysicsOverlapBridge.Queue(f.World, Bounds, 2, 20);
            f.Bridge.Update();
            CollectionAssert.AreEqual(new[] { (1, 10), (2, 20) }, hits);
            Assert.AreEqual(1, f.Bridge.PendingCount);
            f.Bridge.Update();
            CollectionAssert.AreEqual(new[] { (1, 10), (2, 20), (3, 30) }, hits);
            Assert.AreEqual(0, f.Bridge.PendingCount);
        }

        [Test]
        public void WorldsDoNotConsumeEachOthersRequestsAndMissingSingletonInvalidatesCache()
        {
            using var a = new Fixture();
            using var b = new Fixture();
            var aHits = new List<int>();
            var bHits = new List<int>();
            a.Bridge.Hit += (_, attack, _) => aHits.Add(attack);
            b.Bridge.Hit += (_, attack, _) => bHits.Add(attack);
            a.Bridge.Enqueue(Bounds, 1, 10);
            b.Bridge.Enqueue(Bounds, 2, 20);
            a.Bridge.Update();
            CollectionAssert.AreEqual(new[] { 1 }, aHits);
            Assert.IsEmpty(bHits);
            Assert.AreEqual(1, b.Bridge.PendingCount);
            b.Bridge.Update();
            CollectionAssert.AreEqual(new[] { 2 }, bHits);
            a.World.EntityManager.DestroyEntity(a.Singleton);
            a.Bridge.Enqueue(Bounds, 3, 30);
            a.Bridge.Update();
            Assert.IsFalse(a.Bridge.IsReady);
            Assert.AreEqual(1, a.Bridge.PendingCount);
            Assert.AreEqual(1, aHits.Count);
        }

        [Test]
        public void DefaultWorldApiDoesNotRetainDestroyedWorldRequestsOrSubscriptions()
        {
            var previous = World.DefaultGameObjectInjectionWorld;
            int hits = 0;
            try
            {
                using (var a = new Fixture())
                {
                    World.DefaultGameObjectInjectionWorld = a.World;
                    UnityPhysicsOverlapBridge.EntityHit += (_, _, _) => hits++;
                    UnityPhysicsOverlapBridge.Queue(Bounds, 1, 10);
                    a.Bridge.Update();
                    Assert.IsTrue(UnityPhysicsOverlapBridge.Ready);
                    Assert.AreEqual(1, hits);
                    UnityPhysicsOverlapBridge.Queue(Bounds, 2, 20);
                }
                Assert.IsFalse(UnityPhysicsOverlapBridge.Ready);
                using var b = new Fixture();
                World.DefaultGameObjectInjectionWorld = b.World;
                UnityPhysicsOverlapBridge.Queue(Bounds, 3, 30);
                b.Bridge.Update();
                Assert.AreEqual(1, hits, "Old subscribers must not survive default-world destruction.");
                Assert.AreEqual(0, b.Bridge.PendingCount);
            }
            finally { World.DefaultGameObjectInjectionWorld = previous; }
        }
    }
}

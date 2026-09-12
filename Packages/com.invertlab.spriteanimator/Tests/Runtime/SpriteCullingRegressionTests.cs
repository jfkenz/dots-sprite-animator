using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteCullingRegressionTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void FrustumCullingUsesParentWorldPositionAndCameraRotation(bool orthographic)
        {
            using var world = new World("Culling regression");
            var host = new GameObject("Culling camera");
            bool previousLayout = SpriteBatchSpawner.LayoutXy;
            try
            {
                SpriteBatchSpawner.LayoutXy = true;
                host.tag = "MainCamera";
                var camera = host.AddComponent<Camera>();
                camera.orthographic = orthographic;
                camera.orthographicSize = 5;
                camera.aspect = .5f;
                camera.fieldOfView = 60;
                host.transform.SetPositionAndRotation(new Vector3(0, 0, -10), Quaternion.Euler(0, 0, 90));
                var em = world.EntityManager;
                var culler = world.GetOrCreateSystem<SpriteAnimCullingSystem>();
                using var settings = em.CreateEntityQuery(typeof(SpriteCullSettings));
                em.SetComponentData(settings.GetSingletonEntity(), new SpriteCullSettings());
                var parent = em.CreateEntity();
                em.AddComponentData(parent, LocalTransform.FromPosition(new float3(100, 0, 0)));
                var child = em.CreateEntity(typeof(SpriteAnimEnabled));
                em.AddComponentData(child, LocalTransform.FromPositionRotationScale(new float3(-96, 0, 0), quaternion.identity, .1f));
                em.AddComponentData(child, new Parent { Value = parent });
                culler.Update(world.Unmanaged);
                em.CompleteAllTrackedJobs();
                Assert.IsTrue(em.IsComponentEnabled<SpriteAnimEnabled>(child), "World x=4 remains visible through rotated camera.");
                em.SetComponentData(parent, LocalTransform.FromPosition(new float3(200, 0, 0)));
                culler.Update(world.Unmanaged);
                em.CompleteAllTrackedJobs();
                Assert.IsFalse(em.IsComponentEnabled<SpriteAnimEnabled>(child));
                em.SetComponentData(parent, LocalTransform.FromPosition(new float3(100, 0, 0)));
                culler.Update(world.Unmanaged);
                em.CompleteAllTrackedJobs();
                Assert.IsTrue(em.IsComponentEnabled<SpriteAnimEnabled>(child), "Culled entities must re-enter the view.");
            }
            finally { SpriteBatchSpawner.LayoutXy = previousLayout; Object.DestroyImmediate(host); }
        }
    }
}

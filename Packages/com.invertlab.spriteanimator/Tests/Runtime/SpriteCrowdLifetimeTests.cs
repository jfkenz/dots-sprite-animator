using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteCrowdLifetimeTests
    {
        [Test]
        public void CrowdUsesGpuForSimpleClipAndCpuForWeightedPingPongAndRecreatesWorld()
        {
            var previousWorld = World.DefaultGameObjectInjectionWorld;
            var host = new GameObject("Crowd lifetime test");
            var texture = new Texture2D(16, 4);
            try
            {
                var source = host.AddComponent<SpriteAnimSetAuthoring>();
                source.Sheet = texture;
                source.Columns = 4;
                source.Rows = 1;
                source.ShowSpriteInScene = false;
                source.Clips = new[]
                {
                    new SpriteAnimSetAuthoring.ClipAuthoring
                    {
                        Name = "Loop", Frames = new[] { 0, 1, 2, 3 }, Loop = true, FrameRate = 4,
                        WrapMode = SpriteAnimWrap.Loop, OnCompleteClipIndex = -1, ComboWindowEndFrame = -1,
                    },
                    new SpriteAnimSetAuthoring.ClipAuthoring
                    {
                        Name = "Weighted", Frames = new[] { 0, 1, 2, 3 }, FrameRate = 4,
                        WrapMode = SpriteAnimWrap.PingPong, FrameDurationScales = new[] { 1f, 2f, 3f, 1f },
                        OnCompleteClipIndex = -1, ComboWindowEndFrame = -1,
                    },
                };
                var spawner = host.AddComponent<SpriteCrowdSpawnerAuthoring>();
                spawner.Source = source;
                spawner.UseGpuAnim = true;
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    BlobAssetReference<SpriteAnimSetBlob> blob;
                    using (var world = new World("Crowd lifecycle " + cycle))
                    {
                        World.DefaultGameObjectInjectionWorld = world;
                        Assert.IsTrue(spawner.EnsureProto());
                        using var q = world.EntityManager.CreateEntityQuery(typeof(SpriteCrowdEntityTag), typeof(SpriteAnimSetRef));
                        var entity = q.GetSingletonEntity();
                        blob = world.EntityManager.GetComponentData<SpriteAnimSetRef>(entity).Set;
                        Assert.IsTrue(world.EntityManager.HasComponent<SpriteGpuDriven>(entity));
                        spawner.SetAllClips(1);
                        Assert.IsFalse(world.EntityManager.HasComponent<SpriteGpuDriven>(entity));
                        Assert.AreEqual(1, world.EntityManager.GetComponentData<SpriteAnimPlayer>(entity).ClipIndex);
                        Assert.AreEqual(SpriteAnimWrap.PingPong, blob.Value.Clips[1].WrapMode);
                        Assert.AreEqual(3, blob.Value.Clips[1].DurationScales[2]);
                    }
                    Assert.Throws<System.InvalidOperationException>(() => { var count = blob.Value.Clips.Length; });
                }
            }
            finally
            {
                World.DefaultGameObjectInjectionWorld = previousWorld;
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(texture);
            }
        }
    }
}

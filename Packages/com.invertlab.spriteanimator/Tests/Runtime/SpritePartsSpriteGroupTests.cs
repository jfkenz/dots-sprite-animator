using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Sprite groups (eyes open / closed together) and layer sprite keys.</summary>
    public sealed class SpritePartsSpriteGroupTests
    {
        // Sprites: 0 open, 1 closed, 2 smile. Parts: eyeL, eyeR, mouth. Group "Eyes": 0 Open, 1 Closed.
        // Clips: 0 Blink keys Eyes = Closed; 1 Wink keys Eyes = Closed and eyeL's own sprite open; 2 Idle; 3 Smile keys mouth smile.
        static SpritePartsSetBuilder.AppearanceInput Art(string id, int cell) => new SpritePartsSetBuilder.AppearanceInput
        {
            AppearanceId = id, CellIndex = cell, LogicalWorldSize = new float2(1f, 1f), Pivot = new float2(0.5f, 0.5f), FrameScale = new float2(1f, 1f),
        };

        static SpritePartsSetBuilder.SlotInput Part(string id) => new SpritePartsSetBuilder.SlotInput
        {
            Name = id, SlotId = id, RestScale = new float2(1f, 1f), DefaultAppearanceId = "open",
        };

        static SpritePartsSetBuilder.ValueTrackInput EyesClosed() => new SpritePartsSetBuilder.ValueTrackInput
        {
            Kind = (byte)SpritePartsValueKind.SpriteGroup, Target = "Eyes",
            Keys = new[] { new SpritePartsSetBuilder.ValueKeyInput { Time = 0f, Value = 1f } },
        };

        static SpritePartsSetBuilder.TrackInput Sprite(string slot, string app) => new SpritePartsSetBuilder.TrackInput
        {
            SlotId = slot,
            Keys = new[] { new SpritePartsSetBuilder.KeyInput { Time = 0f, Scale = new float2(1f, 1f), AppearanceId = app } },
        };

        static BlobAssetReference<SpritePartsSetBlob> Build()
        {
            SpritePartsSetBuilder.ClipInput Clip(string id, SpritePartsSetBuilder.TrackInput[] tracks, params SpritePartsSetBuilder.ValueTrackInput[] values)
                => new SpritePartsSetBuilder.ClipInput { Name = id, ClipId = id, Duration = 1f, SpeedMultiplier = 1f, Tracks = tracks, ValueTracks = values };
            var none = System.Array.Empty<SpritePartsSetBuilder.TrackInput>();
            var clips = new[]
            {
                Clip("Blink", none, EyesClosed()),
                Clip("Wink", new[] { Sprite("eyeL", "open") }, EyesClosed()),
                Clip("Idle", none),
                Clip("Smile", new[] { Sprite("mouth", "smile") }),
            };
            SpritePartsSetBuilder.SkinBindingInput Bind(string slot, string app) => new SpritePartsSetBuilder.SkinBindingInput { SlotId = slot, AppearanceId = app };
            var groups = new[]
            {
                new SpritePartsSetBuilder.SpriteGroupInput
                {
                    Name = "Eyes",
                    States = new[]
                    {
                        new SpritePartsSetBuilder.SpriteGroupStateInput { Name = "Open", Bindings = new[] { Bind("eyeL", "open"), Bind("eyeR", "open") } },
                        new SpritePartsSetBuilder.SpriteGroupStateInput { Name = "Closed", Bindings = new[] { Bind("eyeL", "closed"), Bind("eyeR", "closed") } },
                    },
                },
            };
            return SpritePartsSetBuilder.Build(Allocator.Temp, new[] { Part("eyeL"), Part("eyeR"), Part("mouth") },
                new[] { Art("open", 0), Art("closed", 1), Art("smile", 2) }, clips, System.Array.Empty<SpritePartsSetBuilder.SkinInput>(),
                spriteGroups: groups);
        }

        static int[] Sprites(BlobAssetReference<SpritePartsSetBlob> blob, int clip, NativeArray<SpritePartsAnimLayer> layers = default,
            NativeArray<int> gameplay = default)
        {
            int n = blob.Value.Slots.Length;
            var ov = new NativeArray<SpritePartsPoseOverride>(0, Allocator.Temp);
            var basePoses = new NativeArray<SpritePartsSampler.Pose>(n, Allocator.Temp);
            var local = new NativeArray<SpritePartsSampler.Pose>(n, Allocator.Temp);
            var ltr = new NativeArray<float4x4>(n, Allocator.Temp);
            var sources = new NativeArray<SpritePartPoseSource>(n, Allocator.Temp);
            var apps = new NativeArray<int>(n, Allocator.Temp);
            try
            {
                SpritePartsPoseWriter.Evaluate(ref blob.Value, SpritePartsPoseWriter.DefaultPlayer(clip, false), ov, layers, basePoses, local, ltr,
                    sources, apps, float4x4.identity, false, false, new SpritePartsEvalExtras { GroupStates = gameplay });
                return apps.ToArray();
            }
            finally
            {
                ov.Dispose();
                basePoses.Dispose();
                local.Dispose();
                ltr.Dispose();
                sources.Dispose();
                apps.Dispose();
            }
        }

        [Test]
        public void A_Keyed_State_Switches_Every_Part_In_The_Group()
        {
            var blob = Build();
            try
            {
                CollectionAssert.AreEqual(new[] { -1, -1, -1 }, Sprites(blob, 2), "Idle: the parts' own sprites.");
                CollectionAssert.AreEqual(new[] { 1, 1, -1 }, Sprites(blob, 0), "Blink closes both eyes with one key.");
                CollectionAssert.AreEqual(new[] { 0, 1, -1 }, Sprites(blob, 1), "A part's own sprite key wins over the group key.");
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Gameplay_State_Wins_And_Layers_Show_Their_Sprites()
        {
            var blob = Build();
            var gameplay = new NativeArray<int>(new[] { 1 }, Allocator.Temp);
            var layers = new NativeArray<SpritePartsAnimLayer>(1, Allocator.Temp);
            try
            {
                CollectionAssert.AreEqual(new[] { 1, 1, -1 }, Sprites(blob, 1, default, gameplay), "Set from gameplay: over the part key too.");
                layers[0] = new SpritePartsAnimLayer { ClipIndex = 3, Weight = 1f, TargetWeight = 1f, OwnClock = 1 };
                CollectionAssert.AreEqual(new[] { -1, -1, 2 }, Sprites(blob, 2, layers), "The Smile layer's sprite key shows.");
            }
            finally
            {
                gameplay.Dispose();
                layers.Dispose();
                blob.Dispose();
            }
        }

        [Test]
        public void Auto_Blink_Closes_For_A_Moment_And_Yields_To_Gameplay()
        {
            var blob = Build();
            using var world = new World("parts-blink");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, float3.zero);
            try
            {
                Assert.IsTrue(SpriteParts.EnableAutoBlink(em, created.Root, "Eyes", "Closed", 0.5f, 0.5f, 0.1f));
                var system = world.GetOrCreateSystem<SpritePartsPlayerSystem>();
                double time = 0;
                void Tick(float dt)
                {
                    time += dt;
                    world.SetTime(new Unity.Core.TimeData(time, dt));
                    system.Update(world.Unmanaged);
                }
                Tick(0.4f);
                Assert.AreEqual(-1, SpriteParts.GetSpriteGroup(em, created.Root, "Eyes"));
                Tick(0.2f);
                Assert.AreEqual(1, SpriteParts.GetSpriteGroup(em, created.Root, "Eyes"), "Blink: closed.");
                Tick(0.15f);
                Assert.AreEqual(-1, SpriteParts.GetSpriteGroup(em, created.Root, "Eyes"), "Open again.");
                SpriteParts.SetSpriteGroup(em, created.Root, "Eyes", "Open");
                Tick(0.6f);
                Assert.AreEqual(0, SpriteParts.GetSpriteGroup(em, created.Root, "Eyes"), "Gameplay's state is left alone.");
            }
            finally
            {
                created.Parts.Dispose();
                blob.Dispose();
            }
        }

        [Test]
        public void SetSpriteGroup_Finds_States_By_Name()
        {
            var blob = Build();
            using var world = new World("parts-groups");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, float3.zero, playing: false);
            try
            {
                Assert.IsTrue(SpriteParts.SetSpriteGroup(em, created.Root, "Eyes", "Closed"));
                Assert.AreEqual(1, SpriteParts.GetSpriteGroup(em, created.Root, "Eyes"));
                Assert.IsFalse(SpriteParts.SetSpriteGroup(em, created.Root, "Eyes", "Squint"));
                SpriteParts.ClearSpriteGroup(em, created.Root, "Eyes");
                Assert.AreEqual(-1, SpriteParts.GetSpriteGroup(em, created.Root, "Eyes"));
            }
            finally
            {
                created.Parts.Dispose();
                blob.Dispose();
            }
        }
    }
}

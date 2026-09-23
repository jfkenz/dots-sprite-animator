using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Jiggle springs: lag behind motion, sag with gravity, settle, and stay out of keyed poses.</summary>
    public sealed class SpritePartsJiggleTests
    {
        static SpritePartsSetBuilder.SlotInput Slot(string id, string parent, float2 pos)
            => new SpritePartsSetBuilder.SlotInput
            {
                Name = id, SlotId = id, ParentSlotId = parent, RestPosition = pos, RestScale = new float2(1f, 1f),
            };

        // body -> hair (joint at the body) -> tip one unit away along tipDir.
        static BlobAssetReference<SpritePartsSetBlob> Hair(float2 tipDir, float gravity, float stiffness = 0.5f)
        {
            var slots = new[] { Slot("body", null, float2.zero), Slot("hair", "body", float2.zero), Slot("tip", "hair", tipDir) };
            var jiggles = new[]
            {
                new SpritePartsSetBuilder.JiggleInput
                {
                    SlotId = "hair", TipLocal = tipDir, Stiffness = stiffness, Damping = 0.3f, Gravity = gravity, Mix = 1f,
                },
            };
            return SpritePartsSetBuilder.Build(Allocator.Temp, slots, System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                System.Array.Empty<SpritePartsSetBuilder.ClipInput>(), System.Array.Empty<SpritePartsSetBuilder.SkinInput>(),
                null, jiggles);
        }

        /// <summary>One frame at <paramref name="rootX"/>; returns the hair's local rotation.</summary>
        static float Frame(BlobAssetReference<SpritePartsSetBlob> blob, NativeArray<SpritePartJiggleState> state, float rootX, float dt,
            bool keyed = false)
        {
            ref var set = ref blob.Value;
            int n = set.Slots.Length;
            var player = SpritePartsPoseWriter.DefaultPlayer(-1, false);
            var basePoses = new NativeArray<SpritePartsSampler.Pose>(n, Allocator.Temp);
            var local = new NativeArray<SpritePartsSampler.Pose>(n, Allocator.Temp);
            var ltr = new NativeArray<float4x4>(n, Allocator.Temp);
            var sources = new NativeArray<SpritePartPoseSource>(n, Allocator.Temp);
            var apps = new NativeArray<int>(n, Allocator.Temp);
            try
            {
                SpritePartsPoseWriter.Evaluate(ref set, player, default, default, basePoses, local, ltr, sources, apps,
                    float4x4.Translate(new float3(rootX, 0f, 0f)), false, false,
                    new SpritePartsEvalExtras { Jiggle = state, DeltaTime = dt, KeyedPoseOnly = keyed });
                return local[1].Rotation;
            }
            finally
            {
                basePoses.Dispose();
                local.Dispose();
                ltr.Dispose();
                sources.Dispose();
                apps.Dispose();
            }
        }

        [Test]
        public void Hair_Lags_Behind_A_Move_Then_Settles()
        {
            var blob = Hair(new float2(0f, -1f), 0f);
            var state = new NativeArray<SpritePartJiggleState>(1, Allocator.Temp);
            try
            {
                Assert.AreEqual(0f, Frame(blob, state, 0f, 0f), 1e-4f, "The first frame starts at the animated pose.");
                float swing = Frame(blob, state, 0.5f, 1f / 60f);
                Assert.Less(swing, -1f, "Moving right leaves the tip behind (to the left): a clockwise swing.");
                float rot = 0f;
                for (int i = 0; i < 300; i++)
                    rot = Frame(blob, state, 0.5f, 1f / 60f);
                Assert.AreEqual(0f, rot, 0.1f, "At rest it settles back to the animated pose.");
            }
            finally
            {
                state.Dispose();
                blob.Dispose();
            }
        }

        [Test]
        public void Gravity_Makes_A_Sideways_Tail_Sag()
        {
            var blob = Hair(new float2(1f, 0f), 20f);
            var state = new NativeArray<SpritePartJiggleState>(1, Allocator.Temp);
            try
            {
                Frame(blob, state, 0f, 0f);
                float rot = 0f;
                for (int i = 0; i < 240; i++)
                    rot = Frame(blob, state, 0f, 1f / 60f);
                Assert.Less(rot, -1f, "The tail droops (clockwise from pointing right).");
                Assert.Greater(rot, -90f, "The spring holds it up part of the way.");
                Assert.AreEqual(0f, Frame(blob, state, 0f, 1f / 60f, keyed: true), 1e-5f, "The keyed pose has no jiggle.");
            }
            finally
            {
                state.Dispose();
                blob.Dispose();
            }
        }

        [Test]
        public void Chain_Expands_To_One_Spring_Per_Joint_With_Tips()
        {
            var profile = new SpriteSheetProfile();
            profile.PartsSlots.Add(new SpritePartSlotDef { SlotId = "head", Name = "Head" });
            profile.PartsSlots.Add(new SpritePartSlotDef { SlotId = "hair1", Name = "Hair 1", ParentSlotId = "head" });
            profile.PartsSlots.Add(new SpritePartSlotDef { SlotId = "hair2", Name = "Hair 2", ParentSlotId = "hair1", RestPosition = new UnityEngine.Vector2(0f, -0.5f) });
            profile.PartsSlots.Add(new SpritePartSlotDef { SlotId = "hair3", Name = "Hair 3", ParentSlotId = "hair2", RestPosition = new UnityEngine.Vector2(0f, -0.4f), IsBone = true, BoneLength = 0.3f });
            profile.PartsJiggles.Add(new SpritePartsJiggleDef { SlotId = "hair1", ChainLength = 3 });
            var inputs = SpritePartsClipConversion.CreateJiggles(profile);
            Assert.AreEqual(3, inputs.Length);
            Assert.AreEqual(new float2(0f, -0.5f), inputs[0].TipLocal, "Tip = the first child.");
            Assert.AreEqual(new float2(0f, -0.4f), inputs[1].TipLocal);
            Assert.AreEqual(new float2(0.3f, 0f), inputs[2].TipLocal, "The last bone swings its own length.");
        }
    }
}

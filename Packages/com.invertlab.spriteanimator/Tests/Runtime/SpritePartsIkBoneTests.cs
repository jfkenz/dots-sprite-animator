using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Runtime IK constraints and bone slots.</summary>
    public sealed class SpritePartsIkBoneTests
    {
        static SpritePartsSetBuilder.SlotInput Slot(string id, string parent, float2 pos)
            => new SpritePartsSetBuilder.SlotInput
            {
                Name = id, SlotId = id, ParentSlotId = parent, RestPosition = pos, RestScale = new float2(1f, 1f),
            };

        // upper (0,0) -> lower (1,0) -> foot (2,0); target on its own at (1,1).
        static BlobAssetReference<SpritePartsSetBlob> Arm(int chain, bool bendPositive, float mix = 1f)
        {
            var slots = new[]
            {
                Slot("upper", null, float2.zero),
                Slot("lower", "upper", new float2(1f, 0f)),
                Slot("foot", "lower", new float2(1f, 0f)),
                Slot("target", null, new float2(1f, 1f)),
            };
            var ik = new[]
            {
                new SpritePartsSetBuilder.IkInput
                {
                    EffectorSlotId = "foot", TargetSlotId = "target", ChainLength = chain, BendPositive = bendPositive, Mix = mix,
                },
            };
            return SpritePartsSetBuilder.Build(Allocator.Temp, slots, System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                System.Array.Empty<SpritePartsSetBuilder.ClipInput>(), System.Array.Empty<SpritePartsSetBuilder.SkinInput>(), ik);
        }

        static float2 Solve(BlobAssetReference<SpritePartsSetBlob> blob, int slot, out float2 knee)
        {
            int n = blob.Value.Slots.Length;
            var local = new NativeArray<SpritePartsSampler.Pose>(n, Allocator.Temp);
            var ltr = new NativeArray<float4x4>(n, Allocator.Temp);
            try
            {
                SpritePartsPoseWriter.EvaluateEditor(ref blob.Value, -1, 0f, local, ltr);
                knee = ltr[1].c3.xy;
                return ltr[slot].c3.xy;
            }
            finally
            {
                local.Dispose();
                ltr.Dispose();
            }
        }

        [Test]
        public void TwoBone_Reaches_The_Target_Both_Bend_Ways()
        {
            var up = Arm(2, true);
            var down = Arm(2, false);
            try
            {
                float2 a = Solve(up, 2, out var kneeA);
                float2 b = Solve(down, 2, out var kneeB);
                Assert.AreEqual(1f, a.x, 1e-3f);
                Assert.AreEqual(1f, a.y, 1e-3f);
                Assert.AreEqual(1f, b.x, 1e-3f);
                Assert.AreEqual(1f, b.y, 1e-3f);
                Assert.AreEqual(1f, math.length(kneeA), 1e-3f, "Bone lengths are kept.");
                Assert.Greater(math.distance(kneeA, kneeB), 0.5f, "The two bend directions put the elbow on opposite sides.");
            }
            finally
            {
                up.Dispose();
                down.Dispose();
            }
        }

        [Test]
        public void OneBone_Aims_At_The_Target()
        {
            var blob = Arm(1, true);
            try
            {
                float2 foot = Solve(blob, 2, out var knee);
                Assert.AreEqual(new float2(1f, 0f), knee, "Chain 1: the upper joint does not move.");
                Assert.AreEqual(1f, foot.x, 1e-3f);
                Assert.AreEqual(1f, foot.y, 1e-3f);
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Mix_Zero_Leaves_The_Clip_Pose()
        {
            var blob = Arm(2, true, 0f);
            try
            {
                float2 foot = Solve(blob, 2, out _);
                Assert.AreEqual(2f, foot.x, 1e-4f);
                Assert.AreEqual(0f, foot.y, 1e-4f);
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Bone_Is_Not_Drawn_But_Its_Children_Are()
        {
            var profile = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            var bodyId = profile.PartsSlots[0].SlotId;
            Assert.IsTrue(SpritePartsAuthoringOps.TryAddBone(profile, bodyId, out var bone).Ok);
            Assert.IsTrue(bone.IsBone);
            bone.DefaultAppearanceId = profile.PartsSlots[0].DefaultAppearanceId; // ignored for bones
            Assert.IsTrue(SpritePartsAuthoringOps.TryAddChildPart(profile, bone.SlotId, out var child).Ok);
            var slots = SpritePartsClipConversion.CreateSlots(profile);
            int bi = profile.PartsSlots.IndexOf(bone), ci = profile.PartsSlots.IndexOf(child);
            Assert.AreEqual(1, slots[bi].Hidden, "A bone never draws itself.");
            Assert.AreEqual(string.Empty, slots[bi].DefaultAppearanceId, "A bone has no image.");
            Assert.AreEqual(0, slots[ci].Hidden, "Parts under a bone still draw.");
        }
    }
}

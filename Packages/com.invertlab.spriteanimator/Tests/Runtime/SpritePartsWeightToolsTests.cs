using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Weight setup helpers: bone chains, nearest bones, Left/Right names, weight mirror.</summary>
    public sealed class SpritePartsWeightToolsTests
    {
        [TestCase("Arm L", "Arm R")]
        [TestCase("hand.l", "hand.r")]
        [TestCase("R_leg", "L_leg")]
        [TestCase("LeftLeg", "RightLeg")]
        [TestCase("Right Arm", "Left Arm")]
        [TestCase("Leg", null)]
        [TestCase("Lamp", null)]
        public void MirrorSideName_Swaps_Sides(string name, string expected)
        {
            Assert.AreEqual(expected, SpritePartsSkinning.MirrorSideName(name));
        }

        [Test]
        public void MirrorWeights_Copies_Left_To_Right_With_Bones_Swapped()
        {
            var mesh = new SpritePartMeshDef
            {
                Vertices = new[] { new Vector2(0.2f, 0.5f), new Vector2(0.8f, 0.5f), new Vector2(0.5f, 0.9f) },
                HullCount = 3,
                Triangles = new[] { 0, 1, 2 },
                Bones = new[] { "arm_l", "arm_r", "body" },
                Weights = new[]
                {
                    0.7f, 0f, 0.3f,   // left vertex
                    0.1f, 0.1f, 0.8f, // right vertex (gets overwritten)
                    0.6f, 0f, 0.4f,   // on the axis
                },
            };
            int pairs = SpritePartsSkinning.MirrorWeights(mesh, 0.5f, true, new[] { 1, 0, 2 });
            Assert.AreEqual(1, pairs);
            Assert.AreEqual(0.7f, mesh.Weights[0], 1e-5f, "The source side is kept.");
            Assert.AreEqual(0f, mesh.Weights[3], 1e-5f);
            Assert.AreEqual(0.7f, mesh.Weights[4], 1e-5f, "The right vertex takes the left arm's weight on the right arm.");
            Assert.AreEqual(0.3f, mesh.Weights[5], 1e-5f);
            Assert.AreEqual(0.3f, mesh.Weights[6], 1e-5f, "An axis vertex splits its sided weight evenly.");
            Assert.AreEqual(0.3f, mesh.Weights[7], 1e-5f);
            Assert.AreEqual(0.4f, mesh.Weights[8], 1e-5f);
        }

        [Test]
        public void ChainBones_Walks_Down_And_Up()
        {
            var profile = new SpriteSheetProfile();
            profile.PartsSlots ??= new List<SpritePartSlotDef>();
            profile.PartsSlots.Add(new SpritePartSlotDef { SlotId = "root", Name = "Root" });
            profile.PartsSlots.Add(new SpritePartSlotDef { SlotId = "upper", Name = "Upper", ParentSlotId = "root" });
            profile.PartsSlots.Add(new SpritePartSlotDef { SlotId = "lower", Name = "Lower", ParentSlotId = "upper" });
            profile.PartsSlots.Add(new SpritePartSlotDef { SlotId = "thumb", Name = "Thumb", ParentSlotId = "upper" });

            CollectionAssert.AreEqual(new[] { "upper", "lower", "thumb" }, SpritePartsSkinning.ChainBones(profile, "upper", true));
            CollectionAssert.AreEqual(new[] { "lower", "upper", "root" }, SpritePartsSkinning.ChainBones(profile, "lower", false));
            CollectionAssert.AreEqual(new[] { "root", "upper" }, SpritePartsSkinning.ChainBones(profile, "root", true, 2));
        }

        [Test]
        public void NearestBones_Keeps_Bones_On_The_Mesh_Best_First()
        {
            var verts = new List<float2> { new float2(0f, 0f), new float2(1f, 0f), new float2(1f, 1f), new float2(0f, 1f) };
            var starts = new List<float2> { new float2(0.5f, 0.4f), new float2(5f, 5f), new float2(1.05f, 0.5f) };
            var ends = new List<float2> { new float2(0.5f, 0.6f), new float2(6f, 5f), new float2(1.05f, 2f) };
            Assert.AreEqual(new List<int> { 2 }, SpritePartsSkinning.NearestBones(verts, starts, ends, 0.1f, 4),
                "Without triangles, the middle bone is 0.64 from every vertex.");
            var nearest = SpritePartsSkinning.NearestBones(verts, starts, ends, 0.1f, 4, new[] { 0, 1, 2, 0, 2, 3 });
            CollectionAssert.AreEqual(new[] { 0, 2 }, nearest, "A bone inside the mesh wins; the far bone is left out.");
        }
    }
}

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
        public void Locked_Bone_Keeps_Its_Weight_Through_Set_Smooth_Prune_And_Auto()
        {
            const int locked = 1 << 2; // bone 2
            // One vertex row: 0.5 / 0.2 / 0.3 (bone 2 locked at 0.3).
            var w = new[] { 0.5f, 0.2f, 0.3f };
            SpritePartsSkinning.SetWeight(w, 3, 0, 0, 1f, locked);
            Assert.AreEqual(0.7f, w[0], 1e-5f, "Bone 0 can only take what the lock leaves.");
            Assert.AreEqual(0f, w[1], 1e-5f);
            Assert.AreEqual(0.3f, w[2], 1e-5f);
            SpritePartsSkinning.SetWeight(w, 3, 0, 2, 0f, locked);
            Assert.AreEqual(0.3f, w[2], 1e-5f, "A locked bone cannot be painted.");

            w = new[] { 0.02f, 0.68f, 0.3f };
            SpritePartsSkinning.Prune(w, 3, 0.05f, locked);
            Assert.AreEqual(0f, w[0], 1e-5f);
            Assert.AreEqual(0.7f, w[1], 1e-5f, "The free bones share what is left.");
            Assert.AreEqual(0.3f, w[2], 1e-5f);

            // Two vertices in a triangle with a third: smoothing moves free bones only.
            var tri = new[] { 0, 1, 2 };
            var rows = new[] { 1f, 0f, 0f, 0f, 0.5f, 0.5f, 0f, 0.5f, 0.5f };
            SpritePartsSkinning.Smooth(rows, 3, tri, null, 1f, 1 << 0);
            Assert.AreEqual(1f, rows[0], 1e-5f, "Vertex 0 is all bone 0, which is locked.");
            for (int v = 0; v < 3; v++)
                Assert.AreEqual(1f, rows[v * 3] + rows[v * 3 + 1] + rows[v * 3 + 2], 1e-5f, "Rows still sum to 1.");

            var fresh = new[] { 0.1f, 0.1f, 0.8f };
            var kept = SpritePartsSkinning.KeepLocked(fresh, new[] { 0.6f, 0.2f, 0.2f }, 3, 1 << 0);
            Assert.AreEqual(0.6f, kept[0], 1e-5f, "Auto keeps the locked bone's old weight.");
            Assert.AreEqual(0.4f * 0.1f / 0.9f, kept[1], 1e-5f);
            Assert.AreEqual(0.4f * 0.8f / 0.9f, kept[2], 1e-5f);
        }

        [Test]
        public void SmoothBy_Only_Touches_Vertices_Under_The_Brush()
        {
            var tri = new[] { 0, 1, 2 };
            var rows = new[] { 1f, 0f, 0f, 1f, 0f, 1f };
            SpritePartsSkinning.SmoothBy(rows, 2, tri, new[] { 1f, 0f, 0f });
            Assert.Less(rows[0], 1f, "Vertex 0 moved toward its neighbours.");
            Assert.AreEqual(0f, rows[2], 1e-6f, "Vertex 1 was outside the brush.");
            Assert.AreEqual(0f, rows[4], 1e-6f);
        }

        [Test]
        public void Pins_Follow_Their_Vertices_When_The_Mesh_Changes()
        {
            var profile = new SpriteSheetProfile();
            var mesh = SpritePartsMeshOps.CreateQuad();
            mesh.Pins = new[] { 1, 3 };
            profile.PartsSlots.Add(new SpritePartSlotDef { SlotId = "face", Name = "Face", Mesh = mesh });
            Assert.AreEqual(mesh.Pins, mesh.Clone().Pins, "Clones keep the pins.");
            // Vertex 0 removed, a new vertex appended: 1 -> 0, 3 -> 2.
            SpritePartsAuthoringOps.RemapSlotDeforms(profile, "face", new[] { -1, 0, 1, 2 }, 4);
            CollectionAssert.AreEqual(new[] { 0, 2 }, profile.PartsSlots[0].Mesh.Pins);
            SpritePartsAuthoringOps.RemapSlotPins(profile, "face", new[] { 0, 1, -1, 2 }, 3);
            CollectionAssert.AreEqual(new[] { 0 }, profile.PartsSlots[0].Mesh.Pins, "A pin on a removed vertex goes.");
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

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Copy / paste / mirror pose.</summary>
    public sealed class SpritePartsPoseToolsTests
    {
        static SpriteSheetProfile Profile()
        {
            var profile = new SpriteSheetProfile();
            profile.PartsSlots.Add(new SpritePartSlotDef { Name = "Arm L", SlotId = "arm_l", RestPosition = new Vector2(-1f, 0f), RestRotation = 10f });
            profile.PartsSlots.Add(new SpritePartSlotDef { Name = "Arm R", SlotId = "arm_r", RestPosition = new Vector2(1f, 0f), RestRotation = -10f });
            profile.PartsSlots.Add(new SpritePartSlotDef { Name = "Body", SlotId = "body" });
            profile.PartsClips.Add(new SpritePartsClipDef { Name = "Walk", ClipId = "walk", Duration = 1f });
            return profile;
        }

        static SpritePartsAuthoringOps.PoseValue V(float x, float y, float rot)
            => new SpritePartsAuthoringOps.PoseValue { Position = new Vector2(x, y), Rotation = rot, Scale = Vector2.one };

        [Test]
        public void Mirror_Swaps_Sides_And_Flips_The_Motion()
        {
            var profile = Profile();
            var pose = new Dictionary<string, SpritePartsAuthoringOps.PoseValue>
            {
                ["arm_l"] = V(-1f, 1f, 30f), // raised 1 and turned +20 from its setup
                ["arm_r"] = V(1f, 0f, -10f), // at its setup
                ["body"] = V(0.5f, 0f, 5f),
            };
            var mirrored = SpritePartsAuthoringOps.MirrorPose(profile, pose);
            Assert.AreEqual(new Vector2(1f, 1f), mirrored["arm_r"].Position, "The right arm takes the left arm's raise.");
            Assert.AreEqual(-30f, mirrored["arm_r"].Rotation, 1e-4f, "...and its turn, mirrored.");
            Assert.AreEqual(new Vector2(-1f, 0f), mirrored["arm_l"].Position, "The left arm takes the right arm's rest.");
            Assert.AreEqual(10f, mirrored["arm_l"].Rotation, 1e-4f);
            Assert.AreEqual(new Vector2(-0.5f, 0f), mirrored["body"].Position, "Centre parts mirror themselves.");
            Assert.AreEqual(-5f, mirrored["body"].Rotation, 1e-4f);
        }

        [Test]
        public void Paste_Keys_The_Pose_And_Respects_The_Selection()
        {
            var profile = Profile();
            var pose = new Dictionary<string, SpritePartsAuthoringOps.PoseValue> { ["arm_l"] = V(-1f, 2f, 45f), ["body"] = V(0f, 0f, 3f) };
            var result = SpritePartsAuthoringOps.PastePose(profile, 0, 0.5f, pose, new HashSet<string> { "arm_l" });
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(1, result.Affected);
            var key = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[0], "arm_l").Keys[0];
            Assert.AreEqual(0.5f, key.Time);
            Assert.AreEqual(45f, key.Rotation);
            Assert.AreEqual(SpritePartsKeyChannel.None, key.Channels & SpritePartsKeyChannel.Shear, "No shear change: no shear key.");
            Assert.IsNull(SpritePartsAuthoringOps.FindTrack(profile.PartsClips[0], "body"), "Not selected: untouched.");
        }
    }
}

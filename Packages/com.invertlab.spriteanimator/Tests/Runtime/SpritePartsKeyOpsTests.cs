using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePartsKeyOpsTests
    {
        SpriteSheetProfile Make()
        {
            var profile = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            return profile;
        }

        [Test]
        public void DeleteKeys_RemovesSelected()
        {
            var profile = Make();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            var pose = new SpritePartsAuthoringOps.PoseEdit
            {
                Position = new Vector2(0.2f, 0f),
                Rotation = 10f,
                Scale = Vector2.one,
            };
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(profile, walk, "body", 0.2f, pose).WroteKey);
            var track = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], "body");
            Assert.IsNotNull(track);
            Assert.Greater(track.Keys.Count, 0);
            var victim = track.Keys[0];
            var result = SpritePartsAuthoringOps.DeleteKeys(profile, walk, new HashSet<SpritePartsKeyDef> { victim });
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.IsFalse(track.Keys.Contains(victim));
        }

        [Test]
        public void MoveKeys_ClampsAndMerges()
        {
            var profile = Make();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            var pose = new SpritePartsAuthoringOps.PoseEdit
            {
                Position = Vector2.zero, Rotation = 0f, Scale = Vector2.one,
            };
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(profile, walk, "body", 0.1f, pose).WroteKey);
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(profile, walk, "body", 0.5f, pose).WroteKey);
            var track = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], "body");
            Assert.AreEqual(2, track.Keys.Count);
            var a = track.Keys[0];
            var b = track.Keys[1];
            float startA = a.Time;
            float delta = b.Time - startA;
            var result = SpritePartsAuthoringOps.MoveKeys(
                profile, walk,
                new List<SpritePartsKeyDef> { a },
                new List<float> { startA },
                delta, 30f, snap: false);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(1, track.Keys.Count);
        }

        [Test]
        public void NegativeScaleKey_Validates()
        {
            var profile = Make();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            var pose = new SpritePartsAuthoringOps.PoseEdit
            {
                Position = Vector2.zero, Rotation = 0f, Scale = new Vector2(-1f, 1f),
            };
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(profile, walk, "body", 0.1f, pose).WroteKey);
            var v = SpritePartsValidation.Validate(profile);
            Assert.IsTrue(v.Ok, string.Join(" | ", v.Errors));
        }
    }
}

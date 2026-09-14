using NUnit.Framework;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePartsIdTests
    {
        [Test]
        public void CanonicalTrimsLowercasesAndDotsSpaces()
        {
            Assert.AreEqual("arm.l", SpritePartIdUtility.Canonical(" Arm L "));
            Assert.AreEqual("weapon", SpritePartIdUtility.Canonical("Weapon"));
            Assert.AreEqual("hand_r", SpritePartIdUtility.Canonical("hand_R"));
        }

        [Test]
        public void CanonicalDoesNotAlterValidIds()
        {
            Assert.AreEqual("torso", SpritePartIdUtility.Canonical("torso"));
            Assert.IsTrue(SpritePartIdUtility.IsValid("arm.l"));
            Assert.IsFalse(SpritePartIdUtility.IsValid("Arm L"));
        }

        [Test]
        public void FrameClipNameLookupStaysCaseSensitive()
        {
            var profile = new SpriteSheetProfile();
            profile.Clips.Add(new SpriteClipDef { Name = "Walk" });
            profile.Clips.Add(new SpriteClipDef { Name = "walk" });

            Assert.AreSame(profile.Clips[0], profile.FindClip("Walk"));
            Assert.AreSame(profile.Clips[1], profile.FindClip("walk"));
            // Missing exact case falls through to first clip (existing frame behavior).
            Assert.AreSame(profile.Clips[0], profile.FindClip("WALK"));
        }

        [Test]
        public void CanonicalizeIdsLeavesFrameClipNamesUntouched()
        {
            var profile = new SpriteSheetProfile
            {
                AnimKind = SpriteAnimKind.Parts,
            };
            profile.PartsSlots.Add(new SpritePartSlotDef
            {
                Name = "Body",
                SlotId = " Body ",
                DrawRank = 0,
            });
            profile.Clips.Add(new SpriteClipDef { Name = "IdleCase" });

            SpritePartsValidation.CanonicalizeIds(profile);

            Assert.AreEqual("body", profile.PartsSlots[0].SlotId);
            Assert.AreEqual("IdleCase", profile.Clips[0].Name);
        }

        [Test]
        public void OldProfileDefaultsAnimKindToFrame()
        {
            var profile = new SpriteSheetProfile();
            Assert.AreEqual(SpriteAnimKind.Frame, profile.AnimKind);
            Assert.IsNotNull(profile.Clips);
        }
    }
}

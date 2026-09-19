using NUnit.Framework;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteStaticKindTests
    {
        static SpriteSheetProfile ProfileWithTexture()
        {
            var profile = new SpriteSheetProfile();
            profile.Sheets.Add(new SpriteSheetDef
            {
                Name = "Body",
                Texture = new Texture2D(8, 8),
                Columns = 2,
                Rows = 2,
            });
            profile.EnsureSheets();
            return profile;
        }

        [Test]
        public void SwitchToStatic_RequiresSheetTexture()
        {
            var profile = new SpriteSheetProfile();
            var result = SpritePartsAuthoringOps.TrySetAnimKind(profile, SpriteAnimKind.Static);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("texture", result.Reason);
            Assert.AreEqual(SpriteAnimKind.Frame, profile.AnimKind);
        }

        [Test]
        public void SwitchToStatic_SucceedsAndPreservesOtherData()
        {
            var profile = ProfileWithTexture();
            profile.Clips.Add(new SpriteClipDef { Name = "Idle", Frames = new[] { 0 } });
            SpritePartsAuthoringOps.CreateEmptyPartsRig(profile);
            profile.AnimKind = SpriteAnimKind.Frame;

            var result = SpritePartsAuthoringOps.TrySetAnimKind(profile, SpriteAnimKind.Static);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(SpriteAnimKind.Static, profile.AnimKind);
            Assert.AreEqual(1, profile.Clips.Count);
            profile.EnsureStaticSprite();
            Assert.NotNull(profile.StaticSprite);
        }

        [Test]
        public void CharacterDrawIndex_MatchesPartsFormula()
        {
            Assert.AreEqual(64, SpriteProfileLinkOps.CharacterDrawIndex(1, 0));
            Assert.AreEqual(66, SpriteProfileLinkOps.CharacterDrawIndex(1, 2));
            float depth = SpriteProfileLinkOps.CharacterSortDepth(1, 2);
            Assert.AreEqual(SpriteSortDepth.FromIndex(66), depth, 1e-6f);
        }

        [Test]
        public void StaticPivotSync_WritesCellPivot()
        {
            var profile = ProfileWithTexture();
            profile.EnsureStaticSprite();
            profile.StaticSprite.Pivot = new Vector2(0.25f, 0.75f);
            SpriteStaticAuthoringOps.SyncStaticPivotToCell(profile);
            var sheet = profile.SheetAt(0);
            Assert.IsTrue(SpriteSheetProfile.TryGetCellPivot(sheet, 0, out var pivot));
            Assert.AreEqual(0.25f, pivot.x, 1e-4f);
            Assert.AreEqual(0.75f, pivot.y, 1e-4f);
        }
    }
}

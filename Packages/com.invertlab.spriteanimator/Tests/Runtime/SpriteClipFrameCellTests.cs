using NUnit.Framework;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteClipFrameCellTests
    {
        [Test]
        public void ResolveSheetCellUsesClipRowWhenFrameRowsInherit()
        {
            var clip = new SpriteClipDef
            {
                Row = 3,
                Frames = new[] { 0, 1, 2 },
            };
            clip.EnsureFrameData();

            clip.ResolveSheetCell(1, 6, 17, out int row, out int column);

            Assert.AreEqual(3, row);
            Assert.AreEqual(1, column);
            Assert.AreEqual(3 * 6 + 1, clip.SheetCellIndex(1, 6, 17));
            Assert.IsFalse(clip.UsesMixedSheetRows());
        }

        [Test]
        public void ResolveSheetCellHonorsPerFrameRowOverride()
        {
            var clip = new SpriteClipDef
            {
                Row = 0,
                Frames = new[] { 2, 2, 2 },
            };
            clip.EnsureFrameData();
            clip.FrameRows[1] = 5;

            clip.ResolveSheetCell(1, 6, 17, out int row, out int column);

            Assert.AreEqual(5, row);
            Assert.AreEqual(2, column);
            Assert.AreEqual(5 * 6 + 2, clip.SheetCellIndex(1, 6, 17));
            Assert.IsTrue(clip.UsesMixedSheetRows());
        }

        [Test]
        public void MixedSheetRowsAreNotGpuEligible()
        {
            var clip = new SpriteClipDef
            {
                Row = 0,
                Frames = new[] { 0, 0 },
                WrapMode = SpriteAnimWrap.Loop,
            };
            clip.EnsureFrameData();
            clip.FrameRows[1] = 4;

            bool eligible = SpriteGpuEligibility.IsGpuEligible(clip, out string reason);

            Assert.IsFalse(eligible);
            StringAssert.Contains("row", reason.ToLowerInvariant());
        }

        [Test]
        public void ShiftEventMarkersAfterInsertMovesByCount()
        {
            var clip = new SpriteClipDef { Frames = new[] { 0, 1, 2 } };
            clip.EnsureFrameData();
            clip.AddEventMarker(2, 3, 0.5f);

            clip.ShiftEventMarkersAfterInsert(1, 2);

            Assert.AreEqual(4, clip.EventMarkers[0].FrameIndex);
        }
    }
}

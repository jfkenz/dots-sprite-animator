using NUnit.Framework;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteSheetCroppedLayoutTests
    {
        [Test]
        public void UniformCellUvMatchesLegacyGridMath()
        {
            var uv = SpriteSheetProfile.GetUniformCellUvRect(4, 2, 5);
            // cell 5 = row 1, col 1
            Assert.AreEqual(1f / 4f, uv.x, 1e-5f);
            Assert.AreEqual(0f, uv.y, 1e-5f);
            Assert.AreEqual(1f / 4f, uv.width, 1e-5f);
            Assert.AreEqual(1f / 2f, uv.height, 1e-5f);
        }

        [Test]
        public void CroppedModeUsesStoredPixelRectForUvAndCropST()
        {
            var tex = new Texture2D(100, 40, TextureFormat.RGBA32, false);
            var sheet = new SpriteSheetDef
            {
                Texture = tex,
                Columns = 2,
                Rows = 1,
                CellLayoutMode = SpriteSheetCellLayoutMode.Cropped,
                CroppedCellRects = new[]
                {
                    new RectInt(10, 5, 30, 20),
                    new RectInt(60, 8, 25, 18),
                },
            };

            var uv0 = SpriteSheetProfile.GetCellUvRect(sheet, 0);
            Assert.AreEqual(10f / 100f, uv0.x, 1e-5f);
            Assert.AreEqual(5f / 40f, uv0.y, 1e-5f);
            Assert.AreEqual(30f / 100f, uv0.width, 1e-5f);
            Assert.AreEqual(20f / 40f, uv0.height, 1e-5f);

            var crop = SpriteSheetProfile.GetCellCropST(sheet, 1);
            Assert.AreEqual(25f / 100f, crop.x, 1e-5f);
            Assert.AreEqual(18f / 40f, crop.y, 1e-5f);
            Assert.AreEqual(60f / 100f, crop.z, 1e-5f);
            Assert.AreEqual(8f / 40f, crop.w, 1e-5f);

            Assert.IsTrue(SpriteSheetProfile.TryGetActiveCellPixels(sheet, 0, out float w, out float h));
            Assert.AreEqual(30f, w, 1e-5f);
            Assert.AreEqual(20f, h, 1e-5f);

            Object.DestroyImmediate(tex);
        }

        [Test]
        public void GridModeIgnoresStoredCropRects()
        {
            var tex = new Texture2D(100, 40, TextureFormat.RGBA32, false);
            var sheet = new SpriteSheetDef
            {
                Texture = tex,
                Columns = 2,
                Rows = 1,
                CellLayoutMode = SpriteSheetCellLayoutMode.Grid,
                CroppedCellRects = new[]
                {
                    new RectInt(10, 5, 30, 20),
                    new RectInt(60, 8, 25, 18),
                },
            };

            var uv = SpriteSheetProfile.GetCellUvRect(sheet, 0);
            Assert.AreEqual(0f, uv.x, 1e-5f);
            Assert.AreEqual(0f, uv.y, 1e-5f);
            Assert.AreEqual(0.5f, uv.width, 1e-5f);
            Assert.AreEqual(1f, uv.height, 1e-5f);

            Object.DestroyImmediate(tex);
        }

        [Test]
        public void BuildCroppedCellRectsTightensOpaqueBoundsInsideBand()
        {
            const int w = 20;
            const int h = 10;
            var pixels = new Color32[w * h];
            // Left cell band [0..10): opaque island at (2,2)-(5,6)
            // Right cell band [10..20): opaque island at (12,1)-(16,7)
            for (int y = 2; y <= 6; y++)
                for (int x = 2; x <= 5; x++)
                    pixels[y * w + x] = new Color32(255, 255, 255, 255);
            for (int y = 1; y <= 7; y++)
                for (int x = 12; x <= 16; x++)
                    pixels[y * w + x] = new Color32(255, 255, 255, 255);

            var rects = SpriteSheetProfile.BuildCroppedCellRects(pixels, w, h, 2, 1, 8);
            Assert.AreEqual(2, rects.Length);
            Assert.AreEqual(new RectInt(2, 2, 4, 5), rects[0]);
            Assert.AreEqual(new RectInt(12, 1, 5, 7), rects[1]);
        }
    }
}

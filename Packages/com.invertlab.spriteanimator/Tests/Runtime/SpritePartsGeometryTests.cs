using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePartsGeometryTests
    {
        Texture2D _swordTex;
        Texture2D _spearTex;

        [SetUp]
        public void SetUp()
        {
            // 32px cell @ 32 PPU => 1 world unit
            _swordTex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            // 96px cell @ 48 PPU => 2 world units
            _spearTex = new Texture2D(96, 96, TextureFormat.RGBA32, false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_swordTex);
            Object.DestroyImmediate(_spearTex);
        }

        SpriteSheetProfile Profile()
        {
            return new SpriteSheetProfile
            {
                AnimKind = SpriteAnimKind.Parts,
                Sheets = new List<SpriteSheetDef>
                {
                    new SpriteSheetDef
                    {
                        Name = "Sword",
                        Texture = _swordTex,
                        Columns = 1,
                        Rows = 1,
                        PixelsPerUnit = 32f,
                        Pivot = new Vector2(0.5f, 0.5f),
                        CellLayoutMode = SpriteSheetCellLayoutMode.Grid,
                    },
                    new SpriteSheetDef
                    {
                        Name = "Spear",
                        Texture = _spearTex,
                        Columns = 1,
                        Rows = 1,
                        PixelsPerUnit = 48f,
                        Pivot = new Vector2(0.5f, 0.5f),
                        CellLayoutMode = SpriteSheetCellLayoutMode.Grid,
                    },
                },
            };
        }

        [Test]
        public void MixedSizeAndPpuResolveCorrectLogicalSize()
        {
            var profile = Profile();
            var sword = new SpritePartAppearanceDef
            {
                AppearanceId = "sword",
                SheetIndex = 0,
                CellIndex = 0,
            };
            var spear = new SpritePartAppearanceDef
            {
                AppearanceId = "spear",
                SheetIndex = 1,
                CellIndex = 0,
            };

            Assert.IsTrue(SpritePartsGeometry.TryResolve(profile, sword, false, out var s, out var e), e);
            Assert.IsTrue(SpritePartsGeometry.TryResolve(profile, spear, false, out var p, out e), e);

            Assert.AreEqual(1f, s.LogicalWorldSize.x, 1e-4f);
            Assert.AreEqual(1f, s.LogicalWorldSize.y, 1e-4f);
            Assert.AreEqual(2f, p.LogicalWorldSize.x, 1e-4f);
            Assert.AreEqual(2f, p.LogicalWorldSize.y, 1e-4f);
        }

        [Test]
        public void OffCenterGripChangesOffsetNotJointContractIndependence()
        {
            var profile = Profile();
            var centered = new SpritePartAppearanceDef
            {
                AppearanceId = "sword",
                SheetIndex = 0,
                CellIndex = 0,
                PivotSource = SpritePartPivotSource.Override,
                PivotOverride = new Vector2(0.5f, 0.5f),
            };
            var grip = new SpritePartAppearanceDef
            {
                AppearanceId = "sword.grip",
                SheetIndex = 0,
                CellIndex = 0,
                PivotSource = SpritePartPivotSource.Override,
                PivotOverride = new Vector2(0.2f, 0.1f),
            };

            Assert.IsTrue(SpritePartsGeometry.TryResolve(profile, centered, false, out var c, out var err), err);
            Assert.IsTrue(SpritePartsGeometry.TryResolve(profile, grip, false, out var g, out err), err);

            Assert.AreEqual(0f, c.FrameOffset.x, 1e-5f);
            Assert.AreEqual(0f, c.FrameOffset.y, 1e-5f);
            // Offset = (0.5 - pivot) * size; size=1 => (0.3, 0.4)
            Assert.AreEqual(0.3f, g.FrameOffset.x, 1e-5f);
            Assert.AreEqual(0.4f, g.FrameOffset.y, 1e-5f);

            // Joint sample unchanged by appearance swap (sampler ignores appearance).
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Weapon",
                    SlotId = "weapon",
                    RestPosition = new float2(0.5f, 0f),
                    RestRotation = 15f,
                    RestScale = new float2(1f, 1f),
                    DrawRank = 0,
                },
            };
            var blob = SpritePartsSetBuilder.Build(Unity.Collections.Allocator.Temp, slots,
                System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                System.Array.Empty<SpritePartsSetBuilder.ClipInput>(),
                System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
            try
            {
                SpritePartsSampler.SampleSlot(ref blob.Value, -1, 0, 0f, out var before);
                SpritePartsSampler.SampleSlot(ref blob.Value, -1, 0, 0f, out var after);
                Assert.AreEqual(before.Position.x, after.Position.x, 1e-6f);
                Assert.AreEqual(before.Rotation, after.Rotation, 1e-6f);
                Assert.AreEqual(0.5f, before.Position.x, 1e-6f);
                Assert.AreEqual(15f, before.Rotation, 1e-6f);
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void RejectsRotatedPacking()
        {
            var profile = Profile();
            var app = new SpritePartAppearanceDef { AppearanceId = "x", SheetIndex = 0, CellIndex = 0 };
            Assert.IsFalse(SpritePartsGeometry.TryResolve(profile, app, rotatedPacking: true,
                out _, out string error));
            StringAssert.Contains("rotated", error.ToLowerInvariant());
        }

        [Test]
        public void FrameScaleAccountsForAspectOnce()
        {
            var profile = Profile();
            // Force non-square logical size
            var app = new SpritePartAppearanceDef
            {
                AppearanceId = "wide",
                SheetIndex = 0,
                CellIndex = 0,
                LogicalWorldSize = new Vector2(2f, 1f),
                PivotSource = SpritePartPivotSource.Override,
                PivotOverride = new Vector2(0.5f, 0.5f),
            };
            Assert.IsTrue(SpritePartsGeometry.TryResolve(profile, app, false, out var r, out var e), e);
            Assert.AreEqual(2f, r.Aspect, 1e-5f);
            // The square sheet's shader aspect is 1, even though this part is 2:1.
            Assert.AreEqual(2f, r.FrameScale.x, 1e-5f);
            Assert.AreEqual(1f, r.FrameScale.y, 1e-5f);
        }

        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(1, 2)]
        public void CroppedPartRenderedDimensionsMatchEditorGeometry(int columns, int rows)
        {
            var profile = Profile();
            var sheet = profile.Sheets[0];
            sheet.Columns = columns;
            sheet.Rows = rows;
            sheet.CellLayoutMode = SpriteSheetCellLayoutMode.Cropped;
            sheet.CroppedCellRects = new RectInt[columns * rows];
            sheet.CroppedCellRects[0] = new RectInt(2, 3, 10, 20);
            var app = new SpritePartAppearanceDef { SheetIndex = 0, CellIndex = 0,
                LogicalWorldSize = new Vector2(1.1f, 1.45f) };
            Assert.IsTrue(SpritePartsGeometry.TryResolve(profile, app, false, out var geometry, out var error), error);
            float shaderAspect = SpriteSheetProfile.GetCellAspect(sheet.Texture, columns, rows);
            Assert.AreEqual(geometry.LogicalWorldSize.x, geometry.FrameScale.x * shaderAspect, 1e-5f);
            Assert.AreEqual(geometry.LogicalWorldSize.y, geometry.FrameScale.y, 1e-5f);
        }

        [Test]
        public void VisualPointFormulaMatchesContract()
        {
            float2 pivot = new float2(0.2f, 0.1f);
            float2 size = new float2(2f, 4f);
            float2 q = new float2(-0.5f, -0.5f); // bottom-left of normalized quad
            float2 p = SpritePartsGeometry.VisualPoint(q, pivot, size);
            // ( -0.5 + 0.5 - 0.2 , -0.5 + 0.5 - 0.1 ) * size = (-0.2, -0.1) * size
            Assert.AreEqual(-0.4f, p.x, 1e-5f);
            Assert.AreEqual(-0.4f, p.y, 1e-5f);
        }
    }
}

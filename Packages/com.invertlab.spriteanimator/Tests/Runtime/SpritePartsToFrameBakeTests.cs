using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePartsToFrameBakeTests
    {
        Texture2D _tex;

        [SetUp]
        public void SetUp()
        {
            _tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var pixels = new Color32[16 * 16];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 0, 0, 255);
            _tex.SetPixels32(pixels);
            _tex.Apply(false, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_tex != null)
                Object.DestroyImmediate(_tex);
        }

        SpriteSheetProfile Profile(float duration)
        {
            var p = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(p);
            p.PartsClips[0].Duration = duration;
            p.Sheets.Add(new SpriteSheetDef
            {
                Name = "BodySheet",
                Texture = _tex,
                Columns = 1,
                Rows = 1,
                PixelsPerUnit = 16f,
                Pivot = new Vector2(0.5f, 0.5f),
            });
            p.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Body",
                AppearanceId = "body.default",
                SheetIndex = 0,
                CellIndex = 0,
            });
            SpritePartsAuthoringOps.FindSlot(p, "body").DefaultAppearanceId = "body.default";
            SpritePartsValidation.CanonicalizeIds(p);
            return p;
        }

        [Test]
        public void Bake_ProducesExpectedFrameCount_AndNonEmptyCells()
        {
            var profile = Profile(1f);
            var kindBefore = profile.AnimKind;
            int clipsBefore = profile.Clips.Count;
            int sheetsBefore = profile.Sheets.Count;

            var plan = SpritePartsToFrameBake.PlanBake(profile, 0, 12f);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(12, plan.FrameCount);
            Assert.Greater(plan.Unsupported.Count, 0, "Unsupported features must be listed.");

            var result = SpritePartsToFrameBake.Apply(plan, profile);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(kindBefore, profile.AnimKind, "Bake must not switch AnimKind.");
            Assert.AreEqual(clipsBefore + 1, profile.Clips.Count);
            Assert.AreEqual(sheetsBefore + 1, profile.Sheets.Count);

            var clip = profile.Clips[result.ClipIndex];
            Assert.AreEqual(12, clip.Frames.Length);
            Assert.NotNull(clip.Frames);
            Assert.Greater(clip.Frames.Length, 0);

            var sheet = profile.Sheets[result.SheetIndex];
            Assert.NotNull(sheet.Texture);
            var pixels = sheet.Texture.GetPixels32();
            bool any = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a > 0)
                {
                    any = true;
                    break;
                }
            }
            Assert.IsTrue(any, "Baked cells must contain visible pixels.");
            Object.DestroyImmediate(result.Texture);
        }

        [Test]
        public void Bake_DurationTimesFps_RoundsToFrameCount()
        {
            var profile = Profile(0.5f);
            var plan = SpritePartsToFrameBake.PlanBake(profile, 0, 10f);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(5, plan.FrameCount);
            var result = SpritePartsToFrameBake.Apply(plan, profile);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(5, profile.Clips[result.ClipIndex].Frames.Length);
            Object.DestroyImmediate(result.Texture);
        }

        [Test]
        public void Bake_IsOneUndoTransaction()
        {
            var profile = Profile(1f);
            var asset = ScriptableObject.CreateInstance<ScriptableSpriteSheetProfile>();
            asset.Data = profile;
            Texture2D baked = null;
            try
            {
                int clipsBefore = profile.Clips.Count;
                int sheetsBefore = profile.Sheets.Count;
                Undo.RegisterCompleteObjectUndo(asset, "Bake Parts Clip to Frame Clip");
                var plan = SpritePartsToFrameBake.PlanBake(profile, 0, 8f);
                var result = SpritePartsToFrameBake.Apply(plan, profile);
                Assert.IsTrue(result.Ok, result.Reason);
                baked = result.Texture;
                Assert.AreEqual(clipsBefore + 1, asset.Data.Clips.Count);
                Assert.AreEqual(sheetsBefore + 1, asset.Data.Sheets.Count);
                Undo.PerformUndo();
                Assert.AreEqual(clipsBefore, asset.Data.Clips.Count);
                Assert.AreEqual(sheetsBefore, asset.Data.Sheets.Count);
            }
            finally
            {
                Undo.ClearAll();
                Object.DestroyImmediate(asset);
                if (baked != null)
                    Object.DestroyImmediate(baked);
            }
        }
    }
}

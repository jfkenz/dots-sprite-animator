using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePartsValidationTests
    {
        Texture2D _tex;

        [SetUp]
        public void SetUp()
        {
            _tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_tex != null)
                Object.DestroyImmediate(_tex);
        }

        SpriteSheetProfile MinimalValid()
        {
            var profile = new SpriteSheetProfile
            {
                AnimKind = SpriteAnimKind.Parts,
                Sheets = new List<SpriteSheetDef>
                {
                    new SpriteSheetDef
                    {
                        Name = "Body",
                        Texture = _tex,
                        Columns = 1,
                        Rows = 1,
                        PixelsPerUnit = 32f,
                        Pivot = new Vector2(0.5f, 0.5f),
                    },
                },
            };
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Body",
                AppearanceId = "body",
                SheetIndex = 0,
                CellIndex = 0,
                LogicalWorldSize = new Vector2(1f, 1f),
            });
            profile.PartsSlots.Add(new SpritePartSlotDef
            {
                Name = "Body",
                SlotId = "body",
                DefaultAppearanceId = "body",
                DrawRank = 0,
                RestScale = Vector2.one,
            });
            profile.PartsClips.Add(new SpritePartsClipDef
            {
                Name = "Idle",
                ClipId = "idle",
                Duration = 1f,
                Speed = 1f,
                WrapMode = (byte)SpritePartsWrap.Loop,
            });
            return profile;
        }

        [Test]
        public void RejectsCycleAndMissingParent()
        {
            var profile = MinimalValid();
            profile.PartsSlots.Add(new SpritePartSlotDef
            {
                Name = "Hand",
                SlotId = "hand",
                ParentSlotId = "missing",
                DefaultAppearanceId = "body",
                DrawRank = 1,
                RestScale = Vector2.one,
            });
            var missing = SpritePartsValidation.Validate(profile);
            Assert.IsFalse(missing.Ok);
            StringAssert.Contains("missing", string.Join(" ", missing.Errors).ToLowerInvariant());

            profile.PartsSlots[1].ParentSlotId = "hand";
            var cycle = SpritePartsValidation.Validate(profile);
            Assert.IsFalse(cycle.Ok);
            StringAssert.Contains("cycle", string.Join(" ", cycle.Errors).ToLowerInvariant());
        }

        [Test]
        public void RejectsMoreThan32Slots()
        {
            var profile = MinimalValid();
            for (int i = 1; i < 33; i++)
            {
                profile.PartsSlots.Add(new SpritePartSlotDef
                {
                    Name = "P" + i,
                    SlotId = "p" + i,
                    DefaultAppearanceId = "body",
                    DrawRank = i % 32,
                    RestScale = Vector2.one,
                });
            }
            // Fix unique ranks for first 32 then add 33rd
            for (int i = 0; i < profile.PartsSlots.Count && i < 32; i++)
                profile.PartsSlots[i].DrawRank = i;
            var result = SpritePartsValidation.Validate(profile);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("32", string.Join(" ", result.Errors));
        }

        [Test]
        public void RejectsInvalidScaleTimeAndDuplicateTracks()
        {
            var profile = MinimalValid();
            profile.PartsSlots[0].RestScale = new Vector2(0f, 1f);
            var scale = SpritePartsValidation.Validate(profile);
            Assert.IsFalse(scale.Ok);

            profile = MinimalValid();
            profile.PartsClips[0].Tracks.Add(new SpritePartsTrackDef
            {
                SlotId = "body",
                Keys = new List<SpritePartsKeyDef>
                {
                    new SpritePartsKeyDef { Time = 2f, Scale = Vector2.one },
                },
            });
            var time = SpritePartsValidation.Validate(profile);
            Assert.IsFalse(time.Ok);
            StringAssert.Contains("time", string.Join(" ", time.Errors).ToLowerInvariant());

            profile = MinimalValid();
            profile.PartsClips[0].Tracks.Add(new SpritePartsTrackDef { SlotId = "body" });
            profile.PartsClips[0].Tracks.Add(new SpritePartsTrackDef { SlotId = "body" });
            var dup = SpritePartsValidation.Validate(profile);
            Assert.IsFalse(dup.Ok);
            StringAssert.Contains("duplicate", string.Join(" ", dup.Errors).ToLowerInvariant());
        }

        [Test]
        public void RejectsDuplicateIds()
        {
            var profile = MinimalValid();
            profile.PartsSlots.Add(new SpritePartSlotDef
            {
                Name = "Body2",
                SlotId = "body",
                DefaultAppearanceId = "body",
                DrawRank = 1,
                RestScale = Vector2.one,
            });
            var result = SpritePartsValidation.Validate(profile);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("duplicate", string.Join(" ", result.Errors).ToLowerInvariant());
        }

        [Test]
        public void ValidMinimalProfilePasses()
        {
            var result = SpritePartsValidation.Validate(MinimalValid());
            Assert.IsTrue(result.Ok, string.Join(" | ", result.Errors));
        }
    }
}

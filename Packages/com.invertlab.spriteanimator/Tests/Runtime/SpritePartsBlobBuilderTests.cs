using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePartsBlobBuilderTests
    {
        Texture2D _tex;

        [SetUp]
        public void SetUp() => _tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);

        [TearDown]
        public void TearDown()
        {
            if (_tex != null)
                Object.DestroyImmediate(_tex);
        }

        SpriteSheetProfile Profile()
        {
            var profile = new SpriteSheetProfile
            {
                AnimKind = SpriteAnimKind.Parts,
                Sheets = new List<SpriteSheetDef>
                {
                    new SpriteSheetDef
                    {
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
                AppearanceId = "body",
                SheetIndex = 0,
                CellIndex = 0,
            });
            profile.PartsSlots.Add(new SpritePartSlotDef
            {
                Name = "Body",
                SlotId = "body",
                DefaultAppearanceId = "body",
                DrawRank = 0,
                RestScale = Vector2.one,
            });
            profile.PartsSlots.Add(new SpritePartSlotDef
            {
                Name = "Hand",
                SlotId = "hand",
                ParentSlotId = "body",
                DefaultAppearanceId = "body",
                DrawRank = 1,
                RestPosition = new Vector2(1f, 0f),
                RestScale = Vector2.one,
            });
            profile.PartsClips.Add(new SpritePartsClipDef
            {
                Name = "Walk",
                ClipId = "walk",
                Duration = 1f,
                Speed = 1.5f,
                WrapMode = (byte)SpritePartsWrap.Loop,
                Tracks = new List<SpritePartsTrackDef>
                {
                    new SpritePartsTrackDef
                    {
                        SlotId = "body",
                        Keys = new List<SpritePartsKeyDef>
                        {
                            new SpritePartsKeyDef
                            {
                                Time = 0f,
                                Position = Vector2.zero,
                                Scale = Vector2.one,
                                EaseMode = (byte)SpriteEaseMode.Linear,
                            },
                            new SpritePartsKeyDef
                            {
                                Time = 1f,
                                Position = new Vector2(2f, 0f),
                                Scale = Vector2.one,
                                EaseMode = (byte)SpriteEaseMode.SmoothStep,
                            },
                        },
                    },
                },
            });
            profile.PartsSkins.Add(new SpritePartsSkinDef
            {
                Name = "Alt",
                SkinId = "alt",
                Bindings = new List<SpritePartsSkinBindingDef>
                {
                    new SpritePartsSkinBindingDef { SlotId = "hand", AppearanceId = "body" },
                },
            });
            return profile;
        }

        [Test]
        public void TryBuildBlobStoresSpeedDenseLookupAndAppearances()
        {
            var profile = Profile();
            Assert.IsTrue(SpritePartsClipConversion.TryBuildBlob(profile, Allocator.Temp,
                out var blob, out var error), error);
            try
            {
                ref var set = ref blob.Value;
                Assert.AreEqual(2, set.Slots.Length);
                Assert.AreEqual(1, set.Clips.Length);
                Assert.AreEqual(1, set.Appearances.Length);
                Assert.AreEqual(1, set.SkinPatches.Length);
                Assert.AreEqual(1.5f, set.Clips[0].SpeedMultiplier, 1e-5f);
                Assert.AreEqual(2, set.Clips[0].SlotTrackIndices.Length);
                Assert.AreEqual(0, set.Clips[0].SlotTrackIndices[0]); // body track
                Assert.AreEqual(-1, set.Clips[0].SlotTrackIndices[1]); // hand missing track
                Assert.AreEqual(1f, set.Appearances[0].LogicalWorldSize.x, 1e-4f);

                SpritePartsSampler.SampleSlot(ref set, 0, 0, 0.25f, out var pose);
                Assert.AreEqual(0.5f, pose.Position.x, 1e-5f); // lerp(0,2,0.25)
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void HiddenSlotCopiesToBlob()
        {
            var profile = Profile();
            profile.PartsSlots[1].Enabled = false;
            Assert.IsTrue(SpritePartsClipConversion.TryBuildBlob(profile, Allocator.Temp,
                out var blob, out var error), error);
            try
            {
                Assert.AreEqual(0, blob.Value.Slots[0].Hidden);
                Assert.AreEqual(1, blob.Value.Slots[1].Hidden);
            }
            finally { blob.Dispose(); }

            profile = Profile();
            profile.PartsSlots[0].Enabled = false;
            Assert.IsTrue(SpritePartsClipConversion.TryBuildBlob(profile, Allocator.Temp,
                out blob, out error), error);
            try
            {
                Assert.AreEqual(1, blob.Value.Slots[0].Hidden);
                Assert.AreEqual(1, blob.Value.Slots[1].Hidden);
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void DeletingBodySlotStillBuilds()
        {
            var profile = Profile();
            profile.PartsSlots.RemoveAt(0);
            Assert.IsTrue(SpritePartsValidation.Validate(profile).Ok,
                string.Join(" | ", SpritePartsValidation.Validate(profile).Errors));
            Assert.IsTrue(SpritePartsClipConversion.TryBuildBlob(profile, Allocator.Temp,
                out var blob, out var error), error);
            try
            {
                Assert.AreEqual(1, blob.Value.Slots.Length);
                Assert.AreEqual(-1, blob.Value.Slots[0].ParentSlotIndex);
                Assert.AreEqual(0, blob.Value.Clips[0].Tracks.Length);
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void ContractTypesAreIComponentData()
        {
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(SpritePartsPlayer)));
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(SpritePartsSetRef)));
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(SpritePartSlot)));
            Assert.IsTrue(typeof(IBufferElementData).IsAssignableFrom(typeof(SpritePartLink)));
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(SpritePartRenderDepth)));
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(SpritePartsDrawGroup)));
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(SpritePartAppearanceState)));
        }

        [Test]
        public void BuilderRejectsDuplicateTracks()
        {
            Assert.Throws<System.ArgumentException>(() =>
            {
                SpritePartsSetBuilder.Build(Allocator.Temp,
                    new[]
                    {
                        new SpritePartsSetBuilder.SlotInput
                        {
                            SlotId = "body", Name = "Body",
                            RestScale = new float2(1f, 1f), DrawRank = 0,
                        },
                    },
                    System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                    new[]
                    {
                        new SpritePartsSetBuilder.ClipInput
                        {
                            Name = "Walk", ClipId = "walk", Duration = 1f,
                            SpeedMultiplier = 1f, WrapMode = 0,
                            Tracks = new[]
                            {
                                new SpritePartsSetBuilder.TrackInput { SlotId = "body", Keys = System.Array.Empty<SpritePartsSetBuilder.KeyInput>() },
                                new SpritePartsSetBuilder.TrackInput { SlotId = "body", Keys = System.Array.Empty<SpritePartsSetBuilder.KeyInput>() },
                            },
                        },
                    },
                    System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
            });
        }
    }
}

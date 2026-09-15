using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePartsSpriteKeysTests
    {
        SpriteSheetProfile MakeFloatingWithAppearances()
        {
            var profile = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            SpritePartsAuthoringOps.NormalizeSiblingOrders(profile);
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Sword", AppearanceId = "sword",
                SheetIndex = 0, CellIndex = 0,
                LogicalWorldSize = new Vector2(0.5f, 1f),
            });
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Spear", AppearanceId = "spear",
                SheetIndex = 0, CellIndex = 1,
                LogicalWorldSize = new Vector2(0.4f, 1.5f),
            });
            var weapon = SpritePartsAuthoringOps.FindSlot(profile, "weapon");
            weapon.DefaultAppearanceId = "sword";
            SpritePartsValidation.CanonicalizeIds(profile);
            return profile;
        }

        [Test]
        public void DeleteHotkeyPath_ReportsLockedReason_WithoutMutating()
        {
            var profile = MakeFloatingWithAppearances();
            var hand = SpritePartsAuthoringOps.FindSlot(profile, "hand.r");
            hand.EditorLocked = true;
            int before = profile.PartsSlots.Count;

            var blocked = SpritePartsAuthoringOps.ValidateDeleteSubtree(profile, "hand.r");
            Assert.IsFalse(blocked.Ok);
            StringAssert.Contains("locked", blocked.Reason.ToLowerInvariant());

            string describe = SpritePartsAuthoringOps.DescribeHierarchyEditBlock(
                profile, "hand.r", SpritePartsStudioMode.Animate, "delete");
            StringAssert.Contains("Rig", describe);

            describe = SpritePartsAuthoringOps.DescribeHierarchyEditBlock(
                profile, "hand.r", SpritePartsStudioMode.Rig, "delete");
            StringAssert.Contains("locked", describe.ToLowerInvariant());

            var del = SpritePartsAuthoringOps.TryDeleteSubtree(profile, "hand.r");
            Assert.IsFalse(del.Ok);
            Assert.AreEqual(before, profile.PartsSlots.Count);
        }

        [Test]
        public void DeleteHotkeyPath_SucceedsInRigWhenUnlocked()
        {
            var profile = MakeFloatingWithAppearances();
            Assert.IsNull(SpritePartsAuthoringOps.DescribeHierarchyEditBlock(
                profile, "weapon", SpritePartsStudioMode.Rig, "delete"));
            var del = SpritePartsAuthoringOps.TryDeleteSubtree(profile, "weapon");
            Assert.IsTrue(del.Ok, del.Reason);
            Assert.IsNull(SpritePartsAuthoringOps.FindSlot(profile, "weapon"));
        }

        [Test]
        public void BreakFromParent_PreservesRestWorld()
        {
            var profile = MakeFloatingWithAppearances();
            var weapon = SpritePartsAuthoringOps.FindSlot(profile, "weapon");
            Assert.IsTrue(SpritePartsAuthoringOps.TryBuildRestWorldMatrices(profile, out var before, out _));
            float4x4 worldBefore = before["weapon"];

            var result = SpritePartsAuthoringOps.TryBreakFromParent(
                profile, "weapon", confirmAnimationReview: true);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.IsTrue(string.IsNullOrEmpty(weapon.ParentSlotId));

            Assert.IsTrue(SpritePartsAuthoringOps.TryBuildRestWorldMatrices(profile, out var after, out _));
            float4x4 worldAfter = after["weapon"];
            Assert.AreEqual(worldBefore.c3.x, worldAfter.c3.x, 1e-4f);
            Assert.AreEqual(worldBefore.c3.y, worldAfter.c3.y, 1e-4f);
        }

        [Test]
        public void MoveUpOneLevel_ToGrandparent_PreservesRestWorld()
        {
            var profile = MakeFloatingWithAppearances();
            var weapon = SpritePartsAuthoringOps.FindSlot(profile, "weapon");
            Assert.AreEqual("hand.r", SpritePartIdUtility.Canonical(weapon.ParentSlotId));
            Assert.IsTrue(SpritePartsAuthoringOps.TryBuildRestWorldMatrices(profile, out var before, out _));
            float4x4 worldBefore = before["weapon"];

            var result = SpritePartsAuthoringOps.TryMoveUpOneLevel(
                profile, "weapon", confirmAnimationReview: true);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual("body", SpritePartIdUtility.Canonical(weapon.ParentSlotId));

            Assert.IsTrue(SpritePartsAuthoringOps.TryBuildRestWorldMatrices(profile, out var after, out _));
            float4x4 worldAfter = after["weapon"];
            Assert.AreEqual(worldBefore.c3.x, worldAfter.c3.x, 1e-4f);
            Assert.AreEqual(worldBefore.c3.y, worldAfter.c3.y, 1e-4f);
        }

        [Test]
        public void AppearanceHold_AndChangeSize_DoesNotMoveJoint()
        {
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Weapon", SlotId = "weapon",
                    RestPosition = new float2(1f, 2f), RestRotation = 15f,
                    RestScale = new float2(1f, 1f), DrawRank = 0,
                    DefaultAppearanceId = "sword",
                },
            };
            var appearances = new[]
            {
                new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = "sword",
                    SheetTableIndex = 0, CellIndex = 0,
                    LogicalWorldSize = new float2(0.5f, 1f),
                    Pivot = new float2(0.5f, 0.5f),
                    FrameOffset = float2.zero, FrameScale = new float2(1f, 1f),
                },
                new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = "spear",
                    SheetTableIndex = 0, CellIndex = 1,
                    LogicalWorldSize = new float2(0.4f, 2f),
                    Pivot = new float2(0.5f, 0.1f),
                    FrameOffset = new float2(0f, 0.2f), FrameScale = new float2(1.2f, 1.2f),
                },
            };
            var clips = new[]
            {
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "Swap", ClipId = "swap", Duration = 1f, SpeedMultiplier = 1f,
                    WrapMode = (byte)SpritePartsWrap.Loop,
                    Tracks = new[]
                    {
                        new SpritePartsSetBuilder.TrackInput
                        {
                            SlotId = "weapon",
                            Keys = new[]
                            {
                                new SpritePartsSetBuilder.KeyInput
                                {
                                    Time = 0f,
                                    Position = new float2(1f, 2f), Rotation = 15f,
                                    Scale = new float2(1f, 1f),
                                    EaseMode = (byte)SpriteEaseMode.Linear,
                                    AppearanceId = "sword",
                                },
                                new SpritePartsSetBuilder.KeyInput
                                {
                                    Time = 0.4f,
                                    Position = new float2(1f, 2f), Rotation = 15f,
                                    Scale = new float2(1f, 1f),
                                    EaseMode = (byte)SpriteEaseMode.Linear,
                                    AppearanceId = string.Empty, // hold sword
                                },
                                new SpritePartsSetBuilder.KeyInput
                                {
                                    Time = 0.7f,
                                    Position = new float2(1f, 2f), Rotation = 15f,
                                    Scale = new float2(1f, 1f),
                                    EaseMode = (byte)SpriteEaseMode.Linear,
                                    AppearanceId = "spear",
                                },
                            },
                        },
                    },
                },
            };

            var blob = SpritePartsSetBuilder.Build(
                Allocator.Temp, slots, appearances, clips,
                System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
            try
            {
                // Joint TRS unchanged across sprite keys.
                SpritePartsSampler.SampleSlot(ref blob.Value, 0, 0, 0.1f, out var a);
                SpritePartsSampler.SampleSlot(ref blob.Value, 0, 0, 0.5f, out var b);
                SpritePartsSampler.SampleSlot(ref blob.Value, 0, 0, 0.9f, out var c);
                Assert.AreEqual(1f, a.Position.x, 1e-5f);
                Assert.AreEqual(1f, b.Position.x, 1e-5f);
                Assert.AreEqual(1f, c.Position.x, 1e-5f);
                Assert.AreEqual(15f, a.Rotation, 1e-5f);
                Assert.AreEqual(15f, c.Rotation, 1e-5f);

                int app0 = SpritePartsSampler.SampleAppearanceIndex(ref blob.Value, 0, 0, 0.1f);
                int appHold = SpritePartsSampler.SampleAppearanceIndex(ref blob.Value, 0, 0, 0.5f);
                int appSpear = SpritePartsSampler.SampleAppearanceIndex(ref blob.Value, 0, 0, 0.9f);
                Assert.AreEqual(0, app0); // sword
                Assert.AreEqual(0, appHold); // hold
                Assert.AreEqual(1, appSpear); // spear

                Assert.AreEqual(0.5f, blob.Value.Appearances[app0].LogicalWorldSize.x, 1e-5f);
                Assert.AreEqual(0.4f, blob.Value.Appearances[appSpear].LogicalWorldSize.x, 1e-5f);
                Assert.AreNotEqual(
                    blob.Value.Appearances[app0].LogicalWorldSize.y,
                    blob.Value.Appearances[appSpear].LogicalWorldSize.y);
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void AppearanceLoopWrap_CarriesLastKeyedSprite()
        {
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput
                {
                    Name = "Body", SlotId = "body",
                    RestPosition = float2.zero, RestScale = new float2(1f, 1f),
                    DefaultAppearanceId = "a",
                },
            };
            var appearances = new[]
            {
                new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = "a", SheetTableIndex = 0, CellIndex = 0,
                    LogicalWorldSize = new float2(1f, 1f), Pivot = new float2(0.5f, 0.5f),
                    FrameOffset = float2.zero, FrameScale = new float2(1f, 1f),
                },
                new SpritePartsSetBuilder.AppearanceInput
                {
                    AppearanceId = "b", SheetTableIndex = 0, CellIndex = 1,
                    LogicalWorldSize = new float2(2f, 2f), Pivot = new float2(0.5f, 0.5f),
                    FrameOffset = float2.zero, FrameScale = new float2(1f, 1f),
                },
            };
            var loop = SpritePartsSetBuilder.Build(
                Allocator.Temp, slots, appearances,
                new[]
                {
                    new SpritePartsSetBuilder.ClipInput
                    {
                        Name = "L", ClipId = "l", Duration = 1f, SpeedMultiplier = 1f,
                        WrapMode = (byte)SpritePartsWrap.Loop,
                        Tracks = new[]
                        {
                            new SpritePartsSetBuilder.TrackInput
                            {
                                SlotId = "body",
                                Keys = new[]
                                {
                                    new SpritePartsSetBuilder.KeyInput
                                    {
                                        Time = 0.6f, Position = float2.zero, Scale = new float2(1f, 1f),
                                        EaseMode = (byte)SpriteEaseMode.Linear, AppearanceId = "b",
                                    },
                                },
                            },
                        },
                    },
                },
                System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
            var once = SpritePartsSetBuilder.Build(
                Allocator.Temp, slots, appearances,
                new[]
                {
                    new SpritePartsSetBuilder.ClipInput
                    {
                        Name = "O", ClipId = "o", Duration = 1f, SpeedMultiplier = 1f,
                        WrapMode = (byte)SpritePartsWrap.Once,
                        Tracks = new[]
                        {
                            new SpritePartsSetBuilder.TrackInput
                            {
                                SlotId = "body",
                                Keys = new[]
                                {
                                    new SpritePartsSetBuilder.KeyInput
                                    {
                                        Time = 0.6f, Position = float2.zero, Scale = new float2(1f, 1f),
                                        EaseMode = (byte)SpriteEaseMode.Linear, AppearanceId = "b",
                                    },
                                },
                            },
                        },
                    },
                },
                System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
            try
            {
                // Wrapped 1.1 -> 0.1: Loop carries last keyed (b); Once has no carry before first key.
                Assert.AreEqual(1, SpritePartsSampler.SampleAppearanceIndex(ref loop.Value, 0, 0, 1.1f));
                Assert.AreEqual(-1, SpritePartsSampler.SampleAppearanceIndex(ref once.Value, 0, 0, 0.1f));
                Assert.AreEqual(1, SpritePartsSampler.SampleAppearanceIndex(ref once.Value, 0, 0, 0.7f));
            }
            finally
            {
                loop.Dispose();
                once.Dispose();
            }
        }

        [Test]
        public void AuthoringWriteKeySprite_AndSampleKeyedAppearanceId()
        {
            var profile = MakeFloatingWithAppearances();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            var pose = new SpritePartsAuthoringOps.PoseEdit
            {
                Position = new Vector2(0.25f, 0f), Rotation = 0f, Scale = Vector2.one,
            };
            var keyed = SpritePartsAuthoringOps.WriteKeySprite(
                profile, walk, "weapon", 0.2f, "spear", pose);
            Assert.IsTrue(keyed.WroteAppearance, keyed.Reason);
            Assert.AreEqual("spear",
                SpritePartsAuthoringOps.SampleKeyedAppearanceId(profile, walk, "weapon", 0.25f));

            // Empty appearance at later time does not clear prior (hold).
            var hold = SpritePartsAuthoringOps.WriteKeySprite(
                profile, walk, "weapon", 0.4f, string.Empty, pose);
            Assert.IsTrue(hold.WroteAppearance, hold.Reason);
            Assert.AreEqual("spear",
                SpritePartsAuthoringOps.SampleKeyedAppearanceId(profile, walk, "weapon", 0.45f));
        }
    }
}
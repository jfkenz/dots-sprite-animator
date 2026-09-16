using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePartsEditorOpsTests
    {
        SpriteSheetProfile MakeFloating()
        {
            var profile = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            return profile;
        }

        [Test]
        public void AnimateDragWritesKeyNotRest()
        {
            var profile = MakeFloating();
            var body = SpritePartsAuthoringOps.FindSlot(profile, "body");
            Vector2 restBefore = body.RestPosition;
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            Assert.GreaterOrEqual(walk, 0);

            var result = SpritePartsAuthoringOps.ApplyPoseEdit(
                profile, SpritePartsStudioMode.Animate, walk, "body", 0.2f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(5f, 6f),
                    Rotation = 15f,
                    Scale = Vector2.one,
                },
                autoKey: true);

            Assert.IsFalse(result.Rejected, result.Reason);
            Assert.IsTrue(result.WroteKey);
            Assert.IsFalse(result.WroteRest);
            Assert.AreEqual(restBefore, body.RestPosition);
            var track = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], "body");
            Assert.IsNotNull(track);
            Assert.Greater(track.Keys.Count, 0);
            Assert.IsTrue(result.InsertedRestAnchorAtZero);
        }

        [Test]
        public void RigDragWritesRestNotKeys()
        {
            var profile = MakeFloating();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            var result = SpritePartsAuthoringOps.ApplyPoseEdit(
                profile, SpritePartsStudioMode.Rig, walk, "hand.r", 0.1f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(1f, 2f),
                    Rotation = 30f,
                    Scale = new Vector2(1.2f, 1.2f),
                },
                autoKey: true);

            Assert.IsTrue(result.WroteRest);
            Assert.IsFalse(result.WroteKey);
            var hand = SpritePartsAuthoringOps.FindSlot(profile, "hand.r");
            Assert.AreEqual(1f, hand.RestPosition.x, 1e-5f);
            Assert.AreEqual(30f, hand.RestRotation, 1e-5f);
            var track = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], "hand.r");
            Assert.IsTrue(track == null || track.Keys.Count == 0);
        }

        [Test]
        public void SkinsModeRejectsTransformEdits()
        {
            var profile = MakeFloating();
            var result = SpritePartsAuthoringOps.ApplyPoseEdit(
                profile, SpritePartsStudioMode.Skins, 0, "body", 0f,
                new SpritePartsAuthoringOps.PoseEdit { Position = Vector2.one, Scale = Vector2.one },
                autoKey: true);
            Assert.IsTrue(result.Rejected);
            Assert.IsFalse(result.WroteKey);
            Assert.IsFalse(result.WroteRest);
        }

        [Test]
        public void OnionUsesSharedSampler_ParentKeysMoveUnkeyedWeapon()
        {
            var profile = MakeFloating();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            // Parent-only keys on hand.r; weapon has no keys.
            SpritePartsAuthoringOps.ApplyPoseEdit(
                profile, SpritePartsStudioMode.Animate, walk, "hand.r", 0f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(0.35f, 0.1f),
                    Rotation = 0f,
                    Scale = Vector2.one,
                }, autoKey: true);
            SpritePartsAuthoringOps.ApplyPoseEdit(
                profile, SpritePartsStudioMode.Animate, walk, "hand.r", 0.3f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(1.35f, 0.1f),
                    Rotation = 0f,
                    Scale = Vector2.one,
                }, autoKey: true);

            Assert.IsTrue(SpritePartsOnion.TrySampleCharacter(
                profile, walk, 0f, Allocator.Temp,
                out var blob0, out var poses0, out var mats0, out var err0), err0);
            Assert.IsTrue(SpritePartsOnion.TrySampleCharacter(
                profile, walk, 0.3f, Allocator.Temp,
                out var blob1, out var poses1, out var mats1, out var err1), err1);
            try
            {
                int weapon = SpritePartsAuthoringOps.FindSlotIndex(profile, "weapon");
                int hand = SpritePartsAuthoringOps.FindSlotIndex(profile, "hand.r");
                Assert.GreaterOrEqual(weapon, 0);
                // Local weapon pose unchanged (rest).
                Assert.AreEqual(poses0[weapon].Position.x, poses1[weapon].Position.x, 1e-4f);
                // World weapon follows animated parent hand.
                float dxHand = mats1[hand].c3.x - mats0[hand].c3.x;
                float dxWeapon = mats1[weapon].c3.x - mats0[weapon].c3.x;
                Assert.AreEqual(dxHand, dxWeapon, 1e-3f);
                Assert.Greater(math.abs(dxWeapon), 0.5f);
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob0, poses0, mats0);
                SpritePartsOnion.DisposeSample(blob1, poses1, mats1);
            }
        }

        [Test]
        public void SkinPreviewDoesNotSerializeUntilSaveSkin()
        {
            var profile = MakeFloating();
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Sword", AppearanceId = "sword",
                LogicalWorldSize = new Vector2(0.5f, 1f),
            });
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Spear", AppearanceId = "spear",
                LogicalWorldSize = new Vector2(0.4f, 1.5f),
            });
            var weapon = SpritePartsAuthoringOps.FindSlot(profile, "weapon");
            weapon.DefaultAppearanceId = "sword";
            SpritePartsValidation.CanonicalizeIds(profile);

            var preview = new Dictionary<string, string>
            {
                ["weapon"] = "spear",
            };
            // Preview resolve changes returned id without mutating slot.
            Assert.AreEqual("spear",
                SpritePartsAuthoringOps.ResolveAppearanceId(weapon, preview));
            Assert.AreEqual("sword", weapon.DefaultAppearanceId);
            Assert.AreEqual(0, profile.PartsSkins[0].Bindings.Count);

            SpritePartsAuthoringOps.SaveSkinFromPreview(profile, "iron", "Iron Sword", preview);
            Assert.AreEqual("sword", weapon.DefaultAppearanceId);
            var skin = profile.PartsSkins.Find(s => s.SkinId == "iron");
            Assert.IsNotNull(skin);
            Assert.AreEqual(1, skin.Bindings.Count);
            Assert.AreEqual("weapon", skin.Bindings[0].SlotId);
            Assert.AreEqual("spear", skin.Bindings[0].AppearanceId);
        }

        [Test]
        public void PoseEvaluationBlobReusesSpritePartsSampler()
        {
            var profile = MakeFloating();
            Assert.IsTrue(SpritePartsClipConversion.TryBuildPoseEvaluationBlob(
                profile, Allocator.Temp, out var blob, out var error), error);
            try
            {
                SpritePartsSampler.SampleSlot(ref blob.Value, 0, 0, 0.5f, out var pose);
                Assert.AreEqual(0f, pose.Position.x, 1e-5f); // body rest
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void OnionGhostsMatchLivePose_WhenIdleHasNoKeys()
        {
            var profile = MakeFloating();
            int idle = SpritePartsAuthoringOps.FindClipIndex(profile, "idle");
            Assert.GreaterOrEqual(idle, 0);
            Assert.IsTrue(SpritePartsOnion.TrySampleCharacter(
                profile, idle, 0.4f, Allocator.Temp,
                out var liveBlob, out var livePoses, out var liveMats, out var err), err);
            try
            {
                var clip = profile.PartsClips[idle];
                var ghosts = SpritePartsOnion.CollectGhostTimes(
                    0.4f, clip.Duration, clip.WrapMode, 1, 1, 2, 30f, false, false);
                Assert.Greater(ghosts.Count, 0, "Idle should still emit onion times");
                foreach (var g in ghosts)
                {
                    Assert.IsTrue(SpritePartsOnion.TrySampleCharacter(
                        profile, idle, g.Time, Allocator.Temp,
                        out var gb, out var gp, out var gm, out var ge), ge);
                    try
                    {
                        Assert.IsTrue(SpritePartsOnion.MatricesApproximatelyEqual(gm, liveMats),
                            $"ghost t={g.Time} should match live rest pose");
                    }
                    finally
                    {
                        SpritePartsOnion.DisposeSample(gb, gp, gm);
                    }
                }
            }
            finally
            {
                SpritePartsOnion.DisposeSample(liveBlob, livePoses, liveMats);
            }
        }

        [Test]
        public void AssignSlotArt_1x1BindsWholeImageAsDefaultAppearance()
        {
            var profile = MakeFloating();
            var tex = Texture2D.whiteTexture;
            var result = SpritePartsAuthoringOps.AssignSlotArt(profile, "body", tex, 1, 1, 0);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual("body.default", result.AppearanceId);
            var body = SpritePartsAuthoringOps.FindSlot(profile, "body");
            Assert.AreEqual("body.default", body.DefaultAppearanceId);
            var app = SpritePartsAuthoringOps.FindAppearance(profile, body.DefaultAppearanceId);
            Assert.IsNotNull(app);
            Assert.AreEqual(0, app.CellIndex);
            var sheet = profile.SheetAt(app.SheetIndex);
            Assert.IsNotNull(sheet);
            Assert.AreEqual(tex, sheet.Texture);
            Assert.AreEqual(1, sheet.Columns);
            Assert.AreEqual(1, sheet.Rows);

            // Re-assign updates the same appearance, does not spawn a second id.
            var again = SpritePartsAuthoringOps.AssignSlotArt(profile, "body", tex, 1, 1, 0);
            Assert.IsTrue(again.Ok, again.Reason);
            Assert.AreEqual("body.default", again.AppearanceId);
            int defaults = 0;
            for (int i = 0; i < profile.PartsAppearances.Count; i++)
            {
                var a = profile.PartsAppearances[i];
                if (a != null && a.AppearanceId == "body.default")
                    defaults++;
            }
            Assert.AreEqual(1, defaults);
        }

        [Test]
        public void AssignSlotArt_SheetCell_ReusesMatchingGrid()
        {
            var profile = MakeFloating();
            var tex = Texture2D.whiteTexture;
            var a = SpritePartsAuthoringOps.AssignSlotArt(profile, "hand.l", tex, 2, 2, 1);
            var b = SpritePartsAuthoringOps.AssignSlotArt(profile, "hand.r", tex, 2, 2, 2);
            Assert.IsTrue(a.Ok, a.Reason);
            Assert.IsTrue(b.Ok, b.Reason);
            Assert.AreEqual(a.SheetIndex, b.SheetIndex);
            Assert.AreEqual(1, a.CellIndex);
            Assert.AreEqual(2, b.CellIndex);
            Assert.AreNotEqual(a.AppearanceId, b.AppearanceId);
        }
    }
}

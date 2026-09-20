using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>
    /// Runtime-mode switching contract: one owner profile, one active runtime
    /// mode, explicit validated Undoable switches, both data sets preserved.
    /// </summary>
    public sealed class SpriteAnimKindSwitchTests
    {
        static SpriteSheetProfile FrameProfileWithPlayableClip()
        {
            var profile = new SpriteSheetProfile();
            profile.Sheets.Add(new SpriteSheetDef
            {
                Name = "Sheet",
                Texture = new Texture2D(8, 8),
                Columns = 2,
                Rows = 2,
                PixelsPerUnit = 32f,
                Pivot = new Vector2(0.5f, 0.5f),
            });
            profile.Clips.Add(new SpriteClipDef { Name = "Idle", SheetIndex = 0, Frames = new[] { 0, 1 } });
            profile.EnsureSheets();
            return profile;
        }

        [Test]
        public void NewProfile_DefaultsToFrameKind()
        {
            Assert.AreEqual(SpriteAnimKind.Frame, new SpriteSheetProfile().AnimKind);
        }

        [Test]
        public void SwitchToParts_RejectedWithoutRig_DataUntouched()
        {
            var profile = FrameProfileWithPlayableClip();
            var result = SpritePartsAuthoringOps.TrySetAnimKind(profile, SpriteAnimKind.Parts);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("slots", result.Reason);
            Assert.AreEqual(SpriteAnimKind.Frame, profile.AnimKind);
            Assert.AreEqual(1, profile.Clips.Count, "Frame data must be preserved.");
        }

        [Test]
        public void SwitchToParts_RejectedWhenRigInvalid_ReasonExplainsFix()
        {
            var profile = FrameProfileWithPlayableClip();
            SpritePartsAuthoringOps.CreateEmptyPartsRig(profile);
            // Empty rig has no slots: activation must be rejected with a fix,
            // not silently bake an unusable character.
            var bad = SpritePartsAuthoringOps.FindSlot(profile, "body") == null;
            Assert.IsTrue(bad);
            profile.AnimKind = SpriteAnimKind.Parts;

            var result = SpritePartsAuthoringOps.TrySetAnimKind(profile, SpriteAnimKind.Frame);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(SpriteAnimKind.Frame, profile.AnimKind);

            var back = SpritePartsAuthoringOps.TrySetAnimKind(profile, SpriteAnimKind.Parts);
            Assert.IsFalse(back.Ok);
            StringAssert.Contains("no slots", back.Reason);
            Assert.AreEqual(SpriteAnimKind.Frame, profile.AnimKind, "Rejected switch must not change the mode.");
            Assert.AreEqual(1, profile.PartsClips.Count, "Parts data must be preserved for later editing.");
        }

        [Test]
        public void SwitchToParts_ValidRig_Succeeds()
        {
            var profile = FrameProfileWithPlayableClip();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            Assert.AreEqual(SpriteAnimKind.Parts, profile.AnimKind);

            // Both data sets coexist on one profile.
            Assert.AreEqual(1, profile.Clips.Count);
            Assert.AreEqual(4, profile.PartsSlots.Count);
            Assert.IsTrue(SpritePartsValidation.Validate(profile).Ok);
        }

        [Test]
        public void Switch_PartsAndBack_PreservesBothDataSets()
        {
            var profile = FrameProfileWithPlayableClip();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            // Author one Parts key so the Parts data is non-trivial.
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(
                profile, walk, "body", 0.25f, new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(0.1f, 0f),
                    Rotation = 5f,
                    Scale = Vector2.one,
                }).WroteKey);

            Assert.IsTrue(SpritePartsAuthoringOps.TrySetAnimKind(profile, SpriteAnimKind.Frame).Ok);
            Assert.AreEqual(SpriteAnimKind.Frame, profile.AnimKind);
            Assert.AreEqual(1, profile.Clips.Count);
            Assert.AreEqual(4, profile.PartsSlots.Count);
            Assert.AreEqual(1, profile.PartsClips[walk].Tracks.Count, "Keys must survive a mode switch.");

            Assert.IsTrue(SpritePartsAuthoringOps.TrySetAnimKind(profile, SpriteAnimKind.Parts).Ok);
            Assert.AreEqual(SpriteAnimKind.Parts, profile.AnimKind);
            Assert.AreEqual(1, profile.Clips.Count);
            Assert.AreEqual(1, profile.PartsClips[walk].Tracks.Count);
            Assert.IsTrue(SpritePartsValidation.Validate(profile).Ok);
        }

        [Test]
        public void SwitchToFrame_RejectedWithoutUsableFrameClip()
        {
            var profile = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            var result = SpritePartsAuthoringOps.TrySetAnimKind(profile, SpriteAnimKind.Frame);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("frame clip", result.Reason);
            Assert.AreEqual(SpriteAnimKind.Parts, profile.AnimKind);
        }

        [Test]
        public void SwitchToFrame_Rejected_DoesNotMigrateLegacySheets()
        {
            var profile = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            int sheetCount = profile.Sheets.Count;
            string jsonBefore = profile.ToJson();

            var result = SpritePartsAuthoringOps.TrySetAnimKind(profile, SpriteAnimKind.Frame);
            Assert.IsFalse(result.Ok);
            Assert.AreEqual(sheetCount, profile.Sheets.Count);
            Assert.AreEqual(jsonBefore, profile.ToJson(),
                "A rejected Frames switch must not invent a sheet from legacy fields.");
        }

        [Test]
        public void CanSetAnimKind_PeekDoesNotAllocatePartsOrSheets()
        {
            var profile = new SpriteSheetProfile();
            profile.Clips.Add(new SpriteClipDef { Name = "Idle", Frames = new[] { 0 } });
            string jsonBefore = profile.ToJson();

            Assert.IsFalse(SpritePartsAuthoringOps.CanSetAnimKind(profile, SpriteAnimKind.Parts, out _));
            Assert.AreEqual(jsonBefore, profile.ToJson(),
                "Peeking Use Parts must not EnsureSheets or create Parts lists.");
            Assert.AreEqual(SpriteAnimKind.Frame, profile.AnimKind);
        }

        [Test]
        public void CanSetAnimKind_PeeksWithoutChangingMode()
        {
            var profile = FrameProfileWithPlayableClip();
            Assert.IsFalse(SpritePartsAuthoringOps.CanSetAnimKind(profile, SpriteAnimKind.Parts, out _));
            Assert.AreEqual(SpriteAnimKind.Frame, profile.AnimKind);

            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            Assert.IsTrue(SpritePartsAuthoringOps.CanSetAnimKind(profile, SpriteAnimKind.Frame, out _));
            Assert.AreEqual(SpriteAnimKind.Parts, profile.AnimKind, "Peek must restore the previous mode.");

            // Same-kind request is a successful no-op.
            Assert.IsTrue(SpritePartsAuthoringOps.TrySetAnimKind(profile, SpriteAnimKind.Parts).Ok);
            Assert.AreEqual(SpriteAnimKind.Parts, profile.AnimKind);
        }

        [Test]
        public void SlotArtBinding_BindsAppearanceAndCell()
        {
            var profile = FrameProfileWithPlayableClip();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Iron Sword",
                AppearanceId = "weapon.iron.sword",
                SheetIndex = 0,
                CellIndex = 2,
            });

            var bound = SpritePartsAuthoringOps.SetSlotDefaultAppearance(
                profile, "weapon", "weapon.iron.sword");
            Assert.IsTrue(bound.Ok, bound.Reason);
            Assert.AreEqual("weapon.iron.sword", profile.PartsSlots[3].DefaultAppearanceId);
            Assert.IsTrue(SpritePartsValidation.Validate(profile).Ok);

            var missing = SpritePartsAuthoringOps.SetSlotDefaultAppearance(
                profile, "weapon", "sword.of.doom");
            Assert.IsFalse(missing.Ok);
            StringAssert.Contains("not in this profile", missing.Reason);

            var cell = SpritePartsAuthoringOps.BindSlotArtFromCell(profile, "body", 0, 3);
            Assert.IsTrue(cell.Ok, cell.Reason);
            var body = SpritePartsAuthoringOps.FindSlot(profile, "body");
            Assert.AreEqual(cell.AppearanceId, body.DefaultAppearanceId);
            Assert.IsTrue(SpritePartsValidation.Validate(profile).Ok);
        }

        [Test]
        public void BatchCreateAppearances_ReusesExactCells_UniquifiesIds()
        {
            var profile = FrameProfileWithPlayableClip();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            // Pre-existing appearance on cell 1: the batch must reuse it.
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Pre", AppearanceId = "pre", SheetIndex = 0, CellIndex = 1,
            });

            var result = SpritePartsAuthoringOps.CreateAppearancesForCells(
                profile, 0, new List<int> { 0, 1, 2, 2 });
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(2, result.Created, "Cells 0 and 2 are created.");
            Assert.AreEqual(2, result.Reused, "Cell 1 and the duplicate 2 are reused.");
            Assert.AreEqual(4, result.AppearanceIds.Count);
            Assert.IsNotNull(SpritePartsAuthoringOps.FindAppearance(profile, result.AppearanceIds[0]));
            Assert.AreEqual("pre", result.AppearanceIds[1]);
            Assert.AreEqual(result.AppearanceIds[2], result.AppearanceIds[3]);
            Assert.IsNotNull(SpritePartsAuthoringOps.FindAppearance(profile, result.AppearanceIds[2]));
            Assert.IsTrue(SpritePartsValidation.Validate(profile).Ok);
        }

        [Test]
        public void SwitchToStatic_RejectedWithoutSheetTexture()
        {
            var profile = new SpriteSheetProfile();
            var result = SpritePartsAuthoringOps.TrySetAnimKind(profile, SpriteAnimKind.Static);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("sheet", result.Reason);
            Assert.AreEqual(SpriteAnimKind.Frame, profile.AnimKind);
        }

        [Test]
        public void SwitchToStatic_SucceedsWithSheet_PreservesClipsAndParts()
        {
            var profile = FrameProfileWithPlayableClip();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            Assert.IsTrue(SpritePartsAuthoringOps.TrySetAnimKind(profile, SpriteAnimKind.Static).Ok);
            Assert.AreEqual(SpriteAnimKind.Static, profile.AnimKind);
            Assert.AreEqual(1, profile.Clips.Count);
            Assert.AreEqual(4, profile.PartsSlots.Count);

            Assert.IsTrue(SpritePartsAuthoringOps.TrySetAnimKind(profile, SpriteAnimKind.Frame).Ok);
            Assert.AreEqual(SpriteAnimKind.Frame, profile.AnimKind);
            Assert.AreEqual(1, profile.Clips.Count);
            Assert.AreEqual(4, profile.PartsSlots.Count);
        }

        [Test]
        public void CanSetAnimKind_StaticPeekDoesNotChangeMode()
        {
            var profile = FrameProfileWithPlayableClip();
            Assert.IsTrue(SpritePartsAuthoringOps.CanSetAnimKind(profile, SpriteAnimKind.Static, out _));
            Assert.AreEqual(SpriteAnimKind.Frame, profile.AnimKind);
        }
    }
}

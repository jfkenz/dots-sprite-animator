using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePartsTreeOpsTests
    {
        SpriteSheetProfile MakeFloating()
        {
            var profile = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            SpritePartsAuthoringOps.NormalizeSiblingOrders(profile);
            return profile;
        }

        [Test]
        public void RenameKeepsSlotId_AndTracksBound()
        {
            var profile = MakeFloating();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            SpritePartsAuthoringOps.ApplyPoseEdit(
                profile, SpritePartsStudioMode.Animate, walk, "weapon", 0.1f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(0.5f, 0f),
                    Rotation = 0f,
                    Scale = Vector2.one,
                }, autoKey: true);

            string idBefore = SpritePartsAuthoringOps.FindSlot(profile, "weapon").SlotId;
            var result = SpritePartsAuthoringOps.TryRenameDisplayName(profile, "weapon", "Blade");
            Assert.IsTrue(result.Ok, result.Reason);
            var weapon = SpritePartsAuthoringOps.FindSlot(profile, idBefore);
            Assert.IsNotNull(weapon);
            Assert.AreEqual("Blade", weapon.Name);
            Assert.AreEqual(idBefore, weapon.SlotId);
            var track = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], idBefore);
            Assert.IsNotNull(track);
            Assert.Greater(track.Keys.Count, 0);
        }

        [Test]
        public void RenameRejectsSiblingCollision_AllowsSameNameUnderOtherParent()
        {
            var profile = MakeFloating();
            Assert.IsFalse(SpritePartsAuthoringOps.TryRenameDisplayName(profile, "hand.l", "Hand R").Ok);
            Assert.IsTrue(SpritePartsAuthoringOps.TryRenameDisplayName(profile, "weapon", "Hand L").Ok);
        }

        [Test]
        public void ReparentPreservesRestWorld_WhenRepresentable()
        {
            var profile = MakeFloating();
            var weapon = SpritePartsAuthoringOps.FindSlot(profile, "weapon");
            Assert.IsTrue(SpritePartsAuthoringOps.TryBuildRestWorldMatrices(profile, out var before, out _), "compose");
            float4x4 worldBefore = before[SpritePartIdUtility.Canonical(weapon.SlotId)];

            // Move weapon from hand.r to body (parent under body).
            var result = SpritePartsAuthoringOps.TryCommitTreeMove(
                profile, "weapon",
                SpritePartsAuthoringOps.TreeDropKind.ParentUnder,
                "body",
                confirmAnimationReview: true);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual("body", SpritePartIdUtility.Canonical(weapon.ParentSlotId));

            Assert.IsTrue(SpritePartsAuthoringOps.TryBuildRestWorldMatrices(profile, out var after, out _));
            float4x4 worldAfter = after[SpritePartIdUtility.Canonical(weapon.SlotId)];
            Assert.AreEqual(worldBefore.c3.x, worldAfter.c3.x, 1e-4f);
            Assert.AreEqual(worldBefore.c3.y, worldAfter.c3.y, 1e-4f);
            Assert.AreEqual(
                SpritePartsHierarchy.ExtractRotationDeg(worldBefore),
                SpritePartsHierarchy.ExtractRotationDeg(worldAfter),
                1e-3f);
        }

        [Test]
        public void ReparentToRoot_PreservesRestWorld()
        {
            var profile = MakeFloating();
            var weapon = SpritePartsAuthoringOps.FindSlot(profile, "weapon");
            Assert.IsTrue(SpritePartsAuthoringOps.TryBuildRestWorldMatrices(profile, out var before, out _));
            float4x4 worldBefore = before["weapon"];

            var result = SpritePartsAuthoringOps.TryCommitTreeMove(
                profile, "weapon",
                SpritePartsAuthoringOps.TreeDropKind.MoveToRoot,
                string.Empty,
                confirmAnimationReview: true);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.IsTrue(string.IsNullOrEmpty(weapon.ParentSlotId));

            Assert.IsTrue(SpritePartsAuthoringOps.TryBuildRestWorldMatrices(profile, out var after, out _));
            float4x4 worldAfter = after["weapon"];
            Assert.AreEqual(worldBefore.c3.x, worldAfter.c3.x, 1e-4f);
            Assert.AreEqual(worldBefore.c3.y, worldAfter.c3.y, 1e-4f);
        }

        [Test]
        public void SiblingReorderDoesNotChangeDrawRank()
        {
            var profile = MakeFloating();
            var handL = SpritePartsAuthoringOps.FindSlot(profile, "hand.l");
            var handR = SpritePartsAuthoringOps.FindSlot(profile, "hand.r");
            int rankL = handL.DrawRank;
            int rankR = handR.DrawRank;
            // hand.l is sibling order 0, hand.r is 1 — move hand.l after hand.r
            var result = SpritePartsAuthoringOps.TryCommitTreeMove(
                profile, "hand.l",
                SpritePartsAuthoringOps.TreeDropKind.InsertAfter,
                "hand.r",
                confirmAnimationReview: false);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(rankL, handL.DrawRank);
            Assert.AreEqual(rankR, handR.DrawRank);
            Assert.AreEqual("body", SpritePartIdUtility.Canonical(handL.ParentSlotId));
            var siblings = SpritePartsAuthoringOps.GetChildrenSorted(profile, "body");
            Assert.AreEqual("hand.r", SpritePartIdUtility.Canonical(siblings[0].SlotId));
            Assert.AreEqual("hand.l", SpritePartIdUtility.Canonical(siblings[1].SlotId));
        }

        [Test]
        public void DeleteSubtree_RemovesTracksAndBindings_SnapshotRestore()
        {
            var profile = MakeFloating();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            SpritePartsAuthoringOps.ApplyPoseEdit(
                profile, SpritePartsStudioMode.Animate, walk, "hand.r", 0.2f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(1f, 0f), Rotation = 10f, Scale = Vector2.one,
                }, autoKey: true);
            SpritePartsAuthoringOps.ApplyPoseEdit(
                profile, SpritePartsStudioMode.Animate, walk, "weapon", 0.2f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(0.4f, 0f), Rotation = 0f, Scale = Vector2.one,
                }, autoKey: true);
            profile.PartsSkins[0].Bindings.Add(new SpritePartsSkinBindingDef
            {
                SlotId = "weapon", AppearanceId = "sword",
            });

            // Snapshot for Undo-equivalent restore.
            var slotSnap = new List<SpritePartSlotDef>();
            foreach (var s in profile.PartsSlots)
            {
                slotSnap.Add(new SpritePartSlotDef
                {
                    Name = s.Name, SlotId = s.SlotId, ParentSlotId = s.ParentSlotId,
                    SiblingOrder = s.SiblingOrder, RestPosition = s.RestPosition,
                    RestRotation = s.RestRotation, RestScale = s.RestScale,
                    DefaultAppearanceId = s.DefaultAppearanceId, DrawRank = s.DrawRank,
                    Enabled = s.Enabled, EditorLocked = s.EditorLocked,
                });
            }
            var trackSnap = new List<SpritePartsTrackDef>();
            foreach (var t in profile.PartsClips[walk].Tracks)
            {
                var nt = new SpritePartsTrackDef { SlotId = t.SlotId, Keys = new List<SpritePartsKeyDef>() };
                foreach (var k in t.Keys)
                    nt.Keys.Add(new SpritePartsKeyDef
                    {
                        Time = k.Time, Position = k.Position, Rotation = k.Rotation,
                        Scale = k.Scale, EaseMode = k.EaseMode, AppearanceId = k.AppearanceId,
                    });
                trackSnap.Add(nt);
            }
            var bindSnap = new List<SpritePartsSkinBindingDef>(profile.PartsSkins[0].Bindings);

            var del = SpritePartsAuthoringOps.TryDeleteSubtree(profile, "hand.r");
            Assert.IsTrue(del.Ok, del.Reason);
            Assert.AreEqual(2, del.DeletedSlotCount); // hand.r + weapon
            Assert.IsNull(SpritePartsAuthoringOps.FindSlot(profile, "hand.r"));
            Assert.IsNull(SpritePartsAuthoringOps.FindSlot(profile, "weapon"));
            Assert.IsNull(SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], "hand.r"));
            Assert.IsNull(SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], "weapon"));
            Assert.AreEqual(0, profile.PartsSkins[0].Bindings.Count);

            // Restore (simulates one Undo restoring subtree + tracks + bindings).
            profile.PartsSlots.Clear();
            profile.PartsSlots.AddRange(slotSnap);
            profile.PartsClips[walk].Tracks.Clear();
            profile.PartsClips[walk].Tracks.AddRange(trackSnap);
            profile.PartsSkins[0].Bindings.Clear();
            profile.PartsSkins[0].Bindings.AddRange(bindSnap);

            Assert.IsNotNull(SpritePartsAuthoringOps.FindSlot(profile, "weapon"));
            Assert.IsNotNull(SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], "weapon"));
            Assert.AreEqual(1, profile.PartsSkins[0].Bindings.Count);
        }

        [Test]
        public void AddPart_BlocksAbove32()
        {
            var profile = new SpriteSheetProfile();
            SpritePartsAuthoringOps.CreateEmptyPartsRig(profile);
            for (int i = 0; i < SpritePartIdUtility.MaxParts; i++)
            {
                var r = SpritePartsAuthoringOps.TryAddRootPart(profile, out _);
                Assert.IsTrue(r.Ok, $"add {i}: {r.Reason}");
            }
            Assert.AreEqual(SpritePartIdUtility.MaxParts, profile.PartsSlots.Count);
            var blocked = SpritePartsAuthoringOps.TryAddRootPart(profile, out _);
            Assert.IsFalse(blocked.Ok);
            StringAssert.Contains("32", blocked.Reason);

            // Reorder / reparent still work at max.
            var a = profile.PartsSlots[0];
            var b = profile.PartsSlots[1];
            var reorder = SpritePartsAuthoringOps.TryCommitTreeMove(
                profile, a.SlotId,
                SpritePartsAuthoringOps.TreeDropKind.InsertAfter, b.SlotId);
            Assert.IsTrue(reorder.Ok, reorder.Reason);
        }

        [Test]
        public void CycleReject_SelfAndDescendant()
        {
            var profile = MakeFloating();
            var self = SpritePartsAuthoringOps.ValidateTreeMove(
                profile, "body",
                SpritePartsAuthoringOps.TreeDropKind.ParentUnder, "body");
            Assert.IsFalse(self.Ok);

            var cycle = SpritePartsAuthoringOps.ValidateTreeMove(
                profile, "body",
                SpritePartsAuthoringOps.TreeDropKind.ParentUnder, "weapon");
            Assert.IsFalse(cycle.Ok);
            StringAssert.Contains("cycle", cycle.Reason.ToLowerInvariant());

            var commit = SpritePartsAuthoringOps.TryCommitTreeMove(
                profile, "body",
                SpritePartsAuthoringOps.TreeDropKind.ParentUnder, "weapon",
                confirmAnimationReview: true);
            Assert.IsFalse(commit.Ok);
            Assert.AreEqual(string.Empty,
                SpritePartsAuthoringOps.FindSlot(profile, "body").ParentSlotId ?? string.Empty);
        }

        [Test]
        public void LockedRejectsRenameAndReparent()
        {
            var profile = MakeFloating();
            var hand = SpritePartsAuthoringOps.FindSlot(profile, "hand.r");
            hand.EditorLocked = true;
            Assert.IsFalse(SpritePartsAuthoringOps.TryRenameDisplayName(profile, "hand.r", "Arm").Ok);
            Assert.IsFalse(SpritePartsAuthoringOps.ValidateTreeMove(
                profile, "weapon",
                SpritePartsAuthoringOps.TreeDropKind.MoveToRoot, string.Empty).Ok);
        }

        [Test]
        public void AddRootDoesNotParentToSelection_AddChildDoes()
        {
            var profile = MakeFloating();
            var root = SpritePartsAuthoringOps.TryAddRootPart(profile, out var createdRoot);
            Assert.IsTrue(root.Ok, root.Reason);
            Assert.IsTrue(string.IsNullOrEmpty(createdRoot.ParentSlotId));

            var child = SpritePartsAuthoringOps.TryAddChildPart(profile, "body", out var createdChild);
            Assert.IsTrue(child.Ok, child.Reason);
            Assert.AreEqual("body", SpritePartIdUtility.Canonical(createdChild.ParentSlotId));
        }

        [Test]
        public void HierarchyBuildOrder_UsesSiblingOrderNotDrawRank()
        {
            var profile = MakeFloating();
            var handL = SpritePartsAuthoringOps.FindSlot(profile, "hand.l");
            var handR = SpritePartsAuthoringOps.FindSlot(profile, "hand.r");
            // Swap sibling order only.
            handL.SiblingOrder = 1;
            handR.SiblingOrder = 0;
            handL.DrawRank = 1;
            handR.DrawRank = 2;
            var kids = SpritePartsAuthoringOps.GetChildrenSorted(profile, "body");
            Assert.AreEqual("hand.r", SpritePartIdUtility.Canonical(kids[0].SlotId));
            Assert.AreEqual("hand.l", SpritePartIdUtility.Canonical(kids[1].SlotId));
            Assert.AreEqual(1, handL.DrawRank);
            Assert.AreEqual(2, handR.DrawRank);
        }
    
        [Test]
        public void MoveDrawRankToFrontIndex_PutsWeaponBehindBody()
        {
            var profile = MakeFloating();
            var weapon = SpritePartsAuthoringOps.FindSlot(profile, "weapon");
            var body = SpritePartsAuthoringOps.FindSlot(profile, "body");
            Assert.Greater(weapon.DrawRank, body.DrawRank);

            var front = SpritePartsAuthoringOps.GetSlotsSortedByDrawRank(profile, frontFirst: true);
            Assert.AreEqual("weapon", SpritePartIdUtility.Canonical(front[0].SlotId));

            var result = SpritePartsAuthoringOps.TryMoveDrawRankToFrontIndex(
                profile, "weapon", front.Count - 1);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(0, weapon.DrawRank);
            Assert.Greater(body.DrawRank, weapon.DrawRank);

            var parent = SpritePartIdUtility.Canonical(weapon.ParentSlotId);
            Assert.AreEqual("hand.r", parent);
        }


        [Test]
        public void DuplicateMirrored_OnlyMirrorsRoot_AndStillBuildsBlob()
        {
            var profile = MakeFloating();
            var hand = SpritePartsAuthoringOps.FindSlot(profile, "hand.l");
            Assert.IsNotNull(hand);
            // Ensure hand has a child so subtree copy is exercised.
            Assert.IsTrue(SpritePartsAuthoringOps.TryAddChildPart(profile, "hand.l", out var child).Ok);
            child.RestPosition = new Vector2(0.25f, 0.1f);
            child.RestScale = Vector2.one;
            float childPx = child.RestPosition.x;
            float childSx = child.RestScale.x;

            var result = SpritePartsAuthoringOps.TryDuplicatePart(
                profile, "hand.l", out var created, mirrorHorizontal: true);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.IsNotNull(created);
            Assert.Less(created.RestScale.x, 0f);
            Assert.AreEqual(-hand.RestPosition.x, created.RestPosition.x, 1e-4f);

            // Find mirrored child under created
            string createdId = SpritePartIdUtility.Canonical(created.SlotId);
            SpritePartSlotDef mirroredChild = null;
            for (int i = 0; i < profile.PartsSlots.Count; i++)
            {
                var s = profile.PartsSlots[i];
                if (s == null) continue;
                if (SpritePartIdUtility.Canonical(s.ParentSlotId) == createdId &&
                    s.SlotId != created.SlotId)
                {
                    mirroredChild = s;
                    break;
                }
            }
            Assert.IsNotNull(mirroredChild, "expected mirrored subtree child");
            Assert.AreEqual(childPx, mirroredChild.RestPosition.x, 1e-4f);
            Assert.AreEqual(childSx, mirroredChild.RestScale.x, 1e-4f);

            var validation = SpritePartsValidation.Validate(profile);
            Assert.IsTrue(validation.Ok, string.Join(" | ", validation.Errors));
            Assert.IsTrue(SpritePartsClipConversion.TryBuildPoseEvaluationBlob(
                profile, Unity.Collections.Allocator.Temp, out var blob, out var err), err);
            if (blob.IsCreated) blob.Dispose();
        }
    }
}

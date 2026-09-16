using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using NUnit.Framework;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteProfileClipImportTests
    {
        static Texture2D Tex() => new Texture2D(8, 8);

        static SpriteSheetProfile MakeFrameSource()
        {
            var p = new SpriteSheetProfile();
            p.Sheets.Add(new SpriteSheetDef
            {
                Name = "WalkSheet", Texture = Tex(), Columns = 4, Rows = 1,
                PixelsPerUnit = 32f, Pivot = new Vector2(0.5f, 0.5f),
            });
            p.Clips.Add(new SpriteClipDef
            {
                Name = "Walk", SheetIndex = 0, Frames = new[] { 0, 1, 2, 3 },
                FrameRate = 8f, FrameDurationScales = new[] { 1f, 1f, 1f, 1f },
            });
            p.Clips[0].EnsureFrameData();
            return p;
        }

        static SpriteSheetProfile MakePartsDest()
        {
            var p = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(p);
            p.Sheets.Add(new SpriteSheetDef
            {
                Name = "Hero", Texture = Tex(), Columns = 2, Rows = 2,
                PixelsPerUnit = 32f, Pivot = new Vector2(0.5f, 0.5f),
            });
            return p;
        }

        [Test]
        public void PlanImport_DoesNotMutateDestination()
        {
            var source = MakeFrameSource();
            var dest = MakePartsDest();
            string before = dest.ToJson();
            var plan = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, new List<int> { 0 }, null, null, null);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(before, dest.ToJson());
        }

        [Test]
        public void Import_FrameClip_CopiesWithSheetRemap()
        {
            var source = MakeFrameSource();
            string sourceJson = source.ToJson();
            var dest = MakePartsDest();
            int sheetsBefore = dest.Sheets.Count;
            int clipsBefore = dest.Clips.Count;

            var plan = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, new List<int> { 0 }, null, null, null);
            Assert.IsTrue(plan.Ok, plan.Reason);
            var result = SpriteProfileClipImport.Apply(plan, source, dest);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(sourceJson, source.ToJson(), "Source must stay unchanged.");
            Assert.AreEqual(sheetsBefore + 1, dest.Sheets.Count);
            Assert.AreEqual(clipsBefore + 1, dest.Clips.Count);
            var imported = dest.Clips[dest.Clips.Count - 1];
            Assert.AreEqual(4, imported.Frames.Length);
            Assert.AreEqual(sheetsBefore, imported.SheetIndex);
        }

        [Test]
        public void Import_PartsClip_RequiresSlotMap_ThenRemaps()
        {
            var source = MakePartsDest();
            // Author a weapon key with appearance on source.
            source.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Sword", AppearanceId = "sword", SheetIndex = 0, CellIndex = 1,
            });
            int walk = SpritePartsAuthoringOps.FindClipIndex(source, "walk");
            Assert.IsTrue(walk >= 0);
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeySprite(
                source, walk, "weapon", 0.1f, "sword",
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(0.2f, 0f), Rotation = 0f, Scale = Vector2.one,
                }).WroteAppearance);

            var dest = MakePartsDest();
            // Same floating template SlotIds exist.
            var plan = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, null, new List<int> { walk },
                null, null);
            Assert.IsFalse(plan.Ok, "Unmapped slots must block.");

            var map = SpriteProfileClipImport.SuggestSlotMap(source, dest, new List<int> { walk });
            Assert.IsTrue(map.ContainsKey("weapon"));
            plan = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, null, new List<int> { walk }, map, null);
            Assert.IsTrue(plan.Ok, plan.Reason);
            int partsBefore = dest.PartsClips.Count;
            var result = SpriteProfileClipImport.Apply(plan, source, dest);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(partsBefore + 1, dest.PartsClips.Count);
            var imported = dest.PartsClips[dest.PartsClips.Count - 1];
            Assert.IsNotNull(SpritePartsAuthoringOps.FindTrack(imported, "weapon"));
        }

        [Test]
        public void FrameSequence_WritesAppearanceKeys_OnEmptyTrack()
        {
            var profile = MakePartsDest();
            profile.Clips.Add(new SpriteClipDef
            {
                Name = "Blink", SheetIndex = 0, Frames = new[] { 0, 1, 0 },
                FrameRate = 10f, FrameDurationScales = new[] { 1f, 1f, 1f },
            });
            profile.Clips[0].EnsureFrameData();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            Assert.IsTrue(walk >= 0);

            var plan = SpritePartsFrameSequenceImport.PlanImport(
                profile, 0, walk, "body", 0f, true);
            Assert.IsTrue(plan.Ok, plan.Reason);
            var result = SpritePartsFrameSequenceImport.Apply(plan, profile);
            Assert.IsTrue(result.Ok, result.Reason);
            string aid0 = SpritePartsAuthoringOps.SampleKeyedAppearanceId(profile, walk, "body", 0.01f);
            Assert.IsFalse(string.IsNullOrEmpty(aid0));
            string aid1 = SpritePartsAuthoringOps.SampleKeyedAppearanceId(profile, walk, "body", 0.15f);
            Assert.IsFalse(string.IsNullOrEmpty(aid1));
        }

        [Test]
        public void FrameSequence_NonRestPoseTrack_WritesAppearanceChannelOnly()
        {
            var profile = MakePartsDest();
            profile.Clips.Add(new SpriteClipDef
            {
                Name = "Blink", SheetIndex = 0, Frames = new[] { 0, 1 },
                FrameRate = 8f, FrameDurationScales = new[] { 1f, 1f },
            });
            profile.Clips[0].EnsureFrameData();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(
                profile, walk, "body", 0.1f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(1f, 0f), Rotation = 15f, Scale = Vector2.one,
                }).WroteKey);
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(
                profile, walk, "body", 0.5f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(-1f, 0f), Rotation = 0f, Scale = Vector2.one,
                }).WroteKey);
            string jsonBefore = profile.ToJson();

            // Non-rest pose no longer blocks: appearance keys go to the channel.
            var plan = SpritePartsFrameSequenceImport.PlanImport(
                profile, 0, walk, "body", 0f, true);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(jsonBefore, profile.ToJson(), "Plan must stay read-only.");

            var result = SpritePartsFrameSequenceImport.Apply(plan, profile);
            Assert.IsTrue(result.Ok, result.Reason);

            var walkClip = profile.PartsClips[walk];
            var poseTrack = SpritePartsAuthoringOps.FindTrack(walkClip, "body");
            Assert.IsNotNull(poseTrack);
            // WriteKeyPose adds the documented rest anchor at t=0: [0, 0.1, 0.5].
            Assert.AreEqual(3, poseTrack.Keys.Count, "Pose keys must be untouched.");
            Assert.AreEqual(1f, poseTrack.Keys[1].Position.x, 1e-5f);
            Assert.AreEqual(15f, poseTrack.Keys[1].Rotation, 1e-4f);
            Assert.AreEqual(-1f, poseTrack.Keys[2].Position.x, 1e-5f);

            var channel = SpritePartsAuthoringOps.FindAppearanceTrack(walkClip, "body");
            Assert.IsNotNull(channel, "Appearance channel must be created.");
            Assert.AreEqual(SpritePartsTrackKind.Appearance, channel.Kind);
            Assert.AreEqual(2, channel.Keys.Count);
            Assert.AreEqual(Vector2.zero, channel.Keys[0].Position, "Channel keys carry no pose.");
            Assert.AreEqual((byte)SpriteEaseMode.Step, channel.Keys[0].EaseMode);

            string a0 = SpritePartsAuthoringOps.SampleKeyedAppearanceId(profile, walk, "body", 0.01f);
            Assert.IsFalse(string.IsNullOrEmpty(a0), "Cell 0 must be keyed.");
            string a1 = SpritePartsAuthoringOps.SampleKeyedAppearanceId(profile, walk, "body", 0.2f);
            Assert.IsFalse(string.IsNullOrEmpty(a1));
            Assert.AreNotEqual(a0, a1, "Cells must swap across the sequence.");

            // Pose motion survives: sample through the shared evaluator.
            Assert.IsTrue(SpritePartsOnion.TrySampleCharacter(
                profile, walk, 0.5f, Allocator.Temp,
                out var blob, out var poses, out var matrices, out string err), err);
            try
            {
                int body = FindBlobSlot(ref blob.Value, "body");
                Assert.GreaterOrEqual(body, 0);
                Assert.AreEqual(-1f, poses[body].Position.x, 1e-3f);
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
            Assert.IsTrue(SpritePartsValidation.Validate(profile).Ok);
        }

        [Test]
        public void FrameSequence_EmptyPoseTrack_CreatesChannelOnly()
        {
            var profile = MakePartsDest();
            profile.Clips.Add(new SpriteClipDef
            {
                Name = "Blink", SheetIndex = 0, Frames = new[] { 0, 1 },
                FrameRate = 8f, FrameDurationScales = new[] { 1f, 1f },
            });
            profile.Clips[0].EnsureFrameData();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");

            var plan = SpritePartsFrameSequenceImport.PlanImport(
                profile, 0, walk, "body", 0f, true);
            Assert.IsTrue(plan.Ok, plan.Reason);
            var result = SpritePartsFrameSequenceImport.Apply(plan, profile);
            Assert.IsTrue(result.Ok, result.Reason);

            var walkClip = profile.PartsClips[walk];
            Assert.IsNull(SpritePartsAuthoringOps.FindTrack(walkClip, "body"),
                "No pose track is created and no rest keys are written.");
            var channel = SpritePartsAuthoringOps.FindAppearanceTrack(walkClip, "body");
            Assert.IsNotNull(channel);
            Assert.AreEqual(2, channel.Keys.Count);
            Assert.IsFalse(string.IsNullOrEmpty(
                SpritePartsAuthoringOps.SampleKeyedAppearanceId(profile, walk, "body", 0.01f)));
            Assert.IsTrue(SpritePartsValidation.Validate(profile).Ok);
        }

        [Test]
        public void FrameSequence_DurationWithoutExtend_StillBlocks_Unchanged()
        {
            var profile = MakePartsDest();
            profile.Clips.Add(new SpriteClipDef
            {
                Name = "Blink", SheetIndex = 0, Frames = new[] { 0, 1, 0, 1, 0, 1, 0, 1 },
                FrameRate = 8f,
                FrameDurationScales = new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f },
            });
            profile.Clips[0].EnsureFrameData();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            string jsonBefore = profile.ToJson();

            var plan = SpritePartsFrameSequenceImport.PlanImport(
                profile, 0, walk, "body", 0.3f, false);
            Assert.IsFalse(plan.Ok);
            StringAssert.Contains("duration", plan.Reason);
            Assert.AreEqual(jsonBefore, profile.ToJson());
        }

        [Test]
        public void FrameSequence_ApplyFailure_RollsBack()
        {
            var profile = MakePartsDest();
            profile.Clips.Add(new SpriteClipDef
            {
                // Cell 99 does not exist on the 2x2 sheet; the seven valid
                // frames extend the duration first, so the failure must roll
                // the extension, the created appearances and the channel back.
                Name = "Blink", SheetIndex = 0, Frames = new[] { 0, 1, 2, 3, 0, 1, 2, 99 },
                FrameRate = 8f,
                FrameDurationScales = new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f },
            });
            profile.Clips[0].EnsureFrameData();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            float durationBefore = profile.PartsClips[walk].Duration;
            int appearancesBefore = profile.PartsAppearances.Count;

            var plan = SpritePartsFrameSequenceImport.PlanImport(
                profile, 0, walk, "body", 0f, true);
            Assert.IsTrue(plan.Ok, plan.Reason);
            var result = SpritePartsFrameSequenceImport.Apply(plan, profile);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("outside sheet", result.Reason);

            Assert.AreEqual(durationBefore, profile.PartsClips[walk].Duration,
                "Duration extension must roll back.");
            Assert.AreEqual(appearancesBefore, profile.PartsAppearances.Count,
                "Created appearances must roll back.");
            Assert.IsNull(SpritePartsAuthoringOps.FindAppearanceTrack(
                profile.PartsClips[walk], "body"), "Created channel must roll back.");
            Assert.IsNull(SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], "body"));
        }

        [Test]
        public void FrameSequence_LegacyPoseKeyIds_MigrateIntoChannel()
        {
            var profile = MakePartsDest();
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Body Old", AppearanceId = "body.old", SheetIndex = 0, CellIndex = 0,
            });
            profile.Clips.Add(new SpriteClipDef
            {
                Name = "Blink", SheetIndex = 0, Frames = new[] { 0, 1 },
                FrameRate = 8f, FrameDurationScales = new[] { 1f, 1f },
            });
            profile.Clips[0].EnsureFrameData();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(
                profile, walk, "body", 0.1f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(0.5f, 0f), Rotation = 0f, Scale = Vector2.one,
                }).WroteKey);
            // Legacy data: id mixed into the pose key.
            var poseTrack = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], "body");
            poseTrack.Keys[1].AppearanceId = "body.old";
            Assert.AreEqual("body.old",
                SpritePartsAuthoringOps.SampleKeyedAppearanceId(profile, walk, "body", 0.15f),
                "Legacy ids sample before the channel exists.");

            var plan = SpritePartsFrameSequenceImport.PlanImport(
                profile, 0, walk, "body", 0.3f, true);
            Assert.IsTrue(plan.Ok, plan.Reason);
            var result = SpritePartsFrameSequenceImport.Apply(plan, profile);
            Assert.IsTrue(result.Ok, result.Reason);

            var walkClip = profile.PartsClips[walk];
            var poseAfter = SpritePartsAuthoringOps.FindTrack(walkClip, "body");
            Assert.AreEqual(string.Empty, poseAfter.Keys[1].AppearanceId,
                "Legacy id migrates off the pose key.");
            Assert.AreEqual(0.5f, poseAfter.Keys[1].Position.x, 1e-5f, "Pose values never change.");
            var channel = SpritePartsAuthoringOps.FindAppearanceTrack(walkClip, "body");
            Assert.IsNotNull(channel);
            Assert.AreEqual("body.old", channel.Keys[0].AppearanceId,
                "Migrated key keeps its time and id.");
            Assert.AreEqual(0.1f, channel.Keys[0].Time, 1e-5f);
            Assert.AreEqual(3, channel.Keys.Count, "Migrated key + sequence keys.");
            Assert.AreEqual("body.old",
                SpritePartsAuthoringOps.SampleKeyedAppearanceId(profile, walk, "body", 0.15f),
                "Legacy swap still resolves after migration.");
        }

        [Test]
        public void WriteKeySprite_WithoutPoseFallback_LeavesPoseUntouched()
        {
            var profile = MakePartsDest();
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Eye", AppearanceId = "eye.open", SheetIndex = 0, CellIndex = 1,
            });
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(
                profile, walk, "body", 0.2f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(2f, 0f), Rotation = 10f, Scale = Vector2.one,
                }).WroteKey);
            var poseBefore = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], "body");
            int poseCount = poseBefore.Keys.Count;
            float x = poseBefore.Keys[1].Position.x;

            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeySprite(
                profile, walk, "body", 0.4f, "eye.open").WroteAppearance);
            var poseAfter = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], "body");
            Assert.AreEqual(poseCount, poseAfter.Keys.Count, "Key Sprite must not add pose keys.");
            Assert.AreEqual(x, poseAfter.Keys[1].Position.x, 1e-5f);
            var channel = SpritePartsAuthoringOps.FindAppearanceTrack(profile.PartsClips[walk], "body");
            Assert.IsNotNull(channel);
            Assert.AreEqual(1, channel.Keys.Count);
            Assert.AreEqual("eye.open", channel.Keys[0].AppearanceId);
        }

        [Test]
        public void AppearanceChannel_BakeSampling_ChannelWins_LegacyFallsBack()
        {
            var profile = MakePartsDest();
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "B0", AppearanceId = "b0", SheetIndex = 0, CellIndex = 0,
            });
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "B1", AppearanceId = "b1", SheetIndex = 0, CellIndex = 1,
            });
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(
                profile, walk, "body", 0.1f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = Vector2.zero, Rotation = 0f, Scale = Vector2.one,
                }).WroteKey);
            var poseTrack = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], "body");
            poseTrack.Keys[0].AppearanceId = "b0"; // legacy mixed data

            Assert.IsTrue(SpritePartsClipConversion.TryBuildBlob(
                profile, Allocator.Temp, out var blob, out string blobError), blobError);
            try
            {
                int body = FindBlobSlot(ref blob.Value, "body");
                Assert.GreaterOrEqual(body, 0);
                // No channel: legacy pose-key id resolves through the blob.
                int legacy = SpritePartsSampler.SampleAppearanceIndex(ref blob.Value, walk, body, 0.2f);
                Assert.AreEqual(0, legacy, "Legacy b0 is appearance index 0.");
            }
            finally
            {
                blob.Dispose();
            }

            // Create the channel: it becomes the single source for the slot.
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyAppearance(
                profile, walk, "body", 0.05f, "b1").WroteAppearance);
            Assert.IsTrue(SpritePartsClipConversion.TryBuildBlob(
                profile, Allocator.Temp, out blob, out blobError), blobError);
            try
            {
                int body = FindBlobSlot(ref blob.Value, "body");
                int sampled = SpritePartsSampler.SampleAppearanceIndex(ref blob.Value, walk, body, 0.07f);
                Assert.AreEqual(1, sampled, "Channel b1 wins over the legacy pose-key id.");
            }
            finally
            {
                blob.Dispose();
            }
            Assert.IsTrue(SpritePartsValidation.Validate(profile).Ok);
        }

        [Test]
        public void Import_ReplacePolicy_FrameClip_KeepsNameAndSlot()
        {
            var source = MakeFrameSource();
            // A second, non-conflicting source clip must still import as a copy.
            source.Clips.Add(new SpriteClipDef
            {
                Name = "Run", SheetIndex = 0, Frames = new[] { 2, 3 },
                FrameRate = 10f, FrameDurationScales = new[] { 1f, 1f },
            });
            source.Clips[1].EnsureFrameData();
            string sourceJson = source.ToJson();

            var dest = MakePartsDest();
            dest.Clips.Add(new SpriteClipDef
            {
                Name = "Walk", SheetIndex = 0, Frames = new[] { 0 },
                FrameRate = 6f, FrameDurationScales = new[] { 1f },
            });
            dest.Clips[0].EnsureFrameData();
            dest.Clips.Add(new SpriteClipDef
            {
                Name = "Idle", SheetIndex = 0, Frames = new[] { 0, 1 },
                FrameRate = 8f, FrameDurationScales = new[] { 1f, 1f },
            });
            dest.Clips[1].EnsureFrameData();
            dest.Clips[1].OnCompleteClipIndex = 0; // Idle chains into the Walk slot.
            dest.Hitboxes.Add(new FrameBoxDef
            {
                ClipName = "Walk", FrameIndex = 0, RectUV = new Rect(0f, 0f, 1f, 1f),
            });
            string destJson = dest.ToJson();

            var plan = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, new List<int> { 0, 1 }, null, null, null,
                SpriteProfileImportConflictPolicy.ReplaceExisting);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(destJson, dest.ToJson(), "Plan must stay read-only.");
            Assert.AreEqual(1, plan.FrameClipsReplaced);
            var walk = plan.FrameClips[0];
            Assert.IsTrue(walk.ReplaceExisting);
            Assert.AreEqual(0, walk.DestinationClipIndex);
            Assert.AreEqual("Walk", walk.DestinationName);
            string usedBy = string.Join(";", walk.UsedBy.ToArray());
            StringAssert.Contains("hitbox", usedBy);
            StringAssert.Contains("OnComplete", usedBy);

            var result = SpriteProfileClipImport.Apply(plan, source, dest);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(3, dest.Clips.Count, "One slot replaced, one copy appended.");
            var replaced = dest.Clips[0];
            Assert.AreEqual("Walk", replaced.Name, "Destination name is stable identity.");
            Assert.AreEqual(4, replaced.Frames.Length, "Content (frames) comes from the source.");
            Assert.AreEqual(8f, replaced.FrameRate);
            Assert.AreEqual(0, dest.Clips[1].OnCompleteClipIndex, "Index references keep resolving.");
            Assert.IsTrue(dest.Hitboxes[0].AppliesToClip("Walk"), "Hitbox stays bound by name.");
            Assert.AreEqual("Run", dest.Clips[2].Name);
            Assert.AreEqual(sourceJson, source.ToJson(), "Source must stay unchanged.");
        }

        [Test]
        public void Import_ReplacePolicy_PartsClip_KeepsClipIdAndSlot()
        {
            var source = MakePartsDest();
            source.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Sword", AppearanceId = "sword", SheetIndex = 0, CellIndex = 1,
            });
            int srcWalk = SpritePartsAuthoringOps.FindClipIndex(source, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeySprite(
                source, srcWalk, "weapon", 0.1f, "sword",
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(0.2f, 0f), Rotation = 0f, Scale = Vector2.one,
                }).WroteAppearance);

            var dest = MakePartsDest();
            dest.PartsDefaultClipId = "walk";
            int destWalk = SpritePartsAuthoringOps.FindClipIndex(dest, "walk");
            string destJson = dest.ToJson();

            var map = SpriteProfileClipImport.SuggestSlotMap(source, dest, new List<int> { srcWalk });
            var plan = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, null, new List<int> { srcWalk }, map, null,
                SpriteProfileImportConflictPolicy.ReplaceExisting);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(destJson, dest.ToJson(), "Plan must stay read-only.");
            Assert.AreEqual(1, plan.PartsClipsReplaced);
            var action = plan.PartsClips[0];
            Assert.IsTrue(action.ReplaceExisting);
            Assert.AreEqual(destWalk, action.DestinationClipListIndex);
            Assert.AreEqual("walk", action.DestinationClipId);
            StringAssert.Contains("default Parts clip", string.Join(";", action.UsedBy.ToArray()));

            var result = SpriteProfileClipImport.Apply(plan, source, dest);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(2, dest.PartsClips.Count, "Replace keeps the slot; nothing is appended.");
            var replaced = dest.PartsClips[destWalk];
            Assert.AreEqual("Walk", replaced.Name, "Destination name is stable identity.");
            var track = SpritePartsAuthoringOps.FindTrack(replaced, "weapon");
            Assert.IsNotNull(track, "Slot-mapped pose track comes from the source.");
            Assert.AreEqual(2, track.Keys.Count, "Rest anchor + authored pose key.");
            var appChannel = SpritePartsAuthoringOps.FindAppearanceTrack(replaced, "weapon");
            Assert.IsNotNull(appChannel, "Keyed sprite lives on the appearance channel.");
            Assert.AreEqual(1, appChannel.Keys.Count);
            Assert.IsFalse(string.IsNullOrEmpty(appChannel.Keys[0].AppearanceId),
                "Keyed appearance is remapped onto the imported copy.");
            Assert.IsTrue(SpritePartsValidation.Validate(dest).Ok);
            Assert.AreEqual("walk", SpritePartIdUtility.Canonical(dest.PartsDefaultClipId),
                "Default clip reference keeps resolving.");
        }

        [Test]
        public void Import_ReplacePolicy_BadSlotMap_StillRejected_Unchanged()
        {
            var source = MakePartsDest();
            source.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Sword", AppearanceId = "sword", SheetIndex = 0, CellIndex = 1,
            });
            int srcWalk = SpritePartsAuthoringOps.FindClipIndex(source, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeySprite(
                source, srcWalk, "weapon", 0.1f, "sword",
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = Vector2.zero, Rotation = 0f, Scale = Vector2.one,
                }).WroteAppearance);

            var dest = MakePartsDest();
            string destJson = dest.ToJson();
            var plan = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, null, new List<int> { srcWalk },
                new Dictionary<string, string>(), null,
                SpriteProfileImportConflictPolicy.ReplaceExisting);
            Assert.IsFalse(plan.Ok, "Unmapped slots must still block under the replace policy.");
            Assert.AreEqual(destJson, dest.ToJson());
        }

        [Test]
        public void Preview_SamplesTransientClipOnDestinationRig_ReadOnly()
        {
            var dest = MakePartsDest();
            string jsonBefore = dest.ToJson();
            var transient = new SpritePartsClipDef
            {
                Name = "PreviewWalk", ClipId = "previewwalk", Duration = 0.5f,
                Tracks = new List<SpritePartsTrackDef>
                {
                    new SpritePartsTrackDef
                    {
                        SlotId = "body",
                        Keys = new List<SpritePartsKeyDef>
                        {
                            new SpritePartsKeyDef
                            {
                                Time = 0f, Position = Vector2.zero, Rotation = 0f, Scale = Vector2.one,
                            },
                            new SpritePartsKeyDef
                            {
                                Time = 0.25f, Position = new Vector2(2f, 1f), Rotation = 30f,
                                Scale = Vector2.one,
                                // Preview clones can reference appearances that only
                                // exist after commit; pose sampling must tolerate them.
                                AppearanceId = "not.yet.imported",
                            },
                        },
                    },
                },
            };

            Assert.IsTrue(SpritePartsOnion.TrySampleClipOnRig(
                dest, transient, 0.25f, Allocator.Temp,
                out var blob, out var poses, out var matrices, out string error), error);
            try
            {
                int body = FindBlobSlot(ref blob.Value, "body");
                Assert.GreaterOrEqual(body, 0);
                Assert.AreEqual(2f, poses[body].Position.x, 0.001f);
                Assert.AreEqual(1f, poses[body].Position.y, 0.001f);
                Assert.AreEqual(30f, poses[body].Rotation, 0.01f);
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }

            Assert.IsTrue(SpritePartsOnion.TrySampleClipOnRig(
                dest, transient, 0f, Allocator.Temp,
                out blob, out poses, out matrices, out error), error);
            try
            {
                int body = FindBlobSlot(ref blob.Value, "body");
                Assert.AreEqual(0f, poses[body].Position.x, 0.001f);
                Assert.AreEqual(0f, poses[body].Rotation, 0.01f);
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }

            Assert.AreEqual(jsonBefore, dest.ToJson(), "Rig preview sampling must not mutate the rig.");

            // A transient track naming a slot the rig does not have fails read-only.
            var badClip = new SpritePartsClipDef
            {
                Name = "Bad", ClipId = "bad", Duration = 1f,
                Tracks = new List<SpritePartsTrackDef>
                {
                    new SpritePartsTrackDef { SlotId = "nonexistent.slot", Keys = new List<SpritePartsKeyDef>() },
                },
            };
            Assert.IsFalse(SpritePartsOnion.TrySampleClipOnRig(
                dest, badClip, 0f, Allocator.Temp, out _, out _, out _, out string badError));
            Assert.IsNotEmpty(badError);
        }

        [Test]
        public void PoseSampling_ToleratesKeyedSprites_OnProfileClips()
        {
            // Regression: pose-only blobs carry no appearance table, so clips with
            // keyed sprites used to fail canvas/onion sampling outright.
            var profile = MakePartsDest();
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Sword", AppearanceId = "sword", SheetIndex = 0, CellIndex = 1,
            });
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeySprite(
                profile, walk, "weapon", 0.1f, "sword",
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = Vector2.zero, Rotation = 0f, Scale = Vector2.one,
                }).WroteAppearance);
            Assert.IsTrue(SpritePartsOnion.TrySampleCharacter(
                profile, walk, 0.1f, Allocator.Temp,
                out var blob, out var poses, out var matrices, out string error), error);
            SpritePartsOnion.DisposeSample(blob, poses, matrices);
        }

        static int FindBlobSlot(ref SpritePartsSetBlob set, string slotId)
        {
            string id = SpritePartIdUtility.Canonical(slotId);
            for (int i = 0; i < set.Slots.Length; i++)
            {
                if (SpritePartIdUtility.Canonical(set.Slots[i].SlotId.ToString()) == id)
                    return i;
            }
            return -1;
        }

        [Test]
        public void Preview_BuildPreviewClips_ReadOnly_AndRemapped()
        {
            var source = MakePartsDest();
            source.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Sword", AppearanceId = "sword", SheetIndex = 0, CellIndex = 1,
            });
            int srcWalk = SpritePartsAuthoringOps.FindClipIndex(source, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeySprite(
                source, srcWalk, "weapon", 0.1f, "sword",
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(0.2f, 0f), Rotation = 0f, Scale = Vector2.one,
                }).WroteAppearance);
            string sourceJson = source.ToJson();

            var dest = MakePartsDest();
            // Same id, different definition: the appearance import becomes a
            // copy 'sword2', and the preview key must be remapped onto it.
            dest.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Old Sword", AppearanceId = "sword", SheetIndex = 0, CellIndex = 0,
            });
            string destJson = dest.ToJson();

            var map = SpriteProfileClipImport.SuggestSlotMap(source, dest, new List<int> { srcWalk });
            var plan = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, null, new List<int> { srcWalk }, map, null);
            Assert.IsTrue(plan.Ok, plan.Reason);

            var previews = SpriteProfileClipImport.BuildPreviewClips(plan, source, dest);
            Assert.AreEqual(1, previews.Count);
            Assert.AreNotSame(source.PartsClips[srcWalk], previews[0], "Preview is a clone.");
            var track = SpritePartsAuthoringOps.FindTrack(previews[0], "weapon");
            Assert.IsNotNull(track, "Slot map applied to the preview pose track.");
            var appChannel = SpritePartsAuthoringOps.FindAppearanceTrack(previews[0], "weapon");
            Assert.IsNotNull(appChannel, "Preview keeps the appearance channel.");
            Assert.AreEqual("sword2", appChannel.Keys[0].AppearanceId,
                "Keyed appearance remapped to the destination copy on the channel.");
            Assert.AreEqual(sourceJson, source.ToJson(), "Preview building must not mutate the source.");
            Assert.AreEqual(destJson, dest.ToJson(), "Preview building must not mutate the destination.");
        }

        [Test]
        public void Preview_CollectSlotMapMismatches_ReportsRestAndParent()
        {
            var source = MakePartsDest();
            var dest = MakePartsDest();
            var map = new Dictionary<string, string>
            {
                ["body"] = "body",
                ["hand.r"] = "hand.r",
                ["weapon"] = "weapon",
            };
            Assert.AreEqual(0, SpriteProfileClipImport.CollectSlotMapMismatches(source, dest, map).Count,
                "Identical rigs with an identity map have no mismatches.");

            SpritePartsAuthoringOps.FindSlot(dest, "body").RestPosition = new Vector2(1f, -0.5f);
            var mismatches = SpriteProfileClipImport.CollectSlotMapMismatches(source, dest, map);
            Assert.AreEqual(1, mismatches.Count);
            Assert.AreEqual("rest", mismatches[0].Kind);
            StringAssert.Contains("rest", mismatches[0].Detail);

            // Source body sits at the root; destination weapon is parented under hand.r.
            var parentMap = new Dictionary<string, string> { ["body"] = "weapon" };
            mismatches = SpriteProfileClipImport.CollectSlotMapMismatches(source, dest, parentMap);
            bool hasParent = false;
            for (int i = 0; i < mismatches.Count; i++)
            {
                if (mismatches[i].Kind == "parent")
                {
                    hasParent = true;
                    StringAssert.Contains("parent", mismatches[i].Detail);
                }
            }
            Assert.IsTrue(hasParent, "Root-to-child mapping must report a parent mismatch.");
        }

        [Test]
        public void SlotMap_MismatchCollector_Hardening_Clauses()
        {
            var source = MakePartsDest();
            var dest = MakePartsDest();
            string sourceJson = source.ToJson();
            string destJson = dest.ToJson();

            // Unmapped source parent resolves to root and still reports when
            // the destination parent is not root (motion composes differently).
            var partialMap = new Dictionary<string, string> { ["hand.r"] = "weapon" };
            var mismatches = SpriteProfileClipImport.CollectSlotMapMismatches(source, dest, partialMap);
            bool hasParent = false;
            for (int i = 0; i < mismatches.Count; i++)
            {
                if (mismatches[i].Kind == "parent" && mismatches[i].SourceSlotId == "hand.r")
                {
                    hasParent = true;
                    StringAssert.Contains("(root)", mismatches[i].Detail);
                }
            }
            Assert.IsTrue(hasParent, "Unmapped source parent must still produce a parent mismatch.");

            // Non-canonical map keys resolve by canonical id: same rig mapped
            // through 'Body'/'Hand.R' must yield NO mismatches (no false hits).
            var casedMap = new Dictionary<string, string>
            {
                ["Body"] = "body",
                ["Hand.R"] = "hand.r",
            };
            Assert.AreEqual(0, SpriteProfileClipImport.CollectSlotMapMismatches(source, dest, casedMap).Count,
                "Canonical resolution must not false-negative on non-canonical keys.");

            // Blank mapped rows are excluded rows: never a mismatch source.
            var blankMap = new Dictionary<string, string> { ["hand.r"] = "" };
            Assert.AreEqual(0, SpriteProfileClipImport.CollectSlotMapMismatches(source, dest, blankMap).Count);

            // Collecting is read-only on both profiles.
            Assert.AreEqual(sourceJson, source.ToJson(), "Collector must not mutate the source.");
            Assert.AreEqual(destJson, dest.ToJson(), "Collector must not mutate the destination.");
        }

        [Test]
        public void SlotMap_NameSuggestion_OfferedNotApplied()
        {
            var source = MakePartsDest();
            // Extra source slot: different id, same display name as dest 'weapon'.
            source.PartsSlots.Add(new SpritePartSlotDef
            {
                Name = "Weapon", SlotId = "blade", ParentSlotId = "hand.r",
                RestPosition = new Vector2(0.25f, 0f), RestScale = Vector2.one, DrawRank = 3,
            });
            int walk = SpritePartsAuthoringOps.FindClipIndex(source, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(
                source, walk, "blade", 0.1f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = Vector2.zero, Rotation = 0f, Scale = Vector2.one,
                }).WroteKey);

            var dest = MakePartsDest();
            // No exact id for 'blade': the id auto-map stays empty...
            Assert.AreEqual(0, SpriteProfileClipImport.SuggestSlotMap(source, dest, new List<int> { walk }).Count);

            // ...but a name suggestion is offered.
            var suggestions = SpriteProfileClipImport.SuggestSlotMapByName(source, dest, new List<int> { walk });
            Assert.AreEqual(1, suggestions.Count);
            Assert.AreEqual("blade", suggestions[0].SourceSlotId);
            Assert.AreEqual("weapon", suggestions[0].DestinationSlotId);
            Assert.AreEqual("Weapon", suggestions[0].DestinationName);

            // Not auto-applied: the plan still blocks until the user confirms.
            var blocked = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, null, new List<int> { walk },
                new Dictionary<string, string>(), null);
            Assert.IsFalse(blocked.Ok);
            StringAssert.Contains("blade", blocked.Reason);

            // User confirms (simulated): the suggested mapping unblocks the plan.
            var confirmed = new Dictionary<string, string> { ["blade"] = suggestions[0].DestinationSlotId };
            var plan = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, null, new List<int> { walk }, confirmed, null);
            Assert.IsTrue(plan.Ok, plan.Reason);
        }

        [Test]
        public void SlotMap_ExactId_StillAutoMaps_NameLayerStaysQuiet()
        {
            var source = MakePartsDest();
            int walk = SpritePartsAuthoringOps.FindClipIndex(source, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(
                source, walk, "weapon", 0.1f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = Vector2.zero, Rotation = 0f, Scale = Vector2.one,
                }).WroteKey);

            var dest = MakePartsDest();
            var exact = SpriteProfileClipImport.SuggestSlotMap(source, dest, new List<int> { walk });
            Assert.IsTrue(exact.ContainsKey("weapon"));
            Assert.AreEqual("weapon", exact["weapon"]);

            // Sources covered by an exact id match get no name suggestion.
            var suggestions = SpriteProfileClipImport.SuggestSlotMapByName(source, dest, new List<int> { walk });
            Assert.AreEqual(0, suggestions.Count);
        }

        [Test]
        public void SlotMap_TwoSourcesSameDest_SuggestedOnce_ManyToOneStillBlocked()
        {
            var source = MakePartsDest();
            source.PartsSlots.Add(new SpritePartSlotDef
            {
                Name = "Weapon", SlotId = "blade", ParentSlotId = "hand.r",
                RestPosition = new Vector2(0.25f, 0f), RestScale = Vector2.one, DrawRank = 3,
            });
            source.PartsSlots.Add(new SpritePartSlotDef
            {
                Name = "weapon", SlotId = "saber", ParentSlotId = "hand.l", // case-insensitive same name
                RestPosition = new Vector2(-0.25f, 0f), RestScale = Vector2.one, DrawRank = 3,
            });
            int walk = SpritePartsAuthoringOps.FindClipIndex(source, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(
                source, walk, "blade", 0.1f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = Vector2.zero, Rotation = 0f, Scale = Vector2.one,
                }).WroteKey);
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(
                source, walk, "saber", 0.2f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = Vector2.zero, Rotation = 0f, Scale = Vector2.one,
                }).WroteKey);

            var dest = MakePartsDest();
            // One destination slot named Weapon: only one source may suggest it.
            var suggestions = SpriteProfileClipImport.SuggestSlotMapByName(source, dest, new List<int> { walk });
            Assert.AreEqual(1, suggestions.Count);
            Assert.AreEqual("blade", suggestions[0].SourceSlotId, "Discovery order wins the single suggestion.");

            // Even if the user maps both onto the same destination, the plan blocks.
            var bothMapped = new Dictionary<string, string>
            {
                ["blade"] = "weapon",
                ["saber"] = "weapon",
            };
            var plan = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, null, new List<int> { walk }, bothMapped, null);
            Assert.IsFalse(plan.Ok);
            StringAssert.Contains("Many-to-one", plan.Reason);
        }

        [Test]
        public void FrameClip_EventClosure_RemapsByMeaning_AddsEntries()
        {
            var source = MakeFrameSource();
            source.Events = new List<SpriteEventDef>
            {
                new SpriteEventDef { Id = 1, Name = "Footstep" },
                new SpriteEventDef { Id = 3, Name = "Attack", Color = Color.red },
                new SpriteEventDef { Id = 5, Name = "Jump" },
            };
            source.Clips[0].AddEventMarker(0, 1, 0f);
            source.Clips[0].AddEventMarker(1, 3, 0.5f);
            source.Clips[0].AddEventMarker(2, 5, 0f);
            source.Clips[0].EnsureFrameData();
            string sourceJson = source.ToJson();

            var dest = MakePartsDest();
            dest.Events = new List<SpriteEventDef>
            {
                new SpriteEventDef { Id = 1, Name = "Hit" },      // same id, different meaning
                new SpriteEventDef { Id = 2, Name = "Footstep" }, // same meaning, different id
                new SpriteEventDef { Id = 3, Name = "Dash" },
                new SpriteEventDef { Id = 5, Name = "Jump" },     // same id, same meaning
            };
            string destJson = dest.ToJson();

            var plan = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, new List<int> { 0 }, null, null, null);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(destJson, dest.ToJson(), "Plan must stay read-only.");
            Assert.AreEqual(2, plan.EventIdRemap[1], "Footstep remaps onto the same-name destination entry.");
            Assert.AreEqual(5, plan.EventIdRemap[5], "Same id + same name stays.");
            Assert.AreEqual(1, plan.EventsToAdd.Count);
            Assert.AreEqual(4, plan.EventsToAdd[0].DestinationId);
            Assert.AreEqual("Attack", plan.EventsToAdd[0].Name);

            var result = SpriteProfileClipImport.Apply(plan, source, dest);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(5, dest.Events.Count, "Exactly one catalog entry is appended.");
            Assert.AreEqual("Attack", dest.Events[4].Name);
            Assert.AreEqual(Color.red, dest.Events[4].Color);
            var imported = dest.Clips[dest.Clips.Count - 1];
            Assert.AreEqual(2, imported.EventMarkers[0].EventId);
            Assert.AreEqual(4, imported.EventMarkers[1].EventId);
            Assert.AreEqual(5, imported.EventMarkers[2].EventId);
            Assert.AreEqual(2, imported.EventIds[0], "Legacy event ids follow the remapped markers.");
            Assert.AreEqual(4, imported.EventIds[1]);
            Assert.AreEqual(5, imported.EventIds[2]);
            Assert.AreEqual(sourceJson, source.ToJson(), "Source must stay unchanged.");
        }

        [Test]
        public void FrameClip_EventClosure_CatalogFull_BlocksClip_ArtOnlyStillWorks()
        {
            var source = MakeFrameSource();
            source.Events = new List<SpriteEventDef> { new SpriteEventDef { Id = 7, Name = "Custom" } };
            source.Clips[0].AddEventMarker(0, 7, 0f);
            source.Clips[0].EnsureFrameData();

            var dest = MakePartsDest();
            dest.Events = new List<SpriteEventDef>();
            for (int id = 1; id <= 255; id++)
                dest.Events.Add(new SpriteEventDef { Id = (byte)id, Name = "E" + id });
            string destJson = dest.ToJson();

            var blocked = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, new List<int> { 0 }, null, null, null);
            Assert.IsFalse(blocked.Ok);
            StringAssert.Contains("Event catalog is full", blocked.Reason);
            StringAssert.Contains("art only", blocked.Reason);
            Assert.AreEqual(destJson, dest.ToJson(), "Blocked plan must change nothing.");

            // Art-only extraction stays available with the clip deselected.
            var artOnly = SpriteProfileClipImport.PlanImport(
                source, dest, new List<int> { 0 }, null, null, null, null, null);
            Assert.IsTrue(artOnly.Ok, artOnly.Reason);
            Assert.AreEqual(0, artOnly.EventsToAdd.Count);
        }

        [Test]
        public void FrameClip_SocketClosure_KeepsDestIdentity_OrCopiesEntry()
        {
            var source = MakeFrameSource();
            source.Clips[0].Sockets.Add(new FrameSocketDef
            {
                Name = "Weapon", FrameIndex = 0,
                LocalPosition = new Vector2(4f, 0f), LocalScale = Vector2.one,
            });
            source.SocketCatalog ??= new SpriteSocketCatalog();
            var srcItem = source.SocketCatalog.Ensure("Weapon");
            srcItem.SocketId = "weapon";
            srcItem.Columns = 2;
            var backRef = ScriptableObject.CreateInstance<ScriptableSpriteSheetProfile>();
            try
            {
                srcItem.Profile = backRef;
                string sourceJson = source.ToJson();

                // Destination has no entry: the closure copies the source item,
                // blanking its profile back-reference.
                var dest = MakePartsDest();
                var plan = SpriteProfileClipImport.PlanImport(
                    source, dest, null, null, new List<int> { 0 }, null, null, null);
                Assert.IsTrue(plan.Ok, plan.Reason);
                Assert.AreEqual(1, plan.SocketCatalogActions.Count);
                Assert.IsFalse(plan.SocketCatalogActions[0].KeepExisting);
                Assert.AreEqual("weapon", plan.SocketCatalogActions[0].DestinationSocketId);
                Assert.IsNull(plan.SocketCatalogActions[0].ItemToAdd.Profile,
                    "No cross-profile references from imported socket entries.");
                string notes = string.Join("\n", plan.Notes.ToArray());
                StringAssert.Contains("not imported", notes,
                    "Profile-level socket motions must be called out, never silently lost.");

                var result = SpriteProfileClipImport.Apply(plan, source, dest);
                Assert.IsTrue(result.Ok, result.Reason);
                Assert.IsNotNull(dest.SocketCatalog);
                var added = dest.SocketCatalog.Find("Weapon");
                Assert.IsNotNull(added);
                Assert.AreEqual("weapon", SpriteSocketIdUtility.Canonical(added.SocketId, added.SocketName));
                Assert.IsNull(added.Profile);
                Assert.AreEqual(sourceJson, source.ToJson(), "Source must stay unchanged.");

                // Destination already catalogs the name: its stable identity wins.
                var dest2 = MakePartsDest();
                dest2.SocketCatalog = new SpriteSocketCatalog();
                dest2.SocketCatalog.Ensure("Weapon").SocketId = "hero.weapon";
                var plan2 = SpriteProfileClipImport.PlanImport(
                    source, dest2, null, null, new List<int> { 0 }, null, null, null);
                Assert.IsTrue(plan2.Ok, plan2.Reason);
                Assert.AreEqual(1, plan2.SocketCatalogActions.Count);
                Assert.IsTrue(plan2.SocketCatalogActions[0].KeepExisting);
                Assert.AreEqual("hero.weapon", plan2.SocketCatalogActions[0].DestinationSocketId);
                var result2 = SpriteProfileClipImport.Apply(plan2, source, dest2);
                Assert.IsTrue(result2.Ok, result2.Reason);
                Assert.AreEqual(1, dest2.SocketCatalog.Items.Count, "Existing entry kept; nothing appended.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(backRef);
            }
        }

        [Test]
        public void FrameClip_EventClosure_UncataloguedId_BlocksClip()
        {
            var source = MakeFrameSource();
            source.Events = new List<SpriteEventDef>();
            source.Clips[0].AddEventMarker(0, 9, 0f);
            source.Clips[0].EnsureFrameData();
            string sourceJson = source.ToJson();

            var dest = MakePartsDest();
            dest.Events = new List<SpriteEventDef> { new SpriteEventDef { Id = 9, Name = "Hit" } };
            string destJson = dest.ToJson();

            var blocked = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, new List<int> { 0 }, null, null, null);
            Assert.IsFalse(blocked.Ok);
            StringAssert.Contains("no catalog entry", blocked.Reason);
            StringAssert.Contains("art only", blocked.Reason);
            Assert.AreEqual(destJson, dest.ToJson(), "Blocked plan must change nothing.");
            Assert.AreEqual(sourceJson, source.ToJson(), "Source must stay unchanged.");
        }

        [Test]
        public void FrameClip_EventClosure_NameMatch_IsCaseInsensitive()
        {
            var source = MakeFrameSource();
            source.Events = new List<SpriteEventDef> { new SpriteEventDef { Id = 1, Name = "Footstep" } };
            source.Clips[0].AddEventMarker(0, 1, 0f);
            source.Clips[0].EnsureFrameData();

            var dest = MakePartsDest();
            dest.Events = new List<SpriteEventDef> { new SpriteEventDef { Id = 4, Name = "footstep" } };

            var plan = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, new List<int> { 0 }, null, null, null);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(4, plan.EventIdRemap[1]);
            Assert.AreEqual(0, plan.EventsToAdd.Count);

            var result = SpriteProfileClipImport.Apply(plan, source, dest);
            Assert.IsTrue(result.Ok, result.Reason);
            var imported = dest.Clips[dest.Clips.Count - 1];
            Assert.AreEqual(4, imported.EventMarkers[0].EventId);
            Assert.AreEqual(1, dest.Events.Count, "Matched by name; no extra catalog row.");
        }

        [Test]
        public void FrameClip_Apply_DoesNotCallEnsureFrameDataOnSource()
        {
            var source = MakeFrameSource();
            source.Events = new List<SpriteEventDef> { new SpriteEventDef { Id = 1, Name = "Footstep" } };
            source.Clips[0].AddEventMarker(0, 1, 0f);
            byte[] eventIds = source.Clips[0].EventIds;
            string sourceJson = source.ToJson();

            var dest = MakePartsDest();
            var plan = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, new List<int> { 0 }, null, null, null);
            Assert.IsTrue(plan.Ok, plan.Reason);
            var result = SpriteProfileClipImport.Apply(plan, source, dest);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreSame(eventIds, source.Clips[0].EventIds, "Clone must not EnsureFrameData the source.");
            Assert.AreEqual(sourceJson, source.ToJson(), "Source must stay unchanged.");
        }

        [Test]
        public void FrameClip_NoEventsOrSockets_NoCatalogChanges()
        {
            var source = MakeFrameSource();
            var dest = MakePartsDest();
            dest.Events = new List<SpriteEventDef> { new SpriteEventDef { Id = 1, Name = "Hit" } };
            Assert.IsNotNull(dest.SocketCatalog, "Fresh profiles carry an empty catalog.");

            var plan = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, new List<int> { 0 }, null, null, null);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(0, plan.EventsToAdd.Count);
            Assert.AreEqual(0, plan.SocketCatalogActions.Count);
            var result = SpriteProfileClipImport.Apply(plan, source, dest);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(1, dest.Events.Count, "Plain clips must not drag catalog entries along.");
            Assert.AreEqual(0, dest.SocketCatalog.Items.Count,
                "No socket catalog entries are added without clip sockets.");
        }

        [Test]
        public void OnionParity_EditorResolveMatchesRuntimeBlob_AppearanceChannel()
        {
            var profile = MakePartsDest();
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "B0", AppearanceId = "b0", SheetIndex = 0, CellIndex = 0,
            });
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "B1", AppearanceId = "b1", SheetIndex = 0, CellIndex = 1,
            });
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyAppearance(
                profile, walk, "body", 0.0f, "b0").WroteAppearance);
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyAppearance(
                profile, walk, "body", 0.2f, "b1").WroteAppearance);

            Assert.IsTrue(SpritePartsClipConversion.TryBuildBlob(
                profile, Allocator.Temp, out var blob, out string blobError), blobError);
            try
            {
                int body = FindBlobSlot(ref blob.Value, "body");
                int weapon = FindBlobSlot(ref blob.Value, "weapon");
                Assert.GreaterOrEqual(body, 0);
                // Ghost times: between keys, in the hold segment, after the last key.
                foreach (float t in new[] { 0.05f, 0.15f, 0.25f, 0.5f })
                {
                    string editorId = SpritePartsAuthoringOps.SampleKeyedAppearanceId(
                        profile, walk, "body", t);
                    int sampled = SpritePartsSampler.SampleAppearanceIndex(
                        ref blob.Value, walk, body, t);
                    Assert.GreaterOrEqual(sampled, 0, "Keyed appearance must resolve at t=" + t);
                    string runtimeId = blob.Value.Appearances[sampled].AppearanceId.ToString();
                    Assert.AreEqual(editorId, runtimeId,
                        "Editor (canvas/onion) and runtime must agree on the appearance channel at t=" + t);
                }
                // Unkeyed slot: no channel keys - editor resolves empty, runtime -1.
                Assert.AreEqual(string.Empty,
                    SpritePartsAuthoringOps.SampleKeyedAppearanceId(profile, walk, "weapon", 0.3f));
                Assert.AreEqual(-1, SpritePartsSampler.SampleAppearanceIndex(
                    ref blob.Value, walk, weapon, 0.3f));
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void OnionParity_LegacyPoseKeyIds_MatchRuntimeBlob()
        {
            var profile = MakePartsDest();
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "W0", AppearanceId = "w0", SheetIndex = 0, CellIndex = 1,
            });
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(
                profile, walk, "weapon", 0.1f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = Vector2.zero, Rotation = 0f, Scale = Vector2.one,
                }).WroteKey);
            // Legacy data: id mixed into the pose key, no appearance channel.
            SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], "weapon")
                .Keys[1].AppearanceId = "w0";

            Assert.IsTrue(SpritePartsClipConversion.TryBuildBlob(
                profile, Allocator.Temp, out var blob, out string blobError), blobError);
            try
            {
                int weapon = FindBlobSlot(ref blob.Value, "weapon");
                foreach (float t in new[] { 0.05f, 0.15f, 0.4f })
                {
                    string editorId = SpritePartsAuthoringOps.SampleKeyedAppearanceId(
                        profile, walk, "weapon", t);
                    int sampled = SpritePartsSampler.SampleAppearanceIndex(
                        ref blob.Value, walk, weapon, t);
                    Assert.GreaterOrEqual(sampled, 0, "Legacy keyed id must still resolve at t=" + t);
                    Assert.AreEqual(editorId, blob.Value.Appearances[sampled].AppearanceId.ToString(),
                        "Legacy fallback must agree between editor and runtime at t=" + t);
                }
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Import_Provenance_StampedOnClips()
        {
            var source = MakeFrameSource();
            var dest = MakePartsDest();

            var plan = SpriteProfileClipImport.PlanImport(
                source, dest, null, null, new List<int> { 0 }, null, null, null);
            Assert.IsTrue(plan.Ok, plan.Reason);
            var result = SpriteProfileClipImport.Apply(plan, source, dest,
                new SpriteProfileArtImport.ImportSourceInfo { Guid = "cliplib.guid", Path = "Assets/ClipLib.asset" });
            Assert.IsTrue(result.Ok, result.Reason);

            var frameClip = dest.Clips[dest.Clips.Count - 1];
            Assert.IsNotNull(frameClip.Import);
            Assert.AreEqual("cliplib.guid", frameClip.Import.SourceGuid);
            StringAssert.Contains("frame clip 'Walk'", frameClip.Import.SourceItem);
            Assert.IsNotEmpty(frameClip.Import.ImportedUtc);
        }

        [Test]
        public void FrameSequence_Provenance_StampedOnCreatedAppearancesOnly()
        {
            var profile = MakePartsDest();
            // Pre-existing appearance for cell 0: reused, provenance untouched.
            profile.PartsAppearances.Add(new SpritePartAppearanceDef
            {
                Name = "Pre", AppearanceId = "pre.cell0", SheetIndex = 0, CellIndex = 0,
            });
            profile.Clips.Add(new SpriteClipDef
            {
                Name = "Blink", SheetIndex = 0, Frames = new[] { 0, 1 },
                FrameRate = 8f, FrameDurationScales = new[] { 1f, 1f },
            });
            profile.Clips[0].EnsureFrameData();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");

            var plan = SpritePartsFrameSequenceImport.PlanImport(
                profile, 0, walk, "body", 0f, true);
            Assert.IsTrue(plan.Ok, plan.Reason);
            var result = SpritePartsFrameSequenceImport.Apply(plan, profile,
                new SpriteProfileArtImport.ImportSourceInfo { Guid = "self.profile" });
            Assert.IsTrue(result.Ok, result.Reason);

            var reused = SpritePartsAuthoringOps.FindAppearance(profile, "pre.cell0");
            Assert.IsNull(reused.Import, "Reused appearances keep what they had (nothing).");

            string cell1Id = SpritePartsAuthoringOps.SampleKeyedAppearanceId(profile, walk, "body", 0.2f);
            var created = SpritePartsAuthoringOps.FindAppearance(profile, cell1Id);
            Assert.IsNotNull(created.Import);
            Assert.AreEqual("self.profile", created.Import.SourceGuid);
            StringAssert.Contains("frame sequence 'Blink'", created.Import.SourceItem);
        }

        [Test]
        public void FrameSequence_Repeat1_MatchesSingleTraversal()
        {
            var profile = MakePartsDest();
            profile.Clips.Add(new SpriteClipDef
            {
                Name = "Blink", SheetIndex = 0, Frames = new[] { 0, 1 },
                FrameRate = 8f, FrameDurationScales = new[] { 1f, 1f },
            });
            profile.Clips[0].EnsureFrameData();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");

            var plan = SpritePartsFrameSequenceImport.PlanImport(
                profile, 0, walk, "body", 0f, true, repeatCount: 1);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(1, plan.RepeatCount);
            Assert.AreEqual(2, plan.KeyCount, "Key count equals the frame count.");
            Assert.AreEqual(0.25f, plan.EndTime, 1e-4f);
            var result = SpritePartsFrameSequenceImport.Apply(plan, profile);
            Assert.IsTrue(result.Ok, result.Reason);
            var channel = SpritePartsAuthoringOps.FindAppearanceTrack(
                profile.PartsClips[walk], "body");
            Assert.AreEqual(2, channel.Keys.Count);
        }

        [Test]
        public void FrameSequence_Repeat3_SixKeys_SameCellSameId_PoseUntouched()
        {
            var profile = MakePartsDest();
            profile.Clips.Add(new SpriteClipDef
            {
                Name = "Blink", SheetIndex = 0, Frames = new[] { 0, 1 },
                FrameRate = 8f, FrameDurationScales = new[] { 1f, 1f },
            });
            profile.Clips[0].EnsureFrameData();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            Assert.IsTrue(SpritePartsAuthoringOps.WriteKeyPose(
                profile, walk, "body", 0.1f,
                new SpritePartsAuthoringOps.PoseEdit
                {
                    Position = new Vector2(1f, 0f), Rotation = 0f, Scale = Vector2.one,
                }).WroteKey);
            var poseBefore = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], "body");
            int poseKeyCount = poseBefore.Keys.Count;
            string jsonBefore = profile.ToJson();

            var plan = SpritePartsFrameSequenceImport.PlanImport(
                profile, 0, walk, "body", 0f, true, repeatCount: 3);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(3, plan.RepeatCount);
            Assert.AreEqual(6, plan.KeyCount);
            Assert.AreEqual(0.75f, plan.EndTime, 1e-4f, "Three 0.25s traversals.");
            Assert.AreEqual(jsonBefore, profile.ToJson(), "Plan must stay read-only.");

            var result = SpritePartsFrameSequenceImport.Apply(plan, profile);
            Assert.IsTrue(result.Ok, result.Reason);

            var channel = SpritePartsAuthoringOps.FindAppearanceTrack(
                profile.PartsClips[walk], "body");
            Assert.IsNotNull(channel);
            Assert.AreEqual(6, channel.Keys.Count);
            // Times advance across repeats: 0, .125, .25, .375, .5, .625.
            Assert.AreEqual(0.125f, channel.Keys[1].Time - channel.Keys[0].Time, 1e-4f);
            Assert.AreEqual(0.625f, channel.Keys[5].Time, 1e-4f);
            // Same cell reuses the same appearance id in every repeat.
            Assert.AreEqual(channel.Keys[0].AppearanceId, channel.Keys[2].AppearanceId);
            Assert.AreEqual(channel.Keys[0].AppearanceId, channel.Keys[4].AppearanceId);
            Assert.AreEqual(channel.Keys[1].AppearanceId, channel.Keys[3].AppearanceId);
            Assert.AreEqual(channel.Keys[1].AppearanceId, channel.Keys[5].AppearanceId);
            Assert.AreNotEqual(channel.Keys[0].AppearanceId, channel.Keys[1].AppearanceId);
            // Only two appearances exist: one per distinct cell.
            Assert.AreEqual(2, profile.PartsAppearances.Count);
            // Pose untouched; duration extended to the repeated end.
            var poseAfter = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[walk], "body");
            Assert.AreEqual(poseKeyCount, poseAfter.Keys.Count);
            Assert.AreEqual(1f, poseAfter.Keys[poseAfter.Keys.Count - 1].Position.x, 1e-5f);
            Assert.AreEqual(0.75f, profile.PartsClips[walk].Duration, 1e-4f);
            // Sampling cycles the cells three times.
            Assert.AreEqual(channel.Keys[0].AppearanceId,
                SpritePartsAuthoringOps.SampleKeyedAppearanceId(profile, walk, "body", 0.26f));
            Assert.AreEqual(channel.Keys[1].AppearanceId,
                SpritePartsAuthoringOps.SampleKeyedAppearanceId(profile, walk, "body", 0.38f));
            Assert.IsTrue(SpritePartsValidation.Validate(profile).Ok);
        }

        [Test]
        public void FrameSequence_RepeatBelowOne_TreatedAsOne_WithNote()
        {
            var profile = MakePartsDest();
            profile.Clips.Add(new SpriteClipDef
            {
                Name = "Blink", SheetIndex = 0, Frames = new[] { 0, 1 },
                FrameRate = 8f, FrameDurationScales = new[] { 1f, 1f },
            });
            profile.Clips[0].EnsureFrameData();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");

            var plan = SpritePartsFrameSequenceImport.PlanImport(
                profile, 0, walk, "body", 0f, true, repeatCount: 0);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(1, plan.RepeatCount);
            Assert.AreEqual(2, plan.KeyCount);
            StringAssert.Contains("treated as 1", string.Join("\n", plan.Notes.ToArray()));

            var negative = SpritePartsFrameSequenceImport.PlanImport(
                profile, 0, walk, "body", 0f, true, repeatCount: -3);
            Assert.IsTrue(negative.Ok, negative.Reason);
            Assert.AreEqual(1, negative.RepeatCount);
        }

        [Test]
        public void FrameSequence_RepeatAboveCap_IsCapped()
        {
            var profile = MakePartsDest();
            profile.Clips.Add(new SpriteClipDef
            {
                Name = "Blink", SheetIndex = 0, Frames = new[] { 0 },
                FrameRate = 8f, FrameDurationScales = new[] { 1f },
            });
            profile.Clips[0].EnsureFrameData();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");

            var plan = SpritePartsFrameSequenceImport.PlanImport(
                profile, 0, walk, "body", 0f, true, repeatCount: 9999);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(256, plan.RepeatCount);
            Assert.AreEqual(256, plan.KeyCount);
            StringAssert.Contains("capped", string.Join("\n", plan.Notes.ToArray()));
        }

        [Test]
        public void FrameSequence_RepeatOverrun_WithoutExtend_Blocks_Unchanged()
        {
            var profile = MakePartsDest();
            profile.Clips.Add(new SpriteClipDef
            {
                Name = "Blink", SheetIndex = 0, Frames = new[] { 0, 1 },
                FrameRate = 8f, FrameDurationScales = new[] { 1f, 1f },
            });
            profile.Clips[0].EnsureFrameData();
            int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            string jsonBefore = profile.ToJson();

            var plan = SpritePartsFrameSequenceImport.PlanImport(
                profile, 0, walk, "body", 0.5f, false, repeatCount: 2);
            Assert.IsFalse(plan.Ok);
            StringAssert.Contains("duration", plan.Reason);
            Assert.AreEqual(jsonBefore, profile.ToJson());
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpritePartsClipRetargetTests
    {
        static SpriteSheetProfile Rig(Vector2 bodyRest)
        {
            var p = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(p);
            SpritePartsAuthoringOps.FindSlot(p, "body").RestPosition = bodyRest;
            return p;
        }

        static void AddBodyTrack(SpriteSheetProfile profile, int clipIndex, Vector2 keyPos, float time)
        {
            profile.PartsClips[clipIndex].Tracks.Add(new SpritePartsTrackDef
            {
                SlotId = "body",
                Kind = SpritePartsTrackKind.Pose,
                Keys = new List<SpritePartsKeyDef>
                {
                    new SpritePartsKeyDef
                    {
                        Time = time,
                        Position = keyPos,
                        Rotation = 15f,
                        Scale = new Vector2(1.5f, 1f),
                    },
                },
            });
        }

        [Test]
        public void Retarget_PreservesKeyTimes()
        {
            var source = Rig(Vector2.zero);
            AddBodyTrack(source, 1, new Vector2(1f, 0f), 0.25f);
            var dest = Rig(new Vector2(10f, 0f));
            var map = new Dictionary<string, string> { { "body", "body" } };

            var plan = SpritePartsClipRetarget.PlanRetarget(
                source, 1, dest, map, SpritePartsClipRetarget.RestDeltaMode.RestDelta);
            Assert.IsTrue(plan.Ok, plan.Reason);
            var result = SpritePartsClipRetarget.Apply(plan, source, dest);
            Assert.IsTrue(result.Ok, result.Reason);

            var clip = dest.PartsClips[result.DestinationClipIndex];
            Assert.AreEqual(1, clip.Tracks.Count);
            Assert.AreEqual(0.25f, clip.Tracks[0].Keys[0].Time, 1e-5f);
        }

        [Test]
        public void Retarget_RestOnlyDifference_RemapsLocalTrs()
        {
            var source = Rig(new Vector2(0f, 0f));
            SpritePartsAuthoringOps.FindSlot(source, "body").RestRotation = 0f;
            SpritePartsAuthoringOps.FindSlot(source, "body").RestScale = Vector2.one;
            AddBodyTrack(source, 1, new Vector2(1f, 2f), 0.1f);
            source.PartsClips[1].Tracks[0].Keys[0].Rotation = 20f;
            source.PartsClips[1].Tracks[0].Keys[0].Scale = new Vector2(2f, 0.5f);

            var dest = Rig(new Vector2(10f, 4f));
            SpritePartsAuthoringOps.FindSlot(dest, "body").RestRotation = 90f;
            SpritePartsAuthoringOps.FindSlot(dest, "body").RestScale = new Vector2(2f, 2f);
            var restBefore = SpritePartsAuthoringOps.FindSlot(dest, "body").RestPosition;
            string destJson = dest.ToJson();

            var plan = SpritePartsClipRetarget.PlanRetarget(
                source, 1, dest, new Dictionary<string, string> { { "body", "body" } },
                SpritePartsClipRetarget.RestDeltaMode.RestDelta);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.AreEqual(destJson, dest.ToJson(), "Plan must not mutate destination rest.");

            var result = SpritePartsClipRetarget.Apply(plan, source, dest);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(restBefore, SpritePartsAuthoringOps.FindSlot(dest, "body").RestPosition,
                "Apply must never rewrite destination rest.");

            var key = dest.PartsClips[result.DestinationClipIndex].Tracks[0].Keys[0];
            Assert.AreEqual(new Vector2(11f, 6f), key.Position);
            Assert.AreEqual(110f, key.Rotation, 1e-3f);
            Assert.AreEqual(new Vector2(4f, 1f), key.Scale);
        }

        [Test]
        public void Retarget_UnresolvedSlotMap_BlocksApply()
        {
            var source = Rig(Vector2.zero);
            AddBodyTrack(source, 1, Vector2.one, 0f);
            source.PartsClips[1].Tracks.Add(new SpritePartsTrackDef
            {
                SlotId = "weapon",
                Kind = SpritePartsTrackKind.Pose,
                Keys = new List<SpritePartsKeyDef> { new SpritePartsKeyDef { Time = 0.5f } },
            });
            var dest = Rig(Vector2.zero);
            var map = new Dictionary<string, string> { { "body", "body" } };

            var plan = SpritePartsClipRetarget.PlanRetarget(
                source, 1, dest, map, SpritePartsClipRetarget.RestDeltaMode.RestDelta);
            Assert.IsFalse(plan.Ok);
            Assert.IsTrue(plan.Reason.Contains("weapon"), plan.Reason);

            var result = SpritePartsClipRetarget.Apply(plan, source, dest);
            Assert.IsFalse(result.Ok);
            Assert.AreEqual(2, dest.PartsClips.Count, "Failed apply must not add a clip.");
        }

        [Test]
        public void Retarget_IsOneUndoTransaction()
        {
            var source = Rig(Vector2.zero);
            AddBodyTrack(source, 1, new Vector2(1f, 0f), 0.2f);
            var dest = Rig(new Vector2(3f, 0f));
            var asset = ScriptableObject.CreateInstance<ScriptableSpriteSheetProfile>();
            asset.Data = dest;
            try
            {
                int clipsBefore = dest.PartsClips.Count;
                Undo.RegisterCompleteObjectUndo(asset, "Retarget Parts Clip");
                var plan = SpritePartsClipRetarget.PlanRetarget(
                    source, 1, dest, new Dictionary<string, string> { { "body", "body" } });
                var result = SpritePartsClipRetarget.Apply(plan, source, dest);
                Assert.IsTrue(result.Ok, result.Reason);
                Assert.AreEqual(clipsBefore + 1, asset.Data.PartsClips.Count);
                Undo.PerformUndo();
                Assert.AreEqual(clipsBefore, asset.Data.PartsClips.Count);
            }
            finally
            {
                Undo.ClearAll();
                Object.DestroyImmediate(asset);
            }
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Shear (Spine): matrices, keys on their own channel, children, and the part's post-transform.</summary>
    public sealed class SpritePartsShearTests
    {
        [Test]
        public void Shear_Tilts_Each_Axis()
        {
            var plain = SpritePartsHierarchy.LocalMatrix(new float2(1f, 2f), 10f, new float2(2f, 3f));
            var zero = SpritePartsHierarchy.LocalMatrix(new float2(1f, 2f), 10f, new float2(2f, 3f), float2.zero);
            Assert.That(math.all(math.abs(plain.c0 - zero.c0) < 1e-6f) && math.all(math.abs(plain.c1 - zero.c1) < 1e-6f));
            var m = SpritePartsHierarchy.LocalMatrix(float2.zero, 0f, new float2(1f, 2f), new float2(0f, 30f));
            Assert.AreEqual(1f, m.c0.x, 1e-5f, "X axis untouched by a Y shear.");
            Assert.AreEqual(math.cos(math.radians(120f)) * 2f, m.c1.x, 1e-5f, "Y axis turned 30 degrees further.");
            Assert.AreEqual(math.sin(math.radians(120f)) * 2f, m.c1.y, 1e-5f);
        }

        static BlobAssetReference<SpritePartsSetBlob> Build(bool keyHasShear)
        {
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput { Name = "a", SlotId = "a", RestScale = new float2(1f, 1f), RestShear = new float2(10f, 0f) },
                new SpritePartsSetBuilder.SlotInput { Name = "b", SlotId = "b", ParentSlotId = "a", RestPosition = new float2(0f, 1f), RestScale = new float2(1f, 1f) },
            };
            SpritePartsSetBuilder.KeyInput Key(float t, float shear) => new SpritePartsSetBuilder.KeyInput
            {
                Time = t, Scale = new float2(1f, 1f), AppearanceId = string.Empty, Shear = new float2(shear, 0f), HasShear = keyHasShear,
            };
            var clips = new[]
            {
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "c", ClipId = "c", Duration = 1f, SpeedMultiplier = 1f, WrapMode = (byte)SpritePartsWrap.Once,
                    Tracks = new[] { new SpritePartsSetBuilder.TrackInput { SlotId = "a", Keys = new[] { Key(0f, 0f), Key(1f, 40f) } } },
                },
            };
            return SpritePartsSetBuilder.Build(Allocator.Temp, slots, System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                clips, System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
        }

        [Test]
        public void Shear_Keys_Blend_Only_When_Keyed()
        {
            var keyed = Build(true);
            var unkeyed = Build(false);
            try
            {
                SpritePartsSampler.SampleSlot(ref keyed.Value, 0, 0, 0.5f, out var pose);
                Assert.AreEqual(20f, pose.Shear.x, 1e-4f);
                SpritePartsSampler.SampleSlot(ref unkeyed.Value, 0, 0, 0.5f, out pose);
                Assert.AreEqual(10f, pose.Shear.x, 1e-4f, "Keys without the shear channel leave the setup shear.");
            }
            finally
            {
                keyed.Dispose();
                unkeyed.Dispose();
            }
        }

        [Test]
        public void Children_Follow_A_Sheared_Parent()
        {
            var blob = Build(true);
            var local = new NativeArray<SpritePartsSampler.Pose>(2, Allocator.Temp);
            var ltr = new NativeArray<float4x4>(2, Allocator.Temp);
            try
            {
                SpritePartsPoseWriter.EvaluateEditor(ref blob.Value, 0, 1f, local, ltr, default);
                // Parent shear x 40: its X axis tilts, the Y axis (where the child sits) does not.
                Assert.AreEqual(0f, ltr[1].c3.x, 1e-4f);
                Assert.AreEqual(1f, ltr[1].c3.y, 1e-4f);
                Assert.AreEqual(math.cos(math.radians(40f)), ltr[1].c0.x, 1e-4f, "The child inherits the tilted X axis.");
            }
            finally
            {
                local.Dispose();
                ltr.Dispose();
                blob.Dispose();
            }
        }

        [Test]
        public void The_Part_Entity_Gets_Shear_In_Its_Post_Transform()
        {
            var blob = Build(true);
            using var world = new World("parts-shear");
            var em = world.EntityManager;
            var created = SpritePartsEntityFactory.Create(em, blob, float3.zero, playing: false);
            try
            {
                SpritePartsPoseWriter.Apply(em, created.Root);
                var post = em.GetComponentData<PostTransformMatrix>(created.Parts[0]).Value;
                Assert.AreEqual(math.cos(math.radians(0f)), post.c0.x, 1e-4f, "At the clip start the key holds shear 0.");
                SpriteParts.SeekNormalized(em, created.Root, 1f);
                post = em.GetComponentData<PostTransformMatrix>(created.Parts[0]).Value;
                Assert.AreEqual(math.sin(math.radians(40f)), post.c0.y, 1e-4f);
            }
            finally
            {
                created.Parts.Dispose();
                blob.Dispose();
            }
        }

        [Test]
        public void Shear_Keys_Stay_Out_Of_Other_Edits()
        {
            var profile = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            int clip = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            var body = SpritePartsAuthoringOps.FindSlot(profile, "body");
            body.RestShear = new Vector2(5f, 0f);
            Assert.IsTrue(SpritePartsAuthoringOps.SetShearKey(profile, clip, "body", 0.5f, new Vector2(25f, 0f)).Ok);
            var keys = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[clip], "body").Keys;
            var anchor = keys.Find(k => k.Time == 0f);
            Assert.AreEqual(5f, anchor.Shear.x, "The first shear key anchors the setup shear at 0.");
            Assert.AreNotEqual(SpritePartsKeyChannel.None, anchor.Channels & SpritePartsKeyChannel.Shear);

            // A normal pose edit at 0.5 keys the other channels and leaves the shear alone.
            SpritePartsAuthoringOps.ApplyPoseEdit(profile, SpritePartsStudioMode.Animate, clip, "body", 0.5f,
                new SpritePartsAuthoringOps.PoseEdit { Position = new Vector2(1f, 0f), Scale = Vector2.one }, autoKey: true);
            var key = keys.Find(k => Mathf.Approximately(k.Time, 0.5f));
            Assert.AreEqual(25f, key.Shear.x);
            Assert.AreNotEqual(SpritePartsKeyChannel.None, key.Channels & SpritePartsKeyChannel.Shear);

            Assert.IsTrue(SpritePartsClipConversion.TryBuildBlob(profile, Allocator.Temp, out var blob, out var error), error);
            try
            {
                int slot = -1;
                for (int i = 0; i < blob.Value.Slots.Length; i++)
                    if (blob.Value.Slots[i].SlotId.ToString() == "body")
                        slot = i;
                SpritePartsSampler.SampleSlot(ref blob.Value, clip, slot, 0.25f, out var pose);
                Assert.AreEqual(15f, pose.Shear.x, 1e-3f, "Halfway from the anchor (5) to the key (25).");
            }
            finally
            {
                blob.Dispose();
            }
        }
    }
}

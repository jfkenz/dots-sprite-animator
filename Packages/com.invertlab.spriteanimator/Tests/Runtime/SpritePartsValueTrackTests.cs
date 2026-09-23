using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Keyed IK Mix / bend, jiggle Mix and parameter values inside clips.</summary>
    public sealed class SpritePartsValueTrackTests
    {
        // "arm" at the origin, "hand" its child one unit right, "target" straight up. Clip 0 "Reach" keys the IK Mix
        // 0 -> 1 over 1 s; clip 1 "Rest" keys nothing; clip 2 "Mouth" is the parameter's clip (body x 0 -> 10);
        // clip 3 "Talk" keys the parameter 0 -> 1.
        static SpritePartsSetBuilder.ValueKeyInput V(float t, float v) => new SpritePartsSetBuilder.ValueKeyInput { Time = t, Value = v };

        static SpritePartsSetBuilder.ClipInput Clip(string id, SpritePartsSetBuilder.TrackInput[] tracks = null,
            params SpritePartsSetBuilder.ValueTrackInput[] values)
            => new SpritePartsSetBuilder.ClipInput
            {
                Name = id, ClipId = id, Duration = 1f, SpeedMultiplier = 1f, WrapMode = (byte)SpritePartsWrap.Once,
                Tracks = tracks ?? System.Array.Empty<SpritePartsSetBuilder.TrackInput>(), ValueTracks = values,
            };

        static BlobAssetReference<SpritePartsSetBlob> Build()
        {
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput { Name = "arm", SlotId = "arm", RestScale = new float2(1f, 1f) },
                new SpritePartsSetBuilder.SlotInput { Name = "hand", SlotId = "hand", ParentSlotId = "arm", RestPosition = new float2(1f, 0f), RestScale = new float2(1f, 1f) },
                new SpritePartsSetBuilder.SlotInput { Name = "target", SlotId = "target", RestPosition = new float2(0f, 1f), RestScale = new float2(1f, 1f) },
                new SpritePartsSetBuilder.SlotInput { Name = "body", SlotId = "body", RestScale = new float2(1f, 1f) },
            };
            var mouthKeys = new[]
            {
                new SpritePartsSetBuilder.KeyInput { Time = 0f, Scale = new float2(1f, 1f), AppearanceId = string.Empty },
                new SpritePartsSetBuilder.KeyInput { Time = 1f, Position = new float2(10f, 0f), Scale = new float2(1f, 1f), AppearanceId = string.Empty },
            };
            var clips = new[]
            {
                Clip("Reach", null,
                    new SpritePartsSetBuilder.ValueTrackInput { Kind = (byte)SpritePartsValueKind.IkMix, Target = "Reach", Keys = new[] { V(0f, 0f), V(1f, 1f) } },
                    new SpritePartsSetBuilder.ValueTrackInput { Kind = (byte)SpritePartsValueKind.IkBend, Target = "Reach", Keys = new[] { V(0f, 1f), V(0.5f, -1f) } },
                    new SpritePartsSetBuilder.ValueTrackInput { Kind = (byte)SpritePartsValueKind.IkMix, Target = "Nobody", Keys = new[] { V(0f, 1f) } }),
                Clip("Rest"),
                Clip("Mouth", new[] { new SpritePartsSetBuilder.TrackInput { SlotId = "body", Keys = mouthKeys } }),
                Clip("Talk", null,
                    new SpritePartsSetBuilder.ValueTrackInput { Kind = (byte)SpritePartsValueKind.Param, Target = "Mouth", Keys = new[] { V(0f, 0f), V(1f, 1f) } }),
            };
            var ik = new[]
            {
                new SpritePartsSetBuilder.IkInput { Name = "Reach", EffectorSlotId = "hand", TargetSlotId = "target", ChainLength = 1, BendPositive = true, Mix = 0f },
            };
            var parameters = new[]
            {
                new SpritePartsSetBuilder.ParamInput { Name = "Mouth", ClipId = "Mouth", Min = 0f, Max = 1f, Default = 0f },
            };
            return SpritePartsSetBuilder.Build(Allocator.Temp, slots, System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                clips, System.Array.Empty<SpritePartsSetBuilder.SkinInput>(), ik, null, parameters);
        }

        static float Evaluate(BlobAssetReference<SpritePartsSetBlob> blob, in SpritePartsPlayer player, int slot, bool rotation)
        {
            var local = new NativeArray<SpritePartsSampler.Pose>(blob.Value.Slots.Length, Allocator.Temp);
            var ltr = new NativeArray<float4x4>(blob.Value.Slots.Length, Allocator.Temp);
            try
            {
                SpritePartsPoseWriter.EvaluateEditor(ref blob.Value, player, local, ltr, default);
                return rotation ? local[slot].Rotation : local[slot].Position.x;
            }
            finally
            {
                local.Dispose();
                ltr.Dispose();
            }
        }

        static SpritePartsPlayer At(int clip, float time)
        {
            var p = SpritePartsPoseWriter.DefaultPlayer(clip, playing: false);
            p.TimeSeconds = time;
            return p;
        }

        [Test]
        public void Tracks_Resolve_By_Name_And_Unknown_Names_Drop()
        {
            var blob = Build();
            try
            {
                ref var clip = ref blob.Value.Clips[0];
                Assert.AreEqual(2, clip.ValueTracks.Length, "The track for a missing IK is dropped.");
                Assert.AreEqual(0, clip.ValueTracks[0].Targets[0]);
                Assert.AreEqual(1, blob.Value.IkConstraints.Length, "A Mix 0 IK stays because a clip keys it.");
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Keyed_Ik_Mix_Drives_The_Solve()
        {
            var blob = Build();
            try
            {
                Assert.AreEqual(0f, Evaluate(blob, At(1, 0.5f), 0, true), 1e-3f, "No keys: the setup Mix (0).");
                Assert.AreEqual(45f, Evaluate(blob, At(0, 0.5f), 0, true), 1e-2f, "Keyed Mix 0.5: half of the 90 degree turn.");
                Assert.AreEqual(90f, Evaluate(blob, At(0, 1f), 0, true), 1e-2f);
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Bend_Is_Held_And_Values_Crossfade()
        {
            var blob = Build();
            try
            {
                var values = SpritePartsValueTracks.Resolve(ref blob.Value, At(0, 0.4f), 1f, default, false);
                Assert.AreEqual(1f, values.IkBend[0], "Held until the next key.");
                values.Dispose();
                values = SpritePartsValueTracks.Resolve(ref blob.Value, At(0, 0.6f), 1f, default, false);
                Assert.AreEqual(-1f, values.IkBend[0]);
                values.Dispose();

                // Fading from Reach at 1 s (Mix 1) into Rest (setup 0), halfway: 0.5.
                var player = At(1, 0.1f);
                player.PreviousClipIndex = 0;
                player.PreviousTimeSeconds = 1f;
                player.BlendDuration = 1f;
                player.BlendElapsed = 0.5f;
                values = SpritePartsValueTracks.Resolve(ref blob.Value, player, 0.5f, default, false);
                Assert.AreEqual(0.5f, values.IkMix[0], 1e-5f);
                values.Dispose();
                Assert.IsFalse(SpritePartsValueTracks.Resolve(ref blob.Value, At(1, 0f), 1f, default, false).IsCreated,
                    "Nothing keyed: no arrays.");
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void A_Clip_Can_Key_A_Parameter()
        {
            var blob = Build();
            try
            {
                Assert.AreEqual(0f, Evaluate(blob, At(1, 0.5f), 3, false), 1e-4f, "Rest: the default value.");
                Assert.AreEqual(5f, Evaluate(blob, At(3, 0.5f), 3, false), 1e-3f, "Talk keys Mouth 0.5: halfway along its clip.");
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Authoring_Keys_Sample_Rename_And_Remove()
        {
            var clip = new SpritePartsClipDef { Duration = 1f };
            SpritePartsAuthoringOps.SetValueKey(clip, SpritePartsValueKind.JiggleMix, "Hair", 0f, 1f);
            SpritePartsAuthoringOps.SetValueKey(clip, SpritePartsValueKind.JiggleMix, "Hair", 1f, 0f);
            SpritePartsAuthoringOps.SetValueKey(clip, SpritePartsValueKind.JiggleMix, "Hair", 1f, 0.2f);
            Assert.AreEqual(2, clip.ValueTracks[0].Keys.Count, "A key at the same time is replaced.");
            Assert.IsTrue(SpritePartsAuthoringOps.TrySampleValue(clip, SpritePartsValueKind.JiggleMix, "Hair", 0.5f, out float v));
            Assert.AreEqual(0.6f, v, 1e-5f);
            Assert.IsFalse(SpritePartsAuthoringOps.TrySampleValue(clip, SpritePartsValueKind.IkMix, "Hair", 0.5f, out _), "Other kinds are separate.");

            var profile = new SpriteSheetProfile();
            profile.PartsClips.Add(clip);
            Assert.AreEqual(1, SpritePartsAuthoringOps.RenameValueTarget(profile, "Hair", "Ponytail", SpritePartsValueKind.JiggleMix));
            Assert.IsNotNull(SpritePartsAuthoringOps.FindValueTrack(clip, SpritePartsValueKind.JiggleMix, "Ponytail"));
            Assert.IsTrue(SpritePartsAuthoringOps.RemoveValueKey(clip, SpritePartsValueKind.JiggleMix, "Ponytail", 0f));
            Assert.IsTrue(SpritePartsAuthoringOps.RemoveValueKey(clip, SpritePartsValueKind.JiggleMix, "Ponytail", 1f));
            Assert.AreEqual(0, clip.ValueTracks.Count, "The emptied track goes.");
        }
    }
}

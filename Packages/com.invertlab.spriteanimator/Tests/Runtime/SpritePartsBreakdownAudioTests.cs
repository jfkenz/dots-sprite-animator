using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>Breakdown keys (favor between neighbours) and audio events.</summary>
    public sealed class SpritePartsBreakdownAudioTests
    {
        static SpriteSheetProfile Profile(out int clipIndex)
        {
            var profile = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            clipIndex = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
            var clip = profile.PartsClips[clipIndex];
            clip.Duration = 1f;
            clip.Tracks.RemoveAll(t => SpritePartIdUtility.Canonical(t.SlotId) == "body");
            clip.Tracks.Add(new SpritePartsTrackDef
            {
                SlotId = "body",
                Keys = new List<SpritePartsKeyDef>
                {
                    new SpritePartsKeyDef { Time = 0f, Position = new Vector2(0f, 0f), Rotation = 350f },
                    new SpritePartsKeyDef { Time = 1f, Position = new Vector2(10f, 4f), Rotation = 30f, Channels = SpritePartsKeyChannel.Position | SpritePartsKeyChannel.Rotation },
                },
            });
            return profile;
        }

        [Test]
        public void Breakdown_Keys_Between_The_Neighbouring_Keys()
        {
            var profile = Profile(out int clip);
            var result = SpritePartsAuthoringOps.KeyBreakdown(profile, clip, "body", 0.5f, 0.25f);
            Assert.IsTrue(result.Ok, result.Reason);
            var key = SpritePartsAuthoringOps.FindTrack(profile.PartsClips[clip], "body").Keys.Find(k => Mathf.Approximately(k.Time, 0.5f));
            Assert.IsNotNull(key);
            Assert.AreEqual(2.5f, key.Position.x, 1e-4f);
            Assert.AreEqual(1f, key.Position.y, 1e-4f);
            Assert.AreEqual(360f, key.Rotation, 1e-3f, "350 -> 30 turns the short way (+40): a quarter is +10.");
            Assert.AreEqual(SpritePartsKeyChannel.None, key.Channels & SpritePartsKeyChannel.Scale,
                "Scale is only keyed at 0: no key after, so no scale breakdown.");

            Assert.IsTrue(SpritePartsAuthoringOps.KeyBreakdown(profile, clip, "body", 0.5f, 1f).Ok, "Re-keying the same time.");
            Assert.AreEqual(10f, key.Position.x, 1e-4f, "The key at the playhead is updated, not used as a neighbour.");
            Assert.IsFalse(SpritePartsAuthoringOps.KeyBreakdown(profile, clip, "body", 1f, 0.5f).Ok, "Nothing after the last key.");
        }

        [Test]
        public void Event_Audio_Bakes_An_Index_And_Plays_Through_The_Handler()
        {
            var profile = Profile(out int clip);
            var sound = AudioClip.Create("step", 128, 1, 44100, false);
            var played = new List<(AudioClip clip, float volume, float balance)>();
            try
            {
                profile.PartsClips[clip].Events.Add(new SpritePartsEventMarker { Time = 0.5f, EventId = 1, Audio = sound, Volume = 0.6f, Balance = -0.5f });
                profile.PartsClips[clip].Events.Add(new SpritePartsEventMarker { Time = 0.7f, EventId = 1 });
                CollectionAssert.AreEqual(new[] { sound }, SpritePartsClipConversion.EventAudio(profile.PartsClips));
                Assert.IsTrue(SpritePartsClipConversion.TryBuildBlob(profile, Allocator.Temp, out var blob, out var error), error);
                using var world = new World("parts-audio");
                try
                {
                    ref var events = ref blob.Value.Clips[clip].Events;
                    Assert.AreEqual(0, events[0].AudioIndex);
                    Assert.AreEqual(0.6f, events[0].Volume, 1e-6f);
                    Assert.AreEqual(-1, events[1].AudioIndex, "No sound.");

                    var em = world.EntityManager;
                    var e = em.CreateEntity();
                    em.AddComponentData(e, new SpritePartsSetRef { Set = blob });
                    em.AddComponentObject(e, new SpritePartsAudioBank { Clips = new[] { sound } });
                    SpritePartsAudio.Handler = (_, c, v, b) => played.Add((c, v, b));
                    SpritePartsEventFiring.Fire(em, new SpritePartsEventFiring.Tick { Entity = e, ClipIndex = clip, From = 0.4f, To = 0.8f, Playing = true });
                    Assert.AreEqual(1, played.Count, "Only the event with a sound plays.");
                    Assert.AreSame(sound, played[0].clip);
                    Assert.AreEqual(-0.5f, played[0].balance, 1e-6f);
                    Assert.AreEqual(2, em.GetBuffer<SpriteAnimEventBuffer>(e).Length, "Both still raise events.");
                }
                finally
                {
                    blob.Dispose();
                }
            }
            finally
            {
                SpritePartsAudio.Handler = null;
                Object.DestroyImmediate(sound);
            }
        }

        [Test]
        public void Default_Event_Inputs_Stay_Silent()
        {
            var slots = new[] { new SpritePartsSetBuilder.SlotInput { Name = "a", SlotId = "a", RestScale = new Unity.Mathematics.float2(1f, 1f) } };
            var clips = new[]
            {
                new SpritePartsSetBuilder.ClipInput
                {
                    Name = "c", ClipId = "c", Duration = 1f, SpeedMultiplier = 1f, Tracks = System.Array.Empty<SpritePartsSetBuilder.TrackInput>(),
                    Events = new[] { new SpritePartsSetBuilder.EventInput { Time = 0.5f, Id = 2 } },
                },
            };
            var blob = SpritePartsSetBuilder.Build(Allocator.Temp, slots, System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                clips, System.Array.Empty<SpritePartsSetBuilder.SkinInput>());
            try
            {
                Assert.AreEqual(-1, blob.Value.Clips[0].Events[0].AudioIndex);
            }
            finally
            {
                blob.Dispose();
            }
        }
    }
}

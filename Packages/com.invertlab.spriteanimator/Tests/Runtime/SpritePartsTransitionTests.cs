using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace InvertLab.Sprites.DOTS.Tests
{
    /// <summary>
    /// Transitions: mix table, eased and chained crossfades, queue and exit time, layer fades, additive and own-clock
    /// layers, masks, fade-out events, whole-character fade and blend spaces.
    /// </summary>
    public sealed class SpritePartsTransitionTests
    {
        // Clips: 0 A (x 0), 1 B (x 10), 2 C (x 20), 3 Walk (x 0..10 over 1 s), 4 Run (x 0..10 over 0.5 s), 5 Bob (x 3).
        // "body" rests at x 1; "arm" rests at x 0 and has no keys.
        static SpritePartsSetBuilder.KeyInput Key(float t, float x)
            => new SpritePartsSetBuilder.KeyInput { Time = t, Position = new float2(x, 0f), Scale = new float2(1f, 1f), AppearanceId = string.Empty };

        static SpritePartsSetBuilder.ClipInput Clip(string id, float duration, params SpritePartsSetBuilder.KeyInput[] keys)
            => new SpritePartsSetBuilder.ClipInput
            {
                Name = id, ClipId = id, Duration = duration, SpeedMultiplier = 1f, WrapMode = (byte)SpritePartsWrap.Loop,
                Tracks = new[] { new SpritePartsSetBuilder.TrackInput { SlotId = "body", Keys = keys } },
            };

        static BlobAssetReference<SpritePartsSetBlob> Build(SpritePartsSetBuilder.TransitionsInput transitions = null,
            bool events = false)
        {
            var slots = new[]
            {
                new SpritePartsSetBuilder.SlotInput { Name = "body", SlotId = "body", RestPosition = new float2(1f, 0f), RestScale = new float2(1f, 1f) },
                new SpritePartsSetBuilder.SlotInput { Name = "arm", SlotId = "arm", RestScale = new float2(1f, 1f), DrawRank = 1 },
            };
            var clips = new[]
            {
                Clip("A", 1f, Key(0f, 0f)),
                Clip("B", 1f, Key(0f, 10f)),
                Clip("C", 1f, Key(0f, 20f)),
                Clip("Walk", 1f, Key(0f, 0f), Key(1f, 10f)),
                Clip("Run", 0.5f, Key(0f, 0f), Key(0.5f, 10f)),
                Clip("Bob", 1f, Key(0f, 3f)),
            };
            if (events)
                clips[0].Events = new[] { new SpritePartsSetBuilder.EventInput { Time = 0.5f, Id = 9 } };
            return SpritePartsSetBuilder.Build(Allocator.Temp, slots, System.Array.Empty<SpritePartsSetBuilder.AppearanceInput>(),
                clips, System.Array.Empty<SpritePartsSetBuilder.SkinInput>(), transitions: transitions);
        }

        sealed class Rig : System.IDisposable
        {
            public World World;
            public EntityManager Em;
            public Entity Root;
            public NativeArray<Entity> Parts;
            public BlobAssetReference<SpritePartsSetBlob> Blob;
            double _time;

            public Rig(BlobAssetReference<SpritePartsSetBlob> blob)
            {
                Blob = blob;
                World = new World("parts-transitions");
                Em = World.EntityManager;
                var created = SpritePartsEntityFactory.Create(Em, blob, float3.zero);
                Root = created.Root;
                Parts = created.Parts;
            }

            public float BodyX => Em.GetComponentData<LocalTransform>(Parts[0]).Position.x;
            public float ArmX => Em.GetComponentData<LocalTransform>(Parts[1]).Position.x;
            public SpritePartsPlayer Player => Em.GetComponentData<SpritePartsPlayer>(Root);

            public void Tick(float dt)
            {
                _time += dt;
                World.SetTime(new Unity.Core.TimeData(_time, dt));
                World.GetOrCreateSystem<SpritePartsPlayerSystem>().Update(World.Unmanaged);
            }

            public void Dispose()
            {
                Parts.Dispose();
                World.Dispose();
                Blob.Dispose();
            }
        }

        static SpritePartsSetBuilder.TransitionsInput Mixes(float defaultMix, params SpritePartsSetBuilder.MixInput[] mixes)
            => new SpritePartsSetBuilder.TransitionsInput { DefaultMix = defaultMix, Mixes = mixes };

        [Test]
        public void Mix_Table_Finds_The_Pair_Then_Any_Then_The_Default()
        {
            var blob = Build(Mixes(0.1f,
                new SpritePartsSetBuilder.MixInput { FromClipId = "A", ToClipId = "B", Duration = 0.5f, Ease = (byte)SpriteEaseMode.EaseIn },
                new SpritePartsSetBuilder.MixInput { FromClipId = null, ToClipId = "C", Duration = 0.3f },
                new SpritePartsSetBuilder.MixInput { FromClipId = "Nope", ToClipId = "A", Duration = 9f }));
            try
            {
                ref var set = ref blob.Value;
                Assert.AreEqual(2, set.Mixes.Length, "A mix naming a missing clip is dropped.");
                Assert.IsTrue(SpritePartsTransitions.FindMix(ref set, 0, 1, out float d, out byte ease));
                Assert.AreEqual(0.5f, d);
                Assert.AreEqual((byte)SpriteEaseMode.EaseIn, ease);
                Assert.IsTrue(SpritePartsTransitions.FindMix(ref set, 1, 2, out d, out _));
                Assert.AreEqual(0.3f, d, "Any clip into C.");
                Assert.IsFalse(SpritePartsTransitions.FindMix(ref set, 1, 0, out d, out _));
                Assert.AreEqual(0.1f, d, "Default.");
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Play_Uses_The_Mix_Table_And_Zero_Is_Instant()
        {
            using var rig = new Rig(Build(Mixes(0f, new SpritePartsSetBuilder.MixInput { FromClipId = "A", ToClipId = "B", Duration = 0.5f })));
            SpriteParts.Play(rig.Em, rig.Root, 0, force: true, crossfadeSeconds: 0f);
            Assert.IsTrue(SpriteParts.Play(rig.Em, rig.Root, "B"));
            Assert.AreEqual(0.5f, rig.Player.BlendDuration, 1e-6f, "Play with no time: the table.");
            Assert.AreEqual(0, rig.Player.PreviousClipIndex);
            Assert.IsTrue(SpriteParts.Play(rig.Em, rig.Root, "C", crossfadeSeconds: 0f));
            Assert.AreEqual(-1, rig.Player.PreviousClipIndex, "0 = instant.");
            Assert.AreEqual(20f, rig.BodyX, 1e-4f);
        }

        [Test]
        public void Crossfade_Ease_Shapes_The_Weight()
        {
            Assert.AreEqual(0.5f, SpritePartsTransitions.Weight(0.5f, 1f, (byte)SpriteEaseMode.Linear), 1e-6f);
            Assert.Less(SpritePartsTransitions.Weight(0.5f, 1f, (byte)SpriteEaseMode.EaseIn), 0.5f);
            Assert.Greater(SpritePartsTransitions.Weight(0.5f, 1f, (byte)SpriteEaseMode.EaseOut), 0.5f);
            Assert.AreEqual(1f, SpritePartsTransitions.Weight(2f, 1f, (byte)SpriteEaseMode.EaseIn), 1e-6f);
            var player = new SpritePartsPlayer { PreviousClipIndex = 0, BlendDuration = 1f, BlendElapsed = 0.5f, BlendEase = (byte)SpriteEaseMode.EaseIn };
            Assert.Less(SpritePartsPoseWriter.IncomingWeight(player), 0.5f);
        }

        [Test]
        public void A_Crossfade_Started_Mid_Fade_Does_Not_Pop()
        {
            using var rig = new Rig(Build());
            SpriteParts.Play(rig.Em, rig.Root, 0, force: true, crossfadeSeconds: 0f);
            SpriteParts.Play(rig.Em, rig.Root, 1, crossfadeSeconds: 1f);
            rig.Tick(0.5f);
            Assert.AreEqual(5f, rig.BodyX, 1e-3f, "Halfway A -> B.");
            SpriteParts.Play(rig.Em, rig.Root, 2, crossfadeSeconds: 1f);
            Assert.AreEqual(5f, rig.BodyX, 1e-3f, "C starts from where A -> B was, not from B.");
            Assert.AreEqual(1, rig.Em.GetBuffer<SpritePartsMixEntry>(rig.Root).Length, "A is kept under B.");
            rig.Tick(0.5f);
            // B is fully in over A now; C is halfway in over B.
            Assert.AreEqual(15f, rig.BodyX, 1e-3f);
            rig.Tick(0.01f);
            Assert.AreEqual(0, rig.Em.GetBuffer<SpritePartsMixEntry>(rig.Root).Length, "Covered clips are dropped.");
            rig.Tick(0.6f);
            Assert.AreEqual(20f, rig.BodyX, 1e-3f);
            Assert.AreEqual(-1, rig.Player.PreviousClipIndex);
        }

        [Test]
        public void Queue_Starts_After_The_Clip_Plus_Delay_And_Play_Clears_It()
        {
            using var rig = new Rig(Build());
            SpriteParts.Play(rig.Em, rig.Root, 0, force: true, crossfadeSeconds: 0f);
            Assert.IsTrue(SpriteParts.Queue(rig.Em, rig.Root, "B", delay: 0.25f, crossfadeSeconds: 0f));
            Assert.IsTrue(SpriteParts.Queue(rig.Em, rig.Root, "C", crossfadeSeconds: 0f));
            rig.Tick(1f);
            Assert.AreEqual(0, rig.Player.ClipIndex, "One play of A is not enough: the delay.");
            rig.Tick(0.3f);
            Assert.AreEqual(1, rig.Player.ClipIndex, "B starts.");
            Assert.AreEqual(10f, rig.BodyX, 1e-4f);
            Assert.AreEqual(1, SpriteParts.QueuedCount(rig.Em, rig.Root));
            rig.Tick(0.5f);
            Assert.AreEqual(1, rig.Player.ClipIndex, "C waits for one play of B.");
            SpriteParts.Play(rig.Em, rig.Root, "A", crossfadeSeconds: 0f);
            Assert.AreEqual(0, SpriteParts.QueuedCount(rig.Em, rig.Root), "Play clears the queue.");
        }

        [Test]
        public void Exit_Time_Starts_Partway_Through()
        {
            using var rig = new Rig(Build());
            SpriteParts.Play(rig.Em, rig.Root, 0, force: true, crossfadeSeconds: 0f);
            Assert.IsTrue(SpriteParts.PlayAtExit(rig.Em, rig.Root, "B", 1.5f, crossfadeSeconds: 0f));
            rig.Tick(0.9f);
            rig.Tick(0.5f);
            Assert.AreEqual(0, rig.Player.ClipIndex, "1.4 plays: not yet.");
            rig.Tick(0.2f);
            Assert.AreEqual(1, rig.Player.ClipIndex, "Past 1.5 plays: B.");
        }

        [Test]
        public void Layers_Fade_And_Are_Removed_At_Zero()
        {
            using var rig = new Rig(Build());
            SpriteParts.Play(rig.Em, rig.Root, 0, force: true, crossfadeSeconds: 0f);
            Assert.IsTrue(SpriteParts.FadeLayer(rig.Em, rig.Root, "B", 1f, 1f));
            rig.Tick(0.5f);
            var layers = rig.Em.GetBuffer<SpritePartsAnimLayer>(rig.Root);
            Assert.AreEqual(0.5f, layers[0].Weight, 1e-4f);
            Assert.AreEqual(5f, rig.BodyX, 1e-3f);
            SpriteParts.FadeLayer(rig.Em, rig.Root, "B", 0f, 0.25f);
            rig.Tick(0.3f);
            Assert.AreEqual(0, rig.Em.GetBuffer<SpritePartsAnimLayer>(rig.Root).Length);
            Assert.AreEqual(0f, rig.BodyX, 1e-4f);
        }

        [Test]
        public void Additive_Layers_Add_Their_Change_And_Own_Clocks_Run_Apart()
        {
            using var rig = new Rig(Build());
            SpriteParts.Play(rig.Em, rig.Root, 1, force: true, crossfadeSeconds: 0f);
            Assert.IsTrue(SpriteParts.SetLayer(rig.Em, rig.Root, "Bob", 1f, additive: true));
            rig.Tick(0.1f);
            Assert.AreEqual(12f, rig.BodyX, 1e-4f, "B (10) + Bob's change from the rest pose (3 - 1).");

            SpriteParts.ClearLayers(rig.Em, rig.Root);
            SpriteParts.Play(rig.Em, rig.Root, 3, force: true, crossfadeSeconds: 0f);
            rig.Tick(0.5f);
            Assert.IsTrue(SpriteParts.SetLayer(rig.Em, rig.Root, "Walk", 1f, ownClock: true));
            rig.Tick(0.2f);
            Assert.AreEqual(2f, rig.BodyX, 1e-3f, "The layer started its own clock at 0, not at the base clip's 0.7.");
        }

        [Test]
        public void Named_Masks_Limit_A_Layer_To_Their_Parts()
        {
            var transitions = new SpritePartsSetBuilder.TransitionsInput
            {
                Masks = new[] { new SpritePartsSetBuilder.MaskInput { Name = "Arms", SlotIds = new[] { "arm" } } },
            };
            using var rig = new Rig(Build(transitions));
            Assert.AreEqual(2u, SpriteParts.MaskBits(rig.Em, rig.Root, "Arms"));
            SpriteParts.Play(rig.Em, rig.Root, 0, force: true, crossfadeSeconds: 0f);
            Assert.IsTrue(SpriteParts.SetLayer(rig.Em, rig.Root, "B", 1f, maskName: "Arms"));
            Assert.IsFalse(SpriteParts.SetLayer(rig.Em, rig.Root, "B", 1f, maskName: "Legs"), "Unknown masks are refused.");
            rig.Tick(0.1f);
            Assert.AreEqual(0f, rig.BodyX, 1e-4f, "The body is not in the mask.");
        }

        [Test]
        public void The_Fading_Out_Clip_Fires_Its_Events_When_Asked()
        {
            foreach (bool fadeOutEvents in new[] { false, true })
            {
                using var rig = new Rig(Build(new SpritePartsSetBuilder.TransitionsInput { FadeOutEvents = fadeOutEvents }, events: true));
                SpriteParts.Play(rig.Em, rig.Root, 0, force: true, crossfadeSeconds: 0f);
                rig.Tick(0.4f);
                SpriteParts.Play(rig.Em, rig.Root, 1, crossfadeSeconds: 1f);
                rig.Tick(0.2f);
                bool fired = false;
                if (rig.Em.HasBuffer<SpriteAnimEventBuffer>(rig.Root))
                {
                    var buffer = rig.Em.GetBuffer<SpriteAnimEventBuffer>(rig.Root);
                    for (int i = 0; i < buffer.Length; i++)
                        fired |= buffer[i].Id == 9;
                }
                Assert.AreEqual(fadeOutEvents, fired);
            }
        }

        [Test]
        public void Character_Fade_Multiplies_Every_Part()
        {
            using var rig = new Rig(Build());
            SpriteParts.Play(rig.Em, rig.Root, 0, force: true, crossfadeSeconds: 0f);
            SpriteParts.FadeCharacter(rig.Em, rig.Root, 0f, 1f);
            Assert.AreEqual(1f, SpriteParts.GetCharacterTint(rig.Em, rig.Root).w, 1e-6f);
            rig.Tick(0.5f);
            Assert.AreEqual(0.5f, SpriteParts.GetCharacterTint(rig.Em, rig.Root).w, 1e-4f);
            rig.World.GetOrCreateSystem<SpritePartsRenderDepthSystem>().Update(rig.World.Unmanaged);
            Assert.AreEqual(0.5f, rig.Em.GetComponentData<SpritePartKeyedTint>(rig.Parts[1]).Value.w, 1e-4f);
            rig.Tick(0.6f);
            Assert.AreEqual(0f, SpriteParts.GetCharacterTint(rig.Em, rig.Root).w, 1e-6f);
        }

        [Test]
        public void Profile_Transitions_Reach_The_Blob()
        {
            var profile = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            profile.PartsDefaultMix = 0.25f;
            profile.PartsFadeOutEvents = true;
            profile.PartsMixes.Add(new SpritePartsMixDef { FromClipId = "idle", ToClipId = "walk", Duration = 0.4f, Ease = (byte)SpriteEaseMode.EaseOut });
            profile.PartsMasks.Add(new SpritePartsMaskDef { Name = "Body", SlotIds = { "body" } });
            profile.PartsBlendSpaces.Add(new SpritePartsBlendSpaceDef
            {
                Name = "Loco",
                Points = { new SpritePartsBlendPointDef { ClipId = "walk", Value = 1f }, new SpritePartsBlendPointDef { ClipId = "idle", Value = 0f } },
            });
            Assert.IsTrue(SpritePartsClipConversion.TryBuildPoseEvaluationBlob(profile, Allocator.Temp, out var blob, out var error), error);
            try
            {
                ref var set = ref blob.Value;
                int idle = SpritePartsAuthoringOps.FindClipIndex(profile, "idle");
                int walk = SpritePartsAuthoringOps.FindClipIndex(profile, "walk");
                Assert.AreEqual(0.25f, set.DefaultMix);
                Assert.AreEqual(1, set.FadeOutEvents);
                Assert.IsTrue(SpritePartsTransitions.FindMix(ref set, idle, walk, out float seconds, out byte ease));
                Assert.AreEqual(0.4f, seconds);
                Assert.AreEqual((byte)SpriteEaseMode.EaseOut, ease);
                Assert.AreEqual(1, math.countbits(set.Masks[0].Bits), "One part in the mask.");
                Assert.AreEqual(idle, set.BlendSpaces[0].Points[0].ClipIndex);
                Assert.AreEqual(walk, set.BlendSpaces[0].Points[1].ClipIndex);
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Editor_Mix_Preview_Shows_The_Fade()
        {
            var blob = Build();
            var local = new NativeArray<SpritePartsSampler.Pose>(2, Allocator.Temp);
            var ltr = new NativeArray<float4x4>(2, Allocator.Temp);
            try
            {
                var mix = new SpritePartsOnion.MixPreviewState { Active = true, From = 0, To = 1, Duration = 1f, Time = 0.5f };
                SpritePartsPoseWriter.EvaluateEditor(ref blob.Value, mix.Player(), local, ltr, default);
                Assert.AreEqual(5f, local[0].Position.x, 1e-4f, "Halfway through the fade.");
                mix.Time = -0.2f;
                SpritePartsPoseWriter.EvaluateEditor(ref blob.Value, mix.Player(), local, ltr, default);
                Assert.AreEqual(0f, local[0].Position.x, 1e-4f, "Before the switch: the first clip alone.");
            }
            finally
            {
                local.Dispose();
                ltr.Dispose();
                blob.Dispose();
            }
        }

        [Test]
        public void Blend_Space_Keeps_The_Clips_In_Step()
        {
            var transitions = new SpritePartsSetBuilder.TransitionsInput
            {
                BlendSpaces = new[]
                {
                    new SpritePartsSetBuilder.BlendSpaceInput { Name = "Loco", ClipIds = new[] { "Run", "Walk" }, Values = new[] { 1f, 0f } },
                },
            };
            using var rig = new Rig(Build(transitions));
            ref var set = ref rig.Blob.Value;
            Assert.AreEqual(3, set.BlendSpaces[0].Points[0].ClipIndex, "Points are sorted by value.");
            Assert.AreEqual(1.5f, SpritePartsTransitions.PhaseRate(ref set, 0, 0.5f), 1e-5f, "Halfway: 1 and 2 cycles a second.");

            Assert.IsTrue(SpriteParts.PlayBlend(rig.Em, rig.Root, "Loco", 0.5f, crossfadeSeconds: 0f));
            rig.Tick(0.2f);
            // Phase 0.3: Walk at 0.3 s and Run at 0.15 s both put the body at 3.
            Assert.AreEqual(3f, rig.BodyX, 1e-3f);
            Assert.AreEqual(4, rig.Player.ClipIndex, "The heaviest point (Run at w 0.5) drives events and keys.");
            SpriteParts.SetBlendValue(rig.Em, rig.Root, 0f);
            rig.Tick(0.2f);
            Assert.AreEqual(5f, rig.BodyX, 1e-3f, "Walk only: phase 0.5.");
            Assert.AreEqual(3, rig.Player.ClipIndex);

            // Leaving the blend space crossfades out of the blend, not out of one clip.
            SpriteParts.Play(rig.Em, rig.Root, "B", crossfadeSeconds: 1f);
            Assert.AreEqual(5f, rig.BodyX, 1e-3f);
            Assert.AreEqual(-1, rig.Em.GetComponentData<SpritePartsBlendState>(rig.Root).SpaceIndex);
        }
    }
}

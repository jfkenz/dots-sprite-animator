using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>One jiggle joint's simulated tip, in world space (so moving the character makes it swing).</summary>
    [InternalBufferCapacity(0)]
    public struct SpritePartJiggleState : IBufferElementData
    {
        public float2 Tip;
        public float2 Velocity;
        public byte Ready;
    }

    /// <summary>What the pose writer needs beyond the clip: jiggle state and the frame time.</summary>
    public struct SpritePartsEvalExtras
    {
        /// <summary>One per <see cref="SpritePartsSetBlob.Jiggles"/>; not created = no jiggle.</summary>
        public NativeArray<SpritePartJiggleState> Jiggle;
        public float DeltaTime;
        /// <summary>The keyed pose only: no parameters, IK or jiggle (the editor writes keys from this).</summary>
        public bool KeyedPoseOnly;
        /// <summary>One value per <see cref="SpritePartsSetBlob.Params"/>; not created (or short) = defaults.</summary>
        public NativeArray<float> ParamValues;
        /// <summary>Cut clipped parts to their masks (the game). The editor clips only when it draws art.</summary>
        public bool Clip;
        /// <summary>Hold each key's pose until the next key (editor blocking preview).</summary>
        public bool Stepped;
        /// <summary>Clips a chained crossfade still shows under the previous clip (<see cref="SpritePartsMixEntry"/>).</summary>
        public NativeArray<SpritePartsMixEntry> MixChain;
        /// <summary>The blend space playing (and one fading out), when <see cref="HasBlend"/>.</summary>
        public SpritePartsBlendState Blend;
        public bool HasBlend;
    }

    /// <summary>
    /// Jiggle (spring physics, like Spine's physics constraints in rotate mode): each joint's tip is a
    /// damped spring chasing where the animation puts it, with gravity; the joint then turns to point at it.
    /// Runs after IK, parents first, sub-stepped at 120 Hz.
    /// </summary>
    public static class SpritePartsJiggle
    {
        const float Step = 1f / 120f;
        const float MaxFrame = 0.1f;

        /// <param name="mix">Per spring (keyed values); not created = each spring's own Mix.</param>
        public static void Apply(ref SpritePartsSetBlob set, NativeArray<SpritePartsSampler.Pose> local,
            NativeArray<float4x4> localToRoot, NativeArray<SpritePartJiggleState> states, float dt, float4x4 worldFromCharacter,
            NativeArray<float> mix = default)
        {
            if (!states.IsCreated || set.Jiggles.Length == 0)
                return;
            float4x4 characterFromWorld = math.inverse(worldFromCharacter);
            dt = math.clamp(dt, 0f, MaxFrame);
            for (int c = 0; c < set.Jiggles.Length && c < states.Length; c++)
            {
                ref var j = ref set.Jiggles[c];
                float jMix = mix.IsCreated && c < mix.Length ? mix[c] : j.Mix;
                if (j.Slot < 0 || j.Slot >= local.Length || jMix <= 0f)
                    continue;
                float4x4 m = localToRoot[j.Slot];
                float2 jointC = m.c3.xy;
                float2 tipC = math.mul(m, new float4(j.TipLocal, 0f, 1f)).xy;
                float2 joint = Point(worldFromCharacter, jointC);
                float2 tip = Point(worldFromCharacter, tipC);
                float length = math.distance(joint, tip);
                if (length < 1e-5f)
                    continue;

                var st = states[c];
                bool lost = !math.all(math.isfinite(st.Tip)) || math.distance(st.Tip, tip) > length * 4f;
                if (st.Ready == 0 || lost)
                {
                    st.Tip = tip;
                    st.Velocity = float2.zero;
                    st.Ready = 1;
                }
                else
                {
                    float2 gravity = new float2(0f, -j.Gravity);
                    for (float left = dt; left > 1e-6f; left -= Step)
                    {
                        float h = math.min(left, Step);
                        float2 accel = j.Spring * (tip - st.Tip) - j.Damping * st.Velocity + gravity;
                        st.Velocity += accel * h;
                        st.Tip += st.Velocity * h;
                        // The bone keeps its length: the tip stays on a circle around the joint.
                        float2 d = st.Tip - joint;
                        float dl = math.length(d);
                        if (dl > 1e-6f)
                        {
                            float2 n = d / dl;
                            st.Tip = joint + n * length;
                            st.Velocity -= n * math.dot(st.Velocity, n);
                        }
                    }
                }
                states[c] = st;

                float2 simC = Point(characterFromWorld, st.Tip);
                float delta = SpritePartsIk.SignedAngle(tipC - jointC, simC - jointC) * jMix;
                if (math.abs(delta) < 1e-5f)
                    continue;
                SpritePartsIk.Turn(ref set, j.Slot, delta, local, localToRoot);
                SpritePartsHierarchy.ComposeLocalToRoot(ref set, local, localToRoot);
            }
        }

        static float2 Point(float4x4 m, float2 p) => math.mul(m, new float4(p, 0f, 1f)).xy;
    }
}

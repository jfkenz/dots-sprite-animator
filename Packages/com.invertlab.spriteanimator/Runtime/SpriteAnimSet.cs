using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Wrap modes for clips.</summary>
    public static class SpriteAnimWrap
    {
        public const byte Loop = 0;
        public const byte Once = 1;
        public const byte PingPong = 2;
        public const byte ReverseLoop = 3;
    }

    /// <summary>One playable animation inside a character's set.</summary>
    public struct SpriteAnimDef
    {
        public ulong NameHash;     // FNV1a64 of the clip name ("Idle", "Run", ...)
        public int   FirstFrame;   // into SpriteAnimSetBlob.Frames (global frame id)
        public int   FrameCount;
        public float FrameRate;    // frames per second
        public byte  WrapMode;     // SpriteAnimWrap.*
        public BlobArray<float> DurationScales; // per clip-frame multiplier
        public BlobArray<byte>  EventIds;       // per clip-frame; 0 = no event
        public BlobArray<float> EventNormalizedTimes; // 0=start, 1=end of frame
        public BlobArray<float2> FrameScales;   // per clip-frame local scale multiplier
        public BlobArray<float> FrameRotations; // per clip-frame local Z rotation in degrees
        public BlobArray<byte> FrameTweenModes; // SpriteEaseMode from frame f -> f+1
        public ulong FacingGroupHash; // 0 when not grouped
        public byte FacingDirection;  // SpriteFacingDirection
        public BlobArray<SpriteSocketFramePoint> FrameSockets;
    }

    public struct SpriteSocketFramePoint
    {
        public int FrameIndex;
        public float2 LocalPosition;
        public float LocalAngle;
        public float2 LocalScale;
        public FixedString64Bytes Name;
    }

    /// <summary>
    /// Every animation of one character in a single persistent blob —
    /// one allocation per character type, switched by index/hash.
    /// </summary>
    public struct SpriteAnimSetBlob
    {
        public BlobArray<SpriteAnimDef> Clips;
        // Play-ordered slots: Frames[clip.FirstFrame + t].x = frame slot index
        // (sheet cell / render slot shown at time t of that clip).
        public BlobArray<float4> Frames;
    }

    /// <summary>The character's animation library (e.g. the soldier's 4 states).</summary>
    public struct SpriteAnimSetRef : IComponentData
    {
        public BlobAssetReference<SpriteAnimSetBlob> Set;
    }

    /// <summary>Tag: this entity's animation set is loaded and ready.</summary>
    public struct SpriteAnimSetLoaded : IComponentData { }

    /// <summary>Playback state: which clip is playing and the frame clock.</summary>
    public struct SpriteAnimPlayer : IComponentData
    {
        public int   ClipIndex;
        public float Time;     // in frames (scaled by DurationScales while advancing)
        public float Speed;
        public byte  Playing;
        public int   LastEventStep;
    }

    /// <summary>
    /// Builds a SpriteAnimSetBlob from clip definitions. New fields are optional
    /// so existing call sites compile unchanged (loop, uniform rate, no events).
    /// </summary>
    public static class SpriteAnimSetBuilder
    {
        public struct ClipInput
        {
            public string Name;
            public bool   Loop;                 // legacy: used when WrapMode omitted
            public float  FrameRate;
            public int[]  GlobalFrameIndices;   // sheet cells, in play order

            // ---- optional extensions ----
            // NOTE: C#9 — no field initializers allowed in structs. Unset means
            // 0 (= Loop); legacy Loop=false callers rely on EffectiveWrapMode.
            public byte   WrapMode;             // 0 loop / 1 once / 2 pingpong / 3 reverse / 255 auto
            public float[] FrameDurationScales; // per-frame duration multiplier (default 1)
            public byte[]  EventIds;            // per-frame event id (default 0)
            public float[] EventNormalizedTimes;// event position inside frame (default 0)
            public float2[] FrameOffsets;       // per-frame render offset in world units
            public float2[] FrameScales;        // per-frame local scale multiplier
            public float[] FrameRotations;      // per-frame local z rotation in degrees
            public byte[] FrameTweenModes;      // per-frame tween mode (SpriteEaseMode)
            public string FacingGroup;          // optional 4/8-way group label
            public SpriteFacingDirection FacingDirection;
            public FrameSocketInput[] FrameSockets;

            public struct FrameSocketInput
            {
                public int FrameIndex;
                public float2 LocalPosition;
                public float LocalAngle;
                public float2 LocalScale;
                public string Name;
            }

            /// <summary>
            /// 255 is unreachable by valid modes; treat it as "derive from Loop".
            /// Since C#9 forbids initializing the field, callers who want a
            /// specific mode just set it; everyone else gets Loop/Once via Loop.
            /// </summary>
            public byte EffectiveWrapMode => Loop
                ? (WrapMode == SpriteAnimWrap.ReverseLoop ? SpriteAnimWrap.ReverseLoop : SpriteAnimWrap.Loop)
                : (WrapMode == SpriteAnimWrap.Once || WrapMode == SpriteAnimWrap.PingPong
                    ? WrapMode
                    : SpriteAnimWrap.Once);
        }

        /// <summary>Build the blob + initial player state.</summary>
        public static (SpriteAnimSetRef, SpriteAnimPlayer) Build(
            Allocator allocator, ClipInput[] clips)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<SpriteAnimSetBlob>();

            var defs = builder.Allocate(ref root.Clips, clips.Length);

            int totalFrames = 0;
            for (int i = 0; i < clips.Length; i++)
                totalFrames += math.max(1, clips[i].GlobalFrameIndices?.Length ?? 1);

            var frames = builder.Allocate(ref root.Frames, totalFrames);

            int cursor = 0;
            for (short ci = 0; ci < clips.Length; ci++)
            {
                var input = clips[ci];
                var idx = input.GlobalFrameIndices;
                int n = idx != null && idx.Length > 0 ? idx.Length : 1;

                ref var def = ref defs[ci];
                def.NameHash = Fnv(string.IsNullOrEmpty(input.Name) ? ("clip" + ci) : input.Name);
                def.FirstFrame = cursor;
                def.FrameCount = n;
                def.FrameRate = math.max(0.01f, input.FrameRate);
                def.WrapMode = input.EffectiveWrapMode;

                var ds = builder.Allocate(ref def.DurationScales, n);
                var ev = builder.Allocate(ref def.EventIds, n);
                var et = builder.Allocate(ref def.EventNormalizedTimes, n);
                var fs = builder.Allocate(ref def.FrameScales, n);
                var fr = builder.Allocate(ref def.FrameRotations, n);
                var ft = builder.Allocate(ref def.FrameTweenModes, n);
                for (int f = 0; f < n; f++)
                {
                    float2 offset = input.FrameOffsets != null && f < input.FrameOffsets.Length
                        ? input.FrameOffsets[f]
                        : float2.zero;
                    frames[cursor + f] = new float4(
                        idx != null && f < idx.Length ? idx[f] : 0, offset.x, offset.y, 0);
                    ds[f] = input.FrameDurationScales != null && f < input.FrameDurationScales.Length
                        ? math.max(0.01f, input.FrameDurationScales[f])
                        : 1f;
                    ev[f] = input.EventIds != null && f < input.EventIds.Length
                        ? input.EventIds[f]
                        : (byte)0;
                    et[f] = input.EventNormalizedTimes != null && f < input.EventNormalizedTimes.Length
                        ? math.saturate(input.EventNormalizedTimes[f])
                        : 0f;
                    fs[f] = input.FrameScales != null && f < input.FrameScales.Length
                        ? input.FrameScales[f]
                        : new float2(1f, 1f);
                    fr[f] = input.FrameRotations != null && f < input.FrameRotations.Length
                        ? input.FrameRotations[f]
                        : 0f;
                    ft[f] = input.FrameTweenModes != null && f < input.FrameTweenModes.Length
                        ? ClampEaseMode(input.FrameTweenModes[f])
                        : (byte)SpriteEaseMode.Linear;
                }

                def.FacingGroupHash = string.IsNullOrWhiteSpace(input.FacingGroup)
                    ? 0UL
                    : Fnv(input.FacingGroup.Trim());
                def.FacingDirection = def.FacingGroupHash == 0
                    ? (byte)SpriteFacingDirection.None
                    : (byte)input.FacingDirection;

                int socketCount = input.FrameSockets?.Length ?? 0;
                var sockets = builder.Allocate(ref def.FrameSockets, socketCount);
                for (int s = 0; s < socketCount; s++)
                {
                    var socket = input.FrameSockets[s];
                    sockets[s] = new SpriteSocketFramePoint
                    {
                        FrameIndex = math.clamp(socket.FrameIndex, 0, math.max(0, n - 1)),
                        LocalPosition = socket.LocalPosition,
                        LocalAngle = socket.LocalAngle,
                        LocalScale = math.all(socket.LocalScale == float2.zero)
                            ? new float2(1f, 1f)
                            : socket.LocalScale,
                        Name = new FixedString64Bytes(string.IsNullOrWhiteSpace(socket.Name)
                            ? $"Socket {s + 1}"
                            : socket.Name.Trim()),
                    };
                }
                cursor += n;
            }

            var result = builder.CreateBlobAssetReference<SpriteAnimSetBlob>(allocator);
            builder.Dispose();

            var player = default(SpriteAnimPlayer);
            player.ClipIndex = 0;
            player.Time = 0f;
            player.Speed = 1f;
            player.Playing = 1;
            player.LastEventStep = int.MinValue;
            return (new SpriteAnimSetRef { Set = result }, player);
        }

        public static ulong Fnv(string s)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < s.Length; i++)
            {
                hash ^= s[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }

        static byte ClampEaseMode(byte mode)
        {
            return mode > (byte)SpriteEaseMode.Step
                ? (byte)SpriteEaseMode.Linear
                : mode;
        }
    }

    /// <summary>Gameplay-facing helpers.</summary>
    public static class SpriteAnims
    {
        public static ulong Fnv(string s) => SpriteAnimSetBuilder.Fnv(s);

        /// <summary>Switch an entity to the named clip (restarts at t=0).</summary>
        public static bool Play(EntityManager em, Entity e, string clipName)
        {
            if (!em.HasComponent<SpriteAnimSetRef>(e))
                return false;
            if (string.IsNullOrWhiteSpace(clipName))
                return false;
            var hash = Fnv(clipName);
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            for (int i = 0; i < set.Clips.Length; i++)
            {
                if (set.Clips[i].NameHash == hash)
                    return Play(em, e, i);
            }
            return false;
        }

        public static bool PlayFacing(EntityManager em, Entity e, string facingGroup,
                                      SpriteFacingDirection facingDirection)
        {
            if (!em.HasComponent<SpriteAnimSetRef>(e) || string.IsNullOrWhiteSpace(facingGroup))
                return false;

            ulong groupHash = Fnv(facingGroup.Trim());
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            int fallbackIndex = -1;
            for (int i = 0; i < set.Clips.Length; i++)
            {
                ref var clip = ref set.Clips[i];
                if (clip.FacingGroupHash != groupHash)
                    continue;
                if (fallbackIndex < 0)
                    fallbackIndex = i;
                if (clip.FacingDirection == (byte)facingDirection)
                {
                    return Play(em, e, i);
                }
            }
            return fallbackIndex >= 0 && Play(em, e, fallbackIndex);
        }

        /// <summary>Switch an entity to clip index (restarts at t=0).</summary>
        public static bool Play(EntityManager em, Entity e, int clipIndex)
        {
            if (!em.HasComponent<SpriteAnimSetRef>(e) || !em.HasComponent<SpriteAnimPlayer>(e))
                return false;
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            if (clipIndex < 0 || clipIndex >= set.Clips.Length)
                return false;

            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            player.ClipIndex = clipIndex;
            player.Time = 0f;
            player.Playing = 1;
            player.LastEventStep = int.MinValue;
            em.SetComponentData(e, player);
            if (em.HasComponent<SpriteAnimCompleted>(e))
                em.RemoveComponent<SpriteAnimCompleted>(e);

            ref var clip = ref set.Clips[clipIndex];
            int firstFrame = clip.WrapMode == SpriteAnimWrap.ReverseLoop
                ? math.max(0, clip.FrameCount - 1)
                : 0;
            if (em.HasComponent<SpriteAnimFrame>(e) && clip.FrameCount > 0)
            {
                float4 first = set.Frames[clip.FirstFrame + firstFrame];
                em.SetComponentData(e, new SpriteAnimFrame
                {
                    Slot = (int)first.x,
                    Offset = first.yz,
                    Scale = clip.FrameScales.Length > firstFrame ? clip.FrameScales[firstFrame] : new float2(1f, 1f),
                    Rotation = clip.FrameRotations.Length > firstFrame ? clip.FrameRotations[firstFrame] : 0f,
                });
            }
            if (em.HasBuffer<SpriteSocketBuffer>(e))
            {
                var sockets = em.GetBuffer<SpriteSocketBuffer>(e);
                sockets.Clear();
                for (int i = 0; i < clip.FrameSockets.Length; i++)
                {
                    var socket = clip.FrameSockets[i];
                    if (socket.FrameIndex != firstFrame)
                        continue;
                    sockets.Add(new SpriteSocketBuffer
                    {
                        Name = socket.Name,
                        LocalPosition = socket.LocalPosition,
                        LocalAngle = socket.LocalAngle,
                        LocalScale = socket.LocalScale,
                    });
                }
            }

            SpriteAnimEvents.Ensure(em, e);
            var events = em.GetBuffer<SpriteAnimEventBuffer>(e);
            events.Clear();
            em.SetComponentEnabled<SpriteAnimEventsPending>(e, false);
            if (firstFrame < clip.EventIds.Length && clip.EventIds[firstFrame] != 0 &&
                (firstFrame >= clip.EventNormalizedTimes.Length ||
                 clip.EventNormalizedTimes[firstFrame] <= 0f))
            {
                events.Add(new SpriteAnimEventBuffer
                {
                    Id = clip.EventIds[firstFrame],
                    ClipIndex = clipIndex,
                    FrameIndex = firstFrame,
                });
                em.SetComponentEnabled<SpriteAnimEventsPending>(e, true);
                player = em.GetComponentData<SpriteAnimPlayer>(e);
                player.LastEventStep = 0;
                em.SetComponentData(e, player);
            }
            return true;
        }
    }
}

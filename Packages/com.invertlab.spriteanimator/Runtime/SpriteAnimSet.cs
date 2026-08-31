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
        public BlobArray<byte>  EventIds;       // first marker per frame; 0 = none (legacy / GPU)
        public BlobArray<float> EventNormalizedTimes; // first marker time per frame
        public BlobArray<SpriteAnimEventKey> EventKeys; // every authored marker on this clip
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
        public FixedString64Bytes SocketId;
        public ulong SocketIdHash;
    }

    public struct SpriteSocketMotionPoint
    {
        public float NormalizedTime;
        public float2 LocalPosition;
        public float LocalAngle;
        public float2 LocalScale;
        public byte EaseMode;
        public byte PathMode;
        public byte UseCustomEase;
        public float4 CustomEaseSamplesA;
        public float4 CustomEaseSamplesB;
        public byte AllowOvershoot;
        public float2 InTangent;
        public float2 OutTangent;
        public float ArcBulge;
        public byte ArcClockwise;
        public byte RotationMode;
        public int RotationTurns;
        public float FacingAngleOffset;
    }

    public struct SpriteSocketTriggerPoint
    {
        public float NormalizedTime;
        public byte EventId;
    }

    /// <summary>Profile-level motion sampled independently from character clips.</summary>
    public struct SpriteSocketMotionBlob
    {
        public FixedString64Bytes Name;
        public FixedString64Bytes SocketId;
        public ulong SocketIdHash;
        public float Duration;
        public float Speed;
        public byte Loop;
        public byte AnchorSpace;
        public BlobArray<SpriteSocketMotionPoint> Keys;
        public BlobArray<SpriteSocketTriggerPoint> Triggers;
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
        public BlobArray<SpriteSocketMotionBlob> SocketMotions;
        public BlobArray<SpriteSocketInventoryBlob> SocketInventories;
    }

    public struct SpriteSocketInventoryBlob
    {
        public FixedString32Bytes Name;
        public uint GroupHash;
        public BlobArray<ulong> SocketIdHashes;
        public BlobArray<byte> Kinds;
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
        public int   OnceEventClip;
        public ulong EventFiredMask;
        public FixedList128Bytes<ushort> OnceFiredKeys;
    }

    /// <summary>One clip event after bake. Multiple keys may share a frame.</summary>
    public struct SpriteAnimEventKey
    {
        public int FrameIndex;
        public float NormalizedTime;
        public byte EventId;
        public byte FireMode;
        public int IntPayload;
        public float FloatPayload;
        public ulong TextHash;
        public FixedList512Bytes<SpriteAnimEventPayload> Payloads;
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
            public EventKeyInput[] EventKeys;   // optional; when set, replaces per-frame arrays at bake
            public float2[] FrameOffsets;       // per-frame render offset in world units
            public float2[] FrameScales;        // per-frame local scale multiplier
            public float[] FrameRotations;      // per-frame local z rotation in degrees
            public byte[] FrameTweenModes;      // per-frame tween mode (SpriteEaseMode)
            public string FacingGroup;          // optional 4/8-way group label
            public SpriteFacingDirection FacingDirection;
            public FrameSocketInput[] FrameSockets;

            public struct EventKeyInput
            {
                public int FrameIndex;
                public float NormalizedTime;
                public byte EventId;
                public byte FireMode;
                public int IntPayload;
                public float FloatPayload;
                public string TextPayload;
                public EventPayloadInput[] Payloads;
            }

            public struct EventPayloadInput
            {
                public string Name;
                public byte Kind;
                public int IntValue;
                public int IntY;
                public int IntZ;
                public int IntW;
                public float FloatValue;
                public float FloatY;
                public float FloatZ;
                public float FloatW;
                public string TextValue;
            }

            public struct FrameSocketInput
            {
                public int FrameIndex;
                public float2 LocalPosition;
                public float LocalAngle;
                public float2 LocalScale;
                public string Name;
                public string SocketId;
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

        public struct SocketMotionInput
        {
            public string Name;
            public string SocketId;
            public float Duration;
            public float Speed;
            public bool Loop;
            public byte AnchorSpace;
            public SocketMotionPointInput[] Keys;
            public SocketTriggerInput[] Triggers;

            public struct SocketMotionPointInput
            {
                public float NormalizedTime;
                public float2 LocalPosition;
                public float LocalAngle;
                public float2 LocalScale;
                public byte EaseMode;
                public byte PathMode;
                public byte UseCustomEase;
                public float4 CustomEaseSamplesA;
                public float4 CustomEaseSamplesB;
                public byte AllowOvershoot;
                public float2 InTangent;
                public float2 OutTangent;
                public float ArcBulge;
                public byte ArcClockwise;
                public byte RotationMode;
                public int RotationTurns;
                public float FacingAngleOffset;
            }

            public struct SocketTriggerInput
            {
                public float NormalizedTime;
                public byte EventId;
            }
        }

        public struct SocketInventoryInput
        {
            public string Name;
            public string[] SocketIds;
            public string[] SocketNames;
            public byte[] Kinds;
        }

        /// <summary>Build the blob + initial player state.</summary>
        public static (SpriteAnimSetRef, SpriteAnimPlayer) Build(
            Allocator allocator, ClipInput[] clips, SocketMotionInput[] socketMotions = null,
            SocketInventoryInput[] socketInventories = null)
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

                var eventKeys = input.EventKeys;
                if (eventKeys == null || eventKeys.Length == 0)
                    eventKeys = EventKeysFromLegacy(input, n);
                int keyCount = math.min(eventKeys.Length, 64);
                var bakedKeys = builder.Allocate(ref def.EventKeys, keyCount);
                for (int k = 0; k < keyCount; k++)
                {
                    var key = eventKeys[k];
                    bakedKeys[k] = BakeEventKey(key, n);
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
                        SocketId = new FixedString64Bytes(
                            SpriteSocketIdUtility.Canonical(socket.SocketId, socket.Name)),
                        SocketIdHash = SpriteSockets.Hash(
                            SpriteSocketIdUtility.Canonical(socket.SocketId, socket.Name)),
                    };
                }
                cursor += n;
            }

            int motionCount = socketMotions?.Length ?? 0;
            var motions = builder.Allocate(ref root.SocketMotions, motionCount);
            for (int i = 0; i < motionCount; i++)
            {
                var input = socketMotions[i];
                ref var motion = ref motions[i];
                motion.Name = new FixedString64Bytes(string.IsNullOrWhiteSpace(input.Name)
                    ? $"Socket {i + 1}"
                    : input.Name.Trim());
                string socketId = SpriteSocketIdUtility.Canonical(input.SocketId, input.Name);
                motion.SocketId = new FixedString64Bytes(socketId);
                motion.SocketIdHash = SpriteSockets.Hash(socketId);
                motion.Duration = math.max(0.01f, input.Duration);
                motion.Speed = math.max(0.01f, input.Speed);
                motion.Loop = input.Loop ? (byte)1 : (byte)0;
                motion.AnchorSpace = input.AnchorSpace <= (byte)SpriteSocketAnchorSpace.World
                    ? input.AnchorSpace
                    : (byte)SpriteSocketAnchorSpace.CharacterPivot;
                int keyCount = input.Keys?.Length ?? 0;
                var keys = builder.Allocate(ref motion.Keys, keyCount);
                for (int k = 0; k < keyCount; k++)
                {
                    var key = input.Keys[k];
                    keys[k] = new SpriteSocketMotionPoint
                    {
                        NormalizedTime = math.saturate(key.NormalizedTime),
                        LocalPosition = key.LocalPosition,
                        LocalAngle = key.LocalAngle,
                        LocalScale = math.all(key.LocalScale == float2.zero)
                            ? new float2(1f, 1f)
                            : key.LocalScale,
                        EaseMode = ClampEaseMode(key.EaseMode),
                        PathMode = key.PathMode <= (byte)SpriteSocketPathMode.None
                            ? key.PathMode
                            : (byte)SpriteSocketPathMode.SmoothPath,
                        UseCustomEase = key.UseCustomEase,
                        CustomEaseSamplesA = key.CustomEaseSamplesA,
                        CustomEaseSamplesB = key.CustomEaseSamplesB,
                        AllowOvershoot = key.AllowOvershoot,
                        InTangent = key.InTangent,
                        OutTangent = key.OutTangent,
                        ArcBulge = key.ArcBulge,
                        ArcClockwise = key.ArcClockwise,
                        RotationMode = key.RotationMode <=
                                       (byte)SpriteSocketRotationMode.None
                            ? key.RotationMode
                            : (byte)SpriteSocketRotationMode.Shortest,
                        RotationTurns = math.clamp(key.RotationTurns, -100, 100),
                        FacingAngleOffset = key.FacingAngleOffset,
                    };
                }
                int triggerCount = input.Triggers?.Length ?? 0;
                var triggers = builder.Allocate(ref motion.Triggers, triggerCount);
                for (int t = 0; t < triggerCount; t++)
                {
                    triggers[t] = new SpriteSocketTriggerPoint
                    {
                        NormalizedTime = math.saturate(input.Triggers[t].NormalizedTime),
                        EventId = input.Triggers[t].EventId,
                    };
                }
            }

            int inventoryCount = socketInventories?.Length ?? 0;
            var inventories = builder.Allocate(ref root.SocketInventories, inventoryCount);
            for (int i = 0; i < inventoryCount; i++)
            {
                var input = socketInventories[i];
                ref var inv = ref inventories[i];
                string invName = string.IsNullOrWhiteSpace(input.Name) ? "inventory" : input.Name.Trim();
                inv.Name = new FixedString32Bytes(invName.Length <= 30 ? invName : invName.Substring(0, 30));
                inv.GroupHash = SpriteSockets.InventoryHash(invName);
                int memberCount = input.SocketIds?.Length ?? 0;
                var hashes = builder.Allocate(ref inv.SocketIdHashes, memberCount);
                var kinds = builder.Allocate(ref inv.Kinds, memberCount);
                for (int m = 0; m < memberCount; m++)
                {
                    hashes[m] = SpriteSockets.Hash(input.SocketIds[m] ?? input.SocketNames?[m]);
                    kinds[m] = input.Kinds != null && m < input.Kinds.Length
                        ? input.Kinds[m]
                        : (byte)SpriteSocketInventoryKind.Frame;
                }
            }

            var result = builder.CreateBlobAssetReference<SpriteAnimSetBlob>(allocator);
            builder.Dispose();

            var player = default(SpriteAnimPlayer);
            player.ClipIndex = 0;
            player.Time = 0f;
            player.Speed = 1f;
            player.Playing = 1;
            player.LastEventStep = int.MinValue;
            player.OnceEventClip = -1;
            player.EventFiredMask = 0;
            player.OnceFiredKeys = default;
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

        static SpriteAnimEventKey BakeEventKey(in ClipInput.EventKeyInput key, int frameCount)
        {
            var baked = new SpriteAnimEventKey
            {
                FrameIndex = math.clamp(key.FrameIndex, 0, math.max(0, frameCount - 1)),
                NormalizedTime = math.saturate(key.NormalizedTime),
                EventId = key.EventId,
                FireMode = key.FireMode,
                IntPayload = key.IntPayload,
                FloatPayload = key.FloatPayload,
                TextHash = string.IsNullOrEmpty(key.TextPayload) ? 0UL : Fnv(key.TextPayload),
            };
            var src = key.Payloads;
            if (src == null || src.Length == 0)
                return baked;

            bool haveInt = false;
            bool haveFloat = false;
            bool haveText = false;
            int n = math.min(src.Length, SpriteEventPayloads.Max);
            for (int i = 0; i < n; i++)
            {
                var entry = src[i];
                byte kind = SpriteEventPayloads.ClampKind(entry.Kind);
                int ix = entry.IntValue;
                if (kind == (byte)SpriteEventPayloadKind.Bool)
                    ix = entry.IntValue != 0 ? 1 : 0;
                else if (kind == (byte)SpriteEventPayloadKind.Byte)
                    ix = math.clamp(entry.IntValue, 0, 255);
                var payload = new SpriteAnimEventPayload
                {
                    Kind = kind,
                    Ints = new int4(ix, entry.IntY, entry.IntZ, entry.IntW),
                    Floats = new float4(entry.FloatValue, entry.FloatY, entry.FloatZ, entry.FloatW),
                    TextHash = string.IsNullOrEmpty(entry.TextValue) ? 0UL : Fnv(entry.TextValue),
                    NameHash = string.IsNullOrWhiteSpace(entry.Name) ? 0UL : Fnv(entry.Name.Trim()),
                };
                baked.Payloads.Add(payload);
                if (!haveInt && kind == (byte)SpriteEventPayloadKind.Int)
                {
                    baked.IntPayload = payload.Ints.x;
                    haveInt = true;
                }
                else if (!haveFloat && kind == (byte)SpriteEventPayloadKind.Float)
                {
                    baked.FloatPayload = payload.Floats.x;
                    haveFloat = true;
                }
                else if (!haveText && (kind == (byte)SpriteEventPayloadKind.Text ||
                    kind == (byte)SpriteEventPayloadKind.Asset))
                {
                    baked.TextHash = payload.TextHash;
                    haveText = true;
                }
            }
            return baked;
        }

        static ClipInput.EventKeyInput[] EventKeysFromLegacy(in ClipInput input, int frameCount)
        {
            if (input.EventIds == null)
                return new ClipInput.EventKeyInput[0];
            int count = 0;
            int n = math.min(frameCount, input.EventIds.Length);
            for (int f = 0; f < n; f++)
            {
                if (input.EventIds[f] != 0)
                    count++;
            }
            if (count == 0)
                return new ClipInput.EventKeyInput[0];
            var keys = new ClipInput.EventKeyInput[count];
            int write = 0;
            for (int f = 0; f < n; f++)
            {
                if (input.EventIds[f] == 0)
                    continue;
                keys[write++] = new ClipInput.EventKeyInput
                {
                    FrameIndex = f,
                    NormalizedTime = input.EventNormalizedTimes != null && f < input.EventNormalizedTimes.Length
                        ? input.EventNormalizedTimes[f]
                        : 0f,
                    EventId = input.EventIds[f],
                };
            }
            return keys;
        }

        static byte ClampEaseMode(byte mode)
        {
            return !SpriteEase.IsValidMode(mode)
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
            player.OnceEventClip = clipIndex;
            player.EventFiredMask = 0;
            player.OnceFiredKeys.Clear();
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
                        SocketId = socket.SocketId,
                        SocketIdHash = socket.SocketIdHash,
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
            bool emitted = false;
            ulong firedMask = 0;
            if (clip.EventKeys.Length > 0)
            {
                for (int k = 0; k < clip.EventKeys.Length; k++)
                {
                    var key = clip.EventKeys[k];
                    if (key.EventId == 0 || key.FrameIndex != firstFrame || key.NormalizedTime > 1e-6f)
                        continue;
                    events.Add(ToEventBuffer(clipIndex, key));
                    emitted = true;
                    if (k < 64)
                        firedMask |= 1UL << k;
                    if (key.FireMode == (byte)SpriteEventFireMode.Once &&
                        player.OnceFiredKeys.Length < player.OnceFiredKeys.Capacity)
                        player.OnceFiredKeys.Add((ushort)k);
                }
            }
            else if (firstFrame < clip.EventIds.Length && clip.EventIds[firstFrame] != 0 &&
                     (firstFrame >= clip.EventNormalizedTimes.Length ||
                      clip.EventNormalizedTimes[firstFrame] <= 0f))
            {
                events.Add(new SpriteAnimEventBuffer
                {
                    Id = clip.EventIds[firstFrame],
                    ClipIndex = clipIndex,
                    FrameIndex = firstFrame,
                });
                emitted = true;
                firedMask = 1UL;
            }
            if (emitted)
            {
                em.SetComponentEnabled<SpriteAnimEventsPending>(e, true);
                player.LastEventStep = 0;
                player.EventFiredMask = firedMask;
                em.SetComponentData(e, player);
            }
            return true;
        }

        static SpriteAnimEventBuffer ToEventBuffer(int clipIndex, in SpriteAnimEventKey key)
        {
            return new SpriteAnimEventBuffer
            {
                Id = key.EventId,
                ClipIndex = clipIndex,
                FrameIndex = key.FrameIndex,
                FireMode = key.FireMode,
                IntPayload = key.IntPayload,
                FloatPayload = key.FloatPayload,
                TextHash = key.TextHash,
                Payloads = key.Payloads,
            };
        }
    }
}

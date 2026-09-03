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
        /// <summary>Play last→first once, then Complete (like Once, backward).</summary>
        public const byte ReverseOnce = 4;
    }

    /// <summary>One playable animation inside a character's set.</summary>
    public struct SpriteAnimDef
    {
        public ulong NameHash;     // FNV1a64 of the clip name ("Idle", "Run", ...)
        public int   FirstFrame;   // into SpriteAnimSetBlob.Frames (global frame id)
        public int   FrameCount;
        public float FrameRate;    // frames per second
        public byte  WrapMode;     // SpriteAnimWrap.*
        public byte  Interrupt;    // SpriteClipInterrupt.*
        public float CancelAfter;  // 0-1 when Interrupt == AfterTime
        public int   Priority;     // higher wins vs current when !force
        public int   OnCompleteClipIndex; // -1 none; Once end fallback clip
        public int   ComboWindowStartFrame; // inclusive
        public int   ComboWindowEndFrame;   // inclusive; <0 = disabled
        public int   ComboWindowPriorityBoost; // lowers effective current priority during window
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
        /// <summary>-1 = empty. Drained (force Play) when Once completes.</summary>
        public int   QueuedClipIndex;
        public byte  QueuedForce;
        /// <summary>-1 = none. Restored after PlayOneShot Once completes.</summary>
        public int   ResumeClipIndex;
        public byte  OneShotActive;
        /// <summary>Default crossfade length used when Play crossfadeSeconds is 0.</summary>
        public float CrossfadeDuration;
        /// <summary>Remaining blend-out seconds after a Play with fade.</summary>
        public float BlendOutTime;
        /// <summary>Active fade length (denominator for Blend). Set by Play.</summary>
        public float BlendDuration;
        /// <summary>1 → 0 over BlendDuration. For shaders / Tint; no dual draw in v1.</summary>
        public float Blend;
        /// <summary>Shared Hold/Hitstop countdown in simulation seconds (SystemAPI.Time.DeltaTime).</summary>
        public float HitstopRemaining;
        /// <summary>Speed restored when Hold/Hitstop ends. Captured when Hold/Hitstop begins.</summary>
        public float HitstopRestoreSpeed;
        /// <summary>1 while Hold/Hitstop timer is active (Speed forced to 0).</summary>
        public byte  HitstopActive;
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
            public byte   WrapMode;             // 0 loop / 1 once / 2 pingpong / 3 reverse / 4 reverse-once / 255 auto
            public byte   Interrupt;            // SpriteClipInterrupt.*; 0 = Always
            public float  CancelAfter;          // 0-1 when Interrupt == AfterTime
            public int    Priority;             // default 0
            public int    OnCompleteClipIndex;  // -1 = none (must set; default(int) is 0 = clip 0)
            public int    ComboWindowStartFrame; // inclusive; default 0
            public int    ComboWindowEndFrame;   // inclusive; set -1 to disable (default 0 is unsafe — bake sets -1 when unset via authoring)
            public int    ComboWindowPriorityBoost; // default 0
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
                : (WrapMode == SpriteAnimWrap.Once
                    || WrapMode == SpriteAnimWrap.PingPong
                    || WrapMode == SpriteAnimWrap.ReverseOnce
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
                def.Interrupt = input.Interrupt;
                def.CancelAfter = math.saturate(input.CancelAfter);
                def.Priority = input.Priority;
                def.OnCompleteClipIndex = input.OnCompleteClipIndex < 0 ? -1 : input.OnCompleteClipIndex;
                def.ComboWindowStartFrame = input.ComboWindowStartFrame;
                def.ComboWindowEndFrame = input.ComboWindowEndFrame;
                def.ComboWindowPriorityBoost = input.ComboWindowPriorityBoost;

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
            player.QueuedClipIndex = -1;
            player.QueuedForce = 0;
            player.ResumeClipIndex = -1;
            player.OneShotActive = 0;
            player.CrossfadeDuration = 0f;
            player.BlendOutTime = 0f;
            player.BlendDuration = 0f;
            player.Blend = 0f;
            player.HitstopRemaining = 0f;
            player.HitstopRestoreSpeed = 1f;
            player.HitstopActive = 0;
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
        public static bool Play(EntityManager em, Entity e, string clipName, bool force = false,
                                float crossfadeSeconds = 0f)
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
                    return Play(em, e, i, force, crossfadeSeconds);
            }
            return false;
        }

        public static bool PlayFacing(EntityManager em, Entity e, string facingGroup,
                                      SpriteFacingDirection facingDirection, bool force = false,
                                      float crossfadeSeconds = 0f)
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
                    return Play(em, e, i, force, crossfadeSeconds);
                }
            }
            return fallbackIndex >= 0 && Play(em, e, fallbackIndex, force, crossfadeSeconds);
        }

        /// <summary>
        /// Switch an entity to clip index (restarts at t=0).
        /// Returns false when priority or interrupt policy blocks the change
        /// unless <paramref name="force"/> is true.
        /// <paramref name="crossfadeSeconds"/> &gt; 0 overrides <see cref="SpriteAnimPlayer.CrossfadeDuration"/>.
        /// </summary>
        public static bool Play(EntityManager em, Entity e, int clipIndex, bool force = false,
                                float crossfadeSeconds = 0f)
        {
            if (!em.HasComponent<SpriteAnimSetRef>(e) || !em.HasComponent<SpriteAnimPlayer>(e))
                return false;
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            if (clipIndex < 0 || clipIndex >= set.Clips.Length)
                return false;

            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            if (!force)
            {
                if (!CanPlayByPriority(ref set, player, clipIndex))
                    return false;
                if (!CanInterruptCurrent(em, e, ref set, player))
                    return false;
            }
            // Regular Play supersedes one-shot resume bookkeeping.
            if (player.OneShotActive != 0)
            {
                player.OneShotActive = 0;
                player.ResumeClipIndex = -1;
            }
            player.ClipIndex = clipIndex;
            player.Time = 0f;
            player.Playing = 1;
            player.LastEventStep = int.MinValue;
            player.OnceEventClip = clipIndex;
            player.EventFiredMask = 0;
            player.OnceFiredKeys.Clear();
            float fade = crossfadeSeconds > 0f ? crossfadeSeconds : player.CrossfadeDuration;
            if (fade > 0f)
            {
                player.BlendDuration = fade;
                player.BlendOutTime = fade;
                player.Blend = 1f;
            }
            else
            {
                player.BlendDuration = 0f;
                player.BlendOutTime = 0f;
                player.Blend = 0f;
            }
            em.SetComponentData(e, player);
            if (em.HasComponent<SpriteAnimCompleted>(e))
                em.RemoveComponent<SpriteAnimCompleted>(e);

            ref var clip = ref set.Clips[clipIndex];
            bool reverseStart = clip.WrapMode == SpriteAnimWrap.ReverseLoop
                || clip.WrapMode == SpriteAnimWrap.ReverseOnce;
            int firstFrame = reverseStart
                ? math.max(0, clip.FrameCount - 1)
                : 0;
            // ReverseOnce seeks to last frame phase (Play resets Time to 0 above).
            if (clip.WrapMode == SpriteAnimWrap.ReverseOnce)
            {
                player.Time = firstFrame;
                em.SetComponentData(e, player);
            }
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
                var flip = em.HasComponent<SpriteFlip>(e)
                    ? em.GetComponentData<SpriteFlip>(e)
                    : default;
                for (int i = 0; i < clip.FrameSockets.Length; i++)
                {
                    var socket = clip.FrameSockets[i];
                    if (socket.FrameIndex != firstFrame)
                        continue;
                    sockets.Add(SpriteFlipUtility.Socket(new SpriteSocketBuffer
                    {
                        Name = socket.Name,
                        SocketId = socket.SocketId,
                        SocketIdHash = socket.SocketIdHash,
                        LocalPosition = socket.LocalPosition,
                        LocalAngle = socket.LocalAngle,
                        LocalScale = socket.LocalScale,
                    }, flip));
                }
            }

            SpriteAnimEvents.Ensure(em, e);
            var events = em.GetBuffer<SpriteAnimEventBuffer>(e);
            events.Clear();
            em.SetComponentEnabled<SpriteAnimEventsPending>(e, false);
            // Lifecycle Start (reserved Id). Dispatched with frame events this tick.
            events.Add(new SpriteAnimEventBuffer
            {
                Id = SpriteAnimLifecycleId.Start,
                ClipIndex = clipIndex,
                FrameIndex = firstFrame,
            });
            ulong firedMask = 0;
            if (clip.EventKeys.Length > 0)
            {
                for (int k = 0; k < clip.EventKeys.Length; k++)
                {
                    var key = clip.EventKeys[k];
                    if (key.EventId == 0 || key.FrameIndex != firstFrame || key.NormalizedTime > 1e-6f)
                        continue;
                    events.Add(ToEventBuffer(clipIndex, key));
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
                firedMask = 1UL;
            }
            // Always pending: at least lifecycle Start was queued.
            em.SetComponentEnabled<SpriteAnimEventsPending>(e, true);
            if (firedMask != 0)
            {
                player.LastEventStep = 0;
                player.EventFiredMask = firedMask;
                em.SetComponentData(e, player);
            }
            return true;
        }

        /// <summary>Store the next clip. Drained with force when the current Once completes.</summary>
        public static bool Queue(EntityManager em, Entity e, string clipName, bool force = true)
        {
            if (!em.HasComponent<SpriteAnimSetRef>(e) || string.IsNullOrWhiteSpace(clipName))
                return false;
            var hash = Fnv(clipName);
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            for (int i = 0; i < set.Clips.Length; i++)
            {
                if (set.Clips[i].NameHash == hash)
                    return Queue(em, e, i, force);
            }
            return false;
        }

        public static bool Queue(EntityManager em, Entity e, int clipIndex, bool force = true)
        {
            if (!em.HasComponent<SpriteAnimSetRef>(e) || !em.HasComponent<SpriteAnimPlayer>(e))
                return false;
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            if (clipIndex < 0 || clipIndex >= set.Clips.Length)
                return false;
            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            // Already stopped / completed: play immediately so the queue is not stranded.
            if (player.Playing == 0 || em.HasComponent<SpriteAnimCompleted>(e))
                return Play(em, e, clipIndex, force: true);
            player.QueuedClipIndex = clipIndex;
            player.QueuedForce = force ? (byte)1 : (byte)0;
            em.SetComponentData(e, player);
            return true;
        }

        /// <summary>
        /// Try Play; if blocked by priority/interrupt and <paramref name="queueIfBlocked"/>,
        /// store the clip in <see cref="SpriteAnimPlayer.QueuedClipIndex"/>.
        /// </summary>
        public static bool PlayOrQueue(EntityManager em, Entity e, string clipName,
                                       bool force = false, bool queueIfBlocked = true,
                                       float crossfadeSeconds = 0f)
        {
            if (Play(em, e, clipName, force, crossfadeSeconds))
                return true;
            return queueIfBlocked && Queue(em, e, clipName, force: true);
        }

        public static bool PlayOrQueue(EntityManager em, Entity e, int clipIndex,
                                       bool force = false, bool queueIfBlocked = true,
                                       float crossfadeSeconds = 0f)
        {
            if (Play(em, e, clipIndex, force, crossfadeSeconds))
                return true;
            return queueIfBlocked && Queue(em, e, clipIndex, force: true);
        }

        /// <summary>
        /// Force-play a clip and remember the previous clip. When the one-shot Once
        /// completes, resume the previous clip (force). Clears on a later normal Play.
        /// </summary>
        public static bool PlayOneShot(EntityManager em, Entity e, string clipName)
        {
            if (!em.HasComponent<SpriteAnimSetRef>(e) || string.IsNullOrWhiteSpace(clipName))
                return false;
            var hash = Fnv(clipName);
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            for (int i = 0; i < set.Clips.Length; i++)
            {
                if (set.Clips[i].NameHash == hash)
                    return PlayOneShot(em, e, i);
            }
            return false;
        }

        public static bool PlayOneShot(EntityManager em, Entity e, int clipIndex)
        {
            if (!em.HasComponent<SpriteAnimSetRef>(e) || !em.HasComponent<SpriteAnimPlayer>(e))
                return false;
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            if (clipIndex < 0 || clipIndex >= set.Clips.Length)
                return false;

            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            if (player.OneShotActive == 0)
                player.ResumeClipIndex = player.ClipIndex;
            player.OneShotActive = 1;
            em.SetComponentData(e, player);
            // force Play without clearing one-shot bookkeeping — bypass normal Play clear
            return PlayOneShotInternal(em, e, clipIndex);
        }

        static bool PlayOneShotInternal(EntityManager em, Entity e, int clipIndex)
        {
            // Same as Play(force) but preserve OneShotActive / ResumeClipIndex.
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            byte oneShot = player.OneShotActive;
            int resume = player.ResumeClipIndex;
            if (!Play(em, e, clipIndex, force: true))
                return false;
            player = em.GetComponentData<SpriteAnimPlayer>(e);
            player.OneShotActive = oneShot;
            player.ResumeClipIndex = resume;
            em.SetComponentData(e, player);
            return true;
        }

        /// <summary>
        /// Target priority must be &gt;= current priority while current is still playing.
        /// Equal priority is allowed (interrupt rules still apply).
        /// </summary>
        static bool CanPlayByPriority(ref SpriteAnimSetBlob set, in SpriteAnimPlayer player, int targetIndex)
        {
            if (player.Playing == 0 || set.Clips.Length == 0)
                return true;
            int cur = player.ClipIndex;
            if (cur < 0 || cur >= set.Clips.Length)
                return true;
            if (targetIndex < 0 || targetIndex >= set.Clips.Length)
                return true;
            int currentPriority = set.Clips[cur].Priority;
            if (InComboWindow(ref set.Clips[cur], player.Time))
                currentPriority -= set.Clips[cur].ComboWindowPriorityBoost;
            return set.Clips[targetIndex].Priority >= currentPriority;
        }

        /// <summary>
        /// Resolve the next clip when a Once ends. Consumes one-shot / queue state.
        /// Order: OneShot resume &gt; Queue &gt; OnCompleteClip &gt; none (-1).
        /// </summary>
        public static int ConsumeCompletionClip(ref SpriteAnimPlayer player, ref SpriteAnimDef ending,
                                               int clipCount)
        {
            if (player.OneShotActive != 0 &&
                player.ResumeClipIndex >= 0 && player.ResumeClipIndex < clipCount)
            {
                int next = player.ResumeClipIndex;
                player.OneShotActive = 0;
                player.ResumeClipIndex = -1;
                return next;
            }
            if (player.QueuedClipIndex >= 0 && player.QueuedClipIndex < clipCount)
            {
                int next = player.QueuedClipIndex;
                player.QueuedClipIndex = -1;
                player.QueuedForce = 0;
                return next;
            }
            if (ending.OnCompleteClipIndex >= 0 && ending.OnCompleteClipIndex < clipCount)
                return ending.OnCompleteClipIndex;
            return -1;
        }

        /// <summary>
        /// Whether Play() may replace the current clip under its interrupt policy.
        /// Stop / force bypass this; completed Once clips are always interruptible.
        /// </summary>
        public static bool CanInterruptCurrent(EntityManager em, Entity e)
        {
            if (!em.HasComponent<SpriteAnimSetRef>(e) || !em.HasComponent<SpriteAnimPlayer>(e))
                return true;
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            return CanInterruptCurrent(em, e, ref set, player);
        }

        static bool CanInterruptCurrent(EntityManager em, Entity e,
            ref SpriteAnimSetBlob set, in SpriteAnimPlayer player)
        {
            if (player.Playing == 0 || set.Clips.Length == 0)
                return true;
            if (em.HasComponent<SpriteAnimCompleted>(e))
                return true;

            int cur = player.ClipIndex;
            if (cur < 0 || cur >= set.Clips.Length)
                return true;

            ref var clip = ref set.Clips[cur];
            // Combo cancel window: treat as Interrupt.Always while inside [start,end].
            if (InComboWindow(ref clip, player.Time))
                return true;
            byte mode = clip.Interrupt;
            if (mode == (byte)SpriteClipInterrupt.Always)
                return true;
            if (mode == (byte)SpriteClipInterrupt.Never)
                return false;
            if (mode == (byte)SpriteClipInterrupt.AfterTime)
                return NormalizedTime(ref clip, player.Time) >= clip.CancelAfter;
            return true;
        }

        /// <summary>Duration-weighted 0-1 progress through the current clip (Once-oriented).</summary>
        public static float NormalizedTime(ref SpriteAnimDef clip, float phase)
        {
            int n = clip.FrameCount;
            if (n <= 0)
                return 1f;
            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                float s = clip.DurationScales.Length > i
                    ? math.max(0.01f, clip.DurationScales[i])
                    : 1f;
                total += s;
            }
            if (total <= 1e-6f)
                return 1f;

            phase = math.max(0f, phase);
            int step = (int)math.floor(phase);
            float frac = math.saturate(phase - step);
            float accrued = 0f;
            int limit = math.min(step, n);
            for (int i = 0; i < limit; i++)
            {
                accrued += clip.DurationScales.Length > i
                    ? math.max(0.01f, clip.DurationScales[i])
                    : 1f;
            }
            if (step < n)
            {
                float cur = clip.DurationScales.Length > step
                    ? math.max(0.01f, clip.DurationScales[step])
                    : 1f;
                accrued += frac * cur;
            }
            return math.saturate(accrued / total);
        }

        /// <summary>
        /// Playback rate. Negative rewinds; 0 freezes the phase clock while Playing may stay 1.
        /// No bake floor — values are applied as-is.
        /// </summary>
        public static void SetSpeed(EntityManager em, Entity e, float speed)
        {
            if (!em.HasComponent<SpriteAnimPlayer>(e))
                return;
            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            if (player.HitstopActive != 0)
            {
                // Keep clock frozen; remember the speed to restore when Hold/Hitstop ends.
                player.HitstopRestoreSpeed = speed;
                player.Speed = 0f;
            }
            else
            {
                player.Speed = speed;
            }
            em.SetComponentData(e, player);
        }

        public static float GetSpeed(EntityManager em, Entity e)
        {
            if (!em.HasComponent<SpriteAnimPlayer>(e))
                return 0f;
            return em.GetComponentData<SpriteAnimPlayer>(e).Speed;
        }

        /// <summary>Playing = 0; keeps Time. Alias of Freeze.</summary>
        public static void Pause(EntityManager em, Entity e) => SetPlaying(em, e, 0);

        /// <summary>
        /// Playing = 1 if the clip is not marked <see cref="SpriteAnimCompleted"/>.
        /// After Once completion use Play instead — Resume alone will not tick completed entities.
        /// </summary>
        public static void Resume(EntityManager em, Entity e)
        {
            if (!em.HasComponent<SpriteAnimPlayer>(e))
                return;
            if (em.HasComponent<SpriteAnimCompleted>(e))
                return;
            SetPlaying(em, e, 1);
        }

        /// <summary>Same as Pause.</summary>
        public static void Freeze(EntityManager em, Entity e) => Pause(em, e);

        /// <summary>Same as Resume.</summary>
        public static void Unfreeze(EntityManager em, Entity e) => Resume(em, e);

        static void SetPlaying(EntityManager em, Entity e, byte playing)
        {
            if (!em.HasComponent<SpriteAnimPlayer>(e))
                return;
            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            player.Playing = playing;
            em.SetComponentData(e, player);
        }

        /// <summary>Jump to a frame index. Clamps. Refreshes display; does not force Play.</summary>
        public static void SeekFrame(EntityManager em, Entity e, int frame)
        {
            if (!em.HasComponent<SpriteAnimSetRef>(e) || !em.HasComponent<SpriteAnimPlayer>(e))
                return;
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            if (set.Clips.Length == 0)
                return;
            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            int clipIndex = math.clamp(player.ClipIndex, 0, set.Clips.Length - 1);
            ref var clip = ref set.Clips[clipIndex];
            int n = math.max(1, clip.FrameCount);
            int clamped = math.clamp(frame, 0, n - 1);
            player.Time = clamped;
            player.LastEventStep = int.MinValue;
            player.EventFiredMask = 0;
            em.SetComponentData(e, player);
            ApplyDisplay(em, e, ref set, ref clip, clamped);
        }

        /// <summary>Jump to normalized 0–1 progress (duration-weighted). Does not force Play.</summary>
        public static void SeekNormalized(EntityManager em, Entity e, float t01)
        {
            if (!em.HasComponent<SpriteAnimSetRef>(e) || !em.HasComponent<SpriteAnimPlayer>(e))
                return;
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            if (set.Clips.Length == 0)
                return;
            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            int clipIndex = math.clamp(player.ClipIndex, 0, set.Clips.Length - 1);
            ref var clip = ref set.Clips[clipIndex];
            float phase = PhaseFromNormalized(ref clip, math.saturate(t01));
            player.Time = phase;
            player.LastEventStep = int.MinValue;
            player.EventFiredMask = 0;
            em.SetComponentData(e, player);
            int draw = clip.FrameCount > 0
                ? math.clamp((int)math.floor(phase), 0, clip.FrameCount - 1)
                : 0;
            ApplyDisplay(em, e, ref set, ref clip, draw);
        }

        /// <summary>Set phase clock in frames (same units as <see cref="SpriteAnimPlayer.Time"/>).</summary>
        public static void SetTime(EntityManager em, Entity e, float phaseInFrames)
        {
            if (!em.HasComponent<SpriteAnimSetRef>(e) || !em.HasComponent<SpriteAnimPlayer>(e))
                return;
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            if (set.Clips.Length == 0)
                return;
            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            int clipIndex = math.clamp(player.ClipIndex, 0, set.Clips.Length - 1);
            ref var clip = ref set.Clips[clipIndex];
            float phase = math.max(0f, phaseInFrames);
            if ((clip.WrapMode == SpriteAnimWrap.Once || clip.WrapMode == SpriteAnimWrap.ReverseOnce)
                && clip.FrameCount > 0)
                phase = math.min(phase, math.max(0, clip.FrameCount - 1) + 0.999f);
            player.Time = phase;
            player.LastEventStep = int.MinValue;
            player.EventFiredMask = 0;
            em.SetComponentData(e, player);
            int draw = clip.FrameCount > 0
                ? SpriteAnimPlayerSystem.DisplayFrame((int)math.floor(phase), clip.FrameCount, clip.WrapMode)
                : 0;
            ApplyDisplay(em, e, ref set, ref clip, draw);
        }

        static float PhaseFromNormalized(ref SpriteAnimDef clip, float t01)
        {
            int n = clip.FrameCount;
            if (n <= 0)
                return 0f;
            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                total += clip.DurationScales.Length > i
                    ? math.max(0.01f, clip.DurationScales[i])
                    : 1f;
            }
            if (total <= 1e-6f)
                return 0f;
            float target = math.saturate(t01) * total;
            float accrued = 0f;
            for (int i = 0; i < n; i++)
            {
                float s = clip.DurationScales.Length > i
                    ? math.max(0.01f, clip.DurationScales[i])
                    : 1f;
                if (accrued + s >= target - 1e-6f || i == n - 1)
                {
                    float frac = s > 1e-8f ? math.saturate((target - accrued) / s) : 0f;
                    return i + frac;
                }
                accrued += s;
            }
            return math.max(0, n - 1);
        }

        static void ApplyDisplay(EntityManager em, Entity e, ref SpriteAnimSetBlob set,
                                 ref SpriteAnimDef clip, int drawFrame)
        {
            if (clip.FrameCount <= 0)
                return;
            drawFrame = math.clamp(drawFrame, 0, clip.FrameCount - 1);
            if (em.HasComponent<SpriteAnimFrame>(e))
            {
                float4 data = set.Frames[clip.FirstFrame + drawFrame];
                em.SetComponentData(e, new SpriteAnimFrame
                {
                    Slot = (int)data.x,
                    Offset = data.yz,
                    Scale = clip.FrameScales.Length > drawFrame ? clip.FrameScales[drawFrame] : new float2(1f, 1f),
                    Rotation = clip.FrameRotations.Length > drawFrame ? clip.FrameRotations[drawFrame] : 0f,
                });
            }
            if (em.HasBuffer<SpriteSocketBuffer>(e))
            {
                var sockets = em.GetBuffer<SpriteSocketBuffer>(e);
                sockets.Clear();
                var flip = em.HasComponent<SpriteFlip>(e)
                    ? em.GetComponentData<SpriteFlip>(e)
                    : default;
                for (int i = 0; i < clip.FrameSockets.Length; i++)
                {
                    var socket = clip.FrameSockets[i];
                    if (socket.FrameIndex != drawFrame)
                        continue;
                    sockets.Add(SpriteFlipUtility.Socket(new SpriteSocketBuffer
                    {
                        Name = socket.Name,
                        SocketId = socket.SocketId,
                        SocketIdHash = socket.SocketIdHash,
                        LocalPosition = socket.LocalPosition,
                        LocalAngle = socket.LocalAngle,
                        LocalScale = socket.LocalScale,
                    }, flip));
                }
            }
        }

        /// <summary>Per-entity UV mirror. Does not change clips or the sheet texture.</summary>
        public static void SetFlip(EntityManager em, Entity e, bool flipX, bool flipY)
        {
            var previous = em.HasComponent<SpriteFlip>(e)
                ? em.GetComponentData<SpriteFlip>(e)
                : SpriteFlip.Identity;
            var flip = new SpriteFlip
            {
                X = (byte)(flipX ? 1 : 0),
                Y = (byte)(flipY ? 1 : 0),
                Pivot = previous.ResolvedPivot,
            };
            if (em.HasComponent<SpriteFlip>(e))
                em.SetComponentData(e, flip);
            else
                em.AddComponentData(e, flip);

            if (em.HasBuffer<SpriteSocketBuffer>(e))
            {
                var delta = new SpriteFlip
                {
                    X = (byte)(previous.X != flip.X ? 1 : 0),
                    Y = (byte)(previous.Y != flip.Y ? 1 : 0),
                };
                if (delta.X != 0 || delta.Y != 0)
                {
                    var sockets = em.GetBuffer<SpriteSocketBuffer>(e);
                    for (int i = 0; i < sockets.Length; i++)
                        sockets[i] = SpriteFlipUtility.Socket(sockets[i], delta);
                }
            }
        }

        public static bool TryGetFlip(EntityManager em, Entity e, out bool flipX, out bool flipY)
        {
            flipX = false;
            flipY = false;
            if (!em.HasComponent<SpriteFlip>(e))
                return false;
            var flip = em.GetComponentData<SpriteFlip>(e);
            flipX = flip.X != 0;
            flipY = flip.Y != 0;
            return true;
        }

        // ---------------------------------------------------------------------
        // Hold / Hitstop (shared timer on SpriteAnimPlayer.Hitstop*)
        // Countdown uses simulation delta (SystemAPI.Time.DeltaTime / authoring Tick dt).
        // ---------------------------------------------------------------------

        /// <summary>
        /// Freeze playback Speed at 0 for <paramref name="durationSeconds"/> (simulation time),
        /// then restore the Speed captured when the freeze began. Nested calls refresh remaining
        /// to max(old, new) and keep the original restore Speed.
        /// Alias of <see cref="Hold"/>; combat-facing name.
        /// </summary>
        public static void Hitstop(EntityManager em, Entity e, float durationSeconds)
            => Hold(em, e, durationSeconds);

        /// <summary>
        /// Freeze the phase clock for a duration (simulation seconds), then restore Speed.
        /// Shares the Hitstop* timer fields with <see cref="Hitstop"/>.
        /// </summary>
        public static void Hold(EntityManager em, Entity e, float durationSeconds)
        {
            if (!em.HasComponent<SpriteAnimPlayer>(e) || durationSeconds <= 0f)
                return;
            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            ApplyHold(ref player, durationSeconds);
            em.SetComponentData(e, player);
        }

        /// <summary>Seek to <paramref name="frame"/> then <see cref="Hold"/> for the duration.</summary>
        public static void HoldAtFrame(EntityManager em, Entity e, int frame, float durationSeconds)
        {
            SeekFrame(em, e, frame);
            Hold(em, e, durationSeconds);
        }

        /// <summary>Apply Hold/Hitstop to a player struct (shared by ECS + authoring).</summary>
        public static void ApplyHold(ref SpriteAnimPlayer player, float durationSeconds)
        {
            if (durationSeconds <= 0f)
                return;
            if (player.HitstopActive == 0)
            {
                player.HitstopRestoreSpeed = player.Speed;
                player.HitstopActive = 1;
                player.HitstopRemaining = durationSeconds;
            }
            else
            {
                player.HitstopRemaining = math.max(player.HitstopRemaining, durationSeconds);
            }
            player.Speed = 0f;
        }

        /// <summary>
        /// Countdown Hold/Hitstop. Call before advance each tick.
        /// When remaining &lt;= 0, restores <see cref="SpriteAnimPlayer.HitstopRestoreSpeed"/>.
        /// </summary>
        public static void TickHold(ref SpriteAnimPlayer player, float dt)
        {
            if (player.HitstopActive == 0)
                return;
            player.HitstopRemaining -= math.max(0f, dt);
            if (player.HitstopRemaining <= 0f)
            {
                player.HitstopRemaining = 0f;
                player.HitstopActive = 0;
                player.Speed = player.HitstopRestoreSpeed;
            }
            else
            {
                player.Speed = 0f;
            }
        }

        // ---------------------------------------------------------------------
        // Combo window
        // ---------------------------------------------------------------------

        /// <summary>
        /// True when the current display frame is inside the clip's combo window
        /// (<c>ComboWindowEndFrame &gt;= 0</c> and frame in [start, end] inclusive).
        /// </summary>
        public static bool InComboWindow(EntityManager em, Entity e)
        {
            if (!em.HasComponent<SpriteAnimSetRef>(e) || !em.HasComponent<SpriteAnimPlayer>(e))
                return false;
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            if (set.Clips.Length == 0)
                return false;
            var player = em.GetComponentData<SpriteAnimPlayer>(e);
            int cur = math.clamp(player.ClipIndex, 0, set.Clips.Length - 1);
            return InComboWindow(ref set.Clips[cur], player.Time);
        }

        public static bool InComboWindow(ref SpriteAnimDef clip, float phase)
        {
            if (clip.ComboWindowEndFrame < 0 || clip.FrameCount <= 0)
                return false;
            int frame = SpriteAnimPlayerSystem.DisplayFrame(
                (int)math.floor(math.max(0f, phase)), clip.FrameCount, clip.WrapMode);
            int start = clip.ComboWindowStartFrame;
            int end = clip.ComboWindowEndFrame;
            if (end < start)
            {
                int tmp = start;
                start = end;
                end = tmp;
            }
            return frame >= start && frame <= end;
        }

        /// <summary>
        /// Play only when <see cref="InComboWindow"/> is true. Still respects Priority/Interrupt
        /// unless <paramref name="force"/> is true.
        /// </summary>
        public static bool TryComboPlay(EntityManager em, Entity e, string clipName,
                                        bool force = false, float crossfadeSeconds = 0f)
        {
            if (!InComboWindow(em, e))
                return false;
            return Play(em, e, clipName, force, crossfadeSeconds);
        }

        public static bool TryComboPlay(EntityManager em, Entity e, int clipIndex,
                                        bool force = false, float crossfadeSeconds = 0f)
        {
            if (!InComboWindow(em, e))
                return false;
            return Play(em, e, clipIndex, force, crossfadeSeconds);
        }

        // ---------------------------------------------------------------------
        // Facing / mirror helpers (do not break existing Play / PlayFacing)
        // ---------------------------------------------------------------------

        /// <summary>Set FlipX only (keeps FlipY). Mirror = <paramref name="flipX"/> true.</summary>
        public static void SetFacing(EntityManager em, Entity e, bool flipX)
        {
            bool flipY = false;
            if (em.HasComponent<SpriteFlip>(e))
                flipY = em.GetComponentData<SpriteFlip>(e).Y != 0;
            SetFlip(em, e, flipX, flipY);
        }

        /// <summary>Set facing then Play. Mirror = flipX true.</summary>
        public static bool Play(EntityManager em, Entity e, int clipIndex, bool force,
                                float crossfadeSeconds, bool flipX)
        {
            SetFacing(em, e, flipX);
            return Play(em, e, clipIndex, force, crossfadeSeconds);
        }

        public static bool Play(EntityManager em, Entity e, string clipName, bool force,
                                float crossfadeSeconds, bool flipX)
        {
            SetFacing(em, e, flipX);
            return Play(em, e, clipName, force, crossfadeSeconds);
        }

        /// <summary>Convenience: Play with FlipX = mirrored.</summary>
        public static bool PlayMirrored(EntityManager em, Entity e, int clipIndex,
                                        bool mirrored = true, bool force = false,
                                        float crossfadeSeconds = 0f)
            => Play(em, e, clipIndex, force, crossfadeSeconds, flipX: mirrored);

        public static bool PlayMirrored(EntityManager em, Entity e, string clipName,
                                        bool mirrored = true, bool force = false,
                                        float crossfadeSeconds = 0f)
            => Play(em, e, clipName, force, crossfadeSeconds, flipX: mirrored);

        /// <summary>Resolve facing-group clip, optionally set FlipX, then Play.</summary>
        public static bool PlayFacing(EntityManager em, Entity e, string facingGroup,
                                      SpriteFacingDirection facingDirection, bool flipX,
                                      bool force = false, float crossfadeSeconds = 0f)
        {
            SetFacing(em, e, flipX);
            return PlayFacing(em, e, facingGroup, facingDirection, force, crossfadeSeconds);
        }

        // ---------------------------------------------------------------------
        // Random start / weighted clip pick
        // ---------------------------------------------------------------------

        /// <summary>
        /// Play a clip then seek to a random frame in [0, FrameCount).
        /// Uses <see cref="Unity.Mathematics.Random"/>; pass a seed or leave 0 to derive from
        /// entity index + player Time bits.
        /// </summary>
        public static bool PlayRandomStart(EntityManager em, Entity e, int clipIndex,
                                           bool force = false, float crossfadeSeconds = 0f,
                                           uint seed = 0)
        {
            if (!Play(em, e, clipIndex, force, crossfadeSeconds))
                return false;
            if (!em.HasComponent<SpriteAnimSetRef>(e) || !em.HasComponent<SpriteAnimPlayer>(e))
                return true;
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            if (clipIndex < 0 || clipIndex >= set.Clips.Length)
                return true;
            ref var clip = ref set.Clips[clipIndex];
            int n = math.max(1, clip.FrameCount);
            if (n <= 1)
                return true;
            if (seed == 0)
            {
                var player = em.GetComponentData<SpriteAnimPlayer>(e);
                seed = math.asuint(player.Time) ^ (uint)(e.Index * 747796405u + 2891336453u);
                if (seed == 0) seed = 1u;
            }
            var rng = new Unity.Mathematics.Random(seed);
            SeekFrame(em, e, rng.NextInt(0, n));
            return true;
        }

        public static bool PlayRandomStart(EntityManager em, Entity e, string clipName,
                                           bool force = false, float crossfadeSeconds = 0f,
                                           uint seed = 0)
        {
            if (!em.HasComponent<SpriteAnimSetRef>(e) || string.IsNullOrWhiteSpace(clipName))
                return false;
            var hash = Fnv(clipName);
            ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
            for (int i = 0; i < set.Clips.Length; i++)
            {
                if (set.Clips[i].NameHash == hash)
                    return PlayRandomStart(em, e, i, force, crossfadeSeconds, seed);
            }
            return false;
        }

        /// <summary>Pick a clip by weight then Play. Burst-friendly NativeArray form.</summary>
        public static bool PlayWeighted(EntityManager em, Entity e,
                                        NativeArray<int> clips, NativeArray<float> weights,
                                        ref Unity.Mathematics.Random rng,
                                        bool force = false, float crossfadeSeconds = 0f)
        {
            int count = math.min(clips.Length, weights.Length);
            if (count <= 0)
                return false;
            int pick = PickWeightedIndex(weights, count, ref rng);
            if (pick < 0)
                return false;
            return Play(em, e, clips[pick], force, crossfadeSeconds);
        }

        /// <summary>Managed overload (authoring / non-Burst).</summary>
        public static bool PlayWeighted(EntityManager em, Entity e,
                                        int[] clips, float[] weights,
                                        bool force = false, float crossfadeSeconds = 0f,
                                        uint seed = 0)
        {
            if (clips == null || weights == null)
                return false;
            int count = math.min(clips.Length, weights.Length);
            if (count <= 0)
                return false;
            if (seed == 0)
            {
                seed = (uint)(e.Index * 747796405u + 2891336453u);
                if (em.HasComponent<SpriteAnimPlayer>(e))
                    seed ^= math.asuint(em.GetComponentData<SpriteAnimPlayer>(e).Time);
                if (seed == 0) seed = 1u;
            }
            var rng = new Unity.Mathematics.Random(seed);
            int pick = PickWeightedIndex(weights, count, ref rng);
            if (pick < 0)
                return false;
            return Play(em, e, clips[pick], force, crossfadeSeconds);
        }

        /// <summary>Up to 4 clip/weight pairs.</summary>
        public static bool PlayWeighted(EntityManager em, Entity e,
                                        int clipA, float wA, int clipB, float wB,
                                        bool force = false, float crossfadeSeconds = 0f,
                                        uint seed = 0)
            => PlayWeighted(em, e, new[] { clipA, clipB }, new[] { wA, wB }, force, crossfadeSeconds, seed);

        public static bool PlayWeighted(EntityManager em, Entity e,
                                        int clipA, float wA, int clipB, float wB,
                                        int clipC, float wC,
                                        bool force = false, float crossfadeSeconds = 0f,
                                        uint seed = 0)
            => PlayWeighted(em, e, new[] { clipA, clipB, clipC }, new[] { wA, wB, wC },
                force, crossfadeSeconds, seed);

        public static bool PlayWeighted(EntityManager em, Entity e,
                                        int clipA, float wA, int clipB, float wB,
                                        int clipC, float wC, int clipD, float wD,
                                        bool force = false, float crossfadeSeconds = 0f,
                                        uint seed = 0)
            => PlayWeighted(em, e, new[] { clipA, clipB, clipC, clipD },
                new[] { wA, wB, wC, wD }, force, crossfadeSeconds, seed);

        static int PickWeightedIndex(NativeArray<float> weights, int count,
                                     ref Unity.Mathematics.Random rng)
        {
            float total = 0f;
            for (int i = 0; i < count; i++)
                total += math.max(0f, weights[i]);
            if (total <= 1e-8f)
                return count > 0 ? rng.NextInt(0, count) : -1;
            float r = rng.NextFloat() * total;
            float acc = 0f;
            for (int i = 0; i < count; i++)
            {
                acc += math.max(0f, weights[i]);
                if (r <= acc)
                    return i;
            }
            return count - 1;
        }

        static int PickWeightedIndex(float[] weights, int count, ref Unity.Mathematics.Random rng)
        {
            float total = 0f;
            for (int i = 0; i < count; i++)
                total += math.max(0f, weights[i]);
            if (total <= 1e-8f)
                return count > 0 ? rng.NextInt(0, count) : -1;
            float r = rng.NextFloat() * total;
            float acc = 0f;
            for (int i = 0; i < count; i++)
            {
                acc += math.max(0f, weights[i]);
                if (r <= acc)
                    return i;
            }
            return count - 1;
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

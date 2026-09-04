using System;
using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    public enum SpriteEaseMode : byte
    {
        Linear = 0,
        SmoothStep = 1,
        EaseIn = 2,
        EaseOut = 3,
        Step = 4,
        EaseInOut = 5,
        SineIn = 6,
        SineOut = 7,
        SineInOut = 8,
        QuadIn = 9,
        QuadOut = 10,
        QuadInOut = 11,
        CubicIn = 12,
        CubicOut = 13,
        CubicInOut = 14,
        QuartIn = 15,
        QuartOut = 16,
        QuartInOut = 17,
        QuintIn = 18,
        QuintOut = 19,
        QuintInOut = 20,
        ExpoIn = 21,
        ExpoOut = 22,
        ExpoInOut = 23,
        CircIn = 24,
        CircOut = 25,
        CircInOut = 26,
        BackIn = 27,
        BackOut = 28,
        BackInOut = 29,
        ElasticIn = 30,
        ElasticOut = 31,
        ElasticInOut = 32,
        BounceIn = 33,
        BounceOut = 34,
        BounceInOut = 35,
        None = 36,
    }

    public enum SpriteSocketPathMode : byte
    {
        SmoothPath = 0,
        Linear = 1,
        Hold = 2,
        CubicBezier = 3,
        Hermite = 4,
        Arc = 5,
        None = 6,
    }

    public enum SpriteSocketRotationMode : byte
    {
        Shortest = 0,
        Clockwise = 1,
        CounterClockwise = 2,
        ContinuousTurns = 3,
        FacePath = 4,
        Hold = 5,
        None = 6,
    }

    public enum SpriteFacingDirection : byte
    {
        None = 255,
        Right = 0,
        UpRight = 1,
        Up = 2,
        UpLeft = 3,
        Left = 4,
        DownLeft = 5,
        Down = 6,
        DownRight = 7,
    }

    public enum SpriteTimelineHitShape : byte
    {
        Circle = 0,
        Polygon = 1,
    }

    public enum SpriteColliderShape : byte
    {
        Square = 0,
        Circle = 1,
        Polygon = 2,
    }

    /// <summary>
    /// Frame = this cell only (slash). Clip = whole time that clip plays
    /// (crouch vs stand hurt). Character = body across clips, optionally
    /// filtered by Include / Exclude clip lists.
    /// Character stays 1 so existing profiles keep their body boxes.
    /// </summary>
    public enum SpriteColliderLifetime : byte
    {
        Frame = 0,
        Character = 1,
        Clip = 2,
    }

    /// <summary>
    /// Query = custom AABB / SpriteHitboxLive. Unity2D = BoxCollider2D, CircleCollider2D,
    /// or PolygonCollider2D on the authoring object. Both = both pipelines.
    /// </summary>
    public enum SpriteColliderPhysics : byte
    {
        Query = 0,
        Unity2D = 1,
        Both = 2,
    }

    /// <summary>
    /// Per-clip cancel policy for <c>SpriteAnims.Play</c> / authoring Play.
    /// Priority, Queue, PlayOneShot, OnCompleteClip, and Crossfade build on this.
    /// </summary>
    public enum SpriteClipInterrupt : byte
    {
        Always = 0,    // free cancel — idle/walk; any Play() replaces
        Never = 1,     // locked until Once completes or Stop/Force — attack cast, death
        AfterTime = 2, // cancelable only when normalized time >= CancelAfter (0-1)
    }

    /// <summary>One animation clip definition inside a sheet profile.</summary>
    [Serializable]
    public class SpriteClipDef
    {
        public const float DefaultFrameRate = 8f;
        public const byte DefaultWrapMode = 0;
        public const float DefaultFrameDurationScale = 1f;
        public static readonly Vector2 DefaultFrameScale = Vector2.one;
        public const float DefaultFrameRotation = 0f;
        public const byte DefaultTweenMode = (byte)SpriteEaseMode.Linear;
        /// <summary>Sentinel in <see cref="FrameRows"/> meaning this frame uses <see cref="Row"/>.</summary>
        public const int InheritClipRow = -1;

        public string Name = "Idle";
        public int SheetIndex;
        public int Row;
        public int[] Frames = { 0, 1, 2, 3 };
        /// <summary>
        /// Per-frame sheet row. <see cref="InheritClipRow"/> uses <see cref="Row"/>.
        /// Lets one clip sample cells from more than one row (1×1 picker, column strips).
        /// </summary>
        public int[] FrameRows;
        public float FrameRate = DefaultFrameRate;
        public byte WrapMode = DefaultWrapMode; // 0 loop / 1 once / 2 pingpong / 3 reverse / 4 reverse-once
        /// <summary>Cancel policy for Play(); see <see cref="SpriteClipInterrupt"/>.</summary>
        public byte Interrupt = (byte)SpriteClipInterrupt.Always;
        /// <summary>Normalized 0-1 threshold when Interrupt == AfterTime.</summary>
        public float CancelAfter = 0f;
        /// <summary>Higher wins when Play is not forced. Equal priority uses Interrupt only.</summary>
        public int Priority = 0;
        /// <summary>Clip index to auto-Play when Once ends (after one-shot resume / queue). -1 = none.</summary>
        public int OnCompleteClipIndex = -1;
        /// <summary>Inclusive start frame of combo cancel window.</summary>
        public int ComboWindowStartFrame = 0;
        /// <summary>Inclusive end frame. &lt; 0 disables the window (InComboWindow is false).</summary>
        public int ComboWindowEndFrame = -1;
        /// <summary>While in the window, subtract this from current Priority for Play gating.</summary>
        public int ComboWindowPriorityBoost = 0;
        public float[] FrameDurationScales = { 1f, 1f, 1f, 1f };
        public byte[] EventIds = { 0, 0, 0, 0 };
        // Normalized position inside each frame (0 = frame start, 1 = frame end).
        public float[] EventNormalizedTimes = { 0f, 0f, 0f, 0f };
        // Per-frame visual offsets in source sprite pixels; also drive runtime playback.
        // Field name remains for backward compatibility with profiles authored before 0.4.
        public Vector2[] OnionOffsets = { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero };
        public Vector2[] FrameScales = { Vector2.one, Vector2.one, Vector2.one, Vector2.one };
        public float[] FrameRotations = { 0f, 0f, 0f, 0f };
        public byte[] FrameTweenModes =
        {
            (byte)SpriteEaseMode.Linear,
            (byte)SpriteEaseMode.Linear,
            (byte)SpriteEaseMode.Linear,
            (byte)SpriteEaseMode.Linear,
        };
        public string FacingGroup = string.Empty;
        public SpriteFacingDirection Facing = SpriteFacingDirection.None;
        public List<FrameSocketDef> Sockets = new();
        public List<SpriteClipEventMarker> EventMarkers = new();

        /// <summary>Keep frame metadata aligned with the play-order frame list.</summary>
        public void EnsureFrameData()
        {
            if (Frames == null || Frames.Length == 0)
                Frames = new[] { 0 };
            int count = Frames.Length;
            FrameDurationScales = Resize(FrameDurationScales, count, DefaultFrameDurationScale);
            EventIds = Resize(EventIds, count, (byte)0);
            EventNormalizedTimes = Resize(EventNormalizedTimes, count, 0f);
            for (int i = 0; i < EventNormalizedTimes.Length; i++)
                EventNormalizedTimes[i] = Mathf.Clamp01(EventNormalizedTimes[i]);
            OnionOffsets = Resize(OnionOffsets, count, Vector2.zero);
            FrameScales = Resize(FrameScales, count, DefaultFrameScale);
            FrameRotations = Resize(FrameRotations, count, DefaultFrameRotation);
            FrameTweenModes = Resize(FrameTweenModes, count, DefaultTweenMode);
            FrameRows = Resize(FrameRows, count, InheritClipRow);
            for (int i = 0; i < FrameTweenModes.Length; i++)
            {
                if (!SpriteEase.IsValidMode(FrameTweenModes[i]))
                    FrameTweenModes[i] = DefaultTweenMode;
            }
            Sockets ??= new List<FrameSocketDef>();
            EnsureEventMarkers();
            SyncLegacyEventsFromMarkers();
        }

        /// <summary>
        /// EventMarkers is the source of truth. EventIds[] keeps the first marker
        /// on each frame so older profiles and GPU eligibility keep working.
        /// </summary>
        public void EnsureEventMarkers()
        {
            EventMarkers ??= new List<SpriteClipEventMarker>();
            if (EventMarkers.Count == 0 && EventIds != null)
            {
                for (int i = 0; i < EventIds.Length; i++)
                {
                    if (EventIds[i] == 0)
                        continue;
                    float time = EventNormalizedTimes != null && i < EventNormalizedTimes.Length
                        ? Mathf.Clamp01(EventNormalizedTimes[i])
                        : 0f;
                    EventMarkers.Add(new SpriteClipEventMarker
                    {
                        FrameIndex = i,
                        NormalizedTime = time,
                        EventId = EventIds[i],
                    });
                }
            }
            for (int i = 0; i < EventMarkers.Count; i++)
                EventMarkers[i]?.EnsurePayloads();
        }

        public void SyncLegacyEventsFromMarkers()
        {
            if (Frames == null || Frames.Length == 0)
                return;
            int count = Frames.Length;
            EventIds = Resize(EventIds, count, (byte)0);
            EventNormalizedTimes = Resize(EventNormalizedTimes, count, 0f);
            for (int i = 0; i < count; i++)
            {
                EventIds[i] = 0;
                EventNormalizedTimes[i] = 0f;
            }
            if (EventMarkers == null)
                return;
            for (int i = 0; i < EventMarkers.Count; i++)
            {
                var marker = EventMarkers[i];
                if (marker == null || marker.EventId == 0)
                    continue;
                marker.NormalizedTime = Mathf.Clamp01(marker.NormalizedTime);
                if (marker.FrameIndex < 0 || marker.FrameIndex >= count)
                    continue;
                if (EventIds[marker.FrameIndex] != 0)
                    continue;
                EventIds[marker.FrameIndex] = marker.EventId;
                EventNormalizedTimes[marker.FrameIndex] = marker.NormalizedTime;
            }
        }

        public SpriteClipEventMarker AddEventMarker(int frame, byte eventId, float normalizedTime = 0f)
        {
            EnsureEventMarkers();
            if (eventId == 0)
                return null;
            frame = Mathf.Clamp(frame, 0, Mathf.Max(0, Frames.Length - 1));
            var marker = new SpriteClipEventMarker
            {
                FrameIndex = frame,
                NormalizedTime = Mathf.Clamp01(normalizedTime),
                EventId = eventId,
            };
            EventMarkers.Add(marker);
            SyncLegacyEventsFromMarkers();
            return marker;
        }

        public SpriteClipEventMarker FirstMarkerOnFrame(int frame)
        {
            int index = IndexOfFirstMarkerOnFrame(frame);
            return index < 0 ? null : EventMarkers[index];
        }

        public int IndexOfFirstMarkerOnFrame(int frame)
        {
            EnsureEventMarkers();
            for (int i = 0; i < EventMarkers.Count; i++)
            {
                var marker = EventMarkers[i];
                if (marker != null && marker.EventId != 0 && marker.FrameIndex == frame)
                    return i;
            }
            return -1;
        }

        public int MarkerCountOnFrame(int frame)
        {
            EnsureEventMarkers();
            int count = 0;
            for (int i = 0; i < EventMarkers.Count; i++)
            {
                var marker = EventMarkers[i];
                if (marker != null && marker.EventId != 0 && marker.FrameIndex == frame)
                    count++;
            }
            return count;
        }

        public void ShiftEventMarkersAfterInsert(int insert, int count = 1)
        {
            EnsureEventMarkers();
            if (count <= 0)
                return;
            for (int i = 0; i < EventMarkers.Count; i++)
            {
                if (EventMarkers[i] != null && EventMarkers[i].FrameIndex >= insert)
                    EventMarkers[i].FrameIndex += count;
            }
            SyncLegacyEventsFromMarkers();
        }

        public void ResolveSheetCell(int frame, int columns, int rows, out int row, out int column)
            => ResolveSheetCell(Row, Frames, FrameRows, frame, columns, rows, out row, out column);

        public int SheetCellIndex(int frame, int columns, int rows)
        {
            ResolveSheetCell(frame, columns, rows, out int row, out int column);
            return row * Mathf.Max(1, columns) + column;
        }

        public bool UsesMixedSheetRows()
        {
            if (FrameRows == null || Frames == null)
                return false;
            int limit = Mathf.Min(FrameRows.Length, Frames.Length);
            for (int i = 0; i < limit; i++)
            {
                if (FrameRows[i] >= 0 && FrameRows[i] != Row)
                    return true;
            }
            return false;
        }

        public static void ResolveSheetCell(int clipRow, int[] frames, int[] frameRows,
            int frame, int columns, int rows, out int row, out int column)
        {
            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            int count = frames != null && frames.Length > 0 ? frames.Length : 1;
            frame = Mathf.Clamp(frame, 0, count - 1);
            column = frames != null && frame < frames.Length
                ? Mathf.Clamp(frames[frame], 0, columns - 1)
                : 0;
            row = Mathf.Clamp(clipRow, 0, rows - 1);
            if (frameRows != null && frame < frameRows.Length && frameRows[frame] >= 0)
                row = Mathf.Clamp(frameRows[frame], 0, rows - 1);
        }

        public void CompactEventMarkers(int[] remap)
        {
            EnsureEventMarkers();
            if (remap == null)
                return;
            EventMarkers.RemoveAll(marker =>
                marker == null || marker.FrameIndex < 0 || marker.FrameIndex >= remap.Length ||
                remap[marker.FrameIndex] < 0);
            for (int i = 0; i < EventMarkers.Count; i++)
                EventMarkers[i].FrameIndex = remap[EventMarkers[i].FrameIndex];
            SyncLegacyEventsFromMarkers();
        }

        public void RemapEventMarkerFrames(int[] oldToNew)
        {
            EnsureEventMarkers();
            if (oldToNew == null)
                return;
            for (int i = 0; i < EventMarkers.Count; i++)
            {
                var marker = EventMarkers[i];
                if (marker == null || marker.FrameIndex < 0 || marker.FrameIndex >= oldToNew.Length)
                    continue;
                marker.FrameIndex = oldToNew[marker.FrameIndex];
            }
            SyncLegacyEventsFromMarkers();
        }

        public List<SpriteClipEventMarker> CloneEventMarkers()
        {
            EnsureEventMarkers();
            var clone = new List<SpriteClipEventMarker>(EventMarkers.Count);
            for (int i = 0; i < EventMarkers.Count; i++)
            {
                if (EventMarkers[i] != null)
                    clone.Add(EventMarkers[i].Clone());
            }
            return clone;
        }

        public bool RemoveEventMarker(SpriteClipEventMarker marker)
        {
            EnsureEventMarkers();
            if (marker == null || !EventMarkers.Remove(marker))
                return false;
            SyncLegacyEventsFromMarkers();
            return true;
        }

        /// <summary>Move one play-order frame and all metadata attached to it.</summary>
        public void MoveFrame(int fromIndex, int toIndex)
        {
            EnsureFrameData();
            fromIndex = Mathf.Clamp(fromIndex, 0, Frames.Length - 1);
            toIndex = Mathf.Clamp(toIndex, 0, Frames.Length - 1);
            if (fromIndex == toIndex)
                return;

            Frames = Move(Frames, fromIndex, toIndex);
            FrameRows = Move(FrameRows, fromIndex, toIndex);
            FrameDurationScales = Move(FrameDurationScales, fromIndex, toIndex);
            EventIds = Move(EventIds, fromIndex, toIndex);
            EventNormalizedTimes = Move(EventNormalizedTimes, fromIndex, toIndex);
            OnionOffsets = Move(OnionOffsets, fromIndex, toIndex);
            FrameScales = Move(FrameScales, fromIndex, toIndex);
            FrameRotations = Move(FrameRotations, fromIndex, toIndex);
            FrameTweenModes = Move(FrameTweenModes, fromIndex, toIndex);

            for (int i = 0; i < Sockets.Count; i++)
                Sockets[i].FrameIndex = RemapIndexAfterMove(Sockets[i].FrameIndex, fromIndex, toIndex);
            EnsureEventMarkers();
            for (int i = 0; i < EventMarkers.Count; i++)
            {
                if (EventMarkers[i] != null)
                    EventMarkers[i].FrameIndex = RemapIndexAfterMove(
                        EventMarkers[i].FrameIndex, fromIndex, toIndex);
            }
            SyncLegacyEventsFromMarkers();
        }

        static int RemapIndexAfterMove(int value, int fromIndex, int toIndex)
        {
            if (value == fromIndex)
                return toIndex;
            if (fromIndex < toIndex && value > fromIndex && value <= toIndex)
                return value - 1;
            if (toIndex < fromIndex && value >= toIndex && value < fromIndex)
                return value + 1;
            return value;
        }

        static T[] Move<T>(T[] source, int fromIndex, int toIndex)
        {
            var result = new List<T>(source);
            T value = result[fromIndex];
            result.RemoveAt(fromIndex);
            result.Insert(toIndex, value);
            return result.ToArray();
        }

        static T[] Resize<T>(T[] source, int count, T defaultValue)
        {
            if (source != null && source.Length == count)
                return source;

            var result = new T[count];
            int copied = source == null ? 0 : Mathf.Min(source.Length, count);
            if (copied > 0)
                Array.Copy(source, result, copied);
            for (int i = copied; i < count; i++)
                result[i] = defaultValue;
            return result;
        }
    }

    /// <summary>Loop = fire every time playback crosses the marker. Once = fire until clip changes or Play() restarts.</summary>
    public enum SpriteEventFireMode : byte
    {
        Loop = 0,
        Once = 1,
    }

    /// <summary>One authored value on an event marker. Cap is SpriteEventPayloads.Max.</summary>
    public enum SpriteEventPayloadKind : byte
    {
        Int = 0,
        Float = 1,
        Text = 2,
        Bool = 3,
        Int2 = 4,
        Int3 = 5,
        Int4 = 6,
        Float2 = 7,
        Float3 = 8,
        Float4 = 9,
        Byte = 10,
        Color = 11,
        Half = 12,
        Asset = 13,
    }

    public static class SpriteEventPayloads
    {
        public const int Max = 8;
        public const byte LastKind = (byte)SpriteEventPayloadKind.Asset;

        public static byte ClampKind(byte kind)
        {
            return kind > LastKind ? (byte)SpriteEventPayloadKind.Int : kind;
        }
    }

    /// <summary>Name is optional; hashed at bake so gameplay can look up "damage" vs "sfx".</summary>
    [Serializable]
    public class SpriteEventPayloadEntry
    {
        public string Name = string.Empty;
        public byte Kind;
        public int IntValue;
        public int IntY;
        public int IntZ;
        public int IntW;
        public float FloatValue;
        public float FloatY;
        public float FloatZ;
        public float FloatW;
        public string TextValue = string.Empty;
        public string AssetGuid = string.Empty;

        public SpriteEventPayloadEntry Clone()
        {
            return new SpriteEventPayloadEntry
            {
                Name = Name ?? string.Empty,
                Kind = Kind,
                IntValue = IntValue,
                IntY = IntY,
                IntZ = IntZ,
                IntW = IntW,
                FloatValue = FloatValue,
                FloatY = FloatY,
                FloatZ = FloatZ,
                FloatW = FloatW,
                TextValue = TextValue ?? string.Empty,
                AssetGuid = AssetGuid ?? string.Empty,
            };
        }
    }

    /// <summary>Human-readable label for the compact byte id stored at runtime.</summary>
    [Serializable]
    public class SpriteEventDef
    {
        public byte Id = 1;
        public string Name = "Footstep";
        public Color Color = new Color(0.35f, 0.85f, 1f, 1f);
    }

    /// <summary>
    /// One animation event on a clip. Multiple markers may share a frame
    /// (footstep + land). EventIds[] stays a first-marker projection for older profiles.
    /// </summary>
    [Serializable]
    public class SpriteClipEventMarker
    {
        public int FrameIndex;
        public float NormalizedTime;
        public byte EventId = 1;
        public byte FireMode;
        public int IntPayload;
        public float FloatPayload;
        public string TextPayload = string.Empty;
        public List<SpriteEventPayloadEntry> Payloads = new();

        public bool FiresOnce => FireMode == (byte)SpriteEventFireMode.Once;

        public void EnsurePayloads()
        {
            Payloads ??= new List<SpriteEventPayloadEntry>();
            if (Payloads.Count == 0)
            {
                if (IntPayload != 0)
                    Payloads.Add(new SpriteEventPayloadEntry
                    {
                        Kind = (byte)SpriteEventPayloadKind.Int,
                        IntValue = IntPayload,
                    });
                if (Mathf.Abs(FloatPayload) > 0f)
                    Payloads.Add(new SpriteEventPayloadEntry
                    {
                        Kind = (byte)SpriteEventPayloadKind.Float,
                        FloatValue = FloatPayload,
                    });
                if (!string.IsNullOrEmpty(TextPayload))
                    Payloads.Add(new SpriteEventPayloadEntry
                    {
                        Kind = (byte)SpriteEventPayloadKind.Text,
                        TextValue = TextPayload,
                    });
            }
            if (Payloads.Count > SpriteEventPayloads.Max)
                Payloads.RemoveRange(SpriteEventPayloads.Max,
                    Payloads.Count - SpriteEventPayloads.Max);
            SyncConveniencePayloads();
        }

        public void SyncConveniencePayloads()
        {
            IntPayload = 0;
            FloatPayload = 0f;
            TextPayload = string.Empty;
            bool haveInt = false;
            bool haveFloat = false;
            bool haveText = false;
            if (Payloads == null)
                return;
            for (int i = 0; i < Payloads.Count; i++)
            {
                var entry = Payloads[i];
                if (entry == null)
                    continue;
                if (!haveInt && entry.Kind == (byte)SpriteEventPayloadKind.Int)
                {
                    IntPayload = entry.IntValue;
                    haveInt = true;
                }
                else if (!haveFloat && entry.Kind == (byte)SpriteEventPayloadKind.Float)
                {
                    FloatPayload = entry.FloatValue;
                    haveFloat = true;
                }
                else if (!haveText && entry.Kind == (byte)SpriteEventPayloadKind.Text)
                {
                    TextPayload = entry.TextValue ?? string.Empty;
                    haveText = true;
                }
            }
        }

        public SpriteEventPayloadEntry AddPayload(SpriteEventPayloadKind kind)
        {
            EnsurePayloads();
            if (Payloads.Count >= SpriteEventPayloads.Max)
                return null;
            var entry = new SpriteEventPayloadEntry { Kind = (byte)kind };
            Payloads.Add(entry);
            SyncConveniencePayloads();
            return entry;
        }

        public bool RemovePayload(SpriteEventPayloadEntry entry)
        {
            EnsurePayloads();
            if (entry == null || !Payloads.Remove(entry))
                return false;
            SyncConveniencePayloads();
            return true;
        }

        public SpriteClipEventMarker Clone()
        {
            var clone = new SpriteClipEventMarker
            {
                FrameIndex = FrameIndex,
                NormalizedTime = Mathf.Clamp01(NormalizedTime),
                EventId = EventId,
                FireMode = FireMode,
                IntPayload = IntPayload,
                FloatPayload = FloatPayload,
                TextPayload = TextPayload ?? string.Empty,
                Payloads = new List<SpriteEventPayloadEntry>(),
            };
            EnsurePayloads();
            for (int i = 0; i < Payloads.Count; i++)
            {
                if (Payloads[i] != null)
                    clone.Payloads.Add(Payloads[i].Clone());
            }
            return clone;
        }
    }

    /// <summary>One hitbox authored on one frame of one clip (uv within cell, origin top-left).</summary>
    [Serializable]
    public class FrameBoxDef
    {
        public string ClipName;
        public int FrameIndex;
        public Rect RectUV; // x,y = top-left corner in cell uv, w/h in cell uv
        public byte Id = 1;
        // Square remains enum value zero so existing box-only profiles migrate safely.
        public SpriteColliderShape Shape = SpriteColliderShape.Square;
        // Normalized inside RectUV. Used only when Shape is Polygon.
        public Vector2[] PolygonUV;
        /// <summary>
        /// Rotation in degrees around RectUV center, authoring space (y-down).
        /// 0 = axis-aligned. Preview and handles apply this; bake stores it on FrameBox.
        /// </summary>
        public float Angle;
        /// <summary>Editor visibility only. Hidden colliders are not drawn in the preview; they still bake.</summary>
        public bool Hidden;
        /// <summary>Editor authoring lock only. Locked colliders still preview and bake.</summary>
        public bool Locked;
        /// <summary>0 = this cell, 1 = character body on every clip, 2 = whole clip.</summary>
        public byte Lifetime;
        /// <summary>0 = query AABB, 1 = Unity 2D collider, 2 = both.</summary>
        public byte Physics;
        /// <summary>Unity 2D colliders only. Query hitboxes ignore this.</summary>
        public bool IsTrigger = true;
        /// <summary>
        /// Character only. Empty = all clips (minus Exclude). Non-empty = only these clips.
        /// </summary>
        public List<string> CharacterIncludeClips = new();
        /// <summary>
        /// Character only. Always wins over Include. Use to drop Attack / projectile clips
        /// from a mixed spritesheet body collider.
        /// </summary>
        public List<string> CharacterExcludeClips = new();

        public bool IsCharacter => Lifetime == (byte)SpriteColliderLifetime.Character;
        public bool IsClip => Lifetime == (byte)SpriteColliderLifetime.Clip;
        public bool IsFrame => !IsCharacter && !IsClip;
        public bool UsesQuery => Physics != (byte)SpriteColliderPhysics.Unity2D;
        public bool UsesUnity2D => Physics != (byte)SpriteColliderPhysics.Query;

        public void EnsureCharacterClipFilters()
        {
            CharacterIncludeClips ??= new List<string>();
            CharacterExcludeClips ??= new List<string>();
        }

        /// <summary>
        /// Whether this box is live on <paramref name="clipName"/>. Character uses
        /// Include (empty = all) then Exclude (always wins).
        /// </summary>
        public bool AppliesToClip(string clipName)
        {
            if (!IsCharacter)
                return string.Equals(ClipName, clipName);
            EnsureCharacterClipFilters();
            if (ListContainsClipName(CharacterExcludeClips, clipName))
                return false;
            if (CharacterIncludeClips.Count == 0)
                return true;
            return ListContainsClipName(CharacterIncludeClips, clipName);
        }

        public bool HasCharacterClipFilter()
        {
            EnsureCharacterClipFilters();
            return CharacterIncludeClips.Count > 0 || CharacterExcludeClips.Count > 0;
        }

        static bool ListContainsClipName(List<string> names, string clipName)
        {
            if (names == null || string.IsNullOrEmpty(clipName))
                return false;
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], clipName))
                    return true;
            }
            return false;
        }

        public void RenameCharacterClipFilter(string oldName, string newName)
        {
            EnsureCharacterClipFilters();
            RenameInList(CharacterIncludeClips, oldName, newName);
            RenameInList(CharacterExcludeClips, oldName, newName);
        }

        static void RenameInList(List<string> names, string oldName, string newName)
        {
            if (names == null || string.IsNullOrEmpty(oldName))
                return;
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], oldName))
                    names[i] = newName;
            }
        }

        public void BindLifetime(string clipName, int frame)
        {
            if (IsCharacter)
            {
                FrameIndex = -1;
                EnsureCharacterClipFilters();
                return;
            }

            if (!string.IsNullOrEmpty(clipName))
                ClipName = clipName;
            FrameIndex = IsClip ? -1 : frame;
        }

        public FrameBoxDef Clone(string clipName = null, int? frameIndex = null)
        {
            EnsureCharacterClipFilters();
            return new FrameBoxDef
            {
                ClipName = clipName ?? ClipName,
                FrameIndex = frameIndex ?? FrameIndex,
                RectUV = RectUV,
                Id = Id,
                Shape = Shape,
                PolygonUV = PolygonUV == null ? null : (Vector2[])PolygonUV.Clone(),
                Angle = Angle,
                Hidden = Hidden,
                Locked = Locked,
                Lifetime = Lifetime,
                Physics = Physics,
                IsTrigger = IsTrigger,
                CharacterIncludeClips = new List<string>(CharacterIncludeClips),
                CharacterExcludeClips = new List<string>(CharacterExcludeClips),
            };
        }

        public void EnsurePolygon(int vertexCount = 6)
        {
            if (PolygonUV == null || PolygonUV.Length < 3)
                PolygonUV = CreateRegularPolygon(vertexCount);
        }

        public static Vector2[] CreateRegularPolygon(int vertexCount = 6)
        {
            vertexCount = Mathf.Clamp(vertexCount, 3, 12);
            var points = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                float angle = -Mathf.PI * 0.5f + Mathf.PI * 2f * i / vertexCount;
                points[i] = new Vector2(
                    0.5f + Mathf.Cos(angle) * 0.48f,
                    0.5f + Mathf.Sin(angle) * 0.48f);
            }
            return points;
        }
    }

    /// <summary>
    /// Named attach point authored on one frame (source pixels, +x right, +y up).
    /// Name is the identity across frames; LocalPosition, LocalAngle, and LocalScale
    /// are per-frame keys. DrawLayer overrides catalog behind/front on that frame.
    /// </summary>
    [Serializable]
    public class FrameSocketDef
    {
        public string Name = "Socket";
        public int FrameIndex;
        public Vector2 LocalPosition = Vector2.zero;
        public float LocalAngle;
        public Vector2 LocalScale = Vector2.one;
        /// <summary>0 unset (hold previous), 1 behind, 2 in front, 3 catalog default.</summary>
        public byte DrawLayer;
    }

    /// <summary>
    /// One key on a profile-level socket motion track. Time is normalized so the
    /// motion is independent of every character clip's frame count and frame rate.
    /// Position is measured from the player pivot in source-sheet pixels.
    /// </summary>
    [Serializable]
    public class SpriteSocketMotionKey
    {
        [Range(0f, 1f)] public float NormalizedTime;
        public Vector2 LocalPosition = Vector2.zero;
        public float LocalAngle;
        public Vector2 LocalScale = Vector2.one;
        public byte DrawLayer;
        public byte EaseMode = (byte)SpriteEaseMode.SmoothStep;
        public byte PathMode = (byte)SpriteSocketPathMode.SmoothPath;
        public Vector2 InTangent;
        public Vector2 OutTangent;
        public float ArcBulge;
        public bool ArcClockwise;
        public byte RotationMode = (byte)SpriteSocketRotationMode.Shortest;
        public int RotationTurns;
        public float FacingAngleOffset;
        public bool AllowOvershoot;
        public bool UseCustomEase;
        public AnimationCurve CustomEaseCurve;
        public Vector4 CustomEaseSamplesA = new(0f, 1f / 7f, 2f / 7f, 3f / 7f);
        public Vector4 CustomEaseSamplesB = new(4f / 7f, 5f / 7f, 6f / 7f, 1f);

        public void EnsureCustomEaseCurve()
        {
            CustomEaseCurve ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }

        public void RebuildCustomEaseSamples()
        {
            EnsureCustomEaseCurve();
            const float s0 = 0f;
            float s1 = CustomEaseSampleAt(1f / 7f, s0);
            float s2 = CustomEaseSampleAt(2f / 7f, s1);
            float s3 = CustomEaseSampleAt(3f / 7f, s2);
            float s4 = CustomEaseSampleAt(4f / 7f, s3);
            float s5 = CustomEaseSampleAt(5f / 7f, s4);
            float s6 = CustomEaseSampleAt(6f / 7f, s5);
            const float s7 = 1f;
            CustomEaseSamplesA = new Vector4(s0, s1, s2, s3);
            CustomEaseSamplesB = new Vector4(s4, s5, s6, s7);
        }

        public float EvaluateCustomEase(float t)
        {
            t = Mathf.Clamp01(t);
            float scaled = t * 7f;
            int from = Mathf.Min(6, Mathf.FloorToInt(scaled));
            int to = from + 1;
            float blend = scaled - from;
            float value = Mathf.LerpUnclamped(
                CustomEaseSample(from), CustomEaseSample(to), blend);
            return AllowOvershoot ? value : Mathf.Clamp01(value);
        }

        float CustomEaseSampleAt(float time, float previous)
        {
            float value = CustomEaseCurve.Evaluate(time);
            if (float.IsNaN(value) || float.IsInfinity(value))
                value = previous;
            return AllowOvershoot
                ? value
                : Mathf.Max(previous, Mathf.Clamp01(value));
        }

        float CustomEaseSample(int index)
        {
            return index switch
            {
                0 => CustomEaseSamplesA.x,
                1 => CustomEaseSamplesA.y,
                2 => CustomEaseSamplesA.z,
                3 => CustomEaseSamplesA.w,
                4 => CustomEaseSamplesB.x,
                5 => CustomEaseSamplesB.y,
                6 => CustomEaseSamplesB.z,
                _ => CustomEaseSamplesB.w,
            };
        }
    }

    [Serializable]
    public class SpriteSocketTriggerDef
    {
        [Range(0f, 1f)] public float NormalizedTime;
        public byte EventId = 1;
    }

    /// <summary>
    /// Independent motion shared by all character clips (pets, orbitals, drones).
    /// The reference sheet supplies pixels-per-unit; the player pivot is always
    /// the local origin, so switching character clips cannot move the anchor.
    /// </summary>
    [Serializable]
    public class SpriteSocketMotionTrack
    {
        public string SocketName = "Socket";
        public int ReferenceSheetIndex;
        [Min(0.01f)] public float Duration = 1f;
        public bool Loop = true;
        public byte DefaultEaseMode = (byte)SpriteEaseMode.SmoothStep;
        public byte DefaultPathMode = (byte)SpriteSocketPathMode.SmoothPath;
        public byte DefaultRotationMode = (byte)SpriteSocketRotationMode.Shortest;
        public byte AnchorSpace = (byte)SpriteSocketAnchorSpace.CharacterPivot;
        public List<SpriteSocketMotionKey> Keys = new();
        public List<SpriteSocketTriggerDef> Triggers = new();

        public void Normalize(int sheetCount)
        {
            SocketName = string.IsNullOrWhiteSpace(SocketName) ? "Socket" : SocketName.Trim();
            ReferenceSheetIndex = Mathf.Clamp(ReferenceSheetIndex, 0, Mathf.Max(0, sheetCount - 1));
            Duration = Mathf.Max(0.01f, Duration);
            if (!SpriteEase.IsValidMode(DefaultEaseMode))
                DefaultEaseMode = (byte)SpriteEaseMode.SmoothStep;
            if (DefaultPathMode > (byte)SpriteSocketPathMode.None)
                DefaultPathMode = (byte)SpriteSocketPathMode.SmoothPath;
            if (DefaultRotationMode > (byte)SpriteSocketRotationMode.None)
                DefaultRotationMode = (byte)SpriteSocketRotationMode.Shortest;
            if (AnchorSpace > (byte)SpriteSocketAnchorSpace.World)
                AnchorSpace = (byte)SpriteSocketAnchorSpace.CharacterPivot;
            Keys ??= new List<SpriteSocketMotionKey>();
            Keys.RemoveAll(key => key == null);
            for (int i = 0; i < Keys.Count; i++)
            {
                Keys[i].NormalizedTime = Mathf.Clamp01(Keys[i].NormalizedTime);
                if (!SpriteEase.IsValidMode(Keys[i].EaseMode))
                    Keys[i].EaseMode = (byte)SpriteEaseMode.SmoothStep;
                if (Keys[i].PathMode > (byte)SpriteSocketPathMode.None)
                    Keys[i].PathMode = (byte)SpriteSocketPathMode.SmoothPath;
                if (Keys[i].RotationMode > (byte)SpriteSocketRotationMode.None)
                    Keys[i].RotationMode = (byte)SpriteSocketRotationMode.Shortest;
                Keys[i].RotationTurns = Mathf.Clamp(Keys[i].RotationTurns, -100, 100);
                if (Keys[i].UseCustomEase && Keys[i].CustomEaseCurve == null)
                {
                    Keys[i].EnsureCustomEaseCurve();
                    Keys[i].RebuildCustomEaseSamples();
                }
                if (Mathf.Approximately(Keys[i].LocalScale.x, 0f) &&
                    Mathf.Approximately(Keys[i].LocalScale.y, 0f))
                    Keys[i].LocalScale = Vector2.one;
            }
            Keys.Sort((a, b) => a.NormalizedTime.CompareTo(b.NormalizedTime));
            Triggers ??= new List<SpriteSocketTriggerDef>();
            Triggers.RemoveAll(trigger => trigger == null || trigger.EventId == 0);
            for (int i = 0; i < Triggers.Count; i++)
                Triggers[i].NormalizedTime = Mathf.Clamp01(Triggers[i].NormalizedTime);
            Triggers.Sort((a, b) => a.NormalizedTime.CompareTo(b.NormalizedTime));
        }
    }

    /// <summary>Named collapsible socket set (Inventory, Pets, ...). Baked as an ECS tag plus member buffer.</summary>
    [Serializable]
    public class SpriteSocketInventory
    {
        public string Name = "Inventory";
        public bool Folded;
        public byte AnchorSpace;
        public List<string> SocketNames = new();
    }

    public static class SpriteSocketIdUtility
    {
        public static string Canonical(string value, string fallback = "socket")
        {
            value = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            var chars = new char[value.Length];
            int count = 0;
            bool separator = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = char.ToLowerInvariant(value[i]);
                bool valid = c is >= 'a' and <= 'z' or >= '0' and <= '9' || c == '_';
                if (valid)
                {
                    chars[count++] = c;
                    separator = false;
                }
                else if (count > 0 && !separator)
                {
                    chars[count++] = '.';
                    separator = true;
                }
            }
            while (count > 0 && chars[count - 1] == '.')
                count--;
            return count == 0 ? "socket" : new string(chars, 0, count);
        }

        public static bool IsValid(string value)
            => !string.IsNullOrWhiteSpace(value) &&
               string.Equals(value, Canonical(value), StringComparison.Ordinal);
    }

    /// <summary>How a socket catalog preview chooses its displayed cell.</summary>
    public enum SpriteSocketPreviewPlayMode : byte
    {
        Cell = 0,
        PlayClip = 1,
        FollowHost = 2,
    }

    /// <summary>
    /// How a socket pose is timed. Follow Clip stays glued to the character.
    /// Own Clock is an independent loop (pet, orbit) and uses a Catmull-Rom path.
    /// </summary>
    public enum SpriteSocketClockMode : byte
    {
        FollowClip = 0,
        OwnClock = 1,
    }

    /// <summary>Whether an independent track follows the character or its spawn pivot.</summary>
    public enum SpriteSocketAnchorSpace : byte
    {
        CharacterPivot = 0,
        World = 1,
    }

    /// <summary>
    /// Editor preview visual for one socket identity. Pose stays on
    /// <see cref="FrameSocketDef"/>; this catalog is shared across clips.
    /// </summary>
    [Serializable]
    public class SpriteSocketCatalogItem
    {
        public string SocketName = "Weapon";
        public string SocketId = string.Empty;
        public Texture2D Texture;
        public ScriptableSpriteSheetProfile Profile;
        public string ClipName = string.Empty;
        public byte PlayMode;
        public int Columns = 1;
        public int Rows = 1;
        public Vector2 Pivot = new(0.5f, 0.5f);
        public int CellIndex;
        public Vector2 GripPixels;
        public float Scale = 1f;
        public bool FlipX;
        public int SortingOffset;
        public bool PreviewEnabled = true;
        /// <summary>Editor authoring lock only. Locked sockets still preview and bake.</summary>
        public bool Locked;
        /// <summary>
        /// 0 = lerp last key back to first (closed loop, default).
        /// 1 = hold the last key until the clip wraps (teleport).
        /// Missing serialized values stay 0 so existing sockets close by default.
        /// </summary>
        public byte PathWrap;
        /// <summary>0 = follow the character clip (weapon). 1 = own loop (pet).</summary>
        public byte MotionMode;
        /// <summary>Own Clock only. 1 = same pace as the clip, 0.5 = 2× slower. 0 means 1.</summary>
        public float Speed;

        public int CellCount => Mathf.Max(1, Columns) * Mathf.Max(1, Rows);
        public bool HasPreview => Texture != null || Profile != null;
        public bool ClosedPath => PathWrap == 0;
        public bool UsesOwnClock => MotionMode == (byte)SpriteSocketClockMode.OwnClock;
        public float ResolvedSpeed => Speed <= 0.0001f ? 1f : Mathf.Clamp(Speed, 0.05f, 8f);

        public SpriteSocketPreviewPlayMode PreviewPlayMode =>
            (SpriteSocketPreviewPlayMode)PlayMode;

        public SpriteSocketClockMode ClockMode =>
            MotionMode == (byte)SpriteSocketClockMode.OwnClock
                ? SpriteSocketClockMode.OwnClock
                : SpriteSocketClockMode.FollowClip;

        public void Normalize()
        {
            SocketId = SpriteSocketIdUtility.Canonical(SocketId, SocketName);
            Columns = Mathf.Max(1, Columns);
            Rows = Mathf.Max(1, Rows);
            CellIndex = Mathf.Clamp(CellIndex, 0, CellCount - 1);
            if (Scale <= 0f)
                Scale = 1f;
            Pivot = new Vector2(Mathf.Clamp01(Pivot.x), Mathf.Clamp01(Pivot.y));
            if (PlayMode > (byte)SpriteSocketPreviewPlayMode.FollowHost)
                PlayMode = (byte)SpriteSocketPreviewPlayMode.Cell;
            if (MotionMode > (byte)SpriteSocketClockMode.OwnClock)
                MotionMode = (byte)SpriteSocketClockMode.FollowClip;
            if (Speed <= 0f)
                Speed = 1f;
            Speed = Mathf.Clamp(Speed, 0.05f, 8f);
        }
    }

    /// <summary>Profile-level socket visuals, keyed by socket name.</summary>
    [Serializable]
    public class SpriteSocketCatalog
    {
        public List<SpriteSocketCatalogItem> Items = new();

        public void EnsureItems()
        {
            Items ??= new List<SpriteSocketCatalogItem>();
        }

        public SpriteSocketCatalogItem Find(string socketName)
        {
            EnsureItems();
            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                if (item != null && SpriteSocketKeys.NamesEqual(item.SocketName, socketName))
                    return item;
            }
            return null;
        }

        public SpriteSocketCatalogItem Ensure(string socketName)
        {
            var existing = Find(socketName);
            if (existing != null)
                return existing;
            var created = new SpriteSocketCatalogItem
            {
                SocketName = SpriteSocketKeys.CanonicalName(socketName),
            };
            Items.Add(created);
            return created;
        }

        public void Remove(string socketName)
        {
            EnsureItems();
            Items.RemoveAll(item => item != null && SpriteSocketKeys.NamesEqual(item.SocketName, socketName));
        }

        public void SyncRename(string fromName, string toName, bool oldNameStillUsed)
        {
            EnsureItems();
            string from = SpriteSocketKeys.CanonicalName(fromName);
            string to = SpriteSocketKeys.CanonicalName(toName);
            if (SpriteSocketKeys.NamesEqual(from, to))
                return;

            var source = Find(from);
            if (source == null)
                return;

            if (oldNameStillUsed)
            {
                if (Find(to) == null)
                    Items.Add(CloneItem(source, to));
                return;
            }

            var dest = Find(to);
            if (dest != null && !ReferenceEquals(dest, source))
                Remove(from);
            else
                source.SocketName = to;
        }

        public void SyncDelete(string socketName, bool nameStillUsed)
        {
            if (!nameStillUsed)
                Remove(socketName);
        }

        static SpriteSocketCatalogItem CloneItem(SpriteSocketCatalogItem source, string socketName)
        {
            return new SpriteSocketCatalogItem
            {
                SocketName = socketName,
                Texture = source.Texture,
                Profile = source.Profile,
                ClipName = source.ClipName,
                PlayMode = source.PlayMode,
                Columns = source.Columns,
                Rows = source.Rows,
                Pivot = source.Pivot,
                CellIndex = source.CellIndex,
                GripPixels = source.GripPixels,
                Scale = source.Scale,
                FlipX = source.FlipX,
                SortingOffset = source.SortingOffset,
                PreviewEnabled = source.PreviewEnabled,
                Locked = source.Locked,
                PathWrap = source.PathWrap,
                MotionMode = source.MotionMode,
                Speed = source.Speed,
            };
        }
    }


    /// <summary>
    /// How sheet cells map to UVs. Grid keeps uniform Columns×Rows cells.
    /// Cropped stores a tight opaque pixel rect per cell (spacing / gutters removed
    /// from the sampled UV) while Columns/Rows still describe the coarse layout.
    /// </summary>
    public enum SpriteSheetCellLayoutMode : byte
    {
        Grid = 0,
        Cropped = 1,
    }

    /// <summary>One cell's pivot override, normalized inside the cell.</summary>
    [Serializable]
    public struct SpriteCellPivot
    {
        public int CellIndex;
        public float X;
        public float Y;

        public SpriteCellPivot(int cellIndex, float x, float y)
        {
            CellIndex = cellIndex;
            X = x;
            Y = y;
        }
    }

    public class SpriteSheetDef
    {
        public string Name = "Sheet";
        public Texture2D Texture;
        public int Columns = SpriteSheetProfile.DefaultColumns;
        public int Rows = SpriteSheetProfile.DefaultRows;
        public float PixelsPerUnit = SpriteSheetProfile.DefaultPixelsPerUnit;
        public Vector2 Pivot = SpriteSheetProfile.DefaultPivot;
        /// <summary>Grid = uniform cells. Cropped = per-cell opaque rects in <see cref="CroppedCellRects"/>.</summary>
        public SpriteSheetCellLayoutMode CellLayoutMode = SpriteSheetCellLayoutMode.Grid;
        /// <summary>
        /// Per-cell tight opaque rects in texture pixel space (x,y = bottom-left,
        /// GetPixels32 convention), row-major. Kept when switching back to Grid so
        /// Cropped can restore without re-detect. Ignored while mode is Grid.
        /// </summary>
        public RectInt[] CroppedCellRects;

        /// <summary>Per-cell pivot overrides (normalized 0-1 inside the cell). Sparse:
        /// cells without an entry use the sheet <see cref="Pivot"/>.</summary>
        public List<SpriteCellPivot> CellPivots;
    }

    /// <summary>
    /// Authoring profile for one spritesheet: grid, ppu, pivot, clips, hitboxes.
    /// Lives in a ScriptableObject asset beside the texture; also serializes to
    /// JSON for tools/diffing. Legacy Sheet/Columns/Rows/PPU/Pivot remain so
    /// existing .asset files and JsonUtility keep loading.
    /// </summary>
    [Serializable]
    public class SpriteSheetProfile
    {
        public const int DefaultColumns = 4;
        public const int DefaultRows = 4;
        public const float DefaultPixelsPerUnit = 100f;
        public const float MinPixelsPerUnit = 0.01f;
        public const int DefaultTimelineHitPolygonVertices = 8;
        public const int DefaultOnionFrameCount = 3;
        public static readonly Vector2 DefaultPivot = new(0.5f, 0.5f);

        public Texture2D Sheet;
        public int Columns = DefaultColumns;
        public int Rows = DefaultRows;
        public float PixelsPerUnit = DefaultPixelsPerUnit;
        public Vector2 Pivot = DefaultPivot;
        /// <summary>Mirrored from the active <see cref="SpriteSheetDef"/> for editor legacy fields.</summary>
        public SpriteSheetCellLayoutMode CellLayoutMode = SpriteSheetCellLayoutMode.Grid;
        /// <summary>Mirrored from the active sheet; see <see cref="SpriteSheetDef.CroppedCellRects"/>.</summary>
        public RectInt[] CroppedCellRects;

        /// <summary>Mirrored from the active sheet; see <see cref="SpriteSheetDef.CellPivots"/>.</summary>
        public List<SpriteCellPivot> CellPivots;
        public List<SpriteSheetDef> Sheets = new();
        public List<SpriteClipDef> Clips = new();
        public List<SpriteEventDef> Events = new();
        public List<FrameBoxDef> Hitboxes = new();
        public SpriteSocketCatalog SocketCatalog = new();
        public List<SpriteSocketMotionTrack> SocketMotions = new();
        public List<SpriteSocketInventory> SocketInventories = new();
        public bool IndependentTimelineInitialized;
        public bool IndependentTimelineUsesSeconds;
        [Min(0.01f)] public float IndependentMotionDurationSeconds = 1f;
        [HideInInspector]
        [Min(1f)] public float IndependentMotionFrameRate = 12f;
        [HideInInspector]
        [Min(2)] public int IndependentMotionFrameCount = 12;
        [Min(0.01f)] public float IndependentMotionSpeed = 1f;
        public bool IndependentMotionLoop = true;
        public bool OnionSettingsInitialized = true;
        public bool OnionSkinEnabled;
        public int OnionPastFrames = DefaultOnionFrameCount;
        public int OnionFutureFrames = DefaultOnionFrameCount;
        public bool ShowOnionLayerNumbers = true;
        public SpriteTimelineHitShape TimelineHitShape = SpriteTimelineHitShape.Circle;
        public Vector2[] TimelineHitPolygon =
        {
            new(0.50f, 0.04f),
            new(0.82f, 0.16f),
            new(0.96f, 0.50f),
            new(0.82f, 0.84f),
            new(0.50f, 0.96f),
            new(0.18f, 0.84f),
            new(0.04f, 0.50f),
            new(0.18f, 0.16f),
        };

        public void EnsureTimelineHitPolygon()
        {
            if (TimelineHitPolygon == null || TimelineHitPolygon.Length < 3)
                TimelineHitPolygon = CreateRegularHitPolygon(DefaultTimelineHitPolygonVertices);
        }

        public void EnsureSocketCatalog()
        {
            SocketCatalog ??= new SpriteSocketCatalog();
            SocketCatalog.EnsureItems();
            if (Clips != null)
            {
                for (int c = 0; c < Clips.Count; c++)
                {
                    var sockets = Clips[c]?.Sockets;
                    if (sockets == null)
                        continue;
                    for (int s = 0; s < sockets.Count; s++)
                    {
                        if (sockets[s] != null)
                            SocketCatalog.Ensure(sockets[s].Name);
                    }
                }
            }
            if (SocketMotions != null)
            {
                for (int i = 0; i < SocketMotions.Count; i++)
                {
                    var motion = SocketMotions[i];
                    if (motion == null)
                        continue;
                    bool missingCatalogIdentity = SocketCatalog.Find(motion.SocketName) == null;
                    var item = SocketCatalog.Ensure(motion.SocketName);
                    if (missingCatalogIdentity)
                        item.MotionMode = (byte)SpriteSocketClockMode.OwnClock;
                }
            }
            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < SocketCatalog.Items.Count; i++)
            {
                var item = SocketCatalog.Items[i];
                if (item == null)
                    continue;
                item.Normalize();
                string root = SpriteSocketIdUtility.Canonical(item.SocketId, item.SocketName);
                string unique = root;
                int suffix = 2;
                while (!usedIds.Add(unique))
                    unique = $"{root}.{suffix++}";
                item.SocketId = unique;
            }
        }

        public void EnsureSocketMotions()
        {
            SocketMotions ??= new List<SpriteSocketMotionTrack>();
            SocketMotions.RemoveAll(track => track == null);
            IndependentMotionFrameRate = Mathf.Max(1f, IndependentMotionFrameRate);
            IndependentMotionSpeed = Mathf.Max(0.01f, IndependentMotionSpeed);
            if (!IndependentTimelineInitialized)
            {
                float legacyDuration = 1f;
                bool legacyLoop = SocketMotions.Count == 0;
                for (int i = 0; i < SocketMotions.Count; i++)
                {
                    legacyDuration = Mathf.Max(legacyDuration, SocketMotions[i].Duration);
                    legacyLoop |= SocketMotions[i].Loop;
                }
                IndependentMotionFrameCount = Mathf.Max(2,
                    Mathf.RoundToInt(legacyDuration * IndependentMotionFrameRate) + 1);
                IndependentMotionLoop = legacyLoop;
                IndependentTimelineInitialized = true;
            }
            IndependentMotionFrameCount = Mathf.Max(2, IndependentMotionFrameCount);
            if (!IndependentTimelineUsesSeconds)
            {
                IndependentMotionDurationSeconds = Mathf.Max(0.01f,
                    (IndependentMotionFrameCount - 1) / IndependentMotionFrameRate);
                IndependentTimelineUsesSeconds = true;
            }
            IndependentMotionDurationSeconds = Mathf.Max(0.01f, IndependentMotionDurationSeconds);
            float duration = IndependentMotionDuration;
            int sheetCount = Mathf.Max(1, Sheets?.Count ?? 0);
            for (int i = 0; i < SocketMotions.Count; i++)
            {
                SocketMotions[i].Duration = duration;
                SocketMotions[i].Loop = IndependentMotionLoop;
                SocketMotions[i].Normalize(sheetCount);
            }
            EnsureSocketInventories();
        }

        public float IndependentMotionDuration
            => Mathf.Max(0.01f, IndependentMotionDurationSeconds);

        public bool ExtendIndependentMotionDurationPreserveTimes(float requiredDuration)
        {
            EnsureSocketMotions();
            float oldDuration = IndependentMotionDuration;
            float newDuration = Mathf.Max(oldDuration, requiredDuration);
            if (newDuration <= oldDuration + 0.000001f)
                return false;
            float normalizedScale = oldDuration / newDuration;
            for (int i = 0; i < SocketMotions.Count; i++)
            {
                var track = SocketMotions[i];
                for (int k = 0; k < track.Keys.Count; k++)
                    track.Keys[k].NormalizedTime *= normalizedScale;
                for (int t = 0; t < track.Triggers.Count; t++)
                    track.Triggers[t].NormalizedTime *= normalizedScale;
                track.Duration = newDuration;
            }
            IndependentMotionDurationSeconds = newDuration;
            IndependentTimelineUsesSeconds = true;
            return true;
        }


        public void EnsureSocketInventories()
        {
            SocketInventories ??= new List<SpriteSocketInventory>();
            SocketInventories.RemoveAll(group => group == null);
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < SocketInventories.Count; i++)
            {
                var group = SocketInventories[i];
                if (string.IsNullOrWhiteSpace(group.Name))
                    group.Name = i == 0 ? "Inventory" : $"Inventory {i + 1}";
                else
                    group.Name = group.Name.Trim();
                group.SocketNames ??= new List<string>();
                for (int n = group.SocketNames.Count - 1; n >= 0; n--)
                {
                    string socketName = group.SocketNames[n];
                    if (string.IsNullOrWhiteSpace(socketName) ||
                        !claimed.Add(socketName.Trim()))
                    {
                        group.SocketNames.RemoveAt(n);
                        continue;
                    }
                    group.SocketNames[n] = socketName.Trim();
                }
                if (group.AnchorSpace > (byte)SpriteSocketAnchorSpace.World)
                    group.AnchorSpace = (byte)SpriteSocketAnchorSpace.CharacterPivot;
            }
            SocketInventories.RemoveAll(group =>
                group.SocketNames == null || group.SocketNames.Count == 0);
        }

        public SpriteSocketInventory FindSocketInventory(string socketName)
        {
            EnsureSocketInventories();
            if (string.IsNullOrWhiteSpace(socketName))
                return null;
            for (int i = 0; i < SocketInventories.Count; i++)
            {
                var group = SocketInventories[i];
                for (int n = 0; n < group.SocketNames.Count; n++)
                {
                    if (string.Equals(group.SocketNames[n], socketName.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                        return group;
                }
            }
            return null;
        }

        public void RemoveSocketFromInventories(string socketName)
        {
            EnsureSocketInventories();
            if (string.IsNullOrWhiteSpace(socketName))
                return;
            for (int i = SocketInventories.Count - 1; i >= 0; i--)
            {
                var group = SocketInventories[i];
                group.SocketNames.RemoveAll(name =>
                    string.Equals(name, socketName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (group.SocketNames.Count == 0)
                    SocketInventories.RemoveAt(i);
            }
        }

        public void RenameSocketInInventories(string fromName, string toName)
        {
            var group = FindSocketInventory(fromName);
            if (group == null || string.IsNullOrWhiteSpace(toName))
                return;
            for (int i = 0; i < group.SocketNames.Count; i++)
            {
                if (string.Equals(group.SocketNames[i], fromName.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    group.SocketNames[i] = toName.Trim();
            }
        }

        public void SetInventorySpace(SpriteSocketInventory group, byte space)
        {
            if (group == null)
                return;
            if (space > (byte)SpriteSocketAnchorSpace.World)
                space = (byte)SpriteSocketAnchorSpace.CharacterPivot;
            group.AnchorSpace = space;
            for (int i = 0; i < group.SocketNames.Count; i++)
            {
                var track = FindSocketMotion(group.SocketNames[i]);
                if (track != null)
                    track.AnchorSpace = space;
            }
        }

        public SpriteSocketMotionTrack FindSocketMotion(string socketName)
        {
            SocketMotions ??= new List<SpriteSocketMotionTrack>();
            if (string.IsNullOrWhiteSpace(socketName))
                return null;
            for (int i = 0; i < SocketMotions.Count; i++)
            {
                var track = SocketMotions[i];
                if (string.Equals(track.SocketName, socketName.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    return track;
            }
            return null;
        }

        public SpriteSocketMotionTrack EnsureSocketMotion(string socketName)
        {
            EnsureSocketMotions();
            var found = FindSocketMotion(socketName);
            if (found != null)
                return found;
            var track = new SpriteSocketMotionTrack
            {
                SocketName = string.IsNullOrWhiteSpace(socketName) ? "Socket" : socketName.Trim(),
                Duration = IndependentMotionDuration,
                Loop = IndependentMotionLoop,
            };
            SocketMotions.Add(track);
            return track;
        }

        public static Vector2[] CreateRegularHitPolygon(int vertexCount)
        {
            vertexCount = Mathf.Clamp(vertexCount, 3, 16);
            var points = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                float angle = -Mathf.PI * 0.5f + Mathf.PI * 2f * i / vertexCount;
                points[i] = new Vector2(
                    0.5f + Mathf.Cos(angle) * 0.46f,
                    0.5f + Mathf.Sin(angle) * 0.46f);
            }
            return points;
        }

        public string ToJson() => JsonUtility.ToJson(this, prettyPrint: true);
        public static SpriteSheetProfile FromJson(string json) =>
            JsonUtility.FromJson<SpriteSheetProfile>(json);

        /// <summary>
        /// Migrate legacy single-sheet fields into <see cref="Sheets"/>, clamp
        /// clip sheet indices, and copy a sheet back onto the legacy fields so
        /// older baker/authoring code that still reads <see cref="Sheet"/> works.
        /// </summary>
        public void EnsureSheets(int syncFromIndex = 0)
        {
            Sheets ??= new List<SpriteSheetDef>();
            if (Sheets.Count == 0)
            {
                string name = Sheet != null && !string.IsNullOrEmpty(Sheet.name)
                    ? Sheet.name
                    : "Sheet";
                Sheets.Add(new SpriteSheetDef
                {
                    Name = name,
                    Texture = Sheet,
                    Columns = Mathf.Max(1, Columns),
                    Rows = Mathf.Max(1, Rows),
                    PixelsPerUnit = PixelsPerUnit > 0f ? PixelsPerUnit : DefaultPixelsPerUnit,
                    Pivot = Pivot == default ? DefaultPivot : Pivot,
                    CellLayoutMode = CellLayoutMode,
                    CroppedCellRects = CroppedCellRects,
                    CellPivots = CellPivots,
                });
            }
            else if (Sheet != null && Sheets[0] != null && Sheets[0].Texture == null)
            {
                var first = Sheets[0];
                first.Texture = Sheet;
                if (first.Columns < 1)
                    first.Columns = Mathf.Max(1, Columns);
                if (first.Rows < 1)
                    first.Rows = Mathf.Max(1, Rows);
                if (first.PixelsPerUnit <= 0f)
                    first.PixelsPerUnit = PixelsPerUnit > 0f ? PixelsPerUnit : DefaultPixelsPerUnit;
                if (first.Pivot == default)
                    first.Pivot = Pivot == default ? DefaultPivot : Pivot;
                if (first.CellLayoutMode == SpriteSheetCellLayoutMode.Grid &&
                    CellLayoutMode == SpriteSheetCellLayoutMode.Cropped)
                    first.CellLayoutMode = CellLayoutMode;
                if ((first.CroppedCellRects == null || first.CroppedCellRects.Length == 0) &&
                    CroppedCellRects != null && CroppedCellRects.Length > 0)
                    first.CroppedCellRects = CroppedCellRects;
                if ((first.CellPivots == null || first.CellPivots.Count == 0) &&
                    CellPivots != null && CellPivots.Count > 0)
                    first.CellPivots = CellPivots;
                if (string.IsNullOrEmpty(first.Name) || first.Name == "Sheet")
                    first.Name = !string.IsNullOrEmpty(Sheet.name) ? Sheet.name : "Sheet";
            }

            int last = Mathf.Max(0, Sheets.Count - 1);
            if (Clips != null)
            {
                for (int i = 0; i < Clips.Count; i++)
                {
                    if (Clips[i] == null)
                        continue;
                    Clips[i].SheetIndex = Mathf.Clamp(Clips[i].SheetIndex, 0, last);
                }
            }

            int sync = Mathf.Clamp(syncFromIndex, 0, last);
            SyncLegacyFromSheet(sync);
        }

        public SpriteSheetDef SheetAt(int index)
        {
            if (Sheets == null || Sheets.Count == 0)
                return null;
            return Sheets[Mathf.Clamp(index, 0, Sheets.Count - 1)];
        }

        public SpriteSheetDef SheetForClip(SpriteClipDef clip)
        {
            if (Sheets == null || Sheets.Count == 0)
                return null;
            int index = clip != null ? clip.SheetIndex : 0;
            return SheetAt(index);
        }

        public SpriteClipDef FindClip(string name)
        {
            if (Clips == null || Clips.Count == 0)
                return null;
            if (!string.IsNullOrWhiteSpace(name))
            {
                for (int i = 0; i < Clips.Count; i++)
                {
                    var clip = Clips[i];
                    if (clip != null && string.Equals(clip.Name, name, StringComparison.Ordinal))
                        return clip;
                }
            }
            return Clips[0];
        }

        public bool TryGetClipDrawCell(SpriteClipDef clip, int frame,
            out Texture2D texture, out int columns, out int rows, out int cellIndex)
        {
            texture = null;
            columns = 1;
            rows = 1;
            cellIndex = 0;
            EnsureSheets();
            var sheet = SheetForClip(clip);
            texture = sheet?.Texture ?? Sheet;
            if (texture == null)
                return false;
            columns = sheet != null && sheet.Columns > 0 ? sheet.Columns : Mathf.Max(1, Columns);
            rows = sheet != null && sheet.Rows > 0 ? sheet.Rows : Mathf.Max(1, Rows);
            if (clip?.Frames == null || clip.Frames.Length == 0)
                return true;
            clip.EnsureFrameData();
            frame = Mathf.Clamp(frame, 0, clip.Frames.Length - 1);
            cellIndex = clip.SheetCellIndex(frame, columns, rows);
            return true;
        }

        public void SyncLegacyFromSheet(int index)
        {
            if (Sheets == null || Sheets.Count == 0)
                return;
            var sheet = Sheets[Mathf.Clamp(index, 0, Sheets.Count - 1)];
            if (sheet == null)
                return;
            Sheet = sheet.Texture;
            Columns = Mathf.Max(1, sheet.Columns);
            Rows = Mathf.Max(1, sheet.Rows);
            PixelsPerUnit = sheet.PixelsPerUnit > 0f ? sheet.PixelsPerUnit : DefaultPixelsPerUnit;
            Pivot = sheet.Pivot;
            CellLayoutMode = sheet.CellLayoutMode;
            CroppedCellRects = sheet.CroppedCellRects;
            CellPivots = sheet.CellPivots;
        }

        public void WriteLegacyIntoSheet(int index)
        {
            if (Sheets == null || Sheets.Count == 0)
                return;
            index = Mathf.Clamp(index, 0, Sheets.Count - 1);
            var sheet = Sheets[index] ?? (Sheets[index] = new SpriteSheetDef());
            sheet.Texture = Sheet;
            sheet.Columns = Mathf.Max(1, Columns);
            sheet.Rows = Mathf.Max(1, Rows);
            sheet.PixelsPerUnit = PixelsPerUnit > 0f ? PixelsPerUnit : DefaultPixelsPerUnit;
            sheet.Pivot = Pivot;
            sheet.CellLayoutMode = CellLayoutMode;
            sheet.CroppedCellRects = CroppedCellRects;
            sheet.CellPivots = CellPivots;
        }


        /// <summary>Per-cell pivot override lookup (sparse list on the sheet).</summary>
        public static bool TryGetCellPivot(SpriteSheetDef sheet, int cellIndex, out Vector2 pivot)
        {
            pivot = default;
            if (sheet?.CellPivots == null || cellIndex < 0)
                return false;
            for (int i = 0; i < sheet.CellPivots.Count; i++)
            {
                if (sheet.CellPivots[i].CellIndex != cellIndex)
                    continue;
                pivot = new Vector2(sheet.CellPivots[i].X, sheet.CellPivots[i].Y);
                return true;
            }
            return false;
        }

        /// <summary>Insert or update a per-cell pivot override on the sheet.</summary>
        public static void SetCellPivot(SpriteSheetDef sheet, int cellIndex, Vector2 pivot)
        {
            if (sheet == null || cellIndex < 0)
                return;
            sheet.CellPivots ??= new List<SpriteCellPivot>();
            for (int i = 0; i < sheet.CellPivots.Count; i++)
            {
                if (sheet.CellPivots[i].CellIndex == cellIndex)
                {
                    sheet.CellPivots[i] = new SpriteCellPivot(cellIndex,
                        Mathf.Clamp01(pivot.x), Mathf.Clamp01(pivot.y));
                    return;
                }
            }
            sheet.CellPivots.Add(new SpriteCellPivot(cellIndex,
                Mathf.Clamp01(pivot.x), Mathf.Clamp01(pivot.y)));
        }

        /// <summary>Remove a per-cell pivot override (falls back to the sheet pivot).</summary>
        public static void ClearCellPivot(SpriteSheetDef sheet, int cellIndex)
        {
            if (sheet?.CellPivots == null)
                return;
            for (int i = 0; i < sheet.CellPivots.Count; i++)
            {
                if (sheet.CellPivots[i].CellIndex == cellIndex)
                {
                    sheet.CellPivots.RemoveAt(i);
                    return;
                }
            }
        }
        
        public const byte CroppedAlphaThreshold = 8;

        /// <summary>Uniform grid UV rect (Unity texcoords, y-up). Row 0 = top of sheet.</summary>
        public static Rect GetUniformCellUvRect(int columns, int rows, int cellIndex)
        {
            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            int count = columns * rows;
            cellIndex = count > 0 ? ((cellIndex % count) + count) % count : 0;
            int column = cellIndex % columns;
            int row = cellIndex / columns;
            return new Rect(
                column / (float)columns,
                1f - (row + 1f) / rows,
                1f / columns,
                1f / rows);
        }

        /// <summary>Whether the sheet is Cropped and has a usable rect for <paramref name="cellIndex"/>.</summary>
        public static bool TryGetCroppedCellPixelRect(SpriteSheetDef sheet, int cellIndex, out RectInt pixelRect)
        {
            pixelRect = default;
            if (sheet == null || sheet.CellLayoutMode != SpriteSheetCellLayoutMode.Cropped)
                return false;
            var rects = sheet.CroppedCellRects;
            if (rects == null || rects.Length == 0)
                return false;
            int columns = Mathf.Max(1, sheet.Columns);
            int rows = Mathf.Max(1, sheet.Rows);
            int count = columns * rows;
            // Stale after Columns/Rows edit — fall back to uniform until Recompute.
            if (count <= 0 || rects.Length != count)
                return false;
            cellIndex = ((cellIndex % count) + count) % count;
            pixelRect = rects[cellIndex];
            return pixelRect.width > 0 && pixelRect.height > 0;
        }

        /// <summary>
        /// Active cell UV for preview / bake / GPU CropST. Grid = uniform; Cropped =
        /// stored opaque rect (falls back to uniform when missing).
        /// </summary>
        public static Rect GetCellUvRect(SpriteSheetDef sheet, int cellIndex)
        {
            int columns = sheet != null && sheet.Columns > 0 ? sheet.Columns : 1;
            int rows = sheet != null && sheet.Rows > 0 ? sheet.Rows : 1;
            if (TryGetCroppedCellPixelRect(sheet, cellIndex, out var pixel) &&
                sheet.Texture != null &&
                sheet.Texture.width > 0 &&
                sheet.Texture.height > 0)
            {
                float invW = 1f / sheet.Texture.width;
                float invH = 1f / sheet.Texture.height;
                return new Rect(
                    pixel.x * invW,
                    pixel.y * invH,
                    pixel.width * invW,
                    pixel.height * invH);
            }
            return GetUniformCellUvRect(columns, rows, cellIndex);
        }

        /// <summary>CropST = (uvWidth, uvHeight, uvOriginX, uvOriginY) bottom-left.</summary>
        public static Vector4 GetCellCropST(SpriteSheetDef sheet, int cellIndex)
        {
            var uv = GetCellUvRect(sheet, cellIndex);
            return new Vector4(uv.width, uv.height, uv.x, uv.y);
        }

        /// <summary>
        /// Pixel size of the active cell. Cropped uses the opaque rect; Grid uses
        /// texture / columns×rows. False when no texture.
        /// </summary>
        public static bool TryGetActiveCellPixels(SpriteSheetDef sheet, int cellIndex,
            out float cellW, out float cellH)
        {
            cellW = 0f;
            cellH = 0f;
            if (TryGetCroppedCellPixelRect(sheet, cellIndex, out var pixel))
            {
                cellW = pixel.width;
                cellH = pixel.height;
                return true;
            }
            return TryGetCellPixels(sheet, out cellW, out cellH);
        }

        public static bool HasCroppedCellData(SpriteSheetDef sheet)
        {
            if (sheet == null || sheet.CroppedCellRects == null || sheet.CroppedCellRects.Length == 0)
                return false;
            int count = Mathf.Max(1, sheet.Columns) * Mathf.Max(1, sheet.Rows);
            return sheet.CroppedCellRects.Length == count;
        }

        /// <summary>
        /// Build per-cell tight opaque rects inside each uniform grid band.
        /// Pixel y = 0 at texture bottom (GetPixels32). Empty cells keep a 1×1
        /// sentinel at the coarse cell origin so length stays Columns×Rows.
        /// </summary>
        public static RectInt[] BuildCroppedCellRects(Color32[] pixels, int width, int height,
            int columns, int rows, byte alphaThreshold = CroppedAlphaThreshold)
        {
            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            var result = new RectInt[columns * rows];
            if (pixels == null || pixels.Length < width * height)
            {
                for (int i = 0; i < result.Length; i++)
                {
                    var uv = GetUniformCellUvRect(columns, rows, i);
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(uv.x * width), 0, width - 1);
                    int y0 = Mathf.Clamp(Mathf.FloorToInt(uv.y * height), 0, height - 1);
                    result[i] = new RectInt(x0, y0, 1, 1);
                }
                return result;
            }

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    int index = row * columns + col;
                    var uv = GetUniformCellUvRect(columns, rows, index);
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(uv.x * width), 0, width);
                    int y0 = Mathf.Clamp(Mathf.FloorToInt(uv.y * height), 0, height);
                    int x1 = Mathf.Clamp(Mathf.CeilToInt((uv.x + uv.width) * width), 0, width);
                    int y1 = Mathf.Clamp(Mathf.CeilToInt((uv.y + uv.height) * height), 0, height);
                    if (x1 <= x0) x1 = Mathf.Min(width, x0 + 1);
                    if (y1 <= y0) y1 = Mathf.Min(height, y0 + 1);

                    int minX = x1, minY = y1, maxX = x0 - 1, maxY = y0 - 1;
                    bool found = false;
                    for (int y = y0; y < y1; y++)
                    {
                        int rowOff = y * width;
                        for (int x = x0; x < x1; x++)
                        {
                            if (pixels[rowOff + x].a > alphaThreshold)
                            {
                                found = true;
                                if (x < minX) minX = x;
                                if (y < minY) minY = y;
                                if (x > maxX) maxX = x;
                                if (y > maxY) maxY = y;
                            }
                        }
                    }
                    result[index] = found
                        ? new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1)
                        : new RectInt(x0, y0, 1, 1);
                }
            }
            return result;
        }

        public static void EstimateBandSpacing(Color32[] pixels, int width, int height,
            byte alphaThreshold, out float avgColGutter, out float avgRowGutter,
            out int opaqueColumnBands, out int opaqueRowBands)
        {
            avgColGutter = 0f;
            avgRowGutter = 0f;
            opaqueColumnBands = 0;
            opaqueRowBands = 0;
            if (pixels == null || width <= 0 || height <= 0 || pixels.Length < width * height)
                return;

            var colOpaque = new bool[width];
            var rowOpaque = new bool[height];
            for (int y = 0; y < height; y++)
            {
                int rowOff = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (pixels[rowOff + x].a > alphaThreshold)
                    {
                        colOpaque[x] = true;
                        rowOpaque[y] = true;
                    }
                }
            }

            MeasureGaps(colOpaque, out opaqueColumnBands, out avgColGutter);
            MeasureGaps(rowOpaque, out opaqueRowBands, out avgRowGutter);
        }

        static void MeasureGaps(bool[] opaque, out int bandCount, out float avgGap)
        {
            bandCount = 0;
            avgGap = 0f;
            int gapSum = 0;
            int gapCount = 0;
            bool inside = false;
            int gapRun = 0;
            bool seenBand = false;
            for (int i = 0; i < opaque.Length; i++)
            {
                if (opaque[i])
                {
                    if (!inside)
                    {
                        if (seenBand && gapRun > 0)
                        {
                            gapSum += gapRun;
                            gapCount++;
                        }
                        bandCount++;
                        inside = true;
                        seenBand = true;
                        gapRun = 0;
                    }
                }
                else
                {
                    if (inside)
                        inside = false;
                    if (seenBand)
                        gapRun++;
                }
            }
            if (gapCount > 0)
                avgGap = gapSum / (float)gapCount;
        }

        /// <summary>Fill a float4 CropST array (xy size, zw origin) for the instance/GPU path.</summary>
        public static Vector4[] BuildCellCropSTArray(SpriteSheetDef sheet)
        {
            int columns = Mathf.Max(1, sheet != null ? sheet.Columns : 1);
            int rows = Mathf.Max(1, sheet != null ? sheet.Rows : 1);
            int count = columns * rows;
            var result = new Vector4[count];
            for (int i = 0; i < count; i++)
                result[i] = GetCellCropST(sheet, i);
            return result;
        }


        /// <summary>
        /// Cell size in source pixels. PPU does not change this — it is
        /// texture size divided by columns / rows.
        /// </summary>
        public static bool TryGetCellPixels(SpriteSheetDef sheet, out float cellW, out float cellH)
        {
            cellW = 0f;
            cellH = 0f;
            if (sheet?.Texture == null)
                return false;
            int columns = Mathf.Max(1, sheet.Columns);
            int rows = Mathf.Max(1, sheet.Rows);
            cellW = sheet.Texture.width / (float)columns;
            cellH = sheet.Texture.height / (float)rows;
            return true;
        }

        public static float GetPixelsPerUnit(SpriteSheetDef sheet)
        {
            float ppu = sheet != null && sheet.PixelsPerUnit > 0f
                ? sheet.PixelsPerUnit
                : DefaultPixelsPerUnit;
            return Mathf.Max(MinPixelsPerUnit, ppu);
        }

        /// <summary>World height of one cell: cellH / PPU. 1 if the sheet has no texture.</summary>
        public static float GetWorldHeight(SpriteSheetDef sheet, float fallback = 1f)
        {
            if (!TryGetCellPixels(sheet, out _, out float cellH))
                return fallback;
            return cellH / GetPixelsPerUnit(sheet);
        }

        /// <summary>Pixel width / height of one cell. 1 if the sheet has no texture.</summary>
        public static float GetCellAspect(SpriteSheetDef sheet)
        {
            if (!TryGetCellPixels(sheet, out float cellW, out float cellH) || cellH < 0.01f)
                return 1f;
            return cellW / cellH;
        }

        public static float GetCellAspect(Texture2D texture, int columns, int rows)
        {
            if (texture == null)
                return 1f;
            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            float cellH = texture.height / (float)rows;
            if (cellH < 0.01f)
                return 1f;
            return (texture.width / (float)columns) / cellH;
        }

        public bool SheetsWorldHeightsDiffer(float epsilon = 0.0005f)
        {
            if (Sheets == null)
                return false;
            float? first = null;
            for (int i = 0; i < Sheets.Count; i++)
            {
                var sheet = Sheets[i];
                if (sheet?.Texture == null)
                    continue;
                float height = GetWorldHeight(sheet);
                if (first == null)
                    first = height;
                else if (Mathf.Abs(height - first.Value) > epsilon)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Make every textured sheet the same world height as
        /// <paramref name="sourceSheetIndex"/>. WorldH = source cellH / source PPU
        /// (1 if the source has no texture). Each other sheet then gets
        /// PPU = max(0.01, its cellH / worldH). Sheets without a texture are skipped.
        /// Legacy fields are synced from the source sheet afterwards.
        /// </summary>
        public void MatchSheetsWorldSize(int sourceSheetIndex)
        {
            if (Sheets == null || Sheets.Count == 0)
                return;

            int src = Mathf.Clamp(sourceSheetIndex, 0, Sheets.Count - 1);
            var source = Sheets[src];
            float worldH = 1f;
            if (TryGetCellPixels(source, out _, out float srcCellH))
            {
                worldH = srcCellH / GetPixelsPerUnit(source);
                if (worldH <= 0f)
                    worldH = 1f;
            }

            for (int i = 0; i < Sheets.Count; i++)
            {
                var sheet = Sheets[i];
                if (!TryGetCellPixels(sheet, out _, out float cellH))
                    continue;
                sheet.PixelsPerUnit = Mathf.Max(MinPixelsPerUnit, cellH / worldH);
            }

            SyncLegacyFromSheet(src);
        }
    }

    public static class SpriteSocketMotionTimeUtility
    {
        public static float ResolveStepSeconds(
            bool frameMode, float secondsStep, float stepFps, int stepCount)
        {
            int count = Mathf.Max(1, stepCount);
            return frameMode
                ? count / Mathf.Max(1f, stepFps)
                : count * Mathf.Max(0.001f, secondsStep);
        }
    }

}

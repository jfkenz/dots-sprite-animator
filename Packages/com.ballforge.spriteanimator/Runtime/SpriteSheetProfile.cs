using System;
using System.Collections.Generic;
using UnityEngine;

namespace BallForge.Sprites.DOTS
{
    public enum SpriteEaseMode : byte
    {
        Linear = 0,
        SmoothStep = 1,
        EaseIn = 2,
        EaseOut = 3,
        Step = 4,
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

        public string Name = "Idle";
        public int Row;
        public int[] Frames = { 0, 1, 2, 3 };
        public float FrameRate = DefaultFrameRate;
        public byte WrapMode = DefaultWrapMode; // 0 loop / 1 once / 2 pingpong / 3 reverse
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
            for (int i = 0; i < FrameTweenModes.Length; i++)
            {
                if (FrameTweenModes[i] > (byte)SpriteEaseMode.Step)
                    FrameTweenModes[i] = DefaultTweenMode;
            }
            Sockets ??= new List<FrameSocketDef>();
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
            FrameDurationScales = Move(FrameDurationScales, fromIndex, toIndex);
            EventIds = Move(EventIds, fromIndex, toIndex);
            EventNormalizedTimes = Move(EventNormalizedTimes, fromIndex, toIndex);
            OnionOffsets = Move(OnionOffsets, fromIndex, toIndex);
            FrameScales = Move(FrameScales, fromIndex, toIndex);
            FrameRotations = Move(FrameRotations, fromIndex, toIndex);
            FrameTweenModes = Move(FrameTweenModes, fromIndex, toIndex);

            for (int i = 0; i < Sockets.Count; i++)
                Sockets[i].FrameIndex = RemapIndexAfterMove(Sockets[i].FrameIndex, fromIndex, toIndex);
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

    /// <summary>Human-readable label for the compact byte id stored at runtime.</summary>
    [Serializable]
    public class SpriteEventDef
    {
        public byte Id = 1;
        public string Name = "Footstep";
        public Color Color = new Color(0.35f, 0.85f, 1f, 1f);
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
    /// Name is the identity across frames; LocalPosition and LocalAngle are per-frame keys.
    /// </summary>
    [Serializable]
    public class FrameSocketDef
    {
        public string Name = "Socket";
        public int FrameIndex;
        public Vector2 LocalPosition = Vector2.zero;
        public float LocalAngle;
    }

    /// <summary>
    /// Authoring profile for one spritesheet: grid, ppu, pivot, clips, hitboxes.
    /// Lives in a ScriptableObject asset beside the texture; also serializes to
    /// JSON for tools/diffing.
    /// </summary>
    [Serializable]
    public class SpriteSheetProfile
    {
        public const int DefaultColumns = 4;
        public const int DefaultRows = 4;
        public const float DefaultPixelsPerUnit = 100f;
        public const int DefaultTimelineHitPolygonVertices = 8;
        public const int DefaultOnionFrameCount = 3;
        public static readonly Vector2 DefaultPivot = new(0.5f, 0.5f);

        public Texture2D Sheet;
        public int Columns = DefaultColumns;
        public int Rows = DefaultRows;
        public float PixelsPerUnit = DefaultPixelsPerUnit;
        public Vector2 Pivot = DefaultPivot;
        public List<SpriteClipDef> Clips = new();
        public List<SpriteEventDef> Events = new();
        public List<FrameBoxDef> Hitboxes = new();
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
    }

    /// <summary>Project asset consumed by both the editor and runtime bakers.</summary>
    public class ScriptableSpriteSheetProfile : ScriptableObject
    {
        public SpriteSheetProfile Data = new();
    }
}

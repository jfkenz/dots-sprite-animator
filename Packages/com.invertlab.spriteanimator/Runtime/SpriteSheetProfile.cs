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
        public int SheetIndex;
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
        /// <summary>
        /// Rotation in degrees around RectUV center, authoring space (y-down).
        /// 0 = axis-aligned. Preview and handles apply this; bake stores it on FrameBox.
        /// </summary>
        public float Angle;
        /// <summary>Editor visibility only. Hidden colliders are not drawn in the preview; they still bake.</summary>
        public bool Hidden;

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
        public List<SpriteSocketMotionKey> Keys = new();
        public List<SpriteSocketTriggerDef> Triggers = new();

        public void Normalize(int sheetCount)
        {
            SocketName = string.IsNullOrWhiteSpace(SocketName) ? "Socket" : SocketName.Trim();
            ReferenceSheetIndex = Mathf.Clamp(ReferenceSheetIndex, 0, Mathf.Max(0, sheetCount - 1));
            Duration = Mathf.Max(0.01f, Duration);
            Keys ??= new List<SpriteSocketMotionKey>();
            Keys.RemoveAll(key => key == null);
            for (int i = 0; i < Keys.Count; i++)
            {
                Keys[i].NormalizedTime = Mathf.Clamp01(Keys[i].NormalizedTime);
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
                PathWrap = source.PathWrap,
                MotionMode = source.MotionMode,
                Speed = source.Speed,
            };
        }
    }

    /// <summary>One texture + grid inside a profile that can hold several sheets.</summary>
    [Serializable]
    public class SpriteSheetDef
    {
        public string Name = "Sheet";
        public Texture2D Texture;
        public int Columns = SpriteSheetProfile.DefaultColumns;
        public int Rows = SpriteSheetProfile.DefaultRows;
        public float PixelsPerUnit = SpriteSheetProfile.DefaultPixelsPerUnit;
        public Vector2 Pivot = SpriteSheetProfile.DefaultPivot;
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
        public List<SpriteSheetDef> Sheets = new();
        public List<SpriteClipDef> Clips = new();
        public List<SpriteEventDef> Events = new();
        public List<FrameBoxDef> Hitboxes = new();
        public SpriteSocketCatalog SocketCatalog = new();
        public List<SpriteSocketMotionTrack> SocketMotions = new();
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
                    var item = SocketCatalog.Ensure(motion.SocketName);
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
            int sheetCount = Mathf.Max(1, Sheets?.Count ?? 0);
            for (int i = 0; i < SocketMotions.Count; i++)
                SocketMotions[i].Normalize(sheetCount);
        }

        public SpriteSocketMotionTrack FindSocketMotion(string socketName)
        {
            EnsureSocketMotions();
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
            var found = FindSocketMotion(socketName);
            if (found != null)
                return found;
            var track = new SpriteSocketMotionTrack
            {
                SocketName = string.IsNullOrWhiteSpace(socketName) ? "Socket" : socketName.Trim(),
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
            int row = Mathf.Clamp(clip.Row, 0, Mathf.Max(0, rows - 1));
            int column = Mathf.Clamp(clip.Frames[frame], 0, Mathf.Max(0, columns - 1));
            cellIndex = row * columns + column;
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

}

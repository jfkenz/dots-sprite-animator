using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Name is the socket identity across frames. Each (name, frame) key stores its
    /// own LocalPosition, LocalAngle, and LocalScale. Playback lerps between keys;
    /// a missing next key holds the last pose.
    /// </summary>
    public static class SpriteSocketKeys
    {
        static readonly Color[] Palette =
        {
            new Color(1f, 0.55f, 0.2f, 1f),
            new Color(0.35f, 0.78f, 1f, 1f),
            new Color(0.35f, 0.85f, 0.4f, 1f),
        };

        public static string CanonicalName(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? "Socket" : name.Trim();
        }

        static readonly HashSet<string> UniqueNameScratch = new(System.StringComparer.Ordinal);

        public static bool NamesEqual(string a, string b)
        {
            if (ReferenceEquals(a, b))
                return true;
            return string.Equals(CanonicalName(a), CanonicalName(b), System.StringComparison.Ordinal);
        }

        public static string NextDefaultName(IList<FrameSocketDef> sockets)
        {
            var names = UniqueNamesInOrder(sockets);
            int n = 1;
            while (true)
            {
                string candidate = $"Socket {n}";
                bool taken = false;
                for (int i = 0; i < names.Count; i++)
                {
                    if (NamesEqual(names[i], candidate))
                    {
                        taken = true;
                        break;
                    }
                }
                if (!taken)
                    return candidate;
                n++;
            }
        }

        public static List<string> UniqueNamesInOrder(IList<FrameSocketDef> sockets)
        {
            var names = new List<string>();
            FillUniqueNamesInOrder(sockets, names);
            return names;
        }

        public static void FillUniqueNamesInOrder(IList<FrameSocketDef> sockets, List<string> names)
        {
            names?.Clear();
            if (sockets == null || names == null)
                return;
            UniqueNameScratch.Clear();
            for (int i = 0; i < sockets.Count; i++)
            {
                var socket = sockets[i];
                if (socket == null)
                    continue;
                string name = CanonicalName(socket.Name);
                if (UniqueNameScratch.Add(name))
                    names.Add(name);
            }
        }

        public static int IdentityIndex(IList<FrameSocketDef> sockets, string name)
        {
            if (sockets == null)
                return -1;
            string want = CanonicalName(name);
            UniqueNameScratch.Clear();
            int index = 0;
            for (int i = 0; i < sockets.Count; i++)
            {
                var socket = sockets[i];
                if (socket == null)
                    continue;
                string current = CanonicalName(socket.Name);
                if (!UniqueNameScratch.Add(current))
                    continue;
                if (current == want)
                    return index;
                index++;
            }
            return -1;
        }

        public static bool NameExists(IList<FrameSocketDef> sockets, string name)
        {
            if (sockets == null)
                return false;
            for (int i = 0; i < sockets.Count; i++)
            {
                var socket = sockets[i];
                if (socket != null && NamesEqual(socket.Name, name))
                    return true;
            }
            return false;
        }

        public static FrameSocketDef FindOnFrame(IList<FrameSocketDef> sockets, string name, int frame)
        {
            if (sockets == null)
                return null;
            for (int i = 0; i < sockets.Count; i++)
            {
                var socket = sockets[i];
                if (socket != null && socket.FrameIndex == frame && NamesEqual(socket.Name, name))
                    return socket;
            }
            return null;
        }

        public static Vector2 ResolvedScale(Vector2 scale)
        {
            if (Mathf.Approximately(scale.x, 0f) && Mathf.Approximately(scale.y, 0f))
                return Vector2.one;
            return scale;
        }

        public static bool TryGetLastKnown(IList<FrameSocketDef> sockets, string name, int frame,
            out Vector2 position, out float angle)
        {
            return TryGetLastKnown(sockets, name, frame, out position, out angle, out _);
        }

        public static bool TryGetLastKnown(IList<FrameSocketDef> sockets, string name, int frame,
            out Vector2 position, out float angle, out Vector2 scale)
        {
            position = Vector2.zero;
            angle = 0f;
            scale = Vector2.one;
            if (sockets == null)
                return false;

            FrameSocketDef bestBefore = null;
            FrameSocketDef bestAfter = null;
            for (int i = 0; i < sockets.Count; i++)
            {
                var socket = sockets[i];
                if (socket == null || !NamesEqual(socket.Name, name))
                    continue;
                if (socket.FrameIndex <= frame)
                {
                    if (bestBefore == null || socket.FrameIndex > bestBefore.FrameIndex)
                        bestBefore = socket;
                }
                else if (bestAfter == null || socket.FrameIndex < bestAfter.FrameIndex)
                {
                    bestAfter = socket;
                }
            }

            var chosen = bestBefore ?? bestAfter;
            if (chosen == null)
                return false;
            position = chosen.LocalPosition;
            angle = chosen.LocalAngle;
            scale = ResolvedScale(chosen.LocalScale);
            return true;
        }

        public static bool TryGetPose(IList<FrameSocketDef> sockets, string name, int frame,
            out Vector2 position, out float angle, out bool onFrame)
        {
            return TryGetPose(sockets, name, frame, out position, out angle, out _, out onFrame);
        }

        public static bool TryGetPose(IList<FrameSocketDef> sockets, string name, int frame,
            out Vector2 position, out float angle, out Vector2 scale, out bool onFrame)
        {
            var key = FindOnFrame(sockets, name, frame);
            if (key != null)
            {
                position = key.LocalPosition;
                angle = key.LocalAngle;
                scale = ResolvedScale(key.LocalScale);
                onFrame = true;
                return true;
            }

            onFrame = false;
            return TryGetLastKnown(sockets, name, frame, out position, out angle, out scale);
        }

        public static void CollectKeysSorted(IList<FrameSocketDef> sockets, string name,
            List<FrameSocketDef> into)
        {
            into?.Clear();
            if (sockets == null || into == null)
                return;
            for (int i = 0; i < sockets.Count; i++)
            {
                var socket = sockets[i];
                if (socket != null && NamesEqual(socket.Name, name))
                    into.Add(socket);
            }
            into.Sort(CompareFrameIndex);
        }

        static int CompareFrameIndex(FrameSocketDef a, FrameSocketDef b)
        {
            int left = a != null ? a.FrameIndex : 0;
            int right = b != null ? b.FrameIndex : 0;
            return left.CompareTo(right);
        }

        public static bool UsesClosedPath(SpriteSocketCatalog catalog, string name)
        {
            var item = catalog?.Find(name);
            return item == null || item.ClosedPath;
        }

        public static bool TryGetNeighborKeys(IList<FrameSocketDef> sockets, string name, int frame,
            out FrameSocketDef previous, out FrameSocketDef next)
        {
            return TryGetNeighborKeys(sockets, name, frame, false, out previous, out next, out _);
        }

        public static bool TryGetNeighborKeys(IList<FrameSocketDef> sockets, string name, int frame,
            bool closedPath, out FrameSocketDef previous, out FrameSocketDef next, out bool wrapped)
        {
            previous = null;
            next = null;
            wrapped = false;
            FrameSocketDef first = null;
            FrameSocketDef last = null;
            if (sockets == null)
                return false;
            for (int i = 0; i < sockets.Count; i++)
            {
                var socket = sockets[i];
                if (socket == null || !NamesEqual(socket.Name, name))
                    continue;
                if (first == null || socket.FrameIndex < first.FrameIndex)
                    first = socket;
                if (last == null || socket.FrameIndex > last.FrameIndex)
                    last = socket;
                if (socket.FrameIndex <= frame)
                {
                    if (previous == null || socket.FrameIndex > previous.FrameIndex)
                        previous = socket;
                }
                else if (next == null || socket.FrameIndex < next.FrameIndex)
                {
                    next = socket;
                }
            }

            if (closedPath && first != null && last != null && first.FrameIndex != last.FrameIndex)
            {
                if (next == null && previous != null)
                {
                    next = first;
                    wrapped = true;
                }
                else if (previous == null && next != null)
                {
                    previous = last;
                    wrapped = true;
                }
            }

            return previous != null || next != null;
        }

        /// <summary>
        /// Samples the socket at authored time: lerp from the key at or before
        /// <paramref name="frame"/> toward the next key. <paramref name="fraction"/> is
        /// 0 at the start of the frame and 1 at the start of the next frame.
        /// When <paramref name="closedPath"/> is set, the last key lerps back to the first.
        /// </summary>
        public static bool TryGetInterpolatedPose(IList<FrameSocketDef> sockets, string name,
            SpriteClipDef clip, int frame, float fraction, bool closedPath,
            out Vector2 position, out float angle, out Vector2 scale, out bool onFrame)
        {
            onFrame = FindOnFrame(sockets, name, frame) != null;
            if (!TryGetNeighborKeys(sockets, name, frame, closedPath, out var from, out var to, out bool wrapped))
            {
                position = Vector2.zero;
                angle = 0f;
                scale = Vector2.one;
                return false;
            }

            if (from == null)
            {
                position = to.LocalPosition;
                angle = to.LocalAngle;
                scale = ResolvedScale(to.LocalScale);
                return true;
            }

            if (to == null || clip?.Frames == null || from.FrameIndex == to.FrameIndex)
            {
                position = from.LocalPosition;
                angle = from.LocalAngle;
                scale = ResolvedScale(from.LocalScale);
                return true;
            }

            float now = SpriteAnimPlayback.AuthoredStartTime(clip, frame) +
                        Mathf.Clamp01(fraction) * SpriteAnimPlayback.FrameDuration(clip, frame);
            float u;
            if (wrapped)
            {
                float total = SpriteAnimPlayback.TotalAuthoredDuration(clip);
                float t0 = SpriteAnimPlayback.AuthoredStartTime(clip, from.FrameIndex);
                float t1 = SpriteAnimPlayback.AuthoredStartTime(clip, to.FrameIndex);
                float span = total - t0 + t1;
                if (span <= 1e-6f)
                {
                    position = from.LocalPosition;
                    angle = from.LocalAngle;
                    scale = ResolvedScale(from.LocalScale);
                    return true;
                }

                float elapsed = now + 1e-6f >= t0 ? now - t0 : total - t0 + now;
                u = Mathf.Clamp01(elapsed / span);
            }
            else
            {
                float t0 = SpriteAnimPlayback.AuthoredStartTime(clip, from.FrameIndex);
                float t1 = SpriteAnimPlayback.AuthoredStartTime(clip, to.FrameIndex);
                u = t1 > t0 ? Mathf.InverseLerp(t0, t1, now) : 0f;
            }

            u = SpriteEase.Evaluate(SpriteEaseMode.Linear, u);
            position = Vector2.Lerp(from.LocalPosition, to.LocalPosition, u);
            angle = Mathf.LerpAngle(from.LocalAngle, to.LocalAngle, u);
            scale = Vector2.Lerp(ResolvedScale(from.LocalScale), ResolvedScale(to.LocalScale), u);
            return true;
        }

        public static bool UsesOwnClock(SpriteSocketCatalog catalog, string name)
        {
            var item = catalog?.Find(name);
            return item != null && item.UsesOwnClock;
        }

        /// <summary>
        /// Host clip time, or an independent wrapped clock when the catalog item is Own Clock.
        /// <paramref name="previewTime"/> is the unwrapped preview clock.
        /// </summary>
        public static float ResolveSampleTime(SpriteClipDef clip, SpriteSocketCatalogItem item,
            float previewTime, bool previewLoop)
        {
            if (clip == null)
                return Mathf.Max(0f, previewTime);
            if (item == null || !item.UsesOwnClock)
                return SpriteAnimPlayback.EvaluatePreview(clip, previewTime, previewLoop).TimelineTime;

            float duration = SpriteAnimPlayback.TotalAuthoredDuration(clip);
            float t = Mathf.Max(0f, previewTime) * item.ResolvedSpeed;
            if (item.ClosedPath || previewLoop)
                return Mathf.Repeat(t, duration);
            return Mathf.Min(t, duration);
        }

        public static bool TrySampleAtTime(IList<FrameSocketDef> sockets, string name,
            SpriteClipDef clip, float sampleTime, bool closedPath, bool catmullRom,
            out Vector2 position, out float angle, out Vector2 scale, out bool onFrame)
        {
            int frame = SpriteAnimPlayback.AuthoredFrameAtTime(clip, sampleTime, out float fraction);
            onFrame = FindOnFrame(sockets, name, frame) != null;
            if (!catmullRom)
            {
                return TryGetInterpolatedPose(sockets, name, clip, frame, fraction, closedPath,
                    out position, out angle, out scale, out _);
            }

            return TryCatmullAtTime(sockets, name, clip, sampleTime, closedPath,
                out position, out angle, out scale);
        }

        static readonly List<FrameSocketDef> SampleScratch = new(16);

        static bool TryCatmullAtTime(IList<FrameSocketDef> sockets, string name, SpriteClipDef clip,
            float sampleTime, bool closedPath, out Vector2 position, out float angle, out Vector2 scale)
        {
            CollectKeysSorted(sockets, name, SampleScratch);
            int n = SampleScratch.Count;
            if (n == 0)
            {
                position = Vector2.zero;
                angle = 0f;
                scale = Vector2.one;
                return false;
            }

            if (n == 1 || clip?.Frames == null)
            {
                var only = SampleScratch[0];
                position = only.LocalPosition;
                angle = only.LocalAngle;
                scale = ResolvedScale(only.LocalScale);
                return true;
            }

            float duration = SpriteAnimPlayback.TotalAuthoredDuration(clip);
            if (closedPath && n >= 2)
                sampleTime = Mathf.Repeat(Mathf.Max(0f, sampleTime), duration);
            else
                sampleTime = Mathf.Clamp(sampleTime, 0f, duration);

            int i1 = FindPathSegment(clip, SampleScratch, sampleTime, closedPath && n >= 2, duration, out float u);
            int i2 = WrapKeyIndex(i1 + 1, n, closedPath);
            int i0 = WrapKeyIndex(i1 - 1, n, closedPath);
            int i3 = WrapKeyIndex(i2 + 1, n, closedPath);
            var p0 = SampleScratch[i0];
            var p1 = SampleScratch[i1];
            var p2 = SampleScratch[i2];
            var p3 = SampleScratch[i3];
            position = CatmullRom(p0.LocalPosition, p1.LocalPosition, p2.LocalPosition, p3.LocalPosition, u);
            angle = Mathf.LerpAngle(p1.LocalAngle, p2.LocalAngle, u);
            scale = Vector2.Lerp(ResolvedScale(p1.LocalScale), ResolvedScale(p2.LocalScale), u);
            return true;
        }

        static int FindPathSegment(SpriteClipDef clip, List<FrameSocketDef> keys, float sampleTime,
            bool closedPath, float duration, out float u)
        {
            int n = keys.Count;
            u = 0f;
            if (n <= 1)
                return 0;

            float first = SpriteAnimPlayback.AuthoredStartTime(clip, keys[0].FrameIndex);
            float last = SpriteAnimPlayback.AuthoredStartTime(clip, keys[n - 1].FrameIndex);

            if (closedPath)
            {
                float span = duration - last + first;
                bool onWrap = sampleTime >= last || sampleTime + 1e-6f < first;
                if (onWrap && span > 1e-6f)
                {
                    float elapsed = sampleTime + 1e-6f >= last
                        ? sampleTime - last
                        : duration - last + sampleTime;
                    u = Mathf.Clamp01(elapsed / span);
                    return n - 1;
                }
            }
            else
            {
                if (sampleTime <= first)
                    return 0;
                if (sampleTime >= last)
                {
                    u = 1f;
                    return n - 2;
                }
            }

            for (int i = 0; i < n - 1; i++)
            {
                float t0 = SpriteAnimPlayback.AuthoredStartTime(clip, keys[i].FrameIndex);
                float t1 = SpriteAnimPlayback.AuthoredStartTime(clip, keys[i + 1].FrameIndex);
                if (sampleTime < t1 || i == n - 2)
                {
                    u = t1 > t0 ? Mathf.InverseLerp(t0, t1, sampleTime) : 0f;
                    return i;
                }
            }

            u = 1f;
            return n - 2;
        }

        static int WrapKeyIndex(int index, int count, bool closedPath)
        {
            if (count <= 0)
                return 0;
            if (closedPath)
                return (index % count + count) % count;
            return Mathf.Clamp(index, 0, count - 1);
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            t = Mathf.Clamp01(t);
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        public static FrameSocketDef EnsureFrameKey(List<FrameSocketDef> sockets, string name, int frame)
        {
            sockets ??= new List<FrameSocketDef>();
            var existing = FindOnFrame(sockets, name, frame);
            if (existing != null)
                return existing;

            TryGetLastKnown(sockets, name, frame, out var position, out var angle, out var scale);
            var created = new FrameSocketDef
            {
                Name = CanonicalName(name),
                FrameIndex = frame,
                LocalPosition = position,
                LocalAngle = angle,
                LocalScale = scale,
            };
            sockets.Add(created);
            return created;
        }

        public const byte DrawUnset = 0;
        public const byte DrawBehind = 1;
        public const byte DrawFront = 2;
        public const byte DrawCatalog = 3;

        public static bool CatalogDrawsBehind(SpriteSocketCatalogItem item)
            => item != null && item.SortingOffset < 0;

        public static byte ResolveDrawLayer(IList<FrameSocketDef> sockets, string name, int frame,
            bool closedPath)
        {
            FrameSocketDef lastBefore = null;
            FrameSocketDef lastInClip = null;
            if (sockets == null)
                return DrawUnset;
            for (int i = 0; i < sockets.Count; i++)
            {
                var socket = sockets[i];
                if (socket == null || !NamesEqual(socket.Name, name) || socket.DrawLayer == DrawUnset)
                    continue;
                if (lastInClip == null || socket.FrameIndex > lastInClip.FrameIndex)
                    lastInClip = socket;
                if (socket.FrameIndex <= frame &&
                    (lastBefore == null || socket.FrameIndex > lastBefore.FrameIndex))
                    lastBefore = socket;
            }

            var chosen = lastBefore;
            if (chosen == null && closedPath)
                chosen = lastInClip;
            return chosen != null ? chosen.DrawLayer : DrawUnset;
        }

        public static bool IsDrawnBehind(IList<FrameSocketDef> sockets, string name, int frame,
            bool catalogBehind, bool closedPath)
        {
            byte layer = ResolveDrawLayer(sockets, name, frame, closedPath);
            if (layer == DrawBehind)
                return true;
            if (layer == DrawFront)
                return false;
            return catalogBehind;
        }

        public static bool IsDrawnBehindAtTime(IList<FrameSocketDef> sockets, string name,
            SpriteClipDef clip, float sampleTime, bool catalogBehind, bool closedPath)
        {
            int frame = SpriteAnimPlayback.AuthoredFrameAtTime(clip, sampleTime, out _);
            return IsDrawnBehind(sockets, name, frame, catalogBehind, closedPath);
        }

        public static void RenameIdentity(IList<FrameSocketDef> sockets, string fromName, string toName)
        {
            if (sockets == null)
                return;
            string next = CanonicalName(toName);
            for (int i = 0; i < sockets.Count; i++)
            {
                var socket = sockets[i];
                if (socket != null && NamesEqual(socket.Name, fromName))
                    socket.Name = next;
            }
        }

        public static int DeleteIdentity(List<FrameSocketDef> sockets, string name)
        {
            if (sockets == null)
                return 0;
            return sockets.RemoveAll(socket => socket != null && NamesEqual(socket.Name, name));
        }

        public static bool RemoveFrameKey(List<FrameSocketDef> sockets, string name, int frame)
        {
            if (sockets == null)
                return false;
            return sockets.RemoveAll(socket =>
                socket != null && NamesEqual(socket.Name, name) && socket.FrameIndex == frame) > 0;
        }

        public static bool NameExistsOnAnyClip(IList<SpriteClipDef> clips, string name)
        {
            if (clips == null)
                return false;
            for (int i = 0; i < clips.Count; i++)
            {
                var sockets = clips[i]?.Sockets;
                if (sockets != null && NameExists(sockets, name))
                    return true;
            }
            return false;
        }

        public static Color ColorForIndex(int index)
        {
            if (index >= 0 && index < Palette.Length)
                return Palette[index];
            float hue = Mathf.Repeat(0.08f + Mathf.Max(0, index) * 0.17f, 1f);
            return Color.HSVToRGB(hue, 0.72f, 1f);
        }
    }
}

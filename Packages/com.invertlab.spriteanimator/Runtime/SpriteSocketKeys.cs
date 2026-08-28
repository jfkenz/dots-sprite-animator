using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Name is the socket identity across frames. Each (name, frame) key stores its
    /// own LocalPosition, LocalAngle, and LocalScale. Missing keys fall back to the last known pose.
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

        public static bool NamesEqual(string a, string b)
        {
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
            if (sockets == null)
                return names;
            for (int i = 0; i < sockets.Count; i++)
            {
                string name = CanonicalName(sockets[i].Name);
                bool exists = false;
                for (int j = 0; j < names.Count; j++)
                {
                    if (NamesEqual(names[j], name))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    names.Add(name);
            }
            return names;
        }

        public static int IdentityIndex(IList<FrameSocketDef> sockets, string name)
        {
            var names = UniqueNamesInOrder(sockets);
            for (int i = 0; i < names.Count; i++)
            {
                if (NamesEqual(names[i], name))
                    return i;
            }
            return -1;
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
                if (sockets != null && IdentityIndex(sockets, name) >= 0)
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

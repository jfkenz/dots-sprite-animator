using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Reads the layers of a Photoshop file (.psd, and .psb large documents): 8-bit RGB or grayscale,
    /// raw or RLE layer data. Layer groups, hidden layers, opacity (baked into alpha) and clipping masks
    /// (a layer clipped to the one below) are kept. Blend modes, adjustment layers, layer masks and effects are not.
    /// </summary>
    public static class SpritePsdReader
    {
        public sealed class Layer
        {
            public string Name;
            /// <summary>Canvas pixels, top-left origin (Photoshop), right/bottom exclusive.</summary>
            public RectInt Rect;
            /// <summary>Rect.width x Rect.height, bottom-left origin (Unity textures). Null for groups.</summary>
            public Color32[] Pixels;
            public bool Visible = true;
            public byte Opacity = 255;
            /// <summary>Clipped to the nearest unclipped layer below (Photoshop clipping mask).</summary>
            public bool Clipped;
            public bool IsGroup;
            /// <summary>Index (in <see cref="Document.Layers"/>) of the group holding this layer, or -1.</summary>
            public int Parent = -1;
            /// <summary>Why the pixels could not be read (compression, depth), or null.</summary>
            public string Problem;
        }

        public sealed class Document
        {
            public int Width;
            public int Height;
            /// <summary>Bottom to top, the way Photoshop stores them; groups come after their children.</summary>
            public List<Layer> Layers = new List<Layer>();
        }

        public static bool TryRead(byte[] data, out Document document, out string error)
        {
            document = null;
            error = null;
            try
            {
                document = Read(data);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static Document Read(byte[] data)
        {
            var r = new Reader(data);
            if (r.Ascii(4) != "8BPS")
                throw new FormatException("Not a Photoshop file.");
            int version = r.U16();
            if (version != 1 && version != 2)
                throw new FormatException("Unknown Photoshop version " + version + ".");
            bool big = version == 2;
            r.Skip(6);
            r.U16(); // merged image channels
            int height = (int)r.U32();
            int width = (int)r.U32();
            int depth = r.U16();
            int mode = r.U16();
            if (depth != 8)
                throw new FormatException("Only 8 bits per channel is supported (this file is " + depth + "-bit). Image > Mode > 8 Bits/Channel in Photoshop.");
            if (mode != 3 && mode != 1)
                throw new FormatException("Only RGB and Grayscale files are supported.");
            r.Skip((int)r.U32()); // colour mode data
            r.Skip((int)r.U32()); // image resources

            var doc = new Document { Width = width, Height = height };
            long layerAndMask = big ? (long)r.U64() : r.U32();
            if (layerAndMask == 0)
                return doc; // flat file: no layers
            long layerInfo = big ? (long)r.U64() : r.U32();
            if (layerInfo == 0)
                return doc;
            int count = Math.Abs((short)r.U16());

            var records = new List<(Layer layer, List<(int id, long length)> channels, int divider)>(count);
            for (int i = 0; i < count; i++)
            {
                var layer = new Layer();
                int top = (int)r.U32(), left = (int)r.U32(), bottom = (int)r.U32(), right = (int)r.U32();
                layer.Rect = new RectInt(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
                int channelCount = r.U16();
                var channels = new List<(int, long)>(channelCount);
                for (int c = 0; c < channelCount; c++)
                {
                    int id = (short)r.U16();
                    long length = big ? (long)r.U64() : r.U32();
                    channels.Add((id, length));
                }
                if (r.Ascii(4) != "8BIM")
                    throw new FormatException("Broken layer record " + i + ".");
                r.Skip(4); // blend mode
                layer.Opacity = r.U8();
                layer.Clipped = r.U8() != 0;
                int flags = r.U8();
                layer.Visible = (flags & 2) == 0;
                r.Skip(1);
                long extraEnd = r.U32();
                extraEnd += r.Position;
                r.Skip((int)r.U32()); // layer mask
                r.Skip((int)r.U32()); // blending ranges
                int nameLength = r.U8();
                layer.Name = Latin1(r.Bytes(nameLength));
                r.Skip((4 - (nameLength + 1) % 4) % 4);
                int divider = 0;
                while (r.Position + 12 <= extraEnd)
                {
                    string sig = r.Ascii(4);
                    if (sig != "8BIM" && sig != "8B64")
                        break;
                    string key = r.Ascii(4);
                    long length = big && LongKey(key) ? (long)r.U64() : r.U32();
                    long start = r.Position;
                    if (key == "luni" && length >= 4)
                    {
                        int chars = (int)r.U32();
                        var sb = new StringBuilder(chars);
                        for (int k = 0; k < chars && r.Position + 2 <= start + length; k++)
                        {
                            char ch = (char)r.U16();
                            if (ch != 0)
                                sb.Append(ch);
                        }
                        if (sb.Length > 0)
                            layer.Name = sb.ToString();
                    }
                    else if ((key == "lsct" || key == "lsdk") && length >= 4)
                        divider = (int)r.U32();
                    r.Position = start + length;
                }
                r.Position = extraEnd;
                records.Add((layer, channels, divider));
            }

            // Channel image data, in record order.
            foreach (var rec in records)
            {
                var layer = rec.layer;
                int w = layer.Rect.width, h = layer.Rect.height;
                bool hasPixels = rec.divider == 0 && w > 0 && h > 0;
                byte[] red = null, green = null, blue = null, alpha = null;
                foreach (var (id, length) in rec.channels)
                {
                    long end = r.Position + length;
                    if (hasPixels && length >= 2 && id >= -1 && id <= 2)
                    {
                        int compression = r.U16();
                        byte[] plane = null;
                        if (compression == 0)
                            plane = r.Bytes(w * h);
                        else if (compression == 1)
                            plane = PackBits(r, w, h, big);
                        else
                            layer.Problem = "ZIP-compressed layer data is not supported.";
                        if (mode == 1 && id == 0)
                            red = green = blue = plane;
                        else if (id == 0) red = plane;
                        else if (id == 1) green = plane;
                        else if (id == 2) blue = plane;
                        else alpha = plane;
                    }
                    r.Position = end;
                }
                if (!hasPixels || red == null || green == null || blue == null)
                    continue;
                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    int src = y * w;
                    int dst = (h - 1 - y) * w; // flip: Unity textures start at the bottom
                    for (int x = 0; x < w; x++)
                    {
                        byte a = alpha != null ? alpha[src + x] : (byte)255;
                        if (layer.Opacity < 255)
                            a = (byte)(a * layer.Opacity / 255);
                        pixels[dst + x] = new Color32(red[src + x], green[src + x], blue[src + x], a);
                    }
                }
                layer.Pixels = pixels;
            }

            // Groups: a bounding divider (3) opens a group below its children; the folder record (1, 2) closes it.
            var open = new Stack<int>();
            foreach (var rec in records)
            {
                int index = doc.Layers.Count;
                var layer = rec.layer;
                layer.Parent = open.Count > 0 ? open.Peek() : -1;
                if (rec.divider == 3)
                {
                    // Placeholder until the folder record names it.
                    layer.IsGroup = true;
                    doc.Layers.Add(layer);
                    open.Push(index);
                    continue;
                }
                if ((rec.divider == 1 || rec.divider == 2) && open.Count > 0)
                {
                    int g = open.Pop();
                    var group = doc.Layers[g];
                    group.Name = layer.Name;
                    group.Visible = layer.Visible;
                    group.Opacity = layer.Opacity;
                    continue;
                }
                doc.Layers.Add(layer);
            }
            return doc;
        }

        static string Latin1(byte[] bytes)
        {
            var chars = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
                chars[i] = (char)bytes[i];
            return new string(chars);
        }

        static bool LongKey(string key)
            => key == "LMsk" || key == "Lr16" || key == "Lr32" || key == "Layr" || key == "Mt16" || key == "Mt32"
               || key == "Mtrn" || key == "Alph" || key == "FMsk" || key == "lnk2" || key == "FEid" || key == "FXid" || key == "PxSD";

        static byte[] PackBits(Reader r, int w, int h, bool big)
        {
            var rowLengths = new int[h];
            for (int y = 0; y < h; y++)
                rowLengths[y] = big ? (int)r.U32() : r.U16();
            var plane = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                long rowEnd = r.Position + rowLengths[y];
                int x = 0;
                while (r.Position < rowEnd && x < w)
                {
                    int n = (sbyte)r.U8();
                    if (n >= 0)
                    {
                        for (int k = 0; k <= n && r.Position < rowEnd; k++)
                        {
                            byte v = r.U8();
                            if (x < w)
                                plane[y * w + x++] = v;
                        }
                    }
                    else if (n != -128)
                    {
                        byte v = r.U8();
                        for (int k = 0; k < 1 - n && x < w; k++)
                            plane[y * w + x++] = v;
                    }
                }
                r.Position = rowEnd;
            }
            return plane;
        }

        sealed class Reader
        {
            readonly byte[] _d;
            public long Position;

            public Reader(byte[] d) => _d = d ?? throw new FormatException("No data.");

            void Need(long n)
            {
                if (Position + n > _d.Length || n < 0)
                    throw new FormatException("The file ends too early (truncated or unsupported).");
            }

            public byte U8()
            {
                Need(1);
                return _d[Position++];
            }

            public int U16()
            {
                Need(2);
                int v = (_d[Position] << 8) | _d[Position + 1];
                Position += 2;
                return v;
            }

            public uint U32()
            {
                Need(4);
                uint v = ((uint)_d[Position] << 24) | ((uint)_d[Position + 1] << 16) | ((uint)_d[Position + 2] << 8) | _d[Position + 3];
                Position += 4;
                return v;
            }

            public ulong U64() => ((ulong)U32() << 32) | U32();

            public void Skip(int n)
            {
                Need(n);
                Position += n;
            }

            public byte[] Bytes(int n)
            {
                Need(n);
                var b = new byte[n];
                Array.Copy(_d, Position, b, 0, n);
                Position += n;
                return b;
            }

            public string Ascii(int n) => Encoding.ASCII.GetString(Bytes(n));
        }
    }
}

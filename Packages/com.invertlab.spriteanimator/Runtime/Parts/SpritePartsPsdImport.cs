using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    public enum SpritePsdGroupMode : byte
    {
        /// <summary>Each layer group becomes a bone; its layers are parts under it.</summary>
        Bones = 0,
        /// <summary>Groups are ignored: every layer is a part at the root.</summary>
        Flatten = 1,
        /// <summary>Each top-level group is merged into one part (fewer parts for big files).</summary>
        MergeGroups = 2,
    }

    public enum SpritePsdOrigin : byte
    {
        /// <summary>The character root sits at the bottom centre of the canvas (feet).</summary>
        BottomCenter = 0,
        Center = 1,
    }

    /// <summary>
    /// Photoshop layers to Parts (AnyPortrait-style PSD import): every layer becomes a part placed where it sits on
    /// the canvas, stacked in layer order, with its pixels trimmed and packed into one sheet (Cropped cells).
    /// Groups become bones, are flattened, or are merged. Hidden layers import switched off; a clipping mask
    /// becomes Clip To the layer below. Re-importing the same file keeps each layer's sheet cell and, by name,
    /// updates the art of parts that already exist instead of adding new ones.
    /// </summary>
    public static class SpritePartsPsdImport
    {
        public sealed class Options
        {
            public float PixelsPerUnit = 100f;
            /// <summary>Texture scale (1, 0.5, 0.25). World size does not change.</summary>
            public float Scale = 1f;
            public SpritePsdOrigin Origin = SpritePsdOrigin.BottomCenter;
            public SpritePsdGroupMode Groups = SpritePsdGroupMode.Bones;
            public bool IncludeHidden = true;
            /// <summary>Parts with a layer's name get its new art (their pose stays).</summary>
            public bool UpdateExisting = true;
            public int Padding = 2;
            public int MaxAtlas = 8192;
        }

        public sealed class Item
        {
            public string Name;
            public bool IsBone;
            /// <summary>Item index of the bone above it, or -1 (root).</summary>
            public int Parent = -1;
            public Color32[] Pixels;
            public int Width;
            public int Height;
            /// <summary>Centre in character-root space (world units, y up).</summary>
            public float2 Root;
            public bool Visible = true;
            /// <summary>Item index of the layer it is clipped to, or -1.</summary>
            public int ClipBase = -1;
            /// <summary>Where the pixels sit in the atlas (bottom-left origin).</summary>
            public RectInt Packed;
        }

        public sealed class Plan
        {
            /// <summary>Parents before children, and bottom layers before top ones.</summary>
            public List<Item> Items = new List<Item>();
            public int AtlasWidth;
            public int AtlasHeight;
            public string Error;
            public List<string> Warnings = new List<string>();
        }

        public struct ApplyResult
        {
            public bool Ok;
            public string Reason;
            public int SheetIndex;
            public int Created;
            public int Updated;
        }

        // ------------------------------------------------------------------ plan

        public static Plan BuildPlan(SpritePsdReader.Document doc, Options o)
        {
            var plan = new Plan();
            if (doc == null || doc.Layers.Count == 0)
            {
                plan.Error = "The file has no layers (a flat image). Save it with layers.";
                return plan;
            }
            float ppu = math.max(0.01f, o.PixelsPerUnit);
            float scale = math.clamp(o.Scale, 0.05f, 1f);
            float2 origin = o.Origin == SpritePsdOrigin.Center
                ? new float2(doc.Width * 0.5f, doc.Height * 0.5f)
                : new float2(doc.Width * 0.5f, doc.Height);

            var layers = doc.Layers;
            // Hidden = switched off itself or inside a switched-off group.
            var hidden = new bool[layers.Count];
            for (int i = 0; i < layers.Count; i++)
            {
                hidden[i] = !layers[i].Visible;
                for (int p = layers[i].Parent, guard = 0; p >= 0 && !hidden[i] && guard < 256; p = layers[p].Parent, guard++)
                    hidden[i] = !layers[p].Visible;
            }

            var itemOf = new Dictionary<int, int>(); // layer index -> item index
            int TopGroup(int i)
            {
                int top = -1;
                for (int p = layers[i].Parent; p >= 0; p = layers[p].Parent)
                    top = p;
                return top;
            }

            if (o.Groups == SpritePsdGroupMode.Bones)
            {
                // Bones first (parents before children), placed later at the centre of what they hold.
                for (int i = 0; i < layers.Count; i++)
                {
                    if (!layers[i].IsGroup || (hidden[i] && !o.IncludeHidden))
                        continue;
                    itemOf[i] = -1; // reserve; created in depth order below
                }
                var groups = new List<int>(itemOf.Keys);
                groups.Sort((a, b) => Depth(layers, a).CompareTo(Depth(layers, b)));
                foreach (int g in groups)
                {
                    int parent = layers[g].Parent >= 0 && itemOf.TryGetValue(layers[g].Parent, out int pi) ? pi : -1;
                    itemOf[g] = plan.Items.Count;
                    plan.Items.Add(new Item { Name = Clean(layers[g].Name, "Group"), IsBone = true, Parent = parent, Visible = !hidden[g] });
                }
            }

            // Merge mode: one composite per top-level group, in canvas space.
            var merged = new Dictionary<int, (Color32[] px, SpritePsdReader.Layer clipBase)>();
            var lastBase = new Dictionary<int, int>(); // parent -> last unclipped layer (for clipping)
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer.IsGroup)
                    continue;
                if (layer.Problem != null)
                    plan.Warnings.Add("'" + layer.Name + "': " + layer.Problem);
                if (layer.Pixels == null || (hidden[i] && !o.IncludeHidden))
                    continue;

                if (o.Groups == SpritePsdGroupMode.MergeGroups)
                {
                    int top = TopGroup(i);
                    if (top >= 0)
                    {
                        if (hidden[i])
                            continue; // hidden layers do not show in a merged picture
                        merged.TryGetValue(top, out var m);
                        if (m.px == null)
                            m = (new Color32[doc.Width * doc.Height], null);
                        CompositeOnto(m.px, doc.Width, doc.Height, layer, layer.Clipped ? m.clipBase : null);
                        if (!layer.Clipped)
                            m.clipBase = layer;
                        merged[top] = m;
                        if (!itemOf.ContainsKey(top))
                        {
                            itemOf[top] = plan.Items.Count;
                            plan.Items.Add(new Item { Name = Clean(layers[top].Name, "Group"), Visible = !hidden[top] });
                        }
                        continue;
                    }
                }

                int parentItem = -1;
                if (o.Groups == SpritePsdGroupMode.Bones && layer.Parent >= 0 && itemOf.TryGetValue(layer.Parent, out int bi))
                    parentItem = bi;
                var item = new Item
                {
                    Name = Clean(layer.Name, "Layer"),
                    Parent = parentItem,
                    Visible = !hidden[i],
                };
                if (!Trim(layer.Pixels, layer.Rect, out item.Pixels, out var rect))
                    continue; // fully transparent
                item.Width = rect.width;
                item.Height = rect.height;
                item.Root = ToRoot(rect, origin, ppu);
                int key = o.Groups == SpritePsdGroupMode.Flatten ? -1 : layer.Parent;
                if (layer.Clipped && lastBase.TryGetValue(key, out int baseItem))
                    item.ClipBase = baseItem;
                itemOf[i] = plan.Items.Count;
                plan.Items.Add(item);
                if (!layer.Clipped)
                    lastBase[key] = itemOf[i];
            }

            foreach (var kv in merged)
            {
                var item = plan.Items[itemOf[kv.Key]];
                if (!Trim(kv.Value.px, new RectInt(0, 0, doc.Width, doc.Height), out item.Pixels, out var rect))
                {
                    item.Pixels = null;
                    continue;
                }
                item.Width = rect.width;
                item.Height = rect.height;
                item.Root = ToRoot(rect, origin, ppu);
            }
            RemoveEmpty(plan);
            UniqueNames(plan);

            // Bones sit at the centre of everything under them.
            for (int b = plan.Items.Count - 1; b >= 0; b--)
            {
                var bone = plan.Items[b];
                if (!bone.IsBone)
                    continue;
                float2 min = new float2(float.MaxValue), max = new float2(float.MinValue);
                bool any = false;
                for (int k = 0; k < plan.Items.Count; k++)
                {
                    if (k == b || !IsUnder(plan, k, b))
                        continue;
                    var it = plan.Items[k];
                    float2 half = it.IsBone ? float2.zero : new float2(it.Width, it.Height) * 0.5f / ppu;
                    min = math.min(min, it.Root - half);
                    max = math.max(max, it.Root + half);
                    any = true;
                }
                bone.Root = any ? (min + max) * 0.5f : float2.zero;
            }

            if (scale < 0.999f)
            {
                foreach (var it in plan.Items)
                {
                    if (it.Pixels == null)
                        continue;
                    int w = math.max(1, (int)math.round(it.Width * scale));
                    int h = math.max(1, (int)math.round(it.Height * scale));
                    it.Pixels = Resample(it.Pixels, it.Width, it.Height, w, h);
                    it.Width = w;
                    it.Height = h;
                }
            }
            Pack(plan, o);
            return plan;
        }

        static int Depth(List<SpritePsdReader.Layer> layers, int i)
        {
            int d = 0;
            for (int p = layers[i].Parent; p >= 0 && d < 256; p = layers[p].Parent)
                d++;
            return d;
        }

        static bool IsUnder(Plan plan, int item, int bone)
        {
            for (int p = plan.Items[item].Parent, guard = 0; p >= 0 && guard < 256; p = plan.Items[p].Parent, guard++)
            {
                if (p == bone)
                    return true;
            }
            return false;
        }

        /// <summary>Drops parts with no pixels (an empty merged group) and re-points parents and clip bases.</summary>
        static void RemoveEmpty(Plan plan)
        {
            var map = new int[plan.Items.Count];
            var kept = new List<Item>(plan.Items.Count);
            for (int i = 0; i < plan.Items.Count; i++)
            {
                var it = plan.Items[i];
                map[i] = !it.IsBone && it.Pixels == null ? -1 : kept.Count;
                if (map[i] >= 0)
                    kept.Add(it);
            }
            foreach (var it in kept)
            {
                it.Parent = it.Parent >= 0 ? map[it.Parent] : -1;
                it.ClipBase = it.ClipBase >= 0 ? map[it.ClipBase] : -1;
            }
            plan.Items = kept;
        }

        /// <summary>Layers that share a name get " 2", " 3"... so each keeps its own part and sheet cell.</summary>
        static void UniqueNames(Plan plan)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var it in plan.Items)
            {
                string key = (it.IsBone ? "bone:" : "part:") + it.Name;
                if (!seen.TryGetValue(key, out int n))
                {
                    seen[key] = 1;
                    continue;
                }
                string name;
                do
                {
                    n++;
                    name = it.Name + " " + n;
                } while (seen.ContainsKey((it.IsBone ? "bone:" : "part:") + name));
                seen[key] = n;
                seen[(it.IsBone ? "bone:" : "part:") + name] = 1;
                it.Name = name;
            }
        }

        static string Clean(string name, string fallback)
        {
            name = (name ?? string.Empty).Trim();
            return name.Length == 0 ? fallback : name;
        }

        static float2 ToRoot(RectInt canvasRect, float2 origin, float ppu)
        {
            float cx = canvasRect.x + canvasRect.width * 0.5f;
            float cy = canvasRect.y + canvasRect.height * 0.5f;
            return new float2(cx - origin.x, origin.y - cy) / ppu;
        }

        /// <summary>Tight opaque bounds. <paramref name="rect"/> is in canvas pixels (top-left origin); the pixels are bottom-left origin.</summary>
        static bool Trim(Color32[] px, RectInt rect, out Color32[] trimmed, out RectInt trimmedRect)
        {
            trimmed = null;
            trimmedRect = default;
            int w = rect.width, h = rect.height;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (px[y * w + x].a == 0)
                        continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0)
                return false;
            int tw = maxX - minX + 1, th = maxY - minY + 1;
            trimmed = new Color32[tw * th];
            for (int y = 0; y < th; y++)
                Array.Copy(px, (minY + y) * w + minX, trimmed, y * tw, tw);
            // Pixel rows run bottom-up; canvas rows run top-down.
            int top = rect.y + (h - 1 - maxY);
            trimmedRect = new RectInt(rect.x + minX, top, tw, th);
            return true;
        }

        /// <summary>Normal blend of <paramref name="layer"/> onto a canvas buffer; clipped layers keep only where the base shows.</summary>
        static void CompositeOnto(Color32[] canvas, int cw, int ch, SpritePsdReader.Layer layer, SpritePsdReader.Layer clipBase)
        {
            int w = layer.Rect.width, h = layer.Rect.height;
            for (int y = 0; y < h; y++)
            {
                int canvasRowFromTop = layer.Rect.y + (h - 1 - y);
                int cy = ch - 1 - canvasRowFromTop;
                if (cy < 0 || cy >= ch)
                    continue;
                for (int x = 0; x < w; x++)
                {
                    int cx = layer.Rect.x + x;
                    if (cx < 0 || cx >= cw)
                        continue;
                    var s = layer.Pixels[y * w + x];
                    float sa = s.a / 255f;
                    if (clipBase != null)
                        sa *= AlphaAt(clipBase, cx, canvasRowFromTop) / 255f;
                    if (sa <= 0f)
                        continue;
                    var d = canvas[cy * cw + cx];
                    float da = d.a / 255f;
                    float oa = sa + da * (1f - sa);
                    Color32 o = default;
                    o.r = (byte)math.round((s.r * sa + d.r * da * (1f - sa)) / oa);
                    o.g = (byte)math.round((s.g * sa + d.g * da * (1f - sa)) / oa);
                    o.b = (byte)math.round((s.b * sa + d.b * da * (1f - sa)) / oa);
                    o.a = (byte)math.round(oa * 255f);
                    canvas[cy * cw + cx] = o;
                }
            }
        }

        /// <summary>A layer's alpha at a canvas pixel (column, row from the top); 0 outside it.</summary>
        static byte AlphaAt(SpritePsdReader.Layer layer, int cx, int rowFromTop)
        {
            int x = cx - layer.Rect.x, row = rowFromTop - layer.Rect.y;
            if (layer.Pixels == null || x < 0 || row < 0 || x >= layer.Rect.width || row >= layer.Rect.height)
                return 0;
            return layer.Pixels[(layer.Rect.height - 1 - row) * layer.Rect.width + x].a;
        }

        static Color32[] Resample(Color32[] src, int sw, int sh, int dw, int dh)
        {
            var dst = new Color32[dw * dh];
            for (int y = 0; y < dh; y++)
            {
                float fy = (y + 0.5f) * sh / dh - 0.5f;
                int y0 = math.clamp((int)math.floor(fy), 0, sh - 1), y1 = math.min(y0 + 1, sh - 1);
                float ty = math.saturate(fy - y0);
                for (int x = 0; x < dw; x++)
                {
                    float fx = (x + 0.5f) * sw / dw - 0.5f;
                    int x0 = math.clamp((int)math.floor(fx), 0, sw - 1), x1 = math.min(x0 + 1, sw - 1);
                    float tx = math.saturate(fx - x0);
                    // Premultiplied so transparent edges do not darken.
                    float4 a = Pre(src[y0 * sw + x0]), b = Pre(src[y0 * sw + x1]), c = Pre(src[y1 * sw + x0]), d = Pre(src[y1 * sw + x1]);
                    float4 v = math.lerp(math.lerp(a, b, tx), math.lerp(c, d, tx), ty);
                    float3 rgb = v.w > 1e-5f ? v.xyz / v.w : float3.zero;
                    dst[y * dw + x] = new Color32((byte)math.round(rgb.x * 255f), (byte)math.round(rgb.y * 255f),
                        (byte)math.round(rgb.z * 255f), (byte)math.round(v.w * 255f));
                }
            }
            return dst;
        }

        static float4 Pre(Color32 c)
        {
            float a = c.a / 255f;
            return new float4(c.r / 255f * a, c.g / 255f * a, c.b / 255f * a, a);
        }

        /// <summary>
        /// Shelf packing, tallest first. Every power-of-two width that fits the widest layer is tried; the
        /// squarest sheet wins (then the smallest).
        /// </summary>
        static void Pack(Plan plan, Options o)
        {
            int pad = math.max(0, o.Padding);
            var order = new List<Item>();
            int widest = 1;
            foreach (var it in plan.Items)
            {
                if (it.IsBone || it.Pixels == null)
                    continue;
                order.Add(it);
                widest = math.max(widest, it.Width + pad * 2);
            }
            if (order.Count == 0)
            {
                plan.Error = "No visible pixels in any layer.";
                return;
            }
            order.Sort((a, b) => b.Height.CompareTo(a.Height));
            int best = -1, bestSide = int.MaxValue;
            long bestArea = long.MaxValue;
            for (int width = 64; width <= o.MaxAtlas; width *= 2)
            {
                if (width < widest)
                    continue;
                int height = Shelves(order, width, pad, false);
                int side = math.max(width, height);
                long sheetArea = (long)width * height;
                if (height <= o.MaxAtlas && (side < bestSide || (side == bestSide && sheetArea < bestArea)))
                {
                    best = width;
                    bestSide = side;
                    bestArea = sheetArea;
                }
            }
            if (best < 0)
            {
                plan.Error = "The layers do not fit a " + o.MaxAtlas + " px sheet. Lower the Texture Scale or merge groups.";
                return;
            }
            plan.AtlasWidth = best;
            plan.AtlasHeight = Shelves(order, best, pad, true);
        }

        /// <summary>Shelf layout at <paramref name="width"/>; returns the height. <paramref name="place"/> writes Packed.</summary>
        static int Shelves(List<Item> order, int width, int pad, bool place)
        {
            int x = pad, y = pad, shelf = 0;
            foreach (var it in order)
            {
                if (x + it.Width + pad > width)
                {
                    x = pad;
                    y += shelf + pad;
                    shelf = 0;
                }
                if (place)
                    it.Packed = new RectInt(x, y, it.Width, it.Height);
                x += it.Width + pad;
                shelf = math.max(shelf, it.Height);
            }
            return y + shelf + pad;
        }

        /// <summary>The packed sheet's pixels (bottom-left origin, transparent background).</summary>
        public static Color32[] AtlasPixels(Plan plan)
        {
            var atlas = new Color32[plan.AtlasWidth * plan.AtlasHeight];
            foreach (var it in plan.Items)
            {
                if (it.IsBone || it.Pixels == null)
                    continue;
                for (int y = 0; y < it.Height; y++)
                    Array.Copy(it.Pixels, y * it.Width, atlas, (it.Packed.y + y) * plan.AtlasWidth + it.Packed.x, it.Width);
            }
            return atlas;
        }

        // ------------------------------------------------------------------ apply

        /// <summary>How many new parts the plan would add to <paramref name="profile"/>.</summary>
        public static int CountNewParts(SpriteSheetProfile profile, Plan plan, Options o)
        {
            int n = 0;
            foreach (var it in plan.Items)
            {
                if (!o.UpdateExisting || FindByName(profile, it.Name, it.IsBone) == null)
                    n++;
            }
            return n;
        }

        static SpritePartSlotDef FindByName(SpriteSheetProfile profile, string name, bool bone)
        {
            if (profile?.PartsSlots == null)
                return null;
            foreach (var s in profile.PartsSlots)
            {
                if (s != null && s.IsBone == bone && string.Equals(s.Name, name, StringComparison.Ordinal))
                    return s;
            }
            return null;
        }

        /// <summary>
        /// Adds the sheet (or refreshes the one from an earlier import of <paramref name="fileName"/>), one appearance
        /// per layer, and the parts. <paramref name="trace"/> makes a mesh for layers that others are clipped to
        /// (pixels, width, height -> mesh), so the clip follows their shape; null skips it.
        /// </summary>
        public static ApplyResult Apply(SpriteSheetProfile profile, Plan plan, string fileName, Texture2D atlas, Options o,
            Func<Color32[], int, int, SpritePartMeshDef> trace = null)
        {
            if (profile == null || plan == null || plan.Error != null)
                return new ApplyResult { Reason = plan?.Error ?? "Nothing to import." };
            profile.EnsureSheets();
            profile.EnsurePartsRig();
            int newParts = CountNewParts(profile, plan, o);
            if (profile.PartsSlots.Count + newParts > SpritePartIdUtility.MaxParts)
                return new ApplyResult
                {
                    Reason = "This adds " + newParts + " parts; the rig can hold " + (SpritePartIdUtility.MaxParts - profile.PartsSlots.Count)
                             + " more (" + SpritePartIdUtility.MaxParts + " max). Merge groups, leave hidden layers out, or update existing parts.",
                };

            string sheetName = string.IsNullOrWhiteSpace(fileName) ? "PSD" : fileName;
            int sheetIndex = -1;
            for (int i = 0; i < profile.Sheets.Count; i++)
            {
                var s = profile.Sheets[i];
                if (s != null && s.Name == sheetName && (s.Texture == atlas || s.Texture == null))
                    sheetIndex = i;
            }
            if (sheetIndex < 0)
            {
                profile.Sheets.Add(new SpriteSheetDef { Name = sheetName });
                sheetIndex = profile.Sheets.Count - 1;
            }
            var sheet = profile.Sheets[sheetIndex];
            sheet.Texture = atlas;
            sheet.PixelsPerUnit = math.max(0.01f, o.PixelsPerUnit) * math.clamp(o.Scale, 0.05f, 1f);
            sheet.Pivot = new Vector2(0.5f, 0.5f);
            sheet.CellLayoutMode = SpriteSheetCellLayoutMode.Cropped;

            // Stable cells: a layer keeps the cell of the appearance that already carries its name on this sheet.
            var cellOf = new Dictionary<string, int>(StringComparer.Ordinal);
            int nextCell = 0;
            foreach (var a in profile.PartsAppearances)
            {
                if (a == null || a.SheetIndex != sheetIndex)
                    continue;
                cellOf[a.Name] = a.CellIndex;
                nextCell = math.max(nextCell, a.CellIndex + 1);
            }
            var appearanceOf = new Dictionary<Item, string>();
            foreach (var it in plan.Items)
            {
                if (it.IsBone || it.Pixels == null)
                    continue;
                string appName = sheetName + "/" + it.Name;
                if (!cellOf.TryGetValue(appName, out int cell))
                {
                    cell = nextCell++;
                    cellOf[appName] = cell;
                }
                var app = profile.PartsAppearances.Find(a => a != null && a.SheetIndex == sheetIndex && a.CellIndex == cell);
                if (app == null)
                {
                    string baseId = SpritePartIdUtility.Canonical("psd." + sheetName + "." + it.Name, "psd.layer");
                    string id = baseId;
                    for (int n = 2; SpritePartsAuthoringOps.FindAppearance(profile, id) != null; n++)
                        id = baseId + "." + n;
                    app = new SpritePartAppearanceDef
                    {
                        Name = appName, AppearanceId = id, SheetIndex = sheetIndex, CellIndex = cell,
                        LogicalWorldSize = Vector2.zero, PivotSource = SpritePartPivotSource.SheetDefault,
                    };
                    profile.PartsAppearances.Add(app);
                }
                app.Name = appName;
                appearanceOf[it] = app.AppearanceId;
            }
            var rects = new RectInt[math.max(1, nextCell)];
            for (int i = 0; i < rects.Length; i++)
                rects[i] = new RectInt(0, 0, 1, 1); // a layer that is gone keeps an empty cell
            foreach (var it in plan.Items)
            {
                if (!it.IsBone && it.Pixels != null)
                    rects[cellOf[sheetName + "/" + it.Name]] = it.Packed;
            }
            sheet.Columns = rects.Length;
            sheet.Rows = 1;
            sheet.CroppedCellRects = rects;

            // Parts, parents first. New parts stack above everything already in the rig, bottom layer lowest.
            int rank = 0;
            foreach (var s in profile.PartsSlots)
                if (s != null) rank = math.max(rank, s.DrawRank + 1);
            var slotOf = new Dictionary<int, SpritePartSlotDef>();
            var result = new ApplyResult { Ok = true, SheetIndex = sheetIndex };
            for (int k = 0; k < plan.Items.Count; k++)
            {
                var it = plan.Items[k];
                var slot = o.UpdateExisting ? FindByName(profile, it.Name, it.IsBone) : null;
                if (slot != null)
                {
                    if (!it.IsBone)
                        slot.DefaultAppearanceId = appearanceOf[it];
                    slotOf[k] = slot;
                    result.Updated++;
                    continue;
                }
                string parentId = it.Parent >= 0 && slotOf.TryGetValue(it.Parent, out var parent) ? parent.SlotId : string.Empty;
                var added = it.IsBone
                    ? SpritePartsAuthoringOps.TryAddBone(profile, parentId, out slot)
                    : SpritePartsAuthoringOps.TryAddPart(profile, parentId, out slot);
                if (slot == null)
                    return new ApplyResult { Reason = added.Reason, SheetIndex = sheetIndex, Created = result.Created, Updated = result.Updated };
                if (!SpritePartsAuthoringOps.TryRenameDisplayName(profile, slot.SlotId, it.Name).Ok)
                    SpritePartsAuthoringOps.TryRenameDisplayName(profile, slot.SlotId,
                        SpritePartsAuthoringOps.UniqueSiblingDisplayName(profile, slot.ParentSlotId, it.Name));
                slot.Enabled = it.Visible;
                slot.DrawRank = rank++;
                if (it.IsBone)
                    slot.BoneLength = 0.5f;
                else
                    slot.DefaultAppearanceId = appearanceOf[it];
                // Rest position: the layer's spot, in its parent's space.
                float4x4 parentRoot = string.IsNullOrEmpty(parentId) ? float4x4.identity : RestToRoot(profile, parentId);
                float2 local = math.mul(math.inverse(parentRoot), new float4(it.Root, 0f, 1f)).xy;
                slot.RestPosition = new Vector2(local.x, local.y);
                slotOf[k] = slot;
                result.Created++;
            }

            // Photoshop clipping masks -> Clip To, and a traced mesh on the base so the clip follows its shape.
            for (int k = 0; k < plan.Items.Count; k++)
            {
                var it = plan.Items[k];
                if (it.ClipBase < 0 || !slotOf.TryGetValue(k, out var slot) || !slotOf.TryGetValue(it.ClipBase, out var baseSlot))
                    continue;
                slot.ClipMaskSlotId = SpritePartIdUtility.Canonical(baseSlot.SlotId);
                var baseItem = plan.Items[it.ClipBase];
                if (trace != null && (baseSlot.Mesh == null || !baseSlot.Mesh.HasMesh))
                {
                    var mesh = trace(baseItem.Pixels, baseItem.Width, baseItem.Height);
                    if (mesh != null && mesh.HasMesh)
                        baseSlot.Mesh = mesh;
                }
            }
            SpritePartsValidation.CanonicalizeIds(profile);
            return result;
        }

        static float4x4 RestToRoot(SpriteSheetProfile profile, string slotId)
        {
            float4x4 m = float4x4.identity;
            var s = SpritePartsAuthoringOps.FindSlot(profile, slotId);
            for (int guard = 0; s != null && guard < 256; guard++)
            {
                m = math.mul(SpritePartsHierarchy.LocalMatrix(new float2(s.RestPosition.x, s.RestPosition.y), s.RestRotation,
                    new float2(s.RestScale.x, s.RestScale.y)), m);
                s = string.IsNullOrWhiteSpace(s.ParentSlotId) ? null : SpritePartsAuthoringOps.FindSlot(profile, s.ParentSlotId);
            }
            return m;
        }
    }
}

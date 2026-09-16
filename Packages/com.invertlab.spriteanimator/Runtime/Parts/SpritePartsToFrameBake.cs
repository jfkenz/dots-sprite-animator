using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Explicit Bake Parts Clip to Frame Clip: sample a Parts clip at a chosen
    /// FPS into a new frame clip (and sheet/cells) owned by the same profile.
    /// Does not change <see cref="SpriteAnimKind"/>. Not a profile mode switch.
    /// Events/sockets/hitboxes are not transferred.
    /// </summary>
    public static class SpritePartsToFrameBake
    {
        public const float DefaultFps = 12f;
        public const int MinCellSize = 8;
        public const int MaxCellSize = 512;

        public sealed class Plan
        {
            public bool Ok;
            public string Reason;
            public int SourceClipIndex = -1;
            public string SourceClipName;
            public float Duration;
            public float Fps = DefaultFps;
            public int FrameCount;
            public int CellWidth;
            public int CellHeight;
            public int Columns;
            public int Rows;
            public string DestinationClipName;
            public string DestinationSheetName;
            public readonly List<string> Unsupported = new List<string>();
            public readonly List<string> Notes = new List<string>();

            public string Summary =>
                Ok
                    ? $"Bake '{SourceClipName}' → frame clip '{DestinationClipName}' ({FrameCount} frame(s) at {Fps:0.#} fps)"
                    : (Reason ?? "Invalid plan.");
        }

        public struct ApplyResult
        {
            public bool Ok;
            public string Reason;
            public string Summary;
            public Texture2D Texture;
            public int SheetIndex;
            public int ClipIndex;
        }

        public static Plan PlanBake(
            SpriteSheetProfile profile,
            int partsClipIndex,
            float fps = DefaultFps,
            int cellSize = 0)
        {
            var plan = new Plan
            {
                SourceClipIndex = partsClipIndex,
                Fps = fps,
            };
            if (profile == null)
            {
                plan.Reason = "Profile is null.";
                return plan;
            }
            profile.EnsurePartsRig();
            if (profile.PartsClips == null || partsClipIndex < 0 ||
                partsClipIndex >= profile.PartsClips.Count ||
                profile.PartsClips[partsClipIndex] == null)
            {
                plan.Reason = "Source Parts clip is missing.";
                return plan;
            }

            var clip = profile.PartsClips[partsClipIndex];
            plan.SourceClipName = clip.Name;
            plan.Duration = Mathf.Max(1e-4f, clip.Duration);
            if (!(fps > 0f) || float.IsNaN(fps) || float.IsInfinity(fps))
            {
                plan.Reason = "FPS must be > 0.";
                return plan;
            }
            plan.Fps = fps;
            plan.FrameCount = Mathf.Max(1, Mathf.RoundToInt(plan.Duration * fps));

            plan.Unsupported.Add("Parts clip events are not transferred (Parts clips have no frame event markers).");
            plan.Unsupported.Add("Sockets and socket motion tracks are not sampled into the frame clip.");
            plan.Unsupported.Add("Hitboxes / timeline colliders are not transferred.");
            plan.Unsupported.Add("Onion offsets, facing groups, and combo windows are not generated.");
            plan.Notes.Add(
                "Export/bake only: this creates a new frame clip and sheet. Runtime AnimKind is not changed. Switch to Frames separately if you want the character to play the baked clip.");
            plan.Notes.Add(
                "Fidelity is sampled appearance composite at the chosen FPS. Full Parts pose/hierarchy is flattened per frame; it is not a live Parts player.");

            GridForFrames(plan.FrameCount, out plan.Columns, out plan.Rows);
            if (cellSize > 0)
            {
                plan.CellWidth = Mathf.Clamp(cellSize, MinCellSize, MaxCellSize);
                plan.CellHeight = plan.CellWidth;
            }
            else if (!TryMeasureCellSize(profile, partsClipIndex, plan.FrameCount, plan.Duration,
                         out plan.CellWidth, out plan.CellHeight, out string measureError))
            {
                plan.Reason = measureError;
                return plan;
            }

            UniqueNames(profile, clip.Name, out plan.DestinationClipName, out plan.DestinationSheetName);
            plan.Ok = true;
            return plan;
        }

        public static ApplyResult Apply(
            Plan plan,
            SpriteSheetProfile profile,
            SpriteProfileArtImport.ImportSourceInfo sourceInfo = default,
            string importedUtc = null)
        {
            if (plan == null || !plan.Ok)
                return Fail(plan?.Reason ?? "Plan is null.");
            if (profile == null)
                return Fail("Profile is null.");

            var replay = PlanBake(profile, plan.SourceClipIndex, plan.Fps,
                plan.CellWidth > 0 ? plan.CellWidth : 0);
            if (!replay.Ok)
                return Fail(replay.Reason);

            if (!TryBuildSheetPixels(profile, replay, out var pixels, out int texW, out int texH,
                    out string bakeError))
                return Fail(bakeError);

            var texture = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            texture.name = replay.DestinationSheetName;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            profile.EnsureSheets();
            profile.Clips ??= new List<SpriteClipDef>();
            int sheetMark = profile.Sheets.Count;
            int clipMark = profile.Clips.Count;
            try
            {
                var sheet = new SpriteSheetDef
                {
                    Name = replay.DestinationSheetName,
                    Texture = texture,
                    Columns = replay.Columns,
                    Rows = replay.Rows,
                    PixelsPerUnit = ResolvePpu(profile),
                    Pivot = new Vector2(0.5f, 0.5f),
                    CellLayoutMode = SpriteSheetCellLayoutMode.Grid,
                    Import = SpriteProfileArtImport.StampProvenance(
                        sourceInfo,
                        $"bake parts clip '{replay.SourceClipName}' sheet",
                        importedUtc),
                };
                profile.Sheets.Add(sheet);

                var frames = new int[replay.FrameCount];
                var frameRows = new int[replay.FrameCount];
                for (int i = 0; i < replay.FrameCount; i++)
                {
                    frames[i] = i % replay.Columns;
                    frameRows[i] = i / replay.Columns;
                }

                var clip = new SpriteClipDef
                {
                    Name = replay.DestinationClipName,
                    SheetIndex = profile.Sheets.Count - 1,
                    Row = 0,
                    Frames = frames,
                    FrameRows = frameRows,
                    FrameRate = replay.Fps,
                    WrapMode = profile.PartsClips[replay.SourceClipIndex].WrapMode,
                    Import = SpriteProfileArtImport.StampProvenance(
                        sourceInfo,
                        $"bake parts clip '{replay.SourceClipName}'",
                        importedUtc),
                };
                clip.EnsureFrameData();
                profile.Clips.Add(clip);
            }
            catch (Exception ex)
            {
                while (profile.Clips.Count > clipMark)
                    profile.Clips.RemoveAt(profile.Clips.Count - 1);
                while (profile.Sheets.Count > sheetMark)
                    profile.Sheets.RemoveAt(profile.Sheets.Count - 1);
                UnityEngine.Object.DestroyImmediate(texture);
                return Fail("Bake aborted, profile unchanged: " + ex.Message);
            }

            return new ApplyResult
            {
                Ok = true,
                Summary = replay.Summary,
                Texture = texture,
                SheetIndex = profile.Sheets.Count - 1,
                ClipIndex = profile.Clips.Count - 1,
            };
        }

        static ApplyResult Fail(string reason) =>
            new ApplyResult { Reason = reason, SheetIndex = -1, ClipIndex = -1 };

        static void GridForFrames(int frameCount, out int columns, out int rows)
        {
            columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(frameCount)));
            rows = Mathf.Max(1, Mathf.CeilToInt(frameCount / (float)columns));
        }

        static float ResolvePpu(SpriteSheetProfile profile)
        {
            if (profile.Sheets != null)
            {
                for (int i = 0; i < profile.Sheets.Count; i++)
                {
                    var sheet = profile.Sheets[i];
                    if (sheet != null && sheet.PixelsPerUnit > 0f)
                        return sheet.PixelsPerUnit;
                }
            }
            return SpriteSheetProfile.DefaultPixelsPerUnit;
        }

        static bool TryMeasureCellSize(
            SpriteSheetProfile profile, int clipIndex, int frameCount, float duration,
            out int cellW, out int cellH, out string error)
        {
            cellW = MinCellSize;
            cellH = MinCellSize;
            error = null;
            if (!TrySampleBounds(profile, clipIndex, frameCount, duration,
                    out float2 min, out float2 max, out error))
                return false;
            float ppu = ResolvePpu(profile);
            float2 size = math.max(max - min, new float2(1e-3f, 1e-3f));
            cellW = Mathf.Clamp(Mathf.CeilToInt(size.x * ppu) + 2, MinCellSize, MaxCellSize);
            cellH = Mathf.Clamp(Mathf.CeilToInt(size.y * ppu) + 2, MinCellSize, MaxCellSize);
            return true;
        }

        static bool TrySampleBounds(
            SpriteSheetProfile profile, int clipIndex, int frameCount, float duration,
            out float2 min, out float2 max, out string error)
        {
            min = new float2(float.PositiveInfinity, float.PositiveInfinity);
            max = new float2(float.NegativeInfinity, float.NegativeInfinity);
            error = null;
            if (!SpritePartsClipConversion.TryBuildBlob(profile, Allocator.Temp,
                    out var blob, out error))
                return false;
            try
            {
                ref var set = ref blob.Value;
                int slots = set.Slots.Length;
                var poses = new NativeArray<SpritePartsSampler.Pose>(slots, Allocator.Temp);
                var matrices = new NativeArray<float4x4>(slots, Allocator.Temp);
                try
                {
                    for (int f = 0; f < frameCount; f++)
                    {
                        float time = FrameTime(f, frameCount, duration);
                        SpritePartsSampler.SampleAll(ref set, clipIndex, time, poses);
                        SpritePartsHierarchy.ComposeLocalToRoot(ref set, poses, matrices);
                        AccrueBounds(profile, ref set, clipIndex, time, matrices, ref min, ref max);
                    }
                }
                finally
                {
                    poses.Dispose();
                    matrices.Dispose();
                }
            }
            finally
            {
                if (blob.IsCreated)
                    blob.Dispose();
            }

            if (!math.isfinite(min.x) || !math.isfinite(max.x))
            {
                min = new float2(-0.5f, -0.5f);
                max = new float2(0.5f, 0.5f);
            }
            return true;
        }

        static void AccrueBounds(
            SpriteSheetProfile profile,
            ref SpritePartsSetBlob set,
            int clipIndex,
            float time,
            NativeArray<float4x4> matrices,
            ref float2 min,
            ref float2 max)
        {
            for (int i = 0; i < set.Slots.Length; i++)
            {
                if (!TryResolveSlotAppearance(profile, ref set, clipIndex, i, time,
                        out var app, out var geo))
                    continue;
                ExpandQuad(matrices[i], geo, ref min, ref max);
            }
        }

        static void ExpandQuad(float4x4 m, SpritePartsGeometry.Resolved geo, ref float2 min, ref float2 max)
        {
            float2[] q =
            {
                new float2(-0.5f, -0.5f),
                new float2(0.5f, -0.5f),
                new float2(0.5f, 0.5f),
                new float2(-0.5f, 0.5f),
            };
            for (int i = 0; i < 4; i++)
            {
                float2 local = SpritePartsGeometry.VisualPoint(q[i], geo.Pivot, geo.LogicalWorldSize);
                float2 world = SpritePartsHierarchy.TransformPoint(m, local);
                min = math.min(min, world);
                max = math.max(max, world);
            }
        }

        static bool TryBuildSheetPixels(
            SpriteSheetProfile profile, Plan plan,
            out Color32[] pixels, out int texW, out int texH, out string error)
        {
            pixels = null;
            texW = plan.Columns * plan.CellWidth;
            texH = plan.Rows * plan.CellHeight;
            error = null;
            pixels = new Color32[texW * texH];

            if (!SpritePartsClipConversion.TryBuildBlob(profile, Allocator.Temp,
                    out var blob, out error))
                return false;

            var cellCache = new Dictionary<int, Color32[]>();
            try
            {
                ref var set = ref blob.Value;
                int slots = set.Slots.Length;
                var poses = new NativeArray<SpritePartsSampler.Pose>(slots, Allocator.Temp);
                var matrices = new NativeArray<float4x4>(slots, Allocator.Temp);
                var order = DrawOrder(ref set);
                try
                {
                    float2 min = new float2(float.PositiveInfinity, float.PositiveInfinity);
                    float2 max = new float2(float.NegativeInfinity, float.NegativeInfinity);
                    for (int f = 0; f < plan.FrameCount; f++)
                    {
                        float time = FrameTime(f, plan.FrameCount, plan.Duration);
                        SpritePartsSampler.SampleAll(ref set, plan.SourceClipIndex, time, poses);
                        SpritePartsHierarchy.ComposeLocalToRoot(ref set, poses, matrices);
                        AccrueBounds(profile, ref set, plan.SourceClipIndex, time, matrices, ref min, ref max);
                    }
                    for (int f = 0; f < plan.FrameCount; f++)
                    {
                        float time = FrameTime(f, plan.FrameCount, plan.Duration);
                        SpritePartsSampler.SampleAll(ref set, plan.SourceClipIndex, time, poses);
                        SpritePartsHierarchy.ComposeLocalToRoot(ref set, poses, matrices);
                        int col = f % plan.Columns;
                        int row = f / plan.Columns;
                        int originX = col * plan.CellWidth;
                        int originY = (plan.Rows - 1 - row) * plan.CellHeight;
                        CompositeCell(
                            profile, ref set, plan.SourceClipIndex, time, matrices, order,
                            pixels, texW, texH, originX, originY,
                            plan.CellWidth, plan.CellHeight, min, max, cellCache);
                    }
                }
                finally
                {
                    poses.Dispose();
                    matrices.Dispose();
                }
            }
            finally
            {
                if (blob.IsCreated)
                    blob.Dispose();
            }

            return true;
        }

        static int[] DrawOrder(ref SpritePartsSetBlob set)
        {
            int n = set.Slots.Length;
            var order = new int[n];
            var ranks = new int[n];
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
                ranks[i] = set.Slots[i].DrawRank;
            }
            Array.Sort(order, (a, b) => ranks[a].CompareTo(ranks[b]));
            return order;
        }

        static void CompositeCell(
            SpriteSheetProfile profile,
            ref SpritePartsSetBlob set,
            int clipIndex,
            float time,
            NativeArray<float4x4> matrices,
            int[] order,
            Color32[] dest,
            int destW,
            int destH,
            int originX,
            int originY,
            int cellW,
            int cellH,
            float2 boundsMin,
            float2 boundsMax,
            Dictionary<int, Color32[]> cellCache)
        {
            float2 size = math.max(boundsMax - boundsMin, new float2(1e-4f, 1e-4f));
            for (int o = 0; o < order.Length; o++)
            {
                int slot = order[o];
                if (!TryResolveSlotAppearance(profile, ref set, clipIndex, slot, time,
                        out var app, out var geo))
                    continue;
                if (!TryGetCellPixels(profile, app, geo, cellCache, out var srcPixels,
                        out int srcW, out int srcH, out int srcX, out int srcY, out int srcCW, out int srcCH))
                    continue;
                var inverse = math.inverse(matrices[slot]);
                for (int y = 0; y < cellH; y++)
                {
                    for (int x = 0; x < cellW; x++)
                    {
                        float u = (x + 0.5f) / cellW;
                        float v = (y + 0.5f) / cellH;
                        float2 world = boundsMin + new float2(u * size.x, v * size.y);
                        float2 local = SpritePartsHierarchy.TransformPoint(inverse, world);
                        float2 q = new float2(
                            local.x / math.max(1e-8f, geo.LogicalWorldSize.x) - 0.5f + geo.Pivot.x,
                            local.y / math.max(1e-8f, geo.LogicalWorldSize.y) - 0.5f + geo.Pivot.y);
                        if (q.x < -0.5f || q.x > 0.5f || q.y < -0.5f || q.y > 0.5f)
                            continue;
                        float su = q.x + 0.5f;
                        float sv = q.y + 0.5f;
                        int sx = srcX + Mathf.Clamp(Mathf.FloorToInt(su * srcCW), 0, srcCW - 1);
                        int sy = srcY + Mathf.Clamp(Mathf.FloorToInt(sv * srcCH), 0, srcCH - 1);
                        var src = srcPixels[sy * srcW + sx];
                        if (src.a == 0)
                            continue;
                        int dx = originX + x;
                        int dy = originY + y;
                        if (dx < 0 || dy < 0 || dx >= destW || dy >= destH)
                            continue;
                        dest[dy * destW + dx] = Blend(dest[dy * destW + dx], src);
                    }
                }
            }
        }

        static Color32 Blend(Color32 under, Color32 over)
        {
            if (over.a == 255 || under.a == 0)
                return over;
            float oa = over.a / 255f;
            float ua = under.a / 255f * (1f - oa);
            float a = oa + ua;
            if (a <= 1e-6f)
                return under;
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt((over.r * oa + under.r * ua) / a), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((over.g * oa + under.g * ua) / a), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((over.b * oa + under.b * ua) / a), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
        }

        static bool TryResolveSlotAppearance(
            SpriteSheetProfile profile,
            ref SpritePartsSetBlob set,
            int clipIndex,
            int slotIndex,
            float time,
            out SpritePartAppearanceDef app,
            out SpritePartsGeometry.Resolved geo)
        {
            app = null;
            geo = default;
            int appearanceIndex = SpritePartsSampler.SampleAppearanceIndex(
                ref set, clipIndex, slotIndex, time);
            if (appearanceIndex < 0)
                appearanceIndex = set.Slots[slotIndex].DefaultAppearanceIndex;
            if (appearanceIndex < 0 || appearanceIndex >= (profile.PartsAppearances?.Count ?? 0))
                return false;
            app = profile.PartsAppearances[appearanceIndex];
            return SpritePartsGeometry.TryResolve(profile, app, rotatedPacking: false, out geo, out _);
        }

        static bool TryGetCellPixels(
            SpriteSheetProfile profile,
            SpritePartAppearanceDef app,
            SpritePartsGeometry.Resolved geo,
            Dictionary<int, Color32[]> cache,
            out Color32[] pixels,
            out int width,
            out int height,
            out int cellX,
            out int cellY,
            out int cellW,
            out int cellH)
        {
            pixels = null;
            width = height = cellX = cellY = cellW = cellH = 0;
            var sheet = profile.SheetAt(geo.SheetIndex);
            if (sheet?.Texture == null)
                return false;
            var tex = sheet.Texture;
            width = tex.width;
            height = tex.height;
            int cacheKey = geo.SheetIndex;
            if (!cache.TryGetValue(cacheKey, out pixels))
            {
                try
                {
                    pixels = tex.GetPixels32();
                }
                catch
                {
                    return false;
                }
                cache[cacheKey] = pixels;
            }

            var uv = SpriteSheetProfile.GetCellUvRect(sheet, geo.CellIndex);
            cellX = Mathf.Clamp(Mathf.RoundToInt(uv.x * width), 0, width - 1);
            cellY = Mathf.Clamp(Mathf.RoundToInt(uv.y * height), 0, height - 1);
            cellW = Mathf.Clamp(Mathf.RoundToInt(uv.width * width), 1, width - cellX);
            cellH = Mathf.Clamp(Mathf.RoundToInt(uv.height * height), 1, height - cellY);
            return pixels != null && pixels.Length == width * height;
        }

        static float FrameTime(int frame, int frameCount, float duration)
        {
            if (frameCount <= 1)
                return 0f;
            return (frame / (float)frameCount) * duration;
        }

        static void UniqueNames(
            SpriteSheetProfile profile, string sourceName,
            out string clipName, out string sheetName)
        {
            string baseName = string.IsNullOrWhiteSpace(sourceName) ? "PartsBake" : sourceName.Trim() + " Frames";
            var takenClips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (profile.Clips != null)
            {
                for (int i = 0; i < profile.Clips.Count; i++)
                    if (profile.Clips[i] != null && !string.IsNullOrWhiteSpace(profile.Clips[i].Name))
                        takenClips.Add(profile.Clips[i].Name);
            }
            clipName = baseName;
            int n = 2;
            while (takenClips.Contains(clipName))
                clipName = baseName + " " + n++;

            var takenSheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (profile.Sheets != null)
            {
                for (int i = 0; i < profile.Sheets.Count; i++)
                    if (profile.Sheets[i] != null && !string.IsNullOrWhiteSpace(profile.Sheets[i].Name))
                        takenSheets.Add(profile.Sheets[i].Name);
            }
            sheetName = clipName + " Sheet";
            n = 2;
            while (takenSheets.Contains(sheetName))
                sheetName = clipName + " Sheet " + n++;
        }
    }
}

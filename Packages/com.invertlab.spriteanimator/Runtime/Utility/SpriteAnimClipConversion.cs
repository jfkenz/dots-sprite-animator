using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Shared clip conversion for baking and runtime crowd prototypes.</summary>
    public static class SpriteAnimClipConversion
    {
        public static SpriteAnimSetBuilder.ClipInput CreateInput(SpriteAnimSetAuthoring authoring, int i)
        {
            var profile = authoring.Profile != null ? authoring.Profile.Data : null;
            bool useProfile = profile?.Clips != null && profile.Clips.Count > 0;
            var profileClip = useProfile ? profile.Clips[i] : null;
            var authorClip = useProfile ? default : authoring.Clips[i];
            if (useProfile)
                profileClip.EnsureFrameData();
            var clipSheet = useProfile ? profile.SheetForClip(profileClip) : null;
            int cols = Mathf.Max(1, clipSheet != null ? clipSheet.Columns : authoring.Columns);
            int rows = Mathf.Max(1, clipSheet != null ? clipSheet.Rows : authoring.Rows);
            float bakePpu = clipSheet != null
                ? SpriteSheetProfile.GetPixelsPerUnit(clipSheet)
                : 1f;
            var frameCols = useProfile ? profileClip.Frames : authorClip.Frames;
            frameCols = frameCols != null && frameCols.Length > 0
                ? frameCols
                : new[] { 0, 1, 2, 3 };
            var frameScales = useProfile ? profileClip.FrameScales : authorClip.FrameScales;
            var frameRotations = useProfile ? profileClip.FrameRotations : authorClip.FrameRotations;
            var frameTweens = useProfile ? profileClip.FrameTweenModes : authorClip.FrameTweenModes;
            int row = useProfile ? profileClip.Row : authorClip.Row;
            int[] frameRows = useProfile ? profileClip.FrameRows : authorClip.FrameRows;
            var slots = new int[frameCols.Length];
            var frameOffsets = new float2[frameCols.Length];
            var clipScales = new float2[frameCols.Length];
            var clipRotations = new float[frameCols.Length];
            var clipTweens = new byte[frameCols.Length];
            for (int f = 0; f < frameCols.Length; f++)
            {
                SpriteClipDef.ResolveSheetCell(row, frameCols, frameRows, f,
                    cols, rows, out int cellRow, out int cellCol);
                slots[f] = cellRow * cols + cellCol;
                Vector2 offset = useProfile && profileClip.OnionOffsets != null && f < profileClip.OnionOffsets.Length
                    ? profileClip.OnionOffsets[f] / bakePpu
                    : !useProfile && authorClip.FrameOffsets != null && f < authorClip.FrameOffsets.Length
                        ? authorClip.FrameOffsets[f]
                        : Vector2.zero;
                frameOffsets[f] = new float2(offset.x, offset.y);
                // per-cell pivot override: shift the frame so the
                // cell pivot sits on the entity origin
                if (clipSheet != null &&
                    SpriteSheetProfile.TryGetCellPivot(clipSheet, slots[f],
                        out var cellPivot))
                {
                    var pivotTexture = clipSheet.Texture;
                    float cellWpx = pivotTexture != null
                        ? pivotTexture.width / (float)cols : 0f;
                    float cellHpx = pivotTexture != null
                        ? pivotTexture.height / (float)rows : 0f;
                    frameOffsets[f] += new float2(
                        (0.5f - Mathf.Clamp01(cellPivot.x)) * cellWpx /
                        Mathf.Max(0.01f, bakePpu),
                        (0.5f - Mathf.Clamp01(cellPivot.y)) * cellHpx /
                        Mathf.Max(0.01f, bakePpu));
                }
                Vector2 scale = frameScales != null && f < frameScales.Length
                    ? frameScales[f]
                    : Vector2.one;
                clipScales[f] = new float2(scale.x, scale.y);
                clipRotations[f] = frameRotations != null && f < frameRotations.Length
                    ? frameRotations[f]
                    : 0f;
                clipTweens[f] = frameTweens != null && f < frameTweens.Length
                    ? frameTweens[f]
                    : (byte)SpriteEaseMode.Linear;
            }



            int socketCount = useProfile
                ? profileClip.Sockets?.Count ?? 0
                : authorClip.Sockets?.Length ?? 0;
            var socketInputs = new SpriteAnimSetBuilder.ClipInput.FrameSocketInput[socketCount];
            for (int s = 0; s < socketInputs.Length; s++)
            {
                var socket = useProfile ? profileClip.Sockets[s] : authorClip.Sockets[s];
                float2 position = useProfile
                    ? new float2(
                        socket.LocalPosition.x / bakePpu,
                        socket.LocalPosition.y / bakePpu)
                    : new float2(socket.LocalPosition.x, socket.LocalPosition.y);
                string socketId = useProfile
                    ? profile.SocketCatalog.Find(socket.Name)?.SocketId
                    : socket.Name;
                socketInputs[s] = new SpriteAnimSetBuilder.ClipInput.FrameSocketInput
                {
                    FrameIndex = socket.FrameIndex,
                    LocalPosition = position,
                    LocalAngle = socket.LocalAngle,
                    LocalScale = new float2(
                        SpriteSocketKeys.ResolvedScale(socket.LocalScale).x,
                        SpriteSocketKeys.ResolvedScale(socket.LocalScale).y),
                    Name = socket.Name,
                    SocketId = socketId,
                };
            }



            return new SpriteAnimSetBuilder.ClipInput
            {
                Name = useProfile
                    ? (string.IsNullOrEmpty(profileClip.Name) ? ("clip" + i) : profileClip.Name)
                    : (string.IsNullOrEmpty(authorClip.Name) ? ("clip" + i) : authorClip.Name),
                Loop = useProfile
                    ? profileClip.WrapMode == SpriteAnimWrap.Loop || profileClip.WrapMode == SpriteAnimWrap.ReverseLoop
                    : authorClip.Loop || authorClip.WrapMode == SpriteAnimWrap.ReverseLoop,
                WrapMode = useProfile ? profileClip.WrapMode : authorClip.WrapMode,
                Interrupt = useProfile ? profileClip.Interrupt : authorClip.Interrupt,
                CancelAfter = useProfile ? profileClip.CancelAfter : authorClip.CancelAfter,
                Priority = useProfile ? profileClip.Priority : authorClip.Priority,
                OnCompleteClipIndex = useProfile
                    ? profileClip.OnCompleteClipIndex
                    : authorClip.OnCompleteClipIndex,
                ComboWindowStartFrame = useProfile
                    ? profileClip.ComboWindowStartFrame
                    : authorClip.ComboWindowStartFrame,
                ComboWindowEndFrame = useProfile
                    ? profileClip.ComboWindowEndFrame
                    : authorClip.ComboWindowEndFrame,
                ComboWindowPriorityBoost = useProfile
                    ? profileClip.ComboWindowPriorityBoost
                    : authorClip.ComboWindowPriorityBoost,
                FrameRate = Mathf.Max(0.1f, useProfile ? profileClip.FrameRate : authorClip.FrameRate),
                GlobalFrameIndices = slots,
                FrameDurationScales = useProfile ? profileClip.FrameDurationScales : authorClip.FrameDurationScales,
                EventIds = useProfile ? profileClip.EventIds : authorClip.EventIds,
                EventNormalizedTimes = useProfile
                    ? profileClip.EventNormalizedTimes
                    : authorClip.EventNormalizedTimes,
                EventKeys = useProfile ? EventKeysFromProfile(profileClip) : null,
                FrameOffsets = frameOffsets,
                FrameScales = clipScales,
                FrameRotations = clipRotations,
                FrameTweenModes = clipTweens,
                FacingGroup = useProfile ? profileClip.FacingGroup : authorClip.FacingGroup,
                FacingDirection = useProfile ? profileClip.Facing : authorClip.FacingDirection,
                FrameSockets = socketInputs,
            };
        }

        static SpriteAnimSetBuilder.ClipInput.EventKeyInput[] EventKeysFromProfile(SpriteClipDef clip)
        {
            if (clip == null)
                return null;
            clip.EnsureEventMarkers();
            if (clip.EventMarkers == null || clip.EventMarkers.Count == 0)
                return null;
            int count = 0;
            for (int i = 0; i < clip.EventMarkers.Count; i++)
            {
                if (clip.EventMarkers[i] != null && clip.EventMarkers[i].EventId != 0)
                    count++;
            }
            if (count == 0)
                return null;
            var keys = new SpriteAnimSetBuilder.ClipInput.EventKeyInput[count];
            int write = 0;
            for (int i = 0; i < clip.EventMarkers.Count; i++)
            {
                var marker = clip.EventMarkers[i];
                if (marker == null || marker.EventId == 0)
                    continue;
                marker.EnsurePayloads();
                keys[write++] = new SpriteAnimSetBuilder.ClipInput.EventKeyInput
                {
                    FrameIndex = marker.FrameIndex,
                    NormalizedTime = marker.NormalizedTime,
                    EventId = marker.EventId,
                    FireMode = marker.FireMode,
                    IntPayload = marker.IntPayload,
                    FloatPayload = marker.FloatPayload,
                    TextPayload = marker.TextPayload,
                    Payloads = PayloadsFromMarker(marker),
                };
            }
            return keys;
        }



        static SpriteAnimSetBuilder.ClipInput.EventPayloadInput[] PayloadsFromMarker(
            SpriteClipEventMarker marker)
        {
            if (marker?.Payloads == null || marker.Payloads.Count == 0)
                return null;
            int count = math.min(marker.Payloads.Count, SpriteEventPayloads.Max);
            var payloads = new SpriteAnimSetBuilder.ClipInput.EventPayloadInput[count];
            int write = 0;
            for (int i = 0; i < marker.Payloads.Count && write < count; i++)
            {
                var entry = marker.Payloads[i];
                if (entry == null)
                    continue;
                payloads[write++] = new SpriteAnimSetBuilder.ClipInput.EventPayloadInput
                {
                    Name = entry.Name,
                    Kind = entry.Kind,
                    IntValue = entry.IntValue,
                    IntY = entry.IntY,
                    IntZ = entry.IntZ,
                    IntW = entry.IntW,
                    FloatValue = entry.FloatValue,
                    FloatY = entry.FloatY,
                    FloatZ = entry.FloatZ,
                    FloatW = entry.FloatW,
                    TextValue = entry.Kind == (byte)SpriteEventPayloadKind.Asset &&
                        !string.IsNullOrEmpty(entry.AssetGuid)
                        ? entry.AssetGuid
                        : entry.TextValue,
                };
            }
            if (write == count)
                return payloads;
            if (write == 0)
                return null;
            var trimmed = new SpriteAnimSetBuilder.ClipInput.EventPayloadInput[write];
            for (int i = 0; i < write; i++)
                trimmed[i] = payloads[i];
            return trimmed;
        }
    }
}

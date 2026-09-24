using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Copy / paste / mirror whole poses (any clip, any time).</summary>
    public static partial class SpritePartsAuthoringOps
    {
        /// <summary>One part's pose values (local, as keys hold them).</summary>
        public struct PoseValue
        {
            public Vector2 Position;
            public float Rotation;
            public Vector2 Scale;
            public Vector2 Shear;
            /// <summary>Deform offsets (null = the setup mesh).</summary>
            public Vector2[] Deform;
        }

        /// <summary>
        /// Keys every listed part at <paramref name="time"/> with its pose (position, rotation, scale; shear and deform
        /// when they differ from the setup). <paramref name="only"/> limits it to some parts (null = all listed).
        /// </summary>
        public static KeyEditResult PastePose(SpriteSheetProfile profile, int clipIndex, float time,
            IReadOnlyDictionary<string, PoseValue> pose, ICollection<string> only = null)
        {
            var result = new KeyEditResult();
            var clip = GetClip(profile, clipIndex);
            if (clip == null || pose == null || pose.Count == 0)
            {
                result.Reason = clip == null ? "Open a clip." : "Copy a pose first.";
                return result;
            }
            foreach (var pair in pose)
            {
                string id = SpritePartIdUtility.Canonical(pair.Key);
                if (only != null && !only.Contains(id))
                    continue;
                var slot = FindSlot(profile, id);
                if (slot == null || slot.EditorLocked)
                    continue;
                var track = GetOrCreateTrack(clip, id);
                track.Keys ??= new List<SpritePartsKeyDef>();
                var key = FindKeyAtTime(track, time);
                if (key == null)
                {
                    key = new SpritePartsKeyDef { Time = time, Channels = SpritePartsKeyChannel.None, AppearanceId = string.Empty };
                    track.Keys.Add(key);
                    track.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
                }
                var v = pair.Value;
                key.Position = v.Position;
                key.Rotation = v.Rotation;
                key.Scale = SanitizeScale(v.Scale);
                key.Channels |= SpritePartsKeyChannel.Transform;
                bool hasShear = (v.Shear - slot.RestShear).sqrMagnitude > 1e-10f
                                || track.Keys.Exists(k => k != null && (k.Channels & SpritePartsKeyChannel.Shear) != 0);
                if (hasShear)
                {
                    key.Shear = v.Shear;
                    key.Channels |= SpritePartsKeyChannel.Shear;
                }
                if (v.Deform != null)
                {
                    key.Deform = (Vector2[])v.Deform.Clone();
                    key.Channels |= SpritePartsKeyChannel.Deform;
                }
                result.Affected++;
            }
            result.Ok = result.Affected > 0;
            if (!result.Ok)
                result.Reason = "None of the copied parts are selected here.";
            return result;
        }

        /// <summary>
        /// The pose flipped left-right: each part takes its partner's change from the setup pose ("Arm L" and "Arm R",
        /// matched by name), mirrored (x moves, turns and shear change sign). Parts without a partner mirror
        /// themselves. Deform stays with each part. Works for rigs whose sides differ in their setup, too.
        /// </summary>
        public static Dictionary<string, PoseValue> MirrorPose(SpriteSheetProfile profile, IReadOnlyDictionary<string, PoseValue> pose)
        {
            var result = new Dictionary<string, PoseValue>();
            if (profile?.PartsSlots == null || pose == null)
                return result;
            foreach (var pair in pose)
            {
                var source = FindSlot(profile, pair.Key ?? string.Empty);
                if (source == null)
                    continue;
                var target = MirrorPartner(profile, source) ?? source;
                string targetId = SpritePartIdUtility.Canonical(target.SlotId);
                var v = pair.Value;
                Vector2 dPos = v.Position - source.RestPosition;
                float dRot = Mathf.DeltaAngle(source.RestRotation, v.Rotation);
                Vector2 dScale = v.Scale - source.RestScale;
                Vector2 dShear = v.Shear - source.RestShear;
                pose.TryGetValue(targetId, out var own);
                result[targetId] = new PoseValue
                {
                    Position = target.RestPosition + new Vector2(-dPos.x, dPos.y),
                    Rotation = target.RestRotation - dRot,
                    Scale = target.RestScale + dScale,
                    Shear = target.RestShear - dShear,
                    Deform = own.Deform,
                };
            }
            return result;
        }

        /// <summary>The part on the other side ("Hand L" -> "Hand R"), or null.</summary>
        public static SpritePartSlotDef MirrorPartner(SpriteSheetProfile profile, SpritePartSlotDef slot)
        {
            string other = SpritePartsSkinning.MirrorSideName(slot?.Name);
            if (string.IsNullOrEmpty(other))
                return null;
            foreach (var s in profile.PartsSlots)
                if (s != null && !ReferenceEquals(s, slot) && s.Name == other)
                    return s;
            return null;
        }
    }
}

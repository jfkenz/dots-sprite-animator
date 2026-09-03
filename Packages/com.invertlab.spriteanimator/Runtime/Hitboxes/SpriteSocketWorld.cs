using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Spawns and updates Unity Transform children under <c>SpriteSockets</c>
    /// from independent motion tracks and frame-attached socket keys.
    /// Authored <see cref="FrameSocketDef.LocalPosition"/> / motion keys are
    /// source-sheet pixels from the profile pivot (+x right, +y up). Scene mesh
    /// origin is cell bottom-center (feet), so poses are converted into that
    /// local space before parenting under the host.
    /// </summary>
    public static class SpriteSocketWorld
    {
        public const string RootName = "SpriteSockets";
        public const string PivotName = "Pivot";

        /// <summary>Normalized cell UV of the Scene/preview mesh origin (bottom-center).</summary>
        public static readonly Vector2 MeshOriginNormalized = new(0.5f, 0f);

        static readonly List<string> NameScratch = new(16);
        static readonly HashSet<string> KeepScratch = new(System.StringComparer.Ordinal);

        public static void SyncUnitySockets(Transform host, SpriteSheetProfile data, string clipName, int frame,
            float independentTimeSeconds, bool flipX, bool flipY, bool bakePivot = true)
        {
            if (host == null)
                return;

            if (bakePivot)
                SyncPivotMarker(host, data, clipName, flipX, flipY);
            else
                ClearPivotMarker(host);

            Transform root = host.Find(RootName);
            if (data == null)
            {
                ClearRoot(root);
                return;
            }

            data.EnsureSheets();
            data.EnsureSocketMotions();

            NameScratch.Clear();
            KeepScratch.Clear();
            var poses = new Dictionary<string, Pose>(System.StringComparer.Ordinal);

            CollectIndependentPoses(data, independentTimeSeconds, poses);
            CollectFramePoses(data, clipName, frame, poses);

            if (poses.Count == 0)
            {
                ClearRoot(root);
                return;
            }

            if (root == null)
            {
                var go = new GameObject(RootName);
                go.transform.SetParent(host, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                root = go.transform;
            }

            // Flip is applied per-pose around profile/sheet pivot (same as UV flip),
            // not via negative root scale (which would mirror FlipY around feet).
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            // Host Quad is scaled to cell world size; socket poses are already in world
            // units from the mesh origin. Cancel host scale so child world position matches
            // gizmos / ECS SpriteSocketAttachmentSystem.
            Vector3 hostScale = host.localScale;
            float invSx = 1f / (Mathf.Abs(hostScale.x) > 1e-4f ? hostScale.x : 1f);
            float invSy = 1f / (Mathf.Abs(hostScale.y) > 1e-4f ? hostScale.y : 1f);

            var displaySheet = DisplaySheet(data, clipName);

            foreach (var kv in poses)
            {
                string name = kv.Key;
                KeepScratch.Add(name);
                Transform child = root.Find(name);
                if (child == null)
                {
                    var go = new GameObject(name);
                    go.transform.SetParent(root, false);
                    child = go.transform;
                }

                Pose pose = kv.Value;
                Vector2 meshLocal = MirrorAroundPivot(pose.Position, displaySheet,
                    ResolvePivot(data, displaySheet), flipX, flipY);
                float angle = FlipAngle(pose.Angle, flipX, flipY);
                Vector2 scale = FlipScale(pose.Scale, flipX, flipY);
                child.localPosition = new Vector3(meshLocal.x * invSx, meshLocal.y * invSy, 0f);
                child.localEulerAngles = new Vector3(0f, 0f, angle);
                child.localScale = new Vector3(scale.x, scale.y, 1f);
            }

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child == null || KeepScratch.Contains(child.name))
                    continue;
                DestroyGo(child.gameObject);
            }
        }

        public static void ClearUnitySockets(Transform host)
        {
            if (host == null)
                return;
            ClearRoot(host.Find(RootName));
        }

        /// <summary>
        /// Creates or updates an empty <c>Pivot</c> child at the authored
        /// <see cref="SpriteSheetProfile.Pivot"/> in mesh-local space (same as
        /// <see cref="PixelsFromPivotToMeshLocal"/> with a zero pixel offset).
        /// When flipped, mirrors around the authored pivot like preview UVs.
        /// </summary>
        public static void SyncPivotMarker(Transform host, SpriteSheetProfile data = null,
            string clipName = null, bool flipX = false, bool flipY = false)
        {
            if (host == null)
                return;
            Transform pivot = host.Find(PivotName);
            if (pivot == null)
            {
                var go = new GameObject(PivotName);
                go.transform.SetParent(host, false);
                pivot = go.transform;
            }

            Vector2 meshLocal = Vector2.zero;
            if (data != null)
            {
                data.EnsureSheets();
                var sheet = DisplaySheet(data, clipName);
                if (sheet != null)
                {
                    Vector2 pivotUv = ResolvePivot(data, sheet);
                    meshLocal = PixelsFromPivotToMeshLocal(sheet, pivotUv, Vector2.zero);
                    meshLocal = MirrorAroundPivot(meshLocal, sheet, pivotUv, flipX, flipY);
                }
            }

            Vector3 hostScale = host.localScale;
            float invSx = 1f / (Mathf.Abs(hostScale.x) > 1e-4f ? hostScale.x : 1f);
            float invSy = 1f / (Mathf.Abs(hostScale.y) > 1e-4f ? hostScale.y : 1f);
            pivot.localPosition = new Vector3(meshLocal.x * invSx, meshLocal.y * invSy, 0f);
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;
        }

        public static void ClearPivotMarker(Transform host)
        {
            if (host == null)
                return;
            Transform pivot = host.Find(PivotName);
            if (pivot != null)
                DestroyGo(pivot.gameObject);
        }

        /// <summary>
        /// Fills results with mesh-local unit poses (world units from bottom-center)
        /// for independent motions and frame sockets (independent wins on name clash).
        /// Used by Scene gizmos when SpriteSockets children are not present.
        /// Results are unflipped; callers apply <see cref="MirrorAroundPivot"/>.
        /// </summary>
        public static void CollectLocalPoses(SpriteSheetProfile data, string clipName, int frame,
            float independentTimeSeconds, List<LocalPose> results)
        {
            results?.Clear();
            if (data == null || results == null)
                return;

            data.EnsureSheets();
            data.EnsureSocketMotions();
            NameScratch.Clear();
            var poses = new Dictionary<string, Pose>(System.StringComparer.Ordinal);
            CollectIndependentPoses(data, independentTimeSeconds, poses);
            CollectFramePoses(data, clipName, frame, poses);
            foreach (var kv in poses)
                results.Add(new LocalPose(kv.Key, kv.Value.Position, kv.Value.Angle, kv.Value.Scale));
        }

        public readonly struct LocalPose
        {
            public readonly string Name;
            public readonly Vector2 Position;
            public readonly float Angle;
            public readonly Vector2 Scale;

            public LocalPose(string name, Vector2 position, float angle, Vector2 scale)
            {
                Name = name;
                Position = position;
                Angle = angle;
                Scale = scale;
            }
        }

        /// <summary>
        /// Converts authored pivot-relative source pixels (+x right, +y up) into
        /// world units from the Scene mesh origin (cell bottom-center / feet).
        /// Matches editor preview: PivotScreen + SourcePixelsToScreenOffset.
        /// </summary>
        public static Vector2 PixelsFromPivotToMeshLocal(
            SpriteSheetDef sheet, Vector2 profilePivot, Vector2 pivotPixels)
        {
            float ppu = Mathf.Max(0.01f, SpriteSheetProfile.GetPixelsPerUnit(sheet));
            Vector2 pivot = new(
                Mathf.Clamp01(profilePivot.x),
                Mathf.Clamp01(profilePivot.y));

            if (!SpriteSheetProfile.TryGetCellPixels(sheet, out float cellW, out float cellH))
                return pivotPixels / ppu;

            // Mesh origin is bottom-center (0.5, 0) in normalized cell space.
            // Profile pivot is also normalized (x left→right, y bottom→top).
            Vector2 pivotFromMesh = new(
                (pivot.x - MeshOriginNormalized.x) * (cellW / ppu),
                (pivot.y - MeshOriginNormalized.y) * (cellH / ppu));
            return pivotFromMesh + pivotPixels / ppu;
        }

        /// <summary>
        /// Mirrors mesh-local world units around the authored sheet/profile pivot,
        /// matching UV FlipX/FlipY. Default pivot (0.5, 0.5) is cell center
        /// (x=0, y = cellHeight/2 on the bottom-center preview quad).
        /// </summary>
        public static Vector2 MirrorAroundPivot(
            Vector2 meshLocalUnits, SpriteSheetDef sheet, Vector2 normalizedPivot,
            bool flipX, bool flipY)
        {
            if (!flipX && !flipY)
                return meshLocalUnits;

            Vector2 axis = PixelsFromPivotToMeshLocal(sheet, normalizedPivot, Vector2.zero);
            if (flipX)
                meshLocalUnits.x = 2f * axis.x - meshLocalUnits.x;
            if (flipY)
                meshLocalUnits.y = 2f * axis.y - meshLocalUnits.y;
            return meshLocalUnits;
        }

        /// <summary>Legacy name — mirrors around <paramref name="sheet"/> pivot (or cell center).</summary>
        public static Vector2 MirrorAroundCellCenter(
            Vector2 meshLocalUnits, SpriteSheetDef sheet, bool flipX, bool flipY)
            => MirrorAroundPivot(meshLocalUnits, sheet, ResolvePivot(null, sheet), flipX, flipY);

        /// <summary>Legacy overload with explicit profile for pivot resolution.</summary>
        public static Vector2 MirrorAroundCellCenter(
            Vector2 meshLocalUnits, SpriteSheetDef sheet, bool flipX, bool flipY,
            SpriteSheetProfile profile)
            => MirrorAroundPivot(meshLocalUnits, sheet, ResolvePivot(profile, sheet), flipX, flipY);

        public static SpriteSheetDef DisplaySheet(SpriteSheetProfile data, string clipName)
        {
            if (data == null)
                return null;
            data.EnsureSheets();
            if (!string.IsNullOrEmpty(clipName))
            {
                var clip = data.FindClip(clipName);
                if (clip != null)
                {
                    var sheet = data.SheetForClip(clip);
                    if (sheet != null)
                        return sheet;
                }
            }
            return data.SheetAt(0);
        }

        static float FlipAngle(float angle, bool flipX, bool flipY)
        {
            if (flipX != flipY)
                return -angle;
            return angle;
        }

        static Vector2 FlipScale(Vector2 scale, bool flipX, bool flipY)
        {
            if (flipX)
                scale.x = -scale.x;
            if (flipY)
                scale.y = -scale.y;
            return scale;
        }

        static Vector2 ProfilePivot(SpriteSheetProfile data)
            => ResolvePivot(data, null);

        /// <summary>
        /// Normalized cell UV pivot used as the FlipX/FlipY axis.
        /// Prefers sheet.Pivot, then profile.Pivot, then cell center (0.5, 0.5).
        /// </summary>
        public static Vector2 ResolvePivot(SpriteSheetProfile data, SpriteSheetDef sheet)
        {
            if (sheet != null && sheet.Pivot != default)
                return new Vector2(Mathf.Clamp01(sheet.Pivot.x), Mathf.Clamp01(sheet.Pivot.y));
            if (data != null && data.Pivot != default)
                return new Vector2(Mathf.Clamp01(data.Pivot.x), Mathf.Clamp01(data.Pivot.y));
            return SpriteSheetProfile.DefaultPivot;
        }

        static void CollectIndependentPoses(SpriteSheetProfile data, float independentTimeSeconds,
            Dictionary<string, Pose> poses)
        {
            if (data.SocketMotions == null)
                return;

            float duration = Mathf.Max(0.01f, data.IndependentMotionDuration);
            for (int i = 0; i < data.SocketMotions.Count; i++)
            {
                var track = data.SocketMotions[i];
                if (track?.Keys == null || track.Keys.Count == 0)
                    continue;

                string name = SpriteSocketKeys.CanonicalName(track.SocketName);
                if (string.IsNullOrEmpty(name) || poses.ContainsKey(name))
                    continue;

                if (!TrySampleIndependent(data, track, independentTimeSeconds, duration,
                        out Vector2 local, out float angle, out Vector2 scale))
                    continue;

                poses[name] = new Pose(local, angle, scale);
            }
        }

        static void CollectFramePoses(SpriteSheetProfile data, string clipName, int frame,
            Dictionary<string, Pose> poses)
        {
            var clip = data.FindClip(clipName);
            if (clip?.Sockets == null || clip.Sockets.Count == 0)
                return;

            var sheet = data.SheetForClip(clip);
            Vector2 pivot = ProfilePivot(data);

            SpriteSocketKeys.FillUniqueNamesInOrder(clip.Sockets, NameScratch);
            for (int i = 0; i < NameScratch.Count; i++)
            {
                string name = NameScratch[i];
                if (poses.ContainsKey(name))
                    continue;

                if (!SpriteSocketKeys.TryGetPose(clip.Sockets, name, frame,
                        out Vector2 pixels, out float angle, out Vector2 scale, out bool onFrame))
                    continue;
                if (!onFrame && SpriteSocketKeys.FindOnFrame(clip.Sockets, name, frame) == null)
                {
                    // Still show last-known pose so attachments remain glued between keys.
                }

                poses[name] = new Pose(
                    PixelsFromPivotToMeshLocal(sheet, pivot, pixels),
                    angle,
                    SpriteSocketKeys.ResolvedScale(scale));
            }
        }

        static bool TrySampleIndependent(SpriteSheetProfile data, SpriteSocketMotionTrack track,
            float independentTimeSeconds, float duration,
            out Vector2 localUnits, out float angle, out Vector2 scale)
        {
            localUnits = Vector2.zero;
            angle = 0f;
            scale = Vector2.one;
            var keys = track.Keys;
            int count = keys.Count;
            if (count == 0)
                return false;

            float t = independentTimeSeconds / duration;
            t = track.Loop ? Mathf.Repeat(t, 1f) : Mathf.Clamp01(t);

            SpriteSocketMotionKey a = keys[0];
            SpriteSocketMotionKey b = a;
            int fromIndex = 0;
            int toIndex = 0;
            float blend = 0f;

            if (count > 1)
            {
                int last = count - 1;
                if (t < keys[0].NormalizedTime && track.Loop)
                {
                    fromIndex = last;
                    toIndex = 0;
                    a = keys[last];
                    b = keys[0];
                    float span = 1f - a.NormalizedTime + b.NormalizedTime;
                    blend = span > 0.0001f ? (t + 1f - a.NormalizedTime) / span : 0f;
                }
                else if (t >= keys[last].NormalizedTime)
                {
                    if (track.Loop && t < 1f)
                    {
                        fromIndex = last;
                        toIndex = 0;
                        a = keys[last];
                        b = keys[0];
                        float span = 1f - a.NormalizedTime + b.NormalizedTime;
                        blend = span > 0.0001f ? (t - a.NormalizedTime) / span : 0f;
                    }
                    else
                    {
                        fromIndex = toIndex = last;
                        a = b = keys[last];
                    }
                }
                else
                {
                    for (int k = 0; k < last; k++)
                    {
                        if (t < keys[k + 1].NormalizedTime)
                        {
                            fromIndex = k;
                            toIndex = k + 1;
                            a = keys[k];
                            b = keys[k + 1];
                            float span = b.NormalizedTime - a.NormalizedTime;
                            blend = span > 0.0001f
                                ? Mathf.Clamp01((t - a.NormalizedTime) / span)
                                : 0f;
                            break;
                        }
                    }
                }
            }

            blend = a.UseCustomEase
                ? a.EvaluateCustomEase(blend)
                : SpriteEase.Evaluate(
                    SpriteEase.IsValidMode(a.EaseMode)
                        ? (SpriteEaseMode)a.EaseMode
                        : SpriteEaseMode.SmoothStep,
                    blend, a.AllowOvershoot);

            Vector2 sampledPixels;
            float2 pathDerivative;
            if (fromIndex == toIndex)
            {
                sampledPixels = a.LocalPosition;
                pathDerivative = new float2(0f, 0f);
            }
            else
            {
                int before = track.Loop
                    ? (fromIndex - 1 + count) % count
                    : Mathf.Max(0, fromIndex - 1);
                int after = track.Loop
                    ? (toIndex + 1) % count
                    : Mathf.Min(count - 1, toIndex + 1);
                float2 p0 = ToFloat2(keys[before].LocalPosition);
                float2 p1 = ToFloat2(a.LocalPosition);
                float2 p2 = ToFloat2(b.LocalPosition);
                float2 p3 = ToFloat2(keys[after].LocalPosition);
                float2 outT = ToFloat2(a.OutTangent);
                float2 inT = ToFloat2(b.InTangent);
                sampledPixels = ToVector2(SpriteSocketMotionInterpolation.Position(
                    a.PathMode, p0, p1, p2, p3, outT, inT,
                    a.ArcBulge, a.ArcClockwise ? (byte)1 : (byte)0, blend));
                pathDerivative = SpriteSocketMotionInterpolation.Derivative(
                    a.PathMode, p0, p1, p2, p3, outT, inT,
                    a.ArcBulge, a.ArcClockwise ? (byte)1 : (byte)0, blend);
            }

            var sheet = data.SheetAt(track.ReferenceSheetIndex);
            localUnits = PixelsFromPivotToMeshLocal(sheet, ProfilePivot(data), sampledPixels);
            angle = SpriteSocketMotionInterpolation.Rotation(
                a.RotationMode, a.LocalAngle, b.LocalAngle, a.RotationTurns,
                a.FacingAngleOffset, pathDerivative, blend);
            scale = Vector2.LerpUnclamped(
                SpriteSocketKeys.ResolvedScale(a.LocalScale),
                SpriteSocketKeys.ResolvedScale(b.LocalScale), blend);
            return true;
        }

        static float2 ToFloat2(Vector2 v) => new float2(v.x, v.y);
        static Vector2 ToVector2(float2 v) => new Vector2(v.x, v.y);

        static void ClearRoot(Transform root)
        {
            if (root == null)
                return;
            DestroyGo(root.gameObject);
        }

        static void DestroyGo(GameObject go)
        {
            if (go == null)
                return;
#if UNITY_EDITOR
            // DestroyImmediate is forbidden during OnValidate, physics trigger/contact,
            // animation events, and rendering callbacks. Use Destroy while playing;
            // edit-mode callers must only DestroyImmediate from delayCall / button clicks.
            // Rename before deferred Destroy so Find(name) cannot hit a zombie same frame.
            if (Application.isPlaying)
            {
                go.name = go.name + "_Destroyed";
                Object.Destroy(go);
            }
            else
                Object.DestroyImmediate(go);
#else
            Object.Destroy(go);
#endif
        }

        readonly struct Pose
        {
            public readonly Vector2 Position;
            public readonly float Angle;
            public readonly Vector2 Scale;

            public Pose(Vector2 position, float angle, Vector2 scale)
            {
                Position = position;
                Angle = angle;
                Scale = scale;
            }
        }
    }
}

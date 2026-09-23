using Unity.Collections;
using Unity.Mathematics;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Spine's transform and path constraints, solved in character-root space after IK (transforms, then paths).
    /// Each constrained part is written back as a local change and the hierarchy recomposed, so children follow.
    /// </summary>
    public static class SpritePartsConstraints
    {
        const int PathSteps = 16;

        // ---- Transform constraints ----

        /// <param name="mixScale">Per constraint (keyed overall mix); not created = 1.</param>
        public static void ApplyTransforms(ref SpritePartsSetBlob set, NativeArray<SpritePartsSampler.Pose> local,
            NativeArray<float4x4> localToRoot, NativeArray<float> mixScale = default)
        {
            for (int c = 0; c < set.TransformConstraints.Length; c++)
            {
                ref var tc = ref set.TransformConstraints[c];
                float k = mixScale.IsCreated && c < mixScale.Length ? math.saturate(mixScale[c]) : 1f;
                if (k <= 0f || tc.Target < 0 || tc.Target >= local.Length)
                    continue;
                var mix = new Mixes
                {
                    Rotate = tc.MixRotate * k, X = tc.MixX * k, Y = tc.MixY * k, ScaleX = tc.MixScaleX * k, ScaleY = tc.MixScaleY * k,
                };
                if (mix.Rotate + mix.X + mix.Y + mix.ScaleX + mix.ScaleY <= 0f)
                    continue;
                for (int b = 0; b < tc.Bones.Length; b++)
                {
                    int bone = tc.Bones[b];
                    if (bone < 0 || bone >= local.Length || bone == tc.Target)
                        continue;
                    if (tc.Local != 0)
                        CopyLocal(ref set, ref tc, bone, local, mix);
                    else
                        CopyWorld(ref set, ref tc, bone, local, localToRoot, mix);
                    SpritePartsHierarchy.ComposeLocalToRoot(ref set, local, localToRoot);
                }
            }
        }

        struct Mixes
        {
            public float Rotate, X, Y, ScaleX, ScaleY;
        }

        static void CopyLocal(ref SpritePartsSetBlob set, ref SpritePartsTransformBlob tc, int bone,
            NativeArray<SpritePartsSampler.Pose> local, in Mixes mix)
        {
            var t = local[tc.Target];
            var p = local[bone];
            ref var rest = ref set.Slots[tc.Target];
            if (tc.Relative != 0)
            {
                p.Rotation += (t.Rotation - rest.RestRotation + tc.OffsetRotation) * mix.Rotate;
                p.Position += (t.Position - rest.RestPosition + tc.OffsetPosition) * new float2(mix.X, mix.Y);
                p.Scale += (t.Scale - rest.RestScale + tc.OffsetScale) * new float2(mix.ScaleX, mix.ScaleY);
            }
            else
            {
                p.Rotation += DeltaDeg(p.Rotation, t.Rotation + tc.OffsetRotation) * mix.Rotate;
                p.Position = math.lerp(p.Position, t.Position + tc.OffsetPosition, new float2(mix.X, mix.Y));
                p.Scale = math.lerp(p.Scale, t.Scale + tc.OffsetScale, new float2(mix.ScaleX, mix.ScaleY));
            }
            local[bone] = p;
        }

        static void CopyWorld(ref SpritePartsSetBlob set, ref SpritePartsTransformBlob tc, int bone,
            NativeArray<SpritePartsSampler.Pose> local, NativeArray<float4x4> localToRoot, in Mixes mix)
        {
            float4x4 tm = localToRoot[tc.Target];
            float4x4 bm = localToRoot[bone];
            float tRot = SpritePartsHierarchy.ExtractRotationDeg(tm);
            float bRot = SpritePartsHierarchy.ExtractRotationDeg(bm);
            float2 tScale = WorldScale(tm), bScale = WorldScale(bm);
            float2 bPos = bm.c3.xy;
            var translate = new float2(mix.X, mix.Y);
            var scale = new float2(mix.ScaleX, mix.ScaleY);
            float turn;
            float2 wantPos, wantScale;
            if (tc.Relative != 0)
            {
                // The target's change from its setup pose, added on.
                float4x4 rm = set.Slots[tc.Target].RestToRoot;
                turn = (DeltaDeg(SpritePartsHierarchy.ExtractRotationDeg(rm), tRot) + tc.OffsetRotation) * mix.Rotate;
                wantPos = bPos + (tm.c3.xy - rm.c3.xy + tc.OffsetPosition) * translate;
                wantScale = bScale + (tScale - WorldScale(rm) + tc.OffsetScale) * scale;
            }
            else
            {
                turn = DeltaDeg(bRot, tRot + tc.OffsetRotation) * mix.Rotate;
                wantPos = bPos + (tm.c3.xy + tc.OffsetPosition - bPos) * translate;
                wantScale = bScale + (tScale + tc.OffsetScale - bScale) * scale;
            }
            if (math.any(translate > 0f))
                MoveTo(ref set, bone, wantPos, local, localToRoot);
            if (math.any(scale > 0f))
            {
                var p = local[bone];
                if (bScale.x > 1e-6f)
                    p.Scale.x *= wantScale.x / bScale.x;
                if (bScale.y > 1e-6f)
                    p.Scale.y *= wantScale.y / bScale.y;
                local[bone] = p;
            }
            SpritePartsIk.Turn(ref set, bone, turn, local, localToRoot);
        }

        // ---- Path constraints ----

        /// <param name="position">Per constraint (keyed position); not created = each constraint's own.</param>
        /// <param name="mixScale">Per constraint (keyed overall mix); not created = 1.</param>
        public static void ApplyPaths(ref SpritePartsSetBlob set, NativeArray<SpritePartsSampler.Pose> local,
            NativeArray<float4x4> localToRoot, NativeArray<float> position = default, NativeArray<float> mixScale = default)
        {
            if (set.PathConstraints.Length == 0)
                return;
            var poly = new NativeList<float2>(64, Allocator.Temp);
            var lengths = new NativeList<float>(64, Allocator.Temp);
            try
            {
                for (int c = 0; c < set.PathConstraints.Length; c++)
                {
                    ref var pc = ref set.PathConstraints[c];
                    float k = mixScale.IsCreated && c < mixScale.Length ? math.saturate(mixScale[c]) : 1f;
                    float mixTranslate = pc.MixTranslate * k, mixRotate = pc.MixRotate * k;
                    if (k <= 0f || pc.PathSlot < 0 || pc.PathSlot >= localToRoot.Length)
                        continue;
                    ref var path = ref set.Slots[pc.PathSlot];
                    if (path.IsPath == 0 || path.PathPoints.Length < 2)
                        continue;
                    SamplePath(ref path, localToRoot[pc.PathSlot], poly, lengths);
                    float total = lengths[lengths.Length - 1];
                    if (total < 1e-6f)
                        continue;
                    bool closed = path.PathClosed != 0;
                    float start = position.IsCreated && c < position.Length ? position[c] : pc.Position;
                    for (int b = 0; b < pc.Bones.Length; b++)
                    {
                        int bone = pc.Bones[b];
                        if (bone < 0 || bone >= local.Length)
                            continue;
                        float d = Along(ref pc, start, b, total, closed);
                        PointAt(poly, lengths, d, out float2 point, out float2 tangent);
                        if (mixTranslate > 0f)
                        {
                            float2 now = localToRoot[bone].c3.xy;
                            MoveTo(ref set, bone, now + (point - now) * mixTranslate, local, localToRoot);
                            SpritePartsHierarchy.ComposeLocalToRoot(ref set, local, localToRoot);
                        }
                        if (mixRotate <= 0f || pc.RotateMode == (byte)SpritePartsPathRotate.None)
                            continue;
                        float2 dir = tangent;
                        if (pc.RotateMode == (byte)SpritePartsPathRotate.Chain)
                        {
                            // Point at where the next part sits (the path's direction at the end of the chain).
                            PointAt(poly, lengths, Along(ref pc, start, b + 1, total, closed), out float2 next, out _);
                            float2 toNext = next - localToRoot[bone].c3.xy;
                            if (math.lengthsq(toNext) > 1e-10f)
                                dir = toNext;
                        }
                        if (math.lengthsq(dir) < 1e-12f)
                            continue;
                        float want = math.degrees(math.atan2(dir.y, dir.x)) + pc.OffsetRotation;
                        float current = SpritePartsHierarchy.ExtractRotationDeg(localToRoot[bone]);
                        SpritePartsIk.Turn(ref set, bone, DeltaDeg(current, want) * mixRotate, local, localToRoot);
                        SpritePartsHierarchy.ComposeLocalToRoot(ref set, local, localToRoot);
                    }
                }
            }
            finally
            {
                poly.Dispose();
                lengths.Dispose();
            }
        }

        /// <summary>Distance along the path of chain part <paramref name="index"/>: closed paths wrap, open ones clamp.</summary>
        static float Along(ref SpritePartsPathBlob pc, float start, int index, float total, bool closed)
        {
            float d = pc.SpacingMode == (byte)SpritePartsPathSpacing.Fixed
                ? start * total + index * pc.Spacing
                : (start + index * pc.Spacing) * total;
            if (closed)
            {
                d %= total;
                return d < 0f ? d + total : d;
            }
            return math.clamp(d, 0f, total);
        }

        /// <summary>The path in root space as a polyline with running lengths.</summary>
        static void SamplePath(ref SpritePartSlotBlob path, float4x4 m, NativeList<float2> poly, NativeList<float> lengths)
        {
            poly.Clear();
            lengths.Clear();
            int n = path.PathPoints.Length;
            bool closed = path.PathClosed != 0;
            int segments = closed ? n : n - 1;
            float run = 0f;
            for (int s = 0; s < segments; s++)
            {
                float2 p0 = PathPoint(ref path, s - 1, closed), p1 = PathPoint(ref path, s, closed);
                float2 p2 = PathPoint(ref path, s + 1, closed), p3 = PathPoint(ref path, s + 2, closed);
                for (int j = s == 0 ? 0 : 1; j <= PathSteps; j++)
                {
                    float2 q = SpritePartsHierarchy.TransformPoint(m, CatmullRom(p0, p1, p2, p3, j / (float)PathSteps));
                    if (poly.Length > 0)
                        run += math.distance(poly[poly.Length - 1], q);
                    poly.Add(q);
                    lengths.Add(run);
                }
            }
        }

        static float2 PathPoint(ref SpritePartSlotBlob path, int i, bool closed)
        {
            int n = path.PathPoints.Length;
            i = closed ? ((i % n) + n) % n : math.clamp(i, 0, n - 1);
            return path.PathPoints[i];
        }

        /// <summary>A Catmull-Rom curve: passes through p1 (u = 0) and p2 (u = 1).</summary>
        public static float2 CatmullRom(float2 p0, float2 p1, float2 p2, float2 p3, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * (2f * p1 + (p2 - p0) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (3f * p1 - p0 - 3f * p2 + p3) * u3);
        }

        static void PointAt(NativeList<float2> poly, NativeList<float> lengths, float d, out float2 point, out float2 tangent)
        {
            int last = poly.Length - 1;
            int i = 0;
            while (i < last - 1 && lengths[i + 1] < d)
                i++;
            float span = lengths[i + 1] - lengths[i];
            float u = span > 1e-8f ? math.saturate((d - lengths[i]) / span) : 0f;
            point = math.lerp(poly[i], poly[i + 1], u);
            tangent = poly[i + 1] - poly[i];
        }

        // ---- Shared ----

        /// <summary>Moves a part so its origin lands on <paramref name="rootPoint"/> (root space), through its parent's space.</summary>
        static void MoveTo(ref SpritePartsSetBlob set, int bone, float2 rootPoint, NativeArray<SpritePartsSampler.Pose> local,
            NativeArray<float4x4> localToRoot)
        {
            int parent = set.Slots[bone].ParentSlotIndex;
            var p = local[bone];
            p.Position = parent >= 0 && parent < localToRoot.Length
                ? SpritePartsHierarchy.InverseTransformPoint(localToRoot[parent], rootPoint)
                : rootPoint;
            local[bone] = p;
        }

        static float2 WorldScale(float4x4 m) => new float2(math.length(m.c0.xy), math.length(m.c1.xy));

        static float DeltaDeg(float from, float to)
        {
            float d = math.fmod(to - from + 180f, 360f);
            if (d < 0f)
                d += 360f;
            return d - 180f;
        }
    }
}

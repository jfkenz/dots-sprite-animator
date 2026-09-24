using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // GRAPH view (Spine's Graph): the selected parts' keys as curves over time, as the game plays them.
    //   Legend       X / Y / Rotation / Scale X / Scale Y / Values: show or hide each; Same Scale shares one height
    //   Drag a point changes that key's value (move keys in time in the dopesheet)
    //   Handles      a Bezier key shows two handles: drag them to shape its ease
    //   Right-click  Linear / Stepped / Bezier / ease presets for that key; Separate Curves gives Y, rotation and
    //                scale their own handles (X keeps the key's)
    public sealed partial class SpriteSheetToolWindow
    {
        [SerializeField] bool _partsGraphView;
        [SerializeField] int _partsGraphChannels = 0x3F;
        [SerializeField] bool _partsGraphSharedScale;

        enum GraphChannel { X, Y, Rotation, ScaleX, ScaleY, Value }

        static readonly string[] s_graphChannelNames = { "X", "Y", "Rotation", "Scale X", "Scale Y", "Values" };
        static readonly Color[] s_graphChannelColors =
        {
            new Color(1f, 0.4f, 0.4f), new Color(0.45f, 0.9f, 0.45f), new Color(0.45f, 0.7f, 1f),
            new Color(1f, 0.7f, 0.3f), new Color(0.85f, 0.9f, 0.35f), new Color(0.8f, 0.6f, 1f),
        };

        sealed class GraphCurve
        {
            public string Label;
            public Color Color;
            public GraphChannel Channel;
            /// <summary>Pose keys holding the channel, by time (pose curves).</summary>
            public List<SpritePartsKeyDef> Keys;
            /// <summary>Value keys by time (value curves).</summary>
            public List<SpritePartsValueKeyDef> ValueKeys;
            public SpritePartsValueKind ValueKind;
            /// <summary>Shown values per key (rotation unwrapped so the curve does not jump at ±180).</summary>
            public float[] Values;
            public float Min;
            public float Max;

            public int Count => Keys != null ? Keys.Count : ValueKeys.Count;
            public float Time(int i) => Keys != null ? Keys[i].Time : ValueKeys[i].Time;
            /// <summary>True when this channel of the key eases with its own handles (separate curves).</summary>
            public bool OwnCurve(int i) => Keys != null && Keys[i].Separate.On && Channel != GraphChannel.X;
            public byte Ease(int i) => OwnCurve(i) ? (byte)SpriteEaseMode.Bezier : Keys != null ? Keys[i].EaseMode : ValueKeys[i].EaseMode;
            public Vector4 Curve(int i) => OwnCurve(i) ? ChannelCurve(Keys[i].Separate, Channel) : Keys != null ? Keys[i].Curve : ValueKeys[i].Curve;
            public object Key(int i) => Keys != null ? Keys[i] : ValueKeys[i];
            public bool Held => Keys == null && (ValueKind == SpritePartsValueKind.IkBend || ValueKind == SpritePartsValueKind.SpriteGroup);
        }

        // Drag state. The scale is frozen while dragging so the curve does not refit under the mouse.
        object _graphDragKey;
        object _graphDragNext;
        GraphChannel _graphDragChannel;
        int _graphDragHandle;
        float _graphDragMin;
        float _graphDragMax;

        void DrawPartsGraphToggle(Rect r)
        {
            if (CurrentPartsClip == null)
                return;
            bool graph = GUI.Toggle(r, _partsGraphView,
                new GUIContent("Graph", "Show the selected parts' keys as curves: drag values, shape Bezier eases"), EditorStyles.miniButton);
            if (graph == _partsGraphView)
                return;
            _partsGraphView = graph;
            _graphDragKey = null;
            Repaint();
        }

        void DrawPartsGraph(Rect rect, SpritePartsClipDef clip, float duration, float labelW)
        {
            var legend = new Rect(rect.x, rect.y, labelW, rect.height);
            var area = new Rect(rect.x + labelW, rect.y + 4f, rect.width - labelW, Mathf.Max(10f, rect.height - 8f));
            DrawPartsGraphLegend(legend);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(area, new Color(0.08f, 0.09f, 0.11f));
                for (int g = 1; g < 4; g++)
                    EditorGUI.DrawRect(new Rect(area.x, area.y + area.height * g / 4f, area.width, 1f), new Color(1f, 1f, 1f, 0.05f));
                DrawPartsTrackFrameGrid(area, duration);
            }

            var curves = BuildGraphCurves(clip);
            if (curves.Count == 0)
            {
                GUI.Label(new Rect(area.x + 8f, area.y + 6f, area.width - 16f, 18f),
                    CurrentPartsSlot == null && _partsSelectedSlotIds.Count == 0
                        ? "Select parts to see their curves."
                        : "No keys on the selected parts for the channels shown.", _mutedStyle);
                return;
            }
            FitGraphCurves(curves);
            HandlePartsGraphInput(area, curves, duration);
            if (Event.current.type != EventType.Repaint)
                return;
            Handles.BeginGUI();
            foreach (var c in curves)
                DrawGraphCurve(area, c, duration);
            Handles.EndGUI();
            if (_partsGraphSharedScale)
            {
                GUI.Label(new Rect(area.x + 4f, area.y, 80f, 14f), curves[0].Max.ToString("0.##"), _mutedStyle);
                GUI.Label(new Rect(area.x + 4f, area.yMax - 14f, 80f, 14f), curves[0].Min.ToString("0.##"), _mutedStyle);
            }
        }

        void DrawPartsGraphLegend(Rect r)
        {
            float y = r.y + 2f;
            for (int c = 0; c < s_graphChannelNames.Length; c++)
            {
                if (y + 15f > r.yMax)
                    return;
                bool on = (_partsGraphChannels & (1 << c)) != 0;
                var row = new Rect(r.x + 2f, y, r.width - 8f, 14f);
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(new Rect(row.x, row.y + 3f, 8f, 8f), on ? s_graphChannelColors[c] : new Color(0.3f, 0.3f, 0.3f));
                bool next = GUI.Toggle(new Rect(row.x + 12f, row.y, row.width - 12f, row.height), on, s_graphChannelNames[c], _mutedStyle);
                if (next != on)
                {
                    _partsGraphChannels ^= 1 << c;
                    Repaint();
                }
                y += 15f;
            }
            if (y + 15f <= r.yMax)
            {
                bool shared = GUI.Toggle(new Rect(r.x + 2f, y + 2f, r.width - 8f, 14f), _partsGraphSharedScale,
                    new GUIContent("Same Scale", "Off: each curve fills the height (timing). On: one value scale for all"), EditorStyles.miniButton);
                if (shared != _partsGraphSharedScale)
                {
                    _partsGraphSharedScale = shared;
                    Repaint();
                }
            }
        }

        List<GraphCurve> BuildGraphCurves(SpritePartsClipDef clip)
        {
            var curves = new List<GraphCurve>();
            var shown = new List<SpritePartSlotDef>();
            foreach (var slot in _profile.PartsSlots)
            {
                if (slot == null)
                    continue;
                string id = SpritePartIdUtility.Canonical(slot.SlotId);
                if (_partsSelectedSlotIds.Contains(id) || slot == CurrentPartsSlot)
                    shown.Add(slot);
            }
            for (int s = 0; s < shown.Count; s++)
            {
                var track = SpritePartsAuthoringOps.FindTrack(clip, shown[s].SlotId);
                if (track?.Keys == null)
                    continue;
                for (int c = 0; c < (int)GraphChannel.Value; c++)
                {
                    if ((_partsGraphChannels & (1 << c)) == 0)
                        continue;
                    var channel = (GraphChannel)c;
                    var keys = track.Keys.FindAll(k => k != null && (k.Channels & KeyChannelOf(channel)) != 0);
                    if (keys.Count == 0)
                        continue;
                    keys.Sort((a, b) => a.Time.CompareTo(b.Time));
                    float shade = shown.Count > 1 ? Mathf.Lerp(1f, 0.6f, s / (float)(shown.Count - 1)) : 1f;
                    var color = s_graphChannelColors[c] * shade;
                    color.a = 1f;
                    curves.Add(new GraphCurve { Label = shown[s].Name + " " + s_graphChannelNames[c], Color = color, Channel = channel, Keys = keys });
                }
            }
            if ((_partsGraphChannels & (1 << (int)GraphChannel.Value)) != 0 && clip.ValueTracks != null)
            {
                foreach (var track in clip.ValueTracks)
                {
                    if (track?.Keys == null || track.Keys.Count == 0)
                        continue;
                    var keys = track.Keys.FindAll(k => k != null);
                    keys.Sort((a, b) => a.Time.CompareTo(b.Time));
                    curves.Add(new GraphCurve
                    {
                        Label = ValueKindLabel(track.Kind) + " " + track.Target, Color = ValueKindColor(track.Kind),
                        Channel = GraphChannel.Value, ValueKeys = keys, ValueKind = track.Kind,
                    });
                }
            }
            return curves;
        }

        static SpritePartsKeyChannel KeyChannelOf(GraphChannel c) => c switch
        {
            GraphChannel.X or GraphChannel.Y => SpritePartsKeyChannel.Position,
            GraphChannel.Rotation => SpritePartsKeyChannel.Rotation,
            _ => SpritePartsKeyChannel.Scale,
        };

        static Vector4 ChannelCurve(in SpritePartsSeparateCurves s, GraphChannel c) => c switch
        {
            GraphChannel.Y => s.Y,
            GraphChannel.Rotation => s.Rotation,
            GraphChannel.ScaleX => s.ScaleX,
            _ => s.ScaleY,
        };

        /// <summary>Writes one channel's handles: its own curve on a separate-curves key, else the shared one.</summary>
        static void SetCurve(GraphCurve c, int i, Vector4 h)
        {
            if (c.Keys == null)
            {
                c.ValueKeys[i].Curve = h;
                return;
            }
            var key = c.Keys[i];
            if (!c.OwnCurve(i))
            {
                key.Curve = h;
                return;
            }
            var s = key.Separate;
            switch (c.Channel)
            {
                case GraphChannel.Y: s.Y = h; break;
                case GraphChannel.Rotation: s.Rotation = h; break;
                case GraphChannel.ScaleX: s.ScaleX = h; break;
                default: s.ScaleY = h; break;
            }
            key.Separate = s;
        }

        static float PoseValue(SpritePartsKeyDef k, GraphChannel c) => c switch
        {
            GraphChannel.X => k.Position.x,
            GraphChannel.Y => k.Position.y,
            GraphChannel.Rotation => k.Rotation,
            GraphChannel.ScaleX => k.Scale.x,
            _ => k.Scale.y,
        };

        static void SetPoseValue(SpritePartsKeyDef k, GraphChannel c, float v)
        {
            switch (c)
            {
                case GraphChannel.X: k.Position.x = v; break;
                case GraphChannel.Y: k.Position.y = v; break;
                case GraphChannel.Rotation: k.Rotation = v; break;
                case GraphChannel.ScaleX: k.Scale.x = v; break;
                default: k.Scale.y = v; break;
            }
        }

        void FitGraphCurves(List<GraphCurve> curves)
        {
            float allMin = float.MaxValue, allMax = float.MinValue;
            foreach (var c in curves)
            {
                c.Values = new float[c.Count];
                for (int i = 0; i < c.Count; i++)
                {
                    float v = c.Keys != null ? PoseValue(c.Keys[i], c.Channel) : c.ValueKeys[i].Value;
                    // Rotation blends the short way round: unwrap so the curve shows that.
                    if (c.Channel == GraphChannel.Rotation && i > 0)
                        v = c.Values[i - 1] + Mathf.DeltaAngle(c.Values[i - 1], v);
                    c.Values[i] = v;
                }
                c.Min = float.MaxValue;
                c.Max = float.MinValue;
                for (int i = 0; i < c.Count; i++)
                {
                    // Include what Bezier handles reach so they stay on screen.
                    float lo = c.Values[i], hi = c.Values[i];
                    if (i + 1 < c.Count && c.Ease(i) == (byte)SpriteEaseMode.Bezier)
                    {
                        var h = c.Curve(i);
                        float d = c.Values[i + 1] - c.Values[i];
                        lo = Mathf.Min(lo, c.Values[i] + d * Mathf.Min(h.y, h.w));
                        hi = Mathf.Max(hi, c.Values[i] + d * Mathf.Max(h.y, h.w));
                    }
                    c.Min = Mathf.Min(c.Min, lo);
                    c.Max = Mathf.Max(c.Max, hi);
                }
                if (_graphDragKey != null && c.Channel == _graphDragChannel && ContainsKey(c, _graphDragKey))
                {
                    c.Min = _graphDragMin;
                    c.Max = _graphDragMax;
                }
                allMin = Mathf.Min(allMin, c.Min);
                allMax = Mathf.Max(allMax, c.Max);
            }
            if (!_partsGraphSharedScale)
                return;
            foreach (var c in curves)
            {
                c.Min = allMin;
                c.Max = allMax;
            }
        }

        static bool ContainsKey(GraphCurve c, object key)
        {
            for (int i = 0; i < c.Count; i++)
                if (ReferenceEquals(c.Key(i), key))
                    return true;
            return false;
        }

        static float GraphY(Rect area, GraphCurve c, float v)
        {
            float span = c.Max - c.Min;
            float u = span > 1e-5f ? (v - c.Min) / span : 0.5f;
            return Mathf.Lerp(area.yMax - 8f, area.y + 8f, u);
        }

        static float GraphValue(Rect area, GraphCurve c, float y)
        {
            float span = c.Max - c.Min;
            if (span <= 1e-5f)
                span = 1f;
            return c.Min + Mathf.InverseLerp(area.yMax - 8f, area.y + 8f, y) * span;
        }

        static float GraphX(Rect area, float time, float duration) => Mathf.Lerp(area.x, area.xMax, Mathf.Clamp01(time / duration));

        static float GraphEase(byte mode, Vector4 curve, float u)
            => mode == (byte)SpriteEaseMode.Bezier
                ? SpriteEase.EvaluateBezier(new float4(curve.x, curve.y, curve.z, curve.w), u)
                : SpriteEase.Evaluate((SpriteEaseMode)mode, u);

        void DrawGraphCurve(Rect area, GraphCurve c, float duration)
        {
            var points = new List<Vector3>();
            int n = c.Count;
            points.Add(new Vector3(area.x, GraphY(area, c, c.Values[0])));
            for (int i = 0; i < n; i++)
            {
                float x = GraphX(area, c.Time(i), duration);
                points.Add(new Vector3(x, GraphY(area, c, c.Values[i])));
                if (i + 1 >= n)
                    continue;
                float t0 = c.Time(i), t1 = c.Time(i + 1);
                bool held = c.Held || c.Ease(i) == (byte)SpriteEaseMode.Step;
                const int steps = 20;
                for (int s = 1; s < steps; s++)
                {
                    float u = s / (float)steps;
                    float e = held ? 0f : GraphEase(c.Ease(i), c.Curve(i), u);
                    points.Add(new Vector3(GraphX(area, Mathf.Lerp(t0, t1, u), duration),
                        GraphY(area, c, Mathf.LerpUnclamped(c.Values[i], c.Values[i + 1], e))));
                }
                if (held)
                    points.Add(new Vector3(GraphX(area, t1, duration), GraphY(area, c, c.Values[i])));
            }
            points.Add(new Vector3(area.xMax, GraphY(area, c, c.Values[n - 1])));
            Handles.color = c.Color;
            Handles.DrawAAPolyLine(2f, points.ToArray());

            for (int i = 0; i < n; i++)
            {
                var p = new Vector2(GraphX(area, c.Time(i), duration), GraphY(area, c, c.Values[i]));
                if (i + 1 < n && c.Ease(i) == (byte)SpriteEaseMode.Bezier && !c.Held)
                {
                    var q = new Vector2(GraphX(area, c.Time(i + 1), duration), GraphY(area, c, c.Values[i + 1]));
                    GraphHandles(area, c, i, duration, out var h1, out var h2);
                    Handles.color = new Color(c.Color.r, c.Color.g, c.Color.b, 0.55f);
                    Handles.DrawAAPolyLine(1.5f, p, h1);
                    Handles.DrawAAPolyLine(1.5f, q, h2);
                    Handles.color = Color.white;
                    Handles.DrawSolidDisc(h1, Vector3.forward, 3f);
                    Handles.DrawSolidDisc(h2, Vector3.forward, 3f);
                }
                bool dragging = ReferenceEquals(_graphDragKey, c.Key(i)) && _graphDragHandle == 0;
                bool selected = c.Keys != null && _partsSelectedKeys.Contains(c.Keys[i]);
                Handles.color = dragging || selected ? new Color(1f, 0.85f, 0.2f) : c.Color;
                Handles.DrawSolidDisc(p, Vector3.forward, dragging || selected ? 5f : 4f);
            }
        }

        /// <summary>Screen positions of key <paramref name="i"/>'s two Bezier handles toward key i + 1.</summary>
        static void GraphHandles(Rect area, GraphCurve c, int i, float duration, out Vector2 h1, out Vector2 h2)
        {
            var h = c.Curve(i);
            float t0 = c.Time(i), t1 = c.Time(i + 1);
            float v0 = c.Values[i], v1 = c.Values[i + 1];
            h1 = new Vector2(GraphX(area, Mathf.Lerp(t0, t1, h.x), duration), GraphY(area, c, Mathf.LerpUnclamped(v0, v1, h.y)));
            h2 = new Vector2(GraphX(area, Mathf.Lerp(t0, t1, h.z), duration), GraphY(area, c, Mathf.LerpUnclamped(v0, v1, h.w)));
        }

        void HandlePartsGraphInput(Rect area, List<GraphCurve> curves, float duration)
        {
            var evt = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (!area.Contains(evt.mousePosition) || (evt.button != 0 && evt.button != 1))
                        return;
                    if (!HitGraph(area, curves, duration, evt.mousePosition, out var curve, out int index, out int handle))
                        return;
                    if (evt.button == 1)
                    {
                        ShowGraphKeyMenu(curve, index);
                        evt.Use();
                        return;
                    }
                    RecordPartsUndo(handle == 0 ? "Edit Curve Key" : "Edit Curve Ease");
                    _graphDragKey = curve.Key(index);
                    _graphDragNext = index + 1 < curve.Count ? curve.Key(index + 1) : null;
                    _graphDragChannel = curve.Channel;
                    _graphDragHandle = handle;
                    _graphDragMin = curve.Min;
                    _graphDragMax = curve.Max;
                    if (curve.Keys != null)
                    {
                        ClearPartsKeySelection();
                        _partsSelectedKeys.Add(curve.Keys[index]);
                    }
                    GUIUtility.hotControl = id;
                    evt.Use();
                    Repaint();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id || _graphDragKey == null)
                        return;
                    DragGraph(area, curves, duration, evt.mousePosition);
                    evt.Use();
                    Repaint();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id)
                        return;
                    GUIUtility.hotControl = 0;
                    _graphDragKey = null;
                    _graphDragNext = null;
                    SaveDirty();
                    evt.Use();
                    Repaint();
                    break;
            }
        }

        bool HitGraph(Rect area, List<GraphCurve> curves, float duration, Vector2 mouse, out GraphCurve hitCurve, out int hitIndex, out int hitHandle)
        {
            hitCurve = null;
            hitIndex = -1;
            hitHandle = 0;
            float best = 8f;
            var candidates = new Vector2[3];
            foreach (var c in curves)
            {
                for (int i = 0; i < c.Count; i++)
                {
                    candidates[0] = new Vector2(GraphX(area, c.Time(i), duration), GraphY(area, c, c.Values[i]));
                    int count = 1;
                    if (i + 1 < c.Count && c.Ease(i) == (byte)SpriteEaseMode.Bezier && !c.Held)
                    {
                        GraphHandles(area, c, i, duration, out candidates[1], out candidates[2]);
                        count = 3;
                    }
                    for (int h = 0; h < count; h++)
                    {
                        float d = Vector2.Distance(candidates[h], mouse);
                        // Handles win ties: they sit on top of their key when the ease is flat there.
                        bool better = d < best || (h != 0 && hitCurve != null && hitHandle == 0 && d <= best + 0.5f);
                        if (!better)
                            continue;
                        best = d;
                        hitCurve = c;
                        hitIndex = i;
                        hitHandle = h;
                    }
                }
            }
            return hitCurve != null;
        }

        void DragGraph(Rect area, List<GraphCurve> curves, float duration, Vector2 mouse)
        {
            GraphCurve curve = null;
            int index = -1;
            foreach (var c in curves)
            {
                if (c.Channel != _graphDragChannel)
                    continue;
                for (int i = 0; i < c.Count && index < 0; i++)
                    if (ReferenceEquals(c.Key(i), _graphDragKey))
                        index = i;
                if (index >= 0)
                {
                    curve = c;
                    break;
                }
            }
            if (curve == null)
                return;
            curve.Min = _graphDragMin;
            curve.Max = _graphDragMax;
            float value = GraphValue(area, curve, mouse.y);
            if (_graphDragHandle == 0)
            {
                if (curve.Keys != null)
                    SetPoseValue(curve.Keys[index], curve.Channel, value);
                else
                    curve.ValueKeys[index].Value = curve.ValueKind == SpritePartsValueKind.IkBend ? (value < 0f ? -1f : 1f) : value;
                return;
            }
            if (index + 1 >= curve.Count)
                return;
            float t0 = curve.Time(index), t1 = curve.Time(index + 1);
            float v0 = curve.Values[index], v1 = curve.Values[index + 1];
            float time = Mathf.Lerp(0f, duration, Mathf.InverseLerp(area.x, area.xMax, mouse.x));
            float hx = t1 - t0 > 1e-6f ? Mathf.Clamp01((time - t0) / (t1 - t0)) : 0.5f;
            var h = curve.Curve(index);
            // A flat segment has no height to shape: only the timing handle moves.
            float hy = Mathf.Abs(v1 - v0) > 1e-5f ? (value - v0) / (v1 - v0) : (_graphDragHandle == 1 ? h.y : h.w);
            if (_graphDragHandle == 1)
            {
                h.x = hx;
                h.y = hy;
            }
            else
            {
                h.z = hx;
                h.w = hy;
            }
            SetCurve(curve, index, h);
        }

        static readonly Vector4 LinearHandles = new Vector4(1f / 3f, 1f / 3f, 2f / 3f, 2f / 3f);

        void ShowGraphKeyMenu(GraphCurve curve, int index)
        {
            var menu = new GenericMenu();
            menu.AddDisabledItem(new GUIContent(curve.Label + " = " + curve.Values[index].ToString("0.###")
                                                + " at " + curve.Time(index).ToString("0.###") + "s"));
            menu.AddSeparator(string.Empty);
            byte current = curve.Ease(index);
            bool own = curve.OwnCurve(index);
            if (curve.Keys != null)
            {
                var poseKey = curve.Keys[index];
                menu.AddItem(new GUIContent("Separate Curves"), poseKey.Separate.On, () =>
                {
                    RecordPartsUndo("Separate Curves");
                    if (poseKey.Separate.On)
                        poseKey.Separate.On = false;
                    else
                    {
                        // Start every channel from what the key does now, so nothing moves until a handle is dragged.
                        if (poseKey.EaseMode != (byte)SpriteEaseMode.Bezier)
                            poseKey.Curve = LinearHandles;
                        poseKey.EaseMode = (byte)SpriteEaseMode.Bezier;
                        poseKey.Separate = SpritePartsSeparateCurves.From(poseKey.Curve);
                    }
                    SaveDirty();
                    Repaint();
                });
                menu.AddSeparator(string.Empty);
            }
            AddEase("Linear", SpriteEaseMode.Linear, LinearHandles);
            AddEase("Stepped", SpriteEaseMode.Step, default);
            AddEase("Bezier", SpriteEaseMode.Bezier, current == (byte)SpriteEaseMode.Bezier ? curve.Curve(index) : LinearHandles);
            AddEase("Presets/Ease In Out", SpriteEaseMode.Bezier, new Vector4(0.42f, 0f, 0.58f, 1f));
            AddEase("Presets/Ease In", SpriteEaseMode.Bezier, new Vector4(0.42f, 0f, 1f, 1f));
            AddEase("Presets/Ease Out", SpriteEaseMode.Bezier, new Vector4(0f, 0f, 0.58f, 1f));
            AddEase("Presets/Overshoot", SpriteEaseMode.Bezier, new Vector4(0.3f, 0f, 0.6f, 1.35f));
            AddEase("Presets/Anticipate", SpriteEaseMode.Bezier, new Vector4(0.4f, -0.35f, 0.7f, 1f));
            menu.ShowAsContext();

            void AddEase(string label, SpriteEaseMode mode, Vector4 handles)
            {
                bool on = label == "Bezier" ? current == (byte)SpriteEaseMode.Bezier : !label.StartsWith("Presets") && current == (byte)mode;
                object key = curve.Key(index);
                menu.AddItem(new GUIContent(own ? label + " (this channel)" : label), on, () =>
                {
                    RecordPartsUndo("Curve Ease");
                    if (own && mode != SpriteEaseMode.Step)
                    {
                        // Separate curves: only this channel's handles change (linear = straight handles).
                        SetCurve(curve, index, mode == SpriteEaseMode.Linear ? LinearHandles : handles);
                    }
                    else if (key is SpritePartsKeyDef pose)
                    {
                        pose.EaseMode = (byte)mode;
                        if (mode == SpriteEaseMode.Bezier)
                            pose.Curve = handles;
                        if (mode == SpriteEaseMode.Step)
                            pose.Separate.On = false;
                    }
                    else if (key is SpritePartsValueKeyDef value)
                    {
                        value.EaseMode = (byte)mode;
                        if (mode == SpriteEaseMode.Bezier)
                            value.Curve = handles;
                    }
                    SaveDirty();
                    Repaint();
                });
            }
        }
    }
}

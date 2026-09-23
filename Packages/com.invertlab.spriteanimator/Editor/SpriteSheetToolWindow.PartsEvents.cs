using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Parts clip events (footstep, hit, spawn an effect), on the time ruler:
    //   right-click the ruler   Add Event > a type (or a new type)
    //   click / drag a marker   select it / move it in time; right-click it to delete
    //   EVENT section           type, time, Int / Float / Text values
    //   playback                a passed event flashes its name on the canvas
    // Types are the profile's event list, shared with Frame clips; the game reads them from SpriteAnimEventBuffer.
    public sealed partial class SpriteSheetToolWindow
    {
        int _partsSelectedEvent = -1;
        bool _partsEventDragging;
        int _partsEventControl;
        float _partsEventDragStartX;
        float _partsEventDragStartTime;
        readonly List<(string name, Color color, double until)> _partsEventFlash = new List<(string, Color, double)>();

        SpriteEventDef PartsEventType(byte id)
            => _profile?.Events?.Find(e => e != null && e.Id == id);

        string PartsEventName(byte id) => PartsEventType(id)?.Name ?? ("Event " + id);

        Color PartsEventColor(byte id) => PartsEventType(id)?.Color ?? new Color(0.35f, 0.85f, 1f);

        SpritePartsEventMarker SelectedPartsEvent
        {
            get
            {
                var events = CurrentPartsClip?.Events;
                return events != null && (uint)_partsSelectedEvent < (uint)events.Count ? events[_partsSelectedEvent] : null;
            }
        }

        /// <summary>Mouse on the ruler's event markers. Returns true when it used the event (no scrub then).</summary>
        bool HandlePartsEventMarkers(Rect ruler, SpritePartsClipDef clip, float duration)
        {
            var evt = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive); // every pass, so ids stay in step
            if (_partsEventDragging && GUIUtility.hotControl == _partsEventControl)
            {
                if (evt.rawType == EventType.MouseDrag && (uint)_partsSelectedEvent < (uint)clip.Events.Count)
                {
                    float t = _partsEventDragStartTime + (evt.mousePosition.x - _partsEventDragStartX) / Mathf.Max(1f, ruler.width) * duration;
                    t = evt.shift ? Mathf.Clamp(t, 0f, duration) : SpritePartsAuthoringOps.SnapTime(t, _partsDisplayFps, duration);
                    clip.Events[_partsSelectedEvent].Time = t;
                    _partsPreviewTime = t;
                    if (_asset != null)
                        EditorUtility.SetDirty(_asset);
                    evt.Use();
                    Repaint();
                    return true;
                }
                if (evt.rawType == EventType.MouseUp)
                {
                    _partsEventDragging = false;
                    GUIUtility.hotControl = 0;
                    EndPartsDragUndo();
                    evt.Use();
                    return true;
                }
            }
            if (evt.type != EventType.MouseDown || !ruler.Contains(evt.mousePosition) || clip == null)
                return false;
            clip.Events ??= new List<SpritePartsEventMarker>();
            int hit = -1;
            for (int i = 0; i < clip.Events.Count; i++)
            {
                float x = Mathf.Lerp(ruler.x, ruler.xMax, clip.Events[i].Time / duration);
                if (Mathf.Abs(evt.mousePosition.x - x) <= 6f && evt.mousePosition.y >= ruler.yMax - 14f)
                    hit = i;
            }
            float time = Mathf.Clamp01(Mathf.InverseLerp(ruler.x, ruler.xMax, evt.mousePosition.x)) * duration;
            if (evt.button == 1)
            {
                var menu = new GenericMenu();
                if (hit >= 0)
                {
                    int index = hit;
                    menu.AddItem(new GUIContent("Delete Event"), false, () => DeletePartsEvent(index));
                    menu.AddSeparator(string.Empty);
                }
                foreach (var type in _profile.Events ?? new List<SpriteEventDef>())
                {
                    if (type == null) continue;
                    byte id = type.Id;
                    menu.AddItem(new GUIContent("Add Event/" + type.Name), false, () => AddPartsEvent(time, id));
                }
                menu.AddItem(new GUIContent("Add Event/New Type..."), false, () => AddPartsEvent(time, NewPartsEventType()));
                menu.ShowAsContext();
                evt.Use();
                return true;
            }
            if (evt.button != 0 || hit < 0)
                return false;
            _partsSelectedEvent = hit;
            _partsPreviewTime = clip.Events[hit].Time;
            _partsPlaying = false;
            BeginPartsDragUndo("Move Event");
            FlushPartsDragUndo();
            _partsEventDragging = true;
            _partsEventDragStartX = evt.mousePosition.x;
            _partsEventDragStartTime = clip.Events[hit].Time;
            _partsEventControl = control;
            GUIUtility.hotControl = control;
            evt.Use();
            Repaint();
            return true;
        }

        /// <summary>The markers: a coloured flag at the foot of the ruler, the selected one outlined.</summary>
        void DrawPartsEventMarkers(Rect ruler, SpritePartsClipDef clip, float duration)
        {
            if (clip?.Events == null || Event.current.type != EventType.Repaint)
                return;
            for (int i = 0; i < clip.Events.Count; i++)
            {
                var ev = clip.Events[i];
                float x = Mathf.Lerp(ruler.x, ruler.xMax, ev.Time / duration);
                var color = PartsEventColor(ev.EventId);
                EditorGUI.DrawRect(new Rect(x - 0.5f, ruler.yMax - 14f, 1.5f, 14f), color);
                Handles.BeginGUI();
                Handles.color = color;
                Handles.DrawAAConvexPolygon(new Vector3(x, ruler.yMax - 14f), new Vector3(x + 7f, ruler.yMax - 10.5f), new Vector3(x, ruler.yMax - 7f));
                if (i == _partsSelectedEvent)
                {
                    Handles.color = Color.white;
                    Handles.DrawAAPolyLine(1.5f, new Vector3(x, ruler.yMax - 15f), new Vector3(x + 8f, ruler.yMax - 10.5f),
                        new Vector3(x, ruler.yMax - 6f), new Vector3(x, ruler.yMax - 15f));
                }
                Handles.EndGUI();
                var hover = new Rect(x - 6f, ruler.yMax - 14f, 14f, 14f);
                if (hover.Contains(Event.current.mousePosition))
                    GUI.Label(new Rect(x + 9f, ruler.yMax - 15f, 140f, 14f), PartsEventName(ev.EventId), _mutedStyle);
            }
        }

        void AddPartsEvent(float time, byte id)
        {
            var clip = CurrentPartsClip;
            if (clip == null || id == 0)
                return;
            RecordPartsUndo("Add Event");
            clip.Events ??= new List<SpritePartsEventMarker>();
            float t = SpritePartsAuthoringOps.SnapTime(time, _partsDisplayFps, Mathf.Max(1e-3f, clip.Duration));
            clip.Events.Add(new SpritePartsEventMarker { Time = t, EventId = id });
            clip.Events.Sort((a, b) => a.Time.CompareTo(b.Time));
            _partsSelectedEvent = clip.Events.FindIndex(e => Mathf.Approximately(e.Time, t) && e.EventId == id);
            SaveDirty();
            _status = PartsEventName(id) + " event at " + t.ToString("0.###") + "s.";
            Repaint();
        }

        void DeletePartsEvent(int index)
        {
            var clip = CurrentPartsClip;
            if (clip?.Events == null || (uint)index >= (uint)clip.Events.Count)
                return;
            RecordPartsUndo("Delete Event");
            clip.Events.RemoveAt(index);
            _partsSelectedEvent = -1;
            SaveDirty();
            Repaint();
        }

        /// <summary>A new event type on the profile's list (ids 1..249; 250+ are reserved for clip start / complete).</summary>
        byte NewPartsEventType()
        {
            _profile.Events ??= new List<SpriteEventDef>();
            byte id = 1;
            while (id < SpriteAnimLifecycleId.Start && _profile.Events.Exists(e => e != null && e.Id == id))
                id++;
            if (id >= SpriteAnimLifecycleId.Start)
            {
                _status = "The event list is full.";
                return 0;
            }
            RecordPartsUndo("New Event Type");
            var palette = new[] { new Color(0.35f, 0.85f, 1f), new Color(1f, 0.6f, 0.25f), new Color(0.5f, 1f, 0.45f),
                new Color(1f, 0.45f, 0.7f), new Color(0.85f, 0.75f, 1f), new Color(1f, 0.9f, 0.35f) };
            _profile.Events.Add(new SpriteEventDef { Id = id, Name = "Event " + id, Color = palette[id % palette.Length] });
            SaveDirty();
            return id;
        }

        /// <summary>EVENT section (Animate) for the selected marker.</summary>
        void DrawPartsEventInspector()
        {
            if (_partsMode != SpritePartsStudioMode.Animate)
                return;
            var ev = SelectedPartsEvent;
            if (ev == null)
                return;
            var clip = CurrentPartsClip;
            if (!PartsSection("EVENT", PartsEventName(ev.EventId) + " at " + ev.Time.ToString("0.###") + "s"))
                return;
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Delete", "Remove this event"), EditorStyles.miniButton, GUILayout.Width(50f)))
            {
                DeletePartsEvent(_partsSelectedEvent);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            var types = _profile.Events ?? new List<SpriteEventDef>();
            var names = new List<string>();
            var ids = new List<byte>();
            foreach (var t in types)
            {
                if (t == null) continue;
                names.Add(t.Name);
                ids.Add(t.Id);
            }
            EditorGUI.BeginChangeCheck();
            int typeIndex = EditorGUILayout.Popup(new GUIContent("Type", "The event's name and id (shared with Frame clips)"),
                Mathf.Max(0, ids.IndexOf(ev.EventId)), names.ToArray());
            var type = PartsEventType(ev.EventId);
            string typeName = type != null ? EditorGUILayout.TextField(new GUIContent("Type Name", "Renames this type everywhere"), type.Name) : null;
            Color typeColor = type != null ? EditorGUILayout.ColorField(new GUIContent("Type Colour"), type.Color) : Color.white;
            float time = EditorGUILayout.FloatField(new GUIContent("Time (s)"), ev.Time);
            int iv = EditorGUILayout.IntField(new GUIContent("Int", "Sent with the event (SpriteAnimEventBuffer.IntPayload)"), ev.IntPayload);
            float fv = EditorGUILayout.FloatField(new GUIContent("Float", "Sent with the event (FloatPayload)"), ev.FloatPayload);
            string tv = EditorGUILayout.TextField(new GUIContent("Text", "Sent as a hash (TextHash = SpriteAnimSetBuilder.Fnv(text))"), ev.TextPayload ?? string.Empty);
            EditorGUILayout.BeginHorizontal();
            var audio = (AudioClip)EditorGUILayout.ObjectField(new GUIContent("Audio", "Played when the event fires (2D)"), ev.Audio, typeof(AudioClip), false);
            using (new EditorGUI.DisabledScope(ev.Audio == null))
            {
                if (GUILayout.Button(new GUIContent("▶", "Listen"), EditorStyles.miniButton, GUILayout.Width(24f)))
                    PreviewPartsEventAudio(ev.Audio);
            }
            EditorGUILayout.EndHorizontal();
            float volume = ev.Volume, balance = ev.Balance;
            if (ev.Audio != null)
            {
                volume = EditorGUILayout.Slider(new GUIContent("Volume"), ev.Volume, 0f, 1f);
                balance = EditorGUILayout.Slider(new GUIContent("Balance", "Stereo pan: -1 left, 1 right"), ev.Balance, -1f, 1f);
            }
            if (EditorGUI.EndChangeCheck())
            {
                RecordPartsUndo("Edit Event");
                if (typeIndex >= 0 && typeIndex < ids.Count)
                    ev.EventId = ids[typeIndex];
                if (type != null && typeName != null)
                {
                    type.Name = typeName;
                    type.Color = typeColor;
                }
                ev.Time = Mathf.Clamp(time, 0f, Mathf.Max(1e-3f, clip.Duration));
                ev.IntPayload = iv;
                ev.FloatPayload = fv;
                ev.TextPayload = tv;
                ev.Audio = audio;
                ev.Volume = volume;
                ev.Balance = balance;
                SaveDirty();
                Repaint();
            }
            EditorGUILayout.LabelField("In the game: SpriteAnimEvents.Raised (Id = " + ev.EventId + ", FrameIndex = -1), or read SpriteAnimEventBuffer.",
                EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>Preview: events the playhead passed this tick flash their names on the canvas.</summary>
        void FlashPartsEvents(SpritePartsClipDef clip, float from, float to)
        {
            if (clip?.Events == null || clip.Events.Count == 0)
                return;
            bool loop = clip.WrapMode != (byte)SpritePartsWrap.Once;
            double until = EditorApplication.timeSinceStartup + 0.7;
            foreach (var ev in clip.Events)
            {
                bool hit = to > from
                    ? (from <= 1e-6f ? ev.Time >= from : ev.Time > from) && ev.Time <= to
                    : loop && to < from && (ev.Time > from || ev.Time <= to);
                if (!hit)
                    continue;
                _partsEventFlash.Add((PartsEventName(ev.EventId), PartsEventColor(ev.EventId), until));
                if (ev.Audio != null)
                    PreviewPartsEventAudio(ev.Audio);
            }
        }

        /// <summary>Plays a clip in the editor (Unity's AudioUtil preview, found by reflection; silent if missing).</summary>
        static void PreviewPartsEventAudio(AudioClip clip)
        {
            if (clip == null)
                return;
            var util = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            var play = util?.GetMethod("PlayPreviewClip", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public,
                null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
            play?.Invoke(null, new object[] { clip, 0, false });
        }

        void DrawPartsEventFlash(Rect canvas)
        {
            if (_partsEventFlash.Count == 0)
                return;
            double now = EditorApplication.timeSinceStartup;
            _partsEventFlash.RemoveAll(f => f.until < now);
            float y = canvas.y + 34f;
            foreach (var (name, color, until) in _partsEventFlash)
            {
                float a = Mathf.Clamp01((float)(until - now) / 0.3f);
                var r = new Rect(canvas.x + 10f, y, 150f, 20f);
                EditorGUI.DrawRect(r, new Color(color.r * 0.3f, color.g * 0.3f, color.b * 0.3f, 0.85f * a));
                EditorGUI.DrawRect(new Rect(r.x, r.y, 4f, r.height), new Color(color.r, color.g, color.b, a));
                GUI.Label(new Rect(r.x + 8f, r.y + 2f, r.width - 10f, 16f), "▶ " + name, _mutedStyle);
                y += 22f;
            }
            Repaint();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    public sealed partial class SpriteSheetToolWindow
    {

        void DrawSocketInheritOverlay()
        {
            if (!_showSocketInheritPanel)
                return;
            var clip = SocketInheritClip();
            if (clip?.Frames == null)
                return;

            float width = Mathf.Clamp(_socketInheritPanelRect.width, 360f, Mathf.Max(360f, position.width - 16f));
            float height = Mathf.Clamp(_socketInheritPanelRect.height, 320f, Mathf.Max(320f, position.height - 24f));
            float x = Mathf.Clamp(_socketInheritPanelRect.x, 8f, Mathf.Max(8f, position.width - width - 8f));
            float y = Mathf.Clamp(_socketInheritPanelRect.y, 8f, Mathf.Max(8f, position.height - height - 8f));
            _socketInheritPanelRect = new Rect(x, y, width, height);

            var evt = Event.current;
            var shade = new Rect(Vector2.zero, position.size);
            int controlId = GUIUtility.GetControlID(
                "SpriteSocketInheritOverlay".GetHashCode(), FocusType.Passive, shade);
            bool owns = GUIUtility.hotControl == controlId;

            if (evt.type == EventType.Repaint)
                EditorGUI.DrawRect(shade, new Color(0f, 0f, 0f, 0.45f));

            var title = new Rect(_socketInheritPanelRect.x, _socketInheritPanelRect.y, width, 26f);
            if (evt.GetTypeForControl(controlId) == EventType.MouseDown && evt.button == 0 &&
                title.Contains(evt.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                _socketInheritDragging = true;
                _socketInheritDragOffset = evt.mousePosition - _socketInheritPanelRect.position;
                GUI.FocusControl(null);
                evt.Use();
            }
            else if (evt.GetTypeForControl(controlId) == EventType.MouseDrag && owns && _socketInheritDragging)
            {
                _socketInheritPanelRect.position = evt.mousePosition - _socketInheritDragOffset;
                evt.Use();
                Repaint();
            }
            else if (evt.GetTypeForControl(controlId) == EventType.MouseUp && owns && _socketInheritDragging)
            {
                GUIUtility.hotControl = 0;
                _socketInheritDragging = false;
                evt.Use();
            }

            EditorGUI.DrawRect(_socketInheritPanelRect, new Color(0.09f, 0.11f, 0.14f, 0.98f));
            DrawBorder(_socketInheritPanelRect, AccentColor, 2f);
            EditorGUI.DrawRect(title, new Color(0.14f, 0.22f, 0.3f, 1f));
            GUI.Label(new Rect(title.x + 8f, title.y + 4f, title.width - 16f, 18f),
                "Socket Frames", EditorStyles.boldLabel);

            var body = new Rect(
                _socketInheritPanelRect.x + 8f,
                _socketInheritPanelRect.y + 30f,
                _socketInheritPanelRect.width - 16f,
                _socketInheritPanelRect.height - 38f);
            GUILayout.BeginArea(body);
            DrawSocketInheritContents(clip);
            GUILayout.EndArea();

            EventType forControl = evt.GetTypeForControl(controlId);
            if (forControl == EventType.MouseDown && !_socketInheritDragging)
            {
                if (!_socketInheritPanelRect.Contains(evt.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    GUI.FocusControl(null);
                    CloseSocketInheritPanel();
                    evt.Use();
                    Repaint();
                }
                return;
            }

            if (forControl == EventType.MouseUp && owns)
            {
                GUIUtility.hotControl = 0;
                evt.Use();
            }
            else if (forControl is EventType.MouseDrag or EventType.ScrollWheel or EventType.ContextClick)
            {
                evt.Use();
            }
        }

        void DrawSocketTransformOverlay()
        {
            if (!_showSocketTransformPanel)
                return;
            var clip = CurrentClip;
            if (clip == null || _socketTransformNames.Count == 0)
            {
                CloseSocketTransformPanel();
                return;
            }

            float width = Mathf.Clamp(_socketTransformPanelRect.width, 280f, Mathf.Max(280f, position.width - 16f));
            float height = Mathf.Clamp(_socketTransformPanelRect.height, 300f, Mathf.Max(300f, position.height - 24f));
            float x = Mathf.Clamp(_socketTransformPanelRect.x, 8f, Mathf.Max(8f, position.width - width - 8f));
            float y = Mathf.Clamp(_socketTransformPanelRect.y, 8f, Mathf.Max(8f, position.height - height - 8f));
            _socketTransformPanelRect = new Rect(x, y, width, height);

            var evt = Event.current;
            var shade = new Rect(Vector2.zero, position.size);
            int controlId = GUIUtility.GetControlID(
                "SpriteSocketTransformOverlay".GetHashCode(), FocusType.Passive, shade);
            bool owns = GUIUtility.hotControl == controlId;

            if (evt.type == EventType.Repaint)
                EditorGUI.DrawRect(shade, new Color(0f, 0f, 0f, 0.45f));

            var title = new Rect(_socketTransformPanelRect.x, _socketTransformPanelRect.y, width, 26f);
            if (evt.GetTypeForControl(controlId) == EventType.MouseDown && evt.button == 0 &&
                title.Contains(evt.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                _socketTransformDragging = true;
                _socketTransformDragOffset = evt.mousePosition - _socketTransformPanelRect.position;
                GUI.FocusControl(null);
                evt.Use();
            }
            else if (evt.GetTypeForControl(controlId) == EventType.MouseDrag && owns && _socketTransformDragging)
            {
                _socketTransformPanelRect.position = evt.mousePosition - _socketTransformDragOffset;
                evt.Use();
                Repaint();
            }
            else if (evt.GetTypeForControl(controlId) == EventType.MouseUp && owns && _socketTransformDragging)
            {
                GUIUtility.hotControl = 0;
                _socketTransformDragging = false;
                evt.Use();
            }

            EditorGUI.DrawRect(_socketTransformPanelRect, new Color(0.09f, 0.11f, 0.14f, 0.98f));
            DrawBorder(_socketTransformPanelRect, AccentColor, 2f);
            EditorGUI.DrawRect(title, new Color(0.14f, 0.22f, 0.3f, 1f));
            GUI.Label(new Rect(title.x + 8f, title.y + 4f, title.width - 16f, 18f),
                "Set Transform", EditorStyles.boldLabel);

            var body = new Rect(
                _socketTransformPanelRect.x + 10f,
                _socketTransformPanelRect.y + 32f,
                _socketTransformPanelRect.width - 20f,
                _socketTransformPanelRect.height - 42f);
            GUILayout.BeginArea(body);
            DrawSocketTransformContents(clip);
            GUILayout.EndArea();

            EventType forControl = evt.GetTypeForControl(controlId);
            if (forControl == EventType.MouseDown && !_socketTransformDragging)
            {
                if (!_socketTransformPanelRect.Contains(evt.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    GUI.FocusControl(null);
                    CloseSocketTransformPanel();
                    evt.Use();
                    Repaint();
                }
                return;
            }

            if (forControl == EventType.MouseUp && owns)
            {
                GUIUtility.hotControl = 0;
                evt.Use();
            }
            else if (forControl is EventType.MouseDrag or EventType.ScrollWheel or EventType.ContextClick)
            {
                evt.Use();
            }
        }

        void DrawSocketTransformContents(SpriteClipDef clip)
        {
            if (clip?.Sockets == null || _socketTransformNames.Count == 0)
            {
                GUILayout.Label("No sockets.", _mutedStyle);
                return;
            }

            _profile.EnsureSocketCatalog();
            string label = _socketTransformNames.Count == 1
                ? _socketTransformNames[0]
                : $"{_socketTransformNames.Count} sockets";
            GUILayout.Label($"{label}  •  {clip.Name}  •  frame {_selectedFrame + 1}", EditorStyles.boldLabel);
            GUILayout.Label("Pose is per frame. Pivot is the preview art grip (0–1).", _mutedStyle);

            Vector2 position = Vector2.zero;
            float angle = 0f;
            Vector2 scale = Vector2.one;
            Vector2 pivot = new Vector2(0.5f, 0.5f);
            bool mixedPos = false;
            bool mixedAngle = false;
            bool mixedScale = false;
            bool mixedPivot = false;
            bool hasPose = false;
            bool hasPivot = false;
            for (int i = 0; i < _socketTransformNames.Count; i++)
            {
                string name = _socketTransformNames[i];
                if (SpriteSocketKeys.TryGetPose(clip.Sockets, name, _selectedFrame,
                        out var pose, out var poseAngle, out var poseScale, out _))
                {
                    if (!hasPose)
                    {
                        position = pose;
                        angle = poseAngle;
                        scale = SpriteSocketKeys.ResolvedScale(poseScale);
                        hasPose = true;
                    }
                    else
                    {
                        if (pose != position)
                            mixedPos = true;
                        if (!Mathf.Approximately(poseAngle, angle))
                            mixedAngle = true;
                        if (SpriteSocketKeys.ResolvedScale(poseScale) != scale)
                            mixedScale = true;
                    }
                }

                var item = _profile.SocketCatalog.Find(name);
                Vector2 itemPivot = item != null ? item.Pivot : new Vector2(0.5f, 0.5f);
                if (!hasPivot)
                {
                    pivot = itemPivot;
                    hasPivot = true;
                }
                else if (itemPivot != pivot)
                    mixedPivot = true;
            }

            _socketTransformAllFrames = EditorGUILayout.Toggle(
                new GUIContent("All Frames",
                    "Write position, rotation, and scale onto every key of the selected sockets."),
                _socketTransformAllFrames);

            EditorGUI.showMixedValue = mixedPos;
            EditorGUI.BeginChangeCheck();
            Vector2 nextPos = EditorGUILayout.Vector2Field("Position (px)", position);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                WriteSocketTransformPose(clip, nextPos, angle, scale, writePos: true, writeAngle: false, writeScale: false);

            EditorGUI.showMixedValue = mixedAngle;
            EditorGUI.BeginChangeCheck();
            float nextAngle = EditorGUILayout.FloatField("Rotation (deg)", angle);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                WriteSocketTransformPose(clip, nextPos, nextAngle, scale, writePos: false, writeAngle: true, writeScale: false);

            EditorGUI.showMixedValue = mixedScale;
            EditorGUI.BeginChangeCheck();
            Vector2 nextScale = EditorGUILayout.Vector2Field("Scale", scale);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                WriteSocketTransformPose(clip, nextPos, nextAngle, nextScale,
                    writePos: false, writeAngle: false, writeScale: true);

            EditorGUI.showMixedValue = mixedPivot;
            EditorGUI.BeginChangeCheck();
            Vector2 nextPivot = EditorGUILayout.Vector2Field(
                new GUIContent("Pivot", "Normalized sprite pivot (0-1). Same for every frame."),
                pivot);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                WriteSocketTransformPivot(nextPivot);

            GUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Reset Pose",
                        "Position 0,0  •  rotation 0°  •  scale 1,1")))
                    WriteSocketTransformPose(clip, Vector2.zero, 0f, Vector2.one,
                        writePos: true, writeAngle: true, writeScale: true);
                if (GUILayout.Button(new GUIContent("Reset Pivot", "Pivot 0.5, 0.5")))
                    WriteSocketTransformPivot(new Vector2(0.5f, 0.5f));
            }
            if (GUILayout.Button("Close"))
            {
                CloseSocketTransformPanel();
                GUIUtility.ExitGUI();
            }
        }

        void WriteSocketTransformPose(SpriteClipDef clip, Vector2 position, float angle, Vector2 scale,
            bool writePos, bool writeAngle, bool writeScale)
        {
            if (clip == null || _socketTransformNames.Count == 0)
                return;
            RecordProfileUndo("Set Socket Transform");
            scale = SpriteSocketKeys.ResolvedScale(scale);
            for (int n = 0; n < _socketTransformNames.Count; n++)
            {
                string name = _socketTransformNames[n];
                if (_socketTransformAllFrames)
                {
                    SpriteSocketKeys.CollectKeysSorted(clip.Sockets, name, _socketPathKeys);
                    if (_socketPathKeys.Count == 0)
                    {
                        WriteSocketTransformKey(
                            SpriteSocketKeys.EnsureFrameKey(clip.Sockets, name, _selectedFrame),
                            position, angle, scale, writePos, writeAngle, writeScale);
                        continue;
                    }
                    for (int k = 0; k < _socketPathKeys.Count; k++)
                        WriteSocketTransformKey(_socketPathKeys[k], position, angle, scale,
                            writePos, writeAngle, writeScale);
                }
                else
                {
                    WriteSocketTransformKey(
                        SpriteSocketKeys.EnsureFrameKey(clip.Sockets, name, _selectedFrame),
                        position, angle, scale, writePos, writeAngle, writeScale);
                }
            }

            _status = _socketTransformAllFrames
                ? $"Set transform on {_socketTransformNames.Count} sockets  •  all frames"
                : $"Set transform on {_socketTransformNames.Count} sockets  •  frame {_selectedFrame + 1}";
            SaveDirty();
            Repaint();
        }

        static void WriteSocketTransformKey(FrameSocketDef key, Vector2 position, float angle, Vector2 scale,
            bool writePos, bool writeAngle, bool writeScale)
        {
            if (key == null)
                return;
            if (writePos)
                key.LocalPosition = new Vector2(Mathf.Round(position.x * 100f) / 100f, Mathf.Round(position.y * 100f) / 100f);
            if (writeAngle)
                key.LocalAngle = angle;
            if (writeScale)
                key.LocalScale = scale;
        }

        void WriteSocketTransformPivot(Vector2 pivot)
        {
            if (_socketTransformNames.Count == 0)
                return;
            RecordProfileUndo("Set Socket Pivot");
            pivot = new Vector2(Mathf.Clamp01(pivot.x), Mathf.Clamp01(pivot.y));
            _profile.EnsureSocketCatalog();
            for (int i = 0; i < _socketTransformNames.Count; i++)
                _profile.SocketCatalog.Ensure(_socketTransformNames[i]).Pivot = pivot;
            _status = $"Pivot {pivot.x:0.##}, {pivot.y:0.##}  •  {_socketTransformNames.Count} sockets";
            SaveDirty();
            Repaint();
        }

        static bool DrawSocketInheritChannelToggle(string label, string tooltip, bool on, GUIStyle style)
        {
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = on
                ? new Color(0.22f, 0.55f, 0.92f, 1f)
                : new Color(0.32f, 0.32f, 0.32f, 1f);
            bool next = GUILayout.Toggle(on, new GUIContent(label, tooltip), style);
            GUI.backgroundColor = prev;
            return next;
        }

        void DrawSocketInheritContents(SpriteClipDef clip)
        {
            if (clip?.Frames == null || _socketInheritNames.Count == 0)
            {
                GUILayout.Label("No clip.", _mutedStyle);
                return;
            }

            string socketLabel = _socketInheritNames.Count == 1
                ? _socketInheritNames[0]
                : $"{_socketInheritNames.Count} sockets";
            int source = Mathf.Clamp(_socketInheritSourceFrame, 0, clip.Frames.Length - 1);
            SpriteSocketKeys.TryGetPose(clip.Sockets, _socketInheritNames[0], source,
                out var sourcePos, out var sourceAngle, out var sourceScale, out bool sourceKeyed);
            bool sourceBehind = SocketInheritDrawsBehind(clip, _socketInheritNames[0], source);
            float sourceTime = AuthoredStartTime(clip, source);
            float sourceDur = FrameDuration(clip, source);
            GUILayout.Label($"{socketLabel}  •  {clip.Name}", EditorStyles.boldLabel);
            GUILayout.Label(
                $"Source  {sourceTime:0.00}s  ({sourceDur:0.00}s)  •  frame {source + 1}" +
                (sourceKeyed ? "  key" : "  inherited") +
                $"   ({sourcePos.x:0.#}, {sourcePos.y:0.#})  {sourceAngle:0.#}°  {sourceScale.x:0.##},{sourceScale.y:0.##}",
                _mutedStyle);
            GUILayout.Label(
                sourceBehind
                    ? "Now  Behind  (purple)  under the character"
                    : "Now  Front  (amber)  over the character",
                _mutedStyle);
            if (_selectedFrame != source)
            {
                float previewTime = AuthoredStartTime(clip, _selectedFrame);
                if (GUILayout.Button($"Use preview {previewTime:0.00}s as source", EditorStyles.miniButton))
                    _socketInheritSourceFrame = _selectedFrame;
            }

            int previewFrame = Mathf.Clamp(_selectedFrame, 0, clip.Frames.Length - 1);
            float drawTime = AuthoredStartTime(clip, previewFrame);
            bool previewBehind = SocketInheritDrawsBehind(clip, _socketInheritNames[0], previewFrame);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(
                        $"Draw Behind at {drawTime:0.00}s",
                        "Key this socket behind the character at the current preview time."),
                        EditorStyles.miniButtonLeft))
                    KeySocketDrawAtTime(clip, previewFrame, behind: true);
                if (GUILayout.Button(new GUIContent(
                        $"Draw Front at {drawTime:0.00}s",
                        "Key this socket in front of the character at the current preview time."),
                        EditorStyles.miniButtonRight))
                    KeySocketDrawAtTime(clip, previewFrame, behind: false);
            }
            GUILayout.Label(
                previewBehind
                    ? $"Current {drawTime:0.00}s is Behind"
                    : $"Current {drawTime:0.00}s is Front",
                _mutedStyle);

            using (new EditorGUILayout.HorizontalScope())
            {
                _socketInheritPosition = DrawSocketInheritChannelToggle(
                    "Position", "Copy Offset X/Y from the source time.",
                    _socketInheritPosition, EditorStyles.miniButtonLeft);
                _socketInheritRotation = DrawSocketInheritChannelToggle(
                    "Rotation", "Copy angle from the source time.",
                    _socketInheritRotation, EditorStyles.miniButtonMid);
                _socketInheritScale = DrawSocketInheritChannelToggle(
                    "Scale", "Copy Scale X/Y from the source time.",
                    _socketInheritScale, EditorStyles.miniButtonRight);
            }

            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("All", "Select every frame."), EditorStyles.miniButton))
                    SelectSocketInheritFrames(clip, "all");
                if (GUILayout.Button(new GUIContent("None", "Clear the frame selection."), EditorStyles.miniButton))
                    SelectSocketInheritFrames(clip, "none");
                if (GUILayout.Button(new GUIContent("Missing", "Times that have no key yet."), EditorStyles.miniButton))
                    SelectSocketInheritFrames(clip, "missing");
                if (GUILayout.Button(new GUIContent("This→End", "From the source time to the end of the clip."),
                        EditorStyles.miniButton))
                    SelectSocketInheritFrames(clip, "rest");
                if (GUILayout.Button(new GUIContent("Timeline", "Use the timeline selection."),
                        EditorStyles.miniButton))
                    SelectSocketInheritFrames(clip, "timeline");
            }

            GUILayout.Space(4f);
            GUILayout.Label(
                $"Time  •  {_socketInheritFrames.Count} selected  •  click a row to preview",
                _mutedStyle);
            _socketInheritScroll = GUILayout.BeginScrollView(_socketInheritScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < clip.Frames.Length; i++)
            {
                bool keyed = false;
                for (int n = 0; n < _socketInheritNames.Count; n++)
                {
                    if (SpriteSocketKeys.FindOnFrame(clip.Sockets, _socketInheritNames[n], i) != null)
                    {
                        keyed = true;
                        break;
                    }
                }
                bool chosen = _socketInheritFrames.Contains(i);
                bool isSource = i == source;
                bool behind = SocketInheritDrawsBehind(clip, _socketInheritNames[0], i);
                float time = AuthoredStartTime(clip, i);
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool nextChosen = GUILayout.Toggle(chosen, GUIContent.none, GUILayout.Width(18f));
                    if (nextChosen != chosen)
                        ToggleSocketInheritFrame(i, SelectionOp.Toggle);

                    var evt = Event.current;
                    var row = GUILayoutUtility.GetRect(1f, 22f, GUILayout.ExpandWidth(true));
                    var swatch = new Rect(row.xMax - 12f, row.y + 6f, 10f, 10f);
                    var labelRect = new Rect(row.x, row.y, Mathf.Max(8f, row.width - 16f), row.height);
                    string text = isSource
                        ? $"{time:0.00}s  source  {(behind ? "Behind" : "Front")}"
                        : keyed
                            ? $"{time:0.00}s  key  {(behind ? "Behind" : "Front")}"
                            : $"{time:0.00}s  inherit  {(behind ? "Behind" : "Front")}";
                    if (i == _selectedFrame)
                        EditorGUI.DrawRect(row, new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.18f));
                    else if (chosen)
                        EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.06f));
                    GUI.Label(labelRect, text, isSource ? EditorStyles.boldLabel : EditorStyles.label);
                    EditorGUI.DrawRect(swatch, behind ? SocketDrawBehindColor : SocketDrawFrontColor);
                    if (evt.type == EventType.MouseDown && evt.button == 0 && row.Contains(evt.mousePosition))
                    {
                        ToggleSocketInheritFrame(i, ReadSelectionOp(evt, orderedList: true));
                        JumpPreviewToFrame(clip, i);
                        _status = $"{socketLabel}  {time:0.00}s  (frame {i + 1})  •  " +
                                  (behind ? "Behind" : "In Front");
                        evt.Use();
                        GUI.FocusControl(null);
                    }
                }
            }
            GUILayout.EndScrollView();

            bool canApply = _socketInheritFrames.Count > 0 &&
                            (_socketInheritPosition || _socketInheritRotation || _socketInheritScale);
            using (new EditorGUI.DisabledScope(!canApply))
            {
                if (GUILayout.Button(new GUIContent("Apply to selected",
                        "Copy checked channels from the source time onto the checked times.")))
                {
                    int changed = ApplySocketInherit(clip, _socketInheritPosition, _socketInheritRotation,
                        _socketInheritScale, _socketInheritFrames, "Inherit Sprite Socket Pose");
                    _status = $"Inherited pose onto {changed} frame key{Plural(changed)}";
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Next",
                            "Copy checked channels onto the following time.")))
                    {
                        var next = new[] { source + 1 };
                        int changed = ApplySocketInherit(clip, _socketInheritPosition, _socketInheritRotation,
                            _socketInheritScale, next, "Copy Sprite Socket to Next Frame");
                        _status = changed > 0
                            ? $"Copied pose to {AuthoredStartTime(clip, source + 1):0.00}s"
                            : "No next frame";
                    }
                    if (GUILayout.Button(new GUIContent("Rest of clip",
                            "Copy checked channels from this time through the end of the clip.")))
                    {
                        var rest = new List<int>();
                        for (int i = source + 1; i < clip.Frames.Length; i++)
                            rest.Add(i);
                        int changed = ApplySocketInherit(clip, _socketInheritPosition, _socketInheritRotation,
                            _socketInheritScale, rest, "Copy Sprite Socket to Rest of Clip");
                        _status = $"Copied pose to {changed} frame key{Plural(changed)}";
                    }
                    if (GUILayout.Button(new GUIContent("Fill missing",
                            "Write keys only on times that do not have one yet.")))
                    {
                        SelectSocketInheritFrames(clip, "missing");
                        int changed = ApplySocketInherit(clip, _socketInheritPosition, _socketInheritRotation,
                            _socketInheritScale, _socketInheritFrames, "Fill Sprite Socket Missing Frames");
                        _status = $"Filled {changed} missing key{Plural(changed)}";
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Reset selected",
                            "Set checked channels to identity (0,0 / 0° / 1,1) on checked times.")))
                    {
                        int changed = ResetSocketInherit(clip, _socketInheritFrames);
                        _status = $"Reset {changed} frame key{Plural(changed)}";
                    }
                    if (GUILayout.Button(new GUIContent("Clear keys",
                            "Delete keys on checked times so they fall back to the last pose.")))
                    {
                        int changed = ClearSocketInheritKeys(clip, _socketInheritFrames);
                        _status = $"Cleared {changed} socket key{Plural(changed)}";
                    }
                }
            }

            if (GUILayout.Button("Close"))
                CloseSocketInheritPanel();
        }

        bool SocketInheritDrawsBehind(SpriteClipDef clip, string name, int frame)
        {
            var item = _profile?.SocketCatalog?.Find(name);
            return SpriteSocketKeys.IsDrawnBehind(
                clip?.Sockets, name, frame,
                SpriteSocketKeys.CatalogDrawsBehind(item),
                SocketSampleClosed(clip, name));
        }

        void PruneSocketDrawSelection(SpriteClipDef clip)
        {
            if (clip == null || string.IsNullOrEmpty(_selectedSocketDrawName) ||
                _selectedSocketDrawFrame < 0 || _selectedSocketDrawFrame >= clip.Frames.Length ||
                IsIndependentSocketName(_selectedSocketDrawName))
            {
                _selectedSocketDrawFrame = -1;
                _selectedSocketDrawName = null;
                return;
            }
            var key = SpriteSocketKeys.FindOnFrame(
                clip.Sockets, _selectedSocketDrawName, _selectedSocketDrawFrame);
            if (key == null || key.DrawLayer == SpriteSocketKeys.DrawUnset)
            {
                _selectedSocketDrawFrame = -1;
                _selectedSocketDrawName = null;
            }
        }

        void KeySocketDrawAtTime(SpriteClipDef clip, int frame, bool behind, string socketName = null)
        {
            if (clip?.Frames == null || frame < 0 || frame >= clip.Frames.Length)
                return;
            socketName = string.IsNullOrEmpty(socketName) ? null : SpriteSocketKeys.CanonicalName(socketName);
            bool fromInherit = string.IsNullOrEmpty(socketName) &&
                               _showSocketInheritPanel && _socketInheritNames.Count > 0;
            if (string.IsNullOrEmpty(socketName) && !fromInherit)
                socketName = SpriteSocketKeys.CanonicalName(_selectedSocketName);
            if (!fromInherit && string.IsNullOrEmpty(socketName))
            {
                _status = "Add a Frame-Attached socket first to place Socket Draw keys";
                return;
            }
            if (!fromInherit && IsIndependentSocketName(socketName))
            {
                _status = "Independent Motion draw keys belong on the Independent Motion timeline";
                return;
            }
            if (fromInherit)
            {
                bool anyAttached = false;
                for (int n = 0; n < _socketInheritNames.Count; n++)
                {
                    if (!IsIndependentSocketName(_socketInheritNames[n]))
                    {
                        anyAttached = true;
                        break;
                    }
                }
                if (!anyAttached)
                {
                    _status = "Independent Motion draw keys belong on the Independent Motion timeline";
                    return;
                }
            }
            RecordProfileUndo(behind ? "Draw Socket Behind" : "Draw Socket In Front");
            byte layer = behind ? SpriteSocketKeys.DrawBehind : SpriteSocketKeys.DrawFront;
            if (fromInherit)
            {
                for (int n = 0; n < _socketInheritNames.Count; n++)
                {
                    string inheritName = SpriteSocketKeys.CanonicalName(_socketInheritNames[n]);
                    if (IsIndependentSocketName(inheritName))
                        continue;
                    var key = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, inheritName, frame);
                    key.DrawLayer = layer;
                    if (string.IsNullOrEmpty(socketName))
                        socketName = inheritName;
                }
            }
            else
            {
                var key = SpriteSocketKeys.EnsureFrameKey(clip.Sockets, socketName, frame);
                key.DrawLayer = layer;
            }
            SelectSocketDrawKey(clip, frame, socketName);
            float time = AuthoredStartTime(clip, frame);
            _status = behind
                ? $"{socketName}  Behind at {time:0.00}s"
                : $"{socketName}  Front at {time:0.00}s";
            SaveDirty();
            Repaint();
        }

    }
}

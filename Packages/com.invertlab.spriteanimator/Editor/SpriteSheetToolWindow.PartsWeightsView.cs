using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Weights view (Spine's weights colours, taken further):
    //   Colors       every vertex is the blend of its bones' colours, smooth across the triangles
    //   Chosen Bone  a heat map of the chosen bone (blue 0% .. red 100%), smooth as well
    //   Pies         each vertex shows its split as a small pie of bone colours
    //   Brushes      Add / Remove / Replace (toward a value) / Smooth, with size and strength
    //   Lock         a locked bone's weights never change: brushes, Direct, Smooth, Prune and Auto leave them
    public sealed partial class SpriteSheetToolWindow
    {
        enum PartsWeightView
        {
            Colors = 0,
            ChosenBone = 1,
            Off = 2,
        }

        enum PartsWeightBrush
        {
            Add = 0,
            Remove = 1,
            Replace = 2,
            Smooth = 3,
        }

        [SerializeField] PartsWeightView _partsWeightView = PartsWeightView.Colors;
        [SerializeField] PartsWeightBrush _partsWeightBrush = PartsWeightBrush.Add;
        [SerializeField] float _partsWeightOpacity = 0.6f;
        [SerializeField] bool _partsWeightPies = true;
        [SerializeField] float _partsWeightReplaceValue = 1f;
        static Material s_partsWeightMaterial;

        static Material PartsWeightMaterial()
        {
            if (s_partsWeightMaterial != null)
                return s_partsWeightMaterial;
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
                return null;
            s_partsWeightMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            s_partsWeightMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            s_partsWeightMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            s_partsWeightMaterial.SetInt("_Cull", (int)CullMode.Off);
            s_partsWeightMaterial.SetInt("_ZWrite", 0);
            s_partsWeightMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            return s_partsWeightMaterial;
        }

        /// <summary>Blue (0) .. cyan .. green .. yellow .. red (1).</summary>
        static Color WeightHeat(float w)
        {
            w = Mathf.Clamp01(w);
            Color[] stops =
            {
                new Color(0.08f, 0.1f, 0.55f), new Color(0.1f, 0.55f, 1f), new Color(0.2f, 0.9f, 0.35f),
                new Color(1f, 0.9f, 0.15f), new Color(1f, 0.15f, 0.1f),
            };
            float f = w * (stops.Length - 1);
            int i = Mathf.Min(stops.Length - 2, Mathf.FloorToInt(f));
            return Color.Lerp(stops[i], stops[i + 1], f - i);
        }

        Color WeightVertexColor(SpritePartMeshDef mesh, int v)
        {
            int bones = mesh.BoneCount;
            if (_partsWeightView == PartsWeightView.ChosenBone)
                return WeightHeat((uint)_partsWeightBone < (uint)bones ? mesh.Weights[v * bones + _partsWeightBone] : 0f);
            Color c = Color.black;
            for (int b = 0; b < bones; b++)
                c += BoneColor(b) * mesh.Weights[v * bones + b];
            c.a = 1f;
            return c;
        }

        /// <summary>The weights as colour, interpolated across each triangle (GL vertex colours).</summary>
        void DrawWeightColors(Rect sprite, SpritePartMeshDef mesh)
        {
            if (Event.current.type != EventType.Repaint || _partsWeightView == PartsWeightView.Off || !mesh.HasWeights)
                return;
            var mat = PartsWeightMaterial();
            if (mat == null || !mat.SetPass(0))
                return;
            var colors = new Color[mesh.VertexCount];
            var points = new Vector2[mesh.VertexCount];
            float ppp = EditorGUIUtility.pixelsPerPoint;
            for (int v = 0; v < colors.Length; v++)
            {
                var c = WeightVertexColor(mesh, v);
                c.a = _partsWeightOpacity;
                colors[v] = c;
                Vector2 gui = GUI.matrix.MultiplyPoint(MeshUvToGui(sprite, mesh.Vertices[v]));
                points[v] = (GUIUtility.GUIToScreenPoint(gui) - position.position) * ppp;
            }
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, position.width * ppp, position.height * ppp, 0f);
            GL.Begin(GL.TRIANGLES);
            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int v = mesh.Triangles[t + k];
                    if ((uint)v >= (uint)colors.Length)
                        continue;
                    GL.Color(colors[v]);
                    GL.Vertex3(points[v].x, points[v].y, 0f);
                }
            }
            GL.End();
            GL.PopMatrix();
        }

        /// <summary>Each vertex as a pie of its bones' colours (the chosen bone's slice is outlined).</summary>
        void DrawWeightPies(Rect sprite, SpritePartMeshDef mesh)
        {
            if (Event.current.type != EventType.Repaint || !_partsWeightPies || !mesh.HasWeights)
                return;
            int bones = mesh.BoneCount;
            Handles.BeginGUI();
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                Vector2 p = MeshUvToGui(sprite, mesh.Vertices[v]);
                float radius = v == _partsWarpHover ? 9f : 6f;
                Handles.color = new Color(0f, 0f, 0f, 0.8f);
                Handles.DrawSolidDisc(p, Vector3.forward, radius + 1.5f);
                float angle = 0f;
                for (int b = 0; b < bones; b++)
                {
                    float w = mesh.Weights[v * bones + b];
                    if (w < 0.005f)
                        continue;
                    Handles.color = BoneColor(b);
                    Vector3 from = Quaternion.Euler(0f, 0f, angle) * Vector3.down;
                    Handles.DrawSolidArc(p, Vector3.forward, from, w * 360f, radius);
                    angle += w * 360f;
                }
            }
            Handles.EndGUI();
        }

        /// <summary>Hover: the vertex's bones with their share, in their colours.</summary>
        void DrawWeightHover(Rect sprite, SpritePartMeshDef mesh)
        {
            if (!mesh.HasWeights || (uint)_partsWarpHover >= (uint)mesh.VertexCount)
                return;
            int bones = mesh.BoneCount;
            Vector2 p = MeshUvToGui(sprite, mesh.Vertices[_partsWarpHover]);
            float y = p.y + 12f;
            var back = new Rect(p.x + 12f, y - 2f, 170f, 4f);
            int rows = 0;
            for (int b = 0; b < bones; b++)
                if (mesh.Weights[_partsWarpHover * bones + b] >= 0.005f) rows++;
            back.height = rows * 16f + 4f;
            EditorGUI.DrawRect(back, new Color(0f, 0f, 0f, 0.7f));
            for (int b = 0; b < bones; b++)
            {
                float w = mesh.Weights[_partsWarpHover * bones + b];
                if (w < 0.005f)
                    continue;
                EditorGUI.DrawRect(new Rect(p.x + 16f, y + 3f, 10f, 10f), BoneColor(b));
                GUI.Label(new Rect(p.x + 30f, y, 150f, 16f),
                    Mathf.RoundToInt(w * 100f) + "%  " + BoneDisplayName(mesh.Bones[b])
                    + (SpritePartsSkinning.IsLocked(mesh.LockedBones, b) ? "  (locked)" : ""), _mutedStyle);
                y += 16f;
            }
        }

        string BoneDisplayName(string slotId)
            => SpritePartsAuthoringOps.FindSlot(_profile, slotId ?? string.Empty)?.Name ?? slotId;

        /// <summary>How much of the mesh a bone moves: its average weight over the vertices.</summary>
        static float BoneInfluence(SpritePartMeshDef mesh, int bone)
        {
            if (!mesh.HasWeights || mesh.VertexCount == 0)
                return 0f;
            float sum = 0f;
            for (int v = 0; v < mesh.VertexCount; v++)
                sum += mesh.Weights[v * mesh.BoneCount + bone];
            return sum / mesh.VertexCount;
        }

        void ToggleWeightLock(int bone)
        {
            var mesh = WeightsSlot()?.Mesh;
            if (mesh == null || (uint)bone >= (uint)mesh.BoneCount)
                return;
            bool locked = SpritePartsSkinning.IsLocked(mesh.LockedBones, bone);
            RecordPartsUndo(locked ? "Unlock Bone Weights" : "Lock Bone Weights");
            mesh.LockedBones ^= 1 << bone;
            SaveDirty();
            _status = BoneDisplayName(mesh.Bones[bone]) + (locked ? " unlocked." : " locked: its weights stay as they are.");
        }

        /// <summary>The bone rows (colour, name, share, lock, unbind) and the view / brush options.</summary>
        void DrawWeightBoneRows(SpritePartMeshDef mesh)
        {
            var lockOn = EditorGUIUtility.IconContent("IN LockButton on");
            var lockOff = EditorGUIUtility.IconContent("IN LockButton");
            for (int b = 0; b < mesh.BoneCount; b++)
            {
                EditorGUILayout.BeginHorizontal();
                var swatch = GUILayoutUtility.GetRect(14f, 18f, GUILayout.Width(14f));
                EditorGUI.DrawRect(new Rect(swatch.x, swatch.y + 3f, 12f, 12f), BoneColor(b));
                bool on = b == _partsWeightBone;
                if (GUILayout.Toggle(on, BoneDisplayName(mesh.Bones[b]), EditorStyles.miniButton) && !on)
                    _partsWeightBone = b;
                GUILayout.Label(Mathf.RoundToInt(BoneInfluence(mesh, b) * 100f) + "%", _mutedStyle, GUILayout.Width(34f));
                bool locked = SpritePartsSkinning.IsLocked(mesh.LockedBones, b);
                var lockContent = new GUIContent(locked ? lockOn : lockOff)
                {
                    tooltip = locked ? "Locked: nothing changes this bone's weights. Click to unlock." : "Lock this bone's weights",
                };
                if (lockContent.image == null)
                    lockContent.text = locked ? "L" : "-";
                if (GUILayout.Toggle(locked, lockContent, EditorStyles.miniButton, GUILayout.Width(24f)) != locked)
                    ToggleWeightLock(b);
                if (GUILayout.Button(new GUIContent("x", "Unbind this bone"), EditorStyles.miniButton, GUILayout.Width(20f)))
                {
                    UnbindWeightBone(b);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        void DrawWeightViewOptions()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("View", "How weights show on the mesh"), GUILayout.Width(40f));
            var view = (PartsWeightView)GUILayout.Toolbar((int)_partsWeightView, new[]
            {
                new GUIContent("Colors", "Every vertex is the blend of its bones' colours"),
                new GUIContent("Chosen", "Heat map of the chosen bone: blue 0% .. red 100%"),
                new GUIContent("Off"),
            }, EditorStyles.miniButton);
            bool pies = GUILayout.Toggle(_partsWeightPies, new GUIContent("Pies", "Each vertex shows its split as a pie"),
                EditorStyles.miniButton, GUILayout.Width(40f));
            EditorGUILayout.EndHorizontal();
            float opacity = EditorGUILayout.Slider(new GUIContent("Opacity", "Colour overlay strength"), _partsWeightOpacity, 0.1f, 1f);
            if (view != _partsWeightView || pies != _partsWeightPies || !Mathf.Approximately(opacity, _partsWeightOpacity))
            {
                _partsWeightView = view;
                _partsWeightPies = pies;
                _partsWeightOpacity = opacity;
                Repaint();
            }
            _partsWeightBrush = (PartsWeightBrush)GUILayout.Toolbar((int)_partsWeightBrush, new[]
            {
                new GUIContent("Add", "Adds the chosen bone's weight under the brush (Shift: Remove)"),
                new GUIContent("Remove", "Takes the chosen bone's weight away (Shift: Add)"),
                new GUIContent("Replace", "Moves the chosen bone's weight toward the Value"),
                new GUIContent("Smooth", "Evens out all weights under the brush with their neighbours"),
            }, EditorStyles.miniButton);
            if (_partsWeightBrush == PartsWeightBrush.Replace)
                _partsWeightReplaceValue = EditorGUILayout.Slider(new GUIContent("Value", "Replace brushes toward this weight"),
                    _partsWeightReplaceValue, 0f, 1f);
        }
    }
}

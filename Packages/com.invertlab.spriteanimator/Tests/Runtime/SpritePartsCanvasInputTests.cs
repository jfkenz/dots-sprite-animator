using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace InvertLab.Sprites.DOTS.Tests
{
    public class SpritePartsCanvasInputTests
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        EditorWindow window;
        Type type;
        void Set(string field, object value) => type.GetField(field, Flags).SetValue(window, value);
        object Get(string field) => type.GetField(field, Flags).GetValue(window);
        void SetEnum(string field, string value) => Set(field,
            Enum.Parse(type.GetField(field, Flags).FieldType, value));
        object Call(string method, params object[] args) => type.GetMethod(method, Flags).Invoke(window, args);
        void Drag(Vector2 start, Vector2 end)
        {
            window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = start });
            Debug.Log("After down tool=" + Get("_partsCanvasTool") + " active=" + Get("_partsDragActive") + " handle=" + Get("_partsTransformHandle") + " joint=" + Get("_partsDragStartJoint"));
            window.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = end, delta = end - start });
            Debug.Log("After drag tool=" + Get("_partsCanvasTool") + " active=" + Get("_partsDragActive") + " status=" + Get("_status"));
            window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = end });
        }
        void Click(Vector2 point)
        {
            window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = point });
            window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = point });
        }
        [UnityTest]
        public IEnumerator ToolbarClicksSelectRotateScaleAndMove()
        {
            type = AppDomain.CurrentDomain.GetAssemblies().Select(a =>
                a.GetType("InvertLab.Sprites.DOTS.Editor.SpriteSheetToolWindow")).First(t => t != null);
            window = (EditorWindow)ScriptableObject.CreateInstance(type);
            var profile = new SpriteSheetProfile();
            SpritePartsAuthoringOps.ApplyFloatingPartsTemplate(profile);
            Set("_asset", null);
            Set("_profile", profile);
            SetEnum("_studioTab", "Parts");
            Set("_partsSelectedSlot", 0);
            window.position = new Rect(50, 50, 1400, 900);
            window.ShowUtility();
            yield return null;
            window.Repaint();
            yield return null;
            float gap = (float)type.GetField("Gap", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
            float toolbar = (float)type.GetField("ToolbarHeight", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
            float x = gap + (float)Get("_clipPanelWidth") + gap + 12;
            float y = toolbar + gap + 34 + 10;
            float timeline = (float)Get("_timelinePanelHeight");
            float workHeight = window.position.height - toolbar - timeline - gap * 3;
            var canvas = new Rect(x - 2, toolbar + gap + 84,
                window.position.width - gap * 4 - (float)Get("_clipPanelWidth") - (float)Get("_inspectorPanelWidth") - 20,
                workHeight - 96);
            var start = canvas.center + new Vector2(5, 0);
            Drag(start, start + new Vector2(10, 10));
            Assert.That(profile.PartsClips[0].Tracks.Count, Is.GreaterThan(0), "Move writes a track");
            yield return null;
            Click(new Vector2(x + 66 + 25, y));
            Assert.That(Get("_partsCanvasTool").ToString(), Is.EqualTo("Rotate"), "Rotate toolbar click");
            yield return null;
            object[] gizmo = { canvas, null, null, null };
            Assert.That((bool)Call("TryGetSelectedPartsGizmo", gizmo), Is.True);
            var joint = (Vector2)gizmo[2];
            Drag(joint + new Vector2(8, 0), joint + new Vector2(0, -8));
            var pose = (SpritePartsAuthoringOps.PoseEdit)Call("SampleLocalPoseForSlot", "body", 0f);
            Assert.That(pose.Rotation, Is.EqualTo(90).Within(0.1f));
            yield return null;
            Click(new Vector2(x + 66 + 64 + 25, y));
            Assert.That(Get("_partsCanvasTool").ToString(), Is.EqualTo("Scale"), "Scale toolbar click");
            yield return null;
            gizmo = new object[] { canvas, null, null, null };
            Assert.That((bool)Call("TryGetSelectedPartsGizmo", gizmo), Is.True);
            var rect = (Rect)gizmo[1];
            joint = (Vector2)gizmo[2];
            float angle = (float)gizmo[3];
            var corner = joint + (Vector2)(Quaternion.Euler(0, 0, angle) * (new Vector2(rect.xMax, rect.yMin) - joint));
            Drag(corner, joint + (corner - joint) * 1.5f);
            pose = (SpritePartsAuthoringOps.PoseEdit)Call("SampleLocalPoseForSlot", "body", 0f);
            Assert.That(pose.Scale.x, Is.EqualTo(1.5f).Within(0.05f));
            Assert.That(pose.Scale.y, Is.EqualTo(1.5f).Within(0.05f));
            yield return null;
            Click(new Vector2(x + 25, y));
            Assert.That(Get("_partsCanvasTool").ToString(), Is.EqualTo("Move"));
        }
        [TearDown]
        public void Cleanup()
        {
            if (window != null) window.Close();
        }
    }
}

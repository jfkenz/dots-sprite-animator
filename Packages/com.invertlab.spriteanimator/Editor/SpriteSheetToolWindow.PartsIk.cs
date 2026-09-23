using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Posing IK (Move tool, IK on): drag a part and the grabbed point follows the mouse by turning the part
    // and its parents (Chain = how many joints turn, the part itself first). CCD solve, worked out again from
    // the pose at the press on every move; writes a rotation key per joint as one undo step. Needs Auto Key.
    public sealed partial class SpriteSheetToolWindow
    {
        [SerializeField] bool _partsIkOn;
        [SerializeField] int _partsIkChain = 2;
        bool _partsIkActive;
        readonly List<string> _partsIkSlots = new List<string>();                          // effector part first
        readonly List<SpritePartsAuthoringOps.PoseEdit> _partsIkStartPoses = new List<SpritePartsAuthoringOps.PoseEdit>();
        readonly List<Vector2> _partsIkStartJoints = new List<Vector2>();                   // window space
        Vector2 _partsIkGrab;
        Vector2[] _partsIkShownJoints;
        Vector2 _partsIkShownEnd;

        const int PartsIkIterations = 16;

        /// <summary>Press on a part in Move with IK on: collect the chain and its pivots.</summary>
        bool TryBeginPartsIk(Rect canvas, SpritePartSlotDef slot, Event evt, int controlId)
        {
            if (!_partsIkOn || slot == null || _partsMode != SpritePartsStudioMode.Animate)
                return false;
            if (!_partsAutoKey)
            {
                _status = "IK needs Auto Key on (it keys several parts at once).";
                return true;
            }
            if (!SpritePartsOnion.TrySampleCharacter(_profile, PartsEvaluationClipIndex(), _partsPreviewTime,
                    Allocator.Temp, out var blob, out var poses, out var matrices, out _))
                return false;
            _partsIkSlots.Clear();
            _partsIkStartPoses.Clear();
            _partsIkStartJoints.Clear();
            try
            {
                ApplyTempPoseToSample(ref blob.Value, poses, matrices);
                int idx = BlobSlotIndex(ref blob.Value, slot.SlotId);
                int chain = Mathf.Clamp(_partsIkChain, 1, 6);
                while (idx >= 0 && _partsIkSlots.Count < chain)
                {
                    string id = blob.Value.Slots[idx].SlotId.ToString();
                    var def = SpritePartsAuthoringOps.FindSlot(_profile, id);
                    if (def == null || def.EditorLocked || SpritePartsAuthoringOps.SlotOrAncestorLocked(_profile, id))
                        break; // a locked joint ends the chain
                    if (!TryGetPartsSlotDrawRect(canvas, ref blob.Value, matrices, idx, _partsPreviewTime,
                            out _, out var joint, out _, out _, out _, poses))
                        break;
                    _partsIkSlots.Add(id);
                    _partsIkStartPoses.Add(SampleLocalPoseForSlot(id, _partsPreviewTime));
                    _partsIkStartJoints.Add(joint);
                    idx = blob.Value.Slots[idx].ParentSlotIndex;
                }
            }
            finally
            {
                SpritePartsOnion.DisposeSample(blob, poses, matrices);
            }
            if (_partsIkSlots.Count == 0)
                return false;
            BeginPartsDragUndoDeferred("IK Pose");
            _partsIkActive = true;
            _partsIkGrab = evt.mousePosition;
            _partsIkShownJoints = _partsIkStartJoints.ToArray();
            _partsIkShownEnd = evt.mousePosition;
            _partsDragActive = true;
            _partsCanvasHotControl = controlId;
            GUIUtility.hotControl = controlId;
            _partsDragSlotId = slot.SlotId;
            _partsDragStartMouse = evt.mousePosition;
            _partsDragStartPose = _partsIkStartPoses[0];
            _status = "IK: " + _partsIkSlots.Count + " joint" + (_partsIkSlots.Count == 1 ? "" : "s") + " follow the mouse.";
            return true;
        }

        /// <summary>
        /// CCD from the pose at the press: each pass turns every joint (grabbed part first) so the grabbed point
        /// swings toward the mouse; turning a joint carries everything below it.
        /// </summary>
        void ApplyPartsIk(Vector2 mouse)
        {
            int n = _partsIkSlots.Count;
            if (!_partsIkActive || n == 0 || (mouse - _partsDragStartMouse).sqrMagnitude < 4f)
                return;
            var joints = _partsIkStartJoints.ToArray();
            var turned = new float[n];
            Vector2 end = _partsIkGrab;
            for (int pass = 0; pass < PartsIkIterations; pass++)
            {
                for (int k = 0; k < n; k++)
                {
                    Vector2 j = joints[k];
                    Vector2 toEnd = end - j;
                    Vector2 toTarget = mouse - j;
                    if (toEnd.sqrMagnitude < 1e-4f || toTarget.sqrMagnitude < 1e-4f)
                        continue;
                    float deg = Vector2.SignedAngle(toEnd, toTarget);
                    turned[k] += deg;
                    end = RotateGui(end, j, deg);
                    for (int m = 0; m < k; m++)
                        joints[m] = RotateGui(joints[m], j, deg); // joints below turn with it
                }
                if ((end - mouse).sqrMagnitude < 0.25f)
                    break;
            }
            _partsIkShownJoints = joints;
            _partsIkShownEnd = end;
            // Same rule as the Rotate tool: a screen turn of +deg is Rotation - deg.
            for (int k = n - 1; k >= 0; k--)
            {
                var pose = _partsIkStartPoses[k];
                pose.Rotation = _partsIkStartPoses[k].Rotation - turned[k];
                ApplyPartsPoseEdit(_partsIkSlots[k], pose);
            }
        }

        static Vector2 RotateGui(Vector2 p, Vector2 centre, float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            float cs = Mathf.Cos(r), sn = Mathf.Sin(r);
            Vector2 d = p - centre;
            return centre + new Vector2(d.x * cs - d.y * sn, d.x * sn + d.y * cs);
        }

        void EndPartsIk()
        {
            _partsIkActive = false;
        }

        /// <summary>The chain while dragging: pivots, the bones between them, the grabbed point and the mouse.</summary>
        void DrawPartsIkOverlay()
        {
            if (!_partsIkActive || _partsIkShownJoints == null || Event.current.type != EventType.Repaint)
                return;
            Handles.BeginGUI();
            var bone = new Color(0.3f, 0.95f, 0.85f, 0.9f);
            Handles.color = bone;
            Vector3 prev = _partsIkShownEnd;
            for (int k = 0; k < _partsIkShownJoints.Length; k++)
            {
                Handles.DrawAAPolyLine(3f, prev, _partsIkShownJoints[k]);
                prev = _partsIkShownJoints[k];
            }
            for (int k = 0; k < _partsIkShownJoints.Length; k++)
                Handles.DrawWireDisc(_partsIkShownJoints[k], Vector3.forward, 6f);
            Handles.color = new Color(1f, 0.85f, 0.3f, 1f);
            Handles.DrawSolidDisc(_partsIkShownEnd, Vector3.forward, 4f);
            Handles.DrawWireDisc(Event.current.mousePosition, Vector3.forward, 8f);
            Handles.EndGUI();
        }
    }
}

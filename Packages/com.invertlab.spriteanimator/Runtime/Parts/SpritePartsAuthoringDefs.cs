using System;
using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Frame flipbook, cutout Parts, or one static cell. Old assets default to Frame (0).</summary>
    public enum SpriteAnimKind : byte
    {
        Frame = 0,
        Parts = 1,
        Static = 2,
    }

    public enum SpritePartsWrap : byte
    {
        Loop = 0,
        Once = 1,
    }

    public enum SpritePartPivotSource : byte
    {
        SheetDefault = 0,
        Cell = 1,
        Override = 2,
    }

    /// <summary>
    /// Optional outfit/skin role. Used by Apply Outfit (appearance binding by role).
    /// Not a motion retarget channel and not required for bake.
    /// </summary>
    public enum SpritePartSemanticRole : byte
    {
        None = 0,
        Body = 1,
        Head = 2,
        Weapon = 3,
        Offhand = 4,
    }

    /// <summary>One named body slot in a Parts rig. Joint rest is parent-local world units.</summary>
    [Serializable]
    public class SpritePartSlotDef
    {
        public string Name = "Body";
        public string SlotId = "body";
        public string ParentSlotId = string.Empty;
        /// <summary>
        /// Tree sibling presentation order under ParentSlotId (or Character root).
        /// Independent of DrawRank / front-back rendering.
        /// </summary>
        public int SiblingOrder;
        public Vector2 RestPosition = Vector2.zero;
        public float RestRotation;
        public Vector2 RestScale = Vector2.one;
        public string DefaultAppearanceId = string.Empty;
        public int DrawRank;
        /// <summary>
        /// When false the part is not drawn in the baked scene (eye off in the Parts tree).
        /// The joint still exists so children keep their hierarchy.
        /// Ancestor hide also hides this part.
        /// </summary>
        public bool Enabled = true;
        /// <summary>Editor-only lock. Locked parts cannot be transformed, renamed, deleted or dragged.</summary>
        public bool EditorLocked;
        /// <summary>
        /// Sibling selection group. Empty = ungrouped. Not a motion parent:
        /// members keep ParentSlotId and their own tracks.
        /// </summary>
        public string GroupId = string.Empty;
        /// <summary>Optional outfit role for Apply Outfit. None = ignored by role mapping.</summary>
        public SpritePartSemanticRole SemanticRole;
        /// <summary>Setup mesh. Empty = the part draws as a rigid rectangle.</summary>
        public SpritePartMeshDef Mesh = new SpritePartMeshDef();
        /// <summary>
        /// Bone: a joint with no image (Spine-style). Never drawn in the game; its children draw normally.
        /// Posed and keyed like any part; can drive mesh weights and IK.
        /// </summary>
        public bool IsBone;
        /// <summary>
        /// Clipping mask: this part only shows inside the part with this id (its mesh outline, or its rectangle).
        /// Empty = not clipped.
        /// </summary>
        public string ClipMaskSlotId = string.Empty;
        /// <summary>Bone length in world units along its +X axis (editor drawing, IK tips). 0 = 1.</summary>
        public float BoneLength = 1f;
    }

    /// <summary>
    /// Setup mesh of one part, the same model as a Spine mesh attachment.
    /// Vertices are texture coordinates of the part image (0..1, y up) and also the rest shape.
    /// The first <see cref="HullCount"/> vertices are the outline in order; the rest sit inside it.
    /// Triangles are generated from the hull, the interior vertices and <see cref="Edges"/>.
    /// Animation only stores per-vertex offsets (<see cref="SpritePartsKeyDef.Deform"/>).
    /// </summary>
    [Serializable]
    public class SpritePartMeshDef
    {
        public Vector2[] Vertices;
        public int HullCount;
        /// <summary>Index pairs the triangulation must keep, besides the hull outline.</summary>
        public int[] Edges;
        public int[] Triangles;
        /// <summary>
        /// Spine weights: slot ids this mesh is bound to (the bones). Empty = unweighted,
        /// the mesh just follows its own part.
        /// </summary>
        public string[] Bones;
        /// <summary>Per vertex, one weight per bone: <c>Weights[vertex * Bones.Length + bone]</c>, each row sums to 1.</summary>
        public float[] Weights;
        /// <summary>Editor: vertices pinned in Warp (brushes, drags and FFD leave them). Not used by the game.</summary>
        public int[] Pins;
        /// <summary>Editor: bit b set = bone b's weights are locked (painting, Smooth, Prune and Auto leave them).</summary>
        public int LockedBones;

        public int VertexCount => Vertices?.Length ?? 0;

        public bool HasMesh =>
            Vertices != null && Vertices.Length >= 3 && HullCount >= 3 &&
            Triangles != null && Triangles.Length >= 3;

        public int BoneCount => Bones?.Length ?? 0;

        public bool HasWeights =>
            BoneCount > 0 && Weights != null && Weights.Length == VertexCount * BoneCount;

        public SpritePartMeshDef Clone() => new SpritePartMeshDef
        {
            Vertices = Vertices == null ? null : (Vector2[])Vertices.Clone(),
            HullCount = HullCount,
            Edges = Edges == null ? null : (int[])Edges.Clone(),
            Triangles = Triangles == null ? null : (int[])Triangles.Clone(),
            Bones = Bones == null ? null : (string[])Bones.Clone(),
            Weights = Weights == null ? null : (float[])Weights.Clone(),
            Pins = Pins == null ? null : (int[])Pins.Clone(),
            LockedBones = LockedBones,
        };
    }

    /// <summary>
    /// Editor-only sibling folder. Members must share one ParentSlotId.
    /// Does not bake into runtime and does not create a joint.
    /// </summary>
    [Serializable]
    public class SpritePartsGroupDef
    {
        public string Name = "Group";
        public string GroupId = "group";
        public bool Enabled = true;
        /// <summary>Editor-only. Locks every member (same as locking each part).</summary>
        public bool EditorLocked;
    }

    /// <summary>Art binding relative to a joint. Swap changes this; motion keys stay.</summary>
    [Serializable]
    public class SpritePartAppearanceDef
    {
        public string Name = "Appearance";
        public string AppearanceId = "appearance";
        public int SheetIndex;
        public int CellIndex;
        /// <summary>World-unit quad size. Zero means resolve from sheet cell / PPU at bake.</summary>
        public Vector2 LogicalWorldSize;
        public SpritePartPivotSource PivotSource = SpritePartPivotSource.SheetDefault;
        public Vector2 PivotOverride = new(0.5f, 0.5f);
        /// <summary>Display-only provenance when this appearance was copied/created by an import.</summary>
        public SpriteImportProvenance Import;
        /// <summary>Optional outfit role. When set, Apply Outfit prefers this over the bound slot's role.</summary>
        public SpritePartSemanticRole SemanticRole;
    }

    /// <summary>
    /// Runtime IK constraint (Spine / AnyPortrait style): the joint of <see cref="EffectorSlotId"/> reaches the
    /// joint of <see cref="TargetSlotId"/> by turning its parent (Chain 1) or its parent and grandparent (Chain 2).
    /// Solved every frame after the clip, in the game and the editor preview. Move / key the target
    /// (usually a bone) to plant a foot or aim a hand; gameplay can drive it with a pose override.
    /// </summary>
    [Serializable]
    public class SpritePartsIkConstraintDef
    {
        public string Name = "IK";
        public bool Enabled = true;
        public string EffectorSlotId = string.Empty;
        public string TargetSlotId = string.Empty;
        [Range(1, 2)] public int ChainLength = 2;
        /// <summary>Which way the middle joint bends (elbow / knee direction).</summary>
        public bool BendPositive = true;
        /// <summary>0 = off, 1 = fully solved; in between blends with the clip pose.</summary>
        [Range(0f, 1f)] public float Mix = 1f;
    }

    /// <summary>
    /// A control parameter (AnyPortrait-style): a value from <see cref="Min"/> to <see cref="Max"/> scrubs a clip
    /// from its start to its end, so a "Mouth" clip keyed closed → open becomes a slider. Gameplay sets the
    /// value at runtime. Additive adds the change from the <see cref="Default"/> pose on top of whatever plays.
    /// </summary>
    [Serializable]
    public class SpritePartsParamDef
    {
        public string Name = "Param";
        public string ClipId = string.Empty;
        public float Min;
        public float Max = 1f;
        public float Default;
        /// <summary>True: add the change from the default pose. False: the parts the clip keys take its pose.</summary>
        public bool Additive = true;
    }

    /// <summary>
    /// Jiggle (spring physics): the joint and <see cref="ChainLength"/> - 1 joints under it (first child each)
    /// swing behind their animated pose, for hair, tails, cloth and ears. Simulated at runtime every frame.
    /// </summary>
    [Serializable]
    public class SpritePartsJiggleDef
    {
        public string Name = "Jiggle";
        public bool Enabled = true;
        public string SlotId = string.Empty;
        [Range(1, 8)] public int ChainLength = 1;
        /// <summary>0 = loose and slow, 1 = stiff: snaps back to the animated pose quickly.</summary>
        [Range(0f, 1f)] public float Stiffness = 0.5f;
        /// <summary>0 = bouncy, 1 = no overshoot.</summary>
        [Range(0f, 1f)] public float Damping = 0.35f;
        /// <summary>World units / s² pulling the tip down.</summary>
        public float Gravity;
        [Range(0f, 1f)] public float Mix = 1f;
    }

    /// <summary>
    /// Which transform channels a key holds (Spine keys translate, rotate, scale and deform separately, so each
    /// can have its own timing). A channel a key does not hold is skipped when that channel is blended.
    /// </summary>
    [Flags]
    public enum SpritePartsKeyChannel : byte
    {
        None = 0,
        Position = 1,
        Rotation = 2,
        Scale = 4,
        Deform = 8,
        Transform = Position | Rotation | Scale,
        All = Position | Rotation | Scale | Deform,
    }

    [Serializable]
    public class SpritePartsKeyDef
    {
        public float Time;
        /// <summary>The channels this key holds. Old keys (and "Key Pose") hold all of them.</summary>
        public SpritePartsKeyChannel Channels = SpritePartsKeyChannel.All;
        public Vector2 Position = Vector2.zero;
        public float Rotation;
        public Vector2 Scale = Vector2.one;
        /// <summary>
        /// Deform key: one offset per vertex of the slot's <see cref="SpritePartSlotDef.Mesh"/>,
        /// in unit-quad space. Null or a different vertex count = the setup mesh, undeformed.
        /// </summary>
        public Vector2[] Deform;
        public byte EaseMode = (byte)SpriteEaseMode.Linear;
        /// <summary>
        /// Optional. Empty = hold previous keyed appearance (or skin/default when none active).
        /// Non-empty must be a profile PartsAppearances id (sheet index + cell already on profile).
        /// </summary>
        public string AppearanceId = string.Empty;
        /// <summary>Colour key: tints the part (alpha fades it). Colour keys blend with each other only.</summary>
        public bool HasColor;
        public Color Color = Color.white;
        /// <summary>Draw-order key: the part's draw rank from this key on (held, no blending).</summary>
        public bool HasDrawOrder;
        public int DrawOrder;
        /// <summary>Bezier handles (x1, y1, x2, y2) used when <see cref="EaseMode"/> is Bezier.</summary>
        public Vector4 Curve = new Vector4(0.33f, 0f, 0.67f, 1f);
    }

    /// <summary>
    /// Track channel. Pose = TRS motion (default; old JSON without the field
    /// stays pose). Appearance = sprite swaps only; pose sampling ignores it,
    /// appearance sampling reads it instead of ids mixed into pose keys.
    /// </summary>
    public enum SpritePartsTrackKind : byte
    {
        Pose = 0,
        Appearance = 1,
    }

    [Serializable]
    public class SpritePartsTrackDef
    {
        public string SlotId = string.Empty;
        public SpritePartsTrackKind Kind;
        public List<SpritePartsKeyDef> Keys = new();
    }

    /// <summary>
    /// An event on a Parts clip (footstep, hit, spawn an effect): fires when playback passes <see cref="Time"/>.
    /// <see cref="EventId"/> is an id from the profile's event list (the same list Frame clips use), so gameplay
    /// reads Parts and Frame events the same way (SpriteAnimEventBuffer / SpriteAnimEvents.Raised).
    /// </summary>
    [Serializable]
    public class SpritePartsEventMarker
    {
        public float Time;
        public byte EventId = 1;
        public int IntPayload;
        public float FloatPayload;
        public string TextPayload = string.Empty;

        public SpritePartsEventMarker Clone() => new SpritePartsEventMarker
        {
            Time = Time, EventId = EventId, IntPayload = IntPayload, FloatPayload = FloatPayload,
            TextPayload = TextPayload ?? string.Empty,
        };
    }

    [Serializable]
    public class SpritePartsClipDef
    {
        public string Name = "Walk";
        public string ClipId = "walk";
        [Min(0.0001f)] public float Duration = 1f;
        public float Speed = 1f;
        public byte WrapMode = (byte)SpritePartsWrap.Loop;
        public List<SpritePartsTrackDef> Tracks = new();
        /// <summary>Events that fire as playback passes their time.</summary>
        public List<SpritePartsEventMarker> Events = new();
        /// <summary>Display-only provenance when this clip was copied from another profile.</summary>
        public SpriteImportProvenance Import;
    }

    [Serializable]
    public class SpritePartsSkinBindingDef
    {
        public string SlotId = string.Empty;
        public string AppearanceId = string.Empty;
    }

    /// <summary>Named patch of SlotId → AppearanceId. Unlisted slots unchanged.</summary>
    [Serializable]
    public class SpritePartsSkinDef
    {
        public string Name = "Default";
        public string SkinId = "default";
        public List<SpritePartsSkinBindingDef> Bindings = new();
    }
}

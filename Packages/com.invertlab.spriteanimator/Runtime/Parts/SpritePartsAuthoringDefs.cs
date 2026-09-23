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

    [Serializable]
    public class SpritePartsKeyDef
    {
        public float Time;
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

    [Serializable]
    public class SpritePartsClipDef
    {
        public string Name = "Walk";
        public string ClipId = "walk";
        [Min(0.0001f)] public float Duration = 1f;
        public float Speed = 1f;
        public byte WrapMode = (byte)SpritePartsWrap.Loop;
        public List<SpritePartsTrackDef> Tracks = new();
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

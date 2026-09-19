using System;
using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Frame flipbook vs cutout Parts. Old assets default to Frame (0).</summary>
    public enum SpriteAnimKind : byte
    {
        Frame = 0,
        Parts = 1,
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
        /// <summary>Optional outfit role for Apply Outfit. None = ignored by role mapping.</summary>
        public SpritePartSemanticRole SemanticRole;
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

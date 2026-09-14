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

    /// <summary>One named body slot in a Parts rig. Joint rest is parent-local world units.</summary>
    [Serializable]
    public class SpritePartSlotDef
    {
        public string Name = "Body";
        public string SlotId = "body";
        public string ParentSlotId = string.Empty;
        public Vector2 RestPosition = Vector2.zero;
        public float RestRotation;
        public Vector2 RestScale = Vector2.one;
        public string DefaultAppearanceId = string.Empty;
        public int DrawRank;
        /// <summary>Editor selection/preview only. Bake still includes the slot.</summary>
        public bool Enabled = true;
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
    }

    [Serializable]
    public class SpritePartsKeyDef
    {
        public float Time;
        public Vector2 Position = Vector2.zero;
        public float Rotation;
        public Vector2 Scale = Vector2.one;
        public byte EaseMode = (byte)SpriteEaseMode.Linear;
    }

    [Serializable]
    public class SpritePartsTrackDef
    {
        public string SlotId = string.Empty;
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

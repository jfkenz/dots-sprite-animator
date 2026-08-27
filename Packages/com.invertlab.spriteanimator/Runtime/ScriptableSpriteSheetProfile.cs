using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Project asset consumed by both the editor and runtime bakers.</summary>
    public class ScriptableSpriteSheetProfile : ScriptableObject
    {
        public SpriteSheetProfile Data = new();
    }
}

using System;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// One static sprite authored on a profile (AnimKind Static). Baked by
    /// <see cref="SpriteStaticAuthoring"/> using sheet cell + pivot here.
    /// </summary>
    [Serializable]
    public class SpriteStaticSpriteDef
    {
        public int SheetIndex;
        public int Row;
        public int Column;
        public Vector2 Pivot = new(0.5f, 0.5f);
        [Min(0.001f)] public float SizeUnits = 1f;
        /// <summary>
        /// Rank within a Parts character sort space (drawIndex = characterOrder×64 + rank).
        /// </summary>
        public int DrawRank;
        public Color Tint = Color.white;
    }
}

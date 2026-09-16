using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Opt-in shared art library: reusable sheets and Parts appearances.
    /// Profiles reference libraries by GUID and Pull/Sync copies (or refreshes)
    /// local art. Nested library refs are allowed; cycles are refused.
    /// Runtime play never loads this asset — bake flattens into the blob.
    /// </summary>
    [CreateAssetMenu(
        menuName = "DOTS Sprite Animator/Sprite Art Library",
        fileName = "SpriteArtLibrary")]
    public class SpriteArtLibrary : ScriptableObject
    {
        public List<SpriteSheetDef> Sheets = new();
        public List<SpritePartAppearanceDef> Appearances = new();
        /// <summary>Other libraries this one composes. Attach/sync refuse cycles.</summary>
        public List<SpriteArtLibraryLink> NestedLibraries = new();
    }
}

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Resolve profile asset links stored as GUID strings on <see cref="SpriteSheetProfile"/>.</summary>
    public static class SpriteProfileLinkOps
    {
        public static string GuidFor(ScriptableSpriteSheetProfile asset)
        {
            if (asset == null)
                return string.Empty;
#if UNITY_EDITOR
            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
#else
            return string.Empty;
#endif
        }

#if UNITY_EDITOR
        public static ScriptableSpriteSheetProfile LoadByGuid(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
                return null;
            string path = AssetDatabase.GUIDToAssetPath(guid.Trim());
            if (string.IsNullOrEmpty(path))
                return null;
            return AssetDatabase.LoadAssetAtPath<ScriptableSpriteSheetProfile>(path);
        }
#endif

        /// <summary>
        /// Shared character sort space: Parts parts and linked static bodies use
        /// drawIndex = characterOrder×64 + rank (see <see cref="SpritePartsPlayback.DrawIndex"/>).
        /// </summary>
        public static int CharacterDrawIndex(int characterOrder, int drawRank)
            => SpritePartsPlayback.DrawIndex(characterOrder, drawRank);

        public static float CharacterSortDepth(int characterOrder, int drawRank)
            => SpriteSortDepth.FromIndex(CharacterDrawIndex(characterOrder, drawRank));
    }
}

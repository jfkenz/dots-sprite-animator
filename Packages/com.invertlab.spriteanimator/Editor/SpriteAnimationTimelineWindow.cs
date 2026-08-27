using UnityEditor;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>Compatibility menu redirect for projects using the original menu path.</summary>
    static class SpriteAnimationTimelineWindow
    {
        [MenuItem("Tools/DOTS Sprite Animator/Open Window")]
        public static void Open() => SpriteSheetToolWindow.Open();
    }
}

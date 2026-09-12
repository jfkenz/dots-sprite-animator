using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>Resolve a Complete sample relative to the calling imported editor script.</summary>
    public static class SpriteAnimatorSamplePaths
    {
        public static string Resolve(string suffix, [CallerFilePath] string callerFile = "")
        {
            var complete = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(callerFile)));
            string normalized = complete?.Replace('\\', '/') ?? "";
            int start = normalized.LastIndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (start < 0) throw new InvalidOperationException("Import the Complete sample into Assets through Package Manager first.");
            return normalized.Substring(start + 1) + suffix;
        }
    }
}

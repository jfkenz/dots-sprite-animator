using System;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Canonical stable IDs for Parts slots / appearances / skins.
    /// Trim, lowercase, spaces and other separators become dots.
    /// Frame clip Play names stay case-sensitive and do not use this helper.
    /// </summary>
    public static class SpritePartIdUtility
    {
        public const int MaxParts = 32;

        public static string Canonical(string value, string fallback = "part")
        {
            value = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            var chars = new char[value.Length];
            int count = 0;
            bool separator = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = char.ToLowerInvariant(value[i]);
                bool valid = c is >= 'a' and <= 'z' or >= '0' and <= '9' || c == '_';
                if (valid)
                {
                    chars[count++] = c;
                    separator = false;
                }
                else if (count > 0 && !separator)
                {
                    chars[count++] = '.';
                    separator = true;
                }
            }
            while (count > 0 && chars[count - 1] == '.')
                count--;
            return count == 0 ? fallback : new string(chars, 0, count);
        }

        public static bool IsValid(string value)
            => !string.IsNullOrWhiteSpace(value) &&
               string.Equals(value, Canonical(value), StringComparison.Ordinal);

        public static ulong Hash(string canonicalId)
            => SpriteAnimSetBuilder.Fnv(canonicalId ?? string.Empty);
    }
}

using System.IO.Enumeration;

namespace Tnzi.AI.Sandbox.Security;

/// <summary>
/// Matches a file name against the configured <c>DeniedPatterns</c> wildcard list
/// (e.g. <c>.env</c>, <c>*.key</c>, <c>*.pem</c>, <c>credentials*</c>) so the
/// sandbox can refuse to read or list sensitive files. Patterns are matched
/// case-insensitively against the leaf file name only - directory components do
/// not participate - using <see cref="FileSystemName.MatchesSimpleExpression"/>
/// (the same simple-expression engine the BCL uses for <c>Directory.GetFiles</c>
/// search patterns), so <c>*</c> and <c>?</c> behave exactly as a shell glob.
/// </summary>
public static class SensitiveFileMatcher
{
    /// <summary>
    /// Returns <c>true</c> when the leaf name of <paramref name="path"/> matches
    /// any pattern in <paramref name="deniedPatterns"/>. A pattern with no glob
    /// metacharacter (e.g. <c>.env</c>) matches the exact file name only.
    /// </summary>
    /// <param name="path">Absolute or relative file/directory path.</param>
    /// <param name="deniedPatterns">Wildcard patterns; <c>null</c>/empty matches nothing.</param>
    /// <param name="matchedPattern">The first pattern that matched, when the result is <c>true</c>.</param>
    public static bool IsDenied(string path, IReadOnlyCollection<string>? deniedPatterns, out string? matchedPattern)
    {
        matchedPattern = null;
        if (deniedPatterns is null || deniedPatterns.Count == 0 || string.IsNullOrEmpty(path))
            return false;

        var fileName = Path.GetFileName(path.AsSpan());
        if (fileName.IsEmpty)
            return false;

        foreach (var pattern in deniedPatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            if (FileSystemName.MatchesSimpleExpression(pattern.AsSpan(), fileName, ignoreCase: true))
            {
                matchedPattern = pattern;
                return true;
            }
        }

        return false;
    }
}

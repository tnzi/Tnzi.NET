using Tnzi.AI.Security;

namespace Tnzi.AI.Sandbox.Security;

/// <summary>
/// Sandbox command rejection check that runs commands through
/// <see cref="IShellCommandAnalyzer"/> for quote- and pipeline-aware token
/// extraction, then evaluates two complementary blacklists:
///
/// <list type="number">
///   <item>
///     <b>Prefix list (token-level)</b> - match the first token of any
///     pipeline segment against a list of denied binaries (e.g. <c>mkfs</c>,
///     <c>shutdown</c>). Immune to quote- and whitespace-bypass attacks like
///     <c>'rm' -rf /</c> or <c>rm  -rf  /</c>, because the analyzer already
///     splits the command into segments respecting <c>'</c> / <c>"</c>, and
///     <see cref="StripWrappingQuotes(string)"/> removes wrapping quotes
///     around the binary name.
///   </item>
///   <item>
///     <b>Substring list</b> - backwards-compatible substring match against
///     the full command. Preserves the behaviour of the original
///     <c>DeniedCommands</c> config so existing rules keep working.
///   </item>
/// </list>
///
/// Both checks are independent; a command is denied if either matches.
/// </summary>
public static class DeniedCommandMatcher
{
    /// <summary>
    /// Evaluates a command against the denial lists. Returns <c>true</c> when
    /// the command should be rejected, and populates <paramref name="reason"/>
    /// with an English explanation suitable for surfacing to the agent.
    /// </summary>
    public static bool IsDenied(
        IShellCommandAnalyzer? analyzer,
        string command,
        IReadOnlyCollection<string> deniedSubstrings,
        IReadOnlyCollection<string> deniedPrefixes,
        out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
            return false;

        // Token-level prefix check - only available when the AI module registered
        // the analyzer. Sandbox can be used in isolation (tests, scripts) where
        // the DI graph stops at AISandboxModule; fall back to substring-only.
        if (analyzer != null && deniedPrefixes.Count > 0)
        {
            // Pre-canonicalize the blacklist once instead of once per segment.
            var bannedSet = BuildCanonicalSet(deniedPrefixes);
            if (bannedSet.Count > 0)
            {
                var analysis = analyzer.Analyze(command);
                foreach (var segment in analysis.Segments)
                {
                    var canonical = StripWrappingQuotes(segment.CommandPrefix);
                    if (bannedSet.Contains(canonical))
                    {
                        reason = $"Command segment '{segment.CommandText}' starts with denied binary '{canonical}'";
                        return true;
                    }
                }
            }
        }

        // Backwards-compatible substring check on the raw command.
        // Keeps existing DeniedCommands semantics (e.g. matches "rm -rf /" inside
        // "rm -rf /tmp/x" because the latter contains the former as a substring).
        foreach (var denied in deniedSubstrings)
        {
            if (string.IsNullOrWhiteSpace(denied))
                continue;
            if (command.Contains(denied, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Command contains blocked pattern '{denied}'";
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> BuildCanonicalSet(IEnumerable<string> raw)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in raw)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var canonical = StripWrappingQuotes(entry.Trim());
            if (!string.IsNullOrEmpty(canonical))
                set.Add(canonical);
        }
        return set;
    }

    /// <summary>
    /// Removes a single layer of wrapping <c>'</c> or <c>"</c> quotes from a token.
    /// Used to canonicalize binary names so <c>'rm'</c> matches the <c>rm</c>
    /// entry in the prefix blacklist.
    /// </summary>
    private static string StripWrappingQuotes(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length < 2)
            return token;

        var first = token[0];
        var last = token[^1];
        if ((first == '\'' && last == '\'') || (first == '"' && last == '"'))
            return token[1..^1];

        return token;
    }
}

using Tnzi.AI.Sandbox.Security;
using Tnzi.AI.Security;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// Unit tests for <see cref="DeniedCommandMatcher"/>, the shared blacklist
/// evaluator used by both <see cref="Providers.Local.LocalSandbox"/> and
/// <see cref="Providers.Docker.DockerSandbox"/>.
///
/// The matcher combines a backwards-compatible substring list with a
/// quote/whitespace-resistant prefix list driven by <see cref="IShellCommandAnalyzer"/>.
/// </summary>
public class DeniedCommandMatcherTests
{
    private static readonly IShellCommandAnalyzer Analyzer = new ShellCommandAnalyzer();

    private static readonly string[] DefaultSubstrings =
        ["rm -rf /", "format c:", "chmod 777 /", "mkfs"];

    private static readonly string[] DefaultPrefixes =
        ["mkfs", "shutdown", "reboot", "halt", "poweroff", "init", "fdisk", "dd"];

    // ------------------------------------------------------------------ //
    // Backwards-compatibility - existing substring behaviour must be kept.
    // ------------------------------------------------------------------ //

    [Fact]
    public void Substring_RmRfRoot_Denied()
    {
        var denied = DeniedCommandMatcher.IsDenied(
            Analyzer, "rm -rf /", DefaultSubstrings, DefaultPrefixes, out var reason);

        denied.ShouldBeTrue();
        reason.ShouldContain("rm -rf /");
    }

    [Fact]
    public void Substring_RmRfRootWildcard_StillDenied_ViaSubstring()
    {
        // Original substring behaviour: "rm -rf /*" contains "rm -rf /" as a
        // substring, so it remains blocked.
        var denied = DeniedCommandMatcher.IsDenied(
            Analyzer, "rm -rf /*", DefaultSubstrings, DefaultPrefixes, out _);

        denied.ShouldBeTrue();
    }

    [Fact]
    public void Substring_CaseInsensitive_StillDenied()
    {
        var denied = DeniedCommandMatcher.IsDenied(
            Analyzer, "RM -RF /", DefaultSubstrings, DefaultPrefixes, out _);

        denied.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ //
    // Prefix list - the main new capability (token-level, bypass-resistant).
    // ------------------------------------------------------------------ //

    [Fact]
    public void Prefix_QuotedBinary_StillDenied()
    {
        // Without the prefix list this would slip past substring matching
        // because the canonical "rm -rf /" string is no longer literal.
        // ShellCommandAnalyzer treats `'rm'` as one token, and StripWrappingQuotes
        // removes the surrounding quotes for prefix comparison.
        var denied = DeniedCommandMatcher.IsDenied(
            Analyzer, "'mkfs' /dev/sda", DefaultSubstrings, DefaultPrefixes, out var reason);

        denied.ShouldBeTrue();
        reason.ShouldContain("mkfs");
    }

    [Fact]
    public void Prefix_PipelineSecondSegment_Denied()
    {
        // `cat | mkfs` would have an entirely safe first segment but a banned
        // second segment. Substring matching alone would miss this if the
        // pattern were not literal; prefix matching catches it per-segment.
        var denied = DeniedCommandMatcher.IsDenied(
            Analyzer, "cat /tmp/foo | mkfs.ext4 /dev/sda", DefaultSubstrings, DefaultPrefixes, out var reason);

        denied.ShouldBeTrue();
        reason.ShouldContain("mkfs");
    }

    [Fact]
    public void Prefix_AfterCommandSeparator_Denied()
    {
        // `;` and `&&` form distinct segments - second segment is the attack.
        var denied = DeniedCommandMatcher.IsDenied(
            Analyzer, "echo hi && shutdown -h now", DefaultSubstrings, DefaultPrefixes, out var reason);

        denied.ShouldBeTrue();
        reason.ShouldContain("shutdown");
    }

    [Fact]
    public void Prefix_NotBanned_Allowed()
    {
        var denied = DeniedCommandMatcher.IsDenied(
            Analyzer, "python /mnt/workspace/build_report.py", DefaultSubstrings, DefaultPrefixes, out _);

        denied.ShouldBeFalse();
    }

    [Fact]
    public void Prefix_BannedWordInsideArgument_Allowed()
    {
        // The word "shutdown" appears as an argument, not as the binary -
        // must NOT be denied (otherwise we'd block legitimate uses like
        // logging or echoing dangerous words).
        var denied = DeniedCommandMatcher.IsDenied(
            Analyzer,
            "echo shutdown",
            deniedSubstrings: [],   // disable substring fallback to isolate prefix behaviour
            DefaultPrefixes,
            out _);

        denied.ShouldBeFalse();
    }

    // ------------------------------------------------------------------ //
    // Fallback behaviour when the analyzer is unavailable.
    // ------------------------------------------------------------------ //

    [Fact]
    public void NoAnalyzer_PrefixListIgnored_SubstringStillEvaluated()
    {
        // If the AI module is not loaded (analyzer = null), prefix checks are
        // skipped but the substring fallback must still apply.
        var denied = DeniedCommandMatcher.IsDenied(
            analyzer: null,
            command: "rm -rf /",
            DefaultSubstrings,
            DefaultPrefixes,
            out _);

        denied.ShouldBeTrue();
    }

    [Fact]
    public void NoAnalyzer_PrefixListIgnored_PrefixOnlyCommandPasses()
    {
        // Without the analyzer, `shutdown` would have been caught by prefix
        // checks. Without it, it slips through (acceptable degraded behaviour).
        var denied = DeniedCommandMatcher.IsDenied(
            analyzer: null,
            command: "shutdown -h now",
            deniedSubstrings: [],
            DefaultPrefixes,
            out _);

        denied.ShouldBeFalse();
    }

    // ------------------------------------------------------------------ //
    // Edge cases.
    // ------------------------------------------------------------------ //

    [Fact]
    public void EmptyCommand_NotDenied()
    {
        var denied = DeniedCommandMatcher.IsDenied(
            Analyzer, "", DefaultSubstrings, DefaultPrefixes, out _);

        denied.ShouldBeFalse();
    }

    [Fact]
    public void WhitespaceCommand_NotDenied()
    {
        var denied = DeniedCommandMatcher.IsDenied(
            Analyzer, "   ", DefaultSubstrings, DefaultPrefixes, out _);

        denied.ShouldBeFalse();
    }

    [Fact]
    public void EmptyBlacklists_NothingDenied()
    {
        var denied = DeniedCommandMatcher.IsDenied(
            Analyzer, "rm -rf /", deniedSubstrings: [], deniedPrefixes: [], out _);

        denied.ShouldBeFalse();
    }

    [Fact]
    public void EmptyStringInBlacklist_Ignored()
    {
        // Defensive: an empty/whitespace entry in the config must not cause
        // every command to match (substring "" is contained in everything).
        var denied = DeniedCommandMatcher.IsDenied(
            Analyzer,
            "echo hello",
            deniedSubstrings: ["", "   "],
            deniedPrefixes: ["", "   "],
            out _);

        denied.ShouldBeFalse();
    }
}

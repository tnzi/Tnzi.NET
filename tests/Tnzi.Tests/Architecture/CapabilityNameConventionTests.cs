using System.Text.RegularExpressions;

namespace Tnzi.Tests.Architecture;

/// <summary>
/// Capability names must be referenced through constants, never written inline.
/// </summary>
/// <remarks>
/// A capability name is a contract shared by the server and every client. If one site writes
/// <c>"chat-draft-restore-v1"</c> and another writes <c>"chat-draft-restore-V1"</c>, negotiation
/// silently never matches - the newer path is simply never taken, and nothing fails. A gate is the
/// only way to catch that, because the symptom is an absence.
/// </remarks>
public class CapabilityNameConventionTests
{
    /// <summary>
    /// Matches an inline literal passed to any of the capability entry points.
    /// </summary>
    /// <remarks>
    /// <c>DeclareCapability</c> is listed explicitly: it is the <b>recommended</b> way to declare,
    /// and <c>\.Declare\s*\(</c> does not match it (the name continues past "Declare"), so leaving
    /// it out would let the gate miss the very call site everyone is told to write.
    /// </remarks>
    private static readonly Regex InlineCapabilityLiteral = new(
        @"\.(DeclareCapability|Declare|Supports|IsDeclared)\s*\(\s*""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void CapabilityNames_AreNeverWrittenAsInlineLiterals()
    {
        var repoRoot = RepoRoot.Locate();

        var scanned = 0;
        var offenders = new List<string>();

        foreach (var file in RepoScan.EnumerateFiles("src", "*.cs"))
        {
            scanned++;
            var content = File.ReadAllText(file);

            // Only files that touch the capability API can offend; this keeps unrelated
            // Declare/Supports method names on other types out of the result.
            if (!content.Contains("Capabilit", StringComparison.Ordinal)) continue;

            foreach (var match in InlineCapabilityLiteral.Matches(content).Cast<System.Text.RegularExpressions.Match>())
            {
                offenders.Add($"{Path.GetRelativePath(repoRoot, file)}: {match.Value.Trim()}");
            }
        }

        // Without this the gate would pass just as happily on an empty scan - the exact way a
        // convention test rots into a permanently-green no-op.
        Assert.True(scanned > 100, $"the source scan found suspiciously few files ({scanned})");

        Assert.True(offenders.Count == 0,
            "capability names must come from a shared constant, not an inline string: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void TheDetector_ActuallyFlagsAnInlineLiteral()
    {
        // The scan above is expected to find nothing today (no capability has consumers yet), so
        // on its own it cannot distinguish "clean" from "broken detector".
        const string offending = """catalog.Declare("chat-draft-restore-v1");""";
        const string offendingViaExtension = """services.DeclareCapability("chat-draft-restore-v1");""";
        const string clean = "catalog.Declare(TnziCapabilities.ChatDraftRestoreV1);";
        const string cleanViaExtension = "services.DeclareCapability(ChatCapabilities.DraftRestoreV1);";

        Assert.Matches(InlineCapabilityLiteral, offending);
        Assert.Matches(InlineCapabilityLiteral, offendingViaExtension);
        Assert.DoesNotMatch(InlineCapabilityLiteral, clean);
        Assert.DoesNotMatch(InlineCapabilityLiteral, cleanViaExtension);
    }
}

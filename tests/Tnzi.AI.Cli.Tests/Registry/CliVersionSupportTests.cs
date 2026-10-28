namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 部署为一家 CLI 声明的支持范围：没声明时一切放行，声明了就按数字逐段比，认不出版本时拒绝。
/// </summary>
public class CliVersionSupportTests
{
    private static readonly CliProviderDescriptor Claude = CliBuiltInProviders.All["claude"];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("0.0.1")]
    public void WithoutADeclaredRange_EveryVersionIsAccepted(string? version)
        => CliVersionSupport.GetRejection(Claude, version, "host").ShouldBeNull();

    [Theory]
    [InlineData("2.1.300", true)]
    [InlineData("2.1.310 (Claude Code)", true)]
    [InlineData("2.1.9", false)]
    [InlineData("2.1", false)]
    [InlineData("2.2.0", false)]
    [InlineData("2.1.300.4", true)]
    [InlineData("v2.1.305", true)]
    public void MinimumIsInclusive_AndTheMaximumCoversEveryVersionUnderTheSegmentsItNames(string version, bool accepted)
    {
        var provider = Claude with { MinimumVersion = "2.1.300", MaximumVersion = "2.1" };

        var rejection = CliVersionSupport.GetRejection(provider, version, "ws-1");

        if (accepted)
        {
            rejection.ShouldBeNull();
        }
        else
        {
            rejection.ShouldNotBeNull().ShouldContain("ws-1");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    public void WithADeclaredRange_AnUnknownVersionIsRefused(string? version)
        => CliVersionSupport.GetRejection(Claude with { MinimumVersion = "2.1" }, version, "ws-1")
            .ShouldNotBeNull().ShouldContain("unknown");

    [Theory]
    [InlineData("2.1.310 (Claude Code)", new[] { 2, 1, 310 })]
    [InlineData("codex-cli 0.46.0", new[] { 0, 46, 0 })]
    [InlineData("nothing here", null)]
    public void TheFirstDottedNumberIsTheVersion(string text, int[]? expected)
        => CliVersionSupport.Parse(text).ShouldBe(expected);
}

namespace Tnzi.AI.Tests.Guardrails;

/// <summary>
/// AllowlistGuardrailProvider 单元测试 - 工具白名单/黑名单防护
/// </summary>
public class AllowlistGuardrailProviderTests
{
    private static IOptionsMonitor<AIOptions> CreateOptions(Action<AllowlistGuardrailOptions>? configure = null)
    {
        var options = new AIOptions();
        configure?.Invoke(options.Guardrails.Allowlist);
        return new StaticOptionsMonitor<AIOptions>(options);
    }

    [Fact]
    public async Task NoToolName_ReturnsAllow()
    {
        var provider = new AllowlistGuardrailProvider(CreateOptions(a =>
        {
            a.DeniedTools = ["bash"];
        }));

        var result = await provider.EvaluateAsync(new GuardrailRequest { Content = "hello" });

        result.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task DeniedTool_ReturnsDeny()
    {
        var provider = new AllowlistGuardrailProvider(CreateOptions(a =>
        {
            a.DeniedTools = ["bash", "rm"];
        }));

        var result = await provider.EvaluateAsync(new GuardrailRequest { ToolName = "bash" });

        result.IsAllowed.ShouldBeFalse();
        result.Reasons[0].Code.ShouldBe(GuardrailReasonCodes.ToolDenied);
        result.PolicyId.ShouldBe("allowlist");
    }

    [Fact]
    public async Task AllowedTool_ReturnsAllow()
    {
        var provider = new AllowlistGuardrailProvider(CreateOptions(a =>
        {
            a.AllowedTools = ["file_read", "web_search"];
        }));

        var result = await provider.EvaluateAsync(new GuardrailRequest { ToolName = "file_read" });

        result.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task ToolNotInAllowList_ReturnsDeny()
    {
        var provider = new AllowlistGuardrailProvider(CreateOptions(a =>
        {
            a.AllowedTools = ["file_read", "web_search"];
        }));

        var result = await provider.EvaluateAsync(new GuardrailRequest { ToolName = "bash" });

        result.IsAllowed.ShouldBeFalse();
        result.Reasons[0].Code.ShouldBe(GuardrailReasonCodes.ToolNotAllowed);
    }

    [Fact]
    public async Task EmptyLists_AllowsAll()
    {
        var provider = new AllowlistGuardrailProvider(CreateOptions());

        var result = await provider.EvaluateAsync(new GuardrailRequest { ToolName = "anything" });

        result.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task PrefixMatch_MatchesPrefix()
    {
        var provider = new AllowlistGuardrailProvider(CreateOptions(a =>
        {
            a.AllowedTools = ["file_"];
            a.MatchExact = false;
        }));

        var result = await provider.EvaluateAsync(new GuardrailRequest { ToolName = "file_read" });
        result.IsAllowed.ShouldBeTrue();

        var result2 = await provider.EvaluateAsync(new GuardrailRequest { ToolName = "web_search" });
        result2.IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task ExactMatch_RequiresExactName()
    {
        var provider = new AllowlistGuardrailProvider(CreateOptions(a =>
        {
            a.AllowedTools = ["file_"];
            a.MatchExact = true;
        }));

        var result = await provider.EvaluateAsync(new GuardrailRequest { ToolName = "file_read" });
        result.IsAllowed.ShouldBeFalse();

        var result2 = await provider.EvaluateAsync(new GuardrailRequest { ToolName = "file_" });
        result2.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task DenyListTakesPrecedence()
    {
        var provider = new AllowlistGuardrailProvider(CreateOptions(a =>
        {
            a.AllowedTools = ["bash"];
            a.DeniedTools = ["bash"];
        }));

        var result = await provider.EvaluateAsync(new GuardrailRequest { ToolName = "bash" });

        // DeniedTools checked first, so bash is denied
        result.IsAllowed.ShouldBeFalse();
        result.Reasons[0].Code.ShouldBe(GuardrailReasonCodes.ToolDenied);
    }

    [Fact]
    public void Name_ReturnsExpectedValue()
    {
        var provider = new AllowlistGuardrailProvider(CreateOptions());
        provider.Name.ShouldBe(nameof(AllowlistGuardrailProvider));
    }
}

namespace Tnzi.AI.Tests.Guardrails;

/// <summary>
/// ContentFilterGuardrail 单元测试 — 验证输出内容过滤的各种场景
/// </summary>
public class ContentFilterGuardrailTests
{
    private static IOptions<AIOptions> CreateOptions(Action<GuardrailsOptions>? configure = null)
    {
        var options = new AIOptions();
        configure?.Invoke(options.Guardrails);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    [Fact]
    public async Task CleanOutput_ReturnsAllowed()
    {
        // Arrange
        var guardrail = new ContentFilterGuardrail(CreateOptions(g =>
        {
            g.EnableContentFilter = true;
            g.BlockedOutputKeywords = ["CLASSIFIED", "TOP SECRET"];
        }));

        // Act
        var result = await guardrail.ValidateAsync("Here is a summary of the weather forecast for today.");

        // Assert
        result.IsAllowed.ShouldBeTrue();
        result.Reason.ShouldBeNull();
    }

    [Fact]
    public async Task OutputContainingBlockedKeyword_ReturnsRejected()
    {
        // Arrange
        var guardrail = new ContentFilterGuardrail(CreateOptions(g =>
        {
            g.EnableContentFilter = true;
            g.BlockedOutputKeywords = ["CLASSIFIED", "CONFIDENTIAL"];
        }));

        // Act
        var result = await guardrail.ValidateAsync("This document is CLASSIFIED and should not be shared.");

        // Assert
        result.IsAllowed.ShouldBeFalse();
        result.GuardrailName.ShouldBe(nameof(ContentFilterGuardrail));
        var reason = result.Reason;
        reason.ShouldNotBeNull();
        reason.ShouldContain("blocked content");
    }

    [Fact]
    public async Task CaseInsensitiveMatch_ReturnsRejected()
    {
        // Arrange: 关键词配置为小写，输出内容为大写
        var guardrail = new ContentFilterGuardrail(CreateOptions(g =>
        {
            g.EnableContentFilter = true;
            g.BlockedOutputKeywords = ["forbidden"];
        }));

        // Act
        var result = await guardrail.ValidateAsync("This action is FORBIDDEN by policy.");

        // Assert
        result.IsAllowed.ShouldBeFalse();
        result.GuardrailName.ShouldBe(nameof(ContentFilterGuardrail));
    }

    [Fact]
    public async Task EmptyBlockedKeywordList_ReturnsAllowed()
    {
        // Arrange: 启用过滤但关键词列表为空
        var guardrail = new ContentFilterGuardrail(CreateOptions(g =>
        {
            g.EnableContentFilter = true;
            g.BlockedOutputKeywords = [];
        }));

        // Act
        var result = await guardrail.ValidateAsync("Any content should pass through with no keywords defined.");

        // Assert
        result.IsAllowed.ShouldBeTrue();
    }
}

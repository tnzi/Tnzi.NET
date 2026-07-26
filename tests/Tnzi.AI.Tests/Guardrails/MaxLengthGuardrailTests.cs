namespace Tnzi.AI.Tests.Guardrails;

/// <summary>
/// MaxLengthGuardrail 单元测试 - 验证输入长度限制的各种边界条件
/// </summary>
public class MaxLengthGuardrailTests
{
    private static IOptionsMonitor<AIOptions> CreateOptions(Action<GuardrailsOptions>? configure = null)
    {
        var options = new AIOptions();
        configure?.Invoke(options.Guardrails);
        return new StaticOptionsMonitor<AIOptions>(options);
    }

    [Fact]
    public async Task InputWithinLimit_ReturnsAllowed()
    {
        // Arrange
        var guardrail = new MaxLengthGuardrail(CreateOptions(g =>
        {
            g.EnableMaxLength = true;
            g.MaxInputLength = 1000;
        }));

        // Act
        var result = await guardrail.ValidateAsync("Hello, how are you?");

        // Assert
        result.IsAllowed.ShouldBeTrue();
        result.Reason.ShouldBeNull();
        result.ModifiedContent.ShouldBeNull();
    }

    [Fact]
    public async Task InputExceedingLimit_ReturnsRejectedWithReason()
    {
        // Arrange
        var guardrail = new MaxLengthGuardrail(CreateOptions(g =>
        {
            g.EnableMaxLength = true;
            g.MaxInputLength = 10;
        }));
        var longInput = new string('a', 50);

        // Act
        var result = await guardrail.ValidateAsync(longInput);

        // Assert
        result.IsAllowed.ShouldBeFalse();
        result.GuardrailName.ShouldBe(nameof(MaxLengthGuardrail));
        result.Reason.ShouldNotBeNullOrEmpty();
        result.Reason!.ShouldContain("exceeds maximum length");
        result.Reason.ShouldContain("10");
        result.Reason.ShouldContain("50");
    }

    [Fact]
    public async Task EmptyInput_ReturnsAllowed()
    {
        // Arrange
        var guardrail = new MaxLengthGuardrail(CreateOptions(g =>
        {
            g.EnableMaxLength = true;
            g.MaxInputLength = 100;
        }));

        // Act
        var result = await guardrail.ValidateAsync(string.Empty);

        // Assert
        result.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task ExactBoundaryLength_ReturnsAllowed()
    {
        // Arrange: 输入长度恰好等于限制值 - 应通过（不超过）
        const int limit = 20;
        var guardrail = new MaxLengthGuardrail(CreateOptions(g =>
        {
            g.EnableMaxLength = true;
            g.MaxInputLength = limit;
        }));
        var exactInput = new string('x', limit);

        // Act
        var result = await guardrail.ValidateAsync(exactInput);

        // Assert
        result.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Disabled_AllowsAnyLength()
    {
        // Arrange: EnableMaxLength=false 时无论多长都应通过
        var guardrail = new MaxLengthGuardrail(CreateOptions(g =>
        {
            g.EnableMaxLength = false;
            g.MaxInputLength = 1; // 极小限制但功能已禁用
        }));

        // Act
        var result = await guardrail.ValidateAsync(new string('z', 100_000));

        // Assert
        result.IsAllowed.ShouldBeTrue();
    }
}

namespace Tnzi.AI.Tests.Guardrails;

/// <summary>
/// GuardrailRunner 单元测试 - 验证 Guardrail 编排逻辑
/// </summary>
public class GuardrailRunnerTests
{
    private static IOptionsMonitor<AIOptions> CreateOptions(Action<GuardrailsOptions>? configure = null)
    {
        var options = new AIOptions();
        options.Guardrails.Enabled = true;
        configure?.Invoke(options.Guardrails);
        return new StaticOptionsMonitor<AIOptions>(options);
    }

    [Fact]
    public async Task AllGuardrailsPass_ReturnsAllowed()
    {
        // Arrange: 两个都通过的 guardrail
        var options = CreateOptions(g =>
        {
            g.EnableMaxLength = true;
            g.MaxInputLength = 1000;
            g.EnablePromptInjectionDetection = true;
        });
        var runner = new GuardrailRunner(
            [new MaxLengthGuardrail(options), new PromptInjectionGuardrail(options)],
            [],
            options,
            Mock.Of<ILogger<GuardrailRunner>>());

        // Act
        var (text, rejection) = await runner.RunInputGuardrailsAsync("Hello world");

        // Assert
        rejection.ShouldBeNull();
        text.ShouldBe("Hello world");
    }

    [Fact]
    public async Task FirstGuardrailRejects_ShortCircuits()
    {
        // Arrange: MaxLength 先拒绝，PromptInjection 不应执行
        var options = CreateOptions(g =>
        {
            g.EnableMaxLength = true;
            g.MaxInputLength = 5;
            g.EnablePromptInjectionDetection = true;
        });
        var runner = new GuardrailRunner(
            [new MaxLengthGuardrail(options), new PromptInjectionGuardrail(options)],
            [],
            options,
            Mock.Of<ILogger<GuardrailRunner>>());

        // Act
        var (_, rejection) = await runner.RunInputGuardrailsAsync("This input is way too long for the limit");

        // Assert: 第一个 guardrail (MaxLength) 拒绝
        rejection.ShouldNotBeNull();
        rejection.IsAllowed.ShouldBeFalse();
        rejection.GuardrailName.ShouldBe(nameof(MaxLengthGuardrail));
    }

    [Fact]
    public async Task NoGuardrailsRegistered_ReturnsAllowed()
    {
        // Arrange: 无任何 guardrail 注册
        var options = CreateOptions();
        var runner = new GuardrailRunner(
            [],
            [],
            options,
            Mock.Of<ILogger<GuardrailRunner>>());

        // Act
        var (text, rejection) = await runner.RunInputGuardrailsAsync("Any input should pass");

        // Assert
        rejection.ShouldBeNull();
        text.ShouldBe("Any input should pass");
    }

    [Fact]
    public async Task GuardrailThrowsException_PropagatesError()
    {
        // Arrange: Mock 一个抛出异常的 guardrail - 顺序模式下异常应传播
        var faultyGuardrail = new Mock<IInputGuardrail>();
        faultyGuardrail
            .Setup(g => g.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Guardrail internal error"));

        var options = CreateOptions(g => g.ExecutionMode = GuardrailExecutionMode.Sequential);
        var runner = new GuardrailRunner(
            [faultyGuardrail.Object],
            [],
            options,
            Mock.Of<ILogger<GuardrailRunner>>());

        // Act & Assert: 异常不被静默吞掉（fail-closed 行为）
        await Should.ThrowAsync<InvalidOperationException>(
            () => runner.RunInputGuardrailsAsync("test input"));
    }
}

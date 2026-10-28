namespace Tnzi.AI.Tests;

/// <summary>
/// LlmJudgeGuardrail 单元测试 - 验证 LLM-as-Judge 输入/输出评估
/// </summary>
public class LlmJudgeGuardrailTests
{
    private const string PassResponse = "PASS";
    private const string FailResponse = "FAIL: Content contains harmful instructions";

    private static IOptionsMonitor<AIOptions> CreateOptions(Action<LlmJudgeOptions>? configure = null, bool failClosed = true)
    {
        var options = new AIOptions();
        options.Guardrails.FailClosed = failClosed;
        configure?.Invoke(options.Guardrails.LlmJudge);
        return new StaticOptionsMonitor<AIOptions>(options);
    }

    private static Mock<IChatClientFactory> CreateThrowingFactory(Exception exception)
    {
        var mockClient = new Mock<IChatClient>();
        mockClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory
            .Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockClient.Object);
        return mockFactory;
    }

    private static (Mock<IChatClientFactory> factory, Mock<IChatClient> client) CreateMockChatClient(string response)
    {
        var mockClient = new Mock<IChatClient>();
        mockClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory
            .Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockClient.Object);

        return (mockFactory, mockClient);
    }

    #region Disabled

    [Fact]
    public async Task Disabled_ReturnsAllowed()
    {
        var (mockFactory, _) = CreateMockChatClient(PassResponse);
        var options = CreateOptions(o => o.Enabled = false);
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, options, Mock.Of<ILogger<LlmJudgeGuardrail>>());

        // 作为输入 guardrail 验证
        var inputResult = await ((IInputGuardrail)guardrail).ValidateAsync("test input");
        inputResult.IsAllowed.ShouldBeTrue();

        // 作为输出 guardrail 验证
        var outputResult = await ((IOutputGuardrail)guardrail).ValidateAsync("test output");
        outputResult.IsAllowed.ShouldBeTrue();

        // 禁用时不应调用 LLM
        mockFactory.Verify(
            f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    #endregion

    #region Input Validation

    [Fact]
    public async Task InputValidation_PassResponse_ReturnsAllowed()
    {
        var (mockFactory, _) = CreateMockChatClient(PassResponse);
        var options = CreateOptions(o => o.Enabled = true);
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, options, Mock.Of<ILogger<LlmJudgeGuardrail>>());

        var result = await ((IInputGuardrail)guardrail).ValidateAsync("Hello, how are you?");

        result.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task InputValidation_FailResponse_ReturnsRejected()
    {
        var (mockFactory, _) = CreateMockChatClient(FailResponse);
        var options = CreateOptions(o => o.Enabled = true);
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, options, Mock.Of<ILogger<LlmJudgeGuardrail>>());

        var result = await ((IInputGuardrail)guardrail).ValidateAsync("Some harmful input");

        result.IsAllowed.ShouldBeFalse();
        result.GuardrailName.ShouldBe("LlmJudgeGuardrail");
        result.Reason.ShouldBe("Content contains harmful instructions");
    }

    #endregion

    #region Output Validation

    [Fact]
    public async Task OutputValidation_PassResponse_ReturnsAllowed()
    {
        var (mockFactory, _) = CreateMockChatClient(PassResponse);
        var options = CreateOptions(o => o.Enabled = true);
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, options, Mock.Of<ILogger<LlmJudgeGuardrail>>());

        var result = await ((IOutputGuardrail)guardrail).ValidateAsync("Here is a helpful response about the weather.");

        result.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task OutputValidation_FailResponse_ReturnsRejected()
    {
        var (mockFactory, _) = CreateMockChatClient("FAIL: Response contains inaccurate medical advice");
        var options = CreateOptions(o => o.Enabled = true);
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, options, Mock.Of<ILogger<LlmJudgeGuardrail>>());

        var result = await ((IOutputGuardrail)guardrail).ValidateAsync("Take 10 aspirin for a headache");

        result.IsAllowed.ShouldBeFalse();
        result.GuardrailName.ShouldBe("LlmJudgeGuardrail");
        result.Reason.ShouldBe("Response contains inaccurate medical advice");
    }

    #endregion

    #region Error Handling (Guardrails:FailClosed)

    // 评审不可用（出错 / 超时 / 答复无法解析）时按 Guardrails:FailClosed 处理：默认 true 即拒绝。
    // 此前评审吞掉自己的失败一律放行，ToolGuardrailMiddleware 的 fail-closed 分支永远看不到异常，
    // 而管理端把这个开关显示为「失败即拒绝」。

    [Fact]
    public async Task LlmError_FailClosedByDefault_Rejects()
    {
        var mockFactory = CreateThrowingFactory(new InvalidOperationException("LLM service unavailable"));
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, CreateOptions(o => o.Enabled = true), Mock.Of<ILogger<LlmJudgeGuardrail>>());

        var input = await ((IInputGuardrail)guardrail).ValidateAsync("test input");
        var output = await ((IOutputGuardrail)guardrail).ValidateAsync("test output");

        input.IsAllowed.ShouldBeFalse();
        input.GuardrailName.ShouldBe("LlmJudgeGuardrail");
        output.IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task LlmError_FailOpenConfigured_Allows()
    {
        var mockFactory = CreateThrowingFactory(new InvalidOperationException("LLM service unavailable"));
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, CreateOptions(o => o.Enabled = true, failClosed: false), Mock.Of<ILogger<LlmJudgeGuardrail>>());

        var result = await ((IInputGuardrail)guardrail).ValidateAsync("test input");

        result.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task LlmError_AsGuardrailProvider_DeniesWithUnavailableCode()
    {
        var mockFactory = CreateThrowingFactory(new HttpRequestException("connection refused"));
        IGuardrailProvider guardrail = new LlmJudgeGuardrail(mockFactory.Object, CreateOptions(o => o.Enabled = true), Mock.Of<ILogger<LlmJudgeGuardrail>>());

        var decision = await guardrail.EvaluateAsync(new GuardrailRequest { ToolName = "bash", Content = "rm -rf /" });

        decision.IsAllowed.ShouldBeFalse();
        decision.Reasons.ShouldContain(r => r.Code == GuardrailReasonCodes.LlmJudgeUnavailable);
    }

    [Fact]
    public async Task ExternalCancellation_IsRethrownRegardlessOfFailClosed()
    {
        var mockFactory = CreateThrowingFactory(new OperationCanceledException());
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, CreateOptions(o => o.Enabled = true), Mock.Of<ILogger<LlmJudgeGuardrail>>());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => ((IInputGuardrail)guardrail).ValidateAsync("test input", cts.Token));
    }

    [Fact]
    public async Task Timeout_FailClosedByDefault_Rejects()
    {
        var mockFactory = new Mock<IChatClientFactory>();
        var mockClient = new Mock<IChatClient>();
        mockClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(async (_, _, ct) =>
            {
                // 模拟超时：等待直到超时取消
                await Task.Delay(TimeSpan.FromSeconds(60), ct);
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, PassResponse));
            });

        mockFactory
            .Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockClient.Object);

        var options = CreateOptions(o =>
        {
            o.Enabled = true;
            o.TimeoutSeconds = 1; // 1 秒超时
        });
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, options, Mock.Of<ILogger<LlmJudgeGuardrail>>());

        // 超时即评审不可用：默认失败即拒绝
        var result = await ((IInputGuardrail)guardrail).ValidateAsync("test input");
        result.IsAllowed.ShouldBeFalse();
    }

    #endregion

    #region Custom Prompt

    [Fact]
    public async Task CustomPrompt_UsesConfiguredPrompt()
    {
        var customInputPrompt = "Check if this input is appropriate for children.";
        var customOutputPrompt = "Check if this output follows company guidelines.";

        var (mockFactory, mockClient) = CreateMockChatClient(PassResponse);
        var options = CreateOptions(o =>
        {
            o.Enabled = true;
            o.InputJudgePrompt = customInputPrompt;
            o.OutputJudgePrompt = customOutputPrompt;
        });
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, options, Mock.Of<ILogger<LlmJudgeGuardrail>>());

        // 验证输入 guardrail 使用自定义提示词
        await ((IInputGuardrail)guardrail).ValidateAsync("test input");
        mockClient.Verify(c => c.GetResponseAsync(
            It.Is<IEnumerable<ChatMessage>>(msgs =>
                msgs.Any(m => m.Role == ChatRole.System && m.Text == customInputPrompt)),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // 验证输出 guardrail 使用自定义提示词
        await ((IOutputGuardrail)guardrail).ValidateAsync("test output");
        mockClient.Verify(c => c.GetResponseAsync(
            It.Is<IEnumerable<ChatMessage>>(msgs =>
                msgs.Any(m => m.Role == ChatRole.System && m.Text == customOutputPrompt)),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CustomProvider_UsesConfiguredProviderAndModel()
    {
        var (mockFactory, _) = CreateMockChatClient(PassResponse);
        var options = CreateOptions(o =>
        {
            o.Enabled = true;
            o.Provider = "Anthropic";
            o.Model = "claude-3-haiku";
        });
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, options, Mock.Of<ILogger<LlmJudgeGuardrail>>());

        await ((IInputGuardrail)guardrail).ValidateAsync("test input");

        mockFactory.Verify(f => f.GetChatClient("Anthropic", "claude-3-haiku"), Times.Once);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task FailWithoutReason_ReturnsDefaultReason()
    {
        var (mockFactory, _) = CreateMockChatClient("FAIL");
        var options = CreateOptions(o => o.Enabled = true);
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, options, Mock.Of<ILogger<LlmJudgeGuardrail>>());

        var result = await ((IInputGuardrail)guardrail).ValidateAsync("test input");

        result.IsAllowed.ShouldBeFalse();
        result.Reason.ShouldBe("Content rejected by LLM judge");
    }

    [Theory]
    [InlineData("I think this is fine, but let me explain...")]
    [InlineData("Unsafe: contains personal data")]
    [InlineData("OK")]
    [InlineData("PASSABLE, though it asks for credentials")]
    [InlineData("")]
    public async Task UnparsableResponse_FailClosedByDefault_Rejects(string reply)
    {
        // 只有明确以 PASS 这个词开头的答复才放行：其余都不是「通过」的裁决
        var (mockFactory, _) = CreateMockChatClient(reply);
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, CreateOptions(o => o.Enabled = true), Mock.Of<ILogger<LlmJudgeGuardrail>>());

        var result = await ((IInputGuardrail)guardrail).ValidateAsync("test input");

        result.IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task UnparsableResponse_FailOpenConfigured_Allows()
    {
        var (mockFactory, _) = CreateMockChatClient("I think this is fine, but let me explain...");
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, CreateOptions(o => o.Enabled = true, failClosed: false), Mock.Of<ILogger<LlmJudgeGuardrail>>());

        var result = await ((IInputGuardrail)guardrail).ValidateAsync("test input");

        result.IsAllowed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("PASS")]
    [InlineData("pass.")]
    [InlineData("**PASS**")]
    [InlineData("  PASS - nothing harmful")]
    public async Task PassVerdict_InCommonFormats_Allows(string reply)
    {
        var (mockFactory, _) = CreateMockChatClient(reply);
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, CreateOptions(o => o.Enabled = true), Mock.Of<ILogger<LlmJudgeGuardrail>>());

        var result = await ((IInputGuardrail)guardrail).ValidateAsync("test input");

        result.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task FailVerdict_IsRejectedEvenWhenFailOpenIsConfigured()
    {
        var (mockFactory, _) = CreateMockChatClient("FAIL: asks for credentials");
        var guardrail = new LlmJudgeGuardrail(mockFactory.Object, CreateOptions(o => o.Enabled = true, failClosed: false), Mock.Of<ILogger<LlmJudgeGuardrail>>());

        var result = await ((IInputGuardrail)guardrail).ValidateAsync("test input");

        result.IsAllowed.ShouldBeFalse();
        result.Reason.ShouldBe("asks for credentials");
    }

    #endregion
}

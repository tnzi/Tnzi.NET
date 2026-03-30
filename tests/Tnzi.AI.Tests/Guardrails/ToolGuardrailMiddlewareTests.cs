namespace Tnzi.AI.Tests.Guardrails;

/// <summary>
/// ToolGuardrailMiddleware 单元测试
/// </summary>
public class ToolGuardrailMiddlewareTests
{
    private static IOptions<AIOptions> CreateOptions(bool enabled = true, bool failClosed = true)
    {
        var opts = new AIOptions();
        opts.Guardrails.Enabled = enabled;
        opts.Guardrails.FailClosed = failClosed;
        return Microsoft.Extensions.Options.Options.Create(opts);
    }

    private static AiMiddlewareContext CreateContext()
    {
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest { ThreadId = Guid.NewGuid() },
            Agent = AgentResolution.Success(
                new AgentExecutor(new Mock<IChatClient>().Object, new AgentExecutorOptions { Name = "Test" }),
                "test-provider", "test-model", null),
            Messages = [],
            ServiceProvider = new Mock<IServiceProvider>().Object
        };
    }

    /// <summary>
    /// 创建模拟 next() 委托 — 在执行时将工具调用消息追加到 context.Messages（模拟真实管道行为）
    /// </summary>
    private static AiMiddlewareDelegate CreateNextWithToolCalls(AgentRunResult result, params ChatMessage[] toolCallMessages)
    {
        return (ctx, _) =>
        {
            foreach (var msg in toolCallMessages)
            {
                ctx.Messages.Add(msg);
            }
            return Task.FromResult(result);
        };
    }

    [Fact]
    public void Order_Is655()
    {
        var middleware = new ToolGuardrailMiddleware(
            [], CreateOptions(), NullLogger<ToolGuardrailMiddleware>.Instance);

        middleware.Order.ShouldBe(AiMiddlewareOrders.ToolGuardrail);
        middleware.Order.ShouldBe(655);
    }

    [Fact]
    public async Task GuardrailsDisabled_PassesThrough()
    {
        var middleware = new ToolGuardrailMiddleware(
            [], CreateOptions(enabled: false), NullLogger<ToolGuardrailMiddleware>.Instance);

        var context = CreateContext();
        var result = new AgentRunResult { Response = "ok" };

        var actual = await middleware.InvokeAsync(
            context, (_, _) => Task.FromResult(result), CancellationToken.None);

        actual.Response.ShouldBe("ok");
    }

    [Fact]
    public async Task NoToolCalls_PassesThrough()
    {
        var mockProvider = new Mock<IGuardrailProvider>();
        var middleware = new ToolGuardrailMiddleware(
            [mockProvider.Object], CreateOptions(), NullLogger<ToolGuardrailMiddleware>.Instance);

        var context = CreateContext();
        var result = new AgentRunResult { Response = "no tools" };

        var actual = await middleware.InvokeAsync(
            context, (_, _) => Task.FromResult(result), CancellationToken.None);

        actual.Response.ShouldBe("no tools");
        mockProvider.Verify(p => p.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProviderDeny_ReturnsRejectedResult()
    {
        var mockProvider = new Mock<IGuardrailProvider>();
        mockProvider.Setup(p => p.Name).Returns("TestProvider");
        mockProvider.Setup(p => p.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailDecision.Deny("tool_denied", "Dangerous tool"));

        var middleware = new ToolGuardrailMiddleware(
            [mockProvider.Object], CreateOptions(), NullLogger<ToolGuardrailMiddleware>.Instance);

        var context = CreateContext();
        var toolCallMsg = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_1", "bash", new Dictionary<string, object?> { ["cmd"] = "rm -rf /" })]);
        var result = new AgentRunResult { Response = "ok" };

        // next() 中添加工具调用消息（模拟 LLM 返回工具调用）
        var actual = await middleware.InvokeAsync(
            context, CreateNextWithToolCalls(result, toolCallMsg), CancellationToken.None);

        actual.FinishReason.ShouldBe(FinishReasons.GuardrailRejected);
        actual.Status.ShouldBe(AgentRunStatus.Failed);
        actual.Response.ShouldContain("bash");
        actual.Response.ShouldContain("Dangerous tool");
    }

    [Fact]
    public async Task ProviderAllow_PassesThrough()
    {
        var mockProvider = new Mock<IGuardrailProvider>();
        mockProvider.Setup(p => p.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailDecision.Allow());

        var middleware = new ToolGuardrailMiddleware(
            [mockProvider.Object], CreateOptions(), NullLogger<ToolGuardrailMiddleware>.Instance);

        var context = CreateContext();
        var toolCallMsg = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_1", "file_read", new Dictionary<string, object?> { ["path"] = "/tmp/x" })]);
        var result = new AgentRunResult { Response = "ok" };

        var actual = await middleware.InvokeAsync(
            context, CreateNextWithToolCalls(result, toolCallMsg), CancellationToken.None);

        actual.Response.ShouldBe("ok");
    }

    [Fact]
    public async Task FailClosed_ExceptionTreatedAsDeny()
    {
        var mockProvider = new Mock<IGuardrailProvider>();
        mockProvider.Setup(p => p.Name).Returns("FailingProvider");
        mockProvider.Setup(p => p.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Provider error"));

        var middleware = new ToolGuardrailMiddleware(
            [mockProvider.Object], CreateOptions(failClosed: true), NullLogger<ToolGuardrailMiddleware>.Instance);

        var context = CreateContext();
        var toolCallMsg = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_1", "bash")]);
        var result = new AgentRunResult { Response = "ok" };

        var actual = await middleware.InvokeAsync(
            context, CreateNextWithToolCalls(result, toolCallMsg), CancellationToken.None);

        actual.FinishReason.ShouldBe(FinishReasons.GuardrailRejected);
    }

    [Fact]
    public async Task FailOpen_ExceptionSkipped()
    {
        var mockProvider = new Mock<IGuardrailProvider>();
        mockProvider.Setup(p => p.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Provider error"));

        var middleware = new ToolGuardrailMiddleware(
            [mockProvider.Object], CreateOptions(failClosed: false), NullLogger<ToolGuardrailMiddleware>.Instance);

        var context = CreateContext();
        var toolCallMsg = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_1", "bash")]);
        var result = new AgentRunResult { Response = "ok" };

        var actual = await middleware.InvokeAsync(
            context, CreateNextWithToolCalls(result, toolCallMsg), CancellationToken.None);

        actual.Response.ShouldBe("ok");
    }

    [Fact]
    public async Task HistoricalToolCalls_NotEvaluated()
    {
        // 历史消息中的工具调用不应被评估
        var mockProvider = new Mock<IGuardrailProvider>();
        mockProvider.Setup(p => p.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailDecision.Deny("tool_denied", "Should not evaluate"));

        var middleware = new ToolGuardrailMiddleware(
            [mockProvider.Object], CreateOptions(), NullLogger<ToolGuardrailMiddleware>.Instance);

        var context = CreateContext();
        // 添加历史工具调用（在 next() 之前已存在）
        context.Messages.Add(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_old", "bash")]));

        var result = new AgentRunResult { Response = "ok" };

        var actual = await middleware.InvokeAsync(
            context, (_, _) => Task.FromResult(result), CancellationToken.None);

        // 历史消息不应触发评估，结果应直接通过
        actual.Response.ShouldBe("ok");
        mockProvider.Verify(p => p.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExternalCliMode_SkipsEvaluation()
    {
        var mockProvider = new Mock<IGuardrailProvider>();
        mockProvider.Setup(p => p.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailDecision.Deny("tool_denied", "Should not be called"));

        var middleware = new ToolGuardrailMiddleware(
            [mockProvider.Object], CreateOptions(), NullLogger<ToolGuardrailMiddleware>.Instance);

        // ExternalCli 模式 — ShouldSkipMiddleware 返回 true
        var context = new AiMiddlewareContext
        {
            Request = new AgentRunRequest { ThreadId = Guid.NewGuid() },
            Agent = AgentResolution.SuccessWithoutExecutor(
                "test-provider", "test-model", null, null, AgentExecutionMode.ExternalCli),
            Messages = [],
            ServiceProvider = new Mock<IServiceProvider>().Object
        };

        var toolCallMsg = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_1", "bash")]);
        var result = new AgentRunResult { Response = "cli output" };

        var actual = await middleware.InvokeAsync(
            context, CreateNextWithToolCalls(result, toolCallMsg), CancellationToken.None);

        actual.Response.ShouldBe("cli output");
        mockProvider.Verify(p => p.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

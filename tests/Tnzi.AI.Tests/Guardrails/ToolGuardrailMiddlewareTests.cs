using System.Runtime.CompilerServices;

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

    private static ToolExecutionContext CreateToolContext(string toolName = "bash")
        => new()
        {
            ToolName = toolName,
            CallId = "call_1",
            Arguments = new Dictionary<string, object?> { ["cmd"] = "rm -rf /" },
            CancellationToken = CancellationToken.None
        };

    private static AiMiddlewareContext CreateAiContext()
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

    private static ToolGuardrailMiddleware CreateMiddleware(
        IEnumerable<IGuardrailProvider>? providers = null,
        IOptions<AIOptions>? options = null,
        IEventBus? eventBus = null,
        IAgentExecutionContextAccessor? accessor = null)
        => new(
            providers ?? [],
            options ?? CreateOptions(),
            NullLogger<ToolGuardrailMiddleware>.Instance,
            eventBus,
            accessor);

    [Fact]
    public void Order_Is655()
    {
        var middleware = CreateMiddleware();

        middleware.Order.ShouldBe(AiMiddlewareOrders.ToolGuardrail);
        middleware.Order.ShouldBe(655);
    }

    [Fact]
    public async Task AiPipelineInvokeAsync_PassesThroughWithoutEvaluatingProviders()
    {
        var provider = new Mock<IGuardrailProvider>();
        var middleware = CreateMiddleware([provider.Object]);
        var context = CreateAiContext();
        var result = new AgentRunResult { Response = "ok" };

        var actual = await middleware.InvokeAsync(context, (_, _) => Task.FromResult(result), CancellationToken.None);

        actual.ShouldBe(result);
        provider.Verify(x => x.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ToolInvokeAsync_GuardrailsDisabled_CallsNext()
    {
        var middleware = CreateMiddleware(options: CreateOptions(enabled: false));
        var nextCalled = false;

        var result = await middleware.InvokeAsync(CreateToolContext(), () =>
        {
            nextCalled = true;
            return Task.FromResult<object?>("ok");
        });

        nextCalled.ShouldBeTrue();
        result.ShouldBe("ok");
    }

    [Fact]
    public async Task ToolInvokeAsync_ProviderDeny_BlocksBeforeCoreExecution()
    {
        var provider = new Mock<IGuardrailProvider>();
        provider.Setup(x => x.Name).Returns("Allowlist");
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailDecision.Deny("tool_denied", "Dangerous tool"));

        var middleware = CreateMiddleware([provider.Object]);
        var nextCalled = false;

        var result = await middleware.InvokeAsync(CreateToolContext(), () =>
        {
            nextCalled = true;
            return Task.FromResult<object?>("should-not-run");
        });

        nextCalled.ShouldBeFalse();
        result.ShouldBeOfType<string>().ShouldContain("Dangerous tool");
        provider.Verify(x => x.EvaluateAsync(
            It.Is<GuardrailRequest>(r =>
                r.ToolName == "bash" &&
                r.ToolInput != null &&
                r.ToolInput["cmd"].ToString() == "rm -rf /"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ToolInvokeAsync_ProviderAllow_CallsNext()
    {
        var provider = new Mock<IGuardrailProvider>();
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailDecision.Allow());

        var middleware = CreateMiddleware([provider.Object]);
        var nextCalled = false;

        var result = await middleware.InvokeAsync(CreateToolContext("file_read"), () =>
        {
            nextCalled = true;
            return Task.FromResult<object?>("tool result");
        });

        nextCalled.ShouldBeTrue();
        result.ShouldBe("tool result");
    }

    [Fact]
    public async Task ToolInvokeAsync_FailClosed_ExceptionBlocks()
    {
        var provider = new Mock<IGuardrailProvider>();
        provider.Setup(x => x.Name).Returns("BrokenProvider");
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Provider error"));

        var middleware = CreateMiddleware([provider.Object], CreateOptions(failClosed: true));

        var result = await middleware.InvokeAsync(CreateToolContext(), () => Task.FromResult<object?>("should-not-run"));

        result.ShouldBeOfType<string>().ShouldContain("Guardrail evaluation failed: Provider error");
    }

    [Fact]
    public async Task ToolInvokeAsync_FailOpen_ExceptionAllows()
    {
        var provider = new Mock<IGuardrailProvider>();
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Provider error"));

        var middleware = CreateMiddleware([provider.Object], CreateOptions(failClosed: false));
        var nextCalled = false;

        var result = await middleware.InvokeAsync(CreateToolContext(), () =>
        {
            nextCalled = true;
            return Task.FromResult<object?>("tool result");
        });

        nextCalled.ShouldBeTrue();
        result.ShouldBe("tool result");
    }

    [Fact]
    public async Task ToolInvokeAsync_PublishesGuardrailEventWithCurrentRequest()
    {
        var request = new AgentRunRequest
        {
            UserId = Guid.NewGuid(),
            ThreadId = Guid.NewGuid(),
            AgentId = Guid.NewGuid()
        };
        var accessor = new AgentExecutionContextAccessor { CurrentRequest = request };
        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(x => x.PublishAsync(It.IsAny<GuardrailRejectionEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var provider = new Mock<IGuardrailProvider>();
        provider.Setup(x => x.Name).Returns("Allowlist");
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailDecision.Deny("tool_denied", "Denied"));

        var middleware = CreateMiddleware([provider.Object], eventBus: eventBus.Object, accessor: accessor);

        _ = await middleware.InvokeAsync(CreateToolContext(), () => Task.FromResult<object?>("should-not-run"));

        eventBus.Verify(x => x.PublishAsync(
            It.Is<GuardrailRejectionEvent>(e =>
                e.UserId == request.UserId &&
                e.ThreadId == request.ThreadId &&
                e.GuardrailName == "ToolGuardrail:bash" &&
                e.Direction == GuardrailDirections.Tool &&
                e.Reason == "Denied"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AgentExecutor_ExecuteAsync_BlockedTool_IsNotInvoked()
    {
        var toolInvoked = false;
        var tool = AIFunctionFactory.Create(() =>
        {
            toolInvoked = true;
            return "danger";
        }, "bash", "Dangerous tool");

        var provider = new Mock<IGuardrailProvider>();
        provider.Setup(x => x.Name).Returns("Allowlist");
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailDecision.Deny("tool_denied", "Dangerous tool"));

        var middleware = CreateMiddleware([provider.Object]);
        var callCount = 0;
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken _) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_1", "bash")])]);
                }

                var toolResult = messages.Last().Contents.OfType<FunctionResultContent>().Single();
                toolResult.Result?.ToString().ShouldContain("blocked by guardrail policy");
                return new ChatResponse([new ChatMessage(ChatRole.Assistant, "guardrail handled")]);
            });

        var executor = new AgentExecutor(client.Object, new AgentExecutorOptions
        {
            Name = "TestAgent",
            Tools = [tool],
            Middlewares = [middleware]
        });

        var response = await executor.ExecuteAsync([new ChatMessage(ChatRole.User, "run bash")], CancellationToken.None);

        toolInvoked.ShouldBeFalse();
        response.Text.ShouldBe("guardrail handled");
    }

    [Fact]
    public async Task AgentExecutor_ExecuteStreamingAsync_BlockedTool_IsNotInvoked()
    {
        var toolInvoked = false;
        var tool = AIFunctionFactory.Create(() =>
        {
            toolInvoked = true;
            return "danger";
        }, "bash", "Dangerous tool");

        var provider = new Mock<IGuardrailProvider>();
        provider.Setup(x => x.Name).Returns("Allowlist");
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailDecision.Deny("tool_denied", "Dangerous tool"));

        var middleware = CreateMiddleware([provider.Object]);
        var callCount = 0;
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken ct) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return CreateToolCallStream(ct);
                }

                var toolResult = messages.Last().Contents.OfType<FunctionResultContent>().Single();
                toolResult.Result?.ToString().ShouldContain("blocked by guardrail policy");
                return CreateTextStream(["guard", "rail", " handled"], ct);
            });

        var executor = new AgentExecutor(client.Object, new AgentExecutorOptions
        {
            Name = "TestAgent",
            Tools = [tool],
            Middlewares = [middleware]
        });

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in executor.ExecuteStreamingAsync([new ChatMessage(ChatRole.User, "run bash")], CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        toolInvoked.ShouldBeFalse();
        string.Concat(chunks.Where(x => x.Text != null).Select(x => x.Text)).ShouldContain("guardrail handled");
    }

    [Fact]
    public async Task AgentExecutor_ExecuteStreamingAsync_BlockedTool_MarksToolCallAsFailed()
    {
        var tool = AIFunctionFactory.Create(() => "danger", "bash", "Dangerous tool");

        var provider = new Mock<IGuardrailProvider>();
        provider.Setup(x => x.Name).Returns("Allowlist");
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GuardrailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailDecision.Deny("tool_denied", "Dangerous tool"));

        var middleware = CreateMiddleware([provider.Object]);
        var callCount = 0;
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken ct) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return CreateToolCallStream(ct);
                }

                return CreateTextStream(["guardrail handled"], ct);
            });

        var executor = new AgentExecutor(client.Object, new AgentExecutorOptions
        {
            Name = "TestAgent",
            Tools = [tool],
            Middlewares = [middleware]
        });

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in executor.ExecuteStreamingAsync([new ChatMessage(ChatRole.User, "run bash")], CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        var toolCall = chunks.Single(x => x.ToolCalls is { Count: > 0 }).ToolCalls!.Single();
        toolCall.Name.ShouldBe("bash");
        toolCall.IsSuccess.ShouldBeFalse();
        toolCall.Error.ShouldContain("Dangerous tool");
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateToolCallStream([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new FunctionCallContent("call_1", "bash")]
        };
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateTextStream(
        IEnumerable<string> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent(chunk)]
            };
        }

        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            FinishReason = ChatFinishReason.Stop,
            Contents = [new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 20 })]
        };
    }
}

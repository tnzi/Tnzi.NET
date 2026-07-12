namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// OutputGuardrailMiddleware 单元测试
/// </summary>
public class OutputGuardrailMiddlewareTests
{
    private const string TestProvider = "OpenAI";
    private const string TestModel = "gpt-4o";

    #region Helper

    private static OutputGuardrailMiddleware CreateMiddleware(
        GuardrailRunner? runner = null,
        IEnumerable<IOutputGuardrail>? outputGuardrails = null,
        AIOptions? options = null)
    {
        var opts = options ?? new AIOptions { Guardrails = new GuardrailsOptions { Enabled = true } };
        var guardrails = outputGuardrails ?? [];

        var actualRunner = runner ?? new GuardrailRunner(
            Enumerable.Empty<IInputGuardrail>(),
            guardrails,
            new StaticOptionsMonitor<AIOptions>(opts),
            Mock.Of<ILogger<GuardrailRunner>>());

        return new OutputGuardrailMiddleware(
            actualRunner,
            guardrails,
            new StaticOptionsMonitor<AIOptions>(opts),
            Mock.Of<ILogger<OutputGuardrailMiddleware>>());
    }

    private static AiMiddlewareContext CreateContext()
    {
        var sp = new Mock<IServiceProvider>();
        sp.Setup(x => x.GetService(typeof(IEventBus))).Returns((IEventBus?)null);

        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest
            {
                UserMessage = "Hello",
                UserId = Guid.NewGuid()
            },
            Agent = AgentResolution.Success(
                agent: null!,
                provider: TestProvider,
                model: TestModel,
                agentId: null),
            ServiceProvider = sp.Object
        };
    }

    private static AgentRunResult CreateResult(string response = "AI response") =>
        new()
        {
            Response = response,
            RunId = Guid.NewGuid(),
            ThreadId = Guid.NewGuid(),
            Usage = new TokenUsageDto { InputTokens = 10, OutputTokens = 20 },
            Model = "actual-model",
            HandoffPath = ["planner", "worker"],
            FinalAgentName = "worker",
            Status = AgentRunStatus.Completed,
            Reasoning = "chain",
            Suggestions = ["next question"],
            Todos = [new TodoItemDto { Content = "Ship fix", Status = TodoStatus.Pending, Order = 1 }],
            Artifacts = [new AgentArtifactDto { Id = Guid.NewGuid(), RunId = Guid.NewGuid(), ThreadId = Guid.NewGuid(), VirtualPath = "/artifacts/report.md", FileName = "report.md" }],
            ClarificationQuestion = "Need more detail?"
        };

    #endregion

    #region InvokeAsync

    [Fact]
    public async Task InvokeAsync_GuardrailsDisabled_PassesThrough()
    {
        // Arrange
        var opts = new AIOptions { Guardrails = new GuardrailsOptions { Enabled = false } };
        var middleware = CreateMiddleware(options: opts);
        var context = CreateContext();
        var expected = CreateResult();

        // Act
        var result = await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(expected));

        // Assert
        result.Response.ShouldBe("AI response");
    }

    [Fact]
    public async Task InvokeAsync_OutputPasses_ReturnsOriginalResult()
    {
        // Arrange — Guardrail 通过
        var guardrail = new Mock<IOutputGuardrail>();
        guardrail.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailResult.Allowed());

        var middleware = CreateMiddleware(outputGuardrails: [guardrail.Object]);
        var context = CreateContext();
        var expected = CreateResult("safe output");

        // Act
        var result = await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(expected));

        // Assert
        result.Response.ShouldBe("safe output");
        result.FinishReason.ShouldBeNull(); // 原始 FinishReason 未修改
    }

    [Fact]
    public async Task InvokeAsync_OutputRejected_ReturnsGuardrailRejectedResult()
    {
        // Arrange — Guardrail 拒绝输出
        var guardrail = new Mock<IOutputGuardrail>();
        guardrail.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailResult.Rejected("ContentFilter", "Contains harmful content"));

        var middleware = CreateMiddleware(outputGuardrails: [guardrail.Object]);
        var context = CreateContext();
        var expected = CreateResult("harmful content here");

        // Act
        var result = await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(expected));

        // Assert
        result.FinishReason.ShouldBe(FinishReasons.GuardrailRejected);
        result.Response.ShouldBe("Contains harmful content");
        result.Status.ShouldBe(AgentRunStatus.Failed);
        // 保留原始元数据
        result.RunId.ShouldBe(expected.RunId);
        result.ThreadId.ShouldBe(expected.ThreadId);
    }

    [Fact]
    public async Task InvokeAsync_TripwireThrown_ReturnsGuardrailRejectedResult()
    {
        // Arrange — Guardrail 抛出 TripwireGuardrailException
        var guardrail = new Mock<IOutputGuardrail>();
        guardrail.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TripwireGuardrailException("ToxicContent", "Toxic content detected"));

        var middleware = CreateMiddleware(outputGuardrails: [guardrail.Object]);
        var context = CreateContext();
        var expected = CreateResult("toxic output");

        // Act
        var result = await middleware.InvokeAsync(context, (ctx, ct) => Task.FromResult(expected));

        // Assert
        result.FinishReason.ShouldBe(FinishReasons.GuardrailRejected);
        result.Status.ShouldBe(AgentRunStatus.Failed);
        result.RunId.ShouldBe(expected.RunId);
        result.ThreadId.ShouldBe(expected.ThreadId);
        result.Usage.ShouldBe(expected.Usage);
        result.Model.ShouldBe(expected.Model);
        result.HandoffPath.ShouldBe(expected.HandoffPath);
        result.FinalAgentName.ShouldBe(expected.FinalAgentName);
        result.Reasoning.ShouldBe(expected.Reasoning);
        result.Suggestions.ShouldBe(expected.Suggestions);
        result.Todos.ShouldBe(expected.Todos);
        result.Artifacts.ShouldBe(expected.Artifacts);
        result.ClarificationQuestion.ShouldBe(expected.ClarificationQuestion);
    }

    #endregion

    #region InvokeStreamingAsync

    [Fact]
    public async Task InvokeStreamingAsync_GuardrailsDisabled_PassesThroughAllChunks()
    {
        // Arrange
        var opts = new AIOptions { Guardrails = new GuardrailsOptions { Enabled = false } };
        var middleware = CreateMiddleware(options: opts);
        var context = CreateContext();

        var streamChunks = new[]
        {
            new AgentStreamChunk { Text = "Hello " },
            new AgentStreamChunk { Text = "World" }
        };

        // Act
        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in middleware.InvokeStreamingAsync(context, (ctx, ct) => streamChunks.ToAsyncEnumerable()))
        {
            chunks.Add(chunk);
        }

        // Assert — 禁用时直接透传
        chunks.Count.ShouldBe(2);
        chunks[0].Text.ShouldBe("Hello ");
        chunks[1].Text.ShouldBe("World");
    }

    #endregion
}

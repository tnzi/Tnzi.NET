namespace Tnzi.AI.Tests;

public class ClarificationMiddlewareTests
{
    [Fact]
    public void Order_Is999()
    {
        var middleware = CreateMiddleware();
        Assert.Equal(999, middleware.Order);
    }

    [Fact]
    public async Task InvokeAsync_NoClarification_PassesThrough()
    {
        var middleware = CreateMiddleware();
        var context = TestHelpers.CreateMinimalContext();
        var expectedResult = new AgentRunResult { Response = "normal response" };

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(expectedResult),
            CancellationToken.None);

        Assert.Equal("normal response", result.Response);
        Assert.Null(result.ClarificationQuestion);
    }

    [Fact]
    public async Task InvokeAsync_WithClarificationToolCall_InterruptsExecution()
    {
        var accessor = new AgentExecutionContextAccessor();
        var middleware = CreateMiddleware(accessor);
        var context = TestHelpers.CreateMinimalContext();

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) =>
            {
                // 模拟工具在 next 执行期间写入澄清请求
                accessor.Properties["ClarificationRequest"] = new ClarificationRequest
                {
                    Question = "Which database do you prefer?",
                    Type = ClarificationType.ApproachChoice,
                    Options = ["PostgreSQL", "MySQL", "SQLite"]
                };
                return Task.FromResult(new AgentRunResult { Response = "" });
            },
            CancellationToken.None);

        Assert.Equal(AgentRunStatus.RequiresClarification, result.Status);
        Assert.NotNull(result.ClarificationQuestion);
        Assert.Contains("database", result.ClarificationQuestion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_WithClarification_PreservesExistingResultFields()
    {
        var accessor = new AgentExecutionContextAccessor();
        var middleware = CreateMiddleware(accessor);
        var context = TestHelpers.CreateMinimalContext();
        var threadId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) =>
            {
                accessor.Properties[ContextPropertyKeys.ClarificationRequest] = new ClarificationRequest
                {
                    Question = "Choose one",
                    Type = ClarificationType.ApproachChoice
                };

                return Task.FromResult(new AgentRunResult
                {
                    Response = "original",
                    RunId = runId,
                    ThreadId = threadId,
                    FinishReason = FinishReasons.Stop,
                    Model = "gpt-5-think",
                    Reasoning = "trace",
                    Suggestions = ["next"],
                    Todos =
                    [
                        new TodoItemDto { Content = "task", Order = 1 }
                    ],
                    Artifacts =
                    [
                        new AgentArtifactDto { FileName = "plan.md", VirtualPath = "/artifacts/plan.md" }
                    ]
                });
            },
            CancellationToken.None);

        Assert.Equal(runId, result.RunId);
        Assert.Equal(threadId, result.ThreadId);
        Assert.Equal("gpt-5-think", result.Model);
        Assert.Equal("trace", result.Reasoning);
        Assert.Single(result.Suggestions!);
        Assert.Single(result.Todos!);
        Assert.Single(result.Artifacts!);
        Assert.Equal(FinishReasons.RequiresClarification, result.FinishReason);
        Assert.Equal(AgentRunStatus.RequiresClarification, result.Status);
    }

    [Fact]
    public async Task InvokeAsync_ChineseContent_FormatsWithChineseLabels()
    {
        var accessor = new AgentExecutionContextAccessor();
        var middleware = CreateMiddleware(accessor);
        var context = TestHelpers.CreateMinimalContext();

        accessor.Properties["ClarificationRequest"] = new ClarificationRequest
        {
            Question = "你想用哪个数据库？",
            Type = ClarificationType.ApproachChoice,
            Options = ["PostgreSQL", "MySQL"]
        };

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "" }),
            CancellationToken.None);

        Assert.Equal(AgentRunStatus.RequiresClarification, result.Status);
        // 中文输入应使用中文选项标签
        Assert.Contains("选项", result.ClarificationQuestion!);
        Assert.Contains("数据库", result.ClarificationQuestion!);
    }

    [Fact]
    public async Task InvokeAsync_WithContext_IncludesContextInMessage()
    {
        var accessor = new AgentExecutionContextAccessor();
        var middleware = CreateMiddleware(accessor);
        var context = TestHelpers.CreateMinimalContext();

        accessor.Properties["ClarificationRequest"] = new ClarificationRequest
        {
            Question = "Which approach?",
            Type = ClarificationType.MissingInfo,
            Context = "We need to decide on the architecture."
        };

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "" }),
            CancellationToken.None);

        Assert.Contains("architecture", result.ClarificationQuestion!);
    }

    [Fact]
    public async Task InvokeAsync_ShouldSkipMiddleware_PassesThrough()
    {
        var middleware = CreateMiddleware();
        var context = new AiMiddlewareContext
        {
            Request = new AgentRunRequest { UserMessage = "test" },
            Agent = AgentResolution.Success(null!, "TestProvider", "test-model", null,
                executionMode: AgentExecutionMode.ExternalCli),
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Messages = []
        };

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "pass" }),
            CancellationToken.None);

        Assert.Equal("pass", result.Response);
    }

    [Fact]
    public async Task InvokeAsync_WithOptions_FormatsNumberedList()
    {
        var accessor = new AgentExecutionContextAccessor();
        var middleware = CreateMiddleware(accessor);
        var context = TestHelpers.CreateMinimalContext();

        accessor.Properties["ClarificationRequest"] = new ClarificationRequest
        {
            Question = "Which database?",
            Type = ClarificationType.ApproachChoice,
            Options = ["PostgreSQL", "MySQL"]
        };

        var result = await middleware.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "" }),
            CancellationToken.None);

        Assert.Contains("Options:", result.ClarificationQuestion!);
        Assert.Contains("1. PostgreSQL", result.ClarificationQuestion!);
        Assert.Contains("2. MySQL", result.ClarificationQuestion!);
    }

    private static ClarificationMiddleware CreateMiddleware(IAgentExecutionContextAccessor? accessor = null)
    {
        return new ClarificationMiddleware(
            accessor ?? new AgentExecutionContextAccessor(),
            NullLogger<ClarificationMiddleware>.Instance);
    }
}

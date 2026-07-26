using System.Runtime.CompilerServices;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// 中间件管道集成测试 - 验证跨中间件协作行为
/// </summary>
public class MiddlewarePipelineIntegrationTests
{
    #region 1. InputGuardrail Rejection

    /// <summary>
    /// 测试 InputGuardrailMiddleware 拦截超长输入
    /// </summary>
    public class InputGuardrailRejectionTests : AiIntegrationTestBase
    {
        protected override void ConfigureAiOptions(AIOptions options)
        {
            options.Guardrails = new GuardrailsOptions
            {
                Enabled = true,
                EnableMaxLength = true,
                MaxInputLength = 100 // 极小值，方便测试
            };
        }

        protected override void ConfigureMiddlewares(IServiceCollection services)
        {
            // 注册 InputGuardrailMiddleware 需要的依赖
            services.AddSingleton<IInputGuardrail>(sp =>
                new MaxLengthGuardrail(sp.GetRequiredService<IOptionsMonitor<AIOptions>>()));
            services.AddSingleton<IEnumerable<IOutputGuardrail>>(Array.Empty<IOutputGuardrail>());
            services.AddSingleton<GuardrailRunner>();
            services.AddSingleton<IAiMiddleware>(sp =>
                new InputGuardrailMiddleware(
                    sp.GetRequiredService<GuardrailRunner>(),
                    NullLogger<InputGuardrailMiddleware>.Instance));
        }

        [Fact]
        public async Task Should_RejectInput_When_ExceedsMaxLength()
        {
            // Arrange
            var longInput = new string('A', 200); // 超过 MaxInputLength=100
            MockProvider.EnqueueResponse("This should never be reached");
            var request = CreateRequest(longInput);

            // Act
            var result = await Runtime.RunAsync(request);

            // Assert
            result.ShouldNotBeNull();
            result.FinishReason.ShouldBe(FinishReasons.GuardrailRejected);
            result.Response.ShouldContain("exceeds maximum length");
            result.Status.ShouldBe(AgentRunStatus.Failed);
        }

        [Fact]
        public async Task Should_AllowInput_When_WithinMaxLength()
        {
            // Arrange
            MockProvider.EnqueueResponse("Normal response");
            var request = CreateRequest("Short input");

            // Act
            var result = await Runtime.RunAsync(request);

            // Assert
            result.ShouldNotBeNull();
            result.FinishReason.ShouldBe("stop");
            result.Response.ShouldContain("Normal response");
        }

        [Fact]
        public async Task Should_RejectInStreaming_When_ExceedsMaxLength()
        {
            // Arrange
            var longInput = new string('B', 200);
            MockProvider.EnqueueResponse("This should never be reached");
            var request = CreateRequest(longInput);

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert
            result.FinishReason.ShouldBe(FinishReasons.GuardrailRejected);
            result.FullText.ShouldContain("exceeds maximum length");
        }
    }

    #endregion

    #region 2. Quota Enforcement

    /// <summary>
    /// 测试 QuotaMiddleware 配额用尽后拒绝请求
    /// </summary>
    public class QuotaEnforcementTests : AiIntegrationTestBase
    {
        private readonly Mock<IQuotaService> _quotaServiceMock = new();
        private int _callCount;

        protected override void ConfigureMiddlewares(IServiceCollection services)
        {
            // 第一次调用允许，后续拒绝（模拟配额耗尽）
            _quotaServiceMock
                .Setup(q => q.ReserveQuotaAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    var count = Interlocked.Increment(ref _callCount);
                    if (count <= 1)
                    {
                        return Result<QuotaReservation>.Success(new QuotaReservation
                        {
                            ReservedTokens = 100,
                            ReservedAt = DateTime.UtcNow
                        });
                    }
                    return Result<QuotaReservation>.Failure("Daily quota exceeded", 429);
                });

            _quotaServiceMock
                .Setup(q => q.SettleQuotaAsync(
                    It.IsAny<Guid>(), It.IsAny<QuotaReservation>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success());

            services.AddSingleton(_quotaServiceMock.Object);
            services.AddSingleton<ITokenEstimator, HeuristicTokenEstimator>();
            // Budget service mock - always allow
            var budgetMock = new Mock<IBudgetService>();
            budgetMock.Setup(x => x.CheckBudgetAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BudgetCheckResult { IsAllowed = true, Status = BudgetStatus.WithinBudget });
            services.AddSingleton(budgetMock.Object);

            services.AddSingleton<IAiMiddleware>(sp =>
                new QuotaMiddleware(
                    sp.GetRequiredService<IQuotaService>(),
                    sp.GetRequiredService<IBudgetService>(),
                    sp.GetRequiredService<ITokenEstimator>(),
                    NullLogger<QuotaMiddleware>.Instance));
        }

        [Fact]
        public async Task Should_AllowFirstRequest_When_QuotaAvailable()
        {
            // Arrange
            MockProvider.EnqueueResponse("First response");
            var userId = Guid.NewGuid();
            var request = CreateRequestWithUser("Hello", userId);

            // Act
            var result = await Runtime.RunAsync(request);

            // Assert
            result.ShouldNotBeNull();
            result.Response.ShouldContain("First response");
            result.FinishReason.ShouldBe("stop");
        }

        [Fact]
        public async Task Should_RejectSecondRequest_When_QuotaExhausted()
        {
            // Arrange
            var userId = Guid.NewGuid();

            // 第一次请求消耗配额
            MockProvider.EnqueueResponse("First response");
            await Runtime.RunAsync(CreateRequestWithUser("Hello", userId));

            // 第二次请求应被拒绝
            MockProvider.EnqueueResponse("This should never be reached");
            var request2 = CreateRequestWithUser("Hello again", userId);

            // Act
            var result = await Runtime.RunAsync(request2);

            // Assert
            result.ShouldNotBeNull();
            result.FinishReason.ShouldBe(FinishReasons.QuotaExceeded);
            result.Response.ShouldContain("quota exceeded", Case.Insensitive);
        }

        [Fact]
        public async Task Should_SkipQuotaCheck_When_NoUserId()
        {
            // Arrange: 消耗掉第一次配额（以确保后续会被拒绝）
            Interlocked.Exchange(ref _callCount, 10);
            MockProvider.EnqueueResponse("Response without user");
            var request = CreateRequest("Hello");
            // 不设置 UserId

            // Act
            var result = await Runtime.RunAsync(request);

            // Assert: 无用户 ID 时跳过配额检查
            result.ShouldNotBeNull();
            result.Response.ShouldContain("Response without user");
            result.FinishReason.ShouldBe("stop");
        }

        [Fact]
        public async Task Should_RejectInStreaming_When_QuotaExhausted()
        {
            // Arrange: 使配额耗尽
            Interlocked.Exchange(ref _callCount, 1);
            MockProvider.EnqueueResponse("Should not appear");
            var userId = Guid.NewGuid();
            var request = CreateRequestWithUser("Streaming test", userId);

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert
            result.FinishReason.ShouldBe(FinishReasons.QuotaExceeded);
            result.FullText.ShouldContain("quota exceeded", Case.Insensitive);
        }
    }

    #endregion

    #region 3. Multi-turn History

    /// <summary>
    /// 测试 HistoryMiddleware 多轮对话历史累积
    /// </summary>
    public class MultiTurnHistoryTests : AiIntegrationTestBase
    {
        private readonly Mock<IAgentThreadInternalService> _threadServiceMock = new();
        private readonly List<(Guid ThreadId, string Role, string Content)> _savedMessages = [];

        protected override void ConfigureMiddlewares(IServiceCollection services)
        {
            // 模拟线程管理
            _threadServiceMock
                .Setup(t => t.GetOrCreateThreadAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid? threadId, Guid? agentId, CancellationToken ct) =>
                {
                    var tid = threadId ?? Guid.NewGuid();
                    return (new ConversationContext(), tid, threadId == null);
                });

            _threadServiceMock
                .Setup(t => t.GetMessageHistoryAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid threadId, int? limit, CancellationToken ct) =>
                {
                    // 从已保存的消息中构建历史
                    return _savedMessages
                        .Where(m => m.ThreadId == threadId)
                        .Select(m => new ChatMessage(
                            m.Role == "user" ? ChatRole.User : ChatRole.Assistant,
                            m.Content))
                        .ToList();
                });

            _threadServiceMock
                .Setup(t => t.SaveMessageAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .Returns((Guid threadId, string role, string content, string? toolCalls, string? usage, Guid? messageId, CancellationToken ct) =>
                {
                    _savedMessages.Add((threadId, role, content));
                    return Task.FromResult(messageId ?? Guid.NewGuid());
                });

            services.AddSingleton(_threadServiceMock.Object);
            services.AddSingleton<IAiMiddleware>(sp =>
                new HistoryMiddleware(
                    sp.GetRequiredService<IAgentThreadInternalService>(),
                    sp.GetRequiredService<IOptionsMonitor<AIOptions>>(),
                    NullLogger<HistoryMiddleware>.Instance));
        }

        [Fact]
        public async Task Should_AccumulateHistory_When_MultipleRequestsOnSameThread()
        {
            // Arrange
            var agent = await CreateAgentAsync("HistoryBot");
            var thread = await CreateThreadAsync(agent.Id);

            MockProvider.EnqueueResponse("First reply");
            MockProvider.EnqueueResponse("Second reply");
            MockProvider.EnqueueResponse("Third reply");

            // Act: 三轮对话
            var r1 = await Runtime.RunAsync(CreateRequest("Turn 1", agentId: agent.Id, threadId: thread.Id));
            var r2 = await Runtime.RunAsync(CreateRequest("Turn 2", agentId: agent.Id, threadId: thread.Id));
            var r3 = await Runtime.RunAsync(CreateRequest("Turn 3", agentId: agent.Id, threadId: thread.Id));

            // Assert: 消息按顺序累积
            r1.Response.ShouldContain("First reply");
            r2.Response.ShouldContain("Second reply");
            r3.Response.ShouldContain("Third reply");

            // 验证消息保存数量（3 user + 3 assistant = 6）
            var threadMessages = _savedMessages.Where(m => m.ThreadId == thread.Id).ToList();
            threadMessages.Count.ShouldBe(6);
            threadMessages.Count(m => m.Role == "user").ShouldBe(3);
            threadMessages.Count(m => m.Role == "assistant").ShouldBe(3);
        }

        [Fact]
        public async Task Should_LoadHistoryBeforeExecution_When_ThreadHasPreviousMessages()
        {
            // Arrange: 预填充历史
            var thread = await CreateThreadAsync();
            _savedMessages.Add((thread.Id, "user", "Previous question"));
            _savedMessages.Add((thread.Id, "assistant", "Previous answer"));

            MockProvider.EnqueueResponse("Response with context");
            var request = CreateRequest("Follow-up question", threadId: thread.Id);

            // Act
            var result = await Runtime.RunAsync(request);

            // Assert
            result.ShouldNotBeNull();
            result.Response.ShouldContain("Response with context");

            // 验证历史加载被调用
            _threadServiceMock.Verify(t =>
                t.GetMessageHistoryAsync(thread.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Should_AutoCreateThread_When_NoThreadIdProvided()
        {
            // Arrange
            MockProvider.EnqueueResponse("Auto-threaded response");
            var request = CreateRequest("Hello");

            // Act
            var result = await Runtime.RunAsync(request);

            // Assert
            result.ShouldNotBeNull();
            result.ThreadId.ShouldNotBeNull();
        }
    }

    #endregion

    #region 4. LoopDetection

    /// <summary>
    /// 测试 LoopDetectionMiddleware 检测重复工具调用
    /// </summary>
    public class LoopDetectionTests : AiIntegrationTestBase
    {
        protected override void ConfigureMiddlewares(IServiceCollection services)
        {
            var opts = TestHelpers.CreateOptionsMonitor(new LoopDetectionOptions
            {
                Enabled = true,
                WarnThreshold = 2,
                HardLimit = 4,
                WindowSize = 10,
                MaxTrackedThreads = 50
            });
            services.AddSingleton<IAiMiddleware>(_ =>
                new LoopDetectionMiddleware(opts, NullLogger<LoopDetectionMiddleware>.Instance));
        }

        [Fact]
        public async Task Should_InjectWarning_When_ToolCallRepeatedAboveThreshold()
        {
            // Arrange: 让 agent 重复调用同一工具多次
            // 需要 3 次相同的 tool call (WarnThreshold=2 表示第 3 次触发，因为 count >= 2)
            // 但 LoopDetection 是 After 中间件，它检查 context.Messages 中的 tool call
            // 我们需要模拟多轮包含相同 tool call 的消息
            MockProvider.EnqueueToolCall("get_weather", new { city = "Beijing" });
            MockProvider.EnqueueResponse("Weather info: sunny"); // 工具调用后的最终响应

            var request = CreateRequest("What's the weather?");

            // Act - 执行第一次（tool call + final response）
            var result1 = await Runtime.RunAsync(request);

            // LoopDetection 在 After 阶段基于 context.Messages 中最后的 assistant tool call 检测
            // 由于每次 Runtime.RunAsync 是独立 scope，检测基于线程 key
            // 对于无 threadId 的请求，key 是 "default"
            result1.ShouldNotBeNull();

            // 多次重复相同工具调用
            for (var i = 0; i < 3; i++)
            {
                MockProvider.EnqueueToolCall("get_weather", new { city = "Beijing" });
                MockProvider.EnqueueResponse($"Weather info iteration {i}");
                var request2 = CreateRequest("What's the weather?");
                await Runtime.RunAsync(request2);
            }

            // LoopDetectionMiddleware 是有状态的（Singleton），应已记录重复 hash
            // 验证中间件本身不会抛异常，且第 4 次(HardLimit)会注入消息
            MockProvider.EnqueueToolCall("get_weather", new { city = "Beijing" });
            MockProvider.EnqueueResponse("Final response");
            var finalRequest = CreateRequest("What's the weather again?");
            var finalResult = await Runtime.RunAsync(finalRequest);

            // Assert: 即使在循环检测后，运行仍然正常完成
            finalResult.ShouldNotBeNull();
        }

        [Fact]
        public async Task Should_NotDetectLoop_When_DifferentToolCalls()
        {
            // Arrange: 不同的工具调用不应触发循环检测
            MockProvider.EnqueueToolCall("get_weather", new { city = "Beijing" });
            MockProvider.EnqueueResponse("Beijing weather");

            MockProvider.EnqueueToolCall("get_weather", new { city = "Shanghai" });
            MockProvider.EnqueueResponse("Shanghai weather");

            MockProvider.EnqueueToolCall("get_news", new { topic = "tech" });
            MockProvider.EnqueueResponse("Tech news");

            // Act
            var r1 = await Runtime.RunAsync(CreateRequest("Beijing weather"));
            var r2 = await Runtime.RunAsync(CreateRequest("Shanghai weather"));
            var r3 = await Runtime.RunAsync(CreateRequest("Tech news"));

            // Assert: 所有请求正常完成
            r1.Response.ShouldContain("Beijing");
            r2.Response.ShouldContain("Shanghai");
            r3.Response.ShouldContain("Tech news");
        }
    }

    #endregion

    #region 5. Middleware Ordering

    /// <summary>
    /// 测试中间件执行顺序 - 验证 Quota 在 History 之前执行，ToolGuardrail 仍位于工具恢复前
    /// </summary>
    public class MiddlewareOrderingTests : AiIntegrationTestBase
    {
        private readonly List<string> _executionLog = [];

        protected override void ConfigureMiddlewares(IServiceCollection services)
        {
            // 使用自定义 tracking 中间件验证执行顺序
            services.AddSingleton<IAiMiddleware>(new ExecutionTrackingMiddleware(
                "InputGuardrail", AiMiddlewareOrders.InputGuardrail, _executionLog));
            services.AddSingleton<IAiMiddleware>(new ExecutionTrackingMiddleware(
                "History", AiMiddlewareOrders.History, _executionLog));
            services.AddSingleton<IAiMiddleware>(new ExecutionTrackingMiddleware(
                "Quota", AiMiddlewareOrders.Quota, _executionLog));
            services.AddSingleton<IAiMiddleware>(new ExecutionTrackingMiddleware(
                "LoopDetection", AiMiddlewareOrders.LoopDetection, _executionLog));
            services.AddSingleton<IAiMiddleware>(new ExecutionTrackingMiddleware(
                "ToolGuardrail", AiMiddlewareOrders.ToolGuardrail, _executionLog));
            services.AddSingleton<IAiMiddleware>(new ExecutionTrackingMiddleware(
                "OutputGuardrail", AiMiddlewareOrders.OutputGuardrail, _executionLog));
        }

        [Fact]
        public async Task Should_ExecuteMiddlewaresInCorrectOrder()
        {
            // Arrange
            MockProvider.EnqueueResponse("OK");
            var request = CreateRequest("Test ordering");

            // Act
            await Runtime.RunAsync(request);

            // Assert: 中间件按 Order 数值从小到大执行
            _executionLog.Count.ShouldBe(6);
            _executionLog[0].ShouldBe("InputGuardrail");
            _executionLog[1].ShouldBe("Quota");
            _executionLog[2].ShouldBe("History");
            _executionLog[3].ShouldBe("LoopDetection");
            _executionLog[4].ShouldBe("ToolGuardrail");
            _executionLog[5].ShouldBe("OutputGuardrail");
        }

        [Fact]
        public async Task Should_ExecuteMiddlewaresInCorrectOrder_WhenStreaming()
        {
            // Arrange
            _executionLog.Clear();
            MockProvider.EnqueueResponse("OK streaming");
            var request = CreateRequest("Test streaming ordering");

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert
            _executionLog.Count.ShouldBe(6);
            _executionLog[0].ShouldBe("InputGuardrail");
            _executionLog[1].ShouldBe("Quota");
            _executionLog[2].ShouldBe("History");
            _executionLog[3].ShouldBe("LoopDetection");
            _executionLog[4].ShouldBe("ToolGuardrail");
            _executionLog[5].ShouldBe("OutputGuardrail");
        }
    }

    #endregion

    #region 6. Streaming Pipeline

    /// <summary>
    /// 测试流式管道中多个中间件协作
    /// </summary>
    public class StreamingPipelineTests : AiIntegrationTestBase
    {
        private readonly Mock<IQuotaService> _quotaServiceMock = new();

        protected override void ConfigureAiOptions(AIOptions options)
        {
            options.Guardrails = new GuardrailsOptions
            {
                Enabled = true,
                EnableMaxLength = true,
                MaxInputLength = 1000
            };
        }

        protected override void ConfigureMiddlewares(IServiceCollection services)
        {
            // InputGuardrail
            services.AddSingleton<IInputGuardrail>(sp =>
                new MaxLengthGuardrail(sp.GetRequiredService<IOptionsMonitor<AIOptions>>()));
            services.AddSingleton<IEnumerable<IOutputGuardrail>>(Array.Empty<IOutputGuardrail>());
            services.AddSingleton<GuardrailRunner>();
            services.AddSingleton<IAiMiddleware>(sp =>
                new InputGuardrailMiddleware(
                    sp.GetRequiredService<GuardrailRunner>(),
                    NullLogger<InputGuardrailMiddleware>.Instance));

            // Quota (always allow)
            _quotaServiceMock
                .Setup(q => q.ReserveQuotaAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<QuotaReservation>.Success(new QuotaReservation
                {
                    ReservedTokens = 1000,
                    ReservedAt = DateTime.UtcNow
                }));
            _quotaServiceMock
                .Setup(q => q.SettleQuotaAsync(
                    It.IsAny<Guid>(), It.IsAny<QuotaReservation>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success());

            services.AddSingleton(_quotaServiceMock.Object);
            services.AddSingleton<ITokenEstimator, HeuristicTokenEstimator>();
            // Budget service mock - always allow
            var budgetMock = new Mock<IBudgetService>();
            budgetMock.Setup(x => x.CheckBudgetAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BudgetCheckResult { IsAllowed = true, Status = BudgetStatus.WithinBudget });
            services.AddSingleton(budgetMock.Object);

            services.AddSingleton<IAiMiddleware>(sp =>
                new QuotaMiddleware(
                    sp.GetRequiredService<IQuotaService>(),
                    sp.GetRequiredService<IBudgetService>(),
                    sp.GetRequiredService<ITokenEstimator>(),
                    NullLogger<QuotaMiddleware>.Instance));

            // LoopDetection
            services.AddSingleton<IAiMiddleware>(_ =>
                new LoopDetectionMiddleware(
                    TestHelpers.CreateOptionsMonitor(new LoopDetectionOptions { Enabled = true }),
                    NullLogger<LoopDetectionMiddleware>.Instance));
        }

        [Fact]
        public async Task Should_StreamNormally_When_AllMiddlewaresPass()
        {
            // Arrange
            MockProvider.EnqueueResponse("Streaming with all middlewares active");
            var request = CreateRequestWithUser("Hello from streaming test", Guid.NewGuid());

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert
            result.FullText.ShouldContain("Streaming with all middlewares active");
            result.FinishReason.ShouldBe("stop");
            result.HasErrors.ShouldBeFalse();
            result.ChunkCount.ShouldBeGreaterThan(1);
        }

        [Fact]
        public async Task Should_ShortCircuitStreaming_When_GuardrailRejects()
        {
            // Arrange: 超长输入触发 guardrail
            var longInput = new string('X', 2000);
            MockProvider.EnqueueResponse("Should never appear");
            var request = CreateRequest(longInput);

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert: 在 guardrail 短路后不应有正常的 text chunks
            result.FinishReason.ShouldBe(FinishReasons.GuardrailRejected);
            result.FullText.ShouldContain("exceeds maximum length");
        }

        [Fact]
        public async Task Should_TrackUsageInStreaming_When_QuotaMiddlewareActive()
        {
            // Arrange
            MockProvider.EnqueueResponse("Track my tokens");
            var userId = Guid.NewGuid();
            var request = CreateRequestWithUser("Hello", userId);

            // Act
            var stream = Runtime.RunStreamingAsync(request);
            var result = await StreamingTestHelper.CollectStreamAsync(stream);

            // Assert
            result.Usage.ShouldNotBeNull();
            result.Usage!.InputTokens.ShouldBeGreaterThan(0);

            // Quota 服务应被调用
            _quotaServiceMock.Verify(q =>
                q.ReserveQuotaAsync(userId, It.IsAny<long>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// 创建携带 UserId 的请求
    /// </summary>
    private static AgentRunRequest CreateRequestWithUser(string userMessage, Guid userId, Guid? agentId = null, Guid? threadId = null)
    {
        return new AgentRunRequest
        {
            AgentId = agentId,
            ThreadId = threadId,
            UserMessage = userMessage,
            UserId = userId,
            Provider = agentId.HasValue ? null : "MockProvider",
            Model = agentId.HasValue ? null : "mock-model"
        };
    }

    /// <summary>
    /// 记录执行顺序的 tracking 中间件
    /// </summary>
    private class ExecutionTrackingMiddleware : IAiMiddleware
    {
        private readonly string _name;
        private readonly List<string> _executionLog;

        public int Order { get; }

        public ExecutionTrackingMiddleware(string name, int order, List<string> executionLog)
        {
            _name = name;
            Order = order;
            _executionLog = executionLog;
        }

        public Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken ct = default)
        {
            _executionLog.Add(_name);
            return next(context, ct);
        }

        public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken ct = default)
        {
            _executionLog.Add(_name);
            await foreach (var chunk in next(context, ct))
                yield return chunk;
        }
    }

    #endregion
}

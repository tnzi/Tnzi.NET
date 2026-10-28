using Tnzi.AI.Rag.Dtos;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// 用户直连的 <c>/api/rag/chat|chat/stream|query</c> 受与 <c>/api/ai/chat</c> 相同的按用户治理约束：
/// token 配额、USD 预算、按用户的工具权限规则都认得出调用者。
/// </summary>
/// <remarks>
/// ★ 此前 <see cref="RagChatEngine"/> 构造的运行请求不带 <c>UserId</c>：<c>QuotaMiddleware</c> 见到空用户直接放行（配额与预算都不查），
/// 绑了用户的工具权限规则一律不匹配；<see cref="RagQueryEngine"/> 经 <c>IAiUtility</c> 直调模型，什么都不经过。
/// 任何一个能查到一个可查询知识库的登录用户，都能借这两个端点绕开管理员给他设的额度与工具限制。
/// </remarks>
public class RagEngineGovernanceTests
{
    private static readonly Guid AllowedKb = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TenantId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private readonly Mock<IRagRetriever> _retriever = new();
    private readonly Mock<IAgentDispatchFacade> _dispatcher = new();
    private readonly Mock<IRagAccessAuthorizer> _authorizer = new();
    private readonly Mock<IAiUtility> _aiUtility = new();
    private readonly Mock<IUsageLimitService> _limits = UsageLimitMocks.Allowing();
    private readonly Mock<ICostCalculator> _costs = new();
    private readonly Mock<IBudgetService> _budget = new();
    private readonly Mock<ICurrentTenant> _currentTenant = new();
    private readonly UsageLimitReservation _reservation = new(UserId, DateTime.UtcNow);
    private IServiceProvider _serviceProvider;
    private AgentRunRequest? _captured;

    public RagEngineGovernanceTests()
    {
        _serviceProvider = ServiceProviderFor(UserId);
        _currentTenant.Setup(t => t.Id).Returns(TenantId);

        _retriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<RagRetrievalOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RetrievalResult { Content = "chunk", Score = 0.9 }]);
        _authorizer
            .Setup(a => a.AuthorizeQueryAsync(It.IsAny<IReadOnlyList<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Guid>>.Success([AllowedKb]));
        _dispatcher
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Callback((AgentRunRequest r, CancellationToken _) => _captured = r)
            .ReturnsAsync(new AgentRunResult { Response = "answer", ThreadId = Guid.NewGuid() });
        _dispatcher
            .Setup(r => r.RunStreamingAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Callback((AgentRunRequest r, CancellationToken _) => _captured = r)
            .Returns(Chunks(new AgentStreamChunk { Text = "answer", FinishReason = "stop" }));

        _aiUtility
            .Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("the answer");
        _budget
            .Setup(b => b.CheckBudgetAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BudgetCheckResult { IsAllowed = true, Status = BudgetStatus.WithinBudget });
        _limits
            .Setup(q => q.BeginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UsageLimitReservation>.Success(_reservation));
        _costs
            .Setup(c => c.CalculateCost(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<long>()))
            .Returns((string _, string _, long input, long output, long _, long _) => (input + output) / 1_000_000m);
        _limits
            .Setup(q => q.SettleAsync(It.IsAny<UsageLimitReservation>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
    }

    private static IServiceProvider ServiceProviderFor(Guid? userId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (userId.HasValue)
        {
            var currentUser = new Mock<ICurrentUser>();
            currentUser.Setup(u => u.Id).Returns(userId);
            currentUser.Setup(u => u.IsAuthenticated).Returns(true);
            services.AddSingleton(currentUser.Object);
        }

        return services.BuildServiceProvider();
    }

    private static async IAsyncEnumerable<AgentStreamChunk> Chunks(params AgentStreamChunk[] chunks)
    {
        await Task.CompletedTask;
        foreach (var chunk in chunks)
        {
            yield return chunk;
        }
    }

    private static StaticOptionsMonitor<AIRagOptions> RagOptions() => new(new AIRagOptions());

    private RagChatEngine CreateChatEngine()
        => new(_serviceProvider, _retriever.Object, _dispatcher.Object, _authorizer.Object, RagOptions());

    private RagQueryEngine CreateQueryEngine()
        => new(_serviceProvider, _retriever.Object, _aiUtility.Object, _authorizer.Object,
            _limits.Object, _budget.Object, new HeuristicTokenEstimator(), _currentTenant.Object, RagOptions(), _costs.Object);

    // =====================================================================
    // /api/rag/chat(/stream)：运行请求带上调用者，管线的按用户治理才认得出他
    // =====================================================================

    [Fact]
    public async Task Chat_RunsAsTheCallingUser()
    {
        var result = await CreateChatEngine().ChatAsync(new RagChatRequest { Query = "hi" });

        result.Succeeded.ShouldBeTrue();
        _captured.ShouldNotBeNull();
        _captured.UserId.ShouldBe(UserId);
        _captured.OperationType.ShouldBe(AIOperationType.Chat);
    }

    [Fact]
    public async Task ChatStreaming_RunsAsTheCallingUser()
    {
        await foreach (var _ in CreateChatEngine().ChatStreamingAsync(new RagChatRequest { Query = "hi" }))
        {
        }

        _captured.ShouldNotBeNull();
        _captured.UserId.ShouldBe(UserId);
        // 与 ChatService 同口径：流式与否由用量中间件区分，请求上一律记 Chat
        _captured.OperationType.ShouldBe(AIOperationType.Chat);
    }

    [Fact]
    public async Task Chat_UsageLimitReachedInThePipeline_IsA429_NotAnAnswer()
    {
        _dispatcher
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "You have reached your 5-hour usage limit.",
                FinishReason = FinishReasons.QuotaExceeded,
                ErrorCode = ErrorCodes.UsageLimitReached
            });

        var result = await CreateChatEngine().ChatAsync(new RagChatRequest { Query = "hi" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(429);
        result.ErrorCode.ShouldBe(ErrorCodes.UsageLimitReached);
    }

    // =====================================================================
    // /api/rag/query：不经管线，预算门与用量上限的开窗口 / 结算在引擎里显式做
    // =====================================================================

    [Fact]
    public async Task Query_BeginsAndSettlesTheCallersUsage_ExactlyOnce()
    {
        var result = await CreateQueryEngine().QueryAsync(new RagQueryRequest { Query = "hi" });

        result.Succeeded.ShouldBeTrue();
        _limits.Verify(q => q.BeginAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        // 结算 = 输入估算 + 回答文本估算折算出的费用
        _limits.Verify(q => q.SettleAsync(_reservation, It.Is<decimal>(c => c > 0m), It.IsAny<CancellationToken>()), Times.Once);
        _costs.Verify(c => c.CalculateCost(It.IsAny<string>(), It.IsAny<string>(), It.Is<long>(t => t > 0), It.Is<long>(t => t > 0), 0L, 0L), Times.Once);
        _budget.Verify(b => b.CheckBudgetAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Query_UsageLimitReached_Returns429_WithTheWindow_AndNeverCallsTheModel()
    {
        _limits
            .Setup(q => q.BeginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UsageLimitMocks.Denial(UsageLimitWindow.Weekly));

        var result = await CreateQueryEngine().QueryAsync(new RagQueryRequest { Query = "hi" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(429);
        result.ErrorCode.ShouldBe(ErrorCodes.UsageLimitReached);
        result.ErrorDetails.ShouldBeOfType<UsageLimitDenialDto>().Window.ShouldBe(UsageLimitWindow.Weekly);
        VerifyModelNeverCalled();
        _limits.Verify(q => q.SettleAsync(It.IsAny<UsageLimitReservation>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Query_MeterFailure_FailsClosed_WithoutCallingItAUsageLimit()
    {
        _limits
            .Setup(q => q.BeginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UsageLimitReservation>.Failure("Concurrent update", 409, ErrorCodes.UsageLimitCheckFailed));

        var result = await CreateQueryEngine().QueryAsync(new RagQueryRequest { Query = "hi" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        result.ErrorCode.ShouldBe(ErrorCodes.UsageLimitCheckFailed);
        VerifyModelNeverCalled();
    }

    [Fact]
    public async Task Query_OverBudget_Returns429_BeforeRetrievalOrTheModel()
    {
        _budget
            .Setup(b => b.CheckBudgetAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BudgetCheckResult { IsAllowed = false, Status = BudgetStatus.BudgetExceeded, Reason = "Monthly budget exceeded" });

        var result = await CreateQueryEngine().QueryAsync(new RagQueryRequest { Query = "hi" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(429);
        result.ErrorCode.ShouldBe(ErrorCodes.QuotaExceeded);
        VerifyModelNeverCalled();
        _retriever.Verify(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<RagRetrievalOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        _limits.Verify(q => q.BeginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Query_ModelThrows_StillSettlesTheReservation()
    {
        _aiUtility
            .Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => CreateQueryEngine().QueryAsync(new RagQueryRequest { Query = "hi" }));

        _limits.Verify(q => q.SettleAsync(_reservation, It.Is<decimal>(c => c > 0m), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Query_ModelReturnsNothing_SettlesOnlyTheInputsCost()
    {
        _aiUtility
            .Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateQueryEngine().QueryAsync(new RagQueryRequest { Query = "hi" });

        result.Succeeded.ShouldBeFalse();
        _costs.Verify(c => c.CalculateCost(It.IsAny<string>(), It.IsAny<string>(), It.Is<long>(t => t > 0), 0L, 0L, 0L), Times.Once);
        _limits.Verify(q => q.SettleAsync(_reservation, It.IsAny<decimal>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Query_NoRelevantContext_NeitherReservesNorCallsTheModel()
    {
        _retriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<RagRetrievalOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateQueryEngine().QueryAsync(new RagQueryRequest { Query = "hi" });

        result.Succeeded.ShouldBeTrue();
        VerifyModelNeverCalled();
        _limits.Verify(q => q.BeginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Query_WithoutAnAuthenticatedUser_SkipsTheUserGates_LikeThePipeline()
    {
        _serviceProvider = ServiceProviderFor(null);

        var result = await CreateQueryEngine().QueryAsync(new RagQueryRequest { Query = "hi" });

        result.Succeeded.ShouldBeTrue();
        _limits.Verify(q => q.BeginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _budget.Verify(b => b.CheckBudgetAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void VerifyModelNeverCalled()
        => _aiUtility.Verify(
            u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
}

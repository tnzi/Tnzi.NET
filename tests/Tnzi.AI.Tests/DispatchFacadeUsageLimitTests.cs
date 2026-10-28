using Tnzi.Exceptions;

namespace Tnzi.AI.Tests;

/// <summary>
/// 外部 CLI 的一轮被拒时什么都不留：用量上限与预算在建线程、存用户消息之前拒绝；调度器入队时自己的门（运行时停用、Agent 停用……）
/// 在用户消息写下之后才判得出，那时把这一轮写下的东西撤掉。还有这一轮的模型交给 CLI 的规则。
/// </summary>
public class DispatchFacadeUsageLimitTests
{
    private static readonly Guid AgentId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ExistingThreadId = Guid.NewGuid();
    private static readonly Guid NewThreadId = Guid.NewGuid();

    private readonly Mock<IAgentThreadInternalService> _threads = new();
    private readonly Mock<IAgentThreadService> _threadService = new();
    private readonly Mock<ICliAgentDispatcher> _dispatcher = new();
    private readonly List<CliRunRequestDto> _enqueued = [];
    private readonly Guid _userMessageId = Guid.NewGuid();

    public DispatchFacadeUsageLimitTests()
    {
        _threads.Setup(t => t.GetOrCreateThreadAsync(It.IsAny<Guid?>(), AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid? threadId, Guid? _, CancellationToken _) => threadId is { } id ? (id, false) : (NewThreadId, true));
        _threads.Setup(t => t.SaveMessageAsync(It.IsAny<Guid>(), MessageRole.User, It.IsAny<string>(), null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_userMessageId);
        _threads.Setup(t => t.FindUserMessageAsync(ExistingThreadId, _userMessageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThreadMessageRef(_userMessageId, 7, "hi"));
        _threads.Setup(t => t.DeleteMessagesFromAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _threadService.Setup(t => t.DeleteAsync(It.IsAny<Guid>())).ReturnsAsync(Result.Success());

        _dispatcher.Setup(d => d.EnqueueAsync(It.IsAny<CliRunRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback((CliRunRequestDto r, CancellationToken _) => _enqueued.Add(r))
            .ReturnsAsync(Result<Guid>.Success(Guid.NewGuid()));
    }

    private AgentDispatchFacade CreateFacade(
        IUsageLimitService? usageLimits = null, IBudgetService? budget = null, AiChatOptions? chatOptions = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _dispatcher.Object);
        services.AddScoped(_ => _threads.Object);
        services.AddScoped(_ => _threadService.Object);

        return new AgentDispatchFacade(
            Mock.Of<IAgentRuntime>(MockBehavior.Strict),
            new ClaudeBinding(),
            _dispatcher.Object,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentDispatchFacade>.Instance,
            new StaticOptionsMonitor<AiChatOptions>(chatOptions ?? new AiChatOptions()),
            usageLimits,
            budget);
    }

    private static AgentRunRequest Request(Guid? threadId = null, string? model = null) => new()
    {
        AgentId = AgentId,
        ThreadId = threadId,
        UserMessage = "hi",
        UserId = UserId,
        Model = model
    };

    private static async Task DrainAsync(IAsyncEnumerable<AgentStreamChunk> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    // ── 写下任何东西之前拒绝 ────────────────────────────────────────────────────

    [Fact]
    public async Task UsageLimitReached_IsRefusedBeforeAThreadOrAMessageIsWritten_AndNoRunIsQueued()
    {
        var limits = new Mock<IUsageLimitService>();
        limits.Setup(l => l.CheckAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsageLimitDenialDto { Window = UsageLimitWindow.Weekly, ResetsAt = new DateTime(2026, 10, 23, 2, 0, 0, DateTimeKind.Utc) });

        var error = await Should.ThrowAsync<BusinessException>(() => DrainAsync(CreateFacade(limits.Object).RunStreamingAsync(Request())));

        error.HttpStatusCode.ShouldBe(429);
        error.Code.ShouldBe(ErrorCodes.UsageLimitReached);
        error.ErrorDetails.ShouldBeOfType<UsageLimitDenialDto>().Window.ShouldBe(UsageLimitWindow.Weekly);
        _threads.Verify(t => t.GetOrCreateThreadAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _threads.Verify(t => t.SaveMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _enqueued.ShouldBeEmpty();
    }

    [Fact]
    public async Task UsageLimitReached_InAnExistingThread_LeavesNoUnansweredMessage()
    {
        var limits = new Mock<IUsageLimitService>();
        limits.Setup(l => l.CheckAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsageLimitDenialDto { Window = UsageLimitWindow.Session, ResetsAt = DateTime.UtcNow.AddHours(1) });

        await Should.ThrowAsync<BusinessException>(() => CreateFacade(limits.Object).RunAsync(Request(ExistingThreadId)));

        _threads.Verify(t => t.SaveMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _enqueued.ShouldBeEmpty();
    }

    [Fact]
    public async Task BudgetExhausted_IsRefusedBeforeAnythingIsWritten()
    {
        var budget = new Mock<IBudgetService>();
        budget.Setup(b => b.CheckBudgetAsync(UserId, It.IsAny<Guid?>(), AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BudgetCheckResult { IsAllowed = false, Status = BudgetStatus.BudgetExceeded, Reason = "Monthly budget exceeded" });

        var error = await Should.ThrowAsync<BusinessException>(() => DrainAsync(CreateFacade(budget: budget.Object).RunStreamingAsync(Request())));

        error.HttpStatusCode.ShouldBe(402);
        error.Code.ShouldBe(ErrorCodes.CliBudgetExceeded);
        _threads.Verify(t => t.SaveMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 入队失败：撤掉这一轮写下的东西 ─────────────────────────────────────────

    [Fact]
    public async Task EnqueueRefused_InAnExistingThread_WithdrawsTheUserMessage()
    {
        _dispatcher.Setup(d => d.EnqueueAsync(It.IsAny<CliRunRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("The bound external CLI runtime is unavailable.", 409, ErrorCodes.CliRuntimeNotFound));

        var error = await Should.ThrowAsync<BusinessException>(() => DrainAsync(CreateFacade().RunStreamingAsync(Request(ExistingThreadId))));

        error.HttpStatusCode.ShouldBe(409);
        _threads.Verify(t => t.DeleteMessagesFromAsync(ExistingThreadId, 7, It.IsAny<CancellationToken>()), Times.Once);
        _threadService.Verify(t => t.DeleteAsync(It.IsAny<Guid>()), Times.Never, "the thread was there before this turn");
    }

    [Fact]
    public async Task EnqueueRefused_InANewThread_RemovesTheThreadItCreated()
    {
        _dispatcher.Setup(d => d.EnqueueAsync(It.IsAny<CliRunRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("The agent is disabled.", 400, ErrorCodes.AgentDisabled));

        await Should.ThrowAsync<BusinessException>(() => DrainAsync(CreateFacade().RunStreamingAsync(Request())));

        _threadService.Verify(t => t.DeleteAsync(NewThreadId), Times.Once);
    }

    // ── 这一轮的模型 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ModelInTheModelListForThisRuntime_IsHandedToTheCli()
    {
        var options = new AiChatOptions { Models = [new ChatModelOption { Id = "claude-opus-5-5", Provider = "claude" }] };

        await DrainAsync(CreateFacade(chatOptions: options).RunStreamingAsync(Request(model: "claude-opus-5-5")).Take(1));

        _enqueued.ShouldHaveSingleItem().Model.ShouldBe("claude-opus-5-5");
    }

    [Theory]
    [InlineData("gpt-5", "a model of another provider")]
    [InlineData("claude-mystery", "a model that is not in the model list")]
    public async Task ModelOutsideTheModelListForThisRuntime_IsNotHandedToTheCli(string model, string reason)
    {
        var options = new AiChatOptions
        {
            Models = [new ChatModelOption { Id = "claude-opus-5-5", Provider = "claude" }, new ChatModelOption { Id = "gpt-5", Provider = "OpenAI" }]
        };

        await DrainAsync(CreateFacade(chatOptions: options).RunStreamingAsync(Request(model: model)).Take(1));

        _enqueued.ShouldHaveSingleItem().Model.ShouldBeNull(reason);
    }

    [Fact]
    public async Task WithoutAModelList_TheBindingDecidesTheModel()
    {
        await DrainAsync(CreateFacade().RunStreamingAsync(Request(model: "claude-opus-5-5")).Take(1));

        _enqueued.ShouldHaveSingleItem().Model.ShouldBeNull();
    }

    private sealed class ClaudeBinding : ICliAgentBindingService
    {
        public Task<CliAgentBindingDto?> GetByAgentIdAsync(Guid agentId, CancellationToken cancellationToken = default)
            => Task.FromResult<CliAgentBindingDto?>(new CliAgentBindingDto
            {
                Id = Guid.NewGuid(), AgentId = agentId, CliRuntimeId = Guid.NewGuid(), ProviderKey = "claude", AcceptsAttachments = true
            });

        public Task<Result<CliAgentBindingDto>> UpsertAsync(Guid agentId, UpsertCliAgentBindingDto input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result> DeleteAsync(Guid agentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

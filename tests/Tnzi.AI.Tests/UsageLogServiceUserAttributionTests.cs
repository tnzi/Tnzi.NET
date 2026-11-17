using Microsoft.AspNetCore.Http;

namespace Tnzi.AI.Tests;

/// <summary>
/// 用量日志计在谁名下的 DbContext：用量行加上线程（线程的创建人是推断的最后一级）。
/// </summary>
public class UsageAttributionDbContext : TnziDbContext<UsageAttributionDbContext>
{
    public UsageAttributionDbContext(DbContextOptions<UsageAttributionDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AgentConfiguration());
        modelBuilder.ApplyConfiguration(new AgentThreadConfiguration());
        modelBuilder.ApplyConfiguration(new UsageLogConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// 每一行用量都要记下它计在谁名下：调用方显式给出的用户优先，其次正在执行的运行请求的用户、当前登录用户、
/// 线程的创建人；都没有才留空。按用户的用量查询以这一列为准，不再经线程反查。
/// </summary>
public class UsageLogServiceUserAttributionTests : IntegratedTestBase<UsageAttributionDbContext>
{
    private static readonly Guid ThreadOwnerId = Guid.Parse("0a6f3f0e-7c2b-4b9e-9a40-0d8f6f1c2a01");
    private static readonly Guid SignedInUserId = Guid.Parse("0a6f3f0e-7c2b-4b9e-9a40-0d8f6f1c2a02");
    private static readonly Guid ExplicitUserId = Guid.Parse("0a6f3f0e-7c2b-4b9e-9a40-0d8f6f1c2a03");
    private static readonly Guid RunRequestUserId = Guid.Parse("0a6f3f0e-7c2b-4b9e-9a40-0d8f6f1c2a04");

    // 字段初始化先于基类构造函数执行，ConfigureServices 里注册的就是它
    private readonly SwitchableCurrentUser _currentUser = new();
    private readonly AgentExecutionContextAccessor _executionContext = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ICurrentUser>(_ => _currentUser);
        services.AddSingleton<IAgentExecutionContextAccessor>(_executionContext);
        services.AddScoped<IRepository<UsageLog, Guid>, EFCoreRepository<UsageAttributionDbContext, UsageLog, Guid>>();
        services.AddScoped<IRepository<AgentThread, Guid>, EFCoreRepository<UsageAttributionDbContext, AgentThread, Guid>>();
    }

    public override void Dispose()
    {
        _executionContext.CurrentRequest = null;
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private UsageLogService CreateService() => new(
        ServiceProvider.GetRequiredService<IRepository<UsageLog, Guid>>(),
        new HttpContextAccessor(),
        ServiceProvider,
        costCalculator: null,
        threadRepository: ServiceProvider.GetRequiredService<IRepository<AgentThread, Guid>>());

    private async Task<Guid> SeedThreadOwnedByAsync(Guid ownerId)
    {
        _currentUser.Id = ownerId;
        var thread = new AgentThread { Title = "t" };
        DbContext.Set<AgentThread>().Add(thread);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        _currentUser.Id = null;
        return thread.Id;
    }

    private static UsageLogEntry Entry(Guid? userId = null, Guid? threadId = null) => new()
    {
        OperationType = AIOperationType.Chat, Provider = "OpenAI", Model = "gpt-4o",
        InputTokens = 10, OutputTokens = 5, DurationMs = 100, UserId = userId, ThreadId = threadId
    };

    private async Task<UsageLog> SingleRowAsync()
    {
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<UsageLog>().SingleAsync();
    }

    [Fact]
    public async Task ExplicitUser_WinsOverTheSignedInUserAndTheThreadOwner()
    {
        // 模型网关这类调用方：请求按运行令牌认证，登录上下文里不是计量对象，调用方知道是谁
        var threadId = await SeedThreadOwnedByAsync(ThreadOwnerId);
        _currentUser.Id = SignedInUserId;

        await CreateService().LogUsageAsync(Entry(ExplicitUserId, threadId));

        (await SingleRowAsync()).UserId.ShouldBe(ExplicitUserId);
    }

    [Fact]
    public async Task NoExplicitUser_TheRunningRequestsUserComesBeforeTheSignedInUser()
    {
        // 续跑、后台子运行以运行归属人的身份跑：与线程服务认定发起人的口径相同
        _currentUser.Id = SignedInUserId;
        _executionContext.CurrentRequest = new AgentRunRequest { UserId = RunRequestUserId };

        await CreateService().LogUsageAsync(Entry());

        (await SingleRowAsync()).UserId.ShouldBe(RunRequestUserId);
    }

    [Fact]
    public async Task NoExplicitUser_TheSignedInUserIsUsed()
    {
        var threadId = await SeedThreadOwnedByAsync(ThreadOwnerId);
        _currentUser.Id = SignedInUserId;

        await CreateService().LogUsageAsync(Entry(threadId: threadId));

        (await SingleRowAsync()).UserId.ShouldBe(SignedInUserId);
    }

    [Fact]
    public async Task NobodySignedIn_TheThreadsOwnerIsUsed()
    {
        // 后台调用（线程标题等）只知道线程
        var threadId = await SeedThreadOwnedByAsync(ThreadOwnerId);

        await CreateService().LogUsageAsync(Entry(threadId: threadId));

        (await SingleRowAsync()).UserId.ShouldBe(ThreadOwnerId);
    }

    [Fact]
    public async Task NobodySignedIn_NoThread_TheRowBelongsToNoUser()
    {
        await CreateService().LogUsageAsync(Entry());

        (await SingleRowAsync()).UserId.ShouldBeNull();
    }

    [Fact]
    public async Task NobodySignedIn_UnknownThread_TheRowIsStillWritten()
    {
        await CreateService().LogUsageAsync(Entry(threadId: Guid.NewGuid()));

        (await SingleRowAsync()).UserId.ShouldBeNull();
    }

    [Fact]
    public async Task OverloadsWithoutAUser_StillAttributeTheRow()
    {
        var threadId = await SeedThreadOwnedByAsync(ThreadOwnerId);

        await CreateService().LogUsageAsync("chat", "OpenAI", "gpt-4o", 10, 5, 100, true, threadId: threadId);

        (await SingleRowAsync()).UserId.ShouldBe(ThreadOwnerId);
    }

    [Fact]
    public async Task ImplementationWrittenBeforeTheEntryOverload_ReceivesTheRowThroughTheDefaultMember()
    {
        // 消费方自己实现的 IUsageLogService 只实现了旧重载：新重载的默认实现转给它，调用不会落空
        var legacy = new LegacyUsageLog();

        await ((IUsageLogService)legacy).LogUsageAsync(Entry(ExplicitUserId) with { EstimatedCostUsd = 0.5m, CachedInputTokens = 3 });

        legacy.Calls.ShouldBe(1);
        legacy.Cost.ShouldBe(0.5m);
    }

    private sealed class LegacyUsageLog : IUsageLogService
    {
        public int Calls { get; private set; }

        public decimal? Cost { get; private set; }

        public Task LogUsageAsync(
            string operationType, string provider, string model, int inputTokens, int outputTokens,
            long durationMs, bool isSuccess, string? errorMessage = null, Guid? agentId = null,
            Guid? threadId = null, CancellationToken ct = default)
        {
            Calls++;
            return Task.CompletedTask;
        }

        public Task LogUsageAsync(
            string operationType, string provider, string model, int inputTokens, int outputTokens,
            long durationMs, bool isSuccess, string? errorMessage, Guid? agentId, Guid? threadId,
            decimal? estimatedCostUsd, CancellationToken ct = default)
        {
            Calls++;
            Cost = estimatedCostUsd;
            return Task.CompletedTask;
        }
    }

    private sealed class SwitchableCurrentUser : ICurrentUser
    {
        public Guid? Id { get; set; }

        public bool IsAuthenticated => Id.HasValue;

        public string? UserName => Id?.ToString();

        public Guid? TenantId => null;

        public string[] Roles => [];

        public bool IsInRole(string role) => false;
    }
}

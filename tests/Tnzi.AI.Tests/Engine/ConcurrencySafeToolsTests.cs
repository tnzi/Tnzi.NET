using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tnzi.AI.Infrastructure.Memory;

namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// 标了 <c>IsConcurrencySafe</c> 的内置工具，同一批里的多个调用会被执行器用 <c>Task.WhenAll</c> 并行跑。
/// </summary>
/// <remarks>
/// <para>
/// 工具提供者从运行的请求作用域里解析，同一批调用拿到的是同一个作用域的 <c>DbContext</c>：
/// 并行的第二个调用撞上 EF 的并发检测（"A second operation was started on this context instance"），
/// 模型同时发两个 <c>search_memory</c> 时有一个回「Search failed」，两个 <c>present_files</c> 有一份产物没登记上。
/// <c>write_todos</c> 写的是运行共享的属性包（普通字典），语义是「最后一次写入胜出」，并行时谁最后落笔不确定。
/// </para>
/// <para>
/// 这里用真实的工具元数据（<see cref="ToolScanner"/> 扫出来的定义）、真实的工具提供者与执行器跑一批两个调用。
/// 数据库调用经拦截器放慢，让两个调用确实重叠：顺序执行时这份延迟只是让测试慢一点。
/// </para>
/// </remarks>
public class ConcurrencySafeToolsTests : IDisposable
{
    /// <summary>每条查询在拦截器里多停这么久，足以让并行的另一个调用在它完成之前进入同一个 DbContext。</summary>
    private static readonly TimeSpan QueryHold = TimeSpan.FromMilliseconds(100);

    /// <summary>顺序执行时第一个调用等第二个调用进场的上限（等不到就是顺序执行，照常放行）。</summary>
    private static readonly TimeSpan OverlapWaitBound = TimeSpan.FromSeconds(1);

    private readonly SqliteConnection _connection;
    private readonly ToolStoreDbContext _context;
    private readonly AgentExecutionContextAccessor _accessor = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _threadId = Guid.NewGuid();

    public ConcurrencySafeToolsTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(m => m.Id).Returns(Guid.Empty);
        currentUser.Setup(m => m.IsAuthenticated).Returns(false);

        _context = new ToolStoreDbContext(
            new DbContextOptionsBuilder<ToolStoreDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(new SlowReaderInterceptor(QueryHold))
                .Options,
            currentUser.Object);
        _context.Database.EnsureCreated();

        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        _accessor.CurrentRequest = new AgentRunRequest { UserMessage = "parent", UserId = _userId, ThreadId = _threadId };
        _accessor.RestoreProperties(new Dictionary<string, object>());
    }

    public void Dispose()
    {
        _accessor.CurrentRequest = null;
        _accessor.ClearProperties();
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task TwoSearchMemoryCallsInOneBatch_BothReturnTheStoredMemory()
    {
        var services = BaseServices();
        services.AddScoped<IMemoryStore>(_ => new DatabaseMemoryStore(
            new EFCoreRepository<ToolStoreDbContext, MemoryEntry, Guid>(_context), NullLogger<DatabaseMemoryStore>.Instance));
        services.AddScoped<MemoryTools>();
        var provider = services.BuildServiceProvider();

        var saved = await provider.GetRequiredService<MemoryTools>().SaveMemoryAsync("Refunds are processed within five business days");
        JsonSerializer.Serialize(saved).ShouldContain("saved");
        _context.ChangeTracker.Clear();

        var results = await RunOneBatchAsync<MemoryTools>(provider, null,
            new FunctionCallContent("call_1", "search_memory", new Dictionary<string, object?> { ["query"] = "refunds" }),
            new FunctionCallContent("call_2", "search_memory", new Dictionary<string, object?> { ["query"] = "business days" }));

        results.Count.ShouldBe(2);
        foreach (var result in results)
        {
            result.ShouldNotContain("Search failed");
            result.ShouldContain("five business days");
        }
    }

    [Fact]
    public async Task TwoPresentFilesCallsInOneBatch_RegisterBothArtifacts()
    {
        var services = BaseServices();
        services.AddScoped<IAgentArtifactService>(sp => new AgentArtifactService(
            sp,
            new EFCoreRepository<ToolStoreDbContext, AgentArtifact, Guid>(_context),
            Mock.Of<IRepository<AgentThread, Guid>>()));
        services.AddScoped<ArtifactTools>();
        var provider = services.BuildServiceProvider();

        var results = await RunOneBatchAsync<ArtifactTools>(provider, null,
            new FunctionCallContent("call_1", "present_files", new Dictionary<string, object?> { ["paths"] = new[] { "/mnt/outputs/report.md" } }),
            new FunctionCallContent("call_2", "present_files", new Dictionary<string, object?> { ["paths"] = new[] { "/mnt/outputs/summary.csv" } }));

        results.Count.ShouldBe(2);
        foreach (var result in results)
        {
            result.ShouldNotContain("Errors");
            result.ShouldContain("Successfully presented 1 file(s)");
        }
    }

    [Fact]
    public async Task TwoWriteTodosCallsInOneBatch_RunInCallOrderAndTheLastListWins()
    {
        var services = BaseServices();
        services.AddScoped<TodoTools>();
        var provider = services.BuildServiceProvider();
        var probe = new OverlapProbeMiddleware(OverlapWaitBound);

        await RunOneBatchAsync<TodoTools>(provider, probe,
            new FunctionCallContent("call_1", "write_todos", new Dictionary<string, object?>
            {
                ["items"] = new[] { new { content = "draft the outline", status = "InProgress" } }
            }),
            new FunctionCallContent("call_2", "write_todos", new Dictionary<string, object?>
            {
                ["items"] = new[] { new { content = "draft the outline", status = "Completed" }, new { content = "write the summary", status = "InProgress" } }
            }));

        probe.MaxInFlight.ShouldBe(1, "write_todos calls share the run's property bag and must not overlap");
        var todos = _accessor.Properties[ContextPropertyKeys.Todos].ShouldBeOfType<List<TodoItemDto>>();
        todos.Count.ShouldBe(2, "the second call in the batch is the latest full list");
    }

    private ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAgentExecutionContextAccessor>(_accessor);
        services.AddSingleton<IOptionsMonitor<AIOptions>>(new StaticOptionsMonitor<AIOptions>(new AIOptions()));
        return services;
    }

    /// <summary>
    /// 模型第一轮一次返回给定的几个调用，第二轮收尾；返回第二轮请求里看到的各个工具结果（按调用顺序）。
    /// </summary>
    private static async Task<List<string>> RunOneBatchAsync<TProvider>(
        IServiceProvider provider, IToolExecutionMiddleware? middleware, params FunctionCallContent[] calls)
    {
        var definitions = new ToolScanner(NullLogger<ToolScanner>.Instance)
            .ScanAssembly(typeof(TProvider).Assembly)
            .Where(t => t.ProviderType == typeof(TProvider))
            .ToList();
        var tools = ToolAdapter.ConvertToAITools(definitions, provider);

        List<string>? toolResults = null;
        var round = 0;
        var chatClient = new ScriptedChatClient(messages =>
        {
            round++;
            if (round == 1)
            {
                return new ChatResponse([new ChatMessage(ChatRole.Assistant, [.. calls])]);
            }

            toolResults = messages
                .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
                .OrderBy(r => r.CallId, StringComparer.Ordinal)
                .Select(r => r.Result?.ToString() ?? string.Empty)
                .ToList();
            return new ChatResponse([new ChatMessage(ChatRole.Assistant, "done")]);
        });

        var executor = new AgentExecutor(chatClient, new AgentExecutorOptions
        {
            Name = "batch",
            Tools = tools,
            ToolDefinitions = definitions.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase),
            Middlewares = middleware == null ? null : [middleware],
            MaxToolIterations = 3
        });

        await executor.ExecuteAsync([new ChatMessage(ChatRole.User, "go")]);

        return toolResults.ShouldNotBeNull();
    }

    private sealed class ScriptedChatClient(Func<IEnumerable<ChatMessage>, ChatResponse> handler) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(handler(messages.ToList()));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 记录同时在跑的调用数。第一个调用先等第二个调用进场（等不到就放行）：并行执行时两个调用必然重叠，顺序执行时只是多等一会儿。
    /// </summary>
    private sealed class OverlapProbeMiddleware(TimeSpan waitBound) : IToolExecutionMiddleware
    {
        private readonly TaskCompletionSource _secondEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _inFlight;
        private int _maxInFlight;

        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

        public async Task<object?> InvokeAsync(ToolExecutionContext context, Func<Task<object?>> next)
        {
            var inFlight = Interlocked.Increment(ref _inFlight);
            int observed;
            while (inFlight > (observed = Volatile.Read(ref _maxInFlight))
                && Interlocked.CompareExchange(ref _maxInFlight, inFlight, observed) != observed)
            {
            }

            try
            {
                if (context.CallId == "call_1")
                {
                    await Task.WhenAny(_secondEntered.Task, Task.Delay(waitBound));
                }
                else
                {
                    _secondEntered.TrySetResult();
                }

                return await next();
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    /// <summary>每条查询都在拦截器里停一会儿，模拟真实数据库往返，让并行的调用确实撞在同一个 DbContext 上。</summary>
    private sealed class SlowReaderInterceptor(TimeSpan hold) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await Task.Delay(hold, cancellationToken);
            return result;
        }
    }
}

internal sealed class ToolStoreDbContext : TnziDbContext<ToolStoreDbContext>
{
    public ToolStoreDbContext(DbContextOptions<ToolStoreDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new MemoryEntryConfiguration());
        modelBuilder.ApplyConfiguration(new AgentArtifactConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

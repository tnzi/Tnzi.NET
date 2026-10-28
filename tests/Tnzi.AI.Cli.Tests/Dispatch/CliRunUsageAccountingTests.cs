using System.Runtime.CompilerServices;
using Tnzi.Json;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 一轮记的是这一轮：续接的会话里 CLI 报的是会话累计值，执行器按同一会话上一轮记下的累计值折算，
/// 运行记录、用量日志与用量上限都只认折算后的那份；累计值单独存下，给下一轮当基线。
/// </summary>
/// <remarks>
/// 真实执行器与 SQLite 仓储，只有进程与协议层是假的。夹具数字取自一条真实对话的连续三轮（见 <see cref="CliTurnUsageTests"/>）。
/// </remarks>
public class CliRunUsageAccountingTests : IntegratedTestBase<CliBudgetDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ThreadId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private const string Sonnet = "claude-sonnet-5-5";
    private const string Haiku = "claude-haiku-4-5";

    private readonly string _workspaces = TestScratch.NewPath("usage-accounting");

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliBudgetDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliBudgetDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliBudgetDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliBudgetDbContext, CliRuntime, Guid>>();
    }

    public override void Dispose()
    {
        TestScratch.Delete(_workspaces);
        base.Dispose();
    }

    private static Dictionary<string, CliAgentTokenUsage> At0704() => new()
    {
        [Sonnet] = new() { InputTokens = 36, OutputTokens = 13509, CacheReadTokens = 660760, CacheWriteTokens = 54856, ReportedCostUsd = 0.486738m },
        [Haiku] = new() { InputTokens = 104486, OutputTokens = 4435, ReportedCostUsd = 0.216661m }
    };

    private static Dictionary<string, CliAgentTokenUsage> At0706() => new()
    {
        [Sonnet] = new() { InputTokens = 40, OutputTokens = 14389, CacheReadTokens = 791728, CacheWriteTokens = 57632, ReportedCostUsd = 0.532844m },
        [Haiku] = new() { InputTokens = 115978, OutputTokens = 4848, ReportedCostUsd = 0.240218m }
    };

    private static Dictionary<string, CliAgentTokenUsage> At0707() => new()
    {
        [Sonnet] = new() { InputTokens = 42, OutputTokens = 15140, CacheReadTokens = 859097, CacheWriteTokens = 58628, ReportedCostUsd = 0.557815m },
        [Haiku] = new() { InputTokens = 115978, OutputTokens = 4848, ReportedCostUsd = 0.240218m }
    };

    private static CliAgentResult Totals(IReadOnlyDictionary<string, CliAgentTokenUsage> totals, string sessionId = "sess-1") => new()
    {
        Status = CliRunStatus.Completed,
        Output = "done",
        SessionId = sessionId,
        DurationMs = 1000,
        Usage = totals,
        UsageIsSessionTotal = true
    };

    [Fact]
    public async Task ResumedTurn_RecordsOnlyThisTurn_AndKeepsTheSessionTotalsForTheNextTurn()
    {
        await SeedBindingAsync();
        await SeedFinishedRunAsync(sessionUsage: At0704());
        var run = await SeedClaimedRunAsync();
        var limits = Allowing();
        var usageLog = new CapturingUsageLog();
        var fixture = new ExecutorFixture(this, Totals(At0706())) { UsageLimits = limits.Object, UsageLog = usageLog };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        fixture.Launches.Single().ResumeSessionId.ShouldBe("sess-1");
        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Completed);

        var usage = ReadUsage(persisted.UsageJson);
        usage[Sonnet].ShouldBe(new CliAgentTokenUsage
        {
            InputTokens = 4, OutputTokens = 880, CacheReadTokens = 130968, CacheWriteTokens = 2776, ReportedCostUsd = 0.046106m
        });
        usage[Haiku].ShouldBe(new CliAgentTokenUsage { InputTokens = 11492, OutputTokens = 413, ReportedCostUsd = 0.023557m });
        persisted.EstimatedCostUsd.ShouldBe(0.069663m);
        ReadUsage(persisted.SessionUsageJson).ShouldBe(At0706());

        limits.Verify(l => l.SettleAsync(It.IsAny<UsageLimitReservation>(), 0.069663m, It.IsAny<CancellationToken>()), Times.Once);

        usageLog.Rows.Single(r => r.Model == Sonnet).ShouldBe(new LoggedRow(Sonnet, 4, 880, 130968, 2776, 0.046106m));
        usageLog.Rows.Single(r => r.Model == Haiku).ShouldBe(new LoggedRow(Haiku, 11492, 413, 0, 0, 0.023557m));
    }

    [Fact]
    public async Task ThreeTurnsInARow_TheThirdTurnIsTheDifferenceFromTheSecond()
    {
        await SeedBindingAsync();
        await SeedFinishedRunAsync(sessionUsage: At0704(), completedAt: DateTime.UtcNow.AddMinutes(-3));

        var second = await SeedClaimedRunAsync();
        await new ExecutorFixture(this, Totals(At0706())).Build().ExecuteAsync(second.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var third = await SeedClaimedRunAsync();
        await new ExecutorFixture(this, Totals(At0707())).Build().ExecuteAsync(third.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var usage = ReadUsage((await ReloadAsync(third.Id)).UsageJson);
        usage.Keys.ToList().ShouldBe([Sonnet], "Haiku was not called in the third turn");
        usage[Sonnet].OutputTokens.ShouldBe(751);
        usage[Sonnet].ReportedCostUsd.ShouldBe(0.024971m);
        (await ReloadAsync(third.Id)).EstimatedCostUsd.ShouldBe(0.024971m);
    }

    [Fact]
    public async Task PreviousRowRecordedBeforeSessionTotalsExisted_ItsUsageColumnIsTheBaseline()
    {
        // 升级前写下的运行只有 UsageJson，而那一列存的就是 CLI 报的累计值：升级后的第一轮不能把整段会话再记一遍
        await SeedBindingAsync();
        await SeedFinishedRunAsync(legacyUsage: At0704());
        var run = await SeedClaimedRunAsync();

        await new ExecutorFixture(this, Totals(At0706())).Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        ReadUsage((await ReloadAsync(run.Id)).UsageJson)[Sonnet].OutputTokens.ShouldBe(880);
    }

    [Fact]
    public async Task ResumeRejected_ThenAFreshSession_SumsBothAttempts_AndStartsTheNewSessionsTotals()
    {
        await SeedBindingAsync();
        await SeedFinishedRunAsync(sessionUsage: At0704());
        var run = await SeedClaimedRunAsync();

        // 第一次续接被拒：CLI 没恢复任何总量，报出的就是它花掉的那一点
        var rejected = new CliAgentResult
        {
            Status = CliRunStatus.Failed,
            FailureReason = CliRunFailureReason.ResumeRejected,
            ResumeRejected = true,
            DurationMs = 100,
            UsageIsSessionTotal = true,
            Usage = new Dictionary<string, CliAgentTokenUsage> { [Sonnet] = new() { InputTokens = 3, OutputTokens = 1, ReportedCostUsd = 0.001m } }
        };
        var fresh = Totals(new Dictionary<string, CliAgentTokenUsage>
        {
            [Sonnet] = new() { InputTokens = 10, OutputTokens = 300, CacheWriteTokens = 40000, ReportedCostUsd = 0.2m }
        }, sessionId: "sess-2");
        var limits = Allowing();
        var fixture = new ExecutorFixture(this, rejected, fresh) { UsageLimits = limits.Object };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        fixture.Launches.Select(l => l.ResumeSessionId).ShouldBe(["sess-1", null]);
        var persisted = await ReloadAsync(run.Id);
        persisted.ProviderSessionId.ShouldBe("sess-2");
        ReadUsage(persisted.UsageJson)[Sonnet].ShouldBe(new CliAgentTokenUsage
        {
            InputTokens = 13, OutputTokens = 301, CacheWriteTokens = 40000, ReportedCostUsd = 0.201m
        });
        persisted.EstimatedCostUsd.ShouldBe(0.201m);
        ReadUsage(persisted.SessionUsageJson)[Sonnet].OutputTokens.ShouldBe(300);
        limits.Verify(l => l.SettleAsync(It.IsAny<UsageLimitReservation>(), 0.201m, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelledTurn_RecordsWhatWasStreamed_AndAnEstimatedTotalForTheNextTurn()
    {
        await SeedBindingAsync();
        await SeedFinishedRunAsync(sessionUsage: At0704());
        var run = await SeedClaimedRunAsync();
        var streamed = new Dictionary<string, CliAgentTokenUsage>
        {
            [Sonnet] = new() { InputTokens = 1, OutputTokens = 120, CacheReadTokens = 66000, CacheWriteTokens = 300 }
        };
        var calculator = new Mock<ICostCalculator>();
        calculator.Setup(c => c.CalculateCost("claude", Sonnet, 66301L, 120L, 66000L, 300L)).Returns(0.0215m);
        var stopped = new CliAgentResult
        {
            Status = CliRunStatus.Failed,
            FailureReason = CliRunFailureReason.ProcessCrashed,
            DurationMs = 100,
            Usage = streamed
        };
        var fixture = new ExecutorFixture(this, stopped) { CostCalculator = calculator.Object };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        ReadUsage(persisted.UsageJson)[Sonnet].OutputTokens.ShouldBe(120);
        persisted.EstimatedCostUsd.ShouldBe(0.0215m, "no reported cost: priced from the configured rates, cache reads and writes included");
        var estimate = ReadUsage(persisted.SessionUsageJson);
        estimate[Sonnet].OutputTokens.ShouldBe(13509 + 120);
        estimate[Haiku].ShouldBe(At0704()[Haiku]);
    }

    [Fact]
    public async Task SelectedModel_IsTheTurnsModel_AndABindingsOwnModelArgumentIsDropped()
    {
        await SeedBindingAsync(customArgsJson: """["--model","claude-sonnet-5-5","--max-turns","5","--model=claude-haiku-5-5"]""");
        var run = await SeedClaimedRunAsync(model: "claude-opus-5-5");
        var fixture = new ExecutorFixture(this, Totals(At0704()));

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var launch = fixture.Launches.Single();
        launch.Model.ShouldBe("claude-opus-5-5");
        launch.CustomArgs.ShouldBe(["--max-turns", "5"]);
    }

    [Fact]
    public async Task NoSelectedModel_TheBindingDecides()
    {
        await SeedBindingAsync(customArgsJson: """["--model","claude-sonnet-5-5"]""");
        var run = await SeedClaimedRunAsync();
        var fixture = new ExecutorFixture(this, Totals(At0704()));

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        fixture.Launches.Single().CustomArgs.ShouldBe(["--model", "claude-sonnet-5-5"]);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Mock<IUsageLimitService> Allowing()
    {
        var limits = new Mock<IUsageLimitService>();
        limits.Setup(l => l.BeginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid userId, CancellationToken _) => Result<UsageLimitReservation>.Success(new UsageLimitReservation(userId, DateTime.UtcNow)));
        limits.Setup(l => l.SettleAsync(It.IsAny<UsageLimitReservation>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        return limits;
    }

    private static Dictionary<string, CliAgentTokenUsage> ReadUsage(string? json)
        => JsonSerializer.Deserialize<Dictionary<string, CliAgentTokenUsage>>(json.ShouldNotBeNull(), TnziJsonDefaults.Options)!;

    private async Task SeedBindingAsync(string? customArgsJson = null)
    {
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            MaterializeSkills = false,
            InjectAgentInstructions = false,
            CustomArgsJson = customArgsJson
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId,
            HostId = "TEST-HOST",
            ProviderKey = "claude",
            Name = "claude @ TEST-HOST",
            ExecutablePath = "/nonexistent/claude",
            Status = CliRuntimeStatus.Online
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>同一线程上已经结束的一轮，结束在会话 <c>sess-1</c> 上。</summary>
    private async Task SeedFinishedRunAsync(
        IReadOnlyDictionary<string, CliAgentTokenUsage>? sessionUsage = null,
        IReadOnlyDictionary<string, CliAgentTokenUsage>? legacyUsage = null,
        DateTime? completedAt = null)
    {
        DbContext.Set<CliRun>().Add(new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Completed,
            Prompt = "earlier",
            ProviderSessionId = "sess-1",
            CompletedAt = completedAt ?? DateTime.UtcNow.AddMinutes(-1),
            SessionUsageJson = sessionUsage is null ? null : JsonSerializer.Serialize(sessionUsage, TnziJsonDefaults.Options),
            UsageJson = legacyUsage is null ? null : JsonSerializer.Serialize(legacyUsage, TnziJsonDefaults.Options)
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<CliRun> SeedClaimedRunAsync(string? model = null)
    {
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Dispatched,
            ClaimedByHostId = "TEST-HOST",
            DispatchedAt = DateTime.UtcNow,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            Prompt = "do the thing",
            Model = model
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    private async Task<CliRun> ReloadAsync(Guid runId)
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters().FirstAsync(r => r.Id == runId);

    private static IOptionsMonitor<CliAgentOptions> EnabledOptions()
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });
        return monitor.Object;
    }

    /// <summary>执行器夹具：每次进程尝试按顺序交出一份结果，并记下每次启动拿到的启动上下文。</summary>
    private sealed class ExecutorFixture(CliRunUsageAccountingTests owner, params CliAgentResult[] attempts)
    {
        private readonly Queue<CliAgentResult> _attempts = new(attempts);

        public IUsageLimitService? UsageLimits { get; init; }

        public ICostCalculator? CostCalculator { get; init; }

        public IUsageLogService? UsageLog { get; init; }

        public List<CliAgentLaunchContext> Launches { get; } = [];

        public CliRunExecutor Build()
        {
            var services = owner.ServiceProvider;
            var runs = services.GetRequiredService<IRepository<CliRun, Guid>>();

            var descriptor = new CliProviderDescriptor
            {
                Key = "claude",
                DisplayName = "Claude Code",
                Protocol = CliAgentProtocol.StreamJson,
                DefaultExecutable = "claude",
                ResumeRejectionDetectable = true
            };

            var registry = new Mock<ICliProviderRegistry>();
            registry.Setup(r => r.Find("claude")).Returns(descriptor);

            var adapters = new Mock<ICliProtocolAdapterFactory>();
            adapters.Setup(f => f.IsImplemented(CliAgentProtocol.StreamJson)).Returns(true);
            adapters.Setup(f => f.Create(CliAgentProtocol.StreamJson)).Returns(() => new ScriptedAdapter(this));

            var host = TestCliHost.Starting(() => new ExitedProcess());

            var workspaceRoot = Path.Combine(owner._workspaces, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspaceRoot);
            var workspace = new CliWorkspace
            {
                RootDirectory = workspaceRoot,
                WorkDirectory = workspaceRoot,
                OutputDirectory = workspaceRoot,
                LogDirectory = workspaceRoot
            };
            var preparer = new Mock<ICliWorkspacePreparer>();
            preparer.Setup(p => p.PrepareAsync(It.IsAny<ICliHostTarget>(), It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(workspace);
            preparer.Setup(p => p.ReuseAsync(It.IsAny<ICliHostTarget>(), It.IsAny<string>(), It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(workspace);

            return new CliRunExecutor(
                runs,
                services.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
                services.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
                services.GetRequiredService<IRepository<CliRuntime, Guid>>(),
                TestAgents.Repository(AgentId),
                registry.Object,
                adapters.Object,
                host,
                preparer.Object,
                Mock.Of<ICliBriefComposer>(),
                Mock.Of<ICliMcpConfigComposer>(),
                new CliRunTokenService(runs, NullLogger<CliRunTokenService>.Instance),
                new CliLaunchEnvironmentComposer(Mock.Of<ICliAuthStatusProbe>(), NullLogger<CliLaunchEnvironmentComposer>.Instance),
                Mock.Of<IAgentGrantService>(),
                Mock.Of<ISkillService>(),
                new CliRunSignalHub(),
                EnabledOptions(),
                services.GetRequiredService<ICurrentTenant>(),
                NullLogger<CliRunExecutor>.Instance,
                services.GetRequiredService<IServiceScopeFactory>(),
                costCalculator: CostCalculator,
                usageLogService: UsageLog,
                usageLimits: UsageLimits);
        }

        private sealed class ScriptedAdapter(ExecutorFixture fixture) : ICliProtocolAdapter
        {
            private CliAgentResult? _result;

            public CliAgentProtocol Protocol => CliAgentProtocol.StreamJson;

            public CliProcessSpec BuildProcess(CliAgentLaunchContext context)
            {
                fixture.Launches.Add(context);
                _result = fixture._attempts.Dequeue();
                return new CliProcessSpec { ExecutablePath = context.ExecutablePath, WorkingDirectory = context.WorkingDirectory };
            }

            public async IAsyncEnumerable<CliAgentEvent> RunAsync(
                ICliAgentTransport transport, CliAgentLaunchContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.CompletedTask;
                yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = "done", SessionId = _result!.SessionId };
            }

            public CliAgentResult GetResult(CliSessionOutcome outcome) => _result!;
        }
    }

    private sealed class ExitedProcess : ICliProcess
    {
        public ICliAgentTransport Transport { get; } = new FakeCliAgentTransport([]);

        public int ProcessId => 4242;

        public bool HasExited => true;

        public int? ExitCode => 0;

        public Task TerminateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record LoggedRow(string Model, int InputTokens, int OutputTokens, int CachedInputTokens, int CacheCreationTokens, decimal? Cost);

    /// <summary>记下每一行用量（带成本与缓存指标的那个重载）。</summary>
    private sealed class CapturingUsageLog : IUsageLogService
    {
        public List<LoggedRow> Rows { get; } = [];

        public Task LogUsageAsync(
            string operationType, string provider, string model, int inputTokens, int outputTokens,
            long durationMs, bool isSuccess, string? errorMessage = null, Guid? agentId = null,
            Guid? threadId = null, CancellationToken ct = default)
            => throw new InvalidOperationException("the CLI path logs costs and cache metrics");

        public Task LogUsageAsync(
            string operationType, string provider, string model, int inputTokens, int outputTokens,
            long durationMs, bool isSuccess, string? errorMessage, Guid? agentId, Guid? threadId,
            decimal? estimatedCostUsd, int cachedInputTokens, int cacheCreationTokens, CancellationToken ct = default)
        {
            Rows.Add(new LoggedRow(model, inputTokens, outputTokens, cachedInputTokens, cacheCreationTokens, estimatedCostUsd));
            return Task.CompletedTask;
        }
    }
}

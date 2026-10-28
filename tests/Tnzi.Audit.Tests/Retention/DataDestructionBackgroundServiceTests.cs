using Microsoft.Extensions.Logging;
using Tnzi.Audit.Tests.TestSupport;

namespace Tnzi.Audit.Tests.Retention;

/// <summary>
/// <see cref="DataDestructionBackgroundService"/> 对一轮 <c>RunAsync</c> 结果的分级：只有「另一轮正在跑」是 Debug，其它失败一律 Error。
/// </summary>
/// <remarks>
/// <para>
/// ★ 锁下移到 <c>RunAsync</c> 时后台服务按 <c>Code == 409</c> 把结果降成 Debug，但 409 在这条路径上早就有别的含义：
/// 同名保留策略也答 409。于是一个配了两条同名策略的部署，从每轮一条 Error 变成每轮一条「别人持锁」的 Debug ——
/// 什么都不销毁，而默认日志级别下一个字都看不见。判据必须是专用错误码，不是状态码。
/// </para>
/// <para>
/// 被测对象是真实的后台服务（经 <c>RunOnceAsync</c> 驱动一轮）；<c>IDataDestructionService</c> 是它的依赖，用桩给出各种结果。
/// </para>
/// </remarks>
public class DataDestructionBackgroundServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(DataDestructionOptions.MaxIntervalHours + 1)]
    public void Validator_RejectsAnIntervalTheLoopCannotSleep(int hours)
    {
        // Task.Delay 等不了 49 天以上；放行一个写大了的间隔 = 第一轮之后后台服务抛异常、宿主停机。
        var result = new DataDestructionOptionsValidator().Validate(null, new DataDestructionOptions { Enabled = true, IntervalHours = hours });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(DataDestructionOptions.IntervalHours));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(DataDestructionOptions.MaxIntervalHours)]
    public void Validator_AcceptsAnIntervalWithinTheBounds(int hours)
    {
        new DataDestructionOptionsValidator().Validate(null, new DataDestructionOptions { Enabled = true, IntervalHours = hours })
            .Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task DuplicatePolicyFailure_IsStillLoggedAsError()
    {
        var logger = new CapturingLogger();
        var service = Create(logger, Result<DataDestructionRunDto>.Failure(
            "Duplicate retention policy names: dup. Policy names must be unique.", 409));

        await service.RunOnceAsync(CancellationToken.None);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Message.ShouldContain("Duplicate retention policy names");
    }

    [Fact]
    public async Task AnotherRunInProgress_IsDebugOnly()
    {
        var logger = new CapturingLogger();
        var service = Create(logger, Result<DataDestructionRunDto>.Failure(
            "Another data destruction run is in progress. Try again after it finishes.",
            409, ErrorCodes.AuditDestructionRunInProgress));

        await service.RunOnceAsync(CancellationToken.None);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Debug);
        logger.Entries.ShouldNotContain(e => e.Level == LogLevel.Error);
    }

    /// <summary>锁在中途丢失也是 409，但它不是「跳过这一轮」：策略跑了一半，必须以 Error 报出。</summary>
    [Fact]
    public async Task LockLostMidRun_IsLoggedAsError()
    {
        var logger = new CapturingLogger();
        var service = Create(logger, Result<DataDestructionRunDto>.Failure(
            "The data destruction run stopped because the distributed lock was lost; the remaining policies will run in the next cycle.",
            409, Tnzi.Exceptions.ErrorCodes.DATA_CONFLICT));

        await service.RunOnceAsync(CancellationToken.None);

        logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Error);
    }

    [Fact]
    public async Task OtherFailure_IsLoggedAsError()
    {
        var logger = new CapturingLogger();
        var service = Create(logger, Result<DataDestructionRunDto>.Failure("Running data destruction is a host-level operation", 403));

        await service.RunOnceAsync(CancellationToken.None);

        logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Error);
    }

    /// <summary>
    /// 一轮只销毁 BatchSize 条；还有剩余时同一周期接着跑，而不是睡满 IntervalHours 再来
    /// （否则每天到期量超过 BatchSize 的策略积压只会线性增长）。
    /// </summary>
    [Fact]
    public async Task Backlog_IsWorkedOffWithinTheSameCycle()
    {
        var runs = new Queue<Result<DataDestructionRunDto>>([Pass(destroyed: 500, hasMore: true), Pass(destroyed: 500, hasMore: true), Pass(destroyed: 120, hasMore: false)]);
        var calls = 0;
        var service = Create(new CapturingLogger(), () => { calls++; return runs.Dequeue(); });

        await service.RunOnceAsync(CancellationToken.None);

        calls.ShouldBe(3);
    }

    [Fact]
    public async Task Backlog_StopsAtTheCapPerCycle()
    {
        // 销毁器没让行离开到期集合（只匿名化）时每一轮都「还有剩余」：上限挡住死循环。
        var calls = 0;
        var service = Create(new CapturingLogger(), () => { calls++; return Pass(destroyed: 500, hasMore: true); });

        await service.RunOnceAsync(CancellationToken.None);

        calls.ShouldBe(DataDestructionBackgroundService.MaxRunsPerCycle);
    }

    [Theory]
    [InlineData(true, 500)]   // 空跑：什么都没删，下一轮拿回的是同一批
    [InlineData(false, 0)]    // 一条没销毁（整批保全或销毁器没删）：接着跑只会原地打转
    public async Task NoProgress_DoesNotRunAgain(bool dryRun, int destroyed)
    {
        var calls = 0;
        var service = Create(new CapturingLogger(), () => { calls++; return Pass(destroyed, hasMore: true, dryRun); });

        await service.RunOnceAsync(CancellationToken.None);

        calls.ShouldBe(1);
    }

    private static Result<DataDestructionRunDto> Pass(int destroyed, bool hasMore, bool dryRun = false)
    {
        var run = new DataDestructionRunDto { IsDryRun = dryRun };
        run.Policies.Add(new DataDestructionPolicyResultDto { PolicyName = "p", DestroyedCount = destroyed, HasMore = hasMore });
        return Result<DataDestructionRunDto>.Success(run);
    }

    private static DataDestructionBackgroundService Create(ILogger<DataDestructionBackgroundService> logger, Result<DataDestructionRunDto> outcome)
        => Create(logger, () => outcome);

    private static DataDestructionBackgroundService Create(ILogger<DataDestructionBackgroundService> logger, Func<Result<DataDestructionRunDto>> outcome)
    {
        var services = new ServiceCollection();
        services.AddScoped<IDataDestructionService>(_ => new StubDestructionService(outcome));
        var provider = services.BuildServiceProvider();

        return new DataDestructionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<DataDestructionOptions>(new DataDestructionOptions { Enabled = true }),
            logger);
    }

    private sealed class StubDestructionService(Func<Result<DataDestructionRunDto>> outcome) : IDataDestructionService
    {
        public Task<Result<DataDestructionRunDto>> RunAsync(CancellationToken cancellationToken = default) => Task.FromResult(outcome());

        public Task<Result> VerifyChainAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<IPagedList<DataDestructionDto>>> GetCertificatesAsync(DataDestructionQueryDto query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class CapturingLogger : ILogger<DataDestructionBackgroundService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}

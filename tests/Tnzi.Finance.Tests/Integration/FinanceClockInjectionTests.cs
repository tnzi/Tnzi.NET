namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 财务服务读的是**注入的** <see cref="TimeProvider"/>，不是环境时钟。
/// </summary>
/// <remarks>
/// 两条用例都把时钟拨到一个真实时间绝不可能落在的年份上：读 <c>DateTime.UtcNow</c> 的实现
/// 会拿到真实的今天，两条断言都对不上——这就是"时钟真的被注入了"的行为证据，
/// 而不是靠读代码确认没有 <c>DateTime.UtcNow</c> 字样。
/// </remarks>
public class FinanceClockInjectionTests : FinanceIntegrationTestBase
{
    /// <summary>远离真实"今天"的固定时刻；日内时刻非零，用于验证按日截断确有发生</summary>
    private static readonly DateTimeOffset FixedNow = new(2031, 6, 15, 8, 30, 0, TimeSpan.Zero);

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        // 基类注册的是 TimeProvider.System；后注册者胜出
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
    }

    [Fact]
    public async Task LedgerLock_FutureDateGuard_MeasuresAgainstTheInjectedClock()
    {
        await SeedCoaAsync();

        // 注入时钟的"今天"。按真实时钟判定的话这是好几年后的未来日，会被守卫拒掉
        var result = await InScopeAsync<ILedgerLockService, Result<LedgerLockDto>>(
            s => s.SetAsync(new SetLedgerLockDto { ClosingDate = FixedNow.UtcDateTime.Date, Note = "Filed" }));

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ClosingDate.ShouldBe(FixedNow.UtcDateTime.Date);

        // 守卫本身仍在：多给的那一天之外照样拒
        var tooFar = await InScopeAsync<ILedgerLockService, Result<LedgerLockDto>>(
            s => s.SetAsync(new SetLedgerLockDto { ClosingDate = FixedNow.UtcDateTime.Date.AddDays(2) }));
        tooFar.Succeeded.ShouldBeFalse();
        tooFar.Code.ShouldBe(400);
    }

    [Fact]
    public async Task AccountBalances_WithoutAsOf_DefaultToTheInjectedClocksToday()
    {
        await SeedCoaAsync();
        var cash = await AccountIdByCodeAsync("1110");

        // 不给基准日：默认值由服务按注入时钟取，并截断到当日
        var result = await InScopeAsync<IChartOfAccountsService, Result<List<AccountBalanceDto>>>(
            s => s.GetBalancesAsync([cash]));

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Single().AsOf.ShouldBe(FixedNow.UtcDateTime.Date);
    }

    /// <summary>停在一个固定时刻的时钟（本组用例不需要推进时间）</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

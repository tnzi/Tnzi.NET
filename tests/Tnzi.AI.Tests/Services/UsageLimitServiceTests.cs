using Tnzi.AI.Tests.Integration;

namespace Tnzi.AI.Tests.Services;

/// <summary>
/// 按用户的用量上限：5 小时窗口与每周窗口，按费用计量（真实 SQLite + 真实 EFCoreRepository + UnitOfWorkManager，时钟可控）。
/// </summary>
/// <remarks>
/// 必须跑在真实 EF 上：开窗口、判上限、累加、跨界重置全是 <c>ExecuteUpdate</c>，Mock 仓储既不执行它们也不延迟保存，
/// 「新插的计量行没 flush 就被条件更新读成到上限」这类缺陷在 Mock 下根本不存在。
/// </remarks>
public class UsageLimitServiceTests : IntegratedTestBase<AiIntegrationDbContext>
{
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly ManualClock _clock = new(Start);
    private readonly UsageLimitOptions _limits = new() { SessionLimitUsd = 5m, WeeklyLimitUsd = 40m };
    private readonly List<IEvent> _events = [];

    private IUnitOfWorkManager Manager => ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        var entityManager = new Mock<IEntityManager>();
        entityManager.Setup(m => m.GetAllDbContextTypes()).Returns([typeof(AiIntegrationDbContext)]);
        services.AddSingleton(_ => entityManager.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        services.AddScoped<IRepository<AiUsageMeter, Guid>, EFCoreRepository<AiIntegrationDbContext, AiUsageMeter, Guid>>();
        services.AddScoped<IRepository<AiUsagePolicy, Guid>, EFCoreRepository<AiIntegrationDbContext, AiUsagePolicy, Guid>>();
        services.AddSingleton<IOptionsMonitor<UsageLimitOptions>>(new StaticOptionsMonitor<UsageLimitOptions>(_limits));
        services.AddSingleton<IOptionsMonitor<AiChatOptions>>(new StaticOptionsMonitor<AiChatOptions>(new AiChatOptions()));
        services.AddSingleton<IOptionsMonitor<AIOptions>>(new StaticOptionsMonitor<AIOptions>(new AIOptions()));
        services.AddSingleton<TimeProvider>(_clock);

        var bus = new Mock<IEventBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<UsageLimitThresholdReachedEvent>(), It.IsAny<CancellationToken>()))
            .Callback((UsageLimitThresholdReachedEvent e, CancellationToken _) => _events.Add(e))
            .Returns(Task.CompletedTask);
        bus.Setup(b => b.PublishAsync(It.IsAny<UsageLimitReachedEvent>(), It.IsAny<CancellationToken>()))
            .Callback((UsageLimitReachedEvent e, CancellationToken _) => _events.Add(e))
            .Returns(Task.CompletedTask);
        services.AddSingleton(bus.Object);

        services.AddScoped<IUsagePolicyService, UsagePolicyService>();
        services.AddScoped<IUsageLimitService, UsageLimitService>();
    }

    private IUsageLimitService Service() => ServiceProvider.GetRequiredService<IUsageLimitService>();

    private async Task<AiUsageMeter?> ReadMeterAsync(Guid userId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<AiUsageMeter>().AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId);
    }

    private async Task<UsageLimitReservation> BeginAsync(Guid userId)
    {
        var begun = await Service().BeginAsync(userId);
        begun.Succeeded.ShouldBeTrue($"[{begun.ErrorCode}] {begun.Message}");
        return begun.Data!;
    }

    private async Task SpendAsync(Guid userId, decimal cost)
        => (await Service().SettleAsync(await BeginAsync(userId), cost)).Succeeded.ShouldBeTrue();

    // ── 开窗口 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstEverTurn_UnderAGlobalUnitOfWork_StartsBothWindowsAtThatMoment()
    {
        // 新插的计量行必须先 flush：随后的条件更新是绕开变更跟踪器的 SQL，看不见只在跟踪器里的行，会被读成「到上限了」
        var userId = Guid.NewGuid();
        Manager.EnableTransaction();

        var begun = await Service().BeginAsync(userId);

        begun.Succeeded.ShouldBeTrue($"[{begun.ErrorCode}] {begun.Message}");
        var meter = (await ReadMeterAsync(userId)).ShouldNotBeNull();
        meter.SessionStartedAt.ShouldBe(Start.UtcDateTime);
        meter.WeeklyStartedAt.ShouldBe(Start.UtcDateTime);
        meter.SessionCostUsd.ShouldBe(0m);
        await Manager.CommitTransactionAsync();
    }

    [Fact]
    public async Task Begin_RunsInsideTheRequestsTransaction_SoARollbackUndoesIt()
    {
        var userId = Guid.NewGuid();
        await SpendAsync(userId, 1m);
        _clock.Advance(TimeSpan.FromHours(6));
        Manager.EnableTransaction();

        (await Service().BeginAsync(userId)).Succeeded.ShouldBeTrue();
        DbContext.Database.CurrentTransaction.ShouldNotBeNull();
        await Manager.RollbackTransactionAsync();

        // 回滚撤掉了重开的 5 小时窗口（以及过期清零），库里仍是过期前那一份
        var meter = (await ReadMeterAsync(userId))!;
        meter.SessionStartedAt.ShouldBe(Start.UtcDateTime);
        meter.SessionCostUsd.ShouldBe(1m);
    }

    [Fact]
    public async Task Settle_AddsTheTurnsCostToBothWindows()
    {
        var userId = Guid.NewGuid();
        await SpendAsync(userId, 1.25m);
        await SpendAsync(userId, 0.75m);

        var meter = (await ReadMeterAsync(userId))!;
        meter.SessionCostUsd.ShouldBe(2m);
        meter.WeeklyCostUsd.ShouldBe(2m);
        meter.LastUsedAt.ShouldBe(Start.UtcDateTime);
    }

    [Fact]
    public async Task ConcurrentTurns_BothSettle_NeitherIsLost()
    {
        // 两轮从同一份快照出发各自结算：读改写会丢掉一轮，原子累加不会
        var userId = Guid.NewGuid();
        var first = await BeginAsync(userId);
        UsageLimitReservation second;
        using (var other = ServiceProvider.CreateScope())
        {
            second = (await other.ServiceProvider.GetRequiredService<IUsageLimitService>().BeginAsync(userId)).Data!;
        }

        await Service().SettleAsync(first, 1.5m);
        using (var other = ServiceProvider.CreateScope())
        {
            await other.ServiceProvider.GetRequiredService<IUsageLimitService>().SettleAsync(second, 2.5m);
        }

        (await ReadMeterAsync(userId))!.SessionCostUsd.ShouldBe(4m);
    }

    // ── 到上限 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AtTheSessionLimit_TheNextTurnIsRefused_WithTheWindowAndItsResetTime()
    {
        var userId = Guid.NewGuid();
        await SpendAsync(userId, 5m);
        _clock.Advance(TimeSpan.FromMinutes(90));

        var begun = await Service().BeginAsync(userId);

        begun.Succeeded.ShouldBeFalse();
        begun.Code.ShouldBe(429);
        begun.ErrorCode.ShouldBe(ErrorCodes.UsageLimitReached);
        var denial = begun.ErrorDetails.ShouldBeOfType<UsageLimitDenialDto>();
        denial.Window.ShouldBe(UsageLimitWindow.Session);
        denial.ResetsAt.ShouldBe(Start.UtcDateTime.AddHours(5));
        _events.OfType<UsageLimitReachedEvent>().ShouldHaveSingleItem().Window.ShouldBe(UsageLimitWindow.Session);
    }

    [Fact]
    public async Task ATurnThatStartsUnderTheLimitAndCrossesIt_IsRecordedInFull_AndOnlyTheNextTurnIsRefused()
    {
        var userId = Guid.NewGuid();
        await SpendAsync(userId, 4m);

        var reservation = await BeginAsync(userId);          // 4 < 5：放行
        await Service().SettleAsync(reservation, 3m);         // 跑完越过上限，照常记账

        (await ReadMeterAsync(userId))!.SessionCostUsd.ShouldBe(7m);
        (await Service().BeginAsync(userId)).ErrorCode.ShouldBe(ErrorCodes.UsageLimitReached);
    }

    [Fact]
    public async Task CheckAsync_RefusesWithoutOpeningAWindow()
    {
        var userId = Guid.NewGuid();
        (await Service().CheckAsync(userId)).ShouldBeNull();
        (await ReadMeterAsync(userId)).ShouldBeNull("a read-only check writes nothing");

        await SpendAsync(userId, 5m);
        var denial = await Service().CheckAsync(userId);

        denial.ShouldNotBeNull();
        denial.Window.ShouldBe(UsageLimitWindow.Session);
    }

    [Fact]
    public async Task BothWindowsAtTheirLimits_ReportTheWindowThatResetsLater()
    {
        _limits.SessionLimitUsd = 5m;
        _limits.WeeklyLimitUsd = 5m;
        var userId = Guid.NewGuid();
        await SpendAsync(userId, 6m);

        var denial = (await Service().CheckAsync(userId)).ShouldNotBeNull();

        denial.Window.ShouldBe(UsageLimitWindow.Weekly, "you cannot use it again until the later reset");
        denial.ResetsAt.ShouldBe(Start.UtcDateTime.AddDays(7));
    }

    [Fact]
    public async Task ALimitOfZero_RefusesEveryTurn_WithNoResetTime()
    {
        _limits.SessionLimitUsd = 0m;
        var userId = Guid.NewGuid();

        var begun = await Service().BeginAsync(userId);

        begun.ErrorCode.ShouldBe(ErrorCodes.UsageLimitReached);
        begun.ErrorDetails.ShouldBeOfType<UsageLimitDenialDto>().ResetsAt.ShouldBeNull();
    }

    [Fact]
    public async Task UnlimitedUser_IsNeverRefused_ButStillMetered()
    {
        _limits.SessionLimitUsd = null;
        _limits.WeeklyLimitUsd = null;
        var userId = Guid.NewGuid();
        await SpendAsync(userId, 500m);

        (await Service().BeginAsync(userId)).Succeeded.ShouldBeTrue();
        var status = await Service().GetStatusAsync(userId);
        status.Windows.ShouldAllBe(w => w.LimitUsd == null && w.UsedPercent == null && !w.Exceeded);
        status.Windows.First().UsedUsd.ShouldBe(500m);
    }

    // ── 窗口的时间规则 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SessionWindow_ResetsFiveHoursAfterItStarted_AndTheNextUseOpensANewOne()
    {
        var userId = Guid.NewGuid();
        await SpendAsync(userId, 5m);
        _clock.Advance(TimeSpan.FromHours(5));

        var begun = await Service().BeginAsync(userId);

        begun.Succeeded.ShouldBeTrue("the 5-hour window has reset");
        var meter = (await ReadMeterAsync(userId))!;
        meter.SessionStartedAt.ShouldBe(Start.UtcDateTime.AddHours(5));
        meter.SessionCostUsd.ShouldBe(0m);
        meter.WeeklyCostUsd.ShouldBe(5m, "the weekly window keeps counting");
        meter.WeeklyStartedAt.ShouldBe(Start.UtcDateTime);
    }

    [Fact]
    public async Task SessionWindow_WithNoUseAfterItEnds_HasNoResetTime_UntilTheNextTurn()
    {
        var userId = Guid.NewGuid();
        await SpendAsync(userId, 1m);
        _clock.Advance(TimeSpan.FromHours(7));

        var session = (await Service().GetStatusAsync(userId)).Windows.Single(w => w.Window == UsageLimitWindow.Session);

        session.StartedAt.ShouldBeNull();
        session.ResetsAt.ShouldBeNull();
        session.UsedUsd.ShouldBe(0m);
        session.UsedPercent.ShouldBe(0m);
    }

    [Fact]
    public async Task WeeklyWindow_PeriodsFollowEachOther_SoTheResetStaysOnTheSameWeekdayAndTime()
    {
        var userId = Guid.NewGuid();
        await SpendAsync(userId, 30m);
        _clock.Advance(TimeSpan.FromDays(16));                 // 第三个周期里（第 14 天起）

        var weekly = (await Service().GetStatusAsync(userId)).Windows.Single(w => w.Window == UsageLimitWindow.Weekly);
        weekly.StartedAt.ShouldBe(Start.UtcDateTime.AddDays(14));
        weekly.ResetsAt.ShouldBe(Start.UtcDateTime.AddDays(21));
        weekly.UsedUsd.ShouldBe(0m);

        await SpendAsync(userId, 2m);
        var meter = (await ReadMeterAsync(userId))!;
        meter.WeeklyStartedAt.ShouldBe(Start.UtcDateTime.AddDays(14));
        meter.WeeklyCostUsd.ShouldBe(2m);
    }

    [Fact]
    public async Task ATurnStillRunningWhenItsWindowEnds_IsRecordedInTheNewWindow()
    {
        var userId = Guid.NewGuid();
        var reservation = await BeginAsync(userId);
        _clock.Advance(TimeSpan.FromHours(5).Add(TimeSpan.FromMinutes(10)));

        await Service().SettleAsync(reservation, 2m);

        var meter = (await ReadMeterAsync(userId))!;
        meter.SessionStartedAt.ShouldBe(Start.UtcDateTime.AddHours(5).AddMinutes(10));
        meter.SessionCostUsd.ShouldBe(2m);
    }

    // ── 事件、状态、管理 ───────────────────────────────────────────────────────

    [Fact]
    public async Task CrossingTheWarningThreshold_PublishesOnce_AndCrossingTheLimitPublishesAgain()
    {
        var userId = Guid.NewGuid();
        await SpendAsync(userId, 3.9m);                        // 78%
        _events.ShouldBeEmpty();

        await SpendAsync(userId, 0.2m);                        // 82%：越过 80%
        await SpendAsync(userId, 0.1m);                        // 84%：已在线上，不再发
        var warnings = _events.OfType<UsageLimitThresholdReachedEvent>().ToList();
        warnings.ShouldHaveSingleItem().LimitReached.ShouldBeFalse();
        warnings[0].Window.ShouldBe(UsageLimitWindow.Session);

        await SpendAsync(userId, 1m);                          // 104%：越过上限
        _events.OfType<UsageLimitThresholdReachedEvent>().Last().LimitReached.ShouldBeTrue();
    }

    [Fact]
    public async Task MyStatus_HasPercentagesAndResetTimes_ButNoAmounts()
    {
        var userId = Guid.NewGuid();
        await SpendAsync(userId, 4.5m);

        var mine = await Service().GetMyStatusAsync(userId);

        mine.CheckedAt.ShouldBe(Start.UtcDateTime);
        var session = mine.Windows.Single(w => w.Window == UsageLimitWindow.Session);
        session.Limited.ShouldBeTrue();
        session.UsedPercent.ShouldBe(90m);
        session.WarningReached.ShouldBeTrue();
        session.ResetsAt.ShouldBe(Start.UtcDateTime.AddHours(5));
        typeof(MyUsageWindowDto).GetProperties().Select(p => p.Name).ShouldNotContain(n => n.Contains("Usd"));
    }

    [Fact]
    public async Task Reset_ClearsBothWindows()
    {
        var userId = Guid.NewGuid();
        await SpendAsync(userId, 5m);

        await Service().ResetAsync(userId);

        (await Service().BeginAsync(userId)).Succeeded.ShouldBeTrue();
        (await ReadMeterAsync(userId))!.WeeklyCostUsd.ShouldBe(0m);
    }

    [Fact]
    public async Task UsersList_ShowsEachUsersWindows_AgainstTheirOwnLimits()
    {
        var heavy = Guid.NewGuid();
        var vip = Guid.NewGuid();
        await SpendAsync(heavy, 2.5m);
        await SpendAsync(vip, 2.5m);
        await ServiceProvider.GetRequiredService<IUsagePolicyService>().SaveUserPolicyAsync(vip, new SaveUsagePolicyDto
        {
            SessionLimit = new UsageLimitSettingDto { Mode = UsageLimitMode.Unlimited },
            WeeklyLimit = new UsageLimitSettingDto { Mode = UsageLimitMode.Inherit }
        });

        var page = (await Service().GetUsersAsync(new UsageMeterQueryDto())).Data!;

        page.TotalCount.ShouldBe(2);
        var heavyRow = page.Items.Single(r => r.UserId == heavy);
        heavyRow.HasCustomPolicy.ShouldBeFalse();
        heavyRow.Windows[0].UsedPercent.ShouldBe(50m);
        var vipRow = page.Items.Single(r => r.UserId == vip);
        vipRow.HasCustomPolicy.ShouldBeTrue();
        vipRow.Windows[0].LimitUsd.ShouldBeNull();
        vipRow.Windows[1].LimitUsd.ShouldBe(40m);
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}

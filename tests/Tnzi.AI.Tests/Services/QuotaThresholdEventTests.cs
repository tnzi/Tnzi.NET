using Tnzi.AI.Tests.Integration;

namespace Tnzi.AI.Tests.Services;

/// <summary>
/// <see cref="QuotaThresholdReachedEvent"/> 必须由生产路径（<c>ReserveQuotaAsync</c>）发出。
/// </summary>
/// <remarks>
/// 发布点此前只在 <c>CheckQuotaAsync</c> 里，而运行时唯一入口 <c>QuotaMiddleware</c> 只走
/// <c>ReserveQuotaAsync</c>：管理端设了 80%/95% 阈值、界面也按 DTO 派生正确显示预警等级，
/// 但按文档订阅事件的宿主一条都收不到，用户直接从「无预警」跳到 429。
/// 语义：只在等级<b>上升</b>（None→Warning、Warning→Critical、None→Critical）那一次预留时发布，
/// 已在阈值之上的后续预留不重复告警。
/// </remarks>
public class QuotaThresholdEventTests : IntegratedTestBase<AiIntegrationDbContext>
{
    private const long DailyLimit = 100;
    private const long MonthlyLimit = 1_000_000;

    private readonly List<QuotaThresholdReachedEvent> _published = [];

    protected override void ConfigureServices(IServiceCollection services)
    {
        var mapperConfig = new TypeAdapterConfig();
        MapperExtensions.SetMapper(new Mapper(mapperConfig));

        services.AddScoped<IRepository<UserQuota, Guid>,
            EFCoreRepository<AiIntegrationDbContext, UserQuota, Guid>>();

        var options = new AIOptions
        {
            Quota = new QuotaOptions
            {
                DefaultDailyTokenLimit = DailyLimit,
                DefaultMonthlyTokenLimit = MonthlyLimit
            }
        };
        services.AddSingleton<IOptionsMonitor<AIOptions>>(new StaticOptionsMonitor<AIOptions>(options));

        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(b => b.PublishAsync(It.IsAny<QuotaThresholdReachedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<QuotaThresholdReachedEvent, CancellationToken>((evt, _) => _published.Add(evt))
            .Returns(Task.CompletedTask);
        services.AddSingleton(eventBus.Object);

        services.AddScoped<IQuotaService, QuotaService>();
    }

    private IQuotaService CreateService() => ServiceProvider.GetRequiredService<IQuotaService>();

    [Fact]
    public async Task ReserveQuotaAsync_CrossingWarningThreshold_PublishesWarning()
    {
        var userId = Guid.NewGuid();
        var service = CreateService();

        (await service.ReserveQuotaAsync(userId, 70)).Succeeded.ShouldBeTrue();
        _published.ShouldBeEmpty();

        (await service.ReserveQuotaAsync(userId, 15)).Succeeded.ShouldBeTrue();

        var evt = _published.ShouldHaveSingleItem();
        evt.UserId.ShouldBe(userId);
        evt.Level.ShouldBe(nameof(QuotaWarningLevel.Warning));
        evt.DailyUsagePercentage.ShouldBe(0.85m);
        evt.RemainingDailyQuota.ShouldBe(15);
    }

    [Fact]
    public async Task ReserveQuotaAsync_AlreadyAboveWarning_DoesNotRepublish()
    {
        var userId = Guid.NewGuid();
        var service = CreateService();

        (await service.ReserveQuotaAsync(userId, 85)).Succeeded.ShouldBeTrue();
        _published.Count.ShouldBe(1);

        (await service.ReserveQuotaAsync(userId, 5)).Succeeded.ShouldBeTrue();

        _published.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ReserveQuotaAsync_CrossingCritical_PublishesCritical()
    {
        var userId = Guid.NewGuid();
        var service = CreateService();

        (await service.ReserveQuotaAsync(userId, 85)).Succeeded.ShouldBeTrue();
        (await service.ReserveQuotaAsync(userId, 11)).Succeeded.ShouldBeTrue();

        _published.Count.ShouldBe(2);
        _published[1].Level.ShouldBe(nameof(QuotaWarningLevel.Critical));
        _published[1].DailyUsagePercentage.ShouldBe(0.96m);
    }

    [Fact]
    public async Task SettleQuotaAsync_ActualCrossesWarning_PublishesWarningOnce()
    {
        // 生产路径永远是「先预留、再结算」，而实际用量超过预估是长补全的常态：
        // 越线发生在结算里时，预留那一次看不到（还在阈值下），结算不投影就一条也不发；
        // 更糟的是下一次预留的「加量前」快照已经在阈值之上，事件从此被永久抑制。
        var userId = Guid.NewGuid();
        var service = CreateService();

        var reservation = await service.ReserveQuotaAsync(userId, 70);
        reservation.Succeeded.ShouldBeTrue();
        _published.ShouldBeEmpty();

        (await service.SettleQuotaAsync(userId, reservation.Data!, actualTokens: 85)).Succeeded.ShouldBeTrue();

        var evt = _published.ShouldHaveSingleItem();
        evt.UserId.ShouldBe(userId);
        evt.Level.ShouldBe(nameof(QuotaWarningLevel.Warning));
        evt.DailyUsagePercentage.ShouldBe(0.85m);
        evt.RemainingDailyQuota.ShouldBe(15);

        (await service.ReserveQuotaAsync(userId, 5)).Succeeded.ShouldBeTrue();
        _published.Count.ShouldBe(1, "the later reservation stays inside the Warning band and must not republish");
    }

    [Fact]
    public async Task SettleQuotaAsync_ActualBelowEstimate_DoesNotPublish()
    {
        var userId = Guid.NewGuid();
        var service = CreateService();

        var reservation = await service.ReserveQuotaAsync(userId, 85);
        _published.Count.ShouldBe(1);

        (await service.SettleQuotaAsync(userId, reservation.Data!, actualTokens: 60)).Succeeded.ShouldBeTrue();

        _published.Count.ShouldBe(1, "settling downwards never raises the level");
    }

    [Fact]
    public async Task UpdateUsageAsync_CrossingWarning_PublishesWarning()
    {
        var userId = Guid.NewGuid();
        var service = CreateService();

        (await service.ReserveQuotaAsync(userId, 70)).Succeeded.ShouldBeTrue();
        (await service.UpdateUsageAsync(userId, 15)).Succeeded.ShouldBeTrue();

        var evt = _published.ShouldHaveSingleItem();
        evt.Level.ShouldBe(nameof(QuotaWarningLevel.Warning));
        evt.DailyUsagePercentage.ShouldBe(0.85m);
    }

    [Fact]
    public async Task ReserveQuotaAsync_Denied_DoesNotPublishThreshold()
    {
        var userId = Guid.NewGuid();
        var service = CreateService();

        (await service.ReserveQuotaAsync(userId, 70)).Succeeded.ShouldBeTrue();
        var denied = await service.ReserveQuotaAsync(userId, 50);

        denied.Succeeded.ShouldBeFalse();
        denied.Code.ShouldBe(429);
        _published.ShouldBeEmpty();
    }
}

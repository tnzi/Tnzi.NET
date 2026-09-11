using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tnzi.Domain.Repositories;
using Tnzi.Feature.Entities;
using Tnzi.Feature.Options;
using Tnzi.Feature.Services;
using Tnzi.MultiTenancy;
using Tnzi.Security.Claims;

namespace Tnzi.Feature.Tests;

/// <summary>
/// 用量记录离开请求路径：<see cref="FeatureUsageService.RecordUsageAsync"/> 只入队，
/// <see cref="FeatureUsageBackgroundService"/> 成批落库，<c>Feature:UsageTrackingEnabled</c> 是总开关。
/// </summary>
/// <remarks>
/// 缺陷形态：此前每次 <c>IsEnabledAsync</c> 都同步 <c>INSERT</c> 一行，无采样、无开关、无批量 ——
/// 一个 <c>[RequireFeature]</c> 端点每请求一次写库往返，表无限增长且关不掉。
/// </remarks>
public class FeatureUsagePipelineTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<IRepository<FeatureUsageRecord, long>> _repository = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();
    private readonly FeatureUsageSender _sender = new(NullLogger<FeatureUsageSender>.Instance);
    private readonly FeatureOptions _options = new();

    public FeatureUsagePipelineTests()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns(UserId);
        currentUser.Setup(u => u.TenantId).Returns(TenantId);
        _serviceProvider.Setup(sp => sp.GetService(typeof(ICurrentUser))).Returns(currentUser.Object);
        _serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(NullLoggerFactory.Instance);
    }

    private FeatureUsageService CreateService(ICurrentTenant? currentTenant = null)
    {
        var monitor = new Mock<IOptionsMonitor<FeatureOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(() => _options);
        return new FeatureUsageService(_serviceProvider.Object, _repository.Object, _sender, monitor.Object, currentTenant);
    }

    private static List<FeatureUsageRecord> Drain(FeatureUsageSender sender)
    {
        var items = new List<FeatureUsageRecord>();
        while (sender.Reader.TryRead(out var item))
        {
            items.Add(item);
        }
        return items;
    }

    /// <summary>★现形用例：记录一次用量不能再打一次库。</summary>
    [Fact]
    public async Task RecordUsageAsync_enqueues_and_never_touches_the_repository()
    {
        var service = CreateService();

        await service.RecordUsageAsync("Feature.Toggle", isEnabled: true, source: "FeatureChecker");

        _repository.Verify(r => r.InsertAsync(It.IsAny<FeatureUsageRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        var queued = Drain(_sender);
        queued.ShouldHaveSingleItem();
        queued[0].FeatureName.ShouldBe("Feature.Toggle");
        queued[0].IsEnabled.ShouldBeTrue();
        queued[0].Source.ShouldBe("FeatureChecker");
    }

    /// <summary>
    /// 谁、哪个租户、什么时候，都要在入队那一刻定格：后台作用域里这三样一个都没有，
    /// 而 SaveChanges 的审计填充只补空值，不会替我们找回请求上下文。
    /// </summary>
    [Fact]
    public async Task RecordUsageAsync_captures_user_tenant_and_time_at_enqueue()
    {
        var tenant = new Mock<ICurrentTenant>();
        tenant.Setup(t => t.Id).Returns(TenantId);
        var service = CreateService(tenant.Object);
        var before = DateTime.UtcNow;

        await service.RecordUsageAsync("Feature.Toggle", isEnabled: false);

        var record = Drain(_sender).Single();
        record.UserId.ShouldBe(UserId);
        record.TenantId.ShouldBe(TenantId);
        record.CreationTime.ShouldBeGreaterThanOrEqualTo(before);
        record.CreationTime.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);
    }

    [Fact]
    public async Task RecordUsageAsync_falls_back_to_the_users_tenant_when_no_tenant_context_is_available()
    {
        var service = CreateService(currentTenant: null);

        await service.RecordUsageAsync("Feature.Toggle", isEnabled: true);

        Drain(_sender).Single().TenantId.ShouldBe(TenantId);
    }

    /// <summary>★总开关：关掉之后连队列都不进，热端点上的表停止增长。</summary>
    [Fact]
    public async Task RecordUsageAsync_is_a_no_op_when_usage_tracking_is_off()
    {
        _options.UsageTrackingEnabled = false;
        var service = CreateService();

        await service.RecordUsageAsync("Feature.Toggle", isEnabled: true);

        Drain(_sender).ShouldBeEmpty();
        _repository.Verify(r => r.InsertAsync(It.IsAny<FeatureUsageRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void A_full_queue_drops_the_newest_record_and_counts_it_instead_of_blocking()
    {
        var sender = new FeatureUsageSender(NullLogger<FeatureUsageSender>.Instance);
        for (var i = 0; i < FeatureUsageSender.Capacity; i++)
        {
            sender.TrySend(new FeatureUsageRecord { FeatureName = "f" }).ShouldBeTrue();
        }

        sender.TrySend(new FeatureUsageRecord { FeatureName = "overflow" }).ShouldBeFalse();

        sender.DroppedCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_background_service_writes_queued_records_in_one_batch()
    {
        var inserted = new TaskCompletionSource<List<FeatureUsageRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _repository
            .Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<FeatureUsageRecord>>(), It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<FeatureUsageRecord> batch, CancellationToken _) => inserted.TrySetResult(batch.ToList()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddScoped(_ => _repository.Object);
        using var provider = services.BuildServiceProvider();

        var sender = new FeatureUsageSender(NullLogger<FeatureUsageSender>.Instance);
        sender.TrySend(new FeatureUsageRecord { FeatureName = "a" });
        sender.TrySend(new FeatureUsageRecord { FeatureName = "b" });
        sender.TrySend(new FeatureUsageRecord { FeatureName = "c" });

        var backgroundService = new FeatureUsageBackgroundService(sender, provider, NullLogger<FeatureUsageBackgroundService>.Instance);
        await backgroundService.StartAsync(CancellationToken.None);
        try
        {
            var batch = await inserted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            batch.Select(r => r.FeatureName).ShouldBe(new[] { "a", "b", "c" });
        }
        finally
        {
            await backgroundService.StopAsync(CancellationToken.None);
        }
    }
}

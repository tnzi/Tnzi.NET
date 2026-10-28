using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.MultiTenancy;

namespace Tnzi.Finance.Tests;

/// <summary>
/// 后台扫描逐租户隔离：一个租户的扫描抛异常，后面的租户照样被扫。
/// </summary>
/// <remarks>
/// 被保护的缺陷：租户循环没有任何隔离，唯一的 catch 包着整次扫描。列表里排在前面的租户只要有一次
/// 扫描抛出（数据库瞬断、某条模板的坏数据），它后面的每个租户这个周期都不再生成，下个周期重演。
/// </remarks>
public class RecurringBackgroundTenantIsolationTests
{
    private static readonly Guid FailingTenant = Guid.NewGuid();
    private static readonly Guid HealthyTenant = Guid.NewGuid();

    [Fact]
    public async Task ATenantWhoseSweepThrows_DoesNotStopTheTenantsAfterIt()
    {
        var swept = new List<Guid?>();
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddSingleton<IRecurringTenantSource>(new FixedTenants(FailingTenant, HealthyTenant));
        services.AddScoped<IRecurringGeneratorService>(sp => new TenantAwareGenerator(sp.GetRequiredService<ICurrentTenant>(), swept));
        using var provider = services.BuildServiceProvider();

        var background = new RecurringGenerationBackgroundService(
            provider,
            new StaticOptionsMonitor(new RecurringOptions()),
            NullLogger<RecurringGenerationBackgroundService>.Instance);

        await background.SweepAsync(CancellationToken.None);

        swept.ShouldBe([FailingTenant, HealthyTenant]);
    }

    [Fact]
    public async Task Cancellation_StillStopsTheSweep()
    {
        var swept = new List<Guid?>();
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddSingleton<IRecurringTenantSource>(new FixedTenants(FailingTenant, HealthyTenant));
        services.AddScoped<IRecurringGeneratorService>(sp => new TenantAwareGenerator(sp.GetRequiredService<ICurrentTenant>(), swept));
        using var provider = services.BuildServiceProvider();

        var background = new RecurringGenerationBackgroundService(
            provider,
            new StaticOptionsMonitor(new RecurringOptions()),
            NullLogger<RecurringGenerationBackgroundService>.Instance);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => background.SweepAsync(cts.Token));
    }

    private sealed class FixedTenants(params Guid[] ids) : IRecurringTenantSource
    {
        public Task<IReadOnlyList<Guid>> GetTenantIdsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Guid>>(ids);
    }

    /// <summary>记下被扫的租户；第一个租户抛异常，取消时抛取消。</summary>
    private sealed class TenantAwareGenerator(ICurrentTenant currentTenant, List<Guid?> swept) : IRecurringGeneratorService
    {
        public Task<Result<RecurringSweepResultDto>> RunDueAsync(DateTime? asOf = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            swept.Add(currentTenant.Id);
            if (currentTenant.Id == FailingTenant)
                throw new InvalidOperationException("The database connection dropped.");
            return Task.FromResult(Result<RecurringSweepResultDto>.Success(new RecurringSweepResultDto()));
        }

        public Task<Result<RecurringSweepResultDto>> RunOneAsync(Guid recurringDocumentId, DateTime? asOf = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StaticOptionsMonitor(RecurringOptions value) : IOptionsMonitor<RecurringOptions>
    {
        public RecurringOptions CurrentValue => value;

        public RecurringOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<RecurringOptions, string?> listener) => null;
    }
}

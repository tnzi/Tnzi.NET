using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Tnzi.EFCore.Outbox;
using Tnzi.EventBus;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// Outbox 落库前必须把环境租户写进事件：中继重发的是库里那份 JSON，落库时没有的字段之后也不会有。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：<c>EventBase.TenantId</c> 的「发布时捕获」此前只有本地总线兑现，
/// Outbox 直接 <c>JsonSerializer.Serialize</c> 落库，<c>TenantId</c> 恒为 null；
/// 另一实例消费时处理器跑在 null 租户作用域里（全局租户过滤器严格等值 → 空结果集 / 无主行）。
/// SQLite 足够：这里断言的是 JSON 内容，与 provider 无关。
/// </remarks>
public class OutboxEventStoreTenantTests : IDisposable
{
    public class TenantProbeEvent : EventBase, IIntegrationEvent
    {
        public string SourceService => "tests";
    }

    private readonly ServiceProvider _serviceProvider;
    private readonly OutboxTestDbContext _dbContext;
    private readonly SqliteConnection _connection;
    private readonly MockCurrentTenant _currentTenant = new();
    private readonly EfCoreEventStore _store;

    public OutboxEventStoreTenantTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<ICurrentTenant>(_currentTenant);

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        services.AddDbContext<OutboxTestDbContext>((_, options) => options.UseSqlite(_connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<OutboxTestDbContext>());

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<OutboxTestDbContext>();
        _dbContext.Database.EnsureCreated();

        _store = new EfCoreEventStore(_serviceProvider, NullLogger<EfCoreEventStore>.Instance);
    }

    [Fact]
    public async Task SaveEventAsync_PersistsTheAmbientTenantIdInEventData()
    {
        var tenantId = Guid.NewGuid();
        _currentTenant.SetTenant(tenantId, "acme");
        var @event = new TenantProbeEvent();

        await _store.SaveEventAsync(@event, typeof(TenantProbeEvent).FullName!);

        _dbContext.ChangeTracker.Clear();
        var stored = await _dbContext.Set<OutboxMessage>().AsNoTracking().SingleAsync();
        var persisted = JsonSerializer.Deserialize<TenantProbeEvent>(stored.EventData);

        Assert.Equal(tenantId, @event.TenantId);
        Assert.NotNull(persisted);
        Assert.Equal(tenantId, persisted!.TenantId);
    }

    [Fact]
    public async Task SaveEventAsync_KeepsAnExplicitTenantId()
    {
        _currentTenant.SetTenant(Guid.NewGuid(), "ambient");
        var explicitTenant = Guid.NewGuid();
        var @event = new TenantProbeEvent { TenantId = explicitTenant };

        await _store.SaveEventAsync(@event, typeof(TenantProbeEvent).FullName!);

        _dbContext.ChangeTracker.Clear();
        var stored = await _dbContext.Set<OutboxMessage>().AsNoTracking().SingleAsync();
        var persisted = JsonSerializer.Deserialize<TenantProbeEvent>(stored.EventData);

        Assert.Equal(explicitTenant, persisted!.TenantId);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}

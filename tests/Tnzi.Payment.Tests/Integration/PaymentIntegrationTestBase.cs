using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Tnzi.Data;
using Tnzi.Domain.Entities;
using Tnzi.Domain.Repositories;
using Tnzi.EFCore;
using Tnzi.EventBus;
using Tnzi.Mapster;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Events;
using Tnzi.Payment.Events.Handlers;
using Tnzi.Payment.Options;
using Tnzi.Payment.Providers;
using Tnzi.Payment.Services;
using Tnzi.TestBase;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// Payment 集成测试基类：真实 SQLite + 仓储 + EventBus + 订阅状态机处理器 + NullProvider，
/// 用于验证只能在真实 DbContext 下生效的原子 CAS 与端到端 off-session 计费流程。
/// </summary>
public abstract class PaymentIntegrationTestBase : IntegratedTestBase<PaymentTestDbContext>
{
    protected PaymentIntegrationTestBase()
    {
        var config = new TypeAdapterConfig();
        MapperExtensions.SetMapper(new Mapper(config));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        // 选项：开启测试渠道，关闭退款审批（便于直接走退款执行）
        services.AddOptions();
        services.Configure<PaymentOptions>(o =>
        {
            o.AllowTestProvider = true;
            o.EnableRefundApproval = false;
            o.DefaultCurrency = "USD";
        });
        services.Configure<PromotionOptions>(_ => { });

        // 仓储
        AddRepo<PaymentEntity>(services);
        AddRepo<Refund>(services);
        AddRepo<Subscription>(services);
        AddRepo<SubscriptionPlan>(services);
        AddRepo<SubscriptionChange>(services);
        AddRepo<Promotion>(services);
        AddRepo<CouponUsage>(services);
        AddRepo<RedemptionCode>(services);
        AddRepo<Invoice>(services);
        AddRepo<InvoiceLineItem>(services);

        // UnitOfWork（让 ExecuteInUnitOfWorkAsync 走真实延迟保存路径）
        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(PaymentTestDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        // 支付渠道（仅 NullProvider）
        services.AddScoped<IPaymentProvider, NullProvider>();
        services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();

        // EventBus + 订阅状态机回流处理器
        services.AddSingleton<IEventBus>(sp =>
            new LocalEventBus(sp, sp.GetRequiredService<ILogger<LocalEventBus>>()));
        services.AddScoped<IEventHandler<PaymentCompletedEvent>, SubscriptionPaymentCompletedHandler>();
        services.AddScoped<IEventHandler<PaymentFailedEvent>, SubscriptionPaymentFailedHandler>();
        services.AddScoped<IEventHandler<PaymentExpiredEvent>, SubscriptionPaymentExpiredHandler>();

        // 业务服务
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IRefundService, RefundService>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<ICouponService, CouponService>();
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<PaymentTestDbContext, TEntity, Guid>(sp.GetRequiredService<PaymentTestDbContext>(), serviceProvider: sp));
    }

    /// <summary>
    /// 在独立 scope 中持久化种子实体（模拟每请求独立 DbContext，避免跨调用身份映射冲突）。
    /// 持久化后实体对象的 Id 已回填。
    /// </summary>
    protected async Task SeedAsync(params object[] entities)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<PaymentTestDbContext>();
        ctx.AddRange(entities);
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// 在独立 scope 中执行一次服务操作（每次操作=一个新的 DbContext，贴近真实请求生命周期）
    /// </summary>
    protected async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        using var scope = ServiceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TService>();
        return await action(svc);
    }

    /// <summary>
    /// 在独立 scope 中读取实体最新状态（绕过身份映射缓存，读取事件处理器子 scope 写入的数据）
    /// </summary>
    protected async Task<TEntity?> ReloadAsync<TEntity>(Guid id) where TEntity : class, IEntity<Guid>
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<TEntity, Guid>>();
        return await repo.FirstOrDefaultAsync(e => e.Id == id);
    }
}

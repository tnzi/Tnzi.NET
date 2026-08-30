using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Tnzi.Domain.Entities;
using Tnzi.EventBus;
using Tnzi.Mapster;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 续费子模块集成测试基类：真实 SQLite + 仓储 + EventBus + 订阅状态机处理器 + NullProvider。
/// </summary>
/// <remarks>
/// 与拆分前父测试项目的 <c>PaymentIntegrationTestBase</c> 逐条等价，只是多注册了本模块
/// 自己那几样（订阅服务、四个扩展点的实现、三个回流处理器）—— 它们现在由
/// <c>PaymentSubscriptionsModule</c> 注册，而不再由父模块注册。
///
/// ★ <c>IStoredPaymentMethodBindingSink</c> 必须真的注册进来：绑卡 / 解绑对订阅行的同步与清理
/// 现在完全经由它发生，不注册就等于在测「没装续费包」的场景，而那是父测试项目的活。
/// </remarks>
public abstract class SubscriptionsIntegrationTestBase : IntegratedTestBase<SubscriptionsTestDbContext>
{
    protected SubscriptionsIntegrationTestBase()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
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
            // 测试渠道即默认渠道，否则建单会去找未注册的 Stripe
            o.DefaultChannelCode = "Null";
            // 线下渠道与其它真实渠道一样需要显式启用
            o.Channels["Offline"] = new ChannelOptions { Enabled = true, Currency = "USD" };
        });
        services.Configure<PromotionOptions>(_ => { });
        services.Configure<TaxOptions>(_ => { });
        // 拆分后订阅配置是独立一节（Payment:Subscription），单独注入。
        services.Configure<SubscriptionOptions>(_ => { });

        // 仓储
        AddRepo<PaymentEntity>(services);
        AddRepo<Refund>(services);
        AddRepo<Subscription>(services);
        AddRepo<SubscriptionPlan>(services);
        AddRepo<SubscriptionChange>(services);
        AddRepo<Promotion>(services);
        AddRepo<CouponUsage>(services);
        AddRepo<RedemptionCode>(services);
        AddRepo<UserCoupon>(services);
        AddRepo<StoredPaymentMethod>(services);

        // UnitOfWork（让 ExecuteInUnitOfWorkAsync 走真实延迟保存路径）
        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns([typeof(SubscriptionsTestDbContext)]);
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        // 支付渠道：测试渠道 + 线下渠道
        services.AddScoped<IPaymentProvider, NullProvider>();
        services.AddScoped<IPaymentProvider, OfflineProvider>();
        services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();

        // EventBus + 订阅状态机回流处理器（现由本模块注册）
        services.AddSingleton<IEventBus>(sp =>
            new LocalEventBus(sp, sp.GetRequiredService<ILogger<LocalEventBus>>()));
        services.AddScoped<IEventHandler<PaymentCompletedEvent>, SubscriptionPaymentCompletedHandler>();
        services.AddScoped<IEventHandler<PaymentFailedEvent>, SubscriptionPaymentFailedHandler>();
        services.AddScoped<IEventHandler<PaymentExpiredEvent>, SubscriptionPaymentExpiredHandler>();

        // 父模块的业务服务
        services.AddScoped<IPaymentTaxCalculator, DefaultPaymentTaxCalculator>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IRefundService, RefundService>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<ICouponService, CouponService>();

        // 本模块的服务与四个扩展点实现
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IStoredPaymentMethodBindingSink, SubscriptionBindingSink>();
        services.AddScoped<ISubscriptionHistoryProbe, SubscriptionHistoryProbe>();
        services.AddScoped<IPaymentStatisticsContributor, SubscriptionStatisticsContributor>();
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<SubscriptionsTestDbContext, TEntity, Guid>(sp.GetRequiredService<SubscriptionsTestDbContext>(), serviceProvider: sp));
    }

    /// <summary>
    /// 在独立 scope 中持久化种子实体（模拟每请求独立 DbContext，避免跨调用身份映射冲突）。
    /// 持久化后实体对象的 Id 已回填。
    /// </summary>
    protected async Task SeedAsync(params object[] entities)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
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

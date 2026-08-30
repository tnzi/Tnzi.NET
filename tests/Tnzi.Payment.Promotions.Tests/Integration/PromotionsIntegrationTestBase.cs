using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Tnzi.Data;
using Tnzi.Domain.Entities;
using Tnzi.EFCore;
using Tnzi.EventBus;
using Tnzi.Mapster;
using Tnzi.Payment.Options;
using Tnzi.TestBase;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Promotions.Tests.Integration;

/// <summary>
/// 折扣子模块集成测试基类：真实 SQLite + 仓储 + EventBus + NullProvider +
/// 父模块的支付服务 + 本模块的促销 / 优惠券服务。
/// </summary>
/// <remarks>
/// 与拆分前父测试项目的 <c>PaymentIntegrationTestBase</c> 逐条等价，只是促销与优惠券那几样
/// 现在由 <c>PaymentPromotionsModule</c> 注册，而不再由父模块注册。
///
/// ★ <c>ICouponService</c> 必须真的注册进来：建单时的试算与核销、渠道失败与超时的还券
/// 全部经由它发生，不注册就等于在测「没装折扣包」的场景，而那是父测试项目的活。
/// 三条注册（<c>ICouponService</c> / <c>ICouponWalletService</c> / <c>ICouponIssuanceService</c>）
/// 指向同一个 Scoped 实例，与模块里的写法一致 —— 否则同一次请求里会出现三份各自持有
/// DbContext 变更跟踪的副本。
/// </remarks>
public abstract class PromotionsIntegrationTestBase : IntegratedTestBase<PromotionsTestDbContext>
{
    protected PromotionsIntegrationTestBase()
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
        // 拆分后促销配置由本模块绑（Payment:Promotion 节路径不变），单独注入。
        services.Configure<PromotionOptions>(_ => { });
        services.Configure<TaxOptions>(_ => { });

        // 仓储
        AddRepo<PaymentEntity>(services);
        AddRepo<Refund>(services);
        AddRepo<StoredPaymentMethod>(services);
        AddRepo<Promotion>(services);
        AddRepo<CouponUsage>(services);
        AddRepo<RedemptionCode>(services);
        AddRepo<UserCoupon>(services);

        // UnitOfWork（让 ExecuteInUnitOfWorkAsync 走真实延迟保存路径）
        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns([typeof(PromotionsTestDbContext)]);
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        // 支付渠道：测试渠道 + 线下渠道
        services.AddScoped<IPaymentProvider, NullProvider>();
        services.AddScoped<IPaymentProvider, OfflineProvider>();
        services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();

        services.AddSingleton<IEventBus>(sp =>
            new LocalEventBus(sp, sp.GetRequiredService<ILogger<LocalEventBus>>()));

        // 父模块的业务服务
        services.AddScoped<IPaymentTaxCalculator, DefaultPaymentTaxCalculator>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IRefundService, RefundService>();

        // 本模块的服务
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<CouponService>();
        services.AddScoped<ICouponService>(sp => sp.GetRequiredService<CouponService>());
        services.AddScoped<ICouponWalletService>(sp => sp.GetRequiredService<CouponService>());
        services.AddScoped<ICouponIssuanceService>(sp => sp.GetRequiredService<CouponService>());
        services.AddScoped<IPromotionAnalyticsProvider, PromotionAnalyticsProvider>();
        services.AddScoped<IPaymentStatisticsService, PaymentStatisticsService>();
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<PromotionsTestDbContext, TEntity, Guid>(sp.GetRequiredService<PromotionsTestDbContext>(), serviceProvider: sp));
    }

    /// <summary>
    /// 在独立 scope 中持久化种子实体（模拟每请求独立 DbContext，避免跨调用身份映射冲突）。
    /// 持久化后实体对象的 Id 已回填。
    /// </summary>
    protected async Task SeedAsync(params object[] entities)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<PromotionsTestDbContext>();
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

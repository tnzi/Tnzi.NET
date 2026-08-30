using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tnzi.Data;
using Tnzi.Domain.Entities;
using Tnzi.Domain.Repositories;
using Tnzi.EFCore;
using Tnzi.EventBus;
using Tnzi.Mapster;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Options;
using Tnzi.Payment.Providers;
using Tnzi.Payment.Services;
using Tnzi.TestBase;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// Payment 集成测试基类：真实 SQLite + 仓储 + EventBus + NullProvider，
/// 用于验证只能在真实 DbContext 下生效的原子 CAS 与端到端支付 / 退款 / 优惠券流程。
/// </summary>
/// <remarks>
/// 刻意<b>不</b>注册订阅侧的任何东西（三个仓储、<c>ISubscriptionService</c>、三个回流处理器、
/// 四个扩展点实现），也<b>不</b>注册促销侧的任何东西（四个仓储、<c>IPromotionService</c>、
/// <c>ICouponService</c>、<c>IPromotionAnalyticsProvider</c>）—— 本测试项目不引用
/// <c>Tnzi.Payment.Subscriptions</c> 也不引用 <c>Tnzi.Payment.Promotions</c>，跑的是
/// 「两个包都没装」的真实现场。各自的集成测试在 <c>Tnzi.Payment.Subscriptions.Tests</c>
/// 与 <c>Tnzi.Payment.Promotions.Tests</c>。
/// </remarks>
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
            // 测试渠道即默认渠道，否则建单会去找未注册的 Stripe
            o.DefaultChannelCode = "Null";
            // 线下渠道与其它真实渠道一样需要显式启用
            o.Channels["Offline"] = new ChannelOptions { Enabled = true, Currency = "USD" };
        });
        // PromotionOptions 随折扣域去了 Tnzi.Payment.Promotions，本项目里没有这个类型。
        services.Configure<TaxOptions>(_ => { });

        // 仓储
        AddRepo<PaymentEntity>(services);
        AddRepo<Refund>(services);
        AddRepo<StoredPaymentMethod>(services);

        // UnitOfWork（让 ExecuteInUnitOfWorkAsync 走真实延迟保存路径）
        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(PaymentTestDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        // 支付渠道：测试渠道 + 线下渠道（线下确认收款链路需要它）
        services.AddScoped<IPaymentProvider, NullProvider>();
        services.AddScoped<IPaymentProvider, OfflineProvider>();
        services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();

        // EventBus（订阅状态机的三个回流处理器随续费域去了 Tnzi.Payment.Subscriptions，
        // 由它自己注册；这里不注册正是「没装续费包」的现场）
        services.AddSingleton<IEventBus>(sp =>
            new LocalEventBus(sp, sp.GetRequiredService<ILogger<LocalEventBus>>()));

        // 业务服务
        services.AddScoped<IPaymentTaxCalculator, DefaultPaymentTaxCalculator>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IRefundService, RefundService>();
        // IPromotionService / ICouponService 随折扣域去了 Tnzi.Payment.Promotions，由它自己注册。
        // 这里不注册 ICouponService 正是「没装折扣包」的现场：带券码的建单会被拒。
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

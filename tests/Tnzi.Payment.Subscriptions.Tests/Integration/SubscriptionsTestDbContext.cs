using Tnzi.Payment.Entities.Configs;
using Tnzi.Security.Claims;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 续费子模块集成测试用 DbContext（SQLite 内存库）：父模块的支付侧表 +
/// 本模块的三张订阅表。
/// </summary>
/// <remarks>
/// 两侧的实体配置放在一个模型里是刻意的 —— 订阅计费真正跑起来要同时用到两边：
/// 续费扣款会建一笔 <c>Payment</c>，绑卡链路会写 <c>StoredPaymentMethod</c>。
/// 反过来，父测试项目的 DbContext <b>不建</b>这三张订阅表，跑的是「没装续费包」的真实现场。
/// </remarks>
public class SubscriptionsTestDbContext : TnziDbContext<SubscriptionsTestDbContext>
{
    /// <summary>
    /// 租户与多租户开关都从容器里来：默认的测试基类给的是「关闭」，
    /// 多租户用例（<c>MultiTenancyEnabled => true</c>）把开关拨开并换上真的 <c>CurrentTenant</c>。
    /// </summary>
    public SubscriptionsTestDbContext(
        DbContextOptions<SubscriptionsTestDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(options, currentUser, currentTenant, multiTenancyOptions: multiTenancyOptions)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new RefundConfiguration());
        modelBuilder.ApplyConfiguration(new PromotionConfiguration());
        modelBuilder.ApplyConfiguration(new CouponUsageConfiguration());
        modelBuilder.ApplyConfiguration(new RedemptionCodeConfiguration());
        modelBuilder.ApplyConfiguration(new UserCouponConfiguration());
        modelBuilder.ApplyConfiguration(new StoredPaymentMethodConfiguration());

        modelBuilder.ApplyConfiguration(new SubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionPlanConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionChangeConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// 多租户开启形态的同一份模型。
/// </summary>
/// <remarks>
/// 必须是<b>另一个 CLR 类型</b>：EF 按 DbContext 类型缓存模型，多租户关闭时 <c>TenantId</c> 列被 Ignore，
/// 同一进程里哪个测试类先建了模型，后来者就沿用它 —— 于是多租户用例在全量跑时读到的
/// <c>TenantId</c> 恒为 null、过滤器不存在，而单跑时一切正常。父类型的 <c>DbContextOptions</c>
/// 可以直接复用（EF 只要求 options 的上下文类型能赋值给实例类型）。
/// </remarks>
public class MultiTenantSubscriptionsTestDbContext : SubscriptionsTestDbContext
{
    public MultiTenantSubscriptionsTestDbContext(
        DbContextOptions<SubscriptionsTestDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        IOptions<MultiTenancyOptions> multiTenancyOptions)
        : base(options, currentUser, currentTenant, multiTenancyOptions)
    {
    }
}

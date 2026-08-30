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
    public SubscriptionsTestDbContext(DbContextOptions<SubscriptionsTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
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

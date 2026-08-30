using Microsoft.EntityFrameworkCore;
using Tnzi.EFCore;
using Tnzi.Payment.Entities.Configs;
using Tnzi.Security.Claims;
using Tnzi.TestBase;

namespace Tnzi.Payment.Promotions.Tests.Integration;

/// <summary>
/// 折扣子模块集成测试用 DbContext（SQLite 内存库）：父模块的支付侧表 + 本模块的四张促销表。
/// </summary>
/// <remarks>
/// 两侧的实体配置放在一个模型里是刻意的 —— 优惠券真正跑起来要同时用到两边：
/// 建单会写一笔 <c>Payment</c> 并在渠道失败或超时未付时把券还回来。
/// 反过来，父测试项目的 DbContext <b>不建</b>这四张促销表，跑的是「没装折扣包」的真实现场。
/// </remarks>
public class PromotionsTestDbContext : TnziDbContext<PromotionsTestDbContext>
{
    public PromotionsTestDbContext(DbContextOptions<PromotionsTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new RefundConfiguration());
        modelBuilder.ApplyConfiguration(new StoredPaymentMethodConfiguration());

        modelBuilder.ApplyConfiguration(new PromotionConfiguration());
        modelBuilder.ApplyConfiguration(new CouponUsageConfiguration());
        modelBuilder.ApplyConfiguration(new RedemptionCodeConfiguration());
        modelBuilder.ApplyConfiguration(new UserCouponConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

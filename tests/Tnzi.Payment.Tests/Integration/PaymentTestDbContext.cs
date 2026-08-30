using Microsoft.EntityFrameworkCore;
using Tnzi.EFCore;
using Tnzi.Payment.Entities.Configs;
using Tnzi.Security.Claims;
using Tnzi.TestBase;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// Payment 模块集成测试用 DbContext（SQLite 内存库）
/// </summary>
/// <remarks>
/// 这里**没有**发票的两张表、订阅的三张表、促销的四张表，而且都是刻意的：本测试项目不引用
/// 任何可选子模块（<c>Tnzi.Payment.Billing</c> / <c>Tnzi.Payment.Subscriptions</c> /
/// <c>Tnzi.Payment.Promotions</c>），跑的就是「只加载支付核心」的真实现场。
/// 支付、退款、绑卡、回调、对账的全部集成用例在这套库上照常通过 ——
/// 那正是「缺席 = 少一项能力，不是错行为」的证据。三个子模块各自的集成测试在
/// <c>Tnzi.Payment.Billing.Tests</c> / <c>Tnzi.Payment.Subscriptions.Tests</c> /
/// <c>Tnzi.Payment.Promotions.Tests</c>，那边的 DbContext 才建对应的表。
/// </remarks>
public class PaymentTestDbContext : TnziDbContext<PaymentTestDbContext>
{
    public PaymentTestDbContext(DbContextOptions<PaymentTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new RefundConfiguration());
        modelBuilder.ApplyConfiguration(new StoredPaymentMethodConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

using Microsoft.EntityFrameworkCore;
using Tnzi.EFCore;
using Tnzi.Payment.Entities.Configs;
using Tnzi.Security.Claims;
using Tnzi.TestBase;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// Payment 模块集成测试用 DbContext（SQLite 内存库）
/// </summary>
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
        modelBuilder.ApplyConfiguration(new SubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionPlanConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionChangeConfiguration());
        modelBuilder.ApplyConfiguration(new PromotionConfiguration());
        modelBuilder.ApplyConfiguration(new CouponUsageConfiguration());
        modelBuilder.ApplyConfiguration(new RedemptionCodeConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceLineItemConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

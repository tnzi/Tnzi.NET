using Tnzi.Payment.Entities.Configs;
using Tnzi.Security.Claims;

namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// 开票子模块集成测试用 DbContext（SQLite 内存库）：父模块的 <c>Payment</c> 表 +
/// 本模块的两张发票表。
/// </summary>
/// <remarks>
/// 两侧的实体配置放在一个模型里是刻意的 —— 「支付 ↔ 发票」的一对一关系现在<b>只由依赖端</b>
/// （本模块的 <c>InvoiceConfiguration</c>）声明，父模块那侧已经没有任何一句话提到发票。
/// 这个 DbContext 能不能建起来，就是「从依赖端单侧声明足够」的直接证据；
/// 建出来的外键与索引形状则由 <c>InvoiceRelationalShapeTests</c> 逐项比对。
/// </remarks>
public class BillingTestDbContext : TnziDbContext<BillingTestDbContext>
{
    public BillingTestDbContext(DbContextOptions<BillingTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceLineItemConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

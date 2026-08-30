using Microsoft.EntityFrameworkCore;
using Tnzi.Finance.Entities.Configs;
using Tnzi.Finance.Offers.Entities.Configs;
using Tnzi.Security.Claims;

namespace Tnzi.Finance.Offers.Tests.Integration;

/// <summary>
/// 要约模块集成测试用 DbContext（SQLite 内存库）。
/// </summary>
/// <remarks>
/// 只装本模块四张表 + 转换与断言真的会碰到的那部分核心表（发票 / 账单 / 主数据 / 总账），
/// 不复制核心测试夹具的全量清单：这个 DbContext 存在的意义是证明本模块在**只加载
/// Finance + Finance.Offers** 的宿主上成立。
/// </remarks>
public class OfferTestDbContext : TnziDbContext<OfferTestDbContext>
{
    public OfferTestDbContext(DbContextOptions<OfferTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 会计内核（转换出来的发票 / 账单要真的过得了草稿创建，报表要真的算得出零）
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new AccountPeriodBalanceConfiguration());
        modelBuilder.ApplyConfiguration(new JournalEntryConfiguration());
        modelBuilder.ApplyConfiguration(new JournalLineConfiguration());
        modelBuilder.ApplyConfiguration(new FiscalYearConfiguration());
        modelBuilder.ApplyConfiguration(new LedgerLockConfiguration());
        modelBuilder.ApplyConfiguration(new ExchangeRateConfiguration());
        // 冲销守卫的判定输入（发票草稿创建 → 过账栈解析 ReversalGuard 时必然被建模）
        modelBuilder.ApplyConfiguration(new ReconciliationConfiguration());
        modelBuilder.ApplyConfiguration(new ReconciliationLineConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentSequenceConfiguration());

        // 主数据
        modelBuilder.ApplyConfiguration(new CustomerConfiguration());
        modelBuilder.ApplyConfiguration(new VendorConfiguration());
        modelBuilder.ApplyConfiguration(new ItemConfiguration());
        modelBuilder.ApplyConfiguration(new TaxAgencyConfiguration());
        modelBuilder.ApplyConfiguration(new TaxRateConfiguration());
        modelBuilder.ApplyConfiguration(new TaxCodeConfiguration());
        modelBuilder.ApplyConfiguration(new TaxCodeComponentConfiguration());

        // 转换目标
        modelBuilder.ApplyConfiguration(new InvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceLineConfiguration());
        modelBuilder.ApplyConfiguration(new BillConfiguration());
        modelBuilder.ApplyConfiguration(new BillLineConfiguration());
        // 供应商删除守卫查费用单（核心自带的检查，与本模块无关但必须照旧生效）
        modelBuilder.ApplyConfiguration(new ExpenseConfiguration());
        modelBuilder.ApplyConfiguration(new ExpenseLineConfiguration());
        modelBuilder.ApplyConfiguration(new CreditMemoConfiguration());
        modelBuilder.ApplyConfiguration(new CreditMemoLineConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentEntryConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentApplicationConfiguration());

        // 本模块
        modelBuilder.ApplyConfiguration(new EstimateConfiguration());
        modelBuilder.ApplyConfiguration(new EstimateLineConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseOrderConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseOrderLineConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

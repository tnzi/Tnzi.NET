using Microsoft.EntityFrameworkCore;
using Tnzi.Finance.Entities.Configs;
using Tnzi.Security.Claims;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// Finance 模块集成测试用 DbContext（SQLite 内存库）
/// </summary>
public class FinanceTestDbContext : TnziDbContext<FinanceTestDbContext>
{
    public FinanceTestDbContext(DbContextOptions<FinanceTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new JournalEntryConfiguration());
        modelBuilder.ApplyConfiguration(new JournalLineConfiguration());
        modelBuilder.ApplyConfiguration(new FiscalYearConfiguration());
        modelBuilder.ApplyConfiguration(new ExchangeRateConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentSequenceConfiguration());
        modelBuilder.ApplyConfiguration(new CustomerConfiguration());
        modelBuilder.ApplyConfiguration(new VendorConfiguration());
        modelBuilder.ApplyConfiguration(new ItemConfiguration());
        modelBuilder.ApplyConfiguration(new TaxAgencyConfiguration());
        modelBuilder.ApplyConfiguration(new TaxRateConfiguration());
        modelBuilder.ApplyConfiguration(new TaxCodeConfiguration());
        modelBuilder.ApplyConfiguration(new TaxCodeComponentConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceLineConfiguration());
        modelBuilder.ApplyConfiguration(new BillConfiguration());
        modelBuilder.ApplyConfiguration(new BillLineConfiguration());
        modelBuilder.ApplyConfiguration(new ExpenseConfiguration());
        modelBuilder.ApplyConfiguration(new ExpenseLineConfiguration());
        modelBuilder.ApplyConfiguration(new CreditMemoConfiguration());
        modelBuilder.ApplyConfiguration(new CreditMemoLineConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentEntryConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentApplicationConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

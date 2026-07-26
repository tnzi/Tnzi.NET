using Microsoft.EntityFrameworkCore;
using Tnzi.EFCore;
using Tnzi.Finance.Entities.Configs;
using Tnzi.Finance.Payroll.Entities.Configs;
using Tnzi.Security.Claims;

namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// Payroll 子模块集成测试用 DbContext（SQLite 内存库）。
/// 含 Payroll 全部实体 + 影子供应商衔接的 Vendor + P4c 过账所需的 Finance 总账栈
/// （Account/JournalEntry/JournalLine/FiscalYear/ExchangeRate + 核心 DocumentSequence）。
/// </summary>
public class PayrollTestDbContext : TnziDbContext<PayrollTestDbContext>
{
    public PayrollTestDbContext(DbContextOptions<PayrollTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Payroll
        modelBuilder.ApplyConfiguration(new EmployeeConfiguration());
        modelBuilder.ApplyConfiguration(new SalaryComponentConfiguration());
        modelBuilder.ApplyConfiguration(new BracketTableConfiguration());
        modelBuilder.ApplyConfiguration(new BracketRowConfiguration());
        modelBuilder.ApplyConfiguration(new SalaryStructureConfiguration());
        modelBuilder.ApplyConfiguration(new SalaryStructureLineConfiguration());
        modelBuilder.ApplyConfiguration(new SalaryAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new PayRunConfiguration());
        modelBuilder.ApplyConfiguration(new PayslipConfiguration());
        modelBuilder.ApplyConfiguration(new PayslipLineConfiguration());

        // Finance 总账栈（过账/付款/作废经 ILedgerPostingService）
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new JournalEntryConfiguration());
        modelBuilder.ApplyConfiguration(new JournalLineConfiguration());
        modelBuilder.ApplyConfiguration(new FiscalYearConfiguration());
        modelBuilder.ApplyConfiguration(new LedgerLockConfiguration());
        modelBuilder.ApplyConfiguration(new ExchangeRateConfiguration());
        modelBuilder.ApplyConfiguration(new VendorConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentSequenceConfiguration());
        modelBuilder.ApplyConfiguration(new AccountPeriodBalanceConfiguration());

        // 冲销守卫的判定输入：PayRun 作废经 ILedgerPostingService.ReverseAsync 走冲销漏斗，
        // 守卫会查这三张表（本套件不勾对账/不导流水，故恒不命中，但模型里必须有）
        modelBuilder.ApplyConfiguration(new ReconciliationConfiguration());
        modelBuilder.ApplyConfiguration(new ReconciliationLineConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

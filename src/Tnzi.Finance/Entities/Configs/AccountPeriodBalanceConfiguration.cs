namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 科目期间余额汇总配置（派生事实表，无审计无软删）
/// </summary>
public class AccountPeriodBalanceConfiguration : EntityTypeConfigurationBase<AccountPeriodBalance, Guid>
{
    public override void Configure(EntityTypeBuilder<AccountPeriodBalance> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Currency).HasMaxLength(8).IsRequired();
        builder.Property(e => e.Debit).HasMoneyPrecision();
        builder.Property(e => e.Credit).HasMoneyPrecision();
        builder.Property(e => e.TxnDebit).HasMoneyPrecision();
        builder.Property(e => e.TxnCredit).HasMoneyPrecision();

        // 无导航属性的 FK → Account（Restrict）：汇总桶引用科目但不遍历科目图
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // 桶唯一键 = (TenantId, AccountId, Period, Currency)：维护端 ExecuteUpdate 命中/0 行插入的
        // 并发首插由此索引兜底（照 Reconciliation 的条件多租户索引模式，但无过滤条件）
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.AccountId, e.Period, e.Currency }).IsUnique();
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => new { e.AccountId, e.Period, e.Currency }).IsUnique();
        }
    }
}

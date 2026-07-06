namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 会计分录行配置（总账事实表）
/// </summary>
public class JournalLineConfiguration : EntityTypeConfigurationBase<JournalLine, Guid>
{
    public override void Configure(EntityTypeBuilder<JournalLine> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(l => l.Currency).HasMaxLength(8).IsRequired();
        builder.Property(l => l.Memo).HasMaxLength(500);
        builder.Property(l => l.PartyType).HasMaxLength(32);
        builder.Property(l => l.PartyId).HasMaxLength(64);
        builder.Property(l => l.Debit).HasMoneyPrecision();
        builder.Property(l => l.Credit).HasMoneyPrecision();
        builder.Property(l => l.TxnDebit).HasMoneyPrecision();
        builder.Property(l => l.TxnCredit).HasMoneyPrecision();
        builder.Property(l => l.ExchangeRate).HasExchangeRatePrecision();
        // Dimensions 存储 JSON 对象，不指定类型以保持数据库兼容性

        builder.HasOne(l => l.Account)
            .WithMany()
            .HasForeignKey(l => l.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(l => l.TenantId);
        }

        builder.HasIndex(l => l.JournalEntryId);
        // 报表聚合主索引：按科目 + 已过账 + 日期范围
        builder.HasIndex(l => new { l.AccountId, l.IsPosted, l.PostingDate });
        // 全账本日期范围扫描（试算平衡/BS/P&L）
        builder.HasIndex(l => new { l.IsPosted, l.PostingDate });
        // 往来方子账查询（应收/应付按客户/供应商）
        builder.HasIndex(l => new { l.PartyType, l.PartyId });
    }
}

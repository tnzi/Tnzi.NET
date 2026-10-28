namespace Tnzi.Finance.Banking.Entities.Configs;

/// <summary>
/// 银行流水行配置
/// </summary>
public class BankTransactionConfiguration : EntityTypeConfigurationBase<BankTransaction, Guid>
{
    public override void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Currency).HasMaxLength(8).IsRequired();
        // 列宽常量与 BankTransactionFieldLimits 共用：摄取器按它们归一化，两处不可能漂移
        // （各写一份数字的话，漂移的症状是导入到一半 500 而不是编译失败）。
        builder.Property(e => e.Description).HasMaxLength(BankTransactionFieldLimits.DescriptionMaxLength);
        builder.Property(e => e.Payee).HasMaxLength(BankTransactionFieldLimits.PayeeMaxLength);
        builder.Property(e => e.Reference).HasMaxLength(BankTransactionFieldLimits.ReferenceMaxLength);
        builder.Property(e => e.ExternalId).HasMaxLength(BankTransactionFieldLimits.ExternalIdMaxLength).IsRequired();
        builder.Property(e => e.MatchRule).HasMaxLength(32);
        builder.Property(e => e.CreatedDocType).HasMaxLength(32);
        builder.Property(e => e.Amount).HasMoneyPrecision();
        builder.Property(e => e.MatchConfidence).HasExchangeRatePrecision();
        builder.Property(e => e.BalanceAfter).HasMoneyPrecision();

        // 逐行去重：同一科目下同一 ExternalId 唯一（软删行不占位）
        var notDeleted = IndexFilterFactory.GetIsDeletedFalse();
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.AccountId, e.ExternalId }).IsUnique().HasFilter(notDeleted);
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => new { e.AccountId, e.ExternalId }).IsUnique().HasFilter(notDeleted);
        }

        builder.HasIndex(e => new { e.AccountId, e.Status });
        builder.HasIndex(e => e.ImportBatchId);
        // 冲销守卫（任何单据作废都会问「这一行被银行流水匹配着吗」，不带科目）与匹配候选的反连接
        // 都按这一列查；表只增不减（每一行导入的流水），没有索引两者都随历年导入量全表扫。
        // 只有已匹配的行有值，过滤掉 NULL 让索引只随已匹配行增长。
        builder.HasIndex(e => e.MatchedJournalLineId)
            .HasFilter(IndexFilterFactory.GetColumnNotNull(nameof(BankTransaction.MatchedJournalLineId)));
    }
}

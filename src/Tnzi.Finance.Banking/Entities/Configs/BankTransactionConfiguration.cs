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
        builder.Property(e => e.Description).HasMaxLength(512);
        builder.Property(e => e.Payee).HasMaxLength(256);
        builder.Property(e => e.Reference).HasMaxLength(128);
        builder.Property(e => e.ExternalId).HasMaxLength(256).IsRequired();
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
    }
}

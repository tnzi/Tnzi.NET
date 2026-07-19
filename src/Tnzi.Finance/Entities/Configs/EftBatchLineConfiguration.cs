namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// EFT 批次行配置
/// </summary>
public class EftBatchLineConfiguration : EntityTypeConfigurationBase<EftBatchLine, Guid>
{
    public override void Configure(EntityTypeBuilder<EftBatchLine> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.PayeeName).HasMaxLength(256);
        builder.Property(e => e.Amount).HasMoneyPrecision();

        // 一笔付款至多在一个存活批次内（作废硬删行后可重入，无软删过滤）
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.PaymentEntryId }).IsUnique();
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => e.PaymentEntryId).IsUnique();
        }

        builder.HasIndex(e => e.EftBatchId);
    }
}

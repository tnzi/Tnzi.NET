namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 收付款单配置
/// </summary>
public class PaymentEntryConfiguration : EntityTypeConfigurationBase<PaymentEntry, Guid>
{
    public override void Configure(EntityTypeBuilder<PaymentEntry> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Number).HasMaxLength(64);
        builder.Property(e => e.Memo).HasMaxLength(500);
        builder.Property(e => e.Reference).HasMaxLength(128);
        builder.Property(e => e.Currency).HasMaxLength(8).IsRequired();
        builder.Property(e => e.SourceType).HasMaxLength(64);
        builder.Property(e => e.SourceId).HasMaxLength(64);
        builder.Property(e => e.ExchangeRate).HasExchangeRatePrecision();
        builder.Property(e => e.Amount).HasMoneyPrecision();
        builder.Property(e => e.BaseAmount).HasMoneyPrecision();
        builder.Property(e => e.AppliedTotal).HasMoneyPrecision();

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Number"));
            // 外部摄取幂等：同租户同来源只允许一张收付款单
            builder.HasIndex(e => new { e.TenantId, e.SourceType, e.SourceId }).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("SourceId"));
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => e.Number).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Number"));
            builder.HasIndex(e => new { e.SourceType, e.SourceId }).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("SourceId"));
        }

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.DocDate);
        builder.HasIndex(e => new { e.PartyType, e.PartyId });
    }
}

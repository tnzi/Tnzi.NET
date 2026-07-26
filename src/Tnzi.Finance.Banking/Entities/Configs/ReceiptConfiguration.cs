namespace Tnzi.Finance.Banking.Entities.Configs;

/// <summary>
/// 收据采集配置
/// </summary>
public class ReceiptConfiguration : EntityTypeConfigurationBase<Receipt, Guid>
{
    public override void Configure(EntityTypeBuilder<Receipt> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.OriginalFileName).HasMaxLength(256);
        builder.Property(e => e.VendorName).HasMaxLength(256);
        builder.Property(e => e.Currency).HasMaxLength(8);
        builder.Property(e => e.Reference).HasMaxLength(128);
        builder.Property(e => e.ConvertedDocType).HasMaxLength(32);
        builder.Property(e => e.FailReason).HasMaxLength(512);
        builder.Property(e => e.Subtotal).HasMoneyPrecision();
        builder.Property(e => e.TaxAmount).HasMoneyPrecision();
        builder.Property(e => e.Total).HasMoneyPrecision();
        builder.Property(e => e.Confidence).HasExchangeRatePrecision();

        builder.HasIndex(e => e.Status);
        if (multiTenancyEnabled)
            builder.HasIndex(e => e.TenantId);
    }
}

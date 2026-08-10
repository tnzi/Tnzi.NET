namespace Tnzi.Finance.Banking.Entities.Configs;

/// <summary>
/// 收据采集配置
/// </summary>
public class ReceiptConfiguration : EntityTypeConfigurationBase<Receipt, Guid>
{
    public override void Configure(EntityTypeBuilder<Receipt> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        // 列宽常量与 ReceiptFieldLimits 共用：提取结果与人工输入都要按它们收敛，
        // 两处各写一份数字迟早漂移，而漂移的症状是插入时 500 而不是编译失败。
        builder.Property(e => e.OriginalFileName).HasMaxLength(ReceiptFieldLimits.FileNameMaxLength);
        builder.Property(e => e.VendorName).HasMaxLength(ReceiptFieldLimits.VendorNameMaxLength);
        builder.Property(e => e.Currency).HasMaxLength(ReceiptFieldLimits.CurrencyMaxLength);
        builder.Property(e => e.Reference).HasMaxLength(ReceiptFieldLimits.ReferenceMaxLength);
        builder.Property(e => e.ConvertedDocType).HasMaxLength(ReceiptFieldLimits.ConvertedDocTypeMaxLength);
        builder.Property(e => e.FailReason).HasMaxLength(ReceiptFieldLimits.FailReasonMaxLength);
        builder.Property(e => e.Subtotal).HasMoneyPrecision();
        builder.Property(e => e.TaxAmount).HasMoneyPrecision();
        builder.Property(e => e.Total).HasMoneyPrecision();
        builder.Property(e => e.Confidence).HasExchangeRatePrecision();

        builder.HasIndex(e => e.Status);
        if (multiTenancyEnabled)
            builder.HasIndex(e => e.TenantId);
    }
}

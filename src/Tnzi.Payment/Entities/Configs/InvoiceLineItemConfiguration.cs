
namespace Tnzi.Payment.Entities.Configs;

/// <summary>
/// 发票明细配置
/// </summary>
public class InvoiceLineItemConfiguration : EntityTypeConfigurationBase<InvoiceLineItem, Guid>
{
    /// <summary>
    /// 配置实体
    /// </summary>
    public override void Configure(EntityTypeBuilder<InvoiceLineItem> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(l => l.Description).HasMaxLength(500).IsRequired();
        builder.Property(l => l.ProductCode).HasMaxLength(64);
        builder.Property(l => l.Quantity).HasQuantityPrecision();
        builder.Property(l => l.UnitPrice).HasMoneyPrecision();
        builder.Property(l => l.Amount).HasMoneyPrecision();
        builder.Property(l => l.DiscountAmount).HasMoneyPrecision();
        builder.Property(l => l.TaxRate).HasRatePrecision();
        builder.Property(l => l.TaxAmount).HasMoneyPrecision();

        if (multiTenancyEnabled)
        {
            builder.HasIndex(l => l.TenantId);
        }

        builder.HasIndex(l => l.InvoiceId);
    }
}

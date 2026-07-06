namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 销售发票行配置
/// </summary>
public class InvoiceLineConfiguration : EntityTypeConfigurationBase<InvoiceLine, Guid>
{
    public override void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.Property(l => l.Description).HasMaxLength(500);
        builder.Property(l => l.Quantity).HasQuantityPrecision();
        builder.Property(l => l.UnitPrice).HasMoneyPrecision();
        builder.Property(l => l.Amount).HasMoneyPrecision();

        builder.HasIndex(l => l.InvoiceId);
        builder.HasIndex(l => l.ItemId);
    }
}

namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 采购账单行配置
/// </summary>
public class BillLineConfiguration : EntityTypeConfigurationBase<BillLine, Guid>
{
    public override void Configure(EntityTypeBuilder<BillLine> builder)
    {
        builder.Property(l => l.Description).HasMaxLength(500);
        builder.Property(l => l.Quantity).HasQuantityPrecision();
        builder.Property(l => l.UnitPrice).HasMoneyPrecision();
        builder.Property(l => l.Amount).HasMoneyPrecision();
        builder.Property(l => l.TaxAmount).HasMoneyPrecision();

        builder.HasIndex(l => l.BillId);
        builder.HasIndex(l => l.ItemId);
    }
}

namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 采购订单行配置
/// </summary>
public class PurchaseOrderLineConfiguration : EntityTypeConfigurationBase<PurchaseOrderLine, Guid>
{
    public override void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Quantity).HasQuantityPrecision();
        builder.Property(e => e.UnitPrice).HasMoneyPrecision();
        builder.Property(e => e.Amount).HasMoneyPrecision();

        builder.HasIndex(e => e.PurchaseOrderId);
    }
}

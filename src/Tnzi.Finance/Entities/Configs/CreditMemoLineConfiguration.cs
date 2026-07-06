namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 销售贷项单行配置
/// </summary>
public class CreditMemoLineConfiguration : EntityTypeConfigurationBase<CreditMemoLine, Guid>
{
    public override void Configure(EntityTypeBuilder<CreditMemoLine> builder)
    {
        builder.Property(l => l.Description).HasMaxLength(500);
        builder.Property(l => l.Quantity).HasQuantityPrecision();
        builder.Property(l => l.UnitPrice).HasMoneyPrecision();
        builder.Property(l => l.Amount).HasMoneyPrecision();

        builder.HasIndex(l => l.CreditMemoId);
        builder.HasIndex(l => l.ItemId);
    }
}

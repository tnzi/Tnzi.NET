namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 报价单行配置
/// </summary>
public class EstimateLineConfiguration : EntityTypeConfigurationBase<EstimateLine, Guid>
{
    public override void Configure(EntityTypeBuilder<EstimateLine> builder)
    {
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Quantity).HasQuantityPrecision();
        builder.Property(e => e.UnitPrice).HasMoneyPrecision();
        builder.Property(e => e.Amount).HasMoneyPrecision();

        builder.HasIndex(e => e.EstimateId);
    }
}

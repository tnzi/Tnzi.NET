namespace Tnzi.Finance.Recurring.Entities.Configs;

/// <summary>
/// 周期性单据模板行配置
/// </summary>
public class RecurringLineConfiguration : EntityTypeConfigurationBase<RecurringLine, Guid>
{
    public override void Configure(EntityTypeBuilder<RecurringLine> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Quantity).HasQuantityPrecision();
        builder.Property(e => e.UnitPrice).HasMoneyPrecision();

        if (multiTenancyEnabled)
            builder.HasIndex(e => e.TenantId);

        builder.HasIndex(e => e.RecurringDocumentId);
    }
}

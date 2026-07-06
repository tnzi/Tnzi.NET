namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 核销记录配置
/// </summary>
public class PaymentApplicationConfiguration : EntityTypeConfigurationBase<PaymentApplication, Guid>
{
    public override void Configure(EntityTypeBuilder<PaymentApplication> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(a => a.AppliedAmount).HasMoneyPrecision();

        builder.HasIndex(a => new { a.SourceType, a.SourceId });
        builder.HasIndex(a => new { a.TargetType, a.TargetId });

        if (multiTenancyEnabled)
            builder.HasIndex(a => a.TenantId);
    }
}

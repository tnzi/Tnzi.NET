namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 税率配置
/// </summary>
public class TaxRateConfiguration : EntityTypeConfigurationBase<TaxRate, Guid>
{
    public override void Configure(EntityTypeBuilder<TaxRate> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(r => r.Name).HasMaxLength(128).IsRequired();
        builder.Property(r => r.Rate).HasRatePrecision();

        builder.HasOne(r => r.Agency)
            .WithMany()
            .HasForeignKey(r => r.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.AgencyId);
        builder.HasIndex(r => r.IsActive);

        if (multiTenancyEnabled)
            builder.HasIndex(r => r.TenantId);
    }
}

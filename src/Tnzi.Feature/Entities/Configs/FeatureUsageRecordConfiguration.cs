namespace Tnzi.Feature.Entities.Configs;

/// <summary>
/// FeatureUsageRecord entity configuration
/// </summary>
public class FeatureUsageRecordConfiguration : EntityTypeConfigurationBase<FeatureUsageRecord, long>
{
    public override void Configure(EntityTypeBuilder<FeatureUsageRecord> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.FeatureName).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Source).HasMaxLength(50);

        // Composite index for querying usage by feature and time range
        builder.HasIndex(e => new { e.FeatureName, e.CreationTime });

        // Composite index for tenant-scoped feature queries
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.FeatureName });
        }
    }
}

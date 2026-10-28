namespace Tnzi.Feature.Entities.Configs;

/// <summary>
/// FeatureValue entity configuration
/// </summary>
public class FeatureValueConfiguration : EntityTypeConfigurationBase<FeatureValue, Guid>
{
    public override void Configure(EntityTypeBuilder<FeatureValue> builder)
    {
        builder.Property(e => e.FeatureDefinitionId).IsRequired();
        builder.Property(e => e.ProviderName).IsRequired().HasMaxLength(FeatureFieldLimits.ProviderKey);
        builder.Property(e => e.ProviderKey).HasMaxLength(FeatureFieldLimits.ProviderKey);
        builder.Property(e => e.Value).IsRequired().HasMaxLength(FeatureFieldLimits.Value);

        // Unique index: one value per definition + provider + key
        builder.HasIndex(e => new { e.FeatureDefinitionId, e.ProviderName, e.ProviderKey }).IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        // Index on ProviderName + ProviderKey for query by provider
        builder.HasIndex(e => new { e.ProviderName, e.ProviderKey });
    }
}

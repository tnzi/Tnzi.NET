namespace Tnzi.Feature.Entities.Configs;

/// <summary>
/// FeatureDefinition entity configuration
/// </summary>
public class FeatureDefinitionConfiguration : EntityTypeConfigurationBase<FeatureDefinition, Guid>
{
    public override void Configure(EntityTypeBuilder<FeatureDefinition> builder)
    {
        builder.Property(e => e.Name).IsRequired().HasMaxLength(FeatureFieldLimits.Name);
        builder.Property(e => e.DisplayName).HasMaxLength(FeatureFieldLimits.DisplayName);
        builder.Property(e => e.Description).HasMaxLength(FeatureFieldLimits.Description);
        builder.Property(e => e.DefaultValue).HasMaxLength(FeatureFieldLimits.Value);
        builder.Property(e => e.ValueType).IsRequired();
        builder.Property(e => e.ParentName).HasMaxLength(FeatureFieldLimits.Name);
        builder.Property(e => e.Group).HasMaxLength(FeatureFieldLimits.Group);

        // Unique index on Name with soft delete filter
        builder.HasIndex(e => e.Name).IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        // Index on Group for query performance
        builder.HasIndex(e => e.Group);

        // Configure relationship with FeatureValue
        builder.HasMany(e => e.Values)
            .WithOne(e => e.FeatureDefinition)
            .HasForeignKey(e => e.FeatureDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

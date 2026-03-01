namespace Tnzi.Feature.Entities.Configs;

/// <summary>
/// FeatureDefinition entity configuration
/// </summary>
public class FeatureDefinitionConfiguration : EntityTypeConfigurationBase<FeatureDefinition, Guid>
{
    public override void Configure(EntityTypeBuilder<FeatureDefinition> builder)
    {
        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.Property(e => e.DisplayName).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.DefaultValue).HasMaxLength(200);
        builder.Property(e => e.ValueType).IsRequired();
        builder.Property(e => e.ParentName).HasMaxLength(100);
        builder.Property(e => e.Group).HasMaxLength(50);

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

namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// SubAgentType 实体配置类
/// </summary>
public class SubAgentTypeConfiguration : EntityTypeConfigurationBase<SubAgentType, Guid>
{
    public override void Configure(EntityTypeBuilder<SubAgentType> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.ToolGroupsJson)
            .HasMaxLength(2000);

        builder.Property(e => e.ExcludedToolGroupsJson)
            .HasMaxLength(2000);

        builder.Property(e => e.Instructions)
            .HasMaxLength(4000);

        builder.Property(e => e.DefaultModel)
            .HasMaxLength(200);

        builder.Property(e => e.CapabilityTagsJson)
            .HasMaxLength(1000);

        // 唯一索引：(TenantId, Name)
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.Name })
                .IsUnique();
        }
        else
        {
            builder.HasIndex(e => e.Name)
                .IsUnique();
        }

        builder.HasIndex(e => e.IsEnabled);
    }
}

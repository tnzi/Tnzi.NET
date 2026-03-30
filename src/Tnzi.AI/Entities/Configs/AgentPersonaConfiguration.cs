namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// AgentPersona 实体配置类
/// </summary>
public class AgentPersonaConfiguration : EntityTypeConfigurationBase<AgentPersona, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentPersona> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Content)
            .IsRequired()
            .HasMaxLength(32000);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        // 唯一索引：(TenantId, Slug)
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.Slug })
                .IsUnique();
        }
        else
        {
            builder.HasIndex(e => e.Slug)
                .IsUnique();
        }

        builder.HasIndex(e => e.IsSystem);
    }
}

namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// AgentVersion 实体配置类
/// </summary>
public class AgentVersionConfiguration : EntityTypeConfigurationBase<AgentVersion, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentVersion> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.AgentId)
            .IsRequired();

        builder.Property(e => e.Version)
            .IsRequired();

        builder.Property(e => e.ChangeNote)
            .HasMaxLength(500);

        builder.HasIndex(e => new { e.AgentId, e.Version })
            .IsUnique();

        builder.HasIndex(e => e.AgentId);
    }
}

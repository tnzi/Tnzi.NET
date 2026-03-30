namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// AgentArtifact 实体配置类
/// </summary>
public class AgentArtifactConfiguration : EntityTypeConfigurationBase<AgentArtifact, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentArtifact> builder)
    {
        builder.Property(e => e.VirtualPath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(e => e.FileName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.ContentType)
            .HasMaxLength(200);

        builder.HasIndex(e => e.ThreadId);
        builder.HasIndex(e => e.RunId);

        // 同一线程内虚拟路径唯一
        builder.HasIndex(e => new { e.ThreadId, e.VirtualPath })
            .IsUnique();
    }
}

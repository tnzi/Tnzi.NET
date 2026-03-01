namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// MemoryEntry 实体配置类
/// </summary>
public class MemoryEntryConfiguration : EntityTypeConfigurationBase<MemoryEntry, Guid>
{
    public override void Configure(EntityTypeBuilder<MemoryEntry> builder)
    {
        builder.Property(e => e.Scope)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Content)
            .IsRequired();

        builder.Property(e => e.Source)
            .HasMaxLength(100);

        // EmbeddingVector: JSON 序列化存储，可空（向后兼容）
        var provider = GetDatabaseProviderOrDefault();
        if (provider == DatabaseProvider.PostgreSQL)
        {
            builder.Property(e => e.EmbeddingVector).HasColumnType("jsonb").IsRequired(false);
        }
        else
        {
            builder.Property(e => e.EmbeddingVector).IsRequired(false);
        }

        builder.HasIndex(e => e.Scope);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.AgentId);
        builder.HasIndex(e => new { e.Scope, e.UserId });
    }
}

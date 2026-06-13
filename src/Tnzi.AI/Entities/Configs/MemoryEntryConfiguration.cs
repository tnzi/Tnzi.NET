namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// MemoryEntry 实体配置类
/// </summary>
public class MemoryEntryConfiguration : EntityTypeConfigurationBase<MemoryEntry, Guid>
{
    public override void Configure(EntityTypeBuilder<MemoryEntry> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Scope)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Content)
            .IsRequired();

        builder.Property(e => e.ContentHash)
            .HasMaxLength(64);

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

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        // 精确重复硬防线：同一 scope 下相同内容只允许一行。
        // Scope 键已编码 user/agent/session 隔离维度（MemoryScope.ToScopeKey）；
        // 多租户开启时把 TenantId 纳入索引列，避免跨租户的同 scope 同内容互相冲突。
        // 过滤 ContentHash 非空（既有行无哈希、不参与约束）。
        // 注意：MemoryEntry 非软删除实体（硬删语义），过滤器不得引用 IsDeleted（EntityPolicyTests B1）。
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.Scope, e.ContentHash })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNull(nameof(MemoryEntry.ContentHash)));
        }
        else
        {
            builder.HasIndex(e => new { e.Scope, e.ContentHash })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNull(nameof(MemoryEntry.ContentHash)));
        }

        builder.HasIndex(e => e.Scope);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.AgentId);
        builder.HasIndex(e => new { e.Scope, e.UserId });

        builder.Property(e => e.Importance)
            .HasDefaultValue(0.5);

        builder.Property(e => e.AccessCount)
            .HasDefaultValue(0);

        builder.Property(e => e.Category)
            .HasMaxLength(50);

        builder.HasIndex(e => new { e.Scope, e.Importance }).IsDescending(false, true);
        builder.HasIndex(e => new { e.Scope, e.Category });
    }
}

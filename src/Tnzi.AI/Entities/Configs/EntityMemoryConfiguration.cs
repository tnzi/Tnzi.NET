namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// EntityMemory 实体配置类
/// </summary>
public class EntityMemoryConfiguration : EntityTypeConfigurationBase<EntityMemory, Guid>
{
    public override void Configure(EntityTypeBuilder<EntityMemory> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.EntityName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.EntityType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Properties)
            .IsRequired();

        // 按 (EntityName, UserId, AgentId) 唯一索引（多租户下按 TenantId 分区）
        // 注意: EntityMemory 继承 MultiTenantAuditedEntity，无 ISoftDelete，不使用 IsDeletedFalse 过滤器
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.EntityName, e.UserId, e.AgentId })
                .IsUnique();
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => new { e.EntityName, e.UserId, e.AgentId })
                .IsUnique();
        }

        // 按 (UserId, LastMentioned) 降序索引，用于快速检索最近提及的实体
        builder.HasIndex(e => new { e.UserId, e.LastMentioned });

        builder.HasIndex(e => e.AgentId);
    }
}

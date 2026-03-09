namespace Tnzi.AI.Domain.Configs;

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

        // 按 (EntityName, UserId) 唯一索引，带软删除过滤（多租户下按 TenantId 分区）
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.EntityName, e.UserId })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(e => e.TenantId)
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }
        else
        {
            builder.HasIndex(e => new { e.EntityName, e.UserId })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        // 按 (UserId, LastMentioned) 降序索引，用于快速检索最近提及的实体
        builder.HasIndex(e => new { e.UserId, e.LastMentioned });

        builder.HasIndex(e => e.AgentId);
    }
}

namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// ToolPermissionRuleEntity 实体配置类
/// </summary>
public class ToolPermissionRuleEntityConfiguration : EntityTypeConfigurationBase<ToolPermissionRuleEntity, Guid>
{
    public override void Configure(EntityTypeBuilder<ToolPermissionRuleEntity> builder)
    {
        builder.Property(e => e.ToolPattern)
            .HasMaxLength(200);

        builder.Property(e => e.ToolGroup)
            .HasMaxLength(200);

        builder.Property(e => e.CommandPrefix)
            .HasMaxLength(500);

        builder.Property(e => e.ServerName)
            .HasMaxLength(200);

        builder.Property(e => e.PathPrefix)
            .HasMaxLength(500);

        builder.Property(e => e.Reason)
            .HasMaxLength(500);

        // 索引：(TenantId, Scope, IsEnabled) 用于高效查询
        builder.HasIndex(e => new { e.TenantId, e.Scope, e.IsEnabled });
    }
}

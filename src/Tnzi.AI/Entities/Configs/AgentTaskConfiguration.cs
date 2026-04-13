namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// AgentTask 实体配置类
/// </summary>
public class AgentTaskConfiguration : EntityTypeConfigurationBase<AgentTask, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentTask> builder)
    {
        builder.Property(e => e.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Result)
            .HasMaxLength(2000);

        // 按运行 + 排序序号查询
        builder.HasIndex(e => new { e.RunId, e.OrderIndex });

        // 按租户 + 状态查询
        builder.HasIndex(e => new { e.TenantId, e.Status });
    }
}

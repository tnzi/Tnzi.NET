namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// McpToolUsageRecord 实体配置类
/// </summary>
public class McpToolUsageRecordConfiguration : EntityTypeConfigurationBase<McpToolUsageRecord, long>
{
    public override void Configure(EntityTypeBuilder<McpToolUsageRecord> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.ToolName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(e => e.CallerApiKeyId)
            .HasMaxLength(100);

        // 按工具名+时间查询（统计分析最常用索引）
        builder.HasIndex(e => new { e.ToolName, e.CreationTime });

        // 多租户按工具名查询
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.ToolName });
        }
    }
}

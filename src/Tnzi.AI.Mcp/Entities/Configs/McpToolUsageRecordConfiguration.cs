namespace Tnzi.AI.Mcp.Entities.Configs;

/// <summary>
/// McpToolUsageRecord 实体配置类
/// </summary>
public class McpToolUsageRecordConfiguration : EntityTypeConfigurationBase<McpToolUsageRecord, long>
{
    public override void Configure(EntityTypeBuilder<McpToolUsageRecord> builder)
    {
        builder.Property(e => e.ToolName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(e => e.CallerApiKeyId)
            .HasMaxLength(100);

        // 按工具名+时间查询（统计分析最常用索引）
        builder.HasIndex(e => new { e.ToolName, e.CreationTime });

        // TenantId 是分析维度字段（非隔离边界，实体不实现 IMultiTenant）：
        // 值来自不可信的 X-Tenant-Id 请求头，仅用于统计分组查询，故建普通索引、
        // 不挂租户过滤器，也不随多租户开关增减列。
        builder.HasIndex(e => new { e.TenantId, e.ToolName });
    }
}

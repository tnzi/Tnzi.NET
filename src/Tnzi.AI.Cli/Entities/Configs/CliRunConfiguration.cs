namespace Tnzi.AI.Cli.Entities.Configs;

/// <summary>
/// 外部执行记录（兼任务队列）配置。
/// </summary>
public class CliRunConfiguration : EntityTypeConfigurationBase<CliRun, Guid>
{
    /// <inheritdoc />
    public override void Configure(EntityTypeBuilder<CliRun> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.ProviderSessionId).HasMaxLength(200);
        builder.Property(e => e.WorkDirectory).HasMaxLength(1000);
        builder.Property(e => e.ClaimedByHostId).HasMaxLength(200);
        builder.Property(e => e.EstimatedCostUsd).HasPrecision(18, 6);
        builder.Property(e => e.WriteBackTokenHash).HasMaxLength(128);

        // 回写凭据校验是按哈希查找的热路径（每次 MCP 请求一次）。
        builder.HasIndex(e => e.WriteBackTokenHash);

        // 认领扫描的热路径：先按状态过滤，再按优先级/创建时间排序。
        // 索引列顺序与查询排序一致，避免每次轮询都做一次全表排序。
        builder.HasIndex(e => new { e.Status, e.Priority, e.CreationTime });

        // 租约回收扫描：找 LeaseExpiresAt 已过期且仍处于占用态的行。
        builder.HasIndex(e => e.LeaseExpiresAt);

        builder.HasIndex(e => e.AgentId);
        builder.HasIndex(e => e.ThreadId);
        builder.HasIndex(e => e.CliRuntimeId);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }
    }
}

namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// AgentRun 实体配置
/// </summary>
public class AgentRunConfiguration : EntityTypeConfigurationBase<AgentRun, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.InputSummary)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(e => e.OutputSummary)
            .HasMaxLength(2000);

        builder.Property(e => e.WorkflowExecutionId)
            .HasMaxLength(64);

        builder.Property(e => e.ParentRunId);

        builder.Property(e => e.RootRunId);

        builder.Property(e => e.Error)
            .HasMaxLength(4000);

        builder.Property(e => e.Status)
            .HasDefaultValue(AgentRunStatus.Pending);

        builder.Property(e => e.ExecutionMode)
            .HasDefaultValue(AgentExecutionMode.Single);

        // === 引用完整性（FK）===
        // AgentRun 是审计记录：绝不能随 Agent/Thread 被级联删除。两列均可空。
        // - AgentId → Agent：SET NULL（与 AgentThread.AgentId 的既有行为一致）。Agent 是软删除实体，
        //   常规删除路径不触发 FK 动作；仅在物理删除 Agent 时置空引用、保留运行记录本身。
        // - ThreadId → AgentThread：RESTRICT（NO ACTION）而非 SET NULL。原因：
        //   Agent→AgentThread(SetNull) + Agent→AgentRun(SetNull) + AgentThread→AgentRun 三边构成菱形，
        //   若第三边也用 SET NULL，SQL Server 会以 "multiple cascade paths"(错误 1785) 拒绝建表
        //   （其静态分析对 SET NULL 链路同样保守计数）。RESTRICT 同时也是更强的审计保证：
        //   不允许在仍有运行记录引用时物理删除 Thread。仓库内 Thread 删除均为软删除 +
        //   ThreadCleanupHandler 先行清理 Run，因此该约束在现有路径下不会被触发。
        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(e => e.AgentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<AgentThread>()
            .WithMany()
            .HasForeignKey(e => e.ThreadId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.AgentId)
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        builder.HasIndex(e => e.ThreadId)
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        builder.HasIndex(e => e.WorkflowDefinitionId)
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        builder.HasIndex(e => e.WorkflowExecutionId)
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        builder.HasIndex(e => e.ParentRunId)
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        builder.HasIndex(e => e.RootRunId)
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        builder.HasIndex(e => e.Status)
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        builder.HasIndex(e => e.CreationTime)
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId)
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasMany(e => e.Nodes)
            .WithOne(n => n.Run)
            .HasForeignKey(n => n.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

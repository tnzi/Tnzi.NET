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

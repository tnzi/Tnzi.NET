namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// WorkflowExecution 实体配置类
/// </summary>
public class WorkflowExecutionConfiguration : EntityTypeConfigurationBase<WorkflowExecution, Guid>
{
    public override void Configure(EntityTypeBuilder<WorkflowExecution> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.ExecutionId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasDefaultValue(WorkflowExecutionStatus.Running);

        builder.Property(e => e.InitialInput)
            .IsRequired();

        builder.Property(e => e.CompletedSteps)
            .IsRequired();

        builder.Property(e => e.StepOutputs)
            .IsRequired();

        builder.Property(e => e.UpdatedTime)
            .IsRequired();

        builder.Property(e => e.StepsAwaitingApproval)
            .IsRequired();

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.ExecutionId }).IsUnique();
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => e.ExecutionId).IsUnique();
        }

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.WorkflowDefinitionId);
    }
}

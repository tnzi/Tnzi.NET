namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// WorkflowExecution 实体配置类
/// </summary>
public class WorkflowExecutionConfiguration : EntityTypeConfigurationBase<WorkflowExecution, Guid>
{
    public override void Configure(EntityTypeBuilder<WorkflowExecution> builder)
    {
        builder.Property(e => e.ExecutionId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.InitialInput)
            .IsRequired();

        builder.Property(e => e.CompletedSteps)
            .IsRequired();

        builder.Property(e => e.StepOutputs)
            .IsRequired();

        builder.HasIndex(e => e.ExecutionId)
            .IsUnique();

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.WorkflowDefinitionId);
    }
}

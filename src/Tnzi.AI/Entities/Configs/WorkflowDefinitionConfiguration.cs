namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// WorkflowDefinition 实体配置类
/// </summary>
public class WorkflowDefinitionConfiguration : EntityTypeConfigurationBase<WorkflowDefinition, Guid>
{
    public override void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.ExecutionMode)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasDefaultValue(WorkflowExecutionMode.Sequential);

        builder.HasIndex(e => e.Name);
        builder.HasIndex(e => e.IsEnabled);
    }
}

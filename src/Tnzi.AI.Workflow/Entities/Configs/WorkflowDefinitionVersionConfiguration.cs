namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// WorkflowDefinitionVersion 实体配置类
/// </summary>
public class WorkflowDefinitionVersionConfiguration : EntityTypeConfigurationBase<WorkflowDefinitionVersion, Guid>
{
    public override void Configure(EntityTypeBuilder<WorkflowDefinitionVersion> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.WorkflowDefinitionId)
            .IsRequired();

        builder.Property(e => e.VersionNumber)
            .IsRequired();

        builder.Property(e => e.Definition)
            .IsRequired();

        builder.Property(e => e.ChangeDescription)
            .HasMaxLength(500);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        builder.HasIndex(e => new { e.WorkflowDefinitionId, e.VersionNumber })
            .IsUnique();

        builder.HasIndex(e => e.WorkflowDefinitionId);
    }
}

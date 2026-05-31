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
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasDefaultValue(WorkflowExecutionStatus.Running);

        builder.Property(e => e.InitialInput)
            .IsRequired();

        builder.Property(e => e.CompletedSteps)
            .IsRequired();

        builder.Property(e => e.StepOutputs)
            .IsRequired();

        builder.Property(e => e.UpdatedTime)
            .IsRequired();

        builder.Property(e => e.CurrentWaitReason)
            .HasMaxLength(100);

        builder.Property(e => e.PendingSignalsJson)
            .IsRequired();

        builder.Property(e => e.StepsAwaitingApproval)
            .IsRequired();

        // ConcurrencyStamp 的 IsConcurrencyToken + HasMaxLength(32) 由框架全局约定
        // EntityRegistrationHelper.ApplyConcurrencyStampConfiguration 对所有 IConcurrencyStamp
        // 实体统一配置（长度匹配 Guid.NewGuid().ToString("N") = 32 chars），值由
        // AuditPropertyHelper 在 SaveChanges 时自动维护，此处无需重复配置（避免误导性的长度漂移）。

        builder.HasIndex(e => new { e.WorkflowDefinitionId, e.Status });

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

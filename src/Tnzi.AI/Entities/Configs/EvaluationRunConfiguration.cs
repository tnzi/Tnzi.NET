namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// EvaluationRun 实体配置类
/// </summary>
public class EvaluationRunConfiguration : EntityTypeConfigurationBase<EvaluationRun, Guid>
{
    public override void Configure(EntityTypeBuilder<EvaluationRun> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Status)
            .IsRequired()
            .HasDefaultValue(EvaluationRunStatus.Running);

        builder.Property(e => e.ResultsJson)
            .IsRequired();

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        builder.HasIndex(e => e.AgentId);
    }
}

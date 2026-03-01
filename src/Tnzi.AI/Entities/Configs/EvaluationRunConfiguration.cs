namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// EvaluationRun 实体配置类
/// </summary>
public class EvaluationRunConfiguration : EntityTypeConfigurationBase<EvaluationRun, Guid>
{
    public override void Configure(EntityTypeBuilder<EvaluationRun> builder)
    {
        builder.Property(e => e.Status)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.ResultsJson)
            .IsRequired();

        builder.HasIndex(e => e.AgentId);
    }
}

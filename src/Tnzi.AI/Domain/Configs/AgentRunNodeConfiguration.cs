namespace Tnzi.AI.Domain.Configs;

/// <summary>
/// AgentRunNode 实体配置
/// </summary>
public class AgentRunNodeConfiguration : EntityTypeConfigurationBase<AgentRunNode, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentRunNode> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.NodeType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.NodeName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.InputSummary)
            .HasMaxLength(2000);

        builder.Property(e => e.Output)
            .HasMaxLength(32000);

        builder.Property(e => e.Error)
            .HasMaxLength(4000);

        builder.Property(e => e.Status)
            .HasDefaultValue(AgentRunNodeStatus.Pending);

        builder.HasIndex(e => e.RunId);
        builder.HasIndex(e => e.Status);
    }
}

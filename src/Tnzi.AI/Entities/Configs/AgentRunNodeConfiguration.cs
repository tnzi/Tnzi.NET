namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// AgentRunNode 实体配置
/// </summary>
public class AgentRunNodeConfiguration : EntityTypeConfigurationBase<AgentRunNode, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentRunNode> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.NodeType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.NodeName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.NodeKey)
            .HasMaxLength(200);

        builder.Property(e => e.InputSummary)
            .HasMaxLength(2000);

        builder.Property(e => e.Output)
            .HasMaxLength(32000);

        builder.Property(e => e.Error)
            .HasMaxLength(4000);

        builder.Property(e => e.AwaitingInputKind)
            .HasMaxLength(100);

        builder.Property(e => e.Status)
            .HasDefaultValue(AgentRunNodeStatus.Pending);

        builder.HasIndex(e => new { e.RunId, e.Status });
        builder.HasIndex(e => new { e.RunId, e.NodeKey });
        builder.HasIndex(e => new { e.RunId, e.ParentNodeId });

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }
    }
}

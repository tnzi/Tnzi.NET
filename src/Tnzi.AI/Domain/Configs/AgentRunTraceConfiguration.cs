namespace Tnzi.AI.Domain.Configs;

/// <summary>
/// AgentRunTrace 实体配置
/// </summary>
public class AgentRunTraceConfiguration : EntityTypeConfigurationBase<AgentRunTrace, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentRunTrace> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(e => e.RunId);
        builder.HasIndex(e => e.NodeId);
        builder.HasIndex(e => e.EventType);

        builder.HasOne(e => e.Run)
            .WithMany()
            .HasForeignKey(e => e.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

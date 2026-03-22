namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// AgentRunTrace 实体配置
/// </summary>
public class AgentRunTraceConfiguration : EntityTypeConfigurationBase<AgentRunTrace, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentRunTrace> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.EventType)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(e => new { e.RunId, e.EventType });
        builder.HasIndex(e => e.NodeId);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        builder.HasOne(e => e.Run)
            .WithMany()
            .HasForeignKey(e => e.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

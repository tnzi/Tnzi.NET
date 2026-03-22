namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// AgentThreadMessage 实体配置类
/// </summary>
public class AgentThreadMessageConfiguration : EntityTypeConfigurationBase<AgentThreadMessage, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentThreadMessage> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Role)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.Content)
            .IsRequired();

        builder.Property(e => e.ToolCalls);

        builder.Property(e => e.Usage)
            .HasMaxLength(500);

        builder.HasOne(e => e.Thread)
            .WithMany(t => t.Messages)
            .HasForeignKey(e => e.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        builder.Property(e => e.FeedbackTags)
            .HasMaxLength(500);

        builder.Property(e => e.FeedbackComment)
            .HasMaxLength(2000);

        builder.HasIndex(e => e.ThreadId);
        builder.HasIndex(e => new { e.ThreadId, e.Order }).IsUnique();
    }
}

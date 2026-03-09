namespace Tnzi.AI.Domain.Configs;

/// <summary>
/// AgentThread 实体配置类
/// </summary>
public class AgentThreadConfiguration : EntityTypeConfigurationBase<AgentThread, Guid>
{
    public override void Configure(EntityTypeBuilder<AgentThread> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title)
            .HasMaxLength(200);

        builder.HasOne(e => e.Agent)
            .WithMany()
            .HasForeignKey(e => e.AgentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        builder.HasIndex(e => e.AgentId);
        builder.HasIndex(e => e.LastActivityTime);
    }
}

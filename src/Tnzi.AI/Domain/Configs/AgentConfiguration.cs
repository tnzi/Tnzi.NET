namespace Tnzi.AI.Domain.Configs;

/// <summary>
/// Agent 实体配置类
/// </summary>
public class AgentConfiguration : EntityTypeConfigurationBase<Agent, Guid>
{
    public override void Configure(EntityTypeBuilder<Agent> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.Provider)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Model)
            .HasMaxLength(100);

        builder.Property(e => e.Instructions)
            .HasMaxLength(32000);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId)
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.Property(e => e.ExecutionMode)
            .HasDefaultValue(AgentExecutionMode.Single);

        builder.HasIndex(e => e.Name)
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        builder.HasIndex(e => e.IsEnabled);

        // v2.1 能力标签字段
        builder.Property(e => e.Domains)
            .HasMaxLength(2000);

        builder.Property(e => e.Roles)
            .HasMaxLength(2000);

        builder.Property(e => e.QualityTier)
            .HasDefaultValue(3);

        builder.Property(e => e.LatencyTier)
            .HasDefaultValue(3);

        builder.Property(e => e.CostTier)
            .HasDefaultValue(3);
    }
}

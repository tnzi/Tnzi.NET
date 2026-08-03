namespace Tnzi.AI.Cli.Entities.Configs;

/// <summary>
/// Agent → 外部运行时绑定配置。
/// </summary>
public class CliAgentBindingConfiguration : EntityTypeConfigurationBase<CliAgentBinding, Guid>
{
    /// <inheritdoc />
    public override void Configure(EntityTypeBuilder<CliAgentBinding> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Model).HasMaxLength(200);
        builder.Property(e => e.ThinkingLevel).HasMaxLength(64);
        builder.Property(e => e.UserWorkDirectory).HasMaxLength(1000);

        // 一个 Agent 至多一个外部绑定 —— 「走哪个运行时」不能有两个答案。
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.AgentId })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => e.AgentId)
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasIndex(e => e.CliRuntimeId);
    }
}

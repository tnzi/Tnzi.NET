namespace Tnzi.Authorization.Entities.Configs;

/// <summary>
/// <see cref="DualControlRequest"/> 实体配置类
/// </summary>
public class DualControlRequestConfiguration : EntityTypeConfigurationBase<DualControlRequest, Guid>
{
    public override void Configure(EntityTypeBuilder<DualControlRequest> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Operation).IsRequired().HasMaxLength(128);
        builder.Property(e => e.TargetId).HasMaxLength(128);
        builder.Property(e => e.Description).HasMaxLength(512);
        builder.Property(e => e.DecisionComment).HasMaxLength(512);

        // 待办列表按状态取，是这张表最热的查询。
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.Status, e.ExpiresAt });
        }
        else
        {
            builder.HasIndex(e => new { e.Status, e.ExpiresAt });
        }

        // 「这条单据有没有在等人批」按动作 + 目标查。
        builder.HasIndex(e => new { e.Operation, e.TargetId });
        builder.HasIndex(e => e.RequesterId);
    }
}

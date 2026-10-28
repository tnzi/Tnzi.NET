namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// <see cref="AiUsagePolicy"/> 的配置。
/// </summary>
/// <remarks>
/// 按用户的行由过滤唯一索引兜底（<c>UserId IS NOT NULL</c>）；租户默认那一行（<c>UserId</c> 为 null）没法跨库用唯一索引约束
/// （PostgreSQL 与 SQLite 视 NULL 互不相等），改由确定的主键兜底：每个租户的默认行用同一个由租户算出来的 Id，
/// 两次并发插入撞主键，见 <c>UsagePolicyService.DefaultPolicyId</c>。
/// </remarks>
public class AiUsagePolicyConfiguration : EntityTypeConfigurationBase<AiUsagePolicy, Guid>
{
    public override void Configure(EntityTypeBuilder<AiUsagePolicy> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.SessionLimitUsd).HasPrecision(18, 6);
        builder.Property(e => e.WeeklyLimitUsd).HasPrecision(18, 6);
        builder.Property(e => e.DefaultModel).HasMaxLength(200);
        builder.Property(e => e.Note).HasMaxLength(512);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.UserId })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNull(nameof(AiUsagePolicy.UserId)));
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => e.UserId)
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNull(nameof(AiUsagePolicy.UserId)));
        }
    }
}

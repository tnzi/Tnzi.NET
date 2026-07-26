namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// UserQuota 实体配置类
/// </summary>
/// <remarks>
/// 注意：避免使用数据库级别的 SQL 函数默认值，以确保跨数据库兼容性和迁移稳定性。
/// </remarks>
public class UserQuotaConfiguration : EntityTypeConfigurationBase<UserQuota, Guid>
{
    public override void Configure(EntityTypeBuilder<UserQuota> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.UserId)
            .IsRequired();

        builder.Property(e => e.DailyTokenLimit)
            .IsRequired()
            .HasDefaultValue(100000); // 默认每日 10 万 Token

        builder.Property(e => e.MonthlyTokenLimit)
            .IsRequired()
            .HasDefaultValue(3000000); // 默认每月 300 万 Token

        builder.Property(e => e.CurrentDailyUsage)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.CurrentMonthlyUsage)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.LastResetDate)
            .IsRequired();

        builder.Property(e => e.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.WarningThreshold)
            .IsRequired()
            .HasDefaultValue(0.8m)
            .HasPrecision(3, 2);

        builder.Property(e => e.CriticalThreshold)
            .IsRequired()
            .HasDefaultValue(0.95m)
            .HasPrecision(3, 2);

        // 乐观并发令牌
        builder.Property(e => e.Version)
            .IsConcurrencyToken();

        // 为 UserId 创建索引（查询优化，多租户下按 TenantId 分区）
        // ★ 过滤器不可省：UserQuota 是软删实体（MultiTenantAuditedEntity），软删行仍占着
        //   数据库唯一约束，而全局查询过滤器让 GetOrCreateQuotaAsync 的查重看不见它 →
        //   一旦将来补上配额删除功能，"查不到就 Insert" 会直接撞约束。
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.UserId })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => e.UserId)
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }
    }
}

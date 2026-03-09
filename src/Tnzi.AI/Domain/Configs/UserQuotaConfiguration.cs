namespace Tnzi.AI.Domain.Configs;

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

        // 乐观并发令牌
        builder.Property(e => e.Version)
            .IsConcurrencyToken();

        // 为 UserId 创建索引（查询优化，多租户下按 TenantId 分区）
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.UserId }).IsUnique();
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => e.UserId).IsUnique();
        }
    }
}

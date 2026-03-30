namespace Tnzi.Notification.Entities.Configs;

/// <summary>
/// NotificationPreference 实体配置类
/// </summary>
public class NotificationPreferenceConfiguration : EntityTypeConfigurationBase<NotificationPreference, Guid>
{
    public override void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(p => p.Channel).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Category).HasMaxLength(100);
        builder.Property(p => p.IsEnabled).HasDefaultValue(true);

        // 唯一约束：每个用户的每个渠道+分类只能有一条偏好记录
        builder.HasIndex(p => new { p.UserId, p.Channel, p.Category })
            .IsUnique();

        // 查询索引
        builder.HasIndex(p => p.UserId);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(p => p.TenantId);
        }
    }
}

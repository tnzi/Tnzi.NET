namespace Tnzi.Notification.Entities.Configs;

/// <summary>
/// Preference 实体配置类
/// </summary>
public class PreferenceConfiguration : EntityTypeConfigurationBase<Preference, Guid>
{
    public override void Configure(EntityTypeBuilder<Preference> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(p => p.Channel).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Category).HasMaxLength(100);
        builder.Property(p => p.IsEnabled).HasDefaultValue(true);

        // 唯一约束：每个用户的每个渠道+分类只能有一条偏好记录
        // Category 为 NULL = 该渠道的默认偏好（不针对任何分类）。拆两条的理由同
        // OptOutConfiguration：PostgreSQL / SQLite 的唯一索引里 NULL 互不相等，
        // 单条索引挡不住同一用户同一渠道被写入两条默认偏好 —— 而读取只会拿到其中
        // 一条，于是"通知开关有时是开的有时是关的"，且两次都看不出哪里错了。
        builder.HasIndex(p => new { p.UserId, p.Channel, p.Category }).IsUnique()
            .HasFilter(IndexFilterFactory.GetColumnNotNull("Category"));
        builder.HasIndex(p => new { p.UserId, p.Channel }).IsUnique()
            .HasFilter(IndexFilterFactory.GetColumnNull("Category"))
            .HasDatabaseName("IX_Notification_Preference_UserId_Channel_Default");

        // 查询索引
        builder.HasIndex(p => p.UserId);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(p => p.TenantId);
        }
    }
}

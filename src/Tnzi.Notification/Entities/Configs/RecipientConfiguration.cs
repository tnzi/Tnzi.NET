namespace Tnzi.Notification.Entities.Configs;

/// <summary>
/// Recipient 实体配置类
/// </summary>
public class RecipientConfiguration : EntityTypeConfigurationBase<Recipient, Guid>
{
    public override void Configure(EntityTypeBuilder<Recipient> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(r => r.Address).IsRequired().HasMaxLength(500);
        builder.Property(r => r.Name).HasMaxLength(200);
        // 列宽常量与 NotificationFieldLimits 共用：投递结果落库前按同一组数字收敛，
        // 两处分别写死会让「收敛到多长」和「列能装多长」悄悄漂开。
        builder.Property(r => r.ExternalMessageId).HasMaxLength(NotificationFieldLimits.ExternalMessageIdMaxLength);
        builder.Property(r => r.FailureReason).HasMaxLength(NotificationFieldLimits.FailureReasonMaxLength);

        // 配置与 Message 的关系
        builder.HasOne(r => r.Message)
            .WithMany(n => n.Recipients)
            .HasForeignKey(r => r.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // 创建索引
        if (multiTenancyEnabled)
        {
            builder.HasIndex(r => r.TenantId);
        }

        builder.HasIndex(r => r.MessageId);
        builder.HasIndex(r => r.Address);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.UserId);
        builder.HasIndex(r => new { r.UserId, r.IsRead });
    }
}

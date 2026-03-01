namespace Tnzi.Notification.Entities.Configs;

/// <summary>
/// Message 实体配置类
/// </summary>
public class MessageConfiguration : EntityTypeConfigurationBase<Message, Guid>
{
    public override void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.Property(n => n.Subject).IsRequired().HasMaxLength(500);
        builder.Property(n => n.Content);
        builder.Property(n => n.FailureReason).HasMaxLength(1000);
        builder.Property(n => n.Category).IsRequired().HasMaxLength(100).HasDefaultValue("General");
        builder.Property(n => n.TemplateName).HasMaxLength(200);
        builder.Property(n => n.TotalRecipientCount).HasDefaultValue(0);
        builder.Property(n => n.SuccessCount).HasDefaultValue(0);
        builder.Property(n => n.FailureCount).HasDefaultValue(0);
        builder.Property(n => n.RetryCount).HasDefaultValue(0);
        builder.Property(n => n.MaxRetryCount).HasDefaultValue(3);

        // 创建索引（优化查询性能）
        builder.HasIndex(n => n.Type);
        builder.HasIndex(n => n.Status);
        builder.HasIndex(n => n.CreationTime);
        builder.HasIndex(n => n.Category);
        builder.HasIndex(n => n.SenderId);
        // 复合索引：常用于按类型和状态查询，并按创建时间排序
        builder.HasIndex(n => new { n.Type, n.Status, n.CreationTime });
        // 复合索引：用于按发送者查询
        builder.HasIndex(n => new { n.SenderId, n.CreationTime });
    }
}

namespace Tnzi.Notification.Entities.Configs;

/// <summary>
/// Recipient 实体配置类
/// </summary>
public class RecipientConfiguration : EntityTypeConfigurationBase<Recipient, Guid>
{
    public override void Configure(EntityTypeBuilder<Recipient> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Address).IsRequired().HasMaxLength(500);
        builder.Property(r => r.Name).HasMaxLength(200);
        builder.Property(r => r.ExternalMessageId).HasMaxLength(200);
        builder.Property(r => r.FailureReason).HasMaxLength(1000);

        // 配置与 Message 的关系
        builder.HasOne(r => r.Message)
            .WithMany(n => n.Recipients)
            .HasForeignKey(r => r.MessageId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        // 创建索引
        builder.HasIndex(r => r.MessageId);
        builder.HasIndex(r => r.Address);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.UserId);
        builder.HasIndex(r => new { r.UserId, r.IsRead });
    }
}

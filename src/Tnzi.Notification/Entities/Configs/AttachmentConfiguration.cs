namespace Tnzi.Notification.Entities.Configs;

/// <summary>
/// Attachment 实体配置类
/// </summary>
public class AttachmentConfiguration : EntityTypeConfigurationBase<Attachment, Guid>
{
    public override void Configure(EntityTypeBuilder<Attachment> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(a => a.FileName).IsRequired().HasMaxLength(500);
        builder.Property(a => a.FilePath).IsRequired().HasMaxLength(1000);
        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(200).HasDefaultValue("application/octet-stream");

        // 配置与 Message 的关系
        builder.HasOne(a => a.Message)
            .WithMany(n => n.Attachments)
            .HasForeignKey(a => a.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // 创建索引
        if (multiTenancyEnabled)
        {
            builder.HasIndex(a => a.TenantId);
        }

        builder.HasIndex(a => a.MessageId);
        builder.HasIndex(a => a.FileId);
    }
}


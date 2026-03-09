namespace Tnzi.Storage.Entities.Configs;

/// <summary>
/// FileUploadSession 实体配置类
/// </summary>
public class FileUploadSessionConfiguration : EntityTypeConfigurationBase<FileUploadSession, Guid>
{
    public override void Configure(EntityTypeBuilder<FileUploadSession> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.ToTable("UploadSession");

        builder.Property(e => e.FileName).IsRequired().HasMaxLength(256);
        builder.Property(e => e.TotalSize).IsRequired();
        builder.Property(e => e.ChunkSize).IsRequired();
        builder.Property(e => e.TotalChunks).IsRequired();
        builder.Property(e => e.UploadedChunks).HasDefaultValue(0);
        builder.Property(e => e.UploadedSize).HasDefaultValue(0);
        builder.Property(e => e.Md5Hash).HasMaxLength(64);
        builder.Property(e => e.IsCompleted).HasDefaultValue(false);
        builder.Property(e => e.IsCancelled).HasDefaultValue(false);
        builder.Property(e => e.CreationTime).IsRequired();
        builder.Property(e => e.ExpiresAt).IsRequired();

        // 创建索引
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        builder.HasIndex(e => e.CreationTime);
        builder.HasIndex(e => e.ExpiresAt);
        builder.HasIndex(e => e.IsCompleted);
        builder.HasIndex(e => e.CreatorId);
    }
}

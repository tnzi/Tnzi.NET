namespace Tnzi.Storage.Entities.Configs;

/// <summary>
/// FileShare 实体配置类
/// </summary>
public class FileShareConfiguration : EntityTypeConfigurationBase<FileShare, Guid>
{
    public override void Configure(EntityTypeBuilder<FileShare> builder)
    {
        builder.ToTable("Share");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FileId).IsRequired();
        builder.Property(e => e.ShareToken).IsRequired().HasMaxLength(64);
        builder.Property(e => e.PasswordHash).HasMaxLength(256);
        builder.Property(e => e.IsEnabled).HasDefaultValue(true);
        builder.Property(e => e.CreationTime).IsRequired();

        // 创建索引
        builder.HasIndex(e => e.FileId);
        builder.HasIndex(e => e.ShareToken).IsUnique();
        builder.HasIndex(e => e.IsEnabled);
        builder.HasIndex(e => e.ExpiresAt);
        builder.HasIndex(e => e.CreationTime);
    }
}

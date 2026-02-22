namespace Tnzi.Storage.Entities.Configs;

/// <summary>
/// FileVersion 实体配置类
/// </summary>
public class FileVersionConfiguration : EntityTypeConfigurationBase<FileVersion, Guid>
{
    public override void Configure(EntityTypeBuilder<FileVersion> builder)
    {
        builder.ToTable("Version");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FileId).IsRequired();
        builder.Property(e => e.Version).IsRequired();
        builder.Property(e => e.Path).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Md5Hash).HasMaxLength(64);
        builder.Property(e => e.Description).HasMaxLength(512);
        builder.Property(e => e.IsCurrent).HasDefaultValue(false);
        builder.Property(e => e.CreationTime).IsRequired();

        // 创建索引
        builder.HasIndex(e => e.FileId);
        builder.HasIndex(e => new { e.FileId, e.Version }).IsUnique();
        builder.HasIndex(e => e.IsCurrent);
        builder.HasIndex(e => e.CreatorId);
        builder.HasIndex(e => e.CreationTime);
    }
}

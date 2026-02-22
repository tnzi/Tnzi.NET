namespace Tnzi.Storage.Entities.Configs;

/// <summary>
/// FileRecord 实体配置类
/// </summary>
public class FileRecordConfiguration : EntityTypeConfigurationBase<FileRecord, Guid>
{
    public override void Configure(EntityTypeBuilder<FileRecord> builder)
    {
        // 表名显式指定为 "Record" 以保持与现有数据库架构兼容；否则由 TableNamePrefix 生成 Storage_FileRecord
        builder.ToTable("Record");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FileName).IsRequired().HasMaxLength(256);
        builder.Property(e => e.OriginalName).HasMaxLength(256);
        builder.Property(e => e.Extension).HasMaxLength(32);
        builder.Property(e => e.ContentType).HasMaxLength(128);
        builder.Property(e => e.Path).HasMaxLength(256);
        builder.Property(e => e.Md5Hash).HasMaxLength(64);
        builder.Property(e => e.Provider).HasMaxLength(50).HasDefaultValue("Local");
        builder.Property(e => e.ThumbnailPath).HasMaxLength(256);
        builder.Property(e => e.ReferenceCount).HasDefaultValue(1);
        builder.Property(e => e.IsTemporary).HasDefaultFalse();

        // 创建索引
        builder.HasIndex(e => e.Md5Hash);
        builder.HasIndex(e => e.CreatorId);
        builder.HasIndex(e => e.CreationTime);
        builder.HasIndex(e => e.Provider);
    }
}


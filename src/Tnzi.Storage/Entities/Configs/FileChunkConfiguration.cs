namespace Tnzi.Storage.Entities.Configs;

/// <summary>
/// FileChunk 实体配置类
/// </summary>
public class FileChunkConfiguration : EntityTypeConfigurationBase<FileChunk, Guid>
{
    public override void Configure(EntityTypeBuilder<FileChunk> builder)
    {
        builder.ToTable("Chunk");

        builder.Property(e => e.UploadSessionId).IsRequired();
        builder.Property(e => e.ChunkIndex).IsRequired();
        builder.Property(e => e.ChunkSize).IsRequired();
        builder.Property(e => e.ChunkPath).HasMaxLength(512);
        builder.Property(e => e.Md5Hash).HasMaxLength(64);
        builder.Property(e => e.CreationTime).IsRequired();

        // 创建索引
        builder.HasIndex(e => e.UploadSessionId);
        builder.HasIndex(e => new { e.UploadSessionId, e.ChunkIndex }).IsUnique();
        builder.HasIndex(e => e.CreationTime);
    }
}

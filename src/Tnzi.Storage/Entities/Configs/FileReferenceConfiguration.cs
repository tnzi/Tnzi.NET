namespace Tnzi.Storage.Entities.Configs;

/// <summary>
/// FileReference 实体配置类
/// </summary>
public class FileReferenceConfiguration : EntityTypeConfigurationBase<FileReference, Guid>
{
    public override void Configure(EntityTypeBuilder<FileReference> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.ToTable("Reference");

        builder.Property(e => e.FileId).IsRequired();
        builder.Property(e => e.EntityType).IsRequired().HasMaxLength(128);
        builder.Property(e => e.EntityId).IsRequired();
        builder.Property(e => e.FieldName).IsRequired().HasMaxLength(128);
        builder.Property(e => e.IsTemporary).HasDefaultValue(true);
        builder.Property(e => e.CreationTime).IsRequired();

        // 创建索引
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        builder.HasIndex(e => e.FileId);
        builder.HasIndex(e => new { e.EntityType, e.EntityId, e.FieldName });
        builder.HasIndex(e => e.IsTemporary);
        builder.HasIndex(e => e.CreationTime);

        // 唯一约束：同一个 (FileId, EntityType, EntityId, FieldName) 只能有一条引用记录。
        // 这是引用去重的数据库层兜底——配合 FileReferenceProcessor / FileReferenceService 的应用层查重，
        // 防止双轨写入产生重复引用行与 ReferenceCount 虚高（导致文件永远清不掉）。
        // 多文件字段(如 Attachments)同一字段引用多个文件时 FileId 不同，故不受影响。
        // FileReference 为硬删除(无 ISoftDelete)，无需软删除过滤。
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.FileId, e.EntityType, e.EntityId, e.FieldName }).IsUnique();
        }
        else
        {
            builder.HasIndex(e => new { e.FileId, e.EntityType, e.EntityId, e.FieldName }).IsUnique();
        }
    }
}


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

        // 唯一约束：同一个实体的同一个字段只能引用一个文件（非临时引用）
        // 注意：临时引用可以有多个，但正式引用应该唯一
        // 这里不设置唯一约束，因为允许临时引用存在多个
    }
}


namespace Tnzi.Audit.Entities.Configs;

/// <summary>
/// AuditPropertyEntry 实体配置类
/// </summary>
public class AuditPropertyEntryConfiguration : EntityTypeConfigurationBase<AuditPropertyEntry, Guid>
{
    public override void Configure(EntityTypeBuilder<AuditPropertyEntry> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理，此处显式指定以保持与原有表名一致
        builder.ToTable("PropertyEntry");

        builder.Property(e => e.PropertyName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.PropertyDisplayName).HasMaxLength(200);
        builder.Property(e => e.PropertyTypeName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.OriginalValue);
        builder.Property(e => e.NewValue);

        builder.HasOne(e => e.AuditEntityEntry)
            .WithMany(e => e.PropertyEntries)
            .HasForeignKey(e => e.AuditEntityEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        // 创建索引
        builder.HasIndex(e => e.AuditEntityEntryId);
        builder.HasIndex(e => new { e.AuditEntityEntryId, e.PropertyName });
    }
}

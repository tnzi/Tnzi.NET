namespace Tnzi.Audit.Entities.Configs;

/// <summary>
/// AuditEntityEntry 实体配置类
/// </summary>
public class AuditEntityEntryConfiguration : EntityTypeConfigurationBase<AuditEntityEntry, Guid>
{
    public override void Configure(EntityTypeBuilder<AuditEntityEntry> builder)
    {
        builder.ToTable("EntityEntry");

        builder.Property(e => e.EntityTypeName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.EntityTypeFullName).IsRequired().HasMaxLength(500);
        builder.Property(e => e.EntityId).HasMaxLength(100);

        // 配置与 AuditOperation 的关系
        builder.HasOne(e => e.AuditOperation)
            .WithMany(e => e.EntityEntries)
            .HasForeignKey(e => e.AuditOperationId)
            .OnDelete(DeleteBehavior.Cascade);

        // 创建索引
        builder.HasIndex(e => e.AuditOperationId);
        builder.HasIndex(e => e.EntityTypeFullName);
        builder.HasIndex(e => new { e.AuditOperationId, e.EntityTypeFullName });
        builder.HasIndex(e => e.CreationTime);
    }
}

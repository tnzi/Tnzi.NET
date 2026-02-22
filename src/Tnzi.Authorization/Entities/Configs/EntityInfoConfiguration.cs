namespace Tnzi.Authorization.Entities.Configs;

/// <summary>
/// EntityInfo 实体配置类
/// </summary>
public class EntityInfoConfiguration : EntityTypeConfigurationBase<EntityInfo, Guid>
{
    public override void Configure(EntityTypeBuilder<EntityInfo> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.TypeName).IsRequired().HasMaxLength(500);
        builder.Property(e => e.DisplayName).HasMaxLength(200);

        // 配置与EntityRole的关系
        builder.HasMany(e => e.EntityRoles)
            .WithOne(e => e.EntityInfo)
            .HasForeignKey(e => e.EntityInfoId)
            .OnDelete(DeleteBehavior.Cascade);

        // 创建索引
        builder.HasIndex(e => e.TypeName).IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
    }
}


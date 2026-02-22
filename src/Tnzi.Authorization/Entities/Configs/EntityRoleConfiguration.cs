namespace Tnzi.Authorization.Entities.Configs;

/// <summary>
/// EntityRole 实体配置类
/// </summary>
public class EntityRoleConfiguration : EntityTypeConfigurationBase<EntityRole, Guid>
{
    public override void Configure(EntityTypeBuilder<EntityRole> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Filter).HasMaxLength(2000);

        // 创建索引
        builder.HasIndex(e => new { e.EntityInfoId, e.RoleId, e.Operation });
        builder.HasIndex(e => e.RoleId);
    }
}


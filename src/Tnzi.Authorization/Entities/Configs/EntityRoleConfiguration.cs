namespace Tnzi.Authorization.Entities.Configs;

/// <summary>
/// EntityRole 实体配置类
/// </summary>
public class EntityRoleConfiguration : EntityTypeConfigurationBase<EntityRole, Guid>
{
    public override void Configure(EntityTypeBuilder<EntityRole> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Filter).HasMaxLength(2000);

        // 创建索引
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.EntityInfoId, e.RoleId, e.Operation });
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => new { e.EntityInfoId, e.RoleId, e.Operation });
        }

        builder.HasIndex(e => e.RoleId);
    }
}


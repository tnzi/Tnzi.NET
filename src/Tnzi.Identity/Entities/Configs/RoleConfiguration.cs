namespace Tnzi.Identity.Entities.Configs;

/// <summary>
/// Role 实体配置类
/// </summary>
public class RoleConfiguration : EntityTypeConfigurationBase<Role, Guid>
{
    public override void Configure(EntityTypeBuilder<Role> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        if (multiTenancyEnabled)
        {
            // 多租户模式：取消默认单列唯一，改为租户内唯一
            builder.HasIndex(r => r.NormalizedName)
                .HasDatabaseName("RoleNameIndex")
                .IsUnique(false);

            builder.HasIndex(r => new { r.TenantId, r.NormalizedName })
                .HasDatabaseName("RoleTenantNameIndex")
                .IsUnique();

            builder.HasIndex(r => r.TenantId);
            return;
        }

        // 单租户模式保持原有唯一约束
        builder.HasIndex(r => r.NormalizedName)
            .HasDatabaseName("RoleNameIndex")
            .IsUnique();
    }
}

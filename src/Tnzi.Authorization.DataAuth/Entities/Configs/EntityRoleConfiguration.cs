namespace Tnzi.Authorization.DataAuth.Entities.Configs;

/// <summary>
/// EntityRole 实体配置类
/// </summary>
public class EntityRoleConfiguration : EntityTypeConfigurationBase<EntityRole, Guid>
{
    public override void Configure(EntityTypeBuilder<EntityRole> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Filter).HasMaxLength(EntityRole.FilterMaxLength);

        // ★ (实体, 角色, 操作) 在未删除的行里唯一。服务层的判重是 read-then-write，两个并发的创建都会读到
        //   「还没有」各插一行；而过滤器按角色把规则并起来，管理员删掉其中一条、另一条照样生效 ——
        //   页面上看已经收回的权限其实还在。最终把关的必须是数据库。
        //   软删的行不参与（删了再建同一条是正常操作），所以带 IsDeleted = false 的过滤条件。
        //   多租户时按租户划分；TenantId 为 NULL 的宿主行在 PostgreSQL / SQLite 上不被含 TenantId 的索引
        //   互相约束（唯一索引里 NULL 互不相等），所以另有一条只管宿主行的索引。
        var notDeleted = IndexFilterFactory.GetIsDeletedFalse();
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.EntityInfoId, e.RoleId, e.Operation }).IsUnique()
                .HasFilter($"{IndexFilterFactory.GetColumnNotNull("TenantId")} AND {notDeleted}")
                .HasDatabaseName("IX_Auth_EntityRole_Tenant_EntityInfo_Role_Operation");
            builder.HasIndex(e => new { e.EntityInfoId, e.RoleId, e.Operation }).IsUnique()
                .HasFilter($"{IndexFilterFactory.GetColumnNull("TenantId")} AND {notDeleted}")
                .HasDatabaseName("IX_Auth_EntityRole_Host_EntityInfo_Role_Operation");
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => new { e.EntityInfoId, e.RoleId, e.Operation }).IsUnique()
                .HasFilter(notDeleted);
        }

        builder.HasIndex(e => e.RoleId);
    }
}


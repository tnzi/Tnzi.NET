namespace Tnzi.Identity.Entities.Configs;

/// <summary>
/// User 实体配置类
/// </summary>
public class UserConfiguration : EntityTypeConfigurationBase<User, Guid>
{
    public override void Configure(EntityTypeBuilder<User> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        // 主键由 Identity 配置

        // 关系配置
        builder.HasOne(u => u.Organization)
            .WithMany()
            .HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.SetNull);

        // 索引配置
        builder.HasIndex(u => u.Email);
        builder.HasIndex(u => u.PhoneNumber);
        builder.HasIndex(u => u.OrganizationId);

        // ★ User 是软删实体（ISoftDelete）。软删只把行标记为已删，物理行仍在表里，而全局
        //   查询过滤器让 UserManager 的查重（UserValidator → FindByNameAsync）**看不见**
        //   那条幽灵行 → validator 放行 → INSERT → 撞数据库唯一约束 → 不透明 500。
        //   实际后果：删掉用户 alice 之后，永远无法再创建 alice。
        //   与 2026-07-22 AuthToken 那次 2FA 登录 500 同源（那次的结论原文写着"必然复发"）。
        //   下面两处唯一索引因此都必须带 IsDeleted 过滤器。
        //   守卫：tests/Tnzi.AspNetCore.Tests/Data/SoftDeleteUniqueIndexConventionTests.cs
        //   （注意该门禁只能看到当前分支注册的索引，多租户分支要靠这里的人工保证）。
        var isDeletedFilter = IndexFilterFactory.GetIsDeletedFalse();

        // 单租户模式沿用 ASP.NET Identity 默认的 NormalizedUserName 唯一索引，
        // 但补上过滤器 —— Identity 建的那个不带过滤器。
        builder.HasIndex(u => u.NormalizedUserName)
            .HasDatabaseName("UserNameIndex")
            .IsUnique()
            .HasFilter(isDeletedFilter);

        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;
        if (multiTenancyEnabled)
        {
            // 多租户模式下，不同租户可以拥有相同用户名：
            // 移除 ASP.NET Identity 默认的 NormalizedUserName 单列唯一索引，
            // 替换为 (TenantId, NormalizedUserName) 租户内唯一的复合索引。
            builder.HasIndex(u => u.NormalizedUserName)
                .HasDatabaseName("UserNameIndex")
                .IsUnique(false);

            builder.HasIndex(u => new { u.TenantId, u.NormalizedUserName })
                .HasDatabaseName("UserTenantNameIndex")
                .IsUnique()
                .HasFilter(isDeletedFilter);

            builder.HasIndex(u => u.TenantId);
        }
    }
}

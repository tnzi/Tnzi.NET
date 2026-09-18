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
        // ★ User → Organization 的外键**不在这里**声明：组织树住在可选包
        //   Tnzi.Identity.Organization 里，核心不引用它的实体。那条外键由该包的
        //   OrganizationConfiguration 从主体侧（HasMany<User>().WithOne()）声明，
        //   列、删除行为与 EF 算出来的约束名与拆分前逐字相同。
        //   下面这条 OrganizationId 索引**留在核心**：加载不加载那个包，User 表的列形状
        //   都要一模一样，否则「装了包的库」和「没装包的库」会长出两种 User 表。

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

        // 沿用 ASP.NET Identity 默认的 NormalizedUserName 唯一索引，但补上过滤器 —— Identity 建的那个不带过滤器。
        //
        // ★★ 多租户开启时用户名**仍然全局唯一**，这是 User 与 Role 的一处刻意不对称。
        //   User 不受租户过滤器管（登录时租户上下文尚未建立、按用户名找人必须跨租户；全局账号 TenantId = null），
        //   于是 UserManager 的 UserValidator 经无过滤的 FindByNameAsync 全局查重，第二个 admin 在哪个租户都建不出来；
        //   登录查找（AuthService.FindUserByLoginInputAsync）同样不带租户。此前这里在多租户分支把本索引改成非唯一、
        //   另建 (TenantId, NormalizedUserName) 复合唯一索引，文档据此写「允许不同租户存在相同用户名」——
        //   那条索引是死的（validator 先拦），而一旦有人绕过 UserManager 造出重名，登录就分不清找的是谁。
        //   三者（索引 / validator / 登录查找）必须一个口径：全局唯一。Role 受过滤器管，RoleValidator 按租户查重，
        //   所以角色名保持租户内唯一。对已开启多租户的库这是一次索引迁移（删 UserTenantNameIndex、UserNameIndex 复为唯一）。
        builder.HasIndex(u => u.NormalizedUserName)
            .HasDatabaseName("UserNameIndex")
            .IsUnique()
            .HasFilter(isDeletedFilter);

        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;
        if (multiTenancyEnabled)
        {
            // 按租户裁剪的管理端查询（UserTenantScope）靠它。
            builder.HasIndex(u => u.TenantId);
        }
    }
}

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
                .IsUnique();

            builder.HasIndex(u => u.TenantId);
        }
    }
}

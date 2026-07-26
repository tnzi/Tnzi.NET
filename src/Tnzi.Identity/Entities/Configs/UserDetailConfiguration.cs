namespace Tnzi.Identity.Entities.Configs;

/// <summary>
/// UserDetail 实体配置类
/// </summary>
public class UserDetailConfiguration : EntityTypeConfigurationBase<UserDetail, Guid>
{
    public override void Configure(EntityTypeBuilder<UserDetail> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(ud => ud.Id);

        // 属性配置
        builder.Property(ud => ud.Nickname).HasMaxLength(50);
        builder.Property(ud => ud.FirstName).HasMaxLength(50);
        builder.Property(ud => ud.LastName).HasMaxLength(50);
        builder.Property(ud => ud.AvatarUrl).HasMaxLength(500);
        builder.Property(ud => ud.Bio).HasMaxLength(500);
        builder.Property(ud => ud.Address).HasMaxLength(200);
        builder.Property(ud => ud.Website).HasMaxLength(200);

        // 关系配置
        builder.HasOne(ud => ud.User)
            .WithOne()
            .HasForeignKey<UserDetail>(ud => ud.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // 索引配置
        // ★ 过滤器不可省：UserDetail 是软删实体（FullAuditedEntity），软删只把行标记为
        //   已删、物理行仍在表里，而全局查询过滤器让 CreateOrUpdateAsync 的存在性查询
        //   **看不见**那条幽灵行 → 查不到就 Insert → 撞数据库唯一约束 → 不透明 500。
        //   这与 2026-07-22 AuthToken 那次 2FA 登录 500 是同一颗雷。
        builder.HasIndex(ud => ud.UserId)
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        if (multiTenancyEnabled)
        {
            builder.HasIndex(ud => ud.TenantId);
        }
    }
}

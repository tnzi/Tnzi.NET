namespace Tnzi.Identity.Entities.Configs;

/// <summary>
/// AuthToken 实体配置类
/// </summary>
public class AuthTokenConfiguration : EntityTypeConfigurationBase<AuthToken, Guid>
{
    public override void Configure(EntityTypeBuilder<AuthToken> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(ut => ut.Id);

        // 属性配置
        builder.Property(ut => ut.LoginProvider).IsRequired().HasMaxLength(128);
        builder.Property(ut => ut.Name).IsRequired().HasMaxLength(128);
        builder.Property(ut => ut.Value).IsRequired();

        // 关系配置
        builder.HasOne(ut => ut.User)
            .WithMany()
            .HasForeignKey(ut => ut.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        // 索引配置：同一用户的同一 Provider 和 Name 组合只能有一个有效 Token
        builder.HasIndex(ut => new { ut.UserId, ut.LoginProvider, ut.Name })
            .IsUnique();
    }
}

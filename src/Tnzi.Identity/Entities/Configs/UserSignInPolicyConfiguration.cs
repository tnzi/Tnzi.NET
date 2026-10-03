namespace Tnzi.Identity.Entities.Configs;

/// <summary>
/// <see cref="UserSignInPolicy"/> 实体配置。
/// </summary>
public class UserSignInPolicyConfiguration : EntityTypeConfigurationBase<UserSignInPolicy, Guid>
{
    public override void Configure(EntityTypeBuilder<UserSignInPolicy> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(p => p.Id);

        builder.Property(p => p.AllowedIps).HasMaxLength(UserSignInPolicy.AllowedIpsMaxLength);

        // 策略没有独立于账号的意义：账号删了它跟着走。
        builder.HasOne(p => p.User)
            .WithOne()
            .HasForeignKey<UserSignInPolicy>(p => p.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // 一人一行。登录守卫按这个索引查，未命中就是没有限制。
        builder.HasIndex(p => p.UserId).IsUnique();
    }
}

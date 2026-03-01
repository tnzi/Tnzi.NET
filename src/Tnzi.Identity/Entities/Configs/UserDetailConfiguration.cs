namespace Tnzi.Identity.Entities.Configs;

/// <summary>
/// UserDetail 实体配置类
/// </summary>
public class UserDetailConfiguration : EntityTypeConfigurationBase<UserDetail, Guid>
{
    public override void Configure(EntityTypeBuilder<UserDetail> builder)
    {
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
        builder.HasIndex(ud => ud.UserId).IsUnique();
    }
}

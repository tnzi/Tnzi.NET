namespace Tnzi.Identity.Entities.Configs;

/// <summary>
/// UserSession 实体配置类
/// </summary>
public class UserSessionConfiguration : EntityTypeConfigurationBase<UserSession, Guid>
{
    public override void Configure(EntityTypeBuilder<UserSession> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(us => us.Id);

        // 属性配置
        builder.Property(us => us.DeviceInfo).HasMaxLength(256);
        builder.Property(us => us.IpAddress).HasMaxLength(50);
        builder.Property(us => us.UserAgent).HasMaxLength(512);

        // 关系配置
        builder.HasOne(us => us.User)
            .WithMany()
            .HasForeignKey(us => us.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // 索引配置
        builder.HasIndex(us => new { us.UserId, us.IsRevoked, us.CreationTime });
        builder.HasIndex(us => us.LastActivityTime);
    }
}

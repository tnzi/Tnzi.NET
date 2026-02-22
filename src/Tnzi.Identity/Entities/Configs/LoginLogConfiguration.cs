namespace Tnzi.Identity.Entities.Configs;

/// <summary>
/// LoginLog 实体配置类
/// </summary>
public class LoginLogConfiguration : EntityTypeConfigurationBase<LoginLog, Guid>
{
    public override void Configure(EntityTypeBuilder<LoginLog> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(ll => ll.Id);

        // 属性配置
        builder.Property(ll => ll.UserName).HasMaxLength(100);
        builder.Property(ll => ll.IpAddress).HasMaxLength(50);
        builder.Property(ll => ll.UserAgent).HasMaxLength(500);
        builder.Property(ll => ll.FailureReason).HasMaxLength(500);
        builder.Property(ll => ll.Status).IsRequired();
        builder.Property(ll => ll.CreationTime).IsRequired();

        // 索引配置
        builder.HasIndex(ll => ll.UserId);
        builder.HasIndex(ll => ll.CreationTime);
        builder.HasIndex(ll => ll.Status);
    }
}

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
    }
}

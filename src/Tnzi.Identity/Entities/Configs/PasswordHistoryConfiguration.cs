namespace Tnzi.Identity.Entities.Configs;

/// <summary>
/// PasswordHistory 实体配置类
/// </summary>
public class PasswordHistoryConfiguration : EntityTypeConfigurationBase<PasswordHistory, Guid>
{
    public override void Configure(EntityTypeBuilder<PasswordHistory> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(ph => ph.Id);

        // 属性配置
        builder.Property(ph => ph.PasswordHash)
            .HasMaxLength(256)
            .IsRequired();
        builder.Property(ph => ph.CreationTime).IsRequired();

        // 关系配置
        builder.HasOne(ph => ph.User)
            .WithMany()
            .HasForeignKey(ph => ph.UserId)
            .IsRequired(false)  // 设置为可选，避免全局查询过滤器警告（当用户被软删除时）
            .OnDelete(DeleteBehavior.Cascade);

        // 索引配置
        builder.HasIndex(ph => new { ph.UserId, ph.CreationTime });
    }
}

namespace Tnzi.Identity.Entities.Configs;

/// <summary>
/// TwoFactorCode 实体配置类
/// </summary>
public class TwoFactorCodeConfiguration : EntityTypeConfigurationBase<TwoFactorCode, Guid>
{
    public override void Configure(EntityTypeBuilder<TwoFactorCode> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(tfc => tfc.Id);

        // 属性配置
        builder.Property(tfc => tfc.UserId)
            .IsRequired(false);
        builder.Property(tfc => tfc.Code)
            .HasMaxLength(10)
            .IsRequired();
        builder.Property(tfc => tfc.Address)
            .HasMaxLength(256)
            .IsRequired();

        // 关系配置（UserId 可空，支持验证码登录场景）
        builder.HasOne(tfc => tfc.User)
            .WithMany()
            .HasForeignKey(tfc => tfc.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        // 索引配置
        builder.HasIndex(tfc => new { tfc.UserId, tfc.Type, tfc.CreationTime });
        builder.HasIndex(tfc => new { tfc.Address, tfc.Type, tfc.CreationTime })
            .HasDatabaseName("IX_TwoFactorCode_Address_Type_CreationTime");
        builder.HasIndex(tfc => tfc.Code);
        builder.HasIndex(tfc => tfc.ExpiresAt);
    }
}

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
        // ★ Purpose 进这个索引而不是**另建**一个：加上用途之后，两类查询的谓词都以
        // (Address, Type, Purpose) 打头 —— 验码是 (… Code, IsUsed, ExpiresAt)，
        // 无缓存时的重发节流是 (… IsUsed) + 按 CreationTime 倒序。一个索引同时覆盖过滤与排序；
        // 再留一个前缀重叠的旧索引只会白付写入成本，且它已经不精确匹配任何查询了。
        builder.HasIndex(tfc => new { tfc.Address, tfc.Type, tfc.Purpose, tfc.CreationTime })
            .HasDatabaseName("IX_TwoFactorCode_Address_Type_Purpose_CreationTime");
        builder.HasIndex(tfc => tfc.Code);
        builder.HasIndex(tfc => tfc.ExpiresAt);
    }
}

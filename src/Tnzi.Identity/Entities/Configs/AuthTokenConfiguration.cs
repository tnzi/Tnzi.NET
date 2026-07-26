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
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // 索引配置：同一用户 + Provider + Name + 会话 组合唯一。
        // 加入 SessionId 后，刷新令牌可按会话各存一条（多设备各自独立刷新）；
        // 非会话绑定的令牌（SessionId=Guid.Empty，如 2FA 临时令牌）仍是每用户一行（upsert 语义不变）。
        // 注意：AuthToken 刻意非软删（AuditedEntity），删除即物理移除，故此处无需（也不应加）
        // IsDeleted=false 过滤器——不存在会占用唯一性的软删幽灵行。详见 AuthToken 实体注释。
        builder.HasIndex(ut => new { ut.UserId, ut.LoginProvider, ut.Name, ut.SessionId })
            .IsUnique();
    }
}

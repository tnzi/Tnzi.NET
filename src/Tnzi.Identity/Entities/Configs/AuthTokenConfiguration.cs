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
        // 列宽取 OneTimeToken.HashLength（SHA-256 的 64 个十六进制字符），不要凭记忆写 128/256。
        builder.Property(ut => ut.PreviousValueHash).HasMaxLength(OneTimeToken.HashLength);
        builder.Property(ut => ut.ValueHash).HasMaxLength(OneTimeToken.HashLength);

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

        // 重放检测的查找路径：拿着一枚查不到的刷新令牌，按上一代哈希再找一次。
        // 刻意**不加唯一约束** —— 两条不同会话的令牌理论上可以先后轮换出同一个上一代哈希吗？
        // 不能（值是 64 字节随机数），但唯一约束在这里没有任何收益，却会把一次哈希碰撞
        // （或一次数据修复）变成插入失败，让登录整个不可用。
        builder.HasIndex(ut => new { ut.LoginProvider, ut.Name, ut.PreviousValueHash });

        // ★ 按值查找的路径（刷新、2FA 临时令牌、邀请、passkey 注册令牌、step-up）。
        // 此前谓词打在 Value 上而 Value 无索引，只能靠 (LoginProvider, Name, …) 那个索引的
        // 前缀走一半；改存哈希之后这里是一次定宽列上的等值命中。
        builder.HasIndex(ut => new { ut.LoginProvider, ut.Name, ut.ValueHash });

        // 撤销会话时要按会话删光其上的令牌（ISessionRevocationService），单列索引给这条路径用。
        builder.HasIndex(ut => ut.SessionId);
    }
}

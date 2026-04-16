namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// McpServerRegistration 实体配置类
/// </summary>
/// <remarks>
/// AuthToken 字段以密文形式存储 (AuthTokenEncrypted)，加密由 McpServerRegistryService 通过
/// IDataProtectionProvider.CreateProtector("Tnzi.AI.Mcp.ServerCredential") 完成。
/// 请勿直接持久化明文凭证。
/// </remarks>
public class McpServerRegistrationConfiguration : EntityTypeConfigurationBase<McpServerRegistration, Guid>
{
    public override void Configure(EntityTypeBuilder<McpServerRegistration> builder)
    {
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.ServerUrl)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Transport)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Command)
            .HasMaxLength(500);

        // Arguments / Tags / AuthTokenEncrypted: 让 EF 自动选择列类型（保持跨数据库兼容）

        builder.Property(e => e.AuthType)
            .HasMaxLength(50);

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.Property(e => e.Priority)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        // 唯一索引：同名注册在未删除集合内只能存在一条
        builder.HasIndex(e => e.Name)
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        builder.HasIndex(e => e.Transport);
        builder.HasIndex(e => e.IsEnabled);
    }
}

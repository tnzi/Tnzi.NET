namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// McpServerRegistration 实体配置类
/// </summary>
/// <remarks>
/// AuthToken 字段以密文形式存储 (AuthTokenEncrypted)，加密由 McpServerRegistryService 通过
/// IDataProtectionProvider.CreateProtector(McpServerRegistration.AuthTokenProtectorPurpose) 完成。
/// 请勿直接持久化明文凭证。
/// </remarks>
public class McpServerRegistrationConfiguration : EntityTypeConfigurationBase<McpServerRegistration, Guid>
{
    public override void Configure(EntityTypeBuilder<McpServerRegistration> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.ServerUrl)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Transport)
            .IsRequired()
            .HasMaxLength(50);

        // Tags / AuthTokenEncrypted: 让 EF 自动选择列类型（保持跨数据库兼容）

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

        // 唯一索引：同名注册在未删除集合内只能存在一条。
        // 多租户开启时按 (TenantId, Name) 分区 → 同一个 MCP server 名字可在不同租户下各存一条。
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.Name })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }
        else
        {
            builder.HasIndex(e => e.Name)
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasIndex(e => e.Transport);
        builder.HasIndex(e => e.IsEnabled);
    }
}

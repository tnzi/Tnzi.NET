namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// Provider 实体配置类
/// </summary>
/// <remarks>
/// API Key 字段以密文形式存储 (ApiKeyEncrypted)，加密由 ProviderService 通过
/// IDataProtectionProvider.CreateProtector("Tnzi.AI.Providers.ApiKey") 完成。
/// 请勿直接持久化明文 ApiKey。
/// </remarks>
public class ProviderConfiguration : EntityTypeConfigurationBase<Provider, Guid>
{
    public override void Configure(EntityTypeBuilder<Provider> builder)
    {
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.ProviderType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Endpoint)
            .HasMaxLength(500);

        // ApiKeyEncrypted: 让 EF 自动选择列类型（SQL Server: nvarchar(max), PG: text 等）
        // 不指定 HasColumnType 以保持跨数据库兼容性

        builder.Property(e => e.DefaultModel)
            .HasMaxLength(100);

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.Property(e => e.Priority)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.Scope)
            .IsRequired();

        // 唯一索引：同一作用域 + 租户下同名 Provider 在未删除集合内只能存在一条
        builder.HasIndex(e => new { e.Name, e.Scope, e.TenantId })
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        builder.HasIndex(e => e.ProviderType);

        // 可见性查询索引 - 加速 Scope/TenantId 联合查询
        builder.HasIndex(e => new { e.Scope, e.TenantId });
    }
}

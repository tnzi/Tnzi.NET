namespace Tnzi.AI.Cli.Entities.Configs;

/// <summary>
/// 外部运行权限请求配置。
/// </summary>
public class CliRunPermissionRequestConfiguration : EntityTypeConfigurationBase<CliRunPermissionRequest, Guid>
{
    /// <inheritdoc />
    public override void Configure(EntityTypeBuilder<CliRunPermissionRequest> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.ProtocolKey).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Tool).HasMaxLength(200);
        builder.Property(e => e.OptionId).HasMaxLength(200);
        builder.Property(e => e.Message).HasMaxLength(2000);

        // 一条请求事件一行：重新附着时按 (RunId, Sequence) 认回停机前的那一行，重复插入撞在这里
        builder.HasIndex(e => new { e.RunId, e.Sequence }).IsUnique();

        // 执行器轮询「这次运行还有哪些没写给 CLI 的」
        builder.HasIndex(e => new { e.RunId, e.Status });

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }
    }
}

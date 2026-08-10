using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Tnzi.Audit.Entities.Configs;

/// <summary>
/// <see cref="AuditDataDestruction"/> 实体配置。
/// </summary>
/// <remarks>
/// <strong>条件映射</strong>：仅在 <c>Audit:DataDestruction:Enabled</c> 为真时才注册为数据表，
/// 与 <see cref="AuditRecordAccessConfiguration"/> 同一路子。
/// </remarks>
public class AuditDataDestructionConfiguration : EntityTypeConfigurationBase<AuditDataDestruction, Guid>
{
    private readonly bool? _forcedEnabled;

    /// <summary>
    /// 按配置 <c>Audit:DataDestruction:Enabled</c> 决定是否建表（自动发现走这个构造）。
    /// </summary>
    public AuditDataDestructionConfiguration()
    {
    }

    /// <summary>
    /// 显式指定是否建表，绕过配置读取。
    /// </summary>
    /// <param name="enabled">是否建表。</param>
    /// <remarks>
    /// 给两类场景用：测试里手工 <c>ApplyConfiguration</c>（拿不到应用服务提供程序），
    /// 以及消费应用希望用自己的开关而不是本模块配置来控制这张表。
    /// </remarks>
    public AuditDataDestructionConfiguration(bool enabled)
    {
        _forcedEnabled = enabled;
    }

    /// <inheritdoc />
    public override void Configure(EntityTypeBuilder<AuditDataDestruction> builder)
    {
        var dbContext = GetDbContext();

        if (!(_forcedEnabled ?? IsEnabled(dbContext)))
        {
            // 实体经 IEntityRegister 自动发现，无法在注册阶段跳过（RegisterTo 非虚）。
            // 改用 EF 的迁移排除：模型里仍有它（几乎无成本），但数据库里不会建这张表。
            builder.ToTable("DataDestruction", t => t.ExcludeFromMigrations());
            return;
        }

        var multiTenancyEnabled = (dbContext as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.ToTable("DataDestruction");

        builder.Property(e => e.PolicyName).IsRequired().HasMaxLength(128);
        builder.Property(e => e.EntityType).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Mode).IsRequired().HasMaxLength(64);
        builder.Property(e => e.EncryptionKeyId).HasMaxLength(128);

        // 十六进制 SHA-256 定长 64；首条的 PreviousHash 为空串。
        builder.Property(e => e.PreviousHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Hash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.IdentifierDigest).IsRequired().HasMaxLength(64);

        // 标识清单可能很长（一批默认上限 500 条），不设列长上限。
        builder.Property(e => e.Identifiers);

        // 全局单链的完整性由这条唯一索引保证：并发写入抢到同一序号时数据库拒绝其一。
        builder.HasIndex(e => e.Sequence).IsUnique();

        // 「这条策略最近销毁过什么」是本表最主要的查询方向。
        builder.HasIndex(e => new { e.PolicyName, e.CreationTime });

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.Ignore(e => e.TenantId);
        }
    }

    /// <summary>
    /// 读取 <c>Audit:DataDestruction:Enabled</c>。
    /// </summary>
    /// <remarks>
    /// 走 EF Core 官方途径拿应用服务提供程序。取不到配置时保守地视为<strong>未启用</strong>：
    /// 这项能力是显式开启的，取不到就说明没开（设计期迁移工具也走这条路）。
    /// </remarks>
    private static bool IsEnabled(DbContext? dbContext)
    {
        var appServices = dbContext?
            .GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?
            .ApplicationServiceProvider;

        var options = appServices?.GetService<IOptionsMonitor<DataDestructionOptions>>();
        return options?.CurrentValue.Enabled ?? false;
    }
}

using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Tnzi.Audit.Entities.Configs;

/// <summary>
/// <see cref="AuditRecordAccess"/> 实体配置。
/// </summary>
/// <remarks>
/// <strong>条件映射</strong>：仅在 <c>Audit:RecordAccess:Enabled</c> 为真时才注册为数据表。
/// 未启用该能力的应用不会多出一张空表，也不会因此产生迁移。
/// </remarks>
public class AuditRecordAccessConfiguration : EntityTypeConfigurationBase<AuditRecordAccess, Guid>
{
    private readonly bool? _forcedEnabled;

    /// <summary>
    /// 按配置 <c>Audit:RecordAccess:Enabled</c> 决定是否建表（自动发现走这个构造）。
    /// </summary>
    public AuditRecordAccessConfiguration()
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
    public AuditRecordAccessConfiguration(bool enabled)
    {
        _forcedEnabled = enabled;
    }

    /// <inheritdoc />
    public override void Configure(EntityTypeBuilder<AuditRecordAccess> builder)
    {
        var dbContext = GetDbContext();

        if (!(_forcedEnabled ?? IsEnabled(dbContext)))
        {
            // 实体经 IEntityRegister 自动发现，无法在注册阶段跳过（RegisterTo 非虚）。
            // 改用 EF 的迁移排除：模型里仍有它（几乎无成本），但**数据库里不会建这张表**，
            // 也不会为它生成任何迁移。未启用该能力的应用因此不多一张空表。
            builder.ToTable("RecordAccess", t => t.ExcludeFromMigrations());
            return;
        }

        var multiTenancyEnabled = (dbContext as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.ToTable("RecordAccess");

        builder.Property(e => e.ResourceType).IsRequired().HasMaxLength(256);
        builder.Property(e => e.ResourceId).IsRequired().HasMaxLength(128);
        builder.Property(e => e.Purpose).HasMaxLength(128);
        builder.Property(e => e.UserName).HasMaxLength(200);

        // 十六进制 SHA-256 定长 64；首条的 PreviousHash 为空串。
        builder.Property(e => e.PreviousHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Hash).IsRequired().HasMaxLength(64);

        // 哈希链的完整性由这条唯一索引保证：并发写入抢到同一序号时数据库拒绝其一，
        // 服务层重读链尾后重试。用唯一约束而不是分布式锁，是因为审计写入是高频路径。
        builder.HasIndex(e => new { e.UserId, e.Sequence }).IsUnique();

        // 「谁读过这条记录」是本表最主要的查询方向。
        builder.HasIndex(e => new { e.ResourceType, e.ResourceId });

        // 配额检查按用户 + 时间窗扫描。
        builder.HasIndex(e => new { e.UserId, e.CreationTime });

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
    /// 读取 <c>Audit:RecordAccess:Enabled</c>。
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

        var options = appServices?.GetService<IOptionsMonitor<RecordAccessAuditOptions>>();
        return options?.CurrentValue.Enabled ?? false;
    }
}
